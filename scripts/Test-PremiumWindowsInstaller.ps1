[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NativeButtonAutomation
{
    private const int GwlStyle = -16;
    private const long WsTabStop = 0x00010000L;
    private const uint BmClick = 0x00F5;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static IntPtr FindChildControl(IntPtr parent, string expectedText, bool requireButton)
    {
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr window, IntPtr parameter)
        {
            StringBuilder className = new StringBuilder(128);
            StringBuilder text = new StringBuilder(512);
            GetClassName(window, className, className.Capacity);
            GetWindowText(window, text, text.Capacity);
            bool isButton = className.ToString().IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0;
            string normalized = text.ToString().Replace("\u2066", "").Replace("\u2067", "")
                .Replace("\u2068", "").Replace("\u2069", "");
            if ((!requireButton || isButton) && String.Equals(normalized, expectedText, StringComparison.Ordinal))
            {
                result = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool IsKeyboardFocusable(IntPtr window)
    {
        long style = GetWindowLongPtr(window, GwlStyle).ToInt64();
        return (style & WsTabStop) != 0 && IsWindowEnabled(window) && IsWindowVisible(window);
    }

    public static void Invoke(IntPtr window)
    {
        SendMessage(window, BmClick, IntPtr.Zero, IntPtr.Zero);
    }
}
'@

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$setup = Join-Path $workspace 'Download\Windows\SOCYVIA-1.0.0-Windows-x64-Setup.exe'
$qaRoot = Join-Path $workspace 'artifacts\premium-installer-qa'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\SOCYVIA-Premium-Installer-QA'
$storageRoot = Join-Path $qaRoot 'researcher-storage'
$screenshots = Join-Path $workspace 'visual-qa\final-distribution\windows-installer'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SOCYVIA\SOCYVIA.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7A3791F3-F195-46C1-91EF-6682771461D6}_is1'

function ConvertFrom-Utf8Base64([string]$value) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
}

$arabicLanguage = ConvertFrom-Utf8Base64 '2KfZhNi52LHYqNmK2Kk='
$arabicInstallTitle = ConvertFrom-Utf8Base64 '2KrYq9io2YrYqiBTT0NZVklB'
$arabicOptions = ConvertFrom-Utf8Base64 '2K7Zitin2LHYp9iqINin2YTYqtir2KjZitiq'
$arabicInstall = ConvertFrom-Utf8Base64 '2KrYq9io2YrYqg=='
$arabicInstallingStatus = ConvertFrom-Utf8Base64 '2KzYp9ixINiq2KvYqNmK2Kog2YXZhNmB2KfYqiDYp9mE2KrYt9io2YrZgi4uLg=='
$arabicCompleteTitle = ConvertFrom-Utf8Base64 '2KrZhSDYqtir2KjZitiqIFNPQ1lWSUEg2KjZhtis2KfYrQ=='
$arabicLaunch = ConvertFrom-Utf8Base64 '2KrYtNi62YrZhCBTT0NZVklB'
$arabicRemoveTitle = ConvertFrom-Utf8Base64 '2KXYstin2YTYqSBTT0NZVklB'
$arabicRemovingStatus = ConvertFrom-Utf8Base64 '2KzYp9ixINil2LLYp9mE2Kkg2YXZhNmB2KfYqiDYp9mE2KrYt9io2YrZgi4uLg=='
$arabicRemovalComplete = ConvertFrom-Utf8Base64 '2KrZhdiqINil2LLYp9mE2KkgU09DWVZJQSDYqNmG2KzYp9it'
$arabicClose = ConvertFrom-Utf8Base64 '2KXYutmE2KfZgg=='

if (-not (Test-Path -LiteralPath $setup)) { throw 'Final premium Setup executable is missing.' }
if (Test-Path -LiteralPath $installRoot) { throw "QA installation path already exists: $installRoot" }
if (Test-Path -LiteralPath $uninstallKey) { throw 'An existing SOCYVIA installation is registered; premium installer QA will not overwrite it.' }
if (Test-Path -LiteralPath $qaRoot) { Remove-Item -LiteralPath $qaRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $qaRoot,$storageRoot,$screenshots | Out-Null

function Get-ProcessWindows([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $processId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
}

function Find-NamedControl([int]$processId, [string]$name, [string]$controlType = '') {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $name)
    $condition = $nameCondition
    if ($controlType -eq 'Button') {
        $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $condition = New-Object System.Windows.Automation.AndCondition($nameCondition,$typeCondition)
    }
    foreach ($window in Get-ProcessWindows $processId) {
        $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $control) {
            $all = $window.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($candidate in $all) {
                $candidateName = $candidate.Current.Name -replace '[\u2066-\u2069]', ''
                $isRequestedType = $controlType -ne 'Button' -or
                    $candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button
                if ($candidateName -eq $name -and $isRequestedType) {
                    $control = $candidate
                    break
                }
            }
        }
        if ($null -ne $control) {
            return [pscustomobject]@{
                Window=$window
                Control=$control
                Provider='UIAutomation'
                KeyboardFocusable=$control.Current.IsKeyboardFocusable
                Handle=[intptr]::Zero
            }
        }
        $handle = [NativeButtonAutomation]::FindChildControl(
            [intptr]$window.Current.NativeWindowHandle,
            $name,
            ($controlType -eq 'Button'))
        if ($handle -ne [intptr]::Zero) {
            return [pscustomobject]@{
                Window=$window
                Control=$null
                Provider='NativeWin32'
                KeyboardFocusable=$(if ($controlType -eq 'Button') { [NativeButtonAutomation]::IsKeyboardFocusable($handle) } else { $false })
                Handle=$handle
            }
        }
    }
    return $null
}

function Wait-NamedControl([int]$processId, [string]$name, [int]$seconds = 60, [string]$controlType = '') {
    for ($attempt=0; $attempt -lt ($seconds * 4); $attempt++) {
        Start-Sleep -Milliseconds 250
        $result = Find-NamedControl $processId $name $controlType
        if ($null -ne $result) { return $result }
    }
    throw "Installer control '$name' did not become available."
}

function Invoke-NamedControl([int]$processId, [string]$name) {
    $result = Wait-NamedControl $processId $name 60 'Button'
    if ($result.Provider -eq 'NativeWin32') {
        [NativeButtonAutomation]::Invoke($result.Handle)
    }
    else {
        $pattern = $result.Control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    }
    Start-Sleep -Milliseconds 350
}

function Capture-Setup([int]$processId, [string]$name) {
    & (Join-Path $PSScriptRoot 'Capture-SocyviaWindow.ps1') `
        -ProcessId $processId -OutputPath (Join-Path $screenshots $name) -PreserveWindowState | Out-Null
}

$setupProcess = $null
$appProcess = $null
$uninstallProcess = $null
$dataPreserved = $false
try {
    $previousStorage = $env:SOCYVIA_STORAGE_ROOT
    $env:SOCYVIA_STORAGE_ROOT = $storageRoot
    try {
        $setupProcess = Start-Process -FilePath $setup -ArgumentList "/DIR=`"$installRoot`"" -PassThru
    }
    finally {
        if ($null -eq $previousStorage) { Remove-Item Env:SOCYVIA_STORAGE_ROOT -ErrorAction SilentlyContinue }
        else { $env:SOCYVIA_STORAGE_ROOT = $previousStorage }
    }

    $welcome = Wait-NamedControl $setupProcess.Id 'Install SOCYVIA' 60 'Button'
    if (-not $welcome.KeyboardFocusable) { throw 'Primary welcome action is not keyboard-focusable.' }
    Write-Host "Welcome action verified via $($welcome.Provider)."
    Capture-Setup $setupProcess.Id '01-setup-welcome.png'

    Invoke-NamedControl $setupProcess.Id $arabicLanguage
    Wait-NamedControl $setupProcess.Id $arabicInstallTitle 30 | Out-Null
    Capture-Setup $setupProcess.Id '02-setup-welcome-arabic.png'

    Invoke-NamedControl $setupProcess.Id 'English'
    Wait-NamedControl $setupProcess.Id 'Install SOCYVIA' 30 'Button' | Out-Null
    Invoke-NamedControl $setupProcess.Id $arabicLanguage
    Wait-NamedControl $setupProcess.Id $arabicInstallTitle 30 | Out-Null
    Invoke-NamedControl $setupProcess.Id $arabicOptions

    $optionsInstall = Wait-NamedControl $setupProcess.Id $arabicInstall 60 'Button'
    if (-not $optionsInstall.KeyboardFocusable) { throw 'Options Install action is not keyboard-focusable.' }
    Write-Host "Arabic options action verified via $($optionsInstall.Provider)."
    Capture-Setup $setupProcess.Id '03-setup-options-arabic.png'

    Invoke-NamedControl $setupProcess.Id 'English'
    Wait-NamedControl $setupProcess.Id 'Installation options' 30 | Out-Null
    Capture-Setup $setupProcess.Id '04-setup-options-english.png'
    Invoke-NamedControl $setupProcess.Id $arabicLanguage
    Wait-NamedControl $setupProcess.Id $arabicOptions 30 | Out-Null

    Invoke-NamedControl $setupProcess.Id $arabicInstall
    Wait-NamedControl $setupProcess.Id $arabicInstallingStatus 30 | Out-Null
    Write-Host 'Arabic installing state verified.'
    Capture-Setup $setupProcess.Id '05-setup-installing-arabic.png'

    Wait-NamedControl $setupProcess.Id $arabicCompleteTitle 180 | Out-Null
    Write-Host 'Arabic completion state verified.'
    Capture-Setup $setupProcess.Id '06-setup-complete-arabic.png'

    if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'SOCYVIA.exe'))) { throw 'Installed SOCYVIA executable is missing.' }
    if (-not (Test-Path -LiteralPath $startMenuShortcut)) { throw 'Start Menu shortcut was not created.' }
    $uninstall = Get-ItemProperty -LiteralPath $uninstallKey
    if ($uninstall.DisplayName -ne 'SOCYVIA' -or $uninstall.DisplayVersion -ne '1.0.0' -or $uninstall.Publisher -ne 'SOCYVIA') {
        throw 'Windows Installed Apps metadata is incorrect.'
    }
    if ($uninstall.UninstallString -notlike '*SOCYVIA.Uninstall.exe*') {
        throw 'Windows Installed Apps does not point to the branded SOCYVIA uninstaller.'
    }

    $before = @(Get-Process -Name SOCYVIA -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    Invoke-NamedControl $setupProcess.Id $arabicLaunch
    Write-Host 'Launch action invoked.'
    for ($attempt=0; $attempt -lt 120; $attempt++) {
        Start-Sleep -Milliseconds 500
        $appProcess = Get-Process -Name SOCYVIA -ErrorAction SilentlyContinue |
            Where-Object { $before -notcontains $_.Id } |
            Select-Object -First 1
        if ($null -ne $appProcess -and $appProcess.MainWindowHandle -ne 0) { break }
    }
    if ($null -eq $appProcess) { throw 'The installed application did not launch from the completion screen.' }
    $appProcess.CloseMainWindow() | Out-Null
    $appProcess.WaitForExit(15000) | Out-Null

    $uninstaller = Join-Path $installRoot 'SOCYVIA.Uninstall.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw 'Branded uninstaller was not installed.' }
    $priorUninstallers = @(Get-Process -Name 'SOCYVIA.Uninstall' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    $uninstallBootstrap = Start-Process -FilePath $uninstaller -PassThru
    for ($attempt=0; $attempt -lt 120; $attempt++) {
        Start-Sleep -Milliseconds 250
        $uninstallProcess = Get-Process -Name 'SOCYVIA.Uninstall' -ErrorAction SilentlyContinue |
            Where-Object { $priorUninstallers -notcontains $_.Id -and $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
        if ($null -ne $uninstallProcess) { break }
    }
    if ($null -eq $uninstallProcess) { throw 'Branded uninstaller window did not open.' }

    Wait-NamedControl $uninstallProcess.Id 'Remove SOCYVIA' 30 | Out-Null
    Capture-Setup $uninstallProcess.Id '07-uninstall-confirm-english.png'
    Invoke-NamedControl $uninstallProcess.Id $arabicLanguage
    Wait-NamedControl $uninstallProcess.Id $arabicRemoveTitle 30 | Out-Null
    Capture-Setup $uninstallProcess.Id '08-uninstall-confirm-arabic.png'
    Invoke-NamedControl $uninstallProcess.Id $arabicRemoveTitle
    Wait-NamedControl $uninstallProcess.Id $arabicRemovingStatus 30 | Out-Null
    Capture-Setup $uninstallProcess.Id '09-uninstall-removing-arabic.png'
    Invoke-NamedControl $uninstallProcess.Id 'English'
    Wait-NamedControl $uninstallProcess.Id 'Removing application files...' 30 | Out-Null
    Capture-Setup $uninstallProcess.Id '10-uninstall-removing-english.png'
    Wait-NamedControl $uninstallProcess.Id "SOCYVIA removed`r`nsuccessfully" 180 | Out-Null
    Capture-Setup $uninstallProcess.Id '11-uninstall-complete-english.png'
    Invoke-NamedControl $uninstallProcess.Id $arabicLanguage
    Wait-NamedControl $uninstallProcess.Id $arabicRemovalComplete 30 | Out-Null
    Capture-Setup $uninstallProcess.Id '12-uninstall-complete-arabic.png'
    Invoke-NamedControl $uninstallProcess.Id $arabicClose
    $uninstallProcess.WaitForExit(15000) | Out-Null
    Start-Sleep -Seconds 2
    if (Test-Path -LiteralPath $installRoot) { throw 'Application binaries remain after uninstall.' }
    if (Test-Path -LiteralPath $startMenuShortcut) { throw 'Start Menu shortcut remains after uninstall.' }
    $dataPreserved = Test-Path -LiteralPath $storageRoot
    if (-not $dataPreserved) { throw 'Uninstall removed isolated researcher storage.' }

    $result = [ordered]@{
        Result='PASS'
        Setup='PREMIUM INSTALLER PHYSICALLY VERIFIED'
        Welcome='VERIFIED'
        Options='VERIFIED'
        Installing='VERIFIED'
        Complete='VERIFIED'
        InstallerLanguageSwitch='ENGLISH-ARABIC-ENGLISH-ARABIC VERIFIED'
        InstallerRtl='PHYSICALLY CAPTURED'
        KeyboardFocusable=$true
        InstalledAppLaunch='VERIFIED'
        StartMenu='VERIFIED'
        InstalledAppsMetadata='VERIFIED'
        Uninstall='BRANDED UNINSTALLER PHYSICALLY VERIFIED'
        UninstallEnglish='VERIFIED'
        UninstallArabic='VERIFIED'
        DataPreservedAfterUninstall=$dataPreserved
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $screenshots 'premium-installer-qa.json'),
        ($result | ConvertTo-Json -Depth 4),
        [System.Text.UTF8Encoding]::new($false))
    $result | ConvertTo-Json -Depth 4
}
finally {
    foreach ($process in @($setupProcess,$appProcess,$uninstallProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
