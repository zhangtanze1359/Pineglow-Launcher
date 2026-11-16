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
// 关键：stateLoadFailedLast 用于区分"未加载"与"上一次失败"。
// loadState 失败时不把失败结果写进 STATE（避免永久缓存），但记录错误消息。
// UI 据此显示"重试实例状态"入口。
let stateLoadFailedLast = null;

async function loadState(force) {
	// 关键：失败态不能永久缓存到 STATE。
	// 否则后续 `if (STATE)` 守卫把失败对象当成有效状态复用，即使服务恢复
	// 资源页也仍用空 gameVersion / loader。
	// 这里：只有成功才缓存；失败时不写 STATE，但用一个模块级标志记录
	// "上一次失败"，供 UI 区分"未加载 / 加载中 / 成功 / 失败"。
	if (STATE && !force) return STATE;
	try {
		STATE = await SG.api.getState();
		STATE.__loadFailed = false;
		stateLoadFailedLast = null;
	} catch (e) {
		// 关键：不要把失败结果写进 STATE。STATE 保持 null，loadState 的下一次
		// 调用（无论显式 retry 还是再次进入资源页）都会重新拉取。
		const err = e && e.message ? e.message : String(e);
		stateLoadFailedLast = err;
		// 兼容：把失败状态也写入 STATE.__loadFailed 标记（不修改 STATE.instances 等）
		// 仅用于 UI 区分显示，不用于缓存。
		if (STATE) {
			STATE.__loadFailed = true;
			STATE.__loadError = err;
		}
		// 清空选中实例，避免安装使用已经查不到的对象。
		selectedInstanceId = null;
		throw e;
	}
	// 选中实例：优先 config.selectedInstanceId，其次首个
	const cfgSel = STATE.config && STATE.config.selectedInstanceId;
	if (cfgSel && STATE.instances.some(i => i.id === cfgSel)) selectedInstanceId = cfgSel;
	else if (STATE.instances.length) selectedInstanceId = STATE.instances[0].id;
	else selectedInstanceId = null;
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

/* 皮肤视觉辅助（steveSvg / skinAvatar / skinThumb / skinFace / faceThumb）
   M1-3 第三步后段：已整体拆至 js/skin-visuals.js（SG.skinVisuals）。
   单独拆一层而不是塞进 skins.js 的理由：faceThumb 同时被设置页（renderAccountCards）
   与侧栏（refreshSidebarAccount）调用，属于跨视图共享的视觉层，不是 skins 私有。
   下列五个薄委托保留旧名字，供本文件的调用点与 SG.__test__ 继续以原入口访问。 */
function steveSvg(scale, palette) { return SG.skinVisuals.steveSvg(scale, palette); }
function skinAvatar(w, url, model) { return SG.skinVisuals.skinAvatar(w, url, model); }
function skinThumb(scale, skin) { return SG.skinVisuals.skinThumb(scale, skin); }
function skinFace(w, url) { return SG.skinVisuals.skinFace(w, url); }
function faceThumb(scale, skin) { return SG.skinVisuals.faceThumb(scale, skin); }

/* 新闻子系统（Phase 3A 状态机、首页新闻区块、新闻页视图、外链二次校验）
   已拆至 js/news.js（SG.news）；launcher 经 SG.news 调用，
   toast/modal/goTo/currentView 在启动时经 SG.news.__wire 单向注入（见文件尾）。 */

/* ================================================================
   视图渲染（真实数据）
   ================================================================ */
async function renderHome() {
	// Phase 3A：首页不等待新闻——先渲染骨架，新闻异步到位后就地刷新
	SG.news.loadNews().then(() => SG.news.refreshSection('home'));
	await loadState();
	const insts = STATE.instances || [];
	const sel = insts.find(i => i.id === selectedInstanceId) || insts[0];
	let totalPlaySeconds = 0, totalPlayCount = 0;
	try {
		const st = await SG.api.getStats();
		if (st) { totalPlaySeconds = st.totalPlaySeconds || 0; totalPlayCount = st.totalPlayCount || 0; }
	} catch (e) { /* 忽略统计加载失败 */ }

	const options = insts.map(i => `<option value="${esc(i.id)}"${i.id === (sel && sel.id) ? ' selected' : ''}>${esc(i.name)} · ${esc(i.version || '—')}</option>`).join('');

	return redesignedHome(insts, sel, options, totalPlaySeconds, totalPlayCount);
}

function uiText(zh,en) { return SG.locale === 'en' ? en : zh; }
const navigationCounts = {};
function uiPlayTime(seconds) {
 if(SG.locale==='en')return esc(fmtPlayTime(seconds));
 const s=Math.max(0,Math.floor(Number(seconds)||0)),m=Math.floor(s/60),h=Math.floor(m/60);
 return h?`${h} <small>小时</small>${m%60?' '+m%60+' <small>分</small>':''}`:m?`${m} <small>分</small>`:`${s} <small>秒</small>`;
}
function uiIcon(name) {
 const paths={play:'<path d="M6 4l14 8-14 8V4Z"/>',cube:'<path d="m12 3 9 5v9l-9 5-9-5V8l9-5Zm0 10v9M3 8l9 5 9-5"/>',layers:'<path d="m12 3 9 5-9 5-9-5 9-5ZM3 12l9 5 9-5M3 16l9 5 9-5"/>',world:'<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a16 16 0 0 1 0 18 16 16 0 0 1 0-18Z"/>',server:'<rect x="3" y="4" width="18" height="6" rx="2"/><rect x="3" y="14" width="18" height="6" rx="2"/><path d="M7 7h.1M7 17h.1"/>',folder:'<path d="M3 6h6l2 2h10v12H3V6Z"/>',download:'<path d="M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4"/>',search:'<circle cx="11" cy="11" r="7"/><path d="m16 16 5 5"/>',trash:'<path d="M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7"/>',close:'<path d="m6 6 12 12M6 18 18 6"/>',user:'<circle cx="12" cy="8" r="4"/><path d="M4 22v-3a8 8 0 0 1 16 0v3"/>'};
 return `<svg viewBox="0 0 24 24" aria-hidden="true">${paths[name]||paths.cube}</svg>`;
}
function uiSearch(id,label,value='') { return `<label class="search-box">${uiIcon('search')}<input type="search" id="${id}" value="${esc(value)}" placeholder="${esc(label)}" aria-label="${esc(label)}"></label>`; }
function bindListSearch(el,id,selector) {
 const input=el.querySelector('#'+id);if(!input)return;
 const apply=()=>{const q=input.value.trim().toLocaleLowerCase();el.querySelectorAll(selector).forEach(row=>{const kind=row.dataset.kind;const filter=el.querySelector('[data-filter].active')?.dataset.filter||'all';row.hidden=!row.textContent.toLocaleLowerCase().includes(q)||(kind&&filter!=='all'&&kind!==filter);});};
 input.addEventListener('input',apply);el.querySelectorAll('[data-filter]').forEach(b=>b.addEventListener('click',apply));
}

function redesignedHome(insts, sel, options, totalPlaySeconds, totalPlayCount) {
 const chips=sel?`<span class="ui-chip">${uiIcon('cube')}${esc(sel.version||'—')}</span><span class="ui-chip">${esc(sel.loader||'Vanilla')}</span><span class="ui-chip">${uiIcon('layers')}${sel.modsCount||0} Mod</span>`:'';
 return `<div class="new-home"><section class="home-hero"><div class="home-hero-art" aria-hidden="true"></div><div class="home-hero-content"><div class="home-launch-row"><div class="home-instance-info"><span class="home-kicker">${uiText('当前实例','Current instance')} · PINEGLOW JAVA EDITION</span><h2 class="home-game-title">${esc(sel?sel.name:t('home.noInstances'))}</h2><div class="home-chips">${chips}</div></div><div class="home-launch-controls"><button class="play-btn" id="btnPlay"${sel?'':' disabled'}>${uiIcon('play')}<span>${esc(t('home.play'))}</span></button><button class="ui-button" id="btnHomeInstances">${esc(t('shell.selectInstance'))}</button></div></div><div class="home-stats"><div class="home-stat">${esc(t('home.statsPlayTime'))}<b>${uiPlayTime(totalPlaySeconds)}</b></div><div class="home-stat">${esc(t('home.statsPlayCount'))}<b>${totalPlayCount}</b></div><div class="home-stat">${esc(t('home.statsInstances'))}<b>${insts.length}</b></div><div class="home-stat">${esc(t('shell.memory'))}<b>${((STATE.config&&STATE.config.maxMemoryMb)||4096)/1024} <small>GB</small></b></div></div></div></section><div class="home-bottom"><section class="ui-card"><div class="ui-card-head"><h2>${esc(t('shell.queue'))}</h2><button class="news-more" id="homeAllDownloads">${esc(t('shell.allTasks'))}</button></div><div id="homeDownloadQueue" class="ui-muted">${esc(t('common.loading'))}</div></section><section class="ui-card news-section"><div class="ui-card-head"><h2>${esc(t('home.recentNews'))}</h2><button class="news-more" id="newsMore">${esc(t('home.newsMore'))}</button></div><div class="news-grid" id="homeNewsSection">${SG.news.homeNewsInnerHtml()}</div></section></div></div>`;
}

async function renderInstallations() {
	await loadState();
	const insts = STATE.instances || [];
	const cards = insts.map(i => {
		const isSel = i.id === selectedInstanceId;
		const kind = /(?:w\d+[a-z]$|snapshot|pre\d|rc\d)/i.test(i.version||'') ? 'snap' : (i.loader && i.loader !== 'Vanilla') ? 'mod' : 'all';
		const playInfo = (i.totalPlaySeconds > 0 || i.playCount > 0)
			? ` · ${esc(t('inst.played', { n: i.playCount || 0, t: fmtPlayTime(i.totalPlaySeconds) }))}`
			: '';
		const sub = `Minecraft ${esc(i.version || '—')} · ${esc(i.lastPlayed || t('inst.neverPlayed'))}${playInfo}`;
		return `
		<div class="inst-card ${isSel ? 'selected' : ''}" data-inst="${esc(i.id)}" data-kind="${kind}">
			<div class="inst-left">
				<div class="inst-icon">${uiIcon(kind==='mod'?'layers':'cube')}</div>
				<div class="inst-meta">
					<div class="inst-name-row">
						<span class="inst-name">${esc(i.name)}</span>
						<span class="inst-tag">${esc(i.loader || 'Vanilla')}</span>${isSel?`<span class="ui-chip chip-ok">${uiText('默认','Default')}</span>`:''}${i.modsCount?`<span class="ui-chip" data-instance-mod-count="${esc(i.id)}">${i.modsCount} Mod</span>`:''}
					</div>
					<span class="inst-sub">${sub}</span>
				</div>
			</div>
			<div class="inst-actions">
				${kind === 'mod' ? `<button class="ui-button inst-mods" data-mods="${esc(i.id)}">Mod</button>` : ''}
				<button class="inst-play ${isSel ? 'primary' : 'ghost'}" data-play="${esc(i.id)}">${esc(t('inst.play'))}</button>
				<button class="inst-more" data-more="${esc(i.id)}" aria-label="${esc(t('shell.more', { name: i.name }))}"><svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg></button>
			</div>
		</div>`;
	}).join('');

	const empty = insts.length ? '' : `<div style="padding:48px;text-align:center;color:var(--muted,#8b91a1)">${esc(t('inst.empty'))}</div>`;

	return `
	<div class="installations-view ui-card">
		<div class="page-head">
			<div><h1 class="page-title">${esc(t('inst.title'))}</h1><p class="page-sub">${esc(t('inst.subtitle'))}</p></div>
		</div>
		<div class="top-control"><div class="list-heading"><h2>${uiText('你的游戏实例','Your game instances')}</h2><p>${insts.length} ${uiText('个实例可用 · 版本与加载器互相独立','instances · separate versions and loaders')}</p></div>${uiSearch('instanceSearch',uiText('搜索实例…','Search instances…'))}
			<div class="filters">
				<button class="filter active" data-filter="all">${esc(t('inst.all'))}</button>
				<button class="filter" data-filter="mod">${esc(t('inst.modded'))}</button>
				<button class="filter" data-filter="snap">${esc(t('inst.snapshots'))}</button>
			</div>
			<button class="btn-new" id="btnAddExisting">
				<svg viewBox="0 0 24 24"><path d="M3 7h6l2 2h10v10H3z"/></svg>
				<span>${esc(t('inst.addExisting'))}</span>
			</button>
			<button class="btn-new" id="btnNewInstall">
				<svg viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg>
				<span>${esc(t('inst.new'))}</span>
			</button>
		</div>
		<div class="inst-list" id="instList">${cards}${empty}</div>
	</div>`;
}

async function renderSkins() { return SG.skins.render(); }




/* ================================================================
   通用弹窗 Modal
   Gate R2 修复 P0-1：使用 modalCleanupTimer + modalGeneration，
   旧延迟回调只清理属于自己代次的 modal；新 modal 不会被旧 cleanup 清空。
   ================================================================ */
let modalCleanupTimer = null;
let modalGeneration = 0;
/* ================================================================
   MR17 无障碍：弹窗的键盘与焦点契约
   ----------------------------------------------------------------
   改动前：弹窗只有鼠标路径 —— 没有 Escape、没有焦点陷阱、关闭后焦点丢失，
   而且 role="button" 的元素虽然可聚焦却**无法用键盘激活**。
   本块补上这四件事，均为最小改动，不换 UI 框架。
   ================================================================ */

// 打开弹窗前记住触发元素；关闭后把焦点还回去（键盘用户不会"丢失位置"）。
let modalReturnFocus = null;
let modalKeyHandler = null;

/** 弹窗内可聚焦元素（过滤掉禁用/不可见/显式排除项）。 */
function modalFocusable(host) {
	const out = [];
	const push = sel => {
		try {
			host.querySelectorAll(sel).forEach(el => { if (el && !out.includes(el)) out.push(el); });
		} catch (e) { /* 选择器不被支持时跳过这一类 */ }
	};
	// 刻意用多个**简单**选择器分别查询再合并：比一条带 :not() 的复合选择器兼容性更好。
	push('button');
	push('a[href]');
	push('input');
	push('select');
	push('textarea');
	push('[tabindex]');
	return out.filter(el => !el.disabled
		&& el.getAttribute('tabindex') !== '-1'
		&& !el.hidden
		&& el.offsetParent !== null);
}

/** Escape 关闭 + Tab 在弹窗内循环（避免焦点跑到被遮住的页面上）。 */
function installModalKeyboard(host) {
	modalKeyHandler = e => {
		if (host.hidden || !host.classList.contains('show')) return;
		if (e.key === 'Escape') { e.preventDefault(); closeModal(); return; }
		if (e.key !== 'Tab') return;
		const items = modalFocusable(host);
		if (items.length === 0) return;
		const first = items[0];
		const last = items[items.length - 1];
		const active = document.activeElement;
		const inside = host.contains(active);
		if (e.shiftKey && (active === first || !inside)) { e.preventDefault(); last.focus(); }
		else if (!e.shiftKey && (active === last || !inside)) { e.preventDefault(); first.focus(); }
	};
	document.addEventListener('keydown', modalKeyHandler, true);
}

/** 危险操作不得默认获得焦点：把焦点给第一个非破坏性按钮，没有才给弹窗本身。 */
function focusModalInitial(host) {
	const dialog = host.querySelector('.modal');
	const focusables = modalFocusable(host);
	// 优先「取消/关闭/完成」这类安全按钮；只有它们不存在时才落到第一个可聚焦元素。
	const safe = focusables.find(el => el.classList && (
		el.classList.contains('ghost') || el.id === 'dConfirmCancel' || /close|cancel/i.test(el.id || '')));
	const target = safe || focusables[0] || dialog;
	if (target && typeof target.focus === 'function') {
		// 用 getAttribute 判空（与 hasAttribute 语义等价），兼容既有测试替身。
		if (target === dialog && dialog.getAttribute('tabindex') == null) dialog.setAttribute('tabindex', '-1');
		target.focus();
	}
}

// role="button" 但非原生 <button> 的元素（账户卡片、删除账户等）必须能用 Enter/Space 激活。
// 原生 <button> 会被排除，避免双重触发。
document.addEventListener('keydown', e => {
	if (e.key !== 'Enter' && e.key !== ' ') return;
	const target = e.target;
	const el = target && target.closest ? target.closest('[role="button"]') : null;
	if (!el || el.tagName === 'BUTTON') return;
	e.preventDefault();
	el.click();
});

function openModal(html) {
	const host = document.getElementById('modalHost');
	if (!host) return null;
	if (modalCleanupTimer) { clearTimeout(modalCleanupTimer); modalCleanupTimer = null; }
	const myGen = ++modalGeneration;
	// 只有"从页面里"打开弹窗时才更新返回焦点；弹窗内换弹窗不应把返回点设成弹窗内部元素。
	if (!host.contains(document.activeElement)) modalReturnFocus = document.activeElement;
	host.innerHTML = '<div class="modal">' + html + '</div>';
	delete host.dataset.drawer;
	host.hidden = false;
	host.dataset.modalGeneration = String(myGen);
	// 对话框语义：屏幕阅读器会以"对话框"宣读，并知道背景不可交互。
	host.setAttribute('role', 'dialog');
	host.setAttribute('aria-modal', 'true');
	const titleEl = host.querySelector('.modal-title');
	host.setAttribute('aria-label', (titleEl && titleEl.textContent) ? titleEl.textContent.trim() : t('common.done'));
	requestAnimationFrame(() => host.classList.add('show'));
	const close = () => closeModal();
	host.onclick = e => { if (e.target === host) close(); };
	installModalKeyboard(host);
	focusModalInitial(host);
	return host;
}
function closeModal() {
	const host = document.getElementById('modalHost');
	if (!host) return;
	host.classList.remove('show');
	// 卸掉键盘处理并移除对话框语义，避免下一次打开时叠加监听器。
	if (modalKeyHandler) { document.removeEventListener('keydown', modalKeyHandler, true); modalKeyHandler = null; }
	host.removeAttribute('role');
	host.removeAttribute('aria-modal');
	host.removeAttribute('aria-label');
	// 把焦点还给触发元素（还在文档里才还，避免焦点落到已消失的节点）。
	const back = modalReturnFocus;
	modalReturnFocus = null;
	if (back && document.contains(back) && typeof back.focus === 'function') {
		try { back.focus(); } catch { }
	}
	if (modalCleanupTimer) { clearTimeout(modalCleanupTimer); modalCleanupTimer = null; }
	const myGen = parseInt(host.dataset.modalGeneration || '0', 10);
	// Phase 3B v2 修复 P0-1/P0-4：先调用当前代次注册的 aborter（abort pending diagnose），
	// 再排队清理 DOM。重复 closeModal 幂等。
	const aborter = _modalAborters.get(myGen);
	if (typeof aborter === 'function') {
		try { aborter(); } catch { }
		unregisterModalAborter(myGen);
	}
	modalCleanupTimer = setTimeout(() => {
		const curGen = parseInt(host.dataset.modalGeneration || '0', 10);
		if (curGen === myGen) {
			host.hidden = true;
			host.innerHTML = '';
			host.onclick = null;
		}
		modalCleanupTimer = null;
	}, 180);
}

// Phase 3B v2 修复 P0-1/P0-4：closeModal 优先 abort 当前代次注册的 controller。
// 其他弹窗不应受影响；只有 openAiDiagnose 显式注册。
const _modalAborters = new Map();
function registerModalAborter(gen, aborter) {
	if (typeof aborter === 'function') _modalAborters.set(gen, aborter);
}
function unregisterModalAborter(gen) { _modalAborters.delete(gen); }

/* ================================================================
   AI 诊断错误码显式映射（Gate R2 修复 P1-2）
   snake_case 后端错误码 → camelCase i18n 键。禁止直接拼字符串。
   ================================================================ */
function mapAiErrorCodeToI18nKey(code) {
	const map = {
		ai_auth_failed: 'diag.err.aiAuthFailed',
		ai_rate_limited: 'diag.err.aiRateLimited',
		ai_timeout: 'diag.err.aiTimeout',
		ai_response_too_large: 'diag.err.aiResponseTooLarge',
		ai_invalid_content_type: 'diag.err.aiInvalidContentType',
		ai_invalid_response: 'diag.err.aiInvalidResponse',
		ai_service_error: 'diag.err.aiServiceError',
		ai_cancelled: 'diag.err.aiCancelled',
		endpoint_not_allowed: 'diag.err.endpointNotAllowed',
		confirmation_required: 'diag.err.confirmationRequired',
		unsupported_protocol: 'diag.err.unsupportedProtocol',
		invalid_response_format: 'diag.err.aiInvalidResponse',
		ai_unavailable: 'diag.err.aiServiceError',
	};
	return Object.prototype.hasOwnProperty.call(map, code || '') ? map[code] : null;
}

/* ================================================================
   AI endpoint allowlist 前端镜像（Gate R2 修复 P1-3）
   与后端 AiEndpointPolicy 保持一致；用于 settings 页与确认面板的 UI 提示。
   ================================================================ */
const AI_ENDPOINT_ALLOWLIST_FRONTEND = ['https://api.deepseek.com/v1', 'https://api.minimaxi.com/v1'];
function isFrontendEndpointAllowed(baseUrl) {
	if (!baseUrl || typeof baseUrl !== 'string') return false;
	const normalized = baseUrl.trim().replace(/\/+$/, '');
	return AI_ENDPOINT_ALLOWLIST_FRONTEND.indexOf(normalized) !== -1;
}

/* 当前选中的 AI provider（仅显示名 + 模型，不暴露 API Key） */
function getActiveAiProviderInfo() {
	try {
		const cfg = (typeof STATE !== 'undefined' && STATE && STATE.config) ? STATE.config : (window.STATE && window.STATE.config) || null;
		if (!cfg) return { name: '—', model: '—', allowed: false };
		const list = cfg.aiProviders || [];
		const cur = list.find(p => p.id === cfg.selectedAiProviderId);
		if (!cur) return { name: '—', model: '—', allowed: false };
		return {
			name: cur.name || '—',
			model: cur.model || '—',
			allowed: isFrontendEndpointAllowed(cur.baseUrl),
		};
	} catch (_) {
		return { name: '—', model: '—', allowed: false };
	}
}
SG.closeModal = closeModal;

/* ================================================================
   AI 提供商管理（多提供商预设）
   ================================================================ */
// 内置预设模板（前端常量，纯填表；选中后仍可自由编辑 URL/模型/Key）
const AI_PRESETS = {
	deepseek: { name: 'DeepSeek', baseUrl: 'https://api.deepseek.com/v1', model: 'deepseek-chat' },
	minimax_cn: { name: 'MiniMax（中国区）', baseUrl: 'https://api.minimaxi.com/v1', model: 'MiniMax-M3' },
	openai:   { name: 'OpenAI',   baseUrl: 'https://api.openai.com/v1',   model: 'gpt-4o-mini' },
	qwen:     { name: '通义千问', baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1', model: 'qwen-plus' },
	zhipu:    { name: '智谱',     baseUrl: 'https://open.bigmodel.cn/api/paas/v4', model: 'glm-4-flash' },
	ollama:   { name: 'Ollama',   baseUrl: 'http://localhost:11434/v1',   model: 'llama3.1' },
};

// 当前选中提供商显示名（设置区）

function aiActiveProvider(cfg) {
	const list = cfg.aiProviders || [];
	return list.find(p => p.id === cfg.selectedAiProviderId) || null;
}

// 管理弹窗：列表 + 新增/编辑/设为当前/删除
function makeAiProviderId() {
	return 'ai-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 8);
}

function openAiManage() {
	const cfg = STATE.config || {};
	const list = cfg.aiProviders || [];
	const rows = list.length ? list.map(p => {
		const active = p.id === cfg.selectedAiProviderId;
		const supported = isFrontendEndpointAllowed(p.baseUrl);
		const unsupportedBadge = supported ? '' : ' <span class="ai-unsupported">' + esc(t('diag.safetyModeBlocked') || t('diag.safetyModeNotice')) + '</span>';
		const activeBtn = active ? '' : (supported
			? `<button class="modal-btn ghost small" data-act="active">${esc(t('set.aiSetActive'))}</button>`
			: `<button class="modal-btn ghost small" data-act="active" disabled title="${esc(t('diag.safetyModeBlocked') || t('diag.safetyModeNotice'))}">${esc(t('set.aiSetActive'))}</button>`);
		return `
			<div class="ai-row" data-id="${esc(p.id)}">
				<div class="ai-info">
					<div class="ai-name">${esc(p.name)}${active ? ' <span class="ai-active">' + esc(t('set.aiActive')) + '</span>' : ''}${unsupportedBadge}</div>
					<div class="ai-sub">${esc(p.baseUrl || '—')} · ${esc(p.model || '—')} · ${p.keyConfigured ? '🔑' : ''}</div>
				</div>
				<div class="ai-actions">
					<button class="modal-btn ghost small" data-act="edit">${esc(t('set.aiEdit'))}</button>
					${activeBtn}
					<button class="modal-btn ghost small danger" data-act="del">${esc(t('set.aiDelete'))}</button>
				</div>
			</div>`;
	}).join('') : `<div class="ai-empty">${esc(t('set.aiNoProviders'))}</div>`;

	openModal(`
		<div class="modal-title">${esc(t('set.aiSection'))}</div>
		<div class="page-sub">${esc(t('set.aiEnableDesc'))}</div>
		<div class="ai-list">${rows}</div>
		<div class="modal-actions">
			<button class="modal-btn ghost" id="aiClose">${esc(t('common.close'))}</button>
			<button class="modal-btn primary" id="aiAdd">${esc(t('set.aiAdd'))}</button>
		</div>`);

	document.getElementById('aiClose').onclick = closeModal;
	document.getElementById('aiAdd').onclick = () => openAiEditor(null);
	document.querySelectorAll('.ai-row').forEach(row => {
		const id = row.dataset.id;
		row.querySelector('[data-act="edit"]').onclick = () => {
			const p = (STATE.config.aiProviders || []).find(x => x.id === id);
			if (p) openAiEditor(p);
		};
		const activeBtn = row.querySelector('[data-act="active"]');
		if (activeBtn) activeBtn.onclick = async () => {
			await saveAiProviders({ selectedAiProviderId: id });
			addRowRefresh();
		};
		const delBtn = row.querySelector('[data-act="del"]');
		if (delBtn) delBtn.onclick = async () => {
			if (!confirm(t('set.aiDeleteConfirm'))) return;
			const rest = (STATE.config.aiProviders || []).filter(x => x.id !== id);
			await saveAiProviders({ aiProviders: rest, selectedAiProviderId: id === STATE.config.selectedAiProviderId ? '' : STATE.config.selectedAiProviderId });
			addRowRefresh();
		};
	});
}

// 编辑/新增视图
function openAiEditor(profile) {
	const cfg = STATE.config || {};
	const isNew = !profile;
	const p = profile || { id: makeAiProviderId(), name: '', baseUrl: '', model: '', apiKey: '' };
	const presets = Object.entries(AI_PRESETS).map(([k, v]) => {
		const fill = v.baseUrl === p.baseUrl && v.model === p.model;
		const supported = isFrontendEndpointAllowed(v.baseUrl);
		const label = supported ? esc(v.name) : `${esc(v.name)} (${esc(t('diag.safetyModeBlocked') || t('diag.safetyModeNotice'))})`;
		return `<option value="${k}" ${fill ? 'selected' : ''}${supported ? '' : ' disabled'}>${label}</option>`;
	}).join('') + `<option value="_custom">${esc(t('set.aiPresetCustom'))}</option>`;

	openModal(`
		<div class="modal-title">${isNew ? esc(t('set.aiAdd')) : esc(t('set.aiEdit'))}</div>
		<div class="modal-field"><span class="modal-label">${esc(t('set.aiNameName'))}</span><input class="modal-input" id="aiName" value="${esc(p.name)}" placeholder="DeepSeek"></div>
		<div class="modal-field"><span class="modal-label">${esc(t('set.aiPresetName'))}</span><select class="modal-input" id="aiPreset">${presets}</select></div>
		<div class="modal-field"><span class="modal-label">${esc(t('set.aiUrlName'))}</span><input class="modal-input" id="aiUrl" value="${esc(p.baseUrl)}" placeholder="https://api.deepseek.com/v1"></div>
		<div class="modal-field"><span class="modal-label">${esc(t('set.aiModelName'))}</span><input class="modal-input" id="aiModel" value="${esc(p.model)}" placeholder="deepseek-chat"></div>
		<div class="modal-field"><span class="modal-label">${esc(t('set.aiKeyName'))}</span><input class="modal-input" id="aiKey" type="password" placeholder="${isNew ? esc(t('set.aiKeyForNew')) : esc(t('set.aiKeyPlaceholder'))}"></div>
		<div class="modal-actions">
			<button class="modal-btn ghost" id="aiCancel">${esc(t('common.cancel'))}</button>
			<button class="modal-btn primary" id="aiOk">${esc(t('common.save'))}</button>
		</div>`);

	// 预设下拉选择 → 自动填充（可再手动改）
	const sel = document.getElementById('aiPreset');
	sel.addEventListener('change', () => {
		const key = sel.value;
		if (key === '_custom' || !AI_PRESETS[key]) return;
		const pr = AI_PRESETS[key];
		document.getElementById('aiName').value = pr.name;
		document.getElementById('aiUrl').value = pr.baseUrl;
		document.getElementById('aiModel').value = pr.model;
	});

	document.getElementById('aiCancel').onclick = closeModal;
	document.getElementById('aiOk').onclick = async () => {
		const name = document.getElementById('aiName').value.trim();
		const baseUrl = document.getElementById('aiUrl').value.trim();
		const model = document.getElementById('aiModel').value.trim();
		const apiKey = document.getElementById('aiKey').value;
		if (!name || !baseUrl) { toast(t('set.aiNameRequired')); return; }
		// Phase 3B v2 修复 P1-3：保存前校验 baseUrl 是否在 allowlist。
		// 不在 allowlist 的自定义 URL 可以保存为非活动草稿，但绝不能成为 current。
		const isAllowed = isFrontendEndpointAllowed(baseUrl);
		const cur = (STATE.config.aiProviders || []).slice();
		const existingP = cur.find(x => x.id === p.id);
		const item = { id: p.id, name, baseUrl, model, keyConfigured: apiKey ? true : !!(existingP && existingP.keyConfigured) };
		if (apiKey) item.apiKey = apiKey;
		const idx = cur.findIndex(x => x.id === p.id);
		if (idx >= 0) cur[idx] = item; else cur.push(item);
		// 新增项若列表之前为空或未选中任何提供商，则默认选中
		let selId = STATE.config.selectedAiProviderId || '';
		if (!selId && isAllowed) selId = item.id;
		await saveAiProviders({ aiProviders: cur, selectedAiProviderId: selId });
		if (!isAllowed) {
			toast(t('diag.safetyModeBlocked') || t('diag.safetyModeNotice'));
		}
		addRowRefresh();
	};
}

// 保存提供商配置并刷新设置区
async function saveAiProviders(extra) {
	const cfg = STATE.config || {};
	const payload = Object.assign({
		aiProviders: cfg.aiProviders || [],
		selectedAiProviderId: cfg.selectedAiProviderId || '',
	}, extra || {});
	try {
		const res = await SG.api.updateConfig(payload);
		if (res && res.config) Object.assign(STATE.config, res.config);
		else Object.assign(STATE.config, payload);
		toast(t('set.aiSaved'));
		invalidate('settings');
		const v = document.getElementById('view-settings');
		if (v) { v.innerHTML = await renderSettings(); bindSettings(v); }
	} catch (err) { toast(t('set.aiSaveFailed') + (err.message || t('common.unknownError'))); }
}

function addRowRefresh() { openAiManage(); }

/* 实例操作菜单（Mod 管理 + 删除） */
function openInstanceMenu(inst) {
	openModal(`
		<div class="modal-title">${esc(inst.name)}</div>
		<div class="page-sub">Minecraft ${esc(inst.version || '—')} · ${esc(inst.loader || 'Vanilla')}</div>
		<div class="modal-actions" style="justify-content:space-between;margin-top:8px">
			<div style="display:flex;gap:10px">
				<button class="modal-btn ghost" id="mMods">${esc(t('inst.manageMods'))}</button>
				<button class="modal-btn ghost" id="mDiagnose">${esc(t('diag.button'))}</button>
				<button class="modal-btn ghost" id="mBisect">${esc(t('bisec.button'))}</button>
				<button class="modal-btn ghost" id="mRecovery">${esc(t('recovery.menu'))}</button>
			</div>
			<div style="display:flex;gap:10px">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.close'))}</button>
				<button class="modal-btn" id="mDel" style="background:#e0463c;color:#fff">${esc(t('inst.delete'))}</button>
			</div>
		</div>`);
	document.getElementById('mCancel').onclick = closeModal;
	document.getElementById('mMods').onclick = () => openModManager(inst);
	document.getElementById('mDiagnose').onclick = () => { closeModal(); openAiDiagnose(inst); };
	document.getElementById('mBisect').onclick = () => { closeModal(); openModBisect(inst); };
	document.getElementById('mRecovery').onclick = () => { closeModal(); openRecoveryPanel(inst); };
	const delBtn = document.getElementById('mDel');
	document.getElementById('mDel').onclick = async () => {
		// G2/INSTANCE-04（有效 RED）：请求在飞行中时重复点击不得产生第二次移除请求。
		// 原来的处理函数没有任何忙态判断，"同一 tick 双击 / 关闭动画期间再点"会发两次删除。
		if (delBtn.disabled) return;
		delBtn.disabled = true;
		try {
			// MR03：这里的语义是"从列表移除"，不是删除文件。确认框必须说明这一点，
			// 并给出将被保留的游戏目录与存档数量，避免用户以为存档被删。
			const dir = inst.gameDirectory || t('inst.defaultLabel');
			const saves = (typeof inst.savesCount === 'number') ? inst.savesCount : 0;
			let msg = t('inst.deleteConfirm', { name: inst.name, dir: dir, saves: String(saves) });
			if (inst.sharedGameDirectory) msg += t('inst.deleteSharedWarn');
			if (!confirm(msg)) return; // 取消：不发请求、不改配置
			closeModal();
			await SG.api.deleteInstance(inst.id);
			await loadState(true);
			invalidate('home');
			const v = document.getElementById('view-installations');
			v.innerHTML = await renderInstallations(); bindInstallations(v);
			toast(t('inst.deleted'));
		} catch (err) {
			toast(t('inst.deleteFailed') + (err.message || t('common.unknownError')));
		} finally {
			// 取消或失败后按钮必须重新可用（成功时列表已重建，忙态随节点一起丢弃）。
			if (delBtn.isConnected) delBtn.disabled = false;
		}
	};
}

/* Mod 管理弹窗 */
async function openModManager(inst) {
 const host = openModal(`<div class="drawer-head"><div><div class="modal-title">${esc(t('mod.manageTitle', { name: inst.name }))}</div><p>${esc(inst.version||'—')} · ${esc(inst.loader||'Vanilla')} · <span data-mod-count>—</span> Mod · ${uiText('恢复区','Recovery')} <span data-recovery-count>—</span></p></div><button class="ui-button drawer-close" id="mCancel" aria-label="${esc(t('common.close'))}">${uiIcon('close')}</button></div><div class="drawer-tabs" role="group" aria-label="${uiText('管理视图','Manage view')}"><button id="modsListTab" aria-pressed="true">${uiText('模组列表','Mod list')} (<span data-mod-count>—</span>)</button><button id="modsRecovery" aria-pressed="false">${esc(t('mod.recoveryOpen'))} (<span data-recovery-count>—</span>)</button></div><div id="modWorkspace" class="mod-workspace"><div class="drawer-tools"><input type="search" id="modSearch" placeholder="${esc(t('mod.search'))}" aria-label="${esc(t('mod.search'))}"><div class="mod-filters" role="group" aria-label="${esc(t('mod.filter.all'))}">${['all','enabled','disabled'].map(k=>`<button data-mod-filter="${k}" aria-pressed="${k==='all'}">${esc(t('mod.filter.'+k))} <span data-mod-filter-count="${k}">—</span></button>`).join('')}</div>
<div class="mod-updates" role="group" aria-label="${esc(t('mod.checkUpdates'))}"><button class="ui-button" id="modCheckUpdates">${esc(t('mod.checkUpdates'))}</button><button class="ui-button primary" id="modUpdateAll" hidden></button><button class="ui-button" id="modUndoUpdates" hidden>${esc(t('mod.undoUpdates'))}</button><span class="mod-updates-status" data-updates-status></span></div></div><div class="drawer-body mod-manage-list" id="modList"><div class="loading-state">${esc(t('common.loading'))}</div></div></div><div class="drawer-body" id="modRecoveryBody" hidden></div>`);
 if (!host) return;
 host.dataset.drawer = 'true';
 const generation = host.dataset.modalGeneration;
 const alive = () => !host.hidden && host.dataset.modalGeneration === generation && host.classList.contains('show');
 document.getElementById('mCancel').onclick = closeModal;
 const workspace=document.getElementById('modWorkspace'),recoveryBody=document.getElementById('modRecoveryBody');
 const setCount=(selector,count)=>host.querySelectorAll(selector).forEach(e=>e.textContent=String(count));
 const context={alive,onCount:n=>setCount('[data-recovery-count]',n),reloadMods:()=>load()};
 const selectTab=async recovery=>{workspace.hidden=recovery;recoveryBody.hidden=!recovery;host.querySelector('#modsListTab').setAttribute('aria-pressed',String(!recovery));host.querySelector('#modsRecovery').setAttribute('aria-pressed',String(recovery));if(recovery)await openRecoveryPanel(inst,true,context);};
 document.getElementById('modsRecovery').onclick=()=>selectTab(true);
 document.getElementById('modsListTab').onclick=()=>selectTab(false);
 const list = document.getElementById('modList');
 let mods=[], filter='all', query='';
 const draw = () => {
   setCount('[data-mod-count]',mods.length);
   const instance=((STATE&&STATE.instances)||[]).find(i=>i.id===inst.id);if(instance)instance.modsCount=mods.length;
   document.querySelectorAll('[data-instance-mod-count]').forEach(e=>{if(e.dataset.instanceModCount===inst.id)e.textContent=mods.length+' Mod';});
   invalidate('home');
   for(const key of ['all','enabled','disabled'])setCount(`[data-mod-filter-count="${key}"]`,mods.filter(m=>key==='all'||!!m.enabled===(key==='enabled')).length);
   const visible=mods.filter(m=>(filter==='all'||!!m.enabled===(filter==='enabled'))&&String(m.displayName||m.fileName).toLowerCase().includes(query.toLowerCase()));
   list.innerHTML=visible.length?visible.map(m=>`<div class="mod-row ${m.enabled?'':'disabled'}"><div class="mod-row-left"><span class="mod-row-name">${esc(m.fileName)}</span><span class="mod-row-sub">${esc(m.displayName||'')} ${esc(m.version||'')} · ${esc(m.sizeText||'')}</span><span class="mod-disabled-label" ${m.enabled?'hidden':''}>${esc(t('mod.filter.disabled'))}</span></div><div class="mod-row-actions"><button class="mod-del ui-button" aria-label="${esc(t('mod.delete'))}" data-del="${esc(m.fileName)}">×</button><button class="toggle ${m.enabled?'on':''}" role="switch" aria-label="${esc(m.displayName||m.fileName)}" aria-checked="${!!m.enabled}" data-mod="${esc(m.fileName)}"></button></div></div>`).join(''):`<div class="empty-state">${esc(t(mods.length?'mod.noMatch':'mod.empty'))}</div>`;
 };
 const load = async () => {
   try { const result=await SG.api.getMods(inst.id); if(!alive())return; mods=result;draw(); }
   catch(err){if(!alive())return;list.innerHTML=`<div class="content-error" role="alert"><h2>${esc(t('mod.queryFailed'))}</h2><p>${esc(err.message)}</p><button class="ui-button" data-mod-retry>${esc(t('content.retry'))}</button></div>`;}
 };
 document.getElementById('modSearch').oninput=e=>{query=e.target.value;draw();};
 host.querySelectorAll('[data-mod-filter]').forEach(b=>b.onclick=()=>{filter=b.dataset.modFilter;host.querySelectorAll('[data-mod-filter]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));draw();});
 list.onclick=async e=>{
   const b=e.target.closest('button');if(!b||b.disabled)return;
   if(b.hasAttribute('data-mod-retry')){await load();return;}
   if(b.hasAttribute('data-cancel-remove')){b.closest('.mod-inline-confirm').remove();return;}
   const row=b.closest('.mod-row');
   if(b.dataset.del){
     list.querySelectorAll('.mod-inline-confirm').forEach(x=>x.remove());
     const strip=document.createElement('div');strip.className='mod-inline-confirm';
     strip.innerHTML=`<p>${esc(t('mod.inlineRemove'))} ${esc(b.dataset.del)}</p><button class="ui-button" data-cancel-remove>${esc(t('common.cancel'))}</button><button class="ui-button confirm-remove" data-remove-file="${esc(b.dataset.del)}">${esc(t('mod.confirmRemove'))}</button>`;
     row.appendChild(strip);strip.querySelector('[data-cancel-remove]').focus();return;
   }
   if(b.dataset.mod){
     b.disabled=true;
     try{const res=await SG.api.toggleMod(inst.id,b.dataset.mod);if(!alive())return;const m=mods.find(x=>x.fileName===b.dataset.mod);
       // Existing API returns the source filename. ModService renames .jar <-> .jar.disabled.
       const originalFile=b.dataset.mod;
       const nextFile=res.fileName&&res.fileName!==originalFile?res.fileName:(res.enabled?originalFile.replace(/\.disabled$/i,''):(/\.disabled$/i.test(originalFile)?originalFile:originalFile+'.disabled'));
       if(m){m.enabled=!!res.enabled;m.fileName=nextFile;}
       b.dataset.mod=nextFile;row.querySelector('.mod-row-name').textContent=nextFile;const remove=row.querySelector('[data-del]');if(remove)remove.dataset.del=nextFile;
       b.classList.toggle('on',!!res.enabled);b.setAttribute('aria-checked',String(!!res.enabled));row.classList.toggle('disabled',!res.enabled);row.querySelector('.mod-disabled-label').hidden=!!res.enabled;
       toast(t(res.enabled?'mod.enabled':'mod.disabled',{name:''}));
       for(const key of ['all','enabled','disabled'])setCount(`[data-mod-filter-count="${key}"]`,mods.filter(m=>key==='all'||!!m.enabled===(key==='enabled')).length);
       if(filter!=='all'&&!!res.enabled!==(filter==='enabled')){row.remove();document.getElementById('modSearch')?.focus();}
     }catch(err){toast(t('mod.toggleFailed')+(err.message||''));}finally{b.disabled=false;}
   }
   if(b.dataset.removeFile){
     b.disabled=true;
     try{await SG.api.deleteMod(inst.id,b.dataset.removeFile);if(!alive())return;mods=mods.filter(x=>x.fileName!==b.dataset.removeFile);draw();SG.api.listRecoveries(inst.id).then(data=>{if(alive())context.onCount((data.recoveries||[]).length);}).catch(()=>{});toast(t('mod.deleted'));document.getElementById('modSearch')?.focus();}
     catch(err){toast(t('mod.deleteFailed')+(err.message||''));b.disabled=false;}
   }
 };
 /* D3③：更新检查 + 批量更新（任务式：检查 → 建任务 → 轮询 → 撤销） */
 const upd = {
   check: document.getElementById('modCheckUpdates'),
   all: document.getElementById('modUpdateAll'),
   undo: document.getElementById('modUndoUpdates'),
   status: host.querySelector('[data-updates-status]'),
   jobId: null,
   candidates: [],
 };
 const setStatus = (text) => { if (alive()) upd.status.textContent = text; };
 upd.check.onclick = async () => {
   upd.check.disabled = true; upd.all.hidden = true; upd.undo.hidden = true;
   setStatus(t('mod.checking'));
   try {
     const data = await SG.api.getModUpdates(inst.id);
     if (!alive()) return;
     upd.candidates = data.candidates || [];
     if (!upd.candidates.length) { setStatus(t('mod.upToDate')); return; }
     setStatus(t('mod.updatesFound', { n: upd.candidates.length }));
     upd.all.textContent = t('mod.updateAll', { n: upd.candidates.length });
     upd.all.hidden = false;
   } catch (err) { setStatus(t('mod.updateCheckFailed') + (err.message || '')); }
   finally { upd.check.disabled = false; }
 };
 upd.all.onclick = async () => {
   upd.all.disabled = true;
   try {
     const items = upd.candidates.map(c => ({ fileName: c.fileName, downloadUrl: c.downloadUrl, expectedSha1: c.sha1, version: c.version }));
     const job = await SG.api.startBatchUpdate(inst.id, items);
     if (!alive()) return;
     upd.jobId = job.jobId;
     const total = (job.items || []).length;
     const poll = async () => {
       if (!alive()) return;
       try {
         const snap = await SG.api.getBatchUpdate(inst.id, upd.jobId);
         const done = (snap.items || []).filter(x => x.status !== 'pending').length;
         if (snap.state === 'running' || snap.state === 'cancelling') {
           setStatus(t('mod.updateRunning', { done, total }));
           setTimeout(poll, 1200);
           return;
         }
         if (snap.state === 'ok') setStatus(t('mod.updateDone', { n: done }));
         else setStatus(t('mod.updatePartial', { file: snap.failedFile || '—' }));
         upd.all.hidden = true;
         if (done > 0) upd.undo.hidden = false;
         await load();
       } catch (err) { setStatus(t('mod.updateCheckFailed') + (err.message || '')); }
     };
     await poll();
   } catch (err) { setStatus(t('mod.updateStartFailed') + (err.message || '')); upd.all.disabled = false; }
 };
 upd.undo.onclick = async () => {
   if (!upd.jobId) return;
   upd.undo.disabled = true;
   try {
     const res = await SG.api.undoBatchUpdate(inst.id, upd.jobId);
     if (!alive()) return;
     const n = (res.undoed || []).length;
     const conflicts = (res.conflicts || []).length;
     setStatus(t('mod.undoDone', { n }) + (conflicts ? ' · ' + t('mod.undoConflict', { n: conflicts }) : ''));
     upd.undo.hidden = true;
     await load();
   } catch (err) { setStatus(t('mod.updateStartFailed') + (err.message || '')); }
   finally { upd.undo.disabled = false; }
 };

 await new Promise(resolve => requestAnimationFrame(resolve));
 await load();
 SG.api.listRecoveries(inst.id).then(data=>{if(alive())context.onCount((data.recoveries||[]).length);}).catch(()=>{});
}

/* MR04：恢复区面板 —— 列出可还原的移除操作，并逐项恢复 */
async function openRecoveryPanel(inst, drawer, context) {
 const embedded=!!(drawer&&context);
 const requestGeneration=embedded?(context.recoverySeq=(context.recoverySeq||0)+1):0;
 const stillHere=()=>!embedded||context.alive()&&context.recoverySeq===requestGeneration&&document.getElementById('modRecoveryBody')&&!document.getElementById('modRecoveryBody').hidden;
 if(!stillHere())return;
	const fmtStamp = (v) => { try { return v ? new Date(v).toLocaleString() : '—'; } catch (e) { return '—'; } };
	const fmtSize = (n) => {
		const b = Number(n) || 0;
		if (b < 1024) return b + ' B';
		if (b < 1048576) return (b / 1024).toFixed(1) + ' KB';
		return (b / 1048576).toFixed(1) + ' MB';
	};
 const panelHtml=`
		<div class="modal-title" ${embedded?'hidden':''}>${esc(t('recovery.title', { name: inst.name }))}</div>
		<div style="font-size:12px;color:var(--muted,#8b91a1);margin-top:6px" id="recoveryIntro"></div>
		<div id="recoveryContent" style="margin-top:10px"><div class="loading-state">${esc(t('common.loading'))}</div></div>
		<div class="modal-actions" ${embedded?'hidden':''}><button class="modal-btn primary" id="rClose">${esc(t('common.done'))}</button></div>`;
 if(embedded)document.getElementById('modRecoveryBody').innerHTML=panelHtml;else openModal(panelHtml);
	if (drawer) document.getElementById('modalHost').dataset.drawer = 'true';
	document.getElementById('rClose').onclick = closeModal;
	const content = document.getElementById('recoveryContent');
	if (!content) return;
	let data;
	try {
		data = await SG.api.listRecoveries(inst.id);
        if(!stillHere())return;
	} catch (err) {
        if(!stillHere())return;
		// G2/RECOVERY-01（有效 RED）：读取失败时原来只丢一句错误就结束——用户既不知道
		// 有哪些文件可恢复，也没有任何重试入口。这里保留真实原因并给出显式重试动作，
		// 重试走同一条生产路径（不新增业务入口）。
		content.innerHTML = `
			<div class="empty-state" style="padding:20px;text-align:left;gap:10px">
				<div style="font-size:13px;line-height:1.7">${esc(t('recovery.queryFailed'))}${esc((err && err.message) || t('common.unknownError'))}</div>
				<button class="modal-btn primary retry-action" id="rRetry">${esc(t('recovery.queryRetry'))}</button>
			</div>`;
		const retry = document.getElementById('rRetry');
		if (retry) { retry.onclick = () => { openRecoveryPanel(inst, drawer, context); }; retry.focus(); }
		return;
	}
	const intro = document.getElementById('recoveryIntro');
	if (intro) intro.textContent = t('recovery.intro', { days: (data && data.retentionDays) || 30 });
	const ops = (data && data.recoveries) || [];
 if(embedded)context.onCount(ops.length);
	if (!ops.length) {
		content.innerHTML = `<div class="empty-state" style="padding:28px">${esc(t('recovery.empty'))}</div>`;
		return;
	}
	const stateText = (s) => t('recovery.status.' + (s || 'quarantined'));
	content.innerHTML = ops.map(op => {
		// MR05：替换/更新产生的记录带"撤销本次更新"语义（显式覆盖回旧版本）。
		const isReplace = op.reason === 'mod-replace';
		const reasonLabel = isReplace ? ' · ' + esc(t('recovery.reasonReplace')) : '';
		// A14：替换类记录展示「旧版本 → 新版本」（服务端从侧车读出；缺失则不显示这一行）。
		const versionLine = (isReplace && (op.oldVersion || op.newVersion))
			? `<div class="rec-versions">${esc(t('recovery.versions', { from: op.oldVersion || t('recovery.unknownVersion'), to: op.newVersion || t('recovery.unknownVersion') }))}</div>`
			: '';
		return `
		<div class="rec-row">
			<div class="rec-row-head">
				<span class="rec-status">${esc(stateText(op.status))}${reasonLabel}</span>
			${versionLine}
				<span class="rec-meta">${esc(t('recovery.at', { time: fmtStamp(op.createdAt) }))} · ${esc(t('recovery.items', { n: op.files.length, size: fmtSize(op.totalBytes) }))}</span>
			</div>
			<div class="rec-files">${op.files.map(f => `<div class="rec-file">${esc(f.relativePath)} <span class="rec-file-state">${esc(stateText(f.state))}</span>${f.error ? ' · ' + esc(f.error) : ''}</div>`).join('')}</div>
			<button class="modal-btn primary rec-restore" data-op="${esc(op.operationId)}" data-overwrite="${isReplace ? '1' : '0'}" ${op.status==='restored'?'disabled':''}>${esc(op.status==='restored'?stateText('restored'):isReplace ? t('recovery.undoReplace') : t('recovery.restore'))}</button>
		</div>`;
	}).join('');
	content.querySelectorAll('.rec-restore').forEach(btn => btn.addEventListener('click', async () => {
		const operationId = btn.dataset.op;
		const overwrite = btn.dataset.overwrite === '1';
		if (!operationId || btn.disabled) return;
		btn.disabled = true;
		try {
			const res = await SG.api.restoreRecovery(inst.id, operationId, overwrite);
			if (res.conflicts > 0) toast(t('recovery.conflictNote'));
			else if (res.ok) toast(overwrite ? t('recovery.undone') : t('recovery.restored'));
			else toast(t('recovery.partial'));
            if(embedded){if(!stillHere())return;await context.reloadMods();await openRecoveryPanel(inst,true,context);}
            else {closeModal();openRecoveryPanel(inst,drawer);}
		} catch (err) {
			toast(t('recovery.failed') + (err.message || t('common.unknownError')));
			btn.disabled = false;
		}
	}));
}

/* MR07：未完成的二分定位 —— 恢复原状态 / 继续上一有效轮 */
function drawBisectPending(content, inst, pending) {
	const conflicts = (pending.conflicts || []).map(c =>
		`<li>${esc(c.fileName)} · ${esc(c.detail || '')}</li>`).join('');
	const rounds = (pending.rounds || []).length;
	content.innerHTML = `
		<div class="diag-block">
			<div class="diag-label" style="color:var(--warn,#fbbf24)">${esc(t('bisec.pendingTitle'))}</div>
			<div style="font-size:13px;margin-top:6px">${esc(t('bisec.pendingHint', { n: rounds, time: pending.createdAt || '—' }))}</div>
			${conflicts ? `<div style="font-size:13px;margin-top:8px">${esc(t('bisec.pendingConflicts'))}</div><ul class="diag-list">${conflicts}</ul>` : ''}
			<div style="display:flex;gap:10px;margin-top:12px;flex-wrap:wrap">
				<button class="modal-btn primary" id="bResumeRestore">${esc(t('bisec.resumeRestore'))}</button>
				<button class="modal-btn" id="bResumeContinue"${pending.canContinue ? '' : ' disabled'}>${esc(t('bisec.resumeContinue'))}</button>
				<button class="modal-btn ghost" id="bPendingClose">${esc(t('common.close'))}</button>
			</div>
		</div>`;
	document.getElementById('bPendingClose').onclick = closeModal;
	const run = async (action) => {
		try {
			const data = await SG.api.bisectResume(inst.id, action);
			drawBisect(content, inst, data);
		} catch (err) {
			if (err && err.payload) { drawBisectPending(content, inst, err.payload); return; }
			toast(t('bisec.failed') + (err.message || t('common.unknownError')));
		}
	};
	document.getElementById('bResumeRestore').onclick = () => run('restore');
	document.getElementById('bResumeContinue').onclick = () => run('continue');
}

async function openModBisect(inst) {
	openModal(`
		<div class="modal-title">${esc(t('bisec.title'))} · ${esc(inst.name)}</div>
		<div class="page-sub">Minecraft ${esc(inst.version || '—')} · ${esc(inst.loader || 'Vanilla')}</div>
		<div class="bisect-safety" id="bisectSafety"></div>
		<div id="bisectContent" style="margin-top:14px"><div class="loading-state">${esc(t('common.loading'))}</div></div>
		<div class="modal-actions"><button class="modal-btn primary" id="bClose">${esc(t('common.done'))}</button></div>`);
	document.getElementById('bClose').onclick = closeModal;

	// A6：安全引导 —— **常驻**在面板顶部（所有状态都可见，包括"未完成会话"与"需要恢复"）。
	// 计划要求「UI 要求在临时测试世界/实例副本运行」并给出存档备份/副本指引；此前完全没有这段文案。
	const safety = document.getElementById('bisectSafety');
	if (safety) {
		safety.innerHTML = `<div class="bisect-safety-title">${esc(t('bisec.safetyTitle'))}</div>` +
			`<div class="bisect-safety-body">${esc(t('bisec.safety'))}</div>`;
	}

	const content = document.getElementById('bisectContent');
	if (!content) return;
	// MR07：上一次没走完的定位会留有落盘记录 —— 先让用户决定"恢复原状态"还是"继续上一有效轮"。
	try {
		const pending = await SG.api.bisectPending(inst.id);
		if (pending && pending.pending) { drawBisectPending(content, inst, pending); return; }
	} catch (err) {
		// G2/BISECT-03（有效 RED）：查询失败时原来直接忽略错误并显示"正常开始"。
		// 那等于在不知道是否存在待恢复会话的情况下让用户开新一轮，可能覆盖掉落盘记录。
		// 现在显式报错，并且只提供重试查询（不新增业务入口、不改服务端恢复安全实现）。
		content.innerHTML = `
			<div class="empty-state" style="padding:24px;text-align:left;gap:10px">
				<div style="font-size:13px;line-height:1.7">${esc(t('bisec.pendingQueryFailed'))}${esc((err && err.message) || t('common.unknownError'))}</div>
				<button class="modal-btn primary retry-action" id="bPendingRetry">${esc(t('bisec.pendingRetry'))}</button>
			</div>`;
		const retry = document.getElementById('bPendingRetry');
		if (retry) { retry.onclick = () => { openModBisect(inst); }; retry.focus(); }
		return;
	}
	content.innerHTML = `<div class="empty-state" style="padding:24px;text-align:left;gap:10px"><div style="font-size:13px;line-height:1.7">${esc(t('bisec.intro'))}</div><button class="modal-btn primary" id="bStart">${esc(t('bisec.startBtn'))}</button></div>`;
	document.getElementById('bStart').onclick = async () => {
		const startBtn = document.getElementById('bStart');
		if (startBtn) startBtn.disabled = true;
		try {
			const data = await SG.api.bisectStart(inst.id);
			drawBisect(content, inst, data);
		} catch (err) {
			content.innerHTML = `<div class="empty-state" style="padding:24px;color:var(--danger,#f87171)">${esc(t('bisec.failed'))} ${esc(err.message || t('common.unknownError'))}</div>`;
		}
	};
}

function drawBisect(content, inst, data) {
	if (!data || !data.enabledAtStart || !data.enabledAtStart.length) {
		content.innerHTML = `<div class="empty-state" style="padding:24px">${esc(t('bisec.empty'))}</div>`;
		return;
	}
	// MR06：文件操作失败 / 恢复未完成 —— 必须与"定位完成且已恢复"明确区分开。
	if (data.needsRecovery) {
		const files = (data.lastResults || []).map(f => `<li>${esc(f.fileName)} · ${esc(f.state)}${f.error ? ' — ' + esc(f.error) : ''}</li>`).join('');
		content.innerHTML = `
			<div class="diag-block">
				<div class="diag-label" style="color:var(--danger,#f87171)">${esc(t('bisec.needsRecoveryTitle'))}</div>
				<div style="font-size:13px;margin-top:6px">${esc(data.recoveryError || t('bisec.needsRecoveryHint'))}</div>
				${files ? `<ul class="diag-list">${files}</ul>` : ''}
				<div style="display:flex;gap:10px;margin-top:12px">
					<button class="modal-btn primary" id="bRetryRecover">${esc(t('bisec.retryRecover'))}</button>
					<button class="modal-btn ghost" id="bCloseRecovery">${esc(t('common.close'))}</button>
				</div>
			</div>`;
		document.getElementById('bCloseRecovery').onclick = closeModal;
		document.getElementById('bRetryRecover').onclick = async () => {
			try {
				const next = await SG.api.bisectGiveUp(inst.id);
				drawBisect(content, inst, next);
			} catch (err) {
				if (err && err.payload) { drawBisect(content, inst, err.payload); return; }
				toast(t('bisec.failed') + (err.message || t('common.unknownError')));
			}
		};
		return;
	}
	if (data.finished) {
		let reason = data.finishReason ? esc(t('bisec.finishedReason')) + ' ' + esc(data.finishReason) : '';
		const culprit = data.culprit;
		content.innerHTML = `
			<div class="diag-block">
				<div class="diag-label">${esc(t('bisec.title'))}</div>
				${culprit ? `<div class="diag-rule-summary">${esc(t('bisec.finishedCulprit'))} <b>${esc(culprit)}</b></div>` : ''}
				${reason ? `<div style="color:var(--muted,#8b91a1);font-size:13px;margin-top:6px">${reason}</div>` : ''}
				<div style="color:var(--muted,#8b91a1);font-size:13px;margin-top:6px">${esc(t('bisec.restored'))}</div>
			</div>`;
		return;
	}
	const list = (data.disabledHalf || []).map(n => `<li>${esc(n)}</li>`).join('');
	content.innerHTML = `
		<div class="diag-block">
			<div class="diag-label">${esc(t('bisec.step', { n: data.step || 1 }))} · ${esc(t('bisec.candidates', { n: (data.candidates || []).length }))}</div>
			${list ? `<div style="font-size:13px;margin:8px 0 6px">${esc(t('bisec.disabledTitle'))}</div><ul class="diag-list">${list}</ul>` : ''}
			<div style="display:flex;gap:10px;margin-top:12px;flex-wrap:wrap">
				<button class="modal-btn" id="bCrashed" style="background:#e0463c;color:#fff">${esc(t('bisec.crashed'))}</button>
				<button class="modal-btn primary" id="bOk">${esc(t('bisec.notCrashed'))}</button>
				<button class="modal-btn ghost" id="bGiveUp">${esc(t('bisec.giveUp'))}</button>
			</div>
		</div>`;
	const act = async (crashed) => {
		try {
			// MR06：把本轮轮次一起提交，服务端据此拒绝过期轮次的重复提交（双击）。
			const next = await SG.api.bisectTest(inst.id, crashed, data.step);
			drawBisect(content, inst, next);
		} catch (err) {
			if (err && err.payload && err.payload.needsRecovery) { drawBisect(content, inst, err.payload); return; }
			content.innerHTML = `<div class="empty-state" style="padding:24px;color:var(--danger,#f87171)">${esc(t('bisec.failed'))} ${esc(err.message || t('common.unknownError'))}</div>`;
		}
	};
	document.getElementById('bCrashed').onclick = () => act(true);
	document.getElementById('bOk').onclick = () => act(false);
	document.getElementById('bGiveUp').onclick = async () => {
		try {
			const next = await SG.api.bisectGiveUp(inst.id);
			drawBisect(content, inst, next);
		} catch (err) {
			if (err && err.payload && err.payload.needsRecovery) { drawBisect(content, inst, err.payload); return; }
			content.innerHTML = `<div class="empty-state" style="padding:24px;color:var(--danger,#f87171)">${esc(t('bisec.failed'))} ${esc(err.message || t('common.unknownError'))}</div>`;
		}
	};
}
/* ================================================================
   AI 崩溃诊断
   ================================================================ */
function renderMarkdown(text) {
	const s = String(text == null ? '' : text);
	const lines = s.split('\n');
	let html = '';
	let inList = false, inOl = false;
	const closeList = () => { if (inList) { html += '</ul>'; inList = false; } if (inOl) { html += '</ol>'; inOl = false; } };
	lines.forEach(line => {
		if (/^#{1,3}\s/.test(line)) {
			closeList();
			const level = line.match(/^#+/)[0].length;
			html += `<h${Math.min(level, 3)}>${esc(line.replace(/^#{1,3}\s*/, ''))}</h${Math.min(level, 3)}>`;
		} else if (/^\s*-\s+/.test(line) || /^\s*\*\s+/.test(line)) {
			if (!inList) { closeList(); html += '<ul>'; inList = true; }
			html += `<li>${esc(line.replace(/^\s*[-*]\s+/, ''))}</li>`;
		} else if (/^\s*\d+\.\s+/.test(line)) {
			if (!inOl) { closeList(); html += '<ol>'; inOl = true; }
			html += `<li>${esc(line.replace(/^\s*\d+\.\s+/, ''))}</li>`;
		} else if (!line.trim()) {
			closeList();
		} else {
			closeList();
			// 行内代码 + 加粗（先转义再回归）
			let t = esc(line);
			t = t.replace(/`([^`]+)`/g, '<code>$1</code>');
			t = t.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
			html += `<p>${t}</p>`;
		}
	});
	closeList();
	return html;
}

/* MR08：诊断历史与导出（本地报告，已脱敏） */
async function openDiagnoseHistory(inst) {
	const fmtStamp = (v) => { try { return v ? new Date(v).toLocaleString() : '—'; } catch (e) { return '—'; } };
	openModal(`
		<div class="modal-title">${esc(t('diag.historyTitle', { name: inst.name }))}</div>
		<div id="diagHistoryContent" style="margin-top:10px"><div class="loading-state">${esc(t('common.loading'))}</div></div>
		<div class="modal-actions"><button class="modal-btn primary" id="dhClose">${esc(t('common.close'))}</button></div>`);
	document.getElementById('dhClose').onclick = closeModal;
	const content = document.getElementById('diagHistoryContent');
	if (!content) return;

	let data;
	try {
		data = await SG.api.diagnoseHistory(inst.id);
	} catch (err) {
		// G2/DIAGNOSIS-03（有效 RED）：读取历史失败原来只有一句错误文本，没有重试入口。
		content.innerHTML = `
			<div class="empty-state" style="padding:20px;text-align:left;gap:10px">
				<div style="font-size:13px;line-height:1.7">${esc(t('diag.historyFailed'))}${esc((err && err.message) || t('common.unknownError'))}</div>
				<button class="modal-btn primary retry-action" id="dhRetry">${esc(t('diag.historyRetry'))}</button>
			</div>`;
		const retry = document.getElementById('dhRetry');
		if (retry) { retry.onclick = () => { openDiagnoseHistory(inst); }; retry.focus(); }
		return;
	}

	const reports = (data && data.reports) || [];
	const notice = (data && data.notice)
		? `<div style="font-size:12px;color:var(--warn,#fbbf24);margin-bottom:8px">${esc(data.notice)}</div>`
		: '';
	if (!reports.length) {
		content.innerHTML = notice + `<div class="empty-state" style="padding:24px">${esc(t('diag.historyEmpty'))}</div>`;
		return;
	}

	const statusText = (s) => t('diag.status.' + (s || 'completed'));
	content.innerHTML = notice + `
		<label style="display:flex;align-items:center;gap:8px;font-size:12px;margin-bottom:10px">
			<input type="checkbox" id="dhIncludeEvidence"> ${esc(t('diag.includeEvidence'))}
		</label>` + reports.map(r => `
		<div class="rec-row">
			<div class="rec-row-head">
				<span class="rec-status">${esc(statusText(r.status))}</span>
				<span class="rec-meta">${esc(fmtStamp(r.createdAt))} · ${esc(t('diag.redactionCount', { n: r.redactionTotal || 0 }))}</span>
			</div>
			<div class="rec-files">${esc(r.summary || '')}</div>
			<div style="display:flex;gap:8px">
				<button class="modal-btn small" data-export="md" data-report="${esc(r.reportId)}">${esc(t('diag.exportMd'))}</button>
				<button class="modal-btn small" data-export="json" data-report="${esc(r.reportId)}">${esc(t('diag.exportJson'))}</button>
			</div>
		</div>`).join('');

	content.querySelectorAll('[data-export]').forEach(btn => btn.addEventListener('click', async () => {
		const cb = document.getElementById('dhIncludeEvidence');
		await downloadDiagnoseReport(inst, btn.dataset.report, btn.dataset.export, !!(cb && cb.checked));
	}));
}

async function downloadDiagnoseReport(inst, reportId, format, includeEvidence) {
	try {
		const res = await SG.api.diagnoseExport(inst.id, reportId, format, includeEvidence);
		if (!res.ok) { toast(t('diag.exportFailed') + 'HTTP ' + res.status); return; }
		const blob = new Blob([res.text], { type: format === 'json' ? 'application/json' : 'text/markdown' });
		const url = URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.href = url;
		a.download = 'pineglow-diagnose-' + reportId + (format === 'json' ? '.json' : '.md');
		document.body.appendChild(a);
		a.click();
		document.body.removeChild(a);
		setTimeout(() => URL.revokeObjectURL(url), 4000);
		toast(t('diag.exported'));
	} catch (err) {
		toast(t('diag.exportFailed') + (err.message || t('common.unknownError')));
	}
}

async function openAiDiagnose(inst) {
	// Phase 3B A-03/A-05 + Gate R2 修复 P1-1：先展示确认面板，让用户选择「发送给 AI」「仅本地分析」「取消」；
	// 仅在明确 confirmAiSend=true 时才真正调用 AI handler。Provider/Model 必须从 STATE.config 读取。
	const provider = getActiveAiProviderInfo();
	const providerName = provider.name;
	const modelName = provider.model;

	openModal(`
		<div class="modal-title">${esc(t('diag.title'))}</div>
		<div class="page-sub">Minecraft ${esc(inst.version || '—')} · ${esc(inst.loader || 'Vanilla')}</div>
		<div id="diagContent" style="margin-top:14px">${renderConfirmPanel(providerName, modelName, provider.allowed)}</div>
		<div class="modal-actions">
			<button class="modal-btn primary" id="dConfirmSend"${provider.allowed ? '' : ' disabled title="' + esc(t('diag.safetyModeBlocked')) + '"'}>${esc(t('diag.confirmSendAi'))}</button>
			<button class="modal-btn ghost" id="dConfirmLocal">${esc(t('diag.confirmLocalOnly'))}</button>
			<button class="modal-btn ghost" id="dHistory">${esc(t('diag.history'))}</button>
			<button class="modal-btn" id="dConfirmCancel">${esc(t('diag.confirmCancel'))}</button>
		</div>`);

	const content = document.getElementById('diagContent');
	if (!content) return;

	const sendBtn = document.getElementById('dConfirmSend');
	const localBtn = document.getElementById('dConfirmLocal');
	const cancelBtn = document.getElementById('dConfirmCancel');
	// MR08：随时查看本地诊断历史（只读，不触发任何外部请求）。
	const historyBtn = document.getElementById('dHistory');
	if (historyBtn) historyBtn.onclick = () => { closeModal(); openDiagnoseHistory(inst); };
	let mode = null; // 'ai' | 'local' | null

	// Phase 3B A-07：AbortController + token 校验，关闭弹窗/切换 mode 时中止未完成请求。
	const controller = new AbortController();
	let token = 0;
	let closed = false; // v2 幂等 teardown 标志

	// Phase 3B v4 步骤 3 P0 修复：当前 operation 状态。cancelOnce 必须看到 started=true 且
	// settled=false 且 cancelSent=false 才发 cancel；cancelSent 必须原子式置 true 再异步发，
	// 避免遮罩 / 按钮 / beforeunload 并发造成重复 cancel 请求。
	let currentOp = null;

	// 安全 operationId 生成：crypto.randomUUID 优先；fallback 必须用 crypto.getRandomValues；
	// 严禁 Math.random（v4 prompt §3 第 2 条）。
	let _newOperationIdCounter = 0;
	function newOperationId() {
		const g = (typeof globalThis !== 'undefined' ? globalThis : (typeof window !== 'undefined' ? window : null));
		const c = g && g.crypto;
		try {
			if (c && typeof c.randomUUID === 'function') return c.randomUUID();
		} catch (_) { /* fallthrough */ }
		if (c && typeof c.getRandomValues === 'function') {
			const buf = new Uint8Array(16);
			c.getRandomValues(buf);
			// RFC4122 v4 标记位
			buf[6] = (buf[6] & 0x0f) | 0x40;
			buf[8] = (buf[8] & 0x3f) | 0x80;
			const hex = [];
			for (let i = 0; i < 16; i++) hex.push(buf[i].toString(16).padStart(2, '0'));
			return (hex.slice(0, 4).join('') + '-' + hex.slice(4, 6).join('') + '-' + hex.slice(6, 8).join('') + '-' + hex.slice(8, 10).join('') + '-' + hex.slice(10, 16).join(''));
		}
		// 末条兜底：vm / 隔离沙箱等无 crypto 的环境走到这里。Date.now + 单调计数器，
		// 不使用 Math.random（v4 prompt §3 第 2 条），session 内唯一即可（不是密码学强度）；
		// 任何 release 浏览器 / WebView2 / 桌面 app 宿主都提供 crypto，不会走到这条路径。
		_newOperationIdCounter = (typeof _newOperationIdCounter === 'number' ? _newOperationIdCounter : 0) + 1;
		const ts = (typeof Date !== 'undefined' && typeof Date.now === 'function') ? Date.now().toString(36) : '0';
		return 'op-ts-' + ts + '-' + _newOperationIdCounter.toString(36);
	}

	// 统一 cancelOnce：仅当 started && !settled && !cancelSent 时执行；
	// 先原子式置 cancelSent=true 再异步发 cancel，防止并发路径造成重复请求。
	function cancelOnce() {
		const op = currentOp;
		if (!op) return;
		if (!op.started || op.settled || op.cancelSent) return;
		op.cancelSent = true;
		const id = op.id;
		const targetInstanceId = inst.id;
		// 异步 fire-and-forget；api 层会自己 throw，不会影响 UI 主流程
		try {
			Promise.resolve()
				.then(() => SG.api.cancelDiagnoseInstance(targetInstanceId, id))
				.catch(() => { /* logged in api layer; UI 不需要显式 toast */ });
		} catch (_) {
			// 同步路径异常也不影响 teardown 后续步骤
		}
	}

	// Phase 3B v4 步骤 3 P1-2：cancel 请求必须带 token。本地 best-effort：
	// 在 beforeunload/pagehide 期间浏览器可能丢弃 fetch，所以退化为同步 XHR
	// 并把 X-Pineglow-Token 作为 header 显式带上（不得用无认证方案绕过）。
	function bestEffortCancelOnUnload(id) {
		try {
			const g = (typeof globalThis !== 'undefined' ? globalThis : (typeof window !== 'undefined' ? window : null));
			const XHR = g && g.XMLHttpRequest;
			const tokenVal = (typeof window !== 'undefined' && window.SG && window.SG.apiToken) ? String(window.SG.apiToken) : '';
			if (typeof XHR !== 'function') return;
			const xhr = new XHR();
			// 同步：unload 期间只有同步请求最稳
			xhr.open('POST', '/api/instances/' + encodeURIComponent(inst.id) + '/diagnose/cancel', false);
			try { xhr.setRequestHeader('Content-Type', 'application/json'); } catch (_) {}
			try { xhr.setRequestHeader('X-Pineglow-Token', tokenVal); } catch (_) {}
			xhr.send(JSON.stringify({ operationId: id || '' }));
		} catch (_) { /* unload 期间任何异常都吞掉 */ }
	}

	cancelBtn.onclick = () => {
		try { controller.abort(); } catch (_) {}
		try { teardown(); } catch (_) {}
		try { closeModal(); } catch (_) {}
	};

	// v2 修复 P0-1：注册 aborter 到当前 modal 代次；closeModal 触发时先 abort 再隐藏 DOM。
	// 单次 modal 最多 1 个 aborter（openAiDiagnose 不嵌套）。
	const myGen = parseInt(document.getElementById('modalHost').dataset.modalGeneration || '0', 10);
	registerModalAborter(myGen, () => {
		try { controller.abort(); } catch (_) { }
		try { teardown(); } catch (_) { }
	});

	// v2 修复 P0-1 + Phase 3B v4 步骤 3 P1-2：window beforeunload 关闭页面/刷新时 abort pending
	// diagnose，并通过带 token 的同步 best-effort 请求通知服务端取消；unload 期间浏览器会丢弃
	// 异步 fetch，不得用无认证方案绕过。
	const onBeforeUnload = () => {
		try { controller.abort(); } catch (_) { }
		const op = currentOp;
		if (op && op.started && !op.settled && !op.cancelSent) {
			op.cancelSent = true;
			bestEffortCancelOnUnload(op.id);
		}
	};
	window.addEventListener('beforeunload', onBeforeUnload);
	window.addEventListener('pagehide', onBeforeUnload);

	// 幂等 teardown：注册一次，所有关闭路径都安全可重复调用。
	// Phase 3B v4 步骤 3 P1-2 规定的顺序：UI token 失效 → abort diagnose fetch → cancelOnce →
	// 解绑 beforeunload / pagehide / modal aborter。
	function teardown() {
		if (closed) return;
		closed = true;
		// 1) UI token 失效——阻止已发出的诊断结果继续覆盖 UI。
		try { token += 1; } catch (_) {}
		// 2) abort 当前 diagnose fetch（若是 pending 状态）。
		try { controller.abort(); } catch (_) { }
		// 3) 统一 cancelOnce：若 operation 已 started 且未 settled 且未发过 cancel，则发 cancel。
		try { cancelOnce(); } catch (_) { }
		// 4) 解绑 beforeunload / pagehide / modal aborter。
		try { window.removeEventListener('beforeunload', onBeforeUnload); } catch (_) { }
		try { window.removeEventListener('pagehide', onBeforeUnload); } catch (_) { }
		try { unregisterModalAborter(myGen); } catch (_) { }
	}

	sendBtn.onclick = () => { mode = 'ai'; runDiag('ai'); };
	localBtn.onclick = () => { mode = 'local'; runDiag('local'); };

	async function runDiag(selectedMode) {
		// Phase 3B v4 步骤 3 P0：每次 runDiag 开始前生成新 operationId，并写入 currentOp 状态。
		const operationId = newOperationId();
		token += 1;
		const myToken = token;
		currentOp = { id: operationId, started: true, settled: false, cancelSent: false };
		sendBtn.disabled = true;
		localBtn.disabled = true;
		cancelBtn.disabled = true;
		content.innerHTML = `<div class="loading-state">${esc(t('diag.analyzing'))}</div><div style="margin-top:10px"><button class="modal-btn ghost" id="dCancelRequest">${esc(t('diag.cancelButton'))}</button></div>`;
		const cancelReqBtn = document.getElementById('dCancelRequest');
		if (cancelReqBtn) cancelReqBtn.onclick = () => {
			// Phase 3B v4 步骤 3 P1-2：运行中取消按钮必须走统一 teardown，不能只 controller.abort()。
			try { teardown(); } catch (_) { }
			try { closeModal(); } catch (_) { }
		};

		let data;
		try {
			data = await SG.api.diagnoseInstance(inst.id, {
				operationId: operationId,
				confirmAiSend: selectedMode === 'ai',
				localOnly: selectedMode === 'local',
				signal: controller.signal,
				// Phase 3B LANG 透传：把当前 UI locale 一起交给 api 层，由其统一做 zh/en 白名单校验。
				locale: SG.locale,
			});
		} catch (e) {
			if (myToken !== token) return;
			const msg = e && e.name === 'AbortError' ? t('diag.err.aiCancelled') : (e && e.message) || t('diag.failed');
			content.innerHTML = `<div class="empty-state" style="padding:24px;color:var(--danger,#f87171)">${esc(msg)}</div><div class="modal-actions"><button class="modal-btn primary" id="dClose">${esc(t('common.done'))}</button></div>`;
			document.getElementById('dClose').onclick = closeModal;
			return;
		}

		if (myToken !== token) return; // 已过期 / 弹窗已关闭
		// Phase 3B v4 步骤 3 P1-2：自然成功 → 先 settled=true 再解绑，不发多余 cancel。
		if (currentOp && currentOp.id === operationId) {
			currentOp.settled = true;
		}
		if (!data || !data.ok) {
			content.innerHTML = `<div class="empty-state" style="padding:24px">${esc(t('diag.failed'))}</div><div class="modal-actions"><button class="modal-btn primary" id="dClose">${esc(t('common.done'))}</button></div>`;
			document.getElementById('dClose').onclick = closeModal;
			return;
		}
		// Phase 3C-1：把用户在前端选择的 mode 传给 renderResult，确保"仅本地分析"路径不会被当作 AI 结果展示
		renderResult(data, myToken, controller, selectedMode);
	}

	function renderResult(data, myToken, controller, selectedMode) {
		const rule = data.rule || {};
		const structured = data.aiResult || {};
		const causes = structured.causes || [];
		const actions = structured.actions || [];
		const mods = data.mods || { issues: [] };
		const issues = (mods.issues || []).map(i => `<li>${esc(i.title)}：${esc(i.description)}</li>`).join('');
		const errorKey = structured.errorCode ? mapAiErrorCodeToI18nKey(structured.errorCode) : null;
		const errorMsg = errorKey ? t(errorKey) : (structured.summary || structured.errorCode || '');
		// Phase 3C-1：可解释性 + 诚实状态
		// - cause.kind 本地化：fact/inference 用本地化标签；任何未识别枚举一律降级为 "未确定"，绝不原样输出未受控文本
		// - 置信度仅在有效（有限数 ∈ [0,1]）且来自真实 AI 响应时显示；低置信度（< 0.4）突出"信息不足"而非给出确定结论
		// - 证据列表与"信息不足"列表分别承载视觉样式；missingInformation 必须独立可识别
		// - actionType 必须在 allowlist 内（disable_mod 是当前唯一会被执行的动作）；未知 actionType 不再渲染可点击按钮
		const KIND_KEYS = { fact: 'diag.kind.fact', inference: 'diag.kind.inference' };
		function normalizeKind(rawKind) {
			const k = typeof rawKind === 'string' ? rawKind : '';
			if (Object.prototype.hasOwnProperty.call(KIND_KEYS, k)) return k;
			return 'unknown';
		}
		function kindBadgeHtml(rawKind) {
			const safe = normalizeKind(rawKind);
			const labelKey = KIND_KEYS[safe] || 'diag.kind.unknown';
			return `<span class="diag-kind-badge diag-kind-${safe}" data-diag-kind="${esc(safe)}" title="${esc(t('diag.kind.source.' + safe))}">${esc(t(labelKey))}</span>`;
		}
		function isValidConfidence(v) {
			return typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= 1;
		}
		// Phase 3C-1 (second pass)：后端 insufficient_information 即使 confidence=1.0 也必须显式标记；
		// 高置信度不能掩盖"信息不足"。本地规则块无 confidence 概念，绝不出 AI 置信度。
		function confidenceBlockHtml(conf, status) {
			if (!isValidConfidence(conf)) return '';
			const pct = Math.round(conf * 100);
			const insufficient = status === 'insufficient_information';
			// 信息不足一律按低置信度样式处理，避免"100% 确定"误导
			const lowClass = (conf < 0.4 || insufficient) ? ' diag-confidence-low' : '';
			const lowBlock = (conf < 0.4 || insufficient) ? `<div class="diag-low-confidence">${esc(insufficient ? t('diag.insufficientInfoNote') : t('diag.lowConfidence'))}</div>` : '';
			return `<div class="diag-confidence${lowClass}"><span class="diag-confidence-value">${esc(t('diag.confidence'))}: ${pct}%</span><span class="diag-confidence-note">${esc(t('diag.confidenceNote'))}</span></div>${lowBlock}`;
		}
		// 已知可点击执行的动作白名单；未知 actionType 即使带 risk/title 也不出按钮
		const KNOWN_ACTION_TYPES = { disable_mod: true };
		function isKnownAction(action) {
			return action && typeof action.actionType === 'string' && Object.prototype.hasOwnProperty.call(KNOWN_ACTION_TYPES, action.actionType);
		}
		// Phase 3C-1 (second pass)：
		// - configured=false（用户未配置 AI）：整段 AI 块不渲染。
		// - 用户明确选 "仅本地分析"（localOnly=true）：即使后端返回 status=insufficient_information / confidence=1.0 / causes[].kind='fact'，
		//   也不能展示"AI 分析"标题、AI 置信度、AI 操作按钮；这条路径整段 AI 块不渲染，并显示"未发送至 AI"提示。
		const aiConfigured = data.configured !== false;
		const isLocalOnly = selectedMode === 'local' || data.localOnly === true;
		const showAiBlock = aiConfigured && !isLocalOnly;

		// Phase 3C-2：真实发送元数据 badge。
		// - data.aiDispatch 来自服务端 /api/instances/{id}/diagnose。
		// - 仅当 sentAtUtc 是合法 ISO-8601 UTC/offset 时间（Date 能解析）时才显示 badge；
		//   缺时间 / 无效时间 / 本地模式 / 旧服务器（无 aiDispatch）一律不出 badge，绝不降级到 "sentNoTime"。
		// - providerName/model 可为空字符串时显示安全占位 "?"。
		// - 时间显示必须带明确时区（GMT±hh:mm 或 IANA 名）；只用 toLocaleString() 不够。
		function dispatchBlockHtml() {
			// 本地分析路径不出 "sent" badge
			if (isLocalOnly) return '';
			const dispatch = data.aiDispatch;
			// 没有 aiDispatch 字段或为空：服务端未真正发起发送（或旧服务器）→ 不出 badge
			if (!dispatch || typeof dispatch !== 'object') return '';
			const provider = typeof dispatch.providerName === 'string' ? dispatch.providerName : '';
			const model = typeof dispatch.model === 'string' ? dispatch.model : '';
			const sentAtUtc = typeof dispatch.sentAtUtc === 'string' ? dispatch.sentAtUtc : '';
			// 关键：sentAtUtc 必须存在且是合法 UTC/offset ISO 时间
			if (!sentAtUtc) return '';
			const parsed = new Date(sentAtUtc);
			if (isNaN(parsed.getTime())) return '';
			// 必须明确是 UTC 或带 offset（不允许裸本地时间格式）
			const looksIsoUtc = /(Z|[+-]\d{2}:?\d{2})$/i.test(sentAtUtc);
			if (!looksIsoUtc) return '';
			// 构造带时区的时间字符串；优先使用本机时区（Intl 仓库兼容）。
			let timeZone = '';
			try {
				timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || '';
			} catch (e) {
				timeZone = '';
			}
			// 用 UTC 字符串 + GMT offset 拼出明确时区表示，不依赖 toLocaleString 隐式行为。
			// 公式：offset = (Date 返回的 minutes - 实际 UTC minutes)
			const tzOffsetMinutes = parsed.getTimezoneOffset(); // 本地相对 UTC 的分钟数（西正东负）
			const sign = tzOffsetMinutes > 0 ? '-' : '+';
			const absMin = Math.abs(tzOffsetMinutes);
			const offsetStr = `GMT${sign}${String(Math.floor(absMin / 60)).padStart(2, '0')}:${String(absMin % 60).padStart(2, '0')}`;
			const dateStr = `${parsed.getFullYear()}-${String(parsed.getMonth() + 1).padStart(2, '0')}-${String(parsed.getDate()).padStart(2, '0')} ${String(parsed.getHours()).padStart(2, '0')}:${String(parsed.getMinutes()).padStart(2, '0')}:${String(parsed.getSeconds()).padStart(2, '0')}`;
			const timeStr = `${dateStr} ${timeZone ? timeZone + ' ' : ''}${offsetStr}`;
			// provider / model 可以为空 → 占位 "?"（不在 badge 内泄露 "?" 之外的占位）
			const failedOrCancelled = !!structured.errorCode && structured.status !== 'confirmation_required'
				|| data.aiError;
			const i18nKey = failedOrCancelled ? 'diag.dispatch.failed' : 'diag.dispatch.sent';
			const cls = failedOrCancelled ? 'diag-dispatch diag-dispatch-failed' : 'diag-dispatch';
			return `<div class="${cls}"><span class="diag-dispatch-dot" aria-hidden="true"></span><span class="diag-dispatch-text">${esc(t(i18nKey, { provider: provider || '?', model: model || '?', time: timeStr }))}</span></div>`;
		}
		let html = '';
		html += `<div class="diag-block"><div class="diag-label">${esc(t('diag.ruleTitle'))}</div>`;
		html += `<div class="diag-rule-summary">${esc(rule.summary || t('diag.noTail'))}</div>`;
		if ((rule.suggestions || []).length) {
			html += `<ul class="diag-list">${(rule.suggestions || []).map(s => `<li>${esc(s)}</li>`).join('')}</ul>`;
		}
		if (issues) html += `<div class="diag-mods-title">${esc(t('diag.modsTitle'))}</div><ul class="diag-list">${issues}</ul>`;
		html += `</div>`;
		// 仅本地分析：显式标注模式，避免用户把本地结果误认为 AI 结论
		if (isLocalOnly) {
			html += `<div class="diag-block diag-local-only-hint"><div class="diag-local-only-banner">${esc(t('diag.localOnlyHint'))}</div></div>`;
		}
		// Phase 3C-2：AI 块存在但 aiDispatch 可能缺失（例如 structured.summary 为空但服务端 error 标记），
		// 仍然要把 dispatch badge 渲染出来（只要服务端确认发起过发送）。这里 showAiBlock 必须为 true 且 isLocalOnly=false。
		// dispatchBlockHtml 内部已做 isLocalOnly 短路。
		if (showAiBlock) {
			const preBadge = dispatchBlockHtml();
			if (preBadge) html += `<div class="diag-block diag-dispatch-block">${preBadge}</div>`;
		}
		if (structured.summary && showAiBlock && (structured.ok !== false && !structured.errorCode || structured.status === 'confirmation_required')) {
			html += `<div class="diag-block"><div class="diag-label">${esc(t('diag.aiTitle'))}</div>`;
			html += `<div class="diag-rule-summary">${esc(structured.summary)}</div>`;
			// 仅当存在有效 AI 响应（status 不是 confirmation_required）时才显示置信度；本地规则本身不带置信度概念
			if (structured.status !== 'confirmation_required') {
				html += confidenceBlockHtml(structured.confidence, structured.status);
			}
			if (structured.status === 'confirmation_required') {
				html += `<div style="color:var(--warn,#fbbf24);font-size:13px;margin-top:6px">${esc(errorMsg || t('diag.err.confirmationRequired'))}</div>`;
			}
			// Phase 3C-1 (second pass)：status=insufficient_information 即使 confidence=1.0 也必须显式标记"信息不足"
			if (structured.status === 'insufficient_information') {
				html += `<div class="diag-insufficient-info"><div class="diag-insufficient-info-title">${esc(t('diag.insufficientInfoTitle'))}</div><div class="diag-insufficient-info-note">${esc(t('diag.insufficientInfoNote'))}</div></div>`;
			}
			if (causes.length && structured.status !== 'confirmation_required') {
				html += '<div class="diag-mods-title">' + esc(t('diag.causes')) + '</div>';
				causes.forEach(cause => {
					const evidence = cause.evidence || [];
					html += '<details class="diag-cause" data-diag-cause-kind="' + esc(normalizeKind(cause.kind)) + '"><summary>' + esc(cause.title || t('diag.unknownCause')) + ' ' + kindBadgeHtml(cause.kind) + '</summary>';
					html += '<p>' + esc(cause.description || '') + '</p>';
					// 证据与正文分别呈现；空证据列表不画空 <ul>
					if (evidence.length) html += '<div class="diag-evidence-title">' + esc(t('diag.evidenceTitle')) + '</div><ul class="diag-evidence">' + evidence.map(item => '<li>' + esc(item) + '</li>').join('') + '</ul>';
					html += '</details>';
				});
			}
			if (structured.missingInformation?.length) html += '<div class="diag-mods-title diag-missing-info-title">' + esc(t('diag.missingInfoTitle')) + '</div><ul class="diag-list diag-missing-info">' + structured.missingInformation.map(item => '<li>' + esc(item) + '</li>').join('') + '</ul>';
			if (structured.warnings?.length) html += '<div class="diag-mods-title">' + esc(t('diag.warnings')) + '</div><ul class="diag-list">' + structured.warnings.map(item => '<li>' + esc(item) + '</li>').join('') + '</ul>';
			if (actions.length && structured.status !== 'confirmation_required') {
				html += '<div class="diag-mods-title">' + esc(t('diag.actions')) + '</div>';
				actions.forEach(action => {
					if (!isKnownAction(action) || !action.target) {
						// 未知 actionType 或缺 target → 不渲染可点击按钮；只展示只读文本与"未知/不允许"标签
						html += '<div class="diag-suspect-row diag-action-unknown"><span>' + esc(action.title || '') + '</span><span class="diag-action-note">' + esc(t('diag.actionUnknown')) + '</span></div>';
						return;
					}
					const risky = action.risk === 'high';
					const modifyLabel = esc(t('diag.actionModify'));
					html += '<div class="diag-suspect-row diag-action-modify"><span>' + esc(action.title) + ' <span class="diag-action-modify-label">(' + modifyLabel + ')</span></span><button class="modal-btn small' + (risky ? ' ghost' : '') + '" data-action="' + esc(action.actionType) + '" data-target="' + esc(action.target || '') + '">' + esc(risky ? t('diag.confirmAction') : t('diag.doAction')) + '</button></div>';
				});
			}
			html += '</div>';
		}
		const suspects = data.suspectMods || [];
		if (suspects.length && structured.status !== 'confirmation_required' && showAiBlock) {
			html += '<div class="diag-block"><div class="diag-label">' + esc(t('diag.suspectTitle')) + '</div>';
			suspects.forEach(name => {
				const modifyLabel = esc(t('diag.actionModify'));
				html += '<div class="diag-suspect-row diag-action-modify"><span>' + esc(name) + ' <span class="diag-action-modify-label">(' + modifyLabel + ')</span></span><button class="modal-btn small" data-suspect="' + esc(name) + '">' + esc(t('diag.disableMod')) + '</button></div>';
			});
			html += '</div>';
		}
		if (structured.errorCode && structured.status !== 'confirmation_required' && showAiBlock) {
			html += `<div class="diag-block"><div class="diag-label">${esc(t('diag.aiTitle'))}</div><div style="color:var(--warn,#fbbf24);font-size:13px">${esc(errorMsg || t('diag.aiFailed'))} ${esc(structured.errorCode)}</div></div>`;
		} else if (!aiConfigured) {
			html += `<div class="diag-block"><div class="diag-label">${esc(t('diag.aiTitle'))}</div><div style="color:var(--muted,#8b91a1);font-size:13px">${esc(t('diag.notConfigured'))}</div></div>`;
		}
		html += `<div class="diag-privacy">${esc(t('diag.privacy'))}</div>`;
		content.innerHTML = html;
		bindActionButtons();
	}

	function bindActionButtons() {
		content.querySelectorAll('[data-action]').forEach(btn => btn.addEventListener('click', async () => {
			const actionType = btn.dataset.action;
			const target = btn.dataset.target;
			if (actionType !== 'disable_mod' || !target) return;
			// Phase 3B A-05：structured 路径同样必须 confirm。
			if (!confirm(t('diag.confirmDisable', { name: target }))) return;
			btn.disabled = true;
			try {
				await SG.api.toggleMod(inst.id, target);
				toast(t('diag.modDisabled', { name: target }));
				btn.textContent = t('diag.disabled');
			} catch (err) {
				btn.disabled = false;
				toast(t('diag.modDisableFailed') + (err.message || ''));
			}
		}));
		content.querySelectorAll('[data-suspect]').forEach(btn => btn.addEventListener('click', async () => {
			const name = btn.dataset.suspect;
			if (!name) return;
			// Phase 3B A-05：旧 suspectMods 路径也必须 confirm。
			if (!confirm(t('diag.confirmDisable', { name }))) return;
			btn.disabled = true;
			try {
				await SG.api.toggleMod(inst.id, name);
				toast(t('diag.modDisabled', { name }));
				btn.textContent = t('diag.disabled');
			} catch (err) {
				btn.disabled = false;
				toast(t('diag.modDisableFailed') + (err.message || ''));
			}
		}));
	}
}

function renderConfirmPanel(providerName, modelName, allowed) {
	const safeProvider = esc(providerName || '—');
	const safeModel = esc(modelName || '—');
	const blockedNotice = allowed === false ? `<div class="diag-confirm-blocked">${esc(t('diag.safetyModeBlocked') || t('diag.safetyModeNotice'))}</div>` : '';
	return `
		<div class="diag-confirm-panel">
			<div class="diag-confirm-title">${esc(t('diag.confirmPanelTitle'))}</div>
			<div class="diag-confirm-intro">${esc(t('diag.confirmPanelIntro'))}</div>
			<div class="diag-confirm-row"><span class="diag-confirm-label">${esc(t('diag.confirmPanelProvider'))}</span><span class="diag-confirm-value">${safeProvider}</span></div>
			<div class="diag-confirm-row"><span class="diag-confirm-label">${esc(t('diag.confirmPanelModel'))}</span><span class="diag-confirm-value">${safeModel}</span></div>
			<div class="diag-confirm-row"><span class="diag-confirm-label">${esc(t('diag.confirmPanelData'))}</span><span class="diag-confirm-value">${esc(t('diag.confirmPanelDataSummary'))}</span></div>
			<div class="diag-confirm-notice">${esc(t('diag.safetyModeNotice'))}</div>
			${blockedNotice}
		</div>`;
}

/* ================================================================
   Content 内容中心
   ================================================================ */
/* ================================================================
   Content 资源库
   ================================================================
   M1-3 第三步收尾：本视图已整体拆至 js/content.js（SG.content）。
   与前面几个视图的关键区别：contentPageGen / contentDebTimer 的**唯一写者**是
   本文件的 goTo（导航核心：进入时分配 gen、切走时置 -1 并清定时器）。
   因此这两个状态**留在本文件**，content.js 通过 SG.content.__wire 注入的
   pageGen() 读取、setDebounce() 挂定时器；本文件不反向依赖视图模块的内部变量。
   下列薄委托保留旧名字，供 VIEWS 路由表、goTo 回访路径与 SG.__test__ 继续使用。 */

/* 关键：v4 引入 contentPageGen 区分"页面进入"与"页内请求"。
   每次 goTo 进入 content 分配一个新的 pageGen（=当时的 navSeq）。
   goTo 切走 content 时设为 -1，让任何 pending 的 renderContent / retryLoadStateAndRefresh
   短路返回。navSeq 不能区分"页面已切走"与"页面内还在"。
   M1-3：这个状态**仍然留在这里**，因为 goTo（导航核心）是它唯一的写者；
   content.js 只经 __wire 的 pageGen() 读它。 */
let contentPageGen = 0;
/* 关键：debounce 定时器提到模块级。goTo 切走 content 时清理，避免离页后
   debounce 到期还触发 searchContent。M1-3：同上，goTo 也要清它，所以句柄留在这里。 */
let contentDebTimer = null;

async function renderContent() { return SG.content.render(); }
function bindContent(el) { return SG.content.bind(el); }
function contentRerenderGrid(el) { return SG.content.rerenderGrid(el); }
function retryLoadStateAndRefresh(el) { return SG.content.retryLoadStateAndRefresh(el); }
function bindContentInstalls(el) { return SG.content.bindContentInstalls(el); }
function bindContentIcons(el) { return SG.content.bindContentIcons(el); }
function searchContentOptions() { return SG.content.searchContentOptions(); }

/* ================================================================
   Worlds 世界
   ================================================================ */
function collectionErrorHtml(nav, error) {
	return `<div class="download-source-error" data-list-error="${esc(nav)}" role="alert" style="grid-column:1 / -1">
		<div class="state-main"><div class="state-title">${esc(t(nav + '.loadFailed'))}</div>
		<p class="state-desc">${esc(t('collection.loadFailedDesc'))}</p>
		<details class="tech-details"><summary>${esc(t('content.techDetails'))}</summary><pre class="tech-body">${esc(error && error.message || error || t('common.unknownError'))}</pre></details>
		<div class="state-actions"><button class="download-retry-btn" data-list-retry="${esc(nav)}">${esc(t('dl.retry'))}</button></div></div></div>`;
}

function bindCollectionRetry(el, nav) {
	el.querySelector('[data-list-retry]')?.addEventListener('click', async e => {
		const button = e.currentTarget;
		if (button.disabled || currentView !== nav || !el.isConnected) return;
		button.disabled = true;
		try { invalidate(nav); await goTo(nav); }
		finally { if (button.isConnected) button.disabled = false; }
	});
}

async function renderWorlds() {
	let worlds = [];
	let error = null;
	try { worlds = await SG.api.getWorlds(); navigationCounts.worlds = worlds.length; } catch (e) { error = e; }
	// M2-1①：「已删除的存档」与存档列表**同页**展示。分开一页会让用户以为删掉就是没了，
	// 而这一版的核心承诺恰恰是"删了还能找回"——入口必须就在删除发生的那个页面。
	let recoveries = [];
	let recoveryError = null;
	try {
		recoveries = typeof SG.api.listSaveRecoveries === 'function' ? await SG.api.listSaveRecoveries(instanceIdForWorlds(worlds)) : [];
	} catch (e) { recoveryError = e; }
	const cards = error ? collectionErrorHtml('worlds', error) : worlds.length ? worlds.map(w => `
		<div class="world-card" data-inst="${esc(w.instanceId)}" data-folder="${esc(w.folderName)}">
			<div class="world-icon">${uiIcon('world')}</div>
			<div class="world-meta">
				<span class="world-name">${esc(w.name)}</span>
				<span class="world-sub">${esc(w.instance || t('worlds.unknownInstance'))} · ${esc(w.lastPlayed || '')}</span>
			</div>
			<span class="world-size">${esc(w.size || '—')}</span><button class="world-btn" data-world-manage>${uiText('管理存档','Manage world')}</button>
		</div>`).join('')
		: `<div class="empty-state"><div class="es-icon">🗺️</div>${esc(t('worlds.empty'))}</div>`;
	const recoveryHtml = renderSaveRecoveryList(recoveries, recoveryError);
	return `
	<div class="content-view">
		<div class="page-head">
			<div>
				<div class="page-title">${esc(t('worlds.title'))}</div>
				<div class="page-sub">${esc(t('worlds.subtitle'))}</div>
			</div>
		</div>
		<div class="list-panel-head"><div class="list-heading"><h2>${uiText('单人存档','Singleplayer worlds')}</h2></div>${uiSearch('worldSearch',uiText('搜索存档…','Search worlds…'))}</div><div class="list-grid">${cards}</div>
		${recoveryHtml}
	</div>`;
}

/* M2-1①：已删除存档列表。**查询失败必须显示错误与重试入口**，不能显示成空列表 ——
 * 那会让用户以为"没东西可恢复"，从而放弃本该能找回的存档。 */
function renderSaveRecoveryList(recoveries, error) {
	if (error) {
		return `<div class="recovery-panel"><div class="wb-label">${esc(t('worlds.deletedList'))}</div>
			<div class="content-error" role="alert"><p>${esc(t('worlds.loadFailed'))}</p>
			<button class="ui-button" data-recovery-retry>${esc(t('content.retry'))}</button></div></div>`;
	}
	if (!recoveries || !recoveries.length) {
		return `<div class="recovery-panel"><div class="wb-label">${esc(t('worlds.deletedList'))}</div>
			<p class="ui-muted">${esc(t('worlds.deletedEmpty'))}</p></div>`;
	}
	const days = recoveries[0].retentionDays || 30;
	const rows = recoveries.map(r => `
		<div class="recovery-row" data-op="${esc(r.operationId)}" data-inst="${esc(r.instanceId || '')}" data-name="${esc(r.saveFolderName)}">
			<span class="recovery-name">${esc(r.saveFolderName)}</span>
			<span class="recovery-meta">${esc(r.sizeText || '')}${r.fileCount ? ' · ' + r.fileCount + ' 个文件' : ''}</span>
			<button class="world-btn" data-recovery-restore>${esc(t('restoreDeleted'))}</button>
			<button class="world-btn danger" data-recovery-purge>${esc(t('purgeDeleted'))}</button>
		</div>`).join('');
	return `<div class="recovery-panel"><div class="wb-label">${esc(t('worlds.deletedList'))}</div>
		<p class="ui-muted">${esc(t('worlds.deletedHint', { days }))}</p>
		<div class="recovery-list">${rows}</div></div>`;
}

/* 存档列表跨实例，但恢复接口是实例级的。取第一条的 instanceId 作为查询锚点；
 * 没有任何存档时退化为当前选中实例（仍能列出该实例的已删除项）。 */
function instanceIdForWorlds(worlds) {
	if (worlds && worlds.length && worlds[0].instanceId) return worlds[0].instanceId;
	return selectedInstanceId || '';
}

function bindWorlds(el) {
 bindCollectionRetry(el, 'worlds');
 bindListSearch(el,'worldSearch','.world-card');
	el.querySelectorAll('.world-card').forEach(c => c.addEventListener('click', async () => {
		const instanceId = c.dataset.inst;
		const folderName = c.dataset.folder;
		const name = c.querySelector('.world-name')?.textContent || folderName;
		if (!instanceId || !folderName) return;
		openWorldActions(instanceId, folderName, name);
	}));

	// M2-1①：已删除存档的恢复 / 永久清理
	el.querySelector('[data-recovery-retry]')?.addEventListener('click', async () => {
		const v = document.getElementById('view-worlds');
		if (v) { v.innerHTML = await renderWorlds(); bindWorlds(v); }
	});
	el.querySelectorAll('.recovery-row').forEach(row => {
		const operationId = row.dataset.op;
		const instanceId = row.dataset.inst || selectedInstanceId || '';
		const name = row.dataset.name || '';
		row.querySelector('[data-recovery-restore]')?.addEventListener('click', async (ev) => {
			ev.stopPropagation();
			const btn = ev.currentTarget;
			btn.disabled = true;
			try {
				const res = await SG.api.restoreSaveRecovery(instanceId, operationId);
				toast(res && res.restoredPath && res.restoredPath !== name
					? t('restoredFromRecovery') + ' → ' + res.restoredPath
					: t('restoredFromRecovery'));
				const v = document.getElementById('view-worlds');
				if (v) { v.innerHTML = await renderWorlds(); bindWorlds(v); }
			} catch (err) {
				toast(t('recoveryFailed') + (err.message || t('common.unknownError')));
				btn.disabled = false;
			}
		});
		row.querySelector('[data-recovery-purge]')?.addEventListener('click', async (ev) => {
			ev.stopPropagation();
			if (!confirm(t('purgeConfirm', { name }))) return;
			const btn = ev.currentTarget;
			btn.disabled = true;
			try {
				await SG.api.purgeSaveRecovery(instanceId, operationId);
				toast(t('purged'));
				const v = document.getElementById('view-worlds');
				if (v) { v.innerHTML = await renderWorlds(); bindWorlds(v); }
			} catch (err) {
				toast(t('recoveryFailed') + (err.message || t('common.unknownError')));
				btn.disabled = false;
			}
		});
	});
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
			// M2-1①：删除已改为"移入恢复区"，后端返回 recoveryId，文案如实说"可找回"。
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
		<div class="server-icon">${uiIcon('server')}</div>
		<div class="server-meta">
			<span class="server-name">${esc(s.name)}</span>
			<span class="server-sub">${esc(s.address)}${s.port && s.port !== 25565 ? ':' + s.port : ''} · ${esc(s.version || t('common.unknown'))}</span>
		</div>
		<div class="server-status">
			<div class="server-players">
				<span class="server-count ${online ? '' : 'offline'}">${online ? (s.online + ' / ' + s.max) : '—'}</span>
				<span class="server-ping ${pingCls}">${pingTxt}</span>
			</div>
			<button class="server-btn" data-server-ping>${uiText('刷新延迟','Refresh ping')}</button>
			<button class="inst-more" data-server-del="${esc(s.id)}" aria-label="${esc(t('servers.deleteConfirm'))}"><svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg></button>
		</div>
	</div>`;
}
async function renderServers() {
	let servers = [];
	let error = null;
	try { servers = await SG.api.getServers(); navigationCounts.servers = servers.length; } catch (e) { error = e; }
	const cards = error ? collectionErrorHtml('servers', error) : servers.length ? servers.map(serverCardHtml).join('')
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
		<div class="list-panel-head"><div class="list-heading"><h2>${uiText('收藏的服务器','Saved servers')}</h2></div>${uiSearch('serverSearch',uiText('搜索服务器…','Search servers…'))}</div><div class="list-grid" id="serverList">${cards}</div>
	</div>`;
}
function bindServers(el) {
 bindCollectionRetry(el, 'servers');
 bindListSearch(el,'serverSearch','.server-card');
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
   ================================================================
   M1-3 第三步：本视图已整体拆至 js/downloads.js（SG.downloads）。
   下列三个薄委托保留旧名字，供 VIEWS 路由表与 SG.__test__ 继续以原入口调用；
   实现、状态与请求代际全部在 downloads.js，本文件不再持有下载中心的任何状态。
   （测试文件 current-version-errors.test.cjs 断言的正是这两个生产入口，未改测试。） */
async function renderDownloads() { return SG.downloads.render(); }
function bindDownloads(el) { return SG.downloads.bind(el); }


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
	news:          { titleKey: 'view.news',          render: () => SG.news.render(), bind: el => SG.news.bind(el) },
	settings:      { titleKey: 'view.settings',      render: renderSettings,      bind: bindSettings },
	playtime:      { titleKey: 'nav.playtime', render: () => SG.playtime.render(), bind: el => SG.playtime.bind(el) },
};
let currentView = null;
let navSeq = 0;

async function goTo(nav) {
	const def = VIEWS[nav];
	if (!def) return;
	const myseq = ++navSeq;
	// Phase 3B v4 NEW green（Codex gpt-5.6-sol 裁决）：导航开始时按当前 modal 代次
	// 触发现有 modal aborter，强制 pending diagnose 走与 closeModal 同一条 UI-token →
	// abort fetch → cancelOnce(instanceId/operationId) → 解绑路径。复用 _modalAborters
	// 与现有 teardown 生命周期，不创建第二套 operation 状态、不关闭无关弹窗。
	// 销毁钩子必须永不阻塞导航，所有内部异常吞掉。
	try {
		const _mh = document.getElementById('modalHost');
		const _curGen = _mh ? parseInt(_mh.dataset.modalGeneration || '0', 10) : 0;
		const _ab = _modalAborters.get(_curGen);
		if (typeof _ab === 'function') {
			try { _ab(); } catch (_) { /* cleanup must not fail navigation */ }
		}
	} catch (_) { /* any lookup failure must not affect navigation */ }
	// 关键（v4 + v5）：切走 content 时清理 debounce + 标记 pageGen 失效。
	// 切走时不清父容器 click 监听器（那是 bindContent 的幂等标记，
	// 下次进入仍能复用），但要清理模块级资源：
	//   - contentDebTimer 防止离页后到期触发 searchContent
	//   - contentPageGen = -1 让任何 pending renderContent / contentRerenderGrid 短路
	// v5 关键：即使 currentView 仍为 null（首次 goTo content 还在 renderContent 中），
	// 用户调用 goTo('home') 也必须立刻标记 pageGen=-1，否则旧 renderContent
	// 在 getState 完成后会继续写 el.innerHTML、设置 rendered、绑定事件。
	if (nav !== 'content' && contentPageGen !== -1) {
		contentPageGen = -1;
		if (contentDebTimer !== null) { clearTimeout(contentDebTimer); contentDebTimer = null; }
	}
	// 关键（v4 + v5）：进入 content 时分配新的 pageGen。
	// 用 myseq（本次 goTo 的 navSeq）作为 pageGen，足以在同一 pageGen 内
	// 区分"这次进入的资源页"与"之前进入的资源页"——goTo 重新进入 content 时
	// myseq 必然变大。同时 pageGen 也用于 retryLoadStateAndRefresh 的
	// "我启动时的 pageGen" 校验：await loadState 期间用户切走再回来，
	// pageGen 会变化，retry 失效。
	if (nav === 'content' && currentView !== 'content') {
		contentPageGen = myseq;
	}
	if (currentView === 'skins' && nav !== 'skins') SG.skins.dispose();
	if (currentView === 'playtime' && nav !== 'playtime' && SG.playtime) SG.playtime.dispose();
	document.querySelectorAll('.view').forEach(v => { v.hidden = true; });
	const el = document.getElementById('view-' + nav);
	if (!el) return;
	if (nav === 'playtime') delete el.dataset.rendered;
	// 关键（v5）：进入 content 时记录本次 goTo 的 pageGen。提交结果前再校验，
	// 避免"旧 renderContent 写空 innerHTML + 设置 rendered + 绑定事件"。
	let expectedPageGen = (nav === 'content') ? contentPageGen : null;
	if (!el.dataset.rendered) {
		el.innerHTML = '<div style="padding:40px;color:var(--muted,#8b91a1);font-size:13px">' + esc(t('common.loading')) + '</div>';
		// 关键：把 render 结果存到局部变量，提交 DOM 前再检查 navSeq。
		// 否则过期请求的 render() 结果（可能为空字符串）会先写入 el.innerHTML，
		// 再标记 el.dataset.rendered = '1' 并绑定事件，最后才被 navSeq 短路返回 —
		// 期间已对当前页面造成不可逆的清空与重复绑定。
		let renderedHtml = null;
		let renderError = null;
		try { renderedHtml = await def.render(); } catch (e) { renderError = e; }
		if (myseq !== navSeq) return;
		// 关键（v5）：资源页的 pageGen 已被 -1 标记（用户已切走或被新生命周期取代），
		// renderContent/重渲返回的是空字符串。这种情况下不能写 innerHTML、不能
		// 设置 rendered、不能绑事件；返回后由新 goTo 重新渲染。
		if (nav === 'content' && expectedPageGen !== contentPageGen) return;
		if (renderError) {
			el.innerHTML = '<div style="padding:40px;color:#e06">' + esc(t('common.loadFailed')) + esc(renderError.message) + '</div>';
		} else {
			el.innerHTML = renderedHtml;
		}
		el.dataset.rendered = '1';
		if (def.bind) def.bind(el);
	} else if (nav === 'content') {
		// 关键（v5）：返回资源页时如果 grid 仍含 loading-state（说明离页时
		// 有 in-flight 的搜索 A 还未完成），自动按当前 source/cat/keyword 重新搜索，
		// 避免页面永久停留在 loading 状态。
		const grid = el.querySelector('#contentGrid');
		if (grid && grid.querySelector('.loading-state')) {
			// 用 fire-and-forget：不让 goTo 阻塞；contentRerenderGrid 自己有 token + pageGen 校验。
			contentRerenderGrid(el);
		}
	}
	// 异步渲染期间若已发生更新的导航，放弃本次显示，避免两个视图叠加
	if (myseq !== navSeq) return;
	// 再次断言：只有目标视图可见（消除 async 竞态导致的页面串叠）
	document.querySelectorAll('.view').forEach(v => { v.hidden = (v !== el); });
	document.getElementById('contentPanel').scrollTop = 0;
	document.querySelectorAll('.nav-item').forEach(b => b.classList.toggle('active', b.dataset.nav === nav));
	updateShell(nav);
	currentView = nav;
	try { if (('#' + nav) !== location.hash) history.replaceState(null, '', '#' + nav); } catch (e) { /* file:// 下忽略 */ }
}
SG.goTo = goTo;

function invalidate(nav) {
	if (nav === 'skins') SG.skins.dispose();
	const el = document.getElementById('view-' + nav);
	if (el) delete el.dataset.rendered;
}

/* ---------------- 启动 ---------------- */
let launchInFlight = false;

/* M2-1④：把后端的结构化问题归一成前端可用的形状。
 *
 * 为什么要归一而不是直接用 check.issues：
 *   老后端（或降级到 mock 的 `_mock` 通道）只返回 errors/warnings 两个字符串数组。
 *   如果不兜底，界面在旧内核上会变成"有问题但不显示任何内容"——比现在更糟。
 *   所以：**issues 优先，缺失时用 errors/warnings 合成**，并统一补上 severity。
 * 刻意不给合成项补 action：旧数据里没有动作信息，硬猜一个跳转是错的。 */
function normPrelaunchIssues(check) {
	if (Array.isArray(check.issues) && check.issues.length) {
		return check.issues.map(x => ({
			code: x.code || '',
			severity: x.severity === 'warning' ? 'warning' : 'error',
			message: x.message == null ? '' : String(x.message),
			action: x.action || null,
			actionLabel: x.actionLabel || null,
		}));
	}
	const out = [];
	(check.errors || []).forEach(m => out.push({ code: '', severity: 'error', message: String(m), action: null, actionLabel: null }));
	(check.warnings || []).forEach(m => out.push({ code: '', severity: 'warning', message: String(m), action: null, actionLabel: null }));
	return out;
}

/* M2-1④：按后端的 action 标识跳转到处理入口。
 *
 * 只认白名单里的三个值，其余一律 toast 提示"请到设置页处理"——
 * 不认识的 action 静默无反应是最糟的，用户会以为按钮坏了。 */
function runPrelaunchAction(action, instanceId) {
	if (action === 'install-version') {
		SG.goTo('downloads');
		return;
	}
	if (action === 'select-java') {
		Promise.resolve(SG.goTo('settings')).then(() => {
			const btn = document.getElementById('btnJrePath');
			if (btn) { btn.scrollIntoView({ block: 'center' }); btn.focus(); }
		}).catch(() => { /* 导航失败不影响用户手动去设置页 */ });
		return;
	}
	if (action === 'open-instance-settings') {
		// 实例设置目前没有独立弹窗，内存与游戏目录都在设置页；直接跳过去。
		SG.goTo('settings');
		return;
	}
	toast(t('launch.fixHint'));
}

async function doLaunch(instanceId, label) {
	if (!instanceId) { toast(t('launch.selectFirst')); return; }
	if (launchInFlight) return;
	launchInFlight = true;
	try {
	// 启动前环境检查
	let check;
	try {
		check = await SG.api.prelaunchCheck(instanceId);
	} catch (e) { check = null; }
	if (!check || typeof check.ok !== 'boolean') {
		openModal(`<div class="modal-title">${esc(t('launch.checkIncompleteTitle'))}</div>
			<p>${esc(t('launch.checkIncompleteDesc'))}</p>
			<div class="modal-actions">
				<button class="modal-btn" id="pcClose">${esc(t('common.close'))}</button>
				<button class="modal-btn primary" id="pcRetry">${esc(t('dl.retry'))}</button>
			</div>`);
		document.getElementById('pcClose').onclick = closeModal;
		document.getElementById('pcRetry').onclick = () => { closeModal(); return doLaunch(instanceId, label); };
		return;
	}
	if (check && !check.ok) {
		const issues = normPrelaunchIssues(check);
		// 主按钮是「去处理」：直接跳到该问题的处理入口，不再让用户自己去翻设置页。
		// 只取**第一条带 action 的错误**——引导必须唯一，给三个按钮等于没给。
		const first = issues.find(x => x.severity === 'error' && x.action);
		const errs = issues.filter(x => x.severity === 'error')
			.map(x => `<div class="check-line err">⚠ ${esc(x.message)}</div>`).join('');
		openModal(`
			<div class="modal-title">${esc(t('launch.checkTitle'))}</div>
			<div class="prelaunch-issues">${errs}</div>
			${first ? `<p class="ui-muted">${esc(t('launch.fixHint'))}</p>` : ''}
			<div class="modal-actions">
				<button class="modal-btn" id="pcClose">${esc(t('common.close'))}</button>
				${first ? `<button class="modal-btn primary" id="pcFix">${esc(first.actionLabel || t('launch.fix'))}</button>` : ''}
			</div>`);
		document.getElementById('pcClose').onclick = closeModal;
		const fixBtn = document.getElementById('pcFix');
		if (fixBtn && first) {
			fixBtn.onclick = () => { closeModal(); runPrelaunchAction(first.action, instanceId); };
		}
		toast(t('launch.checkFailed'));
		return;
	}
	if (check && check.warnings && check.warnings.length) {
		const issues = normPrelaunchIssues(check);
		const firstWarn = issues.find(x => x.severity === 'warning' && x.action);
		const warns = issues.filter(x => x.severity === 'warning')
			.map(x => `<div class="check-line warn">⚠ ${esc(x.message)}</div>`).join('');
		openModal(`
			<div class="modal-title">${esc(t('launch.checkWarnTitle'))}</div>
			<div class="prelaunch-issues">${warns}</div>
			<div class="modal-actions">
				<button class="modal-btn" id="pcCancel">${esc(t('common.cancel'))}</button>
				${firstWarn ? `<button class="modal-btn" id="pcFix">${esc(firstWarn.actionLabel || t('launch.fix'))}</button>` : ''}
				<button class="modal-btn primary" id="pcOk">${esc(t('launch.continue'))}</button>
			</div>`);
		document.getElementById('pcCancel').onclick = closeModal;
		const warnFix = document.getElementById('pcFix');
		if (warnFix && firstWarn) {
			warnFix.onclick = () => { closeModal(); runPrelaunchAction(firstWarn.action, instanceId); };
		}
		document.getElementById('pcOk').onclick = async () => {
			if (launchInFlight) return;
			launchInFlight = true;
			try { closeModal(); await doLaunchInternal(instanceId, label); }
			finally { launchInFlight = false; }
		};
		return;
	}
	await doLaunchInternal(instanceId, label);
	} finally { launchInFlight = false; }
}

let playLaunchStartedAt = 0;
let playLaunchTimer = null;
let playDoneTimer = null;
let playSettleTimer = null;
const PLAY_LAUNCH_MIN_MS = 1200; // 保证加载形变至少可见，启动请求很快返回时也不会一闪而过
const PLAY_DONE_MS = 1500;        // 成功对勾停留时长，之后按钮展开回原宽度
const PLAY_MORPH_MS = 500;        // 与 CSS 中宽度过渡时长保持一致
// 形变链：宽按钮 → 圆形加载 → (成功)圆形对勾 → 宽按钮
// 宽度必须在两个像素值之间过渡（auto 无法过渡），因此收缩前记录实际宽度，展开后再清除内联宽度
function setPlayLaunching(on, ok) {
	clearTimeout(playLaunchTimer);
	clearTimeout(playDoneTimer);
	clearTimeout(playSettleTimer);
	const btn = document.getElementById('btnPlay');
	if (!btn) return;
	if (on) {
		playLaunchStartedAt = Date.now();
		// 若上一轮对勾尚未收起，当前宽度是 44px，应沿用已记录的原宽度
		const shrunk = btn.classList.contains('is-done') || btn.classList.contains('is-launching');
		const full = (shrunk && Number(btn.dataset.fullW)) || btn.offsetWidth;
		btn.dataset.fullW = full;
		btn.style.width = full + 'px';
		void btn.offsetWidth; // 先固定当前宽度，再开始收缩
		btn.classList.remove('is-done');
		btn.classList.add('is-launching');
		btn.style.width = '44px';
		// 文字在加载/完成态被隐藏，这里保留可访问名称并标记忙碌状态
		btn.setAttribute('aria-busy', 'true');
		btn.setAttribute('aria-label', t('home.play'));
		return;
	}
	const wait = Math.max(0, PLAY_LAUNCH_MIN_MS - (Date.now() - playLaunchStartedAt));
	playLaunchTimer = setTimeout(() => {
		btn.classList.remove('is-launching');
		btn.removeAttribute('aria-busy');
		if (ok) {
			btn.classList.add('is-done');
			playDoneTimer = setTimeout(expandPlayBtn, PLAY_DONE_MS);
		} else {
			expandPlayBtn();
		}
	}, wait);
}
function expandPlayBtn() {
	const btn = document.getElementById('btnPlay');
	if (!btn) return;
	btn.classList.remove('is-done');
	btn.removeAttribute('aria-label');
	btn.style.width = (btn.dataset.fullW || '') + 'px';
	playSettleTimer = setTimeout(() => { btn.style.width = ''; }, PLAY_MORPH_MS);
}

function doLaunchInternal(instanceId, label) {
	toast(t('launch.starting', { name: label || t('launch.game') }));
	setPlayLaunching(true);
	return SG.api.launchInstance(instanceId)
		.then(r => {
			setPlayLaunching(false, !!(r && r.ok));
			if (r && r.ok) {
				toast(t('launch.sent'));
				// 快捷操作：启动后最小化启动器
				const cfg = STATE.config || {};
				if (cfg.minimizeOnLaunch) {
					if (NATIVE_SHELL) sendNative({ type: 'window-control', action: 'minimize' });
					else { try { if (window.parent && window.parent.minimizeWindow) window.parent.minimizeWindow(); } catch (e) {} }
				}
			} else toast((r && r.message) || t('launch.failed'));
		})
		.catch(err => { setPlayLaunching(false); toast(t('launch.failedPrefix') + ((err && err.message) || t('common.unknownError'))); });
}

/* ---------------- Play 页 ---------------- */
function bindHome(el) {
	// Phase 3A：只有 linkOk=1 的官方链接才打开，否则跳转 News 页（实现在 js/news.js）
	SG.news.bindHomeNewsCards(el);
	SG.news.bindArt(el);
	SG.news.bindRetry(el);

	const newsMore = el.querySelector('#newsMore');
	if (newsMore) newsMore.addEventListener('click', () => goTo('news'));
	const selEl = el.querySelector('#pfVersionSelect');
	if (selEl) selEl.addEventListener('change', async () => { selectedInstanceId = selEl.value; invalidate('installations'); invalidate('home'); await goTo('home'); });
	el.querySelector('#btnHomeInstances')?.addEventListener('click', () => goTo('installations'));
	el.querySelector('#homeAllDownloads')?.addEventListener('click', () => goTo('downloads'));
	refreshHomeQueue(el);
	const playBtn = el.querySelector('#btnPlay');
	if (playBtn) playBtn.addEventListener('click', () => {
		const inst = (STATE.instances || []).find(i => i.id === selectedInstanceId);
		doLaunch(selectedInstanceId, inst ? inst.name : t('launch.game'));
	});
}

async function refreshHomeQueue(el) {
	const host = el.querySelector('#homeDownloadQueue');
	if (!host) return;
	try {
		const tasks = (await SG.api.getDownloads()).filter(task=>task.status==='queued'||task.status==='downloading');
		if (!host.isConnected) return;
		host.innerHTML = tasks.length ? tasks.slice(0, 4).map(task => {
			const pct = Math.min(100, Math.max(0, Number(task.progress) || 0));
			return `<div class="home-task"><div class="home-task-line"><span>${esc(task.name || task.id)}</span><span class="home-task-state">${esc(t('shell.task.' + (task.status || 'queued')))} · ${Math.round(pct)}%</span></div><div class="home-task-track"><span style="width:${pct}%"></span></div></div>`;
		}).join('') : `<p class="ui-muted">${esc(t('shell.noTasks'))}</p>`;
	} catch (e) { host.textContent = t('shell.queueFailed'); }
}

const SHELL_PAGES = {
	home:['nav.home','shell.homeSub'], installations:['nav.installations','inst.subtitle'],
	worlds:['nav.worlds','worlds.subtitle'], servers:['nav.servers','servers.subtitle'],
	playtime:['nav.playtime','pt.subtitle'], content:['nav.content','content.subtitle'],
	skins:['nav.skins','skin.previewFirst'], downloads:['nav.downloads','dl.subtitle'],
	news:['nav.news','news.subtitle'], settings:['nav.settings','shell.settingsSub'],
};
function updateShell(nav) {
	const keys = SHELL_PAGES[nav] || SHELL_PAGES.home;
	const title = document.getElementById('workspaceTitle'), sub = document.getElementById('workspaceSubtitle');
	if (title) title.textContent = t(keys[0]);
	if (sub) sub.textContent = t(keys[1]);
 if(STATE&&STATE.instances)navigationCounts.installations=STATE.instances.length;
	document.querySelectorAll('[data-nav-count]').forEach(e=>{const n=navigationCounts[e.dataset.navCount];e.hidden=n==null;if(n!=null)e.textContent=String(n);});
	document.querySelectorAll('.nav-item').forEach(b => {
		if (b.dataset.nav === nav) b.setAttribute('aria-current','page'); else b.removeAttribute('aria-current');
	});
	const zh = document.getElementById('btnLangZh'), en = document.getElementById('btnLangEn');
	if (zh) zh.setAttribute('aria-pressed', String(SG.locale === 'zh'));
	if (en) en.setAttribute('aria-pressed', String(SG.locale === 'en'));
	const version = document.getElementById('shellVersion');
	if (version && STATE && STATE.config && STATE.config.appVersion) version.textContent = 'v' + STATE.config.appVersion;
}

function openCommandPalette() {
	const host = document.getElementById('modalHost');
	// Do not replace an in-flight business dialog (diagnosis, import or confirmation).
	if (host && !host.hidden && host.classList.contains('show')) return;
	const entries = Object.keys(SHELL_PAGES).map(nav => ({name:t('shell.open') + ' · ' + t(SHELL_PAGES[nav][0]), run:()=>goTo(nav)}));
	entries.push({name:t('home.play'),run:()=>{const i=(STATE&&STATE.instances||[]).find(x=>x.id===selectedInstanceId);if(i)doLaunch(i.id,i.name);else goTo('installations');}});
	entries.push({name:t('set.openLogs'),run:()=>SG.api.openLogDirectory()});
	entries.push({name:t('set.checkUpdate'),run:async()=>{await goTo('settings');document.getElementById('btnCheckUpdate')?.click();}});
	openModal(`<div class="modal-title">${esc(t('shell.quick'))}</div><input class="palette-search" id="commandQuery" type="search" autocomplete="off" placeholder="${esc(t('shell.commandSearch'))}" aria-label="${esc(t('shell.commandSearch'))}"><div class="palette-results" id="commandResults"></div><p class="palette-hint">${esc(t('shell.commandHint'))}</p>`);
	const input = document.getElementById('commandQuery'), list = document.getElementById('commandResults');
	const draw = () => {
		const q = input.value.trim().toLocaleLowerCase();
		list.innerHTML = entries.map((e,i)=>({e,i})).filter(x=>x.e.name.toLocaleLowerCase().includes(q)).map(x=>`<button class="palette-item" data-command="${x.i}">${esc(x.e.name)}<small>↵</small></button>`).join('') || `<p class="ui-muted">${esc(t('shell.noCommands'))}</p>`;
	};
	draw(); input.oninput = draw;
	const run = b => {if(!b)return;const entry=entries[Number(b.dataset.command)];closeModal();Promise.resolve(entry.run()).catch(e=>toast(e.message));};
	list.onclick=e=>run(e.target.closest('[data-command]'));
	input.onkeydown=e=>{if(e.key==='ArrowDown'){e.preventDefault();list.querySelector('button')?.focus();}else if(e.key==='Enter'){e.preventDefault();run(list.querySelector('button'));}};
	list.onkeydown=e=>{const buttons=[...list.querySelectorAll('button')],idx=buttons.indexOf(document.activeElement);if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();buttons[(idx+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus();}};
	input.focus();
}

function wireRedesignedShell() {
	document.getElementById('btnQuickOpen')?.addEventListener('click',openCommandPalette);
	document.getElementById('btnProfile')?.addEventListener('click',()=>goTo('settings'));
	document.getElementById('btnLangZh')?.addEventListener('click',()=>SG.setLocale('zh'));
	document.getElementById('btnLangEn')?.addEventListener('click',()=>SG.setLocale('en'));
	document.getElementById('btnRefreshView')?.addEventListener('click',async e=>{
		if(e.currentTarget.disabled)return;
		const b=e.currentTarget,nav=currentView||'home',startedSeq=navSeq;b.disabled=true;
		try {await loadState(true);await loadAccounts(true);refreshSidebarAccount();if(startedSeq!==navSeq)return;invalidate(nav);await goTo(nav);}
		catch(err){toast(t('common.loadFailed')+(err.message||''));}finally{b.disabled=false;}
	});
	document.addEventListener('keydown',e=>{if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();openCommandPalette();}});
}

/* ================================================================
function fmtPlayTime(seconds) {
	seconds = Math.max(0, Math.floor(seconds || 0));
	if (seconds < 60) return seconds + 's';
	const m = Math.floor(seconds / 60);
	if (m < 60) return m + 'm';
	const h = Math.floor(m / 60);
	if (h < 24) return h + 'h ' + (m % 60) + 'm';
	const d = Math.floor(h / 24);
	return d + 'd ' + (h % 24) + 'h';
}

/* ---------------- Installations 页 ---------------- */
function bindInstallations(el) {
 bindListSearch(el,'instanceSearch','.inst-card');
	el.querySelectorAll('.filter').forEach(f => {
		f.addEventListener('click', () => {
			el.querySelectorAll('.filter').forEach(x => x.classList.remove('active'));
			f.classList.add('active');
			const kind = f.dataset.filter;
			el.querySelectorAll('.inst-card').forEach(card => {
				let show = true;
				if (kind === 'mod') show = card.dataset.kind === 'mod';
				else if (kind === 'snap') show = card.dataset.kind === 'snap';
                const query=el.querySelector('#instanceSearch')?.value.trim().toLocaleLowerCase()||'';
                card.hidden=!show||!card.textContent.toLocaleLowerCase().includes(query);
			});
		});
	});
	// MR03：从已有目录添加 —— "从列表移除"的逆操作，只写配置记录，不创建/移动/删除文件。
	el.querySelector('#btnAddExisting')?.addEventListener('click', () => {
		openModal(`
			<div class="modal-title">${esc(t('inst.addExistingTitle'))}</div>
			<div style="font-size:12px;color:var(--muted,#8b91a1);margin-bottom:10px">${esc(t('inst.addExistingHint'))}</div>
			<div class="modal-field"><span class="modal-label">${esc(t('inst.addExistingDirLabel'))}</span><input class="modal-input" id="mRegDir" placeholder="${esc(t('inst.addExistingDirPlaceholder'))}"></div>
			<div class="modal-field"><span class="modal-label">${esc(t('inst.addExistingNameLabel'))}</span><input class="modal-input" id="mRegName"></div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="mOk">${esc(t('inst.addExistingSubmit'))}</button>
			</div>`);
		document.getElementById('mCancel').onclick = closeModal;
		document.getElementById('mOk').onclick = async () => {
			const dir = (document.getElementById('mRegDir').value || '').trim();
			const nm = (document.getElementById('mRegName').value || '').trim();
			if (!dir) { toast(t('inst.addExistingDirRequired')); return; }
			try {
				await SG.api.registerInstance(dir, nm);
				closeModal();
				await loadState(true);
				invalidate('home');
				const v = document.getElementById('view-installations');
				v.innerHTML = await renderInstallations(); bindInstallations(v);
				toast(t('inst.addExistingAdded'));
			} catch (err) { toast(t('inst.addExistingFailed') + (err.message || t('common.unknownError'))); }
		};
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
	el.querySelectorAll('[data-mods]').forEach(b => b.addEventListener('click', e => {
		e.stopPropagation(); const inst = (STATE.instances || []).find(i => i.id === b.dataset.mods); if (inst) openModManager(inst);
	}));
	el.querySelectorAll('.inst-card').forEach(card => {
		card.addEventListener('click', e => {
			if (e.target.closest('[data-play],[data-more],[data-mods]')) return;
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
/* M1-3 第三步后段：皮肤视图已整体拆至 js/skins.js（SG.skins）。
   activeSkinViewer 的生命周期也归 skins.js 所有，本文件不再持有该变量；
   需要销毁时调用 SG.skins.dispose()（goTo 切走、invalidate 两处）。 */
function bindSkins(el) { return SG.skins.bind(el); }

/* ---------------- Settings 页 ----------------
   M1-3 第三步收尾：本视图已整体拆至 js/settings.js（SG.settings）。
   与 content 的差别：settings 没有导航级状态，它读写的是 STATE.config 的属性，
   所以注入的是 getState() 取值函数（STATE 是对象引用，改属性作用于同一对象）。
   设置页内部 6 处「改完整页刷新」抽成了模块内的 rerenderInPlace()。
   下列薄委托保留旧名字，供 VIEWS 路由表、openAiManage 的回调与 SG.__test__ 使用。
   ---- 未拆：Settings 更新区（updateMonitor 等）仍在本文件，其后紧接。 --- */
async function renderSettings() { return SG.settings.render(); }
function bindSettings(el) { return SG.settings.bind(el); }

/* ---------------- Settings 更新区 ---------------- */
let updateMonitor = null;
let latestUpdateInfo = null;

function renderUpdateNotice(result) {
	if (!result || result.error) return;
	latestUpdateInfo = result.enabled && result.updateAvailable ? result : null;
	let banner = document.getElementById('updateNotice');
	if (!latestUpdateInfo) { if (banner) banner.remove(); return; }
	if (!banner) {
		const main = document.querySelector('.workspace-main');
		if (!main) return;
		banner = document.createElement('button');
		banner.type = 'button';
		banner.id = 'updateNotice';
		banner.className = 'update-notice';
		banner.addEventListener('click', () => showUpdateAnnouncement(latestUpdateInfo));
		main.insertBefore(banner, document.getElementById('contentPanel'));
	}
	banner.textContent = t('set.updateNotice', { ver: result.latestVersion });
}

function showUpdateAnnouncement(result) {
	const host = document.getElementById('modalHost');
	if (!result || !host || !host.hidden || document.hidden) return false;
	openModal(`<div class="modal-title">${esc(t('set.updateAvailable', { ver: result.latestVersion }))}</div>
		<p class="page-sub">${esc(t('set.updateFromVersion', { ver: result.currentVersion }))}</p>
		<div class="update-release-label">${esc(t('set.releaseNotes'))}</div>
		<div class="update-release-notes">${esc(result.notes || t('set.noReleaseNotes'))}</div>
		<div class="modal-actions"><button class="modal-btn ghost" id="updateLater">${esc(t('set.updateLater'))}</button>
		<button class="modal-btn primary" id="updateOpenSettings">${esc(t('set.updateOpenSettings'))}</button></div>`);
	document.getElementById('updateLater').onclick = closeModal;
	document.getElementById('updateOpenSettings').onclick = async () => {
		closeModal();
		await goTo('settings');
		document.getElementById('btnCheckUpdate')?.click();
	};
	return true;
}

function startUpdateMonitor() {
	if (updateMonitor || SG.apiMode !== 'live' || !SG.createUpdateMonitor) return;
	updateMonitor = SG.createUpdateMonitor({
		check: () => SG.api.checkUpdate(),
		onResult: renderUpdateNotice,
		notify: showUpdateAnnouncement,
	});
	updateMonitor.start();
	window.addEventListener('pagehide', () => updateMonitor.stop(), { once: true });
}

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
			const r = await (updateMonitor ? updateMonitor.check({ notify: false }) : SG.api.checkUpdate());
			renderUpdateNotice(r);
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
						<div class="update-release-label">${esc(t('set.releaseNotes'))}</div>
						<div class="update-notes">${esc(r.notes || t('set.noReleaseNotes'))}</div>
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
		// MR20：先展示"上次替换到底成没成"，再谈状态。
		renderUpdateTrust(el, s);
		if (s.state === 'downloading') pollUpdateProgress(el, s);
		else if (s.state === 'completed') renderUpdateReady(el, s);
		else if (s.state === 'failed') statusEl.textContent = '❌ ' + (s.error || t('set.updateCheckFailed'));
	}).catch(() => {});
}

// MR20：把「上次替换结果」与「信任链声明」显示给用户。
//
// 为什么必须有：替换脚本是**退出启动器之后**才跑的，界面当时只能知道"脚本已启动"。
// 如果脚本失败了（备份失败/替换失败/校验失败），用户下次打开启动器看到的仍是旧版本 ——
// 若不告诉他"上次没成"，他会以为更新完成了。这里把脚本写下的结果如实展示出来。
function renderUpdateTrust(el, s) {
	const host = el.querySelector('#updateTrust');
	if (!host) return;
	const parts = [];
	const last = s.lastApplyResult;
	if (last && last.applied === false) {
		parts.push(
			`<div class="update-warn">⚠️ ${esc(t('set.lastApplyFailed', { result: last.result || 'unknown' }))}` +
			`${last.version ? ' · ' + esc(t('set.lastApplyVersion', { version: last.version })) : ''}` +
			`<div class="update-warn-hint">${esc(t('set.lastApplyFailedHint'))}</div></div>`);
	}
	if (s.trustNote) {
		parts.push(`<div class="update-trust">${esc(t('set.updateTrustNote', { note: s.trustNote }))}</div>`);
	}
	host.innerHTML = parts.join('');
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
	if (!acc) {
		const name = document.getElementById('profileName'), status = document.getElementById('profileStatus');
		if (name) name.textContent = t('shell.notSignedIn');
		if (status) status.textContent = t('sidebar.offlineMode');
		return;
	}
	const nameEl = document.getElementById('profileName');
	if (nameEl) nameEl.textContent = acc.userName || 'Player';
	const statusEl = document.getElementById('profileStatus');
	if (statusEl) statusEl.textContent = acc.type === 'microsoft' ? t('sidebar.microsoft') : acc.type === 'authlib' ? t('sidebar.authlib') : t('sidebar.offlineMode');
	const avatarEl = document.getElementById('profileAvatar');
	if (avatarEl) {
		// 新版侧栏头像为 30px；按容器内宽生成图标及皮肤图集，避免沿用 40px 后被裁切。
		const style = getComputedStyle(avatarEl);
		const size = Math.floor(parseFloat(style.width) - parseFloat(style.borderLeftWidth) - parseFloat(style.borderRightWidth));
		avatarEl.innerHTML = faceThumb(size, acc.skins && acc.skins[0]);
	}
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
	if (latestUpdateInfo) renderUpdateNotice(latestUpdateInfo);
	Object.keys(VIEWS).forEach(invalidate);
	if (currentView) {
		const nav = currentView;
		currentView = null; // 强制 goTo 重新渲染
		goTo(nav);
	}
};

async function init() {
	wireWindowControls();
	wireRedesignedShell();
	wireNav();
	wireLangToggle();
	startUpdateMonitor();
	document.documentElement.setAttribute('lang', SG.locale === 'zh' ? 'zh-CN' : 'en');
	if (SG.applyStaticI18n) SG.applyStaticI18n(document);
	// 预取账户用于侧栏
	loadAccounts().then(refreshSidebarAccount).catch(() => {});
	const startNav = (location.hash || '').replace('#', '');
	// 资源页或新闻页等依赖 STATE 的视图必须先于首次渲染加载实例状态；
	// 否则 #content 直进时 searchContentOptions 拿不到 gameVersion/loader。
	if (startNav === 'content') {
		try { await loadState(); } catch (e) { /* loadState 内部已降级 */ }
	}
	await goTo(VIEWS[startNav] ? startNav : 'home');
	window.addEventListener('hashchange', () => {
		const nav = (location.hash || '').replace('#', '');
		if (VIEWS[nav] && nav !== currentView) goTo(nav);
	});
	wireDragImport();
}

/* ---------------- 拖拽导入整合包 ---------------- */
function wireDragImport() {
	let counter = 0;
	window.addEventListener('dragenter', (e) => {
		e.preventDefault();
		counter++;
		showDropOverlay();
	});
	window.addEventListener('dragover', (e) => { e.preventDefault(); });
	window.addEventListener('dragleave', (e) => {
		e.preventDefault();
		if (--counter <= 0) { counter = 0; hideDropOverlay(); }
	});
	window.addEventListener('drop', (e) => {
		e.preventDefault();
		counter = 0;
		hideDropOverlay();
		const files = e.dataTransfer && e.dataTransfer.files;
		if (!files || !files.length) return;
		handleImportFiles(files);
	});
}

function showDropOverlay() {
	let el = document.getElementById('dropOverlay');
	if (!el) {
		el = document.createElement('div');
		el.id = 'dropOverlay';
		el.className = 'drop-overlay';
		el.innerHTML = `<div class="drop-box"><div class="drop-icon">📦</div><div class="drop-text">${esc(t('import.dropHint'))}</div></div>`;
		document.body.appendChild(el);
	}
	el.classList.add('active');
}

function hideDropOverlay() {
	const el = document.getElementById('dropOverlay');
	if (el) el.classList.remove('active');
}

async function handleImportFiles(files) {
	const file = files[0];
	if (!file) return;
	const okExt = /\.(zip|mrpack|mcm)$/i;
	if (!okExt.test(file.name)) {
		toast(t('import.badType'));
		return;
	}
	openModal(`
		<div class="modal-title">${esc(t('import.importing', { name: file.name }))}</div>
		<div class="page-sub">${esc(t('import.importingSub'))}</div>
		<div class="loading-state" style="padding:16px">${esc(t('common.loading'))}</div>
		<div class="modal-actions">
			<button class="modal-btn ghost" id="impCancel">${esc(t('import.cancel'))}</button>
			<button class="modal-btn" id="impClose">${esc(t('common.close'))}</button>
		</div>
	`);
	document.getElementById('impClose').onclick = closeModal;
	// MR11：导入是可取消的——上传/安装阶段都能取消，取消后不留半成品实例。
	const operationId = (window.crypto && crypto.randomUUID) ? crypto.randomUUID().replace(/-/g, '') : ('imp' + Date.now() + Math.random().toString(36).slice(2));
	// LQ-01：把「用户点击取消的意图」和「服务端确认取消」分开保存。点击取消只是意图——
	// 取消接口失败、或只回 pendingCancel 时，服务端都没有确认取消，不能据此宣称导入已中止。
	let cancelRequested = false;
	let cancelConfirmed = false;
	const cancelBtn = document.getElementById('impCancel');
	if (cancelBtn) {
		cancelBtn.onclick = async () => {
			if (cancelRequested) return;
			cancelRequested = true;
			cancelBtn.disabled = true;
			cancelBtn.textContent = t('import.cancelling');
			try {
				const r = await SG.api.cancelImport(operationId);
				if (r && r.cancelled === true) { cancelConfirmed = true; toast(t('import.cancelled')); }
				else { toast(t('import.cancelRecorded')); }
			} catch (e) {
				toast(t('import.cancelFailed') + ((e && e.message) || ''));
			}
		};
	}
	try {
		const b64 = await readFileAsBase64(file);
		const res = await SG.api.importInstance(file.name, b64, operationId);
		toast(t('import.done', { name: res.name || file.name }));
		closeModal();
		await loadState(true);
		selectedInstanceId = res.instanceId || selectedInstanceId;
		// 刷新首页与实例页
		invalidate('home');
		invalidate('installations');
		const v = document.getElementById('view-home');
		if (v) { v.innerHTML = await renderHome(); bindHome(v); }
	} catch (err) {
		// LQ-01：只有服务端明确确认取消（取消接口回了 cancelled，或导入错误自身带 cancelled 信号）
		// 才允许显示"已取消/未生成实例"。
		if ((err && err.cancelled) || cancelConfirmed) {
			// 取消不是失败：明确告诉用户"没有生成实例"，而不是丢一句导入失败。
			toast(t('import.cancelled'));
			const stc = document.querySelector('.modal .loading-state');
			if (stc) stc.textContent = t('import.cancelledHint');
			return;
		}
		// LQ-01：普通失败不能被当成"已取消"。点过取消但服务端未确认时，如实报失败并说明
		// 结果尚未确认，让用户可以去实例列表核对，而不是相信"没有生成实例"。
		const why = (err && err.message) || t('common.unknownError');
		const failedText = t('import.failed') + why + (cancelRequested ? ' ' + t('import.outcomeUnconfirmed') : '');
		toast(failedText);
		const st = document.querySelector('.modal .loading-state');
		if (st) st.textContent = failedText;
	}
}

function readFileAsBase64(file) {
	return new Promise((resolve, reject) => {
		const reader = new FileReader();
		reader.onload = () => {
			const str = String(reader.result || '').replace(/^data:[^;]+;base64,/, '');
			resolve(str);
		};
		reader.onerror = () => reject(reader.error || new Error('读取文件失败'));
		reader.readAsDataURL(file);
	});
}

/* M1-3：新闻子系统已拆至 js/news.js（SG.news）。本文件加载在其后，news.js
 * 加载期无法引用 IIFE 内部函数，故在任何渲染发生前把基础设施单向注入给它。
 * 缺失注入时新闻交互为空操作（不会抛错），渲染依赖的 SG.news 本体缺失则直接抛错暴露接线问题。 */
SG.news.__wire({
	toast,
	openModal,
	closeModal,
	goTo,
	currentView: () => currentView,
});

/* M1-3 第三步：下载中心已拆至 js/downloads.js（SG.downloads），同样在加载期
 * 无法引用本 IIFE 内部函数，故在任何渲染发生前把基础设施单向注入。
 * navigationCounts 传的是**对象引用**，downloads.js 写入的下载计数会直接反映到
 * 本文件的 updateShell，与拆分前是同一份数据。 */
SG.downloads.__wire({
	toast,
	currentView: () => currentView,
	navigationCounts,
});

/* M1-3 第三步后段：皮肤视图已拆至 js/skins.js（SG.skins）。同样的单向注入。
 * accounts 传的是**取值函数**而不是数组本身：launcher 的 loadAccounts 会整体替换
 * ACCOUNTS 变量（不能传引用），取函数保证 skins 每次拿到的是最新一份。
 * invalidate/goTo 用于"应用皮肤后刷新本页"，与拆分前是同一组调用。 */
SG.skins.__wire({
	toast,
	currentView: () => currentView,
	accounts: () => ACCOUNTS,
	loadAccounts,
	invalidate,
	goTo,
});

/* M1-3 第三步收尾：资源库已拆至 js/content.js（SG.content）。
 * ★ 本视图的注入面与其它视图不同：contentPageGen / contentDebTimer 的**所有权仍在
 *   本文件**（goTo 是唯一写者），所以这里注入的是**访问器**而不是值：
 *     pageGen:      () => contentPageGen      —— content.js 只读，用于代际校验
 *     setDebounce:  (fn, ms) => {...}         —— 挂 debounce；先清旧再挂新，
 *                                                句柄写入本文件的 contentDebTimer，
 *                                                这样 goTo 切走时才能清掉它
 *   STATE / stateLoadFailedLast / selectedInstanceId 同样传取值函数：它们都会被
 *   本文件整体重新赋值，传快照会拿到过期值。 */
SG.content.__wire({
	toast,
	getState: () => STATE,
	loadState,
	selectedInstanceId: () => selectedInstanceId,
	stateLoadFailedLast: () => stateLoadFailedLast,
	pageGen: () => contentPageGen,
	setDebounce: (fn, ms) => {
		if (contentDebTimer !== null) { clearTimeout(contentDebTimer); contentDebTimer = null; }
		contentDebTimer = setTimeout(fn, ms);
	},
	uiIcon,
	uiSearch,
});

/* M1-3 第三步收尾：设置页已拆至 js/settings.js（SG.settings）。
 * settings 没有导航级状态，全部读写都落在 STATE.config 的属性上，所以这里传
 * getState() 取值函数——STATE 是对象引用，视图侧的 `_getState().config.x = v`
 * 与本文件的 `STATE.config.x = v` 作用于同一个对象。
 * addUpdateBindings / refreshSidebarAccount 仍在本文件（更新区未拆），传给它是
 * 为了让设置页在账户操作后仍能刷新侧栏与更新区。 */
SG.settings.__wire({
	toast,
	openModal,
	closeModal,
	invalidate,
	refreshSidebarAccount,
	openAiManage,
	addUpdateBindings,
	loadAccounts,
	loadState,
	getState: () => STATE,
	uiIcon,
});

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
else init();

/* ---------------- 测试钩子（生产路径不引用 SG.__test__） ----------------
 * 暴露内部函数与状态给 node:test 行为测试用。任何测试代码都不应修改
 * SG.__test__ 中的引用，调用方也只能调用下面列出的方法。
 */
window.SG = window.SG || {};
SG.__test__ = {
	// 视图渲染与事件绑定
	renderContent,
	bindContent,
	bindContentInstalls,
	// M1-3：contentCardHtml 是纯函数，已随视图拆到 js/content.js 的 core 面上
	contentCardHtml: SG.content.core.contentCardHtml,
	searchContentOptions,
	loadState,
	// CV-04：下载中心（版本源 / 下载任务两套独立状态，行为测试需要生产入口）
	renderDownloads,
	bindDownloads,
	getDownloadsState() { return SG.downloads.getState(); },   // M1-3：状态在 js/downloads.js
	// CV-05：验证时间判定（"未验证"口径），测试用渲染路径为主，这里供边界断言
	newsValidatedDate() { return SG.news.newsValidatedDate(); },
	// R2-02：设置页（fake-ip 兼容开关测试需要）
	renderSettings,
	bindSettings,
	// 导航（用于竞态测试）
	goTo,
	bindGoTo(nav) { return () => goTo(nav); },
	getNavSeq() { return navSeq; },
	getCurrentView() { return currentView; },
	getState() { return STATE; },
	setState(s) { STATE = s; },
	resetState() { STATE = null; },
	getSelectedInstanceId() { return selectedInstanceId; },
	setSelectedInstanceId(id) { selectedInstanceId = id; },
	// M1-3：content 视图状态已移到 js/content.js；这里全部委托过去。
	getContentSource() { return SG.content.getState().source; },
	setContentSource(v) { SG.content.setSource(v); },
	getContentCat() { return SG.content.getState().cat; },
	setContentCat(v) { SG.content.setCat(v); },
	getContentQuery() { return SG.content.getState().query; },
	setContentQuery(v) { SG.content.setQuery(v); },
	getContentLoadError() { return SG.content.getState().loadError; },
	setContentLoadError(v) { SG.content.setLoadError(v); },
	getContentSearchToken() { return SG.content.getState().searchToken; },
	resetContentSearchToken() { SG.content.resetSearchToken(); },
	getContentPageGen() { return contentPageGen; },
	// Phase 3A 新闻测试钩子（生产路径不引用；新闻子系统已拆至 js/news.js，这里做委托转发）
	loadNews: (...a) => SG.news.loadNews(...a),
	refreshNews: (...a) => SG.news.refreshNews(...a),
	isFrontendAllowedNewsLink: SG.news.isFrontendAllowedNewsLink,
	isFrontendAllowedNewsLinkId: SG.news.isFrontendAllowedNewsLinkId,
	openNewsLinkWithDedup: (...a) => SG.news.openNewsLinkWithDedup(...a),
	bindHomeNewsCards: (...a) => SG.news.bindHomeNewsCards(...a),
	bindHome,
	newsModalAnchorAllowed: n => SG.news.newsModalAnchorAllowed(n),
	renderNews: (...a) => SG.news.render(...a),
	renderHome,
	bindNews: (...a) => SG.news.bind(...a),
	resetNews() { SG.news.resetNews(); },
	getNewsMeta() { return SG.news.getNewsMeta(); },
	getNewsItems() { return SG.news.getNewsItems(); },
	// 纯函数区（无 DOM / 无网络 / 无状态）：一个字符改错就静默出错，必须能直测。
	// 仅在同一 __test__ 对象里增加引用，不改任何运行时行为；生产路径不引用 SG.__test__。
	esc,
	loaderColor,
	firstLetter,
	uiText,
	uiIcon,
	uiPlayTime,
	parseValidatedAt: SG.news.core.parseValidatedAt,
	steveSvg,
	skinAvatar,
	newsStateText() { return SG.news.newsStateText(); },
	newsErrorText() { return SG.news.newsErrorText(); },
	// M2-1④：启动前检查的结构化引导（纯函数 + 一个按 action 跳转的动作分发）。
	// 同样只在 __test__ 上挂引用，生产路径不引用 SG.__test__。
	normPrelaunchIssues,
	runPrelaunchAction,
	doLaunch,
	getLaunchInFlight() { return launchInFlight; },
	// M2-1①：已删除存档（可恢复）区块的渲染与实例锚点选择。
	renderSaveRecoveryList,
	instanceIdForWorlds,
	renderWorlds,
	bindWorlds,
	// LONG-QUALITY-GOAL / G2：六条普通用户流程的行为测试入口。
	// 只导出既有内部函数，不改变任何运行时行为；生产路径不引用 SG.__test__。
	openModal,
	closeModal,
	openRecoveryPanel,
	openModBisect,
	drawBisect,
	drawBisectPending,
	openDiagnoseHistory,
	handleImportFiles,
	initUpdateSection,
	renderUpdateTrust,
	renderUpdateReady,
	renderInstallations,
	openInstanceMenu,
};

/* ---------------- Gate R2 测试导出（仅在测试环境暴露） ----------------
 * 单元测试需命中生产路径（openAiDiagnose、closeModal、mapAiErrorCodeToI18nKey 等），
 * 因此在 IIFE 结束后把关键函数挂到 window.__aiDiagnostics 供 vm 测试访问。
 * 生产环境无副作用：仅挂属性，不改变任何运行时行为。
 */
if (typeof window !== 'undefined' && window.__TEST_DIAG__ === true) {
	window.__aiDiagnostics = {
		closeModal, openModal, openAiDiagnose, renderConfirmPanel,
		goTo,
		mapAiErrorCodeToI18nKey, getActiveAiProviderInfo,
		AI_PRESETS, AI_ENDPOINT_ALLOWLIST_FRONTEND, isFrontendEndpointAllowed,
	};
}

})();
