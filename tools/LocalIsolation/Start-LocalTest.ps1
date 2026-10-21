[CmdletBinding()]
param([string]$SettingsPath, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj'))
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'DOTNET_REQUIRED' }
if (-not (Test-Path -LiteralPath $projectPath)) { throw 'PROJECT_NOT_FOUND' }
$listeners = netstat -ano -p tcp
if ($LASTEXITCODE -ne 0) { throw 'LISTENER_CHECK_UNAVAILABLE' }
if ($listeners -match '127\.0\.0\.1:5186\s.*LISTENING|0\.0\.0\.0:5186\s.*LISTENING|\[::\]:5186\s.*LISTENING') { throw 'LOCAL_API_PORT_BUSY' }
if ($CheckOnly) {
    Write-Output 'PASS: dotnet and project available; API port 5186 unused. No settings read, no database probe, no API started.'
    return
}
if (-not $SettingsPath -or -not [IO.Path]::IsPathRooted($SettingsPath) -or -not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) { throw 'PRIVATE_SETTINGS_FILE_REQUIRED' }
if ([IO.Path]::GetFileName($SettingsPath) -notlike 'appsettings.*.Local.json') { throw 'USE_GIT_IGNORED_LOCAL_SETTINGS_FILENAME' }
# Startup is an explicit operator action after D4 approval; this script never creates a DB/user,
# migrates/seeds, changes firewall or sets up a tunnel. API enforces exact isolated connection.
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousDotnetEnvironment = $env:DOTNET_ENVIRONMENT
$previousSettings = $env:EARLYCARE_LOCAL_SETTINGS
try {
    $env:ASPNETCORE_ENVIRONMENT = 'IsolatedTest'
    $env:DOTNET_ENVIRONMENT = 'IsolatedTest'
    $env:EARLYCARE_LOCAL_SETTINGS = [IO.Path]::GetFullPath($SettingsPath)
    Write-Output 'Starting loopback API only; stop using Ctrl+C. No database readiness claim.'
    dotnet run --no-launch-profile --no-build --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'API_PROCESS_FAILED' }
} finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:DOTNET_ENVIRONMENT = $previousDotnetEnvironment
    $env:EARLYCARE_LOCAL_SETTINGS = $previousSettings
}
