'use strict';
/*
 * ui-script-order.cjs —— 前端脚本加载清单的**单一来源**
 *
 * 由来（M1-3 第一步）：`ui/js/launcher.js` 有 4,600+ 行、18 个视图的 render/bind 全在里面。
 * 要按视图拆出去，得先解决一个前置问题：**"哪些脚本、按什么顺序加载"这件事被写死在 3 个地方**
 *   - `ui/index.html` 的 <script> 标签（生产运行时）
 *   - `ui/tests/helpers/live-launcher.cjs` 的 for 循环（测试）
 *   - 各测试自己 `fs.readFileSync` 的零散组合（多个文件）
 *
 * 只要这三处不同步，拆文件就会出现"生产能跑、测试读不到"或反之。所以先把它收敛到这一处：
 * 拆文件时只改本文件的数组，`index.html` 与测试都从它派生。
 *
 * 路径口径：全部是**相对 `ui/` 的路径**（与 `index.html` 里 `<script src>` 的字面值一致，
 * 只去掉 `js/` 前缀后归一到 "相对 ui/js 的文件名" 这一层由调用方处理，见 `toJsRelative`）。
 * 统一口径是为了让 `existsSync(path.join(UI, entry))` 这种最直白的检查就能成立。
 *
 * 顺序语义（不是随便排的，改顺序会真的坏）：
 *   1. `vendor/three.min.js`  —— 全局 THREE，皮肤预览依赖
 *   2. `js/skin3d-core.js`    —— 纯函数，无 SG 依赖，必须最先
 *   3. `js/skin3d.js`         —— 依赖 Skin3DCore + THREE
 *   4. `js/i18n.js`           —— 提供 SG.t / SG.locale；后面的模块都用它
 *   5. `js/api.js`            —— 提供 SG.api；用到 SG.t（错误文案）
 *   6. `js/playtime.js`       —— 依赖 SG.api + SG.t
 *   7. `js/update-monitor.js` —— 依赖 SG.api
 *   8. `js/launcher.js`       —— 主壳，依赖以上全部
 */

const SCRIPT_ORDER = [
	'vendor/three.min.js',
	'js/skin3d-core.js',
	'js/skin3d.js',
	'js/i18n.js',
	'js/api.js',
	'js/playtime.js',
	'js/update-monitor.js',
	'js/launcher.js',
];

/** 相对 `ui/` 的路径 → 相对 `ui/js/` 的文件名。vendor 不属于 js/，返回 null。 */
function toJsRelative(entry) {
	return entry.startsWith('js/') ? entry.slice(3) : null;
}

/**
 * 从 `ui/index.html` 里解析真实的 <script src> 顺序，归一到"相对 ui/"口径。
 * 用于守卫测试：本文件的数组一旦与 HTML 漂移，测试立刻红。
 */
function parseIndexHtmlOrder(html) {
	const out = [];
	const re = /<script\b[^>]*\bsrc\s*=\s*"([^"]+)"/gi;
	let m;
	while ((m = re.exec(html)) !== null) {
		let src = m[1].trim();
		if (!src) continue;
		// 归一：去掉开头的 ./、以及 js/ 前缀以外的形式统一成 "vendor/xxx" 或 "js/xxx"
		src = src.replace(/^\.\//, '');
		if (!src.startsWith('vendor/') && !src.startsWith('js/')) {
			src = 'js/' + src;
		}

		out.push(src);
	}

	return out;
}

module.exports = { SCRIPT_ORDER, toJsRelative, parseIndexHtmlOrder };
