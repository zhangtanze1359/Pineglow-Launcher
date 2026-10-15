'use strict';
/*
 * goal-core-flow.test.cjs — LONG-QUALITY-GOAL / G2 六流程普通行为验证
 * ---------------------------------------------------------------------------
 * 与 api-contract / MR0x 静态扫描测试互补：这里在 vm 中加载真实 i18n.js + 真实
 * launcher.js，注入可控 SG.api 与受控 DOM，然后**调用真实生产入口**
 * （openRecoveryPanel / openModBisect / openDiagnoseHistory / handleImportFiles /
 *   initUpdateSection / renderInstallations+openInstanceMenu），断言可观察行为：
 * 请求次数、按钮可用性、状态文案、失败分支、重试入口。
 *
 * 静态扫描只能作为辅助，不能单独证明行为；真实 Avalonia/WebView2 GUI 仍属未验证。
 *
 * 本文件下面的 DOM 桩与 ui/tests/launcher-behavior.test.cjs 同源（同一受控桩代码），
 * 因此两类测试对 DOM 语义的判断口径一致。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = "C:\\Users\\Administrator\\AppData\\Local\\Temp\\dsr-rework-r0-685bb8d5";
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');

/* ===== 受控 DOM 桩（与 launcher-behavior.test.cjs 同源） ===== */

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
				// LONG-QUALITY-GOAL / G2：布尔属性必须同时反映到元素属性上，
				// 否则 <button disabled> / <input checked> 在受控 DOM 里读不到真实语义。
				if (k === 'disabled') this.disabled = !(v === undefined || v === null || v === false || v === 'false');
				if (k === 'checked') this.checked = !(v === undefined || v === null || v === false || v === 'false');
				if (k === 'hidden') this.hidden = !(v === undefined || v === null || v === false || v === 'false');
				// 反射属性：真实 DOM 里 meta.content / input.value 是可读属性，
				// 受控 DOM 必须同样反射，否则「读取 meta 拿会话 token」这类生产代码读不到值。
				if (k === 'content' || k === 'value') this[k] = v;
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
			// LONG-QUALITY-GOAL / G2 补充：openModal 需要 host.contains(document.activeElement)。
			// 语义与真实 DOM 一致：沿 parent 链判断自身是否是 node 的祖先（含自身）。
			contains(node) {
				let cur = node;
				while (cur) { if (cur === this) return true; cur = cur.parent; }
				return false;
			},
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

/* ===== 加载器：真实 i18n.js + 真实 launcher.js + 可控 SG.api ===== */
function loadGoalLauncher(apiOverrides, options) {
	const opt = options || {};
	const { document, elementsById, makeEl } = makeDomStub();

	// 生产代码要求弹窗宿主与 toast 宿主存在；这里按 ui/index.html 的真实 id 建立。
	const modalHost = document.createElement('div');
	modalHost.setAttribute('id', 'modalHost');
	document.body.appendChild(modalHost);
	const toastEl = document.createElement('div');
	toastEl.setAttribute('id', 'toast');
	document.body.appendChild(toastEl);
	const toastLog = [];
	let toastText = '';
	Object.defineProperty(toastEl, 'textContent', {
		configurable: true,
		get() { return toastText; },
		set(v) { toastText = String(v == null ? '' : v); toastLog.push(toastText); },
	});

	document.activeElement = null;
	document.contains = () => true;

	const calls = [];
	const defaults = {
		async getState() { return { config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }; },
		async getAccounts() { return { accounts: [], capes: [] }; },
		async listRecoveries() { return { retentionDays: 30, recoveries: [] }; },
		async restoreRecovery() { return { ok: true, conflicts: 0 }; },
		async bisectPending() { return { pending: false }; },
		async bisectStart() { return { enabledAtStart: [] }; },
		async bisectTest() { return { enabledAtStart: [] }; },
		async bisectGiveUp() { return { enabledAtStart: [] }; },
		async bisectResume() { return { enabledAtStart: [] }; },
		async diagnoseHistory() { return { reports: [], notice: null }; },
		async diagnoseExport() { return { ok: true, status: 200, text: 'x' }; },
		async deleteInstance() { return { ok: true }; },
		async registerInstance() { return { ok: true }; },
		async importInstance() { return { ok: true, name: 'pack.zip' }; },
		async cancelImport() { return { cancelled: true }; },
		async getUpdateStatus() { return { state: 'idle', progress: 0 }; },
	};
	const merged = Object.assign({}, defaults, apiOverrides || {});
	const api = {};
	for (const key of Object.keys(merged)) {
		const fn = merged[key];
		api[key] = async function (...args) {
			calls.push({ name: key, args });
			return fn.apply(null, args);
		};
	}

	const ctx = {
		SG: { setLocale() {}, t: (k) => k, applyStaticI18n() {} },
		location: { protocol: 'http:', hash: '' },
		history: { replaceState() {}, pushState() {} },
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN', platform: 'Win32' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		document,
		setTimeout, clearTimeout, setInterval, clearInterval,
		requestAnimationFrame: (fn) => setTimeout(fn, 0),
		cancelAnimationFrame: () => {},
		Promise, URLSearchParams, console, Date, JSON, Math, Object, Array, String, Number,
		Boolean, Error, Symbol, RegExp, Map, Set, Proxy, Reflect, isNaN, parseInt, parseFloat,
		Image: function () { return { addEventListener() {}, src: '' }; },
		Blob: function () {},
		FileReader: function () {
			this.result = null;
			this.onload = null;
			this.onerror = null;
			this.readAsDataURL = () => {
				setTimeout(() => {
					this.result = 'data:application/zip;base64,QUJD';
					if (this.onload) this.onload();
				}, 0);
			};
		},
		URL: { createObjectURL() { return 'blob:goal-fake'; }, revokeObjectURL() {} },
		crypto: { randomUUID: () => 'goal-test-operation-id-0001' },
		// 生产代码用裸 confirm()；测试必须能控制它（取消路径不得发请求）。
		confirm: () => (typeof opt.confirm === 'function' ? !!opt.confirm() : true),
		window: null,
		fetch: () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({}) }),
	};
	ctx.window = ctx;
	ctx.SG.api = api;
	vm.createContext(ctx);
	vm.runInContext('SG.apiMode = "live"; SG.apiToken = "test-token";\n' + I18N_JS, ctx);
	vm.runInContext(LAUNCHER_JS, ctx);
	const T = ctx.SG.__test__;
	if (!T) throw new Error('SG.__test__ 未注入；launcher.js 加载失败');
	return { ctx, document, elementsById, makeEl, T, api, calls, toastLog, toastEl, modalHost, t: ctx.SG.t };
}

