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
 * 本轮修复（2026-10-13，CV-07）：
 *   1. CDP 客户端：每个 pending 请求保存自己的 method；错误响应按 method 正确 reject。
 *      旧实现里 `rej(new Error(... (${method || ''})))` 引用了**不存在**的 method，
 *      在 message 回调里同步抛 ReferenceError —— 该请求已从 pending 删除却又从未 settle，
 *      于是一次 CDP 错误会让验收**永久挂起**（RED 证据 docs/current-version-audit-2026-10-13/
 *      cdp-error-red.log：`CDP_ERROR_THROW=ReferenceError:method is not defined`）。
 *      现在：未知 id / 重复响应安全忽略，连接关闭时把未完成请求全部 reject，回调内异常兜底。
 *   2. 终态判定：明确区分「正常有内容 / 成功空态 / 已落地错误状态 / 仍在加载」。
 *      旧实现只看最小字符数，短页面一律记成 external-pending —— 空态与错误态天然就短，
 *      于是"已落地的终态"被误报成"还在加载"。
 *   3. 失败分类：不再把所有 502/503/504 当"代理超时"放行。只有**响应正文**明确写出
 *      上游网络错误证据（DNS/连接/超时/TLS 等）时才可归为外部依赖；未知的本机 5xx
 *      （含 500）继续 fail-closed 记为产品问题。读正文时先用**完整正文**判断，再截断展示。
 *   4. 报告脱敏：写 JSON 之前对整份报告递归脱敏 token / Cookie / Bearer / 查询串敏感参数；
 *      URL 自身带敏感值时**不重发**该请求，只记录脱敏后的形状；并自检会话令牌不出现在归档里。
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

const VIEWS = ['home', 'installations', 'worlds', 'servers', 'playtime', 'content', 'skins', 'downloads', 'news', 'settings'];

/* ------------------------------------------------------------------ *
 * HA-01：界面图片统计口径与懒加载滚动（模块作用域，便于行为验证直接执行同一份源码）
 *
 * 为什么放在这里而不是内联在循环里：第二轮独立验收指出，只断言「会触发分支」
 * 不能证明「滚动有效」。这里把它们做成可导出的字符串函数，测试可以注入到真实浏览器里
 * 执行**生产同一份**逻辑，而不是另写一套判定。
 * ------------------------------------------------------------------ */

/* 只统计「可能渲染真实图片」的节点，否则会把自己的隐藏占位当成加载失败：
 * 内容库 24 张图被 .mod-icon{display:none} 隐藏、布局宽高为 0；skins 页只有一个
 * hidden 且无 src 的本地文件预览占位 —— 它们不可能加载出图片，旧实现却把整份巡检
 * 判成 FAIL（exit 1）。
 * 判定只依据**实际渲染 / 布局 / 来源**，不依据可访问性元数据：
 *   - `aria-hidden="true"` 只对辅助技术隐藏，元素照样可见（独立验收反例：一个
 *     aria-hidden 的可见破图被旧实现漏报）。因此不据此排除。
 *   - `type` 属性不会把 HTML <img> 变成 SVG <image>；选择器已是 'img'，
 *     再按 type 文本排除会把可见 HTML 图片错当成 SVG 而漏报。因此不据此排除。
 * 仍在文档内、但滚出视口的节点保留统计（懒加载），由 LAZY_SCROLL_INDEX_FN 处理。 */
const IMG_VISIBLE_FN = `function (i) {
  if (!i || !i.isConnected) return false;
  if (!i.getAttribute('src') && !i.getAttribute('srcset')) return false;   // 无来源 => 尚未选择文件的占位
  if (i.hidden) return false;                                             // hidden 属性：整元素不渲染
  let n = i;
  while (n && n.nodeType === 1 && n !== document.documentElement) {
    const s = getComputedStyle(n);
    if (s.display === 'none' || s.visibility === 'hidden' || s.visibility === 'collapse') return false;
    n = n.parentElement;
  }
  const r = i.getBoundingClientRect();
  if (r.width <= 0 || r.height <= 0) return false;   // 隐藏祖先 / 未参与布局 => 无法验证可见性
  return true;
}`;

/* 图片统计探针。__VIEW__ 替换为页面 id，__VISIBLE_FN__ 替换为 IMG_VISIBLE_FN。
 * 用占位符而不是嵌套模板字符串：嵌套反引号会让外部工具（含本文件自己的字符串抽取）
 * 无法可靠地取出这段源码，而行为验证必须能拿到**同一份**逻辑。 */
const IMG_PROBE_TEMPLATE = `(() => {
  const root = document.getElementById('view-__VIEW__');
  if (!root) return { total: 0, loaded: 0, broken: 0, offscreen: 0, slots: 0, pendingSlots: 0 };
  const visible = (__VISIBLE_FN__);
  const imgs = Array.from(root.querySelectorAll('img')).filter(visible);
  const slots = Array.from(root.querySelectorAll('[data-news-art-id]'));
  const pendingSlots = slots.filter(s => !s.querySelector('img') && !s.classList.contains('news-art-failed')).length;
  return {
    total: imgs.length,
    loaded: imgs.filter(i => i.complete && i.naturalWidth > 0).length,
    broken: imgs.filter(i => i.complete && i.naturalWidth === 0 && (i.currentSrc || i.getAttribute('src') || i.getAttribute('srcset'))).length,
    offscreen: imgs.filter(i => { const r = i.getBoundingClientRect(); return r.bottom <= 0 || r.top >= innerHeight; }).length,
    slots: slots.length,
    pendingSlots
  };
})()`;

/* 懒加载滚动相关的表达式工厂：生产路径与行为验证共用**同一套**构造，
 * 避免"测试自己拼一套表达式"导致验证不到真实接线（第三轮独立复验 R3-01 的教训）。
 * 所有表达式只接收可序列化数据（下标 / JSON），DOM 元素一律在页面内部查询。 */
