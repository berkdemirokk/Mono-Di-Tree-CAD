using System.Collections.Immutable;
using System.Globalization;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

internal sealed record DxfExpandedContour(ImmutableArray<Point2> Points, bool Closed,
    ImmutableArray<DxfInsertOccurrence> InsertPath, int EntityOccurrence, string? EffectiveLayer = null)
{
    // Source entities only; vertex/endpoint correspondence is not represented.
    public ImmutableArray<int> EntityOccurrences { get; init; } = ImmutableArray<int>.Empty;
    public ImmutableArray<int> ChildOccurrences { get; init; } = ImmutableArray<int>.Empty;
}

/// <summary>
/// Conservative, transactional expansion of a restricted 2D INSERT subset.
/// The callback overload does not resolve inherited layer semantics and is
/// suitable only for isolated tests. The strict overload uses an explicit
/// context-aware resolver and is used by the opt-in importer.
/// </summary>
internal static class DxfInsertExpander
{
    private const int MaxDepth = 64;
    private const int MaxVisits = 100_000;
    private const int MaxPoints = 200_000;
    // The current contour validator compares edge pairs. Bound aggregate work
    // across the import, not just the size of each individual contour.
    private const long MaxContourValidationPairs = 16_777_216;
    // Exact rational checking scales with both vertex count and nesting depth.
    private const int MaxPrecisionFrameVisits = 100_000;
    // General-angle preparation also runs exact trigonometric arithmetic.
    // Bound it separately from the number of checked points; many small
    // occurrences would otherwise prepare an expensive chain for each one.
    // These are work limits, not elapsed-time or responsiveness guarantees.
    private const int MaxGeneralContextPreparations = 256;
    private const int MaxGeneralPreparedFrames = 4096;
    private static readonly HashSet<int> BlockFields = [2, 3, 5, 8, 10, 20, 30, 70, 100, 330];
    private static readonly HashSet<int> InsertFields = [2, 5, 6, 8, 10, 20, 30, 41, 42, 43, 44, 45, 50, 66, 67, 70, 71, 100, 210, 220, 230, 330, 410];
    private static readonly HashSet<int> PolyFields = [5, 6, 8, 10, 20, 38, 39, 40, 41, 42, 43, 67, 70, 90, 100, 210, 220, 230, 330, 410];
    private static readonly HashSet<int> StrictInsertFields = [.. InsertFields, 60, 62, 420, 430];
    private static readonly HashSet<int> StrictPolyFields = [.. PolyFields, 60, 62, 420, 430];
    private static readonly HashSet<int> StrictLineFields = [5, 6, 8, 10, 20, 30, 11, 21, 31, 39, 60, 62, 67, 100, 210, 220, 230, 330, 410, 420, 430];
    private static readonly HashSet<int> LegacyPolylineFields = [5, 6, 8, 10, 20, 30, 39, 40, 41, 60, 62, 66, 67, 70, 100, 210, 220, 230, 330, 410, 420, 430];
    private static readonly HashSet<int> LegacyVertexFields = [5, 6, 8, 10, 20, 30, 40, 41, 42, 60, 62, 67, 70, 100, 210, 220, 230, 330, 410, 420, 430];
    private static readonly HashSet<int> LegacySequenceEndFields = [5, 6, 8, 60, 62, 67, 100, 330, 410, 420, 430];
    private const int MaxLines = 4096;
    private readonly record struct LineSourcePoint(NumberToken X, NumberToken Y);
    private readonly record struct ExpandedLine(Point2 A, Point2 B, LineSourcePoint SourceA, LineSourcePoint SourceB, int Ordinal);