/* ===== 行为辅助 ===== */
// 受控 DOM 只把「显式赋值过 innerHTML 的节点」保留为字符串；解析出来的子节点只有 children/text。
// 因此读取解析子树的文本要用 textOf()，不能直接读 textContent/innerHTML。
function textOf(el) {
	if (!el) return '';
	const parts = [];
	(function walk(node) {
		if (node.tagName === '#text' || node.tagName === '#TEXT') { parts.push(node.textContent || ''); return; }
		if (typeof node.textContent === 'string' && node.textContent && (!node.children || !node.children.length)) {
			parts.push(node.textContent);
		}
		for (const c of (node.children || [])) walk(c);
	})(el);
	return parts.join('');
}
// 生产代码里有 setTimeout(0)（FileReader 桩、toast 清理），只清微任务不足以推进流程。
async function settle(n) {
	const count = n || 6;
	for (let i = 0; i < count; i++) {
		await Promise.resolve();
		await new Promise(r => setTimeout(r, 1));
	}
}
async function activate(el) {
	assert.ok(el, 'expected element is missing');
	el.dispatchEvent('click', {});
	if (typeof el.onclick === 'function') {
		await el.onclick({ target: el, preventDefault() {}, stopPropagation() {} });
	}
	await settle();
}
function callNames(h) { return h.calls.map(c => c.name); }
function countCalls(h, name) { return h.calls.filter(c => c.name === name).length; }

/* ===== 对照：证明受控夹具本身可用（正常路径必须通过） ===== */
test('CONTROL openRecoveryPanel renders a normal recovery record', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({
			retentionDays: 30,
			recoveries: [{
				operationId: 'op-1', status: 'quarantined', createdAt: '2026-10-03T00:00:00Z',
				totalBytes: 2048, files: [{ relativePath: 'a.jar', state: 'quarantined' }],
			}],
		}),
	});
	await h.T.openRecoveryPanel({ id: 'i1', name: 'Inst' });
	const body = h.document.getElementById('recoveryContent');
	assert.ok(body, 'the modal content host must exist');
	assert.match(body.innerHTML, /a\.jar/, 'the file row must be rendered');
	assert.ok(h.document.querySelector('.rec-restore'), 'the restore button must exist');
	assert.equal(countCalls(h, 'listRecoveries'), 1);
});

/* ===== RECOVERY-01：查询失败必须给出明确错误 + 重试 ===== */
test('RECOVERY-01 recovery query failure shows an error and a retry action', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => { throw new Error('boom-transport'); },
	});
	await h.T.openRecoveryPanel({ id: 'i1', name: 'Inst' });
	assert.equal(countCalls(h, 'listRecoveries'), 1);
	const retry = h.document.getElementById('rRetry');
	assert.ok(retry, 'RECOVERY-01: the failure branch must offer a retry action');
	const body = h.document.getElementById('recoveryContent');
	assert.match(body.innerHTML, /boom-transport/, 'the real error must be shown');
	assert.equal(h.document.querySelector('.rec-restore'), null, 'no restore action on the failure branch');

	await activate(retry);
	assert.equal(countCalls(h, 'listRecoveries'), 2, 'retrying must query again');
});

