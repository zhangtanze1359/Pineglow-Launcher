'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { liveLauncher } = require('./helpers/live-launcher.cjs');

test('FA01_LiveStatePreservesSavedSelection', async () => {
	const h = liveLauncher(); await h.go('home');
	assert.equal(h.T.getSelectedInstanceId(), 'b');
	assert.match(h.get('view-home').innerHTML, /Instance B/);
	await h.click(h.get('btnPlay'));
	assert(h.calls.some(r => r.path === '/api/instances/b/launch'));
	assert(!h.calls.some(r => r.path === '/api/instances/a/launch'));
});

test('FA02_LiveStatePreservesSafeConfigAndFalsyValues', async () => {
	const h = liveLauncher();
	h.raw.config.proxyPassword = 'never-expose'; h.raw.config.curseForgeApiKey = 'never-expose';
	let state = await h.api.getState();
	for (const key of ['selectedInstanceId', 'selectedAccountId', 'downloadSource', 'proxyEnabled', 'proxyHost', 'proxyPort', 'proxyUsername', 'proxyPasswordConfigured', 'newsFakeIpCompat', 'minimizeOnLaunch', 'autoCloseOnExit', 'curseForgeApiKeyConfigured'])
		assert.equal(state.config[key], h.raw.config[key], key);
	assert.equal(state.config.proxyPassword, undefined); assert.equal(state.config.curseForgeApiKey, undefined);
	Object.assign(h.raw.config, { proxyEnabled: false, proxyPort: 0, newsFakeIpCompat: false, minimizeOnLaunch: false, autoCloseOnExit: false, curseForgeApiKeyConfigured: false });
	state = await h.api.getState();
	for (const key of ['proxyEnabled', 'proxyPort', 'newsFakeIpCompat', 'minimizeOnLaunch', 'autoCloseOnExit', 'curseForgeApiKeyConfigured']) assert.equal(state.config[key], h.raw.config[key], key);
});

test('FA03_SourcePickerLoadsSelectsAndRetries', async () => {
	const h = liveLauncher(); await h.go('settings');
	await h.click(h.get('btnSourceChange'));
	assert.equal(h.document.querySelectorAll('.source-option').length, 2);
	assert(h.document.querySelector('[data-source="bmclapi"]').querySelector('.source-check'));
	await h.click(h.document.querySelector('[data-source="official"]'));
	assert.equal(h.raw.config.downloadSource, 'official');
	assert.equal(h.T.getState().config.downloadSource, 'official');
	h.failures.add('/api/sources'); await h.click(h.get('btnSourceChange'));
	assert(h.get('scRetry'));
	h.failures.clear(); await h.click(h.get('scRetry'));
	assert.equal(h.document.querySelectorAll('.source-option').length, 2);
});

test('FA04_AiToggleSynchronizesAndRollsBack', async () => {
	const h = liveLauncher(); await h.go('settings');
	let toggle = h.document.querySelector('[data-toggle="aiDiag"]');
	await h.click(toggle); assert.equal(h.T.getState().config.aiDiagnosisEnabled, true);
	assert.equal(toggle.getAttribute('aria-checked'), 'true');
	h.ctx.SG.setLocale('en'); await h.flush();
	toggle = h.document.querySelector('[data-toggle="aiDiag"]');
	assert.equal(toggle.getAttribute('aria-checked'), 'true');
	h.failures.add('/api/config'); await h.click(toggle);
	assert.equal(toggle.getAttribute('aria-checked'), 'true'); assert.equal(h.T.getState().config.aiDiagnosisEnabled, true);
	assert.match(h.get('toast').textContent, /unavailable/);
	assert(h.get('toast').textContent.startsWith(h.ctx.SG.t('set.aiToggleSaveFailed')));
	h.failures.clear(); await h.click(toggle);
	assert.equal(h.T.getState().config.aiDiagnosisEnabled, false);
	assert.equal(toggle.disabled, false);
});

