'use strict';
/*
 * launcher.js 行为测试（DOM/交互层）
 * --------------------------------------------------------------------------
 * 与 api-contract.test.cjs 互补：
 *   - api-contract 只验证 api.js 的契约（fetch / 错误 / mock fallback）
 *   - launcher-behavior 在 vm 中加载完整 launcher.js，调用 SG.__test__
 *     暴露的内部函数，验证：
 *       1. 来源事件只绑定 #contentSourceSwitch .source-btn
 *       2. 点击资源卡片不触发搜索
 *       3. 点击安装按钮只提交一次，使用卡片自身身份
 *       4. 卡片身份缺失/非法时拒绝安装
 *       5. #content 直进时 renderContent 在搜索前 await loadState
 *       6. Vanilla 实例的 loader 不被发送
 *       7. 状态加载失败时 STATE.__loadFailed 被打上标记
 *       8. 竞态：A 慢 B 快 → B 显示；A 晚失败 → 不覆盖 B；切换分类 → 旧分类不写入
 *       9. 错误状态显示重试入口，重试后旧错误清掉
 *
 * 限制：vm 里只装 i18n + launcher.js（依赖 api.js 的部分用受控 stub 替换）。
 * 真实 WebView 验收仍需在 Avalonia + NativeWebView GUI 环境下做。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

/* ============== 受控 DOM 桩：仅支持 launcher.js Phase 1 范围用到的能力 ============== */
function makeDomStub() {
	const elementsById = new Map();
	const allElements = [];

	function makeEl(tag) {
		const listeners = {};
		const el = {
			ownerDocument: null,  // 后填
			tagName: (tag || 'div').toUpperCase(),
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
			innerHTML: '',
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
			removeAttribute(k) { delete this.attributes[k]; }, // MR17/A13：产品代码 3 处调用
			focus() { this._focused = true; }, // MR17/A13：产品代码 4 处调用
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
				eventObj.currentTarget = this;
				// 真实 DOM 行为：先沿捕获路径从根→target 触发 capture 监听，
				// 再沿冒泡路径从 target→根 触发 bubble 监听。
				// 这里 currentTarget 简单实现为事件被分发到的元素（每次 fn.call 之前更新）。
				const path = [];
				let cur = this;
				while (cur) { path.unshift(cur); cur = cur.parent; }
				// 捕获阶段：从根到 target
				for (const node of path) {
					const arr = (node._listeners[type] || []).filter(x => x.capture);
					for (const { fn } of arr) {
						eventObj.currentTarget = node;
						fn.call(node, eventObj);
						if (eventObj._stoppedImmediate) break;
					}
					if (eventObj._stoppedImmediate) break;
				}
				// 冒泡阶段：从 target 到根
				if (!eventObj._stoppedImmediate) {
					for (const node of [...path].reverse()) {
						const arr = (node._listeners[type] || []).filter(x => !x.capture);
						for (const { fn } of arr) {
							eventObj.currentTarget = node;
							fn.call(node, eventObj);
							if (eventObj._stoppedImmediate) break;
						}
						if (eventObj._stoppedImmediate) break;
						if (eventObj._stopped) break;
					}
				}
				// 增强 stopPropagation / stopImmediatePropagation
				const origStop = eventObj.stopPropagation;
				const origStopImm = eventObj.stopImmediatePropagation;
				eventObj.stopPropagation = function () { eventObj._stopped = true; if (origStop) origStop.call(eventObj); };
				eventObj.stopImmediatePropagation = function () { eventObj._stoppedImmediate = true; eventObj._stopped = true; if (origStopImm) origStopImm.call(eventObj); };
			},
			appendChild(child) {
				child.parent = this;
				this.children.push(child);
				if (child.id) elementsById.set(child.id, child);
				child._connected = true;
				return child;
			},
			insertBefore(child, ref) {
				child.parent = this;
				const i = ref ? this.children.indexOf(ref) : this.children.length;
				this.children.splice(i < 0 ? this.children.length : i, 0, child);
				if (child.id) elementsById.set(child.id, child);
				child._connected = true;
				return child;
			},
			remove() {
				if (this.parent) {
					const i = this.parent.children.indexOf(this);
					if (i >= 0) this.parent.children.splice(i, 1);
				}
				if (this.id) elementsById.delete(this.id);
				this._connected = false;
			},
			querySelector(sel) { return querySelectorIn(this, sel); },
			querySelectorAll(sel) { return querySelectorAllIn(this, sel); },
			closest(sel) { return closestIn(this, sel); },
			getBoundingClientRect() { return { top: 0, left: 0, width: 100, height: 30 }; },
			cloneNode() { return makeEl(tag); },
		};
		// dataset 用 Proxy 同步到 _dataset，便于测试读取
		el.dataset = new Proxy({}, {
			set(t, k, v) { t[k] = v; el.attributes['data-' + k] = v; return true; },
			get(t, k) { if (k in t) return t[k]; return el.attributes['data-' + k]; },
			has(t, k) { return k in t || ('data-' + k) in el.attributes; },
			deleteProperty(t, k) { delete t[k]; delete el.attributes['data-' + k]; return true; },
		});
		// isConnected getter + innerHTML setter（任何赋值都重新解析并替换子节点）
		Object.defineProperty(el, 'isConnected', {
			get() { return el._connected === true; },
			configurable: true,
		});
		let _innerHTML = '';
		Object.defineProperty(el, 'innerHTML', {
			get() { return _innerHTML; },
			set(v) {
				_innerHTML = String(v == null ? '' : v);
				// 关键：无论赋什么值，都要重置子节点（真实 DOM 行为）。
				// 真实浏览器中，innerHTML='<div/>' 会把现有所有子节点替换为解析后的新节点，
				// 旧节点（包括它们的事件监听）会从 DOM 断开。
				function disconnect(node) {
					node._connected = false;
					for (const c of (node.children || [])) disconnect(c);
				}
				for (const c of el.children) {
					c.parent = null;
					disconnect(c);
				}
				el.children = [];
				// 解析新 HTML 字符串，重新建一棵子树
				if (_innerHTML !== '') {
					parseInto(el, _innerHTML);
				}
			},
			configurable: true,
		});
		allElements.push(el);
		return el;
	}

	function matches(el, sel) {
		// 简化：仅支持 tag、.class、#id、[attr=v]、组合（空格 = 后代）
		if (!sel) return false;
		sel = sel.trim();
		// 简单：分词
		const tokens = sel.split(/\s+/);
		// 末段必须在 el 上匹配
		const last = tokens[tokens.length - 1];
		if (!matchOne(el, last)) return false;
		// 前面段必须存在祖先匹配
		for (let i = tokens.length - 2; i >= 0; i--) {
			let p = el.parent;
			let ok = false;
			while (p) {
				if (matchOne(p, tokens[i])) { ok = true; break; }
				p = p.parent;
			}
			if (!ok) return false;
		}
		return true;
	}
	function matchOne(el, token) {
		if (!token) return false;
		// 把复合选择器（如 ".source-btn[data-source=\"curseforge\"]"）
		// 拆成 .source-btn + [data-source=...] 逐项匹配
		let rest = token;
		// 先剥 tagName（如果有）：仅在开头是字母序列
		const tagMatch = rest.match(/^([a-zA-Z][a-zA-Z0-9]*)/);
		if (tagMatch) {
			if (!el.tagName || el.tagName.toLowerCase() !== tagMatch[1].toLowerCase()) return false;
			rest = rest.slice(tagMatch[1].length);
		}
		// 再剥 .class / #id / [attr=val] 的任意组合
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
				const k = m[1];
				const v = m[2];
				const actual = el.attributes[k];
				if (v == null) {
					if (actual === undefined) return false;
				} else {
					const cleanV = v.replace(/^['"]|['"]$/g, '');
					if (actual !== cleanV) return false;
				}
				rest = rest.slice(m[0].length);
			} else {
				return false;
			}
		}
		return true;
	}

	function collectAll(root, results) {
		for (const c of root.children) {
			results.push(c);
			collectAll(c, results);
		}
	}
	function querySelectorIn(root, sel) {
		const all = [];
		collectAll(root, all);
		return all.find(el => matches(el, sel)) || null;
	}
	function querySelectorAllIn(root, sel) {
		const all = [];
		collectAll(root, all);
		return all.filter(el => matches(el, sel));
	}
	function closestIn(el, sel) {
		let cur = el;
		while (cur) {
			if (matches(cur, sel)) return cur;
			cur = cur.parent;
		}
		return null;
	}

	const document = {
		readyState: 'loading',  // 让 launcher.js 把 init 排队到 DOMContentLoaded
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
			// 关键：launcher.js 用 DOMContentLoaded 触发 init()；测试中要禁止
			if (type === 'DOMContentLoaded') return;  // 永远不触发 init
			(document.body._listeners[type] = document.body._listeners[type] || []).push({ fn, capture: !!capture });
		},
		removeEventListener() {},
		dispatchEvent(type, obj) { document.body.dispatchEvent(type, obj); },
	};
	return { document, elementsById, makeEl };
}

