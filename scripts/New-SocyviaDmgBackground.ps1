[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $workspace 'packaging\macos\socyvia-dmg-background.png'
} else { $OutputPath }
$logoPath = Join-Path $workspace 'Assets\Branding\socyvia-mark.png'
$fontPath = Join-Path $workspace 'Assets\Fonts\IBMPlexSans-SemiBold.ttf'
$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null

$bitmap = [Drawing.Bitmap]::new(760,460,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$logo = [Drawing.Image]::FromFile($logoPath)
$fonts = [Drawing.Text.PrivateFontCollection]::new()
try {
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([Drawing.Color]::FromArgb(255,247,250,255))

    $softBlue = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(40,43,105,230))
    $graphics.FillEllipse($softBlue,-120,300,440,300)
    $graphics.FillEllipse($softBlue,520,-130,360,300)
    $softBlue.Dispose()

    $graphics.DrawImage($logo,[Drawing.Rectangle]::new(343,34,74,74))
    $fonts.AddFontFile($fontPath)
    $family = $fonts.Families[0]
    $titleFont = [Drawing.Font]::new($family,23,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
    $captionFont = [Drawing.Font]::new($family,14,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
    $ink = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,10,33,71))
    $muted = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,84,108,151))
    $blue = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255,43,105,230),5)
    $blue.EndCap = [Drawing.Drawing2D.LineCap]::ArrowAnchor
    $center = [Drawing.StringFormat]::new()
    $center.Alignment = [Drawing.StringAlignment]::Center
    $graphics.DrawString('SOCYVIA',$titleFont,$ink,[Drawing.RectangleF]::new(0,114,760,42),$center)
    $graphics.DrawString('Scientific Desktop  |  1.0.0',$captionFont,$muted,[Drawing.RectangleF]::new(0,154,760,28),$center)
    $graphics.DrawLine($blue,292,282,468,282)
    $graphics.DrawString('Drag SOCYVIA to Applications',$captionFont,$muted,[Drawing.RectangleF]::new(0,390,760,30),$center)

    $center.Dispose(); $blue.Dispose(); $muted.Dispose(); $ink.Dispose(); $captionFont.Dispose(); $titleFont.Dispose()
    $bitmap.Save($output,[Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $fonts.Dispose(); $logo.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
Get-Item -LiteralPath $output | Select-Object FullName,Length
