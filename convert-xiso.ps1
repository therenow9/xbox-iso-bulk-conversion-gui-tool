<#
.SYNOPSIS
    Batch-extracts Original Xbox XISO images into game folders using extract-xiso.exe.

.DESCRIPTION
    Reads every .iso from the XISO Format folder and extracts it into its own folder
    under the Extracted Format folder, e.g.

        E:\Roms\Xbox\Games\Extracted Format\Halo 2\default.xbe

    Source ISOs are never modified or deleted.

    Safe to re-run as your library grows: any game whose folder already contains a
    default.xbe is skipped, so only newly downloaded ISOs get processed.

    Each game is extracted into a temporary "<name>.partial" folder and only renamed
    to its final name once extract-xiso reports success and default.xbe is present.
    That means a cancelled or crashed run can never leave behind a folder that looks
    finished, and the next run will pick it up correctly.

.PARAMETER SkipSystemUpdate
    Pass -s to extract-xiso so the $SystemUpdate folder is not extracted.
    Optional - leave it off if you want a byte-for-byte faithful extraction.

.PARAMETER Force
    Re-extract everything, even games that already have a default.xbe.

.PARAMETER DryRun
    List what would be extracted and what would be skipped. Writes nothing.

.PARAMETER Include
    Only process these image filenames (name only, not a full path). Everything else
    in the source folder is ignored. Leave it off to process the whole folder.

.PARAMETER IncludeFile
    A text file holding one image filename per line, doing the same job as -Include.
    Used by the GUI, which can have more selected games than fit on a command line.

.PARAMETER NoMediaCheck
    Do not check for the Xbox media signature - attempt every image regardless.
    Use this if a genuine but unusual dump is being reported as NotXbox.

.EXAMPLE
    .\Convert-Xiso.ps1 -DryRun
    Preview: shows which ISOs are new and which are already done.

.EXAMPLE
    .\Convert-Xiso.ps1
    Extract every new ISO.

.EXAMPLE
    .\Convert-Xiso.ps1 -Source "E:\Roms\Xbox\Games\New" -Force
#>

