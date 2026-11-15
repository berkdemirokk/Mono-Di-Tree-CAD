using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Tests.PlannerTestData;

namespace TekstilCAD.Tests;

/// <summary>The preview sentences of FINAL_COMMANDS.md C01-C23 (with the corrections of review-a11y-copy.md C.2), verbatim.</summary>
public class CommandPlannerTests
{
    private static ProjectDocument B1Doc(out ProjectDocument doc) => doc = B1();

    [Fact]
    public void C01RectangleInEmptyProjectStartsAtTwentyTwenty() =>
        Assert.Equal("Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm). Sol üst köşe: soldan 20 mm, yukarıdan 20 mm.",
            Ok(Plan(new RectangleCommand(120, 80), new ProjectDocument())).Preview);

    [Fact]
    public void C01RectanglePlacesNextToTheRightmostPieceWithTwentyMillimetreGap() =>
        Assert.Equal("Dikdörtgen çizilecek: 120 × 80 mm (12 × 8 cm). Sol üst köşe: soldan 1240 mm, yukarıdan 20 mm.",
            Ok(Plan(new RectangleCommand(120, 80), B1())).Preview);

    [Fact]
    public void C01RectangleWithDecimalsUsesCommaAndBothUnits() =>
        Assert.Equal("Dikdörtgen çizilecek: 12,5 × 80,25 mm (1,25 × 8,025 cm). Sol üst köşe: soldan 20 mm, yukarıdan 20 mm.",
            Ok(Plan(new RectangleCommand(12.5, 80.25), new ProjectDocument())).Preview);

    [Fact]
    public void C02SquareSentence() =>
        Assert.Equal("Kare çizilecek: 100 × 100 mm (10 × 10 cm). Sol üst köşe: soldan 20 mm, yukarıdan 20 mm.",
            Ok(Plan(new RectangleCommand(100, 100, IsSquare: true), new ProjectDocument())).Preview);

    [Fact]
    public void C03HorizontalLineSentence() =>
        Assert.Equal("Çizgi çizilecek: 150 mm (15 cm), yatay. Başlangıç noktası: soldan 20 mm, yukarıdan 20 mm.",
            Ok(Plan(new LineCommand(150, LineOrientation.Horizontal), new ProjectDocument())).Preview);

    [Fact]
    public void C04VerticalLineSentence() =>
        Assert.Equal("Çizgi çizilecek: 80 mm (8 cm), dikey. Başlangıç noktası: soldan 20 mm, yukarıdan 20 mm.",
            Ok(Plan(new LineCommand(80, LineOrientation.Vertical), new ProjectDocument())).Preview);

    [Fact]
    public void C05MoveRightSentence() =>
        Assert.Equal("‘Ön beden’ 25 mm sağa taşınacak (2,5 cm). Sol üst köşe soldan 45 mm, yukarıdan 20 mm olacak.",
            Ok(Plan(new MoveByCommand(25, 0), B1Doc(out var d), Sel(d))).Preview);

