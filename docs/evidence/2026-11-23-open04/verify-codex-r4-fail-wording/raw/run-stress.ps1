$runner = Join-Path $PSScriptRoot 'run-review.ps1'
$load = Join-Path $PSScriptRoot 'synthetic-load.py'
$ready = Join-Path $env:TEMP 'load-ready'
New-Item -ItemType Directory -Path $ready | Out-Null
$workers = @()
try {
    Write-Output "READY_DIR=$ready"
    $python = (Get-Command python.exe).Source
    for ($i = 1; $i -le 9; $i++) {
        $mode = if ($i -le 8) { 'cpu' } else { 'io' }
        $worker = Start-Process -FilePath $python -ArgumentList @('"'+$load+'"', $mode, $i, $ready) -WindowStyle Hidden -PassThru
        $workers += $worker
        Write-Output "START worker=$i mode=$mode pid=$($worker.Id)"
    }
    $deadline = (Get-Date).AddSeconds(25)
    do {
        $markers = @(Get-ChildItem -LiteralPath $ready -Filter '*.ready')
        if ($markers.Count -eq 9) { break }
        if ((Get-Date) -gt $deadline) { throw "Only $($markers.Count)/9 workers initialized" }
        Start-Sleep -Milliseconds 200
    } while ($true)
    function Write-WorkerSnapshot($label) {
        Write-Output "$label $((Get-Date).ToString('o'))"
        $workers | ForEach-Object {
            $_.Refresh()
            Write-Output "pid=$($_.Id) exited=$($_.HasExited) cpu_seconds=$($_.TotalProcessorTime.TotalSeconds)"
        }
        Get-Content -LiteralPath (Join-Path $ready 'io-iterations.txt')
    }
    Write-WorkerSnapshot 'BEFORE_TEST'
    & $runner -StressOnly
    Write-WorkerSnapshot 'AFTER_TEST'
}
finally {
    foreach ($worker in $workers) {
        if (!$worker.HasExited) {
            Stop-Process -InputObject $worker -Force
            $worker.WaitForExit()
        }
        Write-Output "STOP pid=$($worker.Id) exited=$($worker.HasExited)"
    }
}

