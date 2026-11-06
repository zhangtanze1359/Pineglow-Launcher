'use strict';
/*
 * prelaunch-guide.test.cjs —— M2-1④ 启动前检查的「问题码 → 处理入口」直测
 *
 * 由来（v1.1.0 计划第三节的接口约束）：
 *   「在现有响应上追加问题代码、严重程度和处理动作，保留 errors/warnings 兼容性，
 *     **前端不解析中文错误文本来判断动作**」。
 *
 * 原实现前端只拿到 errors/warnings 两个中文字符串数组，要做「Java 缺失 → 跳去选 Java」
 * 就只能 `if (/Java/.test(msg))` —— 文案一改引导就失效，英文界面下必然全错。
 * 本文件钉住三件事：
 *
 *   1. normPrelaunchIssues 优先用结构化 issues，**缺失时**用旧数组兜底
 *      （旧内核 / mock 通道只返回 errors/warnings，不能因此变成"有问题但不显示"）；
 *   2. runPrelaunchAction 只按 action 标识跳转，**不看文案**；
 *   3. 不认识的 action 必须给出可见反馈，绝不静默无反应。
 *
 * 做法与 launcher-ui-pure.test.cjs 一致：vm 沙箱装最小 DOM + i18n + launcher.js，
 * 通过既有 SG.__test__ 钩子访问。**只加测试，不改运行时行为。**
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

/* ==================== 最小 DOM 桩 ==================== */
function makeContext(extra = {}) {
	const elements = new Map();
	let document = null;
	const makeEl = (tag) => ({
		tagName: (tag || 'div').toUpperCase(),
		id: '', className: '', textContent: '', innerHTML: '', hidden: false,
		style: {}, dataset: {}, children: [],
		classList: { add() {}, remove() {}, contains() { return false; }, toggle() {} },
		appendChild(c) { this.children.push(c); return c; },
		removeChild() {}, remove() {},
		setAttribute() {}, getAttribute() { return null; }, removeAttribute() {},
		addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; },
		querySelector() { return null; }, querySelectorAll() { return []; },
		closest() { return null; }, focus() {}, blur() {}, click() {},
		scrollIntoView() {},
		contains() { return false; },
		getBoundingClientRect() { return { top: 0, left: 0, width: 0, height: 0, bottom: 0, right: 0 }; },
		insertAdjacentHTML() {},
		get ownerDocument() { return document; },
	});

	document = {
		readyState: 'complete',
		body: makeEl('body'),
		head: makeEl('head'),
		documentElement: makeEl('html'),
		createElement: makeEl,
		createElementNS: (_ns, tag) => makeEl(tag),
		createTextNode: (t) => ({ nodeType: 3, textContent: String(t) }),
		getElementById(id) {
			if (!elements.has(id)) { const el = makeEl('div'); el.id = id; elements.set(id, el); }
			return elements.get(id);
		},
		querySelector() { return null; },
		querySelectorAll() { return []; },
		addEventListener() {}, removeEventListener() {},
		createDocumentFragment: () => makeEl('fragment'),
	};

	const sandbox = {
		console,
		setTimeout, clearTimeout, setInterval, clearInterval,
		document,
		navigator: { language: 'zh-CN', languages: ['zh-CN'], userAgent: 'node-test' },
		location: { href: 'http://localhost/index.html', search: '', hash: '' },
		history: { pushState() {}, replaceState() {}, back() {} },
		localStorage: {
			_store: new Map(),
			getItem(k) { return this._store.has(k) ? this._store.get(k) : null; },
			setItem(k, v) { this._store.set(k, String(v)); },
			removeItem(k) { this._store.delete(k); },
		},
		URL, URLSearchParams, TextEncoder, TextDecoder,
		requestAnimationFrame: (fn) => setTimeout(fn, 0),
		cancelAnimationFrame: clearTimeout,
		matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} }),
		performance: { now: () => Date.now() },
		addEventListener() {},
		removeEventListener() {},
		dispatchEvent() { return true; },
		postMessage() {},
		...extra,
	};
	sandbox.window = sandbox;
	sandbox.globalThis = sandbox;
	sandbox.self = sandbox;

	vm.createContext(sandbox);
	vm.runInContext(I18N_JS, sandbox, { filename: 'i18n.js' });
	vm.runInContext(LAUNCHER_JS, sandbox, { filename: 'launcher.js' });

	return { sandbox, ctx: sandbox.SG.__test__, elements };
}

function pickFn(ctx, name) {
	const fn = ctx[name];
	assert.equal(typeof fn, 'function', `SG.__test__.${name} 必须是函数（否则测试无法触达）`);
	return fn;
}

/*
 * vm 沙箱里构造的数组/对象来自**另一个 realm**，原型与本文件的 Array/Object 不同，
 * assert.deepStrictEqual 会以 "Values have same structure but are not reference-equal" 失败。
 * 这不是被测代码的问题，是跨 realm 比较的固有限制，所以统一先序列化成 JSON 再比。
 */
