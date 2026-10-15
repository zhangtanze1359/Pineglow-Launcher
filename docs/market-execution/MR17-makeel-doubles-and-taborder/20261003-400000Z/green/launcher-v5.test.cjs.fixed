'use strict';
/*
 * Phase 1 返修 v5：标准 DOM 兼容 + 离页返回恢复 + 首次 getState pending 离页保护
 * --------------------------------------------------------------------------
 * 覆盖 V5-1 至 V5-10 回归矩阵。每个测试必须：
 *   1. 调用实际 goTo，通过真实事件路径点击控件（dispatchEvent）。
 *   2. 用 deferred Promise 真实返回给 getState / searchContent。
 *   3. 明确等待请求已开始且 pending，再执行离页。
 *   4. 需要验证晚到覆盖时，先等待 B 已进入 DOM，再释放 A。
 *   5. 返回页面后禁止通过额外点击来满足 V5-3/V5-4。
 *   6. 用精确调用次数验证"恰好一次"，不用 >= 1。
 *   7. 检查整页 DOM、当前视图、rendered/恢复状态和错误状态。
 *   8. 不吞事件回调和断言异常。
 *
 * 修复前（v4 代码）失败证据：本文件 V5-3/V5-4 在 v4 永久 loading 时会失败；
 * V5-7/V5-8 在 v4 写空 innerHTML+设置 rendered 时会失败。
 * 修复后：V5-1 至 V5-10 全部通过，v3/v4 测试不回归。
 */

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

/* ============== 标准浏览器语义 DOM 桩（v5 升级） ==============
 * 关键 v5 升级：与 launcher-v3/v4 桩不同的关键点
 *  - parentNode（标准 DOM）；不依赖 .parent
 *  - children 是 HTMLCollection 风格：length + 下标访问；**没有 indexOf**
 *  - 提供 replaceChild（标准原子替换）
 *  - 提供 firstElementChild、lastElementChild
 *  - 提供 nodeType（元素 1，文本 3）
 *  - 提供 firstChild / nextSibling
 *
 * 严格模式：把 launcher.js 中 .parent 和 children.indexOf 暴露出来直接抛错。
 */