    public static ImmutableArray<DxfExpandedContour> Expand(DxfInsertIndex index, double millimetersPerUnit,
        Func<string, string> decodeBlockName, Func<DxfIndexEntity, bool> approveEntity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approveEntity);
        return ExpandCore(index, millimetersPerUnit, decodeBlockName, approveEntity, null, 0, cancellationToken);
    }

    /// <summary>Expands with per-occurrence inherited layer context; hidden or non-plot entities reject the transaction.</summary>
    public static ImmutableArray<DxfExpandedContour> Expand(DxfInsertIndex index, double millimetersPerUnit,
        Func<string, string> decodeBlockName, DxfInsertLayerResolver layerResolver, CancellationToken cancellationToken = default)
        => Expand(index, millimetersPerUnit, decodeBlockName, layerResolver, 0, cancellationToken);

    /// <summary>
    /// Strict expansion verifies each coordinate against exact source-decimal arithmetic.
    /// The unit factor means the exact decimal represented by the double's invariant
    /// round-trip string. Tolerance is absolute millimeters per coordinate component,
    /// not radial distance. General angles use conservative interval enclosures;
    /// insufficient tolerance or computational budget rejects the transaction.
    /// </summary>
    public static ImmutableArray<DxfExpandedContour> Expand(DxfInsertIndex index, double millimetersPerUnit,
        Func<string, string> decodeBlockName, DxfInsertLayerResolver layerResolver, double componentToleranceMm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layerResolver);
        if (!double.IsFinite(componentToleranceMm) || componentToleranceMm < 0)
            throw new ArgumentOutOfRangeException(nameof(componentToleranceMm));
        return ExpandCore(index, millimetersPerUnit, decodeBlockName, null, layerResolver, componentToleranceMm, cancellationToken);
    }

    private static ImmutableArray<DxfExpandedContour> ExpandCore(DxfInsertIndex index, double millimetersPerUnit,
        Func<string, string> decodeBlockName, Func<DxfIndexEntity, bool>? approveEntity,
        DxfInsertLayerResolver? layerResolver, double componentToleranceMm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(decodeBlockName);
        if (!double.IsFinite(millimetersPerUnit) || millimetersPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(millimetersPerUnit));
        cancellationToken.ThrowIfCancellationRequested();
        var blocks = new Dictionary<string, DxfIndexedBlock>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in index.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!blocks.TryAdd(block.Name, block)) throw new InvalidDataException("DXF blok adı yinelenmiş.");
        }
        var result = ImmutableArray.CreateBuilder<DxfExpandedContour>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<DxfInsertOccurrence>();
        // Outer-to-inner while traversing; reversed once for each contour.
        var precisionPath = new List<DxfPrecisionTransform?>();
        var generalPrecisionPath = new List<DxfGeneralPrecisionTransform>();
        // Repeated occurrences in this import share immutable angle bounds.
        // Scope and count are bounded by this import's existing frame budget.
        var generalAngleCache = new DxfGeneralInsertPrecision.AngleCache(MaxGeneralPreparedFrames);
        var unitsText = millimetersPerUnit.ToString("R", CultureInfo.InvariantCulture);
        int precisionFrameVisits = 0, generalContextPreparations = 0, generalPreparedFrames = 0;
        int visits = 0, totalPoints = 0, totalLines = 0;
        long contourValidationPairs = 0;
        foreach (var model in index.ModelEntities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.Entity.Type == "INSERT") ExpandInsert(model, null, null, null, null);
        }
        return result.ToImmutable();

        void ExpandInsert(DxfIndexedEntity indexed, DxfInsertTransform? parent, NumberToken? parentBaseX, NumberToken? parentBaseY, DxfInsertLayerContext? parentLayer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visits > MaxVisits) throw new InvalidDataException("DXF INSERT işlem sınırını aşıyor.");
            if (path.Count >= MaxDepth) throw new InvalidDataException("DXF INSERT derinlik sınırını aşıyor.");
            var e = indexed.Entity;
            CheckFields(e, layerResolver is null ? InsertFields : StrictInsertFields);
            var effectiveLayer = Resolve(e, parentLayer);
            if (!indexed.Children.IsEmpty || indexed.SequenceEnd is not null || !Exact(e,66,0))
                throw new InvalidDataException("DXF INSERT attribute desteklenmiyor.");
            Plane(e);
            if (!Exact(e,43,1) || !Exact(e,44,0) || !Exact(e,45,0) || !Exact(e,70,1) || !Exact(e,71,1))
                throw new InvalidDataException("DXF INSERT dizi veya Z ölçeği desteklenmiyor.");
            var group = One(e,2,true)!.Value;
            var name = decodeBlockName(group.RawValue);
            if (string.IsNullOrWhiteSpace(name) || !blocks.TryGetValue(name, out var block))
                throw new InvalidDataException("DXF INSERT blok başvurusu bulunamadı: " + name);
            if (!active.Add(name)) throw new InvalidDataException("DXF INSERT döngü içeriyor: " + name);
            try
            {
                var header = block.Header;
                CheckFields(header, BlockFields);
                // BLOCK headers are definitions, not nested drawable entities.
                if (layerResolver is null) Approve(header);
                if (!Exact(header,70,0) || !Exact(header,30,0))
                    throw new InvalidDataException("DXF BLOCK bayrağı veya Z değeri desteklenmiyor.");
                // Strict placement cannot infer a missing X or Y coordinate.
                // The DXF reference specifies these as point components, but
                // does not state that omitted components default to zero.
                if (layerResolver is not null)
                {
                    _ = One(header, 10, true); _ = One(header, 20, true);
                    _ = One(e, 10, true); _ = One(e, 20, true);
                }
                var bx = Token(header,10,0); var by = Token(header,20,0);
                var ix = Token(e,10,0); var iy = Token(e,20,0);
                var sx = Token(e,41,1); var sy = Token(e,42,1); var angle = Token(e,50,0);
                DxfPrecisionTransform? precisionFrame = null;
                DxfGeneralPrecisionTransform? generalFrame = null;
                if (layerResolver is not null)
                {
                    if (DxfInsertPrecision.TryGetQuarterTurns(angle.Canonical, out var quarterTurns, cancellationToken))
                        precisionFrame = new DxfPrecisionTransform(bx.Canonical, by.Canonical, ix.Canonical, iy.Canonical,
                            sx.Canonical, sy.Canonical, quarterTurns);
                    generalFrame = new DxfGeneralPrecisionTransform(bx.Canonical, by.Canonical, ix.Canonical, iy.Canonical,
                        sx.Canonical, sy.Canonical, angle.Canonical);
                }
                if ((parentBaseX.HasValue && ix.Value == parentBaseX.Value.Value && ix.Canonical != parentBaseX.Value.Canonical) ||
                    (parentBaseY.HasValue && iy.Value == parentBaseY.Value.Value && iy.Canonical != parentBaseY.Value.Canonical))
                    throw new InvalidDataException("DXF iç içe INSERT koordinatları kayan nokta hassasiyetiyle çakışıyor.");
                DxfInsertTransform transform;
                try
                {
                    transform = DxfInsertTransform.Create(new(bx.Value, by.Value), new(ix.Value, iy.Value), sx.Value, sy.Value, angle.Value);
                    if (parent.HasValue) transform = transform.Then(parent.Value);
                }
                catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("DXF INSERT dönüşümü geçersiz.", ex); }
                path.Add(new(block.Name, e.SourceOrdinal));
                if (generalFrame is not null) { precisionPath.Add(precisionFrame); generalPrecisionPath.Add(generalFrame); }
                try
                {
                    // The captured verifier belongs to this occurrence only. Its
                    // prepared chain is built lazily and reused for every point
                    // in this block instance; sibling/nested instances get their
                    // own independently captured chain.
                    DxfPrecisionTransform[]? cardinalFrames = null;
                    DxfGeneralInsertPrecision.PreparedContext? generalContext = null;
                    bool VerifyPoint(string sourceX, string sourceY, Point2 actual)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (precisionPath.All(frame => frame is not null))
                        {
                            cardinalFrames ??= precisionPath.AsEnumerable().Reverse().Select(frame => frame!).ToArray();
                            return DxfInsertPrecision.IsWithinTolerance(sourceX, sourceY, cardinalFrames, unitsText,
                                actual.X, actual.Y, componentToleranceMm, cancellationToken);
                        }
                        if (generalContext is null)
                        {
                            int framesToPrepare = generalPrecisionPath.Count;
                            if (generalContextPreparations >= MaxGeneralContextPreparations ||
                                framesToPrepare > MaxGeneralPreparedFrames - generalPreparedFrames)
                                throw new InvalidDataException("DXF genel açılı yerleşimlerin hassasiyet hazırlama iş sınırı aşıldı; aktarım yapılmadı.");
                            generalContextPreparations++;
                            generalPreparedFrames += framesToPrepare;
                            generalContext = DxfGeneralInsertPrecision.Prepare(generalPrecisionPath.AsEnumerable().Reverse().ToArray(),
                                unitsText, generalAngleCache, cancellationToken);
                        }
                        return generalContext.IsWithinTolerance(sourceX, sourceY, actual.X, actual.Y,
                            componentToleranceMm, cancellationToken);
                    }
                    // Local to this occurrence: separate inserts may touch at the
                    // same coordinates but never belong to the same contour.
                    var lineGroups = new Dictionary<string, List<ExpandedLine>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var child in block.Entities)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (child.Entity.Type == "INSERT") ExpandInsert(child, transform, bx, by, effectiveLayer);
                        else if (child.Entity.Type == "LWPOLYLINE") ExpandPolyline(child, transform, bx, by, effectiveLayer, VerifyPoint);
                        else if (child.Entity.Type == "POLYLINE" && layerResolver is not null)
                            ExpandLegacyPolyline(child, transform, bx, by, effectiveLayer, VerifyPoint);
                        else if (child.Entity.Type == "LINE" && layerResolver is not null)
                        {
                            var (layer, line) = ExpandLine(child, transform, bx, by, effectiveLayer, VerifyPoint);
                            if (!lineGroups.TryGetValue(layer, out var groupLines)) lineGroups[layer] = groupLines = [];
                            groupLines.Add(line);
                        }
                        else throw new InvalidDataException("DXF BLOCK içeriği desteklenmiyor: " + child.Entity.Type);
                    }
                    foreach (var (layer, lines) in lineGroups) AddLineContours(layer, lines);
                }
                finally
                {
                    if (generalFrame is not null) { precisionPath.RemoveAt(precisionPath.Count - 1); generalPrecisionPath.RemoveAt(generalPrecisionPath.Count - 1); }
                    path.RemoveAt(path.Count - 1);
                }
            }
            finally { active.Remove(name); }
        }

        (string Layer, ExpandedLine Line) ExpandLine(DxfIndexedEntity indexed, DxfInsertTransform transform,
            NumberToken bx, NumberToken by, DxfInsertLayerContext? parentLayer, Func<string,string,Point2,bool> verifyPoint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visits > MaxVisits || ++totalLines > MaxLines) throw new InvalidDataException("DXF LINE işlem sınırını aşıyor.");
            var e = indexed.Entity;
            CheckFields(e, StrictLineFields);
            var effective = Resolve(e, parentLayer) ?? throw new InvalidDataException("DXF LINE katmanı belirlenemedi.");
            if (!indexed.Children.IsEmpty || indexed.SequenceEnd is not null) throw new InvalidDataException("DXF LINE alt varlığı geçersiz.");
            Plane(e);
            if (!Exact(e,31,0) || !Exact(e,39,0)) throw new InvalidDataException("DXF LINE yüksekliği veya kalınlığı desteklenmiyor.");
            _ = One(e,10,true); _ = One(e,20,true); _ = One(e,11,true); _ = One(e,21,true);
            var a = new LineSourcePoint(Token(e,10,0),Token(e,20,0));
            var b = new LineSourcePoint(Token(e,11,0),Token(e,21,0));
            if (a.X.Canonical == b.X.Canonical && a.Y.Canonical == b.Y.Canonical)
                throw new InvalidDataException("DXF sıfır uzunluklu LINE desteklenmiyor.");
            long work = (long)2 * precisionPath.Count;
            if (precisionPath.Count == 0 || work > MaxPrecisionFrameVisits - precisionFrameVisits)
                throw new InvalidDataException("DXF hassasiyet doğrulama iş sınırını aşıyor.");
            precisionFrameVisits += (int)work;
            Point2 Map(LineSourcePoint source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((source.X.Value == bx.Value && source.X.Canonical != bx.Canonical) ||
                    (source.Y.Value == by.Value && source.Y.Canonical != by.Canonical))
                    throw new InvalidDataException("DXF kaynak koordinatları kayan nokta hassasiyetiyle çakışıyor.");
                Point2 point;
                try { point = transform.ApplyMillimeters(new(source.X.Value,source.Y.Value), millimetersPerUnit); }
                catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("DXF LINE koordinatı çalışma sınırını aşıyor.",ex); }
                if (!verifyPoint(source.X.Canonical,source.Y.Canonical,point))
                    throw new InvalidDataException("DXF LINE noktasının ölçü hassasiyeti doğrulanamadı.");
                return point;
            }
            var pa=Map(a); var pb=Map(b);
            if (pa == pb) throw new InvalidDataException("DXF LINE uçları hassasiyetle çakışıyor.");
            return (effective.EffectiveLayer, new ExpandedLine(pa,pb,a,b,e.SourceOrdinal));
        }

        void AddLineContours(string layer, List<ExpandedLine> lines)
        {
            // Endpoint adjacency alone misses crossings, T-junctions, near
            // contacts and partial overlap. Reject the whole transaction when
            // the existing bounded, cancellable intersection check is unsure.
            var edgesForCheck = new List<(Point2 A, Point2 B)>(lines.Count);
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                edgesForCheck.Add((line.A,line.B));
            }
            if (DxfFormat.HasAmbiguousLineConnectionsCancellable(edgesForCheck,cancellationToken))
                throw new InvalidDataException("DXF LINE çizgileri kesişiyor, örtüşüyor veya belirsiz bağlanıyor.");
            var origins = new Dictionary<Point2, LineSourcePoint>();
            var xOrigins = new Dictionary<double, string>();
            var yOrigins = new Dictionary<double, string>();
            var adjacency = new Dictionary<Point2, List<int>>();
            var uniqueEdges = new HashSet<(Point2, Point2)>();
            var vertexOrder = new Dictionary<Point2,int>();
            void Register(Point2 point, LineSourcePoint source, int edge)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckOrigin(xOrigins,source.X); CheckOrigin(yOrigins,source.Y);
                if (origins.TryGetValue(point, out var original))
                {
                    if (original.X.Canonical != source.X.Canonical || original.Y.Canonical != source.Y.Canonical)
                        throw new InvalidDataException("DXF LINE uçları kaynak hassasiyeti nedeniyle çakışıyor.");
                }
                else { origins.Add(point,source); vertexOrder.Add(point,vertexOrder.Count); }
                if (!adjacency.TryGetValue(point,out var edges)) adjacency[point] = edges = [];
                edges.Add(edge);
                if (edges.Count > 2) throw new InvalidDataException("DXF LINE bağlantısı dallanıyor; kontur belirsiz.");
            }
            for (var i=0; i<lines.Count; i++)
            {
                var line=lines[i]; Register(line.A,line.SourceA,i); Register(line.B,line.SourceB,i);
                var key = vertexOrder[line.A] < vertexOrder[line.B] ? (line.A,line.B) : (line.B,line.A);
                if (!uniqueEdges.Add(key)) throw new InvalidDataException("DXF LINE kenarı yinelenmiş; kontur belirsiz.");
            }
            var used = new bool[lines.Count];
            // Open chains begin at degree-one endpoints; then remaining loops.
            foreach (var start in adjacency.Keys.Where(p=>adjacency[p].Count==1).Concat(adjacency.Keys.Where(p=>adjacency[p].Count==2)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (adjacency[start].All(edge=>used[edge])) continue;
                var points = new List<Point2> { start };
                var current = start; var ordinal = -1;
                var sourceOrdinals=ImmutableArray.CreateBuilder<int>();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int next = -1;
                    foreach (var edge in adjacency[current]) if (!used[edge]) { next=edge; break; }
                    if (next < 0) break;
                    used[next]=true; var line=lines[next]; if (ordinal<0) ordinal=line.Ordinal;
                    sourceOrdinals.Add(line.Ordinal);
                    current = line.A == current ? line.B : line.A;
                    points.Add(current);
                    if (current == start) break;
                    if (points.Count > ProjectStorage.MaximumPointsPerPiece + 1)
                        throw new InvalidDataException("DXF LINE kontur nokta sınırını aşıyor.");
                }
                bool closed=points.Count>2 && points[^1]==start;
                if (closed) points.RemoveAt(points.Count-1);
                if (closed && points.Count<3) throw new InvalidDataException("DXF LINE kapalı konturu geçersiz.");
                if (result.Count>=ProjectStorage.MaximumPieces || points.Count>MaxPoints-totalPoints)
                    throw new InvalidDataException("DXF genişletilmiş kontur veya nokta sınırını aşıyor.");
                if (closed)
                {
                    ReserveClosedContourValidation(points.Count);
                    ValidateClosedContour(points);
                }
                totalPoints+=points.Count;
                result.Add(new DxfExpandedContour(points.ToImmutableArray(),closed,path.ToImmutableArray(),ordinal,layer) { EntityOccurrences=sourceOrdinals.ToImmutable() });
            }
        }

        void ExpandLegacyPolyline(DxfIndexedEntity indexed, DxfInsertTransform transform, NumberToken bx, NumberToken by, DxfInsertLayerContext? parentLayer, Func<string,string,Point2,bool> verifyPoint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var poly = indexed.Entity;
            CheckFields(poly, LegacyPolylineFields);
            CheckSubclasses(poly, "AcDbEntity", "AcDb2dPolyline");
            var polyLayer = Resolve(poly, parentLayer) ?? throw new InvalidDataException("DXF POLYLINE katmanı belirlenemedi.");
            Plane(poly);
            if (!Exact(poly,10,0) || !Exact(poly,20,0) || !Exact(poly,39,0) || !Exact(poly,40,0) || !Exact(poly,41,0))
                throw new InvalidDataException("DXF POLYLINE temel noktası, yüksekliği veya genişliği desteklenmiyor.");
            var flags = Integer(poly,70,0);
            if ((flags & ~(1 | 128)) != 0 || flags < 0)
                throw new InvalidDataException("DXF POLYLINE eğri, 3B veya mesh bayrağı desteklenmiyor.");
            // Group 66 is obsolete, but a malformed or duplicate value must not
            // silently acquire meaning. Actual ownership comes from the index.
            var group66 = One(poly,66);
            if (group66.HasValue && (!int.TryParse(group66.Value.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var flag66) || flag66 is not (0 or 1)))
                throw new InvalidDataException("DXF POLYLINE 66 alanı geçersiz.");
            if (indexed.SequenceEnd is null || indexed.Children.IsDefault || indexed.Children.Length > ProjectStorage.MaximumPointsPerPiece)
                throw new InvalidDataException("DXF POLYLINE köşeleri veya SEQEND kaydı geçersiz.");
            var minimum = (flags & 1) != 0 ? 3 : 2;
            if (indexed.Children.Length < minimum || result.Count >= ProjectStorage.MaximumPieces ||
                indexed.Children.Length > MaxPoints-totalPoints || precisionPath.Count == 0 ||
                (long)indexed.Children.Length * precisionPath.Count > MaxPrecisionFrameVisits-precisionFrameVisits)
                throw new InvalidDataException("DXF POLYLINE köşe veya hassasiyet iş sınırı geçersiz.");
            var end = indexed.SequenceEnd;
            if (end.Type != "SEQEND") throw new InvalidDataException("DXF POLYLINE SEQEND kaydı geçersiz.");
            CheckFields(end, LegacySequenceEndFields);
            CheckSubclasses(end, "AcDbEntity", "AcDbSequenceEnd");
            var endLayer = Resolve(end, parentLayer) ?? throw new InvalidDataException("DXF SEQEND katmanı belirlenemedi.");
            if (!string.Equals(endLayer.EffectiveLayer,polyLayer.EffectiveLayer,StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(endLayer.EffectiveLineType,polyLayer.EffectiveLineType,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DXF POLYLINE SEQEND katmanı veya çizgi tipi uyuşmuyor.");

            // Adapt the validated sequence into a temporary lightweight record;
            // raw vertex tokens and source entity occurrence remain intact, and
            // the existing exact decimal / cancellation / point checks are reused.
            var groups = ImmutableArray.CreateBuilder<DxfIndexGroup>();
            foreach (var group in poly.Groups)
                if (group.Code is 5 or 6 or 8 or 60 or 62 or 67 or 70 or 100 or 210 or 220 or 230 or 330 or 410 or 420 or 430)
                    groups.Add(group);
            var countText=indexed.Children.Length.ToString(CultureInfo.InvariantCulture);
            groups.Add(new DxfIndexGroup(90,countText,countText));
            foreach (var vertex in indexed.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visits > MaxVisits) throw new InvalidDataException("DXF POLYLINE işlem sınırını aşıyor.");
                if (vertex.Type != "VERTEX") throw new InvalidDataException("DXF POLYLINE alt varlığı VERTEX değil.");
                CheckFields(vertex,LegacyVertexFields);
                CheckSubclasses(vertex, "AcDbEntity", "AcDbVertex", "AcDb2dVertex");
                var vertexLayer=Resolve(vertex,parentLayer) ?? throw new InvalidDataException("DXF VERTEX katmanı belirlenemedi.");
                if (!string.Equals(vertexLayer.EffectiveLayer,polyLayer.EffectiveLayer,StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(vertexLayer.EffectiveLineType,polyLayer.EffectiveLineType,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("DXF POLYLINE köşe katmanı veya çizgi tipi uyuşmuyor.");
                Plane(vertex);
                if (Integer(vertex,70,0)!=0 || !Exact(vertex,40,0) || !Exact(vertex,41,0) || !Exact(vertex,42,0))
                    throw new InvalidDataException("DXF POLYLINE köşesi eğri, genişlik veya desteklenmeyen bayrak içeriyor.");
                groups.Add(One(vertex,10,true)!.Value);
                groups.Add(One(vertex,20,true)!.Value);
            }
            var adapted = new DxfIndexEntity("LWPOLYLINE",groups.ToImmutable(),poly.SourceOrdinal);
            ExpandPolyline(new DxfIndexedEntity(adapted,ImmutableArray<DxfIndexEntity>.Empty,null),transform,bx,by,parentLayer,verifyPoint);
            result[^1]=result[^1] with { ChildOccurrences=indexed.Children.Select(child=>child.SourceOrdinal).Append(end.SourceOrdinal).ToImmutableArray() };
        }

        void ExpandPolyline(DxfIndexedEntity indexed, DxfInsertTransform transform, NumberToken bx, NumberToken by, DxfInsertLayerContext? parentLayer, Func<string,string,Point2,bool> verifyPoint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visits > MaxVisits) throw new InvalidDataException("DXF INSERT işlem sınırını aşıyor.");
            var e = indexed.Entity;
            CheckFields(e, layerResolver is null ? PolyFields : StrictPolyFields);
            var effectiveLayer = Resolve(e, parentLayer);
            Plane(e);
            if (!indexed.Children.IsEmpty || indexed.SequenceEnd is not null) throw new InvalidDataException("DXF LWPOLYLINE alt varlığı geçersiz.");
            if (!Exact(e,38,0) || !Exact(e,39,0) || !Exact(e,43,0))
                throw new InvalidDataException("DXF LWPOLYLINE yükseklik veya genişlik desteklenmiyor.");
            var flags = Integer(e,70,0);
            if ((flags & ~(1 | 128)) != 0) throw new InvalidDataException("DXF LWPOLYLINE bayrakları desteklenmiyor.");
            bool closed = (flags & 1) != 0;
            var count = Integer(e,90,-1);
            if (count < (closed ? 3 : 2) || count > ProjectStorage.MaximumPointsPerPiece)
                throw new InvalidDataException("DXF LWPOLYLINE nokta sayısı geçersiz.");
            if (result.Count >= ProjectStorage.MaximumPieces || totalPoints > MaxPoints - count)
                throw new InvalidDataException("DXF genişletilmiş kontur veya nokta sınırını aşıyor.");
            if (closed && layerResolver is not null)
                ReserveClosedContourValidation(count);
            if (layerResolver is not null)
            {
                // Reserve work before allocating the point buffer or doing rational arithmetic.
                long work = (long)count * precisionPath.Count;
                if (precisionPath.Count == 0 || work > MaxPrecisionFrameVisits - precisionFrameVisits)
                    throw new InvalidDataException("DXF hassasiyet doğrulama iş sınırını aşıyor.");
                precisionFrameVisits += (int)work;
            }
            var points = ImmutableArray.CreateBuilder<Point2>(count);
            var rawPoints = new HashSet<Point2>();
            var outputPoints = new HashSet<Point2>();
            var xOrigins = new Dictionary<double, string>();
            var yOrigins = new Dictionary<double, string>();
            NumberToken? pendingX = null;
            foreach (var g in e.Groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (g.Code is 40 or 41 or 42)
                {
                    if (ParseNumber(g.Value).Canonical != "0") throw new InvalidDataException("DXF eğri veya genişlik desteklenmiyor.");
                }
                else if (g.Code == 10)
                {
                    if (pendingX.HasValue) throw new InvalidDataException("DXF LWPOLYLINE Y eksik.");
                    pendingX = ParseNumber(g.Value);
                }
                else if (g.Code == 20)
                {
                    if (!pendingX.HasValue || points.Count >= count) throw new InvalidDataException("DXF LWPOLYLINE koordinat sırası geçersiz.");
                    var sy = ParseNumber(g.Value); var sx = pendingX.Value;
                    if ((sx.Value == bx.Value && sx.Canonical != bx.Canonical) || (sy.Value == by.Value && sy.Canonical != by.Canonical))
                        throw new InvalidDataException("DXF kaynak koordinatları kayan nokta hassasiyetiyle çakışıyor.");
                    CheckOrigin(xOrigins, sx); CheckOrigin(yOrigins, sy);
                    var source = new Point2(sx.Value, sy.Value);
                    if (!rawPoints.Add(source)) throw new InvalidDataException("DXF kaynak noktaları yinelenmiş veya hassasiyetle çakışmış.");
                    Point2 mapped;
                    try { mapped = transform.ApplyMillimeters(source, millimetersPerUnit); }
                    catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("DXF dönüşmüş koordinat çalışma sınırını aşıyor.", ex); }
                    if (layerResolver is not null && !verifyPoint(sx.Canonical, sy.Canonical, mapped))
                        throw new InvalidDataException("DXF dönüşmüş noktanın ölçü hassasiyeti doğrulanamadı.");
                    if (!outputPoints.Add(mapped)) throw new InvalidDataException("DXF dönüşmüş noktalar hassasiyetle çakışıyor.");
                    points.Add(mapped); pendingX = null;
                }
            }
            if (pendingX.HasValue || points.Count != count) throw new InvalidDataException("DXF LWPOLYLINE nokta sayısı uyuşmuyor.");
            if (closed && layerResolver is not null)
                ValidateClosedContour(points);
            totalPoints += count;
            result.Add(new DxfExpandedContour(points.ToImmutable(), closed, path.ToImmutableArray(), e.SourceOrdinal, effectiveLayer?.EffectiveLayer) { EntityOccurrences=ImmutableArray.Create(e.SourceOrdinal) });
        }
        void ReserveClosedContourValidation(int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count > Geometry.MaxValidatedContourVertices)
                throw new InvalidDataException("DXF kapalı kontur doğrulama nokta sınırını aşıyor.");
            long pairs = (long)count * (count - 1) / 2;
            if (pairs > MaxContourValidationPairs - contourValidationPairs)
                throw new InvalidDataException("DXF kapalı kontur doğrulama iş sınırını aşıyor.");
            contourValidationPairs += pairs;
        }
        void ValidateClosedContour(IReadOnlyList<Point2> points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { Geometry.ValidateContour(points, cancellationToken); }
            catch (ArgumentException ex)
            { throw new InvalidDataException("DXF kapalı kontur geçersiz veya kendisiyle kesişiyor; aktarım yapılmadı.", ex); }
            catch (ArithmeticException ex)
            { throw new InvalidDataException("DXF kapalı konturun geometrisi doğrulanamadı; aktarım yapılmadı.", ex); }
        }
        DxfInsertLayerContext? Resolve(DxfIndexEntity entity, DxfInsertLayerContext? parentLayer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (layerResolver is null) { Approve(entity); return null; }
            var context = layerResolver.Resolve(entity, parentLayer, parentLayer is not null, cancellationToken);
            if (!context.Visible || !context.PlotEligible)
                throw new InvalidDataException("DXF görünmez veya plotlanmayan blok nesnesi doğrulanamadı: " + context.EffectiveLayer);
            return context;
        }
        void Approve(DxfIndexEntity entity)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (approveEntity is null || !approveEntity(entity)) throw new InvalidDataException("DXF katman veya görünürlük semantiği doğrulanamadı: " + entity.Type);
        }
    }

    private static void CheckFields(DxfIndexEntity e, HashSet<int> allowed)
    {
        foreach (var g in e.Groups) if (!allowed.Contains(g.Code)) throw new InvalidDataException($"DXF {e.Type} desteklenmeyen alan: {g.Code}.");
    }
    private static void CheckSubclasses(DxfIndexEntity e, params string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in e.Groups)
            if (group.Code == 100 && (!allowed.Contains(group.Value, StringComparer.Ordinal) || !seen.Add(group.Value)))
                throw new InvalidDataException($"DXF {e.Type} alt sınıfı desteklenmiyor veya yinelenmiş.");
    }
    private static void Plane(DxfIndexEntity e)
    {
        if (!Exact(e,30,0) || !Exact(e,210,0) || !Exact(e,220,0) || !Exact(e,230,1))
            throw new InvalidDataException("DXF 3B veya OCS dönüşümü desteklenmiyor.");
    }
    private static DxfIndexGroup? One(DxfIndexEntity e, int code, bool required = false)
    {
        DxfIndexGroup? found = null;
        foreach (var g in e.Groups) if (g.Code == code)
        {
            if (found.HasValue) throw new InvalidDataException($"DXF {e.Type} {code} alanı yinelenmiş.");
            found = g;
        }
        if (required && (!found.HasValue || string.IsNullOrWhiteSpace(found.Value.Value)))
            throw new InvalidDataException($"DXF {e.Type} {code} alanı eksik.");
        return found;
    }
    private static double Num(DxfIndexEntity e, int code, double fallback) => Token(e,code,fallback).Value;
    private static bool Exact(DxfIndexEntity e, int code, int expected)
    {
        var group = One(e, code);
        return !group.HasValue || ParseNumber(group.Value.Value).Canonical == (expected == 0 ? "0" : "1e0");
    }
    private static NumberToken Token(DxfIndexEntity e, int code, double fallback)
    {
        var g = One(e,code); return g.HasValue ? ParseNumber(g.Value.Value) : ParseNumber(fallback.ToString("R",CultureInfo.InvariantCulture));
    }
    private static int Integer(DxfIndexEntity e, int code, int fallback)
    {
        var g = One(e,code);
        if (!g.HasValue) return fallback;
        if (!int.TryParse(g.Value.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value))
            throw new InvalidDataException($"DXF {e.Type} {code} tam sayısı geçersiz.");
        return value;
    }
    private readonly record struct NumberToken(double Value, string Canonical);
    private static void CheckOrigin(Dictionary<double, string> origins, NumberToken coordinate)
    {
        if (origins.TryGetValue(coordinate.Value, out var original) && original != coordinate.Canonical)
            throw new InvalidDataException("DXF kaynak koordinatları kayan nokta hassasiyetiyle çakışıyor.");
        origins.TryAdd(coordinate.Value, coordinate.Canonical);
    }
    private static NumberToken ParseNumber(string raw)
    {
        if (raw.Length is 0 or > 256 || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new InvalidDataException("DXF sayısal değeri geçersiz.");
        var text = raw.AsSpan().Trim(); int i = 0; bool negative = false;
        if (text[i] is '+' or '-') negative = text[i++] == '-';
        var digits = new System.Text.StringBuilder(); int fractional = 0; bool dot = false;
        while (i < text.Length && text[i] is not ('e' or 'E'))
        {
            char ch = text[i++];
            if (ch == '.' && !dot) { dot = true; continue; }
            if (ch is < '0' or > '9') throw new InvalidDataException("DXF ondalık biçimi geçersiz.");
            digits.Append(ch); if (dot) fractional++;
        }
        if (digits.Length == 0) throw new InvalidDataException("DXF ondalık biçimi geçersiz.");
        int exponent = 0; bool expNegative = false;
        if (i < text.Length)
        {
            i++; if (i < text.Length && text[i] is '+' or '-') expNegative = text[i++] == '-';
            if (i == text.Length) throw new InvalidDataException("DXF üssü eksik.");
            while (i < text.Length)
            {
                char ch = text[i++]; if (ch is < '0' or > '9') throw new InvalidDataException("DXF üssü geçersiz.");
                if (exponent > 100000) throw new InvalidDataException("DXF üs sınırını aşıyor.");
                exponent = exponent * 10 + ch - '0';
            }
            if (expNegative) exponent = -exponent;
        }
        int first = 0, last = digits.Length - 1;
        while (first < digits.Length && digits[first] == '0') first++;
        if (first == digits.Length) return new(value,"0");
        while (digits[last] == '0') last--;
        return new(value,(negative ? "-" : "") + digits.ToString(first,last-first+1) + "e" + (exponent - fractional + digits.Length-1-last).ToString(CultureInfo.InvariantCulture));
    }
}