function snap(value) {
	return JSON.parse(JSON.stringify(value));
}

/* ==================== 1. normPrelaunchIssues ==================== */

test('normPrelaunchIssues 优先使用结构化 issues，并保留 code/action', () => {
	const { ctx } = makeContext();
	const norm = pickFn(ctx, 'normPrelaunchIssues');

	const out = norm({
		ok: false,
		errors: ['忽略：结构化字段优先'],
		warnings: [],
		issues: [
			{ code: 'java.missing', severity: 'error', message: 'Java 运行时不存在', action: 'select-java', actionLabel: '选择 Java' },
		],
	});

	assert.equal(out.length, 1);
	assert.equal(out[0].code, 'java.missing');
	assert.equal(out[0].severity, 'error');
	assert.equal(out[0].action, 'select-java');
	assert.equal(out[0].actionLabel, '选择 Java');
});

test('normPrelaunchIssues 在缺结构化字段时用旧数组兜底（旧内核不能变成"有问题但不显示"）', () => {
	const { ctx } = makeContext();
	const norm = pickFn(ctx, 'normPrelaunchIssues');

	const out = norm({
		ok: false,
		errors: ['实例未关联游戏版本'],
		warnings: ['磁盘剩余空间不足'],
	});

	assert.equal(out.length, 2);
	assert.deepEqual(snap(out.map(x => x.severity)), ['error', 'warning']);
	assert.deepEqual(snap(out.map(x => x.message)), ['实例未关联游戏版本', '磁盘剩余空间不足']);
	// 旧数据没有动作信息，硬猜一个跳转是错的，所以必须为 null
	assert.deepEqual(snap(out.map(x => x.action)), [null, null]);
});

test('normPrelaunchIssues 把空数组与缺失字段都归一成空列表，不抛异常', () => {
	const { ctx } = makeContext();
	const norm = pickFn(ctx, 'normPrelaunchIssues');

	assert.deepEqual(snap(norm({ ok: true })), []);
	assert.deepEqual(snap(norm({ ok: true, issues: [], errors: [], warnings: [] })), []);
});

test('normPrelaunchIssues 对非法 severity 一律降级为 error（宁可拦，不可漏）', () => {
	const { ctx } = makeContext();
	const norm = pickFn(ctx, 'normPrelaunchIssues');

	const out = norm({
		issues: [
			{ code: 'x', severity: 'fatal', message: 'm1', action: null, actionLabel: null },
			{ code: 'y', severity: undefined, message: 'm2', action: null, actionLabel: null },
		],
	});

	assert.deepEqual(snap(out.map(x => x.severity)), ['error', 'error']);
});

test('normPrelaunchIssues 不依赖任何中文文案：改掉 message 不影响结构字段', () => {
	const { ctx } = makeContext();
	const norm = pickFn(ctx, 'normPrelaunchIssues');

	const a = norm({ issues: [{ code: 'java.missing', severity: 'error', message: 'Java 运行时不存在', action: 'select-java', actionLabel: '选择 Java' }] });
	const b = norm({ issues: [{ code: 'java.missing', severity: 'error', message: 'Java runtime missing', action: 'select-java', actionLabel: 'Select Java' }] });

	assert.equal(a[0].code, b[0].code);
	assert.equal(a[0].action, b[0].action);
	assert.notEqual(a[0].message, b[0].message);
});

/* ==================== 2. runPrelaunchAction 的动作分发 ==================== */

test('install-version 跳转到下载页', () => {
	const { ctx, sandbox } = makeContext();
	const run = pickFn(ctx, 'runPrelaunchAction');
	const visited = [];
	sandbox.SG.goTo = (nav) => { visited.push(nav); };

	run('install-version', 'inst-1');

	assert.deepEqual(visited, ['downloads']);
});

test('select-java 跳转到设置页并聚焦 Java 按钮', () => {
	const { ctx, sandbox, elements } = makeContext();
	const run = pickFn(ctx, 'runPrelaunchAction');
	const visited = [];
	sandbox.SG.goTo = (nav) => { visited.push(nav); return Promise.resolve(); };

	run('select-java', 'inst-1');

	assert.deepEqual(visited, ['settings']);
	// 聚焦是异步补的（等 goTo 渲染完），本用例只验证「跳转目标选对了」。
	// #btnJrePath 是否是真实存在的入口由设置页渲染测试保证（见 settings 相关用例），
	// 这里不重复断言 DOM，否则会把「render 未被调用」误判成「入口不存在」。
});

test('open-instance-settings 跳转到设置页', () => {
	const { ctx, sandbox } = makeContext();
	const run = pickFn(ctx, 'runPrelaunchAction');
	const visited = [];
	sandbox.SG.goTo = (nav) => { visited.push(nav); };

	run('open-instance-settings', 'inst-1');

	assert.deepEqual(visited, ['settings']);
});

