param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-105: defect #1's UI entry -- the last piece that makes the loader-install backend reachable.
#
# Record 104 established that this spans THREE files (the i18n one was easy to miss: every visible
# string in this UI goes through t('key'), so a new button without keys would render raw key names).
#
# ORDER: the i18n anchors below are the POST-P-JAVALABEL text, so apply-java-label.ps1 must run first.
#
# VERIFICATION BOUNDARY: this patch is validated by static checks only (apply + build + Node suite +
# UI resource closure + cold start, which re-checks the embedded UI byte-for-byte). The button's
# RUNTIME behaviour in a real browser is NOT verified here.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$report = New-Object System.Collections.Generic.List[object]
$U = Join-Path $Root 'ui\js'
$L = [char]10

function Write-PreservingEncoding([string]$file, [string]$text) {
  $bytes = [IO.File]::ReadAllBytes($file)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($hasBom)))
}

function Patch([string]$file, [string]$old, [string]$new, [string]$label) {
  $text = [IO.File]::ReadAllText($file)
  $usesCrlf = $text.Contains("`r`n")
  if ($usesCrlf) { $old = $old -replace "`r?`n", "`r`n"; $new = $new -replace "`r?`n", "`r`n" }
  else { $old = $old -replace "`r?`n", "`n"; $new = $new -replace "`r?`n", "`n" }
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) { throw "[$label] expected exactly 1 match in $file but found $count" }
  Write-PreservingEncoding $file $text.Replace($old, $new)
  $report.Add([pscustomobject]@{ label = $label; ok = $true })
  Write-Output "[OK] $label"
}

# ---- 1. api.js: the call
$api = Join-Path $U 'api.js'
$old1 = @(
'		async getInstances() {',
'			const data = await _req(''/api/instances'');',
'			return (data.instances || []).map(i => mapInstance(i, data.selectedInstanceId));',
'		},'
) -join $L
$new1 = @(
'		async getInstances() {',
'			const data = await _req(''/api/instances'');',
'			return (data.instances || []).map(i => mapInstance(i, data.selectedInstanceId));',
'		},',
'		// FIX-LOADERUI：为已存在的实例安装加载器（缺陷 1 的前端入口）。',
'		// 后端路由 POST /api/instances/{id}/loader 已端到端验证（记录 99）；这里只是把它接出来。',
'		async installLoader(instanceId, loaderType, loaderVersion) {',
'			return await _post(''/api/instances/'' + encodeURIComponent(instanceId) + ''/loader'',',
'				{ loaderType, loaderVersion });',
'		},'
) -join $L
Patch $api $old1 $new1 'P-LOADERUI 1/4 api.js: installLoader()'

# ---- 2. launcher.js: the button on each instance card
$ljs = Join-Path $U 'launcher.js'
$old2 = @(
'			<div class="inst-actions">',
'				<button class="inst-play ${isSel ? ''primary'' : ''ghost''}" data-play="${esc(i.id)}">${esc(t(''inst.play''))}</button>'
) -join $L
$new2 = @(
'			<div class="inst-actions">',
'				<button class="inst-play ${isSel ? ''primary'' : ''ghost''}" data-play="${esc(i.id)}">${esc(t(''inst.play''))}</button>',
'				<button class="inst-more" data-loader="${esc(i.id)}" title="${esc(t(''inst.installLoader''))}" aria-label="${esc(t(''inst.installLoader''))}">${esc(t(''inst.installLoader''))}</button>'
) -join $L
Patch $ljs $old2 $new2 'P-LOADERUI 2/4 launcher.js: the button'

