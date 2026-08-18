<#
.SYNOPSIS
    Builds the release drop into dist\XisoConverter\.

.DESCRIPTION
    Produces the folder you actually ship: one self-contained XisoConverter.exe with
    no .NET prerequisite, plus convert-xiso.ps1 beside it. Copy that folder anywhere
    and it runs.

    Framework-dependent builds are ~1 MB instead of ~70 MB but need the .NET 8
    Desktop Runtime installed on the machine that runs them.

.PARAMETER FrameworkDependent
    Build the small version that requires the .NET 8 Desktop Runtime.

.PARAMETER Zip
    Also pack the drop into dist\XisoConverter-<version>.zip.

.PARAMETER Configuration
    Release (the default) or Debug. Debug is for a quick local run, not a release.

.PARAMETER BundleTool
    Include extract-xiso from third-party\extract-xiso\ in the drop, so users do not
    have to download it separately. Run tools\Get-ExtractXiso.ps1 first to populate
    that folder. Its LICENSE.TXT is bundled with it - the 4-clause BSD licence it ships
    under requires the notice to travel with the binary, so this is not optional and
    the build refuses to proceed without it.

.PARAMETER Version
    Overrides the version baked into the .exe and used to name the zip. Release
    workflows pass the git tag; leave it off to use Directory.Build.props.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Zip
    .\build.ps1 -BundleTool -Zip
    .\build.ps1 -FrameworkDependent -Zip
#>

[CmdletBinding()]
param(
    [switch] $FrameworkDependent,
    [switch] $Zip,
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [switch] $BundleTool,
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$repo    = $PSScriptRoot
$project = Join-Path $repo 'src\XisoConverterGui\XisoConverterGui.csproj'
$dist    = Join-Path $repo 'dist'
$drop    = Join-Path $dist 'XisoConverter'

# The SDK is not always on PATH for a fresh shell.
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "The .NET SDK was not found. Install it with: winget install Microsoft.DotNet.SDK.8"
}

Write-Host ''
Write-Host '  Building XISO Converter' -ForegroundColor White
Write-Host '  -----------------------' -ForegroundColor DarkGray
Write-Host "  Configuration : $Configuration"
Write-Host "  Runtime       : $(if ($FrameworkDependent) { 'framework-dependent (needs .NET 8 Desktop Runtime)' } else { 'self-contained (no prerequisite)' })"
Write-Host "  extract-xiso  : $(if ($BundleTool) { 'bundled' } else { 'not bundled - user supplies it' })"
if ($Version) { Write-Host "  Version       : $Version" }
Write-Host ''

# Checked before the build so a licence problem is not discovered after five minutes
# of compiling.
$toolDir = Join-Path $repo 'third-party\extract-xiso'
if ($BundleTool) {
    $toolExe = Join-Path $toolDir 'extract-xiso.exe'
    $toolLic = Join-Path $toolDir 'LICENSE.TXT'

    if (-not (Test-Path -LiteralPath $toolExe)) {
        throw "extract-xiso.exe is not in $toolDir. Run tools\Get-ExtractXiso.ps1 first."
    }
    if (-not (Test-Path -LiteralPath $toolLic)) {
        throw "LICENSE.TXT is missing from $toolDir. extract-xiso may not be redistributed without it."
    }
}

# A running copy holds a lock on its own .exe, and "access is denied" on a path you
# did not ask about is a confusing way to find that out.
$running = @(Get-Process -Name 'XisoConverter' -ErrorAction SilentlyContinue |
             Where-Object { $_.Path -and $_.Path.StartsWith($drop, [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) {
    throw "XisoConverter.exe is running from $drop (PID $($running.Id -join ', ')). Close it and build again."
}

if (Test-Path -LiteralPath $drop) { Remove-Item -LiteralPath $drop -Recurse -Force }
New-Item -ItemType Directory -Path $drop -Force | Out-Null

$publishArgs = @(
    'publish', $project
    '-c', $Configuration
    '-o', $drop
    '--nologo'
    '-v', 'minimal'
)

if ($FrameworkDependent) {
    $publishArgs += @(
        '-p:SelfContained=false'
        '-p:PublishSingleFile=true'
        '-p:EnableCompressionInSingleFile=false'
        '--runtime', 'win-x64'
    )
}

if ($Version) {
    $publishArgs += @("-p:Version=$Version", "-p:FileVersion=$(($Version -split '-')[0]).0", "-p:InformationalVersion=$Version")
}

& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Belt and braces: the engine must be next to the .exe, because the GUI shells out
# to it and refuses to run without it. The manual ships with it.
$engine = Join-Path $drop 'convert-xiso.ps1'
if (-not (Test-Path -LiteralPath $engine)) {
    Copy-Item (Join-Path $repo 'convert-xiso.ps1') $engine
}

$manual = Join-Path $drop 'README.md'
if (-not (Test-Path -LiteralPath $manual)) {
    Copy-Item (Join-Path $repo 'release\README.md') $manual
}

$notices = Join-Path $drop 'THIRD-PARTY-NOTICES.md'
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $notices -Force

$licence = Join-Path $repo 'LICENSE'
if (Test-Path -LiteralPath $licence) { Copy-Item $licence (Join-Path $drop 'LICENSE') -Force }

# extract-xiso travels with its own licence, in its own folder, unmodified. The app
# picks it up from here automatically, so a bundled drop needs no setup at all.
if ($BundleTool) {
    $bundled = Join-Path $drop 'extract-xiso'
    New-Item -ItemType Directory -Path $bundled -Force | Out-Null
    Copy-Item (Join-Path $toolDir '*') $bundled -Force
    Write-Host "  Bundled extract-xiso from $toolDir" -ForegroundColor DarkGray
}

$exe = Join-Path $drop 'XisoConverter.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Expected $exe but it was not produced." }

Write-Host ''
Write-Host '  Drop contents' -ForegroundColor Cyan
Get-ChildItem -LiteralPath $drop -Recurse -File |
    Sort-Object -Property Length -Descending |
    ForEach-Object {
        '    {0,10}  {1}' -f ('{0:N1} MB' -f ($_.Length / 1MB)), $_.FullName.Substring($drop.Length + 1)
    }

if ($Zip) {
    $label = if ($Version) { $Version } else { (Get-Item -LiteralPath $exe).VersionInfo.FileVersion }
    $archive = Join-Path $dist "XisoConverter-$label.zip"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path (Join-Path $drop '*') -DestinationPath $archive
    Write-Host ''
    Write-Host ('  Zipped -> {0}  ({1:N1} MB)' -f $archive, ((Get-Item $archive).Length / 1MB)) -ForegroundColor Cyan
}

Write-Host ''
Write-Host '  Release build ready' -ForegroundColor Green
Write-Host "    $exe"
Write-Host ''
