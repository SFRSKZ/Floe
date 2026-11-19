# Floe · Tidepool — one-command preview
#
#   .\preview.ps1
#   .\preview.ps1 -Plugin tidepool -Query "dock=0&t=31&h=120"
#
# Serves the repository root (the preview imports ../js/*.js) and opens the page.

param(
    [int]$Port = 8765,
    [string]$Plugin = "tidepool",
    [string]$Query = "dock=1&t=31"
)

$repo = Split-Path -Parent $PSScriptRoot
$url = "http://127.0.0.1:$Port/plugins/$Plugin/preview/preview.html?$Query"

$server = Get-Command python -ErrorAction SilentlyContinue
if (-not $server) {
    Write-Host "Python bulunamadı. Şunlardan biriyle bir statik sunucu açın:" -ForegroundColor Yellow
    Write-Host "  npx serve `"$repo`""
    Write-Host "  then open $url"
    exit 1
}

Write-Host "Floe plugin preview: $url" -ForegroundColor Cyan
Write-Host "Serving $repo — Ctrl+C to stop." -ForegroundColor DarkGray
Start-Process $url
python -m http.server $Port --directory $repo --bind 127.0.0.1
