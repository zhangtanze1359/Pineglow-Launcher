import json, time, base64, urllib.request
from websocket import create_connection
page = next(t for t in json.load(urllib.request.urlopen("http://127.0.0.1:9333/json")) if t.get("type")=="page")
ws = create_connection(page["webSocketDebuggerUrl"], max_size=None, suppress_origin=True)
_id=0
def cmd(m,p=None):
    global _id;_id+=1
    ws.send(json.dumps({"id":_id,"method":m,"params":p or {}}))
    while True:
        r=json.loads(ws.recv())
        if r.get("id")==_id: return r
cmd("Page.enable"); cmd("Runtime.enable"); cmd("Network.enable")
cmd("Network.setCacheDisabled", {"cacheDisabled": True})
cmd("Page.reload", {"ignoreCache": True})
time.sleep(4)
def goto(nav):
    cmd("Runtime.evaluate", {"expression": f"location.hash='#{nav}'; SG && SG.goTo && SG.goTo('{nav}');"})
    time.sleep(1.8)
def shot(name):
    r = cmd("Page.captureScreenshot", {"format":"png"})
    d = r.get("result",{}).get("data")
    if d: open(f"dev/shots/verify_{name}.png","wb").write(base64.b64decode(d)); print("saved", name)
for nav in ["settings","downloads"]:
    goto(nav); shot(nav)
ws.close()
