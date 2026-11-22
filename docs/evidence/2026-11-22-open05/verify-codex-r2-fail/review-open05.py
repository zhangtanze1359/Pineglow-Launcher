"""Independent OPEN-05 review runner. Run with the detached worktree path."""
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from datetime import datetime
from base64 import b64encode

worktree = Path(sys.argv[1]).resolve(strict=True)
evidence = Path(__file__).parent
raw = evidence / "raw"
raw.mkdir(exist_ok=True)
candidate = worktree / "scripts/audit-evidence-tree.ps1"
ledger = worktree / "scripts/audit-plan-ledger.ps1"
real = worktree / "docs/market-execution"
records = []
out_by_name = {}
env = {k: v for k, v in os.environ.items()
       if k.upper() != "PSEXECUTIONPOLICYPREFERENCE"}
ps = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def invoke(name, script, cwd, params, expected, *, splat=False):
    if splat:
        pairs = "; ".join(k + " = " + quote(v) for k, v in params.items())
        setup = "$boundArgs = @{" + pairs + "}\n"
        args = "@boundArgs"
    else:
        setup = ""
        args = " ".join("-" + k + " " + quote(v) for k, v in params.items())
    command = (
        "[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + setup +
        "try { & " + quote(script) + " " + args + "; exit $LASTEXITCODE }\n"
        "catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"
    )
    encoded = b64encode(command.encode("utf-16le")).decode("ascii")
    argv = [str(ps), "-NoProfile", "-NonInteractive", "-OutputFormat", "Text",
            "-EncodedCommand", encoded]
    started = datetime.now().astimezone().isoformat()
    result = subprocess.run(argv, cwd=cwd, env=env, capture_output=True, timeout=30)
    (raw / (name + ".out.txt")).write_bytes(result.stdout)
    (raw / (name + ".err.txt")).write_bytes(result.stderr)
    stdout = result.stdout.decode("utf-8-sig")
    stderr = result.stderr.decode("utf-8-sig")
    out_by_name[name] = stdout
    units = re.search(r"(?:扫描到|解析到) (\d+) 个 (?:run 目录|包行)", stdout)
    if units is None:
        units = re.search(r"解析到 (\d+) 个包行", stdout)
    findings = re.search(r"发现 (\d+) 处不一致", stdout)
    count = int(findings[1]) if findings else (
        0 if re.search(r"证据树内部自洽|台账与磁盘证据一致", stdout) else None)
    scannum = int(units[1]) if units else None
    match = re.search(r"\[(?:EVIDENCE_ROOT_[A-Z_]+|DEFAULT_ROOT_MISSING)\]", stderr)
    code = match[0] if match else ""
    got = (scannum, count, result.returncode, code)
    record = dict(name=name, cwd=str(cwd), time=started, command=command,
                  launcher="powershell.exe -NoProfile -NonInteractive -OutputFormat Text -EncodedCommand",
                  process_env="PSExecutionPolicyPreference omitted", expected=expected,
                  units=scannum, findings=count, exit=result.returncode, error=code,
                  matches=got == expected)
    records.append(record)
    return got


def make_fixture(root, extra=False):
    (root / "MR00/20261002-072004Z").mkdir(parents=True)
    run = root / "MR77/20261006-083004Z"
    run.mkdir(parents=True)
    deliverable = b"# Independent OPEN-05 review sample\n"
    (run / "deliverable.md").write_bytes(deliverable)
    (run / "manifest.json").write_text(json.dumps({
        "runId": run.name,
        "files": [{"path": "deliverable.md",
                   "sha256": hashlib.sha256(deliverable).hexdigest()}],
    }), encoding="ascii")
    if extra:
        (run / "extra.txt").write_bytes(b"Not recorded in manifest.\n")


