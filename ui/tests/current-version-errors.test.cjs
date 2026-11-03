'use strict';
/*
 * CV-04 / CV-05 / CV-06 聚焦回归：下载中心失败态、新闻验证时间、内容中心错误块
 * ==========================================================================
 * 这是**单文件聚焦**回归（不是全项目测试架构）：只覆盖本轮三个前端缺陷，
 * 夹具沿用仓库既有的 vm + 受控 DOM 桩（与 launcher-behavior.test.cjs /
 * news-behavior.test.cjs 同源），不引入 jsdom 或新框架。
 *
 * 全部断言走**生产入口**（launcher.js 的 renderDownloads / bindDownloads /
 * loadNews / renderNews / renderContent / bindContent 与 SG.__test__ 暴露的
 * 既有钩子），测试里不复制产品逻辑，只驱动与观察。
 *
 * 对应原始 RED：
 *   - docs/current-version-audit-2026-10-13/independent-ui-red.json
 *       CV-04 failedAsEmpty=true（请求失败被当成成功空列表）
 *       CV-05 actual="1-01-01"（Go 零值 time 被渲染成日期）
 *   - docs/current-version-audit-2026-10-13/baseline-ui-shots/content.png
 *       （内容失败只渲染原始错误串，没有可读主说明/技术详情/可用重试入口）
 *
 * 覆盖的回归：
 *   CV-04 1. 版本源抛错 → 错误块 + 真实重试，而不是"暂无可下载版本"
 *         2. 成功返回 [] 才是空态
 *         3. 失败 → 重试仍失败 → 重试成功（错误块消失，列表恢复）
 *         4. 版本源失败时已有下载任务仍保留
 *         5. 任务接口失败不假装"暂无下载任务"
 *         6. 重试进行中按钮禁用，重复点击不产生并发
 *         7. 恢复后移除错误并保留版本筛选
 *         8. 迟到的旧失败响应不覆盖较新的成功状态
 *         9. 页面已切走时旧请求结束不写 DOM
 *   CV-05 10. null/空/无效/0001-01-01（含时区）一律"尚未验证"，不显示日期
 *         11. 有效验证时间（live / cached）保持显示
 *         12. 失败 / 缓存 / 成功空态仍互相区分
 *         13. 无效的新闻条目标题日期不把原串写进日期字段
 *   CV-06 14. 主说明通俗，技术错误进原生 details/pre 且默认折叠、已转义
 *         15. 失败→重试仍失败→成功，且重试保留来源/类别/查询/游戏版本筛选
 *         16. 新增中英文案齐全（无 raw key 漏出）
 *         17. 状态组件样式存在（长文本换行、焦点可见、窄窗口规则）
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');
const PINEGLOW_CSS = fs.readFileSync(path.join(ROOT, 'ui', 'css', 'pineglow.css'), 'utf8');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');

function realApi(local, remoteFailure, localFailure) {
	const ctx = { SG: {}, location: { protocol: 'http:' }, document: { querySelector() { return null; } }, setTimeout, URL, URLSearchParams,
		fetch: async url => ({ ok: url === '/api/versions' ? !localFailure : !remoteFailure,
			status: 500, json: async () => url === '/api/versions' ? (localFailure ? { error: 'local-list-failed' } : { local }) : (remoteFailure ? { error: 'upstream-unavailable' } : { versions: [] }) }) };
	ctx.window = ctx; vm.createContext(ctx); vm.runInContext(API_JS, ctx);
	return ctx.SG.api;
}

test('CV-04: actual API preserves legacy local fallback and strict error with installed versions', async () => {
	const api = realApi(['1.20.1'], true);
	assert.equal((await api.getRemoteVersions())[0].installed, true);
	await assert.rejects(api.getRemoteVersions({ throwOnRemoteError: true }), e => e.message.includes('upstream-unavailable') && e.localVersions[0].id === '1.20.1');
	await assert.rejects(realApi([], true).getRemoteVersions({ throwOnRemoteError: true }), e => e.localVersions.length === 0);
	await assert.rejects(realApi([], false, true).getRemoteVersions(), /local-list-failed/);
	assert.equal((await realApi([], false).getRemoteVersions({ throwOnRemoteError: true })).length, 0);
});

test('CV-04: downloads uses actual strict API and retains installed versions below failure card', async () => {
	const h = loadLauncher();
	h.ctx.SG.api = realApi(['1.20.1'], true);
	h.ctx.SG.api.getDownloads = async () => [];
	const html = await h.T.renderDownloads();
	assert.match(html, /class="download-source-error"/);
	assert.match(html, /1\.20\.1/);
	assert.doesNotMatch(html, /data-empty="source"/);
});

// Execute the exact production launch block, with modal DOM and API responses
// controlled. No duplicate launch/check business logic in this fixture.
function launchHarness(check) {
	const { document } = makeDomStub();
	const host = document.createElement('div'); document.body.appendChild(host);
	const calls = { checks: 0, launches: 0 };
	const ctx = { document, STATE: { config: {} }, NATIVE_SHELL: false,
		SG: { api: { async prelaunchCheck() { calls.checks++; return check(calls.checks); }, async launchInstance() { calls.launches++; return { ok: true }; } } },
		t: k => k, esc: s => String(s), toast() {}, openModal(html) { host.innerHTML = html; }, closeModal() {}, console };
	vm.createContext(ctx);
	const start = LAUNCHER_JS.indexOf('let launchInFlight = false;');
	const end = LAUNCHER_JS.indexOf('/* ---------------- Play 页', start);
	assert(start > 0 && end > start);
	vm.runInContext(LAUNCHER_JS.slice(start, end), ctx);
	return { ctx, host, calls, document };
}

test('CV-08: thrown/null/malformed checks fail closed; failed retry never launches', async () => {
	for (const result of [null, {}, { ok: 'true' }]) {
		const h = launchHarness(() => result); await h.ctx.doLaunch('i1');
		assert.equal(h.calls.launches, 0); assert(h.document.getElementById('pcRetry'));
		assert.equal(h.document.getElementById('pcOk'), null);
	}
	const h = launchHarness(() => { throw new Error('offline'); });
	await h.ctx.doLaunch('i1'); await h.document.getElementById('pcRetry').onclick();
	assert.equal(h.calls.checks, 2); assert.equal(h.calls.launches, 0);
});

test('CV-08: successful retry launches once; repeated pending clicks do not duplicate checks', async () => {
	let complete; const pending = new Promise(r => { complete = r; });
	const h = launchHarness(n => n === 1 ? null : pending);
	await h.ctx.doLaunch('i1');
	const retry = h.document.getElementById('pcRetry').onclick();
	const repeated = h.ctx.doLaunch('i1');
	assert.equal(h.calls.checks, 2); assert.equal(h.calls.launches, 0);
	complete({ ok: true, warnings: [] }); await Promise.all([retry, repeated]);
	assert.equal(h.calls.launches, 1);
});

test('CV-08: explicit errors block; warnings require confirmation and confirm double-click launches once', async () => {
	const blocked = launchHarness(() => ({ ok: false, errors: ['missing game files'] }));
	await blocked.ctx.doLaunch('i1'); assert.equal(blocked.calls.launches, 0);
	const h = launchHarness(() => ({ ok: true, warnings: ['memory warning'] }));
	await h.ctx.doLaunch('i1'); assert.equal(h.calls.launches, 0);
	const confirm = h.document.getElementById('pcOk').onclick;
	await Promise.all([confirm(), confirm()]); assert.equal(h.calls.launches, 1);
});

/* ============================================================================
 * 受控 DOM 桩
 * - 事件传播：capture 根→target，冒泡 target→根（bindContent 的委托监听取自捕获阶段）
 * - dataset：kebab-case 双向映射（data-news-art-id <-> dataset.newsArtId）
 * - innerHTML：赋值即重新解析并替换子节点（真实 DOM 语义）
 * ========================================================================== */
