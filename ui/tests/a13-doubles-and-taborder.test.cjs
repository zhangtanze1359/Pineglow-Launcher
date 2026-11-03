'use strict';
/*
 * MR17 补测（缺口登记表 A13）：
 *   ① 其它 makeEl 替身**按同一口径补齐** —— 并做成守卫，防止再次漏成员；
 *   ② 六类界面的 **Tab 顺序静态清单**（卡片要求"走六类界面记录 Tab 顺序"，此前只保证了弹窗内顺序）。
 *
 * 为什么是"静态清单"而不是真实浏览器遍历：本仓库的 Node 测试跑在极简假 DOM 上，
 * 没有真实布局/焦点引擎；真实逐屏 Tab 遍历需要浏览器（见本包 deliverable §五）。
 * 静态清单的价值在于：**把顺序固定下来**，任何调整侧栏/顶栏顺序的改动都会被这条用例标出来。
 */
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const repoRoot = path.resolve(__dirname, '..', '..');
const testsDir = path.join(repoRoot, 'ui', 'tests');
const htmlPath = path.join(repoRoot, 'ui', 'index.html');

/* ===================== ① makeEl 替身必须支持产品实际调用的 DOM 成员 ===================== */

/** launcher.js 实际调用的、而极简假 DOM 容易漏掉的成员（MR17 那次就是漏了它们）。 */
const REQUIRED_ON_DOUBLES = ['focus', 'removeAttribute', 'closest', 'matches'];

function doubleFiles() {
	return fs.readdirSync(testsDir).filter(f => f.endsWith('.test.cjs'));
}

function definesMakeEl(source) {
	return /function\s+makeEl\s*\(/.test(source);
}

test('A13-①：每个定义了 makeEl 的测试文件，其替身都必须支持产品调用的 DOM 成员', () => {
	const missing = [];
	for (const file of doubleFiles()) {
		const source = fs.readFileSync(path.join(testsDir, file), 'utf8');
		if (!definesMakeEl(source)) continue;

		for (const member of REQUIRED_ON_DOUBLES) {
			// 成员可能写成方法 `focus(` 或属性 `offsetParent:`
			const asMethod = new RegExp('\\b' + member + '\\s*\\(');
			const asProp = new RegExp('\\b' + member + '\\s*:');
			if (!asMethod.test(source) && !asProp.test(source)) {
				missing.push(file + ' 缺少 ' + member);
			}
		}
	}

	assert.deepStrictEqual(missing, [],
		'以下 makeEl 替身缺少产品代码实际调用的 DOM 成员（应按 ai-diagnose 的同一口径补齐）：\n  ' +
		missing.join('\n  '));
});

test('A13-①（反向核对）：产品代码确实调用了这些成员 —— 否则上面的要求就是空谈', () => {
	const jsDir = path.join(repoRoot, 'ui', 'js');
	const sources = fs.readdirSync(jsDir).filter(f => f.endsWith('.js'))
		.map(f => fs.readFileSync(path.join(jsDir, f), 'utf8')).join('\n');

	for (const member of ['focus', 'removeAttribute']) {
		const n = (sources.match(new RegExp('\\.' + member + '\\s*\\(', 'g')) || []).length;
		assert.ok(n > 0, '产品代码里应至少有一处 .' + member + '() —— 否则不该要求替身支持它');
	}
});

/* ===================== ② 六类界面的 Tab 顺序静态清单 ===================== */

/** 从 index.html 按**文档顺序**抽出静态可聚焦元素。 */
function staticFocusables(html) {
	const focusableTags = new Set(['button', 'a', 'input', 'select', 'textarea']);
	const out = [];
	const re = /<([a-z][a-z0-9]*)\b([^>]*)>/g;
	let m;
	while ((m = re.exec(html)) !== null) {
		const tag = m[1];
		const attrs = m[2];
		// 可聚焦 = 原生可聚焦标签 **或** 显式带 tabindex（例如承载视图的容器）
		if (!focusableTags.has(tag) && !/\btabindex\s*=/.test(attrs)) continue;

		const idM = attrs.match(/\bid="([^"]+)"/);
		const navM = attrs.match(/\bdata-nav="([^"]+)"/);
		const clsM = attrs.match(/\bclass="([^"]+)"/);
		out.push({
			tag,
			id: idM ? idM[1] : '',
			nav: navM ? navM[1] : '',
			cls: clsM ? clsM[1] : '',
			tabindex: /\btabindex\s*=/.test(attrs),
		});
	}
	return out;
}

test('A13-②：新版十页导航的 Tab 顺序', () => {
	const html = fs.readFileSync(htmlPath, 'utf8');
	const navOrder = staticFocusables(html)
		.filter(e => e.nav)
		.map(e => e.nav);

	assert.deepStrictEqual(navOrder, [
		'home',
		'installations',
		'worlds',
		'servers',
		'playtime',
		'content',
		'skins',
		'downloads',
		'news',
		'settings',
	], '侧栏导航的 Tab 顺序变了 —— 请同步更新本清单与可访问性说明');
});

test('A13-②：静态 chrome 的可聚焦元素顺序（跳转链接 → 窗口控制 → 侧栏 → 语言切换）', () => {
	const html = fs.readFileSync(htmlPath, 'utf8');
	const seq = staticFocusables(html).map(e => {
		if (e.cls.includes('skip-link')) return 'skip-link';
		if (e.id) return e.id;
		if (e.nav) return 'nav:' + e.nav;
		return e.tag + ':' + e.cls.split(/\s+/)[0];
	});

	// 前 4 项：跳转链接、最小化、最大化、关闭（窗口控制属于顶栏，排在侧栏之前）
	assert.deepStrictEqual(seq.slice(0, 4), ['skip-link', 'btnMinimize', 'btnMaximize', 'btnClose'],
		'静态 chrome 的前四个可聚焦元素应是：跳转链接 → 最小化 → 最大化 → 关闭');

	// 语言切换必须存在且在侧栏之后
	const langIdx = seq.indexOf('btnLangZh');
	assert.ok(langIdx > 0, '应存在独立中文按钮');
	assert.ok(langIdx > seq.indexOf('nav:home'), '语言切换应排在侧栏之后（与 index.html 的文档顺序一致）');
	assert.ok(seq.indexOf('btnLangEn') > langIdx, '英文按钮紧随中文按钮');
	assert.ok(seq.indexOf('btnProfile') < seq.indexOf('nav:home'), '账户入口排在导航前');
	assert.ok(seq.indexOf('btnQuickOpen') > seq.indexOf('btnLangEn'), '主区快速操作排在侧栏后');

	// 完整性：静态可聚焦元素共 15 个（含 view 容器上的 tabindex）
	assert.strictEqual(staticFocusables(html).length, 20,
		'新版静态可聚焦元素数量变了（记录值 20）：' + JSON.stringify(seq));
});
