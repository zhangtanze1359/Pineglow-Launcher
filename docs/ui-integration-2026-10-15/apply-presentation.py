from pathlib import Path
import re, json
root=Path(__file__).resolve().parents[2]
p=root/'ui/js/launcher.js'
s=p.read_text(encoding='utf-8')
start=s.index('/* Previous home markup retained only as a reference for legacy DOM hooks. */')
end=s.index('async function renderInstallations()',start)
s=s[:start]+s[end:]
start=s.index("\tel.querySelector('#btnUploadSkin')?.addEventListener('click', () => {",s.index('function bindSkins'))
end=s.index("\tel.querySelector('#btnBrowseSkin')",start)
s=s[:start]+'''
 let pendingSkin = null;
 const apply = el.querySelector('#btnApplyLocalSkin');
 const modelSelect = el.querySelector('#skinModelSelect');
 const previewLocal = () => {
   if (!pendingSkin) return;
   const slim = modelSelect && modelSelect.value === 'slim';
   const local = { url: pendingSkin.dataUrl, model: slim ? 'slim' : 'classic' };
   if (viewer) viewer.setSkin(local, null);
   const name = el.querySelector('#activeSkinName'), model = el.querySelector('#activeSkinModel');
   if (name) name.textContent = pendingSkin.name;
   if (model) model.textContent = t(slim ? 'skin.slimModel' : 'skin.classicModel');
   const texture = el.querySelector('#localSkinTexture');
   if (texture) { texture.src = pendingSkin.dataUrl; texture.hidden = false; }
   const label = el.querySelector('#localSkinFileName');
   if (label) label.textContent = pendingSkin.name;
   if (apply) apply.disabled = !active || active.type !== 'microsoft';
 };
 modelSelect?.addEventListener('change', previewLocal);
 el.querySelector('#btnUploadSkin')?.addEventListener('click', () => {
   const input = document.createElement('input');
   input.type = 'file'; input.accept = 'image/png';
   input.onchange = () => {
     const f = input.files && input.files[0];
     if (!f) return;
     if (!/\\.png$/i.test(f.name) || (f.type && f.type !== 'image/png') || f.size > 1024 * 1024) { toast(t('skin.invalidFile')); return; }
     const reader = new FileReader();
     reader.onerror = () => toast(t('skin.invalidFile'));
     reader.onload = () => {
       if (!el.isConnected) return;
       const dataUrl = String(reader.result || ''), image = new Image();
       image.onerror = () => toast(t('skin.invalidFile'));
       image.onload = () => {
         if (!el.isConnected) return;
         if (image.width !== 64 || (image.height !== 64 && image.height !== 32)) { toast(t('skin.invalidDimensions')); return; }
         pendingSkin = { dataUrl, name: f.name }; previewLocal();
         toast(t('skin.preview', { name: f.name }));
       };
       image.src = dataUrl;
     };
     reader.readAsDataURL(f);
   };
   input.click();
 });
 apply?.addEventListener('click', async () => {
   if (!pendingSkin || apply.disabled) return;
   if (!active || active.type !== 'microsoft') { toast(t('skin.needMicrosoft')); return; }
   apply.disabled = true;
   try {
     await SG.api.uploadSkin(active.id, pendingSkin.dataUrl.replace(/^data:[^;]+;base64,/, ''), modelSelect?.value === 'slim');
     toast(t('skin.uploaded')); await loadAccounts(true); invalidate('skins');
     if (currentView === 'skins') await goTo('skins');
   } catch (err) { toast(t('skin.uploadFailed') + (err.message || t('common.unknownError'))); }
   finally { if (apply.isConnected) apply.disabled = false; }
 });
''' + s[end:]
# Selecting an account-owned texture must clear any local upload pending state.
needle="\t\t\tconst skin = skins.find(s => s.id === c.dataset.skin);"
s=s.replace(needle,needle+"\n\t\t\tpendingSkin = null; if (apply) apply.disabled = true;\n\t\t\tconst texture = el.querySelector('#localSkinTexture'); if (texture) texture.hidden = true;")
p.write_text(s,encoding='utf-8',newline='\r\n')

