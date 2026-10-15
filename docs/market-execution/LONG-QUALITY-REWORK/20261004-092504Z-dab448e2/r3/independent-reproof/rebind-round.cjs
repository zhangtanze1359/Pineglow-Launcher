'use strict';
/*
 * rebind-round.cjs — rebind the PRESERVED independent disproof instruments to this round.
 *
 * Two faithful adaptations, both recorded in the round checkpoint:
 *  1. independent-inputs.json is rebuilt from THIS round's input-manifest.json (path/size/sha of
 *     every verified input of the current execution copy). The expectations of the probes are not
 *     touched.
 *  2. runner-disproof.cjs's synthetic child TRX gains UnitTestResult rows that agree with its own
 *     counters. The new runner contract requires counters and result rows to describe the same
 *     population, so without this the CONTROL invocation (the first, non-resume run) would fail for
 *     a reason unrelated to the probe. No expectation string is modified.
 *
 * Usage: node rebind-round.cjs <independentInputsJson> <thisRoundInputManifest> <executionRoot> <runnerDisproofCjs>
 */
const fs = require('node:fs');
const path = require('node:path');

const [inputsJson, manifestPath, executionRoot, disproofPath] = process.argv.slice(2);
if (!inputsJson || !manifestPath || !executionRoot || !disproofPath) {
	console.error('usage: node rebind-round.cjs <inputsJson> <manifest> <executionRoot> <runnerDisproofCjs>');
	process.exit(2);
}

/* ---- 1. rebuild the verified input list from this round's manifest ---- */
const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const inputs = (manifest.inputs || []).map(e => ({ path: e.path, sha256: e.sha256, sizeBytes: e.sizeBytes }));
const doc = { sourceRoot: manifest.sourceRoot, executionRoot, inputs };
fs.writeFileSync(inputsJson, JSON.stringify(doc, null, 2), 'utf8');

const crypto = require('node:crypto');
let mismatched = 0;
for (const e of inputs) {
	const p = path.join(executionRoot, e.path.split('/').join(path.sep));
	if (!fs.existsSync(p)) { mismatched++; continue; }
	const buf = fs.readFileSync(p);
	const sha = crypto.createHash('sha256').update(buf).digest('hex').toUpperCase();
	if (sha !== String(e.sha256).toUpperCase() || buf.length !== e.sizeBytes) mismatched++;
}
fs.writeFileSync(inputsJson + '.rebind.json', JSON.stringify({
	reboundUtc: new Date().toISOString(), sourceRoot: manifest.sourceRoot, executionRoot,
	count: inputs.length, mismatched, originManifest: manifestPath,
}, null, 2), 'utf8');

/* ---- 2. make the synthetic child TRX respect the current row/counter contract ---- */
let src = fs.readFileSync(disproofPath, 'utf8');
const before = src;
src = src.replace(
	/const child=path\.join\(dir,'child\.cjs'\);fs\.writeFileSync\(child,`[^`]*`\);/,
	"const child=path.join(dir,'child.cjs');fs.writeFileSync(child,`const fs=require('fs');const arg=process.argv[2];" +
	"const rows=(p,f,s)=>{let o='',n=0;for(let i=0;i<p;i++)o+='<UnitTestResult testName=\"t'+(n++)+'\" outcome=\"Passed\" />';" +
	"for(let i=0;i<f;i++)o+='<UnitTestResult testName=\"t'+(n++)+'\" outcome=\"Failed\" />';" +
	"for(let i=0;i<s;i++)o+='<UnitTestResult testName=\"t'+(n++)+'\" outcome=\"NotExecuted\" />';return o;};" +
	"if(arg==='write')fs.writeFileSync('unit.trx','<TestRun><ResultSummary><Counters total=\"3\" executed=\"3\" passed=\"3\" failed=\"0\" notExecuted=\"0\"/></ResultSummary><Results>'+rows(3,0,0)+'</Results></TestRun>');" +
	"console.log('child ran');`);"
);
fs.writeFileSync(disproofPath, src, 'utf8');

console.log(JSON.stringify({
	inputsRebuilt: inputs.length, mismatched,
	childTrxPatched: before !== src,
}, null, 2));
process.exit(mismatched === 0 ? 0 : 1);
