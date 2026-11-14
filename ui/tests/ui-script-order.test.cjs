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

/* ==================== ⑤ 拆出去的视图脚本必须真的被生产加载 ==================== */

/*
 * 由来（M1-3 第二步→第三步的真实事故）：
 *   把视图拆到独立文件后，**只有清单和 index.html 同步改了，生产才会加载它**。
 *   已经踩过两次：拆 news.js 时 ui/tests/helpers/live-launcher.cjs 的硬编码数组漏了一行，
 *   拆 downloads.js 时同一个数组又漏了一行。两次的症状都不是报错，而是
 *   `TypeError: Cannot read properties of undefined (reading '__wire')` —— 看起来像
 *   "某个测试的环境问题"，实际是加载清单漂移，很容易被当成噪音忽略过去。
 *
 * 本组断言把这件事变成可机械检查的：拆出去的视图脚本
 *   ① 在 SCRIPT_ORDER 里（且排在 launcher.js 之前）；
 *   ② 在 index.html 里（且排在 launcher.js 之前的同一个位置）；
 *   ③ 自身导出了对应的 `SG.<模块名>` 命名空间（否则 launcher.js 的 __wire 调用会炸）。
 */

/* 文件名 → SG 命名空间。多数模块同名，但 skin-visuals.js 的命名空间是
 * SG.skinVisuals（连字符在 JS 标识符里非法），必须显式登记，不能靠文件名推导。 */
const NS = { playtime: 'playtime', news: 'news', downloads: 'downloads', skins: 'skins', content: 'content', settings: 'settings', 'skin-visuals': 'skinVisuals' };
const VIEW_MODULES = ['playtime', 'news', 'downloads', 'skins', 'content', 'settings'];
/* 跨视图共享层的模块：没有独立的 render/bind 入口，但同样必须被生产加载。
 * 单独列出来是因为上面那组断言要求 render+bind 委托，对共享层不成立。 */
const SHARED_MODULES = ['skin-visuals'];
/* 模块内部挂命名空间用的局部别名（news/downloads/skin-visuals 用 `const SG = root.SG`
 * 后 `SG.x = {...}`；skins.js 用 `SG_NS` 以避免与同名局部变量冲突）。 */
const NS_ASSIGN = name => new RegExp("(SG|SG_NS|root\\.SG)\\." + NS[name] + "\\s*=\\s*\\{");

test('拆出去的视图脚本都登记在清单里，且排在 launcher.js 之前', () => {
	for (const name of [...VIEW_MODULES, ...SHARED_MODULES]) {
		const entry = 'js/' + name + '.js';
		assert.ok(SCRIPT_ORDER.includes(entry), `${entry} 未登记进 SCRIPT_ORDER：生产环境不会加载它，拆出的视图将整页失效`);
		assert.ok(SCRIPT_ORDER.indexOf(entry) < SCRIPT_ORDER.indexOf('js/launcher.js'),
			`${entry} 必须排在 launcher.js 之前：launcher.js 启动时会向 SG.${name} 注入基础设施`);
	}
});

test('拆出去的视图脚本在 index.html 里也真实加载，且位于 launcher.js 之前', () => {
	const declared = parseIndexHtmlOrder(indexHtml());
	const launcherAt = declared.indexOf('js/launcher.js');

	assert.notEqual(launcherAt, -1, 'index.html 必须加载 launcher.js');
	for (const name of [...VIEW_MODULES, ...SHARED_MODULES]) {
		const entry = 'js/' + name + '.js';
		const at = declared.indexOf(entry);
		assert.notEqual(at, -1, `index.html 未加载 ${entry}：测试全绿但生产点开该页会白屏`);
		assert.ok(at < launcherAt, `${entry} 必须排在 launcher.js 之前（launcher 启动时向它注入依赖）`);
	}
});

