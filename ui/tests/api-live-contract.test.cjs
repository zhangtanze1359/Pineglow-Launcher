'use strict';
/*
 * api-live-contract.test.cjs —— 补齐 api.js 的 **live 通道 URL 契约**。
 *
 * 为什么需要它（2026-11-06 未覆盖路径体检结论）：
 *   统计 ui/tests 里实际出现的 `SG.api.<method>` 只有 **17 个**，而 ui/js/launcher.js 与
 *   playtime.js 里有 **59 个**被真实调用；**42 个方法从未被任何测试触达**。
 *   已有 api-contract.test.cjs 只锁了 getState / getDownloads / searchContent 三个，
 *   其余全靠"没报错就等于对"。
 *
 *   风险不在"会不会崩"，而在**静默的路由漂移**：前端把 URL 写成 /api/instance（少个 s）、
 *   把方法从 POST 写成 GET、把路径参数忘了 encodeURIComponent —— 单测全绿，
 *   只有真机点下去才 404。本文件用受控 fetch 把每个方法的**方法 + 路径 + 查询串 + body**
 *   逐个钉住，任何一处漂移都会红。
 *
 * 做法：用 node:vm 在沙箱里加载真实 api.js（不做任何改写），注入一个记录型 fetch，
 * 逐方法调用后断言 URL 形状。不联外网、不启动 EXE。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const API_SRC = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');

/**
 * 在沙箱里加载 api.js 并返回 { api, calls }。
 * 记录型 fetch 一律返回空成功体，这样每个方法都能走完它自己的后处理。
 */
function loadApi(options = {}) {
	const calls = [];
	const sandbox = {
		console,
		TextEncoder,
		TextDecoder,
		URL: Object.assign(function (u, b) { return new (require('node:url').URL)(u, b); }, {
			createObjectURL: () => 'blob:stub',
			revokeObjectURL: () => {},
		}),
		URLSearchParams,
		encodeURIComponent,
		decodeURIComponent,
		setTimeout: (fn) => { try { fn(); } catch { /* ignore */ } return 0; },
		clearTimeout: () => {},
		setInterval: () => 0,
		clearInterval: () => {},
		location: { protocol: 'http:', href: 'http://127.0.0.1:48123/?v=1#home', hash: '#home', search: '' },
		document: {
			querySelector: (sel) => {
				// api.js 启动时会从 meta 读会话令牌并覆盖 SG.apiToken。
				// 不给它一个元素，令牌就是空串，后面"必须带令牌头"的断言会假失败。
				if (String(sel).includes('pineglow-session-token')) {
					return { content: 'STUB-TOKEN-0123456789' };
				}
				return null;
			},
			querySelectorAll: () => [],
			addEventListener: () => {},
			createElement: () => ({ style: {}, classList: { add() {}, remove() {} }, appendChild() {} }),
			head: { appendChild() {} },
			body: { appendChild() {} },
			documentElement: { style: { setProperty() {} }, dataset: {} },
			readyState: 'complete',
		},
		window: {},
		navigator: { language: 'zh-CN', clipboard: { writeText: async () => {} } },
		localStorage: { getItem: () => null, setItem: () => {}, removeItem: () => {} },
		fetch: async (input, init) => {
			const url = typeof input === 'string' ? input : (input && input.url) || String(input);
			calls.push({
				url,
				method: (init && init.method) || 'GET',
				body: init && init.body,
				headers: (init && init.headers) || {},
			});
			if (options.failFetch) {
				throw new Error('network down');
			}
			// 返回"形状够用"的响应：这些方法会解构 res.instance.id / res.account.id 等，
			// 返回空对象会让它们在**后处理阶段**抛 TypeError，从而掩盖真正的契约断言。
			// 这里统一给一个既含 instance 又含 account 的宽松体，让流程走完。
			const body = {
				ok: true,
				id: 'stub-id',
				instance: { id: 'stub-id', name: 'stub', version: '1.20.1', loader: 'Vanilla' },
				account: { id: 'stub-acc', userName: 'stub', type: 'offline' },
				accounts: [],
				instances: [],
				servers: [],
				server: { id: 'stub-srv', name: 'stub', address: 'mc.example.com', port: 25565, online: 0, max: 0 },
				versions: [],
				sources: [],
				worlds: [],
				news: [],
				tasks: [],
				downloads: [],
				mods: [],
				backups: [],
				config: { gameDirectory: '', instances: [] },
			};
			return {
				ok: true,
				status: 200,
				statusText: 'OK',
				headers: { get: () => 'application/json' },
				json: async () => (options.payloadFor ? options.payloadFor(url) : body),
				text: async () => '{}',
				blob: async () => ({ size: 4, type: 'image/png' }),
			};
		},
	};
	sandbox.window = sandbox;
	sandbox.globalThis = sandbox;
	sandbox.SG = { apiToken: 'STUB-TOKEN-0123456789' };

	vm.createContext(sandbox);
	vm.runInContext(API_SRC, sandbox, { filename: 'api.js' });

	const api = sandbox.SG && sandbox.SG.api;
	assert.ok(api, 'api.js 必须挂到 SG.api');
	return { api, calls, sandbox };
}

