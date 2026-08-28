[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest = Join-Path $workspace 'packaging\windows\PremiumBootstrapper\app.manifest'
$installerSource = Join-Path $workspace 'packaging\windows\PremiumBootstrapper\Program.cs'
$uninstallerSource = Join-Path $workspace 'packaging\windows\PremiumUninstaller\Program.cs'
$output = Join-Path $workspace 'visual-qa\final-distribution\windows-installer\dpi-contract.json'
$manifestText = [System.IO.File]::ReadAllText($manifest, [System.Text.Encoding]::UTF8)
$installerText = [System.IO.File]::ReadAllText($installerSource, [System.Text.Encoding]::UTF8)
$uninstallerText = [System.IO.File]::ReadAllText($uninstallerSource, [System.Text.Encoding]::UTF8)
if ($manifestText -notmatch 'PerMonitorV2' -or $manifestText -notmatch '<dpiAware[^>]*>true/pm</dpiAware>') {
    throw 'Premium Setup is not declared Per-Monitor V2 DPI aware.'
}

$designWindow = [pscustomobject]@{ Width=840; Height=560 }
$sidebar = [pscustomobject]@{ Width=274; Center=137 }
$content = [pscustomobject]@{ Width=566; LeftGuide=48; RightGuide=518 }
$critical = @(
    [pscustomobject]@{ Name='Welcome title'; X=322; Y=76; Width=470; Height=56 },
    [pscustomobject]@{ Name='Welcome card'; X=322; Y=210; Width=456; Height=160 },
    [pscustomobject]@{ Name='Options action'; X=322; Y=458; Width=222; Height=48 },
    [pscustomobject]@{ Name='Primary action'; X=578; Y=458; Width=200; Height=48 }
)
$englishAnchors = @(48,48,48,48)
$arabicRightEdges = @((48+470),(64+454),(62+456),(62+456))
$sidebarCenters = @((96+(82/2)),(28+(218/2)),(45+(184/2)))
$exactArabicTagline = [System.Text.Encoding]::UTF8.GetString(
    [System.Convert]::FromBase64String('2KfZhNin2K7Yqtio2KfYsSDYp9mE2LnZhNmF2Yog2YTZhNi52YTZiNmFXG7Yp9mE2KfYrNiq2YXYp9i52YrYqSDYp9mE2K3Yp9iz2YjYqNmK2Kk='))

$sourceContract = [ordered]@{
    installerUsesLogicalRtlPaintAlignment = ($installerText.Contains('label.TextAlign = arabic && technical') -and
        $installerText.Contains('_text.TextAlign = ContentAlignment.TopLeft;'))
    uninstallerUsesLogicalRtlPaintAlignment = $uninstallerText.Contains('label.TextAlign = arabic && technical')
    exactTwoLineArabicTagline = ($installerText.Contains($exactArabicTagline) -and $uninstallerText.Contains($exactArabicTagline))
    technicalPathExplicitlyLtr = $installerText.Contains('_installLocation.RightToLeft = RightToLeft.No')
    brandedUninstallerPresent = $uninstallerText.Contains('internal sealed class UninstallForm')
}
$failedSourceContract = @($sourceContract.GetEnumerator() | Where-Object { -not [bool]$_.Value })
if ($failedSourceContract.Count -gt 0) {
    throw "Premium Setup directional source contract failed: $($failedSourceContract.Name -join ', ')."
}

$results = foreach ($percent in 100,125,150,200) {
    $factor = $percent / 100.0
    $windowWidth = [math]::Round($designWindow.Width * $factor)
    $windowHeight = [math]::Round($designWindow.Height * $factor)
    $inside = $true
    foreach ($control in $critical) {
        $right = [math]::Round(($control.X + $control.Width) * $factor)
        $bottom = [math]::Round(($control.Y + $control.Height) * $factor)
        if ($right -gt $windowWidth -or $bottom -gt $windowHeight) { $inside = $false }
    }
    $englishAligned = @($englishAnchors | Where-Object { [math]::Abs(($_ * $factor) - ($content.LeftGuide * $factor)) -gt 1 }).Count -eq 0
    $arabicAligned = @($arabicRightEdges | Where-Object { [math]::Abs(($_ * $factor) - ($content.RightGuide * $factor)) -gt 1 }).Count -eq 0
    $sidebarCentered = @($sidebarCenters | Where-Object { [math]::Abs(($_ * $factor) - ($sidebar.Center * $factor)) -gt 1 }).Count -eq 0
    $pass = $inside -and $englishAligned -and $arabicAligned -and $sidebarCentered
    [ordered]@{
        scale="$percent%"
        window="${windowWidth}x${windowHeight}"
        criticalControlsInside=$inside
        proportionalGeometry=$true
        perMonitorV2=$true
        englishMainContentLeftAnchored=$englishAligned
        arabicMainContentRightAnchored=$arabicAligned
        sidebarBrandingCentered=$sidebarCentered
        exactTwoLineArabicTagline=$sourceContract.exactTwoLineArabicTagline
        technicalValuesDirectionallySafe=$sourceContract.technicalPathExplicitlyLtr
        brandedUninstaller=$sourceContract.brandedUninstallerPresent
        result=$(if ($pass) {'PASS'} else {'FAIL'})
    }
}
if ($results.result -contains 'FAIL') { throw 'Premium Setup DPI geometry contract failed.' }
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($output)) | Out-Null
[System.IO.File]::WriteAllText($output, ($results | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
$results | Format-Table -AutoSize
