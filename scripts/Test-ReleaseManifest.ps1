[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$download = Join-Path $workspace 'Download'
$manifestPath = Join-Path $download 'release-manifest.json'
$checksumsPath = Join-Path $download 'Checksums\SHA256SUMS.txt'
$readmePath = Join-Path $download 'README.txt'
foreach ($path in $manifestPath,$checksumsPath,$readmePath) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Release metadata is missing: $path" }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.product -ne 'SOCYVIA' -or $manifest.version -ne '1.0.0' -or
    $manifest.tag -ne 'v1.0.0' -or $manifest.releaseTitle -ne 'SOCYVIA 1.0.0') {
    throw 'Release manifest identity is invalid.'
}

$roles = [ordered]@{
    'Windows/SOCYVIA-1.0.0-Windows-x64-Setup.exe'='recommended'
    'Windows/SOCYVIA-1.0.0-Windows-x64-Portable.zip'='alternative'
    'macOS/SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip'='alternative'
    'macOS/SOCYVIA-1.0.0-macOS-Intel-x64.zip'='alternative'
    'Linux/SOCYVIA-1.0.0-Linux-x64.AppImage'='recommended'
    'Linux/SOCYVIA-1.0.0-Linux-x64.deb'='alternative'
    'Linux/SOCYVIA-1.0.0-Linux-x64.tar.gz'='advanced'
}
if (@($manifest.artifacts).Count -ne $roles.Count) { throw 'Release manifest artifact count is invalid.' }

$checksumMap = @{}
foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding UTF8) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Malformed checksum line: $line" }
    $checksumMap[$matches[2]] = $matches[1]
}
if ($checksumMap.Count -ne $roles.Count) { throw 'Checksum file artifact count is invalid.' }

foreach ($relative in $roles.Keys) {
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('..')) { throw "Unsafe public path: $relative" }
    $artifact = @($manifest.artifacts | Where-Object filename -eq $relative)
    if ($artifact.Count -ne 1) { throw "Manifest entry is missing or duplicated: $relative" }
    $artifact = $artifact[0]
    if ($artifact.distributionRole -ne $roles[$relative] -or
        [bool]$artifact.recommended -ne ($roles[$relative] -eq 'recommended')) {
        throw "Manifest role is incorrect: $relative"
    }
    $path = Join-Path $download $relative.Replace('/','\')
    if (-not (Test-Path -LiteralPath $path)) { throw "Manifest artifact is missing: $relative" }
    $file = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([long]$artifact.size -ne $file.Length -or $artifact.sha256 -ne $hash -or $checksumMap[$file.Name] -ne $hash) {
        throw "Manifest/checksum mismatch: $relative"
    }
    if ($artifact.packageVerificationStatus -ne 'PACKAGE VERIFIED') { throw "Package verification status is missing: $relative" }
}

$pending = @($manifest.pendingArtifacts)
if ($pending.Count -ne 2) { throw 'Expected exactly two pending macOS DMG entries.' }
foreach ($entry in $pending) {
    $preparedStatus = 'DMG PACKAGING PREPARED ' + [char]0x2014 + ' macOS HOST REQUIRED'
    if ($entry.artifactType -ne 'disk-image' -or $entry.distributionRole -ne 'recommended' -or
        $entry.status -ne $preparedStatus) {
        throw 'Pending DMG metadata is invalid.'
    }
    if (Test-Path -LiteralPath (Join-Path $download $entry.filename.Replace('/','\'))) {
        throw "A DMG exists but has not been added to the verified artifact set: $($entry.filename)"
    }
}

$allowed = @(
    'README.txt','release-manifest.json','Checksums\SHA256SUMS.txt',
    'Windows\SOCYVIA-1.0.0-Windows-x64-Setup.exe','Windows\SOCYVIA-1.0.0-Windows-x64-Portable.zip',
    'macOS\SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip','macOS\SOCYVIA-1.0.0-macOS-Intel-x64.zip',
    'Linux\SOCYVIA-1.0.0-Linux-x64.AppImage','Linux\SOCYVIA-1.0.0-Linux-x64.deb','Linux\SOCYVIA-1.0.0-Linux-x64.tar.gz'
)
$actual = @(Get-ChildItem -LiteralPath $download -Recurse -File | ForEach-Object {
    $_.FullName.Substring($download.Length + 1)
})
$difference = Compare-Object ($allowed | Sort-Object) ($actual | Sort-Object)
if ($difference) { throw "Download contains missing or unexpected files:`n$($difference | Out-String)" }

$readme = [IO.File]::ReadAllText($readmePath,[Text.Encoding]::UTF8)
foreach ($name in $roles.Keys | ForEach-Object { [IO.Path]::GetFileName($_) }) {
    if (-not $readme.Contains($name)) { throw "README does not mention $name." }
}
foreach ($phrase in 'unsigned','not notarized','not physically tested on macOS','physical Linux QA') {
    if ($readme.IndexOf($phrase,[StringComparison]::OrdinalIgnoreCase) -lt 0) { throw "README lacks status disclosure '$phrase'." }
}

[ordered]@{
    result='PASS'
    identity='SOCYVIA 1.0.0 / v1.0.0'
    realArtifacts=$roles.Count
    pendingMacOSDmgs=$pending.Count
    manifestHashes='MATCH'
    checksumHashes='MATCH'
    downloadTree='CLEAN'
    readme='VALID'
} | ConvertTo-Json -Depth 4
