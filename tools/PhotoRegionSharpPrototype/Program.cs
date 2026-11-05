using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoRegionSharpPrototype;
using TekstilCAD.Core;

if(args.Length==3 && (args[0]=="--batch" || args[0]=="--components-batch"))
{
    if(File.Exists(args[2]))throw new IOException("Çıktı zaten var.");
    var jobInfo=new FileInfo(args[1]);if(jobInfo.Length>2_000_000)throw new InvalidDataException("İş dosyası boyut sınırını aşıyor.");
    var jobBytes=File.ReadAllBytes(args[1]);
    if(jobBytes.Length>2_000_000)throw new InvalidDataException("İş dosyası boyut sınırını aşıyor.");
    var jobs=JsonSerializer.Deserialize<BatchJob[]>(jobBytes) ?? throw new InvalidDataException();
    if(jobs.Length is <1 or >1000)throw new InvalidDataException("İş sayısı geçersiz.");
    var rows=new List<BatchResult>();var componentRows=new List<ComponentBatchResult>();
    foreach(var job in jobs)
    {
        var source=File.ReadAllBytes(job.Image);
        if(Convert.ToHexString(SHA256.HashData(source))!=job.ImageSha256)throw new InvalidDataException("Görsel özeti eşleşmiyor.");
        using var bitmap=Cv2.ImDecode(source,ImreadModes.Color);
        if(bitmap.Empty() || !bitmap.IsContinuous())throw new InvalidDataException("Görsel çözülemedi.");
        var bgr=new byte[checked(bitmap.Width*bitmap.Height*3)];Marshal.Copy(bitmap.Data,bgr,0,bgr.Length);
        var proposal=RegionProposal.Start(bgr,bitmap.Width,bitmap.Height,new Rect(job.X,job.Y,job.Width,job.Height),seed:job.Seed);
        var binary=proposal.CopyBinaryMask();var digest=Convert.ToHexString(SHA256.HashData(binary));
        if(args[0]=="--batch")rows.Add(new BatchResult(job.AnnotationId,digest));
        else
        {
            var components=MaskComponents.Analyze(binary,bitmap.Width,bitmap.Height);
            var largest=components.Components.OrderByDescending(c=>c.Pixels).ThenBy(c=>c.Id).FirstOrDefault();
            var selected=largest.Id==0 ? components.CopySelection([]) : components.CopySelection([largest.Id]);
            var batchContours=MaskContours.Extract(binary,bitmap.Width,bitmap.Height);
            var mainLoop=batchContours.Loops.Where(l=>!l.IsHole).OrderByDescending(l=>l.Count).FirstOrDefault();
            var reduced=mainLoop is null ? [] : ContourCollinearReduction.Reduce(mainLoop.CopyPoints());
            var reducedPoints=reduced.Select(p=>new Point2(p.X,p.Y)).ToArray();
            static bool ValidTopology(Point2[] points)
            {
                try { LargeOpenPathValidator.Validate(points);LargeContourValidator.Validate(points);return true; }
                catch(ArgumentException) { return false; }
            }
            var topologyValid=ValidTopology(reducedPoints);
            var rawPoints=mainLoop is null ? [] : mainLoop.CopyPoints().Select(p=>new Point2(p.X,p.Y)).ToArray();
            var rawTopologyValid=ValidTopology(rawPoints);
            componentRows.Add(new ComponentBatchResult(job.AnnotationId,digest,components.Components.Count,
                components.Components.Sum(c=>c.Pixels),largest.Pixels,selected.Count(x=>x==1),
                Convert.ToHexString(SHA256.HashData(selected)),batchContours.Loops.Count(l=>!l.IsHole),
                batchContours.Loops.Count(l=>l.IsHole),batchContours.Loops.Sum(l=>l.Count),
                batchContours.Loops.Where(l=>!l.IsHole).Select(l=>l.Count).DefaultIfEmpty(0).Max(),
                reduced.Length,rawTopologyValid,topologyValid));
        }
    }
    using var file=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write,FileShare.None);
    var jobHash=Convert.ToHexString(SHA256.HashData(jobBytes));var version=Cv2.GetVersionString() ?? throw new InvalidDataException("OpenCV sürümü okunamadı.");
    if(args[0]=="--batch")JsonSerializer.Serialize(file,new BatchReport(1,jobHash,version,rows.ToArray()),new JsonSerializerOptions { WriteIndented=true });
    else JsonSerializer.Serialize(file,new ComponentBatchReport(1,jobHash,version,componentRows.ToArray()),new JsonSerializerOptions { WriteIndented=true });
    Console.WriteLine($"Completed {jobs.Length} masks; no image or mask files written.");return;
}
if(args.Length!=1 || args[0]!="--selftest")throw new ArgumentException("Use --selftest.");
const int w=96,h=72;var pixels=new byte[w*h*3];
for(int y=0;y<h;y++)for(int x=0;x<w;x++)
{
    var offset=(y*w+x)*3;var inside=x>=25 && x<70 && y>=20 && y<55;
    pixels[offset]=(byte)(inside ? 20 : 35);pixels[offset+1]=(byte)(inside ? 30 : 55);pixels[offset+2]=(byte)(inside ? 220 : 75);
}
var original=(byte[])pixels.Clone();var rect=new Rect(15,10,65,55);
Cv2.SetTheRNG(987654321);var rngBefore=Cv2.GetTheRNG().State;
var first=RegionProposal.Start(pixels,w,h,rect);var second=RegionProposal.Start(pixels,w,h,rect);
void Check(bool condition,string message) { if(!condition)throw new InvalidOperationException(message);Console.WriteLine("PASS "+message); }
var bgra=new byte[w*h*4];for(var i=0;i<w*h;i++) { Array.Copy(pixels,i*3,bgra,i*4,3);bgra[i*4+3]=255; }
var convertedBgr=PhotoRasterConversion.BgraToBgrOnWhite(bgra,w,h);
Check(convertedBgr.SequenceEqual(pixels) && RegionProposal.Start(convertedBgr,w,h,rect).CopyBinaryMask().SequenceEqual(first.CopyBinaryMask()),"desktop BGRA conversion retains opaque region proposal");
Check(first.CopyLabels().SequenceEqual(second.CopyLabels()),"repeated synthetic mask");
Check(Cv2.GetTheRNG().State==rngBefore,"thread random state restored");
Check(first.CopyBinaryMask()[30*w+40]==1 && first.CopyBinaryMask()[2*w+2]==0,"foreground and background");
var altered=first.CopyLabels();altered[0]=99;Check(first.CopyLabels()[0]!=99,"snapshot does not expose mutable storage");
var next=RegionProposal.Refine(pixels,w,h,first,[new(40,30,false),new(3,3,true)]);
Check(next.CopyLabels()[30*w+40]==0 && next.CopyLabels()[3*w+3]==1,"explicit corrections retained");
Check(pixels.SequenceEqual(original),"source image unchanged");
var bad=(byte[])pixels.Clone();bad[0]^=1;
try { RegionProposal.Refine(bad,w,h,first,[]);throw new Exception("stale image accepted"); } catch(InvalidOperationException) { Console.WriteLine("PASS stale image rejected"); }
try { RegionProposal.Refine(pixels,w,h,first,[new(4,4,true),new(4,4,false)]);throw new Exception("conflict accepted"); } catch(ArgumentException) { Console.WriteLine("PASS conflicting marks rejected"); }
using var cancellation=new CancellationTokenSource();
try { RegionProposal.Start(pixels,w,h,rect,cancellation.Token,i=> { if(i==1)cancellation.Cancel(); });throw new Exception("cancellation ignored"); }
catch(OperationCanceledException) { Console.WriteLine("PASS cancellation between native iterations"); }
Check(first.CopyLabels().SequenceEqual(second.CopyLabels()),"prior snapshot survives cancellation");
Check(Cv2.GetTheRNG().State==rngBefore,"thread random state restored after cancellation");
try { RegionProposal.Start(pixels,w,h,new Rect(0,0,w,h));throw new Exception("full image rectangle accepted"); } catch(ArgumentException) { Console.WriteLine("PASS rectangle without background rejected"); }
var sample=new byte[] {
    1,1,1,0,0,0,0,
    1,0,1,0,0,1,0,
    1,1,1,0,0,0,1,
    0,0,0,0,0,0,0 };
