'use strict';
/*
 * MR08：诊断历史与导出 —— 文案与接线契约测试
 * --------------------------------------------------------------------------
 * 背景：诊断结果原本只存在于一次 HTTP 响应里，关掉弹窗就没有本地记录；
 * 想反馈问题只能手工复制日志，而日志里往往带着 token / api_key / 服务器地址。
 * MR08 增加本地脱敏历史与 Markdown/JSON 导出。
 *
 * 覆盖范围（明确边界，不声称端到端）：
 *   A. i18n 行为：中英两侧都能取到新键，且没有把状态词写成误导性文案。
 *   B. api.js 行为：diagnoseHistory 走 GET /diagnose/history；
 *      diagnoseExport 走 GET /diagnose/export 并正确带上 format / includeEvidence，
 *      且**不抛错**（失败也要能把文本与状态码交给界面）。
 *   C. 静态扫描（supplementary defense）：launcher.js 有历史入口、两个导出按钮、
 *      以及"包含证据摘录"的勾选项。
 *
 * 不覆盖：真实 WebView2 中的呈现与文件下载落盘，仍为"未验证"。
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
			text: () => Promise.resolve('# report'),
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
	'diag.history', 'diag.historyTitle', 'diag.historyEmpty', 'diag.historyFailed',
	'diag.includeEvidence', 'diag.exportMd', 'diag.exportJson', 'diag.exported',
	'diag.exportFailed', 'diag.redactionCount',
	'diag.status.completed', 'diag.status.rule_only', 'diag.status.cancelled', 'diag.status.failed',
];

test('MR08: i18n 在 zh 与 en 两侧都提供诊断历史与导出相关键', () => {
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

test('MR08: 四种诊断状态词在中英两侧互不重复（避免状态含义混淆）', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		const values = ['completed', 'rule_only', 'cancelled', 'failed'].map(s => sg.t('diag.status.' + s));
		assert.equal(new Set(values).size, 4, `${loc} 的状态词应当互不相同：${values.join('/')}`);
	}
});

/* ============== B. api.js 行为 ============== */

test('MR08: diagnoseHistory 走 GET /diagnose/history 并归一化 notice 与计数', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({
			limit: 50, retentionDays: 30, overLimitCount: 3, olderThanRetentionCount: 1,
			notice: '历史已超出建议范围',
			reports: [{ reportId: 'r1', status: 'rule_only', summary: 's', redactionTotal: 2 }],
		}),
	}));

	const res = await api.diagnoseHistory('inst-1');
	assert.equal(res.limit, 50);
	assert.equal(res.overLimitCount, 3);
	assert.ok(res.notice.length > 0);
	assert.equal(res.reports.length, 1);

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/inst-1/diagnose/history'), `URL 必须是历史接口：${call.url}`);
	assert.equal((call.init.method || 'GET').toUpperCase(), 'GET');
});

test('MR08: diagnoseExport 带上 format 与 includeEvidence，且失败不抛错', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: false, status: 400,
		json: () => Promise.resolve({ error: '诊断报告不存在' }),
		text: () => Promise.resolve('诊断报告不存在'),
	}));

	const res = await api.diagnoseExport('inst-1', 'r 1', 'json', true);
	assert.equal(res.ok, false, '导出失败不得抛错，应把状态交给界面');
	assert.equal(res.status, 400);
	assert.ok(res.text.length > 0);

	const url = stub.fetchCalls[0].url;
	assert.ok(url.includes('/api/instances/inst-1/diagnose/export'), `URL 必须是导出接口：${url}`);
	assert.ok(url.includes('format=json'), `必须带 format：${url}`);
	assert.ok(url.includes('includeEvidence=1'), `勾选时必须带 includeEvidence=1：${url}`);
	assert.ok(url.includes('reportId=r%201'), `reportId 必须被编码：${url}`);

	await api.diagnoseExport('inst-1', 'r1', 'md', false);
	assert.ok(stub.fetchCalls[1].url.includes('includeEvidence=0'), '未勾选时必须是 0');
});

/* ============== C. 静态扫描（补充防线） ============== */

test('MR08 static scan: launcher.js 有历史入口与两个导出按钮', () => {
	assert.ok(LAUNCHER_JS.includes('async function openDiagnoseHistory('), '必须存在 openDiagnoseHistory');
	const diag = LAUNCHER_JS.slice(LAUNCHER_JS.indexOf('async function openAiDiagnose('),
		LAUNCHER_JS.indexOf('async function openAiDiagnose(') + 1600);
	assert.ok(diag.includes("id=\"dHistory\""), '确认面板必须有历史入口');
	assert.ok(diag.includes('openDiagnoseHistory(inst)'), '入口必须打开历史面板');

	const start = LAUNCHER_JS.indexOf('async function openDiagnoseHistory(');
	const block = LAUNCHER_JS.slice(start, start + 3600);
	assert.ok(block.includes('SG.api.diagnoseHistory('), '面板必须调用 diagnoseHistory');
	assert.ok(block.includes('data-export="md"'), '必须有 Markdown 导出按钮');
	assert.ok(block.includes('data-export="json"'), '必须有 JSON 导出按钮');
	assert.ok(block.includes('dhIncludeEvidence'), '必须有"包含证据摘录"勾选项');
	assert.ok(block.includes('SG.api.diagnoseExport('), '导出必须走 diagnoseExport');
	assert.ok(LAUNCHER_JS.includes('async function downloadDiagnoseReport('), '必须存在下载函数');
});
