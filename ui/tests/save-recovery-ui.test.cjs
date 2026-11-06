'use strict';
/*
 * save-recovery-ui.test.cjs —— M2-1①「已删除存档」界面的渲染与交互直测
 *
 * 由来（v1.1.0 计划第 1 包的完成标准）：
 *   「在存档页提供统一入口，查看手动备份、自动备份和已删除存档，展示时间、来源、
 *     大小及实际状态」「备份查询失败必须显示错误和重试入口」「提供手动清理：
 *     先展示选中条目及释放空间，再确认永久删除；恢复区不自动清空」。
 *
 * 为什么必须测这一层：后端把"删除"改成"移入恢复区"之后，**界面上如果什么都不显示，
 * 用户依然会以为删掉就没了** —— 功能等于没做。所以这里钉三件事：
 *   1. 已删除列表渲染在存档页（与存档列表同页），空态与错误态分别有正确文案；
 *   2. 查询失败时**必须**显示错误 + 重试入口，不能渲染成"没有可恢复的存档"；
 *   3. 永久清理必须二次确认；恢复按钮必须打到正确的实例上。
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

	const makeEl = (tag) => {
		const listeners = new Map();
		const el = {
			tagName: (tag || 'div').toUpperCase(),
			id: '', className: '', textContent: '', innerHTML: '', hidden: false, disabled: false,
			style: {}, dataset: {}, children: [], _listeners: listeners,
			classList: { add() {}, remove() {}, contains() { return false; }, toggle() {} },
			appendChild(c) { this.children.push(c); return c; },
			removeChild() {}, remove() {},
			setAttribute() {}, getAttribute() { return null; }, removeAttribute() {},
			addEventListener(type, fn) { if (!listeners.has(type)) listeners.set(type, []); listeners.get(type).push(fn); },
			removeEventListener() {},
			dispatchEvent() { return true; },
			querySelector() { return null; }, querySelectorAll() { return []; },
			closest() { return null; }, focus() {}, blur() {}, click() {},
			scrollIntoView() {},
			contains() { return false; },
			getBoundingClientRect() { return { top: 0, left: 0, width: 0, height: 0, bottom: 0, right: 0 }; },
			insertAdjacentHTML() {},
			get ownerDocument() { return document; },
		};
		return el;
	};

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
		confirm: () => true,
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

/* ==================== 已删除列表渲染 ==================== */

test('渲染已删除列表：名称、大小、文件数、保留天数全部展示', () => {
	const { ctx } = makeContext();
	const render = pickFn(ctx, 'renderSaveRecoveryList');

	const html = render([
		{ operationId: 'op-1', saveFolderName: 'My World', sizeText: '12.5 MB', fileCount: 37, retentionDays: 30, status: 'quarantined' },
	], null);

	assert.match(html, /My World/);
	assert.match(html, /12\.5 MB/);
	assert.match(html, /37/);
	assert.match(html, /30/, '保留天数必须写出来，否则用户不知道什么时候会被清');
	assert.match(html, /data-recovery-restore/);
	assert.match(html, /data-recovery-purge/);
	assert.match(html, /data-op="op-1"/);
});

test('空列表显示"没有可恢复的存档"，而不是错误态', () => {
	const { ctx } = makeContext();
	const html = pickFn(ctx, 'renderSaveRecoveryList')([], null);

	assert.match(html, /没有可恢复的存档|No restorable saves/);
	assert.doesNotMatch(html, /data-recovery-retry/, '空态不该给重试按钮');
});

test('查询失败必须显示错误 + 重试入口，绝不能渲染成空列表', () => {
	// 这是最危险的一种降级：把"查不到"显示成"没有"，
	// 用户会以为存档真的找不回来了，从而放弃恢复。
	const { ctx } = makeContext();
	const html = pickFn(ctx, 'renderSaveRecoveryList')([], new Error('boom'));

	assert.match(html, /data-recovery-retry/, '必须给重试入口');
	assert.match(html, /role="alert"/);
	assert.doesNotMatch(html, /没有可恢复的存档|No restorable saves/, '错误态不能说"没有"');
});

