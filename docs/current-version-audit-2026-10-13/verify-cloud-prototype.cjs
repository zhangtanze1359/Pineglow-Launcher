'use strict';
const fs=require('fs'),path=require('path'),assert=require('assert/strict');
const {pathToFileURL}=require('url');
const {chromium}=require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
  const out=path.join(__dirname,'cloud-prototype-qa');fs.mkdirSync(out,{recursive:true});
  const browser=await chromium.launch({channel:'msedge',headless:true});
  const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const results=[];
  try {
    await page.goto(pathToFileURL(path.resolve(__dirname,'../opendesign-ui-artifact-2026-10-13/index.html')).href);
    await page.waitForLoadState('networkidle');
    for(const size of [{width:1280,height:800},{width:960,height:640}]) {
      await page.setViewportSize(size);
      for(const screen of ['downloads','content','news']) {
        await page.locator('[data-action="nav"][data-screen="'+screen+'"]').click();
        await page.locator('#protoStates [data-state="error"]').click();
        if(screen==='news')await page.locator('#protoCache [data-value="none"]').click();
        const metrics=await page.evaluate(()=>({viewport:innerWidth,doc:document.documentElement.scrollWidth,
          main:document.querySelector('.pg-main').scrollWidth,mainClient:document.querySelector('.pg-main').clientWidth}));
        assert(metrics.doc<=metrics.viewport+1,'document horizontal overflow');
        assert(metrics.main<=metrics.mainClient+1,'main horizontal overflow');
        if(screen==='news') {
          const text=await page.locator('[data-view="news"]').innerText();
          assert(text.includes('尚未验证'));assert(!text.includes('1-01-01'));
        }
        if(screen==='content') {
          const detail=page.locator('[data-view="content"] details').first();
          assert.equal(await detail.getAttribute('open'),null);
          await detail.locator('summary').focus();await page.keyboard.press('Space');
          assert.notEqual(await detail.getAttribute('open'),null);
          await detail.locator('summary').press('Space');
        }
        await page.locator('#protoPanel [data-action="proto-toggle"]').click();
        await page.screenshot({path:path.join(out,screen+'-'+size.width+'.png'),fullPage:true});
        await page.locator('#protoToggle').click();
        results.push({screen,size,metrics,status:'PASS'});
      }
    }
    await page.locator('[data-action="nav"][data-screen="downloads"]').click();
    await page.locator('#protoStates [data-state="error"]').click();
    await page.locator('#protoOutcome [data-value="fail"]').click();
    const retry=()=>page.locator('[data-view="downloads"] .download-retry-btn').first();
    await retry().click();assert(await retry().isDisabled());
    await page.locator('#protoStates [data-state="retryError"].is-active').waitFor();
    assert(!(await retry().isDisabled()));
    await page.locator('#protoOutcome [data-value="success"]').click();await retry().click();
    await page.locator('#protoStates [data-state="retrySuccess"].is-active').waitFor();
    assert(await page.locator('[data-view="downloads"] .download-tasks').isVisible());
    results.push({check:'retry failure then recovery, independent retained tasks',status:'PASS'});
    assert.equal(errors.length,0);
    fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({scope:'OpenDesign prototype only; simulated data',results,errors,status:'PASS'},null,2));
    console.log('CLOUD_PROTOTYPE_QA=PASS checks='+results.length);
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
