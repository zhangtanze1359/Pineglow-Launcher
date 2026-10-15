'use strict';
/*
 * build-package.cjs — assemble the LONG-QUALITY-REWORK delivery package.
 * Copies only already-produced evidence into a portable, relative-path-only layout.
 *
 * Usage: node build-package.cjs <evidenceRoot>
 */
const fs = require('node:fs');
const path = require('node:path');

const root = process.argv[2];
if (!root) { console.error('usage: node build-package.cjs <evidenceRoot>'); process.exit(2); }
const p = (...a) => path.join(root, ...a);
const RUN_ID = '20261004-092504Z-dab448e2-rework';
const pkg = p('r4', 'FINAL-LOCAL-QUALITY', RUN_ID);

if (fs.existsSync(pkg)) { console.error('package already exists: ' + pkg); process.exit(1); }
fs.mkdirSync(pkg, { recursive: true });

const copies = [];
function put(src, destRel) {
	const dst = path.join(pkg, destRel.split('/').join(path.sep));
	fs.mkdirSync(path.dirname(dst), { recursive: true });
	fs.copyFileSync(src, dst);
	copies.push(destRel);
}
function putDir(srcDir, destRel, filter) {
	if (!fs.existsSync(srcDir)) return;
	for (const name of fs.readdirSync(srcDir)) {
		const s = path.join(srcDir, name);
		if (fs.statSync(s).isDirectory()) { putDir(s, destRel + '/' + name, filter); continue; }
		if (filter && !filter(name)) continue;
		put(s, destRel + '/' + name);
	}
}

/* ---- reports (staged in r4/report) ---- */
put(p('r4', 'report', 'README.md'), 'README.md');
put(p('r4', 'report', 'LIMITATIONS.md'), 'LIMITATIONS.md');
put(p('CHECKPOINT.md'), 'CHECKPOINT.md');
for (const f of ['R0-baseline-and-red.md', 'R1-import-terminal.md', 'R2-runner-contract.md', 'R3-regression.md', 'R4-delivery.md', 'LQ-01..LQ-06-closure.md']) {
	put(p('r4', 'report', f), 'report/' + f);
}

/* ---- machine verdict ---- */
for (const f of ['machine-verdict.json', 'counts.json', 'changed-files.json']) put(p('r4', 'verdict', f), 'verdict/' + f);
put(p('r3', 'scenario', 'scenario-matrix.json'), 'verdict/scenario-matrix.json');
put(p('r3', 'demo', 'recovery-demo.json'), 'verdict/recovery-demo.json');

/* ---- source / terminal ---- */
put(p('r0', 'input-manifest.json'), 'source/r0-input-manifest.json');
put(p('r3', 'g2', 'input-manifest.json'), 'source/r3-input-manifest.json');
put(p('r3', 'g2', 'execution-root.json'), 'source/execution-root.json');
put(p('r3', 'g2', 'copy-verification.json'), 'source/copy-verification.json');
put(p('r3', 'g2', 'git-head.txt'), 'source/r3-git-head.txt');
put(p('r4', 'terminal', 'source-after-manifest.json'), 'source/terminal-source-after-manifest.json');
put(p('r4', 'terminal', 'git-head.txt'), 'source/terminal-git-head.txt');
put(p('r4', 'terminal', 'source-status-terminal.txt'), 'source/terminal-source-status.txt');
put(p('r4', 'terminal', 'capture-summary-terminal.txt'), 'source/terminal-capture-summary.txt');

/* ---- runner + run package ---- */
put(p('r3', 'goal-stages.json'), 'runner/goal-stages.json');
put(p('r3', 'run-final', 'run-binding.json'), 'runner/run-binding.json');
put(p('r3', 'run-final', 'runner-verdict.json'), 'runner/runner-verdict.json');
put(p('r3', 'r3-runner.log'), 'runner/r3-runner.log');
put(p('r3', 'r3-runner-final.log'), 'runner/r3-runner-final.log');
put(p('r3', 'r3-runner-resume.log'), 'runner/r3-runner-resume.log');
putDir(p('r3', 'run-final', 'verdicts'), 'runner/archived-verdicts');
putDir(p('r3', 'run-final', 'stages'), 'runner/stages');
putDir(p('r3', 'run-final', 'attempts'), 'attempts', n => !/^refusal-/.test(n));
putDir(p('r3', 'run-final'), 'runner/refusals', n => /^refusal-/.test(n));

/* ---- independent instruments (including the rebinding helpers, so the rebinding is auditable) ---- */
putDir(p('r0', 'red'), 'independent/r0-red');
putDir(p('r3', 'independent-reproof'), 'independent/r3-reproof');

/* ---- audit + scenario ---- */
put(p('r3', 'audit-real.json'), 'audit/audit-real.json');
put(p('r3', 'audit-real.log'), 'audit/audit-real.log');
put(p('r3', 'scenario', 'node-goal-core-flow.log'), 'scenario/node-goal-core-flow.log');
put(p('r3', 'scenario', 'node-goal-core-flow.result.json'), 'scenario/node-goal-core-flow.result.json');
put(p('r3', 'scenario', 'build.log'), 'scenario/build.log');

/* ---- demo fixtures (the demonstration's own raw evidence; the sentinel flag is consumed) ---- */
put(p('r3', 'demo', 'recovery-demo.cjs'), 'demo/recovery-demo.cjs');
put(p('r3', 'demo', 'recovery-demo.json'), 'demo/recovery-demo.json');
put(p('r3', 'demo', 'recovery-demo.log'), 'demo/recovery-demo.log');
putDir(p('r3', 'demo', 'fixture'), 'demo/fixture', n => !/^make-it-fail/.test(n));

fs.writeFileSync(path.join(pkg, 'PACKAGE-CONTENTS.txt'),
	copies.slice().sort().join('\n') + '\n', 'utf8');

console.log(JSON.stringify({ package: pkg, runId: RUN_ID, files: copies.length + 1 }, null, 2));
