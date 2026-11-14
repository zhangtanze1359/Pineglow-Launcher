/* settings 渲染输出对比：BASE(ac035e6) vs 工作区，逐字符。
   覆盖 renderSettings 的可观察分支：默认配置 / 高内存+自动 / 各 dlSource /
   newsFakeIp 开关 / AI 启用 / 有账户 / 无账户 / proxy 配置 / 多 Java。 */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const cp = require('child_process');

const ROOT = 'D:/Projects/_worktrees/pineglow-integration-20261107';
const BASE = process.env.EQ_BASE || 'ac035e6';
const tmp = ROOT + '/.oldsrc';
fs.rmSync(tmp, { recursive: true, force: true });
fs.mkdirSync(tmp, { recursive: true });
for (const f of cp.execSync('git ls-tree --name-only ' + BASE + ' ui/js/', { cwd: ROOT, encoding: 'utf8' })
	.split('\n').map(s => s.trim()).filter(Boolean)) {
	fs.writeFileSync(path.join(tmp, path.basename(f)),
		cp.execSync('git show ' + BASE + ':' + f, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 << 20 }));
}

const fixture = fs.readFileSync(path.join(ROOT, 'ui/tests/current-version-errors.test.cjs'), 'utf8');
const dom = fixture.slice(fixture.indexOf('function makeDomStub()'), fixture.indexOf('function loadLauncher(handlers)'));

function load(dir, extra, scenario) {
	const sb = {
		SG: {}, console,
		location: { protocol: 'http:', hash: '' }, history: { replaceState() {} },
		navigator: { language: 'zh-CN' },
		localStorage: { getItem() { return null; }, setItem() {} },
		setTimeout: (fn) => { const t = setTimeout(fn, 0); t.unref(); return t; },
		clearTimeout, setInterval: (a) => { const t = setInterval(a, 999999); t.unref(); return t; }, clearInterval,
		URL, URLSearchParams, requestAnimationFrame: fn => fn(),
		getComputedStyle: () => ({ width: '30px', borderLeftWidth: '0', borderRightWidth: '0' }),
		addEventListener() {}, confirm: () => true,
	};
	sb.window = sb; vm.createContext(sb);
	vm.runInContext(dom, sb);
	const { document } = sb.makeDomStub(); sb.document = document;
	for (const id of ['contentPanel', 'modalHost', 'toast', ...['home','settings','skins','content','downloads','news','worlds','servers','installations','playtime'].map(n => 'view-' + n)]) {
		const el = document.createElement('div'); el.id = id;
		if (id.startsWith('view-')) el.className = 'view';
		document.body.appendChild(el);
	}
	sb.document.contains = n => !!n?.isConnected;
	sb.document.createElement = ((orig) => tag => {
		const el = orig(tag);
		el.contains = node => { while (node) { if (node === el) return true; node = node.parent; } return false; };
		return el;
	})(document.createElement.bind(document));
	sb.fetch = async (url) => {
		const p = String(url).split('?')[0];
		if (p === '/api/state') return { ok: true, status: 200, json: async () => (scenario.state) };
		if (p === '/api/accounts') return { ok: true, status: 200, json: async () => ({ accounts: scenario.accounts || [] }) };
		return { ok: true, status: 200, json: async () => (scenario.other || {}) };
	};
	for (const f of ['i18n.js', 'api.js', ...extra, 'launcher.js']) {
		vm.runInContext(fs.readFileSync(path.join(dir, f), 'utf8'), sb, { filename: f });
	}
	return sb;
}

const BASE_STATE = {
	config: { maxMemoryMb: 4096, useAutoMemory: false, downloadSource: 'official', useMirror: false,
		proxyEnabled: false, proxyHost: '', proxyPort: 7890, proxyUsername: '', proxyPasswordConfigured: false,
		newsFakeIpCompat: false, aiDiagnosisEnabled: false, minimizeOnLaunch: false, autoCloseOnExit: false,
		curseForgeApiKeyConfigured: false, selectedAiProviderId: '' },
	instances: [{ id: 'i1', name: 'Inst', version: '1.20.1', loader: 'Fabric' }],
	accounts: [], java: [],
};

