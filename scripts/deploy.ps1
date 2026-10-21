# Builds MacDock into its fixed install folder and restarts it there.
# Autostart points at whichever exe ran last, so the dock always runs from this one place.
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$install = Join-Path $env:LOCALAPPDATA "MacDock\app"

Get-Process MacDock -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { Stop-Process -Id $_.Id -Force }
}

dotnet build (Join-Path $root "MacDock.sln") -c $Configuration "-p:OutDir=$install\" | Select-String -Pattern " error |Hata|Uyarı|Warn" 
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Start-Process (Join-Path $install "MacDock.exe")
"MacDock running from $install"