[CmdletBinding()]
param(
    [string] $ExtractXiso = 'E:\Roms\Xbox\Tools\extract-xiso-Win64_Release\artifacts\extract-xiso.exe',
    [string] $Source      = 'E:\Roms\Xbox\Games\XISO Format',
    [string] $Output      = 'E:\Roms\Xbox\Games\Extracted Format',
    [string[]] $Include,
    [string]   $IncludeFile,
    [switch] $NoMediaCheck,
    [switch] $SkipSystemUpdate,
    [switch] $Force,
    [switch] $DryRun,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- helpers ----

# In -Json mode the script emits one compact JSON object per line on stdout and
# writes nothing else, so a GUI can read progress a line at a time. See the
# EVENTS section in the comment-based help of GUI-SPEC.md for the schema.
function Send-Event {
    param([hashtable]$Data)
    if ($Json) { Write-Output ($Data | ConvertTo-Json -Compress -Depth 4) }
}

function Write-Step { param([string]$m) if (-not $Json) { Write-Host "==> $m" -ForegroundColor Cyan  } }
function Write-Ok   { param([string]$m) if (-not $Json) { Write-Host "    $m" -ForegroundColor Green } }
function Write-Warn { param([string]$m) if (-not $Json) { Write-Host "    $m" -ForegroundColor Yellow } }
function Write-Err  { param([string]$m) if (-not $Json) { Write-Host "    $m" -ForegroundColor Red   } }

function Format-Size {
    param([double]$Bytes)
    if ($Bytes -ge 1TB) { return ('{0:N2} TB' -f ($Bytes / 1TB)) }
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    return ('{0:N0} KB' -f ($Bytes / 1KB))
}

function Format-Duration {
    param([double]$Seconds)
    $ts = [TimeSpan]::FromSeconds($Seconds)
    if ($ts.TotalHours -ge 1) { return ('{0:%h}h {0:mm}m {0:ss}s' -f $ts) }
    if ($ts.TotalMinutes -ge 1) { return ('{0:%m}m {0:ss}s' -f $ts) }
    return ('{0:N0}s' -f $Seconds)
}

# Every Original Xbox image carries this 20-byte signature at sector 32 of its game
# partition. A trimmed image (what "xiso" normally means) puts that at 0x10000; a
# redump-style full dump has a video partition first, so the game partition starts at
# 0x18300000 and the signature lands at 0x18310000. Checking both offsets tells an
# Xbox image apart from a PS2, GameCube, 360 or PC one without trusting the filename,
# which turns "extract-xiso exited with code 1" into something the user can act on.
$XboxMediaMagic   = 'MICROSOFT*XBOX*MEDIA'
$XboxMediaOffsets = @([int64]0x10000, [int64]0x18310000)

function Test-XboxImage {
    param([string]$Path)

    try { $stream = [System.IO.File]::OpenRead($Path) } catch { return $false }

    try {
        $buffer = New-Object byte[] $XboxMediaMagic.Length
        foreach ($offset in $XboxMediaOffsets) {
            if ($stream.Length -lt ($offset + $buffer.Length)) { continue }

            $stream.Position = $offset
            $read = 0
            while ($read -lt $buffer.Length) {
                $n = $stream.Read($buffer, $read, $buffer.Length - $read)
                if ($n -le 0) { break }
                $read += $n
            }

            if ($read -eq $buffer.Length -and
                [System.Text.Encoding]::ASCII.GetString($buffer) -eq $XboxMediaMagic) {
                return $true
            }
        }
    }
    finally { $stream.Dispose() }

    return $false
}

# FATX - the Xbox filesystem - is stricter than Windows. Names are capped at 42
# characters, and these characters are illegal. Comma, plus, semicolon and equals
# are all perfectly legal on Windows, which is why a folder like
# "Thing, The (USA)" copies fine on the PC and then fails to FTP to the console:
# the Xbox reports "directory already exists" and then can't navigate into it.
$FatxMaxName  = 42
$FatxBadChars = '"*+,/:;<=>?\|'

# Strips characters neither Windows nor FATX will accept in a folder name.
function Get-SafeName {
    param([string]$Name, [switch]$WindowsOnly)

    $invalid = [System.Collections.Generic.List[char]]::new()
    $invalid.AddRange([System.IO.Path]::GetInvalidFileNameChars())
    if (-not $WindowsOnly) { $invalid.AddRange($FatxBadChars.ToCharArray()) }

    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Name.ToCharArray()) {
        if ($invalid -contains $ch) { [void]$sb.Append(' ') } else { [void]$sb.Append($ch) }
    }
    return (($sb.ToString() -replace '\s+', ' ').Trim().TrimEnd('.'))
}

# Shortens a name to fit FATX's 42-character cap without turning it to gibberish:
# drop bracketed tags first, then trailing region/dump junk, then a leading
# article, and only cut mid-title as a last resort.
function Get-FatxFittedName {
    param([string]$Name)

    $n = $Name
    if ($n.Length -le $FatxMaxName) { return $n }

    $n = ($n -replace '\s*[\(\[][^\)\]]*[\)\]]', '').Trim()          # (USA), [!], (En,Fr,De)
    if ($n.Length -le $FatxMaxName) { return $n }

    $n = ($n -replace '(?i)[\s_\-]+(NTSC(-[UJ])?|PAL|USA|EUR|JPN|Region\s*Free|Complete|Repack|v\d+(\.\d+)*)$', '').Trim()
    if ($n.Length -le $FatxMaxName) { return $n }

    $n = ($n -replace '^(?i)(The|A|An)\s+', '').Trim()
    if ($n.Length -le $FatxMaxName) { return $n }

    $cut = $n.Substring(0, $FatxMaxName)
    if ($cut -match '^(.*)\s\S*$') { $cut = $Matches[1] }
    return $cut.Trim().TrimEnd('.', '-', '_', ':')
}