test('FA05_ListFailureRetriesWithoutEmptyState', async () => {
	for (const nav of ['worlds', 'servers']) {
		const h = liveLauncher(); h.failures.add('/api/' + nav); await h.go(nav);
		const view = h.get('view-' + nav);
		assert(view.querySelector('[data-list-retry]'), nav);
		assert.equal(view.querySelector('.empty-state'), null);
		assert.match(view.innerHTML, /&lt;img/);
		h.failures.clear(); await h.click(view.querySelector('[data-list-retry]'));
		assert.equal(view.querySelector('[data-list-retry]'), null);
		assert(view.querySelector(nav === 'worlds' ? '.world-card' : '.server-card'));
	}
});

test('FA05_LateRetryCannotNavigateBack', async () => {
	let release;
	const h = liveLauncher({ respond: path => path === '/api/worlds' && release === null ? new Promise(r => { release = r; }) : undefined });
	h.failures.add('/api/worlds'); await h.go('worlds'); h.failures.clear(); release = null;
	const button = h.document.querySelector('[data-list-retry]'); assert(button, '失败列表应有可用重试');
	const retry = h.click(button);
	await h.flush(); await h.go('servers'); release({ worlds: [] }); await retry;
	assert.equal(h.T.getCurrentView(), 'servers'); assert.equal(h.get('view-servers').hidden, false);
	assert.equal(h.get('view-worlds').hidden, true);
});

test('FA02_ProxySavePreservesConfiguredValuesWithoutExposingPassword', async () => {
	const h = liveLauncher(); await h.go('settings'); await h.click(h.get('btnProxy'));
	assert.equal(h.get('proxyToggle').getAttribute('aria-checked'), 'true');
	assert.equal(h.get('pHost').value, '127.0.0.1'); assert.equal(h.get('pPort').value, '7890');
	assert.equal(h.get('pPass').value, ''); await h.click(h.get('pOk'));
	const saved = h.calls.find(r => r.path === '/api/config').body;
	assert.equal(saved.proxyEnabled, true); assert.equal(saved.proxyHost, '127.0.0.1');
	assert.equal(saved.proxyPort, 7890); assert.equal(saved.proxyUsername, 'audit-user'); assert.equal(saved.proxyPassword, '');
});

test('FA03_LateSourceResponseDoesNotReplaceBusinessDialog', async () => {
	let release;
	const h = liveLauncher({ respond: path => path === '/api/sources' ? new Promise(r => { release = r; }) : undefined });
	await h.go('settings'); const pending = h.click(h.get('btnSourceChange')); await h.flush();
	h.T.openModal('<div class="modal-title">Business dialog</div>');
	release({ selected: 'bmclapi', sources: [{ key: 'bmclapi', available: true }] }); await pending;
	assert.match(h.get('modalHost').innerHTML, /Business dialog/);
	assert.equal(h.document.querySelector('.source-option'), null);
});

test('FA03_DuplicateSourceSelectionOnlySavesOnce', async () => {
	let release;
	const h = liveLauncher({ respond: (path, init, raw) => {
		if (path !== '/api/sources/select') return;
		raw.config.downloadSource = JSON.parse(init.body).key;
		return new Promise(r => { release = r; });
	} });
	await h.go('settings'); await h.click(h.get('btnSourceChange'));
	const option = h.document.querySelector('[data-source="official"]');
	const first = h.click(option), second = h.click(option); await h.flush();
	assert.equal(h.calls.filter(r => r.path === '/api/sources/select').length, 1);
	release({ ok: true, selected: 'official', useMirror: false }); await Promise.all([first, second]);
	assert.equal(h.T.getState().config.downloadSource, 'official');
});

test('FA03_SourceSaveFailurePreservesSelectionAndAllowsRetry', async () => {
	const h = liveLauncher(); await h.go('settings'); await h.click(h.get('btnSourceChange'));
	const option = h.document.querySelector('[data-source="official"]');
	h.failures.add('/api/sources/select'); await h.click(option);
	assert.equal(h.raw.config.downloadSource, 'bmclapi');
	assert.equal(h.T.getState().config.downloadSource, 'bmclapi');
	assert.equal(option.disabled, false); assert.equal(h.get('modalHost').hidden, false);
	assert.match(h.get('toast').textContent, /unavailable/);
	h.failures.clear(); await h.click(option);
	assert.equal(h.T.getState().config.downloadSource, 'official');
});

