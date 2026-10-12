'use strict';
/*
 * MR03：实例"从列表移除"的文案与接线契约测试
 * --------------------------------------------------------------------------
 * 背景（本轮独立复核）：后端 `HandleDeleteInstanceAsync` 只从 config.Instances
 * 移除记录，**从不删除游戏目录/存档/Mod**；但 UI 文案写的是
 *   'inst.delete': '删除实例'
 *   'inst.deleteConfirm': '删除实例「{name}」？此操作不可撤销。'
 * 这会让用户以为存档已被删除 —— 属于"不能在 UI 暗示已删除文件"的硬要求。
 *
 * 覆盖范围（明确边界，不声称端到端）：
 *   A. i18n 行为：中英两边都能取到新键，且删除了"不可撤销"这类误导措辞；
 *      确认文案必须带上目录与存档数量占位符。
 *   B. api.js 行为：registerInstance 在 live 模式下 POST 到 /api/instances/register，
 *      非 ok 响应必须抛错（不能静默成功）。
 *   C. 静态扫描（supplementary defense）：launcher.js 的确认流程必须传 dir/saves，
 *      并处理 sharedGameDirectory。
 *
 * 不覆盖：真实 WebView2 里 confirm() 的实际呈现、图形界面的点击链路，这些仍是"未验证"。
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
		SG: {},
		location: { protocol: 'http:' },
		fetch: fetchStub,
		document,
		navigator: { language: 'en-US', userLanguage: 'en-US' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		setTimeout, clearTimeout, console,
		URLSearchParams, Promise, JSON,
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

/* ============== A. i18n 文案 ============== */

const NEW_KEYS = [
	'inst.delete',
	'inst.deleteConfirm',
	'inst.deleteSharedWarn',
	'inst.deleted',
	'inst.deleteFailed',
	'inst.addExisting',
	'inst.addExistingTitle',
	'inst.addExistingHint',
	'inst.addExistingDirLabel',
	'inst.addExistingDirPlaceholder',
	'inst.addExistingNameLabel',
	'inst.addExistingSubmit',
	'inst.addExistingAdded',
	'inst.addExistingFailed',
	'inst.addExistingDirRequired',
];

test('MR03: i18n 在 zh 与 en 两侧都提供新增键，且取值非空', () => {
	const sg = loadI18n();
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		for (const key of NEW_KEYS) {
			const value = sg.t(key);
			assert.equal(typeof value, 'string', `${loc} 的 ${key} 应当是字符串`);
			assert.ok(value.length > 0, `${loc} 的 ${key} 不能为空`);
			assert.notEqual(value, key, `${loc} 缺少键 ${key}`);
		}
	}
});

test('MR03: 中英确认文案不再声称不可撤销，且必须带目录与存档占位符', () => {
	const sg = loadI18n();

	sg.setLocale('zh');
	const zh = sg.t('inst.deleteConfirm', { name: '测试实例', dir: 'D:\\mc', saves: '3' });
	assert.ok(!zh.includes('不可撤销'), `中文确认文案不得再写"不可撤销"：${zh}`);
	assert.ok(zh.includes('D:\\mc'), `中文确认文案必须显示目录：${zh}`);
	assert.ok(zh.includes('3'), `中文确认文案必须显示存档数量：${zh}`);
	assert.ok(zh.includes('不删除') || zh.includes('仅移除'), `中文确认文案必须说明文件保留：${zh}`);

	sg.setLocale('en');
	const en = sg.t('inst.deleteConfirm', { name: 'demo', dir: 'D:\\mc', saves: '3' });
	assert.ok(!/cannot be undone/i.test(en), `英文确认文案不得再写 "cannot be undone"：${en}`);
	assert.ok(en.includes('D:\\mc'), `英文确认文案必须显示目录：${en}`);
	assert.ok(en.includes('3'), `英文确认文案必须显示存档数量：${en}`);

	sg.setLocale('zh');
	assert.equal(sg.t('inst.delete'), '从列表移除', '按钮文案必须是"从列表移除"');
	assert.ok(!sg.t('inst.delete').includes('删除实例'), '不得再用"删除实例"作为按钮文案');
	sg.setLocale('en');
	assert.equal(sg.t('inst.delete'), 'Remove from List');
});

