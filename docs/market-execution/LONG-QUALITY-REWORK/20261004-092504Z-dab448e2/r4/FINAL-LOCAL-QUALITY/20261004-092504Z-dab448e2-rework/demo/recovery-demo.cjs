'use strict';
/*
 * recovery-demo.cjs — R3 failure -> recovery demonstration on the real runner.
 *
 * Section A (same definition, transient failure):
 *   1. attempt 1 fails (child exits 1, TRX has 1 failed row) -> stage FAILED, verdict FAIL, exit 1
 *   2. the cause is removed (nothing in the definition changes)
 *   3. Resume -> the SAME stage re-runs in a NEW attempt directory and passes
 *   4. proof: attempt 1's stage record, stdout, stderr and TRX are byte-identical afterwards,
 *      and essentially: the old attempt evidence is never overwritten
 *
 * Section B (changed input, separate item):
 *   5. the actual bytes of a verified input change
 *   6. Resume on the SAME run directory is refused (exit 2) and the old run is untouched
 *   7. a NEW run directory + a NEW runId runs to PASS, and the old run keeps its records
 *
 * Usage: node recovery-demo.cjs <runnerPs1> <evidenceDemoDir>
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');

const [runner, demoDir] = process.argv.slice(2);
if (!runner || !demoDir) { console.error('usage: node recovery-demo.cjs <runnerPs1> <demoDir>'); process.exit(2); }
const PWSH = 'C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe';
const env = { ...process.env, PSModulePath: 'C:/Windows/System32/WindowsPowerShell/v1.0/Modules;C:/Program Files/WindowsPowerShell/Modules' };
const sha256 = p => crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex').toUpperCase();

fs.mkdirSync(demoDir, { recursive: true });
const fixtureDir = path.join(demoDir, 'fixture');
fs.mkdirSync(path.join(fixtureDir, 'run-a'), { recursive: true });
fs.mkdirSync(path.join(fixtureDir, 'run-b'), { recursive: true });

const source = path.join(fixtureDir, 'source.txt');
fs.writeFileSync(source, 'frozen input', 'utf8');
const manifest = path.join(fixtureDir, 'input.json');
function writeManifest() {
	const buf = fs.readFileSync(source);
	fs.writeFileSync(manifest, JSON.stringify({
		schemaVersion: 1, sourceRoot: fixtureDir, executionRoot: fixtureDir,
		inputs: [{ path: 'source.txt', sizeBytes: buf.length, sha256: sha256(source) }],
	}, null, 2), 'utf8');
}
writeManifest();

const sentinel = path.join(fixtureDir, 'make-it-fail.flag');
fs.writeFileSync(sentinel, 'fail', 'utf8');
const child = path.join(fixtureDir, 'child.cjs');
fs.writeFileSync(child, `
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const out = process.argv[2];
const flag = process.argv[3];
const failing = fs.existsSync(flag);
fs.mkdirSync(path.dirname(out), { recursive: true });
const rows = failing
	? '<UnitTestResult testName="a" outcome="Passed" /><UnitTestResult testName="b" outcome="Failed" />'
	: '<UnitTestResult testName="a" outcome="Passed" /><UnitTestResult testName="b" outcome="Passed" />';
fs.writeFileSync(out, '<?xml version="1.0" encoding="UTF-8"?><TestRun><ResultSummary><Counters total="2" executed="2" passed="' +
	(failing ? 1 : 2) + '" failed="' + (failing ? 1 : 0) + '" notExecuted="0" /></ResultSummary><Results>' + rows + '</Results></TestRun>');
console.log('demo child ran, failing=' + failing);
process.exit(failing ? 1 : 0);
`, 'utf8');

const stagesFile = path.join(fixtureDir, 'stages.json');
function writeStages(doc) { fs.writeFileSync(stagesFile, JSON.stringify(doc, null, 2), 'utf8'); }
const stageDoc = {
	schemaVersion: 1,
	runId: 'demo-run-1',
	requiredStages: ['demo'],
	stages: [{
		id: 'demo',
		file: process.execPath,
		argv: [child, '{attemptDir}/results/demo.trx', sentinel],
		cwd: path.join(fixtureDir, 'run-a'),
		produces: ['results/demo.trx'],
		expect: { trx: 'results/demo.trx', minTests: 2, maxSkipped: 0 },
	}],
};

function invoke(runDir, extra, label) {
	writeStages(stageDoc);
	const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', runner,
		'-InputManifest', manifest, '-RunDir', runDir, '-StagesFile', stagesFile].concat(extra || []);
	const p = spawnSync(PWSH, args, { encoding: 'utf8', env, timeout: 120000 });
	const vpath = path.join(runDir, 'runner-verdict.json');
	const v = fs.existsSync(vpath) ? JSON.parse(fs.readFileSync(vpath, 'utf8')) : null;
	return { label, runDir, args, exitCode: p.status, stdout: p.stdout || '', stderr: p.stderr || '', verdict: v };
}

const runA = path.join(fixtureDir, 'run-a');
const runB = path.join(fixtureDir, 'run-b');
const report = { runner, demoDir, fixtureDir, sections: {} };

/* ---------------- Section A ---------------- */
const a1 = invoke(runA, [], 'A1-fail');
// attempt-owned evidence: these files belong to attempt 1 and must never be rewritten.
// stages/demo.json is deliberately NOT in this set: it is the current-state pointer, and the
// superseded record is archived instead (checked separately below).
const a1Files = {
	stdout: path.join(runA, 'attempts', 'demo', '1', 'logs', 'demo.stdout.log'),
	stderr: path.join(runA, 'attempts', 'demo', '1', 'logs', 'demo.stderr.log'),
	trx: path.join(runA, 'attempts', 'demo', '1', 'results', 'demo.trx'),
};
const statePointer = path.join(runA, 'stages', 'demo.json');
const a1StateBytes = fs.readFileSync(statePointer);
const a1Shas = {};
for (const [k, p] of Object.entries(a1Files)) a1Shas[k] = fs.existsSync(p) ? sha256(p) : null;

