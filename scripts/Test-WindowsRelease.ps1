[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Drawing

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installer = Join-Path $workspace 'Download\Windows\SOCYVIA-1.0.0-Windows-x64-Setup.exe'
$portableArchive = Join-Path $workspace 'Download\Windows\SOCYVIA-1.0.0-Windows-x64-Portable.zip'
$qaRoot = Join-Path $workspace 'artifacts\release-qa'
$installRoot = Join-Path $env:LOCALAPPDATA 'SOCYVIA-Release-QA-1.0.0'
$storageRoot = Join-Path $qaRoot 'storage'
$portableRoot = Join-Path $qaRoot 'portable'
$screenshots = Join-Path $workspace 'visual-qa\final-release-installed'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SOCYVIA\SOCYVIA.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7A3791F3-F195-46C1-91EF-6682771461D6}_is1'

if (-not (Test-Path -LiteralPath $installer) -or -not (Test-Path -LiteralPath $portableArchive)) {
    throw 'Windows release artifacts are missing.'
}
if (Test-Path -LiteralPath $installRoot) { throw "QA install target already exists: $installRoot" }
if (Test-Path -LiteralPath $uninstallKey) { throw 'An existing SOCYVIA installation is registered; release QA will not overwrite it.' }
if (Test-Path -LiteralPath $qaRoot) { Remove-Item -LiteralPath $qaRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $qaRoot,$storageRoot,$portableRoot,$screenshots | Out-Null

function Get-ProcessWindows([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $processId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
}

function Find-InProcess([int]$processId, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $automationId)
    foreach ($window in Get-ProcessWindows $processId) {
        $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $control) { return [pscustomobject]@{ Window=$window; Control=$control } }
    }
    return $null
}

function Wait-Control([int]$processId, [string]$automationId, [int]$seconds = 30) {
    for ($attempt = 0; $attempt -lt ($seconds * 2); $attempt++) {
        Start-Sleep -Milliseconds 500
        $result = Find-InProcess $processId $automationId
        if ($null -ne $result) { return $result }
    }
    throw "Installed SOCYVIA control '$automationId' did not become available."
}

function Invoke-Control([int]$processId, [string]$automationId) {
    $result = Wait-Control $processId $automationId
    $pattern = $result.Control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 450
}

