<#
.SYNOPSIS
    Regression tests for convert-xiso.ps1, using the fake extract-xiso stub.

.DESCRIPTION
    Exercises every status path, the FATX naming rules, the media-signature check and
    the exit codes, without needing real ISOs or an Xbox. The engine is invoked exactly
    the way the GUI invokes it - Windows PowerShell 5.1, -Command bootstrap, UTF-8
    forced, arguments passed through the environment - so the encoding behaviour that
    bit us once is covered too.

    Exits 0 if everything passes, 1 otherwise. Safe to run locally and in CI.

.EXAMPLE
    .\Invoke-Tests.ps1
#>

[CmdletBinding()]
param(
    [string] $Root = (Join-Path $env:TEMP 'XisoConverterTests')
)

$ErrorActionPreference = 'Stop'

$repo   = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script = Join-Path $repo 'convert-xiso.ps1'
$source = Join-Path $Root 'XISO Format'
$output = Join-Path $Root 'Extracted Format'

$stub = Join-Path $repo 'tools\FakeExtractXiso\bin\Release\net8.0\fake-extract-xiso.exe'
if (-not (Test-Path -LiteralPath $stub)) {
    $stub = Join-Path $repo 'tools\FakeExtractXiso\bin\Debug\net8.0\fake-extract-xiso.exe'
}
if (-not (Test-Path -LiteralPath $stub)) {
    throw "fake-extract-xiso.exe not found. Run: dotnet build $repo\XisoConverter.sln"
}

$passed = 0
$failed = 0

function Test-That {
    param([string] $Name, [scriptblock] $Condition)

    $ok = $false
    try { $ok = [bool](& $Condition) } catch { $ok = $false }

    if ($ok) {
        $script:passed++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    } else {
        $script:failed++
        Write-Host "  FAIL  $Name" -ForegroundColor Red
    }
}

# Mirrors ConverterRunner.cs: Windows PowerShell 5.1 mangles non-ASCII on redirected
# stdout unless the child sets its own output encoding first, and paths travel in the
# environment so no quoting can corrupt them.
function Invoke-Engine {
    param([string[]] $Switches = @(), [hashtable] $Extra = @{})

    $bootstrap = '[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); ' +
                 '& $env:XC_SCRIPT -Json -ExtractXiso $env:XC_TOOL -Source $env:XC_SOURCE -Output $env:XC_OUTPUT'
    foreach ($s in $Switches) { $bootstrap += " $s" }
    $bootstrap += '; exit ([int]$LASTEXITCODE)'

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $bootstrap)) {
        $psi.ArgumentList.Add($a)
    }
    $psi.Environment['XC_SCRIPT'] = $script
    $psi.Environment['XC_TOOL']   = $stub
    $psi.Environment['XC_SOURCE'] = $source
    $psi.Environment['XC_OUTPUT'] = $output
    foreach ($k in $Extra.Keys) { $psi.Environment[$k] = $Extra[$k] }

    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $psi.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)

    $proc = [System.Diagnostics.Process]::Start($psi)
    $out = $proc.StandardOutput.ReadToEnd()
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $events = @()
    foreach ($line in ($out -split "`r?`n")) {
        if ($line.Trim().StartsWith('{')) { $events += ($line | ConvertFrom-Json) }
    }

    return [pscustomobject]@{
        ExitCode = $proc.ExitCode
        Events   = $events
        StdErr   = $err.Trim()
        Done     = @($events | Where-Object { $_.event -eq 'game-done' })
        Summary  = @($events | Where-Object { $_.event -eq 'summary' })[0]
    }
}

function Get-Status {
    param($Result, [string] $Iso)
    return (@($Result.Done | Where-Object { $_.iso -eq $Iso })[0]).status
}

Write-Host ''
Write-Host '  convert-xiso.ps1 regression tests' -ForegroundColor White
Write-Host '  ---------------------------------' -ForegroundColor DarkGray
Write-Host ''

& (Join-Path $PSScriptRoot 'New-TestLibrary.ps1') -Root $Root -Clean | Out-Null

$accented = "Pok" + [char]0xE9 + "mon Caf" + [char]0xE9 + " - " + [char]0x014C + "kami Edition.iso"

