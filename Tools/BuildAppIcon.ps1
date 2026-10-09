param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetPath = Join-Path $ProjectRoot 'Source/Assets'
$source = [System.Drawing.Image]::FromFile((Join-Path $assetPath 'AppIcon.png'))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @()
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $buffer = [System.IO.MemoryStream]::new()
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,$buffer.ToArray()
        } finally { $buffer.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [System.IO.File]::Create((Join-Path $assetPath 'AppIcon.ico'))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose() }
} finally { $source.Dispose() }
Write-Output ('AppIcon.ico: ' + ($sizes -join ', ') + ' pixels, transparent PNG frames')
