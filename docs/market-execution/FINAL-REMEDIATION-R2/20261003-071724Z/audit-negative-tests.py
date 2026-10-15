#!/usr/bin/env python3
"""audit-negative-tests.py - the auditor is only trustworthy if its own counterexamples fail.

Builds synthetic evidence trees in a GUID temp directory and runs
`audit-all-manifests-r2.py` against each one, asserting the expected verdict.

Negative cases (must be non-zero):
  * empty root                          - the target list must be non-empty
  * existing root without manifests     - same, non-empty coverage
  * bad detached manifest.sha256        - detached digest must match manifest.json
  * missing detached (remediation line)- packages of this line must carry one
  * missing listed file                 - completeness
  * unlisted file in the package        - completeness
  * sha256 drift on an unknown path     - unknown drift
  * runId mismatch                      - run identity
  * manifest listing itself             - self reference
  * registered path with a THIRD value  - historical exception must be byte-bound

Positive cases (must be zero):
  * complete valid package              - correct manifest + correct detached
  * exactly-registered historical exception - manifest old values + registered current bytes

The judged evidence tree is never modified: the MG0 fixture copies the real bytes read-only.
"""
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).parent
AUDITOR = HERE / 'audit-all-manifests-r2.py'
EXCEPTIONS = HERE / 'historical-exceptions.json'
ROOTS_FILE = HERE / 'audit-roots.json'
OLD_DRIFT_SHA = '1F7197B34E86025D32C0B8DDB6DE0A136F1CB3489FD4927AEEDAA97698F0BE88'
OLD_DRIFT_SIZE = 3114


def real_roots():
    if ROOTS_FILE.is_file():
        try:
            return [pathlib.Path(p) for p in json.loads(
                ROOTS_FILE.read_text(encoding='utf-8-sig')).get('roots', []) if str(p).strip()]
        except Exception:  # noqa: BLE001
            pass
    return [HERE.parent.parent]


def find_real_mg0_drift():
    for root in real_roots():
        candidate = root / 'MG0-verification-package-20261003' / 'drift-notes.md'
        if candidate.is_file():
            return candidate
    return None


def sha(text):
    import hashlib
    return hashlib.sha256(text).hexdigest().upper()


def write_package(root, name, files, run_id=None, detached=True, self_ref=False, manifest=None):
    pkg = root / name
    pkg.mkdir(parents=True, exist_ok=True)
    entries = []
    for rel, data in files.items():
        target = pkg / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        entries.append({'path': rel, 'sizeBytes': len(data), 'sha256': sha(data)})
    if self_ref:
        entries.append({'path': 'manifest.json', 'sizeBytes': 1, 'sha256': sha(b'x')})
    doc = manifest or {'runId': run_id or name, 'files': entries}
    (pkg / 'manifest.json').write_text(json.dumps(doc, ensure_ascii=False), encoding='utf-8')
    if detached:
        import hashlib
        digest = hashlib.sha256((pkg / 'manifest.json').read_bytes()).hexdigest().upper()
        (pkg / 'manifest.sha256').write_text(digest + '  manifest.json\n', encoding='ascii')
    return pkg


def run_case(label, root, expect_fail, out_dir):
    out = out_dir / (label + '.json')
    proc = subprocess.run(
        [sys.executable, str(AUDITOR), str(root), str(out), str(EXCEPTIONS)],
        capture_output=True, text=True, encoding='utf-8', errors='replace')
    ok = (proc.returncode != 0) if expect_fail else (proc.returncode == 0)
    summary = (proc.stdout or '').strip().splitlines()
    return {
        'case': label,
        'expected': 'non-zero' if expect_fail else 'zero',
        'exitCode': proc.returncode,
        'ok': ok,
        'summary': summary[0] if summary else '',
    }