test('拆出去的视图脚本各自导出 SG.<模块名>，且与 launcher.js 的 __wire 调用同名', () => {
	// 拆分协议：视图脚本挂在 SG.<name> 上，launcher.js 通过 SG.<name>.__wire 注入基础设施。
	// 两边任一侧改名而另一侧没改，就得到一个运行期 TypeError（不是加载期报错）。
	const launcherSrc = fs.readFileSync(path.join(UI, 'js', 'launcher.js'), 'utf8');

	for (const name of VIEW_MODULES) {
		const ns = NS[name];
		const src = fs.readFileSync(path.join(UI, 'js', name + '.js'), 'utf8');
		assert.ok(NS_ASSIGN(name).test(src),
			`${name}.js 必须导出 SG.${ns}（launcher.js 从这里取 render/bind）`);

		// playtime 直接调用 SG.playtime.render，news/downloads/skins 是薄委托；
		// 三种接法都要认，但**必须是渲染/绑定入口真的走了过去**，不能只挂一个 __wire：
		// 只留 __wire 正是 MUT-3 暴露的假接线——脚本加载了、也"引用了"，但点进那一页是空白。
		const delegatesRender = new RegExp('[\\s{;]' + ns + ":\\s*\\{[^}]*SG\\." + ns).test(launcherSrc)
			|| new RegExp("render:.*SG\\." + ns + "\\.render").test(launcherSrc)
			|| new RegExp("function render" + name.charAt(0).toUpperCase() + name.slice(1) + "\\(\\)\\s*\\{\\s*return SG\\." + ns).test(launcherSrc);
		const delegatesBind = new RegExp("SG\\." + ns + "\\.bind").test(launcherSrc)
			|| new RegExp("function bind" + name.charAt(0).toUpperCase() + name.slice(1) + "\\(el\\)\\s*\\{\\s*return SG\\." + ns).test(launcherSrc);
		assert.ok(delegatesRender, `launcher.js 的渲染入口没有委托到 SG.${ns}：视图会渲染成空白页`);
		assert.ok(delegatesBind, `launcher.js 的事件绑定入口没有委托到 SG.${ns}：视图渲染出来也没有交互`);
	}
});

test('每个拆出模块用到的注入项，launcher.js 的 __wire 里都必须真的提供', () => {
	// 由来（M1-3 第三步 content）：content.js 通过 __wire 拿 pageGen()/setDebounce() 等访问器。
	// 漏掉任何一个注入项，功能不会报错、而是**静默失效**：
	//   - 漏 pageGen  → 代际校验恒为 0，离页后迟到请求会复活隐藏页面（实测能被行为测试抓到）
	//   - 漏 setDebounce → 搜索框 debounce 变成空操作（实测**全量 485 全绿也没抓到**）
	// 后者是真实存在的覆盖盲区（拆分前就没有 debounce 的行为测试），静态守不住就只能靠人记；
	// 这条断言至少保证"模块声明需要的注入项"与"launcher 实际注入的"不会漂移。
	const launcherSrc = fs.readFileSync(path.join(UI, 'js', 'launcher.js'), 'utf8');

	for (const name of VIEW_MODULES) {
		const ns = NS[name];
		const src = fs.readFileSync(path.join(UI, 'js', name + '.js'), 'utf8');

		// 模块自己在 __wire 里读取的键名（deps.xxx）
		const usedInWire = [...src.matchAll(/deps\.([A-Za-z_$][\w$]*)/g)].map(m => m[1]);
		if (!usedInWire.length) continue;

		// launcher 里对该模块的 __wire 调用体
		const callAt = launcherSrc.indexOf('SG.' + ns + '.__wire(');
		assert.notEqual(callAt, -1, `launcher.js 没有调用 SG.${ns}.__wire(...)`);
		let depth = 0, i = launcherSrc.indexOf('{', callAt), start = i;
		for (; i < launcherSrc.length; i++) {
			if (launcherSrc[i] === '{') depth++;
			else if (launcherSrc[i] === '}') { depth--; if (depth === 0) break; }
		}
		const wireBody = launcherSrc.slice(start, i + 1);

		for (const key of usedInWire) {
			// 支持简写 {toast} 与显式 {toast: fn} 两种写法
			const provided = new RegExp('(^|[{,\\s])' + key + '\\s*[:,}]').test(wireBody);
			assert.ok(provided, `SG.${ns}.__wire 缺少注入项 "${key}"（${name}.js 读 deps.${key}）：该功能会静默失效`);
		}
	}
});

test('跨视图共享层也导出了 SG.<模块名>，且被 launcher.js 或视图模块真实使用', () => {
	// 共享层（skin-visuals）没有 render/bind 入口，所以上面那组"必须委托"的断言不适用。
	// 但"加载了却没人用"的假接线同样要防——只是它的故障表现不同：
	// 不是白屏，而是皮肤缩略图/头像渲染时 `SG.skinVisuals` 为 undefined 直接抛错。
	const launcherSrc = fs.readFileSync(path.join(UI, 'js', 'launcher.js'), 'utf8');
	const allSources = launcherSrc + VIEW_MODULES.concat(SHARED_MODULES)
		.map(n => fs.readFileSync(path.join(UI, 'js', n + '.js'), 'utf8')).join('\n');

	for (const name of SHARED_MODULES) {
		const ns = NS[name];
		const src = fs.readFileSync(path.join(UI, 'js', name + '.js'), 'utf8');
		assert.ok(NS_ASSIGN(name).test(src), `${name}.js 必须导出 SG.${ns}`);
		assert.ok(new RegExp("SG\\." + ns + "\\.[A-Za-z_$][A-Za-z0-9_$]*\\s*\\(").test(allSources),
			`没有任何模块调用 SG.${ns}.<方法>()：脚本加载了却没人用它（假接线）`);
	}
});
