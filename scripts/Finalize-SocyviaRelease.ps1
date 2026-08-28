[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$download = Join-Path $workspace 'Download'
$checksumFolder = Join-Path $download 'Checksums'
$artifacts = @(
    @{ Platform='Windows'; Architecture='x64'; Type='installer'; Role='recommended'; File='Windows\SOCYVIA-1.0.0-Windows-x64-Setup.exe'; Physical='PHYSICALLY VERIFIED ON WINDOWS'; Signing='UNSIGNED' },
    @{ Platform='Windows'; Architecture='x64'; Type='portable-archive'; Role='alternative'; File='Windows\SOCYVIA-1.0.0-Windows-x64-Portable.zip'; Physical='PHYSICALLY VERIFIED ON WINDOWS'; Signing='UNSIGNED' },
    @{ Platform='macOS'; Architecture='arm64'; Type='application-bundle-archive'; Role='alternative'; File='macOS\SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip'; Physical='TARGET OS PHYSICAL QA REQUIRED'; Signing='UNSIGNED / NOT NOTARIZED' },
    @{ Platform='macOS'; Architecture='x64'; Type='application-bundle-archive'; Role='alternative'; File='macOS\SOCYVIA-1.0.0-macOS-Intel-x64.zip'; Physical='TARGET OS PHYSICAL QA REQUIRED'; Signing='UNSIGNED / NOT NOTARIZED' },
    @{ Platform='Linux'; Architecture='x64'; Type='appimage'; Role='recommended'; File='Linux\SOCYVIA-1.0.0-Linux-x64.AppImage'; Physical='TARGET OS PHYSICAL QA REQUIRED'; Signing='UNSIGNED' },
    @{ Platform='Linux'; Architecture='x64'; Type='debian-package'; Role='alternative'; File='Linux\SOCYVIA-1.0.0-Linux-x64.deb'; Physical='TARGET OS PHYSICAL QA REQUIRED'; Signing='UNSIGNED' },
    @{ Platform='Linux'; Architecture='x64'; Type='portable-runtime-archive'; Role='advanced'; File='Linux\SOCYVIA-1.0.0-Linux-x64.tar.gz'; Physical='TARGET OS PHYSICAL QA REQUIRED'; Signing='UNSIGNED' }
)

$manifestArtifacts = @()
$checksumLines = @()
foreach ($artifact in $artifacts) {
    $path = Join-Path $download $artifact.File
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing public artifact: $($artifact.File)" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $file = Get-Item -LiteralPath $path
    $checksumLines += "$hash  $($file.Name)"
    $manifestArtifacts += [ordered]@{
        product = 'SOCYVIA'
        version = '1.0.0'
        platform = $artifact.Platform
        architecture = $artifact.Architecture
        artifactType = $artifact.Type
        distributionRole = $artifact.Role
        recommended = $artifact.Role -eq 'recommended'
        filename = $artifact.File.Replace('\','/')
        size = $file.Length
        sha256 = $hash
        buildStatus = 'BUILT'
        packageVerificationStatus = 'PACKAGE VERIFIED'
        physicalQaStatus = $artifact.Physical
        signingStatus = $artifact.Signing
    }
}

[System.IO.Directory]::CreateDirectory($checksumFolder) | Out-Null
[System.IO.File]::WriteAllLines((Join-Path $checksumFolder 'SHA256SUMS.txt'), $checksumLines, [System.Text.UTF8Encoding]::new($false))
$manifest = [ordered]@{
    product = 'SOCYVIA'
    version = '1.0.0'
    tag = 'v1.0.0'
    releaseTitle = 'SOCYVIA 1.0.0'
    artifacts = $manifestArtifacts
    pendingArtifacts = @(
        [ordered]@{
            platform='macOS'; architecture='arm64'; artifactType='disk-image'; distributionRole='recommended'
            filename='macOS/SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.dmg'
            status=('DMG PACKAGING PREPARED ' + [char]0x2014 + ' macOS HOST REQUIRED')
        },
        [ordered]@{
            platform='macOS'; architecture='x64'; artifactType='disk-image'; distributionRole='recommended'
            filename='macOS/SOCYVIA-1.0.0-macOS-Intel-x64.dmg'
            status=('DMG PACKAGING PREPARED ' + [char]0x2014 + ' macOS HOST REQUIRED')
        }
    )
}
[System.IO.File]::WriteAllText((Join-Path $download 'release-manifest.json'),
    ($manifest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))

Write-Output (Join-Path $checksumFolder 'SHA256SUMS.txt')
Write-Output (Join-Path $download 'release-manifest.json')
