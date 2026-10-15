// Navigation sweep for the Pineglow launcher UI (own tool; product source untouched).
// Clicks every sidebar entry in the SHIPPED frontend and captures a screenshot + visible text per view.
// Node >= 22 only (built-in fetch + WebSocket). No npm dependencies.
//
// Usage: node cdp-ui-nav-sweep.cjs <appUrl> <debugPort> <outDir>

const fs = require('node:fs');
const path = require('node:path');

const [appUrl, debugPortRaw, outDir, labelFilterRaw, settleRaw] = process.argv.slice(2);
const debugPort = Number(debugPortRaw);
const settleMs = Number(settleRaw) > 0 ? Number(settleRaw) : 2500;
const labelFilter = labelFilterRaw ? labelFilterRaw.split(',').map((s) => s.trim()).filter(Boolean) : null;
if (!appUrl || !debugPort || !outDir) {
  console.error('usage: node cdp-ui-nav-sweep.cjs <appUrl> <debugPort> <outDir>');
  process.exit(2);
}
fs.mkdirSync(outDir, { recursive: true });
const shotsDir = path.join(outDir, 'views');
fs.mkdirSync(shotsDir, { recursive: true });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function listTargets() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${debugPort}/json/list`);
      const j = await r.json();
      const page = j.find((t) => t.type === 'page' && t.webSocketDebuggerUrl);
      if (page) return page;
    } catch { }
    await sleep(500);
  }
  throw new Error('no debuggable page target appeared');
}

function cdp(ws) {
  let id = 0;
  const pending = new Map();
  const events = [];
  ws.addEventListener('message', (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) reject(new Error(JSON.stringify(msg.error)));
      else resolve(msg.result);
    } else if (msg.method) events.push(msg);
  });
  const send = (method, params = {}) =>
    new Promise((resolve, reject) => {
      const myId = ++id;
      pending.set(myId, { resolve, reject });
      ws.send(JSON.stringify({ id: myId, method, params }));
    });
  return { send, events };
}

const NAV_LABELS = labelFilter || ['开始游戏', '安装管理', '世界', '服务器', '内容中心', '皮肤', '下载', '新闻', '设置'];
const safeName = (s) => s.replace(/[^0-9A-Za-z\u4e00-\u9fa5]+/g, '_');

(async () => {
  const result = {
    probe: 'cdp-ui-nav-sweep', appUrl, debugPort,
    timeHK: new Date().toISOString(),
    ok: false, navLabels: NAV_LABELS, views: [],
    exceptions: [], consoleErrors: [], httpErrors: [],
  };

  const target = await listTargets();
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  const { send, events } = cdp(ws);

  await send('Runtime.enable');
  await send('Log.enable');
  await send('Page.enable');
  await send('Network.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 940, deviceScaleFactor: 1, mobile: false });

  await send('Page.navigate', { url: appUrl });
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if (events.some((e) => e.method === 'Page.loadEventFired')) break;
    await sleep(200);
  }
  await sleep(7000);

  const evaluate = async (expr) => {
    const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) return { __error: r.exceptionDetails.text };
    return r.result ? r.result.value : undefined;
  };

  // Discover clickable sidebar entries once, so the sweep is driven by the real DOM.
  const discovered = await evaluate(`(() => {
    const out = [];
    const seen = new Set();
    document.querySelectorAll('*').forEach(el => {
      const t = (el.textContent || '').trim();
      if (!t || t.length > 12) return;
      if (el.children.length > 0 && ![...el.children].every(c => (c.textContent||'').trim() === '')) {
        // keep only leaf-ish nodes
        if (el.children.length > 1) return;
      }
      const r = el.getBoundingClientRect();
      if (r.width < 20 || r.height < 14 || r.left > 300) return;   // sidebar only
      if (seen.has(t)) return;
      seen.add(t); out.push(t);
    });
    return out;
  })()`);
  result.sidebarCandidates = discovered;

  const clickByText = (label) => evaluate(`(() => {
    const label = ${JSON.stringify('__L__')}.replace('__L__', ${JSON.stringify(label)});
    const nodes = Array.from(document.querySelectorAll('*'));
    const cands = nodes.filter(el => {
      const t = (el.textContent || '').trim();
      if (t !== label) return false;
      const r = el.getBoundingClientRect();
      return r.width >= 20 && r.height >= 14 && r.left <= 300;
    });
    if (!cands.length) return { clicked: false, reason: 'not found' };
    // deepest match = the actual clickable row
    cands.sort((a, b) => b.querySelectorAll('*').length - a.querySelectorAll('*').length);
    const el = cands[cands.length - 1];
    el.scrollIntoView({ block: 'center' });
    const r = el.getBoundingClientRect();
    const cx = Math.round(r.left + r.width / 2), cy = Math.round(r.top + r.height / 2);
    for (const type of ['pointerdown','mousedown','pointerup','mouseup','click']) {
      el.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, clientX: cx, clientY: cy, view: window }));
    }
    return { clicked: true, x: cx, y: cy };
  })()`);

  for (const label of NAV_LABELS) {
    const before = events.length;
    const clicked = await clickByText(label);
    await sleep(settleMs);
    const text = await evaluate('(document.body && document.body.innerText || "")');
    const activeNav = await evaluate(`(() => {
      // the highlighted sidebar row: highest background-alpha / accent color among the nav rows
      const rows = Array.from(document.querySelectorAll('*')).filter(el => {
        const t = (el.textContent || '').trim();
        if (!t || t.length > 12) return false;
        const r = el.getBoundingClientRect();
        return r.width >= 20 && r.height >= 14 && r.left <= 300;
      });
      const scored = rows.map(el => {
        const cs = getComputedStyle(el);
        const bg = cs.backgroundColor || '';
        const m = bg.match(/rgba?\\(([^)]+)\\)/);
        const a = m ? (m[1].split(',').length > 3 ? parseFloat(m[1].split(',')[3]) : 1) : 0;
        const lum = (() => { const p = m ? m[1].split(',').map(Number) : [0,0,0]; return (p[0]+p[1]+p[2])/3; })();
        return { t: el.textContent.trim(), score: a * (1 + lum / 255), bg };
      }).filter(s => s.score > 0.12).sort((a,b) => b.score - a.score);
      return scored.slice(0, 2).map(s => s.t);
    })()`);
    const mainText = await evaluate(`(() => {
      const main = document.querySelector('main,[class*=content],[class*=view],[class*=page]');
      if (!main) return '';
      return (main.innerText || '').trim().slice(0, 600);
    })()`);
    const inFlight = await evaluate('document.readyState');
    const heading = await evaluate('(() => { const h = document.querySelector("h1,h2,.view-title,[class*=title]"); return h ? (h.textContent||"").trim().slice(0,80) : ""; })()');
    let shotBytes = 0;
    try {
      const shot = await send('Page.captureScreenshot', { format: 'png' });
      const buf = Buffer.from(shot.data, 'base64');
      fs.writeFileSync(path.join(shotsDir, `${safeName(label)}.png`), buf);
      shotBytes = buf.length;
    } catch { }
    const newEvents = events.slice(before);
    const viewExceptions = newEvents.filter((e) => e.method === 'Runtime.exceptionThrown').length;
    const viewConsoleErr = newEvents.filter((e) => e.method === 'Runtime.consoleAPICalled' && e.params.type === 'error').length;
    const viewHttpErr = newEvents
      .filter((e) => e.method === 'Network.responseReceived')
      .map((e) => e.params.response)
      .filter((r) => r.status >= 400 && !/favicon\.ico$/.test(r.url))
      .map((r) => `${r.status} ${r.url}`);
    result.views.push({
      label, clicked, heading,
      activeNav, mainTextHead: (mainText || '').slice(0, 400), readyState: inFlight,
      bodyTextLength: (text || '').length,
      distinctiveLine: (text || '').split('\n').map((s) => s.trim()).filter((s) => s.length > 3).slice(0, 14),
      screenshotBytes: shotBytes,
      exceptions: viewExceptions,
      consoleErrors: viewConsoleErr,
      httpErrors: viewHttpErr,
    });
  }

  for (const e of events) {
    if (e.method === 'Runtime.exceptionThrown') result.exceptions.push(e.params.exceptionDetails.text);
    else if (e.method === 'Runtime.consoleAPICalled' && e.params.type === 'error') {
      result.consoleErrors.push((e.params.args || []).map((a) => a.value ?? a.description ?? a.type).join(' '));
    } else if (e.method === 'Network.responseReceived' && e.params.response.status >= 400 && !/favicon\.ico$/.test(e.params.response.url)) {
      result.httpErrors.push(`${e.params.response.status} ${e.params.response.url}`);
    }
  }

  const visited = result.views.filter((v) => v.clicked && v.clicked.clicked && v.bodyTextLength > 150).length;
  const withErrors = result.views.filter((v) => v.exceptions > 0 || v.consoleErrors > 0).length;
  result.visitedViewCount = visited;
  result.viewsWithJsErrors = withErrors;
  result.ok = visited >= 8 && withErrors === 0 && result.exceptions.length === 0;

  fs.writeFileSync(path.join(outDir, 'ui-nav-sweep.json'), JSON.stringify(result, null, 2), 'utf8');
  console.log(JSON.stringify({
    ok: result.ok,
    visitedViewCount: visited,
    viewsWithJsErrors: withErrors,
    exceptions: result.exceptions.length,
    views: result.views.map((v) => ({
      label: v.label, clicked: v.clicked.clicked, heading: v.heading,
      textLen: v.bodyTextLength, shot: v.screenshotBytes, jsErr: v.exceptions, httpErr: v.httpErrors.length,
    })),
  }, null, 2));
  try { ws.close(); } catch { }
  process.exit(result.ok ? 0 : 1);
})().catch((e) => { console.error('PROBE_ERROR: ' + (e && e.stack || e)); process.exit(3); });
