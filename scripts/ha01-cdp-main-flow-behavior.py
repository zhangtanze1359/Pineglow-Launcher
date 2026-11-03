#!/usr/bin/env python3
"""HA-01 懒加载：**正式主流程分支**的隔离复验（R4-02 入口条件 + R4-03 finally 恢复）。

与 ha01-cdp-lazy-scroll-behavior.py 的区别：那个只验证辅助函数/表达式；
第四轮独立验收指出「只调用 count/scrollAt 会绕过入口条件」，因此这里复刻正式脚本里
**整段懒加载分支**（入口判断 → 保存 → 逐目标滚动 → 复测 → 恢复），并断言：

  R4-02  只有延迟占位图（无 src，故图片探针 total=1/offscreen=0）时，
         入口条件仍必须进入滚动分支；滚完后这些目标要真的拿到 src 并加载，
         且最终完成判定（imagesPending 口径）不能放行。
  R4-03  探测中途抛错时仍必须恢复原有滚动位置（finally），
         且恢复失败要单独记录、不掩盖原始探测错误。

所有表达式来自正式脚本的 buildLazyExprs(view)；包装器与正式脚本相同。
退出码：0 PASS / 1 FAIL / 3 SKIP / 2 FATAL
"""
import json
import os
import socket
import subprocess
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

try:
    from playwright.sync_api import sync_playwright
except ImportError:
    print("SKIP: 未安装 python playwright")
    sys.exit(3)

VERIFIER = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parent / "verify-ui-pages-live.cjs"
REPORT = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("ha01-cdp-main-flow-report.json")
BROWSER_CHANNEL = os.environ.get("PLAYWRIGHT_CHANNEL", "msedge")
BROWSER_PATH = os.environ.get("HA01_BROWSER_PATH", "")

if not VERIFIER.exists():
    print(f"FATAL: 找不到正式脚本 {VERIFIER}")
    sys.exit(2)

