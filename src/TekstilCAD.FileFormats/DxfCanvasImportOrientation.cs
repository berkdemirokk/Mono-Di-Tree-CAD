using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Detached optional Y reflection after supported DXF geometry has been imported.</summary>
public static class DxfCanvasImportOrientation
{
    public static ProjectDocument ReflectY(ProjectDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);ProjectStorage.Validate(source);
        // These links describe prior geometry, so do not silently reinterpret them.
        if(source.GradingRules.Count!=0 || source.NestingSnapshot is not null || source.ImportProvenance is not null)
            throw new InvalidDataException("Serileme, pastal veya önceki kaynak geçmişi içeren proje bu DXF dönüşümünde kullanılamaz.");
        var result=source.Clone();
        foreach(var piece in result.Pieces)
        {
            piece.Points=piece.Points.Select(p=>new Point2(p.X,-p.Y)).ToList();
            if(piece.Grainline is { } grain)
            { grain.Start=new Point2(grain.Start.X,-grain.Start.Y);grain.End=new Point2(grain.End.X,-grain.End.Y); }
        }
        ProjectStorage.Validate(result);return result;
    }
}
