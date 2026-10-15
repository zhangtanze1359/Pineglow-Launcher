'use strict';
/*
 * rebind-root.cjs — point a copied Node test's ROOT constant at this round's execution copy.
 * Usage: node rebind-root.cjs <testFile> <executionRoot>
 * Fails loudly if the ROOT constant is not found (so a silently-unbound copy cannot pass).
 */
const fs = require('node:fs');
const [testFile, executionRoot] = process.argv.slice(2);
if (!testFile || !executionRoot) { console.error('usage: node rebind-root.cjs <testFile> <executionRoot>'); process.exit(2); }
let s = fs.readFileSync(testFile, 'utf8');
const re = /const ROOT = "[^"]*";/;
if (!re.test(s)) { console.error('ROOT constant not found in ' + testFile); process.exit(1); }
const before = s;
s = s.replace(re, 'const ROOT = ' + JSON.stringify(executionRoot) + ';');
fs.writeFileSync(testFile, s, 'utf8');
console.log(JSON.stringify({ file: testFile, rebound: before !== s, executionRoot }));
