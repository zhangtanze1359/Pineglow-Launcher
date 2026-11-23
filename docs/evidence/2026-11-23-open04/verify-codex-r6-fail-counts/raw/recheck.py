"""Read-only reconciliation of the archived OPEN-04 inputs and test results."""

import csv
from collections import Counter
from datetime import datetime
from hashlib import sha256
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


ROOT = Path.cwd()
SOURCE = Path("D:/Projects/松光启动器")
EVIDENCE = Path("docs/evidence/2026-11-23-open04")
TARGET = "327f8ff4d439ef3592dd8d37a6adbc6286eaf73d"
INPUTS = ["dev/", "scripts/test-inventory.txt", ".github/workflows/build.yml", "global.json"]
TIMING = re.compile(r"state=applied waited=([\d.]+)s")
TARGET_TEST = "Pineglow.Launcher.Tests.UpdateApplyScriptPathTests.ApplyScript_WaitsForRunningTargetThenReplaces"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
COUNTS = ("passed", "failed", "skipped", "total")
SUMMARY = re.compile(
    r"^\s*(?:(?:full |stress )?run|round)\s+(\d+): exit=(\d+) "
    r"passed=(\d*) failed=(\d*)(?: skipped=(\d+) total=(\d+))?"
)
REVIEW_DIR = "docs/evidence/2026-11-23-open04-review"
R1 = ROOT.parent / "t3-e7793f14-84e84c6d5686f841" / REVIEW_DIR
R2 = ROOT.parent / "t3-b8e17a78-c244607ac2e04676" / (REVIEW_DIR + "-r2")
R3 = ROOT.parent / "t3-0fc59f52-4e70cb11eb60f08e" / REVIEW_DIR


def git(*args, source=None, accepted=(0,)):
    cmd = ["git"]
    if source:
        cmd.extend(("-C", source.as_posix()))
    cmd.extend(args)
    result = subprocess.run(cmd, capture_output=True, encoding="utf-8")
    print("\n> " + " ".join(cmd))
    print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="")
    print(f"exit={result.returncode}")
    assert result.returncode in accepted
    return result.stdout.splitlines()


def read_text(path):
    data = path.read_bytes()
    encoding = "utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"
    return data.decode(encoding)


def lines(path):
    return read_text(path).splitlines()


def hash_file(path):
    print(f"SHA256 {sha256(path.read_bytes()).hexdigest()} {path}")


def executor_runs():
    raw = EVIDENCE / "raw"
    print("\nExecutor raw directory:", ", ".join(p.name for p in sorted(raw.iterdir())))
    trx = list(raw.rglob("*.trx"))
    print(f"Executor raw TRX count: {len(trx)}")
    assert not trx
    observed = Counter()
    directly_logged = Counter()
    counters = Counter()
    for file in sorted(raw.glob("*.txt")):
        print(f"\n> read {file}")
        hash_file(file)
        group = file.stem
        for number, line in enumerate(lines(file), 1):
            print(f"{number:2} {line}")
            if "=== filter:" in line:
                group = line.split("~")[1].split()[0]
            match = SUMMARY.match(line)
            if not match:
                continue
            _, exit_code, *counts = match.groups()
            assert exit_code == "0"
            observed[group] += 1
            if TIMING.search(line):
                directly_logged[group] += 1
            if all(field not in ("", None) for field in counts):
                counters.update(dict(zip(COUNTS, map(int, counts))))
            else:
                assert file.name == "open04-timing-waited.txt"
    print("Executor runs per group:", dict(observed))
    print("Executor timing lines per group:", dict(directly_logged))
    print("Executor xUnit summary totals:", dict(counters))
    assert list(observed.values()) == [10, 5, 5, 3, 3, 8]
    assert sum(directly_logged.values()) == 8
    assert counters == dict(zip(COUNTS, (9107, 0, 0, 9107)))


def trx_audit(label, raw, expected_runs, expected_totals):
    print(f"\n{label} TRX source: {raw}")
    runs = sorted(raw.glob("*.trx"))
    assert len(runs) == expected_runs
    outcomes = Counter()
    full_outcomes = Counter()
    target_observations = []
    full_test_ids = []
    for path in runs:
        hash_file(path)
        xml = ET.parse(path)
        counters = xml.find("t:ResultSummary/t:Counters", NS).attrib
        results = xml.findall("t:Results/t:UnitTestResult", NS)
        run_counts = Counter(item.get("outcome") for item in results)
        assert set(run_counts) <= {"Passed", "Failed", "NotExecuted"}
        assert len(results) == int(counters["total"])
        assert all(run_counts[key] == int(counters[attr]) for key, attr in (
            ("Passed", "passed"), ("Failed", "failed"), ("NotExecuted", "notExecuted")
        ))
        outcomes.update(run_counts)
        assert run_counts["Passed"] + run_counts["Failed"] == int(counters["executed"])
        if len(results) == 1502:
            test_ids = {item.get("testId"): item.get("testName") for item in results}
            assert len(test_ids) == 1502
            full_test_ids.append(test_ids)
            full_outcomes.update(run_counts)
        target = [item for item in results if item.get("testName") == TARGET_TEST]
        timing = ""
        if target:
            assert len(target) == 1 and target[0].get("outcome") == "Passed"
            timing = target[0].findtext("t:Output/t:StdOut", default="", namespaces=NS)
            match = TIMING.search(timing)
            assert match
            target_observations.append(float(match[1]))
        if label == "R4":
            console = read_text(path.with_suffix(".txt"))
            if timing:
                assert timing in console
        for item in results:
            if item.get("outcome") == "Failed":
                print("FAILED", item.get("testName"), item.findtext(
                    "t:Output/t:ErrorInfo/t:Message", namespaces=NS
                ))
        print(path.stem, "passed/failed/skipped/total:",
              run_counts["Passed"], run_counts["Failed"], run_counts["NotExecuted"], len(results), timing)
    assert all(full_test_ids[0] == test_ids for test_ids in full_test_ids)
    print(f"{label}: {len(runs)} runs, outcomes={dict(outcomes)}, "
          f"full_runs={len(full_test_ids)} full_outcomes={dict(full_outcomes)}, "
          f"target_applied={len(target_observations)} waited_range={min(target_observations)}–{max(target_observations)}s")
    assert tuple(outcomes[key] for key in ("Passed", "Failed", "NotExecuted")) == expected_totals
    return len(full_test_ids), len(target_observations)


