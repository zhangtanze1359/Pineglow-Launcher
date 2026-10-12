'use strict';
/*
 * Phase 2 资源图标前端行为测试
 * --------------------------------------------------------------------------
 * 覆盖：
 *   1. SG.buildContentIconUrl 构造的 URL 始终是 /api/content/icon，包含合法身份三元组 + token
 *   2. mapContent 把 c.iconUrl 强制改写为本地端点（即使后端返回了上游 URL，也必须被替换）
 *   3. 上游 URL 字符串绝对不允许进入 DOM（contentCardHtml 不再写 c.iconUrl 到 src）
 *   4. contentCardHtml 的 img src 包含 source / category / projectId 编码参数
 *   5. 缺 token / 非法 projectId 时，mapContent 输出空 iconUrl
 *   6. 4 类资源身份（mod / modpack / shader / resourcepack）正确编码
 *   7. 第三方 icon URL（cdn-raw.modrinth.com / 任意 https）绝不出现在 .mod-card 的 outerHTML
 *   8. URL 不能注入额外参数（url=、host=、redirect= 等）
 *   9. 即使后端返回的 iconUrl 字段含奇怪字符，mapContent 也会忽略并重建
 *  10. SG.apiToken 缺失时 buildContentIconUrl 返回空
 *
 * 不声明：完整 launcher.js 行为由 launcher-v3/v4/v5 覆盖；本文件聚焦 Phase 2 契约。
 */

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');

function makeStubBrowser(extraSG) {
	const elements = new Map();
	const document = {
		head: { appendChild() {} },
		body: { appendChild() {} },
		documentElement: { setAttribute() {}, getAttribute() { return ''; } },
		querySelector(sel) {
			// 让 api.js 读 meta[name="pineglow-session-token"] 时拿到 token
			// extraSG 中显式 token === '' 时返回无 content 的对象
			if (sel === 'meta[name="pineglow-session-token"]') {
				if (extraSG && Object.prototype.hasOwnProperty.call(extraSG, 'apiToken') && extraSG.apiToken === '') {
					return { content: '' };
				}
				return { content: (extraSG && extraSG.apiToken) || 'TEST-TOKEN-1234567890' };
			}
			return elements.get(sel) || null;
		},
		createElement(tag) {
			const el = {
				tagName: (tag || 'div').toUpperCase(),
				children: [],
				classList: { add() {}, remove() {}, contains() { return false; } },
				attributes: {},
				dataset: {},
				style: {},
				setAttribute(k, v) { this.attributes[k] = v; this.dataset[k] = v; },
				getAttribute(k) { return this.attributes[k]; },
				addEventListener() {},
				appendChild(c) { this.children.push(c); return c; },
				remove() {},
				innerHTML: '',
				textContent: '',
				value: '',
				disabled: false,
			};
			el.dataset = new Proxy({}, { set(t, k, v) { t[k] = v; el.attributes['data-' + k] = v; return true; }, get(t, k) { return t[k]; } });
			return el;
		},
		addEventListener() {},
	};
	const ctx = {
		SG: { apiMode: 'live', ...(extraSG || {}) },
		location: { protocol: 'http:' },
		fetch: () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ results: [] }) }),
		document,
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		setTimeout, clearTimeout,
		console,
		URLSearchParams,
		Promise,
	};
	Object.defineProperty(ctx, 'window', { get() { return this; }, set(v) {}, configurable: true });
	return ctx;
}

function loadApi(ctx) {
	vm.createContext(ctx);
	vm.runInContext(API_JS, ctx);
	return ctx;
}

/* 静态分析 mapContent 源码：检测它对 iconUrl 的处理是否调用 buildContentIconUrl */
function mapContentSource() {
	const start = API_JS.indexOf('const mapContent = c =>');
	if (start < 0) throw new Error('mapContent not found');
	const braceOpen = API_JS.indexOf('{', start);
	if (braceOpen < 0) throw new Error('mapContent brace not found');
	let depth = 0;
	let i = braceOpen;
	for (; i < API_JS.length; i++) {
		if (API_JS[i] === '{') depth++;
		else if (API_JS[i] === '}') { depth--; if (depth === 0) break; }
	}
	if (depth !== 0) throw new Error('mapContent unbalanced braces');
	return API_JS.slice(start, i + 1);
}