function makeDomStub() {
	const elementsById = new Map();
	const kebabAttr = (k) => String(k).replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();

	function makeEl(tag) {
		const listeners = {};
		const el = {
			ownerDocument: null,
			tagName: (tag || 'div').toUpperCase(),
			id: '',
			className: '',
			_connected: false,
			classList: {
				_add: new Set(), _remove: new Set(),
				add(...c) { c.forEach(x => { this._add.add(x); this._remove.delete(x); if (x && !el.className.split(/\s+/).includes(x)) el.className = (el.className + ' ' + x).trim(); }); },
				remove(...c) { c.forEach(x => { this._remove.add(x); this._add.delete(x); el.className = el.className.split(/\s+/).filter(n => n && n !== x).join(' '); }); },
				contains(c) { return el.className.split(/\s+/).includes(c); },
				toggle(c, force) {
					if (force === true) this.add(c);
					else if (force === false) this.remove(c);
					else if (this.contains(c)) this.remove(c);
					else this.add(c);
				},
			},
			attributes: {},
			dataset: {},
			style: {},
			children: [],
			parent: null,
			hidden: false,
			disabled: false,
			value: '',
			textContent: '',
			_listeners: listeners,
			setAttribute(k, v) { this.attributes[k] = v; if (k === 'id') this.id = v; if (k === 'class') this.className = v; },
			getAttribute(k) { return this.attributes[k]; },
			removeAttribute(k) { delete this.attributes[k]; },
			focus() { this._focused = true; },
			offsetParent: null,
			addEventListener(type, fn, capture) { (listeners[type] = listeners[type] || []).push({ fn, capture: !!capture }); },
			removeEventListener(type, fn) { const arr = listeners[type] || []; const i = arr.findIndex(e => e.fn === fn); if (i >= 0) arr.splice(i, 1); },
			dispatchEvent(type, eventObj) {
				eventObj = eventObj || {};
				if (typeof eventObj.preventDefault !== 'function') eventObj.preventDefault = () => { };
				if (typeof eventObj.stopPropagation !== 'function') eventObj.stopPropagation = () => { eventObj._stopped = true; };
				eventObj.type = type;
				eventObj.target = this;
				eventObj.currentTarget = this;
				const path = [];
				let cur = this;
				while (cur) { path.unshift(cur); cur = cur.parent; }
				for (const node of path) {
					for (const { fn } of (node._listeners[type] || []).filter(x => x.capture)) {
						eventObj.currentTarget = node;
						fn.call(node, eventObj);
						if (eventObj._stoppedImmediate) break;
					}
					if (eventObj._stoppedImmediate) break;
				}
				if (!eventObj._stoppedImmediate) {
					for (const node of [...path].reverse()) {
						for (const { fn } of (node._listeners[type] || []).filter(x => !x.capture)) {
							eventObj.currentTarget = node;
							fn.call(node, eventObj);
							if (eventObj._stoppedImmediate) break;
						}
						if (eventObj._stoppedImmediate || eventObj._stopped) break;
					}
				}
			},
			appendChild(child) { child.parent = this; this.children.push(child); if (child.id) elementsById.set(child.id, child); child._connected = true; return child; },
			insertBefore(child, ref) {
				child.parent = this;
				const i = ref ? this.children.indexOf(ref) : this.children.length;
				this.children.splice(i < 0 ? this.children.length : i, 0, child);
				if (child.id) elementsById.set(child.id, child);
				child._connected = true;
				return child;
			},
			remove() {
				if (this.parent) { const i = this.parent.children.indexOf(this); if (i >= 0) this.parent.children.splice(i, 1); }
				if (this.id) elementsById.delete(this.id);
				this._connected = false;
			},
			querySelector(sel) { return querySelectorIn(this, sel); },
			querySelectorAll(sel) { return querySelectorAllIn(this, sel); },
			matches(sel) { return matches(this, sel); },
			closest(sel) { let n = this; while (n) { if (matches(n, sel)) return n; n = n.parent; } return null; },
			getBoundingClientRect() { return { top: 0, left: 0, width: 100, height: 30 }; },
			cloneNode() { return makeEl(tag); },
		};
		el.dataset = new Proxy({}, {
			set(t, k, v) { t[k] = v; el.attributes['data-' + kebabAttr(k)] = v; return true; },
			get(t, k) { if (k in t) return t[k]; return el.attributes['data-' + kebabAttr(k)]; },
			has(t, k) { return k in t || ('data-' + kebabAttr(k)) in el.attributes; },
			deleteProperty(t, k) { delete t[k]; delete el.attributes['data-' + kebabAttr(k)]; return true; },
		});
		Object.defineProperty(el, 'isConnected', { get() { return el._connected === true; }, configurable: true });
		let _innerHTML = '';
		Object.defineProperty(el, 'innerHTML', {
			get() { return _innerHTML; },
			set(v) {
				_innerHTML = String(v == null ? '' : v);
				const disconnect = (node) => { node._connected = false; for (const c of (node.children || [])) disconnect(c); };
				for (const c of el.children) { c.parent = null; disconnect(c); }
				el.children = [];
				if (_innerHTML !== '' && el.ownerDocument) parseInto(el, _innerHTML);
			},
			configurable: true,
		});
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
				if (!m || !el.classList || !el.classList.contains(m[1])) return false;
				rest = rest.slice(m[0].length);
			} else if (rest[0] === '#') {
				const m = rest.match(/^#([a-zA-Z_][\w-]*)/);
				if (!m || el.id !== m[1]) return false;
				rest = rest.slice(m[0].length);
			} else if (rest[0] === '[') {
				const m = rest.match(/^\[([^=\]]+)(?:=([^\]]+))?\]/);
				if (!m) return false;
				const actual = el.attributes[m[1]];
				if (m[2] == null) { if (actual === undefined) return false; }
				else if (actual !== m[2].replace(/^['"]|['"]$/g, '')) return false;
				rest = rest.slice(m[0].length);
			} else return false;
		}
		return true;
	}
	function matches(el, sel) {
		const tokens = String(sel).trim().split(/\s+/);
		if (!matchOne(el, tokens[tokens.length - 1])) return false;
		for (let i = tokens.length - 2; i >= 0; i--) {
			let p = el.parent, ok = false;
			while (p) { if (matchOne(p, tokens[i])) { ok = true; break; } p = p.parent; }
			if (!ok) return false;
		}
		return true;
	}
	function collectAll(root, out) { for (const c of root.children) { out.push(c); collectAll(c, out); } }
	function querySelectorIn(root, sel) { const all = []; collectAll(root, all); return all.find(el => matches(el, sel)) || null; }
	function querySelectorAllIn(root, sel) { const all = []; collectAll(root, all); return all.filter(el => matches(el, sel)); }

	const document = {
		readyState: 'loading',   // 与 launcher-behavior 一致：DOMContentLoaded 被过滤，init() 不自动跑
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
		addEventListener(type, fn, capture) {
			if (type === 'DOMContentLoaded') return;
			(document.body._listeners[type] = document.body._listeners[type] || []).push({ fn, capture: !!capture });
		},
		removeEventListener() { },
		dispatchEvent(type, obj) { document.body.dispatchEvent(type, obj); },
	};
	return { document, elementsById, makeEl };
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
		if (match.startsWith('</')) { if (stack.length > 1) stack.pop(); return ''; }
		const isSelfClose = match.endsWith('/>') || VOID_TAGS.has(tagName.toLowerCase());
		const el = document.createElement(tagName);
		if (attrs) {
			let m;
			attrRe.lastIndex = 0;
			while ((m = attrRe.exec(attrs)) !== null) {
				el.setAttribute(m[1], m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : '')));
			}
		}
		stack[stack.length - 1].appendChild(el);
		if (!isSelfClose) stack.push(el);
		return '';
	});
}

