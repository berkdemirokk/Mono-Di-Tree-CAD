[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$base = Join-Path $root 'work\process-recovery-tests'
$dir = Join-Path $base ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
$ready = Join-Path $dir 'ready.txt'
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$probe = Join-Path $root 'tests\ProcessProbe\bin\Release\net10.0\ProcessProbe.dll'
if (!(Test-Path -LiteralPath $probe)) { throw 'Build tests/ProcessProbe/ProcessProbe.csproj in Release before running this integration check.' }
$process = $null
try {
    $process = Start-Process -FilePath $sdk -ArgumentList @('"' + $probe + '"', 'hold', '"' + $dir + '"', '"' + $ready + '"') -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (!(Test-Path -LiteralPath $ready) -and !$process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path -LiteralPath $ready)) { throw 'Child did not create its durable recovery file.' }
    $path = [IO.File]::ReadAllText($ready)
    if ([IO.Path]::GetDirectoryName($path) -ne $dir -or !(Test-Path -LiteralPath $path)) { throw 'Invalid recovery path from child.' }
    $live = @(& $sdk $probe scan $dir)
    if ($LASTEXITCODE -ne 0 -or $live.Count -ne 0) { throw 'Live recovery session appeared in scan.' }
    # Verify actual OS exclusive lock independently, before forcing an abrupt process exit.
    $lockPath = [IO.Path]::ChangeExtension($path, '.lock')
    try { $lock = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lock.Dispose(); throw 'Live process lease was not exclusive.' }
    catch [IO.IOException] { }
    $process.Kill(); $process.WaitForExit()
    if (!$process.HasExited) { throw 'Child did not terminate.' }
    # Reopen the lease after abrupt termination and check the durable payload survives.
    $lock = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lock.Dispose()
    $dead = @(& $sdk $probe scan $dir)
    if ($LASTEXITCODE -ne 0 -or $dead.Count -ne 1 -or $dead[0] -ne 'Process crash recovery|True|False') { throw 'Crash recovery candidate was not correctly discovered.' }
    $doc = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($doc.Name -ne 'Process crash recovery') { throw 'Recovery payload was not preserved.' }
    Write-Output 'PASS: independent process lease released after termination; saved recovery payload preserved.'
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    # Keep independent test artifacts for inspection; no recursive deletion.
}

