from pathlib import Path
root=Path(__file__).resolve().parents[2]
p=root/'ui/js/launcher.js';s=p.read_text(encoding='utf-8')
a=s.index('async function openModManager(inst) {');b=s.index('/* MR04：恢复区面板',a)
s=s[:a]+'''async function openModManager(inst) {
 const host = openModal(`<div class="drawer-head"><div><div class="modal-title">${esc(t('mod.manageTitle', { name: inst.name }))}</div><p>${esc(inst.version || '—')} · ${esc(inst.loader || 'Vanilla')}</p></div><button class="ui-button" id="mCancel">${esc(t('common.close'))}</button></div><div class="drawer-tools"><input type="search" id="modSearch" placeholder="${esc(t('mod.search'))}" aria-label="${esc(t('mod.search'))}"><div role="group" aria-label="${esc(t('mod.filter.all'))}">${['all','enabled','disabled'].map(k=>`<button data-mod-filter="${k}" aria-pressed="${k==='all'}">${esc(t('mod.filter.'+k))}</button>`).join('')}</div><button id="modsRecovery">${esc(t('mod.recoveryOpen'))}</button></div><div class="drawer-body mod-manage-list" id="modList"><div class="loading-state">${esc(t('common.loading'))}</div></div>`);
 if (!host) return;
 host.dataset.drawer = 'true';
 const generation = host.dataset.modalGeneration;
 const alive = () => !host.hidden && host.dataset.modalGeneration === generation && host.classList.contains('show');
 document.getElementById('mCancel').onclick = closeModal;
 document.getElementById('modsRecovery').onclick = () => openRecoveryPanel(inst, true);
 const list = document.getElementById('modList');
 let mods=[], filter='all', query='';
 const draw = () => {
   const visible=mods.filter(m=>(filter==='all'||!!m.enabled===(filter==='enabled'))&&String(m.displayName||m.fileName).toLowerCase().includes(query.toLowerCase()));
   list.innerHTML=visible.length?visible.map(m=>`<div class="mod-row ${m.enabled?'':'disabled'}"><div class="mod-row-left"><span class="mod-row-name">${esc(m.displayName||m.fileName)}</span><span class="mod-row-sub">${esc(m.version||'')} · ${esc(m.sizeText||'')}</span><span class="mod-disabled-label" ${m.enabled?'hidden':''}>${esc(t('mod.filter.disabled'))}</span></div><div class="mod-row-actions"><button class="mod-del ui-button" aria-label="${esc(t('mod.delete'))}" data-del="${esc(m.fileName)}">×</button><button class="toggle ${m.enabled?'on':''}" role="switch" aria-label="${esc(m.displayName||m.fileName)}" aria-checked="${!!m.enabled}" data-mod="${esc(m.fileName)}"></button></div></div>`).join(''):`<div class="empty-state">${esc(t(mods.length?'mod.noMatch':'mod.empty'))}</div>`;
 };
 const load = async () => {
   try { const result=await SG.api.getMods(inst.id); if(!alive())return; mods=result;draw(); }
   catch(err){if(!alive())return;list.innerHTML=`<div class="content-error" role="alert"><h2>${esc(t('mod.queryFailed'))}</h2><p>${esc(err.message)}</p><button class="ui-button" data-mod-retry>${esc(t('content.retry'))}</button></div>`;}
 };
 document.getElementById('modSearch').oninput=e=>{query=e.target.value;draw();};
 host.querySelectorAll('[data-mod-filter]').forEach(b=>b.onclick=()=>{filter=b.dataset.modFilter;host.querySelectorAll('[data-mod-filter]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));draw();});
 list.onclick=async e=>{
   const b=e.target.closest('button');if(!b||b.disabled)return;
   if(b.hasAttribute('data-mod-retry')){await load();return;}
   if(b.hasAttribute('data-cancel-remove')){b.closest('.mod-inline-confirm').remove();return;}
   const row=b.closest('.mod-row');
   if(b.dataset.del){
     list.querySelectorAll('.mod-inline-confirm').forEach(x=>x.remove());
     const strip=document.createElement('div');strip.className='mod-inline-confirm';
     strip.innerHTML=`<p>${esc(t('mod.inlineRemove'))} ${esc(b.dataset.del)}</p><button class="ui-button" data-cancel-remove>${esc(t('common.cancel'))}</button><button class="ui-button confirm-remove" data-remove-file="${esc(b.dataset.del)}">${esc(t('mod.confirmRemove'))}</button>`;
     row.appendChild(strip);strip.querySelector('[data-cancel-remove]').focus();return;
   }
   if(b.dataset.mod){
     b.disabled=true;
     try{const res=await SG.api.toggleMod(inst.id,b.dataset.mod);if(!alive())return;const m=mods.find(x=>x.fileName===b.dataset.mod);if(m)m.enabled=!!res.enabled;
       b.classList.toggle('on',!!res.enabled);b.setAttribute('aria-checked',String(!!res.enabled));row.classList.toggle('disabled',!res.enabled);row.querySelector('.mod-disabled-label').hidden=!!res.enabled;
       toast(t(res.enabled?'mod.enabled':'mod.disabled',{name:''}));
       if(filter!=='all'&&!!res.enabled!==(filter==='enabled')){row.remove();document.getElementById('modSearch')?.focus();}
     }catch(err){toast(t('mod.toggleFailed')+(err.message||''));}finally{b.disabled=false;}
   }
   if(b.dataset.removeFile){
     b.disabled=true;
     try{await SG.api.deleteMod(inst.id,b.dataset.removeFile);if(!alive())return;mods=mods.filter(x=>x.fileName!==b.dataset.removeFile);draw();toast(t('mod.deleted'));document.getElementById('modSearch')?.focus();}
     catch(err){toast(t('mod.deleteFailed')+(err.message||''));b.disabled=false;}
   }
 };
 await load();
}

''' + s[b:]
s=s.replace('async function openRecoveryPanel(inst) {','async function openRecoveryPanel(inst, drawer) {')
needle="\tdocument.getElementById('rClose').onclick = closeModal;"
s=s.replace(needle,"\tif (drawer) document.getElementById('modalHost').dataset.drawer = 'true';\n"+needle)
# Remove the unsupported game-folder action: no speculative SG.api method.
a=s.index('${sel ? `<button class="ui-button" id="btnHomeFolder"');b=s.index("}</div></div><div class=\"home-stats",a)
s=s[:a]+s[b+1:]
a=s.index("\tel.querySelector('#btnHomeFolder')?.addEventListener");b=s.index("\tel.querySelector('#homeAllDownloads')",a)
s=s[:a]+s[b:]
s=s.replace("entries.push({name:t('set.checkUpdate')", "entries.push({name:t('set.openLogs'),run:()=>SG.api.openLogDirectory()});\n\tentries.push({name:t('set.checkUpdate')")
p.write_text(s,encoding='utf-8',newline='\r\n')
