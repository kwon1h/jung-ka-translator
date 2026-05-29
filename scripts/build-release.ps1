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

$exeFiles = @(Get-ChildItem -LiteralPath $releaseDir -File -Filter '*.exe')
if ($exeFiles.Count -ne 1) {
    throw "Expected exactly one .exe in $releaseDir, found $($exeFiles.Count)."
}

$expectedExe = Join-Path $releaseDir 'GameOverlayTranslator.exe'
if (-not (Test-Path -LiteralPath $expectedExe)) {
    throw "Expected release executable was not found: $expectedExe"
}

$unexpectedFiles = @(Get-ChildItem -LiteralPath $releaseDir -File | Where-Object { $_.Extension -in @('.csv', '.dll', '.pdb', '.json') })
if ($unexpectedFiles.Count -gt 0) {
    $names = ($unexpectedFiles | Select-Object -ExpandProperty Name) -join ', '
    throw "Release directory contains unexpected companion files: $names"
}

Write-Host "Release build complete: $expectedExe"
