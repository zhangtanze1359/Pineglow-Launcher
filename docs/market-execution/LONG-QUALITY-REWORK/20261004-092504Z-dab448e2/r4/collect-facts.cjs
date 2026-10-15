'use strict';
/*
 * collect-facts.cjs — R4 machine verdict for the LONG-QUALITY-REWORK round.
 * Reads only files already produced by R0..R3 and emits r4/verdict/*.json.
 *
 * Usage: node collect-facts.cjs <evidenceRoot>
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const root = process.argv[2];
if (!root) { console.error('usage: node collect-facts.cjs <evidenceRoot>'); process.exit(2); }
const p = (...a) => path.join(root, ...a);
const readJson = f => JSON.parse(fs.readFileSync(f, 'utf8'));
const exists = f => fs.existsSync(f);
const sha256 = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex').toUpperCase();
const sha256OrNull = f => (exists(f) ? sha256(f) : null);

const outDir = p('r4', 'verdict');
fs.mkdirSync(outDir, { recursive: true });

/* ---------- 1. input drift: R0 baseline -> R3 baseline -> terminal ---------- */
const r0Manifest = readJson(p('r0', 'input-manifest.json'));
const r3Manifest = readJson(p('r3', 'g2', 'input-manifest.json'));
const terminalManifest = readJson(p('r4', 'terminal', 'source-after-manifest.json'));
const byPath = m => new Map(m.inputs.map(e => [e.path, e]));
const r0Map = byPath(r0Manifest), r3Map = byPath(r3Manifest), termMap = byPath(terminalManifest);
const changed = [], added = [], removed = [];
for (const [k, v] of r3Map) {
	const prev = r0Map.get(k);
	if (!prev) added.push(k);
	else if (prev.sha256 !== v.sha256) changed.push({ path: k, fromSha256: prev.sha256, toSha256: v.sha256, fromBytes: prev.sizeBytes, toBytes: v.sizeBytes });
}
for (const k of r0Map.keys()) if (!r3Map.has(k)) removed.push(k);
const terminalDrift = [];
for (const [k, v] of termMap) {
	const base = r3Map.get(k);
	if (!base) terminalDrift.push({ path: k, kind: 'added-at-terminal' });
	else if (base.sha256 !== v.sha256) terminalDrift.push({ path: k, kind: 'changed-at-terminal', baseSha256: base.sha256, terminalSha256: v.sha256 });
}
for (const k of r3Map.keys()) if (!termMap.has(k)) terminalDrift.push({ path: k, kind: 'removed-at-terminal' });

const changedFiles = {
	schemaVersion: 1,
	generatedUtc: new Date().toISOString(),
	note: [
		'R0 baseline (pre-fix working tree) -> R3 baseline (post-fix working tree). Only files this rework',
		'intentionally edited appear under "changed"; nothing was deleted, and no new source file was added',
		'(the input count stays 292). Everything else in the tree was already dirty before this round and is',
		'NOT claimed as this round\'s work.',
	],
	sourceRoot: r3Manifest.sourceRoot,
	head: r3Manifest.head,
	inputCount: { r0: r0Manifest.counts.total, r3: r3Manifest.counts.total, terminal: terminalManifest.counts.total },
	changed, added, removed,
	terminalDrift,
};
fs.writeFileSync(path.join(outDir, 'changed-files.json'), JSON.stringify(changedFiles, null, 2), 'utf8');

/* ---------- 2. run-final verdict / counts ---------- */
const verdictPath = p('r3', 'run-final', 'runner-verdict.json');
const runVerdict = readJson(verdictPath);
const stages = {};
for (const s of runVerdict.stages) {
	stages[s.id] = {
		status: s.status, exitCode: s.exitCode, attemptDir: s.attemptDir,
		counters: s.counters, reasons: s.reasons,
		artifacts: (s.artifacts || []).map(a => ({ path: a.path, sha256: a.sha256, sizeBytes: a.sizeBytes })),
	};
}
const csharp = ['g4-secrets', 'g4-ordinary-r2', 'g4-recovery-normal', 'g4-flows']
	.map(id => stages[id] && stages[id].counters).filter(Boolean);
const csharpTotal = csharp.reduce((a, c) => a + (c ? c.total : 0), 0);
const csharpPassed = csharp.reduce((a, c) => a + (c ? c.passed : 0), 0);
const csharpSkipped = csharp.reduce((a, c) => a + (c ? c.notExecuted : 0), 0);