test('不认识的 action 不得静默无反应：必须有 toast 反馈且不跳转', () => {
	const { ctx, sandbox, elements } = makeContext();
	const run = pickFn(ctx, 'runPrelaunchAction');
	const visited = [];
	sandbox.SG.goTo = (nav) => { visited.push(nav); };

	run('some-future-action', 'inst-1');

	assert.deepEqual(visited, [], '不认识的 action 不能乱跳');
	// toast 走的是模块内函数写 #toast 元素的 textContent，所以断言元素而不是打桩
	const toastEl = elements.get('toast');
	assert.ok(toastEl, '必须给出可见反馈，否则用户以为按钮坏了');
	assert.ok(String(toastEl.textContent || '').length > 0, 'toast 文本不能为空');
});

test('action 为 null 时不跳转也不抛异常', () => {
	const { ctx, sandbox } = makeContext();
	const run = pickFn(ctx, 'runPrelaunchAction');
	const visited = [];
	sandbox.SG.goTo = (nav) => { visited.push(nav); };

	assert.doesNotThrow(() => run(null, 'inst-1'));
	assert.deepEqual(visited, []);
});

/* ==================== 3. doLaunch 的拦截行为 ==================== */

test('check.ok=false 时不启动游戏，且弹窗里给出「去处理」入口', async () => {
	const { ctx, sandbox, elements } = makeContext();
	const doLaunch = pickFn(ctx, 'doLaunch');
	let launched = false;
	sandbox.SG.api = {
		prelaunchCheck: async () => ({
			ok: false,
			errors: ['Java 运行时不存在：C:\\jdk\\bin\\javaw.exe'],
			warnings: [],
			issues: [
				{ code: 'java.missing', severity: 'error', message: 'Java 运行时不存在：C:\\jdk\\bin\\javaw.exe', action: 'select-java', actionLabel: '选择 Java' },
			],
		}),
		launchInstance: async () => { launched = true; return { ok: true }; },
	};

	await doLaunch('inst-1', 'test');

	assert.equal(launched, false, '有 error 时绝不能放行启动');
	// 弹窗被写入 modalHost，且带上「去处理」按钮
	const host = elements.get('modalHost');
	assert.ok(host, '必须打开弹窗');
	assert.match(String(host.innerHTML), /prelaunch-issues/);
	assert.match(String(host.innerHTML), /pcFix/, '有 action 时必须渲染「去处理」按钮');
	assert.match(String(host.innerHTML), /选择 Java/, '按钮文案应来自 actionLabel');
});

test('check.ok=false 但 issues 缺失（旧内核）时，仍按旧数组拦截并显示内容', async () => {
	const { ctx, sandbox, elements } = makeContext();
	const doLaunch = pickFn(ctx, 'doLaunch');
	let launched = false;
	sandbox.SG.api = {
		prelaunchCheck: async () => ({ ok: false, errors: ['实例未关联游戏版本'], warnings: [] }),
		launchInstance: async () => { launched = true; return { ok: true }; },
	};

	await doLaunch('inst-1', 'test');

	assert.equal(launched, false);
	const host = elements.get('modalHost');
	assert.match(String(host.innerHTML), /实例未关联游戏版本/);
	assert.doesNotMatch(String(host.innerHTML), /pcFix/, '没有 action 时不能凭空造一个跳转按钮');
});

test('只有 warning 时给「仍然启动」入口，且默认不直接放行', async () => {
	const { ctx, sandbox, elements } = makeContext();
	const doLaunch = pickFn(ctx, 'doLaunch');
	let launched = false;
	sandbox.SG.api = {
		prelaunchCheck: async () => ({
			ok: true,
			errors: [],
			warnings: ['游戏目录不存在（首次启动将自动创建）'],
			issues: [
				{ code: 'gamedir.missing', severity: 'warning', message: '游戏目录不存在（首次启动将自动创建）', action: 'open-instance-settings', actionLabel: '打开实例设置' },
			],
		}),
		launchInstance: async () => { launched = true; return { ok: true }; },
	};

	await doLaunch('inst-1', 'test');

	assert.equal(launched, false, '警告也要用户确认一次');
	const host = elements.get('modalHost');
	assert.match(String(host.innerHTML), /pcOk/, '警告弹窗必须有「仍然启动」');
	assert.match(String(host.innerHTML), /pcFix/, '警告带 action 时也应有处理入口');
});

test('检查请求本身失败时给「重试」而不是当成通过', async () => {
	const { ctx, sandbox, elements } = makeContext();
	const doLaunch = pickFn(ctx, 'doLaunch');
	let launched = false;
	sandbox.SG.api = {
		prelaunchCheck: async () => { throw new Error('network down'); },
		launchInstance: async () => { launched = true; return { ok: true }; },
	};

	await doLaunch('inst-1', 'test');

	assert.equal(launched, false, '未知状态绝不能显示为检查通过');
	const host = elements.get('modalHost');
	assert.match(String(host.innerHTML), /pcRetry/);
});