    [Fact]
    public void C05MoveUpSentence()
    {
        var doc = B1();
        Assert.Equal("‘Ön beden’ 5 mm yukarı taşınacak (0,5 cm). Sol üst köşe soldan 20 mm, yukarıdan 15 mm olacak.",
            Ok(Plan(new MoveByCommand(0, -5), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C06MoveTwoWaysSentence()
    {
        var doc = B1();
        Assert.Equal("‘Ön beden’ 20 mm sağa ve 10 mm yukarı taşınacak (2 cm sağa ve 1 cm yukarı). Sol üst köşe soldan 40 mm, yukarıdan 10 mm olacak.",
            Ok(Plan(new MoveByCommand(20, -10), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C07MoveToSentence()
    {
        var doc = B1();
        Assert.Equal("‘Ön beden’ sol üst köşesi soldan 100 mm (10 cm), yukarıdan 50 mm (5 cm) olacak şekilde taşınacak.",
            Ok(Plan(new MoveToCommand(100, 50), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C08RotateClockwiseSentenceAndChips()
    {
        var doc = B1();
        var plan = Ok(Plan(new RotateCommand(90, RotationDirection.Clockwise), doc, Sel(doc)));
        Assert.Equal("‘Ön beden’ saat yönünde 90° döndürülecek (orta noktası yerinde kalır). Ölçüsü 480 × 620 mm'den 620 × 480 mm'ye değişecek.", plan.Preview);
        Assert.Equal(["90 sola döndür", "180 derece"], plan.Alternatives.Select(b => b.Label));
    }

    [Fact]
    public void C09Rotate270ClockwiseSentenceNamesTheAskedAngle()
    {
        var doc = B1();
        var plan = Ok(Plan(new RotateCommand(270, RotationDirection.Clockwise), doc, Sel(doc)));
        Assert.Equal("‘Ön beden’ saat yönünde 270° döndürülecek (orta noktası yerinde kalır). Ölçüsü 480 × 620 mm'den 620 × 480 mm'ye değişecek.", plan.Preview);
        Assert.Equal(new RotatePieceOperation(doc.Pieces[0].Id, -90, "rotate-ccw"), plan.Operation);
    }

    [Fact]
    public void C10RotateCounterClockwiseSentence()
    {
        var doc = B1();
        var plan = Ok(Plan(new RotateCommand(90, RotationDirection.CounterClockwise), doc, Sel(doc)));
        Assert.Equal("‘Ön beden’ saat yönünün tersine 90° döndürülecek (orta noktası yerinde kalır). Ölçüsü 480 × 620 mm'den 620 × 480 mm'ye değişecek.", plan.Preview);
        Assert.Equal(new RotatePieceOperation(doc.Pieces[0].Id, -90, "rotate-ccw"), plan.Operation);
    }

    [Fact]
    public void C11Rotate180Sentence()
    {
        var doc = B1();
        var plan = Ok(Plan(new RotateCommand(180, RotationDirection.Clockwise), doc, Sel(doc)));
        Assert.Equal("‘Ön beden’ 180° döndürülecek (orta noktası yerinde kalır). Parçanın ölçüsü değişmez.", plan.Preview);
        Assert.Equal(new RotatePieceOperation(doc.Pieces[0].Id, 180, "rotate-half"), plan.Operation);
    }

    [Fact]
    public void C08RotateASquareKeepsItsSize()
    {
        var doc = PieceOperationTestData.Doc(Piece("Kare", 0, 0, 100, 100));
        Assert.Equal("‘Kare’ saat yönünde 90° döndürülecek (orta noktası yerinde kalır). Parçanın ölçüsü değişmez.",
            Ok(Plan(new RotateCommand(90, RotationDirection.Clockwise), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C12MirrorLeftRightSentence()
    {
        var doc = B1();
        var plan = Ok(Plan(new MirrorCommand(MirrorAxis.Vertical), doc, Sel(doc)));
        Assert.Equal("‘Ön beden’ sağ-sol yönünde aynalanacak (ortasından geçen dikey çizgiye göre).", plan.Preview);
        Assert.Equal(new MirrorPieceOperation(doc.Pieces[0].Id, MirrorAxis.Vertical, "mirror-vertical"), plan.Operation);
    }

    [Fact]
    public void C13MirrorTopBottomSentence()
    {
        var doc = B1();
        var plan = Ok(Plan(new MirrorCommand(MirrorAxis.Horizontal), doc, Sel(doc)));
        Assert.Equal("‘Ön beden’ alt-üst yönünde aynalanacak (ortasından geçen yatay çizgiye göre).", plan.Preview);
        Assert.Equal(new MirrorPieceOperation(doc.Pieces[0].Id, MirrorAxis.Horizontal, "mirror-horizontal"), plan.Operation);
    }

    private static ProjectDocument OneRectangle() => PieceOperationTestData.Doc(Piece("Parça 1", 20, 20, 120, 80));

    [Fact]
    public void C14ResizeWidthSentence()
    {
        var doc = OneRectangle();
        Assert.Equal("‘Parça 1’ genişliği 120 mm'den 95 mm'ye (9,5 cm) değişecek. Yükseklik 80 mm aynı kalır; sol kenar yerinde durur.",
            Ok(Plan(new ResizeCommand(95, null), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C15ResizeHeightSentence()
    {
        var doc = OneRectangle();
        Assert.Equal("‘Parça 1’ yüksekliği 80 mm'den 60 mm'ye (6 cm) değişecek. Genişlik 120 mm aynı kalır; üst kenar yerinde durur.",
            Ok(Plan(new ResizeCommand(null, 60), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C16ResizeBothSentence()
    {
        var doc = OneRectangle();
        Assert.Equal("‘Parça 1’ ölçüsü 120 × 80 mm'den 95 × 60 mm'ye (9,5 × 6 cm) değişecek. Sol üst köşe yerinde durur.",
            Ok(Plan(new ResizeCommand(95, 60), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C17RenameSentenceKeepsTheTypedLetters()
    {
        var doc = B1();
        Assert.Equal("‘Ön beden’ adı ‘Ön Beden 2’ olacak.",
            Ok(Plan(new RenameCommand("Ön Beden 2"), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void C19DeleteSelectedSentenceIsDangerous()
    {
        var doc = B1();
        var plan = Ok(Plan(new DeleteCommand(PieceTarget.Selected), doc, Sel(doc)));
        Assert.Equal("‘Ön beden’ silinecek (480 × 620 mm). Geri alabilirsiniz.", plan.Preview);
        Assert.True(plan.Danger);
        Assert.True(plan.RequiresConfirmation);
        Assert.Equal(PlanClass.Dangerous, plan.Class);
    }

    [Fact]
    public void C20DeleteByNameSentence()
    {
        var doc = B1();
        var plan = Ok(Plan(new DeleteCommand(PieceTarget.Named, "kol"), doc));
        Assert.Equal("‘Kol’ silinecek (200 × 300 mm). Geri alabilirsiniz.", plan.Preview);
        Assert.Equal(doc.Pieces[2].Id, plan.TargetPieceId);
        Assert.False(plan.NeedsSelectedPiece);
    }

    [Fact]
    public void C21SelectByNameSentenceWithoutDiacritics()
    {
        var doc = B1();
        var plan = Ok(Plan(new SelectCommand(PieceTarget.Named, "on beden"), doc));
        Assert.Equal("‘Ön beden’ seçilecek.", plan.Preview);
        Assert.Equal(doc.Pieces[0].Id, plan.SelectionAfter);
        Assert.Equal(PlanClass.NoChange, plan.Class);
    }

    [Fact]
    public void C22SelectNextAndPreviousWrapAround()
    {
        var doc = B1();
        Assert.Equal("‘Arka beden’ seçilecek.", Ok(Plan(new SelectCommand(PieceTarget.Next), doc, Sel(doc, 0))).Preview);
        Assert.Equal("‘Ön beden’ seçilecek.", Ok(Plan(new SelectCommand(PieceTarget.Next), doc, Sel(doc, 2))).Preview);
        Assert.Equal("‘Kol’ seçilecek.", Ok(Plan(new SelectCommand(PieceTarget.Previous), doc, Sel(doc, 0))).Preview);
        Assert.Equal("‘Ön beden’ seçilecek.", Ok(Plan(new SelectCommand(PieceTarget.Next), doc)).Preview);
        Assert.Equal("‘Kol’ seçilecek.", Ok(Plan(new SelectCommand(PieceTarget.Previous), doc)).Preview);
    }

    [Fact]
    public void C23ClearSelectionSentence()
    {
        var doc = B1();
        var plan = Ok(Plan(new SelectCommand(PieceTarget.None), doc, Sel(doc)));
        Assert.Equal("Seçim bırakılacak. Parçalar değişmez.", plan.Preview);
        Assert.Null(plan.SelectionAfter);
    }

    [Fact]
    public void UnitAssumedAddsTheSentenceAndACentimetreChip()
    {
        var plan = Ok(Plan(new RectangleCommand(60, 45), new ProjectDocument(), unitAssumed: true));
        Assert.Equal("Dikdörtgen çizilecek: 60 × 45 mm (6 × 4,5 cm). Sol üst köşe: soldan 20 mm, yukarıdan 20 mm. Birim yazmadınız; milimetre sayıldı.", plan.Preview);
        Assert.Equal(["60 x 45 cm dikdörtgen"], plan.Alternatives.Select(b => b.Label));
    }

    [Fact]
    public void UnitAssumedMoveAndResizeGetTheSentenceToo()
    {
        var doc = OneRectangle();
        Assert.EndsWith(" Birim yazmadınız; milimetre sayıldı.", Ok(Plan(new MoveByCommand(40, 0), doc, Sel(doc), unitAssumed: true)).Preview);
        Assert.EndsWith(" Birim yazmadınız; milimetre sayıldı.", Ok(Plan(new ResizeCommand(95, null), doc, Sel(doc), unitAssumed: true)).Preview);
        Assert.DoesNotContain("Birim yazmadınız", Ok(Plan(new MoveByCommand(40, 0), doc, Sel(doc))).Preview);
    }

    [Fact]
    public void ParseResultOverloadPassesRefusalsThroughAndReadsTheUnitFlag()
    {
        var doc = B1();
        var view = View(doc);
        var refused = CommandPlanner.Plan(CommandParser.Parse("zzz", new ParserContext(["Ön beden"])), view, Selection.None);
        Assert.Equal("R01", refused.Code);
        var assumed = CommandPlanner.Plan(CommandParser.Parse("60 x 45 dikdörtgen", new ParserContext(["Ön beden"])), view, Selection.None);
        Assert.EndsWith("Birim yazmadınız; milimetre sayıldı.", assumed.Plan!.Preview);
    }

    [Fact]
    public void WindowAndViewCommandsCarryTheirFixedSentences()
    {
        var doc = B1();
        var selected = Sel(doc);
        var view = new DocumentView(doc, Epoch) { UndoCount = 2, RedoCount = 1, LastUndoLabel = "25 mm sağa taşıma", FileName = "gomlek.tcad" };
        string P(Command c) => CommandPlanner.Plan(c, view, selected).Plan!.Preview;
        Assert.Equal("Son işlem geri alınacak: 25 mm sağa taşıma.", P(new UndoCommand()));
        Assert.Equal("Son 2 işlem geri alınacak.", P(new UndoCommand(5)));
        Assert.Equal("Geri alınan işlem yinelenecek.", P(new RedoCommand()));
        Assert.Equal("Tüm parçalar ekrana sığdırılacak. Parçalar değişmez.", P(new ViewCommand(ViewAction.FitAll)));
        Assert.Equal("Seçili parça ekrana sığdırılacak. Parçalar değişmez.", P(new ViewCommand(ViewAction.FitSelected)));
        Assert.Equal("Görünüm sola kaydırılacak. Parçalar değişmez.", P(new ViewCommand(ViewAction.PanLeft)));
        Assert.Equal("Fare aracı: Dikdörtgen. İki köşeye tıklayın.", P(new ToolCommand(DrawingTool.Rectangle)));
        Assert.Equal("Proje kaydedilecek: gomlek.tcad", P(new FileCommand(FileAction.Save)));
        Assert.Equal("Yeni, boş bir proje başlayacak.", P(new FileCommand(FileAction.New)));
        Assert.Equal("Dikiş payı şimdilik “Dikiş payı ver…” penceresinde yapılır. Pencere 10 mm (1 cm) değeriyle açılacak; önizlemeyi görüp siz uygularsınız.",
            P(new HandoffCommand(HandoffTarget.TextileTools, 2, 10)));
        Assert.Equal("Beden oluşturmak için serileme kuralı gerekir. “Beden oluştur…” penceresi açılacak.", P(new HandoffCommand(HandoffTarget.TextileTools, 1)));
        Assert.Equal("Çoğaltma penceresi açılacak. Kopyanın nasıl oluşacağını orada seçersiniz.", P(new HandoffCommand(HandoffTarget.Duplicate)));
        Assert.Equal("Dışa aktarma penceresi açılacak. Dosya yalnız inceleme içindir; üretim onayı değildir ve makineye gönderilmez.", P(new HandoffCommand(HandoffTarget.Export)));
        Assert.True(CommandPlanner.Plan(new HandoffCommand(HandoffTarget.Export), view, selected).Plan!.Danger);
    }

    [Fact]
    public void PlanDuplicateSentenceAndCopy()
    {
        var doc = B1();
        var plan = Ok(CommandPlanner.PlanDuplicate(View(doc), Sel(doc)));
        Assert.Equal("‘Ön beden’ çoğaltılacak: ‘Ön beden kopya’ adlı kopya 20 mm (2 cm) sağa ve 20 mm (2 cm) aşağı kaydırılarak eklenecek.", plan.Preview);
        Assert.True(plan.AppliesInCore);
        Assert.Equal(PlanKind.DuplicatePiece, plan.Kind);
    }
}
