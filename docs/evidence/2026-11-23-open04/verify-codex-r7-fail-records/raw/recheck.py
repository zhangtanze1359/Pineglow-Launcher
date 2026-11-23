"""Recount OPEN-04 source inputs, executor summaries, and original TRX files."""

import csv
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from collections import Counter
from datetime import datetime
from pathlib import Path
from statistics import mean

ROOT = Path.cwd()
EVIDENCE = ROOT / "docs/evidence/2026-11-23-open04"
TARGET = "ApplyScript_WaitsForRunningTargetThenReplaces"
TARGET_CLASS = "Pineglow.Launcher.Tests.UpdateApplyScriptPathTests"
TRX = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def git(*args):
    result = subprocess.run(
        ("git", *args), capture_output=True, encoding="utf-8", check=False
    )
    print("git", *args)
    print(result.stdout.rstrip() or "<empty>")
    if result.stderr:
        print(result.stderr.rstrip())
    print(f"exit={result.returncode}")
    assert result.returncode == 0
    return result.stdout.splitlines()


def read_executor():
    totals = Counter()
    timings = {}
    state_count = 0
    for file in sorted((EVIDENCE / "raw").glob("*.txt")):
        lines = file.read_text(encoding="utf-8-sig").splitlines()
        fields = Counter()
        samples = []
        elapsed = []
        for line in lines:
            counters = re.search(
                r"exit=(\d+) passed=(\d+) failed=(\d+) skipped=(\d+) total=(\d+)", line
            )
            if counters:
                code, passed, failed, skipped, total = map(int, counters.groups())
                assert passed + failed + skipped == total
                fields.update(
                    runs=1, passed=passed, failed=failed, skipped=skipped, total=total
                )
                assert code == 0
            if match := re.search(r"state=(\S+) waited=([\d.]+)s", line):
                samples.append((match[1], float(match[2])))
            if match := re.search(r"elapsed=([\d.]+)s", line):
                elapsed.append(float(match[1]))
        totals.update(fields)
        timings[file.name] = elapsed
        state_count += len(samples)
        print(file.name, dict(fields), "state/waited", samples, "elapsed", elapsed)
        if samples:
            print("waited mean", mean(sample[1] for sample in samples))
            print("timing rows with empty result counts", sum("exit=0 passed= failed=" in line for line in lines))
    ordinary = mean(timings["open04-layer3-full.txt"])
    loaded = mean(timings["open04-layer4-stress.txt"])
    print("executor totals", dict(totals), "direct state", state_count)
    print(f"ordinary={ordinary:.6f}s loaded={loaded:.6f}s increase={(loaded / ordinary - 1) * 100:.6f}%")
    print("executor top-level raw TRX", len(list((EVIDENCE / "raw").glob("*.trx"))))
    assert totals == Counter(runs=26, passed=9107, failed=0, skipped=0, total=9107)
    assert state_count == 8


def read_trx(label, folder, expected_pass, expected_fail, expected_full, commands):
    print(f"\n{label} {folder}")
    totals = Counter()
    direct = []
    nonpasses = []
    full = []
    case_sets = []
    target_from_substring = set()
    paths_from_substring = set()
    ace_evidence = Counter()
    for file in sorted(folder.glob("*.trx")):
        root = ET.parse(file).getroot()
        summary = root.find("./t:ResultSummary/t:Counters", TRX).attrib
        definitions = {}
        for definition in root.findall("./t:TestDefinitions/t:UnitTest", TRX):
            method = definition.find("./t:TestMethod", TRX).attrib
            definitions[definition.get("id")] = (method["className"], method["name"])
        results = root.findall("./t:Results/t:UnitTestResult", TRX)
        outcomes = Counter(node.get("outcome") for node in results)
        total = int(summary["total"])
        assert total == len(results) == len(definitions)
        assert outcomes["Passed"] == int(summary["passed"])
        assert outcomes["Failed"] == int(summary["failed"])
        assert outcomes["NotExecuted"] == int(summary["notExecuted"])
        assert set(outcomes) <= {"Passed", "Failed", "NotExecuted"}
        assert {node.get("testId") for node in results} == definitions.keys()
        totals.update(outcomes)
        if total == 1502:
            full.append(file.name)
            case_sets.append(set(definitions))
            if commands:
                assert "--filter" not in commands[file.stem][0]
            target_from_substring.update(
                method for method in definitions.values()
                if TARGET.lower() in ".".join(method).lower()
            )
            paths_from_substring.update(
                method for method in definitions.values()
                if "updateapplyscriptpathtests" in ".".join(method).lower()
            )
        target_notes = []
        for node in results:
            case = definitions[node.get("testId")]
            outcome = node.get("outcome")
            output = node.findtext("./t:Output/t:StdOut", default="", namespaces=TRX)
            if case == (TARGET_CLASS, TARGET):
                match = re.search(r"\[R06/upgrade-wait\] state=(\S+) waited=([\d.]+)s", output)
                assert outcome == "Passed" and match
                target_notes.append((match[1], float(match[2])))
            if case == (TARGET_CLASS, "ApplyScript_WhenTargetHasExplicitDenyAce_KeepsRunnableOldVersion"):
                if "[R06/upgrade-deny-ace] state=" in output:
                    ace_evidence["ran_script"] += 1
                elif "[R06/upgrade-deny-ace]" in output:
                    ace_evidence["other_log"] += 1
                else:
                    ace_evidence["no_log"] += 1
            if outcome != "Passed":
                message = node.findtext("./t:Output/t:ErrorInfo/t:Message", namespaces=TRX)
                stack = node.findtext("./t:Output/t:ErrorInfo/t:StackTrace", namespaces=TRX)
                nonpasses.append((file.name, case, outcome, message, stack))
        direct.extend(target_notes)
        sha = hashlib.sha256(file.read_bytes()).hexdigest()
        command_exit = commands[file.stem][1] if commands else "unavailable"
        print(file.name, f"exit={command_exit}", dict(outcomes), target_notes, f"sha256={sha}")
    print("full TRX", full)
    print("full test ID sets equal", all(case_sets[0] == each for each in case_sets))
    print("summary", dict(totals), "direct target states", len(direct), "ACE", dict(ace_evidence))
    print("target filter matches", sorted(target_from_substring))
    print("path class filter matches", sorted(paths_from_substring))
    print("nonpasses", nonpasses)
    assert len(list(folder.glob("*.trx"))) == len(commands)
    assert totals == Counter(Passed=expected_pass, Failed=expected_fail)
    assert len(full) == expected_full
    assert target_from_substring == {(TARGET_CLASS, TARGET)}
    assert len(paths_from_substring) == 9
    assert all(state == "applied" for state, _ in direct)