function buildLazyExprs(view) {
  const rootExpr = `document.getElementById('view-${view}')`;
  return {
    save: `(() => {
      const root = ${rootExpr};
      if (!root) return { containers: [], windowX: 0, windowY: 0 };
      return (${LAZY_SAVE_FN})(root);
    })()`,
    count: `(() => {
      const root = ${rootExpr};
      if (!root) return 0;
      const visible = (${IMG_VISIBLE_FN});
      return Array.from(root.querySelectorAll('img')).filter(i => {
        if (i.getAttribute('data-defer-src') && !i.getAttribute('src')) return true;   // 懒加载占位
        if (!visible(i)) return false;
        const r = i.getBoundingClientRect();
        return r.bottom <= 0 || r.top >= innerHeight;
      }).length;
    })()`,
    scrollAt: (index) => `(() => {
      const root = ${rootExpr};
      if (!root) return { scrolled: false, containerCount: 0 };
      return (${LAZY_SCROLL_INDEX_FN.replace('__VISIBLE__', IMG_VISIBLE_FN)})(root, ${index});
    })()`,
    restore: (savedJson) => `(() => {
      const root = ${rootExpr};
      if (!root) return 0;
      return (${LAZY_RESTORE_FN})(root, ${savedJson});
    })()`,
  };
}

/* 组装某个页面的探针表达式（生产路径与行为验证共用同一套替换规则）。 */
function buildImgProbeExpr(view) {
  return IMG_PROBE_TEMPLATE
    .replace('__VIEW__', view)
    .replace('__VISIBLE_FN__', IMG_VISIBLE_FN);
}

/* 懒加载滚动：把滚出视口的候选真正带进可视区域。
 * 修旧实现的三个问题：
 *   1. 产品实际滚动的是内层容器（.content-panel，ui/css/figma.css 的 overflow-y:auto），
 *      固定滚 window 对它没有任何作用 => 找每个图片**真实的可滚动祖先**。
 *   2. 连续同步滚动后立即复位不给浏览器渲染/懒加载调度机会
 *      => 由调用方**逐个目标**调用本函数，并在两次之间等待复测。
 *      逐个是必要的：多目标时滚到较远那个会把较近那个又推出视口
 *      （实测两个目标、一次性 scrollIntoView 后近端 top=-483），
 *      于是近端永远不会真正进入视口。
 *   3. 不复位滚动位置 => 由 __SAVE__FN__ / __RESTORE__FN__ 记录并恢复**原值**。
 *
 * 第三轮独立复验 R3-01：**函数只接受可序列化的下标**，不接受 DOM 元素。
 * 原因：正式路径的 evaluate 包装器只接收一个 expression 且用 returnByValue:true，
 * 第二参数会被丢弃、DOM 元素跨 CDP 按值转换会变成 {}（实测 targets 变 [{}]、
 * 返回 {}、滚动 0 次），滚动根本没发生。所以元素查询与调用必须都在页面表达式内完成。 */
const LAZY_SCROLL_INDEX_FN = `function (root, index) {
  if (!root) return { scrolled: false, containerCount: 0 };
  const scrollable = (el) => {
    if (!el || el.nodeType !== 1) return false;
    const s = getComputedStyle(el);
    const canY = /(auto|scroll|overlay)/.test(s.overflowY) && el.scrollHeight > el.clientHeight + 1;
    const canX = /(auto|scroll|overlay)/.test(s.overflowX) && el.scrollWidth > el.clientWidth + 1;
    return canY || canX;
  };
  const visible = (__VISIBLE__);
  // 视口外候选 = 「已可见且有来源、但还没进视口」**或**「声明了延迟来源、尚未赋 src
  //   （data-defer-src）的懒加载占位」。
  //   第二类必须先被滚到才会拿到 src，否则它们永远不会加载、也不会被统计 ——
  //   而它们恰恰是懒加载真正要等的那批（第三轮独立复验 R3-01 联测暴露）。
  const imgs = Array.from(root.querySelectorAll('img')).filter(i => {
    if (i.getAttribute('data-defer-src') && !i.getAttribute('src')) return true;   // 懒加载占位
    if (!visible(i)) return false;
    const r = i.getBoundingClientRect();
    return r.bottom <= 0 || r.top >= innerHeight;
  });
  const img = imgs[index];
  if (!img) return { scrolled: false, containerCount: 0 };
  let containers = 0;
  let p = img.parentElement;
  while (p && p !== document.documentElement) {
    if (scrollable(p)) containers++;
    p = p.parentElement;
  }
  // block:'nearest' 而非 'center'：center 对容器内靠后的元素会把容器滚到最大值，
  // 反而把元素推到视口**上方**（实测 top=-487）。nearest 只带到最近的边缘。
  try { img.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) { return { scrolled: false, containerCount: containers }; }
  const r = img.getBoundingClientRect();
  return { scrolled: r.top < innerHeight && r.bottom > 0, containerCount: containers };
}`;

/* 记录即将被触碰的滚动容器与 window 的**原始位置**（供恢复用）。 */
const LAZY_SAVE_FN = `function (root) {
  const scrollable = (el) => {
    if (!el || el.nodeType !== 1) return false;
    const s = getComputedStyle(el);
    const canY = /(auto|scroll|overlay)/.test(s.overflowY) && el.scrollHeight > el.clientHeight + 1;
    const canX = /(auto|scroll|overlay)/.test(s.overflowX) && el.scrollWidth > el.clientWidth + 1;
    return canY || canX;
  };
  const seen = new Set();
  for (const img of Array.from(root.querySelectorAll('img'))) {
    let p = img.parentElement;
    while (p && p !== document.documentElement) {
      if (scrollable(p)) seen.add(p);
      p = p.parentElement;
    }
  }
  return {
    containers: Array.from(seen).map(el => ({ top: el.scrollTop, left: el.scrollLeft })),
    windowX: window.scrollX,
    windowY: window.scrollY,
  };
}`;

/* 恢复：只复位**本轮实际触碰过**的容器与 window，并恢复保存的**原值**（不是清零）。
 * R3-02：旧实现把所有容器 scrollTop/scrollLeft 一律设为 0、window 归 0，
 * 初值非零时会把状态改坏（实测 137 -> 0）。 */
