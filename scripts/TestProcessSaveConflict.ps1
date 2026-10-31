[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dir = Join-Path (Join-Path $root 'work\process-save-tests') ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$probe = Join-Path $root 'tests\ProcessProbe\bin\Release\net10.0\ProcessProbe.dll'
if (!(Test-Path -LiteralPath $probe)) { throw 'Build tests/ProcessProbe/ProcessProbe.csproj in Release first.' }
$project = Join-Path $dir 'shared.tcad'
$gate = Join-Path $dir 'go'
$workers = @()
try {
    & $sdk $probe save-init $project
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize test project.' }
    foreach ($name in @('First', 'Second')) {
        $ready = Join-Path $dir "$name.ready"
        $result = Join-Path $dir "$name.result"
        $quoted = @($probe, 'save-race', $project, $name, $ready, $gate, $result) | ForEach-Object { '"' + $_ + '"' }
        $process = Start-Process -FilePath $sdk -ArgumentList $quoted -WindowStyle Hidden -PassThru
        $workers += [pscustomobject]@{ Name=$name; Ready=$ready; Result=$result; Process=$process }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (@($workers | Where-Object { !(Test-Path -LiteralPath $_.Ready) }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $deadline -or @($workers | Where-Object { $_.Process.HasExited }).Count -gt 0) { throw 'Both writers did not reach the snapshot barrier.' }
        Start-Sleep -Milliseconds 20
    }
    $hash1 = [IO.File]::ReadAllText($workers[0].Ready)
    $hash2 = [IO.File]::ReadAllText($workers[1].Ready)
    if ($hash1 -ne $hash2 -or $hash1 -ne (Get-FileHash -LiteralPath $project -Algorithm SHA256).Hash) { throw 'Writers did not load the same original version.' }
    [IO.File]::WriteAllText($gate, 'go')
    foreach ($worker in $workers) {
        if (!$worker.Process.WaitForExit(30000) -or $worker.Process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $worker.Result)) { throw "Writer $($worker.Name) failed or timed out." }
    }
    $results = @($workers | ForEach-Object { [IO.File]::ReadAllText($_.Result) })
    $saved = @($results | Where-Object { $_ -match '^saved\|(First|Second)$' })
    $conflicts = @($results | Where-Object { $_ -match '^conflict\|(First|Second)$' })
    if ($saved.Count -ne 1 -or $conflicts.Count -ne 1 -or $saved[0].Split('|')[1] -eq $conflicts[0].Split('|')[1]) { throw "Expected one saved writer and one conflict: $results" }
    $document = Get-Content -LiteralPath $project -Raw | ConvertFrom-Json
    if ($document.Name -ne $saved[0].Split('|')[1]) { throw 'The on-disk project does not contain the winning writer.' }
    Write-Output "PASS: independent processes loaded the same version; $($saved[0]); $($conflicts[0]); winning file retained."
} finally {
    foreach ($worker in $workers) { if (!$worker.Process.HasExited) { $worker.Process.Kill(); $worker.Process.WaitForExit() } }
    # Retain only this test's isolated artifacts for diagnosis.
}
