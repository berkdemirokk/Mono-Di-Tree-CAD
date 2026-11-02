using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;
using TekstilCAD.Desktop;

// Reads 80 contour jobs from contour_benchmark.py; never shows a window or
// opens a device. Uses production canvas and pixel validators, not an imitation.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Length!=3)throw new ArgumentException("Expected jobs.json results.json new-output.json.");
        var jobs=JsonSerializer.Deserialize<Job[]>(File.ReadAllText(args[0])) ?? throw new InvalidDataException();
        var paths=JsonSerializer.Deserialize<PathResult[]>(File.ReadAllText(args[1])) ?? throw new InvalidDataException();
        var indexed=paths.ToDictionary(p=>p.Id);
        if(jobs.Length%80!=0 || indexed.Count!=jobs.Length)throw new InvalidDataException("Expected 80 equally grouped contour jobs.");
        int anchorCount=jobs.Length/80;
        if(anchorCount<3 || anchorCount>17)throw new InvalidDataException("Expected 3 to 17 anchors per contour.");
        var output=new List<Report>();
        for(int ordinal=0;ordinal<80;ordinal++)
        {
            var group=jobs.Skip(ordinal*anchorCount).Take(anchorCount).ToArray();
            if(group.Where((job,i)=>job.Id!=ordinal*anchorCount+i || job.Image!=group[0].Image).Any())
                throw new InvalidDataException("Unexpected job grouping.");
            var anchors=group.Select(job=>new Point(job.X1,job.Y1)).ToArray();
            if(group.Where((job,i)=>job.X2!=anchors[(i+1)%anchorCount].X || job.Y2!=anchors[(i+1)%anchorCount].Y).Any())
                throw new InvalidDataException("Segments do not form the expected anchor loop.");
            var candidates=group.Select(job=>indexed[job.Id]).ToArray();
            if(candidates.Any(p=>!p.Found || p.Path.Any(point=>point.Length!=2)))
            { output.Add(new(ordinal,false,false,null,null));continue; }
            using var stream=File.OpenRead(group[0].Image);
            var image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);image.Freeze();
            var canvas=new PhotoTraceCanvas { Image=image };
            if(!canvas.TryAddPixelPoint(new Point(0,0)) || !canvas.TryAddPixelPoint(new Point(1,0)) ||
                anchors.Any(p=>!canvas.TryAddPixelPoint(p)))
            { output.Add(new(ordinal,false,false,null,null));continue; }
            var candidatePaths=candidates.Select(p=>(IReadOnlyList<Point>)p.Path.Select(q=>new Point(q[0],q[1])).ToArray()).ToArray();
            if(!canvas.SetAllPathSuggestions(anchors,candidatePaths,includeClosing:true))
            { output.Add(new(ordinal,true,false,null,null));continue; }
            var proposal=canvas.ProposedTraceSnapshot() ?? throw new InvalidDataException("Proposal disappeared.");
            var geometry=proposal.Select(p=>new Point2(p.X,p.Y)).ToArray();
            bool valid;
            try { LargeOpenPathValidator.Validate(geometry);LargeContourValidator.Validate(geometry);valid=true; }
            catch(ArgumentException) { valid=false; }
            var edges=valid ? null : PhotoPathCrossingDiagnostic.Find(proposal,true,CancellationToken.None);
            output.Add(new(ordinal,true,true,valid,edges is not null));
        }
        using var file=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(file,output,new JsonSerializerOptions { WriteIndented=true });
    }
    private sealed record Job(int Id,string Image,double X1,double Y1,double X2,double Y2);
    private sealed record PathResult(int Id,bool Found,double[][] Path);
    private sealed record Report(int Ordinal,bool PointsAccepted,bool ProposalAccepted,bool? PixelGeometryValid,bool? CrossingPairFound);
}
