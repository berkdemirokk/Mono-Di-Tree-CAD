namespace TekstilCAD.Core.Commands;

public static partial class CommandParser
{
    private static HashSet<string> Set(params string[] words) => new(words, StringComparer.Ordinal);

    // ---- Filler and verbs ------------------------------------------------------------------------------------------

    private static readonly HashSet<string> FillerWords = Set("lutfen", "sunu", "bunu", "su", "bu", "hemen", "bana");

    /// <summary>Optional verbs that may end a command and carry no meaning on their own.</summary>
    private static readonly HashSet<string> TrailingVerbs = Set("ciz", "cek", "yap", "et", "ver", "olsun", "getir");

    // ---- Directions ------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, (int Right, int Down)> Directions = new(StringComparer.Ordinal)
    {
        ["saga"] = (1, 0), ["sag"] = (1, 0),
        ["sola"] = (-1, 0), ["sol"] = (-1, 0),
        ["yukari"] = (0, -1), ["yukariya"] = (0, -1),
        ["asagi"] = (0, 1), ["asagiya"] = (0, 1),
    };

    // ---- Family vocabulary -----------------------------------------------------------------------------------------

    private static readonly HashSet<string> RectWords = Set("dikdortgen", "dikdortgeni", "kutu", "kutuyu");
    private static readonly HashSet<string> SquareWords = Set("kare", "kareyi");
    private static readonly HashSet<string> LineWords = Set("cizgi", "cizgiyi");
    private static readonly HashSet<string> OrientationWords = Set("yatay", "dikey");

    private static readonly HashSet<string> WidthLabels = Set("en", "eni", "enini", "genislik", "genisligi", "genisligini");
    private static readonly HashSet<string> HeightLabels = Set("boy", "boyu", "boyunu", "yukseklik", "yuksekligi", "yuksekligini");
    private static readonly HashSet<string> BothLabels = Set("olcu", "olcusu", "olcusunu", "olcuyu", "boyut", "boyutu", "boyutunu");

    private static readonly HashSet<string> MoveVerbs = Set("tasi", "kaydir");
    private static readonly HashSet<string> PositionMarkers = Set("konum", "konuma", "konumuna", "noktaya", "noktasina", "nokta");
    private static readonly HashSet<string> XLabels = Set("x", "soldan");
    private static readonly HashSet<string> YLabels = Set("y", "yukaridan", "ustten");

    private static readonly HashSet<string> RotateWords = Set(
        "dondur", "derece", "yarimtur", "ceyrektur", "basasagi", "saatyonunde", "saatyonununtersine");

    private static readonly HashSet<string> MirrorWords = Set("aynala");
    private static readonly HashSet<string> MirrorModifiers = Set("dikey", "yatay", "sagsol", "altust", "yukariasagi", "eksene", "eksen", "gore", "eksenine");

    private static readonly HashSet<string> DeleteVerbs = Set("sil", "kaldir");
    private static readonly HashSet<string> SelectVerbs = Set("sec");
    private static readonly HashSet<string> SelectedPieceWords = Set("parca", "parcayi", "secili", "seciliyi", "secileni", "parcasini", "bunu");

    private static readonly HashSet<string> RenameLabels = Set("adi", "adini", "ismi", "ismini", "isim", "ad");
    private static readonly HashSet<string> RenameVerbs = Set("olsun", "yap", "degistir", "koy", "yaz", "et", "ver");

    private static readonly HashSet<string> SeamWords = Set("dikis", "pay", "payi", "payini", "etrafina");
    private static readonly HashSet<string> MarkerWords = Set("pastal", "yerlestir", "kumas");
    private static readonly HashSet<string> GroupWords = Set("hepsini", "tumunu", "parcalari", "birdenfazla");

    private static readonly HashSet<string> UndoWords = Set("geri", "adim", "adimi");

    // ---- Words every family accepts as glue -------------------------------------------------------------------------

    private static readonly HashSet<string> GlueWords = Set("ve", "carpi", "x", "ciz", "cek", "yap", "et", "ver", "olsun", "ayarla", "degistir", "getir");

    // ---- Words per family: anything else left over is refused, never ignored ----------------------------------------------

    private static readonly HashSet<string> DrawAllowed = Union(RectWords, SquareWords, LineWords, OrientationWords,
        WidthLabels, HeightLabels, Set("kenar", "kenari"), GlueWords);