/* ===== BISECT-03：pending 查询失败不得静默当作“无未完成会话” ===== */
test('BISECT-03 pending query failure shows an explicit error and retry', async () => {
	let fail = true;
	const h = loadGoalLauncher({
		bisectPending: async () => { if (fail) throw new Error('pending-transport'); return { pending: false }; },
	});
	await h.T.openModBisect({ id: 'i1', name: 'Inst', version: '1.20.1', loader: 'Fabric' });
	assert.equal(countCalls(h, 'bisectPending'), 1);
	const retry = h.document.getElementById('bPendingRetry');
	assert.ok(retry, 'BISECT-03: pending query failure must offer a retry action');
	assert.equal(h.document.getElementById('bStart'), null,
		'BISECT-03: the normal start button must NOT be shown when the previous state is unknown');
	assert.match(h.document.getElementById('bisectContent').innerHTML, /pending-transport/,
		'the real error must be shown');

	fail = false;
	await activate(retry);
	assert.equal(countCalls(h, 'bisectPending'), 2, 'retrying must query again');
	assert.ok(h.document.getElementById('bStart'), 'after the retry the normal start entry appears');
});

/* ===== DIAGNOSIS-03：历史读取失败必须给出错误 + 重试 ===== */
test('DIAGNOSIS-03 history query failure shows an error and retry', async () => {
	const h = loadGoalLauncher({
		diagnoseHistory: async () => { throw new Error('history-transport'); },
	});
	await h.T.openDiagnoseHistory({ id: 'i1', name: 'Inst' });
	assert.equal(countCalls(h, 'diagnoseHistory'), 1);
	const retry = h.document.getElementById('dhRetry');
	assert.ok(retry, 'DIAGNOSIS-03: the failure branch must offer a retry action');
	assert.match(h.document.getElementById('diagHistoryContent').innerHTML, /history-transport/,
		'the real error must be shown');

	await activate(retry);
	assert.equal(countCalls(h, 'diagnoseHistory'), 2, 'retrying must query again');
});

/* ===== DIAGNOSIS-04：非空历史必须真的渲染出来（并且能说明未包含原始证据） ===== */
test('DIAGNOSIS-04 a non-empty history renders and export states the evidence scope', async () => {
	const h = loadGoalLauncher({
		diagnoseHistory: async () => ({
			notice: null,
			reports: [{
				reportId: 'rpt-1', status: 'completed', createdAt: '2026-10-03T00:00:00Z',
				summary: 'summary text', redactionTotal: 2,
			}],
		}),
	});
	await h.T.openDiagnoseHistory({ id: 'i1', name: 'Inst' });
	const exportBtn = h.document.querySelector('[data-export="json"]');
	assert.ok(exportBtn, 'DIAGNOSIS-04: a non-empty history must render its export action');
	assert.equal(exportBtn.dataset.report, 'rpt-1');

	const cb = h.document.getElementById('dhIncludeEvidence');
	assert.ok(cb, 'the include-original-evidence checkbox must exist');
	assert.ok(!cb.checked, 'the original evidence must be excluded by default');

	await activate(exportBtn);
	const call = h.calls.filter(c => c.name === 'diagnoseExport').pop();
	assert.ok(call, 'export must call diagnoseExport');
	assert.equal(call.args[1], 'rpt-1');
	assert.equal(call.args[2], 'json');
	assert.equal(call.args[3], false, 'includeEvidence must stay false when unchecked');
});

/* =========================================================================
 * G2 第 2 批：其余 22 项普通流程行为场景（第二批在同一受控夹具上运行）
 * ========================================================================= */

/* ===== 只加载 api.js 的夹具：验证已发布归一化路径（不改产品实现） ===== */
function loadApiOnly(fetchHandler) {
	const { document, elementsById } = makeDomStub();
	// api.js 在加载时会把 SG.apiToken 覆盖为页面里的会话 meta；没有 meta 就永远是空串。
	// 这里按真实 index.html 的结构放一个受控 meta，测试才能验证 token 透传。
	const meta = document.createElement('meta');
	meta.setAttribute('name', 'pineglow-session-token');
	meta.setAttribute('content', 'test-token');
	document.body.appendChild(meta);
	const ctx = {
		SG: { setLocale() {}, t: (k) => k, applyStaticI18n() {}, apiMode: 'live', apiToken: 'test-token' },
		location: { protocol: 'http:', hash: '' },
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		document,
		setTimeout, clearTimeout, setInterval, clearInterval,
		Promise, URLSearchParams, console, Date, JSON, Math, Object, Array, String, Number,
		Boolean, Error, Symbol, RegExp, Map, Set, isNaN, parseInt, parseFloat,
		fetch: fetchHandler,
		window: null,
	};
	ctx.window = ctx;
	vm.createContext(ctx);
	vm.runInContext('SG.apiMode = "live";\n' + I18N_JS, ctx);
	vm.runInContext(API_JS, ctx);
	return { ctx, api: ctx.SG.api, document, elementsById };
}

