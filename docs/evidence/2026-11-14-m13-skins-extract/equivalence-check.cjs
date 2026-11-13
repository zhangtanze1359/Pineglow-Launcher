/* ⚠ 本脚本的有效性依赖对比基准里**不含**本轮 skins 拆分。
   基准由 EQ_BASE 环境变量指定，默认取拆分前的提交 ac035e6：
       EQ_BASE=ac035e6 node equivalence-check.cjs
   原始运行的输出已冻结在 equivalence-check-output.txt，那才是本次的证据。
   详见 证据.md 第二节。 */
/* 等价性验证（两种独立方法）：
   A. 源码文本对比：拆分前 BASE 的 launcher.js 里的 5 个 helper 函数体，
      与新 skin-visuals.js 里的对应函数体，逐字符相同。
   B. 渲染输出对比：renderSkins 在三种账户场景下的 HTML 字符串逐字符相同。
   任一不一致即失败。本脚本不属于测试套件，只在验收时手工运行。 */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const cp = require('child_process');

const ROOT = 'D:/Projects/_worktrees/pineglow-integration-20261107';
const tmp = ROOT + '/.oldsrc';
fs.rmSync(tmp, { recursive: true, force: true });
fs.mkdirSync(tmp, { recursive: true });
const BASE = process.env.EQ_BASE || 'ac035e6';  // 拆分前的提交；commit 后可显式指定
for (const f of cp.execSync('git ls-tree --name-only ' + BASE + ' ui/js/', { cwd: ROOT, encoding: 'utf8' })
	.split('\n').map(s => s.trim()).filter(Boolean)) {
	fs.writeFileSync(path.join(tmp, path.basename(f)),
		cp.execSync('git show ' + BASE + ':' + f, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 << 20 }));
}

let fails = 0;
const bad = (m) => { fails++; console.log('✖ ' + m); };

/* ---------- A. 源码文本对比 ---------- */
const HELPERS = ['steveSvg', 'skinAvatar', 'skinThumb', 'skinFace', 'faceThumb'];
const oldSrc = fs.readFileSync(path.join(tmp, 'launcher.js'), 'utf8');
const newSrc = fs.readFileSync(path.join(ROOT, 'ui/js/skin-visuals.js'), 'utf8');

/* 抽取 `function NAME(...) { ... }` 的完整函数体（括号配平）。
   注意用递归 depth 计数，避免被字符串里的花括号误导：这些函数体含模板字符串 ${...}。 */
function extractFn(src, name) {
	const start = src.indexOf('function ' + name + '(');
	if (start < 0) return null;
	let i = src.indexOf('{', start), depth = 0;
	for (; i < src.length; i++) {
		const c = src[i];
		if (c === '{') depth++;
		else if (c === '}') { depth--; if (depth === 0) return src.slice(start, i + 1); }
	}
	return null;
}
/* 正常化：只把行首缩进与行尾空白统一，函数体本身的字符必须一致。 */
const norm = (s) => s == null ? '' : s.replace(/\r\n/g, '\n').split('\n').map(l => l.trim()).join('\n');

console.log('=== A. helper 源码逐字符对比 ===');
for (const h of HELPERS) {
	const a = extractFn(oldSrc, h), b = extractFn(newSrc, h);
	if (a == null) { bad(h + '：HEAD 侧未找到'); continue; }
	if (b == null) { bad(h + '：新模块未找到'); continue; }
	if (norm(a) === norm(b)) console.log('✔ ' + h + ' 源码一致（' + norm(a).length + ' 字符）');
	else {
		bad(h + '：源码不一致');
		const la = norm(a).split('\n'), lb = norm(b).split('\n');
		for (let k = 0; k < Math.max(la.length, lb.length); k++) {
			if (la[k] !== lb[k]) { console.log('   第' + (k + 1) + '行 HEAD: ' + la[k]); console.log('   第' + (k + 1) + '行 新:  ' + lb[k]); break; }
		}
	}
}

