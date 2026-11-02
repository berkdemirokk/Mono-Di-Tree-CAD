param([Parameter(Mandatory=$true)][string]$Dotnet,[string]$Node='node')
$ErrorActionPreference='Stop'
$experiment=$PSScriptRoot
foreach($project in @('bridge-probe/Probe.csproj','process-probe/ProcessProbe.csproj','windows-node-probe/NodeProbe.csproj')) {
  & $Dotnet build (Join-Path $experiment $project) --verbosity quiet
  if($LASTEXITCODE -ne 0){throw "Synthetic probe build failed: $project"}
}
$env:TEKSTILCAD_DOTNET=$Dotnet
& $Node --test (Join-Path $experiment '*.test.mjs')
if($LASTEXITCODE -ne 0){throw 'Synthetic parallel tests failed.'}