function mountView(h, id, html) {
	let el = h.document.getElementById(id);
	if (!el) {
		el = h.document.createElement('div');
		el.setAttribute('id', id);
		h.document.body.appendChild(el);
	}
	el.innerHTML = html || '';
	return el;
}

function instA() { return { id: 'i1', name: 'AlphaPack', version: '1.20.1', loader: 'Fabric', gameDirectory: 'D:\\mc\\alpha', savesCount: 3 }; }
function instB() { return { id: 'i2', name: 'BetaPack', version: '1.19.2', loader: 'Vanilla', gameDirectory: 'D:\\mc\\beta', savesCount: 0 }; }
function recOp(over) {
	return Object.assign({
		operationId: 'op-1', status: 'quarantined', createdAt: '2026-10-03T00:00:00Z',
		totalBytes: 2048, files: [{ relativePath: 'a.jar', state: 'quarantined' }],
	}, over || {});
}

/* ===================== INSTANCE-01..04：实例移除 ===================== */
test('INSTANCE-01 cancel does not issue a delete request', async () => {
	const h = loadGoalLauncher({
		getState: async () => ({ config: {}, instances: [instA(), instB()], accounts: [], java: [] }),
	}, { confirm: () => false });
	mountView(h, 'view-installations', '');
	h.T.openInstanceMenu(instA());
	await activate(h.document.getElementById('mDel'));
	assert.equal(countCalls(h, 'deleteInstance'), 0, 'cancelling must not delete');
	const view = h.document.getElementById('view-installations');
	assert.equal(view.innerHTML, '', 'cancelling must not re-render the list');
});

test('INSTANCE-02 confirm removes the instance from the list and re-renders', async () => {
	const state = { config: { selectedInstanceId: 'i1' }, instances: [instA(), instB()], accounts: [], java: [] };
	const h = loadGoalLauncher({
		getState: async () => JSON.parse(JSON.stringify(state)),
		deleteInstance: async (id) => {
			state.instances = state.instances.filter(i => i.id !== id);
			return { ok: true };
		},
	}, { confirm: () => true });
	mountView(h, 'view-installations', '');
	h.T.openInstanceMenu(instA());
	await activate(h.document.getElementById('mDel'));
	assert.equal(countCalls(h, 'deleteInstance'), 1, 'exactly one delete request');
	const view = h.document.getElementById('view-installations');
	assert.doesNotMatch(view.innerHTML, /AlphaPack/, 'the removed instance must disappear from the list');
	assert.match(view.innerHTML, /BetaPack/, 'the remaining instance must stay');
});

test('INSTANCE-03 removing the selected instance clears the stale selection', async () => {
	const state = { config: { selectedInstanceId: 'i1' }, instances: [instA(), instB()], accounts: [], java: [] };
	const h = loadGoalLauncher({
		getState: async () => JSON.parse(JSON.stringify(state)),
		deleteInstance: async (id) => {
			state.instances = state.instances.filter(i => i.id !== id);
			return { ok: true };
		},
	}, { confirm: () => true });
	mountView(h, 'view-installations', '');
	await h.T.renderInstallations();
	h.T.openInstanceMenu(instA());
	assert.equal(h.T.getSelectedInstanceId(), 'i1', 'precondition: the removed instance is selected');
	await activate(h.document.getElementById('mDel'));
	assert.notEqual(h.T.getSelectedInstanceId(), 'i1', 'the stale selection id must not survive the removal');
	assert.equal(h.T.getSelectedInstanceId(), 'i2', 'selection falls back to a surviving instance');
});

test('INSTANCE-04 repeated clicks during the request issue one request', async () => {
	let release;
	const pending = new Promise(r => { release = r; });
	const h = loadGoalLauncher({
		getState: async () => ({ config: {}, instances: [instA(), instB()], accounts: [], java: [] }),
		deleteInstance: () => pending,
	}, { confirm: () => true });
	mountView(h, 'view-installations', '');
	h.T.openInstanceMenu(instA());
	const del = h.document.getElementById('mDel');
	const first = del.onclick({ target: del, preventDefault() {}, stopPropagation() {} });
	const second = del.onclick({ target: del, preventDefault() {}, stopPropagation() {} });
	assert.equal(countCalls(h, 'deleteInstance'), 1, 'the second click while in flight must be ignored');
	release({ ok: true });
	await first;
	await second;
	await settle();
	assert.equal(countCalls(h, 'deleteInstance'), 1);
});

