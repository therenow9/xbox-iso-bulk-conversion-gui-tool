<#
.SYNOPSIS
    Downloads a pinned extract-xiso build into third-party\extract-xiso\.

.DESCRIPTION
    extract-xiso is bundled into the release drop rather than committed to this repo,
    so the binary is fetched at build time from its own project. The version is pinned
    here so a release is reproducible: bump $Tag deliberately, never implicitly.

    Its licence is fetched alongside it. That is not optional - extract-xiso is under a
    4-clause BSD licence whose second clause requires the copyright notice, conditions
    and disclaimer to travel with any binary redistribution.

.PARAMETER Tag
    The extract-xiso release to pin to. Defaults to the version this repo ships.

.PARAMETER Destination
    Where to put extract-xiso.exe and LICENSE.TXT.

.PARAMETER Force
    Re-download even if the files are already present.

.EXAMPLE
    .\Get-ExtractXiso.ps1
    .\Get-ExtractXiso.ps1 -Tag build-202505152050 -Force
#>

[CmdletBinding()]
param(
    [string] $Tag = 'build-202505152050',
    [string] $Destination,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is glacial without this

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside a param default when
# [CmdletBinding()] is present - it only works in PowerShell 7 - so the default is
# resolved here instead. This project targets 5.1, which ships with Windows.
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $Destination) { $Destination = Join-Path $here '..\third-party\extract-xiso' }


$repo    = 'XboxDev/extract-xiso'
$asset   = 'extract-xiso-Win64_Release.zip'
$zipUrl  = "https://github.com/$repo/releases/download/$Tag/$asset"
$licUrl  = "https://raw.githubusercontent.com/$repo/$Tag/LICENSE.TXT"

$Destination = [System.IO.Path]::GetFullPath($Destination)
$exePath = Join-Path $Destination 'extract-xiso.exe'
$licPath = Join-Path $Destination 'LICENSE.TXT'
$verPath = Join-Path $Destination 'VERSION.txt'

if (-not $Force -and (Test-Path -LiteralPath $exePath) -and (Test-Path -LiteralPath $licPath)) {
    $have = if (Test-Path -LiteralPath $verPath) { (Get-Content -LiteralPath $verPath -Raw).Trim() } else { '' }
    if ($have -eq $Tag) {
        Write-Host "  extract-xiso $Tag already present in $Destination" -ForegroundColor DarkGray
        return
    }
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('extract-xiso-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    Write-Host "  Downloading $asset ($Tag)" -ForegroundColor Cyan
    $zip = Join-Path $staging $asset
    Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing

    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $staging 'unpacked') -Force

    # The archive nests the binary under artifacts\, but do not depend on that.
    $found = Get-ChildItem -LiteralPath (Join-Path $staging 'unpacked') -Recurse -File -Filter 'extract-xiso.exe' |
             Select-Object -First 1
    if (-not $found) { throw "extract-xiso.exe was not found inside $asset." }

    Copy-Item -LiteralPath $found.FullName -Destination $exePath -Force

    Write-Host '  Downloading LICENSE.TXT' -ForegroundColor Cyan
    Invoke-WebRequest -Uri $licUrl -OutFile $licPath -UseBasicParsing

    if ((Get-Item -LiteralPath $licPath).Length -lt 200) {
        throw "LICENSE.TXT looks truncated. Refusing to bundle a binary without its licence."
    }

    Set-Content -LiteralPath $verPath -Value $Tag -Encoding ascii

    # Best effort, never fatal: builds must still work offline or if the API is down.
    # This exists so "we bundle the latest extract-xiso" stays true over time instead
    # of quietly becoming a stale claim in the README.
    try {
        $latest = (Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" `
                    -Headers @{ 'User-Agent' = 'xiso-converter-build' }).tag_name
        if ($latest -and $latest -ne $Tag) {
            Write-Warning "extract-xiso $latest is now available; this repo pins $Tag."
            Write-Warning "Bump `$Tag at the top of tools\Get-ExtractXiso.ps1 to ship it."
        }
    } catch {
        Write-Host "  (could not check for a newer extract-xiso: $($_.Exception.Message))" -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host "  extract-xiso $Tag ready" -ForegroundColor Green
    Write-Host "    $exePath  ($('{0:N0}' -f (Get-Item $exePath).Length) bytes)"
    Write-Host "    $licPath"
    Write-Host ''
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