def crosscheck_r4():
    raw = EVIDENCE / "verify-codex-r4-fail-wording/raw"
    with (raw / "runs.csv").open(encoding="utf-8-sig", newline="") as file:
        results = {row["run"]: row for row in csv.DictReader(file)}
    commands = {row["run"]: row for row in map(json.loads, lines(raw / "commands.jsonl"))}
    assert results.keys() == commands.keys() == {path.stem for path in raw.glob("*.trx")}
    for name, result in results.items():
        xml = ET.parse(raw / (name + ".trx"))
        counters = xml.find("t:ResultSummary/t:Counters", NS).attrib
        expected = (1 if name.startswith("target-") else 9 if name.startswith("paths-")
                    else 8 if name.startswith("concurrency-") else 1502)
        assert result["exit"] == "0" and commands[name]["expected"] == expected
        assert (int(result["expected"]), int(result["passed"]), int(result["total"])) == (expected,) * 3
        assert all(result[key] == counters[key] for key in ("passed", "failed", "total"))
        assert result["skipped"] == counters["notExecuted"]
        assert commands[name]["start"] == result["start"]
    print(f"R4 command/exit/TRX counters match: {len(results)}/19")


print("time=" + datetime.now().astimezone().isoformat())
print("> pwd\n" + str(ROOT))
git("rev-parse", "--show-toplevel")
assert git("rev-parse", "HEAD") == [TARGET]
git("status", "--short", "--untracked-files=all")
git("diff", "--stat")
git("diff", "--cached", "--stat")
assert git("rev-parse", "HEAD", "refs/heads/main", source=SOURCE) == [TARGET, TARGET]
assert not git("diff", "--exit-code", "63dadf7", "327f8ff", "--", *INPUTS, source=SOURCE)
changed = git("-c", "core.quotepath=false", "diff", "--name-only", "63dadf7", "327f8ff", source=SOURCE)
assert changed and all(path.startswith("docs/") for path in changed)
print(f"Changed paths: {len(changed)} total; {len(changed)} under docs/")
assert not git("diff", "--exit-code", "367ab91", TARGET, "--", ".", ":(exclude)docs/")
for baseline in ("25c7e2b", "320963f"):
    assert not git("diff", "--exit-code", baseline, TARGET, "--", *INPUTS)
for baseline in ("453f18f", "0748c6d", TARGET):
    status = subprocess.check_output(["git", "show", f"{baseline}:docs/open-items.md"], encoding="utf-8")
    print(f"\n> git show {baseline}:docs/open-items.md (relevant lines)")
    for number, line in enumerate(status.splitlines(), 1):
        if re.search(r"各轮一致|技术结论一致支持|第 2 次", line):
            print(f"{number}: {line[:400]}")
for filename in (
    "dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs",
    "dev/PineglowLauncher.Tests/UpdateConcurrencyTests.cs",
    "dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs",
    "dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs",
    ".github/workflows/build.yml", "scripts/test-inventory.txt", "global.json",
):
    hash_file(Path(filename))
executor_runs()
assert trx_audit("R4", EVIDENCE / "verify-codex-r4-fail-wording/raw", 19, (4567, 0, 0)) == (3, 16)
crosscheck_r4()
assert trx_audit("R1", R1 / "raw", 19, (4567, 0, 0)) == (3, 16)
assert trx_audit("R3", R3 / "raw", 23, (6071, 1, 0)) == (4, 17)
print(f"\nR2 source: {R2}")
print("R2 build tail:", lines(R2 / "build.txt")[-1])
print("R2 surviving TRX count:", len(list(R2.rglob("*.trx"))))
print("Frozen-target historical archive search:")
archived = git("-c", "core.quotepath=false", "ls-tree", "-r", "--name-only", TARGET,
               REVIEW_DIR, REVIEW_DIR + "-r2")
print("R1/R3 original evidence directories in frozen target:",
      sum(path.startswith(REVIEW_DIR + "/") for path in archived))
print("\nFrozen-target executor + R4 full rounds: 6 + 3 = 9; 9012 + 4506 = 13518 passed.")
print("Executor + R4 target appearances by the brief's method: 10 + 5 + 3 + 3 + 8 + 16 = 45.")
print("Executor + R4 directly recorded state=applied and waited: 0 + 0 + 0 + 0 + 8 + 16 = 24.")
print("Additional historical sources R1 + R3 (outside frozen target): 7 full rounds; 33 target state records.")
print("time=" + datetime.now().astimezone().isoformat())
