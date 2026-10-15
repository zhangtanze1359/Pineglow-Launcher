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
if (trx && trx !== '-') {
	const p = path.isAbsolute(trx) ? trx : path.resolve(process.cwd(), trx);
	fs.mkdirSync(path.dirname(p), { recursive: true });
	fs.writeFileSync(p, '<?xml version="1.0" encoding="UTF-8"?>\n' +
		'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">' +
		'<ResultSummary><Counters total="' + total + '" executed="' + (total - skipped) +
		'" passed="' + passed + '" failed="' + failed + '" notExecuted="' + skipped +
		'" /></ResultSummary></TestRun>');
}
console.log('synthetic stage done: exit=' + exitCode + ' total=' + total);
process.exit(exitCode);