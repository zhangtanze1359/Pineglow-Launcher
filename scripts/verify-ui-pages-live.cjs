#!/usr/bin/env node
/*
 * verify-ui-pages-live.cjs —— 真实窗口 + 九页界面的**运行时**验收（R06）。
 *
 * 为什么需要它：
 *   现有门槛只证明"HTTP 能把界面资源以正确 MIME 发出去"（verify-single-file-standalone.ps1）
 *   和"本地静态引用闭包完整"（check-ui-resource-closure.cjs）。
 *   它们都**不能**证明界面在真实 WebView2 里真的渲染出来了：
 *   JS 抛异常、路由切换失败、某页渲染成空白，都可能在资源全部 200 的情况下发生。
 *
 * 做法：把真实发布产物放进全新目录启动，用产品自带的 WebView2 远程调试端口
 * （PINEGLOW_WEBVIEW_DEBUG，见 WebShellWindow.OnEnvironmentRequested）连 CDP，
 *   1. 逐页点击导航（真实用户动作，走 .nav-item[data-nav] 的 click）；
 *   2. 断言每页容器存在、激活态正确、渲染出非平凡文本；
 *   3. 逐页截图存证；
 *   4. 收集未捕获异常 / console.error / **本机资源的网络失败**（外部服务失败另计，不冒充产品缺陷）。
 *
 * 退出码：0 = 通过；1 = 失败；2 = 用法/环境错误。
 *
 * 用法：
 *   node scripts/verify-ui-pages-live.cjs --app <单文件EXE> [--out <截图目录>] [--json <报告>] [--keep]
 */

'use strict';

const fs = require('fs');
const os = require('os');
const net = require('net');
const path = require('path');
const { spawn } = require('child_process');

const VIEWS = ['home', 'installations', 'worlds', 'servers', 'content', 'skins', 'downloads', 'news', 'settings'];

/*
 * 每页"渲染完成"的最小内容量（字符数）。
 * 为什么不能只判"非空/非占位"：本轮实测 news 曾停在 80 字的中间态（标题+副标题+正在加载），
 * 80 > 20 会被误判为完成。这里按页面实际结构给出下限；达不到就记为 external-pending
 * （外部依赖未落地，比如新闻/版本列表取不回来），而不是当成"界面坏了"，也不是通过。
 */
const VIEW_MIN_TEXT = {
  home: 60,
  installations: 30,
  worlds: 20,
  servers: 20,
  content: 400,
  skins: 40,
  downloads: 200,
  news: 400,
  settings: 200
};

function parseArgs(argv) {
  const out = { app: null, out: null, json: null, keep: false, timeoutSec: 90, settleSec: 75, fakeIpCompat: false, config: null, allowMissingImages: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--app') out.app = argv[++i];
    else if (a === '--out') out.out = argv[++i];
    else if (a === '--json') out.json = argv[++i];
    else if (a === '--timeout') out.timeoutSec = Number(argv[++i]);
    else if (a === '--settle-timeout') out.settleSec = Number(argv[++i]);
    else if (a === '--fake-ip-compat') out.fakeIpCompat = true;
    else if (a === '--allow-missing-images') out.allowMissingImages = true;
    else if (a === '--config') out.config = argv[++i];
    else if (a === '--keep') out.keep = true;
    else if (a === '--help' || a === '-h') out.help = true;
    else throw new Error(`未知参数: ${a}`);
  }
  return out;
}

/*
 * "视图还在加载"的判定：内容只有加载占位符。
 * 为什么要单独判它：部分视图（downloads / content / news）要先向**外部服务**取数据；
 * 本机外部网络受限时，它们会一直等到服务端 45s 超时才落地空状态。
 * 那属于外部依赖，不是产品缺陷 —— 但也不能因此把"没渲染出来"记成通过。
 * 规则：在预算内落地真实内容才算 OK；超预算仍只有占位符则记为 EXTERNAL_PENDING（非通过、非产品失败）。
 */
function looksLoading(text) {
  const t = (text || '').trim();
  if (t.length === 0) return true;
  return t.length <= 12;
}

