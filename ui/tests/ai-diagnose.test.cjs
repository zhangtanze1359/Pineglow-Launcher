'use strict';
/*
 * Phase 3B A-03 / A-05 / A-07 UI 测试
 * --------------------------------------------------------------------------
 * 目标：验证 api.js mock 与 launcher.js 关键安全行为，不依赖真实 Avalonia WebView。
 * 覆盖：
 *   - A-03 confirm 门禁：未传 confirmAiSend=true 或 localOnly=true 时返回 confirmation_required
 *   - A-03 仅本地分析：localOnly=true 时返回本地结果
 *   - A-05 两条禁用 Mod 入口在 launcher.js 都接 confirm 调用
 *   - A-07 AbortController：signal.aborted 时立即抛 AbortError
 *   - ai-diagnose 错误码在 i18n 中存在映射
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');

/* ========== 极简 jsdom 替代：仅 api.js / i18n.js / launcher.js 所需 ========== */
function makeStubBrowser() {
	const elements = new Map();
	const listeners = new WeakMap();
	function fire(target, type, ev) {
		const arr = listeners.get(target) || [];
		arr.filter(l => l.type === type).forEach(l => l.handler(ev));
	}
	function makeEl(tag) {
		const el = {
			tagName: (tag || 'div').toUpperCase(),
			children: [],
			attributes: {},
			dataset: {},
			classList: {
				_set: new Set(),
				add(c) { this._set.add(c); },
				remove(c) { this._set.delete(c); },
				contains(c) { return this._set.has(c); },
			},
			style: {},
			innerHTML: '',
			textContent: '',
			value: '',
			disabled: false,
			hidden: false,
			setAttribute(k, v) { this.attributes[k] = v; if (k.startsWith('data-')) this.dataset[k.slice(5)] = v; },
			getAttribute(k) { return this.attributes[k]; },
			addEventListener(type, handler) {
				const arr = listeners.get(this) || [];
				arr.push({ type, handler });
				listeners.set(this, arr);
			},
			removeEventListener(type, handler) {
				const arr = listeners.get(this) || [];
				listeners.set(this, arr.filter(l => !(l.type === type && l.handler === handler)));
			},
			appendChild(c) { this.children.push(c); return c; },
			remove() {},
			querySelector() { return null; },
			querySelectorAll() { return []; },
			get innerText() { return this.textContent; },
			// MR17：launcher.js 的弹窗键盘契约会调用这些标准 DOM 方法。
			// 真实元素本来就有，这里是补齐测试替身（不是给产品代码加特例）。
			tagName: String(tag || 'div').toUpperCase(),
			contains(node) {
				if (!node) return false;
				if (node === this) return true;
				return this.children.some(c => c === node || (c && typeof c.contains === 'function' && c.contains(node)));
			},
			removeAttribute(k) { delete this.attributes[k]; },
			closest() { return null; },
			matches() { return false; },
			focus() { if (typeof document_ !== 'undefined' && document_) document_.activeElement = this; },
			offsetParent: {},
		};
		el.dataset = new Proxy({}, {
			set(t, k, v) { t[k] = v; el.attributes['data-' + k] = v; return true; },
			get(t, k) { return t[k]; },
		});
		return el;
	}
	// document 同时作为 vm sandbox 的 document 全局
	const document = {
		head: makeEl('head'),
		body: makeEl('body'),
		documentElement: makeEl('html'),
		createElement: tag => makeEl(tag),
		getElementById(id) {
			if (!elements.has(id)) elements.set(id, makeEl('div'));
			return elements.get(id);
		},
		querySelector(sel) { return elements.get(sel) || null; },
		querySelectorAll() { return []; },
		addEventListener(type, handler) { fire(document.body, type, {}); },
		// MR17：closeModal 会卸掉键盘监听；假 document 需要这个标准方法。
		removeEventListener() {},
		setAttribute() {},
		getAttribute() { return ''; },
	};
	const ctx = {
		document,
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN' },
		localStorage: { getItem: () => null, setItem: () => {} },
		URLSearchParams,
		AbortController: typeof AbortController !== 'undefined' ? AbortController : (function () {
			function C() { this.signal = { aborted: false, addEventListener() {} }; }
			C.prototype.abort = function () { this.signal.aborted = true; };
			return C;
		})(),
		Promise,
		Date,
		Math,
		Object,
		Array,
		JSON,
		Number,
		String,
		Boolean,
		setTimeout,
		clearTimeout,
		setInterval: () => 0,
		clearInterval: () => 0,
		console,
		SG: {},
	};
	Object.defineProperty(ctx, 'window', {
		get() { return ctx; },
		set(v) { /* 防止被覆盖 */ },
		configurable: true,
	});
	ctx.fetch = () => Promise.reject(new Error('no live fetch in unit tests'));
	ctx.addEventListener = function (type, handler) { fire(ctx, type, {}); };
	return { document, window: ctx };
}

function loadApi(bundle) {
	const ctx = bundle.window;
	ctx.SG = ctx.SG || {};
	ctx.SG.apiMode = 'mock';
	ctx.SG.apiToken = 'unit-token';
	ctx.SG.mock = {};
	const sandbox = ctx;
	sandbox.console = console;
	sandbox.AbortController = ctx.AbortController;
	sandbox.fetch = ctx.fetch;
	sandbox.setInterval = () => 0;
	sandbox.clearInterval = () => 0;
	sandbox.URLSearchParams = URLSearchParams;
	sandbox.navigator = { language: 'zh-CN', userLanguage: 'zh-CN' };
	sandbox.localStorage = { getItem: () => null, setItem: () => {} };
	sandbox.DOMException = typeof DOMException !== 'undefined' ? DOMException : class extends Error { constructor(m, n) { super(m); this.name = n || 'Error'; } };
	Object.defineProperty(sandbox, 'window', {
		get() { return this; },
		set(v) { /* 防止被覆盖 */ },
		configurable: true,
	});
	vm.createContext(sandbox);
	vm.runInContext(I18N_JS, sandbox, { filename: 'i18n.js' });
	vm.runInContext(API_JS, sandbox, { filename: 'api.js' });
	return { ctx: sandbox, SG: ctx.SG, t: ctx.SG.t };
}

function loadLauncher(bundle, deps) {
	const ctx = bundle.window;
	ctx.SG = deps.SG;
	ctx.t = deps.t;
	const sandbox = {
		window: ctx,
		document: bundle.document,
		console,
		AbortController: ctx.AbortController,
		DOMException: typeof DOMException !== 'undefined' ? DOMException : class extends Error { constructor(m, n) { super(m); this.name = n || 'Error'; } },
		Promise,
		Date,
		Math,
		Object,
		Array,
		JSON,
		Number,
		String,
		Boolean,
		setTimeout,
		clearTimeout,
		confirm: () => true, // 默认放行，让 disable 按钮调用 toggleMod
		alert: () => {},
	};
	vm.createContext(sandbox);
	// 只截取 openAiDiagnose + renderConfirmPanel 函数体片段用于静态扫描
	vm.runInContext(I18N_JS, sandbox, { filename: 'i18n.js' });
	return sandbox;
}

/* ========== A-03 confirm 门禁 ========== */
test('A-03: diagnoseInstance 不传 confirmAiSend=true 时返回 confirmation_required', async () => {
	const bundle = makeStubBrowser();
	const { SG } = loadApi(bundle);
	const result = await SG.api.diagnoseInstance('inst-1', {});
	assert.equal(result.ok, true);
	assert.ok(result.aiResult, '应有 aiResult');
	assert.equal(result.aiResult.errorCode, 'confirmation_required');
	assert.equal(result.aiResult.status, 'failed');
});

test('A-03: diagnoseInstance 不传任何 flag 时不包含真实 AI 字段，仅本地规则', async () => {
	const bundle = makeStubBrowser();
	const { SG } = loadApi(bundle);
	const result = await SG.api.diagnoseInstance('inst-1', {});
	assert.equal(result.aiResult.causes.length, 0);
	assert.equal(result.suspectMods.length, 0);
});

/* ========== A-03 localOnly ========== */
test('A-03: diagnoseInstance 传 localOnly=true 返回 insufficient_information', async () => {
	const bundle = makeStubBrowser();
	const { SG } = loadApi(bundle);
	const result = await SG.api.diagnoseInstance('inst-1', { localOnly: true });
	assert.equal(result.aiResult.ok, true);
	assert.equal(result.aiResult.status, 'insufficient_information');
	assert.ok(result.aiResult.warnings.length > 0, '警告应非空');
});

/* ========== A-07 AbortController ========== */
test('A-07: 预取消的 signal 立即抛 AbortError', async () => {
	const bundle = makeStubBrowser();
	const { SG } = loadApi(bundle);
	const controller = new bundle.window.AbortController();
	controller.abort();
	await assert.rejects(
		SG.api.diagnoseInstance('inst-1', { confirmAiSend: true, signal: controller.signal }),
		err => err.name === 'AbortError'
	);
});

/* ========== A-03 confirmAiSend=true 走 mock 成功路径 ========== */
test('A-03: confirmAiSend=true 返回 mock 的完整 AI 诊断数据', async () => {
	const bundle = makeStubBrowser();
	const { SG } = loadApi(bundle);
	const result = await SG.api.diagnoseInstance('inst-1', { confirmAiSend: true });
	assert.equal(result.ok, true);
	assert.ok(result.ai);
	assert.ok(result.suspectMods && result.suspectMods.length > 0);
});

/* ========== A-05 launcher.js 静态扫描：两条 disable_mod 路径都接 confirm ========== */
test('A-05: launcher.js 中 [data-action] 与 [data-suspect] 两个分支都先调用 confirm()', () => {
	const actionMatches = LAUNCHER_JS.match(/data-action[\s\S]*?addEventListener[\s\S]*?confirm\(/g) || [];
	const suspectMatches = LAUNCHER_JS.match(/data-suspect[\s\S]*?addEventListener[\s\S]*?confirm\(/g) || [];
	assert.ok(actionMatches.length >= 1, 'data-action 路径必须包含 confirm() 调用');
	assert.ok(suspectMatches.length >= 1, 'data-suspect 路径必须包含 confirm() 调用（Phase 3B A-05）');
});

/* ========== A-07 launcher.js 静态扫描：使用 AbortController ========== */
test('A-07: launcher.js openAiDiagnose 中使用 AbortController 并将 signal 传至 api.diagnoseInstance', () => {
	assert.match(LAUNCHER_JS, /openAiDiagnose[\s\S]*?AbortController/);
	assert.match(LAUNCHER_JS, /openAiDiagnose[\s\S]*?signal:\s*controller\.signal/);
	assert.match(LAUNCHER_JS, /openAiDiagnose[\s\S]*?controller\.abort\(\)/);
});

/* ========== A-03 确认面板 i18n 文案完整性 ========== */
test('A-03: i18n 中 confirmPanel / confirmSendAi / confirmLocalOnly / confirmCancel / safetyModeNotice 都存在', () => {
	const bundle = makeStubBrowser();
	const { t } = loadApi(bundle);
	for (const key of [
		'diag.confirmPanelTitle',
		'diag.confirmPanelIntro',
		'diag.confirmPanelProvider',
		'diag.confirmPanelModel',
		'diag.confirmPanelData',
		'diag.confirmPanelDataSummary',
		'diag.confirmSendAi',
		'diag.confirmLocalOnly',
		'diag.confirmCancel',
		'diag.safetyModeNotice',
		'diag.cancelButton',
		'diag.err.aiAuthFailed',
		'diag.err.aiRateLimited',
		'diag.err.aiTimeout',
		'diag.err.aiResponseTooLarge',
		'diag.err.aiInvalidContentType',
		'diag.err.aiInvalidResponse',
		'diag.err.aiServiceError',
		'diag.err.aiCancelled',
		'diag.err.endpointNotAllowed',
		'diag.err.confirmationRequired',
	]) {
		const v = t(key);
		assert.notEqual(v, key, `${key} must resolve to a non-key string; got ${v}`);
	}
});

/* ========== A-08 错误码文案：稳定映射 ========== */
test('A-08: 错误码诊断消息可映射为 i18n', () => {
	const bundle = makeStubBrowser();
	const { t } = loadApi(bundle);
	const codes = ['aiAuthFailed', 'aiRateLimited', 'aiTimeout', 'aiResponseTooLarge', 'aiInvalidContentType', 'aiInvalidResponse', 'aiServiceError', 'aiCancelled', 'endpointNotAllowed', 'confirmationRequired'];
	for (const code of codes) {
		const v = t(`diag.err.${code}`);
		assert.notEqual(v, `diag.err.${code}`, `${code} must resolve`);
	}
});

/* ========== Gate R1 RED 1/5：Modal 代次竞态 ========== */
test('Gate R1 RED: openAiDiagnose must survive stale closeModal cleanup timer', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	assert.ok(launcher && launcher.closeModal && launcher.openAiDiagnose, 'real launcher.js exports must be available');
	// 先 closeModal（排队 180ms 清理 timer）
	launcher.closeModal();
	// 立即 openAiDiagnose 创建新 modal
	await launcher.openAiDiagnose({ id: 'inst-x', version: '1.20.1', loader: 'Vanilla' });
	const host = ctx.document.getElementById('modalHost');
	assert.ok(host.innerHTML.includes('AI Crash Diagnosis') || host.innerHTML.length > 100, 'new modal must be present');
	// 推进时间到 250ms 之后
	ctx.advanceTimers(250);
	// 当前 RED：旧的 180ms 清理 timer 会清空 modalHost；GREEN 后应保持
	assert.ok(
		host.innerHTML.includes('AI Crash Diagnosis') || host.innerHTML.length > 100,
		`modal must survive stale cleanup timer; got innerHTML length=${host.innerHTML.length}`
	);
});

/* ========== Gate R1 RED 2/5：snake_case 错误码逐项渲染 ========== */
test('Gate R1 RED: snake_case error codes from backend must map to i18n keys via mapAiErrorCodeToI18nKey', () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const mapFn = launcher.mapAiErrorCodeToI18nKey;
	assert.equal(typeof mapFn, 'function');
	const errCodes = ['ai_auth_failed','ai_rate_limited','ai_timeout','ai_response_too_large','ai_invalid_content_type','ai_invalid_response','ai_service_error','ai_cancelled','endpoint_not_allowed','confirmation_required'];
	for (const code of errCodes) {
		const key = mapFn(code);
		assert.notEqual(key, null, `${code} must map to non-null i18n key`);
		assert.ok(key.startsWith('diag.err.'), `${code} must map to diag.err.*; got ${key}`);
		assert.ok(!key.includes('_'), `${code} must map to camelCase key (no underscore); got ${key}`);
	}
});

