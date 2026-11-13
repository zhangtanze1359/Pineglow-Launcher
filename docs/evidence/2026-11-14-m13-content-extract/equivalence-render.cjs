/* content 拆出的**渲染输出**逐字符对比：拆分前(BASE) vs 拆分后(工作区)。
   覆盖 renderContent 的全部可观察分支：
     无实例 / 有实例 / 搜索有结果 / 搜索无结果 / 搜索失败(错误块) /
     状态加载失败(filter badge) / 各种分类与来源。
   用法：EQ_BASE=ac035e6 node render-compare.cjs */
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

function makeCtx() {
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
	return sb;
}

/* 场景驱动的加载：respond 决定 /api/search-content 等响应 */
function load(dir, extra, scenario) {
	const sb = makeCtx();
	vm.runInContext(dom, sb);
	const { document } = sb.makeDomStub(); sb.document = document;
	for (const id of ['contentPanel', 'modalHost', 'toast', ...['home','settings','skins','content','downloads','news'].map(n => 'view-' + n)]) {
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
		if (p === '/api/state') return { ok: true, status: 200, json: async () => ({ config: {}, instances: scenario.instances || [], accounts: [], java: [] }) };
		if (p === '/api/content/search') {
			// api.js 读的是 data.results（不是 items），字段名写错会让"有结果"分支退化成空列表，
			// 那样这个场景就什么都没测到——必须用正确的字段名。
			if (scenario.searchFails) return { ok: false, status: 500, json: async () => ({ error: '<img src=x onerror=alert(1)>' }) };
			return { ok: true, status: 200, json: async () => ({ results: scenario.items || [] }) };
		}
		if (p === '/api/content-tasks') return { ok: true, status: 200, json: async () => ({ tasks: [] }) };
		return { ok: true, status: 200, json: async () => ({}) };
	};
	for (const f of ['i18n.js', 'api.js', ...extra, 'launcher.js']) {
		vm.runInContext(fs.readFileSync(path.join(dir, f), 'utf8'), sb, { filename: f });
	}
	return sb;
}

/* 用 SG.__test__ 的生产入口驱动：设置筛选状态 → 走真实 goTo('content') */
async function renderView(box, scenario) {
	const T = box.SG.__test__;
	T.resetState && T.resetState();
	T.setSelectedInstanceId(scenario.instanceId || 'i1');
	if (scenario.cat) T.setContentCat(scenario.cat);
	if (scenario.query != null) T.setContentQuery(scenario.query);
	if (scenario.source) T.setContentSource(scenario.source);
	if (scenario.loadError != null) T.setContentLoadError(scenario.loadError);
	const el = box.document.getElementById('view-content');
	delete el.dataset.rendered;
	await T.goTo('content');
	return el.innerHTML;
}

const ITEMS = [
	{ id: 'p1', title: 'Sodium', author: 'jellysquid', desc: 'Rendering engine', downloads: '1.2M',
		icon: '📦', iconUrl: '/api/content/icon?project=p1&token=t', source: 'modrinth', category: 'mod',
		versions: ['1.20.1', '1.21'], loaders: ['fabric'] },
	{ id: 'p2', title: 'Create', author: 'simibubi', desc: 'Tech mod', downloads: '5M',
		icon: '⚙', iconUrl: '', source: 'curseforge', category: 'mod',
		versions: ['1.20.1'], loaders: ['forge'] },
];

const SCENARIOS = [
	{ name: '有实例+有结果', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], items: ITEMS },
	{ name: '有实例+空结果', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], items: [] },
	{ name: '无实例(未过滤)', instances: [], items: ITEMS },
	{ name: 'Vanilla 实例(不带 loader)', instances: [{ id: 'i1', name: 'V', version: '1.20.1', loader: 'Vanilla' }], items: ITEMS },
	{ name: '搜索失败(错误块)', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], searchFails: true },
	{ name: '分类=整合包', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], items: ITEMS, cat: 'modpack' },
	{ name: '分类=光影', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], items: ITEMS, cat: 'shader' },
	{ name: '来源=curseforge', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], items: ITEMS, source: 'curseforge' },
	{ name: '带搜索词', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], items: ITEMS, query: 'sodium' },
	{ name: '预置 loadError(错误块保留)', instances: [{ id: 'i1', name: '实例一', version: '1.20.1', loader: 'Fabric' }], items: [], loadError: 'boom <script>' },
];

let fails = 0;
(async () => {
	for (const sc of SCENARIOS) {
		// 基准侧（ac035e6）还没有 skin-visuals/skins/content 三个新模块：
		// 那时 skins 与 content 的实现都还在 launcher.js 里，所以只加载 news/downloads。
		const before = load(path.join(tmp), ['news.js', 'downloads.js'], sc);
		const after = load(path.join(ROOT, 'ui/js'), ['news.js', 'downloads.js', 'skin-visuals.js', 'skins.js', 'content.js'], sc);
		let a, b;
		try { a = await renderView(before, sc); } catch (e) { a = 'THROW: ' + e.message; }
		try { b = await renderView(after, sc); } catch (e) { b = 'THROW: ' + e.message; }
		const na = String(a).replace(/\r\n/g, '\n'), nb = String(b).replace(/\r\n/g, '\n');
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
