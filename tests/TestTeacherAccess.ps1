param(
    [string]$RuntimeRoot = 'C:\Program Files\Microsoft Visual Studio\2022\Community\dotnet\net8.0\runtime',
    [string]$Compiler = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path ([System.IO.Path]::GetTempPath()) ('teacher-access-check-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $taskOutput | Out-Null
$taskCore = Get-ChildItem (Join-Path $RuntimeRoot 'shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$taskAspNet = Get-ChildItem (Join-Path $RuntimeRoot 'shared/Microsoft.AspNetCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$taskReferences = @(Get-ChildItem $taskCore.FullName,$taskAspNet.FullName -Filter '*.dll' | ForEach-Object {
    try {
        [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
        '/reference:"' + $_.FullName + '"'
    } catch [System.BadImageFormatException] { } # Skip native runtime binaries.
})
$taskGlobals = Join-Path $taskOutput 'GlobalUsings.cs'
@'
global using System;
global using System.Linq;
global using System.Net.Http;
global using System.Threading.Tasks;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
'@ | Set-Content -LiteralPath $taskGlobals -Encoding UTF8
$taskSources = @(
    "$taskRoot/EarlyInterventionCare.Api/Contracts/TeacherGrantContracts.cs",
    "$taskRoot/EarlyInterventionCare.Api/Services/ITeacherGrantService.cs",
    "$taskRoot/EarlyInterventionCare.Api/Development/TestTeacherGrant.cs",
    "$taskRoot/EarlyInterventionCare.Api/Development/InMemoryTeacherGrantService.cs",
    "$taskRoot/EarlyInterventionCare.Api/Development/TeacherAccessMiddleware.cs",
    "$taskRoot/EarlyInterventionCare.Api/Controllers/DevelopmentTeacherGrantsController.cs",
    "$taskRoot/EarlyInterventionCare.Api/Controllers/TeacherTestFormController.cs",
    "$PSScriptRoot/TeacherAccessHarness.cs", $taskGlobals
) | ForEach-Object { '"' + $_ + '"' }
$taskDll = Join-Path $taskOutput 'TeacherAccessChecks.dll'
$taskArgs = @('/nologo','/target:exe','/langversion:12','/nullable:enable',('/out:"' + $taskDll + '"')) + $taskReferences + $taskSources
$taskRsp = Join-Path $taskOutput 'compile.rsp'
$taskArgs | Set-Content -LiteralPath $taskRsp -Encoding UTF8
& $Compiler "@$taskRsp"
if ($LASTEXITCODE -ne 0) { throw 'C-module compilation failed.' }
@{
    runtimeOptions = @{
        tfm = 'net8.0'
        frameworks = @(
            @{ name = 'Microsoft.NETCore.App'; version = $taskCore.Name },
            @{ name = 'Microsoft.AspNetCore.App'; version = $taskAspNet.Name }
        )
    }
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'TeacherAccessChecks.runtimeconfig.json') -Encoding UTF8
& (Join-Path $RuntimeRoot 'dotnet.exe') $taskDll
if ($LASTEXITCODE -ne 0) { throw 'HTTP verification failed.' }
Write-Output 'C-module HTTP checks passed. Full API build/startup must be verified separately.'
