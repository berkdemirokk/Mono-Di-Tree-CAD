using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

public sealed record DxfUnitInfo(int? Code, string Description, double? MillimetersPerUnit, bool IsReliable);
public enum DxfImportMode { General, Controlled }
/// <summary>Choices used to interpret a completed import. Layer names are approval inputs, not evidence of textile meaning.</summary>
public sealed record DxfImportParameters(DxfTextEncoding Encoding, double MillimetersPerUnit, DxfImportMode Mode,
    double? ComponentToleranceMm, ImmutableArray<string> ApprovedLayers)
{ public double? BulgeToleranceMm { get; init; } }
public sealed record DxfLayerMapping(Guid PieceId, string OriginalName, string LayerName)
{
    public bool Changed => !string.Equals(OriginalName, LayerName, StringComparison.Ordinal);
}
public enum DxfExportOmissionKind { PieceRole, Size, Material, Quantity, GrainlineUnsupported, GradingRules, NestingSnapshot, ImportProvenance, GrainlineReviewState, PieceIdentity }
/// <summary>An omitted field or unsupported feature; a null PieceId denotes project-level data.</summary>
public sealed record DxfExportOmission(Guid? PieceId, DxfExportOmissionKind Kind);
/// <summary>Read-only analysis of a copied project; this does not validate destination, machine use, or third-party interpretation.</summary>
public sealed record DxfExportAnalysis(string PieceFingerprint, ImmutableArray<DxfLayerMapping> Layers, ImmutableArray<DxfExportOmission> Omissions);
/// <summary>Counts of source group records whose metadata is not retained. Counts are not entity counts or a complete loss report.</summary>
public sealed record DxfMetadataLossSummary(int ExtendedDataGroups, int ApplicationDataGroups, int LineweightGroups, int TransparencyGroups);
/// <summary>Metadata group counts attached to one source code-0 record; contents and textile meanings are not interpreted.</summary>
public sealed record DxfMetadataSourceRecord(int SourceOrdinal, string EntityType, DxfMetadataLossSummary Counts);
/// <summary>Bounded source-record inventory; truncated reports omit further records.</summary>
public sealed record DxfMetadataSourceReport(ImmutableArray<DxfMetadataSourceRecord> Records, bool Truncated);
/// <summary>One uninterpreted, decoded DXF group. GroupOrdinal counts nonzero-code groups after the source record's code-0 marker. RawValue retains whitespace, not original byte encoding or line endings.</summary>
public sealed record DxfOpaqueGroup(int SourceOrdinal, int GroupOrdinal, int Code, string RawValue, bool InApplicationGroup);
/// <summary>Bounded prefix of 102 application groups and extended-data groups. Truncation makes this insufficient for reconstruction; source bytes remain authoritative.</summary>
public sealed record DxfOpaqueMetadata(ImmutableArray<DxfOpaqueGroup> Groups, bool Truncated);
/// <summary>Decoded, uninterpreted non-private group; whitespace is retained, not byte encoding or line endings.</summary>
public sealed record DxfAnnotationGroup(int Code, string RawValue)
{
    /// <summary>One-based position after the source code-0 marker, including intervening private groups. Zero denotes an unspecified position.</summary>
    public int GroupOrdinal { get; init; }
}
/// <summary>Source text/attribute record, not a verified size, piece name or placed annotation. Private 102 groups are separately reported as opaque metadata.</summary>
public sealed record DxfAnnotationRecord(int SourceOrdinal, string Section, string? Block, string EntityType, ImmutableArray<DxfAnnotationGroup> Groups)
{
    /// <summary>Layer-table visibility only, or null if missing, inherited or viewport visibility is uncertain. This does not establish entity visibility.</summary>
    public bool? LayerVisible { get; init; }
    /// <summary>Whether the layer has no explicit transparency group; null when its layer cannot be resolved.</summary>
    public bool? LayerHasNoTransparency { get; init; }
}
/// <summary>Bounded prefix of whole text/attribute records. Truncated means later records are omitted.</summary>
public sealed record DxfAnnotationReport(ImmutableArray<DxfAnnotationRecord> Records, bool Truncated);
/// <summary>Owned VERTEX or SEQEND groups, with private 102 groups separately retained in opaque metadata.</summary>
public sealed record DxfSupplementalChildRecord(int SourceOrdinal, string EntityType, ImmutableArray<DxfAnnotationGroup> Groups);
/// <summary>Uninterpreted source geometry; this is not a validated contour, notch, drill mark, or placed instance.</summary>
public sealed record DxfSupplementalGeometryRecord(int SourceOrdinal, string Section, string? Block, string EntityType, ImmutableArray<DxfAnnotationGroup> Groups)
{
    public ImmutableArray<DxfSupplementalChildRecord> Children { get; init; } = ImmutableArray<DxfSupplementalChildRecord>.Empty;
}
/// <summary>Bounded whole records for selected geometry. Inspection includes unassessed polyline candidates; this is not a complete loss report.</summary>
public sealed record DxfSupplementalGeometryReport(ImmutableArray<DxfSupplementalGeometryRecord> Records, bool Truncated)
{
    /// <summary>True only when general import assessed skipped polylines. False does not imply that no unsupported polyline exists.</summary>
    public bool SkippedPolylinesAssessed { get; init; }
    /// <summary>True when the report also includes polyline candidates that have not been evaluated for support.</summary>
    public bool IncludesUnassessedPolylineCandidates { get; init; }
}
/// <summary>One skipped source record in the general reader, with its first detected reason. SourceOrdinal is the zero-based index among parsed code-0 records, not a line number or DXF handle. A record is not necessarily a contour or a placed instance.</summary>
public sealed record DxfSkippedSourceRecord(int SourceOrdinal, string Section, string? Block, string Layer, string EntityType, string Reason);
/// <summary>Bounded sample of skipped records. Truncated means further records were omitted. This is not a complete semantic loss report.</summary>
public sealed record DxfSkippedSourceReport(ImmutableArray<DxfSkippedSourceRecord> Records, bool Truncated);
/// <summary>Uninterpreted groups of a skipped code-0 record, excluding private 102 groups retained separately.</summary>
public sealed record DxfSkippedSourceDetail(DxfSkippedSourceRecord Source, ImmutableArray<DxfAnnotationGroup> Groups);
/// <summary>Bounded prefix of whole skipped records; this is not a complete loss report or textile interpretation.</summary>
public sealed record DxfSkippedSourceDetailReport(ImmutableArray<DxfSkippedSourceDetail> Records, bool Truncated);
/// <summary>Original decoded non-private groups for one source record; this does not establish textile meaning or placement.</summary>
public sealed record DxfSourceGeometryRecord(int SourceOrdinal, string Section, string? Block, string EntityType, ImmutableArray<DxfAnnotationGroup> Groups)
{
    /// <summary>One-based positions parallel to Groups, counting all groups after the code-0 marker, including private groups. Opaque metadata may be truncated.</summary>
    public ImmutableArray<int> GroupOrdinals { get; init; } = ImmutableArray<int>.Empty;
}
/// <summary>Bounded prefix of whole records selected by the containing report. Private groups are retained separately.</summary>
public sealed record DxfSourceGeometryReport(ImmutableArray<DxfSourceGeometryRecord> Records, bool Truncated);
/// <summary>An actual source INSERT occurrence, outermost first.</summary>
public sealed record DxfSourceInsert(string BlockName, int SourceOrdinal);
/// <summary>Entity records for a created piece, not vertex correspondence. Ordinals index parsed code-0 records. In the general reader block geometry is a definition, not a placement.</summary>
public sealed record DxfPieceSource(Guid PieceId, string Section, string? Block, string Layer, string EntityType, ImmutableArray<int> SourceOrdinals)
{
    /// <summary>Actual INSERT path for controlled placements; empty for direct geometry and general-reader definitions.</summary>
    public ImmutableArray<DxfSourceInsert> InsertPath { get; init; } = ImmutableArray<DxfSourceInsert>.Empty;
    /// <summary>Legacy POLYLINE VERTEX and SEQEND record ordinals in source order, not a point correspondence.</summary>
    public ImmutableArray<int> ChildSourceOrdinals { get; init; } = ImmutableArray<int>.Empty;
}
public sealed record DxfImportResult(ProjectDocument Project, List<string> Warnings)
{
    public DxfUnitInfo Units { get; init; } = new(null, "Birim başlığı yok", null, false);
    /// <summary>Whether the legacy import encountered an INSERT it did not apply. Null means this result did not come from a completed import.</summary>
    public bool? SkippedInsertEntities { get; init; }
    /// <summary>Whether a skipped INSERT could affect geometry included by the legacy import. Null means the result is not a completed import. Excluded records are ignored only when their exclusion metadata is unambiguous.</summary>
    public bool? SkippedRelevantInsertEntities { get; init; }
    /// <summary>Whether the legacy import included geometry directly from block definitions. Null means this result did not come from a completed import.</summary>
    public bool? ImportedBlockDefinitions { get; init; }
    /// <summary>SHA-256 of exactly the bytes read from the supplied stream position; null for manually constructed results.</summary>
    public string? SourceSha256 { get; init; }
    /// <summary>Number of bytes in that captured input; null for manually constructed results.</summary>
    public long? SourceByteLength { get; init; }
    /// <summary>Actual import choices; null for manual results and preliminary inspections.</summary>
    public DxfImportParameters? ImportParameters { get; internal init; }
    /// <summary>Snapshot-wide group counts, or null when not inspected. These do not determine textile meaning.</summary>
    public DxfMetadataLossSummary? MetadataLoss { get; internal init; }
    /// <summary>Source-record metadata locations; null when no inspection was performed.</summary>
    public DxfMetadataSourceReport? MetadataSources { get; internal init; }
    /// <summary>Uninterpreted metadata captured from this snapshot, or null when not inspected. It is not persisted to the project.</summary>
    public DxfOpaqueMetadata? OpaqueMetadata { get; internal init; }
    /// <summary>Uninterpreted source text and attribute records; not persisted in the project.</summary>
    public DxfAnnotationReport? Annotations { get; internal init; }
    /// <summary>Selected unsupported point and curve records; not project geometry or a complete loss report.</summary>
    public DxfSupplementalGeometryReport? SupplementalGeometry { get; internal init; }
    internal string? AnnotationSnapshotSha256 { get; init; }
    internal long? AnnotationSnapshotByteLength { get; init; }
    internal bool? CapturedSkippedRelevantInsertEntities { get; init; }
    internal bool? CapturedImportedBlockDefinitions { get; init; }
    internal ImmutableArray<string> CapturedWarnings { get; init; }
    /// <summary>General-reader skip records, or null for controlled import and uninspected results.</summary>
    public DxfSkippedSourceReport? SkippedSourceRecords { get; internal init; }
    /// <summary>Uninterpreted skipped source groups from general import; null means not assessed.</summary>
    public DxfSkippedSourceDetailReport? SkippedSourceDetails { get; internal init; }
    /// <summary>Historical source geometry groups, not saved in the project; null means no inventory was captured.</summary>
    public DxfSourceGeometryReport? SourceGeometry { get; internal init; }
    /// <summary>Bounded, uninterpreted OBJECTS-section records; object references and meanings are not resolved.</summary>
    public DxfSourceGeometryReport? SourceObjects { get; internal init; }
    /// <summary>Historical piece source records at import time; not a semantic classification. Compare PieceSourcesFingerprint before associating them with the mutable Project.</summary>
    public ImmutableArray<DxfPieceSource>? PieceSources { get; internal init; }
    /// <summary>Fingerprint of the piece state when source records were captured; null for manually constructed results.</summary>
    public string? PieceSourcesFingerprint { get; internal init; }
}

/// <summary>A layer found in the source table, not approval of its cutting meaning.</summary>
public sealed record DxfBlockLayerInspection(string Name, bool? Hidden, bool? PlotEnabled, string? LineType);
/// <summary>Information from one bounded snapshot; no geometry is imported or validated by this inspection.</summary>
public sealed class DxfBlockPlacementInspection
{
    public string SourceSha256 { get; }
    public long SourceByteLength { get; }
    public DxfTextEncoding Encoding { get; }
    public ImmutableArray<DxfBlockLayerInspection> Layers { get; }
    /// <summary>True if the ENTITIES section contains an INSERT; model-space and visibility are not confirmed by inspection.</summary>
    public bool HasModelInsert { get; }
    public ImmutableArray<string> Limitations { get; }
    /// <summary>Decoding, header and metadata warnings identified during inspection.</summary>
    public ImmutableArray<string> Warnings { get; }
    /// <summary>Locations of selected metadata group types in the inspected snapshot; not their decoded meanings.</summary>
    public DxfMetadataSourceReport MetadataSources { get; }
    /// <summary>Bounded, uninterpreted decoded metadata; it may contain application-private information.</summary>
    public DxfOpaqueMetadata OpaqueMetadata { get; }
    /// <summary>Text/attribute source records; their textile meaning and placement are not validated.</summary>
    public DxfAnnotationReport Annotations { get; }
    /// <summary>Uninterpreted source point and curve records, not validated geometry.</summary>
    public DxfSupplementalGeometryReport SupplementalGeometry { get; }
    /// <summary>Bounded source geometry and INSERT records, without interpreting or validating placement.</summary>
    public DxfSourceGeometryReport? SourceGeometry { get; internal init; }
    /// <summary>Other source entities captured for review before import; their meaning and support are unassessed.</summary>
    public DxfSourceGeometryReport? OtherSourceRecords { get; internal init; }
    /// <summary>Bounded OBJECTS-section records, without resolving references or meanings.</summary>
    public DxfSourceGeometryReport? SourceObjects { get; internal init; }
    internal DxfBlockPlacementInspection(string sourceSha256, long sourceByteLength, DxfTextEncoding encoding,
        ImmutableArray<DxfBlockLayerInspection> layers, bool hasModelInsert,
        ImmutableArray<string> limitations, ImmutableArray<string> warnings, DxfMetadataSourceReport metadataSources, DxfOpaqueMetadata opaqueMetadata, DxfAnnotationReport annotations, DxfSupplementalGeometryReport supplementalGeometry)
    {
        SourceSha256=sourceSha256; SourceByteLength=sourceByteLength; Encoding=encoding;
        Layers=layers; HasModelInsert=hasModelInsert; Limitations=limitations; Warnings=warnings; MetadataSources=metadataSources; OpaqueMetadata=opaqueMetadata; Annotations=annotations; SupplementalGeometry=supplementalGeometry;
    }
}

/// <summary>Text decoding must be explicitly selected; legacy encodings are never guessed.</summary>
public enum DxfTextEncoding { Utf8, Windows1254, Windows1252 }

