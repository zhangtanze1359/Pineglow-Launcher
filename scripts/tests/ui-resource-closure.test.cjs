'use strict';
/*
 * ui-resource-closure.test.cjs — R02 门槛测试：UI 本地静态资源闭包检查器自身
 * ---------------------------------------------------------------------------
 * 由来（docs/release-readiness-review-2026-10-05.md R02）：
 *   ui/index.html → css/pineglow.css → ../assets/pine-forest.svg 这条链上的两个文件
 *   当时未纳入候选源码，副本与正式包都缺它们，而 Node 全量与构建仍然通过。
 *   因此需要一条独立门槛：缺资源必须非零失败，且必须在中文/空格路径下同样可靠。
 *
 * 用例：
 *   CL-01 真实 ui/ 闭包完整 → exit 0
 *   CL-02 夹具删除 assets/pine-forest.svg → exit 1，且缺失项精确指向 pine-forest.svg（CSS 链被发现）
 *   CL-03 夹具删除 css/pineglow.css → exit 1
 *   CL-04 夹具删除 js/api.js（HTML 直接引用）→ exit 1
 *   CL-05 中文+空格路径下的完整夹具 → exit 0（路径不得影响判定）
 *   CL-06 无关多余文件（未被引用的图片）不影响判定 → exit 0
 *   CL-07 JSON 报告结构与缺失计数
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const CHECKER = path.join(ROOT, 'scripts', 'check-ui-resource-closure.cjs');
const REAL_UI = path.join(ROOT, 'ui');

function runChecker(uiRoot, jsonOut) {
  const args = [CHECKER, '--root', uiRoot, '--quiet'];
  if (jsonOut) args.push('--json', jsonOut);
  const r = spawnSync(process.execPath, args, { encoding: 'utf8' });
  return { code: r.status, stdout: r.stdout || '', stderr: r.stderr || '' };
}

function copyUi(destRoot) {
  fs.mkdirSync(destRoot, { recursive: true });
  for (const rel of ['index.html']) fs.copyFileSync(path.join(REAL_UI, rel), path.join(destRoot, rel));
  for (const dir of ['css', 'js', 'assets', 'vendor']) {
    const src = path.join(REAL_UI, dir);
    if (!fs.existsSync(src)) continue;
    const target = path.join(destRoot, dir);
    fs.mkdirSync(target, { recursive: true });
    // M0-4（2026-11-06）：原实现直接 `fs.cpSync(src, target, {recursive:true})`，
    // 让 cpSync 自己创建目标目录。实测在本机 Node v22.22.2 + Windows 上，
    // 当**祖先目录名含非 ASCII**（CL-05 的 `松光 启动器 资源闭合 测试`）时，
    // cpSync 会用 GBK 解码后的乱码名另建一个目录（如 `鏉惧厜 娴嬭瘯`），
    // 返回值不报错，但目标目录里 0 个文件 —— 于是 CL-05 的"资源闭合完整"断言
    // 必然失败，且失败原因完全掩盖在 `code !== 0` 里。
    // 这是测试夹具的缺陷，不是被检查对象（check-ui-resource-closure.cjs）的缺陷：
    // 检查器单独在中文路径下 exit=0。改为先显式建目录再逐文件复制，绕开该行为。
    copyDirRecursive(src, target);
  }
  return destRoot;
}

/** 逐文件递归复制（不用 fs.cpSync，避开其对非 ASCII 祖先路径的编码问题）。 */
function copyDirRecursive(src, dest) {
  fs.mkdirSync(dest, { recursive: true });
  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    const from = path.join(src, entry.name);
    const to = path.join(dest, entry.name);
    if (entry.isDirectory()) copyDirRecursive(from, to);
    else if (entry.isFile()) fs.copyFileSync(from, to);
  }
}

function tmpDir(label) {
  return fs.mkdtempSync(path.join(os.tmpdir(), `pineglow-closure-${label}-`));
}

test('CL-01 真实 ui/ 资源闭包完整', () => {
  const jsonPath = path.join(tmpDir('real'), 'closure.json');
  const r = runChecker(REAL_UI, jsonPath);
  assert.equal(r.code, 0, `真实 ui/ 应为 GREEN，stdout=${r.stdout} stderr=${r.stderr}`);
  const report = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
  assert.equal(report.missingCount, 0);
  assert.ok(report.totalLocalReferences >= 8, `应发现 HTML 的 8 条本地引用，实际 ${report.totalLocalReferences}`);
  const resolved = new Set();
  assert.ok(report.scannedFiles >= 9, `应递归扫描 CSS/JS，实际 ${report.scannedFiles}`);
  return resolved;
});

test('CL-02 删除 assets/pine-forest.svg（CSS 间接引用）必须 RED 且精确定位', () => {
  const dir = copyUi(tmpDir('red-svg'));
  const target = path.join(dir, 'assets', 'pine-forest.svg');
  assert.ok(fs.existsSync(target), '夹具先决条件：pine-forest.svg 必须存在于 ui/');
  fs.rmSync(target);
  const jsonPath = path.join(tmpDir('red-svg-out'), 'closure.json');
  const r = runChecker(dir, jsonPath);
  assert.equal(r.code, 1, '缺 CSS 间接资源必须非零失败');
  const report = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
  assert.equal(report.missingCount, 1);
  assert.equal(report.missing[0].resolved, 'assets/pine-forest.svg');
  assert.equal(report.missing[0].from, 'css/pineglow.css');
});

test('CL-03 删除 css/pineglow.css（HTML 直接引用）必须 RED', () => {
  const dir = copyUi(tmpDir('red-css'));
  fs.rmSync(path.join(dir, 'css', 'pineglow.css'));
  const r = runChecker(dir);
  assert.equal(r.code, 1);
});

test('CL-04 删除 js/api.js 必须 RED', () => {
  const dir = copyUi(tmpDir('red-js'));
  fs.rmSync(path.join(dir, 'js', 'api.js'));
  const r = runChecker(dir);
  assert.equal(r.code, 1);
});

test('CL-05 中文+空格路径下的完整夹具仍为 GREEN', () => {
  const base = tmpDir('unicode');
  const dir = path.join(base, '松光 启动器 资源闭合 测试');
  copyUi(dir);
  const r = runChecker(dir);
  assert.equal(r.code, 0, `中文/空格路径不应影响判定，stdout=${r.stdout}`);
});

test('CL-06 未被引用的多余文件不影响判定', () => {
  const dir = copyUi(tmpDir('extra'));
  fs.writeFileSync(path.join(dir, 'assets', 'unused-extra.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47]));
  const r = runChecker(dir);
  assert.equal(r.code, 0);
});

test('CL-07 JSON 报告含入口、扫描数与缺失列表', () => {
  const dir = copyUi(tmpDir('json'));
  fs.rmSync(path.join(dir, 'assets', 'pine-forest.svg'));
  const jsonPath = path.join(tmpDir('json-out'), 'closure.json');
  const r = runChecker(dir, jsonPath);
  assert.equal(r.code, 1);
  const report = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
  assert.deepEqual(report.entryPoints, ['index.html']);
  assert.ok(Array.isArray(report.missing) && report.missing.length === 1);
  assert.equal(typeof report.generatedAt, 'string');
});