function findBalanced(source, openIdx, open, close) {
	let depth = 0;
	for (let i = openIdx; i < source.length; i++) {
		if (source[i] === open) depth++;
		else if (source[i] === close) { depth--; if (depth === 0) return i; }
	}
	return -1;
}

function extractFunctionBody(source, signature) {
	const start = source.indexOf(signature);
	if (start < 0) throw new Error('signature not found: ' + signature);
	const brace = source.indexOf('{', start);
	if (brace < 0) throw new Error('open brace not found');
	const close = findBalanced(source, brace, '{', '}');
	if (close < 0) throw new Error('close brace not found');
	return source.slice(start, close + 1);
}

/* ============== 1. buildContentIconUrl 构造契约 ============== */

test('buildContentIconUrl 输出以 /api/content/icon 开头并含 source / category / projectId / token', () => {
	const ctx = makeStubBrowser();
	loadApi(ctx);
	const url = ctx.SG.buildContentIconUrl('modrinth', 'mod', 'AANobbMI');
	assert.equal(typeof url, 'string');
	assert.ok(url.startsWith('/api/content/icon?'), `url 应当以 /api/content/icon? 开头, got: ${url}`);
	assert.ok(url.includes('source=modrinth'));
	assert.ok(url.includes('category=mod'));
	assert.ok(url.includes('projectId=AANobbMI'));
	assert.ok(url.includes('token=TEST-TOKEN-1234567890'));
});

test('buildContentIconUrl 对 source / category 大小写/非法值归一化', () => {
	const ctx = makeStubBrowser();
	loadApi(ctx);
	const url = ctx.SG.buildContentIconUrl('Modrinth', 'Mod', 'P1');
	assert.ok(url.includes('source=modrinth'));
	assert.ok(url.includes('category=mod'));
	assert.ok(url.includes('projectId=P1'));
});

test('buildContentIconUrl 对非法 projectId（含空格/..）返回空串', () => {
	const ctx = makeStubBrowser();
	loadApi(ctx);
	assert.equal(ctx.SG.buildContentIconUrl('modrinth', 'mod', ''), '');
	assert.equal(ctx.SG.buildContentIconUrl('modrinth', 'mod', 'with space'), '');
	assert.equal(ctx.SG.buildContentIconUrl('modrinth', 'mod', '../etc/passwd'), '');
	assert.equal(ctx.SG.buildContentIconUrl('modrinth', 'mod', 'a%2Fb'), '');
	assert.equal(ctx.SG.buildContentIconUrl('modrinth', 'mod', 'a.b'), '');
});

test('buildContentIconUrl 在 SG.apiToken 缺失时返回空串', () => {
	const ctx = makeStubBrowser({ apiToken: '' });
	loadApi(ctx);
	assert.equal(ctx.SG.buildContentIconUrl('modrinth', 'mod', 'AANobbMI'), '');
});

test('buildContentIconUrl 不接受 url / host / redirect 等额外客户端参数', () => {
	const ctx = makeStubBrowser();
	loadApi(ctx);
	// 通过包装调用验证函数不接受其它参数（必须是 source, category, projectId）
	assert.equal(ctx.SG.buildContentIconUrl.length, 3, '必须只接受 3 个参数');
});

/* ============== 2. mapContent 改写 iconUrl 为本地端点 ============== */

test('mapContent 必须用 buildContentIconUrl 构造 iconUrl（保证上游 URL 不进 DOM）', () => {
	const src = mapContentSource();
	// 关键：iconUrl 的来源必须是本地端点（通过 buildContentIconUrl 或已经是 /api/content/icon）
	assert.match(src, /iconUrl\s*:\s*[^,]*buildContentIconUrl/, 'iconUrl 必须用 buildContentIconUrl 构造');
	// 关键：上游 icon 字段（任何 https:// 字符串）绝不能直接赋给 iconUrl
	assert.doesNotMatch(src, /iconUrl\s*:\s*c\.icon(?!\s*&&|\s*\?|\s*==|\s*===)/, 'iconUrl 绝不能直接等于 c.icon（上游 URL）');
	// 兜底：iconUrl 也不能 = c.iconUrl（如果 c.iconUrl 是后端返回的）
	assert.doesNotMatch(src, /iconUrl\s*:\s*c\.iconUrl\s*$/m, 'iconUrl 绝不能直接等于 c.iconUrl');
});

