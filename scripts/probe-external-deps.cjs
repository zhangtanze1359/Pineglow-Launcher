#!/usr/bin/env node
/*
 * probe-external-deps.cjs —— 产品依赖的外部端点可达性探测
 *
 * 由来（任务 #13：功能域逐一可执行验证）：
 *   本轮发现的最大产品级问题是「默认更新源 Gitee 的 update.json 从未创建」——
 *   代码里写着一个地址，线上那个文件根本不存在，而**没有任何自动化会发现这件事**，
 *   因为单元测试用的是假处理器（正确的做法），真实地址只在用户机器上才被访问。
 *
 *   同理还有一批端点：Fabric/Quilt meta、Mojang 清单、Modrinth、Adoptium JDK、
 *   Forge/NeoForge maven、authlib-injector、官方新闻。它们任何一个挂掉，
 *   对应功能就整块不可用，但仓库里没有任何一处会告诉你"现在哪个是坏的"。
 *
 * 本脚本不做断言、不进 CI 门槛（网络抖动会让它假失败），它是**诊断工具**：
 *   node scripts/probe-external-deps.cjs            # 人类可读
 *   node scripts/probe-external-deps.cjs --json f   # 写机器可读结果
 *
 * 退出码：0 = 全部可达；1 = 有端点不可达（供本地/发版前手工卡口用）。
 *
 * 判定口径（刻意的）：
 *   - 只判 HTTP 状态与耗时，不解析业务语义（清单内容是否合理另有测试覆盖）。
 *   - 代理导致的 502/连接失败与"目标真挂"在 curl 层难以区分，所以**同时**打印
 *     URL 和错误首行，让人自己判断是网络问题还是地址问题。
 *   - 每个端点标注它支撑哪个功能、挂掉后的降级行为——这才是"哪个坏了影响什么"。
 */

'use strict';

const { spawnSync } = require('node:child_process');

const TIMEOUT_SEC = 25;

/**
 * 端点清单。covered = 该端点不可用时的降级行为。
 * required = true 表示没有任何降级，挂了就是功能整块不可用。
 */
const ENDPOINTS = [
	// ---- 更新链路（本轮已实测主源 404）----
	{
		id: 'update-primary',
		url: 'https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json',
		feature: '更新检查（主源）',
		required: false,
		covered: '落到 GitHub Release 的 manifest.json 备用源',
	},
	{
		id: 'update-mirror',
		url: 'https://github.com/zhangtanze1359/Pineglow-Launcher/releases/latest/download/manifest.json',
		feature: '更新检查（备用源）',
		required: false,
		covered: '两个源都挂则更新检查不可用，但启动与游玩不受影响',
	},

	// ---- 游戏版本与资源下载 ----
	{
		id: 'mojang-manifest',
		url: 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json',
		feature: '原版版本列表 / 游戏文件下载',
		required: true,
		covered: '可由镜像（BMCLAPI 等）代取，取决于用户在设置里选的下载源',
	},

	// ---- 加载器 ----
	{
		id: 'fabric-game',
		url: 'https://meta.fabricmc.net/v2/versions/game',
		feature: 'Fabric 可选游戏版本',
		required: true,
		covered: '无降级：Fabric 安装页整块不可用',
	},
	{
		id: 'fabric-loader',
		url: 'https://meta.fabricmc.net/v2/versions/loader',
		feature: 'Fabric Loader 版本列表',
		required: true,
		covered: '无降级',
	},
	{
		id: 'quilt-game',
		url: 'https://meta.quiltmc.org/v3/versions/game',
		feature: 'Quilt 可选游戏版本',
		required: false,
		covered: '无降级，但 Quilt 是次要加载器',
	},
	{
		id: 'forge-maven',
		url: 'https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml',
		feature: 'Forge 版本列表',
		required: true,
		covered: '无降级',
	},
	{
		id: 'neoforge-maven',
		url: 'https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml',
		feature: 'NeoForge 版本列表',
		required: true,
		covered: '无降级',
	},

	// ---- 内容库 ----
	{
		id: 'modrinth-search',
		url: 'https://api.modrinth.com/v2/search?query=sodium&limit=1',
		feature: '内容中心搜索（Mod/整合包/资源包/光影）',
		required: true,
		covered: '无降级：内容中心整块不可用',
	},
	{
		id: 'curseforge-api',
		url: 'https://api.curseforge.com/v1/mods/search?gameId=432&searchFilter=sodium',
		feature: 'CurseForge 搜索（可选源）',
		required: false,
		covered: '无 Key 时本来就不用；Modrinth 是主源',
	},

	// ---- Java 运行时 ----
	{
		id: 'adoptium',
		url: 'https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse',
		feature: '自动下载 Java 运行时',
		required: false,
		covered: '用户可手动指定本地 Java；纯离线环境必然走这条路',
	},
	{
		id: 'authlib-injector',
		url: 'https://authlib-injector.yushi.moe/artifact/53/authlib-injector-1.2.5.jar',
		feature: '外置登录（Authlib-Injector agent）',
		required: false,
		covered: '无降级：外置登录不可用，离线/正版不受影响',
	},

	// ---- 新闻 ----
	{
		id: 'mojang-news',
		url: 'https://launchercontent.mojang.com/news.json',
		feature: '首页官方新闻',
		required: false,
		covered: '显示错误态 + 重试入口（绝不用静态假新闻兜底）',
	},

	// ---- AI（可选功能）----
	{
		id: 'deepseek',
		url: 'https://api.deepseek.com/v1/models',
		feature: 'AI 崩溃诊断（需用户自备 Key）',
		required: false,
		covered: '未配置 Key 时功能不启用；401 也算"端点可达"',
	},
];

