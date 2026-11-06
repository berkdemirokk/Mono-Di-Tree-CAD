using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.FileFormats;

/// <summary>Validates detached rotated history, not manufacturing suitability or historical authenticity.
/// This helper does not enable a project version or replace raw JSON validation.</summary>
internal static class RotatedProjectValidation
{
    internal static void Verify(ProjectDocument project,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(project);
        var snapshot=project.NestingSnapshot ?? throw new InvalidDataException("Döndürme izi için pastal geçmişi gerekli.");
        var trace=snapshot.RotationTrace ?? throw new InvalidDataException("Döndürme izi eksik.");
        if(project.Pieces is null || trace.SourcePieces is null || trace.LayoutPieces is null || trace.Copies is null ||
           snapshot.Placements is null || project.Pieces.Count>ProjectStorage.MaximumPieces ||
           trace.SourcePieces.Count>NestingEngine.MaximumPieces || trace.LayoutPieces.Count>NestingEngine.MaximumPieces ||
           trace.Copies.Count!=snapshot.Placements.Count)
            throw new InvalidDataException("Döndürme izi parça veya eşleme sayısı geçersiz.");
        static bool Same(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
        if(snapshot.Algorithm!=NestingSnapshot.ShelfAlgorithm || snapshot.LayoutFingerprint!=trace.LayoutFingerprint ||
           !Same(snapshot.FabricWidth,trace.FabricWidth) || !Same(snapshot.UsedLength,trace.UsedLength) ||
           !Same(snapshot.Gap,trace.Gap) || !Same(snapshot.EdgeMargin,trace.EdgeMargin))
            throw new InvalidDataException("Pastal geçmişi ve döndürme izi kısıtları eşleşmiyor.");
        for(var i=0;i<trace.Copies.Count;i++)
        {
            token.ThrowIfCancellationRequested();var c=trace.Copies[i];var p=snapshot.Placements[i];
            if(c is null || p is null || c.SourcePieceId!=p.SourcePieceId || c.CopyNumber!=p.CopyNumber || c.PlacedPieceId!=p.PlacedPieceId)
                throw new InvalidDataException("Pastal ve döndürme kopya eşlemesi farklı.");
        }
        long points=0,marks=0;
        foreach(var piece in project.Pieces.Concat(trace.SourcePieces).Concat(trace.LayoutPieces))
        {
            token.ThrowIfCancellationRequested();
            if(piece is null || piece.Points is null || piece.Points.Count>ProjectStorage.MaximumPointsPerPiece ||
               (points+=piece.Points.Count)>ProjectStorage.MaximumTotalPoints ||
               piece.Notches is { } list && ((marks+=list.Count)>ProjectStorage.MaximumTotalPoints || list.Any(m=>m is null)) ||
               !WellFormed(piece.Name) || !WellFormed(piece.Metadata?.Role) || !WellFormed(piece.Metadata?.Size) || !WellFormed(piece.Metadata?.Material))
                throw new InvalidDataException("Döndürme izi kaynak ve yerleşim sınırı veya metni geçersiz.");
            piece.ExperimentalALine?.Validate();
        }
        try
        {
            // Verify the captured historical layout even if the visible project has
            // since changed. IsCurrentFor remains the separate freshness check.
            var historical=new ProjectDocument { Pieces=trace.LayoutPieces };
            _=RotatedNestingRecord.FromTrace(trace,historical,token);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Döndürme izi geometrik olarak doğrulanamadı.",ex); }
    }

    private static bool WellFormed(string? text)
    {
        if(text is null)return true;
        for(var i=0;i<text.Length;i++)
        {
            if(char.IsHighSurrogate(text[i])) { if(++i>=text.Length || !char.IsLowSurrogate(text[i]))return false; }
            else if(char.IsLowSurrogate(text[i]))return false;
        }
        return true;
    }
}
