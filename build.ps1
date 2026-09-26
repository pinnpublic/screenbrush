param([switch]$Check, [switch]$DesktopCheck, [switch]$Samples)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$previousCli = $env:DOTNET_CLI_HOME
$previousAppData = $env:APPDATA
$previousPackages = $env:NUGET_PACKAGES
Push-Location $projectRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
    $env:APPDATA = Join-Path $projectRoot '.tools/build-appdata'
    $env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
    & $dotnet restore tests/ScreenBrush.Checks/ScreenBrush.Checks.csproj --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed. Install the .NET 10 SDK.' }
    & $dotnet build tests/ScreenBrush.Checks/ScreenBrush.Checks.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Check) {
        & $dotnet tests/ScreenBrush.Checks/bin/Release/net10.0-windows/ScreenBrush.Checks.dll
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    }
    if ($DesktopCheck) {
        & $dotnet tests/ScreenBrush.Checks/bin/Release/net10.0-windows/ScreenBrush.Checks.dll --desktop
        if ($LASTEXITCODE -ne 0) { throw 'Desktop checks failed.' }
    }
    & $dotnet publish src/ScreenBrush/ScreenBrush.csproj -c Release --no-restore -o artifacts/ScreenBrush
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Samples) {
        & $dotnet artifacts/ScreenBrush/ScreenBrush.dll --render-samples artifacts/brush-samples.png
        if ($LASTEXITCODE -ne 0) { throw 'Sample rendering failed.' }
    }
    Write-Host 'Ready: artifacts/ScreenBrush/ScreenBrush.exe'
}
finally {
    $env:DOTNET_CLI_HOME = $previousCli
    $env:APPDATA = $previousAppData
    $env:NUGET_PACKAGES = $previousPackages
    Pop-Location
}
