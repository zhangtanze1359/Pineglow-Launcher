'use strict';
/*
 * api.js 行为契约测试
 * --------------------------------------------------------------------------
 * 上一轮验收发现的 bug：
 *   - contentSource 私有变量被 api.js 引用 → ReferenceError
 *   - 正式模式 catch 自动回退 mock → Sodium、Iris Shaders 等伪造项目
 *   - 文档写"11+15=26 通过"，但脚本用 setTimeout(process.exit,50) 强制退出
 *     把第 12 个 60ms 异步测试（allowMockFallback）提前 kill，实际跑了 11 个。
 *
 * 本文件用 Node 内置 node:test + node:assert/strict 重写：
 *   - 不再有 setTimeout 强制退出；node:test 自己的运行器负责等待所有 promise。
 *   - 断言失败 → 进程退出非零（node:test 原生行为）。
 *   - 每个测试都跑过 12 个；异步失败会被等待并报告。
 *
 * 覆盖范围（只测 api.js + i18n.js，不声称这是 launcher.js / 端到端）：
 *   - live / file 协议下 apiMode 决定
 *   - wrapped._allowMockFallback 暴露与默认 false
 *   - searchContent 透传 source / gameVersion / loader
 *   - searchContent 默认 modrinth、非法值规整
 *   - searchContent 在 live 失败时不回退 mock
 *   - getState 失败时不回退 mock
 *   - 5xx 响应抛错
 *   - 显式 allowMockFallback=true 才回退，并把 _liveError 挂上
 *   - i18n 中英文切换
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

/* ============== 最小浏览器桩：ctx === window，全局直接 ============== */
function makeStubBrowser(opts) {
	opts = opts || {};
	const elements = new Map();
	const document = {
		head: { appendChild() {} },
		body: { appendChild() {} },
		documentElement: { setAttribute() {}, getAttribute() { return ''; } },
		querySelector(sel) { return elements.get(sel) || null; },
		createElement(tag) {
			const el = {
				tagName: (tag || 'div').toUpperCase(),
				children: [],
				classList: { _add: [], add() {}, remove() {}, contains() { return false; } },
				attributes: {},
				dataset: {},
				style: {},
				setAttribute(k, v) { this.attributes[k] = v; this.dataset[k] = v; },
				getAttribute(k) { return this.attributes[k]; },
				addEventListener() {}, appendChild(c) { this.children.push(c); return c; },
				remove() {}, innerHTML: '', textContent: '', value: '', disabled: false,
			};
			el.dataset = new Proxy({}, { set(t, k, v) { t[k] = v; el.attributes['data-' + k] = v; return true; }, get(t, k) { return t[k]; } });
			return el;
		},
		addEventListener() {},
	};
	const fetchCalls = [];
	const fetchStub = (url, init) => {
		fetchCalls.push({ url: String(url), init: init || {} });
		if (opts.fetchHandler) return opts.fetchHandler(url, init);
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
	};
	const ctx = {
		SG: {},
		location: { protocol: 'http:' },
		fetch: fetchStub,
		document,
		navigator: { language: 'en-US', userLanguage: 'en-US' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		setTimeout, clearTimeout,
		console,
		URLSearchParams,
		Promise,
		fetchCalls,
	};
	// window === globalThis === ctx：让 api.js 的 window.SG 与裸 SG 引用都能解析
	Object.defineProperty(ctx, 'window', {
		get() { return this; },
		set(v) { /* 防止被覆盖 */ },
		configurable: true,
	});
	return { ctx, document, fetchStub, fetchCalls };
}

function loadApi(fetchHandler, extraSGProps) {
	const stub = makeStubBrowser({ fetchHandler });
	stub.ctx.SG.apiMode = 'live';
	stub.ctx.SG.apiToken = 'test-token';
	if (extraSGProps) Object.assign(stub.ctx.SG, extraSGProps);
	vm.createContext(stub.ctx);
	vm.runInContext(API_JS, stub.ctx);
	return { api: stub.ctx.SG.api, stub };
}

function loadApiRaw(ctx) {
	vm.createContext(ctx);
	vm.runInContext(API_JS, ctx);
	return ctx.SG.api;
}

function loadI18nRaw(ctx) {
	vm.createContext(ctx);
	vm.runInContext(I18N_JS, ctx);
	return ctx.SG;
}

/* ============== 1. apiMode 决定 ============== */
test('api.js 在 http: 协议下默认是 live 模式', () => {
	const stub = makeStubBrowser();
	stub.ctx.location.protocol = 'http:';
	const api = loadApiRaw(stub.ctx);
	assert.equal(stub.ctx.SG.apiMode, 'live');
	assert.equal(api._implMode, 'live');
});

test('api.js 在 file:// 协议下是 mock 模式', () => {
	const stub = makeStubBrowser();
	stub.ctx.location.protocol = 'file:';
	const api = loadApiRaw(stub.ctx);
	assert.equal(stub.ctx.SG.apiMode, 'mock');
	assert.equal(api._implMode, 'mock');
});

/* ============== 2. wrapped 元数据 ============== */
test('wrapped 暴露 _implMode 与 _allowMockFallback，且默认 false', () => {
	const { api } = loadApi();
	assert.equal(api._implMode, 'live');
	assert.equal(api._allowMockFallback, false);
});

/* ============== 3-5. searchContent 透传与规整 ============== */
test('live 模式下 searchContent 透传 source=curseforge', async () => {
	let capturedUrl = null;
	const { api } = loadApi((url) => {
		capturedUrl = String(url);
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
	});
	await api.searchContent('sodium', 'mod', { source: 'curseforge', gameVersion: '1.21.1', loader: 'Fabric' });
	assert.ok(capturedUrl.includes('source=curseforge'), 'URL 应包含 source=curseforge, got: ' + capturedUrl);
	assert.ok(capturedUrl.includes('category=mod'));
	assert.ok(capturedUrl.includes('q=sodium'));
	assert.ok(capturedUrl.includes('gameVersion=1.21.1'));
	assert.ok(capturedUrl.includes('loader=Fabric'));
});

test('live 模式下 searchContent 不传 source 默认 modrinth 且不抛 ReferenceError', async () => {
	let capturedUrl = null;
	const { api } = loadApi((url) => {
		capturedUrl = String(url);
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
	});
	await api.searchContent('sodium', 'mod', { gameVersion: '', loader: '' });
	assert.ok(capturedUrl.includes('source=modrinth'), '默认 source 应为 modrinth, got: ' + capturedUrl);
});

test('live 模式下 searchContent 非法 source 被规整为 modrinth，URL 不注入原始串', async () => {
	let capturedUrl = null;
	const { api } = loadApi((url) => {
		capturedUrl = String(url);
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
	});
	await api.searchContent('x', 'mod', { source: '<script>alert(1)</script>' });
	assert.ok(capturedUrl.includes('source=modrinth'), '非法 source 应被规整');
	assert.ok(!capturedUrl.includes('<script>'), 'URL 中不得注入原始字符串');
});

/* ============== 6-7. live 失败不静默回退 mock ============== */
test('live 模式 searchContent 在 fetch reject 时必须抛错，不能回退 mock', async () => {
	const { api } = loadApi(() => Promise.reject(new Error('HTTP 500')));
	await assert.rejects(
		api.searchContent('sodium', 'mod', { source: 'modrinth' }),
		/HTTP 500/,
	);
});

test('live 模式 getState 在 fetch reject 时必须抛错，不能回退 mock', async () => {
	const { api } = loadApi(() => Promise.reject(new Error('network down')));
	await assert.rejects(
		api.getState(),
		/network down/,
	);
});

/* ============== 8. 5xx 响应 ============== */
test('live 模式 5xx 响应时 _req 抛错，wrapped 不回退', async () => {
	const { api } = loadApi(() => Promise.resolve({
		ok: false, status: 502,
		json: () => Promise.resolve({ error: 'upstream' }),
	}));
	await assert.rejects(
		api.searchContent('sodium', 'mod', { source: 'modrinth' }),
		/upstream/,
	);
});

/* ============== 9. 显式 allowMockFallback 才回退，且挂 _liveError ============== */
test('显式打开 allowMockFallback 后，live 失败才会回退，并把错误挂在结果上', async () => {
	const fetchHandler = () => Promise.reject(new Error('HTTP 502'));
	const stub = makeStubBrowser({ fetchHandler });
	stub.ctx.SG.apiAllowMockFallback = true;
	stub.ctx.SG.apiMode = 'live';
	stub.ctx.SG.apiToken = 'test-token';
	vm.createContext(stub.ctx);
	vm.runInContext(API_JS, stub.ctx);
	const api = stub.ctx.SG.api;
	const value = await api.searchContent('sodium', 'mod', { source: 'modrinth' });
	assert.ok(Array.isArray(value), '回退结果应为数组 (mock.searchContent 返回数组)');
	assert.ok(value._liveError, '回退结果应包含 _liveError');
	assert.match(value._liveError, /HTTP 502/);
});

/* ============== 10. results 包含 source / category ============== */
test('searchContent 返回 results 字段中包含 source / category', async () => {
	const fakeResults = [
		{ id: 'AANobbMI', title: 'Sodium', desc: 'd', author: 'jelly', downloads: 1000, category: 'mod', source: 'modrinth', versions: ['1.21.x'], loader: 'Fabric', icon: 'http://x/i.png' },
		{ id: 'XYZ', title: 'Iris', desc: 'd', author: 'cb', downloads: 500, category: 'mod', source: 'modrinth', versions: ['1.21.x'], loader: 'Fabric', icon: '' },
	];
	const { api } = loadApi(() => Promise.resolve({
		ok: true, status: 200, json: () => Promise.resolve({ results: fakeResults })
	}));
	const list = await api.searchContent('sodium', 'mod', { source: 'modrinth' });
	assert.equal(list.length, 2);
	assert.equal(list[0].source, 'modrinth');
	assert.equal(list[0].category, 'mod');
	assert.equal(list[1].source, 'modrinth');
});

/* ============== 11. i18n 中英文 ============== */
test('i18n.js 加载后 SG.t 返回中文/英文', () => {
	const stub = makeStubBrowser();
	const sg = loadI18nRaw(stub.ctx);
	assert.equal(typeof sg.t, 'function');
	// 默认 navigator.language='en-US' → 走英文，'nav.home' 译为 'Play'
	assert.equal(sg.t('nav.home'), 'Play');
	// 切换到中文
	sg.setLocale('zh');
	assert.equal(sg.t('nav.home'), '开始游戏');
	// 切换回英文
	sg.setLocale('en');
	assert.equal(sg.t('nav.home'), 'Play');
});

/* ============== 12. 内容中心新文案存在（中英） ============== */
test('i18n.js 含 content.retry / installMissingSource / installMissingCategory / unfiltered / stateLoadFailed', () => {
	const stub = makeStubBrowser();
	const sg = loadI18nRaw(stub.ctx);
	// 中英文都要求存在
	sg.setLocale('zh');
	assert.equal(typeof sg.t('content.retry'), 'string');
	assert.ok(sg.t('content.retry').length > 0);
	assert.equal(typeof sg.t('content.installMissingSource'), 'string');
	assert.equal(typeof sg.t('content.installMissingCategory'), 'string');
	assert.equal(typeof sg.t('content.unfiltered'), 'string');
	assert.equal(typeof sg.t('content.stateLoadFailed'), 'string');
	sg.setLocale('en');
	assert.equal(typeof sg.t('content.retry'), 'string');
	assert.ok(sg.t('content.retry').length > 0);
	assert.equal(typeof sg.t('content.installMissingSource'), 'string');
	assert.equal(typeof sg.t('content.installMissingCategory'), 'string');
	assert.equal(typeof sg.t('content.unfiltered'), 'string');
	assert.equal(typeof sg.t('content.stateLoadFailed'), 'string');
});

/* ============== 13. R2-02：SG.api.updateConfig 契约 ==============
 * 只测请求路径、方法、token、JSON 字段契约；不涉及 launcher.js 交互行为。
 * 交互行为（点击、回滚、连点）放在 news-behavior.test.cjs。
 */

test('R2-02: mock 默认配置 newsFakeIpCompat = false（real contract）', () => {
	const stub = makeStubBrowser();
	stub.ctx.SG.apiMode = 'mock';
	vm.createContext(stub.ctx);
	vm.runInContext(API_JS, stub.ctx);
	const ms = stub.ctx.SG.mock && stub.ctx.SG.mock.settings;
	assert.ok(ms, 'SG.mock.settings 必须存在');
	assert.ok('newsFakeIpCompat' in ms, 'mock.settings 必须含 newsFakeIpCompat 字段');
	assert.equal(ms.newsFakeIpCompat, false, 'mock.settings.newsFakeIpCompat 默认必须为 false');
});

test('R2-02: SG.api.updateConfig({newsFakeIpCompat:true}) 必须 POST /api/config 带 token + 准确字段', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		// 区分 /api/config POST 与 /api/state GET
		if (u === '/api/state') {
			return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({
				version: '1.0.0', pid: 1,
				config: { gameDirectory: '', javaPath: '', playerName: 'P', maxMemoryMb: 4096, useAutoMemory: false, windowWidth: 1280, windowHeight: 720, serverHost: '', serverPort: '25565', useMirror: false, proxyEnabled: false, proxyHost: '', proxyPort: 0, proxyUsername: '', proxyPasswordConfigured: false, minimizeOnLaunch: false, autoCloseOnExit: false, selectedVersion: '', curseForgeApiKeyConfigured: false, selectedInstanceId: '', selectedAccountId: '', accountMode: 'offline', aiDiagnosisEnabled: false, selectedAiProviderId: '', aiProviders: [] },
				instances: [], accounts: [], java: [],
			}) });
		}
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ ok: true }) });
	});
	// api.js 加载时会重置 apiToken；必须在加载后重新设
	stub.ctx.SG.apiToken = 'test-token';
	await api.updateConfig({ newsFakeIpCompat: true });
	const postCall = calls.find(c => c.url === '/api/config');
	assert.ok(postCall, '必须发起到 /api/config 的请求');
	assert.equal((postCall.init.method || '').toUpperCase(), 'POST', 'updateConfig 必须用 POST');
	const body = postCall.init.body;
	assert.ok(typeof body === 'string' && body.length > 0, 'updateConfig 必须有 body');
	const parsed = JSON.parse(body);
	assert.equal(parsed.newsFakeIpCompat, true, 'body 必须包含 newsFakeIpCompat:true');
	// token 由 _req 注入
	assert.equal(postCall.init.headers && postCall.init.headers['X-Pineglow-Token'], 'test-token', '必须携带正确的 X-Pineglow-Token');
});

