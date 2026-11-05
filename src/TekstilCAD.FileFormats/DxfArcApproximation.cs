using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Experimental positive-OCS-normal arc sampling. Does not parse DXF or certify total error.</summary>
public static class DxfArcApproximation
{
    public static DxfBulgeApproximationResult Approximate(Point2 center,double radius,double startDegrees,double endDegrees,
        double millimetersPerUnit,double toleranceMm,int remainingPointBudget,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!center.IsFinite)throw new ArgumentOutOfRangeException(nameof(center));
        if(!double.IsFinite(radius) || radius<=0)throw new ArgumentOutOfRangeException(nameof(radius));
        if(!double.IsFinite(startDegrees) || !double.IsFinite(endDegrees))throw new ArgumentOutOfRangeException(nameof(startDegrees));
        if(!double.IsFinite(millimetersPerUnit) || millimetersPerUnit<=0)throw new ArgumentOutOfRangeException(nameof(millimetersPerUnit));
        if(!double.IsFinite(toleranceMm) || toleranceMm<=0)throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        static double Ulp(double x) { var a=Math.Abs(x);return Math.BitIncrement(a)-a; }
        var angularSpacing=Math.Max(Ulp(startDegrees),Ulp(endDegrees))*Math.PI/180;
        var angleError=angularSpacing*radius*millimetersPerUnit;
        if(!double.IsFinite(angleError) || angleError>toleranceMm/2048)
            throw new InvalidDataException("Yay açıları istenen toleransta güvenle gösterilemiyor.");
        static double Normalize(double degrees) { var n=degrees%360;return n<0 ? n+360 : n; }
        var start=Normalize(startDegrees);var end=Normalize(endDegrees);
        var sweep=end-start;if(sweep<0)sweep+=360;
        if(!double.IsFinite(sweep) || sweep<=0 || sweep>=360)
            throw new InvalidDataException("Yay başlangıç ve bitiş açıları çakışıyor veya belirsiz.");
        var segmentCount=(int)Math.Ceiling(sweep/90);
        var segmentSweep=sweep/segmentCount;
        var bulge=Math.Tan(segmentSweep*Math.PI/720);
        var vertices=new List<DxfBulgeVertex>(segmentCount+1);
        for(var i=0;i<=segmentCount;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var degrees=i==segmentCount ? end : Normalize(start+segmentSweep*i);
            var radians=degrees*Math.PI/180;
            var px=center.X+radius*Math.Cos(radians);var py=center.Y+radius*Math.Sin(radians);
            var dx=px-center.X;var dy=py-center.Y;
            var representedRadius=Math.Sqrt(dx*dx+dy*dy);
            var error=Math.Abs(representedRadius-radius)*millimetersPerUnit;
            if(!double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(error) || error>toleranceMm/2048 || representedRadius<=0)
                throw new InvalidDataException("Yay uçları istenen toleransta güvenle gösterilemiyor.");
            vertices.Add(new(new Point2(px,py),i==segmentCount ? 0 : bulge));
        }
        return DxfBulgeApproximation.Approximate(vertices,false,millimetersPerUnit,toleranceMm,remainingPointBudget,cancellationToken);
    }
}
