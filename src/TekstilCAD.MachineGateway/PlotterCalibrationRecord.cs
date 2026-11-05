using System.Text;
using System.Text.Json;
using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

// A measurement note, not a device certification or proof of physical output.
public sealed record PlotterCalibrationRecord(string ReferencePath,string Sha256,double UnitsPerMillimeter,double Scale,bool Orientation,double X,double Y,double? Diagonal1,double? Diagonal2,double XDeviation,double YDeviation,double? Diagonal1Deviation,double? Diagonal2Deviation,double? DiagonalDifference);

public static class PlotterCalibrationRecordStorage
{
    public const string Extension=".tcad-plotter-calibration.json";
    public static PlotterCalibrationRecord Create(string path,string hash,double units,bool orientation,double x,double y,double? d1,double? d2)
    {
        if(string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || hash is null || hash.Length!=64 || hash.Any(c=>!(c is >= '0' and <= '9' or >= 'A' and <= 'F')) || !double.IsFinite(units) || units<=0 || units>10000 || !Valid(x,1000) || !Valid(y,1000) || d1.HasValue!=d2.HasValue || (d1.HasValue && (!Valid(d1.Value,1500)||!Valid(d2!.Value,1500)))) throw new InvalidDataException("Referans veya ölçüler geçersiz.");
        var prepared=orientation ? PlotterCalibrationSquare.PrepareWithOrientationMarker(units,1) : PlotterCalibrationSquare.Prepare(units,1);
        if(prepared.ContentSha256!=hash)throw new InvalidDataException("Referans özeti ve ayarları uyuşmuyor.");
        var xy=PlotterScaleCheck.Compare(x,y);
        PlotterScaleCheck.DiagonalResult? diagonal=d1.HasValue ? PlotterScaleCheck.CompareDiagonals(d1.Value,d2!.Value) : null;
        return new(path,hash,units,1,orientation,x,y,d1,d2,xy.XDeviationMillimeters,xy.YDeviationMillimeters,diagonal?.FirstDeviationMillimeters,diagonal?.SecondDeviationMillimeters,diagonal?.DifferenceMillimeters);
    }
    private static bool Valid(double n,double max)=>double.IsFinite(n) && n>0 && n<=max;
    // Reject network/device paths and links observed during this check. This is
    // not an atomic traversal: concurrent local path replacement can race it.
    // Run on a worker because ordinary local storage can also be slow.
    public static bool ReferenceMatches(PlotterCalibrationRecord record)
    {
        var path=record.ReferencePath;
        if(OperatingSystem.IsWindows())
        {
            if(path.Length<3 || !char.IsAsciiLetter(path[0]) || path[1]!=':' || path[2]!='\\' || path.StartsWith(@"\\",StringComparison.Ordinal))return false;
            var drive=new DriveInfo(Path.GetPathRoot(path)!);
            if(drive.DriveType!=DriveType.Fixed)return false;
        }
        var full=Path.GetFullPath(path);
        var root=Path.GetPathRoot(full)!;
        var current=root;
        foreach(var part in full[root.Length..].Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))
        {
            if(part.Length==0)continue;
            current=Path.Combine(current,part);
            try { if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false; }
            catch(FileNotFoundException) { return false; }
            catch(DirectoryNotFoundException) { return false; }
        }
        return ProjectStorage.CaptureVersion(full).Sha256==record.Sha256;
    }
    public static void SaveNew(string path,PlotterCalibrationRecord record)
    {
        Validate(record);
        if(!path.EndsWith(Extension,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Kayıt uzantısı geçersiz.");
        var target=Path.GetFullPath(path);ProjectStorage.EnsureWritableDestination(target);
        ProjectStorage.AtomicWriteIfUnchanged(target,JsonSerializer.Serialize(record),new ProjectFileVersion(target,null));
    }
    public static PlotterCalibrationRecord Load(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>16384)throw new InvalidDataException("Kayıt çok büyük.");
        using var reader=new StreamReader(file,new UTF8Encoding(false,true));
        var text=reader.ReadToEnd();
        if(Encoding.UTF8.GetByteCount(text)>16384)throw new InvalidDataException("Kayıt çok büyük.");
        using var document=JsonDocument.Parse(text,new JsonDocumentOptions { MaxDepth=4 });
        var expectedNames=typeof(PlotterCalibrationRecord).GetProperties().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in document.RootElement.EnumerateObject())
            if(!expectedNames.Contains(property.Name) || !names.Add(property.Name))throw new InvalidDataException("Kayıtta bilinmeyen veya yinelenen alan var.");
        if(!names.SetEquals(expectedNames))throw new InvalidDataException("Kayıtta eksik alan var.");
        var value=JsonSerializer.Deserialize<PlotterCalibrationRecord>(text) ?? throw new InvalidDataException("Kayıt boş.");
        Validate(value);return value;
    }
    private static void Validate(PlotterCalibrationRecord value)
    {
        var expected=Create(value.ReferencePath,value.Sha256,value.UnitsPerMillimeter,value.Orientation,value.X,value.Y,value.Diagonal1,value.Diagonal2);
        if(value!=expected)throw new InvalidDataException("Kayıt içeriği veya hesaplanan sapmalar geçersiz.");
    }
}
