param(
    [int]$ProcessId = 0,

    [long]$WindowHandle = 0,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [switch]$PreserveWindowState
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class SocyviaWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
'@

[SocyviaWindowCapture]::SetProcessDPIAware() | Out-Null

$window = if ($WindowHandle -ne 0) {
    [IntPtr]$WindowHandle
}
else {
    if ($ProcessId -eq 0) {
        throw "Provide ProcessId or WindowHandle."
    }
    (Get-Process -Id $ProcessId).MainWindowHandle
}
if ($window -eq [IntPtr]::Zero) {
    throw "The requested process/window has no drawable main window."
}

if (-not $PreserveWindowState) {
    [SocyviaWindowCapture]::ShowWindow($window, 9) | Out-Null
}
[SocyviaWindowCapture]::SetForegroundWindow($window) | Out-Null
Start-Sleep -Milliseconds 450

$rect = New-Object SocyviaWindowCapture+RECT
if (-not [SocyviaWindowCapture]::GetWindowRect($window, [ref]$rect)) {
    throw "Unable to resolve the SOCYVIA window bounds."
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "SOCYVIA returned invalid window bounds: ${width}x${height}."
}

$fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullOutputPath)) | Out-Null

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $deviceContext = $graphics.GetHdc()
    try {
        if (-not [SocyviaWindowCapture]::PrintWindow($window, $deviceContext, 2)) {
            throw "Unable to render the SOCYVIA window into the QA capture."
        }
    }
    finally {
        $graphics.ReleaseHdc($deviceContext)
    }
    $bitmap.Save($fullOutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Output $fullOutputPath
