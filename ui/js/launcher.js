/* ================================================================
   松光启动器 · Figma 1:1 复刻 · 视图控制器
   对接 SG.api（js/api.js）真实数据；native shell 桥接保留 C# 消息契约
   ================================================================ */
(function () {
'use strict';

/* ---------------- native shell 桥接 ---------------- */
const NATIVE_SHELL = !!(window.chrome && window.chrome.webview) || typeof window.invokeCSharpAction === 'function';
function sendNative(msg) {
	try {
		const payload = JSON.stringify(msg);
		if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(payload);
		else if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(payload);
	} catch (err) { console.warn('[shell] native message failed:', err); }
}

/* ---------------- toast ---------------- */
let toastTimer;
function toast(msg) {
	const el = document.getElementById('toast');
	if (!el) return;
	el.textContent = msg;
	el.classList.add('show');
	clearTimeout(toastTimer);
	toastTimer = setTimeout(() => el.classList.remove('show'), 2200);
}
window.SG = window.SG || {};
SG.toast = toast;

/* ---------------- 工具 ---------------- */
const t = (k, vars) => (window.SG && SG.t ? SG.t(k, vars) : k);
function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
const LOADER_COLORS = { Fabric: '#2b6cb0', Forge: '#7a3d1a', NeoForge: '#821ed2', OptiFine: '#b45309', Quilt: '#7c3aed', Vanilla: '#1e832a' };
function loaderColor(l) { return LOADER_COLORS[l] || '#2b3040'; }
function firstLetter(s) { const t = (s || '').trim(); return t ? t[0].toUpperCase() : 'M'; }

/* ================================================================
   全局状态缓存（一次拉取，多页复用）
   ================================================================ */
let STATE = null;      // { config, instances, accounts, java }
let ACCOUNTS = [];     // getAccounts 结果（含 skins/capes）
let selectedInstanceId = null;

async function loadState(force) {
	if (STATE && !force) return STATE;
	try {
		STATE = await SG.api.getState();
	} catch (e) {
		STATE = { config: {}, instances: [], accounts: [], java: [] };
	}
	// 选中实例：优先 config.selectedInstanceId，其次首个
	const cfgSel = STATE.config && STATE.config.selectedInstanceId;
	if (cfgSel && STATE.instances.some(i => i.id === cfgSel)) selectedInstanceId = cfgSel;
	else if (STATE.instances.length) selectedInstanceId = STATE.instances[0].id;
	return STATE;
}
async function loadAccounts(force) {
	if (ACCOUNTS.length && !force) return ACCOUNTS;
	try {
		const r = await SG.api.getAccounts();
		ACCOUNTS = r.accounts || [];
	} catch (e) { ACCOUNTS = []; }
	return ACCOUNTS;
}

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
function skinAvatar(w, url) {
	const U = w / 16;                 // 每个皮肤像素对应的屏幕像素（人物正面宽 16 皮肤像素）
	const atlas = 64 * U;             // 整张图集缩放后的尺寸
	const layer = (sx, sy, sw, sh, x, y, mirror) =>
		`<div style="position:absolute;left:${x * U}px;top:${y * U}px;width:${sw * U}px;height:${sh * U}px;` +
		`background:url('${esc(url)}') -${sx * U}px -${sy * U}px/${atlas}px ${atlas}px no-repeat;` +
		`image-rendering:pixelated;${mirror ? 'transform:scaleX(-1);' : ''}"></div>`;
	const parts = [
		layer(8, 8, 8, 8, 4, 0, false),    // 头
		layer(20, 20, 8, 12, 4, 8, false), // 身体
		layer(44, 20, 4, 12, 0, 8, false), // 右臂
		layer(44, 20, 4, 12, 12, 8, true), // 左臂（镜像）
		layer(4, 20, 4, 12, 4, 20, false), // 右腿
		layer(4, 20, 4, 12, 8, 20, true),  // 左腿（镜像）
	].join('');
	return `<div style="position:relative;width:${16 * U}px;height:${32 * U}px;image-rendering:pixelated">${parts}</div>`;
}
function skinThumb(scale, skin) {
	if (skin && skin.url) return skinAvatar(scale, skin.url);
	const c = (skin && skin.colors) || {};
	return steveSvg(scale, { head: c.head || '#b5835a', face: c.face || '#7d5a3c', body: c.body || '#3aa0a0', arm: c.arm || c.head || '#b5835a', leg: c.leg || '#3a4a80' });
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
	const c = (skin && skin.colors) || {};
	const head = c.head || '#b5835a';
	return `<svg viewBox="0 0 8 8" width="${scale}" height="${scale}" style="image-rendering:pixelated;display:block">
		<rect x="0" y="0" width="8" height="8" fill="${head}"/>
		<rect x="1.5" y="3" width="1.6" height="1.6" fill="#fff"/><rect x="4.9" y="3" width="1.6" height="1.6" fill="#fff"/>
		<rect x="1.9" y="3.4" width="1" height="1" fill="#3a5da0"/><rect x="5.3" y="3.4" width="1" height="1" fill="#3a5da0"/>
	</svg>`;
}

/* ================================================================
   静态内容（后端无对应接口：官方新闻）
   ================================================================ */
const NEWS = [
	{ catKey: 'news.community', titleKey: 'news.title1', date: 'Sep 5, 2024',  art: 'linear-gradient(135deg,#c8703a,#7a3d1a)' },
	{ catKey: 'news.update',    titleKey: 'news.title2', date: 'Aug 28, 2024', art: 'linear-gradient(135deg,#4a90d9,#2d5c8f)' },
	{ catKey: 'news.merch',     titleKey: 'news.title3', date: 'Aug 12, 2024', art: 'linear-gradient(135deg,#3aa06a,#1f5e3e)' },
];

/* ================================================================
   视图渲染（真实数据）
   ================================================================ */
async function renderHome() {
	await loadState();
	const insts = STATE.instances || [];
	const sel = insts.find(i => i.id === selectedInstanceId) || insts[0];

	const options = insts.map(i => `<option value="${esc(i.id)}"${i.id === (sel && sel.id) ? ' selected' : ''}>${esc(i.name)} · ${esc(i.version || '—')}</option>`).join('');
	const news = NEWS.map(n => {
		const title = t(n.titleKey);
		return `
		<button class="news-card" data-news="${esc(title)}">
			<div class="news-art" style="background:${n.art}"></div>
			<div class="news-body">
				<span class="news-cat">${esc(t(n.catKey))}</span>
				<span class="news-title">${esc(title)}</span>
				<span class="news-date">${esc(n.date)}</span>
			</div>
		</button>`;
	}).join('');

	return `
	<div class="play-view">
		<div class="banner">
			<div class="banner-art"></div>
			<div class="banner-body">
				<span class="banner-badge">${esc(t('home.badge'))}</span>
				<h1 class="banner-title">${esc(t('home.bannerTitle'))}</h1>
				<p class="banner-desc">${esc(t('home.bannerDesc'))}</p>
			</div>
		</div>

		<div class="news-section">
			<h2 class="section-title">${esc(t('home.recentNews'))}</h2>
			<div class="news-grid">${news}</div>
		</div>

		<div class="play-footer">
			<div class="pf-version">
				<span class="pf-label">${esc(t('home.selectedVersion'))}</span>
				<div class="pf-select-btn" style="position:relative">
					<span class="sw"></span>
					<select id="pfVersionSelect" style="appearance:none;background:transparent;border:0;color:inherit;font:inherit;cursor:pointer;padding-right:20px;outline:none">
						${options || `<option>${esc(t('home.noInstances'))}</option>`}
					</select>
					<svg viewBox="0 0 24 24" style="pointer-events:none;position:absolute;right:8px;top:50%;transform:translateY(-50%)"><path d="m6 9 6 6 6-6"/></svg>
				</div>
			</div>
			<button class="play-btn" id="btnPlay"${sel ? '' : ' disabled'}>
				<svg viewBox="0 0 24 24"><path d="M6 4l14 8-14 8V4Z"/></svg>
				<span>${esc(t('home.play'))}</span>
			</button>
			<div class="pf-right">
				<span class="pf-edition">${esc(t('home.javaEdition'))}</span>
				<span class="pf-status">${esc(t('home.instancesAvailable', { n: insts.length }))}</span>
			</div>
		</div>
	</div>`;
}

async function renderInstallations() {
	await loadState();
	const insts = STATE.instances || [];
	const cards = insts.map(i => {
		const isSel = i.id === selectedInstanceId;
		const kind = (i.loader && i.loader !== 'Vanilla') ? 'mod' : 'all';
		const sub = `Minecraft ${esc(i.version || '—')} · ${esc(i.lastPlayed || t('inst.neverPlayed'))}`;
		return `
		<div class="inst-card ${isSel ? 'selected' : ''}" data-inst="${esc(i.id)}" data-kind="${kind}">
			<div class="inst-left">
				<div class="inst-icon" style="background:${loaderColor(i.loader)}">${firstLetter(i.name)}</div>
				<div class="inst-meta">
					<div class="inst-name-row">
						<span class="inst-name">${esc(i.name)}</span>
						<span class="inst-tag">${esc(i.loader || 'Vanilla')}</span>
					</div>
					<span class="inst-sub">${sub}</span>
				</div>
			</div>
			<div class="inst-actions">
				<button class="inst-play ${isSel ? 'primary' : 'ghost'}" data-play="${esc(i.id)}">${esc(t('inst.play'))}</button>
				<button class="inst-more" data-more="${esc(i.id)}" aria-label="${esc(t('inst.play'))}"><svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg></button>
			</div>
		</div>`;
	}).join('');

	const empty = insts.length ? '' : `<div style="padding:48px;text-align:center;color:var(--muted,#8b91a1)">${esc(t('inst.empty'))}</div>`;

	return `
	<div class="installations-view">
		<div class="top-control">
			<div class="filters">
				<button class="filter active" data-filter="all">${esc(t('inst.all'))}</button>
				<button class="filter" data-filter="mod">${esc(t('inst.modded'))}</button>
				<button class="filter" data-filter="snap">${esc(t('inst.snapshots'))}</button>
			</div>
			<button class="btn-new" id="btnNewInstall">
				<svg viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg>
				<span>${esc(t('inst.new'))}</span>
			</button>
		</div>
		<div class="inst-list" id="instList">${cards}${empty}</div>
	</div>`;
}

async function renderSkins() {
	const accounts = await loadAccounts();
	const active = accounts.find(a => a.selected) || accounts[0];
	const skins = (active && active.skins && active.skins.length) ? active.skins : [];
	const activeSkin = skins[0] || null;
	const activeName = active ? t('skin.ofUser', { name: active.userName }) : t('skin.default');

	const cards = skins.length ? skins.map((s, idx) => `
		<button class="skin-card ${idx === 0 ? 'selected' : ''}" data-skin="${esc(s.id)}">
			<div class="skin-thumb">${skinThumb(44, s)}</div>
			<span class="skin-card-name">${esc(s.name || t('skin.item'))}</span>
		</button>`).join('')
		: `<div style="padding:24px;color:var(--muted,#8b91a1);font-size:13px">${esc(t('skin.emptyList'))}</div>`;

	return `
	<div class="skins-view">
		<div class="skin-preview-panel">
			<div class="skin-preview-title">${esc(t('skin.currentActive'))}</div>
			<div class="preview-canvas" id="previewCanvas">${skinThumb(140, activeSkin)}</div>
			<div class="canvas-details">
				<span class="canvas-name" id="activeSkinName">${esc(activeName)}</span>
				<span class="canvas-model">${esc(active ? (active.type === 'microsoft' ? t('acc.microsoft') : active.type === 'authlib' ? t('acc.authlib') : t('acc.offline')) : t('skin.classicModel'))}</span>
			</div>
		</div>
		<div class="inventory-panel">
			<div class="inventory-actions">
				<button class="skin-btn primary" id="btnUploadSkin">
					<svg viewBox="0 0 24 24"><path d="M12 16V4M7 9l5-5 5 5M4 20h16"/></svg>
					<span>${esc(t('skin.upload'))}</span>
				</button>
				<button class="skin-btn ghost" id="btnBrowseSkin">
					<svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18"/></svg>
					<span>${esc(t('skin.browse'))}</span>
				</button>
			</div>
			<div class="saved-skins">
				<div class="saved-label">${esc(t('skin.saved', { n: skins.length }))}</div>
				<div class="skins-grid" id="skinsGrid">${cards}</div>
			</div>
		</div>
	</div>`;
}

async function renderSettings() {
	await loadState();
	const cfg = STATE.config || {};
	const java = STATE.java || [];
	const accounts = await loadAccounts();
	const acc = accounts.find(a => a.selected) || accounts[0];

	const MIN = 2, MAX = 16;
	const gb = Math.max(MIN, Math.min(MAX, (cfg.maxMemoryMb || 4096) / 1024));
	const pct = ((gb - MIN) / (MAX - MIN)) * 100;
	const autoMem = !!cfg.useAutoMemory;
	const useMirror = !!cfg.useMirror;

	const currentJava = java.find(j => j.path === cfg.javaPath);
	const javaLabel = cfg.javaPath
		? (currentJava ? `Java ${currentJava.versionMajor} · ${currentJava.path}` : cfg.javaPath)
		: (java.length ? t('set.javaAuto', { n: java.length }) : t('set.javaBundled'));

	const accName = acc ? acc.userName : t('set.notLoggedIn');
	const accType = acc ? (acc.type === 'microsoft' ? t('acc.microsoft') : acc.type === 'authlib' ? t('acc.authlib') : t('acc.offline')) : '';
	const accMail = acc ? (acc.uuid ? (t('set.uuidPrefix') + acc.uuid.slice(0, 18) + '…') : t('set.offlineMode')) : t('set.clickAddAccount');

	return `
	<div class="settings-view">
		<div class="settings-section">
			<div class="section-heading">${esc(t('set.javaSection'))}</div>
			<div class="section-content">
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.memName'))}</span>
						<span class="setting-desc">${esc(t('set.memDesc'))}</span>
					</div>
					<div class="slider-wrap">
						<div class="slider" id="memSlider"${autoMem ? ' style="opacity:.4;pointer-events:none"' : ''}>
							<div class="slider-fill" style="width:${pct}%"></div>
							<div class="slider-knob" style="left:${pct}%"></div>
						</div>
						<span class="setting-value" id="memValue">${gb.toFixed(1)} GB</span>
					</div>
				</div>
				<div class="setting-divider"></div>
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.autoMemName'))}</span>
						<span class="setting-desc">${esc(t('set.autoMemDesc'))}</span>
					</div>
					<button class="toggle ${autoMem ? 'on' : ''}" data-toggle="autoMem" role="switch" aria-checked="${autoMem}"></button>
				</div>
				<div class="setting-divider"></div>
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.javaPathName'))}</span>
						<span class="setting-desc">${esc(t('set.javaPathDesc'))}</span>
					</div>
					<button class="setting-btn" id="btnJrePath">${esc(javaLabel)}</button>
				</div>
			</div>
		</div>

		<div class="settings-section">
			<div class="section-heading">${esc(t('set.behaviorSection'))}</div>
			<div class="section-content">
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.mirrorName'))}</span>
						<span class="setting-desc">${esc(t('set.mirrorDesc'))}</span>
					</div>
					<button class="toggle ${useMirror ? 'on' : ''}" data-toggle="mirror" role="switch" aria-checked="${useMirror}"></button>
				</div>
				<div class="setting-divider"></div>
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.logsName'))}</span>
						<span class="setting-desc">${esc(t('set.logsDesc'))}</span>
					</div>
					<button class="setting-btn" id="btnOpenLogs">${esc(t('set.openLogs'))}</button>
				</div>
			</div>
		</div>

		<div class="settings-section">
			<div class="section-heading">${esc(t('set.accountSection'))}</div>
			<div class="section-content">
				<div class="account-row">
					<div class="account-left">
						<div class="account-avatar" id="settingsAvatar">${faceThumb(40, acc && acc.skins && acc.skins[0])}</div>
						<div class="account-meta">
							<span class="account-name">${esc(accName)}${accType ? ' · ' + esc(accType) : ''}</span>
							<span class="account-mail">${esc(accMail)}</span>
						</div>
					</div>
					<div style="display:flex;gap:10px">
						<button class="setting-btn" id="btnMsLogin">${esc(t('set.microsoftLogin'))}</button>
						<button class="setting-btn" id="btnAddAccount">${esc(t('set.addAccount'))}</button>
						<button class="btn-signout" id="btnSignOut">${esc(t('set.switch'))}</button>
					</div>
				</div>
			</div>
		</div>

		<div class="settings-section">
			<div class="section-heading">${esc(t('set.updateSection'))}</div>
			<div class="section-content">
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.updateVersion'))}</span>
						<span class="setting-desc" id="updateCurVer">…</span>
					</div>
					<button class="setting-btn" id="btnCheckUpdate">${esc(t('set.checkUpdate'))}</button>
				</div>
				<div class="setting-divider"></div>
				<div id="updateStatus" class="update-status"></div>
			</div>
		</div>
	</div>`;
}

/* ================================================================
   通用弹窗 Modal
   ================================================================ */
function openModal(html) {
	const host = document.getElementById('modalHost');
	if (!host) return null;
	host.innerHTML = '<div class="modal">' + html + '</div>';
	host.hidden = false;
	requestAnimationFrame(() => host.classList.add('show'));
	const close = () => closeModal();
	host.onclick = e => { if (e.target === host) close(); };
	return host;
}
function closeModal() {
	const host = document.getElementById('modalHost');
	if (!host) return;
	host.classList.remove('show');
	setTimeout(() => { host.hidden = true; host.innerHTML = ''; host.onclick = null; }, 180);
}
SG.closeModal = closeModal;

/* 实例操作菜单（Mod 管理 + 删除） */
function openInstanceMenu(inst) {
	openModal(`
		<div class="modal-title">${esc(inst.name)}</div>
		<div class="page-sub">Minecraft ${esc(inst.version || '—')} · ${esc(inst.loader || 'Vanilla')}</div>
		<div class="modal-actions" style="justify-content:space-between;margin-top:8px">
			<button class="modal-btn ghost" id="mMods">${esc(t('inst.manageMods'))}</button>
			<div style="display:flex;gap:10px">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.close'))}</button>
				<button class="modal-btn" id="mDel" style="background:#e0463c;color:#fff">${esc(t('inst.delete'))}</button>
			</div>
		</div>`);
	document.getElementById('mCancel').onclick = closeModal;
	document.getElementById('mMods').onclick = () => openModManager(inst);
	document.getElementById('mDel').onclick = async () => {
		if (!confirm(t('inst.deleteConfirm', { name: inst.name }))) return;
		closeModal();
		try {
			await SG.api.deleteInstance(inst.id);
			await loadState(true);
			invalidate('home');
			const v = document.getElementById('view-installations');
			v.innerHTML = await renderInstallations(); bindInstallations(v);
			toast(t('inst.deleted'));
		} catch (err) { toast(t('inst.deleteFailed') + (err.message || t('common.unknownError'))); }
	};
}

/* Mod 管理弹窗 */
async function openModManager(inst) {
	openModal(`<div class="modal-title">${esc(t('mod.manageTitle', { name: inst.name }))}</div><div class="mod-manage-list" id="modList"><div class="loading-state">${esc(t('common.loading'))}</div></div><div class="modal-actions"><button class="modal-btn primary" id="mCancel">${esc(t('common.done'))}</button></div>`);
	document.getElementById('mCancel').onclick = closeModal;
	let mods = [];
	try { mods = await SG.api.getMods(inst.id); } catch (e) { mods = []; }
	const listEl = document.getElementById('modList');
	if (!listEl) return;
	if (!mods.length) { listEl.innerHTML = '<div class="empty-state" style="padding:32px"><div class="es-icon">🧩</div>' + esc(t('mod.empty')) + '</div>'; return; }
	listEl.innerHTML = mods.map(m => `
		<div class="mod-row ${m.enabled ? '' : 'disabled'}">
			<div class="mod-row-left">
				<span class="mod-row-name">${esc(m.displayName)}</span>
				<span class="mod-row-sub">${esc(m.version || '')} ${m.loader ? '· ' + esc(m.loader) : ''} · ${esc(m.sizeText || '')}</span>
			</div>
			<button class="toggle ${m.enabled ? 'on' : ''}" role="switch" aria-checked="${m.enabled}" data-mod="${esc(m.fileName)}"></button>
		</div>`).join('');
	listEl.querySelectorAll('.toggle').forEach(t => t.addEventListener('click', async () => {
		const fileName = t.dataset.mod;
		if (!fileName || t.disabled) return;
		t.disabled = true;
		const row = t.closest('.mod-row');
		try {
			const res = await SG.api.toggleMod(inst.id, fileName);
			const on = !!res.enabled;
			t.classList.toggle('on', on);
			t.setAttribute('aria-checked', on ? 'true' : 'false');
			if (row) row.classList.toggle('disabled', !on);
			toast(on ? window.SG.t('mod.enabled', { name: '' }) : window.SG.t('mod.disabled', { name: '' }));
		} catch (err) {
			toast(window.SG.t('mod.toggleFailed') + (err.message || window.SG.t('common.unknownError')));
		} finally {
			t.disabled = false;
		}
	}));
}

/* ================================================================
   Content 内容中心
   ================================================================ */
const CONTENT_CATS = [
	{ key: 'mod', labelKey: 'content.mods' },
	{ key: 'modpack', labelKey: 'content.modpacks' },
	{ key: 'shader', labelKey: 'content.shaders' },
	{ key: 'resourcepack', labelKey: 'content.resourcepacks' },
];
let contentCat = 'mod';
let contentQuery = '';

function contentCardHtml(c) {
	const iconInner = (c.iconUrl && c.iconUrl.startsWith('http'))
		? `<img src="${esc(c.iconUrl)}" alt="" style="width:100%;height:100%;border-radius:10px;object-fit:cover">`
		: esc(c.icon || '📦');
	const badges = (c.versions || []).slice(0, 2).map(v => `<span class="mod-badge">${esc(v)}</span>`).join('');
	return `
	<div class="mod-card">
		<div class="mod-icon">${iconInner}</div>
		<div class="mod-body">
			<div class="mod-top">
				<span class="mod-name">${esc(c.title)}</span>
				<button class="mod-install" data-install="${esc(c.id)}">${esc(t('content.install'))}</button>
			</div>
			<span class="mod-author">${esc(t('content.by', { author: c.author || t('common.unknown') }))}</span>
			<span class="mod-desc">${esc(c.desc || '')}</span>
			<div class="mod-meta">
				<span class="mod-dl"><svg viewBox="0 0 24 24"><path d="M12 3v12M7 10l5 5 5-5M4 20h16"/></svg>${esc(c.downloads || '0')}</span>
				${c.loader ? `<span class="mod-badge">${esc(c.loader)}</span>` : ''}
				${badges}
			</div>
		</div>
	</div>`;
}

async function renderContent() {
	let results = [];
	try { results = await SG.api.searchContent(contentQuery, contentCat); } catch (e) { results = []; }
	const tabs = CONTENT_CATS.map(c => `<button class="filter ${c.key === contentCat ? 'active' : ''}" data-cat="${c.key}">${esc(t(c.labelKey))}</button>`).join('');
	const grid = results.length
		? results.map(contentCardHtml).join('')
		: `<div class="empty-state"><div class="es-icon">🔍</div>${esc(t('content.empty'))}</div>`;
	return `
	<div class="content-view">
		<div class="page-head">
			<div>
				<div class="page-title">${esc(t('content.title'))}</div>
				<div class="page-sub">${esc(t('content.subtitle'))}</div>
			</div>
			<div class="search-box">
				<svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/></svg>
				<input type="text" id="contentSearch" placeholder="${esc(t('content.searchPlaceholder'))}" value="${esc(contentQuery)}">
			</div>
		</div>
		<div class="content-tabs">${tabs}</div>
		<div class="content-grid" id="contentGrid">${grid}</div>
	</div>`;
}

function bindContent(el) {
	const rerenderGrid = async () => {
		const grid = el.querySelector('#contentGrid');
		if (grid) grid.innerHTML = `<div class="loading-state">${esc(t('common.loading'))}</div>`;
		let results = [];
		try { results = await SG.api.searchContent(contentQuery, contentCat); } catch (e) { results = []; }
		if (grid) grid.innerHTML = results.length ? results.map(contentCardHtml).join('') : `<div class="empty-state"><div class="es-icon">🔍</div>${esc(t('content.emptyShort'))}</div>`;
		bindContentInstalls(el);
	};
	el.querySelectorAll('[data-cat]').forEach(b => b.addEventListener('click', () => {
		el.querySelectorAll('[data-cat]').forEach(x => x.classList.remove('active'));
		b.classList.add('active');
		contentCat = b.dataset.cat;
		rerenderGrid();
	}));
	const search = el.querySelector('#contentSearch');
	if (search) {
		let debTimer;
		search.addEventListener('input', () => { contentQuery = search.value; clearTimeout(debTimer); debTimer = setTimeout(rerenderGrid, 350); });
	}
	bindContentInstalls(el);
}
function bindContentInstalls(el) {
	el.querySelectorAll('[data-install]').forEach(b => b.addEventListener('click', async () => {
		const projectId = b.dataset.install;
		if (!projectId || b.dataset.busy) return;
		const card = b.closest('.mod-card');
		const category = contentCat;
		b.dataset.busy = '1';
		b.disabled = true;
		b.textContent = t('content.installing');
		try {
			const res = await SG.api.installContent({ category, projectId, instanceId: selectedInstanceId });
			toast(t('content.queued', { id: res.id }));
			// 轮询任务直至完成（live 模式下有真实进度；mock 立即 completed）
			const poll = async () => {
				let tasks = [];
				try { tasks = await SG.api.getContentTasks(); } catch (e) { tasks = []; }
				const task = tasks.find(x => x.id === res.id);
				if (!task) {
					b.textContent = t('content.installed');
					b.classList.add('installed');
					toast(t('content.installed'));
					return;
				}
				if (task.status === 'completed' || task.status === 'failed') {
					b.textContent = task.status === 'completed' ? t('content.installed') : t('content.installFailed');
					b.classList.add(task.status === 'completed' ? 'installed' : 'failed');
					if (task.status === 'completed') {
						toast(t('content.installed'));
						// modpack 创建了新实例，刷新实例列表
						if (task.category === 'modpack') await loadState(true);
					} else {
						toast(t('content.installFailed') + ': ' + (task.message || t('common.unknownError')));
					}
					return;
				}
				if (card) card.setAttribute('data-progress', Math.round((task.progress || 0) * 100));
				b.textContent = t('content.installing') + ' ' + Math.round((task.progress || 0) * 100) + '%';
				setTimeout(poll, 1200);
			};
			poll();
		} catch (err) {
			b.textContent = t('content.install');
			toast(t('content.installFailed') + ': ' + (err.message || t('common.unknownError')));
		} finally {
			delete b.dataset.busy;
			b.disabled = false;
		}
	}));
}

/* ================================================================
   Worlds 世界
   ================================================================ */
async function renderWorlds() {
	let worlds = [];
	try { worlds = await SG.api.getWorlds(); } catch (e) { worlds = []; }
	const cards = worlds.length ? worlds.map(w => `
		<div class="world-card" data-inst="${esc(w.instanceId)}" data-folder="${esc(w.folderName)}">
			<div class="world-icon" style="background:${w.iconBg || '#2b3040'}">${esc(w.icon || '🌍')}</div>
			<div class="world-meta">
				<span class="world-name">${esc(w.name)}</span>
				<span class="world-sub">${esc(w.instance || t('worlds.unknownInstance'))} · ${esc(w.lastPlayed || '')}</span>
			</div>
			<span class="world-size">${esc(w.size || '—')}</span>
		</div>`).join('')
		: `<div class="empty-state"><div class="es-icon">🗺️</div>${esc(t('worlds.empty'))}</div>`;
	return `
	<div class="content-view">
		<div class="page-head">
			<div>
				<div class="page-title">${esc(t('worlds.title'))}</div>
				<div class="page-sub">${esc(t('worlds.subtitle'))}</div>
			</div>
		</div>
		<div class="list-grid">${cards}</div>
	</div>`;
}
function bindWorlds(el) {
	el.querySelectorAll('.world-card').forEach(c => c.addEventListener('click', async () => {
		const instanceId = c.dataset.inst;
		const folderName = c.dataset.folder;
		const name = c.querySelector('.world-name')?.textContent || folderName;
		if (!instanceId || !folderName) return;
		openWorldActions(instanceId, folderName, name);
	}));
}

/* 存档操作弹窗：备份 / 查看备份 / 打开目录 / 删除 */
async function openWorldActions(instanceId, folderName, name) {
	const backupsHtml = await renderBackupList(instanceId, folderName);
	openModal(`
		<div class="modal-title">${esc(name)}</div>
		<div class="world-actions">
			<button class="modal-btn primary" id="waBackup">${esc(t('worlds.backup'))}</button>
			<button class="modal-btn ghost" id="waOpen">${esc(t('worlds.openFolder'))}</button>
			<button class="modal-btn danger" id="waDelete">${esc(t('worlds.delete'))}</button>
		</div>
		<div class="world-backups">
			<div class="wb-label">${esc(t('worlds.backups'))}</div>
			<div id="wbList">${backupsHtml}</div>
		</div>
		<div class="modal-actions"><button class="modal-btn" id="waClose">${esc(t('common.close'))}</button></div>
	`);
	document.getElementById('waClose').onclick = closeModal;
	document.getElementById('waBackup').onclick = async () => {
		const btn = document.getElementById('waBackup');
		btn.disabled = true;
		btn.textContent = t('worlds.backingUp');
		try {
			await SG.api.createSaveBackup(instanceId, folderName);
			toast(t('worlds.backupDone'));
			closeModal();
		} catch (err) {
			toast(t('worlds.backupFailed') + (err.message || t('common.unknownError')));
		} finally {
			btn.disabled = false;
			btn.textContent = t('worlds.backup');
		}
	};
	document.getElementById('waOpen').onclick = async () => {
		try {
			await SG.api.openSaveDirectory(instanceId, folderName);
			toast(t('worlds.openFolder'));
		} catch (err) { toast(t('worlds.openFailed') + (err.message || t('common.unknownError'))); }
	};
	document.getElementById('waDelete').onclick = async () => {
		if (!confirm(t('worlds.deleteConfirm', { name }))) return;
		try {
			await SG.api.deleteSave(instanceId, folderName);
			toast(t('worlds.deleted'));
			closeModal();
			const v = document.getElementById('view-worlds');
			v.innerHTML = await renderWorlds(); bindWorlds(v);
		} catch (err) { toast(t('worlds.deleteFailed') + (err.message || t('common.unknownError'))); }
	};
	const listEl = document.getElementById('wbList');
	if (listEl) {
		listEl.querySelectorAll('[data-restore]').forEach(b => b.addEventListener('click', async () => {
			const backupName = b.dataset.restore;
			if (!confirm(t('worlds.restoreConfirm', { name: backupName }))) return;
			b.disabled = true;
			try {
				await SG.api.restoreSaveBackup(instanceId, folderName, backupName);
				toast(t('worlds.restored'));
				closeModal();
			} catch (err) {
				toast(t('worlds.restoreFailed') + (err.message || t('common.unknownError')));
				b.disabled = false;
			}
		}));
	}
}

async function renderBackupList(instanceId, folderName) {
	let backups = [];
	try { backups = await SG.api.listSaveBackups(instanceId, folderName); } catch (e) { backups = []; }
	if (!backups.length) return `<div style="padding:8px;color:var(--muted,#8b91a1);font-size:12px">${esc(t('worlds.noBackups'))}</div>`;
	return backups.map(b => `
		<div class="wb-row">
			<div class="wb-info">
				<span class="wb-name">${esc(b.fileName)}</span>
				<span class="wb-sub">${esc(b.sizeText || '')} · ${esc(String(b.createdAt || '').slice(0, 19).replace('T', ' ') || '')}</span>
			</div>
			<button class="modal-btn ghost" data-restore="${esc(b.fileName)}">${esc(t('worlds.restore'))}</button>
		</div>`).join('');
}

/* ================================================================
   Servers 服务器
   ================================================================ */
function serverCardHtml(s) {
	const online = s.online2;
	const pingCls = s.latency == null ? 'ping-off' : s.latency < 60 ? 'ping-good' : 'ping-mid';
	const pingTxt = s.latency == null ? t('servers.offline') : s.latency + ' ms';
	return `
	<div class="server-card" data-server="${esc(s.id)}">
		<div class="server-icon" style="background:${s.iconBg || '#2b3040'}">${esc(s.icon || '🏳️')}</div>
		<div class="server-meta">
			<span class="server-name">${esc(s.name)}</span>
			<span class="server-sub">${esc(s.address)}${s.port && s.port !== 25565 ? ':' + s.port : ''} · ${esc(s.version || t('common.unknown'))}</span>
		</div>
		<div class="server-status">
			<div class="server-players">
				<span class="server-count ${online ? '' : 'offline'}">${online ? (s.online + ' / ' + s.max) : '—'}</span>
				<span class="server-ping ${pingCls}">${pingTxt}</span>
			</div>
			<span class="server-dot ${online ? 'on' : 'off'}"></span>
			<button class="inst-more" data-server-del="${esc(s.id)}" aria-label="${esc(t('servers.deleteConfirm'))}"><svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg></button>
		</div>
	</div>`;
}
async function renderServers() {
	let servers = [];
	try { servers = await SG.api.getServers(); } catch (e) { servers = []; }
	const cards = servers.length ? servers.map(serverCardHtml).join('')
		: `<div class="empty-state"><div class="es-icon">🖥️</div>${esc(t('servers.empty'))}</div>`;
	return `
	<div class="content-view">
		<div class="page-head">
			<div>
				<div class="page-title">${esc(t('servers.title'))}</div>
				<div class="page-sub">${esc(t('servers.subtitle'))}</div>
			</div>
			<button class="btn-new" id="btnAddServer">
				<svg viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg>
				<span>${esc(t('servers.add'))}</span>
			</button>
		</div>
		<div class="list-grid" id="serverList">${cards}</div>
	</div>`;
}
function bindServers(el) {
	el.querySelector('#btnAddServer')?.addEventListener('click', () => {
		openModal(`
			<div class="modal-title">${esc(t('servers.addTitle'))}</div>
			<div class="modal-field"><span class="modal-label">${esc(t('servers.nameLabel'))}</span><input class="modal-input" id="mSvName" placeholder="${esc(t('servers.namePlaceholder'))}"></div>
			<div class="modal-field"><span class="modal-label">${esc(t('servers.hostLabel'))}</span><input class="modal-input" id="mSvHost" placeholder="play.example.com"></div>
			<div class="modal-field"><span class="modal-label">${esc(t('servers.portLabel'))}</span><input class="modal-input" id="mSvPort" value="25565"></div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="mOk">${esc(t('servers.add'))}</button>
			</div>`);
		document.getElementById('mCancel').onclick = closeModal;
		document.getElementById('mOk').onclick = async () => {
			const name = document.getElementById('mSvName').value.trim();
			const host = document.getElementById('mSvHost').value.trim();
			const port = document.getElementById('mSvPort').value.trim() || '25565';
			if (!name || !host) { toast(t('servers.nameHostRequired')); return; }
			closeModal();
			try {
				await SG.api.addServer({ name, host, port });
				const v = document.getElementById('view-servers');
				v.innerHTML = await renderServers(); bindServers(v);
				toast(t('servers.added'));
			} catch (e) { toast(t('servers.addFailed') + (e.message || t('common.unknownError'))); }
		};
	});
	el.querySelectorAll('[data-server-del]').forEach(b => b.addEventListener('click', async e => {
		e.stopPropagation();
		const id = b.dataset.serverDel;
		if (!confirm(t('servers.deleteConfirm'))) return;
		try {
			await SG.api.deleteServer(id);
			const v = document.getElementById('view-servers');
			v.innerHTML = await renderServers(); bindServers(v);
			toast(t('servers.deleted'));
		} catch (err) { toast(t('servers.deleteFailed') + (err.message || t('common.unknownError'))); }
	}));
	el.querySelectorAll('.server-card').forEach(c => c.addEventListener('click', async e => {
		if (e.target.closest('[data-server-del]')) return;
		const id = c.dataset.server;
		toast(t('servers.pinging'));
		try {
			await SG.api.pingServer(id);
			const v = document.getElementById('view-servers');
			v.innerHTML = await renderServers(); bindServers(v);
		} catch (err) { toast(t('servers.pingFailed')); }
	}));
}

/* ================================================================
   Downloads 下载中心
   ================================================================ */
let dlVersionFilter = 'release';
let dlPollTimer = null;

function versionRowHtml(v) {
	const installed = v.installed;
	return `
	<div class="version-row">
		<div class="version-left">
			<span class="version-id">${esc(v.id)}</span>
			<span class="version-type ${v.type}">${v.type === 'snapshot' ? t('dl.snapshot') : t('dl.release')}</span>
			<span class="version-date">${esc(v.releaseTime || '')}</span>
		</div>
		<button class="version-dl-btn ${installed ? 'installed' : ''}" ${installed ? '' : `data-dlver="${esc(v.id)}"`}>${installed ? t('dl.installed') : t('dl.download')}</button>
	</div>`;
}
function dlTaskHtml(t) {
	return `
	<div class="dl-task">
		<div class="dl-task-icon" style="background:${t.iconBg || '#0f766e'}">${esc(t.icon || '⛏️')}</div>
		<div class="dl-task-body">
			<div class="dl-task-top">
				<span class="dl-task-name">${esc(t.name)}</span>
				<span class="dl-task-status">${esc(t.statusLabel || '')} ${t.progress ? (t.progress + '%') : ''}</span>
			</div>
			<div class="dl-progress"><div class="dl-progress-fill" style="width:${t.progress || 0}%"></div></div>
		</div>
	</div>`;
}
async function renderDownloads() {
	let versions = [], tasks = [];
	try { versions = await SG.api.getRemoteVersions(); } catch (e) { versions = []; }
	try { tasks = await SG.api.getDownloads(); } catch (e) { tasks = []; }
	const filtered = versions.filter(v => dlVersionFilter === 'all' ? true : v.type === dlVersionFilter);
	const vlist = filtered.length ? filtered.slice(0, 60).map(versionRowHtml).join('') : `<div class="empty-state">${esc(t('dl.noVersions'))}</div>`;
	const tlist = tasks.length ? tasks.map(dlTaskHtml).join('') : `<div class="empty-state" style="padding:32px"><div class="es-icon">📥</div>${esc(t('dl.noTasks'))}</div>`;
	return `
	<div class="content-view">
		<div class="page-head">
			<div>
				<div class="page-title">${esc(t('dl.title'))}</div>
				<div class="page-sub">${esc(t('dl.subtitle'))}</div>
			</div>
		</div>
		<div class="dl-layout">
			<div>
				<div class="dl-section-title">
					<span>${esc(t('dl.available'))}</span>
					<div class="filters">
						<button class="filter ${dlVersionFilter === 'release' ? 'active' : ''}" data-vfilter="release">${esc(t('dl.release'))}</button>
						<button class="filter ${dlVersionFilter === 'snapshot' ? 'active' : ''}" data-vfilter="snapshot">${esc(t('dl.snapshot'))}</button>
						<button class="filter ${dlVersionFilter === 'all' ? 'active' : ''}" data-vfilter="all">${esc(t('dl.all'))}</button>
					</div>
				</div>
				<div class="version-list" id="versionList">${vlist}</div>
			</div>
			<div>
				<div class="dl-section-title"><span>${esc(t('dl.tasks'))}</span></div>
				<div class="list-grid" id="dlTasks">${tlist}</div>
			</div>
		</div>
	</div>`;
}
function bindDownloads(el) {
	el.querySelectorAll('[data-vfilter]').forEach(b => b.addEventListener('click', async () => {
		dlVersionFilter = b.dataset.vfilter;
		const v = document.getElementById('view-downloads');
		v.innerHTML = await renderDownloads(); bindDownloads(v);
	}));
	el.querySelectorAll('[data-dlver]').forEach(b => b.addEventListener('click', async () => {
		const id = b.dataset.dlver;
		b.textContent = t('dl.queuing'); b.classList.add('installed');
		try {
			await SG.api.downloadVersion(id);
			toast(t('dl.queued', { id }));
			const v = document.getElementById('view-downloads');
			v.innerHTML = await renderDownloads(); bindDownloads(v);
		} catch (e) { toast(t('dl.downloadFailed') + (e.message || t('common.unknownError'))); }
	}));
}

/* ================================================================
   路由 + 交互
   ================================================================ */
const VIEWS = {
	home:          { titleKey: 'view.home',          render: renderHome,          bind: bindHome },
	installations: { titleKey: 'view.installations', render: renderInstallations, bind: bindInstallations },
	worlds:        { titleKey: 'view.worlds',        render: renderWorlds,        bind: bindWorlds },
	servers:       { titleKey: 'view.servers',       render: renderServers,       bind: bindServers },
	content:       { titleKey: 'view.content',       render: renderContent,       bind: bindContent },
	skins:         { titleKey: 'view.skins',         render: renderSkins,         bind: bindSkins },
	downloads:     { titleKey: 'view.downloads',     render: renderDownloads,     bind: bindDownloads },
	settings:      { titleKey: 'view.settings',      render: renderSettings,      bind: bindSettings },
};
let currentView = null;
let navSeq = 0;

async function goTo(nav) {
	const def = VIEWS[nav];
	if (!def) return;
	const myseq = ++navSeq;
	document.querySelectorAll('.view').forEach(v => { v.hidden = true; });
	const el = document.getElementById('view-' + nav);
	if (!el) return;
	if (!el.dataset.rendered) {
		el.innerHTML = '<div style="padding:40px;color:var(--muted,#8b91a1);font-size:13px">' + esc(t('common.loading')) + '</div>';
		try { el.innerHTML = await def.render(); } catch (e) { el.innerHTML = '<div style="padding:40px;color:#e06">' + esc(t('common.loadFailed')) + esc(e.message) + '</div>'; }
		el.dataset.rendered = '1';
		if (def.bind) def.bind(el);
	}
	// 异步渲染期间若已发生更新的导航，放弃本次显示，避免两个视图叠加
	if (myseq !== navSeq) return;
	// 再次断言：只有目标视图可见（消除 async 竞态导致的页面串叠）
	document.querySelectorAll('.view').forEach(v => { v.hidden = (v !== el); });
	document.getElementById('contentPanel').scrollTop = 0;
	document.querySelectorAll('.nav-item').forEach(b => b.classList.toggle('active', b.dataset.nav === nav));
	currentView = nav;
	try { if (('#' + nav) !== location.hash) history.replaceState(null, '', '#' + nav); } catch (e) { /* file:// 下忽略 */ }
}
SG.goTo = goTo;

function invalidate(nav) {
	const el = document.getElementById('view-' + nav);
	if (el) delete el.dataset.rendered;
}

/* ---------------- 启动 ---------------- */
function doLaunch(instanceId, label) {
	if (!instanceId) { toast(t('launch.selectFirst')); return; }
	toast(t('launch.starting', { name: label || t('launch.game') }));
	SG.api.launchInstance(instanceId)
		.then(r => { if (r && r.ok) toast(t('launch.sent')); else toast((r && r.message) || t('launch.failed')); })
		.catch(err => toast(t('launch.failedPrefix') + ((err && err.message) || t('common.unknownError'))));
}

/* ---------------- Play 页 ---------------- */
function bindHome(el) {
	el.querySelectorAll('.news-card').forEach(c => {
		c.addEventListener('click', () => toast(t('home.newsOpen', { title: c.dataset.news })));
	});
	const selEl = el.querySelector('#pfVersionSelect');
	if (selEl) selEl.addEventListener('change', () => { selectedInstanceId = selEl.value; invalidate('installations'); });
	const playBtn = el.querySelector('#btnPlay');
	if (playBtn) playBtn.addEventListener('click', () => {
		const inst = (STATE.instances || []).find(i => i.id === selectedInstanceId);
		doLaunch(selectedInstanceId, inst ? inst.name : t('launch.game'));
	});
}

/* ---------------- Installations 页 ---------------- */
function bindInstallations(el) {
	el.querySelectorAll('.filter').forEach(f => {
		f.addEventListener('click', () => {
			el.querySelectorAll('.filter').forEach(x => x.classList.remove('active'));
			f.classList.add('active');
			const kind = f.dataset.filter;
			el.querySelectorAll('.inst-card').forEach(card => {
				let show = true;
				if (kind === 'mod') show = card.dataset.kind === 'mod';
				else if (kind === 'snap') show = false; // 快照实例暂无标记
				card.style.display = show ? '' : 'none';
			});
		});
	});
	el.querySelector('#btnNewInstall')?.addEventListener('click', async () => {
		let localVersions = [];
		try { const vs = await SG.api.getVersions(); localVersions = vs.local || []; } catch (e) { /* ignore */ }
		const opts = localVersions.length
			? localVersions.map(v => `<option value="${esc(v)}">${esc(v)}</option>`).join('')
			: '<option value="1.21.1">1.21.1</option>';
		openModal(`
			<div class="modal-title">${esc(t('inst.createTitle'))}</div>
			<div class="modal-field"><span class="modal-label">${esc(t('inst.nameLabel'))}</span><input class="modal-input" id="mInstName" placeholder="${esc(t('inst.namePlaceholder'))}"></div>
			<div class="modal-field"><span class="modal-label">${esc(t('inst.versionLabel'))}</span><select class="modal-select" id="mInstVer">${opts}</select></div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="mOk">${esc(t('inst.create'))}</button>
			</div>`);
		document.getElementById('mCancel').onclick = closeModal;
		document.getElementById('mOk').onclick = async () => {
			const name = document.getElementById('mInstName').value.trim();
			const version = document.getElementById('mInstVer').value;
			if (!name) { toast(t('inst.nameRequired')); return; }
			closeModal();
			toast(t('inst.creating', { name }));
			try {
				await SG.api.createInstance({ name, version });
				await loadState(true);
				invalidate('home');
				const v = document.getElementById('view-installations');
				v.innerHTML = await renderInstallations(); bindInstallations(v);
				toast(t('inst.created'));
			} catch (e) { toast(t('inst.createFailed') + (e.message || t('common.unknownError'))); }
		};
	});
	el.querySelectorAll('[data-play]').forEach(b => {
		b.addEventListener('click', e => {
			e.stopPropagation();
			const inst = (STATE.instances || []).find(i => i.id === b.dataset.play);
			doLaunch(b.dataset.play, inst ? inst.name : t('inst.defaultLabel'));
		});
	});
	el.querySelectorAll('[data-more]').forEach(b => {
		b.addEventListener('click', e => {
			e.stopPropagation();
			const id = b.dataset.more;
			const inst = (STATE.instances || []).find(i => i.id === id);
			if (!inst) return;
			openInstanceMenu(inst);
		});
	});
	el.querySelectorAll('.inst-card').forEach(card => {
		card.addEventListener('click', e => {
			if (e.target.closest('[data-play],[data-more]')) return;
			selectedInstanceId = card.dataset.inst;
			el.querySelectorAll('.inst-card').forEach(c => {
				const on = c === card;
				c.classList.toggle('selected', on);
				const pb = c.querySelector('.inst-play');
				if (pb) { pb.classList.toggle('primary', on); pb.classList.toggle('ghost', !on); }
			});
			invalidate('home');
		});
	});
}

/* ---------------- Skins 页 ---------------- */
function bindSkins(el) {
	const active = ACCOUNTS.find(a => a.selected) || ACCOUNTS[0];
	const skins = (active && active.skins) || [];
	el.querySelectorAll('.skin-card').forEach(c => {
		c.addEventListener('click', () => {
			el.querySelectorAll('.skin-card').forEach(x => x.classList.remove('selected'));
			c.classList.add('selected');
			const skin = skins.find(s => s.id === c.dataset.skin);
			if (skin) {
				const canvas = el.querySelector('#previewCanvas');
				if (canvas) canvas.innerHTML = skinThumb(140, skin);
				const nameEl = el.querySelector('#activeSkinName');
				if (nameEl) nameEl.textContent = skin.name || t('skin.item');
				toast(t('skin.preview', { name: skin.name || '' }));
			}
		});
	});
	el.querySelector('#btnUploadSkin')?.addEventListener('click', () => {
		const active = ACCOUNTS.find(a => a.selected) || ACCOUNTS[0];
		if (!active) { toast(t('skin.needAccount')); return; }
		if (active.type !== 'microsoft') { toast(t('skin.needMicrosoft')); return; }
		const input = document.createElement('input');
		input.type = 'file';
		input.accept = 'image/png';
		input.onchange = () => {
			const f = input.files && input.files[0];
			if (!f) return;
			const reader = new FileReader();
			reader.onload = async () => {
				const b64 = String(reader.result || '').replace(/^data:image\/png;base64,/, '');
				const btn = el.querySelector('#btnUploadSkin');
				if (btn) { btn.disabled = true; btn.textContent = t('skin.uploading'); }
				try {
					await SG.api.uploadSkin(active.id, b64, false);
					toast(t('skin.uploaded'));
					// 刷新账号列表（皮肤数量/外观变化）
					await loadState(true);
					invalidate('skins');
					const v = document.getElementById('view-skins');
					v.innerHTML = await renderSkins(); bindSkins(v);
				} catch (err) {
					toast(t('skin.uploadFailed') + (err.message || t('common.unknownError')));
				} finally {
					if (btn) { btn.disabled = false; btn.textContent = t('skin.upload'); }
				}
			};
			reader.readAsDataURL(f);
		};
		input.click();
	});
	el.querySelector('#btnBrowseSkin')?.addEventListener('click', () => SG.goTo('content'));
}

/* ---------------- Settings 页 ---------------- */
function bindSettings(el) {
	// 内存滑块（2–16 GB）→ 真实写入 maxMemoryMb
	const slider = el.querySelector('#memSlider');
	if (slider) {
		const fill = slider.querySelector('.slider-fill');
		const knob = slider.querySelector('.slider-knob');
		const valueEl = el.querySelector('#memValue');
		const MIN = 2, MAX = 16;
		let dragging = false, pendingGb = null, saveTimer = null;
		const commit = () => { if (pendingGb != null) SG.api.updateConfig({ maxMemoryMb: Math.round(pendingGb * 1024), useAutoMemory: false }).catch(() => {}); };
		const setFromClientX = (cx) => {
			const rect = slider.getBoundingClientRect();
			let pct = (cx - rect.left) / rect.width;
			pct = Math.max(0, Math.min(1, pct));
			const gb = Math.round((MIN + pct * (MAX - MIN)) * 2) / 2;
			const realPct = ((gb - MIN) / (MAX - MIN)) * 100;
			fill.style.width = realPct + '%';
			knob.style.left = realPct + '%';
			valueEl.textContent = gb.toFixed(1) + ' GB';
			pendingGb = gb;
			clearTimeout(saveTimer); saveTimer = setTimeout(commit, 300);
		};
		slider.addEventListener('pointerdown', e => { dragging = true; slider.setPointerCapture(e.pointerId); setFromClientX(e.clientX); });
		slider.addEventListener('pointermove', e => { if (dragging) setFromClientX(e.clientX); });
		slider.addEventListener('pointerup', () => { dragging = false; commit(); });
	}
	// 开关 → 真实写入
	el.querySelectorAll('.toggle').forEach(t => {
		t.addEventListener('click', () => {
			const on = t.classList.toggle('on');
			t.setAttribute('aria-checked', on ? 'true' : 'false');
			const key = t.dataset.toggle;
			if (key === 'autoMem') {
				SG.api.updateConfig({ useAutoMemory: on }).catch(() => {});
				const s = el.querySelector('#memSlider');
				if (s) { s.style.opacity = on ? '.4' : ''; s.style.pointerEvents = on ? 'none' : ''; }
				invalidate('home');
			} else if (key === 'mirror') {
				SG.api.setMirror(on).catch(() => {});
			}
		});
	});
	el.querySelector('#btnJrePath')?.addEventListener('click', () => {
		const java = STATE.java || [];
		if (!java.length) { toast(t('set.jreNotFound')); return; }
		const cfg = STATE.config || {};
		const idx = java.findIndex(j => j.path === cfg.javaPath);
		const next = java[(idx + 1) % java.length]; // 循环切换到下一个 JDK
		SG.api.updateConfig({ javaPath: next.path }).then(() => {
			STATE.config.javaPath = next.path;
			el.querySelector('#btnJrePath').textContent = `Java ${next.versionMajor} · ${next.path}`;
			toast(t('set.javaSwitched', { ver: next.versionText }));
		}).catch(err => toast(t('set.switchFailed') + (err.message || '')));
	});
	el.querySelector('#btnOpenLogs')?.addEventListener('click', () => {
		SG.api.openLogDirectory().then(r => {
			if (!r.ok) toast(t('set.logsFailed'));
		}).catch(e => toast(t('set.logsFailed') + (e.message || '')));
	});
	el.querySelector('#btnMsLogin')?.addEventListener('click', async () => {
		const btn = el.querySelector('#btnMsLogin');
		if (!btn || btn.disabled) return;
		btn.disabled = true;
		btn.textContent = t('set.microsoftLoggingIn');
		try {
			await SG.api.microsoftLogin();
			await loadAccounts(true);
			invalidate('skins');
			const v = document.getElementById('view-settings');
			v.innerHTML = await renderSettings(); bindSettings(v);
			refreshSidebarAccount();
			toast(t('set.microsoftSuccess'));
		} catch (e) {
			toast(t('set.microsoftFailed') + (e.message || t('common.unknownError')));
			const b = document.getElementById('view-settings')?.querySelector('#btnMsLogin');
			if (b) { b.disabled = false; b.textContent = t('set.microsoftLogin'); }
		}
	});
	el.querySelector('#btnSignOut')?.addEventListener('click', () => {
		if (ACCOUNTS.length <= 1) { toast(t('set.onlyOneAccount')); return; }
		const cur = ACCOUNTS.findIndex(a => a.selected);
		const next = ACCOUNTS[(cur + 1) % ACCOUNTS.length];
		SG.api.selectAccount(next.id).then(async () => {
			await loadAccounts(true);
			invalidate('skins');
			const v = document.getElementById('view-settings');
			v.innerHTML = await renderSettings(); bindSettings(v);
			refreshSidebarAccount();
			toast(t('set.switchedTo', { name: next.userName }));
		}).catch(err => toast(t('set.switchFailed') + (err.message || '')));
	});
	el.querySelector('#btnAddAccount')?.addEventListener('click', () => {
		openModal(`
			<div class="modal-title">${esc(t('set.addOfflineTitle'))}</div>
			<div class="page-sub">${esc(t('set.addOfflineDesc'))}</div>
			<div class="modal-field"><span class="modal-label">${esc(t('set.playerNameLabel'))}</span><input class="modal-input" id="mAccName" placeholder="Player"></div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="mOk">${esc(t('set.add'))}</button>
			</div>`);
		document.getElementById('mCancel').onclick = closeModal;
		document.getElementById('mOk').onclick = async () => {
			const name = document.getElementById('mAccName').value.trim();
			if (!name) { toast(t('set.playerNameRequired')); return; }
			closeModal();
			try {
				await SG.api.addOfflineAccount(name);
				await loadAccounts(true);
				invalidate('skins');
				const v = document.getElementById('view-settings');
				v.innerHTML = await renderSettings(); bindSettings(v);
				refreshSidebarAccount();
				toast(t('set.accountAdded', { name }));
			} catch (e) { toast(t('set.addFailed') + (e.message || t('common.unknownError'))); }
		};
	});
	addUpdateBindings(el);
}

/* ---------------- Settings 更新区 ---------------- */
function addUpdateBindings(el) {
	initUpdateSection(el);
	el.querySelector('#btnCheckUpdate')?.addEventListener('click', async () => {
		const btn = el.querySelector('#btnCheckUpdate');
		const statusEl = el.querySelector('#updateStatus');
		const verEl = el.querySelector('#updateCurVer');
		if (!btn || btn.disabled) return;
		btn.disabled = true;
		const oldText = btn.textContent;
		btn.textContent = '…';
		try {
			const r = await SG.api.checkUpdate();
			if (verEl && r.currentVersion) verEl.textContent = r.currentVersion;
			if (!r.enabled) { if (statusEl) statusEl.textContent = t('set.updateNotConfigured'); return; }
			if (r.error) { if (statusEl) statusEl.textContent = '❌ ' + r.error; return; }
			if (!r.updateAvailable) {
				if (statusEl) statusEl.textContent = '✅ ' + t('set.upToDate');
			} else {
				if (!statusEl) return;
				statusEl.innerHTML = `
					<div class="update-available">
						<span class="update-title">${esc(t('set.updateAvailable', { ver: r.latestVersion }))}</span>
						${r.notes ? `<div class="update-notes">${esc(r.notes)}</div>` : ''}
						<button class="setting-btn" id="btnDownloadUpdate">${esc(t('set.downloadUpdate'))}</button>
					</div>`;
				el.querySelector('#btnDownloadUpdate')?.addEventListener('click', async () => {
					const db = el.querySelector('#btnDownloadUpdate');
					if (db) { db.disabled = true; db.textContent = '…'; }
					try {
						await SG.api.startUpdateDownload();
						pollUpdateProgress(el, { progress: 0 });
					} catch (e) {
						if (statusEl) statusEl.textContent = '❌ ' + (e.message || '');
					}
				});
			}
		} catch (e) {
			if (statusEl) statusEl.textContent = '❌ ' + t('set.updateCheckFailed') + (e.message || '');
		} finally {
			btn.disabled = false;
			btn.textContent = oldText;
		}
	});
}

function initUpdateSection(el) {
	const verEl = el.querySelector('#updateCurVer');
	const statusEl = el.querySelector('#updateStatus');
	if (!statusEl) return;
	SG.api.getUpdateStatus().then(s => {
		if (!s) return;
		if (verEl && s.currentVersion) verEl.textContent = s.currentVersion;
		if (s.state === 'downloading') pollUpdateProgress(el, s);
		else if (s.state === 'completed') renderUpdateReady(el, s);
		else if (s.state === 'failed') statusEl.textContent = '❌ ' + (s.error || t('set.updateCheckFailed'));
	}).catch(() => {});
}

function renderUpdateReady(el, s) {
	const statusEl = el.querySelector('#updateStatus');
	if (!statusEl) return;
	statusEl.innerHTML = `
		<div class="update-available">
			<span class="update-title">${esc(t('set.updateDownloaded'))}${s.version ? ' v' + esc(s.version) : ''}</span>
			<button class="setting-btn" id="btnApplyUpdate">${esc(t('set.restartToApply'))}</button>
		</div>`;
	el.querySelector('#btnApplyUpdate')?.addEventListener('click', () => {
		SG.api.applyUpdate().then(r => {
			toast((r && r.message) || t('set.updateReady'));
		}).catch(e => toast(t('set.updateCheckFailed') + (e.message || '')));
	});
}

function pollUpdateProgress(el, s) {
	const statusEl = el.querySelector('#updateStatus');
	if (!statusEl) return;
	const pct = Math.round((s.progress || 0) * 100);
	statusEl.innerHTML = `<div class="update-downloading">${esc(t('set.downloading', { pct }))}</div>`;
	const timer = setInterval(async () => {
		try {
			const st = await SG.api.getUpdateStatus();
			const se = el.querySelector('#updateStatus');
			if (!se) { clearInterval(timer); return; }
			if (st.state === 'downloading') {
				se.textContent = t('set.downloading', { pct: Math.round((st.progress || 0) * 100) });
			} else {
				clearInterval(timer);
				if (st.state === 'completed') renderUpdateReady(el, st);
				else se.textContent = '❌ ' + (st.error || t('set.updateCheckFailed'));
			}
		} catch (e) { clearInterval(timer); }
	}, 1000);
}

/* ================================================================
   窗口控制 + 拖拽
   ================================================================ */
function wireWindowControls() {
	document.getElementById('btnMinimize')?.addEventListener('click', () => {
		if (NATIVE_SHELL) sendNative({ type: 'window-control', action: 'minimize' });
	});
	document.getElementById('btnMaximize')?.addEventListener('click', () => {
		if (NATIVE_SHELL) sendNative({ type: 'window-control', action: 'toggle-maximize' });
	});
	document.getElementById('btnClose')?.addEventListener('click', () => {
		if (NATIVE_SHELL) sendNative({ type: 'window-control', action: 'close' });
	});

	const bar = document.getElementById('titleBar');
	if (bar) {
		let drag = null;
		bar.addEventListener('pointerdown', e => {
			if (e.button !== 0 || e.target.closest('button')) return;
			drag = { sx: e.clientX, sy: e.clientY, started: false };
			try { bar.setPointerCapture(e.pointerId); } catch (err) { /* ignore */ }
		});
		bar.addEventListener('pointermove', e => {
			if (!drag) return;
			if (!drag.started && (Math.abs(e.clientX - drag.sx) > 4 || Math.abs(e.clientY - drag.sy) > 4)) {
				drag.started = true;
				if (NATIVE_SHELL) sendNative({ type: 'drag-start' });
			}
			if (drag.started && NATIVE_SHELL) sendNative({ type: 'drag-move' });
		});
		const end = () => { if (drag && drag.started && NATIVE_SHELL) sendNative({ type: 'drag-end' }); drag = null; };
		bar.addEventListener('pointerup', end);
		bar.addEventListener('pointercancel', end);
		bar.addEventListener('dblclick', () => { if (NATIVE_SHELL) sendNative({ type: 'window-control', action: 'toggle-maximize' }); });
	}
}

/* ================================================================
   侧栏账户
   ================================================================ */
function refreshSidebarAccount() {
	const acc = ACCOUNTS.find(a => a.selected) || ACCOUNTS[0];
	if (!acc) return;
	const nameEl = document.getElementById('profileName');
	if (nameEl) nameEl.textContent = acc.userName || 'Player';
	const statusEl = document.getElementById('profileStatus');
	if (statusEl) statusEl.textContent = acc.type === 'microsoft' ? t('sidebar.microsoft') : acc.type === 'authlib' ? t('sidebar.authlib') : t('sidebar.offlineMode');
	const avatarEl = document.getElementById('profileAvatar');
	if (avatarEl) avatarEl.innerHTML = faceThumb(40, acc.skins && acc.skins[0]);
}

/* ================================================================
   初始化
   ================================================================ */
function wireNav() {
	document.getElementById('navigation')?.addEventListener('click', e => {
		const item = e.target.closest('.nav-item');
		if (!item || !item.dataset.nav || item.dataset.nav === currentView) return;
		goTo(item.dataset.nav);
	});
}

/* ---------------- 语言切换 ---------------- */
function wireLangToggle() {
	document.getElementById('btnLangToggle')?.addEventListener('click', () => {
		SG.setLocale(SG.locale === 'zh' ? 'en' : 'zh');
	});
}

// 语言切换后：刷新静态文案 + 侧栏账户 + 丢弃所有已渲染视图缓存并重渲染当前页
SG.onLocaleChange = () => {
	if (SG.applyStaticI18n) SG.applyStaticI18n(document);
	refreshSidebarAccount();
	Object.keys(VIEWS).forEach(invalidate);
	if (currentView) {
		const nav = currentView;
		currentView = null; // 强制 goTo 重新渲染
		goTo(nav);
	}
};

async function init() {
	wireWindowControls();
	wireNav();
	wireLangToggle();
	document.documentElement.setAttribute('lang', SG.locale === 'zh' ? 'zh-CN' : 'en');
	if (SG.applyStaticI18n) SG.applyStaticI18n(document);
	// 预取账户用于侧栏
	loadAccounts().then(refreshSidebarAccount).catch(() => {});
	const startNav = (location.hash || '').replace('#', '');
	await goTo(VIEWS[startNav] ? startNav : 'home');
	window.addEventListener('hashchange', () => {
		const nav = (location.hash || '').replace('#', '');
		if (VIEWS[nav] && nav !== currentView) goTo(nav);
	});
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
else init();

})();
