'use strict';
/*
 * MR20：自更新失效与恢复 —— 文案与接线契约测试
 * --------------------------------------------------------------------------
 * 背景：替换脚本是**退出启动器之后**才跑的。原实现直接覆盖 exe，没有备份、没有校验、没有回滚，
 * 界面也无从知道"上次到底替换成功没有"——用户下次打开看到的仍是旧版本，却以为更新完成了。
 *
 * 覆盖范围（明确边界，不声称端到端）：
 *   A. i18n 行为：中英两侧都能取到新键。
 *   B. api.js 行为：getUpdateStatus 必须把 trustNote / lastApplyResult 透给界面。
 *   C. 静态扫描（补充防线）：界面会展示"上次替换未成功"，且只在 applied === false 时才告警；
 *      信任链声明（SHA256 不等于身份认证）也会展示。
 *
 * 不覆盖：真实替换脚本执行（那是 C# 侧 UpdateRecoveryTests 的 3 个真实执行用例）、真实重启。
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
	'set.updateTrustNote', 'set.lastApplyFailed', 'set.lastApplyFailedHint', 'set.lastApplyVersion',
];

test('MR20: i18n 在 zh 与 en 两侧都提供更新恢复相关键', () => {
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

test('MR20: "上次替换未成功"的文案必须说清"仍是旧版本"，不能含糊成"更新失败"', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		const failed = sg.t('set.lastApplyFailed', { result: 'verify_failed' });
		const hint = sg.t('set.lastApplyFailedHint');
		assert.ok(failed.includes('verify_failed'), `${loc} 的告警必须带出具体结果码`);
		assert.ok(hint.length > 0 && hint !== 'set.lastApplyFailedHint');
	}
});

/* ============== B. api.js 行为 ============== */

test('MR20: getUpdateStatus 把 trustNote 与 lastApplyResult 透给界面', async () => {
	const { api, stub } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({
			state: 'idle',
			currentVersion: '1.0.0',
			trustNote: 'SHA256 校验用于发现下载损坏或截断，不提供发布者身份认证；',
			lastApplyResult: { result: 'verify_failed', applied: false, version: '1.0.1' },
		}),
		text: () => Promise.resolve(''),
	}));

	const s = await api.getUpdateStatus();
	assert.ok(s.trustNote.length > 0, 'trustNote 必须透出');
	assert.equal(s.lastApplyResult.applied, false);
	assert.equal(s.lastApplyResult.result, 'verify_failed');

	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/update'), `URL 必须是更新状态接口：${call.url}`);
});

test('MR20: 服务端没给这两个字段时也不能崩（归一为默认值）', async () => {
	const { api } = loadApi(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({ state: 'idle', currentVersion: '1.0.0' }),
		text: () => Promise.resolve(''),
	}));

	const s = await api.getUpdateStatus();
	assert.equal(s.trustNote, '');
	assert.equal(s.lastApplyResult, null);
});

/* ============== C. 静态扫描（补充防线） ============== */

test('MR20 static scan: 界面展示上次替换结果与信任链声明，且只在 applied===false 时告警', () => {
	assert.ok(LAUNCHER_JS.includes('function renderUpdateTrust('), '必须存在 renderUpdateTrust');
	const start = LAUNCHER_JS.indexOf('function renderUpdateTrust(');
	const block = LAUNCHER_JS.slice(start, start + 1400);

	assert.ok(block.includes('s.lastApplyResult'), '必须读取 lastApplyResult');
	assert.ok(block.includes('last.applied === false'), '只在 applied===false 时告警（成功时不能吓用户）');
	assert.ok(block.includes("t('set.lastApplyFailed'"), '必须展示"上次替换未成功"');
	assert.ok(block.includes("t('set.updateTrustNote'"), '必须展示信任链声明');
	assert.ok(block.includes('s.trustNote'), '信任链声明来自服务端，不能本地硬编码');

	// 初始化时必须调用它
	const init = LAUNCHER_JS.slice(LAUNCHER_JS.indexOf('function initUpdateSection('),
		LAUNCHER_JS.indexOf('function initUpdateSection(') + 700);
	assert.ok(init.includes('renderUpdateTrust(el, s)'), '初始化更新区时必须渲染替换结果');

	// 容器必须存在
	assert.ok(LAUNCHER_JS.includes('id="updateTrust"'), '设置页必须有 updateTrust 容器');
});

/* ============== C. v1.0.5 补齐的三个用户可见状态 ============== */

const V105_KEYS = [
	'set.applyingUpdate', 'set.updateRetry', 'set.lastApplySucceeded', 'set.lastApplySucceededHint',
];

