param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][string]$Jobs,
    [Parameter(Mandatory=$true)][string]$ExpectedResults,
    [Parameter(Mandatory=$true)][string]$NewDirectory
)
$ErrorActionPreference='Stop'
if (Test-Path -LiteralPath $NewDirectory) { throw 'Destination already exists.' }
$source=(Resolve-Path -LiteralPath $PublishDirectory).Path
$jobsPath=(Resolve-Path -LiteralPath $Jobs).Path
$expected=(Resolve-Path -LiteralPath $ExpectedResults).Path
$all=Get-ChildItem -LiteralPath $source
if (@($all | Where-Object { $_.PSIsContainer }).Count -ne 0) { throw 'Expected a flat published directory.' }
$ffmpeg=@($all | Where-Object { $_.Name -eq 'opencv_videoio_ffmpeg4100_64.dll' })
if ($ffmpeg.Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $source 'PhotoRegionSharpPrototype.exe'))) { throw 'Expected published EXE and FFmpeg DLL.' }
$created=New-Item -ItemType Directory -Path $NewDirectory
$destination=$created.FullName
$all | Where-Object { $_.Name -ne 'opencv_videoio_ffmpeg4100_64.dll' } | Copy-Item -Destination $destination
$env:DOTNET_ROOT=Join-Path $destination 'no-dotnet-installation'
$env:DOTNET_MULTILEVEL_LOOKUP='0'
$exe=Join-Path $destination 'PhotoRegionSharpPrototype.exe'
& $exe --selftest
if ($LASTEXITCODE -ne 0) { throw 'Synthetic self-test failed.' }
$actual=Join-Path $destination 'component-results.json'
& $exe --components-batch $jobsPath $actual
if ($LASTEXITCODE -ne 0) { throw 'Photo batch failed.' }
$actualHash=(Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash
$expectedHash=(Get-FileHash -LiteralPath $expected -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) { throw 'Photo results differ.' }
$report=[ordered]@{
    scope='Self-contained x64 prototype, synthetic checks and specified photo batch; no device or offline-network proof.'
    omitted_file=$ffmpeg[0].Name
    omitted_bytes=$ffmpeg[0].Length
    omitted_sha256=(Get-FileHash -LiteralPath $ffmpeg[0].FullName -Algorithm SHA256).Hash
    native_library_sha256=(Get-FileHash -LiteralPath (Join-Path $destination 'OpenCvSharpExtern.dll') -Algorithm SHA256).Hash
    jobs_sha256=(Get-FileHash -LiteralPath $jobsPath -Algorithm SHA256).Hash
    results_sha256=$actualHash
    results_match=$true
    # Results are excluded from the product-file size.
    published_bytes_without_ffmpeg=($all | Where-Object { $_.Name -ne $ffmpeg[0].Name } | Measure-Object -Property Length -Sum).Sum
}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'comparison.json') -Encoding utf8NoBOM
$report | ConvertTo-Json
