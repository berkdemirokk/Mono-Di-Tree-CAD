using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

internal static class OpenPathSourceSnapshot
{
    internal static bool Matches(PatternPiece current,PatternPiece original)
    {
        if(current.Id!=original.Id || current.Name!=original.Name || current.Closed!=original.Closed || !current.Points.SequenceEqual(original.Points))return false;
        var a=current.Metadata;var b=original.Metadata;
        if((a is null)!=(b is null) || (a is not null && b is not null &&
           (a.Role!=b.Role || a.Size!=b.Size || a.Material!=b.Material || a.Quantity!=b.Quantity)))return false;
        var marks=current.Notches;var previous=original.Notches;
        if((marks is null)!=(previous is null) || (marks is not null && previous is not null &&
           (marks.Count!=previous.Count || !marks.Zip(previous).All(pair =>
               pair.First is not null && pair.Second is not null && pair.First.VertexIndex==pair.Second.VertexIndex && pair.First.Position==pair.Second.Position))))return false;
        var g=current.Grainline;var h=original.Grainline;
        return (g is null && h is null) || (g is not null && h is not null && g.Start==h.Start && g.End==h.End && g.NeedsReview==h.NeedsReview);
    }
}
