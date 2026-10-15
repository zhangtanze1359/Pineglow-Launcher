"""表达式引用的静态检查（actionlint 的一个可静态化子集）。

actionlint 拿不到（本机网络阻断，见 run-20261006-0050-actionlint/），但它检查的很大一类是**纯静态**的：
`${{ }}` 里引用的东西到底存不存在。这类错误只在发布时才炸，本地从来没验过：

  - `steps.<id>.outputs...` —— 同一个 job 里**在它之前**必须真有这个 id 的步骤；
  - `matrix.<key>`         —— job 的 strategy.matrix（含 include）里必须有这个键；
  - `env.<name>`           —— workflow/job/step 三级 env 里必须定义过；
  - `needs.<job>`          —— 必须是真的 job 名；
  - `secrets.*` / `inputs.*` / `vars.*` —— 本地无从验证，**只列为"不可验证"**，不当失败。

另外顺带检查 upload-artifact 的 `path:`：该文件应当由同一 job 里**更早**的步骤产生。

边界（不得当成 actionlint）：不校验表达式语法细节、类型、函数、shell 语义、action 输入 schema、runner 行为。
"""

import os
import re
import sys
import shutil

import yaml

REPO_ROOT = r"D:\松光启动器"
FILES = [".github/workflows/build.yml", ".github/workflows/release.yml"]
EXPR_RE = re.compile(r"\$\{\{(.*?)\}\}", re.S)
UNVERIFIABLE = ("secrets.", "inputs.", "vars.", "job.", "strategy.", "github.", "runner.", "env.NODE",
                "steps.", "matrix.", "needs.")  # 逐步细化，见下


class Loader12(yaml.SafeLoader):
    """YAML 1.2 语义：不让 on/off/yes/no 变成布尔，这样 `on:` 才是键名。"""


for _ch, _res in list(Loader12.yaml_implicit_resolvers.items()):
    Loader12.yaml_implicit_resolvers[_ch] = [(t, r) for (t, r) in _res
                                             if t != "tag:yaml.org,2002:bool"]
Loader12.add_implicit_resolver("tag:yaml.org,2002:bool", re.compile(r"^(?:true|True|TRUE|false|False|FALSE)$"),
                               list("tTfF"))


def walk_strings(node, path=""):
    """把 YAML 里所有字符串连同其位置路径吐出来。"""
    if isinstance(node, dict):
        for k, v in node.items():
            yield from walk_strings(v, f"{path}.{k}")
    elif isinstance(node, list):
        for i, v in enumerate(node):
            yield from walk_strings(v, f"{path}[{i}]")
    elif isinstance(node, str):
        yield path, node


def check_file(path, problems, notes, jobs_index):
    doc = yaml.load(open(path, encoding="utf-8"), Loader=Loader12)
    jobs = doc.get("jobs", {}) or {}
    wf_env = set((doc.get("env") or {}).keys())

    for job_name, job in jobs.items():
        if not isinstance(job, dict):
            continue
        job_env = set((job.get("env") or {}).keys())
        matrix_keys = set()
        strategy = job.get("strategy") or {}
        matrix = strategy.get("matrix") or {}
        if isinstance(matrix, dict):
            for k, v in matrix.items():
                if k == "include" and isinstance(v, list):
                    for entry in v:
                        if isinstance(entry, dict):
                            matrix_keys |= set(entry.keys())
                else:
                    matrix_keys.add(k)

        defined_steps = set()
        produced = set()  # 同一 job 里更早步骤生成的文件名
        dotnet_publish_seen = False
        steps = job.get("steps") or []
        for idx, step in enumerate(steps):
            if not isinstance(step, dict):
                continue
            step_env = set((step.get("env") or {}).keys())
            label = f"{os.path.basename(path)}:{job_name}:步骤{idx + 1}({step.get('name') or step.get('uses') or '?'})"

            # ---- 表达式检查 ----
            for spath, text in walk_strings(step):
                for m in EXPR_RE.finditer(text):
                    expr = m.group(1)
                    for ref in re.findall(r"\b([A-Za-z_][\w-]*)\.([\w\-\.\[\]'\"*]+)", expr):
                        ctx, name = ref
                        first = name.split(".")[0].split("[")[0]
                        if ctx == "steps":
                            if first not in defined_steps:
                                problems.append(f"{label} {spath}: 引用了 `steps.{first}`，"
                                                f"但同一 job 里**在它之前**没有这个 id 的步骤（已定义: {sorted(defined_steps) or '无'}）")
                        elif ctx == "matrix":
                            if first not in matrix_keys:
                                problems.append(f"{label} {spath}: 引用了 `matrix.{first}`，"
                                                f"但 matrix 里没有这个键（有: {sorted(matrix_keys) or '无'}）")
                        elif ctx == "env":
                            if first not in (wf_env | job_env | step_env):
                                problems.append(f"{label} {spath}: 引用了 `env.{first}`，"
                                                f"但 workflow/job/step 三级 env 都没定义（workflow:{sorted(wf_env)} job:{sorted(job_env)} step:{sorted(step_env)}）")
                        elif ctx == "needs":
                            if first not in jobs:
                                problems.append(f"{label} {spath}: 引用了 `needs.{first}`，但没有这个 job（有: {sorted(jobs)}）")
                        elif ctx in ("secrets", "inputs", "vars", "github", "runner", "strategy", "job"):
                            notes.append(f"{label} {spath}: `{ctx}.{first}` 本地不可验证（需要仓库/事件上下文）")
                        else:
                            notes.append(f"{label} {spath}: 未知上下文 `{ctx}.{first}`（只记录）")

            # ---- 记录本步骤会产生什么文件（给 upload path 检查用）----
            run = step.get("run")
            if isinstance(run, str):
                if "dotnet publish" in run:
                    dotnet_publish_seen = True
                for f in re.findall(r"[\w./\\-]+\.(?:json|trx|exe|txt|md)", run):
                    produced.add(os.path.basename(f).replace("\\", "/"))
                for m in re.finditer(r"--json\s+([\w./\\-]+)", run):
                    produced.add(os.path.basename(m.group(1)).replace("\\", "/"))
                for m in re.finditer(r"(?:JsonPath|Set-Content\s+-Path|-JsonPath)\s+([\w./\\-]+)", run):
                    produced.add(os.path.basename(m.group(1)).replace("\\", "/"))

            # ---- upload-artifact 的 path 是否由更早步骤产生 ----
            uses = str(step.get("uses", ""))
            if uses.startswith("actions/upload-artifact"):
                paths = (step.get("with") or {}).get("path") or ""
                for line in str(paths).splitlines():
                    p = line.strip()
                    if not p:
                        continue
                    base = os.path.basename(p.rstrip("/")).replace("\\", "/")
                    # `dotnet publish` 不带 /p:PublishDir 时，默认输出就是 bin/<配置>/<tfm>/<rid>/publish/。
                    # 第一版检查器不认这条，把 build.yml 的上传路径误报成"没有产生它的步骤"（假阳性）。
                    default_publish_glob = re.search(r"bin/[^/]+/[^/]+/[^/]+/publish/?$", p) is not None
                    if default_publish_glob and dotnet_publish_seen:
                        notes.append(f"{label} path `{p}` 命中更早 `dotnet publish` 的**默认输出目录** ✓（非文件名匹配）")
                    elif base in produced:
                        notes.append(f"{label} path `{p}` 由更早步骤产生 ✓")
                    else:
                        problems.append(f"{label} path `{p}` 在**更早**的步骤里找不到产生它的痕迹"
                                        f"（可能是上游 checkout 自带、或路径写错；如属前者请在此处说明）")

            if step.get("id"):
                defined_steps.add(step["id"])

    jobs_index[os.path.basename(path)] = {n: (j or {}).get("needs") for n, j in jobs.items()}