const SCENARIOS = [
	{ name: '默认配置·无账户', state: BASE_STATE, accounts: [] },
	{ name: '高内存+自动内存', state: { ...BASE_STATE, config: { ...BASE_STATE.config, maxMemoryMb: 12288, useAutoMemory: true } } },
	{ name: '源=bmclapi+镜像', state: { ...BASE_STATE, config: { ...BASE_STATE.config, downloadSource: 'bmclapi', useMirror: true } } },
	{ name: '源=curseforge', state: { ...BASE_STATE, config: { ...BASE_STATE.config, downloadSource: 'curseforge' } } },
	{ name: 'fakeIp 开关 on', state: { ...BASE_STATE, config: { ...BASE_STATE.config, newsFakeIpCompat: true } } },
	{ name: 'AI 诊断 on', state: { ...BASE_STATE, config: { ...BASE_STATE.config, aiDiagnosisEnabled: true, selectedAiProviderId: 'p1', aiProviders: [{ id: 'p1', name: 'DeepSeek' }] } } },
	{ name: '有账户(微软)', state: BASE_STATE,
		accounts: [{ id: 'a1', userName: 'Tester', type: 'microsoft', selected: true, skinCount: 2, capeCount: 1, skins: [{ id: 's1', url: 'https://t/x.png', model: 'classic' }] }] },
	{ name: '有账户(离线)', state: BASE_STATE, accounts: [{ id: 'a2', userName: 'Off', type: 'offline', selected: true }], },
	{ name: 'proxy 已配', state: { ...BASE_STATE, config: { ...BASE_STATE.config, proxyEnabled: true, proxyHost: '127.0.0.1', proxyPort: 7890, proxyUsername: 'u', proxyPasswordConfigured: true } } },
	{ name: '多 Java', state: { ...BASE_STATE, java: [{ path: '/j1', versionMajor: 21 }, { path: '/j2', versionMajor: 17 }] } },
	{ name: 'Java 已指定', state: { ...BASE_STATE, config: { ...BASE_STATE.config, javaPath: '/j2' }, java: [{ path: '/j2', versionMajor: 17 }] } },
];

let fails = 0;
(async () => {
	for (const sc of SCENARIOS) {
		const before = load(path.join(tmp), ['news.js', 'downloads.js'], sc);
		const after = load(path.join(ROOT, 'ui/js'), ['news.js', 'downloads.js', 'skin-visuals.js', 'skins.js', 'content.js', 'settings.js'], sc);
		let a, b;
		try { a = await before.SG.__test__.renderSettings(); } catch (e) { a = 'THROW: ' + e.message; }
		try { b = await after.SG.__test__.renderSettings(); } catch (e) { b = 'THROW: ' + e.message; }
		const na = String(a).replace(/\r\n/g, '\n'), nb = String(b).replace(/\r\n/g, '\n');
		// 哨兵
		if (na === 'THROW' || na.startsWith('THROW')) { fails++; console.log('✖ ' + sc.name + '：BASE 侧渲染抛错 ' + na.slice(0, 80)); continue; }
		if (nb.startsWith('THROW')) { fails++; console.log('✖ ' + sc.name + '：NEW 侧渲染抛错 ' + nb.slice(0, 80)); continue; }
		if (!/memSlider/.test(na)) { fails++; console.log('✖ ' + sc.name + '：渲染结果里没有 memSlider（对比无效）'); continue; }
		if (na === nb) { console.log('✔ ' + sc.name + '（' + na.length + ' 字符）'); continue; }
		fails++;
		console.log('✖ ' + sc.name + ' 不一致');
		for (let k = 0; k < Math.max(na.length, nb.length); k++) {
			if (na[k] !== nb[k]) {
				console.log('   首个差异 @' + k);
				console.log('   BASE: ' + JSON.stringify(na.slice(Math.max(0, k - 70), k + 110)));
				console.log('   NEW : ' + JSON.stringify(nb.slice(Math.max(0, k - 70), k + 110)));
				break;
			}
		}
	}
	fs.rmSync(tmp, { recursive: true, force: true });
	console.log(fails === 0 ? '\n结论：' + SCENARIOS.length + '/' + SCENARIOS.length + ' 场景渲染输出逐字符一致' : '\n结论：' + fails + ' 个场景不一致');
	process.exit(fails === 0 ? 0 : 1);
})();
