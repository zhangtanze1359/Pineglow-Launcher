'use strict';
/*
 * goal-validation.test.cjs — LONG-QUALITY-GOAL / G4 运行器自身门槛（合成用例）
 * ---------------------------------------------------------------------------
 * 用合成子命令（一个只写 TRX 并返回指定退出码的 node 脚本）驱动
 * scripts/run-goal-validation.ps1，断言真实退出码与 runner-verdict.json / refusal-*.json
 * 的原因码：
 *
 *   LQ-02 已核实际输入：实际输入字节变化（清单文件字节不变）必须拒绝复用/拒绝续跑；
 *        空清单、重复项、字段非法、实际文件缺失都必须被拒绝。
 *   LQ-03 新 attempt 独立目录：子命令未生成本轮 TRX 不能拿旧 TRX 通过；计数器必须齐全、
 *        一致，executed=0 即使 total>0 也必须 FAIL；UnitTestResult 行数必须与计数器相符。
 *   LQ-04 produces 参与指纹：新增必需产物不存在不能 REUSED；必需产物被删除必须重跑。
 *   LQ-05 不兼容续跑：输入绑定变化时拒绝沿用旧 runId/目录，旧记录、日志、TRX、产物不被覆盖。
 *   LQ-06 空 stages、unknown Only、重复 stage id、缺必需 stage 都不能整体 PASS；合法 Only
 *        只产生明确 partial verdict。
 *
 * 原有 10 项契约（正常通过、非零退出、缺 TRX、0 测试、required skip、TRX 有失败、中断记录
 * 不复用、输入变化不复用、兼容复用+产物篡改、日志计数不足）全部保留并加强，不删用例、
 * 不改 Skip、不放宽计数。
 *
 * 这些用例全部是合成夹具，不跑产品测试、不写证据包、不碰 D:\松光启动器 的产品源码。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const RUNNER = path.join(ROOT, 'scripts', 'run-goal-validation.ps1');
// This host has no PowerShell 7: the harness shell is Windows PowerShell 5.1
// (C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe). The runner is written to parse
// and run identically there, and that is what these tests exercise.
const PWSH = process.env.DSH_PWSH || 'powershell.exe';

/* SYNTH: argv = <trxPath> <exitCode> <total> <passed> <failed> <skipped> [logLine]
 * It writes a TRX whose Counters and UnitTestResult rows describe the SAME population, so the
 * runner's consistency gate is exercised against realistic data. */
const SYNTH = `
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const a = process.argv.slice(2);
const trx = a[0];
const exitCode = Number(a[1] || 0);
const total = Number(a[2] || 0);
const passed = Number(a[3] || 0);
const failed = Number(a[4] || 0);
const skipped = Number(a[5] || 0);
const logLine = a[6] || '';
if (logLine) process.stdout.write(logLine.replace(/\\\\n/g, '\\n') + '\\n');
if (trx && trx !== '-') {
	const p = path.isAbsolute(trx) ? trx : path.resolve(process.cwd(), trx);
	fs.mkdirSync(path.dirname(p), { recursive: true });
	const executed = total - skipped;
	const xml = '<?xml version="1.0" encoding="UTF-8"?>\\n' +
		'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">' +
		'<ResultSummary><Counters total="' + total + '" executed="' + executed +
		'" passed="' + passed + '" failed="' + failed + '" notExecuted="' + skipped +
		'" /></ResultSummary><Results>' + rows(passed, failed, skipped) + '</Results></TestRun>';
	fs.writeFileSync(p, xml);
}
function rows(p, f, s) {
	const out = [];
	let n = 0;
	for (let i = 0; i < p; i++) out.push('<UnitTestResult testName="t' + (n++) + '" outcome="Passed" />');
	for (let i = 0; i < f; i++) out.push('<UnitTestResult testName="t' + (n++) + '" outcome="Failed" />');
	for (let i = 0; i < s; i++) out.push('<UnitTestResult testName="t' + (n++) + '" outcome="NotExecuted" />');
	return out.join('');
}
process.exit(exitCode);
`;

function sha256(buf) { return crypto.createHash('sha256').update(buf).digest('hex').toUpperCase(); }