/// <summary>ASCII 2D exchange subset. AAMA/ASTM metadata and INSERT transforms are not implemented.</summary>
public static class DxfFormat
{
    private readonly record struct Pair(int Code, string Value, string RawValue)
    {
        public int GroupOrdinal { get; init; }
    }
    // Keep the original decimal tokens so double rounding cannot turn distinct
    // source coordinates into an apparently exact junction or repeated vertex.
    private readonly record struct Coordinate(string Text,double Value);
    private readonly record struct SourcePoint(Coordinate X,Coordinate Y);
    private readonly record struct CanonicalDecimal(bool Negative,string Digits,int Exponent);
    private sealed record Entity(string Type, List<Pair> Data)
    {
        public bool HasOpaqueMetadata { get; set; }
        public string Text(int code, string fallback = "") => Data.FirstOrDefault(p => p.Code == code).Value ?? fallback;
        public string LayerName()
        {
            var matches=Data.Where(p=>p.Code==8).ToArray();
            if(matches.Length>1)throw new InvalidDataException($"DXF {Type} için katman alanı yinelenmiş.");
            return matches.Length==0 ? "0" : DecodeLayerName(matches[0].RawValue);
        }
        public double Number(int code, double fallback = 0)
        {
            var matches=Data.Where(p=>p.Code==code).ToArray();
            if(matches.Length>1)throw new InvalidDataException($"DXF {Type} için {code} alanı yinelenmiş.");
            return matches.Length==0 ? fallback : Parse(matches[0].Value);
        }
        public int Flags(int code)
        {
            var matches=Data.Where(p=>p.Code==code).ToArray();
            if(matches.Length>1)throw new InvalidDataException($"DXF {Type} için {code} bayrağı yinelenmiş.");
            if(matches.Length==0)return 0;
            if(!short.TryParse(matches[0].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value) || value<0)
                throw new InvalidDataException($"DXF {Type} için {code} bayrağı geçersiz; negatif olmayan 16 bit tam sayı gerekir.");
            return value;
        }
        public double RequiredNumber(int code)
        {
            var matches = Data.Where(p => p.Code == code).ToArray();
            if (matches.Length > 1) throw new InvalidDataException($"DXF {Type} için {code} koordinatı yinelenmiş.");
            var pair = matches.FirstOrDefault();
            if (pair.Value is null) throw new InvalidDataException($"DXF {Type} için gerekli {code} koordinatı eksik.");
            return Parse(pair.Value);
        }
        public Coordinate RequiredCoordinate(int code)
        {
            var matches=Data.Where(p=>p.Code==code).ToArray();
            if(matches.Length>1)throw new InvalidDataException($"DXF {Type} için {code} koordinatı yinelenmiş.");
            if(matches.Length==0)throw new InvalidDataException($"DXF {Type} için gerekli {code} koordinatı eksik.");
            return ReadCoordinate(matches[0].Value);
        }
    }
    private sealed class LineGroupComparer : IEqualityComparer<(string Section,int BlockOccurrence,string Block,string Layer,string LineType,string Color,string Appearance)>
    {
        public bool Equals((string Section,int BlockOccurrence,string Block,string Layer,string LineType,string Color,string Appearance) a,(string Section,int BlockOccurrence,string Block,string Layer,string LineType,string Color,string Appearance) b)
            => a.BlockOccurrence==b.BlockOccurrence && StringComparer.Ordinal.Equals(a.Section,b.Section)
                && StringComparer.Ordinal.Equals(a.Block,b.Block) && StringComparer.OrdinalIgnoreCase.Equals(a.Layer,b.Layer)
                && StringComparer.Ordinal.Equals(a.LineType,b.LineType) && StringComparer.Ordinal.Equals(a.Color,b.Color) && StringComparer.Ordinal.Equals(a.Appearance,b.Appearance);
        public int GetHashCode((string Section,int BlockOccurrence,string Block,string Layer,string LineType,string Color,string Appearance) value)
            => HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Section),value.BlockOccurrence,
                StringComparer.Ordinal.GetHashCode(value.Block),StringComparer.OrdinalIgnoreCase.GetHashCode(value.Layer),StringComparer.Ordinal.GetHashCode(value.LineType),StringComparer.Ordinal.GetHashCode(value.Color),StringComparer.Ordinal.GetHashCode(value.Appearance));
    }
    private static double Parse(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
            throw new InvalidDataException("DXF sayısal değeri geçersiz: " + value);
        return n;
    }
    // A bounded ASCII decimal grammar keeps exact comparisons predictable even
    // for hostile 64-MB input. Decimal normalization is deferred until a
    // floating-point collision actually requires an exact comparison.
    private static Coordinate ReadCoordinate(string text)
    {
        if(text.Length==0 || text.Length>256)throw new InvalidDataException("DXF koordinat metni 256 karakter sınırını aşıyor veya boş.");
        var i=0;
        if(text[i] is '+' or '-')i++;
        var digits=0;var dot=false;
        while(i<text.Length && text[i] is not ('e' or 'E'))
        {
            var c=text[i++];
            if(c=='.' && !dot) { dot=true;continue; }
            if(c<'0' || c>'9')throw new InvalidDataException("DXF koordinatının ondalık biçimi geçersiz.");
            digits++;
        }
        if(digits==0)throw new InvalidDataException("DXF koordinatının ondalık biçimi geçersiz.");
        if(i<text.Length)
        {
            i++;if(i<text.Length && text[i] is '+' or '-')i++;
            var exponentDigits=0;var magnitude=0;
            while(i<text.Length)
            {
                var c=text[i++];if(c<'0' || c>'9')throw new InvalidDataException("DXF koordinat üssü geçersiz.");
                exponentDigits++;magnitude=magnitude*10+(c-'0');
                if(magnitude>1_000_000)throw new InvalidDataException("DXF koordinat üssü desteklenen sınırı aşıyor.");
            }
            if(exponentDigits==0)throw new InvalidDataException("DXF koordinat üssü geçersiz.");
        }
        return new Coordinate(text,Parse(text));
    }
    private static CanonicalDecimal Canonicalize(Coordinate coordinate)
    {
        var text=coordinate.Text;var i=0;var negative=false;
        if(text[i] is '+' or '-') { negative=text[i]=='-';i++; }
        var digits=new StringBuilder(text.Length);var fractional=0;var dot=false;
        while(i<text.Length && text[i] is not ('e' or 'E'))
        {
            var c=text[i++];if(c=='.') { dot=true;continue; }
            digits.Append(c);if(dot)fractional++;
        }
        var exponent=0;
        if(i<text.Length)
        {
            i++;var exponentNegative=false;
            if(i<text.Length && text[i] is '+' or '-') { exponentNegative=text[i]=='-';i++; }
            while(i<text.Length)exponent=exponent*10+(text[i++]-'0');
            if(exponentNegative)exponent=-exponent;
        }
        var first=0;while(first<digits.Length && digits[first]=='0')first++;
        if(first==digits.Length)return new CanonicalDecimal(false,"0",0);
        var last=digits.Length-1;while(digits[last]=='0')last--;
        return new CanonicalDecimal(negative,digits.ToString(first,last-first+1),exponent-fractional+(digits.Length-1-last));
    }
    private static bool SameCoordinate(Coordinate left,Coordinate right)
        => string.Equals(left.Text,right.Text,StringComparison.Ordinal) || Canonicalize(left)==Canonicalize(right);
    // A nonzero decimal can underflow to binary64 zero. Such a value must not
    // turn a curve or a wide polyline into an apparently straight contour.
    private static bool IsNonzeroDecimal(string text)
    {
        var value=ReadCoordinate(text);
        return value.Value!=0 || Canonicalize(value).Digits!="0";
    }
    private static bool SameSourcePoint(SourcePoint left,SourcePoint right)
        => SameCoordinate(left.X,right.X) && SameCoordinate(left.Y,right.Y);
    private static string F(double n) => n.ToString("G17", CultureInfo.InvariantCulture);
    private static string DisplayFactor(double n) => n.ToString("R", CultureInfo.InvariantCulture);
    public static DxfImportResult Import(string path, double millimetersPerUnit = 1)
        => Import(path, millimetersPerUnit, CancellationToken.None);

    public static DxfImportResult Import(string path, double millimetersPerUnit, CancellationToken cancellationToken)
        => Import(path,millimetersPerUnit,DxfTextEncoding.Utf8,cancellationToken);

    public static DxfImportResult Import(string path, double millimetersPerUnit, DxfTextEncoding encoding, CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Import(input, Path.GetFileNameWithoutExtension(path), millimetersPerUnit, encoding, cancellationToken);
    }
    public static DxfImportResult Import(Stream input, string projectName, double millimetersPerUnit = 1)
        => Import(input, projectName, millimetersPerUnit, CancellationToken.None);

    public static DxfImportResult Import(Stream input, string projectName, double millimetersPerUnit, CancellationToken cancellationToken)
        => Import(input,projectName,millimetersPerUnit,DxfTextEncoding.Utf8,cancellationToken);

    /// <summary>Reads with the explicitly selected encoding. No fallback or guessing is performed.</summary>
    public static DxfImportResult Import(Stream input, string projectName, double millimetersPerUnit, DxfTextEncoding encoding, CancellationToken cancellationToken=default)
        => ImportCore(input,projectName,millimetersPerUnit,encoding,null,cancellationToken);

    /// <summary>Explicit experimental 2D bulge import; tolerance is a nominal sampling target, not a production guarantee.</summary>
    public static DxfImportResult ImportWithBulgeApproximation(Stream input, string projectName, double millimetersPerUnit,
        DxfTextEncoding encoding, double toleranceMm, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(toleranceMm) || toleranceMm <= 0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        return ImportCore(input, projectName, millimetersPerUnit, encoding, null, cancellationToken, null, toleranceMm);
    }

    /// <summary>
    /// Opts into the restricted model-space and INSERT importer. The caller explicitly approves
    /// the cutting meaning of each named layer and an absolute, per-coordinate
    /// tolerance in millimeters. The factor's invariant round-trip decimal is
    /// used by the exact placement checker; it is not an original input token.
    /// This accepts only the validated subset, not general DXF or AAMA semantics.
    /// </summary>
    public static DxfImportResult ImportWithBlockPlacements(Stream input, string projectName,
        double millimetersPerUnit, DxfTextEncoding encoding, double componentToleranceMm,
        IReadOnlyCollection<string> approvedLayers, CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(approvedLayers);
        if (!double.IsFinite(componentToleranceMm) || componentToleranceMm < 0)
            throw new ArgumentOutOfRangeException(nameof(componentToleranceMm));
        var names=CopyApprovedLayers(approvedLayers,cancellationToken);
        return ImportCore(input,projectName,millimetersPerUnit,encoding,new StrictInsertOptions(componentToleranceMm,names),cancellationToken);
    }

    /// <summary>Inspect layer-table data from one snapshot without importing geometry. The stream position is restored.</summary>
    public static DxfBlockPlacementInspection InspectBlockPlacements(Stream input, DxfTextEncoding encoding, CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanSeek) throw new InvalidDataException("DXF akışı konumlandırılamıyor.");
        var start=input.Position;
        DxfBlockPlacementInspection? result=null;
        try
        {
            _=ImportCore(input,"DXF incelemesi",1,encoding,null,cancellationToken,found=>result=found);
            return result ?? throw new InvalidDataException("DXF katman incelemesi tamamlanamadı.");
        }
        finally { input.Position=start; }
    }

    /// <summary>Imports only if the input bytes match the inspection snapshot.</summary>
    public static DxfImportResult ImportWithBlockPlacementsVerified(Stream input, string projectName,
        double millimetersPerUnit, DxfTextEncoding encoding, double componentToleranceMm,
        IReadOnlyCollection<string> approvedLayers, string expectedSha256, long expectedByteLength,
        CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(approvedLayers);
        if (!double.IsFinite(componentToleranceMm) || componentToleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(componentToleranceMm));
        if (expectedSha256 is null || expectedSha256.Length!=64 || !expectedSha256.All(Uri.IsHexDigit)) throw new ArgumentException("DXF kaynak özeti geçersiz.",nameof(expectedSha256));
        if (expectedByteLength<0 || expectedByteLength>64L*1024*1024) throw new ArgumentOutOfRangeException(nameof(expectedByteLength));
        var names=CopyApprovedLayers(approvedLayers,cancellationToken);
        return ImportCore(input,projectName,millimetersPerUnit,encoding,
            new StrictInsertOptions(componentToleranceMm,names,expectedSha256,expectedByteLength),cancellationToken);
    }

    /// <summary>Uses the inspected encoding and snapshot identity, binding a later selection to that inspection.</summary>
    public static DxfImportResult ImportWithInspectedBlockPlacements(Stream input, string projectName,
        double millimetersPerUnit, double componentToleranceMm, IReadOnlyCollection<string> approvedLayers,
        DxfBlockPlacementInspection inspection, CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return ImportWithBlockPlacementsVerified(input,projectName,millimetersPerUnit,inspection.Encoding,
            componentToleranceMm,approvedLayers,inspection.SourceSha256,inspection.SourceByteLength,cancellationToken);
    }

    private static HashSet<string> CopyApprovedLayers(IReadOnlyCollection<string> approvedLayers,CancellationToken token)
    {
        var selections=DxfBoundedSelections.Copy(approvedLayers,ProjectStorage.MaximumPieces,token);
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long characters=0;
        foreach(var name in selections)
        {
            token.ThrowIfCancellationRequested();
            if(name is null || name.Length>DxfSourceSnapshotCheck.MaximumBytes-characters)
                throw new InvalidDataException("Onaylı katman adlarının toplam boyut sınırı aşıldı.");
            characters+=name.Length;
            if(string.IsNullOrWhiteSpace(name) || !result.Add(name))
                throw new ArgumentException("Onaylı katman adları boş veya yinelenmiş olamaz.",nameof(approvedLayers));
        }
        return result;
    }

    private sealed record StrictInsertOptions(double ComponentToleranceMm, HashSet<string> ApprovedLayers, string? ExpectedSha256=null, long? ExpectedByteLength=null);
    private static DxfImportResult ImportCore(Stream input, string projectName, double millimetersPerUnit,
        DxfTextEncoding encoding, StrictInsertOptions? strict, CancellationToken cancellationToken,
        Action<DxfBlockPlacementInspection>? inspectionSink=null, double? bulgeToleranceMm=null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!Enum.IsDefined(encoding))throw new ArgumentOutOfRangeException(nameof(encoding));
        if (!double.IsFinite(millimetersPerUnit) || millimetersPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(millimetersPerUnit));
        const int maxBytes = 64 * 1024 * 1024;
        if (!input.CanRead || !input.CanSeek || input.Length - input.Position > maxBytes) throw new InvalidDataException("DXF akışı desteklenmiyor veya 64 MB sınırını aşıyor.");
        // Bound bytes actually read as well as the initial length: another writer may grow a file.
        // Header and geometry are parsed from the same snapshot; caller retains ownership of input.
        using var snapshot = new MemoryStream();
        using var sourceHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            read = input.Read(buffer, 0, buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0) break;
            if (snapshot.Length + read > maxBytes) throw new InvalidDataException("DXF dosyası 64 MB sınırını aşıyor.");
            snapshot.Write(buffer, 0, read);
            sourceHash.AppendData(buffer,0,read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var sourceSha256=Convert.ToHexString(sourceHash.GetHashAndReset());
        var sourceByteLength=snapshot.Length;
        if(strict?.ExpectedSha256 is { } expectedHash &&
            (!string.Equals(sourceSha256,expectedHash,StringComparison.OrdinalIgnoreCase) || sourceByteLength!=strict.ExpectedByteLength))
            throw new InvalidDataException("DXF kaynağı katman incelemesinden sonra değişti; dosyayı yeniden inceleyin.");
        snapshot.Position = 0;
        // BOMs must agree with the explicit selection. Never auto-detect or
        // replace malformed sequences in names and identifiers.
        var raw=snapshot.GetBuffer();var length=snapshot.Length;
        var utf8Bom=length>=3 && raw[0]==0xEF && raw[1]==0xBB && raw[2]==0xBF;
        var utf16Bom=length>=2 && ((raw[0]==0xFF && raw[1]==0xFE) || (raw[0]==0xFE && raw[1]==0xFF));
        var utf32BeBom=length>=4 && raw[0]==0 && raw[1]==0 && raw[2]==0xFE && raw[3]==0xFF;
        if(utf16Bom || utf32BeBom)throw new InvalidDataException("DXF UTF-16/UTF-32 BOM içeriyor; bu kodlamalar desteklenmiyor.");
        if(utf8Bom && encoding!=DxfTextEncoding.Utf8)throw new InvalidDataException("DXF UTF-8 BOM ile seçilen ANSI kodlaması çelişiyor; doğru kodlamayı seçin.");
        if(utf8Bom)snapshot.Position=3;
        var alsoValidUtf8=false;
        if(encoding!=DxfTextEncoding.Utf8)
        {
            // This is a warning only, never a choice or fallback. Scan in
            // bounded chunks so cancellation and memory limits still apply.
            try
            {
                var utf8Probe=new UTF8Encoding(false,true).GetDecoder();
                for(var offset=0;offset<length;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count=(int)Math.Min(8192,length-offset);
                    utf8Probe.GetCharCount(raw,offset,count,offset+count==length);offset+=count;
                }
                alsoValidUtf8=true;
            }
            catch(DecoderFallbackException) { }
        }
        var decoder=encoding==DxfTextEncoding.Utf8 ? new UTF8Encoding(false,true) :
            CodePagesEncodingProvider.Instance.GetEncoding(encoding==DxfTextEncoding.Windows1254 ? 1254 : 1252,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback)
                ?? throw new InvalidOperationException("ANSI kod çözücüsü kullanılamıyor.");
        using var reader = new StreamReader(snapshot, decoder, false, 4096, leaveOpen: true);
        var lines = new List<string>();
        try
        {
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lines.Add(line);
                if (lines.Count > 1_000_000) throw new InvalidDataException("DXF grup sayısı sınırı aşıldı.");
            }
        }
        catch(DecoderFallbackException ex)
        { throw new InvalidDataException(encoding==DxfTextEncoding.Utf8
            ? "DXF metni geçerli UTF-8 değil. Dosya eski ANSI kodlamasındaysa uygun kodlamayı açıkça seçin veya UTF-8 DXF olarak yeniden dışa aktarın."
            : "DXF metni seçilen ANSI kodlamasında geçerli değil; kodlamayı doğrulayın.",ex); }
        var pairs = new List<Pair>();
        for (int i = 0; i < lines.Count; i += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i == lines.Count - 1 && string.IsNullOrWhiteSpace(lines[i])) break;
            if (!int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                throw new InvalidDataException($"ASCII DXF grup kodu geçersiz: {i + 1}. satır.");
            if (i + 1 >= lines.Count)
                throw new InvalidDataException($"ASCII DXF grup değeri eksik: {i + 1}. satırdaki kodun ardından değer satırı bekleniyor.");
            pairs.Add(new Pair(code, lines[i + 1].Trim(), lines[i + 1]));
        }
        var eofIndex = pairs.FindIndex(p => p.Code == 0 && p.Value == "EOF");
        if (eofIndex < 0) throw new InvalidDataException("DXF EOF kaydı yok.");
        if (eofIndex != pairs.Count - 1)
            throw new InvalidDataException($"DXF EOF kaydından sonra veri var: {2 * (eofIndex + 1) + 1}. satır; dosyanın sonu belirsiz.");
        var extendedDataGroups=0; var lineweightGroups=0; var transparencyGroups=0;
        var warnings = new List<string>();
        warnings.Add("ASTM D6673 tarzı alışverişte serileme tablosu ayrı bir ASCII dosyasında olabilir; bu DXF işlemi o dosyayı aramaz veya içe almaz.");
        if(encoding==DxfTextEncoding.Windows1254)warnings.Add("DXF metni kullanıcının seçtiği Windows-1254 (Türkçe ANSI) ile çözüldü; kodlamanın kaynak dosyayla eşleştiğini doğrulayın.");
        if(encoding==DxfTextEncoding.Windows1252)warnings.Add("DXF metni kullanıcının seçtiği Windows-1252 (Batı Avrupa ANSI) ile çözüldü; kodlamanın kaynak dosyayla eşleştiğini doğrulayın.");
        if(alsoValidUtf8)warnings.Add("Aynı DXF baytları UTF-8 olarak da geçerli; hangi kodlamanın doğru olduğunu kaynak dosyada doğrulayın.");
        var entities = new List<Entity>();
        // Group 102 contains application-private data. Codes in such a group
        // must not masquerade as entity coordinates, normals, widths or flags.
        // Parse the scope once before any entity-specific reader sees the data.
        var applicationDepth=0;var applicationDataGroups=0;var hasApplicationData=false;var hasViewportData=false;
        const int maxMetadataSourceRecords=4096;
        var metadataRecords=ImmutableArray.CreateBuilder<DxfMetadataSourceRecord>();
        var metadataRecordsTruncated=false;
        const int maxOpaqueGroups=32768, maxOpaqueCharacters=1_000_000;
        var opaqueGroups=ImmutableArray.CreateBuilder<DxfOpaqueGroup>();
        var opaqueTruncated=false;var opaqueCharacters=0;var groupOrdinal=0;
        int recordExtended=0,recordApplication=0,recordLineweight=0,recordTransparency=0;
        void FlushMetadataRecord()
        {
            if(entities.Count>0 && (recordExtended!=0 || recordApplication!=0))
                entities[^1].HasOpaqueMetadata=true;
            if(entities.Count>0 && (recordExtended!=0 || recordApplication!=0 || recordLineweight!=0 || recordTransparency!=0))
            {
                if(metadataRecords.Count<maxMetadataSourceRecords)
                    metadataRecords.Add(new(entities.Count-1,entities[^1].Type,new(recordExtended,recordApplication,recordLineweight,recordTransparency)));
                else metadataRecordsTruncated=true;
            }
            recordExtended=recordApplication=recordLineweight=recordTransparency=0;
        }
        foreach (var pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(pair.Code==0)groupOrdinal=0;
            else if(entities.Count==0 && (pair.Code==102 || pair.Code>=1000))opaqueTruncated=true;
            else if(entities.Count>0)
            {
                groupOrdinal++;
                var privateGroup=applicationDepth>0 || pair.Code==102;
                if(!opaqueTruncated && (privateGroup || pair.Code>=1000))
                {
                    if(opaqueGroups.Count>=maxOpaqueGroups || pair.RawValue.Length>maxOpaqueCharacters-opaqueCharacters)
                        opaqueTruncated=true;
                    else
                    {
                        opaqueGroups.Add(new(entities.Count-1,groupOrdinal,pair.Code,pair.RawValue,privateGroup));
                        opaqueCharacters+=pair.RawValue.Length;
                    }
                }
            }
            // Private 102 payload codes have application-defined meanings;
            // do not classify them as ordinary entity appearance or XDATA.
            if(applicationDepth==0 && pair.Code!=102)
            {
                if(pair.Code>=1000) { extendedDataGroups++;if(entities.Count>0)recordExtended++; }
                if(pair.Code==370) { lineweightGroups++;if(entities.Count>0)recordLineweight++; }
                if(pair.Code==440) { transparencyGroups++;if(entities.Count>0)recordTransparency++; }
            }
            if (pair.Code == 0)
            {
                if(applicationDepth!=0)throw new InvalidDataException("DXF uygulama veri grubu varlık bitmeden kapatılmamış.");
                FlushMetadataRecord();
                entities.Add(new Entity(pair.Value, []));
                if(pair.Value is "VPORT" or "VIEWPORT")hasViewportData=true;
            }
            else if (entities.Count > 0)
            {
                if(pair.Code==102)
                {
                    if(pair.Value.StartsWith('{')) { applicationDepth++;applicationDataGroups++;recordApplication++;hasApplicationData=true; }
                    else if(pair.Value=="}")
                    {
                        if(applicationDepth==0)throw new InvalidDataException("DXF uygulama veri grubu beklenmedik biçimde kapanıyor.");
                        applicationDepth--;
                    }
                    else throw new InvalidDataException("DXF uygulama veri grubu sınırı geçersiz.");
                }
                else if(applicationDepth==0)entities[^1].Data.Add(pair with { GroupOrdinal=groupOrdinal });
            }
        }
        if(applicationDepth!=0)throw new InvalidDataException("DXF uygulama veri grubu kapatılmamış.");
        FlushMetadataRecord();
        // Unit headers must use the same private-group exclusion as entity
        // parsing. Private group 9/70 values are not HEADER variables.
        var headerPairs=pairs;
        if(hasApplicationData)
        {
            headerPairs=[];
            var depth=0;var started=false;
            foreach(var pair in pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(pair.Code==0)started=true;
                if(!started) { headerPairs.Add(pair);continue; }
                if(pair.Code==102)
                {
                    if(pair.Value.StartsWith('{'))depth++;
                    else if(pair.Value=="}")depth--;
                }
                else if(depth==0)headerPairs.Add(pair);
            }
        }
        var units=ReadUnits(headerPairs,cancellationToken);
        if (units.IsReliable && units.MillimetersPerUnit is double expected && Math.Abs(expected - millimetersPerUnit) <= Math.Abs(expected) * 1e-12)
            warnings.Add($"DXF birim başlığı mevcut; {units.Description}, seçilen ölçek 1 dosya birimi = {DisplayFactor(millimetersPerUnit)} mm. Ölçüyü doğrulayın.");
        else if (units.MillimetersPerUnit is double declared && units.IsReliable)
            warnings.Add($"DXF birim uyuşmazlığı: başlık {units.Description} (1 birim = {DisplayFactor(declared)} mm), seçilen ölçek {DisplayFactor(millimetersPerUnit)} mm. Başlık otomatik ölçeklemez; ölçüyü doğrulayın.");
        else warnings.Add($"DXF birimi güvenilir biçimde belirlenemedi: {units.Description}. Seçilen ölçek 1 birim = {DisplayFactor(millimetersPerUnit)} mm; ölçüyü doğrulayın.");
        var metadataSources=new DxfMetadataSourceReport(metadataRecords.ToImmutable(),metadataRecordsTruncated);
        var opaqueMetadata=new DxfOpaqueMetadata(opaqueGroups.ToImmutable(),opaqueTruncated);
        var layerStates=ReadLayerStates(entities,cancellationToken);
        // Preserve undecoded annotation groups for explicit review. They are
        // not mapped to garment fields or treated as block placements.
        const int maxAnnotations=4096,maxAnnotationGroups=32768,maxAnnotationCharacters=1_000_000;
        var annotationRecords=ImmutableArray.CreateBuilder<DxfAnnotationRecord>();
        var annotationTruncated=false;var annotationGroupCount=0;var annotationCharacters=0;
        string annotationSection="",annotationBlock="";
        for(var ordinal=0;ordinal<entities.Count;ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item=entities[ordinal];
            if(item.Type=="SECTION") { annotationSection=item.Text(2);annotationBlock="";continue; }
            if(item.Type=="ENDSEC") { annotationSection="";annotationBlock="";continue; }
            if(annotationSection=="BLOCKS" && item.Type=="BLOCK") { annotationBlock=item.Text(2);continue; }
            if(item.Type=="ENDBLK") { annotationBlock="";continue; }
            if(item.Type is not ("TEXT" or "MTEXT" or "ATTRIB" or "ATTDEF"))continue;
            if(annotationRecords.Count>=maxAnnotations || item.Data.Count>maxAnnotationGroups-annotationGroupCount)
            { annotationTruncated=true;break; }
            var characters=0;
            foreach(var group in item.Data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(group.RawValue.Length>maxAnnotationCharacters-annotationCharacters-characters)
                { annotationTruncated=true;break; }
                characters+=group.RawValue.Length;
            }
            if(annotationTruncated)break;
            var groups=ImmutableArray.CreateBuilder<DxfAnnotationGroup>(item.Data.Count);
            foreach(var group in item.Data) { cancellationToken.ThrowIfCancellationRequested();groups.Add(new DxfAnnotationGroup(group.Code,group.RawValue) { GroupOrdinal=group.GroupOrdinal }); }
            bool? layerVisible=null,layerHasNoTransparency=null;
            try
            {
                var layerName=item.LayerName();
                if(!(annotationSection=="BLOCKS" && layerName.Equals("0",StringComparison.OrdinalIgnoreCase)) &&
                   layerStates.TryGetValue(layerName,out var state))
                {
                    layerHasNoTransparency=!state.HasTransparency;
                    if(!state.Unknown)layerVisible=state.Hidden ? false : (state.ViewportDefaultFrozen || hasViewportData ? null : true);
                }
            }
            catch(InvalidDataException) { /* Keep the raw record; its layer remains unverified. */ }
            annotationRecords.Add(new DxfAnnotationRecord(ordinal,annotationSection,annotationBlock.Length==0 ? null : annotationBlock,item.Type,groups.MoveToImmutable()) { LayerVisible=layerVisible, LayerHasNoTransparency=layerHasNoTransparency });
            annotationGroupCount+=item.Data.Count;annotationCharacters+=characters;
        }
        var annotations=new DxfAnnotationReport(annotationRecords.ToImmutable(),annotationTruncated);
        var metadataLoss=new DxfMetadataLossSummary(extendedDataGroups,applicationDataGroups,lineweightGroups,transparencyGroups);
        if (extendedDataGroups>0) warnings.Add("DXF genişletilmiş meta verileri aktarılmaz; çentik/beden/katman semantiğini doğrulayın.");
        if (lineweightGroups>0 || transparencyGroups>0) warnings.Add("DXF çizgi kalınlığı veya saydamlık meta verileri aktarılmaz; çizgilerin kaynak dosyadaki anlamını doğrulayın.");
        if(hasApplicationData)warnings.Add("DXF 102 uygulama veri grupları aktarılmaz; geometri yalnız ana varlık verilerinden alındı.");
        AddEncodingHeaderWarnings(entities,encoding,raw,(int)length,utf8Bom,warnings,cancellationToken);
        List<DxfIndexEntity> BuildIndexEntities()
        {
            var indexed=new List<DxfIndexEntity>(entities.Count);
            for(var ordinal=0;ordinal<entities.Count;ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source=entities[ordinal];
                var groups=ImmutableArray.CreateBuilder<DxfIndexGroup>(source.Data.Count);
                foreach(var pair in source.Data)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    groups.Add(new(pair.Code,pair.Value,pair.RawValue));
                }
                indexed.Add(new(source.Type,groups.MoveToImmutable(),ordinal));
            }
            return indexed;
        }
        // Keep a bounded prefix of whole raw source records independently of
        // whether a later geometry reader imports, skips or expands them.
        const int maxSourceGeometryRecords=4096,maxSourceGeometryGroups=32768,maxSourceGeometryCharacters=1_000_000;
        var rawGeometry=ImmutableArray.CreateBuilder<DxfSourceGeometryRecord>();
        var rawGeometryTruncated=false;var rawGeometryGroups=0;var rawGeometryCharacters=0;
        string rawSection="",rawBlock="";
        for(var ordinal=0;ordinal<entities.Count;ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item=entities[ordinal];
            if(item.Type=="SECTION") { rawSection=item.Text(2);rawBlock="";continue; }
            if(item.Type=="ENDSEC") { rawSection="";rawBlock="";continue; }
            if(rawSection=="BLOCKS" && item.Type=="BLOCK") { rawBlock=item.Text(2);continue; }
            if(item.Type=="ENDBLK") { rawBlock="";continue; }
            if(rawSection is not ("ENTITIES" or "BLOCKS") || item.Type is not ("LINE" or "LWPOLYLINE" or "POLYLINE" or "VERTEX" or "SEQEND" or "INSERT"))continue;
            if(rawGeometry.Count>=maxSourceGeometryRecords || item.Data.Count>maxSourceGeometryGroups-rawGeometryGroups)
            { rawGeometryTruncated=true;break; }
            var characters=0;
            foreach(var group in item.Data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(group.RawValue.Length>maxSourceGeometryCharacters-rawGeometryCharacters-characters)
                { rawGeometryTruncated=true;break; }
                characters+=group.RawValue.Length;
            }
            if(rawGeometryTruncated)break;
            var groups=ImmutableArray.CreateBuilder<DxfAnnotationGroup>(item.Data.Count);
            var groupOrdinals=ImmutableArray.CreateBuilder<int>(item.Data.Count);
            foreach(var group in item.Data) { cancellationToken.ThrowIfCancellationRequested();groups.Add(new DxfAnnotationGroup(group.Code,group.RawValue) { GroupOrdinal=group.GroupOrdinal });groupOrdinals.Add(group.GroupOrdinal); }
            rawGeometry.Add(new DxfSourceGeometryRecord(ordinal,rawSection,rawBlock.Length==0 ? null : rawBlock,item.Type,groups.MoveToImmutable())
                { GroupOrdinals=groupOrdinals.MoveToImmutable() });
            rawGeometryGroups+=item.Data.Count;rawGeometryCharacters+=characters;
        }
        var sourceGeometry=new DxfSourceGeometryReport(rawGeometry.ToImmutable(),rawGeometryTruncated);
        var sourceObjects=CaptureOtherSourceRecords(entities,cancellationToken,objectsOnly:true);
        if(sourceGeometry.Records.Any(record=>record.Groups.Any(group=>group.Code==5 || group.Code is >=330 and <=369)))
            warnings.Add("DXF kaynak geometri tanıtıcıları veya nesne bağlantıları projede korunmaz; kaynak kayıt ilişkilerini doğrulayın.");
        if(sourceObjects.Records.Length>0 || sourceObjects.Truncated)
            warnings.Add("DXF OBJECTS kayıtları proje nesnelerine aktarılmaz; sözlük ve özel nesne ilişkilerini kaynak dosyada doğrulayın.");
        if(sourceObjects.Truncated || sourceGeometry.Truncated || annotations.Truncated || opaqueMetadata.Truncated || metadataSources.Truncated)
            warnings.Add("DXF kaynak inceleme envanterlerinden en az biri kesildi; eksik kayıtlar için özgün dosyaya başvurun.");
        if(inspectionSink is not null)
        {
            var inspectionSupplemental=CaptureSupplementalGeometry(entities,cancellationToken,includePolylineCandidates:true);
            var inspectionOther=CaptureOtherSourceRecords(entities,cancellationToken);
            if(inspectionSupplemental.Truncated || inspectionOther.Truncated)
                warnings.Add("DXF ön incelemesindeki ek kayıt envanteri kesildi; eksik kayıtlar için özgün dosyaya başvurun.");
            var indexed=BuildIndexEntities();
            var index=DxfInsertIndex.Build(indexed,DecodeLayerName,cancellationToken);
            var layerList=DxfInsertLayerTable.Read(indexed,DecodeLayerName,cancellationToken)
                .Select(state=>new DxfBlockLayerInspection(state.Name,state.Hidden,state.PlotEnabled,state.LineType)).ToImmutableArray();
            var limits=ImmutableArray.CreateBuilder<string>();
            limits.Add("Bu inceleme geometriyi doğrulamaz; kaynak katman renkleri projede korunmaz ve kesim anlamı ayrıca doğrulanmalıdır.");
            limits.Add("Sınırlı 2B düz ve çoklu çizgiler desteklenir. Blok dönüşleri seçilen ölçü toleransıyla doğrulanır; belirsiz veya desteklenmeyen veriler reddedilir.");
            limits.Add("Bilinmeyen katman görünürlüğü, plot durumu veya çizgi tipi desteklenmeyebilir; gerçek dosya ve ölçüleri doğrulayın.");
            if(hasApplicationData || pairs.Any(p=>p.Code>=1000)) limits.Add("Uygulama veya genişletilmiş DXF verisi var; kontrollü aktarım reddedilir.");
            cancellationToken.ThrowIfCancellationRequested();
            var inspectionWarnings=warnings.Where(w=>!w.StartsWith("DXF birim",StringComparison.OrdinalIgnoreCase)).ToImmutableArray();
            inspectionSink(new DxfBlockPlacementInspection(sourceSha256,sourceByteLength,encoding,layerList,
                index.ModelEntities.Any(item=>item.Entity.Type=="INSERT"),limits.ToImmutable(),inspectionWarnings,metadataSources,opaqueMetadata,annotations,inspectionSupplemental)
                { SourceGeometry=sourceGeometry, OtherSourceRecords=inspectionOther, SourceObjects=sourceObjects });
            return new DxfImportResult(new ProjectDocument { Name=projectName },warnings) { Units=units, SourceSha256=sourceSha256, SourceByteLength=sourceByteLength, MetadataLoss=metadataLoss, MetadataSources=metadataSources, OpaqueMetadata=opaqueMetadata, Annotations=annotations, SupplementalGeometry=inspectionSupplemental, SourceGeometry=sourceGeometry };
        }
        var doc = new ProjectDocument { Name = projectName };
        var strictImport = strict is not null;
        var acceptedPoints=0;
        var pieceSources=ImmutableArray.CreateBuilder<DxfPieceSource>();
        void AddPiece(string name, List<Point2> points, bool closed, string? sourceSection=null,
            string? sourceBlock=null, string? sourceLayer=null, string? sourceType=null, IEnumerable<int>? ordinals=null,
            IEnumerable<int>? childOrdinals=null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if((strictImport || bulgeToleranceMm is not null) && points.Count>=2)
            {
                if(doc.Pieces.Count>=ProjectStorage.MaximumPieces || points.Count>ProjectStorage.MaximumTotalPoints-acceptedPoints)
                    throw new InvalidDataException("DXF doğrudan ve yerleştirilmiş geometrinin ortak sınırını aşıyor.");
                acceptedPoints+=points.Count;
            }
            Add(doc,name,points,closed);
            if(!strictImport && points.Count>=2 && sourceSection is not null && sourceLayer is not null && sourceType is not null && ordinals is not null)
                pieceSources.Add(new DxfPieceSource(doc.Pieces[^1].Id,sourceSection,sourceBlock,sourceLayer,sourceType,ordinals.ToImmutableArray())
                    { ChildSourceOrdinals=childOrdinals?.ToImmutableArray() ?? ImmutableArray<int>.Empty });
        }
        long bulgeValidationComparisons = 0;
        int convertedBulgeSegments = 0, convertedBulgeContours = 0;
        List<Point2> ApproximateBulge(List<DxfBulgeVertex> vertices, bool closed, List<Point2> originalEndpoints)
        {
            if (bulgeToleranceMm is not double tolerance) throw new InvalidDataException("Bulge toleransı eksik.");
            var remaining=ProjectStorage.MaximumTotalPoints-acceptedPoints;
            if (remaining<1) throw new InvalidDataException("Bulge toplam nokta sınırını aşıyor.");
            var generated=DxfBulgeApproximation.Approximate(vertices,closed,millimetersPerUnit,tolerance,remaining,cancellationToken).Points.ToList();
            var cursor=0;
            foreach(var endpoint in originalEndpoints)
            {
                while(cursor<generated.Count && generated[cursor]!=endpoint) { cancellationToken.ThrowIfCancellationRequested();cursor++; }
                if(cursor==generated.Count) throw new InvalidDataException("Bulge kaynak ucu korunamadı.");
                cursor++;
            }
            if(generated.Count>Geometry.MaxValidatedContourVertices)throw new InvalidDataException("Bulge kontur doğrulama sınırını aşıyor.");
            bulgeValidationComparisons += (long)generated.Count * (generated.Count - 1) / 2;
            if (bulgeValidationComparisons > 50_000_000) throw new InvalidDataException("Bulge toplam doğrulama iş sınırını aşıyor.");
            try
            {
                if(closed) Geometry.ValidateContour(generated,cancellationToken);
                else if(Geometry.HasSelfIntersections(generated,false,cancellationToken)) throw new InvalidDataException("Bulge açık konturu kesişiyor veya sıfır uzunluk içeriyor.");
            }
            catch(ArgumentException ex) { throw new InvalidDataException("Bulge konturu doğrulanamadı.",ex); }
            int curvedSegments = 0;
            for (int i=0; i<vertices.Count; i++)
            {
                if ((i&255)==0) cancellationToken.ThrowIfCancellationRequested();
                if (vertices[i].Bulge != 0) curvedSegments++;
            }
            if (curvedSegments > 0) { convertedBulgeSegments += curvedSegments; convertedBulgeContours++; }
            return generated;
        }
        if(strict is not null)
        {
            // All data used here comes from the same bounded, decoded snapshot.
            var indexedEntities=BuildIndexEntities();
            var index=DxfInsertIndex.Build(indexedEntities,DecodeLayerName,cancellationToken);
            var hasInsert=index.ModelEntities.Any(item=>item.Entity.Type=="INSERT");
            {
                if(hasApplicationData || pairs.Any(p=>p.Code>=1000))
                    throw new InvalidDataException("DXF uygulama veya genişletilmiş verisi içeren kontrollü aktarım henüz doğrulanmadı.");
                var states=DxfInsertLayerTable.Read(indexedEntities,DecodeLayerName,cancellationToken)
                    .Select(state=>state with { CuttingSemanticsApproved=strict.ApprovedLayers.Contains(state.Name) }).ToArray();
                var resolver=new DxfInsertLayerResolver(states,DecodeLayerName);
                // Direct contours use the same strict checks as block
                // contours. Give each an internal identity placement on its own
                // layer. This path is an implementation detail, not source
                // provenance, and no synthetic path is returned to the caller.
                var expandedIndex=IncludeDirectGeometry(index,indexedEntities.Count,cancellationToken);
                var expanded=DxfInsertExpander.Expand(expandedIndex,millimetersPerUnit,DecodeLayerName,resolver,strict.ComponentToleranceMm,cancellationToken);
                foreach(var contour in expanded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(doc.Pieces.Count>=ProjectStorage.MaximumPieces || contour.Points.Length>ProjectStorage.MaximumTotalPoints-acceptedPoints)
                        throw new InvalidDataException("DXF doğrudan ve yerleştirilmiş geometrinin ortak sınırını aşıyor.");
                    AddPiece(contour.EffectiveLayer ?? throw new InvalidDataException("DXF etkin katmanı eksik."),contour.Points.ToList(),contour.Closed);
                    if(contour.EntityOccurrences.IsDefaultOrEmpty || contour.EntityOccurrences.Any(ordinal=>ordinal<0 || ordinal>=indexedEntities.Count))
                        throw new InvalidDataException("DXF kaynak varlık kimliği doğrulanamadı.");
                    if(contour.ChildOccurrences.IsDefault || contour.ChildOccurrences.Any(ordinal=>ordinal<0 || ordinal>=indexedEntities.Count))
                        throw new InvalidDataException("DXF kaynak alt varlık kimliği doğrulanamadı.");
                    // Synthetic direct-placement ordinals are allocated strictly
                    // after the captured source records, independent of names.
                    var synthetic=contour.InsertPath.Any(step=>step.Occurrence>=indexedEntities.Count);
                    if(synthetic && (contour.InsertPath.Length!=1 || contour.InsertPath[0].Occurrence<indexedEntities.Count))
                        throw new InvalidDataException("DXF sentetik yerleşim yolu doğrulanamadı.");
                    if(!synthetic && (contour.InsertPath.IsEmpty || contour.InsertPath.Any(step=>step.Occurrence<0 ||
                        step.Occurrence>=indexedEntities.Count || indexedEntities[step.Occurrence].Type!="INSERT")))
                        throw new InvalidDataException("DXF kaynak INSERT yolu doğrulanamadı.");
                    var insertPath=synthetic ? ImmutableArray<DxfSourceInsert>.Empty : contour.InsertPath
                        .Select(step=>new DxfSourceInsert(step.BlockName,step.Occurrence)).ToImmutableArray();
                    var entityType=indexedEntities[contour.EntityOccurrences[0]].Type;
                    if(contour.EntityOccurrences.Any(ordinal=>indexedEntities[ordinal].Type!=entityType))
                        throw new InvalidDataException("DXF kontur kaynak varlık türleri uyuşmuyor.");
                    pieceSources.Add(new(doc.Pieces[^1].Id,insertPath.IsEmpty ? "ENTITIES" : "BLOCKS",
                        insertPath.IsEmpty ? null : insertPath[^1].BlockName,contour.EffectiveLayer!,entityType,contour.EntityOccurrences)
                        { InsertPath=insertPath, ChildSourceOrdinals=contour.ChildOccurrences });
                }
                warnings.Add(hasInsert
                    ? "Kontrollü aktarımda yalnız doğrulanan 2B LINE/LWPOLYLINE/POLYLINE ve INSERT yerleşimleri aktarıldı; dönüşler seçilen toleransla denetlendi. Katman kesim anlamı seçilen onaya dayanır; diğer DXF/AAMA semantiği doğrulanmadı."
                    : "Kontrollü aktarımda yalnız doğrulanan model alanı 2B LINE/LWPOLYLINE/POLYLINE verileri aktarıldı; kullanılmayan blok tanımları alınmadı. Katman kesim anlamı seçilen onaya dayanır; diğer DXF/AAMA semantiği doğrulanmadı.");
                warnings.Add("DXF renkleri projede korunmaz; onaylanan katmanların kesim ve dikiş anlamlarını kaynak dosyada doğrulayın.");
                warnings.Add($"DXF koordinat denetimi için bileşen başına tolerans {DisplayFactor(strict.ComponentToleranceMm)} mm; birim çarpanı double değerinin invariant R ondalık gösterimidir. Ölçüleri kaynakta doğrulayın.");
            }
        }
        // Display names are not source identities: a block/layer combination
        // can display the same text as an unrelated ENTITIES layer.
        var segments = new Dictionary<(string Section, int BlockOccurrence, string Block, string Layer, string LineType, string Color, string Appearance), List<(Point2 A, Point2 B,int Ordinal)>>(new LineGroupComparer());
        var lineOrigins = new Dictionary<(string Section, int BlockOccurrence, string Block, string Layer, string LineType, string Color, string Appearance), Dictionary<Point2, SourcePoint>>(new LineGroupComparer());
        var unsupported = new HashSet<string>();
        // These reasons are constructed by the reader from a finite set of
        // geometry/exclusion checks; arbitrary source entity names never enter
        // this set. Do not let vendor names crowd safety-relevant reasons out.
        var skippedGeometryReasons=new HashSet<string>(StringComparer.Ordinal);
        var unsupportedSummaryTruncated=false;
        void SummarizeUnsupported(string reason)
        {
            // Source entity names are untrusted. Keep the user-facing warning
            // bounded for project provenance, while the skipped-record report
            // retains the original reason independently.
            const int maxReasons=64,maxReasonLength=48;
            var safe=new StringBuilder(Math.Min(reason.Length,maxReasonLength));
            foreach(var c in reason)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(safe.Length>=maxReasonLength) { unsupportedSummaryTruncated=true;break; }
                safe.Append(char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c);
            }
            if(safe.Length>0 && char.IsHighSurrogate(safe[^1])) { safe.Length--;unsupportedSummaryTruncated=true; }
            var value=safe.ToString();
            if(unsupported.Contains(value))return;
            if(unsupported.Count>=maxReasons) { unsupportedSummaryTruncated=true;return; }
            unsupported.Add(value);
        }
        const int maxSkippedPolylines=4096;
        var skippedPolylines=new HashSet<int>();var skippedPolylinesTruncated=false;
        const int maxSkipRecords=4096;
        var skippedRecords=ImmutableArray.CreateBuilder<DxfSkippedSourceRecord>();
        var skippedRecordsTruncated=false;
        string section = "", block = ""; int blockOccurrence=0, nextBlockOccurrence=0;
        string? excludedBlockReason=null;
        bool excludedBlockCertain=false;
        bool blockGeometry = false, skippedInsertEntities=false, skippedRelevantInsertEntities=false, unknownLayerUsed=false, nonPlotLayerUsed=false,modelLayerExamined=false,viewportLayerUsed=false,unknownLineTypeUsed=false,colorsUsed=false,unknownColorUsed=false,opaqueLinesSeparated=false; int lineCount = 0;
        for (int i = 0; i < entities.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var e = entities[i];
            if (e.Type == "SECTION") { section = e.Text(2); continue; }
            if (e.Type == "ENDSEC") { section = ""; block=""; excludedBlockReason=null; excludedBlockCertain=false; continue; }
            if (section is not ("ENTITIES" or "BLOCKS")) continue;
            if (bulgeToleranceMm is not null && e.Type == "INSERT")
                throw new InvalidDataException("Bulge incelemesinde INSERT yerleşimleri desteklenmiyor; dosya aktarılmadı.");
            // Definitions are not model-space placements. Never interpret their
            // geometry as positioned output in the opt-in importer, including
            // straight LINE/LWPOLYLINE records or hidden entities.
            if (bulgeToleranceMm is not null && section == "BLOCKS" &&
                e.Type is not ("BLOCK" or "ENDBLK" or "SEQEND" or "EOF" or "TEXT" or "MTEXT" or "ATTRIB" or "ATTDEF"))
                throw new InvalidDataException("Bulge aktarımında blok tanım geometrisi desteklenmiyor; dosya aktarılmadı.");
            // In legacy mode even hidden, paper-space, or nested INSERTs are not
            // expanded. Record this before visibility filtering rather than
            // depending on localized warnings or on whether a contour was kept.
            if (!strictImport && e.Type == "INSERT") skippedInsertEntities=true;
            if(strictImport && section=="ENTITIES" && e.Type=="POLYLINE")
            {
                // The strict index already validated and imported the complete
                // POLYLINE sequence. Never reinterpret its owned children as
                // independent entities in the legacy pass.
                while(i+1<entities.Count && entities[i+1].Type=="VERTEX")
                { cancellationToken.ThrowIfCancellationRequested(); i++; }
                if(i+1>=entities.Count || entities[i+1].Type!="SEQEND")
                    throw new InvalidDataException("POLYLINE SEQEND kaydı eksik.");
                i++;
                continue;
            }
            if(strictImport && (section=="BLOCKS" || e.Type is "INSERT" or "LWPOLYLINE" or "LINE")) continue;
            if (e.Type == "BLOCK")
            {
                var names=e.Data.Where(p=>p.Code==2).ToArray();
                if(names.Length!=1)throw new InvalidDataException("DXF blok adı eksik veya yinelenmiş.");
                try { block=DecodeLayerName(names[0].RawValue); }
                catch(InvalidDataException ex) { throw new InvalidDataException("DXF blok adında geçersiz veya belirsiz Unicode, ters bölü ya da boşluk var.",ex); }
                blockOccurrence=++nextBlockOccurrence;
                excludedBlockReason=ExcludedSpaceOrVisibility(e,cancellationToken) ??
                    (block.StartsWith("*Paper_Space",StringComparison.OrdinalIgnoreCase) ? "kağıt alanı blok geometrisi" : null);
                excludedBlockCertain=excludedBlockReason is not null && HasUnambiguousExclusionMetadata(e,cancellationToken);
                continue;
            }
            if (e.Type == "ENDBLK") { block = ""; blockOccurrence=0; excludedBlockReason=null; excludedBlockCertain=false; continue; }
            if (e.Type is "EOF" or "SEQEND") continue;
            var layer=e.LayerName();
            var sourceOrdinal=i;
            void RecordSkipped(string reason,bool knownGeometryReason=true)
            {
                if (bulgeToleranceMm is not null && knownGeometryReason && e.Type is not ("TEXT" or "MTEXT" or "ATTRIB" or "ATTDEF"))
                    throw new InvalidDataException("DXF geometrisi güvenle aktarılamadı: " + reason);
                SummarizeUnsupported(reason);
                if(knownGeometryReason)skippedGeometryReasons.Add(reason);
                if(strictImport)return;
                if(e.Type is "POLYLINE" or "LWPOLYLINE")
                {
                    if(skippedPolylines.Count<maxSkippedPolylines)skippedPolylines.Add(sourceOrdinal);
                    else skippedPolylinesTruncated=true;
                }
                if(skippedRecords.Count<maxSkipRecords)
                    skippedRecords.Add(new(sourceOrdinal,section,block.Length==0 ? null : block,layer,e.Type,reason));
                else skippedRecordsTruncated=true;
            }
            var entityExcludedReason=ExcludedSpaceOrVisibility(e,cancellationToken);
            var excludedReason=entityExcludedReason ?? excludedBlockReason;
            if(!strictImport && e.Type=="INSERT")
            {
                // Do not equate an excluded but malformed or contradictory
                // record with a known irrelevant one. Layer/color inheritance
                // is not used as evidence of exclusion.
                var definitelyExcluded=HasUnambiguousExclusionMetadata(e,cancellationToken) &&
                    (entityExcludedReason is not null || (excludedBlockReason is not null && excludedBlockCertain));
                if(!definitelyExcluded)skippedRelevantInsertEntities=true;
            }
            if(excludedReason is null && section=="ENTITIES" && e.Type is ("LINE" or "LWPOLYLINE" or "POLYLINE"))
            {
                modelLayerExamined=true;
                if(!layerStates.TryGetValue(layer,out var state))unknownLayerUsed=true;
                else
                {
                    if(state.Unknown)unknownLayerUsed=true;
                    if(state.NonPlot)nonPlotLayerUsed=true;
                    if(state.ViewportDefaultFrozen)viewportLayerUsed=true;
                    if(state.Hidden && !state.Unknown)excludedReason="kapalı veya donmuş DXF katmanı";
                }
            }
            if(excludedReason is not null)
            {
                // A legacy POLYLINE owns its VERTEX sequence. Consume it as one
                // excluded entity, never reinterpret its vertices independently.
                if(e.Type=="POLYLINE")
                {
                    while(i+1<entities.Count && entities[i+1].Type=="VERTEX")
                    { cancellationToken.ThrowIfCancellationRequested();i++;_ = entities[i].LayerName();_ = ExcludedSpaceOrVisibility(entities[i],cancellationToken); }
                    if(i+1>=entities.Count || entities[i+1].Type!="SEQEND")
                        throw new InvalidDataException("POLYLINE SEQEND kaydı eksik.");
                }
                RecordSkipped(excludedReason+" (kontur atlandı)");
                continue;
            }
            var name = (block.Length > 0 ? block + " / " : "") + layer;
            if (e.Type is "LINE" or "LWPOLYLINE" or "POLYLINE")
            {
                if (HasUnsupportedPlane(e))
                {
                    if(e.Type=="POLYLINE")
                    {
                        while(i+1<entities.Count && entities[i+1].Type=="VERTEX") { cancellationToken.ThrowIfCancellationRequested(); i++; }
                        if(i+1>=entities.Count || entities[i+1].Type!="SEQEND")throw new InvalidDataException("POLYLINE SEQEND kaydı eksik.");
                        i++;
                    }
                    RecordSkipped(e.Type + " 3D/elevation/normal (kontur atlandı)"); continue;
                }
            }
            var lineTypeKey="";
            if(e.Type is "LINE" or "LWPOLYLINE" or "POLYLINE")
            {
                var values=e.Data.Where(p=>p.Code==6).ToArray();
                if(values.Length>1 || (values.Length==1 && string.IsNullOrWhiteSpace(values[0].Value)))
                    throw new InvalidDataException($"DXF {e.Type} çizgi tipi boş veya yinelenmiş.");
                var explicitType=values.Length==0 ? "BYLAYER" : values[0].Value.Trim();
                string? effective;
                if(explicitType.Equals("BYLAYER",StringComparison.OrdinalIgnoreCase))
                    effective=section=="BLOCKS" && layer.Equals("0",StringComparison.OrdinalIgnoreCase)
                        ? null : layerStates.TryGetValue(layer,out var lineLayer) ? lineLayer.LineType : null;
                else if(explicitType.Equals("BYBLOCK",StringComparison.OrdinalIgnoreCase))effective=null;
                else effective=explicitType;
                if(effective is not null && !effective.Equals("CONTINUOUS",StringComparison.OrdinalIgnoreCase))
                {
                    // A pattern may represent a fold, seam or other non-cut line.
                    // Converting it silently to an unbroken contour changes meaning.
                    if(e.Type=="POLYLINE")
                    {
                        while(i+1<entities.Count && entities[i+1].Type=="VERTEX") { cancellationToken.ThrowIfCancellationRequested();i++; }
                        if(i+1>=entities.Count || entities[i+1].Type!="SEQEND")throw new InvalidDataException("POLYLINE SEQEND kaydı eksik.");
                    }
                    RecordSkipped("sürekli olmayan veya desteklenmeyen çizgi tipi (kontur atlandı)");
                    continue;
                }
                if(effective is null)
                {
                    unknownLineTypeUsed=true;
                    lineTypeKey=explicitType.Equals("BYBLOCK",StringComparison.OrdinalIgnoreCase) ? "BYBLOCK-belirsiz" : "BYLAYER-belirsiz";
                }
                else lineTypeKey="CONTINUOUS";
            }
            if (e.Type == "LINE")
            {
                if (++lineCount > 4096) throw new InvalidDataException("DXF LINE birleştirme sınırı 4096 segmenttir.");
                var sourceStart=new SourcePoint(e.RequiredCoordinate(10),e.RequiredCoordinate(20));
                var sourceEnd=new SourcePoint(e.RequiredCoordinate(11),e.RequiredCoordinate(21));
                var start=ScaleSourcePoint(sourceStart,millimetersPerUnit);
                var end=ScaleSourcePoint(sourceEnd,millimetersPerUnit);
                try { Geometry.Validate(start);Geometry.Validate(end); }
                catch(ArgumentOutOfRangeException ex) { throw new InvalidDataException("DXF LINE koordinatları ölçek sonrası çalışma sınırlarını aşıyor.",ex); }
                if(start==end)
                {
                    if(SameSourcePoint(sourceStart,sourceEnd)) { RecordSkipped("sıfır uzunluklu LINE (atlandı)");continue; }
                    throw new InvalidDataException("DXF LINE uçları ayrıştırma veya ölçekleme hassasiyeti nedeniyle birleşti; çizim aktarılmadı.");
                }
                var colorKey=ResolveEntityColor(e,section,layer,layerStates,out var unknownColor);
                colorsUsed=true;unknownColorUsed|=unknownColor;
                // Unknown application metadata can distinguish cut/sew or
                // other textile roles. Never merge such a source LINE with a
                // neighbour merely because its geometry and layer match.
                var appearanceKey=ResolveLineAppearanceKey(e);
                if(e.HasOpaqueMetadata) { appearanceKey+="/opaque:"+sourceOrdinal.ToString(CultureInfo.InvariantCulture);opaqueLinesSeparated=true; }
                var key = (section, blockOccurrence, block, layer, lineTypeKey, colorKey, appearanceKey);
                if (!lineOrigins.TryGetValue(key, out var origins)) lineOrigins[key] = origins = [];
                CheckLineEndpointOrigin(origins,start,sourceStart);
                CheckLineEndpointOrigin(origins,end,sourceEnd);
                if (!segments.TryGetValue(key, out var list)) segments[key] = list = [];
                list.Add((start,end,sourceOrdinal));
                blockGeometry |= section == "BLOCKS";
            }
            else if (e.Type == "LWPOLYLINE")
            {
                _=ResolveEntityColor(e,section,layer,layerStates,out var unknownColor);
                colorsUsed=true;unknownColorUsed|=unknownColor;
                var lightweightFlags=e.Flags(70);
                // Only closure and linetype generation are defined for this
                // entity. Unknown bits cannot safely be treated as 2D contours.
                if ((lightweightFlags & ~(1 | 128)) != 0)
                    throw new InvalidDataException("LWPOLYLINE desteklenmeyen bayrak içeriyor; kontur aktarılmadı.");
                var points = new List<Point2>();
                var scaledOrigins = new Dictionary<Point2,SourcePoint>();
                var counts = e.Data.Where(p => p.Code == 90).ToArray();
                if (counts.Length != 1 || !int.TryParse(counts[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var declared) || declared < 2 || declared > ProjectStorage.MaximumPointsPerPiece)
                    throw new InvalidDataException("LWPOLYLINE nokta sayısı eksik veya geçersiz.");
                if(strictImport && (doc.Pieces.Count>=ProjectStorage.MaximumPieces || declared>ProjectStorage.MaximumTotalPoints-acceptedPoints))
                    throw new InvalidDataException("DXF doğrudan ve yerleştirilmiş geometrinin ortak sınırını aşıyor.");
                Coordinate? xValue = null;
                var sourceVertices = new List<DxfBulgeVertex>();
                var lastBulgeIndex = -1;
                foreach (var group in e.Data)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (group.Code == 10)
                    {
                        if (xValue.HasValue) throw new InvalidDataException("LWPOLYLINE Y koordinatı eksik.");
                        xValue = ReadCoordinate(group.Value);
                    }
                    else if (group.Code == 20)
                    {
                        if (!xValue.HasValue) throw new InvalidDataException("LWPOLYLINE X koordinatı eksik veya Y yinelenmiş.");
                        if (points.Count >= declared) throw new InvalidDataException("LWPOLYLINE bildirilen nokta sayısını aşıyor.");
                        var sourcePoint=new SourcePoint(xValue.Value,ReadCoordinate(group.Value));
                        if (bulgeToleranceMm is not null && ((sourcePoint.X.Value == 0 && IsNonzeroDecimal(sourcePoint.X.Text)) || (sourcePoint.Y.Value == 0 && IsNonzeroDecimal(sourcePoint.Y.Text))))
                            throw new InvalidDataException("Polilin kaynak koordinatı sayısal dönüşümde sıfıra düştü.");
                        points.Add(ScalePolylinePoint(sourcePoint,millimetersPerUnit,scaledOrigins));
                        if (bulgeToleranceMm is not null) sourceVertices.Add(new(new Point2(sourcePoint.X.Value,sourcePoint.Y.Value),0));
                        xValue = null;
                    }
                    else if (group.Code == 42 && bulgeToleranceMm is not null)
                    {
                        if (xValue.HasValue || sourceVertices.Count == 0 || lastBulgeIndex == sourceVertices.Count-1)
                            throw new InvalidDataException("LWPOLYLINE bulge köşesi eksik, sırası belirsiz veya yinelenmiş.");
                        var parsed = ReadCoordinate(group.Value);
                        if (parsed.Value == 0 && IsNonzeroDecimal(group.Value)) throw new InvalidDataException("Bulge sayısal dönüşümde sıfıra düştü.");
                        sourceVertices[^1] = sourceVertices[^1] with { Bulge = parsed.Value };
                        lastBulgeIndex = sourceVertices.Count-1;
                    }
                }
                if (xValue.HasValue || points.Count != declared) throw new InvalidDataException("LWPOLYLINE nokta sayısı veya koordinatları uyuşmuyor.");
                if (HasNonzeroPolylineWidth(e, cancellationToken, 40, 41, 43)) { RecordSkipped("genişlikli LWPOLYLINE (kontur atlandı)"); continue; }
                if (bulgeToleranceMm is not null) points = ApproximateBulge(sourceVertices, (lightweightFlags & 1) != 0, points);
                else if (e.Data.Any(p => p.Code == 42 && IsNonzeroDecimal(p.Value))) { RecordSkipped("bulge eğrisi (kontur atlandı)"); continue; }
                AddPiece(name, points, (lightweightFlags & 1) != 0,section,block.Length==0 ? null : block,layer,e.Type,[sourceOrdinal]);
                blockGeometry |= section == "BLOCKS";
            }
            else if (e.Type == "POLYLINE")
            {
                _=ResolveEntityColor(e,section,layer,layerStates,out var unknownColor);
                colorsUsed=true;unknownColorUsed|=unknownColor;
                var vertices = new List<Entity>();
                while (i + 1 < entities.Count && entities[i + 1].Type == "VERTEX")
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(strictImport && (vertices.Count>=ProjectStorage.MaximumPointsPerPiece ||
                        vertices.Count>=ProjectStorage.MaximumTotalPoints-acceptedPoints))
                        throw new InvalidDataException("DXF doğrudan ve yerleştirilmiş geometrinin ortak sınırını aşıyor.");
                    vertices.Add(entities[++i]);
                }
                if (i + 1 >= entities.Count || entities[i + 1].Type != "SEQEND") throw new InvalidDataException("POLYLINE SEQEND kaydı eksik.");
                var polylineFlags=e.Flags(70);
                if ((polylineFlags & ~255) != 0)
                    throw new InvalidDataException("POLYLINE desteklenmeyen bayrak içeriyor; kontur aktarılmadı.");
                if ((polylineFlags & (8 | 16 | 32 | 64)) != 0) { RecordSkipped("3D/mesh POLYLINE"); continue; }
                if (vertices.Count < 2) throw new InvalidDataException("POLYLINE yeterli nokta içermiyor.");
                if(strictImport && (doc.Pieces.Count>=ProjectStorage.MaximumPieces || vertices.Count>ProjectStorage.MaximumTotalPoints-acceptedPoints))
                    throw new InvalidDataException("DXF doğrudan ve yerleştirilmiş geometrinin ortak sınırını aşıyor.");
                bool hasBulge = false, has3D = false, hasFittedVertex = false;
                string? excludedVertexReason=null;
                var hasWidth = HasNonzeroPolylineWidth(e, cancellationToken, 40, 41);
                foreach (var vertex in vertices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var flags = vertex.Flags(70);
                    // Undefined and unused VERTEX flag bits carry semantics we
                    // cannot safely interpret as a plain 2D vertex.
                    if ((flags & ~(1 | 2 | 8 | 16 | 32 | 64 | 128)) != 0)
                        throw new InvalidDataException("VERTEX desteklenmeyen bayrak içeriyor; kontur aktarılmadı.");
                    _ = vertex.LayerName();
                    var vertexReason=ExcludedSpaceOrVisibility(vertex,cancellationToken);
                    excludedVertexReason ??= vertexReason;
                    has3D |= (flags & (32 | 64 | 128)) != 0 || HasUnsupportedPlane(vertex);
                    hasFittedVertex |= (flags & (1 | 2 | 8 | 16)) != 0;
                    var bulge=vertex.Data.Where(p=>p.Code==42).ToArray();
                    if(bulge.Length>1)throw new InvalidDataException("DXF VERTEX için 42 alanı yinelenmiş.");
                    if(bulge.Length==1)hasBulge |= IsNonzeroDecimal(bulge[0].Value);
                    hasWidth |= HasNonzeroPolylineWidth(vertex, cancellationToken, 40, 41);
                }
                if(excludedVertexReason is not null) { RecordSkipped(excludedVertexReason+" VERTEX (kontur atlandı)");continue; }
                if (has3D) { RecordSkipped("3D VERTEX (kontur atlandı)"); continue; }
                if (hasWidth) { RecordSkipped("genişlikli POLYLINE (kontur atlandı)"); continue; }
                if (hasBulge && bulgeToleranceMm is null) { RecordSkipped("bulge eğrisi (kontur atlandı)"); continue; }
                if (hasFittedVertex || (polylineFlags & (2 | 4)) != 0) { RecordSkipped("eğri/spline POLYLINE (kontur atlandı)"); continue; }
                var points = new List<Point2>(vertices.Count);
                var sourceVertices = new List<DxfBulgeVertex>();
                var scaledOrigins = new Dictionary<Point2,SourcePoint>();
                foreach (var vertex in vertices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourcePoint=new SourcePoint(vertex.RequiredCoordinate(10),vertex.RequiredCoordinate(20));
                    if (bulgeToleranceMm is not null && ((sourcePoint.X.Value == 0 && IsNonzeroDecimal(sourcePoint.X.Text)) || (sourcePoint.Y.Value == 0 && IsNonzeroDecimal(sourcePoint.Y.Text))))
                        throw new InvalidDataException("Polilin kaynak koordinatı sayısal dönüşümde sıfıra düştü.");
                    points.Add(ScalePolylinePoint(sourcePoint,millimetersPerUnit,scaledOrigins));
                    if (bulgeToleranceMm is not null)
                    {
                        var groups=vertex.Data.Where(p=>p.Code==42).ToArray();
                        var bulge=groups.Length==0 ? 0 : ReadCoordinate(groups[0].Value).Value;
                        if (groups.Length>0 && bulge==0 && IsNonzeroDecimal(groups[0].Value)) throw new InvalidDataException("Bulge sayısal dönüşümde sıfıra düştü.");
                        sourceVertices.Add(new(new Point2(sourcePoint.X.Value,sourcePoint.Y.Value),bulge));
                    }
                }
                if (bulgeToleranceMm is not null) points=ApproximateBulge(sourceVertices,(polylineFlags & 1) != 0,points);
                AddPiece(name, points, (polylineFlags & 1) != 0,section,block.Length==0 ? null : block,layer,e.Type,[sourceOrdinal],
                    Enumerable.Range(sourceOrdinal+1,vertices.Count+1));
                blockGeometry |= section == "BLOCKS";
            }
            else
            {
                // Text/attributes are non-contour annotations and retain their
                // existing separate reporting path. Unknown entity types may
                // contain geometry and cannot be silently dropped in opt-in mode.
                if (bulgeToleranceMm is not null && e.Type is not ("TEXT" or "MTEXT" or "ATTRIB" or "ATTDEF"))
                    throw new InvalidDataException("Bulge aktarımında desteklenmeyen DXF varlığı: " + e.Type);
                RecordSkipped(e.Type,knownGeometryReason:false);
            }
        }
        var ambiguousLineGroups = 0;
        foreach (var (key, list) in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = (key.Block.Length > 0 ? key.Block + " / " : "") + key.Layer;
            // A LINE endpoint with several possible neighbours is not enough
            // information to choose a contour. Greedy joining would make the
            // result depend on the order of entities in the DXF. Keep every
            // original segment instead, and require the user to review it.
            if (HasAmbiguousLineConnectionsCancellable(list.Select(edge=>(edge.A,edge.B)).ToArray(), cancellationToken))
            {
                ambiguousLineGroups++;
                foreach (var edge in list)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddPiece(name, [edge.A, edge.B], false,key.Section,key.Block.Length==0 ? null : key.Block,key.Layer,"LINE",[edge.Ordinal]);
                }
                continue;
            }
            while (list.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = list[^1]; list.RemoveAt(list.Count - 1);
                var points = new List<Point2> { first.A, first.B };
                var lineOrdinals = new List<int> { first.Ordinal };
                bool extended;
                do
                {
                    extended = false;
                    for (int k = 0; k < list.Count; k++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var edge = list[k];
                        if (Near(points[^1], edge.A)) points.Add(edge.B);
                        else if (Near(points[^1], edge.B)) points.Add(edge.A);
                        else if (Near(points[0], edge.B)) points.Insert(0, edge.A);
                        else if (Near(points[0], edge.A)) points.Insert(0, edge.B);
                        else continue;
                        lineOrdinals.Add(edge.Ordinal);list.RemoveAt(k); extended = true; break;
                    }
                } while (extended && !Near(points[0], points[^1]));
                var closed = points.Count > 2 && Near(points[0], points[^1]);
                if (closed) points.RemoveAt(points.Count - 1);
                AddPiece(name, points, closed,key.Section,key.Block.Length==0 ? null : key.Block,key.Layer,"LINE",lineOrdinals);
            }
        }
        if (ambiguousLineGroups > 0)
            warnings.Add($"Belirsiz LINE bağlantıları: {ambiguousLineGroups} katman/blok grubundaki çizgiler yanlış birleştirmeyi önlemek için ayrı aktarıldı. Bağlantıları kontrol edin.");
        if(opaqueLinesSeparated)warnings.Add("Uygulama veya genişletilmiş verisi bulunan LINE kayıtları anlamları bilinmediği için ayrı aktarıldı; bağlantıları ve kaynak semantiğini kontrol edin.");
        // Keep safety-relevant reasons complete even when the general
        // unsupported-type summary is truncated or full of vendor types.
        if(skippedGeometryReasons.Count>0)warnings.Add("Atlanan DXF geometrisi: "+string.Join(", ",skippedGeometryReasons.Order(StringComparer.Ordinal)));
        if (blockGeometry) warnings.Add("BLOCK tanım geometrisi doğrudan alındı; INSERT yerleşim/dönüşümleri ve katman görünürlüğü mirası uygulanmaz. AAMA/ASTM beden, çentik ve iplik yönü semantiği doğrulanmadı.");
        if(unknownLayerUsed)warnings.Add("DXF katman görünürlüğü bilinmiyor: katman tablosu veya gerekli durum bilgisi eksik. Görünürlük ve viewport ayarlarını doğrulayın.");
        if(nonPlotLayerUsed)warnings.Add("DXF plotlanmayan katman bilgisi aktarılmaz; yazdırma/kesim kapsamını doğrulayın.");
        if(unknownLineTypeUsed)warnings.Add("DXF çizgi tipi mirası doğrulanamadı (katman tipi eksik veya BYBLOCK); bu geometri düz çizgi olarak gösterilir. Kesim/dikiş amacını kaynak dosyada doğrulayın.");
        if(colorsUsed)warnings.Add("DXF renk bilgileri projede korunmaz; çizgilerin kesim, dikiş veya diğer anlamlarını kaynak dosyada doğrulayın.");
        if(unknownColorUsed)warnings.Add("DXF renk mirası doğrulanamadı; BYLAYER veya BYBLOCK kaynak renklerini kontrol edin.");
        if(modelLayerExamined && (hasViewportData || viewportLayerUsed))warnings.Add("DXF viewport'a özel katman görünürlüğü doğrulanmaz; görünürlük ve kesim kapsamını kontrol edin.");
        var unsupportedSummary=string.Join(", ",unsupported.Order())+(unsupportedSummaryTruncated ? "; özet kısaltıldı" : "");
        if (unsupported.Count > 0 && strictImport) throw new InvalidDataException("Kontrollü aktarım sırasında desteklenmeyen DXF verisi bulundu: "+unsupportedSummary);
        if (unsupported.Count > 0) warnings.Add("Atlanan desteklenmeyen DXF verileri: " + unsupportedSummary);
        if (doc.Pieces.Count == 0) throw new InvalidDataException("Desteklenen 2D kontur bulunamadı. " + string.Join(" ", warnings));
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.Validate(doc);
        cancellationToken.ThrowIfCancellationRequested();
        // Even an excluded INSERT can reference definitions that this legacy
        // reader imports directly as ordinary pieces. Without resolving every
        // reference, their presence is not evidence of a correct source scope.
        if(skippedInsertEntities && blockGeometry)skippedRelevantInsertEntities=true;
        var supplementalGeometry=CaptureSupplementalGeometry(entities,cancellationToken,strictImport ? null : skippedPolylines,skippedPolylinesTruncated);
        var skippedDetails=strictImport ? null : CaptureSkippedSourceDetails(entities,skippedRecords,skippedRecordsTruncated,cancellationToken);
        if(supplementalGeometry.Truncated || skippedRecordsTruncated || skippedDetails?.Truncated==true)
            warnings.Add("DXF atlanan veya ek kayıt envanteri kesildi; eksik kayıtlar için özgün dosyaya başvurun.");
        var approvedLayers=strict is null ? ImmutableArray<string>.Empty : strict.ApprovedLayers.ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (bulgeToleranceMm is not null) ProjectStorage.Validate(doc);
        if (bulgeToleranceMm is double sampledTolerance)
            warnings.Add(convertedBulgeSegments > 0
                ? $"Bulge eğrileri örneklendi: {convertedBulgeContours} kontur, {convertedBulgeSegments} yay; istenen nominal sapma {DisplayFactor(sampledTolerance)} mm. Sayısal/üretim doğruluğu garanti edilmez; kaynağı kontrol edin."
                : $"Bulge seçeneği etkin; dönüştürülen yay yok. Seçilen nominal sapma {DisplayFactor(sampledTolerance)} mm; kaynağı kontrol edin.");
        var importParameters=new DxfImportParameters(encoding,millimetersPerUnit,strictImport ? DxfImportMode.Controlled : DxfImportMode.General,
            strict?.ComponentToleranceMm,approvedLayers) { BulgeToleranceMm = bulgeToleranceMm };
        return new DxfImportResult(doc, warnings) { Units = units, SourceSha256=sourceSha256, SourceByteLength=sourceByteLength, ImportParameters=importParameters,
            SkippedInsertEntities=skippedInsertEntities, SkippedRelevantInsertEntities=skippedRelevantInsertEntities, ImportedBlockDefinitions=blockGeometry, MetadataLoss=metadataLoss, MetadataSources=metadataSources, OpaqueMetadata=opaqueMetadata, Annotations=annotations, SupplementalGeometry=supplementalGeometry,
            AnnotationSnapshotSha256=sourceSha256, AnnotationSnapshotByteLength=sourceByteLength,
            CapturedSkippedRelevantInsertEntities=skippedRelevantInsertEntities, CapturedImportedBlockDefinitions=blockGeometry, CapturedWarnings=warnings.ToImmutableArray(),
            SkippedSourceRecords=strictImport ? null : new DxfSkippedSourceReport(skippedRecords.ToImmutable(),skippedRecordsTruncated), SkippedSourceDetails=skippedDetails, SourceGeometry=sourceGeometry, SourceObjects=sourceObjects,
            PieceSources=pieceSources.ToImmutable(), PieceSourcesFingerprint=NestingSnapshot.ComputeLayoutFingerprint(doc,cancellationToken) };
    }

    private static DxfSourceGeometryReport CaptureOtherSourceRecords(IReadOnlyList<Entity> entities,CancellationToken token,bool objectsOnly=false)
    {
        const int maxRecords=4096,maxGroups=32768,maxCharacters=1_000_000;
        var records=ImmutableArray.CreateBuilder<DxfSourceGeometryRecord>();
        var groupCount=0;var characterCount=0;var truncated=false;
        string section="",block="";
        for(var ordinal=0;ordinal<entities.Count;ordinal++)
        {
            token.ThrowIfCancellationRequested();
            var entity=entities[ordinal];
            if(entity.Type=="SECTION") { section=entity.Text(2);block="";continue; }
            if(entity.Type=="ENDSEC") { section="";block="";continue; }
            if(!objectsOnly && section=="BLOCKS" && entity.Type=="BLOCK") { block=entity.Text(2);continue; }
            if(!objectsOnly && entity.Type=="ENDBLK") { block="";continue; }
            if(objectsOnly)
            {
                if(section!="OBJECTS")continue;
            }
            else if(section is not ("ENTITIES" or "BLOCKS") || entity.Type is "LINE" or "LWPOLYLINE" or "POLYLINE" or "VERTEX" or "SEQEND" or "INSERT" or "TEXT" or "MTEXT" or "ATTRIB" or "ATTDEF" or "POINT" or "ARC" or "CIRCLE" or "ELLIPSE" or "SPLINE")continue;
            if(records.Count>=maxRecords || entity.Data.Count>maxGroups-groupCount) { truncated=true;break; }
            var characters=0;var tooLarge=false;
            foreach(var group in entity.Data)
            {
                token.ThrowIfCancellationRequested();
                if(group.RawValue.Length>maxCharacters-characterCount-characters) { tooLarge=true;break; }
                characters+=group.RawValue.Length;
            }
            if(tooLarge) { truncated=true;break; }
            var groups=ImmutableArray.CreateBuilder<DxfAnnotationGroup>(entity.Data.Count);
            var ordinals=ImmutableArray.CreateBuilder<int>(entity.Data.Count);
            foreach(var group in entity.Data)
            {
                token.ThrowIfCancellationRequested();
                groups.Add(new DxfAnnotationGroup(group.Code,group.RawValue) { GroupOrdinal=group.GroupOrdinal });ordinals.Add(group.GroupOrdinal);
            }
            records.Add(new DxfSourceGeometryRecord(ordinal,section,block.Length==0 ? null : block,entity.Type,groups.MoveToImmutable()) { GroupOrdinals=ordinals.MoveToImmutable() });
            groupCount+=entity.Data.Count;characterCount+=characters;
        }
        return new(records.ToImmutable(),truncated);
    }

    private static DxfSkippedSourceDetailReport CaptureSkippedSourceDetails(IReadOnlyList<Entity> entities,
        IEnumerable<DxfSkippedSourceRecord> skipped,bool skippedTruncated,CancellationToken token)
    {
        const int maxGroups=32768,maxCharacters=1_000_000;
        var records=ImmutableArray.CreateBuilder<DxfSkippedSourceDetail>();
        var totalGroups=0;var totalCharacters=0;var truncated=skippedTruncated;
        foreach(var source in skipped)
        {
            token.ThrowIfCancellationRequested();
            var entity=entities[source.SourceOrdinal];
            if(entity.Data.Count>maxGroups-totalGroups) { truncated=true;break; }
            var characters=0;var tooLarge=false;
            foreach(var group in entity.Data)
            {
                token.ThrowIfCancellationRequested();
                if(group.RawValue.Length>maxCharacters-totalCharacters-characters) { tooLarge=true;break; }
                characters+=group.RawValue.Length;
            }
            if(tooLarge)
            { truncated=true;break; }
            var groups=ImmutableArray.CreateBuilder<DxfAnnotationGroup>(entity.Data.Count);
            foreach(var group in entity.Data) { token.ThrowIfCancellationRequested();groups.Add(new DxfAnnotationGroup(group.Code,group.RawValue) { GroupOrdinal=group.GroupOrdinal }); }
            records.Add(new(source,groups.MoveToImmutable()));
            totalGroups+=entity.Data.Count;totalCharacters+=characters;
        }
        return new(records.ToImmutable(),truncated);
    }

    private static DxfSupplementalGeometryReport CaptureSupplementalGeometry(IReadOnlyList<Entity> entities,CancellationToken token,
        IReadOnlySet<int>? skippedPolylines=null,bool skippedPolylinesTruncated=false,bool includePolylineCandidates=false)
    {
        const int maxRecords=4096,maxGroups=32768,maxCharacters=1_000_000;
        var records=ImmutableArray.CreateBuilder<DxfSupplementalGeometryRecord>();
        var totalGroups=0;var totalCharacters=0;var totalRecords=0;var truncated=false;
        string section="",block="";
        for(var ordinal=0;ordinal<entities.Count;ordinal++)
        {
            token.ThrowIfCancellationRequested();
            var entity=entities[ordinal];
            if(entity.Type=="SECTION") { section=entity.Text(2);block="";continue; }
            if(entity.Type=="ENDSEC") { section="";block="";continue; }
            if(section=="BLOCKS" && entity.Type=="BLOCK") { block=entity.Text(2);continue; }
            if(entity.Type=="ENDBLK") { block="";continue; }
            if(section is not ("ENTITIES" or "BLOCKS"))continue;
            if(entity.Type is not ("POINT" or "ARC" or "CIRCLE" or "ELLIPSE" or "SPLINE") &&
               !((entity.Type is "POLYLINE" or "LWPOLYLINE") && (includePolylineCandidates || skippedPolylines?.Contains(ordinal)==true)))continue;
            var last=ordinal;
            if(entity.Type=="POLYLINE")
            {
                while(last+1<entities.Count && entities[last+1].Type=="VERTEX") { token.ThrowIfCancellationRequested();last++; }
                if(last+1>=entities.Count || entities[last+1].Type!="SEQEND") { truncated=true;break; }
                last++;
            }
            var recordCount=last-ordinal+1;
            if(recordCount>maxRecords-totalRecords) { truncated=true;break; }
            var groupCount=0;var characters=0;
            for(var index=ordinal;index<=last && !truncated;index++)
            {
                token.ThrowIfCancellationRequested();
                var source=entities[index];
                if(source.Data.Count>maxGroups-totalGroups-groupCount) { truncated=true;break; }
                groupCount+=source.Data.Count;
                foreach(var group in source.Data)
                {
                    token.ThrowIfCancellationRequested();
                    if(group.RawValue.Length>maxCharacters-totalCharacters-characters) { truncated=true;break; }
                    characters+=group.RawValue.Length;
                }
            }
            if(truncated)break;
            ImmutableArray<DxfAnnotationGroup> CopyGroups(Entity source)
            {
                var groups=ImmutableArray.CreateBuilder<DxfAnnotationGroup>(source.Data.Count);
                foreach(var group in source.Data) { token.ThrowIfCancellationRequested();groups.Add(new DxfAnnotationGroup(group.Code,group.RawValue) { GroupOrdinal=group.GroupOrdinal }); }
                return groups.MoveToImmutable();
            }
            var children=ImmutableArray.CreateBuilder<DxfSupplementalChildRecord>(last-ordinal);
            for(var index=ordinal+1;index<=last;index++)
            { token.ThrowIfCancellationRequested();children.Add(new(index,entities[index].Type,CopyGroups(entities[index]))); }
            records.Add(new DxfSupplementalGeometryRecord(ordinal,section,block.Length==0 ? null : block,entity.Type,CopyGroups(entity)) { Children=children.MoveToImmutable() });
            totalRecords+=recordCount;totalGroups+=groupCount;totalCharacters+=characters;
            ordinal=last;
        }
        return new DxfSupplementalGeometryReport(records.ToImmutable(),truncated || skippedPolylinesTruncated)
            { SkippedPolylinesAssessed=skippedPolylines is not null, IncludesUnassessedPolylineCandidates=includePolylineCandidates };
    }

    // Header declarations are hints, not an encoding detector. Only inspect
    // real HEADER variables after application-private groups have been removed.
    private static void AddEncodingHeaderWarnings(IReadOnlyList<Entity> entities,DxfTextEncoding selected,byte[] raw,int length,bool utf8Bom,List<string> warnings,CancellationToken token)
    {
        var headers=new List<Entity>();var malformedSection=false;
        foreach(var entity in entities)
        {
            token.ThrowIfCancellationRequested();
            if(entity.Type!="SECTION")continue;
            var names=entity.Data.TakeWhile(p=>p.Code!=9).Where(p=>p.Code==2).ToArray();
            if(!names.Any(p=>p.Value=="HEADER"))continue;
            if(names.Length!=1)malformedSection=true;
            headers.Add(entity);
        }
        if(headers.Count==0)return;
        if(headers.Count!=1 || malformedSection)
        { warnings.Add("DXF kodlama başlığı belirsiz: HEADER bölümü yinelenmiş veya geçersiz; seçilen kodlama değiştirilmedi.");return; }
        var data=headers[0].Data;
        (string? Value,bool Ambiguous) ReadVariable(string name,int valueCode)
        {
            string? result=null;var found=0;var malformed=false;
            for(var i=0;i<data.Count;i++)
            {
                token.ThrowIfCancellationRequested();
                if(data[i].Code!=9 || data[i].Value!=name)continue;
                found++;var values=new List<string>();
                for(var j=i+1;j<data.Count && data[j].Code!=9;j++)
                { token.ThrowIfCancellationRequested();if(data[j].Code==valueCode)values.Add(data[j].Value); }
                if(values.Count!=1 || string.IsNullOrWhiteSpace(values[0]))malformed=true;
                else result=values[0];
            }
            return (result,malformed || found>1);
        }
        var version=ReadVariable("$ACADVER",1);var codepage=ReadVariable("$DWGCODEPAGE",3);
        if(version.Ambiguous || codepage.Ambiguous)
        { warnings.Add("DXF sürüm veya kod sayfası başlığı yinelenmiş ya da geçersiz; seçilen kodlamayı kaynakla doğrulayın.");return; }
        bool? modern=version.Value switch
        {
            "AC1006" or "AC1009" or "AC1012" or "AC1014" or "AC1015" or "AC1018" => false,
            "AC1021" or "AC1024" or "AC1027" or "AC1032" => true,
            _ => null
        };
        if(version.Value is not null && modern is null)
            warnings.Add("DXF sürüm başlığı bilinmiyor; seçilen kodlamayı kaynakla doğrulayın.");
        var nonAscii=utf8Bom;
        for(var i=0;i<length && !nonAscii;i++) { if((i&8191)==0)token.ThrowIfCancellationRequested();nonAscii=raw[i]>=128; }
        if(!nonAscii)return; // ASCII and its Unicode escapes have the same bytes in both encodings.
        if(modern==true && selected!=DxfTextEncoding.Utf8)
            warnings.Add($"DXF sürüm başlığı UTF-8 bekleyen 2007 veya sonrası; seçilen Windows-{(selected==DxfTextEncoding.Windows1254 ? 1254 : 1252)} farklıdır. Kodlama değiştirilmedi; kaynak dosyayı doğrulayın.");
        else if(modern==false && selected==DxfTextEncoding.Utf8)
            warnings.Add("DXF sürüm başlığı 2004 veya öncesi, seçilen kodlama UTF-8; ASCII dışı metnin kaynağını doğrulayın. Kodlama değiştirilmedi.");
        if(modern!=true && selected!=DxfTextEncoding.Utf8 && codepage.Value is { } page && !page.Equals(selected==DxfTextEncoding.Windows1254 ? "ANSI_1254" : "ANSI_1252",StringComparison.OrdinalIgnoreCase))
            warnings.Add($"DXF kod sayfası başlığı seçilen Windows-{(selected==DxfTextEncoding.Windows1254 ? 1254 : 1252)} ile farklı veya bilinmiyor; başlık kesin kanıt değildir. Kaynağı doğrulayın.");
    }

    // $INSUNITS is a HEADER insertion-scale hint, not proof of physical dimensions.
    private static DxfUnitInfo ReadUnits(List<Pair> pairs, CancellationToken cancellationToken)
    {
        var matches = new List<List<Pair>>(); string section = "";
        for (int i = 0; i < pairs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = pairs[i];
            if (p.Code == 0 && p.Value == "SECTION") { section = i + 1 < pairs.Count && pairs[i + 1].Code == 2 ? pairs[i + 1].Value : ""; continue; }
            if (p.Code == 0 && p.Value == "ENDSEC") { section = ""; continue; }
            if (section != "HEADER" || p.Code != 9 || p.Value != "$INSUNITS") continue;
            var data = new List<Pair>();
            while (i + 1 < pairs.Count && pairs[i + 1].Code != 9 && pairs[i + 1].Code != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                data.Add(pairs[++i]);
            }
            matches.Add(data);
        }
        if (matches.Count == 0) return new(null, "Birim başlığı yok", null, false);
        if (matches.Count != 1) return new(null, "Yinelenmiş birim başlığı", null, false);
        var values = matches[0].Where(x => x.Code == 70).ToArray();
        if (values.Length != 1 || !int.TryParse(values[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            return new(null, "Geçersiz birim başlığı", null, false);
        return code switch
        {
            0 => new(code, "Birimsiz", null, false),
            1 => new(code, "inç", 25.4, true), 2 => new(code, "feet", 304.8, true),
            3 => new(code, "kara mili", 1609344, true), 4 => new(code, "mm", 1, true),
            5 => new(code, "cm", 10, true), 6 => new(code, "metre", 1000, true),
            7 => new(code, "kilometre", 1000000, true), 8 => new(code, "mikroinç", 0.0000254, true),
            9 => new(code, "mil (binde inç)", 0.0254, true), 10 => new(code, "yard", 914.4, true),
            11 => new(code, "angstrom", 1e-7, true), 12 => new(code, "nanometre", 1e-6, true),
            13 => new(code, "mikron", 0.001, true), 14 => new(code, "desimetre", 100, true),
            15 => new(code, "dekametre", 10000, true), 16 => new(code, "hektometre", 100000, true),
            17 => new(code, "gigametre", 1e12, true),
            _ => new(code, $"Bilinmeyen veya desteklenmeyen birim kodu {code}", null, false)
        };
    }
    private static bool HasUnsupportedPlane(Entity e)
    {
        bool unsupported=false;
        foreach(var code in new[] { 30,31,38,39,210,220,230 })
        {
            var values=e.Data.Where(p=>p.Code==code).ToArray();
            if(values.Length>1)throw new InvalidDataException($"DXF {e.Type} için {code} alanı yinelenmiş.");
            if(values.Length==0)continue;
            var actual=ReadCoordinate(values[0].Value);
            // Exact source decimals matter even if binary64 rounds to 0 or 1.
            unsupported |= Canonicalize(actual) != (code==230
                ? new CanonicalDecimal(false,"1",0) : new CanonicalDecimal(false,"0",0));
        }
        return unsupported;
    }
    private readonly record struct LayerState(bool Hidden,bool Unknown,bool NonPlot,bool ViewportDefaultFrozen,string? LineType,string? Color,bool HasTransparency);

    // These attributes are not retained by the project. Preserve distinct
    // source declarations while joining LINEs; inheritance equivalence is not
    // inferred. This prevents different declarations from becoming one path.
    private static string ResolveLineAppearanceKey(Entity entity)
    {
        string Field(int code)
        {
            var values=entity.Data.Where(p=>p.Code==code).ToArray();
            if(values.Length>1)throw new InvalidDataException($"DXF {entity.Type} için {code} alanı yinelenmiş.");
            if(values.Length==0)return "missing";
            if(!long.TryParse(values[0].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number) || number<int.MinValue || number>uint.MaxValue)
                throw new InvalidDataException($"DXF {entity.Type} için {code} alanı geçersiz.");
            return number.ToString(CultureInfo.InvariantCulture);
        }
        return Field(370)+"/"+Field(440);
    }

    // Colors are not persisted by the project model. Preserve their identity
    // during LINE chaining so different source meanings cannot be joined.
    private static string ResolveEntityColor(Entity entity,string section,string layer,Dictionary<string,LayerState> layers,out bool unknown)
    {
        int? ReadInteger(int code)
        {
            var values=entity.Data.Where(p=>p.Code==code).ToArray();
            if(values.Length>1)throw new InvalidDataException($"DXF {entity.Type} için {code} renk alanı yinelenmiş.");
            if(values.Length==0)return null;
            if(!int.TryParse(values[0].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number))
                throw new InvalidDataException($"DXF {entity.Type} için {code} renk alanı geçersiz.");
            return number;
        }
        var aci=ReadInteger(62);var rgb=ReadInteger(420);
        if(aci is <0 or >256 || rgb is <0 or >0xFFFFFF)
            throw new InvalidDataException($"DXF {entity.Type} renk değeri geçersiz.");
        var names=entity.Data.Where(p=>p.Code==430).ToArray();
        if(names.Length>1 || (names.Length==1 && string.IsNullOrWhiteSpace(names[0].Value)))
            throw new InvalidDataException($"DXF {entity.Type} renk adı boş veya yinelenmiş.");
        if(rgb is not null) { unknown=false;return $"RGB:{rgb.Value}"; }
        if(names.Length==1) { unknown=true;return "NAME:"+names[0].Value; }
        if(aci is >=1 and <=255) { unknown=false;return $"ACI:{aci.Value}"; }
        if(aci==0) { unknown=true;return "BYBLOCK:belirsiz"; }
        if(section=="BLOCKS" && layer.Equals("0",StringComparison.OrdinalIgnoreCase))
        { unknown=true;return "BYLAYER:blok-mirası-belirsiz"; }
        if(layers.TryGetValue(layer,out var state) && state.Color is not null)
        {
            unknown=state.Color.StartsWith("NAME:",StringComparison.Ordinal);
            return state.Color;
        }
        unknown=true;return "BYLAYER:belirsiz";
    }
    private static Dictionary<string,LayerState> ReadLayerStates(IReadOnlyList<Entity> entities,CancellationToken cancellationToken)
    {
        var states=new Dictionary<string,LayerState>(StringComparer.OrdinalIgnoreCase);
        var inTables=false;var inLayerTable=false;
        foreach(var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(entity.Type=="SECTION") { if(inLayerTable)throw new InvalidDataException("DXF LAYER tablosu kapatılmamış.");inTables=entity.Text(2)=="TABLES";inLayerTable=false;continue; }
            if(entity.Type=="ENDSEC") { if(inLayerTable)throw new InvalidDataException("DXF LAYER tablosu kapatılmamış.");inTables=false;inLayerTable=false;continue; }
            if(!inTables)continue;
            if(entity.Type=="TABLE") { if(inLayerTable)throw new InvalidDataException("DXF LAYER tablosu kapatılmamış.");inLayerTable=entity.Text(2)=="LAYER";continue; }
            if(entity.Type=="ENDTAB") { inLayerTable=false;continue; }
            if(!inLayerTable || entity.Type!="LAYER")continue;
            var names=entity.Data.Where(p=>p.Code==2).ToArray();
            if(names.Length!=1 || string.IsNullOrWhiteSpace(names[0].RawValue))throw new InvalidDataException("DXF katman adı eksik veya yinelenmiş.");
            int? ReadOptionalInteger(int code)
            {
                var values=entity.Data.Where(p=>p.Code==code).ToArray();
                if(values.Length>1)throw new InvalidDataException($"DXF katmanında {code} alanı yinelenmiş.");
                if(values.Length==0)return null;
                if(!int.TryParse(values[0].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number))
                    throw new InvalidDataException($"DXF katmanında {code} alanı geçersiz.");
                return number;
            }
            var flags=ReadOptionalInteger(70);var color=ReadOptionalInteger(62);var plot=ReadOptionalInteger(290);var rgb=ReadOptionalInteger(420);
            if(color is < -255 or >256 || rgb is <0 or >0xFFFFFF)throw new InvalidDataException("DXF katman renk değeri geçersiz.");
            var colorNames=entity.Data.Where(p=>p.Code==430).ToArray();
            if(colorNames.Length>1 || (colorNames.Length==1 && string.IsNullOrWhiteSpace(colorNames[0].Value)))
                throw new InvalidDataException("DXF katman renk adı boş veya yinelenmiş.");
            var layerColor=rgb is not null ? $"RGB:{rgb.Value}" : colorNames.Length==1 ? "NAME:"+colorNames[0].Value :
                color is >=1 and <=255 ? $"ACI:{color.Value}" : color is >=-255 and <=-1 ? $"ACI:{-color.Value}" : null;
            if(flags<0 || plot is not (null or 0 or 1))throw new InvalidDataException("DXF katman bayrağı geçersiz.");
            var hidden=(flags.GetValueOrDefault() & 1)!=0 || color<0;
            var unknown=flags is null || color is null || (flags.GetValueOrDefault() & 16)!=0 || (flags.GetValueOrDefault() & ~119)!=0;
            var lineTypes=entity.Data.Where(p=>p.Code==6).ToArray();
            if(lineTypes.Length>1 || (lineTypes.Length==1 && string.IsNullOrWhiteSpace(lineTypes[0].Value)))
                throw new InvalidDataException("DXF katman çizgi tipi boş veya yinelenmiş.");
            var lineType=lineTypes.Length==0 ? null : lineTypes[0].Value.Trim();
            if(lineType is not null && (lineType.Equals("BYLAYER",StringComparison.OrdinalIgnoreCase) || lineType.Equals("BYBLOCK",StringComparison.OrdinalIgnoreCase)))lineType=null;
            if(!states.TryAdd(DecodeLayerName(names[0].RawValue),new LayerState(hidden,unknown,plot==0,(flags.GetValueOrDefault() & 2)!=0,lineType,layerColor,entity.Data.Any(pair=>pair.Code==440))))
                throw new InvalidDataException("DXF katman adı büyük/küçük harf duyarsız karşılaştırmada yinelenmiş.");
        }
        if(inLayerTable)throw new InvalidDataException("DXF LAYER tablosu kapatılmamış.");
        return states;
    }

    // DXF string values may encode a UTF-16 unit as \U+HHHH. A literal
    // backslash is not a valid layer-name character in the supported subset;
    // reject ambiguous or malformed escapes instead of creating wrong identities.
    private static string DecodeLayerName(string value)
    {
        var result=new StringBuilder(value.Length);
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            if(c=='\\')
            {
                if(i+6>=value.Length || value[i+1]!='U' || value[i+2]!='+' ||
                    !ushort.TryParse(value.AsSpan(i+3,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var unit))
                    throw new InvalidDataException("DXF katman adında geçersiz veya belirsiz Unicode kaçışı var.");
                c=(char)unit;i+=6;
            }
            if(char.IsHighSurrogate(c))
            {
                if(i+1<value.Length && char.IsLowSurrogate(value[i+1]))
                { result.Append(c).Append(value[++i]);continue; }
                if(i+7>=value.Length || value[i+1]!='\\' || value[i+2]!='U' || value[i+3]!='+' ||
                    !ushort.TryParse(value.AsSpan(i+4,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var low) || !char.IsLowSurrogate((char)low))
                    throw new InvalidDataException("DXF katman adındaki Unicode vekil çifti geçersiz.");
                result.Append(c).Append((char)low);i+=7;continue;
            }
            if(char.IsLowSurrogate(c) || char.IsControl(c) || c=='\\' || c is '\u2028' or '\u2029')
                throw new InvalidDataException("DXF katman adında desteklenmeyen Unicode karakteri var.");
            result.Append(c);
        }
        var name=result.ToString();
        if(name.Length==0 || name!=name.Trim())throw new InvalidDataException("DXF katman adı boş veya başında/sonunda boşluk içeriyor.");
        return name;
    }

    // This is only evidence for an exclusion, never evidence that an INSERT is
    // visible. For INSERT/BLOCK the ordinary geometry reader does not interpret
    // color, so an ambiguous color must not make an exclusion trustworthy.
    private static bool HasUnambiguousExclusionMetadata(Entity entity,CancellationToken cancellationToken)
    {
        int? visibility=null,space=null,color=null;
        string? layout=null;
        foreach(var pair in entity.Data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(pair.Code is 60 or 67 or 62)
            {
                if(!int.TryParse(pair.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value))return false;
                if(pair.Code==60) { if(visibility.HasValue || value is not (0 or 1))return false; visibility=value; }
                else if(pair.Code==67) { if(space.HasValue || value is not (0 or 1))return false; space=value; }
                else { if(color.HasValue || value < -255 || value > 256)return false; color=value; }
            }
            else if(pair.Code==410)
            {
                if(layout is not null)return false;
                layout=pair.Value.Trim();
                if(layout.Length==0)return false;
            }
        }
        if(space.HasValue && layout is not null &&
            (space.Value==0)!=layout.Equals("Model",StringComparison.OrdinalIgnoreCase))return false;
        return true;
    }

    // Model-space visible entities only. Layout names and flags are kept out of
    // LINE group keys by excluding non-model data before segment collection.
    private static string? ExcludedSpaceOrVisibility(Entity entity,CancellationToken cancellationToken)
    {
        var common=new List<Pair>();
        foreach(var pair in entity.Data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(pair.Code is 60 or 62 or 67 or 410)common.Add(pair);
        }
        int? ReadFlag(int code)
        {
            var values=common.Where(p=>p.Code==code).ToArray();
            if(values.Length>1)throw new InvalidDataException($"DXF {entity.Type} için {code} bayrağı yinelenmiş.");
            if(values.Length==0)return null;
            if(!int.TryParse(values[0].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var flag) || flag is not (0 or 1))
                throw new InvalidDataException($"DXF {entity.Type} için {code} bayrağı geçersiz.");
            return flag;
        }
        var visibility=ReadFlag(60);var space=ReadFlag(67);
        int? entityColor=null;
        if(entity.Type is "LINE" or "LWPOLYLINE" or "POLYLINE" or "VERTEX")
        {
            var colors=common.Where(p=>p.Code==62).ToArray();
            if(colors.Length>1)throw new InvalidDataException($"DXF {entity.Type} için 62 renk alanı yinelenmiş.");
            if(colors.Length==1)
            {
                if(!int.TryParse(colors[0].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var color) || color < -255 || color > 256)
                    throw new InvalidDataException($"DXF {entity.Type} için 62 renk değeri geçersiz.");
                entityColor=color;
            }
        }
        var layouts=common.Where(p=>p.Code==410).ToArray();
        if(layouts.Length>1)throw new InvalidDataException($"DXF {entity.Type} için 410 yerleşimi yinelenmiş.");
        var layout=layouts.Length==0 ? null : layouts[0].Value.Trim();
        if(layout is not null && layout.Length==0)throw new InvalidDataException("DXF yerleşim adı boş.");
        if(visibility==1)return "görünmez DXF nesnesi";
        if(entityColor<0)return "negatif renk kodlu kapalı DXF nesnesi";
        if(space==1)return "kağıt alanı DXF nesnesi";
        if(layout is not null && !layout.Equals("Model",StringComparison.OrdinalIgnoreCase))return "model dışı veya belirsiz yerleşim DXF nesnesi";
        return null;
    }
    private static bool HasNonzeroPolylineWidth(Entity e,CancellationToken cancellationToken,params int[] codes)
    {
        bool nonzero=false;
        foreach(var group in e.Data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(Array.IndexOf(codes,group.Code)>=0 && IsNonzeroDecimal(group.Value)) nonzero=true;
        }
        return nonzero;
    }
    private static bool Near(Point2 a, Point2 b) => Math.Abs(a.X - b.X) <= 1e-6 && Math.Abs(a.Y - b.Y) <= 1e-6;
    private static bool HasAmbiguousLineConnections(IReadOnlyList<(Point2 A, Point2 B)> edges)
        => HasAmbiguousLineConnectionsCancellable(edges, CancellationToken.None);

    internal static bool HasAmbiguousLineConnectionsCancellable(IReadOnlyList<(Point2 A, Point2 B)> edges, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Both endpoint proximity and the narrow intersection test require
        // overlap of bounding boxes with this tolerance. Sweep by minimum X;
        // the existing 4096-LINE limit still bounds the dense worst case.
        var matches = new byte[edges.Count * 2];
        if (edges.Any(e => !e.A.IsFinite || !e.B.IsFinite))
        {
            // Preserve the previous behavior for malformed coordinates; the
            // subsequent project validation remains responsible for rejection.
            for (var i = 0; i < edges.Count; i++)
                for (var j = i + 1; j < edges.Count; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (AmbiguousLinePair(edges, matches, i, j)) return true;
                }
            return false;
        }
        const double tolerance = 1e-6;
        var ordered = Enumerable.Range(0, edges.Count)
            .Select(i => (Index: i, MinX: Math.Min(edges[i].A.X, edges[i].B.X),
                MaxX: Math.Max(edges[i].A.X, edges[i].B.X),
                MinY: Math.Min(edges[i].A.Y, edges[i].B.Y),
                MaxY: Math.Max(edges[i].A.Y, edges[i].B.Y)))
            .OrderBy(e => e.MinX).ThenBy(e => e.Index).ToArray();
        var active = new List<int>();
        for (var position = 0; position < ordered.Length; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ordered[position];
            for (var k = active.Count - 1; k >= 0; k--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var other = ordered[active[k]];
                if (other.MaxX + tolerance < current.MinX) { active.RemoveAt(k); continue; }
                if (other.MaxY + tolerance < current.MinY || current.MaxY + tolerance < other.MinY) continue;
                var i = Math.Min(other.Index, current.Index);
                var j = Math.Max(other.Index, current.Index);
                if (AmbiguousLinePair(edges, matches, i, j)) return true;
            }
            active.Add(position);
        }
        return false;
    }
    private static bool AmbiguousLinePair(IReadOnlyList<(Point2 A, Point2 B)> edges, byte[] matches, int i, int j)
    {
        var ei=edges[i]; var ej=edges[j];
        var aa = Near(ei.A, ej.A);
        var ab = Near(ei.A, ej.B);
        var ba = Near(ei.B, ej.A);
        var bb = Near(ei.B, ej.B);
        // Preserve the previous exact decision for every candidate pair.
        if ((aa && ei.A != ej.A) || (ab && ei.A != ej.B) ||
            (ba && ei.B != ej.A) || (bb && ei.B != ej.B)) return true;
        if ((aa && bb) || (ab && ba)) return true;
        if (SegmentsHaveAmbiguousIntersection(ei.A, ei.B, ej.A, ej.B, aa || ab || ba || bb)) return true;
        if (aa && (++matches[2*i] > 1 || ++matches[2*j] > 1)) return true;
        if (ab && (++matches[2*i] > 1 || ++matches[2*j+1] > 1)) return true;
        if (ba && (++matches[2*i+1] > 1 || ++matches[2*j] > 1)) return true;
        if (bb && (++matches[2*i+1] > 1 || ++matches[2*j+1] > 1)) return true;
        return false;
    }
    private static bool SegmentsHaveAmbiguousIntersection(Point2 a, Point2 b, Point2 c, Point2 d, bool sharedEndpoint)
    {
        const double tolerance = 1e-6;
        // Bounded broad phase. An uncertain or near contact is treated as an
        // ambiguity, never as evidence that lines should be joined.
        if (Math.Max(a.X, b.X) + tolerance < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) + tolerance < Math.Min(a.X, b.X) ||
            Math.Max(a.Y, b.Y) + tolerance < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) + tolerance < Math.Min(a.Y, b.Y)) return false;
        static bool NearSegment(Point2 p, Point2 x, Point2 y)
        {
            var dx=y.X-x.X; var dy=y.Y-x.Y; var lengthSquared=dx*dx+dy*dy;
            if (lengthSquared == 0) return Near(p,x);
            var t=Math.Clamp(((p.X-x.X)*dx+(p.Y-x.Y)*dy)/lengthSquared,0,1);
            var ex=p.X-(x.X+t*dx); var ey=p.Y-(x.Y+t*dy);
            return ex*ex+ey*ey <= 1e-12;
        }
        if (sharedEndpoint)
        {
            // One exact shared endpoint is normally a valid chain. Overlap or
            // a second endpoint on the other segment is not a simple joint.
            if (a != c && a != d && NearSegment(a,c,d)) return true;
            if (b != c && b != d && NearSegment(b,c,d)) return true;
            if (c != a && c != b && NearSegment(c,a,b)) return true;
            if (d != a && d != b && NearSegment(d,a,b)) return true;
            return false;
        }
        return Geometry.SegmentsIntersect(a,b,c,d) || NearSegment(a,c,d) || NearSegment(b,c,d) || NearSegment(c,a,b) || NearSegment(d,a,b);
    }
    private static void CheckLineEndpointOrigin(Dictionary<Point2,SourcePoint> origins,Point2 scaled,SourcePoint source)
    {
        if(origins.TryGetValue(scaled,out var prior) && !SameSourcePoint(prior,source))
            throw new InvalidDataException("DXF LINE uçları ayrıştırma veya ölçekleme hassasiyeti nedeniyle birleşti; çizim aktarılmadı.");
        origins[scaled]=source;
    }
    private static Point2 ScaleSourcePoint(SourcePoint source,double factor)
    {
        double Scale(Coordinate coordinate)
        {
            var scaled=coordinate.Value*factor;
            if (scaled == 0 && IsNonzeroDecimal(coordinate.Text))
                throw new InvalidDataException("DXF koordinatı ayrıştırma veya ölçekleme hassasiyeti nedeniyle sıfıra düştü; geometri aktarılmadı.");
            return scaled;
        }
        return new Point2(Scale(source.X),Scale(source.Y));
    }
    private static Point2 ScalePolylinePoint(SourcePoint source,double factor,Dictionary<Point2,SourcePoint> scaledOrigins)
    {
        var scaled=ScaleSourcePoint(source,factor);
        try { Geometry.Validate(scaled); }
        catch(ArgumentOutOfRangeException ex) { throw new InvalidDataException("DXF polilin koordinatları ölçek sonrası çalışma sınırlarını aşıyor.",ex); }
        if(scaledOrigins.TryGetValue(scaled,out var prior) && !SameSourcePoint(prior,source))
            throw new InvalidDataException("DXF polilin noktaları ayrıştırma veya ölçekleme hassasiyeti nedeniyle birleşti; kontur aktarılmadı.");
        scaledOrigins[scaled]=source;
        return scaled;
    }
    private static DxfInsertIndex IncludeDirectGeometry(DxfInsertIndex index,int nextOrdinal,CancellationToken cancellationToken)
    {
        var blocks=index.Blocks.ToBuilder();
        var model=index.ModelEntities.ToBuilder();
        var names=new HashSet<string>(index.Blocks.Select(block=>block.Name),StringComparer.OrdinalIgnoreCase);
        var directCount=0; var lineCount=0;
        // A separate synthetic occurrence per effective source layer ensures
        // model-space lines can join only other model-space lines, never block
        // instances. The strict resolver later validates every LINE's own
        // visibility, color and linetype before any geometry is returned.
        var linesByLayer=new Dictionary<string,(DxfIndexGroup Layer,List<DxfIndexedEntity> Lines)>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in index.ModelEntities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(item.Entity.Type is not ("LWPOLYLINE" or "LINE" or "POLYLINE"))continue;
            var layerFields=item.Entity.Groups.Where(group=>group.Code==8).ToArray();
            var lineFields=item.Entity.Groups.Where(group=>group.Code==6).ToArray();
            if(layerFields.Length>1 || lineFields.Length>1)
                throw new InvalidDataException("DXF doğrudan kontur katmanı veya çizgi tipi yinelenmiş.");
            if(lineFields.Length==1 && lineFields[0].Value.Equals("BYBLOCK",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DXF doğrudan konturda BYBLOCK çizgi tipi doğrulanamıyor.");
            var layer=layerFields.Length==0 ? new DxfIndexGroup(8,"0","0") : layerFields[0];
            if(item.Entity.Type=="POLYLINE")
            {
                // These children belong to model space, not to a real INSERT.
                // The synthetic INSERT must not turn their layer 0 or BYBLOCK
                // into inherited properties that were absent in the source.
                var parentLayer=DecodeLayerName(layer.RawValue);
                foreach(var child in item.Children.Append(item.SequenceEnd ?? throw new InvalidDataException("POLYLINE SEQEND kaydı eksik.")))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var childLayers=child.Groups.Where(group=>group.Code==8).ToArray();
                    var childTypes=child.Groups.Where(group=>group.Code==6).ToArray();
                    if(childLayers.Length>1 || childTypes.Length>1)
                        throw new InvalidDataException("DXF POLYLINE alt varlığında katman veya çizgi tipi yinelenmiş.");
                    var childLayer=childLayers.Length==0 ? "0" : DecodeLayerName(childLayers[0].RawValue);
                    if(!string.Equals(parentLayer,childLayer,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("DXF model alanındaki POLYLINE ve köşe/SEQEND katmanları uyuşmuyor.");
                    if(childTypes.Length==1 && childTypes[0].Value.Equals("BYBLOCK",StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("DXF model alanındaki POLYLINE alt varlığında BYBLOCK çizgi tipi doğrulanamıyor.");
                }
            }
            if(item.Entity.Type=="LINE")
            {
                if(++lineCount>4096)throw new InvalidDataException("DXF LINE işlem sınırını aşıyor.");
                var layerName=DecodeLayerName(layer.RawValue);
                if(!linesByLayer.TryGetValue(layerName,out var lineGroup))
                {
                    lineGroup=(layer,new List<DxfIndexedEntity>());
                    linesByLayer.Add(layerName,lineGroup);
                }
                lineGroup.Lines.Add(item);
                continue;
            }
            if(++directCount>ProjectStorage.MaximumPieces)
                throw new InvalidDataException("DXF doğrudan kontur sınırını aşıyor.");
            AddDirectGroup(layer,ImmutableArray.Create(item));
        }
        foreach(var lineGroup in linesByLayer.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddDirectGroup(lineGroup.Layer,lineGroup.Lines.ToImmutableArray());
        }
        return new DxfInsertIndex(blocks.ToImmutable(),model.ToImmutable());

        void AddDirectGroup(DxfIndexGroup layer,ImmutableArray<DxfIndexedEntity> directEntities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name;
            do { name=$"__TEKSTILCAD_DIRECT_{nextOrdinal++}"; } while(!names.Add(name));
            var header=new DxfIndexEntity("BLOCK",ImmutableArray.Create(
                new DxfIndexGroup(2,name,name),new DxfIndexGroup(10,"0","0"),new DxfIndexGroup(20,"0","0")),nextOrdinal++);
            var end=new DxfIndexEntity("ENDBLK",ImmutableArray<DxfIndexGroup>.Empty,nextOrdinal++);
            blocks.Add(new DxfIndexedBlock(name,header,directEntities,end));
            var insert=new DxfIndexEntity("INSERT",ImmutableArray.Create(
                new DxfIndexGroup(2,name,name),layer,new DxfIndexGroup(10,"0","0"),new DxfIndexGroup(20,"0","0")),nextOrdinal++);
            model.Add(new DxfIndexedEntity(insert,ImmutableArray<DxfIndexEntity>.Empty,null));
        }
    }

    private static void Add(ProjectDocument d, string name, List<Point2> points, bool closed)
    {
        if (points.Count > ProjectStorage.MaximumPointsPerPiece || d.Pieces.Count >= ProjectStorage.MaximumPieces) throw new InvalidDataException("DXF geometri sınırı aşıldı.");
        if (points.Count >= 2) d.Pieces.Add(new PatternPiece { Name = name, Points = points, Closed = closed });
    }
    /// <summary>Plans unique layer names in piece order; export uses this exact mapping.</summary>
    public static IReadOnlyList<DxfLayerMapping> PlanLayers(ProjectDocument project)
    {
        ProjectStorage.ValidateForDxfExport(project);
        if (project.Pieces.Any(p => p.Points.Count < 2)) throw new InvalidDataException("DXF dışa aktarımı için her parçada en az iki nokta gerekir; eksik parçaları düzeltin.");
        foreach (var p in project.Pieces.Where(p => p.Closed))
        {
            var count = p.Points.Count;
            if (count > 1 && p.Points[0] == p.Points[^1]) count--;
            if (count < 3 || (count > 1 && p.Points[0] == p.Points[count - 1]))
                throw new InvalidDataException("Kapalı DXF konturu en az üç nokta içermeli ve kapanış noktası yalnız bir kez yinelenmelidir.");
        }
        var usedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextSuffix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DxfLayerMapping>(project.Pieces.Count);
        static string Truncate(string value, int limit)
        {
            if (value.Length <= limit) return value;
            if (limit > 0 && char.IsHighSurrogate(value[limit - 1])) limit--;
            return value[..limit];
        }
        foreach (var p in project.Pieces)
        {
            var source = p.Name ?? "0";
            var clean = new StringBuilder(source.Length);
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (char.IsHighSurrogate(c) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
                { clean.Append(c).Append(source[++i]); continue; }
                clean.Append(char.IsSurrogate(c) || char.IsControl(c) || c is '\u2028' or '\u2029' || "<>/\\\":;?*|=,".Contains(c) ? '_' : c);
            }
            var layer = clean.ToString().Trim();
            if (layer.Length == 0) layer = "0";
            // Truncation can expose trailing whitespace that the DXF reader trims.
            // Normalize before checking uniqueness so the written and read names agree.
            var baseLayer = Truncate(layer, 255).TrimEnd();
            if (baseLayer.Length == 0) baseLayer = "0";
            layer = baseLayer;
            if (!usedLayers.Add(layer))
            {
                var suffix = nextSuffix.GetValueOrDefault(baseLayer, 2);
                do
                {
                    var end = "_" + suffix++;
                    layer = Truncate(baseLayer, 255 - end.Length).TrimEnd() + end;
                } while (!usedLayers.Add(layer));
                nextSuffix[baseLayer] = suffix;
            }
            result.Add(new DxfLayerMapping(p.Id, p.Name ?? "", layer));
        }
        return result.AsReadOnly();
    }
    /// <summary>Reports metadata omitted by the geometric DXF writer and unsupported grainlines without writing a file.</summary>
    public static DxfExportAnalysis AnalyzeExport(ProjectDocument project,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.ValidateForDxfExport(project);
        var copy=project.Clone();
        ProjectStorage.ValidateForDxfExport(copy);
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint=NestingSnapshot.ComputeLayoutFingerprint(copy,cancellationToken);
        var layers=PlanLayers(copy).ToImmutableArray();
        var omissions=ImmutableArray.CreateBuilder<DxfExportOmission>();
        foreach(var piece in copy.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(piece.Metadata is { } metadata)
            {
                if(metadata.Role is not null)omissions.Add(new(piece.Id,DxfExportOmissionKind.PieceRole));
                if(metadata.Size is not null)omissions.Add(new(piece.Id,DxfExportOmissionKind.Size));
                if(metadata.Material is not null)omissions.Add(new(piece.Id,DxfExportOmissionKind.Material));
                if(metadata.Quantity is not null)omissions.Add(new(piece.Id,DxfExportOmissionKind.Quantity));
            }
            if(piece.Grainline is not null)omissions.Add(new(piece.Id,DxfExportOmissionKind.GrainlineUnsupported));
        }
        if(copy.GradingRules.Count!=0)omissions.Add(new(null,DxfExportOmissionKind.GradingRules));
        if(copy.NestingSnapshot is not null)omissions.Add(new(null,DxfExportOmissionKind.NestingSnapshot));
        if(copy.ImportProvenance is not null)omissions.Add(new(null,DxfExportOmissionKind.ImportProvenance));
        cancellationToken.ThrowIfCancellationRequested();
        return new(fingerprint,layers,omissions.ToImmutable());
    }
    public static void Export(ProjectDocument project, string path) => ExportCore(project, path, null);
    /// <summary>Export only if the selected destination still matches its captured version.</summary>
    public static void Export(ProjectDocument project, string path, ProjectFileVersion expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        ExportCore(project, path, expectedVersion);
    }
    private static void ExportCore(ProjectDocument project, string path, ProjectFileVersion? expectedVersion)
    {
        var layers = PlanLayers(project);
        GrainlineExportGuard.EnsureSupported(project);
        if(layers.Count==0)throw new InvalidDataException("DXF dışa aktarımı için en az bir parça gerekir.");
        // Handles are deterministic within this file and never shared by two
        // emitted records. The 0 owner is the root table owner, not a new handle.
        ulong nextHandle=0x10;
        string Handle() => (nextHandle++).ToString("X",CultureInfo.InvariantCulture);
        var lineTypeTableHandle=Handle();var continuousHandle=Handle();var layerTableHandle=Handle();
        var layerNames=layers.Select(mapping=>mapping.LayerName).ToList();
        if(!layerNames.Contains("0",StringComparer.OrdinalIgnoreCase))layerNames.Insert(0,"0");
        var layerHandles=layerNames.Select(_=>Handle()).ToArray();
        var entityHandles=project.Pieces.Select(_=>Handle()).ToArray();
        var seed=nextHandle.ToString("X",CultureInfo.InvariantCulture);
        // The atomic writer emits UTF-8; DXF 2007 (AC1021) defines UTF-8 text.
        var b = new StringBuilder("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1021\n9\n$INSUNITS\n70\n4\n9\n$HANDSEED\n5\n");
        b.Append(seed).Append("\n0\nENDSEC\n0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLTYPE\n5\n").Append(lineTypeTableHandle)
            .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n1\n0\nLTYPE\n5\n").Append(continuousHandle)
            .Append("\n330\n").Append(lineTypeTableHandle).Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLinetypeTableRecord\n2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0\n0\nENDTAB\n0\nTABLE\n2\nLAYER\n5\n").Append(layerTableHandle)
            .Append("\n330\n0\n100\nAcDbSymbolTable\n70\n").Append(layerNames.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for(var i=0;i<layerNames.Count;i++)
            b.Append("0\nLAYER\n5\n").Append(layerHandles[i]).Append("\n330\n").Append(layerTableHandle)
                .Append("\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\n").Append(layerNames[i]).Append("\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n");
        b.Append("0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        for (var i = 0; i < project.Pieces.Count; i++)
        {
            var p = project.Pieces[i]; var layer = layers[i].LayerName;
            var count = p.Points.Count;
            if (p.Closed && count > 1 && p.Points[0] == p.Points[^1]) count--;
            b.Append("0\nLWPOLYLINE\n5\n").Append(entityHandles[i]).Append("\n100\nAcDbEntity\n8\n").Append(layer)
                .Append("\n100\nAcDbPolyline\n90\n").Append(count).Append("\n70\n").Append(p.Closed ? 1 : 0).Append('\n');
            for (var pointIndex = 0; pointIndex < count; pointIndex++)
            {
                var pt = p.Points[pointIndex];
                b.Append("10\n").Append(F(pt.X)).Append("\n20\n").Append(F(pt.Y)).Append('\n');
            }
        }
        b.Append("0\nENDSEC\n0\nEOF\n");
        if (expectedVersion is null) ProjectStorage.AtomicWrite(path, b.ToString());
        else ProjectStorage.AtomicWriteIfUnchanged(path, b.ToString(), expectedVersion);
    }
}

