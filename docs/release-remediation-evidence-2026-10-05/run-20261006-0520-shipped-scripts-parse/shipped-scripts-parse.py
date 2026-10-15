"""候选快照里"随包交付的脚本"静态解析检查（第 22 轮）。

为什么需要：
  白名单（source-snapshot-inputs.txt）把 ~15 个可复现性工具脚本一起交付给评审方，
  但**快照验证器只执行其中一部分**——其余脚本从写出来那天起就没人再跑过。
  一个语法坏掉（例如缺 BOM 导致 PS 5.1 按 GBK 解析）的脚本会**原样进入候选**，
  评审方一跑就报错，而本地全绿时看不出来。

做法：**只解析、不执行**。
  - .ps1/.psm1 → Windows PowerShell 的 Parser API（纯语法检查）
  - .js/.cjs   → `node --check`（纯语法检查）
  - .py        → 内建 compile()（不落 __pycache__）
这样既覆盖全部交付脚本，又不会产生任何副作用（不启动产品、不碰配置、不联网）。
"""

import json
import os
import subprocess
import sys

REPO_ROOT = r"D:\松光启动器"
WL = os.path.join(REPO_ROOT, "scripts", "source-snapshot-inputs.txt")
EVI = os.path.join(REPO_ROOT, "docs", "release-remediation-evidence-2026-10-05",
                   "run-20261006-0520-shipped-scripts-parse")


def whitelist():
    out = []
    for line in open(WL, encoding="utf-8"):
        s = line.strip()
        if s and not s.startswith("#"):
            out.append(s)
    return out


def ps_parse(paths):
    """用 PowerShell 的 Parser 检查语法；不执行脚本。"""
    tmp = os.path.join(EVI, "_ps-parse-check.ps1")
    arr = ",".join("'" + p.replace("'", "''") + "'" for p in paths)
    body = (
        "$files = @(" + arr + ")\r\n"
        "$bad = 0\r\n"
        "foreach ($f in $files) {\r\n"
        "  $tokens = $null; $errors = $null\r\n"
        "  [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $f).Path, [ref]$tokens, [ref]$errors)\r\n"
        "  if ($errors -and $errors.Count -gt 0) {\r\n"
        "    $bad++\r\n"
        "    Write-Output ('ERR ' + $f + ' :: ' + (($errors | ForEach-Object { $_.Message }) -join ' | '))\r\n"
        "  } else { Write-Output ('OK ' + $f) }\r\n"
        "}\r\n"
        "Write-Output ('PSPARSE_BAD=' + $bad)\r\n"
    )
    # 这段包装脚本本身含中文吗？不含；但仍以带 BOM 的 UTF-8 写出，避免宿主按 ANSI 解析
    with open(tmp, "w", encoding="utf-8-sig", newline="\r\n") as fh:
        fh.write(body)
    proc = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tmp],
                          capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300)
    return proc.stdout or "", proc.stderr or "", proc.returncode


def node_check(paths):
    res = {}
    for p in paths:
        proc = subprocess.run(["node", "--check", os.path.join(REPO_ROOT, p)],
                              capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
        res[p] = (proc.returncode, (proc.stdout or "") + (proc.stderr or ""))
    return res


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:  # noqa: BLE001
        pass
    os.makedirs(EVI, exist_ok=True)
    files = [f for f in whitelist() if os.path.isfile(os.path.join(REPO_ROOT, f))]
    ps = [f for f in files if f.endswith((".ps1", ".psm1"))]
    js = [f for f in files if f.endswith((".js", ".cjs", ".mjs"))]
    py = [f for f in files if f.endswith(".py")]
    print(f"交付脚本：.ps1/.psm1 {len(ps)} 个，.js/.cjs {len(js)} 个，.py {len(py)} 个（共 {len(files)} 个白名单文件）")

    problems = []

    out, err, code = ps_parse(ps)
    ps_bad = 0
    for line in out.splitlines():
        line = line.strip()
        if line.startswith("PSPARSE_BAD="):
            ps_bad = int(line.split("=", 1)[1])
        elif line.startswith("ERR "):
            problems.append("PowerShell 语法错误: " + line[4:])
            print("  ✗", line[4:][:200])
    if code != 0 and ps_bad == 0:
        problems.append(f"PowerShell 解析进程异常退出（code={code}）: {err.strip()[:200]}")
    print(f"  PowerShell 解析：{len(ps) - ps_bad}/{len(ps)} 通过")

    node_res = node_check(js)
    node_bad = 0
    for p, (rc, o) in node_res.items():
        if rc != 0:
            node_bad += 1
            problems.append(f"JS 语法错误: {p} :: {o.strip().splitlines()[0] if o.strip() else 'rc=' + str(rc)}")
            print(f"  ✗ {p}: {o.strip()[:160]}")
    print(f"  JS 解析(node --check)：{len(js) - node_bad}/{len(js)} 通过")

    py_bad = 0
    for p in py:
        try:
            compile(open(os.path.join(REPO_ROOT, p), encoding="utf-8").read(), p, "exec")
        except SyntaxError as exc:
            py_bad += 1
            problems.append(f"Python 语法错误: {p} :: {exc}")
            print(f"  ✗ {p}: {exc}")
    print(f"  Python 解析：{len(py) - py_bad}/{len(py)} 通过")

    verdict = "PASS" if not problems else "FAIL"
    report = {
        "counts": {"ps": len(ps), "js": len(js), "py": len(py)},
        "psBad": ps_bad, "nodeBad": node_bad, "pyBad": py_bad,
        "problems": problems, "verdict": verdict,
        "boundary": "只做语法解析，不执行脚本；运行期行为（缺参数、路径失效、依赖缺失）不在此覆盖",
    }
    with open(os.path.join(EVI, "shipped-scripts-parse.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
    try:
        os.remove(os.path.join(EVI, "_ps-parse-check.ps1"))
    except OSError:
        pass
    print(f"\nSHIPPED-SCRIPTS-PARSE RESULT={verdict} problems={len(problems)}")
    if problems:
        for p in problems:
            print("  -", p)
        sys.exit(1)


if __name__ == "__main__":
    main()
