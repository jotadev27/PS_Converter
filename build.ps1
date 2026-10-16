param([switch]$Verify, [switch]$VerifyNative, [switch]$Package, [switch]$CleanInstaller, [switch]$SourceOnly, [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PortableName = 'PS_converter', [string]$CodecSourceDirectory, [string]$RuntimeDirectory, [string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
if ($SourceOnly -and ($Package -or $VerifyNative)) { throw 'Source-only builds cannot create a complete release package or run native codec tests.' }
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is required. No dependencies will be installed automatically.' }
$work = Join-Path $projectRoot '.analysis\build'
$portable = Join-Path $work ('portable\' + $PortableName)
if (-not $CodecSourceDirectory) { $CodecSourceDirectory = Join-Path $projectRoot '.analysis\codec-inputs' }
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path $projectRoot '.analysis\runtimes\ready' }
$codecCatalogPath = Join-Path $projectRoot 'tools\codec-package.json'
$codecCatalog = Get-Content -LiteralPath $codecCatalogPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $work,$portable -Force | Out-Null
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Runtime.Serialization.dll')
$assetTool = Join-Path $work 'BuildAssets.exe'
$geometryReferences = @('PresentationCore.dll','WindowsBase.dll') | ForEach-Object { '/r:' + (Join-Path (Split-Path -Parent $compiler) ('WPF\'+$_)) }
& $compiler /nologo /target:exe /warn:4 /warnaserror+ "/out:$assetTool" @references /r:System.Xml.dll @geometryReferences (Join-Path $projectRoot 'tools\BuildAssets.cs') (Join-Path $projectRoot 'tools\SvgRasterizer.cs') (Join-Path $projectRoot 'src\Theme.cs') (Join-Path $projectRoot 'src\Models.cs')
if ($LASTEXITCODE -ne 0) { throw 'Asset builder failed.' }
& $assetTool (Join-Path $projectRoot 'assets')
if ($LASTEXITCODE -ne 0) { throw 'Asset generation failed.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$consoleResources = @('psp','ps3','vita','ps4') | ForEach-Object {
    $image = Join-Path $projectRoot "assets\consoles\$_.png"
    if (-not (Test-Path -LiteralPath $image)) { throw "Missing console artwork: $_" }
    "/resource:$image,console.$_"
}
$consoleResources += "/resource:$codecCatalogPath,codec.catalog"
$consoleResources += @('upload','file-music','folder','settings-2','play','square','x','check','arrow-right') | ForEach-Object {
    '/resource:' + (Join-Path $projectRoot ('assets\icons\' + $_ + '.png')) + ',ui-icon.' + $_
}
$consoleResources += '/resource:' + (Join-Path $projectRoot 'assets\app.ico') + ',app.icon'
$application = Join-Path $portable 'PS_converter.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /warnaserror+ "/out:$application" "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" "/win32icon:$(Join-Path $projectRoot 'assets\app.ico')" @references @consoleResources @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
$codecFiles = @()
if (-not $SourceOnly) {
foreach ($tool in $codecCatalog) {
    foreach ($file in $tool.Files) {
        $sourceFolder = if ($tool.Name -eq 'lame.exe') { 'LAME' } else { 'ATRAC' }
        $source = if ($file.Source) { Join-Path $RuntimeDirectory $file.Source } else { Join-Path (Join-Path $CodecSourceDirectory $sourceFolder) (Split-Path -Leaf $file.Path) }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing codec input: $source. No dependencies are downloaded or installed automatically." }
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Sha256) { throw "Codec input does not match the inspected identity: $($tool.Name)" }
        $destination = Join-Path $portable $file.Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
        $codecFiles += $file.Path
    }
}
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $portable 'README.md')
if ($Verify) {
    $tests = Join-Path $work 'PS_converter.Tests.exe'
    & $compiler /nologo /target:exe /platform:anycpu /optimize+ /warn:4 /warnaserror+ /main:PSConverter.Tests "/out:$tests" @references @consoleResources @sources (Join-Path $projectRoot 'tests\RegressionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    $testArguments = @((Join-Path $projectRoot '.analysis\test-runs'),$portable)
    if ($SourceOnly) { $testArguments += '--source-only' }
    & $tests @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
}
if ($VerifyNative) {
    $nativeTests = Join-Path $work 'PS_converter.NativeTests.exe'
    & $compiler /nologo /target:exe /platform:anycpu /optimize+ /warn:4 /warnaserror+ /main:PSConverter.NativeCodecTests "/out:$nativeTests" @references @consoleResources @sources (Join-Path $projectRoot 'tests\NativeCodecTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Native test build failed.' }
    & $nativeTests $portable (Join-Path $projectRoot '.analysis\native-test-runs')
    if ($LASTEXITCODE -ne 0) { throw 'Some native platform checks failed. See the printed native-results.txt location; this is not a fully verified platform release.' }
}
if ($Package) {
    & (Join-Path $projectRoot 'tools\PackageRelease.ps1') -ProjectRoot $projectRoot -ApplicationDirectory $portable -Verify:$Verify -CleanInstaller:$CleanInstaller -InnoCompiler $InnoCompiler
}
Write-Output "Built $application"