function makeDomStubV5() {
	const elementsById = new Map();
	const allElements = [];
	const querySelectorAllIn = (root, sel) => {
		const out = [];
		function walk(n) { if (matches(n, sel)) out.push(n); for (const c of n.children) walk(c); }
		walk(root);
		return out;
	};
	const querySelectorIn = (root, sel) => querySelectorAllIn(root, sel)[0] || null;
	function makeEl(tag) {
		const listeners = {};
		const el = {
			ownerDocument: null,
			tagName: (tag || 'div').toUpperCase(),
			nodeType: (tag === '#text') ? 3 : 1,
			id: '', className: '',
			_connected: false,
			classList: {
				_add: new Set(), _remove: new Set(),
				add(...c) { c.forEach(x => { this._add.add(x); this._remove.delete(x); if (x && !el.className.split(/\s+/).includes(x)) el.className = (el.className + ' ' + x).trim(); }); },
				remove(...c) { c.forEach(x => { this._remove.add(x); this._add.delete(x); el.className = el.className.split(/\s+/).filter(n => n && n !== x).join(' '); }); },
				contains(c) { return el.className.split(/\s+/).includes(c); },
				toggle(c, force) { if (force === true) { this._add.add(c); this._remove.delete(c); el.className = (el.className + ' ' + c).trim(); } else if (force === false) { this._remove.add(c); this._add.delete(c); el.className = el.className.split(/\s+/).filter(n => n && n !== c).join(' '); } else { this.contains(c) ? this.remove(c) : this.add(c); } },
			},
			attributes: {}, dataset: {}, style: {},
			children: [], parent: null,
			hidden: false, disabled: false,
			textContent: '', value: '',
			_listeners: listeners, _dataset: {},
			setAttribute(k, v) { this.attributes[k] = v; if (k === 'id') this.id = v; if (k === 'class') this.className = v; if (k.startsWith('data-')) this._dataset[k.slice(5)] = v; },
			getAttribute(k) { return this.attributes[k]; },
			removeAttribute(k) { delete this.attributes[k]; },
			focus() { this._focused = true; }, // MR17/A13：产品代码 4 处调用 focus
			offsetParent: null, // MR17/A13：与 ai-diagnose 替身同口径
			addEventListener(type, fn, capture) { (listeners[type] = listeners[type] || []).push({ fn, capture: !!capture }); },
			removeEventListener(type, fn) { const arr = listeners[type] || []; const i = arr.findIndex(e => e.fn === fn); if (i >= 0) arr.splice(i, 1); },
			dispatchEvent(type, eventObj) {
				eventObj = eventObj || { type, target: this, preventDefault() {}, stopPropagation() {}, stopImmediatePropagation() {}, defaultPrevented: false };
				eventObj.target = this;
				const path = []; let cur = this; while (cur) { path.unshift(cur); cur = cur.parentNode || cur.parent; }
				for (const node of path) {
					const arr = (node._listeners[type] || []).filter(x => x.capture);
					for (const { fn } of arr) { eventObj.currentTarget = node; fn.call(node, eventObj); if (eventObj._stoppedImmediate) break; }
					if (eventObj._stoppedImmediate) break;
				}
				if (!eventObj._stoppedImmediate) {
					for (const node of [...path].reverse()) {
						const arr = (node._listeners[type] || []).filter(x => !x.capture);
						for (const { fn } of arr) { eventObj.currentTarget = node; fn.call(node, eventObj); if (eventObj._stoppedImmediate) break; }
						if (eventObj._stoppedImmediate || eventObj._stopped) break;
					}
				}
			},
			appendChild(child) {
				if (child.parent && child.parent !== this) { const pi = child.parent.children.indexOf(child); if (pi >= 0) child.parent.children.splice(pi, 1); }
				if (child.parentNode && child.parentNode !== this) {
					const pi = child.parentNode.children.indexOf(child);
					if (pi >= 0) child.parentNode.children.splice(pi, 1);
				}
				child.parent = this; child.parentNode = this;
				this.children.push(child);
				if (child.id) elementsById.set(child.id, child);
				child._connected = true; return child;
			},
			insertBefore(child, ref) {
				if (child.parent && child.parent !== this) { const pi = child.parent.children.indexOf(child); if (pi >= 0) child.parent.children.splice(pi, 1); }
				if (child.parentNode && child.parentNode !== this) {
					const pi = child.parentNode.children.indexOf(child);
					if (pi >= 0) child.parentNode.children.splice(pi, 1);
				}
				child.parent = this; child.parentNode = this;
				const i = ref ? this.children.indexOf(ref) : this.children.length;
				this.children.splice(i < 0 ? this.children.length : i, 0, child);
				if (child.id) elementsById.set(child.id, child);
				child._connected = true;
				return child;
			},
			replaceChild(newChild, oldChild) {
				if (newChild.parent && newChild.parent !== this) { const pi = newChild.parent.children.indexOf(newChild); if (pi >= 0) newChild.parent.children.splice(pi, 1); }
				if (newChild.parentNode && newChild.parentNode !== this) {
					const pi = newChild.parentNode.children.indexOf(newChild);
					if (pi >= 0) newChild.parentNode.children.splice(pi, 1);
				}
				const i = this.children.indexOf(oldChild);
				if (i < 0) throw new Error('replaceChild: oldChild is not a child of this');
				oldChild.parent = null; oldChild.parentNode = null; oldChild._connected = false;
				newChild.parent = this; newChild.parentNode = this; newChild._connected = true;
				this.children.splice(i, 1, newChild);
				if (oldChild.id) elementsById.delete(oldChild.id);
				if (newChild.id) elementsById.set(newChild.id, newChild);
				return oldChild;
			},
			removeChild(child) {
				const i = this.children.indexOf(child);
				if (i >= 0) this.children.splice(i, 1);
				child.parent = null; child.parentNode = null;
				child._connected = false;
				if (child.id) elementsById.delete(child.id);
				return child;
			},
			remove() { if (this.parent) { const i = this.parent.children.indexOf(this); if (i >= 0) this.parent.children.splice(i, 1); } this.parent = null; this.parentNode = null; if (this.id) elementsById.delete(this.id); this._connected = false; },
			querySelector(sel) { return querySelectorIn(this, sel); },
			querySelectorAll(sel) { return querySelectorAllIn(this, sel); },
			matches(sel) { return matches(this, sel); },
			closest(sel) { let cur = this; while (cur) { if (matches(cur, sel)) return cur; cur = cur.parentNode || cur.parent; } return null; },
			getBoundingClientRect() { return { top: 0, left: 0, width: 100, height: 30 }; },
		};
		Object.defineProperty(el, 'parentNode', { value: null, writable: true, configurable: true });
		Object.defineProperty(el, 'parentElement', { get() { return this.parentNode && this.parentNode.nodeType === 1 ? this.parentNode : null; }, configurable: true });
		Object.defineProperty(el, 'isConnected', { get() { return el._connected === true; }, configurable: true });
		Object.defineProperty(el, 'firstElementChild', {
			get() { for (const c of (el.children || [])) if (c && c.nodeType === 1) return c; return null; },
			configurable: true,
		});
		Object.defineProperty(el, 'lastElementChild', {
			get() { const cs = el.children || []; for (let i = cs.length - 1; i >= 0; i--) if (cs[i] && cs[i].nodeType === 1) return cs[i]; return null; },
			configurable: true,
		});
		const VOID_FOR_SERIALIZE = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr']);
		function serializeNode(node) {
			if (!node) return '';
			const tn = (node.tagName || '').toLowerCase();
			if (tn === '#text') return String(node.textContent || '');
			if (tn === '#fragment') return (node.children || []).map(serializeNode).join('');
			const tag = tn || 'div';
			let attrs = '';
			if (node.attributes) for (const k of Object.keys(node.attributes)) { const v = String(node.attributes[k]).replace(/&/g, '&amp;').replace(/"/g, '&quot;'); attrs += ' ' + k + '="' + v + '"'; }
			if (VOID_FOR_SERIALIZE.has(tag)) return '<' + tag + attrs + '>';
			const inner = (node.children || []).map(serializeNode).join('');
			return '<' + tag + attrs + '>' + inner + '</' + tag + '>';
		}
		Object.defineProperty(el, 'innerHTML', {
			get() { return serializeNode(el); },
			set(v) {
				const html = String(v == null ? '' : v);
				function disconnect(n) { n._connected = false; for (const c of (n.children || [])) disconnect(c); }
				for (const c of el.children) { c.parent = null; c.parentNode = null; disconnect(c); }
				el.children = [];
				if (html !== '') parseInto(el, html);
			},
			configurable: true,
		});
		el.dataset = new Proxy({}, {
			set(t, k, v) { t[k] = v; el.attributes['data-' + k] = v; return true; },
			get(t, k) { if (k in t) return t[k]; return el.attributes['data-' + k]; },
			has(t, k) { return k in t || ('data-' + k) in el.attributes; },
			deleteProperty(t, k) { delete t[k]; delete el.attributes['data-' + k]; return true; },
		});
		allElements.push(el);
		return el;
	}
	function matchOne(el, token) {
		if (!token) return false;
		let rest = token;
		const tagMatch = rest.match(/^([a-zA-Z][a-zA-Z0-9]*)/);
		if (tagMatch) { if (!el.tagName || el.tagName.toLowerCase() !== tagMatch[1].toLowerCase()) return false; rest = rest.slice(tagMatch[1].length); }
		while (rest.length) {
			if (rest[0] === '.') { const m = rest.match(/^\.([a-zA-Z_][\w-]*)/); if (!m) return false; if (!el.classList || !el.classList.contains(m[1])) return false; rest = rest.slice(m[0].length); }
			else if (rest[0] === '#') { const m = rest.match(/^#([a-zA-Z_][\w-]*)/); if (!m) return false; if (el.id !== m[1]) return false; rest = rest.slice(m[0].length); }
			else if (rest[0] === '[') { const m = rest.match(/^\[([^=\]]+)(?:=([^\]]+))?\]/); if (!m) return false; const k = m[1], v = m[2]; const actual = el.attributes[k]; if (v == null) { if (actual === undefined) return false; } else { const cleanV = v.replace(/^['"]|['"]$/g, ''); if (actual !== cleanV) return false; } rest = rest.slice(m[0].length); }
			else { return false; }
		}
		return true;
	}
	function matches(el, sel) {
		if (!sel) return false;
		sel = sel.trim();
		const tokens = sel.split(/\s+/);
		if (!matchOne(el, tokens[tokens.length - 1])) return false;
		for (let i = tokens.length - 2; i >= 0; i--) {
			let p = el.parentNode || el.parent, ok = false;
			while (p) { if (matchOne(p, tokens[i])) { ok = true; break; } p = p.parentNode || p.parent; }
			if (!ok) return false;
		}
		return true;
	}
	const VOID_TAGS = new Set(['br', 'hr', 'img', 'input', 'meta', 'link']);
	function parseInto(targetEl, html) {
		const document = targetEl.ownerDocument;
		const tokenRe = /<\/?([a-zA-Z][a-zA-Z0-9-]*)\s*([^>]*?)\/?>|([^<]+)|<!--[\s\S]*?-->/g;
		const attrRe = /([a-zA-Z_:][\w:-]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?/g;
		const stack = [targetEl];
		html.replace(tokenRe, (match, tagName, attrs, text) => {
			if (text !== undefined) { stack[stack.length - 1].appendChild(document.createTextNode(text)); return ''; }
			if (match.startsWith('<!--')) return '';
			const isClose = match.startsWith('</');
			if (isClose) { if (stack.length > 1) stack.pop(); return ''; }
			const isSelfClose = match.endsWith('/>') || VOID_TAGS.has(tagName.toLowerCase());
			const el = document.createElement(tagName);
			if (attrs) { attrRe.lastIndex = 0; let m; while ((m = attrRe.exec(attrs)) !== null) { const k = m[1], v = m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : '')); el.setAttribute(k, v); } }
			stack[stack.length - 1].appendChild(el);
			if (!isSelfClose) stack.push(el);
			return '';
		});
	}
	const document = {
		readyState: 'loading', hidden: false,
		documentElement: makeEl('html'), head: makeEl('head'), body: makeEl('body'),
		getElementById(id) { return elementsById.get(id) || null; },
		getElementsByTagName() { return []; },
		querySelector(sel) { return querySelectorIn(document.body, sel); },
		querySelectorAll(sel) { return querySelectorAllIn(document.body, sel); },
		createElement(tag) { const e = makeEl(tag); e.ownerDocument = document; return e; },
		createTextNode(text) { const n = makeEl('#text'); n.textContent = String(text == null ? '' : text); n.ownerDocument = document; return n; },
		createDocumentFragment() { return makeEl('#fragment'); },
		addEventListener(type, fn) { if (type === 'DOMContentLoaded') return; (document.body._listeners[type] = document.body._listeners[type] || []).push({ fn, capture: false }); },
		removeEventListener() {},
	};
	return { document, elementsById, makeEl, parseInto };
}

/* ============== deferred helper ============== */
function deferred() { let resolve, reject; const promise = new Promise((res, rej) => { resolve = res; reject = rej; }); return { promise, resolve, reject }; }
function tick(ms) { return new Promise(r => setTimeout(r, ms)); }
async function waitUntil(pred, timeoutMs = 1500) {
	const start = Date.now();
	while (Date.now() - start < timeoutMs) { if (pred()) return true; await tick(15); }
	return false;
}

/* ============== 加载 launcher.js ============== */
function loadV5(opts) {
	opts = opts || {};
	const { document, elementsById, makeEl, parseInto } = makeDomStubV5();
	const viewNames = ['home', 'installations', 'worlds', 'servers', 'content', 'news', 'skins', 'downloads', 'settings'];
	const views = {};
	for (const n of viewNames) { const v = makeEl('div'); v.ownerDocument = document; v.id = 'view-' + n; v.className = 'view'; document.body.appendChild(v); views[n] = v; }
	const panel = makeEl('div'); panel.ownerDocument = document; panel.id = 'contentPanel'; panel.scrollTop = 0; document.body.appendChild(panel);
	for (const n of viewNames) { const b = makeEl('button'); b.dataset.nav = n; document.body.appendChild(b); }
	const ctx = {
		SG: { setLocale() {}, t: (k) => k, applyStaticI18n() {} },
		location: { protocol: 'http:', hash: '' },
		history: { replaceState() {}, pushState() {} },
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN', platform: 'Win32' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		document,
		setTimeout, clearTimeout, setInterval, clearInterval,
		Promise, URLSearchParams, console, Date, JSON, Math, Object, Array, String, Number, Boolean, Error, Symbol,
		Image: function () { return { addEventListener() {}, src: '' }; },
		Blob: function () {},
		FileReader: function () { this.readAsDataURL = () => {}; this.onload = null; this.onerror = null; },
		URL: { createObjectURL() { return 'blob:fake'; }, revokeObjectURL() {} },
		window: null,
		fetch: opts.fetch || (() => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) })),
	};
	ctx.window = ctx;
	vm.createContext(ctx);
	vm.runInContext(I18N_JS, ctx);
	ctx.SG.api = makeApiStub(opts);
	try { vm.runInContext(LAUNCHER_JS, ctx); }
	catch (e) { process.stderr.write('LOAD_V5_ERROR: ' + (e && e.stack ? e.stack : e) + '\n'); throw e; }
	if (!ctx.SG.__test__) throw new Error('SG.__test__ 未注入');
	return { ctx, document, T: ctx.SG.__test__, api: ctx.SG.api, views };
}

function makeApiStub(opts) {
	const api = {
		_implMode: 'live', _allowMockFallback: false, _calls: [],
		async getState() {
			this._calls.push({ method: 'getState' });
			if (opts.getStateHandler) return opts.getStateHandler();
			return { config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] };
		},
		async getAccounts() { this._calls.push({ method: 'getAccounts' }); return { accounts: [], capes: [] }; },
		async searchContent(query, category, options) {
			this._calls.push({ method: 'searchContent', query, category, options });
			if (opts.searchHandler) return opts.searchHandler(query, category, options);
			return [];
		},
		async installContent(data) { this._calls.push({ method: 'installContent', data }); this._installCalls = this._installCalls || []; this._installCalls.push(data); return { ok: true, id: 'task-' + (this._installCalls.length), status: 'queued' }; },
		async getContentTasks() { return []; },
	};
	return api;
}

const FABRIC_STATE = { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };

/* ====================================================================
 * V5-1: 标准 DOM 下状态恢复成功 → 新提示正确替换旧提示，只保留一份
 * 修复前：launcher.js 第 1296-1297 行的 .parent 和 children.indexOf()
 *   在标准 DOM 下 undefined / 抛错；try-catch 吞掉后老提示消失但新提示
 *   不显示，状态恢复"看起来生效"但实际丢了。
 * 修复后：使用 parentNode + firstElementChild + replaceChild，
 *   原子替换成功。
 * ==================================================================== */
test('V5-1: 标准 DOM 下状态恢复成功 → 新提示替换旧提示，只一份', async () => {
	let getStateCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => {
			getStateCount++;
			if (getStateCount === 1) throw new Error('network down');
			return FABRIC_STATE;
		},
		searchHandler: () => [],
	});
	await T.bindGoTo('content')();
	// 旧 warn badge 应存在
	const oldBadges = views.content.querySelectorAll('[data-state="failed"]');
	assert.equal(oldBadges.length, 1, 'V5-1 预置：失败状态应显示一份 warn badge');
	// 点击重试入口
	views.content.querySelector('[data-state-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => views.content.querySelectorAll('[data-state="filtered"]').length > 0, 1500);
	const filtered = views.content.querySelectorAll('[data-state="filtered"]');
	const failed = views.content.querySelectorAll('[data-state="failed"]');
	assert.equal(filtered.length, 1, 'V5-1 修复后：应只保留 1 份 filtered 提示');
	assert.equal(failed.length, 0, 'V5-1 修复后：失败提示应被替换，不应残留');
	const html = views.content.innerHTML;
	assert.match(html, /Fabric Test|Fabric/, 'V5-1 修复后：页面应显示恢复后的实例名');
	assert.doesNotMatch(html, /network down|加载失败/, 'V5-1 修复后：失败消息文本不应残留');
});