/* ============== 加载 launcher.js 到隔离上下文，阻止 init 自动跑 ============== */
function loadLauncher(fetchHandler, getStateHandler) {
	const { document, elementsById, makeEl } = makeDomStub();
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
		window: null,  // 后填
		fetch: (url, init) => {
			if (fetchHandler) return fetchHandler(url, init);
			return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
		},
	};
	ctx.window = ctx;
	ctx.fetchCalls = [];
	// 把 fetch 包装一下，捕获 URL
	const originalFetch = ctx.fetch;
	ctx.fetch = (url, init) => {
		ctx.fetchCalls.push({ url: String(url), init: init || {} });
		return originalFetch(url, init);
	};
	// 装入 i18n.js + api.js（api.js 必须先于 launcher.js，否则 SG.api 未定义）
	const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
	const BOOT_API = 'SG.apiMode = "live"; SG.apiToken = "test-token";';
	// 准备一个可控的 SG.api
	const apiStub = makeApiStub(fetchHandler, getStateHandler);
	ctx.SG.api = apiStub;
	vm.createContext(ctx);
	// 在跑 launcher.js 之前装入 api.js（只用到 SG.api 已被注入）
	vm.runInContext(BOOT_API + '\n' + I18N_JS, ctx);
	// 现在跑 launcher.js；document.readyState='loading' 且 addEventListener 已过滤 DOMContentLoaded → init 不会跑
	vm.runInContext(LAUNCHER_JS, ctx);
	const T = ctx.SG.__test__;
	if (!T) throw new Error('SG.__test__ 未注入；launcher.js 加载失败');
	return { ctx, document, T, api: apiStub, elementsById, makeEl };
}

