# mirror-gitee-manifest.ps1 —— 在 Gitee 源码镜像之前备份 / 之后恢复 update.json
#
# 为什么需要它（2026-11-24 事故）：
#   `update.json` 曾被纳入版本控制，而 mirror-gitee.yml 是**整分支强制推送**。
#   一次镜像用仓库里的旧清单（1.0.6）覆盖了 Gitee 上刚由 release.yml 更新的
#   新清单（1.0.7），导致更新链退回旧版本。
#
# 修复思路：
#   1. update.json 已移出版本控制（.gitignore 覆盖），不再随分支推送
#   2. 但整分支 force push 仍会**删除**远端这个未跟踪的文件
#   3. 所以镜像前把它读到本地临时文件，镜像后通过 contents API 原样写回
#
# 用法：
#   Backup 模式：  ./mirror-gitee-manifest.ps1 -Mode backup -Token $env:GITEE_TOKEN -BackupPath $p
#   Restore 模式： ./mirror-gitee-manifest.ps1 -Mode restore -Token $env:GITEE_TOKEN -BackupPath $p
#
# 安全性：
#   * Token 只从参数传入（CI 用 env 提供），不写进日志、不写进文件
#   * 只打印版本号与 sha，不打印清单全文（虽然清单本身是公开的，但保持克制）
#
# 退出码：0 = 成功或无需操作；1 = 应视为失败（恢复失败）

param(
  [Parameter(Mandatory = $true)][ValidateSet('backup', 'restore')][string]$Mode,
  [Parameter(Mandatory = $true)][string]$Token,
  [Parameter(Mandatory = $true)][string]$BackupPath
)

$ErrorActionPreference = 'Stop'
$RepoApi = 'https://gitee.com/api/v5/repos/ztz1359/pineglow-launcher'
$UserAgent = 'pineglow-mirror'

function Invoke-GiteeGet([string]$url) {
  $req = [System.Net.HttpWebRequest]::Create($url)
  $req.Method = 'GET'
  $req.UserAgent = $UserAgent
  $req.Timeout = 30000
  try {
    $resp = $req.GetResponse()
    $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
    $body = $reader.ReadToEnd()
    $reader.Close(); $resp.Close()
    return @{ Ok = $true; Body = $body }
  } catch [System.Net.WebException] {
    $code = 0
    if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
    return @{ Ok = $false; Code = $code; Body = '' }
  }
}

function Invoke-GiteeWrite([string]$url, [string]$method, [string]$jsonBody) {
  $req = [System.Net.HttpWebRequest]::Create($url)
  $req.Method = $method
  $req.UserAgent = $UserAgent
  $req.ContentType = 'application/json'
  $req.Timeout = 30000
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonBody)
  $req.ContentLength = $bytes.Length
  $stream = $req.GetRequestStream()
  $stream.Write($bytes, 0, $bytes.Length)
  $stream.Close()
  try {
    $resp = $req.GetResponse()
    return @{ Ok = $true; Code = [int]$resp.StatusCode }
  } catch [System.Net.WebException] {
    $code = 0
    if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
    return @{ Ok = $false; Code = $code }
  }
}

if ($Mode -eq 'backup') {
  Write-Host '--- 备份 Gitee 上的 update.json（若有）---'

  $url = "$RepoApi/contents/update.json?ref=master&access_token=$Token"
  $r = Invoke-GiteeGet $url

  if (-not $r.Ok) {
    Write-Host "  查询返回 HTTP $($r.Code)，跳过备份"
    exit 0
  }

  # Gitee 对不存在的文件返回 200 + 空数组；要判断是否真有 sha 字段
  if ($r.Body -notmatch '"sha"') {
    Write-Host '  远端无 update.json（响应无 sha 字段），跳过备份'
    exit 0
  }

  $meta = $r.Body | ConvertFrom-Json
  $contentText = [System.Text.Encoding]::UTF8.GetString(
    [System.Convert]::FromBase64String(($meta.content -replace '\s', '')))

  [System.IO.File]::WriteAllText($BackupPath, $contentText, (New-Object System.Text.UTF8Encoding $false))

  # 同时把 sha 存到旁路文件，restore 时用（Gitee 更新已存在文件需要 sha）
  [System.IO.File]::WriteAllText("$BackupPath.sha", [string]$meta.sha, (New-Object System.Text.UTF8Encoding $false))

  $ver = '?'
  try { $ver = ($contentText | ConvertFrom-Json).version } catch { }
  Write-Host "  已备份：version=$ver  sha=$($meta.sha.Substring(0, [Math]::Min(12, $meta.sha.Length)))"
  exit 0
}

if ($Mode -eq 'restore') {
  if (-not (Test-Path $BackupPath)) {
    Write-Host '  没有备份文件，跳过恢复（远端原本就没有 update.json）'
    exit 0
  }

  Write-Host '--- 恢复 Gitee 上的 update.json ---'

  $content = [System.IO.File]::ReadAllText($BackupPath)
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($content))

  $shaPath = "$BackupPath.sha"
  $sha = ''
  if (Test-Path $shaPath) { $sha = (Get-Content $shaPath -Raw).Trim() }

  $body = @{ access_token = $Token; content = $b64; message = 'mirror: restore update.json' }
  if ($sha) {
    $body['sha'] = $sha
    $method = 'PUT'
  } else {
    $method = 'POST'
  }

  $res = Invoke-GiteeWrite "$RepoApi/contents/update.json" $method ($body | ConvertTo-Json -Compress)
  if (-not $res.Ok) {
    Write-Host "::error::[镜像失败] 恢复 update.json 失败 HTTP $($res.Code)"
    exit 1
  }

  $ver = '?'
  try { $ver = ($content | ConvertFrom-Json).version } catch { }
  Write-Host "  已恢复：version=$ver  （HTTP $($res.Code)）"
  exit 0
}