/* ============================================================================
 * 夹具：真实 i18n + 真实 launcher.js + 受控 SG.api
 * handlers 里抛错即代表"请求失败"（async 包装后成为 rejected promise）。
 * handler 返回 Promise 时按 Promise 采用，用于控制完成顺序（竞态用例）。
 * ========================================================================== */
function loadLauncher(handlers) {
	handlers = handlers || {};
	const { document, elementsById, makeEl } = makeDomStub();

	// 生产视图容器：goTo 需要 #contentPanel 与 .view 容器；重试写 DOM 需要 #view-downloads
	const mkView = (id) => { const e = document.createElement('div'); e.className = 'view'; e.id = id; document.body.appendChild(e); return e; };
	const panel = document.createElement('div'); panel.className = 'content-panel'; panel.id = 'contentPanel'; document.body.appendChild(panel);
	const viewHome = mkView('view-home');
	const viewDownloads = mkView('view-downloads');
	const viewContent = mkView('view-content');
	const viewNews = mkView('view-news');
	for (const nav of ['home', 'content', 'downloads', 'news']) {
		const b = document.createElement('button'); b.className = 'nav-item'; b.dataset.nav = nav; document.body.appendChild(b);
	}

	const ctx = {
		SG: {},
		location: { protocol: 'http:', hash: '' },
		history: { replaceState() { }, pushState() { } },
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN', platform: 'Win32' },
		localStorage: { getItem() { return null; }, setItem() { }, removeItem() { } },
		document,
		setTimeout, clearTimeout, setInterval, clearInterval,
		Promise, URLSearchParams, console, Date, JSON, Math, Object, Array, String, Number, Boolean, Error, Symbol,
		Image: function () { return { addEventListener() { }, src: '' }; },
		Blob: function () { },
		FileReader: function () { this.readAsDataURL = () => { }; this.onload = null; this.onerror = null; },
		URL: Object.assign(globalThis.URL, { createObjectURL: () => 'blob:fake', revokeObjectURL() { } }),
		window: null,
		fetch: () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) }),
	};
	ctx.window = ctx;

	const api = {
		calls: { versions: 0, tasks: 0, content: [], news: 0, queued: [] },
		async getState() {
			return { config: { selectedInstanceId: 'i1' }, instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], accounts: [], java: [] };
		},
		async getAccounts() { return { accounts: [], capes: [] }; },
		async getStats() { return { totalPlaySeconds: 0, totalPlayCount: 0 }; },
		async getDownloads() {
			api.calls.tasks++;
			return handlers.tasks ? await handlers.tasks(api.calls.tasks) : [];
		},
		async getRemoteVersions() {
			api.calls.versions++;
			return handlers.versions ? await handlers.versions(api.calls.versions) : [];
		},
		async downloadVersion(id) { api.calls.queued.push(id); return { ok: true }; },
		async searchContent(query, category, options) {
			api.calls.content.push({ query, category, options });
			return handlers.content ? await handlers.content(api.calls.content.length) : [];
		},
		async getNews() {
			api.calls.news++;
			return handlers.news ? await handlers.news(api.calls.news) : { items: [], meta: { state: 'empty', validatedAt: null } };
		},
		async getNewsImage(id) { return 'blob:fake-' + id; },
	};
	ctx.SG.api = api;

	vm.createContext(ctx);
	vm.runInContext(I18N_JS, ctx);                        // 真实 i18n（断言真实中英文案）
	vm.runInContext('SG.apiMode = "live"; SG.apiToken = "test-token";', ctx);
	vm.runInContext(LAUNCHER_JS, ctx);
	const T = ctx.SG.__test__;
	if (!T) throw new Error('SG.__test__ 未注入；launcher.js 加载失败');
	return { ctx, document, T, api, elementsById, makeEl, viewHome, viewDownloads, viewContent, viewNews };
}

