/* =========================================================================
 * G2 第 2 批：其余 22 项普通流程行为场景（第二批在同一受控夹具上运行）
 * ========================================================================= */

/* ===== 只加载 api.js 的夹具：验证已发布归一化路径（不改产品实现） ===== */
function loadApiOnly(fetchHandler) {
	const { document, elementsById } = makeDomStub();
	// api.js 在加载时会把 SG.apiToken 覆盖为页面里的会话 meta；没有 meta 就永远是空串。
	// 这里按真实 index.html 的结构放一个受控 meta，测试才能验证 token 透传。
	const meta = document.createElement('meta');
	meta.setAttribute('name', 'pineglow-session-token');
	meta.setAttribute('content', 'test-token');
	document.body.appendChild(meta);
	const ctx = {
		SG: { setLocale() {}, t: (k) => k, applyStaticI18n() {}, apiMode: 'live', apiToken: 'test-token' },
		location: { protocol: 'http:', hash: '' },
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		document,
		setTimeout, clearTimeout, setInterval, clearInterval,
		Promise, URLSearchParams, console, Date, JSON, Math, Object, Array, String, Number,
		Boolean, Error, Symbol, RegExp, Map, Set, isNaN, parseInt, parseFloat,
		fetch: fetchHandler,
		window: null,
	};
	ctx.window = ctx;
	vm.createContext(ctx);
	vm.runInContext('SG.apiMode = "live";\n' + I18N_JS, ctx);
	vm.runInContext(API_JS, ctx);
	return { ctx, api: ctx.SG.api, document, elementsById };
}

function mountView(h, id, html) {
	let el = h.document.getElementById(id);
	if (!el) {
		el = h.document.createElement('div');
		el.setAttribute('id', id);
		h.document.body.appendChild(el);
	}
	el.innerHTML = html || '';
	return el;
}

function instA() { return { id: 'i1', name: 'AlphaPack', version: '1.20.1', loader: 'Fabric', gameDirectory: 'D:\\mc\\alpha', savesCount: 3 }; }
function instB() { return { id: 'i2', name: 'BetaPack', version: '1.19.2', loader: 'Vanilla', gameDirectory: 'D:\\mc\\beta', savesCount: 0 }; }
function recOp(over) {
	return Object.assign({
		operationId: 'op-1', status: 'quarantined', createdAt: '2026-10-03T00:00:00Z',
		totalBytes: 2048, files: [{ relativePath: 'a.jar', state: 'quarantined' }],
	}, over || {});
}

/* ===================== INSTANCE-01..04：实例移除 ===================== */
test('INSTANCE-01 cancel does not issue a delete request', async () => {
	const h = loadGoalLauncher({
		getState: async () => ({ config: {}, instances: [instA(), instB()], accounts: [], java: [] }),
	}, { confirm: () => false });
	mountView(h, 'view-installations', '');
	h.T.openInstanceMenu(instA());
	await activate(h.document.getElementById('mDel'));
	assert.equal(countCalls(h, 'deleteInstance'), 0, 'cancelling must not delete');
	const view = h.document.getElementById('view-installations');
	assert.equal(view.innerHTML, '', 'cancelling must not re-render the list');
});

test('INSTANCE-02 confirm removes the instance from the list and re-renders', async () => {
	const state = { config: { selectedInstanceId: 'i1' }, instances: [instA(), instB()], accounts: [], java: [] };
	const h = loadGoalLauncher({
		getState: async () => JSON.parse(JSON.stringify(state)),
		deleteInstance: async (id) => {
			state.instances = state.instances.filter(i => i.id !== id);
			return { ok: true };
		},
	}, { confirm: () => true });
	mountView(h, 'view-installations', '');
	h.T.openInstanceMenu(instA());
	await activate(h.document.getElementById('mDel'));
	assert.equal(countCalls(h, 'deleteInstance'), 1, 'exactly one delete request');
	const view = h.document.getElementById('view-installations');
	assert.doesNotMatch(view.innerHTML, /AlphaPack/, 'the removed instance must disappear from the list');
	assert.match(view.innerHTML, /BetaPack/, 'the remaining instance must stay');
});