function fixture() {
	const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'goal-runner-'));
	const synth = path.join(dir, 'synth-stage.cjs');
	fs.writeFileSync(synth, SYNTH, 'utf8');
	const source = path.join(dir, 'source.txt');
	fs.writeFileSync(source, 'frozen input', 'utf8');
	const inputManifest = path.join(dir, 'input-manifest.json');
	const fx = { dir, synth, source, inputManifest, runDir: path.join(dir, 'run') };
	writeManifest(fx);
	return fx;
}

/* A valid, non-empty, verified input manifest: the runner re-checks the ACTUAL file. */
function writeManifest(fx, over) {
	const buf = fs.readFileSync(fx.source);
	const doc = Object.assign({
		schemaVersion: 1,
		sourceRoot: fx.dir,
		executionRoot: fx.dir,
		inputs: [{ path: 'source.txt', sizeBytes: buf.length, sha256: sha256(buf) }],
	}, over || {});
	fs.writeFileSync(fx.inputManifest, JSON.stringify(doc, null, 2), 'utf8');
	return doc;
}

function writeStages(fx, stages, runId, extra) {
	const file = path.join(fx.dir, 'stages.json');
	const doc = Object.assign({ schemaVersion: 1, runId: runId || '20261003-000000Z-test', stages }, extra || {});
	fs.writeFileSync(file, JSON.stringify(doc, null, 2), 'utf8');
	return file;
}

function stageDef(fx, over) {
	return Object.assign({
		id: 'unit',
		file: process.execPath,
		// {attemptDir} is the per-attempt directory the runner creates, so every attempt's TRX and
		// logs are physically separate and can never be confused with a previous attempt's.
		// {attemptDir} stays literal in the fingerprint, so a resume still recognises the stage.
		argv: [fx.synth, '{attemptDir}/results/unit.trx', '0', '3', '3', '0', '0'],
		cwd: fx.runDir,
		produces: ['results/unit.trx'],
		expect: { trx: 'results/unit.trx', minTests: 1, maxSkipped: 0 },
	}, over || {});
}

function runRunner(args) {
	const proc = spawnSync(PWSH, ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', RUNNER].concat(args),
		{ encoding: 'utf8', timeout: 120000 });
	return { code: proc.status, stdout: proc.stdout || '', stderr: proc.stderr || '' };
}

function run(fx, stagesFile, extra) {
	return runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stagesFile].concat(extra || []));
}

function verdict(fx) {
	return JSON.parse(fs.readFileSync(path.join(fx.runDir, 'runner-verdict.json'), 'utf8'));
}

function refusals(fx) {
	return fs.readdirSync(fx.runDir).filter(n => /^refusal-/.test(n));
}

function refusal(fx) {
	const files = refusals(fx);
	assert.ok(files.length > 0, 'a refusal record must exist');
	const all = files.map(f => JSON.parse(fs.readFileSync(path.join(fx.runDir, f), 'utf8')));
	return all[all.length - 1];
}

function reasonsOf(v, id) {
	const s = (v.stages || []).find(x => x.id === id);
	return s ? (s.reasons || []) : [];
}

function attemptFile(fx, id, seq, rel) {
	return path.join(fx.runDir, 'attempts', id, String(seq), rel.split('/').join(path.sep));
}

/* ============================ 原有 10 项契约（保留并加强） ============================ */

test('runner 01: a valid stage passes and its real exit code is recorded', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	const r = run(fx, stages);
	assert.equal(r.code, 0, 'runner must exit 0\n' + r.stdout + r.stderr);
	const v = verdict(fx);
	assert.equal(v.verdict, 'PASS');
	assert.equal(v.stages[0].status, 'COMPLETED');
	assert.equal(v.stages[0].exitCode, 0);
	assert.equal(v.stages[0].counters.total, 3);
	assert.equal(v.stages[0].counters.failed, 0);
	assert.equal(v.stages[0].artifacts[0].sha256.length, 64, 'artifact must be bound by sha256');
	assert.ok(fs.existsSync(attemptFile(fx, 'unit', 1, 'results/unit.trx')), 'the TRX must live in the attempt directory');
});

test('runner 02: a non-zero child exit is propagated and recorded', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '{attemptDir}/results/unit.trx', '3', '3', '2', '1', '0'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1, 'runner must exit non-zero\n' + r.stdout + r.stderr);
	const v = verdict(fx);
	assert.equal(v.verdict, 'FAIL');
	assert.equal(v.stages[0].exitCode, 3, 'the real child exit code must survive');
	assert.ok(reasonsOf(v, 'unit').includes('EXIT_NONZERO'));
	assert.ok(reasonsOf(v, 'unit').includes('TESTS_FAILED'));
});

