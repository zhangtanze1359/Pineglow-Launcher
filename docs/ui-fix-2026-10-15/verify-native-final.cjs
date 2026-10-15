'use strict';
const fs=require('fs'),path=require('path'),assert=require('assert/strict');
const {chromium}=require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root=path.join(__dirname,'native-final-check'),report={kind:'Real WebView2 with live local API; viewport dimensions emulated by CDP',pages:[],errors:[],interactions:[]};
const save=()=>fs.writeFileSync(path.join(root,'webview-report.json'),JSON.stringify(report,null,2));
(async()=>{
 const browser=await chromium.connectOverCDP('http://127.0.0.1:'+(process.env.PINEGLOW_VERIFY_DEBUG||'51342'));
 const page=browser.contexts()[0].pages()[0];page.setDefaultTimeout(12000);
 page.on('pageerror',e=>{report.errors.push(e.message);save();});
 await page.evaluate(()=>SG.api.updateConfig({maxMemoryMb:4096,useAutoMemory:true}));await page.reload();await page.waitForFunction(()=>window.SG&&SG.__test__&&SG.__test__.getCurrentView());
 assert.equal(await page.evaluate(()=>SG.apiMode),'live');
 await page.locator('#btnLangZh').click();
 const pages=['home','installations','worlds','servers','playtime','content','skins','downloads','news','settings'];
 for(const size of [{width:1280,height:800},{width:960,height:640},{width:1600,height:900}]){
  await page.setViewportSize(size);
  for(const nav of pages){
   console.log(`Checking ${nav} at ${size.width}`);
   try {
    await page.locator(`[data-nav="${nav}"]`).click();
    await page.waitForFunction(n=>SG.__test__.getCurrentView()===n,nav,{timeout:60000});
    await page.locator('#view-'+nav).waitFor({state:'visible'});
    await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
    const metrics=await page.evaluate(n=>{
     const el=document.getElementById('view-'+n),p=document.getElementById('contentPanel');
     return {text:el.innerText.slice(0,180),chars:el.innerText.length,visibleViews:[...document.querySelectorAll('.view')].filter(x=>!x.hidden).map(x=>x.id),current:document.querySelector('[aria-current="page"]')?.dataset.nav,overflow:p.scrollWidth-p.clientWidth,title:document.getElementById('workspaceTitle').textContent};
    },nav);
    assert.ok(metrics.chars>5,nav+' empty');assert.deepEqual(metrics.visibleViews,['view-'+nav]);assert.equal(metrics.current,nav);assert.ok(metrics.overflow<=1,nav+' horizontal overflow '+metrics.overflow);
    await page.screenshot({animations:'disabled',path:path.join(root,`${nav}-${size.width}-live.png`)});
    report.pages.push({nav,...size,status:'PASS',...metrics});
   }catch(e){report.pages.push({nav,...size,status:'FAIL',error:e.message});}
   save();
  }
 }
 await page.setViewportSize({width:1280,height:800});
 await page.locator('[data-nav="home"]').click();
 await page.locator('#btnQuickOpen').click();await page.locator('#commandQuery').fill('游戏时长');
 await page.locator('#commandQuery').press('Enter');await page.waitForFunction(()=>SG.__test__.getCurrentView()==='playtime');
 report.interactions.push({name:'Quick actions search and Enter navigate',status:'PASS'});
 await page.waitForFunction(()=>document.getElementById('modalHost').hidden);
 await page.locator('#btnLangEn').click();await page.waitForFunction(()=>document.getElementById('workspaceTitle').textContent==='Play time');
 assert.equal(await page.locator('#btnLangEn').getAttribute('aria-pressed'),'true');
 report.interactions.push({name:'English rerenders the current page and pressed state',status:'PASS'});
 await page.locator('#btnLangZh').click();
 await page.locator('#btnQuickOpen').click();await page.locator('#commandQuery').press('Escape');
 await page.waitForFunction(()=>document.getElementById('modalHost').hidden);
 assert.equal(await page.evaluate(()=>document.activeElement.id),'btnQuickOpen');
 report.interactions.push({name:'Escape restores quick action trigger focus',status:'PASS'});
 
 await page.locator('[data-nav="playtime"]').click();await page.locator('[data-pt-range="custom"]').click();
 const today=new Date().toLocaleDateString('sv-SE',{timeZone:'Asia/Hong_Kong'});await page.locator('#ptFrom').fill(today);await page.locator('#ptTo').fill(today);await page.locator('[data-pt-apply]').click();
 assert.deepEqual(await page.locator('#ptSummary b').allTextContents(),['2 小时','2','1','1 小时']);report.interactions.push({name:'Actual persisted native fixture sessions produce correct single-day summary',status:'PASS'});
 await page.locator('[data-nav="settings"]').click();await page.locator('.skip-link').focus();await page.locator('.skip-link').press('Enter');assert.equal(await page.evaluate(()=>document.activeElement.id),'contentPanel');
 report.interactions.push({name:'Native keyboard skip focuses visible main content outside home',status:'PASS'});
 await page.locator('[data-toggle="autoMem"]').click();await page.waitForFunction(()=>!document.querySelector('[data-toggle="autoMem"]').disabled);await page.locator('#memSlider').focus();await page.locator('#memSlider').press('ArrowRight');await page.waitForTimeout(100);
 assert.equal(await page.locator('#memSlider').inputValue(),'4608');assert.equal(JSON.parse(fs.readFileSync(path.join(__dirname,'native-final/data/config.json'),'utf8')).MaxMemoryMb,4608);
 report.interactions.push({name:'Native memory slider keyboard commits 4.5GB to isolated real config',status:'PASS'});
 await page.locator('[data-nav="installations"]').click();await page.locator('[data-mods]').first().click();await page.locator('#modSearch').fill('layout_alpha');
 await page.locator('.mod-row [role="switch"]').click();await page.waitForFunction(()=>document.querySelector('.mod-row-name').textContent.endsWith('.disabled'));
 const modsDir=path.join(__dirname,'native-final/fixture-game/0/mods');assert.ok(fs.existsSync(path.join(modsDir,'layout_alpha.jar.disabled')));assert.ok(!fs.existsSync(path.join(modsDir,'layout_alpha.jar')));
 await page.locator('.mod-row [role="switch"]').click();await page.waitForFunction(()=>document.querySelector('.mod-row [role="switch"]').getAttribute('aria-checked')==='true');assert.ok(fs.existsSync(path.join(modsDir,'layout_alpha.jar')));
 report.interactions.push({name:'Native toggle twice renames the owned mod file and preserves identity',status:'PASS'});
 await page.locator('.mod-row [data-del]').click();await page.locator('[data-cancel-remove]').click();assert.ok(fs.existsSync(path.join(modsDir,'layout_alpha.jar')));
 await page.locator('.mod-row [data-del]').click();await page.locator('[data-remove-file]').click();await page.waitForFunction(()=>document.querySelector('[data-mod-count]').textContent==='1');assert.ok(!fs.existsSync(path.join(modsDir,'layout_alpha.jar')));
 report.interactions.push({name:'Native inline cancel preserves file, confirm quarantines owned mod',status:'PASS'});
 let failOnce=true;await page.route('**/api/instances/*/recovery',async route=>{if(failOnce){failOnce=false;await route.fulfill({status:503,json:{error:'Owned native retry probe'}});}else await route.continue();});
 await page.locator('#modsRecovery').click();await page.locator('#rRetry').click();await page.locator('.rec-restore').first().waitFor();assert.equal(await page.locator('#modalHost').getAttribute('data-drawer'),'true');await page.unroute('**/api/instances/*/recovery');
 report.interactions.push({name:'Fault-injected native recovery read retry stays in drawer, next read uses real API',status:'PASS'});
 await page.locator('.rec-row').filter({has:page.locator('.rec-status',{hasText:/^可恢复/})}).first().locator('.rec-restore').click();await page.waitForFunction(()=>document.querySelector('[data-mod-count]').textContent==='2');assert.equal(await page.locator('#modalHost').getAttribute('data-drawer'),'true');assert.ok(fs.existsSync(path.join(modsDir,'layout_alpha.jar')));
 await page.locator('#modsListTab').click();assert.equal(await page.locator('#modSearch').inputValue(),'layout_alpha');assert.equal(await page.locator('.mod-row [role="switch"]').getAttribute('aria-checked'),'true');
 report.interactions.push({name:'Native recovery preserves the removed file state and drawer search',status:'PASS'});
 await page.screenshot({animations:'disabled',path:path.join(root,'mod-restored-native.png')});await page.locator('#mCancel').click();await page.waitForFunction(()=>document.getElementById('modalHost').hidden);
 assert.equal(await page.evaluate(()=>document.activeElement.getAttribute('data-mods')),'layout-native-0');report.interactions.push({name:'Native drawer close returns focus to the mod entry',status:'PASS'});
 await page.locator('[data-nav="home"]').click();await page.screenshot({animations:'disabled',path:path.join(root,'final-home-native.png')});
 save();await browser.close();

 if(report.pages.some(x=>x.status==='FAIL')||report.errors.length)process.exitCode=1;
})().catch(e=>{report.fatal=e.stack;save();console.error(e);process.exitCode=1;});