const LAZY_RESTORE_FN = `function (root, saved) {
  const scrollable = (el) => {
    if (!el || el.nodeType !== 1) return false;
    const s = getComputedStyle(el);
    const canY = /(auto|scroll|overlay)/.test(s.overflowY) && el.scrollHeight > el.clientHeight + 1;
    const canX = /(auto|scroll|overlay)/.test(s.overflowX) && el.scrollWidth > el.clientWidth + 1;
    return canY || canX;
  };
  const state = saved || { containers: [], windowX: 0, windowY: 0 };
  const seen = new Set();
  for (const img of Array.from(root.querySelectorAll('img'))) {
    let p = img.parentElement;
    while (p && p !== document.documentElement) {
      if (scrollable(p)) seen.add(p);
      p = p.parentElement;
    }
  }
  const list = Array.from(seen);
  let n = 0;
  for (let i = 0; i < list.length; i++) {
    const original = state.containers && state.containers[i];
    if (!original) continue;                 // 只恢复本轮记录过的容器
    if (list[i].scrollTop !== original.top) { list[i].scrollTop = original.top; n++; }
    if (list[i].scrollLeft !== original.left) { list[i].scrollLeft = original.left; n++; }
  }
  if (window.scrollX !== state.windowX || window.scrollY !== state.windowY) {
    window.scrollTo(state.windowX || 0, state.windowY || 0);
    n++;
  }
  return n;
}`;

/*
 * 每页"正常有内容"的最小内容量（字符数）。
 * 为什么不能只判"非空/非占位"：本轮实测 news 曾停在 80 字的中间态（标题+副标题+正在加载），
 * 80 > 20 会被误判为完成。这里按页面实际结构给出下限。
 *
 * 关键（CV-07）：这个下限**只**用来判定"正常有内容"，不能拿它一概判定"仍在加载"。
 * 页面可能已经落到别的**终态**（成功空态 / 已落地错误状态），它们天然比正常内容短，
 * 由 classifyViewState 先看页面里的终态标记再决定 —— 短 ≠ 加载。
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

/* 页面内用来判定**终态**的 DOM 标记（在生产 launcher.js 里一一对应）：
 *   loading → .loading-state / .news-skeleton / [data-news-skeleton="1"] / 骨架行
 *   empty   → .empty-state / [data-news-state="empty"]
 *   error   → .content-error / [data-error="1"] / [data-tasks-error="1"] /
 *             [data-news-state="error"] / .news-error-state / [data-state="failed"]
 * 这些标记与"重试入口"标记共同证明：这一页已经落地，而不是还在等数据。 */
const LOADING_MARKERS = ['.loading-state', '.news-skeleton', '[data-news-skeleton="1"]', '.skeleton-row', '.skeleton-stack'];
const EMPTY_MARKERS = ['.empty-state', '[data-news-state="empty"]'];
const ERROR_MARKERS = ['.content-error', '[data-error="1"]', '[data-tasks-error="1"]', '[data-news-state="error"]', '.news-error-state', '[data-state="failed"]'];
const ACTION_MARKERS = ['[data-retry-btn]', '[data-news-retry]', '[data-dl-retry]', '[data-dl-tasks-retry]', '[data-state-retry-btn]'];

/*
 * 终态判定：一页只有落到下面四种终态之一才算"有结论"，其余一律按"仍在加载"处理。
 *   1. content  正常有内容（文本量达到该页下限）
 *   2. empty    成功空态（请求成功但列表为空）—— 是**终态**，不是加载中
 *   3. error    已落地的错误状态（页面自己画出了错误块与重试入口）—— 也是终态
 *   4. loading  仍在加载（骨架/占位，或内容量不足且没有任何终态标记）
 * 另有 missing：容器根本不存在（页面结构坏了，与"加载中"是两回事）。
 */
function classifyViewState(probe, minText) {
  if (!probe || probe.exists !== true) return 'missing';
  const len = Number(probe.textLen) || 0;
  if (len <= 0) return 'loading';
  if (probe.loading) return 'loading';
  if (probe.error) return 'error';
  if (probe.empty) return 'empty';
  const floor = Number(minText) || 20;
  return len >= floor ? 'content' : 'loading';
}

/* 终态集合：落到其中之一就不必再等（等待预算只该花在"还在加载"上）。 */
function isTerminalState(state) {
  return state === 'content' || state === 'empty' || state === 'error';
}

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

/* ---------------------------------------------------------------- 失败分类 */

/* 「上游网络错误」的**正文证据**（大小写不敏感）。
 * 关键（CV-07）：只有响应正文明确写出下列证据时，才允许把一次本机 API 失败归为"外部依赖"。
 * 只看状态码是不够的：502/503/504 既可能是代理连不上上游，也可能是本机网关自己坏了；
 * 未知的本机 5xx（含 500）一律 fail-closed 记为产品问题。 */
const UPSTREAM_NETWORK_EVIDENCE = [
  /dns_private/i,
  /dns (?:resolution|lookup|error|failure|timeout)/i,
  /getaddrinfo/i,
  /\bENOTFOUND\b/,
  /\bEAI_AGAIN\b/,
  /\bECONNREFUSED\b/,
  /\bECONNRESET\b/,
  /\bETIMEDOUT\b/,
  /\bEPIPE\b/,
  /socket hang ?up/i,
  /network is unreachable/i,
  /no such host/i,
  /name or service not known/i,
  /(?:certificate|handshake|tls) (?:verify|verification|error|failed|failure)/i,
  /upstream (?:connect|connection|request|timeout|error|unavailable|failed)/i,
  /不知道这样的主机|无法解析(?:主机|域名)|域名解析失败/,
  /无法连接 Minecraft 版本下载源|无法连接(?:到)?(?:下载源|上游)|连接(?:超时|被拒绝|被重置)/
];

function hasUpstreamNetworkEvidence(bodyText) {
  const s = String(bodyText == null ? '' : bodyText);
  if (!s) return false;
  return UPSTREAM_NETWORK_EVIDENCE.some((re) => re.test(s));
}

