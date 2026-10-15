'use strict';
const fs=require('fs'),path=require('path'),assert=require('assert/strict');
const {chromium}=require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root=__dirname,report={kind:'Real WebView2 with live local API; viewport dimensions emulated by CDP',pages:[],errors:[],interactions:[]};
const save=()=>fs.writeFileSync(path.join(root,'webview-report.json'),JSON.stringify(report,null,2));
(async()=>{
 const browser=await chromium.connectOverCDP('http://127.0.0.1:'+(process.env.PINEGLOW_VERIFY_DEBUG||'49377'));
 const page=browser.contexts()[0].pages()[0];page.setDefaultTimeout(12000);
 page.on('pageerror',e=>{report.errors.push(e.message);save();});
 await page.reload();await page.waitForFunction(()=>window.SG&&SG.__test__&&SG.__test__.getCurrentView());
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
 save();await browser.close();
 if(report.pages.some(x=>x.status==='FAIL')||report.errors.length)process.exitCode=1;
})().catch(e=>{report.fatal=e.stack;save();console.error(e);process.exitCode=1;});