# --------------------------------------------------------------- dry run scan ----
Write-Host '  Scan (-DryRun)' -ForegroundColor Cyan
$scan = Invoke-Engine -Switches @('-DryRun')

Test-That 'scan exits 0' { $scan.ExitCode -eq 0 }
Test-That 'scan writes nothing to stderr' { $scan.StdErr -eq '' }
Test-That 'scan reports every image' { $scan.Done.Count -eq 13 }
Test-That 'new image is ToDo' { (Get-Status $scan 'Halo 2.iso') -eq 'ToDo' }
Test-That 'already-extracted image is Skipped' { (Get-Status $scan 'Ninja Gaiden Black.iso') -eq 'Skipped' }
Test-That 'PS2 image is NotXbox' { (Get-Status $scan 'Final Fantasy X (PS2).iso') -eq 'NotXbox' }
Test-That 'Xbox 360 image is NotXbox' { (Get-Status $scan 'Gears of War (Xbox 360).iso') -eq 'NotXbox' }
Test-That 'redump-offset image is recognised as Xbox' {
    (Get-Status $scan 'Panzer Dragoon Orta (full dump).iso') -eq 'ToDo'
}
Test-That 'summary counts NotXbox separately' { $scan.Summary.notXbox -eq 2 }
Test-That 'NotXbox alone does not fail the run' { $scan.ExitCode -eq 0 }

# ------------------------------------------------------------- naming rules ----
Write-Host ''
Write-Host '  FATX naming' -ForegroundColor Cyan

Test-That 'comma is stripped' {
    (@($scan.Done | Where-Object { $_.iso -eq 'Thing, The (USA).iso' })[0]).folder -eq 'Thing The (USA)'
}
Test-That 'over-long name is shortened to <= 42 chars' {
    $f = (@($scan.Done | Where-Object { $_.iso -like 'Star Wars*' })[0]).folder
    $f.Length -le 42 -and $f -eq 'Star Wars Knights of the Old Republic II'
}
Test-That 'bracketed tags are dropped' {
    (@($scan.Done | Where-Object { $_.iso -like 'Tom Clancy*' })[0]).folder -eq "Tom Clancy's Splinter Cell Pandora"
}
Test-That 'double extension is removed' {
    (@($scan.Done | Where-Object { $_.iso -eq 'Jet Set Radio Future.xiso.iso' })[0]).folder -eq 'Jet Set Radio Future'
}
Test-That 'no folder name carries a FATX-illegal character' {
    $bad = '"*+,/:;<=>?\|'.ToCharArray()
    -not (@($scan.Done | Where-Object { $f = $_.folder; @($bad | Where-Object { $f -and $f.Contains($_) }).Count -gt 0 }).Count)
}
Test-That 'non-ASCII survives the JSON stream intact' {
    @($scan.Done | Where-Object { $_.iso -eq $accented }).Count -eq 1
}

# ------------------------------------------------------------ real conversion ----
Write-Host ''
Write-Host '  Conversion' -ForegroundColor Cyan
$run = Invoke-Engine -Switches @('-SkipSystemUpdate')

Test-That 'a run with failures exits 2' { $run.ExitCode -eq 2 }
Test-That 'good image extracts OK' { (Get-Status $run 'Halo 2.iso') -eq 'OK' }
Test-That 'non-zero exit from the tool is Failed' { (Get-Status $run 'fail game.iso') -eq 'Failed' }
Test-That 'clean exit without default.xbe is Failed' {
    (Get-Status $run 'junk incomplete dump.iso') -eq 'Failed'
}
Test-That 'failure carries a reason' {
    -not [string]::IsNullOrWhiteSpace((@($run.Done | Where-Object { $_.iso -eq 'fail game.iso' })[0]).detail)
}
Test-That 'folder from an older naming rule is renamed, not re-extracted' {
    (Get-Status $run 'Jet Set Radio Future.xiso.iso') -eq 'Skipped' -and
    (Test-Path -LiteralPath (Join-Path $output 'Jet Set Radio Future')) -and
    -not (Test-Path -LiteralPath (Join-Path $output 'Jet Set Radio Future.xiso'))
}
Test-That 'no .partial folder survives the run' {
    @(Get-ChildItem -LiteralPath $output -Directory -Filter '*.partial').Count -eq 0
}
Test-That 'a failed image leaves no folder behind' {
    -not (Test-Path -LiteralPath (Join-Path $output 'fail game'))
}
Test-That '-SkipSystemUpdate leaves out $SystemUpdate' {
    -not (Test-Path -LiteralPath (Join-Path $output 'Halo 2\$SystemUpdate'))
}
Test-That 'accented title lands on disk intact' {
    Test-Path -LiteralPath (Join-Path $output ("Pok" + [char]0xE9 + "mon Caf" + [char]0xE9 + " - " + [char]0x014C + "kami Edition"))
}
Test-That 'every created folder fits FATX' {
    $bad = '"*+,/:;<=>?\|'.ToCharArray()
    $dirs = Get-ChildItem -LiteralPath $output -Directory
    @($dirs | Where-Object { $n = $_.Name; $n.Length -gt 42 -or @($bad | Where-Object { $n.Contains($_) }).Count -gt 0 }).Count -eq 0
}
Test-That 'NotXbox images were never extracted' {
    -not (Test-Path -LiteralPath (Join-Path $output 'Final Fantasy X (PS2)')) -and
    -not (Test-Path -LiteralPath (Join-Path $output 'Gears of War (Xbox 360)'))
}

