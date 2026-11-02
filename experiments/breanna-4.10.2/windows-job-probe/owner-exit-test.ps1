param([Parameter(Mandatory=$true)][string]$Dotnet,[Parameter(Mandatory=$true)][string]$Node,[ValidateSet("ownerexit","ownerterminate")][string]$Mode="ownerexit")
$ErrorActionPreference='Stop'
$here=$PSScriptRoot
$assembly=Join-Path $here 'bin\Debug\net10.0\JobProbe.dll'
$worker=Join-Path $here 'child-worker.mjs'
$start=[System.Diagnostics.ProcessStartInfo]::new($Dotnet)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardError=$true
foreach($arg in @($assembly,$Node,$worker,$Mode)){[void]$start.ArgumentList.Add($arg)}
$owner=[System.Diagnostics.Process]::new();$owner.StartInfo=$start
if(!$owner.Start()){throw 'Owner failed to start'}
try {
 $read=$owner.StandardOutput.ReadLineAsync();if(!$read.Wait(10000)){throw 'Owner sentinel timeout'}
 $line=$read.Result;if($line -notmatch '^OWNERCHILD=([0-9]+)$'){throw 'Missing synthetic sentinel'}
 $child=[System.Diagnostics.Process]::GetProcessById([int]$Matches[1])
 try {
  if($child.HasExited){throw 'Child exited before owner release'}
  $owner.StandardInput.WriteLine('EXIT');$owner.StandardInput.Flush()
  if(!$owner.WaitForExit(5000)){throw 'Owner exit timeout'}
  if($owner.ExitCode -ne 88){throw 'Unexpected owner exit'}
  if(!$child.WaitForExit(5000)){throw 'Child survived owner exit'}
  "PASS: synthetic child alive before $Mode, terminated after owner process exit."
 } finally {$child.Dispose()}
} finally {if(!$owner.HasExited){$owner.Kill($true);$owner.WaitForExit(5000)|Out-Null};$owner.Dispose()}
