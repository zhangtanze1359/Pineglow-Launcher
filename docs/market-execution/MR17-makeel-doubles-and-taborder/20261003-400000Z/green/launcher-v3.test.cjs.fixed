'use strict';
/*
 * Phase 1 返修 v3：导航竞态 / 重试残留错误 / 实例状态失败恢复
 * --------------------------------------------------------------------------
 * 每个测试必须复现"上一轮产品代码里的具体缺陷"，失败断言前先在 vm 中跑 launcher.js
 * 的实际函数，验证：
 *   1. 真实 goTo 路径
 *   2. 真实事件（点击重试按钮、点击来源按钮、点击安装按钮）
 *   3. 真实 DOM（innerHTML 重置、capture/bubble、isConnected）
 *
 * 测试运行器用 node:test + node:assert/strict。node:test 等待所有 promise 完成后
 * 退出，任何 assertion 失败 → 进程非零。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

/* ============== 复用 launcher-behavior.test.cjs 的 DOM 桩与加载器 ============== */
// 关键：DOM 桩要支持多个 view-* 元素 + 真实 innerHTML 重置
// 为避免依赖复杂的跨文件 require，这里重新构建最小但更严格的版本

function makeDomStub() {
	const elementsById = new Map();
	const allElements = [];
	const querySelectorAllIn = (root, sel) => {
		const out = [];
		function walk(n) {
			if (matches(n, sel)) out.push(n);
			for (const c of n.children) walk(c);
		}
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
			id: '',
			className: '',
			_connected: false,
			classList: {
				_add: new Set(), _remove: new Set(),
				add(...c) { c.forEach(x => { this._add.add(x); this._remove.delete(x); if (x && !el.className.split(/\s+/).includes(x)) el.className = (el.className + ' ' + x).trim(); }); },
				remove(...c) { c.forEach(x => { this._remove.add(x); this._add.delete(x); el.className = el.className.split(/\s+/).filter(n => n && n !== x).join(' '); }); },
				contains(c) { return el.className.split(/\s+/).includes(c); },
				toggle(c, force) { if (force === true) { this._add.add(c); this._remove.delete(c); el.className = (el.className + ' ' + c).trim(); } else if (force === false) { this._remove.add(c); this._add.delete(c); el.className = el.className.split(/\s+/).filter(n => n && n !== c).join(' '); } else { this.contains(c) ? this.remove(c) : this.add(c); } },
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
			_dataset: {},
			setAttribute(k, v) {
				this.attributes[k] = v;
				if (k === 'id') this.id = v;
				if (k === 'class') this.className = v;
				if (k.startsWith('data-')) this._dataset[k.slice(5)] = v;
			},
			getAttribute(k) { return this.attributes[k]; },
			removeAttribute(k) { delete this.attributes[k]; },
			focus() { this._focused = true; }, // MR17/A13：产品代码 4 处调用 focus
			offsetParent: null, // MR17/A13：与 ai-diagnose 替身同口径
			addEventListener(type, fn, capture) { (listeners[type] = listeners[type] || []).push({ fn, capture: !!capture }); },
			removeEventListener(type, fn) {
				const arr = listeners[type] || [];
				const i = arr.findIndex(e => e.fn === fn);
				if (i >= 0) arr.splice(i, 1);
			},
			dispatchEvent(type, eventObj) {
				eventObj = eventObj || { type, target: this, preventDefault() {}, stopPropagation() {}, stopImmediatePropagation() {}, defaultPrevented: false };
				eventObj.target = this;
				const path = [];
				let cur = this;
				while (cur) { path.unshift(cur); cur = cur.parentNode; }
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
		Object.defineProperty(el, 'isConnected', { get() { return el._connected === true; }, configurable: true });
		// 关键：getter 实时序列化当前子节点，而不是返回缓存字符串。
		// 真实浏览器中，el.innerHTML 返回当前 DOM 的序列化；
		// 设置子节点 el.innerHTML = '...' 后，父节点的 innerHTML 也应该反映新内容。
		// 之前的实现用 _innerHTML 缓存字符串，导致子节点更新后父节点的 innerHTML 不变。
		const VOID_FOR_SERIALIZE = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr']);
		function serializeNode(node) {
			if (!node) return '';
			// 文本节点：直接输出文本内容（真实浏览器不包裹 <#text>）
			const tn = (node.tagName || '').toLowerCase();
			if (tn === '#text') return String(node.textContent || '');
			if (tn === '#fragment') return (node.children || []).map(serializeNode).join('');
			const tag = tn || 'div';
			let attrs = '';
			if (node.attributes) {
				for (const k of Object.keys(node.attributes)) {
					const v = String(node.attributes[k]).replace(/&/g, '&amp;').replace(/"/g, '&quot;');
					attrs += ' ' + k + '="' + v + '"';
				}
			}
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
		// 关键（v5）：标准 DOM 规范要求 firstElementChild / firstChild / childNodes。
		// launcher.js 的 retryLoadStateAndRefresh 改用 div.firstElementChild 提取新 badge。
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
		allElements.push(el);
		return el;
	}

	function matchOne(el, token) {
		if (!token) return false;
		let rest = token;
		const tagMatch = rest.match(/^([a-zA-Z][a-zA-Z0-9]*)/);
		if (tagMatch) {
			if (!el.tagName || el.tagName.toLowerCase() !== tagMatch[1].toLowerCase()) return false;
			rest = rest.slice(tagMatch[1].length);
		}
		while (rest.length) {
			if (rest[0] === '.') {
				const m = rest.match(/^\.([a-zA-Z_][\w-]*)/);
				if (!m) return false;
				if (!el.classList || !el.classList.contains(m[1])) return false;
				rest = rest.slice(m[0].length);
			} else if (rest[0] === '#') {
				const m = rest.match(/^#([a-zA-Z_][\w-]*)/);
				if (!m) return false;
				if (el.id !== m[1]) return false;
				rest = rest.slice(m[0].length);
			} else if (rest[0] === '[') {
				const m = rest.match(/^\[([^=\]]+)(?:=([^\]]+))?\]/);
				if (!m) return false;
				const k = m[1], v = m[2];
				const actual = el.attributes[k];
				if (v == null) { if (actual === undefined) return false; }
				else { const cleanV = v.replace(/^['"]|['"]$/g, ''); if (actual !== cleanV) return false; }
				rest = rest.slice(m[0].length);
			} else { return false; }
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
			if (text !== undefined) {
				const t = document.createTextNode(text);
				stack[stack.length - 1].appendChild(t);
				return '';
			}
			if (match.startsWith('<!--')) return '';
			const isClose = match.startsWith('</');
			if (isClose) { if (stack.length > 1) stack.pop(); return ''; }
			const isSelfClose = match.endsWith('/>') || VOID_TAGS.has(tagName.toLowerCase());
			const el = document.createElement(tagName);
			if (attrs) {
				attrRe.lastIndex = 0;
				let m;
				while ((m = attrRe.exec(attrs)) !== null) {
					const k = m[1], v = m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : ''));
					el.setAttribute(k, v);
				}
			}
			stack[stack.length - 1].appendChild(el);
			if (!isSelfClose) stack.push(el);
			return '';
		});
	}

	const document = {
		readyState: 'loading',
		hidden: false,
		documentElement: makeEl('html'),
		head: makeEl('head'),
		body: makeEl('body'),
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

/* ============== 加载 launcher.js（阻止 init 自动跑） ============== */
function loadLauncher(opts) {
	opts = opts || {};
	const { document, elementsById, makeEl, parseInto } = makeDomStub();
	// 预创建 view-* 元素供 goTo 查找
	const viewNames = ['home', 'installations', 'worlds', 'servers', 'content', 'news', 'skins', 'downloads', 'settings'];
	const views = {};
	for (const n of viewNames) {
		const v = makeEl('div');
		v.ownerDocument = document;
		v.id = 'view-' + n;
		v.className = 'view';
		document.body.appendChild(v);
		views[n] = v;
	}
	// contentPanel for goTo scrollTop
	const panel = makeEl('div');
	panel.ownerDocument = document;
	panel.id = 'contentPanel';
	panel.scrollTop = 0;
	document.body.appendChild(panel);
	// nav items
	for (const n of viewNames) {
		const b = makeEl('button');
		b.dataset.nav = n;
		document.body.appendChild(b);
	}

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
	// 注入 api 桩
	ctx.SG.api = makeApiStub(opts);
	vm.runInContext(LAUNCHER_JS, ctx);
	if (!ctx.SG.__test__) throw new Error('SG.__test__ 未注入');
	return { ctx, document, T: ctx.SG.__test__, api: ctx.SG.api, views };
}

function makeApiStub(opts) {
	opts = opts || {};
	const api = {
		_implMode: 'live',
		_allowMockFallback: false,
		_calls: [],
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

/* ============== 测试：v3 - 1. 导航竞态清空页面 ============== */
test('问题 1：A 慢、离开、返回、B 成功、A 后成功 → B 仍显示，页面不为空', async () => {
	let resolveA, resolveB;
	const promiseA = new Promise(r => { resolveA = r; });
	const promiseB = new Promise(r => { resolveB = r; });
	let callIdx = 0;
	const { T, document, views } = loadLauncher({
		searchHandler: (q, c) => { callIdx++; return callIdx === 1 ? promiseA : promiseB; },
	});
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const go1 = T.bindGoTo('content');
	const p1 = go1();
	const goHome = T.bindGoTo('home');
	const p2 = goHome();
	const go3 = T.bindGoTo('content');
	const p3 = go3();
	// B 先成功
	resolveB([{ id: 'B1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }]);
	// A 后成功
	resolveA([{ id: 'A1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }]);
	await Promise.all([p1, p2, p3]);
	const viewContent = views.content;
	const html = viewContent.innerHTML;
	assert.match(html, /BMod/, '当前页应保留 B 的结果, actual=' + html.slice(0, 200));
	assert.doesNotMatch(html, /AMod/, 'A 的旧结果不应写入当前页, actual=' + html.slice(0, 200));
});

/* ============== 测试：v3 - 2. 搜索重试残留错误 ============== */
test('问题 2：首次搜索失败 → 点击实际重试按钮 → 成功：整页无旧错误、无旧重试按钮', async () => {
	let firstCall = true;
	const { T, document, views, api } = loadLauncher({
		searchHandler: () => {
			if (firstCall) { firstCall = false; return Promise.reject(new Error('HTTP 500')); }
			return Promise.resolve([{ id: 'OK1', title: 'OKMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: 'Fabric', category: 'mod', source: 'modrinth' }]);
		},
	});
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	await T.bindGoTo('content')();
	let viewContent = views.content;
	let html = viewContent.innerHTML;
	assert.match(html, /HTTP 500/, '首次失败应显示错误');
	assert.match(html, /data-retry-btn="1"/, '首次失败应显示重试按钮');
	const retryBtn = viewContent.querySelector('[data-retry-btn="1"]');
	assert.ok(retryBtn, '重试按钮必须存在');
	retryBtn.dispatchEvent('click', { type: 'click', target: retryBtn, preventDefault() {}, stopPropagation() {} });
	await new Promise(r => setTimeout(r, 100));
	await new Promise(r => setTimeout(r, 100));
	viewContent = views.content;
	html = viewContent.innerHTML;
	assert.match(html, /OKMod/, '重试成功后应显示 OKMod');
	assert.doesNotMatch(html, /HTTP 500/, '重试成功后旧错误必须消失');
	assert.equal(viewContent.querySelectorAll('[data-retry-btn="1"]').length, 0, '重试成功后旧重试按钮必须消失');
});

/* ============== 测试：v3 - 3. 实例状态失败后无法恢复过滤 ============== */
test('问题 3：第一次 getState 失败 → 点击实际重试入口 → 服务恢复后过滤条件正确', async () => {
	let firstGetState = true;
	let searchOptionsAfterRetry = null;
	const { T, document, views, api } = loadLauncher({
		getStateHandler: () => {
			if (firstGetState) {
				firstGetState = false;
				throw new Error('network down');
			}
			return { config: { selectedInstanceId: 'inst-Fabric' }, instances: [{ id: 'inst-Fabric', name: 'Fabric Test', version: '1.21.1', loader: 'Fabric', modsCount: 5, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] };
		},
		searchHandler: (q, cat, opts) => {
			// 记录重试后的搜索参数
			searchOptionsAfterRetry = opts;
			return [];
		},
	});
	await T.bindGoTo('content')();
	let viewContent = views.content;
	let html = viewContent.innerHTML;
	assert.match(html, /stateLoadFailed|实例状态加载失败/, '首次失败应显示状态加载失败提示');
	assert.doesNotMatch(html, /Fabric Test|Fabric/, '首次失败时不应使用未恢复的实例');
	const stateRetryBtn = viewContent.querySelector('[data-state-retry-btn="1"]');
	assert.ok(stateRetryBtn, '必须有"重试实例状态"按钮, actual HTML=' + html.slice(0, 300));
	stateRetryBtn.dispatchEvent('click', { type: 'click', target: stateRetryBtn, preventDefault() {}, stopPropagation() {} });
	await new Promise(r => setTimeout(r, 100));
	await new Promise(r => setTimeout(r, 100));
	viewContent = views.content;
	html = viewContent.innerHTML;
	const getStateCalls = api._calls.filter(c => c.method === 'getState');
	assert.ok(getStateCalls.length >= 2, 'getState 必须被再次调用, 实际调用 ' + getStateCalls.length);
	assert.ok(searchOptionsAfterRetry && searchOptionsAfterRetry.gameVersion === '1.21.1', '重试成功后搜索应携带 gameVersion=1.21.1, 实际=' + JSON.stringify(searchOptionsAfterRetry));
	assert.match(html, /Fabric Test|Fabric/, '页面应显示恢复后的实例信息');
	assert.doesNotMatch(html, /stateLoadFailed|实例状态加载失败/, '恢复后失败提示应消失');
});