test('INSTANCE-03 removing the selected instance clears the stale selection', async () => {
	const state = { config: { selectedInstanceId: 'i1' }, instances: [instA(), instB()], accounts: [], java: [] };
	const h = loadGoalLauncher({
		getState: async () => JSON.parse(JSON.stringify(state)),
		deleteInstance: async (id) => {
			state.instances = state.instances.filter(i => i.id !== id);
			return { ok: true };
		},
	}, { confirm: () => true });
	mountView(h, 'view-installations', '');
	await h.T.renderInstallations();
	h.T.openInstanceMenu(instA());
	assert.equal(h.T.getSelectedInstanceId(), 'i1', 'precondition: the removed instance is selected');
	await activate(h.document.getElementById('mDel'));
	assert.notEqual(h.T.getSelectedInstanceId(), 'i1', 'the stale selection id must not survive the removal');
	assert.equal(h.T.getSelectedInstanceId(), 'i2', 'selection falls back to a surviving instance');
});

test('INSTANCE-04 repeated clicks during the request issue one request', async () => {
	let release;
	const pending = new Promise(r => { release = r; });
	const h = loadGoalLauncher({
		getState: async () => ({ config: {}, instances: [instA(), instB()], accounts: [], java: [] }),
		deleteInstance: () => pending,
	}, { confirm: () => true });
	mountView(h, 'view-installations', '');
	h.T.openInstanceMenu(instA());
	const del = h.document.getElementById('mDel');
	const first = del.onclick({ target: del, preventDefault() {}, stopPropagation() {} });
	const second = del.onclick({ target: del, preventDefault() {}, stopPropagation() {} });
	assert.equal(countCalls(h, 'deleteInstance'), 1, 'the second click while in flight must be ignored');
	release({ ok: true });
	await first;
	await second;
	await settle();
	assert.equal(countCalls(h, 'deleteInstance'), 1);
});

/* ===================== RECOVERY-02..06：恢复区 ===================== */
test('RECOVERY-02 empty recovery list shows the empty state', async () => {
	const h = loadGoalLauncher({ listRecoveries: async () => ({ retentionDays: 30, recoveries: [] }) });
	await h.T.openRecoveryPanel(instA());
	const body = h.document.getElementById('recoveryContent');
	assert.match(body.innerHTML, /recovery\.empty|恢复区为空|recovery area is empty/i, 'empty state text expected');
	assert.equal(h.document.querySelector('.rec-restore'), null, 'no restore action without a record');
});

test('RECOVERY-03 normal restore reports success and refreshes the panel', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({ retentionDays: 30, recoveries: [recOp()] }),
		restoreRecovery: async () => ({ ok: true, conflicts: 0 }),
	});
	await h.T.openRecoveryPanel(instA());
	const btn = h.document.querySelector('.rec-restore');
	assert.ok(btn, 'restore button must exist');
	await activate(btn);
	const call = h.calls.filter(c => c.name === 'restoreRecovery').pop();
	assert.ok(call, 'restoreRecovery must be called');
	assert.equal(call.args[0], 'i1');
	assert.equal(call.args[1], 'op-1');
	assert.equal(call.args[2], false, 'a plain delete record must not overwrite');
	assert.equal(countCalls(h, 'listRecoveries'), 2, 'the panel must be refreshed after the restore');
	assert.ok(h.toastLog.some(m => m === h.t('recovery.restored')), 'success toast expected, got: ' + JSON.stringify(h.toastLog));
});

test('RECOVERY-04 partial results list each file with its state', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({
			retentionDays: 30,
			recoveries: [recOp({
				files: [
					{ relativePath: 'ok.jar', state: 'restored' },
					{ relativePath: 'locked.jar', state: 'failed', error: 'file in use' },
				],
			})],
		}),
	});
	await h.T.openRecoveryPanel(instA());
	const body = h.document.getElementById('recoveryContent');
	assert.match(body.innerHTML, /ok\.jar/, 'the restored file must be listed');
	assert.match(body.innerHTML, /locked\.jar/, 'the failed file must be listed');
	assert.match(body.innerHTML, /file in use/, 'the per-file error must be shown');
});

test('RECOVERY-05 conflict result never claims a successful overwrite', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({ retentionDays: 30, recoveries: [recOp({ reason: 'mod-replace' })] }),
		restoreRecovery: async () => ({ ok: false, conflicts: 2 }),
	});
	await h.T.openRecoveryPanel(instA());
	await activate(h.document.querySelector('.rec-restore'));
	assert.ok(h.toastLog.includes(h.t('recovery.conflictNote')), 'the conflict notice must be shown, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.includes(h.t('recovery.undone')), 'a conflict must never claim the update was undone');
	assert.ok(!h.toastLog.includes(h.t('recovery.restored')), 'a conflict must never claim a restore');
});

