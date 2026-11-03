'use strict';
/*
 * MR07 补测（缺口登记表 A6）：二分定位的「临时测试世界 / 实例副本」**安全引导**。
 *
 * 计划要求："UI 要求在临时测试世界/实例副本运行"并给出**可校验的**存档备份/副本指引。
 * MR07 报告 §六 原话："**"临时测试世界 / 实例副本"的引导未实现** …… 本包**没有**做这部分 UI/文案"。
 *
 * 本文件把三件事钉住：
 *   ① 面板**真的**渲染这段引导（常驻，所有状态可见）；
 *   ② 中英**两种语言**都定义了这两个键，且都非空；
 *   ③ 文案**确实说了**该说的三件事：临时测试世界 / 实例副本、自行备份、启动器不自动备份。
 *
 * 为什么用静态断言而不是跑 DOM：Node 侧是极简假 DOM，没有真实渲染；
 * 这类"文案必须存在且说到点子上"的契约用源码级断言最稳（与 A13 的静态清单同一思路）。
 */
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const repoRoot = path.resolve(__dirname, '..', '..');
const launcherSrc = fs.readFileSync(path.join(repoRoot, 'ui', 'js', 'launcher.js'), 'utf8');
const i18nSrc = fs.readFileSync(path.join(repoRoot, 'ui', 'js', 'i18n.js'), 'utf8');

/** 取出某个键在 i18n.js 里的**全部**取值（zh 与 en 各一次）。 */
function valuesOf(key) {
	const out = [];
	const re = new RegExp("'" + key.replace('.', '\\.') + "'\\s*:\\s*'((?:[^'\\\\]|\\\\.)*)'", 'g');
	let m;
	while ((m = re.exec(i18nSrc)) !== null) {
		out.push(m[1]);
	}
	return out;
}

test('A6-①：二分面板常驻渲染安全引导（不是只在某个分支里）', () => {
	// 引导块必须在 openModBisect 的模态里（标题之后、内容之前），这样"未完成会话/需要恢复"等状态也能看到
	assert.ok(/<div class="bisect-safety" id="bisectSafety"><\/div>/.test(launcherSrc),
		'模态里应有一个常驻的 bisectSafety 容器');

	const fnStart = launcherSrc.indexOf('async function openModBisect');
	const fnEnd = launcherSrc.indexOf('function drawBisect(', fnStart);
	assert.ok(fnStart > 0 && fnEnd > fnStart, '应能定位 openModBisect 函数体');

	const body = launcherSrc.slice(fnStart, fnEnd);
	assert.ok(/getElementById\('bisectSafety'\)/.test(body), 'openModBisect 必须填充 bisectSafety');
	assert.ok(/bisec\.safetyTitle/.test(body) && /bisec\.safety'/.test(body),
		'引导必须使用 bisec.safetyTitle 与 bisec.safety 两个键');
});

test('A6-②：中英两种语言都定义了这两个键，且都非空', () => {
	for (const key of ['bisec.safetyTitle', 'bisec.safety']) {
		const values = valuesOf(key);
		assert.strictEqual(values.length, 2, key + ' 应在 i18n.js 里出现两次（zh + en），实际 ' + values.length);
		for (const v of values) {
			assert.ok(v.trim().length > 0, key + ' 的取值不能为空');
		}
	}
});

test('A6-③：文案确实覆盖了计划要求的三件事（中文侧）', () => {
	const zhTitle = valuesOf('bisec.safetyTitle')[0];
	const zhBody = valuesOf('bisec.safety')[0];
	const zhAll = zhTitle + ' ' + zhBody;

	assert.ok(zhAll.includes('临时测试世界'), '必须提到「临时测试世界」');
	assert.ok(zhAll.includes('实例副本'), '必须提到「实例副本」');
	assert.ok(zhBody.includes('备份'), '必须给出「备份」指引');
	assert.ok(/不会自动备份|不会.*备份/.test(zhBody), '必须明确说明启动器**不会**自动备份（避免用户误以为已备份）');
});

test('A6-③（英文侧）：同样覆盖这三件事', () => {
	const enAll = valuesOf('bisec.safetyTitle')[1] + ' ' + valuesOf('bisec.safety')[1];

	assert.ok(/temporary test world/i.test(enAll), '英文文案必须提到 temporary test world');
	assert.ok(/instance copy|copy of the instance/i.test(enAll), '英文文案必须提到实例副本');
	assert.ok(/back up|backup/i.test(enAll), '英文文案必须给出备份指引');
	assert.ok(/does NOT back up|does not back up/i.test(enAll), '英文文案必须明确说明启动器不会自动备份');
});
