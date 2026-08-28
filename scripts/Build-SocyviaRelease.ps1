[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$download = Join-Path $workspace 'Download'
$staging = Join-Path $workspace 'artifacts\release-staging'

function Assert-ReleasePath([string]$path, [string]$expectedLeaf) {
    $full = [System.IO.Path]::GetFullPath($path)
    if ([System.IO.Path]::GetDirectoryName($full) -ne $workspace -and
        [System.IO.Path]::GetDirectoryName($full) -ne (Join-Path $workspace 'artifacts')) {
        throw "Release path is outside the approved workspace scope: $full"
    }
    if ([System.IO.Path]::GetFileName($full) -ne $expectedLeaf) {
        throw "Unexpected release target: $full"
    }
}

Assert-ReleasePath $download 'Download'
Assert-ReleasePath $staging 'release-staging'
if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Recurse -Force }
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }

@('Windows','macOS','Linux','Checksums') | ForEach-Object {
    New-Item -ItemType Directory -Force -Path (Join-Path $download $_) | Out-Null
}
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Push-Location $workspace
try {
    dotnet restore SOCYVIA.csproj --disable-parallel
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    foreach ($rid in @('win-x64','osx-arm64','osx-x64','linux-x64')) {
        $output = Join-Path $staging $rid
        dotnet publish SOCYVIA.csproj -c Release -r $rid --self-contained true --no-restore `
            -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
            -p:Version=1.0.0 -o $output
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
        Get-ChildItem -LiteralPath $output -Recurse -File |
            Where-Object Extension -eq '.pdb' |
            Remove-Item -Force
    }

    $archiveProject = Join-Path $workspace 'scripts\ReleaseArchiveTool\ReleaseArchiveTool.csproj'
    dotnet run --project $archiveProject -c Release -- zip `
        (Join-Path $staging 'win-x64') `
        (Join-Path $download 'Windows\SOCYVIA-1.0.0-Windows-x64-Portable.zip') `
        - SOCYVIA.exe
    if ($LASTEXITCODE -ne 0) { throw 'Windows portable archive failed.' }

    $iconStage = Join-Path $staging 'platform-icons'
    $macIcon = Join-Path $iconStage 'socyvia-mark.icns'
    $linuxIcon = Join-Path $iconStage 'socyvia.png'
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'New-SocyviaPlatformIcons.ps1') `
        -MacIconPath $macIcon -LinuxIconPath $linuxIcon | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Platform icon generation failed.' }

    foreach ($mac in @(
        @{ Rid='osx-arm64'; File='SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip' },
        @{ Rid='osx-x64'; File='SOCYVIA-1.0.0-macOS-Intel-x64.zip' })) {
        $bundle = Join-Path $staging "$($mac.Rid)-bundle\SOCYVIA.app"
        $contents = Join-Path $bundle 'Contents'
        $macos = Join-Path $contents 'MacOS'
        $resources = Join-Path $contents 'Resources'
        New-Item -ItemType Directory -Force -Path $macos,$resources | Out-Null
        Copy-Item -LiteralPath (Join-Path $workspace 'packaging\macos\Info.plist') -Destination $contents
        Copy-Item -LiteralPath $macIcon -Destination (Join-Path $resources 'socyvia-mark.icns')
        Copy-Item -Path (Join-Path $staging "$($mac.Rid)\*") -Destination $macos -Recurse -Force
        dotnet run --project $archiveProject -c Release -- zip $bundle `
            (Join-Path $download "macOS\$($mac.File)") SOCYVIA.app Contents/MacOS/SOCYVIA
        if ($LASTEXITCODE -ne 0) { throw "macOS archive failed for $($mac.Rid)." }
    }

    $linuxRootName = 'SOCYVIA-1.0.0-Linux-x64'
    $linuxBundle = Join-Path $staging "linux-bundle\$linuxRootName"
    New-Item -ItemType Directory -Force -Path $linuxBundle | Out-Null
    Copy-Item -Path (Join-Path $staging 'linux-x64\*') -Destination $linuxBundle -Recurse -Force
    Copy-Item -LiteralPath $linuxIcon -Destination (Join-Path $linuxBundle 'socyvia.png')
    Copy-Item -LiteralPath (Join-Path $workspace 'packaging\linux\socyvia.desktop') -Destination $linuxBundle
    Set-Content -LiteralPath (Join-Path $linuxBundle 'README.txt') -Encoding UTF8 -Value @(
        'SOCYVIA 1.0.0 for Linux x64',
        '',
        'Run ./SOCYVIA from this directory. The package is self-contained and does not require the .NET SDK.'
    )
    dotnet run --project $archiveProject -c Release -- tar $linuxBundle `
        (Join-Path $download 'Linux\SOCYVIA-1.0.0-Linux-x64.tar.gz') $linuxRootName SOCYVIA
    if ($LASTEXITCODE -ne 0) { throw 'Linux archive failed.' }

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $PSScriptRoot 'Build-PremiumWindowsInstaller.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Premium Windows installer compilation failed.' }

    Copy-Item -LiteralPath (Join-Path $workspace 'packaging\README.txt') -Destination (Join-Path $download 'README.txt')
}
finally { Pop-Location }

Get-ChildItem -LiteralPath $download -Recurse -File | Select-Object FullName,Length
