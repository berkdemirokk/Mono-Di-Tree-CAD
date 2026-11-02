using TekstilCAD.Core;
using TekstilCAD.FileFormats;

// Writes one deterministic synthetic job only to a new local file.
if(args.Length!=1)throw new ArgumentException("Expected a new output file path.");
var rectangle=new PatternPiece { Name="Synthetic rectangle",Closed=true,
    Points=[new(-12.5,-2.5),new(-2.5,-2.5),new(-2.5,7.5),new(-12.5,7.5)] };
var line=new PatternPiece { Name="Synthetic long line",Closed=false };
for(var i=0;i<100;i++)line.Points.Add(new(20+i*.25,10+(i%2)*.25));
var project=new ProjectDocument { Pieces=[rectangle,line] };
var prepared=HpglFormat.Prepare(project,40);
var path=Path.GetFullPath(args[0]);
ProjectStorage.AtomicWriteIfUnchanged(path,prepared.Text,new ProjectFileVersion(path,null));
