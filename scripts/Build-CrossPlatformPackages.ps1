[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$download = Join-Path $workspace 'Download'
$staging = Join-Path $workspace 'artifacts\cross-platform-packaging'
$tools = Join-Path $workspace 'artifacts\cross-platform-tools'
$linuxTar = Join-Path $download 'Linux\SOCYVIA-1.0.0-Linux-x64.tar.gz'
$appImage = Join-Path $download 'Linux\SOCYVIA-1.0.0-Linux-x64.AppImage'
$deb = Join-Path $download 'Linux\SOCYVIA-1.0.0-Linux-x64.deb'
$windowsExpected = [ordered]@{
    'Windows\SOCYVIA-1.0.0-Windows-x64-Setup.exe'='9e958b1f267319d92675c9b722aea9a7ff403d7019bd96da7269ca1a6d936ceb'
    'Windows\SOCYVIA-1.0.0-Windows-x64-Portable.zip'='803e3cad8ba4939d822b485453cffeb41e1d9410a99976363fcb9678072319f9'
}
$runtimeHash = '1cc49bcf1e2ccd593c379adb17c9f85a36d619088296504de95b1d06215aebbf'
$squashToolsHash = '891b1ed46dd856d05e429b01de2c7d86175a4c22478b5a6770f94b9c2100d0f6'

foreach ($entry in $windowsExpected.GetEnumerator()) {
    $path = Join-Path $download $entry.Key
    if (-not (Test-Path -LiteralPath $path)) { throw "Frozen Windows artifact missing: $($entry.Key)" }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Value) { throw "Frozen Windows artifact changed: $($entry.Key)" }
}

$expectedStaging = [IO.Path]::GetFullPath((Join-Path $workspace 'artifacts\cross-platform-packaging'))
if ([IO.Path]::GetFullPath($staging) -ne $expectedStaging) { throw 'Unsafe cross-platform staging path.' }
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging,$tools,(Join-Path $download 'Linux') | Out-Null

function Assert-Download([string]$relative) {
    $path = Join-Path $download $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "Required frozen package is missing: $relative" }
    return $path
}

function Assert-MacBundle([string]$zipPath, [uint32]$expectedCpuType, [string]$label) {
    $extract = Join-Path $staging "mac-$label"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extract -Force
    $bundle = Join-Path $extract 'SOCYVIA.app'
    $plist = Join-Path $bundle 'Contents\Info.plist'
    $executable = Join-Path $bundle 'Contents\MacOS\SOCYVIA'
    $icon = Join-Path $bundle 'Contents\Resources\socyvia-mark.icns'
    foreach ($path in $bundle,$plist,$executable,$icon) {
        if (-not (Test-Path -LiteralPath $path)) { throw "$label macOS bundle is incomplete: $path" }
    }
    $plistText = [IO.File]::ReadAllText($plist,[Text.Encoding]::UTF8)
    foreach ($required in 'com.socyvia.desktop','1.0.0','<string>SOCYVIA</string>','socyvia-mark.icns') {
        if (-not $plistText.Contains($required)) { throw "$label Info.plist is missing '$required'." }
    }
    $bytes = [IO.File]::ReadAllBytes($executable)
    if ($bytes.Length -lt 32 -or -not ($bytes[0] -eq 0xCF -and $bytes[1] -eq 0xFA -and $bytes[2] -eq 0xED -and $bytes[3] -eq 0xFE)) {
        throw "$label executable is not a 64-bit little-endian Mach-O file."
    }
    $cpu = [BitConverter]::ToUInt32($bytes,4)
    if ($cpu -ne $expectedCpuType) { throw "$label executable architecture is incorrect (0x$($cpu.ToString('x8')))." }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entry = $archive.Entries | Where-Object FullName -eq 'SOCYVIA.app/Contents/MacOS/SOCYVIA' | Select-Object -First 1
        if ($null -eq $entry) { throw "$label ZIP lacks the bundle executable entry." }
        $unixMode = ($entry.ExternalAttributes -shr 16) -band 0xFFFF
        if (($unixMode -band 0x49) -eq 0) { throw "$label ZIP does not preserve an executable mode." }
    }
    finally { $archive.Dispose() }
}

