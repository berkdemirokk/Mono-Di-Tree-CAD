param([Parameter(Mandatory=$true)][string]$Dotnet)
$ErrorActionPreference='Stop'
$base='https://nodejs.org/dist/v24.22.1'
$runtime=Join-Path $PSScriptRoot 'runtime-official'
$node=Join-Path $runtime 'node.exe'
$checksums=Join-Path $runtime 'SHASUMS256.txt'
$license=Join-Path $runtime 'LICENSE.txt'
New-Item -ItemType Directory -Force $runtime | Out-Null
Invoke-WebRequest -Uri "$base/SHASUMS256.txt" -OutFile $checksums -TimeoutSec 30
$line=@(Get-Content -LiteralPath $checksums | Where-Object {$_ -match '^([a-f0-9]{64})\s+win-x64/node\.exe\s*$'})
if($line.Count -ne 1){throw 'Official executable checksum is missing or ambiguous.'}
$expected=($line[0] -split '\s+')[0]
if($expected -ne 'd702daa815eabbff763c7d3808e235cf031cf2ca70211b56847bc72f51e240e5'){throw 'Published checksum changed; inspect before execution.'}
Invoke-WebRequest -Uri "$base/win-x64/node.exe" -OutFile $node -TimeoutSec 60
$actual=(Get-FileHash -LiteralPath $node -Algorithm SHA256).Hash.ToLowerInvariant()
if($actual -ne $expected){throw 'Downloaded Node executable does not match official checksum; not executed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'license-research/NODE-v24.22.1-LICENSE.txt') -Destination $license
$licenseHash=(Get-FileHash -LiteralPath $license -Algorithm SHA256).Hash.ToLowerInvariant()
if($licenseHash -ne '5888dbb9a1d2b18f2c3e6c5f6af1b39de658372b402a0577b002777f14c62ace'){throw 'Reference license differs from recorded upstream bytes.'}
& $node --version
if($LASTEXITCODE -ne 0){throw 'Downloaded Node failed to run.'}
$env:TEKSTILCAD_DOTNET=$Dotnet
& $node --test (Join-Path $PSScriptRoot '*.test.mjs')
if($LASTEXITCODE -ne 0){throw 'Synthetic regression suite failed.'}