/* ============== B. api.js live 行为 ============== */

function loadApiLive(fetchHandler) {
	const stub = makeStubBrowser({ fetchHandler });
	stub.ctx.SG.apiMode = 'live';
	stub.ctx.SG.apiToken = 'test-token';
	vm.createContext(stub.ctx);
	vm.runInContext(API_JS, stub.ctx);
	return { api: stub.ctx.SG.api, stub };
}

test('MR03: live 模式 registerInstance POST 到 /api/instances/register 并带上目录与名称', async () => {
	const { api, stub } = loadApiLive(() => Promise.resolve({
		ok: true, status: 200,
		json: () => Promise.resolve({ ok: true, instance: { id: 'reg-1', name: '挂回' } }),
	}));

	const res = await api.registerInstance('D:\\Games\\.minecraft', '挂回');
	assert.equal(res.ok, true);

	assert.equal(stub.fetchCalls.length, 1, '只应发出一次请求');
	const call = stub.fetchCalls[0];
	assert.ok(call.url.includes('/api/instances/register'), `URL 必须是 register 路由，实际：${call.url}`);
	assert.equal((call.init.method || '').toUpperCase(), 'POST');
	const body = JSON.parse(call.init.body);
	assert.equal(body.gameDirectory, 'D:\\Games\\.minecraft');
	assert.equal(body.name, '挂回');
});

test('MR03: live 模式 registerInstance 在非 ok 响应时必须抛错，不能静默成功', async () => {
	const { api } = loadApiLive(() => Promise.resolve({
		ok: false, status: 400,
		json: () => Promise.resolve({ ok: false, error: '目录不存在，无法挂回：D:\\nope' }),
	}));

	await assert.rejects(
		() => api.registerInstance('D:\\nope', ''),
		/directory|目录|挂回|400/i,
		'非 ok 响应必须抛错'
	);
});

/* ============== C. 静态扫描（补充防线） ============== */

test('MR03 static scan: launcher.js 的移除确认必须传 dir 与 saves，并处理 sharedGameDirectory', () => {
	const start = LAUNCHER_JS.indexOf("document.getElementById('mDel').onclick");
	assert.ok(start > 0, 'launcher.js 必须存在实例菜单的 mDel 处理');
	const block = LAUNCHER_JS.slice(start, start + 1200);

	assert.ok(/inst\.deleteConfirm[^)]*dir/.test(block), '确认文案必须传入 dir');
	assert.ok(/inst\.deleteConfirm[^)]*saves/.test(block), '确认文案必须传入 saves');
	assert.ok(block.includes('sharedGameDirectory'), '必须消费 sharedGameDirectory 以提示共享目录');
	assert.ok(block.includes("t('inst.deleteSharedWarn')"), '共享目录提示必须使用 i18n 键');
	assert.ok(/if \(!confirm\(msg\)\) return;/.test(block), '用户取消时必须直接返回，不发请求');
});

test('MR03 static scan: launcher.js 提供"从已有目录添加"入口并调用 registerInstance', () => {
	assert.ok(LAUNCHER_JS.includes("id=\"btnAddExisting\""), '安装管理页必须有 btnAddExisting 入口');
	assert.ok(LAUNCHER_JS.includes('SG.api.registerInstance('), '必须调用 SG.api.registerInstance');
	assert.ok(LAUNCHER_JS.includes("t('inst.addExistingHint')"), '必须显示"不会改动文件"的说明');
});

test('MR03 static scan: api.js mock 侧同样提供 registerInstance（只新增记录）', () => {
	const idx = API_JS.lastIndexOf('async registerInstance(');
	assert.ok(idx > 0, 'api.js 必须存在 registerInstance');
	const liveIdx = API_JS.indexOf('async registerInstance(');
	assert.notEqual(liveIdx, idx, 'live 与 mock 两侧都必须有 registerInstance');
	const mockBlock = API_JS.slice(idx, idx + 600);
	assert.ok(mockBlock.includes('SG.mock.instances.push'), 'mock 侧只应新增列表记录');
	assert.ok(!/unlink|rmdir|deleteFile/i.test(mockBlock), 'mock 侧不得引入文件删除语义');
});
