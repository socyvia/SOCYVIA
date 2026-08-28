param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(100, 125, 150, 200)]
    [int]$ScalePercent
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

$scale = $ScalePercent / 100
$env:AVALONIA_GLOBAL_SCALE_FACTOR = $scale.ToString(
    [System.Globalization.CultureInfo]::InvariantCulture)

$executable = Resolve-Path (
    Join-Path $PSScriptRoot '..\bin\Debug\net10.0\SOCYVIA.exe')
$process = Start-Process -FilePath $executable.Path -PassThru

try {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $enterCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'EnterWorkspaceButton')

    $window = $null
    $enterButton = $null
    for ($attempt = 0; $attempt -lt 14 -and $null -eq $enterButton; $attempt++) {
        Start-Sleep -Seconds 1
        $window = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        if ($null -ne $window) {
            $enterButton = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $enterCondition)
        }
    }

    if ($null -eq $enterButton) {
        throw 'The login action was clipped or unavailable.'
    }

    $invoke = $enterButton.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Start-Sleep -Seconds 4

    $window = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $processCondition)
    $requiredControls = @(
        'HomeButton',
        'ContentLibraryButton',
        'StudiesButton',
        'SettingsButton',
        'CurrentSectionTitle',
        'WebsiteFooterLink',
        'MinimizeButton',
        'MaximizeRestoreButton',
        'CloseButton')

    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($automationId in $requiredControls) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            $automationId)
        $control = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)

        if ($null -eq $control) {
            $failures.Add($automationId)
            continue
        }

        $controlBounds = $control.Current.BoundingRectangle
        $windowBounds = $window.Current.BoundingRectangle
        $hasArea = $controlBounds.Width -gt 0 -and $controlBounds.Height -gt 0
        $intersectsWindow =
            $controlBounds.Right -gt $windowBounds.Left -and
            $controlBounds.Left -lt $windowBounds.Right -and
            $controlBounds.Bottom -gt $windowBounds.Top -and
            $controlBounds.Top -lt $windowBounds.Bottom
        if (-not $hasArea -or -not $intersectsWindow) {
            $failures.Add($automationId)
        }
    }

    $bounds = $window.Current.BoundingRectangle
    [pscustomobject]@{
        Scale = "${ScalePercent}%"
        Window = "$([int]$bounds.Width)x$([int]$bounds.Height)"
        Result = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
        MissingOrClipped = $failures -join ','
    } | ConvertTo-Json -Compress
}
finally {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Remove-Item Env:AVALONIA_GLOBAL_SCALE_FACTOR -ErrorAction SilentlyContinue
}