function makeApiStub(fetchHandler, getStateHandler) {
	// api.js 的 searchContent 实际逻辑：构造 URL，调 fetch，mapContent
	// 这里我们用可控 fetchHandler 模拟
	const api = {
		_implMode: 'live',
		_allowMockFallback: false,
		async getState() {
			if (getStateHandler) return getStateHandler();
			return { config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] };
		},
		async getAccounts() { return { accounts: [], capes: [] }; },
		async searchContent(query, category, options) {
			const opt = options || {};
			const source = (opt.source && /^(modrinth|curseforge)$/i.test(opt.source)) ? opt.source.toLowerCase() : 'modrinth';
			const params = new URLSearchParams({ q: query || '', category: category || 'mod', source });
			if (opt.gameVersion) params.set('gameVersion', opt.gameVersion);
			if (opt.loader) params.set('loader', opt.loader);
			const url = '/api/content/search?' + params.toString();
			const res = await (fetchHandler ? fetchHandler(url, { method: 'GET' }) : Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) }));
			if (!res.ok) {
				const j = await res.json().catch(() => ({}));
				throw new Error(j.error || ('HTTP ' + res.status));
			}
			const data = await res.json();
			return (data.results || []).map(r => ({
				id: r.id || r.projectId,
				title: r.title || '?',
				desc: r.desc || r.description || '',
				author: r.author || '',
				downloads: r.downloads || 0,
				icon: r.icon || '',
				versions: r.versions || [],
				loader: r.loader || '',
				category: r.category || 'mod',
				source: (r.source && /^(modrinth|curseforge)$/i.test(r.source)) ? r.source.toLowerCase() : source,
			}));
		},
		async installContent(data) {
			if (!api._installCalls) api._installCalls = [];
			api._installCalls.push(data);
			return { ok: true, id: 'task-' + api._installCalls.length, status: 'queued' };
		},
		async getContentTasks() { return []; },
	};
	return api;
}

