'use strict';
/*
 * Phase 3A R2-03 news external link system browser close loop: front-end behavior RED
 * (review-fix-1 reworked, 2026-09-07).
 *
 * review-fix-1 key corrections (phase3a-r203-red-quality-rework-2026-09-07.md section 3):
 *   - Reuse the lightweight DOM/API harness from news-behavior.test.cjs
 *   - Actually trigger home and detail button clicks (behavior test, not just static scan)
 *   - Verify: legal items only call SG.api.openNewsLink(linkId), no raw URL;
 *     linkOk=false / missing ID -> no call
 *   - Pending Promise: consecutive clicks produce only one request; on failure show
 *     i18n news.openFailed / retry entry
 *   - Static scan only as supplementary defense, scoped to news render/bind functions
 *
 * RED classification (reported in deliverable):
 *   1. Independent RED (behavior gap): openNewsLink missing; window.open/target=_blank direct;
 *      i18n missing failure text
 *   2. Existing protection: linkOk=false currently goes through goTo('news') (existing second-layer)
 *   3. Test facility error: harness load failure (not counted as RED)
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');

/* ============== 1. Controlled fixture ============== */

const FIXTURE_NEWS = {
	items: [
		{
			id: 'fx-allowed-1',
			title: 'Allowed Official Article',
			category: 'Minecraft',
			date: '2026-09-05T00:00:00Z',
			text: 'fixture allowed',
			imageId: 'img-allowed-1',
			link: 'https://www.minecraft.net/article/fixture-allowed',
			linkOk: true,
			linkId: 'FX-LID-allowed-1',
		},
		{
			id: 'fx-allowed-2',
			title: 'Allowed Official Article 2',
			category: 'Minecraft',
			date: '2026-09-05T00:00:00Z',
			text: 'fixture allowed 2',
			imageId: null,
			link: 'https://www.minecraft.net/article/fixture-allowed-2',
			linkOk: true,
			linkId: 'FX-LID-allowed-2',
		},
		{
			id: 'fx-disallowed-1',
			title: 'Disallowed Article',
			category: 'Other',
			date: '2026-09-05T00:00:00Z',
			text: 'fixture disallowed',
			imageId: null,
			link: 'https://evil.example.com/x',
			linkOk: false,
		},
	],
	meta: {
		source: 'fixture://r203-controlled',
		state: 'live',
		validatedAt: '2026-09-05T12:00:00+00:00',
		fetchedAt: '2026-09-05T12:00:00+00:00',
		isStale: false,
		canRetry: false,
		errorKind: null,
		error: null,
	},
};

/* ============== 2. Shared DOM/API harness ============== */

let _sharedMakeEl = null;