const flush = async (turns = 4) => { for (let i = 0; i < turns; i++) await new Promise(r => setTimeout(r, 5)); };
const deferred = () => { let resolve, reject; const p = new Promise((res, rej) => { resolve = res; reject = rej; }); return { p, resolve, reject }; };
const escapeRe = (s) => String(s).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/* ============================================================================
 * CV-04：下载中心 —— 失败 ≠ 成功空态，任务独立
 * ========================================================================== */

test('CV-04: getRemoteVersions 抛错时显示双语错误块与真实重试，而不是"暂无可下载版本"', async () => {
	const { T } = loadLauncher({
		versions: () => { throw new Error('版本源不可用（HTTP 503）'); },
		tasks: () => [],
	});
	const html = await T.renderDownloads();
	assert.match(html, /class="download-source-error"[^>]*data-error="1"/, 'RED：版本请求失败必须渲染错误块');
	assert.match(html, /data-dl-retry="1"/, '失败态必须提供真实重试按钮');
	assert.match(html, /无法获取可下载版本/, '主说明应是可读的中文文案');
	assert.doesNotMatch(html, /暂无可下载版本/, 'RED：请求失败绝不能被当成"成功空列表"');
	assert.doesNotMatch(html, /data-empty="source"/, '失败态不得带成功空态标记');
	assert.match(html, /<details class="tech-details">/, '原始错误应进入可展开的技术详情');
	assert.match(html, /版本源不可用（HTTP 503）/, '原始错误文本应保留在 HTML（转义后置于 pre）');
	assert.equal(T.getDownloadsState().versions.status, 'error', '内部状态必须区分 error 与 ready');
});

test('CV-04: 只有成功返回空数组才显示空态', async () => {
	const { T } = loadLauncher({ versions: () => [], tasks: () => [] });
	const html = await T.renderDownloads();
	assert.match(html, /data-empty="source"/, '成功空数组 → 成功空态标记');
	assert.match(html, /暂无可下载版本/, '成功空数组 → 空态文案');
	assert.doesNotMatch(html, /data-error="1"/, '成功空数组不得出现错误块');
	assert.doesNotMatch(html, /data-dl-retry="1"/, '空态不需要重试按钮（有刷新语义的筛选可用）');
	assert.equal(T.getDownloadsState().versions.status, 'ready');
	assert.equal(T.getDownloadsState().versions.error, null);
});

