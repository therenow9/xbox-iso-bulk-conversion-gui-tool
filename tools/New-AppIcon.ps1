<#
.SYNOPSIS
    Regenerates src\XisoConverterGui\app.ico.

.DESCRIPTION
    Draws a game disc at every size Windows asks for and packs them into one .ico.
    The result is committed, so this only needs running if the artwork changes.
#>

[CmdletBinding()]
param(
    [string] $Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside a param default when
# [CmdletBinding()] is present - it only works in PowerShell 7 - so the default is
# resolved here instead. This project targets 5.1, which ships with Windows.
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $Path) { $Path = Join-Path $here '..\src\XisoConverterGui\app.ico' }


$sizes = 16, 24, 32, 48, 64, 128, 256

$discColour = [System.Drawing.Color]::FromArgb(255, 16, 124, 16)    # Xbox green
$edgeColour = [System.Drawing.Color]::FromArgb(255, 10, 82, 10)
$hubColour  = [System.Drawing.Color]::FromArgb(255, 250, 250, 250)

function New-DiscPng {
    param([int] $Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $pad = [Math]::Max(1, [int]($Size * 0.04))
        $box = [System.Drawing.RectangleF]::new($pad, $pad, ($Size - 2 * $pad), ($Size - 2 * $pad))

        $fill = [System.Drawing.SolidBrush]::new($discColour)
        $graphics.FillEllipse($fill, $box)
        $fill.Dispose()

        $pen = [System.Drawing.Pen]::new($edgeColour, [float][Math]::Max(1, $Size * 0.05))
        $graphics.DrawEllipse($pen, $box)
        $pen.Dispose()

        # Centre hole, so it reads as a disc rather than a green dot.
        $hole = [float]($Size * 0.26)
        $holeBox = [System.Drawing.RectangleF]::new((($Size - $hole) / 2), (($Size - $hole) / 2), $hole, $hole)
        $hub = [System.Drawing.SolidBrush]::new($hubColour)
        $graphics.FillEllipse($hub, $holeBox)
        $hub.Dispose()
    }
    finally { $graphics.Dispose() }

    $buffer = [System.IO.MemoryStream]::new()
    $bitmap.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $buffer.ToArray()
}

$images = @($sizes | ForEach-Object { New-DiscPng -Size $_ })

# ICONDIR, then one 16-byte ICONDIRENTRY per image, then the PNG payloads.
$ico = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($ico)
try {
    $writer.Write([uint16]0)              # reserved
    $writer.Write([uint16]1)              # type: icon
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        # 256 is stored as 0 - the width and height fields are a single byte each.
        $dimension = [byte]$(if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] })
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)            # palette entries
        $writer.Write([byte]0)            # reserved
        $writer.Write([uint16]1)          # colour planes
        $writer.Write([uint16]32)         # bits per pixel
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    foreach ($image in $images) { $writer.Write($image) }
    $writer.Flush()

    $full = [System.IO.Path]::GetFullPath($Path)
    [System.IO.File]::WriteAllBytes($full, $ico.ToArray())
    Write-Host "  Wrote $full  ($($ico.Length) bytes, $($sizes.Count) sizes)" -ForegroundColor Green
}
finally { $writer.Dispose() }
