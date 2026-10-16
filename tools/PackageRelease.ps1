param([Parameter(Mandatory=$true)][string]$ProjectRoot,[Parameter(Mandatory=$true)][string]$ApplicationDirectory,[switch]$Verify,[switch]$CleanInstaller,[string]$InnoCompiler)
$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath($ProjectRoot)
$ApplicationDirectory=[IO.Path]::GetFullPath($ApplicationDirectory)
$stage=Join-Path $ProjectRoot ('.analysis\release-stage\'+[guid]::NewGuid().ToString('N'))
$output=Join-Path $ProjectRoot 'installer'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$catalog=Get-Content -LiteralPath (Join-Path $ProjectRoot 'tools\codec-package.json') -Raw | ConvertFrom-Json
$files=@(
    [PSCustomObject]@{Name='PS_converter.exe';Source=(Join-Path $ApplicationDirectory 'PS_converter.exe')},
    [PSCustomObject]@{Name='README.md';Source=(Join-Path $ProjectRoot 'README.md')},
    [PSCustomObject]@{Name='LICENSE';Source=(Join-Path $ProjectRoot 'LICENSE')},
    [PSCustomObject]@{Name='docs/QUICKSTART.md';Source=(Join-Path $ProjectRoot 'docs\QUICKSTART.md')},
    [PSCustomObject]@{Name='docs/THIRD_PARTY.md';Source=(Join-Path $ProjectRoot 'docs\THIRD_PARTY.md')},
    [PSCustomObject]@{Name='licenses/Lucide.txt';Source=(Join-Path $ProjectRoot 'assets\icons\LICENSE.txt')}
)
foreach ($relative in @('assets/app.png','assets/screenshots/main.png','assets/screenshots/audio-ready.png','assets/screenshots/options.png','assets/screenshots/setup.png')) {
    $files += [PSCustomObject]@{Name=$relative;Source=(Join-Path $ProjectRoot $relative)}
}
foreach ($tool in $catalog) {foreach ($file in $tool.Files) {
    $source=Join-Path $ApplicationDirectory $file.Path
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Sha256) {throw "Packaged codec hash differs: $($file.Path)"}
    $files += [PSCustomObject]@{Name=$file.Path;Source=$source}
}}
$runtimeLicenses=Join-Path $ProjectRoot '.analysis\runtimes\ready\licenses'
foreach ($name in @('Microsoft-VC2005.rtf','Microsoft-VC2008.txt','Microsoft-VC2013.rtf')) {
    $source=Join-Path $runtimeLicenses $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {$files += [PSCustomObject]@{Name=('licenses/'+$name);Source=$source}}
}
$manifest=@($files | ForEach-Object {
    if (-not (Test-Path -LiteralPath $_.Source -PathType Leaf)) {throw "Missing package input: $($_.Name)"}
    [PSCustomObject]@{Name=$_.Name;Length=(Get-Item -LiteralPath $_.Source).Length;Sha256=(Get-FileHash -LiteralPath $_.Source -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$manifestPath=Join-Path $stage 'package-manifest.json'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$portable=Join-Path $stage 'PS_Converter-v1.0-portable.zip'
$stream=[IO.File]::Open($portable,[IO.FileMode]::CreateNew)
$archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {foreach ($file in $files) {[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.Source,$file.Name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}}
finally {$archive.Dispose();$stream.Dispose()}
$classicOutput=@(& (Join-Path $ProjectRoot 'tools\BuildClassicInstaller.ps1') -ProjectRoot $ProjectRoot -PortableArchive $portable -InnoCompiler $InnoCompiler -Verify:$Verify)
$classicOutput | Write-Output
$setupLine=@($classicOutput | Where-Object {$_ -is [string] -and $_.StartsWith('Classic installer: ')})
if ($setupLine.Count -ne 1) {throw 'The classic installer output was not reported.'}
$setup=$setupLine[0].Substring('Classic installer: '.Length)
if ($Verify) {Write-Output ("PASS portable archive inventory: $($manifest.Count) explicitly selected files.")}
$notes=@'
PS_Converter v1.0
Release date: 2026-10-16

Windows audio conversion for PSP, PS3, PS Vita and PS4.

DOWNLOADS
- PS_Converter-v1.0-setup.exe: English setup wizard with license terms, a destination selector and console artwork. No administrator privileges required.
- PS_Converter-v1.0-portable.zip: extract the entire ZIP and double-click PS_converter.exe. Keep its codecs folder beside the application. No repository clone is needed.
- SHA256SUMS.txt: SHA-256 checksums for the installer, portable archive and this release text.

WHAT IS INCLUDED
- MP3/WAV to AT3/AT9 and AT3/AT9 to WAV/MP3 workflows.
- PSP, PS3, PS Vita and PS4 profiles, batch conversion, cancellation and typed advanced settings.
- Centered Convert button, a clean file list, visible input borders, official Lucide icons and a larger conversion-arrow application mark.
- The inspected encoders and matching app-local x86 runtime files. No system-wide runtime installation is performed.

REQUIREMENTS
Windows 10/11, .NET Framework 4.8 and Windows Media Foundation. Windows N editions require the Media Feature Pack. Store the extracted application on a writable local drive.

VERIFICATION
31 C# regression checks pass. Conversion/core source files are unchanged by the visual update. Installer and portable extraction are compared against their complete SHA-256 inventory. Physical-console playback and a pristine Windows-machine installation have not been verified.
Native Vita and PS4 round trips pass with generated audio. PSP/PS3 retain a pre-existing DLL relocation loader error in the command sandbox; their native verification remains limited.

LICENSING
PS_Converter source code is MIT licensed. Lucide and Microsoft notices remain included. Native ATRAC tools have separate ownership and unresolved redistribution terms; MIT does not license those binaries. These local build artifacts are not a legal clearance for public redistribution. Confirm the third-party terms before uploading binary release assets.

SOURCE
https://github.com/jotadev27/PS_Converter
'@
$notesPath=Join-Path $stage 'RELEASE-v1.0.txt';[IO.File]::WriteAllText($notesPath,$notes+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
$checksum=Join-Path $stage 'SHA256SUMS.txt'
$lines=@($setup,$portable,$notesPath) | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path -Leaf $_)}
[IO.File]::WriteAllLines($checksum,$lines,[Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($CleanInstaller) {
    if ((Resolve-Path -LiteralPath $output).ProviderPath -ne (Join-Path $ProjectRoot 'installer')) {throw 'Unexpected installer cleanup target.'}
    $entries=@(Get-ChildItem -LiteralPath $output -Recurse -Force)
    if (((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or @($entries | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}).Count -ne 0) {throw 'Installer cleanup refuses reparse points.'}
    foreach ($entry in @(Get-ChildItem -LiteralPath $output -Force)) {
        if (-not $entry.FullName.StartsWith($output.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Cleanup escaped the approved installer folder.'}
        Remove-Item -LiteralPath $entry.FullName -Recurse -Force
    }
}
foreach ($file in @($setup,$portable,$notesPath,$checksum)) {Copy-Item -LiteralPath $file -Destination (Join-Path $output (Split-Path -Leaf $file)) -Force}
Write-Output ('Release files: '+$output)
Write-Output ('Release verification stage: '+$stage)
