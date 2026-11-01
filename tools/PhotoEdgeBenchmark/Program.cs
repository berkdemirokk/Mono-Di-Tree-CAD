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
    var image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
    image.Freeze();
    foreach(var job in group)
    {
        var ok=PhotoEdgeSuggestion.TryFind(image,new Point(job.X,job.Y),out var p);
        output.Add(new Result(job.Id,ok,ok ? p.X : null,ok ? p.Y : null));
    }
}
File.WriteAllText(args[1],JsonSerializer.Serialize(output));
record Job(int Id,string Image,double X,double Y);
record Result(int Id,bool Found,double? X,double? Y);
