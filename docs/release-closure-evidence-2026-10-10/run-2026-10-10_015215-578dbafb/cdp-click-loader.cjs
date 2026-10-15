// Focused CDP check for P-LOADERUI: does clicking the new [data-loader] button open the modal?
// Own tool; the product source is untouched. Node >= 22 (built-in fetch + WebSocket).
//
// Usage: node cdp-click-loader.cjs <appUrl> <debugPort> <outFile>
const fs = require('node:fs');

const [appUrl, debugPortRaw, outFile] = process.argv.slice(2);
const debugPort = Number(debugPortRaw);
if (!appUrl || !debugPort || !outFile) {
  console.error('usage: node cdp-click-loader.cjs <appUrl> <debugPort> <outFile>');
  process.exit(2);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const result = { probe: 'cdp-click-loader', steps: [], exceptions: [], consoleErrors: [] };

async function findPage() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${debugPort}/json/list`);
      const j = await r.json();
      const p = j.find((t) => t.type === 'page' && t.webSocketDebuggerUrl);
      if (p) return p;
    } catch {}
    await sleep(500);
  }
  throw new Error('no CDP page target');
}

function connect(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    let id = 0;
    const pending = new Map();
    ws.addEventListener('message', (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.id && pending.has(msg.id)) {
        const { res, rej } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.error ? rej(new Error(JSON.stringify(msg.error))) : res(msg.result);
        return;
      }
      if (msg.method === 'Runtime.exceptionThrown') result.exceptions.push(msg.params.exceptionDetails.text);
      if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error') {
        result.consoleErrors.push((msg.params.args || []).map((a) => a.value || a.description || '').join(' '));
      }
    });
    ws.addEventListener('error', reject);
    ws.addEventListener('open', () => {
      const send = (method, params) =>
        new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method, params })); });
      resolve({ send, close: () => ws.close() });
    });
  });
}

async function main() {
  const page = await findPage();
  const { send, close } = await connect(page.webSocketDebuggerUrl);
  await send('Runtime.enable');
  await send('Page.enable');
  await send('Page.navigate', { url: appUrl });
  await sleep(6000);

  const evaluate = async (expr) => {
    const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.text + ' ' + (r.exceptionDetails.exception?.description || ''));
    return r.result.value;
  };

  // 1) go to the installations view by clicking its sidebar entry
  const nav = await evaluate(`(() => {
    const all = [...document.querySelectorAll('*')].filter(e => e.children.length === 0);
    const el = all.find(e => (e.textContent || '').trim() === '安装管理');
    if (!el) return { found: false };
    (el.closest('button,a,li,div[role]') || el).click();
    return { found: true, tag: (el.closest('button,a,li') || el).tagName };
  })()`);
  result.steps.push({ step: 'nav to 安装管理', ...nav });
  await sleep(4000);

  // 2) is the button there?
  const btn = await evaluate(`(() => {
    const b = document.querySelector('[data-loader]');
    if (!b) return { found: false };
    return { found: true, text: (b.textContent || '').trim(), title: b.getAttribute('title'), aria: b.getAttribute('aria-label'), count: document.querySelectorAll('[data-loader]').length };
  })()`);
  result.steps.push({ step: 'find [data-loader]', ...btn });
  result.buttonText = btn.text;
  result.buttonCount = btn.count;
  if (!btn.found) { result.ok = false; fs.writeFileSync(outFile, JSON.stringify(result, null, 2)); close(); return; }

  // 3) click it and see whether a modal appears
  await evaluate(`(() => { document.querySelector('[data-loader]').click(); return true; })()`);
  await sleep(2000);
  const modal = await evaluate(`(() => {
    const title = document.querySelector('.modal-title');
    const okBtn = document.getElementById('mOk');
    const typeSel = document.getElementById('mLoaderType');
    return {
      modalPresent: !!title,
      modalTitle: title ? (title.textContent || '').trim() : null,
      hasConfirm: !!okBtn,
      hasTypeSelect: !!typeSel,
      typeOptions: typeSel ? [...typeSel.options].map(o => o.value) : [],
      hasVersionInput: !!document.getElementById('mLoaderVersion')
    };
  })()`);
  result.steps.push({ step: 'click and look for the modal', ...modal });
  result.ok = modal.modalPresent === true && modal.hasConfirm === true && modal.hasTypeSelect === true && modal.hasVersionInput === true;
  fs.writeFileSync(outFile, JSON.stringify(result, null, 2), 'utf8');
  close();
}

main().catch((e) => {
  result.error = String(e && e.message ? e.message : e);
  result.ok = false;
  try { fs.writeFileSync(outFile, JSON.stringify(result, null, 2), 'utf8'); } catch {}
  process.exit(1);
});
