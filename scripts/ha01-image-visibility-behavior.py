#!/usr/bin/env python3
"""HA-01 行为验证（真实浏览器布局，非 DOM 桩）。

判定依赖 getComputedStyle / getBoundingClientRect / naturalWidth / isConnected，
这些只有真实布局引擎才有意义。这里直接从**正式脚本源码**抽出 IMG_VISIBLE_FN
（不是复制一份），在真实浏览器里对每个图片节点求值并断言分类结果。

覆盖流程要求的四类：
  A 隐藏（含隐藏祖先、display:none 的 .mod-icon、显式 0 尺寸、无 src 占位）不应误报
  B 可见破图仍必须报错
  C 可见未完成图片不能提前算作完成
  D 视口外节点保留统计并触发滚动探测（不能被当成"没渲染"）
另外保留"修复前口径"的对照，证明用例有区分力。

前置条件：本机可用的 Chromium 内核浏览器。默认用 Edge（channel=msedge）；
未安装时用 PLAYWRIGHT_CHANNEL 覆盖，或用 HA01_BROWSER_PATH 指定可执行文件。
缺 Python playwright 或浏览器不可用时以退出码 3 明确跳过，不伪装成通过。

用法：
  python scripts/ha01-image-visibility-behavior.py [正式脚本路径] [报告输出路径]
"""
import json
import os
import re
import sys
from pathlib import Path

try:
    from playwright.sync_api import sync_playwright
except ImportError:
    print("SKIP: 未安装 python playwright（pip install playwright），本项无法执行")
    sys.exit(3)

VERIFIER = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "scripts" / "verify-ui-pages-live.cjs"
REPORT = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("ha01-behavior-report.json")
BROWSER_CHANNEL = os.environ.get("PLAYWRIGHT_CHANNEL", "msedge")
BROWSER_PATH = os.environ.get("HA01_BROWSER_PATH", "")

if not VERIFIER.exists():
    print(f"FATAL: 找不到正式脚本 {VERIFIER}")
    sys.exit(2)

src = VERIFIER.read_text(encoding="utf-8")
m = re.search(r"const IMG_VISIBLE_FN = `(.*?)`;", src, re.S)
if not m:
    print("FATAL: 无法从正式脚本中抽取 IMG_VISIBLE_FN")
    sys.exit(2)
IMG_VISIBLE_FN = m.group(1)
print(f"抽取到 IMG_VISIBLE_FN（{len(IMG_VISIBLE_FN)} 字符）来自 {VERIFIER.name}")

PNG = ("data:image/png;base64,"
       "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==")
DEAD = "http://127.0.0.1:1/dead.png"   # 连接被拒 => 走到 complete 且 naturalWidth=0

PAGE = f"""<!doctype html><html><head><meta charset="utf-8"><style>
  body {{ margin: 0; }}
  #hiddenAncestor {{ display: none; }}
  #invisibleAncestor {{ visibility: hidden; }}
  .mod-icon {{ display: none; }}
  #zeroBoxExplicit img {{ width: 0; height: 0; }}
  #scroller {{ height: 300px; overflow: auto; }}
  .spacer {{ height: 40px; }}
  #tallSpacer {{ height: 2000px; }}
</style></head><body>
  <img id="visible-broken" src="{DEAD}" width="20" height="20">
  <img id="offscreen-lazy" src="{PNG}" loading="lazy" width="20" height="20">
  <img id="no-src-attr" hidden width="20" height="20">
  <img id="hidden-attr" src="{PNG}" hidden width="20" height="20">
  <img id="aria-hidden-visible" src="{DEAD}?ah=1" aria-hidden="true" width="84" height="84">
  <img id="type-mislabeled-visible" src="{DEAD}?tp=1" type="image/svg+xml" width="84" height="84">
  <div id="hiddenAncestor"><img id="ancestor-display-none" src="{PNG}" width="20" height="20"></div>
  <div id="invisibleAncestor"><img id="ancestor-visibility" src="{PNG}" width="20" height="20"></div>
  <div class="mod-icon"><img id="content-icon-hidden" src="{PNG}" width="20" height="20"></div>
  <div id="zeroBoxExplicit"><img id="zero-explicit" src="{PNG}"></div>
  <div id="scroller">
    <img id="inside-scroller" src="{PNG}" width="20" height="20">
    <div class="spacer"></div>
    <img id="scrolled-out" src="{PNG}" width="20" height="20">
  </div>
  <div id="tallSpacer"></div>
  <img id="below-viewport" src="{PNG}" loading="lazy" width="20" height="20">
  <svg width="20" height="20"><image id="svg-image" href="{PNG}" width="20" height="20"></image></svg>
  <div id="removed-host"></div>
  <img id="pending-img" src="{DEAD}?hang=1" width="20" height="20">
  <script>
    const host = document.getElementById('removed-host');
    const gone = document.createElement('img');
    gone.id = 'detached'; gone.src = '{PNG}'; gone.width = 20; gone.height = 20;
    host.appendChild(gone);
    window.__detached = gone;
    host.removeChild(gone);
    window.__zeroBoxSized = (() => {{
      const d = document.createElement('div');
      d.style.cssText = 'display:block;width:0;height:0;overflow:hidden';
      const i = document.createElement('img');
      i.id = 'zero-box-sized'; i.src = '{PNG}'; i.width = 20; i.height = 20;
      d.appendChild(i); document.body.appendChild(d); return i;
    }})();
    window.__zeroBox = (() => {{
      const d = document.createElement('div');
      d.style.cssText = 'display:block;width:0;height:0;overflow:hidden';
      const i = document.createElement('img');
      i.id = 'zero-box'; i.src = '{PNG}';
      d.appendChild(i); document.body.appendChild(d); return i;
    }})();
  </script>
</body></html>"""

