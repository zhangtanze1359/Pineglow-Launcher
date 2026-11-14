/* settings 拆出等价性：BASE(ac035e6) launcher.js 里 settings 相关函数 vs 新 settings.js。
   本步的注入改写面最大（toast/openModal/closeModal/invalidate/refreshSidebarAccount/
   openAiManage/addUpdateBindings/loadAccounts/loadState/STATE/uiIcon + 6 处整页重渲染），
   所以归一化要把这些全部还原成拆分前的裸写法再比。 */
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const ROOT = 'D:/Projects/_worktrees/pineglow-integration-20261107';
const BASE = process.env.EQ_BASE || 'ac035e6';

const oldSrc = cp.execSync('git show ' + BASE + ':ui/js/launcher.js', { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 << 20 });
const newSrc = fs.readFileSync(path.join(ROOT, 'ui/js/settings.js'), 'utf8');

const FNS = ['AUTH_PRESETS', 'sourceLabel', 'openSourcePicker', 'renderSettings', 'bindSettings',
	'aiActiveName', 'renderAccountCards'];
const FN_FUNCS = ['sourceLabel', 'openSourcePicker', 'renderSettings', 'bindSettings', 'aiActiveName', 'renderAccountCards'];

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
function extractConst(src, name) {
	const m = new RegExp('const ' + name + ' = \\[[\\s\\S]*?\\n\\];').exec(src);
	return m ? m[0] : null;
}

const REWRITES = [
	[/_getState\(\)/g, 'STATE'],
	[/_loadState\(/g, 'loadState('],
	[/_loadAccounts\(/g, 'loadAccounts('],
	[/_toast\(/g, 'toast('],
	[/_openModal\(/g, 'openModal('],
	// closeModal 既被调用也被当成值传递（xxx.onclick = closeModal），
	// 所以不能只替换带括号的形式，要连裸引用一起还原。
	[/\b_closeModal\b/g, 'closeModal'],
	[/_invalidate\(/g, 'invalidate('],
	[/_refreshSidebarAccount\(/g, 'refreshSidebarAccount('],
	[/_openAiManage\(/g, 'openAiManage('],
	[/_addUpdateBindings\(/g, 'addUpdateBindings('],
	[/_uiIcon\(/g, 'uiIcon('],
	[/SG\.skinVisuals\.faceThumb\(/g, 'faceThumb('],
];
/* 整页重渲染：NEW 的 `await rerenderInPlace();` = BASE 的两行
   （`const v = ...; v.innerHTML = await renderSettings(); bindSettings(v);`）。
   注意缩进层级在原文里有 3 层与 4 层两种，归一化阶段已经 trim 掉行首空白，
   所以这里用 /gm 全替换，并把 BASE 侧的两行先折叠成一行再比。 */
function undoRerenderNew(s) {
	return s.replace(/^await rerenderInPlace\(\);$/gm,
		"const v = document.getElementById('view-settings'); v.innerHTML = await renderSettings(); bindSettings(v);");
}
function foldRerenderBase(s) {
	// BASE 的两行 → 一行，与 undoRerenderNew 产物对齐
	return s.replace(
		/^const v = document\.getElementById\('view-settings'\);$/m,
		"__V_LINE__");
}
/* 更简单可靠的做法：比较前把两侧的"整页重渲染"段落都替换成同一个占位符。
   BASE 是两行（const v = ...; v.innerHTML = ...; bindSettings(v);），
   NEW 是一行 await rerenderInPlace();。把两行整体吃掉再换成一行占位符，
   否则 BASE 会多留一个空行导致后续行错位。 */
function collapseRerender(s) {
	return s
		.replace(/^const v = document\.getElementById\('view-settings'\);\nv\.innerHTML = await renderSettings\(\); bindSettings\(v\);$/gm, '__RERENDER__')
		.replace(/^const v = document\.getElementById\('view-settings'\);\s*\nv\.innerHTML = await renderSettings\(\); bindSettings\(v\);$/gm, '__RERENDER__')
		.replace(/^await rerenderInPlace\(\);$/gm, '__RERENDER__')
		.replace(/^\s*$/gm, '');
}

function norm(s) {
	let out = String(s == null ? '' : s);
	for (const [re, to] of REWRITES) out = out.replace(re, to);
	// 紫合归一化产生的多余括号
	out = out.replace(/\({2,}/g, '(').replace(/\){2,}/g, ')');
	return out.replace(/\r\n/g, '\n').split('\n').map(l => l.trim())
		.filter(l => !l.startsWith('//'))
		.join('\n');
}

let fails = 0;
for (const f of FNS) {
	const isConst = (f === 'AUTH_PRESETS');
	const a = isConst ? extractConst(oldSrc, f) : extractFn(oldSrc, f);
	const bRaw = isConst ? extractConst(newSrc, f) : extractFn(newSrc, f);
	if (!a) { fails++; console.log('✖ ' + f + '：BASE 侧未找到'); continue; }
	if (!bRaw) { fails++; console.log('✖ ' + f + '：settings.js 未找到'); continue; }
	const A = collapseRerender(norm(a));
	const B = collapseRerender(norm(bRaw));
	if (A === B) { console.log('✔ ' + f + '（归一化后一致，' + A.length + ' 字符）'); continue; }

	fails++;
	console.log('✖ ' + f + ' 不一致');
	const la = A.split('\n'), lb = B.split('\n');
	let shown = 0;
	for (let k = 0; k < Math.max(la.length, lb.length); k++) {
		if (la[k] === lb[k]) continue;
		console.log('   BASE: ' + (la[k] === undefined ? '<无此行>' : la[k].slice(0, 160)));
		console.log('   NEW : ' + (lb[k] === undefined ? '<无此行>' : lb[k].slice(0, 160)));
		if (++shown >= 3) break;
	}
}
console.log(fails === 0 ? '\n结论：' + FNS.length + '/' + FNS.length + ' 归一化后全部一致' : '\n结论：' + fails + ' 个不一致');
process.exit(fails === 0 ? 0 : 1);
