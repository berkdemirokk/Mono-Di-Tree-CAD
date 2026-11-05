using TekstilCAD.Core;

namespace GarmentCodeSlice;

// Adapted from GarmentCode MIT, copyright (c) 2024 Maria Korosteleva.
// Fixed straight-edge research subset; coordinates here are centimetres.
public sealed record Input(decimal Bust, decimal BackWidth, decimal Waist, decimal WaistBackWidth,
    decimal WaistLine, decimal ShoulderDegrees, decimal ShirtWidth, decimal ShirtFlare,
    decimal ShirtLength, decimal SkirtLength, decimal SkirtFlare);
public sealed record Panel(string Name, IReadOnlyList<Point2> Vertices);
public sealed record Slice(IReadOnlyList<Panel> Panels, IReadOnlyList<string> SourceSkirtStitches)
{
    public string Status => "unresolved";
    public bool ProductionAllowed => false;
    public bool ExportAllowed => false;
}
public static class Engine
{
    private sealed record CalculatedInput(double Bust,double BackWidth,double Waist,double WaistBackWidth,double WaistLine,double ShoulderDegrees,double ShirtWidth,double ShirtFlare,double ShirtLength,double SkirtLength,double SkirtFlare);
    public static Slice Create(string syntheticId, Input raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        decimal scale=syntheticId switch { "synthetic-base" => 1m, "synthetic-scaled" => 1.2m, _ => throw new ArgumentException("Unknown synthetic fixture.",nameof(syntheticId)) };
        var expected=new Input(100m*scale,44m*scale,80m*scale,36m*scale,40m*scale,12m,1.1m,1m,1m,60m*scale,12m*scale);
        if(raw!=expected) throw new ArgumentException("Only the exact predefined synthetic fixture is supported.",nameof(raw));
        // Conversion takes place only after all eleven decimal fields match.
        var x=new CalculatedInput((double)raw.Bust,(double)raw.BackWidth,(double)raw.Waist,(double)raw.WaistBackWidth,
            (double)raw.WaistLine,(double)raw.ShoulderDegrees,(double)raw.ShirtWidth,(double)raw.ShirtFlare,
            (double)raw.ShirtLength,(double)raw.SkirtLength,(double)raw.SkirtFlare);
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
            Point2[] p=name=="skirt_front" ? [new(0,0),new(bottom,0),new(flare+top,x.SkirtLength),new(flare,x.SkirtLength)] : [new(0,0),new(flare,x.SkirtLength),new(flare+top,x.SkirtLength),new(bottom,0)];
            Geometry.ValidateContour(p); return new(name,Array.AsReadOnly(p));
        }
        return new(Array.AsReadOnly(new[]{Torso("front_half",true),Torso("back_half",false),Skirt("skirt_front",x.Waist-x.WaistBackWidth),Skirt("skirt_back",x.WaistBackWidth)}),Array.AsReadOnly(new[]{"skirt_front:right=skirt_back:right","skirt_front:left=skirt_back:left"}));
    }
}




