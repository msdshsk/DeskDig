<#
.SYNOPSIS
  DeskDig のアプリアイコン（.ico）を生成する。

.DESCRIPTION
  暗い角丸の背景に 4 枚のタイルを置き、右下の 1 枚だけを黄色にして「掘り出した 1 枚」を表す。
  16 / 20 / 24 / 32 / 48 / 64 / 128 / 256 px を PNG 圧縮で 1 つの .ico にまとめる。
  出力先: src/DeskDig/Assets/DeskDig.ico（csproj の ApplicationIcon で参照）

.EXAMPLE
  pwsh scripts/make-icon.ps1
#>
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\DeskDig\Assets\DeskDig.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$background = [System.Drawing.Color]::FromArgb(40, 44, 52)    # #282C34
$tile       = [System.Drawing.Color]::FromArgb(97, 175, 239)  # #61AFEF
$accent     = [System.Drawing.Color]::FromArgb(229, 192, 123) # #E5C07B

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0) { $path.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h)); return $path }
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 比率は 32px 基準: 背景 2..30、タイル 8px、間隔 4px
    $u = $size / 32.0
    $bgRect = New-RoundedRect (2 * $u) (2 * $u) (28 * $u) (28 * $u) ([Math]::Max(1, 5 * $u))
    $g.FillPath((New-Object System.Drawing.SolidBrush $background), $bgRect)

    $tileSize = 8 * $u
    $tileRadius = [Math]::Max(0.5, 1.5 * $u)
    $positions = @(
        @{ X = 6;  Y = 6;  Color = $tile },
        @{ X = 18; Y = 6;  Color = $tile },
        @{ X = 6;  Y = 18; Color = $tile },
        @{ X = 18; Y = 18; Color = $accent }
    )
    foreach ($p in $positions) {
        $brush = New-Object System.Drawing.SolidBrush $p.Color
        $g.FillPath($brush, (New-RoundedRect ($p.X * $u) ($p.Y * $u) $tileSize $tileSize $tileRadius))
        $brush.Dispose()
    }
    $g.Dispose()
    return $bmp
}

# PNG 圧縮エントリの .ico を組み立てる
$entries = foreach ($s in $sizes) {
    $bmp = Render-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
}

$dir = Split-Path $OutputPath -Parent
New-Item -ItemType Directory -Force $dir | Out-Null
$fs = [System.IO.File]::Create($OutputPath)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0)                 # reserved
$w.Write([uint16]1)                 # type: icon
$w.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $w.Write([byte]$dim)            # width (0 = 256)
    $w.Write([byte]$dim)            # height
    $w.Write([byte]0)               # color count
    $w.Write([byte]0)               # reserved
    $w.Write([uint16]1)             # planes
    $w.Write([uint16]32)            # bit count
    $w.Write([uint32]$e.Bytes.Length)
    $w.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $w.Write($e.Bytes) }
$w.Dispose(); $fs.Dispose()

"written: $OutputPath ($([math]::Round((Get-Item $OutputPath).Length / 1KB)) KB, sizes: $($sizes -join ', '))"
