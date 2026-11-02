using System.Text.Json;
using TekstilCAD.Core;

if(args.Length!=2)throw new ArgumentException("Expected input and output JSON paths.");
var jobs=JsonSerializer.Deserialize<List<Job>>(File.ReadAllText(args[0])) ?? throw new InvalidDataException();
var output=new List<Result>();
foreach(var job in jobs)
{
    try
    {
        var corners=job.Corners.Select(p=>new Point2(p[0],p[1])).ToArray();
        var samples=job.Samples.Select(p=>new Point2(p[0],p[1])).ToArray();
        var plane=PhotoPlaneRectification.Convert(job.Width,job.Height,corners,8,5,samples);
        var single=PhotoTraceCalibration.Convert(job.Width,job.Height,corners[0],corners[1],8,samples);
        output.Add(new Result(job.Name,null,plane.Select(p=>new[]{p.X,p.Y}).ToArray(),single.Select(p=>new[]{p.X,p.Y}).ToArray()));
    }
    catch(Exception e) when(e is ArgumentException or InvalidOperationException or OverflowException)
    { output.Add(new Result(job.Name,e.Message,[],[])); }
}
File.WriteAllText(args[1],JsonSerializer.Serialize(output));
record Job(string Name,int Width,int Height,double[][] Corners,double[][] Samples);
record Result(string Name,string? Error,double[][] Plane,double[][] Single);