test('R2-02: SG.api.updateConfig({newsFakeIpCompat:false}) 准确传递 false', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		if (u === '/api/state') {
			return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({
				version: '1.0.0', pid: 1,
				config: { gameDirectory: '', javaPath: '', playerName: 'P', maxMemoryMb: 4096, useAutoMemory: false, windowWidth: 1280, windowHeight: 720, serverHost: '', serverPort: '25565', useMirror: false, proxyEnabled: false, proxyHost: '', proxyPort: 0, proxyUsername: '', proxyPasswordConfigured: false, minimizeOnLaunch: false, autoCloseOnExit: false, selectedVersion: '', curseForgeApiKeyConfigured: false, selectedInstanceId: '', selectedAccountId: '', accountMode: 'offline', aiDiagnosisEnabled: false, selectedAiProviderId: '', aiProviders: [] },
				instances: [], accounts: [], java: [],
			}) });
		}
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ ok: true }) });
	});
	stub.ctx.SG.apiToken = 'test-token';
	await api.updateConfig({ newsFakeIpCompat: false });
	const postCall = calls.find(c => c.url === '/api/config');
	const parsed = JSON.parse(postCall.init.body);
	assert.equal(parsed.newsFakeIpCompat, false, 'body 必须包含 newsFakeIpCompat:false');
});

