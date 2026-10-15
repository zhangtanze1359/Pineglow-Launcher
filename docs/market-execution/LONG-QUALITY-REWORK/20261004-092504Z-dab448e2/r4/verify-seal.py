"""Independent cross-check of the sealed LONG-QUALITY-REWORK package.

Deliberately a different implementation (Python stdlib) from the Node sealer, so the seal is
verified by a second, independent code path. Writes its report OUTSIDE the package only.

Usage: python verify-seal.py <evidenceRoot>
"""
import hashlib
import json
import os
import sys
from datetime import datetime, timezone

RUN_ID = "20261004-092504Z-dab448e2-rework"


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest().upper()


def main(root):
    pkg = os.path.join(root, "r4", "FINAL-LOCAL-QUALITY", RUN_ID)
    manifest_path = os.path.join(pkg, "manifest.json")
    detached_path = os.path.join(root, "r4", RUN_ID + ".manifest.sha256")
    report_path = os.path.join(root, "r4", "independent-seal-crosscheck.json")

    with open(manifest_path, "r", encoding="utf-8") as fh:
        doc = json.load(fh)

    failures = []
    verified = 0
    for e in doc["entries"]:
        abs_path = os.path.join(pkg, *e["path"].split("/"))
        if not os.path.isfile(abs_path):
            failures.append({"path": e["path"], "reason": "MISSING"})
            continue
        size = os.path.getsize(abs_path)
        if size != e["sizeBytes"]:
            failures.append({"path": e["path"], "reason": "SIZE_MISMATCH",
                             "expected": e["sizeBytes"], "actual": size})
            continue
        digest = sha256(abs_path)
        if digest != e["sha256"]:
            failures.append({"path": e["path"], "reason": "SHA_MISMATCH",
                             "expected": e["sha256"], "actual": digest})
            continue
        verified += 1

    present = set()
    for dirpath, _dirnames, filenames in os.walk(pkg):
        for name in filenames:
            rel = os.path.relpath(os.path.join(dirpath, name), pkg).replace(os.sep, "/")
            present.add(rel)
    listed = {e["path"] for e in doc["entries"]}
    unlisted = sorted(present - listed - {"manifest.json"})
    missing_from_disk = sorted(listed - present)

    with open(detached_path, "r", encoding="utf-8") as fh:
        detached_value = fh.read().strip().split()[0]
    actual_manifest_sha = sha256(manifest_path)
    detached_matches = detached_value == actual_manifest_sha

    # cross-check the package's own machine verdict claims against the sealed evidence
    mv_path = os.path.join(pkg, "verdict", "machine-verdict.json")
    counts_path = os.path.join(pkg, "verdict", "counts.json")
    with open(mv_path, "r", encoding="utf-8") as fh:
        mv = json.load(fh)
    with open(counts_path, "r", encoding="utf-8") as fh:
        counts = json.load(fh)

    run_verdict_path = os.path.join(pkg, "runner", "runner-verdict.json")
    with open(run_verdict_path, "r", encoding="utf-8") as fh:
        run_verdict = json.load(fh)

    claims = {
        "implementationReady_true": mv.get("implementationReady") is True,
        "all_lq_closed": all(x.get("status") == "CLOSED" for x in mv.get("lq", [])),
        "lq_count_6": len(mv.get("lq", [])) == 6,
        "runner_verdict_pass": run_verdict.get("verdict") == "PASS",
        "runner_stage_count_10": run_verdict.get("stageCount") == 10,
        "runner_failed_zero": run_verdict.get("failedCount") == 0,
        "verified_inputs_292": run_verdict.get("verifiedInputCount") == 292,
        "counts_csharp_73": counts.get("csharp", {}).get("total") == 73 and counts.get("csharp", {}).get("passed") == 73,
        "counts_node_269": counts.get("node", {}).get("measuredPass") == 269,
        "counts_runner_28": counts.get("runnerContract", {}).get("measuredPass") == 28,
        "counts_auditor_26": counts.get("auditor", {}).get("measuredOk") == 26,
        "counts_bisect_19": counts.get("bisect", {}).get("measuredPass") == 19,
        "scenario_28_of_28": counts.get("scenarioMatrix", {}).get("passed") == counts.get("scenarioMatrix", {}).get("scenarios") == 28,
        "independent_false_pass_zero": counts.get("independentRunnerDisproof", {}).get("falsePassAfterFix") == 0,
        "independent_all_rejected": counts.get("independentRunnerDisproof", {}).get("allRejectedAfterFix") is True,
        "approvals_not_granted": mv.get("formalPhaseApproval") == "NOT_GRANTED" and mv.get("releaseApproval") == "NOT_GRANTED",
    }

    report = {
        "generatedUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "verifier": "verify-seal.py (independent Python implementation)",
        "package": RUN_ID,
        "manifestCount": doc["count"],
        "filesPresent": len(present),
        "verified": verified,
        "failures": failures,
        "unlisted": unlisted,
        "missingFromDisk": missing_from_disk,
        "manifestSha256": actual_manifest_sha,
        "detachedValue": detached_value,
        "detachedMatches": detached_matches,
        "claimCrossCheck": claims,
        "claimsAllHold": all(claims.values()),
        "result": "PASS" if (not failures and not unlisted and not missing_from_disk
                             and detached_matches and all(claims.values())) else "FAIL",
    }
    with open(report_path, "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2)
    print(json.dumps(report, indent=2))
    return 0 if report["result"] == "PASS" else 1


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("usage: python verify-seal.py <evidenceRoot>")
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
