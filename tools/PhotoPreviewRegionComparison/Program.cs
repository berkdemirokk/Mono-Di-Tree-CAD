using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using OpenCvSharp;
using PhotoRegionSharpPrototype;
using TekstilCAD.Desktop;

if(args.Length!=3)throw new ArgumentException("Expected jobs JSON, previous C# results, new output JSON.");
if(File.Exists(args[2]))throw new IOException("Çıktı zaten var.");
var jobBytes=File.ReadAllBytes(args[0]);if(jobBytes.Length>2_000_000)throw new InvalidDataException("İş dosyası çok büyük.");
var jobs=JsonSerializer.Deserialize<Job[]>(jobBytes) ?? throw new InvalidDataException("İş dosyası geçersiz.");
var expected=JsonSerializer.Deserialize<Baseline>(File.ReadAllBytes(args[1])) ?? throw new InvalidDataException("Karşılaştırma dosyası geçersiz.");
var opencvVersion=Cv2.GetVersionString() ?? throw new InvalidDataException("OpenCV sürümü okunamadı.");
if(jobs.Length!=80 || expected.Results.Length!=jobs.Length || expected.OpenCvVersion!=opencvVersion || !string.Equals(expected.JobsSha256,Convert.ToHexString(SHA256.HashData(jobBytes)),StringComparison.Ordinal))
    throw new InvalidDataException("İşler önceki sonuçlarla eşleşmiyor.");
var expectedById=expected.Results.ToDictionary(x=>x.AnnotationId);var rows=new List<Row>();
foreach(var job in jobs)
{
    if(!expectedById.TryGetValue(job.AnnotationId,out var previous))throw new InvalidDataException("Anotasyon eşleşmiyor.");
    var source=File.ReadAllBytes(job.Image);
    if(Convert.ToHexString(SHA256.HashData(source))!=job.ImageSha256)throw new InvalidDataException("Görsel değişmiş.");
    var preview=ReferencePhotoDecoder.DecodeWithGeometry(source);
    var image=preview.Bitmap;
    if(preview.SourceOrientation!=1 || image.PixelWidth!=preview.OrientedSourceWidth || image.PixelHeight!=preview.OrientedSourceHeight)
        throw new InvalidDataException("Karşılaştırma yalnız yeniden boyutlanmamış, yön değişimi olmayan görüntüler içindir.");
    var bgr=PhotoRasterCapture.CaptureBgr(image,new Int32Rect(0,0,image.PixelWidth,image.PixelHeight));
    var mask=RegionProposal.Start(bgr,image.PixelWidth,image.PixelHeight,new OpenCvSharp.Rect(job.X,job.Y,job.Width,job.Height),seed:job.Seed).CopyBinaryMask();
    var digest=Convert.ToHexString(SHA256.HashData(mask));
    rows.Add(new Row(job.AnnotationId,Convert.ToHexString(SHA256.HashData(bgr)),digest,digest==previous.MaskSha256));
}
if(rows.Select(r=>r.AnnotationId).Distinct().Count()!=jobs.Length)throw new InvalidDataException("Tekrarlanan anotasyon.");
var report=new Report(Convert.ToHexString(SHA256.HashData(jobBytes)),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))),opencvVersion,rows.Count,rows.Count(r=>r.MaskMatches),rows.ToArray());
using var file=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write,FileShare.None);
JsonSerializer.Serialize(file,report,new JsonSerializerOptions { WriteIndented=true });
Console.WriteLine($"Compared {report.Count}: {report.Matched} masks match.");

internal sealed record Job(int AnnotationId,string Image,string ImageSha256,int X,int Y,int Width,int Height,ulong Seed);
internal sealed record Baseline(string JobsSha256,string OpenCvVersion,BaselineRow[] Results);
internal sealed record BaselineRow(int AnnotationId,string MaskSha256);
internal sealed record Row(int AnnotationId,string PreviewBgrSha256,string MaskSha256,bool MaskMatches);
internal sealed record Report(string JobsSha256,string BaselineSha256,string OpenCvVersion,int Count,int Matched,Row[] Results);
