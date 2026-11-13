(function (root) {
'use strict';
if (!root || !root.SG) return;
const SG = root.SG;
/* ================================================================
   skin-visuals.js —— 皮肤视觉辅助（M1-3 第三步后段）
   ================================================================
   为什么单独拆一层，而不是塞进 skins.js：
     这 5 个 helper 里，`faceThumb` 同时被**设置页**（renderAccountCards）
     和**侧栏**（refreshSidebarAccount）调用，`steveSvg`/`skinAvatar` 还被
     纯函数测试直接断言。它们不属于 skins 视图私有，属于跨视图共享的视觉层。
     硬塞进 skins.js 会让设置页依赖 skins 视图模块，是错的方向。

   本文件不持有状态、不碰 DOM 事件、不发请求：纯字符串拼接。
   唯一外部依赖是 `esc`（URL/文案转义，防属性截断），模块内自带同实现，
   与 launcher.js 的 esc 保持字符级一致（拆分前是同一个函数）。
*/
function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
const t = (k, vars) => (SG.t ? SG.t(k, vars) : k);

/* 简易像素小人 SVG（皮肤无贴图时的占位） */
function steveSvg(scale, palette) {
	const p = palette || { head: '#b5835a', face: '#7d5a3c', body: '#3aa0a0', arm: '#b5835a', leg: '#3a4a80' };
	return `<svg viewBox="0 0 80 120" width="${scale}" style="image-rendering:pixelated">
		<rect x="26" y="6"  width="28" height="28" fill="${p.head}"/>
		<rect x="32" y="16" width="6" height="6" fill="#fff"/><rect x="42" y="16" width="6" height="6" fill="#fff"/>
		<rect x="33" y="17" width="4" height="4" fill="#3a5da0"/><rect x="43" y="17" width="4" height="4" fill="#3a5da0"/>
		<rect x="30" y="26" width="20" height="4" fill="${p.face}"/>
		<rect x="28" y="36" width="24" height="34" fill="${p.body}"/>
		<rect x="16" y="36" width="10" height="30" fill="${p.arm}"/>
		<rect x="54" y="36" width="10" height="30" fill="${p.arm}"/>
		<rect x="28" y="72" width="10" height="34" fill="${p.leg}"/>
		<rect x="42" y="72" width="10" height="34" fill="${p.leg}"/>
	</svg>`;
}
/* 从 64×64（兼容 64×32）皮肤图集裁剪出正面 2D 小人；右侧肢体镜像出左侧，旧版皮肤也不缺块 */
function skinAvatar(w, url, model) {
	const U = w / 16;                 // 每个皮肤像素对应的屏幕像素（人物正面宽 16 皮肤像素）
	const atlas = 64 * U;             // 整张图集缩放后的尺寸
	const layer = (sx, sy, sw, sh, x, y, mirror) =>
		`<div style="position:absolute;left:${x * U}px;top:${y * U}px;width:${sw * U}px;height:${sh * U}px;` +
		`background:url('${esc(url)}') -${sx * U}px -${sy * U}px/${atlas}px ${atlas}px no-repeat;` +
		`image-rendering:pixelated;${mirror ? 'transform:scaleX(-1);' : ''}"></div>`;
	const slim = model === 'slim';
	const parts = [
		layer(8, 8, 8, 8, 4, 0, false),    // 头
		layer(20, 20, 8, 12, 4, 8, false), // 身体
		layer(44, 20, slim ? 3 : 4, 12, slim ? 0.5 : 0, 8, false), // 右臂
		layer(slim ? 36 : 44, slim ? 52 : 20, slim ? 3 : 4, 12, slim ? 12.5 : 12, 8, false), // 左臂
		layer(4, 20, 4, 12, 4, 20, false), // 右腿
		layer(4, 20, 4, 12, 8, 20, true),  // 左腿（镜像）
	].join('');
	return `<div style="position:relative;width:${16 * U}px;height:${32 * U}px;image-rendering:pixelated">${parts}</div>`;
}
function skinThumb(scale, skin) {
	if (skin && skin.url) return skinAvatar(scale, skin.url, skin.model);
	return `<span class="ui-muted" role="status">${esc(t('skin.textureUnavailable'))}</span>`;
}
/* 仅头部大头照：裁剪图集头层(8,8)+帽层(40,8)填满 w×w 方块，用于账户身份头像 */
function skinFace(w, url) {
	const U = w / 8;               // 头部正面宽 8 皮肤像素
	const atlas = 64 * U;
	const layer = (sx, sy) =>
		`<div style="position:absolute;inset:0;` +
		`background:url('${esc(url)}') -${sx * U}px -${sy * U}px/${atlas}px ${atlas}px no-repeat;` +
		`image-rendering:pixelated"></div>`;
	return `<div style="position:relative;width:${w}px;height:${w}px;overflow:hidden;image-rendering:pixelated">${layer(8, 8)}${layer(40, 8)}</div>`;
}
function faceThumb(scale, skin) {
	if (skin && skin.url) return skinFace(scale, skin.url);
	return `<svg viewBox="0 0 24 24" width="${scale}" height="${scale}" aria-hidden="true" style="fill:none;stroke:currentColor;stroke-width:1.5"><circle cx="12" cy="8" r="3.5"/><path d="M5 21v-2a7 7 0 0 1 14 0v2"/></svg>`;
}

const SG_NS = SG;
SG_NS.skinVisuals = {
	steveSvg,
	skinAvatar,
	skinThumb,
	skinFace,
	faceThumb,
	// 纯函数区（无状态、无 DOM、无网络）：供 node:test 直接断言
	core: { steveSvg, skinAvatar, skinThumb, skinFace, faceThumb },
};

if (typeof module === 'object' && module.exports) module.exports = SG_NS.skinVisuals.core;
})(typeof window === 'undefined' ? null : window);
