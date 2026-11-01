# Builds the selected Floe product(s) and wraps each in a single setup exe that installs it for the current user.
# Run the setup from Explorer: started from inside a packaged app, it would land in that app's private AppData.
param(
    [string]$Configuration = "Release",
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "work"),
    [ValidateSet("Floe", "CP77", "All")]
    [string]$Product = "All")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$products = if ($Product -eq "All") { @("Floe", "CP77") } else { @($Product) }

New-Item -ItemType Directory $Out -Force | Out-Null

foreach ($name in $products) {
    $setupName = if ($name -eq "CP77") { "FloeCP77Setup" } else { "FloeSetup" }
    # one stage per product, so one payload never picks up the other's files
    $stage = Join-Path ([IO.Path]::GetTempPath()) ("floe-setup-stage-" + $name.ToLowerInvariant())
    $app = Join-Path $stage "app"
    $zip = Join-Path $stage "Floe.zip"

    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory $app | Out-Null

    # self-contained: the .NET 8 desktop runtime travels with Floe, so a fresh PC needs nothing installed first.
    # publish has no --no-incremental of its own (only build does), so force a full build and publish that output
    dotnet build (Join-Path $root "src\Floe.App\Floe.App.csproj") -c $Configuration -r win-x64 --self-contained true -v q -nologo --no-incremental "-p:FloeProduct=$name"
    if ($LASTEXITCODE -ne 0) { throw "$name build failed" }
    dotnet publish (Join-Path $root "src\Floe.App\Floe.App.csproj") -c $Configuration -r win-x64 --self-contained true -o $app -v q -nologo "-p:FloeProduct=$name"
    if ($LASTEXITCODE -ne 0) { throw "$name publish failed" }
    Compress-Archive -Path (Join-Path $app "*") -DestinationPath $zip

    dotnet publish (Join-Path $PSScriptRoot "FloeSetup\FloeSetup.csproj") -c Release "-p:FloeProduct=$name" "-p:FloePayload=$zip" -o (Join-Path $stage "setup") -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "$name setup build failed" }
    Copy-Item (Join-Path $stage "setup\$setupName.exe") (Join-Path $Out "$setupName.exe") -Force
    "Setup: $(Join-Path $Out "$setupName.exe")"
}