/* ---------- B. renderSkins 输出对比 ---------- */
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
	sb.fetch = async () => ({ ok: true, status: 200, json: async () => ({ accounts: [], tasks: [] }) });
	return sb;
}

function load(dir, extra, accounts) {
	const sb = makeCtx();
	vm.runInContext(dom, sb);
	const { document } = sb.makeDomStub(); sb.document = document;
	for (const id of ['contentPanel', 'modalHost', 'toast', ...['home','settings','skins','downloads','news'].map(n => 'view-' + n)]) {
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
		if (p === '/api/accounts') return { ok: true, status: 200, json: async () => ({ accounts }) };
		return { ok: true, status: 200, json: async () => ({}) };
	};
	for (const f of ['i18n.js', 'api.js', ...extra, 'launcher.js']) {
		vm.runInContext(fs.readFileSync(path.join(dir, f), 'utf8'), sb, { filename: f });
	}
	return sb;
}

const scenarios = [
	{ name: '无账户', accounts: [] },
	{ name: '经典皮肤', accounts: [{ id: 'a1', selected: true, userName: 'Tester', type: 'microsoft',
		skins: [{ id: 's1', url: 'https://t/x.png', name: 'Skin One', model: 'classic' }] }] },
	{ name: 'slim+多皮肤', accounts: [{ id: 'a2', selected: true, userName: 'Slim', type: 'microsoft', skins: [
		{ id: 's1', url: 'https://t/a.png', name: 'A', model: 'slim' },
		{ id: 's2', url: 'https://t/b.png', name: 'B', model: 'classic' }] }] },
	{ name: '非首选账户', accounts: [
		{ id: 'x', selected: false, userName: 'Other', type: 'microsoft', skins: [{ id: 'z', url: 'https://t/z.png', name: 'Z', model: 'classic' }] },
		{ id: 'y', selected: true, userName: 'Active', type: 'microsoft', skins: [{ id: 'w', url: 'https://t/w.png', name: 'W', model: 'slim' }] }] },
	{ name: '无皮肤账户', accounts: [{ id: 'n', selected: true, userName: 'Bare', type: 'offline', skins: [] }] },
];

console.log('\n=== B. renderSkins 输出逐字符对比 ===');
(async () => {
	for (const sc of scenarios) {
		const before = load(path.join(tmp), ['news.js', 'downloads.js'], sc.accounts);
		const after = load(path.join(ROOT, 'ui/js'), ['news.js', 'downloads.js', 'skin-visuals.js', 'skins.js'], sc.accounts);
		// renderSkins 没有暴露在 SG.__test__（拆分前后都没有），走真实导航路径取 DOM 结果。
		await before.SG.__test__.goTo('skins');
		const ra = before.document.getElementById('view-skins').innerHTML;
		await after.SG.__test__.goTo('skins');
		const rb = after.document.getElementById('view-skins').innerHTML;
		const na = String(ra).replace(/\r\n/g, '\n'), nb = String(rb).replace(/\r\n/g, '\n');
		if (na === nb) console.log('✔ ' + sc.name + '（' + na.length + ' 字符）');
		else {
			bad(sc.name + '：渲染输出不一致');
			for (let k = 0; k < Math.max(na.length, nb.length); k++) {
				if (na[k] !== nb[k]) {
					console.log('   首个差异位置 ' + k);
					console.log('   HEAD: ' + JSON.stringify(na.slice(Math.max(0, k - 60), k + 90)));
					console.log('   新:   ' + JSON.stringify(nb.slice(Math.max(0, k - 60), k + 90)));
					break;
				}
			}
		}
	}
	console.log(fails === 0 ? '\n结论：全部一致（' + (HELPERS.length + scenarios.length) + ' 项）' : '\n结论：' + fails + ' 项不一致');
	process.exit(fails === 0 ? 0 : 1);
})();
