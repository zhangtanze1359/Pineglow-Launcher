import base64
import hashlib
import json
import os
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path
from shutil import copytree

worktree, fixture, evidence = (Path(item).resolve() for item in sys.argv[1:])
fixture.mkdir()
raw = evidence / "raw"
raw.mkdir()
native_env = dict(os.environ)
native_env.pop("PSExecutionPolicyPreference", None)
tree = worktree / "scripts/audit-evidence-tree.ps1"
ledger = worktree / "scripts/audit-plan-ledger.ps1"
parent = fixture / "parent-ed2e92c.ps1"
parent_cmd = ["git", "show", "ed2e92c:scripts/audit-evidence-tree.ps1"]
parent_result = subprocess.run(parent_cmd, cwd=worktree, capture_output=True, check=True)
parent.write_bytes(parent_result.stdout)
(evidence / "parent.txt").write_text(
    f"cwd={worktree}\ncommand={parent_cmd}\nexit=0\nsha256={hashlib.sha256(parent_result.stdout).hexdigest()}\n",
    encoding="utf-8",
)
repo = fixture / "审计 仓库"
good = repo / "docs/market-execution"
override_root = fixture / "显式 中文证据"
missing_repo = fixture / "empty-repo"
elsewhere = fixture / "other-cwd"
current_id = "20261122-010203Z"
legacy_id = "20261002-072004Z"
current_run = good / "MR77" / current_id
legacy_run = good / "MR00" / legacy_id
for path in (current_run, legacy_run, missing_repo, elsewhere):
    path.mkdir(parents=True)