/* ========== Gate R1 RED 3/5：两条禁用 Mod 路径都必须 confirm ========== */
test('Gate R1 RED: structured action disable_mod confirm=false → toggleMod 0 calls', async () => {
	const ctx = makeLoaderContext();
	let toggleCalls = 0;
	ctx.window.SG.api.toggleMod = async () => { toggleCalls++; return { ok: true }; };
	ctx.window.confirm = () => false;
	const launcher = loadRealLauncher(ctx);
	// 手动创建一个有 [data-action] 按钮的 modal 模拟 runResult 后状态
	const host = ctx.document.getElementById('modalHost');
	host.innerHTML = '<div class="modal"><div id="diagContent"><button data-action="disable_mod" data-target="bad-mod.jar">Disable</button></div></div>';
	host.hidden = false;
	// 直接调用 openAiDiagnose 让按钮绑定注册；real openAiDiagnose 会替换 innerHTML，所以改为：
	// 监听 diagContent 的 click 事件，确认 launcher's bindActionButtons 已生效
	const content = host.querySelector('#diagContent');
	const btn = content.querySelector('[data-action]');
	// launcher.js 在 runDiag -> renderResult 后会 bindActionButtons（querySelectorAll + addEventListener）；
	// 我们的 test 需让 launcher's openAiDiagnose 走完 runDiag 才能绑按钮。
	// 简化做法：直接通过 mock SG.api.diagnoseInstance 走完到 renderResult。
	ctx.window.SG.api.diagnoseInstance = async () => ({
		ok: true,
		aiResult: { ok: true, status: 'resolved', summary: 'test', causes: [], actions: [{ id: 'a1', title: 'disable', description: '...', risk: 'high', reversible: true, actionType: 'disable_mod', target: 'bad-mod.jar' }], warnings: [], missingInformation: [], errorCode: null },
		suspectMods: [],
	});
	await launcher.openAiDiagnose({ id: 'inst-x', version: '1.20.1', loader: 'Vanilla' });
	// 点击 "发送给 AI" 按钮触发 runDiag
	const sendBtn = host.querySelector('#dConfirmSend');
	if (sendBtn) sendBtn.click();
	// 让 runDiag 完成：mock 100ms + renderResult
	await new Promise(r => setTimeout(r, 400));
	const actionBtn = host.querySelector('[data-action]');
	if (actionBtn) actionBtn.click();
	await new Promise(r => setTimeout(r, 50));
	assert.equal(toggleCalls, 0, 'confirm=false must not call toggleMod');
});

test('Gate R1 RED: suspectMods confirm=false → toggleMod 0 calls', async () => {
	const ctx = makeLoaderContext();
	let toggleCalls = 0;
	ctx.window.SG.api.toggleMod = async () => { toggleCalls++; return { ok: true }; };
	ctx.window.confirm = () => false;
	ctx.window.SG.api.diagnoseInstance = async () => ({
		ok: true,
		aiResult: { ok: true, status: 'resolved', summary: 'test', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null },
		suspectMods: ['bad-mod.jar'],
	});
	const launcher = loadRealLauncher(ctx);
	await launcher.openAiDiagnose({ id: 'inst-x', version: '1.20.1', loader: 'Vanilla' });
	const host = ctx.document.getElementById('modalHost');
	const sendBtn = host.querySelector('#dConfirmSend');
	if (sendBtn) sendBtn.click();
	await new Promise(r => setTimeout(r, 100));
	const suspectBtn = host.querySelector('[data-suspect]');
	if (suspectBtn) suspectBtn.click();
	await new Promise(r => setTimeout(r, 50));
	assert.equal(toggleCalls, 0, 'suspectMods confirm=false must not call toggleMod');
});

/* ========== Gate R1 RED 5/5：Provider/Model 从 STATE.config 显示，Key 不在 DOM ========== */
test('Gate R1 RED: Provider/Model must come from STATE.config, not "—"', async () => {
	const ctx = makeLoaderContext();
	ctx.window.STATE = {
		config: {
			aiProviders: [
				{ id: 'p1', name: 'DeepSeek Provider', baseUrl: 'https://api.deepseek.com/v1', model: 'deepseek-chat', apiKey: 'SECRET_KEY_NOT_TO_LEAK' },
			],
			selectedAiProviderId: 'p1',
		},
	};
	const launcher = loadRealLauncher(ctx);
	await launcher.openAiDiagnose({ id: 'inst-x', version: '1.20.1', loader: 'Vanilla' });
	const host = ctx.document.getElementById('modalHost');
	const html = host.innerHTML;
	assert.ok(html.includes('DeepSeek Provider'), 'provider name must be rendered');
	assert.ok(html.includes('deepseek-chat'), 'model must be rendered');
	assert.ok(!html.includes('SECRET_KEY_NOT_TO_LEAK'), 'API key must NEVER appear in DOM');
	assert.ok(!/>\s*—\s*</.test(html.replace(/[一-鿿]/g, '')), 'Provider/Model must not show placeholder "—" (in ASCII segments)');
});

/* ========== Gate V2 RED: 取消全路径 ========== */
/* 务实做法：直接命中生产 launcher.js 源码，对路径关键行为做静态断言。
 * 当前 RED：closeModal 仅做 setTimeout 清理 host；abort 只出现在 cancelBtn 与 dCancelRequest。
 * GREEN 期望：closeModal、遮罩 dClose、unload 都必须 abort controller。 */
const LAUNCHER_SRC_FOR_CANCEL = fs.readFileSync(path.resolve(__dirname, '../js/launcher.js'), 'utf8');

