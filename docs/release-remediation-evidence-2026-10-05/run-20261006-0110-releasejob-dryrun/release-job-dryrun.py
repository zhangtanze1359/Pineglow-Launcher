"""release.yml 的 release 作业步骤正文本地干跑（含 RED 用例）。

为什么需要它：
  `release` 作业里的「产物形态断言 / 生成 manifest / 清单与附件一致」三条门槛**只在 CI 跑**，
  本地从来没执行过；换句话说，"发布时会不会因为某个门槛写错而失败、或者写错了却放行"
  目前没有任何本地证据。这个脚本把 **release.yml 里真实的 run 正文**抽出来执行（不是重写一份副本），
  并对每条门槛都配 RED 用例，证明它会咬。

明确的边界（不要过度解读）：
  - 只执行纯 PowerShell 的步骤正文；**不**执行 actions（checkout/upload/create-release）；
  - **不等于 actionlint 通过，也不等于 runner 通过**（工作流语法/表达式语义/runner 环境都不在此覆盖内）；
  - 表达式 `${{ }}` 由本脚本做**有限替换**；只要替换后仍残留 `${{`，就**拒绝运行该步骤**（宁可报错，不假跑）。
"""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

import yaml

REPO_ROOT = r"D:\松光启动器"
WF = os.path.join(REPO_ROOT, ".github", "workflows", "release.yml")
# 快照路径会随每轮验证变化：用 PINEGLOW_SNAP 指定即可复跑，不必改脚本。
DEFAULT_SNAP = r"C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-a6d9df5d00d64a0883eddb1698319270"
SNAP = os.environ.get("PINEGLOW_SNAP") or DEFAULT_SNAP
SNAP_EXE = os.path.join(SNAP, r"dev\PineglowLauncher\bin\Release\net10.0\win-x64\publish\Pineglow.Launcher.exe")
SNAP_DLL = os.path.join(SNAP, r"dev\PineglowLauncher\bin\Release\net10.0\win-x64\Pineglow.Launcher.dll")
ASSET = "Pineglow-Launcher-win-x64-single-file.exe"

STEPS_WANTED = [
    "Verify assembly version matches tag",
    "Assert artifact is a self-contained single EXE",
    "Rename exe + generate manifest",
    "Validate update manifest against final artifact",
]

EXPR = {
    r"${{ steps.version.outputs.version }}": None,  # 按用例注入
    r"${{ github.ref_name }}": None,
    r"${{ github.repository }}": "zhangtanze1359/Pineglow-Launcher",
}


def load_steps():
    doc = yaml.safe_load(open(WF, encoding="utf-8"))
    steps = doc["jobs"]["release"]["steps"]
    by_name = {}
    for s in steps:
        if isinstance(s, dict) and s.get("name") in STEPS_WANTED:
            by_name[s["name"]] = s.get("run")
    missing = [n for n in STEPS_WANTED if n not in by_name]
    if missing:
        print("找不到预期步骤（工作流改名了？）:", missing)
        sys.exit(2)
    return by_name


def render(body, version, tag):
    out = body
    for k, v in EXPR.items():
        val = version if k.endswith("outputs.version }}") else (tag if "ref_name" in k else v)
        out = out.replace(k, str(val))
    return out


def run_ps(script_path, cwd):
    # 步骤正文含中文注释 → 写作带 BOM 的 UTF-8（宿主是 Windows PowerShell 5.1）
    proc = subprocess.run(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script_path],
        cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300,
    )
    return proc.returncode, (proc.stdout or "") + (proc.stderr or "")


