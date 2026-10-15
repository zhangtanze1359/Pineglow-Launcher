#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check-action-contracts.py —— 校验工作流里用到的每个 action：引用能解、输入名合法、运行时未废弃。

为什么需要它（写在前面，因为这是本脚本存在的唯一理由）：
  actionlint 校验 YAML/表达式/工作流 schema，但**不校验 `with:` 里的输入名**——
  第 32 轮的反向对照里故意写的 `bogus-input` 它一声不吭。
  而本轮新增/改写的 CI 步骤**从未在真 runner 上跑过**，一个拼错的输入名会让首次推送直接变红。
  所以这里用"**action 自己的 action.yml**"当权威：输入名对不上就是硬失败。

数据来源（**刻意不用 api.github.com，避免 60 次/小时的匿名配额**）：
  - 引用 → 提交：`git ls-remote https://github.com/<owner>/<repo> <ref>`
  - action.yml：`https://raw.githubusercontent.com/<owner>/<repo>/<peeled-sha>/action.yml`
  - Node 版本是否存在：`https://nodejs.org/dist/index.json`
  - SDK 版本是否存在：dotnetcli release-metadata（先 index，再对应 channel 的 releases.json）

退出码：0 = 全部通过；1 = 有硬失败；2 = 用法/环境错误。

用法：
  python check-action-contracts.py --repo <仓库根> [--json <输出.json>] [--out <报告.md>]
