<#
.SYNOPSIS
    Builds a throwaway ISO library that exercises every path through the converter.

.DESCRIPTION
    Creates a source folder of tiny fake .iso files and an output folder, covering:

      * a plain name                                  -> OK
      * a name with commas                            -> FATX rename
      * a name well over 42 characters                -> FATX shortening
      * accented characters                           -> encoding round-trip
      * an image that fails                           -> Failed
      * an image that exits clean but writes nothing  -> Failed (no default.xbe)
      * a slow image                                  -> something to cancel
      * a game already extracted                      -> Skipped
      * a folder under an older naming rule           -> renamed, not re-extracted
      * images with no Xbox media signature           -> NotXbox
      * a redump-style layout                         -> NotXbox check finds it anyway

    The images are sparse, so a "4 GB" library costs nothing on disk. Each one that
    is meant to look like an Xbox image gets a real MICROSOFT*XBOX*MEDIA signature
    written at the right offset, because the converter now checks for it.

    Nothing here touches your real library.

.EXAMPLE
    .\New-TestLibrary.ps1
    .\New-TestLibrary.ps1 -Root D:\scratch\xiso-test -Clean
#>

[CmdletBinding()]
param(
    [string] $Root = (Join-Path $env:TEMP 'XisoConverterTest'),
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $Root 'XISO Format'
$output = Join-Path $Root 'Extracted Format'

if ($Clean -and (Test-Path -LiteralPath $Root)) {
    Remove-Item -LiteralPath $Root -Recurse -Force
}

New-Item -ItemType Directory -Path $source -Force | Out-Null
New-Item -ItemType Directory -Path $output -Force | Out-Null

# Kind decides where - or whether - the Xbox media signature is written:
#   xiso   : trimmed image, signature at 0x10000
#   redump : full dump, video partition first, so the signature is at 0x18310000
#   other  : no signature at all, i.e. an image for some other console
$images = @(
    @{ Name = 'Halo 2.iso';                                                            Kb = 4300000; Kind = 'xiso' }
    @{ Name = 'Thing, The (USA).iso';                                                  Kb = 1900000; Kind = 'xiso' }
    @{ Name = 'Star Wars Knights of the Old Republic II The Sith Lords (USA).xiso.iso'; Kb = 3800000; Kind = 'xiso' }
    @{ Name = 'Tom Clancy''s Splinter Cell Pandora Tomorrow (En,Fr,De) [!].iso';        Kb = 2600000; Kind = 'xiso' }
    @{ Name = 'Pokemon Cafe - Okami Edition.iso';                                       Kb = 900000;  Kind = 'xiso' }
    @{ Name = 'Ninja Gaiden Black.iso';                                                 Kb = 3100000; Kind = 'xiso' }
    @{ Name = 'Jet Set Radio Future.xiso.iso';                                          Kb = 1400000; Kind = 'xiso' }
    @{ Name = 'slow big game.iso';                                                      Kb = 6200000; Kind = 'xiso' }
    @{ Name = 'fail game.iso';                                                          Kb = 700000;  Kind = 'xiso' }
    @{ Name = 'junk incomplete dump.iso';                                               Kb = 500000;  Kind = 'xiso' }
    @{ Name = 'Panzer Dragoon Orta (full dump).iso';                                    Kb = 7300000; Kind = 'redump' }
    @{ Name = 'Final Fantasy X (PS2).iso';                                              Kb = 4300000; Kind = 'other' }
    @{ Name = 'Gears of War (Xbox 360).iso';                                            Kb = 7100000; Kind = 'other' }
)

# A recognisable accented title, written out here so the file really does carry
# non-ASCII characters on disk rather than relying on this script's own encoding.
$accented = "Pok" + [char]0xE9 + "mon Caf" + [char]0xE9 + " - " + [char]0x014C + "kami Edition.iso"

$signature = [System.Text.Encoding]::ASCII.GetBytes('MICROSOFT*XBOX*MEDIA')

function Write-XboxSignature {
    param([string] $File, [int64] $Offset)

    $stream = [System.IO.File]::OpenWrite($File)
    try {
        $stream.Position = $Offset
        $stream.Write($signature, 0, $signature.Length)
    }
    finally { $stream.Dispose() }
}

foreach ($image in $images) {
    $name = if ($image.Name -eq 'Pokemon Cafe - Okami Edition.iso') { $accented } else { $image.Name }
    $path = Join-Path $source $name

    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    New-Item -ItemType File -Path $path -Force | Out-Null

    # Marked sparse first, so a "4 GB" image reports its full size without costing
    # anything on disk. On a volume that cannot do that, settle for a small file.
    $sparse = $false
    try {
        & fsutil sparse setflag "$path" *> $null
        $sparse = ($LASTEXITCODE -eq 0)
    } catch { }

    $length = if ($sparse) { [int64]$image.Kb * 1KB } else { [int64]64KB }
    if ($image.Kind -eq 'redump' -and $length -lt 0x18320000) { $length = [int64]0x18320000 }

    $stream = [System.IO.File]::OpenWrite($path)
    try { $stream.SetLength($length) } finally { $stream.Dispose() }

    switch ($image.Kind) {
        'xiso'   { Write-XboxSignature $path 0x10000 }
        'redump' { Write-XboxSignature $path 0x18310000 }
        default  { }   # deliberately unsigned - this is the NotXbox case
    }
}

# Already extracted - the converter must skip this one.
$done = Join-Path $output 'Ninja Gaiden Black'
New-Item -ItemType Directory -Path $done -Force | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $done 'default.xbe'), [byte[]]::new(1024))

# Extracted under an older naming rule ("Jet Set Radio Future.xiso"): the converter
# should rename this folder rather than extract the image again.
$legacy = Join-Path $output 'Jet Set Radio Future.xiso'
New-Item -ItemType Directory -Path $legacy -Force | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $legacy 'default.xbe'), [byte[]]::new(1024))

Write-Host ''
Write-Host '  Test library ready' -ForegroundColor Green
Write-Host "    Source : $source"
Write-Host "    Output : $output"
Write-Host "    Images : $($images.Count)  ($(@($images | Where-Object Kind -eq 'other').Count) with no Xbox signature)"
Write-Host ''
Write-Host '  Point the GUI at those two folders and at fake-extract-xiso.exe.' -ForegroundColor DarkGray
Write-Host ''