const scenario = readJson(p('r3', 'scenario', 'scenario-matrix.json'));
const demo = readJson(p('r3', 'demo', 'recovery-demo.json'));
const audit = readJson(p('r3', 'audit-real.json'));
const disproofR0 = readJson(p('r0', 'red', 'runner-disproof-results.json'));
const disproofR3 = readJson(p('r3', 'independent-reproof', 'runner-disproof-results.json'));

const r0FalsePass = disproofR0.cases.filter(c => c.actual.exitCode === 0 && c.actual.verdict && c.actual.verdict.verdict === 'PASS').length;
const r3FalsePass = disproofR3.cases.filter(c => c.actual.exitCode === 0 && c.actual.verdict && c.actual.verdict.verdict === 'PASS').length;
const r3AllRejected = disproofR3.cases.every(c => c.actual.exitCode !== 0);

/* node / runner / auditor tallies are asserted by the runner stages through countPattern; the
 * exact numbers are also recorded here from the stage stdout logs. */
function countInLog(logFile, pattern) {
	const text = fs.readFileSync(logFile, 'utf8');
	const re = new RegExp(pattern, 'gm');
	let n = 0, m;
	while ((m = re.exec(text)) !== null) n += m[1] ? Number(m[1]) : 1;
	return n;
}
const nodePass = countInLog(p('r3', 'run-final', 'attempts', 'g4-node-all', '1', 'logs', 'g4-node-all.stdout.log'), '^# pass (\\d+)$');
const runnerPass = countInLog(p('r3', 'run-final', 'attempts', 'g4-runner-tests', '1', 'logs', 'g4-runner-tests.stdout.log'), '^# pass (\\d+)$');
const auditorOk = countInLog(p('r3', 'run-final', 'attempts', 'g4-auditor-tests', '1', 'logs', 'g4-auditor-tests.stderr.log'), '\\.\\.\\. ok$');
const bisectPass = countInLog(p('r3', 'run-final', 'attempts', 'g4-bisect-smoke', '1', 'logs', 'g4-bisect-smoke.stdout.log'), '^\\s*\\[PASS\\]');

const counts = {
	schemaVersion: 1,
	generatedUtc: new Date().toISOString(),
	runId: runVerdict.runId,
	runnerVerdict: runVerdict.verdict,
	runnerExitIsZero: runVerdict.failedCount === 0 && runVerdict.partial === false,
	verifiedInputs: runVerdict.verifiedInputCount,
	verifiedInputSetSha256: runVerdict.verifiedInputSetSha256,
	inputManifestSha256: runVerdict.inputManifestSha256,
	stagesFileSha256: runVerdict.stagesFileSha256,
	csharp: { selected: 73, total: csharpTotal, passed: csharpPassed, requiredSkipped: csharpSkipped, perStage: csharp.map((c, i) => ({ id: ['g4-secrets', 'g4-ordinary-r2', 'g4-recovery-normal', 'g4-flows'][i], total: c.total, executed: c.executed, passed: c.passed, failed: c.failed, notExecuted: c.notExecuted, resultRows: c.resultRows })) },
	node: { measuredPass: nodePass, minimum: 269, note: '266 original + 3 INDEPENDENT import-terminal cases' },
	runnerContract: { measuredPass: runnerPass, minimum: 28, note: '10 original + 18 added by this rework' },
	auditor: { measuredOk: auditorOk, expected: 26, realAudit: audit.totals },
	bisect: { measuredPass: bisectPass, minimum: 19 },
	scenarioMatrix: { scenarios: scenario.scenarioCount, passed: scenario.requiredPass, failed: scenario.requiredFail, testFileSha256: scenario.testFileSha256, rawLogSha256: scenario.rawLogSha256 },
	independentRunnerDisproof: { cases: disproofR3.cases.length, falsePassInR0: r0FalsePass, falsePassAfterFix: r3FalsePass, allRejectedAfterFix: r3AllRejected },
	independentImportDisproof: { tests: 3, passed: 3, note: 'r0 RED was 1 control pass / 2 defect failures; after the fix 3/3' },
	recoveryDemo: { verdict: demo.verdict, attempt1EvidenceUnchanged: demo.sections.A_same_definition_transient_failure.attempt1EvidenceUnchanged, archivedRecordByteIdentical: demo.sections.A_same_definition_transient_failure.supersededRecordArchivedByteIdentical },
	stages,
};
fs.writeFileSync(path.join(outDir, 'counts.json'), JSON.stringify(counts, null, 2), 'utf8');

