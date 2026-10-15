'use strict';
/*
 * rebind-inputs.cjs — rebind an evidence-side input list to THIS round's GUID execution copy
 * and re-verify every entry (existence + sizeBytes + SHA256) against the copy.
 *
 * Usage: node rebind-inputs.cjs <inputsJsonPath> <sourceRoot> <executionRoot>
 * Rewrites the file in place (sourceRoot/executionRoot) and writes <inputsJsonPath>.rebind.json
 * with per-entry verification; exit 1 if any entry mismatches.
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const [inputsJson, sourceRoot, executionRoot] = process.argv.slice(2);
if (!inputsJson || !sourceRoot || !executionRoot) {
	console.error('usage: node rebind-inputs.cjs <inputsJson> <sourceRoot> <executionRoot>');
	process.exit(2);
}
const doc = JSON.parse(fs.readFileSync(inputsJson, 'utf8'));
const verify = [];
let mismatched = 0;
for (const e of (doc.inputs || [])) {
	const p = path.join(executionRoot, e.path.split('/').join(path.sep));
	const row = { path: e.path, expectedSha256: e.sha256, expectedSize: e.sizeBytes, exists: false, ok: false };
	if (fs.existsSync(p)) {
		row.exists = true;
		const buf = fs.readFileSync(p);
		row.actualSize = buf.length;
		row.actualSha256 = crypto.createHash('sha256').update(buf).digest('hex').toUpperCase();
		row.ok = row.actualSha256 === String(e.sha256).toUpperCase() && row.actualSize === e.sizeBytes;
	}
	if (!row.ok) mismatched++;
	verify.push(row);
}
doc.sourceRoot = sourceRoot;
doc.executionRoot = executionRoot;
fs.writeFileSync(inputsJson, JSON.stringify(doc, null, 2), 'utf8');
fs.writeFileSync(inputsJson + '.rebind.json', JSON.stringify({
	reboundUtc: new Date().toISOString(),
	sourceRoot, executionRoot,
	count: verify.length, mismatched,
	entries: verify,
}, null, 2), 'utf8');
console.log(JSON.stringify({ count: verify.length, mismatched, executionRoot }));
process.exit(mismatched === 0 ? 0 : 1);
