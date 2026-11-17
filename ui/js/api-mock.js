/* ================= mock 数据 + mock 实现（架构评审 ⑦，2026-11-17） =================

   为什么单独一个文件：
     这两段（数据 + 实现）占了 api.js 的四成，而它们**只在非 live 模式**用得上。
     原来它们住在 api.js 里，结果是——发布包带着一整套夹具世界，
     live 分支还曾经误把夹具数据当真实状态返回（memoryRecommendation，已修）。

   加载顺序：**必须先于 api.js**（index.html 与所有前端测试装载器都按这个顺序）。
   api.js 只在 `SG.apiMode !== 'live'` 且 `SG.createMockApi` 存在时才构造 mock；
   缺这个文件而强行用 mock 模式 → 启动即报错，不静默降级。
   ========================================================================== */

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

/* ================= mock 实现（与 live 同形状，只读写上面的内存数据） ================= */
SG.createMockApi = function (delay) {
	const mock = {
		async _mock(value) { await new Promise(r => setTimeout(r, delay)); return value; },
		async getState() {
			return this._mock({ config: SG.mock.settings, memory: SG.mock.memoryRecommendation, instances: SG.mock.instances, accounts: SG.mock.accounts, java: SG.mock.java });
		},
		async updateConfig(patch) {
			Object.assign(SG.mock.settings, patch);
			return this._mock({ ok: true, config: SG.mock.settings });
		},
		async updateInstanceSettings(instanceId, patch) {
			const inst = (SG.mock.instances || []).find(i => i.id === instanceId);
			if (inst && patch && Object.prototype.hasOwnProperty.call(patch, 'autoBackupOnExit')) inst.autoBackupOnExit = !!patch.autoBackupOnExit;
			return this._mock({ ok: true, instanceId, autoBackupOnExit: !!(inst && inst.autoBackupOnExit) });
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
		async getModUpdates(instanceId) {
			return this._mock({ checkedCount: 0, skipped: { noMetadata: 0, upToDate: 0, noDownload: 0, queryFailed: 0, noCompatibleVersion: 0, items: [] }, candidates: [] });
		},
		async startBatchUpdate(instanceId, items) {
			return this._mock({ ok: true, jobId: 'mock-job', state: 'running', items: (items || []).map(i => ({ fileName: i.fileName, status: 'pending' })) });
		},
		async getBatchUpdate(instanceId, jobId) {
			return this._mock({ ok: true, jobId, state: 'ok', cancelRequested: false, failedFile: null, failedReason: null, items: [] });
		},
		async cancelBatchUpdate(instanceId, jobId) {
			return this._mock({ ok: true, jobId, state: 'cancelled', items: [] });
		},
		async undoBatchUpdate(instanceId, jobId) {
			return this._mock({ ok: true, jobId, undoed: [], conflicts: [], state: 'undone', items: [] });
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
			// mock 通道也要给出与 live 同形的返回，否则演示模式下界面分支不一致。
			return this._mock({ ok: true, recoveryId: 'mock-' + Date.now().toString(36), sizeBytes: 0, recoverable: true, retentionDays: 30 });
		},
		async listSaveRecoveries(instanceId) {
			return this._mock([]);
		},
		async restoreSaveRecovery(instanceId, operationId) {
			return this._mock({ ok: true, restoredPath: '[恢复] ' + operationId, saveFolderName: '' });
		},
		async purgeSaveRecovery(instanceId, operationId) {
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
	return mock;
};
