[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$download = Join-Path $workspace 'Download'
$staging = Join-Path $workspace 'artifacts\public-package-audit'
$reportPath = Join-Path $workspace 'release-assets\FINAL-PACKAGE-SECURITY.json'

$resolvedStaging = [System.IO.Path]::GetFullPath($staging)
if ($resolvedStaging -ne [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts\public-package-audit'))) {
    throw "Unsafe audit staging path: $resolvedStaging"
}
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

$forbiddenExtensions = @('.cs','.csproj','.sln','.pdb','.env','.db','.sqlite','.sqlite3','.log')
$forbiddenSegments = @('Tests','visual-qa','node_modules','bin','obj','artifacts','QA','.idea','.git')
$findings = [System.Collections.Generic.List[string]]::new()

function Audit-Tree([string]$root, [string]$platform) {
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)
    foreach ($file in $files) {
        if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) {
            $findings.Add("$platform forbidden extension: $($file.FullName.Substring($root.Length + 1))")
        }
        $segments = $file.FullName.Substring($root.Length + 1).Split([char[]]'\/')
        for ($segmentIndex = 0; $segmentIndex -lt $segments.Length; $segmentIndex++) {
            $segment = $segments[$segmentIndex]
            if ($platform -like 'Linux*' -and $segment -ieq 'bin' -and $segmentIndex -gt 0 -and $segments[$segmentIndex - 1] -ieq 'usr') {
                continue
            }
            if ($forbiddenSegments -contains $segment) {
                $findings.Add("$platform forbidden path segment: $segment")
                break
            }
        }
        if ($platform -like 'macOS*' -and $file.Extension -ieq '.exe') {
            $findings.Add("$platform contains Windows executable: $($file.Name)")
        }
        if ($platform -like 'Linux*' -and ($file.Extension -ieq '.exe' -or $file.Extension -ieq '.dylib')) {
            $findings.Add("$platform contains foreign executable/library: $($file.Name)")
        }
        if ($platform -like 'Windows*' -and ($file.Extension -ieq '.dylib' -or $file.Extension -ieq '.so')) {
            $findings.Add("$platform contains foreign library: $($file.Name)")
        }
    }

    $patterns = @(
        'gsk_[A-Za-z0-9_-]{16,}',
        '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
        'C:\\Users\\Abdellah RIAD',
        '/Users/Abdellah RIAD/',
        '/home/Abdellah RIAD/',
        'RiderProjects\\SOCYVIA',
        'Bearer eyJ[A-Za-z0-9_-]+'
    )
    foreach ($pattern in $patterns) {
        $matches = @(& rg -a -l --glob '!*.png' --glob '!*.ico' --glob '!*.icns' --glob '!*.ttf' --glob '!*.woff*' --glob '!*.jpg' --glob '!*.jpeg' --glob '!*.gif' --glob '!*.mp4' --glob '!*.mp3' --glob '!*.wav' -- $pattern $root 2>$null)
        foreach ($match in $matches) { $findings.Add("$platform sensitive pattern '$pattern': $match") }
    }
    return $files.Count
}

