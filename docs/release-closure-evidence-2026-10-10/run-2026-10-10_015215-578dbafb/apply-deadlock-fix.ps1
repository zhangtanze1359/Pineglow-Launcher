param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-29: remove the "sync-over-async on the UI thread" deadlock (defect #11) in an ISOLATED copy.
#
# ConfigStore.LoadAsync() captures the SynchronizationContext on its ONE real await
# (File.ReadAllTextAsync). ProductionConfigStore.Load() blocks on it with GetAwaiter().GetResult(),
# so calling LoadConfig() from a thread that HAS a SynchronizationContext (the Avalonia UI thread)
# deadlocks: the continuation is posted back to the thread that is blocked waiting for it.
# SaveAsync()'s only await is a completed Task, but it is given the same treatment for consistency.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$S = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services'
$report = New-Object System.Collections.Generic.List[object]

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
  $report.Add([pscustomobject]@{ label = $label; file = $file.Substring($Root.Length + 1); ok = $true })
  Write-Output "[OK] $label"
}

$cfg = Join-Path $S 'ConfigStore.cs'

# 1. the real one: this await is what makes the whole call chain context-capturing
$old1 = @'
			string json = await File.ReadAllTextAsync(configPath);
'@
$new1 = @'
			// FIX-DEADLOCK: ProductionConfigStore.Load() blocks on this method with
			// GetAwaiter().GetResult(). Without ConfigureAwait(false) the continuation is posted back
			// to the caller's SynchronizationContext — on the Avalonia UI thread that is the thread
			// blocked waiting for the result, i.e. a deadlock (reproduced in round 28).
			string json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
'@
Patch $cfg $old1 $new1 'P-DEADLOCK 1/3 LoadAsync reads the config off-context'

# 2. the migration path re-saves through SaveAsync
$old2 = @'
					await SaveAsync(config);
'@
$new2 = @'
					await SaveAsync(config).ConfigureAwait(false);
'@
Patch $cfg $old2 $new2 'P-DEADLOCK 2/3 LoadAsync migration save'

# 3. SaveAsync's placeholder await (SaveCore is fully synchronous)
$old3 = @'
		await Task.CompletedTask;
'@
$new3 = @'
		await Task.CompletedTask.ConfigureAwait(false);
'@
Patch $cfg $old3 $new3 'P-DEADLOCK 3/3 SaveAsync placeholder await'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'deadlock-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
