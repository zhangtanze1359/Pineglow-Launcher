"""Independent ordinary evidence-completeness checks; only GUID temporary fixtures mutate."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime, timezone

PACKAGE = pathlib.Path(r'C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-execution\FINAL-REMEDIATION-R2\20261003-071724Z')
EVIDENCE = pathlib.Path(__file__).parent
BASE = pathlib.Path(tempfile.mkdtemp(prefix='pineglow-audit-coverage-'))
RESULTS = []

def digest(data):
    return hashlib.sha256(data).hexdigest().upper()

def package(root, name='RUN-OK', empty=False, no_size=False, duplicate=False):
    p = root / name
    p.mkdir(parents=True)
    files = []
    if not empty:
        data = b'ordinary evidence control'
        (p / 'evidence.txt').write_bytes(data)
        entry = {'path': 'evidence.txt', 'sha256': digest(data)}
        if not no_size:
            entry['sizeBytes'] = len(data)
        files.append(entry)
        if duplicate:
            files.append(dict(entry))
    manifest = json.dumps({'runId': name, 'files': files}).encode()
    (p / 'manifest.json').write_bytes(manifest)
    (p / 'manifest.sha256').write_text(digest(manifest) + '  manifest.json\n', encoding='ascii')
    return p

def check(name, roots, expect_failure=True):
    runner = BASE / ('runner-' + name)
    runner.mkdir()
    auditor = runner / 'audit-all-manifests-r2.py'
    shutil.copyfile(PACKAGE / auditor.name, auditor)
    (runner / 'audit-roots.json').write_text(json.dumps({'roots': [str(r) for r in roots]}), encoding='utf-8')
    (runner / 'historical-exceptions.json').write_text('{"exceptions": []}', encoding='utf-8')
    output = EVIDENCE / ('coverage-' + name + '.json')
    command = [sys.executable, str(auditor), '', str(output)]
    start = datetime.now(timezone.utc).isoformat()
    proc = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace')
    ok = (proc.returncode != 0) if expect_failure else proc.returncode == 0
    record = {'id': name, 'expected': 'nonzero' if expect_failure else 'zero', 'actualExit': proc.returncode,
              'status': 'PASS' if ok else 'FAIL', 'command': command, 'cwd': str(pathlib.Path.cwd()),
              'startUtc': start, 'endUtc': datetime.now(timezone.utc).isoformat(),
              'stdout': proc.stdout, 'stderr': proc.stderr, 'roots': [str(r) for r in roots]}
    RESULTS.append(record)
    print(name, record['status'], 'expected=' + record['expected'], 'exit=' + str(proc.returncode))

good = BASE / 'good'
package(good)
check('valid-control', [good], False)
check('required-root-missing', [good, BASE / 'missing-required-root'])
empty_root = BASE / 'empty-required-root'
empty_root.mkdir()
check('required-root-empty', [good, empty_root])
orphan = good / 'UNSEALED-RUN'
orphan.mkdir()
(orphan / 'results.log').write_text('unsealed ordinary run evidence', encoding='utf-8')
check('unsealed-sibling-package', [good])
empty_manifest = BASE / 'empty-manifest'
package(empty_manifest, empty=True)
check('empty-manifest', [empty_manifest])
no_size = BASE / 'size-missing'
package(no_size, no_size=True)
check('required-size-missing', [no_size])
duplicate = BASE / 'duplicate'
package(duplicate, duplicate=True)
check('duplicate-entry-control', [duplicate])
summary = {'fixtureRoot': str(BASE), 'auditorSource': str(PACKAGE / 'audit-all-manifests-r2.py'),
           'cases': RESULTS, 'failed': [r['id'] for r in RESULTS if r['status'] == 'FAIL']}
(EVIDENCE / 'audit-coverage-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'total': len(RESULTS), 'failed': summary['failed'], 'fixtureRoot': str(BASE)}))
sys.exit(1 if summary['failed'] else 0)
