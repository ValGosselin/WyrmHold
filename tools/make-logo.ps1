# Fabrique le logo de Wyrmhold (« l'œil du wyrm », choisi le 9 octobre 2026) à partir de formes simples :
#   WyrmHold.App\Assets\wyrmhold.ico      icône du programme et de l'installateur (16 à 256 px)
#   WyrmHold.App\Assets\logo-256.png      logo affiché dans l'appli
#   WyrmHold.App\Assets\logo-512.png      image affichée pendant l'installation
# Lancer depuis la racine du dépôt :  powershell -ExecutionPolicy Bypass -File tools\make-logo.ps1
#
# Les formes sont décrites sur une grille de 64 × 64 (comme le dessin SVG d'origine, logo.svg),
# puis agrandies à chaque taille. En dessous de 48 px, une version simplifiée (œil plus large,
# sans sourcils) reste lisible dans la barre des tâches.

Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\WyrmHold.App\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

$background = [System.Drawing.ColorTranslator]::FromHtml("#2C2C2A")
$eye        = [System.Drawing.ColorTranslator]::FromHtml("#BA7517")
$brow       = [System.Drawing.ColorTranslator]::FromHtml("#FAC775")

# Une forme en amande faite de deux courbes de Bézier : de (x0, 32) à (64 - x0, 32), en passant par le haut puis par le bas.
function New-Almond([float]$x0, [float]$cx1, [float]$cy1, [float]$cx2, [float]$cy2) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier($x0, 32, $cx1, $cy1, $cx2, $cy1, (64 - $x0), 32)
    $path.AddBezier((64 - $x0), 32, $cx2, $cy2, $cx1, $cy2, $x0, 32)
    $path.CloseFigure()
    return $path
}

# La pupille fendue : deux courbes verticales entre (32, top) et (32, 64 - top).
function New-Pupil([float]$top, [float]$side) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier(32, $top, (32 - $side), ($top + 7), (32 - $side), (57 - $top), 32, (64 - $top))
    $path.AddBezier(32, (64 - $top), (32 + $side), (57 - $top), (32 + $side), ($top + 7), 32, $top)
    $path.CloseFigure()
    return $path
}

function New-RoundedSquare([float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc((64 - $d), 0, $d, $d, 270, 90)
    $path.AddArc((64 - $d), (64 - $d), $d, $d, 0, 90)
    $path.AddArc(0, (64 - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-LogoBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform(($size / 64.0), ($size / 64.0))

    $g.FillPath((New-Object System.Drawing.SolidBrush $background), (New-RoundedSquare 14))

    if ($size -le 16) {
        $g.FillPath((New-Object System.Drawing.SolidBrush $eye), (New-Almond 4 16 13 48 51))
        $g.FillPath((New-Object System.Drawing.SolidBrush $background), (New-Pupil 16 9))
    }
    elseif ($size -le 32) {
        $g.FillPath((New-Object System.Drawing.SolidBrush $eye), (New-Almond 6 17 15 47 49))
        $g.FillPath((New-Object System.Drawing.SolidBrush $background), (New-Pupil 18 8))
    }
    else {
        $g.FillPath((New-Object System.Drawing.SolidBrush $eye), (New-Almond 8 18 16 46 48))
        $g.FillPath((New-Object System.Drawing.SolidBrush $background), (New-Pupil 19 7))

        $pen = New-Object System.Drawing.Pen $brow, 3
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($pen, 14, 18, 22, 23)
        $g.DrawLine($pen, 50, 18, 42, 23)
    }

    $g.Dispose()
    return $bitmap
}

function Get-PngBytes([System.Drawing.Bitmap]$bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $stream.ToArray()   # la virgule : renvoyer le tableau entier, pas octet par octet
}

# Format « classique » d'une image d'icône (BMP 32 bits) : lu par tous les outils de Windows,
# contrairement au PNG pour les petites tailles. En-tête de 40 octets, pixels de bas en haut,
# puis un masque 1 bit (tout à zéro : la transparence vient du canal alpha).
function Get-DibBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $stream = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $stream

    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($size * $size * 4 + $maskRow * $size))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bitmap.UnlockBits($data)

    for ($y = $size - 1; $y -ge 0; $y--) {
        $w.Write($pixels, $y * $data.Stride, $size * 4)
    }

    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return , $stream.ToArray()   # la virgule : renvoyer le tableau entier, pas octet par octet
}

# --- Le fichier .ico : un en-tête, une entrée par taille, puis les images. 256 px en PNG (plus léger). ---
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
    $bitmap = New-LogoBitmap $s
    if ($s -eq 256) { $images.Add((Get-PngBytes $bitmap)) } else { $images.Add((Get-DibBytes $bitmap)) }
    $bitmap.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dimension = if ($s -ge 256) { 0 } else { $s }   # 0 veut dire 256 dans ce format
    $w.Write([byte]$dimension); $w.Write([byte]$dimension); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write($image) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "wyrmhold.ico"), $ico.ToArray())

foreach ($s in 256, 512) {
    $bitmap = New-LogoBitmap $s
    $bitmap.Save((Join-Path $assets "logo-$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

Write-Host "Logo créé dans $assets"
