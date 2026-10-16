param([string]$ProjectRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$files=@(git -C $ProjectRoot ls-files --cached)
if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) {throw 'Stage the explicit public file inventory before running this check.'}
$blocked='(?i)(^|/)(installer|\.analysis|\.codex|\.agents|data|bin|obj|packages|\.vs|\.idea|\.psconverter-work)(/|$)|\.(exe|dll|msi|cab|zip|7z|mp3|wav|at3|at9|mp4|pfx|p12|pem|key|log|tmp|pending)$|(^|/)\.env($|\.)'
$secret='gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|xox[baprs]-[A-Za-z0-9-]{20,}'
$privatePath='(?i)[a-z]:[\\/](?:Users|Documents and Settings)[\\/][a-z0-9_.-]+[\\/]|/(?:Users|home)/[a-z0-9_.-]+/'
foreach ($relative in $files) {
    if ($relative -match $blocked) {throw "Nonpublic file is staged: $relative"}
    $path=Join-Path $ProjectRoot $relative
    if ((Get-Item -LiteralPath $path).Length -gt 20MB) {throw "Unexpectedly large public file: $relative"}
    if ([IO.Path]::GetExtension($relative) -in @('.png','.ico')) {continue}
    $text=[IO.File]::ReadAllText($path)
    if ($text -match $secret) {throw "Possible credential material in staged file: $relative"}
    if ($text -match $privatePath) {throw "Personal filesystem path in staged file: $relative"}
}
Write-Output ("PASS public inventory: $($files.Count) files; no blocked artifacts, recognized secret patterns or personal filesystem paths.")