# Turns a filename into a folder name that will survive the trip to the Xbox:
# every trailing image extension dropped, FATX-illegal characters removed, and
# the result fitted to 42 characters.
#   "Halo 2.xiso.iso"                     -> "Halo 2"
#   "Thing, The (USA).iso"                -> "Thing The (USA)"
#   "Star Wars KOTOR II NTSC.xiso.iso"    -> "Star Wars KOTOR II NTSC"
function Get-GameFolderName {
    param([string]$FileName)
    $base = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    while ($base -match '\.(iso|xiso)$') { $base = $base -replace '\.(iso|xiso)$', '' }
    return (Get-FatxFittedName (Get-SafeName $base))
}

# ---------------------------------------------------------------- preflight ----

if (-not $Json) {
    Write-Host ''
    Write-Host '  Xbox XISO -> Extracted Format' -ForegroundColor White
    Write-Host '  -----------------------------' -ForegroundColor DarkGray
}

if (-not (Test-Path -LiteralPath $ExtractXiso)) {
    Write-Err "extract-xiso.exe not found at:"
    Write-Err "  $ExtractXiso"
    Write-Err "Edit the `$ExtractXiso default at the top of this script, or pass -ExtractXiso '<path>'."
    Send-Event @{ event = 'error'; code = 'tool-missing'; text = "extract-xiso.exe not found at: $ExtractXiso" }
    exit 1
}
if (-not (Test-Path -LiteralPath $Source)) {
    Write-Err "Source folder not found: $Source"
    Send-Event @{ event = 'error'; code = 'source-missing'; text = "Source folder not found: $Source" }
    exit 1
}

if (-not (Test-Path -LiteralPath $Output)) {
    if ($DryRun) {
        Write-Warn "Output folder does not exist yet (would be created): $Output"
    } else {
        New-Item -ItemType Directory -Path $Output -Force | Out-Null
    }
}

$isos = @(Get-ChildItem -LiteralPath $Source -File -ErrorAction SilentlyContinue |
          Where-Object { $_.Extension -in '.iso', '.xiso' } |
          Sort-Object Name)

# Optional selection filter. This narrows which images are looked at and nothing
# else: naming, skipping and extraction below are untouched by it. It exists so the
# GUI's per-row checkboxes mean something without the naming rules being forked
# into the front-end.
$selected = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
foreach ($n in $Include) { if ($n -and $n.Trim()) { [void]$selected.Add($n.Trim()) } }
if ($IncludeFile) {
    if (-not (Test-Path -LiteralPath $IncludeFile)) {
        Write-Err "Include file not found: $IncludeFile"
        Send-Event @{ event = 'error'; code = 'include-missing'; text = "Include file not found: $IncludeFile" }
        exit 1
    }
    foreach ($n in (Get-Content -LiteralPath $IncludeFile -Encoding UTF8)) {
        if ($n -and $n.Trim()) { [void]$selected.Add($n.Trim()) }
    }
}
if ($selected.Count -gt 0) {
    $isos = @($isos | Where-Object { $selected.Contains($_.Name) })
    Write-Step "Filter : $($selected.Count) image(s) selected"
}

Write-Step "Tool   : $ExtractXiso"
Write-Step "Source : $Source"
Write-Step "Output : $Output"
Write-Step "Found  : $($isos.Count) image(s)"
if ($DryRun) { Write-Warn 'DRY RUN - nothing will be written.' }
if (-not $Json) { Write-Host '' }

Send-Event @{
    event  = 'start'
    total  = $isos.Count
    tool   = $ExtractXiso
    source = $Source
    output = $Output
    dryRun = [bool]$DryRun
}