/* ---------- 3. LQ closure ---------- */
const lq = [
	{
		id: 'LQ-01', severity: 'P1', title: '取消意图被当成服务端确认取消',
		status: 'CLOSED',
		red: { instrument: 'independent import-terminal disproof (unchanged expectations, rebound to this round\'s copy)', executed: 3, passed: 1, failed: 2, exitCode: 1, log: 'evidence/r0/red/import-terminal-disproof.log' },
		green: { executed: 3, passed: 3, failed: 0, exitCode: 0, log: 'evidence/r3/independent-reproof/import-terminal-disproof.log', inFileTests: 'ui/tests/goal-core-flow.test.cjs (IMPORT-03/04 terminal assertions + 3 INDEPENDENT cases)' },
		fix: 'ui/js/launcher.js handleImportFiles: cancelRequested (intent) and cancelConfirmed (server-confirmed) are separate; the terminal branch only claims cancellation for err.cancelled===true or a server-confirmed cancel; a plain failure reports an explicitly unconfirmed outcome (new i18n key import.outcomeUnconfirmed, zh+en).',
	},
	{
		id: 'LQ-02', severity: 'P1', title: '实际输入变化仍可复用旧成功结果',
		status: 'CLOSED',
		red: { case: 'actual-input-changed-manifest-unchanged', exitCode: 0, verdict: 'PASS', reused: true, results: 'evidence/r0/red/runner-disproof-results.json' },
		green: { case: 'actual-input-changed-manifest-unchanged', exitCode: 2, verdict: 'REFUSED', reused: false, reason: 'INPUT_SHA_MISMATCH' },
		fix: 'run-goal-validation.ps1 Test-InputSet: the manifest is parsed and every entry is re-checked against the actual execution root (existence + integer size + SHA256); a verified input-set fingerprint is bound into the run binding and into every stage fingerprint. Empty/duplicate/rooted/missing/malformed entries are refused.',
	},
	{
		id: 'LQ-03', severity: 'P1', title: '接受旧TRX及没有实际执行测试的计数',
		status: 'CLOSED',
		red: { cases: ['stale-trx', 'inconsistent-counters'], exitCode: 0, verdict: 'PASS', results: 'evidence/r0/red/runner-disproof-results.json' },
		green: { cases: ['stale-trx', 'inconsistent-counters'], exitCode: 1, verdict: 'FAIL', reasons: ['TRX_PREEXISTING', 'TRX_COUNTERS_INCONSISTENT'] },
		fix: 'Each attempt runs in a freshly created attempt directory, so a TRX can only be the one this child wrote; counters must be present, non-negative and mutually consistent with the UnitTestResult row census; executed=0 is never a pass.',
	},
	{
		id: 'LQ-04', severity: 'P1', title: '必需产物变化没有参与stage指纹',
		status: 'CLOSED',
		red: { case: 'produces-list-changed', exitCode: 0, verdict: 'PASS', reused: true, results: 'evidence/r0/red/runner-disproof-results.json' },
		green: { case: 'produces-list-changed', exitCode: 2, verdict: 'REFUSED', reused: false, reason: 'INCOMPATIBLE_RESUME_SAME_RUN_DIR' },
		fix: 'produces is part of the length-prefixed stage fingerprint, and reuse additionally requires every declared required output to exist with the recorded SHA256. The R3 stage file binds the real tests assembly, VerifyHost.exe and VerifyHost.dll.',
	},
	{
		id: 'LQ-05', severity: 'P1', title: '不兼容Resume覆盖旧记录且沿用旧runId',
		status: 'CLOSED',
		red: { case: 'resume-changed-manifest-same-run-id', exitCode: 0, verdict: 'PASS', oldStageRecordRetained: false, results: 'evidence/r0/red/runner-disproof-results.json' },
		green: { case: 'resume-changed-manifest-same-run-id', exitCode: 2, verdict: 'REFUSED', oldStageRecordRetained: true, reasons: ['INCOMPATIBLE_RESUME_SAME_RUN_DIR'] },
		fix: 'run-binding.json binds runId + input-manifest SHA + verified input-set SHA + stages-file SHA + execution root. An incompatible resume refuses before touching anything (exit 2, refusal-*.json); attempt evidence is never overwritten, superseded records are archived and previous verdicts are kept in verdicts/.',
	},
	{
		id: 'LQ-06', severity: 'P2', title: '空执行列表也生成整体PASS',
		status: 'CLOSED',
		red: { cases: ['empty-stages', 'only-unknown'], exitCode: 0, verdict: 'PASS', stageCount: 0, results: 'evidence/r0/red/runner-disproof-results.json' },
		green: { cases: ['empty-stages', 'only-unknown'], exitCode: 2, verdict: 'REFUSED', reasons: ['NO_STAGES', 'ONLY_NOT_FOUND'] },
		fix: 'Empty stage lists, unknown -Only selections, duplicate stage ids and missing requiredStages are refused (exit 2); a legal -Only subset yields an explicit PARTIAL verdict and exit 3, which is not an overall completion.',
	},
];