const FAILURE_BODY_DISPLAY_LIMIT = 200;

/* 先用**完整正文**判断，再截断成展示片段。
 * 顺序不能反：证据可能出现在第 200 字节之后，先截断就会把真实的 DNS/连接错误
 * 误判成"未知本机失败"，从而把一个外部依赖问题记成产品缺陷（或反之）。 */
function evaluateFailureBody(fullBody) {
  const full = String(fullBody == null ? '' : fullBody);
  let evidence = full;
  try {
    const parsed = JSON.parse(full);
    // Only error fields are evidence. A note describing DNS must not excuse
    // an unrelated internal error; JSON.parse also decodes .NET Unicode escapes.
    const message = value => typeof value === 'string' ? value
      : value && typeof value.message === 'string' ? value.message : '';
    evidence = parsed && typeof parsed === 'object'
      ? [message(parsed.error), message(parsed.message)].filter(Boolean).join('\n')
      : typeof parsed === 'string' ? parsed : '';
  } catch (_) { /* Non-JSON responses may contain a plain network error. */ }
  return {
    upstreamNetwork: hasUpstreamNetworkEvidence(evidence),
    fullLength: full.length,
    display: full.slice(0, FAILURE_BODY_DISPLAY_LIMIT)
  };
}

/* 本机失败分类：只有 UI 自己的资源失败才算产品问题。
 * 依据（延续上一轮实测结论）：
 *   - /api/news-image 400 dns_private 是 fail-closed 策略在 fake-ip(TUN) 环境下的正确行为
 *     （外部域名解析到 198.18.0.x 广播测试段）；打开 newsFakeIpCompat 后实测 4/4 成功。
 *   - /api/content/icon 404 是上游确实没有该图标；账号 skin-image 404 是当前没有账号皮肤。
 *   - favicon.ico 404 属外观级。
 * 本函数只做"不需要正文就能确定"的分类；其余本机 API 失败统一落到 productLocal（fail-closed），
 * 再由 evaluateFailureBody 用完整正文决定能否改判为 upstreamNetwork（外部依赖）。
 * 这些都会被**逐条记录在报告里**；未知的本机 5xx 仍然算产品问题（fail closed）。 */