# Product copy remains bilingual; no prototype data flows into production.
zh={
'nav.group.system':'系统','nav.playtime':'游戏时长','shell.notSignedIn':'未登录','shell.newUi':'新版界面','shell.quick':'快速操作','shell.open':'打开','shell.homeSub':'选择实例，开始你的下一段旅程','shell.settingsSub':'账户、运行环境与启动器偏好','shell.welcome':'准备好，进入方块世界','shell.selectInstance':'选择实例','shell.memory':'内存分配','shell.queue':'下载队列','shell.allTasks':'全部任务','shell.noTasks':'暂无下载任务','shell.queueFailed':'暂时无法读取队列，可前往下载页重试。','shell.more':'管理 {name}','shell.commandSearch':'搜索页面或操作…','shell.commandHint':'↑ ↓ 选择 · Enter 执行 · Esc 关闭','shell.noCommands':'没有匹配的操作',
'skin.chooseFile':'选择本地皮肤','skin.previewFirst':'先预览皮肤，确认后再单独应用到账户。','skin.applyAccount':'应用到账户','skin.microsoftOnly':'本地预览适用于所有账户；应用需要微软账户。','skin.invalidDimensions':'皮肤必须是 64×64 或 64×32 的 PNG 图片。',
'pt.subtitle':'查看真实游玩记录、时长分布与日历','pt.total':'累计游玩时长','pt.count':'游玩次数','pt.instances':'实例','pt.recorded':'已记录会话','pt.historyNote':'累计值保留已有统计。日历从本次更新后正常结束的游戏开始记录；启动器提前退出的会话不会计入，旧时长无法还原为逐日记录。','pt.range':'统计范围','pt.range.week':'本周','pt.range.month':'本月','pt.range.7':'近 7 天','pt.range.30':'近 30 天','pt.range.custom':'自定义','pt.from':'开始','pt.to':'结束','pt.apply':'应用范围','pt.clear':'清除日期筛选','pt.chart':'时长分布','pt.hint':'悬停或聚焦色块，查看日期、实例与时长。','pt.previous':'上个月','pt.next':'下个月','pt.calendarNote':'底部填充高度表示当日时长；点击有记录的日期筛选当前范围。','pt.weekdays':'一,二,三,四,五,六,日','pt.empty':'此范围没有已记录的游玩会话。','pt.loadFailed':'无法读取游戏时长','pt.invalidRange':'请填写有效日期，且开始日期不能晚于结束日期。','pt.longRange':'自定义范围最多为 120 天，上一次有效图表已保留。','pt.drawFailed':'时长图绘制失败：',
'mod.search':'搜索 Mod…','mod.filter.all':'全部','mod.filter.enabled':'已启用','mod.filter.disabled':'已禁用','mod.queryFailed':'无法读取 Mod 列表','mod.recoveryOpen':'恢复区','mod.inlineRemove':'移入恢复区后可恢复，不会立即永久删除。','mod.confirmRemove':'移入恢复区','mod.noMatch':'没有匹配的 Mod',
}
en={
'nav.group.system':'SYSTEM','nav.playtime':'Play time','shell.notSignedIn':'Not signed in','shell.newUi':'New interface','shell.quick':'Quick actions','shell.open':'Open','shell.homeSub':'Choose an instance and start your next adventure','shell.settingsSub':'Accounts, runtime and launcher preferences','shell.welcome':'Ready for your next world','shell.selectInstance':'Choose instance','shell.memory':'Allocated memory','shell.queue':'Download queue','shell.allTasks':'All tasks','shell.noTasks':'No download tasks','shell.queueFailed':'Unable to read the queue. Open Downloads to retry.','shell.more':'Manage {name}','shell.commandSearch':'Search pages or actions…','shell.commandHint':'↑ ↓ Select · Enter Run · Esc Close','shell.noCommands':'No matching actions',
'skin.chooseFile':'Choose local skin','skin.previewFirst':'Preview a skin, then apply it to your account separately.','skin.applyAccount':'Apply to account','skin.microsoftOnly':'Local preview works with all accounts. Applying requires a Microsoft account.','skin.invalidDimensions':'Use a 64×64 or 64×32 PNG skin.',
'pt.subtitle':'Real play sessions, time distribution and calendar','pt.total':'Total play time','pt.count':'Play count','pt.instances':'Instances','pt.recorded':'Recorded sessions','pt.historyNote':'Existing totals are retained. The calendar records games that finish normally after this update, while the launcher remains open. Older totals cannot be reconstructed into daily sessions.','pt.range':'Time range','pt.range.week':'This week','pt.range.month':'This month','pt.range.7':'Last 7 days','pt.range.30':'Last 30 days','pt.range.custom':'Custom','pt.from':'From','pt.to':'To','pt.apply':'Apply range','pt.clear':'Clear date filter','pt.chart':'Play time distribution','pt.hint':'Hover or focus a block to inspect its date, instance and duration.','pt.previous':'Previous month','pt.next':'Next month','pt.calendarNote':'Bar height represents daily play time. Select a recorded day to filter the current range.','pt.weekdays':'M,T,W,T,F,S,S','pt.empty':'No recorded play sessions in this range.','pt.loadFailed':'Unable to load play time','pt.invalidRange':'Enter valid dates with the start on or before the end.','pt.longRange':'Choose up to 120 days. The last valid chart is retained.','pt.drawFailed':'Unable to draw chart: ',
'mod.search':'Search mods…','mod.filter.all':'All','mod.filter.enabled':'Enabled','mod.filter.disabled':'Disabled','mod.queryFailed':'Unable to load mods','mod.recoveryOpen':'Recovery','mod.inlineRemove':'Move this file to recovery so it can be restored later.','mod.confirmRemove':'Move to recovery','mod.noMatch':'No matching mods',
}
p=root/'ui/js/i18n.js'; s=p.read_text(encoding='utf-8'); marker='/* ---------------- 语言状态 ---------------- */'
s=s.replace(marker,'Object.assign(DICT.zh, '+json.dumps(zh,ensure_ascii=False,indent=2)+');\nObject.assign(DICT.en, '+json.dumps(en,ensure_ascii=False,indent=2)+');\n\n'+marker)
p.write_text(s,encoding='utf-8',newline='\r\n')

p=root/'ui/assets/pine-forest.svg';s=p.read_text(encoding='utf-8'); colors={'#29494c':'#111214','#658d78':'#292023','#afbb91':'#827579','#e2c48e':'#ff6363','#d1c097':'#ff6363','#547569':'#28292b','#3a6158':'#1b1c1e','#24493f':'#111214','#16362f':'#090a0c'}
for a,b in colors.items():s=s.replace(a,b)
(root/'ui/assets/pine-forest-night.svg').write_text(s,encoding='utf-8')
