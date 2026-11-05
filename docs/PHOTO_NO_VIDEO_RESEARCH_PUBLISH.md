# Experimental x64 photo research publish

The opt-in `PhotoResearchNoVideo` profiles publish a self-contained Windows x64 desktop and, separately, the local photo-region prototype. The shared target removes only the resolved publish item named `opencv_videoio_ffmpeg4100_64.dll` after the SDK computes publish items; it neither deletes existing files nor modifies normal builds or other profiles. The target stops if the requested runtime/self-contained settings do not match, single-file publishing is enabled, or that exact resolved item is absent. Other native binaries are retained.

Use **new empty output directories**. A publish into an existing directory can retain old files, and this profile intentionally does not delete them. The example assumes the repository root, an installed .NET SDK and already available packages. It executes only when a developer runs it:

```powershell
$photoDesktop = 'C:\research-output\desktop-no-video'
$photoPrototype = 'C:\research-output\prototype-no-video'
if ((Test-Path -LiteralPath $photoDesktop) -or (Test-Path -LiteralPath $photoPrototype)) { throw 'Choose new output directories.' }
dotnet publish src/TekstilCAD.Desktop/TekstilCAD.Desktop.csproj -p:PublishProfile=PhotoResearchNoVideo -o $photoDesktop
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
dotnet publish tools/PhotoRegionSharpPrototype/PhotoRegionSharpPrototype.csproj -p:PublishProfile=PhotoResearchNoVideo -o $photoPrototype
if ($LASTEXITCODE -ne 0) { throw 'Prototype publish failed.' }
foreach ($folder in @($photoDesktop,$photoPrototype)) {
    if (@(Get-ChildItem -LiteralPath $folder -Recurse -File -Filter 'opencv_videoio_ffmpeg4100_64.dll' -Force).Count -ne 0) { throw 'Unexpected video DLL in output or a subdirectory.' }
    if (-not (Test-Path -LiteralPath (Join-Path $folder 'OpenCvSharpExtern.dll'))) { throw 'OpenCvSharpExtern missing.' }
}
& (Join-Path $photoPrototype 'PhotoRegionSharpPrototype.exe') --selftest
if ($LASTEXITCODE -ne 0) { throw 'Prototype self-test failed.' }
$desktopExe = Join-Path $photoDesktop 'TekstilCAD.Studio.exe'
foreach ($check in @(
    @('--verify-chat-model','chat-review.json'),
    @('--verify-aline','aline-review.json'),
    @('--verify','desktop-review.json')
)) {
    $reportPath = Join-Path $photoDesktop $check[1]
    $process = Start-Process -FilePath $desktopExe -ArgumentList @($check[0], ('"' + $reportPath + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Desktop verification failed: $($check[0])." }
}
```

Each executable invocation is a separate process. Inspect the reports and actual loaded files as well as exit codes; no result is implied by this document. The prototype synthetic self-test and desktop verification do not establish video support, camera support, real-image segmentation accuracy or physical manufacturing suitability. A clean-machine, offline deployment and other architectures are separate work.

The two NuGet package metadata records declare Apache-2.0, but the provenance, exact build options, incorporated components, and required license/NOTICE texts for the actual OpenCV/OpenCvSharp, FFmpeg, .NET and other native/runtime binaries must be independently verified before redistribution. Excluding a separate video DLL does not establish which components are embedded in `OpenCvSharpExtern.dll` or make a licensing claim. See [local evidence](PHOTO_GRABCUT_SHARP_PROTOTYPE.md).
