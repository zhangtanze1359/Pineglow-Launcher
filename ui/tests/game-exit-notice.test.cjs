'use strict';
/*
 * N09-09 补充：游戏退出显式提示（秒退 / 异常退出）行为测试
 * --------------------------------------------------------------------------
 * 用户反馈「启动不了游戏、点了没反应」：游戏秒退时退出码只写日志，界面无提示。
 * 候选改为：观察窗口轮询 GET /api/game-status?since=N，按事件号去重，
 * 对「秒退（<15 秒）或退出码非 0」的退出给一条持久 toast；正常长会话结束静默。
 *
 * 退出归属采用**服务端分界**（第七轮）：启动成功响应携带 exitEventBase（服务端启动
 * 预约时刻的退出事件号），界面只在本会话第一次启动时采纳一次，不做任何历史读取。
 *
 * 退出送达采用**批次 + 提示队列**（第八轮）：状态接口按 since 批量回传退出事件，
 * 逐条排队各提示一次——两次退出都发生在两次轮询之间时一条都不丢（第七轮独立复验
 * 反例：只回最近一次会丢第一条的提示）。
 *
 * 用 helpers/live-launcher.cjs 全量装载生产脚本（含真实 api.js 与 i18n）。
 * 真实 WebView2 呈现与时长由独立复验的桌面级实测取证。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const { liveLauncher } = require('./helpers/live-launcher.cjs');

const exitStatus = exit => ({ lastGameExit: exit, exits: exit ? [exit] : [], lastEventId: exit ? exit.eventId : 0 });

test('N09-09：秒退/异常退出显式提示、按事件号去重、正常长会话静默', async () => {
	let gameStatus = exitStatus(null);
	const h = await liveLauncher({
		respond: pathname => (pathname === '/api/game-status' ? gameStatus : undefined),
	});
	const toastEl = h.get('toast');
	assert.ok(toastEl, '夹具应提供 #toast 容器');

	// ① 契约映射：后端字段映射成前端判定所需形状（含秒退 34ms、ExitCode=1 的真实现场）
	gameStatus = exitStatus({ eventId: 3, instanceId: 'i1', exitCode: 1, durationMs: 34, quickExit: true, needsNotice: true });
	const mapped = await h.api.getGameStatus();
	assert.equal(mapped.exits.length, 1);
	assert.equal(mapped.lastGameExit.eventId, 3);
	assert.equal(mapped.lastGameExit.exitCode, 1);
	assert.equal(mapped.lastGameExit.needsNotice, true);
	assert.equal(mapped.lastGameExit.quickExit, true);

	// ② 秒退 → 显式提示，事件号推进为已见
	h.T.setExitWatchBaseline(0);
	assert.equal(h.T.handleGameExitStatus(mapped), 'notified');
	assert.ok(toastEl.classList.contains('show'), '秒退后提示应可见');
	assert.match(toastEl.textContent, /ExitCode 1/);
	assert.equal(h.T.getExitWatchBaseline(), 3);
	h.T.finishExitNotice();

	// ③ 同一退出事件再次到达 → 不重复提示
	toastEl.textContent = '';
	assert.equal(h.T.handleGameExitStatus(mapped), 'none');
	assert.equal(toastEl.textContent, '');

	// ④ 正常长会话结束（退出码 0、运行一小时）→ 静默推进基线，不打扰用户
	const normal = exitStatus({ eventId: 4, instanceId: 'i1', exitCode: 0, durationMs: 3600000, quickExit: false, needsNotice: false });
	assert.equal(h.T.handleGameExitStatus(normal), 'seen');
	assert.equal(toastEl.textContent, '');
	assert.equal(h.T.getExitWatchBaseline(), 4);

	// ⑤ 运行中崩溃（长会话但退出码非 0）→ 仍然提示
	const crashed = exitStatus({ eventId: 5, instanceId: 'i1', exitCode: 1, durationMs: 600000, quickExit: false, needsNotice: true });
	assert.equal(h.T.handleGameExitStatus(crashed), 'notified');
	assert.match(toastEl.textContent, /ExitCode 1/);
	h.T.finishExitNotice();

	// ⑥ 历史分界来自启动响应、只采纳一次：更早的退出不补报，后来的启动不许再抬分界
	h.T.setExitWatchBaseline(0);
	h.T.setLaunchExitHorizon(5);
	assert.equal(h.T.getExitWatchBaseline(), 5);
	h.T.setLaunchExitHorizon(9);
	assert.equal(h.T.getExitWatchBaseline(), 5, '第二次启动的分界更高也不许抬');
	toastEl.textContent = '';
	assert.equal(h.T.handleGameExitStatus(exitStatus({ eventId: 5, instanceId: 'i1', exitCode: 1, durationMs: 20, quickExit: true, needsNotice: true })), 'none');
	assert.equal(toastEl.textContent, '', '分界之上的历史退出不补报');
	assert.equal(h.T.handleGameExitStatus(exitStatus({ eventId: 6, instanceId: 'i1', exitCode: 1, durationMs: 20, quickExit: true, needsNotice: true })), 'notified');
	h.T.finishExitNotice();

	// ⑦ 文案包含退出码与运行时长（指向日志入口），未知退出码用「未知」占位
	assert.match(h.T.gameExitNoticeText({ exitCode: 1, durationMs: 34 }), /ExitCode 1/);
	assert.match(h.T.gameExitNoticeText({ exitCode: 1, durationMs: 34 }), /0/);
	assert.match(h.T.gameExitNoticeText({ exitCode: null, durationMs: 1000 }), /未知|unknown/);
});

test('退出归属回归（第四轮漏报现场）：退出先于启动成功响应被记录，仍必须提示', async () => {
	let gameStatus = exitStatus(null);
	let releaseLaunch;
	const launchGate = new Promise(resolve => { releaseLaunch = resolve; });
	const h = await liveLauncher({
		respond: async (pathname, init) => {
			if (pathname === '/api/game-status') return gameStatus;
			if (pathname.endsWith('/launch') && (init.method || 'GET') === 'POST') {
				// 模拟独立复验实测的时序：启动成功响应被拖到游戏退出之后（378ms 窗口）
				await launchGate;
				return { ok: true, pid: 123, minimizeOnLaunch: false, exitEventBase: 0 };
			}
			return undefined;
		},
	});
	h.T.setState({ config: {}, instances: [], accounts: [], java: [] });
	const toastEl = h.get('toast');

	// 点击开始游戏：启动成功响应被门闩拖住
	const launchP = h.T.doLaunchInternal('b', 'Instance B');
	await h.flush();

	// 游戏在响应回来之前就退出（第四轮实测：22ms 退出 / 378ms 响应）
	gameStatus = exitStatus({ eventId: 1, instanceId: 'b', exitCode: 1, durationMs: 22, quickExit: true, needsNotice: true });

	// 放行启动成功响应：历史分界来自响应（0），本次退出必须提示
	releaseLaunch();
	await launchP;
	await h.flush();
	assert.match(toastEl.textContent, /已发出启动指令|launch\.sent/);
	assert.equal(await h.T.gameExitTick(), 'notified');
	assert.match(toastEl.textContent, /ExitCode 1/);
	assert.equal(h.T.getExitWatchBaseline(), 1);
	h.T.stopGameExitWatch();
});

test('退出归属回归（第五、六轮反例）：迟到读取/再次启动都不许吞掉尚未提示的秒退', async () => {
	let gameStatus = exitStatus(null);
	let launchCount = 0;
	const h = await liveLauncher({
		respond: async (pathname) => {
			if (pathname === '/api/game-status') return gameStatus;
			if (pathname.endsWith('/launch')) {
				launchCount++;
				// 服务端按预约时刻给分界：第二次启动时退出事件 1 已在册
				return { ok: true, pid: 100 + launchCount, minimizeOnLaunch: false, exitEventBase: launchCount - 1 };
			}
			return undefined;
		},
	});
	h.T.setState({ config: {}, instances: [], accounts: [], java: [] });

	// 第一次启动（分界 0）→ 游戏秒退（事件 1），观察窗口还没来得及轮询
	await h.T.doLaunchInternal('b', 'Instance B');
	await h.flush();
	gameStatus = exitStatus({ eventId: 1, instanceId: 'b', exitCode: 1, durationMs: 28, quickExit: true, needsNotice: true });

	// 用户马上第二次点击（第六轮反例：失败/重试路径曾在此重新读取并吞掉事件 1；
	// 第五轮反例：迟到的初始化读取曾在此吞掉事件 1。新机制没有任何历史读取）
	await h.T.doLaunchInternal('b', 'Instance B');
	await h.flush();
	assert.equal(h.T.getExitWatchBaseline(), 0, '第二次启动的分界（1）不得抬高历史分界');

	// 观察窗口恢复轮询：尚未提示的秒退仍必须提示，且只提示一次
	assert.equal(await h.T.gameExitTick(), 'notified');
	assert.equal(h.T.getExitWatchBaseline(), 1);
	assert.equal(await h.T.gameExitTick(), 'none');
	h.T.stopGameExitWatch();
});

test('退出送达回归（第七轮反例）：两次退出都发生在轮询之前时一条提示都不丢', async () => {
	let gameStatus = { lastGameExit: null, exits: [], lastEventId: 0 };
	const h = await liveLauncher({
		respond: pathname => (pathname === '/api/game-status' ? gameStatus : undefined),
	});
	const toastEl = h.get('toast');
	h.T.setExitWatchBaseline(0);

	// 两次秒退都发生在下一次轮询之前（第七轮独立断言的 [1, 7] 现场）：
	// 旧实现只回最近一次 → 期望 [1, 7] 实得 [7]；现在按批次逐条排队各提示一次
	gameStatus = {
		lastGameExit: { eventId: 7, instanceId: 'b', exitCode: 2, durationMs: 30, quickExit: true, needsNotice: true },
		exits: [
			{ eventId: 1, instanceId: 'b', exitCode: 1, durationMs: 20, quickExit: true, needsNotice: true },
			{ eventId: 7, instanceId: 'b', exitCode: 2, durationMs: 30, quickExit: true, needsNotice: true },
		],
		lastEventId: 7,
	};
	assert.equal(await h.T.gameExitTick(), 'notified');
	assert.equal(h.T.getExitWatchBaseline(), 7);

	// 第一条立即显示，第二条在队列里按序显示——一条都不丢
	assert.match(toastEl.textContent, /ExitCode 1/);
	const queued = h.T.getExitNoticeQueue();
	assert.equal(queued.length, 1, '第一条已显示、第二条应排队');
	assert.match(queued[0], /ExitCode 2/);
	h.T.finishExitNotice();
	assert.match(toastEl.textContent, /ExitCode 2/, '第一条显示完后第二条必须接着显示');
	assert.equal(h.T.getExitNoticeQueue().length, 0);
	h.T.stopGameExitWatch();

	// 批次里的正常退出（needsNotice=false）只静默推进，不占队列
	const mixed = {
		exits: [
			{ eventId: 8, instanceId: 'b', exitCode: 0, durationMs: 120000, quickExit: false, needsNotice: false },
			{ eventId: 9, instanceId: 'b', exitCode: 1, durationMs: 40, quickExit: true, needsNotice: true },
		],
		lastGameExit: null,
		lastEventId: 9,
	};
	assert.equal(h.T.handleGameExitStatus(mixed), 'notified');
	assert.equal(h.T.getExitWatchBaseline(), 9);
	assert.equal(h.T.getExitNoticeQueue().length + (toastEl.textContent.includes('ExitCode 1') ? 1 : 0), 1, '混合批次只有异常退出占提示');
	h.T.finishExitNotice();
});

test('退出送达回归（架构审查反例）：双开跨轮询——A 提示后不停表，B 的秒退照常提示', async () => {
	let gameStatus = { lastGameExit: null, exits: [], lastEventId: 0, runningCount: 0 };
	const h = await liveLauncher({
		respond: pathname => (pathname === '/api/game-status' ? gameStatus : undefined),
	});
	const toastEl = h.get('toast');
	h.T.setExitWatchBaseline(0);

	// A、B 均已启动后，A 在首轮查询前退出（码 11）；B 仍在跑（runningCount=1）
	gameStatus = {
		lastGameExit: { eventId: 1, instanceId: 'a', exitCode: 11, durationMs: 5, quickExit: true, needsNotice: true },
		exits: [{ eventId: 1, instanceId: 'a', exitCode: 11, durationMs: 5, quickExit: true, needsNotice: true }],
		lastEventId: 1,
		runningCount: 1,
	};
	assert.equal(await h.T.gameExitTick(), 'notified');
	assert.match(toastEl.textContent, /ExitCode 11/);
	assert.ok(h.T.getExitWatchActive(), 'B 还在跑，观察窗口不许停');
	h.T.finishExitNotice();

	// B 在下一轮才秒退（码 22）——必须照样提示（旧实现在 A 提示后已停表 → 永远漏报）
	gameStatus = {
		lastGameExit: { eventId: 2, instanceId: 'b', exitCode: 22, durationMs: 8, quickExit: true, needsNotice: true },
		exits: [{ eventId: 2, instanceId: 'b', exitCode: 22, durationMs: 8, quickExit: true, needsNotice: true }],
		lastEventId: 2,
		runningCount: 0,
	};
	assert.equal(await h.T.gameExitTick(), 'notified');
	assert.match(toastEl.textContent, /ExitCode 22/);
	assert.equal(h.T.getExitWatchBaseline(), 2);
	assert.ok(!h.T.getExitWatchActive(), '没有游戏在跑才收摊');
});

test('退出送达回归（架构审查反例）：正常退出后另一实例异常退出仍提示', async () => {
	let gameStatus = { lastGameExit: null, exits: [], lastEventId: 0, runningCount: 2 };
	const h = await liveLauncher({
		respond: pathname => (pathname === '/api/game-status' ? gameStatus : undefined),
	});
	const toastEl = h.get('toast');
	h.T.setExitWatchBaseline(0);

	// A 正常退出（码 0、长会话）：静默，且不许停表
	gameStatus = {
		exits: [{ eventId: 1, instanceId: 'a', exitCode: 0, durationMs: 300000, quickExit: false, needsNotice: false }],
		lastGameExit: { eventId: 1, instanceId: 'a', exitCode: 0, durationMs: 300000, quickExit: false, needsNotice: false },
		lastEventId: 1,
		runningCount: 1,
	};
	assert.equal(await h.T.gameExitTick(), 'seen');
	assert.equal(toastEl.textContent, '');
	assert.ok(h.T.getExitWatchActive(), '正常退出也不许停表（另一实例还在跑）');

	// B 随后异常退出（码 1）：仍要提示
	gameStatus = {
		exits: [{ eventId: 2, instanceId: 'b', exitCode: 1, durationMs: 20, quickExit: true, needsNotice: true }],
		lastGameExit: { eventId: 2, instanceId: 'b', exitCode: 1, durationMs: 20, quickExit: true, needsNotice: true },
		lastEventId: 2,
		runningCount: 0,
	};
	assert.equal(await h.T.gameExitTick(), 'notified');
	assert.match(toastEl.textContent, /ExitCode 1/);
	assert.ok(!h.T.getExitWatchActive());
});

test('退出送达回归（复验在途反例）：旧响应不得取消新启动的观察窗口', async () => {
	let statusCalls = 0;
	let releaseStale;
	const staleGate = new Promise(r => { releaseStale = r; });
	let gameStatus = { lastGameExit: null, exits: [], lastEventId: 0, runningCount: 1 };
	const h = await liveLauncher({
		respond: async (pathname) => {
			if (pathname === '/api/game-status') {
				statusCalls++;
				if (statusCalls === 1) await staleGate; // 第一次查询在途（旧快照）
				return gameStatus;
			}
			if (pathname.endsWith('/launch')) {
				return { ok: true, pid: 101, minimizeOnLaunch: false, exitEventBase: 0 };
			}
			return undefined;
		},
	});
	h.T.setState({ config: {}, instances: [], accounts: [], java: [] });
	h.T.setExitWatchBaseline(0);

	// 第一次查询在途：其快照是 A 退出后、B 启动成功之前的状态（runningCount=0、事件 1）
	gameStatus = {
		lastGameExit: { eventId: 1, instanceId: 'a', exitCode: 11, durationMs: 5, quickExit: true, needsNotice: true },
		exits: [{ eventId: 1, instanceId: 'a', exitCode: 11, durationMs: 5, quickExit: true, needsNotice: true }],
		lastEventId: 1,
		runningCount: 0,
	};
	const staleTick = h.T.gameExitTick();

	// B 启动成功 → 新纪元接管观察窗口并排定下一次查询
	await h.T.doLaunchInternal('b', 'Instance B');
	await h.flush();
	assert.ok(h.T.getExitWatchActive(), 'B 的观察窗口已接管');

	// 旧响应此刻才回来：A 的提示照发，但不许收摊 B 的观察窗口
	releaseStale();
	assert.equal(await staleTick, 'notified');
	assert.ok(h.T.getExitWatchActive(), '旧响应不许取消新启动的观察窗口');

	// B 秒退（码 22）：下一轮必须照常提示
	h.T.finishExitNotice();
	gameStatus = {
		lastGameExit: { eventId: 2, instanceId: 'b', exitCode: 22, durationMs: 8, quickExit: true, needsNotice: true },
		exits: [{ eventId: 2, instanceId: 'b', exitCode: 22, durationMs: 8, quickExit: true, needsNotice: true }],
		lastEventId: 2,
		runningCount: 0,
	};
	assert.equal(await h.T.gameExitTick(), 'notified');
	assert.match(h.get('toast').textContent, /ExitCode 22/);
	assert.ok(!h.T.getExitWatchActive(), '没有游戏在跑才收摊');
});
