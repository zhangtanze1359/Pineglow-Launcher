'use strict';
const fs=require('fs'),path=require('path'),os=require('os'),net=require('net'),assert=require('assert/strict');
const {spawn,spawnSync}=require('child_process');
const {chromium}=require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const port=()=>new Promise((res,rej)=>{const s=net.createServer();s.on('error',rej);s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>res(p));});});
(async()=>{
  const exe=path.resolve(process.argv[2]);assert(fs.existsSync(exe),'current exe missing');
  const out=path.join(__dirname,'final-webview-states');fs.mkdirSync(out,{recursive:true});
  const work=fs.mkdtempSync(path.join(os.tmpdir(),'pineglow-ui-accept-'));
  const app=path.join(work,'Pineglow.Launcher.exe');fs.copyFileSync(exe,app);fs.writeFileSync(path.join(work,'portable.flag'),'portable');
  const apiPort=await port(),debugPort=await port(),base='http://127.0.0.1:'+apiPort;
  const child=spawn(app,[],{cwd:work,stdio:'ignore',env:{...process.env,PINEGLOW_UI_DIR:'',PINEGLOW_API_PORT:String(apiPort),
    PINEGLOW_WEBVIEW_DEBUG:String(debugPort),DOTNET_BUNDLE_EXTRACT_BASE_DIR:path.join(work,'extract')}});
  let exited=false;child.on('exit',()=>{exited=true;});
  let browser;const checks=[],errors=[];let launchCalls=0,versionCalls=0;
  const control={versions:'error',content:'error',news:'error',prelaunch:'error'};
  const report={scope:'Real native WebView and real frontend API adapter; controlled local HTTP responses, no real external installation or account success',exe,work,apiPort,debugPort,checks,errors};
  try {
    const deadline=Date.now()+60000;
    while(true){try{if((await fetch(base+'/api/health')).ok)break;}catch{}if(Date.now()>deadline)throw Error('native API timeout');await sleep(300);}
    let targets;
    while(true){try{targets=await(await fetch('http://127.0.0.1:'+debugPort+'/json/list')).json();if(targets.some(t=>t.type==='page'))break;}catch{}if(Date.now()>deadline)throw Error('WebView CDP timeout');await sleep(300);}
    browser=await chromium.connectOverCDP('http://127.0.0.1:'+debugPort);
    const context=browser.contexts()[0];const page=context.pages()[0];page.on('pageerror',e=>errors.push(e.message));
    const json=(route,status,data)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
    await page.route(base+'/api/versions',r=>json(r,200,{local:[]}));
    await page.route(base+'/api/versions/remote',async r=>{versionCalls++;await sleep(400);
      if(control.versions==='error')return json(r,500,{error:'无法连接 Minecraft 版本下载源，已自动尝试官方和镜像地址。'});
      return json(r,200,{versions:control.versions==='empty'?[]:[{id:'1.21.1',type:'release',releaseTime:'2026-10-13'}]});});
    await page.route(base+'/api/downloads',r=>json(r,200,{tasks:[{id:'fixture-download',name:'已有下载任务-受控夹具',progress:27,status:'downloading'}]}));
    await page.route(base+'/api/content/search?*',async r=>{await sleep(300);if(control.content==='error')return json(r,500,{error:'受控连接失败 <b>应作为文本</b> '+('long-diagnostic-'.repeat(50))});return json(r,200,{results:[]});});
    await page.route(base+'/api/news?*',r=>json(r,200,{items:[],meta:{source:'controlled-fixture',state:control.news==='error'?'error':'empty',
      validatedAt:control.news==='error'?'0001-01-01T00:00:00+00:00':'2026-10-13T02:00:00Z',canRetry:true,errorKind:control.news==='error'?'network':null}}));
    await page.route(/\/api\/instances\/[^/]+\/prelaunch-check(?:\?|$)/,r=>control.prelaunch==='error'?json(r,500,{error:'受控启动检查失败'}):json(r,200,{ok:control.prelaunch!=='issues',errors:control.prelaunch==='issues'?['受控检查发现缺少游戏文件']:[],warnings:control.prelaunch==='warnings'?['受控内存警告']:[]}));
    await page.route(/\/api\/instances\/[^/]+\/launch(?:\?|$)/,r=>{launchCalls++;return json(r,500,{error:'fixture prevents actual launch'});});
    await page.reload();await page.locator('#btnPlay').waitFor();
    const nav=async name=>{await page.locator('.nav-item[data-nav="'+name+'"]').click();await page.locator('#view-'+name).waitFor({state:'visible'});};
    const shot=async name=>page.screenshot({path:path.join(out,name+'.png')});
    const metric=async name=>{const x=await page.locator('#view-'+name).evaluate(el=>({scroll:el.scrollWidth,client:el.clientWidth}));assert(x.scroll<=x.client+1,'horizontal overflow '+name);return x;};
    await page.setViewportSize({width:960,height:640});
    await nav('downloads');await page.locator('#view-downloads .download-source-error').waitFor();
    assert((await page.locator('#view-downloads').innerText()).includes('已有下载任务-受控夹具'));
    assert.equal(await page.locator('#versionList [data-empty="source"]').count(),0);
    await shot('downloads-error-960');checks.push({id:'CV-04-real-api-error-and-retained-tasks',status:'PASS',metrics:await metric('downloads')});
    await page.locator('[data-vfilter="all"]').click();await page.locator('#view-downloads .download-retry-btn').waitFor({state:'visible'});
    await page.waitForFunction(()=>document.querySelector('[data-vfilter="all"]').classList.contains('active'));
    await page.waitForFunction(()=>!document.querySelector('[data-dl-retry]').disabled);
    const before=versionCalls;
    await page.locator('[data-dl-retry]').evaluate(b=>{b.click();b.click();});
    assert(await page.locator('[data-dl-retry]').isDisabled());
    await page.waitForFunction(()=>{const b=document.querySelector('[data-dl-retry]');return b&&!b.disabled;});
    assert.equal(versionCalls,before+1);checks.push({id:'CV-04-double-click-deduplicated-and-retry-still-fails',status:'PASS'});
    control.versions='success';await page.locator('[data-dl-retry]').click();await page.locator('#versionList .version-id').waitFor();
    assert.equal(await page.locator('#versionList .download-source-error').count(),0);
    assert(await page.locator('[data-vfilter="all"]').evaluate(b=>b.classList.contains('active')));
    checks.push({id:'CV-04-recovery-preserves-filter',status:'PASS'});await shot('downloads-recovered-960');
    control.versions='empty';await page.locator('[data-vfilter="release"]').click();await page.locator('#versionList [data-empty="source"]').waitFor();
    assert.equal(await page.locator('#versionList .download-source-error').count(),0);
    checks.push({id:'CV-04-real-api-successful-empty-distinguished',status:'PASS'});
    await nav('content');await page.locator('#view-content .content-error').waitFor();
    await page.locator('#contentSearch').fill('retained-query');await sleep(650);await page.locator('#view-content .content-error').waitFor();
    const detail=page.locator('#view-content .content-error details');assert.equal(await detail.getAttribute('open'),null);
    await detail.locator('summary').focus();await page.keyboard.press('Space');assert.notEqual(await detail.getAttribute('open'),null);
    assert.equal(await detail.locator('b').count(),0);assert((await detail.innerText()).includes('<b>应作为文本</b>'));
    await shot('content-error-details-960');checks.push({id:'CV-06-keyboard-details-escaping-and-width',status:'PASS',metrics:await metric('content')});
    await detail.locator('summary').press('Space');control.content='success';await page.locator('[data-retry-btn]').click();
    await page.waitForFunction(()=>!document.querySelector('#view-content .content-error'));
    assert.equal(await page.locator('#contentSearch').inputValue(),'retained-query');checks.push({id:'CV-06-recovery-retains-query',status:'PASS'});
    await nav('news');await page.locator('[data-news-state="error"]').first().waitFor();
    const newsText=await page.locator('#view-news').innerText();assert(newsText.includes('尚未验证'));assert(!newsText.includes('1-01-01'));
    await shot('news-unverified-960');checks.push({id:'CV-05-real-api-minvalue-hidden',status:'PASS'});
    control.news='success';await page.locator('#view-news [data-news-retry]').click();await page.locator('[data-news-state="empty"]').first().waitFor();
    assert((await page.locator('#view-news').innerText()).includes('2026-10-13'));checks.push({id:'CV-05-real-api-valid-date-preserved',status:'PASS'});
    await nav('home');await page.locator('#btnPlay').click();await page.locator('#pcClose').waitFor();
    assert.equal(launchCalls,0);await shot('prelaunch-check-unavailable');checks.push({id:'CV-08-check-error-blocks-launch',status:'PASS'});
    await page.locator('#pcRetry').click();await page.locator('#pcRetry').waitFor();assert.equal(launchCalls,0);
    checks.push({id:'CV-08-retry-still-fails-blocks-launch',status:'PASS'});
    control.prelaunch='success';await Promise.all([page.waitForResponse(r=>/\/api\/instances\/[^/]+\/launch$/.test(r.url())),page.locator('#pcRetry').click()]);
    assert.equal(launchCalls,1);checks.push({id:'CV-08-successful-retry-reaches-intercepted-launch-once',status:'PASS'});
    control.prelaunch='warnings';await page.locator('#btnPlay').click();await page.locator('#pcOk').waitFor();assert.equal(launchCalls,1);
    await Promise.all([page.waitForResponse(r=>/\/api\/instances\/[^/]+\/launch$/.test(r.url())),page.locator('#pcOk').evaluate(b=>{b.click();b.click();})]);
    assert.equal(launchCalls,2);checks.push({id:'CV-08-warning-confirmation-required-and-deduplicated',status:'PASS'});
    control.prelaunch='issues';await page.locator('#btnPlay').click();await page.locator('.prelaunch-issues').waitFor();
    assert((await page.locator('.prelaunch-issues').innerText()).includes('受控检查发现缺少游戏文件'));assert.equal(launchCalls,2);
    checks.push({id:'CV-08-explicit-check-errors-still-block',status:'PASS'});await page.locator('#pcClose').click();
    assert.equal(errors.length,0);report.status='PASS';report.versionCalls=versionCalls;report.launchCalls=launchCalls;
    console.log('NATIVE_WEBVIEW_STATES=PASS checks='+checks.length);
  } catch(e) {report.status='FAIL';report.failure=e.message;console.error(e);process.exitCode=1;}
  finally {
    fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));
    if(browser)try{await browser.close();}catch{}
    if(!exited&&child.pid)spawnSync('taskkill',['/T','/F','/PID',String(child.pid)],{stdio:'ignore'});
  }
})().catch(e=>{console.error(e);process.exitCode=1;});
