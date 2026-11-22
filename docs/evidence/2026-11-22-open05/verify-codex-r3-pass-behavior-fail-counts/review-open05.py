import base64
import ctypes
import hashlib
import json
import os
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path


WT = Path(r'D:\Projects\_worktrees\pg-open05-review3')
SOURCE = Path(r'D:\Projects\松光启动器')
EVIDENCE = Path(__file__).resolve().parent
CANDIDATE = 'fe49b34'
PARENT = 'ed2e92c'
R3 = '932115c'
ARTICLES = 'docs/evidence/2026-11-22-open05'
TREE = 'scripts/audit-evidence-tree.ps1'
LEDGER = 'scripts/audit-plan-ledger.ps1'
RUN = EVIDENCE / 'verified-run'
RAW = RUN / 'raw'
ENV = os.environ.copy()
ENV.pop('PSExecutionPolicyPreference', None)
CASES = {}


def git(*args, cwd=WT):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', *args], cwd=cwd)


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def quoted(value):
    return "'" + str(value).replace("'", "''") + "'"


def now():
    return datetime.now().astimezone().isoformat()


def stderr_errors(stderr):
    """Keep the text and error records, excluding verified host/progress serialization."""
    errors = []
    for line in stderr.splitlines():
        if line == '#< CLIXML':
            continue
        if line.startswith('<Objs '):
            records = ET.fromstring(line)
            if all(record.get('S') in {'progress', 'information'} for record in records):
                continue
        errors.append(line)
    return '\n'.join(errors)


def run(name, script, repo, *, value=None, cwd=WT, plan=None, splat=False, expected):
    args = f' -RepoRoot {quoted(repo)}'
    if value is not None:
        args += f' -EvidenceRoot {quoted(value)}'
    if plan is not None:
        args += f' -PlanPath {quoted(plan)}'
    call = f'& {quoted(script)}{args}'
    if splat:
        call = f"$reviewArgs = @{{ RepoRoot = {quoted(repo)}; EvidenceRoot = '' }}; & {quoted(script)} @reviewArgs"
    decoded = '\n'.join([
        '[Console]::OutputEncoding = [Text.Encoding]::UTF8',
        "$ErrorActionPreference = 'Stop'",
        "$ProgressPreference = 'SilentlyContinue'",
        f'try {{ {call}; exit $LASTEXITCODE }} catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}',
    ])
    encoded = base64.b64encode(decoded.encode('utf-16-le')).decode('ascii')
    command = ['powershell.exe', '-NoProfile', '-NonInteractive', '-OutputFormat', 'Text', '-EncodedCommand', encoded]
    started = now()
    result = subprocess.run(command, cwd=cwd, env=ENV, capture_output=True, timeout=60)
    (RAW / f'{name}.out.txt').write_bytes(result.stdout)
    (RAW / f'{name}.err.txt').write_bytes(result.stderr)
    stdout = result.stdout.decode('utf-8-sig')
    stderr = stderr_errors(result.stderr.decode('utf-8-sig'))
    scan = re.search(r'(?:扫描到|解析到) (\d+) 个', stdout)
    finding = re.search(r'发现 (\d+) 处不一致', stdout)
    is_clean = '证据树内部自洽' in stdout or '台账与磁盘证据一致' in stdout
    errors = re.findall(r'\[([A-Z][A-Z_]+)\]', stderr)
    actual = [int(scan[1]) if scan else None, int(finding[1]) if finding else (0 if is_clean else None), result.returncode]
    passed = actual == expected[:3]
    if len(expected) == 4:
        signal = expected[3]
        passed = passed and signal in (stderr if not scan else stdout)
        if not scan:
            passed = passed and not stdout.strip() and not re.search(r'扫描到|解析到', stderr)
    if scan:
        passed = passed and not stderr.strip()
    CASES[name] = dict(time=started, cwd=str(cwd), command=command[:-1] + ['<UTF-16LE Base64 of decoded>'],
                       decoded=decoded, call_body=call, expected=expected, actual=actual, codes=errors, passed=passed)
    print(f'{name:<32} {actual!s:<20} {"PASS" if passed else "FAIL"}', flush=True)
    return stdout


def make_run(root, pkg, run_id, *, unlisted=False):
    folder = root / pkg / run_id
    folder.mkdir(parents=True)
    report = b'# Independent OPEN-05 review\n'
    (folder / 'deliverable.md').write_bytes(report)
    write_json(folder / 'manifest.json', {'runId': run_id, 'files': [{'path': 'deliverable.md', 'sha256': sha(report)}]})
    if unlisted:
        (folder / 'unlisted.txt').write_bytes(b'intentional unlisted evidence\n')