function countAbortUsages(src) {
	const hasAbortInCloseModal = /function\s+closeModal\s*\([^)]*\)\s*\{[\s\S]{0,1500}?(controller\.abort|aborter\(\)|_modalAborters)/.test(src);
	const hasAbortInUnload = /(?:window\.)?addEventListener\s*\(\s*(['"])(?:beforeunload|unload|pagehide)\1[\s\S]{0,400}?controller\.abort/.test(src);
	const hasAbortInDClose = /dClose[\s\S]{0,200}?controller\.abort/.test(src);
	return { hasAbortInCloseModal, hasAbortInUnload, hasAbortInDClose };
}

test('Gate V2 RED-A: closeModal() must abort controller (currently RED)', () => {
	const { hasAbortInCloseModal } = countAbortUsages(LAUNCHER_SRC_FOR_CANCEL);
	assert.ok(hasAbortInCloseModal, 'RED 当前：closeModal 内部未调用 controller.abort();遮罩/dClose 路径不会 abort');
});

test('Gate V2 RED-B: window unload listener must abort controller (currently RED)', () => {
	const { hasAbortInUnload } = countAbortUsages(LAUNCHER_SRC_FOR_CANCEL);
	assert.ok(hasAbortInUnload, 'RED 当前：未注册 beforeunload/unload/pagehide listener 触发 abort');
});

test('Gate V2 RED-C: modal teardown must be idempotent (currently RED)', () => {
	const hasTeardownFn = /function\s+(teardown|abortAndClose|closeAndAbort|tearDown|abortDiag)\s*\(/.test(LAUNCHER_SRC_FOR_CANCEL)
		|| /const\s+(teardown|abortAndClose|closeAndAbort|tearDown|abortDiag)\s*=/.test(LAUNCHER_SRC_FOR_CANCEL);
	const hasClosedFlag = /let\s+closed\s*=\s*(true|false)/.test(LAUNCHER_SRC_FOR_CANCEL);
	assert.ok(hasTeardownFn || hasClosedFlag, 'RED 当前：openAiDiagnose 缺少幂等 teardown 函数或 closed 标志');
});

test('Gate V2 RED-D: cancelBtn.onclick already aborts (contract lock)', () => {
	const hasCancelAbort = /cancelBtn\.onclick\s*=\s*\(\s*\)\s*=>\s*\{[\s\S]*?controller\.abort/.test(LAUNCHER_SRC_FOR_CANCEL);
	assert.ok(hasCancelAbort, 'contract lock: cancelBtn.onclick must call controller.abort');
});

/* ========== Gate V2 RED: 设置页不一致 ========== */
test('Gate V2 RED-F: openAiEditor must validate custom baseUrl against allowlist on save', () => {
	const src = fs.readFileSync(path.resolve(__dirname, '../js/launcher.js'), 'utf8');
	// v2 RED 期望：openAiEditor 的保存逻辑必须校验 baseUrl 是否在 allowlist
	const hasSaveValidate = /aiOk[\s\S]*?isFrontendEndpointAllowed/.test(src) || /saveAiProfile[\s\S]*?isFrontendEndpointAllowed/.test(src) || /openAiEditor[\s\S]*?isFrontendEndpointAllowed[\s\S]*?\(/.test(src);
	// 标记/列表标签
	const hasListBadge = src.includes('safetyModeBlocked') && src.includes('openAiManage');
	assert.ok(hasSaveValidate, `RED 当前：openAiEditor 保存时未校验 allowlist；hasSaveValidate=${hasSaveValidate} hasListBadge=${hasListBadge}`);
});

/* ========== Gate V2 RED: 测试 hook 默认不可见 ========== */
test('Gate V2 RED-G: launcher.js source must guard __aiDiagnostics with test flag', () => {
	const src = fs.readFileSync(path.resolve(__dirname, '../js/launcher.js'), 'utf8');
	const hasGuard = /window\.__TEST_DIAG__\s*===\s*true/.test(src);
	const unconditional = /window\.__aiDiagnostics\s*=\s*\{/.test(src) && !/__TEST_DIAG__/.test(src.split('window.__aiDiagnostics = {')[0]);
	assert.ok(hasGuard && !unconditional, 'RED: __aiDiagnostics must be guarded by __TEST_DIAG__ flag');
});

test('Gate V2 RED-G2: __aiDiagnostics is undefined in production runtime (without __TEST_DIAG__)', () => {
	const ctx = makeLoaderContext();
	// 显式确保 __TEST_DIAG__ 关闭
	ctx.window.__TEST_DIAG__ = false;
	const sandbox = ctx;
	sandbox.console = console;
	sandbox.URLSearchParams = URLSearchParams;
	sandbox.DOMException = typeof DOMException !== 'undefined' ? DOMException : class extends Error { constructor(m, n) { super(m); this.name = n || 'Error'; } };
	sandbox.requestAnimationFrame = (fn) => sandbox.setTimeout(fn, 0);
	sandbox.location = { href: 'http://localhost', protocol: 'http:', host: 'localhost' };
	sandbox.addEventListener = () => {};
	vm.createContext(sandbox);
	vm.runInContext(I18N_JS, sandbox, { filename: 'i18n.js' });
	vm.runInContext(API_JS, sandbox, { filename: 'api.js' });
	vm.runInContext(LAUNCHER_JS, sandbox, { filename: 'launcher.js', timeout: 8000 });
	assert.equal(sandbox.window.__aiDiagnostics, undefined, 'production must NOT expose __aiDiagnostics');
});
/* ========== Gate V2 RED: _probe-launcher-load.cjs 必须删除 ========== */
test('Gate V2 RED-H: _probe-launcher-load.cjs must be removed', () => {
	const probePath = path.resolve(__dirname, '_probe-launcher-load.cjs');
	assert.ok(!fs.existsSync(probePath), `RED 当前：_probe-launcher-load.cjs 仍存在；必须删除 (path=${probePath})`);
});

/* ========== Gate R1 测试基础设施 ========== */
function makeLoaderContext() {
	const elements = new Map();
	const listeners = new WeakMap();
	const timers = []; // { id, fn, ms, cancelled }
	let nextTimerId = 0;
	const listenersByType = new WeakMap();
	function fire(target, type, ev) {
		const arr = listeners.get(target) || [];
		arr.filter(l => l.type === type).forEach(l => { try { l.handler(ev); } catch (e) { console.error('listener err', e); } });
	}
	function makeEl(tag) {
		const el = {
			tagName: (tag || 'div').toUpperCase(),
			children: [],
			attributes: {},
			dataset: new Proxy({}, { set(t, k, v) { t[k] = v; el.attributes['data-' + k] = v; return true; }, get(t, k) { return t[k]; } }),
			classList: {
				_set: new Set(),
				add(c) { this._set.add(c); },
				remove(c) { this._set.delete(c); },
				contains(c) { return this._set.has(c); },
				toggle(c) { if (this._set.has(c)) this._set.delete(c); else this._set.add(c); return this._set.has(c); },
			},
			style: {},
			textContent: '',
			value: '',
			disabled: false,
			hidden: false,
			setAttribute(k, v) { this.attributes[k] = v; if (k.startsWith('data-')) this.dataset[k.slice(5)] = v; },
			getAttribute(k) { return this.attributes[k]; },
			addEventListener(type, handler) {
				const arr = listeners.get(this) || [];
				arr.push({ type, handler });
				listeners.set(this, arr);
			},
			removeEventListener(type, handler) {
				const arr = listeners.get(this) || [];
				listeners.set(this, arr.filter(l => !(l.type === type && l.handler === handler)));
			},
			appendChild(c) { this.children.push(c); c.parentElement = this; return c; },
			remove() {},
			// MR17：launcher.js 的弹窗键盘契约会调用这些标准 DOM 方法。
			// 真实元素本来就有，这里是补齐测试替身（不是给产品代码加特例）。
			contains(node) {
				if (!node) return false;
				if (node === this) return true;
				return this.children.some(c => c === node || (c && typeof c.contains === 'function' && c.contains(node)));
			},
			removeAttribute(k) { delete this.attributes[k]; },
			closest() { return null; },
			matches() { return false; },
			focus() { if (typeof document_ !== 'undefined' && document_) document_.activeElement = this; },
			offsetParent: {},
			findById(id) {
				if (this.attributes.id === id) return this;
				for (const c of this.children) {
					const found = c.findById ? c.findById(id) : null;
					if (found) return found;
				}
				return null;
			},
			findByAttr(sel) {
				const m = sel.match(/^\[([^=\]]+)(?:=([^\]]+))?\]$/);
				if (!m) return null;
				const attr = m[1], val = m[2] ? m[2].replace(/^["']|["']$/g, '') : null;
				if ((val == null && this.attributes[attr] != null) || (val != null && this.attributes[attr] === val)) return this;
				for (const c of this.children) {
					const found = c.findByAttr ? c.findByAttr(sel) : null;
					if (found) return found;
				}
				return null;
			},
			findAllByAttr(sel) {
				const m = sel.match(/^\[([^=\]]+)(?:=([^\]]+))?\]$/);
				if (!m) return [];
				const attr = m[1], val = m[2] ? m[2].replace(/^["']|["']$/g, '') : null;
				let results = [];
				if ((val == null && this.attributes[attr] != null) || (val != null && this.attributes[attr] === val)) results.push(this);
				for (const c of this.children) {
					results = results.concat(c.findAllByAttr ? c.findAllByAttr(sel) : []);
				}
				return results;
			},
			querySelector(sel) {
				if (sel.startsWith('#')) return this.findById(sel.slice(1));
				if (sel.startsWith('[')) return this.findByAttr(sel);
				return null;
			},
			querySelectorAll(sel) {
				if (sel.startsWith('[')) return this.findAllByAttr(sel);
				return [];
			},
			click() {
				fire(this, 'click', { target: this });
				if (typeof this.onclick === 'function') {
					try { this.onclick({ target: this }); } catch (e) { console.error('onclick err', e); }
				}
			},
			get innerText() { return this.textContent; },
		};
		Object.defineProperty(el, 'innerHTML', {
			get() { return el._innerHTML || ''; },
			set(html) {
				el._innerHTML = html;
				el.children = [];
				parseIntoChildren(el, html);
			},
		});
		return el;
	}
	function parseIntoChildren(parent, html) {
		// 极简解析：匹配顶层标签，按属性构建子节点；嵌套只解析第一层。
		const tagRe = /<([a-zA-Z][a-zA-Z0-9]*)\b([^>]*?)(\/?)>([\s\S]*?)(<\/\1>|$)/g;
		let m;
		let depth = 0;
		const stack = [parent];
		const re2 = /<([a-zA-Z][a-zA-Z0-9]*)\b([^>]*?)(\/?)>|<\/([a-zA-Z][a-zA-Z0-9]*)>|<!--[\s\S]*?-->/g;
		let cursor = 0;
		while ((m = re2.exec(html)) !== null) {
			if (m[1]) {
				// open tag
				const tag = m[1];
				const attrs = m[2];
				const selfClose = m[3] === '/';
				const child = makeEl(tag);
				const idM = attrs.match(/\bid\s*=\s*"([^"]+)"/);
				if (idM) {
					child.attributes.id = idM[1];
					try { elements.set(idM[1], child); } catch (_) { /* ignore */ }
				}
				const classM = attrs.match(/\bclass\s*=\s*"([^"]+)"/);
				if (classM) classM[1].split(/\s+/).forEach(c => { try { child.classList.add(c); } catch {} });
				const dataRegex = /\bdata-([a-zA-Z-]+)\s*=\s*"([^"]+)"/g;
				let dm;
				while ((dm = dataRegex.exec(attrs)) !== null) {
					child.attributes['data-' + dm[1]] = dm[2];
					// Phase 3C-1: 同步写入 dataset Proxy，让 launcher.js 的 btn.dataset.action 能在 stub DOM 中读到 data-* 值
					try { child.dataset[dm[1]] = dm[2]; } catch (_) { /* Proxy 不可写时忽略；attributes 已存 */ }
				}
				const cur = stack[stack.length - 1];
				cur.children.push(child);
				if (!selfClose) stack.push(child);
			} else if (m[4]) {
				// close tag
				if (stack.length > 1) stack.pop();
			}
		}
	}
	const document_ = {
		head: makeEl('head'),
		body: makeEl('body'),
		documentElement: makeEl('html'),
		createElement: tag => makeEl(tag),
		getElementById(id) {
			if (elements.has(id)) return elements.get(id);
			const el = makeEl('div');
			el.attributes.id = id;
			elements.set(id, el);
			return el;
		},
		querySelector(sel) {
			if (sel.startsWith('#')) return document_.getElementById(sel.slice(1));
			return null;
		},
		querySelectorAll() { return []; },
		addEventListener() {},
		removeEventListener() {}, // MR17
		setAttribute() {},
		getAttribute() { return ''; },
	};
	const timersController = {
		set: (fn, ms) => {
			const id = nextTimerId++;
			timers.push({ id, fn, ms, cancelled: false });
			return id;
		},
		clear: (id) => {
			const t = timers.find(x => x.id === id);
			if (t) t.cancelled = true;
		},
	};
	function advanceTimers(self, ms) {
		let elapsed = 0;
		while (elapsed < ms) {
			const due = timers.filter(t => !t.cancelled && t.ms <= ms - elapsed);
			if (due.length === 0) break;
			for (const t of due) {
				t.cancelled = true;
				try { t.fn(); } catch (e) { console.error('timer err', e); }
			}
			elapsed += Math.min(...due.map(t => t.ms));
		}
	}
	const ctx = {
		document: document_,
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN' },
		localStorage: { getItem: () => null, setItem: () => {} },
		URLSearchParams,
		AbortController: typeof AbortController !== 'undefined' ? AbortController : (function () {
			function C() { this.signal = { aborted: false, addEventListener() {} }; }
			C.prototype.abort = function () { this.signal.aborted = true; };
			return C;
		})(),
		Promise,
		Date,
		Math,
		Object,
		Array,
		JSON,
		Number,
		String,
		Boolean,
		setTimeout: timersController.set,
		clearTimeout: timersController.clear,
		setInterval: () => 0,
		clearInterval: () => 0,
		console,
		SG: {
			apiMode: 'mock',
			apiToken: 'unit-token',
			mock: {},
			api: {
				diagnoseInstance: async () => ({
					ok: true,
					aiResult: { ok: false, status: 'failed', errorCode: 'confirmation_required', summary: '...', causes: [], actions: [], warnings: [], missingInformation: [] },
					suspectMods: [],
				}),
				toggleMod: async () => ({ ok: true }),
			},
		},
	};
	Object.defineProperty(ctx, 'window', { get() { return ctx; }, set(v) { }, configurable: true });
	ctx.advanceTimers = (ms) => advanceTimers(ctx, ms);
	ctx.timers = timers;
	return ctx;
}

function loadLauncher(ctx) {
	const sandbox = ctx;
	sandbox.console = console;
	sandbox.URLSearchParams = URLSearchParams;
	sandbox.DOMException = typeof DOMException !== 'undefined' ? DOMException : class extends Error { constructor(m, n) { super(m); this.name = n || 'Error'; } };
	vm.createContext(sandbox);
	vm.runInContext(I18N_JS, sandbox, { filename: 'i18n.js' });
	vm.runInContext(API_JS, sandbox, { filename: 'api.js' });
	// launcher.js 需要 modalHost 在 document 上存在；插入 stub
	const host = ctx.document.getElementById('modalHost');
	host.hidden = false;
	// 不直接 eval 整个 launcher.js（依赖过多）；改为源码扫描并按需抽取函数体
	// 当前 RED 阶段我们只确认 launcher.js 文本中存在必要修复模式 + 让 loadLauncher 提供最简可调用的 closeModal/openAiDiagnose 占位
	sandbox.openAiDiagnose = async (inst) => {
		// GREEN 阶段会替换为真实函数体；RED 阶段仅暴露签名供测试
		host.innerHTML = `<div id="diagContent">AI Crash Diagnosis · inst=${inst.id} · providers=${JSON.stringify(sandbox.STATE?.config?.aiProviders || [])} · selected=${sandbox.STATE?.config?.selectedAiProviderId || ''}</div>`;
	};
	sandbox.closeModal = () => {
		host.classList.remove('show');
		sandbox.setTimeout(() => { host.hidden = true; host.innerHTML = ''; }, 180);
	};
	return sandbox;
}

/* ========== loadRealLauncher：命中生产路径 ========== */
function loadRealLauncher(ctx) {
	const sandbox = ctx;
	sandbox.console = console;
	sandbox.URLSearchParams = URLSearchParams;
	sandbox.DOMException = typeof DOMException !== 'undefined' ? DOMException : class extends Error { constructor(m, n) { super(m); this.name = n || 'Error'; } };
	sandbox.requestAnimationFrame = sandbox.requestAnimationFrame || ((fn) => sandbox.setTimeout(fn, 0));
	sandbox.location = sandbox.location || { href: 'http://localhost', protocol: 'http:', host: 'localhost' };
	sandbox.addEventListener = sandbox.addEventListener || (() => {});
	// v2 修复：显式设置 __TEST_DIAG__ flag 守护测试导出
	sandbox.__TEST_DIAG__ = true;
	vm.createContext(sandbox);
	vm.runInContext(I18N_JS, sandbox, { filename: 'i18n.js' });
	vm.runInContext(API_JS, sandbox, { filename: 'api.js' });
	vm.runInContext(LAUNCHER_JS, sandbox, { filename: 'launcher.js', timeout: 8000 });
	// launcher.js 内的 IIFE 把导出挂到 window.__aiDiagnostics
	const diag = sandbox.window.__aiDiagnostics;
	if (!diag) throw new Error('launcher.js did not expose window.__aiDiagnostics');
	return diag;
}

// 若 GREEN 实现提供了 launcherErrorMap，RED 测试通过它走；否则当前 RED
const launcherErrorMap = (snake, t) => {
	// 当前 launcher.js 行为：直接拼 `diag.err.${errorCode}` → 返回 `diag.err.ai_auth_failed`
	// GREEN 后应改成显式 map。占位实现返回 snake_case 以触发 RED。
	return `diag.err.${snake}`;
};

/* ============== Phase 3B v4 步骤 1 RED-B：UI 生产路径 ============== */

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

async function waitFor(predicate, timeoutMs = 1500) {
	const start = Date.now();
	while (!predicate()) {
		if (Date.now() - start > timeoutMs) throw new Error('waitFor timeout');
		await sleep(5);
	}
}

function installCaptureStubs(ctx) {
	const captured = { diagnose: [], cancel: [] };
	// ui/js/api.js 第 8 行会读取 document.querySelector('meta[name="pineglow-session-token"]')
	// 拿 token；若沙箱里没有该 meta 元素，会把 SG.apiToken 强行置为 ''，导致 ui/js/launcher.js
	// 的 bestEffortCancelOnUnload 同步 XHR 仍按真实契约写到 'X-Pineglow-Token: ""'。
	// 测试场景强制恢复成 'unit-token'，保证 X-Pineglow-Token 契约断言有可比对的基线。
	ctx.window.SG.apiToken = 'unit-token';
	ctx.window.SG.api.diagnoseInstance = async function (id, opts) {
		captured.diagnose.push({ id, opts });
		// pending promise that listens to signal abort
		return await new Promise((resolve, reject) => {
			if (opts && opts.signal) {
				if (opts.signal.aborted) {
					const e = new Error('aborted');
					e.name = 'AbortError';
					return reject(e);
				}
				const onAbort = () => {
					const e = new Error('aborted');
					e.name = 'AbortError';
					reject(e);
				};
				try { opts.signal.addEventListener('abort', onAbort); } catch (_) { /* ignore */ }
			}
		});
	};
	ctx.window.SG.api.cancelDiagnoseInstance = async function (id, operationId, opts) {
		captured.cancel.push({ id, operationId, opts: opts || {} });
		return { ok: true, cancelled: true };
	};
	return captured;
}

function installResolveDiagnoseStub(ctx) {
	const captured = { diagnose: [], cancel: [] };
	ctx.window.SG.api.diagnoseInstance = async function (id, opts) {
		captured.diagnose.push({ id, opts });
		return {
			ok: true,
			rule: { summary: '本地规则', suggestions: ['建议1'] },
			mods: { issues: [] },
			aiResult: { ok: true, status: 'insufficient_information', confidence: 1.0, summary: '本地分析', causes: [{ title: '本地规则', description: '本地规则', kind: 'fact', confidence: 1.0, evidence: ['建议1'] }], actions: [], warnings: ['本地分析'], missingInformation: [], errorCode: null },
			suspectMods: [],
			hasTail: true,
			snapshot: null,
			aiError: null,
			configured: true,
		};
	};
	ctx.window.SG.api.cancelDiagnoseInstance = async function (id, operationId, opts) {
		captured.cancel.push({ id, operationId, opts: opts || {} });
		return { ok: true, cancelled: true };
	};
	return captured;
}

test('RED-B1: openAiDiagnose + Local only must generate and pass operationId to diagnoseInstance', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installResolveDiagnoseStub(ctx);
	// also stub cancelDiagnoseInstance so we can detect any calls (currently 0)
	captured.cancel = [];
	ctx.window.SG.api.cancelDiagnoseInstance = async function (id, operationId, opts) {
		captured.cancel.push({ id, operationId, opts: opts || {} });
		return { ok: true, cancelled: true };
	};
	await launcher.openAiDiagnose({ id: 'inst-red-b1', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	const localBtn = host.querySelector('#dConfirmLocal');
	assert.ok(localBtn, 'dConfirmLocal must exist: ' + (host.innerHTML || '').slice(0, 500));
	localBtn.click();
	if (typeof console !== 'undefined') console.error('[DBG]', typeof localBtn.onclick);
	// wait for diagnoseInstance to be called
	await waitFor(() => captured.diagnose.length >= 1);
	const call = captured.diagnose[0];
	assert.equal(call.id, 'inst-red-b1', 'diagnose must be called for the right instanceId');
	assert.ok(call.opts && typeof call.opts.operationId === 'string' && call.opts.operationId.length > 0, `operationId must be a non-empty string in the diagnose opts (currently RED: actual=${JSON.stringify(call.opts)})`);
});

test('RED-B2: running cancel must call cancelDiagnoseInstance with same instanceId/operationId', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installCaptureStubs(ctx);
	await launcher.openAiDiagnose({ id: 'inst-red-b2', version: '1.20.1', loader: 'Forge' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	// wait until diagnoseInstance is invoked (operationId captured)
	await waitFor(() => captured.diagnose.length >= 1);
	// Click the running-cancel button (dCancelRequest) to trigger cancel
	await waitFor(() => host.querySelector('#dCancelRequest'));
	const cancelReqBtn = host.querySelector('#dCancelRequest');
	cancelReqBtn.click();
	// wait for cancelDiagnoseInstance to be invoked
	await waitFor(() => captured.cancel.length >= 1, 500).catch(() => { /* ignore timeout, asserted below */ });
	const cancelCall = captured.cancel[0];
	assert.ok(cancelCall, `cancelDiagnoseInstance must be called (currently RED: no cancel call). diagnoseInstance opts: ${JSON.stringify(captured.diagnose[0] && captured.diagnose[0].opts)}`);
	assert.equal(cancelCall.id, 'inst-red-b2', 'cancelDiagnoseInstance must use same instanceId');
	const diagnoseOpId = captured.diagnose[0].opts && captured.diagnose[0].opts.operationId;
	assert.equal(cancelCall.operationId, diagnoseOpId, 'cancelDiagnoseInstance must use same operationId');

});

test('RED-B3: each operation must trigger cancelDiagnoseInstance at most once across close/mask/unload', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installCaptureStubs(ctx);
	await launcher.openAiDiagnose({ id: 'inst-red-b3', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	await waitFor(() => captured.diagnose.length >= 1);
	await waitFor(() => host.querySelector('#dCancelRequest'));
	host.querySelector('#dCancelRequest').click();
	await sleep(80);
	// Now spam additional close/mask triggers; each must NOT add a new cancel call
	// 1) click cancelBtn (close)
	const cancelBtn = host.querySelector('#dConfirmCancel');
	if (cancelBtn) cancelBtn.click();
	await sleep(20);
	// 2) closeModal direct call
	if (typeof launcher.closeModal === 'function') launcher.closeModal();
	await sleep(20);
	// 3) re-click dCancelRequest (would be no-op since modal closed)
	const cancelReqBtn2 = host.querySelector('#dCancelRequest');
	if (cancelReqBtn2) cancelReqBtn2.click();
	await sleep(20);
	// cancel count must be exactly 1 (currently RED: 0)
	assert.equal(captured.cancel.length, 1, `each operation must produce at most one cancel call (currently RED: cancel count=${captured.cancel.length}, diagnose opts=${JSON.stringify(captured.diagnose[0] && captured.diagnose[0].opts)})`);
});

test('RED-B4: natural completion must NOT trigger cancel when result modal is closed', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installResolveDiagnoseStub(ctx);
	captured.cancel = [];
	ctx.window.SG.api.cancelDiagnoseInstance = async function (id, operationId, opts) {
		captured.cancel.push({ id, operationId, opts: opts || {} });
		return { ok: true, cancelled: true };
	};
	await launcher.openAiDiagnose({ id: 'inst-red-b4', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	// wait until runDiag completes: confirm panel content is replaced by diag-privacy
	await sleep(300);
	if (typeof console !== 'undefined') console.error('[B4] innerHTML len=', host.innerHTML.length, 'children=', host.children.length);
	// close the result modal via the confirm-panel close button
	const cancelBtn = host.querySelector('#dConfirmCancel');
	cancelBtn.click();
	await sleep(50);
	assert.equal(captured.cancel.length, 0, `natural completion + close must not call cancelDiagnoseInstance (currently passes but for the wrong reason: cancel is never called at all)`);
});

test('RED-B5: confirm-panel direct cancel must NOT generate operationId, call diagnose, or call cancel', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installResolveDiagnoseStub(ctx);
	captured.cancel = [];
	ctx.window.SG.api.cancelDiagnoseInstance = async function (id, operationId, opts) {
		captured.cancel.push({ id, operationId, opts: opts || {} });
		return { ok: true, cancelled: true };
	};
	await launcher.openAiDiagnose({ id: 'inst-red-b5', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	// click dConfirmCancel directly (never click Local only)
	host.querySelector('#dConfirmCancel').click();
	await sleep(50);
	assert.equal(captured.diagnose.length, 0, 'direct cancel on confirm panel must not call diagnoseInstance');
	assert.equal(captured.cancel.length, 0, 'direct cancel on confirm panel must not call cancelDiagnoseInstance');
});
/* ============== Phase 3B v4 NEW: granular trigger paths ============== */
/* 范围：仅 ui/tests/ai-diagnose.test.cjs，新增 v4 granular 触发路径测试和最小必要 stub。 */
/* 不动 api-contract.test.cjs、不动生产代码、不复制生产逻辑、不动旧测试预期。 */

function installWindowEventTracker(ctx) {
	const windowListeners = {};
	ctx.addEventListener = function (type, handler) {
		if (!windowListeners[type]) windowListeners[type] = [];
		windowListeners[type].push(handler);
	};
	ctx.removeEventListener = function (type, handler) {
		if (!windowListeners[type]) return;
		windowListeners[type] = windowListeners[type].filter(h => h !== handler);
	};
	ctx.fireWindowEvent = function (type, event) {
		(windowListeners[type] || []).forEach(h => {
			try { h(event || {}); } catch (e) { }
		});
	};
	return ctx;
}

function installXhrStub(ctx, captured) {
	captured.xhr = captured.xhr || [];
	const XHR = function () {
		this._method = null;
		this._url = null;
		this._body = null;
		this._headers = {};
		this._sync = false;
	};
	XHR.prototype.open = function (method, url, sync) {
		this._method = method;
		this._url = url;
		this._sync = !!sync;
	};
	XHR.prototype.setRequestHeader = function (k, v) {
		this._headers[k] = v;
	};
	XHR.prototype.send = function (body) {
		this._body = body;
		captured.xhr.push({
			method: this._method,
			url: this._url,
			body: body,
			headers: this._headers,
			sync: this._sync,
		});
	};
	ctx.XMLHttpRequest = XHR;
	ctx.window.XMLHttpRequest = XHR;
	return captured;
}

async function waitForSettledResult(host, timeoutMs) {
	if (typeof timeoutMs !== 'number') timeoutMs = 1500;
	const start = Date.now();
	while (true) {
		const content = host && host.findById ? host.findById('diagContent') : null;
		if (content && content.innerHTML && content.innerHTML.indexOf('diag-rule-summary') >= 0) return true;
		if (Date.now() - start > timeoutMs) return false;
		await new Promise(r => setTimeout(r, 5));
	}
}

test('V4-NEW-1 mask-close: fresh pending + mask click must call cancelDiagnoseInstance with same instanceId/operationId', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installCaptureStubs(ctx);
	await launcher.openAiDiagnose({ id: 'inst-v4-mask', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	await waitFor(() => captured.diagnose.length >= 1);
	const diagnoseOpId = captured.diagnose[0].opts && captured.diagnose[0].opts.operationId;
	host.click();
	await new Promise(r => setTimeout(r, 80));
	assert.ok(captured.cancel.length >= 1, 'mask/close must call cancelDiagnoseInstance (cancel=' + captured.cancel.length + ', xhr=' + JSON.stringify(captured.xhr || []) + ')');
	assert.equal(captured.cancel[0].id, 'inst-v4-mask', 'mask/close must use same instanceId');
	assert.equal(captured.cancel[0].operationId, diagnoseOpId, 'mask/close must use same operationId');
});

test('V4-NEW-2 navigation: fresh pending + goTo must call cancelDiagnoseInstance with same instanceId/operationId', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installCaptureStubs(ctx);
	await launcher.openAiDiagnose({ id: 'inst-v4-nav', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	await waitFor(() => captured.diagnose.length >= 1);
	const diagnoseOpId = captured.diagnose[0].opts && captured.diagnose[0].opts.operationId;
	// 真实生产 goTo 通过 __aiDiagnostics 暴露。launcher.js 的 IIFE 让 vm.runInContext('goTo("home")')
	// 拿不到函数引用，但生产路径下脚本全局存在；这里走 launcher 对象上的导出函数，保证命中真实 goTo。
	assert.equal(typeof launcher.goTo, 'function', 'production goTo must be exposed via __aiDiagnostics for test reach');
	const p = launcher.goTo('home');
	if (p && typeof p.then === 'function') await p;
	await new Promise(r => setTimeout(r, 80));
	// at-most-once：一个 operation 在 goTo 触发下只能调 1 次 cancel（不允许 >= 1 模糊断言）。
	assert.equal(captured.cancel.length, 1, 'goTo navigation must call cancelDiagnoseInstance exactly once per operation (currently RED or weak). diagnoseOpId=' + diagnoseOpId + ', cancel=' + JSON.stringify(captured.cancel.map(c => ({ id: c.id, op: c.operationId }))) + ', xhr=' + JSON.stringify(captured.xhr || []));
	assert.equal(captured.cancel[0].id, 'inst-v4-nav', 'navigation must use same instanceId');
	assert.equal(captured.cancel[0].operationId, diagnoseOpId, 'navigation must use same operationId');
});

test('V4-NEW-3 beforeunload: fresh pending + window beforeunload must trigger exactly one authenticated XHR cancel', async () => {
	const ctx = makeLoaderContext();
	installWindowEventTracker(ctx);
	const launcher = loadRealLauncher(ctx);
	const captured = installCaptureStubs(ctx);
	installXhrStub(ctx, captured);
	await launcher.openAiDiagnose({ id: 'inst-v4-bu', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	await waitFor(() => captured.diagnose.length >= 1);
	const diagnoseOpId = captured.diagnose[0].opts && captured.diagnose[0].opts.operationId;
	ctx.fireWindowEvent('beforeunload', {});
	await new Promise(r => setTimeout(r, 50));
	// 真实契约断言：exactly 1 XHR，POST，cancel 端点（含 instanceId），operationId 对齐，
	// X-Pineglow-Token 携带且非空，async 参数 = false（同步 best-effort），不得用 fetch cancelDiagnoseInstance。
	assert.equal(captured.xhr.length, 1, 'beforeunload must fire exactly one XHR (currently RED: cancel=' + JSON.stringify(captured.cancel.map(c => ({ id: c.id, op: c.operationId }))) + ', xhr=' + JSON.stringify(captured.xhr) + ')');
	const x = captured.xhr[0];
	assert.equal(x.method, 'POST', 'cancel XHR must be POST');
	assert.equal(x.url, '/api/instances/inst-v4-bu/diagnose/cancel', 'cancel XHR must hit /api/instances/{instanceId}/diagnose/cancel');
	let parsed;
	try { parsed = JSON.parse(x.body); } catch (e) { parsed = null; }
	assert.ok(parsed && typeof parsed.operationId === 'string', 'cancel XHR body must be JSON with operationId string (body=' + JSON.stringify(x.body) + ')');
	assert.equal(parsed.operationId, diagnoseOpId, 'cancel XHR operationId must equal diagnose operationId');
	assert.equal(x.headers['Content-Type'], 'application/json', 'cancel XHR must set Content-Type: application/json');
	assert.equal(typeof x.headers['X-Pineglow-Token'], 'string', 'cancel XHR must carry X-Pineglow-Token header');
	assert.equal(x.headers['X-Pineglow-Token'], 'unit-token', 'X-Pineglow-Token must equal SG.apiToken at unload (' + JSON.stringify(x.headers) + ')');
	assert.equal(x.sync, false, 'cancel XHR must be synchronous (xhr.open async=false per v4 prompt §3 P1-2 best-effort contract)');
	assert.equal(captured.cancel.length, 0, 'beforeunload must NOT additionally fire async cancelDiagnoseInstance (only XHR best-effort)');
});

test('V4-NEW-4 pagehide: fresh pending + window pagehide must trigger exactly one authenticated XHR cancel; beforeunload+pagehide still totals one', async () => {
	const ctx = makeLoaderContext();
	installWindowEventTracker(ctx);
	const launcher = loadRealLauncher(ctx);
	const captured = installCaptureStubs(ctx);
	installXhrStub(ctx, captured);
	await launcher.openAiDiagnose({ id: 'inst-v4-ph', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	await waitFor(() => captured.diagnose.length >= 1);
	const diagnoseOpId = captured.diagnose[0].opts && captured.diagnose[0].opts.operationId;
	ctx.fireWindowEvent('pagehide', {});
	await new Promise(r => setTimeout(r, 50));
	// 同 V4-NEW-3 的强断言
	assert.equal(captured.xhr.length, 1, 'pagehide must fire exactly one XHR (currently RED: cancel=' + JSON.stringify(captured.cancel.map(c => ({ id: c.id, op: c.operationId }))) + ', xhr=' + JSON.stringify(captured.xhr) + ')');
	const x = captured.xhr[0];
	assert.equal(x.method, 'POST', 'cancel XHR must be POST');
	assert.equal(x.url, '/api/instances/inst-v4-ph/diagnose/cancel', 'cancel XHR must hit /api/instances/{instanceId}/diagnose/cancel');
	let parsed;
	try { parsed = JSON.parse(x.body); } catch (e) { parsed = null; }
	assert.ok(parsed && typeof parsed.operationId === 'string', 'cancel XHR body must be JSON with operationId string (body=' + JSON.stringify(x.body) + ')');
	assert.equal(parsed.operationId, diagnoseOpId, 'cancel XHR operationId must equal diagnose operationId');
	assert.equal(x.headers['Content-Type'], 'application/json', 'cancel XHR must set Content-Type: application/json');
	assert.equal(typeof x.headers['X-Pineglow-Token'], 'string', 'cancel XHR must carry X-Pineglow-Token header');
	assert.equal(x.headers['X-Pineglow-Token'], 'unit-token', 'X-Pineglow-Token must equal SG.apiToken at unload (' + JSON.stringify(x.headers) + ')');
	assert.equal(x.sync, false, 'cancel XHR must be synchronous (xhr.open async=false per v4 prompt §3 P1-2 best-effort contract)');
	assert.equal(captured.cancel.length, 0, 'pagehide must NOT additionally fire async cancelDiagnoseInstance (only XHR best-effort)');
	// at-most-once 覆盖：再触发一次 beforeunload，total XHR 仍为 1（已发过 cancelSent=true）
	ctx.fireWindowEvent('beforeunload', {});
	await new Promise(r => setTimeout(r, 50));
	assert.equal(captured.xhr.length, 1, 'pagehide+beforeunload across one operation must total exactly one XHR (at-most-once; cancelSent guard)');
	assert.equal(captured.cancel.length, 0, 'no async cancelDiagnoseInstance at any unload path');
});

test('V4-NEW-5 natural-completion: fresh pending + natural completion + close must NOT trigger cancel', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installResolveDiagnoseStub(ctx);
	await launcher.openAiDiagnose({ id: 'inst-v4-settle', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	const settled = await waitForSettledResult(host, 1500);
	assert.ok(settled, 'natural completion must render result content (.diag-rule-summary)');
	const cancelBtn = host.querySelector('#dConfirmCancel');
	if (cancelBtn) cancelBtn.click();
	await new Promise(r => setTimeout(r, 50));
	assert.equal(captured.cancel.length, 0, 'natural completion + close must NOT trigger cancel (cancel=' + captured.cancel.length + ', xhr=' + JSON.stringify(captured.xhr || []) + ')');
});

test('V4-NEW-6 confirm-cancel: direct confirm cancel must NOT call ID generator, diagnose, or cancel', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	const captured = installCaptureStubs(ctx);
	let uuidCount = 0;
	let getRandomValuesCount = 0;
	const origRandomUUID = ctx.crypto && ctx.crypto.randomUUID;
	const origGetRandomValues = ctx.crypto && ctx.crypto.getRandomValues;
	if (ctx.crypto) {
		if (typeof origRandomUUID === 'function') {
			ctx.crypto.randomUUID = function () {
				uuidCount++;
				return origRandomUUID.call(ctx.crypto);
			};
		}
		if (typeof origGetRandomValues === 'function') {
			ctx.crypto.getRandomValues = function (arr) {
				getRandomValuesCount++;
				return origGetRandomValues.call(ctx.crypto, arr);
			};
		}
	}
	await launcher.openAiDiagnose({ id: 'inst-v4-confirm', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmCancel').click();
	await new Promise(r => setTimeout(r, 50));
	assert.equal(uuidCount, 0, 'crypto.randomUUID must NOT be called on direct confirm cancel (currently: ' + uuidCount + ')');
	assert.equal(getRandomValuesCount, 0, 'crypto.getRandomValues must NOT be called on direct confirm cancel (currently: ' + getRandomValuesCount + ')');
	assert.equal(captured.diagnose.length, 0, 'direct confirm cancel must not call diagnoseInstance');
	assert.equal(captured.cancel.length, 0, 'direct confirm cancel must not call cancelDiagnoseInstance');
});

/* ============== Phase 3C-1: AI 诊断结果可解释性 + 诚实状态 ==============
 * 范围（仅 ui/js/launcher.js 的诊断区域 / ui/js/i18n.js 的诊断键 / ui/css/figma.css 诊断样式）：
 *   - kind 本地化（fact/inference/unknown + 未知降级）
 *   - 置信度边界（NaN / 超界 / 0% 误显 / 低值警告 / 模型估计≠概率 提示）
 *   - 证据与信息缺口分别展示（空证据不画空 ul；missingInformation 有独立标签/样式）
 *   - 未知 actionType 不出现可点击按钮；disable_mod/suspectMods 仍 confirm（不回归）
 *   - 取消后旧响应不可覆盖 UI（沿用 Phase 3B token 校验；新增 3C-1 视角断言）
 *   - 等待状态不伪造"读取→规则→AI"精确阶段（静态源码契约锁）
 *   - 中英本地化键完整性
 * 关键：makeLoaderContext() 的 DOM 中，host.innerHTML 是缓存字符串，子元素 innerHTML 更新不会反映在 host.innerHTML 上。
 *   因此：等待渲染完成必须通过 host.findById('diagContent').innerHTML；断言尽量对真实生成的 innerHTML 字符串做语义模式匹配，
 *   避免依赖 querySelector('.classname') —— 后者在此 stub 里不识别 class 选择器。
 */

function installResolvedAiResult(ctx, aiResult) {
	const captured = { diagnose: [], cancel: [] };
	ctx.window.SG.api.diagnoseInstance = async function (id, opts) {
		captured.diagnose.push({ id, opts });
		return {
			ok: true,
			rule: { summary: '本地规则摘要', suggestions: ['本地建议 A'] },
			mods: { issues: [] },
			aiResult,
			suspectMods: [],
			hasTail: true,
			snapshot: null,
			aiError: null,
			configured: true,
		};
	};
	ctx.window.SG.api.cancelDiagnoseInstance = async function (id, operationId, opts) {
		captured.cancel.push({ id, operationId, opts: opts || {} });
		return { ok: true, cancelled: true };
	};
	return captured;
}

// 读取最新渲染的 diagContent.innerHTML（host.innerHTML 是缓存字符串，不反映子节点更新）
function readLatestDiagContent(host) {
	const c = host.findById && host.findById('diagContent');
	return (c && c.innerHTML) || host.innerHTML || '';
}

async function runAiDiagnoseToResult(ctx, opts) {
	const launcher = loadRealLauncher(ctx);
	// 必须在 loadRealLauncher 之后调用 setLocale（loadRealLauncher 才执行 i18n.js 注入 SG.setLocale）。
	if (opts.locale) ctx.window.SG.setLocale(opts.locale);
	const captured = installResolvedAiResult(ctx, opts.aiResult);
	await launcher.openAiDiagnose({ id: opts.instanceId || 'inst-3c1', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	// Phase 3C-1 (second pass)：本 helper 用于"AI 路径"断言 —— 必须点"发送给 AI"按钮，
	// 让 selectedMode='ai' 透传到 renderResult，AI 块才会被渲染（local-only 路径会抑制 AI 块）。
	host.querySelector('#dConfirmSend').click();
	// 渲染完成的稳定标记：diagContent 子树中既包含规则摘要又包含隐私页脚
	await waitFor(() => {
		const c = readLatestDiagContent(host);
		return c.indexOf('diag-rule-summary') >= 0 && c.indexOf('diag-privacy') >= 0;
	}, opts.timeoutMs || 2000);
	return { host, captured };
}

test('3C-1 R1: cause.kind=fact renders localized fact badge (zh) — current RED: shows raw "fact"', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '示例原因', description: '示例描述', kind: 'fact', confidence: 0.9, evidence: ['证据 A'] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(/diag-kind-fact/.test(html), `fact kind must render a .diag-kind-fact badge; html=${html.slice(0, 800)}`);
	assert.ok(html.indexOf('事实') >= 0, `fact kind must render localized label 事实 (zh); html=${html.slice(0, 800)}`);
	// 必须有 data-diag-cause-kind 属性证明 kind 被规范化为 fact
	const causeKinds = host.findAllByAttr('[data-diag-cause-kind="fact"]') || [];
	assert.ok(causeKinds.length >= 1, `expected at least one cause with data-diag-cause-kind="fact"; html=${html.slice(0, 800)}`);
});

test('3C-1 R2: cause.kind=inference renders localized inference badge (en)', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'en',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI summary',
			causes: [{ title: 'Example cause', description: 'desc', kind: 'inference', confidence: 0.6, evidence: ['evidence A'] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(/diag-kind-inference/.test(html), `inference kind must render .diag-kind-inference badge; html=${html.slice(0, 800)}`);
	assert.ok(html.indexOf('Inference') >= 0, `inference kind must render localized label Inference (en); html=${html.slice(0, 800)}`);
});

test('3C-1 R3: unknown kind falls back to "未确定" / "Undetermined" — current RED: passes raw kind through', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '未知示例', description: '描述', kind: 'totally_unknown_kind_from_backend', confidence: 0.5, evidence: [] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(/diag-kind-unknown/.test(html), `unknown kind must render .diag-kind-unknown badge; html=${html.slice(0, 800)}`);
	assert.ok(html.indexOf('未确定') >= 0, `unknown kind must fall back to 未确定 (zh); html=${html.slice(0, 800)}`);
	// 关键：原始未受控枚举字符串不应出现在用户可见文本中（badge 内只显示本地化"未确定"）。
	// 由于原始 kind 可能用于 data-* 属性，这里用更宽松的可见区域检测：搜索 badge summary 的子串拼接来确认。
	assert.ok(html.indexOf('totally_unknown_kind_from_backend') < 0 || /data-diag-kind\s*=\s*"totally_unknown_kind_from_backend"/.test(html),
		`raw kind must not leak into visible text unless as data attribute; html=${html.slice(0, 800)}`);
});

test('3C-1 R4: confidence=NaN must NOT render misleading 0% — current RED: shows 置信度: 0%', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: Number.NaN, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'fact', confidence: 0.5, evidence: [] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	// 真实产品输出在无效 confidence 下绝对不能渲染 .diag-confidence；当前实现把 NaN 渲染成 "0%"。
	assert.ok(!/class\s*=\s*"diag-confidence"/.test(html),
		`confidence=NaN must NOT render .diag-confidence element (currently RED: shows misleading "0%"); html=${html.slice(0, 800)}`);
	assert.ok(!/置信度[：:]\s*0\s*[%％]/.test(html),
		`confidence=NaN must NOT render "置信度: 0%" pattern; html=${html.slice(0, 800)}`);
	assert.ok(!/NaN/.test(html), `confidence=NaN must NOT leak literal "NaN" string; html=${html.slice(0, 800)}`);
});

test('3C-1 R5: confidence=1.5 must NOT render 150% — current RED: shows 150%', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 1.5, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'fact', confidence: 0.5, evidence: [] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(!/class\s*=\s*"diag-confidence"/.test(html),
		`confidence=1.5 must NOT render .diag-confidence element (out-of-range); html=${html.slice(0, 800)}`);
	assert.ok(!/置信度[：:]\s*1?5?0\s*[%％]/.test(html),
		`confidence=1.5 must NOT render 150% pattern; html=${html.slice(0, 800)}`);
});

test('3C-1 R6: confidence=0.2 must render low-confidence warning — current RED: no warning at all', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.2, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'inference', confidence: 0.2, evidence: [] }],
			actions: [], warnings: [], missingInformation: ['请提供最近日志路径'], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(/diag-low-confidence/.test(html) || html.indexOf('低置信度') >= 0 || html.indexOf('lowConfidence') >= 0,
		`low confidence must produce diag-low-confidence section + 低置信度 label; html=${html.slice(0, 1000)}`);
	// 信息缺口必须独立显示，不能合并到证据列表
	assert.ok(/diag-missing-info|diag-missing/.test(html),
		`missingInformation must use a distinct CSS class; html=${html.slice(0, 1000)}`);
});

test('3C-1 R7: confidence disclaimer "≠ 概率" must accompany any rendered confidence — current RED: no disclaimer', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'inference', confidence: 0.6, evidence: ['证据'] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(/class\s*=\s*"diag-confidence"/.test(html),
		`valid confidence=0.7 must still render .diag-confidence; html=${html.slice(0, 800)}`);
	assert.ok(/diag-confidence-note|≠\s*概率|不等于\s*概率|model.*estimate|model estimate|not.*probability/i.test(html),
		`confidence display must include disclaimer that model estimate ≠ probability; html=${html.slice(0, 1000)}`);
});

test('3C-1 R8: empty evidence list must NOT render empty <ul> — current behavior: passes (skip via if length)', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'fact', confidence: 0.5, evidence: [] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	// evidence=[] → 不应有 .diag-evidence 列表；契约：空列表不画
	assert.ok(!/class\s*=\s*"diag-evidence"/.test(html),
		`empty evidence must not render .diag-evidence list; html=${html.slice(0, 1000)}`);
});

test('3C-1 R9: missingInformation must render with distinct CSS class — current behavior: reuses diag-mods-title', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'fact', confidence: 0.5, evidence: ['证据'] }],
			actions: [], warnings: [], missingInformation: ['请提供最近日志路径', '请提供 JVM 参数'], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(/class\s*=\s*"diag-evidence"/.test(html),
		`evidence list must still render when evidence[] has items; html=${html.slice(0, 1000)}`);
	// 信息缺口列表必须使用独立类名（不是 diag-mods-title 复用）
	assert.ok(html.indexOf('diag-missing-info') >= 0,
		`missingInformation must use distinct class diag-missing-info; html=${html.slice(0, 1000)}`);
	// 证据列表与信息缺口列表是不同元素（独立 ul 节点）
	const evIdx = html.indexOf('class="diag-evidence"');
	const missIdx = html.indexOf('diag-missing-info');
	assert.ok(evIdx >= 0 && missIdx >= 0, 'both evidence and missingInformation lists must exist');
});

test('3C-1 R10: unknown actionType must NOT render a clickable button — current RED: every action gets a button', async () => {
	const ctx = makeLoaderContext();
	let toggleCalls = 0;
	ctx.window.SG.api.toggleMod = async () => { toggleCalls++; return { ok: true }; };
	ctx.window.confirm = () => true;
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'inference', confidence: 0.6, evidence: [] }],
			actions: [
				{ id: 'a1', title: '删除世界', description: '危险', risk: 'high', reversible: false, actionType: 'delete_world', target: 'overworld' },
				{ id: 'a2', title: '禁用 Mod', description: '禁用示例 mod.jar', risk: 'low', reversible: true, actionType: 'disable_mod', target: 'mod.jar' },
			],
			warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	// 未知 actionType 必须不出现可点击按钮。当前实现：所有 action 都渲染 <button data-action=...>
	assert.ok(!/button[^>]*data-action\s*=\s*"delete_world"/i.test(html),
		`unknown actionType must NOT render a clickable button; html snippet=${html.match(/[\s\S]{0,200}delete_world[\s\S]{0,200}/)?.[0] || 'n/a'}`);
	// 安全 actionType（disable_mod）仍须渲染可点击按钮（Phase 3B A-05 不回归）
	assert.ok(/button[^>]*data-action\s*=\s*"disable_mod"[^>]*data-target\s*=\s*"mod\.jar"/i.test(html),
		`disable_mod button must still render for known actionType; html=${html.slice(0, 1500)}`);
});

test('3C-1 R11: structured disable_mod still requires confirm() — Phase 3B A-05 contract lock (zh)', async () => {
	const ctx = makeLoaderContext();
	let confirmCalls = 0;
	let toggleCalls = 0;
	ctx.window.confirm = (msg) => { confirmCalls++; return false; };
	ctx.window.SG.api.toggleMod = async () => { toggleCalls++; return { ok: true }; };
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '示例', description: '描述', kind: 'inference', confidence: 0.6, evidence: [] }],
			actions: [{ id: 'a1', title: '禁用 Mod', description: '禁用 mod.jar', risk: 'high', reversible: true, actionType: 'disable_mod', target: 'mod.jar' }],
			warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	assert.ok(/button[^>]*data-action\s*=\s*"disable_mod"[^>]*data-target\s*=\s*"mod\.jar"/i.test(html),
		`disable_mod button must be rendered; html=${html.slice(0, 1500)}`);
	const btn = host.findByAttr && host.findByAttr('[data-action="disable_mod"]');
	assert.ok(btn, 'disable_mod button must be reachable via findByAttr');
	if (btn) btn.click();
	await new Promise(r => setTimeout(r, 30));
	assert.ok(confirmCalls >= 1, 'disable_mod click must invoke confirm() at least once (Phase 3B A-05 contract lock)');
	assert.equal(toggleCalls, 0, 'disable_mod click with confirm=false must NOT call toggleMod');
});

test('3C-1 R12: cancel-after-resolve race must NOT let late response overwrite UI', async () => {
	// 构造延迟 promise：先 cancel（token+1），再 resolve → renderResult 必须因 myToken !== token 而放弃写 UI。
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	let resolveDiagnose;
	const pending = new Promise(res => { resolveDiagnose = res; });
	ctx.window.SG.api.diagnoseInstance = async function (id, opts) { return pending; };
	ctx.window.SG.api.cancelDiagnoseInstance = async () => ({ ok: true, cancelled: true });

	await launcher.openAiDiagnose({ id: 'inst-3c1-race', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	// 等 runDiag 进入 await pending
	await new Promise(r => setTimeout(r, 60));
	// 触发 cancel → teardown → token++
	const cancelReqBtn = host.findByAttr && host.findByAttr('[id="dCancelRequest"]');
	assert.ok(cancelReqBtn, 'dCancelRequest must exist during pending');
	cancelReqBtn.click();
	await new Promise(r => setTimeout(r, 30));
	// 关键：用 findById 读最新 diagContent（host.innerHTML 是缓存字符串）。
	const htmlAfterCancel = readLatestDiagContent(host);
	// 旧响应现在 resolve —— 必须被 token 校验拦截
	resolveDiagnose({
		ok: true,
		rule: { summary: '旧响应', suggestions: [] },
		mods: { issues: [] },
		aiResult: { ok: true, status: 'resolved', confidence: 0.9, summary: '旧响应 AI', evidence: [], causes: [{ title: 'X', kind: 'fact', evidence: [] }], actions: [], warnings: [], missingInformation: [], errorCode: null },
		suspectMods: [],
	});
	await new Promise(r => setTimeout(r, 60));
	const htmlAfterLateResolve = readLatestDiagContent(host);
	assert.equal(htmlAfterLateResolve, htmlAfterCancel, 'late resolved response must NOT overwrite post-cancel UI; len before=' + htmlAfterCancel.length + ' after=' + htmlAfterLateResolve.length);
	assert.ok(htmlAfterLateResolve.indexOf('旧响应 AI') < 0, 'late resolved response summary must not leak into UI; html=' + htmlAfterLateResolve.slice(0, 400));
});

test('3C-1 L13: launcher.js must NOT use timer-driven fake stage progression (contract lock — currently passes)', () => {
	// 当前实现已正确：等待文案"正在分析崩溃日志…"是单一静态句，不随时间变化。
	// 本测试为契约锁，未来若引入 setTimeout/setInterval + stage/phase/elapsed 伪造后端阶段，应立即失败。
	const src = fs.readFileSync(path.resolve(__dirname, '../js/launcher.js'), 'utf8');
	const openAiStart = src.indexOf('async function openAiDiagnose');
	const openAiEnd = src.indexOf('\nasync function ', openAiStart + 30);
	const block = openAiEnd > openAiStart ? src.slice(openAiStart, openAiEnd) : src.slice(openAiStart);
	const hasFakeStage = /setTimeout[\s\S]{0,300}?(stage|phase|elapsed|progress|step)/i.test(block)
		|| /setInterval[\s\S]{0,300}?(stage|phase|elapsed|progress|step)/i.test(block)
		|| /\bstage\s*\+\s*\+/.test(block);
	assert.ok(!hasFakeStage, 'openAiDiagnose must not implement timer-driven fake stage progression; block snippet: ' + block.slice(0, 500));
});

test('3C-1 R14: i18n must provide diag.kind.* and diag.confidenceNote/disclaimer keys in BOTH zh and en', () => {
	// loadRealLauncher 内部已经 vm.createContext + 跑 i18n.js + 跑 api.js + 跑 launcher.js
	// 必须用这个入口拿到合法的 vm Context，不能直接对裸 ctx 调 vm.runInContext。
	const ctx = makeLoaderContext();
	loadRealLauncher(ctx);
	const t = ctx.window.SG.t;
	const setLocale = ctx.window.SG.setLocale;
	assert.equal(typeof t, 'function', 'SG.t must be a function after i18n.js loaded');
	assert.equal(typeof setLocale, 'function', 'SG.setLocale must be a function after i18n.js loaded');
	const required = [
		'diag.kind.fact',
		'diag.kind.inference',
		'diag.kind.unknown',
		'diag.confidenceNote',
		'diag.lowConfidence',
		'diag.evidenceTitle',
		'diag.missingInfoTitle',
		'diag.actionUnknown',
	];
	for (const loc of ['zh', 'en']) {
		setLocale(loc);
		for (const key of required) {
			const v = t(key);
			assert.notEqual(v, key, `${key} must resolve to a non-key string in locale=${loc}; got: ${v}`);
			assert.ok(v && v.length > 0, `${key} in locale=${loc} must not be empty`);
		}
	}
});

test('3C-1 R15: configured=false path must NOT show AI confidence even if mock supplies one', async () => {
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	ctx.window.SG.setLocale('zh');
	// 干净路径：configured=false 且 backend 未返回 errorCode（最常见场景——用户根本没配置 AI）
	ctx.window.SG.api.diagnoseInstance = async () => ({
		ok: true,
		rule: { summary: '本地', suggestions: [] },
		mods: { issues: [] },
		aiResult: { ok: true, status: 'resolved', confidence: 0.8, summary: 'AI 摘要', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null },
		suspectMods: [],
		configured: false,
	});
	await launcher.openAiDiagnose({ id: 'inst-3c1-noconf', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	// 等规则摘要写入（renderResult 即使 configured=false 也会渲染 rule summary）
	await waitFor(() => readLatestDiagContent(host).indexOf('diag-rule-summary') >= 0, 2000);
	const html = readLatestDiagContent(host);
	assert.ok(!/置信度[：:]/.test(html), 'configured=false path must NOT show AI confidence; html=' + html.slice(0, 800));
	assert.ok(html.indexOf('未配置 AI') >= 0 || html.indexOf('notConfigured') >= 0, 'configured=false path must show notConfigured hint; html=' + html.slice(0, 800));
});

/* ============== Phase 3C-1 second-pass fixes ==============
 * 独立复核发现的真实语义缺陷：
 *  - A. Local-only 路径（C# AiDiagnoseService.BuildLocalOnlyResult 在 localOnly=true 时返回
 *    status=insufficient_information / confidence=1.0 / causes[].kind='fact'）被 renderResult 当作 AI
 *    结果展示"AI 分析 100%"，违反 3C-1 来源与置信度要求。
 *  - B. AI 模式下 cause.kind=fact 的 tooltip 写"来自本地规则 / From local rules"，把 AI 标记的事实
 * 冒充本地规则证据。
 *  - C. status=insufficient_information 即使 confidence=1.0 也应显式提示"信息不足"，不能被高置信度
 *    掩盖；missingInformation 独立列表必须保留。
 */

test('3C-1 R16: local-only path must NOT show AI block — backend BuildLocalOnlyResult returns status=insufficient_information + confidence=1.0 + kind=fact', async () => {
	// 关键：模拟真实 C# BuildLocalOnlyResult 的返回（status / confidence / causes.kind 都要一致）；
	// configured=true（用户已配置 AI，但选择"仅本地分析"）。
	const ctx = makeLoaderContext();
	const launcher = loadRealLauncher(ctx);
	ctx.window.SG.setLocale('zh');
	ctx.window.SG.api.diagnoseInstance = async () => ({
		ok: true,
		rule: { summary: '本地规则摘要', suggestions: ['本地建议 A'] },
		mods: { issues: [] },
		aiResult: {
			ok: true,
			status: 'insufficient_information',
			confidence: 1.0,
			summary: '本地分析结果',
			causes: [{ title: '本地规则', description: '本地规则摘要', kind: 'fact', confidence: 1.0, evidence: ['本地建议 A'] }],
			actions: [],
			warnings: ['未发送至 AI；显示本地规则与 Mod 健康分析'],
			missingInformation: ['ai_provider', 'user_confirmation'],
			errorCode: null,
		},
		suspectMods: [],
		configured: true,
	});
	await launcher.openAiDiagnose({ id: 'inst-3c1-local', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	// 用户选了"仅本地分析"
	const localBtn = host.findByAttr && host.findByAttr('[id="dConfirmLocal"]');
	assert.ok(localBtn, 'dConfirmLocal must exist');
	localBtn.click();
	// 等规则摘要写入
	await waitFor(() => readLatestDiagContent(host).indexOf('diag-rule-summary') >= 0, 2000);
	const html = readLatestDiagContent(host);
	// 必须不出现 AI 标题、AI 置信度、AI 操作按钮、AI kind badge
	assert.ok(!/AI 分析/.test(html), 'local-only path must NOT render AI 分析 title; html=' + html.slice(0, 1200));
	assert.ok(!/置信度[：:]/.test(html), 'local-only path must NOT render AI confidence; html=' + html.slice(0, 1200));
	assert.ok(!/diag-kind-fact/.test(html), 'local-only path must NOT render AI fact kind badge; html=' + html.slice(0, 1200));
	assert.ok(!/data-action=/.test(html), 'local-only path must NOT render any AI action button; html=' + html.slice(0, 1200));
	// 仍必须显示本地规则摘要与本地建议
	assert.ok(html.indexOf('本地规则摘要') >= 0 || html.indexOf('本地建议 A') >= 0, 'local-only path must still show rule summary; html=' + html.slice(0, 1200));
	// 必须有"未发送至 AI"之类的本地模式提示，避免用户误以为是 AI 结论
	assert.ok(html.indexOf('未发送至 AI') >= 0 || html.indexOf('仅本地分析') >= 0, 'local-only path must surface local-only hint; html=' + html.slice(0, 1200));
});

test('3C-1 R17: AI path fact-kind tooltip must NOT say "来自本地规则" — AI 标记为事实 ≠ 本地规则证据', async () => {
	// AI 模式下 kind=fact 的来源是模型，不是本地规则；tooltip 必须诚实说明这一点。
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'resolved', confidence: 0.7, summary: 'AI 摘要',
			causes: [{ title: '示例原因', description: '示例描述', kind: 'fact', confidence: 0.9, evidence: ['证据 A'] }],
			actions: [], warnings: [], missingInformation: [], errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	// 必须保留 kind badge（事实）+ 本地化标签
	assert.ok(/diag-kind-fact/.test(html), 'AI fact kind must still render badge; html=' + html.slice(0, 800));
	assert.ok(html.indexOf('事实') >= 0, 'AI fact kind must show 事实 label; html=' + html.slice(0, 800));
	// 关键：tooltip 不能写"来自本地规则"——AI 不等于本地规则
	assert.ok(!/来自本地规则/.test(html), 'AI fact-kind tooltip must NOT pretend to be local rule source; html=' + html.slice(0, 1200));
	// 必须有诚实说明：标记为事实，需核查证据
	const hasHonestTooltip = /AI\s*标记.*事实|AI marked.*fact|verify the evidence|仍需核查|核查证据/i.test(html);
	assert.ok(hasHonestTooltip, 'AI fact-kind tooltip must be honest about AI classification; html=' + html.slice(0, 1200));
});

test('3C-1 R18: status=insufficient_information must show "信息不足" banner even when confidence >= 0.4', async () => {
	// 关键：confidence=1.0 的 insufficient_information 仍必须显式提示信息不足，不能被高置信度掩盖。
	// 背景：C# BuildLocalOnlyResult 会返回 confidence=1.0 + status=insufficient_information。
	// 但即使在 AI 路径下 backend 也可能返回同样的组合——用户必须看到"信息不足"，而不是"100% 确定"。
	// 这里用 AI 路径（configured=true, status=insufficient_information）来锁定行为。
	const ctx = makeLoaderContext();
	const { host } = await runAiDiagnoseToResult(ctx, {
		locale: 'zh',
		aiResult: {
			ok: true, status: 'insufficient_information', confidence: 1.0, summary: 'AI 摘要',
			causes: [], actions: [], warnings: [],
			missingInformation: ['ai_provider', 'user_confirmation'],
			errorCode: null,
		},
	});
	const html = readLatestDiagContent(host);
	// 必须显式标记 insufficient_information（与一般 missingInformation 区分，避免被正常 missing info 掩盖）
	assert.ok(/信息不足|insufficient[_-]?information|diag-insufficient/i.test(html),
		'insufficient_information status must surface explicit insufficient banner; html=' + html.slice(0, 1200));
	// missingInformation 独立列表必须保留
	assert.ok(html.indexOf('ai_provider') >= 0 && html.indexOf('user_confirmation') >= 0,
		'missingInformation list must be rendered independently; html=' + html.slice(0, 1200));
	assert.ok(/diag-missing-info/.test(html), 'missingInformation must use distinct class; html=' + html.slice(0, 1200));
	// confidence 仍可显示为数值（>=0.4），但必须伴随 disclaimer 与 insufficient 提示；不能简单展示"100% 确定"
	const hasLowConfidenceStyle = /diag-confidence-low|diag-low-confidence|insufficient/i.test(html);
	assert.ok(hasLowConfidenceStyle, 'high-confidence insufficient_information must still surface low/insufficient hint; html=' + html.slice(0, 1200));
});

test('3C-1 R19: i18n must provide diag.insufficient* keys in BOTH zh and en', () => {
	const ctx = makeLoaderContext();
	loadRealLauncher(ctx);
	const t = ctx.window.SG.t;
	const setLocale = ctx.window.SG.setLocale;
	const required = [
		'diag.insufficientInfoTitle',
		'diag.insufficientInfoNote',
		'diag.kind.source.fact',
		'diag.kind.source.inference',
		'diag.kind.source.unknown',
		'diag.localOnlyHint',
	];
	for (const loc of ['zh', 'en']) {
		setLocale(loc);
		for (const key of required) {
			const v = t(key);
			assert.notEqual(v, key, `${key} must resolve to a non-key string in locale=${loc}; got: ${v}`);
			assert.ok(v && v.length > 0, `${key} in locale=${loc} must not be empty`);
		}
	}
});

/* =================== Phase 3C-2 P0 UI 聚焦行为测试 ===================
 * 关键契约（已就绪）：
 *  - 仅当 aiDispatch.sentAtUtc 是合法 ISO-8601 UTC/offset 时才显示 badge；
 *  - 时间字符串必须包含 GMT±hh:mm 显式偏移；
 *  - 本地模式 / 旧服务器（无 aiDispatch） / 缺时间 / 无效时间 绝不显示 badge；
 *  - 不降级到 sentNoTime / failedNoTime 之类文案；
 *  - providerName/model 即使包含 HTML 字符也必须经 esc() 转义，不能注入 script。
 * =================================================================== */

function diagDataWithDispatch(overrides) {
	return Object.assign({
		ok: true,
		hasTail: true,
		snapshot: { version: 1, id: 'snap-1', context: {} },
		rule: { summary: 'rule-summary', suggestions: [] },
		mods: { enabledCount: 0, disabledCount: 0, onlineManagedCount: 0, issues: [] },
		ai: null,
		aiResult: { ok: true, status: 'resolved', confidence: 0.8, summary: 'ai-summary', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: null },
		aiError: null,
		aiDispatch: { providerName: 'DeepSeek', model: 'deepseek-chat', sentAtUtc: '2026-09-24T10:00:00.000Z' },
		configured: true,
		suspectMods: [],
	}, overrides || {});
}

// 自定义 runner：允许完整控制 api.diagnoseInstance 返回值（含 aiDispatch / aiError / configured）
async function runDiagWithFullData(ctx, opts) {
	const launcher = loadRealLauncher(ctx);
	if (opts.locale) ctx.window.SG.setLocale(opts.locale);
	const captured = { diagnose: [], cancel: [] };
	ctx.window.SG.api.diagnoseInstance = async function (id, opts2) {
		captured.diagnose.push({ id, opts: opts2 });
		return opts.fullData;
	};
	await launcher.openAiDiagnose({ id: opts.instanceId || 'inst-3c2', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	if (opts.selectedMode === 'local') {
		const localBtn = host.querySelector('#dConfirmLocal');
		assert.ok(localBtn, 'dConfirmLocal button must exist for local mode path');
		localBtn.click();
	} else {
		host.querySelector('#dConfirmSend').click();
	}
	await waitFor(() => {
		const c = readLatestDiagContent(host);
		return c.indexOf('diag-rule-summary') >= 0 && c.indexOf('diag-privacy') >= 0;
	}, opts.timeoutMs || 2000);
	return { host, captured };
}

test('3C-2 U-01: valid aiDispatch (UTC) must render badge with GMT offset and provider/model', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: diagDataWithDispatch(),
		selectedMode: 'ai',
	});
	const html = readLatestDiagContent(host);
	assert.ok(/diag-dispatch\b/.test(html), 'valid aiDispatch must render badge; html=' + html.slice(0, 2000));
	assert.ok(/DeepSeek/.test(html), 'badge must show providerName; html=' + html.slice(0, 2000));
	assert.ok(/deepseek-chat/.test(html), 'badge must show model; html=' + html.slice(0, 2000));
	assert.ok(/GMT[+-]\d{2}:\d{2}/.test(html), 'badge must include explicit GMT offset; html=' + html.slice(0, 2000));
	assert.ok(/Request initiated to AI/.test(html), 'badge must use honest wording; html=' + html.slice(0, 2000));
	assert.ok(!/deliver|processed|successfully/i.test(html), 'badge must not claim delivery; html=' + html.slice(0, 2000));
});

test('3C-2 U-02: localOnly mode must NOT render dispatch badge even if server returns aiDispatch', async () => {
	const ctx = makeLoaderContext();
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: diagDataWithDispatch(),
		selectedMode: 'local',
	});
	const html = readLatestDiagContent(host);
	assert.ok(!/diag-dispatch\b/.test(html), 'localOnly must not render dispatch badge; html=' + html.slice(0, 2000));
	assert.ok(/Not sent to AI/.test(html), 'localOnly must surface local-only hint; html=' + html.slice(0, 2000));
});

test('3C-2 U-03: old server with NO aiDispatch field must NOT render dispatch badge', async () => {
	const ctx = makeLoaderContext();
	const oldServerData = diagDataWithDispatch();
	delete oldServerData.aiDispatch;
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: oldServerData,
		selectedMode: 'ai',
	});
	const html = readLatestDiagContent(host);
	assert.ok(!/diag-dispatch\b/.test(html), 'old server (no aiDispatch) must not render badge; html=' + html.slice(0, 2000));
});

test('3C-2 U-04: missing sentAtUtc must NOT render dispatch badge (no fallback to sentNoTime)', async () => {
	const ctx = makeLoaderContext();
	const noTimeData = diagDataWithDispatch({
		aiDispatch: { providerName: 'DeepSeek', model: 'deepseek-chat' }, // no sentAtUtc
	});
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: noTimeData,
		selectedMode: 'ai',
	});
	const html = readLatestDiagContent(host);
	assert.ok(!/diag-dispatch\b/.test(html), 'missing sentAtUtc must not render badge; html=' + html.slice(0, 2000));
	assert.ok(!/sentNoTime/i.test(html), 'must NOT fall back to sentNoTime text; html=' + html.slice(0, 2000));
});

test('3C-2 U-05: invalid sentAtUtc (no UTC suffix / offset) must NOT render dispatch badge', async () => {
	const ctx = makeLoaderContext();
	const invalidData = diagDataWithDispatch({
		aiDispatch: { providerName: 'DeepSeek', model: 'deepseek-chat', sentAtUtc: '2026-09-24 10:00:00' }, // no Z / offset
	});
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: invalidData,
		selectedMode: 'ai',
	});
	const html = readLatestDiagContent(host);
	assert.ok(!/diag-dispatch\b/.test(html), 'invalid sentAtUtc must not render badge; html=' + html.slice(0, 2000));
});