/* PHASE3BV4-REDA */

test('RED-A1: operationId must be in /diagnose JSON body', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		if (u === '/api/state') {
			return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ version: '1.0.0', pid: 1, config: {}, instances: [], accounts: [], java: [] }) });
		}
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ ok: true, aiResult: { ok: true, status: 'insufficient_information', summary: 't', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null }, suspectMods: [], rule: { summary: '', suggestions: [] }, mods: { issues: [] } }) });
	});
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-red-a1-1234567890abcdef';
	await api.diagnoseInstance('inst-1', { operationId: OP, confirmAiSend: false, localOnly: true });
	const diagCall = calls.find(c => c.url === '/api/instances/inst-1/diagnose');
	assert.ok(diagCall, 'must POST /api/instances/inst-1/diagnose');
	assert.equal((diagCall.init.method || '').toUpperCase(), 'POST');
	const parsed = JSON.parse(diagCall.init.body);
	assert.equal(parsed.operationId, OP, 'body.operationId must equal caller operationId (currently RED)');
});

test('RED-A2: cancelDiagnoseInstance must POST /diagnose/cancel with same operationId and token', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ ok: true, cancelled: true }) });
	});
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-red-a2-abcdef1234567890';
	assert.equal(typeof api.cancelDiagnoseInstance, 'function', 'SG.api.cancelDiagnoseInstance must exist (currently RED: missing)');
	await api.cancelDiagnoseInstance('inst-1', OP);
	const cancelCall = calls.find(c => c.url === '/api/instances/inst-1/diagnose/cancel');
	assert.ok(cancelCall, 'must POST /api/instances/inst-1/diagnose/cancel');
	assert.equal((cancelCall.init.method || '').toUpperCase(), 'POST');
	const headers = cancelCall.init.headers || {};
	assert.ok(headers['X-Pineglow-Token'] === 'test-token', 'cancel must carry X-Pineglow-Token');
	const parsed = JSON.parse(cancelCall.init.body);
	assert.deepEqual(Object.keys(parsed).sort(), ['operationId'], 'cancel body must contain only operationId');
	assert.equal(parsed.operationId, OP, 'cancel body.operationId must equal caller operationId');
});

