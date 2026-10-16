#!/usr/bin/env python3
"""HA-01 懒加载滚动行为验证（内层滚动容器 + 真实 HTTP 图片 + 两次求值）。

背景（第二轮独立验收 H2-03）：旧实现的滚动固定作用于 window，而产品实际滚动的是内层
容器 `.content-panel`（ui/css/figma.css 的 overflow-y:auto）；而且连续同步滚动后立即
复位，不给浏览器渲染/懒加载调度机会。旧行为脚本只断言「offscreen 数量不为 0」，
证明不了「滚动有效」。

本脚本执行与生产**同一份**源码（从 scripts/verify-ui-pages-live.cjs 抽取
IMG_PROBE_TEMPLATE / LAZY_SCROLL_FN / LAZY_RESTORE_FN，不另写判定），并且
**分两次独立 evaluate** 运行（生产也是这样两次求值），因此可以断言：
  1. 修复前：内层容器 scrollTop 保持 0、图片请求 0 次、loaded=0（RED）
  2. 修复后：滚动使图片进入视口、HTTP 请求发生、loaded=1（GREEN）
  3. 复位后容器与 window 回到原位置
  4. 同容器内的可见破图仍被计为 broken
退出码：0 PASS / 1 FAIL / 3 SKIP(playwright 或浏览器不可用) / 2 FATAL
"""
import json
import os
import re
import socket
import sys
import threading
import time
from functools import partial
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

try:
    from playwright.sync_api import sync_playwright
except ImportError:
    print("SKIP: 未安装 python playwright")
    sys.exit(3)

VERIFIER = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "verify-ui-pages-live.cjs"
REPORT = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("ha01-lazy-scroll-report.json")
BROWSER_CHANNEL = os.environ.get("PLAYWRIGHT_CHANNEL", "msedge")
BROWSER_PATH = os.environ.get("HA01_BROWSER_PATH", "")

if not VERIFIER.exists():
    print(f"FATAL: 找不到正式脚本 {VERIFIER}")
    sys.exit(2)

src = VERIFIER.read_text(encoding="utf-8")


def grab(name):
    m = re.search(r"^const %s = `(.*?)`;" % re.escape(name), src, re.S | re.M)
    if not m:
        print(f"FATAL: 无法从正式脚本抽取 {name}")
        sys.exit(2)
    return m.group(1)


IMG_VISIBLE_FN = grab("IMG_VISIBLE_FN")
IMG_PROBE = (
    grab("IMG_PROBE_TEMPLATE")
    .replace("__VIEW__", "fixture")
    .replace("__VISIBLE_FN__", IMG_VISIBLE_FN)
)
LAZY_SCROLL_FN = grab("LAZY_SCROLL_FN")
LAZY_RESTORE_FN = grab("LAZY_RESTORE_FN")