test('3C-2 U-06: malformed sentAtUtc (NaN-parseable) must NOT render dispatch badge', async () => {
	const ctx = makeLoaderContext();
	const malformedData = diagDataWithDispatch({
		aiDispatch: { providerName: 'DeepSeek', model: 'deepseek-chat', sentAtUtc: 'not-a-date' },
	});
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: malformedData,
		selectedMode: 'ai',
	});
	const html = readLatestDiagContent(host);
	assert.ok(!/diag-dispatch\b/.test(html), 'malformed sentAtUtc must not render badge; html=' + html.slice(0, 2000));
});

test('3C-2 U-07: failed/cancelled dispatch must show failed state badge (yellow)', async () => {
	const ctx = makeLoaderContext();
	const failData = diagDataWithDispatch({
		aiResult: { ok: false, status: 'failed', confidence: 0, summary: 'failed-summary', causes: [], actions: [], warnings: [], missingInformation: [], errorCode: 'ai_timeout' },
		aiError: 'ai_timeout: failed-summary',
		aiDispatch: { providerName: 'DeepSeek', model: 'deepseek-chat', sentAtUtc: '2026-09-24T10:00:00.000Z' },
	});
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: failData,
		selectedMode: 'ai',
	});
	const html = readLatestDiagContent(host);
	assert.ok(/diag-dispatch-failed/.test(html), 'failed dispatch must use failed style; html=' + html.slice(0, 2000));
	assert.ok(/diagnosis failed\/cancelled/i.test(html), 'failed dispatch must show honest failure wording; html=' + html.slice(0, 2000));
	assert.ok(!/class="diag-confidence/.test(html), 'failed AI response must not show a model confidence value; html=' + html.slice(0, 2000));
	assert.ok(!/failed-summary/.test(html), 'failed AI response must not render raw failure summary as an AI conclusion; html=' + html.slice(0, 2000));
});

