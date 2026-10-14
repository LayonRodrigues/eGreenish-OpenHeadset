# Gera src/OpenHeadset.App/Assets/OpenHeadset.ico a partir do ícone "headphones" da Reicon (MIT).
# Uso (precisa de STA por causa do WPF):  powershell -STA -File scripts/make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$icons = Get-Content (Join-Path $root 'src/OpenHeadset.App/Icons.xaml') -Raw
$data = [regex]::Match($icons, '<Geometry x:Key="Icon\.Headphones">([^<]+)</Geometry>').Groups[1].Value
if (-not $data) { throw 'Icon.Headphones não encontrado em Icons.xaml' }
$glyph = [System.Windows.Media.Geometry]::Parse($data)

$tile  = [System.Windows.Media.SolidColorBrush][System.Windows.Media.ColorConverter]::ConvertFromString('#178D84')
$white = [System.Windows.Media.Brushes]::White

function Render-Png([int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $radius = $size * 0.22
    $dc.DrawRoundedRectangle($tile, $null, (New-Object System.Windows.Rect 0, 0, $size, $size), $radius, $radius)
    $glyphSize = $size * 0.66                       # ícone de 24x24 ocupando 66% do quadrado
    $scale = $glyphSize / 24.0
    $offset = ($size - $glyphSize) / 2.0
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform $offset, $offset))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $dc.DrawGeometry($white, $null, $glyph)
    $dc.Pop(); $dc.Pop(); $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = $sizes | ForEach-Object { , (Render-Png $_) }

# Contêiner ICO com imagens PNG (aceito desde o Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()

$dest = Join-Path $root 'src/OpenHeadset.App/Assets'
New-Item -ItemType Directory -Force $dest | Out-Null
[IO.File]::WriteAllBytes((Join-Path $dest 'OpenHeadset.ico'), $out.ToArray())
"OpenHeadset.ico: $([int]($out.Length/1KB)) KB, tamanhos: $($sizes -join ', ')"