PNG = bytes.fromhex(
    "89504e470d0a1a0a0000000d4948445200000001000000010806000000"
    "1f15c4890000000d4944415478da63f8cfc0f01f00050001ffabc818"
    "00000049456e44ae426082"
)
REQUESTS = {"png": 0, "slow": 0}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        if self.path.startswith("/ok.png"):
            REQUESTS["png"] += 1
            self._send_png()
        elif self.path.startswith("/slow.png"):
            REQUESTS["slow"] += 1
            time.sleep(6)          # 足够长：测量窗口内保持「加载中」
            self._send_png()
        else:
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()

    def _send_png(self):
        self.send_response(200)
        self.send_header("Content-Type", "image/png")
        self.send_header("Content-Length", str(len(PNG)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(PNG)

    def log_message(self, *args):
        pass


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


PAGE = """<!doctype html><html><head><meta charset="utf-8"><style>
  body { margin: 0; }
  /* 复刻 ui/css/figma.css 的 .content-panel：真正的滚动容器是它，不是 window */
  .content-panel { height: 300px; overflow-y: auto; border: 1px solid #ccc; }
  .spacer { height: 700px; }
</style></head><body>
  <div id="view-fixture">
    <div class="content-panel" id="panel">
      <img id="top-img" src="/ok.png?top=1" width="40" height="40">
      <div class="spacer"></div>
      <!-- 目标：用 IntersectionObserver 模拟真实懒加载（进入视口才设 src）。
           实测（lazy-recon.py）：headless Chromium 会把 loading="lazy" 的图片
           在滚动前就全部取走，用它无法表达"延迟到进入视口才加载"。
           观察器跑在页面主世界，因此不依赖 Playwright 注入脚本的时序。 -->
      <img id="deep-img" data-defer-src="/ok.png?deferred=1" width="40" height="40">
      <div class="spacer"></div>
      <img id="deep-broken" data-defer-src="/nope-404.png" width="40" height="40">
    </div>
  </div>
  <script>
    window.__deferredRequests = 0;
    (function () {
      const io = new IntersectionObserver(function (entries) {
        for (const e of entries) {
          if (!e.isIntersecting) continue;
          const el = e.target;
          if (el.dataset.deferSrc && !el.getAttribute('src')) {
            el.setAttribute('src', el.dataset.deferSrc);
          }
          io.unobserve(el);
        }
      }, { root: document.getElementById('panel'), threshold: 0 });
      io.observe(document.getElementById('deep-img'));
      io.observe(document.getElementById('deep-broken'));
    })();
  </script>
</body></html>"""


def main():
    port = free_port()
    httpd = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()

    page_html = PAGE.replace("/ok.png", f"http://127.0.0.1:{port}/ok.png")
    results = {"problems": []}

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
        page.wait_for_timeout(1500)

        def probe():
            return page.evaluate(IMG_PROBE)

        before = probe()
        results["before"] = before

        # 位置 / 滚动状态的直接观测
        geo_before = page.evaluate(
            """() => {
              const p = document.getElementById('panel');
              const d = document.getElementById('deep-img');
              return { panelScrollTop: p.scrollTop, winY: window.scrollY,
                       deepTop: d.getBoundingClientRect().top,
                       deepHasSrc: !!d.getAttribute('src'),
                       deepComplete: d.complete, deepNatural: d.naturalWidth };
            }"""
        )
        results["geoBefore"] = geo_before
        req_before = REQUESTS["png"]

        # === 生产同一份逻辑：逐个目标滚入（与 verify-ui-pages-live.cjs 的循环一致） ===
        # 注意用 page.evaluate_handle 传真实元素句柄：直接传 DOM 元素会被序列化成普通对象，
        # root.querySelectorAll 就不是函数了（实测踩过）。
        root_handle = page.evaluate_handle("() => document.getElementById('view-fixture')")
        # 延迟加载的候选在滚动前**没有 src**，因此按设计不参与「可见图片」统计
        # （没有 src 的节点不可能渲染出图片）。这里与真实懒加载一致：候选由夹具自己声明
        # （data-defer-src），靠滚动把它带进视口后观察器才赋 src。
        # 用 query_selector_all 拿真正的 JSHandle；JSHandle 传给 evaluate 会被序列化，
        # 必须用 evaluate_handle 才能作为活节点传入（否则报 getBoundingClientRect is not a function）。
        candidates = page.query_selector_all("img[data-defer-src]")
        results["offscreenTargets"] = len(candidates)
        page.wait_for_timeout(300)
        after_scroll = probe()
        results["afterScroll"] = after_scroll
        results["httpPngRequestsAfterScroll"] = REQUESTS["png"]
        results["perTarget"] = []

        for idx, target in enumerate(candidates):
            res = page.evaluate_handle(
                "(img) => (" + LAZY_SCROLL_FN + ")(img)", target
            ).json_value()
            geo = page.evaluate_handle(
                "(img) => ({ top: Math.round(img.getBoundingClientRect().top),"
                " complete: img.complete, natural: img.naturalWidth,"
                " hasSrc: !!img.getAttribute('src') })",
                target,
            ).json_value()
            results["perTarget"].append({"res": res, "geo": geo})
            # 目标之间等待，让渲染 / 懒加载有机会发生
            page.wait_for_timeout(1600)
        page.wait_for_timeout(1500)
        after_all = probe()
        results["afterAllScrolls"] = after_all
        geo_after_scroll = page.evaluate(
            """() => {
              const p = document.getElementById('panel');
              const d = document.getElementById('deep-img');
              return { panelScrollTop: p.scrollTop, winY: window.scrollY,
                       deepTop: d.getBoundingClientRect().top,
                       deepComplete: d.complete, deepNatural: d.naturalWidth };
            }"""
        )
        results["geoAfterScroll"] = geo_after_scroll
        results["httpPngRequestsAfterScroll"] = REQUESTS["png"]

        # === 复位 ===
        restored = page.evaluate(
            "(root) => (" + LAZY_RESTORE_FN + ")(root)",
            page.evaluate_handle("() => document.getElementById('view-fixture')"),
        )
        page.wait_for_timeout(300)
        geo_restored = page.evaluate("() => ({ panelScrollTop: document.getElementById('panel').scrollTop, winY: window.scrollY })")
        results["restored"] = restored
        results["geoRestored"] = geo_restored

        # === 语义反例：aria-hidden 的可见破图必须仍被统计并计为 broken ===
        aria = page.evaluate(
            """() => {
              const wrap = document.getElementById('panel');
              const mk = (id, src, extra) => {
                const i = document.createElement('img');
                i.id = id; i.src = src; i.width = 84; i.height = 84;
                if (extra) i.setAttribute('aria-hidden', 'true');
                wrap.appendChild(i); return i;
              };
              mk('aria-broken', 'http://127.0.0.1:1/dead.png', true);
              const panel = document.getElementById('panel');
              panel.scrollTop = panel.scrollHeight;   // 让新增破图进入视口
              return null;
            }"""
        )
        page.wait_for_timeout(1500)
        after_aria = probe()
        results["afterAriaBroken"] = after_aria

        # 受控「加载中 -> 成功」对照：用真正慢的响应（不是连接被拒，
        # 被拒会立刻 complete 并算破图，那样证明不了 pending）。
        page.evaluate(
            """() => {
              const i = document.createElement('img');
              i.id = 'pending-img'; i.src = 'http://127.0.0.1:__PORT__/slow.png?p=1';
              i.width = 40; i.height = 40;
              document.getElementById('panel').scrollTop = document.getElementById('panel').scrollHeight;
              document.getElementById('panel').appendChild(i);
            }""".replace("__PORT__", str(port))
        )
        page.wait_for_timeout(1200)                 # 响应还没回来 => 应保持未完成
        with_pending = probe()
        results["withPending"] = with_pending
        page.wait_for_timeout(7000)                 # 等慢响应返回 => 应变为已加载
        after_slow = probe()
        results["afterSlowLoaded"] = after_slow
        results["slowRequests"] = REQUESTS["slow"]

        browser.close()

    httpd.shutdown()

    # ------------------------------- 断言 -------------------------------
    b, a = results["before"], results["afterScroll"]
    gb, ga = results["geoBefore"], results["geoAfterScroll"]

    # 1) RED 前置条件：内层容器未滚动、目标在视口外、且目标尚未加载
    if gb["panelScrollTop"] != 0:
        results["problems"].append(f"前置条件：内层容器初始 scrollTop 应为 0，实际 {gb['panelScrollTop']}")
    if gb["deepTop"] < 700:
        results["problems"].append(f"前置条件：deep-img 初始应在视口外，实际 top={gb['deepTop']}")
    if gb["deepComplete"] and gb["deepNatural"] > 0:
        results["problems"].append("前置条件：deep-img 在滚动前就已加载，无法证明滚动触发了懒加载")
    if gb["deepHasSrc"]:
        results["problems"].append("前置条件：deep-img 在滚动前就被赋了 src，延迟加载语义没有生效")
    # 2) GREEN：生产滚动逻辑必须真的作用于内层容器，并让每个目标进入视口
    if ga["panelScrollTop"] <= 0:
        results["problems"].append(f"滚动后内层容器 scrollTop 仍为 {ga['panelScrollTop']}：滚动没有作用于真实滚动祖先（H2-03）")
    if results["offscreenTargets"] < 1:
        results["problems"].append("夹具里没有视口外目标，测不到滚动分支")
    for i, t in enumerate(results.get("perTarget", [])):
        if not t["res"].get("scrolled"):
            results["problems"].append(f"第 {i + 1} 个目标 scrollIntoView 后仍未进入视口：{t['geo']}")
        if not (0 < t["geo"]["top"] < 700):
            results["problems"].append(f"第 {i + 1} 个目标滚动后 top={t['geo']['top']} 不在视口内")
    # 3) 懒加载请求与加载结果（滚动后才应发生）
    if results["httpPngRequestsAfterScroll"] <= req_before:
        results["problems"].append("滚动后没有发生图片 HTTP 请求：未触发懒加载")
    aa = results.get("afterAllScrolls", {})
    if aa.get("loaded", 0) < 2:
        results["problems"].append(f"逐个滚入后 loaded={aa.get('loaded')}，延迟加载的图片未全部成功")
    if aa.get("broken", 0) < 1:
        results["problems"].append(f"延迟加载的 404 图片应计为 broken，实际 broken={aa.get('broken')}")
    # 4) 复位
    if results["geoRestored"]["panelScrollTop"] != 0 or results["geoRestored"]["winY"] != 0:
        results["problems"].append(f"滚动位置未复位：{results['geoRestored']}")
    # 5) aria-hidden 的可见破图仍必须被统计并计为 broken
    if after_aria.get("broken", 0) < 1:
        results["problems"].append(
            f"aria-hidden 的可见破图未被计入 broken（broken={after_aria.get('broken')}, total={after_aria.get('total')}）：H2-02 未修好"
        )
    # 6) 可见未完成不能被当成完成
    wp = results["withPending"]
    if wp.get("loaded", 0) + wp.get("broken", 0) >= wp.get("total", 0):
        results["problems"].append("存在可见未完成图片时仍被算作全部完成：真实 pending 被提前放行")
    # 7) 受控对照：慢响应返回后必须转为已加载
    sl = results["afterSlowLoaded"]
    if results["slowRequests"] < 1:
        results["problems"].append("慢响应端点没有被请求：pending 对照无效")
    if sl.get("loaded", 0) + sl.get("broken", 0) < sl.get("total", 0):
        results["problems"].append(
            f"慢响应返回后仍有未完成图片（loaded={sl.get('loaded')} broken={sl.get('broken')} total={sl.get('total')}）"
        )

    results["verdict"] = "PASS" if not results["problems"] else "FAIL"
    REPORT.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    print("\n--- RED 对照（滚动前） ---")
    print(f"  内层容器 scrollTop={gb['panelScrollTop']}  deep-img top={gb['deepTop']}  "
          f"probe total={b.get('total')} loaded={b.get('loaded')} broken={b.get('broken')} offscreen={b.get('offscreen')}")
    print("--- GREEN（逐个执行生产 LAZY_SCROLL_FN，期间等待后复测） ---")
    print(f"  内层容器 scrollTop={ga['panelScrollTop']}  HTTP 图片请求 {req_before} -> {results['httpPngRequestsAfterScroll']}")
    for i, t in enumerate(results.get("perTarget", [])):
        print(f"  目标{i + 1}: scrolled={t['res'].get('scrolled')} top={t['geo']['top']} "
              f"complete={t['geo']['complete']} natural={t['geo']['natural']} hasSrc={t['geo']['hasSrc']} "
              f"容器数={t['res'].get('containerCount')}")
    print(f"  逐个滚入后 probe: total={aa.get('total')} loaded={aa.get('loaded')} broken={aa.get('broken')}")
    print(f"--- 复位：容器 {results['geoRestored']['panelScrollTop']} / window {results['geoRestored']['winY']}（期望均 0）")
    print(f"--- aria-hidden 可见破图：total={after_aria.get('total')} broken={after_aria.get('broken')}（期望 broken>=1）")
    print(f"--- 慢响应对照：请求 {results['slowRequests']} 次；等待中 total={wp.get('total')} loaded={wp.get('loaded')} broken={wp.get('broken')}"
          f" -> 返回后 loaded={sl.get('loaded')} broken={sl.get('broken')} total={sl.get('total')}")
    print(f"\nHA-01 懒加载滚动行为验证判定: {results['verdict']}")
    for p in results["problems"]:
        print("  - " + p)
    return 0 if results["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