test('CV-04: 失败 → 重试仍失败 → 重试成功（错误块消失，列表恢复）', async () => {
	const { T, document, viewDownloads } = loadLauncher({
		versions: (n) => {
			if (n === 1) throw new Error('第一次失败 HTTP 503');
			if (n === 2) throw new Error('第二次仍失败 HTTP 503');
			return [{ id: '1.21.4', type: 'release', releaseTime: '2026-08-01' }];
		},
		tasks: () => [],
	});
	await T.goTo('downloads');
	assert.match(viewDownloads.innerHTML, /data-error="1"/, '首次进入即为失败态');
	assert.match(viewDownloads.innerHTML, /第一次失败/, '首次失败的技术详情应落地');

	// 第一次重试：仍然失败，必须还能再重试（失败后重试按钮仍可用）
	let btn = viewDownloads.querySelector('[data-dl-retry]');
	assert.ok(btn, '应有重试按钮');
	btn.dispatchEvent('click', {});
	await flush();
	assert.match(viewDownloads.innerHTML, /data-error="1"/, '重试仍失败时必须继续显示错误块');
	assert.match(viewDownloads.innerHTML, /第二次仍失败/, '更新后的技术详情应替换旧错误');
	btn = viewDownloads.querySelector('[data-dl-retry]');
	assert.ok(btn, '第二次失败后重试按钮仍必须存在');

	// 第二次重试：成功 → 错误块消失，列表恢复
	btn.dispatchEvent('click', {});
	await flush();
	assert.doesNotMatch(viewDownloads.innerHTML, /data-error="1"/, '恢复后必须移除错误块');
	assert.match(viewDownloads.innerHTML, /1\.21\.4/, '恢复后应显示版本列表');
	assert.equal(T.getDownloadsState().versions.status, 'ready');
	assert.equal(T.getDownloadsState().versions.error, null);
	assert.equal(T.getDownloadsState().tasks.status, 'ready', '任务区独立：不应被版本源的失败/恢复影响');
	assert.ok(document.getElementById('view-downloads'), '视图容器仍在');
});

test('CV-04: 版本源失败时已有下载任务仍保留（任务来自独立数据源）', async () => {
	const tasks = [{ name: 'Fabric 1.20.1', statusLabel: '下载中', progress: 42, icon: '🧵' }];
	const { T } = loadLauncher({
		versions: (n) => { if (n === 1) return [{ id: '1.20.1', type: 'release' }]; throw new Error('版本源掉线'); },
		tasks: () => tasks,
	});
	const first = await T.renderDownloads();
	assert.match(first, /Fabric 1\.20\.1/, '第一次成功时任务应显示');

	// 第二次渲染：版本源失败，任务源仍正常
	const second = await T.renderDownloads();
	assert.match(second, /data-error="1"/, '版本源失败应显示错误块');
	assert.match(second, /Fabric 1\.20\.1/, 'RED：版本源失败不得清掉已有下载任务');
	assert.match(second, /42%/, '任务进度应保留');
	assert.doesNotMatch(second, /data-empty="tasks"/, '有任务时不得显示"暂无下载任务"');
	assert.equal(T.getDownloadsState().tasks.status, 'ready');
});

test('CV-04: 下载任务接口失败不假装"暂无下载任务"', async () => {
	const { T } = loadLauncher({
		versions: () => [{ id: '1.20.1', type: 'release' }],
		tasks: () => { throw new Error('任务接口 500'); },
	});
	const html = await T.renderDownloads();
	assert.match(html, /data-tasks-error="1"/, '任务接口失败必须显式报错');
	assert.match(html, /无法获取下载任务/, '任务错误应有人话说明');
	assert.doesNotMatch(html, /暂无下载任务/, 'RED：任务接口失败不得假装没有任务');
	assert.match(html, /任务接口 500/, '任务错误的技术详情应保留');
	assert.match(html, /1\.20\.1/, '任务失败不得影响版本列表');
	assert.equal(T.getDownloadsState().tasks.status, 'error');
});

test('CV-04: 重试进行中按钮禁用，重复点击不产生并发请求', async () => {
	const never = new Promise(() => { });
	const { T, viewDownloads } = loadLauncher({
		versions: (n) => { if (n === 1) throw new Error('首次失败'); return never; },
		tasks: () => [],
	});
	await T.goTo('downloads');
	const btn = viewDownloads.querySelector('[data-dl-retry]');
	assert.ok(btn, '应有重试按钮');
	assert.equal(btn.disabled, false, '初始不应处于禁用态');

	btn.dispatchEvent('click', {});
	assert.equal(btn.disabled, true, '重试进行中按钮必须禁用');
	btn.dispatchEvent('click', {});
	btn.dispatchEvent('click', {});
	await flush();
	assert.equal(btn.disabled, true, '请求未完成前必须保持禁用');
	assert.equal(T.getDownloadsState().versions.status, 'error', '未完成时不得写入新的成功/失败状态');
});

test('CV-04: 并发保护 —— 进行中的重复点击不产生第二次请求', async () => {
	const never = new Promise(() => { });
	const seen = [];
	const { T, viewDownloads } = loadLauncher({
		versions: (n) => { seen.push(n); if (n === 1) throw new Error('首次失败'); return never; },
		tasks: () => [],
	});
	await T.goTo('downloads');
	assert.equal(seen.length, 1, '首屏只应请求一次版本源');
	const btn = viewDownloads.querySelector('[data-dl-retry]');
	btn.dispatchEvent('click', {});
	btn.dispatchEvent('click', {});
	btn.dispatchEvent('click', {});
	await flush();
	assert.equal(seen.length, 2, '进行中的重复点击必须只产生一次新请求，实际 ' + seen.length);
});