test('3C-2 U-08: malicious providerName with HTML/script chars must be HTML-escaped', async () => {
	const ctx = makeLoaderContext();
	const xssData = diagDataWithDispatch({
		aiDispatch: {
			providerName: '<script>alert("xss")</script>',
			model: '"><img src=x onerror=alert(1)>',
			sentAtUtc: '2026-09-24T10:00:00.000Z',
		},
	});
	const { host } = await runDiagWithFullData(ctx, {
		locale: 'en',
		fullData: xssData,
		selectedMode: 'ai',
	});
	const html = readLatestDiagContent(host);
	assert.ok(!/<script>alert/.test(html), 'providerName HTML chars must be escaped (no raw <script>); html=' + html.slice(0, 2000));
	assert.ok(!/<img src=x/.test(html), 'model HTML chars must be escaped (no raw <img>); html=' + html.slice(0, 2000));
	assert.ok(/&lt;script&gt;alert/.test(html) || /&lt;img/.test(html), 'escaped chars must be present; html=' + html.slice(0, 2000));
});

test('MiniMax CN preset uses the exact approved endpoint', () => {
	const diag = loadRealLauncher(makeLoaderContext());
	assert.equal(diag.AI_PRESETS.minimax_cn.baseUrl, 'https://api.minimaxi.com/v1');
	assert.equal(diag.AI_PRESETS.minimax_cn.model, 'MiniMax-M3');
	assert.equal(diag.isFrontendEndpointAllowed('https://api.minimaxi.com/v1'), true);
	assert.equal(diag.isFrontendEndpointAllowed('https://api.minimaxi.com.evil.example/v1'), false);
	assert.equal(diag.isFrontendEndpointAllowed('http://api.minimaxi.com/v1'), false);
});