def audit_body(output):
    return '\n'.join(line for line in output.splitlines() if not re.match(r'\s*(证据树|来源)\s*=', line))


def drive_command(journal, *args):
    proc = subprocess.run(['subst.exe', *args], cwd=WT, capture_output=True, check=True)
    journal.append(dict(time=now(), command=['subst.exe', *args], cwd=str(WT), exit=proc.returncode,
                        stdout=proc.stdout.decode('mbcs', 'replace'), stderr=proc.stderr.decode('mbcs', 'replace')))
    write_json(RUN / 'subst.json', journal)
    return proc.stdout


def exercise(tmp, checks):
    current = WT / TREE
    ledger = WT / LEDGER
    parent = tmp / 'ed2e92c-tree.ps1'
    previous = tmp / '932115c-tree.ps1'
    for rev, path in ((PARENT, parent), (R3, previous)):
        data = git('show', f'{rev}:{TREE}')
        path.write_bytes(data)
        checks[f'{rev}_tree_sha256'] = sha(data)

    self_tree = tmp / 'complete'
    legacy_tree = tmp / '白名单 夹具'
    dirty_tree = tmp / 'with-finding'
    for root in (self_tree, legacy_tree, dirty_tree):
        make_run(root, 'review-package', '20261122-000000Z')
    historical = legacy_tree / 'MR00' / '20261002-072004Z'
    historical.mkdir(parents=True)
    (historical / 'historical-note.txt').write_bytes(b'pre-convention evidence\n')
    make_run(dirty_tree, 'review-package', '20261122-000001Z', unlisted=True)
    fixtures = {
        label: {'dirs': sorted(p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_dir()),
                'files': {p.relative_to(root).as_posix(): sha(p.read_bytes()) for p in sorted(root.rglob('*')) if p.is_file()}}
        for label, root in [('complete', self_tree), ('legacy', legacy_tree), ('dirty', dirty_tree)]
    }
    write_json(RUN / 'independent-fixtures.json', fixtures)

    elsewhere = tmp / 'elsewhere'
    elsewhere.mkdir()
    plan = WT / 'docs/market-task-plan-2026-10-03.md'
    clean_repo = tmp / '测试 中文 repo'
    empty = tmp / 'empty'
    empty.mkdir()
    no_default = tmp / 'missing-default'
    no_default.mkdir()
    plain = tmp / 'ordinary.txt'
    plain.write_bytes(b'ordinary file\n')
    make_run(clean_repo / 'docs/market-execution', 'MR00', '20261122-000000Z')
    real_tree = WT / 'docs/market-execution'
    for label, script, count, findings in [('tree', current, 61, 609), ('ledger', ledger, 31, 4)]:
        default = run(f'{label}-default', script, WT, expected=[count, findings, 1, '默认（-RepoRoot'])
        explicit = run(f'{label}-override', script, WT, value=real_tree, expected=[count, findings, 1, '显式 -EvidenceRoot'])
        checks[f'{label}_default_explicit_body_equal'] = audit_body(default) == audit_body(explicit)
        for case, value, code in [('empty', '', 'EVIDENCE_ROOT_BLANK'), ('whitespace', ' \t ', 'EVIDENCE_ROOT_BLANK'),
                                  ('nonexistent', tmp / 'uncreated', 'EVIDENCE_ROOT_MISSING'),
                                  ('file', plain, 'EVIDENCE_ROOT_NOT_DIRECTORY')]:
            run(f'{label}-{case}', script, WT, value=value, expected=[None, None, 1, f'[{code}]'])
        run(f'{label}-missing-default', script, no_default, expected=[None, None, 1, '[DEFAULT_ROOT_MISSING]'])
        default_cjk = run(f'{label}-cjk-default', script, clean_repo, plan=plan if script == ledger else None,
                          expected=[31, 19, 1, '默认（-RepoRoot'] if script == ledger else [1, 0, 0, '默认（-RepoRoot'])
        explicit_cjk = run(f'{label}-cjk-explicit', script, clean_repo, value=clean_repo / 'docs/market-execution',
                           plan=plan if script == ledger else None,
                           expected=[31, 19, 1, '显式 -EvidenceRoot'] if script == ledger else [1, 0, 0, '显式 -EvidenceRoot'])
        checks[f'{label}_cjk_default_explicit_body_equal'] = audit_body(default_cjk) == audit_body(explicit_cjk)
        run(f'{label}-empty-tree', script, WT, value=empty,
            expected=[31, 20, 1, '[缺证据目录]'] if script == ledger else [0, 0, 0])
    run('tree-splat-empty', current, WT, splat=True, expected=[None, None, 1, '[EVIDENCE_ROOT_BLANK]'])
    run('tree-reporoot-empty', current, '', expected=[None, None, 1, 'because it is an empty string'])
    for form, arg, cwd in [('trailing', str(real_tree) + '\\', WT), ('relative', r'docs\market-execution', WT)]:
        result = run(f'ledger-{form}', ledger, WT, value=arg, cwd=cwd, expected=[31, 4, 1])
        checks[f'ledger_{form}_body_equal'] = audit_body(result) == audit_body(explicit)
    real_parent = run('parent-real-absolute', parent, WT, value=real_tree, expected=[61, 609, 1])
    real_current = (RAW / 'tree-override.out.txt').read_text(encoding='utf-8-sig')
    checks['real_parent_candidate_body_equal'] = audit_body(real_parent) == audit_body(real_current)

    journal = []
    initial = drive_command(journal)
    logical_drives = ctypes.windll.kernel32.GetLogicalDrives()
    drive = next(letter + ':' for letter in 'TUSRQ' if not logical_drives & (1 << (ord(letter) - ord('A'))))
    root_path = drive + '\\'
    tests = [('self', self_tree, [1, 0, 0]), ('legacy', legacy_tree, [2, 0, 0]),
             ('dirty', dirty_tree, [2, 1, 1, r'20261122-000001Z :: unlisted.txt'])]
    for label, tree, expected in tests:
        forms = [('abs', tree, tmp), ('trailing', str(tree) + '\\', tmp), ('relative', tree.name, tmp),
                 ('dot', '.\\' + tree.name, tmp), ('dotdot', '..\\' + tree.name, elsewhere)]
        candidate = {}
        for form, arg, cwd in forms:
            candidate[form] = run(f'{label}-{form}', current, WT, value=arg, cwd=cwd, expected=expected)
            parent_expected = [2, 2, 1] if label == 'legacy' and form != 'abs' else expected
            run(f'parent-{label}-{form}', parent, WT, value=arg, cwd=cwd, expected=parent_expected)
        checks[f'{label}_five_bodies_equal'] = len(set(map(audit_body, candidate.values()))) == 1
        drive_command(journal, drive, str(tree))
        try:
            mapping = drive_command(journal)
            checks[f'{label}_drive_reported'] = drive.encode() in mapping and Path(root_path).is_dir()
            candidate_drive = run(f'{label}-drive', current, WT, value=root_path, expected=expected)
            checks[f'{label}_drive_body_equal'] = audit_body(candidate_drive) == audit_body(candidate['abs'])
            run(f'parent-{label}-drive', parent, WT, value=root_path,
                expected=[2, 2, 1] if label == 'legacy' else expected)
            if label == 'legacy':
                run('r3-legacy-drive', previous, WT, value=root_path, expected=[2, 2, 1, r'R00\20261002-072004Z'])
        finally:
            drive_command(journal, drive, '/d')
            checks[f'{label}_subst_restored'] = drive_command(journal) == initial and not Path(root_path).exists()
    return drive


