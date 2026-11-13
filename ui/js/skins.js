(function (root) {
'use strict';
if (!root || !root.SG) return;
const SG = root.SG;
/* ================================================================
   skins.js —— Skins 视图（M1-3 第三步后段）
   ================================================================
   协议与 news.js / downloads.js 一致：IIFE + SG.skins + __wire 单向注入 +
   core 纯函数面 + module.exports。launcher.js 保留 renderSkins()/bindSkins(el)
   两个薄委托，VIEWS 路由表与 SG.__test__ 的入口名不变。

   与 downloads 的差别（为什么这里要注入更多东西）：
     皮肤的渲染依赖账户列表（ACCOUNTS，由 launcher 的 loadAccounts 维护），
     应用皮肤后要 loadAccounts(true) + invalidate('skins') + goTo('skins')，
     所以需要注入：accounts 读取、loadAccounts、invalidate、goTo、toast。
     这些都是**单向注入**，本文件不反向写 launcher 的状态。

   activeSkinViewer 的所有权（本步的明确决定）：
     它是 3D 视图实例，**只有 skins 视图会创建/销毁它**。拆分前 launcher.js 里
     有三处碰它：goTo 切走时 dispose、invalidate('skins') 时 dispose、bindSkins 内
     重建时 dispose。前两处是"外部要求销毁"，因此改为由本模块**导出 dispose()**，
     launcher 侧只调用 SG.skins.dispose()，不再持有该变量本身。
     这样 activeSkinViewer 的生命周期完全落在本文件内，不会出现两份真相。
*/
let _toast = () => {};
let _currentView = () => null;
let _accounts = () => [];
let _loadAccounts = async () => [];
let _invalidate = () => {};
let _goTo = async () => {};

let activeSkinViewer = null;

function wire(deps) {
	if (!deps) return;
	if (typeof deps.toast === 'function') _toast = deps.toast;
	if (typeof deps.currentView === 'function') _currentView = deps.currentView;
	if (typeof deps.accounts === 'function') _accounts = deps.accounts;
	if (typeof deps.loadAccounts === 'function') _loadAccounts = deps.loadAccounts;
	if (typeof deps.invalidate === 'function') _invalidate = deps.invalidate;
	if (typeof deps.goTo === 'function') _goTo = deps.goTo;
}

const t = (k, vars) => (SG.t ? SG.t(k, vars) : k);
function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
function uiText(zh, en) { return SG.locale === 'en' ? en : zh; }
function uiIcon(name) {
	const paths = { user: '<circle cx="12" cy="8" r="4"/><path d="M4 22v-3a8 8 0 0 1 16 0v3"/>' };
	return `<svg viewBox="0 0 24 24" aria-hidden="true">${paths[name] || paths.user}</svg>`;
}
/* 视觉辅助来自 skin-visuals.js（跨视图共享层）；缺失时直接抛错，暴露接线问题 */
const sv = () => SG.skinVisuals;

/* ---------------- 渲染 ---------------- */

async function renderSkins() {
	const accounts = await _loadAccounts();
	const active = accounts.find(a => a.selected) || accounts[0];
	const skins = (active && active.skins && active.skins.length) ? active.skins : [];
	const activeSkin = skins[0] || null;
	const activeName = active ? t('skin.ofUser', { name: active.userName }) : t('skin.default');

	const cards = skins.length ? skins.map((s, idx) => `
		<button class="skin-card ${idx === 0 ? 'selected' : ''}" data-skin="${esc(s.id)}">
			<div class="skin-thumb">${sv().skinThumb(44, s)}</div>
			<span class="skin-card-name">${esc(s.name || t('skin.item'))}</span>
		</button>`).join('')
		: `<div style="padding:24px;color:var(--muted,#8b91a1);font-size:13px">${esc(t('skin.emptyList'))}</div>`;

	return `
	<div class="skins-view">
		<div class="skin-preview-panel">
			<div class="skin-preview-title"><div>${esc(t('skin.previewTitle'))}<p class="ui-muted">${esc(active?active.userName:uiText('未登录 · 可选择本地皮肤预览','Not signed in · preview a local skin'))}</p></div><span class="ui-chip" id="skinHeaderModel">${esc((activeSkin&&activeSkin.model)==='slim'?t('skin.slimModel'):t('skin.classicModel'))}</span></div>
			<div class="preview-canvas" id="previewCanvas">
				<div id="skin3dContainer" class="skin3d-container"></div>
				<div id="skin2dFallback" class="skin2d-fallback" hidden role="status">
					${active && activeSkin && activeSkin.url ? `<img src="/api/accounts/${encodeURIComponent(active.id)}/skin-image?token=${encodeURIComponent(SG.apiToken || '')}" alt="${esc(t('skin.fallbackAlt'))}">` : ''}
					<span id="skinFallbackMessage">${esc(t('skin.webglFallback'))}</span>${!active?`<button class="ui-button primary skin-login" id="btnSkinLogin">${uiIcon('user')}${uiText('登录 Microsoft 账户','Sign in with Microsoft')}</button>`:''}
				</div>
			</div>
			<div class='skin3d-hint'>
				<span class='skin3d-hint-dot'></span>
				<span>${esc(t('skin.dragHint'))}</span>
			</div>
			<div class="skin3d-controls" aria-label="${esc(t('skin.controls'))}">
				<button class="skin-control" id="btnSkinReset" type="button">${esc(t('skin.resetView'))}</button>
				<button class="skin-control" id="btnSkinRotate" type="button" aria-pressed="true">${esc(t('skin.pauseRotate'))}</button>
			</div>
			<div class="canvas-details">
				<span class="canvas-name" id="activeSkinName">${esc(activeName)}</span>
				<span class="canvas-model" id="activeSkinModel">${esc((activeSkin && activeSkin.model) === 'slim' ? t('skin.slimModel') : t('skin.classicModel'))}</span>
			</div>
		</div>
		<div class="inventory-panel">
			<div class="inventory-actions">
				<div class="skin-model-field"><span>${esc(t('skin.model'))}</span><div class="skin-model-segment" role="group" aria-label="${esc(t('skin.model'))}"><button data-skin-model="classic" aria-pressed="true">${esc(t('skin.classicModel'))}</button><button data-skin-model="slim" aria-pressed="false">Slim / Alex</button></div><select id="skinModelSelect" hidden aria-label="${esc(t('skin.model'))}"><option value="classic">${esc(t('skin.classicModel'))}</option><option value="slim">${esc(t('skin.slimModel'))}</option></select></div>
				<button class="skin-btn primary" id="btnUploadSkin">
					<svg viewBox="0 0 24 24"><path d="M12 16V4M7 9l5-5 5 5M4 20h16"/></svg>
					<span>${esc(t('skin.chooseFile'))}</span>
				</button>
                <p class="ui-muted">${uiText('64×64 或 64×32 PNG，不超过 1 MiB。选择后先预览。','64×64 or 64×32 PNG, up to 1 MiB. Preview before applying.')}</p>
			</div>
			<div class="saved-skins">
				<div class="saved-label">${esc(t('skin.saved', { n: skins.length }))}</div>
				<div class="skins-grid" id="skinsGrid">${cards}</div>
				<div class="skin-local-preview"><p>${esc(t('skin.previewFirst'))}</p><img id="localSkinTexture" alt="${esc(t('skin.fallbackAlt'))}" hidden><p id="localSkinFileName"></p><div class="skin-local-actions"><button class="ui-button primary" id="btnApplyLocalSkin" disabled>${esc(t('skin.applyAccount'))}</button></div><p>${esc(t('skin.microsoftOnly'))}</p></div>
            </div>
				<button class="skin-btn ghost" id="btnBrowseSkin">
					<svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18"/></svg>
					<span>${esc(t('skin.browse'))}</span>
				</button>
        </div>
    </div>`;
}

/* ---------------- 绑定 ---------------- */

function dispose() {
	if (activeSkinViewer) {
		try { activeSkinViewer.dispose(); } catch (e) { /* 销毁失败不应阻断导航 */ }
		activeSkinViewer = null;
	}
}

function bindSkins(el) {
	dispose();
	const accounts = _accounts();
	const active = accounts.find(a => a.selected) || accounts[0];
	const skins = (active && active.skins) || [];
	let viewer = null;
	const container = el.querySelector('#skin3dContainer');
	const fallback = el.querySelector('#skin2dFallback');
	const fallbackMessage = el.querySelector('#skinFallbackMessage');
	const showFallback = (message, url) => {
		if (container) container.hidden = true;
		if (fallback) {
			fallback.hidden = false;
			if (fallbackMessage) fallbackMessage.textContent = message || t('skin.webglFallback');
			let image = fallback.querySelector('img');
			if (!image && url) { image=document.createElement('img'); image.alt=t('skin.fallbackAlt'); fallback.prepend(image); }
			if (image && url) image.src = url;
		}
	};
	const showViewer = () => { if (container) container.hidden = false; if (fallback) fallback.hidden = true; };
	if (container && typeof Skin3DViewer !== 'undefined' && typeof THREE !== 'undefined') {
		try {
			viewer = new Skin3DViewer(container, { ariaLabel: t('skin.canvasLabel'), onError: showFallback, onReady: showViewer });
			activeSkinViewer = viewer;
			if (skins[0] && skins[0].url) viewer.setSkin(skins[0], active ? active.id : null);
			else showFallback(t('skin.textureUnavailable'));
		} catch (e) {
			viewer = null;
			showFallback(t('skin.webglFallback'));
		}
	} else showFallback(t('skin.webglFallback'));
	el.querySelector('#btnSkinReset')?.addEventListener('click', () => viewer && viewer.resetView());
	el.querySelector('#btnSkinRotate')?.addEventListener('click', e => {
		if (!viewer) return;
		viewer.setAutoRotate(!viewer.autoRotate);
		e.currentTarget.setAttribute('aria-pressed', String(viewer.autoRotate));
		e.currentTarget.textContent = viewer.autoRotate ? t('skin.pauseRotate') : t('skin.resumeRotate');
	});
	el.querySelectorAll('.skin-card').forEach(c => {
		c.addEventListener('click', () => {
			el.querySelectorAll('.skin-card').forEach(x => x.classList.remove('selected'));
			c.classList.add('selected');
			const skin = skins.find(s => s.id === c.dataset.skin);
			pendingSkin = null; if (apply) apply.disabled = true;
			const texture = el.querySelector('#localSkinTexture'); if (texture) texture.hidden = true;
			if (skin) {
				if (viewer) viewer.setSkin(skin, active ? active.id : null);
				const nameEl = el.querySelector('#activeSkinName');
				if (nameEl) nameEl.textContent = skin.name || t('skin.item');
				const modelEl = el.querySelector('#activeSkinModel');
				if (modelEl) modelEl.textContent = skin.model === 'slim' ? t('skin.slimModel') : t('skin.classicModel');
                syncModel(skin.model);
				_toast(t('skin.preview', { name: skin.name || '' }));
			}
		});
	});

 let pendingSkin = null;
 const apply = el.querySelector('#btnApplyLocalSkin');
 const modelSelect = el.querySelector('#skinModelSelect');
 const syncModel=model=>{const value=model==='slim'?'slim':'classic';if(modelSelect)modelSelect.value=value;el.querySelectorAll('[data-skin-model]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.skinModel===value)));const label=el.querySelector('#skinHeaderModel');if(label)label.textContent=t(value==='slim'?'skin.slimModel':'skin.classicModel');};
 syncModel(skins[0]?.model);
 const previewLocal = () => {
   if (!pendingSkin) {const selected=el.querySelector('.skin-card.selected');const skin=skins.find(x=>x.id===selected?.dataset.skin)||skins[0];if(skin&&viewer)viewer.setSkin({...skin,model:modelSelect.value},active?active.id:null);return;}
   const slim = modelSelect && modelSelect.value === 'slim';
   const local = { url: pendingSkin.dataUrl, model: slim ? 'slim' : 'classic' };
   if (viewer) viewer.setSkin(local, null); else showFallback(t('skin.webglFallback'), pendingSkin.dataUrl);
   const name = el.querySelector('#activeSkinName'), model = el.querySelector('#activeSkinModel');
   if (name) name.textContent = pendingSkin.name;
   if (model) model.textContent = t(slim ? 'skin.slimModel' : 'skin.classicModel');
   const texture = el.querySelector('#localSkinTexture');
   if (texture) { texture.src = pendingSkin.dataUrl; texture.hidden = false; }
   const label = el.querySelector('#localSkinFileName');
   if (label) label.textContent = pendingSkin.name;
   if (apply) apply.disabled = !active || active.type !== 'microsoft';
 };
 modelSelect?.addEventListener('change', previewLocal);
 el.querySelectorAll('[data-skin-model]').forEach(b=>b.addEventListener('click',()=>{modelSelect.value=b.dataset.skinModel;el.querySelectorAll('[data-skin-model]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));const label=el.querySelector('#skinHeaderModel');if(label)label.textContent=b.textContent;previewLocal();}));
 el.querySelector('#btnSkinLogin')?.addEventListener('click',()=>_goTo('settings'));

 el.querySelector('#btnUploadSkin')?.addEventListener('click', () => {
   const input = document.createElement('input');
   input.type = 'file'; input.accept = 'image/png';
   input.onchange = () => {
     const f = input.files && input.files[0];
     if (!f) return;
     if (!/\.png$/i.test(f.name) || (f.type && f.type !== 'image/png') || f.size > 1024 * 1024) { _toast(t('skin.invalidFile')); return; }
     const reader = new FileReader();
     reader.onerror = () => _toast(t('skin.invalidFile'));
     reader.onload = () => {
       if (!el.isConnected) return;
       const dataUrl = String(reader.result || ''), image = new Image();
       image.onerror = () => _toast(t('skin.invalidFile'));
       image.onload = () => {
         if (!el.isConnected) return;
         if (image.width !== 64 || (image.height !== 64 && image.height !== 32)) { _toast(t('skin.invalidDimensions')); return; }
         pendingSkin = { dataUrl, name: f.name }; previewLocal();
         _toast(t('skin.preview', { name: f.name }));
       };
       image.src = dataUrl;
     };
     reader.readAsDataURL(f);
   };
   input.click();
 });
 apply?.addEventListener('click', async () => {
   if (!pendingSkin || apply.disabled) return;
   if (!active || active.type !== 'microsoft') { _toast(t('skin.needMicrosoft')); return; }
   apply.disabled = true;
   try {
     await SG.api.uploadSkin(active.id, pendingSkin.dataUrl.replace(/^data:[^;]+;base64,/, ''), modelSelect?.value === 'slim');
     _toast(t('skin.uploaded')); await _loadAccounts(true); _invalidate('skins');
     if (_currentView() === 'skins') await _goTo('skins');
   } catch (err) { _toast(t('skin.uploadFailed') + (err.message || t('common.unknownError'))); }
   finally { if (apply.isConnected) apply.disabled = false; }
 });
	el.querySelector('#btnBrowseSkin')?.addEventListener('click', () => _goTo('content'));
}

const SG_NS = SG;
SG_NS.skins = {
	render: renderSkins,
	bind: bindSkins,
	dispose,
	// 供 SG.__test__ 与 launcher 侧查询（测试断言"视图是否还持有 3D 实例"）
	hasActiveViewer() { return !!activeSkinViewer; },
	__wire: wire,
	// 纯函数区：本视图没有独立的纯函数，视觉辅助在 skin-visuals.js
	core: {},
};

if (typeof module === 'object' && module.exports) module.exports = SG_NS.skins.core;
})(typeof window === 'undefined' ? null : window);
