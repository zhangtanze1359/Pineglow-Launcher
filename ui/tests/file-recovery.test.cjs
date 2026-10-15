'use strict';
/*
 * MR04：Mod 可恢复删除 —— 文案与接线契约测试
 * --------------------------------------------------------------------------
 * 背景：MR04 之前 `mods/{file}/delete` 走的是 ModService.DeleteMod → File.Delete，
 * 文件永久消失；而且 UI 根本没有删除入口。本包把删除改为"移入恢复区"，并补上
 * 删除入口与恢复区面板。
 *
 * 覆盖范围（明确边界，不声称端到端）：
 *   A. i18n 行为：中英两侧都能取到新键；删除确认文案必须说明"移入恢复区、可还原"，
 *      不得使用"不可撤销 / cannot be undone"这类措辞。
 *   B. api.js 行为：live 侧 listRecoveries / restoreRecovery 的 URL 与方法正确；
 *      mock 侧 deleteMod 之后能列出并恢复同一条记录（行为一致）。
 *   C. 静态扫描（supplementary defense）：launcher.js 有删除入口与恢复区面板，
 *      取消时不发请求。
 *
 * 不覆盖：恢复区在真实 WebView2 中的渲染与点击链路，仍为"未验证"。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');

/* ============== 最小浏览器桩 ============== */
function makeStubBrowser(opts) {
	opts = opts || {};
	const document = {
		head: { appendChild() {} },
		body: { appendChild() {} },
		documentElement: { setAttribute() {}, getAttribute() { return ''; } },
		querySelector() { return null; },
		createElement() { return { style: {}, setAttribute() {}, appendChild() {} }; },
		addEventListener() {},
	};
	const fetchCalls = [];
	const fetchStub = (url, init) => {
		fetchCalls.push({ url: String(url), init: init || {} });
		if (opts.fetchHandler) return opts.fetchHandler(url, init);
		return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ ok: true }) });
	};
	const ctx = {
		SG: { mock: { instances: [], mods: {}, servers: [], contentTasks: [] } },
		location: { protocol: 'http:' },
		fetch: fetchStub,
		document,
		navigator: { language: 'en-US', userLanguage: 'en-US' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		setTimeout, clearTimeout, console,
		URLSearchParams, Promise, JSON, Date, Object, Number, Math,
		fetchCalls,
	};
	Object.defineProperty(ctx, 'window', {
		get() { return this; },
		set() { },
		configurable: true,
	});
	return { ctx, document, fetchCalls };
}

function loadI18n() {
	const stub = makeStubBrowser();
	vm.createContext(stub.ctx);
	vm.runInContext(I18N_JS, stub.ctx);
	return stub.ctx.SG;
}

/* ============== A. i18n ============== */

const NEW_KEYS = [
	'mod.delete', 'mod.deleteHint', 'mod.deleteConfirm', 'mod.deleted', 'mod.deleteFailed',
	'recovery.menu', 'recovery.title', 'recovery.intro', 'recovery.empty', 'recovery.items',
	'recovery.at', 'recovery.restore', 'recovery.restored', 'recovery.partial', 'recovery.failed',
	'recovery.conflictNote', 'recovery.status.quarantined', 'recovery.status.restored',
	'recovery.status.partial', 'recovery.status.conflict', 'recovery.status.failed',
];

test('MR04: i18n 在 zh 与 en 两侧都提供 mod/恢复区 新增键', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		for (const key of NEW_KEYS) {
			const value = sg.t(key);
			assert.equal(typeof value, 'string', `${loc} 的 ${key} 应当是字符串`);
			assert.ok(value.length > 0 && value !== key, `${loc} 缺少键 ${key}`);
		}
	}
});