EXPECT_VISIBLE = [
    "visible-broken",
    "offscreen-lazy",
    "pending-img",
    "zero-box",          # 实测 1x1：无尺寸属性时 img 保持自然尺寸，父容器 0x0 不改变其布局盒
    "zero-box-sized",    # 实测 20x20
    "below-viewport",    # 视口外但仍在布局内 => 保留统计（懒加载分支）
    "scrolled-out",
    # 第二轮独立验收的反例：aria-hidden 只对辅助技术隐藏，元素照样可见；
    # HTML <img> 的 type 属性也不会把它变成 SVG <image>。二者都必须参与统计，
    # 否则可见破图会被漏报（旧实现在这里错排除，已被反例证伪）。
    "aria-hidden-visible",
    "type-mislabeled-visible",
]
EXPECT_HIDDEN = [
    "no-src-attr", "hidden-attr",
    "ancestor-display-none", "ancestor-visibility",
    "content-icon-hidden",   # 真实 .mod-icon display:none -> 0x0
    "zero-explicit",         # 显式 width:0;height:0
    "svg-image",             # 真正的 SVG <image> 节点（不是 HTMLImageElement）
    "detached",              # isConnected=false
]
EXPECT_OFFSCREEN = ["below-viewport", "scrolled-out"]
# 这些节点无法用 getElementById 取到，必须用专门的求值方式（否则会被本轮循环覆盖成 MISSING）
LOOP_EXCLUDED = {"detached"}