deliverable = current_run / "deliverable.md"
deliverable.write_text("Independent review fixture\n", encoding="utf-8")
manifest = {"runId": current_id, "files": [
    {"path": "deliverable.md", "sha256": hashlib.sha256(deliverable.read_bytes()).hexdigest()}
]}
(current_run / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
copytree(good, override_root)
(override_root / "MR77/20261122-112233Z").mkdir()
plan = repo / "docs/market-task-plan-2026-10-03.md"
plan.write_text(
    "\n".join((
        "| ID | 任务 | 优先级 | 状态 |",
        "| --- | --- | --- | --- |",
        "| MR00 | Legacy run | P0 | 证据就绪 |",
        "| MR77 | Current run | P0 | 证据就绪 |",
        "",
        f"Current references: {chr(96)}{legacy_id}{chr(96)}, {chr(96)}{current_id}{chr(96)}.",
    )), encoding="utf-8",
)
regular_file = fixture / "regular.txt"
regular_file.write_text("Not a directory\n", encoding="utf-8")
missing_plan = fixture / "missing-plan.md"
default_source = r"默认（-RepoRoot\docs\market-execution）"
explicit_source = "显式 -EvidenceRoot"
clean_tree = r"(?s)扫描到 2 个 run 目录.*证据树内部自洽"
clean_ledger = r"(?s)解析到 2 个包行.*台账与磁盘证据一致"
red_tree = r"(?s)扫描到 2 个 run 目录.*\[缺少 deliverable\.md\].*\[缺少 manifest\.json\].*发现 2 处不一致"
override_tree = r"(?s)扫描到 3 个 run 目录.*20261122-112233Z.*发现 2 处不一致"
real_tree = r"(?s)扫描到 [1-9][0-9]* 个 run 目录.*发现 [1-9][0-9]* 处不一致"
real_ledger = r"(?s)解析到 [1-9][0-9]* 个包行.*发现 [1-9][0-9]* 处不一致"
powershell = Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
results = {}
outputs = {}

def literal(value):
    return "'" + str(value).replace("'", "''") + "'"

def compare_body(output):
    return [line for line in output.splitlines() if not line.startswith(("  证据树 = ", "  来源   = "))]

def audit(name, script, args, exit_code, *, pattern="", root=None, source=None,
          tag=None, control=None, cwd=fixture, setup=""):
    statement = f"& {literal(script)} {args}"
    body = "\n".join((
        "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)",
        "$ErrorActionPreference = 'Stop'",
        setup,
        statement,
        "exit $LASTEXITCODE",
    ))
    command = [str(powershell), "-NoProfile", "-NonInteractive", "-EncodedCommand",
               base64.b64encode(body.encode("utf-16le")).decode("ascii")]
    started = datetime.now().astimezone().isoformat()
    proc = subprocess.run(command, cwd=cwd, env=native_env, capture_output=True, timeout=45)
    out_path, err_path = raw / f"{name}.stdout.txt", raw / f"{name}.stderr.txt"
    out_path.write_bytes(proc.stdout)
    err_path.write_bytes(proc.stderr)
    stdout = proc.stdout.decode("utf-8", errors="replace")
    stderr = proc.stderr.decode("utf-8", errors="replace")
    outputs[name] = stdout
    body_equal = None
    if control:
        body_equal = compare_body(stdout) == compare_body(outputs[control])
    actual_checks = {"exit": proc.returncode == exit_code}
    if tag:
        actual_checks["tag"] = f"[{tag}]" in stderr
        actual_checks["stdout_empty"] = proc.stdout == b""
    else:
        actual_checks["audit_body"] = bool(re.search(pattern, stdout))
        actual_checks["no_error_record"] = '<S S="Error">' not in stderr
        actual_checks["selected_root"] = f"  证据树 = {root}" in stdout.splitlines()
        actual_checks["root_source"] = f"  来源   = {source}" in stdout.splitlines()
    if control:
        actual_checks["body_equal"] = body_equal
    result = dict(
        timestamp=started, command=command, decoded=body, cwd=str(cwd),
        expected_exit=exit_code, actual_exit=proc.returncode, expected_pattern=pattern,
        expected_path=str(root) if root else None, expected_source=source, expected_tag=tag,
        same_body_as=control, checks=actual_checks, passed=all(actual_checks.values()),
        stdout=str(out_path), stderr=str(err_path),
    )
    results[name] = result
    print(f"{name:<27} {'PASS' if result['passed'] else 'FAIL'} exit={proc.returncode} {actual_checks}", flush=True)

origin = f"-RepoRoot {literal(repo)}"
for kind, script, clean, override_exit, override_pattern in (
    ("tree", tree, clean_tree, 1, override_tree),
    ("ledger", ledger, clean_ledger, 0, clean_ledger),
):
    guard = f" -PlanPath {literal(missing_plan)}" if kind == "ledger" else ""
    audit(f"{kind}-default", script, origin, 0, pattern=clean, root=good, source=default_source)
    audit(f"{kind}-default-explicit", script, f"{origin} -EvidenceRoot {literal(good)}", 0,
          pattern=clean, root=good, source=explicit_source, control=f"{kind}-default")
    audit(f"{kind}-cjk-precedence", script, f"{origin} -EvidenceRoot {literal(override_root)}",
          override_exit, pattern=override_pattern, root=override_root, source=explicit_source)
    audit(f"{kind}-default-missing", script, f"-RepoRoot {literal(missing_repo)}{guard}", 1,
          tag="DEFAULT_ROOT_MISSING")
    for case, value, tag in (
        ("blank", " \t ", "EVIDENCE_ROOT_BLANK"),
        ("empty", "", "EVIDENCE_ROOT_BLANK"),
        ("missing", fixture / "nonexistent", "EVIDENCE_ROOT_MISSING"),
        ("file", regular_file, "EVIDENCE_ROOT_NOT_DIRECTORY"),
    ):
        audit(f"{kind}-{case}", script, f"{origin} -EvidenceRoot {literal(value)}{guard}", 1, tag=tag)
    audit(f"{kind}-splat-empty", script, f"@p{guard}", 1, tag="EVIDENCE_ROOT_BLANK",
          setup=f"$p = @{{ RepoRoot = {literal(repo)}; EvidenceRoot = '' }}")

def path_case(name, value, cwd=fixture, selected_tree=good):
    args = f"{origin} -EvidenceRoot {literal(value)}"
    audit(f"tree-path-{name}", tree, args, 0, pattern=clean_tree, root=selected_tree,
          source=explicit_source, control="tree-default", cwd=cwd)
    audit(f"ledger-path-{name}", ledger, args, 0, pattern=clean_ledger, root=value,
          source=explicit_source, control="ledger-default", cwd=cwd)
    if name == "abs":
        audit(f"parent-path-{name}", parent, args, 0, pattern=clean_tree, root=value,
              source=explicit_source, control="tree-default", cwd=cwd)
    else:
        audit(f"parent-path-{name}", parent, args, 1, pattern=red_tree, root=value,
              source=explicit_source, cwd=cwd)

relative = str(good.relative_to(fixture))
path_case("abs", str(good))
path_case("trail", str(good) + "\\")
path_case("relative", relative)
path_case("dot", ".\\" + relative)
path_case("dotdot", "..\\" + relative, cwd=elsewhere)

def subst(*args):
    return subprocess.run(["subst.exe", *args], capture_output=True, cwd=fixture, timeout=15)

def item(proc):
    return dict(command=proc.args, cwd=str(fixture), exit=proc.returncode,
                stdout=proc.stdout.decode("mbcs", errors="replace"),
                stderr=proc.stderr.decode("mbcs", errors="replace"),
                timestamp=datetime.now().astimezone().isoformat())

before = subst()
before.check_returncode()
letter = next(letter for letter in "ZYXWVUTSRQPONMLKJIHGFE"
              if not Path(letter + ":\\").exists() and not re.search(
                  ("^" + letter + ":"), item(before)["stdout"], re.M | re.I))
drive = letter + ":"
drive_root = drive + "\\"
create = subst(drive, str(good))
mapping = {"before": item(before), "create": item(create)}
try:
    create.check_returncode()
    active = subst()
    active.check_returncode()
    mapping["active"] = item(active)
    assert (Path(drive_root) / "MR00" / legacy_id).is_dir()
    path_case("volume", drive_root, selected_tree=drive_root)
finally:
    if create.returncode == 0:
        removed, after = subst(drive, "/D"), subst()
        mapping.update(remove=item(removed), after=item(after), root_exists_after=Path(drive_root).exists())
    (evidence / "subst.json").write_text(json.dumps(mapping, indent=2, ensure_ascii=False), encoding="utf-8")
    if create.returncode == 0:
        removed.check_returncode()
        after.check_returncode()
        assert after.stdout == before.stdout and not mapping["root_exists_after"]

real_root = worktree / "docs/market-execution"
for kind, script, pattern in (("tree", tree, real_tree), ("ledger", ledger, real_ledger)):
    audit(f"real-{kind}-default", script, "", 1, pattern=pattern, root=real_root,
          source=default_source, cwd=worktree)
    audit(f"real-{kind}-explicit", script, f"-EvidenceRoot {literal(real_root)}", 1, pattern=pattern,
          root=real_root, source=explicit_source, control=f"real-{kind}-default", cwd=worktree)
audit("parent-real-tree-abs", parent, f"-RepoRoot {literal(worktree)} -EvidenceRoot {literal(real_root)}",
      1, pattern=real_tree, root=real_root, source=explicit_source, control="real-tree-explicit", cwd=worktree)

(evidence / "matrix.json").write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8")
passed = sum(result["passed"] for result in results.values())
print(f"total={len(results)} passed={passed} failed={len(results)-passed} skipped=0")
sys.exit(int(passed != len(results)))


