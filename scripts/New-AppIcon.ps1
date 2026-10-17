param([string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Quickfetch\Assets\Quickfetch.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common
$parent = Split-Path $Output -Parent
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.RectangleF]::new(0,0,32,32),[System.Drawing.ColorTranslator]::FromHtml('#5689F5'),[System.Drawing.ColorTranslator]::FromHtml('#2854BB'),45)
    $ring = [System.Drawing.Pen]::new([System.Drawing.Color]::White,2.1)
    $bolt = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#A9DDFF'))
    $stream = [System.IO.MemoryStream]::new()
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 32.0, $size / 32.0)
        $path.AddArc(1,1,12,12,180,90); $path.AddArc(19,1,12,12,270,90)
        $path.AddArc(19,19,12,12,0,90); $path.AddArc(1,19,12,12,90,90); $path.CloseFigure()
        $graphics.FillPath($gradient,$path)
        $ring.StartCap = $ring.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($ring,6.5,6,16,16,40,295)
        $graphics.DrawLine($ring,21,21,26,26)
        $graphics.FillPolygon($bolt,[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(16,6.5),[System.Drawing.PointF]::new(10,15),[System.Drawing.PointF]::new(14.5,15),[System.Drawing.PointF]::new(12.5,22),[System.Drawing.PointF]::new(21,11.5),[System.Drawing.PointF]::new(16.5,11.5)))
        $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
        $frames += [pscustomobject]@{Size=$size;Data=$stream.ToArray()}
    }
    finally { $stream.Dispose();$bolt.Dispose();$ring.Dispose();$gradient.Dispose();$path.Dispose();$graphics.Dispose();$bitmap.Dispose() }
}
$file = [System.IO.File]::Create($Output)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([ushort]0);$writer.Write([ushort]1);$writer.Write([ushort]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([ushort]1);$writer.Write([ushort]32);$writer.Write([uint]$frame.Data.Length);$writer.Write([uint]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
}
finally { $writer.Dispose();$file.Dispose() }
Get-Item -LiteralPath $Output | Select-Object FullName,Length
