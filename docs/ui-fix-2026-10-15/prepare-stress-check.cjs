const fs=require('fs'),path=require('path');const dir=__dirname;let s=fs.readFileSync(path.join(dir,'audit-final.cjs'),'utf8');
s=s.replace("out=path.join(__dirname,'green')","out=path.join(__dirname,'green-final')").replace("'green/fatal.txt'","'green-final/fatal.txt'");
s=s.replace("checks.push({name,status:condition?'PASS':'FAIL'});if(!condition)","checks.push({name,status:condition?'PASS':'FAIL'});fs.writeFileSync(path.join(out,'behavior-progress.json'),JSON.stringify(checks,null,2));if(!condition)");
const anchor="check('No uncaught product JavaScript errors',errors.length===0);";
const more=`
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
 ${anchor}
`;
s=s.replace(anchor,more);fs.writeFileSync(path.join(dir,'audit-stress.cjs'),s);
