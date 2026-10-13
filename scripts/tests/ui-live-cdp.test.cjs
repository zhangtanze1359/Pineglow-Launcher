'use strict';
/*
 * CV-07 聚焦回归：scripts/verify-ui-pages-live.cjs 的 CDP 错误处理 / 终态判定 /
 * 失败分类 / 报告脱敏
 * ==========================================================================
 * 单文件聚焦回归：只覆盖本轮对该验收脚本的四处修复，不启动真实 EXE、不联网、
 * 不开真实 WebSocket（用受控 FakeWebSocket 驱动消息往返）。
 *
 * 对应原始 RED：
 *   docs/current-version-audit-2026-10-13/cdp-error-red.log
 *     CDP_ERROR_THROW=ReferenceError:method is not defined
 *   —— connectCdp 的错误分支引用了不存在的 method，在 message 回调里同步抛错；
 *      该请求此时已从 pending 删除、却从未 settle，于是一次 CDP 错误让整轮验收永久挂起。
 *
 * 覆盖：
 *   1. CDP 错误响应按请求自己的 method 正确 reject，且不抛 ReferenceError
 *   2. 成功响应 resolve；notifications 正常入队
 *   3. 未知 id 响应安全忽略（不影响其它 pending 请求）
 *   4. 重复响应安全处理（不抛错、不影响已完成请求）
 *   5. 连接关闭时未完成请求全部 reject（绝不永久挂起）
 *   6. 非 JSON 消息安全忽略
 *   7. 终态判定：正常有内容 / 成功空态 / 已落地错误状态 / 仍在加载（短 ≠ 加载）
 *   8. 未知本机 500（含 502/503/504）仍记产品问题，不能凭状态码放行
 *   9. 只有**完整响应正文**证明的上游网络错误才改判为外部依赖
 *  10. 先用完整正文判断、再截断展示
 *  11. 报告脱敏 token / Cookie / Bearer / 查询串敏感值
 *  12. URL 自带敏感值时不再重发，只记录脱敏形状
 *  13. require 时不得自动执行验收主流程（守卫必须留在源码里）
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..', '..');
const SCRIPT_PATH = path.join(ROOT, 'scripts', 'verify-ui-pages-live.cjs');
const SRC = fs.readFileSync(SCRIPT_PATH, 'utf8');

/* ============== 受控 WebSocket 替身（记录 send，允许测试注入消息） ============== */
class FakeWebSocket {
	constructor(url) {
		this.url = url;
		this.sent = [];
		this.listeners = {};
		this.closed = false;
	}
	addEventListener(type, fn) { (this.listeners[type] = this.listeners[type] || []).push(fn); }
	emit(type, ev) { for (const fn of (this.listeners[type] || [])) fn(ev); }
	open() { this.emit('open', {}); }
	/** 把一个 CDP 消息投递给客户端实现（等价于真实 WebSocket 收到帧） */
	push(msg) { this.emit('message', { data: typeof msg === 'string' ? msg : JSON.stringify(msg) }); }
	send(text) { this.sent.push(JSON.parse(text)); }
	close() { this.closed = true; this.emit('close', {}); }
}

let lastSocket = null;
globalThis.WebSocket = function WebSocketCtor(url) {
	const ws = new FakeWebSocket(url);
	lastSocket = ws;
	return ws;
};

const mod = require(SCRIPT_PATH);

/** 建立一个已 open 的 CDP 客户端 */
async function openCdp() {
	const p = mod.connectCdp('ws://127.0.0.1:1/devtools/page/1');
	const ws = lastSocket;
	ws.open();
	return { cdp: await p, ws };
}

