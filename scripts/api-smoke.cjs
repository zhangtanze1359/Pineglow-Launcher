#!/usr/bin/env node
/*
 * api-smoke.cjs —— 对真实单文件 EXE 做**跨域 API 冒烟**，逐项验证功能是否正常工作。
 *
 * 为什么单独写一个：现有门槛各有侧重——
 *   verify-single-file-standalone.ps1  证明「资源能取到、MIME 对、字节与源码一致」
 *   verify-ui-pages-live.cjs           证明「10 个视图在真实 WebView2 里渲染出来」
 * 两者都不逐项验证**接口本身**是否返回合理的结构与语义。这个脚本补这一段：
 * 起真实进程 → 从 index.html 的 meta 取会话令牌 → 对每个功能域打真实 HTTP 请求，
 * 断言状态码 + 关键字段存在 + 错误语义正确（该 400 的必须 400，不能是兜底 404）。
 *
 * 只读为主：除「创建实例 → 删除实例」这一对自带清理的写操作外，不触碰用户的游戏目录。
 *
 * 退出码：0 = 全部通过；1 = 有失败；2 = 环境/用法错误。
 */

'use strict';

const fs = require('fs');
const os = require('os');
const net = require('net');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');

const args = { app: null, json: null, out: null };
for (let i = 2; i < process.argv.length; i++) {
	const a = process.argv[i];
	if (a === '--app') args.app = process.argv[++i];
	else if (a === '--json') args.json = process.argv[++i];
	else if (a === '--out') args.out = process.argv[++i];
}
if (!args.app) {
	console.error('用法: node api-smoke.cjs --app <EXE> [--json report.json] [--out dir]');
	process.exit(2);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function freePort() {
	return new Promise((resolve, reject) => {
		const s = net.createServer();
		s.unref();
		s.on('error', reject);
		s.listen(0, '127.0.0.1', () => {
			const p = s.address().port;
			s.close(() => resolve(p));
		});
	});
}

function req(port, method, urlPath, token, body) {
	return new Promise((resolve) => {
		const data = body === undefined ? null : Buffer.from(JSON.stringify(body), 'utf8');
		const headers = {};
		if (token) headers['X-Pineglow-Token'] = token;
		if (data) {
			headers['Content-Type'] = 'application/json';
			headers['Content-Length'] = String(data.length);
		}
		const r = http.request(
			{ host: '127.0.0.1', port, method, path: urlPath, headers, timeout: 20000 },
			(res) => {
				const chunks = [];
				res.on('data', (c) => chunks.push(c));
				res.on('end', () => {
					const text = Buffer.concat(chunks).toString('utf8');
					let json = null;
					try { json = JSON.parse(text); } catch { /* 非 JSON 也算一种结果，由断言决定 */ }
					resolve({ status: res.statusCode, text, json, headers: res.headers });
				});
			}
		);
		r.on('timeout', () => { r.destroy(new Error('timeout')); });
		r.on('error', (e) => resolve({ status: 0, text: '', json: null, error: e.message }));
		if (data) r.write(data);
		r.end();
	});
}

const results = [];
function check(name, ok, detail) {
	results.push({ name, ok: !!ok, detail: detail === undefined ? '' : String(detail) });
	const tag = ok ? 'OK  ' : 'FAIL';
	console.log(`[${tag}] ${name}${detail ? ' :: ' + detail : ''}`);
}

async function main() {
	const workRoot = path.join(os.tmpdir(), '松光 API 冒烟 ' + Math.random().toString(36).slice(2, 10));
	fs.mkdirSync(workRoot, { recursive: true });
	const exe = path.join(workRoot, 'launcher.exe');
	fs.copyFileSync(args.app, exe);
	fs.writeFileSync(path.join(workRoot, 'portable.flag'), '');

	const port = await freePort();
	const env = {
		...process.env,
		PINEGLOW_API_PORT: String(port),
		DOTNET_BUNDLE_EXTRACT_BASE_DIR: path.join(workRoot, 'extract'),
	};
	delete env.PINEGLOW_UI_DIR;
	delete env.PINEGLOW_WEBVIEW_DEBUG;

	const child = spawn(exe, [], { cwd: workRoot, env, stdio: ['ignore', 'pipe', 'pipe'] });
	let stderr = '';
	child.stderr.on('data', (d) => { stderr += d.toString('utf8'); });
	const cleanup = () => {
		try { child.kill(); } catch { /* ignore */ }
	};
	process.on('exit', cleanup);

	// ---- 等 /api/health（不需要令牌）----
	let health = null;
	const deadline = Date.now() + 90000;
	while (Date.now() < deadline) {
		const r = await req(port, 'GET', '/api/health', null);
		if (r.status === 200 && r.json && r.json.ok === true) { health = r.json; break; }
		await sleep(400);
	}
	check('启动：/api/health 就绪', !!health, health ? `pid=${health.pid} port=${health.port}` : `stderr=${stderr.slice(0, 200)}`);
	if (!health) return finish(1);

	// ---- 取会话令牌：从 index.html 的 meta 里读 ----
	const html = await req(port, 'GET', '/', null);
	const m = /name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"/.exec(html.text || '');
	const token = m ? m[1] : null;
	check('安全：index.html 注入了会话令牌', !!token, token ? '长度=' + token.length : '未找到 meta');

	// ---- 安全边界三项 ----
	const noToken = await req(port, 'GET', '/api/state', null);
	check('安全：无令牌访问 /api/state 返回 401', noToken.status === 401, 'status=' + noToken.status);

	const badToken = await req(port, 'GET', '/api/state', 'DEADBEEF');
	check('安全：错误令牌返回 401', badToken.status === 401, 'status=' + badToken.status);

	const badOrigin = await new Promise((resolve) => {
		const r = http.request(
			{ host: '127.0.0.1', port, method: 'GET', path: '/api/state', headers: { Origin: 'http://evil.example.com', 'X-Pineglow-Token': token }, timeout: 10000 },
			(res) => { res.resume(); res.on('end', () => resolve(res.statusCode)); }
		);
		r.on('error', () => resolve(0));
		r.end();
	});
	check('安全：非白名单 Origin 被拒（403）', badOrigin === 403, 'status=' + badOrigin);

	const corsAny = html.headers['access-control-allow-origin'];
	check('安全：CORS 不写 * （只回显校验通过的 Origin）', corsAny !== '*', 'ACAO=' + String(corsAny));

	// ---- 交叉域只读接口 ----
	const getters = [
		['系统', 'GET', '/api/state', (j) => j && typeof j === 'object'],
		['实例', 'GET', '/api/instances', (j) => Array.isArray(j) || (j && Array.isArray(j.instances))],
		['账号', 'GET', '/api/accounts', (j) => Array.isArray(j) || (j && Array.isArray(j.accounts))],
		['版本(本地)', 'GET', '/api/versions', (j) => j !== null],
		['更新配置/检查', 'GET', '/api/update/check', (j) => j !== null],
		['更新状态', 'GET', '/api/update', (j) => j !== null],
		['配置回读', 'GET', '/api/state', (j) => j && j.config !== undefined],
		['Java 列表', 'GET', '/api/java', (j) => j !== null],
		['统计', 'GET', '/api/stats', (j) => j !== null],
		['下载来源', 'GET', '/api/sources', (j) => j !== null],
		['服务器列表', 'GET', '/api/servers', (j) => j !== null],
		['新闻', 'GET', '/api/news', (j) => j !== null],
		['下载任务', 'GET', '/api/downloads', (j) => j !== null],
		['内容任务', 'GET', '/api/content/tasks', (j) => j !== null],
		['世界列表', 'GET', '/api/worlds', (j) => j !== null],
	];
	for (const [domain, method, url, shape] of getters) {
		const r = await req(port, method, url, token);
		const okShape = r.json !== null && shape(r.json);
		check(`${domain}：${method} ${url}`, r.status === 200 && okShape, `status=${r.status}${okShape ? '' : ' body=' + (r.text || '').slice(0, 120)}`);
	}

	// ---- 语义断言：不存在的实例必须 400/404 而非兜底 404 ----
	const missing = await req(port, 'POST', '/api/instances/__no_such_instance__/launch', token, {});
	const msg = (missing.json && (missing.json.error || missing.json.message)) || missing.text || '';
	check(
		'错误语义：启动不存在的实例给业务错误（含实例不存在），不是路由兜底 404',
		missing.status === 400 || missing.status === 404,
		`status=${missing.status} msg=${String(msg).slice(0, 80)}`
	);
	check('错误语义：错误信息不是「未找到接口」（路由兜底标记）', !String(msg).includes('未找到接口'), 'msg=' + String(msg).slice(0, 80));

	// ---- 写操作闭环：创建实例 → 查详情 → 删除 ----
	const create = await req(port, 'POST', '/api/instances', token, { name: 'api-smoke-' + Date.now(), version: '1.20.1' });
	const createdId = create.json && (create.json.id || (create.json.instance && create.json.instance.id));
	check('实例：创建实例返回 id', create.status === 200 && !!createdId, `status=${create.status} id=${createdId || 'n/a'} body=${(create.text || '').slice(0, 100)}`);

	if (createdId) {
		const detail = await req(port, 'GET', `/api/instances/${encodeURIComponent(createdId)}`, token);
		check('实例：读取新建实例详情', detail.status === 200, `status=${detail.status}`);
		// 删除走 POST /instances/{id}/delete（DELETE 方法没有登记路由，前端 api.js:883 也用的 POST）
		const del = await req(port, 'POST', `/api/instances/${encodeURIComponent(createdId)}/delete`, token, {});
		check('实例：删除新建实例成功', del.status === 200, `status=${del.status} body=${(del.text || '').slice(0, 80)}`);
	}

	// ---- 启动前自检（真实读磁盘/Java，验证 preflight 链路）----
	const inst = await req(port, 'GET', '/api/instances', token);
	const first = (Array.isArray(inst.json) ? inst.json : inst.json && inst.json.instances && inst.json.instances[0]) || null;
	const firstId = first && (first.id || first.Id);
	if (firstId) {
		const pre = await req(port, 'GET', `/api/instances/${encodeURIComponent(firstId)}/prelaunch-check`, token);
		const hasChecks = pre.json && (Array.isArray(pre.json.checks) || Array.isArray(pre.json.items) || pre.json.ok !== undefined);
		check('启动前自检：返回检查项结构', pre.status === 200 && !!hasChecks, `status=${pre.status} body=${(pre.text || '').slice(0, 120)}`);
	} else {
		check('启动前自检：返回检查项结构', false, '未取到任何实例 id，无法验证');
	}

	// ---- 静态资源与 SPA 回退 ----
	const css = await req(port, 'GET', '/css/figma.css', null);
	check('静态：CSS 以 text/css 提供', css.status === 200 && String(css.headers['content-type']).includes('text/css'), `status=${css.status} type=${css.headers['content-type']}`);

	const deep = await req(port, 'GET', '/nonexistent-deep-link', null);
	check('静态：未知路径返回 404 而不是误投文件', deep.status === 404, `status=${deep.status}`);

	const traversal = await req(port, 'GET', '/../app.manifest', null);
	check('安全：路径穿越被拦（不返回 manifest 内容）', !String(traversal.text || '').includes('<assembly'), `status=${traversal.status}`);

	// ---- 进程仍然健康（上面那么多请求之后）----
	const health2 = await req(port, 'GET', '/api/health', null);
	check('稳定性：全部请求后进程仍健康', health2.status === 200 && health2.json && health2.json.ok === true, 'status=' + health2.status);

	return finish(0);
}

function finish(code) {
	const failed = results.filter((r) => !r.ok);
	const report = {
		generatedAt: new Date().toISOString(),
		app: args.app,
		total: results.length,
		passed: results.length - failed.length,
		failed: failed.length,
		verdict: failed.length === 0 ? 'PASS' : 'FAIL',
		results,
	};
	if (args.json) {
		fs.mkdirSync(path.dirname(args.json), { recursive: true });
		fs.writeFileSync(args.json, JSON.stringify(report, null, 2), 'utf8');
	}
	console.log(`\nAPI-SMOKE RESULT=${report.verdict} passed=${report.passed}/${report.total}`);
	if (failed.length) {
		console.log('失败项:');
		for (const f of failed) console.log('  - ' + f.name + ' :: ' + f.detail);
	}
	process.exit(failed.length === 0 ? 0 : 1);
}

main().catch((e) => {
	console.error('未捕获异常:', e && e.stack || e);
	process.exit(1);
});