test('RECOVERY-06 submit disables the button and failure restores it', async () => {
	let rejectRestore;
	const pending = new Promise((_res, rej) => { rejectRestore = rej; });
	const h = loadGoalLauncher({
		listRecoveries: async () => ({ retentionDays: 30, recoveries: [recOp()] }),
		restoreRecovery: () => pending,
	});
	await h.T.openRecoveryPanel(instA());
	const btn = h.document.querySelector('.rec-restore');
	btn.dispatchEvent('click', {});
	await settle(2);
	assert.equal(btn.disabled, true, 'the button must be disabled while the request is in flight');
	const err = new Error('locked-now');
	rejectRestore(err);
	await settle(8);
	assert.equal(btn.disabled, false, 'a failed restore must re-enable the button');
	assert.ok(h.toastLog.some(m => m.indexOf('locked-now') >= 0), 'the failure must be reported, got: ' + JSON.stringify(h.toastLog));
});

/* ===================== BISECT-01/02/04/05 ===================== */
test('BISECT-01 no pending session shows the normal start entry', async () => {
	const h = loadGoalLauncher({ bisectPending: async () => ({ pending: false }) });
	await h.T.openModBisect(instA());
	assert.ok(h.document.getElementById('bStart'), 'the normal start entry must be offered');
	const safety = h.document.querySelector('.bisect-safety');
	assert.ok(safety && safety.innerHTML.length > 0, 'the safety guidance must stay visible');
});

test('BISECT-02 pending session offers restore or continue', async () => {
	const h = loadGoalLauncher({
		bisectPending: async () => ({ pending: true, canContinue: true, createdAt: '2026-10-03T00:00:00Z', rounds: [1, 2], conflicts: [{ fileName: 'x.jar', detail: 'locked' }] }),
	});
	await h.T.openModBisect(instA());
	assert.ok(h.document.getElementById('bResumeRestore'), 'restore entry expected');
	const cont = h.document.getElementById('bResumeContinue');
	assert.ok(cont, 'continue entry expected');
	assert.equal(cont.disabled, false, 'continue must be enabled when the server says it can continue');

	const h2 = loadGoalLauncher({
		bisectPending: async () => ({ pending: true, canContinue: false, createdAt: '2026-10-03T00:00:00Z', rounds: [] }),
	});
	await h2.T.openModBisect(instA());
	assert.equal(h2.document.getElementById('bResumeContinue').disabled, true,
		'continue must be disabled when the server says it cannot continue');
});

test('BISECT-04 file-lock failure keeps the needs-recovery state', async () => {
	const h = loadGoalLauncher({ bisectPending: async () => ({ pending: false }) });
	await h.T.openModBisect(instA());
	const content = h.document.getElementById('bisectContent');
	h.T.drawBisect(content, instA(), {
		enabledAtStart: ['guard.jar'],
		needsRecovery: true,
		recoveryError: 'locked hard',
		lastResults: [{ fileName: 'guard.jar', state: 'failed', error: 'in use' }],
	});
	assert.ok(h.document.getElementById('bRetryRecover'), 'the retry-recovery action must be offered');
	assert.match(content.innerHTML, /guard\.jar/, 'the failing file must be listed');
	assert.match(content.innerHTML, /locked hard/, 'the recovery error must be shown');
	assert.doesNotMatch(content.innerHTML, new RegExp(h.t('bisec.restored')), 'a needs-recovery state must not claim the mods were restored');
});

test('BISECT-05 failed recovery never shows all-complete', async () => {
	const h = loadGoalLauncher({
		bisectPending: async () => ({ pending: false }),
		bisectGiveUp: async () => {
			const e = new Error('still locked');
			// 结构化失败体的形状与 api.js _normalizeBisect 的字段口径一致（含 enabledAtStart），
			// 这样 drawBisect 会走 needsRecovery 分支，而不是退化成"没有可定位的 Mod"。
			e.payload = {
				enabledAtStart: ['guard.jar'],
				needsRecovery: true, recoveryError: 'still locked',
				lastResults: [{ fileName: 'guard.jar', state: 'failed', error: 'in use' }],
			};
			throw e;
		},
	});
	await h.T.openModBisect(instA());
	const content = h.document.getElementById('bisectContent');
	h.T.drawBisect(content, instA(), { enabledAtStart: ['guard.jar'], needsRecovery: true, recoveryError: 'locked', lastResults: [] });
	await activate(h.document.getElementById('bRetryRecover'));
	assert.ok(h.document.getElementById('bRetryRecover'), 'the panel must stay in the needs-recovery state');
	assert.match(content.innerHTML, /still locked/, 'the new recovery error must be shown');
	assert.doesNotMatch(content.innerHTML, new RegExp(h.t('bisec.restored')), 'a failed recovery must never show all-complete');
	// 静态观察（不在本目标的修改范围）：若服务端结构化失败体缺少 enabledAtStart，
	// drawBisect 会先落到"没有可定位的 Mod"空态，而不是 needsRecovery 分支。
	// 本轮只记录，不改服务端协议，也不放宽该断言。
});

