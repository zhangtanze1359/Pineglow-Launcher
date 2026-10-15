param(
  [Parameter(Mandatory)][string]$FixtureRoot,
  [Parameter(Mandatory)][string]$OutDir,
  [int]$StartupTimeoutSec = 120,
  [int]$RenderWaitSec = 12
)
# Round-19 probe (own tool; product source untouched).
# Verifies the REAL host: starts the shipped single-file EXE with a NORMAL window and captures
# the actual WebView2 window contents from the desktop, then closes it.
# This is the piece a headless-Chromium probe cannot cover (same engine, different host).
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinCap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static string Title(IntPtr h) {
    int n = GetWindowTextLength(h); if (n <= 0) return "";
    var sb = new System.Text.StringBuilder(n + 2); GetWindowText(h, sb, sb.Capacity); return sb.ToString();
  }
}
'@
[void][WinCap]::SetProcessDPIAware()

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function OwnedProcs([string]$exePath) {
  @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $exePath) })
}

$cold = $FixtureRoot
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
$apiPort = Get-FreePort

$result = [ordered]@{
  probe = 'native-window-capture'; fixture = $cold; exe = $targetExe
  apiPort = $apiPort; healthReady = $false
  windowFound = $false; windowTitle = ''; windowHandle = 0
  windowRect = ''; visible = $false
  methodPrintWindow = $false; methodScreenBitBlt = $false
  pngPrintWindow = ''; pngScreen = ''
  bytesPrintWindow = 0; bytesScreen = 0
  nonBlackRatioPrintWindow = 0.0; nonBlackRatioScreen = 0.0
  ownedProcessesStopped = $false
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'
$env:PINEGLOW_API_PORT = "$apiPort"

$proc = $null
try {
  # NORMAL window (not hidden): WebView2 only composes when the window is really shown
  $proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru

  $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try {
      $h = Invoke-WebRequest "http://127.0.0.1:$apiPort/" -UseBasicParsing -TimeoutSec 5
      if ($h.Content -match 'name="pineglow-session-token"') { $result.healthReady = $true; break }
    } catch { }
  }

  # wait for the real window handle to appear
  $hwnd = [IntPtr]::Zero
  $deadline = (Get-Date).AddSeconds(60)
  while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero -and [WinCap]::IsWindowVisible($proc.MainWindowHandle)) {
      $hwnd = $proc.MainWindowHandle
    }
  }
  if ($hwnd -eq [IntPtr]::Zero) { throw 'no visible main window handle appeared' }

  $result.windowFound = $true
  $result.windowHandle = [int64]$hwnd
  $result.windowTitle = [WinCap]::Title($hwnd)
  $result.visible = [WinCap]::IsWindowVisible($hwnd)

  [void][WinCap]::ShowWindow($hwnd, 9)      # SW_RESTORE
  [void][WinCap]::SetForegroundWindow($hwnd)
  # let WebView2 finish its first paint + the SPA finish its API round-trips
  Start-Sleep -Seconds $RenderWaitSec

  $r = New-Object WinCap+RECT
  [void][WinCap]::GetWindowRect($hwnd, [ref]$r)
  $w = $r.Right - $r.Left; $h2 = $r.Bottom - $r.Top
  $result.windowRect = "$($r.Left),$($r.Top),$w x $h2"
  if ($w -le 0 -or $h2 -le 0) { throw "bad window rect $($result.windowRect)" }

  function Measure-NonBlack([System.Drawing.Bitmap]$bmp) {
    $step = 7; $tot = 0; $non = 0
    for ($y = 0; $y -lt $bmp.Height; $y += $step) {
      for ($x = 0; $x -lt $bmp.Width; $x += $step) {
        $tot++
        $c = $bmp.GetPixel($x, $y)
        if (($c.R + $c.G + $c.B) -gt 24) { $non++ }
      }
    }
    if ($tot -eq 0) { return 0.0 }
    return [math]::Round($non / $tot, 4)
  }

  # method 1: PrintWindow with PW_RENDERFULLCONTENT (2) -- captures DirectComposition surfaces
  try {
    $bmp = New-Object System.Drawing.Bitmap($w, $h2)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [WinCap]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    if ($ok) {
      $p1 = Join-Path $OutDir 'real-window-printwindow.png'
      $bmp.Save($p1, [System.Drawing.Imaging.ImageFormat]::Png)
      $result.methodPrintWindow = $true
      $result.pngPrintWindow = $p1
      $result.bytesPrintWindow = (Get-Item $p1).Length
      $result.nonBlackRatioPrintWindow = Measure-NonBlack $bmp
    }
    $bmp.Dispose()
  } catch { $result.printWindowError = $_.Exception.Message }

  # method 2: screen BitBlt of the window rect (works when the window is really on screen)
  try {
    $bmp2 = New-Object System.Drawing.Bitmap($w, $h2)
    $g2 = [System.Drawing.Graphics]::FromImage($bmp2)
    $g2.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h2)))
    $g2.Dispose()
    $p2 = Join-Path $OutDir 'real-window-screen.png'
    $bmp2.Save($p2, [System.Drawing.Imaging.ImageFormat]::Png)
    $result.methodScreenBitBlt = $true
    $result.pngScreen = $p2
    $result.bytesScreen = (Get-Item $p2).Length
    $result.nonBlackRatioScreen = Measure-NonBlack $bmp2
    $bmp2.Dispose()
  } catch { $result.screenError = $_.Exception.Message }
}
catch { $result.error = $_.Exception.Message }
finally {
  if ($proc -and -not $proc.HasExited) {
    try { [void]$proc.CloseMainWindow() } catch { }
    Start-Sleep -Seconds 2
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  }
  foreach ($o in (OwnedProcs $targetExe)) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 1
  $result.ownedProcessesStopped = (@(OwnedProcs $targetExe).Count -eq 0)
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'real-window-summary.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.windowFound) { exit 2 }
if (-not $result.methodPrintWindow -and -not $result.methodScreenBitBlt) { exit 3 }
exit 0