test('CV-04: 恢复后移除错误并保留版本筛选', async () => {
	const { T, viewDownloads } = loadLauncher({
		versions: (n) => {
			if (n === 1) return [{ id: '1.20.1', type: 'release' }, { id: '24w14a', type: 'snapshot' }];
			if (n === 2) throw new Error('切换筛选时掉了');
			return [{ id: '1.21.4', type: 'release' }, { id: '24w20a', type: 'snapshot' }];
		},
		tasks: () => [],
	});
	await T.goTo('downloads');
	assert.match(viewDownloads.innerHTML, /1\.20\.1/, '默认 release 筛选应显示正式版');

	// 切到快照筛选（失败前设置的筛选状态必须在重试后保留）
	viewDownloads.querySelector('[data-vfilter="snapshot"]').dispatchEvent('click', {});
	await flush();
	assert.match(viewDownloads.innerHTML, /data-error="1"/, '该次请求失败应显示错误块');
	assert.match(viewDownloads.innerHTML, /class="filter active" data-vfilter="snapshot"/, '错误态下筛选仍应保持为快照');

	viewDownloads.querySelector('[data-dl-retry]').dispatchEvent('click', {});
	await flush();
	assert.doesNotMatch(viewDownloads.innerHTML, /data-error="1"/, '恢复后错误块必须移除');
	assert.match(viewDownloads.innerHTML, /24w20a/, '恢复后应显示快照版本');
	assert.doesNotMatch(viewDownloads.innerHTML, /1\.21\.4/, '恢复后仍应保留快照筛选（正式版被过滤）');
	assert.match(viewDownloads.innerHTML, /class="filter active" data-vfilter="snapshot"/, '恢复后筛选必须保留');
	assert.equal(T.getDownloadsState().versions.status, 'ready');
});

test('CV-04: 迟到的旧失败响应不得覆盖较新的成功状态', async () => {
	const oldVersions = deferred();
	const newVersions = deferred();
	const h = loadLauncher({ tasks: () => [] });
	let calls = 0;
	h.api.getRemoteVersions = () => { calls++; return calls === 1 ? oldVersions.p : newVersions.p; };

	const pA = h.T.renderDownloads();   // 旧请求
	const pB = h.T.renderDownloads();   // 新请求（代际更大）
	newVersions.resolve([{ id: 'NEW-1', type: 'release', releaseTime: '2026-09-01' }]);
	const htmlB = await pB;
	oldVersions.reject(new Error('旧请求晚到的失败'));
	const htmlA = await pA;

	assert.equal(htmlA, '', '过期的旧响应必须短路（不写状态、不返回 HTML）');
	assert.match(htmlB, /NEW-1/, '较新的成功结果应保留');
	assert.equal(h.T.getDownloadsState().versions.status, 'ready', 'RED：旧失败不得把状态改回 error');
	assert.equal(h.T.getDownloadsState().versions.error, null);
});

test('CV-04: 页面已切走时，旧请求结束不写 DOM', async () => {
	const { T, viewDownloads } = loadLauncher({
		versions: (n) => { if (n === 1) throw new Error('首次失败'); return [{ id: '1.21.4', type: 'release' }]; },
		tasks: () => [],
	});
	await T.goTo('downloads');
	const before = viewDownloads.innerHTML;
	assert.match(before, /data-error="1"/);

	await T.goTo('home');
	assert.equal(T.getCurrentView(), 'home', '应已切到首页');
	const btn = viewDownloads.querySelector('[data-dl-retry]');
	btn.dispatchEvent('click', {});
	await flush();
	assert.equal(viewDownloads.innerHTML, before, '页面已切走：旧请求结束后不得改写该页 DOM');
});

/* ============================================================================
 * CV-05：新闻验证时间（meta.validatedAt 契约不变）
 * ========================================================================== */

async function renderNewsWithValidatedAt(validatedAt, state) {
	const h = loadLauncher({
		news: () => ({ items: [], meta: { source: 'launchercontent.mojang.com', state: state || 'error', validatedAt, isStale: true, canRetry: true, errorKind: 'network', error: 'down' } }),
	});
	await h.T.loadNews();
	const html = await h.T.renderNews();
	return { html, h };
}

test('CV-05: null / 空 / 无效 / 0001-01-01（含时区）一律"尚未验证"，且不出现 1-01-01', async () => {
	const bad = [
		null,
		'',
		'   ',
		'not-a-date',
		'0001-01-01',
		'0001-01-01T00:00:00Z',
		'0001-01-01T00:00:00+00:00',
		'0001-01-01T00:00:00+08:00',
		'0001-01-01T00:00:00-08:00',
	];
	for (const v of bad) {
		const { html } = await renderNewsWithValidatedAt(v, 'error');
		const label = JSON.stringify(v);
		assert.match(html, /data-news-verified="0"/, label + ' 应标记为未验证');
		assert.match(html, /尚未验证/, label + ' 应显示"尚未验证"文案');
		assert.doesNotMatch(html, /1-01-01/, label + ' RED：不得显示 1-01-01');
		assert.doesNotMatch(html, /0001-01-01/, label + ' 原始零值串不得进入页面');
		if (typeof v === 'string' && v.trim() && v !== '0001-01-01') {
			assert.doesNotMatch(html, new RegExp(escapeRe(v.trim())), label + ' 无效原串不得被当作日期显示');
		}
	}
});

test('CV-05: live 状态下零值验证时间同样不显示日期', async () => {
	const { html } = await renderNewsWithValidatedAt('0001-01-01T00:00:00Z', 'live');
	assert.match(html, /data-news-state="live"/);
	assert.match(html, /data-news-verified="0"/, '零值验证时间必须标记为"尚未验证"');
	assert.doesNotMatch(html, /1-01-01/);
	assert.match(html, /尚未验证/);
});

