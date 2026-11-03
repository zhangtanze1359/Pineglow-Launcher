'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const sourceDir = process.env.PINEGLOW_UI_TEST_SOURCE_DIR || path.join(root, 'ui/js');
const fixture = fs.readFileSync(path.join(root, 'ui/tests/current-version-errors.test.cjs'), 'utf8');
const dom = fixture.slice(fixture.indexOf('function makeDomStub()'), fixture.indexOf('function loadLauncher(handlers)'));
const flush = async () => { for (let i = 0; i < 25; i++) await Promise.resolve(); };

function liveLauncher(options = {}) {
	const raw = { config: { selectedInstanceId: 'b', selectedAccountId: 'account-b', maxMemoryMb: 4096, useAutoMemory: true,
		downloadSource: 'bmclapi', useMirror: true, proxyEnabled: true, proxyHost: '127.0.0.1', proxyPort: 7890,
		proxyUsername: 'audit-user', proxyPasswordConfigured: true, newsFakeIpCompat: true,
		minimizeOnLaunch: true, autoCloseOnExit: true, curseForgeApiKeyConfigured: true, aiDiagnosisEnabled: false,
		...(options.config || {}) },
		instances: [{ id: 'a', name: 'Instance A', minecraftVersion: '1.21.1', loaderType: 'Vanilla' },
			{ id: 'b', name: 'Instance B', minecraftVersion: '1.21.1', loaderType: 'Fabric' }], accounts: [], java: [] };
	const calls = [], failures = new Set();
	const ctx = { SG: {}, console, setTimeout: (fn, ms) => { const t = setTimeout(fn, ms); t.unref(); return t; }, clearTimeout,
		setInterval: (fn, ms) => { const t = setInterval(fn, ms); t.unref(); return t; }, clearInterval,
		location: { protocol: 'http:', hash: '' }, history: { replaceState() {} }, navigator: { language: 'zh-CN' },
		localStorage: { getItem() { return null; }, setItem() {} }, URL, URLSearchParams,
		requestAnimationFrame: fn => fn(), getComputedStyle: () => ({ width: '30px', borderLeftWidth: '0', borderRightWidth: '0' }),
		addEventListener() {}, confirm: () => true };
	ctx.window = ctx;
	vm.createContext(ctx); vm.runInContext(dom, ctx);
	const { document } = ctx.makeDomStub(); ctx.document = document;
	document.contains = node => !!node?.isConnected;
	const originalCreate = document.createElement.bind(document);
	document.createElement = tag => {
		const el = originalCreate(tag);
		const setAttribute = el.setAttribute.bind(el), removeAttribute = el.removeAttribute.bind(el);
		el.setAttribute = (name, value) => {
			setAttribute(name, value);
			if (name === 'value') el.value = String(value);
			if (name === 'disabled') el.disabled = true;
		};
		el.removeAttribute = name => { removeAttribute(name); if (name === 'disabled') el.disabled = false; };
		el.contains = node => { while (node) { if (node === el) return true; node = node.parent; } return false; };
		return el;
	};
	for (const id of ['contentPanel', 'modalHost', 'toast', ...['home', 'settings', 'worlds', 'servers', 'downloads', 'news'].map(n => 'view-' + n)]) {
		const el = document.createElement('div'); el.id = id;
		if (id.startsWith('view-')) el.className = 'view';
		if (id === 'modalHost') el.hidden = true;
		document.body.appendChild(el);
	}
	ctx.fetch = async (url, init = {}) => {
		const pathname = String(url).split('?')[0], body = init.body ? JSON.parse(init.body) : null;
		calls.push({ path: pathname, method: init.method || 'GET', body });
		let data;
		if (options.respond) data = await options.respond(pathname, init, raw);
		if (failures.has(pathname)) return { ok: false, status: 503, json: async () => ({ error: '<img src=x onerror=oops> unavailable' }) };
		if (data === undefined) {
			if (pathname === '/api/state') data = structuredClone(raw);
			else if (pathname === '/api/config') { Object.assign(raw.config, body); data = { ok: true }; }
			else if (pathname === '/api/sources') data = { selected: raw.config.downloadSource, sources: [{ key: 'official', available: true }, { key: 'bmclapi', available: true }] };
			else if (pathname === '/api/sources/select') { raw.config.downloadSource = body.key; raw.config.useMirror = body.key !== 'official'; data = { ok: true, selected: body.key, useMirror: raw.config.useMirror }; }
			else if (pathname === '/api/instances') data = { instances: raw.instances, selectedInstanceId: raw.config.selectedInstanceId };
			else if (pathname === '/api/worlds') data = { worlds: [{ instanceId: 'b', folderName: 'World B', name: 'World B' }] };
			else if (pathname === '/api/servers') data = { servers: [{ id: 's1', name: 'Server One', host: 'example.invalid', port: 25565 }] };
			else if (pathname === '/api/update') data = { state: 'idle', currentVersion: '1.0.0' };
			else if (pathname === '/api/news') data = { items: [], meta: { state: 'empty' } };
			else data = { ok: true, accounts: [], tasks: [], totalPlaySeconds: 0, totalPlayCount: 0 };
		}
		return { ok: true, status: 200, json: async () => data };
	};
	for (const file of ['i18n.js', 'api.js', 'launcher.js']) vm.runInContext(fs.readFileSync(path.join(sourceDir, file), 'utf8'), ctx);
	async function click(el) {
		if (!el) throw new Error('Expected a real rendered control');
		const event = { target: el, currentTarget: el, preventDefault() {}, stopPropagation() {} };
		if (el.onclick) await el.onclick(event);
		for (const entry of el._listeners.click || []) await entry.fn.call(el, event);
		await flush();
	}
	return { ctx, document, T: ctx.SG.__test__, api: ctx.SG.api, raw, calls, failures, click, flush,
		get: id => document.getElementById(id), go: nav => ctx.SG.__test__.goTo(nav) };
}
module.exports = { liveLauncher, flush };