/* ============== 1. 来源事件只绑定 #contentSourceSwitch .source-btn ============== */
test('点击 Modrinth 按钮触发一次正确来源的 searchContent；CurseForge 同样', async () => {
	const fetchedUrls = [];
	const { T, document } = loadLauncher((url) => {
		fetchedUrls.push(String(url));
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
	});
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const html = await T.renderContent();
	document.body.innerHTML = '';
	const root = makeMockContainer(document, html);
	T.bindContent(root);
	assert.equal(T.getContentSource(), 'modrinth');
	const modrinthBtn = root.querySelector('#contentSourceSwitch .source-btn[data-source="modrinth"]');
	const curseforgeBtn = root.querySelector('#contentSourceSwitch .source-btn[data-source="curseforge"]');
	assert.ok(modrinthBtn, '应能找到 Modrinth 来源按钮');
	assert.ok(curseforgeBtn, '应能找到 CurseForge 来源按钮');
	assert.ok(fetchedUrls[0] && fetchedUrls[0].includes('source=modrinth'), '首次 renderContent 应发 modrinth URL, got: ' + fetchedUrls[0]);
	curseforgeBtn.dispatchEvent('click', {});
	await waitMicro();
	await waitMicro();
	assert.equal(T.getContentSource(), 'curseforge', '点击后 contentSource 应切换为 curseforge');
	const curseforgeUrls = fetchedUrls.filter(u => u.includes('source=curseforge'));
	assert.ok(curseforgeUrls.length >= 1, '点击 CurseForge 后应发 curseforge URL, 实际 URLs: ' + JSON.stringify(fetchedUrls));
	assert.ok(fetchedUrls.some(u => u.includes('source=modrinth')), '首次 modrinth URL 仍应保留');
});

/* ============== 2. 点击资源卡片不触发搜索 ============== */
test('点击资源卡片不会触发 searchContent，也不会改变 contentSource', async () => {
	let searchCallCount = 0;
	const fetchHandler = (url) => {
		if (String(url).includes('/api/content/search')) searchCallCount++;
		return Promise.resolve({
			ok: true, status: 200,
			json: () => Promise.resolve({
				results: [{
					id: 'C1', title: 'SomeMod', desc: 'd', author: 'a', downloads: 1,
					icon: '', versions: [], loader: 'Fabric', category: 'mod', source: 'modrinth',
				}],
			}),
		});
	};
	const { T, document } = loadLauncher(fetchHandler);
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const html = await T.renderContent();
	document.body.innerHTML = '';
	const root = makeMockContainer(document, html);
	T.bindContent(root);
	const beforeSource = T.getContentSource();
	const beforeCount = searchCallCount;
	// 找一张卡片
	const card = root.querySelector('.mod-card');
	assert.ok(card, '应有至少一张卡片');
	if (card) {
		card.dispatchEvent('click', { type: 'click', target: card, preventDefault() {}, stopPropagation() {} });
		await waitMicro();
	}
	assert.equal(T.getContentSource(), beforeSource, '点击卡片不应改变 contentSource');
	assert.equal(searchCallCount, beforeCount, '点击卡片不应触发新的 searchContent');
});

/* ============== 3. 点击安装按钮只提交一次，使用卡片自身身份 ============== */
test('点击安装按钮只提交一次 installContent，使用卡片自身 source/category', async () => {
	const fetchedResults = {
		results: [{
			id: 'CARD-X', title: 'SomeMod', desc: 'd', author: 'a', downloads: 1,
			icon: '', versions: ['1.21.x'], loader: 'Fabric',
			category: 'shader', source: 'curseforge',
		}],
	};
	const { T, document, api } = loadLauncher((url) => {
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(fetchedResults) });
	});
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	// 故意让全局 contentSource / contentCat 与卡片不同
	T.setContentSource('modrinth');
	T.setContentCat('mod');
	const html = await T.renderContent();
	document.body.innerHTML = '';
	const root = makeMockContainer(document, html);
	T.bindContent(root);
	// 卡片身份应为 curseforge + shader
	const card = root.querySelector('.mod-card');
	assert.ok(card, '应能找到卡片');
	assert.equal(card.dataset.source, 'curseforge', '卡片 data-source 应为 curseforge');
	assert.equal(card.dataset.category, 'shader', '卡片 data-category 应为 shader');
	const installBtn = card.querySelector('.mod-install[data-install]');
	assert.ok(installBtn, '应能找到安装按钮');
	installBtn.dispatchEvent('click', { type: 'click', target: installBtn, preventDefault() {}, stopPropagation() {} });
	await waitMicro();
	await waitMicro();
	assert.ok(api._installCalls && api._installCalls.length === 1, `应只调用 1 次 installContent，实际 ${api._installCalls && api._installCalls.length}`);
	assert.equal(api._installCalls[0].source, 'curseforge', '安装必须使用卡片 source=curseforge');
	assert.equal(api._installCalls[0].category, 'shader', '安装必须使用卡片 category=shader');
	assert.equal(api._installCalls[0].projectId, 'CARD-X');
});