test('FA03_LateSourceSaveDoesNotRestartNavigationOrReplaceDialog', async () => {
	let release;
	const h = liveLauncher({ respond: (path, init, raw) => {
		if (path !== '/api/sources/select') return;
		raw.config.downloadSource = JSON.parse(init.body).key;
		return new Promise(r => { release = r; });
	} });
	await h.go('settings'); await h.click(h.get('btnSourceChange'));
	const saving = h.click(h.document.querySelector('[data-source="official"]')); await h.flush();
	h.T.openModal('<div class="modal-title">Business dialog</div>');
	const generation = h.T.getNavSeq();
	release({ ok: true, selected: 'official', useMirror: false }); await saving;
	assert.equal(h.T.getNavSeq(), generation);
	assert.match(h.get('modalHost').innerHTML, /Business dialog/);
	assert.equal(h.T.getState().config.downloadSource, 'official');
});

test('FA04_PendingSaveDeduplicatesAcrossLocaleRerender', async () => {
	let release;
	const h = liveLauncher({ respond: (path, init, raw) => {
		if (path !== '/api/config') return;
		Object.assign(raw.config, JSON.parse(init.body));
		return new Promise(r => { release = r; });
	} });
	await h.go('settings'); await h.click(h.document.querySelector('[data-toggle="aiDiag"]'));
	h.ctx.SG.setLocale('en'); await h.flush();
	const control = h.document.querySelector('[data-toggle="aiDiag"]');
	assert.equal(control.disabled, true); await h.click(control);
	assert.equal(h.calls.filter(r => r.path === '/api/config').length, 1);
	release({ ok: true }); await h.flush();
	assert.equal(control.disabled, false); assert.equal(control.getAttribute('aria-checked'), 'true');
	assert.equal(h.T.getState().config.aiDiagnosisEnabled, true);
});

test('FA05_RetryFailureRemainsRetryableAndTrueEmptyIsDistinct', async () => {
	for (const nav of ['worlds', 'servers']) {
		const h = liveLauncher(); h.failures.add('/api/' + nav); await h.go(nav);
		const before = h.calls.filter(r => r.path === '/api/' + nav).length;
		const button = h.document.querySelector('[data-list-retry]');
		const first = h.click(button), repeated = h.click(button); await Promise.all([first, repeated]);
		assert.equal(h.calls.filter(r => r.path === '/api/' + nav).length, before + 1);
		assert(h.document.querySelector('[data-list-retry]'));
		assert.equal(h.document.querySelector('[data-list-retry]').disabled, false);
		const empty = liveLauncher({ respond: path => path === '/api/' + nav ? { [nav]: [] } : undefined });
		await empty.go(nav); assert(empty.get('view-' + nav).querySelector('.empty-state'));
		assert.equal(empty.document.querySelector('[data-list-retry]'), null);
	}
});

test('FA05_ListErrorTextIsEscapedAndLocalized', async () => {
	for (const locale of ['zh', 'en']) {
		const h = liveLauncher(); h.ctx.SG.setLocale(locale); h.failures.add('/api/worlds'); await h.go('worlds');
		const html = h.get('view-worlds').innerHTML;
		assert.match(html, /&lt;img src=x onerror=oops&gt;/); assert.doesNotMatch(html, /<img src=x/);
		assert(!html.includes('worlds.loadFailed')); assert(!html.includes('collection.loadFailedDesc'));
		assert.match(html, locale === 'zh' ? /无法读取存档列表/ : /Unable to load worlds/);
		for (const key of ['set.aiToggleSaveFailed', 'set.sourceUnavailable', 'servers.loadFailed', 'collection.loadFailedDesc'])
			assert.notEqual(h.ctx.SG.t(key), key, `${locale}: ${key}`);
	}
});