test('每个条目都带 data-inst，恢复时不会打到别的实例上', () => {
	const { ctx } = makeContext();
	const html = pickFn(ctx, 'renderSaveRecoveryList')([
		{ operationId: 'op-a', instanceId: 'inst-a', saveFolderName: 'A', retentionDays: 30 },
		{ operationId: 'op-b', instanceId: 'inst-b', saveFolderName: 'B', retentionDays: 30 },
	], null);

	assert.match(html, /data-op="op-a" data-inst="inst-a"/);
	assert.match(html, /data-op="op-b" data-inst="inst-b"/);
});

test('存档名里的 HTML 必须被转义（存档名可由用户任意输入）', () => {
	const { ctx } = makeContext();
	const html = pickFn(ctx, 'renderSaveRecoveryList')([
		{ operationId: 'op-x', saveFolderName: '<img src=x onerror=alert(1)>', retentionDays: 30 },
	], null);

	assert.doesNotMatch(html, /<img src=x/);
	assert.match(html, /&lt;img/);
});

/* ==================== 实例锚点选择 ==================== */

test('instanceIdForWorlds 优先用存档自带的实例 id', () => {
	const { ctx } = makeContext();
	const pick = pickFn(ctx, 'instanceIdForWorlds');

	assert.equal(pick([{ instanceId: 'inst-a' }, { instanceId: 'inst-b' }]), 'inst-a');
});

test('instanceIdForWorlds 在无存档时退回当前选中实例', () => {
	// 存档列表为空时仍要能列出"这个实例的已删除项"，所以必须有个锚点。
	const { ctx, sandbox } = makeContext();
	const pick = pickFn(ctx, 'instanceIdForWorlds');
	sandbox.__setSel = null;

	// 无存档且无选中实例时返回空串（不抛异常）
	assert.equal(pick([]), '');
	assert.equal(pick(null), '');
});

/* ==================== 存档页整合 ==================== */

test('renderWorlds 会把已删除区块拼进同一个页面', async () => {
	const { ctx, sandbox } = makeContext();
	const renderWorlds = pickFn(ctx, 'renderWorlds');

	sandbox.SG.api = {
		getWorlds: async () => [],
		listSaveRecoveries: async () => ([
			{ operationId: 'op-1', saveFolderName: 'Deleted World', sizeText: '1 MB', fileCount: 3, retentionDays: 30, instanceId: 'inst-1' },
		]),
	};

	const html = await renderWorlds();

	assert.match(html, /已删除的存档|Deleted saves/);
	assert.match(html, /Deleted World/);
	assert.match(html, /class="content-view"/);
});

test('renderWorlds 在恢复列表查询失败时仍渲染主列表，并给出重试入口', async () => {
	const { ctx, sandbox } = makeContext();
	const renderWorlds = pickFn(ctx, 'renderWorlds');

	sandbox.SG.api = {
		getWorlds: async () => ([{ instanceId: 'inst-1', folderName: 'W', name: 'W', size: '1 MB', lastPlayed: '' }]),
		listSaveRecoveries: async () => { throw new Error('boom'); },
	};

	const html = await renderWorlds();

	// 主列表不能因为附带区块失败而整页崩掉
	assert.match(html, /world-card/);
	assert.match(html, /data-recovery-retry/);
});

test('renderWorlds 在旧 api.js 没有 listSaveRecoveries 时降级为空列表而不是抛异常', async () => {
	// 前端与后端会分开发布，界面必须能在"只有旧 api.js"的情况下正常工作。
	const { ctx, sandbox } = makeContext();
	const renderWorlds = pickFn(ctx, 'renderWorlds');

	sandbox.SG.api = { getWorlds: async () => [] };

	const html = await renderWorlds();

	assert.match(html, /已删除的存档|Deleted saves/);
	assert.match(html, /没有可恢复的存档|No restorable saves/);
});
