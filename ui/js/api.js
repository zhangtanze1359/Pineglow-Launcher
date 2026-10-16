/* ================= API 层（live：串通 C# LocalApiService；mock：file:// 验收用） ================= */
window.SG = window.SG || {};

SG.pages = {};
SG.registerPage = (name, def) => { SG.pages[name] = def; };

// The shipped UI must never display sample accounts or simulated successes after API startup fails.
// Standalone test/demo contexts without the product marker retain their explicit mock seam.
const productUi = document.documentElement && document.documentElement.getAttribute && document.documentElement.getAttribute('data-pineglow-product') === 'true';
SG.apiMode = productUi || (typeof location !== 'undefined' && location.protocol === 'http:') ? 'live' : 'mock';
SG.apiToken = (document.querySelector('meta[name="pineglow-session-token"]') || {}).content || '';

/* ================= mock 数据（验收 / 降级兜底） ================= */
SG.mock = {
	settings: {
		gameDirectory: 'D:\\games\\.minecraft',
		javaPath: '',
		playerName: 'Player',
		selectedVersion: '1.21.1',
		maxMemoryMb: 4096,
		useAutoMemory: true,
		windowWidth: 1280,
		windowHeight: 720,
		serverHost: '',
		serverPort: '25565',
		accountMode: 'offline',
		useMirror: false,
		modSource: 'Modrinth',
		curseForgeApiKeyConfigured: false,
		aiDiagnosisEnabled: false,
		selectedAiProviderId: 'p1',
		aiProviders: [
			{ id: 'p1', name: 'DeepSeek', baseUrl: 'https://api.deepseek.com/v1', model: 'deepseek-chat', keyConfigured: false },
		],
		// R2-02：新闻图片 fake-ip/TUN 兼容开关（默认关闭，fail closed）
		newsFakeIpCompat: false,
	},
	memoryRecommendation: { minimumMb: 2048, recommendedMb: 4096, maximumMb: 16384, formatText: '4 GB · 智能' },
	instances: [
		{ id: 'a1', name: 'Fabulously Optimized', loader: 'Fabric', version: '1.21.1', icon: '🌳', iconBg: 'linear-gradient(135deg,#4db6ac,#2e7d6b)', lastPlayed: '2 天前游玩', recent: true, modsCount: 87, savesCount: 3, gameDir: 'D:\\games\\.minecraft' },
		{ id: 'a2', name: 'ATM9: To the Sky', loader: 'Forge', version: '1.20.1', icon: '🎈', iconBg: 'linear-gradient(135deg,#f9a8d4,#be185d)', lastPlayed: '昨天游玩', recent: true, modsCount: 214, savesCount: 2, gameDir: 'D:\\games\\.minecraft' },
		{ id: 'a3', name: 'Create: Above and Beyond', loader: 'Forge', version: '1.18.2', icon: '⚙️', iconBg: 'linear-gradient(135deg,#90c4ff,#1f5ff1)', lastPlayed: '3 天前游玩', recent: true, modsCount: 168, savesCount: 4, gameDir: 'D:\\games\\.minecraft' },
		{ id: 'a4', name: 'Vanilla', loader: 'Vanilla', version: '1.21.4', icon: '⛏️', iconBg: 'linear-gradient(135deg,#81f4af,#0faa4f)', lastPlayed: '5 天前游玩', recent: true, modsCount: 0, savesCount: 1, gameDir: 'D:\\games\\.minecraft' },
		{ id: 'a5', name: 'SkyFactory 5', loader: 'NeoForge', version: '1.21.1', icon: '🌌', iconBg: 'linear-gradient(135deg,#dab2ff,#821ed2)', lastPlayed: '1 周前游玩', recent: false, modsCount: 122, savesCount: 1, gameDir: 'D:\\games\\.minecraft' },
		{ id: 'a6', name: 'Better MC', loader: 'Forge', version: '1.20.1', icon: '🏰', iconBg: 'linear-gradient(135deg,#ffc171,#ef6307)', lastPlayed: '1 周前游玩', recent: false, modsCount: 145, savesCount: 5, gameDir: 'D:\\games\\.minecraft' },
	],
	worlds: [
		{ name: '我的生存世界', instance: 'Fabulously Optimized', icon: '🏡', iconBg: 'linear-gradient(135deg,#b8fad2,#0f8642)', lastPlayed: '2 天前', size: '1.2 GB' },
		{ name: '红石实验室', instance: 'Create: Above and Beyond', icon: '🔴', iconBg: 'linear-gradient(135deg,#ffcad3,#b7193d)', lastPlayed: '3 天前', size: '856 MB' },
		{ name: '天空岛基地', instance: 'ATM9: To the Sky', icon: '🏝️', iconBg: 'linear-gradient(135deg,#90c4ff,#1749de)', lastPlayed: '昨天', size: '2.4 GB' },
		{ name: '末地飞船', instance: 'Vanilla', icon: '🚀', iconBg: 'linear-gradient(135deg,#dab2ff,#5b21b6)', lastPlayed: '5 天前', size: '210 MB' },
		{ name: '暮色森林探索', instance: 'SkyFactory 5', icon: '🌲', iconBg: 'linear-gradient(135deg,#81f4af,#0f8642)', lastPlayed: '1 周前', size: '1.8 GB' },
		{ name: '海景小镇', instance: 'Better MC', icon: '🌊', iconBg: 'linear-gradient(135deg,#90c4ff,#1e3a8a)', lastPlayed: '3 周前', size: '3.1 GB' },
		{ name: '空岛极限', instance: 'ATM9: To the Sky', icon: '⛰️', iconBg: 'linear-gradient(135deg,#ffc171,#b45309)', lastPlayed: '1 个月前', size: '940 MB' },
		{ name: '地下城', instance: 'Fabulously Optimized', icon: '🕯️', iconBg: 'linear-gradient(135deg,#e2d5ff,#6d28d9)', lastPlayed: '2 个月前', size: '1.5 GB' },
	],
	servers: [
		{ id: 's1', name: '松光官方生存服', address: 'play.songguang.cn', port: 25565, online: 12, max: 200, online2: true, icon: '🏳️', iconBg: 'linear-gradient(135deg,#81f4af,#0a7141)', version: '1.21.x', latency: 23 },
		{ id: 's2', name: '建筑工坊', address: 'build.songguang.cn', port: 25565, online: 8, max: 100, online2: true, icon: '🏗️', iconBg: 'linear-gradient(135deg,#ffdba8,#a44419)', version: '1.20.x', latency: 41 },
		{ id: 's3', name: '红石科技服', address: 'tech.songguang.cn', port: 25565, online: 0, max: 50, online2: false, icon: '⚡', iconBg: 'linear-gradient(135deg,#90c4ff,#193861)', version: '1.21.x', latency: null },
		{ id: 's4', name: '纯净小游戏', address: 'minigame.songguang.cn', port: 25565, online: 23, max: 80, online2: true, icon: '🎮', iconBg: 'linear-gradient(135deg,#f9a8d4,#9d174d)', version: '1.21.x', latency: 35 },
		{ id: 's5', name: '暮色冒险服', address: 'twilight.songguang.cn', port: 25565, online: 3, max: 60, online2: true, icon: '🌙', iconBg: 'linear-gradient(135deg,#dab2ff,#5b21b6)', version: '1.20.x', latency: 52 },
		{ id: 's6', name: '创造建筑服', address: 'creative.songguang.cn', port: 25565, online: 0, max: 40, online2: false, icon: '🛠️', iconBg: 'linear-gradient(135deg,#b8fad2,#0f8642)', version: '1.21.x', latency: null },
	],
	content: [
		{ id: 'c1', title: 'Sodium', desc: '优化渲染引擎的免费开源 Mod，大幅提升帧率并减少卡顿。', author: 'jellysquid3', downloads: '12.4M', icon: '🧂', iconBg: 'linear-gradient(135deg,#ffdba8,#c2410c)', versions: ['1.21.x', '1.20.x', '1.19.x'], loader: 'Fabric', category: 'mod', rating: 4.9 },
		{ id: 'c2', title: 'Iris Shaders', desc: '与 Sodium 配合使用的现代光影加载器，支持 OptiFine 光影包。', author: 'coderbot', downloads: '8.1M', icon: '🌈', iconBg: 'linear-gradient(135deg,#dab2ff,#6d28d9)', versions: ['1.21.x', '1.20.x'], loader: 'Fabric', category: 'mod', rating: 4.8 },
		{ id: 'c3', title: 'Create', desc: '以机械为主题的模组，齿轮、传送带与自动化，重塑科技玩法。', author: 'simibubi', downloads: '15.2M', icon: '⚙️', iconBg: 'linear-gradient(135deg,#90c4ff,#1f5ff1)', versions: ['1.20.x', '1.18.x'], loader: 'Forge', category: 'mod', rating: 4.9 },
		{ id: 'c4', title: 'JEI（物品管理器）', desc: '查看所有物品的合成配方与用途，整合包必备。', author: 'mezz', downloads: '21.8M', icon: '📖', iconBg: 'linear-gradient(135deg,#b8fad2,#0f8642)', versions: ['1.21.x', '1.20.x', '1.18.x'], loader: 'Forge', category: 'mod', rating: 4.7 },
		{ id: 'c5', title: 'Fabric API', desc: 'Fabric 模组的核心 API 与钩子，大多数 Fabric Mod 的前置。', author: 'modmuss50', downloads: '45.3M', icon: '🧵', iconBg: 'linear-gradient(135deg,#e2d5ff,#7c3aed)', versions: ['1.21.x', '1.20.x'], loader: 'Fabric', category: 'mod', rating: 5.0 },
		{ id: 'c6', title: 'Complementary Shaders', desc: '色彩鲜明、开箱即用的光影包，性能与画质平衡。', author: 'EminGT', downloads: '3.6M', icon: '🖼️', iconBg: 'linear-gradient(135deg,#ffc171,#ea580c)', versions: ['1.21.x', '1.20.x'], loader: 'Shader', category: 'shader', rating: 4.8 },
		{ id: 'c7', title: 'XeKr 红石显示资源包', desc: '清晰显示红石信号的数值与状态，红石玩家福音。', author: 'XeKr', downloads: '2.1M', icon: '🔴', iconBg: 'linear-gradient(135deg,#ffcad3,#be123c)', versions: ['1.21.x', '1.20.x'], loader: 'Resource', category: 'resourcepack', rating: 4.6 },
		{ id: 'c8', title: 'Fabulously Optimized 整合包', desc: '高配置优化整合包，开箱即用的流畅体验。', author: 'Fabulously Optimized', downloads: '5.4M', icon: '🌳', iconBg: 'linear-gradient(135deg,#4db6ac,#0f766e)', versions: ['1.21.x'], loader: 'Fabric', category: 'modpack', rating: 4.9 },
		{ id: 'c9', title: 'Continuity', desc: '让玻璃、书架等方块呈现连接纹理的客户端 Mod。', author: 'PepperCode1', downloads: '4.8M', icon: '🔗', iconBg: 'linear-gradient(135deg,#b8fad2,#15803d)', versions: ['1.21.x', '1.20.x'], loader: 'Fabric', category: 'mod', rating: 4.5 },
		{ id: 'c10', title: 'Mod Menu', desc: '在游戏内查看与配置已安装 Mod 的界面。', author: 'TerraformersMC', downloads: '18.9M', icon: '🧩', iconBg: 'linear-gradient(135deg,#90c4ff,#1d4ed8)', versions: ['1.21.x', '1.20.x'], loader: 'Fabric', category: 'mod', rating: 4.8 },
		{ id: 'c11', title: 'Minimalist 光影', desc: '极简风格高性能光影，低配也能流畅运行。', author: 'Alex', downloads: '890K', icon: '🌅', iconBg: 'linear-gradient(135deg,#ffdba8,#f59e0b)', versions: ['1.21.x'], loader: 'Shader', category: 'shader', rating: 4.4 },
		{ id: 'c12', title: '精致像素资源包', desc: '全方块高清重绘，保留原版风格的高清纹理。', author: 'PixelPainter', downloads: '1.2M', icon: '🖌️', iconBg: 'linear-gradient(135deg,#e2d5ff,#7c3aed)', versions: ['1.21.x', '1.20.x'], loader: 'Resource', category: 'resourcepack', rating: 4.5 },
	],
	accounts: [
		{ id: 'u1', type: 'offline', userName: 'Alex', uuid: 'a94f5b10-2f8d-4b6e-9c1a-3d7e2f8b5c1d', skinCount: 2, capeCount: 1, selected: true, skins: [
			{ id: 'sk1', name: '经典 Alex', colors: { head: '#f0b98a', body: '#e8714f', arm: '#e8714f', leg: '#3d5a80' } },
			{ id: 'sk2', name: '丛林行者', colors: { head: '#d9a066', body: '#4d7c2f', arm: '#3d6b28', leg: '#2f4f1f' } },
		]},
		{ id: 'u2', type: 'microsoft', userName: 'Pineglow_Dev', uuid: 'f3c2e9d1-6a7b-4c5d-8e9f-0a1b2c3d4e5f', skinCount: 3, capeCount: 2, selected: false, skins: [
			{ id: 'sk3', name: '未来战士', colors: { head: '#c9d4e8', body: '#3b5b8c', arm: '#2c4a77', leg: '#1f3560' } },
			{ id: 'sk4', name: '烈焰法师', colors: { head: '#e8c39a', body: '#b53a2f', arm: '#8c2a22', leg: '#5c1a14' } },
			{ id: 'sk5', name: '潜行者', colors: { head: '#b8b0a8', body: '#4a4642', arm: '#383430', leg: '#262320' } },
		]},
		{ id: 'u3', type: 'authlib', userName: 'LittleSkin_User', uuid: '1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d', skinCount: 1, capeCount: 0, selected: false, skins: [
			{ id: 'sk6', name: '樱色少女', colors: { head: '#f7d7e0', body: '#f2a2c0', arm: '#e882a8', leg: '#c96b8f' } },
		]},
	],
	capes: [
		{ id: 'cp1', name: '松光披风', alias: '周年纪念披风', active: true },
		{ id: 'cp2', name: 'Mojang 经典披风', alias: '经典披风', active: false },
	],
	java: [
		{ path: 'C:\\Program Files\\Java\\jdk-21', versionMajor: 21, versionText: '21.0.5+11', vendor: 'Oracle', valid: true },
		{ path: 'C:\\Program Files\\Java\\jdk-17', versionMajor: 17, versionText: '17.0.13+11', vendor: 'Oracle', valid: true },
		{ path: 'C:\\Program Files\\Java\\jre8', versionMajor: 8, versionText: '1.8.0_421', vendor: 'Oracle', valid: true },
	],
	localVersions: ['1.21.5', '1.21.4', '1.21.1', '1.20.6', '1.20.1', '1.18.2', '1.16.5'],
	remoteVersions: (() => {
		const majors = [
			{ m: '1.21', minors: [6, 5, 4, 3, 2, 1] },
			{ m: '1.20', minors: [6, 5, 4, 3, 2, 1] },
			{ m: '1.19', minors: [4, 3, 2, 1] },
			{ m: '1.18', minors: [2, 1] },
			{ m: '1.17', minors: [1] },
			{ m: '1.16', minors: [5, 4, 3, 2, 1] },
			{ m: '1.15', minors: [2, 1] },
			{ m: '1.14', minors: [4, 3, 2, 1] },
			{ m: '1.13', minors: [2, 1] },
			{ m: '1.12', minors: [2, 1] },
			{ m: '1.11', minors: [2, 1] },
			{ m: '1.10', minors: [2, 1] },
			{ m: '1.9', minors: [4, 3, 2, 1] },
			{ m: '1.8', minors: [9, 8, 7] },
			{ m: '1.7', minors: [10, 9] },
			{ m: '1.6', minors: [4] },
			{ m: '1.5', minors: [2] },
			{ m: '1.4', minors: [7] },
			{ m: '1.3', minors: [2] },
			{ m: '1.2', minors: [5] },
			{ m: '1.1', minors: [] },
			{ m: '1.0', minors: [] },
		];
		const snaps = ['24w41a', '24w40a', '23w45a', '23w42a', '22w46a', '22w44a'];
		const list = [];
		const baseDate = new Date('2026-07-08');
		majors.forEach((g, gi) => {
			const minors = g.minors.length ? g.minors : [0];
			minors.forEach(mi => {
				const id = g.minors.length ? g.m + '.' + mi : g.m;
				const d = new Date(baseDate);
				d.setMonth(d.getMonth() - (gi * 12 + mi * 4));
				list.push({ id, type: 'release', releaseTime: d.toISOString().slice(0, 10) });
			});
		});
		snaps.forEach((s, si) => {
			const d = new Date(baseDate);
			d.setMonth(d.getMonth() - (2 + si * 4));
			list.push({ id: s, type: 'snapshot', releaseTime: d.toISOString().slice(0, 10) });
		});
		return list.sort((a, b) => b.id.localeCompare(a.id, undefined, { numeric: true }));
	})().map(v => Object.assign(v, {
		sizeText: (180 + Math.floor(Math.abs(Math.sin(v.id.length * 37.7)) * 260)) + ' MB',
	})),
	mods: {
		'a1': [
			{ fileName: 'sodium-0.6.8.jar', displayName: 'Sodium', version: '0.6.8', loader: 'Fabric', enabled: true, sizeText: '1.4 MB' },
			{ fileName: 'iris-1.8.1.jar', displayName: 'Iris Shaders', version: '1.8.1', loader: 'Fabric', enabled: true, sizeText: '2.8 MB' },
			{ fileName: 'modmenu-13.0.0.jar', displayName: 'Mod Menu', version: '13.0.0', loader: 'Fabric', enabled: true, sizeText: '680 KB' },
			{ fileName: 'continuity-3.0.0.jar', displayName: 'Continuity', version: '3.0.0', loader: 'Fabric', enabled: false, sizeText: '240 KB' },
		],
	},
	news: {
		items: [
			{ id: 'n1', title: 'Minecraft 官方新闻示例', category: 'Minecraft', date: '2026-08-30T00:00:00Z', text: 'mock 模式示例内容。', imageId: null, link: 'https://www.minecraft.net/article', linkOk: true },
		],
		meta: { source: 'launchercontent.mojang.com', state: 'live', validatedAt: '2026-09-05T12:00:00+00:00', fetchedAt: '2026-09-05T12:00:00+00:00', isStale: false, canRetry: false, errorKind: null, error: null },
	},
	downloads: [],
	diagnose: {
		ok: true,
		hasTail: true,
		rule: { summary: '游戏因内存不足或内存参数不合适而退出。', suggestions: ['适当增加游戏内存，同时不要超过电脑可用内存。', '大型整合包建议关闭其他高内存程序后重试。'] },
		mods: { enabledCount: 87, disabledCount: 3, onlineManagedCount: 80, issues: [{ severity: 'Warning', title: '发现重复安装的在线 Mod', description: 'Sodium 有 2 个启用文件。' }] },
		ai: '**崩溃原因**：日志中出现 OutOfMemoryError，说明游戏分配的内存不足。\n\n**修复步骤**：\n1. 在设置中把内存上调到 6GB 以上\n2. 关闭后台高占用程序\n3. 若仍崩溃，尝试移除 Sodium 的重复文件\n\n**可疑 Mod**：`Sodium` 存在重复安装。\n\n{"explain":"内存不足","fixes":["上调内存"],"suspectMods":["sodium-0.6.8.jar"]}',
			aiError: null,
		configured: true,
		suspectMods: ['sodium-0.6.8'],
	},
};

