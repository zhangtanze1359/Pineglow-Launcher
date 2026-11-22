import collections
import hashlib
import subprocess
import sys
from datetime import datetime
from pathlib import Path

main, checkout = (Path(path) for path in sys.argv[1:])
target = "8586e92"
archive = "docs/evidence/2026-11-22-open05"
files = ["scripts/audit-evidence-tree.ps1", "scripts/audit-plan-ledger.ps1"]

def run(args, cwd=checkout):
    process = subprocess.run(args, cwd=cwd, capture_output=True)
    print(f"\n{datetime.now().astimezone().isoformat()} cwd={cwd} command={args} exit={process.returncode}")
    print(process.stdout.decode("utf-8", errors="replace"), end="")
    if process.stderr:
        print(process.stderr.decode("utf-8", errors="replace"), end="")
    return process

def git(*args, cwd=checkout):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *args], cwd=cwd)

def git_tree(revision, prefix):
    output = git("ls-tree", "-rz", revision, "--", prefix)
    return {entry.split(b"\t", 1)[1].decode("utf-8"): entry.split(b"\t", 1)[0].split(b" ")[2]
            for entry in output.split(b"\0") if entry}

for cwd in (main, checkout):
    print(f"\npwd={cwd}")
    for cmd in [
        ("rev-parse", "--show-toplevel"), ("rev-parse", "HEAD"),
        ("status", "--short", "--untracked-files=all"), ("diff", "--stat"), ("diff", "--cached", "--stat")
    ]:
        run(["git", "-c", "core.longpaths=true", *cmd], cwd=cwd)
run(["git", "log", "--oneline", "-8"], cwd=main)
run(["git", "show", "--stat", target], cwd=main)
run(["git", "rev-parse", "refs/heads/main"], cwd=main)
run(["git", "diff", "--name-status", target, "refs/heads/main"], cwd=main)
for commit in ("ed2e92c", "932115c", "fe49b34", "a5e9730", "aa64a57", target):
    run(["git", "merge-base", "--is-ancestor", commit, "refs/heads/main"], cwd=main)
run(["git", "diff", "--exit-code", "fe49b34", target, "--", *files], cwd=main)
run(["git", "diff", "--exit-code", target, "refs/heads/main", "--", *files, "docs/market-execution", archive], cwd=main)
run(["git", "diff", "--exit-code", target, "refs/heads/main", "--", "docs/open-items.md"], cwd=main)
run(["git", "config", "--show-origin", "--get", "core.autocrlf"], cwd=main)
run(["git", "ls-files", "--eol", "--", *files], cwd=main)
run(["git", "ls-files", "--eol", "--", *files], cwd=checkout)
for file in files:
    names = [("target git object", git("show", f"{target}:{file}")),
             ("main workdir", (main / file).read_bytes()), ("clean workdir", (checkout / file).read_bytes())]
    for where, content in names:
        print(file, where, f"SHA256={hashlib.sha256(content).hexdigest().upper()} "
              f"CRLF={content.count(bytes((13,10)))} LF={content.count(bytes((10,)))} byte_count={len(content)}")
    print(file, "blob", git("rev-parse", f"{target}:{file}").decode().strip())
    for workdir in (main, checkout):
        run(["git", "hash-object", "--path", file, file], cwd=workdir)

expected = {
    "verify-codex-r1-fail": 234,
    "verify-codex-r2-fail": 93,
    "verify-codex-r3-pass-behavior-fail-counts": 391,
    "verify-codex-r4-pass": 3,
    "target-confirm": 80,
    "raw": 166,
}
for folder, expect in expected.items():
    prefix = f"{archive}/{folder}"
    stored = git_tree(target, prefix)
    directory = checkout / prefix
    disk = {f.relative_to(checkout).as_posix() for f in directory.rglob("*") if f.is_file()}
    print(folder, f"expected={expect} git={len(stored)} disk={len(disk)} file_names_equal={disk == stored.keys()}")
    print("  tracked not on disk=", sorted(stored.keys() - disk), "disk not tracked=", sorted(disk - stored.keys()))
    first = git("log", "--reverse", "--format=%H", target, "--", prefix).decode().splitlines()[0]
    original = git_tree(first, prefix)
    if folder == "raw":
        continue
    compare = collections.Counter(original.values()) == collections.Counter(stored.values())
    print(f"  first={first} original_files={len(original)} blob_multiset_equal={compare}")
    if original.keys() != stored.keys():
        print("  structurally moved paths=", len(original.keys() - stored.keys()),
              "new paths=", len(stored.keys() - original.keys()))
    if folder.startswith("verify-codex-r3"):
        counts = collections.Counter()
        for path in stored:
            part = path.removeprefix(prefix + "/")
            counts[part.split("/")[0] if "/" in part else "(top-level)"] += 1
        print("  by subsection", sorted(counts.items()), "wrapper tracked=", f"{prefix}/README.md" in stored)
count = collections.Counter()
groups = collections.defaultdict(set)
for file in (checkout / archive / "raw").iterdir():
    name = file.name
    prefix = next((name_prefix for name_prefix in ("counterproof-P-", "counterproof-F-", "probe-", "tree-pathform-")
                   if name.startswith(name_prefix)), None)
    if prefix:
        count[prefix] += 1
    else:
        suffix = next((part for part in (".out.txt", ".err.txt") if name.endswith(part)), ".txt")
        count[suffix] += 1
        groups[suffix].add(name.removesuffix(suffix))
print(f"raw categories={dict(count)} total={sum(count.values())} triplets={groups['.out.txt'] == groups['.err.txt'] == groups['.txt']}")
run(["git", "log", "--full-history", "--format=%h %s", "--name-status", "--diff-filter=D", "-M100%", target, "--", archive])
renames = git("show", "--format=", "--name-status", "-M100%", "0fd28df", "--", archive).decode().splitlines()
print(f"0fd28df exact rename count={sum(line.startswith('R100') for line in renames)}, other entries={[line for line in renames if not line.startswith('R100')]}")
for cwd in (main, checkout):
    data = cwd / "docs/market-execution"
    disk = [file for file in data.rglob("*") if file.is_file()]
    ignored = git("ls-files", "--others", "--ignored", "--exclude-standard", "-z", "--", "docs/market-execution", cwd=cwd)
    ignored_names = [name for name in ignored.split(b"\0") if name]
    stored = git("ls-files", "-z", "--", "docs/market-execution", cwd=cwd)
    print(f"\n{cwd} real_input_files={len(disk)} tracked={len(stored.split(bytes((0,))))-1} "
          f"ignored={len(ignored_names)} ignored_log={sum(name.endswith(b'.log') for name in ignored_names)}")