/* ============== 4. 卡片身份缺失/非法时拒绝安装 ============== */
test('卡片 source 缺失时拒绝安装并 toast 提示', async () => {
	const { T, document, api } = loadLauncher(() => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [{ id: 'X', title: 't', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: '' }] }) }));
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const html = await T.renderContent();
	document.body.innerHTML = '';
	const root = makeMockContainer(document, html);
	T.bindContent(root);
	// 手动去掉卡片的 data-source
	const card = root.querySelector('.mod-card');
	const btn = card.querySelector('.mod-install');
	delete card.dataset.source;
	delete btn.dataset.source;
	btn.dispatchEvent('click', { type: 'click', target: btn, preventDefault() {}, stopPropagation() {} });
	await waitMicro();
	assert.equal((api._installCalls || []).length, 0, 'source 缺失时不应调用 installContent');
});

/* ============== 5. #content 直进时 renderContent 在搜索前 await loadState ============== */
test('renderContent 在 STATE 为 null 时会调用 getState，结果透传到 searchContentOptions', async () => {
	let getStateCalled = 0;
	let lastUrl = null;
	const { T } = loadLauncher((url) => {
		lastUrl = String(url);
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
	}, () => {
		getStateCalled++;
		return {
			config: { selectedInstanceId: 'inst-A' },
			instances: [{ id: 'inst-A', name: 'Fabulously', version: '1.21.1', loader: 'Fabric', modsCount: 80, savesCount: 0, gameDir: 'D:\\games' }],
			accounts: [], java: [],
		};
	});
	// 不预设 STATE，让 renderContent 自己拉
	assert.equal(T.getState(), null, '预置：STATE 初始为 null');
	await T.renderContent();
	assert.ok(getStateCalled >= 1, 'STATE 缺失时 renderContent 必须调用 getState 至少 1 次');
	assert.ok(T.getSelectedInstanceId() === 'inst-A', 'loadState 应从 config.selectedInstanceId 取到 inst-A');
	const opt = T.searchContentOptions();
	assert.equal(opt.gameVersion, '1.21.1', 'searchContentOptions.gameVersion 应来自已加载实例');
	assert.equal(opt.loader, 'Fabric', 'searchContentOptions.loader 应来自已加载实例');
	assert.ok(lastUrl && lastUrl.includes('gameVersion=1.21.1'), '搜索 URL 应包含 gameVersion=1.21.1, got: ' + lastUrl);
	assert.ok(lastUrl && lastUrl.includes('loader=Fabric'), '搜索 URL 应包含 loader=Fabric, got: ' + lastUrl);
});

/* ============== 6. Vanilla 实例的 loader 不被发送 ============== */
test('Vanilla 实例的 loader 参数为空字符串，不发送错误筛选', async () => {
	let lastUrl = null;
	const { T } = loadLauncher((url) => { lastUrl = String(url); return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) }); },
		() => ({ config: { selectedInstanceId: 'vanilla-1' }, instances: [{ id: 'vanilla-1', name: 'Vanilla', version: '1.21.4', loader: 'Vanilla', modsCount: 0, savesCount: 0, gameDir: 'D:\\games' }], accounts: [], java: [] }));
	await T.renderContent();
	const opt = T.searchContentOptions();
	assert.equal(opt.loader, '', 'Vanilla 实例 loader 应为空字符串');
	assert.ok(lastUrl && !/loader=vanilla/i.test(lastUrl), '搜索 URL 不应包含 loader=vanilla, got: ' + lastUrl);
});

