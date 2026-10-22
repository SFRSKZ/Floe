# Builds Floe into its fixed install folder and restarts it there.
# Autostart points at whichever exe ran last, so the dock always runs from this one place.
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$install = Join-Path $env:LOCALAPPDATA "Floe\app"

# MacDock is the name builds ran under before the rename.
Get-Process Floe, MacDock -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { Stop-Process -Id $_.Id -Force }
}

dotnet build (Join-Path $root "Floe.sln") -c $Configuration "-p:OutDir=$install\" | Select-String -Pattern " error |Hata|Uyarı|Warn" 
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Start-Process (Join-Path $install "Floe.exe")
"Floe running from $install"