def run_forms(label, script, root, other_cwd, expected):
    forms = [
        ("abs", root, root.parent),
        ("trailing", str(root) + "\\", root.parent),
        ("rel", root.name, root.parent),
        ("dot", ".\\" + root.name, root.parent),
        ("dotdot", "..\\" + root.name, other_cwd),
    ]
    for index, (form, arg, cwd) in enumerate(forms):
        invoke(label + "-" + form, script, cwd,
               dict(RepoRoot=worktree, EvidenceRoot=arg), expected[index])


def audit_body(stdout):
    return "\n".join(line for line in stdout.splitlines()
                     if not re.match(r"\s*(?:证据树|来源)\s*=", line))


with tempfile.TemporaryDirectory(prefix="_review-open05-", dir=worktree) as tmp:
    stage = Path(tmp).resolve(strict=True)
    if stage.parent != worktree:
        raise RuntimeError("Independent fixture directory escaped its worktree")
    parent_bytes = subprocess.check_output(
        ["git", "show", "ed2e92c:scripts/audit-evidence-tree.ps1"], cwd=worktree)
    parent = stage / "parent-audit-evidence-tree.ps1"
    parent.write_bytes(parent_bytes)
    base = stage / "复验 样本"
    clean = base / "docs/market-execution"
    dirty = stage / "含空格 有差异"
    drive_input = stage / "drive-fixture"
    make_fixture(clean)
    make_fixture(dirty, extra=True)
    make_fixture(drive_input)
    (drive_input / "cwd").mkdir()
    (stage / "no-default/docs").mkdir(parents=True)
    plain = stage / "ordinary.txt"
    plain.write_bytes(b"ordinary file\n")
    clean_expected = (2, 0, 0, "")
    dirty_expected = (2, 1, 1, "")
    real_expected = (61, 609, 1, "")
    parent_expected = (2, 2, 1, "")
    sibling = clean.parent / "other-cwd"
    sibling.mkdir()
    run_forms("fixture", candidate, clean, sibling, [clean_expected] * 5)
    run_forms("findings", candidate, dirty, base, [dirty_expected] * 5)
    run_forms("real", candidate, real, worktree / "docs/handoff", [real_expected] * 5)
    run_forms("parent", parent, clean, sibling, [clean_expected] + [parent_expected] * 4)

    invoke("tree-default", candidate, stage, {}, real_expected)
    invoke("tree-default-cjk", candidate, stage, dict(RepoRoot=base), clean_expected)
    invoke("tree-explicit-cjk", candidate, stage, dict(RepoRoot=worktree, EvidenceRoot=dirty),
           dirty_expected)
    plan = base / "docs/market-task-plan-2026-10-03.md"
    plan.write_text(
        "| ID | 任务 | 优先级 | 状态 |\n"
        "| --- | --- | --- | --- |\n"
        "| MR00 | legacy | P0 | 证据就绪 |\n"
        "| MR77 | fixture | P0 | 证据就绪 |\n",
        encoding="utf-8-sig")
    invoke("ledger-default", ledger, stage, {}, (31, 4, 1, ""))
    invoke("ledger-absolute", ledger, stage, dict(EvidenceRoot=real), (31, 4, 1, ""))
    invoke("ledger-default-cjk", ledger, stage, dict(RepoRoot=base), clean_expected)
    invoke("ledger-explicit-cjk", ledger, stage, dict(RepoRoot=base, EvidenceRoot=clean),
           clean_expected)
    for label, script in [("tree", candidate), ("ledger", ledger)]:
        for name, parameters, code in [
            ("empty", dict(RepoRoot=worktree, EvidenceRoot=""), "EVIDENCE_ROOT_BLANK"),
            ("blank", dict(RepoRoot=worktree, EvidenceRoot=" \t "), "EVIDENCE_ROOT_BLANK"),
            ("missing", dict(RepoRoot=worktree, EvidenceRoot=stage / "absent"),
             "EVIDENCE_ROOT_MISSING"),
            ("file", dict(RepoRoot=worktree, EvidenceRoot=plain), "EVIDENCE_ROOT_NOT_DIRECTORY"),
            ("missing-default", dict(RepoRoot=stage / "no-default"), "DEFAULT_ROOT_MISSING"),
        ]:
            invoke(label + "-" + name, script, stage, parameters, (None, None, 1, "[" + code + "]"))

    invoke("tree-splat-empty", candidate, stage, dict(RepoRoot=worktree, EvidenceRoot=""),
           (None, None, 1, "[EVIDENCE_ROOT_BLANK]"), splat=True)

    subst = Path(os.environ["SystemRoot"]) / "System32/subst.exe"
    drive = next(d + ":" for d in "ZYWXVUT" if not Path(d + ":\\").exists())
    mapping = subprocess.run([str(subst), drive, str(drive_input)], cwd=stage, capture_output=True)
    if mapping.returncode != 0:
        raise RuntimeError("Failed to create isolated test drive: " + repr(mapping.stderr))
    try:
        (raw / "drive-mapping.txt").write_bytes(subprocess.check_output([str(subst)], cwd=stage))
        invoke("drive-candidate", candidate, drive + "\\cwd",
               dict(RepoRoot=worktree, EvidenceRoot=drive + "\\"), clean_expected)
        invoke("drive-parent", parent, drive + "\\cwd",
               dict(RepoRoot=worktree, EvidenceRoot=drive + "\\"), clean_expected)
        invoke("drive-folder-control", candidate, stage,
               dict(RepoRoot=worktree, EvidenceRoot=drive_input), clean_expected)
    finally:
        subprocess.run([str(subst), drive, "/D"], cwd=stage, check=True)
    source = {
        "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=worktree, text=True).strip(),
        "parent_source": "git show ed2e92c:scripts/audit-evidence-tree.ps1",
        "parent_sha256": hashlib.sha256(parent_bytes).hexdigest().upper(),
        "tree_sha256": hashlib.sha256(candidate.read_bytes()).hexdigest().upper(),
        "ledger_sha256": hashlib.sha256(ledger.read_bytes()).hexdigest().upper(),
        "inputs": [str(clean), str(dirty), str(real), str(drive_input)],
        "real_files": sum(1 for p in real.rglob("*") if p.is_file()),
    }

