$ErrorActionPreference = 'SilentlyContinue'
# Claude Code Notification hook: reads the hook JSON from stdin and shows it in Island.
$raw = [Console]::In.ReadToEnd()
$message = 'Нужен ваш ответ'
try {
    $data = $raw | ConvertFrom-Json
    if ($data.message) { $message = [string]$data.message }
} catch { }

# Island accepts only requests carrying its token, which lives in its own settings file.
$token = $null
try {
    $settings = Get-Content -Raw -Encoding UTF8 (Join-Path $env:APPDATA 'Island\settings.json') | ConvertFrom-Json
    $token = $settings.NotifyToken
} catch { }
if (-not $token) { exit 0 }

$body = @{ title = 'Claude Code ждёт ответа'; message = $message } | ConvertTo-Json
Invoke-RestMethod -Uri 'http://127.0.0.1:47831/notify' -Method Post `
    -Headers @{ 'X-Island-Token' = $token } `
    -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
    -ContentType 'application/json; charset=utf-8' -TimeoutSec 2 | Out-Null
exit 0