def main():
    # 宿主控制台是 GBK：不重设编码的话，打印非 GBK 字符会直接 UnicodeEncodeError 崩掉
    # （与本仓库 VerifyHost 把控制台固定成 UTF-8 是同一类问题）。
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:  # noqa: BLE001
        pass
    problems, notes = [], []
    jobs_index = {}
    for f in FILES:
        check_file(os.path.join(REPO_ROOT, f), problems, notes, jobs_index)

    print("== 表达式/引用静态检查 ==")
    print(f"  不可验证项（只记录）: {len(notes)} 条")
    for n in notes[:12]:
        print("    ·", n)
    if len(notes) > 12:
        print(f"    …… 其余 {len(notes) - 12} 条同类")
    print(f"  问题: {len(problems)} 条")
    for p in problems:
        print("    ✗", p)

    # ---- 自证：注入两个错误引用，检查器必须报出来 ----
    tmp = os.path.join(REPO_ROOT, "docs", "release-remediation-evidence-2026-10-05",
                       "run-20261006-0300-expr-check", "red")
    if os.path.exists(tmp):
        shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(os.path.join(tmp, ".github", "workflows"))
    raw = open(os.path.join(REPO_ROOT, FILES[1]), encoding="utf-8").read()
    injected = raw.replace("/p:Version=${{ steps.version.outputs.version }}",
                           "/p:Version=${{ steps.nosuchstep.outputs.nope }} ${{ matrix.nokey }}") \
                 if "/p:Version=${{ steps.version.outputs.version }}" in raw else None
    red_ok = False
    if injected:
        with open(os.path.join(tmp, ".github", "workflows", "release.yml"), "w", encoding="utf-8") as fh:
            fh.write(injected)
        # 用同一个检查逻辑跑注入版
        p2, n2 = [], []
        old_files = list(FILES)
        try:
            FILES.clear()
            FILES.append(".github/workflows/release.yml")
            global REPO_ROOT_ORIG
            REPO_ROOT_ORIG = REPO_ROOT
            check_file(os.path.join(tmp, ".github", "workflows", "release.yml"), p2, n2, {})
        finally:
            FILES.clear()
            FILES.extend(old_files)
        hits = [x for x in p2 if "nosuchstep" in x], [x for x in p2 if "matrix.nokey" in x]
        red_ok = bool(hits[0]) and bool(hits[1])
        print("\n== 自证（RED）==")
        print(f"  注入 `steps.nosuchstep` 与 `matrix.nokey` 后：报出 {len(p2)} 条问题")
        for x in p2[:4]:
            print("    ✗", x[:150])
        print(f"  两个注入都被抓到: {red_ok}")
    else:
        print("\n== 自证（RED）== 未找到注入锚点，跳过（不冒充已验证）")

    verdict = "PASS" if (not problems and red_ok) else "FAIL"
    print(f"\nEXPR-CHECK RESULT={verdict} problems={len(problems)} red_selftest={red_ok}")
    if verdict != "PASS":
        sys.exit(1)


if __name__ == "__main__":
    main()
