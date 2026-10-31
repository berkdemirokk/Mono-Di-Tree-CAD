[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dir = Join-Path (Join-Path $root 'work\recovery-pre-rename-tests') ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
[IO.File]::WriteAllText((Join-Path $dir 'pre-rename-mode'), 'enabled')
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$desktop = Join-Path $root 'src\TekstilCAD.Desktop\bin\Release\net10.0-windows\TekstilCAD.Studio.dll'
if (!(Test-Path -LiteralPath $desktop)) { throw 'Build the desktop in Release first.' }
$producer = $null; $inspector = $null
try {
    $producer = Start-Process -FilePath $sdk -ArgumentList @('"' + $desktop + '"', '--verify-recovery-child', '"' + $dir + '"') -WindowStyle Hidden -PassThru
    $barrier = Join-Path $dir 'pre-rename-ready'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (!(Test-Path -LiteralPath $barrier) -and !$producer.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path -LiteralPath $barrier)) {
        $errorPath = Join-Path $dir 'child-error.txt'
        if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
        throw 'Child did not reach the flushed pre-rename boundary.'
    }
    $marker = [IO.File]::ReadAllText((Join-Path $dir 'ready.json')) | ConvertFrom-Json
    $recoveryDir = [IO.Path]::GetFullPath((Join-Path $dir 'appdata\Recovery'))
    $target = [IO.Path]::GetFullPath($marker.Path)
    if ([IO.Path]::GetDirectoryName($target) -ne $recoveryDir -or [IO.Path]::GetExtension($target) -ne '.tcr' -or !(Test-Path -LiteralPath $target)) { throw 'Invalid baseline path.' }
    $baselineHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    $tempFiles = @(Get-ChildItem -LiteralPath $recoveryDir -Filter (([IO.Path]::GetFileName($target)) + '.*.tmp') -File)
    if ($tempFiles.Count -ne 1 -or $tempFiles[0].Length -eq 0) { throw 'Expected one nonempty flushed temporary recovery file.' }
    $lockPath = [IO.Path]::ChangeExtension($target, '.lock')
    try { $lease = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lease.Dispose(); throw 'Live lease was not exclusive.' }
    catch [IO.IOException] { }
    $producer.Kill(); $producer.WaitForExit()
    $lease = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lease.Dispose()
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $baselineHash) { throw 'The baseline changed before the atomic rename.' }
    $inspector = Start-Process -FilePath $sdk -ArgumentList @('"' + $desktop + '"', '--verify-recovery-inspect', '"' + $dir + '"') -WindowStyle Hidden -PassThru
    if (!$inspector.WaitForExit(30000)) { throw 'Hidden inspection timed out.' }
    if ($inspector.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $dir 'inspection.txt'))) {
        $errorPath = Join-Path $dir 'inspection-error.txt'
        if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
        throw 'Baseline recovery inspection failed.'
    }
    Write-Output 'PASS: flushed second recovery interrupted before rename; original project, draft and pending drawing remain intact.'
} finally {
    if ($producer -and !$producer.HasExited) { $producer.Kill(); $producer.WaitForExit() }
    if ($inspector -and !$inspector.HasExited) { $inspector.Kill(); $inspector.WaitForExit() }
    # Deliberately retain isolated files, including any interrupted temporary file.
}