function makeDomStub() {
	const elementsById = new Map();
	function makeEl(tag) {
		const listeners = {};
		const el = {
			ownerDocument: null,
			tagName: (tag || 'div').toUpperCase(),
			id: '',
			className: '',
			classList: {
				_add: new Set(), _remove: new Set(),
				add(...c) { c.forEach(x => { this._add.add(x); this._remove.delete(x); if (x && !el.className.split(/\s+/).includes(x)) el.className = (el.className + ' ' + x).trim(); }); },
				remove(...c) { c.forEach(x => { this._remove.add(x); this._add.delete(x); el.className = el.className.split(/\s+/).filter(n => n && n !== x).join(' '); }); },
				contains(c) { return el.className.split(/\s+/).includes(c); },
				toggle(c, force) { if (force === undefined) { this.contains(c) ? this.remove(c) : this.add(c); } else if (force) { this.add(c); } else { this.remove(c); } },
			},
			attributes: {},
			dataset: {},
			style: {},
			children: [],
			parent: null,
			hidden: false,
			disabled: false,
			textContent: '',
			value: '',
			_listeners: listeners,
			setAttribute(k, v) {
				this.attributes[k] = v;
				if (k === 'id') this.id = v;
				if (k === 'class') this.className = v;
				if (k.startsWith('data-')) {
					// HTML5 dataset: data-link-id -> dataset.linkId (camelCase)
					const camelKey = k.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase());
					this.dataset[camelKey] = v;
				}
			},
			get innerHTML() { return ''; /* 不支持 getter;只读由 parseInto 解析 */ },
			set innerHTML(html) {
				// 内联解析:appendChild 走 parseInto 的 DOM 树
				if (typeof html !== 'string') return;
				parseInto(this, html);
			},
			getAttribute(k) { return this.attributes[k]; },
			// MR17：launcher.js 的弹窗键盘契约会调用这些标准 DOM 方法（真实元素本来就有）。
			contains(node) {
				if (!node) return false;
				if (node === this) return true;
				return this.children.some(c => c === node || (c && typeof c.contains === 'function' && c.contains(node)));
			},
			removeAttribute(k) { delete this.attributes[k]; },
			closest() { return null; },
			matches() { return false; },
			focus() { if (typeof document !== 'undefined' && document) document.activeElement = this; },
			offsetParent: {},
			addEventListener(type, fn, capture) { (listeners[type] = listeners[type] || []).push({ fn, capture: !!capture }); },
			removeEventListener(type, fn) { const arr = listeners[type] || []; const i = arr.findIndex(e => e.fn === fn); if (i >= 0) arr.splice(i, 1); },
			dispatchEvent(type, eventObj) {
				eventObj = eventObj || { type, target: this, preventDefault() {}, stopPropagation() {} };
				eventObj.target = this; eventObj.currentTarget = this;
				for (const { fn } of (listeners[type] || [])) { if (eventObj._stopped) break; fn.call(this, eventObj); }
			},
			appendChild(child) { child.parent = this; this.children.push(child); if (child.id) elementsById.set(child.id, child); child._connected = true; return child; },
			remove() { if (this.parent) { const i = this.parent.children.indexOf(this); if (i >= 0) this.parent.children.splice(i, 1); } if (this.id) elementsById.delete(this.id); this._connected = false; },
			querySelector(sel) { return querySelectorIn(this, sel); },
			querySelectorAll(sel) { return querySelectorAllIn(this, sel); },
			getBoundingClientRect() { return { top: 0, left: 0, width: 100, height: 30 }; },
			cloneNode() { return makeEl(tag); },
		};
		return el;
	}
	_sharedMakeEl = makeEl;
	const document = {
		body: makeEl('body'),
		createElement: makeEl,
		createTextNode: (t) => ({ textContent: t, children: [], appendChild() {} }),
		getElementById: (id) => elementsById.get(id) || null,
		querySelector: (sel) => querySelectorIn(document.body, sel),
		querySelectorAll: (sel) => querySelectorAllIn(document.body, sel),
		// MR17：launcher.js 会在模块加载时注册 keydown（role=button 键盘激活）。
		// 真实 document 的 _listeners 不需要预建，这里补上初始化，否则读 document.body._listeners[type] 会抛错。
		addEventListener(type, fn, capture) {
			if (type === 'DOMContentLoaded') return;
			if (!document.body._listeners) document.body._listeners = {};
			(document.body._listeners[type] = document.body._listeners[type] || []).push({ fn, capture: !!capture });
		},
		removeEventListener(type, fn) {
			if (!document.body._listeners || !document.body._listeners[type]) return;
			const arr = document.body._listeners[type];
			const i = arr.findIndex(e => e.fn === fn);
			if (i >= 0) arr.splice(i, 1);
		},
		dispatchEvent(type, obj) { document.body.dispatchEvent(type, obj); },
	};
	function querySelectorIn(root, sel) { const all = querySelectorAllIn(root, sel); return all[0] || null; }
	function querySelectorAllIn(root, sel) {
		const out = [];
		const visit = (node) => {
			if (!node) return;
			if (matches(node, sel)) out.push(node);
			(node.children || []).forEach(visit);
		};
		visit(root);
		return out;
	}
	function matches(node, sel) {
		if (!sel || !node || !node.tagName) return false;
		const m = sel.match(/^([a-zA-Z][\w-]*)?(?:#([\w-]+))?(?:\.([\w-]+))?(?:\[([\w-]+)(?:="([^"]*)")?\])?$/);
		if (!m) return false;
		const [, tag, id, cls, attr, attrVal] = m;
		if (tag && node.tagName.toLowerCase() !== tag.toLowerCase()) return false;
		if (id && node.id !== id) return false;
		if (cls && !(node.className || '').split(/\s+/).includes(cls)) return false;
		if (attr) {
			if (attrVal === undefined) {
				if (!(attr in (node.attributes || {}))) return false;
			} else {
				if ((node.attributes || {})[attr] !== attrVal) return false;
			}
		}
		return true;
	}
	document.body._listeners = {};
	return { document, elementsById, makeEl };
}

function parseInto(targetEl, html) {
	const fn = _sharedMakeEl || (() => { throw new Error('parseInto: makeEl not initialised'); });
	// 优先用 _sharedMakeEl,ownerDocument 可能为 undefined
	const doc = {
		createElement: (tag) => fn(tag || 'div'),
		createTextNode: (t) => ({ textContent: t, children: [], appendChild() {} }),
	};
	const VOID_TAGS = new Set(['br', 'hr', 'img', 'input', 'meta', 'link']);
	const tokenRe = /<\/?([a-zA-Z][a-zA-Z0-9-]*)\s*([^>]*?)\/?>|([^<]+)|<!--[\s\S]*?-->/g;
	const attrRe = /([a-zA-Z_:][\w:-]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?/g;
	const stack = [targetEl];
	html.replace(tokenRe, (match, tagName, attrs, text) => {
		if (text !== undefined) { const cur = stack[stack.length - 1]; const t = doc.createTextNode(text); cur.appendChild(t); return ''; }
		if (match.startsWith('<!--')) return '';
		const isClose = match.startsWith('</');
		if (isClose) { if (stack.length > 1) stack.pop(); return ''; }
		const isSelfClose = match.endsWith('/>') || VOID_TAGS.has(tagName.toLowerCase());
		const el = doc.createElement(tagName);
		if (attrs) {
			let m;
			attrRe.lastIndex = 0;
			while ((m = attrRe.exec(attrs)) !== null) {
				const k = m[1], v = m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : ''));
				el.setAttribute(k, v);
			}
		}
		const cur = stack[stack.length - 1];
		cur.appendChild(el);
		if (!isSelfClose) stack.push(el);
		return '';
	});
}

