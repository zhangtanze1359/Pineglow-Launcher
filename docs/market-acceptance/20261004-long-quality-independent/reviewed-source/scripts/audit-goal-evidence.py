#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""audit-goal-evidence.py - strict evidence auditor for the LONG-QUALITY-GOAL run.

Why a second auditor exists
  The previous auditor (market-execution .../audit-all-manifests-r2.py) only walked the
  manifests it happened to find (rglob) and therefore produced false passes:
    * a declared-but-missing required root was filtered out (line 81 of the old script);
    * a declared-but-empty required root passed as long as some other root had manifests;
    * an unsealed sibling run package next to a valid one was invisible (the "unlisted"
      check only ran inside packages that already had a manifest);
    * an empty new manifest degraded to an empty entry set and passed;
    * a new entry with no size field was not size-checked at all.
  This auditor inverts the direction: the *expectation* is a frozen file
  (expected-evidence.json).  Every required root and every declared package is checked
  by identity; anything discovered on disk that is not declared fails.

Contract (frozen input)
  expected-evidence.json
    schemaVersion : 1
    roots[]       : { id, path, required, schemaPolicy, packageCount,
                      siblingAllowlist[], packages[] }
      packages[]  : { relativePath, runId, required, schemaPolicy, manifestSha256? }
    exceptions[]  : { manifest, path, recordedSha256, recordedSize,
                      currentSha256, currentSize, manifestSha256?, reason, source }

Modes
  audit (default)   --expected FILE [--out FILE] [--exceptions FILE]
  freeze            --root <id>=<path>[:<policy>] [--new-package <rootId>=<rel>[:<runId>]]
                    [--allow-sibling <rootId>=<rel>[:<reason>]] --out FILE