/* =================== LANG: launcher 必须把 SG.locale 透传给 api.diagnoseInstance ===================
 * 语言链路 Phase 1 RED（2026-09-25）：
 *   - launcher.js 的 openAiDiagnose → runDiag → SG.api.diagnoseInstance(id, opts)
 *     必须把 SG.locale 放进 opts.locale；api.js 再依据该值选择 zh/en。
 *   - launcher 不在本层做白名单/默认值（defense-in-depth 在 api.js + 后端）；只负责透传。
 *   - 与 RED-B1（operationId）同等约束：opts.locale 必须为字符串 'zh' / 'en'，并与 SG.locale 一致。
 *   - 不动生产代码；只追加测试；本节点唯一修改 launcher 的语义是后续 GREEN 阶段实现透传。
 * ======================================================================================= */

test('LANG-5 RED: launcher.runDiag must pass opts.locale=zh when SG.locale=zh (default navigator)', async () => {
	const ctx = makeLoaderContext(); // navigator='zh-CN' → SG.locale 默认为 'zh'
	const launcher = loadRealLauncher(ctx);
	const captured = installResolveDiagnoseStub(ctx);
	await launcher.openAiDiagnose({ id: 'inst-lang-5', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	await waitFor(() => captured.diagnose.length >= 1);
	const call = captured.diagnose[0];
	assert.ok(call && call.opts && typeof call.opts.locale === 'string',
		`opts.locale must be a string equal to SG.locale (currently RED: actual=${JSON.stringify(call && call.opts)})`);
	assert.equal(call.opts.locale, 'zh', `opts.locale must equal SG.locale='zh' (currently RED: actual=${call.opts.locale})`);
});

test('LANG-6 RED: launcher.runDiag must pass opts.locale=en after SG.setLocale("en")', async () => {
	const ctx = makeLoaderContext(); // navigator='zh-CN' → SG.locale 初始化为 'zh'
	const launcher = loadRealLauncher(ctx);
	const captured = installResolveDiagnoseStub(ctx);
	// 用户切换到英文 UI
	ctx.window.SG.setLocale('en');
	assert.equal(ctx.window.SG.locale, 'en', 'precondition: SG.setLocale("en") must set SG.locale=en');
	await launcher.openAiDiagnose({ id: 'inst-lang-6', version: '1.21.1', loader: 'Fabric' });
	const host = ctx.document.getElementById('modalHost');
	host.querySelector('#dConfirmLocal').click();
	await waitFor(() => captured.diagnose.length >= 1);
	const call = captured.diagnose[0];
	assert.equal(call.opts.locale, 'en', `opts.locale must equal SG.locale='en' after setLocale (currently RED: actual=${call.opts.locale})`);
});