/* ============================ PHASE 3C-2: aiDispatch metadata ============================
 * Phase 3C-2 P0 修复后契约：
 *   - /api/instances/{id}/diagnose 响应包含 aiDispatch 字段；
 *   - aiDispatch === null 时表示"未发起请求"（localOnly / 未确认 / endpoint 被拒 / 发送前取消）；
 *   - aiDispatch 非空时（{providerName, model, sentAtUtc}）表示服务端 SendAsync 入口被实际触发；
 *   - api.js 不对该字段做伪造或重写，调用方可直接消费。
 * 本批次只验证 api.js 透传 shape；渲染由 launcher.js 处理（在 ai-diagnose.test.cjs 中）。
 * ===================================================================================== */

test('PHASE3C2-A1: api.js must expose aiDispatch field unchanged (localOnly path → null)', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		calls.push({ url: String(url), init: init || {} });
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve({
				ok: true,
				hasTail: false,
				snapshot: { version: 1, id: 'snap-x', context: {} },
				rule: { summary: 'r', suggestions: [] },
				mods: { enabledCount: 0, disabledCount: 0, onlineManagedCount: 0, issues: [] },
				ai: null,
				aiResult: { ok: false, status: 'insufficient_information', confidence: 1, summary: 'i', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null },
				aiError: null,
				aiDispatch: null,
				configured: true,
				suspectMods: [],
			}),
		});
	});
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-phase3c2-a1-1234567890';
	const result = await api.diagnoseInstance('inst-1', { operationId: OP, confirmAiSend: false, localOnly: true });
	// 关键契约：api.js 必须透传 aiDispatch 字段，不吃掉、不改写
	assert.ok('aiDispatch' in result, 'diagnose result must expose aiDispatch key');
	assert.equal(result.aiDispatch, null, 'localOnly path → aiDispatch must be null');
});