/* ============== 7. 状态加载失败时区分"无实例"与"加载失败" ============== */
test('getState 失败时显示"加载失败"而非"无实例"，且 STATE 保持 null 以便下次重试', async () => {
	const { T, document } = loadLauncher(
		(url) => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) }),
		() => { throw new Error('network down'); }
	);
	// 不预置 STATE，让 renderContent 触发 loadState，loadState 失败
	await T.renderContent();
	const state = T.getState();
	// v3 修复：loadState 失败时 STATE 保持 null（不缓存失败对象），
	// 这样下次调用 loadState 仍会重新尝试。
	assert.equal(state, null, 'STATE 失败时应保持 null，避免永久缓存失败');
	const html = await T.renderContent();
	assert.match(html, /content\.stateLoadFailed|状态加载失败/, '页面应显示"加载失败"提示');
	assert.match(html, /data-state-retry-btn/, '页面应包含"重试实例状态"入口');
	assert.doesNotMatch(html, /data-state="unfiltered"/, '失败时不应显示"未按实例过滤"');
});

/* ============== 8. 竞态：A 慢 B 快 → B 显示 ============== */
test('竞态：A 请求慢、B 请求快，最终 grid 显示 B 的结果', async () => {
	// 用 deferred 主动控制完成顺序
	let resolveA, resolveB;
	const promiseA = new Promise(r => { resolveA = r; });
	const promiseB = new Promise(r => { resolveB = r; });
	let callIdx = 0;
	const fetchHandler = (url) => {
		callIdx++;
		if (callIdx === 1) return promiseA;  // A 是先发的
		return promiseB;  // B 后发
	};
	const { T, document } = loadLauncher(fetchHandler);
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	// 第一次 renderContent → A
	const html1Promise = T.renderContent();
	// 立刻切 source → B
	T.setContentSource('curseforge');
	const html2Promise = T.renderContent();
	// 先让 B 完成（B 更快）
	resolveB({ ok: true, status: 200, json: () => Promise.resolve({ results: [{ id: 'B-1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'curseforge' }] }) });
	resolveA({ ok: true, status: 200, json: () => Promise.resolve({ results: [{ id: 'A-1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }] }) });
	const out1 = await html1Promise;  // A 已 resolved 但 token 短路 → ''
	const out2 = await html2Promise;  // B 已 resolved → BMod
	assert.equal(out1, '', 'A 慢 → out1 应被 token 短路为空');
	assert.match(out2, /BMod/, 'B 快 → out2 应包含 BMod');
	assert.doesNotMatch(out2, /AMod/, 'A 的结果不应进入最新输出（被 token 短路）');
});

/* ============== 9. 错误状态显示重试入口 ============== */
test('搜索失败时显示重试入口；重试成功后旧错误清掉', async () => {
	let firstCall = true;
	const fetchHandler = () => {
		if (firstCall) {
			firstCall = false;
			return Promise.reject(new Error('HTTP 500'));
		}
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) });
	};
	const { T, document } = loadLauncher(fetchHandler);
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const html = await T.renderContent();
	assert.match(html, /data-retry-btn="1"/, '失败时 HTML 应包含重试按钮');
	assert.match(html, /HTTP 500/, '失败时 HTML 应显示错误消息');
	// 第二次 renderContent（重试）应清掉错误
	const html2 = await T.renderContent();
	assert.doesNotMatch(html2, /HTTP 500/, '重试成功后旧错误应消失');
	assert.doesNotMatch(html2, /data-error="1"/, '重试成功后 content-error 块应消失');
});

/* ============== 10. Mod → shader → Mod 切换：来源控件可见性正确 ============== */
test('分类切换为 shader 时来源控件隐藏，切回 mod 时重新显示', async () => {
	const { T, document } = loadLauncher(() => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) }));
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const htmlMod = await T.renderContent();
	assert.match(htmlMod, /id="contentSourceSwitch"[^>]*style="display:"/, 'mod 时来源控件默认显示');
	// 切到 shader
	T.setContentCat('shader');
	const htmlShader = await T.renderContent();
	assert.match(htmlShader, /id="contentSourceSwitch"[^>]*style="display:none"/, 'shader 时来源控件应隐藏');
	// 切回 mod
	T.setContentCat('mod');
	const htmlMod2 = await T.renderContent();
	assert.match(htmlMod2, /id="contentSourceSwitch"[^>]*style="display:"/, '切回 mod 时来源控件应重新显示');
});

/* ============== 11. 列表重新渲染后无重复事件导致重复提交 ============== */
test('rerenderGrid 后旧安装按钮的事件 handler 不会重复触发', async () => {
	const { T, document, api } = loadLauncher(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({
			results: [{
				id: 'DUPE', title: 'Mod', desc: 'd', author: 'a', downloads: 1,
				icon: '', versions: [], loader: 'Fabric', category: 'mod', source: 'modrinth',
			}],
		}),
	}));
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const html = await T.renderContent();
	document.body.innerHTML = '';
	const root = makeMockContainer(document, html);
	T.bindContent(root);
	const btn = root.querySelector('.mod-install');
	assert.ok(btn, '应能找到安装按钮');
	// 第一次点击：调用一次
	btn.dispatchEvent('click', { type: 'click', target: btn, preventDefault() {}, stopPropagation() {} });
	await waitMicro();
	await waitMicro();
	// 触发一次切换分类 + 切回（导致 bindContentInstalls 再次跑）→ 旧按钮元素已被替换
	T.setContentCat('shader');
	await T.renderContent();
	T.setContentCat('mod');
	const html2 = await T.renderContent();
	document.body.innerHTML = '';
	const root2 = makeMockContainer(document, html2);
	T.bindContent(root2);
	// 切回后旧按钮的 click 不应再触发任何东西（DOM 已被替换）
	btn.dispatchEvent('click', { type: 'click', target: btn, preventDefault() {}, stopPropagation() {} });
	await waitMicro();
	// 总安装次数仍为 1（仅第一次有效）
	assert.equal((api._installCalls || []).length, 1, `rerender 后旧按钮事件不应再触发 install, 实际 ${(api._installCalls || []).length}`);
});

/* ============== 12. A 晚失败，B 已成功 → A 不覆盖 B ============== */
test('A 晚失败、B 已成功 → A 的错误不得覆盖 B 的结果', async () => {
	let resolveA, resolveB;
	const promiseA = new Promise((_, rej) => { resolveA = () => rej(new Error('A timeout')); });
	const promiseB = new Promise(r => { resolveB = r; });
	let callIdx = 0;
	const fetchHandler = () => { callIdx++; return callIdx === 1 ? promiseA : promiseB; };
	const { T } = loadLauncher(fetchHandler);
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const html1Promise = T.renderContent();
	T.setContentQuery('hello');
	const html2Promise = T.renderContent();
	// B 先成功
	resolveB({ ok: true, status: 200, json: () => Promise.resolve({ results: [{ id: 'B1', title: 'BMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }] }) });
	// A 后失败
	resolveA();
	const out1 = await html1Promise;  // A 失败但 token 短路 → ''
	const out2 = await html2Promise;  // B 成功 → BMod
	assert.equal(out1, '', 'A 失败时 out1 应被 token 短路');
	assert.match(out2, /BMod/, 'B 的结果应保留');
	assert.equal(T.getContentLoadError(), null, 'A 的错误不应污染 loadError（被 token 短路）');
});

/* ============== 13. A 晚成功、B 已失败 → A 不覆盖 B 的错误 ============== */
test('A 晚成功、B 已失败 → A 的结果不得覆盖 B 的错误', async () => {
	let resolveA, resolveB;
	const promiseA = new Promise(r => { resolveA = r; });
	const promiseB = new Promise((_, rej) => { resolveB = () => rej(new Error('B timeout')); });
	let callIdx = 0;
	const fetchHandler = () => { callIdx++; return callIdx === 1 ? promiseA : promiseB; };
	const { T } = loadLauncher(fetchHandler);
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const html1Promise = T.renderContent();
	T.setContentQuery('hello');
	const html2Promise = T.renderContent();
	// B 先失败（token=2，是最新）
	resolveB();
	// A 后成功（token=1，已过期）
	resolveA({ ok: true, status: 200, json: () => Promise.resolve({ results: [{ id: 'A1', title: 'AMod', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }] }) });
	const out1 = await html1Promise;  // A 已过期，被 token 短路 → ''
	const out2 = await html2Promise;  // B 失败且 token 匹配 → HTML 含错误
	assert.equal(out1, '', 'A 慢成功时 out1 应被 token 短路');
	assert.match(out2, /B timeout/, 'out2 应包含 B 的错误消息');
	assert.doesNotMatch(out2, /AMod/, 'A 的旧结果不得写入最终输出');
	assert.equal(T.getContentLoadError(), 'B timeout', 'loadError 应是 B 的错误（不是 A）');
});

/* ============== 14. 切换分类：旧分类结果不得写入新分类列表 ============== */
test('切到 shader 时 mod 的旧结果不得写入新分类的 grid', async () => {
	let resolveMod, resolveShader;
	const promiseMod = new Promise(r => { resolveMod = r; });
	const promiseShader = new Promise(r => { resolveShader = r; });
	let callIdx = 0;
	const fetchHandler = () => {
		callIdx++;
		const q = (callIdx === 1) ? 'mod query' : 'shader query';
		// 用 q 区分
		return callIdx === 1 ? promiseMod : promiseShader;
	};
	const { T } = loadLauncher((url) => fetchHandler(url));
	T.setState({ config: {}, instances: [], accounts: [], java: [] });
	T.setContentCat('mod');
	const modPromise = T.renderContent();
	T.setContentCat('shader');
	const shaderPromise = T.renderContent();
	resolveShader({ ok: true, status: 200, json: () => Promise.resolve({ results: [{ id: 'S1', title: 'ShaderOnly', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'shader', source: 'modrinth' }] }) });
	resolveMod({ ok: true, status: 200, json: () => Promise.resolve({ results: [{ id: 'M1', title: 'ModOnly', desc: 'd', author: 'a', downloads: 1, icon: '', versions: [], loader: '', category: 'mod', source: 'modrinth' }] }) });
	const modOut = await modPromise;
	const shaderOut = await shaderPromise;
	assert.equal(modOut, '', 'mod 旧结果应被 token 短路');
	assert.match(shaderOut, /ShaderOnly/, 'shader 新结果应保留');
	assert.doesNotMatch(shaderOut, /ModOnly/, 'mod 的旧结果不得写入 shader 列表');
});

/* ============== 工具函数 ============== */
// 极简 HTML 解析器：把 html 字符串解析为子树并 append 到 targetEl
// 用于 innerHTML 重新赋值与 makeMockContainer
const VOID_TAGS = new Set(['br', 'hr', 'img', 'input', 'meta', 'link']);
function parseInto(targetEl, html) {
	const document = targetEl.ownerDocument;
	const tokenRe = /<\/?([a-zA-Z][a-zA-Z0-9-]*)\s*([^>]*?)\/?>|([^<]+)|<!--[\s\S]*?-->/g;
	const attrRe = /([a-zA-Z_:][\w:-]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?/g;
	const stack = [targetEl];
	html.replace(tokenRe, (match, tagName, attrs, text) => {
		if (text !== undefined) {
			const cur = stack[stack.length - 1];
			const t = document.createTextNode(text);
			cur.appendChild(t);
			return '';
		}
		if (match.startsWith('<!--')) return '';
		const isClose = match.startsWith('</');
		if (isClose) {
			if (stack.length > 1) stack.pop();
			return '';
		}
		const isSelfClose = match.endsWith('/>') || VOID_TAGS.has(tagName.toLowerCase());
		const el = document.createElement(tagName);
		if (attrs) {
			let m;
			attrRe.lastIndex = 0;
			while ((m = attrRe.exec(attrs)) !== null) {
				const k = m[1];
				const v = m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : ''));
				el.setAttribute(k, v);
			}
		}
		const cur = stack[stack.length - 1];
		cur.appendChild(el);
		if (!isSelfClose) stack.push(el);
		return '';
	});
}
function parseHtml(document, html) {
	const root = document.createElement('div');
	parseInto(root, html);
	return root;
}
function makeMockContainer(document, html) {
	const root = parseHtml(document, html);
	document.body.appendChild(root);
	return root;
}

// 调试：打印 HTML 头部 + 解析后根的子元素（默认关闭）
function debugParse(_document, _html) { /* no-op */ }
function waitMicro() { return new Promise(r => setTimeout(r, 10)); }
