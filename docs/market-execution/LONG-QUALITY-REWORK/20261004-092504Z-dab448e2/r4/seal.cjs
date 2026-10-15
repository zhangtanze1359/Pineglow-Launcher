'use strict';
/*
 * seal.cjs — seal the delivery package and audit it from OUTSIDE.
 *
 * Order (nothing inside the package may be written after step 2):
 *   1. list every package file (except manifest.json itself) with size + SHA256 -> manifest.json
 *   2. write manifest.json INTO the package
 *   3. write the detached digest OUTSIDE the package
 *   4. out-of-package audit: verify every entry, detect unlisted files, check the detached digest
 *   5. post-seal independent re-verification: a second, separate pass over the same manifest
 *
 * Usage: node seal.cjs <evidenceRoot>
 */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const root = process.argv[2];
if (!root) { console.error('usage: node seal.cjs <evidenceRoot>'); process.exit(2); }
const RUN_ID = '20261004-092504Z-dab448e2-rework';
const pkg = path.join(root, 'r4', 'FINAL-LOCAL-QUALITY', RUN_ID);
const outDir = path.join(root, 'r4');
const sha256 = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex').toUpperCase();

function walk(dir, base) {
	const out = [];
	for (const name of fs.readdirSync(dir).sort()) {
		const abs = path.join(dir, name);
		const rel = base ? base + '/' + name : name;
		const st = fs.lstatSync(abs);
		if (st.isDirectory()) out.push(...walk(abs, rel));
		else if (st.isFile()) out.push({ rel, abs, sizeBytes: st.size });
		else throw new Error('unexpected non-regular file in package: ' + rel);
	}
	return out;
}

/* ---- 1. manifest ---- */
const all = walk(pkg, '');
const manifestPath = path.join(pkg, 'manifest.json');
if (fs.existsSync(manifestPath)) { console.error('manifest.json already exists: package is already sealed'); process.exit(1); }
const entries = all
	.filter(e => e.rel !== 'manifest.json')
	.map(e => ({ path: e.rel, sizeBytes: e.sizeBytes, sha256: sha256(e.abs) }));
const manifest = {
	schemaVersion: 1,
	scope: 'windows-local-offline-quality',
	round: 'LONG-QUALITY-REWORK',
	runId: RUN_ID,
	generatedUtc: new Date().toISOString(),
	sourceRoot: 'D:\\松光启动器',
	sourceHead: 'f145d14eb26b0b7459a28491e764ac8c573085d3',
	note: [
		'manifest.json lists every file in this package except itself (a file cannot contain its own digest).',
		'The authoritative digest of manifest.json is the detached file recorded OUTSIDE the package:',
		'  ../' + RUN_ID + '.manifest.sha256',
		'Package-internal references are portable relative paths only.',
	],
	count: entries.length,
	totalBytes: entries.reduce((a, e) => a + e.sizeBytes, 0),
	entries,
};

/* ---- 2. write the manifest inside the package (last write to the package) ---- */
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
const manifestSha256 = sha256(manifestPath);

/* ---- 3. detached digest outside the package ---- */
const detachedPath = path.join(outDir, RUN_ID + '.manifest.sha256');
fs.writeFileSync(detachedPath, manifestSha256 + '  manifest.json\n', 'utf8');

/* ---- 4. out-of-package audit ---- */
function auditPackage(label) {
	const doc = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
	const failures = [];
	const verified = [];
	for (const e of doc.entries) {
		const abs = path.join(pkg, e.path.split('/').join(path.sep));
		if (!fs.existsSync(abs)) { failures.push({ path: e.path, reason: 'MISSING' }); continue; }
		const st = fs.statSync(abs);
		if (st.size !== e.sizeBytes) { failures.push({ path: e.path, reason: 'SIZE_MISMATCH', expected: e.sizeBytes, actual: st.size }); continue; }
		const actual = sha256(abs);
		if (actual !== e.sha256) { failures.push({ path: e.path, reason: 'SHA_MISMATCH', expected: e.sha256, actual }); continue; }
		verified.push(e.path);
	}
	const present = walk(pkg, '').map(x => x.rel);
	const listed = new Set(doc.entries.map(e => e.path));
	const unlisted = present.filter(x => x !== 'manifest.json' && !listed.has(x));
	const detachedText = fs.readFileSync(detachedPath, 'utf8').trim();
	const detachedValue = detachedText.split(/\s+/)[0];
	const detachedMatches = detachedValue === sha256(manifestPath);
	return {
		label,
		generatedUtc: new Date().toISOString(),
		package: RUN_ID,
		packageDir: pkg,
		manifestSha256: sha256(manifestPath),
		detachedFile: detachedPath,
		detachedMatches,
		manifestCount: doc.entries.length,
		filesPresent: present.length,
		verified: verified.length,
		failures,
		unlisted,
		result: (failures.length === 0 && unlisted.length === 0 && detachedMatches) ? 'PASS' : 'FAIL',
	};
}
const audit = auditPackage('out-of-package-audit');
fs.writeFileSync(path.join(outDir, 'out-of-package-audit.json'), JSON.stringify(audit, null, 2), 'utf8');

/* ---- 5. post-seal independent re-verification (separate pass) ---- */
const reverify = auditPackage('post-seal-reverify');
reverify.note = 'Second, separate pass over the same manifest after the audit file was written outside the package; no package file was touched between the two passes.';
fs.writeFileSync(path.join(outDir, 'post-seal-reverify.json'), JSON.stringify(reverify, null, 2), 'utf8');

console.log(JSON.stringify({
	package: pkg,
	manifestCount: manifest.count,
	manifestTotalBytes: manifest.totalBytes,
	manifestSha256,
	detached: detachedPath,
	audit: audit.result,
	reverify: reverify.result,
	failures: audit.failures.length,
	unlisted: audit.unlisted.length,
	detachedMatches: audit.detachedMatches,
}, null, 2));
process.exit(audit.result === 'PASS' && reverify.result === 'PASS' ? 0 : 1);
