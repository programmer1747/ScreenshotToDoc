# Generates dist/app.ico - screenshot crop marks over a document, drawn with
# GDI+ so the repo carries no binary art.
# Windows Vista+ reads PNG-compressed .ico entries, which keeps this simple.

param([string]$OutPath = (Join-Path $PSScriptRoot '..\dist\app.ico'))

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    $pad = [int](18 * $s)
    $r = [Math]::Max(2, [int](46 * $s))
    $box = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))

    # rounded background
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($box.X, $box.Y, $r, $r, 180, 90)
    $path.AddArc(($box.Right - $r), $box.Y, $r, $r, 270, 90)
    $path.AddArc(($box.Right - $r), ($box.Bottom - $r), $r, $r, 0, 90)
    $path.AddArc($box.X, ($box.Bottom - $r), $r, $r, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $box,
        [System.Drawing.Color]::FromArgb(255, 33, 96, 180),
        [System.Drawing.Color]::FromArgb(255, 20, 44, 92),
        45.0)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # the document being pasted into
    $dw = [int](96 * $s); $dh = [int](116 * $s)
    $dx = [int](($size - $dw) / 2); $dy = [int](($size - $dh) / 2)
    if ($dw -gt 0 -and $dh -gt 0) {
        $g.FillRectangle([System.Drawing.Brushes]::White, $dx, $dy, $dw, $dh)

        $lineBrush = New-Object System.Drawing.SolidBrush(
            [System.Drawing.Color]::FromArgb(255, 150, 170, 200))
        $lh = [Math]::Max(1, [int](8 * $s))
        for ($i = 0; $i -lt 4; $i++) {
            $ly = $dy + [int]((22 + $i * 24) * $s)
            $lw = $dw - [int](32 * $s)
            if ($lw -gt 0) { $g.FillRectangle($lineBrush, ($dx + [int](16 * $s)), $ly, $lw, $lh) }
        }
        $lineBrush.Dispose()
    }

    # screenshot crop marks
    $penW = [Math]::Max(2, [int](11 * $s))
    $pen = New-Object System.Drawing.Pen(
        [System.Drawing.Color]::FromArgb(255, 120, 210, 255), $penW)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'

    $m = [int](40 * $s); $len = [Math]::Max(2, [int](40 * $s))
    $l = $m; $t = $m; $rr = $size - $m; $b = $size - $m

    $g.DrawLines($pen, @(
        (New-Object System.Drawing.Point($l, ($t + $len))),
        (New-Object System.Drawing.Point($l, $t)),
        (New-Object System.Drawing.Point(($l + $len), $t))))
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.Point(($rr - $len), $t)),
        (New-Object System.Drawing.Point($rr, $t)),
        (New-Object System.Drawing.Point($rr, ($t + $len)))))
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.Point($l, ($b - $len))),
        (New-Object System.Drawing.Point($l, $b)),
        (New-Object System.Drawing.Point(($l + $len), $b))))
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.Point(($rr - $len), $b)),
        (New-Object System.Drawing.Point($rr, $b)),
        (New-Object System.Drawing.Point($rr, ($b - $len)))))
    $pen.Dispose()

    $g.Dispose()
    return $bmp
}

$sizes = @(256, 64, 48, 32, 16)
$blobs = @()
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

$outDir = Split-Path -Parent $OutPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$fs = [System.IO.File]::Create($OutPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: icon
$bw.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = $sizes[$i]
    $dimByte = if ($dim -ge 256) { 0 } else { $dim }   # 0 means 256 in the ICO header
    $bw.Write([byte]$dimByte)                          # width
    $bw.Write([byte]$dimByte)                          # height
    $bw.Write([byte]0)                                 # palette colours
    $bw.Write([byte]0)                                 # reserved
    $bw.Write([uint16]1)                               # colour planes
    $bw.Write([uint16]32)                              # bits per pixel
    $bw.Write([uint32]$blobs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($b in $blobs) { $bw.Write($b) }

$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host ("icon written: {0} ({1} bytes)" -f $OutPath, (Get-Item $OutPath).Length)