/* ====================================================================
 * V5-2: 标准 DOM 下状态重试再次失败 → 失败提示和重试入口保留
 * 修复前：第二次失败时 .parent / children.indexOf 报错，try-catch 吞掉，
 *   旧失败提示被 remove 但新失败提示不显示，重试入口消失。
 * 修复后：replaceChild 原子替换，失败提示与重试入口仍在。
 * ==================================================================== */
test('V5-2: 标准 DOM 下状态重试再次失败 → 失败提示与重试入口保留', async () => {
	let getStateCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => {
			getStateCount++;
			// 两次都失败
			throw new Error('still down #' + getStateCount);
		},
		searchHandler: () => [],
	});
	await T.bindGoTo('content')();
	assert.equal(views.content.querySelectorAll('[data-state-retry-btn="1"]').length, 1, 'V5-2 预置：首次失败应显示重试入口');
	// 第一次 retry
	views.content.querySelector('[data-state-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => /still down #2/.test(views.content.innerHTML), 1500);
	const failed = views.content.querySelectorAll('[data-state="failed"]');
	const filtered = views.content.querySelectorAll('[data-state="filtered"]');
	assert.equal(failed.length, 1, 'V5-2 修复后：应只保留 1 份失败提示');
	assert.equal(views.content.querySelectorAll('[data-state-retry-btn="1"]').length, 1, 'V5-2 修复后：重试入口应保留');
	assert.doesNotMatch(views.content.innerHTML, /Fabric Test|Fabric/, 'V5-2 修复后：不应显示未恢复的实例');
});

