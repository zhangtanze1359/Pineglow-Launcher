/* ================================================================
   松光启动器 · 下载中心（M1-3 第三步，自 launcher.js 按视图拆出）
   ================================================================
 * 拆分说明（延续 playtime.js / news.js 的先例模式）：
 *   - 本文件承载下载中心的**全部**前端逻辑：版本源与下载任务两套独立状态、
 *     请求代际、重试防连发、筛选保留、错误块与成功空态的区分。
 *   - 加载顺序在 launcher.js **之前**（见 ui/tests/helpers/ui-script-order.cjs），
 *     因此不能直接引用 launcher.js IIFE 内部的 esc/uiIcon/toast/currentView
 *     与 navigationCounts；这些由 launcher.js 在启动时经 `SG.downloads.__wire({...})`
 *     单向注入，注入前调用它们是空操作（模块加载期不触发任何渲染）。
 *   - 与 news.js 相同：模块自带的纯辅助（esc / uiIcon）与 launcher.js 内同名实现
 *     逐字符一致；任何带全局状态或 DOM 副作用的逻辑只能经 __wire 注入。
 */

(function (root) {
'use strict';
if (!root || !root.SG) return;
const SG = root.SG;

/* ---------------- 本模块自带的纯辅助 ---------------- */
const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const t = (k, vars) => (SG.t ? SG.t(k, vars) : k);

/* uiIcon 的图标表与 launcher.js 内实现保持逐字符一致（只取本模块用到的 download）。 */
const UI_ICON_PATHS = { download: '<path d="M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4"/>' };
function uiIcon(name) {
	return `<svg viewBox="0 0 24 24" aria-hidden="true">${UI_ICON_PATHS[name] || UI_ICON_PATHS.download}</svg>`;
}

/* ---------------- launcher.js 基础设施（运行时经 __wire 注入） ---------------- */
let _toast = () => {};
let _currentView = () => null;
let _navigationCounts = null;

function wire(deps) {
	if (!deps) return;
	if (typeof deps.toast === 'function') _toast = deps.toast;
	if (typeof deps.currentView === 'function') _currentView = deps.currentView;
	// navigationCounts 是 launcher.js 的模块级对象（同一引用，写入即生效）
	if (deps.navigationCounts && typeof deps.navigationCounts === 'object') _navigationCounts = deps.navigationCounts;
}

/* CV-04（本次修复）：下载中心的「版本源」与「下载任务」是**两个互相独立的状态**。
 * 原始缺陷（RED 证据 docs/current-version-audit-2026-10-13/independent-ui-red.json CV-04）：
 *   `catch (e) { versions = []; }` 把"请求失败"降级成"成功空列表"，外部源不可用时界面显示
 *   "暂无可下载版本" —— 把失败伪造成事实，用户既看不到错误也没有真实重试入口。
 * 口径（与 Cloud 状态矩阵 normal/loading/error/empty 一致）：
 *   versions.status: 'loading' | 'ready' | 'error'
 *     ready + 数据        → 版本列表
 *     ready + []          → 真正的**成功空态**（dl.noVersions）
 *     ready + 过滤后为空  → dl.noVersionsForFilter（有数据，只是当前筛选没命中）
 *     error               → .download-source-error 错误块 + 真实重试（绝不显示空态）
 *   tasks.status: 'loading' | 'ready' | 'error'
 *     error → 显式任务错误块，**不能**假装"暂无下载任务"。
 *   两者互不影响：版本源失败时，已有任务来自独立数据源，照常显示。
 *   dlReqGen 请求代际：迟到的旧响应（成功或失败）都不得覆盖较新的状态或已切走的页面。 */
let dlVersionFilter = 'release';
let dlPollTimer = null;
let dlVersions = { status: 'loading', error: null, items: [] };
let dlTasks = { status: 'loading', error: null, items: [] };
let dlReqGen = 0;
let dlReloadInFlight = false;   // 重试防连发：按钮 disabled + 逻辑门双保险

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
		<div class="dl-task-icon">${uiIcon('download')}</div>
		<div class="dl-task-body">
			<div class="dl-task-top">
				<span class="dl-task-name">${esc(t.name)}</span>
				<span class="dl-task-status">${esc(t.statusLabel || '')} ${t.progress ? (t.progress + '%') : ''}</span>
			</div>
			<div class="dl-progress"><div class="dl-progress-fill" style="width:${t.progress || 0}%"></div></div>
		</div>
	</div>`;
}

/* CV-04 / CV-06：版本源错误块。通俗主说明 + 可展开技术详情 + 真实重试。
 * 设计来源：pineglow-state-components.css 的 `.download-source-error` / `.download-retry-btn` /
 * `.tech-details` / `.tech-body`（integration-notes.md §4 第 1 条）。 */
function dlSourceErrorHtml(errText) {
	const tech = String(errText == null ? '' : errText);
	return `<div class="download-source-error" data-error="1" role="alert">
		<div class="state-icon state-icon--danger" aria-hidden="true">!</div>
		<div class="state-main">
			<div class="state-title">${esc(t('dl.sourceErrorTitle'))}</div>
			<p class="state-desc">${esc(t('dl.sourceErrorDesc'))}</p>
			<div class="state-actions"><button class="download-retry-btn" type="button" data-dl-retry="1">${esc(t('dl.retry'))}</button></div>
			${tech ? `<details class="tech-details"><summary>${esc(t('dl.techDetails'))}</summary><pre class="tech-body">${esc(tech)}</pre></details>` : ''}
		</div>
	</div>`;
}

/* CV-04：下载任务错误块。任务接口失败必须显式报错，绝不回落成"暂无下载任务"。 */
function dlTasksErrorHtml(errText) {
	const tech = String(errText == null ? '' : errText);
	return `<div class="download-source-error download-tasks-error" data-tasks-error="1" role="alert">
		<div class="state-icon state-icon--danger" aria-hidden="true">!</div>
		<div class="state-main">
			<div class="state-title">${esc(t('dl.tasksErrorTitle'))}</div>
			<p class="state-desc">${esc(t('dl.tasksErrorDesc'))}</p>
			<div class="state-actions"><button class="download-retry-btn" type="button" data-dl-tasks-retry="1">${esc(t('dl.retry'))}</button></div>
			${tech ? `<details class="tech-details"><summary>${esc(t('dl.techDetails'))}</summary><pre class="tech-body">${esc(tech)}</pre></details>` : ''}
		</div>
	</div>`;
}

async function renderDownloads() {
	const gen = ++dlReqGen;
	// 两个请求并发且彼此独立：任一失败都不影响另一个的结果落地（版本失败仍保留任务）。
	const versionsPromise = Promise.resolve()
		.then(() => SG.api.getRemoteVersions({ throwOnRemoteError: true }))
		.then(items => ({ ok: true, items: Array.isArray(items) ? items : [] }))
		.catch(e => ({ ok: false, error: (e && e.message) ? e.message : String(e), items: e && Array.isArray(e.localVersions) ? e.localVersions : [] }));
	const tasksPromise = Promise.resolve()
		.then(() => SG.api.getDownloads())
		.then(items => ({ ok: true, items: Array.isArray(items) ? items : [] }))
		.catch(e => ({ ok: false, error: (e && e.message) ? e.message : String(e) }));
	const results = await Promise.all([versionsPromise, tasksPromise]);
	// 关键：代际校验。迟到的旧请求（无论成功还是失败）都不得覆盖较新的状态。
	if (gen !== dlReqGen) return '';
	const vRes = results[0], tRes = results[1];
	dlVersions = vRes.ok
		? { status: 'ready', error: null, items: vRes.items }
		: { status: 'error', error: vRes.error, items: vRes.items };
	dlTasks = tRes.ok
		? { status: 'ready', error: null, items: tRes.items }
		: { status: 'error', error: tRes.error, items: [] };
	return downloadsPageHtml();
}

/* 纯呈现层：只读 dlVersions / dlTasks / dlVersionFilter，不发请求。
 * 这样"失败"与"成功空态"是同一次状态判定的两个分支，不可能再退化成一个。 */
function downloadsPageHtml() {
	const versions = dlVersions.items;
	const tasks = dlTasks.items;
	if (dlTasks.status === 'ready' && _navigationCounts) _navigationCounts.downloads = tasks.filter(x => x.status === 'queued' || x.status === 'downloading').length;
	const filtered = versions.filter(v => dlVersionFilter === 'all' ? true : v.type === dlVersionFilter);
	let vlist;
	if (dlVersions.status === 'error') {
		vlist = dlSourceErrorHtml(dlVersions.error) + filtered.slice(0, 60).map(versionRowHtml).join('');
	} else if (dlVersions.status === 'loading') {
		vlist = `<div class="loading-state">${esc(t('common.loading'))}</div>`;
	} else if (filtered.length) {
		vlist = filtered.slice(0, 60).map(versionRowHtml).join('');
	} else {
		// 成功空态：区分"源里一个都没有"与"当前筛选没命中"。
		vlist = versions.length
			? `<div class="empty-state" data-empty="filter">${esc(t('dl.noVersionsForFilter'))}</div>`
			: `<div class="empty-state" data-empty="source">${esc(t('dl.noVersions'))}</div>`;
	}
	let tlist;
	if (dlTasks.status === 'error') {
		tlist = dlTasksErrorHtml(dlTasks.error);
	} else if (dlTasks.status === 'loading') {
		tlist = `<div class="loading-state">${esc(t('common.loading'))}</div>`;
	} else if (tasks.length) {
		tlist = tasks.map(dlTaskHtml).join('');
	} else {
		tlist = `<div class="empty-state" style="padding:32px" data-empty="tasks"><div class="es-icon">📥</div>${esc(t('dl.noTasks'))}</div>`;
	}
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
				<div class="list-grid download-tasks" id="dlTasks">${tlist}</div>
			</div>
		</div>
	</div>`;
}

/* CV-04：重载下载页（筛选切换 / 下载排队 / 重试都走这里）。
 * - 防连发：dlReloadInFlight 逻辑门 + 触发按钮 disabled，进行中的重复点击不产生并发请求；
 * - 页面已切走（currentView !== 'downloads'）时不写 DOM，旧请求结束不会覆盖较新状态；
 * - 重渲染后错误块自然消失（恢复），dlVersionFilter 是模块级状态 → 版本筛选被保留。 */
async function reloadDownloadsView(el, triggerBtn) {
	if (dlReloadInFlight) return false;
	dlReloadInFlight = true;
	if (triggerBtn && typeof triggerBtn === 'object') triggerBtn.disabled = true;
	let rendered = false;
	try {
		const html = await renderDownloads();
		if (!html) return false;                        // 代际已过期：更新的请求已接管
		if (_currentView() !== 'downloads') return false;  // 页面已切走：绝不写 DOM
		const view = document.getElementById('view-downloads') || el;
		if (!view) return false;
		view.innerHTML = html;
		bindDownloads(view);
		rendered = true;
		return true;
	} finally {
		dlReloadInFlight = false;
		// 没有重渲染（页面已切走 / 代际过期）时把按钮恢复可用。
		// 否则用户切走再回来会看到一个永远禁用的重试按钮（DOM 没被替换，disabled 就留在原地）。
		if (!rendered && triggerBtn && typeof triggerBtn === 'object') triggerBtn.disabled = false;
	}
}

function bindDownloads(el) {
	el.querySelectorAll('[data-vfilter]').forEach(b => b.addEventListener('click', async () => {
		dlVersionFilter = b.dataset.vfilter;   // 筛选是模块级状态：重试/重渲染后保持
		await reloadDownloadsView(el);
	}));
	el.querySelectorAll('[data-dlver]').forEach(b => b.addEventListener('click', async () => {
		const id = b.dataset.dlver;
		const original = b.textContent;
		// 与 Play 按钮相同的形变：先固定当前宽度，再收缩为圆形加载
		const full = b.offsetWidth;
		b.style.width = full + 'px';
		void b.offsetWidth;
		b.classList.add('is-queuing');
		b.style.width = '28px';
		b.setAttribute('aria-label', t('dl.queuing'));
		// 动效完整播完（约 0.9 秒）后再刷新列表，即使请求很快返回也不截断动画
		const minWait = new Promise(res => setTimeout(res, 900));
		try {
			await SG.api.downloadVersion(id);
			await minWait;
			_toast(t('dl.queued', { id }));
			await reloadDownloadsView(el);
		} catch (e) {
			await minWait;
			// 失败时展开回原按钮，避免一直停在加载状态
			b.classList.remove('is-queuing');
			b.removeAttribute('aria-label');
			b.style.width = full + 'px';
			setTimeout(() => { b.style.width = ''; }, 900);
			b.textContent = original;
			_toast(t('dl.downloadFailed') + (e.message || t('common.unknownError')));
		}
	}));
	// CV-04：真实的"重试"入口（版本源与下载任务各自独立）。
	el.querySelectorAll('[data-dl-retry]').forEach(b => b.addEventListener('click', () => { reloadDownloadsView(el, b); }));
	el.querySelectorAll('[data-dl-tasks-retry]').forEach(b => b.addEventListener('click', () => { reloadDownloadsView(el, b); }));
}

/* ---------------- 对外表面（launcher.js / 测试经 SG.downloads 调用） ---------------- */
SG.downloads = {
	render: renderDownloads,
	bind: bindDownloads,
	reload: reloadDownloadsView,
	getState() {
		return {
			versions: { status: dlVersions.status, error: dlVersions.error },
			tasks: { status: dlTasks.status, error: dlTasks.error },
		};
	},
	reset() {
		dlVersionFilter = 'release';
		dlPollTimer = null;
		dlVersions = { status: 'loading', error: null, items: [] };
		dlTasks = { status: 'loading', error: null, items: [] };
		dlReqGen = 0;
		dlReloadInFlight = false;
	},
	// launcher.js 启动时注入基础设施（toast / currentView / navigationCounts），单向
	__wire: wire,
	// 纯函数区（无 DOM / 无网络 / 无状态）：供 node:test 直接断言
	core: { versionRowHtml, dlTaskHtml, dlSourceErrorHtml, dlTasksErrorHtml, downloadsPageHtml, esc, uiIcon },
};

if (typeof module === 'object' && module.exports) module.exports = SG.downloads.core;
})(typeof window === 'undefined' ? null : window);