"""

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.request

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

try:
    import yaml
except ModuleNotFoundError:
    print("需要 PyYAML（DSH 内置 python 自带；外部 python 需 pip install pyyaml）")
    sys.exit(2)

UA = {"User-Agent": "dsh-verify"}


def http_text(url, timeout=30):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read().decode("utf-8", "replace")


def http_json(url, timeout=30):
    return json.loads(http_text(url, timeout))


def git_ls_remote(repo_url, ref):
    """返回 {refname: sha}；同时给出去重后的 peeled 提交。"""
    out = subprocess.run(
        ["git", "ls-remote", repo_url, ref],
        capture_output=True, text=True, timeout=60,
    )
    if out.returncode != 0:
        return None, (out.stderr or "").strip()
    refs = {}
    for line in out.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) == 2:
            refs[parts[1]] = parts[0]
    return refs, ""


def peel(refs):
    """优先取 `^{}` 剥离后的提交（带注解的 tag 指向 tag 对象，raw 取不到内容）。"""
    peeled = [v for k, v in refs.items() if k.endswith("^{}")]
    if peeled:
        return peeled[0]
    return next(iter(refs.values())) if refs else None


def collect_steps(root, workflows):
    steps = []
    for rel in workflows:
        path = os.path.join(root, rel.replace("/", os.sep))
        with open(path, encoding="utf-8") as fh:
            doc = yaml.safe_load(fh)
        jobs = (doc or {}).get("jobs") or {}
        for jid, job in jobs.items():
            if not isinstance(job, dict):
                continue
            for st in (job.get("steps") or []):
                if isinstance(st, dict) and "uses" in st:
                    steps.append({
                        "workflow": rel,
                        "job": jid,
                        "stepName": st.get("name") or "",
                        "uses": str(st["uses"]).strip(),
                        "with": st.get("with") or {},
                    })
    return steps


def parse_action_manifest(text):
    doc = yaml.safe_load(text) or {}
    inputs = doc.get("inputs") or {}
    norm = {}
    for name, spec in inputs.items():
        spec = spec or {}
        norm[str(name)] = {
            "required": bool(spec.get("required", False)),
            "default": spec.get("default", None),
            "deprecationMessage": spec.get("deprecationMessage"),
        }
    runs = doc.get("runs") or {}
    return {
        "name": doc.get("name"),
        "inputs": norm,
        "requiredInputs": sorted(k for k, v in norm.items() if v["required"]),
        "using": runs.get("using"),
    }


DEPRECATED_RUNTIMES = {"node12", "node16", "docker"}


def check_node_versions(versions_needed):
    """versions_needed: set of version strings like '24.19.0'"""
    res = {}
    if not versions_needed:
        return res
    try:
        index = http_json("https://nodejs.org/dist/index.json")
    except Exception as exc:  # noqa: BLE001
        return {v: {"checked": False, "error": str(exc)} for v in versions_needed}
    have = {e.get("version", "").lstrip("v") for e in index}
    for v in versions_needed:
        res[v] = {"checked": True, "exists": v in have}
    return res


def check_dotnet_sdk(version):
    out = {"version": version, "checked": False}
    try:
        idx = http_json("https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json")
        channel = None
        for ch in idx.get("releases-index", []):
            cv = ch.get("channel-version", "")
            if cv and version.startswith(cv + "."):
                channel = ch
                break
        if not channel:
            out["error"] = "在 releases-index 里找不到匹配的大版本"
            return out
        rel = http_json(channel["releases.json"])
        found = False
        for r in rel.get("releases", []):
            for sdk in r.get("sdks", []):
                if sdk.get("version") == version:
                    found = True
                    break
            if found:
                break
        out.update({"checked": True, "exists": found, "channel": channel.get("channel-version")})
    except Exception as exc:  # noqa: BLE001
        out["error"] = str(exc)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True)
    ap.add_argument("--json")
    ap.add_argument("--out")
    ap.add_argument("--workflows", nargs="*",
                    default=[".github/workflows/build.yml", ".github/workflows/release.yml"])
    args = ap.parse_args()

    root = os.path.abspath(args.repo)
    steps = collect_steps(root, args.workflows)

    findings = []          # 硬失败
    notes = []             # 提示
    actions = {}
    for st in steps:
        uses = st["uses"]
        if "@" not in uses:
            findings.append(f"{st['workflow']}:{st['job']} 的 uses 缺 @ref: {uses}")
            continue
        slug, ref = uses.rsplit("@", 1)
        key = f"{slug}@{ref}"
        if key in actions:
            continue
        repo_url = f"https://github.com/{slug}.git"
        refs, err = git_ls_remote(repo_url, ref)
        info = {"slug": slug, "ref": ref, "repoUrl": repo_url, "resolved": None, "manifest": None}
        if not refs:
            info["error"] = f"引用解析失败: {err or 'no refs'}"
            findings.append(f"{key} 引用无法解析（git ls-remote 无结果）")
            actions[key] = info
            continue
        sha = peel(refs)
        info["resolved"] = sha
        info["refs"] = refs
        manifest = None
        for fname in ("action.yml", "action.yaml"):
            try:
                txt = http_text(f"https://raw.githubusercontent.com/{slug}/{sha}/{fname}")
            except Exception:  # noqa: BLE001
                continue
            try:
                manifest = parse_action_manifest(txt)
            except Exception as exc:  # noqa: BLE001
                info["manifestParseError"] = str(exc)
                continue
            info["manifestFile"] = fname
            break
        if not manifest:
            info["error"] = "取不到 action.yml（可能是 docker 型或其他布局）"
            notes.append(f"{key} 未取到 action.yml，无法校验输入名")
        info["manifest"] = manifest
        actions[key] = info

    # 逐 step 校验输入名 / 必填项
    step_results = []
    node_versions = set()
    for st in steps:
        uses = st["uses"]
        slug, ref = uses.rsplit("@", 1) if "@" in uses else (uses, "")
        info = actions.get(f"{slug}@{ref}")
        used = sorted(st["with"].keys())
        unknown, missing_required, dep = [], [], None
        if info and info.get("manifest"):
            declared = info["manifest"]["inputs"]
            unknown = [k for k in used if k not in declared]
            missing_required = [k for k in info["manifest"]["requiredInputs"] if k not in used]
            dep = info["manifest"]["using"]
            if unknown:
                findings.append(f"{uses} 的 with 出现未声明的输入: {', '.join(unknown)}")
            if missing_required:
                findings.append(f"{uses} 缺少必填输入: {', '.join(missing_required)}")
            if dep in DEPRECATED_RUNTIMES:
                findings.append(f"{uses} 的运行时 {dep} 已被 GitHub 淘汰")
        if used:
            nv = st["with"].get("node-version")
            if nv:
                node_versions.add(str(nv).strip("'\""))
        step_results.append({
            "workflow": st["workflow"], "job": st["job"], "stepName": st["stepName"],
            "uses": uses, "withKeys": used, "unknownInputs": unknown,
            "missingRequired": missing_required, "runtime": dep,
        })

    # 运行时版本是否存在
    node_check = check_node_versions(node_versions)
    for v, r in node_check.items():
        if r.get("checked") and not r.get("exists"):
            findings.append(f"setup-node 指定的 Node {v} 在 nodejs.org 上不存在")
        elif not r.get("checked"):
            notes.append(f"Node {v} 未能核实（{r.get('error')}）")

    sdk = None
    gj = os.path.join(root, "global.json")
    if os.path.exists(gj):
        try:
            with open(gj, encoding="utf-8") as fh:
                sdk = (json.load(fh).get("sdk") or {}).get("version")
        except Exception:  # noqa: BLE001
            pass
    dotnet_check = check_dotnet_sdk(sdk) if sdk else None
    if dotnet_check and dotnet_check.get("checked") and not dotnet_check.get("exists"):
        findings.append(f"global.json 指定的 SDK {sdk} 在 dotnet release-metadata 里不存在")
    elif dotnet_check and not dotnet_check.get("checked") and sdk:
        notes.append(f"SDK {sdk} 未能核实（{dotnet_check.get('error')}）")

    result = {
        "repo": root,
        "workflows": args.workflows,
        "stepCount": len(steps),
        "uniqueActions": sorted(actions.keys()),
        "actions": actions,
        "steps": step_results,
        "nodeVersions": node_check,
        "dotnetSdk": dotnet_check,
        "findings": findings,
        "notes": notes,
        "verdict": "PASS" if not findings else "FAIL",
    }

    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(result, fh, ensure_ascii=False, indent=2)

    print("== action 契约校验 ==")
    for key, info in sorted(actions.items()):
        m = info.get("manifest") or {}
        print(f"  {key}")
        print(f"    resolved = {str(info.get('resolved'))[:16]}  using = {m.get('using')}  inputs = {len(m.get('inputs') or {})}")
        if info.get("error"):
            print(f"    !! {info['error']}")
    print("-- 输入名逐条 --")
    for r in step_results:
        flag = "OK " if not r["unknownInputs"] and not r["missingRequired"] else "!! "
        print(f"  {flag}{r['uses']:<40} with={r['withKeys']}")
    print("-- 运行时 --")
    print(f"  node: {json.dumps(node_check, ensure_ascii=False)}")
    print(f"  sdk : {json.dumps(dotnet_check, ensure_ascii=False)}")
    print(f"-- 结论: {result['verdict']}  硬失败 {len(findings)} / 提示 {len(notes)}")
    for f in findings:
        print("  [FAIL] " + f)
    for n in notes:
        print("  [note] " + n)
    print(f"ACTION-CONTRACT RESULT={result['verdict']} steps={len(steps)} actions={len(actions)} findings={len(findings)}")
    return 0 if not findings else 1


if __name__ == "__main__":
    sys.exit(main())
