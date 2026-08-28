[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'osx-arm64', 'osx-x64', 'linux-x64')]
    [string]$Target = 'win-x64',
    [string]$Configuration = 'Release'
)

$ProjectPath = Join-Path $PSScriptRoot '..\SOCYVIA.csproj'
$OutputPath = Join-Path $PSScriptRoot "..\artifacts\release-staging\$Target"
dotnet restore $ProjectPath -r $Target --ignore-failed-sources --disable-parallel
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $ProjectPath -c $Configuration -r $Target --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -p:Version=1.0.0 -o $OutputPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "SOCYVIA 1.0.0 $Target files are in $OutputPath"
Write-Host 'This script performs no deployment, signing, notarization, or public upload.'
