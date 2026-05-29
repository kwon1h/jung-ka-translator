[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$KeepRelease
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Test-IsWorkspacePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $comparison = [StringComparison]::OrdinalIgnoreCase
    return $Path.Equals($repoRoot, $comparison) -or $Path.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, $comparison)
}

function Remove-GeneratedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $resolved) {
        return
    }

    foreach ($item in $resolved) {
        $fullPath = $item.Path
        if (-not (Test-IsWorkspacePath -Path $fullPath)) {
            throw "Refusing to remove path outside workspace: $fullPath"
        }

        if ($PSCmdlet.ShouldProcess($fullPath, 'Remove generated build artifact')) {
            Remove-Item -LiteralPath $fullPath -Recurse -Force
        }
    }
}

$sourceRoots = @(
    (Join-Path $repoRoot 'src'),
    (Join-Path $repoRoot 'tests')
)

foreach ($sourceRoot in $sourceRoots) {
    if (-not (Test-Path -LiteralPath $sourceRoot)) {
        continue
    }

    Get-ChildItem -LiteralPath $sourceRoot -Directory -Recurse |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        ForEach-Object { Remove-GeneratedPath -Path $_.FullName }
}

$artifactTargets = @((Join-Path $repoRoot 'artifacts\GameOverlayTranslator-win-x64'))
if (-not $KeepRelease) {
    $artifactTargets += (Join-Path $repoRoot 'artifacts\release')
}

foreach ($artifactTarget in $artifactTargets) {
    Remove-GeneratedPath -Path $artifactTarget
}