same_bodies = {
    name: len({audit_body(out_by_name[name + "-" + form])
               for form in ("abs", "trailing", "rel", "dot", "dotdot")}) == 1
    for name in ("fixture", "findings", "real")
}
same_bodies["five_explicit_forms_full_stdout"] = all(
    len({out_by_name[name + "-" + form]
         for form in ("abs", "trailing", "rel", "dot", "dotdot")}) == 1
    for name in ("fixture", "findings", "real"))
same_bodies["drive_parent_vs_candidate_full_stdout"] = (
    out_by_name["drive-parent"] == out_by_name["drive-candidate"])
same_bodies["parent_vs_candidate_absolute"] = (
    audit_body(out_by_name["parent-abs"]) == audit_body(out_by_name["fixture-abs"]))
same_bodies["tree_default_vs_explicit_real"] = (
    audit_body(out_by_name["tree-default"]) == audit_body(out_by_name["real-abs"]))
same_bodies["ledger_default_vs_explicit_real"] = (
    audit_body(out_by_name["ledger-default"]) == audit_body(out_by_name["ledger-absolute"]))
(evidence / "results.json").write_text(json.dumps(
    dict(source=source, same_audit_bodies=same_bodies, cases=records),
    ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
with (evidence / "results.csv").open("w", newline="", encoding="utf-8-sig") as file:
    fields = ["name", "units", "findings", "exit", "error", "matches", "time", "cwd"]
    writer = csv.DictWriter(file, fieldnames=fields, extrasaction="ignore")
    writer.writeheader()
    writer.writerows(records)
for row in records:
    print(row["name"], row["units"], row["findings"], row["exit"], row["error"], row["matches"])
print(same_bodies)
sys.exit(0 if all(row["matches"] for row in records) and all(same_bodies.values()) else 1)

