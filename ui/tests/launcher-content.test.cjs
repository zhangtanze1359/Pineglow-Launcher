'use strict';
/*
 * launcher.js 资源/搜索契约的静态断言
 * --------------------------------------------------------------------------
 * 为什么是静态断言而不是运行 launcher.js？
 *   launcher.js（2386 行）耦合到完整 WebView DOM、皮肤 3D、toast、tab 路由等，
 *   完整加载会需要数百行 jsdom 级别 stub。Phase 1 关心的 3 个具体缺陷
 *   （contentSource 作用域、卡片身份、竞态 token）都是局部、确定的代码模式，
 *   用静态断言 + 关键字符串检查足以在回归时把它们打回去。
 *
 * 覆盖的回归：
 *   1. searchContentOptions 必须返回 source 字段（不允许 api.js 再依赖私有变量）
 *   2. contentCardHtml 必须给 .mod-card / .mod-install 添加 data-source / data-category
 *   3. bindContentInstalls 必须从 button.dataset 读取 source/category，不能用全局
 *   4. renderContent 与 bindContent 的 rerenderGrid 在 await 之后必须再检查 token
 *      （A 慢 B 快不能被 A 覆盖）
 *   5. proxiedNewsImageUrl 仍然使用 /api/news-image（这是修复计划后续阶段覆盖）
 */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');

let passed = 0, failed = 0;
const failures = [];
function test(name, fn) {
	try { fn(); passed++; process.stdout.write(`PASS ${name}\n`); }
	catch (err) { failed++; failures.push({ name, err }); process.stdout.write(`FAIL ${name}: ${err && err.message}\n`); }
}

/* helper: 找两个索引之间、并且不与其它成对花括号嵌套冲突的子串 */
function findBalanced(source, openIdx, open, close) {
	let depth = 0;
	for (let i = openIdx; i < source.length; i++) {
		const ch = source[i];
		if (ch === open) depth++;
		else if (ch === close) { depth--; if (depth === 0) return i; }
	}
	return -1;
}

/* helper: 取函数体（从 `function NAME(` 到配对 `}`） */
function extractFunctionBody(source, signature) {
	const start = source.indexOf(signature);
	if (start < 0) throw new Error('signature not found: ' + signature);
	const braceStart = source.indexOf('{', start);
	if (braceStart < 0) throw new Error('open brace not found for: ' + signature);
	const close = findBalanced(source, braceStart, '{', '}');
	if (close < 0) throw new Error('close brace not found for: ' + signature);
	return source.slice(start, close + 1);
}

/* ============== 1. contentSource 作用域 ============== */
test('searchContentOptions 包含 source 字段', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function searchContentOptions()');
	assert.match(body, /source\s*:\s*contentSource/, 'searchContentOptions 必须把当前 contentSource 透传给 api.js');
});

test('searchContentOptions 仍透传 gameVersion/loader', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function searchContentOptions()');
	assert.match(body, /gameVersion\s*:/);
	assert.match(body, /loader\s*:/);
});

test('api.js searchContent 不再引用外部 contentSource 变量', () => {
	const api = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
	// 抓出 searchContent 异步方法体
	const start = api.indexOf('async searchContent(query, category, options)');
	if (start < 0) throw new Error('searchContent method not found');
	const brace = api.indexOf('{', start);
	const close = findBalanced(api, brace, '{', '}');
	const body = api.slice(start, close + 1);
	assert.doesNotMatch(body, /\bcontentSource\b/, 'api.js 不应再裸引用 contentSource 私有变量');
	assert.match(body, /opt\.source/, '必须从 opt.source 显式取值');
	assert.match(body, /source\s*=\s*\(opt\.source\s*&&/, '默认值回退到 modrinth');
});

/* ============== 2. contentCardHtml 身份字段 ============== */
test('contentCardHtml 渲染 .mod-card 时带 data-source', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	assert.match(body, /<div class="mod-card"[^>]*data-source=/, 'mod-card 必须有 data-source');
});

test('contentCardHtml 渲染 .mod-card 时带 data-category', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	assert.match(body, /<div class="mod-card"[^>]*data-category=/, 'mod-card 必须有 data-category');
});

test('contentCardHtml 给安装按钮带 data-source / data-category', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	assert.match(body, /<button class="mod-install"[^>]*data-source=/, '安装按钮必须有 data-source');
	assert.match(body, /<button class="mod-install"[^>]*data-category=/, '安装按钮必须有 data-category');
});

