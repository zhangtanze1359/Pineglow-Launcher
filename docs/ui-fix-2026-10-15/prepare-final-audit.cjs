const fs=require('fs'),path=require('path');const dir=__dirname;
let s=fs.readFileSync(path.join(dir,'audit.cjs'),'utf8');
s=s.replace("out=__dirname;","out=path.join(__dirname,'green');fs.mkdirSync(out,{recursive:true});");
s=s.replace("path.join(out,'../ui-recheck-2026-10-15/design/pineglow-launcher.html')","path.join(__dirname,'../ui-recheck-2026-10-15/design/pineglow-launcher.html')");
s=s.replace("const requests=[],errors=[];let recoveryFail=false;",`const requests=[],errors=[],checks=[];let recoveryFail=false;
 const check=(name,condition)=>{checks.push({name,status:condition?'PASS':'FAIL'});if(!condition)throw Error(name);};
 let fixtureMods=(DB.mods[instances[0].id]||[]).map(m=>({...m,fileName:m.file+(m.enabled?'':'.disabled'),displayName:m.name,isEnabled:m.enabled,sizeText:m.size})),fixtureRecoveries=[];`);
s=s.replace("if(req.method()!=='GET'){await route.fulfill({status:409,json:{error:'READ_ONLY_AUDIT'}});return;}",`if(req.method()!=='GET'){
   if(p==='/api/config'){Object.assign(config,JSON.parse(req.postData()||'{}'));await route.fulfill({json:{ok:true}});return;}
   const parts=p.split('/').map(decodeURIComponent),file=parts.at(-2);
   if(p.endsWith('/toggle')){const m=fixtureMods.find(x=>x.fileName===file);if(m){m.isEnabled=!m.isEnabled;m.fileName=m.isEnabled?m.fileName.replace(/\\.disabled$/,''):m.fileName+'.disabled';await route.fulfill({json:{ok:true,fileName:m.fileName,isEnabled:m.isEnabled}});return;}}
   if(p.endsWith('/delete')){const m=fixtureMods.find(x=>x.fileName===file);if(m){fixtureMods=fixtureMods.filter(x=>x!==m);fixtureRecoveries.push({operationId:'audit-recovery',reason:'manual',status:'quarantined',createdAt:'2026-10-15T04:00:00Z',totalBytes:100,files:[{relativePath:m.fileName,state:'quarantined',sizeBytes:100}],mod:m});await route.fulfill({json:{ok:true,recoverable:true}});return;}}
   if(p.endsWith('/restore')){const rec=fixtureRecoveries.find(x=>x.operationId===file);if(rec){rec.status='restored';fixtureMods.push({...rec.mod,isEnabled:false,fileName:rec.mod.fileName.replace(/\\.disabled$/,'')+'.disabled'});await route.fulfill({json:{ok:true,status:'restored',conflicts:0}});return;}}
   await route.fulfill({status:409,json:{error:'UNEXPECTED_AUDIT_WRITE'}});return;
  }`);
s=s.replace("body={mods:(DB.mods[instances[0].id]||[]).map(m=>({...m,fileName:m.file||m.fileName||m.name+'.jar',displayName:m.name,isEnabled:m.enabled,sizeText:m.size}))}","body={mods:fixtureMods}");
s=s.replace("body={retentionDays:30,recoveries:[]}","body={retentionDays:30,recoveries:fixtureRecoveries}");
s=s.replace("metrics.push({width,name,reference:await inspect(ref),product:await inspect(prod)});",`const reference=await inspect(ref),product=await inspect(prod);metrics.push({width,name,reference,product});
   check(name+' '+width+' only visible page, no horizontal overflow',await prod.evaluate(()=>{const p=document.querySelector('#contentPanel');return [...document.querySelectorAll('.view')].filter(e=>!e.hidden).length===1&&p.scrollWidth-p.clientWidth<=1;}));`);
const anchor="fs.writeFileSync(path.join(out,'metrics.json'),JSON.stringify(metrics,null,2));";
const probes=`
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
 check('No uncaught product JavaScript errors',errors.length===0);
 fs.writeFileSync(path.join(out,'behavior-checks.json'),JSON.stringify({kind:'Controlled API simulation, all writes in memory; not backend proof',checks},null,2));
 `;
s=s.replace(anchor,probes+anchor);
s=s.replace(".catch(e=>{console.error(e);server.close();process.exitCode=1;});",".catch(e=>{fs.writeFileSync(path.join(__dirname,'green/fatal.txt'),e.stack);console.error(e);server.close();process.exitCode=1;});");
fs.writeFileSync(path.join(dir,'audit-final.cjs'),s);
