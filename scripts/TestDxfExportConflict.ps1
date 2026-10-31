[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dir = Join-Path (Join-Path $root 'work\dxf-export-conflict-tests') ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$probe = Join-Path $root 'tests\ProcessProbe\bin\Release\net10.0\ProcessProbe.dll'
if (!(Test-Path -LiteralPath $probe)) { throw 'Build tests/ProcessProbe/ProcessProbe.csproj in Release first.' }
$target = Join-Path $dir 'shared.dxf'
$gate = Join-Path $dir 'go'
$workers = @()
try {
    # A valid, existing destination ensures both writers use the same content token.
    [IO.File]::WriteAllText($target, "0`nSECTION`n2`nENTITIES`n0`nLINE`n10`n0`n20`n0`n11`n5`n21`n0`n0`nENDSEC`n0`nEOF`n", [Text.UTF8Encoding]::new($false))
    foreach ($name in @('First', 'Second')) {
        $ready = Join-Path $dir "$name.ready"
        $result = Join-Path $dir "$name.result"
        $quoted = @($probe, 'dxf-export-race', $target, $name, $ready, $gate, $result) | ForEach-Object { '"' + $_ + '"' }
        $process = Start-Process -FilePath $sdk -ArgumentList $quoted -WindowStyle Hidden -PassThru
        $workers += [pscustomobject]@{ Name=$name; Ready=$ready; Result=$result; Process=$process }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (@($workers | Where-Object { !(Test-Path -LiteralPath $_.Ready) }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $deadline -or @($workers | Where-Object { $_.Process.HasExited }).Count -gt 0) { throw 'Both exporters did not reach the snapshot barrier.' }
        Start-Sleep -Milliseconds 20
    }
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ([IO.File]::ReadAllText($workers[0].Ready) -ne $hash -or [IO.File]::ReadAllText($workers[1].Ready) -ne $hash) { throw 'Exporters did not capture the same destination version.' }
    [IO.File]::WriteAllText($gate, 'go')
    foreach ($worker in $workers) {
        if (!$worker.Process.WaitForExit(30000) -or $worker.Process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $worker.Result)) { throw "Exporter $($worker.Name) failed or timed out." }
    }
    $results = @($workers | ForEach-Object { [IO.File]::ReadAllText($_.Result) })
    $saved = @($results | Where-Object { $_ -match '^saved\|(First|Second)$' })
    $conflicts = @($results | Where-Object { $_ -match '^conflict\|(First|Second)$' })
    if ($saved.Count -ne 1 -or $conflicts.Count -ne 1 -or $saved[0].Split('|')[1] -eq $conflicts[0].Split('|')[1]) { throw "Expected one saved exporter and one conflict: $results" }
    $winner = $saved[0].Split('|')[1]
    $contents = [IO.File]::ReadAllText($target)
    $expected = if ($winner -eq 'First') { '10' } else { '20' }
    if ($contents -notmatch "(?m)^8\r?\n$winner\r?$" -or $contents -notmatch "(?m)^10\r?\n$expected\r?$") { throw 'Winning DXF geometry or layer was not retained.' }
    & $sdk $probe dxf-import $target | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Winning DXF failed re-import.' }
    Write-Output "PASS: independent conditional exporters captured one version; $($saved[0]); $($conflicts[0]); winner remains readable."
} finally {
    foreach ($worker in $workers) { if (!$worker.Process.HasExited) { $worker.Process.Kill(); $worker.Process.WaitForExit() } }
    # Retain only isolated test artifacts for diagnosis.
}
