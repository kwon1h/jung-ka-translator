[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$project = Join-Path $repoRoot 'src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj'
$releaseDir = Join-Path $repoRoot 'artifacts\release'

& (Join-Path $PSScriptRoot 'clean.ps1')

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

& $dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $releaseDir `
    -p:PublishSingleFile=true `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot 'clean.ps1') -KeepRelease

$expectedExe = Join-Path $releaseDir 'GameOverlayTranslator.exe'
if (-not (Test-Path -LiteralPath $expectedExe)) {
    throw "Expected release executable was not found: $expectedExe"
}

$unexpectedFiles = @(Get-ChildItem -LiteralPath $releaseDir -File | Where-Object { $_.FullName -ne $expectedExe })
foreach ($unexpectedFile in $unexpectedFiles) {
    Remove-Item -LiteralPath $unexpectedFile.FullName -Force
}

$releaseFiles = @(Get-ChildItem -LiteralPath $releaseDir -File)
if ($releaseFiles.Count -ne 1 -or $releaseFiles[0].FullName -ne $expectedExe) {
    $names = ($releaseFiles | Select-Object -ExpandProperty Name) -join ', '
    throw "Expected only GameOverlayTranslator.exe in $releaseDir, found: $names"
}

Write-Host "Release build complete: $expectedExe"