/** 断言"最后一次调用"的方法与路径。 */
function lastCall(calls) {
	assert.ok(calls.length > 0, '应当至少发出一次请求');
	return calls[calls.length - 1];
}

function assertCall(call, expectedMethod, expectedPathFragment) {
	assert.equal(call.method, expectedMethod, `方法应为 ${expectedMethod}，实际 ${call.method}（url=${call.url}）`);
	assert.ok(
		call.url.includes(expectedPathFragment),
		`路径应包含 ${expectedPathFragment}，实际 ${call.url}`
	);
}

/* ============ 实例域 ============ */

test('live: createInstance → POST /api/instances，body 带 name/version', async () => {
	const { api, calls } = loadApi();
	await api.createInstance({ name: 'Test', version: '1.20.1' });

	// 注意：createInstance 内部会先 POST 再 mapInstance，可能只发一次请求。
	// 取**第一条** POST /api/instances 而不是最后一条，避免被后续请求干扰。
	const call = calls.find(c => c.method === 'POST' && c.url.endsWith('/api/instances'));
	assert.ok(call, '应当发出 POST /api/instances，实际：' + JSON.stringify(calls.map(c => c.method + ' ' + c.url)));
	assert.ok(call.body && call.body.includes('Test'), 'body 应包含实例名');
	assert.ok(call.body.includes('1.20.1'), 'body 应包含版本');
});

test('live: deleteInstance → POST /api/instances/{id}/delete（不是 DELETE 方法）', async () => {
	const { api, calls } = loadApi();
	await api.deleteInstance('abc123');

	const call = lastCall(calls);
	// 后端只登记了 POST .../delete 这一条路由（见 LocalApiService.Routing.cs），
	// 这里钉住前端与之一致，避免有人"顺手改成 RESTful DELETE"从而 404。
	assertCall(call, 'POST', '/api/instances/abc123/delete');
});

test('live: launchInstance → POST /api/instances/{id}/launch', async () => {
	const { api, calls } = loadApi();
	await api.launchInstance('abc123');
	assertCall(lastCall(calls), 'POST', '/api/instances/abc123/launch');
});

test('live: instance id 会被 URL 编码（防路径注入）', async () => {
	const { api, calls } = loadApi();
	await api.launchInstance('../evil?x=1');

	const url = lastCall(calls).url;
	assert.ok(!url.includes('../evil'), 'id 必须被编码，不能原样拼进路径：' + url);
	assert.ok(url.includes('..%2Fevil') || url.includes('..%2fevil'), '期望 encodeURIComponent 结果：' + url);
});

test('live: prelaunchCheck → GET /api/instances/{id}/prelaunch-check', async () => {
	const { api, calls } = loadApi();
	await api.prelaunchCheck('abc123');
	assertCall(lastCall(calls), 'GET', '/api/instances/abc123/prelaunch-check');
});

test('live: getMods → GET /api/instances/{id}/mods', async () => {
	const { api, calls } = loadApi();
	await api.getMods('abc123');
	assertCall(lastCall(calls), 'GET', '/api/instances/abc123/mods');
});

/* ============ 存档域 ============ */

test('live: createSaveBackup → POST .../saves/{folder}/backup', async () => {
	const { api, calls } = loadApi();
	await api.createSaveBackup('inst1', 'My World');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/saves/My%20World/backup');
});

test('live: restoreSaveBackup → POST .../saves/{folder}/backups/{name}/restore', async () => {
	const { api, calls } = loadApi();
	await api.restoreSaveBackup('inst1', 'world1', 'bak.zip');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/saves/world1/backups/bak.zip/restore');
});

test('live: deleteSave → POST .../saves/{folder}/delete', async () => {
	const { api, calls } = loadApi();
	await api.deleteSave('inst1', 'world1');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/saves/world1/delete');
});

