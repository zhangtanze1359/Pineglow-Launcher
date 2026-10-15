/* ===== 加载器：真实 i18n.js + 真实 launcher.js + 可控 SG.api ===== */
function loadGoalLauncher(apiOverrides, options) {
	const opt = options || {};
	const { document, elementsById, makeEl } = makeDomStub();

	// 生产代码要求弹窗宿主与 toast 宿主存在；这里按 ui/index.html 的真实 id 建立。
	const modalHost = document.createElement('div');
	modalHost.setAttribute('id', 'modalHost');
	document.body.appendChild(modalHost);
	const toastEl = document.createElement('div');
	toastEl.setAttribute('id', 'toast');
	document.body.appendChild(toastEl);
	const toastLog = [];
	let toastText = '';
	Object.defineProperty(toastEl, 'textContent', {
		configurable: true,
		get() { return toastText; },
		set(v) { toastText = String(v == null ? '' : v); toastLog.push(toastText); },
	});

	document.activeElement = null;
	document.contains = () => true;

	const calls = [];
	const defaults = {
		async getState() { return { config: { selectedInstanceId: '' }, instances: [], accounts: [], java: [] }; },
		async getAccounts() { return { accounts: [], capes: [] }; },
		async listRecoveries() { return { retentionDays: 30, recoveries: [] }; },
		async restoreRecovery() { return { ok: true, conflicts: 0 }; },
		async bisectPending() { return { pending: false }; },
		async bisectStart() { return { enabledAtStart: [] }; },
		async bisectTest() { return { enabledAtStart: [] }; },
		async bisectGiveUp() { return { enabledAtStart: [] }; },
		async bisectResume() { return { enabledAtStart: [] }; },
		async diagnoseHistory() { return { reports: [], notice: null }; },
		async diagnoseExport() { return { ok: true, status: 200, text: 'x' }; },
		async deleteInstance() { return { ok: true }; },
		async registerInstance() { return { ok: true }; },
		async importInstance() { return { ok: true, name: 'pack.zip' }; },
		async cancelImport() { return { cancelled: true }; },
		async getUpdateStatus() { return { state: 'idle', progress: 0 }; },
	};
	const merged = Object.assign({}, defaults, apiOverrides || {});
	const api = {};
	for (const key of Object.keys(merged)) {
		const fn = merged[key];
		api[key] = async function (...args) {
			calls.push({ name: key, args });
			return fn.apply(null, args);
		};
	}

	const ctx = {
		SG: { setLocale() {}, t: (k) => k, applyStaticI18n() {} },
		location: { protocol: 'http:', hash: '' },
		history: { replaceState() {}, pushState() {} },
		navigator: { language: 'zh-CN', userLanguage: 'zh-CN', platform: 'Win32' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		document,
		setTimeout, clearTimeout, setInterval, clearInterval,
		requestAnimationFrame: (fn) => setTimeout(fn, 0),
		cancelAnimationFrame: () => {},
		Promise, URLSearchParams, console, Date, JSON, Math, Object, Array, String, Number,
		Boolean, Error, Symbol, RegExp, Map, Set, Proxy, Reflect, isNaN, parseInt, parseFloat,
		Image: function () { return { addEventListener() {}, src: '' }; },
		Blob: function () {},
		FileReader: function () {
			this.result = null;
			this.onload = null;
			this.onerror = null;
			this.readAsDataURL = () => {
				setTimeout(() => {
					this.result = 'data:application/zip;base64,QUJD';
					if (this.onload) this.onload();
				}, 0);
			};
		},
		URL: { createObjectURL() { return 'blob:goal-fake'; }, revokeObjectURL() {} },
		crypto: { randomUUID: () => 'goal-test-operation-id-0001' },
		// 生产代码用裸 confirm()；测试必须能控制它（取消路径不得发请求）。
		confirm: () => (typeof opt.confirm === 'function' ? !!opt.confirm() : true),
		window: null,
		fetch: () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({}) }),
	};
	ctx.window = ctx;
	ctx.SG.api = api;
	vm.createContext(ctx);
	vm.runInContext('SG.apiMode = "live"; SG.apiToken = "test-token";\n' + I18N_JS, ctx);
	vm.runInContext(LAUNCHER_JS, ctx);
	const T = ctx.SG.__test__;
	if (!T) throw new Error('SG.__test__ 未注入；launcher.js 加载失败');
	return { ctx, document, elementsById, makeEl, T, api, calls, toastLog, toastEl, modalHost, t: ctx.SG.t };
}

/* ===== 行为辅助 ===== */
// 受控 DOM 只把「显式赋值过 innerHTML 的节点」保留为字符串；解析出来的子节点只有 children/text。
// 因此读取解析子树的文本要用 textOf()，不能直接读 textContent/innerHTML。
function textOf(el) {
	if (!el) return '';
	const parts = [];
	(function walk(node) {
		if (node.tagName === '#text' || node.tagName === '#TEXT') { parts.push(node.textContent || ''); return; }
		if (typeof node.textContent === 'string' && node.textContent && (!node.children || !node.children.length)) {
			parts.push(node.textContent);
		}
		for (const c of (node.children || [])) walk(c);
	})(el);
	return parts.join('');
}
// 生产代码里有 setTimeout(0)（FileReader 桩、toast 清理），只清微任务不足以推进流程。
async function settle(n) {
	const count = n || 6;
	for (let i = 0; i < count; i++) {
		await Promise.resolve();
		await new Promise(r => setTimeout(r, 1));
	}
}
async function activate(el) {
	assert.ok(el, 'expected element is missing');
	el.dispatchEvent('click', {});
	if (typeof el.onclick === 'function') {
		await el.onclick({ target: el, preventDefault() {}, stopPropagation() {} });
	}
	await settle();
}
function callNames(h) { return h.calls.map(c => c.name); }
function countCalls(h, name) { return h.calls.filter(c => c.name === name).length; }

