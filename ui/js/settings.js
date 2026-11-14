(function (root) {
'use strict';
if (!root || !root.SG) return;
const SG = root.SG;
/* ================================================================
   settings.js —— Settings 设置页（M1-3 第三步最后一个视图）
   ================================================================
   协议与 news / downloads / skins / content 一致：IIFE + SG.settings + __wire 单向注入 +
   core 纯函数面 + module.exports。launcher.js 保留 renderSettings()/bindSettings(el)
   两个薄委托，VIEWS 路由表与 SG.__test__ 的入口名不变。

   ★ 本视图与 content 的对比：
     - content 把 pageGen 生命周期**留在 launcher**（goTo 是唯一写者）；
     - settings 没有任何导航级状态，它的全部状态都是 **STATE.config 的属性**，
       所以这里注入的是 getState() 取值函数，读写都经由它。
       STATE 本身是对象引用，`STATE.config.x = v` 与 `_getState().config.x = v`
       作用在同一对象上——本轮的所有写入型改写都依赖这一点。

   ★ 整页重渲染（rerenderInPlace）：
     设置里有 6 处"改完之后整页刷新"（内存/自动内存/Java 路径/账户切换/微软登录成功/
     皮肤刷新）。拆分前它们写的是
         const v = document.getElementById('view-settings');
         v.innerHTML = await renderSettings(); bindSettings(v);
     这属于本模块自递归，不该经由 launcher。抽成本文件的 rerenderInPlace()。

   ★ 未拆的部分：
     `/* ---- Settings 更新区 ---- * /`（updateMonitor / addUpdateBindings /
     renderUpdateNotice / pollUpdateProgress / wireWindowControls / refreshSidebarAccount /
     wireNav）是独立子系统，被 init 与全局 UI 使用，**留在 launcher.js**。
     本文件通过 __wire 拿到 addUpdateBindings 与 refreshSidebarAccount 两个入口。
   ================================================================ */

/* ---------------- 注入的外部访问器 ---------------- */
let _toast = () => {};
let _openModal = () => {};
let _closeModal = () => {};
let _invalidate = () => {};
let _refreshSidebarAccount = () => {};
let _openAiManage = () => {};
let _addUpdateBindings = () => {};
let _loadAccounts = async () => [];
let _loadState = async () => {};
let _getState = () => null;
/* uiIcon 由 launcher 注入：那里有一张 13 项图标总表，复刻一份就多一个漂移点 */
let _uiIcon = () => '';

function wire(deps) {
	if (!deps) return;
	if (typeof deps.toast === 'function') _toast = deps.toast;
	if (typeof deps.openModal === 'function') _openModal = deps.openModal;
	if (typeof deps.closeModal === 'function') _closeModal = deps.closeModal;
	if (typeof deps.invalidate === 'function') _invalidate = deps.invalidate;
	if (typeof deps.refreshSidebarAccount === 'function') _refreshSidebarAccount = deps.refreshSidebarAccount;
	if (typeof deps.openAiManage === 'function') _openAiManage = deps.openAiManage;
	if (typeof deps.addUpdateBindings === 'function') _addUpdateBindings = deps.addUpdateBindings;
	if (typeof deps.loadAccounts === 'function') _loadAccounts = deps.loadAccounts;
	if (typeof deps.loadState === 'function') _loadState = deps.loadState;
	if (typeof deps.getState === 'function') _getState = deps.getState;
	if (typeof deps.uiIcon === 'function') _uiIcon = deps.uiIcon;
}

const t = (k, vars) => (SG.t ? SG.t(k, vars) : k);
function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
function uiText(zh, en) { return SG.locale === 'en' ? en : zh; }
/* AI 设置防重锁：原在 launcher.js，随视图一起搬来（只有设置页用它） */
let aiSettingsSaving = false;

/* ---------------- 整页重渲染（本模块自递归） ---------------- */
async function rerenderInPlace() {
	const v = document.getElementById('view-settings');
	if (!v) return;
	v.innerHTML = await renderSettings();
	bindSettings(v);
}

/* ==================== 原文：Settings 页预设与来源选择 ==================== */

// GENSETTINGS-HEAD-BEGIN
// 外置登录皮肤站预设（LittleSkin 等，地址为 authlib-injector API 根）
const AUTH_PRESETS = [
	{ name: 'LittleSkin', url: 'https://littleskin.cn/api/yggdrasil', desc: '国内最大皮肤站' },
	{ name: 'Blessing Skin', url: 'https://skin.prinzeugen.net/api/yggdrasil', desc: 'Blessing Skin 演示站' },
	{ name: '自定地址', url: '', desc: '填写任意 authlib-injector 兼容地址' },
];
function sourceLabel(key) {
	const names = { official: t('set.sourceOfficial'), bmclapi: 'BMCLAPI', mcbbs: t('set.sourceMcbbs') };
	return names[key] || key || t('set.sourceOfficial');
}
async function openSourcePicker() {
	const host = _openModal(`<div class="modal-title">${esc(t('set.sourceName'))}</div>
		<div class="source-list"><div class="loading-state">${esc(t('common.loading'))}</div></div>
		<div class="modal-actions"><button class="modal-btn" id="scClose">${esc(t('common.close'))}</button></div>`);
	if (!host) return;
	const generation = host.dataset.modalGeneration;
	const isCurrent = () => !host.hidden && host.dataset.modalGeneration === generation;
	document.getElementById('scClose').onclick = _closeModal;
	try {
		const response = await SG.api.getSources();
		if (!isCurrent()) return;
		const sources = response.sources || [];
		if (!sources.length) throw new Error(t('set.sourceLoadFailed'));
		const selected = response.selected || (_getState() && _getState().config.downloadSource) || 'official';
		host.querySelector('.source-list').innerHTML = sources.map(s => {
			const latency = s.available ? (s.latencyMs >= 0 ? Math.round(s.latencyMs) + ' ms' : '—') : t('set.sourceUnavailable');
			return `<button class="source-option" data-source="${esc(s.key)}"><span class="source-name">${esc(sourceLabel(s.key))}</span>
				<span class="source-lat">${esc(latency)}</span>${s.key === selected ? '<span class="source-check">✓</span>' : ''}</button>`;
		}).join('');
		let saving = false;
		const sourceEls = host.querySelectorAll('.source-option');
		sourceEls.forEach(opt => opt.addEventListener('click', async () => {
			if (saving || !isCurrent()) return;
			saving = true;
			sourceEls.forEach(button => { button.disabled = true; });
			try {
				const result = await SG.api.selectSource(opt.dataset.source);
				if (!result.ok) throw new Error(t('set.sourceSwitchFailed'));
				_getState().config.downloadSource = result.selected;
				_getState().config.useMirror = result.useMirror;
				_toast(t('set.sourceSwitched', { name: sourceLabel(result.selected) }));
				if (isCurrent()) _closeModal();
				invalidate('settings');
				const button = document.getElementById('btnSourceChange');
				if (button) button.textContent = t('set.sourceCurrent', { name: sourceLabel(result.selected) });
			} catch (error) { _toast(t('set.sourceSwitchFailed') + (error.message || t('common.unknownError'))); }
			finally { saving = false; sourceEls.forEach(button => { button.disabled = false; }); }
		}));
	} catch (error) {
		if (!isCurrent()) return;
		host.querySelector('.source-list').innerHTML = `<div role="alert"><p>${esc(t('set.sourceLoadFailed'))}</p>
			<details class="tech-details"><summary>${esc(t('content.techDetails'))}</summary><pre class="tech-body">${esc(error.message || t('common.unknownError'))}</pre></details>
			<button class="modal-btn primary" id="scRetry">${esc(t('dl.retry'))}</button></div>`;
		document.getElementById('scRetry').onclick = openSourcePicker;
	}
}

// GENSETTINGS-HEAD-END

function aiActiveName(cfg) {
	const list = cfg.aiProviders || [];
	const cur = list.find(p => p.id === cfg.selectedAiProviderId);
	return cur ? cur.name : t('set.aiNotSet');
}

function renderAccountCards(accounts) {
	if (!accounts || !accounts.length) return '<div class="account-empty">' + esc(t('set.clickAddAccount')) + '</div>';
	return accounts.map(a => {
		const typeLabel = a.type === 'microsoft' ? t('acc.microsoft') : a.type === 'authlib' ? t('acc.authlib') : t('acc.offline');
		const badge = a.selected ? '<span class="account-current">' + esc(t('set.currentAccount')) + '</span>' : '<span class="account-use">' + esc(t('set.useAccount')) + '</span>';
		return '<div class="account-card' + (a.selected ? ' selected' : '') + '" data-account="' + esc(a.id) + '" role="button" tabindex="0">' +
			'<span class="account-card-avatar">' + SG.skinVisuals.faceThumb(40, a.skins && a.skins[0]) + '</span>' +
			'<span class="account-card-body">' +
				'<span class="account-card-name">' + esc(a.userName || 'Player') + '</span>' +
				'<span class="account-card-type">' + esc(typeLabel) + '</span>' +
				'<span class="account-card-stats">' +
					'<span class="account-stat">' + esc(t('set.skinCount', { n: a.skinCount || 0 })) + '</span>' +
					'<span class="account-stat">' + esc(t('set.capeCount', { n: a.capeCount || 0 })) + '</span>' +
				'</span>' +
			'</span>' +
			badge +
			'<span class="account-card-del" data-del="' + esc(a.id) + '" role="button" tabindex="0">' + esc(t('set.deleteAccount')) + '</span>' +
		'</div>';
	}).join('');
}

/* ==================== 原文：renderSettings ==================== */

// GENSETTINGS-RENDER-BEGIN
async function renderSettings() {
	await _loadState();
	const cfg = _getState().config || {};
	const java = _getState().java || [];
	const accounts = await _loadAccounts();
	const acc = accounts.find(a => a.selected) || accounts[0];

	const MIN = 2, MAX = 16;
	const gb = Math.max(MIN, Math.min(MAX, (cfg.maxMemoryMb || 4096) / 1024));
	const pct = ((gb - MIN) / (MAX - MIN)) * 100;
	const autoMem = !!cfg.useAutoMemory;
	const useMirror = !!cfg.useMirror;
	const dlSource = cfg.downloadSource || 'official';
	// R2-02：新闻图片 fake-ip/TUN 兼容开关（默认 false，fail closed）
	const newsFakeIp = !!cfg.newsFakeIpCompat;

	const currentJava = java.find(j => j.path === cfg.javaPath);
	const javaLabel = cfg.javaPath
		? (currentJava ? `Java ${currentJava.versionMajor} · ${currentJava.path}` : cfg.javaPath)
		: (java.length ? t('set.javaAuto', { n: java.length }) : t('set.javaBundled'));


	return `
	<div class="settings-view"><div class="settings-grid">
		<div class="settings-section">
			<div class="section-heading">${esc(t('set.javaSection'))}</div>
			<div class="section-content">
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.memName'))}</span>
						<span class="setting-desc">${esc(t('set.memDesc'))}</span>
					</div>
					<div class="slider-wrap">
						<input type="range" class="memory-range" id="memSlider" min="2048" max="16384" step="512" value="${Math.round(gb*1024)}" aria-label="${esc(t('set.memName'))}"${autoMem ? ' disabled' : ''}>
						<span class="setting-value" id="memValue">${gb.toFixed(1)} GB</span>
					</div>
				</div>
				<div class="setting-divider"></div>
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.autoMemName'))}</span>
						<span class="setting-desc">${esc(t('set.autoMemDesc'))}</span>
					</div>
					<button class="toggle ${autoMem ? 'on' : ''}" data-toggle="autoMem" aria-label="${esc(t('set.autoMemName'))}" role="switch" aria-checked="${autoMem}"></button>
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
						<span class="setting-name">${esc(t('set.sourceName'))}</span>
						<span class="setting-desc">${esc(t('set.sourceDesc'))}</span>
					</div>
					<div class="source-select" id="sourceSelect">
						<button class="setting-btn" id="btnSourceChange">${esc(t('set.sourceCurrent', { name: sourceLabel(dlSource) }))}</button>
					</div>
				</div>
				<div class="setting-divider"></div>
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.fakeIpName'))}</span>
						<span class="setting-desc">${esc(t('set.fakeIpDesc'))}</span>
					</div>
					<button class="toggle ${newsFakeIp ? 'on' : ''}" data-toggle="newsFakeIp" aria-label="${esc(t('set.fakeIpName'))}" role="switch" aria-checked="${newsFakeIp}"></button>
				</div>
				<div class="setting-divider"></div>
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.proxyName'))}</span>
						<span class="setting-desc">${esc(t('set.proxyDesc'))}</span>
					</div>
					<button class="setting-btn" id="btnProxy">${esc(t('set.proxyConfigure'))}</button>
				</div>

			</div>
		</div>

        <section class="settings-section settings-quick"><div class="section-heading">${esc(t('set.quickName'))}</div><div class="section-content"><p class="ui-muted">${esc(t('set.quickDesc'))}</p><button class="setting-btn" id="btnQuick">${esc(t('set.quickConfigure'))}</button></div></section>
        <section class="settings-section settings-logs"><div class="section-heading">${esc(t('set.logsName'))}</div><div class="section-content"><p class="ui-muted">${esc(t('set.logsDesc'))}</p><button class="setting-btn" id="btnOpenLogs">${_uiIcon('folder')}${esc(t('set.openLogs'))}</button></div></section>

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
				<div id="updateTrust" class="update-trust-host"></div>
			</div>
		</div>

		<div class="settings-section">
			<div class="section-heading">${esc(t('set.aiSection'))}</div>
			<div class="section-content">
				<div class="setting-row">
					<div class="setting-info">
						<span class="setting-name">${esc(t('set.aiEnableName'))}</span>
						<span class="setting-desc">${esc(t('set.aiEnableDesc'))}</span>
					</div>
					<button class="toggle ${cfg.aiDiagnosisEnabled ? 'on' : ''}" data-toggle="aiDiag" aria-label="${esc(t('set.aiEnableName'))}" role="switch" aria-checked="${!!cfg.aiDiagnosisEnabled}"${aiSettingsSaving ? ' disabled' : ''}></button>
				</div>
				<div class="setting-divider"></div>
				<div class="setting-row">
				<div class="setting-info">
					<span class="setting-name">${esc(t('set.aiSection'))}</span>
					<span class="setting-desc">${esc(aiActiveName(cfg))}</span>
				</div>
				<button class="setting-btn" id="btnAiManage">${esc(t('set.aiManage'))}</button>
			</div>
			</div>
		</div>
        </div>
		<div class="settings-section">
			<div class="section-heading">${esc(t('set.accountSection'))}</div>
			<div class="section-content">
				<div class="account-login-actions">
					<button class="setting-btn" id="btnMsLogin">${esc(t('set.microsoftLogin'))}</button>
					<button class="setting-btn" id="btnAuthlibLogin">${esc(t('set.authlibLogin'))}</button>
					<button class="setting-btn" id="btnAddAccount">${esc(t('set.addAccount'))}</button>
				</div>
				<div class="account-cards" id="accountCards">${renderAccountCards(accounts)}</div>
			</div>
		</div>
	</div>`;
}
// GENSETTINGS-RENDER-END

/* ==================== 原文：bindSettings ==================== */

// GENSETTINGS-BIND-BEGIN
function bindSettings(el) {
	// 内存滑块（2–16 GB）→ 真实写入 maxMemoryMb
 const slider=el.querySelector('#memSlider');
 if(slider){
  slider.addEventListener('input',()=>{el.querySelector('#memValue').textContent=(Number(slider.value)/1024).toFixed(1)+' GB';});
  slider.addEventListener('change',async()=>{const previous=_getState().config.maxMemoryMb;try{await SG.api.updateConfig({maxMemoryMb:Number(slider.value),useAutoMemory:false});_getState().config.maxMemoryMb=Number(slider.value);_invalidate('home');}catch(err){slider.value=String(previous);el.querySelector('#memValue').textContent=(previous/1024).toFixed(1)+' GB';_toast(err.message||t('common.unknownError'));}});
 }

	// 开关 → 真实写入
	// 保存期间去重；AI 分支同时保护语言重绘后的新控件，成功同步状态、失败回滚。
	// 注意：参数名用 toggle 而非 t，避免遮蔽顶层 i18n 函数 t(key)
	// 注意：必须用 contains + 显式设 on，避免依赖 DOM classList.toggle() 的返回值
	// （测试桩的 classList.toggle 不返回 boolean，会让 on 变 undefined）
	const submittingToggles = new Set();
	el.querySelectorAll('.toggle').forEach(toggle => {
		toggle.addEventListener('click', () => {
			const key = toggle.dataset.toggle;
			if (key === 'autoMem') {
				if(submittingToggles.has(key))return;
				submittingToggles.add(key);toggle.disabled=true;
				const previous=toggle.classList.contains('on');
				const on = !toggle.classList.contains('on');
				toggle.classList.toggle('on', on);
				toggle.setAttribute('aria-checked', on ? 'true' : 'false');
				SG.api.updateConfig({ useAutoMemory: on }).then(()=>{_getState().config.useAutoMemory=on;}).catch(err=>{toggle.classList.toggle('on',previous);toggle.setAttribute('aria-checked',String(previous));if(s)s.disabled=previous;_toast(err.message||t('common.unknownError'));}).finally(()=>{submittingToggles.delete(key);toggle.disabled=false;});
				const s = el.querySelector('#memSlider');
				if (s) s.disabled = on;
				_invalidate('home');
			} else if (key === 'aiDiag') {
				if (aiSettingsSaving) return;
				aiSettingsSaving = true;
				const previous = !!_getState().config.aiDiagnosisEnabled;
				const desired = !previous;
				const show = (value, busy) => document.querySelectorAll('[data-toggle="aiDiag"]').forEach(button => {
					button.classList.toggle('on', value); button.setAttribute('aria-checked', String(value)); button.disabled = busy;
				});
				show(desired, true);
				SG.api.updateConfig({ aiDiagnosisEnabled: desired }).then(result => {
					const value = result && result.config && typeof result.config.aiDiagnosisEnabled === 'boolean' ? result.config.aiDiagnosisEnabled : desired;
					_getState().config.aiDiagnosisEnabled = value; show(value, true);
				}).catch(error => {
					_getState().config.aiDiagnosisEnabled = previous; show(previous, true);
					_toast(t('set.aiToggleSaveFailed') + (error.message || t('common.unknownError')));
				}).finally(() => { aiSettingsSaving = false; show(!!_getState().config.aiDiagnosisEnabled, false); });
			} else if (key === 'newsFakeIp') {
				// R2-02：连点去重（in-flight 时第二次点击直接拒绝）
				if (submittingToggles.has(key)) return;
				submittingToggles.add(key);
				toggle.disabled = true;
				const on = !toggle.classList.contains('on');
				toggle.classList.toggle('on', on);
				toggle.setAttribute('aria-checked', on ? 'true' : 'false');
				const desired = on;
				SG.api.updateConfig({ newsFakeIpCompat: desired })
					.then((res) => {
						if (res && res.config) Object.assign(_getState().config, res.config);
						else _getState().config.newsFakeIpCompat = desired;
						submittingToggles.delete(key);
						toggle.disabled = false;
						_toast(t('set.fakeIpSaved'));
					})
					.catch((err) => {
						// 失败回滚：class/ARIA/_getState() 全部恢复为点击前的值
						const cur = !!(_getState().config && _getState().config.newsFakeIpCompat);
						toggle.classList.toggle('on', cur);
						toggle.setAttribute('aria-checked', cur ? 'true' : 'false');
						submittingToggles.delete(key);
						toggle.disabled = false;
						_toast(t('set.fakeIpSaveFailed') + (err && err.message ? err.message : t('common.unknownError')));
					});
			}
		});
	});
	// 下载源选择
	el.querySelector('#btnSourceChange')?.addEventListener('click', openSourcePicker);
	// 代理配置
	el.querySelector('#btnProxy')?.addEventListener('click', () => {
		const cfg = _getState().config || {};
		const enabled = !!cfg.proxyEnabled;
		_openModal(`
			<div class="modal-title">${esc(t('set.proxyName'))}</div>
			<div class="page-sub">${esc(t('set.proxyDesc'))}</div>
			<div class="modal-field" style="flex-direction:row;align-items:center;gap:10px">
				<label style="font-size:13px;color:var(--text,#e8ecf4)">${esc(t('set.proxyEnable'))}</label>
				<button class="toggle ${enabled ? 'on' : ''}" id="proxyToggle" role="switch" aria-checked="${enabled}"></button>
			</div>
			<div class="modal-field"><span class="modal-label">${esc(t('set.proxyHost'))}</span><input class="modal-input" id="pHost" value="${esc(cfg.proxyHost || '')}" placeholder="127.0.0.1"></div>
			<div class="modal-field"><span class="modal-label">${esc(t('set.proxyPort'))}</span><input class="modal-input" id="pPort" value="${esc(cfg.proxyPort || '')}" placeholder="7890" type="number"></div>
			<div class="modal-field"><span class="modal-label">${esc(t('set.proxyUser'))}</span><input class="modal-input" id="pUser" value="${esc(cfg.proxyUsername || '')}" placeholder="${esc(t('set.proxyUserOptional'))}"></div>
			<div class="modal-field"><span class="modal-label">${esc(t('set.proxyPass'))}</span><input class="modal-input" id="pPass" value="${esc(cfg.proxyPassword || '')}" type="password" placeholder="${esc(t('set.proxyPassOptional'))}"></div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="pCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="pOk">${esc(t('common.save'))}</button>
			</div>`);
		document.getElementById('pCancel').onclick = _closeModal;
		document.getElementById('proxyToggle').onclick = () => {
			const t = document.getElementById('proxyToggle');
			t.classList.toggle('on');
			t.setAttribute('aria-checked', t.classList.contains('on') ? 'true' : 'false');
		};
		document.getElementById('pOk').onclick = async () => {
			const payload = {
				proxyEnabled: document.getElementById('proxyToggle').classList.contains('on'),
				proxyHost: document.getElementById('pHost').value.trim(),
				proxyPort: parseInt(document.getElementById('pPort').value, 10) || 0,
				proxyUsername: document.getElementById('pUser').value.trim(),
				proxyPassword: document.getElementById('pPass').value,
			};
			_closeModal();
			try {
				await SG.api.updateConfig(payload);
				Object.assign(_getState().config, payload);
				_toast(t('set.proxySaved'));
			} catch (err) { _toast(t('set.proxyFailed') + (err.message || t('common.unknownError'))); }
		};
	});
	// AI 提供商管理
	el.querySelector('#btnAiManage')?.addEventListener('click', () => _openAiManage());
	// 快捷操作
	el.querySelector('#btnQuick')?.addEventListener('click', () => {
		const cfg = _getState().config || {};
		_openModal(`
			<div class="modal-title">${esc(t('set.quickName'))}</div>
			<div class="page-sub">${esc(t('set.quickDesc'))}</div>
			<div class="quick-opt">
				<button class="toggle ${cfg.minimizeOnLaunch ? 'on' : ''}" id="qMin" role="switch" aria-checked="${!!cfg.minimizeOnLaunch}"></button>
				<span class="quick-label">${esc(t('set.quickMinimize'))}</span>
			</div>
			<div class="quick-opt">
				<button class="toggle ${cfg.autoCloseOnExit ? 'on' : ''}" id="qClose" role="switch" aria-checked="${!!cfg.autoCloseOnExit}"></button>
				<span class="quick-label">${esc(t('set.quickClose'))}</span>
			</div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="qCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="qOk">${esc(t('common.save'))}</button>
			</div>`);
		document.getElementById('qCancel').onclick = _closeModal;
		const bindToggle = (id) => document.getElementById(id).onclick = () => {
			const t = document.getElementById(id);
			t.classList.toggle('on');
			t.setAttribute('aria-checked', t.classList.contains('on') ? 'true' : 'false');
		};
		bindToggle('qMin'); bindToggle('qClose');
		document.getElementById('qOk').onclick = async () => {
			const payload = {
				minimizeOnLaunch: document.getElementById('qMin').classList.contains('on'),
				autoCloseOnExit: document.getElementById('qClose').classList.contains('on'),
			};
			_closeModal();
			try {
				await SG.api.updateConfig(payload);
				Object.assign(_getState().config, payload);
				_toast(t('set.quickSaved'));
			} catch (err) { _toast(t('set.quickFailed') + (err.message || t('common.unknownError'))); }
		};
	});
	el.querySelector('#btnJrePath')?.addEventListener('click', () => {
		const java = _getState().java || [];
		if (!java.length) { _toast(t('set.jreNotFound')); return; }
		const cfg = _getState().config || {};
		const idx = java.findIndex(j => j.path === cfg.javaPath);
		const next = java[(idx + 1) % java.length]; // 循环切换到下一个 JDK
		SG.api.updateConfig({ javaPath: next.path }).then(() => {
			_getState().config.javaPath = next.path;
			el.querySelector('#btnJrePath').textContent = `Java ${next.versionMajor} · ${next.path}`;
			_toast(t('set.javaSwitched', { ver: next.versionText }));
		}).catch(err => _toast(t('set.switchFailed') + (err.message || '')));
	});
	el.querySelector('#btnOpenLogs')?.addEventListener('click', () => {
		SG.api.openLogDirectory().then(r => {
			if (!r.ok) _toast(t('set.logsFailed'));
		}).catch(e => _toast(t('set.logsFailed') + (e.message || '')));
	});
	el.querySelector('#btnMsLogin')?.addEventListener('click', async () => {
		const btn = el.querySelector('#btnMsLogin');
		if (!btn || btn.disabled) return;
		btn.disabled = true;
		btn.textContent = t('set.microsoftLoggingIn');
		try {
			const info = await SG.api.microsoftDeviceStart();
			if (!info || !info.ok) throw new Error((info && info.message) || '无法创建微软登录码');
			const userCode = info.userCode || '';
			const verifyUrl = info.verificationUriComplete || info.verificationUri || 'https://microsoft.com/link';
			const interval = Math.max(3, info.interval || 5);
			_openModal([
				'<div class="modal-title">' + esc(t('set.microsoftDeviceTitle')) + '</div>',
				'<div class="page-sub">' + esc(t('set.microsoftDeviceDesc')) + '</div>',
				'<div style="text-align:center;margin:18px 0;padding:22px;background:var(--card);border:1px solid var(--border);border-radius:12px;">',
				'  <div style="font-size:13px;color:var(--muted);margin-bottom:10px;">' + esc(t('set.microsoftDeviceStep1')) + '</div>',
				'  <a href="' + esc(verifyUrl) + '" target="_blank" rel="noopener" style="color:var(--green);font-weight:600;word-break:break-all;">' + esc(verifyUrl) + '</a>',
				'  <div style="font-size:13px;color:var(--muted);margin:14px 0 8px;">' + esc(t('set.microsoftDeviceStep2')) + '</div>',
				'  <div style="font-family:var(--font-head);font-size:34px;font-weight:700;letter-spacing:8px;color:var(--text);">' + esc(userCode) + '</div>',
				'  <div style="font-size:12px;color:var(--muted);margin-top:14px;" id="msDeviceStatus">' + esc(t('set.microsoftDeviceWaiting')) + '</div>',
				'</div>',
				'<div class="modal-actions">',
				'  <button class="modal-btn" id="msDeviceCancel">' + esc(t('common.cancel')) + '</button>',
				'</div>'
			].join(''));
			document.getElementById('msDeviceCancel')?.addEventListener('click', _closeModal);
			const deadline = Date.now() + (info.expiresIn || 900) * 1000;
			let finished = false;
			while (Date.now() < deadline) {
				await new Promise(r => setTimeout(r, interval * 1000));
				const statusEl = document.getElementById('msDeviceStatus');
				if (!statusEl) break;
				try {
					const poll = await SG.api.microsoftDevicePoll();
					if (poll && poll.ok && poll.status === 'completed') { finished = true; break; }
					if (poll && poll.status === 'failed') { statusEl.textContent = t('set.microsoftDeviceFailed') + (poll.message || ''); break; }
				} catch (pollErr) {
					statusEl.textContent = t('set.microsoftDeviceFailed') + (pollErr.message || '');
					break;
				}
			}
			_closeModal();
			if (finished) {
				await _loadAccounts(true);
				_invalidate('skins');
				await rerenderInPlace();
				_refreshSidebarAccount();
				_toast(t('set.microsoftSuccess'));
			}
		} catch (e) {
			_toast(t('set.microsoftFailed') + (e.message || t('common.unknownError')));
		} finally {
			const b = document.getElementById('view-settings')?.querySelector('#btnMsLogin');
			if (b) { b.disabled = false; b.textContent = t('set.microsoftLogin'); }
		}
	});
	// 外置登录（皮肤站预设）
	el.querySelector('#btnAuthlibLogin')?.addEventListener('click', () => {
		const presetOptions = AUTH_PRESETS.map((p, i) =>
			`<button class="auth-preset ${i === 0 ? 'active' : ''}" data-url="${esc(p.url)}" data-desc="${esc(p.desc)}">${esc(p.name)}<span class="auth-preset-desc">${esc(p.desc)}</span></button>`).join('');
		_openModal(`
			<div class="modal-title">${esc(t('set.authlibTitle'))}</div>
			<div class="page-sub">${esc(t('set.authlibDesc'))}</div>
			<div class="auth-presets" id="authPresets">${presetOptions}</div>
			<div class="modal-field">
				<span class="modal-label">${esc(t('set.authServerLabel'))}</span>
				<input class="modal-input" id="mAuthUrl" value="https://littleskin.cn" placeholder="https://example.com">
			</div>
			<div class="modal-field">
				<span class="modal-label">${esc(t('set.authUserLabel'))}</span>
				<input class="modal-input" id="mAuthUser" placeholder="用户名 / 邮箱">
			</div>
			<div class="modal-field">
				<span class="modal-label">${esc(t('set.authPassLabel'))}</span>
				<input class="modal-input" id="mAuthPass" type="password" placeholder="••••••••">
			</div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="mOk">${esc(t('set.add'))}</button>
			</div>`);
		document.getElementById('mCancel').onclick = _closeModal;
		const presetWrap = document.getElementById('authPresets');
		if (presetWrap) {
			presetWrap.querySelectorAll('.auth-preset').forEach(p => p.addEventListener('click', () => {
				presetWrap.querySelectorAll('.auth-preset').forEach(x => x.classList.remove('active'));
				p.classList.add('active');
				document.getElementById('mAuthUrl').value = p.dataset.url;
			}));
		}
		document.getElementById('mOk').onclick = async () => {
			const serverUrl = document.getElementById('mAuthUrl').value.trim();
			const userName = document.getElementById('mAuthUser').value.trim();
			const password = document.getElementById('mAuthPass').value;
			if (!serverUrl || !userName || !password) { _toast(t('set.authFieldsRequired')); return; }
			_closeModal();
			try {
				await SG.api.authlibLogin(serverUrl, userName, password);
				await _loadAccounts(true);
				_invalidate('skins');
				await rerenderInPlace();
				_refreshSidebarAccount();
				_toast(t('set.authlibSuccess', { name: userName }));
			} catch (e) { _toast(t('set.authlibFailed') + (e.message || t('common.unknownError'))); }
		};
	});
	el.querySelector('#btnSignOut')?.addEventListener('click', () => {
		if (ACCOUNTS.length <= 1) { _toast(t('set.onlyOneAccount')); return; }
		const cur = ACCOUNTS.findIndex(a => a.selected);
		const next = ACCOUNTS[(cur + 1) % ACCOUNTS.length];
		SG.api.selectAccount(next.id).then(async () => {
			await _loadAccounts(true);
			_invalidate('skins');
			await rerenderInPlace();
			_refreshSidebarAccount();
			_toast(t('set.switchedTo', { name: next.userName }));
		}).catch(err => _toast(t('set.switchFailed') + (err.message || '')));
	});
	el.querySelector('#btnAddAccount')?.addEventListener('click', () => {
		_openModal(`
			<div class="modal-title">${esc(t('set.addOfflineTitle'))}</div>
			<div class="page-sub">${esc(t('set.addOfflineDesc'))}</div>
			<div class="modal-field"><span class="modal-label">${esc(t('set.playerNameLabel'))}</span><input class="modal-input" id="mAccName" placeholder="Player"></div>
			<div class="modal-actions">
				<button class="modal-btn ghost" id="mCancel">${esc(t('common.cancel'))}</button>
				<button class="modal-btn primary" id="mOk">${esc(t('set.add'))}</button>
			</div>`);
		document.getElementById('mCancel').onclick = _closeModal;
		document.getElementById('mOk').onclick = async () => {
			const name = document.getElementById('mAccName').value.trim();
			if (!name) { _toast(t('set.playerNameRequired')); return; }
			_closeModal();
			try {
				await SG.api.addOfflineAccount(name);
				await _loadAccounts(true);
				_invalidate('skins');
				await rerenderInPlace();
				_refreshSidebarAccount();
				_toast(t('set.accountAdded', { name }));
			} catch (e) { _toast(t('set.addFailed') + (e.message || t('common.unknownError'))); }
		};
	});

	// 账号卡片：点击切换账户	// 账号卡片：点击切换账户
	el.querySelectorAll('.account-card').forEach(card => {
		card.addEventListener('click', async e => {
			if (e.target.closest('.account-card-del')) return;
			const id = card.dataset.account;
			const target = ACCOUNTS.find(a => a.id === id);
			if (!target || target.selected) return;
			try {
				await SG.api.selectAccount(id);
				await _loadAccounts(true);
				_invalidate('skins');
				await rerenderInPlace();
				_refreshSidebarAccount();
				_toast(t('set.switchedTo', { name: target.userName }));
			} catch (err) { _toast(t('set.switchFailed') + (err.message || '')); }
		});
	});
	// 删除账户
	el.querySelectorAll('.account-card-del').forEach(btn => {
		btn.addEventListener('click', async e => {
			e.stopPropagation();
			const id = btn.dataset.del;
			const target = ACCOUNTS.find(a => a.id === id);
			if (!target) return;
			if (!confirm(t('set.deleteConfirm', { name: target.userName }))) return;
			try {
				await SG.api.deleteAccount(id);
				await _loadAccounts(true);
				_invalidate('skins');
				await rerenderInPlace();
				_refreshSidebarAccount();
				_toast(t('set.accountDeleted', { name: target.userName }));
			} catch (err) { _toast(t('set.deleteFailed') + (err.message || '')); }
		});
	});	_addUpdateBindings(el);
}
// GENSETTINGS-BIND-END

const SG_NS = SG;
SG_NS.settings = {
	render: renderSettings,
	bind: bindSettings,
	rerenderInPlace,
	__wire: wire,
	// 纯函数区（无 DOM / 无网络 / 无状态）：供 node:test 直接断言
	core: { AUTH_PRESETS, sourceLabel },
};

if (typeof module === 'object' && module.exports) module.exports = SG_NS.settings.core;
})(typeof window === 'undefined' ? null : window);