function Capture-ControlWindow([int]$processId, [string]$automationId, [string]$filename) {
    $result = Wait-Control $processId $automationId
    $element = $result.Control
    $handle = 0
    while ($null -ne $element) {
        if ($element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and
            $element.Current.NativeWindowHandle -ne 0) {
            $handle = $element.Current.NativeWindowHandle
            break
        }
        $element = [System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($element)
    }
    if ($handle -eq 0) { $handle = $result.Window.Current.NativeWindowHandle }
    & (Join-Path $PSScriptRoot 'Capture-SocyviaWindow.ps1') -WindowHandle $handle `
        -OutputPath (Join-Path $screenshots $filename) -PreserveWindowState | Out-Null
}

function Assert-EmptyStateCentered([int]$processId, [string]$language) {
    $title = Wait-Control $processId 'EmptyTitle'
    $description = Wait-Control $processId 'EmptyDescription'
    $button = Wait-Control $processId 'EmptyCreateButton'
    $buttonBounds = $button.Control.Current.BoundingRectangle
    $groupCenter = $buttonBounds.Left + ($buttonBounds.Width / 2)
    foreach ($entry in @($title,$description)) {
        $bounds = $entry.Control.Current.BoundingRectangle
        $center = $bounds.Left + ($bounds.Width / 2)
        if ([math]::Abs($center - $groupCenter) -gt 1.5) {
            throw "$language Studies empty-state elements disagree by $([math]::Round($center - $groupCenter,2)) pixels."
        }
    }
}

function Start-IsolatedApp([string]$executable) {
    $prior = $env:SOCYVIA_STORAGE_ROOT
    $env:SOCYVIA_STORAGE_ROOT = $storageRoot
    try { return Start-Process -FilePath $executable -PassThru }
    finally {
        if ($null -eq $prior) { Remove-Item Env:SOCYVIA_STORAGE_ROOT -ErrorAction SilentlyContinue }
        else { $env:SOCYVIA_STORAGE_ROOT = $prior }
    }
}

$installedProcess = $null
$relaunchProcess = $null
$portableProcess = $null
$dataPreservedAfterUninstall = $false
try {
    $install = Start-Process -FilePath $installer -ArgumentList @(
        '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
        "/DIR=`"$installRoot`"", "/LOG=`"$qaRoot\installer.log`"") -PassThru -Wait
    if ($install.ExitCode -ne 0) { throw "Installer exited with $($install.ExitCode)." }

    $installedExe = Join-Path $installRoot 'SOCYVIA.exe'
    if (-not (Test-Path -LiteralPath $installedExe)) { throw 'Installed executable was not found.' }
    if (-not (Test-Path -LiteralPath $startMenuShortcut)) { throw 'Start Menu shortcut was not created.' }
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedExe)
    if ($version.ProductVersion -ne '1.0.0' -or $version.ProductName -ne 'SOCYVIA') {
        throw "Installed metadata is invalid: $($version.ProductName) $($version.ProductVersion)."
    }
    $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($installedExe)
    if ($null -eq $icon) { throw 'Installed executable has no associated SOCYVIA icon.' }
    $icon.Dispose()

    $installedProcess = Start-IsolatedApp $installedExe
    Wait-Control $installedProcess.Id 'EnterWorkspaceButton' | Out-Null
    Invoke-Control $installedProcess.Id 'EnglishLanguageButton'
    Invoke-Control $installedProcess.Id 'NewModeButton'
    Capture-ControlWindow $installedProcess.Id 'EnterWorkspaceButton' 'installed-english-new-researcher.png'
    Invoke-Control $installedProcess.Id 'ArabicLanguageButton'
    Capture-ControlWindow $installedProcess.Id 'EnterWorkspaceButton' 'installed-arabic-new-researcher.png'
    Invoke-Control $installedProcess.Id 'EnglishLanguageButton'

    $name = Wait-Control $installedProcess.Id 'ResearcherNameBox'
    $valuePattern = $name.Control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue('SOCYVIA Release QA')
    $privacy = Wait-Control $installedProcess.Id 'PrivacyCheckBox'
    $toggle = $privacy.Control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
    Invoke-Control $installedProcess.Id 'EnterWorkspaceButton'

    $homeControl = Wait-Control $installedProcess.Id 'HomeButton' 45
    # NextButton and CreateStudyButton also exist in main-window workspaces.
    # Anchor onboarding QA to a control unique to the modal so the captured
    # native window is the actual first-launch surface.
    $onboarding = Find-InProcess $installedProcess.Id 'TourContextText'
    if ($null -ne $onboarding) {
        Capture-ControlWindow $installedProcess.Id 'TourContextText' 'installed-onboarding.png'
        Invoke-Control $installedProcess.Id 'SkipButton'
    }
    Capture-ControlWindow $installedProcess.Id 'HomeButton' 'installed-dashboard-english.png'

    Invoke-Control $installedProcess.Id 'StudiesButton'
    Wait-Control $installedProcess.Id 'EmptyCreateButton' | Out-Null
    Assert-EmptyStateCentered $installedProcess.Id 'English'
    Capture-ControlWindow $installedProcess.Id 'EmptyCreateButton' 'installed-studies-empty-english.png'

    foreach ($control in @('ContentLibraryButton','AnalysisButton','ReportsButton','SocyviaAiButton','SettingsButton')) {
        Invoke-Control $installedProcess.Id $control
    }
    Invoke-Control $installedProcess.Id 'SettingsArabicButton'
    Capture-ControlWindow $installedProcess.Id 'SettingsButton' 'installed-settings-arabic.png'
    Invoke-Control $installedProcess.Id 'StudiesButton'
    Wait-Control $installedProcess.Id 'EmptyCreateButton' | Out-Null
    Assert-EmptyStateCentered $installedProcess.Id 'Arabic'
    Capture-ControlWindow $installedProcess.Id 'EmptyCreateButton' 'installed-studies-empty-arabic.png'
    Invoke-Control $installedProcess.Id 'SettingsButton'
    Invoke-Control $installedProcess.Id 'SettingsEnglishButton'
    Invoke-Control $installedProcess.Id 'CloseButton'
    $installedProcess.WaitForExit(10000) | Out-Null

    $relaunchProcess = Start-IsolatedApp $installedExe
    Wait-Control $relaunchProcess.Id 'EnterWorkspaceButton' | Out-Null
    Invoke-Control $relaunchProcess.Id 'EnglishLanguageButton'
    Capture-ControlWindow $relaunchProcess.Id 'EnterWorkspaceButton' 'installed-english-registered-researcher.png'
    Invoke-Control $relaunchProcess.Id 'ArabicLanguageButton'
    Capture-ControlWindow $relaunchProcess.Id 'EnterWorkspaceButton' 'installed-arabic-registered-researcher.png'
    Invoke-Control $relaunchProcess.Id 'CloseButton'
    $relaunchProcess.WaitForExit(10000) | Out-Null

    Expand-Archive -LiteralPath $portableArchive -DestinationPath $portableRoot
    $portableExe = Join-Path $portableRoot 'SOCYVIA.exe'
    if (-not (Test-Path -LiteralPath $portableExe)) { throw 'Portable executable was not found at archive root.' }
    $portableProcess = Start-IsolatedApp $portableExe
    Wait-Control $portableProcess.Id 'EnterWorkspaceButton' | Out-Null
    Invoke-Control $portableProcess.Id 'CloseButton'
    $portableProcess.WaitForExit(10000) | Out-Null

    $uninstaller = Join-Path $installRoot 'SOCYVIA.Uninstall.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw 'Uninstaller was not installed.' }
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT' -PassThru -Wait
    if ($uninstall.ExitCode -ne 0) { throw "Uninstaller exited with $($uninstall.ExitCode)." }
    for ($attempt=0; $attempt -lt 180 -and (Test-Path -LiteralPath $installRoot); $attempt++) {
        Start-Sleep -Milliseconds 500
    }
    if (Test-Path -LiteralPath $installRoot) { throw 'Installed application binaries remained after uninstall.' }
    if (Test-Path -LiteralPath $startMenuShortcut) { throw 'Start Menu shortcut remained after uninstall.' }
    $dataPreservedAfterUninstall = Test-Path -LiteralPath $storageRoot
    if (-not $dataPreservedAfterUninstall) { throw 'Uninstall removed isolated researcher data.' }

    [ordered]@{
        Result='PASS'
        Installer='INSTALLER VERIFIED'
        InstalledApp='PHYSICALLY LAUNCHED'
        Portable='PHYSICALLY LAUNCHED'
        StartMenu='VERIFIED'
        Icon='VERIFIED'
        Version='1.0.0'
        DataPreservedAfterUninstall=$dataPreservedAfterUninstall
        Screenshots=(Get-ChildItem -LiteralPath $screenshots -File | Select-Object -ExpandProperty FullName)
    } | ConvertTo-Json -Depth 4
}
finally {
    foreach ($process in @($installedProcess,$relaunchProcess,$portableProcess)) {
        if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
}