$macArm = Assert-Download 'macOS\SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip'
$macX64 = Assert-Download 'macOS\SOCYVIA-1.0.0-macOS-Intel-x64.zip'
Assert-MacBundle $macArm 0x0100000C 'arm64'
Assert-MacBundle $macX64 0x01000007 'x64'

if (-not (Test-Path -LiteralPath $linuxTar)) { throw 'Frozen Linux tar.gz is missing.' }
$linuxExtract = Join-Path $staging 'linux-tar'
New-Item -ItemType Directory -Force -Path $linuxExtract | Out-Null
tar -xzf $linuxTar -C $linuxExtract
if ($LASTEXITCODE -ne 0) { throw 'Linux tar.gz extraction failed.' }
$payload = Join-Path $linuxExtract 'SOCYVIA-1.0.0-Linux-x64'
$linuxExecutable = Join-Path $payload 'SOCYVIA'
if (-not (Test-Path -LiteralPath $linuxExecutable)) { throw 'Linux tar.gz lacks SOCYVIA.' }
$elf = [IO.File]::ReadAllBytes($linuxExecutable)
if ($elf.Length -lt 20 -or -not ($elf[0] -eq 0x7F -and $elf[1] -eq 0x45 -and $elf[2] -eq 0x4C -and $elf[3] -eq 0x46) -or
    [BitConverter]::ToUInt16($elf,18) -ne 0x003E) { throw 'Linux payload is not an x86_64 ELF executable.' }