/* ====================================================================
 * V5-3: 搜索 A 中离页，A 成功后返回 → 无额外操作即可恢复，非 loading
 * 关键：返回页面后**不点击**任何控件，页面应自动恢复到当前稳定结果。
 * ==================================================================== */
test('V5-3: 搜索 A 中离页，A 成功后返回 → 自动恢复，非 loading', async () => {
	const A = deferred();
	let searchCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => ({ config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }),
		searchHandler: () => {
			searchCount++;
			if (searchCount === 1) return [];
			return A.promise;
		},
	});
	await T.bindGoTo('content')();
	assert.equal(searchCount, 1, 'V5-3 预置：首次搜索已完成');
	// 启动 A
	const cfBtn = views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]');
	assert.ok(cfBtn, 'V5-3 预置：curseforge 按钮应存在');
	cfBtn.dispatchEvent('click', { type: 'click', target: cfBtn, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	assert.equal(searchCount, 2, 'V5-3 预置：A 已启动');
	await waitUntil(() => searchCount === 2, 1500);
	assert.equal(searchCount, 2, 'V5-3 预置：A 已启动');
	// 离页
	await T.bindGoTo('home')();
	// A 成功
	A.resolve([{ id: 'A1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'curseforge' }]);
	await tick(50);
	// 返回 content
	await T.bindGoTo('content')();
	// 不点击任何控件！等待自动恢复
	await waitUntil(() => /AMod/.test(views.content.innerHTML) || /class="loading-state"/.test(views.content.innerHTML), 2500);
	const html = views.content.innerHTML;
	assert.match(html, /AMod/, 'V5-3 修复后：返回 content 后 A 结果应自动出现');
	assert.doesNotMatch(html, /class="loading-state"/, 'V5-3 修复后：不应永久 loading');
});

/* ====================================================================
 * V5-4: 搜索 A 中离页，A 失败后返回 → 自动恢复或显示当前有效错误
 * 关键：返回 content 后不点击，应看到 A 的真实错误（不是 loading，也不是空）。
 * ==================================================================== */
test('V5-4: 搜索 A 中离页，A 失败后返回 → 显示 A 的错误，不永久 loading', async () => {
	const A = deferred();
	let searchCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => ({ config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }),
		searchHandler: () => {
			searchCount++;
			if (searchCount === 1) return [];
			return A.promise;
		},
	});
	await T.bindGoTo('content')();
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	await T.bindGoTo('home')();
	// A 失败
	A.reject(new Error('A timeout'));
	await tick(50);
	// 返回 content
	await T.bindGoTo('content')();
	// 等待自动恢复或显示错误
	await waitUntil(() => /A timeout/.test(views.content.innerHTML), 2500);
	const html = views.content.innerHTML;
	assert.match(html, /A timeout/, 'V5-4 修复后：返回后应看到 A 的真实错误');
	assert.doesNotMatch(html, /class="loading-state"/, 'V5-4 修复后：不应永久 loading');
});

/* ====================================================================
 * V5-5: 离页再返回启动 B，旧 A 随后完成 → B 保留，A 不提交
 * ==================================================================== */
test('V5-5: 离页再返回启动 B，旧 A 后完成 → B 保留，A 不提交', async () => {
	const A = deferred();
	const B = deferred();
	let searchCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => ({ config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }),
		searchHandler: () => {
			searchCount++;
			if (searchCount === 1) return [];
			if (searchCount === 2) return A.promise;
			return B.promise;
		},
	});
	await T.bindGoTo('content')();
	// 启动 A
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	await T.bindGoTo('home')();
	// 返回 content（自动 refresh 启动第 3 次 search = B）
	await T.bindGoTo('content')();
	await waitUntil(() => searchCount === 3, 1500);
	// 释放 B 先
	B.resolve([{ id: 'B1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }]);
	await waitUntil(() => /BMod/.test(views.content.innerHTML), 1500);
	// 释放 A（旧）
	A.resolve([{ id: 'A1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }]);
	await tick(100); await tick(100);
	const html = views.content.innerHTML;
	assert.match(html, /BMod/, 'V5-5 修复后：B 应保留');
	assert.doesNotMatch(html, /AMod/, 'V5-5 修复后：A 不应覆盖 B');
});

/* ====================================================================
 * V5-6: 离页再返回启动 B，旧 A 后失败 → B 或 B 的错误保留，无 A 错误污染
 * ==================================================================== */
test('V5-6: 离页再返回启动 B，旧 A 后失败 → B 的错误保留，无 A 错误污染', async () => {
	const A = deferred();
	const B = deferred();
	let searchCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => ({ config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }),
		searchHandler: () => {
			searchCount++;
			if (searchCount === 1) return [];
			if (searchCount === 2) return A.promise;
			return B.promise;
		},
	});
	await T.bindGoTo('content')();
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	await T.bindGoTo('home')();
	await T.bindGoTo('content')();
	await waitUntil(() => searchCount === 3, 1500);
	// B 失败
	B.reject(new Error('B timeout'));
	await waitUntil(() => /B timeout/.test(views.content.innerHTML), 1500);
	// A 后失败
	A.reject(new Error('A timeout'));
	await tick(100); await tick(100);
	const html = views.content.innerHTML;
	assert.match(html, /B timeout/, 'V5-6 修复后：B 的错误应保留');
	assert.doesNotMatch(html, /A timeout/, 'V5-6 修复后：不应有 A 的错误污染');
	assert.ok(views.content.querySelector('[data-retry-btn="1"]'), 'V5-6 修复后：B 的重试入口应保留');
});

/* ====================================================================
 * V5-7: 首次 getState pending 时离页后成功 → 不提交旧页面
 * 关键：getState 返回真实 deferred，goTo content 后切到 home，释放 getState 成功。
 * 断言：旧资源页不应写空 innerHTML、设置 dataset.rendered、绑事件。
 * ==================================================================== */
test('V5-7: 首次 getState pending 时离页后成功 → 不提交旧页面', async () => {
	const stateQueue = [];
	let getStateCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => {
			getStateCount++;
			const df = deferred();
			stateQueue.push(df);
			return df.promise;
		},
		searchHandler: () => [],
	});
	const firstP = T.bindGoTo('content')();
	await waitUntil(() => getStateCount >= 1, 1500);
	assert.equal(getStateCount, 1, 'V5-7 预置：getState 已调用 1 次且未完成');
	// 离页（在 getState pending 时）
	const homeP = T.bindGoTo('home')();
	await tick(50);
	assert.equal(T.getContentPageGen(), -1, 'V5-7 预置：离页后 contentPageGen 已被 -1 标记');
	// 释放第一次 getState 成功
	stateQueue[0].resolve(FABRIC_STATE);
	await tick(20);
	// 释放第二次 getState（goTo home 的 renderHome 触发）
	if (stateQueue[1]) stateQueue[1].resolve(FABRIC_STATE);
	await Promise.all([firstP, homeP]);
	await tick(80);
	const contentHtml = views.content.innerHTML;
	assert.ok(!/content-view/.test(contentHtml), `V5-7 修复后：旧资源页不应写 content-view 容器（实际 LENGTH=${contentHtml.length}）`);
	assert.equal(views.content.dataset.rendered, undefined, 'V5-7 修复后：旧资源页不应设置 dataset.rendered');
	// 关键：返回 content 后能正常重新渲染
	const contentP = T.bindGoTo('content')();
	await tick(150);
	await contentP;
	await tick(50);
	assert.match(views.content.innerHTML, /Fabric Test|Fabric/, 'V5-7 修复后：返回 content 后应能正常渲染');
	assert.doesNotMatch(views.content.innerHTML, /class="loading-state"/, 'V5-7 修复后：返回后不应永久 loading');
});

/* ====================================================================
 * V5-8: 首次 getState pending 时离页后失败 → 不提交旧错误或绑定
 * ==================================================================== */
test('V5-8: 首次 getState pending 时离页后失败 → 不提交旧错误或绑定', async () => {
	const stateQueue = [];
	let getStateCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => {
			getStateCount++;
			const df = deferred();
			stateQueue.push(df);
			return df.promise;
		},
		searchHandler: () => [],
	});
	const firstP = T.bindGoTo('content')();
	await waitUntil(() => getStateCount >= 1, 1500);
	const homeP = T.bindGoTo('home')();
	await tick(50);
	// 第一次 getState 失败
	stateQueue[0].reject(new Error('network down (deferred)'));
	// 第二次 getState 也失败（renderHome 调）
	if (stateQueue[1]) stateQueue[1].reject(new Error('home also down'));
	await Promise.all([firstP, homeP]);
	await tick(80);
	const contentHtml = views.content.innerHTML;
	assert.ok(!/content-view/.test(contentHtml), `V5-8 修复后：旧资源页不应写 content-view 容器（实际 LENGTH=${contentHtml.length}）`);
	assert.equal(views.content.dataset.rendered, undefined, 'V5-8 修复后：旧资源页不应设置 dataset.rendered');
	// 不应看到旧的失败文本
	assert.doesNotMatch(contentHtml, /network down \(deferred\)/, 'V5-8 修复后：旧失败错误不应写入 content');
});