def main():
    print(datetime.now().astimezone().isoformat())
    print("pwd", ROOT)
    git("rev-parse", "--show-toplevel")
    git("rev-parse", "HEAD")
    inputs = ("dev/", "scripts/test-inventory.txt", ".github/workflows/build.yml", "global.json")
    main_checkout = ("-C", "D:/Projects/松光启动器")
    git(*main_checkout, "rev-parse", "HEAD")
    git(*main_checkout, "diff", "--exit-code", "63dadf7", "0bbc63e", "--", *inputs)
    changed = git(*main_checkout, "diff", "--name-only", "63dadf7", "0bbc63e")
    print("changed path count", len(changed))
    assert changed and all(path.lstrip('"').startswith("docs/") for path in changed)
    git("diff", "--exit-code", "367ab91", "63dadf7", "--", *inputs)
    for path in (
        ROOT / "dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs",
        ROOT / "dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs",
        EVIDENCE / "layer4-stress.ps1",
    ):
        raw = path.read_bytes()
        head = subprocess.check_output(("git", "show", f"HEAD:{path.relative_to(ROOT).as_posix()}"))
        lf = raw.replace(b"\r\n", b"\n")
        print(path.relative_to(ROOT), "disk", hashlib.sha256(raw).hexdigest(),
              "HEAD", hashlib.sha256(head).hexdigest(),
              "LF", hashlib.sha256(lf).hexdigest(),
              "CRLF", hashlib.sha256(lf.replace(b"\n", b"\r\n")).hexdigest())
    read_executor()
    r1 = ROOT.parent / "t3-0fc59f52-4e70cb11eb60f08e/docs/evidence/2026-11-23-open04-review"
    r3 = ROOT.parent / "t3-399f2fe4-9138d141e7975b6e/docs/evidence/2026-11-23-open04-review4"
    r4 = EVIDENCE / "verify-codex-r4-fail-wording/raw"
    with (r1 / "runs.csv").open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        keys = {name.lower(): name for name in reader.fieldnames}
        r1_commands = {
            row[keys["run"]]: (row[keys["command"]], row[keys["exit"]]) for row in reader
        }
    r3_rows = [json.loads(line) for line in (r3 / "rounds.jsonl").read_text(encoding="utf-8-sig").splitlines()]
    r3_commands = {row["run"]: (row["command"], row["exit"]) for row in r3_rows}
    print("round 3 original summary flags", dict(Counter(row["met_expected"] for row in r3_rows)),
          "console count fields", sum("xunit" in row for row in r3_rows))
    r4_rows = [json.loads(line) for line in (r4 / "commands.jsonl").read_text(encoding="utf-8-sig").splitlines()]
    with (r4 / "runs.csv").open(encoding="utf-8-sig", newline="") as stream:
        r4_exits = {row["run"]: row["exit"] for row in csv.DictReader(stream)}
    r4_commands = {row["run"]: (row["command"], r4_exits[row["run"]]) for row in r4_rows}
    read_trx("round 1", r1 / "raw", 6071, 1, 4, r1_commands)
    read_trx("round 3", r3, 3065, 0, 2, r3_commands)
    read_trx("round 4", r4, 4567, 0, 3, r4_commands)


if __name__ == "__main__":
    main()
