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
 *   node scripts/gates.cjs --baseline      # 跑完写 docs/baseline.md
 *   node scripts/gates.cjs --require-fresh-baseline   # 基线过期直接失败（CI 可用）
 *
 * 2026-11-17 两项补强（都是这一轮真实踩到的）：
 *   A. 失败测试名。原来 summary 只抽 `失败!` 汇总行，输了测试名那一行被 tail -3 挤掉，
 *      拿到的是「失败 1 条」但不知道是哪条 —— 验收人要自己造 trx 文件才能知道。
 *      现在每条失败用例单独抽出来打在你的终端上。
 *   B. 基线过期检测。docs/baseline.md 记的 commit 与实际 HEAD 不一致时，它会**主动喊出来**。
 *      原来全仓库没有任何东西读它：AGENTS.md 说「数字对不上先怀疑自己跑错目录」，
 *      而基线自己已经过期了两轮（0a37256 → acc9498），这条规则反而把人引向错误结论。
 *
 *      这里必须区分两种情况，否则会造一个新坑：
 *        - 基线提交之后**代码/门槛没动**（只改了 docs）→ 数字仍然有效，判 fresh-doc-only，不报警。
 *          典型的例子就是「刚用 --baseline 生成、然后把它提交」：提交后 HEAD 前进了
 *          一个只改 docs 的提交，数字完全没变，这时候喊过期是噪声。
 *        - 基线提交之后**改过会影响门槛的文件** → 数字失效，判 stale，大声报。
 *      「影响门槛的文件」= dev/ · ui/ · scripts/ · release-notes/ · 根 csproj/global.json。
 *      只编译注释、只改 docs 不改代码，不该触发报警。
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
    // 失败用例名（xUnit 两种格式都出现，见下行注释）
    failures: [
      /^\s*失败\s+\S+/,        //  失败 Pineglow.Launcher.Tests.XxxTests.Case [12 ms]
      /\[FAIL\]\s*$/,          //  [xUnit.net 00:00:00.51]  Pineglow...Case [FAIL]
      /^\s*Failed\s+\S+/,      // 英文 locale
    ],
    // --baseline：用来写 docs/baseline.md 的数字
    count: /通过:\s*(\d+)，已跳过:\s*(\d+)/,
  },
  {
    id: 'inventory',
    label: '前端全量 + 清单双向断言',
    cmd: process.execPath,
    args: [path.join(ROOT, 'scripts', 'run-inventory-tests.cjs')],
    required: true,
    summary: /ℹ (pass|fail|skipped)/,
    count: /ℹ pass\s*(\d+)/,
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
    count: /(\d+)\s*个 token/,
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
  const opts = { only: null, json: false, baseline: false, requireFreshBaseline: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--json') opts.json = true;
    else if (a === '--baseline') opts.baseline = true;
    else if (a === '--require-fresh-baseline') opts.requireFreshBaseline = true;
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
  const failures = collectFailures(out, gate.failures);
  const countMatch = gate.count ? out.match(gate.count) : null;
  return {
    id: gate.id,
    label: gate.label,
    status: res.status === 0 ? 'PASS' : 'FAIL',
    exitCode: res.status,
    ms: Date.now() - started,
    count: countMatch ? Number(countMatch[1]) : null,
    skipped: countMatch && countMatch[2] !== undefined ? Number(countMatch[2]) : null,
    failures,
    lines: lines.length ? lines : tailLines(out, null, 3),
  };
}

/**
 * 抽出失败的用例名。失败时你要的是「哪条」，不是「几条」。
 * 同一用例可能在 xUnit 输出里出现两次（明细行 + [FAIL] 汇总行），这里按名字去重。
 */
function collectFailures(text, patterns) {
  if (!patterns || !patterns.length) return [];
  const seen = new Set();
  const out = [];
  for (const raw of String(text || '').split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    if (!patterns.some((p) => p.test(line))) continue;
    const name = normalizeFailureName(line);
    if (!name || seen.has(name)) continue;
    seen.add(name);
    out.push(name);
  }
  return out;
}

/** 把两种格式剥成同一个名字，并去掉 xUnit 前缀与耗时尾巴。 */
function normalizeFailureName(line) {
  let s = line
    .replace(/^\[xUnit\.net[^\]]*\]\s*/, '')
    .replace(/^失败\s+/, '')
    .replace(/^Failed\s+/, '')
    .replace(/\s*\[(FAIL|\d+\s*ms)\]\s*$/g, '')
    .trim();
  return s;
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

  const head = currentHead();
  const baseline = inspectBaseline(head);

  if (opts.json) {
    console.log(JSON.stringify({ branch: currentBranch(), head, baseline, results, failed: failed.map((r) => r.id), missingRequired: missingRequired.map((r) => r.id), missingOptional: missingOptional.map((r) => r.id) }, null, 2));
  } else {
    console.log('\n' + '─'.repeat(60));
    console.log('分支 ' + currentBranch() + ' @ ' + head);
    for (const r of results) {
      const mark = r.status === 'PASS' ? '✔' : r.status === 'MISSING' ? '…' : '✘';
      const timing = r.ms ? ` (${(r.ms / 1000).toFixed(1)}s)` : '';
      console.log(`${mark} ${r.id.padEnd(11)} ${r.status.padEnd(8)}${timing}`);
    }

    const failedWithNames = results.filter((r) => r.status === 'FAIL' && r.failures.length);
    if (failedWithNames.length) {
      console.log('\n✘ 失败的用例（不用再去翻 trx）：');
      for (const r of failedWithNames) {
        for (const name of r.failures) console.log(`  [${r.id}] ${name}`);
      }
    }

    if (missingOptional.length) {
      console.log('\n⚠ 本分支缺少可选门槛：' + missingOptional.map((r) => r.id).join('、') + '（这不是绿，是"没跑"）');
    }

    if (baseline.status === 'stale') {
      const moved = baseline.touched && baseline.touched.length
        ? `（基线之后动过硬门槛的路径：${baseline.touched.slice(0, 5).join('、')}${baseline.touched.length > 5 ? ' 等 ' + baseline.touched.length + ' 个' : ''}）`
        : '（无法比对增量，按保守处理）';
      console.log(
        '\n⚠ docs/baseline.md 已过期：它记的是 ' + baseline.commit + '，当前 HEAD 是 ' + head + '。' + moved + '\n' +
        '  里面的 ' + (baseline.summary || '数字') + ' 不是本分支的数字，不要拿它当判据。\n' +
        '  要刷新：node scripts/gates.cjs --baseline（必须四道门全绿时才刷）'
      );
    } else if (baseline.status === 'fresh-doc-only') {
      // 故意不报警：只在 json 里留痕。基线之后只动了 docs，数字没变。
      console.log('\nℹ 基线来自 ' + baseline.commit + '，此后只改过文档，数字仍然成立。');
    } else if (baseline.status === 'missing') {
      console.log('\n⚠ 找不到 docs/baseline.md：没有任何基线数字可用。');
    } else if (baseline.status === 'not-a-baseline') {
      console.log('\n⚠ docs/baseline.md 里没读到 commit（文件可能被手改过）：' + baseline.detail);
    }
  }

  if (opts.baseline) {
    const file = writeBaseline(results, failed, missingRequired);
    console.log('\n基线已写入 ' + path.relative(ROOT, file));
  }

  if (failed.length || missingRequired.length) process.exit(1);
  if (opts.requireFreshBaseline && baseline.status !== 'fresh' && baseline.status !== 'fresh-doc-only') {
    console.error('\n✘ --require-fresh-baseline：基线状态 ' + baseline.status + '（需要 fresh）。');
    process.exit(3);
  }
  if (missingOptional.length) process.exit(4);
  process.exit(0);
}

