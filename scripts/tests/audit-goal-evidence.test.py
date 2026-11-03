#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""audit-goal-evidence.test.py - synthetic contract tests for audit-goal-evidence.py.

Every case builds a throwaway package tree under the OS temp directory (never inside a real
evidence root), writes a frozen expectation, runs the auditor as a child process and asserts
the *real* exit code plus the failure codes it reported. A positive case must exit 0 with no
failures; a negative case must exit non-zero *with the expected reason* (a crash would also be
non-zero, so the expected code is asserted - an exception is not a correct rejection).

Cases requested by the plan (19) plus the two extra boundary cases:
  valid single strict package | exact registered legacy exception | empty single root |
  mixed roots with a missing required root | mixed roots with an empty required root |
  unsealed sibling run package | empty new manifest | missing size | missing runId | wrong runId |
  duplicate entries | missing file | unlisted file inside the package | unknown SHA drift |
  known path third bytes | bad detached | missing detached | self reference |
  manifest rewritten after the freeze | exception whose manifest changed | declared package
  count mismatch | unlisted package on disk | required root without a package list |
  unreadable manifest | legacy policy control.

Run:  python -X utf8 scripts/tests/audit-goal-evidence.test.py
"""

import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
import uuid

HERE = pathlib.Path(__file__).resolve().parent
AUDITOR = HERE.parent / 'audit-goal-evidence.py'


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest().upper()


def sha256_file(path):
    return sha256_bytes(pathlib.Path(path).read_bytes())


def jwrite(path, obj):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(obj, fh, ensure_ascii=False, indent=2)
        fh.write('\n')


def jread(path):
    with open(path, 'rb') as fh:
        return json.loads(fh.read().decode('utf-8-sig'))


class Fixture(object):
    """Builds one synthetic evidence tree plus the expectation that describes it."""

    def __init__(self, case):
        self.root = pathlib.Path(tempfile.mkdtemp(prefix='goal-audit-%s-' % case))
        self.roots = []
        self.exceptions = []

    # ---- tree building ---------------------------------------------------
    def package_dir(self, root_name, rel):
        base = self.root / root_name
        return base, base / pathlib.PurePosixPath(rel)

    def make_package(self, root_name, rel, files, run_id=None, policy='strict-new',
                     detached=True, omit_paths=(), extra_entries=(), extra_files=None,
                     unlisted_files=None, manifest_overrides=None, count=None, write_manifest=True):
        base, pkg = self.package_dir(root_name, rel)
        pkg.mkdir(parents=True, exist_ok=True)
        contents = {}
        for name, data in (files or {}).items():
            target = pkg / pathlib.PurePosixPath(name)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            contents[name] = data
        for name, data in (extra_files or {}).items():
            target = pkg / pathlib.PurePosixPath(name)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        for name, data in (unlisted_files or {}).items():
            target = pkg / pathlib.PurePosixPath(name)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)

        entries = []
        for name, data in sorted(contents.items()):
            if name in omit_paths:
                continue
            entries.append({'path': name, 'sizeBytes': len(data), 'sha256': sha256_bytes(data)})
        entries.extend(extra_entries)

        manifest = {
            'schemaVersion': 1,
            'runId': run_id if run_id is not None else pkg.name,
            'generatedUtc': '2026-10-03T09:00:00Z',
            'files': entries,
        }
        if count is not None:
            manifest['count'] = count
        else:
            manifest['count'] = len(entries)
        if manifest_overrides:
            manifest.update(manifest_overrides)
        if write_manifest:
            jwrite(pkg / 'manifest.json', manifest)
        if detached and write_manifest:
            (pkg / 'manifest.sha256').write_text(
                sha256_file(pkg / 'manifest.json') + '  manifest.json\n', encoding='ascii')
        return pkg

    def add_root(self, root_id, root_name, packages, policy='strict-new', required=True,
                 package_count=None, sibling_allowlist=None, unlisted_file_allowlist=None):
        self.roots.append({
            'id': root_id,
            'path': str(self.root / root_name),
            'required': required,
            'schemaPolicy': policy,
            'packageCount': len(packages) if package_count is None else package_count,
            'siblingAllowlist': sibling_allowlist or [],
            'unlistedFileAllowlist': unlisted_file_allowlist or [],
            'packages': packages,
        })

    @staticmethod
    def declare(rel, run_id=None, policy='strict-new', manifest_sha=None, required=True):
        item = {
            'relativePath': rel,
            'runId': run_id if run_id is not None else pathlib.PurePosixPath(rel).name,
            'required': required,
            'schemaPolicy': policy,
        }
        if manifest_sha:
            item['manifestSha256'] = manifest_sha
        return item

    def expected(self, exceptions=None):
        return {
            'schemaVersion': 1,
            'scope': 'windows-local-offline-quality-test',
            'generatedUtc': '2026-10-03T09:00:00Z',
            'roots': self.roots,
            'exceptions': self.exceptions if exceptions is None else exceptions,
        }

    # ---- running ---------------------------------------------------------
    def audit(self, expected=None, expected_path=None):
        if expected_path is None:
            expected_path = self.root / 'expected-evidence.json'
            jwrite(expected_path, self.expected() if expected is None else expected)
        out = self.root / ('report-%s.json' % uuid.uuid4().hex[:8])
        proc = subprocess.run([sys.executable, '-X', 'utf8', str(AUDITOR),
                               '--expected', str(expected_path), '--out', str(out)],
                              capture_output=True, text=True)
        report = jread(out) if out.is_file() else {'failures': [], 'totals': {}}
        return proc.returncode, report

    def cleanup(self):
        shutil.rmtree(self.root, ignore_errors=True)


class AuditorContractTest(unittest.TestCase):
    def setUp(self):
        self.fx = Fixture(self._testMethodName)
        self.addCleanup(self.fx.cleanup)

    # -- helpers -----------------------------------------------------------
    def codes(self, report):
        return sorted({f['code'] for f in report.get('failures', [])})

    def assert_rejected(self, code, report, exit_code):
        self.assertNotEqual(exit_code, 0, 'a negative case must not exit 0')
        self.assertIn(code, self.codes(report),
                      'expected failure %s, got %s' % (code, self.codes(report)))

    # -- positive controls -------------------------------------------------
    def test_01_valid_single_package_passes(self):
        self.fx.make_package('root-a', 'run-1', {'green/out.log': b'ok\n', 'notes.md': b'n\n'})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assertEqual(0, code, report.get('failures'))
        self.assertEqual([], report['failures'])
        self.assertEqual(2, report['totals']['filesVerified'])

    def test_02_exact_registered_exception_passes(self):
        pkg = self.fx.make_package('root-a', 'run-1', {'drift.md': b'old\n'})
        old = (pkg / 'drift.md').read_bytes()
        old_sha = sha256_bytes(old)
        (pkg / 'drift.md').write_bytes(b'new-bytes\n')
        new_sha = sha256_file(pkg / 'drift.md')
        self.fx.make_package('root-a', 'run-1', {})  # keeps the manifest/detached in sync is not wanted here
        # rebuild the manifest explicitly so the recorded value is the OLD one
        jwrite(pkg / 'manifest.json', {
            'schemaVersion': 1, 'runId': 'run-1', 'generatedUtc': '2026-10-03T09:00:00Z',
            'files': [{'path': 'drift.md', 'sizeBytes': len(old), 'sha256': old_sha}],
        })
        (pkg / 'manifest.sha256').write_text(sha256_file(pkg / 'manifest.json') + '\n', encoding='ascii')
        self.fx.exceptions = [{
            'manifest': 'run-1/manifest.json', 'path': 'drift.md',
            'recordedSha256': old_sha, 'recordedSize': len(old),
            'currentSha256': new_sha, 'currentSize': len(b'new-bytes\n'),
            'reason': 'synthetic', 'source': 'test',
        }]
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assertEqual(0, code, report.get('failures'))
        self.assertEqual(1, report['totals']['knownDrift'])

    def test_25_legacy_policy_without_sizes_passes(self):
        pkg = self.fx.make_package('root-a', '20261003-000000Z', {'a.log': b'x\n'},
                                   policy='legacy-policy')
        jwrite(pkg / 'manifest.json', {
            'runId': '20261003-000000Z',
            'entries': [{'path': 'a.log'}],
        })
        (pkg / 'manifest.sha256').unlink(missing_ok=True)
        self.fx.add_root('a', 'root-a', [self.fx.declare('20261003-000000Z', policy='legacy-policy')],
                         policy='legacy-policy')
        code, report = self.fx.audit()
        self.assertEqual(0, code, report.get('failures'))

    # -- required root / package coverage ----------------------------------
    def test_03_empty_single_root_is_rejected(self):
        (self.fx.root / 'root-a').mkdir(parents=True)
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('ROOT_EMPTY', report, code)

    def test_04_missing_required_root_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        self.fx.add_root('b', 'root-b-missing', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('ROOT_MISSING', report, code)

    def test_05_empty_required_root_beside_valid_root_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        (self.fx.root / 'root-b').mkdir(parents=True)
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        self.fx.add_root('b', 'root-b', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('ROOT_EMPTY', report, code)

    def test_23_required_root_without_package_list_is_rejected(self):
        (self.fx.root / 'root-a').mkdir(parents=True)
        self.fx.add_root('a', 'root-a', [])
        code, report = self.fx.audit()
        self.assert_rejected('ROOT_NO_PACKAGE_LIST', report, code)

    def test_22_manifest_on_disk_but_not_declared_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        self.fx.make_package('root-a', 'run-2', {'b.log': b'y\n'})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('UNLISTED_PACKAGE', report, code)

    def test_21_declared_package_count_mismatch_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')], package_count=7)
        code, report = self.fx.audit()
        self.assert_rejected('PACKAGE_COUNT_MISMATCH', report, code)

    def test_06_unsealed_sibling_run_package_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        sibling = self.fx.root / 'root-a' / 'run-2'
        sibling.mkdir(parents=True)
        (sibling / 'results.log').write_bytes(b'not sealed\n')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('UNSEALED_SIBLING', report, code)

    # -- manifest field contract -------------------------------------------
    def test_07_empty_new_manifest_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('MANIFEST_EMPTY_FILES', report, code)

    def test_08_missing_size_is_rejected(self):
        data = b'payload\n'
        self.fx.make_package('root-a', 'run-1', {'a.log': data},
                             extra_entries=[{'path': 'b.log', 'sha256': sha256_bytes(b'q\n')}],
                             unlisted_files={'b.log': b'q\n'})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('ENTRY_MISSING_SIZE', report, code)

    def test_09_missing_runid_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'},
                             manifest_overrides={'runId': None})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('MANIFEST_MISSING_FIELD', report, code)

    def test_10_wrong_runid_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'}, run_id='run-9')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('PACKAGE_RUNID_MISMATCH', report, code)

    def test_11_duplicate_entries_are_rejected(self):
        data = b'dup\n'
        self.fx.make_package('root-a', 'run-1', {'a.log': data},
                             extra_entries=[{'path': 'a.log', 'sizeBytes': len(data),
                                             'sha256': sha256_bytes(data)}])
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('ENTRY_DUPLICATE', report, code)

    def test_12_missing_file_is_rejected(self):
        ghost = b'ghost\n'
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'},
                             extra_entries=[{'path': 'ghost.log', 'sizeBytes': len(ghost),
                                             'sha256': sha256_bytes(ghost)}])
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('FILE_MISSING', report, code)

    def test_13_unlisted_file_inside_package_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'},
                             unlisted_files={'sneaky.log': b'z\n'})
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('UNLISTED_FILE', report, code)

    def test_14_unknown_sha_drift_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'},
                             extra_entries=[{'path': 'a.log', 'sizeBytes': 2,
                                             'sha256': 'A' * 64}])
        # extra_entries duplicates the path; use a clean single-entry manifest instead
        pkg = self.fx.root / 'root-a' / 'run-1'
        jwrite(pkg / 'manifest.json', {
            'schemaVersion': 1, 'runId': 'run-1', 'generatedUtc': '2026-10-03T09:00:00Z',
            'files': [{'path': 'a.log', 'sizeBytes': 2, 'sha256': 'A' * 64}],
        })
        (pkg / 'manifest.sha256').write_text(sha256_file(pkg / 'manifest.json') + '\n', encoding='ascii')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('FILE_SHA_DRIFT', report, code)

    def test_15_known_path_third_bytes_is_rejected(self):
        pkg = self.fx.make_package('root-a', 'run-1', {'drift.md': b'old\n'})
        old_sha, old_size = sha256_bytes(b'old\n'), 4
        (pkg / 'drift.md').write_bytes(b'third-set-of-bytes\n')
        size = len(b'third-set-of-bytes\n')
        jwrite(pkg / 'manifest.json', {
            'schemaVersion': 1, 'runId': 'run-1', 'generatedUtc': '2026-10-03T09:00:00Z',
            'files': [{'path': 'drift.md', 'sizeBytes': old_size, 'sha256': old_sha}],
        })
        (pkg / 'manifest.sha256').write_text(sha256_file(pkg / 'manifest.json') + '\n', encoding='ascii')
        self.fx.exceptions = [{
            'manifest': 'run-1/manifest.json', 'path': 'drift.md',
            'recordedSha256': old_sha, 'recordedSize': old_size,
            'currentSha256': sha256_bytes(b'registered-current\n'), 'currentSize': len(b'registered-current\n'),
            'reason': 'synthetic', 'source': 'test',
        }]
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('FILE_DRIFT_UNREGISTERED', report, code)
        self.assertGreater(size, 0)

    def test_16_bad_detached_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        (self.fx.root / 'root-a' / 'run-1' / 'manifest.sha256').write_text('0' * 64 + '\n', encoding='ascii')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('BAD_DETACHED', report, code)

    def test_17_new_package_without_detached_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'}, detached=False)
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('MISSING_DETACHED', report, code)

    def test_18_self_reference_is_rejected(self):
        pkg = self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        jwrite(pkg / 'manifest.json', {
            'schemaVersion': 1, 'runId': 'run-1', 'generatedUtc': '2026-10-03T09:00:00Z',
            'files': [{'path': 'a.log', 'sizeBytes': 2, 'sha256': sha256_bytes(b'x\n')},
                      {'path': 'manifest.json', 'sizeBytes': 10, 'sha256': 'B' * 64}],
        })
        (pkg / 'manifest.sha256').write_text(sha256_file(pkg / 'manifest.json') + '\n', encoding='ascii')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assertIn('SELF_REFERENCE', self.codes(report))
        self.assertNotEqual(0, code)

    def test_19_manifest_rewritten_after_freeze_is_rejected(self):
        pkg = self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        frozen = sha256_file(pkg / 'manifest.json')
        jwrite(pkg / 'manifest.json', {
            'schemaVersion': 1, 'runId': 'run-1', 'generatedUtc': '2026-10-03T09:00:00Z',
            'files': [{'path': 'a.log', 'sizeBytes': 2, 'sha256': sha256_bytes(b'x\n')}],
            'note': 'rewritten after the freeze',
        })
        (pkg / 'manifest.sha256').write_text(sha256_file(pkg / 'manifest.json') + '\n', encoding='ascii')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1', manifest_sha=frozen)])
        code, report = self.fx.audit()
        self.assert_rejected('MANIFEST_SHA_DRIFT', report, code)

    def test_20_exception_whose_manifest_changed_is_rejected(self):
        pkg = self.fx.make_package('root-a', 'run-1', {'drift.md': b'old\n'})
        old_sha, old_size = sha256_bytes(b'old\n'), 4
        (pkg / 'drift.md').write_bytes(b'current\n')
        jwrite(pkg / 'manifest.json', {
            'schemaVersion': 1, 'runId': 'run-1', 'generatedUtc': '2026-10-03T09:00:00Z',
            'files': [{'path': 'drift.md', 'sizeBytes': old_size, 'sha256': old_sha}],
        })
        manifest_sha = sha256_file(pkg / 'manifest.json')
        (pkg / 'manifest.sha256').write_text(manifest_sha + '\n', encoding='ascii')
        self.fx.exceptions = [{
            'manifest': 'run-1/manifest.json', 'path': 'drift.md',
            'recordedSha256': old_sha, 'recordedSize': old_size,
            'currentSha256': sha256_bytes(b'current\n'), 'currentSize': 8,
            'manifestSha256': manifest_sha, 'reason': 'synthetic', 'source': 'test',
        }]
        # the manifest is rewritten after the exception was registered
        jwrite(pkg / 'manifest.json', {
            'schemaVersion': 1, 'runId': 'run-1', 'generatedUtc': '2026-10-03T09:00:00Z',
            'files': [{'path': 'drift.md', 'sizeBytes': old_size, 'sha256': old_sha}],
            'note': 'touched again',
        })
        (pkg / 'manifest.sha256').write_text(sha256_file(pkg / 'manifest.json') + '\n', encoding='ascii')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('EXCEPTION_MANIFEST_DRIFT', report, code)

    def test_24_unreadable_manifest_is_a_clean_rejection(self):
        pkg = self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        (pkg / 'manifest.json').write_bytes(b'{ not json at all')
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('PACKAGE_MANIFEST_UNREADABLE', report, code)

    def test_26_unused_registered_exception_is_rejected(self):
        self.fx.make_package('root-a', 'run-1', {'a.log': b'x\n'})
        self.fx.exceptions = [{
            'manifest': 'run-1/manifest.json', 'path': 'never-there.md',
            'recordedSha256': 'C' * 64, 'recordedSize': 1,
            'currentSha256': 'D' * 64, 'currentSize': 1,
            'reason': 'synthetic', 'source': 'test',
        }]
        self.fx.add_root('a', 'root-a', [self.fx.declare('run-1')])
        code, report = self.fx.audit()
        self.assert_rejected('EXCEPTION_UNUSED', report, code)


if __name__ == '__main__':
    unittest.main(verbosity=2)
