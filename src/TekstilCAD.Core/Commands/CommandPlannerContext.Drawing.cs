using static TekstilCAD.Core.Commands.CommandPlannerText;

namespace TekstilCAD.Core.Commands;

internal sealed partial class PlannerContext
{
    /// <summary>Corner of the first piece of a project, in millimetres (C01 sentence: "soldan 20 mm, yukarıdan 20 mm").</summary>
    internal const double FirstPieceOriginMm = 20;

    private PlanResult PlanTrapezoid(TrapezoidCommand c)
    {
        if (ProjectGuards() is { } guard) return Refuse(guard);
        if (new[] { c.BottomMm, c.TopMm, c.HeightMm }.Any(side =>
                !double.IsFinite(side) || side < CommandParser.MinLengthMm || side > CommandParser.MaxLengthMm))
            return Refuse(Refusals.OutOfRangeMeasure());

        // Place and validate the bounding rectangle with the same layer, capacity and gap rules as other pieces.
        var width = Math.Max(c.BottomMm, c.TopMm);
        var result = PieceOperations.AddRectangle(Doc, width, c.HeightMm, FirstOrigin(), view.NewPieceLayerId);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        var edited = result.Document!;
        var piece = edited.Pieces.First(p => p.Id == result.PieceId);
        var bounds = BoundsOf(piece);
        var topInset = (width - c.TopMm) / 2;
        var bottomInset = (width - c.BottomMm) / 2;
        piece.Points = [new(bounds.MinX + topInset, bounds.MinY), new(bounds.MaxX - topInset, bounds.MinY),
                        new(bounds.MaxX - bottomInset, bounds.MaxY), new(bounds.MinX + bottomInset, bounds.MaxY)];
        if (Math.Abs(piece.Points[1].X - piece.Points[0].X - c.TopMm) > c.TopMm * 1e-9 ||
            Math.Abs(piece.Points[2].X - piece.Points[3].X - c.BottomMm) > c.BottomMm * 1e-9)
            return Refuse(Custom(Refusals.OutOfRange, "Bu konumda yamuk kenar ölçüleri korunamıyor."));
        Geometry.ValidateContour(piece.Points);
        var stamp = PieceStamp.Capture(view, selection, null, StampScope.Document);
        var id = Reid(edited, piece.Id, $"{stamp.ContentDigest}|trapezoid|{Inv(c.BottomMm)}|{Inv(c.TopMm)}|{Inv(c.HeightMm)}");
        var dimensions = $"alt {Mm(c.BottomMm)}, üst {Mm(c.TopMm)}, yükseklik {Mm(c.HeightMm)}";
        return Of(new Plan(PlanKind.AddTrapezoid, PlanClass.Geometry, c, $"Yamuk çizilecek: {dimensions}.", stamp, Doc.Clone(), edited)
        {
            Ghost = Scene(GhostStyle.Preview, [new GhostPolygon(piece.Points.ToArray(), true)], SizeLabels(bounds)),
            CreatedPieceId = id, AddsPiece = true, ActionName = "Çizim", ResultText = $"Yamuk çizildi: {dimensions}."
        });
    }

    private PlanResult PlanRectangle(RectangleCommand c)
    {
        if (ProjectGuards() is { } guard) return Refuse(guard);
        var width = c.WidthMm;
        var height = c.IsSquare ? c.WidthMm : c.HeightMm;
        var result = PieceOperations.AddRectangle(Doc, width, height, FirstOrigin(), view.NewPieceLayerId);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        var stamp = PieceStamp.Capture(view, selection, null, StampScope.Document);
        var edited = result.Document!.Clone();
        var piece = edited.Pieces.First(p => p.Id == result.PieceId);
        var newId = Reid(edited, piece.Id, $"{stamp.ContentDigest}|rect|{Inv(width)}|{Inv(height)}|{Inv(piece.Points[0].X)}|{Inv(piece.Points[0].Y)}");
        var bounds = BoundsOf(piece);
        var noun = c.IsSquare ? "Kare" : "Dikdörtgen";
        var preview = WithUnitNote(
            $"{noun} çizilecek: {Size(width, height)}. Sol üst köşe: {Position(bounds.MinX, bounds.MinY)}.");
        var alternatives = new List<RefusalButton>();
        if (unitAssumed)
            alternatives.Add(c.IsSquare
                ? new RefusalButton($"{N(width)} cm kare")
                : new RefusalButton($"{N(width)} x {N(height)} cm dikdörtgen"));
        var plan = new Plan(PlanKind.AddRectangle, PlanClass.Geometry, c, preview,
            stamp, Doc.Clone(), edited)
        {
            Ghost = Scene(GhostStyle.Preview, [new GhostPolygon(piece.Points.ToArray(), true)], SizeLabels(bounds)),
            Alternatives = alternatives,
            ResultText = $"{noun} çizildi: {SizeMm(width, height)}.",
            ActionName = "Çizim",
            CreatedPieceId = newId,
            AddsPiece = true,
        };
        return Of(plan);
    }