/** 等待 fail-fast：如果 promise 永远不 settle，超时后断言失败（而不是挂死整轮） */
function withTimeout(promise, ms, label) {
	return Promise.race([
		promise,
		new Promise((_, rej) => setTimeout(() => rej(new Error(`超时未 settle：${label}`)), ms)),
	]);
}
/* ============== 1. 错误响应按 method reject，且不抛 ReferenceError ============== */
test('CV-07: CDP 错误响应按请求自己的 method reject（不再 ReferenceError）', async () => {
	const { cdp, ws } = await openCdp();
	const p = cdp.send('Runtime.evaluate', { expression: '1+1' });
	assert.equal(ws.sent.length, 1, 'send 应把请求写进 WebSocket');
	const { id, method } = ws.sent[0];
	assert.equal(method, 'Runtime.evaluate');
	assert.equal(id, 1);

	// RED：旧实现会在这里同步抛 ReferenceError:method is not defined
	assert.doesNotThrow(() => ws.push({ id, error: { code: -32000, message: 'boom' } }),
		'CDP 错误响应处理不得同步抛错（旧缺陷是 ReferenceError）');

	await assert.rejects(withTimeout(p, 500, 'Runtime.evaluate'), (err) => {
		assert.match(err.message, /boom/, '应保留 CDP 的错误消息');
		assert.match(err.message, /Runtime\.evaluate/, '错误消息必须带上是哪条 CDP 命令失败');
		return true;
	});
});

/* ============== 2. 成功响应与通知 ============== */
test('CV-07: 成功响应 resolve，method 不同也不会串号', async () => {
	const { cdp, ws } = await openCdp();
	const a = cdp.send('Page.enable');
	const b = cdp.send('Page.captureScreenshot', { format: 'png' });
	ws.push({ id: ws.sent[1].id, result: { data: 'BASE64' } });   // 先回第二条
	ws.push({ id: ws.sent[0].id, result: { ok: true } });
	assert.deepEqual(await a, { ok: true });
	assert.deepEqual(await b, { data: 'BASE64' });
	assert.equal(cdp.notifications.length, 0);
	ws.push({ method: 'Runtime.consoleAPICalled', params: { type: 'error' } });
	assert.equal(cdp.notifications.length, 1, '通知应按帧顺序入队');
});

/* ============== 3. 未知 id ============== */
test('CV-07: 未知 id 的响应被安全忽略，不影响其它 pending 请求', async () => {
	const { cdp, ws } = await openCdp();
	const p = cdp.send('Runtime.enable');
	assert.doesNotThrow(() => ws.push({ id: 9999, result: { ignored: true } }), '未知 id 不得抛错');
	assert.doesNotThrow(() => ws.push({ id: 0, result: {} }), 'id=0 也必须安全处理');
	ws.push({ id: ws.sent[0].id, result: { ok: 1 } });
	assert.deepEqual(await withTimeout(p, 500, 'Runtime.enable'), { ok: 1 });
});

/* ============== 4. 重复响应 ============== */
test('CV-07: 重复响应安全处理（第二次同 id 不抛错）', async () => {
	const { cdp, ws } = await openCdp();
	const p = cdp.send('Log.enable');
	const { id } = ws.sent[0];
	ws.push({ id, result: { first: true } });
	assert.deepEqual(await p, { first: true });
	assert.doesNotThrow(() => ws.push({ id, result: { second: true } }), '重复响应不得抛错');
	assert.doesNotThrow(() => ws.push({ id, error: { message: 'late error' } }), '迟到的错误响应也不得抛错');
});

/* ============== 5. 连接关闭 ============== */
test('CV-07: 连接关闭时未完成的请求全部 reject（绝不永久挂起）', async () => {
	const { cdp, ws } = await openCdp();
	const p = cdp.send('Runtime.evaluate', { expression: 'never' });
	ws.close();
	await assert.rejects(withTimeout(p, 500, '关闭后的请求'), /CDP 连接已关闭/);
});

/* ============== 6. 非 JSON 帧 ============== */
test('CV-07: 非 JSON 帧被安全忽略', async () => {
	const { cdp, ws } = await openCdp();
	const p = cdp.send('Runtime.enable');
	assert.doesNotThrow(() => ws.push('not-json{'), '坏帧不得抛错');
	ws.push({ id: ws.sent[0].id, result: { ok: 2 } });
	assert.deepEqual(await withTimeout(p, 500, 'Runtime.enable'), { ok: 2 });
});

