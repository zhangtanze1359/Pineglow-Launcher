'use strict';
/*
 * goal-validation.test.cjs — LONG-QUALITY-GOAL / G4 运行器自身门槛（合成用例）
 * ---------------------------------------------------------------------------
 * 用合成子命令（一个只写 TRX 并返回指定退出码的 node 脚本）驱动
 * scripts/run-goal-validation.ps1，断言真实退出码与 runner-verdict.json 的原因码：
 *   1  正常通过（对照）
 *   2  子进程非零退出必须传播（并记录子进程真实退出码）
 *   3  缺 TRX 不得 PASS
 *   4  0 测试不得 PASS
 *   5  required skip 不得 PASS
 *   6  TRX 内有失败不得 PASS
 *   7  中断的 stage（记录不是 COMPLETED）不得被 resume 复用
 *   8  输入变化后不得复用旧 stage
 *   9  产物被改动/删除后不得复用
 *  10  日志计数不足不得 PASS
 *
 * 这些用例全部是合成夹具，不跑产品测试、不写证据包、不碰 D:\松光启动器 的产品源码。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const RUNNER = path.join(ROOT, 'scripts', 'run-goal-validation.ps1');
// This host has no PowerShell 7: the harness shell is Windows PowerShell 5.1
// (C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe). The runner is written to parse
// and run identically there, and that is what these tests exercise.
const PWSH = process.env.DSH_PWSH || 'powershell.exe';

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
	const xml = '<?xml version="1.0" encoding="UTF-8"?>\\n' +
		'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">' +
		'<ResultSummary><Counters total="' + total + '" executed="' + (total - skipped) +
		'" passed="' + passed + '" failed="' + failed + '" notExecuted="' + skipped +
		'" /></ResultSummary></TestRun>';
	fs.writeFileSync(p, xml);
}
process.exit(exitCode);
`;

function fixture() {
	const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'goal-runner-'));
	const synth = path.join(dir, 'synth-stage.cjs');
	fs.writeFileSync(synth, SYNTH, 'utf8');
	const inputManifest = path.join(dir, 'input-manifest.json');
	fs.writeFileSync(inputManifest, JSON.stringify({ schemaVersion: 1, inputs: [] }, null, 2), 'utf8');
	return { dir, synth, inputManifest, runDir: path.join(dir, 'run') };
}

function writeStages(fx, stages, runId) {
	const file = path.join(fx.dir, 'stages.json');
	fs.writeFileSync(file, JSON.stringify({ schemaVersion: 1, runId: runId || '20261003-000000Z-test', stages }, null, 2), 'utf8');
	return file;
}

function stageDef(fx, over) {
	return Object.assign({
		id: 'unit',
		file: process.execPath,
		argv: [fx.synth, 'results/unit.trx', '0', '3', '3', '0', '0'],
		// the child works in the run package, exactly like a real stage whose --results-directory
		// points at the run package, so a relative TRX/artifact path resolves for both sides
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

function verdict(fx) {
	return JSON.parse(fs.readFileSync(path.join(fx.runDir, 'runner-verdict.json'), 'utf8'));
}

function reasonsOf(v, id) {
	const s = (v.stages || []).find(x => x.id === id);
	return s ? (s.reasons || []) : [];
}

test('runner 01: a valid stage passes and its real exit code is recorded', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	const r = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
	assert.equal(r.code, 0, 'runner must exit 0\n' + r.stdout + r.stderr);
	const v = verdict(fx);
	assert.equal(v.verdict, 'PASS');
	assert.equal(v.stages[0].status, 'COMPLETED');
	assert.equal(v.stages[0].exitCode, 0);
	assert.equal(v.stages[0].counters.total, 3);
	assert.equal(v.stages[0].counters.failed, 0);
	assert.equal(v.stages[0].artifacts[0].sha256.length, 64, 'artifact must be bound by sha256');
});

test('runner 02: a non-zero child exit is propagated and recorded', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, 'results/unit.trx', '3', '3', '2', '1', '0'],
	})]);
	const r = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
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
	const r = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('TRX_MISSING'));
});

test('runner 04: zero executed tests is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, 'results/unit.trx', '0', '0', '0', '0', '0'],
	})]);
	const r = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
	assert.equal(r.code, 1);
	const reasons = reasonsOf(verdict(fx), 'unit');
	assert.ok(reasons.includes('ZERO_TESTS') || reasons.includes('TESTS_BELOW_MIN'), JSON.stringify(reasons));
});

test('runner 05: a required skip is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, 'results/unit.trx', '0', '3', '2', '0', '1'],
	})]);
	const r = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('REQUIRED_SKIP'));
});

test('runner 06: a failed TRX result is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, 'results/unit.trx', '0', '3', '2', '1', '0'],
	})]);
	const r = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('TESTS_FAILED'));
});

test('runner 07: an interrupted stage is never reused on resume', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	const first = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
	assert.equal(first.code, 0);
	// simulate an interrupted run: the stored record is not COMPLETED
	const statePath = path.join(fx.runDir, 'stages', 'unit.json');
	const stored = JSON.parse(fs.readFileSync(statePath, 'utf8'));
	stored.status = 'RUNNING';
	fs.writeFileSync(statePath, JSON.stringify(stored, null, 2), 'utf8');
	const resumed = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages, '-Resume']);
	assert.equal(resumed.code, 0, resumed.stdout + resumed.stderr);
	const v = verdict(fx);
	assert.equal(v.stages[0].reused, false, 'a non-COMPLETED record must not be reused');
	assert.equal(v.stages[0].status, 'COMPLETED');
	assert.ok(fs.existsSync(path.join(fx.runDir, 'stages', 'unit.incomplete.' + stored.runId + '.json')),
		'the interrupted record must be archived, not overwritten');
});

test('runner 08: a changed input manifest refuses reuse', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	assert.equal(runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]).code, 0);
	const firstVerdict = verdict(fx);
	assert.equal(firstVerdict.stages[0].status, 'COMPLETED');

	fs.appendFileSync(fx.inputManifest, '\n', 'utf8');
	const resumed = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages, '-Resume']);
	assert.equal(resumed.code, 0, resumed.stdout + resumed.stderr);
	const v = verdict(fx);
	assert.notEqual(v.inputManifestSha256, firstVerdict.inputManifestSha256, 'input sha must change');
	assert.equal(v.stages[0].reused, false, 'a different input manifest must refuse reuse');
	assert.equal(v.stages[0].inputManifestSha256, v.inputManifestSha256, 'the record must bind the new input sha');
});

test('runner 09: a compatible completed stage is reused, a tampered artifact is not', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx)]);
	assert.equal(runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]).code, 0);

	const reuse = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages, '-Resume']);
	assert.equal(reuse.code, 0, reuse.stdout + reuse.stderr);
	assert.equal(verdict(fx).stages[0].reused, true, 'an unchanged completed stage must be reusable');

	fs.appendFileSync(path.join(fx.runDir, 'results', 'unit.trx'), ' <!-- tampered --> ', 'utf8');
	const afterTamper = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages, '-Resume']);
	assert.equal(afterTamper.code, 0, afterTamper.stdout + afterTamper.stderr);
	assert.equal(verdict(fx).stages[0].reused, false, 'a tampered artifact must refuse reuse');
});

test('runner 10: a log count below the minimum is not a pass', () => {
	const fx = fixture();
	const stages = writeStages(fx, [stageDef(fx, {
		argv: [fx.synth, '-', '0', '0', '0', '0', '0', 'tests 2'],
		produces: [],
		expect: { countPattern: 'tests (\\d+)', minCount: 5 },
	})]);
	const r = runRunner(['-InputManifest', fx.inputManifest, '-RunDir', fx.runDir, '-StagesFile', stages]);
	assert.equal(r.code, 1);
	assert.ok(reasonsOf(verdict(fx), 'unit').includes('COUNT_BELOW_MIN'));
});
