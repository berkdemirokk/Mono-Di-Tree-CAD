using System.Security.Cryptography;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

// Test-only schema proposal, not part of project persistence or production.
// Its source edge belongs to the UNNOTCHED closed source snapshot.
internal sealed record ExperimentalPhysicalNotchV1(
    int Version, Guid SourcePieceId, string SnapshotHash, int SourceEdgeIndex,
    Point2 EdgeStart, Point2 EdgeEnd, string EdgeKind, double Fraction,
    string Direction, string Unit, double Width, double Depth,
    double PositionTolerance, double WidthTolerance, double DepthTolerance);

internal static class ExperimentalPhysicalNotchValidation
{
    internal static string Fingerprint(PatternPiece p)
    {
        using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
        {
            w.Write(p.Id.ToByteArray());w.Write(p.Closed);w.Write(p.Points.Count);
            foreach(var point in p.Points){w.Write(BitConverter.DoubleToInt64Bits(point.X));w.Write(BitConverter.DoubleToInt64Bits(point.Y));}
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
    // Absence is unknown, not a declaration that there are no physical notches.
    internal static ExperimentalPhysicalNotchV1? FromLegacy(PatternPiece _) => null;

    internal static bool Verify(PatternPiece source,ExperimentalPhysicalNotchV1? spec,Point2[] geometry,out string reason)
    {
        reason="Missing or invalid contract.";
        if(spec is null || spec.Version!=1 || source.Id==Guid.Empty || spec.SourcePieceId!=source.Id || !source.Closed ||
           source.Points is null || source.Points.Count<3 || source.Points.Count>4096 || source.Points[0]==source.Points[^1] ||
           spec.SourceEdgeIndex<0 || spec.SourceEdgeIndex>=source.Points.Count || spec.EdgeKind!="straight" || spec.Unit!="mm" ||
           (spec.Direction!="left" && spec.Direction!="right") || !double.IsFinite(spec.Fraction) || spec.Fraction<=0 || spec.Fraction>=1 ||
           !double.IsFinite(spec.Width) || spec.Width<=0 || !double.IsFinite(spec.Depth) || spec.Depth<=0 ||
           !double.IsFinite(spec.PositionTolerance) || spec.PositionTolerance<0 || !double.IsFinite(spec.WidthTolerance) || spec.WidthTolerance<0 ||
           !double.IsFinite(spec.DepthTolerance) || spec.DepthTolerance<0 || geometry is null || geometry.Length!=3)return false;
        if(!string.Equals(spec.SnapshotHash,Fingerprint(source),StringComparison.Ordinal)) {reason="Source snapshot identity changed.";return false;}
        int i=spec.SourceEdgeIndex;var a=source.Points[i];var b=source.Points[(i+1)%source.Points.Count];
        if(a!=spec.EdgeStart || b!=spec.EdgeEnd){reason="Source edge identity changed.";return false;}
        try
        {
            Geometry.ValidateContour(source.Points);foreach(var point in geometry)Geometry.Validate(point);
            double length=Geometry.Distance(a,b);if(length<=0 || spec.Width/2>=Math.Min(spec.Fraction,1-spec.Fraction)*length)return false;
            int side=spec.Direction=="left"?1:-1;
            if(side!=Math.Sign(Geometry.SignedArea(source.Points))){reason="Direction is not inward for the source orientation.";return false;}
            var unit=(b-a)*(1/length);var center=a+(b-a)*spec.Fraction;
            var expectedA=center-unit*(spec.Width/2);var expectedC=center+unit*(spec.Width/2);
            var expectedTip=center+new Point2(-unit.Y,unit.X)*(side*spec.Depth);
            if(Geometry.Distance(geometry[0],expectedA)>spec.PositionTolerance || Geometry.Distance(geometry[1],expectedTip)>spec.PositionTolerance ||
               Geometry.Distance(geometry[2],expectedC)>spec.PositionTolerance ||
               Math.Abs(Geometry.Distance(geometry[0],geometry[2])-spec.Width)>spec.WidthTolerance ||
               Math.Abs(Geometry.Distance((geometry[0]+geometry[2])*.5,geometry[1])-spec.Depth)>spec.DepthTolerance)
            {reason="Supplied V geometry is outside explicit tolerances.";return false;}
            reason="Experimental straight-edge V measurement only; representation, transformed topology, persistence and manufacturing are not certified.";return true;
        }
        catch(ArgumentException){reason="Invalid source or V coordinates.";return false;}
    }
}
