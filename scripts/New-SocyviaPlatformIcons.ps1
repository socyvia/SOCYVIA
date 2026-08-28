param(
    [Parameter(Mandatory = $true)] [string]$MacIconPath,
    [Parameter(Mandatory = $true)] [string]$LinuxIconPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path (Join-Path $PSScriptRoot '..\Assets\Branding\socyvia-mark.png')).Path
$source = [System.Drawing.Image]::FromFile($sourcePath)

function New-PngBytes([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $scale = [Math]::Min($size / $source.Width, $size / $source.Height)
        $width = [int][Math]::Round($source.Width * $scale)
        $height = [int][Math]::Round($source.Height * $scale)
        $left = [int](($size - $width) / 2)
        $top = [int](($size - $height) / 2)
        $graphics.DrawImage($source, $left, $top, $width, $height)
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-BigEndianInt32([System.IO.Stream]$stream, [int]$value) {
    $bytes = [BitConverter]::GetBytes([System.Net.IPAddress]::HostToNetworkOrder($value))
    $stream.Write($bytes, 0, $bytes.Length)
}

try {
    $linuxFullPath = [System.IO.Path]::GetFullPath($LinuxIconPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($linuxFullPath)) | Out-Null
    [System.IO.File]::WriteAllBytes($linuxFullPath, (New-PngBytes 256))

    $chunks = [ordered]@{
        'icp4' = New-PngBytes 16
        'icp5' = New-PngBytes 32
        'icp6' = New-PngBytes 64
        'ic07' = New-PngBytes 128
        'ic08' = New-PngBytes 256
        'ic09' = New-PngBytes 512
        'ic10' = New-PngBytes 1024
    }
    $macFullPath = [System.IO.Path]::GetFullPath($MacIconPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($macFullPath)) | Out-Null
    $output = [System.IO.File]::Create($macFullPath)
    try {
        $header = [System.Text.Encoding]::ASCII.GetBytes('icns')
        $output.Write($header, 0, $header.Length)
        $totalLength = 8 + (($chunks.GetEnumerator() | ForEach-Object { 8 + $_.Value.Length }) | Measure-Object -Sum).Sum
        Write-BigEndianInt32 $output ([int]$totalLength)
        foreach ($chunk in $chunks.GetEnumerator()) {
            $type = [System.Text.Encoding]::ASCII.GetBytes($chunk.Key)
            $output.Write($type, 0, $type.Length)
            Write-BigEndianInt32 $output (8 + $chunk.Value.Length)
            $output.Write($chunk.Value, 0, $chunk.Value.Length)
        }
    }
    finally { $output.Dispose() }
}
finally { $source.Dispose() }

Write-Output $MacIconPath
Write-Output $LinuxIconPath