test('runner 03: a missing TRX is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '-', '0', '0', '0', '0', '0'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('TRX_MISSING'));
});

test('runner 04: zero executed tests is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '{attemptDir}/results/unit.trx', '0', '0', '0', '0', '0'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1);
	const reasons = reasonsOf(verdict(fx), 'unit');
	assert.ok(reasons.includes('ZERO_TESTS') || reasons.includes('TESTS_BELOW_MIN'), JSON.stringify(reasons));
});

test('runner 05: a required skip is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '{attemptDir}/results/unit.trx', '0', '3', '2', '0', '1'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('REQUIRED_SKIP'));
});

test('runner 06: a failed TRX result is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '{attemptDir}/results/unit.trx', '0', '3', '2', '1', '0'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('TESTS_FAILED'));
});

test('runner 07: an interrupted stage is never reused on resume', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	const first = run(fx, stages);
	assert.equal(first.code, 0);
	// simulate an interrupted run: the stored record is not COMPLETED
	const statePath = path.join(fx.runDir, 'stages', 'unit.json');
	const stored = JSON.parse(fs.readFileSync(statePath, 'utf8'));
	stored.status = 'RUNNING';
	fs.writeFileSync(statePath, JSON.stringify(stored, null, 2), 'utf8');
	const resumed = run(fx, stages, ['-Resume']);
	assert.equal(resumed.code, 0, resumed.stdout + resumed.stderr);
	const v = verdict(fx);
	assert.equal(v.stages[0].reused, false, 'a non-COMPLETED record must not be reused');
	assert.equal(v.stages[0].status, 'COMPLETED');
	assert.ok(fs.existsSync(path.join(fx.runDir, 'stages', 'unit.incomplete.' + stored.runId + '.json')),
		'the interrupted record must be archived, not overwritten');
	assert.ok(fs.existsSync(attemptFile(fx, 'unit', 2, 'results/unit.trx')), 'the re-run must use a new attempt directory');
});

test('runner 08: a changed input binding refuses an incompatible resume and keeps the old record', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	assert.equal(run(fx, stages).code, 0);
	const firstVerdict = verdict(fx);
	assert.equal(firstVerdict.stages[0].status, 'COMPLETED');
	const firstSha = firstVerdict.inputManifestSha256;
	const statePath = path.join(fx.runDir, 'stages', 'unit.json');
	const before = fs.readFileSync(statePath, 'utf8');

	fs.appendFileSync(fx.inputManifest, '\n', 'utf8');
	const resumed = run(fx, stages, ['-Resume']);
	assert.equal(resumed.code, 2, 'an incompatible resume must be refused, not silently re-run\n' + resumed.stdout);
	assert.ok(refusals(fx).length > 0, 'the refusal must be recorded');
	assert.ok(refusal(fx).reasons.includes('INCOMPATIBLE_RESUME_SAME_RUN_DIR'), JSON.stringify(refusal(fx).reasons));
	assert.equal(fs.readFileSync(statePath, 'utf8'), before, 'the previous stage record must not be touched');
	const v = verdict(fx);
	assert.equal(v.inputManifestSha256, firstSha, 'the previous verdict must not be overwritten by the refusal');
});

test('runner 09: a compatible completed stage is reused, a tampered artifact is not', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	assert.equal(run(fx, stages).code, 0);

	const reuse = run(fx, stages, ['-Resume']);
	assert.equal(reuse.code, 0, reuse.stdout + reuse.stderr);
	assert.equal(verdict(fx).stages[0].reused, true, 'an unchanged completed stage must be reusable');

	const trx = attemptFile(fx, 'unit', 1, 'results/unit.trx');
	fs.appendFileSync(trx, ' <!-- tampered --> ', 'utf8');
	const tampered = fs.readFileSync(trx);
	const afterTamper = run(fx, stages, ['-Resume']);
	assert.equal(afterTamper.code, 0, afterTamper.stdout + afterTamper.stderr);
	assert.equal(verdict(fx).stages[0].reused, false, 'a tampered artifact must refuse reuse');
	assert.ok(fs.existsSync(attemptFile(fx, 'unit', 2, 'results/unit.trx')), 'the re-run must use a new attempt directory');
	assert.deepEqual(fs.readFileSync(trx), tampered,
		'the previous attempt evidence must be left exactly as found, never silently repaired or rewritten');
});