test('PHASE3C2-A2: api.js must expose aiDispatch field unchanged (sent path → object)', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		calls.push({ url: String(url), init: init || {} });
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve({
				ok: true,
				hasTail: false,
				snapshot: { version: 1, id: 'snap-x', context: {} },
				rule: { summary: 'r', suggestions: [] },
				mods: { enabledCount: 0, disabledCount: 0, onlineManagedCount: 0, issues: [] },
				ai: null,
				aiResult: { ok: true, status: 'resolved', confidence: 0.8, summary: 'x', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null },
				aiError: null,
				aiDispatch: {
					providerName: 'DeepSeek',
					model: 'deepseek-chat',
					sentAtUtc: '2026-09-24T10:00:00.0000000+00:00',
				},
				configured: true,
				suspectMods: [],
			}),
		});
	});
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-phase3c2-a2-1234567890';
	const result = await api.diagnoseInstance('inst-1', { operationId: OP, confirmAiSend: true, localOnly: false });
	assert.ok(result.aiDispatch, 'sent path → aiDispatch must be object');
	assert.equal(typeof result.aiDispatch, 'object', 'aiDispatch must be object');
	assert.equal(result.aiDispatch.providerName, 'DeepSeek', 'providerName must come from server response');
	assert.equal(result.aiDispatch.model, 'deepseek-chat', 'model must come from server response');
	assert.equal(typeof result.aiDispatch.sentAtUtc, 'string', 'sentAtUtc must be ISO-8601 string from server');
	// 关键：sentAtUtc 必须能被 Date 解析（前端 toLocaleString 才有效）
	assert.ok(!isNaN(new Date(result.aiDispatch.sentAtUtc).getTime()), 'sentAtUtc must parse as Date');
});