# ------------------------------------------------------------------ skipping ----
Write-Host ''
Write-Host '  Re-run and filtering' -ForegroundColor Cyan
$again = Invoke-Engine -Switches @('-SkipSystemUpdate')

Test-That 're-running skips what is already done' {
    (Get-Status $again 'Halo 2.iso') -eq 'Skipped'
}

$includeFile = Join-Path $Root 'include.txt'
@('Halo 2.iso', 'Thing, The (USA).iso') | Set-Content -LiteralPath $includeFile -Encoding utf8BOM
$filtered = Invoke-Engine -Switches @('-IncludeFile $env:XC_INCLUDE', '-DryRun') -Extra @{ XC_INCLUDE = $includeFile }

Test-That '-IncludeFile narrows the run to the listed images' { $filtered.Done.Count -eq 2 }
Test-That '-IncludeFile keeps the right images' {
    @($filtered.Done | Where-Object { $_.iso -eq 'Halo 2.iso' }).Count -eq 1
}

$forced = Invoke-Engine -Switches @('-Include ''Final Fantasy X (PS2).iso''', '-NoMediaCheck', '-DryRun')
Test-That '-NoMediaCheck lets a NotXbox image through' {
    (Get-Status $forced 'Final Fantasy X (PS2).iso') -eq 'ToDo'
}

# --------------------------------------------------------------- preflight ----
Write-Host ''
Write-Host '  Preflight' -ForegroundColor Cyan
$noTool = Invoke-Engine -Extra @{ XC_TOOL = 'D:\definitely\not\here\extract-xiso.exe' }

Test-That 'missing tool exits 1' { $noTool.ExitCode -eq 1 }
Test-That 'missing tool reports a tool-missing error event' {
    (@($noTool.Events | Where-Object { $_.event -eq 'error' })[0]).code -eq 'tool-missing'
}

$noSource = Invoke-Engine -Extra @{ XC_SOURCE = 'D:\definitely\not\here' }
Test-That 'missing source exits 1' { $noSource.ExitCode -eq 1 }
Test-That 'missing source reports a source-missing error event' {
    (@($noSource.Events | Where-Object { $_.event -eq 'error' })[0]).code -eq 'source-missing'
}

# ----------------------------------------------------------- bundled binary ----
$bundled = Join-Path $repo 'third-party\extract-xiso\extract-xiso.exe'
if (Test-Path -LiteralPath $bundled) {
    Write-Host ''
    Write-Host '  Bundled extract-xiso' -ForegroundColor Cyan
    Test-That 'the bundled binary runs' { (& $bundled -v 2>&1 | Out-String) -match 'extract-xiso' }
    Test-That 'its licence is bundled with it' {
        Test-Path -LiteralPath (Join-Path $repo 'third-party\extract-xiso\LICENSE.TXT')
    }
}

# ------------------------------------------------------------------ results ----
Write-Host ''
Write-Host '  ------------------------------------' -ForegroundColor DarkGray
if ($failed -eq 0) {
    Write-Host "  All $passed tests passed" -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  $failed of $($passed + $failed) tests FAILED" -ForegroundColor Red
Write-Host ''
exit 1