/* ============== 7. 终态判定 ============== */
test('CV-07: 终态判定区分 正常有内容 / 成功空态 / 已落地错误 / 仍加载', () => {
	const probe = (o) => Object.assign({ exists: true, textLen: 0, loading: false, empty: false, error: false, hasAction: false }, o);
	const MIN = 400;   // 以 content 页的下限为例

	assert.equal(mod.classifyViewState(probe({ textLen: 520 }), MIN), 'content', '内容量达标 = 正常有内容');
	assert.equal(mod.classifyViewState(probe({ textLen: 30 }), MIN), 'loading', '无任何终态标记且内容量不足 = 仍在加载');
	assert.equal(mod.classifyViewState(probe({ textLen: 0 }), MIN), 'loading', '空文本 = 仍在加载');
	assert.equal(mod.classifyViewState(probe({ exists: false }), MIN), 'missing', '容器不存在 = 结构缺失');

	// 关键：短 ≠ 加载。空态/错误态天然比正常内容短，必须按终态处理。
	assert.equal(mod.classifyViewState(probe({ textLen: 18, empty: true }), MIN), 'empty', 'RED：成功空态不得因字数不够被当成加载中');
	assert.equal(mod.classifyViewState(probe({ textLen: 24, error: true, hasAction: true }), MIN), 'error', 'RED：已落地错误状态不得被当成加载中');
	assert.equal(mod.classifyViewState(probe({ textLen: 12, empty: true }), 20), 'empty', '低于任何下限的成功空态也是终态');

	// 加载标记优先级最高：仍在 loading 时不因为同时存在其它标记就判终态
	assert.equal(mod.classifyViewState(probe({ textLen: 900, loading: true, empty: true }), MIN), 'loading', '仍在加载优先');
	assert.equal(mod.classifyViewState(probe({ textLen: 900, loading: true, error: true }), MIN), 'loading', '仍在加载优先（错误标记在骨架之后才落地）');

	// 终态集合：只有这三种算"有结论"
	assert.equal(mod.isTerminalState('content'), true);
	assert.equal(mod.isTerminalState('empty'), true);
	assert.equal(mod.isTerminalState('error'), true);
	assert.equal(mod.isTerminalState('loading'), false);
	assert.equal(mod.isTerminalState('missing'), false);

	// 判定所需的 DOM 标记必须覆盖生产实际渲染的类名/钩子
	assert.ok(mod.EMPTY_MARKERS.includes('.empty-state'));
	assert.ok(mod.ERROR_MARKERS.includes('.content-error'));
	assert.ok(mod.ERROR_MARKERS.includes('[data-error="1"]'));
	assert.ok(mod.ERROR_MARKERS.includes('[data-tasks-error="1"]'));
	assert.ok(mod.ERROR_MARKERS.includes('[data-news-state="error"]'));
	assert.ok(mod.LOADING_MARKERS.includes('.loading-state'));
	assert.ok(mod.ACTION_MARKERS.includes('[data-dl-retry]'));
});

/* ============== 8. 未知本机 5xx 继续阻断 ============== */
test('CV-07: 未知本机 500/502/503/504 仍记为产品问题（不得凭状态码放行）', () => {
	const apiBase = 'http://127.0.0.1:51234';
	const local = (p, text) => mod.classifyFailure({ url: apiBase + p, text }, apiBase);

	assert.equal(local('/api/versions/remote', 'Failed to load resource: the server responded with a status of 500'), 'productLocal', 'RED：未知 500 必须阻断');
	assert.equal(local('/api/content/search', 'status of 502'), 'productLocal', 'RED：502 不得自动当"代理超时"放行');
	assert.equal(local('/api/news', 'status of 503'), 'productLocal', 'RED：503 不得自动放行');
	assert.equal(local('/api/news', 'status of 504'), 'productLocal', 'RED：504 不得自动放行');
	assert.equal(local('/api/accounts/x/skin-image', '404'), 'skinImageNoAccount', '账号皮肤 404 仍是已知无账号');
	assert.equal(local('/api/news-image?id=x', '400'), 'newsImageProxy', '新闻图片代理是已知类别（正文另判）');
	assert.equal(local('/api/content/icon?projectId=x', '404'), 'contentIconUpstream', '图标上游缺失是已知类别');
	assert.equal(local('/js/launcher.js', '404'), 'uiAsset', '界面自身资源失败是产品问题');
	assert.equal(local('/', '500'), 'uiAsset', '首页失败属界面资源');
	assert.equal(local('/favicon.ico', '404'), 'cosmetic', 'favicon 属外观级');
	assert.equal(mod.classifyFailure({ url: 'https://launchercontent.mojang.com/x', text: '500' }, apiBase), 'external', '非本机地址仍是外部请求');
});