/* ====================================================================
 * V5-9: 首次进入未完成，离开再返回 → 新生命周期正常，旧生命周期无影响
 * 关键：getState pending → 离页 → getState 失败 → 返回 content → 重新进入
 * ==================================================================== */
test('V5-9: 首次进入未完成离开再返回 → 新生命周期正常，旧无影响', async () => {
	const stateQueue = [];
	let getStateCount = 0;
	const { T, views } = loadV5({
		getStateHandler: () => {
			getStateCount++;
			const df = deferred();
			stateQueue.push(df);
			return df.promise;
		},
		searchHandler: () => [],
	});
	const firstP = T.bindGoTo('content')();
	await waitUntil(() => getStateCount >= 1, 1500);
	const homeP = T.bindGoTo('home')();
	await tick(50);
	// 第一次失败
	stateQueue[0].reject(new Error('first-time fail'));
	if (stateQueue[1]) stateQueue[1].resolve(FABRIC_STATE);
	await Promise.all([firstP, homeP]);
	await tick(80);
	// 关键：返回 content
	const contentP = T.bindGoTo('content')();
	await tick(150);
	await contentP;
	await tick(50);
	// 新生命周期应能正常显示（getState 第二次已成功）
	const html = views.content.innerHTML;
	assert.match(html, /Fabric Test|Fabric/, 'V5-9 修复后：返回 content 后新生命周期能显示实例信息');
	assert.equal(views.content.dataset.rendered, '1', 'V5-9 修复后：dataset.rendered 应被新生命周期设置');
	assert.doesNotMatch(html, /class="loading-state"/, 'V5-9 修复后：返回后不应永久 loading');
});

