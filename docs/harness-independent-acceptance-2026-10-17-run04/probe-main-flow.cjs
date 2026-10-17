'use strict';
const fs=require('fs'),path=require('path'),http=require('http');
const {chromium}=require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const dir=__dirname,ctx=JSON.parse(fs.readFileSync(path.join(dir,'context.json'),'utf8').replace(/^\uFEFF/,''));
const file=path.join(ctx.sourceRoot,'scripts/verify-ui-pages-live.cjs'),source=fs.readFileSync(file,'utf8'),mod=require(file);
const evaluateSource=source.match(/const evaluate = (async \(expression\) => \{[\s\S]*?\n    \});/)[1];
const start=source.indexOf('      const lazyExprs = buildLazyExprs(view);');
const block=source.slice(start,source.indexOf('      const imagesMs =',start));
if(!block.includes('report.lazyScroll.push'))throw Error('Source extraction failed');
fs.writeFileSync(path.join(dir,'main-block-under-test.txt'),block);
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==','base64');
const hits=[];
const server=http.createServer((req,res)=>{
 if(req.url.startsWith('/img')){hits.push(req.url);res.writeHead(200,{'Content-Type':'image/png','Cache-Control':'no-store'});res.end(png);return;}
 const deferred=req.url.includes('deferred');
 res.writeHead(200,{'Content-Type':'text/html'});
 res.end(`<!doctype html><style>html,body{height:100%;overflow:hidden;margin:0}#panel{height:240px;overflow:auto;border:2px solid black}img{width:48px;height:48px}.gap{height:12000px}</style>
 <div id="panel"><div id="view-fixture"><img src="/img?top=${req.url}"><div class="gap"></div>
 <img id="a" ${deferred?'data-defer-src':'loading="lazy" src'}="/img?a=${req.url}"><div class="gap"></div>
 <img id="b" ${deferred?'data-defer-src':'loading="lazy" src'}="/img?b=${req.url}"><div style="height:500px"></div></div></div>
 <script>const io=new IntersectionObserver(es=>es.forEach(e=>{if(e.isIntersecting&&e.target.dataset.deferSrc){e.target.src=e.target.dataset.deferSrc;io.unobserve(e.target)}}),{root:document.getElementById('panel')});document.querySelectorAll('[data-defer-src]').forEach(i=>io.observe(i));</script>`);
});
let browser;
const output={head:ctx.head,uses:['exact official main branch incl guard, loop, catch and restore','exact official evaluate wrapper','actual Edge Runtime.evaluate CDP','local HTTP native-lazy and IntersectionObserver fixtures'],cases:[]};
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function runCase(base,name,fault=false){
 const page=await browser.newPage({viewport:{width:800,height:600}});
 try{
  await page.goto(base+'/'+name,{waitUntil:'load'});await sleep(500);
  const real=await page.context().newCDPSession(page);
  const view='fixture',imgProbeExpr=mod.buildImgProbeExpr(view),buildLazyExprs=mod.buildLazyExprs;
  let injected=false,scrolled=false;
  const cdp={send:async(method,params)=>{
   if(fault&&scrolled&&!injected&&params.expression===imgProbeExpr){injected=true;throw Error('OWNED_FIXTURE_CDP_TRANSIENT_ERROR');}
   const r=await real.send(method,params);
   if(r?.result?.value?.scrolled)scrolled=true;
   return r;
  }};
  const evaluate=eval('('+evaluateSource+')');
  if(fault)await evaluate('document.getElementById("panel").scrollTop=137');
  let imgStats=await evaluate(imgProbeExpr);
  const e=buildLazyExprs(view),saved=await evaluate(e.save);
  const before={stats:imgStats,count:await evaluate(e.count),top:await evaluate('document.getElementById("panel").scrollTop')};
  const report={problems:[]},hitStart=hits.length;
  await eval('(async()=>{'+block+'})()');
  await sleep(250);
  const after={stats:await evaluate(imgProbeExpr),count:await evaluate(e.count),top:await evaluate('document.getElementById("panel").scrollTop'),images:await evaluate('Array.from(document.querySelectorAll("#a,#b")).map(i=>({id:i.id,src:i.getAttribute("src"),natural:i.naturalWidth,top:i.getBoundingClientRect().top}))'),hits:hits.slice(hitStart),report,injected};
  await page.screenshot({path:path.join(dir,name+'-after-main.png')});
  const record={name,before,after};
  // Positive control on the same fixture: explicitly target the remaining node in-page.
  if(!fault){
   await evaluate('document.getElementById("b").scrollIntoView({block:"nearest"})');
   await page.waitForFunction(()=>document.getElementById('b').naturalWidth>0,{timeout:5000});
   record.control={bLoaded:await evaluate('document.getElementById("b").naturalWidth'),hits:hits.slice(hitStart)};
  }
  await evaluate(e.restore(JSON.stringify(saved)));
  record.cleanupTop=await evaluate('document.getElementById("panel").scrollTop');
  if(fault)record.confirmed=injected&&before.top===137&&after.top!==137&&after.report.problems.length===1&&record.cleanupTop===137;
  else if(name.includes('deferred'))record.confirmed=before.count===2&&before.stats.offscreen===0&&!after.report.lazyScroll&&after.images.every(i=>!i.src)&&record.control.bLoaded>0;
  else record.confirmed=before.count===2&&before.stats.offscreen===2&&after.images[0].natural>0&&after.images[1].natural===0&&after.report.lazyScroll[0].scrolledInView===1&&record.control.bLoaded>0;
  output.cases.push(record);
 }finally{await page.close();}
}
async function main(){
 await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});
 const base='http://127.0.0.1:'+server.address().port;
 await runCase(base,'native-two');
 await runCase(base,'deferred-only');
 await runCase(base,'native-fault',true);
 output.verdict=output.cases.every(c=>c.confirmed)?'COUNTEREXAMPLES_CONFIRMED':'INCONCLUSIVE';
 if(output.verdict==='INCONCLUSIVE')process.exitCode=2;
}
main().catch(e=>{output.error=e.stack;output.verdict='PROBE_ERROR';process.exitCode=1}).finally(async()=>{
 if(browser)await browser.close();
 await new Promise(r=>server.close(r));
 fs.writeFileSync(path.join(dir,'main-flow-probe.json'),JSON.stringify(output,null,2));
 console.log(JSON.stringify(output,null,2));
});
