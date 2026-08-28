[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Remove-ApprovedTree([string]$relativePath, [string]$requiredLeaf) {
    $full = [System.IO.Path]::GetFullPath((Join-Path $workspace $relativePath))
    if (-not $full.StartsWith($workspace + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup target escaped the workspace: $full"
    }
    if ([System.IO.Path]::GetFileName($full) -ne $requiredLeaf) {
        throw "Cleanup target leaf did not match: $full"
    }
    if (Test-Path -LiteralPath $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
        Write-Output "Removed $full"
    }
}

foreach ($pair in @(
    @('artifacts','artifacts'),
    @('bin','bin'), @('obj','obj'),
    @('AnalyticsTests\bin','bin'), @('AnalyticsTests\obj','obj'),
    @('FoundationTests\bin','bin'), @('FoundationTests\obj','obj'),
    @('ScientificTests\bin','bin'), @('ScientificTests\obj','obj'),
    @('CloudflareProviderTests\bin','bin'), @('CloudflareProviderTests\obj','obj'),
    @('scripts\DisposablePublicationQa\bin','bin'), @('scripts\DisposablePublicationQa\obj','obj'),
    @('scripts\ReleaseArchiveTool\bin','bin'), @('scripts\ReleaseArchiveTool\obj','obj'),
    @('scripts\LinuxPackageTool\bin','bin'), @('scripts\LinuxPackageTool\obj','obj'),
    @('CloudflareWorker\node_modules','node_modules'),
    @('CloudflareWorker\.wrangler-dry-run','.wrangler-dry-run'),
    @('visual-qa\final-shell','final-shell'), @('visual-qa\final-ui','final-ui'),
    @('visual-qa\final-release-login\qa-storage','qa-storage'),
    @('visual-qa\final-release-login\scale-100','scale-100'),
    @('visual-qa\final-release-login\scale-125','scale-125'),
    @('visual-qa\final-release-login\scale-150','scale-150'),
    @('visual-qa\final-release-login\scale-200','scale-200'),
    @('visual-qa\final-release-login\scale-debug','scale-debug'),
    @('visual-qa\final-release-login\screenshot-storage','screenshot-storage'))) {
    Remove-ApprovedTree $pair[0] $pair[1]
}

foreach ($name in @(
    'ar-new-2.png','ar-new.png','ar-registered.png','en-new.png','en-registered.png',
    'initial-ar-new.png','process.id','final-process.id')) {
    $path = Join-Path $workspace "visual-qa\final-release-login\$name"
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

Write-Output 'Release intermediates removed; Download and final QA evidence were preserved.'