test('MR04: 删除确认文案必须说明可恢复，且不得声称不可撤销', () => {
	const sg = loadI18n();

	sg.setLocale('zh');
	const zh = sg.t('mod.deleteConfirm', { name: '测试实例', file: 'a.jar' });
	assert.ok(zh.includes('a.jar'), `确认文案必须带文件名：${zh}`);
	assert.ok(zh.includes('恢复区'), `确认文案必须说明移入恢复区：${zh}`);
	assert.ok(zh.includes('还原'), `确认文案必须说明可还原：${zh}`);
	assert.ok(!zh.includes('不可撤销'), `不得再出现"不可撤销"：${zh}`);
	assert.ok(!/永久|彻底删/.test(zh), `不得暗示永久删除：${zh}`);

	sg.setLocale('en');
	const en = sg.t('mod.deleteConfirm', { name: 'demo', file: 'a.jar' });
	assert.ok(en.includes('a.jar'));
	assert.ok(/recovery area/i.test(en), `英文确认文案必须提到 recovery area：${en}`);
	assert.ok(/restored/i.test(en), `英文确认文案必须提到可还原：${en}`);
	assert.ok(!/cannot be undone/i.test(en), `不得出现 cannot be undone：${en}`);
});

/* ============== B. api.js 行为 ============== */

function loadApi(fetchHandler, mode) {
	const stub = makeStubBrowser({ fetchHandler });
	if (mode === 'mock') {
		// api.js 按 location.protocol 决定 live/mock：非 http: 即 mock。
		stub.ctx.location.protocol = 'file:';
	} else {
		stub.ctx.SG.apiMode = 'live';
	}
	stub.ctx.SG.apiToken = 'test-token';
	vm.createContext(stub.ctx);
	vm.runInContext(API_JS, stub.ctx);
	return { api: stub.ctx.SG.api, stub };
}

test('MR04: live 侧 listRecoveries 走 GET /recovery', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({
			retentionDays: 30,
			recoveries: [{ operationId: 'op-1', status: 'quarantined', files: [{ relativePath: 'mods/a.jar', state: 'quarantined' }] }],
		}),
	}));

	const res = await api.listRecoveries('inst-1');
	assert.equal(res.retentionDays, 30);
	assert.equal(res.recoveries.length, 1);
	assert.equal(res.recoveries[0].operationId, 'op-1');
	assert.equal(res.recoveries[0].files[0].relativePath, 'mods/a.jar');

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/inst-1/recovery'), `URL 必须是 recovery 列表：${call.url}`);
	assert.equal((call.init.method || 'GET').toUpperCase(), 'GET');
});

test('MR04: live 侧 restoreRecovery 走 POST /recovery/{op}/restore 并透出冲突数', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({
			ok: false, status: 'conflict', conflicts: 1,
			files: [{ relativePath: 'mods/a.jar', state: 'conflict' }],
		}),
	}));

	const res = await api.restoreRecovery('inst-1', 'op-1');
	assert.equal(res.ok, false);
	assert.equal(res.conflicts, 1);
	assert.equal(res.files[0].state, 'conflict');

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/inst-1/recovery/op-1/restore'), `URL 必须指向 restore：${call.url}`);
	assert.equal((call.init.method || '').toUpperCase(), 'POST');
});

test('MR04: mock 侧删除后可列出并恢复同一条记录（行为与 live 一致）', async () => {
	const { api } = loadApi(null, 'mock');
	assert.equal(api._implMode, 'mock', 'file: 协议下应当是 mock 实现');

	await api.deleteMod('inst-m', 'a.jar');
	const afterDelete = await api.listRecoveries('inst-m');
	assert.equal(afterDelete.recoveries.length, 1, '删除后应当有一条可恢复记录');
	const op = afterDelete.recoveries[0].operationId;
	assert.equal(afterDelete.recoveries[0].files[0].state, 'quarantined');

	const restored = await api.restoreRecovery('inst-m', op);
	assert.equal(restored.status, 'restored');
	const afterRestore = await api.listRecoveries('inst-m');
	assert.equal(afterRestore.recoveries[0].status, 'restored');
	assert.equal(afterRestore.recoveries[0].files[0].state, 'restored');
});

/* ============== C. 静态扫描（补充防线） ============== */

test('MR04 static scan: 新版 Mod 抽屉使用独立行内确认入口', () => {
	const start = LAUNCHER_JS.indexOf('async function openModManager(');
	const block = LAUNCHER_JS.slice(start, LAUNCHER_JS.indexOf('async function openRecoveryPanel(',start));
	assert.ok(block.includes('data-cancel-remove'), '行内确认提供取消');
	assert.ok(block.includes("strip.querySelector('[data-cancel-remove]').focus()"), '初始焦点给取消');
	assert.ok(block.includes('if(b.dataset.removeFile)'), '真实删除由独立确认按钮触发');
	assert.ok(block.includes('await SG.api.deleteMod('), '确认后调用真实 API');
	assert.ok(block.includes("t('mod.deleted')"), '成功提示仍说明移入恢复区');
});

