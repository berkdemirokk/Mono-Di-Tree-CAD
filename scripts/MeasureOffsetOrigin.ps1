param([Parameter(Mandatory=$true)][string]$ClipperAssembly)
Add-Type -Path $ClipperAssembly
function Inflate($path,[double]$delta) {
    $paths=[Clipper2Lib.Paths64]::new();$paths.Add($path)
    return ,([Clipper2Lib.Clipper]::InflatePaths($paths,$delta,[Clipper2Lib.JoinType]::Miter,[Clipper2Lib.EndType]::Polygon,2.0,0.0))
}
function MaxSample($src,$target) {
    [double]$max=0
    for([int]$i=0;$i -lt $src.Count;$i++) {
        $a=$src[$i];$b=$src[($i+1)%$src.Count]
        for([int]$k=0;$k -lt 2;$k++) {
            [double]$px=($b.X-$a.X)*$k*.5;[double]$py=($b.Y-$a.Y)*$k*.5;[double]$best=[double]::PositiveInfinity
            for([int]$j=0;$j -lt $target.Count;$j++) {
                $c=$target[$j];$d=$target[($j+1)%$target.Count]
                [double]$cx=$c.X-$a.X;[double]$cy=$c.Y-$a.Y;[double]$dx=$d.X-$c.X;[double]$dy=$d.Y-$c.Y
                [double]$den=$dx*$dx+$dy*$dy;[double]$q=if($den -eq 0){0}else{[Math]::Clamp((($px-$cx)*$dx+($py-$cy)*$dy)/$den,0.,1.)}
                [double]$ex=$px-$cx-$q*$dx;[double]$ey=$py-$cy-$q*$dy;[double]$v=$ex*$ex+$ey*$ey
                if($v -lt $best){$best=$v}
            }
            if($best -gt $max){$max=$best}
        }
    }
    return [Math]::Sqrt($max)
}
foreach($shift in @(0L,50000000000000L)) {
  foreach($reverse in @($false,$true)) {
    $source=[Clipper2Lib.Path64]::new()
    for($i=0;$i -lt 512;$i++) { $a=[Math]::Tau*$i/512;$r=1000+200*[Math]::Cos(3*$a);$x=[long][Math]::Round($r*[Math]::Cos($a)*1000000,[MidpointRounding]::AwayFromZero)+$shift;$y=[long][Math]::Round($r*[Math]::Sin($a)*1000000,[MidpointRounding]::AwayFromZero)+$shift;$source.Add([Clipper2Lib.Point64]::new($x,$y)) }
    if($reverse){$source.Reverse()}
    $origin=$source[0]
    foreach($mode in @('global','local')) {
      $dx=if($mode -eq 'local'){$origin.X}else{0L};$dy=if($mode -eq 'local'){$origin.Y}else{0L}
      $path=[Clipper2Lib.Path64]::new();foreach($p in $source){$path.Add([Clipper2Lib.Point64]::new(($p.X-$dx),($p.Y-$dy)))}
      $f=Inflate $path 1000000;if($f.Count -ne 1){"shift=$shift reverse=$reverse mode=$mode forwardpaths=$($f.Count)";continue}
      $back=Inflate $f[0] -1000000;if($back.Count -ne 1){"shift=$shift reverse=$reverse mode=$mode inversepaths=$($back.Count)";continue}
      $m1=MaxSample $path $back[0];$m2=MaxSample $back[0] $path
      "shift=$shift reverse=$reverse mode=$mode forward=$($f[0].Count) inverse=$($back[0].Count) source_to_inverse_grid=$m1 inverse_to_source_grid=$m2 strict8=$([Math]::Max($m1,$m2) -le 8)"
    }
  }
}