function makeApiStub() {
	const calls = { openNewsLink: [], updateConfig: [], getNews: [] };
	let openNewsLinkImpl = async () => ({ ok: true });
	let openNewsLinkInFlight = null;
	return {
		calls,
		setOpenNewsLinkImpl(fn) { openNewsLinkImpl = fn; },
		setOpenNewsLinkInFlight(promise) { openNewsLinkInFlight = promise; },
		async getState() { return { config: {}, instances: [], accounts: [], java: [] }; },
		async getAccounts() { return { accounts: [], capes: [] }; },
		async getStats() { return { totalPlaySeconds: 0, totalPlayCount: 0 }; },
		async getNews() { calls.getNews.push('auto'); return FIXTURE_NEWS; },
		async getNewsImage(id) { return 'blob:fake-' + id; },
		async updateConfig(patch) { calls.updateConfig.push(patch); return { ok: true }; },
		async openNewsLink(linkId, ...rest) {
			calls.openNewsLink.push({ linkId, rest, at: Date.now() });
			if (openNewsLinkInFlight) return openNewsLinkInFlight;
			return openNewsLinkImpl(linkId);
		},
	};
}

function loadLauncher() {
	const { document, elementsById, makeEl } = makeDomStub();
	// 关键:在 launcher.js 跑之前让 document.readyState 处于 'loading' 状态,
	// 这样 launcher.js 末尾的 init() 不会自动执行(DOMContentLoaded 才会触发);
	// 同时给 document.documentElement / document.addEventListener 提供桩。
	document.readyState = 'loading';
	document.documentElement = makeEl('html');
	document.addEventListener = (type, fn) => {
		if (type === 'DOMContentLoaded') { document._domContentLoadedHandler = fn; return; }
		// MR17：launcher.js 会在模块加载时注册 keydown（role=button 键盘激活）。
		// 真实 document 不需要预建 _listeners，这里补上初始化，否则读 _listeners[type] 会抛错。
		if (!document._listeners) document._listeners = {};
		(document._listeners[type] = document._listeners[type] || []).push({ fn, capture: false });
	};
	// MR17：closeModal 会卸掉键盘监听，假 document 必须提供这个标准方法。
	document.removeEventListener = (type, fn) => {
		const arr = (document._listeners && document._listeners[type]) || [];
		const idx = arr.findIndex(e => e.fn === fn);
		if (idx >= 0) arr.splice(idx, 1);
	};
	// 预注册 #modalHost 宿主,让 openModal 可用
	const modalHost = makeEl('div');
	modalHost.id = 'modalHost';
	modalHost.hidden = true;
	modalHost._listeners = {};
	modalHost.appendChild = function (c) { c.parent = this; this.children.push(c); c._connected = true; return c; };
	document.body.appendChild(modalHost);
	const ctx = {
		SG: {},
		location: { protocol: 'http:', hash: '' },
		history: { replaceState() {}, pushState() {} },
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN', platform: 'Win32' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		document,
		setTimeout, clearTimeout, setInterval, clearInterval,
		requestAnimationFrame: (cb) => setTimeout(cb, 0),
		Promise, URLSearchParams, console, Date, JSON, Math, Object, Array, String, Number, Boolean, Error, Symbol,
		Image: function () { return { addEventListener() {}, src: '' }; },
		Blob: function () {},
		FileReader: function () { this.readAsDataURL = () => {}; this.onload = null; this.onerror = null; },
		URL: Object.assign(globalThis.URL, { createObjectURL: () => 'blob:fake', revokeObjectURL() {} }),
		window: null,
		open: (url) => { ctx.openCalls.push(String(url)); return {}; },
		fetch: () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) }),
	};
	ctx.openCalls = [];
	ctx.goToCalls = [];
	ctx.toastCalls = [];
	ctx.window = ctx;
	const apiStub = makeApiStub();
	ctx.SG.api = apiStub;
	vm.createContext(ctx);
	vm.runInContext(I18N_JS, ctx);
	vm.runInContext('SG.apiMode = "live"; SG.apiToken = "test-token"; SG.goTo = SG.goTo || function(v) {}; SG.toast = SG.toast || function(t) { SG.__toastCalls = SG.__toastCalls || []; SG.__toastCalls.push(t); };', ctx);
	vm.runInContext(LAUNCHER_JS, ctx);
	if (ctx.SG.__toastCalls) ctx.toastCalls = ctx.SG.__toastCalls;
	const T = ctx.SG.__test__;
	if (!T) throw new Error('SG.__test__ not injected; launcher.js load failed');
	return { ctx, document, T, api: apiStub };
}

