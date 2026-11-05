using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using TekstilCAD.Patterns;

namespace BackWaistComparison;

public sealed record Interval(BigInteger Lower, BigInteger Upper);
public sealed record Report(string Body, string EvidenceSha256, string LowerSnapshotDeclaration,
    Interval UpperHalfMm, Interval LowerHalfMm, Interval UpperMinusLowerMm)
{
    public string UnitsPerMm => "1073741824";
    public string UpperSourceDeclaration => "Breanna 4.10.2; back.mjs disk hash 2eb13d29c5549ef18cc8180ed751f4d50635a91e408526c9fbed3de3e5d9edb0; executed module unresolved";
    public string UpperVariant => "shoulder-and-waist; front 06:00 and 13:30";
    public string LowerSource => ALinePanelPreview.ModelId;
    public string Status => "unresolved: upper executed-module identity, measurement protocols, dart closure and sewing correspondence are not proven";
    public bool ProductionAllowed => false;
    public bool GeometryExportAllowed => false;
    public bool DeviceAllowed => false;
}

public static class BackWaistReport
{
    public const string EvidenceSha256="372e257e9cf46e0a3de3974898798877e7dbb82ccc29c0eb0bb2c2986f56bf45";
    private static BigInteger BinaryUnits(double x)
    {
        if(!double.IsFinite(x)) throw new ArgumentException("Non-finite coordinate");
        ulong bits=BitConverter.DoubleToUInt64Bits(x), fraction=bits&((1UL<<52)-1), exponent=(bits>>52)&2047;
        BigInteger value=exponent==0 ? fraction : (BigInteger)((1UL<<52)|fraction) << checked((int)exponent-1);
        return (bits>>63)!=0 ? -value : value; // exact units of 2^-1074 mm
    }
    private static Interval AxisLength(double start,double end)
    {
        var difference=BigInteger.Abs(BinaryUnits(end)-BinaryUnits(start));
        var lo=difference >> 1044; // units of 2^-30 mm
        var remainder=difference-(lo<<1044);
        return new(lo,lo+(remainder.IsZero ? BigInteger.Zero : BigInteger.One));
    }
    public static Report Compare(byte[] evidenceBytes,string body,ALinePanelInput syntheticInput,string lowerSnapshotDeclaration,bool declaredSynthetic)
    {
        ArgumentNullException.ThrowIfNull(evidenceBytes);ArgumentNullException.ThrowIfNull(syntheticInput);
        if(!declaredSynthetic || string.IsNullOrWhiteSpace(lowerSnapshotDeclaration))throw new ArgumentException("Explicit synthetic snapshot declaration required");
        if(body is not ("synthetic-base" or "synthetic-scaled" or "synthetic-distinct"))throw new ArgumentException("Unsupported synthetic case");
        var bytes=evidenceBytes.ToArray();var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if(hash!=EvidenceSha256)throw new ArgumentException("Unexpected upper evidence hash");
        using var doc=JsonDocument.Parse(bytes);
        var cases=doc.RootElement.GetProperty("cases").EnumerateArray().Where(x=>x.GetProperty("body").GetString()==body).ToArray();
        if(cases.Length!=1)throw new ArgumentException("Ambiguous upper case");
        var c=cases[0];
        if(c.GetProperty("variant").GetString()!="shoulder-and-waist" || c.GetProperty("part").GetString()!="breanna.back" || c.GetProperty("physicalApproval").GetBoolean())throw new ArgumentException("Unsupported upper case");
        var i=c.GetProperty("halfBackWaist");
        if(i.GetProperty("unitsPerMm").GetString()!="1073741824")throw new ArgumentException("Wrong upper units");
        var upper=new Interval(BigInteger.Parse(i.GetProperty("lowerUnits").GetString()!),BigInteger.Parse(i.GetProperty("upperUnits").GetString()!));
        if(upper.Lower<=0 || upper.Upper<upper.Lower)throw new ArgumentException("Invalid upper interval");
        var panel=ALinePanelPreview.Create(syntheticInput).Back;
        var start=panel[0];var end=panel[1];
        if(start.Y!=end.Y || start.X==end.X)throw new ArgumentException("Unsupported or degenerate lower waist");
        var lower=AxisLength(start.X,end.X);
        return new(body,hash,lowerSnapshotDeclaration,upper,lower,new(upper.Lower-lower.Upper,upper.Upper-lower.Lower));
    }
}
