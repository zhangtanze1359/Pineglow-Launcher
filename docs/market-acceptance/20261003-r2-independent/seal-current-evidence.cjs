'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = __dirname;
const sha = buffer => crypto.createHash('sha256').update(buffer).digest('hex').toUpperCase();
const manifestPath = path.join(root, 'manifest.json');
const detachedPath = path.join(root, 'manifest.sha256');
if (fs.existsSync(manifestPath) || fs.existsSync(detachedPath)) throw new Error('Evidence already sealed; do not overwrite.');
const files = [];
function visit(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a,b)=>a.name.localeCompare(b.name))) {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) visit(file);
    else if (entry.isFile()) {
      const rel = path.relative(root, file).split(path.sep).join('/');
      const bytes = fs.readFileSync(file);
      files.push({path:rel, sizeBytes:bytes.length, sha256:sha(bytes)});
    } else throw new Error('Unsupported file type in evidence: ' + file);
  }
}
visit(root);
const manifest = {runId:path.basename(root), createdUtc:new Date().toISOString(), kind:'independent-acceptance-evidence', files};
const bytes = Buffer.from(JSON.stringify(manifest,null,2) + '\n');
fs.writeFileSync(manifestPath, bytes, {flag:'wx'});
fs.writeFileSync(detachedPath, sha(bytes) + '  manifest.json\n', {flag:'wx'});
const mismatches = files.filter(f => {
  const b = fs.readFileSync(path.join(root,f.path));
  return b.length !== f.sizeBytes || sha(b) !== f.sha256;
});
const summary = {checked:files.length, mismatches, detachedMatches:fs.readFileSync(detachedPath,'utf8').split(/\s+/)[0]===sha(fs.readFileSync(manifestPath)), checkedUtc:new Date().toISOString()};
const summaryPath = path.join(path.dirname(root),path.basename(root)+'-verification.json');
fs.writeFileSync(summaryPath, JSON.stringify(summary,null,2)+'\n', {flag:'wx'});
console.log(JSON.stringify({checked:files.length,mismatches:mismatches.length,detachedMatches:summary.detachedMatches,summaryPath}));
if (mismatches.length || !summary.detachedMatches) process.exitCode = 1;