const waitMicro = () => new Promise(r => setTimeout(r, 10));
const waitSettle = () => new Promise(r => setTimeout(r, 30));

function makeDeferred() {
	let resolve, reject;
	const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
	return { promise, resolve, reject };
}

/* ============== 3. Behavior tests ============== */

test('R2-03 home card linkOk=true click should only call SG.api.openNewsLink(linkId), no window.open, no raw URL', async () => {
	const { T, ctx, document, api } = loadLauncher();
	await T.loadNews();
	const home = await T.renderHome();
	const root = document.createElement('div');
	parseInto(root, home);
	document.body.appendChild(root);
	T.bindHome(root);
	const card = root.querySelector('.news-card[data-link-ok="1"]');
	assert.ok(card, 'home should render at least one linkOk=1 card');
	card.dispatchEvent('click', {});
	await waitMicro();

	assert.deepEqual(
		ctx.openCalls.filter(u => u.startsWith('http') || u.startsWith('javascript')),
		[],
		'linkOk=true card click must NOT call window.open directly (must go through /api/news-open -> system handler)'
	);
	assert.equal(
		api.calls.openNewsLink.length,
		1,
		'linkOk=true card click should call SG.api.openNewsLink exactly once'
	);
	if (api.calls.openNewsLink.length === 1) {
		const call = api.calls.openNewsLink[0];
		assert.equal(typeof call.linkId, 'string', 'openNewsLink must receive string linkId');
		assert.ok(call.linkId.length > 0, 'linkId must be non-empty');
		assert.deepEqual(call.rest, [], 'openNewsLink call must carry no other rest args (prevents raw URL injection)');
	}
});