def main():
    base = pathlib.Path(tempfile.mkdtemp(prefix='pineglow-audit-neg-'))
    out_dir = base / 'results'
    out_dir.mkdir(parents=True, exist_ok=True)
    results = []

    # ---- negative ----
    empty = base / 'empty-root'
    empty.mkdir()
    results.append(run_case('empty-root', empty, True, out_dir))

    noman = base / 'no-manifest'
    noman.mkdir()
    (noman / 'readme.txt').write_text('no manifests here', encoding='utf-8')
    results.append(run_case('no-manifest', noman, True, out_dir))

    bad = base / 'bad-detached'
    pkg = write_package(bad, 'RUN-1', {'a.txt': b'hello'})
    (pkg / 'manifest.sha256').write_text('0' * 64 + '  manifest.json\n', encoding='ascii')
    results.append(run_case('bad-detached', bad, True, out_dir))

    nodet = base / 'FINAL-REMEDIATION-missing-detached'
    write_package(nodet, 'RUN-2', {'a.txt': b'hello'}, detached=False)
    results.append(run_case('missing-detached-remediation-line', nodet, True, out_dir))

    miss = base / 'missing-entry'
    pkg = write_package(miss, 'RUN-3', {'a.txt': b'hello'})
    (pkg / 'b.txt').unlink(missing_ok=True)
    doc = json.loads((pkg / 'manifest.json').read_text(encoding='utf-8'))
    doc['files'].append({'path': 'b.txt', 'sizeBytes': 5, 'sha256': sha(b'world')})
    (pkg / 'manifest.json').write_text(json.dumps(doc), encoding='utf-8')
    import hashlib
    (pkg / 'manifest.sha256').write_text(
        hashlib.sha256((pkg / 'manifest.json').read_bytes()).hexdigest().upper() + '  manifest.json\n', encoding='ascii')
    results.append(run_case('missing-listed-file', miss, True, out_dir))

    unl = base / 'unlisted'
    pkg = write_package(unl, 'RUN-4', {'a.txt': b'hello'})
    (pkg / 'c.txt').write_bytes(b'unlisted')
    import hashlib
    (pkg / 'manifest.sha256').write_text(
        hashlib.sha256((pkg / 'manifest.json').read_bytes()).hexdigest().upper() + '  manifest.json\n', encoding='ascii')
    results.append(run_case('unlisted-file', unl, True, out_dir))

    drift = base / 'unknown-drift'
    pkg = write_package(drift, 'RUN-5', {'a.txt': b'hello'})
    (pkg / 'a.txt').write_bytes(b'changed-after-sealing')
    import hashlib
    (pkg / 'manifest.sha256').write_text(
        hashlib.sha256((pkg / 'manifest.json').read_bytes()).hexdigest().upper() + '  manifest.json\n', encoding='ascii')
    results.append(run_case('unknown-sha-drift', drift, True, out_dir))

    rid = base / 'runid-mismatch'
    write_package(rid, 'RUN-6', {'a.txt': b'hello'}, run_id='SOMETHING-ELSE')
    results.append(run_case('runid-mismatch', rid, True, out_dir))

    selfref = base / 'self-reference'
    write_package(selfref, 'RUN-7', {'a.txt': b'hello'}, self_ref=True)
    results.append(run_case('manifest-self-reference', selfref, True, out_dir))

    third = base / 'known-third-bytes'
    mg0 = third / 'MG0-verification-package-20261003'
    mg0.mkdir(parents=True)
    third_bytes = b'THIRD-VALUE-NEVER-REGISTERED'
    (mg0 / 'drift-notes.md').write_bytes(third_bytes)
    (mg0 / 'manifest.json').write_text(json.dumps({
        'runId': 'MG0-verification-package-20261003',
        'files': [{'path': 'drift-notes.md', 'sizeBytes': OLD_DRIFT_SIZE, 'sha256': OLD_DRIFT_SHA}],
    }), encoding='utf-8')
    results.append(run_case('known-path-third-bytes', third, True, out_dir))

    # ---- positive ----
    good = base / 'valid-package'
    write_package(good, 'RUN-OK', {'a.txt': b'hello', 'sub/b.txt': b'world'})
    results.append(run_case('valid-package', good, False, out_dir))

    real_mg0 = find_real_mg0_drift()
    if real_mg0 is not None:
        reg = base / 'registered-exception'
        mg0 = reg / 'MG0-verification-package-20261003'
        mg0.mkdir(parents=True)
        shutil.copyfile(real_mg0, mg0 / 'drift-notes.md')
        (mg0 / 'manifest.json').write_text(json.dumps({
            'runId': 'MG0-verification-package-20261003',
            'files': [{'path': 'drift-notes.md', 'sizeBytes': OLD_DRIFT_SIZE, 'sha256': OLD_DRIFT_SHA}],
        }), encoding='utf-8')
        results.append(run_case('registered-exception', reg, False, out_dir))
    else:
        results.append({'case': 'registered-exception', 'expected': 'zero', 'exitCode': None,
                        'ok': False, 'summary': 'real MG0 drift-notes.md not found under ' + str(real_roots())})

    payload = {'fixtureRoot': str(base), 'auditor': str(AUDITOR), 'cases': results,
               'failed': [r for r in results if not r['ok']]}
    (out_dir / 'audit-negative-tests.json').write_text(
        json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf-8')

    for item in results:
        print('{:<38} expect={:<9} exit={:<5} {}'.format(
            item['case'], item['expected'], str(item['exitCode']), 'OK' if item['ok'] else 'MISMATCH'))
    print(json.dumps({'cases': len(results), 'mismatches': len(payload['failed']),
                      'fixtureRoot': str(base)}, ensure_ascii=False))
    return 1 if payload['failed'] else 0


if __name__ == '__main__':
    sys.exit(main())