def check_worker(checks):
    evidence = WT / ARTICLES
    paths = [TREE, LEDGER, f'{ARTICLES}/matrix-fixture.ps1', f'{ARTICLES}/open05-fix-r2.patch',
             f'{ARTICLES}/patched/audit-evidence-tree.ps1', f'{ARTICLES}/patched/audit-plan-ledger.ps1']
    checks['hashes'] = {path: {'checkout': sha((WT / path).read_bytes()), 'git_blob': sha(git('show', f'{CANDIDATE}:{path}'))}
                        for path in paths}
    checks['patched_files_match_scripts'] = [(WT / path).read_bytes() == (evidence / 'patched' / Path(path).name).read_bytes()
                                             for path in (TREE, LEDGER)]
    raw = evidence / 'raw'
    counts = dict(metadata=0, streams=0, counterproof=0, probes=0, r3=0)
    metadata = {}
    for p in sorted(raw.iterdir()):
        assert p.is_file(), p
        name = p.name
        if name.startswith('counterproof-'):
            counts['counterproof'] += 1
        elif name.startswith('probe-'):
            counts['probes'] += 1
        elif name.startswith('tree-pathform-'):
            counts['r3'] += 1
        elif name.endswith(('.out.txt', '.err.txt')):
            counts['streams'] += 1
        else:
            counts['metadata'] += 1
            info = dict(line.split('=', 1) for line in p.read_text(encoding='utf-8-sig').splitlines()
                        if re.match(r'^\w+=', line))
            stdout = (raw / f'{p.stem}.out.txt').read_text(encoding='utf-8-sig')
            stderr = (raw / f'{p.stem}.err.txt').read_text(encoding='utf-8-sig')
            regex = info['expect_pattern']
            exact = bool(re.search(regex, stdout, re.IGNORECASE) or re.search(regex, stderr, re.IGNORECASE))
            multiline = bool(re.search(regex, stdout, re.IGNORECASE | re.DOTALL) or re.search(regex, stderr, re.IGNORECASE | re.DOTALL))
            metadata[p.stem] = dict(verdict=info['verdict'], actual_exit=int(info['actual_exit']),
                                    expected_exit=int(info['expect_exit']), regex=regex, exact_regex_match=exact,
                                    dotall_regex_match=multiline, call_body=info['call_body'],
                                    scan_flag_matches=(info['stdout_shows_scan'] == str(bool(re.search(r'扫描到|解析到', stdout)))))
    checks['metadata_comparison'] = metadata
    checks['raw_counts'] = counts | {'total': sum(counts.values())}
    checks['counterproof_breakdown'] = {name: sum(p.name.startswith(f'counterproof-{name}-') for p in raw.iterdir())
                                        for name in ('P', 'F')}
    old_paths = git('ls-tree', '-r', '--name-only', R3, '--', f'{ARTICLES}/raw').decode().splitlines()
    old_files = [p for p in old_paths if Path(p).name.startswith(('tree-pathform-', 'counterproof-'))]
    checks['r3_historical_blobs'] = {path: git('rev-parse', f'{R3}:{path}').strip().decode() ==
                                    git('rev-parse', f'{CANDIDATE}:{path}').strip().decode() for path in old_files}
    checks['archived_review_file_counts'] = {p.name: sum(1 for f in p.rglob('*') if f.is_file()) for p in
                                            (evidence / 'verify-codex-r1-fail', evidence / 'verify-codex-r2-fail')}
    checks['splat_decoded_matches_body'] = (f"try {{ {metadata['tree-splat-empty']['call_body']}; exit $LASTEXITCODE }}" in
                                          (raw / 'tree-splat-empty.txt').read_text(encoding='utf-8-sig'))


