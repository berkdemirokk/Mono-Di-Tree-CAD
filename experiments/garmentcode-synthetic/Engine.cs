using TekstilCAD.Core;

namespace GarmentCodeSlice;

// Adapted from GarmentCode MIT, copyright (c) 2024 Maria Korosteleva.
// Fixed straight-edge research subset; coordinates here are centimetres.
public sealed record Input(double Bust, double BackWidth, double Waist, double WaistBackWidth,
    double WaistLine, double ShoulderDegrees, double ShirtWidth, double ShirtFlare,
    double ShirtLength, double SkirtLength, double SkirtFlare);
public sealed record Panel(string Name, IReadOnlyList<Point2> Vertices);
public sealed record Slice(IReadOnlyList<Panel> Panels, IReadOnlyList<string> SourceSkirtStitches)
{
    public string Status => "unresolved";
    public bool ProductionAllowed => false;
    public bool ExportAllowed => false;
}
public static class Engine
{
    public static Slice Create(Input x)
    {
        ArgumentNullException.ThrowIfNull(x);
        double[] positive=[x.Bust,x.BackWidth,x.Waist,x.WaistBackWidth,x.WaistLine,x.ShirtWidth,x.ShirtFlare,x.ShirtLength,x.SkirtLength];
        if(positive.Any(v=>!double.IsFinite(v)||v<=0||v>1000) || !double.IsFinite(x.SkirtFlare)||x.SkirtFlare<0||x.SkirtFlare>1000 || !double.IsFinite(x.ShoulderDegrees)||x.ShoulderDegrees<0||x.ShoulderDegrees>=45 || x.BackWidth>=x.Bust || x.WaistBackWidth>=x.Waist) throw new ArgumentException("Invalid or unsupported numeric input.");
        double slope=Math.Tan(x.ShoulderDegrees*Math.PI/180);
        Panel Torso(string name,bool front)
        {
            double frac=(front?(x.Bust-x.BackWidth):x.BackWidth)/(2*x.Bust);
            double width=frac*x.ShirtWidth*x.Bust, bottom=width*x.ShirtFlare;
            double length=x.ShirtLength*x.WaistLine-(front?slope*(2*frac-0.5)*x.Bust:0);
            if(length<=0) throw new ArgumentException("Invalid torso length.");
            Point2[] p=[new(0,0),new(-bottom,0),new(-width,length),new(0,length+slope*width)];
            Geometry.ValidateContour(p); return new(name,Array.AsReadOnly(p));
        }
        Panel Skirt(string name,double top)
        {
            double flare=x.SkirtFlare, bottom=top+2*flare;
            Point2[] p=[new(0,0),new(flare,x.SkirtLength),new(flare+top,x.SkirtLength),new(bottom,0)];
            Geometry.ValidateContour(p); return new(name,Array.AsReadOnly(p));
        }
        return new(Array.AsReadOnly(new[]{Torso("front_half",true),Torso("back_half",false),Skirt("skirt_front",x.Waist-x.WaistBackWidth),Skirt("skirt_back",x.WaistBackWidth)}),Array.AsReadOnly(new[]{"skirt_front:right=skirt_back:right","skirt_front:left=skirt_back:left"}));
    }
}