test('live: listSaveRecoveries → GET .../saves/recoveries', async () => {
	// M2-1①：已删除存档列表。注意路径是 saves/recoveries，**没有 folder 段**——
	// 写成 .../saves/{folder}/recoveries 会命中不了后端登记的路由（段数不同）。
	const { api, calls } = loadApi();
	await api.listSaveRecoveries('inst1');
	assertCall(lastCall(calls), 'GET', '/api/instances/inst1/saves/recoveries');
});

test('live: restoreSaveRecovery → POST .../saves/recoveries/{op}/restore', async () => {
	const { api, calls } = loadApi();
	await api.restoreSaveRecovery('inst1', 'op-1');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/saves/recoveries/op-1/restore');
});

test('live: purgeSaveRecovery → POST .../saves/recoveries/{op}/purge', async () => {
	const { api, calls } = loadApi();
	await api.purgeSaveRecovery('inst1', 'op-1');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/saves/recoveries/op-1/purge');
});

test('live: deleteSave 会把 recoveryId 与 recoverable 透传给界面', async () => {
	// 前端靠这两个字段决定"是否提示可从恢复区找回"，丢了就变成一句笼统的"已删除"。
	// 用既有的 payloadFor 钩子覆写响应体（不新增测试专用入口）。
	const { api } = loadApi({
		payloadFor: () => ({ ok: true, folderName: 'world1', recoveryId: 'op-42', sizeBytes: 2048, recoverable: true, retentionDays: 30 }),
	});
	const res = await api.deleteSave('inst1', 'world1');

	assert.equal(res.ok, true);
	assert.equal(res.recoveryId, 'op-42');
	assert.equal(res.sizeBytes, 2048);
	assert.equal(res.recoverable, true);
	assert.equal(res.retentionDays, 30);
});

test('live: listSaveBackups → GET .../saves/{folder}/backups', async () => {
	const { api, calls } = loadApi();
	await api.listSaveBackups('inst1', 'world1');
	assertCall(lastCall(calls), 'GET', '/api/instances/inst1/saves/world1/backups');
});

/* ============ 账号域 ============ */

test('live: addOfflineAccount → POST /api/accounts/offline', async () => {
	const { api, calls } = loadApi();
	await api.addOfflineAccount('Steve');
	const call = calls.find(c => c.method === 'POST' && c.url.includes('/api/accounts/offline'));
	assert.ok(call, '应当 POST /api/accounts/offline，实际：' + JSON.stringify(calls.map(c => c.method + ' ' + c.url)));
	assert.ok(call.body && call.body.includes('Steve'), 'body 应包含用户名');
});

test('live: deleteAccount → 路径包含 accountId', async () => {
	const { api, calls } = loadApi();
	await api.deleteAccount('acc-1');
	const call = calls.find(c => c.url.includes('/api/accounts/acc-1'));
	assert.ok(call, '应当命中 /api/accounts/acc-1，实际：' + JSON.stringify(calls.map(c => c.url)));
});

test('live: selectAccount → 指向所选账号', async () => {
	const { api, calls } = loadApi();
	await api.selectAccount('acc-2');
	// 实测签名：POST /api/accounts/select，accountId 在 body 里，不在路径上。
	// 这里按真实契约断言——比"路径里应该有 id"更强，因为它同时锁住了"不得把 id 塞进 URL"。
	const call = calls.find(c => c.method === 'POST' && c.url.endsWith('/api/accounts/select'));
	assert.ok(call, '应当 POST /api/accounts/select，实际：' + JSON.stringify(calls.map(c => c.method + ' ' + c.url)));
	assert.ok(call.body && call.body.includes('acc-2'), 'body 应包含 accountId');
});

test('live: uploadSkin → POST /api/accounts/{id}/skin，body 带 pngBase64', async () => {
	const { api, calls } = loadApi();
	await api.uploadSkin('acc-1', 'AAAA', 'classic');
	// 实测签名：POST /api/accounts/skin，accountId/pngBase64/slim 全在 body。
	const call = calls.find(c => c.method === 'POST' && c.url.endsWith('/api/accounts/skin'));
	assert.ok(call, '应当 POST /api/accounts/skin，实际：' + JSON.stringify(calls.map(c => c.method + ' ' + c.url)));
	assert.ok(call.body && call.body.includes('AAAA'), 'body 应带 pngBase64');
	assert.ok(call.body.includes('acc-1'), 'body 应带 accountId');
});

