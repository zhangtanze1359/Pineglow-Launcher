#!/usr/bin/env python3
"""HA-01 懒加载滚动：**走正式 CDP 接线**的行为验证（辅助函数级）。

覆盖范围（与 ha01-cdp-main-flow-behavior.py 互补）：
  - 表达式由正式脚本的 buildLazyExprs(view) 产出，用与正式脚本**相同**的包装器发送
    （Runtime.evaluate + returnByValue），因此覆盖真实接线而不是另写一套判定；
  - targets 返回**固定的 DOM 序号**，两个目标都会被滚到（回归 R4-01：
    旧实现对"随滚动变化的候选数组"递增下标，会跳过第二个目标）；
  - 非零初值的滚动位置恢复为**原值**（回归 R3-02，旧实现是清零）；
  - 元素身份不能跨 CDP 传递的对照（回归 R3-01）。

夹具用内层 overflow 容器 + IntersectionObserver 延迟加载（headless Chromium 会把
loading=lazy 的图片在滚动前全部取走，无法表达「进入视口才加载」）。
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
REPORT = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("ha01-cdp-lazy-scroll-report.json")
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
HITS = []


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        if self.path.startswith("/ok.png"):
            HITS.append(self.path)
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
    <img id="deep-a" data-defer-src="/ok.png?deferred=a" width="40" height="40">
    <div class="spacer"></div>
    <img id="deep-b" data-defer-src="/ok.png?deferred=b" width="40" height="40">
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
      io.observe(document.getElementById('deep-a'));
      io.observe(document.getElementById('deep-b'));
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

        # ==== 与正式脚本完全相同的包装器 ====
        def evaluate(expression):
            r = cdp.send("Runtime.evaluate", {"expression": expression, "returnByValue": True, "awaitPromise": True})
            if r.get("exceptionDetails"):
                raise RuntimeError(f"页面异常: {r['exceptionDetails'].get('text')}")
            result = r.get("result", {})
            return result.get("value")

        # ==== 表达式全部来自正式脚本 ====
        script = (
            "const m=require(process.argv[1]);"
            "const e=m.buildLazyExprs('fixture');"
            "const scroll={}; for (let i=0;i<12;i++) scroll[i]=e.scrollAt(i);"
            "process.stdout.write(JSON.stringify({save:e.save,count:e.count,targets:e.targets,"
            "scroll:scroll,restore:e.restore('__SAVED__')}));"
        )
        out = subprocess.run(["node", "-e", script, str(VERIFIER)], capture_output=True, text=True, encoding="utf-8")
        if out.returncode != 0:
            print(f"FATAL: 无法从正式脚本构造表达式：{out.stderr[:300]}")
            httpd.shutdown()
            return 2
        exprs = json.loads(out.stdout)
        scroll_exprs = exprs["scroll"]

        before = page.evaluate(
            """() => {
              const p = document.getElementById('panel');
              const a = document.getElementById('deep-a'), b = document.getElementById('deep-b');
              return { panelScrollTop: p.scrollTop,
                       aTop: Math.round(a.getBoundingClientRect().top), aHasSrc: !!a.getAttribute('src'),
                       bTop: Math.round(b.getBoundingClientRect().top), bHasSrc: !!b.getAttribute('src') };
            }"""
        )
        results["before"] = before
        req_before = len(HITS)

        targets = evaluate(exprs["targets"])
        results["steps"]["targets"] = targets
        target_list = targets if isinstance(targets, list) else []
        saved = evaluate(exprs["save"])
        results["steps"]["save"] = saved

        scrolls = []
        for t in target_list:
            idx = t.get("domIndex") if isinstance(t, dict) else t
            expr = scroll_exprs.get(str(idx))
            if not expr:
                results["problems"].append(f"没有为 DOM 序号 {idx} 生成滚动表达式")
                continue
            scrolls.append({"domIndex": idx, "res": evaluate(expr)})
            page.wait_for_timeout(1500)
        results["steps"]["scrolls"] = scrolls

        req_after = len(HITS)
        after = page.evaluate(
            """() => {
              const p = document.getElementById('panel');
              const a = document.getElementById('deep-a'), b = document.getElementById('deep-b');
              return { panelScrollTop: p.scrollTop,
                       aHasSrc: !!a.getAttribute('src'), aNatural: a.naturalWidth,
                       bHasSrc: !!b.getAttribute('src'), bNatural: b.naturalWidth };
            }"""
        )
        results["after"] = after
        results["httpRequests"] = {"before": req_before, "after": req_after}

        # R3-01 对照：按值取 DOM 元素拿不到元素
        by_value = evaluate("document.getElementById('deep-a')")
        results["steps"]["elementByValue"] = repr(by_value)[:60]

        # R3-02 回归：非零初值必须恢复原值
        page.evaluate("() => { document.getElementById('panel').scrollTop = 137; }")
        page.wait_for_timeout(150)
        saved2 = evaluate(exprs["save"])
        results["steps"]["saveNonZero"] = saved2
        page.evaluate("() => { document.getElementById('panel').scrollTop = 900; }")
        page.wait_for_timeout(150)
        restored = evaluate(exprs["restore"].replace("__SAVED__", json.dumps(saved2)))
        results["steps"]["restoreResult"] = restored
        final_top = page.evaluate("() => document.getElementById('panel').scrollTop")
        results["steps"]["finalScrollTop"] = final_top

        browser.close()
    httpd.shutdown()

    # ============================== 断言 ==============================
    if before["aHasSrc"] or before["bHasSrc"]:
        results["problems"].append("前置：延迟图在滚动前就已有 src，夹具没有延迟语义")
    # targets 必须是可序列化的序号数组
    if not isinstance(targets, list) or len(targets) != 2:
        results["problems"].append(f"targets 应为 2 个固定序号，实际 {targets!r}（R4-01 回归）")
    # R4-01：两个目标都必须被滚到并加载
    if len(scrolls) != 2:
        results["problems"].append(f"实际只滚了 {len(scrolls)} 个目标，应滚 2 个（R4-01：递增下标会跳过）")
    for s in scrolls:
        if not (isinstance(s["res"], dict) and s["res"].get("scrolled")):
            results["problems"].append(f"DOM 序号 {s['domIndex']} 未滚入视口：{s['res']!r}")
    if not (after["aNatural"] > 0 and after["bNatural"] > 0):
        results["problems"].append(
            f"两个延迟目标未全部加载：a.natural={after['aNatural']} b.natural={after['bNatural']}（R4-01）"
        )
    if after["panelScrollTop"] <= 0:
        results["problems"].append(f"容器 scrollTop 仍为 {after['panelScrollTop']}：滚动未作用于真实滚动祖先")
    if req_after - req_before < 2:
        results["problems"].append(f"滚动后新增图片请求只有 {req_after - req_before} 个，应为 2")
    if not (by_value == {} or by_value is None):
        results["problems"].append(f"预期按值取元素为空对象，实际 {by_value!r}")
    if not saved2 or not saved2.get("containers") or saved2["containers"][0]["top"] != 137:
        results["problems"].append(f"保存的原值不是 137：{saved2!r}")
    if final_top != 137:
        results["problems"].append(f"恢复后 scrollTop={final_top}，期望 137（R3-02）")

    results["verdict"] = "PASS" if not results["problems"] else "FAIL"
    REPORT.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    print("\n--- 走正式 CDP 接线的表达式（buildLazyExprs 产出） ---")
    print(f"  targets = {targets!r}")
    print(f"  save    = {saved!r}")
    for s in scrolls:
        print(f"  scrollAt(domIndex={s['domIndex']}) = {s['res']!r}")
    print(f"--- 滚动前 {before} ---")
    print(f"--- 滚动后 {after} ---")
    print(f"--- 图片 HTTP 请求 {req_before} -> {req_after}（应 +2） ---")
    print(f"--- R3-01 对照：按值取元素 = {results['steps']['elementByValue']}（应为空对象） ---")
    print(f"--- R3-02 对照：保存 {saved2.get('containers') if saved2 else None} -> 最终 scrollTop={final_top}（期望 137） ---")
    print(f"\nHA-01 CDP 接线行为验证判定: {results['verdict']}")
    for p in results["problems"]:
        print("  - " + p)
    return 0 if results["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
