from pathlib import Path
root=Path(__file__).resolve().parents[2]
p=root/'ui/js/launcher.js'
s=p.read_text(encoding='utf-8')
def replace(a,b):
 global s
 if a not in s: raise RuntimeError('Missing anchor: '+a[:110])
 s=s.replace(a,b,1)
def function(start,end,body):
 global s
 a=s.index(start);b=s.index(end,a);s=s[:a]+body+'\n\n'+s[b:]

helper='''function uiText(zh,en) { return SG.locale === 'en' ? en : zh; }
function uiIcon(name) {
 const paths={play:'<path d="M6 4l14 8-14 8V4Z"/>',cube:'<path d="m12 3 9 5v9l-9 5-9-5V8l9-5Zm0 10v9M3 8l9 5 9-5"/>',layers:'<path d="m12 3 9 5-9 5-9-5 9-5ZM3 12l9 5 9-5M3 16l9 5 9-5"/>',world:'<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a16 16 0 0 1 0 18 16 16 0 0 1 0-18Z"/>',server:'<rect x="3" y="4" width="18" height="6" rx="2"/><rect x="3" y="14" width="18" height="6" rx="2"/><path d="M7 7h.1M7 17h.1"/>',folder:'<path d="M3 6h6l2 2h10v12H3V6Z"/>',download:'<path d="M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4"/>',search:'<circle cx="11" cy="11" r="7"/><path d="m16 16 5 5"/>',trash:'<path d="M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7"/>',close:'<path d="m6 6 12 12M6 18 18 6"/>',user:'<circle cx="12" cy="8" r="4"/><path d="M4 22v-3a8 8 0 0 1 16 0v3"/>'};
 return `<svg viewBox="0 0 24 24" aria-hidden="true">${paths[name]||paths.cube}</svg>`;
}
function uiSearch(id,label) { return `<label class="search-box">${uiIcon('search')}<input type="search" id="${id}" placeholder="${esc(label)}" aria-label="${esc(label)}"></label>`; }
function bindListSearch(el,id,selector) {
 const input=el.querySelector('#'+id);if(!input)return;
 const apply=()=>{const q=input.value.trim().toLocaleLowerCase();el.querySelectorAll(selector).forEach(row=>{const kind=row.dataset.kind;const filter=el.querySelector('[data-filter].active')?.dataset.filter||'all';row.hidden=!row.textContent.toLocaleLowerCase().includes(q)||(kind&&filter!=='all'&&kind!==filter);});};
 input.addEventListener('input',apply);el.querySelectorAll('[data-filter]').forEach(b=>b.addEventListener('click',apply));
}
'''
replace('function redesignedHome(',helper+'\nfunction redesignedHome(')
function('function redesignedHome(','async function renderInstallations()',r'''function redesignedHome(insts, sel, options, totalPlaySeconds, totalPlayCount) {
 const chips=sel?`<span class="ui-chip">${uiIcon('cube')}${esc(sel.version||'—')}</span><span class="ui-chip">${esc(sel.loader||'Vanilla')}</span><span class="ui-chip">${uiIcon('layers')}${sel.modsCount||0} Mod</span>`:'';
 return `<div class="new-home"><section class="home-hero"><div class="home-hero-art" aria-hidden="true"></div><div class="home-hero-content"><div class="home-launch-row"><div class="home-instance-info"><span class="home-kicker">${uiText('当前实例','Current instance')} · PINEGLOW JAVA EDITION</span><h2 class="home-game-title">${esc(sel?sel.name:t('home.noInstances'))}</h2><div class="home-chips">${chips}</div></div><div class="home-launch-controls"><button class="play-btn" id="btnPlay"${sel?'':' disabled'}>${uiIcon('play')}<span>${esc(t('home.play'))}</span></button><button class="ui-button" id="btnHomeInstances">${esc(t('shell.selectInstance'))}</button></div></div><div class="home-stats"><div class="home-stat">${esc(t('home.statsPlayTime'))}<b>${SG.locale==='zh'?`${Math.floor(totalPlaySeconds/3600)} <small>小时</small>`:esc(fmtPlayTime(totalPlaySeconds))}</b></div><div class="home-stat">${esc(t('home.statsPlayCount'))}<b>${totalPlayCount}</b></div><div class="home-stat">${esc(t('home.statsInstances'))}<b>${insts.length}</b></div><div class="home-stat">${esc(t('shell.memory'))}<b>${((STATE.config&&STATE.config.maxMemoryMb)||4096)/1024} <small>GB</small></b></div></div></div></section><div class="home-bottom"><section class="ui-card"><div class="ui-card-head"><h2>${esc(t('shell.queue'))}</h2><button class="news-more" id="homeAllDownloads">${esc(t('shell.allTasks'))}</button></div><div id="homeDownloadQueue" class="ui-muted">${esc(t('common.loading'))}</div></section><section class="ui-card news-section"><div class="ui-card-head"><h2>${esc(t('home.recentNews'))}</h2><button class="news-more" id="newsMore">${esc(t('home.newsMore'))}</button></div><div class="news-grid" id="homeNewsSection">${homeNewsInnerHtml()}</div></section></div></div>`;
}''')
# Installation hook IDs remain unchanged.
replace("const kind = (i.loader && i.loader !== 'Vanilla') ? 'mod' : 'all';","const kind = /(?:w\\d+[a-z]$|snapshot|pre\\d|rc\\d)/i.test(i.version||'') ? 'snap' : (i.loader && i.loader !== 'Vanilla') ? 'mod' : 'all';")
replace('<div class="inst-icon" style="background:${loaderColor(i.loader)}">${firstLetter(i.name)}</div>','<div class="inst-icon">${uiIcon(kind===\'mod\'?\'layers\':\'cube\')}</div>')
replace('<span class="inst-tag">${esc(i.loader || \'Vanilla\')}</span>','<span class="inst-tag">${esc(i.loader || \'Vanilla\')}</span>${isSel?`<span class="ui-chip chip-ok">${uiText(\'默认\',\'Default\')}</span>`:\'\'}${i.modsCount?`<span class="ui-chip">${i.modsCount} Mod</span>`:\'\'}')
replace('<div class="installations-view">','<div class="installations-view ui-card">')
replace('<div class="top-control">','<div class="top-control"><div class="list-heading"><h2>${uiText(\'你的游戏实例\',\'Your game instances\')}</h2><p>${insts.length} ${uiText(\'个实例可用 · 版本与加载器互相独立\',\'instances · separate versions and loaders\')}</p></div>${uiSearch(\'instanceSearch\',uiText(\'搜索实例…\',\'Search instances…\'))}')
replace('function bindInstallations(el) {','function bindInstallations(el) {\n bindListSearch(el,\'instanceSearch\',\'.inst-card\');')
# Content toolbar: search at top right; categories and context on second row.
old=s[s.index('\t\t\t<div class="head-actions">',s.index('async function renderContent()')):s.index('\t\t<div class="content-grid"',s.index('async function renderContent()'))]
new='''            ${uiSearch('contentSearch',t('content.searchPlaceholder')).replace('<input ',`<input value="${esc(contentQuery)}" `)}
        </div>
        <div class="content-toolbar"><div class="content-tabs">${tabs}</div><div class="content-context">${filterBadge}<div class="content-source-switch" id="contentSourceSwitch" style="display:${contentCat === 'mod' ? '' : 'none'}"><button class="source-btn ${contentSource === 'modrinth' ? 'active' : ''}" data-source="modrinth">Modrinth</button><button class="source-btn ${contentSource === 'curseforge' ? 'active' : ''}" data-source="curseforge">CurseForge</button></div></div></div>
'''
replace(old,new)
replace('<button class="mod-install" data-install=', '<button class="mod-install" data-install=') # verified unchanged contract
replace("${esc(t('content.install'))}</button>","${uiIcon('download')}${esc(t('content.install'))}</button>")
# Structure the existing settings cards without changing their handlers.
replace('''<div class="slider" id="memSlider"${autoMem ? ' style="opacity:.4;pointer-events:none"' : ''}>
							<div class="slider-fill" style="width:${pct}%"></div>
							<div class="slider-knob" style="left:${pct}%"></div>
						</div>''','''<input type="range" class="memory-range" id="memSlider" min="2048" max="16384" step="512" value="${Math.round(gb*1024)}" aria-label="${esc(t('set.memName'))}"${autoMem ? ' disabled' : ''}>''')