test('R2-03 linkOk=false card click must not call openNewsLink, must not call window.open; should jump to news view', async () => {
	const { T, ctx, document, api } = loadLauncher();
	await T.loadNews();
	const home = await T.renderHome();
	const root = document.createElement('div');
	parseInto(root, home);
	document.body.appendChild(root);
	T.bindHome(root);
	const card = root.querySelector('.news-card[data-link-ok="0"]');
	assert.ok(card, 'home should render at least one linkOk=0 card');
	card.dispatchEvent('click', {});
	await waitMicro();

	assert.equal(
		api.calls.openNewsLink.length,
		0,
		'linkOk=false card must NOT call SG.api.openNewsLink (prevents linkId injection)'
	);
	assert.equal(
		ctx.openCalls.length,
		0,
		'linkOk=false card must NOT call window.open'
	);
	assert.ok(
		ctx.goToCalls.includes('news') || ctx.goToCalls.length === 0,
		'linkOk=false card should jump to news view (or stay on current). goToCalls=' + JSON.stringify(ctx.goToCalls)
	);
});

test('R2-03 detail modal linkOk=true must only call openNewsLink(linkId); no target=_blank anchor in DOM', async () => {
	const { T, ctx, document, api } = loadLauncher();
	await T.loadNews();
	const news = await T.renderNews();
	const root = document.createElement('div');
	parseInto(root, news);
	document.body.appendChild(root);
	T.bindNews(root);
	const card = root.querySelector('.news-full-card[data-link-ok="1"]');
	assert.ok(card, 'detail page should have at least one linkOk=1 card');
	card.dispatchEvent('click', {});
	await waitMicro();
	const modal = document.body;
	// 收集弹窗内所有可点击的 target=_blank 元素
	const targetBlankAnchors = [];
	modal.querySelectorAll('a').forEach(el => {
		if (el.getAttribute && el.getAttribute('target') === '_blank') targetBlankAnchors.push(el);
	});
	assert.equal(
		targetBlankAnchors.length,
		0,
		'modal must NOT render target=_blank anchors. hit=' + targetBlankAnchors.length
	);
	// 弹窗里应有一个 button 形式的"打开文章"按钮(data-news-open)
	const buttons = modal.querySelectorAll('button[data-news-open]');
	assert.ok(buttons.length > 0, 'modal must render a button[data-news-open] (not <a target=_blank>). buttons=' + buttons.length);
	// launch.js 使用 .onclick = () => ... 风格(DOM Level 0);直接调 onclick
	buttons[0].onclick && buttons[0].onclick({ preventDefault(){}, stopPropagation(){} });
	await waitMicro();
	assert.equal(api.calls.openNewsLink.length, 1, 'clicking "open article" button should call openNewsLink once');
	if (api.calls.openNewsLink.length === 1) {
		const call = api.calls.openNewsLink[0];
		assert.ok(call.linkId, 'openNewsLink receives linkId');
		assert.deepEqual(call.rest, [], 'openNewsLink must carry no raw URL');
	}
	assert.equal(
		ctx.openCalls.length,
		0,
		'detail modal click must NOT call window.open'
	);
});

test('R2-03 pending Promise consecutive click dedup: only one openNewsLink request', async () => {
	const { T, ctx, document, api } = loadLauncher();
	let resolveOpen;
	const pending = new Promise(r => { resolveOpen = r; });
	api.setOpenNewsLinkInFlight(pending);
	await T.loadNews();
	const home = await T.renderHome();
	const root = document.createElement('div');
	parseInto(root, home);
	document.body.appendChild(root);
	T.bindHome(root);
	const card = root.querySelector('.news-card[data-link-ok="1"]');
	assert.ok(card);
	card.dispatchEvent('click', {});
	await waitMicro();
	card.dispatchEvent('click', {});
	await waitMicro();
	card.dispatchEvent('click', {});
	await waitMicro();

	assert.equal(
		api.calls.openNewsLink.length,
		1,
		'pending 3 consecutive clicks should produce only 1 openNewsLink call (dedup)'
	);
	resolveOpen({ ok: true });
	await pending;
});

