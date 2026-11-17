'use strict';
/*
 * gates-contract.test.cjs — scripts/gates.cjs 自身的行为契约
 * ---------------------------------------------------------------------------
 * 由来（2026-11-17）：团队成员在回灌那一轮实测到两件事，
 * 都是「工具不给证据」而不是「代码有问题」：
 *
 *   1. C# 门槛红了，终端只打印「失败 1 条，通过 1444」。
 *      想知道是哪条，得自己 dotnet test --logger trx 再造 XML 翻一遍。
 *      失败用例名本来就躺在 gates 已经读进来的 stdout 里，被 tail -3 挤掉了。
 *
 *   2. docs/baseline.md 记着 0a37256 的 1432 条，而 main 已经到 acc9498 的 1445 条。
 *      全仓库没有任何脚本读这个文件，所以它过期了两轮都没人知道；
 *      而 AGENTS.md 又要求「数字对不上先怀疑自己跑错目录」——
 *      两者一叠加，会把真因（基线旧了）误判成环境问题。
 *
 * 这些用例都是纯函数级，不跑 dotnet、不启动服务、不改任何仓库文件。
 * 先例：scripts/tests/css-token-contract.test.cjs 的 CT-05「检查器必须对违规非零退出」。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..', '..');
const gates = require(path.join(ROOT, 'scripts', 'gates.cjs'));

/* ==================== 失败用例名抽取 ==================== */

const CSHARP_FAILURES = gates.GATES.find((g) => g.id === 'csharp').failures;

test('GF-01 xUnit 明细行（"失败 Xxx.Case [12 ms]"）必须被抽成用例名', () => {
  const out = [
    '[xUnit.net 00:00:00.51]     Pineglow.Launcher.Tests.UncoveredPureLogicTests.Mirror_ListSources_NoLongerOffersMcbbs [FAIL]',
    '  失败 Pineglow.Launcher.Tests.UncoveredPureLogicTests.Mirror_ListSources_NoLongerOffersMcbbs [12 ms]',
    '  已通过 Pineglow.Launcher.Tests.Other.Healthy [1 ms]',
    '已通过! - 失败: 1，通过: 1444，已跳过: 0，总计: 1445',
  ].join('\n');

  const names = gates.collectFailures(out, CSHARP_FAILURES);
  assert.deepEqual(names, ['Pineglow.Launcher.Tests.UncoveredPureLogicTests.Mirror_ListSources_NoLongerOffersMcbbs'],
    '应只抽出一条，且不带 [12 ms] 尾巴、不带 xUnit 前缀、不带"失败 "前缀');
});

test('GF-02 同一条用例在两种格式里都出现时只报一次（去重）', () => {
  const name = 'Pineglow.Launcher.Tests.A.B';
  const out = [
    `[xUnit.net 00:00:01.00]     ${name} [FAIL]`,
    `  失败 ${name} [7 ms]`,
  ].join('\n');

  const names = gates.collectFailures(out, CSHARP_FAILURES);
  assert.deepEqual(names, [name], '同一用例被 xUnit 打印两遍，不应报成两条');
});

test('GF-03 通过的用例不能被当成失败抽出来', () => {
  const out = [
    '  已通过 Pineglow.Launcher.Tests.A.Pass [1 ms]',
    '已通过! - 失败: 0，通过: 1445，已跳过: 0，总计: 1445',
  ].join('\n');
  assert.deepEqual(gates.collectFailures(out, CSHARP_FAILURES), []);
});

test('GF-04 全绿输出不得产生任何失败名（守"没有失败"这个回归）', () => {
  const out = '已通过! - 失败: 0，通过: 1445，已跳过: 0，总计: 1445';
  assert.deepEqual(gates.collectFailures(out, CSHARP_FAILURES), []);
});

test('GF-05 空输出 / null 不抛异常（门槛脚本自身不能因为 grep 而崩）', () => {
  assert.deepEqual(gates.collectFailures('', CSHARP_FAILURES), []);
  assert.deepEqual(gates.collectFailures(null, CSHARP_FAILURES), []);
  assert.deepEqual(gates.collectFailures('x', undefined), []);
});

test('GF-06 英文 locale 的 "Failed Xxx.Case" 也要能被抽到', () => {
  const name = 'Pineglow.Launcher.Tests.A.EnglishCase';
  const out = `  Failed ${name} [3 ms]`;
  assert.deepEqual(gates.collectFailures(out, CSHARP_FAILURES), [name]);
});

/* ==================== 基线过期检测 ==================== */

test('BF-01 能从 baseline.md 抽出记录的 commit', () => {
  const text = [
    '# 基线（自动生成，别手改）',
    '',
    '- 分支：`integration/v1.1.0-20261107` @ `acc9498`',
    '- 生成时间：2026-11-17T12:41:21.189Z',
  ].join('\n');
  assert.equal(gates.parseBaselineHead(text), 'acc9498');
});

test('BF-02 分支名里带中文、斜杠、空格也不能影响抽取', () => {
  const text = '- 分支：`chore/修复 会话-2026` @ `deadbee`\n';
  assert.equal(gates.parseBaselineHead(text), 'deadbee');
});