if __name__ == '__main__':
    RAW.mkdir(parents=True, exist_ok=False)
    checks = {'start': now(), 'cwd': str(Path.cwd()), 'worktree': str(WT),
              'head': git('rev-parse', 'HEAD').strip().decode(), 'main': git('rev-parse', 'main').strip().decode(),
              'before_status': git('status', '--short', '--untracked-files=all').decode(),
              'range_scripts': git('diff', '--name-status', R3, CANDIDATE, '--', 'scripts/').decode(),
              'review_driver_sha256': sha(Path(__file__).read_bytes())}
    assert checks['head'] == checks['main'] and checks['head'].startswith(CANDIDATE)
    for filename, result in [
        ('scope-show-stat.txt', git('show', '--stat', CANDIDATE, cwd=SOURCE)),
        ('scope-scripts.diff.txt', git('diff', R3, CANDIDATE, '--', 'scripts/', cwd=SOURCE)),
        ('scope-parent-chain.txt', git('log', '-6', '--format=%H %P %s', CANDIDATE, cwd=SOURCE)),
    ]:
        (EVIDENCE / filename).write_bytes(result)
    check_worker(checks)
    try:
        with tempfile.TemporaryDirectory(prefix='_independent-review-', dir=WT) as scratch:
            tmp = Path(scratch).resolve()
            assert tmp.parent == WT.resolve() and tmp.name.startswith('_independent-review-')
            checks['drive'] = exercise(tmp, checks)
    finally:
        checks['end'] = now()
        write_json(RUN / 'results.json', CASES)
        write_json(RUN / 'evidence-checks.json', checks)
    print(f'{sum(case["passed"] for case in CASES.values())}/{len(CASES)} behavioral expectations met')
    raise SystemExit(int(not all(case['passed'] for case in CASES.values())))
