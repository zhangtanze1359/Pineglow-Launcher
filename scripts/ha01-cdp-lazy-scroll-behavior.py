#!/usr/bin/env python3
"""HA-01 懒加载滚动：**走正式 CDP 接线**的行为验证（R3-01 / R3-02）。

为什么重写：上一版用 Playwright 的活 ElementHandle 直接调用辅助函数，
绕过了正式路径的 CDP 值序列化与 evaluate 包装器，因此它的 PASS **不能**证明主流程有效
（第三轮独立复验据此给出反例）。本版改为：

  1. 从正式脚本取 `buildLazyExprs(view)` 生成的**同一批表达式字符串**；
  2. 用与正式脚本**同样的包装器**发送 —— `Runtime.evaluate { expression, returnByValue: true }`；
  3. 断言元素身份没有跨边界传递（回归 R3-01：DOM 元素按值转换会变成 {}）；
  4. 断言非零初值的滚动位置被**恢复为原值**（回归 R3-02：旧实现是清零）。

夹具用内层 overflow 容器 + IntersectionObserver 延迟加载（headless Chromium 会把
loading=lazy 的图片在滚动前全部取走，无法表达「进入视口才加载」）。
退出码：0 PASS / 1 FAIL / 3 SKIP / 2 FATAL
"""
import json
import os
import socket
import sys
import threading
import time
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
  /* 复刻 ui/css/figma.css 的 .content-panel：真正的滚动容器 */
  .content-panel { height: 300px; overflow-y: auto; border: 1px solid #ccc; }
  .spacer { height: 700px; }
</style></head><body>
  <div id="view-fixture"><div class="content-panel" id="panel">
    <img id="top-img" src="/ok.png?top=1" width="40" height="40">
    <div class="spacer"></div>
    <img id="deep-img" data-defer-src="/ok.png?deferred=1" width="40" height="40">
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
      io.observe(document.getElementById('deep-img'));
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

        # ==== 与正式脚本完全相同的包装器（scripts/verify-ui-pages-live.cjs:583-586） ====
        def evaluate(expression):
            r = cdp.send("Runtime.evaluate", {"expression": expression, "returnByValue": True, "awaitPromise": True})
            if r.get("exceptionDetails"):
                raise RuntimeError(f"页面异常: {r['exceptionDetails'].get('text')}")
            result = r.get("result", {})
            if result.get("subtype") == "error":
                raise RuntimeError("页面返回 error 对象")
            return result.get("value")

        # ==== 从正式脚本取同一批表达式 ====
        # scrollAt 取多个下标（真实页面可能有多个视口外目标）；restore 需要把保存值传进去，
        # 所以先构造表达式骨架：用不可能出现的哨兵值填充，运行时再按需替换。
        import subprocess
        script = (
            "const m=require(process.argv[1]);"
            "const e=m.buildLazyExprs('fixture');"
            "process.stdout.write(JSON.stringify({save:e.save,count:e.count,"
            "scroll0:e.scrollAt(0),scroll1:e.scrollAt(1),"
            "restore:e.restore('__SAVED__')}));"
        )
        out = subprocess.run(
            ["node", "-e", script, str(VERIFIER)], capture_output=True, text=True, encoding="utf-8"
        )
        if out.returncode != 0:
            print(f"FATAL: 无法从正式脚本构造表达式：{out.stderr[:300]}")
            httpd.shutdown()
            return 2
        exprs = json.loads(out.stdout)
        results["exprsFromProduction"] = True

        # ---- 前置：容器未滚动、延迟图未加载 ----
        before = page.evaluate(
            """() => {
              const p = document.getElementById('panel');
              const d = document.getElementById('deep-img');
              return { panelScrollTop: p.scrollTop, deepTop: Math.round(d.getBoundingClientRect().top),
                       deepHasSrc: !!d.getAttribute('src') };
            }"""
        )
        results["before"] = before
        req_before = len(HITS)

        # ---- 走正式表达式：计数 + 逐个滚入 ----
        target_count = evaluate(exprs["count"])
        results["steps"]["count"] = target_count
        saved = evaluate(exprs["save"])
        results["steps"]["save"] = saved
        scrolls = []
        for i in range(int(target_count or 0)):
            res = evaluate(exprs[f"scroll{i}"] if f"scroll{i}" in exprs else exprs["scroll0"])
            scrolls.append(res)
            page.wait_for_timeout(1500)
        results["steps"]["scrolls"] = scrolls
        req_after = len(HITS)
        after = page.evaluate(
            """() => {
              const p = document.getElementById('panel');
              const d = document.getElementById('deep-img');
              return { panelScrollTop: p.scrollTop, deepTop: Math.round(d.getBoundingClientRect().top),
                       deepComplete: d.complete, deepNatural: d.naturalWidth,
                       deepHasSrc: !!d.getAttribute('src') };
            }"""
        )
        results["after"] = after
        results["httpRequests"] = {"before": req_before, "after": req_after}

        # ---- R3-01 回归：元素身份不能跨 CDP 传递（旧实现正是这样丢的） ----
        element_by_value = evaluate("document.getElementById('deep-img')")
        results["steps"]["elementByValueIsEmptyObject"] = (element_by_value == {} or element_by_value is None)
        results["steps"]["elementByValue"] = repr(element_by_value)[:80]

        # ---- R3-02 回归：非零初值必须被恢复为原值，而不是清零 ----
        page.evaluate("() => { document.getElementById('panel').scrollTop = 137; }")
        page.wait_for_timeout(200)
        saved2 = evaluate(exprs["save"])
        results["steps"]["saveNonZero"] = saved2
        page.evaluate("() => { document.getElementById('panel').scrollTop = 900; }")
        page.wait_for_timeout(200)
        restored = evaluate(exprs["restore"].replace("__SAVED__", json.dumps(saved2)))
        results["steps"]["restoreResult"] = restored
        final_top = page.evaluate("() => document.getElementById('panel').scrollTop")
        results["steps"]["finalScrollTop"] = final_top

        browser.close()
    httpd.shutdown()

    # ============================== 断言 ==============================
    if before["panelScrollTop"] != 0:
        results["problems"].append(f"前置：容器初值应为 0，实际 {before['panelScrollTop']}")
    if before["deepHasSrc"]:
        results["problems"].append("前置：延迟图在滚动前就已有 src，夹具没有延迟语义")
    # 计数必须真的看到视口外目标（旧实现返回 [{}]，count 也拿不到数字）
    if not isinstance(target_count, int) or target_count < 1:
        results["problems"].append(f"正式表达式 count 未返回可用的视口外目标数：{target_count!r}（R3-01 回归）")
    # 滚动必须真的发生
    if not scrolls or not any(isinstance(s, dict) and s.get("scrolled") for s in scrolls):
        results["problems"].append(f"正式路径滚动未生效：{scrolls!r}（R3-01）")
    if after["panelScrollTop"] <= 0:
        results["problems"].append(f"内层容器 scrollTop 仍为 {after['panelScrollTop']}：滚动没有作用于真实滚动祖先")
    # 懒加载必须被真正触发
    if req_after <= req_before:
        results["problems"].append("滚动后没有新的图片 HTTP 请求：懒加载未被触发")
    if not (after["deepComplete"] and after["deepNatural"] > 0):
        results["problems"].append(
            f"延迟图未加载成功：complete={after['deepComplete']} natural={after['deepNatural']}"
        )
    # R3-01 回归：按值取 DOM 元素必然得不到元素
    if not results["steps"]["elementByValueIsEmptyObject"]:
        results["problems"].append(
            f"预期 DOM 元素按值返回为空对象，实际 {results['steps']['elementByValue']}；回归前提不成立"
        )
    # R3-02 回归：非零原值必须恢复
    if not saved2 or not saved2.get("containers") or saved2["containers"][0]["top"] != 137:
        results["problems"].append(f"保存的原值不是 137：{saved2!r}")
    if final_top != 137:
        results["problems"].append(f"恢复后 scrollTop={final_top}，期望 137（R3-02：旧实现清零）")

    results["verdict"] = "PASS" if not results["problems"] else "FAIL"
    REPORT.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    print("\n--- 走正式 CDP 接线的表达式（buildLazyExprs 产出） ---")
    print(f"  count(视口外目标) = {target_count!r}")
    print(f"  save(原位置)      = {saved!r}")
    for i, s in enumerate(scrolls):
        print(f"  scrollAt({i})      = {s!r}")
    print(f"--- 滚动前 {before} ---")
    print(f"--- 滚动后 {after} ---")
    print(f"--- 图片 HTTP 请求 {req_before} -> {req_after} ---")
    print(f"--- R3-01 对照：按值取元素 = {results['steps']['elementByValue']}（应为空对象） ---")
    print(f"--- R3-02 对照：保存 {saved2.get('containers') if saved2 else None} -> 最终 scrollTop={final_top}（期望 137） ---")
    print(f"\nHA-01 CDP 接线行为验证判定: {results['verdict']}")
    for p in results["problems"]:
        print("  - " + p)
    return 0 if results["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
