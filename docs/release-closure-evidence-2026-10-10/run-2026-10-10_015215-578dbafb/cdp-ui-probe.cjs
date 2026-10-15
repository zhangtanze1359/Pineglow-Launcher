// UI-level probe for the Pineglow launcher (own tool; product source untouched).
// Drives the SHIPPED frontend (ui/) in a real Chromium engine via the Chrome DevTools Protocol.
// Node >= 22 only: uses the built-in global fetch + WebSocket. No npm dependencies.
//
// Usage: node cdp-ui-probe.cjs <appUrl> <debugPort> <outDir>
//   appUrl     e.g. http://127.0.0.1:51234/
//   debugPort  the --remote-debugging-port given to chrome
//   outDir     where probe-result.json / ui-dom.html / ui-shot.png are written

const fs = require('node:fs');
const path = require('node:path');

const [appUrl, debugPortRaw, outDir] = process.argv.slice(2);
const debugPort = Number(debugPortRaw);
if (!appUrl || !debugPort || !outDir) {
  console.error('usage: node cdp-ui-probe.cjs <appUrl> <debugPort> <outDir>');
  process.exit(2);
}
fs.mkdirSync(outDir, { recursive: true });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function listTargets() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${debugPort}/json/list`);
      const j = await r.json();
      const page = j.find((t) => t.type === 'page' && t.webSocketDebuggerUrl);
      if (page) return page;
    } catch { /* chrome not up yet */ }
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
    } else if (msg.method) {
      events.push(msg);
    }
  });
  const send = (method, params = {}) =>
    new Promise((resolve, reject) => {
      const myId = ++id;
      pending.set(myId, { resolve, reject });
      ws.send(JSON.stringify({ id: myId, method, params }));
    });
  return { send, events };
}

(async () => {
  const result = {
    probe: 'cdp-ui-probe',
    appUrl,
    debugPort,
    timeHK: new Date().toISOString(),
    ok: false,
    steps: [],
    exceptions: [],
    consoleErrors: [],
    consoleWarnings: [],
    failedRequests: [],
    httpErrors: [],
  };
  const step = (name, ok, detail) => {
    result.steps.push({ name, ok, detail });
    if (!ok) result.ok = false;
  };

  const target = await listTargets();
  result.targetUrl = target.url;
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((res, rej) => {
    ws.addEventListener('open', res);
    ws.addEventListener('error', rej);
  });
  const { send, events } = cdp(ws);

  await send('Runtime.enable');
  await send('Log.enable');
  await send('Page.enable');
  await send('Network.enable');

  // Navigate from scratch so nothing before attach is missed.
  await send('Page.navigate', { url: appUrl });
  // Wait for load, then let the SPA fetch data from the local API.
  const deadline = Date.now() + 30000;
  let loaded = false;
  while (Date.now() < deadline) {
    if (events.some((e) => e.method === 'Page.loadEventFired')) { loaded = true; break; }
    await sleep(200);
  }
  step('页面 load 事件', loaded, loaded ? 'Page.loadEventFired received' : 'no load event within 30s');
  await sleep(6000); // let the SPA's own fetches settle

  for (const e of events) {
    if (e.method === 'Runtime.exceptionThrown') {
      const d = e.params.exceptionDetails;
      result.exceptions.push({
        text: d.text,
        description: d.exception && d.exception.description,
        url: d.url,
        line: d.lineNumber,
      });
    } else if (e.method === 'Runtime.consoleAPICalled') {
      const text = (e.params.args || [])
        .map((a) => (a.value !== undefined ? String(a.value) : a.description || a.type))
        .join(' ');
      if (e.params.type === 'error') result.consoleErrors.push(text);
      else if (e.params.type === 'warning') result.consoleWarnings.push(text);
    } else if (e.method === 'Network.loadingFailed') {
      result.failedRequests.push({ errorText: e.params.errorText, type: e.params.type });
    } else if (e.method === 'Network.responseReceived') {
      const s = e.params.response.status;
      if (s >= 400) result.httpErrors.push({ status: s, url: e.params.response.url });
    }
  }

  const evaluate = async (expr) => {
    const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    return r.result ? r.result.value : undefined;
  };

  result.title = await evaluate('document.title');
  result.readyState = await evaluate('document.readyState');
  result.bodyTextLength = await evaluate('(document.body && document.body.innerText || "").length');
  result.bodyTextHead = await evaluate('(document.body && document.body.innerText || "").slice(0, 3000)');
  result.elementCount = await evaluate('document.querySelectorAll("*").length');
  result.hasTokenMeta = await evaluate('!!document.querySelector(\'meta[name="pineglow-session-token"]\')');
  result.apiHelperPresent = await evaluate('typeof window.SG !== "undefined"');
  result.navItems = await evaluate('Array.from(document.querySelectorAll("[data-view],[data-page],nav a,nav button")).map(e=>e.getAttribute("data-view")||e.getAttribute("data-page")||(e.textContent||"").trim()).filter(Boolean).slice(0,40)');
  result.dynamicMarkers = await evaluate(`(() => {
    const t = (document.body && document.body.innerText) || '';
    const probes = {
      hasVersionNumber: /\\b1\\.[0-9]+(\\.[0-9]+)?\\b/.test(t),
      hasMCVersionList: /1\\.21|1\\.20|1\\.19|1\\.7\\.10/.test(t),
      hasInstanceName: /d7-|实例/.test(t),
      hasDownloadText: /下载|安装/.test(t),
      hasSettingsText: /设置|Java|内存/.test(t),
      hasAccountText: /账号|登录/.test(t),
    };
    return probes;
  })()`);
  result.htmlLength = await evaluate('document.documentElement.outerHTML.length');

  const html = await evaluate('document.documentElement.outerHTML');
  if (typeof html === 'string') fs.writeFileSync(path.join(outDir, 'ui-dom.html'), html, 'utf8');

  try {
    const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
    fs.writeFileSync(path.join(outDir, 'ui-shot.png'), Buffer.from(shot.data, 'base64'));
    result.screenshotBytes = Buffer.from(shot.data, 'base64').length;
  } catch (e) {
    result.screenshotError = String(e && e.message);
  }

  step('页面标题非空', !!result.title, String(result.title));
  step('DOM 已渲染元素', (result.elementCount || 0) > 50, `elements=${result.elementCount}`);
  step('SPA 已把内容渲染进 DOM', (result.bodyTextLength || 0) > 200, `bodyTextLength=${result.bodyTextLength}`);
  step('会话令牌 meta 存在', !!result.hasTokenMeta, String(result.hasTokenMeta));
  step('无未捕获异常', result.exceptions.length === 0, `exceptions=${result.exceptions.length}`);
  step('无 console.error', result.consoleErrors.length === 0, `consoleErrors=${result.consoleErrors.length}`);
  step('无 HTTP 4xx/5xx', result.httpErrors.length === 0, `httpErrors=${result.httpErrors.length}`);
  result.ok = result.steps.every((s) => s.ok);

  fs.writeFileSync(path.join(outDir, 'ui-probe-result.json'), JSON.stringify(result, null, 2), 'utf8');
  console.log(JSON.stringify({
    ok: result.ok,
    title: result.title,
    elementCount: result.elementCount,
    bodyTextLength: result.bodyTextLength,
    exceptions: result.exceptions.length,
    consoleErrors: result.consoleErrors.length,
    httpErrors: result.httpErrors.length,
    failedRequests: result.failedRequests.length,
    dynamicMarkers: result.dynamicMarkers,
    steps: result.steps,
  }, null, 2));
  try { ws.close(); } catch { }
  process.exit(result.ok ? 0 : 1);
})().catch((e) => {
  console.error('PROBE_ERROR: ' + (e && e.stack || e));
  process.exit(3);
});
