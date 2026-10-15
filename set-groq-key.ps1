# Stores a Groq API key in .NET user secrets for ChatGPTClone.Api without showing it on screen.
# Run by double-clicking set-groq-key.cmd. The key never goes into the project folder or git.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'ChatGPTClone.Api'

Write-Host ''
Write-Host '  Groq API key setup for AIChat' -ForegroundColor Cyan
Write-Host '  1. On console.groq.com/keys create a NEW key and click Copy.'
Write-Host '  2. Paste it below (right-click or Ctrl+V). It stays hidden, that is normal.'
Write-Host ''

$secure = Read-Host '  Paste the Groq key and press Enter' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr).Trim() }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

if (-not $key.StartsWith('gsk_') -or $key.Length -lt 40) {
    Write-Host ''
    Write-Host '  That does not look like a Groq key (it should start with gsk_). Nothing was saved.' -ForegroundColor Red
}
elseif ($key.EndsWith('aSxRW')) {
    Write-Host ''
    Write-Host '  This is the old key that was shared in the chat. Delete it on Groq and create a new one. Nothing was saved.' -ForegroundColor Red
}
else {
    try {
        $models = Invoke-RestMethod -Uri 'https://api.groq.com/openai/v1/models' -Headers @{ Authorization = "Bearer $key" } -TimeoutSec 20
        dotnet user-secrets set 'Ai:Groq:ApiKey' $key --project $project | Out-Null
        Write-Host ''
        Write-Host "  Saved. Groq accepted the key ($($models.data.Count) models available)." -ForegroundColor Green
        Write-Host '  You can close this window and tell Claude "saved".'
    }
    catch {
        Write-Host ''
        Write-Host '  Groq rejected this key (or there is no internet). Nothing was saved.' -ForegroundColor Red
    }
}

Remove-Variable key, secure -ErrorAction SilentlyContinue
Write-Host ''
Read-Host '  Press Enter to close'
