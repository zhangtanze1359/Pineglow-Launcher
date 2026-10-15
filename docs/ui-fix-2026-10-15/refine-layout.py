from pathlib import Path
d=Path(__file__).resolve().parents[2];p=d/'ui/js/launcher.js';s=p.read_text(encoding='utf-8')
def rep(a,b):
 global s
 if a not in s:raise RuntimeError(a[:90])
 s=s.replace(a,b,1)
rep('<h2 class="content-library-title">${esc(t(\'shell.contentLibrary\'))}</h2>','<h2 class="content-library-title">${esc(t(\'shell.contentLibrary\'))}</h2><p class="ui-muted">${uiText(\'查找适合当前实例的内容\',\'Find content for your current instance\')}</p>')
rep('<div class="skin-preview-title">${esc(t(\'skin.previewTitle\'))}', '<div class="skin-preview-title"><div>${esc(t(\'skin.previewTitle\'))}<p class="ui-muted">${esc(active?active.userName:uiText(\'未登录 · 可选择本地皮肤预览\',\'Not signed in · preview a local skin\'))}</p></div>')
rep('<span id="skinFallbackMessage">${esc(t(\'skin.webglFallback\'))}</span>', '<span id="skinFallbackMessage">${esc(t(\'skin.webglFallback\'))}</span>${!active?`<button class="ui-button primary skin-login" id="btnSkinLogin">${uiIcon(\'user\')}${uiText(\'登录 Microsoft 账户\',\'Sign in with Microsoft\')}</button>`:\'\'}')
rep('<div class="inventory-actions">${!active?`<button class="ui-button skin-login" id="btnSkinLogin">${uiIcon(\'user\')}${uiText(\'登录 Microsoft 账户\',\'Sign in with Microsoft\')}</button>`:\'\'}','<div class="inventory-actions">')
browse='''				<button class="skin-btn ghost" id="btnBrowseSkin">
					<svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18"/></svg>
					<span>${esc(t('skin.browse'))}</span>
				</button>'''
rep(browse,'''                <p class="ui-muted">${uiText('64×64 或 64×32 PNG，不超过 1 MiB。选择后先预览。','64×64 or 64×32 PNG, up to 1 MiB. Preview before applying.')}</p>''')
rep('<div class="skins-grid" id="skinsGrid">${cards}</div>','<div class="skins-grid" id="skinsGrid">${cards}</div>')
rep('''			</div>
		</div>
	</div>`;
}

function renderAccountCards''','''            </div>
'''+browse+'''
        </div>
    </div>`;
}

function renderAccountCards''')
rep('<span class="news-cat">${esc(n.category || \'\')}</span>\n\t\t\t\t<h3 class="news-full-title">','<div class="news-meta-row"><span class="news-cat">${esc(n.category || \'\')}</span><span class="news-date">${esc(fmtDate(n.date))}</span></div>\n\t\t\t\t<h3 class="news-full-title">')
rep('<span class="news-date">${esc(fmtDate(n.date))}</span><button class="ui-button news-details">','<button class="ui-button news-details">')
rep('<div class="dl-task-icon" style="background:${t.iconBg || \'#0f766e\'}">${esc(t.icon || \'⛏️\')}</div>','<div class="dl-task-icon">${uiIcon(\'download\')}</div>')
# Keep the real file identity visible after rename, including when a filter is active.
rep("b.dataset.mod=nextFile;const remove=", "b.dataset.mod=nextFile;row.querySelector('.mod-row-name').textContent=nextFile;const remove=")
# Explicit keyboard actions need not make the whole card focusable.
rep("const query=el.querySelector('#instanceSearch')?.value.trim().toLocaleLowerCase()||'';", "const query=el.querySelector('#instanceSearch')?.value.trim().toLocaleLowerCase()||'';")
# Give existing switches their actual settings names.
for key,label in [('autoMem','set.autoMemName'),('newsFakeIp','set.fakeIpName'),('aiDiag','set.aiEnableName')]:
 rep(f'data-toggle="{key}" role="switch"',f'data-toggle="{key}" aria-label="${{esc(t(\'{label}\'))}}" role="switch"')
p.write_text(s,encoding='utf-8')
css=d/'ui/css/redesign.css';x=css.read_text(encoding='utf-8');x+='''
/* Final spacing pass against the supplied desktop reference. */
.content-panel { scrollbar-gutter:auto; }
.home-task-line > span:first-child { color:var(--fg); }
.home-task-state { font-size:12px; }
.content-view .page-head:has(.content-library-title) { margin-bottom:12px; }
.content-source-switch .source-btn { white-space:nowrap; }
.content-context { gap:8px; }
.top-control .search-box { max-width:306px; }
.section-heading { padding:0; border:0; }
.section-content { padding:0; }
.setting-row:has(.slider-wrap) { display:grid!important; grid-template-columns:1fr auto; gap:8px; }
.setting-row:has(.slider-wrap) .setting-info,.setting-row:has(.slider-wrap) .slider-wrap { display:contents; }
.setting-row:has(.slider-wrap) .setting-name { grid-column:1; grid-row:1; }
.setting-row:has(.slider-wrap) .setting-value { grid-column:2; grid-row:1; }
.setting-row:has(.slider-wrap) .memory-range { grid-column:1/-1; grid-row:2; }
.setting-row:has(.slider-wrap) .setting-desc { grid-column:1/-1; grid-row:3; }
.memory-range { appearance:none; -webkit-appearance:none; height:22px; }
.memory-range::-webkit-slider-runnable-track { height:4px; background:rgba(249,249,249,.12); border-radius:99px; }
.memory-range::-webkit-slider-thumb { appearance:none; -webkit-appearance:none; width:14px; height:14px; border-radius:50%; background:var(--fg); margin-top:-5px; }
.skin-preview-panel { position:relative; }
.skin3d-hint { justify-content:flex-start; padding:16px; height:54px; }
.skin3d-controls { position:absolute; right:0; bottom:0; padding:12px 16px; }
.skin3d-fallback,.skin2d-fallback { gap:16px; }
.inventory-panel { gap:16px; }
.saved-skins { padding:0; }
.skins-grid:has(> div:only-child) { grid-template-columns:minmax(0,1fr); text-align:center; }
.skins-grid:has(> div:only-child) > div { width:100%; padding:24px!important; }
.news-meta-row { display:flex; gap:8px; align-items:center; }
.news-meta-row .news-cat { padding:1px 7px; border:1px solid var(--border); border-radius:6px; }
.news-full-title { font-size:18px; }
.news-status { margin:0 0 12px; }
.news-full-list { margin:0; }
.news-library-title { margin-bottom:0; }
.cal-cell:disabled { opacity:1; color:var(--meta); }
.cal-cell:disabled:hover { background:var(--bg); }
.refresh-view { border:0; }
''';css.write_text(x,encoding='utf-8')
print('Refined component spacing and explicit states')