# ---- 3. launcher.js: wire it up (mirrors the existing btnAddExisting modal)
$old3 = @(
'	el.querySelector(''#btnNewInstall'')?.addEventListener(''click'', async () => {'
) -join $L
$new3 = @(
'	// FIX-LOADERUI：安装加载器（缺陷 1 的前端入口）。结构照抄上面的 btnAddExisting 弹窗。',
'	el.querySelectorAll(''[data-loader]'').forEach(btn => btn.addEventListener(''click'', () => {',
'		const iid = btn.dataset.loader;',
'		openModal(`',
'			<div class="modal-title">${esc(t(''inst.installLoaderTitle''))}</div>',
'			<div style="font-size:12px;color:var(--muted,#8b91a1);margin-bottom:10px">${esc(t(''inst.installLoaderHint''))}</div>',
'			<div class="modal-field"><span class="modal-label">${esc(t(''inst.installLoaderType''))}</span>',
'				<select class="modal-input" id="mLoaderType">',
'					<option value="Fabric">Fabric</option>',
'					<option value="Quilt">Quilt</option>',
'					<option value="Forge">Forge</option>',
'					<option value="NeoForge">NeoForge</option>',
'				</select></div>',
'			<div class="modal-field"><span class="modal-label">${esc(t(''inst.installLoaderVersion''))}</span>',
'				<input class="modal-input" id="mLoaderVersion" placeholder="0.19.5"></div>',
'			<div class="modal-actions">',
'				<button class="modal-btn ghost" id="mCancel">${esc(t(''common.cancel''))}</button>',
'				<button class="modal-btn primary" id="mOk">${esc(t(''inst.installLoaderSubmit''))}</button>',
'			</div>`);',
'		document.getElementById(''mCancel'').onclick = closeModal;',
'		document.getElementById(''mOk'').onclick = async () => {',
'			const type = document.getElementById(''mLoaderType'').value;',
'			const version = (document.getElementById(''mLoaderVersion'').value || '''').trim();',
'			if (!version) { toast(t(''inst.installLoaderVersionRequired'')); return; }',
'			try {',
'				await SG.api.installLoader(iid, type, version);',
'				closeModal();',
'				await loadState(true);',
'				invalidate(''home'');',
'				const v = document.getElementById(''view-installations'');',
'				v.innerHTML = await renderInstallations(); bindInstallations(v);',
'				toast(t(''inst.installLoaderDone''));',
'			} catch (err) { toast(t(''inst.installLoaderFailed'') + (err.message || t(''common.unknownError''))); }',
'		};',
'	}));',
'	el.querySelector(''#btnNewInstall'')?.addEventListener(''click'', async () => {'
) -join $L
Patch $ljs $old3 $new3 'P-LOADERUI 3/4 launcher.js: wire the button'

# ---- 4. i18n.js: zh + en keys (anchored on the post-P-JAVALABEL text)
$i18n = Join-Path $U 'i18n.js'
$old4 = @(
"	'set.javaBundled': '未检测到 Java，请指定路径',"
) -join $L
$new4 = @(
"	'set.javaBundled': '未检测到 Java，请指定路径',",
"	'inst.installLoader': '安装加载器',",
"	'inst.installLoaderTitle': '为该实例安装加载器',",
"	'inst.installLoaderHint': '安装会下载所选加载器及其库文件；原版实例还需补齐该版本的资源。',",
"	'inst.installLoaderType': '加载器',",
"	'inst.installLoaderVersion': '加载器版本',",
"	'inst.installLoaderSubmit': '开始安装',",
"	'inst.installLoaderVersionRequired': '请填写加载器版本',",
"	'inst.installLoaderDone': '加载器安装完成',",
"	'inst.installLoaderFailed': '安装加载器失败：',"
) -join $L
Patch $i18n $old4 $new4 'P-LOADERUI 4/4a i18n.js: zh keys'

$old5 = @(
"	'set.javaBundled': 'No Java detected - please set a path',"
) -join $L
$new5 = @(
"	'set.javaBundled': 'No Java detected - please set a path',",
"	'inst.installLoader': 'Install loader',",
"	'inst.installLoaderTitle': 'Install a loader for this instance',",
"	'inst.installLoaderHint': 'The install downloads the chosen loader and its libraries; a vanilla instance also needs that version of the assets.',",
"	'inst.installLoaderType': 'Loader',",
"	'inst.installLoaderVersion': 'Loader version',",
"	'inst.installLoaderSubmit': 'Install',",
"	'inst.installLoaderVersionRequired': 'Enter a loader version',",
"	'inst.installLoaderDone': 'Loader installed',",
"	'inst.installLoaderFailed': 'Loader install failed: ',"
) -join $L
Patch $i18n $old5 $new5 'P-LOADERUI 4/4b i18n.js: en keys'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'loaderui-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
