'use strict';
/*
 * MR07：Bisect 重启恢复 —— 文案与接线契约测试
 * --------------------------------------------------------------------------
 * 背景：会话原本只存在于内存 ConcurrentDictionary，进程一重启，
 * "哪些文件是本次会话禁用的、原始状态是什么"就全丢了。
 * MR07 把会话与基线落盘（游戏目录之外），并提供"恢复原状态 / 继续上一有效轮"。
 *
 * 覆盖范围（明确边界，不声称端到端）：
 *   A. i18n 行为：中英两侧都能取到新键。
 *   B. api.js 行为：bisectPending 走 GET /bisect；bisectResume 走 POST /bisect/resume 并带 action；
 *      归一化结果必须暴露 pending/roundValidity/roundIssues/sessionId。
 *   C. 静态扫描（supplementary defense）：launcher.js 在打开定位面板时先查未完成会话，
 *      并提供两个恢复入口。
 *
 * 不覆盖：真实 WebView2 中的呈现与点击链路，仍为"未验证"。
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
	'bisec.pendingTitle', 'bisec.pendingHint', 'bisec.pendingConflicts',
	'bisec.resumeRestore', 'bisec.resumeContinue',
	'bisec.roundInvalidTitle', 'bisec.roundInvalidHint',
];

test('MR07: i18n 在 zh 与 en 两侧都提供重启恢复相关键', () => {
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

/* ============== B. api.js 行为 ============== */

test('MR07: bisectPending 走 GET /bisect 并透出 conflicts/canContinue', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({
			ok: true, pending: true, sessionId: 's1', status: 'active', step: 2,
			conflicts: [{ fileName: 'a.jar', expected: 'disabled', actual: 'enabled', detail: '期望 disabled，实际 enabled' }],
			canRestore: true, canContinue: false,
		}),
	}));

	const res = await api.bisectPending('inst-1');
	assert.equal(res.pending, true);
	assert.equal(res.conflicts.length, 1);
	assert.equal(res.canContinue, false);

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/inst-1/bisect'), `URL 必须是 bisect 查询：${call.url}`);
	assert.equal((call.init.method || 'GET').toUpperCase(), 'GET');
});

test('MR07: bisectResume 走 POST /bisect/resume 并带上 action', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({
			ok: true, sessionId: 's1', step: 2, waitingForReport: true,
			roundValidity: 'limited', roundIssues: ['缺少 manifest 元数据'],
		}),
	}));

	const res = await api.bisectResume('inst-1', 'continue');
	assert.equal(res.step, 2);
	assert.equal(res.waitingForReport, true);
	assert.equal(res.roundValidity, 'limited');
	assert.equal(res.roundIssues.length, 1);

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/inst-1/bisect/resume'), `URL 必须是 resume：${call.url}`);
	assert.equal((call.init.method || '').toUpperCase(), 'POST');
	assert.equal(JSON.parse(call.init.body).action, 'continue');
});

/* ============== C. 静态扫描（补充防线） ============== */

test('MR07 static scan: launcher.js 打开定位面板时先查未完成会话，并提供两个恢复入口', () => {
	assert.ok(LAUNCHER_JS.includes('function drawBisectPending('), '必须存在 drawBisectPending');
	const start = LAUNCHER_JS.indexOf('function openModBisect(');
	const block = LAUNCHER_JS.slice(start, start + 1400);
	assert.ok(block.includes('SG.api.bisectPending('), '打开面板时必须先查询未完成会话');
	assert.ok(block.includes('drawBisectPending('), '发现未完成会话时必须渲染恢复选择');

	const pending = LAUNCHER_JS.slice(LAUNCHER_JS.indexOf('function drawBisectPending('),
		LAUNCHER_JS.indexOf('function openModBisect('));
	assert.ok(pending.includes("run('restore')"), '必须提供「恢复原状态」');
	assert.ok(pending.includes("run('continue')"), '必须提供「继续上一有效轮」');
	assert.ok(pending.includes('SG.api.bisectResume('), '两个入口都必须调用 bisectResume');
	assert.ok(pending.includes('canContinue'), '必须尊重服务端的 canContinue（有冲突时禁用继续）');
});
