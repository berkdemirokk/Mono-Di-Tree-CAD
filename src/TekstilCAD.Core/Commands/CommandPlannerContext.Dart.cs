using static TekstilCAD.Core.Commands.CommandPlannerText;

namespace TekstilCAD.Core.Commands;

internal sealed partial class PlannerContext
{
    private PlanResult PlanDart(DartCommand command)
    {
        if(!TryEditTarget(out var piece,out var refusal))return refusal;
        if(!double.IsFinite(command.IntakeMm) || command.IntakeMm is <1 or >20_000 || !double.IsFinite(command.DepthMm) || command.DepthMm is <1 or >20_000)
            return Refuse(Refusals.OutOfRangeMeasure());
        if(!piece.Closed)return Refuse(Custom(Refusals.ExecutionFailed,"Pens yalnız kapalı bir parça kenarına eklenebilir."));
        if(piece.Curves is not null)return Refuse(Refusals.HasCurves());
        var count=piece.Points.Count>3 && piece.Points[0]==piece.Points[^1] ? piece.Points.Count-1 : piece.Points.Count;
        var edgeIndex=command.EdgeNumber-1;
        if(edgeIndex<0 || edgeIndex>=count)return Refuse(Custom(Refusals.ExecutionFailed,$"{Quote(piece.Name)} parçasında {command.EdgeNumber}. kenar yok."));
        var a=piece.Points[edgeIndex];var b=piece.Points[(edgeIndex+1)%count];var length=Geometry.Distance(a,b);
        if(length<=Geometry.Epsilon || command.IntakeMm>=length)
            return Refuse(Custom(Refusals.ExecutionFailed,"Pens ağzı seçilen kenara sığmıyor; kenardan daha küçük bir ağız ölçüsü girin."));
        var halfFraction=command.IntakeMm/(2*length);
        var start=.5-halfFraction;var end=.5+halfFraction;
        PatternPiece marked;
        try { marked=DartTools.AddDart(piece,edgeIndex,start,end,command.DepthMm); }
        catch(ArgumentException ex) { return Refuse(Custom(Refusals.ExecutionFailed,ex.Message)); }
        var legs=DartTools.GetLegs(marked,marked.Darts![^1]);
        var edited=Doc.Clone();var index=edited.Pieces.FindIndex(p=>p.Id==piece.Id);
        if(index<0)return Refuse(Custom(Refusals.PreviewStale,"Parça değişti; önizlemeyi yeniden oluşturun."));
        edited.Pieces[index]=marked;
        var name=Quote(piece.Name);
        var preview=$"{name} parçasının {command.EdgeNumber}. kenarına {Mm(command.IntakeMm)} ağızlı, {Mm(command.DepthMm)} derinlikte iç pens eklenecek. Kontur değişmez; dikiş kolları kesim çizgisi değildir. Dikim ve beden uygunluğunu kontrol edin.";
        var stamp=PieceStamp.Capture(view,selection,piece.Id,StampScope.Document);
        return Of(new Plan(PlanKind.AddDart,PlanClass.Geometry,command,preview,stamp,Doc.Clone(),edited)
        {
            Ghost=Scene(GhostStyle.Preview,[Outline(piece)],[new GhostLabel(legs.Apex,$"Pens · {Mm(command.IntakeMm)} ağız / {Mm(command.DepthMm)} derinlik",GhostLabelPlacement.Above)]) with
                { Lines=[new GhostLine(legs.Start,legs.Apex),new GhostLine(legs.Apex,legs.End)] },
            Caveats=["Pens kolları iç dikiş işaretidir; DXF dışa aktarımında desteklenmediği için kayıp olarak raporlanır.","Gerçek beden ve dikim uygunluğu operatörce doğrulanmadı."],
            ResultText=$"{name} parçasına {command.EdgeNumber}. kenarda iç pens eklendi; kontur korundu.",
            ActionName="Pens ekleme",
            TargetPieceId=piece.Id,
            NeedsSelectedPiece=true,
        });
    }
}