test('contentCardHtml 拒绝对 source/category 做空白字符串透传', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function contentCardHtml(');
	// 不允许把空串、未定义值直接写进 data-source
	assert.match(body, /cardSource\s*=\s*\(c\.source\s*&&\s*\/\^/);
	assert.match(body, /cardCategory\s*=\s*\(c\.category\s*&&\s*\/\^/);
});

/* ============== 3. bindContentInstalls 使用卡片自身 source/category ============== */
test('bindContentInstalls 从 card.dataset.source 读取 source', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContentInstalls(el)');
	// 实际代码用 card.dataset.source 兜底（按钮上的 data-source 优先级最高，缺失时回到卡片）；
	// 关键是不允许直接把全局 contentSource 透传给 installContent。
	assert.match(body, /card\.dataset\.source/);
	assert.doesNotMatch(body, /source:\s*contentSource\b(?!\s*\?)/, '不得把全局 contentSource 直接透传给 installContent');
});

test('bindContentInstalls 从 button.dataset.category 读取 category', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContentInstalls(el)');
	assert.match(body, /b\.dataset\.category/);
});

test('installContent 实际调用透传 source 字段', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'function bindContentInstalls(el)');
	assert.match(body, /SG\.api\.installContent\(\s*\{\s*category,\s*projectId,\s*instanceId:\s*selectedInstanceId,\s*source\s*\}\s*\)/);
});

/* ============== 4. 竞态 token 检查 ============== */
test('renderContent 在 await 之后（成功路径）检查 token', () => {
	const body = extractFunctionBody(LAUNCHER_JS, 'async function renderContent()');
	const awaitIdx = body.indexOf('await SG.api.searchContent');
	if (awaitIdx < 0) throw new Error('searchContent call not found in renderContent');
	const after = body.slice(awaitIdx);
	// 抓出 catch 块结束位置（最近的 `}` 配对）
	const catchStart = after.indexOf('catch (e)');
	if (catchStart < 0) throw new Error('catch block not found');
	const catchOpen = after.indexOf('{', catchStart);
	const catchClose = findBalanced(after, catchOpen, '{', '}');
	const tail = after.slice(catchClose + 1);
	assert.match(tail, /contentSearchToken/, 'catch 之后必须再次检查 contentSearchToken（成功路径）');
});

test('bindContent 的 grid 重渲染在 await 之后（成功路径）检查 token', () => {
	// v4：rerenderGrid 提升为模块级 contentRerenderGrid
	const body = extractFunctionBody(LAUNCHER_JS, 'async function contentRerenderGrid(el)');
	const awaitIdx = body.indexOf('await SG.api.searchContent');
	if (awaitIdx < 0) throw new Error('searchContent call not found in contentRerenderGrid');
	const after = body.slice(awaitIdx);
	const catchStart = after.indexOf('catch (e)');
	const catchOpen = after.indexOf('{', catchStart);
	const catchClose = findBalanced(after, catchOpen, '{', '}');
	const tail = after.slice(catchClose + 1);
	assert.match(tail, /contentSearchToken/, 'contentRerenderGrid 的 catch 之后必须再次检查 token');
});

test('contentRerenderGrid 切换分类后立即同步 source switch 可见性', () => {
	// v4：rerenderGrid 提升为模块级 contentRerenderGrid
	const body = extractFunctionBody(LAUNCHER_JS, 'async function contentRerenderGrid(el)');
	assert.match(body, /#contentSourceSwitch/, 'contentRerenderGrid 内必须操作 #contentSourceSwitch');
	assert.match(body, /contentCat === 'mod'/, '可见性应基于当前 contentCat');
});

/* ============== 5. 文档契约（防止再次改回错误的 silent fallback） ============== */
test('api.js wrapped 层默认不允许 mock 回退（allowMockFallback 必须 false）', () => {
	const api = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
	const wrapStart = api.indexOf('Object.keys(impl).forEach');
	if (wrapStart < 0) throw new Error('wrap block not found');
	const brace = api.indexOf('{', wrapStart);
	const close = findBalanced(api, brace, '{', '}');
	const block = api.slice(wrapStart, close + 1);
	// 必须显式依赖 allowMockFallback
	assert.match(block, /allowMockFallback/, 'wrapped 块必须基于 allowMockFallback 判断');
	// 旧代码使用裸 !isLive || isReadOnly 当作默认可回退条件，必须删除
	assert.doesNotMatch(block, /if\s*\(\s*isLive\s*&&\s*isReadOnly/, '旧的"live && isReadOnly 自动回退 mock"必须移除');
});

test('api.js wrapped 层 catch 必须把错误透传（_liveError 字段）', () => {
	const api = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
	const block = api.slice(api.indexOf('Object.keys(impl).forEach'));
	assert.match(block, /_liveError/, 'fallback 路径必须把原始错误以 _liveError 形式挂到结果上');
});

/* ============== 输出汇总 ============== */
process.on('exit', () => {
	process.stdout.write(`\nlauncher-content.test.cjs: passed=${passed} failed=${failed}\n`);
	if (failed > 0) {
		for (const f of failures) process.stdout.write(`- ${f.name}: ${f.err && f.err.message}\n`);
		process.exitCode = 1;
	}
});
