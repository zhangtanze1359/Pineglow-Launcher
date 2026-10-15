'use strict';
/*
 * build-scenario-matrix.cjs — R3 scenario matrix for the six ordinary user flows.
 *
 * Rebuilt from the sealed g2/scenario-matrix.json, with the two corrections the independent review
 * demanded:
 *   * IMPORT-03 / IMPORT-04 no longer register "the test title passed". Each now requires BOTH the
 *     in-file scenario test AND the INDEPENDENT terminal-state proof extracted from the independent
 *     review, and both assertions are taken AFTER the import request settles.
 *   * every scenario is bound to the final test-file SHA256 and to the raw TAP log of this round.
 *
 * Usage: node build-scenario-matrix.cjs <executionRoot> <outDir>
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');

const [executionRoot, outDir] = process.argv.slice(2);
if (!executionRoot || !outDir) { console.error('usage: node build-scenario-matrix.cjs <executionRoot> <outDir>'); process.exit(2); }
fs.mkdirSync(outDir, { recursive: true });

const TEST_REL = 'ui/tests/goal-core-flow.test.cjs';
const testFile = path.join(executionRoot, TEST_REL.split('/').join(path.sep));
const testFileSha256 = crypto.createHash('sha256').update(fs.readFileSync(testFile)).digest('hex').toUpperCase();

const logPath = path.join(outDir, 'node-goal-core-flow.log');
const proc = spawnSync(process.execPath, ['--test', '--test-reporter=tap', TEST_REL],
	{ cwd: executionRoot, encoding: 'utf8', timeout: 300000 });
fs.writeFileSync(logPath, (proc.stdout || '') + (proc.stderr || ''), 'utf8');
fs.writeFileSync(path.join(outDir, 'node-goal-core-flow.result.json'),
	JSON.stringify({ exitCode: proc.status, testFile: TEST_REL, testFileSha256, log: path.basename(logPath) }, null, 2), 'utf8');

const tap = proc.stdout || '';
const outcomes = new Map();
for (const m of tap.matchAll(/^(not ok|ok) \d+ - (.*)$/gm)) {
	outcomes.set(m[2].trim(), m[1] === 'ok');
}
const tally = m => (tap.match(new RegExp('^# ' + m + ' (\\d+)$', 'gm')) || []).length;
const number = key => { const r = tap.match(new RegExp('^# ' + key + ' (\\d+)$', 'm')); return r ? Number(r[1]) : null; };

const SCENARIOS = [
	{ id: 'INSTANCE-01', entry: "openInstanceMenu -> '#mDel' with confirm=false", expected: 'cancelling the confirmation issues no delete request at all', tests: ['INSTANCE-01 cancel does not issue a delete request'] },
	{ id: 'INSTANCE-02', entry: "openInstanceMenu -> '#mDel' with confirm=true", expected: 'the instance disappears from the re-rendered list and the others stay', tests: ['INSTANCE-02 confirm removes the instance from the list and re-renders'] },
	{ id: 'INSTANCE-03', entry: 'removing the currently selected instance', expected: 'the stale selection id does not survive; selection falls back to a surviving instance', tests: ['INSTANCE-03 removing the selected instance clears the stale selection'] },
	{ id: 'INSTANCE-04', entry: "repeated '#mDel' clicks while the delete request is in flight", expected: 'exactly one delete request is issued', tests: ['INSTANCE-04 repeated clicks during the request issue one request'] },
	{ id: 'RECOVERY-01', entry: 'openRecoveryPanel with listRecoveries rejecting', expected: 'an explicit error and a retry action; no restore action', tests: ['RECOVERY-01 recovery query failure shows an error and a retry action'] },
	{ id: 'RECOVERY-02', entry: 'openRecoveryPanel with an empty recovery list', expected: 'the empty state is shown and no restore action exists', tests: ['RECOVERY-02 empty recovery list shows the empty state'] },
	{ id: 'RECOVERY-03', entry: 'restore a quarantined record (ok=true)', expected: 'success is reported and the panel is refreshed', tests: ['RECOVERY-03 normal restore reports success and refreshes the panel'] },
	{ id: 'RECOVERY-04', entry: 'restore result with per-file states', expected: 'each file and its state/error is listed', tests: ['RECOVERY-04 partial results list each file with its state'] },
	{ id: 'RECOVERY-05', entry: 'restore with conflicts=2 (ok=false)', expected: 'the conflict notice is shown and no success/undo is claimed', tests: ['RECOVERY-05 conflict result never claims a successful overwrite'] },
	{ id: 'RECOVERY-06', entry: 'restore request in flight, then rejected', expected: 'the button is disabled in flight and re-enabled on failure, with the error reported', tests: ['RECOVERY-06 submit disables the button and failure restores it'] },
	{ id: 'BISECT-01', entry: 'openModBisect with no pending session', expected: 'the normal start entry and the safety guidance are shown', tests: ['BISECT-01 no pending session shows the normal start entry'] },
	{ id: 'BISECT-02', entry: 'openModBisect with a pending session (canContinue true/false)', expected: 'restore/continue entries appear and continue is disabled when the server says it cannot continue', tests: ['BISECT-02 pending session offers restore or continue'] },
	{ id: 'BISECT-03', entry: 'bisectPending rejecting', expected: 'an explicit error with retry; the normal start button must not be offered while the state is unknown', tests: ['BISECT-03 pending query failure shows an explicit error and retry'] },
	{ id: 'BISECT-04', entry: 'drawBisect with needsRecovery and a file-lock failure', expected: 'the retry-recovery action stays and no all-complete claim is made', tests: ['BISECT-04 file-lock failure keeps the needs-recovery state'] },
	{ id: 'BISECT-05', entry: 'bisectGiveUp failing with a structured recovery body', expected: 'the panel stays in needs-recovery and never shows all-complete', tests: ['BISECT-05 failed recovery never shows all-complete'] },
	{ id: 'IMPORT-01', entry: 'handleImportFiles -> SG.api.importInstance(fileName, base64, operationId)', expected: 'a non-empty operationId is bound to the request before any cancel entry is offered', tests: ['IMPORT-01 operationId is bound before cancel is offered'] },
	{ id: 'IMPORT-02', entry: 'the import request rejects with err.cancelled=true', expected: 'the cancelled wording is shown and it is not reported as a failure', tests: ['IMPORT-02 cancel before acceptance reports the cancelled result'] },
	{
		id: 'IMPORT-03',
		entry: "handleImportFiles: cancelImport resolves {pendingCancel:true, ok:false}, THEN importInstance rejects for an ordinary reason; the assertion is taken AFTER 'await running'",
		expected: 'the terminal state is a failure that explicitly says the outcome is unconfirmed; it must never say the import was cancelled and must never claim no instance was created',
		tests: [
			'IMPORT-03 cancel after acceptance reports the real server result',
			'INDEPENDENT IMPORT-03 pending cancel followed by ordinary import failure must keep outcome unconfirmed',
		],
	},
	{
		id: 'IMPORT-04',
		entry: "handleImportFiles: cancelImport REJECTS (unknown operation), THEN importInstance rejects for an ordinary reason; the assertion is taken AFTER 'await running'",
		expected: 'the terminal state is a failure that explicitly says the outcome is unconfirmed; the cancel intent alone must never be upgraded to a confirmed cancellation',
		tests: [
			'IMPORT-04 unknown or failed cancel never claims the import was cancelled',
			'INDEPENDENT IMPORT-04 failed cancel followed by ordinary import failure must not claim cancelled',
		],
	},
	{ id: 'IMPORT-05', entry: 'handleImportFiles: the cancel button is clicked while the import request is in flight', expected: 'cancelImport is called with exactly the operationId the import request was started with', tests: ['INDEPENDENT IMPORT-01 cancel uses the active import operationId'] },
	{ id: 'DIAGNOSIS-01', entry: 'api.diagnoseInstance with localOnly=true', expected: 'the local-only flag reaches the server with confirmAiSend=false and no API key', tests: ['DIAGNOSIS-01 local diagnosis works without an API key'] },
	{ id: 'DIAGNOSIS-02', entry: 'openDiagnoseHistory with an empty history', expected: 'the empty state is shown and no export action exists', tests: ['DIAGNOSIS-02 empty history shows the empty state'] },
	{ id: 'DIAGNOSIS-03', entry: 'diagnoseHistory rejecting', expected: 'an explicit error with retry and the real message', tests: ['DIAGNOSIS-03 history query failure shows an error and retry'] },
	{ id: 'DIAGNOSIS-04', entry: 'openDiagnoseHistory with a non-empty history and export', expected: 'the report row and export action render; original evidence is excluded by default', tests: ['DIAGNOSIS-04 a non-empty history renders and export states the evidence scope'] },
	{ id: 'UPDATE-01', entry: 'initUpdateSection with an idle status and no previous apply result', expected: 'no false alarm and no success claim', tests: ['UPDATE-01 no previous apply result shows no false alarm'] },
	{ id: 'UPDATE-02', entry: 'initUpdateSection with lastApplyResult.applied=false', expected: 'a warning naming the failed version and the still-old hint', tests: ['UPDATE-02 a previous non-applied update is reported as still old'] },
	{ id: 'UPDATE-03', entry: 'api.getUpdateStatus with missing compatibility fields', expected: 'defaults are applied instead of crashing', tests: ['UPDATE-03 missing compatibility fields do not crash'] },
	{ id: 'UPDATE-04', entry: 'getUpdateStatus rejecting', expected: 'no success claim and no trust block', tests: ['UPDATE-04 status query failure never shows update success'] },
];

const scenarios = SCENARIOS.map(s => {
	const rows = s.tests.map(t => ({ testId: t, testInFile: outcomes.has(t), testPassed: outcomes.get(t) === true }));
	const missing = rows.filter(r => !r.testInFile).map(r => r.testId);
	const failed = rows.filter(r => r.testInFile && !r.testPassed).map(r => r.testId);
	return {
		id: s.id,
		required: true,
		layer: 'node-behavior',
		entry: s.entry,
		expected: s.expected,
		tests: rows,
		missingTests: missing,
		failedTests: failed,
		verdict: (missing.length === 0 && failed.length === 0) ? 'PASS' : 'FAIL',
		evidence: 'node-goal-core-flow.log',
	};
});

const requiredPass = scenarios.filter(s => s.verdict === 'PASS').length;
const matrix = {
	schemaVersion: 2,
	generatedUtc: new Date().toISOString(),
	executionRoot,
	testFile: TEST_REL,
	testFileSha256,
	rawLog: 'node-goal-core-flow.log',
	rawLogSha256: crypto.createHash('sha256').update(fs.readFileSync(logPath)).digest('hex').toUpperCase(),
	nodeTallies: { tests: number('tests'), pass: number('pass'), fail: number('fail'), skipped: number('skipped'), exitCode: proc.status },
	scenarioCount: scenarios.length,
	requiredScenarioCount: scenarios.length,
	requiredPass,
	requiredFail: scenarios.length - requiredPass,
	scenarios,
};
fs.writeFileSync(path.join(outDir, 'scenario-matrix.json'), JSON.stringify(matrix, null, 2), 'utf8');
console.log(JSON.stringify({
	testFileSha256, nodeTallies: matrix.nodeTallies,
	scenarioCount: matrix.scenarioCount, requiredPass, requiredFail: matrix.requiredFail,
	failing: scenarios.filter(s => s.verdict !== 'PASS').map(s => s.id),
}, null, 2));
process.exit(matrix.requiredFail === 0 && proc.status === 0 ? 0 : 1);
