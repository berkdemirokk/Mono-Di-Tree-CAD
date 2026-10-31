param([Parameter(Mandatory)][string]$ExecutablePath)
$ErrorActionPreference='Stop'
$exe = Get-Item -LiteralPath $ExecutablePath
if ($exe.Name -ne 'TekstilCAD.Studio.exe') { throw 'Expected TekstilCAD executable.' }
$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$shortcutPath = Join-Path $desktop 'TekstilCAD Studio 0.2.lnk'
if (Test-Path -LiteralPath $shortcutPath) { throw 'Existing shortcut preserved; choose a new name manually.' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe.FullName
$shortcut.WorkingDirectory = $exe.DirectoryName
$shortcut.IconLocation = $exe.FullName + ',0'
$shortcut.Description = 'TekstilCAD Studio 0.2 — Windows CAD önizlemesi'
$shortcut.Save()
Write-Output $shortcutPath
