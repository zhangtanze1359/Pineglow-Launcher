/* ================= API 层（live：串通 C# LocalApiService；mock：file:// 验收用） ================= */
window.SG = window.SG || {};

SG.pages = {};
SG.registerPage = (name, def) => { SG.pages[name] = def; };

SG.apiMode = typeof location !== 'undefined' && location.protocol === 'http:' ? 'live' : 'mock';

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
	downloads: [],
};

/* ================= 统一异步接口（live：fetch 真实 API；mock：内存数据） ================= */
SG.api = (() => {
	const BASE = '';
	const delay = 60;
	const isLive = SG.apiMode === 'live';

	const _mock = async value => { await new Promise(r => setTimeout(r, delay)); return value; };

	const _req = async (path, options) => {
		const res = await fetch(BASE + path, options);
		if (!res.ok) {
			let msg = 'HTTP ' + res.status;
			try {
				const j = await res.json();
				if (j && (j.error || j.message)) msg = j.error || j.message;
			} catch (e) { /* 非 JSON 错误体 */ }
			throw new Error(msg + ' (' + path + ')');
		}
		return res.json();
	};

	const _post = (path, body) => _req(path, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(body || {}),
	});

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
		selected: a.id === selectedId,
		skins: (a.skins && a.skins.length ? a.skins : (a.skinUrl ? [{ id: a.id + '-skin', name: a.userName + ' 的皮肤', url: a.skinUrl, colors: { head: '#f0b98a', body: '#e8714f', arm: '#e8714f', leg: '#3d5a80' } }] : [])).map(s => ({
			id: s.id || s.skinId,
			name: s.name || '皮肤',
			url: s.url || s.skinUrl || '',
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

	const mapContent = c => ({
		id: c.id || c.projectId,
		title: c.title || '未知项目',
		desc: c.desc || c.description || '',
		author: c.author || '',
		downloads: fmtCount(c.downloads),
		icon: c.icon === '📦' ? '📦' : (c.icon && c.icon.startsWith('data:') ? '📦' : '📦'),
		iconBg: 'linear-gradient(135deg,#90c4ff,#1f5ff1)',
		versions: c.versions || [],
		loader: c.loader || '',
		category: c.category || 'mod',
		rating: c.rating || 4.5,
		iconUrl: c.icon || '',
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
		progress: t.progress || 0,
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
					modSource: 'Modrinth',
					curseForgeApiKeyConfigured: !!data.config.curseForgeApiKey,
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
		async searchContent(query, category) {
			const cat = category && category !== 'all' ? category : 'mod';
			// 后端对空 query 返回空列表；首屏用分类默认热词兜底，保证进页即有内容（与 mock 一致）
			const DEFAULT_QUERY = { mod: 'sodium', modpack: 'fabulously', shader: 'shaders', resourcepack: 'faithful' };
			const q = (query && query.trim()) ? query.trim() : (DEFAULT_QUERY[cat] || 'sodium');
			const params = new URLSearchParams({ q, category: cat, source: contentSource || 'modrinth' });
			const data = await _req('/api/content/search?' + params.toString());
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
			return { ok: !!res.ok };
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
		async selectSource(key) {
			const res = await _post('/api/sources/select', { key });
			return { ok: !!res.ok, selected: res.selected || '', useMirror: !!res.useMirror };
		},
		async getRemoteVersions() {
			const data = await _req('/api/versions');
			try {
				const r = await _req('/api/versions/remote');
				return (r.versions || []).map(v => mapVersion(v, data.local));
			} catch (e) {
				return (data.local || []).map(id => ({ id, type: 'release', releaseTime: '', sizeText: '已安装', installed: true }));
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
		async microsoftLogin() {
			const res = await _post('/api/accounts/microsoft', {});
			if (!res || !res.ok) throw new Error((res && res.message) || '微软登录失败');
			const acc = mapAccount(res.account, res.account.id);
			const a = await this.getAccounts();
			return { ok: true, account: acc, accounts: a.accounts };
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
			return await _req('/api/update');
		},
		async applyUpdate() {
			return await _post('/api/update/apply', {});
		},
		async launchInstance(instanceId) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/launch', {});
			return { ok: !!res.ok, launched: !!res.ok, pid: res.pid || 0, message: res.error || '' };
		},
		async deleteInstance(instanceId) {
			await _post('/api/instances/' + encodeURIComponent(instanceId) + '/delete', {});
			return { ok: true };
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
		async searchContent(query, category) {
			let list = SG.mock.content;
			if (category && category !== 'all') list = list.filter(c => c.category === category);
			if (query) { const q = query.toLowerCase(); list = list.filter(c => c.title.toLowerCase().includes(q) || c.desc.toLowerCase().includes(q) || c.author.toLowerCase().includes(q)); }
			return this._mock(list);
		},		async getAccounts() { return this._mock({ accounts: SG.mock.accounts, capes: SG.mock.capes }); },
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
			return this._mock({ ok: true });
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
		async microsoftLogin() {
			const acc = { id: 'ms-' + Date.now(), userName: 'Steve_Adv', type: 'microsoft', selected: true, avatarBg: 'linear-gradient(135deg,#1e7a54,#0f3d2a)' };
			SG.mock.accounts.forEach(a => a.selected = false);
			SG.mock.accounts.push(acc);
			SG.mock.settings.accountMode = 'microsoft';
			return this._mock({ ok: true, account: acc, accounts: SG.mock.accounts });
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
		async deleteInstance(instanceId) {
			const idx = SG.mock.instances.findIndex(x => x.id === instanceId);
			if (idx >= 0) SG.mock.instances.splice(idx, 1);
			return this._mock({ ok: true });
		},
		async deleteServer(serverId) {
			const idx = SG.mock.servers.findIndex(x => x.id === serverId);
			if (idx >= 0) SG.mock.servers.splice(idx, 1);
			return this._mock({ ok: true });
		},
	};

	const impl = isLive ? live : mock;
	const wrapped = {};
	const isReadOnly = k => k.startsWith('get') || k === 'searchContent';
	Object.keys(impl).forEach(k => {
		wrapped[k] = (...args) => {
			const p = impl[k].apply(impl, args);
			if (!p || !p.catch) return p;
			if (isLive && isReadOnly(k)) {
				return p.catch(err => {
					console.warn('[api] ' + k + ' failed, falling back to mock:', err && err.message);
					return mock[k].apply(mock, args);
				});
			}
			return p;
		};
	});
	wrapped._mock = mock._mock.bind(mock);
	return wrapped;
})();