    private static readonly HashSet<string> MoveAllowed = Union(MoveVerbs, PositionMarkers, XLabels, YLabels, GlueWords, SelectedPieceWords,
        Set("al", "git", "saga", "sag", "sola", "sol", "yukari", "yukariya", "asagi", "asagiya"));

    private static readonly HashSet<string> RotateAllowed = Union(RotateWords, GlueWords, SelectedPieceWords,
        Set("cevir", "saga", "sola", "tersyone"));

    private static readonly HashSet<string> MirrorAllowed = Union(MirrorWords, MirrorModifiers, GlueWords, SelectedPieceWords);

    private static readonly HashSet<string> ResizeAllowed = Union(WidthLabels, HeightLabels, BothLabels, GlueWords, SelectedPieceWords);

    private static readonly HashSet<string> UndoAllowed = Union(UndoWords, Set("al", "ve"));

    private static readonly HashSet<string> SeamAllowed = Union(SeamWords, GlueWords, Set("ekle", "birak", "olarak"));

    private static readonly HashSet<string> MarkerAllowed = Union(MarkerWords, GlueWords,
        Set("eni", "en", "hazirla", "parcalari", "kumasin", "genisligi"));

    private static readonly HashSet<string> GroupAllowed = Union(GroupWords, MoveVerbs, MoveAllowed, Set("parcayi", "birden", "fazla"));

    private static readonly HashSet<string> MetaAllowed = Union(Set("adet", "olsun", "yap", "et", "ver"));

