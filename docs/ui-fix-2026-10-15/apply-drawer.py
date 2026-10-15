from pathlib import Path
p=Path(__file__).resolve().parents[2]/'ui/js/launcher.js'
s=p.read_text(encoding='utf-8')
def swap(a,b):
 global s
 if a not in s:raise RuntimeError(a[:100])
 s=s.replace(a,b,1)
a=s.index(' const host = openModal(',s.index('async function openModManager'))
b=s.index('\n if (!host)',a)
s=s[:a]+''' const host = openModal(`<div class="drawer-head"><div><div class="modal-title">${esc(t('mod.manageTitle', { name: inst.name }))}</div><p>${esc(inst.version||'—')} · ${esc(inst.loader||'Vanilla')} · <span data-mod-count>—</span> Mod · ${uiText('恢复区','Recovery')} <span data-recovery-count>—</span></p></div><button class="ui-button drawer-close" id="mCancel" aria-label="${esc(t('common.close'))}">${uiIcon('close')}</button></div><div class="drawer-tabs" role="group" aria-label="${uiText('管理视图','Manage view')}"><button id="modsListTab" aria-pressed="true">${uiText('模组列表','Mod list')} (<span data-mod-count>—</span>)</button><button id="modsRecovery" aria-pressed="false">${esc(t('mod.recoveryOpen'))} (<span data-recovery-count>—</span>)</button></div><div id="modWorkspace" class="mod-workspace"><div class="drawer-tools"><input type="search" id="modSearch" placeholder="${esc(t('mod.search'))}" aria-label="${esc(t('mod.search'))}"><div class="mod-filters" role="group" aria-label="${esc(t('mod.filter.all'))}">${['all','enabled','disabled'].map(k=>`<button data-mod-filter="${k}" aria-pressed="${k==='all'}">${esc(t('mod.filter.'+k))} <span data-mod-filter-count="${k}">—</span></button>`).join('')}</div></div><div class="drawer-body mod-manage-list" id="modList"><div class="loading-state">${esc(t('common.loading'))}</div></div></div><div class="drawer-body" id="modRecoveryBody" hidden></div>`);'''+s[b:]
swap(" document.getElementById('modsRecovery').onclick = () => openRecoveryPanel(inst, true);",''' const workspace=document.getElementById('modWorkspace'),recoveryBody=document.getElementById('modRecoveryBody');
 const setCount=(selector,count)=>host.querySelectorAll(selector).forEach(e=>e.textContent=String(count));
 const context={alive,onCount:n=>setCount('[data-recovery-count]',n),reloadMods:()=>load()};
 const selectTab=async recovery=>{workspace.hidden=recovery;recoveryBody.hidden=!recovery;host.querySelector('#modsListTab').setAttribute('aria-pressed',String(!recovery));host.querySelector('#modsRecovery').setAttribute('aria-pressed',String(recovery));if(recovery)await openRecoveryPanel(inst,true,context);};
 document.getElementById('modsRecovery').onclick=()=>selectTab(true);
 document.getElementById('modsListTab').onclick=()=>selectTab(false);''')
swap(' const draw = () => {\n   const visible=mods.filter', ''' const draw = () => {
   setCount('[data-mod-count]',mods.length);
   for(const key of ['all','enabled','disabled'])setCount(`[data-mod-filter-count="${key}"]`,mods.filter(m=>key==='all'||!!m.enabled===(key==='enabled')).length);
   const visible=mods.filter''')
swap("${esc(m.displayName||m.fileName)}</span><span class=\"mod-row-sub\">${esc(m.version||'')} · ${esc(m.sizeText||'')}","${esc(m.fileName)}</span><span class=\"mod-row-sub\">${esc(m.displayName||'')} ${esc(m.version||'')} · ${esc(m.sizeText||'')}")
swap("       toast(t(res.enabled?'mod.enabled':'mod.disabled',{name:''}));","       toast(t(res.enabled?'mod.enabled':'mod.disabled',{name:''}));\n       for(const key of ['all','enabled','disabled'])setCount(`[data-mod-filter-count=\"${key}\"]`,mods.filter(m=>key==='all'||!!m.enabled===(key==='enabled')).length);")
swap("mods=mods.filter(x=>x.fileName!==b.dataset.removeFile);draw();toast(t('mod.deleted'));","mods=mods.filter(x=>x.fileName!==b.dataset.removeFile);draw();SG.api.listRecoveries(inst.id).then(data=>{if(alive())context.onCount((data.recoveries||[]).length);}).catch(()=>{});toast(t('mod.deleted'));")
swap(' await load();\n}\n\n/* MR04', ''' await load();
 SG.api.listRecoveries(inst.id).then(data=>{if(alive())context.onCount((data.recoveries||[]).length);}).catch(()=>{});
}

/* MR04''')
swap('async function openRecoveryPanel(inst, drawer) {','''async function openRecoveryPanel(inst, drawer, context) {
 const embedded=!!(drawer&&context);
 const stillHere=()=>!embedded||context.alive()&&document.getElementById('modRecoveryBody')&&!document.getElementById('modRecoveryBody').hidden;
 if(!stillHere())return;''')
swap('''	openModal(`
		<div class="modal-title">${esc(t('recovery.title', { name: inst.name }))}</div>''',''' const panelHtml=`
		<div class="modal-title" ${embedded?'hidden':''}>${esc(t('recovery.title', { name: inst.name }))}</div>''')
swap('''<div class="modal-actions"><button class="modal-btn primary" id="rClose">${esc(t('common.done'))}</button></div>`);
	if (drawer)''','''<div class="modal-actions" ${embedded?'hidden':''}><button class="modal-btn primary" id="rClose">${esc(t('common.done'))}</button></div>`;
 if(embedded)document.getElementById('modRecoveryBody').innerHTML=panelHtml;else openModal(panelHtml);
	if (drawer)''')
swap('''		data = await SG.api.listRecoveries(inst.id);
	} catch (err) {''','''		data = await SG.api.listRecoveries(inst.id);
        if(!stillHere())return;
	} catch (err) {
        if(!stillHere())return;''')
swap('retry.onclick = () => { openRecoveryPanel(inst); };','retry.onclick = () => { openRecoveryPanel(inst, drawer, context); };')
swap("\tconst ops = (data && data.recoveries) || [];","\tconst ops = (data && data.recoveries) || [];\n if(embedded)context.onCount(ops.length);")
swap('''			closeModal();
			openRecoveryPanel(inst);''','''            if(embedded){if(!stillHere())return;await context.reloadMods();await openRecoveryPanel(inst,true,context);}
            else {closeModal();openRecoveryPanel(inst,drawer);}''')
p.write_text(s,encoding='utf-8')
print('Unified mod recovery drawer')
