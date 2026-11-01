using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using TekstilCAD.Desktop;

if(args.Length!=2)throw new ArgumentException("Expected input and output JSON paths.");
var jobs=JsonSerializer.Deserialize<List<Job>>(File.ReadAllText(args[0])) ?? throw new InvalidDataException();
var output=new List<Result>();
foreach(var group in jobs.GroupBy(x=>x.Image))
{
    using var stream=File.OpenRead(group.Key);
    var image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);image.Freeze();
    foreach(var job in group)
    {
        var ok=PhotoEdgePath.TryFind(image,new Point(job.X1,job.Y1),new Point(job.X2,job.Y2),out var path);
        output.Add(new Result(job.Id,ok,path.Select(p=>new[]{p.X,p.Y}).ToArray()));
    }
}
File.WriteAllText(args[1],JsonSerializer.Serialize(output));
record Job(int Id,string Image,double X1,double Y1,double X2,double Y2);
record Result(int Id,bool Found,double[][] Path);