var sampleCopy=(byte[])sample.Clone();var parts=MaskComponents.Analyze(sample,7,4);
Check(parts.Components.Count==2 && parts.Components[0].Pixels==8 && parts.Components[1].Pixels==2 &&
      parts.ComponentAt(5,1)==parts.ComponentAt(6,2) && parts.ComponentAt(1,1)==0,"components preserve holes and diagonal connectivity");
Check(parts.CopySelection([1,2]).SequenceEqual(sample) && parts.CopySelection([1])[12]==0 &&
      parts.CopySelection([1])[8]==0 && sample.SequenceEqual(sampleCopy),"explicit selection preserves holes and source");
var selection=parts.CopySelection([1]);selection[0]=0;
Check(parts.CopySelection([1])[0]==1 && parts.CopySelection([]).All(x=>x==0),"selection copies are independent and empty selection is empty");
try { parts.CopySelection([3]);throw new Exception("invalid component accepted"); } catch(ArgumentOutOfRangeException) { Console.WriteLine("PASS invalid component rejected"); }
try { MaskComponents.Analyze([2],1,1);throw new Exception("invalid mask accepted"); } catch(ArgumentException) { Console.WriteLine("PASS invalid binary mask rejected"); }
try { MaskComponents.Analyze(sample,7,4,new CancellationToken(true));throw new Exception("component cancellation ignored"); } catch(OperationCanceledException) { Console.WriteLine("PASS component cancellation"); }
var contours=MaskContours.Extract(parts.CopySelection([1,2]),7,4);
Check(contours.Loops.Count==3 && contours.Loops.Count(l=>l.IsHole)==1 &&
      contours.Loops.Where(l=>l.IsHole).All(l=>!contours.Loops[l.Parent].IsHole),"contours retain both outer loops and hole hierarchy");
