param([string]$Target,[string]$JsonPath)
$tokens=$null
$parseErrors=$null
$null=[System.Management.Automation.Language.Parser]::ParseFile($Target,[ref]$tokens,[ref]$parseErrors)
[pscustomobject]@{ version=$PSVersionTable.PSVersion.ToString(); target=$Target; errorCount=@($parseErrors).Count; errors=@($parseErrors | ForEach-Object { [pscustomobject]@{id=$_.ErrorId;message=$_.Message;line=$_.Extent.StartLineNumber} }) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $JsonPath -Encoding utf8
