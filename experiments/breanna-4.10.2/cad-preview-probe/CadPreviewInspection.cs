// Diagnostic preview primitives only; deliberately incompatible with production PatternPiece.
internal readonly record struct P(double X,double Y);
internal sealed record Seg(string Kind,P? To=null,P? C1=null,P? C2=null);
internal sealed record PathInput(string PieceId,IReadOnlyList<Seg> Segments);
internal readonly record struct Box(double MinX,double MinY,double MaxX,double MaxY);
internal sealed record DiagnosticPath(string PieceId,IReadOnlyList<Seg> Segments,Box ControlBounds);
internal sealed record DiagnosticPreview(IReadOnlyList<DiagnosticPath> Paths)
{
    internal bool TopologyVerified=>false;internal bool Transferable=>false;internal bool ProductionApproved=>false;
    internal string Loss=>"Çentikler, dikiş payı, yerleşim ve dikim uygunluğu doğrulanmadı veya aktarılmadı.";
}
internal static class CadPreviewInspection
{
    private static readonly string[] Roles={"breanna.front","breanna.back","breanna.sleeve"};
    private static void Check(P? p) {if(p is not P q||!double.IsFinite(q.X)||!double.IsFinite(q.Y)||Math.Abs(q.X)>100000000||Math.Abs(q.Y)>100000000)throw new InvalidDataException("Geçersiz koordinat.");}
    internal static DiagnosticPreview Inspect(IReadOnlyList<PathInput> inputs,string unit,IReadOnlyList<double> matrix)
    {
        if(unit!="mm"||matrix.Count!=6||matrix.Any(x=>!double.IsFinite(x)))throw new InvalidDataException("Birim veya dönüşüm bilinmiyor.");
        var det=matrix[0]*matrix[3]-matrix[1]*matrix[2];if(!double.IsFinite(det)||Math.Abs(det)<1e-12)throw new InvalidDataException("Tekil dönüşüm.");
        if(inputs.Count!=3||inputs.Select(x=>x.PieceId).Distinct(StringComparer.Ordinal).Count()!=3||inputs.Any(x=>!Roles.Contains(x.PieceId,StringComparer.Ordinal)))throw new InvalidDataException("Parça kimliği belirsiz.");
        P Map(P? q){Check(q);var p=q!.Value;var r=new P(matrix[0]*p.X+matrix[2]*p.Y+matrix[4],matrix[1]*p.X+matrix[3]*p.Y+matrix[5]);Check(r);return r;}
        var output=new List<DiagnosticPath>();int total=0;
        foreach(var input in inputs){var src=input.Segments;if(src.Count<3||(total+=src.Count)>4096||src[0].Kind!="move"||src[^1].Kind!="close")throw new InvalidDataException("Açık veya belirsiz yol.");
            var dst=new List<Seg>();var points=new List<P>();P current=default;
            for(int i=0;i<src.Count;i++){var s=src[i];Seg result;
                if(s.Kind=="move"&&i==0&&s.C1 is null&&s.C2 is null){current=Map(s.To);points.Add(current);result=new("move",current);}
                else if(s.Kind=="line"&&i>0&&i<src.Count-1&&s.C1 is null&&s.C2 is null){var p=Map(s.To);current=p;points.Add(p);result=new("line",p);}
                else if(s.Kind=="cubic"&&i>0&&i<src.Count-1){var a=Map(s.C1);var b=Map(s.C2);var p=Map(s.To);current=p;points.AddRange(new[]{a,b,p});result=new("cubic",p,a,b);}
                else if(s.Kind=="close"&&i==src.Count-1&&s.To is null&&s.C1 is null&&s.C2 is null)result=new("close");
                else throw new InvalidDataException("Bilinmeyen veya yanlış sıralı komut.");dst.Add(result);
            }
            var box=new Box(points.Min(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.X),points.Max(p=>p.Y));
            if(box.MinX==box.MaxX||box.MinY==box.MaxY)throw new InvalidDataException("Sıfır alan kutusu.");output.Add(new(input.PieceId,dst.AsReadOnly(),box));
        }
        return new(output.AsReadOnly());
    }
    internal static void RequireCadPreview(DiagnosticPreview inspection) => throw new InvalidOperationException("Topoloji ve anlam doğrulanmadığı için CAD önizlemesine aktarılamaz.");
}