replace('<div class="settings-view">','<div class="settings-view"><div class="settings-grid">')
# Remove quick/log rows from behavior card, render as separate cards before account.
a=s.index('\t\t\t\t<div class="setting-divider"></div>\n\t\t\t\t<div class="setting-row">\n\t\t\t\t\t<div class="setting-info">\n\t\t\t\t\t\t<span class="setting-name">${esc(t(\'set.quickName\'))}')
b=s.index('\n\t\t\t</div>\n\t\t</div>',a)
quickrows=s[a:b]
s=s[:a]+s[b:]
accountstart=s.index('\n\t\t<div class="settings-section">\n\t\t\t<div class="section-heading">${esc(t(\'set.accountSection\'))}')
accountend=s.index('\n\t\t<div class="settings-section">',accountstart+10)
account=s[accountstart:accountend];s=s[:accountstart]+s[accountend:]
insert=s.index('\n\t\t<div class="settings-section">\n\t\t\t<div class="section-heading">${esc(t(\'set.updateSection\'))}')
cards='''
        <section class="settings-section settings-quick"><div class="section-heading">${esc(t('set.quickName'))}</div><div class="section-content"><p class="ui-muted">${esc(t('set.quickDesc'))}</p><button class="setting-btn" id="btnQuick">${esc(t('set.quickConfigure'))}</button></div></section>
        <section class="settings-section settings-logs"><div class="section-heading">${esc(t('set.logsName'))}</div><div class="section-content"><p class="ui-muted">${esc(t('set.logsDesc'))}</p><button class="setting-btn" id="btnOpenLogs">${uiIcon('folder')}${esc(t('set.openLogs'))}</button></div></section>
'''
s=s[:insert]+cards+s[insert:]
a=s.index('\n\t</div>`;',s.index('<div class="settings-view">'))
s=s[:a]+'\n        </div>'+account+s[a:]
# Native range, no pointer-only custom slider.
a=s.index('\tconst slider = el.querySelector(\'#memSlider\');',s.index('function bindSettings'))
b=s.index('\n\t// 开关',a)
s=s[:a]+''' const slider=el.querySelector('#memSlider');
 if(slider){
  slider.addEventListener('input',()=>{el.querySelector('#memValue').textContent=(Number(slider.value)/1024).toFixed(1)+' GB';});
  slider.addEventListener('change',async()=>{const previous=STATE.config.maxMemoryMb;try{await SG.api.updateConfig({maxMemoryMb:Number(slider.value),useAutoMemory:false});STATE.config.maxMemoryMb=Number(slider.value);invalidate('home');}catch(err){slider.value=String(previous);el.querySelector('#memValue').textContent=(previous/1024).toFixed(1)+' GB';toast(err.message||t('common.unknownError'));}});
 }
'''+s[b:]
replace("if (s) { s.style.opacity = on ? '.4' : ''; s.style.pointerEvents = on ? 'none' : ''; }","if (s) s.disabled = on;")
# World panel and keyboard-visible actions, use current backed actions.
replace('<div class="world-icon" style="background:${w.iconBg || \'#2b3040\'}">${esc(w.icon || \'🌍\')}</div>','<div class="world-icon">${uiIcon(\'world\')}</div>')
replace('<span class="world-size">${esc(w.size || \'—\')}</span>','<span class="world-size">${esc(w.size || \'—\')}</span><button class="world-btn" data-world-manage>${uiText(\'管理存档\',\'Manage world\')}</button>')
replace('<div class="list-grid">${cards}</div>','<div class="list-panel-head"><div class="list-heading"><h2>${uiText(\'单人存档\',\'Singleplayer worlds\')}</h2></div>${uiSearch(\'worldSearch\',uiText(\'搜索存档…\',\'Search worlds…\'))}</div><div class="list-grid">${cards}</div>')
replace('function bindWorlds(el) {','function bindWorlds(el) {\n bindListSearch(el,\'worldSearch\',\'.world-card\');')
replace('<div class="server-icon" style="background:${s.iconBg || \'#2b3040\'}">${esc(s.icon || \'🏳️\')}</div>','<div class="server-icon">${uiIcon(\'server\')}</div>')
replace('<span class="server-dot ${online ? \'on\' : \'off\'}"></span>','<button class="server-btn" data-server-ping>${uiText(\'刷新延迟\',\'Refresh ping\')}</button>')
replace('<div class="list-grid" id="serverList">${cards}</div>','<div class="list-panel-head"><div class="list-heading"><h2>${uiText(\'收藏的服务器\',\'Saved servers\')}</h2></div>${uiSearch(\'serverSearch\',uiText(\'搜索服务器…\',\'Search servers…\'))}</div><div class="list-grid" id="serverList">${cards}</div>')
replace('function bindServers(el) {','function bindServers(el) {\n bindListSearch(el,\'serverSearch\',\'.server-card\');')
# News details remains controlled via existing trusted linkId handler.
replace('<div class="news-full-body">','<div class="news-full-body">')
replace('<span class="news-date">${esc(fmtDate(n.date))}</span>\n\t\t\t</div>','<span class="news-date">${esc(fmtDate(n.date))}</span><button class="ui-button news-details">${uiText(\'查看详情\',\'View details\')}</button>\n\t\t\t</div>')
replace('<div class="content-view">\n\t\t<div class="page-head">\n\t\t\t<div>\n\t\t\t\t<div class="page-title">${esc(t(\'news.title\'))}', '<div class="content-view news-view">\n\t\t<div class="page-head">\n\t\t\t<div>\n\t\t\t\t<div class="page-title">${esc(t(\'news.title\'))}')
replace('<div id="newsViewContent">','<h2 class="content-library-title news-library-title">${uiText(\'官方动态\',\'Official news\')}</h2><div id="newsViewContent">')
# Skin headings, two-button model selector while keeping select for renderer contract.
replace('<div class="skin-preview-title">${esc(t(\'skin.previewTitle\'))}</div>','<div class="skin-preview-title">${esc(t(\'skin.previewTitle\'))}<span class="ui-chip" id="skinHeaderModel">${esc((activeSkin&&activeSkin.model)===\'slim\'?t(\'skin.slimModel\'):t(\'skin.classicModel\'))}</span></div>')
replace('<div class="skin3d-hint">','<div class="skin3d-hint">') if '<div class="skin3d-hint">' in s else None
replace('<label class="skin-model-field">${esc(t(\'skin.model\'))}<select id="skinModelSelect"><option value="classic">${esc(t(\'skin.classicModel\'))}</option><option value="slim">${esc(t(\'skin.slimModel\'))}</option></select></label>','<div class="skin-model-field"><span>${esc(t(\'skin.model\'))}</span><div class="skin-model-segment" role="group" aria-label="${esc(t(\'skin.model\'))}"><button data-skin-model="classic" aria-pressed="true">${esc(t(\'skin.classicModel\'))}</button><button data-skin-model="slim" aria-pressed="false">Slim / Alex</button></div><select id="skinModelSelect" hidden aria-label="${esc(t(\'skin.model\'))}"><option value="classic">${esc(t(\'skin.classicModel\'))}</option><option value="slim">${esc(t(\'skin.slimModel\'))}</option></select></div>')
replace('<div class="inventory-actions">','<div class="inventory-actions">${!active?`<button class="ui-button skin-login" id="btnSkinLogin">${uiIcon(\'user\')}${uiText(\'登录 Microsoft 账户\',\'Sign in with Microsoft\')}</button>`:\'\'}')
replace("modelSelect?.addEventListener('change', previewLocal);",'''modelSelect?.addEventListener('change', previewLocal);
 el.querySelectorAll('[data-skin-model]').forEach(b=>b.addEventListener('click',()=>{modelSelect.value=b.dataset.skinModel;el.querySelectorAll('[data-skin-model]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));const label=el.querySelector('#skinHeaderModel');if(label)label.textContent=b.textContent;previewLocal();}));
 el.querySelector('#btnSkinLogin')?.addEventListener('click',()=>goTo('settings'));
''')
p.write_text(s,encoding='utf-8')

idx=root/'ui/index.html';s=idx.read_text(encoding='utf-8').replace('href="#view-home"','href="#contentPanel"').replace('id="contentPanel" aria-labelledby','id="contentPanel" tabindex="-1" aria-labelledby');idx.write_text(s,encoding='utf-8')
ip=root/'ui/js/i18n.js';s=ip.read_text(encoding='utf-8');anchor='function detectDefault() {'
extra="""Object.assign(DICT.zh, {'pt.rangeTotal':'总时长','pt.activeDays':'活跃天数','pt.longest':'单次最长','pt.calendar':'日历','pt.monthTotal':'本月合计','pt.recordingInfo':'关于游玩记录'});
Object.assign(DICT.en, {'pt.rangeTotal':'Total play time','pt.activeDays':'Active days','pt.longest':'Longest session','pt.calendar':'Calendar','pt.monthTotal':'Month total','pt.recordingInfo':'About play records'});
"""
s=s.replace(anchor,extra+anchor);ip.write_text(s,encoding='utf-8')
print('Updated launcher, index and locale labels')