function Expand-Deb([string]$package, [string]$destination) {
    $members = Join-Path $destination 'members'
    $root = Join-Path $destination 'root'
    New-Item -ItemType Directory -Force -Path $members,$root | Out-Null
    $bytes = [IO.File]::ReadAllBytes($package)
    if ($bytes.Length -lt 8 -or [Text.Encoding]::ASCII.GetString($bytes,0,8) -ne "!<arch>`n") {
        throw 'Debian package is not an ar archive.'
    }
    $position = 8
    $names = [Collections.Generic.List[string]]::new()
    while ($position + 60 -le $bytes.Length) {
        $header = [Text.Encoding]::ASCII.GetString($bytes,$position,60)
        if ($header.Substring(58,2) -ne (([string][char]0x60) + "`n")) { throw 'Debian package has an invalid ar member header.' }
        $name = $header.Substring(0,16).Trim().TrimEnd('/')
        $size = 0L
        if (-not [long]::TryParse($header.Substring(48,10).Trim(),[ref]$size)) { throw 'Debian package member size is invalid.' }
        $position += 60
        if ($position + $size -gt $bytes.Length) { throw 'Debian package member exceeds the archive.' }
        $memberPath = Join-Path $members $name
        $data = [byte[]]::new([int]$size)
        [Array]::Copy($bytes,$position,$data,0,$size)
        [IO.File]::WriteAllBytes($memberPath,$data)
        $names.Add($name)
        $position += $size
        if (($size -band 1) -ne 0) { $position++ }
    }
    foreach ($required in 'debian-binary','control.tar.gz','data.tar.gz') {
        if (-not $names.Contains($required)) { throw "Debian package lacks $required." }
    }
    if ([IO.File]::ReadAllText((Join-Path $members 'debian-binary'),[Text.Encoding]::ASCII) -ne "2.0`n") {
        throw 'Debian package format version is not 2.0.'
    }
    $controlRoot = Join-Path $destination 'control'
    New-Item -ItemType Directory -Force -Path $controlRoot | Out-Null
    tar -xzf (Join-Path $members 'control.tar.gz') -C $controlRoot
    if ($LASTEXITCODE -ne 0) { throw 'Debian control archive extraction failed.' }
    $control = [IO.File]::ReadAllText((Join-Path $controlRoot 'control'),[Text.Encoding]::UTF8)
    foreach ($required in 'Package: socyvia','Version: 1.0.0','Architecture: amd64') {
        if (-not $control.Contains($required)) { throw "Debian control metadata lacks '$required'." }
    }
    tar -xzf (Join-Path $members 'data.tar.gz') -C $root
    if ($LASTEXITCODE -ne 0) { throw 'Debian data archive extraction failed.' }
    $dataListing = @(tar -tvzf (Join-Path $members 'data.tar.gz'))
    if ($LASTEXITCODE -ne 0 -or ($dataListing -join "`n") -notmatch '(?m)^-rwxr-xr-x.* opt/socyvia/SOCYVIA$' -or
        ($dataListing -join "`n") -notmatch '(?m)^-rwxr-xr-x.* usr/bin/socyvia$') {
        throw 'Debian executable modes are invalid.'
    }
    foreach ($required in 'opt\socyvia\SOCYVIA','usr\bin\socyvia','usr\share\applications\socyvia.desktop','usr\share\metainfo\com.socyvia.desktop.metainfo.xml') {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required))) { throw "Debian package lacks $required." }
    }
    return $root
}

function Expand-AppImage([string]$package, [string]$destination) {
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $stream = [IO.File]::OpenRead($package)
    try {
        $header = [byte[]]::new(64)
        if ($stream.Read($header,0,$header.Length) -ne 64) { throw 'AppImage header is incomplete.' }
        if (-not ($header[0] -eq 0x7F -and $header[1] -eq 0x45 -and $header[2] -eq 0x4C -and $header[3] -eq 0x46 -and
            [Text.Encoding]::ASCII.GetString($header,8,3) -eq "AI$([char]2)" -and [BitConverter]::ToUInt16($header,18) -eq 0x003E)) {
            throw 'AppImage is not a type-2 x86_64 ELF.'
        }
        $offset = [BitConverter]::ToUInt64($header,40) +
            ([uint64][BitConverter]::ToUInt16($header,58) * [uint64][BitConverter]::ToUInt16($header,60))
        if ($offset -le 64 -or $offset -ge $stream.Length) { throw 'AppImage SquashFS offset is invalid.' }
        $stream.Position = [long]$offset
        $squash = Join-Path $destination 'payload.squashfs'
        $output = [IO.File]::Create($squash)
        try { $stream.CopyTo($output) } finally { $output.Dispose() }
    }
    finally { $stream.Dispose() }
    $magic = [IO.File]::ReadAllBytes($squash)[0..3]
    if ([Text.Encoding]::ASCII.GetString($magic) -ne 'hsqs') { throw 'AppImage payload is not SquashFS.' }
    $rdsquashfs = (Get-ChildItem (Join-Path $workspace 'artifacts\cross-platform-tools\squashfs') -Recurse -File -Filter rdsquashfs.exe | Select-Object -First 1).FullName
    if (-not $rdsquashfs) { throw 'rdsquashfs is required for the final AppImage audit.' }
    $root = Join-Path $destination 'root'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    # The Windows SquashFS port cannot materialize Unix symlinks. The only link
    # in this AppDir is the conventional .DirIcon alias, so extract all regular
    # payload files while omitting links, then audit the exact extracted bytes.
    & $rdsquashfs --no-slink --unpack-root $root --unpack-path / --quiet $squash
    if ($LASTEXITCODE -ne 0) { throw 'AppImage SquashFS extraction failed.' }
    foreach ($required in 'AppRun','usr\bin\SOCYVIA','socyvia.desktop','usr\share\metainfo\com.socyvia.desktop.metainfo.xml') {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required))) { throw "AppImage lacks $required." }
    }
    return $root
}

