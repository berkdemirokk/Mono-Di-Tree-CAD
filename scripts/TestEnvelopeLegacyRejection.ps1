param(
    [Parameter(Mandatory=$true)][string]$Dotnet,
    [Parameter(Mandatory=$true)][string]$PhysicalReader,
    [Parameter(Mandatory=$true)][string]$PhotoReader
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workRoot=Join-Path $repoRoot 'work'
[IO.Directory]::CreateDirectory($workRoot) | Out-Null
$runId=[guid]::NewGuid().ToString('N')
$runRoot=Join-Path $workRoot ('envelope-shape-'+$runId)
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
# This UUID is a test fixture, not a reserved or approved format identifier.
$marker='urn:uuid:c8b151bf-c3fe-41d9-b5f9-2ae5ebf3c1e1'
$body='{"Name":"synthetic","Pieces":[{"Id":"11111111-1111-4111-8111-111111111111","Name":"piece","Closed":true,"Points":[{"X":0,"Y":0},{"X":10,"Y":0},{"X":0,"Y":10}]}],"GradingRules":[]}'
$cases=@(
    @{ Name='legacy-v1'; Text=('[1,'+$body+']'); Accept=$true },
    @{ Name='legacy-object'; Text=$body; Accept=$true },
    @{ Name='legacy-object-with-unknown-marker'; Text=($body.Substring(0,$body.Length-1)+',"vendor":{"marker":"'+$marker+'"}}'); Accept=$false },
    @{ Name='three-element-namespaced'; Text=('["'+$marker+'",1,'+$body+']'); Accept=$false },
    @{ Name='two-element-string-marker'; Text=('["'+$marker+'",'+$body+']'); Accept=$false },
    @{ Name='nested-wrapper-object'; Text=('{"format":"'+$marker+'","project":'+$body+'}'); Accept=$false },
    @{ Name='unknown-numeric-ten'; Text=('[10,'+$body+']'); Accept=$false }
)
$readerItems=@(@{ Name='physical'; Path=[IO.Path]::GetFullPath($PhysicalReader) },@{ Name='photo'; Path=[IO.Path]::GetFullPath($PhotoReader) })
$results=[Collections.Generic.List[object]]::new()
$failed=$false
foreach($item in $readerItems) {
    if(-not [IO.File]::Exists($item.Path)) { throw "Historical reader missing: $($item.Name)" }
    $readerHash=(Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash
    foreach($case in $cases) {
        $fixture=Join-Path $runRoot ($item.Name+'-'+$case.Name+'.json')
        [IO.File]::WriteAllText($fixture,$case.Text,$utf8)
        $before=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
        $output=@(& $Dotnet $item.Path $fixture 2>&1);$exit=$LASTEXITCODE
        $after=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
        $correct=($exit -eq $(if($case.Accept){0}else{2})) -and ($before -eq $after)
        if(-not $correct) { $failed=$true }
        $results.Add([pscustomobject]@{ reader=$item.Name;readerPath=$item.Path;readerSha256=$readerHash;fixture=$case.Name;
            fixtureSha256=$before;expectedAccept=$case.Accept;exitCode=$exit;sourceUnchanged=($before -eq $after);correct=$correct;output=($output -join "`n") })
    }
    if((Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash -ne $readerHash) { $failed=$true }
}
$report=Join-Path $runRoot 'report.json'
[IO.File]::WriteAllText($report,([pscustomobject]@{runId=$runId;fixtureIdentifier=$marker;status=$(if($failed){'FAIL'}else{'PASS'});results=@($results)} | ConvertTo-Json -Depth 6),$utf8)
Write-Output "Report: $report"
Write-Output "Status: $(if($failed){'FAIL'}else{'PASS'}); reader cases: $($results.Count)"
if($failed) { exit 1 }
