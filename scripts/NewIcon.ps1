$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(64,64)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.ColorTranslator]::FromHtml('#101923'))
$points = [Drawing.PointF[]]@([Drawing.PointF]::new(16,13),[Drawing.PointF]::new(27,18),[Drawing.PointF]::new(35,13),[Drawing.PointF]::new(47,21),[Drawing.PointF]::new(51,33),[Drawing.PointF]::new(45,38),[Drawing.PointF]::new(47,52),[Drawing.PointF]::new(16,52))
$fill = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#173D3A'))
$pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#59DFBD'),2.5)
$graphics.FillPolygon($fill,$points)
$graphics.DrawPolygon($pen,$points)
$guide = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#93A7BA'),1)
$guide.DashStyle = [Drawing.Drawing2D.DashStyle]::Dash
$graphics.DrawLine($guide,30,26,30,45)
$dot = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F3C777'))
$graphics.FillEllipse($dot,12.5,9.5,7,7)
$stream = [IO.MemoryStream]::new()
$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
$png = $stream.ToArray()
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\TekstilCAD.Desktop\Assets\Studio.ico'
$file = [IO.File]::Create($target)
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
$writer.Dispose(); $stream.Dispose(); $dot.Dispose(); $guide.Dispose(); $pen.Dispose(); $fill.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
