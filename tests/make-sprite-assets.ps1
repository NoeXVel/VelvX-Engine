Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore,WindowsBase

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sprites = Join-Path $root "Templates/Starter-2D/Assets/Sprites"
New-Item -ItemType Directory -Force -Path $sprites | Out-Null

function Add-Rect($g, [int]$x, [int]$y, [int]$w, [int]$h, [string]$hex) {
    $b = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($hex))
    $g.FillRectangle($b, $x, $y, $w, $h)
    $b.Dispose()
}

# Sprite sheet do herói: 4 colunas x 1 linha, 32x32 por quadro.
$sheet = New-Object System.Drawing.Bitmap(128, 32)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::Transparent)
for ($i = 0; $i -lt 4; $i++) {
    $ox = $i * 32
    $bob = @(0, 1, 0, -1)[$i]
    Add-Rect $g ($ox + 11) (8 + $bob) 10 12 '#A98BFF'
    Add-Rect $g ($ox + 12) (4 + $bob) 8 7 '#D8C8FF'
    Add-Rect $g ($ox + 13) (6 + $bob) 2 2 '#1A1428'
    Add-Rect $g ($ox + 17) (6 + $bob) 2 2 '#1A1428'
    Add-Rect $g ($ox + 9) (20 + $bob) 4 7 '#6C4ED8'
    Add-Rect $g ($ox + 19) (20 + $bob) 4 7 '#6C4ED8'
    Add-Rect $g ($ox + 11) (27 + $bob) 10 3 '#33265C'
}
$g.Dispose()
$sheet.Save((Join-Path $sprites "hero_sheet.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

# Estrela coletável.
$star = New-Object System.Drawing.Bitmap(32, 32)
$g = [System.Drawing.Graphics]::FromImage($star)
$g.Clear([System.Drawing.Color]::Transparent)
Add-Rect $g 14 5 4 22 '#FFD37E'
Add-Rect $g 8 11 16 4 '#FFD37E'
Add-Rect $g 10 8 4 4 '#FFF0BC'
Add-Rect $g 18 8 4 4 '#FFF0BC'
Add-Rect $g 14 13 4 12 '#F2A93B'
$g.Dispose()
$star.Save((Join-Path $sprites "star.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$star.Dispose()

# GIF animado do orbe.
$encoder = New-Object System.Windows.Media.Imaging.GifBitmapEncoder
for ($f = 0; $f -lt 6; $f++) {
    $frame = New-Object System.Drawing.Bitmap(32, 32)
    $g = [System.Drawing.Graphics]::FromImage($frame)
    $g.Clear([System.Drawing.Color]::Transparent)
    $radius = 9 + ($f % 3)
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#8B6CFF'))
    $g.FillEllipse($brush, 16 - $radius, 16 - $radius, $radius * 2, $radius * 2)
    $brush.Dispose()
    $glow = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#E0D8FF'))
    $g.FillEllipse($glow, 10, 9, 5, 4)
    $glow.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $frame.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $ms.Position = 0
    $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
        $ms,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $encoder.Frames.Add($decoder.Frames[0])
    $frame.Dispose()
    $ms.Dispose()
}
$out = New-Object System.IO.FileStream((Join-Path $sprites "orb.gif"), [System.IO.FileMode]::Create)
$encoder.Save($out)
$out.Dispose()

Write-Output "ASSETS OK -> $sprites"
