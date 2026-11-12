/* ================================================================
   松光启动器 · 新闻子系统（M1-3 自 launcher.js 按视图拆出）
   ================================================================
 * 拆分说明（M1-3 第二步，延续 playtime.js 的先例模式）：
 *   - 本文件承载新闻的**全部**前端逻辑：状态机（Phase 3A）、首页区块、
 *     新闻页视图、图片加载、外链二次校验与连点去重。
 *   - 加载顺序在 launcher.js **之前**（见 ui/tests/helpers/ui-script-order.cjs），
 *     因此不能直接引用 launcher.js IIFE 内部的 toast/openModal/goTo 等基础设施；
 *     这些依赖由 launcher.js 在启动时经 `SG.news.__wire({...})` 单向注入，
 *     注入前调用它们是空操作（模块加载期不触发任何渲染）。
 *   - 纯函数（parseValidatedAt / fmtDate / 外链校验）在 SG.news.core 上，
 *     并按 CommonJS 导出，供 node:test 直接断言。
 */
(function (root) {
'use strict';
if (!root || !root.SG) return;
const SG = root.SG;

/* ---------------- 本模块自带的纯辅助 ---------------- */
/* 与 launcher.js 内同名实现保持逐字符一致；拆分允许模块内自带 1 行纯函数，
 * 但不得复制任何带状态/带 DOM 副作用的逻辑（那些只能经 __wire 注入）。 */
const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const t = (k, vars) => (SG.t ? SG.t(k, vars) : k);
const uiText = (zh, en) => SG.locale === 'en' ? en : zh;

function fmtDate(v) {
	if (!v) return '';
	const d = new Date(v);
	// CV-05：无效日期与零值一律返回空串，绝不把原始字符串塞进日期字段
	// （旧实现 `return String(v)` 会把主机名/解析错误当成"日期"显示出来）。
	if (isNaN(d.getTime())) return '';
	if (d.getUTCFullYear() <= 1) return '';
	const p = n => String(n).padStart(2, '0');
	return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/* ---------------- launcher.js 基础设施（运行时经 __wire 注入） ---------------- */
let _toast = () => {};
let _openModal = () => {};
let _closeModal = () => {};
let _goTo = () => {};
let _currentView = () => null;

function wire(deps) {
	if (!deps) return;
	if (typeof deps.toast === 'function') _toast = deps.toast;
	if (typeof deps.openModal === 'function') _openModal = deps.openModal;
	if (typeof deps.closeModal === 'function') _closeModal = deps.closeModal;
	if (typeof deps.goTo === 'function') _goTo = deps.goTo;
	if (typeof deps.currentView === 'function') _currentView = deps.currentView;
}

/* ---------------- News 新闻（Phase 3A 状态机） ----------------
 * 生产路径不再有静态 fallback 假新闻。
 * 状态：live/revalidated/memory/cached/empty/error（由后端 /api/news meta 提供）。
 * 请求代际（NEWS_REQ_GEN）保证迟到的旧响应不会覆盖新响应；
 * NEWS_INFLIGHT 计数实现手动刷新防连点；自动加载带 15 秒错误退避。
 */
let LIVE_NEWS = null;   // 最近一次可展示的官方新闻列表
let NEWS_META = null;   // { source, state, validatedAt, fetchedAt, isStale, canRetry, errorKind, error, _localCheckedAt }
let NEWS_INFLIGHT = 0;
let NEWS_REQ_GEN = 0;
let NEWS_LAST_ATTEMPT = 0;
const NEWS_MEMORY_FRESH_MS = 30 * 60 * 1000;   // 与后端 30 分钟内存新鲜期一致
const NEWS_ERROR_BACKOFF_MS = 15 * 1000;

const NEWS_STATE_KEYS = {
	live: 'news.stateLive',
	revalidated: 'news.stateRevalidated',
	memory: 'news.stateMemory',
	cached: 'news.stateCached',
	empty: 'news.stateEmpty',
	error: 'news.stateError',
};

const NEWS_ERROR_KIND_KEYS = {
	rate_limited: 'news.errRateLimited',
	timeout: 'news.errTimeout',
	network: 'news.errNetwork',
	parse: 'news.errParse',
	http_server: 'news.errHttp',
	http_client: 'news.errHttp',
	empty_response: 'news.errEmpty',
	internal: 'news.errInternal',
};

function isNewsFresh() {
	return !!NEWS_META
		&& (NEWS_META.state === 'live' || NEWS_META.state === 'revalidated' || NEWS_META.state === 'memory')
		&& Date.now() - (NEWS_META._localCheckedAt || 0) < NEWS_MEMORY_FRESH_MS;
}

async function loadNews(options) {
	options = options || {};
	const force = !!options.force;
	if (!force && isNewsFresh()) return { ok: true, skipped: true };
	if (!force && NEWS_META && NEWS_META.state !== 'live' && NEWS_META.state !== 'revalidated' && NEWS_META.state !== 'memory'
		&& Date.now() - NEWS_LAST_ATTEMPT < NEWS_ERROR_BACKOFF_MS) {
		return { ok: true, skipped: true };
	}
	if (force && NEWS_INFLIGHT > 0) return { ok: false, busy: true };   // 手动刷新防连点
	const gen = ++NEWS_REQ_GEN;
	NEWS_INFLIGHT++;
	NEWS_LAST_ATTEMPT = Date.now();
	try {
		const result = await SG.api.getNews(force ? { refresh: true } : undefined);
		if (gen !== NEWS_REQ_GEN) return { ok: false, superseded: true };   // 迟到的旧响应丢弃
		applyNewsResponse(result);
		return { ok: true };
	} catch (e) {
		if (gen !== NEWS_REQ_GEN) return { ok: false, superseded: true };
		NEWS_META = Object.assign({}, NEWS_META || {}, {
			state: 'error', isStale: true, canRetry: true,
			errorKind: 'network', error: (e && e.message) || String(e),
			_localCheckedAt: Date.now(),
		});
		return { ok: false };
	} finally {
		NEWS_INFLIGHT--;
	}
}

async function refreshNews() {
	const result = await loadNews({ force: true });
	return result;
}

function applyNewsResponse(result) {
	const meta = (result && result.meta) || {};
	const items = (result && result.items) || null;
	if (items && items.length) {
		LIVE_NEWS = items;
	} else if (meta.state === 'empty' || meta.state === 'error') {
		LIVE_NEWS = null;   // 空态/错误态绝不回退旧列表之外的假数据
	}
	NEWS_META = {
		source: meta.source || '',
		state: meta.state || 'error',
		validatedAt: meta.validatedAt || null,
		fetchedAt: meta.fetchedAt || null,
		isStale: !!meta.isStale,
		canRetry: !!meta.canRetry,
		errorKind: meta.errorKind || null,
		error: meta.error || null,
		_localCheckedAt: Date.now(),
	};
}

function newsStateText() {
	if (!NEWS_META) return t('news.loading');
	const key = NEWS_STATE_KEYS[NEWS_META.state];
	return key ? t(key) : t('news.stateError');
}

function newsErrorText() {
	if (!NEWS_META || !NEWS_META.errorKind) return '';
	const key = NEWS_ERROR_KIND_KEYS[NEWS_META.errorKind];
	return key ? t(key) : t('news.errInternal');
}

/* CV-05（历史修复）：验证时间的「未验证」判定。
 * 原始缺陷（RED 证据 docs/current-version-audit-2026-10-13/independent-ui-red.json CV-05）：
 *   后端首次失败时 meta.validatedAt 是 Go 零值 time.Time（0001-01-01T00:00:00Z），
 *   旧实现 `if (!validatedAt) return 未知时间` 认为该字符串为真值，直接交给 fmtDate，
 *   界面于是把「零值」渲染成日期 "1-01-01" —— 既不是错误也不是未验证，属于伪造信息。
 * 判定口径（保持 meta.validatedAt 契约不变，只改前端呈现）：
 *   null / undefined / 空串 / 无法解析的原串 / 0001-01-01（含任意时区偏移）一律 = 未验证。
 *   零值在任意偏移下都落在 UTC 0000-12-31 ~ 0001-01-01 之间，故统一按 UTC 年份 <= 1 判定，
 *   这样 +08:00（退到 0000-12-31）与 -08:00（仍是 0001-01-01）两种变体都能覆盖。 */
function parseValidatedAt(v) {
	if (v == null) return null;
	if (typeof v === 'number') {
		const n = new Date(v);
		return (isNaN(n.getTime()) || n.getUTCFullYear() <= 1) ? null : n;
	}
	const s = String(v).trim();
	if (!s) return null;
	const d = new Date(s);
	if (isNaN(d.getTime())) return null;        // 无效原串绝不进入日期字段
	if (d.getUTCFullYear() <= 1) return null;   // Go 零值 time（含时区变体）
	return d;
}

function newsValidatedDate() { return parseValidatedAt(NEWS_META && NEWS_META.validatedAt); }

function newsValidatedText() {
	const d = newsValidatedDate();
	return d ? fmtDate(d) : t('news.unknownTime');
}

function newsList() { return LIVE_NEWS || []; }   // 生产路径无静态 fallback

/* R-03：前端第二层外链校验（纵深防御；后端 NewsEndpointPolicy 为权威）。
 * 仅 HTTPS、无 userinfo、无非默认端口、精确官方主机、拒绝 IP literal/末尾点/IDN。 */
const NEWS_LINK_ALLOWED_HOSTS = ['www.minecraft.net', 'minecraft.net', 'aka.ms', 'www.youtube.com'];

function isFrontendAllowedNewsLink(url) {
	if (!url || typeof url !== 'string') return false;
	let u = null;
	try { u = new URL(url); } catch (e) { return false; }
	if (u.protocol !== 'https:') return false;
	if (u.username || u.password) return false;
	if (u.port) return false;
	// Node/浏览器 URL 会规范化尾点；从原始 href 提取 authority 检测尾点与 userinfo
	const authority = u.href.slice('https://'.length).split(/[/?#]/)[0] || '';
	if (authority.includes('@')) return false;
	if (authority.endsWith('.') || authority.includes('..')) return false;
	let host = (u.hostname || '').toLowerCase();
	if (host.endsWith('.')) host = host.slice(0, -1);
	if (!host || host.endsWith('.')) return false;
	if (!/^[a-z0-9.-]+$/.test(host)) return false;
	if (host.includes('xn--')) return false;
	if (/^\d{1,3}(\.\d{1,3}){3}$/.test(host)) return false;
	return NEWS_LINK_ALLOWED_HOSTS.indexOf(host) !== -1;
}

/* R2-03：linkId 形态校验（客户端二次门）。
 * 服务端是权威；此函数只用于防点击传空 / 非法 linkId 误发请求。
 * 允许的字符：URL-safe Base64（A-Z a-z 0-9 - _），16-64 字符。 */
function isFrontendAllowedNewsLinkId(id) {
	if (!id || typeof id !== 'string') return false;
	if (id.length < 16 || id.length > 64) return false;
	return /^[A-Za-z0-9_-]+$/.test(id);
}

/* R2-03：连点去重 + 失败重试 + i18n 错误提示。
 * pending 中按钮被禁用；失败时显示 news.openFailed 文案并允许再次点击。 */
const __newsOpenInFlight = new Set();

function openNewsLinkWithDedup(linkId, triggerEl) {
	if (!isFrontendAllowedNewsLinkId(linkId)) return;
	if (__newsOpenInFlight.has(linkId)) return;
	__newsOpenInFlight.add(linkId);
	if (triggerEl && typeof triggerEl.disabled === 'boolean') triggerEl.disabled = true;

	// modal 内错误展示位（存在则显示）
	const errEl = document.getElementById('newsOpenError');
	const showError = (msg) => { if (errEl) { errEl.textContent = msg; errEl.style.display = 'block'; } };
	const clearError = () => { if (errEl) { errEl.textContent = ''; errEl.style.display = 'none'; } };
	clearError();

	SG.api.openNewsLink(linkId).then(() => {
		// 成功：不显示错误；toast 由调用方按需触发
	}).catch((e) => {
		// 失败：显示 i18n 文案
		showError(t('news.openFailed'));
	}).finally(() => {
		// settle 统一恢复按钮 + 清除 in-flight（Codex live 复现回归：成功分支不再永久禁用）
		__newsOpenInFlight.delete(linkId);
		if (triggerEl && typeof triggerEl.disabled === 'boolean') triggerEl.disabled = false;
	});
}

function newsArtInner(n, cls) {
	// 图片不直接 <img src>（无法携带会话令牌，真实 WebView 实测 401）；
	// 由 bindNewsArt 经 SG.api.getNewsImage（fetch+blob，带令牌）加载，失败显示本地占位。
	return `<div class="${cls}" data-news-art-id="${esc(n.imageId || '')}"></div>`;
}

function bindNewsArt(root) {
	if (!root) return;
	root.querySelectorAll('[data-news-art-id]').forEach(el => {
		const id = el.dataset.newsArtId;
		if (!id || el._artLoaded) return;
		el._artLoaded = true;
		const img = document.createElement('img');
		img.className = 'news-art-img';
		img.alt = '';
		img.setAttribute('data-news-art-img', '1');
		img.addEventListener('error', () => {
			// 图片失败只显示本地占位，绝不回退远程 URL
			el.classList.add('news-art-failed');
			img.style.display = 'none';
		});
		SG.api.getNewsImage(id)
			.then(url => { img.src = url; el.appendChild(img); })
			.catch(() => el.classList.add('news-art-failed'));
	});
}

function newsStatusHtml() {
	if (!NEWS_META) {
		return `<div class="news-meta" data-news-meta="loading">${esc(t('news.loading'))}</div>`;
	}
	const stateAttr = esc(NEWS_META.state);
	// CV-05：未验证（null/空/无效/0001-01-01）时不显示任何日期，只显示"尚未验证"；
	// 有效验证时间才写进日期字段。data-news-verified 供验收脚本与测试区分两种终态。
	const validatedDate = newsValidatedDate();
	const verified = validatedDate !== null;
	const timeText = verified ? newsValidatedText() : '';
	if (NEWS_META.state === 'cached') {
		const line = verified
			? esc(t('news.cachedBanner', { time: timeText }))
			: esc(t('news.cachedBannerNoTime'));
		return `<div class="news-banner news-banner-warn" data-news-banner="cached" data-news-state="cached" data-news-verified="${verified ? '1' : '0'}">${line}</div>`;
	}
	const line = verified
		? `${esc(newsStateText())} · ${esc(t('news.lastValidated', { time: timeText }))}`
		: `${esc(newsStateText())} · ${esc(t('news.unverified'))}`;
	return `<div class="news-meta" data-news-state="${stateAttr}" data-news-verified="${verified ? '1' : '0'}">${line}</div>`;
}

function newsEmptyOrErrorHtml() {
	const state = NEWS_META ? NEWS_META.state : 'loading';
	if (state === 'empty') {
		return `<div class="empty-state" data-news-state="empty"><div class="es-icon">📰</div>${esc(t('news.empty'))}</div>
		<div class="news-retry-row"><button class="modal-btn ghost" id="newsRetry" data-news-retry="1">${esc(t('news.retry'))}</button></div>`;
	}
	if (state === 'error') {
		return `<div class="empty-state news-error-state" data-news-state="error"><div class="es-icon">⚠️</div>${esc(t('news.loadFailed'))}</div>
		<div class="news-error-detail">${esc(newsErrorText())}</div>
		<div class="news-retry-row"><button class="modal-btn ghost" id="newsRetry" data-news-retry="1">${esc(t('news.retry'))}</button></div>`;
	}
	return `<div class="news-skeleton" data-news-skeleton="1">
		<div class="news-skeleton-card"></div><div class="news-skeleton-card"></div><div class="news-skeleton-card"></div>
	</div>`;
}

function homeNewsInnerHtml() {
	if (LIVE_NEWS && LIVE_NEWS.length) {
		const cards = newsList().slice(0, 4).map(n => {
			// R2-03：只渲染 linkId，不渲染原始 link；data-link-id 用于点击后 SG.api.openNewsLink(linkId)
			const linkIdAttr = (n.linkOk && n.linkId) ? ` data-link-id="${esc(n.linkId)}"` : '';
			return `<button class="news-card" data-news="${esc(n.title)}"${linkIdAttr} data-link-ok="${n.linkOk ? '1' : '0'}">
			${newsArtInner(n, 'news-card-art')}
			<span class="news-card-body"><span class="news-card-title">${esc(n.title)}</span><span class="news-card-meta"><span class="news-cat">${esc(n.category || '')}</span> · <span class="news-date">${esc(fmtDate(n.date))}</span></span></span>
		</button>`;
		}).join('');
		if (NEWS_META && (NEWS_META.state === 'cached')) {
			// CV-05：缓存态同样遵循"验证时间无效就不显示日期"的口径。
			const cachedDate = newsValidatedDate();
			const cachedLine = cachedDate
				? esc(t('news.cachedBanner', { time: fmtDate(cachedDate) }))
				: esc(t('news.cachedBannerNoTime'));
			return `${cards}<div class="news-banner news-banner-warn" data-news-banner="cached" data-news-verified="${cachedDate ? '1' : '0'}">${cachedLine}</div>`;
		}
		return cards;
	}
	if (NEWS_META && (NEWS_META.state === 'empty' || NEWS_META.state === 'error')) {
		return `<div class="home-news-error">${esc(NEWS_META.state === 'error' ? t('news.loadFailed') : t('news.empty'))}</div>
		<div class="news-retry-row"><button class="modal-btn ghost" id="newsRetry" data-news-retry="1">${esc(t('news.retry'))}</button></div>`;
	}
	return `<div class="news-skeleton" data-news-skeleton="1">
		<div class="news-skeleton-card"></div><div class="news-skeleton-card"></div><div class="news-skeleton-card"></div><div class="news-skeleton-card"></div>
	</div>`;
}

async function renderNews() {
	// Phase 3A：先渲染当前状态/骨架，数据到位后就地刷新（不阻塞整页）
	loadNews().then(() => refreshNewsSectionInView('news'));
	return newsPageHtml();
}

function newsPageHtml() {
	const items = newsList();
	return `
	<div class="content-view news-view">
		<div class="page-head">
			<div>
				<div class="page-title">${esc(t('news.title'))}</div>
				<div class="page-sub">${esc(t('news.subtitle'))}</div>
			</div>
			<button class="modal-btn ghost" id="newsRefresh">${esc(t('news.refresh'))}</button>
		</div>
		<h2 class="content-library-title news-library-title">${uiText('官方动态','Official news')}</h2><div id="newsViewContent">
			${newsStatusHtml()}
			<div class="news-full-list">${items.length ? items.map((n, idx) => newsFullCardHtml(n, idx)).join('') : newsEmptyOrErrorHtml()}</div>
		</div>
	</div>`;
}

/* 数据异步到位后，就地刷新当前视图中的新闻区块（页面已切走时不动 DOM） */
function refreshNewsSectionInView(viewName) {
	if (_currentView() !== viewName) return;   // 切页保护
	const view = document.getElementById('view-' + viewName);
	if (!view || !view.isConnected) return;
	if (viewName === 'home') {
		const section = view.querySelector('#homeNewsSection');
		if (!section) return;
		section.innerHTML = homeNewsInnerHtml();
		bindHomeNewsCards(section);
		bindNewsArt(section);
	} else if (viewName === 'news') {
		const section = view.querySelector('#newsViewContent');
		if (!section) return;
		section.innerHTML = `${newsStatusHtml()}<div class="news-full-list">${newsList().length ? newsList().map((n, idx) => newsFullCardHtml(n, idx)).join('') : newsEmptyOrErrorHtml()}</div>`;
		bindNewsListEvents(section);
		bindNewsArt(section);
	}
}

function newsFullCardHtml(n, idx) {
	return `
	<article class="news-full-card" data-idx="${idx}" data-link-ok="${n.linkOk ? '1' : '0'}" data-news-title="${esc(n.title)}"${(n.linkOk && n.linkId) ? ` data-link-id="${esc(n.linkId)}"` : ''}>
		${newsArtInner(n, 'news-full-art')}
		<div class="news-full-body">
			<div class="news-meta-row"><span class="news-cat">${esc(n.category || '')}</span><span class="news-date">${esc(fmtDate(n.date))}</span></div>
			<h3 class="news-full-title">${esc(n.title)}</h3>
			<p class="news-full-desc">${esc(n.text || '')}</p>
			<button class="ui-button news-details">${uiText('查看详情','View details')}</button>
		</div>
	</article>`;
}

/* 新闻列表卡片点击：只有通过后端白名单的官方链接才可打开；详情弹窗内不渲染不可信外链 */
function bindNewsListEvents(scope) {
	scope.querySelectorAll('.news-full-card').forEach(c => {
		if (c._newsCardBound) return;
		c._newsCardBound = true;
		c.addEventListener('click', () => {
			const n = newsList()[+c.dataset.idx];
			if (!n) return;
			const linkOk = n.linkOk === true || c.dataset.linkOk === '1';
			const linkId = (n.linkId && typeof n.linkId === 'string') ? n.linkId : (c.dataset.linkId || '');
			// R2-03：详情弹窗使用 button[data-news-open] 调用 SG.api.openNewsLink(linkId)；
			// 不再渲染 <a target="_blank"> 外链。
			const openArticleBtn = (linkOk && linkId)
				? `<button class="modal-btn primary" data-news-open="1" data-link-id="${esc(linkId)}" id="newsOpenArticle">${esc(t('news.openArticle'))}</button>`
				: '';
			_openModal(`
				<div class="modal-title">${esc(n.title)}</div>
				<div class="page-sub">${esc(n.category || '')} · ${esc(fmtDate(n.date))}</div>
				<p style="margin:12px 0;font-size:13px;line-height:1.7;color:var(--text,#e8ecf4)">${esc(n.text || '')}</p>
				${openArticleBtn}
				<div id="newsOpenError" class="news-open-error" style="display:none;margin-top:8px;color:var(--danger,#e74c3c);font-size:12px"></div>
				<div class="modal-actions"><button class="modal-btn" id="newsClose">${esc(t('common.close'))}</button></div>
			`);
			document.getElementById('newsClose').onclick = _closeModal;
			// R2-03：详情弹窗"打开文章"按钮 → openNewsLinkWithDedup
			const openBtn = document.getElementById('newsOpenArticle');
			if (openBtn) {
				openBtn.onclick = () => openNewsLinkWithDedup(openBtn.dataset.linkId || linkId, openBtn);
			}
		});
	});
}

function bindHomeNewsCards(scope) {
	scope.querySelectorAll('.news-card').forEach(c => {
		if (c._newsCardBound) return;
		c._newsCardBound = true;
		c.addEventListener('click', () => {
			// R2-03：只传 linkId，调用 SG.api.openNewsLink(linkId)；移除 window.open / target=_blank。
			// 二次校验：后端 linkOk===1 + 非空 linkId + 前端第二层精确主机校验全部通过才打开。
			const linkId = c.dataset.linkId || '';
			const linkOk = c.dataset.linkOk === '1';
			if (linkOk && linkId && isFrontendAllowedNewsLinkId(linkId)) {
				openNewsLinkWithDedup(linkId, c);
			} else {
				_goTo('news');
			}
		});
	});
}

function bindNews(el) {
	// 刷新按钮（防连点 + 事件去重）
	const refreshBtn = el.querySelector('#newsRefresh');
	if (refreshBtn && !refreshBtn._newsBound) {
		refreshBtn._newsBound = true;
		refreshBtn.addEventListener('click', async event => {
			const button = event.currentTarget;
			if (button.disabled) return;
			button.disabled = true;
			button.textContent = t('news.refreshing');
			const result = await refreshNews();
			button.disabled = false;
			button.textContent = t('news.refresh');
			if (result && result.ok) {
				const view = document.getElementById('view-news');
				if (view && _currentView() === 'news') { view.innerHTML = await renderNews(); bindNews(view); }
			} else if (result && !result.busy) {
				_toast(t('news.refreshFailed'));
			}
		});
	}
	// 重试按钮（错误/空态恢复入口）
	bindNewsRetry(el);
	bindNewsListEvents(el);
	bindNewsArt(el);
}

function bindNewsRetry(el) {
	el.querySelectorAll('[data-news-retry]').forEach(btn => {
		if (btn._newsBound) return;
		btn._newsBound = true;
		btn.addEventListener('click', async event => {
			const button = event.currentTarget;
			button.disabled = true;
			const result = await refreshNews();
			button.disabled = false;
			if (result && result.ok) {
				refreshNewsSectionInView('news');
				refreshNewsSectionInView('home');
			} else if (result && !result.busy && !result.superseded) {
				_toast(t('news.refreshFailed'));
			}
		});
	});
}

/* ---------------- 对外表面（launcher.js / 测试经 SG.news 调用） ---------------- */
SG.news = {
	render: renderNews,
	bind: bindNews,
	loadNews,
	refreshNews,
	resetNews() { LIVE_NEWS = null; NEWS_META = null; NEWS_INFLIGHT = 0; NEWS_REQ_GEN = 0; NEWS_LAST_ATTEMPT = 0; },
	getNewsMeta() { return NEWS_META; },
	getNewsItems() { return LIVE_NEWS; },
	// 首页区块（launcher 的 redesignedHome / bindHome 调用）
	homeNewsInnerHtml,
	bindHomeNewsCards,
	bindArt: bindNewsArt,
	bindRetry: bindNewsRetry,
	refreshSection: refreshNewsSectionInView,
	// 外链二次校验与连点去重（__test__ 委托到这里）
	isFrontendAllowedNewsLink,
	isFrontendAllowedNewsLinkId,
	openNewsLinkWithDedup,
	newsModalAnchorAllowed(n) { return !!n && n.linkOk === true && isFrontendAllowedNewsLink(n.link); },
	// 文案辅助（__test__ 委托到这里）
	newsStateText,
	newsErrorText,
	newsValidatedDate,
	// 纯函数区（无 DOM / 无网络 / 无状态）：供 node:test 直接断言
	core: { parseValidatedAt, fmtDate, isFrontendAllowedNewsLink, isFrontendAllowedNewsLinkId },
	// launcher.js 启动时注入基础设施（toast/modal/goTo/currentView），单向、不可覆盖为非函数
	__wire: wire,
};

if (typeof module === 'object' && module.exports) module.exports = SG.news.core;
})(typeof window === 'undefined' ? null : window);