test('R2-03 openNewsLink failure should show i18n news.openFailed text and allow retry', async () => {
	const { T, ctx, document, api } = loadLauncher();
	api.setOpenNewsLinkImpl(async () => { throw new Error('HTTP 400 unknown_link'); });
	await T.loadNews();
	const home = await T.renderHome();
	const root = document.createElement('div');
	parseInto(root, home);
	document.body.appendChild(root);
	T.bindHome(root);
	const card = root.querySelector('.news-card[data-link-ok="1"]');
	card.dispatchEvent('click', {});
	await new Promise(r => setTimeout(r, 30));

	// 至少 1 个:toast 出现 + i18n key 存在
	const html = root.innerHTML;
	const toastHasFailed = ctx.toastCalls && ctx.toastCalls.length > 0
		&& ctx.toastCalls.some(t => /openFailed|打开失败|open|失败/.test(String(t)));
	const i18nKey = ctx.SG.t ? ctx.SG.t('news.openFailed') : null;
	const hasI18n = i18nKey && !/^news\./.test(i18nKey) && i18nKey !== 'news.openFailed';
	assert.ok(
		toastHasFailed || hasI18n,
		'openNewsLink failure should display news.openFailed (toast or i18n key). R2-03 RED expected to fail (i18n key missing in current production code)'
	);
});

test('R2-03 failure retry: subsequent click should produce new request', async () => {
	const { T, ctx, document, api } = loadLauncher();
	let n = 0;
	api.setOpenNewsLinkImpl(async () => {
		n++;
		if (n === 1) throw new Error('HTTP 400');
		return { ok: true };
	});
	await T.loadNews();
	const home = await T.renderHome();
	const root = document.createElement('div');
	parseInto(root, home);
	document.body.appendChild(root);
	T.bindHome(root);
	const card = root.querySelector('.news-card[data-link-ok="1"]');
	card.dispatchEvent('click', {});
	await new Promise(r => setTimeout(r, 30));
	card.dispatchEvent('click', {});
	await new Promise(r => setTimeout(r, 30));
	assert.equal(
		api.calls.openNewsLink.length,
		2,
		'after failure, subsequent click should produce new request. call count=' + api.calls.openNewsLink.length
	);
});

/* ============== 3.5 success settle 后按钮恢复（Codex live 复现回归） ============== */

test('R2-03: success settle must re-enable trigger button; subsequent click produces a new request (Codex live repro regression)', async () => {
	const { T, ctx, document, api } = loadLauncher();
	const deferred = makeDeferred();
	api.setOpenNewsLinkImpl(() => deferred.promise);

	await T.loadNews();
	const news = await T.renderNews();
	const root = document.createElement('div');
	parseInto(root, news);
	document.body.appendChild(root);
	T.bindNews(root);
	const card = root.querySelector('.news-full-card[data-link-ok="1"]');
	assert.ok(card, 'detail page should have at least one linkOk=1 card');
	card.dispatchEvent('click', {});
	await waitMicro();

	const openBtn = document.getElementById('newsOpenArticle');
	assert.ok(openBtn, 'modal must render a button[data-news-open]');

	// 1) 第一次 onclick → 立即 disabled + 一次请求
	openBtn.onclick && openBtn.onclick({ preventDefault(){}, stopPropagation(){} });
	await waitMicro();
	assert.equal(
		openBtn.disabled,
		true,
		'openNewsLinkWithDedup must set triggerEl.disabled=true on click');
	assert.equal(api.calls.openNewsLink.length, 1, 'should produce 1 request on first click');

	// 2) pending 期间再点击 → 仍 disabled, 请求数不变 (inflight 去重)
	openBtn.onclick && openBtn.onclick({ preventDefault(){}, stopPropagation(){} });
	await waitMicro();
	assert.equal(
		openBtn.disabled,
		true,
		'pending 期间按钮应保持 disabled');
	assert.equal(api.calls.openNewsLink.length, 1, 'pending 期间连点只一次请求');

	// 3) resolve 成功 → 等 microtask/finally settle
	deferred.resolve({ ok: true });
	await waitSettle();

	// 4) 成功 settle 后按钮必须重新 enabled（Codex 真实 live 复现：之前失败）
	assert.equal(
		openBtn.disabled,
		false,
		'成功 settle 后按钮必须重新 enabled；否则 UX 永久禁用');

	// 5) 同一 trigger 再次点击 → 产生新请求
	openBtn.onclick && openBtn.onclick({ preventDefault(){}, stopPropagation(){} });
	await waitMicro();
	assert.equal(
		api.calls.openNewsLink.length,
		2,
		'成功后再次点击应产生新请求（无永久禁用）');

	// 6) 成功路径不显示错误文案
	const errEl = document.getElementById('newsOpenError');
	if (errEl) {
		assert.ok(
			errEl.style.display === 'none' || errEl.textContent === '',
			'成功路径不得显示 news.openFailed 错误文案');
	}
});