/* 本机失败分类：只有 UI 自己的资源失败才算产品问题。
 * 依据（本轮实测）：
 *   - /api/news-image 400 dns_private 是 fail-closed 策略在 fake-ip(TUN) 环境下的正确行为
 *     （外部域名解析到 198.18.0.x 广播测试段）；打开 newsFakeIpCompat 后实测 4/4 成功。
 *   - /api/content/icon 404 是上游确实没有该图标；账号 skin-image 404 是当前没有账号皮肤。
 *   - favicon.ico 404 属外观级。
 * 这些都会被**逐条记录在报告里**，不作为通过/失败依据；未知的本机 API 失败仍然算产品问题（fail closed）。
 */
function classifyFailure(entry, apiBase) {
  const url = entry.url || '';
  const status = (/(\d{3})/.exec(entry.text || '') || [])[1] || '';
  if (url.indexOf(apiBase) !== 0) return 'external';
  const p = url.slice(apiBase.length);
  if (/^\/(css|js|vendor|assets)\//.test(p) || p === '/' || p === '/index.html') return 'uiAsset';
  if (p.indexOf('/favicon.ico') === 0) return 'cosmetic';
  if (p.indexOf('/api/news-image') === 0) return 'newsImageProxy';
  if (p.indexOf('/api/content/icon') === 0) return 'contentIconUpstream';
  if (p.indexOf('/api/accounts/') === 0 && p.indexOf('/skin-image') > 0) return 'skinImageNoAccount';
  if (status === '502' || status === '503' || status === '504') return 'proxyTimeout';
  return 'productLocal';
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function freePort() {
  return new Promise((resolve, reject) => {
    const srv = net.createServer();
    srv.on('error', reject);
    srv.listen(0, '127.0.0.1', () => {
      const port = srv.address().port;
      srv.close(() => resolve(port));
    });
  });
}

async function waitFor(fn, timeoutMs, intervalMs = 500) {
  const deadline = Date.now() + timeoutMs;
  let last = null;
  while (Date.now() < deadline) {
    try {
      const v = await fn();
      if (v) return v;
    } catch (e) { last = e; }
    await sleep(intervalMs);
  }
  throw new Error(`等待超时（${timeoutMs}ms）${last ? '：' + last.message : ''}`);
}

/* ---------------------------------------------------------------- CDP 客户端 */

function connectCdp(wsUrl) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl);
    const pending = new Map();
    const notifications = [];
    let seq = 0;
    let opened = false;

    const timer = setTimeout(() => { if (!opened) { try { ws.close(); } catch { } reject(new Error('CDP WebSocket 连接超时')); } }, 15000);

    ws.addEventListener('open', () => {
      opened = true;
      clearTimeout(timer);
      resolve({
        send(method, params = {}) {
          const id = ++seq;
          return new Promise((res, rej) => {
            pending.set(id, { res, rej });
            ws.send(JSON.stringify({ id, method, params }));
          });
        },
        notifications,
        close() { try { ws.close(); } catch { } }
      });
    });
    ws.addEventListener('error', () => { if (!opened) { clearTimeout(timer); reject(new Error('CDP WebSocket 连接失败')); } });
    ws.addEventListener('message', (ev) => {
      let msg;
      try { msg = JSON.parse(ev.data); } catch { return; }
      if (msg.id && pending.has(msg.id)) {
        const { res, rej } = pending.get(msg.id);
        pending.delete(msg.id);
        if (msg.error) rej(new Error(`${msg.error.message || 'CDP error'} (${method || ''})`));
        else res(msg.result);
      } else if (msg.method) {
        notifications.push(msg);
      }
    });
  });
}

/* ---------------------------------------------------------------- 主流程 */

