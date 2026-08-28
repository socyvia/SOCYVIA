param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(100,125,150,200)]
    [int]$ScalePercent
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SocyviaWindowState
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window, int command);
}
'@

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$executable = Join-Path $workspace 'bin\Debug\net10.0\SOCYVIA.exe'
$qaRoot = Join-Path $workspace "artifacts\studies-empty-dpi\$ScalePercent"
$screenshots = Join-Path $workspace 'visual-qa\studies-empty-dpi'
$expectedQaRoot = [IO.Path]::GetFullPath((Join-Path $workspace "artifacts\studies-empty-dpi\$ScalePercent"))
if ([IO.Path]::GetFullPath($qaRoot) -ne $expectedQaRoot) { throw 'Unsafe Studies DPI QA path.' }
if (Test-Path -LiteralPath $qaRoot) { Remove-Item -LiteralPath $qaRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $qaRoot,$screenshots | Out-Null
if (-not (Test-Path -LiteralPath $executable)) { throw 'Debug SOCYVIA executable is missing.' }

function Find-Control([int]$processId, [string]$automationId) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $idCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $processCondition)
    foreach ($window in $windows) {
        $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $idCondition)
        if ($null -ne $control) { return [pscustomobject]@{Window=$window;Control=$control} }
    }
    return $null
}

function Wait-Control([int]$processId, [string]$automationId, [int]$seconds=45) {
    for ($attempt=0; $attempt -lt ($seconds * 4); $attempt++) {
        Start-Sleep -Milliseconds 250
        $result = Find-Control $processId $automationId
        if ($null -ne $result) { return $result }
    }
    throw "Studies DPI control '$automationId' was unavailable at $ScalePercent%."
}

function Invoke-Control([int]$processId, [string]$automationId) {
    $result = Wait-Control $processId $automationId
    $pattern = $result.Control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 400
}

function Assert-Centered([int]$processId, [string]$language, [string]$windowState) {
    $title = Wait-Control $processId 'EmptyTitle'
    $description = Wait-Control $processId 'EmptyDescription'
    $button = Wait-Control $processId 'EmptyCreateButton'
    $buttonBounds = $button.Control.Current.BoundingRectangle
    $center = $buttonBounds.Left + ($buttonBounds.Width / 2)
    foreach ($item in @($title,$description)) {
        $bounds = $item.Control.Current.BoundingRectangle
        $delta = ($bounds.Left + ($bounds.Width / 2)) - $center
        if ([math]::Abs($delta) -gt 1.5) {
            throw "$language $windowState empty state is off-center by $([math]::Round($delta,2)) pixels at $ScalePercent%."
        }
    }
    return [ordered]@{
        Language=$language
        State=$windowState
        ButtonCenter=[math]::Round($center,2)
        Result='PASS'
    }
}

function Capture-State([int]$processId, [string]$name) {
    & (Join-Path $PSScriptRoot 'Capture-SocyviaWindow.ps1') -ProcessId $processId `
        -OutputPath (Join-Path $screenshots "$ScalePercent-$name.png") -PreserveWindowState | Out-Null
}

$priorStorage = $env:SOCYVIA_STORAGE_ROOT
$priorScale = $env:AVALONIA_GLOBAL_SCALE_FACTOR
$env:SOCYVIA_STORAGE_ROOT = $qaRoot
$env:AVALONIA_GLOBAL_SCALE_FACTOR = ($ScalePercent / 100.0).ToString([Globalization.CultureInfo]::InvariantCulture)
$process = $null
try {
    $process = Start-Process -FilePath $executable -PassThru
    Wait-Control $process.Id 'EnterWorkspaceButton' | Out-Null
    Invoke-Control $process.Id 'EnglishLanguageButton'
    Invoke-Control $process.Id 'NewModeButton'
    $name = Wait-Control $process.Id 'ResearcherNameBox'
    $name.Control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("Studies DPI $ScalePercent")
    $privacy = Wait-Control $process.Id 'PrivacyCheckBox'
    $toggle = $privacy.Control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
    Invoke-Control $process.Id 'EnterWorkspaceButton'
    Wait-Control $process.Id 'HomeButton' 60 | Out-Null
    if ($null -ne (Find-Control $process.Id 'TourContextText')) { Invoke-Control $process.Id 'SkipButton' }

    Invoke-Control $process.Id 'StudiesButton'
    $window = (Wait-Control $process.Id 'EmptyCreateButton').Window
    $handle = [intptr]$window.Current.NativeWindowHandle
    [SocyviaWindowState]::ShowWindow($handle, 3) | Out-Null
    Start-Sleep -Milliseconds 600
    $results = @()
    $results += [pscustomobject](Assert-Centered $process.Id 'English' 'Maximized')
    Capture-State $process.Id 'english-maximized'

    [SocyviaWindowState]::ShowWindow($handle, 9) | Out-Null
    Start-Sleep -Milliseconds 600
    $results += [pscustomobject](Assert-Centered $process.Id 'English' 'Restored')

    Invoke-Control $process.Id 'SettingsButton'
    Invoke-Control $process.Id 'SettingsArabicButton'
    Invoke-Control $process.Id 'StudiesButton'
    $results += [pscustomobject](Assert-Centered $process.Id 'Arabic' 'Restored')
    Capture-State $process.Id 'arabic-restored'

    [SocyviaWindowState]::ShowWindow($handle, 3) | Out-Null
    Start-Sleep -Milliseconds 600
    $results += [pscustomobject](Assert-Centered $process.Id 'Arabic' 'Maximized')
    $results | ConvertTo-Json -Depth 4
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    if ($null -eq $priorStorage) { Remove-Item Env:SOCYVIA_STORAGE_ROOT -ErrorAction SilentlyContinue } else { $env:SOCYVIA_STORAGE_ROOT=$priorStorage }
    if ($null -eq $priorScale) { Remove-Item Env:AVALONIA_GLOBAL_SCALE_FACTOR -ErrorAction SilentlyContinue } else { $env:AVALONIA_GLOBAL_SCALE_FACTOR=$priorScale }
}