def main():
    for p in (WF, SNAP_EXE, SNAP_DLL):
        if not os.path.exists(p):
            print("缺少输入:", p)
            sys.exit(2)

    steps = load_steps()
    evi = os.path.join(REPO_ROOT, "docs", "release-remediation-evidence-2026-10-05",
                       "run-20261006-0110-releasejob-dryrun")
    os.makedirs(evi, exist_ok=True)
    work = os.path.join(evi, "work")
    if os.path.exists(work):
        shutil.rmtree(work, ignore_errors=True)
    dist = os.path.join(work, "dist")
    dll_dir = os.path.join(work, r"dev\PineglowLauncher\bin\Release\net10.0\win-x64")
    os.makedirs(dist)
    os.makedirs(dll_dir)

    # 直接复制：硬链接跨卷不可用（快照在 C:，证据在工作区 D:），实测 mklink /H 失败。
    # 只在准备阶段复制一次（58MB），用例之间只重置小文件。
    def link(src, dst):
        if os.path.exists(dst):
            os.remove(dst)
        shutil.copy2(src, dst)
        return os.path.getsize(dst)

    exe_bytes = link(SNAP_EXE, os.path.join(dist, ASSET))
    dll_bytes = link(SNAP_DLL, os.path.join(dll_dir, "Pineglow.Launcher.dll"))
    linked = exe_bytes
    print(f"dist 资产: {ASSET} = {linked} bytes; DLL = {dll_bytes} bytes（复制自快照）")

    # 写步骤正文（每个用例一份，避免互相覆盖）
    scripts = {}
    for name in STEPS_WANTED:
        for tagver in ("1.0.0", "1.0.1"):
            body = render(steps[name], tagver, "v" + tagver)
            if "${{" in body:
                print(f"步骤『{name}』仍含未替换表达式，拒绝运行（宁可报错不假跑）")
                sys.exit(2)
            path = os.path.join(evi, f"step-{re.sub(r'[^A-Za-z0-9]+','-',name).strip('-')}-v{tagver}.ps1")
            with open(path, "w", encoding="utf-8-sig", newline="\r\n") as fh:
                fh.write(body)
            scripts[(name, tagver)] = path
    print(f"已导出 {len(scripts)} 份步骤正文（UTF-8 BOM）到 {os.path.relpath(evi, REPO_ROOT)}")

    size = os.path.getsize(os.path.join(dist, ASSET))
    results = []

    def reset_dist(extra_files=(), extra_dirs=()):
        keep = {ASSET, "manifest.json"}
        for f in os.listdir(dist):
            p = os.path.join(dist, f)
            if os.path.isdir(p):
                shutil.rmtree(p, ignore_errors=True)
            elif f not in keep:
                try:
                    os.remove(p)
                except OSError:
                    pass
        if os.path.exists(os.path.join(dist, "manifest.json")):
            os.remove(os.path.join(dist, "manifest.json"))
        for f in extra_files:
            with open(os.path.join(dist, f), "w", encoding="utf-8") as fh:
                fh.write("x")
        for d in extra_dirs:
            os.makedirs(os.path.join(dist, d), exist_ok=True)

    def case(cid, step, version, tag, expect_zero, note, setup=None):
        if setup:
            setup()
        code, out = run_ps(scripts[(step, version)], work)
        ok = (code == 0) == expect_zero
        results.append({
            "case": cid, "step": step, "version": version, "expected_exit_zero": expect_zero,
            "actual_exit": code, "as_expected": ok, "note": note,
            "output_tail": out.strip()[-400:],
        })
        print(f"  [{'OK ' if ok else 'BAD'}] {cid} exit={code} (期望{'0' if expect_zero else '非0'}) {note}")
        return code, out

    print("\n== A. 产物形态断言 ==")
    reset_dist()
    case("A1-clean", "Assert artifact is a self-contained single EXE", "1.0.0", "v1.0.0", True, "干净 dist 只含 1 个 EXE")
    reset_dist(extra_files=["stray.pdb"], extra_dirs=["UI"])
    case("A2-pdb-and-UI-cleaned", "Assert artifact is a self-contained single EXE", "1.0.0", "v1.0.0", True,
         "多余 PDB 与 UI/ 应被**清理**后放行（不是直接失败）")
    leftover_pdb = os.path.exists(os.path.join(dist, "stray.pdb"))
    leftover_ui = os.path.isdir(os.path.join(dist, "UI"))
    print(f"    清理后 PDB 还在? {leftover_pdb}  UI 还在? {leftover_ui}")
    results.append({"case": "A2-cleanup-effect", "step": "Assert artifact is a self-contained single EXE",
                    "expected_exit_zero": True, "actual_exit": 0, "as_expected": not leftover_pdb and not leftover_ui,
                    "note": "清理副作用：PDB 与 UI/ 必须真的被删掉", "output_tail": ""})
    reset_dist(extra_files=["notes.json"])
    case("A3-stray-file", "Assert artifact is a self-contained single EXE", "1.0.0", "v1.0.0", False, "多余非 EXE 文件必须失败")
    reset_dist(extra_dirs=["extras"])
    case("A4-stray-dir", "Assert artifact is a self-contained single EXE", "1.0.0", "v1.0.0", False, "多余目录必须失败")
    reset_dist(extra_files=["second.exe"])
    case("A5-two-exes", "Assert artifact is a self-contained single EXE", "1.0.0", "v1.0.0", False, "两个 EXE 必须失败")

    print("\n== B. 程序集版本与 tag 一致 ==")
    reset_dist()
    case("B1-match", "Verify assembly version matches tag", "1.0.0", "v1.0.0", True, "DLL 版本与 tag 一致")
    case("B2-mismatch", "Verify assembly version matches tag", "1.0.1", "v1.0.1", False, "DLL 是 1.0.0 而 tag 是 1.0.1，必须失败")

    print("\n== C. 生成 manifest 与清单一致性门槛 ==")
    reset_dist()
    case("C1-generate", "Rename exe + generate manifest", "1.0.1", "v1.0.1", True, "生成 dist/manifest.json")
    mpath = os.path.join(dist, "manifest.json")
    man = json.load(open(mpath, encoding="utf-8-sig")) if os.path.exists(mpath) else None
    print("    manifest =", json.dumps(man, ensure_ascii=False))
    good = man
    results.append({"case": "C1-manifest-shape", "step": "Rename exe + generate manifest",
                    "expected_exit_zero": True, "actual_exit": 0,
                    "as_expected": bool(good) and set(good) >= {"version", "url", "size", "sha256"}
                    and good["size"] == size and len(good["sha256"]) == 64
                    and str(good["url"]).startswith("https://") and str(good["url"]).endswith(ASSET),
                    "note": "生成的清单字段/URL/SHA/size 必须自洽", "output_tail": ""})

    def write_manifest(**over):
        m = dict(good)
        m.update(over)
        with open(mpath, "w", encoding="utf-8") as fh:
            json.dump(m, fh, ensure_ascii=False)

    case("V1-good", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", True, "正确清单放行")
    write_manifest(version="1.0.0")
    case("V2-version", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", False, "version 与 tag 不一致必须失败")
    write_manifest(url="http://example.com/" + ASSET)
    case("V3-not-https", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", False, "非 HTTPS 地址必须失败（MR20 不变量）")
    write_manifest(url="https://example.com/other.exe")
    case("V4-wrong-asset", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", False, "URL 不指向本次 win-x64 资产必须失败")
    # V5：大写 SHA256 会被**放行**。这不是缺陷 —— PowerShell 的 -match 默认大小写不敏感，
    # 而客户端 UpdateService.IsValidSha256 用 Uri.IsHexDigit（不区分大小写）、
    # Sha256Matches 用 StringComparison.OrdinalIgnoreCase 比较，两端一致。
    # 本条用例的作用是把这个"看起来像漏判"的行为钉住：改天有人把它收紧成 -cmatch，
    # 就会变成发布端比客户端更严、拒绝一份客户端本可正常使用的清单。
    write_manifest(sha256=good["sha256"].upper())
    case("V5-sha-uppercase-accepted", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", True,
         "大写 SHA256 放行（与客户端 OrdinalIgnoreCase 行为一致，不是漏判）")
    write_manifest(sha256="0" * 64)
    case("V6-sha-mismatch", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", False, "SHA256 与实际附件不符必须失败")
    write_manifest(size=int(good["size"]) + 1)
    case("V7-size", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", False, "size 与实际附件不符必须失败")
    os.remove(mpath)
    case("V8-missing", "Validate update manifest against final artifact", "1.0.1", "v1.0.1", False, "缺清单必须失败")

    bad = [r for r in results if not r["as_expected"]]
    report = {
        "generatedLocal": subprocess.run(["powershell.exe", "-NoProfile", "-Command",
                                          "(Get-Date).ToString('s')"], capture_output=True, text=True).stdout.strip(),
        "workflow": ".github/workflows/release.yml",
        "snapshot": SNAP,
        "asset": ASSET, "assetBytes": size,
        "cases": results, "failed": [r["case"] for r in bad],
        "verdict": "PASS" if not bad else "FAIL",
        "boundary": "只跑步骤正文；不等于 actionlint / runner 通过；不执行 actions",
    }
    out_json = os.path.join(evi, "release-job-dryrun.json")
    with open(out_json, "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
    print(f"\nRELEASE-JOB-DRYRUN RESULT={report['verdict']} cases={len(results)} failed={len(bad)}")
    if not bad:
        # 成功就清掉 work/（里面有 53MB 的附件副本）：证据已经落在 JSON 与抽取出的步骤正文里。
        # 失败时**保留**，方便直接翻 dist/ 现场。
        shutil.rmtree(work, ignore_errors=True)
        print("work/ 已清理（失败时会保留供排查）")
    if bad:
        print("未按预期:", [r["case"] for r in bad])
        sys.exit(1)


if __name__ == "__main__":
    main()
