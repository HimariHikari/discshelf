param(
    [string]$Source = "$PSScriptRoot\..\Assets\discshelf-logo.png",
    [string]$Destination = "$PSScriptRoot\..\Assets\discshelf.ico"
)

# Package transparent PNG frames in a Windows icon, without changing the artwork.
Add-Type -AssemblyName PresentationCore, WindowsBase
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$bitmap = [System.Windows.Media.Imaging.BitmapImage]::new()
$bitmap.BeginInit()
$bitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$bitmap.UriSource = [Uri]::new($sourcePath)
$bitmap.EndInit()
$bitmap.Freeze()
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $scale = $size / [Math]::Max($bitmap.PixelWidth, $bitmap.PixelHeight)
    $width = $bitmap.PixelWidth * $scale
    $height = $bitmap.PixelHeight * $scale
    $drawing.DrawImage($bitmap, [System.Windows.Rect]::new(($size - $width) / 2, ($size - $height) / 2, $width, $height))
    $drawing.Close()
    $target = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $target.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($target))
    $memory = [IO.MemoryStream]::new()
    $encoder.Save($memory)
    [PSCustomObject]@{ Size = $size; Bytes = $memory.ToArray() }
    $memory.Dispose()
}
$file = [IO.File]::Create([IO.Path]::GetFullPath($Destination))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output "Created $Destination with $($frames.Count) transparent icon sizes."
