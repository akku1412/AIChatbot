<#
.SYNOPSIS
  Builds a deployable package: the ASP.NET Core API with the Angular app in its wwwroot.

.DESCRIPTION
  1. Production build of the Angular app (AIChatFrontend).
  2. dotnet publish of ChatGPTClone.Api (Release).
  3. Copies the Angular browser files into <output>\wwwroot, so one site serves both
     the SPA and /api (same origin, which the HttpOnly refresh cookie relies on).
  No secrets are written: configure them on the server (see README.md, "Production configuration").

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\publish.ps1
  powershell -ExecutionPolicy Bypass -File .\publish.ps1 -Output D:\deploy\aichat
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$frontend = Join-Path $PSScriptRoot 'AIChatFrontend'
$api = Join-Path $PSScriptRoot 'ChatGPTClone.Api\ChatGPTClone.Api.csproj'

Write-Host '1/3 Building Angular (production)...' -ForegroundColor Cyan
Push-Location $frontend
try {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    npx ng build --configuration production
    if ($LASTEXITCODE -ne 0) { throw 'Angular build failed' }
}
finally { Pop-Location }

Write-Host '2/3 Publishing the API (Release)...' -ForegroundColor Cyan
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
dotnet publish $api -c Release -o $Output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host '3/3 Copying the Angular app into wwwroot...' -ForegroundColor Cyan
$browser = Join-Path $frontend 'dist\AIChatFrontend\browser'
$wwwroot = Join-Path $Output 'wwwroot'
New-Item -ItemType Directory -Force $wwwroot | Out-Null
Copy-Item (Join-Path $browser '*') $wwwroot -Recurse -Force

# Never ship development-only settings.
Remove-Item (Join-Path $Output 'appsettings.Development.json') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "Done: $Output" -ForegroundColor Green
Write-Host 'Run it with:  dotnet ChatGPTClone.Api.dll   (after setting the production environment variables)'