/* ===================== RECOVERY-02..06：恢复区 ===================== */
test('RECOVERY-02 empty recovery list shows the empty state', async () => {
	const h = loadGoalLauncher({ listRecoveries: async () => ({ retentionDays: 30, recoveries: [] }) });
	await h.T.openRecoveryPanel(instA());
	const body = h.document.getElementById('recoveryContent');
	assert.match(body.innerHTML, /recovery\.empty|恢复区为空|recovery area is empty/i, 'empty state text expected');
	assert.equal(h.document.querySelector('.rec-restore'), null, 'no restore action without a record');
});

test('RECOVERY-03 normal restore reports success and refreshes the panel', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({ retentionDays: 30, recoveries: [recOp()] }),
		restoreRecovery: async () => ({ ok: true, conflicts: 0 }),
	});
	await h.T.openRecoveryPanel(instA());
	const btn = h.document.querySelector('.rec-restore');
	assert.ok(btn, 'restore button must exist');
	await activate(btn);
	const call = h.calls.filter(c => c.name === 'restoreRecovery').pop();
	assert.ok(call, 'restoreRecovery must be called');
	assert.equal(call.args[0], 'i1');
	assert.equal(call.args[1], 'op-1');
	assert.equal(call.args[2], false, 'a plain delete record must not overwrite');
	assert.equal(countCalls(h, 'listRecoveries'), 2, 'the panel must be refreshed after the restore');
	assert.ok(h.toastLog.some(m => m === h.t('recovery.restored')), 'success toast expected, got: ' + JSON.stringify(h.toastLog));
});

test('RECOVERY-04 partial results list each file with its state', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({
			retentionDays: 30,
			recoveries: [recOp({
				files: [
					{ relativePath: 'ok.jar', state: 'restored' },
					{ relativePath: 'locked.jar', state: 'failed', error: 'file in use' },
				],
			})],
		}),
	});
	await h.T.openRecoveryPanel(instA());
	const body = h.document.getElementById('recoveryContent');
	assert.match(body.innerHTML, /ok\.jar/, 'the restored file must be listed');
	assert.match(body.innerHTML, /locked\.jar/, 'the failed file must be listed');
	assert.match(body.innerHTML, /file in use/, 'the per-file error must be shown');
});

test('RECOVERY-05 conflict result never claims a successful overwrite', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({ retentionDays: 30, recoveries: [recOp({ reason: 'mod-replace' })] }),
		restoreRecovery: async () => ({ ok: false, conflicts: 2 }),
	});
	await h.T.openRecoveryPanel(instA());
	await activate(h.document.querySelector('.rec-restore'));
	assert.ok(h.toastLog.includes(h.t('recovery.conflictNote')), 'the conflict notice must be shown, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.includes(h.t('recovery.undone')), 'a conflict must never claim the update was undone');
	assert.ok(!h.toastLog.includes(h.t('recovery.restored')), 'a conflict must never claim a restore');
});

test('RECOVERY-06 submit disables the button and failure restores it', async () => {
	let rejectRestore;
	const pending = new Promise((_res, rej) => { rejectRestore = rej; });
	const h = loadGoalLauncher({
		listRecoveries: async () => ({ retentionDays: 30, recoveries: [recOp()] }),
		restoreRecovery: () => pending,
	});
	await h.T.openRecoveryPanel(instA());
	const btn = h.document.querySelector('.rec-restore');
	btn.dispatchEvent('click', {});
	await settle(2);
	assert.equal(btn.disabled, true, 'the button must be disabled while the request is in flight');
	const err = new Error('locked-now');
	rejectRestore(err);
	await settle(8);
	assert.equal(btn.disabled, false, 'a failed restore must re-enable the button');
	assert.ok(h.toastLog.some(m => m.indexOf('locked-now') >= 0), 'the failure must be reported, got: ' + JSON.stringify(h.toastLog));
});

/* ===================== BISECT-01/02/04/05 ===================== */
test('BISECT-01 no pending session shows the normal start entry', async () => {
	const h = loadGoalLauncher({ bisectPending: async () => ({ pending: false }) });
	await h.T.openModBisect(instA());
	assert.ok(h.document.getElementById('bStart'), 'the normal start entry must be offered');
	const safety = h.document.querySelector('.bisect-safety');
	assert.ok(safety && safety.innerHTML.length > 0, 'the safety guidance must stay visible');
});

