(function (root) {
'use strict';
if (!root || !root.SG) return;
const SG = root.SG;
/* ================================================================
   content.js —— Content 资源库视图（M1-3 第三步收尾）
   ================================================================
   协议与 news / downloads / skins 一致：IIFE + SG.content + __wire 单向注入 +
   core 纯函数面 + module.exports。launcher.js 保留 renderContent()/bindContent(el)
   两个薄委托，VIEWS 路由表与 SG.__test__ 的入口名不变。

   ★ 本视图与前面三个最大的不同：**它的生命周期状态由 launcher 持有。**
     contentPageGen 与 contentDebTimer 的**唯一写者**是 goTo（导航核心）：
       - 进入 content 时 goTo 把 contentPageGen 设为本次 navSeq；
       - 切走 content 时 goTo 把它设为 -1 并清掉 debounce 定时器；
     content 视图只是这两个状态的**读者**，用它们做代际校验（"我这次渲染是否已过期"）。

     因此这里**不把它们搬进本文件**。若搬进来，goTo 就得反过来 import 视图模块的
     内部变量，形成导航核心 ← 视图模块的反向依赖。正确做法是：
     状态留在 launcher（所有者不变），本文件通过注入了四个访问器读写：
       pageGen()        → 读当前 pageGen（-1 表示已离页）
       setPageGen(n)    → 写（仅 retryLoadStateAndRefresh 需要写回 -1 的场景不用，保持只读）
       clearDebounce()  → 清 debounce 定时器（goTo 也用同一个）
       setDebounce(t)   → 挂 debounce 定时器
     本文件只对 pageGen 做**读**；写仍然只发生在 launcher。

   其余注入项：STATE 读取、loadState、selectedInstanceId、stateLoadFailedLast、toast。
   ================================================================ */

/* 分类表：与 launcher 的 CONTENT_CATS 保持逐项一致（拆分前是同一个常量）。
   放在本文件是安全的：它只被 content 视图使用。 */
const CONTENT_CATS = [
	{ key: 'mod', labelKey: 'content.mods' },
	{ key: 'modpack', labelKey: 'content.modpacks' },
	{ key: 'shader', labelKey: 'content.shaders' },
	{ key: 'resourcepack', labelKey: 'content.resourcepacks' },
];

let contentCat = 'mod';
let contentQuery = '';
let contentSource = 'modrinth'; // modrinth / curseforge
let contentSearchToken = 0;
let contentLoadError = null;
/* 关键：state retry 防重。连续双击只让第一次进入 retryLoadStateAndRefresh。
   这是纯视图内状态，与 pageGen 不同，可以由本文件持有。 */
let contentStateRetryInFlight = false;

/* ---------------- 注入的外部访问器 ---------------- */
let _toast = () => {};
let _getState = () => null;
let _loadState = async () => {};
let _selectedInstanceId = () => null;
let _stateLoadFailedLast = () => null;
let _pageGen = () => 0;        // 只读：-1 表示已离页
let _setDebounce = () => {};   // 挂/清 debounce 定时器（实现在 launcher，因为 goTo 也要清）

function wire(deps) {
	if (!deps) return;
	if (typeof deps.toast === 'function') _toast = deps.toast;
	if (typeof deps.getState === 'function') _getState = deps.getState;
	if (typeof deps.loadState === 'function') _loadState = deps.loadState;
	if (typeof deps.selectedInstanceId === 'function') _selectedInstanceId = deps.selectedInstanceId;
	if (typeof deps.stateLoadFailedLast === 'function') _stateLoadFailedLast = deps.stateLoadFailedLast;
	if (typeof deps.pageGen === 'function') _pageGen = deps.pageGen;
	if (typeof deps.setDebounce === 'function') _setDebounce = deps.setDebounce;
	if (typeof deps.uiIcon === 'function') _uiIcon = deps.uiIcon;
	if (typeof deps.uiSearch === 'function') _uiSearch = deps.uiSearch;
}

const t = (k, vars) => (SG.t ? SG.t(k, vars) : k);
function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
function uiText(zh, en) { return SG.locale === 'en' ? en : zh; }
/* uiIcon / uiSearch 在 launcher 里有一张 13 项图标总表，本视图只用 download 与 search。
 * 为保证渲染逐字符一致，这两个不在本文件复刻，而是由 __wire 注入原实现
 * （复刻一旦与原表漂移，卡片上的 SVG 就会静默变样）。 */
let _uiIcon = () => '';
let _uiSearch = (id, label, value = '') => '';

/* ---------------- 渲染 ---------------- */

function contentCardHtml(c) {
	// Phase 2：图标必须只引用本地图标端点（c.iconUrl 在 api.js mapContent 中已被替换）。
	// 任何包含 http(s):// 的 iconUrl 都是后端契约错误，绝不能写进 DOM。
	const hasLocalIcon = !!(c.iconUrl && c.iconUrl.indexOf && c.iconUrl.indexOf('/api/content/icon') === 0);
	const placeholder = esc(c.icon || '📦');
	const iconInner = hasLocalIcon
		? `<span class="mod-icon-placeholder" aria-hidden="true">${placeholder}</span>`
		+ `<img class="content-icon-img" data-icon-state="loading" src="${esc(c.iconUrl)}" alt="" width="52" height="52" loading="lazy" decoding="async" style="opacity:0">`
		: `<span class="mod-icon-placeholder" aria-hidden="true">${placeholder}</span>`;
	const badges = [...new Set([...(c.versions || []).slice(0, 2),...(c.loaders&&c.loaders.length?c.loaders:c.loader?[c.loader]:[])])].map(v => `<span class="mod-badge">${esc(v)}</span>`).join('');
	// 卡片必须携带自身的 source / category / projectId 身份信息，
	// 安装时使用 data-* 读取，不再依赖可能已经变化的全局筛选状态。
	const cardSource = (c.source && /^(modrinth|curseforge)$/i.test(c.source)) ? c.source.toLowerCase() : 'modrinth';
	const cardCategory = (c.category && /^(mod|modpack|shader|resourcepack)$/i.test(c.category)) ? c.category.toLowerCase() : 'mod';
	// websiteUrl 仅作 data-* 留档，不出现在可视文本中
	return `
	<div class="mod-card" data-source="${esc(cardSource)}" data-category="${esc(cardCategory)}" data-icon-state="${hasLocalIcon ? 'loading' : 'missing'}">
		<div class="mod-icon">${iconInner}</div>
		<div class="mod-body">
			<div class="mod-top">
				<div class="mod-title-group"><span class="mod-name">${esc(c.title)}</span><span class="mod-author">${esc(t('content.by', { author: c.author || t('common.unknown') }))}</span></div>
				<button class="mod-install" data-install="${esc(c.id)}" data-source="${esc(cardSource)}" data-category="${esc(cardCategory)}">${_uiIcon('download')}${esc(t('content.install'))}</button>
			</div>
			<span class="mod-desc">${esc(c.desc || '')}</span>
			<div class="mod-meta">
				<span class="mod-dl"><svg viewBox="0 0 24 24"><path d="M12 3v12M7 10l5 5 5-5M4 20h16"/></svg>${esc(c.downloads || '0')}</span>
				${badges}
			</div>
		</div>
	</div>`;
}

function contentErrorBlockHtml(errText) {
	const tech = String(errText == null ? '' : errText);
	return `<div class="content-error" data-error="1" role="alert">
		<div class="state-icon state-icon--danger" aria-hidden="true">!</div>
		<div class="state-main">
			<div class="state-title">${esc(t('content.errorTitle'))}</div>
			<p class="state-desc">${esc(t('content.errorDesc'))}</p>
			<div class="state-actions"><button class="content-retry-btn" type="button" data-retry-btn="1">${esc(t('content.retry'))}</button></div>
			${tech ? `<details class="tech-details"><summary>${esc(t('content.techDetails'))}</summary><pre class="tech-body">${esc(tech)}</pre></details>` : ''}
		</div>
	</div>`;
}

async function renderContent() {
	const token = ++contentSearchToken;
	contentLoadError = null;
	// 渲染前确保 STATE 已就绪；#content 直进时 init 阶段也未必加载过。
	// 注意：不把 STATE 取成局部快照——await loadState() 之后全局 STATE 会被整体替换，
	// 用快照会读到加载前的空值（拆分前用的就是全局 STATE 的最新值）。
	if (!_getState()) {
		try { await _loadState(); } catch (e) { /* loadState throws on failure; STATE stays null so next call retries */ }
		if (token !== contentSearchToken) return '';
	}
	// 关键：v4 pageGen 校验。content 已切走（goTo 切到别处）时不再继续。
	if (_pageGen() === -1) return '';
	const instance = ((_getState() && _getState().instances) || []).find(i => i.id === _selectedInstanceId());
	const filterHint = instance
		? `${esc(instance.name || '')} · ${esc(instance.version || '')}${instance.loader && instance.loader !== 'Vanilla' ? ' · ' + esc(instance.loader) : ''}`
		: t('content.unfiltered');
	const filterBadge = renderContentFilterBadge(instance, filterHint);
	let results = [];
	try {
		results = await SG.api.searchContent(contentQuery, contentCat, searchContentOptions());
	} catch (e) {
		// 即使失败也要在 await 之后检查序号，避免旧请求的失败错误覆盖到新的请求上下文。
		if (token !== contentSearchToken) return '';
		// 关键：await 期间页面已切走，放弃。
		if (_pageGen() === -1) return '';
		contentLoadError = e && e.message ? e.message : String(e);
	}
	// 成功路径同样要检查序号：A 请求慢、B 请求快，最终必须显示 B。
	if (token !== contentSearchToken) return '';
	// 关键：await 期间页面已切走，放弃。
	if (_pageGen() === -1) return '';
	const tabs = CONTENT_CATS.map(c => `<button class="filter ${c.key === contentCat ? 'active' : ''}" data-cat="${c.key}" aria-pressed="${c.key===contentCat}">${esc(t(c.labelKey))}</button>`).join('');
	// 关键：错误块必须放在 content-grid 内部。
	// rerenderGrid 走 grid.innerHTML 重置时，会连同错误块与重试按钮一起被替换，
	// 避免"列表已恢复但错误块与按钮仍残留"的双重状态。
	const errorBlock = contentLoadError ? contentErrorBlockHtml(contentLoadError) : '';
	const gridInner = results.length
		? results.map(contentCardHtml).join('')
		: (contentLoadError
			? ''
			: `<div class="empty-state"><div class="es-icon">\u{1F50D}</div>${esc(t('content.empty'))}</div>`);
	const grid = errorBlock + gridInner;
	return `
	<div class="content-view">
		<div class="page-head">
			<div>
				<h2 class="content-library-title">${esc(t('shell.contentLibrary'))}</h2><p class="ui-muted">${uiText('查找适合当前实例的内容','Find content for your current instance')}</p>
			</div>
            ${_uiSearch('contentSearch',t('content.searchPlaceholder'),contentQuery)}
        </div>
        <div class="content-toolbar"><div class="content-tabs">${tabs}</div><div class="content-context">${filterBadge}<div class="content-source-switch" id="contentSourceSwitch" style="display:${contentCat === 'mod' ? '' : 'none'}"><button class="source-btn ${contentSource === 'modrinth' ? 'active' : ''}" data-source="modrinth">Modrinth</button><button class="source-btn ${contentSource === 'curseforge' ? 'active' : ''}" data-source="curseforge">CurseForge</button></div></div></div>
		<div class="content-grid" id="contentGrid">${grid}</div>
	</div>`;
}

/* 关键（v4）：把 filter badge 渲染抽出来，让 retryLoadStateAndRefresh
 * 可以单独更新它而不写整个 el.innerHTML。instance 与 filterHint 仍由
 * 调用方传入（与 renderContent 计算口径一致）。 */
function renderContentFilterBadge(instance, filterHint) {
	const stateFailedMsg = _stateLoadFailedLast() || (_getState() && _getState().__loadError);
	return stateFailedMsg
		? `<span class="content-filter-warn" data-state="failed">${esc(t('content.stateLoadFailed'))}${stateFailedMsg ? '：' + esc(stateFailedMsg) : ''} <button class="content-state-retry-btn" data-state-retry-btn="1">${esc(t('content.retry'))}</button></span>`
		: `<span class="content-filter-hint" data-state="${instance ? 'filtered' : 'unfiltered'}">${filterHint}</span>`;
}

/* 关键（v4）：把原 bindContent 内的 rerenderGrid 提升为模块级。
 * - source / 分类 / 搜索 debounce / 状态重试后都走它
 * - 仅替换 #contentGrid 的 innerHTML，不动父容器 el 上的 click 监听器
 * - token 校验保证 A 慢 B 快时只写最后一次完成的
 * - pageGen 校验保证离页后的迟到请求不会复活隐藏页面 */
async function contentRerenderGrid(el) {
	if (!el) return;
	const token = ++contentSearchToken;
	const grid = el.querySelector('#contentGrid');
	const switchEl = el.querySelector('#contentSourceSwitch');
	if (switchEl) switchEl.style.display = contentCat === 'mod' ? '' : 'none';
	// 若 STATE 尚未就绪（例如 #content 直进），先拉取一次；
	// 拉取后再次比对 token 与 pageGen，避免把过期实例状态写到当前页面。
	if (!_getState()) {
		try { await _loadState(); } catch (e) { /* loadState 内部已降级 */ }
		if (token !== contentSearchToken) return;
	}
	// 关键：await loadState 期间页面可能已切走。
	if (_pageGen() === -1) return;
	// 始终先清空 grid（包括所有旧错误块、旧重试按钮、旧 loading 提示），
	// 然后显示新的 loading。重试路径下也走这条统一逻辑，避免双重状态。
	if (grid) grid.innerHTML = `<div class="loading-state">${esc(t('common.loading'))}</div>`;
	// 清空 grid 外的旧错误块（兼容性兜底；如果未来有人重新把错误块放外面，
	// 这里仍然能保证外层不留旧块）。
	const oldError = el.querySelector('.content-error[data-error="1"]');
	if (oldError) oldError.remove();
	let results = [];
	contentLoadError = null;
	try {
		results = await SG.api.searchContent(contentQuery, contentCat, searchContentOptions());
	} catch (e) {
		if (token !== contentSearchToken) return;
		// 关键：失败时同样校验 pageGen，避免把过期错误写回已切走的页面。
		if (_pageGen() === -1) return;
		// S9：CurseForge 需要用户自己的 Key（程序不再内置）。这不是"功能坏了"，要给可执行的引导。
		const code = e && e.payload && e.payload.code;
		contentLoadError = (code === 'curseforge_key_required')
			? t('content.cfKeyRequired')
			: (e && e.message ? e.message : String(e));
	}
	if (token !== contentSearchToken) return;
	// 关键：成功路径也要校验 pageGen。
	if (_pageGen() === -1) return;
	const errorBlock = contentLoadError ? contentErrorBlockHtml(contentLoadError) : '';
	const gridInner = results.length
		? results.map(contentCardHtml).join('')
		: (contentLoadError ? '' : `<div class="empty-state"><div class="es-icon">🔍</div>${esc(t('content.emptyShort'))}</div>`);
	if (grid) grid.innerHTML = errorBlock + gridInner;
	bindContentInstalls(el);
	// 关键：重渲染后旧 img 被回收，必须重新绑定图标四状态（否则永远停留在 loading）。
	bindContentIcons(el);
}

function searchContentOptions() {
	const instance = ((_getState() && _getState().instances) || []).find(i => i.id === _selectedInstanceId());
	return {
		// 显式传递当前来源与实例过滤条件，api.js 不再依赖任何私有状态。
		source: contentSource,
		gameVersion: instance ? instance.version : '',
		loader: instance && instance.loader !== 'Vanilla' ? instance.loader : '',
	};
}

function bindContent(el) {
	// 关键（v4）：父容器 el 自身的 click 监听器只绑一次。
	// 之前每次 bindContent 都给 el addEventListener('click', ...)，但 el.innerHTML 替换
	// 只清子节点、不清 el 自身的监听器，导致多次 bindContent 后单击一次触发 N 次搜索。
	// 用 el._contentClickBound 幂等标记：第一次绑定后不再添加捕获监听器。
	if (!el._contentClickBound) {
		el._contentClickBound = true;
		el.addEventListener('click', (e) => {
			const t = e.target;
			if (t && t.matches && t.matches('[data-retry-btn]')) {
				e.preventDefault();
				contentRerenderGrid(el);
			} else if (t && t.matches && t.matches('[data-state-retry-btn]')) {
				e.preventDefault();
				retryLoadStateAndRefresh(el);
			}
		}, true);
	}
	// 严格限定：来源按钮只在 #contentSourceSwitch 容器内的 .source-btn 绑定。
	// 卡片和安装按钮也带 data-source，但绝不能继承"切换来源"事件。
	// 关键（v4）：innerHTML 替换后子节点换新，这里每次都重新 querySelector 与绑定即可。
	const sourceButtons = el.querySelectorAll('#contentSourceSwitch .source-btn[data-source]');
	sourceButtons.forEach(b => b.addEventListener('click', () => {
		sourceButtons.forEach(x => {x.classList.remove('active');x.setAttribute('aria-pressed','false');});
		b.classList.add('active');b.setAttribute('aria-pressed','true');
		const next = b.dataset.source;
		if (!/^(modrinth|curseforge)$/i.test(next)) return;
		contentSource = next.toLowerCase();
		contentRerenderGrid(el);
	}));
	el.querySelectorAll('[data-cat]').forEach(b => b.addEventListener('click', () => {
		el.querySelectorAll('[data-cat]').forEach(x => x.classList.remove('active'));
		b.classList.add('active');b.setAttribute('aria-pressed','true');
		contentCat = b.dataset.cat;
		contentRerenderGrid(el);
	}));
	const search = el.querySelector('#contentSearch');
	if (search) {
		// 关键（v4）：debounce 定时器改用模块级 contentDebTimer。
		// goTo 切走 content 时清空，避免离页后到期仍触发 searchContent。
		// 重新进入 content 时由新的 bindContent 接管，不复用旧 timer。
		// M1-3：定时器句柄仍归 launcher（goTo 也要清它），这里通过注入的
		// setDebounce(fn, ms) 挂新的、内部负责先清旧的。
		search.addEventListener('input', () => {
			contentQuery = search.value;
			_setDebounce(() => contentRerenderGrid(el), 350);
		});
	}
	bindContentInstalls(el);
	bindContentIcons(el);
}

/* Phase 2：资源卡片图标的四状态事件绑定（loading/loaded/missing/failed）。
 * 关键不变量：
 *   1. 只能对当前 el 范围内的 img 绑定。grid 重渲染后旧 img 被回收，新 img 会重新绑定。
 *   2. error 监听器移除失败 img、保留占位层，禁止无限重试（不修改 src）。
 *   3. 加载成功时淡入；不影响 install 按钮的 source/category。
 *   4. 不修改 data-source / data-category，不触发额外搜索。
 *   5. error 不暴露上游 URL / token / 内部错误到 DOM。 */
function bindContentIcons(el) {
	if (!el) return;
	const grid = el.querySelector('#contentGrid');
	if (!grid) return;
	const imgs = grid.querySelectorAll('img.content-icon-img');
	imgs.forEach((img) => {
		// 每个 img 只绑定一次；如已绑定则跳过（grid 重渲染后旧 img 已消失，不会重复）
		if (img.dataset.iconBound === '1') return;
		img.dataset.iconBound = '1';

		// 标记卡片初始为 loading 状态（contentCardHtml 已设）
		const card = img.closest('.mod-card');
		if (card && card.dataset.iconState === 'loading') {
			// 默认显示占位；img 已 style="display:none" 等成功后再切到 loaded
		}

		// 关键：磁盘缓存命中时 img 在绑定前已 complete（load 事件已错过），
		// 必须同步兜底判定，否则图标永远停留在 loading 占位。
		if (img.complete && img.naturalWidth > 0) {
			img.style.opacity = '1';
			img.dataset.iconState = 'loaded';
			const c0 = img.closest('.mod-card');
			if (c0) {
				c0.dataset.iconState = 'loaded';
				const ph0 = c0.querySelector('.mod-icon-placeholder');
				if (ph0) ph0.style.display = 'none';
			}
			return;
		}
		if (img.complete && img.naturalWidth === 0) {
			// 绑定前已失败（如 404/415）→ 与 error 监听器同路径：failed + 移除 img
			img.dataset.iconState = 'failed';
			const c1 = img.closest('.mod-card');
			if (c1) c1.dataset.iconState = 'failed';
			try { img.remove(); } catch (e) { /* ignore */ }
			return;
		}

		img.addEventListener('load', () => {
			// 关键：grid 重渲染后旧 img 不会触发 load 事件（已被 GC），
			// 因此这里的 load 必然来自当前 el 范围内的 img。
			if (!img.isConnected) return;
			// opacity:0 初始隐藏保留布局框（display:none + loading=lazy 在 Chromium 中
			// 永不与视口相交 → 请求永不发出）；load 后置 1 淡入。
			img.style.opacity = '1';
			img.dataset.iconState = 'loaded';
			const c = img.closest('.mod-card');
			if (c) c.dataset.iconState = 'loaded';
			// 隐藏占位层
			const ph = c ? c.querySelector('.mod-icon-placeholder') : null;
			if (ph) ph.style.display = 'none';
		}, { once: true });

		img.addEventListener('error', () => {
			// 关键：error 后只切到 failed 状态、隐藏 img、保留占位。
			// 严禁重新设置 src、严禁重试请求。
			if (!img.isConnected) return;
			img.dataset.iconState = 'failed';
			const c = img.closest('.mod-card');
			if (c) c.dataset.iconState = 'failed';
			// 移除 img 而不是保留隐藏，避免 listener 反复触发
			try { img.remove(); } catch (e) { /* ignore */ }
			// 确保占位层可见
			if (c) {
				const ph = c.querySelector('.mod-icon-placeholder');
				if (ph) ph.style.display = '';
			}
		}, { once: true });
	});
}

async function retryLoadStateAndRefresh(el) {
	const myGen = _pageGen();
	if (myGen === -1) return;  // content 已切走
	if (contentStateRetryInFlight) return;  // 防重
	contentStateRetryInFlight = true;
	const stateBadge = el.querySelector('.content-filter-warn');
	if (stateBadge) stateBadge.setAttribute('data-loading', '1');
	try {
		try { await _loadState(true); } catch (e) { /* loadState 内部已更新 stateLoadFailedLast，throw 仅用于通知调用方 */ }
		// 关键：await loadState 期间页面可能已切走或再次进入新生命周期。
		if (myGen !== _pageGen()) return;
		// 成功或失败路径都用同样的更新方式：替换 filter badge + 触发 grid 重渲染。
		// filter badge 用 pageGen 校验的 renderContentFilterBadge 同步替换；
		// 替换时若元素已不存在（被新内容替换）则跳过。
		// 注意：此处必须取**await 之后**的最新 STATE（loadState 会整体替换它）。
		const instance = ((_getState() && _getState().instances) || []).find(i => i.id === _selectedInstanceId());
		const filterHint = instance
			? `${esc(instance.name || '')} · ${esc(instance.version || '')}${instance.loader && instance.loader !== 'Vanilla' ? ' · ' + esc(instance.loader) : ''}`
			: t('content.unfiltered');
		const newBadge = renderContentFilterBadge(instance, filterHint);
		const oldBadge = el.querySelector('[data-state]');
		if (oldBadge) {
			// 关键（v5）：改用浏览器标准 DOM API，避免依赖 .parent（测试桩专有）
			// 与 children.indexOf()（HTMLCollection 不提供）。先构造新节点，
			// 再原子替换旧节点；任何一步失败至少保留旧提示，不让页面陷入"提示消失"。
			const parent = oldBadge.parentNode;
			if (parent) {
				let newNode = null;
				try {
					// 关键：把 HTML 字符串先写到临时容器，再用 firstElementChild
					// 提取第一个元素节点。newBadge 已经过 esc() 编码，不含未受信任脚本。
					const tmp = document.createElement('div');
					tmp.innerHTML = newBadge;
					newNode = tmp.firstElementChild;
				} catch (e) { newNode = null; }
				if (newNode) {
					// 关键：parentNode.replaceChild 是原子操作；旧节点先被替换、
					// 不会留下"oldBadge 已 remove 但 newBadge 未插入"的真空。
					try {
						parent.replaceChild(newNode, oldBadge);
					} catch (e) {
						// 极端失败（如 parentNode 已被离页清理）：保留旧提示
					}
				}
				// 若 newNode 构造失败：保留旧提示；不删除旧节点
			}
		}
		if (myGen !== _pageGen()) return;
		await contentRerenderGrid(el);
	} finally {
		contentStateRetryInFlight = false;
	}
}

function bindContentInstalls(el) {
	el.querySelectorAll('[data-install]').forEach(b => b.addEventListener('click', async (event) => {
		// 防止事件冒泡导致多次触发（卡片 click + 按钮 click）。
		if (event) { try { event.stopPropagation(); } catch (e) { /* ignore */ } }
		// 防止 rerender 后旧按钮（已被 DOM 替换）仍能触发 install。
		if (b.isConnected === false) return;
		const projectId = b.dataset.install;
		if (!projectId || b.dataset.busy) return;
		const card = b.closest('.mod-card');
		// 身份必须来自被点击的卡片 / 按钮自身；缺失或非法时直接拒绝并提示，
		// 不允许悄悄回退到全局 contentSource / contentCat 猜测。
		const rawSource = (b.dataset && b.dataset.source) || (card && card.dataset && card.dataset.source) || '';
		const rawCategory = (b.dataset && b.dataset.category) || (card && card.dataset && card.dataset.category) || '';
		if (!/^(modrinth|curseforge)$/i.test(rawSource)) {
			_toast(t('content.installMissingSource'));
			return;
		}
		if (!/^(mod|modpack|shader|resourcepack)$/i.test(rawCategory)) {
			_toast(t('content.installMissingCategory'));
			return;
		}
		const category = rawCategory.toLowerCase();
		const source = rawSource.toLowerCase();
		b.dataset.busy = '1';
		b.disabled = true;
		b.textContent = t('content.installing');
		try {
			const res = await SG.api.installContent({ category, projectId, instanceId: _selectedInstanceId(), source });
			_toast(t('content.queued', { id: res.id }));
			// 轮询任务直至完成（live 模式下有真实进度；mock 立即 completed）
			const poll = async () => {
				let tasks = [];
				try { tasks = await SG.api.getContentTasks(); } catch (e) { tasks = []; }
				const task = tasks.find(x => x.id === res.id);
				if (!task) {
					b.textContent = t('content.installed');
					b.classList.add('installed');
					_toast(t('content.installed'));
					return;
				}
				if (task.status === 'completed' || task.status === 'failed') {
					b.textContent = task.status === 'completed' ? t('content.installed') : t('content.installFailed');
					b.classList.add(task.status === 'completed' ? 'installed' : 'failed');
					if (task.status === 'completed') {
						_toast(t('content.installed'));
						// modpack 创建了新实例，刷新实例列表
						if (task.category === 'modpack') await _loadState(true);
					} else {
						_toast(t('content.installFailed') + ': ' + (task.message || t('common.unknownError')));
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
			_toast(t('content.installFailed') + ': ' + (err.message || t('common.unknownError')));
		} finally {
			delete b.dataset.busy;
			b.disabled = false;
		}
	}));
}

const SG_NS = SG;
SG_NS.content = {
	render: renderContent,
	bind: bindContent,
	rerenderGrid: contentRerenderGrid,      // goTo 回访 content 时若 grid 仍是 loading 需重搜
	retryLoadStateAndRefresh,
	bindContentInstalls,
	bindContentIcons,
	searchContentOptions,
	// 状态读写（SG.__test__ 委托到这里）
	getState() {
		return { cat: contentCat, query: contentQuery, source: contentSource,
			searchToken: contentSearchToken, loadError: contentLoadError };
	},
	setCat(v) { if (/^(mod|modpack|shader|resourcepack)$/i.test(v)) contentCat = v.toLowerCase(); },
	setQuery(v) { contentQuery = String(v == null ? '' : v); },
	setSource(v) { if (/^(modrinth|curseforge)$/i.test(v)) contentSource = v.toLowerCase(); },
	setLoadError(v) { contentLoadError = v == null ? null : String(v); },
	resetSearchToken() { contentSearchToken = 0; },
	__wire: wire,
	// 纯函数区（无 DOM / 无网络 / 无状态）：供 node:test 直接断言
	core: { contentCardHtml, contentErrorBlockHtml, CONTENT_CATS },
};

if (typeof module === 'object' && module.exports) module.exports = SG_NS.content.core;
})(typeof window === 'undefined' ? null : window);
