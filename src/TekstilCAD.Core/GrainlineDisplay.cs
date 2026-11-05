namespace TekstilCAD.Core;

/// <summary>Display-only arrowhead; never added to a pattern contour or cutting path.</summary>
public static class GrainlineDisplay
{
    public readonly record struct Arrow(Point2 Left, Point2 Tip, Point2 Right);

    public static Arrow CreateArrow(Point2 start, Point2 end, double maximumLength)
    {
        if (!start.IsFinite || !end.IsFinite || !double.IsFinite(maximumLength) || maximumLength <= 0)
            throw new ArgumentException("İplik yönü gösterim koordinatları geçersiz.");
        var dx=end.X-start.X; var dy=end.Y-start.Y;
        var magnitude=Math.Max(Math.Abs(dx),Math.Abs(dy));
        if (!double.IsFinite(magnitude) || magnitude==0)
            throw new ArgumentException("İplik yönü gösterim uzunluğu geçersiz.");
        var nx=dx/magnitude; var ny=dy/magnitude;
        var norm=Math.Sqrt(nx*nx+ny*ny); nx/=norm; ny/=norm;
        // Avoid squaring huge vectors or overflowing their length.
        var length=Math.Min(maximumLength,magnitude*0.2*norm);
        var left=new Point2(end.X-nx*length-ny*length*0.35,end.Y-ny*length+nx*length*0.35);
        var right=new Point2(end.X-nx*length+ny*length*0.35,end.Y-ny*length-nx*length*0.35);
        if (!left.IsFinite || !right.IsFinite || left==end || right==end || left==right)
            throw new ArgumentException("İplik yönü oku bu koordinat hassasiyetinde gösterilemiyor.");
        return new(left,end,right);
    }

    public static string Description(Grainline line) =>
        $"İplik yönü: başlangıç ({MeasurementInput.Format(line.Start.X)}, {MeasurementInput.Format(line.Start.Y)}) mm → bitiş ({MeasurementInput.Format(line.End.X)}, {MeasurementInput.Format(line.End.Y)}) mm. " +
        (line.NeedsReview ? "KONTROL BEKLİYOR; üretim onayı değildir." : "Kontrol bekleme işareti yok; üretim onayı değildir.");
}