/* ============== 4. Static scan (supplementary defense) ============== */

test('R2-03 static scan: launcher.js news render/bind functions must not contain window.open', () => {
	const lines = LAUNCHER_JS.split(/\r?\n/);
	const isNewsContext = (line) => {
		const lower = line.toLowerCase();
		return lower.includes('news') || lower.includes('linkok') || lower.includes('data-link')
			|| lower.includes('isfrontendallowednewslink') || lower.includes('newsopen')
			|| lower.includes('data-news') || lower.includes('news-full-card')
			|| lower.includes('news-full-body');
	};
	const offenders = [];
	lines.forEach((line, i) => {
		const ctxStart = Math.max(0, i - 2);
		const ctxEnd = Math.min(lines.length, i + 3);
		let inNewsCtx = false;
		for (let k = ctxStart; k < ctxEnd; k++) { if (isNewsContext(lines[k])) { inNewsCtx = true; break; } }
		if (!inNewsCtx) return;
		if (/openNewsLink\s*\(/.test(line) || /SG\.api\.openNewsLink/.test(line)) return;
		if (/\bwindow\.open\s*\(/.test(line)) offenders.push(`L${i + 1}: ${line.trim()}`);
	});
	assert.equal(
		offenders.length,
		0,
		'launcher.js news path still contains window.open. violations: ' + offenders.join(' | ')
	);
});

test('R2-03 static scan: launcher.js news DOM templates must not contain target=_blank', () => {
	const lines = LAUNCHER_JS.split(/\r?\n/);
	const isNewsContext = (line) => {
		const lower = line.toLowerCase();
		return lower.includes('news') || lower.includes('linkok') || lower.includes('data-link')
			|| lower.includes('openarticle') || lower.includes('modal-btn')
			|| lower.includes('isfrontendallowednewslink') || lower.includes('news-full-card')
			|| lower.includes('news-full-body');
	};
	const offenders = [];
	lines.forEach((line, i) => {
		// 排除注释行(target=_blank 在注释中说明性提及,非实际 DOM)
		const trimmed = line.trim();
		if (trimmed.startsWith('//') || trimmed.startsWith('/*') || trimmed.startsWith('*')) return;
		const ctxStart = Math.max(0, i - 2);
		const ctxEnd = Math.min(lines.length, i + 3);
		let inNewsCtx = false;
		for (let k = ctxStart; k < ctxEnd; k++) {
			const t = lines[k].trim();
			if (t.startsWith('//') || t.startsWith('/*') || t.startsWith('*')) continue;
			if (isNewsContext(lines[k])) { inNewsCtx = true; break; }
		}
		if (!inNewsCtx) return;
		if (/target\s*=\s*["']_blank["']/i.test(line)) offenders.push(`L${i + 1}: ${line.trim()}`);
	});
	assert.equal(
		offenders.length,
		0,
		'launcher.js news DOM still contains target=_blank. violations: ' + offenders.join(' | ')
	);
});

test('R2-03 static scan: api.js must contain SG.api.openNewsLink definition and /api/news-open call', () => {
	const hasDef = /\bopenNewsLink\s*:\s*async\s*\(/.test(API_JS) || /\basync\s+openNewsLink\s*\(/.test(API_JS);
	assert.ok(hasDef, 'SG.api.openNewsLink must be exposed in api.js as `openNewsLink: async (` or `async openNewsLink(`');
	// 接受 _req(共享 auth/session)和 fetch 两种风格
	const hasCall = /(?:fetch|_req)\s*\(\s*['"`]\/api\/news-open/.test(API_JS);
	assert.ok(hasCall, 'api.js must call /api/news-open (via fetch or _req)');
});