test('live: microsoftDeviceStart → POST /api/accounts/microsoft/device', async () => {
	const { api, calls } = loadApi();
	await api.microsoftDeviceStart();
	const call = calls.find(c => c.method === 'POST' && c.url.includes('/api/accounts/microsoft/device'));
	assert.ok(call, '应当 POST /api/accounts/microsoft/device，实际：' + JSON.stringify(calls.map(c => c.method + ' ' + c.url)));
});

test('live: microsoftDevicePoll → POST /api/accounts/microsoft/poll', async () => {
	const { api, calls } = loadApi();
	await api.microsoftDevicePoll();
	assertCall(lastCall(calls), 'POST', '/api/accounts/microsoft/poll');
});

/* ============ 服务器域 ============ */

test('live: addServer → POST /api/servers，body 带 name/host', async () => {
	const { api, calls } = loadApi();
	// 实测签名：addServer({ name, host, port })，不是位置参数。
	await api.addServer({ name: 'Srv', host: 'mc.example.com', port: 25565 });
	const call = calls.find(c => c.method === 'POST' && c.url.includes('/api/servers'));
	assert.ok(call, '应当 POST /api/servers，实际：' + JSON.stringify(calls.map(c => c.method + ' ' + c.url)));
	assert.ok(call.body && call.body.includes('mc.example.com'), 'body 应包含 host');
});

test('live: pingServer → POST /api/servers/{id}/ping', async () => {
	const { api, calls } = loadApi();
	await api.pingServer('srv-1');
	const call = calls.find(c => c.url.includes('/api/servers/srv-1/ping'));
	assert.ok(call, '应当 POST /api/servers/srv-1/ping，实际：' + JSON.stringify(calls.map(c => c.method + ' ' + c.url)));
	assert.equal(call.method, 'POST');
});

test('live: deleteServer → 指向该 serverId', async () => {
	const { api, calls } = loadApi();
	await api.deleteServer('srv-1');
	const call = lastCall(calls);
	assert.ok(call.url.includes('/api/servers/srv-1'), '实际 ' + call.url);
});

test('live: getServers → GET /api/servers', async () => {
	const { api, calls } = loadApi();
	await api.getServers();
	assertCall(lastCall(calls), 'GET', '/api/servers');
});

/* ============ 下载与更新域 ============ */

test('live: getVersions → GET /api/versions', async () => {
	const { api, calls } = loadApi();
	await api.getVersions();
	assertCall(lastCall(calls), 'GET', '/api/versions');
});

test('live: getRemoteVersions → GET /api/versions/remote', async () => {
	const { api, calls } = loadApi();
	await api.getRemoteVersions();
	assertCall(lastCall(calls), 'GET', '/api/versions/remote');
});

test('live: downloadVersion → POST /api/versions/download', async () => {
	const { api, calls } = loadApi();
	await api.downloadVersion('1.20.1');
	const call = lastCall(calls);
	assert.equal(call.method, 'POST');
	assert.ok(call.url.includes('/api/versions/download'), '实际 ' + call.url);
});

test('live: getSources → GET /api/sources', async () => {
	const { api, calls } = loadApi();
	await api.getSources();
	assertCall(lastCall(calls), 'GET', '/api/sources');
});

test('live: selectSource → 带 source key', async () => {
	const { api, calls } = loadApi();
	await api.selectSource('bmclapi');
	const call = lastCall(calls);
	assert.ok(call.url.includes('/api/sources'), '实际 ' + call.url);
	assert.ok(call.body && call.body.includes('bmclapi'), 'body 应包含 key');
});

test('live: checkUpdate → GET /api/update/check', async () => {
	const { api, calls } = loadApi();
	await api.checkUpdate();
	assertCall(lastCall(calls), 'GET', '/api/update/check');
});

test('live: getUpdateStatus → GET /api/update（不是 /api/update/status）', async () => {
	const { api, calls } = loadApi();
	await api.getUpdateStatus();
	// 后端路由表里状态端点是 GET /api/update；/api/update/status 不存在。
	// 这条断言是为了防止有人"按直觉"改成 /status 然后 404。
	assertCall(lastCall(calls), 'GET', '/api/update');
	assert.ok(!lastCall(calls).url.endsWith('/status'), '不应出现 /api/update/status');
});

test('live: startUpdateDownload → POST /api/update/download', async () => {
	const { api, calls } = loadApi();
	await api.startUpdateDownload();
	assertCall(lastCall(calls), 'POST', '/api/update/download');
});

test('live: applyUpdate → POST /api/update/apply', async () => {
	const { api, calls } = loadApi();
	await api.applyUpdate();
	assertCall(lastCall(calls), 'POST', '/api/update/apply');
});

/* ============ 新闻、内容、系统域 ============ */

