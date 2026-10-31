[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dir = Join-Path (Join-Path $root 'work\plot-export-conflict-tests') ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
$sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$probe = Join-Path $root 'tests\ProcessProbe\bin\Release\net10.0\ProcessProbe.dll'
if (!(Test-Path -LiteralPath $probe)) { throw 'Build tests/ProcessProbe/ProcessProbe.csproj in Release first.' }
foreach ($format in @('svg','hpgl')) {
    $extension = if ($format -eq 'svg') { '.svg' } else { '.plt' }
    $target = Join-Path $dir ('shared' + $extension)
    $gate = Join-Path $dir ($format + '.go')
    $workers = @()
    try {
        [IO.File]::WriteAllText($target, 'initial content', [Text.UTF8Encoding]::new($false))
        foreach ($name in @('First','Second')) {
            $ready = Join-Path $dir "$format-$name.ready"
            $result = Join-Path $dir "$format-$name.result"
            $quoted = @($probe, 'plot-export-race', $format, $target, $name, $ready, $gate, $result) | ForEach-Object { '"' + $_ + '"' }
            $process = Start-Process -FilePath $sdk -ArgumentList $quoted -WindowStyle Hidden -PassThru
            $workers += [pscustomobject]@{ Name=$name; Ready=$ready; Result=$result; Process=$process }
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (@($workers | Where-Object { !(Test-Path -LiteralPath $_.Ready) }).Count -gt 0) {
            if ([DateTime]::UtcNow -gt $deadline -or @($workers | Where-Object { $_.Process.HasExited }).Count -gt 0) { throw "$format exporters did not reach the barrier." }
            Start-Sleep -Milliseconds 20
        }
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ([IO.File]::ReadAllText($workers[0].Ready) -ne $hash -or [IO.File]::ReadAllText($workers[1].Ready) -ne $hash) { throw "$format exporters captured different destination versions." }
        [IO.File]::WriteAllText($gate, 'go')
        foreach ($worker in $workers) {
            if (!$worker.Process.WaitForExit(30000) -or $worker.Process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $worker.Result)) { throw "$format exporter $($worker.Name) failed or timed out." }
        }
        $results = @($workers | ForEach-Object { [IO.File]::ReadAllText($_.Result) })
        $saved = @($results | Where-Object { $_ -match '^saved\|(First|Second)$' })
        $conflicts = @($results | Where-Object { $_ -match '^conflict\|(First|Second)$' })
        if ($saved.Count -ne 1 -or $conflicts.Count -ne 1 -or $saved[0].Split('|')[1] -eq $conflicts[0].Split('|')[1]) { throw "Expected one successful $format export and one conflict: $results" }
        $winner = $saved[0].Split('|')[1]
        $contents = [IO.File]::ReadAllText($target)
        if ($format -eq 'svg') {
            [xml]$xml = $contents
            $expected = if ($winner -eq 'First') { '10' } else { '20' }
            $path = $xml.SelectSingleNode("//*[local-name()='path']")
            $title = $xml.SelectSingleNode("//*[local-name()='title']")
            if ($null -eq $path -or $path.GetAttribute('d') -ne "M 0 0 L $expected 0" -or $null -eq $title -or $title.InnerText -ne $winner) { throw 'Winning SVG geometry or title was not retained.' }
        } else {
            $expected = if ($winner -eq 'First') { '400' } else { '800' }
            if ($contents -notmatch "(?m)^IN;SP1;PA;\r?$" -or $contents -notmatch "(?m)^PU0,0;PD$expected,0;PU;\r?$" -or $contents -notmatch "(?m)^SP0;\r?$") { throw 'Winning HPGL geometry was not retained.' }
        }
        Write-Output "PASS: $format independent exporters captured one version; $($saved[0]); $($conflicts[0]); winner content verified."
    } finally {
        foreach ($worker in $workers) { if (!$worker.Process.HasExited) { $worker.Process.Kill(); $worker.Process.WaitForExit() } }
        # Retain isolated test artifacts; no hardware is accessed.
    }
}