test('PHASE3C2-A3: i18n must provide diag.dispatch.* keys in BOTH zh and en', () => {
	// i18n.js 用 IIFE 包裹；通过 window.SG.t(key, vars) 调用 t() 来验证键存在 + 文案不误导。
	const stub = makeStubBrowser();
	loadI18nRaw(stub.ctx);
	const SG = stub.ctx.SG;
	assert.ok(SG, 'SG must be exposed');
	assert.equal(typeof SG.t, 'function', 'SG.t must be a function');
	// P0 修复（最终）：不再有 "sentNoTime" / "failedNoTime" 键 —— 无时间就不显示 badge。
	const keys = ['diag.dispatch.sent', 'diag.dispatch.failed', 'diag.dispatch.notSent'];
	SG.setLocale('zh');
	for (const key of keys) {
		const v = SG.t(key, { provider: 'DeepSeek', model: 'deepseek-chat', time: '2026-09-24 18:00:00' });
		assert.ok(typeof v === 'string', `zh ${key} must resolve to a string (got: ${v})`);
		assert.ok(v.length > 0, `zh ${key} must resolve to non-empty string`);
		// 关键：不能声称"已送达"或"AI 已处理"
		assert.ok(!/已送达|已处理|送达/.test(v), `zh ${key} must not claim delivery (got: ${v})`);
	}
	SG.setLocale('en');
	for (const key of keys) {
		const v = SG.t(key, { provider: 'DeepSeek', model: 'deepseek-chat', time: '2026-09-24 18:00:00' });
		assert.ok(typeof v === 'string', `en ${key} must resolve to a string (got: ${v})`);
		assert.ok(v.length > 0, `en ${key} must resolve to non-empty string`);
		// 关键：不能声称 delivered / processed / successfully
		assert.ok(!/deliver|processed|successfully|arrived/i.test(v), `en ${key} must not claim delivery (got: ${v})`);
	}
	// 关键 P0 修复：被删除的 sentNoTime / failedNoTime 键必须不存在（避免误用降级文案）
	assert.equal(SG.t('diag.dispatch.sentNoTime', {}), 'diag.dispatch.sentNoTime', 'zh removed key must fall through (no fallback key)');
	assert.equal(SG.t('diag.dispatch.failedNoTime', {}), 'diag.dispatch.failedNoTime', 'en removed key must fall through (no fallback key)');
	SG.setLocale('zh');
});

/* ============================ LANG: locale passthrough ============================
 * 语言链路 Phase 1 RED（2026-09-25）：
 *   - api.js diagnoseInstance POST body 必须包含 SG.locale（'zh' 或 'en'），
 *     后端 LocalApiService 据此向 AiDiagnoseService 传入语言指令。
 *   - 缺失/非法 SG.locale → 安全默认 'zh'（松光启动器为中国区项目，保守默认）。
 *   - 测试加载 i18n.js 让 SG.locale 真实初始化；不引入新的 endpoint、不动 JSON 键名。
 * =============================================================================== */

