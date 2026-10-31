[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not [OperatingSystem]::IsWindows()) { throw 'This test verifies Windows file sharing semantics.' }
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dir = Join-Path $root ('work\dxf-writer-conflict-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$probe = Join-Path $root 'tests\ProcessProbe\bin\Release\net10.0\ProcessProbe.dll'
if (!(Test-Path -LiteralPath $probe)) { throw 'Build tests/ProcessProbe/ProcessProbe.csproj in Release first.' }
$file = Join-Path $dir 'sample.dxf'
$ready = Join-Path $dir 'ready'
$release = Join-Path $dir 'release'
[IO.File]::WriteAllText($file, "0`nSECTION`n2`nENTITIES`n0`nLINE`n10`n0`n20`n0`n11`n10`n21`n0`n0`nENDSEC`n0`nEOF`n")
$before = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
$process = $null
try {
    $process = Start-Process -FilePath $sdk -ArgumentList @('"' + $probe + '"', 'dxf-hold-writer', '"' + $file + '"', '"' + $ready + '"', '"' + $release + '"') -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (!(Test-Path -LiteralPath $ready) -and !$process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path -LiteralPath $ready) -or $process.HasExited) { throw 'Child writer was not live.' }
    $blocked = @(& $sdk $probe dxf-import $file)
    if ($LASTEXITCODE -ne 3 -or $blocked.Count -ne 1 -or $blocked[0] -ne 'io-error') { throw 'Import did not reject the live writer.' }
    [IO.File]::WriteAllText($release, 'release')
    if (!$process.WaitForExit(30000) -or $process.ExitCode -ne 0) { throw 'Child writer did not exit normally.' }
    $imported = @(& $sdk $probe dxf-import $file)
    if ($LASTEXITCODE -ne 0 -or $imported.Count -ne 1 -or $imported[0] -ne 'imported|1') { throw 'Import after writer closed failed.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $before) { throw 'Source file changed.' }
    Write-Output 'PASS: live independent writer prevents DXF import; import succeeds after close and bytes remain unchanged.'
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
