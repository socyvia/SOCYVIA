param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(100, 125, 150, 200)]
    [int]$ScalePercent,

    [string]$StorageRoot = '',

    [string]$Executable = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

if ([string]::IsNullOrWhiteSpace($StorageRoot)) {
    $StorageRoot = Join-Path $env:TEMP "socyvia-login-geometry-$ScalePercent"
}
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $PSScriptRoot '..\bin\Debug\net10.0\SOCYVIA.exe'
}

$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$resolvedStorage = [System.IO.Path]::GetFullPath($StorageRoot)
[System.IO.Directory]::CreateDirectory($resolvedStorage) | Out-Null

$priorStorage = $env:SOCYVIA_STORAGE_ROOT
$priorScale = $env:AVALONIA_GLOBAL_SCALE_FACTOR
$env:SOCYVIA_STORAGE_ROOT = $resolvedStorage
$env:AVALONIA_GLOBAL_SCALE_FACTOR = ($ScalePercent / 100).ToString(
    [System.Globalization.CultureInfo]::InvariantCulture)

function Find-Control {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$AutomationId
    )
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    $control = $Window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
    if ($null -eq $control) {
        throw "Login control '$AutomationId' was not found."
    }
    return $control
}

function Invoke-Control {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$AutomationId
    )
    $control = Find-Control -Window $Window -AutomationId $AutomationId
    $pattern = $control.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 350
}

function Get-Geometry {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$State
    )
    $ids = [System.Collections.Generic.List[string]]@(
        'ResearcherAccessTitleText',
        'ResearcherAccessSubtitleText',
        'ExistingModeButton',
        'NewModeButton',
        'PasswordBox',
        'RememberMeCheckBox',
        'EnterWorkspaceButton')
    if ($State -like '*New*') {
        $ids.Add('ResearcherNameBox')
        $ids.Add('ConfirmPasswordBox')
        $ids.Add('PrivacyCheckBox')
    }
    else {
        $ids.Add('ResearcherProfileComboBox')
    }

    $controls = @{}
    foreach ($id in $ids) {
        $control = Find-Control -Window $Window -AutomationId $id
        $bounds = $control.Current.BoundingRectangle
        $controls[$id] = [ordered]@{
            Left = [Math]::Round($bounds.Left, 2)
            Top = [Math]::Round($bounds.Top, 2)
            Width = [Math]::Round($bounds.Width, 2)
            Height = [Math]::Round($bounds.Height, 2)
            Offscreen = $control.Current.IsOffscreen
        }
    }
    return [ordered]@{ State = $State; Controls = $controls }
}

function Assert-MirroredGeometry {
    param($English, $Arabic, [string]$Mode)
    $ids = if ($Mode -eq 'new') {
        @('ExistingModeButton','NewModeButton',
          'ResearcherNameBox','PasswordBox',
          'ConfirmPasswordBox','RememberMeCheckBox','PrivacyCheckBox','EnterWorkspaceButton')
    }
    else {
        @('ExistingModeButton','NewModeButton',
          'ResearcherProfileComboBox','PasswordBox',
          'RememberMeCheckBox','EnterWorkspaceButton')
    }

    foreach ($id in $ids) {
        $properties = if ($id -in @('RememberMeCheckBox','PrivacyCheckBox')) {
            @('Top','Height')
        }
        else {
            @('Top','Width','Height')
        }
        foreach ($property in $properties) {
            $difference = [Math]::Abs(
                [double]$English.Controls[$id][$property] -
                [double]$Arabic.Controls[$id][$property])
            if ($difference -gt 1.0) {
                throw "$Mode $id $property drifted by $difference physical pixels between English and Arabic."
            }
        }
        if ($English.Controls[$id].Offscreen -or $Arabic.Controls[$id].Offscreen) {
            throw "$mode $id is offscreen."
        }
    }
}

$process = $null
try {
    $process = Start-Process -FilePath $resolvedExecutable -PassThru
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $window = $null
    for ($attempt = 0; $attempt -lt 40 -and $null -eq $window; $attempt++) {
        Start-Sleep -Milliseconds 500
        $window = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        if ($null -ne $window) {
            try { Find-Control -Window $window -AutomationId 'EnterWorkspaceButton' | Out-Null }
            catch { $window = $null }
        }
    }
    if ($null -eq $window) { throw 'SOCYVIA login did not become automation-ready.' }

    Invoke-Control -Window $window -AutomationId 'EnglishLanguageButton'
    Invoke-Control -Window $window -AutomationId 'NewModeButton'
    $englishNew = Get-Geometry -Window $window -State 'English-New'

    Invoke-Control -Window $window -AutomationId 'ArabicLanguageButton'
    $arabicNew = Get-Geometry -Window $window -State 'Arabic-New'
    Assert-MirroredGeometry -English $englishNew -Arabic $arabicNew -Mode 'new'

    Invoke-Control -Window $window -AutomationId 'EnglishLanguageButton'
    $englishNewAfterRoundTrip = Get-Geometry -Window $window -State 'English-New-AfterRoundTrip'
    Assert-MirroredGeometry -English $englishNew -Arabic $englishNewAfterRoundTrip -Mode 'new'

    Invoke-Control -Window $window -AutomationId 'ExistingModeButton'
    $englishRegistered = Get-Geometry -Window $window -State 'English-Registered'
    Invoke-Control -Window $window -AutomationId 'ArabicLanguageButton'
    $arabicRegistered = Get-Geometry -Window $window -State 'Arabic-Registered'
    Assert-MirroredGeometry -English $englishRegistered -Arabic $arabicRegistered -Mode 'registered'

    Invoke-Control -Window $window -AutomationId 'NewModeButton'
    Invoke-Control -Window $window -AutomationId 'ExistingModeButton'
    $arabicRegisteredAfterModeRoundTrip = Get-Geometry -Window $window -State 'Arabic-Registered-AfterModeRoundTrip'
    Assert-MirroredGeometry -English $arabicRegistered -Arabic $arabicRegisteredAfterModeRoundTrip -Mode 'registered'

    $action = $arabicNew.Controls['EnterWorkspaceButton']
    $windowBounds = $window.Current.BoundingRectangle
    $bottomClearance = $windowBounds.Bottom - ($action.Top + $action.Height)
    # UI Automation reports screen pixels after platform DPI normalization;
    # the minimum visible clearance therefore remains constant across scale factors.
    if ($bottomClearance -lt 35) {
        throw "Arabic action window-edge clearance is only $bottomClearance physical pixels."
    }

    [ordered]@{
        Scale = "$ScalePercent%"
        Result = 'PASS'
        BottomClearance = [Math]::Round($bottomClearance, 2)
        States = @($englishNew, $arabicNew, $englishRegistered, $arabicRegistered)
    } | ConvertTo-Json -Depth 8
}
finally {
    if ($null -ne $process) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $priorStorage) { Remove-Item Env:SOCYVIA_STORAGE_ROOT -ErrorAction SilentlyContinue }
    else { $env:SOCYVIA_STORAGE_ROOT = $priorStorage }
    if ($null -eq $priorScale) { Remove-Item Env:AVALONIA_GLOBAL_SCALE_FACTOR -ErrorAction SilentlyContinue }
    else { $env:AVALONIA_GLOBAL_SCALE_FACTOR = $priorScale }
}