/* ===== 对照：证明受控夹具本身可用（正常路径必须通过） ===== */
test('CONTROL openRecoveryPanel renders a normal recovery record', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => ({
			retentionDays: 30,
			recoveries: [{
				operationId: 'op-1', status: 'quarantined', createdAt: '2026-10-03T00:00:00Z',
				totalBytes: 2048, files: [{ relativePath: 'a.jar', state: 'quarantined' }],
			}],
		}),
	});
	await h.T.openRecoveryPanel({ id: 'i1', name: 'Inst' });
	const body = h.document.getElementById('recoveryContent');
	assert.ok(body, 'the modal content host must exist');
	assert.match(body.innerHTML, /a\.jar/, 'the file row must be rendered');
	assert.ok(h.document.querySelector('.rec-restore'), 'the restore button must exist');
	assert.equal(countCalls(h, 'listRecoveries'), 1);
});

/* ===== RECOVERY-01：查询失败必须给出明确错误 + 重试 ===== */
test('RECOVERY-01 recovery query failure shows an error and a retry action', async () => {
	const h = loadGoalLauncher({
		listRecoveries: async () => { throw new Error('boom-transport'); },
	});
	await h.T.openRecoveryPanel({ id: 'i1', name: 'Inst' });
	assert.equal(countCalls(h, 'listRecoveries'), 1);
	const retry = h.document.getElementById('rRetry');
	assert.ok(retry, 'RECOVERY-01: the failure branch must offer a retry action');
	const body = h.document.getElementById('recoveryContent');
	assert.match(body.innerHTML, /boom-transport/, 'the real error must be shown');
	assert.equal(h.document.querySelector('.rec-restore'), null, 'no restore action on the failure branch');

	await activate(retry);
	assert.equal(countCalls(h, 'listRecoveries'), 2, 'retrying must query again');
});

/* ===== BISECT-03：pending 查询失败不得静默当作“无未完成会话” ===== */
test('BISECT-03 pending query failure shows an explicit error and retry', async () => {
	let fail = true;
	const h = loadGoalLauncher({
		bisectPending: async () => { if (fail) throw new Error('pending-transport'); return { pending: false }; },
	});
	await h.T.openModBisect({ id: 'i1', name: 'Inst', version: '1.20.1', loader: 'Fabric' });
	assert.equal(countCalls(h, 'bisectPending'), 1);
	const retry = h.document.getElementById('bPendingRetry');
	assert.ok(retry, 'BISECT-03: pending query failure must offer a retry action');
	assert.equal(h.document.getElementById('bStart'), null,
		'BISECT-03: the normal start button must NOT be shown when the previous state is unknown');
	assert.match(h.document.getElementById('bisectContent').innerHTML, /pending-transport/,
		'the real error must be shown');

	fail = false;
	await activate(retry);
	assert.equal(countCalls(h, 'bisectPending'), 2, 'retrying must query again');
	assert.ok(h.document.getElementById('bStart'), 'after the retry the normal start entry appears');
});

/* ===== DIAGNOSIS-03：历史读取失败必须给出错误 + 重试 ===== */
test('DIAGNOSIS-03 history query failure shows an error and retry', async () => {
	const h = loadGoalLauncher({
		diagnoseHistory: async () => { throw new Error('history-transport'); },
	});
	await h.T.openDiagnoseHistory({ id: 'i1', name: 'Inst' });
	assert.equal(countCalls(h, 'diagnoseHistory'), 1);
	const retry = h.document.getElementById('dhRetry');
	assert.ok(retry, 'DIAGNOSIS-03: the failure branch must offer a retry action');
	assert.match(h.document.getElementById('diagHistoryContent').innerHTML, /history-transport/,
		'the real error must be shown');

	await activate(retry);
	assert.equal(countCalls(h, 'diagnoseHistory'), 2, 'retrying must query again');
});

/* ===== DIAGNOSIS-04：非空历史必须真的渲染出来（并且能说明未包含原始证据） ===== */
test('DIAGNOSIS-04 a non-empty history renders and export states the evidence scope', async () => {
	const h = loadGoalLauncher({
		diagnoseHistory: async () => ({
			notice: null,
			reports: [{
				reportId: 'rpt-1', status: 'completed', createdAt: '2026-10-03T00:00:00Z',
				summary: 'summary text', redactionTotal: 2,
			}],
		}),
	});
	await h.T.openDiagnoseHistory({ id: 'i1', name: 'Inst' });
	const exportBtn = h.document.querySelector('[data-export="json"]');
	assert.ok(exportBtn, 'DIAGNOSIS-04: a non-empty history must render its export action');
	assert.equal(exportBtn.dataset.report, 'rpt-1');

	const cb = h.document.getElementById('dhIncludeEvidence');
	assert.ok(cb, 'the include-original-evidence checkbox must exist');
	assert.ok(!cb.checked, 'the original evidence must be excluded by default');

	await activate(exportBtn);
	const call = h.calls.filter(c => c.name === 'diagnoseExport').pop();
	assert.ok(call, 'export must call diagnoseExport');
	assert.equal(call.args[1], 'rpt-1');
	assert.equal(call.args[2], 'json');
	assert.equal(call.args[3], false, 'includeEvidence must stay false when unchecked');
});
