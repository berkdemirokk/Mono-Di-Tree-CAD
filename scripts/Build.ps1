param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
if (!(Test-Path $sdk)) { $sdk = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $sdk build (Join-Path $root 'TekstilCAD.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Test) {
    & $sdk test (Join-Path $root 'tests\TekstilCAD.Tests\TekstilCAD.Tests.csproj') -c Release --no-build --nologo
    exit $LASTEXITCODE
}
