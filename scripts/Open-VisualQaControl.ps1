param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [Parameter(Mandatory = $true)]
    [string]$AutomationId
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

$root = [System.Windows.Automation.AutomationElement]::RootElement
$processCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
    $ProcessId)
$window = $root.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    $processCondition)
if ($null -eq $window) {
    throw "No SOCYVIA window was found for process $ProcessId."
}

$idCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    $AutomationId)
$control = $window.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    $idCondition)
if ($null -eq $control) {
    throw "SOCYVIA control '$AutomationId' was not found."
}

$invoke = $control.GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
