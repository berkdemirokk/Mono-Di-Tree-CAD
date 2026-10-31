$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
if (!(Test-Path $sdk)) { $sdk = 'dotnet' }
$env:DOTNET_ROOT = Split-Path $sdk -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $sdk run --project (Join-Path $root 'src\TekstilCAD.Desktop\TekstilCAD.Desktop.csproj') -c Release