Exit codes  0 = no failures  1 = failures  2 = usage / unreadable expectation
Nothing is ever written inside an audited package: the report goes to --out only.
"""

import argparse
import hashlib
import json
import os
import pathlib
import re
import sys
from datetime import datetime, timezone

AUDITOR = 'audit-goal-evidence.py'
SCHEMA_VERSION = 1
EXEMPT_NAMES = {'manifest.json', 'manifest.sha256'}
HEX64 = re.compile(r'^[0-9a-fA-F]{64}$')
ENTRY_LIST_KEYS = ('files', 'entries', 'artifacts')
RUN_PACKAGE_MARKERS = ('results.log', 'run.log', 'summary.log')
POLICIES = ('strict-new', 'legacy-policy')


# --------------------------------------------------------------------------- helpers

def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest().upper()


def norm(value):
    return str(value or '').replace('\\', '/').strip()


def now_utc():
    return datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')


def load_json(path):
    raw = pathlib.Path(path).read_bytes()
    for enc in ('utf-8-sig', 'utf-8', 'cp1252'):
        try:
            return json.loads(raw.decode(enc))
        except UnicodeDecodeError:
            continue
    return json.loads(raw.decode('utf-8', 'replace'))


def write_json(path, obj):
    p = pathlib.Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(p, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(obj, fh, ensure_ascii=False, indent=2)
        fh.write('\n')


def entry_path(entry):
    for key in ('path', 'relativePath', 'relative_path', 'name'):
        if isinstance(entry.get(key), str) and entry[key].strip():
            return norm(entry[key])
    return None


def entry_size(entry):
    for key in ('sizeBytes', 'size', 'bytes'):
        value = entry.get(key)
        if isinstance(value, bool):
            continue
        if isinstance(value, int):
            return value
    return None


def entry_sha(entry):
    for key in ('sha256', 'sha', 'hash'):
        value = entry.get(key)
        if isinstance(value, str) and value.strip():
            return value.strip().upper()
    return None


def path_is_safe(rel):
    if rel.startswith('/') or rel.startswith('\\'):
        return False
    if re.match(r'^[A-Za-z]:', rel):
        return False
    parts = [p for p in rel.split('/')]
    return not any(p in ('..', '') for p in parts[:-1])


class Audit(object):
    def __init__(self, expected_path, exceptions_file=None):
        self.expected_path = pathlib.Path(expected_path)
        self.exceptions_file = exceptions_file
        self.failures = []
        self.known_drift = []
        self.notes = []
        self.roots_report = []
        self.consumed_exceptions = set()
        self.declared_exception_keys = set()
        self.entry_count = 0
        self.files_verified = 0

    # -- failure bookkeeping -------------------------------------------------
    def fail(self, code, root_id='', package='', path='', detail=''):
        self.failures.append({'code': code, 'root': root_id, 'package': package,
                              'path': path, 'detail': detail})

    # -- exception index -----------------------------------------------------
    def build_exception_index(self, data):
        index = {}
        entries = data.get('exceptions') or []
        if not isinstance(entries, list):
            self.fail('EXPECTED_BAD_EXCEPTIONS', detail='exceptions must be a list')
            return index
        for item in entries:
            key = (norm(item.get('manifest')), norm(item.get('path')))
            missing = [f for f in ('manifest', 'path', 'recordedSha256', 'recordedSize',
                                   'currentSha256', 'currentSize')
                       if item.get(f) in (None, '')]
            if missing:
                self.fail('EXCEPTION_INCOMPLETE', path=key[1],
                          detail='missing fields: ' + ','.join(missing))
                continue
            if key in index:
                self.fail('EXCEPTION_DUPLICATE', path=key[1],
                          detail='manifest=' + key[0])
                continue
            index[key] = item
            self.declared_exception_keys.add(key)
        return index

    # -- policy helpers ------------------------------------------------------
    @staticmethod
    def entries_of(data, policy):
        for key in ENTRY_LIST_KEYS:
            if key in data and isinstance(data[key], list):
                return data[key], key
        return None, None

    def verify_detached(self, pkg_dir, manifest_file, policy, root_id, rel_pkg):
        detached = pkg_dir / 'manifest.sha256'
        if not detached.is_file():
            if policy == 'strict-new':
                self.fail('MISSING_DETACHED', root_id, rel_pkg, 'manifest.sha256',
                          'strict-new package must carry manifest.sha256')
            return
        try:
            recorded = detached.read_text(encoding='utf-8-sig').strip().split()[0].upper()
        except Exception as exc:  # noqa: BLE001
            self.fail('BAD_DETACHED', root_id, rel_pkg, 'manifest.sha256', 'unreadable: %s' % exc)
            return
        actual = sha256_of(manifest_file)
        if recorded != actual:
            self.fail('BAD_DETACHED', root_id, rel_pkg, 'manifest.sha256',
                      'detached=%s actual=%s' % (recorded, actual))

    def verify_entry(self, entry, pkg_dir, root_id, rel_pkg, rel_manifest, policy,
                     exception_index, seen_paths):
        if not isinstance(entry, dict):
            self.fail('ENTRY_NOT_OBJECT', root_id, rel_pkg, rel_manifest, repr(entry)[:120])
            return
        rel = entry_path(entry)
        if not rel or not path_is_safe(rel):
            self.fail('ENTRY_BAD_PATH', root_id, rel_pkg, rel_manifest,
                      'unsafe or missing path: %r' % (rel,))
            return
        self.entry_count += 1
        if rel == 'manifest.json' or rel == 'manifest.sha256':
            self.fail('SELF_REFERENCE', root_id, rel_pkg, rel,
                      'a manifest must not list itself or its detached digest')
            return
        if rel in seen_paths:
            self.fail('ENTRY_DUPLICATE', root_id, rel_pkg, rel, 'listed twice')
            return
        seen_paths.add(rel)

        size = entry_size(entry)
        sha = entry_sha(entry)
        if policy == 'strict-new':
            if size is None:
                self.fail('ENTRY_MISSING_SIZE', root_id, rel_pkg, rel,
                          'strict-new entries must carry an integer size')
            if sha is None:
                self.fail('ENTRY_MISSING_SHA', root_id, rel_pkg, rel,
                          'strict-new entries must carry a 64-hex sha256')
        if sha is not None and not HEX64.match(sha):
            self.fail('ENTRY_BAD_SHA', root_id, rel_pkg, rel, 'not 64 hex: %r' % sha)
            return
        if size is not None and (size < 0 or isinstance(size, bool)):
            self.fail('ENTRY_BAD_SIZE', root_id, rel_pkg, rel, 'not a non-negative int: %r' % (size,))
            return

        target = pkg_dir / pathlib.PurePosixPath(rel)
        if not target.is_file():
            key = (rel_manifest, rel)
            if key in exception_index:
                self.fail('EXCEPTION_UNUSED', root_id, rel_pkg, rel,
                          'registered exception but the file does not exist')
            else:
                self.fail('FILE_MISSING', root_id, rel_pkg, rel, 'listed but absent on disk')
            return

        actual_size = target.stat().st_size
        need_sha = sha is not None
        actual_sha = sha256_of(target) if need_sha else None
        self.files_verified += 1

        key = (rel_manifest, rel)
        exc = exception_index.get(key)

        if exc is not None:
            self.consumed_exceptions.add(key)
            exc_manifest_sha = exc.get('manifestSha256')
            if exc_manifest_sha:
                mf = pkg_dir / 'manifest.json'
                if mf.is_file() and sha256_of(mf) != str(exc_manifest_sha).upper():
                    self.fail('EXCEPTION_MANIFEST_DRIFT', root_id, rel_pkg, rel_manifest,
                              'manifest bytes changed since the exception was registered')
            rec_sha = str(exc['recordedSha256']).upper()
            cur_sha = str(exc['currentSha256']).upper()
            rec_size = exc['recordedSize']
            cur_size = exc['currentSize']
            bound_ok = True
            if need_sha and sha != rec_sha:
                bound_ok = False
            if size is not None and rec_size != size:
                bound_ok = False
            if not bound_ok:
                self.fail('EXCEPTION_CONFLICT', root_id, rel_pkg, rel,
                          'the manifest does not record the value this exception binds '
                          '(manifest sha=%s size=%s, exception recorded sha=%s size=%s)'
                          % (sha, size, rec_sha, rec_size))
                return
            if ((not need_sha or actual_sha == cur_sha) and actual_size == cur_size):
                self.known_drift.append({
                    'manifest': rel_manifest, 'path': rel, 'root': root_id,
                    'package': rel_pkg, 'recordedSha256': rec_sha,
                    'currentSha256': cur_sha, 'actualSha256': actual_sha,
                    'recordedSize': rec_size, 'currentSize': cur_size,
                    'reason': exc.get('reason', ''), 'source': exc.get('source', '')})
                return
            if need_sha and actual_sha == rec_sha and actual_size == rec_size:
                self.notes.append('exception %s/%s currently matches the recorded value'
                                  % (rel_manifest, rel))
                return
            self.fail('FILE_DRIFT_UNREGISTERED', root_id, rel_pkg, rel,
                      'actual sha=%s size=%s matches neither the recorded (%s/%s) nor the '
                      'registered current (%s/%s) value'
                      % (actual_sha, actual_size, rec_sha, rec_size, cur_sha, cur_size))
            return

        if need_sha and sha != actual_sha:
            self.fail('FILE_SHA_DRIFT', root_id, rel_pkg, rel,
                      'manifest=%s actual=%s' % (sha, actual_sha))
        if size is not None and size != actual_size:
            self.fail('FILE_SIZE_DRIFT', root_id, rel_pkg, rel,
                      'manifest=%d actual=%d' % (size, actual_size))

    def verify_manifest(self, manifest_file, pkg_dir, root_dir, root_id, rel_pkg, policy,
                        exception_index, allow_unlisted_prefixes):
        rel_manifest = manifest_file.relative_to(root_dir).as_posix()
        try:
            data = load_json(manifest_file)
        except Exception as exc:  # noqa: BLE001
            self.fail('PACKAGE_MANIFEST_UNREADABLE', root_id, rel_pkg, rel_manifest, str(exc))
            return None
        if not isinstance(data, dict):
            self.fail('PACKAGE_MANIFEST_UNREADABLE', root_id, rel_pkg, rel_manifest,
                      'manifest root is not an object')
            return None

        if policy == 'strict-new':
            for field in ('schemaVersion', 'generatedUtc', 'runId'):
                if data.get(field) in (None, ''):
                    self.fail('MANIFEST_MISSING_FIELD', root_id, rel_pkg, rel_manifest,
                              'strict-new manifest must carry %s' % field)

        run_id = data.get('runId')
        if run_id:
            if norm(run_id) != pkg_dir.name:
                self.fail('PACKAGE_RUNID_MISMATCH', root_id, rel_pkg, rel_manifest,
                          'runId=%s dir=%s' % (run_id, pkg_dir.name))
        elif policy == 'strict-new':
            self.fail('MANIFEST_MISSING_RUNID', root_id, rel_pkg, rel_manifest, 'no runId')

        entries, key_used = self.entries_of(data, policy)
        if entries is None:
            if policy == 'strict-new':
                self.fail('MANIFEST_EMPTY_FILES', root_id, rel_pkg, rel_manifest,
                          'no files/entries/artifacts list')
            else:
                self.notes.append('legacy manifest without an entry list: %s' % rel_manifest)
            entries = []
        if policy == 'strict-new' and len(entries) == 0:
            self.fail('MANIFEST_EMPTY_FILES', root_id, rel_pkg, rel_manifest,
                      'strict-new manifest must list at least one file')

        seen_paths = set()
        for entry in entries:
            self.verify_entry(entry, pkg_dir, root_id, rel_pkg, rel_manifest, policy,
                              exception_index, seen_paths)

        listed = set(seen_paths)
        for name in EXEMPT_NAMES:
            listed.add(name)
        if key_used:
            declared_count = data.get('count')
            if isinstance(declared_count, int) and declared_count != len(entries):
                self.fail('MANIFEST_COUNT_MISMATCH', root_id, rel_pkg, rel_manifest,
                          'count=%d entries=%d' % (declared_count, len(entries)))
        for file in sorted(pkg_dir.rglob('*')):
            if not file.is_file():
                continue
            rel = file.relative_to(pkg_dir).as_posix()
            if rel in listed:
                continue
            if any(rel == p or rel.startswith(p.rstrip('/') + '/') for p in allow_unlisted_prefixes):
                self.notes.append('allowed unlisted file %s/%s' % (rel_pkg, rel))
                continue
            self.fail('UNLISTED_FILE', root_id, rel_pkg, rel, 'present in package but not listed')
        return data

    # -- main ---------------------------------------------------------------
    def run(self):
        if not self.expected_path.is_file():
            print(json.dumps({'error': 'expected-evidence file missing',
                              'path': str(self.expected_path)}, ensure_ascii=False))
            return 2
        expected = load_json(self.expected_path)
        expected_sha = sha256_of(self.expected_path)

        if expected.get('schemaVersion') != SCHEMA_VERSION:
            self.fail('EXPECTED_BAD_SCHEMA_VERSION', detail=str(expected.get('schemaVersion')))
        roots = expected.get('roots')
        if not isinstance(roots, list) or not roots:
            self.fail('EXPECTED_NO_ROOTS', detail='roots must be a non-empty list')
            roots = []

        exception_index = self.build_exception_index(expected)
        if self.exceptions_file:
            extra = load_json(self.exceptions_file)
            for item in (extra.get('exceptions') or []):
                key = (norm(item.get('manifest')), norm(item.get('path')))
                if key not in exception_index:
                    exception_index[key] = item
                    self.declared_exception_keys.add(key)

        packages_total = 0
        for root in roots:
            root_id = str(root.get('id') or '?')
            path = pathlib.Path(str(root.get('path') or ''))
            policy = str(root.get('schemaPolicy') or 'legacy-policy')
            required = bool(root.get('required', True))
            declared = root.get('packages') or []
            if policy not in POLICIES:
                self.fail('EXPECTED_BAD_POLICY', root_id, detail=policy)
            if required and not declared:
                self.fail('ROOT_NO_PACKAGE_LIST', root_id, path=str(path),
                          detail='a required root must declare its package list')
            report = {'id': root_id, 'path': str(path), 'policy': policy,
                      'required': required, 'exists': path.is_dir(),
                      'manifestsFound': 0, 'packagesDeclared': len(declared),
                      'packagesChecked': 0}
            self.roots_report.append(report)

            if not path.is_dir():
                if required:
                    self.fail('ROOT_MISSING', root_id, path=str(path),
                              detail='declared required root does not exist')
                else:
                    self.notes.append('optional root absent: %s' % path)
                continue

            found_manifests = sorted(p.relative_to(path).as_posix()
                                     for p in path.rglob('manifest.json'))
            report['manifestsFound'] = len(found_manifests)
            if required and not found_manifests:
                self.fail('ROOT_EMPTY', root_id, path=str(path),
                          detail='declared required root contains no manifest.json')

            declared_count = root.get('packageCount')
            if isinstance(declared_count, int) and declared_count != len(declared):
                self.fail('PACKAGE_COUNT_MISMATCH', root_id, path=str(path),
                          detail='packageCount=%d declared=%d' % (declared_count, len(declared)))

            allow_unlisted = [norm(x) for x in (root.get('unlistedFileAllowlist') or [])]
            declared_keys = set()
            for pkg in declared:
                rel_pkg = norm(pkg.get('relativePath'))
                declared_keys.add(rel_pkg)
                pkg_policy = str(pkg.get('schemaPolicy') or policy)
                pkg_required = bool(pkg.get('required', True))
                pkg_dir = path / pathlib.PurePosixPath(rel_pkg)
                packages_total += 1
                if not pkg_dir.is_dir():
                    if pkg_required:
                        self.fail('PACKAGE_MISSING', root_id, rel_pkg,
                                  detail='declared required package does not exist')
                    else:
                        self.notes.append('optional package absent: %s/%s' % (root_id, rel_pkg))
                    continue
                manifest_file = pkg_dir / 'manifest.json'
                if not manifest_file.is_file():
                    self.fail('PACKAGE_MANIFEST_MISSING', root_id, rel_pkg,
                              detail='package has no manifest.json')
                    continue
                inner = [p for p in pkg_dir.rglob('manifest.json')]
                if len(inner) != 1:
                    self.fail('PACKAGE_MANIFEST_COUNT', root_id, rel_pkg,
                              detail='expected exactly 1 manifest.json, found %d' % len(inner))
                if pkg.get('runId') and norm(pkg['runId']) != pkg_dir.name:
                    self.fail('PACKAGE_RUNID_MISMATCH', root_id, rel_pkg,
                              detail='declared runId=%s dir=%s' % (pkg['runId'], pkg_dir.name))
                declared_sha = pkg.get('manifestSha256')
                if declared_sha:
                    actual = sha256_of(manifest_file)
                    if actual != str(declared_sha).upper():
                        self.fail('MANIFEST_SHA_DRIFT', root_id, rel_pkg, 'manifest.json',
                                  'declared=%s actual=%s' % (declared_sha, actual))
                self.verify_detached(pkg_dir, manifest_file, pkg_policy, root_id, rel_pkg)
                self.verify_manifest(manifest_file, pkg_dir, path, root_id, rel_pkg,
                                     pkg_policy, exception_index, allow_unlisted)
                report['packagesChecked'] += 1

            for rel_manifest in found_manifests:
                rel_dir = pathlib.PurePosixPath(rel_manifest).parent.as_posix()
                if rel_dir == '.':
                    rel_dir = ''
                if rel_dir not in declared_keys:
                    self.fail('UNLISTED_PACKAGE', root_id, rel_dir, rel_manifest,
                              detail='manifest discovered on disk but not declared')

            allow_siblings = {}
            for item in (root.get('siblingAllowlist') or []):
                if isinstance(item, dict):
                    allow_siblings[norm(item.get('relativePath'))] = item.get('reason', '')
                else:
                    allow_siblings[norm(item)] = ''

            parents = set()
            parents.add('')
            for pkg in declared:
                rel_pkg = norm(pkg.get('relativePath'))
                parts = rel_pkg.split('/')
                parents.add('/'.join(parts[:-1]) if len(parts) > 1 else '')
            for parent in sorted(parents):
                base = path / pathlib.PurePosixPath(parent) if parent else path
                if not base.is_dir():
                    continue
                for child in sorted(base.iterdir()):
                    if not child.is_dir():
                        continue
                    crel = (parent + '/' + child.name) if parent else child.name
                    if crel in declared_keys or crel in allow_siblings:
                        continue
                    marker = None
                    for name in RUN_PACKAGE_MARKERS:
                        if (child / name).is_file():
                            marker = name
                            break
                    if marker is None:
                        for _probe in child.glob('*.trx'):
                            marker = _probe.name
                            break
                    if marker is None:
                        continue
                    self.fail('UNSEALED_SIBLING', root_id, crel,
                              detail='run-package marker %r but no manifest.json' % marker)

        for key in sorted(self.declared_exception_keys):
            if key not in self.consumed_exceptions:
                self.fail('EXCEPTION_UNUSED', path=key[1], detail='manifest=' + key[0])

        result = {
            'schemaVersion': 1,
            'auditor': AUDITOR,
            'auditorSha256': sha256_of(pathlib.Path(__file__)),
            'generatedUtc': now_utc(),
            'expected': str(self.expected_path),
            'expectedSha256': expected_sha,
            'roots': self.roots_report,
            'totals': {
                'roots': len(self.roots_report),
                'packages': packages_total,
                'manifestEntries': self.entry_count,
                'filesVerified': self.files_verified,
                'failures': len(self.failures),
                'knownDrift': len(self.known_drift),
                'notes': len(self.notes),
            },
            'failures': self.failures,
            'knownDrift': self.known_drift,
            'notes': self.notes,
            'exitCode': 1 if self.failures else 0,
        }
        return result


# --------------------------------------------------------------------------- freeze

def freeze(args):
    roots = []
    for spec in args.root:
        head, _, maybe_policy = spec.rpartition(':')
        if maybe_policy in POLICIES and head:
            policy = maybe_policy
        else:
            head, policy = spec, 'legacy-policy'
        root_id, _, path = head.partition('=')
        if not root_id or not path:
            raise SystemExit('--root must look like id=<absolute path>[:policy]')
        if policy not in POLICIES:
            raise SystemExit('unknown policy: ' + policy)
        base = pathlib.Path(path)
        if not base.is_dir():
            raise SystemExit('root does not exist: ' + path)
        packages = []
        for manifest in sorted(base.rglob('manifest.json')):
            rel = manifest.parent.relative_to(base).as_posix()
            packages.append({
                'relativePath': rel,
                'runId': manifest.parent.name,
                'required': True,
                'schemaPolicy': policy,
                'manifestSha256': sha256_of(manifest),
            })
        allow = []
        for spec2 in args.allow_sibling:
            rid, _, rest = spec2.partition('=')
            if rid != root_id:
                continue
            rel, _, reason = rest.partition(':')
            allow.append({'relativePath': norm(rel), 'reason': reason or 'reviewed'})
        roots.append({
            'id': root_id,
            'path': str(base),
            'required': True,
            'schemaPolicy': policy,
            'packageCount': len(packages),
            'siblingAllowlist': allow,
            'packages': packages,
        })
    for spec in args.new_package:
        rid, _, rest = spec.partition('=')
        rel, _, run_id = rest.partition(':')
        target = None
        for root in roots:
            if root['id'] == rid:
                target = root
        if target is None:
            raise SystemExit('--new-package refers to unknown root: ' + rid)
        target['packages'].append({
            'relativePath': norm(rel),
            'runId': run_id or pathlib.PurePosixPath(norm(rel)).name,
            'required': True,
            'schemaPolicy': 'strict-new',
            'pendingAtFreeze': True,
        })
        target['packageCount'] = len(target['packages'])
    expected = {
        'schemaVersion': SCHEMA_VERSION,
        'scope': 'windows-local-offline-quality',
        'generatedUtc': now_utc(),
        'note': ('Frozen expectation. Coverage is by identity: every required root and every '
                 'declared package is checked; anything found on disk but not declared fails.'),
        'roots': roots,
        'exceptions': [],
    }
    if args.exceptions_file:
        loaded = load_json(args.exceptions_file)
        expected['exceptions'] = loaded.get('exceptions') or []
        expected['exceptionsSource'] = str(args.exceptions_file)
        expected['exceptionsSourceSha256'] = sha256_of(args.exceptions_file)
    write_json(args.out, expected)
    return expected


# --------------------------------------------------------------------------- main

def main(argv=None):
    parser = argparse.ArgumentParser(description='strict evidence auditor (LONG-QUALITY-GOAL)')
    parser.add_argument('--expected', help='frozen expected-evidence.json (audit mode)')
    parser.add_argument('--out', help='report path (also used by freeze mode)')
    parser.add_argument('--exceptions', help='optional additional exceptions file')
    parser.add_argument('--root', action='append', default=[],
                        help='freeze mode: id=<absolute path>[:policy]')
    parser.add_argument('--new-package', action='append', default=[],
                        help='freeze mode: rootId=<relativePath>[:runId]')
    parser.add_argument('--allow-sibling', action='append', default=[],
                        help='freeze mode: rootId=<relativePath>[:reason]')
    parser.add_argument('--exceptions-file', dest='exceptions_file',
                        help='freeze mode: JSON file with an "exceptions" list to embed')
    args = parser.parse_args(argv)

    if args.root:
        if not args.out:
            raise SystemExit('freeze mode needs --out')
        freeze(args)
        return 0

    if not args.expected:
        raise SystemExit('audit mode needs --expected (or use --root to freeze)')
    audit = Audit(args.expected, args.exceptions)
    result = audit.run()
    if result == 2:
        return 2
    if args.out:
        write_json(args.out, result)
    summary = {
        'auditor': AUDITOR,
        'expected': result['expected'],
        'totals': result['totals'],
        'failureCodes': sorted({f['code'] for f in result['failures']}),
    }
    print(json.dumps(summary, ensure_ascii=False))
    return result['exitCode']


if __name__ == '__main__':
    sys.exit(main())