function classifyFailure(entry, apiBase) {
  const url = (entry && entry.url) ? entry.url : '';
  if (url.indexOf(apiBase) !== 0) return 'external';
  const p = url.slice(apiBase.length);
  if (/^\/(css|js|vendor|assets)\//.test(p) || p === '/' || p === '/index.html') return 'uiAsset';
  if (p.indexOf('/favicon.ico') === 0) return 'cosmetic';
  if (p.indexOf('/api/news-image') === 0) return 'newsImageProxy';
  if (p.indexOf('/api/content/icon') === 0) return 'contentIconUpstream';
  if (p.indexOf('/api/accounts/') === 0 && p.indexOf('/skin-image') > 0) return 'skinImageNoAccount';
  return 'productLocal';
}

/* ---------------------------------------------------------------- 报告脱敏 */

const REDACTED = '<REDACTED>';
const SENSITIVE_REPORT_KEY = /^(?:token|authorization|proxy-authorization|cookie|set-cookie|x-pineglow-token|pineglow-session-token|(?:session|access|refresh|id)[-_]?token|api[-_]?key|apikey|secret|password|passwd|pwd|signature|session[-_]?id)$/i;

/* 归档前统一脱敏：token / Cookie / Authorization: Bearer / 查询串里的敏感参数。
 * 对**整份报告**递归应用，保证任何嵌套字段（URL、日志文本、响应体）都不会把会话令牌
 * 带进证据文件。 */
function redactSensitive(value) {
  let s = String(value == null ? '' : value);
  // Structured bodies: redact values by key, including nested credentials.
  try {
    const parsed = JSON.parse(s);
    if (parsed && typeof parsed === 'object') return JSON.stringify(redactReport(parsed));
  } catch (_) { /* Text/header/URL redaction below. */ }
  // A Cookie header can contain multiple semicolon-separated credentials.
  s = s.replace(/(\b(?:set-cookie|cookie|authorization|proxy-authorization)\s*:\s*)[^\r\n]*/gi, `$1${REDACTED}`);
  // 认证头形态：Authorization: Bearer <token>
  s = s.replace(/\b(Bearer|Basic|Negotiate)\s+[A-Za-z0-9._~+/=-]{6,}/gi, `$1 ${REDACTED}`);
  // 名值形态：请求头 / HTML meta content / JSON 字段（token / cookie / authorization / session …）
  s = s.replace(
    /(["']?(?:token|authorization|proxy-authorization|cookie|set-cookie|x-pineglow-token|pineglow-session-token|session[-_]?token|access[-_]?token|refresh[-_]?token|id[-_]?token|api[-_]?key|apikey|secret|password|passwd|pwd|signature|session[-_]?id)["']?\s*[:=]\s*)("?)[^\s;&"'<>]{1,}/gi,
    `$1$2${REDACTED}`
  );
  // HTML 形态：<meta name="pineglow-session-token" content="<令牌>">
  s = s.replace(
    /((?:name|property)\s*=\s*["']?(?:pineglow-session-token|session[-_]?token|token|csrf[-_]?token|xsrf[-_]?token)["']?[^>]*?\scontent\s*=\s*["']?)[^"'\s>]+/gi,
    `$1${REDACTED}`
  );
  // URL 查询串里的敏感参数（含未在上面列出的 *token* / *auth* / *code* 变体）
  s = s.replace(
    /([?&][A-Za-z0-9_.-]*(?:token|secret|signature|password|passwd|pwd|apikey|api[-_]key|session[-_]?id|auth|code)[A-Za-z0-9_.-]*=)[^&#\s"'<>]*/gi,
    `$1${REDACTED}`
  );
  return s;
}

function redactReport(node) {
  if (typeof node === 'string') return redactSensitive(node);
  if (Array.isArray(node)) return node.map(redactReport);
  if (node && typeof node === 'object') {
    const out = {};
    for (const k of Object.keys(node)) out[k] = SENSITIVE_REPORT_KEY.test(k) ? REDACTED : redactReport(node[k]);
    return out;
  }
  return node;
}

/* 失败 URL 的复采计划：URL 自身带敏感值时**不重发**（重发等于把 token 再送一次），
 * 只记录脱敏后的形状 —— 避免"重新发送 URL 里的 token 再把它写进归档"。 */
function planApiFailureProbe(url) {
  const raw = String(url == null ? '' : url);
  if (!raw) return { refetch: false, url: '', reason: 'empty-url' };
  const safe = redactSensitive(raw);
  if (safe !== raw) return { refetch: false, url: safe, reason: 'url-carries-secret' };
  return { refetch: true, url: raw, reason: '' };
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

    /* 连接结束（关闭/出错）时，必须把仍在等待的请求全部 reject。
     * 否则调用方 await 一个永远不会 settle 的 promise —— 整轮验收静默挂死，
     * 既没有失败结论也没有超时结论。 */
    const rejectAllPending = (reason) => {
      const entries = Array.from(pending.entries());
      pending.clear();
      for (const [id, entry] of entries) {
        try { entry.rej(new Error(`${reason}: ${entry.method || 'unknown'} (id=${id})`)); } catch { /* reject 回调本身抛错也不能影响其它请求 */ }
      }
    };

    ws.addEventListener('open', () => {
      opened = true;
      clearTimeout(timer);
      resolve({
        send(method, params = {}) {
          const id = ++seq;
          return new Promise((res, rej) => {
            // 关键（CV-07）：每个 pending 请求保存自己的 method。
            // 错误响应里要能报出"是哪条 CDP 命令失败"；旧实现引用了作用域里不存在的
            // `method`，在 message 回调里同步抛 ReferenceError → 请求永不 settle。
            pending.set(id, { res, rej, method });
            try {
              ws.send(JSON.stringify({ id, method, params }));
            } catch (e) {
              pending.delete(id);
              rej(new Error(`CDP 发送失败: ${method} (${(e && e.message) ? e.message : e})`));
            }
          });
        },
        notifications,
        close() { try { ws.close(); } catch { } }
      });
    });
    ws.addEventListener('error', () => { if (!opened) { clearTimeout(timer); rejectAllPending('CDP WebSocket 连接失败'); reject(new Error('CDP WebSocket 连接失败')); } });
    ws.addEventListener('close', () => {
      clearTimeout(timer);
      rejectAllPending('CDP 连接已关闭，请求未完成');
      if (!opened) reject(new Error('CDP WebSocket 在建立前关闭'));
    });
    ws.addEventListener('message', (ev) => {
      let msg;
      try { msg = JSON.parse(ev.data); } catch { return; }
      // 整个回调都必须安全：这里的异常会变成未捕获异常并让请求永久挂起（旧缺陷正是如此）。
      try {
        if (msg && msg.id !== undefined && msg.id !== null) {
          const entry = pending.get(msg.id);
          // 未知 id 或重复响应（entry 已被删除）→ 安全忽略，不影响其它请求。
          if (!entry) return;
          pending.delete(msg.id);
          if (msg.error) entry.rej(new Error(`${msg.error.message || 'CDP error'} (${entry.method || ''})`));
          else entry.res(msg.result);
          return;
        }
        if (msg && msg.method) notifications.push(msg);
      } catch (e) {
        try { console.warn('[ui-pages] CDP 消息处理失败: ' + ((e && e.message) ? e.message : e)); } catch { }
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
    landedErrorStates: [],
    landedEmptyStates: [],
    imagesPending: [],
    productLocalFailures: [],
    uiAssetFailures: [],
    newsImageProxyFailures: [],
    contentIconFailures: [],
    skinImageFailures: [],
    cosmeticFailures: [],
    upstreamNetworkFailures: [],
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
    // 会话令牌只留在内存里用于请求头，绝不写进 report（写报告前还会再自检一次）。
    cdpSessionToken = tokenMatch[1];

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

      // 等这一页真正落地（**终态判定**，CV-07）。
      // 预算给足（默认 75s）：依赖外部服务的页面在本机外部网络受限时，
      // 要等服务端 45s 超时之后才会渲染空状态/错误状态。
      // 一旦落到终态就立刻停等 —— content / empty / error 都是有结论的状态，
      // 不该把整段预算耗在"已经知道结果"的页面上。
      const probeExpr = `(() => {
        const el = document.getElementById('view-${view}');
        const text = el ? (el.innerText || '').replace(/\\s+/g, ' ').trim() : '';
        const has = (sels) => !!el && sels.some((s) => !!el.querySelector(s));
        return {
          exists: !!el,
          textLen: text.length,
          text: text.slice(0, 400),
          sample: text.slice(0, 120),
          loading: has(${JSON.stringify(LOADING_MARKERS)}),
          empty: has(${JSON.stringify(EMPTY_MARKERS)}),
          error: has(${JSON.stringify(ERROR_MARKERS)}),
          hasAction: has(${JSON.stringify(ACTION_MARKERS)})
        };
      })()`;
      const activeExpr = `(document.querySelector('.nav-item.active') && document.querySelector('.nav-item.active').dataset) ? document.querySelector('.nav-item.active').dataset.nav : null`;

      let active = null;
      let probe = null;
      let state = 'loading';
      const settleDeadline = Date.now() + args.settleSec * 1000;
      const minText = VIEW_MIN_TEXT[view] || 20;
      while (true) {
        active = await evaluate(activeExpr);
        probe = await evaluate(probeExpr);
        state = classifyViewState(probe, minText);
        const settled = active === view && isTerminalState(state);
        if (settled || Date.now() >= settleDeadline) break;
        await sleep(400);
      }
      const settleMs = Date.now() - startedAt;

      // 图片是异步加载的（getNewsImage 走 fetch+blob，再 append 到占位 div 里）：
      // 截图前必须等它们真的画出来，否则会把"还没加载"误记成"界面坏了"（本轮实测踩过）。
      // 注意占位符：news 用的是 [data-news-art-id] 容器 + 事后 append <img>，
      // 所以不能只看 <img> 数量 —— 一开始是 0 个 img、有 N 个待填槽位。
      // HA-01 的图片判定与懒加载滚动逻辑定义在模块作用域（IMG_VISIBLE_FN /
      // IMG_PROBE_TEMPLATE / LAZY_SCROLL_FN / LAZY_RESTORE_FN），并对外导出，
      // 使行为验证能执行与生产**同一份**源码而不是另写一份判定。
      const imgProbeExpr = buildImgProbeExpr(view);
      const imgWaitStart = Date.now();
      let imgStats = { total: 0, loaded: 0, broken: 0, offscreen: 0, slots: 0, pendingSlots: 0 };
      while (true) {
        imgStats = await evaluate(imgProbeExpr);
        // 目标函数：所有被统计的图片都出结果（成功或明确失败），且没有待填的异步槽位。
        // 旧实现把 pending 定义为 loaded < total，于是"永久 0/24"会一直等到超时；
        // 现在旧的隐藏/无来源节点已被排除，可见而仍在加载的才继续等。
        const pending = imgStats && ((imgStats.pendingSlots > 0) || (imgStats.total > 0 && imgStats.loaded + imgStats.broken < imgStats.total));
        if (!pending) break;
        if (Date.now() - imgWaitStart > 20000) break;
        await sleep(500);
      }
      // HA-01 懒加载：把滚出视口的候选真正带进可视区域，避免"没进视口"被当成"没渲染"。
      // 第二轮独立验收的反例说明旧实现的三个问题，这里逐条修：
      //   1. 产品实际滚动的是内层容器（.content-panel，ui/css/figma.css 的 overflow-y:auto），
      //      固定滚 window 对它没有任何作用 => 改为找每个图片**真实的可滚动祖先**。
      //   2. 旧实现连续同步滚动后立即复位，不给浏览器渲染/懒加载调度机会
      //      => 改为逐项滚入视口，并在每步之间留出等待。
      //   3. 旧实现不复位滚动位置 => 记录每个滚动容器的 scrollTop/scrollLeft，
      //      测完复位（截图前恢复原状）。
      // 注意仍不做「滚过就放行」：滚完继续按原预算统计 loaded/broken，
      // 可见破图与真实未完成依旧算失败。
      // R3-01：**元素身份不能跨 CDP 传递**。正式路径的 evaluate 只接收一个 expression
      // 且 returnByValue:true —— 传 DOM 元素会变成 {}，传第二参数会被直接丢弃，
      // 传 `(img) => ...` 这样的函数表达式只会创建函数对象、不会调用它（实测 targets=[{}]、
      // 返回 {}、滚动 0 次）。因此元素查询与调用都在**页面表达式内部**完成，
      // 跨边界只传可序列化的下标与已保存的滚动位置。
      // 表达式由 buildLazyExprs 统一构造，保证行为验证跑的是同一套接线。
      const lazyExprs = buildLazyExprs(view);
      if (imgStats && imgStats.offscreen > 0) {
        try {
          const saved = await evaluate(lazyExprs.save);
          const targetCount = await evaluate(lazyExprs.count);
          let inView = 0;
          let containers = 0;
          const budgetEnd = Date.now() + 8000;
          for (let index = 0; index < targetCount; index++) {
            const res = await evaluate(lazyExprs.scrollAt(index));
            if (res) {
              if (res.scrolled) inView++;
              containers = Math.max(containers, res.containerCount || 0);
            }
            // 每个目标之间等待，给渲染 / 懒加载调度留机会
            const stepEnd = Math.min(budgetEnd, Date.now() + 1200);
            while (Date.now() < stepEnd) {
              imgStats = await evaluate(imgProbeExpr);
              if (imgStats.loaded + imgStats.broken >= imgStats.total && imgStats.pendingSlots === 0) break;
              await sleep(300);
            }
            if (Date.now() >= budgetEnd) break;
          }
          imgStats = await evaluate(imgProbeExpr);
          const restored = await evaluate(lazyExprs.restore(JSON.stringify(saved || { containers: [], windowX: 0, windowY: 0 })));
          report.lazyScroll = report.lazyScroll || [];
          report.lazyScroll.push({
            view,
            offscreen: targetCount,
            scrolledInView: inView,
            scrolledContainers: containers,
            restoredContainers: restored,
          });
        } catch (e) {
          report.problems.push(`视图 ${view}: 懒加载滚动探测失败 ${e.message}`);
        }
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

      // 四分类终态：正常有内容 / 成功空态 / 已落地错误状态 / 仍在加载（+ 结构缺失）。
      // 前两类是"界面按预期渲染"，第三类是"界面行为正确但外部依赖失败"，
      // 第四类才是"没渲染出来"；把三者混成一个"加载中"正是本轮要修的误报。
      const ok = active === view && (state === 'content' || state === 'empty') && shotBytes > 5000;
      const outcome = ok
        ? (state === 'empty' ? 'empty' : 'ok')
        : (state === 'error' && active === view ? 'external-error'
          : ((state === 'loading' && active === view && probe && probe.exists === true) ? 'external-pending' : 'broken'));
      // HA-01：区分三件事，别再混成一句「图片未在预算内渲染」。
      //   - 明确失败：可见且 complete 但 naturalWidth=0（真破图）=> 一定进 problems。
      //   - 未完成：被统计的可见图片里还有没出结果的 => 预算内没画出来 => 进 problems。
      //   - 待填槽位：news 这类需要异步 fetch 的占位，仍按原逻辑计入 pending。
      const imagesPending = imgStats && (
        (imgStats.pendingSlots > 0)
        || (imgStats.broken > 0)
        || (imgStats.total > 0 && imgStats.loaded + imgStats.broken < imgStats.total)
      );
      report.views[view] = {
        ok,
        outcome,
        state,
        hasRetryAction: !!(probe && probe.hasAction),
        settleMs,
        imagesMs,
        imagesTotal: imgStats ? imgStats.total : 0,
        imagesLoaded: imgStats ? imgStats.loaded : 0,
        imagesBroken: imgStats ? imgStats.broken : 0,
        imageSlots: imgStats ? imgStats.slots : 0,
        pendingSlots: imgStats ? imgStats.pendingSlots : 0,
        activeNav: active,
        exists: probe ? probe.exists : false,
        textLen: probe ? probe.textLen : 0,
        sample: probe ? probe.sample : '',
        screenshot: shotPath,
        screenshotBytes: shotBytes
      };
      const tag = ok ? (state === 'empty' ? 'EMPTY' : 'OK  ')
        : (outcome === 'external-error' ? 'ERR ' : (outcome === 'external-pending' ? 'WAIT' : 'FAIL'));
      console.log(`[ui-pages] ${tag} ${view.padEnd(14)} state=${state} active=${active} textLen=${probe ? probe.textLen : 0} settle=${settleMs}ms img=${imgStats ? imgStats.loaded + '/' + imgStats.total : '-'} broken=${imgStats ? imgStats.broken : 0} slots=${imgStats ? imgStats.slots : 0}(pending=${imgStats ? imgStats.pendingSlots : 0},${imagesMs}ms) shot=${shotBytes}B`);
      if (state === 'error' && active === view) {
        report.landedErrorStates.push({ view, settleMs, sample: probe ? probe.sample : '' });
      } else if (state === 'empty' && active === view) {
        report.landedEmptyStates.push({ view, settleMs, sample: probe ? probe.sample : '' });
      } else if (outcome === 'external-pending') {
        report.externalPending.push({ view, settleMs, sample: probe ? probe.sample : '' });
      } else if (!ok) {
        report.problems.push(`视图 ${view}: state=${state} active=${active} exists=${probe && probe.exists} textLen=${probe && probe.textLen} shot=${shotBytes}`);
      }
      if (imagesPending) {
        const item = { view, total: imgStats.total, loaded: imgStats.loaded, broken: imgStats.broken, slots: imgStats.slots, pendingSlots: imgStats.pendingSlots, waitedMs: imagesMs };
        report.imagesPending.push(item);
        if (!args.allowMissingImages) {
          const reasons = [];
          if (imgStats.broken > 0) reasons.push(`可见破图 ${imgStats.broken}/${imgStats.total}`);
          if (imgStats.loaded + imgStats.broken < imgStats.total) reasons.push(`可见未完成 ${imgStats.total - imgStats.loaded - imgStats.broken}/${imgStats.total}`);
          if (imgStats.pendingSlots > 0) reasons.push(`待填槽位 ${imgStats.pendingSlots}`);
          report.problems.push(`视图 ${view}: 图片未在预算内渲染（img ${imgStats.loaded}/${imgStats.total}，${reasons.join('，')}，等 ${imagesMs}ms）`);
        }
      }
    }

    // 记录外部依赖的真实耗时（downloads 页依赖它；本机外部网络受限时以此解释 WAIT）
    try {
      const probeStart = Date.now();
      const res = await fetch(`${apiBase}/api/versions/remote`, { headers: { 'X-Pineglow-Token': cdpSessionToken } });
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
          // 第一阶段：只看 URL 形状能确定的分类。剩余的一律先按 productLocal（fail-closed）落桶，
          // 再由下一段的**完整响应正文**决定能否改判为外部依赖。
          // 注意：这里刻意不再用状态码把 502/503/504 直接当"代理超时"放行。
          const kind = classifyFailure(item, apiBase);
          if (kind === 'uiAsset') report.uiAssetFailures.push(item);
          else if (kind === 'newsImageProxy') report.newsImageProxyFailures.push(item);
          else if (kind === 'contentIconUpstream') report.contentIconFailures.push(item);
          else if (kind === 'skinImageNoAccount') report.skinImageFailures.push(item);
          else if (kind === 'cosmetic') report.cosmeticFailures.push(item);
          else if (kind === 'productLocal') report.productLocalFailures.push(item);
          else report.externalNetworkFailures.push(item);
        }
      }
    }

    // 把"只看到状态码"的本机 API 失败补上**完整响应正文**里的原因，并据此二次分类：
    //   正文明确证明上游网络错误 → 记为外部依赖（upstreamNetworkFailures，非产品问题）；
    //   否则（含未知的 500/502/503/504）→ 留在 productLocalFailures，继续阻断。
    const apiFailures = [...report.newsImageProxyFailures, ...report.contentIconFailures, ...report.skinImageFailures, ...report.productLocalFailures];
    const seen = new Set();
    for (const item of apiFailures) {
      if (report.apiFailureBodies.length >= 6) break;
      if (seen.has(item.url)) continue;
      seen.add(item.url);
      const plan = planApiFailureProbe(item.url);
      if (!plan.refetch) {
        // URL 自带敏感值：绝不重发（重发等于把 token 再送一次），只留脱敏后的形状。
        report.apiFailureBodies.push({ url: plan.url, skipped: plan.reason });
        continue;
      }
      try {
        const r = await fetch(plan.url, { headers: { 'X-Pineglow-Token': cdpSessionToken } });
        const full = await r.text();
        // 先用完整正文判断，再截断展示（证据可能在 200 字节之后）。
        const verdict = evaluateFailureBody(full);
        report.apiFailureBodies.push({
          url: plan.url,
          status: r.status,
          decision: verdict.upstreamNetwork ? 'upstreamNetwork' : 'productLocal',
          bodyLength: verdict.fullLength,
          body: verdict.display
        });
        if (verdict.upstreamNetwork) {
          // 正文证明是上游网络问题：从原来的桶里移出，改记到"外部依赖"桶，
          // 避免同一条失败同时出现在两个桶里（那会让汇总数字互相矛盾）。
          for (const bucket of [report.newsImageProxyFailures, report.contentIconFailures, report.skinImageFailures, report.productLocalFailures]) {
            const i = bucket.indexOf(item);
            if (i >= 0) bucket.splice(i, 1);
          }
          report.upstreamNetworkFailures.push({ url: item.url, text: item.text, evidence: 'response-body' });
        }
      } catch (e) {
        report.apiFailureBodies.push({ url: plan.url, error: redactSensitive(e.message) });
      }
    }

    if (report.exceptions.length > 0) report.problems.push(`未捕获 JS 异常 ${report.exceptions.length} 个`);
    if (report.consoleErrors.length > 0) report.problems.push(`console.error ${report.consoleErrors.length} 条`);
    if (report.uiAssetFailures.length > 0) report.problems.push(`界面自身资源加载失败 ${report.uiAssetFailures.length} 个（这是产品问题）`);
    if (report.productLocalFailures.length > 0) report.problems.push(`无法归类的本机接口失败 ${report.productLocalFailures.length} 个（含未知 5xx，按 fail-closed 记为产品问题）`);
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
    landedEmptyStates: report.landedEmptyStates.length,
    landedErrorStates: report.landedErrorStates.length,
    exceptions: report.exceptions.length,
    consoleErrors: report.consoleErrors.length,
    uiAssetFailures: report.uiAssetFailures.length,
    productLocalFailures: report.productLocalFailures.length,
    newsImageProxyFailures: report.newsImageProxyFailures.length,
    contentIconFailures: report.contentIconFailures.length,
    skinImageFailures: report.skinImageFailures.length,
    upstreamNetworkFailures: report.upstreamNetworkFailures.length,
    cosmeticFailures: report.cosmeticFailures.length,
    externalNetworkFailures: report.externalNetworkFailures.length
  };
  report.viewsSeen = viewCount;

  // 写盘前对**整份报告**递归脱敏，并自检会话令牌没有混进归档。
  const safeReport = redactReport(report);
  const serialized = JSON.stringify(safeReport, null, 2);
  if (cdpSessionToken && serialized.indexOf(cdpSessionToken) !== -1) {
    console.error('[ui-pages] 脱敏自检失败：报告里出现会话令牌，拒绝写出 JSON');
    process.exit(2);
  }

  if (args.json) {
    fs.mkdirSync(path.dirname(path.resolve(args.json)), { recursive: true });
    fs.writeFileSync(path.resolve(args.json), serialized, 'utf8');
  }

  console.log('');
  console.log(`UI-PAGES RESULT=${safeReport.verdict} views=${okViews}/${VIEWS.length} externalPending=${safeReport.externalPending.length} empty=${safeReport.landedEmptyStates.length} errorStates=${safeReport.landedErrorStates.length} exceptions=${safeReport.exceptions.length} consoleErrors=${safeReport.consoleErrors.length} uiAssetFailures=${safeReport.uiAssetFailures.length} productLocalFailures=${safeReport.productLocalFailures.length}`);
  console.log(`  （非致命，逐条记录）newsImageProxy=${safeReport.newsImageProxyFailures.length} contentIcon=${safeReport.contentIconFailures.length} skinImage=${safeReport.skinImageFailures.length} upstreamNetwork=${safeReport.upstreamNetworkFailures.length} cosmetic=${safeReport.cosmeticFailures.length} external=${safeReport.externalNetworkFailures.length}`);
  if (safeReport.apiFailureBodies.length > 0) {
    console.log('  API 失败响应体（用于区分策略拒绝 / 上游缺失 / 环境 DNS；判断用完整正文，展示已截断）:');
    for (const b of safeReport.apiFailureBodies) {
      const head = b.status ? String(b.status) : (b.skipped ? `SKIP(${b.skipped})` : 'ERR');
      console.log(`    ${head} [${b.decision || '-'}] ${b.body || b.error || ''}  <- ${String(b.url || '').slice(0, 80)}…`);
    }
  }
  if (safeReport.externalPending.length > 0) {
    console.log('仍在加载：外部依赖未在预算内落地（非产品失败，需在联网环境复验）:');
    for (const p of safeReport.externalPending) console.log(`  - ${p.view} 等了 ${p.settleMs}ms，内容仍是占位`);
  }
  if (safeReport.landedErrorStates.length > 0) {
    console.log('已落地错误状态（界面行为正确，数据来自外部依赖；不算通过）:');
    for (const p of safeReport.landedErrorStates) console.log(`  - ${p.view} 等了 ${p.settleMs}ms，页面已显示错误与重试入口`);
  }
  if (safeReport.landedEmptyStates.length > 0) {
    console.log('成功空态（请求成功且列表为空，属于终态，计为通过）:');
    for (const p of safeReport.landedEmptyStates) console.log(`  - ${p.view} 等了 ${p.settleMs}ms`);
  }
  if (safeReport.problems.length > 0) {
    console.log('问题:');
    for (const p of safeReport.problems) console.log('  - ' + p);
  }
  process.exit(safeReport.verdict === 'PASS' ? 0 : 1);
}

/* 只有被当作脚本直接运行时才执行验收主流程。
 * 被 node:test 以 require() 引入时只导出纯函数，便于聚焦回归测试（不启动真实 EXE、不联网）。 */
const isMainModule = require.main === module;

/* 会话令牌只在内存中用于请求头；模块级保存一份，供写报告前的脱敏自检使用。
 * 任何情况下都不写进 report。 */
let cdpSessionToken = '';

if (isMainModule) {
  main().catch((e) => { console.error('[ui-pages] 致命错误: ' + (e && e.stack ? e.stack : e)); process.exit(2); });
}

module.exports = {
  VIEWS,
  VIEW_MIN_TEXT,
  IMG_VISIBLE_FN,
  IMG_PROBE_TEMPLATE,
  buildImgProbeExpr,
  buildLazyExprs,
  LAZY_SCROLL_INDEX_FN,
  LAZY_SAVE_FN,
  LAZY_RESTORE_FN,
  LOADING_MARKERS,
  EMPTY_MARKERS,
  ERROR_MARKERS,
  ACTION_MARKERS,
  classifyViewState,
  isTerminalState,
  classifyFailure,
  hasUpstreamNetworkEvidence,
  evaluateFailureBody,
  redactSensitive,
  redactReport,
  planApiFailureProbe,
  connectCdp,
  FAILURE_BODY_DISPLAY_LIMIT
};