test('CV-05: 有效验证时间保持显示（live）', async () => {
	const { html, h } = await renderNewsWithValidatedAt('2026-09-05T12:00:00+00:00', 'live');
	assert.match(html, /data-news-verified="1"/, '有效验证时间应标记为已验证');
	assert.match(html, /2026-09-05/, '有效验证时间应显示日期');
	assert.match(html, /最后验证/, '应保留"最后验证"文案');
	assert.ok(h.T.getNewsMeta().validatedAt === '2026-09-05T12:00:00+00:00', 'meta.validatedAt 契约不变');
});

test('CV-05: cached 状态保留缓存验证时间', async () => {
	const items = [{ id: 'n1', title: '缓存新闻', category: 'Minecraft', date: '2026-08-30T00:00:00Z', text: 't', imageId: null, link: '', linkOk: false }];
	const h = loadLauncher({
		news: () => ({ items, meta: { source: 's', state: 'cached', validatedAt: '2026-09-05T08:00:00+00:00', isStale: true, canRetry: true, errorKind: 'network', error: 'down' } }),
	});
	await h.T.loadNews();
	const html = await h.T.renderNews();
	assert.match(html, /data-news-banner="cached"/, '应有离线缓存横幅');
	assert.match(html, /data-news-verified="1"/, '缓存的验证时间有效');
	assert.match(html, /2026-09-05/, '应保留缓存验证日期');
	assert.match(html, /缓存新闻/, '缓存内容应保留');
});

test('CV-05: cached 状态但验证时间为零值时不显示日期', async () => {
	const items = [{ id: 'n1', title: '缓存新闻', category: 'Minecraft', date: '2026-08-30T00:00:00Z', text: 't', imageId: null, link: '', linkOk: false }];
	const h = loadLauncher({
		news: () => ({ items, meta: { source: 's', state: 'cached', validatedAt: '0001-01-01T00:00:00Z', isStale: true, canRetry: true, errorKind: 'network', error: 'down' } }),
	});
	await h.T.loadNews();
	const html = await h.T.renderNews();
	assert.match(html, /data-news-banner="cached"/, '缓存横幅仍应存在');
	assert.match(html, /data-news-verified="0"/, '零值时间应标记为未验证');
	assert.doesNotMatch(html, /1-01-01/, 'RED：不得显示 1-01-01');
	assert.match(html, /验证时间未知/, '无有效时间时应说明验证时间未知');
});

test('CV-05: 失败 / 缓存 / 成功空态三种终态仍互相区分', async () => {
	const err = await renderNewsWithValidatedAt(null, 'error');
	assert.match(err.html, /data-news-state="error"/, '失败态标记');
	assert.match(err.html, /data-news-retry="1"/, '失败态有重试入口');

	const cached = await renderNewsWithValidatedAt('2026-09-05T08:00:00+00:00', 'cached');
	assert.match(cached.html, /data-news-banner="cached"/, '缓存态标记');
	assert.doesNotMatch(cached.html, /data-news-state="error"/, '缓存态不是失败态');

	const empty = await renderNewsWithValidatedAt(null, 'empty');
	assert.match(empty.html, /data-news-state="empty"/, '成功空态标记');
	assert.doesNotMatch(empty.html, /data-news-state="error"/, '成功空态不是失败态');
	assert.match(empty.html, /data-news-retry="1"/, '空态也有刷新入口');
});

test('CV-05: 新闻条目的无效日期不把原串写进日期字段', async () => {
	const items = [{ id: 'n1', title: '标题', category: 'C', date: 'garbage-date', text: 't', imageId: null, link: '', linkOk: false }];
	const h = loadLauncher({
		news: () => ({ items, meta: { source: 's', state: 'live', validatedAt: '2026-09-05T12:00:00+00:00', isStale: false, canRetry: false, errorKind: null, error: null } }),
	});
	await h.T.loadNews();
	const html = await h.T.renderNews();
	assert.match(html, /标题/, '条目应渲染');
	assert.doesNotMatch(html, /garbage-date/, 'RED：无效原串不得进入日期字段');
	assert.match(html, /2026-09-05/, '有效验证时间不受影响');
});

/* ============================================================================
 * CV-06：内容中心错误块 / 重试（来源、类别、查询、游戏版本筛选必须保留）
 * ========================================================================== */

function contentHarness(handlers) {
	const h = loadLauncher(handlers);
	h.T.setState({ config: {}, instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], accounts: [], java: [] });
	h.T.setSelectedInstanceId('i1');
	return h;
}

async function renderContentIntoView(h) {
	const html = await h.T.renderContent();
	h.viewContent.innerHTML = html;
	h.T.bindContent(h.viewContent);
	return html;
}

test('CV-06: 主说明通俗，技术错误进原生 details/pre，默认折叠且已转义', async () => {
	const raw = '<img src=x onerror=alert(1)> & "quotes"';
	const h = contentHarness({ content: () => { throw new Error(raw); } });
	const html = await renderContentIntoView(h);

	assert.match(html, /class="content-error"[^>]*data-error="1"/, '必须渲染 .content-error 错误块');
	assert.match(html, /class="content-retry-btn"[^>]*data-retry-btn="1"|data-retry-btn="1"[^>]*class="content-retry-btn"/, '必须渲染 .content-retry-btn 与重试钩子');
	assert.match(html, /内容加载失败/, '主说明应是通俗标题');
	assert.match(html, /没能从内容源取回数据/, '主说明应是通俗解释（而不是原始错误串）');
	assert.match(html, /<details class="tech-details">/, '技术错误应进入原生 details');
	assert.doesNotMatch(html, /<details[^>]*\sopen[\s>]/, '技术详情必须默认不铺开（无 open 属性）');
	assert.match(html, /<pre class="tech-body">/, '技术错误应在原生 pre 中');
	assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/, '动态文本必须 HTML 转义');
	assert.match(html, /&quot;quotes&quot;/, '引号也必须转义');
	assert.doesNotMatch(html, /<img\s+src=x/, 'RED：不得把原始错误当成 HTML 注入');
	assert.match(html, /<pre class="tech-body">[^<]*&lt;img/, '转义后的原始错误应落在 pre 内');
});