$runtime = Join-Path $tools 'runtime-x86_64'
$squashZip = Join-Path $tools 'squashfs-tools-ng-1.3.2-mingw64.zip'
if (-not (Test-Path -LiteralPath $runtime)) {
    Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='SOCYVIA-release-packager'} `
        -Uri 'https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64' -OutFile $runtime
}
if ((Get-FileHash $runtime -Algorithm SHA256).Hash.ToLowerInvariant() -ne $runtimeHash) {
    throw 'Official AppImage runtime does not match the pinned release hash.'
}
if (-not (Test-Path -LiteralPath $squashZip)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://infraroot.at/pub/squashfs/windows/squashfs-tools-ng-1.3.2-mingw64.zip' -OutFile $squashZip
}
if ((Get-FileHash $squashZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $squashToolsHash) {
    throw 'SquashFS packaging tools do not match the pinned release hash.'
}
$squashTools = Join-Path $tools 'squashfs'
if (-not (Test-Path -LiteralPath $squashTools)) { Expand-Archive -LiteralPath $squashZip -DestinationPath $squashTools }
$gensquashfs = (Get-ChildItem $squashTools -Recurse -File -Filter gensquashfs.exe | Select-Object -First 1).FullName
$rdsquashfs = (Get-ChildItem $squashTools -Recurse -File -Filter rdsquashfs.exe | Select-Object -First 1).FullName
if (-not $gensquashfs -or -not $rdsquashfs) { throw 'SquashFS tools are incomplete.' }

$appDir = Join-Path $staging 'SOCYVIA.AppDir'
$appBin = Join-Path $appDir 'usr\bin'
$appApplications = Join-Path $appDir 'usr\share\applications'
$appIcons = Join-Path $appDir 'usr\share\icons\hicolor\256x256\apps'
$appMeta = Join-Path $appDir 'usr\share\metainfo'
New-Item -ItemType Directory -Force -Path $appBin,$appApplications,$appIcons,$appMeta | Out-Null
Copy-Item -Path (Join-Path $payload '*') -Destination $appBin -Recurse -Force
Copy-Item -LiteralPath (Join-Path $workspace 'packaging\linux\AppRun') -Destination $appDir
Copy-Item -LiteralPath (Join-Path $workspace 'packaging\linux\socyvia.desktop') -Destination (Join-Path $appDir 'socyvia.desktop')
Copy-Item -LiteralPath (Join-Path $payload 'socyvia.png') -Destination (Join-Path $appDir 'socyvia.png')
Copy-Item -LiteralPath (Join-Path $workspace 'packaging\linux\socyvia.desktop') -Destination $appApplications
Copy-Item -LiteralPath (Join-Path $payload 'socyvia.png') -Destination (Join-Path $appIcons 'socyvia.png')
Copy-Item -LiteralPath (Join-Path $workspace 'packaging\linux\com.socyvia.desktop.metainfo.xml') -Destination $appMeta

$packFile = Join-Path $staging 'appdir.pack'
$lines = [Collections.Generic.List[string]]::new()
function Get-AppDirRelative([string]$path) {
    $root = [IO.Path]::GetFullPath($appDir).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { throw "AppDir path escaped staging: $full" }
    return $full.Substring($root.Length).Replace('\','/')
}
$directories = Get-ChildItem $appDir -Recurse -Directory | Sort-Object @{Expression={$_.FullName.Split([IO.Path]::DirectorySeparatorChar).Count}},FullName
foreach ($directory in $directories) {
    $relative = Get-AppDirRelative $directory.FullName
    $lines.Add("dir /$relative 0755 0 0")
}
foreach ($file in Get-ChildItem $appDir -Recurse -File | Sort-Object FullName) {
    $relative = Get-AppDirRelative $file.FullName
    $mode = if ($relative -eq 'AppRun' -or $relative -eq 'usr/bin/SOCYVIA') { '0755' } else { '0644' }
    $lines.Add("file /$relative $mode 0 0 $relative")
}
$lines.Add('slink /.DirIcon 0777 0 0 socyvia.png')
[IO.File]::WriteAllLines($packFile,$lines,[Text.UTF8Encoding]::new($false))
$squashImage = Join-Path $staging 'socyvia.squashfs'
& $gensquashfs --pack-dir $appDir --pack-file $packFile --compressor gzip --all-root --force --quiet $squashImage
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $squashImage)) { throw 'SquashFS image creation failed.' }

if (Test-Path -LiteralPath $appImage) { Remove-Item -LiteralPath $appImage -Force }
$output = [IO.File]::Create($appImage)
try {
    $input = [IO.File]::OpenRead($runtime); try { $input.CopyTo($output) } finally { $input.Dispose() }
    $input = [IO.File]::OpenRead($squashImage); try { $input.CopyTo($output) } finally { $input.Dispose() }
}
finally { $output.Dispose() }
$appBytes = [IO.File]::ReadAllBytes($appImage)
$runtimeLength = (Get-Item $runtime).Length
if ([Text.Encoding]::ASCII.GetString($appBytes,8,3) -ne "AI$([char]2)" -or
    [Text.Encoding]::ASCII.GetString($appBytes,[int]$runtimeLength,4) -ne 'hsqs') {
    throw 'Generated AppImage does not satisfy the type-2 ELF/SquashFS structure.'
}
$describe = & $rdsquashfs --describe $squashImage
if ($LASTEXITCODE -ne 0 -or ($describe -join "`n") -notmatch '(?m)^file AppRun 0755' -or
    ($describe -join "`n") -notmatch '(?m)^file usr/bin/SOCYVIA 0755') {
    throw 'Generated AppImage payload or executable modes are invalid.'
}

$debProject = Join-Path $workspace 'scripts\LinuxPackageTool\LinuxPackageTool.csproj'
dotnet run --project $debProject -c Release -- deb $payload (Join-Path $payload 'socyvia.png') `
    (Join-Path $workspace 'packaging\linux\socyvia.desktop') `
    (Join-Path $workspace 'packaging\linux\com.socyvia.desktop.metainfo.xml') $deb
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $deb)) { throw 'Debian package creation failed.' }

[ordered]@{
    WindowsFrozen=$true
    macOSArm64Bundle='PASS'
    macOSX64Bundle='PASS'
    LinuxTarPayload='PASS'
    AppImageType2Structure='PASS'
    AppImagePayload='PASS'
    DebianPackageCreated='PASS'
    AppImage=$appImage
    Deb=$deb
} | ConvertTo-Json -Depth 4
