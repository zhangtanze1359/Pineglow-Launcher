"""release.yml「Mirror to Gitee (optional)」步骤的失败路径干跑。

为什么做：
  这条步骤有 100 多行 PowerShell（建 release → 上传附件 → 改写 manifest → 写固定 update.json），
  而且**从来没被执行过**（需要 GITEE_TOKEN）。R03 的要求恰恰针对它的失败行为：
  「缺 token / 镜像失败不得声称固定 update.json 已更新或输出无效地址」。
  只看代码不算验证 —— 这次把步骤正文抽出来，在**没有有效凭据**的前提下把它真的跑起来。

做法与 release 作业干跑一致：按步骤名从 release.yml 取真实 run 正文 → 替换已知表达式
（残留 ${{ 就拒绝运行）→ 落成 UTF-8 BOM 的 .ps1 → 在搭好的 dist/ 形状里执行，
env 里的 GITEE_TOKEN 由本脚本控制。**不会**使用任何真实令牌：G1 为空，G2 是明显的假串。
本机 gitee.com 走的是 fake-IP DNS，请求根本到不了真实服务（实测与 actionlint 那次同类：固定应答）。
"""

import json
import os
import shutil
import subprocess
import sys

import yaml

REPO_ROOT = r"D:\松光启动器"
WF = os.path.join(REPO_ROOT, ".github", "workflows", "release.yml")
# 快照路径会随每轮验证变化：用 PINEGLOW_SNAP 指定即可复跑，不必改脚本。
DEFAULT_SNAP = r"C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-8816365554cc4ab2aca5e89007ce505a"
SNAP = os.environ.get("PINEGLOW_SNAP") or DEFAULT_SNAP
SNAP_EXE = os.path.join(SNAP, r"dev\PineglowLauncher\bin\Release\net10.0\win-x64\publish\Pineglow.Launcher.exe")
STEP_NAME = "Mirror to Gitee (optional)"
ASSET = "Pineglow-Launcher-win-x64-single-file.exe"
TAG = "v1.0.1"
VERSION = "1.0.1"
REPO = "zhangtanze1359/Pineglow-Launcher"

EXPR = {
    r"${{ steps.version.outputs.version }}": VERSION,
    r"${{ github.ref_name }}": TAG,
    r"${{ github.repository }}": REPO,
}