    private static HashSet<string> Union(params HashSet<string>[] sets)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in sets) all.UnionWith(s);
        return all;
    }

    // ---- Word sequences that read as one word -----------------------------------------------------------------------

    private static readonly (string[] Words, string Merged)[] MultiWords =
    {
        (new[] { "saat", "yonunun", "tersine" }, "saatyonununtersine"),
        (new[] { "saat", "yonunun", "tersi" }, "saatyonununtersine"),
        (new[] { "saat", "yonunde" }, "saatyonunde"),
        (new[] { "yarim", "tur" }, "yarimtur"),
        (new[] { "ceyrek", "tur" }, "ceyrektur"),
        (new[] { "ters", "yone" }, "tersyone"),
        (new[] { "bas", "asagi" }, "basasagi"),
        (new[] { "ters", "cevir" }, "terscevir"),
        (new[] { "sag", "sol" }, "sagsol"),
        (new[] { "sol", "sag" }, "sagsol"),
        (new[] { "alt", "ust" }, "altust"),
        (new[] { "ust", "alt" }, "altust"),
        (new[] { "yukari", "asagi" }, "yukariasagi"),
        (new[] { "asagi", "yukari" }, "yukariasagi"),
        (new[] { "birden", "fazla" }, "birdenfazla"),
    };

    // ---- Words offered as "Şunu mu demek istediniz" (longer than five letters only) ---------------------------------------

    private static readonly string[] DidYouMeanWords =
    {
        "dikdörtgen", "döndür", "aynala", "kaydır", "yakınlaştır", "uzaklaştır", "genişliği", "yüksekliği",
        "kopyala", "kaydet", "yinele", "görünüm", "ayrıntılı", "koordinat", "pastal", "serileme", "yerleştir",
        "kaldır", "çoğalt", "taşıma", "sığdır", "göster", "parçayı", "dışa", "aktar", "bırak", "iptal",
    };

    private static readonly Dictionary<string, string> DidYouMeanByFolded = BuildDidYouMean();

    private static Dictionary<string, string> BuildDidYouMean()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var display in DidYouMeanWords)
        {
            var folded = TurkishText.Fold(display);
            if (folded.Length > 5) map[folded] = display;
        }
        return map;
    }

    // ---- Whole phrases ------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, ParseResult> Phrases = BuildPhrases();

    private static Dictionary<string, ParseResult> BuildPhrases()
    {
        var map = new Dictionary<string, ParseResult>(StringComparer.Ordinal);

        void Add(Command command, params string[] keys)
        {
            var result = ParseResult.Understood(command);
            foreach (var key in keys) map[key] = result;
        }

        void AddRefusal(Refusal refusal, params string[] keys)
        {
            var result = ParseResult.Refused(refusal);
            foreach (var key in keys) map[key] = result;
        }

        // Drawing helpers, selection and deletion without a name.
        Add(new HandoffCommand(HandoffTarget.Duplicate), "cogalt", "kopyala", "bir kopya daha", "kopyasini cikar", "kopya", "kopyasini al");
        Add(new DeleteCommand(PieceTarget.Selected), "sil", "parcayi sil", "kaldir", "parcayi kaldir", "secili parcayi sil",
            "seciliyi sil", "secileni sil", "secili parcayi kaldir");
        Add(new SelectCommand(PieceTarget.Next), "sonraki parcayi sec", "sonraki parca", "siradaki parca", "siradaki parcayi sec", "sonrakini sec");
        Add(new SelectCommand(PieceTarget.Previous), "onceki parcayi sec", "onceki parca", "oncekini sec");
        Add(new SelectCommand(PieceTarget.None), "secimi birak", "secimi kaldir", "hicbirini secme", "secimi temizle", "secimi iptal et");

        // Undo and redo.
        Add(new UndoCommand(1), "geri al", "geri", "son islemi geri al", "son islemi geri");
        Add(new RedoCommand(), "yinele", "ileri al", "ileri", "tekrar yap", "tekrar", "yeniden yap");

        // View.
        Add(new ViewCommand(ViewAction.FitAll), "tumunu goster", "hepsini goster", "ekrana sigdir", "hepsini sigdir", "tumunu sigdir", "tumunu ekrana sigdir");
        Add(new ViewCommand(ViewAction.FitSelected), "seciliyi goster", "parcayi goster", "secili parcayi goster");
        Add(new ViewCommand(ViewAction.ZoomIn), "yakinlastir", "yaklas");
        Add(new ViewCommand(ViewAction.ZoomOut), "uzaklastir", "uzaklas");
        foreach (var noun in new[] { "gorunumu", "gorunum" })
        {
            Add(new ViewCommand(ViewAction.PanLeft), $"{noun} sola kaydir", $"{noun} sol kaydir");
            Add(new ViewCommand(ViewAction.PanRight), $"{noun} saga kaydir", $"{noun} sag kaydir");
            Add(new ViewCommand(ViewAction.PanUp), $"{noun} yukari kaydir", $"{noun} yukariya kaydir");
            Add(new ViewCommand(ViewAction.PanDown), $"{noun} asagi kaydir", $"{noun} asagiya kaydir");
        }
        Add(new ViewCommand(ViewAction.AllTools), "tum araclar", "araclar", "tum aletler");
        Add(new ViewCommand(ViewAction.DetailedView), "ayrintili gorunum", "uzman gorunumu", "tum dugmeler", "ayrintili gorunume gec", "uzman gorunum");
        Add(new ViewCommand(ViewAction.TextLarger), "yaziyi buyut");
        Add(new ViewCommand(ViewAction.TextSmaller), "yaziyi kucult");
        Add(new ViewCommand(ViewAction.ThemeDark), "koyu tema");
        Add(new ViewCommand(ViewAction.ThemeLight), "acik tema");

        // Two-meaning words are refused with both readings spelled out.
        AddRefusal(Refusals.FlipOrRotate(), "ters cevir", "terscevir", "cevir");
        AddRefusal(Refusals.ZoomOrResize(), "buyut", "kucult");

        // Mouse tools.
        Add(new ToolCommand(DrawingTool.Select), "sec araci", "secim araci", "fareyle sec", "secme araci");
        Add(new ToolCommand(DrawingTool.Line), "cizgi araci", "fareyle cizgi");
        Add(new ToolCommand(DrawingTool.Polyline), "nokta nokta ciz", "coklu cizgi araci", "kontur ciz", "coklu cizgi", "nokta nokta");
        Add(new ToolCommand(DrawingTool.Rectangle), "dikdortgen araci", "fareyle dikdortgen");
        Add(new ToolCommand(DrawingTool.Bezier), "egri araci", "bezier araci", "kavis ciz", "kavis araci");
        Add(new ToolCommand(DrawingTool.Arc), "yay araci", "yay ciz");

        // Finishing a drawing.
        Add(new FinishDrawingCommand(FinishDrawingAction.Close), "cizimi kapat", "kapat", "parcayi kapat");
        Add(new FinishDrawingCommand(FinishDrawingAction.FinishOpen), "cizimi acik birak", "acik bitir", "acik birak");
        Add(new FinishDrawingCommand(FinishDrawingAction.Cancel), "cizimi iptal et", "cizimi iptal", "cizimi birak");
        Add(new FinishDrawingCommand(FinishDrawingAction.RemoveLastPoint), "son noktayi sil", "son nokta", "bir nokta geri", "son noktayi geri al");
        Add(new HandoffCommand(HandoffTarget.CoordinateDrawing), "koordinat gir", "koordinatla ciz", "koordinat");

        // Files.
        Add(new FileCommand(FileAction.Save), "kaydet", "projeyi kaydet", "sakla");
        Add(new FileCommand(FileAction.SaveAs), "farkli kaydet", "baska adla kaydet", "yeni isimle kaydet");
        Add(new FileCommand(FileAction.SaveCopy), "yedegini al", "kopyasini kaydet", "yedek kopya", "yedek al");
        Add(new FileCommand(FileAction.Open), "ac", "dosya ac", "proje ac", "kalibi ac", "dunku isi getir");
        Add(new FileCommand(FileAction.New), "yeni", "yeni proje", "yeni kalip", "bos sayfa", "sifirdan basla");
        Add(new HandoffCommand(HandoffTarget.ImportDxf), "dxf al", "dxf ac", "dxf yukle", "dosyadan kalip al", "ice aktar", "dxf");
        Add(new HandoffCommand(HandoffTarget.Export), "disa aktar", "pdf yap", "pdf", "plotter dosyasi hazirla", "kesime gonder",
            "plotterdan cikar", "yazdir", "plotter dosyasi", "plotter");
        Add(new HandoffCommand(HandoffTarget.ExportSelectedDxf), "secili parcayi dxf yap", "secili parcayi dxf", "secilen parcayi aktar", "secili parcayi aktar");
        Add(new HandoffCommand(HandoffTarget.ExportSelectedA4), "a4 cikti", "a4 pdf yap", "a4 pdf", "secili parcayi yazdir", "a4");

        // Hand-offs to existing windows.
        Add(new HandoffCommand(HandoffTarget.TextileTools, Tab: 1), "serileme", "serileme yap", "bir beden buyut", "bir beden kucult",
            "bir ust beden", "bir alt beden", "beden buyut", "beden kucult", "beden serileme", "beden uret");
        foreach (var size in new[] { "xs", "s", "m", "l", "xl", "xxl", "2xl", "3xl" })
            foreach (var noun in new[] { "beden", "bedeni" })
                foreach (var verb in new[] { "cikar", "yap", "uret", "olustur", "hazirla", "ekle" })
                    Add(new HandoffCommand(HandoffTarget.TextileTools, Tab: 1), $"{size} {noun} {verb}");
        Add(new HandoffCommand(HandoffTarget.TextileTools, Tab: 3), "pastal yap", "pastal", "parcalari yerlestir", "pastal hazirla", "parcalari yerlestir pastal");
        Add(new HandoffCommand(HandoffTarget.MachineCenter), "makine dosyasi hazirla", "makine dosyasi", "simulatoru ac", "simulator");
        Add(new HandoffCommand(HandoffTarget.Grainline), "iplik yonu", "ipligi kaldir", "duz iplik", "iplik");
        Add(new HandoffCommand(HandoffTarget.PieceMetadata), "parca bilgileri", "kumasi astar yap", "adet", "parca bilgisi");
        Add(new HandoffCommand(HandoffTarget.MirrorCopy), "karsi parcasini cikar", "sol kolu da yap", "yanina aynali parca", "karsi parca", "sag kolu da yap");
        Add(new HandoffCommand(HandoffTarget.GroupMove), "parcalari tasi", "birden fazla parcayi tasi", "hepsini tasi", "tum parcalari tasi");
        Add(new HandoffCommand(HandoffTarget.Layers), "yeni katman", "katmanlar", "katman ekle", "katman");

        // Help and small windows.
        Add(new HelpCommand(), "yardim", "neler yazabilirim", "ornekleri goster", "ornekler", "?");
        Add(new HandoffCommand(HandoffTarget.About), "hakkinda", "surum");
        Add(new HandoffCommand(HandoffTarget.RecoveryScan), "kurtarma tara", "kurtarma kayitlarini tara");
        Add(new HandoffCommand(HandoffTarget.DesignRequest), "elbise istegi yaz", "kalip istegi hazirla", "elbise istegi", "kalip istegi");
        Add(new CancelPreviewCommand(), "vazgec", "iptal");

        return map;
    }
}