test('CV-06: 失败 → 重试仍失败 → 成功，且重试保留来源/类别/查询/游戏版本筛选', async () => {
	const card = { id: 'P1', title: 'ShaderPack', desc: 'd', author: 'a', downloads: 10, icon: '', versions: [], loader: 'Fabric', category: 'shader', source: 'curseforge' };
	const h = contentHarness({
		content: (n) => {
			if (n === 1) throw new Error('第一次内容源失败');
			if (n === 2) throw new Error('第二次仍然失败');
			return [card];
		},
	});
	h.T.setContentSource('curseforge');
	h.T.setContentCat('shader');
	h.T.setContentQuery('shaders-x');

	const html1 = await renderContentIntoView(h);
	assert.match(html1, /data-error="1"/, '首次失败应显示错误块');
	assert.equal(h.T.getContentSource(), 'curseforge');
	assert.equal(h.T.getContentCat(), 'shader');
	assert.equal(h.T.getContentQuery(), 'shaders-x');

	// 第一次重试：仍失败 → 错误块与其重试按钮必须仍然可用
	h.viewContent.querySelector('[data-retry-btn]').dispatchEvent('click', {});
	await flush();
	// The lightweight DOM retains the original parent innerHTML string; inspect
	// the actual grid node that production replaces during a retry.
	assert.match(h.viewContent.querySelector('#contentGrid').innerHTML, /data-error="1"/, '重试仍失败时必须继续显示错误块');
	assert.ok(h.viewContent.querySelector('[data-retry-btn]'), '失败后重试按钮必须仍可用');

	// 第二次重试：成功 → 错误块消失，卡片出现
	h.viewContent.querySelector('[data-retry-btn]').dispatchEvent('click', {});
	await flush();
	assert.doesNotMatch(h.viewContent.querySelector('#contentGrid').innerHTML, /data-error="1"/, '恢复后必须移除错误块');
	assert.match(h.viewContent.querySelector('#contentGrid').innerHTML, /ShaderPack/, '恢复后应显示卡片');

	// 三次请求都必须带上同一套筛选（来源 / 类别 / 查询 / 游戏版本）
	assert.equal(h.api.calls.content.length, 3, '应是首次 + 两次重试，共三次请求');
	for (const call of h.api.calls.content) {
		assert.equal(call.query, 'shaders-x', '重试必须保留查询');
		assert.equal(call.category, 'shader', '重试必须保留类别');
		assert.equal(call.options.source, 'curseforge', '重试必须保留内容源');
		assert.equal(call.options.gameVersion, '1.20.1', '重试必须保留游戏版本筛选');
		assert.equal(call.options.loader, 'Fabric', '重试必须保留加载器筛选');
	}
	// 页头筛选控件的呈现也要保留
	assert.match(h.viewContent.innerHTML, /class="source-btn active" data-source="curseforge"/, '内容源按钮应保持激活');
	assert.match(h.viewContent.innerHTML, /class="filter active" data-cat="shader"/, '类别页签应保持激活');
	assert.match(h.viewContent.innerHTML, /id="contentSearch"[^>]*value="shaders-x"/, '搜索框应保留查询词');
});

test('CV-06: 新增状态文案中英文齐全（真实词典，无 raw key）', () => {
	const h = loadLauncher({});
	const keys = [
		'news.unverified', 'news.cachedBannerNoTime',
		'content.errorTitle', 'content.errorDesc', 'content.techDetails',
		'dl.retry', 'dl.sourceErrorTitle', 'dl.sourceErrorDesc',
		'dl.tasksErrorTitle', 'dl.tasksErrorDesc', 'dl.techDetails', 'dl.noVersionsForFilter',
	];
	h.ctx.SG.setLocale('zh');
	for (const k of keys) assert.notEqual(h.ctx.SG.t(k), k, 'zh 缺少 key: ' + k);
	h.ctx.SG.setLocale('en');
	for (const k of keys) assert.notEqual(h.ctx.SG.t(k), k, 'en 缺少 key: ' + k);
});

test('CV-06: 状态组件样式存在（长文本换行、焦点可见、窄窗口不横向溢出）', () => {
	const start = PINEGLOW_CSS.indexOf('CV-04 / CV-05 / CV-06 状态组件');
	assert.ok(start > 0, 'pineglow.css 应包含本轮状态组件样式块');
	const block = PINEGLOW_CSS.slice(start);
	for (const sel of ['.content-error', '.content-retry-btn', '.download-source-error', '.download-retry-btn', '.download-tasks', '.tech-details', '.tech-body']) {
		assert.ok(block.includes(sel), '状态组件样式缺少选择器: ' + sel);
	}
	const techBody = block.slice(block.indexOf('.tech-body {'));
	assert.match(techBody, /white-space:\s*pre-wrap/, '技术详情必须换行而非溢出');
	assert.match(techBody, /overflow-wrap:\s*anywhere/, '技术详情长文本必须允许任意位置换行');
	assert.match(techBody, /max-width:\s*100%/, '技术详情宽度不得超过容器');
	assert.match(block, /focus-visible/, '重试按钮与 summary 必须有可见焦点环');
	assert.match(block, /@media \(max-width: 1000px\)/, '960px 窗口需要窄宽度规则兜住横向溢出');
});
