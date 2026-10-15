#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""red-old-auditor-coverage.py - preserve the RED that forced the new strict auditor.

For each coverage shape that the R2 auditor got wrong, this script builds a *fresh* synthetic
evidence tree under TEMP, runs the OLD auditor (read-only copy inside the R2 package) and the
NEW auditor on the same tree, and records both real exit codes.

Expected pairing (this is the point of the file):
    shape                              old auditor   new auditor
    required root missing              exit 0 (RED)  non-zero (GREEN)
    required root empty                exit 0 (RED)  non-zero (GREEN)
    unsealed sibling run package       exit 0 (RED)  non-zero (GREEN)
    empty new manifest                 exit 0 (RED)  non-zero (GREEN)
    entry without a size field         exit 0 (RED)  non-zero (GREEN)

The old auditor is only executed; it is never modified. Its own fixtures/outputs are untouched.
"""

import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

OLD_AUDITOR = pathlib.Path(
    r'C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-execution'
    r'\FINAL-REMEDIATION-R2\20261003-071724Z\audit-all-manifests-r2.py')
NEW_AUDITOR = pathlib.Path(r'D:\松光启动器\scripts\audit-goal-evidence.py')
OUT_DIR = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path(tempfile.gettempdir())


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def jwrite(path, obj):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(obj, fh, ensure_ascii=False, indent=2)
        fh.write('\n')


def package(root, rel, files, run_id=None, entries=None, detached=True):
    pkg = root / rel
    pkg.mkdir(parents=True, exist_ok=True)
    for name, data in files.items():
        target = pkg / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    if entries is None:
        entries = [{'path': n, 'sizeBytes': len(d), 'sha256': sha(d)} for n, d in sorted(files.items())]
    jwrite(pkg / 'manifest.json', {
        'schemaVersion': 1,
        'runId': run_id or pkg.name,
        'generatedUtc': '2026-10-03T09:40:00Z',
        'files': entries,
    })
    if detached:
        (pkg / 'manifest.sha256').write_text(
            sha((pkg / 'manifest.json').read_bytes()) + '\n', encoding='ascii')
    return pkg


def expected(roots):
    return {'schemaVersion': 1, 'scope': 'red-demo', 'generatedUtc': '2026-10-03T09:40:00Z',
            'roots': roots, 'exceptions': []}


def declare(rel, run_id=None, policy='strict-new'):
    return {'relativePath': rel, 'runId': run_id or pathlib.PurePosixPath(rel).name,
            'required': True, 'schemaPolicy': policy}


def run(cmd):
    proc = subprocess.run(cmd, capture_output=True, text=True)
    return proc.returncode, (proc.stdout or '') + (proc.stderr or '')


def shape_required_root_missing(base):
    root = base / 'root-a'
    root.mkdir(parents=True, exist_ok=True)
    package(root, 'run-1', {'a.log': b'x\n'})
    return expected([
        {'id': 'a', 'path': str(root), 'required': True, 'schemaPolicy': 'strict-new',
         'packageCount': 1, 'packages': [declare('run-1')]},
        {'id': 'b', 'path': str(base / 'nowhere'), 'required': True, 'schemaPolicy': 'strict-new',
         'packageCount': 1, 'packages': [declare('run-1')]},
    ]), root


def shape_required_root_empty(base):
    root = base / 'root-a'
    root.mkdir(parents=True, exist_ok=True)
    package(root, 'run-1', {'a.log': b'x\n'})
    (base / 'root-b').mkdir(parents=True, exist_ok=True)
    return expected([
        {'id': 'a', 'path': str(root), 'required': True, 'schemaPolicy': 'strict-new',
         'packageCount': 1, 'packages': [declare('run-1')]},
        {'id': 'b', 'path': str(base / 'root-b'), 'required': True, 'schemaPolicy': 'strict-new',
         'packageCount': 1, 'packages': [declare('run-1')]},
    ]), root


def shape_unsealed_sibling(base):
    root = base / 'root-a'
    root.mkdir(parents=True, exist_ok=True)
    package(root, 'run-1', {'a.log': b'x\n'})
    sib = root / 'run-2'
    sib.mkdir(parents=True, exist_ok=True)
    (sib / 'results.log').write_bytes(b'unsealed output\n')
    return expected([
        {'id': 'a', 'path': str(root), 'required': True, 'schemaPolicy': 'strict-new',
         'packageCount': 1, 'packages': [declare('run-1')]},
    ]), root


def shape_empty_manifest(base):
    root = base / 'root-a'
    root.mkdir(parents=True, exist_ok=True)
    pkg = root / 'run-1'
    pkg.mkdir(parents=True, exist_ok=True)
    jwrite(pkg / 'manifest.json', {'schemaVersion': 1, 'runId': 'run-1',
                                   'generatedUtc': '2026-10-03T09:40:00Z', 'files': []})
    (pkg / 'manifest.sha256').write_text(
        sha((pkg / 'manifest.json').read_bytes()) + '\n', encoding='ascii')
    return expected([
        {'id': 'a', 'path': str(root), 'required': True, 'schemaPolicy': 'strict-new',
         'packageCount': 1, 'packages': [declare('run-1')]},
    ]), root


def shape_missing_size(base):
    root = base / 'root-a'
    root.mkdir(parents=True, exist_ok=True)
    package(root, 'run-1', {'a.log': b'x\n'},
            entries=[{'path': 'a.log', 'sha256': sha(b'x\n')}])
    return expected([
        {'id': 'a', 'path': str(root), 'required': True, 'schemaPolicy': 'strict-new',
         'packageCount': 1, 'packages': [declare('run-1')]},
    ]), root


SHAPES = [
    ('required-root-missing', shape_required_root_missing),
    ('required-root-empty', shape_required_root_empty),
    ('unsealed-sibling-package', shape_unsealed_sibling),
    ('empty-new-manifest', shape_empty_manifest),
    ('entry-without-size', shape_missing_size),
]


def main():
    work = pathlib.Path(tempfile.mkdtemp(prefix='goal-red-auditor-'))
    results = []
    print('old auditor : %s' % OLD_AUDITOR)
    print('new auditor : %s' % NEW_AUDITOR)
    print('fixture root: %s' % work)
    for name, builder in SHAPES:
        base = work / name
        base.mkdir(parents=True, exist_ok=True)
        exp, audit_root = builder(base)
        exp_path = base / 'expected-evidence.json'
        jwrite(exp_path, exp)

        old_out = base / 'old-audit.json'
        old_code, old_text = run([sys.executable, '-X', 'utf8', str(OLD_AUDITOR),
                                  str(base), str(old_out)])
        new_out = base / 'new-audit.json'
        new_code, new_text = run([sys.executable, '-X', 'utf8', str(NEW_AUDITOR),
                                  '--expected', str(exp_path), '--out', str(new_out)])
        new_codes = []
        if new_out.is_file():
            with open(new_out, encoding='utf-8-sig') as fh:
                new_codes = sorted({f['code'] for f in json.load(fh).get('failures', [])})
        entry = {
            'shape': name,
            'oldAuditorExit': old_code,
            'oldAuditorStdout': old_text.strip()[:400],
            'newAuditorExit': new_code,
            'newAuditorFailureCodes': new_codes,
            'newAuditorStdout': new_text.strip()[:400],
            'expected': 'old=0 (false pass preserved as RED), new!=0',
            'verdict': 'RED-REPRODUCED' if old_code == 0 and new_code != 0 else 'CHANGED',
        }
        results.append(entry)
        print('[%s] old=%s new=%s codes=%s' % (name, old_code, new_code, ','.join(new_codes)))

    summary = {
        'schemaVersion': 1,
        'generatedUtc': '2026-10-03T09:40:00Z',
        'oldAuditor': str(OLD_AUDITOR),
        'oldAuditorSha256': sha(OLD_AUDITOR.read_bytes()) if OLD_AUDITOR.is_file() else None,
        'newAuditor': str(NEW_AUDITOR),
        'newAuditorSha256': sha(NEW_AUDITOR.read_bytes()),
        'fixtureRoot': str(work),
        'shapes': results,
        'allRedReproduced': all(r['verdict'] == 'RED-REPRODUCED' for r in results),
    }
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    jwrite(OUT_DIR / 'red-old-auditor-coverage.json', summary)
    print(json.dumps({'allRedReproduced': summary['allRedReproduced'],
                      'shapes': len(results)}, ensure_ascii=False))
    return 0 if summary['allRedReproduced'] else 1


if __name__ == '__main__':
    sys.exit(main())