var contourPoints=contours.Loops[0].CopyPoints();var savedPoint=contourPoints[0];contourPoints[0]=new Point(-1,-1);
Check(contours.Loops[0].CopyPoints()[0]==savedPoint && sample.SequenceEqual(sampleCopy),"contour and input copies are independent");
Check(MaskContours.Extract(new byte[4],2,2).Loops.Count==0 && MaskContours.Extract([1],1,1).Loops.Single().Count==1,"empty and single-pixel contours stay explicit");
try { MaskContours.Extract([2],1,1);throw new Exception("invalid contour mask accepted"); } catch(ArgumentException) { Console.WriteLine("PASS invalid contour mask rejected"); }
try { MaskContours.Extract(sample,7,4,new CancellationToken(true));throw new Exception("contour cancellation ignored"); } catch(OperationCanceledException) { Console.WriteLine("PASS contour cancellation"); }
Point[] rectangle=[new(0,0),new(1,0),new(2,0),new(2,1),new(2,2),new(1,2),new(0,2),new(0,1)];
Check(ContourCollinearReduction.Reduce(rectangle).SequenceEqual(new[] { new Point(0,0),new Point(2,0),new Point(2,2),new Point(0,2) }) && rectangle.Length==8,
      "cyclic collinear reduction preserves rectangle corners and source");
Point[] backtrack=[new(0,0),new(2,0),new(1,0),new(1,1)];
Check(ContourCollinearReduction.Reduce(backtrack).SequenceEqual(backtrack),"reduction preserves reversal and topology defects for review");
var simpleReduced=ContourCollinearReduction.Reduce(rectangle).Select(p=>new Point2(p.X,p.Y)).ToArray();
LargeOpenPathValidator.Validate(simpleReduced);LargeContourValidator.Validate(simpleReduced);
try { LargeContourValidator.Validate(ContourCollinearReduction.Reduce(backtrack).Select(p=>new Point2(p.X,p.Y)).ToArray());throw new Exception("invalid contour accepted"); }
catch(ArgumentException) { Console.WriteLine("PASS reduced contour uses CAD topology validation"); }
try { ContourCollinearReduction.Reduce([new Point(-1,0)]);throw new Exception("invalid contour point accepted"); } catch(ArgumentOutOfRangeException) { Console.WriteLine("PASS invalid reduction point rejected"); }
try { ContourCollinearReduction.Reduce(rectangle,new CancellationToken(true));throw new Exception("reduction cancellation ignored"); } catch(OperationCanceledException) { Console.WriteLine("PASS reduction cancellation"); }

internal sealed record BatchJob(int AnnotationId,string Image,string ImageSha256,int X,int Y,int Width,int Height,ulong Seed);
internal sealed record BatchResult(int AnnotationId,string MaskSha256);
internal sealed record BatchReport(int Version,string JobsSha256,string OpenCvVersion,BatchResult[] Results);
internal sealed record ComponentBatchResult(int AnnotationId,string MaskSha256,int ComponentCount,int ForegroundPixels,int LargestPixels,int SelectedPixels,string SelectedSha256,int OuterContours,int Holes,int ContourPoints,int LargestOuterContourPoints,int ReducedLargestOuterContourPoints,bool RawPixelTopologyValid,bool ReducedPixelTopologyValid);
internal sealed record ComponentBatchReport(int Version,string JobsSha256,string OpenCvVersion,ComponentBatchResult[] Results);