if ($isos.Count -eq 0) {
    Write-Warn 'No .iso files found in the source folder. Nothing to do.'
    Send-Event @{ event = 'summary'; extracted = 0; skipped = 0; failed = 0; todo = 0; seconds = 0 }
    exit 0
}

# Free-space sanity check: extracted games are roughly the size of the ISO.
$todoBytes = ($isos | Measure-Object -Property Length -Sum).Sum
try {
    $outRoot = [System.IO.Path]::GetPathRoot((Resolve-Path -LiteralPath $Output -ErrorAction SilentlyContinue).Path)
    if (-not $outRoot) { $outRoot = [System.IO.Path]::GetPathRoot($Output) }
    $drive = Get-PSDrive -Name $outRoot.TrimEnd(':', '\') -ErrorAction SilentlyContinue
    if ($drive -and $drive.Free -and $drive.Free -lt $todoBytes) {
        Write-Warn ("Heads up: {0} free on {1}, but the source ISOs total {2}." -f (Format-Size $drive.Free), $outRoot, (Format-Size $todoBytes))
        Write-Warn 'Already-extracted games will be skipped, so this may still be fine.'
        if (-not $Json) { Write-Host '' }
        Send-Event @{
            event = 'warning'; code = 'low-space'
            freeBytes = [int64]$drive.Free; neededBytes = [int64]$todoBytes
        }
    }
} catch { }

$logPath = Join-Path $Output ('_convert-log_{0:yyyy-MM-dd_HH-mm-ss}.txt' -f (Get-Date))

# -------------------------------------------------------------------- work ----

$results = New-Object System.Collections.Generic.List[object]
$index = 0
$overallStart = Get-Date

foreach ($iso in $isos) {
    $index++
    Write-Step "[$index/$($isos.Count)] $($iso.Name)   ($(Format-Size $iso.Length))"

    $name = Get-GameFolderName $iso.Name
    if ([string]::IsNullOrWhiteSpace($name)) { $name = "Game_$index" }

    $dest    = Join-Path $Output $name
    $partial = Join-Path $Output ($name + '.partial')

    Send-Event @{
        event = 'game-start'
        index = $index; total = $isos.Count
        iso   = $iso.Name; folder = $name; bytes = [int64]$iso.Length
    }

    # --- is this actually an Original Xbox image? ---------------------------
    # Checked before anything else touches the disk, so a PS2 or 360 image never
    # gets a folder migrated, a .partial created, or minutes of extraction spent
    # on it only to fail with a message that looks like a corrupt download.
    if (-not $NoMediaCheck -and -not (Test-XboxImage $iso.FullName)) {
        Write-Warn 'No Xbox media signature - not an Original Xbox image. Skipping.'
        $results.Add([pscustomobject]@{ Status = 'NotXbox'; Iso = $iso.Name; Folder = '-'; Time = '-' })
        Send-Event @{
            event = 'game-done'; index = $index; iso = $iso.Name; folder = $name
            status = 'NotXbox'; seconds = 0; bytes = 0
            detail = "no $XboxMediaMagic signature at 0x10000 or 0x18310000"
        }
        continue
    }

    # --- migrate folders left by earlier, looser naming rules ---------------
    # Covers ".xiso" left on the end, and names that kept FATX-illegal
    # characters or ran past 42 chars. Rename rather than re-extract.
    $rawBase = [System.IO.Path]::GetFileNameWithoutExtension($iso.Name)
    $trimmed = $rawBase
    while ($trimmed -match '\.(iso|xiso)$') { $trimmed = $trimmed -replace '\.(iso|xiso)$', '' }

    $legacyNames = @(
        (Get-SafeName $rawBase -WindowsOnly)
        (Get-SafeName $trimmed -WindowsOnly)
        (Get-SafeName $trimmed)
    ) | Where-Object { $_ -and $_ -ne $name } | Select-Object -Unique

    if (-not $DryRun -and -not (Test-Path -LiteralPath $dest)) {
        foreach ($legacy in $legacyNames) {
            $legacyPath = Join-Path $Output $legacy
            if (Test-Path -LiteralPath (Join-Path $legacyPath 'default.xbe')) {
                Rename-Item -LiteralPath $legacyPath -NewName $name
                Write-Warn "Renamed existing folder '$legacy' -> '$name'."
                break
            }
        }
    }

    # --- duplicate check -------------------------------------------------
    if (-not $Force -and (Test-Path -LiteralPath (Join-Path $dest 'default.xbe'))) {
        Write-Warn 'Already extracted - skipping.'
        $results.Add([pscustomobject]@{ Status = 'Skipped'; Iso = $iso.Name; Folder = $name; Time = '-' })
        Send-Event @{
            event = 'game-done'; index = $index; iso = $iso.Name; folder = $name
            status = 'Skipped'; seconds = 0; bytes = 0
        }
        continue
    }
    if (Test-Path -LiteralPath $dest) {
        Write-Warn 'Folder exists but has no default.xbe - re-extracting.'
    }

    if ($DryRun) {
        Write-Ok "Would extract to: $dest"
        $results.Add([pscustomobject]@{ Status = 'ToDo'; Iso = $iso.Name; Folder = $name; Time = '-' })
        Send-Event @{
            event = 'game-done'; index = $index; iso = $iso.Name; folder = $name
            status = 'ToDo'; seconds = 0; bytes = 0
        }
        continue
    }

    # --- extract into a .partial folder first -----------------------------
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
    New-Item -ItemType Directory -Path $partial -Force | Out-Null

    $xisoArgs = @('-x')
    if ($SkipSystemUpdate) { $xisoArgs += '-s' }
    $xisoArgs += @('-d', $partial, $iso.FullName)

    $start   = Get-Date
    $status  = 'Failed'
    $elapsed = 0

    $size   = 0L
    $detail = ''

    try {
        & $ExtractXiso @xisoArgs 2>&1 | ForEach-Object {
            if ($Json) { Send-Event @{ event = 'log'; index = $index; text = "$_" } }
            else { Write-Host "    $_" -ForegroundColor DarkGray }
        }
        $exit    = $LASTEXITCODE
        $elapsed = ((Get-Date) - $start).TotalSeconds

        if ($exit -ne 0) {
            Write-Err "extract-xiso exited with code $exit - leaving this one out."
            $detail = "extract-xiso exited with code $exit"
        }
        elseif (-not (Test-Path -LiteralPath (Join-Path $partial 'default.xbe'))) {
            Write-Err 'Finished, but no default.xbe was produced - treating as failed.'
            $detail = 'no default.xbe was produced'
        }
        else {
            # --- promote .partial to the real folder --------------------------
            if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
            Move-Item -LiteralPath $partial -Destination $dest -Force
            $status = 'OK'

            $size = [int64]((Get-ChildItem -LiteralPath $dest -Recurse -File -Force |
                     Measure-Object -Property Length -Sum).Sum)
            Write-Ok ("Done -> {0}   ({1} in {2})" -f $name, (Format-Size $size), (Format-Duration $elapsed))
        }
    }
    catch {
        $elapsed = ((Get-Date) - $start).TotalSeconds
        Write-Err "Error: $($_.Exception.Message)"
        $detail = $_.Exception.Message
    }

    Send-Event @{
        event  = 'game-done'; index = $index; iso = $iso.Name; folder = $name
        status = $status; seconds = [math]::Round($elapsed, 1); bytes = $size; detail = $detail
    }

    # Never leave a half-extracted folder behind.
    if ($status -ne 'OK' -and (Test-Path -LiteralPath $partial)) {
        Remove-Item -LiteralPath $partial -Recurse -Force -ErrorAction SilentlyContinue
    }

    $results.Add([pscustomobject]@{
        Status = $status
        Iso    = $iso.Name
        Folder = $name
        Time   = if ($status -eq 'OK') { Format-Duration $elapsed } else { '-' }
    })
}

# ----------------------------------------------------------------- summary ----

$ok      = @($results | Where-Object Status -eq 'OK').Count
$skipped = @($results | Where-Object Status -eq 'Skipped').Count
$failed  = @($results | Where-Object Status -eq 'Failed').Count
$todo    = @($results | Where-Object Status -eq 'ToDo').Count
$notXbox = @($results | Where-Object Status -eq 'NotXbox').Count
$totalSec = ((Get-Date) - $overallStart).TotalSeconds

# Built by hand rather than with Format-Table so columns line up identically in a
# console window, from the .bat launcher, and in the log file.
$w1 = [Math]::Max(3, (($results.Iso    | Measure-Object -Maximum -Property Length).Maximum))
$w2 = [Math]::Max(6, (($results.Folder | Measure-Object -Maximum -Property Length).Maximum))
$fmt = "{0,-8}  {1,-$w1}  {2,-$w2}  {3,8}"

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add(($fmt -f 'Status', 'ISO', 'Folder', 'Time'))
$lines.Add(($fmt -f ('-' * 8), ('-' * $w1), ('-' * $w2), ('-' * 8)))
foreach ($r in $results) { $lines.Add(($fmt -f $r.Status, $r.Iso, $r.Folder, $r.Time)) }

if (-not $Json) {
    Write-Host ''
    Write-Step 'Summary'
    foreach ($line in $lines) {
        $colour = 'Gray'
        if ($line -like 'OK*')      { $colour = 'Green'  }
        if ($line -like 'Skipped*') { $colour = 'DarkGray' }
        if ($line -like 'Failed*')  { $colour = 'Red'    }
        if ($line -like 'ToDo*')    { $colour = 'Cyan'   }
        if ($line -like 'NotXbox*') { $colour = 'Yellow' }
        Write-Host "    $line" -ForegroundColor $colour
    }
    Write-Host ''
}
$notXboxNote = if ($notXbox -gt 0) { "   Not Xbox: $notXbox" } else { '' }
if ($DryRun) {
    if (-not $Json) {
        Write-Host ("    To extract: {0}   Already done: {1}{2}   Elapsed: {3}" -f $todo, $skipped, $notXboxNote, (Format-Duration $totalSec)) -ForegroundColor Cyan
    }
}
else {
    if (-not $Json) {
        Write-Host ("    Extracted: {0}   Skipped: {1}   Failed: {2}{3}   Elapsed: {4}" -f $ok, $skipped, $failed, $notXboxNote, (Format-Duration $totalSec)) -ForegroundColor Cyan
    }
    try {
        $lines | Set-Content -LiteralPath $logPath -Encoding UTF8
        if (-not $Json) { Write-Host "    Log: $logPath" -ForegroundColor DarkGray }
    } catch { $logPath = '' }
}

Send-Event @{
    event   = 'summary'
    extracted = $ok; skipped = $skipped; failed = $failed; todo = $todo; notXbox = $notXbox
    seconds = [math]::Round($totalSec, 1)
    log     = $logPath
}

# Not an error: those images were left alone deliberately, so the exit code stays 0
# unless something actually failed.
if ($notXbox -gt 0) {
    Write-Warn "$notXbox image(s) carried no Xbox media signature and were left alone."
    Write-Warn 'Re-run with -NoMediaCheck if you believe one of them really is an Xbox image.'
}

if ($failed -gt 0) {
    if (-not $Json) { Write-Host '' }
    Write-Warn 'Some images failed. Usual causes: an incomplete download, a redump-style'
    Write-Warn 'full dump that needs rebuilding first, or the drive running out of space.'
    if (-not $Json) { Write-Host '' }
    exit 2
}

if (-not $Json) { Write-Host '' }
exit 0