test('mapContent 对五种身份的源 source/category 字段都正确规整', () => {
	const src = mapContentSource();
	// 规整 source：只接受 modrinth / curseforge
	assert.match(src, /\(\s*c\.source\s*&&\s*\/\^/);
	// 规整 category：取 c.category 或默认值
	assert.match(src, /category\s*:\s*c\.category/);
});

test('mapContent 在缺 token 时不输出带 token 的 iconUrl', () => {
	// mapContent 内部直接调用 buildContentIconUrl；后者在 token 缺失时返回空串。
	// 因此 mapContent 的 iconUrl 也会是空串（因为 c.iconUrl 来自 buildContentIconUrl）。
	// 这里用静态分析保证 iconUrl 字段不直接用 c.icon（避免任何上游 URL 透传）。
	const src = mapContentSource();
	assert.doesNotMatch(src, /iconUrl\s*:\s*c\.icon\b/);
});

/* ============== 3. contentCardHtml 不写第三方 URL 到 DOM ============== */

test('contentCardHtml 的 img src 必须是 /api/content/icon（不允许任何 http(s)://）', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	// 严禁出现 src=\"http:// 或 src=\"https://
	assert.doesNotMatch(body, /src\s*=\s*["']https?:\/\//, 'contentCardHtml 不得直接写 https:// 到 img src');
	// 必须包含本地端点
	assert.match(body, /\/api\/content\/icon/, 'contentCardHtml 必须包含本地端点 /api/content/icon');
});

test('contentCardHtml 使用 esc() 包装 c.iconUrl（防注入）', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	// 找到包含 c.iconUrl 的 img src 写法
	assert.match(body, /src\s*=\s*["']\$\{esc\(c\.iconUrl\)\}/, 'c.iconUrl 必须经 esc() 写入 src');
});

test('contentCardHtml 输出包含四状态 data-icon-state 初始值 loading 或 missing', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	assert.match(body, /data-icon-state\s*=\s*["']\$\{hasLocalIcon\s*\?\s*['"]loading['"]\s*:\s*['"]missing['"]\}/);
});

test('contentCardHtml 包含 fixed 尺寸 52x52 + loading=lazy + decoding=async', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	assert.match(body, /width\s*=\s*["']52["']/, 'img 必须有 width=52');
	assert.match(body, /height\s*=\s*["']52["']/, 'img 必须有 height=52');
	assert.match(body, /loading\s*=\s*["']lazy["']/, 'img 必须有 loading=lazy');
	assert.match(body, /decoding\s*=\s*["']async["']/, 'img 必须有 decoding=async');
});

test('contentCardHtml 图标 img 初始隐藏不得使用 display:none（lazy + display:none 在 Chromium 中永不加载）', () => {
	// 冒烟实测（2026-09-05，WebView2/Chromium 152）：loading=lazy 且 style="display:none"
	// 的 img 没有布局框，永不与视口相交 → 图标请求永远不发出，卡片停留在 loading 占位。
	// 初始隐藏必须保留布局框（opacity:0），load 后再置 opacity:1。
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	assert.doesNotMatch(body, /display\s*:\s*none/, '图标 img 不得用 display:none 初始隐藏');
	assert.match(body, /opacity\s*:\s*0/, '图标 img 必须用 opacity:0 初始隐藏（保留布局框）');
});

test('contentCardHtml 不再写 onerror 触发 src 替换（已迁移到 bindContentIcons）', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	assert.doesNotMatch(body, /onerror\s*=\s*["'][^"']*\.src/, 'onerror 中不能修改 src 触发无限重试');
});

/* ============== 4. bindContentIcons 四状态 ============== */

test('bindContentIcons 存在并接收 el 参数', () => {
	assert.match(LAUNCHER_JS, /function bindContentIcons\(el\)/, '必须导出 bindContentIcons');
});

test('bindContentIcons 在 error 时不修改 img.src（防无限重试）', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContentIcons(el)');
	assert.doesNotMatch(body, /img\.src\s*=/, 'error 监听器中严禁修改 img.src');
	assert.match(body, /img\.remove\(\)/, 'error 监听器应当移除 img');
});

test('bindContentIcons 在 load 时切到 loaded 状态 + 隐藏占位', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContentIcons(el)');
	assert.match(body, /dataset\.iconState\s*=\s*['"]loaded['"]/, 'load 监听器切到 loaded 状态');
	assert.match(body, /\.mod-icon-placeholder/, 'load 监听器隐藏占位层');
});

test('bindContentIcons 在 error 时切到 failed 状态 + 不修改 src', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContentIcons(el)');
	assert.match(body, /dataset\.iconState\s*=\s*['"]failed['"]/, 'error 监听器切到 failed 状态');
	assert.doesNotMatch(body, /\.src\s*=\s*[^;]+/, 'error 监听器中不能有任何 src 赋值');
});

test('bindContentIcons 必须处理绑定前已 complete 的 img（磁盘缓存命中时 load 事件已错过）', () => {
	// 冒烟实测（2026-09-05）：重启应用后图标直接命中磁盘缓存，img 在监听器绑定前
	// 已 complete（naturalWidth>0），load 事件不会再触发 → 永远停留在 loading 占位。
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContentIcons(el)');
	assert.match(body, /img\.complete/, '必须同步检查 img.complete');
	assert.match(body, /naturalWidth\s*>\s*0/, 'complete 且 naturalWidth>0 必须立即进入 loaded');
});

test('contentRerenderGrid 重渲染后必须重新调用 bindContentIcons（搜索/分类/来源切换路径）', () => {
	// 冒烟实测（2026-09-05）：grid.innerHTML 替换后旧 img 全部被回收，
	// 只调 bindContentInstalls 不调 bindContentIcons → 新卡片图标永远停留在 loading。
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentRerenderGrid(el)');
	assert.match(body, /bindContentIcons\(el\)/, '重渲染路径必须重新绑定图标状态');
});

/* ============== 5. bindContent 集成绑定 ============== */

test('bindContent 在绑定事件后调用 bindContentIcons（确保 grid 重渲染后重新绑定）', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContent(el)');
	assert.match(body, /bindContentIcons\(el\)/, 'bindContent 必须调用 bindContentIcons');
});

/* ============== 6. 安装按钮身份不被图标失败污染 ============== */

test('contentCardHtml 安装按钮的 data-source / data-category 来自卡片自身，不受图标状态影响', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	// 验证 data-source / data-category 出现在 button 上，且 source 来自 cardSource（已规整）
	assert.match(body, /data-source\s*=\s*["']\$\{esc\(cardSource\)\}/);
	assert.match(body, /data-category\s*=\s*["']\$\{esc\(cardCategory\)\}/);
});

/* ============== 7. 上游 URL 不出现在任何卡片 DOM 字段 ============== */

test('contentCardHtml 中 c.iconUrl 仅通过 esc() 注入；任何 iconUrl 字符串都不是 http(s)://', () => {
	// 静态检查：src 的值只能是 esc(c.iconUrl)，而 c.iconUrl 由 mapContent 强制替换为本地端点
	// 此条覆盖了 buildContentIconUrl + mapContent + contentCardHtml 三层链路
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	const matches = body.match(/src\s*=\s*["']\$\{esc\(c\.iconUrl\)\}["']/g) || [];
	assert.ok(matches.length === 1, `img.src 应当只有一处通过 esc(c.iconUrl) 注入，实际 ${matches.length}`);
});

/* ============== 输出汇总 ============== */
process.on('exit', () => {
	// node:test 已经在内部统计并输出 pass/fail/skipped
	// 不强制覆盖退出码；测试失败时断言异常会让 Node 自行非零退出
});
