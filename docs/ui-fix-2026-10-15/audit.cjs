// Read-only audit harness. Static UI plus controlled API fixtures; no product backend.
const fs=require('fs'),path=require('path'),http=require('http');
const {chromium}=require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root=path.resolve(__dirname,'../../ui'),out=__dirname;
const original=fs.readFileSync(path.join(out,'../ui-recheck-2026-10-15/design/pineglow-launcher.html'),'utf8');
const instrumented=original.replace('init();\n})();','init();\nwindow.__audit={DB,state,go,ACTIONS};\n})();');
if(instrumented===original)throw Error('reference instrumentation anchor missing');
const server=http.createServer((req,res)=>{
 const url=new URL(req.url,'http://localhost');
 if(url.pathname==='/reference'||url.pathname==='/reference-raw'){res.setHeader('Content-Type','text/html; charset=utf-8');res.end(url.pathname==='/reference'?instrumented:original);return;}
 const file=path.resolve(root,'.'+decodeURIComponent(url.pathname==='/'?'/index.html':url.pathname));
 if(!file.startsWith(root+path.sep)){res.writeHead(403);res.end();return;}
 try{res.setHeader('Content-Type',({'.html':'text/html; charset=utf-8','.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml','.png':'image/png'})[path.extname(file)]||'application/octet-stream');res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}
});
(async()=>{
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const base='http://127.0.0.1:'+server.address().port;
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
 const context=await browser.newContext({viewport:{width:1280,height:800},locale:'zh-CN',timezoneId:'Asia/Hong_Kong'});
 const ref=await context.newPage();await ref.goto(base+'/reference');await ref.waitForFunction(()=>window.__audit);
 const DB=await ref.evaluate(()=>__audit.DB);fs.writeFileSync(path.join(out,'prototype-data.json'),JSON.stringify(DB,null,2));
 const instances=DB.instances.map((i,n)=>({id:i.id,name:i.name,minecraftVersion:i.ver,loaderType:i.loader.startsWith('Fabric')?'Fabric':i.loader.startsWith('Forge')?'Forge':'Vanilla',gameDirectory:i.dir,lastLaunchTime:'2026-10-15T03:00:00Z',totalPlaySeconds:i.hours*3600,playCount:i.sessions,modsCount:i.mods,savesCount:i.saves}));
 const sessions=instances.flatMap((i,n)=>[10,15].map((d,k)=>({id:i.id+'-'+d,instanceId:i.id,instanceName:i.name,startedAt:`2026-10-${d}T0${n}:00:00+08:00`,endedAt:`2026-10-${d}T0${n+1}:00:00+08:00`,durationSeconds:3600})));
 const config={gameDirectory:'D:\\AuditFixtures',playerName:'Player',selectedInstanceId:instances[0].id,selectedVersion:'1.21.5',maxMemoryMb:4096,useAutoMemory:true,accountMode:'offline',useMirror:true};
 const requests=[],errors=[];let recoveryFail=false;
 const prod=await context.newPage();prod.on('pageerror',e=>errors.push(e.message));
 await prod.route('**/api/**',async route=>{
  const req=route.request(),u=new URL(req.url()),p=u.pathname;requests.push({method:req.method(),path:p,body:req.postData()});
  if(req.method()!=='GET'){await route.fulfill({status:409,json:{error:'READ_ONLY_AUDIT'}});return;}
  let body={};
  if(p==='/api/state')body={config,instances,accounts:[],java:[]};
  else if(p==='/api/instances')body={instances,selectedInstanceId:instances[0].id};
  else if(p==='/api/stats')body={totalPlaySeconds:214*3600,totalPlayCount:194,totalInstances:4,sessions,perInstance:instances};
  else if(p==='/api/accounts')body={accounts:[]};
  else if(p==='/api/worlds')body={worlds:DB.worlds.map(w=>({...w,displayName:w.name,folderName:w.name,instanceId:DB.instances.find(i=>i.name===w.inst)?.id,instanceName:w.inst,sizeText:w.size,lastPlayed:'2026-10-15T03:00:00Z'}))};
  else if(p==='/api/servers')body={servers:DB.servers.map(s=>({...s,onlinePlayers:s.online?s.players:-1,maxPlayers:s.cap,versionName:s.ver,latencyMs:s.ping}))};
  else if(p==='/api/downloads')body={tasks:DB.tasks.map(t=>({...t,status:t.state,progress:(t.pct||0)/100}))};
  else if(p==='/api/versions')body={local:instances.map(i=>i.minecraftVersion)};
  else if(p==='/api/versions/remote')body={versions:DB.versions};
  else if(p==='/api/content/search')body={results:DB.content.mods.map(c=>({...c,title:c.name,versions:c.tags.slice(0,1),loader:c.tags[1]||'',downloads:100000,category:'mod',source:'modrinth'}))};
  else if(p.includes('/content/icon')){await route.fulfill({status:404,body:''});return;}
  else if(p==='/api/news')body={items:DB.news.map(n=>({...n,category:n.cat,text:n.body,date:'2026-10-12'})),meta:{state:'live',validatedAt:'2026-10-15T04:00:00Z',source:'Controlled audit fixture'}};
  else if(p.endsWith('/mods'))body={mods:(DB.mods[instances[0].id]||[]).map(m=>({...m,fileName:m.file||m.fileName||m.name+'.jar',displayName:m.name,isEnabled:m.enabled,sizeText:m.size}))};
  else if(p.endsWith('/recovery')){if(recoveryFail){recoveryFail=false;await route.fulfill({status:503,json:{error:'Controlled audit recovery failure'}});return;}body={retentionDays:30,recoveries:[]};}
  else if(p==='/api/java')body={installations:[]};
  else if(p==='/api/sources')body={selected:'official',sources:[{key:'official',name:'官方',available:true}]};
  await route.fulfill({json:body});
 });
 await prod.goto(base);await prod.waitForSelector('[data-nav="home"].active');
 await prod.waitForTimeout(300);
 const pages=['home','installations','worlds','servers','stats','content','skins','downloads','news','settings'],metrics=[];
 const inspect=page=>page.evaluate(()=>{
  const sels=['.sidebar','.account-panel','.account-btn','.profile','.art-strip','.home-hero-art','[data-od-id="launch-card"]','.home-hero','.btn-lg','.home-play-btn','.content-grid','.grid-3','.settings-view','.grid-2','.pt-grid','.stats-layout','.skins-layout','.skins-view'];
  return {text:document.body.innerText,overflow:document.documentElement.scrollWidth>innerWidth,nodes:sels.flatMap(s=>[...document.querySelectorAll(s)].filter(e=>e.getClientRects().length).map(e=>{const r=e.getBoundingClientRect(),c=getComputedStyle(e);return {s,x:r.x,y:r.y,w:r.width,h:r.height,grid:c.gridTemplateColumns,font:c.font,fontWeight:c.fontWeight,borderRadius:c.borderRadius};}))};
 });
 fs.mkdirSync(path.join(out,'screenshots'),{recursive:true});
 for(const width of [1280,960,1600]){
  for(const page of [ref,prod])await page.setViewportSize({width,height:800});
  for(const name of pages){
   await ref.evaluate(n=>{__audit.go(n);document.activeElement.blur();},name);
   await prod.locator(`[data-nav="${name==='stats'?'playtime':name}"]`).click();
   await prod.waitForTimeout(180);await ref.waitForTimeout(80);
   await ref.screenshot({path:path.join(out,`screenshots/${width}-${name}-reference.png`),animations:'disabled'});
   await prod.screenshot({path:path.join(out,`screenshots/${width}-${name}-product.png`),animations:'disabled'});
   metrics.push({width,name,reference:await inspect(ref),product:await inspect(prod)});
  }
 }
 await prod.setViewportSize({width:1280,height:800});await prod.locator('[data-nav="playtime"]').click();await prod.waitForTimeout(100);
 const before=await prod.locator('.playtime-view .home-stats').innerText();
 await prod.locator('[data-pt-range="custom"]').click();await prod.locator('#ptFrom').fill('2026-10-15');await prod.locator('#ptTo').fill('2026-10-15');await prod.locator('[data-pt-apply]').click();
 const after=await prod.locator('.playtime-view .home-stats').innerText(),cells=await prod.locator('.tm-cell').count();
 await prod.screenshot({path:path.join(out,'screenshots/probe-stats-custom.png'),animations:'disabled'});
 await prod.locator('[data-nav="settings"]').click();await prod.waitForTimeout(80);
 const slider=prod.locator('#memSlider'),sliderBefore=await slider.evaluate(e=>({html:e.outerHTML,value:document.querySelector('#memValue').textContent}));
 await prod.waitForTimeout(10);
 const sliderAfter=await slider.evaluate(e=>({html:e.outerHTML,value:document.querySelector('#memValue').textContent}));
 await prod.locator('[data-toggle="autoMem"]').click();await slider.focus();await slider.press('ArrowRight');
 const enabledSlider=await slider.evaluate(e=>({active:document.activeElement.id,role:e.getAttribute('role'),tabindex:e.getAttribute('tabindex'),value:document.querySelector('#memValue').textContent}));
 await prod.locator('.skip-link').focus();await prod.locator('.skip-link').press('Enter');
 const skip=await prod.evaluate(()=>({hash:location.hash,active:document.activeElement.id,targetVisible:!!document.querySelector('#contentPanel').getClientRects().length,current:document.querySelector('.nav-item.active')?.dataset.nav}));
 await prod.locator('[data-nav="installations"]').click();await prod.waitForTimeout(100);
 fs.writeFileSync(path.join(out,'probe-installations.html'),await prod.locator('#view-installations').innerHTML());
 await prod.locator('[data-mods]').first().click();await prod.locator('#modSearch').waitFor();await prod.waitForTimeout(100);
 await prod.screenshot({path:path.join(out,'screenshots/probe-mod-list.png'),animations:'disabled'});
 await ref.setViewportSize({width:1280,height:800});await ref.evaluate(()=>{__audit.go('installations');__audit.ACTIONS['mods-open']({dataset:{id:'fabric-1215'}});document.activeElement.blur();});
 await ref.screenshot({path:path.join(out,'screenshots/probe-mod-list-reference.png'),animations:'disabled'});
 const modList=await prod.locator('#modalHost').innerText();
 recoveryFail=true;await prod.locator('#modsRecovery').click();await prod.locator('#rRetry').waitFor();
 const recoveryBefore=await prod.locator('#modalHost').evaluate(e=>({drawer:e.dataset.drawer||null,text:e.innerText,rect:e.firstElementChild.getBoundingClientRect().toJSON()}));
 await prod.screenshot({path:path.join(out,'screenshots/probe-recovery-before-retry.png'),animations:'disabled'});
 await prod.locator('#rRetry').click();await prod.waitForTimeout(120);
 const recoveryAfter=await prod.locator('#modalHost').evaluate(e=>({drawer:e.dataset.drawer||null,text:e.innerText,rect:e.firstElementChild.getBoundingClientRect().toJSON()}));
 await prod.screenshot({path:path.join(out,'screenshots/probe-recovery-after-retry.png'),animations:'disabled'});
 const raw=await context.newPage();await raw.goto(base+'/reference-raw');await raw.locator('#nav [data-view="stats"]').click();await raw.waitForTimeout(120);
 const rawReference={text:await raw.locator('#treemap').innerText(),cells:await raw.locator('#treemap .tm-cell').count()};
 await raw.screenshot({path:path.join(out,'screenshots/probe-raw-reference-stats.png'),animations:'disabled'});
 fs.writeFileSync(path.join(out,'metrics.json'),JSON.stringify(metrics,null,2));
 fs.writeFileSync(path.join(out,'probes.json'),JSON.stringify({stats:{before,after,cells,range:'2026-10-15',expectedRangeSeconds:14400,expectedRangeCount:4},slider:{before:sliderBefore,after:sliderAfter,enabled:enabledSlider},skip,mods:{modList,recoveryBefore,recoveryAfter},rawReference,errors,requests},null,2));
 console.log(JSON.stringify({screenshots:60,statsUnchanged:before===after,cells,enabledSlider,skip,recoveryAfter,errors}));
 }finally{await browser.close();server.close();}
})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
