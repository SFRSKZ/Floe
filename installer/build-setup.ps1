# Builds Floe and wraps it in a single FloeSetup.exe that installs it for the current user.
# Run the setup from Explorer: started from inside a packaged app, it would land in that app's private AppData.
param([string]$Configuration = "Release", [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "work"))

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path ([IO.Path]::GetTempPath()) "floe-setup-stage"
$app = Join-Path $stage "app"
$zip = Join-Path $stage "Floe.zip"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory $app | Out-Null

# self-contained: the .NET 8 desktop runtime travels with Floe, so a fresh PC needs nothing installed first
dotnet publish (Join-Path $root "src\Floe.App\Floe.App.csproj") -c $Configuration -r win-x64 --self-contained true -o $app -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "Floe build failed" }
Compress-Archive -Path (Join-Path $app "*") -DestinationPath $zip

dotnet publish (Join-Path $PSScriptRoot "FloeSetup\FloeSetup.csproj") -c Release "-p:FloePayload=$zip" -o (Join-Path $stage "setup") -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "setup build failed" }
Copy-Item (Join-Path $stage "setup\FloeSetup.exe") (Join-Path $Out "FloeSetup.exe") -Force
"Setup: $(Join-Path $Out 'FloeSetup.exe')"