/* ===================== IMPORT-01..04 ===================== */
test('IMPORT-01 operationId is bound before cancel is offered', async () => {
	const seen = { cancelBtnAtRequest: null, importOpId: null, cancelOpId: null };
	const h = loadGoalLauncher({
		importInstance: async (name, b64, opId) => {
			seen.cancelBtnAtRequest = !!h.document.getElementById('impCancel');
			seen.importOpId = opId;
			const e = new Error('stop-after-record');
			throw e;
		},
		cancelImport: async (opId) => { seen.cancelOpId = opId; return { cancelled: true }; },
	});
	mountView(h, 'view-home', '');
	await h.T.handleImportFiles([{ name: 'pack.zip' }]);
	assert.equal(seen.cancelBtnAtRequest, true, 'the cancel entry must exist before the request is issued');
	assert.ok(typeof seen.importOpId === 'string' && seen.importOpId.length > 0, 'a non-empty operationId must be sent');
	await activate(h.document.getElementById('impCancel') || { dispatchEvent() {} });
});

test('IMPORT-02 cancel before acceptance reports the cancelled result', async () => {
	const h = loadGoalLauncher({
		importInstance: async () => { const e = new Error('cancelled by user'); e.cancelled = true; throw e; },
	});
	mountView(h, 'view-home', '');
	await h.T.handleImportFiles([{ name: 'pack.zip' }]);
	assert.ok(h.toastLog.includes(h.t('import.cancelled')), 'the cancelled wording is expected, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.some(m => m.indexOf(h.t('import.failed')) === 0), 'a user cancel is not a failure');
});

test('IMPORT-03 cancel after acceptance reports the real server result', async () => {
	let rejectImport;
	const pendingImport = new Promise((_res, rej) => { rejectImport = rej; });
	const h = loadGoalLauncher({
		importInstance: () => pendingImport,
		cancelImport: async () => ({ pendingCancel: true, ok: false }),
	});
	mountView(h, 'view-home', '');
	const running = h.T.handleImportFiles([{ name: 'pack.zip' }]);
	await settle(4);
	await activate(h.document.getElementById('impCancel'));
	assert.ok(h.toastLog.includes(h.t('import.cancelRecorded')), 'the recorded-cancel wording is expected, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.includes(h.t('import.cancelled')), 'it must not claim the import was already cancelled');
	rejectImport(new Error('server still working'));
	await running;
});

test('IMPORT-04 unknown or failed cancel never claims the import was cancelled', async () => {
	let rejectImport;
	const pendingImport = new Promise((_res, rej) => { rejectImport = rej; });
	const h = loadGoalLauncher({
		importInstance: () => pendingImport,
		cancelImport: async () => { throw new Error('unknown-operation'); },
	});
	mountView(h, 'view-home', '');
	const running = h.T.handleImportFiles([{ name: 'pack.zip' }]);
	await settle(4);
	await activate(h.document.getElementById('impCancel'));
	assert.ok(h.toastLog.some(m => m.indexOf(h.t('import.cancelFailed')) === 0), 'the cancel failure must be reported, got: ' + JSON.stringify(h.toastLog));
	assert.ok(!h.toastLog.includes(h.t('import.cancelled')), 'a failed cancel must never claim the import was cancelled');
	rejectImport(new Error('stop'));
	await running;
});

/* ===================== DIAGNOSIS-01/02 ===================== */
test('DIAGNOSIS-01 local diagnosis works without an API key', async () => {
	const calls = [];
	const h = loadApiOnly(async (url, init) => {
		calls.push({ url: String(url), init: init || {} });
		return {
			ok: true, status: 200,
			json: async () => ({ reportId: 'local-1', status: 'completed', summary: 'local only', reportText: 'LOCAL' }),
			text: async () => 'LOCAL',
		};
	});
	const r = await h.api.diagnoseInstance('i1', { localOnly: true });
	const body = JSON.parse(calls[0].init.body);
	assert.equal(body.localOnly, true, 'the local-only flag must reach the server');
	assert.equal(body.confirmAiSend, false, 'a local run must not ask to send anything to an AI');
	assert.equal(r.reportId, 'local-1');
	assert.equal(r.reportText, 'LOCAL');
	assert.equal(calls[0].init.headers['X-Pineglow-Token'], 'test-token');
});

test('DIAGNOSIS-02 empty history shows the empty state', async () => {
	const h = loadGoalLauncher({ diagnoseHistory: async () => ({ reports: [], notice: null }) });
	await h.T.openDiagnoseHistory(instA());
	const body = h.document.getElementById('diagHistoryContent');
	assert.match(body.innerHTML, /diag\.historyEmpty|还没有诊断历史记录|No diagnosis history/i, 'empty state text expected');
	assert.equal(h.document.querySelector('[data-export]'), null, 'no export action without a report');
});

/* ===================== UPDATE-01..04 ===================== */
function updateFixture(statusHandler) {
	const h = loadGoalLauncher({ getUpdateStatus: statusHandler });
	const host = h.document.createElement('div');
	host.setAttribute('id', 'updateHost');
	h.document.body.appendChild(host);
	host.innerHTML = '<span id="updateCurVer">…</span><div id="updateStatus" class="update-status"></div><div id="updateTrust" class="update-trust-host"></div>';
	return { h, host };
}

test('UPDATE-01 no previous apply result shows no false alarm', async () => {
	const { h, host } = updateFixture(async () => ({ state: 'idle', progress: 0 }));
	h.T.initUpdateSection(host);
	await settle(6);
	assert.equal(h.document.querySelector('.update-warn'), null, 'no warning without a failed apply result');
	const status = h.document.getElementById('updateStatus');
	assert.ok(!/✅/.test(status.textContent), 'an idle status must not claim success, got: ' + status.textContent);
});

test('UPDATE-02 a previous non-applied update is reported as still old', async () => {
	const { h, host } = updateFixture(async () => ({
		state: 'idle',
		lastApplyResult: { applied: false, result: 'verify-failed', version: '1.0.1' },
		trustNote: '',
	}));
	h.T.initUpdateSection(host);
	await settle(6);
	const warn = h.document.querySelector('.update-warn');
	assert.ok(warn, 'a failed previous apply must produce a warning');
	const warnText = textOf(warn);
	assert.match(warnText, /1\.0\.1/, 'the version that failed must be shown, got: ' + JSON.stringify(warnText));
	assert.match(warnText, new RegExp(h.t('set.lastApplyFailedHint')), 'the hint must explain that the old version is still in use, got: ' + JSON.stringify(warnText));
});

test('UPDATE-03 missing compatibility fields do not crash', async () => {
	const calls = [];
	const h = loadApiOnly(async (url, init) => {
		calls.push({ url: String(url), init: init || {} });
		return { ok: true, status: 200, json: async () => ({ state: 'idle' }), text: async () => '' };
	});
	const s = await h.api.getUpdateStatus();
	assert.equal(s.trustNote, '', 'trustNote must default to an empty string');
	assert.equal(s.lastApplyResult, null, 'lastApplyResult must default to null');
	assert.equal(s.state, 'idle');
	assert.ok(String(calls[0].url).indexOf('/api/update') >= 0);
});

test('UPDATE-04 status query failure never shows update success', async () => {
	const { h, host } = updateFixture(async () => { throw new Error('update-status-transport'); });
	h.T.initUpdateSection(host);
	await settle(6);
	const status = h.document.getElementById('updateStatus');
	const trust = h.document.getElementById('updateTrust');
	assert.ok(!/✅/.test(status.textContent), 'a failed status query must not claim success');
	assert.ok(status.textContent.indexOf(h.t('set.updateDownloaded')) < 0, 'a failed status query must not claim a downloaded update');
	assert.equal(trust.innerHTML, '', 'no trust block without server data');
	assert.ok(!/update-transport/.test(document_text_of(h)), 'the raw error must not be turned into a success claim');
});

function document_text_of(h) {
	return h.document.getElementById('updateStatus').textContent + ' ' + h.document.getElementById('updateTrust').innerHTML;
}