test('v1.0.5: i18n 在 zh 与 en 两侧都提供「正在替换 / 重试 / 已成功」三个状态的文案', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		for (const key of V105_KEYS) {
			const value = sg.t(key);
			assert.equal(typeof value, 'string', `${loc} 的 ${key} 应当是字符串`);
			assert.ok(value.length > 0 && value !== key, `${loc} 缺少键 ${key}`);
		}
	}
});

test('v1.0.5: 「上次已成功」的文案必须带上目标版本号，且不能与失败文案共用一套话术', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		const ok = sg.t('set.lastApplySucceeded', { version: '1.0.5' });
		const failed = sg.t('set.lastApplyFailed', { result: 'verify_failed' });
		assert.ok(ok.includes('1.0.5'), `${loc} 的成功提示必须带出版本号：${ok}`);
		assert.notEqual(ok, failed, `${loc} 的成功与失败文案不能相同`);
	}
});

test('v1.0.5: 界面必须有 applying 分支、失败重试入口与成功回显（静态扫描）', () => {
	// 这三条是 v1.0.5 的 RED 3：原来
	//   - 没有 state === 'applying' 分支 → 点完"立即重启更新"后界面停在"已下载"，
	//     用户不知道它是否已经开始替换；
	//   - failed 只有一行红字，没有任何重试入口；
	//   - lastApplyResult.applied === true 时完全静默，用户无法确认"上次真的换成新版了"。
	assert.ok(/state === 'applying'/.test(LAUNCHER_JS), "initUpdateSection 必须有 applying 分支");
	assert.ok(/last\.applied === true/.test(LAUNCHER_JS), "renderUpdateTrust 必须有成功分支");
	assert.ok(/id="btnRetryUpdate"/.test(LAUNCHER_JS), "失败状态必须给重试按钮");
	assert.ok(/btnApplyUpdate'\)\?\.addEventListener[\s\S]{0,400}btn\.disabled = true/.test(LAUNCHER_JS),
		"点『立即重启更新』后必须禁用按钮，避免连点触发两次 apply（服务端已有守卫，前端并行防护）");
});

test('v1.0.5: 失败重试走的是同一条下载入口，不另造路径', () => {
	// 重试必须复用 SG.api.startUpdateDownload（与首次下载同一 API），
	// 防止出现"第二次下载走另一套校验"的分叉。
	const retryBlock = LAUNCHER_JS.split('function renderUpdateFailed')[1] || '';
	assert.ok(retryBlock.includes('startUpdateDownload'), '重试必须调用 startUpdateDownload');
	assert.ok(!/fetch\(/.test(retryBlock), '重试不得绕过 api.js 直接 fetch');
});

/* ============== D. v1.0.5 r3：下载中途失败必须当场给重试入口 ============== */

test('v1.0.5-r3: pollUpdateProgress 的失败分支必须走 renderUpdateFailed（不能只写一行红字）', () => {
	// RED 6（r3 真机点出来）：下载失败当下只写 `❌ <error>`，界面上没有重试按钮。
	// r1 已经把 renderUpdateFailed 接到 initUpdateSection（页面刷新时），
	// 但实时轮询那条路径漏了，所以「下载失败」与「重进设置页」看到的东西不一致。
	const start = LAUNCHER_JS.indexOf('function pollUpdateProgress');
	assert.ok(start > 0, '找不到 pollUpdateProgress');
	const end = LAUNCHER_JS.indexOf('function ', start + 10);
	const body = LAUNCHER_JS.slice(start, end > 0 ? end : undefined);
	assert.ok(/renderUpdateFailed\(el,/.test(body),
		'pollUpdateProgress 的失败分支必须调用 renderUpdateFailed（否则用户看不到重试按钮）');
	assert.ok(!/se\.textContent = '❌ '/.test(body),
		'不得再退回「只写一行红字」的旧写法');
});

test('v1.0.5-r3: 重试按钮复用同一条下载入口，且失败后可再次点击', () => {
	const start = LAUNCHER_JS.indexOf('function renderUpdateFailed');
	assert.ok(start > 0, '找不到 renderUpdateFailed');
	const end = LAUNCHER_JS.indexOf('function ', start + 10);
	const body = LAUNCHER_JS.slice(start, end > 0 ? end : undefined);
	assert.ok(body.includes('btnRetryUpdate'), '必须有重试按钮 id');
	assert.ok(body.includes('startUpdateDownload'), '重试必须复用 startUpdateDownload');
	assert.ok(/btn\.disabled = false/.test(body), '重试失败后必须把按钮恢复可点，否则重试成了一次性');
});