fs.unlinkSync(sentinel);           // remove the cause; the stage DEFINITION is unchanged
const a2 = invoke(runA, ['-Resume'], 'A2-recover');
const a2Shas = {};
for (const [k, p] of Object.entries(a1Files)) a2Shas[k] = fs.existsSync(p) ? sha256(p) : null;
const archiveName = 'demo.incomplete.demo-run-1.json';
const archivedRecord = path.join(runA, 'stages', archiveName);
const archivedRecordMatches = fs.existsSync(archivedRecord) && fs.readFileSync(archivedRecord).equals(a1StateBytes);

report.sections.A_same_definition_transient_failure = {
	firstRun: { exitCode: a1.exitCode, verdict: a1.verdict && a1.verdict.verdict, stageStatus: a1.verdict && a1.verdict.stages[0].status, reasons: a1.verdict && a1.verdict.stages[0].reasons },
	recoveryRun: { exitCode: a2.exitCode, verdict: a2.verdict && a2.verdict.verdict, stageStatus: a2.verdict && a2.verdict.stages[0].status, reused: a2.verdict && a2.verdict.stages[0].reused, attemptDir: a2.verdict && a2.verdict.stages[0].attemptDir },
	attempt1EvidenceUnchanged: Object.keys(a1Shas).every(k => a1Shas[k] && a1Shas[k] === a2Shas[k]),
	shas: { before: a1Shas, after: a2Shas },
	supersededRecordArchivedByteIdentical: archivedRecordMatches,
	archivedRecord: archiveName,
	archivedRecords: fs.readdirSync(path.join(runA, 'stages')),
	attemptDirs: fs.readdirSync(path.join(runA, 'attempts', 'demo')),
	archivedVerdicts: fs.existsSync(path.join(runA, 'verdicts')) ? fs.readdirSync(path.join(runA, 'verdicts')) : [],
	attempt2TrxExists: fs.existsSync(path.join(runA, 'attempts', 'demo', '2', 'results', 'demo.trx')),
};

/* ---------------- Section B ---------------- */
const runC = path.join(fixtureDir, 'run-c');
fs.mkdirSync(runC, { recursive: true });
const b0 = invoke(runB, [], 'B0-baseline');
const b0Verdict = fs.readFileSync(path.join(runB, 'runner-verdict.json'));
const b0Binding = sha256(path.join(runB, 'run-binding.json'));

// change the ACTUAL bytes of the verified input; the manifest file itself is not touched
fs.writeFileSync(source, 'changed inpt', 'utf8');
const b1 = invoke(runB, ['-Resume'], 'B1-incompatible-resume');
const refusalFiles = fs.readdirSync(runB).filter(n => /^refusal-/.test(n));
const refusal = refusalFiles.length ? JSON.parse(fs.readFileSync(path.join(runB, refusalFiles[refusalFiles.length - 1]), 'utf8')) : null;

