'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '../..');
const validator = require(path.join(root, 'scripts/verify-ui-pages-live.cjs'));
const results = [];
async function check(id, run) {
  try { await run(); results.push({ id, status: 'PASS' }); }
  catch (e) { results.push({ id, status: 'FAIL', error: validator.redactSensitive(e.message) }); }
}
function apiHarness(routes) {
  const calls = [];
  const ctx = { SG: {}, document: { querySelector() { return null; } }, location: { protocol: 'http:' }, setTimeout, URL, URLSearchParams,
    fetch: async (url, opts) => {
      calls.push({ url, method: (opts && opts.method) || 'GET' });
      const r = routes[url];
      if (r instanceof Error) throw r;
      if (!r) throw new Error('Unexpected controlled route');
      return { ok: r.status < 400, status: r.status, json: async () => r.body };
    } };
  ctx.window = ctx; vm.createContext(ctx);
  vm.runInContext(fs.readFileSync(path.join(root, 'ui/js/api.js'), 'utf8'), ctx);
  return { ctx, calls, api: ctx.SG.api };
}
(async () => {
  await check('CV07-JSON-error-object-unicode-after-200', () => {
    const encoded = JSON.stringify({ error: { message: 'x'.repeat(350) + '不知道这样的主机' } }).replace(/[\u4e00-\u9fff]/g, c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'));
    const result = validator.evaluateFailureBody(encoded);
    assert.equal(result.upstreamNetwork, true); assert.equal(result.display.length, 200);
  });
  await check('CV07-no-excuse-from-nested-note-or-unrelated-data', () => {
    for (const body of [
      { error: { message: 'Unexpected internal invariant', note: 'DNS failure' } },
      { error: 'Internal invariant', data: { message: '不知道这样的主机' } },
      { error: 'Internal invariant', note: 'upstream connection failed' },
    ]) assert.equal(validator.evaluateFailureBody(JSON.stringify(body)).upstreamNetwork, false);
  });
  await check('CV07-all-short-auth-schemes-and-next-header-preserved', () => {
    for (const scheme of ['Basic', 'Bearer', 'Negotiate']) {
      const safe = validator.redactSensitive('Authorization: ' + scheme + ' abc\r\nContent-Type: application/json');
      assert(!safe.includes('abc')); assert(safe.includes('Content-Type: application/json'));
      assert(!validator.redactSensitive('Proxy-Authorization: ' + scheme + ' abc').includes('abc'));
    }
  });
  await check('CV07-nested-json-all-cookie-values-and-statistics', () => {
    const sentinels = ['aShort1', 'bShort2', 'cShort3', 'dShort4', 'eShort5'];
    const raw = { rows: [{ token: sentinels[0], Cookie: 'one=' + sentinels[1] + '; two=' + sentinels[2], Authorization: 'Basic ' + sentinels[3] }],
      body: JSON.stringify({ error: { token: sentinels[4] } }), sessionTokenPresent: true, authCookieCount: 2 };
    const safe = JSON.stringify(validator.redactReport(raw));
    for (const s of sentinels) assert(!safe.includes(s));
    assert.equal(validator.redactReport(raw).sessionTokenPresent, true); assert.equal(validator.redactReport(raw).authCookieCount, 2);
    const header = validator.redactSensitive('Cookie: one=' + sentinels[0] + '; two=' + sentinels[1] + '; three=' + sentinels[2] + '\nAccept: application/json');
    for (const s of sentinels.slice(0, 3)) assert(!header.includes(s)); assert(header.includes('Accept: application/json'));
  });
  await check('CV04-real-fetch-network-rejection-preserves-fallback-and-strict-local', async () => {
    const h = apiHarness({ '/api/versions': { status: 200, body: { local: ['local-one', 'local-two'] } }, '/api/versions/remote': new TypeError('controlled offline') });
    assert.equal((await h.api.getRemoteVersions()).length, 2);
    await assert.rejects(h.api.getRemoteVersions({ throwOnRemoteError: true }), e => e.localVersions.length === 2 && e.localVersions.every(v => v.installed));
  });
  await check('CV04-local-failure-never-returns-fallback-or-fetches-remote', async () => {
    for (const options of [undefined, { throwOnRemoteError: true }]) {
      const h = apiHarness({ '/api/versions': { status: 503, body: { error: 'controlled local failure' } } });
      await assert.rejects(h.api.getRemoteVersions(options)); assert.equal(h.calls.length, 1);
    }
  });
  // Reuse only the repository's controlled DOM fixture declarations. The full
  // production launcher and full actual api.js are evaluated, without a copied
  // launch implementation or SG.api.prelaunchCheck stub.
  const fixture = fs.readFileSync(path.join(root, 'ui/tests/current-version-errors.test.cjs'), 'utf8');
  const domStart = fixture.indexOf('function makeDomStub()');
  const domEnd = fixture.indexOf('function loadLauncher(', domStart);
  await check('CV08-full-launcher-plus-actual-api-failed-retry-and-warning-confirm', async () => {
    const fixtureContext = {}; vm.createContext(fixtureContext); vm.runInContext(fixture.slice(domStart, domEnd), fixtureContext);
    const { document } = fixtureContext.makeDomStub();
    const host = document.createElement('div'); host.id = 'modalHost'; document.body.appendChild(host);
    host.contains = node => { while (node) { if (node === host) return true; node = node.parent; } return false; };
    document.contains = node => Boolean(node && node.isConnected);
    const route = '/api/instances/controlled-id/prelaunch-check';
    const routes = { [route]: { status: 500, body: { error: 'controlled check failure' } }, '/api/instances/controlled-id/launch': { status: 200, body: { ok: true } } };
    const h = apiHarness(routes);
    Object.assign(h.ctx, { document, clearTimeout, requestAnimationFrame: fn => fn(), navigator: { language: 'zh-CN' }, localStorage: { getItem() { return null; }, setItem() {} }, console });
    const launcher = fs.readFileSync(path.join(root, 'ui/js/launcher.js'), 'utf8');
    const end = launcher.lastIndexOf('})();'); assert(end > 0);
    vm.runInContext(launcher.slice(0, end) + '\nSG.acceptanceLaunch = doLaunch;\n' + launcher.slice(end), h.ctx);
    await h.ctx.SG.acceptanceLaunch('controlled-id');
    assert(document.getElementById('pcRetry')); assert(!document.getElementById('pcOk'));
    await document.getElementById('pcRetry').onclick();
    assert.equal(h.calls.filter(c => c.url.endsWith('/launch')).length, 0);
    routes[route] = { status: 200, body: null };
    await document.getElementById('pcRetry').onclick(); assert.equal(h.calls.filter(c => c.url.endsWith('/launch')).length, 0);
    routes[route] = { status: 200, body: { ok: true, warnings: ['controlled warning'] } };
    await document.getElementById('pcRetry').onclick(); assert(document.getElementById('pcOk'));
    assert.equal(h.calls.filter(c => c.url.endsWith('/launch')).length, 0);
    await document.getElementById('pcOk').onclick(); assert.equal(h.calls.filter(c => c.url.endsWith('/launch')).length, 1);
  });
  const files = ['ui/js/api.js', 'ui/js/launcher.js', 'scripts/verify-ui-pages-live.cjs'];
  const hashes = Object.fromEntries(files.map(f => [f, crypto.createHash('sha256').update(fs.readFileSync(path.join(root, f))).digest('hex')]));
  const report = { generatedUtc: new Date().toISOString(), sourceRoot: root, scope: 'Controlled Node VM and actual production APIs; no native GUI or external service', hashes, checks: results, passed: results.filter(r => r.status === 'PASS').length, failed: results.filter(r => r.status === 'FAIL').length };
  fs.writeFileSync(path.join(__dirname, 'independent-agent-probes.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify({ passed: report.passed, failed: report.failed, checks: results }));
  process.exit(report.failed ? 1 : 0);
})();
