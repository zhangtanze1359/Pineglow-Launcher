from pathlib import Path
p=Path(__file__).resolve().parents[2]/'ui/css/redesign.css'
s=p.read_text(encoding='utf-8')
s=s.replace('--meta: #818283','--meta: #6a6b6c').replace("--font-head: 'Inter','Segoe UI','Microsoft YaHei UI',system-ui,sans-serif;","--font-head: 'Inter','Inter Fallback','SF Pro Display',-apple-system,system-ui,'Segoe UI',sans-serif;").replace("--font-body: 'Inter','Segoe UI','Microsoft YaHei UI',system-ui,sans-serif;","--font-body: 'Inter','Inter Fallback','SF Pro Text',-apple-system,system-ui,'Segoe UI',sans-serif;")
# Remove the earlier approximation; the blocks below own desktop layout.
s=s[:s.index('@media(max-width:1150px)')]+s[s.index('@media(pointer:coarse)'):]
s+='''
/* ZIP handoff: these selectors own the new page layouts. Legacy CSS remains
   for dialogs and product flows, with its layout defaults explicitly replaced. */
body { font-size:14px; font-weight:500; letter-spacing:.2px; font-feature-settings:"calt" 1,"kern" 1,"liga" 1,"ss03" 1; }
h1,h2,h3,h4 { font-family:var(--font-head); font-weight:600; letter-spacing:-.01em; }
button,input,select,textarea { font-family:inherit; letter-spacing:inherit; }
button:disabled { opacity:.45; cursor:not-allowed; }
.brand-sub { border-left:1px solid var(--border); margin-left:4px; padding-left:12px; }
.brand-pixel { width:12px; height:14px; }
.sidebar { width:236px; flex-basis:236px; }
.navigation { gap:2px; }
.nav-item { font-weight:500; }
.nav-group-label { letter-spacing:2px; }
.nav-count { color:var(--meta); }
.lang-switch button { height:24px; }
.workspace-topbar { min-height:0; padding:16px 32px 12px; }
.workspace-topbar h1 { font-weight:600; }
.workspace-topbar p { margin-top:2px; }
.quick-open { height:28px; font-size:12px; padding:0 12px; }
.content-panel { padding:24px 32px 48px; }
.view { margin:0 auto; }
.ui-button,.btn-new,.modal-btn,.setting-btn,.world-btn,.server-btn,.skin-btn { display:inline-flex; align-items:center; justify-content:center; gap:8px; border:1px solid rgba(249,249,249,.10); background:transparent; color:var(--fg); font-weight:600; }
.ui-button svg,.setting-btn svg,.skin-btn svg,.world-btn svg,.mod-install svg,.inst-icon svg,.world-icon svg,.server-icon svg,.ui-chip svg { width:15px; height:15px; flex-shrink:0; fill:none; stroke:currentColor; stroke-width:1.6; stroke-linejoin:round; stroke-linecap:round; }
.ui-button.primary,.modal-btn.primary,.btn-new,.skin-btn.primary { background:rgba(249,249,249,.82); color:var(--ink); border-color:transparent; }
.ui-button:hover,.setting-btn:hover,.world-btn:hover,.server-btn:hover { background:rgba(249,249,249,.07); }
.ui-button.primary:hover,.btn-new:hover,.skin-btn.primary:hover { background:var(--fg); }
.ui-chip { display:inline-flex; align-items:center; gap:5px; height:20px; padding:2px 8px; border:1px solid var(--border); border-radius:6px; background:rgba(249,249,249,.04); font:11px var(--font-data); color:var(--fg-2); white-space:nowrap; }
.ui-chip svg { width:11px; height:11px; }
.chip-ok { color:var(--success); border-color:rgba(89,217,155,.32); background:rgba(89,217,155,.15); }
.ui-card,.content-view,.settings-section { padding:16px; }
.ui-card-head { margin-bottom:12px; }
.ui-card-head h2,.content-library-title,.list-heading h2,.section-heading { font-size:14px; font-weight:600; line-height:1.6; letter-spacing:0; }
.ui-muted,.list-heading p { color:var(--meta); font-size:12px; }
.search-box { width:200px; flex:0 1 200px; height:34px; padding:0 12px; border:1px solid rgba(249,249,249,.10); gap:8px; }
.search-box input { width:100%; background:transparent; border:0; font-size:14px; color:var(--fg); }
.search-box svg { width:13px; height:13px; }
.home-hero { padding:0; box-shadow:0 0 0 1px #1b1c1e,0 0 0 1px var(--bg) inset; border-color:transparent; }
.home-hero-art { position:relative; inset:auto; height:120px; background:url('../assets/pine-forest-night.svg') center top/cover; opacity:1; border-bottom:1px solid var(--border); }
.home-hero-art::after { content:""; position:absolute; inset:0; background:linear-gradient(180deg,transparent 14%,rgba(16,17,17,.94) 100%); }
.home-hero-content { padding:20px; gap:20px; }
.home-launch-row { flex-wrap:wrap; gap:20px; }
.home-instance-info { min-width:0; flex:1; }
.home-kicker { font:12px var(--font-data); letter-spacing:.2px; color:var(--meta); }
.home-game-title { font-size:24px; line-height:1.2; margin-top:8px; overflow-wrap:anywhere; }
.home-chips { display:flex; flex-wrap:wrap; gap:8px; margin-top:8px; }
.home-launch-controls { gap:8px; }
.home-launch-controls .play-btn,.home-launch-controls .ui-button { height:44px; border-radius:12px; padding:0 24px; font-size:16px; }
.home-launch-controls .play-btn { background:rgba(249,249,249,.82); }
.home-launch-controls .play-btn svg { width:18px; height:18px; fill:none; stroke:currentColor; stroke-width:1.6; }
.home-stats { border:0; padding:0; gap:12px; }
.home-stat { font-size:12px; color:var(--meta); }
.home-stat b { font:600 20px/1.3 var(--font-data); color:var(--fg); margin-top:8px; letter-spacing:.2px; }
.home-stat small { font:12px var(--font-body); color:var(--muted); }
.home-bottom { grid-template-columns:minmax(0,1.65fr) minmax(0,1fr); }
.home-task { padding:12px 8px; }
.home-task:first-child { border-top:0; }
.home-task-line { font-size:12px; }
.home-task-line > span:first-child { font-size:14px; font-weight:600; }
.home-task-track { height:4px; border-radius:3px; }
.home-task-track span { background:var(--accent); }
#homeNewsSection .news-card { padding:12px 8px; min-height:0; }
#homeNewsSection .news-card:first-child { border-top:0; }
#homeNewsSection .news-card-title { font-size:14px; }
.content-view .page-head { margin-bottom:16px; min-height:34px; }
.content-toolbar { display:flex; align-items:center; justify-content:space-between; gap:12px; margin-bottom:12px; flex-wrap:wrap; }
.content-tabs { margin:0; background:rgba(249,249,249,.02); }
.content-tabs .filter { height:26px; padding:0 12px; font-size:12px; }
.content-context { display:flex; align-items:center; justify-content:flex-end; gap:8px; flex-wrap:wrap; }
.content-filter-hint { display:inline-flex; padding:2px 8px; height:20px; align-items:center; font:11px var(--font-data); border:1px solid var(--border); background:rgba(249,249,249,.04); border-radius:6px; color:var(--fg-2); }
.content-source-switch { display:flex; padding:2px; border:1px solid var(--border); border-radius:6px; gap:0; }
.source-btn { height:24px; padding:0 8px; font-size:11px; }
.content-grid { grid-template-columns:repeat(3,minmax(0,1fr)); }
.mod-card { padding:16px; }
.mod-top { align-items:flex-start; gap:8px; }
.mod-name { font-size:16px; font-weight:600; line-height:1.4; white-space:normal; overflow-wrap:anywhere; }
.mod-body { gap:8px; }
.mod-author { font-size:12px; color:var(--meta); }
.mod-desc { font-size:12px; min-height:0; line-height:1.6; color:var(--fg-2); margin:4px 0; }
.mod-install { height:28px; padding:0 12px; font-size:12px; display:inline-flex; gap:8px; align-items:center; }
.mod-meta { margin-top:0; }
.mod-dl { display:none; }
.mod-badge { font-size:11px; padding:1px 7px; }
.installations-view { border:1px solid var(--border); border-radius:12px; background:var(--surface); }
.top-control { display:flex; flex-wrap:wrap; gap:8px; justify-content:flex-end; align-items:center; margin-bottom:12px; }
.list-heading { margin-right:auto; min-width:0; }
.list-heading p { margin-top:2px; }
.filters { display:flex; align-items:center; width:fit-content; background:rgba(249,249,249,.02); border:1px solid var(--border); padding:3px; }
.filter { height:26px; min-height:26px; font-size:12px; padding:0 12px; }
.btn-new { min-height:28px; padding:0 12px; }
#btnAddExisting { background:transparent; border-color:rgba(249,249,249,.10); color:var(--fg); }
.inst-list,.worlds-list,.server-list,.list-grid { border:0; border-radius:0; gap:0; background:transparent; }
.inst-card,.world-card,.server-card { padding:12px 8px; border:0; border-top:1px solid var(--border); background:transparent; min-height:73px; gap:12px; }
.inst-card:first-child,.world-card:first-child,.server-card:first-child { border-top:0; }
.inst-left { gap:12px; }
.inst-icon,.world-icon,.server-icon { width:46px; height:46px; flex:0 0 46px; display:grid; place-items:center; border-radius:6px; border:1px solid var(--border); background:var(--bg)!important; color:var(--muted); }
.inst-icon svg,.world-icon svg,.server-icon svg { width:18px; height:18px; }
.inst-name,.world-name,.server-name { font-size:14px; font-weight:600; }
.inst-name-row { flex-wrap:wrap; gap:8px; }
.inst-tag { font-size:11px; padding:1px 7px; }
.inst-sub,.world-sub,.server-sub { font-size:12px; color:var(--muted); line-height:1.6; }
.inst-actions { gap:8px; }
.inst-play.primary,.inst-play.ghost { background:transparent; color:var(--fg); border:1px solid rgba(249,249,249,.10); height:28px; border-radius:6px; }
.inst-mods,.world-btn,.server-btn { height:28px; min-height:28px; font-size:12px; padding:0 12px; }
.inst-more { border-radius:6px; color:var(--muted); }
.list-panel-head { display:flex; justify-content:space-between; align-items:center; gap:12px; flex-wrap:wrap; margin-bottom:12px; }
.world-meta,.server-meta { min-width:0; flex:1; }
.world-size { font-size:12px; color:var(--meta); }
.world-card .world-btn { flex-shrink:0; }
.server-status { gap:12px; }
.server-count { font:12px var(--font-data); font-weight:500; color:var(--meta); }
.server-ping { font:12px var(--font-data); color:var(--fg); }
.settings-view { display:flex; flex-direction:column; gap:20px; }
.settings-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:16px; align-items:stretch; }
.settings-section { min-width:0; margin:0; border:1px solid var(--border); border-radius:12px; }
.section-heading { margin:0 0 12px; color:var(--fg); text-transform:none; }
.section-content { gap:20px; }
.setting-row { gap:8px; align-items:flex-start; flex-wrap:wrap; }
.setting-info { max-width:none; gap:4px; flex:1; min-width:0; }
.setting-name { font-size:14px; font-weight:500; }
.setting-desc { font-size:12px; color:var(--meta); line-height:1.6; }
.setting-divider { display:none; }
.settings-grid .setting-row:has(.slider-wrap),.settings-grid .setting-row:has(#btnJrePath),.settings-grid .setting-row:has(#sourceSelect),.settings-grid .setting-row:has(#btnProxy) { flex-direction:column; align-items:stretch; gap:8px; }
.slider-wrap { width:100%; gap:12px; flex-direction:row-reverse; }
.setting-value { font:600 12px var(--font-data); white-space:nowrap; }
.memory-range { width:100%; min-width:0; height:22px; accent-color:var(--fg); background:transparent; cursor:pointer; }
.memory-range:disabled { cursor:not-allowed; opacity:.45; }
.setting-btn { font-size:12px; height:34px; max-width:100%; overflow-wrap:anywhere; white-space:normal; }
#btnJrePath,#btnProxy,#btnSourceChange,.settings-quick .setting-btn,.settings-logs .setting-btn { width:100%; }
.settings-quick .section-content,.settings-logs .section-content { gap:16px; }
.toggle { width:34px; height:20px; flex:0 0 34px; border-radius:999px; background:rgba(249,249,249,.07); border:1px solid var(--border); }
.toggle::after { width:14px; height:14px; left:2px; top:2px; background:var(--muted); }
.toggle.on { background:rgba(249,249,249,.82); }
.toggle.on::after { transform:translateX(14px); background:var(--surface); }
.skins-view { grid-template-columns:minmax(0,1fr) 320px; gap:16px; }
.skin-preview-panel { padding:0; overflow:hidden; display:flex; flex-direction:column; }
.skin-preview-title { display:flex; align-items:center; justify-content:space-between; padding:16px; font-size:14px; border-bottom:1px solid var(--border); }
.preview-canvas { height:296px; min-height:296px; width:100%; border:0; border-radius:0; margin:0; background-color:var(--bg); background-image:linear-gradient(rgba(249,249,249,.025) 1px,transparent 1px),linear-gradient(90deg,rgba(249,249,249,.025) 1px,transparent 1px); background-size:28px 28px; }
.skin3d-hint { margin:0; padding:12px 16px; text-align:left; border-top:1px solid var(--border); font-size:12px; color:var(--meta); }
.skin3d-hint-dot { display:none; }
.skin3d-controls { margin:0; padding:0 16px 12px; justify-content:flex-end; }
.skin-control { height:28px; border-radius:6px; padding:0 12px; font-size:12px; border:1px solid var(--border); background:transparent; }
.canvas-details { display:none; }
.inventory-panel { padding:16px; }
.inventory-actions { display:flex; flex-direction:column; align-items:stretch; gap:16px; }
.skin-model-field { display:flex; flex-direction:column; gap:6px; color:var(--meta); font-size:12px; }
.skin-model-segment { display:flex; border:1px solid var(--border); border-radius:6px; padding:3px; gap:2px; }
.skin-model-segment button { flex:1; height:26px; font-size:12px; border-radius:4px; color:var(--muted); }
.skin-model-segment button[aria-pressed="true"] { background:rgba(249,249,249,.07); color:var(--fg); }
.skin-btn { width:100%; height:34px; }
.skin-btn.primary { background:transparent; border-color:rgba(249,249,249,.10); color:var(--fg); }
.saved-skins { padding-top:20px; }
.saved-label { font-size:12px; color:var(--meta); margin-bottom:8px; }
.skins-grid:has(> div:only-child) { background:var(--bg); border:1px solid var(--border); border-radius:12px; min-height:100px; place-items:center; }
.skin-local-preview { margin-top:16px; gap:8px; }
.skin-login { font-size:12px; }
.dl-layout { display:flex; flex-direction:column; gap:20px; }
#view-downloads > .content-view { padding:0; background:transparent; border:0; }
.dl-layout > div { padding:16px; border:1px solid var(--border); background:var(--surface); border-radius:12px; }
.dl-section-title { display:flex; align-items:center; justify-content:space-between; margin-bottom:12px; font-size:14px; gap:12px; }
.version-list { gap:0; }
.version-row { border:0; border-top:1px solid var(--border); border-radius:0; background:transparent; padding:12px 8px; min-height:72px; }
.version-row:first-child { border-top:0; }
.version-name { font-size:14px; }
.version-dl-btn { min-height:28px; height:28px; padding:0 12px; font-size:12px; background:transparent; color:var(--fg); border:1px solid rgba(249,249,249,.10); }
.download-tasks .download-card,.download-tasks .dl-task { padding:12px 8px; border:0; border-top:1px solid var(--border); border-radius:0; background:transparent; }
.dl-task-icon { background:var(--bg)!important; }
.content-view.news-view { padding:16px; background:var(--surface); border:1px solid var(--border); }
.news-library-title { margin-top:-42px; min-height:28px; pointer-events:none; }
.news-status { font-size:12px; }
.news-full-list { gap:12px; margin-top:12px; }
.news-full-card { display:block; padding:16px; border:1px solid var(--border); border-radius:12px; background:var(--surface); }
.news-full-art { display:none; }
.news-full-body { gap:8px; }
.news-cat { font-size:11px; color:var(--muted); }
.news-full-title { font-size:18px; font-weight:600; }
.news-full-desc { font-size:12px; color:var(--fg-2); line-height:1.6; }
.news-details { width:fit-content; height:28px; font-size:12px; padding:0 12px; }
.news-date { font-size:12px; color:var(--meta); }
.modal-host[data-drawer="true"] { backdrop-filter:blur(5px); background:rgba(7,8,10,.76); }
.modal-host[data-drawer="true"] .modal { width:min(560px,calc(100vw - 236px)); padding:20px; gap:16px; border-radius:0; animation:none; }
.drawer-head { flex-shrink:0; }
.drawer-head .modal-title { font-size:18px; }
.drawer-head p { font-size:12px; color:var(--muted); }
.drawer-close { height:28px; width:28px; padding:0; border:0; }
.drawer-tabs { display:flex; gap:2px; border:1px solid var(--border); border-radius:6px; padding:3px; flex-shrink:0; }
.drawer-tabs button { flex:1; height:26px; border-radius:4px; font-size:12px; color:var(--muted); }
.drawer-tabs button[aria-pressed="true"] { background:rgba(249,249,249,.07); color:var(--fg); }
.mod-workspace { display:flex; flex-direction:column; flex:1; min-height:0; gap:12px; }
.drawer-tools { flex-direction:column; align-items:stretch; flex-shrink:0; gap:8px; }
.drawer-tools input { flex:auto; min-width:0; width:100%; height:34px; padding:0 12px; }
.mod-filters { display:flex; border:1px solid var(--border); border-radius:6px; padding:3px; }
.drawer-tools button { border:0; flex:1; height:26px; }
.modal-host[data-drawer="true"] .mod-manage-list { max-height:none; flex:1; gap:0; }
.mod-manage-list .mod-row { padding:12px 8px; border:0; border-top:1px solid var(--border); background:transparent; border-radius:0; flex-shrink:0; gap:12px; }
.mod-row:first-child { border-top:0; }
.mod-row-left { display:flex; flex-direction:column; gap:6px; }
.mod-row-name { font:600 12px var(--font-data); }
.mod-row-sub { font-size:12px; color:var(--muted); }
.mod-row-actions { display:flex; align-items:center; gap:12px; flex-shrink:0; }
.mod-row-actions .toggle { order:-1; }
.mod-del { border:0; width:28px; height:28px; padding:0; color:var(--muted); }
.mod-disabled-label { width:fit-content; }
.playtime-view { gap:20px; }
.pt-summary { padding:16px; }
.pt-summary .home-stat b { font-size:18px; }
.pt-grid { grid-template-columns:minmax(0,1.65fr) minmax(0,1fr); gap:20px; }
.pt-controls { gap:0; border:1px solid var(--border); border-radius:6px; padding:3px; width:fit-content; max-width:100%; margin-bottom:12px; }
.pt-controls button { height:26px; border:0; border-radius:4px; padding:0 12px; }
.pt-custom { padding:8px; }
.pt-range-label { background:var(--bg); border:1px solid var(--border); border-radius:6px; font-size:12px; padding:8px 12px; margin-bottom:2px; }
#ptError:empty { display:none; }
.treemap { height:340px; }
.cal-head { justify-content:flex-start; gap:12px; font:12px var(--font-data); color:var(--meta); }
.cal-head .ui-button { border:0; width:28px; height:28px; padding:0; }
.cal-cell { height:44px; text-align:left; padding:4px; color:var(--fg); }
.cal-cell small { font-size:10px; color:var(--fg-2); }
.cal-summary { border-top:1px solid var(--border); margin-top:16px; padding-top:12px; display:flex; flex-direction:column; gap:8px; font-size:12px; color:var(--meta); }
.cal-summary span { display:flex; justify-content:space-between; gap:8px; }
.cal-summary b { color:var(--fg); font:12px var(--font-data); }
.pt-legend { flex-direction:column; gap:0; margin-top:8px; }
.pt-legend span { padding:8px 0; border-top:1px solid var(--border); }
.pt-history { padding:8px 0; }
.pt-history p { margin-top:8px; }
@media(max-width:1040px) { .skins-view { grid-template-columns:minmax(0,1fr); } }
@media(max-width:1100px) { .content-panel { padding-left:24px; padding-right:24px; } .workspace-topbar { padding-left:24px; padding-right:24px; } }
@media(max-width:900px) { .content-grid,.settings-grid { grid-template-columns:minmax(0,1fr); } .home-bottom,.pt-grid { grid-template-columns:minmax(0,1fr); } .home-stats { grid-template-columns:repeat(2,minmax(0,1fr)); } }
@media(pointer:coarse) { .nav-item,.ui-button,.quick-open,.filter,.content-tab,.modal-btn,.skin-control,.cal-cell { min-height:44px; } }
'''
p.write_text(s,encoding='utf-8')
print('Reconciled desktop component layout rules')
