[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceArchive,
    [string]$RuntimeArchive,
    [switch]$BuildExtractedSource
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$requiredSource = @('TekstilCAD.slnx','global.json','AGENTS.md','src/TekstilCAD.Core/TekstilCAD.Core.csproj','src/TekstilCAD.Desktop/TekstilCAD.Desktop.csproj','src/TekstilCAD.FileFormats/TekstilCAD.FileFormats.csproj','src/TekstilCAD.Grading/TekstilCAD.Grading.csproj','src/TekstilCAD.Nesting/TekstilCAD.Nesting.csproj','src/TekstilCAD.Patterns/TekstilCAD.Patterns.csproj','src/TekstilCAD.Patterns/CLIPPER2-LICENSE.txt','src/TekstilCAD.Patterns/THIRD_PARTY_NOTICES.txt','src/TekstilCAD.MachineGateway/TekstilCAD.MachineGateway.csproj','src/TekstilCAD.Licensing/TekstilCAD.Licensing.csproj','tests/TekstilCAD.Tests/TekstilCAD.Tests.csproj')
function Test-Zip([string]$Path, [string[]]$Required, [bool]$IsSource) {
    $zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Path))
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $hashes = [ordered]@{}
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ($name.StartsWith('/') -or $name.Contains(':') -or ($name.Split('/') -contains '..') -or !$names.Add($name)) { throw "Unsafe or duplicated ZIP entry: $name" }
            if ($IsSource -and $name -match '(^|/)(\.tools|bin|obj|work|artifacts|\.git|node_modules|\.cache)(/|$)|(^|/)\.env($|\.)|\.(pfx|p12|pem|key|user|suo)$') { throw "Excluded source entry: $name" }
            if ($name.EndsWith('/')) { continue }
            $data = $entry.Open()
            try { $hashes[$name] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)) }
            finally { $data.Dispose() }
        }
        foreach ($item in $Required) { if (!$names.Contains($item)) { throw "Required archive member missing: $item" } }
        return [ordered]@{ archive=[IO.Path]::GetFileName($Path); files=$hashes.Count; members=$hashes }
    } finally { $zip.Dispose() }
}
$result = [ordered]@{ source=(Test-Zip $SourceArchive $requiredSource $true); cleanBuild='not requested' }
if ($RuntimeArchive) {
    $result.runtime = Test-Zip $RuntimeArchive @('TekstilCAD.Studio.exe','coreclr.dll','hostfxr.dll','PresentationFramework.dll','Clipper2Lib.dll','CLIPPER2-LICENSE.txt','THIRD_PARTY_NOTICES.txt') $false
    # Published notices must be the same files reviewed in the source archive.
    foreach ($notice in @('CLIPPER2-LICENSE.txt','THIRD_PARTY_NOTICES.txt')) {
        if ($result.runtime.members[$notice] -ne $result.source.members["src/TekstilCAD.Patterns/$notice"]) {
            throw "Runtime license notice differs from source archive: $notice"
        }
    }
}
if ($BuildExtractedSource) {
    $extraction = Join-Path $root ('work\archive-check-' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($SourceArchive),$extraction)
    $sdk = Join-Path $root '.tools\dotnet\dotnet.exe'
    if (!(Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
    & $sdk build (Join-Path $extraction 'TekstilCAD.slnx') -c Release --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Extracted source build failed.' }
    & $sdk test (Join-Path $extraction 'tests\TekstilCAD.Tests\TekstilCAD.Tests.csproj') -c Release --no-build --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Extracted source tests failed.' }
    $result.cleanBuild = 'build and tests passed'
    $result.extractedTo = $extraction
}
return $result