PNG = bytes.fromhex(
    "89504e470d0a1a0a0000000d4948445200000001000000010806000000"
    "1f15c4890000000d4944415478da63f8cfc0f01f00050001ffabc818"
    "00000049456e44ae426082"
)


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        if self.path.startswith("/ok.png"):
            self.send_response(200)
            self.send_header("Content-Type", "image/png")
            self.send_header("Content-Length", str(len(PNG)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(PNG)
        else:
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()

    def log_message(self, *a):
        pass


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


PAGE = """<!doctype html><html><head><meta charset="utf-8"><style>
  body { margin: 0; }
  .content-panel { height: 300px; overflow-y: auto; border: 1px solid #ccc; }
  .spacer { height: 700px; }
</style></head><body>
  <div id="view-fixture"><div class="content-panel" id="panel">
    <img id="top-img" src="/ok.png?top=1" width="40" height="40">
    <div class="spacer"></div>
    <img id="defer-a" data-defer-src="/ok.png?d=a" width="40" height="40">
    <div class="spacer"></div>
    <img id="defer-b" data-defer-src="/ok.png?d=b" width="40" height="40">
  </div></div>
  <script>
    (function () {
      const io = new IntersectionObserver(function (entries) {
        for (const e of entries) {
          if (!e.isIntersecting) continue;
          const el = e.target;
          if (el.dataset.deferSrc && !el.getAttribute('src')) el.setAttribute('src', el.dataset.deferSrc);
          io.unobserve(el);
        }
      }, { root: document.getElementById('panel'), threshold: 0 });
      io.observe(document.getElementById('defer-a'));
      io.observe(document.getElementById('defer-b'));
    })();
  </script>
</body></html>"""


def main():
    port = free_port()
    httpd = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    page_html = PAGE.replace("/ok.png", f"http://127.0.0.1:{port}/ok.png")
    results = {"problems": [], "steps": {}}

    with sync_playwright() as pw:
        kw = {"headless": True}
        if BROWSER_PATH:
            kw["executable_path"] = BROWSER_PATH
        else:
            kw["channel"] = BROWSER_CHANNEL
        try:
            browser = pw.chromium.launch(**kw)
        except Exception as e:
            httpd.shutdown()
            print(f"SKIP: 无法启动浏览器（{kw}）：{str(e).splitlines()[0][:160]}")
            return 3
        page = browser.new_page(viewport={"width": 900, "height": 700})
        page.set_content(page_html, wait_until="load")
        page.wait_for_timeout(1200)
        cdp = page.context.new_cdp_session(page)

        def evaluate(expression):
            r = cdp.send("Runtime.evaluate", {"expression": expression, "returnByValue": True, "awaitPromise": True})
            if r.get("exceptionDetails"):
                raise RuntimeError(f"页面异常: {r['exceptionDetails'].get('text')}")
            return r.get("result", {}).get("value")

        script = (
            "const m=require(process.argv[1]);"
            "const e=m.buildLazyExprs('fixture');"
            "const scroll={}; for (let i=0;i<12;i++) scroll[i]=e.scrollAt(i);"
            "process.stdout.write(JSON.stringify({save:e.save,targets:e.targets,scroll:scroll,"
            "restore:e.restore('__SAVED__'),probe:m.buildImgProbeExpr('fixture')}));"
        )
        out = subprocess.run(["node", "-e", script, str(VERIFIER)], capture_output=True, text=True, encoding="utf-8")
        if out.returncode != 0:
            print(f"FATAL: 无法从正式脚本构造表达式：{out.stderr[:300]}")
            httpd.shutdown()
            return 2
        exprs = json.loads(out.stdout)
        scroll_exprs = exprs["scroll"]

        # ---- 入口前的图片探针（正式口径） ----
        probe_before = evaluate(exprs["probe"])
        results["steps"]["probeBefore"] = probe_before

        targets = evaluate(exprs["targets"])
        results["steps"]["targets"] = targets
        target_list = targets if isinstance(targets, list) else []

        # ================= 复刻正式主流程分支 =================
        def run_main_block(inject_error_after_first=False):
            saved = None
            in_view = 0
            containers = 0
            probe_error = None
            problems = []
            restore_result = None
            restore_error = None
            try:
                saved = evaluate(exprs["save"])
                for i, t in enumerate(target_list):
                    idx = t.get("domIndex") if isinstance(t, dict) else t
                    res = evaluate(scroll_exprs[str(idx)])
                    if res:
                        if res.get("scrolled"):
                            in_view += 1
                        containers = max(containers, res.get("containerCount") or 0)
                    if inject_error_after_first and i == 0:
                        raise RuntimeError("注入的可恢复探测错误")
                    page.wait_for_timeout(1200)
            except Exception as e:
                probe_error = str(e)
                problems.append(f"懒加载滚动探测失败 {e}")
            finally:
                try:
                    restore_result = evaluate(exprs["restore"].replace("__SAVED__", json.dumps(saved or {"containers": [], "windowX": 0, "windowY": 0})))
                except Exception as e:
                    restore_error = str(e)
                    problems.append(f"懒加载滚动恢复失败 {e}" + (f"（原始探测错误：{probe_error}）" if probe_error else ""))
            return {"saved": saved, "inView": in_view, "containers": containers,
                    "probeError": probe_error, "restoreResult": restore_result,
                    "restoreError": restore_error, "problems": problems}

        # ---- A) 正常路径：入口条件必须因延迟占位而进入 ----
        entry_ok = len(target_list) > 0
        results["steps"]["entryCondition"] = {
            "probeOffscreen": probe_before.get("offscreen"),
            "probeTotal": probe_before.get("total"),
            "probeDeferred": probe_before.get("deferred"),
            "targets": len(target_list),
        }
        block_a = run_main_block()
        results["steps"]["blockA"] = block_a
        after_a = page.evaluate(
            """() => {
              const a = document.getElementById('defer-a'), b = document.getElementById('defer-b');
              return { aHasSrc: !!a.getAttribute('src'), aNatural: a.naturalWidth,
                       bHasSrc: !!b.getAttribute('src'), bNatural: b.naturalWidth,
                       panelScrollTop: document.getElementById('panel').scrollTop };
            }"""
        )
        results["steps"]["afterA"] = after_a
        probe_after = evaluate(exprs["probe"])
        results["steps"]["probeAfter"] = probe_after

        # ---- B) 错误注入：恢复必须仍然执行 ----
        page.evaluate("() => { document.getElementById('panel').scrollTop = 137; }")
        page.wait_for_timeout(150)
        block_b = run_main_block(inject_error_after_first=True)
        results["steps"]["blockB"] = block_b
        recovered = page.evaluate("() => document.getElementById('panel').scrollTop")
        results["steps"]["scrollTopAfterErrorInjection"] = recovered

        browser.close()
    httpd.shutdown()

    # ============================== 断言 ==============================
    # R4-02：入口条件不能因为探针 offscreen=0 就跳过整段
    if not entry_ok:
        results["problems"].append(
            f"入口条件把延迟占位挡在流程外：probe offscreen={probe_before.get('offscreen')} "
            f"deferred={probe_before.get('deferred')} targets={len(target_list)}（R4-02）"
        )
    if len(target_list) != 2:
        results["problems"].append(f"应识别 2 个延迟目标，实际 {target_list!r}")
    # 正常路径：两个延迟目标都要拿到 src 并加载
    if not after_a["aHasSrc"] or not after_a["bHasSrc"]:
        results["problems"].append(f"延迟目标未取得来源：{after_a}（R4-02）")
    if not (after_a["aNatural"] > 0 and after_a["bNatural"] > 0):
        results["problems"].append(f"延迟目标未加载成功：{after_a}（R4-02）")
    if block_a["inView"] != 2:
        results["problems"].append(f"正常路径只滚入 {block_a['inView']} 个目标，应为 2")
    # 滚完后不应再有未完成的延迟目标
    if probe_after.get("deferred", 0) != 0:
        results["problems"].append(f"滚完后仍有 {probe_after.get('deferred')} 个延迟目标没有来源")
    # R4-03：错误注入后仍必须恢复原值
    if not block_b["probeError"]:
        results["problems"].append("错误注入没有生效，R4-03 未被真正触发")
    if block_b["restoreResult"] is None and not block_b["restoreError"]:
        results["problems"].append("恢复调用没有执行（既无结果也无错误）")
    if recovered != 137:
        results["problems"].append(
            f"探测抛错后滚动位置为 {recovered}，期望恢复为 137（R4-03：恢复必须在 finally）"
        )

    results["verdict"] = "PASS" if not results["problems"] else "FAIL"
    REPORT.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    print("\n--- 入口条件（R4-02）---")
    print(f"  正式图片探针: total={probe_before.get('total')} offscreen={probe_before.get('offscreen')} "
          f"deferred={probe_before.get('deferred')}")
    print(f"  buildLazyExprs.targets = {targets!r}  => 入口 {'进入' if entry_ok else '被跳过'}")
    print("--- A) 正常路径 ---")
    print(f"  滚入 {block_a['inView']}/2，容器数 {block_a['containers']}")
    print(f"  滚完后 {after_a}")
    print(f"  复测: total={probe_after.get('total')} loaded={probe_after.get('loaded')} "
          f"broken={probe_after.get('broken')} deferred={probe_after.get('deferred')}")
    print("--- B) 错误注入（R4-03）---")
    print(f"  注入错误生效: {bool(block_b['probeError'])}  恢复结果={block_b['restoreResult']}  "
          f"恢复错误={block_b['restoreError']}")
    print(f"  注入后 scrollTop={recovered}（期望 137）")
    print(f"\nHA-01 主流程隔离复验判定: {results['verdict']}")
    for p in results["problems"]:
        print("  - " + p)
    return 0 if results["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