test('MR04 static scan: launcher.js 提供恢复区面板，且渲染与恢复都走 api', () => {
	assert.ok(LAUNCHER_JS.includes('async function openRecoveryPanel('), '必须存在 openRecoveryPanel');
	assert.ok(LAUNCHER_JS.includes("id=\"mRecovery\""), '实例菜单必须有恢复区入口');
	assert.ok(LAUNCHER_JS.includes('openRecoveryPanel(inst)'), '入口必须打开面板');

	const start = LAUNCHER_JS.indexOf('async function openRecoveryPanel(');
	const block = LAUNCHER_JS.slice(start, LAUNCHER_JS.indexOf('function drawBisectPending(', start));
	assert.ok(block.includes('SG.api.listRecoveries('), '面板必须调用 listRecoveries');
	assert.ok(block.includes('SG.api.restoreRecovery('), '面板必须调用 restoreRecovery');
	assert.ok(block.includes("t('recovery.conflictNote')"), '冲突必须提示"未覆盖"');
	assert.ok(block.includes("t('recovery.empty')"), '空态必须有文案');
});

/* ============== D. MR05：替换/更新的撤销语义 ============== */

test('MR05: i18n 在 zh 与 en 两侧都提供撤销更新相关键', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		for (const key of ['recovery.reasonReplace', 'recovery.undoReplace', 'recovery.undone']) {
			const value = sg.t(key);
			assert.equal(typeof value, 'string', `${loc} 的 ${key} 应当是字符串`);
			assert.ok(value.length > 0 && value !== key, `${loc} 缺少键 ${key}`);
		}
	}
});

test('MR05: live 侧 restoreRecovery 把 overwrite 显式传给服务端', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({ ok: true, status: 'restored', overwrite: true, conflicts: 0, files: [] }),
	}));

	await api.restoreRecovery('inst-1', 'op-1', true);
	const withFlag = JSON.parse(stub.fetchCalls[0].init.body);
	assert.equal(withFlag.overwrite, true, '撤销更新时必须显式 overwrite=true');

	await api.restoreRecovery('inst-1', 'op-1');
	const withoutFlag = JSON.parse(stub.fetchCalls[1].init.body);
	assert.equal(withoutFlag.overwrite, false, '默认必须是不覆盖');
});

test('MR05: mock 侧重复恢复默认报冲突，显式 overwrite 才成功', async () => {
	const { api } = loadApi(null, 'mock');

	await api.deleteMod('inst-r', 'a.jar');
	const op = (await api.listRecoveries('inst-r')).recoveries[0].operationId;

	const first = await api.restoreRecovery('inst-r', op);
	assert.equal(first.ok, true, '第一次恢复应当成功');
	assert.equal(first.status, 'restored');

	const second = await api.restoreRecovery('inst-r', op);
	assert.equal(second.ok, false, '默认不覆盖：已经恢复过的记录再恢复应报冲突');
	assert.equal(second.status, 'conflict');

	const forced = await api.restoreRecovery('inst-r', op, true);
	assert.equal(forced.ok, true, '显式 overwrite 应当成功');
	assert.equal(forced.overwrite, true);
});

test('MR05 static scan: launcher.js 恢复区对 mod-replace 记录显示"撤销本次更新"并带 overwrite', () => {
	const start = LAUNCHER_JS.indexOf('async function openRecoveryPanel(');
	const block = LAUNCHER_JS.slice(start, LAUNCHER_JS.indexOf('function drawBisectPending(', start));

	assert.ok(block.includes("op.reason === 'mod-replace'"), '必须区分 mod-replace 记录');
	assert.ok(block.includes('data-overwrite='), '必须把 overwrite 意图写进 DOM');
	assert.ok(block.includes("t('recovery.undoReplace')"), '替换记录必须显示"撤销本次更新"');
	assert.ok(/restoreRecovery\(inst\.id, operationId, overwrite\)/.test(block), '必须把 overwrite 传给 api');
	assert.ok(block.includes("t('recovery.undone')"), '撤销成功必须有独立提示');
});
