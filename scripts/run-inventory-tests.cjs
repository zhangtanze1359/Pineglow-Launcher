#!/usr/bin/env node
/*
 * run-inventory-tests.cjs —— 正式测试清单的**执行与守卫**（发版阻断项 R04）
 *
 * 由来（docs/release-readiness-review-2026-10-05.md R04）：
 *   build.yml 的 Node 步骤硬编码了 11 个 *.test.cjs，而候选源码里实际有 21 个；
 *   漏掉的 10 个（a11y-keyboard / bisect-recovery / update-recovery …）在 CI 里从未执行。
 *   固定清单必然随源码漂移，所以把清单外置成 scripts/test-inventory.txt，
 *   并在这里做双向一致性断言：
 *     - 清单里写的文件必须存在（缺失 = 非零失败，不允许「悄悄少跑」）；
 *     - 磁盘上实际存在的 *.test.cjs 必须都在清单里（新增测试忘了登记 = 非零失败）；
 *     - 清单文件、C# 测试项目路径也必须存在。
 *
 * 用法：
 *   node scripts/run-inventory-tests.cjs            # 先校验清单，再运行全部 node 测试
 *   node scripts/run-inventory-tests.cjs --check    # 只校验清单（CI 的独立门槛步骤用）
 *   node scripts/run-inventory-tests.cjs --list     # 打印 node 测试文件（每行一个）
 *   node scripts/run-inventory-tests.cjs --json <path>  # 写清单校验报告
 *
 * 退出码：0 = 通过；1 = 清单漂移或测试失败；2 = 用法/环境错误。
 */

'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const INVENTORY = path.join(ROOT, 'scripts', 'test-inventory.txt');

// 发现规则：必须是 *.test.cjs；下划线开头的临时/调试脚本不算（例如 _fake-test.cjs 不匹配 .test.cjs）
const DISCOVERY_DIRS = [path.join(ROOT, 'ui', 'tests'), path.join(ROOT, 'scripts', 'tests')];

function parseArgs(argv) {
  const out = { check: false, list: false, json: null };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--check') out.check = true;
    else if (a === '--list') out.list = true;
    else if (a === '--json') out.json = argv[++i];
    else if (a === '--help' || a === '-h') out.help = true;
    else throw new Error(`未知参数: ${a}`);
  }
  return out;
}

function readInventory() {
  if (!fs.existsSync(INVENTORY)) throw new Error(`找不到测试清单: ${INVENTORY}`);
  const entries = [];
  const text = fs.readFileSync(INVENTORY, 'utf8');
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (line === '' || line.startsWith('#')) continue;
    const m = /^(node|project)\s+(\S+)$/.exec(line);
    if (!m) throw new Error(`清单行格式非法（应为 "<node|project> <path>"）: ${line}`);
    entries.push({ runner: m[1], rel: m[2].replace(/\\/g, '/') });
  }
  return entries;
}

function discoverNodeTests() {
  const found = [];
  for (const dir of DISCOVERY_DIRS) {
    if (!fs.existsSync(dir)) continue;
    for (const name of fs.readdirSync(dir)) {
      if (!name.endsWith('.test.cjs')) continue;
      found.push(path.relative(ROOT, path.join(dir, name)).replace(/\\/g, '/'));
    }
  }
  return found.sort();
}

function main() {
  let args;
  try { args = parseArgs(process.argv.slice(2)); }
  catch (e) { console.error(String(e.message || e)); process.exit(2); }
  if (args.help) { console.log('用法: node scripts/run-inventory-tests.cjs [--check] [--list] [--json out.json]'); return; }

  let entries;
  try { entries = readInventory(); }
  catch (e) { console.error(`[inventory] ${e.message}`); process.exit(2); }

  const problems = [];
  const nodeEntries = entries.filter(e => e.runner === 'node');
  const projectEntries = entries.filter(e => e.runner === 'project');

  // 1. 清单项必须存在
  const missing = [];
  for (const e of entries) {
    if (!fs.existsSync(path.join(ROOT, e.rel))) { missing.push(e.rel); }
  }
  if (missing.length > 0) {
    problems.push(`清单里 ${missing.length} 个文件不存在（清单与源码漂移）: ${missing.join(', ')}`);
  }

  // 2. 磁盘上的 node 测试必须都在清单里
  const discovered = discoverNodeTests();
  const listed = new Set(nodeEntries.map(e => e.rel));
  const unlisted = discovered.filter(f => !listed.has(f));
  if (unlisted.length > 0) {
    problems.push(`磁盘上有 ${unlisted.length} 个未登记的 *.test.cjs（新增测试必须登记，否则 CI 不会跑）: ${unlisted.join(', ')}`);
  }

  // 3. 不能一个测试都没有
  if (nodeEntries.length === 0) problems.push('清单里没有任何 node 测试文件');
  if (projectEntries.length === 0) problems.push('清单里没有 C# 测试项目（project 行）');

  const report = {
    generatedAt: new Date().toISOString(),
    inventoryFile: INVENTORY,
    nodeCount: nodeEntries.length,
    projectCount: projectEntries.length,
    discoveredNodeTests: discovered.length,
    nodeFiles: nodeEntries.map(e => e.rel),
    missing,
    unlisted,
    problems,
    verdict: problems.length === 0 ? 'PASS' : 'FAIL'
  };

  if (args.json) {
    fs.mkdirSync(path.dirname(path.resolve(args.json)), { recursive: true });
    fs.writeFileSync(path.resolve(args.json), JSON.stringify(report, null, 2), 'utf8');
  }

  if (args.list) {
    for (const e of nodeEntries) console.log(e.rel);
    process.exit(problems.length === 0 ? 0 : 1);
  }

  if (problems.length > 0) {
    console.error(`[inventory] 清单校验失败（node=${nodeEntries.length} 项目=${projectEntries.length} 磁盘发现=${discovered.length}）`);
    for (const p of problems) console.error(`  - ${p}`);
    process.exit(1);
  }
  console.log(`[inventory] 清单校验通过：node 测试 ${nodeEntries.length} 个，C# 项目 ${projectEntries.length} 个，磁盘发现一致 ${discovered.length} 个`);

  if (args.check) return;

  // 运行 node 测试（继承 stdio，输出直接进调用方日志）
  const files = nodeEntries.map(e => e.rel);
  const res = spawnSync(process.execPath, ['--test', '--test-reporter=spec', ...files], { cwd: ROOT, stdio: 'inherit' });
  if (res.error) { console.error(`[inventory] 启动 node --test 失败: ${res.error.message}`); process.exit(2); }
  process.exit(res.status === null ? 1 : res.status);
}

main();
