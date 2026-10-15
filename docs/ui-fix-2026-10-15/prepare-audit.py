from pathlib import Path
d=Path(__file__).resolve().parent
s=(d.parent/'ui-recheck-2026-10-15/audit.cjs').read_text(encoding='utf-8')
s=s.replace("path.join(out,'design/pineglow-launcher.html')","path.join(out,'../ui-recheck-2026-10-15/design/pineglow-launcher.html')")
s=s.replace('for(const width of [1280,960])','for(const width of [1280,960,1600])')
s=s.replace('await slider.focus();await slider.press(\'ArrowRight\');await prod.waitForTimeout(300);','await prod.waitForTimeout(10);')
s=s.replace("const skip=await prod.evaluate(()=>({hash:location.hash,active:document.activeElement.outerHTML.slice(0,350),targetVisible:!!document.querySelector('#view-home').getClientRects().length,current:document.querySelector('.nav-item.active')?.dataset.nav}));","const skip=await prod.evaluate(()=>({hash:location.hash,active:document.activeElement.id,targetVisible:!!document.querySelector('#contentPanel').getClientRects().length,current:document.querySelector('.nav-item.active')?.dataset.nav}));")
s=s.replace("await ref.evaluate(()=>{__audit.go('installations');","await ref.setViewportSize({width:1280,height:800});await ref.evaluate(()=>{__audit.go('installations');")
s=s.replace('screenshots:40','screenshots:60')
s=s.replace("console.log(JSON.stringify({screenshots:60,statsUnchanged:before===after,cells,sliderBefore:sliderBefore.value,sliderAfter:sliderAfter.value,skip,errors,installButtons:await prod.locator('#view-installations button').evaluateAll(es=>es.map(e=>({text:e.textContent,html:e.outerHTML.slice(0,300)})))}));","console.log(JSON.stringify({screenshots:60,statsUnchanged:before===after,cells,enabledSlider,skip,recoveryAfter,errors}));")
(d/'audit.cjs').write_text(s,encoding='utf-8')
print('Prepared isolated updated audit')