test('BISECT-02 pending session offers restore or continue', async () => {
	const h = loadGoalLauncher({
		bisectPending: async () => ({ pending: true, canContinue: true, createdAt: '2026-10-03T00:00:00Z', rounds: [1, 2], conflicts: [{ fileName: 'x.jar', detail: 'locked' }] }),
	});
	await h.T.openModBisect(instA());
	assert.ok(h.document.getElementById('bResumeRestore'), 'restore entry expected');
	const cont = h.document.getElementById('bResumeContinue');
	assert.ok(cont, 'continue entry expected');
	assert.equal(cont.disabled, false, 'continue must be enabled when the server says it can continue');

	const h2 = loadGoalLauncher({
		bisectPending: async () => ({ pending: true, canContinue: false, createdAt: '2026-10-03T00:00:00Z', rounds: [] }),
	});
	await h2.T.openModBisect(instA());
	assert.equal(h2.document.getElementById('bResumeContinue').disabled, true,
		'continue must be disabled when the server says it cannot continue');
});

test('BISECT-04 file-lock failure keeps the needs-recovery state', async () => {
	const h = loadGoalLauncher({ bisectPending: async () => ({ pending: false }) });
	await h.T.openModBisect(instA());
	const content = h.document.getElementById('bisectContent');
	h.T.drawBisect(content, instA(), {
		enabledAtStart: ['guard.jar'],
		needsRecovery: true,
		recoveryError: 'locked hard',
		lastResults: [{ fileName: 'guard.jar', state: 'failed', error: 'in use' }],
	});
	assert.ok(h.document.getElementById('bRetryRecover'), 'the retry-recovery action must be offered');
	assert.match(content.innerHTML, /guard\.jar/, 'the failing file must be listed');
	assert.match(content.innerHTML, /locked hard/, 'the recovery error must be shown');
	assert.doesNotMatch(content.innerHTML, new RegExp(h.t('bisec.restored')), 'a needs-recovery state must not claim the mods were restored');
});

test('BISECT-05 failed recovery never shows all-complete', async () => {
	const h = loadGoalLauncher({
		bisectPending: async () => ({ pending: false }),
		bisectGiveUp: async () => {
			const e = new Error('still locked');
			// 结构化失败体的形状与 api.js _normalizeBisect 的字段口径一致（含 enabledAtStart），
			// 这样 drawBisect 会走 needsRecovery 分支，而不是退化成"没有可定位的 Mod"。
			e.payload = {
				enabledAtStart: ['guard.jar'],
				needsRecovery: true, recoveryError: 'still locked',
				lastResults: [{ fileName: 'guard.jar', state: 'failed', error: 'in use' }],
			};
			throw e;
		},
	});
	await h.T.openModBisect(instA());
	const content = h.document.getElementById('bisectContent');
	h.T.drawBisect(content, instA(), { enabledAtStart: ['guard.jar'], needsRecovery: true, recoveryError: 'locked', lastResults: [] });
	await activate(h.document.getElementById('bRetryRecover'));
	assert.ok(h.document.getElementById('bRetryRecover'), 'the panel must stay in the needs-recovery state');
	assert.match(content.innerHTML, /still locked/, 'the new recovery error must be shown');
	assert.doesNotMatch(content.innerHTML, new RegExp(h.t('bisec.restored')), 'a failed recovery must never show all-complete');
	// 静态观察（不在本目标的修改范围）：若服务端结构化失败体缺少 enabledAtStart，
	// drawBisect 会先落到"没有可定位的 Mod"空态，而不是 needsRecovery 分支。
	// 本轮只记录，不改服务端协议，也不放宽该断言。
});

/* ===================== IMPORT-01..04 ===================== */
test('IMPORT-01 operationId is bound before cancel is offered', async () => {
	const seen = { cancelBtnAtRequest: null, importOpId: null, cancelOpId: null };
	const h = loadGoalLauncher({
		importInstance: async (name, b64, opId) => {
			seen.cancelBtnAtRequest = !!h.document.getElementById('impCancel');
			seen.importOpId = opId;
			const e = new Error('stop-after-record');
			throw e;
		},
		cancelImport: async (opId) => { seen.cancelOpId = opId; return { cancelled: true }; },
	});
	mountView(h, 'view-home', '');
	await h.T.handleImportFiles([{ name: 'pack.zip' }]);
	assert.equal(seen.cancelBtnAtRequest, true, 'the cancel entry must exist before the request is issued');
	assert.ok(typeof seen.importOpId === 'string' && seen.importOpId.length > 0, 'a non-empty operationId must be sent');
	await activate(h.document.getElementById('impCancel') || { dispatchEvent() {} });
});

