import csv
from collections import Counter
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

raw = Path(__file__).resolve().parent
rows = list(csv.DictReader((raw / "runs.csv").open(encoding="utf-8-sig")))
expected = (
    {f"target-{i:02}": 1 for i in range(1, 11)}
    | {f"paths-{i}": 9 for i in range(1, 4)}
    | {f"concurrency-{i}": 8 for i in range(1, 4)}
    | {"full-1": 1502, "full-2": 1502, "full-stress-1": 1502}
)
assert Counter(row["run"] for row in rows) == Counter(expected.keys())
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
all_full_names = None
target = "ApplyScript_WaitsForRunningTargetThenReplaces"
config = "ConfigPreservationTests.LoadAsync_IfItWritesBack_StillPreservesExistingFields"
sum_counts = [0, 0, 0, 0]
for row in rows:
    run = row["run"]
    xml = ET.parse(raw / f"{run}.trx")
    results = xml.findall("t:Results/t:UnitTestResult", ns)
    summary = xml.find("t:ResultSummary/t:Counters", ns)
    assert summary is not None, run
    counters = tuple(int(summary.get(key)) for key in ["passed", "failed", "notExecuted", "total"])
    outcomes = Counter(result.get("outcome") for result in results)
    calculated = (
        outcomes["Passed"],
        outcomes["Failed"],
        outcomes["NotExecuted"] + outcomes["Skipped"],
        len(results),
    )
    assert calculated == counters, (run, calculated, counters, outcomes)
    assert sum(counters[:3]) == counters[3] == expected[run], (run, counters)
    assert counters == tuple(int(row[key]) for key in ["passed", "failed", "skipped", "total"]), run
    assert int(row["exit"]) == int(counters[1] > 0), (run, row["exit"])
    for index, count in enumerate(counters):
        sum_counts[index] += count

    log = (raw / f"{run}.txt").read_text(encoding="utf-16")
    for labels, amount in [
        ("测试总数|Total tests", counters[3]),
        ("通过数|Passed", counters[0]),
        ("失败数|Failed", counters[1]),
        ("跳过数|Skipped", counters[2]),
    ]:
        found = re.findall(rf"^\s*(?:{labels}):\s*(\d+)\s*$", log, re.M)
        assert (int(found[-1]) if found else 0) == amount, (run, labels, found, amount)
    names = Counter(test.get("testName") for test in results)
    if run.startswith("full-"):
        identities = {(test.get("testId"), test.get("testName")) for test in results}
        assert len({test_id for test_id, _ in identities}) == len(identities) == 1502, run
        if all_full_names is None:
            all_full_names = identities
        assert all_full_names == identities, run
    if run.startswith("target-"):
        assert len(names) == 1 and next(iter(names)).endswith(target), run
    matches = [result for result in results if result.get("testName").endswith(target)]
    if not run.startswith("concurrency-"):
        assert len(matches) == 1, run
        stdout = matches[0].findtext("t:Output/t:StdOut", namespaces=ns) or ""
        waited = re.search(r"\[R06/upgrade-wait\] state=(\S+) waited=(\d+(?:[.,]\d+)?)s", stdout)
        assert waited and waited[0] == row["timing"], (run, stdout, row["timing"])
        timing = " ".join(waited.groups())
    else:
        assert not matches and not row["timing"], run
        timing = "-"
    if run.startswith(("paths-", "full-")):
        assert "[R06/upgrade-deny-ace] state=replace_failed target=OLD-CONTENT" in log, run
    config_outcomes = [result.get("outcome") for result in results if result.get("testName").endswith(config)]
    print(f"{run}: exit={row['exit']} counts={counters} target={timing} config={config_outcomes}")
    for result in results:
        if result.get("outcome") == "Failed":
            print(f"  Failed: {result.get('testName')}")
print(f"PASS: {len(rows)} TRX, console and CSV cross-checked; aggregate passed/failed/skipped/total={tuple(sum_counts)}.")
print(f"Full (test ID, display name) sets match: {len(all_full_names)} unique test IDs, {len({name for _, name in all_full_names})} display names.")


