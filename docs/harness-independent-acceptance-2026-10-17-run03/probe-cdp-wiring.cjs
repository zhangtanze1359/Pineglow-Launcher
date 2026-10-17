'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { chromium } = require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const dir = __dirname;
const ctx = JSON.parse(fs.readFileSync(path.join(dir, 'context.json'), 'utf8').replace(/^\uFEFF/, ''));
const file = path.join(ctx.sourceRoot, 'scripts/verify-ui-pages-live.cjs');
const source = fs.readFileSync(file, 'utf8');
const mod = require(file);
const evaluateSource = source.match(/const evaluate = (async \(expression\) => \{[\s\S]*?\n    \});/)[1];
function expression(name) {
  const raw = source.match(new RegExp('const ' + name + ' = `([\\s\\S]*?)`;'))[1];
  return raw.replaceAll('${view}', 'fixture').replaceAll('${IMG_VISIBLE_FN}', mod.IMG_VISIBLE_FN).replaceAll('${LAZY_SCROLL_FN}', mod.LAZY_SCROLL_FN).replaceAll('${LAZY_RESTORE_FN}', mod.LAZY_RESTORE_FN);
}
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==', 'base64');
let requests = 0;
const server = http.createServer((req, res) => {
  if (req.url.startsWith('/lazy.png')) {
    requests++;
    res.writeHead(200, {'Content-Type':'image/png', 'Cache-Control':'no-store'});
    res.end(png);
  } else {
    res.writeHead(200, {'Content-Type':'text/html; charset=utf-8'});
    res.end(`<!doctype html><style>html,body{height:100%;overflow:hidden;margin:0}#panel{height:200px;overflow:auto;border:2px solid black}</style>
      <div id="panel"><div id="view-fixture"><p>Exact CDP wrapper integration</p><div style="height:12000px"></div><img id="lazy" loading="lazy" src="/lazy.png" width="64" height="64"></div></div>`);
  }
});
let browser;
const report = {head:ctx.head, uses:['official evaluate wrapper via eval', 'official listOffscreenExpr', 'official scrollOneExpr', 'actual Runtime.evaluate over Chromium CDP', 'official LAZY_RESTORE_FN'], counterexamples:[]};
async function main() {
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base='http://127.0.0.1:'+server.address().port;
  browser=await chromium.launch({channel:'msedge',headless:true});
  const page=await browser.newPage({viewport:{width:800,height:600}});
  await page.goto(base,{waitUntil:'domcontentloaded'});
  await page.waitForTimeout(500);
  const cdp=await page.context().newCDPSession(page);
  // This is the exact local wrapper; its closure uses the real CDP session above.
  const evaluate=eval('('+evaluateSource+')');
  const probe=mod.buildImgProbeExpr('fixture');
  const before=await evaluate(probe);
  const requestsBefore=requests;
  const targets=await evaluate(expression('listOffscreenExpr'));
  const results=[];
  for(const target of targets || []) {
    const res=await evaluate(expression('scrollOneExpr'),target);
    results.push({valueType:typeof res,value:res===undefined?null:res});
    await page.waitForTimeout(500);
  }
  const after=await evaluate(probe);
  const geometry=await evaluate('({scrollTop:document.getElementById("panel").scrollTop,lazyTop:document.getElementById("lazy").getBoundingClientRect().top,windowY:scrollY})');
  const requestsAfter=requests;
  await page.screenshot({path:path.join(dir,'cdp-wrapper-no-scroll.png')});
  // A control calls the same helper explicitly in the browser context, using a live node.
  const control=await evaluate('('+mod.LAZY_SCROLL_FN+')(document.getElementById("lazy"))');
  await page.waitForFunction(()=>document.getElementById('lazy').naturalWidth>0,{timeout:5000});
  const afterControl=await evaluate(probe);
  report.counterexamples.push({name:'main_cdp_does_not_invoke_scroll_helper',before,targets,results,after,geometry,requestsBefore,requestsAfter,control,afterControl,confirmed:targets.length>0&&results.every(r=>r.valueType==='undefined')&&geometry.scrollTop===0&&requestsBefore===requestsAfter&&after.loaded===0&&afterControl.loaded===1});
  await page.screenshot({path:path.join(dir,'cdp-explicit-invocation-control.png')});

  await page.evaluate(()=>{document.getElementById('panel').scrollTop=137;});
  const original=await evaluate('({top:document.getElementById("panel").scrollTop,left:document.getElementById("panel").scrollLeft,windowY:scrollY})');
  await evaluate('('+mod.LAZY_SCROLL_FN+')(document.getElementById("lazy"))');
  const restoredContainers=await evaluate(expression('runLazyRestoreExpr'));
  const restored=await evaluate('({top:document.getElementById("panel").scrollTop,left:document.getElementById("panel").scrollLeft,windowY:scrollY})');
  report.counterexamples.push({name:'restore_zeros_instead_of_original_scroll',original,restoredContainers,restored,confirmed:original.top===137&&restored.top===0});
  report.verdict=report.counterexamples.every(r=>r.confirmed)?'COUNTEREXAMPLES_CONFIRMED':'INCONCLUSIVE';
}
main().catch(e=>{report.error=e.stack;report.verdict='PROBE_ERROR';process.exitCode=1;}).finally(async()=>{
  if(browser)await browser.close();
  await new Promise(resolve=>server.close(resolve));
  fs.writeFileSync(path.join(dir,'cdp-wiring.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify(report,null,2));
});