/* ================= 统一异步接口（live：fetch 真实 API；mock：内存数据） ================= */
SG.api = (() => {
	const BASE = '';
	const delay = 60;
	const isLive = SG.apiMode === 'live';

	const _mock = async value => { await new Promise(r => setTimeout(r, delay)); return value; };

	const _req = async (path, options) => {
		options = options || {};
		options.headers = Object.assign({}, options.headers || {}, { 'X-Pineglow-Token': SG.apiToken });
		const res = await fetch(BASE + path, options);
		if (!res.ok) {
			let msg = 'HTTP ' + res.status;
			let payload = null;
			try {
				const j = await res.json();
				payload = j;
				if (j && (j.error || j.message)) msg = j.error || j.message;
			} catch (e) { /* 非 JSON 错误体 */ }
			// MR06：把结构化错误体一起抛出——调用方（如二分定位）需要 needsRecovery/逐文件原因。
			const err = new Error(msg + ' (' + path + ')');
			err.status = res.status;
			err.payload = payload;
			throw err;
		}
		return res.json();
	};

	const _post = (path, body, options) => {
		const opts = Object.assign({}, options || {});
		opts.method = 'POST';
		opts.headers = Object.assign({}, opts.headers || {}, { 'Content-Type': 'application/json' });
		opts.body = JSON.stringify(body || {});
		return _req(path, opts);
	};

	const fmtBytes = n => {
		if (!n && n !== 0) return '—';
		if (n >= 1024 * 1024 * 1024) return (n / 1024 / 1024 / 1024).toFixed(1) + ' GB';
		if (n >= 1024 * 1024) return Math.round(n / 1024 / 1024) + ' MB';
		if (n >= 1024) return (n / 1024).toFixed(0) + ' KB';
		return n + ' B';
	};

	const fmtCount = n => {
		if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
		if (n >= 1000) return (n / 1000).toFixed(1) + 'K';
		return String(n);
	};

	const fmtLastPlayed = iso => {
		if (!iso) return '从未游玩';
		const t = new Date(iso);
		if (isNaN(t.getTime())) return '从未游玩';
		const diff = Date.now() - t.getTime();
		const day = 86400000;
		if (diff < day) return '今天游玩';
		if (diff < 2 * day) return '昨天游玩';
		if (diff < 7 * day) return Math.floor(diff / day) + ' 天前游玩';
		if (diff < 30 * day) return Math.floor(diff / (7 * day)) + ' 周前游玩';
		return Math.floor(diff / (30 * day)) + ' 个月前游玩';
	};

	const LOADER_GRADIENTS = {
		Fabric: 'linear-gradient(135deg,#4db6ac,#2e7d6b)',
		Forge: 'linear-gradient(135deg,#f9a8d4,#be185d)',
		NeoForge: 'linear-gradient(135deg,#dab2ff,#821ed2)',
		Vanilla: 'linear-gradient(135deg,#81f4af,#0faa4f)',
	};
	const defaultGradient = loader => LOADER_GRADIENTS[loader] || 'linear-gradient(135deg,#90c4ff,#1f5ff1)';

	const mapInstance = (i, selectedId) => ({
		id: i.id,
		name: i.name,
		loader: i.loaderType || 'Vanilla',
		version: i.minecraftVersion || '',
		icon: '⛏️',
		iconBg: defaultGradient(i.loaderType),
		lastPlayed: fmtLastPlayed(i.lastLaunchTime),
		lastPlayedTs: i.lastLaunchTime || 0,
		recent: false,
		modsCount: i.modsCount || 0,
		savesCount: i.savesCount || 0,
		gameDir: i.gameDirectory || '',
		totalPlaySeconds: i.totalPlaySeconds || 0,
		playCount: i.playCount || 0,
		selected: i.id === selectedId,
	});

	const mapAccount = (a, selectedId) => ({
		id: a.id,
		type: a.type || 'offline',
		userName: a.userName || 'Player',
		uuid: a.uuid || '',
		skinCount: a.skinCount || 0,
		capeCount: a.capeCount || 0,
		skinUrl: a.skinUrl || '',
		skinModel: a.skinModel || 'classic',
		selected: a.id === selectedId,
		skins: (a.skins && a.skins.length ? a.skins : (a.skinUrl ? [{ id: a.id + '-skin', name: a.userName + ' 的皮肤', url: a.skinUrl, model: a.skinModel || 'classic', colors: { head: '#f0b98a', body: '#e8714f', arm: '#e8714f', leg: '#3d5a80' } }] : [])).map(s => ({
			id: s.id || s.skinId,
			name: s.name || '皮肤',
			url: s.url || s.skinUrl || '',
			model: s.model || a.skinModel || 'classic',
			colors: s.colors || { head: '#f0b98a', body: '#e8714f', arm: '#e8714f', leg: '#3d5a80' },
		})),
	});

	const mapCapes = accounts => {
		const capes = [];
		(accounts || []).forEach(a => (a.capes || []).forEach(c => capes.push({
			id: c.id || c.capeId,
			name: c.alias || c.name || '披风',
			alias: c.alias || '',
			active: !!c.isActive,
		})));
		return capes;
	};

	const mapServer = s => ({
		id: s.id,
		name: s.name || '服务器',
		address: s.host || '',
		port: s.port || 25565,
		online: s.onlinePlayers >= 0 ? s.onlinePlayers : 0,
		max: s.maxPlayers || 0,
		online2: s.onlinePlayers >= 0,
		icon: '🏳️',
		iconBg: 'linear-gradient(135deg,#81f4af,#0a7141)',
		version: s.versionName || '未知',
		latency: s.latencyMs,
	});

	const mapWorld = (w, instances) => {
		const inst = instances.find(x => x.id === w.instanceId);
		return {
			instanceId: w.instanceId || '',
			folderName: w.folderName || w.displayName || w.name || '',
			name: w.displayName || w.folderName || '存档',
			instance: w.instanceName || (inst ? inst.name : '未知实例'),
			icon: '🌍',
			iconBg: 'linear-gradient(135deg,#b8fad2,#0f8642)',
			lastPlayed: fmtLastPlayed(w.lastPlayed),
			size: w.sizeText || '—',
		};
	};

	// Phase 2：构造受保护的本地资源图标端点。
	// 客户端只接受 source/category/projectId 三元组，绝不写入任何上游 URL。
	// 缺少 token 时返回空串（让前端直接走 missing 占位，避免无 token 仍发起请求）。
	const buildContentIconUrl = (source, category, projectId) => {
		const src = (source && /^(modrinth|curseforge)$/i.test(source)) ? source.toLowerCase() : 'modrinth';
		const cat = (category && /^(mod|modpack|shader|resourcepack)$/i.test(category)) ? category.toLowerCase() : 'mod';
		const pid = (projectId == null) ? '' : String(projectId);
		// projectId 必须是 [a-zA-Z0-9_-] 才合法；其它字符（防止被注入路径）一律视为无效
		if (!/^[a-zA-Z0-9_-]{1,80}$/.test(pid)) return '';
		const token = (window.SG && window.SG.apiToken) ? String(window.SG.apiToken) : '';
		if (!token) return '';
		return '/api/content/icon?source=' + encodeURIComponent(src)
			+ '&category=' + encodeURIComponent(cat)
			+ '&projectId=' + encodeURIComponent(pid)
			+ '&token=' + encodeURIComponent(token);
	};

	const mapContent = c => ({
		id: c.id || c.projectId,
		title: c.title || '未知项目',
		desc: c.desc || c.description || '',
		author: c.author || '',
		downloads: fmtCount(c.downloads),
		// icon 字段：仅用于 emoji 占位，不写入 <img src>。上游 iconUrl 不再透传给 DOM。
		icon: '📦',
		iconBg: 'linear-gradient(135deg,#90c4ff,#1f5ff1)',
		versions: c.versions || [],
		loaders: c.loaders || (c.loader ? [c.loader] : []),
		loader: c.loader || '',
		category: c.category || 'mod',
		source: (c.source && /^(modrinth|curseforge)$/i.test(c.source)) ? c.source.toLowerCase() : 'modrinth',
		rating: c.rating || 4.5,
		// Phase 2：iconUrl 始终是本地受保护端点；上游 URL 不可达前端
		iconUrl: (c.iconUrl && c.iconUrl.startsWith && c.iconUrl.startsWith('/api/content/icon'))
			? c.iconUrl + (c.iconUrl.indexOf('token=') >= 0 ? '' : ('&token=' + encodeURIComponent((window.SG && window.SG.apiToken) || '')))
			: buildContentIconUrl(c.source, c.category, c.id || c.projectId),
		websiteUrl: c.websiteUrl || '',
	});

	const mapVersion = (v, local) => ({
		id: v.id,
		type: v.type === 'snapshot' ? 'snapshot' : 'release',
		releaseTime: (v.releaseTime || '').slice(0, 10),
		sizeText: v.sizeText || '—',
		installed: v.installed !== undefined ? v.installed : (local || []).includes(v.id),
	});

	const mapTask = t => ({
		id: t.id,
		name: t.name || ('Minecraft ' + t.versionId + ' 客户端'),
		versionId: t.versionId,
		icon: '⛏️',
		iconBg: 'linear-gradient(135deg,#5eead4,#0f766e)',
		size: '—',
		progress: Math.round(Math.min(1, Math.max(0, Number(t.progress) || 0)) * 100),
		status: t.status || 'queued',
		statusLabel: t.status === 'completed' ? '已完成' : t.status === 'failed' ? '失败' : t.status === 'downloading' ? '下载中' : '排队中',
		message: t.message || '',
	});

	const live = {
		async getState() {
			const data = await _req('/api/state');
			return {
				config: {
					gameDirectory: data.config.gameDirectory || '',
					javaPath: data.config.javaPath || '',
					playerName: data.config.playerName || 'Player',
					selectedVersion: data.config.selectedVersion || '',
					maxMemoryMb: data.config.maxMemoryMb || 4096,
					useAutoMemory: !!data.config.useAutoMemory,
					windowWidth: data.config.windowWidth || 1280,
					windowHeight: data.config.windowHeight || 720,
					serverHost: data.config.serverHost || '',
					serverPort: String(data.config.serverPort || '25565'),
					accountMode: data.config.accountMode || 'offline',
					useMirror: !!data.config.useMirror,
					selectedInstanceId: data.config.selectedInstanceId || '',
					selectedAccountId: data.config.selectedAccountId || '',
					downloadSource: data.config.downloadSource || 'official',
					proxyEnabled: !!data.config.proxyEnabled,
					proxyHost: data.config.proxyHost || '',
					proxyPort: Number.isFinite(data.config.proxyPort) ? data.config.proxyPort : 0,
					proxyUsername: data.config.proxyUsername || '',
					proxyPasswordConfigured: !!data.config.proxyPasswordConfigured,
					newsFakeIpCompat: !!data.config.newsFakeIpCompat,
					minimizeOnLaunch: !!data.config.minimizeOnLaunch,
					autoCloseOnExit: !!data.config.autoCloseOnExit,
					modSource: 'Modrinth',
					curseForgeApiKeyConfigured: !!data.config.curseForgeApiKeyConfigured,
					aiDiagnosisEnabled: !!data.config.aiDiagnosisEnabled,
					selectedAiProviderId: data.config.selectedAiProviderId || '',
					aiProviders: (data.config.aiProviders || []).map(p => ({
						id: p.id, name: p.name, baseUrl: p.baseUrl, model: p.model, keyConfigured: !!p.keyConfigured,
					})),
				},
				memory: SG.mock.memoryRecommendation,
				instances: (data.instances || []).map(i => mapInstance(i, data.config.selectedInstanceId)),
				accounts: (data.accounts || []).map(a => mapAccount(a, data.config.selectedAccountId)),
				java: (data.java || []).map(j => ({ path: j.javaPath, versionMajor: j.versionMajor, versionText: j.versionText, vendor: j.vendor, valid: !!j.isValid })),
			};
		},
		async updateConfig(patch) {
			await _post('/api/config', patch);
			const s = await this.getState();
			return { ok: true, config: s.config };
		},
		async scanJava() {
			const data = await _req('/api/java');
			return (data.installations || []).map(j => ({ path: j.javaPath, versionMajor: j.versionMajor, versionText: j.versionText, vendor: j.vendor, valid: !!j.isValid }));
		},
		async getInstances() {
			const data = await _req('/api/instances');
			return (data.instances || []).map(i => mapInstance(i, data.selectedInstanceId));
		},
		async getWorlds() {
			const data = await _req('/api/worlds');
			const instances = await this.getInstances();
			return (data.worlds || []).map(w => mapWorld(w, instances));
		},
		async getServers() {
			const data = await _req('/api/servers');
			return (data.servers || []).map(mapServer);
		},
		async getNews(options) {
			const data = await _req('/api/news?refresh=' + (options && options.refresh ? '1' : '0'));
			if (data && data.error) throw new Error(data.error);
			// Phase 3A 契约：items 携带服务端注册的 imageId / linkOk / linkId；meta 提供稳定状态元数据
			return {
				items: (data.items || []).map(n => ({
					id: n.id || '',
					title: n.title || '',
					category: n.category || '',
					date: n.date || '',
					text: n.text || '',
					imageId: n.imageId || null,
					link: n.link || '',
					linkOk: n.linkOk === true,
					linkId: (n.linkId && typeof n.linkId === 'string') ? n.linkId : null,
				})),
				meta: {
					source: (data.meta && data.meta.source) || data.source || '',
					state: (data.meta && data.meta.state) || (data.isStale ? 'cached' : 'live'),
					validatedAt: (data.meta && data.meta.validatedAt) || data.fetchedAt || null,
					fetchedAt: (data.meta && data.meta.fetchedAt) || data.fetchedAt || null,
					isStale: (data.meta && data.meta.isStale) != null ? data.meta.isStale : !!data.isStale,
					canRetry: (data.meta && data.meta.canRetry) != null ? data.meta.canRetry : true,
					errorKind: (data.meta && data.meta.errorKind) || null,
					error: (data.meta && data.meta.error) || data.error || null,
				},
			};
		},
		// R2-03：客户端只传 linkId，服务端取回 URL 并二次白名单校验后经受控 opener 打开。
		// 不得在 body 中携带 raw URL / href / link 字段。
		async openNewsLink(linkId) {
			if (!linkId || typeof linkId !== 'string') {
				throw new Error('openNewsLink: linkId must be a non-empty string');
			}
			const data = await _req('/api/news-open', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ linkId }),
			});
			if (data && data.error) {
				const err = new Error('news-open: ' + data.error);
				err.code = data.error;
				throw err;
			}
			return data || { ok: true };
		},
		async getNewsImage(id) {
			// <img src> 无法携带自定义认证头（真实 WebView 实测 401）：
			// 图片统一经 fetch+blob 加载，令牌仅存在于请求头，不进 URL/日志
			const res = await fetch('/api/news-image?id=' + encodeURIComponent(id), {
				headers: { 'X-Pineglow-Token': SG.apiToken },
			});
			if (!res.ok) throw new Error('HTTP ' + res.status);
			return URL.createObjectURL(await res.blob());
		},
		async searchContent(query, category, options) {
			const opt = options || {};
			const cat = category && category !== 'all' ? category : 'mod';
			// 后端对空 query 返回空列表；首屏用分类默认热词兜底，保证进页即有内容（与 mock 一致）
			const DEFAULT_QUERY = { mod: 'sodium', modpack: 'fabulously', shader: 'shaders', resourcepack: 'faithful' };
			const q = (query && query.trim()) ? query.trim() : (DEFAULT_QUERY[cat] || 'sodium');
			// 只使用调用方显式传入的 source；api.js 不读取其他脚本的私有变量。
			// 调用方必须负责从 UI 状态传入 source（launcher.js → searchContentOptions）。
			const source = (opt.source && /^(modrinth|curseforge)$/i.test(opt.source))
				? opt.source.toLowerCase()
				: 'modrinth';
			const params = new URLSearchParams({
				q,
				category: cat,
				source,
				gameVersion: opt.gameVersion || '',
				loader: opt.loader || '',
			});
			const data = await _req('/api/content/search?' + params.toString());
			if (data && data.error) throw new Error(data.error);
			return (data.results || []).map(mapContent);
		},
		async getAccounts() {
			const data = await _req('/api/accounts');
			return { accounts: (data.accounts || []).map(a => mapAccount(a, data.selectedAccountId)), capes: mapCapes(data.accounts || []) };
		},
		async getJava() {
			const data = await _req('/api/java');
			return (data.installations || []).map(j => ({ path: j.javaPath, versionMajor: j.versionMajor, versionText: j.versionText, vendor: j.vendor, valid: !!j.isValid }));
		},
		async getVersions() {
			const data = await _req('/api/versions');
			let remote = [];
			try {
				const r = await _req('/api/versions/remote');
				remote = (r.versions || []).map(v => mapVersion(v, data.local));
			} catch (e) { /* 远程列表失败，仅本地 */ }
			return { local: data.local || [], remote };
		},
		async getMods(instanceId) {
			const data = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/mods');
			return (data.mods || []).map(m => ({
				fileName: m.fileName,
				displayName: m.displayName || m.fileName,
				version: m.version || '',
				loader: m.loaderHint || '',
				enabled: !!m.isEnabled,
				sizeText: m.sizeText || fmtBytes(m.sizeBytes),
			}));
		},
		async toggleMod(instanceId, fileName) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/mods/' + encodeURIComponent(fileName) + '/toggle', {});
			return { ok: !!res.ok, fileName: res.fileName, enabled: !!res.isEnabled };
		},
		async deleteMod(instanceId, fileName) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/mods/' + encodeURIComponent(fileName) + '/delete', {});
			return { ok: !!res.ok, operationId: res.operationId || '', recoverable: !!res.recoverable };
		},
		// MR04：恢复区 —— 列出可还原的移除操作
		async listRecoveries(instanceId) {
			const data = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/recovery');
			return {
				retentionDays: data.retentionDays || 30,
				recoveries: (data.recoveries || []).map(r => ({
					operationId: r.operationId,
					reason: r.reason || '',
					status: r.status || '',
					createdAt: r.createdAt || '',
					expiresAt: r.expiresAt || '',
					totalBytes: r.totalBytes || 0,
					files: (r.files || []).map(f => ({
						relativePath: f.relativePath,
						state: f.state,
						sizeBytes: f.sizeBytes,
						error: f.error || '',
					})),
				})),
			};
		},
		// MR05：overwrite=true 用于"撤销本次更新"（把旧版本覆盖回去）；默认 false 不覆盖。
		async restoreRecovery(instanceId, operationId, overwrite) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/recovery/' + encodeURIComponent(operationId) + '/restore', { overwrite: !!overwrite });
			return {
				ok: !!(res && res.ok),
				status: (res && res.status) || '',
				overwrite: !!(res && res.overwrite),
				conflicts: (res && res.conflicts) || 0,
				files: (res && res.files) || [],
			};
		},
		async installContent(data) {
			const res = await _post('/api/content/install', {
				category: data.category || 'mod',
				projectId: data.projectId,
				instanceId: data.instanceId || '',
				source: data.source || 'modrinth',
			});
			return { ok: !!res.ok, id: res.id || '', status: res.status || 'queued' };
		},
		async getContentTasks() {
			const data = await _req('/api/content/tasks');
			return (data.tasks || []).map(t => ({
				id: t.id,
				category: t.category || 'mod',
				title: t.title || '',
				status: t.status || 'queued',
				progress: t.progress || 0,
				message: t.message || '',
				resultInstanceId: t.resultInstanceId || '',
			}));
		},
		async pingServer(serverId) {
			const data = await _post('/api/servers/' + encodeURIComponent(serverId) + '/ping');
			return mapServer(data.server);
		},
		async addServer(data) {
			const res = await _post('/api/servers', { name: data.name, host: data.host, port: String(data.port || 25565) });
			return mapServer(res.server);
		},
		async createInstance(data) {
			const res = await _post('/api/instances', { name: data.name, version: data.version || '' });
			return mapInstance(res.instance, res.instance.id);
		},
		async setMirror(enabled) {
			await _post('/api/config', { useMirror: enabled });
			return { ok: true };
		},
		async getSources() {
			const data = await _req('/api/sources');
			return {
				selected: data.selected || 'official',
				sources: (data.sources || []).map(s => ({
					key: s.key,
					name: s.name || s.key,
					baseUrl: s.baseUrl || '',
					latencyMs: s.latencyMs,
					available: !!s.available,
				})),
			};
		},
		async getStats() {
			return await _req('/api/stats');
		},
		async selectSource(key) {
			const res = await _post('/api/sources/select', { key });
			return { ok: !!res.ok, selected: res.selected || '', useMirror: !!res.useMirror };
		},
		async getRemoteVersions(options) {
			const data = await _req('/api/versions');
			try {
				const r = await _req('/api/versions/remote');
				return (r.versions || []).map(v => mapVersion(v, data.local));
			} catch (e) {
				const localVersions = (data.local || []).map(id => ({ id, type: 'release', releaseTime: '', sizeText: '已安装', installed: true }));
				if (options && options.throwOnRemoteError) {
					const error = new Error((e && e.message) || String(e));
					error.localVersions = localVersions;
					throw error;
				}
				return localVersions;
			}
		},
		async downloadVersion(versionId) {
			const res = await _post('/api/versions/download', { versionId });
			return {
				id: res.id,
				name: 'Minecraft ' + res.versionId + ' 客户端',
				versionId: res.versionId,
				icon: '⛏️',
				iconBg: 'linear-gradient(135deg,#5eead4,#0f766e)',
				size: '—',
				progress: 0,
				status: 'queued',
				statusLabel: '排队中',
			};
		},
		async getDownloads() {
			const data = await _req('/api/downloads');
			return (data.tasks || []).map(mapTask);
		},
		async selectAccount(accountId) {
			await _post('/api/accounts/select', { accountId });
			const a = await this.getAccounts();
			return { ok: true, accounts: a.accounts };
		},
		async addOfflineAccount(userName) {
			const res = await _post('/api/accounts/offline', { userName });
			const acc = mapAccount(res.account, res.account.id);
			const a = await this.getAccounts();
			return { ok: true, account: acc, accounts: a.accounts };
		},
		async microsoftDeviceStart() {
			const res = await _post('/api/accounts/microsoft/device', {});
			if (!res || !res.ok) throw new Error((res && res.message) || '无法创建微软登录码');
			return res;
		},
		async microsoftDevicePoll() {
			const res = await _post('/api/accounts/microsoft/poll', {});
			if (!res) throw new Error('微软登录轮询失败');
			return res;
		},
		async deleteAccount(accountId) {
			await _req('/api/accounts/' + encodeURIComponent(accountId), { method: 'DELETE' });
			const a = await this.getAccounts();
			return { ok: true, selectedAccountId: a.accounts.find(x => x.selected) && a.accounts.find(x => x.selected).id, accounts: a.accounts };
		},		async microsoftLogin() {
			const res = await _post('/api/accounts/microsoft', {});
			if (!res || !res.ok) throw new Error((res && res.message) || '微软登录失败');
			const acc = mapAccount(res.account, res.account.id);
			const a = await this.getAccounts();
			return { ok: true, account: acc, accounts: a.accounts };
		},
		async refreshSkin(accountId) {
			const res = await _post('/api/accounts/refresh-skin', { accountId });
			if (!res || !res.ok) throw new Error((res && res.message) || 'refresh skin failed');
			return { ok: true, account: mapAccount(res.account, res.account && res.account.id) };
		},
		async authlibLogin(serverUrl, userName, password) {
			const res = await _post('/api/accounts/authlib', { serverUrl, userName, password });
			if (!res || !res.ok) throw new Error((res && res.message) || '外置登录失败');
			const acc = mapAccount(res.account, res.account.id);
			const a = await this.getAccounts();
			return { ok: true, account: acc, accounts: a.accounts };
		},
		async openLogDirectory() {
			const res = await _post('/api/logs/open', {});
			return { ok: !!res.ok, path: res.path || '' };
		},
		async uploadSkin(accountId, pngBase64, slim) {
			const res = await _post('/api/accounts/skin', { accountId: accountId || '', pngBase64, slim: !!slim });
			return { ok: !!res.ok, account: mapAccount(res.account, res.account && res.account.id) };
		},
		async createSaveBackup(instanceId, folderName) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/saves/' + encodeURIComponent(folderName) + '/backup', {});
			return { ok: !!res.ok, path: res.path || '', folderName: res.folderName || '' };
		},
		async listSaveBackups(instanceId, folderName) {
			const data = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/saves/' + encodeURIComponent(folderName) + '/backups');
			return (data.backups || []).map(b => ({
				fileName: b.fileName,
				groupName: b.groupName || '',
				sizeText: b.sizeText || '',
				createdAt: b.createdAt || '',
			}));
		},
		async restoreSaveBackup(instanceId, folderName, backupName) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/saves/' + encodeURIComponent(folderName) + '/backups/' + encodeURIComponent(backupName) + '/restore', {});
			return { ok: !!res.ok, path: res.path || '' };
		},
		async deleteSave(instanceId, folderName) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/saves/' + encodeURIComponent(folderName) + '/delete', {});
			return { ok: !!res.ok };
		},
		async openSaveDirectory(instanceId, folderName) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/saves/' + encodeURIComponent(folderName) + '/open', {});
			return { ok: !!res.ok, path: res.path || '' };
		},
		async checkUpdate() {
			return await _req('/api/update/check');
		},
		async startUpdateDownload() {
			return await _post('/api/update/download', {});
		},
		async getUpdateStatus() {
			const data = await _req('/api/update');
			// MR20：信任链声明与"上次替换结果"必须透给界面 ——
			// 界面不能因为"脚本已启动"就显示更新完成，只有 applied 才算替换成功。
			return Object.assign({ trustNote: '', lastApplyResult: null }, data || {});
		},
		async applyUpdate() {
			return await _post('/api/update/apply', {});
		},
		async launchInstance(instanceId) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/launch', {});
			return { ok: !!res.ok, launched: !!res.ok, pid: res.pid || 0, message: res.error || '' };
		},
		async diagnoseInstance(instanceId, options) {
			const opts = options || {};
			const reqOpts = {};
			if (opts.signal) reqOpts.signal = opts.signal;
			// Phase 3B LANG 透传：locale 只允许 'zh' / 'en'，缺失或非法一律回落到安全默认 'zh'。
			// 优先采纳 launcher 显式传入的 opts.locale；未提供时回退读 SG.locale；
			// 任何不可信任字符串绝不直接透传到后端，避免污染 AiDiagnoseService 的指令语言。
			const candidate = (typeof opts.locale === 'string' && opts.locale)
				? opts.locale
				: (typeof SG !== 'undefined' && SG ? SG.locale : undefined);
			const safeLocale = (candidate === 'zh' || candidate === 'en') ? candidate : 'zh';
			return await _post('/api/instances/' + encodeURIComponent(instanceId) + '/diagnose', {
				// Phase 3B v4 步骤 3 P0 修复：把客户端生成的 operationId 一并写入 diagnose body，
				// 否则后端按 v3 报告对缺 ID 请求返回 400。
				operationId: typeof opts.operationId === 'string' ? opts.operationId : '',
				confirmAiSend: !!opts.confirmAiSend,
				localOnly: !!opts.localOnly,
				locale: safeLocale,
			}, reqOpts);
		},
		async cancelDiagnoseInstance(instanceId, operationId, options) {
			const opts = options || {};
			const reqOpts = {};
			// Phase 3B v4 步骤 3 P1-2 修复：cancel 请求**绝不**复用已 abort 的 diagnose signal。
			// 故意不把 opts.signal 透传到 fetch——唯一在作用域里的 signal 就是已被 abort 的 diagnose
			// signal，一旦透传，cancel 自己会被立即 abort 而永远到不了后端，等于把"已调用 fetch"
			// 冒充"服务端已收到"，违反 v4 prompt §3 第 6 条（不得用无认证方案绕过）。
			// 如果将来需要给 cancel 加独立的可取消信号，由调用方在 opts 中提供**新的** signal，
			// api 层仅在不破坏上述契约的前提下透传；当前契约要求 cancel 始终真正抵达服务端。
			return await _post('/api/instances/' + encodeURIComponent(instanceId) + '/diagnose/cancel', {
				operationId: typeof operationId === 'string' ? operationId : '',
			}, reqOpts);
		},
		async bisectStart(instanceId) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/bisect/start', {});
			return this._normalizeBisect(res);
		},
		async bisectTest(instanceId, crashed, step) {
			const body = { crashed: !!crashed };
			// MR06：带上所回答的轮次，服务端据此拒绝"过期轮次"的重复提交。
			if (typeof step === 'number') body.step = step;
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/bisect/test', body);
			return this._normalizeBisect(res);
		},
		async bisectGiveUp(instanceId) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/bisect/giveup', {});
			return this._normalizeBisect(res);
		},
		// MR07：查询是否有未完成的二分定位会话（重启后）
		async bisectPending(instanceId) {
			const data = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/bisect');
			return Object.assign({ pending: !!(data && data.pending) }, data || {});
		},
		// MR07：action = 'restore' | 'continue'
		async bisectResume(instanceId, action) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/bisect/resume', { action: action || 'restore' });
			return this._normalizeBisect(res);
		},
		// MR08：诊断历史（只读本地）
		async diagnoseHistory(instanceId) {
			const data = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/diagnose/history');
			return {
				limit: (data && data.limit) || 50,
				retentionDays: (data && data.retentionDays) || 30,
				overLimitCount: (data && data.overLimitCount) || 0,
				olderThanRetentionCount: (data && data.olderThanRetentionCount) || 0,
				notice: (data && data.notice) || '',
				reports: (data && data.reports) || [],
			};
		},
		// MR08：导出报告。返回 { ok, status, text }，不抛错，便于界面直接展示失败原因。
		async diagnoseExport(instanceId, reportId, format, includeEvidence) {
			const qs = '?reportId=' + encodeURIComponent(reportId)
				+ '&format=' + encodeURIComponent(format || 'md')
				+ '&includeEvidence=' + (includeEvidence ? '1' : '0');
			const res = await fetch(BASE + '/api/instances/' + encodeURIComponent(instanceId) + '/diagnose/export' + qs, {
				headers: { 'X-Pineglow-Token': SG.apiToken },
			});
			const text = await res.text();
			return { ok: res.ok, status: res.status, text };
		},
		_normalizeBisect(data) {
			return {
				enabledAtStart: (data && data.enabledAtStart) || [],
				candidates: (data && data.candidates) || [],
				step: (data && data.step) || 0,
				waitingForReport: !!(data && data.waitingForReport),
				disabledHalf: (data && data.disabledHalf) || [],
				culprit: (data && data.culprit) || null,
				finished: !!(data && data.finished),
				finishReason: (data && data.finishReason) || null,
				// MR06：文件操作失败时的恢复上下文，UI 必须能区分"定位完成/恢复完成/恢复未完成"。
				needsRecovery: !!(data && data.needsRecovery),
				recoveryError: (data && data.recoveryError) || null,
				lastResults: (data && data.lastResults) || [],
				version: (data && data.version) || 0,
				// MR07：依赖有效性与会话标识
				sessionId: (data && data.sessionId) || '',
				roundInvalid: !!(data && data.roundInvalid),
				roundValidity: (data && data.roundValidity) || 'ok',
				roundIssues: (data && data.roundIssues) || [],
			};
		},
		async deleteInstance(instanceId) {
			await _post('/api/instances/' + encodeURIComponent(instanceId) + '/delete', {});
			return { ok: true };
		},
		// MR03：把已存在的游戏目录挂回实例列表；只写配置，不触碰文件。
		async registerInstance(gameDirectory, name) {
			const res = await _post('/api/instances/register', { gameDirectory: gameDirectory || '', name: name || '' });
			if (!res || !res.ok) throw new Error((res && res.message) || (res && res.error) || '添加失败');
			return { ok: true, instance: res.instance || null };
		},
		async importInstance(fileName, fileBase64, operationId) {
			try {
				const res = await _post('/api/instances/import', { fileName, fileBase64, operationId: operationId || '' });
				if (!res || !res.ok) {
					const err = new Error((res && res.message) || (res && res.error) || '导入失败');
					err.cancelled = !!(res && res.cancelled);
					throw err;
				}
				return { ok: true, instanceId: res.instanceId || '', name: res.name || '' };
			} catch (err) {
				// _post 在非 2xx 时会自己抛错，服务端 payload 挂在 err.payload 上。
				// MR11：取消（409 + cancelled）必须能被界面识别，不能混成"导入失败"。
				if (err && err.payload && err.payload.cancelled) {
					err.cancelled = true;
					err.message = err.payload.error || err.message;
				}
				throw err;
			}
		},
		// MR11：取消进行中的导入。必须总是调用（服务端也靠它登记"取消先到"的墓碑）。
		async cancelImport(operationId) {
			const res = await _post('/api/instances/import/cancel', { operationId: operationId || '' });
			return {
				ok: !!(res && res.ok),
				cancelled: !!(res && res.cancelled),
				pendingCancel: !!(res && res.pendingCancel),
				message: (res && res.message) || '',
			};
		},
		async prelaunchCheck(instanceId) {
			const data = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/prelaunch-check');
			return {
				ok: !!data.ok,
				errors: data.errors || [],
				warnings: data.warnings || [],
				maxMemoryMb: data.maxMemoryMb || 0,
				gameDir: data.gameDir || '',
			};
		},
		async deleteServer(serverId) {
			await _post('/api/servers/' + encodeURIComponent(serverId) + '/delete', {});
			return { ok: true };
		},
	};

	const mock = {
		async _mock(value) { await new Promise(r => setTimeout(r, delay)); return value; },
		async getState() {
			return this._mock({ config: SG.mock.settings, memory: SG.mock.memoryRecommendation, instances: SG.mock.instances, accounts: SG.mock.accounts, java: SG.mock.java });
		},
		async updateConfig(patch) {
			Object.assign(SG.mock.settings, patch);
			return this._mock({ ok: true, config: SG.mock.settings });
		},
		async scanJava() { return this._mock(SG.mock.java); },
		async getInstances() { return this._mock(SG.mock.instances); },
		async getWorlds() { return this._mock(SG.mock.worlds); },
		async getServers() { return this._mock(SG.mock.servers); },
		async getNews() { return this._mock(SG.mock.news); },
		async getNewsImage(id) { return 'blob:mock-' + id; },		async searchContent(query, category) {
			let list = SG.mock.content;
			if (category && category !== 'all') list = list.filter(c => c.category === category);
			if (query) { const q = query.toLowerCase(); list = list.filter(c => c.title.toLowerCase().includes(q) || c.desc.toLowerCase().includes(q) || c.author.toLowerCase().includes(q)); }
			return this._mock(list);
		},
		async getAccounts() { return this._mock({ accounts: SG.mock.accounts, capes: SG.mock.capes }); },
		async getJava() { return this._mock(SG.mock.java); },
		async getVersions() { return this._mock({ local: SG.mock.localVersions, remote: SG.mock.remoteVersions }); },
		async getMods(instanceId) { return this._mock(SG.mock.mods[instanceId] || []); },
		async toggleMod(instanceId, fileName) {
			const list = SG.mock.mods[instanceId] || [];
			const mod = list.find(m => m.fileName === fileName);
			if (mod) mod.enabled = !mod.enabled;
			return this._mock({ ok: true, fileName, enabled: !!(mod && mod.enabled) });
		},
		async deleteMod(instanceId, fileName) {
			const list = SG.mock.mods[instanceId] || [];
			const idx = list.findIndex(m => m.fileName === fileName);
			if (idx >= 0) list.splice(idx, 1);
			// MR04：mock 侧同样留一条可恢复记录，行为与 live 一致。
			SG.mock.recoveries = SG.mock.recoveries || {};
			const ops = SG.mock.recoveries[instanceId] = SG.mock.recoveries[instanceId] || [];
			const operationId = 'mock-' + Date.now();
			ops.unshift({
				operationId,
				reason: 'mod-delete',
				status: 'quarantined',
				createdAt: new Date().toISOString(),
				expiresAt: '',
				totalBytes: 0,
				files: [{ relativePath: 'mods/' + fileName, state: 'quarantined', sizeBytes: 0, error: '' }],
			});
			return this._mock({ ok: true, operationId, recoverable: true });
		},
		async listRecoveries(instanceId) {
			SG.mock.recoveries = SG.mock.recoveries || {};
			return this._mock({ retentionDays: 30, recoveries: SG.mock.recoveries[instanceId] || [] });
		},
		async restoreRecovery(instanceId, operationId, overwrite) {
			SG.mock.recoveries = SG.mock.recoveries || {};
			const ops = SG.mock.recoveries[instanceId] || [];
			const op = ops.find(x => x.operationId === operationId) || null;
			// MR05：mock 侧同样区分"默认不覆盖"与"撤销更新时显式覆盖"。
			if (op && op.status === 'restored' && !overwrite) {
				op.files = op.files.map(f => Object.assign({}, f, { state: 'conflict' }));
				return this._mock({ ok: false, status: 'conflict', overwrite: false, conflicts: op.files.length, files: op.files });
			}
			if (op) {
				op.status = 'restored';
				op.files = op.files.map(f => Object.assign({}, f, { state: 'restored' }));
			}
			return this._mock({ ok: true, status: 'restored', overwrite: !!overwrite, conflicts: 0, files: op ? op.files : [] });
		},
		async installContent(data) {
			const task = {
				id: 'ct' + Date.now(),
				category: data.category || 'mod',
				title: '',
				status: 'completed',
				progress: 1,
				message: '安装完成',
				resultInstanceId: '',
			};
			SG.mock.contentTasks = SG.mock.contentTasks || [];
			SG.mock.contentTasks.push(task);
			return this._mock({ ok: true, id: task.id, status: 'queued' });
		},
		async getContentTasks() { return this._mock((SG.mock.contentTasks || []).map(t => ({ ...t }))); },
		async pingServer(serverId) {
			const s = SG.mock.servers.find(x => x.id === serverId);
			if (!s) return this._mock(null);
			s.latency = s.online2 ? Math.floor(15 + Math.random() * 50) : null;
			return this._mock(s);
		},
		async addServer(data) {
			const s = { id: 's' + (SG.mock.servers.length + 1), name: data.name, address: data.host, port: data.port || 25565, online: 0, max: 0, online2: false, icon: '🌐', iconBg: 'linear-gradient(135deg,#90c4ff,#1f5ff1)', version: '未知', latency: null };
			SG.mock.servers.push(s);
			return this._mock(s);
		},
		async createInstance(data) {
			const inst = {
				id: 'i' + (SG.mock.instances.length + 1),
				name: data.name || '新实例',
				version: data.version || '1.21.4',
				loader: 'Vanilla',
				icon: '⛏',
				iconBg: 'linear-gradient(135deg,#5eead4,#0f766e)',
				lastPlayed: '从未游玩',
				modsCount: 0,
			};
			SG.mock.instances.push(inst);
			return this._mock(inst);
		},
		async setMirror(enabled) {
			SG.mock.settings.useMirror = enabled;
			return this._mock({ ok: true });
		},
		async getSources() {
			const lat = { official: 35, bmclapi: 42, mcbbs: 51 };
			return this._mock({
				selected: SG.mock.settings.downloadSource || 'official',
				sources: [
					{ key: 'official', name: '官方源', baseUrl: 'https://piston-meta.mojang.com', latencyMs: lat.official, available: true },
					{ key: 'bmclapi', name: 'BMCLAPI', baseUrl: 'https://bmclapi2.bangbang93.com', latencyMs: lat.bmclapi, available: true },
					{ key: 'mcbbs', name: 'MCBBS 镜像', baseUrl: 'https://download.mcbbs.net', latencyMs: lat.mcbbs, available: true },
				],
			});
		},
		async getStats() {
			return this._mock({ totalPlaySeconds: 0, totalPlayCount: 0, totalInstances: SG.mock.instances.length, perInstance: [] });
		},
		async selectSource(key) {
			SG.mock.settings.downloadSource = key;
			SG.mock.settings.useMirror = key !== 'official';
			return this._mock({ ok: true, selected: key, useMirror: key !== 'official' });
		},
		async getRemoteVersions() {
			const installed = SG.mock.localVersions || [];
			const list = (SG.mock.remoteVersions || []).map(v => ({
				...v,
				installed: installed.includes(v.id),
				typeLabel: v.type === 'snapshot' ? '快照' : '正式版',
			}));
			return this._mock(list);
		},
		async downloadVersion(versionId) {
			const v = (SG.mock.remoteVersions || []).find(x => x.id === versionId);
			const task = {
				id: 'dlv' + Date.now(),
				name: 'Minecraft ' + versionId + ' 客户端',
				versionId,
				icon: '⛏️',
				iconBg: 'linear-gradient(135deg,#5eead4,#0f766e)',
				size: (v && v.sizeText) || '300 MB',
				progress: 0,
				status: 'queued',
				statusLabel: '排队中',
			};
			SG.mock.downloads.push(task);
			return this._mock(task);
		},
		async getDownloads() { return this._mock(SG.mock.downloads); },
		async selectAccount(accountId) {
			SG.mock.accounts.forEach(a => a.selected = (a.id === accountId));
			return this._mock({ ok: true, accounts: SG.mock.accounts });
		},
		async addOfflineAccount(userName) {
			const acc = { id: 'off-' + Date.now(), userName, type: 'offline', selected: true, avatarBg: 'linear-gradient(135deg,#7c8a3d,#4d5b1f)' };
			SG.mock.accounts.forEach(a => a.selected = false);
			SG.mock.accounts.push(acc);
			SG.mock.settings.playerName = userName;
			SG.mock.settings.accountMode = 'offline';
			return this._mock({ ok: true, account: acc, accounts: SG.mock.accounts });
		},
		async microsoftDeviceStart() {
			return this._mock({ ok: true, userCode: 'ABCD1234', verificationUri: 'https://microsoft.com/link', verificationUriComplete: '', expiresIn: 900, interval: 5 });
		},
		async microsoftDevicePoll() {
			const acc = { id: 'ms-' + Date.now(), userName: 'Steve_Adv', type: 'microsoft', selected: true, avatarBg: 'linear-gradient(135deg,#1e7a54,#0f3d2a)' };
			SG.mock.accounts.forEach(a => a.selected = false);
			SG.mock.accounts.push(acc);
			SG.mock.settings.accountMode = 'microsoft';
			return this._mock({ ok: true, status: 'completed', account: acc, accounts: SG.mock.accounts });
		},
		async microsoftLogin() {
			const acc = { id: 'ms-' + Date.now(), userName: 'Steve_Adv', type: 'microsoft', selected: true, avatarBg: 'linear-gradient(135deg,#1e7a54,#0f3d2a)' };
			SG.mock.accounts.forEach(a => a.selected = false);
			SG.mock.accounts.push(acc);
			SG.mock.settings.accountMode = 'microsoft';
			return this._mock({ ok: true, account: acc, accounts: SG.mock.accounts });
		},
		async refreshSkin(accountId) {
			const acc = SG.mock.accounts.find(a => a.id === accountId) || SG.mock.accounts[0];
			if (acc) { acc.skinCount = (acc.skinCount || 0) + 1; acc.type = 'authlib'; }
			return this._mock({ ok: true, account: acc });
		},
				async authlibLogin(serverUrl, userName, password) {
			const acc = { id: 'al-' + Date.now(), userName, type: 'authlib', selected: true, avatarBg: 'linear-gradient(135deg,#6d5ae0,#3b2d8f)', authServerUrl: serverUrl };
			SG.mock.accounts.forEach(a => a.selected = false);
			SG.mock.accounts.push(acc);
			SG.mock.settings.accountMode = 'authlib';
			return this._mock({ ok: true, account: acc, accounts: SG.mock.accounts });
		},
		async openLogDirectory() { return this._mock({ ok: true, path: '[logs 目录]' }); },
		async uploadSkin(accountId, pngBase64, slim) {
			const acc = SG.mock.accounts.find(a => a.id === accountId);
			if (acc) acc.skinCount = (acc.skinCount || 0) + 1;
			return this._mock({ ok: true, account: acc || SG.mock.accounts[0] });
		},
		async deleteAccount(accountId) {
			SG.mock.accounts = SG.mock.accounts.filter(a => a.id !== accountId);
			return this._mock({ ok: true, selectedAccountId: SG.mock.accounts[0] && SG.mock.accounts[0].id, accounts: SG.mock.accounts });
		},
		async createSaveBackup(instanceId, folderName) {
			return this._mock({ ok: true, path: '[备份] ' + folderName + '.zip', folderName });
		},
		async listSaveBackups(instanceId, folderName) {
			return this._mock([{ fileName: folderName + '-20260810-120000.zip', groupName: '存档', sizeText: '1.2 MB', createdAt: new Date().toISOString() }]);
		},
		async restoreSaveBackup(instanceId, folderName, backupName) {
			return this._mock({ ok: true, path: '[恢复] ' + folderName });
		},
		async deleteSave(instanceId, folderName) {
			const list = SG.mock.worlds;
			const idx = list.findIndex(w => w.name === folderName);
			if (idx >= 0) list.splice(idx, 1);
			return this._mock({ ok: true });
		},
		async openSaveDirectory(instanceId, folderName) {
			return this._mock({ ok: true, path: '[存档目录] ' + folderName });
		},
		async checkUpdate() { return this._mock({ enabled: true, currentVersion: '1.0.0', updateAvailable: false, latestVersion: '1.0.0', notes: '', mandatory: false }); },
		async startUpdateDownload() { return this._mock({ ok: true, state: 'downloading' }); },
		async getUpdateStatus() { return this._mock({ state: 'completed', progress: 1, version: '1.0.0', path: '', error: '' }); },
		async applyUpdate() { return this._mock({ ok: true }); },
		async launchInstance(instanceId) {
			const inst = SG.mock.instances.find(x => x.id === instanceId);
			return this._mock({ ok: true, launched: true, message: inst ? '已发出启动指令' : '' });
		},
		async diagnoseInstance(instanceId, options) {
			const opts = options || {};
			if (opts.signal && opts.signal.aborted) {
				throw new DOMException('aborted', 'AbortError');
			}
			// mock 端 fail-closed：无 confirmAiSend 也不要求 localOnly 时，按 confirmation_required 返回。
			if (!opts.confirmAiSend && !opts.localOnly) {
				return this._mock({
					ok: true,
					hasTail: true,
					snapshot: null,
					rule: SG.mock.diagnose.rule,
					mods: SG.mock.diagnose.mods,
					aiResult: {
						ok: false,
						status: 'failed',
						confidence: 0,
						summary: '请在 UI 中确认后再发送 AI 诊断',
						causes: [],
						actions: [],
						warnings: ['请在 UI 中确认后再发送 AI 诊断'],
						missingInformation: ['confirmAiSend'],
						errorCode: 'confirmation_required',
					},
					aiError: 'confirmation_required: 请在 UI 中确认后再发送 AI 诊断',
					configured: true,
					suspectMods: [],
				});
			}
			if (opts.localOnly) {
				return this._mock({
					ok: true,
					hasTail: true,
					snapshot: null,
					rule: SG.mock.diagnose.rule,
					mods: SG.mock.diagnose.mods,
					aiResult: {
						ok: true,
						status: 'insufficient_information',
						confidence: 1.0,
						summary: SG.mock.diagnose.rule.summary || '本地分析',
						causes: [{ title: '本地规则', description: SG.mock.diagnose.rule.summary || '', kind: 'fact', confidence: 1.0, evidence: SG.mock.diagnose.rule.suggestions || [] }],
						actions: [],
						warnings: ['未发送至 AI；显示本地规则与 Mod 健康分析'],
						missingInformation: ['ai_provider', 'user_confirmation'],
						errorCode: null,
					},
					aiError: null,
					configured: true,
					suspectMods: [],
				});
			}
			// 模拟网络延迟以允许 abort
			return await new Promise((resolve, reject) => {
				const timer = setTimeout(() => resolve(this._mockSync(SG.mock.diagnose)), 100);
				if (opts.signal) {
					opts.signal.addEventListener('abort', () => {
						clearTimeout(timer);
						reject(new DOMException('aborted', 'AbortError'));
					}, { once: true });
				}
			});
		},
		// Phase 3B v4 步骤 3 P0 修复：mock 端提供 cancelDiagnoseInstance，保持 live/mock 边界。
		async cancelDiagnoseInstance(instanceId, operationId, options) {
			return this._mock({ ok: true, cancelled: true, instanceId: instanceId || '', operationId: operationId || '' });
		},
		_mockSync(value) { return value; },
		async bisectStart(instanceId) {
			const list = (SG.mock.mods[instanceId] || []).filter(m => m.enabled).map(m => m.fileName);
			this._bisectState = { instanceId, enabledAtStart: list.slice(), candidates: list.slice(), step: 0, waitingForReport: false, disabledHalf: [], culprit: null, finished: false, finishReason: null };
			this._bisectApplyStep();
			return this._mock(this._bisectPayload());
		},
		async bisectTest(instanceId, crashed) {
			const s = this._bisectState;
			if (!s || s.instanceId !== instanceId) return this._mock({ error: 'no session' });
			s.candidates = crashed ? s.candidates.filter(n => !s.disabledHalf.includes(n)) : s.disabledHalf.slice();
			s.waitingForReport = false;
			s.disabledHalf = [];
			if (s.candidates.length === 1) { s.finished = true; s.culprit = s.candidates[0]; }
			else this._bisectApplyStep();
			if (s.finished) this._bisectRestore();
			return this._mock(this._bisectPayload());
		},
		async bisectGiveUp(instanceId) {
			const s = this._bisectState;
			if (s && s.instanceId === instanceId) {
				s.finished = true;
				s.finishReason = 'giveup';
				this._bisectRestore();
			}
			return this._mock(this._bisectPayload());
		},
		_bisectApplyStep() {
			const s = this._bisectState;
			if (!s || s.finished) return;
			if (s.candidates.length === 0) { s.finished = true; s.finishReason = 'empty'; this._bisectRestore(); return; }
			if (s.candidates.length === 1) { s.finished = true; s.culprit = s.candidates[0]; this._bisectRestore(); return; }
			const half = Math.floor(s.candidates.length / 2);
			s.disabledHalf = s.candidates.slice(0, half);
			s.waitingForReport = true;
			s.step++;
			const mods = SG.mock.mods[s.instanceId] || [];
			s.disabledHalf.forEach(name => { const m = mods.find(x => x.fileName === name); if (m) m.enabled = false; });
		},
		_bisectRestore() {
			const s = this._bisectState;
			if (!s) return;
			const mods = SG.mock.mods[s.instanceId] || [];
			s.enabledAtStart.forEach(name => { const m = mods.find(x => x.fileName === name); if (m) m.enabled = true; });
		},
		_bisectPayload() {
			const s = this._bisectState;
			return {
				enabledAtStart: s ? s.enabledAtStart : [],
				candidates: s ? s.candidates : [],
				step: s ? s.step : 0,
				waitingForReport: s ? s.waitingForReport : false,
				disabledHalf: s ? s.disabledHalf : [],
				culprit: s ? s.culprit : null,
				finished: s ? s.finished : false,
				finishReason: s ? s.finishReason : null,
			};
		},
		async deleteInstance(instanceId) {
			const idx = SG.mock.instances.findIndex(x => x.id === instanceId);
			if (idx >= 0) SG.mock.instances.splice(idx, 1);
			return this._mock({ ok: true });
		},
		// MR03：mock 侧同样只新增列表记录，不产生任何文件操作。
		async registerInstance(gameDirectory, name) {
			const inst = {
				id: 'reg-' + Date.now(),
				name: (name || '').trim() || '已存在的实例',
				gameDirectory: gameDirectory || '',
				type: 'vanilla',
				version: '',
				savesCount: 0,
				modsCount: 0,
			};
			SG.mock.instances.push(inst);
			return this._mock({ ok: true, instance: inst });
		},
		async importInstance(fileName, fileBase64) {
			const inst = { id: 'imp-' + Date.now(), name: fileName.replace(/\.(zip|mrpack|mcm)$/i, '') + ' 导入', type: 'vanilla', version: '1.21.4' };
			SG.mock.instances.push(inst);
			return this._mock({ ok: true, instanceId: inst.id, name: inst.name });
		},
		async prelaunchCheck(instanceId) {
			return this._mock({ ok: true, errors: [], warnings: [], maxMemoryMb: 4096, gameDir: '[游戏目录]' });
		},
		async deleteServer(serverId) {
			const idx = SG.mock.servers.findIndex(x => x.id === serverId);
			if (idx >= 0) SG.mock.servers.splice(idx, 1);
			return this._mock({ ok: true });
		},
	};

	// 显式开关：默认 false（正式模式绝不静默回退到 mock）。
	// 设为 true 仅在 demo / 前端无后端环境（如 file:// 调试页）时由调用方主动打开。
	// 任何 catch 回退路径都必须把原始错误附在结果上，禁止吞掉。
	const allowMockFallback = !!(window.SG && window.SG.apiAllowMockFallback);
	const impl = isLive ? live : mock;
	const wrapped = {};
	const isReadOnly = k => k.startsWith('get') || k === 'searchContent';
	Object.keys(impl).forEach(k => {
		wrapped[k] = (...args) => {
			const p = impl[k].apply(impl, args);
			if (!p || !p.catch) return p;
			// 仅在显式开启 allowMockFallback 时才允许回退，且仅限只读方法。
			// 回退必须把真实错误以 _liveError 形式透传给上层，便于 UI 区分"无网络"与"无结果"。
			if (isLive && allowMockFallback && isReadOnly(k)) {
				return p.catch(err => {
					console.warn('[api] ' + k + ' live failed, fallback to mock (opt-in):', err && err.message);
					return mock[k].apply(mock, args).then(value => {
						if (value && typeof value === 'object') value._liveError = err && err.message ? String(err.message) : 'live_error';
						return value;
					});
				});
			}
			return p;
		};
	});
	wrapped._mock = mock._mock.bind(mock);
	wrapped._implMode = isLive ? 'live' : 'mock';
	wrapped._allowMockFallback = allowMockFallback;
	return wrapped;
})();

// Phase 2：暴露本地图标 URL 构造器，供 launcher.js 和测试复用
SG.buildContentIconUrl = (source, category, projectId) => {
	const src = (source && /^(modrinth|curseforge)$/i.test(source)) ? source.toLowerCase() : 'modrinth';
	const cat = (category && /^(mod|modpack|shader|resourcepack)$/i.test(category)) ? category.toLowerCase() : 'mod';
	const pid = (projectId == null) ? '' : String(projectId);
	if (!/^[a-zA-Z0-9_-]{1,80}$/.test(pid)) return '';
	const token = (window.SG && window.SG.apiToken) ? String(window.SG.apiToken) : '';
	if (!token) return '';
	return '/api/content/icon?source=' + encodeURIComponent(src)
		+ '&category=' + encodeURIComponent(cat)
		+ '&projectId=' + encodeURIComponent(pid)
		+ '&token=' + encodeURIComponent(token);
};
