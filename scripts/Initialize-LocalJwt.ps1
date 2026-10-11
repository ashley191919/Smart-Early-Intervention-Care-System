param()
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\EarlyInterventionCare.Api\EarlyInterventionCare.Api.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$secretsId = [string]$projectXml.Project.PropertyGroup.UserSecretsId
if ([string]::IsNullOrWhiteSpace($secretsId)) { throw 'Project UserSecretsId is missing.' }
$secretPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json"
$existing = @{}
if (Test-Path -LiteralPath $secretPath) {
    $document = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
    foreach ($property in $document.PSObject.Properties) { $existing[$property.Name] = $property.Value }
    if ($document.Jwt) {
        foreach ($property in $document.Jwt.PSObject.Properties) {
            if (-not $existing.ContainsKey("Jwt:$($property.Name)")) { $existing["Jwt:$($property.Name)"] = $property.Value }
        }
    }
}
$missing = @{}
if ([string]::IsNullOrWhiteSpace([string]$existing['Jwt:SigningKey'])) {
    $keyBytes = New-Object byte[] 32
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($keyBytes) } finally { $random.Dispose() }
    $missing['Jwt:SigningKey'] = [Convert]::ToBase64String($keyBytes)
}
if ([string]::IsNullOrWhiteSpace([string]$existing['Jwt:Issuer'])) { $missing['Jwt:Issuer'] = 'EarlyInterventionCare.Local' }
if ([string]::IsNullOrWhiteSpace([string]$existing['Jwt:Audience'])) { $missing['Jwt:Audience'] = 'EarlyInterventionCare.LocalClient' }
if ([string]::IsNullOrWhiteSpace([string]$existing['Jwt:ExpirationMinutes'])) { $missing['Jwt:ExpirationMinutes'] = '30' }
if ($missing.Count -gt 0) {
    # Supply values through stdin so credentials are not printed or placed in command arguments.
    $result = ($missing | ConvertTo-Json -Compress) | & dotnet user-secrets set --project $projectPath 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Unable to save local User Secrets. No secret values will be printed.' }
    Write-Host ('Added missing JWT settings: ' + (($missing.Keys | Sort-Object) -join ', '))
} else { Write-Host 'Existing JWT settings retained; no key was replaced.' }
Write-Host ('Windows user: ' + [Environment]::UserName)
Write-Host ('User Secrets file: ' + $secretPath)
Write-Host 'Next: start the http profile in Development. Do not share secrets.json.'