test('IMPORT-02 cancel before acceptance reports the cancelled result', async () => {
	const h = loadGoalLauncher({
		importInstance: async () => { const e = new Error('cancelled by user'); e.cancelled = true; throw e; },
	});
	mountView(h, 'view-home', '');
	await h.T.handleImportFiles([{ name: 'pack.zip' }]);
	assert.ok(h.toastLog.includes(h.t('import.cancelled')), 'the cancelled wording is expected, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.some(m => m.indexOf(h.t('import.failed')) === 0), 'a user cancel is not a failure');
});

test('IMPORT-03 cancel after acceptance reports the real server result', async () => {
	let rejectImport;
	const pendingImport = new Promise((_res, rej) => { rejectImport = rej; });
	const h = loadGoalLauncher({
		importInstance: () => pendingImport,
		cancelImport: async () => ({ pendingCancel: true, ok: false }),
	});
	mountView(h, 'view-home', '');
	const running = h.T.handleImportFiles([{ name: 'pack.zip' }]);
	await settle(4);
	await activate(h.document.getElementById('impCancel'));
	assert.ok(h.toastLog.includes(h.t('import.cancelRecorded')), 'the recorded-cancel wording is expected, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.includes(h.t('import.cancelled')), 'it must not claim the import was already cancelled');
	rejectImport(new Error('server still working'));
	await running;
});

test('IMPORT-04 unknown or failed cancel never claims the import was cancelled', async () => {
	let rejectImport;
	const pendingImport = new Promise((_res, rej) => { rejectImport = rej; });
	const h = loadGoalLauncher({
		importInstance: () => pendingImport,
		cancelImport: async () => { throw new Error('unknown-operation'); },
	});
	mountView(h, 'view-home', '');
	const running = h.T.handleImportFiles([{ name: 'pack.zip' }]);
	await settle(4);
	await activate(h.document.getElementById('impCancel'));
	assert.ok(h.toastLog.some(m => m.indexOf(h.t('import.cancelFailed')) === 0), 'the cancel failure must be reported, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.includes(h.t('import.cancelled')), 'a failed cancel must never claim the import was cancelled');
	rejectImport(new Error('stop'));
	await running;
});

/* ===================== DIAGNOSIS-01/02 ===================== */
test('DIAGNOSIS-01 local diagnosis works without an API key', async () => {
	const calls = [];
	const h = loadApiOnly(async (url, init) => {
		calls.push({ url: String(url), init: init || {} });
		return {
			ok: true, status: 200,
			json: async () => ({ reportId: 'local-1', status: 'completed', summary: 'local only', reportText: 'LOCAL' }),
			text: async () => 'LOCAL',
		};
	});
	const r = await h.api.diagnoseInstance('i1', { localOnly: true });
	const body = JSON.parse(calls[0].init.body);
	assert.equal(body.localOnly, true, 'the local-only flag must reach the server');
	assert.equal(body.confirmAiSend, false, 'a local run must not ask to send anything to an AI');
	assert.equal(r.reportId, 'local-1');
	assert.equal(r.reportText, 'LOCAL');
	assert.equal(calls[0].init.headers['X-Pineglow-Token'], 'test-token');
});

test('DIAGNOSIS-02 empty history shows the empty state', async () => {
	const h = loadGoalLauncher({ diagnoseHistory: async () => ({ reports: [], notice: null }) });
	await h.T.openDiagnoseHistory(instA());
	const body = h.document.getElementById('diagHistoryContent');
	assert.match(body.innerHTML, /diag\.historyEmpty|还没有诊断历史记录|No diagnosis history/i, 'empty state text expected');
	assert.equal(h.document.querySelector('[data-export]'), null, 'no export action without a report');
});

/* ===================== UPDATE-01..04 ===================== */
function updateFixture(statusHandler) {
	const h = loadGoalLauncher({ getUpdateStatus: statusHandler });
	const host = h.document.createElement('div');
	host.setAttribute('id', 'updateHost');
	h.document.body.appendChild(host);
	host.innerHTML = '<span id="updateCurVer">…</span><div id="updateStatus" class="update-status"></div><div id="updateTrust" class="update-trust-host"></div>';
	return { h, host };
}

test('UPDATE-01 no previous apply result shows no false alarm', async () => {
	const { h, host } = updateFixture(async () => ({ state: 'idle', progress: 0 }));
	h.T.initUpdateSection(host);
	await settle(6);
	assert.equal(h.document.querySelector('.update-warn'), null, 'no warning without a failed apply result');
	const status = h.document.getElementById('updateStatus');
	assert.ok(!/✅/.test(status.textContent), 'an idle status must not claim success, got: ' + status.textContent);
});

test('UPDATE-02 a previous non-applied update is reported as still old', async () => {
	const { h, host } = updateFixture(async () => ({
		state: 'idle',
		lastApplyResult: { applied: false, result: 'verify-failed', version: '1.0.1' },
		trustNote: '',
	}));
	h.T.initUpdateSection(host);
	await settle(6);
	const warn = h.document.querySelector('.update-warn');
	assert.ok(warn, 'a failed previous apply must produce a warning');
	const warnText = textOf(warn);
	assert.match(warnText, /1\.0\.1/, 'the version that failed must be shown, got: ' + JSON.stringify(warnText));
	assert.match(warnText, new RegExp(h.t('set.lastApplyFailedHint')), 'the hint must explain that the old version is still in use, got: ' + JSON.stringify(warnText));
});

test('UPDATE-03 missing compatibility fields do not crash', async () => {
	const calls = [];
	const h = loadApiOnly(async (url, init) => {
		calls.push({ url: String(url), init: init || {} });
		return { ok: true, status: 200, json: async () => ({ state: 'idle' }), text: async () => '' };
	});
	const s = await h.api.getUpdateStatus();
	assert.equal(s.trustNote, '', 'trustNote must default to an empty string');
	assert.equal(s.lastApplyResult, null, 'lastApplyResult must default to null');
	assert.equal(s.state, 'idle');
	assert.ok(String(calls[0].url).indexOf('/api/update') >= 0);
});

test('UPDATE-04 status query failure never shows update success', async () => {
	const { h, host } = updateFixture(async () => { throw new Error('update-status-transport'); });
	h.T.initUpdateSection(host);
	await settle(6);
	const status = h.document.getElementById('updateStatus');
	const trust = h.document.getElementById('updateTrust');
	assert.ok(!/✅/.test(status.textContent), 'a failed status query must not claim success');
	assert.ok(status.textContent.indexOf(h.t('set.updateDownloaded')) < 0, 'a failed status query must not claim a downloaded update');
	assert.equal(trust.innerHTML, '', 'no trust block without server data');
	assert.ok(!/update-transport/.test(document_text_of(h)), 'the raw error must not be turned into a success claim');
});

function document_text_of(h) {
	return h.document.getElementById('updateStatus').textContent + ' ' + h.document.getElementById('updateTrust').innerHTML;
}


test('INDEPENDENT IMPORT-01 cancel uses the active import operationId', async () => {
 let rejectImport; const pending=new Promise((_r,j)=>{rejectImport=j});
 const h=loadGoalLauncher({importInstance:()=>pending,cancelImport:async()=>({pendingCancel:true,ok:false})});
 mountView(h,'view-home',''); const running=h.T.handleImportFiles([{name:'pack.zip'}]);await settle(4);
 const importCall=h.calls.find(c=>c.name==='importInstance');assert.ok(importCall);
 await activate(h.document.getElementById('impCancel'));
 const cancelCall=h.calls.find(c=>c.name==='cancelImport');assert.ok(cancelCall);
 assert.equal(cancelCall.args[0],importCall.args[2]);rejectImport(new Error('terminal error'));await running;
});
test('INDEPENDENT IMPORT-04 failed cancel followed by ordinary import failure must not claim cancelled', async () => {
 let rejectImport;const pending=new Promise((_r,j)=>{rejectImport=j});
 const h=loadGoalLauncher({importInstance:()=>pending,cancelImport:async()=>{throw new Error('unknown-operation')}});
 mountView(h,'view-home','');const running=h.T.handleImportFiles([{name:'pack.zip'}]);await settle(4);
 await activate(h.document.getElementById('impCancel'));
 assert.ok(!h.toastLog.includes(h.t('import.cancelled')),'precondition: cancel failure itself is correctly reported');
 rejectImport(new Error('ordinary transport failure'));await running;
 console.log('IMPORT-04 terminal toast log: '+JSON.stringify(h.toastLog));
 assert.ok(!h.toastLog.includes(h.t('import.cancelled')),'failed cancel plus unrelated import failure must not claim confirmed cancellation; actual '+JSON.stringify(h.toastLog));
});
test('INDEPENDENT IMPORT-03 pending cancel followed by ordinary import failure must keep outcome unconfirmed', async () => {
 let rejectImport;const pending=new Promise((_r,j)=>{rejectImport=j});
 const h=loadGoalLauncher({importInstance:()=>pending,cancelImport:async()=>({pendingCancel:true,ok:false})});
 mountView(h,'view-home','');const running=h.T.handleImportFiles([{name:'pack.zip'}]);await settle(4);
 await activate(h.document.getElementById('impCancel'));
 assert.ok(h.toastLog.includes(h.t('import.cancelRecorded')));
 rejectImport(new Error('server still working'));await running;
 console.log('IMPORT-03 terminal toast log: '+JSON.stringify(h.toastLog));
 assert.ok(!h.toastLog.includes(h.t('import.cancelled')),'recorded cancellation intent does not prove cancelled when final request fails for an ordinary reason; actual '+JSON.stringify(h.toastLog));
});
