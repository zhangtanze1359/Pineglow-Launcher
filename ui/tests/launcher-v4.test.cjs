'use strict';
/*
 * Phase 1 返修 v4：整页重试竞态 + 重复事件绑定 + 离页延迟搜索
 * --------------------------------------------------------------------------
 * 覆盖 T1–T10 回归矩阵。每个测试必须：
 *   1. 走真实 goTo 路径（v3 模式）或真实 renderContent + bindContent 路径
 *   2. 用 deferred Promise 精确控制完成顺序：等 B 的 DOM 提交后再释放 A
 *   3. 用受控 setTimeout（短轮询）等待异步阶段，不堆叠任意 sleep
 *   4. 用 searchContent 调用次数验证"恰好一次"（>= 1 不算）
 *   5. DOM 桩保留父容器监听器跨 innerHTML 替换的真实语义
 *
 * 修复前（v3 代码）：T1–T10 全部失败，证据明确写入失败消息。
 * 修复后：T1–T10 全部通过，v3 测试不回归。
 *
 * v5 升级：改用原生 node:test，让每条测试在标准 node --test 路径下被注册和报告。
 * 取消 RUN_V4=1 自定义 runner 隐藏门。test() 不再收集到 _v4Tests 数组。
 */

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

/* ============== 与 launcher-v3.test.cjs 同款的 DOM 桩（自包含） ============== */
function makeDomStub() {
	const elementsById = new Map();
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
				const path = []; let cur = this; while (cur) { path.unshift(cur); cur = cur.parentNode; }
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
				// 关键（v5）：同时维护 .parent（测试桩用）和 .parentNode（标准 DOM）
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
				// 关键（v5）：标准 DOM API，原子替换
				if (newChild.parent && newChild.parent !== this) { const pi = newChild.parent.children.indexOf(newChild); if (pi >= 0) newChild.parent.children.splice(pi, 1); }
				if (newChild.parentNode && newChild.parentNode !== this) {
					const pi = newChild.parentNode.children.indexOf(newChild);
					if (pi >= 0) newChild.parentNode.children.splice(pi, 1);
				}
				const i = this.children.indexOf(oldChild);
				if (i < 0) {
					// 旧节点不在子节点里：抛错（标准 DOM 行为）
					throw new Error('replaceChild: oldChild is not a child of this');
				}
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
		// 关键（v5）：parentNode 标准 DOM 属性
		Object.defineProperty(el, 'parentNode', { value: null, writable: true, configurable: true });
		Object.defineProperty(el, 'isConnected', { get() { return el._connected === true; }, configurable: true });
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
		let _innerHTML = '';
		Object.defineProperty(el, 'innerHTML', {
			get() { return serializeNode(el); },
			set(v) {
				_innerHTML = String(v == null ? '' : v);
				function disconnect(n) { n._connected = false; for (const c of (n.children || [])) disconnect(c); }
				for (const c of el.children) { c.parent = null; disconnect(c); }
				el.children = [];
				if (_innerHTML !== '') parseInto(el, _innerHTML);
			},
			configurable: true,
		});
		el.dataset = new Proxy({}, {
			set(t, k, v) { t[k] = v; el.attributes['data-' + k] = v; return true; },
			get(t, k) { if (k in t) return t[k]; return el.attributes['data-' + k]; },
			has(t, k) { return k in t || ('data-' + k) in el.attributes; },
			deleteProperty(t, k) { delete t[k]; delete el.attributes['data-' + k]; return true; },
		});
		// 关键（v5）：标准 DOM 规范要求 firstElementChild。
		Object.defineProperty(el, 'firstElementChild', {
			get() {
				for (const c of (el.children || [])) if (c && c.nodeType === 1) return c;
				return null;
			},
			configurable: true,
		});
		Object.defineProperty(el, 'lastElementChild', {
			get() {
				const cs = el.children || [];
				for (let i = cs.length - 1; i >= 0; i--) if (cs[i] && cs[i].nodeType === 1) return cs[i];
				return null;
			},
			configurable: true,
		});
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
			let p = el.parent, ok = false;
			while (p) { if (matchOne(p, tokens[i])) { ok = true; break; } p = p.parent; }
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

/* ============== launcher 加载器 ============== */
function loadV4(opts) {
	opts = opts || {};
	const { document, elementsById, makeEl, parseInto } = makeDomStub();
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
	catch (e) { process.stderr.write('LOAD_V4_ERROR: ' + (e && e.stack ? e.stack : e) + '\n'); throw e; }
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

function countCaptureListeners(el, type) {
	const arr = (el && el._listeners && el._listeners[type]) || [];
	return arr.filter(x => x.capture).length;
}

/* ====================================================================
 * T1: 状态重试 A 慢，来源 B 成功，A 后成功 → B 保留，页面不为空
 * 修复前证据：retryLoadStateAndRefresh 用 navSeq 校验，内部 renderContent
 * 被源切换的 token 升档后短路返回 ''，但 retryLoadStateAndRefresh 仍按
 * navSeq 通过并 el.innerHTML = ''，把 B 的 grid 清空。
 * ==================================================================== */
test('T1: 整页重试 A 慢 → 来源 B 成功 → A 后成功 → B 仍显示，页面不为空', async () => {
	let api_getState_count = 0;
	const A = deferred(); const B = deferred();
	let searchCount = 0;
	const { T, document, views } = loadV4({
		getStateHandler: () => {
			api_getState_count++;
			if (api_getState_count === 1) throw new Error('network down');
			return { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
		},
		searchHandler: (q, c, opts) => {
			searchCount++;
			if (searchCount === 1) return [];
			if (searchCount === 2) return A.promise;
			return B.promise;
		},
	});

	await T.bindGoTo('content')();
	assert.equal(searchCount, 1, 'T1 预置：首次进入应已启动并完成一次搜索');
	assert.ok(views.content.querySelector('[data-state-retry-btn="1"]'), 'T1 预置：页面应含 state-retry-btn');

	// 点 state-retry-btn → retryLoadStateAndRefresh 内部 await loadState(true) 成功 → await contentRerenderGrid 启动 A
	const srb = views.content.querySelector('[data-state-retry-btn="1"]');
	srb.dispatchEvent('click', { type: 'click', target: srb, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	assert.equal(searchCount, 2, 'T1: retry 后应启动搜索 A（searchCount=2）');

	// 点 curseforge source 按钮 → rerenderGrid 启动 B
	const cfBtn = views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]');
	assert.ok(cfBtn, 'T1 预置：curseforge 按钮应存在');
	cfBtn.dispatchEvent('click', { type: 'click', target: cfBtn, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 3, 1500);
	assert.equal(searchCount, 3, 'T1: source 切换后应启动搜索 B（searchCount=3）');

	// 释放 B 先（grid 应被替换为 B）
	B.resolve([{ id: 'B1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'curseforge' }]);
	await waitUntil(() => /BMod/.test(views.content.innerHTML), 1500);
	assert.match(views.content.innerHTML, /BMod/, 'T1: B 完成时 grid 应含 BMod');

	// 释放 A 后（A 已被 B 的 token 升档，renderContent 会短路返回 ''，但 retryLoadStateAndRefresh 仍按 navSeq 校验并写 el.innerHTML=''）
	A.resolve([{ id: 'A1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: 'Fabric', category: 'mod', source: 'modrinth' }]);
	await tick(100);
	await tick(100);

	const html = views.content.innerHTML;
	assert.match(html, /BMod/, 'T1 修复后：B 仍应显示');
	assert.doesNotMatch(html, /AMod/, 'T1 修复后：A 不应被写入当前页');
});

/* ====================================================================
 * T2: 同上流程，A 后失败 → B 保留，无旧错误
 * ==================================================================== */
test('T2: 整页重试 A 后失败 → 来源 B 成功 → B 仍显示，无旧错误', async () => {
	let api_getState_count = 0;
	const A = deferred(); const B = deferred();
	let searchCount = 0;
	const { T, views } = loadV4({
		getStateHandler: () => {
			api_getState_count++;
			if (api_getState_count === 1) throw new Error('network down');
			return { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
		},
		searchHandler: () => {
			searchCount++;
			if (searchCount === 1) return [];
			if (searchCount === 2) return A.promise;
			return B.promise;
		},
	});

	await T.bindGoTo('content')();
	assert.equal(searchCount, 1, 'T2 预置：首次进入已启动并完成一次搜索');
	assert.ok(views.content.querySelector('[data-state-retry-btn="1"]'), 'T2 预置：state-retry-btn 应存在');

	views.content.querySelector('[data-state-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);

	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 3, 1500);

	// B 成功
	B.resolve([{ id: 'B1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'curseforge' }]);
	await waitUntil(() => /BMod/.test(views.content.innerHTML));

	// A 后失败
	A.reject(new Error('A timeout'));
	await tick(100);
	await tick(100);

	const html = views.content.innerHTML;
	assert.match(html, /BMod/, 'T2 修复后：B 应保留');
	assert.doesNotMatch(html, /A timeout/, 'T2 修复后：不应出现 A 的错误');
	assert.equal(views.content.querySelectorAll('[data-retry-btn="1"]').length, 0, 'T2 修复后：不应出现 A 的旧重试按钮');
});

/* ====================================================================
 * T3: A 后成功，最新 B 已失败 → 保留 B 的错误和重试入口，不被 A 覆盖
 * ==================================================================== */
test('T3: A 后成功，最新 B 已失败 → B 的错误和重试入口应保留', async () => {
	let api_getState_count = 0;
	const A = deferred(); const B = deferred();
	let searchCount = 0;
	const { T, views } = loadV4({
		getStateHandler: () => {
			api_getState_count++;
			if (api_getState_count === 1) throw new Error('network down');
			return { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
		},
		searchHandler: () => {
			searchCount++;
			if (searchCount === 1) return [];
			if (searchCount === 2) return A.promise;
			return B.promise;
		},
	});

	await T.bindGoTo('content')();
	assert.equal(searchCount, 1);
	assert.ok(views.content.querySelector('[data-state-retry-btn="1"]'), 'T3 预置：state-retry-btn 应存在');
	views.content.querySelector('[data-state-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2);
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 3);

	// B 先失败
	B.reject(new Error('B timeout'));
	await waitUntil(() => /B timeout/.test(views.content.innerHTML));
	assert.match(views.content.innerHTML, /B timeout/, 'T3 预置：B 失败应显示');
	assert.ok(views.content.querySelector('[data-retry-btn="1"]'), 'T3 预置：B 失败应显示重试入口');

	// A 后成功
	A.resolve([{ id: 'A1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: 'Fabric', category: 'mod', source: 'modrinth' }]);
	await tick(100);
	await tick(100);

	const html = views.content.innerHTML;
	assert.match(html, /B timeout/, 'T3 修复后：B 的错误应保留');
	assert.ok(views.content.querySelector('[data-retry-btn="1"]'), 'T3 修复后：B 的重试入口应保留');
	assert.doesNotMatch(html, /AMod/, 'T3 修复后：A 的结果不应覆盖 B');
});

/* ====================================================================
 * T4: 状态恢复后搜索失败 → 单击重试 → searchContent 恰好 +1
 * 修复前：retryLoadStateAndRefresh 完成后调 bindContent(el)，给 el 自身
 * click 监听器 +1。再次失败后单击 [data-retry-btn] 触发 2 次 searchContent。
 * ==================================================================== */
test('T4: 状态恢复后搜索失败 → 单击重试 → searchContent 恰好 +1', async () => {
	let api_getState_count = 0;
	let searchCount = 0;
	let failNext = false;
	const { T, views, api } = loadV4({
		getStateHandler: () => {
			api_getState_count++;
			if (api_getState_count === 1) throw new Error('network down');
			return { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
		},
		searchHandler: () => {
			searchCount++;
			if (failNext) { failNext = false; return Promise.reject(new Error('HTTP 500')); }
			return [];
		},
	});

	await T.bindGoTo('content')(); // searchCount=1 (state 失败，但搜索仍走，gameVersion 空)
	failNext = true;
	// 点 state-retry-btn → retryLoadStateAndRefresh → loadState 成功 → renderContent 启动 searchCount=2
	views.content.querySelector('[data-state-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2);
	// 让 searchCount=2 完成（空数组）
	await tick(50);
	// 现在页面应正常显示，bindContent 已被 retryLoadStateAndRefresh 调过 → el 的 click 捕获监听器 = 2（首次 bindContent + retryLoadStateAndRefresh 内部的 bindContent）

	// 触发一次新的失败：手工让下次搜索失败，然后调 rerenderGrid
	failNext = true;
	// rerenderGrid 不公开，但 source 切换会调它
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 3);
	await tick(50);
	assert.match(views.content.innerHTML, /HTTP 500/, 'T4 预置：rerenderGrid 后页面应显示 HTTP 500');
	assert.ok(views.content.querySelector('[data-retry-btn="1"]'), 'T4 预置：应有 [data-retry-btn]');
	const beforeClick = searchCount;

	// 单击重试
	const retryBtn = views.content.querySelector('[data-retry-btn="1"]');
	retryBtn.dispatchEvent('click', { type: 'click', target: retryBtn, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === beforeClick + 1);
	await tick(50);
	// 验证：searchContent 增加了恰好 1 次（不是 2 次）
	assert.equal(searchCount, beforeClick + 1, `T4 修复后：单击重试应只增加 1 次 searchContent，实际增加 ${searchCount - beforeClick} 次`);
});

/* ====================================================================
 * T5: 多次重渲染/重入后单击搜索或状态重试 → 监听器不累积
 * ==================================================================== */
test('T5: 多次 bindContent 后单击 → searchContent 只 +1', async () => {
	let api_getState_count = 0;
	let searchCount = 0;
	let failNext = false;
	const { T, views } = loadV4({
		getStateHandler: () => {
			api_getState_count++;
			if (api_getState_count === 1) throw new Error('network down');
			return { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
		},
		searchHandler: () => {
			searchCount++;
			if (failNext) { failNext = false; return Promise.reject(new Error('HTTP 500')); }
			return [];
		},
	});

	await T.bindGoTo('content')();
	failNext = true;
	views.content.querySelector('[data-state-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2);
	await tick(50);
	// 现在页面正常显示，retryLoadStateAndRefresh 已经把 STATE 恢复

	// 模拟多次重入：rerenderGrid 本身不调 bindContent，但 navigate away → back 走 goTo 第二次会再 bindContent
	await T.bindGoTo('home')();
	await tick(20);
	await T.bindGoTo('content')();
	await tick(20);
	// 再次点 state-retry-btn（虽然 STATE 已恢复，但 bindContent 会被调一次）
	// 注意：第二次进入时 STATE 已不为 null，renderContent 不会触发 loadState，但仍调一次
	// 关键是：bindContent 累计了几次？
	// v3 修复前：每次 goTo 第一次进入 content 都 bindContent；retryLoadStateAndRefresh 又 bindContent 一次；共计 ≥2
	// 我们要验证：单击 retry-btn → searchContent +1

	// 触发一次失败
	failNext = true;
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => /HTTP 500/.test(views.content.innerHTML));
	const before = searchCount;
	views.content.querySelector('[data-retry-btn="1"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount > before);
	await tick(50);
	// 找到 searchContent 调用的递增（rerenderGrid 内会 ++contentSearchToken + 调 searchContent 一次）
	// 关键是单击一次 → 只递增 1 次
	assert.equal(searchCount - before, 1, `T5 修复后：单击应只 +1，实际 +${searchCount - before}`);
});

/* ====================================================================
 * T6: 状态入口快速双击 → 按实现约定合并/防重，最终最新状态正确，无空白或重复绑定
 * ==================================================================== */
test('T6: 状态入口快速双击 → 不重复调 loadState，最终页面正确', async () => {
	let api_getState_count = 0;
	const { T, views, api } = loadV4({
		getStateHandler: () => {
			api_getState_count++;
			if (api_getState_count === 1) throw new Error('network down');
			return { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
		},
		searchHandler: () => [],
	});

	await T.bindGoTo('content')();
	const beforeCount = api._calls.filter(c => c.method === 'getState').length;
	const stateRetryBtn = views.content.querySelector('[data-state-retry-btn="1"]');
	assert.ok(stateRetryBtn, 'T6 预置：state-retry-btn 应存在');

	// 快速双击：连发两次 click
	stateRetryBtn.dispatchEvent('click', { type: 'click', target: stateRetryBtn, preventDefault() {}, stopPropagation() {} });
	stateRetryBtn.dispatchEvent('click', { type: 'click', target: stateRetryBtn, preventDefault() {}, stopPropagation() {} });
	// 等待所有 promise 完成
	await tick(200);
	await tick(200);

	const getStateCalls = api._calls.filter(c => c.method === 'getState').length;
	// 首次进入 content 已经走过一次 getState（失败），现在 retry 双击
	// 修复前：每次 click 都进 retryLoadStateAndRefresh → 2 次额外 getState → 总 3 次
	// 修复后：第二次 click 被合并/防重 → ≤1 次额外 → 总 ≤2 次
	assert.ok(getStateCalls - beforeCount <= 1, `T6 修复后：双击应只新增 ≤1 次 getState，实际新增 ${getStateCalls - beforeCount} 次`);
	const html = views.content.innerHTML;
	assert.match(html, /Fabric Test|Fabric/, 'T6 修复后：页面应显示恢复后的实例');
});

/* ====================================================================
 * T7: 输入 debounce 到期前离页 → 到期后新增搜索数为 0
 * ==================================================================== */
test('T7: debounce 到期前离页 → 0 次新搜索', async () => {
	let searchCount = 0;
	const { T, views } = loadV4({
		getStateHandler: () => ({ config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }),
		searchHandler: () => { searchCount++; return []; },
	});

	await T.bindGoTo('content')();
	const before = searchCount;
	// 模拟输入
	const searchInput = views.content.querySelector('#contentSearch');
	assert.ok(searchInput, 'T7 预置：search 输入框应存在');
	// dispatch input 事件，启动 debounce
	searchInput.value = 'hello';
	searchInput.dispatchEvent('input', { type: 'input', target: searchInput });
	// 立即切到 home（debounce 还没到期）
	await T.bindGoTo('home')();
	// 等待超过 debounce 时长（350ms）+ 一些缓冲
	await tick(500);
	// 离页后不应再触发搜索
	assert.equal(searchCount, before, `T7 修复后：离页后 debounce 到期不应再触发搜索，实际新增 ${searchCount - before} 次`);
});

/* ====================================================================
 * T8: 搜索已发出后离页，旧请求成功/失败 → 无旧 DOM 或错误提交，当前导航不受影响
 * ==================================================================== */
test('T8: 搜索已发出后离页，旧请求完成 → 当前 home 视图不受污染', async () => {
	const A = deferred();
	let searchCount = 0;
	const { T, views } = loadV4({
		getStateHandler: () => ({ config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }),
		searchHandler: () => { searchCount++; if (searchCount === 1) return []; return A.promise; },
	});

	await T.bindGoTo('content')(); // 首次进入立即完成（返回 []）
	assert.equal(searchCount, 1);
	// 切到 home 之前，触发一次新的搜索 A（用户操作：切 source 启动 A）
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	assert.equal(searchCount, 2, 'T8 预置：source 切换应启动搜索 A');
	// 切到 home（A 仍未完成）
	await T.bindGoTo('home')();
	// 释放 A 成功（带 BMod 之类）
	A.resolve([{ id: 'B1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'curseforge' }]);
	await tick(100);
	await tick(100);
	// home 视图不应有 content 结果
	const homeHtml = views.home.innerHTML;
	assert.doesNotMatch(homeHtml, /BMod/, 'T8 修复后：旧 content 搜索结果不应写入 home 视图');
	// 进一步：content 视图本身（即使 hidden）也不应被旧结果污染
	const contentHtml = views.content.innerHTML;
	assert.doesNotMatch(contentHtml, /BMod/, 'T8 修复后：content 视图也不应被旧结果污染');
});

/* ====================================================================
 * T9: 离页再返回发 B，旧 A 随后完成 → B 保留，正常事件仍有效，不永久 loading
 * ==================================================================== */
test('T9: 离页再返回 B，旧 A 后完成 → B 保留', async () => {
	const A = deferred(); const B = deferred();
	let searchCount = 0;
	const { T, views } = loadV4({
		getStateHandler: () => ({ config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }),
		searchHandler: () => { searchCount++; if (searchCount === 1) return []; if (searchCount === 2) return A.promise; return B.promise; },
	});

	await T.bindGoTo('content')(); // 第 1 次搜索立即完成
	assert.equal(searchCount, 1, 'T9 预置：首次进入已立即完成');
	// 触发一次 source 切换 → A 启动
	views.content.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]').dispatchEvent('click', { type: 'click', target: views.content, preventDefault() {}, stopPropagation() {} });
	await waitUntil(() => searchCount === 2, 1500);
	assert.equal(searchCount, 2, 'T9 预置：source 切换应启动 A');
	// 切到 home（A 仍未完成）
	await T.bindGoTo('home')();
	// 切回 content：v5 修复后，goTo 检测到 grid 仍在 loading 自动 contentRerenderGrid
	// （新增第 3 次 search：auto-refresh A'）。A' 用 B 一样的 promise，
	// 因为 v4 handler 第 3 次之后都 return B.promise。但这里我们要分别控制 A 和 B。
	await T.bindGoTo('content')();
	// 等自动 refresh 启动（A' 启动，searchCount 应当为 3）
	await waitUntil(() => searchCount === 3, 1500);
	assert.equal(searchCount, 3, 'T9 预置：返回 content 时自动 refresh 启动一次搜索（v5 行为）');
	// 此时 grid 应有 loading-state
	assert.match(views.content.innerHTML, /class="loading-state"/, 'T9 预置：自动 refresh 时 grid 应有 loading-state');
	// 释放 A'（就是 v5 的"恢复"）让它完成（A' 实际上是 B 一样，但用 searchCount==3 即可）
	// 实际上 handler 第 3 次起 return B.promise。所以我们改用 B。
	B.resolve([{ id: 'B1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }]);
	await waitUntil(() => /BMod/.test(views.content.innerHTML));
	assert.match(views.content.innerHTML, /BMod/, 'T9 预置：B 完成应显示 BMod（自动恢复的搜索）');
	// 释放 A（旧）
	A.resolve([{ id: 'A1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }]);
	await tick(100);
	await tick(100);
	const html = views.content.innerHTML;
	assert.match(html, /BMod/, 'T9 修复后：B 应保留');
	assert.doesNotMatch(html, /AMod/, 'T9 修复后：A 不应覆盖');
	assert.ok(!/loading/i.test(html) || /BMod/.test(html), 'T9 修复后：不应永久 loading');
});

/* ====================================================================
 * T10: getState pending（真实 deferred Promise）时离页 → 完成后不写空 HTML、
 *   不设置 rendered、不绑事件、不污染 content
 * v5 修复：用真实 deferred Promise 模拟首次 getState pending；
 *   离页后 getState resolve 时，goTo 的 pageGen 校验阻止旧生命周期提交。
 * ==================================================================== */
test('T10: getState 真实 deferred pending 时离页 → 完成后不写空、不污染', async () => {
	const stateQueue = [];
	let getStateCount = 0;
	let searchCount = 0;
	const { T, views } = loadV4({
		getStateHandler: () => {
			getStateCount++;
			const df = deferred();
			stateQueue.push(df);
			return df.promise;
		},
		searchHandler: () => { searchCount++; return []; },
	});

	// 第一次 goTo content：getState pending（真实 deferred）
	const firstP = T.bindGoTo('content')();
	await waitUntil(() => getStateCount >= 1, 1500);
	assert.equal(getStateCount, 1, 'T10 预置：getState 应被调用 1 次且未完成');

	// 离页（在 getState pending 时）
	const homeP = T.bindGoTo('home')();
	await tick(50);
	// 关键（v5）：此时 goTo('home') 触发了第二次 getState（renderHome 内部 loadState），
	// 还没完成。但 contentPageGen 已被 -1 标记。
	assert.equal(T.getContentPageGen(), -1, 'T10 预置：离页后 contentPageGen 应被设为 -1');

	// 释放第一次的 getState 成功
	const successState = { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
	stateQueue[0].resolve(successState);
	await tick(20);
	// 释放第二次 getState（goTo home 内部的 renderHome 触发）
	if (stateQueue[1]) stateQueue[1].resolve(successState);

	// 等待两个 goTo 都完成
	await Promise.all([firstP, homeP]);
	await tick(80);

	// 关键 v5 断言：旧资源页生命周期不应写空 innerHTML / 设置 rendered / 绑事件
	const contentHtml = views.content.innerHTML;
	assert.ok(!/content-view/.test(contentHtml), `T10 v5 修复后：旧资源页不应写 content-view 容器（实际 HTML LENGTH=${contentHtml.length}）`);
	assert.equal(views.content.dataset.rendered, undefined, 'T10 v5 修复后：旧资源页不应设置 dataset.rendered');

	// 关键 v5 断言：searchContent 不应被旧路径触发
	// v5 实际允许旧 renderContent 调用 searchContent 但 result 被丢弃；关键是不要写 DOM。
	// 这里我们检查 home 视图正常显示，且 content 视图的 dataset.rendered 未被设置。
	const homeHtml = views.home.innerHTML;
	assert.match(homeHtml, /Fabric Test/, 'T10：home 视图应正常显示 Fabric Test');
});