/**
 * 写 docs/baseline.md —— 门槛数字的**唯一真值**。
 *
 * 由来（2026-11-17 retro 第 2 条）：同一批数字（1380 / 1394 / 1410 / 1418）曾散在 4 个文档里，
 * AGENTS.md 记的还是三个版本之前的 1380，实际早就是 1400+。数字一旦分家，推理就会建在旧数字上。
 * 现在文档只留一行指针，数字由这条命令生成。
 */
function writeBaseline(results, failed, missingRequired) {
  const dir = path.join(ROOT, 'docs');
  if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, 'baseline.md');
  const byId = (id) => results.find((r) => r.id === id);
  const csharp = byId('csharp');
  const front = byId('inventory');
  const css = byId('css-tokens');
  const lines = [
    '# 基线（自动生成，别手改）',
    '',
    '<!-- 由 node scripts/gates.cjs --baseline 生成；手改会在下次生成时被覆盖。 -->',
    '',
    '- 分支：`' + currentBranch() + '` @ `' + currentHead() + '`',
    '- 生成时间：' + new Date().toISOString(),
    '- 命令：`node scripts/gates.cjs --baseline`',
    '',
    '| 门槛 | 结果 |',
    '| --- | --- |',
    '| C# 全量测试 | ' + describe(csharp, ' 条通过 / 跳过 ' + (csharp && csharp.skipped !== null ? csharp.skipped : '?') + ' 条') + ' |',
    '| 前端全量 + 清单 | ' + describe(front, ' 条通过') + ' |',
    '| CSS token | ' + describe(css, ' 个 token') + ' |',
    '| UI 资源闭包 | ' + describe(byId('closure'), '') + ' |',
    '',
    failed.length || missingRequired.length
      ? '**本分支这次不是全绿**：失败 ' + (failed.map((r) => r.id).join('、') || '无') + '；缺必跑项 ' +
        (missingRequired.map((r) => r.id).join('、') || '无') + '。上面任何数字都不能当基线用。'
      : '全部门槛通过；上面就是本分支当前的验收数字。',
    '',
  ];
  fs.writeFileSync(file, lines.join('\n'), 'utf8');
  return file;
}

