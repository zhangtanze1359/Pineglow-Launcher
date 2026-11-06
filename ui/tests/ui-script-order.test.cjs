'use strict';
/*
 * ui-script-order.test.cjs —— 前端脚本加载清单的一致性门槛（M1-3 第一步）
 *
 * 这条门槛存在的唯一理由：**「哪些脚本、按什么顺序加载」原本写在两处，
 * 拆 launcher.js 时会悄悄漂移。**
 *   - `ui/index.html` 决定生产运行时顺序；
 *   - `ui/tests/helpers/ui-script-order.cjs` 是测试侧的单一来源。
 *
 * 漂移的两个真实后果：
 *   1. 拆出去的视图脚本忘了加进 HTML → 生产环境点了那一页就白屏，而**所有测试都绿**；
 *   2. 顺序写反（比如 api.js 排到 i18n.js 前面）→ `SG.t` 在 api.js 里还不存在，
 *      错误文案退化成 key 字符串；构建与测试同样不会报。
 *
 * 所以本文件做三件事：
 *   ① `SCRIPT_ORDER` 与 `index.html` 的 `<script src>` 顺序必须逐项相同；
 *   ② 每个清单项在磁盘上必须真实存在（"登记了但没文件"要红）；
 *   ③ 反向：`ui/js/` 下的每个 `.js` 都必须被清单包含（"加了文件但没登记"要红）。
 *      反向那条是防"新拆的视图脚本压根没接线"。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..', '..');
const UI = path.join(ROOT, 'ui');
const { SCRIPT_ORDER, toJsRelative, parseIndexHtmlOrder } = require('./helpers/ui-script-order.cjs');

function indexHtml() {
	return fs.readFileSync(path.join(UI, 'index.html'), 'utf8');
}

/* ==================== ① 与 index.html 一致 ==================== */

test('SCRIPT_ORDER 必须与 index.html 的 <script src> 顺序逐项一致', () => {
	const actual = parseIndexHtmlOrder(indexHtml());

	assert.deepEqual(actual, SCRIPT_ORDER,
		'清单与实际加载顺序不一致：请同步 ui/tests/helpers/ui-script-order.cjs（它是唯一来源）');
});

test('index.html 里不存在清单之外的内联业务脚本', () => {
	// 允许内联脚本做"令牌注入"这类引导工作，但不允许把业务逻辑塞进 HTML——
	// 那样它既不在清单里、也没有任何测试能加载到。
	const html = indexHtml();
	const inline = [...html.matchAll(/<script\b(?![^>]*\bsrc\s*=)[^>]*>([\s\S]*?)<\/script>/gi)]
		.map(m => m[1].trim())
		.filter(code => code.length > 0);

	for (const code of inline) {
		// 判定口径：出现 "function " 或 "=>" 且超过 200 字符，就视为业务逻辑
		const looksLikeLogic = code.length > 200 && (/\bfunction\s+\w+\s*\(/.test(code) || /=>/.test(code));
		assert.equal(looksLikeLogic, false,
			'index.html 里出现了较长的内联业务脚本，请移到 ui/js/ 并登记进 SCRIPT_ORDER：\n' + code.slice(0, 200));
	}
});

/* ==================== ② 清单项必须存在 ==================== */

test('清单里每一项在磁盘上都必须存在', () => {
	const missing = [];
	for (const entry of SCRIPT_ORDER) {
		const full = path.join(UI, entry);
		if (!fs.existsSync(full)) missing.push(entry);
	}

	assert.deepEqual(missing, [], '清单登记了但文件不存在：' + missing.join(', '));
});

/* ==================== ③ 反向：js/ 下的文件必须都登记 ==================== */

test('ui/js 下的每个 .js 都必须登记进 SCRIPT_ORDER（防"新拆的脚本没接线"）', () => {
	const onDisk = fs.readdirSync(path.join(UI, 'js'))
		.filter(f => f.endsWith('.js'))
		.sort();

	const declared = SCRIPT_ORDER
		.map(toJsRelative)
		.filter(Boolean)
		.sort();

	assert.deepEqual(onDisk, declared,
		'ui/js 下的文件与 SCRIPT_ORDER 不一致：新增的脚本必须登记，否则生产环境不会加载它');
});

/* ==================== ④ 顺序语义（依赖在依赖者之前）==================== */

test('依赖关系满足：i18n 在 api 之前，api 在 playtime 之前，playtime 在 launcher 之前', () => {
	// 这几条是真实的加载期依赖（api.js 用了 SG.t；playtime.js 用了 SG.api）。
	// 顺序写反时不会抛错，只会让文案/接口在特定路径上失效，属于最难查的那一类。
	const idx = (name) => SCRIPT_ORDER.indexOf('js/' + name);

	assert.ok(idx('i18n.js') < idx('api.js'), 'api.js 使用 SG.t，必须排在 i18n.js 之后');
	assert.ok(idx('api.js') < idx('playtime.js'), 'playtime.js 使用 SG.api，必须排在 api.js 之后');
	assert.ok(idx('playtime.js') < idx('launcher.js'), 'launcher.js 通过 SG.playtime 引用，必须排在其后');
	assert.ok(idx('skin3d-core.js') < idx('skin3d.js'), 'skin3d.js 使用 Skin3DCore，必须排在 core 之后');
	assert.ok(SCRIPT_ORDER.indexOf('vendor/three.min.js') < idx('skin3d.js'), 'skin3d.js 使用全局 THREE，必须排在 vendor 之后');
	assert.equal(idx('launcher.js'), SCRIPT_ORDER.length - 1, 'launcher.js 是主壳，必须最后加载');
});
