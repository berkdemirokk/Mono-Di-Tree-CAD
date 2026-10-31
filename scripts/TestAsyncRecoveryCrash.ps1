[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dir = Join-Path (Join-Path $root 'work\async-recovery-process-tests') ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$desktop = Join-Path $root 'src\TekstilCAD.Desktop\bin\Release\net10.0-windows\TekstilCAD.Studio.dll'
$probe = Join-Path $root 'tests\ProcessProbe\bin\Release\net10.0\ProcessProbe.dll'
if (!(Test-Path -LiteralPath $desktop) -or !(Test-Path -LiteralPath $probe)) { throw 'Build the solution and ProcessProbe in Release first.' }
$producer = $null
$inspector = $null
try {
    $producer = Start-Process -FilePath $sdk -ArgumentList @('"' + $desktop + '"', '--verify-recovery-child', '"' + $dir + '"') -WindowStyle Hidden -PassThru
    $ready = Join-Path $dir 'ready.json'
    $childError = Join-Path $dir 'child-error.txt'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (!(Test-Path -LiteralPath $ready) -and !$producer.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path -LiteralPath $ready)) {
        if (Test-Path -LiteralPath $childError) { throw ([IO.File]::ReadAllText($childError)) }
        throw 'Hidden child did not complete its recovery write before timeout.'
    }
    $marker = [IO.File]::ReadAllText($ready) | ConvertFrom-Json
    $recoveryDir = [IO.Path]::GetFullPath((Join-Path $dir 'appdata\Recovery'))
    $path = [IO.Path]::GetFullPath($marker.Path)
    if ([IO.Path]::GetDirectoryName($path) -ne $recoveryDir -or [IO.Path]::GetExtension($path) -ne '.tcr' -or !(Test-Path -LiteralPath $path)) { throw 'Invalid recovery path from child.' }
    $lockPath = [IO.Path]::ChangeExtension($path, '.lock')
    try { $lease = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lease.Dispose(); throw 'Live recovery lease was not exclusive.' }
    catch [IO.IOException] { }
    $live = @(& $sdk $probe scan $recoveryDir)
    if ($LASTEXITCODE -ne 0 -or $live.Count -ne 0) { throw 'Active child recovery appeared in scan.' }
    $producer.Kill(); $producer.WaitForExit()
    $lease = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lease.Dispose()
    $inspector = Start-Process -FilePath $sdk -ArgumentList @('"' + $desktop + '"', '--verify-recovery-inspect', '"' + $dir + '"') -WindowStyle Hidden -PassThru
    if (!$inspector.WaitForExit(30000)) { throw 'Hidden inspection timed out.' }
    if ($inspector.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $dir 'inspection.txt'))) {
        $errorPath = Join-Path $dir 'inspection-error.txt'
        if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
        throw 'Recovery payload inspection failed.'
    }
    Write-Output 'PASS: committed async recovery survives abrupt termination; lease, project, draft and pending drawing verified.'
} finally {
    if ($producer -and !$producer.HasExited) { $producer.Kill(); $producer.WaitForExit() }
    if ($inspector -and !$inspector.HasExited) { $inspector.Kill(); $inspector.WaitForExit() }
    # Keep isolated artifacts; never delete user or reference data.
}