test('runner 10: a log count below the minimum is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '-', '0', '0', '0', '0', '0', 'tests 2'],
		produces: [],
		expect: { countPattern: 'tests (\\d+)', minCount: 5 },
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('COUNT_BELOW_MIN'));
});

test('runner 11: a stage can require its expected text on stderr instead of stdout', () => {
	const fx = fixture();
	// the child prints nothing on stdout and its marker on stderr (python unittest does exactly this)
	const stderrChild = path.join(fx.dir, 'synth-stderr.cjs');
	fs.writeFileSync(stderrChild, `
'use strict';
process.stderr.write('case one ... ok\\ncase two ... ok\\n');
`, 'utf8');
	const onStdout = writeStages(fx, [stageDef(fx, {
		file: process.execPath,
		argv: [stderrChild],
		produces: [],
		expect: { countPattern: '\\.\\.\\. ok', minCount: 2 },
	})]);
	const r1 = run(fx, onStdout);
	assert.equal(r1.code, 1, 'stderr text must not satisfy a stdout count');
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('COUNT_BELOW_MIN'));

	const fx2 = fixture();
	const onStderr = writeStages(fx2, [stageDef(fx2, {
		file: process.execPath,
		argv: [stderrChild],
		produces: [],
		expect: { countPattern: '\\.\\.\\. ok', countIn: 'stderr', minCount: 2 },
	})]);
	const r2 = run(fx2, onStderr);
	assert.equal(r2.code, 0, 'an explicit stderr count must pass\n' + r2.stdout + r2.stderr);
	assert.equal(verdict(fx2).verdict, 'PASS');

	const fx3 = fixture();
	const forbidOnStderr = writeStages(fx3, [stageDef(fx3, {
		file: process.execPath,
		argv: [stderrChild],
		produces: [],
		expect: { forbidPattern: 'case one', forbidIn: 'stderr' },
	})]);
	const r3 = run(fx3, forbidOnStderr);
	assert.equal(r3.code, 1, 'a forbidden stderr pattern must fail the stage');
	assert.ok(reasonsOf(verdict(fx3), 'unit').includes('FORBIDDEN_PATTERN'));
});

/* ============================ LQ-02 实际输入绑定 ============================ */

test('LQ-02: changed input bytes with an unchanged manifest refuse reuse and PASS', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	assert.equal(run(fx, stages).code, 0);
	const verdictPath = path.join(fx.runDir, 'runner-verdict.json');
	const verdictBefore = fs.readFileSync(verdictPath);
	const manifestShaBefore = verdict(fx).inputManifestSha256;

	// only the CONTENT of the file the manifest points at changes; the manifest file is untouched.
	// Same byte length, so this isolates the SHA check from the size check.
	fs.writeFileSync(fx.source, 'changed inpt', 'utf8');
	const resumed = run(fx, stages, ['-Resume']);
	assert.notEqual(resumed.code, 0, 'input drift must never produce a PASS\n' + resumed.stdout);
	assert.ok(refusals(fx).length > 0, 'the drift must be refused explicitly');
	assert.ok(refusal(fx).reasons.includes('INPUT_SHA_MISMATCH'), JSON.stringify(refusal(fx).reasons));
	assert.equal(verdict(fx).inputManifestSha256, manifestShaBefore);
	assert.deepEqual(fs.readFileSync(verdictPath), verdictBefore,
		'the refusal must leave the previous verdict byte-identical');
});