test('live: getNews → GET /api/news，带 token 查询参数或头', async () => {
	const { api, calls } = loadApi();
	await api.getNews();
	const call = lastCall(calls);
	assert.ok(call.url.includes('/api/news'), '实际 ' + call.url);
});

test('live: getNewsImage → 走 news-image 端点', async () => {
	const { api, calls } = loadApi();
	await api.getNewsImage('imgid');
	const call = calls.find(c => c.url.includes('/api/news-image'));
	assert.ok(call, '应当请求 news-image，实际：' + JSON.stringify(calls.map(c => c.url)));
	// 关键契约：图片 id 必须编码，且令牌只走请求头、不进 URL（进 URL 就会落日志）
	assert.ok(call.url.includes('id=imgid'), '实际 ' + call.url);
	assert.ok(!call.url.includes('STUB-TOKEN'), '令牌绝不能出现在 URL 里：' + call.url);
	assert.ok(
		(call.headers['X-Pineglow-Token'] || call.headers['x-pineglow-token']),
		'图片请求也必须带令牌头'
	);
});

test('live: getContentTasks → GET /api/content/tasks', async () => {
	const { api, calls } = loadApi();
	await api.getContentTasks();
	assertCall(lastCall(calls), 'GET', '/api/content/tasks');
});

test('live: installContent → POST /api/content/install', async () => {
	const { api, calls } = loadApi();
	await api.installContent({ category: 'mod', projectId: 'x' });
	assertCall(lastCall(calls), 'POST', '/api/content/install');
});

test('live: getWorlds → GET /api/worlds', async () => {
	const { api, calls } = loadApi();
	await api.getWorlds();
	const call = calls.find(c => c.url.includes('/api/worlds'));
	assert.ok(call, '应当请求 /api/worlds，实际：' + JSON.stringify(calls.map(c => c.url)));
	assert.equal(call.method, 'GET');
});

test('live: getStats → GET /api/stats', async () => {
	const { api, calls } = loadApi();
	await api.getStats();
	assertCall(lastCall(calls), 'GET', '/api/stats');
});

test('live: openLogDirectory → POST /api/logs', async () => {
	const { api, calls } = loadApi();
	await api.openLogDirectory();
	assertCall(lastCall(calls), 'POST', '/api/logs');
});

test('live: openSaveDirectory → POST .../saves/{folder}/open', async () => {
	const { api, calls } = loadApi();
	await api.openSaveDirectory('inst1', 'world1');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/saves/world1/open');
});

/* ============ 二分定位域 ============ */

test('live: bisectStart → POST .../bisect/start', async () => {
	const { api, calls } = loadApi();
	await api.bisectStart('inst1');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/bisect/start');
});

test('live: bisectTest → POST .../bisect/test', async () => {
	const { api, calls } = loadApi();
	await api.bisectTest('inst1', 'works');
	const call = lastCall(calls);
	assert.equal(call.method, 'POST');
	assert.ok(call.url.includes('/api/instances/inst1/bisect/test'), '实际 ' + call.url);
});

test('live: bisectGiveUp → POST .../bisect/giveup', async () => {
	const { api, calls } = loadApi();
	await api.bisectGiveUp('inst1');
	assertCall(lastCall(calls), 'POST', '/api/instances/inst1/bisect/giveup');
});

/* ============ 统一契约 ============ */

test('live: 所有写操作都带会话令牌头', async () => {
	const { api, calls } = loadApi();
	await api.createInstance({ name: 'T' });
	await api.deleteInstance('x');

	for (const call of calls.filter(c => c.url.includes('/api/'))) {
		const headers = call.headers || {};
		assert.ok(
			headers['X-Pineglow-Token'] || headers['x-pineglow-token'],
			'缺少会话令牌头：' + JSON.stringify(headers) + ' url=' + call.url
		);
	}
});

test('live: 网络失败必须抛错，不得静默返回伪造数据', async () => {
	const { api } = loadApi({ failFetch: true });

	await assert.rejects(() => api.getServers(), /network|api|unavailable/i);
});

test('live: 任何端点都不得出现双斜杠或未编码空格', async () => {
	const { api, calls } = loadApi();

	await api.getServers();
	await api.createSaveBackup('inst 1', 'my world');

	for (const call of calls.filter(c => c.url.includes('/api/'))) {
		assert.ok(!/\/\/api/.test(call.url), '出现双斜杠：' + call.url);
		assert.ok(!/\s/.test(call.url), 'URL 里有未编码空格：' + call.url);
	}
});