    private PlanResult PlanLine(LineCommand c)
    {
        if (ProjectGuards() is { } guard) return Refuse(guard);
        var horizontal = c.Orientation == LineOrientation.Horizontal;
        var result = PieceOperations.AddLine(Doc, c.LengthMm, FirstOrigin(), view.NewPieceLayerId);
        if (!result.Succeeded) return Refuse(MapFailure(result));
        var edited = result.Document!.Clone();
        var piece = edited.Pieces.First(p => p.Id == result.PieceId);
        if (!horizontal)
        {
            // Core operations draw horizontal lines only; the vertical one is the same checked line turned about its start.
            var start = piece.Points[0];
            var end = new Point2(start.X, start.Y + c.LengthMm);
            if (!end.IsFinite || Math.Abs(end.Y) > Geometry.MaxCoordinate || Math.Abs((end.Y - start.Y) - c.LengthMm) > c.LengthMm * 1e-9)
                return Refuse(Custom(Refusals.OutOfRange, "Çizgi çalışma alanının dışına taşıyor. Daha kısa bir ölçü yazın.", new RefusalButton("150 çizgi")));
            List<Point2> points = [start, end];
            try { LargeOpenPathValidator.Validate(points); }
            catch (ArgumentException) { return Refuse(Custom(Refusals.ExecutionFailed, "Bu ölçüyle geçerli bir çizgi oluşturulamadı. Daha büyük bir ölçü yazın.")); }
            piece.Points = points;
        }
        var stamp = PieceStamp.Capture(view, selection, null, StampScope.Document);
        var newId = Reid(edited, piece.Id, $"{stamp.ContentDigest}|line|{Inv(c.LengthMm)}|{horizontal}|{Inv(piece.Points[0].X)}|{Inv(piece.Points[0].Y)}");
        var origin = piece.Points[0];
        var orientation = horizontal ? "yatay" : "dikey";
        var preview = WithUnitNote(
            $"Çizgi çizilecek: {MmCm(c.LengthMm)}, {orientation}. Başlangıç noktası: {Position(origin.X, origin.Y)}.");
        var anchor = Mid(piece.Points[0], piece.Points[1]);
        var alternatives = new List<RefusalButton>();
        if (unitAssumed)
            alternatives.Add(new RefusalButton($"{N(c.LengthMm)} cm {(horizontal ? "" : "dikey ")}çizgi"));
        var plan = new Plan(PlanKind.AddLine, PlanClass.Geometry, c, preview,
            stamp, Doc.Clone(), edited)
        {
            Ghost = Scene(GhostStyle.Preview, [new GhostPolygon(piece.Points.ToArray(), false)],
                [new GhostLabel(anchor, Mm(c.LengthMm), horizontal ? GhostLabelPlacement.Above : GhostLabelPlacement.Right)]),
            Alternatives = alternatives,
            ResultText = $"Çizgi çizildi: {Mm(c.LengthMm)}.",
            ActionName = "Çizim",
            CreatedPieceId = newId,
            AddsPiece = true,
        };
        return Of(plan);
    }

    /// <summary>The first piece goes to (20, 20) mm; later pieces use the placement of <see cref="PieceOperations"/> (null = its default).</summary>
    private Point2? FirstOrigin() => Doc.Pieces.Count == 0 ? new Point2(FirstPieceOriginMm, FirstPieceOriginMm) : null;
}
