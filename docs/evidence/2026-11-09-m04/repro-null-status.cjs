'use strict';
/*
 * repro-null-status.cjs —— M0-4 失败机理最小复现（不依赖 Linux 环境）
 *
 * 断言两件事：
 *   1) spawnSync 指向「不存在的可执行文件」时，proc.status === null 且 proc.error.code === 'ENOENT'
 *      —— 这正是 M0-4 记录里「进程未启动产生 null 退出状态」的成因；
 *   2) 修复前的写法在非 Windows 上必然落到某个不存在的名字：
 *      旧代码 `process.env.DSH_PWSH || 'powershell.exe'` 在 Linux 上解析为 powershell.exe，
 *      而 ubuntu runner 只提供 PowerShell 7（pwsh），于是命中 (1)；
 *      新写法按 process.platform 选择，Windows 分支不变。
 *
 * 本脚本只读：不改产品代码、不写证据包、不访问网络。
 */
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const path = require('node:path');

const MISSING = path.join(__dirname, 'definitely-not-a-real-interpreter-xyz');

// —— 1) 失败机理：进程根本没起来 ——
const failed = spawnSync(MISSING, ['-NoProfile', '-Command', 'exit 0'], { encoding: 'utf8', timeout: 5000 });
console.log('[1] missing interpreter -> status       =', failed.status);
console.log('[1] missing interpreter -> error.code   =', failed.error && failed.error.code);
assert.equal(failed.status, null, '进程未启动时 status 必须为 null（这正是旧记录里的现象）');
assert.equal(failed.error.code, 'ENOENT', '进程未启动时应为 ENOENT');

// —— 2) 旧写法 vs 新写法在非 Windows 上的解析结果 ——
function resolveOld(platform) {
	// 修复前：scripts/tests/goal-validation.test.cjs（commit 91d71ac 之前）
	return process.env.DSH_PWSH || 'powershell.exe';
}
function resolveNew(platform) {
	// 修复后：按 process.platform 选择
	return process.env.DSH_PWSH || (platform === 'win32' ? 'powershell.exe' : 'pwsh');
}

const oldOnLinux = resolveOld('linux');
const newOnLinux = resolveNew('linux');
const newOnWin = resolveNew('win32');
console.log('[2] old code on linux  ->', oldOnLinux);
console.log('[2] new code on linux  ->', newOnLinux);
console.log('[2] new code on win32  ->', newOnWin);
assert.equal(oldOnLinux, 'powershell.exe', '旧写法在 Linux 上仍解析为 powershell.exe（ubuntu runner 无此文件）');
assert.equal(newOnLinux, 'pwsh', '新写法在 Linux 上解析为 pwsh（PowerShell 7）');
assert.equal(newOnWin, 'powershell.exe', 'Windows 行为保持不变（PowerShell 5.1）');

// —— 3) 修复后对 spawn 失败改为显式抛错，不再把 null 当退出码吞掉 ——
function runRunnerStyle(PWSH) {
	const proc = spawnSync(PWSH, ['-NoProfile', '-Command', 'exit 0'], { encoding: 'utf8', timeout: 5000 });
	if (proc.error) throw new Error(`Cannot execute ${PWSH}: ${proc.error.message}`, { cause: proc.error });
	return { code: proc.status };
}
let threw = false;
try {
	runRunnerStyle(MISSING);
} catch (err) {
	threw = true;
	console.log('[3] spawn 失败已显式抛错 ->', err.message);
}
assert.ok(threw, '修复后必须显式抛错，而不是返回 code=null 让上层误判');

console.log('RESULT=PASS');
