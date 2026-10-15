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
