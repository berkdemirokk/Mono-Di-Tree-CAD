[CmdletBinding()]
param(
    [string]$OutputDirectory = 'C:\Users\pc\Documents\Codex\2026-10-31\a-2\outputs'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$allowedOutputs = 'C:\Users\pc\Documents\Codex\2026-10-31\a-2\outputs'
if ($destination -ne $allowedOutputs -and !$destination.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package destination must be the designated outputs folder or a project subfolder.'
}
Push-Location $root
try {
    $changes = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Git status failed.' }
    if ($changes.Count -gt 0) { throw 'Commit all source changes before packaging; archives must match HEAD.' }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'A committed source checkpoint is required.' }
    $stamp = (& git show -s --format=%cI HEAD).Trim()
    $version = $commit.Substring(0,12)
    $tracked = @(& git ls-tree -r --name-only HEAD)
    $forbidden = '(^|/)(\.tools|bin|obj|work|artifacts|\.git|node_modules|\.cache)(/|$)|(^|/)\.env($|\.)|\.(pfx|p12|pem|key|user|suo)$'
    if ($tracked | Where-Object { $_ -match $forbidden }) { throw 'Tracked source contains excluded build, cache or credential files.' }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $sourceZip = Join-Path $destination "TekstilCAD-source-$version.zip"
    $runtimeZip = Join-Path $destination "TekstilCAD-Windows-x64-$version.zip"
    $manifestPath = Join-Path $destination "TekstilCAD-manifest-$version.json"
    foreach ($target in @($sourceZip,$runtimeZip,$manifestPath)) { if (Test-Path -LiteralPath $target) { throw "Output already exists: $target" } }
    & git archive --format=zip --output=$sourceZip HEAD
    if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
    $sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
    if (!(Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    # Always publish from this checkout. Reusing an arbitrary directory could
    # otherwise label binaries from a different revision as this Git commit.
    $PublishedDirectory = Join-Path $root ('work\package-' + [Guid]::NewGuid().ToString('N'))
    & $sdk publish (Join-Path $root 'src\TekstilCAD.Desktop\TekstilCAD.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $PublishedDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed. Source archive retained for inspection.' }
    $changesAfterPublish = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $changesAfterPublish.Count -gt 0 -or (& git rev-parse HEAD).Trim() -ne $commit) {
        throw 'Source changed during publish; package provenance cannot be established. Source archive retained for inspection.'
    }
    $PublishedDirectory = [IO.Path]::GetFullPath($PublishedDirectory)
    if (!$PublishedDirectory.StartsWith($root + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Published files must be inside project workspace.' }
    foreach ($required in @('TekstilCAD.Studio.exe','coreclr.dll','hostfxr.dll','PresentationFramework.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $PublishedDirectory $required))) { throw "Self-contained output is missing $required" }
    }
    Add-Type -AssemblyName System.IO.Compression
    $fixedTime = [DateTimeOffset]::Parse($stamp)
    if ($fixedTime.Year -lt 1980 -or $fixedTime.Year -gt 2107) { $fixedTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero) }
    $stream = [IO.File]::Open($runtimeZip,[IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach ($file in (Get-ChildItem -LiteralPath $PublishedDirectory -File -Recurse | Sort-Object FullName)) {
                if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Publish contains linked files.' }
                $name = [IO.Path]::GetRelativePath($PublishedDirectory,$file.FullName).Replace('\','/')
                $entry = $zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTime
                $inputStream = $file.OpenRead(); $outputStream = $entry.Open()
                try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
    $verification = & (Join-Path $PSScriptRoot 'VerifyArchive.ps1') -SourceArchive $sourceZip -RuntimeArchive $runtimeZip
    $manifest = [ordered]@{
        commit = $commit; commitTime = $stamp; target = 'win-x64'; selfContained = $true
        dataLocation = '%LOCALAPPDATA%\TekstilCAD\Studio'; note = 'Application data is separate from source and installation. Physical machine compatibility is unverified.'
        archives = @(@{name=[IO.Path]::GetFileName($sourceZip);sha256=(Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash},@{name=[IO.Path]::GetFileName($runtimeZip);sha256=(Get-FileHash -LiteralPath $runtimeZip -Algorithm SHA256).Hash})
        verification = $verification
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Output $sourceZip
    Write-Output $runtimeZip
    Write-Output $manifestPath
} finally { Pop-Location }

