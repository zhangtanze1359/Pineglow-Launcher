/* ================= API 层（live：串通 C# LocalApiService；mock：file:// 验收用） ================= */
window.SG = window.SG || {};

SG.pages = {};
SG.registerPage = (name, def) => { SG.pages[name] = def; };

// The shipped UI must never display sample accounts or simulated successes after API startup fails.
// Standalone test/demo contexts without the product marker retain their explicit mock seam.
const productUi = document.documentElement && document.documentElement.getAttribute && document.documentElement.getAttribute('data-pineglow-product') === 'true';
SG.apiMode = productUi || (typeof location !== 'undefined' && location.protocol === 'http:') ? 'live' : 'mock';
SG.apiToken = (document.querySelector('meta[name="pineglow-session-token"]') || {}).content || '';

/* ================= mock 世界已搬到 ui/js/api-mock.js（必须先于本文件加载） ================= */
// 架构评审 ⑦：数据与实现合起来占本文件的四成，而它们只在非 live 模式用得上。
// 本文件只保留：传输（fetch）+ DTO→视图模型映射 + 显式 mock 回退开关。

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
		// D3②：实例级「退出后自动备份」开关（旧配置没有该字段 → false）
		autoBackupOnExit: !!i.autoBackupOnExit,
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
				instances: (data.instances || []).map(i => mapInstance(i, data.config.selectedInstanceId)),
				accounts: (data.accounts || []).map(a => mapAccount(a, data.config.selectedAccountId)),
				java: (data.java || []).map(j => ({ path: j.javaPath, versionMajor: j.versionMajor, versionText: j.versionText, vendor: j.vendor, valid: !!j.isValid })),
			};
		},
		/** N09-09 补充（游戏退出显式提示）：轻量「退出事件」状态。
		 *  只读运行注册表，不含配置与 Java 扫描。since 之后的退出按批次逐条返回，
		 *  两次退出都发生在两次轮询之间时一条都不丢（第七轮反例）。 */
		async getGameStatus(since) {
			const path = '/api/game-status';
			const data = await _req(Number.isFinite(since) ? path + '?since=' + Math.floor(since) : path);
			const mapExit = e => ({
				eventId: Number(e.eventId) || 0,
				instanceId: e.instanceId || '',
				exitCode: (e.exitCode === null || e.exitCode === undefined) ? null : Number(e.exitCode),
				durationMs: Number(e.durationMs) || 0,
				quickExit: !!e.quickExit,
				needsNotice: !!e.needsNotice,
			});
			const exits = Array.isArray(data.exits) ? data.exits.map(mapExit) : [];
			const e = data && data.lastGameExit;
			return {
				lastGameExit: e ? mapExit(e) : null,
				exits,
				lastEventId: Number(data.lastEventId) || 0,
				runningCount: (data.runningCount === null || data.runningCount === undefined) ? null : Number(data.runningCount),
			};
		},
		async updateConfig(patch) {
			await _post('/api/config', patch);
			const s = await this.getState();
			return { ok: true, config: s.config };
		},
		/** D3②：实例级设置（目前只有 autoBackupOnExit）。 */
		async updateInstanceSettings(instanceId, patch) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/settings', patch || {});
			return { ok: !!res.ok, instanceId: res.instanceId, autoBackupOnExit: !!res.autoBackupOnExit };
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
		// D3③：更新检查 + 批量更新（任务式：建任务 → 轮询 → 撤销）
		async getModUpdates(instanceId) {
			const data = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/mods/updates');
			return {
				checkedCount: data.checkedCount || 0,
				skipped: data.skipped || {},
				candidates: data.candidates || [],
			};
		},
		async startBatchUpdate(instanceId, items) {
			const data = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/mods/batch-update', { items });
			return data;
		},
		async getBatchUpdate(instanceId, jobId) {
			return await _req('/api/instances/' + encodeURIComponent(instanceId) + '/mods/batch-update/' + encodeURIComponent(jobId));
		},
		async cancelBatchUpdate(instanceId, jobId) {
			return await _post('/api/instances/' + encodeURIComponent(instanceId) + '/mods/batch-update/' + encodeURIComponent(jobId) + '/cancel', {});
		},
		async undoBatchUpdate(instanceId, jobId) {
			return await _post('/api/instances/' + encodeURIComponent(instanceId) + '/mods/batch-update/' + encodeURIComponent(jobId) + '/undo', {});
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
			let local = options?.preferRemote ? null : (await _req('/api/versions')).local || [];
			try {
				const remote = await _req('/api/versions/remote');
				const versions = remote.versions || [];
				if (!local && versions.some(version => version.installed === undefined)) {
					local = (await _req('/api/versions')).local || [];
				}
				return versions.map(version => mapVersion(version, local));
			} catch (e) {
				local ??= await _req('/api/versions').then(data => data.local || [], () => []);
				const localVersions = local.map(id => ({ id, type: 'release', releaseTime: '', sizeText: '已安装', installed: true }));
				if (options?.throwOnRemoteError) {
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
			// M2-1①：删除已改为"移入恢复区"，后端返回 recoveryId 供"已删除存档"列表使用。
			return {
				ok: !!res.ok,
				recoveryId: res.recoveryId || '',
				sizeBytes: Number(res.sizeBytes) || 0,
				recoverable: res.recoverable === true,
				retentionDays: Number(res.retentionDays) || 0,
			};
		},
		async listSaveRecoveries(instanceId) {
			const res = await _req('/api/instances/' + encodeURIComponent(instanceId) + '/saves/recoveries');
			return Array.isArray(res.recoveries) ? res.recoveries : [];
		},
		async restoreSaveRecovery(instanceId, operationId) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/saves/recoveries/' + encodeURIComponent(operationId) + '/restore', {});
			return { ok: !!res.ok, restoredPath: res.restoredPath || '', saveFolderName: res.saveFolderName || '' };
		},
		async purgeSaveRecovery(instanceId, operationId) {
			const res = await _post('/api/instances/' + encodeURIComponent(instanceId) + '/saves/recoveries/' + encodeURIComponent(operationId) + '/purge', {});
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
			return {
				ok: !!res.ok,
				launched: !!res.ok,
				pid: res.pid || 0,
				message: res.error || '',
				// N09-09（退出归属）：服务端启动预约时刻的退出事件号，用作界面的历史分界
				exitEventBase: (res.exitEventBase === null || res.exitEventBase === undefined) ? null : Number(res.exitEventBase),
			};
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
		// 反馈入口（2026-11-24）：写到本地数据目录，界面提示文件位置。
		async submitFeedback(message, contact) {
			const res = await _post('/api/feedback', { message: message || '', contact: contact || '' });
			if (!res || !res.ok) throw new Error((res && res.error) || '提交失败');
			return { ok: true, path: res.path || '' };
		},
		async lastFeedback() {
			const res = await _req('/api/feedback/last');
			return { ok: !!(res && res.ok), path: (res && res.path) || '' };
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
		// 分块导入（2026-11-24）：>24 MiB 的整合包走整包 base64 会被请求体上限拒绝。
		// begin 拿 uploadId → 逐块发（每块 ≤ chunkBytes）→ finish 触发服务端导入。
		// onProgress(received, total) 可选，用于把进度反馈给界面。
		async importInstanceChunked(file, operationId, onProgress) {
			const buf = await file.arrayBuffer();
			const bytes = new Uint8Array(buf);
			// base64 膨胀 4/3；超过上限的包必须走分块。
			const begin = await _post('/api/instances/import/upload-begin', {
				fileName: file.name,
				sizeBytes: bytes.length,
			});
			if (!begin || !begin.ok) throw new Error((begin && begin.error) || '无法开始上传');
			const uploadId = begin.uploadId;
			const chunkBytes = begin.chunkBytes || (2 * 1024 * 1024);

			const toB64 = (u8) => {
				let s = '';
				const step = 0x8000; // 分段拼接，避免 apply 参数个数上限
				for (let i = 0; i < u8.length; i += step) {
					s += String.fromCharCode.apply(null, u8.subarray(i, i + step));
				}
				return btoa(s);
			};

			for (let offset = 0; offset < bytes.length; offset += chunkBytes) {
				const slice = bytes.subarray(offset, Math.min(offset + chunkBytes, bytes.length));
				const r = await _post('/api/instances/import/upload-chunk', {
					uploadId,
					offset,
					dataBase64: toB64(slice),
				});
				if (!r || !r.ok) throw new Error((r && r.error) || '分块上传失败');
				if (onProgress) { try { onProgress(r.received, bytes.length); } catch { /* 进度回调失败不影响上传 */ } }
			}

			const fin = await _post('/api/instances/import/upload-finish', { uploadId });
			if (!fin || !fin.ok) {
				const err = new Error((fin && fin.error) || '导入失败');
				err.cancelled = !!(fin && fin.cancelled);
				throw err;
			}
			return { ok: true, instanceId: fin.instanceId || '', name: fin.name || '' };
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
			if (!data || typeof data.ok !== 'boolean') return null;
			return {
				ok: data.ok,
				issues: Array.isArray(data.issues) ? data.issues : [],
				javaInstallMajor: data.javaInstallMajor || 0,
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

// ⑦：mock 实现也从 api-mock.js 来；live 模式下它就是 null。
	const mock = typeof SG.createMockApi === 'function' ? SG.createMockApi(delay) : null;
	if (!isLive && !mock) {
		throw new Error('mock 模式需要 ui/js/api-mock.js（必须先于 api.js 加载）');
	}


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
			if (isLive && mock && allowMockFallback && isReadOnly(k)) {
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
	// token 拼进 URL 的规则只在这里实现一次（安全相关：复制第二份就等于给了第二条入口）
	wrapped.buildContentIconUrl = buildContentIconUrl;
	return wrapped;
})();

// Phase 2：对外只暴露 **同一个** 构造器实例（原来是两份一字不差的实现）。
// content-icon.test.cjs 断言两者是同一个函数：复制第二份会立刻变红。
SG.buildContentIconUrl = SG.api.buildContentIconUrl;
