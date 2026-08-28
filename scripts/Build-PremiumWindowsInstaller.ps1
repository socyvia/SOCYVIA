[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$download = Join-Path $workspace 'Download'
$portable = Join-Path $download 'Windows\SOCYVIA-1.0.0-Windows-x64-Portable.zip'
$publicSetup = Join-Path $download 'Windows\SOCYVIA-1.0.0-Windows-x64-Setup.exe'
$staging = Join-Path $workspace 'artifacts\premium-installer'
$payload = Join-Path $staging 'payload'
$engine = Join-Path $staging 'SOCYVIA-1.0.0-Windows-x64-Engine.exe'
$source = Join-Path $workspace 'packaging\windows\PremiumBootstrapper\Program.cs'
$uninstallSource = Join-Path $workspace 'packaging\windows\PremiumUninstaller\Program.cs'
$uninstallUi = Join-Path $payload 'SOCYVIA.Uninstall.exe'
$manifest = Join-Path $workspace 'packaging\windows\PremiumBootstrapper\app.manifest'
$icon = Join-Path $workspace 'Assets\Branding\socyvia-mark.ico'
$logo = Join-Path $workspace 'Assets\Branding\socyvia-mark.png'
$fontRegular = Join-Path $workspace 'Assets\Fonts\IBMPlexSans-Regular.ttf'
$fontSemiBold = Join-Path $workspace 'Assets\Fonts\IBMPlexSans-SemiBold.ttf'
$fontArabicRegular = Join-Path $workspace 'Assets\Fonts\IBMPlexSansArabic-Regular.ttf'
$fontArabicSemiBold = Join-Path $workspace 'Assets\Fonts\IBMPlexSansArabic-SemiBold.ttf'
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $portable)) { throw "Preserved Windows portable payload is missing: $portable" }
if (-not (Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 compiler is unavailable.' }
if (-not (Test-Path -LiteralPath $csc)) { throw '.NET Framework C# compiler is unavailable.' }

$resolvedStaging = [System.IO.Path]::GetFullPath($staging)
$expectedStaging = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts\premium-installer'))
if ($resolvedStaging -ne $expectedStaging) { throw "Unsafe staging path: $resolvedStaging" }
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

try {
    Expand-Archive -LiteralPath $portable -DestinationPath $payload
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'SOCYVIA.exe'))) {
        throw 'The preserved portable archive does not contain SOCYVIA.exe at its root.'
    }

    $uninstallCompilerArguments = @(
        '/nologo', '/utf8output', '/target:winexe', '/platform:x64', '/optimize+', '/debug-',
        "/out:$uninstallUi", "/win32icon:$icon", "/win32manifest:$manifest",
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll',
        "/resource:$logo,Socyvia.Logo.png",
        "/resource:$icon,Socyvia.Icon.ico",
        "/resource:$fontRegular,Socyvia.Font.Regular",
        "/resource:$fontSemiBold,Socyvia.Font.SemiBold",
        "/resource:$fontArabicRegular,Socyvia.Font.ArabicRegular",
        "/resource:$fontArabicSemiBold,Socyvia.Font.ArabicSemiBold",
        $uninstallSource
    )
    & $csc $uninstallCompilerArguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $uninstallUi)) {
        throw 'The branded SOCYVIA uninstaller could not be built.'
    }

    & $iscc /Qp "/DPayloadDir=$payload" "/DEngineOutputDir=$staging" `
        (Join-Path $workspace 'packaging\windows\SOCYVIA.iss')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $engine)) {
        throw 'The silent SOCYVIA installation engine could not be built.'
    }

    $compilerArguments = @(
        '/nologo', '/utf8output', '/target:winexe', '/platform:x64', '/optimize+', '/debug-',
        "/out:$publicSetup", "/win32icon:$icon", "/win32manifest:$manifest",
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll',
        "/resource:$engine,Socyvia.Engine.exe",
        "/resource:$logo,Socyvia.Logo.png",
        "/resource:$icon,Socyvia.Icon.ico",
        "/resource:$fontRegular,Socyvia.Font.Regular",
        "/resource:$fontSemiBold,Socyvia.Font.SemiBold",
        "/resource:$fontArabicRegular,Socyvia.Font.ArabicRegular",
        "/resource:$fontArabicSemiBold,Socyvia.Font.ArabicSemiBold",
        $source
    )
    & $csc $compilerArguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $publicSetup)) {
        throw 'The premium SOCYVIA Setup bootstrapper could not be built.'
    }

    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($publicSetup)
    if ($version.ProductName -ne 'SOCYVIA' -or $version.ProductVersion -ne '1.0.0') {
        throw "Unexpected Setup metadata: $($version.ProductName) $($version.ProductVersion)"
    }

    Get-Item -LiteralPath $publicSetup | Select-Object FullName,Length,@{Name='SHA256';Expression={(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
