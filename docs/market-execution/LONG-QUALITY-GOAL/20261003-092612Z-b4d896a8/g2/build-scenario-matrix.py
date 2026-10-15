#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""build-scenario-matrix.py - G2 scenario matrix from the frozen requirements + real test run.

Reads  <evidenceRoot>/g0/requirements.json          (the 27 frozen scenarios)
       D:\\松光启动器\\ui\\tests\\goal-core-flow.test.cjs (the behaviour test file)
       <evidenceRoot>/g2/node-goal-core-flow.log    (the real node:test output)
and writes <evidenceRoot>/g2/scenario-matrix.json.

A scenario is PASS only when its frozen testId appears as a test name in the test file AND the
node run reports that test as passing (no "✖ <name>" line). Nothing is inferred from intent.
"""

import json
import pathlib
import re
import sys

TEST_FILE = pathlib.Path(r'D:\松光启动器\ui\tests\goal-core-flow.test.cjs')


def read_text_any(path):
    """PowerShell 5.1 redirection writes UTF-16LE; hand-written evidence is UTF-8. Accept both."""
    raw = pathlib.Path(path).read_bytes()
    for enc in ('utf-8-sig', 'utf-16', 'utf-8', 'cp936'):
        try:
            text = raw.decode(enc)
        except (UnicodeDecodeError, UnicodeError):
            continue
        if text.count('\u0000') == 0:
            return text
    return raw.decode('utf-8', 'replace')


def main(argv):
    if len(argv) < 2:
        raise SystemExit('usage: build-scenario-matrix.py <evidenceRoot>')
    root = pathlib.Path(argv[1])
    requirements = json.loads((root / 'g0' / 'requirements.json').read_text(encoding='utf-8-sig'))
    test_src = TEST_FILE.read_text(encoding='utf-8')
    log = read_text_any(root / 'g2' / 'node-goal-core-flow.log')

    declared = set(re.findall(r"^test\('(.+?)',", test_src, re.M))
    failing = set(re.findall(r'^✖ (.+?) \(', log, re.M))
    passing = set(re.findall(r'^✔ (.+?) \(', log, re.M))
    tallies = {}
    for key in ('tests', 'pass', 'fail', 'skipped'):
        m = re.search(r'^ℹ %s (\d+)' % key, log, re.M)
        tallies[key] = int(m.group(1)) if m else None

    scenarios = []
    for item in requirements['items']:
        test_id = item.get('testId') or ''
        in_file = test_id in declared
        verdict = 'PASS' if (in_file and test_id in passing) else ('FAIL' if in_file else 'MISSING')
        scenarios.append({
            'id': item['id'],
            'required': item.get('required', True),
            'layer': item['layer'],
            'entry': item['entry'],
            'expected': item['expected'],
            'testId': test_id,
            'testInFile': in_file,
            'testPassed': test_id in passing,
            'testFailed': test_id in failing,
            'verdict': verdict,
            'evidence': item.get('evidence'),
        })

    required = [s for s in scenarios if s['required']]
    matrix = {
        'schemaVersion': 1,
        'generatedUtc': __import__('datetime').datetime.now(
            __import__('datetime').timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'testFile': str(TEST_FILE),
        'testFileSha256': __import__('hashlib').sha256(TEST_FILE.read_bytes()).hexdigest().upper(),
        'rawLog': 'g2/node-goal-core-flow.log',
        'nodeTallies': tallies,
        'declaredTestNames': sorted(declared),
        'scenarioCount': len(scenarios),
        'requiredScenarioCount': len(required),
        'requiredPass': sum(1 for s in required if s['verdict'] == 'PASS'),
        'requiredFail': sum(1 for s in required if s['verdict'] != 'PASS'),
        'scenarios': scenarios,
    }
    out = root / 'g2' / 'scenario-matrix.json'
    with open(out, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(matrix, fh, ensure_ascii=False, indent=2)
        fh.write('\n')
    print(json.dumps({
        'scenarios': matrix['scenarioCount'],
        'requiredPass': matrix['requiredPass'],
        'requiredFail': matrix['requiredFail'],
        'nodeTallies': tallies,
    }, ensure_ascii=False))
    return 0 if matrix['requiredFail'] == 0 else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