test('LANG-1 RED: api.diagnoseInstance body must include locale from SG.locale (currently RED)', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve({ ok: true, aiResult: { ok: true, status: 'insufficient_information', summary: '', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null }, suspectMods: [], rule: { summary: '', suggestions: [] }, mods: { issues: [] } }),
		});
	});
	// 加载 i18n.js（i18n.js IIFE 会基于 navigator.language 决定 SG.locale；本 stub navigator='en-US' → en）
	loadI18nRaw(stub.ctx);
	// 把 SG.locale 强制设成 'zh' 模拟中文 UI（覆盖 i18n 默认）
	stub.ctx.SG.locale = 'zh';
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-lang-1-1234567890';
	await api.diagnoseInstance('inst-1', { operationId: OP, confirmAiSend: false, localOnly: true });
	const diagCall = calls.find(c => c.url === '/api/instances/inst-1/diagnose');
	assert.ok(diagCall, 'must POST /api/instances/inst-1/diagnose');
	const parsed = JSON.parse(diagCall.init.body);
	assert.equal(parsed.locale, 'zh', 'body.locale must equal SG.locale (currently RED: missing)');
});

test('LANG-2 RED: api.diagnoseInstance body must include locale=en when SG.locale=en', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve({ ok: true, aiResult: { ok: true, status: 'insufficient_information', summary: '', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null }, suspectMods: [], rule: { summary: '', suggestions: [] }, mods: { issues: [] } }),
		});
	});
	loadI18nRaw(stub.ctx);
	stub.ctx.SG.locale = 'en';
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-lang-2-1234567890';
	await api.diagnoseInstance('inst-1', { operationId: OP, confirmAiSend: false, localOnly: true });
	const diagCall = calls.find(c => c.url === '/api/instances/inst-1/diagnose');
	const parsed = JSON.parse(diagCall.init.body);
	assert.equal(parsed.locale, 'en', 'body.locale must equal SG.locale (currently RED: missing)');
});

test('LANG-3 RED: api.diagnoseInstance with missing SG.locale must default to safe "zh"', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve({ ok: true, aiResult: { ok: true, status: 'insufficient_information', summary: '', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null }, suspectMods: [], rule: { summary: '', suggestions: [] }, mods: { issues: [] } }),
		});
	});
	// 不加载 i18n.js，SG.locale 为 undefined：必须默认 'zh'
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-lang-3-1234567890';
	await api.diagnoseInstance('inst-1', { operationId: OP, confirmAiSend: false, localOnly: true });
	const diagCall = calls.find(c => c.url === '/api/instances/inst-1/diagnose');
	const parsed = JSON.parse(diagCall.init.body);
	assert.equal(parsed.locale, 'zh', 'missing SG.locale must default to safe "zh" (currently RED: missing or wrong)');
});

test('LANG-4 RED: api.diagnoseInstance with illegal SG.locale must default to safe "zh"', async () => {
	const calls = [];
	const { api, stub } = loadApi((url, init) => {
		const u = String(url);
		calls.push({ url: u, init: init || {} });
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve({ ok: true, aiResult: { ok: true, status: 'insufficient_information', summary: '', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null }, suspectMods: [], rule: { summary: '', suggestions: [] }, mods: { issues: [] } }),
		});
	});
	stub.ctx.SG.locale = 'ja-JP';  // 不在白名单
	stub.ctx.SG.apiToken = 'test-token';
	const OP = 'op-lang-4-1234567890';
	await api.diagnoseInstance('inst-1', { operationId: OP, confirmAiSend: false, localOnly: true });
	const diagCall = calls.find(c => c.url === '/api/instances/inst-1/diagnose');
	const parsed = JSON.parse(diagCall.init.body);
	assert.equal(parsed.locale, 'zh', 'illegal SG.locale must default to safe "zh" (currently RED: not enforced)');
});
