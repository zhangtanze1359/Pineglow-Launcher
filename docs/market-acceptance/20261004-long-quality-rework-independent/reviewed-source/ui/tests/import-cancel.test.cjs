'use strict';
/*
 * MR11：整合包导入取消 —— 文案与接线契约测试
 * --------------------------------------------------------------------------
 * 背景：导入是长任务（要联网装 Minecraft 与加载器）。服务端加了
 * POST /api/instances/import/cancel，但如果前端不传 operationId、也没有取消按钮，
 * 这个端点就没人调用 —— 等于没有这个功能。本文件锁住这条接线。
 *
 * 覆盖范围（明确边界，不声称端到端）：
 *   A. i18n 行为：中英两侧都能取到新键。
 *   B. api.js 行为：importInstance 带 operationId；取消响应被归一化；
 *      被取消的导入会归档成 err.cancelled，而不是普通"导入失败"。
 *   C. 静态扫描（补充防线）：上传前生成 operationId、有取消按钮、取消走 cancelImport、
 *      取消后提示"未生成实例"而不是"导入失败"。
 *
 * 不覆盖：真实 WebView2 里的点击链路与真实导入（必然联网），仍为"未验证"。
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
		return Promise.resolve({
			ok: true, status: 200,
			json: () => Promise.resolve({ ok: true }),
			text: () => Promise.resolve(''),
		});
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
	Object.defineProperty(ctx, 'window', { get() { return this; }, set() { }, configurable: true });
	return { ctx, document, fetchCalls };
}

function loadI18n() {
	const stub = makeStubBrowser();
	vm.createContext(stub.ctx);
	vm.runInContext(I18N_JS, stub.ctx);
	return stub.ctx.SG;
}

function loadApi(fetchHandler) {
	const stub = makeStubBrowser({ fetchHandler });
	stub.ctx.SG.apiMode = 'live';
	stub.ctx.SG.apiToken = 'test-token';
	vm.createContext(stub.ctx);
	vm.runInContext(API_JS, stub.ctx);
	return { api: stub.ctx.SG.api, stub };
}

/* ============== A. i18n ============== */

const NEW_KEYS = [
	'import.cancel', 'import.cancelling', 'import.cancelled',
	'import.cancelledHint', 'import.cancelRecorded', 'import.cancelFailed',
	'import.outcomeUnconfirmed',
];

test('MR11: i18n 在 zh 与 en 两侧都提供导入取消相关键', () => {
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

test('MR11: "已取消"与"导入失败"必须用不同文案，不能混为一谈', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		const cancelled = sg.t('import.cancelled');
		const failed = sg.t('import.failed');
		assert.notEqual(cancelled, failed, `${loc} 的取消与失败文案不能相同`);
		assert.ok(!sg.t('import.cancelledHint').includes('失败') || loc === 'en',
			`${loc} 的取消说明不应把取消说成失败`);
	}
});

/* ============== B. api.js 行为 ============== */

test('MR11: importInstance 带上 operationId，取消时归档成 err.cancelled', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: false, status: 409,
		json: () => Promise.resolve({ ok: false, cancelled: true, error: '导入已取消，未生成实例。' }),
		text: () => Promise.resolve(''),
	}));

	let caught = null;
	try {
		await api.importInstance('pack.mrpack', 'AAAA', 'op-12345678');
	} catch (err) {
		caught = err;
	}

	assert.ok(caught, '被取消的导入必须抛错（由界面决定怎么提示）');
	assert.equal(caught.cancelled, true, '必须带 cancelled 标记，界面才能区别于"导入失败"');

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/import'), `URL 必须是导入接口：${call.url}`);
	const body = JSON.parse(call.init.body);
	assert.equal(body.operationId, 'op-12345678', 'operationId 必须发给服务端，否则无法取消');
});

test('MR11: cancelImport 走 POST /api/instances/import/cancel 并透出 pendingCancel', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({ ok: true, cancelled: false, pendingCancel: true, message: 'recorded_for_later' }),
		text: () => Promise.resolve(''),
	}));

	const res = await api.cancelImport('op-abcdefgh');
	assert.equal(res.ok, true);
	assert.equal(res.cancelled, false);
	assert.equal(res.pendingCancel, true, '取消先到时服务端会登记墓碑，界面必须能区分');
	assert.equal(res.message, 'recorded_for_later');

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/import/cancel'), `URL 必须是取消失效接口：${call.url}`);
	assert.equal((call.init.method || '').toUpperCase(), 'POST');
	assert.equal(JSON.parse(call.init.body).operationId, 'op-abcdefgh');
});

/* ============== C. 静态扫描（补充防线） ============== */

test('MR11 static scan: 上传前生成 operationId、有取消按钮、取消后提示"未生成实例"', () => {
	const start = LAUNCHER_JS.indexOf('async function handleImportFiles(');
	assert.ok(start >= 0, '必须存在 handleImportFiles');
	const block = LAUNCHER_JS.slice(start, start + 3200);

	assert.ok(block.includes('const operationId = '), '上传前必须生成 operationId');
	assert.ok(block.includes('crypto.randomUUID'), 'operationId 应当用 crypto.randomUUID 生成');
	assert.ok(block.includes('id="impCancel"'), '导入弹窗必须有取消按钮');
	assert.ok(block.includes('SG.api.cancelImport(operationId)'), '取消必须走 cancelImport');
	assert.ok(block.includes('SG.api.importInstance(file.name, b64, operationId)'), '导入必须把 operationId 传给服务端');
	assert.ok(block.includes('err.cancelled'), '界面必须识别"被取消"并区别于导入失败');
	assert.ok(block.includes("t('import.cancelledHint')"), '取消后必须提示"未生成实例"');
	// LQ-01：点击取消只是意图。终态必须由「服务端确认取消」决定，不能由 cancelRequested 决定。
	assert.ok(block.includes('cancelConfirmed'), 'LQ-01: 必须有「服务端确认取消」的独立标记');
	assert.ok(!/if \(cancelRequested \|\|/.test(block), 'LQ-01: 取消意图不得直接决定终态文案');
	assert.ok(block.includes("t('import.outcomeUnconfirmed')"), 'LQ-01: 未确认的结果必须如实说明');
});