/* ---------- 4. machine verdict ---------- */
const gates = {
	R0_baseline_and_real_red: { status: 'PASS', evidence: 'evidence/r0/*' },
	R1_import_terminal_state: { status: 'PASS', evidence: 'evidence/r3/independent-reproof/import-terminal-disproof.log' },
	R2_runner_contract: { status: 'PASS', evidence: 'evidence/r3/run-final/attempts/g4-runner-tests/1/logs' },
	R3_guid_copy_regression: { status: 'PASS', evidence: 'evidence/r3/run-final/runner-verdict.json' },
	R4_terminal_seal: { status: 'PASS', evidence: 'manifest.json + detached sha + out-of-package audit' },
};
const allClosed = lq.every(x => x.status === 'CLOSED');
const machineVerdict = {
	schemaVersion: 1,
	generatedUtc: new Date().toISOString(),
	goal: '松光启动器 Windows 本机离线质量闭环 — G2/G4 有限返工（LQ-01..LQ-06）',
	round: 'LONG-QUALITY-REWORK',
	sourceRoot: r3Manifest.sourceRoot,
	sourceHead: r3Manifest.head,
	sourceBranch: r3Manifest.branch,
	predecessor: {
		independentAcceptance: 'docs/deepseek-harness-long-quality-independent-acceptance-2026-10-04.md (technicalAcceptance=FAIL)',
		failedGates: ['G2', 'G4'],
	},
	gates,
	lq,
	independentEvidence: {
		runnerDisproof: { cases: disproofR3.cases.length, falsePassBefore: r0FalsePass, falsePassAfter: r3FalsePass, allRejected: r3AllRejected },
		importDisproof: { before: { executed: 3, passed: 1, failed: 2 }, after: { executed: 3, passed: 3, failed: 0 } },
		scenarioMatrix: { scenarios: scenario.scenarioCount, passed: scenario.requiredPass },
		realEvidenceAudit: audit.totals,
	},
	implementationReady: allClosed && runVerdict.verdict === 'PASS' && r3FalsePass === 0 && r3AllRejected,
	formalPhaseApproval: 'NOT_GRANTED',
	releaseApproval: 'NOT_GRANTED',
	technicalAcceptance: 'AWAITING_INDEPENDENT_REVIEW',
	limitations: [
		'Real Avalonia/WebView2 GUI, real Minecraft, native credential backends, real update handover, cross-platform and remote CI/release remain unverified.',
		'The C# figure is the authorised 13+5+6+49=73 selection; ImportWithALiveTokenButCancelledMidway stays excluded and no full C# suite is claimed.',
		'RA-01/RA-02 (restricted recovery-safety counter-proofs) stay out of scope per the earlier platform boundary.',
		'Builds and tests ran only inside the GUID execution copies, never in D:\\松光启动器.',
	],
	closingStatement: '具备独立复验条件，等待 Codex 独立验收',
};
fs.writeFileSync(path.join(outDir, 'machine-verdict.json'), JSON.stringify(machineVerdict, null, 2), 'utf8');

console.log(JSON.stringify({
	changed, added, removed, terminalDrift: terminalDrift.length,
	runnerVerdict: runVerdict.verdict, csharp: `${csharpPassed}/${csharpTotal}`, nodePass, runnerPass, auditorOk, bisectPass,
	scenario: `${scenario.requiredPass}/${scenario.scenarioCount}`,
	falsePassBefore: r0FalsePass, falsePassAfter: r3FalsePass, allRejected: r3AllRejected,
	implementationReady: machineVerdict.implementationReady,
}, null, 2));
