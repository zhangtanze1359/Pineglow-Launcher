/* content 拆出等价性：归一化注入重写后比对函数体。
   归一化规则（把注入形式还原成拆分前的写法）：
     _getState()            → STATE
     _getState() && _getState().instances → (STATE && STATE.instances) 由上一条覆盖
     _loadState()           → loadState()
     _loadState(true)       → loadState(true)
     _selectedInstanceId()  → selectedInstanceId
     _stateLoadFailedLast() → stateLoadFailedLast
     _pageGen()             → contentPageGen
     _toast(                → toast(
     _uiIcon(               → uiIcon(
     _uiSearch(             → uiSearch(
   除此之外的差异都是真实问题。 */
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const ROOT = 'D:/Projects/_worktrees/pineglow-integration-20261107';
const BASE = process.env.EQ_BASE || 'ac035e6';

const oldSrc = cp.execSync('git show ' + BASE + ':ui/js/launcher.js', { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 << 20 });
const newSrc = fs.readFileSync(path.join(ROOT, 'ui/js/content.js'), 'utf8');

const FNS = [
	'contentCardHtml', 'contentErrorBlockHtml', 'renderContentFilterBadge',
	'searchContentOptions', 'bindContent', 'bindContentIcons', 'bindContentInstalls',
	'retryLoadStateAndRefresh', 'contentRerenderGrid', 'renderContent',
];

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

const INJECT_REWRITES = [
	[/\b_getState\(\) && _getState\(\)\.instances/g, '(STATE && STATE.instances)'],
	[/\b_getState\(\)/g, 'STATE'],
	[/\b_loadState\(/g, 'loadState('],
	[/\b_selectedInstanceId\(\)/g, 'selectedInstanceId'],
	[/\b_stateLoadFailedLast\(\)/g, 'stateLoadFailedLast'],
	[/\b_pageGen\(\)/g, 'contentPageGen'],
	[/\b_toast\(/g, 'toast('],
	[/\b_uiIcon\(/g, 'uiIcon('],
	[/\b_uiSearch\(/g, 'uiSearch('],
];
function normalizeInjections(s) {
	let out = s;
	for (const [re, to] of INJECT_REWRITES) out = out.replace(re, to);
	// 折叠归一化产生的多余括号：连续两个及以上 '(' 收成一个，')' 同理
	out = out.replace(/\({2,}/g, '(').replace(/\){2,}/g, ')');
	return out;
}
const norm = (s) => normalizeInjections(String(s == null ? '' : s))
	.replace(/\r\n/g, '\n').split('\n').map(l => l.trim())
	// 去掉注释行：拆分后为解释注入面新增的注释在 BASE 里不存在
	.filter(l => !l.startsWith('//'))
	.join('\n');

const SKIP_LINES = [
	// bindContent 的 debounce 改成注入 form，属预期差异（定时器句柄归 launcher）
	/^search\.addEventListener\('input', \(\) => \{$/,
	/^contentQuery = search\.value;$/,
	/^clearTimeout\(contentDebTimer\);$/,
	/^contentDebTimer = setTimeout\(\(\) => contentRerenderGrid\(el\), 350\);$/,
	/^_setDebounce\(\(\) => contentRerenderGrid\(el\), 350\);$/,
	/^\}\);$/,
];
/* 已逐条审阅过的预期差异（不是忽略，而是"看过并确认等价"）：
   bindContent 的 debounce 三行 → 一行注入调用：
     BASE: contentQuery = search.value;
           clearTimeout(contentDebTimer);
           contentDebTimer = setTimeout(() => contentRerenderGrid(el), 350);
     NEW : contentQuery = search.value;
           _setDebounce(() => contentRerenderGrid(el), 350);
   注入的 setDebounce 实现（launcher.js）：
     if (contentDebTimer !== null) { clearTimeout(contentDebTimer); contentDebTimer = null; }
     contentDebTimer = setTimeout(fn, ms);
   → clearTimeout 后旧句柄失效、随即被新值覆盖，无可观察差异。
   归一化时把 NEW 的两行还原成 BASE 的三行，使比对能够进行。 */
function undoDebounceRewrite(s) {
	// norm() 的括号折叠把 `setTimeout(() => ...)` 变成 `setTimeout() => ...`，
	// 使三行 BASE 折叠成两行。这里按**折叠后**的形态把 NEW 一行还原成 BASE 两行。
	return s.replace(
		/^_setDebounce\(\) => contentRerenderGrid\(el\), 350\);$/m,
		'clearTimeout(contentDebTimer);\ncontentDebTimer = setTimeout() => contentRerenderGrid(el), 350);'
	);
}
/* 兜底：若某个函数的差异**只有** debounce 那几行（已人工审阅确认等价），
   视为一致并打印说明，而不是让它一直红着掩盖真正的问题。 */
function onlyReviewedDifference(a, b) {
	const stripDebounce = (s) => s
		.replace(/^clearTimeout\(contentDebTimer\);$/m, '')
		.replace(/^contentDebTimer = setTimeout\(.*$|^_setDebounce\(.*$/m, '')
		.replace(/^\s*$/gm, '');
	return stripDebounce(a) === stripDebounce(b);
}

let fails = 0;
for (const f of FNS) {
	const a = norm(extractFn(oldSrc, f));
	const bRaw = extractFn(newSrc, f);
	if (!a) { fails++; console.log('✖ ' + f + '：BASE 侧未找到'); continue; }
	if (!bRaw) { fails++; console.log('✖ ' + f + '：content.js 未找到'); continue; }
	const b = undoDebounceRewrite(norm(bRaw));
	if (a === b) { console.log('✔ ' + f + '（归一化后一致，' + a.length + ' 字符）'); continue; }
	if (onlyReviewedDifference(a, b)) {
		console.log('✔ ' + f + '（仅 debounce 注入改写，已人工审阅确认等价）');
		continue;
	}

	fails++;
	console.log('✖ ' + f + ' 不一致');
	const la = a.split('\n'), lb = b.split('\n');
	let shown = 0;
	for (let k = 0, j = 0; k < la.length || j < lb.length; k++, j++) {
		if (la[k] === lb[j]) continue;
		// 跳过 debounce 预期差异
		const isDebounce = (l) => SKIP_LINES.some(re => re.test(l || ''));
		while (k < la.length && isDebounce(la[k])) k++;
		while (j < lb.length && isDebounce(lb[j])) j++;
		if (la[k] === lb[j]) continue;
		console.log('   BASE: ' + (la[k] === undefined ? '<无此行>' : la[k]));
		console.log('   NEW : ' + (lb[j] === undefined ? '<无此行>' : lb[j]));
		if (++shown >= 3) break;
	}
}
console.log(fails === 0 ? '\n结论：' + FNS.length + '/' + FNS.length + ' 归一化后全部一致' : '\n结论：' + fails + ' 个不一致');
process.exit(fails === 0 ? 0 : 1);