try {
    $windowsRoot = Join-Path $staging 'windows-portable'
    $macArmRoot = Join-Path $staging 'mac-arm64'
    $macIntelRoot = Join-Path $staging 'mac-x64'
    $linuxRoot = Join-Path $staging 'linux-x64'
    $appImageRoot = Expand-AppImage (Join-Path $download 'Linux\SOCYVIA-1.0.0-Linux-x64.AppImage') (Join-Path $staging 'linux-appimage')
    $debRoot = Expand-Deb (Join-Path $download 'Linux\SOCYVIA-1.0.0-Linux-x64.deb') (Join-Path $staging 'linux-deb')
    Expand-Archive -LiteralPath (Join-Path $download 'Windows\SOCYVIA-1.0.0-Windows-x64-Portable.zip') -DestinationPath $windowsRoot
    Expand-Archive -LiteralPath (Join-Path $download 'macOS\SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip') -DestinationPath $macArmRoot
    Expand-Archive -LiteralPath (Join-Path $download 'macOS\SOCYVIA-1.0.0-macOS-Intel-x64.zip') -DestinationPath $macIntelRoot
    New-Item -ItemType Directory -Force -Path $linuxRoot | Out-Null
    tar -xzf (Join-Path $download 'Linux\SOCYVIA-1.0.0-Linux-x64.tar.gz') -C $linuxRoot
    if ($LASTEXITCODE -ne 0) { throw 'Linux archive extraction failed.' }

    $counts = [ordered]@{
        WindowsPortable=(Audit-Tree $windowsRoot 'Windows portable')
        macOSArm64=(Audit-Tree $macArmRoot 'macOS arm64')
        macOSIntel=(Audit-Tree $macIntelRoot 'macOS x64')
        LinuxX64=(Audit-Tree $linuxRoot 'Linux')
        LinuxAppImage=(Audit-Tree $appImageRoot 'Linux AppImage')
        LinuxDeb=(Audit-Tree $debRoot 'Linux DEB')
    }

    $setup = Join-Path $download 'Windows\SOCYVIA-1.0.0-Windows-x64-Setup.exe'
    $assembly = [System.Reflection.Assembly]::LoadFile($setup)
    $resources = @($assembly.GetManifestResourceNames() | Sort-Object)
    $expectedResources = @(
        'Socyvia.Engine.exe',
        'Socyvia.Font.ArabicRegular',
        'Socyvia.Font.ArabicSemiBold',
        'Socyvia.Font.Regular',
        'Socyvia.Font.SemiBold',
        'Socyvia.Icon.ico',
        'Socyvia.Logo.png'
    )
    if (Compare-Object $resources $expectedResources) {
        $findings.Add('Windows Setup contains an unexpected embedded resource set.')
    }
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
    if ($version.ProductName -ne 'SOCYVIA' -or $version.ProductVersion -ne '1.0.0') {
        $findings.Add('Windows Setup product/version metadata is incorrect.')
    }

    if ($findings.Count -gt 0) { throw ($findings -join [Environment]::NewLine) }
    $result = [ordered]@{
        result='PASS'
        archiveFileCounts=$counts
        setupEmbeddedResources=$resources
        setupProduct='SOCYVIA'
        setupVersion='1.0.0'
        secretValues=0
        privateKeys=0
        developerPaths=0
        researchDatabases=0
        pdbFiles=0
        sourceProjectFiles=0
        qaProfiles=0
    }
    [System.IO.File]::WriteAllText($reportPath, ($result | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
    $result | ConvertTo-Json -Depth 6
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