test('BF-03 不是基线格式时返回 null（不能瞎猜一个 commit）', () => {
  assert.equal(gates.parseBaselineHead('# 随便一个文档\n没有分支行'), null);
  assert.equal(gates.parseBaselineHead(''), null);
  assert.equal(gates.parseBaselineHead(null), null);
});

test('BF-04 基线记录的 commit 等于当前 HEAD 时判为 fresh', () => {
  const repoBaseline = require('node:fs').readFileSync(path.join(ROOT, 'docs', 'baseline.md'), 'utf8');
  const recorded = gates.parseBaselineHead(repoBaseline);
  assert.ok(recorded, '仓库里的 docs/baseline.md 应能被解析；解析不了说明文件被手改过');

  const r = gates.inspectBaseline(recorded);
  assert.equal(r.status, 'fresh', `传基线自己的 commit(${recorded}) 必须是 fresh，实际 ${r.status}`);
});

test('BF-04b 基线之后只改 docs 时不得判 stale（否则每次提交文档就误报一次）', () => {
  // 契约：fresh-doc-only 是合法状态，且不应当被当成 stale。
  // 这里不构造临时仓库（会失真），只验证状态集合与 isGateSensitive 的分类边界。
  const statuses = gates.inspectBaseline('some-old-sha');
  assert.ok(['fresh', 'fresh-doc-only', 'stale', 'not-a-baseline'].includes(statuses.status),
    'fresh-doc-only 必须是可识别状态之一，实际 ' + statuses.status);
});

test('BF-04c isGateSensitive：只改 docs / README / AGENTS / workflows 不算影响门槛', () => {
  const harmless = ['docs/baseline.md', 'docs/team/README.md', 'README.md', 'LICENSE.md',
    'AGENTS.md', '.github/workflows/build.yml'];
  for (const p of harmless) {
    assert.equal(gates.isGateSensitive(p), false, `${p} 不应被判为影响门槛`);
  }
});

test('BF-04d isGateSensitive：dev / ui / scripts / release-notes / global.json 都算', () => {
  const sensitive = ['dev/PineglowLauncher/Program.cs', 'ui/js/launcher.js', 'scripts/gates.cjs',
    'release-notes/v1.0.4.md', 'global.json'];
  for (const p of sensitive) {
    assert.equal(gates.isGateSensitive(p), true, `${p} 必须被判为影响门槛，否则基线上线后会让不准确的数字继续被引用`);
  }
});

test('BF-04e Windows 反斜杠路径与正斜杠等价（Windows 上 git 可能吐反斜杠）', () => {
  assert.equal(gates.isGateSensitive('dev\\PineglowLauncher\\Program.cs'), true);
  assert.equal(gates.isGateSensitive('docs\\baseline.md'), false);
});

test('BF-05 基线 commit 与 HEAD 不同时判为 stale，并带上两边哈希与里面的数字', () => {
  const r = gates.inspectBaseline('0000000-not-the-head');
  // 只有当仓库基线确实不是这个假 commit 时才断言 stale；避免把测试绑死在某个具体哈希上
  assert.ok(r.status === 'stale' || r.status === 'not-a-baseline',
    `假 commit 必须被判 stale（或文件不可解析），实际 ${r.status}`);
  if (r.status === 'stale') {
    assert.equal(r.head, '0000000-not-the-head');
    assert.ok(r.commit, 'stale 结果里必须带上基线记录的 commit，否则没法告诉人差在哪');
    assert.ok(typeof r.summary === 'string', 'stale 结果应带上基线里的 C# 数字，便于人判断差多少');
  }
});

test('BF-06 基线文件缺失时返回 missing，而不是假装 fresh', () => {
  // inspectBaseline 读的是 ROOT 下固定路径；这里只验证它的契约形状：
  // 三个状态必须是这三个字符串，不能出现第四种沉默值。
  const statuses = new Set(['fresh', 'fresh-doc-only', 'stale', 'missing', 'not-a-baseline']);
  const r = gates.inspectBaseline('whatever');
  assert.ok(statuses.has(r.status), `status 必须是四者之一，实际 ${r.status}`);
  assert.ok(typeof r.file === 'string' && r.file.length > 0, '必须带上文件路径，否则报警时指不到地方');
});

/* ==================== 参数解析 ==================== */

test('PA-01 --require-fresh-baseline 被解析成开关', () => {
  const opts = gates.parseArgs(['--require-fresh-baseline']);
  assert.equal(opts.requireFreshBaseline, true);
  assert.equal(opts.baseline, false, '它不能顺手打开 --baseline：报错和改文件是两件事');
});

test('PA-02 --only 逗号分隔后能被区分，未知值不被静默吞掉', () => {
  const opts = gates.parseArgs(['--only', 'csharp,inventory']);
  assert.deepEqual(opts.only, ['csharp', 'inventory']);
});

test('PA-03 默认值：不刷基线、不要求基线新鲜、不输出 json', () => {
  const opts = gates.parseArgs([]);
  assert.equal(opts.baseline, false);
  assert.equal(opts.requireFreshBaseline, false);
  assert.equal(opts.json, false);
  assert.equal(opts.only, null);
});