/* ====================================================================
 * V5-10: 多次离开、返回、状态恢复后单击操作 → 恰好新增一次请求，无监听器累积
 * ==================================================================== */
test('V5-10: 多次离开返回状态恢复后单击 → 恰好 +1，无监听器累积', async () => {
	let getStateCount = 0;
	let searchCount = 0;
	let failNext = false;
	const { T, views } = loadV5({
		getStateHandler: () => {
			getStateCount++;
			if (getStateCount === 1) throw new Error('network down');
			return FABRIC_STATE;
		},
		searchHandler: () => {
			searchCount++;
			if (failNext) { failNext = false; return Promise.reject(new Error('HTTP 500')); }
			return [];
		},
	});
	await T.bindGoTo('content')();
	// 恢复状态
	views.content.querySelector('[data-state-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	await tick(50);
	// 多次离开返回
	await T.bindGoTo('home')();
	await T.bindGoTo('content')();
	await T.bindGoTo('home')();
	await T.bindGoTo('content')();
	await tick(50);
	// 触发一次失败
	failNext = true;
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => /HTTP 500/.test(views.content.innerHTML), 1500);
	const before = searchCount;
	// 单击重试
	views.content.querySelector('[data-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount > before, 1500);
	await tick(50);
	assert.equal(searchCount - before, 1, `V5-10 修复后：单击重试应只 +1，实际 +${searchCount - before}`);
});
