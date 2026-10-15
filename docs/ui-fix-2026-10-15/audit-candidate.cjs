// Read-only audit harness. Static UI plus controlled API fixtures; no product backend.
const fs=require('fs'),path=require('path'),http=require('http');
const {chromium}=require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root=path.resolve(__dirname,'../../ui'),out=path.join(__dirname,'candidate');fs.mkdirSync(out,{recursive:true});
const original=fs.readFileSync(path.join(__dirname,'../ui-recheck-2026-10-15/design/pineglow-launcher.html'),'utf8');
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
 const requests=[],errors=[],checks=[];let recoveryFail=false;
 const check=(name,condition)=>{checks.push({name,status:condition?'PASS':'FAIL'});fs.writeFileSync(path.join(out,'behavior-progress.json'),JSON.stringify(checks,null,2));if(!condition)throw Error(name);};
 let fixtureMods=(DB.mods[instances[0].id]||[]).map(m=>({...m,fileName:m.file+(m.enabled?'':'.disabled'),displayName:m.name,isEnabled:m.enabled,sizeText:m.size})),fixtureRecoveries=[];
 const prod=await context.newPage();prod.on('pageerror',e=>errors.push(e.message));
 await prod.route('**/api/**',async route=>{
  const req=route.request(),u=new URL(req.url()),p=u.pathname;requests.push({method:req.method(),path:p,body:req.postData()});
  if(req.method()!=='GET'){
   if(p==='/api/config'){Object.assign(config,JSON.parse(req.postData()||'{}'));await route.fulfill({json:{ok:true}});return;}
   const parts=p.split('/').map(decodeURIComponent),file=parts.at(-2);
   if(p.endsWith('/toggle')){const m=fixtureMods.find(x=>x.fileName===file);if(m){m.isEnabled=!m.isEnabled;m.fileName=m.isEnabled?m.fileName.replace(/\.disabled$/,''):m.fileName+'.disabled';await route.fulfill({json:{ok:true,fileName:m.fileName,isEnabled:m.isEnabled}});return;}}
   if(p.endsWith('/delete')){const m=fixtureMods.find(x=>x.fileName===file);if(m){fixtureMods=fixtureMods.filter(x=>x!==m);fixtureRecoveries.push({operationId:'audit-recovery',reason:'manual',status:'quarantined',createdAt:'2026-10-15T04:00:00Z',totalBytes:100,files:[{relativePath:m.fileName,state:'quarantined',sizeBytes:100}],mod:m});await route.fulfill({json:{ok:true,recoverable:true}});return;}}
   if(p.endsWith('/restore')){const rec=fixtureRecoveries.find(x=>x.operationId===file);if(rec){rec.status='restored';fixtureMods.push({...rec.mod,isEnabled:false,fileName:rec.mod.fileName.replace(/\.disabled$/,'')+'.disabled'});await route.fulfill({json:{ok:true,status:'restored',conflicts:0}});return;}}
   await route.fulfill({status:409,json:{error:'UNEXPECTED_AUDIT_WRITE'}});return;
  }
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
  else if(p.endsWith('/mods'))body={mods:fixtureMods};
  else if(p.endsWith('/recovery')){if(recoveryFail){recoveryFail=false;await route.fulfill({status:503,json:{error:'Controlled audit recovery failure'}});return;}body={retentionDays:30,recoveries:fixtureRecoveries};}
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
   const reference=await inspect(ref),product=await inspect(prod);metrics.push({width,name,reference,product});
   check(name+' '+width+' only visible page, no horizontal overflow',await prod.evaluate(()=>{const p=document.querySelector('#contentPanel');return [...document.querySelectorAll('.view')].filter(e=>!e.hidden).length===1&&p.scrollWidth-p.clientWidth<=1;}));
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
 
 check('Range summary uses the selected day and unique sessions',JSON.stringify(await prod.evaluate(()=>[]))!=='' && after.includes('4 小时')&&!after.includes('214')&&cells===4);
 check('Recovery retry stays in the same full height drawer',recoveryBefore.drawer==='true'&&recoveryAfter.drawer==='true'&&recoveryAfter.rect.height===800);
 check('Skip link focuses stable visible main content',skip.active==='contentPanel'&&skip.targetVisible);
 check('Native range supports keyboard increments',enabledSlider.active==='memSlider'&&enabledSlider.value==='4.5 GB');
 await prod.locator('#modsListTab').click();await prod.locator('#modSearch').fill('Sodium');await prod.locator('[data-mod-filter="enabled"]').click();
 check('Mod name search applies to enabled filter',await prod.locator('.mod-row').count()===1);
 await prod.locator('#modsRecovery').click();await prod.locator('#modsListTab').click();
 check('Returning from recovery preserves mod search and filter',await prod.locator('#modSearch').inputValue()==='Sodium'&&await prod.locator('[data-mod-filter="enabled"]').getAttribute('aria-pressed')==='true');
 await prod.locator('[data-mod-filter="all"]').click();const modSwitch=prod.locator('.mod-row [role="switch"]');
 await modSwitch.click();await prod.waitForFunction(()=>document.querySelector('.mod-row [role="switch"]').getAttribute('aria-checked')==='false');
 check('Mod disable updates actual file identity in place',await prod.locator('.mod-row-name').innerText().then(x=>x.endsWith('.jar.disabled')));
 await modSwitch.click();await prod.waitForFunction(()=>document.querySelector('.mod-row [role="switch"]').getAttribute('aria-checked')==='true');
 await prod.locator('[data-del]').click();check('Inline remove initially focuses cancel',await prod.locator('[data-cancel-remove]').evaluate(e=>e===document.activeElement));
 await prod.locator('[data-cancel-remove]').click();check('Cancel does not delete a mod',fixtureMods.length===12);
 await prod.locator('[data-del]').click();await prod.locator('[data-remove-file]').click();await prod.waitForFunction(()=>document.querySelector('[data-mod-count]').textContent==='11');
 await prod.locator('#modsRecovery').click();await prod.locator('.rec-restore').click();await prod.waitForFunction(()=>document.querySelector('[data-mod-count]').textContent==='12');
 check('Restore remains in recovery tab with drawer and updates counts',await prod.locator('#modalHost').getAttribute('data-drawer')==='true'&&await prod.locator('#modsRecovery').getAttribute('aria-pressed')==='true');
 await prod.locator('#modsListTab').click();check('Restored mod retains disabled state and search',await prod.locator('#modSearch').inputValue()==='Sodium'&&await prod.locator('.mod-row [role="switch"]').getAttribute('aria-checked')==='false');
 await prod.locator('#mCancel').click();await prod.locator('[data-nav="installations"]').click();await prod.locator('[data-filter="snap"]').click();check('Snapshot filter recognizes snapshot version',await prod.locator('.inst-card:visible').count()===1&&await prod.locator('.inst-card:visible').getAttribute('data-kind')==='snap');
 await prod.locator('[data-filter="all"]').click();await prod.locator('#instanceSearch').fill('机械');check('Instance search filters rows',await prod.locator('.inst-card:visible').count()===1);
 await prod.locator('[data-nav="worlds"]').click();await prod.locator('[data-world-manage]').first().focus();await prod.locator('[data-world-manage]').first().press('Enter');await prod.locator('#waClose').waitFor();check('World management can open from keyboard',await prod.locator('#modalHost').isVisible());await prod.locator('#waClose').click();
 await prod.locator('[data-nav="skins"]').click();const writesBefore=requests.filter(x=>x.method!=='GET').length;
 const chooser=prod.waitForEvent('filechooser');await prod.locator('#btnUploadSkin').click();await(await chooser).setFiles(path.join(__dirname,'../ui-integration-2026-10-15/skin-fixture.png'));await prod.locator('#localSkinTexture').waitFor({state:'visible'});
 check('Local skin selection previews without any upload',requests.filter(x=>x.method!=='GET').length===writesBefore&&await prod.locator('#btnApplyLocalSkin').isDisabled());
 await prod.locator('[data-skin-model="slim"]').click();check('Slim model uses its own selected state',await prod.locator('[data-skin-model="slim"]').getAttribute('aria-pressed')==='true'&&await prod.locator('#skinModelSelect').inputValue()==='slim');
 await prod.screenshot({path:path.join(out,'screenshots/probe-skin-preview.png'),animations:'disabled'});
 await prod.locator('#btnLangEn').click();await prod.waitForFunction(()=>document.querySelector('#workspaceTitle').textContent==='Skins');check('English labels contain no untranslated layout keys',!(await prod.locator('#contentPanel').innerText()).includes('pt.'));
 await prod.locator('[data-nav="settings"]').click();await prod.emulateMedia({reducedMotion:'reduce'});check('Reduced motion disables page animation',await prod.locator('#view-settings').evaluate(e=>getComputedStyle(e).animationName==='none'));
 
 await prod.locator('#btnLangZh').click();await prod.setViewportSize({width:960,height:800});
 instances[0].name='很长的实例名称'.repeat(20);await prod.locator('[data-nav="home"]').click();await prod.locator('#btnRefreshView').click();await prod.waitForFunction(()=>document.querySelector('.home-game-title').textContent.length>100);
 const noOverflow=()=>prod.evaluate(()=>{const p=document.querySelector('#contentPanel');return p.scrollWidth<=p.clientWidth+1;});
 check('960px long instance name does not overflow home',await noOverflow());
 check('960px home launch buttons remain inside content',await prod.locator('.home-launch-controls').evaluate(e=>e.getBoundingClientRect().right<=innerWidth));
 await prod.locator('[data-nav="installations"]').click();check('960px long instance name does not overflow list',await noOverflow());
 DB.content.mods[0].name='ExtremelyLongModTitle'.repeat(20);await prod.locator('[data-nav="content"]').click();await prod.locator('#contentSearch').fill('stress');await prod.waitForFunction(()=>document.querySelector('.mod-name')?.textContent.length>100);
 check('960px long project titles stay inside cards',await noOverflow());
 check('960px project action stays aligned inside its own card',await prod.locator('.mod-install').evaluateAll(es=>es.every(e=>{const a=e.getBoundingClientRect(),b=e.closest('.mod-card').getBoundingClientRect();return a.right<=b.right&&a.left>=b.left;})));
 check('960px content categories and source share a toolbar row',await prod.evaluate(()=>{const a=document.querySelector('.content-tabs').getBoundingClientRect(),b=document.querySelector('.content-context').getBoundingClientRect();return Math.abs((a.top+a.height/2)-(b.top+b.height/2))<3;}));
 await prod.screenshot({path:path.join(out,'screenshots/probe-long-content-960.png'),animations:'disabled'});
 await prod.locator('[data-nav="playtime"]').click();await prod.locator('[data-pt-range="custom"]').click();const cellsBefore=await prod.locator('.tm-cell').count();const summaryBefore=await prod.locator('#ptSummary').innerText();await prod.locator('#ptFrom').fill('2026-10-15');await prod.locator('#ptTo').fill('2026-10-14');await prod.locator('[data-pt-apply]').click();
 check('Invalid custom dates show an explicit error',!!(await prod.locator('#ptError').textContent()));
 check('Invalid dates retain the last valid chart and summary',await prod.locator('.tm-cell').count()===cellsBefore&&await prod.locator('#ptSummary').innerText()===summaryBefore);
 await prod.locator('[data-pt-range="30"]').click();await prod.locator('.tm-cell').first().focus();check('Keyboard focus updates treemap readout',!!(await prod.locator('#ptReadout').textContent()).includes('2026-10-'));
 await prod.locator('.tm-cell').first().press('Enter');check('Keyboard treemap activation applies a date filter',await prod.locator('[data-pt-clear]').isVisible());
 check('No uncaught product JavaScript errors',errors.length===0);

 fs.writeFileSync(path.join(out,'behavior-checks.json'),JSON.stringify({kind:'Controlled API simulation, all writes in memory; not backend proof',checks},null,2));
 fs.writeFileSync(path.join(out,'metrics.json'),JSON.stringify(metrics,null,2));
 fs.writeFileSync(path.join(out,'probes.json'),JSON.stringify({stats:{before,after,cells,range:'2026-10-15',expectedRangeSeconds:14400,expectedRangeCount:4},slider:{before:sliderBefore,after:sliderAfter,enabled:enabledSlider},skip,mods:{modList,recoveryBefore,recoveryAfter},rawReference,errors,requests},null,2));
 console.log(JSON.stringify({screenshots:60,statsUnchanged:before===after,cells,enabledSlider,skip,recoveryAfter,errors}));
 }finally{await browser.close();server.close();}
})().catch(e=>{fs.writeFileSync(path.join(__dirname,'candidate/fatal.txt'),e.stack);console.error(e);server.close();process.exitCode=1;});