/* ============== 9. 正文证据才可改判外部依赖 ============== */
test('CV-07: 只有完整正文明确证明的上游网络错误才可列外部依赖', () => {
	assert.equal(mod.evaluateFailureBody('{"error":"dns_private"}').upstreamNetwork, true, 'dns_private 是正文证据');
	assert.equal(mod.evaluateFailureBody('{"error":"getaddrinfo ENOTFOUND host"}').upstreamNetwork, true, 'DNS 解析失败是正文证据');
	assert.equal(mod.evaluateFailureBody('upstream connect timeout').upstreamNetwork, true, '上游连接超时是正文证据');
	assert.equal(mod.evaluateFailureBody('socket hang up').upstreamNetwork, true, '连接被断开是正文证据');
	assert.equal(mod.evaluateFailureBody('{"error":"internal server error"}').upstreamNetwork, false, '通用 500 正文不构成外部依赖证据');
	assert.equal(mod.evaluateFailureBody('{"error":"boom"}').upstreamNetwork, false, '未知正文不构成证据');
	assert.equal(mod.evaluateFailureBody('').upstreamNetwork, false, '空正文不构成证据');
	assert.equal(mod.hasUpstreamNetworkEvidence('plain text'), false);
});

test('CV-07: decode Chinese error fields, reject network keywords in unrelated JSON notes', () => {
	const message = '无法连接 Minecraft 版本下载源，已自动尝试官方和镜像地址。';
	const escaped = JSON.stringify({ error: message }).replace(/[\u007f-\uffff]/g, c => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'));
	assert.equal(mod.evaluateFailureBody(escaped).upstreamNetwork, true);
	assert.equal(mod.evaluateFailureBody(JSON.stringify({ error: { message: '不知道这样的主机。 (api.modrinth.com:443)' } })).upstreamNetwork, true);
	assert.equal(mod.evaluateFailureBody(JSON.stringify({ error: '内部错误', note: 'getaddrinfo ENOTFOUND' })).upstreamNetwork, false);
	assert.equal(mod.evaluateFailureBody(JSON.stringify({ error: 'x'.repeat(400) + ' DNS lookup failed' })).upstreamNetwork, true);
});

test('CV-07: redact structured credentials and all cookies while retaining boolean statistics', () => {
	const report = { token: 'a', nested: { Cookie: 'sid=first; second=secret', Authorization: 'Basic tiny', access_token: { secret: 'nested' } }, sessionTokenPresent: true };
	const safe = mod.redactReport(report);
	assert.equal(safe.token, '<REDACTED>'); assert.equal(safe.nested.Cookie, '<REDACTED>');
	assert.equal(safe.nested.Authorization, '<REDACTED>'); assert.equal(safe.nested.access_token, '<REDACTED>');
	assert.equal(safe.sessionTokenPresent, true);
	const headers = mod.redactSensitive('Cookie: sid=first; second=secret\r\nContent-Type: application/json');
	assert(!headers.includes('first') && !headers.includes('secret')); assert(headers.includes('Content-Type'));
	assert(!mod.redactSensitive(JSON.stringify(report)).includes('second='));
	for (const scheme of ['Basic', 'Bearer', 'Negotiate']) {
		const header = mod.redactSensitive('Authorization: ' + scheme + ' abc\r\nContent-Type: application/json');
		assert(!header.includes('abc')); assert(header.includes('Content-Type'));
	}
});

/* ============== 10. 先完整正文判断，再截断展示 ============== */
test('CV-07: 读错误响应先用完整正文判断，再截断展示', () => {
	const padding = 'x'.repeat(400);
	const body = `${padding} getaddrinfo ENOTFOUND upstream.host`;
	const verdict = mod.evaluateFailureBody(body);
	assert.equal(verdict.upstreamNetwork, true, 'RED：证据在第 200 字节之后仍必须被识别（不能先截断再判断）');
	assert.equal(verdict.fullLength, body.length, '应记录完整正文长度');
	assert.equal(verdict.display.length, mod.FAILURE_BODY_DISPLAY_LIMIT, '展示片段必须截断到上限');
	assert.equal(verdict.display.includes('getaddrinfo'), false, '展示片段确实不含证据（证明判断用的是完整正文）');
});

/* ============== 11. 报告脱敏 ============== */
test('CV-07: 报告脱敏 token / Cookie / Bearer / 查询串敏感参数', () => {
	const secret = 'ABCDEF0123456789';

	assert.match(mod.redactSensitive(`X-Pineglow-Token: ${secret}`), /<REDACTED>/, '自定义令牌头应脱敏');
	assert.equal(mod.redactSensitive(`X-Pineglow-Token: ${secret}`).includes(secret), false);
	assert.equal(mod.redactSensitive(`Authorization: Bearer ${secret}`).includes(secret), false, 'Bearer 令牌应脱敏');
	assert.equal(mod.redactSensitive(`{"authorization":"Bearer ${secret}"}`).includes(secret), false, 'JSON 里的 Bearer 应脱敏');
	assert.equal(mod.redactSensitive(`Cookie: session=${secret}; theme=dark`).includes(secret), false, 'Cookie 应脱敏');
	assert.equal(mod.redactSensitive(`{"token":"${secret}"}`).includes(secret), false, 'JSON token 字段应脱敏');
	assert.equal(mod.redactSensitive(`name="pineglow-session-token" content="${secret}"`).includes(secret), false, 'HTML meta 里的会话令牌应脱敏');

	const url = `http://127.0.0.1:1/api/x?token=${secret}&version=1`;
	const safeUrl = mod.redactSensitive(url);
	assert.equal(safeUrl.includes(secret), false, '查询串里的 token 应脱敏');
	assert.match(safeUrl, /version=1/, '非敏感参数应保留（便于定位问题）');

	const report = { a: { b: [`?access_token=${secret}`, 'ok'] }, n: 3, flag: true };
	const safe = mod.redactReport(report);
	assert.equal(JSON.stringify(safe).includes(secret), false, '递归脱敏必须覆盖嵌套字段');
	assert.equal(safe.n, 3);
	assert.equal(safe.flag, true);
});

/* ============== 12. URL 自带敏感值不重发 ============== */
test('CV-07: URL 自带敏感值时不再重发该请求，只记录脱敏后的形状', () => {
	const secret = 'SECRETVALUE0123456789';
	const plan = mod.planApiFailureProbe(`http://127.0.0.1:1/api/x?token=${secret}`);
	assert.equal(plan.refetch, false, 'RED：不得重新发送带 token 的 URL 再把它写进归档');
	assert.equal(plan.reason, 'url-carries-secret');
	assert.equal(plan.url.includes(secret), false, '记录的 URL 必须已脱敏');
	assert.match(plan.url, /<REDACTED>/);

	const clean = mod.planApiFailureProbe('http://127.0.0.1:1/api/news-image?id=x');
	assert.equal(clean.refetch, true, '不含敏感值的 URL 才允许复采正文');
	assert.equal(clean.url, 'http://127.0.0.1:1/api/news-image?id=x');

	assert.equal(mod.planApiFailureProbe('').refetch, false, '空 URL 不复采');
	assert.equal(mod.planApiFailureProbe(null).refetch, false, 'null URL 不复采');
});

/* ============== 13. 源码守卫 ============== */
test('CV-07: 验收主流程只在本文件被直接运行时执行（require 时不得启动真实 EXE）', () => {
	assert.match(SRC, /require\.main === module/, '必须保留 require.main 守卫，否则测试 require 该文件会启动真实 EXE');
	assert.equal(typeof mod.connectCdp, 'function');
	assert.equal(typeof mod.classifyViewState, 'function');
	assert.equal(typeof mod.redactReport, 'function');
	assert.equal(mod.VIEWS.length, 9, '九个页面的巡检范围不变');
});

test('CV-07: 旧缺陷代码形态已被移除（pending 必须保存 method）', () => {
	assert.doesNotMatch(SRC, /rej\(new Error\(`\$\{msg\.error\.message \|\| 'CDP error'\} \(\$\{method \|\| ''\}\)`\)\)/,
		'RED：不得再引用作用域里不存在的 method');
	assert.match(SRC, /pending\.set\(id, \{ res, rej, method \}\)/, '每个 pending 请求必须保存自己的 method');
	assert.doesNotMatch(SRC, /status === '502' \|\| status === '503'/, 'RED：不得再按状态码把 5xx 一律放行');
	assert.doesNotMatch(SRC, /slice\(0,\s*200\);\s*\n\s*report\.apiFailureBodies\.push\(\{ url: item\.url/,
		'RED：不得先截断正文再判断');
});