function probe(endpoint) {
	const started = Date.now();
	// 用 GET + 只取头，避免部分 CDN 对 HEAD 返回 405 造成误判
	const result = spawnSync('curl', [
		'-s', '-o', process.platform === 'win32' ? 'NUL' : '/dev/null',
		'-w', '%{http_code}',
		'-L', '--max-time', String(TIMEOUT_SEC),
		endpoint.url,
	], { encoding: 'utf8', timeout: (TIMEOUT_SEC + 10) * 1000 });

	const raw = (result.stdout || '').trim();
	const code = Number(raw) || 0;
	const ms = Date.now() - started;
	const stderrHead = (result.stderr || '').trim().split('\n').filter(Boolean)[0] || '';

	return {
		...endpoint,
		httpCode: code,
		ms,
		reachable: code >= 200 && code < 400,
		// 401/403 说明端点活着、只是要凭据，对"可达性"来说算通过
		authGated: code === 401 || code === 403,
		error: code === 0 ? (stderrHead || `超时/连接失败（${TIMEOUT_SEC}s）`) : null,
	};
}

function main() {
	const argv = process.argv.slice(2);
	let jsonOut = null;
	for (let i = 0; i < argv.length; i++) {
		if (argv[i] === '--json') jsonOut = argv[++i];
		else if (argv[i] === '--help' || argv[i] === '-h') {
			console.log('用法: node scripts/probe-external-deps.cjs [--json <path>]');
			return 0;
		}
	}

	const results = ENDPOINTS.map(probe);

	const pad = (s, n) => {
		const w = [...String(s)].reduce((a, c) => a + (c.charCodeAt(0) > 255 ? 2 : 1), 0);
		return String(s) + ' '.repeat(Math.max(0, n - w));
	};

	console.log('外部依赖可达性探测  ' + new Date().toISOString());
	console.log('='.repeat(100));
	console.log(pad('端点', 22) + pad('HTTP', 7) + pad('耗时', 9) + pad('结果', 10) + '支撑功能 / 降级');
	console.log('-'.repeat(100));
	for (const r of results) {
		let status;
		if (r.reachable) status = 'OK';
		else if (r.authGated) status = '需凭据';
		else status = '不可达';
		const detail = r.reachable || r.authGated
			? r.feature + (r.required ? '（无降级）' : '（' + r.covered + '）')
			: r.feature + ' —— ' + (r.error || 'HTTP ' + r.httpCode);
		console.log(pad(r.id, 22) + pad(r.httpCode || '---', 7) + pad(r.ms + 'ms', 9) + pad(status, 10) + detail);
	}
	console.log('-'.repeat(100));

	const hardFailures = results.filter((r) => !r.reachable && !r.authGated);
	const requiredFailures = hardFailures.filter((r) => r.required);
	const optionalFailures = hardFailures.filter((r) => !r.required);

	console.log(`可达 ${results.length - hardFailures.length}/${results.length}，不可达 ${hardFailures.length}`);
	if (requiredFailures.length) {
		console.log('\n【无降级的功能已不可用】');
		for (const r of requiredFailures) console.log('  - ' + r.id + '：' + r.feature);
	}
	if (optionalFailures.length) {
		console.log('\n【有降级，但用户会看到错误态】');
		for (const r of optionalFailures) console.log('  - ' + r.id + '：' + r.covered);
	}
	if (requiredFailures.length === 0 && optionalFailures.length === 0) {
		console.log('\n全部端点可达。');
	}
	console.log('\n注意：本机走 HTTP 代理，CONNECT 502 与目标真挂在这一层无法区分。');

	if (jsonOut) {
		const fs = require('node:fs');
		fs.writeFileSync(jsonOut, JSON.stringify({ at: new Date().toISOString(), results }, null, 2), 'utf8');
		console.log('已写入 ' + jsonOut);
	}

	return requiredFailures.length === 0 ? 0 : 1;
}

process.exit(main());