// adopting a changed input is a NEW baseline manifest + a NEW run directory + a NEW runId
writeManifest();
const stageDocB = JSON.parse(JSON.stringify(stageDoc));
stageDocB.runId = 'demo-run-2';
fs.writeFileSync(path.join(fixtureDir, 'stages-b.json'), JSON.stringify(stageDocB, null, 2), 'utf8');
function invokeB(runDir, extra, label) {
	const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', runner,
		'-InputManifest', manifest, '-RunDir', runDir, '-StagesFile', path.join(fixtureDir, 'stages-b.json')].concat(extra || []);
	const p = spawnSync(PWSH, args, { encoding: 'utf8', env, timeout: 120000 });
	const vpath = path.join(runDir, 'runner-verdict.json');
	const v = fs.existsSync(vpath) ? JSON.parse(fs.readFileSync(vpath, 'utf8')) : null;
	return { label, runDir, exitCode: p.status, stdout: p.stdout || '', stderr: p.stderr || '', verdict: v };
}
const b2 = invokeB(runC, [], 'B2-new-run-id');

report.sections.B_changed_input_needs_new_runId = {
	baseline: { exitCode: b0.exitCode, verdict: b0.verdict && b0.verdict.verdict, runId: b0.verdict && b0.verdict.runId, verifiedInputSetSha256: b0.verdict && b0.verdict.verifiedInputSetSha256 },
	incompatibleResume: { exitCode: b1.exitCode, refusalReasons: refusal && refusal.reasons, oldVerdictPreserved: b0Verdict.equals(fs.readFileSync(path.join(runB, 'runner-verdict.json'))), oldBindingPreserved: b0Binding === sha256(path.join(runB, 'run-binding.json')) },
	newRunIdRun: { exitCode: b2.exitCode, verdict: b2.verdict && b2.verdict.verdict, runId: b2.verdict && b2.verdict.runId, verifiedInputSetSha256: b2.verdict && b2.verdict.verifiedInputSetSha256 },
	inputSetShaChanged: !!(b0.verdict && b2.verdict && b0.verdict.verifiedInputSetSha256 !== b2.verdict.verifiedInputSetSha256),
};

// A changed input is refused either by the LQ-02 verified-input gate (INPUT_SHA_MISMATCH, which
// fires first because the bytes no longer match the manifest) or by the LQ-05 run binding
// (INCOMPATIBLE_RESUME_SAME_RUN_DIR, which catches manifest/definition changes). Either is a
// refusal; neither may ever reuse or report a new PASS.
const refusedByInputGate = !!refusal && refusal.reasons.includes('INPUT_SHA_MISMATCH');
const refusedByBinding = !!refusal && refusal.reasons.includes('INCOMPATIBLE_RESUME_SAME_RUN_DIR');
report.sections.B_changed_input_needs_new_runId.incompatibleResume.refusedByInputGate = refusedByInputGate;
report.sections.B_changed_input_needs_new_runId.incompatibleResume.refusedByBinding = refusedByBinding;

const ok = a1.exitCode === 1 && a2.exitCode === 0 &&
	report.sections.A_same_definition_transient_failure.attempt1EvidenceUnchanged &&
	report.sections.A_same_definition_transient_failure.supersededRecordArchivedByteIdentical &&
	report.sections.A_same_definition_transient_failure.attempt2TrxExists &&
	b1.exitCode === 2 && (refusedByInputGate || refusedByBinding) &&
	report.sections.B_changed_input_needs_new_runId.incompatibleResume.oldVerdictPreserved &&
	b2.exitCode === 0 && b2.verdict && b2.verdict.verdict === 'PASS' &&
	report.sections.B_changed_input_needs_new_runId.inputSetShaChanged;

report.verdict = ok ? 'DEMO_PASS' : 'DEMO_FAIL';
fs.writeFileSync(path.join(demoDir, 'recovery-demo.json'), JSON.stringify(report, null, 2), 'utf8');
console.log(JSON.stringify({ verdict: report.verdict, A: report.sections.A_same_definition_transient_failure, B: report.sections.B_changed_input_needs_new_runId }, null, 2));
process.exit(ok ? 0 : 1);
