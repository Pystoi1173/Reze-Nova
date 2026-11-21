Add-Type -AssemblyName System.Drawing
$root = $PSScriptRoot
$source = Join-Path $root 'assets\ui\language-source.jpg'
$destination = Join-Path $root 'assets\ui\language.ico'
$bitmap = New-Object Drawing.Bitmap 256, 256
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.Clear([Drawing.Color]::Black)
$image = [Drawing.Image]::FromFile($source)
$scale = [Math]::Max(256.0 / $image.Width, 256.0 / $image.Height)
$width = [int]($image.Width * $scale)
$height = [int]($image.Height * $scale)
$x = [int]((256 - $width) / 2)
$y = [int]((256 - $height) / 2)
$graphics.DrawImage($image, $x, $y, $width, $height)
$image.Dispose()
$graphics.Dispose()
$png = Join-Path $root 'assets\ui\language.png'
$bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
$bytes = [IO.File]::ReadAllBytes($png)
$stream = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]1)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]32)
$writer.Write([UInt32]$bytes.Length)
$writer.Write([UInt32]22)
$writer.Write($bytes)
$writer.Flush()
[IO.File]::WriteAllBytes($destination, $stream.ToArray())
$writer.Dispose()
$stream.Dispose()
Remove-Item -LiteralPath $png -Force
Copy-Item -LiteralPath $destination -Destination (Join-Path $root 'dist\assets\ui\language.ico') -Force