/**
 * 读 docs/baseline.md，判断它是否还是当前 HEAD 的数字。
 *
 * 为什么要有这个：基线是「门槛数字的唯一真值」，但它由 --baseline 手写、没有任何东西读。
 * 实测（2026-11-17）：文件里记着 0a37256 的 1432 条，而 main 已经跑到 acc9498 的 1445 条。
 * AGENTS.md 又要求「数字对不上先怀疑自己跑错目录」——基线与事实分叉时，这条规则会把
 * 人引向「环境有问题」的错误结论，而真因是基线本身过期了。
 *
 * 四种结果：
 *   fresh           —— HEAD 就是基线记的那个提交
 *   fresh-doc-only  —— HEAD 前进了，但增量只碰 docs/：数字仍然成立，不报警
 *   stale           —— 增量碰到了会影响门槛的路径：数字失效，必须重跑
 *   missing / not-a-baseline —— 文件不在或读不懂
 */
function inspectBaseline(head) {
  const file = path.join(ROOT, 'docs', 'baseline.md');
  if (!fs.existsSync(file)) return { status: 'missing', file };
  let text;
  try {
    text = fs.readFileSync(file, 'utf8');
  } catch (e) {
    return { status: 'not-a-baseline', file, detail: String(e.message || e) };
  }
  const commit = parseBaselineHead(text);
  if (!commit) return { status: 'not-a-baseline', file, detail: '没有匹配到 "- 分支：`...` @ `<sha>`" 这一行' };
  const csharp = /\|\s*C# 全量测试\s*\|\s*([^|]+)\|/.exec(text);
  const summary = csharp ? csharp[1].trim() : '';
  if (commit === head) return { status: 'fresh', file, commit, summary, head };

  const touched = baselineSensitiveDiff(commit, head);
  if (touched && touched.length === 0) {
    return { status: 'fresh-doc-only', file, commit, summary, head };
  }
  return { status: 'stale', file, commit, summary, head, touched: touched || null };
}

/**
 * 基线提交 → HEAD 之间，动了哪些**会影响门槛数字**的路径。
 * 返回空数组 = 只改了 docs 之类的东西，数字仍然成立。
 * 返回 null = 没法判断（git 不可用 / 基线提交不在历史里），按保守当 stale 处理。
 */
function baselineSensitiveDiff(baseCommit, head) {
  const r = spawnSync('git', ['diff', '--name-only', `${baseCommit}..${head}`], {
    cwd: ROOT,
    encoding: 'utf8',
    windowsHide: true,
  });
  if (r.error || r.status !== 0) return null;
  const files = String(r.stdout || '').split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
  return files.filter(isGateSensitive);
}

/** 改到这些路径会改变门槛数字；只改 docs/ 不会。 */
function isGateSensitive(relPath) {
  const p = String(relPath).replace(/\\/g, '/');
  if (p.startsWith('docs/')) return false;
  if (p === 'README.md' || p === 'LICENSE' || p === 'AGENTS.md') return false;
  if (p.startsWith('.github/')) return false; // CI 配置不影响本地门槛数字
  return (
    p.startsWith('dev/') ||
    p.startsWith('ui/') ||
    p.startsWith('scripts/') ||
    p.startsWith('release-notes/') ||
    p === 'global.json' ||
    p === 'Directory.Build.props' ||
    p === 'build.cmd'
  );
}

/** 从 baseline.md 正文抽出记录的 commit（短哈希）；抽不到返回 null。 */
function parseBaselineHead(text) {
  const m = /^- 分支：`[^`]*` @ `([^`]+)`/m.exec(String(text || ''));
  return m ? m[1].trim() : null;
}

function describe(result, unit) {
  if (!result) return '未跑';
  if (result.status === 'MISSING') return '本分支没有这条门槛（未跑）';
  if (result.status === 'FAIL') return '失败（exit ' + result.exitCode + '）';
  return (result.count === null ? '通过' : result.count + unit) + '';
}

/** git 不在 PATH 时（如从别的进程里调用 node）不要假装知道分支，老实报出来。 */
function gitOutput(args) {
  const r = spawnSync('git', args, { cwd: ROOT, encoding: 'utf8', windowsHide: true });
  if (r.error || r.status !== 0) return '(git 不可用：' + (r.error ? r.error.code : 'exit ' + r.status) + ')';
  return (r.stdout || '').trim() || '(unknown)';
}

function currentBranch() {
  return gitOutput(['rev-parse', '--abbrev-ref', 'HEAD']);
}

function currentHead() {
  return gitOutput(['rev-parse', '--short', 'HEAD']);
}

module.exports = {
  parseArgs,
  tailLines,
  collectFailures,
  normalizeFailureName,
  parseBaselineHead,
  inspectBaseline,
  isGateSensitive,
  GATES,
};

if (require.main === module) main();
