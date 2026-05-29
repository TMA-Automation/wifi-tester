# Generuje wielorozdzielczościowy .ico dla WifiTester (fala WiFi + soczewka "tester").
# Uruchom: powershell -File tools/make-icon.ps1
param([string]$OutIco = "src/WifiTester.App/wifitester.ico")

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$size

    # Tło: zaokrąglony kwadrat z gradientem granat -> błękit (marka TMA, profesjonalny).
    $pad = [math]::Round($s * 0.06)
    $rectF = New-Object System.Drawing.RectangleF($pad, $pad, ($s - 2*$pad), ($s - 2*$pad))
    $radius = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rectF.X, $rectF.Y, $d, $d, 180, 90)
    $path.AddArc($rectF.Right - $d, $rectF.Y, $d, $d, 270, 90)
    $path.AddArc($rectF.Right - $d, $rectF.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rectF.X, $rectF.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $c1 = [System.Drawing.Color]::FromArgb(255, 12, 36, 84)    # granat
    $c2 = [System.Drawing.Color]::FromArgb(255, 0, 122, 204)   # błękit
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectF, $c1, $c2, 60.0)
    $g.FillPath($brush, $path)

    # Fala WiFi: 3 łuki otwarte do góry + kropka, w jasnym cyjanie.
    $cx = $s * 0.5
    $cy = $s * 0.70                       # środek okręgów (dół-środek)
    $penColor = [System.Drawing.Color]::FromArgb(255, 224, 247, 255)
    $startAngle = 215.0
    $sweep = 110.0

    foreach ($r in @(0.30, 0.21, 0.12)) {
        $rad = $s * $r
        $w = [single]([math]::Max(1.5, $s * 0.055))
        $pen = New-Object System.Drawing.Pen($penColor, $w)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($pen, [single]($cx - $rad), [single]($cy - $rad), [single]($rad*2), [single]($rad*2), [single]$startAngle, [single]$sweep)
        $pen.Dispose()
    }
    # Kropka u podstawy fali.
    $dotR = [single]($s * 0.045)
    $dotBrush = New-Object System.Drawing.SolidBrush($penColor)
    $g.FillEllipse($dotBrush, [single]($cx - $dotR), [single]($cy - $dotR), [single]($dotR*2), [single]($dotR*2))

    $g.Dispose()
    return $bmp
}

# Zbuduj klatkę DIB (BMP) dla wpisu ICO — GDI/NotifyIcon czyta DIB na każdym rozmiarze
# (klatki PNG zawodzą w System.Drawing.Icon, dlatego nie używamy PNG).
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $bd.Stride
    $src = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $src, 0, $src.Length)
    $bmp.UnlockBits($bd)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER (wysokość podwojona: XOR + AND).
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)
    # Piksele BGRA, dół-góra.
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($src, $y * $stride, $w * 4) }
    # Maska AND (zera — przezroczystość bierze się z kanału alfa), wiersz wyrównany do 4 B.
    $maskStride = [int]([math]::Floor(($w + 31) / 32) * 4)
    $bw.Write((New-Object byte[] ($maskStride * $h)), 0, ($maskStride * $h))
    $bw.Flush()
    return $ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.ArrayList
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $dib = Get-DibBytes $bmp
    [void]$frames.Add($dib)
    Write-Output "  klatka $sz px -> $($dib.Length) B"
    $bmp.Dispose()
}

$outPath = Join-Path (Get-Location) $OutIco
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $frames[$i]
    $b = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$b); $bw.Write([byte]$b)   # width, height (0 = 256)
    $bw.Write([byte]0); $bw.Write([byte]0)     # colors, reserved
    $bw.Write([uint16]1); $bw.Write([uint16]32) # planes, bpp
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write([byte[]]$data, 0, ([byte[]]$data).Length) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output "Zapisano: $outPath ($([math]::Round((Get-Item $outPath).Length/1kb,1)) KB)"