def main():
    for p in (WF, SNAP_EXE):
        if not os.path.exists(p):
            print("缺少输入:", p)
            sys.exit(2)

    doc = yaml.safe_load(open(WF, encoding="utf-8"))
    step = next((s for s in doc["jobs"]["release"]["steps"]
                 if isinstance(s, dict) and s.get("name") == STEP_NAME), None)
    if step is None:
        print("找不到步骤:", STEP_NAME)
        sys.exit(2)
    body = step["run"]
    for k, v in EXPR.items():
        body = body.replace(k, v)
    if "${{" in body:
        print("步骤正文仍含未替换表达式，拒绝运行（宁可报错不假跑）")
        sys.exit(2)

    evi = os.path.join(REPO_ROOT, "docs", "release-remediation-evidence-2026-10-05",
                       "run-20261006-0140-gitee-mirror-dryrun")
    os.makedirs(evi, exist_ok=True)
    script = os.path.join(evi, "step-Mirror-to-Gitee-optional.ps1")
    with open(script, "w", encoding="utf-8-sig", newline="\r\n") as fh:
        fh.write(body)
    print("已导出步骤正文:", os.path.relpath(script, REPO_ROOT))

    work = os.path.join(evi, "work")
    dist = os.path.join(work, "dist")
    if os.path.exists(work):
        shutil.rmtree(work, ignore_errors=True)
    os.makedirs(dist)
    shutil.copy2(SNAP_EXE, os.path.join(dist, ASSET))
    size = os.path.getsize(os.path.join(dist, ASSET))
    baseline_manifest = {
        "version": VERSION,
        "url": f"https://github.com/{REPO}/releases/download/{TAG}/{ASSET}",
        "size": size,
        "sha256": "5009cf73d0801e83ed68e23829486b2d27d1a313e0db67b5acbdc17bf169383c",
        "notes": f"Pineglow Launcher {VERSION}",
        "mandatory": False,
    }

    def fresh_manifest():
        with open(os.path.join(dist, "manifest.json"), "w", encoding="utf-8") as fh:
            json.dump(baseline_manifest, fh, ensure_ascii=False)

    results = []

    def case(cid, token, note, script_path=None, extra_env=None):
        fresh_manifest()
        status_path = os.path.join(dist, "mirror-status.json")
        if os.path.exists(status_path):
            os.remove(status_path)
        env = dict(os.environ)
        env["GITEE_TOKEN"] = token
        if extra_env:
            env.update(extra_env)
        proc = subprocess.run(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script_path or script],
            cwd=work, capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=600, env=env,
        )
        status = None
        if os.path.exists(status_path):
            try:
                status = json.load(open(status_path, encoding="utf-8-sig"))
            except Exception as exc:  # noqa: BLE001
                status = {"__parse_error__": str(exc)}
        manifest = json.load(open(os.path.join(dist, "manifest.json"), encoding="utf-8-sig"))
        rec = {
            "case": cid, "token_kind": ("空" if token == "" else "明显无效的假串"),
            "expected": note,
            "exit_code": proc.returncode,
            "mirror_status_written": status is not None,
            "mirror_status": status,
            "manifest_url_after_step": manifest.get("url"),
            "manifest_url_still_github": str(manifest.get("url", "")).startswith(f"https://github.com/{REPO}/"),
            "output_tail": (proc.stdout + proc.stderr).strip()[-1200:],
        }
        results.append(rec)
        print(f"\n--- {cid} (token={rec['token_kind']}) exit={proc.returncode} "
              f"status文件={'有' if status is not None else '无'} ---")
        print("  mirror-status:", json.dumps(status, ensure_ascii=False))
        print("  manifest.url 仍指向 GitHub:", rec["manifest_url_still_github"])
        return rec

    g1 = case("G1-no-token", "", "缺 token：应如实落盘 tokenPresent=false 并说明走备用源，步骤本身不失败")
    g2 = case("G2-invalid-token", "invalid-token-for-test-only", "无效 token/接口不可达：不得声称已上传或已更新 update.json；应留下可读的状态记录")

    # G3：故障注入 —— 把 API 基址换成一个必然连不上的地址（端口 9/discard），
    # 模拟"Gitee API 不可达"。**只改这一处 URL**，其余分支逻辑一字不动。
    injected = os.path.join(evi, "step-Mirror-to-Gitee-DOWN-API-fault-injected.ps1")
    orig_api = 'https://gitee.com/api/v5/repos/$owner/$repo'
    down_api = 'http://127.0.0.1:9/api/v5/repos/$owner/$repo'
    if orig_api not in body:
        print("故障注入失败：步骤里找不到预期的 API 基址，拒绝继续（避免伪造一次'注入'）")
        sys.exit(2)
    with open(injected, "w", encoding="utf-8-sig", newline="\r\n") as fh:
        fh.write(body.replace(orig_api, down_api))
    g3 = case("G3-api-unreachable-fault-injected", "invalid-token-for-test-only",
              "API 不可达（注入）：这一步会 throw；关键问题是**它是否留下可读的状态记录**",
              script_path=injected)

    checks = [
        ("G1 exit==0（缺 token 不阻断发布）", g1["exit_code"] == 0),
        ("G1 写出 mirror-status.json", g1["mirror_status_written"]),
        ("G1 tokenPresent=false", (g1["mirror_status"] or {}).get("tokenPresent") is False),
        ("G1 attachmentUploaded=false", (g1["mirror_status"] or {}).get("attachmentUploaded") is False),
        ("G1 updateJsonUpdated=false", (g1["mirror_status"] or {}).get("updateJsonUpdated") is False),
        ("G1 manifestUrlSource 保持 github", (g1["mirror_status"] or {}).get("manifestUrlSource") == "github"),
        ("G1 note 非空（说清后果）", bool(str((g1["mirror_status"] or {}).get("note", "")).strip())),
        ("G1 manifest.url 未被改写", g1["manifest_url_still_github"]),
        ("G2 未声称 updateJsonUpdated", (g2["mirror_status"] or {}).get("updateJsonUpdated") is not True),
        ("G2 manifest.url 未被改写成无效地址", g2["manifest_url_still_github"]),
        ("G2 留下可读状态记录（写盘或非零退出二者至少其一可解释）",
         g2["mirror_status_written"] or g2["exit_code"] != 0),
        # G3 是本轮真正要问的问题：API 不可达时会不会"失败了却没有留下任何可读后果"
        ("G3 失败时仍留下 mirror-status.json（否则 artifact 上传会 if-no-files-found: warn 悄悄没证据）",
         g3["mirror_status_written"]),
        ("G3 未声称已更新 update.json", (g3["mirror_status"] or {}).get("updateJsonUpdated") is not True),
        ("G3 manifest.url 未被改写成无效地址", g3["manifest_url_still_github"]),
    ]
    bad = [name for name, ok in checks if not ok]
    print("\n== 断言 ==")
    for name, ok in checks:
        print(f"  [{'OK ' if ok else 'BAD'}] {name}")
    report = {
        "cases": results,
        "checks": [{"check": n, "ok": ok} for n, ok in checks],
        "failed": bad,
        "verdict": "PASS" if not bad else "FAIL",
        "boundary": "只跑步骤正文；不执行 actions；未使用真实 Gitee 凭据；不等于 CI 通过",
    }
    with open(os.path.join(evi, "gitee-mirror-dryrun.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
    print(f"\nGITEE-MIRROR-DRYRUN RESULT={report['verdict']} failed={len(bad)}")
    if not bad:
        # 成功就清掉 work/（里面有 53MB 的附件副本）：证据已经落在 JSON 里。失败时保留供排查。
        shutil.rmtree(work, ignore_errors=True)
        print("work/ 已清理（失败时会保留供排查）")
    if bad:
        print("未通过:", bad)
        sys.exit(1)


if __name__ == "__main__":
    main()
