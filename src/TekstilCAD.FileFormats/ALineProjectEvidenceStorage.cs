using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Unapproved V11 project evidence. SHA-256 detects corruption, not fraud or suitability.
/// Callers must keep supplied values stable during capture; all subsequent checks use copies.</summary>
public static class ALineProjectEvidenceStorage
{
    private static double[] Copy(IReadOnlyList<double>? input,int count)
    {
        if(input is null || input.Count!=count)throw new InvalidDataException("A kesim yerleşim alanı geçersiz.");
        var result=new double[count];for(var i=0;i<count;i++)result[i]=input[i];return result;
    }
    private static ALineProjectEvidence Capture(ALineProjectEvidence e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if(e.CandidateEvidenceJson is null || e.CandidateEvidenceJson.Length>16384)throw new InvalidDataException("A kesim kanıtı geçersiz.");
        return e with { TranslationMm=Copy(e.TranslationMm,4),FrontPlacedCoordinatesMm=Copy(e.FrontPlacedCoordinatesMm,10),BackPlacedCoordinatesMm=Copy(e.BackPlacedCoordinatesMm,10) };
    }
    private static bool Same(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
    private static double[] Place(double[] native,double x,double y)
    {
        if(!double.IsFinite(x) || !double.IsFinite(y))throw new InvalidDataException("Yerleşim ötelemesi geçersiz.");
        var placed=new double[10];
        for(var i=0;i<10;i++)
        {
            var offset=i%2==0?x:y;placed[i]=native[i]+offset;
            if(!double.IsFinite(placed[i]) || !Same(placed[i]-offset,native[i]))throw new InvalidDataException("Yerleşim ötelemesi koordinat hassasiyetini korumuyor.");
        }
        Geometry.ValidateContour(Enumerable.Range(0,5).Select(i=>new Point2(placed[2*i],placed[2*i+1])).ToArray());
        return placed;
    }
    private static bool Separate(double[] a,double[] b)
    {
        static double Min(double[] p,int axis)=>Enumerable.Range(0,5).Min(i=>p[2*i+axis]);
        static double Max(double[] p,int axis)=>Enumerable.Range(0,5).Max(i=>p[2*i+axis]);
        return Max(a,0)<Min(b,0) || Max(b,0)<Min(a,0) || Max(a,1)<Min(b,1) || Max(b,1)<Min(a,1);
    }
    private static string Digest(ALineProjectEvidence e,ALineCandidateEvidence candidate)
    {
        using var memory=new MemoryStream();using(var w=new BinaryWriter(memory,new UTF8Encoding(false,true),true))
        {
            w.Write("TekstilCAD.aline-project-evidence.v1");w.Write(candidate.IntegritySha256);
            foreach(var vector in new[]{e.TranslationMm,e.FrontPlacedCoordinatesMm,e.BackPlacedCoordinatesMm})
            { w.Write(vector.Length);foreach(var value in vector)w.Write(BitConverter.DoubleToInt64Bits(value)); }
        }
        return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }
    public static ALineProjectEvidence Create(ALineCandidateEvidence candidate,IReadOnlyList<double> translationMm)
    {
        var json=ALineCandidateEvidenceStorage.Serialize(candidate);
        var detached=ALineCandidateEvidenceStorage.Deserialize(json);var translation=Copy(translationMm,4);
        var front=Place(detached.FrontCoordinatesMm,translation[0],translation[1]);var back=Place(detached.BackCoordinatesMm,translation[2],translation[3]);
        if(!Separate(front,back))throw new InvalidDataException("Panel yerleşim sınırları ayrık olmalıdır.");
        var result=new ALineProjectEvidence(json,translation,front,back,"");return result with { IntegritySha256=Digest(result,detached) };
    }
    public static void Validate(ALineProjectEvidence evidence)=>ValidateAndRead(evidence);
    internal static ALineCandidateEvidence ValidateAndRead(ALineProjectEvidence evidence)
    {
        var e=Capture(evidence);var candidate=ALineCandidateEvidenceStorage.Deserialize(e.CandidateEvidenceJson);
        var front=Place(candidate.FrontCoordinatesMm,e.TranslationMm[0],e.TranslationMm[1]);var back=Place(candidate.BackCoordinatesMm,e.TranslationMm[2],e.TranslationMm[3]);
        if(!Separate(front,back) || !front.Zip(e.FrontPlacedCoordinatesMm).All(p=>Same(p.First,p.Second)) || !back.Zip(e.BackPlacedCoordinatesMm).All(p=>Same(p.First,p.Second)) || e.IntegritySha256!=Digest(e,candidate))
            throw new InvalidDataException("A kesim yerleşim kanıtı veya bütünlük özeti uyuşmuyor.");
        return candidate;
    }
    /// <summary>Checks current panels against captured evidence; callers keep the document stable during capture. This is not approval.</summary>
    public static bool Matches(ALineProjectEvidence evidence,ProjectDocument document)
    {
        var e=Capture(evidence);ArgumentNullException.ThrowIfNull(document);
        if(document.Pieces is null || document.Pieces.Count>ProjectStorage.MaximumPieces)return false;
        var candidate=ValidateAndRead(e);
        bool Match(Guid id,string side,double[] coordinates)
        {
            var pieces=document.Pieces.Where(p=>p is not null && p.Id==id).Take(2).ToArray();
            if(pieces.Length!=1 || !pieces[0].Closed || pieces[0].Points is not { Count:5 } || pieces[0].ExperimentalALine is not { } marker || marker.Algorithm!=candidate.Algorithm || marker.SourceSha256!=candidate.SourceSha256 || marker.Side!=side)return false;
            var xy=pieces[0].Points.SelectMany(p=>new[]{p.X,p.Y}).ToArray();
            return xy.Zip(coordinates).All(pair=>Same(pair.First,pair.Second));
        }
        return Match(candidate.FrontPieceId,"front",e.FrontPlacedCoordinatesMm) && Match(candidate.BackPieceId,"back",e.BackPlacedCoordinatesMm);
    }
    /// <summary>
    /// Conservative preflight only, not authorization. Callers must keep both inputs
    /// stable throughout capture and must later recheck the actual monotonic target
    /// revision, source, approval and capacity in the atomic commit transaction.
    /// A touching box counts as a collision; false positives are intentional.
    /// </summary>
    public static void EnsureTargetClear(ALineProjectEvidence evidence,ProjectDocument target)
    {
        var captured=Capture(evidence);var candidate=ValidateAndRead(captured);
        ArgumentNullException.ThrowIfNull(target);ProjectStorage.Validate(target);
        var snapshot=target.Clone();ProjectStorage.Validate(snapshot);
        if(snapshot.NestingSnapshot is not null)throw new InvalidOperationException("Pastal geçmişi olan hedefte otomatik yerleşim desteklenmiyor.");
        if(snapshot.Pieces.Count>ProjectStorage.MaximumPieces-2 || snapshot.Pieces.Sum(p=>(long)p.Points.Count)>ProjectStorage.MaximumTotalPoints-10)
            throw new InvalidOperationException("İki panel için hedef kapasitesi yetersiz.");
        if(snapshot.ALineEvidence is { } history)
        {
            if(history.Count>=128)throw new InvalidOperationException("A kesim kanıt geçmişi kapasitesi dolu.");
            foreach(var historical in history)
            {
                var previous=ValidateAndRead(historical);
                if(previous.FrontPieceId==candidate.FrontPieceId || previous.FrontPieceId==candidate.BackPieceId ||
                   previous.BackPieceId==candidate.FrontPieceId || previous.BackPieceId==candidate.BackPieceId)
                    throw new InvalidOperationException("Panel kimliği hedefin kanıt geçmişinde zaten kullanılıyor.");
            }
        }
        // Photo and DXF provenance intentionally outlive removed pieces. Reusing
        // either identity would falsely associate a new panel with old evidence.
        bool ProposedId(Guid id)=>id==candidate.FrontPieceId || id==candidate.BackPieceId;
        if(snapshot.PhotoTraceHistory?.Any(p=>ProposedId(p.PieceId))==true ||
           snapshot.ImportProvenance?.PieceIds.Any(ProposedId)==true)
            throw new InvalidOperationException("Panel kimliği hedefin fotoğraf veya DXF geçmişinde zaten kullanılıyor.");
        // Grading references must refer to live pieces (ProjectStorage.Validate),
        // and any nesting history was rejected above, including rotated history.
        if(snapshot.Pieces.Any(p=>p.Id==candidate.FrontPieceId || p.Id==candidate.BackPieceId))
            throw new InvalidOperationException("Panel kimliği hedefte zaten kullanılıyor.");
        static Bounds2 Bounds(double[] xy)=>Geometry.Bounds(Enumerable.Range(0,5).Select(i=>new Point2(xy[i*2],xy[i*2+1])).ToArray());
        static bool Clear(Bounds2 a,Bounds2 b)=>a.MaxX<b.MinX || b.MaxX<a.MinX || a.MaxY<b.MinY || b.MaxY<a.MinY;
        var front=Bounds(captured.FrontPlacedCoordinatesMm);var back=Bounds(captured.BackPlacedCoordinatesMm);
        foreach(var piece in snapshot.Pieces)
        {
            // Include grainline endpoints as well as every contour point; open
            // paths and degenerate boxes deliberately receive the same treatment.
            var points=piece.Points.ToList();
            if(piece.Grainline is { } grainline) { points.Add(grainline.Start);points.Add(grainline.End); }
            var bounds=Geometry.Bounds(points);
            if(!Clear(front,bounds) || !Clear(back,bounds))throw new InvalidOperationException("Panel yerleşimi hedefteki bir parçaya değiyor veya çakışıyor.");
        }
    }}
