
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
