import base64, json, pathlib, re, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from playwright.sync_api import sync_playwright

root = pathlib.Path(__file__).resolve().parent
ctx = json.loads((root / 'context.json').read_text(encoding='utf-8-sig'))
src = (pathlib.Path(ctx['sourceRoot']) / 'scripts/verify-ui-pages-live.cjs').read_text(encoding='utf-8-sig')
fn = re.search(r'const IMG_VISIBLE_FN = `(.*?)`;', src, re.S).group(1)
probe = re.search(r'const imgProbeExpr = `(.*?)`;', src, re.S).group(1)
probe = probe.replace('${view}', 'content').replace('${IMG_VISIBLE_FN}', fn)
pending = re.search(r'const imagesPending = imgStats && \((.*?)\n      \);', src, re.S).group(1)
scroll = re.search(r'await evaluate\(`(\(\(\) => \{\n            const root = document.getElementById\(\x27view-\$\{view\}\x27\);.*?window.scrollTo\(0, 0\);\n          \}\)\(\))`\);', src, re.S).group(1)
scroll = scroll.replace('${view}', 'content')
png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==')
held = threading.Event()
hits = []
pages = {
    '/aria': '''<!doctype html><style>img{margin:12px;border:2px solid #999}</style><div id="view-content">
      <p>Visible broken images with accessibility / type metadata</p>
      <img id="decorative-broken" aria-hidden="true" src="/bad-a.png" width="80" height="80">
      <img id="svg-typed-broken" type="image/svg+xml" src="/bad-b.png" width="80" height="80">
      <img id="good" src="/good.png" width="80" height="80">
      <div hidden><img id="actually-hidden" src="/bad-c.png" width="80" height="80"></div>
      <img id="upload-placeholder" hidden width="80" height="80"></div>''',
    '/lazy': '''<!doctype html><style>html,body{margin:0;height:100%;overflow:hidden}
       #scroller{height:200px;overflow-y:auto;border:2px solid black}</style>
       <div id="scroller"><div id="view-content"><p>Nested scroll container</p>
       <div style="height:12000px"></div><img id="lazy" src="/lazy.png" loading="lazy" width="60" height="60"></div></div>''',
    '/pending': '''<!doctype html><div id="view-content"><img id="pending" src="/hold.png" width="80" height="80"></div>'''
}

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        hits.append(self.path)
        if self.path in pages:
            data, status, mime = pages[self.path].encode(), 200, 'text/html; charset=utf-8'
        elif self.path == '/hold.png':
            held.wait(15)
            data, status, mime = png, 200, 'image/png'
        elif self.path in ('/good.png', '/lazy.png'):
            data, status, mime = png, 200, 'image/png'
        else:
            data, status, mime = b'not found', 404, 'text/plain'
        self.send_response(status)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, *args):
        pass

server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
thread = threading.Thread(target=server.serve_forever, daemon=True)
thread.start()
base = 'http://127.0.0.1:' + str(server.server_port)
report = {'sourceHead': ctx['head'], 'uses': ['official IMG_VISIBLE_FN', 'official imgProbeExpr', 'official imagesPending expression', 'official scroll expression'], 'counterexamples': [], 'controls': []}

def stats(page):
    state = page.evaluate(probe)
    need_failure = page.evaluate('(imgStats)=>!!(' + pending + ')', state)
    return {'stats': state, 'wouldFlagImages': need_failure}

try:
    with sync_playwright() as p:
        browser = p.chromium.launch(channel='msedge', headless=True)
        page = browser.new_page(viewport={'width': 800, 'height': 600})
        page.goto(base + '/aria', wait_until='networkidle')
        first = stats(page)
        first['nodes'] = page.locator('img').evaluate_all('els=>els.map(i=>({id:i.id,complete:i.complete,naturalWidth:i.naturalWidth,rectWidth:i.getBoundingClientRect().width,rectHeight:i.getBoundingClientRect().height,hidden:i.hidden,ariaHidden:i.getAttribute("aria-hidden"),type:i.getAttribute("type")}))')
        first['actualVisibleBroken'] = sum(n['id'] in ('decorative-broken', 'svg-typed-broken') and n['complete'] and n['naturalWidth']==0 and n['rectWidth']>0 and n['rectHeight']>0 for n in first['nodes'])
        first['confirmed'] = first['actualVisibleBroken']==2 and not first['wouldFlagImages']
        report['counterexamples'].append({'name': 'visible_broken_excluded_by_aria_or_type', **first})
        page.screenshot(path=str(root/'visible-broken-omitted.png'))

        page.goto(base+'/lazy', wait_until='domcontentloaded')
        page.wait_for_timeout(500)
        before = stats(page)
        before_hits = hits.count('/lazy.png')
        page.evaluate(scroll)
        page.wait_for_timeout(1500)
        after = stats(page)
        positions = page.evaluate('({windowY:scrollY,nestedTop:document.getElementById("scroller").scrollTop,lazyTop:document.getElementById("lazy").getBoundingClientRect().top})')
        after_hits = hits.count('/lazy.png')
        page.screenshot(path=str(root/'lazy-after-official-scroll.png'))
        page.locator('#lazy').scroll_into_view_if_needed()
        page.wait_for_function('document.getElementById("lazy").naturalWidth>0', timeout=5000)
        real_scroll = stats(page)
        report['counterexamples'].append({'name':'nested_lazy_scroll_not_exercised', 'before':before,'afterOfficialScroll':after,'positions':positions,'requestsBefore':before_hits,'requestsAfterOfficialScroll':after_hits,'afterActualElementScroll':real_scroll,'confirmed':before['stats']['offscreen']>0 and after_hits==before_hits and positions['nestedTop']==0 and after['stats']['loaded']==0 and real_scroll['stats']['loaded']==1})
        page.screenshot(path=str(root/'lazy-after-real-element-scroll.png'))

        page.goto(base+'/pending', wait_until='domcontentloaded')
        page.wait_for_timeout(200)
        waiting = stats(page)
        held.set()
        page.wait_for_function('document.getElementById("pending").naturalWidth>0')
        loaded = stats(page)
        report['controls'].append({'name':'real_pending_then_loaded', 'pending':waiting,'loaded':loaded,'ok':waiting['wouldFlagImages'] and not loaded['wouldFlagImages']})
        browser.close()
finally:
    held.set()
    server.shutdown()
    server.server_close()

report['verdict'] = 'COUNTEREXAMPLES_CONFIRMED' if all(r['confirmed'] for r in report['counterexamples']) and all(r['ok'] for r in report['controls']) else 'INCONCLUSIVE'
(root/'boundary-probes.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
