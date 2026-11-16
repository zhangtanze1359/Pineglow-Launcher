#!/usr/bin/env node
/*
 * gates.cjs —— 一条命令跑完「本项目全部门槛」，并按清单报告缺哪条。
 *
 * 存在的理由（2026-11-17 retro）：
 *   每次会话都要手抄四条命令（C# / inventory / css-tokens / closure），
 *   抄漏一条没有任何提示；而且不同分支的门槛集合还不一样
 *   （release/v1.0.4 上 check-css-tokens.cjs 根本不存在，只能靠人记得写"未执行"）。
 *   这里把"门槛集合"变成数据：缺必跑项 → exit 4 并大声报出来；
 *   某条失败 → exit 1；全绿 → exit 0 并打印哪几条真跑过。
 *
 * 用法：
 *   node scripts/gates.cjs                 # 跑全部
 *   node scripts/gates.cjs --only inventory,closure
 *   node scripts/gates.cjs --json          # 机器可读结果（便于贴进证据包）
 *
 * 环境约定（两个真踩过的坑，固化在这里，不靠人记文档）：
 *   1. dotnet 门槛必须把这个进程的 PATH 前置成 Windows 目录
 *      （否则 GNU find.exe 抢占，更新脚本测试假失败 —— AGENTS.md 里那条注释的来历）。
 *   2. 需要传 MSBuild 属性时用 `-p:`，**不要用 `/p:`**：Git Bash/MSYS 会把 `/p:` 当路径改写成
 *      `C:/Program Files/Git/p:`，dotnet 直接报"开关语法错误"。
 */

'use strict';

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');

/** Windows 目录（dotnet 门槛需要把 find.exe 之类的原生工具排在前面）。 */
const WINDOWS_PATH_PREFIX = ['C:\Windows\System32', 'C:\Windows'];

const GATES = [
  {
    id: 'csharp',
    label: 'C# 全量测试',
    cmd: 'dotnet',
    args: ['test', 'dev/PineglowLauncher.Tests', '-c', 'Release'],
    windowsPath: true,
    required: true,
    // 只抽末行汇总，避免把 1 分钟的输出全塞进终端
    summary: /已通过!|失败!|Passed!|Failed!/,
  },
  {
    id: 'inventory',
    label: '前端全量 + 清单双向断言',
    cmd: process.execPath,
    args: [path.join(ROOT, 'scripts', 'run-inventory-tests.cjs')],
    required: true,
    summary: /ℹ (pass|fail|skipped)/,
  },
  {
    id: 'css-tokens',
    label: 'CSS token 审计',
    cmd: process.execPath,
    args: [path.join(ROOT, 'scripts', 'check-css-tokens.cjs')],
    // v1.0.3 及更早的分支没有这个脚本 —— 缺了不算失败，但必须被喊出来
    required: false,
    scriptExists: path.join(ROOT, 'scripts', 'check-css-tokens.cjs'),
    summary: /token-audit|token/i,
  },
  {
    id: 'closure',
    label: 'UI 资源闭包',
    cmd: process.execPath,
    args: [path.join(ROOT, 'scripts', 'check-ui-resource-closure.cjs')],
    required: true,
    summary: /\[closure\]/,
  },
];

function parseArgs(argv) {
  const opts = { only: null, json: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--json') opts.json = true;
    else if (a === '--only') opts.only = (argv[++i] || '').split(',').filter(Boolean);
  }
  return opts;
}

function envFor(gate) {
  const env = { ...process.env };
  if (gate.windowsPath && process.platform === 'win32') {
    const current = env.PATH || env.Path || '';
    env.PATH = WINDOWS_PATH_PREFIX.concat(current.split(path.delimiter)).join(path.delimiter);
  }
  return env;
}

function tailLines(text, pattern, max) {
  return String(text || '')
    .split(/\r?\n/)
    .filter((line) => (pattern ? pattern.test(line) : true))
    .slice(-max)
    .map((line) => line.trim())
    .filter(Boolean);
}

function runGate(gate) {
  if (gate.scriptExists && !fs.existsSync(gate.scriptExists)) {
    return { id: gate.id, label: gate.label, status: 'MISSING', exitCode: null, lines: ['脚本不存在：' + path.relative(ROOT, gate.scriptExists)] };
  }

  const started = Date.now();
  const res = spawnSync(gate.cmd, gate.args, {
    cwd: ROOT,
    env: envFor(gate),
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    timeout: 15 * 60 * 1000,
    windowsHide: true,
  });
  const out = (res.stdout || '') + (res.stderr || '');
  const lines = tailLines(out, gate.summary, 3);
  return {
    id: gate.id,
    label: gate.label,
    status: res.status === 0 ? 'PASS' : 'FAIL',
    exitCode: res.status,
    ms: Date.now() - started,
    lines: lines.length ? lines : tailLines(out, null, 3),
  };
}

function main() {
  const opts = parseArgs(process.argv.slice(2));
  const selected = GATES.filter((g) => !opts.only || opts.only.includes(g.id));
  if (!selected.length) {
    console.error('没有匹配的门槛：' + opts.only.join(','));
    process.exit(2);
  }

  const results = [];
  for (const gate of selected) {
    if (!opts.json) process.stdout.write(`\n▶ ${gate.label} (${gate.id})\n`);
    const r = runGate(gate);
    results.push(r);
    if (!opts.json) {
      for (const line of r.lines) process.stdout.write(`  ${line}\n`);
    }
  }

  const failed = results.filter((r) => r.status === 'FAIL');
  const missingRequired = results.filter((r) => r.status === 'MISSING' && GATES.find((g) => g.id === r.id).required);
  const missingOptional = results.filter((r) => r.status === 'MISSING' && !GATES.find((g) => g.id === r.id).required);

  if (opts.json) {
    console.log(JSON.stringify({ branch: currentBranch(), head: currentHead(), results, failed: failed.map((r) => r.id), missingRequired: missingRequired.map((r) => r.id), missingOptional: missingOptional.map((r) => r.id) }, null, 2));
  } else {
    console.log('\n' + '─'.repeat(60));
    console.log('分支 ' + currentBranch() + ' @ ' + currentHead());
    for (const r of results) {
      const mark = r.status === 'PASS' ? '✔' : r.status === 'MISSING' ? '…' : '✘';
      const timing = r.ms ? ` (${(r.ms / 1000).toFixed(1)}s)` : '';
      console.log(`${mark} ${r.id.padEnd(11)} ${r.status.padEnd(8)}${timing}`);
    }
    if (missingOptional.length) {
      console.log('\n⚠ 本分支缺少可选门槛：' + missingOptional.map((r) => r.id).join('、') + '（这不是绿，是"没跑"）');
    }
  }

  if (failed.length || missingRequired.length) process.exit(1);
  if (missingOptional.length) process.exit(4);
  process.exit(0);
}

function currentBranch() {
  const r = spawnSync('git', ['rev-parse', '--abbrev-ref', 'HEAD'], { cwd: ROOT, encoding: 'utf8', windowsHide: true });
  return (r.stdout || '').trim() || '(unknown)';
}

function currentHead() {
  const r = spawnSync('git', ['rev-parse', '--short', 'HEAD'], { cwd: ROOT, encoding: 'utf8', windowsHide: true });
  return (r.stdout || '').trim() || '(unknown)';
}

main();