test('LQ-02: an empty, duplicated or malformed manifest is refused', () => {
	const cases = [
		[{ inputs: [] }, 'INPUT_SET_EMPTY'],
		[{ inputs: [{ path: 'source.txt', sizeBytes: 999999, sha256: sha256(Buffer.from('frozen input')) }] }, 'INPUT_SIZE_MISMATCH'],
		[{ inputs: [{ path: 'source.txt', sizeBytes: 0, sha256: 'not-a-sha' }] }, 'INPUT_SHA_INVALID'],
		[{ inputs: [{ path: 'missing-file.txt', sizeBytes: 1, sha256: 'A'.repeat(64) }] }, 'INPUT_FILE_MISSING'],
		[{ inputs: [{ path: 'a/b/../c.txt', sizeBytes: 1, sha256: 'A'.repeat(64) }] }, 'INPUT_PATH_INVALID'],
		[{ inputs: [{ path: 'source.txt', sizeBytes: 12, sha256: 'A'.repeat(64) }, { path: 'source.txt', sizeBytes: 12, sha256: 'A'.repeat(64) }] }, 'INPUT_DUPLICATE'],
	];
	for (const [over, expected] of cases) {
		const fx = fixture();
		writeManifest(fx, { inputs: over.inputs });
		const stages = writeStages(fx, [stageDef(fx)]);
		const r = run(fx, stages);
		assert.equal(r.code, 2, 'invalid input set must be refused: ' + expected + '\n' + r.stdout);
		assert.ok(refusal(fx).reasons.includes(expected),
			expected + ' expected, got ' + JSON.stringify(refusal(fx).reasons));
	}
});

/* ============================ LQ-03 本轮 TRX 与计数器 ============================ */

test('LQ-03: a pre-existing TRX the child never rewrote cannot be accepted', () => {
	const fx = fixture();
	const stale = path.join(fx.dir, 'stale.trx');
	fs.writeFileSync(stale, '<?xml version="1.0" encoding="UTF-8"?>\n' +
		'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary>' +
		'<Counters total="3" executed="3" passed="3" failed="0" notExecuted="0" /></ResultSummary>' +
		'<Results><UnitTestResult outcome="Passed" /><UnitTestResult outcome="Passed" /><UnitTestResult outcome="Passed" /></Results></TestRun>', 'utf8');
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '-', '0', '0', '0', '0', '0'],
		produces: [],
		expect: { trx: stale, minTests: 3, maxSkipped: 0 },
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1, 'a TRX that existed before the child ran must not be a PASS\n' + r.stdout);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('TRX_PREEXISTING'), JSON.stringify(reasonsOf(verdict(fx), 'unit')));
});

