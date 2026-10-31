using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>
/// Prepares a detached grainline with its contour anchor. These checks are
/// numerical representation guards, not manufacturing tolerances or evidence
/// that a grainline is appropriate for a fabric or cutting direction.
/// </summary>
internal static class GrainlineTransform
{
    internal static bool Same(Grainline? a,Grainline? b) => ReferenceEquals(a,b) ||
        (a is not null && b is not null && a.Start==b.Start && a.End==b.End && a.NeedsReview==b.NeedsReview);

    internal static Grainline? Translate(Grainline? grain,Point2 anchor,Point2 placedAnchor,double dx,double dy)
    {
        if(grain is null)return null;
        Validate(grain);
        // Geometry.Translate checks the grainline and both vectors to the same
        // contour anchor. This can conservatively reject hard-to-represent moves.
        var moved=Geometry.Translate([anchor,grain.Start,grain.End],dx,dy);
        if(moved[0]!=placedAnchor)throw new ArgumentException("İplik yönü ve kontur aynı konuma güvenle taşınamadı.");
        var result=new Grainline { Start=moved[1],End=moved[2],NeedsReview=grain.NeedsReview };
        Validate(result);return result;
    }

    internal static Grainline? Transform(Grainline? grain,Point2 anchor,Point2 placedAnchor,
        Func<IReadOnlyList<Point2>,List<Point2>> transform)
    {
        if(grain is null)return null;
        Validate(grain);
        var source=new[] { anchor,grain.Start,grain.End };
        var moved=transform(source);
        if(moved.Count!=3 || moved[0]!=placedAnchor)
            throw new ArgumentException("İplik yönü ve kontur aynı dönüşümle güvenle hesaplanamadı.");
        var result=new Grainline { Start=moved[1],End=moved[2],NeedsReview=grain.NeedsReview };
        Validate(result);
        // Rotation and reflection are rigid. Check the grainline length and its
        // distances from the contour anchor with a strict technical relative
        // representation bound; reject instead of silently moving a small mark.
        for(var i=0;i<3;i++)for(var j=i+1;j<3;j++)
        {
            var before=Geometry.Distance(source[i],source[j]);var after=Geometry.Distance(moved[i],moved[j]);
            if(before==0 ? after!=0 : Math.Abs(after-before)>before*1e-9)
                throw new ArgumentException("Dönüşüm sırasında iplik yönü sayısal hassasiyetle korunamıyor.");
        }
        return result;
    }

    private static void Validate(Grainline grain)
    {
        Geometry.Validate(grain.Start);Geometry.Validate(grain.End);
        if(grain.Start==grain.End)throw new ArgumentException("İplik yönünün iki ucu farklı olmalıdır.");
    }
}
