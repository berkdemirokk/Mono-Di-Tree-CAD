using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace DressSeamBridge;

// Identities and confirmation are caller declarations, not independently verified.
public sealed record SourceIdentity(string Source, string Version, string Hash, string Draft);
public sealed record UpperCandidate(SourceIdentity Source, string Part, string Edge, bool DeclaredSynthetic);
public sealed record ResearchEdge(string Id, Point2 StartMm, Point2 EndMm, double LengthMm);
public sealed record LowerPanel(string Part, ResearchEdge Waist, IReadOnlyList<ResearchEdge> Side);
public sealed record IndependentTotal(double Millimeters, string Source);
public sealed record Request(ALinePanelInput Input, ALinePanelInput ConfirmedInput, bool Confirmed,
    bool SymmetryAccepted, string LowerSnapshotId, SourceIdentity LowerSource,
    IndependentTotal? IndependentWaist, IReadOnlyList<UpperCandidate> UpperCandidates);

public sealed class Report
{
    public ALinePanelInput InputMm { get; }
    public string LowerSnapshotId { get; }
    public SourceIdentity LowerSource { get; }
    public double DerivedWaistMm { get; }
    public IndependentTotal? IndependentWaist { get; }
    public IReadOnlyList<LowerPanel> LowerPanels { get; }
    public IReadOnlyList<UpperCandidate> UpperCandidates { get; }
    public string RelationStatus => "unresolved: no proven role, pairing, dart transformation, tolerance or independent evidence";
    public string Evidence => "caller-declared research; source identities and confirmation are not independently verified; floating-point lengths are not certified";
    public bool GeometryExportAllowed => false;
    public bool ProductionAllowed => false;
    public bool DeviceAllowed => false;
    internal Report(Request r, double waist, LowerPanel[] panels, UpperCandidate[] upper)
    {
        InputMm=r.Input; LowerSnapshotId=r.LowerSnapshotId; LowerSource=r.LowerSource;
        DerivedWaistMm=waist; IndependentWaist=r.IndependentWaist;
        LowerPanels=Array.AsReadOnly(panels); UpperCandidates=Array.AsReadOnly(upper);
    }
}

public static class DressSeamReport
{
    static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);
    static bool Valid(SourceIdentity? s) => s is not null && Has(s.Source) && Has(s.Version) && Has(s.Hash) && Has(s.Draft);
    public static Report Create(Request r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (!r.Confirmed || !r.SymmetryAccepted || r.Input is null || r.ConfirmedInput != r.Input)
            throw new ArgumentException("Current eleven measurements and symmetry must be explicitly confirmed.");
        if (!Has(r.LowerSnapshotId) || !Valid(r.LowerSource)) throw new ArgumentException("Lower provenance required.");
        if (r.LowerSource.Source != ALinePanelPreview.ModelId) throw new ArgumentException("Lower source must identify the A-line model.");
        // This invokes the existing implementation and validates its eleven inputs.
        var geometry=ALinePanelPreview.Create(r.Input);
        double waist=r.Input.FrontWaistArc+r.Input.BackWaistArc;
        if (r.IndependentWaist is { } total && (!double.IsFinite(total.Millimeters) || total.Millimeters <= 0 || !Has(total.Source) || total.Millimeters != waist))
            throw new ArgumentException("Independent total is invalid or differs from the derived total; no tolerance is assumed.");
        if (r.UpperCandidates is null) throw new ArgumentException("Use an empty list when upper candidates are unknown.");
        var upper=r.UpperCandidates.ToArray();
        var ids=new HashSet<(SourceIdentity,string,string)>();
        foreach (var candidate in upper)
        {
            if (candidate is null || !candidate.DeclaredSynthetic || !Valid(candidate.Source) || !Has(candidate.Part) || !Has(candidate.Edge)) throw new ArgumentException("Synthetic upper candidate identity incomplete.");
            if (candidate.Source.Hash == r.LowerSource.Hash || candidate.Source.Draft == r.LowerSource.Draft) throw new ArgumentException("Upper and lower source hashes and draft IDs must remain distinct.");
            if (!ids.Add((candidate.Source,candidate.Part,candidate.Edge))) throw new ArgumentException("Duplicate upper candidate.");
        }
        static ResearchEdge Edge(string id, Point2 a, Point2 b) => new(id,a,b,Geometry.Distance(a,b));
        static LowerPanel Panel(string part, IReadOnlyList<Point2> p) => new(part,Edge("waist",p[0],p[1]),
            Array.AsReadOnly(new[]{Edge("side-waist-hip",p[1],p[2]),Edge("side-hip-hem",p[2],p[3])}));
        return new Report(r,waist,new[]{Panel("frontLower",geometry.Front),Panel("backLower",geometry.Back)},upper);
    }
}
