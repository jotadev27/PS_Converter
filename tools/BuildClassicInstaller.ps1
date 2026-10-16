param(
    [string]$ProjectRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$PortableArchive,
    [string]$InnoCompiler,
    [switch]$Verify,
    [switch]$UpdateRelease
)
$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot)
if (-not $PortableArchive) {$PortableArchive=Join-Path $ProjectRoot 'installer\PS_Converter-v1.0-portable.zip'}
if (-not $InnoCompiler) {$InnoCompiler=Join-Path $ProjectRoot '.analysis\inno\compiler\ISCC.exe'}
if (-not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {throw 'An authorized Inno Setup compiler is required. Supply -InnoCompiler; no dependency is downloaded or installed automatically.'}
$signature=Get-AuthenticodeSignature -LiteralPath $InnoCompiler
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.|Jordan Russell') {throw 'The Inno Setup compiler must have a valid official publisher signature.'}
$portableHash=(Get-FileHash -LiteralPath $PortableArchive -Algorithm SHA256).Hash
$stage=Join-Path $ProjectRoot ('.analysis\classic-installer\'+[guid]::NewGuid().ToString('N'))
$payload=Join-Path $stage 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($PortableArchive)
$inventory=@();$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$total=0L
try {
    if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 128) {throw 'Unexpected portable package inventory.'}
    foreach ($entry in $archive.Entries) {
        $name=$entry.FullName
        # Treat ZIP names as data: reject Windows paths, traversal, collisions and script delimiters.
        $total+=$entry.Length
        if ($name -notmatch '^[A-Za-z0-9_./ -]+$' -or $name -match '(^/|/$|//|[. ](/|$)|(^|/)(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])([./]|$))' -or -not $names.Add($name) -or $entry.Length -gt 256MB -or $total -gt 512MB) {throw 'Unsafe portable package entry.'}
        $destination=[IO.Path]::GetFullPath((Join-Path $payload $name))
        if (-not $destination.StartsWith($payload+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Portable entry escaped its staging folder.'}
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$false)
        $inventory += [PSCustomObject]@{Name=$name;Length=$entry.Length;Sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash}
    }
} finally {$archive.Dispose()}
foreach ($required in @('PS_converter.exe','LICENSE','docs/THIRD_PARTY.md')) {
    if (-not $names.Contains($required)) {throw "Missing installer input: $required"}
}
$inventory | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $stage 'package-manifest.json') -Encoding UTF8
$entries=foreach ($file in $inventory) {
    $relative=$file.Name.Replace('/','\')
    $subdirectory=Split-Path -Parent $relative
    $destination=if ($subdirectory) {'{app}\'+$subdirectory} else {'{app}'}
    'Source: "'+(Join-Path $payload $relative)+'"; DestDir: "'+$destination+'"; Flags: ignoreversion'
}
[IO.File]::WriteAllLines((Join-Path $stage 'payload.iss'),[string[]]$entries,[Text.UTF8Encoding]::new($true))
$terms=[IO.File]::ReadAllText((Join-Path $payload 'LICENSE'))+"`r`n`r`nTHIRD-PARTY NOTICES`r`n`r`n"+[IO.File]::ReadAllText((Join-Path $payload 'docs\THIRD_PARTY.md'))
[IO.File]::WriteAllText((Join-Path $stage 'license-terms.txt'),$terms.Replace("`r`n","`n").Replace("`n","`r`n"),[Text.UTF8Encoding]::new($true))
$artworkBuilder=Join-Path $stage 'SetupArtwork.exe'
$csharp=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csharp /nologo /target:exe /warn:4 /warnaserror+ "/out:$artworkBuilder" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $ProjectRoot 'tools\Setup.cs') (Join-Path $ProjectRoot 'src\Theme.cs') (Join-Path $ProjectRoot 'src\Models.cs')
if ($LASTEXITCODE -ne 0) {throw 'Installer artwork compilation failed.'}
& $artworkBuilder (Join-Path $ProjectRoot 'assets') $stage
if ($LASTEXITCODE -ne 0) {throw 'Installer artwork generation failed.'}
& $InnoCompiler '/Qp' ('/DProjectRoot='+$ProjectRoot) ('/DStageRoot='+$stage) (Join-Path $ProjectRoot 'tools\ClassicSetup.iss')
if ($LASTEXITCODE -ne 0) {throw 'Classic installer compilation failed.'}
$setup=Join-Path $stage 'PS_Converter-v1.0-setup.exe'
if ($Verify) {
    $installed=Join-Path $stage 'installer-check'
    $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/DIR="'+$installed+'"'),('/LOG="'+(Join-Path $stage 'install.log')+'"'))
    $process=Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) {$process.Kill();throw 'Classic installation test timed out.'}
    if ($process.ExitCode -ne 0) {throw "Classic installation test failed: $($process.ExitCode)"}
    $actual=@(Get-ChildItem -LiteralPath $installed -File -Recurse)
    if ($actual.Count -ne $inventory.Count) {throw 'Installed file inventory differs from the portable archive.'}
    foreach ($file in $inventory) {
        if ((Get-FileHash -LiteralPath (Join-Path $installed $file.Name) -Algorithm SHA256).Hash -ne $file.Sha256) {throw "Installer payload differs: $($file.Name)"}
    }
    $protected=Join-Path $stage 'preserve-existing'
    New-Item -ItemType Directory -Path $protected | Out-Null
    $sentinel=Join-Path $protected 'sentinel.txt';[IO.File]::WriteAllText($sentinel,'preserve this file')
    $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/DIR="'+$protected+'"'),('/LOG="'+(Join-Path $stage 'protection.log')+'"'))
    $process=Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {$process.Kill();throw 'Existing-folder test timed out.'}
    if ($process.ExitCode -eq 0 -or [IO.File]::ReadAllText($sentinel) -ne 'preserve this file' -or @(Get-ChildItem -LiteralPath $protected -Force).Count -ne 1) {throw 'Installer changed an existing destination.'}
    Write-Output "PASS classic installer: all $($inventory.Count) file hashes match the portable; existing destination preserved."
}
if ((Get-FileHash -LiteralPath $PortableArchive -Algorithm SHA256).Hash -ne $portableHash) {throw 'The portable archive was modified.'}
if ($UpdateRelease) {
    if (-not $Verify) {throw 'Verify the installer before replacing release output.'}
    $output=Join-Path $ProjectRoot 'installer'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath $setup -Destination (Join-Path $output 'PS_Converter-v1.0-setup.exe') -Force
    $checksums=foreach ($name in @('PS_Converter-v1.0-setup.exe','PS_Converter-v1.0-portable.zip','RELEASE-v1.0.txt')) {
        $file=Join-Path $output $name
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$name
    }
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),[string[]]$checksums,[Text.UTF8Encoding]::new($false))
}
Write-Output ('Classic installer: '+$setup)