async function main() {
  let args;
  try { args = parseArgs(process.argv.slice(2)); }
  catch (e) { console.error(String(e.message || e)); process.exit(2); }
  if (args.help) { console.log('用法: node scripts/verify-ui-pages-live.cjs --app <EXE> [--out dir] [--json report.json]'); return; }
  if (!args.app) { console.error('缺少 --app <单文件EXE>'); process.exit(2); }

  const exe = path.resolve(args.app);
  if (!fs.existsSync(exe)) { console.error(`找不到发布产物: ${exe}`); process.exit(2); }

  const workRoot = fs.mkdtempSync(path.join(os.tmpdir(), '松光 界面九页 '));
  const shotsDir = args.out ? path.resolve(args.out) : path.join(workRoot, 'shots');
  fs.mkdirSync(shotsDir, { recursive: true });

  const appName = path.basename(exe);
  fs.copyFileSync(exe, path.join(workRoot, appName));
  fs.writeFileSync(path.join(workRoot, 'portable.flag'), 'portable');
  const extractDir = path.join(workRoot, 'bundle-extract');
  fs.mkdirSync(extractDir, { recursive: true });

  // 可选：预写 portable config。fake-ip/TUN 环境（外部域名解析到 198.18/15 广播测试段）
  // 下，新闻图片默认被 fail-closed 策略拒绝；--fake-ip-compat 打开产品的兼容开关后再验一遍。
  let configJson = null;
  if (args.config) configJson = JSON.parse(fs.readFileSync(path.resolve(args.config), 'utf8'));
  else if (args.fakeIpCompat) configJson = { newsFakeIpCompat: true };
  if (configJson) {
    const dataDir = path.join(workRoot, 'data');
    fs.mkdirSync(dataDir, { recursive: true });
    // 无 BOM：config.json 由 JsonSerializer 解析，带 BOM 会被判为坏配置
    fs.writeFileSync(path.join(dataDir, 'config.json'), JSON.stringify(configJson), 'utf8');
    console.log('[ui-pages] 预写 config.json: ' + JSON.stringify(configJson));
  }

  const apiPort = await freePort();
  const debugPort = await freePort();

  console.log(`[ui-pages] app=${exe}`);
  console.log(`[ui-pages] workdir=${workRoot}`);
  console.log(`[ui-pages] api=${apiPort} debug=${debugPort}`);

  const child = spawn(path.join(workRoot, appName), [], {
    cwd: workRoot,
    stdio: 'ignore',
    env: Object.assign({}, process.env, {
      PINEGLOW_API_PORT: String(apiPort),
      PINEGLOW_WEBVIEW_DEBUG: String(debugPort),
      DOTNET_BUNDLE_EXTRACT_BASE_DIR: extractDir
    })
  });

  const apiBase = `http://127.0.0.1:${apiPort}`;
  const report = {
    generatedAt: new Date().toISOString(),
    app: exe,
    workDir: workRoot,
    apiPort,
    debugPort,
    views: {},
    exceptions: [],
    consoleErrors: [],
    externalPending: [],
    imagesPending: [],
    productLocalFailures: [],
    uiAssetFailures: [],
    newsImageProxyFailures: [],
    contentIconFailures: [],
    skinImageFailures: [],
    cosmeticFailures: [],
    proxyTimeoutFailures: [],
    externalNetworkFailures: [],
    apiFailureBodies: [],
    problems: [],
    verdict: 'UNKNOWN',
    options: { fakeIpCompat: args.fakeIpCompat, settleSec: args.settleSec }
  };

  let cdp = null;
  try {
    await waitFor(async () => {
      const r = await fetch(`${apiBase}/api/health`);
      return r.ok;
    }, args.timeoutSec * 1000);
    console.log('[ui-pages] API 就绪');

    const html = await (await fetch(`${apiBase}/`)).text();
    const tokenMatch = /name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"/.exec(html);
    if (!tokenMatch) throw new Error('界面里找不到会话令牌（页面可能不是真实 UI）');
    report.sessionTokenPresent = true;

    const target = await waitFor(async () => {
      const res = await fetch(`http://127.0.0.1:${debugPort}/json/list`);
      const list = await res.json();
      return list.find((t) => t.type === 'page' && typeof t.webSocketDebuggerUrl === 'string');
    }, 45000, 1000);
    console.log(`[ui-pages] CDP target: ${target.url}`);
    report.cdpTargetUrl = target.url;

    cdp = await connectCdp(target.webSocketDebuggerUrl);
    await cdp.send('Runtime.enable');
    await cdp.send('Page.enable');
    await cdp.send('Log.enable').catch(() => { });

    const evaluate = async (expression) => {
      const r = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
      if (r && r.exceptionDetails) throw new Error('页面内求值异常: ' + (r.exceptionDetails.text || ''));
      return r && r.result ? r.result.value : undefined;
    };

    // 等界面脚本完成首屏（存在 nav 且 home 已激活）
    await waitFor(async () => {
      const ready = await evaluate(`!!document.querySelector('.nav-item[data-nav="home"]')`);
      return ready === true;
    }, 30000, 500);

    for (const view of VIEWS) {
      const startedAt = Date.now();
      const clicked = await evaluate(`(() => { const b = document.querySelector('.nav-item[data-nav="${view}"]'); if (!b) return false; b.click(); return true; })()`);
      if (clicked !== true) {
        report.views[view] = { ok: false, reason: '找不到导航按钮' };
        report.problems.push(`视图 ${view}: 找不到导航按钮`);
        continue;
      }

      // 等这一页真正落地：导航激活态切过去 **且** 内容不再是加载占位符。
      // 预算给足（默认 75s）：依赖外部服务的页面在本机外部网络受限时，
      // 要等服务端 45s 超时之后才会渲染空状态。
      const probeExpr = `(() => {
        const el = document.getElementById('view-${view}');
        const text = el ? (el.innerText || '').replace(/\\s+/g, ' ').trim() : '';
        return { exists: !!el, textLen: text.length, text: text.slice(0, 400), sample: text.slice(0, 120) };
      })()`;
      const activeExpr = `(document.querySelector('.nav-item.active') && document.querySelector('.nav-item.active').dataset) ? document.querySelector('.nav-item.active').dataset.nav : null`;

      let active = null;
      let probe = null;
      const settleDeadline = Date.now() + args.settleSec * 1000;
      const minText = VIEW_MIN_TEXT[view] || 20;
      while (true) {
        active = await evaluate(activeExpr);
        probe = await evaluate(probeExpr);
        const settled = active === view && probe && probe.exists === true && !looksLoading(probe.text) && probe.textLen >= minText;
        if (settled || Date.now() >= settleDeadline) break;
        await sleep(400);
      }
      const settleMs = Date.now() - startedAt;

      // 图片是异步加载的（getNewsImage 走 fetch+blob，再 append 到占位 div 里）：
      // 截图前必须等它们真的画出来，否则会把"还没加载"误记成"界面坏了"（本轮实测踩过）。
      // 注意占位符：news 用的是 [data-news-art-id] 容器 + 事后 append <img>，
      // 所以不能只看 <img> 数量 —— 一开始是 0 个 img、有 N 个待填槽位。
      const imgProbeExpr = `(() => {
        const root = document.getElementById('view-${view}');
        if (!root) return { total: 0, loaded: 0, slots: 0, pendingSlots: 0 };
        const imgs = Array.from(root.querySelectorAll('img'));
        const slots = Array.from(root.querySelectorAll('[data-news-art-id]'));
        const pendingSlots = slots.filter(s => !s.querySelector('img') && !s.classList.contains('news-art-failed')).length;
        return { total: imgs.length, loaded: imgs.filter(i => i.complete && i.naturalWidth > 0).length, slots: slots.length, pendingSlots };
      })()`;
      const imgWaitStart = Date.now();
      let imgStats = { total: 0, loaded: 0, slots: 0, pendingSlots: 0 };
      while (true) {
        imgStats = await evaluate(imgProbeExpr);
        const pending = imgStats && ((imgStats.pendingSlots > 0) || (imgStats.total > 0 && imgStats.loaded < imgStats.total));
        if (!pending) break;
        if (Date.now() - imgWaitStart > 20000) break;
        await sleep(500);
      }
      const imagesMs = Date.now() - imgWaitStart;

      const shotPath = path.join(shotsDir, `${view}.png`);
      let shotBytes = 0;
      try {
        const shot = await cdp.send('Page.captureScreenshot', { format: 'png' });
        const buf = Buffer.from(shot.data, 'base64');
        fs.writeFileSync(shotPath, buf);
        shotBytes = buf.length;
      } catch (e) {
        report.problems.push(`视图 ${view}: 截图失败 ${e.message}`);
      }

      const ok = active === view && probe && probe.exists === true && probe.textLen >= minText && shotBytes > 5000;
      // 三分类，避免把"外部依赖没回来"和"界面坏了"混为一谈
      const outcome = ok
        ? 'ok'
        : (probe && probe.exists === true && active === view && probe.textLen > 0 ? 'external-pending' : 'broken');
      const imagesPending = imgStats && ((imgStats.pendingSlots > 0) || (imgStats.total > 0 && imgStats.loaded < imgStats.total));
      report.views[view] = {
        ok,
        outcome,
        settleMs,
        imagesMs,
        imagesTotal: imgStats ? imgStats.total : 0,
        imagesLoaded: imgStats ? imgStats.loaded : 0,
        imageSlots: imgStats ? imgStats.slots : 0,
        pendingSlots: imgStats ? imgStats.pendingSlots : 0,
        activeNav: active,
        exists: probe ? probe.exists : false,
        textLen: probe ? probe.textLen : 0,
        sample: probe ? probe.sample : '',
        screenshot: shotPath,
        screenshotBytes: shotBytes
      };
      console.log(`[ui-pages] ${ok ? 'OK  ' : (outcome === 'external-pending' ? 'WAIT' : 'FAIL')} ${view.padEnd(14)} active=${active} textLen=${probe ? probe.textLen : 0} settle=${settleMs}ms img=${imgStats ? imgStats.loaded + '/' + imgStats.total : '-'} slots=${imgStats ? imgStats.slots : 0}(pending=${imgStats ? imgStats.pendingSlots : 0},${imagesMs}ms) shot=${shotBytes}B`);
      if (outcome === 'external-pending') {
        report.externalPending.push({ view, settleMs, sample: probe ? probe.sample : '' });
      } else if (!ok) {
        report.problems.push(`视图 ${view}: active=${active} exists=${probe && probe.exists} textLen=${probe && probe.textLen} shot=${shotBytes}`);
      }
      if (imagesPending) {
        const item = { view, total: imgStats.total, loaded: imgStats.loaded, slots: imgStats.slots, pendingSlots: imgStats.pendingSlots, waitedMs: imagesMs };
        report.imagesPending.push(item);
        if (!args.allowMissingImages) {
          report.problems.push(`视图 ${view}: 图片未在预算内渲染（img ${imgStats.loaded}/${imgStats.total}，待填槽位 ${imgStats.pendingSlots}，等 ${imagesMs}ms）`);
        }
      }
    }

    // 记录外部依赖的真实耗时（downloads 页依赖它；本机外部网络受限时以此解释 WAIT）
    try {
      const probeStart = Date.now();
      const res = await fetch(`${apiBase}/api/versions/remote`, { headers: { 'X-Pineglow-Token': tokenMatch[1] } });
      report.remoteVersionsProbe = { status: res.status, ms: Date.now() - probeStart };
      console.log(`[ui-pages] /api/versions/remote -> ${res.status} (${report.remoteVersionsProbe.ms}ms)`);
    } catch (e) {
      report.remoteVersionsProbe = { error: e.message };
    }

    // 收集运行时异常与失败请求
    for (const n of cdp.notifications) {
      if (n.method === 'Runtime.exceptionThrown') {
        const d = n.params.exceptionDetails || {};
        report.exceptions.push({ text: d.text || '', url: d.url || '', line: d.lineNumber });
      } else if (n.method === 'Runtime.consoleAPICalled' && n.params.type === 'error') {
        const text = (n.params.args || []).map((a) => a.value !== undefined ? String(a.value) : (a.description || a.type)).join(' ');
        report.consoleErrors.push({ text });
      } else if (n.method === 'Log.entryAdded') {
        const e = n.params.entry || {};
        if (e.level === 'error') {
          const item = { text: e.text || '', url: e.url || '' };
          const kind = classifyFailure(item, apiBase);
          if (kind === 'uiAsset') report.uiAssetFailures.push(item);
          else if (kind === 'newsImageProxy') report.newsImageProxyFailures.push(item);
          else if (kind === 'contentIconUpstream') report.contentIconFailures.push(item);
          else if (kind === 'skinImageNoAccount') report.skinImageFailures.push(item);
          else if (kind === 'cosmetic') report.cosmeticFailures.push(item);
          else if (kind === 'proxyTimeout') report.proxyTimeoutFailures.push(item);
          else if (kind === 'productLocal') report.productLocalFailures.push(item);
          else report.externalNetworkFailures.push(item);
        }
      }
    }

    // 把"只看到状态码"的 API 失败补上响应体里的原因（例如 400 dns_private / 404 unknown_image）
    const apiFailures = [...report.newsImageProxyFailures, ...report.contentIconFailures, ...report.skinImageFailures, ...report.productLocalFailures];
    const seen = new Set();
    for (const item of apiFailures) {
      if (report.apiFailureBodies.length >= 6) break;
      if (seen.has(item.url)) continue;
      seen.add(item.url);
      try {
        const r = await fetch(item.url, { headers: { 'X-Pineglow-Token': tokenMatch[1] } });
        const body = (await r.text()).slice(0, 200);
        report.apiFailureBodies.push({ url: item.url, status: r.status, body });
      } catch (e) {
        report.apiFailureBodies.push({ url: item.url, error: e.message });
      }
    }

    if (report.exceptions.length > 0) report.problems.push(`未捕获 JS 异常 ${report.exceptions.length} 个`);
    if (report.consoleErrors.length > 0) report.problems.push(`console.error ${report.consoleErrors.length} 条`);
    if (report.uiAssetFailures.length > 0) report.problems.push(`界面自身资源加载失败 ${report.uiAssetFailures.length} 个（这是产品问题）`);
    if (report.productLocalFailures.length > 0) report.problems.push(`无法归类的本机接口失败 ${report.productLocalFailures.length} 个（按 fail-closed 记为产品问题）`);
  } catch (e) {
    report.problems.push('执行失败: ' + e.message);
  } finally {
    if (cdp) cdp.close();
    try { child.kill(); } catch { }
    try {
      if (process.platform === 'win32') {
        const { spawnSync } = require('child_process');
        spawnSync('taskkill', ['/T', '/F', '/PID', String(child.pid)], { stdio: 'ignore' });
      }
    } catch { }
    await sleep(500);
    if (!args.keep) {
      try { fs.rmSync(workRoot, { recursive: true, force: true }); } catch { }
    }
  }

  const viewCount = Object.keys(report.views).length;
  const okViews = Object.values(report.views).filter((v) => v.ok).length;
  report.verdict = report.problems.length === 0 && okViews === VIEWS.length ? 'PASS' : 'FAIL';
  report.summary = {
    views: VIEWS.length,
    okViews,
    externalPending: report.externalPending.length,
    exceptions: report.exceptions.length,
    consoleErrors: report.consoleErrors.length,
    uiAssetFailures: report.uiAssetFailures.length,
    productLocalFailures: report.productLocalFailures.length,
    newsImageProxyFailures: report.newsImageProxyFailures.length,
    contentIconFailures: report.contentIconFailures.length,
    skinImageFailures: report.skinImageFailures.length,
    proxyTimeoutFailures: report.proxyTimeoutFailures.length,
    cosmeticFailures: report.cosmeticFailures.length,
    externalNetworkFailures: report.externalNetworkFailures.length
  };

  if (args.json) {
    fs.mkdirSync(path.dirname(path.resolve(args.json)), { recursive: true });
    fs.writeFileSync(path.resolve(args.json), JSON.stringify(report, null, 2), 'utf8');
  }

  console.log('');
  console.log(`UI-PAGES RESULT=${report.verdict} views=${okViews}/${VIEWS.length} externalPending=${report.externalPending.length} exceptions=${report.exceptions.length} consoleErrors=${report.consoleErrors.length} uiAssetFailures=${report.uiAssetFailures.length} productLocalFailures=${report.productLocalFailures.length}`);
  console.log(`  （非致命，逐条记录）newsImageProxy=${report.newsImageProxyFailures.length} contentIcon=${report.contentIconFailures.length} skinImage=${report.skinImageFailures.length} proxyTimeout=${report.proxyTimeoutFailures.length} cosmetic=${report.cosmeticFailures.length} external=${report.externalNetworkFailures.length}`);
  if (report.apiFailureBodies.length > 0) {
    console.log('  API 失败响应体（用于区分策略拒绝 / 上游缺失 / 环境 DNS）:');
    for (const b of report.apiFailureBodies) console.log(`    ${b.status || 'ERR'} ${b.body || b.error}  <- ${b.url.slice(0, 80)}…`);
  }
  if (report.externalPending.length > 0) {
    console.log('外部依赖未在预算内落地（非产品失败，需在联网环境复验）:');
    for (const p of report.externalPending) console.log(`  - ${p.view} 等了 ${p.settleMs}ms，内容仍是占位`);
  }
  if (report.problems.length > 0) {
    console.log('问题:');
    for (const p of report.problems) console.log('  - ' + p);
  }
  process.exit(report.verdict === 'PASS' ? 0 : 1);
}

main().catch((e) => { console.error('[ui-pages] 致命错误: ' + (e && e.stack ? e.stack : e)); process.exit(2); });