test('LQ-03: total > 0 with executed = 0 is never a pass', () => {
	const fx = fixture();
	// counters are internally consistent (3 = 0 + 3, 0 = 0 + 0) but nothing was executed
	const bad = path.join(fx.dir, 'synth-zero-executed.cjs');
	fs.writeFileSync(bad, `
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const p = path.resolve(process.cwd(), process.argv[2]);
fs.mkdirSync(path.dirname(p), { recursive: true });
fs.writeFileSync(p, '<?xml version="1.0" encoding="UTF-8"?><TestRun>' +
	'<ResultSummary><Counters total="3" executed="0" passed="0" failed="0" notExecuted="3" /></ResultSummary>' +
	'<Results><UnitTestResult outcome="NotExecuted" /><UnitTestResult outcome="NotExecuted" /><UnitTestResult outcome="NotExecuted" /></Results></TestRun>');
`, 'utf8');
	const stages = writeStages(fx, [stageDef(fx, {
		file: process.execPath,
		argv: [bad, '{attemptDir}/results/unit.trx'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1, 'zero executed tests must not pass\n' + r.stdout);
	const reasons = reasonsOf(verdict(fx), 'unit');
	assert.ok(reasons.includes('ZERO_TESTS'), JSON.stringify(reasons));
	assert.ok(reasons.includes('REQUIRED_SKIP'), JSON.stringify(reasons));
});

test('LQ-03: the exact inconsistent counter shape reported by the independent review is rejected', () => {
	const fx = fixture();
	// total=3, executed=0, passed=0, failed=0, notExecuted=0 with no result rows:
	// every one of these is impossible in a real TRX and must never reach PASS.
	const bad = path.join(fx.dir, 'synth-bogus-counters.cjs');
	fs.writeFileSync(bad, `
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const p = path.resolve(process.cwd(), process.argv[2]);
fs.mkdirSync(path.dirname(p), { recursive: true });
fs.writeFileSync(p, '<?xml version="1.0" encoding="UTF-8"?><TestRun>' +
	'<ResultSummary><Counters total="3" executed="0" passed="0" failed="0" notExecuted="0" /></ResultSummary></TestRun>');
`, 'utf8');
	const stages = writeStages(fx, [stageDef(fx, {
		file: process.execPath,
		argv: [bad, '{attemptDir}/results/unit.trx'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1, 'a bogus counter set must not pass\n' + r.stdout);
	const reasons = reasonsOf(verdict(fx), 'unit');
	assert.ok(reasons.includes('TRX_COUNTERS_INCONSISTENT'), JSON.stringify(reasons));
	assert.ok(reasons.includes('ZERO_TESTS'), JSON.stringify(reasons));
});

test('LQ-03: counters that disagree with the UnitTestResult rows are rejected', () => {
	const fx = fixture();
	// a child that writes counters claiming 3 executed/passed but only ONE result row
	const bad = path.join(fx.dir, 'synth-mismatch.cjs');
	fs.writeFileSync(bad, `
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const p = path.resolve(process.cwd(), process.argv[2]);
fs.mkdirSync(path.dirname(p), { recursive: true });
fs.writeFileSync(p, '<?xml version="1.0" encoding="UTF-8"?><TestRun>' +
	'<ResultSummary><Counters total="3" executed="3" passed="3" failed="0" notExecuted="0" /></ResultSummary>' +
	'<Results><UnitTestResult outcome="Passed" /></Results></TestRun>');
`, 'utf8');
	const stages = writeStages(fx, [stageDef(fx, {
		file: process.execPath,
		argv: [bad, '{attemptDir}/results/unit.trx'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1, 'a TRX whose rows disagree with its counters must not pass\n' + r.stdout);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('TRX_COUNTERS_INCONSISTENT'),
		JSON.stringify(reasonsOf(verdict(fx), 'unit')));
});

test('LQ-03: a TRX with no result rows at all is rejected', () => {
	const fx = fixture();
	const bad = path.join(fx.dir, 'synth-norows.cjs');
	fs.writeFileSync(bad, `
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const p = path.resolve(process.cwd(), process.argv[2]);
fs.mkdirSync(path.dirname(p), { recursive: true });
fs.writeFileSync(p, '<?xml version="1.0" encoding="UTF-8"?><TestRun>' +
	'<ResultSummary><Counters total="3" executed="3" passed="3" failed="0" notExecuted="0" /></ResultSummary></TestRun>');
`, 'utf8');
	const stages = writeStages(fx, [stageDef(fx, {
		file: process.execPath,
		argv: [bad, '{attemptDir}/results/unit.trx'],
	})]);
	const r = run(fx, stages);
	assert.equal(r.code, 1, 'a TRX without result rows must not pass\n' + r.stdout);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('TRX_NO_RESULT_ROWS'),
		JSON.stringify(reasonsOf(verdict(fx), 'unit')));
});

/* ============================ LQ-04 produces 指纹与产物 ============================ */

test('LQ-04: adding a required output that does not exist refuses reuse', () => {
	const fx = fixture();
	const base = stageDef(fx);
	const stages = writeStages(fx, [base]);
	assert.equal(run(fx, stages).code, 0);
	assert.equal(verdict(fx).stages[0].status, 'COMPLETED');

	const changed = stageDef(fx);
	changed.produces = ['results/unit.trx', 'missing-new-output.bin'];
	const stages2 = writeStages(fx, [changed]);
	const resumed = run(fx, stages2, ['-Resume']);
	assert.equal(resumed.code, 2, 'a changed output definition is an incompatible resume\n' + resumed.stdout);
	assert.ok(refusal(fx).reasons.includes('INCOMPATIBLE_RESUME_SAME_RUN_DIR'), JSON.stringify(refusal(fx).reasons));

	// in a FRESH run directory the same definition must FAIL, never PASS
	const fx2 = fixture();
	const stages3 = writeStages(fx2, [Object.assign(stageDef(fx2), { produces: ['results/unit.trx', 'missing-new-output.bin'] })]);
	const r = run(fx2, stages3);
	assert.equal(r.code, 1, 'a required output that does not exist must fail the stage\n' + r.stdout);
	assert.ok(reasonsOf(verdict(fx2), 'unit').includes('ARTIFACT_MISSING'), JSON.stringify(reasonsOf(verdict(fx2), 'unit')));
});

test('LQ-04: a deleted required output refuses reuse and re-runs', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	assert.equal(run(fx, stages).code, 0);
	fs.unlinkSync(attemptFile(fx, 'unit', 1, 'results/unit.trx'));
	const resumed = run(fx, stages, ['-Resume']);
	assert.equal(resumed.code, 0, resumed.stdout + resumed.stderr);
	assert.equal(verdict(fx).stages[0].reused, false, 'a missing required output must refuse reuse');
	assert.ok(fs.existsSync(attemptFile(fx, 'unit', 2, 'results/unit.trx')), 'it must re-run in a new attempt directory');
});

/* ============================ LQ-05 失败恢复不可覆盖 ============================ */

test('LQ-05: failure recovery preserves the original attempt logs, TRX and verdict', () => {
	const fx = fixture();
	const failing = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '{attemptDir}/results/unit.trx', '3', '3', '2', '1', '0'],
	})]);
	const first = run(fx, failing);
	assert.equal(first.code, 1);
	const firstVerdictSha = sha256(fs.readFileSync(path.join(fx.runDir, 'runner-verdict.json')));
	const logPath = attemptFile(fx, 'unit', 1, 'logs/unit.stdout.log');
	const trxPath = attemptFile(fx, 'unit', 1, 'results/unit.trx');
	const logSha = sha256(fs.readFileSync(logPath));
	const trxSha = sha256(fs.readFileSync(trxPath));

	const fixed = writeStages(fx, [stageDef(fx)]);
	const second = run(fx, fixed, ['-Resume']);
	assert.equal(second.code, 2, 'changing the definition is an incompatible resume');
	assert.equal(sha256(fs.readFileSync(logPath)), logSha, 'the failing attempt stdout must not be overwritten');
	assert.equal(sha256(fs.readFileSync(trxPath)), trxSha, 'the failing attempt TRX must not be overwritten');
	assert.equal(sha256(fs.readFileSync(path.join(fx.runDir, 'runner-verdict.json'))), firstVerdictSha,
		'the failing verdict must not be overwritten');
	assert.ok(fs.existsSync(path.join(fx.runDir, 'stages', 'unit.json')), 'the failing record must stay');
});

test('LQ-05: a transient failure recovers on resume without touching the failing attempt evidence', () => {
	const fx = fixture();
	// the child fails while the sentinel exists, then succeeds: same definition, new attempt
	const sentinel = path.join(fx.dir, 'fail-once.flag');
	fs.writeFileSync(sentinel, 'x', 'utf8');
	const flaky = path.join(fx.dir, 'synth-flaky.cjs');
	fs.writeFileSync(flaky, `
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const trx = process.argv[2];
const flag = process.argv[3];
const failing = fs.existsSync(flag);
const p = path.isAbsolute(trx) ? trx : path.resolve(process.cwd(), trx);
fs.mkdirSync(path.dirname(p), { recursive: true });
fs.writeFileSync(p, '<?xml version="1.0"?><TestRun><ResultSummary><Counters total="3" executed="3" passed="' +
	(failing ? '2' : '3') + '" failed="' + (failing ? '1' : '0') + '" notExecuted="0" /></ResultSummary><Results>' +
	(failing
		? '<UnitTestResult outcome="Passed" /><UnitTestResult outcome="Passed" /><UnitTestResult outcome="Failed" />'
		: '<UnitTestResult outcome="Passed" /><UnitTestResult outcome="Passed" /><UnitTestResult outcome="Passed" />') +
	'</Results></TestRun>');
process.exit(failing ? 1 : 0);
`, 'utf8');
	const stages = writeStages(fx, [stageDef(fx, {
		file: process.execPath,
		argv: [flaky, '{attemptDir}/results/unit.trx', sentinel],
	})]);
	const first = run(fx, stages);
	assert.equal(first.code, 1, 'the first attempt must fail\n' + first.stdout);
	const log1 = attemptFile(fx, 'unit', 1, 'logs/unit.stdout.log');
	const trx1 = attemptFile(fx, 'unit', 1, 'results/unit.trx');
	const logSha = sha256(fs.readFileSync(log1));
	const trxSha = sha256(fs.readFileSync(trx1));
	const record1 = JSON.parse(fs.readFileSync(path.join(fx.runDir, 'stages', 'unit.json'), 'utf8'));
	assert.equal(record1.status, 'FAILED');

	fs.unlinkSync(sentinel);
	const second = run(fx, stages, ['-Resume']);
	assert.equal(second.code, 0, 'the recovery attempt must pass\n' + second.stdout + second.stderr);
	const v = verdict(fx);
	assert.equal(v.stages[0].status, 'COMPLETED');
	assert.equal(sha256(fs.readFileSync(log1)), logSha, 'the failing attempt stdout must be byte-identical after recovery');
	assert.equal(sha256(fs.readFileSync(trx1)), trxSha, 'the failing attempt TRX must be byte-identical after recovery');
	assert.ok(fs.existsSync(path.join(fx.runDir, 'stages', 'unit.incomplete.' + record1.runId + '.json')),
		'the failing record must be archived, not overwritten');
	assert.ok(fs.existsSync(attemptFile(fx, 'unit', 2, 'results/unit.trx')), 'the recovery must run in a new attempt directory');
	assert.ok(fs.existsSync(path.join(fx.runDir, 'verdicts')), 'the previous run verdict must be archived');
});

/* ============================ LQ-06 空执行、Only、必需 stage ============================ */

test('LQ-06: an empty stage list is refused, never an overall PASS', () => {
	const fx = fixture();
	const stages = writeStages(fx, []);
	const r = run(fx, stages);
	assert.equal(r.code, 2, 'zero stages must be refused\n' + r.stdout);
	assert.ok(refusal(fx).reasons.includes('NO_STAGES'), JSON.stringify(refusal(fx).reasons));
	assert.notEqual(verdict(fx).verdict, 'PASS');
});

test('LQ-06: an unknown -Only selection is refused', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	const r = run(fx, stages, ['-Only', 'missing-id']);
	assert.equal(r.code, 2, 'an unknown -Only id must be refused\n' + r.stdout);
	assert.ok(refusal(fx).reasons.includes('ONLY_NOT_FOUND'), JSON.stringify(refusal(fx).reasons));
});

test('LQ-06: a legal -Only subset yields PARTIAL, not an overall PASS', () => {
	const fx = fixture();
	const a = stageDef(fx);
	const b = Object.assign(stageDef(fx), { id: 'unit-b' });
	const stages = writeStages(fx, [a, b], 'partial-run', { requiredStages: ['unit', 'unit-b'] });
	const r = run(fx, stages, ['-Only', 'unit']);
	assert.equal(r.code, 3, 'a partial selection must not exit 0\n' + r.stdout);
	const v = verdict(fx);
	assert.equal(v.verdict, 'PARTIAL');
	assert.equal(v.partial, true);
	assert.equal(v.stageCount, 1);

	const full = run(fx, stages);
	assert.equal(full.code, 0, full.stdout + full.stderr);
	assert.equal(verdict(fx).verdict, 'PASS');
});

test('LQ-06: a duplicated stage id is refused', () => {
	const fx = fixture();
	const a = stageDef(fx);
	const b = Object.assign(stageDef(fx), { id: 'unit' });
	const stages = writeStages(fx, [a, b]);
	const r = run(fx, stages);
	assert.equal(r.code, 2, 'a duplicated stage id must be refused\n' + r.stdout);
	assert.ok(refusal(fx).reasons.includes('DUPLICATE_STAGE_ID'), JSON.stringify(refusal(fx).reasons));
});

test('LQ-06: a required stage missing from the definition is refused', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)], 'req-run', { requiredStages: ['unit', 'g4-flows'] });
	const r = run(fx, stages);
	assert.equal(r.code, 2, 'a missing required stage must be refused\n' + r.stdout);
	assert.ok(refusal(fx).reasons.includes('REQUIRED_STAGE_MISSING'), JSON.stringify(refusal(fx).reasons));
});

test('LQ-06: a binding file is written and bound to the verified input set', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	assert.equal(run(fx, stages).code, 0);
	const binding = JSON.parse(fs.readFileSync(path.join(fx.runDir, 'run-binding.json'), 'utf8'));
	const v = verdict(fx);
	assert.equal(binding.verifiedInputSetSha256, v.verifiedInputSetSha256, 'the binding must record the verified input set');
	assert.equal(binding.inputManifestSha256, v.inputManifestSha256);
	assert.equal(v.verifiedInputCount, 1);
	assert.equal(typeof v.verifiedInputRoot, 'string');
	assert.ok(v.verifiedInputRoot.length > 0, 'the verified input root must be recorded');
	assert.ok(!refusals(fx).length, 'a valid run must not be refused');
});