def main():
    results = {"visible": {}, "hidden": {}, "counts": {}, "legacyCounts": {}, "problems": []}
    with sync_playwright() as pw:
        launch_kw = {"headless": True}
        if BROWSER_PATH:
            launch_kw["executable_path"] = BROWSER_PATH
        else:
            launch_kw["channel"] = BROWSER_CHANNEL
        try:
            browser = pw.chromium.launch(**launch_kw)
        except Exception as e:
            print(f"SKIP: 无法启动浏览器（{launch_kw}）：{str(e).splitlines()[0][:160]}")
            print("      可用 PLAYWRIGHT_CHANNEL 或 HA01_BROWSER_PATH 指定本机浏览器")
            return 3
        page = browser.new_page(viewport={"width": 800, "height": 600})
        page.set_content(PAGE, wait_until="load")
        page.wait_for_timeout(2500)

        fn = IMG_VISIBLE_FN
        for name in EXPECT_VISIBLE + [n for n in EXPECT_HIDDEN if n not in LOOP_EXCLUDED]:
            js = ("(name) => { const el = document.getElementById(name);"
                  " if (!el) return 'MISSING';"
                  " return (__FN__)(el); }").replace("__FN__", fn)
            val = page.evaluate(js, name)
            want = name in EXPECT_VISIBLE
            results["visible" if want else "hidden"][name] = val
            if val != want:
                results["problems"].append(f"{name}: 期望 visible={want}，实际={val}")

        # isConnected=false 的节点：在同一次主世界求值里创建+移除+判定，
        # 避免依赖内联脚本的执行时机（set_content 下 window.__detached 不一定可见）。
        js_det = ("() => { const host = document.getElementById('removed-host');"
                  " const g = document.createElement('img');"
                  " g.src = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==';"
                  " g.width = 20; g.height = 20;"
                  " host.appendChild(g); const connectedWhenAttached = (__FN__)(g);"
                  " host.removeChild(g); const afterRemove = (__FN__)(g);"
                  " return { connectedWhenAttached, afterRemove, isConnected: g.isConnected }; }"
                 ).replace("__FN__", fn)
        det = page.evaluate(js_det)
        results["hidden"]["detached"] = det["afterRemove"]
        results["detachedDetail"] = det
        if det["afterRemove"] is not False:
            results["problems"].append(
                f"detached: 期望移除后 visible=False，实际={det['afterRemove']}"
            )
        if det["connectedWhenAttached"] is not True:
            results["problems"].append(
                f"detached 对照项：仍挂载时应为 visible=True，实际={det['connectedWhenAttached']}"
            )

        counts = page.evaluate(
            ("() => {\n"
             "  const root = document.body;\n"
             "  const visible = (__FN__);\n"
             "  const imgs = Array.from(root.querySelectorAll('img')).filter(visible);\n"
             "  return {\n"
             "    rawImgs: root.querySelectorAll('img').length,\n"
             "    total: imgs.length,\n"
             "    loaded: imgs.filter(i => i.complete && i.naturalWidth > 0).length,\n"
             "    broken: imgs.filter(i => i.complete && i.naturalWidth === 0 && (i.currentSrc || i.getAttribute('src') || i.getAttribute('srcset'))).length,\n"
             "    offscreen: imgs.filter(i => { const r = i.getBoundingClientRect(); return r.bottom <= 0 || r.top >= window.innerHeight; }).length,\n"
             "    ids: imgs.map(i => i.id).sort(),\n"
             "  };\n"
             "}").replace("__FN__", fn)
        )
        results["counts"] = counts
        results["legacyCounts"] = page.evaluate(
            """() => {
              const imgs = Array.from(document.body.querySelectorAll('img'));
              return { total: imgs.length, loaded: imgs.filter(i => i.complete && i.naturalWidth > 0).length };
            }"""
        )
        browser.close()

    legacy = results["legacyCounts"]
    for name in EXPECT_OFFSCREEN:
        if name not in counts["ids"]:
            results["problems"].append(f"{name}: 视口外节点被错误排除，懒加载滚动分支永远不会触发")
    if counts.get("offscreen", 0) < 1:
        results["problems"].append("offscreen 计数为 0，滚动探测分支不会触发")
    if counts["broken"] < 1:
        results["problems"].append(f"可见破图未被计入 broken（broken={counts['broken']}）")
    if counts["loaded"] + counts["broken"] >= counts["total"]:
        results["problems"].append(
            f"可见未完成图片被提前算作完成（loaded={counts['loaded']} broken={counts['broken']} total={counts['total']}）"
        )
    if legacy["total"] <= counts["total"]:
        results["problems"].append(
            f"修复前口径与修复后没有差异（legacy={legacy['total']} counted={counts['total']}）：用例没有区分力"
        )

    results["verdict"] = "PASS" if not results["problems"] else "FAIL"
    REPORT.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    print("\n--- A: 不应误报（期望 visible=False） ---")
    for k, v in results["hidden"].items():
        print(f"  {'OK ' if v is False else 'BAD'} {k:<24} = {v}")
    print("\n--- B/D: 应保留统计（期望 visible=True） ---")
    for k, v in results["visible"].items():
        print(f"  {'OK ' if v is True else 'BAD'} {k:<24} = {v}")
    print("\n--- C: 统计口径 ---")
    print(f"  修复前：raw img={legacy['total']} loaded={legacy['loaded']}  => 隐藏/无来源一律算成未加载")
    print(f"  修复后：计数 img={counts['total']}（原始 {counts['rawImgs']}）"
          f"loaded={counts['loaded']} broken={counts['broken']} offscreen={counts['offscreen']}")
    print(f"  被统计节点：{counts['ids']}")
    print(f"\nHA-01 行为验证判定: {results['verdict']}")
    for p in results["problems"]:
        print("  - " + p)
    return 0 if results["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
