import Foundation

// Port of CommandParser.Vocabulary.cs: the word lists, the whole-phrase table and the "did you mean" words.
// Every word is folded (lower-case, ASCII) except the DidYouMeanWords display texts.

extension CommandParser {
    static func wordSet(_ words: String...) -> Set<String> { Set(words) }

    static func unionOf(_ sets: [Set<String>]) -> Set<String> {
        var all = Set<String>()
        for s in sets { all.formUnion(s) }
        return all
    }

    // ---- Filler and verbs --------------------------------------------------------------------------------------------

    static let fillerWords = wordSet("lutfen", "sunu", "bunu", "su", "bu", "hemen", "bana")

    /// Optional verbs that may end a command and carry no meaning on their own.
    static let trailingVerbs = wordSet("ciz", "cek", "yap", "et", "ver", "olsun", "getir")

    // ---- Directions: (right, down) in screen directions ----------------------------------------------------------------

    static let directions: [String: (right: Int, down: Int)] = [
        "saga": (1, 0), "sag": (1, 0),
        "sola": (-1, 0), "sol": (-1, 0),
        "yukari": (0, -1), "yukariya": (0, -1),
        "asagi": (0, 1), "asagiya": (0, 1),
    ]

    // ---- Family vocabulary -------------------------------------------------------------------------------------------

    static let rectWords = wordSet("dikdortgen", "dikdortgeni", "kutu", "kutuyu")
    static let squareWords = wordSet("kare", "kareyi")
    static let lineWords = wordSet("cizgi", "cizgiyi")
    static let orientationWords = wordSet("yatay", "dikey")

    static let widthLabels = wordSet("en", "eni", "enini", "genislik", "genisligi", "genisligini")
    static let heightLabels = wordSet("boy", "boyu", "boyunu", "yukseklik", "yuksekligi", "yuksekligini")
    static let bothLabels = wordSet("olcu", "olcusu", "olcusunu", "olcuyu", "boyut", "boyutu", "boyutunu")

    static let moveVerbs = wordSet("tasi", "kaydir")
    static let positionMarkers = wordSet("konum", "konuma", "konumuna", "noktaya", "noktasina", "nokta")
    static let xLabels = wordSet("x", "soldan")
    static let yLabels = wordSet("y", "yukaridan", "ustten")

    static let rotateWords = wordSet(
        "dondur", "derece", "yarimtur", "ceyrektur", "basasagi", "saatyonunde", "saatyonununtersine")

    static let mirrorWords = wordSet("aynala")
    static let mirrorModifiers = wordSet("dikey", "yatay", "sagsol", "altust", "yukariasagi", "eksene", "eksen", "gore", "eksenine")

    static let deleteVerbs = wordSet("sil", "kaldir", "silin", "siliniz", "kaldirin")
    static let selectVerbs = wordSet("sec", "secin", "seciniz")
    static let selectedPieceWords = wordSet("parca", "parcayi", "parcanin", "secili", "seciliyi", "secileni", "parcasini", "bunu")

    static let renameLabels = wordSet("adi", "adini", "ismi", "ismini", "isim", "ad")
    static let renameVerbs = wordSet("olsun", "yap", "degistir", "koy", "yaz", "et", "ver")

    static let seamWords = wordSet("dikis", "pay", "payi", "payini", "etrafina")
    static let markerWords = wordSet("pastal", "yerlestir", "kumas")
    static let groupWords = wordSet("hepsini", "tumunu", "parcalari", "birdenfazla")

    static let undoWords = wordSet("geri", "adim", "adimi")

    // ---- Words every family accepts as glue --------------------------------------------------------------------------

    static let glueWords = wordSet("ve", "carpi", "x", "ciz", "cek", "yap", "et", "ver", "olsun", "ayarla", "degistir", "getir")

    // ---- Words per family: anything else left over is refused, never ignored -----------------------------------------------

    static let drawAllowed: Set<String> = unionOf([rectWords, squareWords, lineWords, orientationWords,
                                                   widthLabels, heightLabels, wordSet("kenar", "kenari"), glueWords])

    static let moveAllowed: Set<String> = unionOf([moveVerbs, positionMarkers, xLabels, yLabels, glueWords, selectedPieceWords,
                                                   wordSet("al", "git", "saga", "sag", "sola", "sol", "yukari", "yukariya", "asagi", "asagiya")])

    static let rotateAllowed: Set<String> = unionOf([rotateWords, glueWords, selectedPieceWords,
                                                     wordSet("cevir", "saga", "sola", "tersyone")])

    static let mirrorAllowed: Set<String> = unionOf([mirrorWords, mirrorModifiers, glueWords, selectedPieceWords])

    static let resizeAllowed: Set<String> = unionOf([widthLabels, heightLabels, bothLabels, glueWords, selectedPieceWords])

    static let undoAllowed: Set<String> = unionOf([undoWords, wordSet("al", "ve")])

    static let seamAllowed: Set<String> = unionOf([seamWords, glueWords, wordSet("ekle", "birak", "olarak")])

    static let markerAllowed: Set<String> = unionOf([markerWords, glueWords,
                                                     wordSet("eni", "en", "hazirla", "parcalari", "kumasin", "genisligi")])

    static let groupAllowed: Set<String> = unionOf([groupWords, moveVerbs, moveAllowed, wordSet("parcayi", "birden", "fazla")])

    static let metaAllowed: Set<String> = unionOf([wordSet("adet", "olsun", "yap", "et", "ver")])

    // ---- Word sequences that read as one word -------------------------------------------------------------------------

    static let multiWords: [(words: [String], merged: String)] = [
        (["saat", "yonunun", "tersine"], "saatyonununtersine"),
        (["saat", "yonunun", "tersi"], "saatyonununtersine"),
        (["saat", "yonunde"], "saatyonunde"),
        (["yarim", "tur"], "yarimtur"),
        (["ceyrek", "tur"], "ceyrektur"),
        (["ters", "yone"], "tersyone"),
        (["bas", "asagi"], "basasagi"),
        (["ters", "cevir"], "terscevir"),
        (["sag", "sol"], "sagsol"),
        (["sol", "sag"], "sagsol"),
        (["alt", "ust"], "altust"),
        (["ust", "alt"], "altust"),
        (["yukari", "asagi"], "yukariasagi"),
        (["asagi", "yukari"], "yukariasagi"),
        (["birden", "fazla"], "birdenfazla"),
    ]

    // ---- Words offered as "Şunu mu demek istediniz" (longer than five letters only) ------------------------------------------

    static let didYouMeanWords: [String] = [
        "dikdörtgen", "döndür", "aynala", "kaydır", "yakınlaştır", "uzaklaştır", "genişliği", "yüksekliği",
        "kopyala", "kaydet", "yinele", "görünüm", "ayrıntılı", "koordinat", "pastal", "serileme", "yerleştir",
        "kaldır", "çoğalt", "taşıma", "sığdır", "göster", "parçayı", "dışa", "aktar", "bırak", "iptal",
    ]

    /// folded word -> display text. Only folded words longer than five letters are offered.
    static let didYouMeanByFolded: [String: String] = buildDidYouMean()

    static func buildDidYouMean() -> [String: String] {
        var map: [String: String] = [:]
        for display in didYouMeanWords {
            let folded = TurkishText.fold(display)
            if folded.utf16.count > 5 { map[folded] = display }
        }
        return map
    }

    // ---- Whole phrases -----------------------------------------------------------------------------------------------

    static let phrases: [String: ParseResult] = buildPhrases()

    static func buildPhrases() -> [String: ParseResult] {
        var map: [String: ParseResult] = [:]

        func add(_ command: ParsedCommand, _ keys: [String]) {
            let result = ParseResult.understood(command)
            for key in keys { map[key] = result }
        }

        func addRefusal(_ refusal: CommandRefusal, _ keys: [String]) {
            let result = ParseResult.refused(refusal)
            for key in keys { map[key] = result }
        }

        // Drawing helpers, selection and deletion without a name.
        add(.handoff(.duplicate, tab: nil, value: nil),
            ["cogalt", "kopyala", "bir kopya daha", "kopyasini cikar", "kopya", "kopyasini al"])
        add(.delete(.selected, name: nil),
            ["sil", "parcayi sil", "kaldir", "parcayi kaldir", "secili parcayi sil",
             "seciliyi sil", "secileni sil", "secili parcayi kaldir"])
        add(.select(.next, name: nil),
            ["sonraki parcayi sec", "sonraki parca", "siradaki parca", "siradaki parcayi sec", "sonrakini sec"])
        add(.select(.previous, name: nil), ["onceki parcayi sec", "onceki parca", "oncekini sec"])
        add(.select(.none, name: nil),
            ["secimi birak", "secimi kaldir", "hicbirini secme", "secimi temizle", "secimi iptal et"])

        // Undo and redo.
        add(.undo(steps: 1), ["geri al", "geri", "son islemi geri al", "son islemi geri"])
        add(.redo, ["yinele", "ileri al", "ileri", "tekrar yap", "tekrar", "yeniden yap"])

        // View.
        add(.view(.fitAll),
            ["tumunu goster", "hepsini goster", "ekrana sigdir", "hepsini sigdir", "tumunu sigdir", "tumunu ekrana sigdir"])
        add(.view(.fitSelected), ["seciliyi goster", "parcayi goster", "secili parcayi goster"])
        add(.view(.zoomIn), ["yakinlastir", "yaklas"])
        add(.view(.zoomOut), ["uzaklastir", "uzaklas"])
        for noun in ["gorunumu", "gorunum"] {
            add(.view(.panLeft), ["\(noun) sola kaydir", "\(noun) sol kaydir"])
            add(.view(.panRight), ["\(noun) saga kaydir", "\(noun) sag kaydir"])
            add(.view(.panUp), ["\(noun) yukari kaydir", "\(noun) yukariya kaydir"])
            add(.view(.panDown), ["\(noun) asagi kaydir", "\(noun) asagiya kaydir"])
        }
        // C# ViewAction.AllTools has no Swift counterpart (the iPhone has no "all tools" panel); the nearest
        // meaning is the detailed view, so these three phrases are kept (they still count as known words).
        add(.view(.allTools), ["tum araclar", "araclar", "tum aletler"])
        add(.view(.detailedView),
            ["ayrintili gorunum", "uzman gorunumu", "tum dugmeler", "ayrintili gorunume gec", "uzman gorunum"])
        add(.view(.textLarger), ["yaziyi buyut"])
        add(.view(.textSmaller), ["yaziyi kucult"])
        add(.view(.themeDark), ["koyu tema"])
        add(.view(.themeLight), ["acik tema"])

        // Two-meaning words are refused with both readings spelled out.
        addRefusal(Refusals.flipOrRotate(), ["ters cevir", "terscevir", "cevir"])
        addRefusal(Refusals.zoomOrResize(), ["buyut", "kucult"])

        add(.previewDrawing, ["cizimi onizle", "kalibi onizle", "cizim onizlemesi"])
        add(.plotterCalibration, ["plotter kalibrasyon dosyasi hazirla"])
        for pattern in StudioPattern.allCases {
            let name = TurkishText.fold(pattern.rawValue)
            add(.addStudioPattern(pattern), ["\(name) modeli ekle", "\(name) kalibi ekle"])
        }

        // Mouse tools.
        add(.tool(.select), ["sec araci", "secim araci", "fareyle sec", "secme araci"])
        add(.tool(.line), ["cizgi araci", "fareyle cizgi"])
        add(.tool(.polyline), ["nokta nokta ciz", "coklu cizgi araci", "kontur ciz", "coklu cizgi", "nokta nokta"])
        add(.tool(.rectangle), ["dikdortgen araci", "fareyle dikdortgen"])
        add(.tool(.bezier), ["egri araci", "bezier araci", "kavis ciz", "kavis araci"])
        add(.tool(.arc), ["yay araci", "yay ciz"])

        // Finishing a drawing.
        add(.finishDrawing(.close), ["cizimi kapat", "kapat", "parcayi kapat"])
        add(.finishDrawing(.finishOpen), ["cizimi acik birak", "acik bitir", "acik birak"])
        add(.finishDrawing(.cancel), ["cizimi iptal et", "cizimi iptal", "cizimi birak"])
        add(.finishDrawing(.removeLastPoint), ["son noktayi sil", "son nokta", "bir nokta geri", "son noktayi geri al"])
        add(.handoff(.coordinateDrawing, tab: nil, value: nil), ["koordinat gir", "koordinatla ciz", "koordinat"])

        // Files.
        add(.file(.save), ["kaydet", "projeyi kaydet", "sakla"])
        add(.file(.saveAs), ["farkli kaydet", "baska adla kaydet", "yeni isimle kaydet"])
        add(.file(.saveCopy), ["yedegini al", "kopyasini kaydet", "yedek kopya", "yedek al"])
        add(.file(.open), ["ac", "dosya ac", "proje ac", "kalibi ac", "dunku isi getir"])
        add(.file(.new), ["yeni", "yeni proje", "yeni kalip", "bos sayfa", "sifirdan basla"])
        add(.handoff(.importDxf, tab: nil, value: nil),
            ["dxf al", "dxf ac", "dxf yukle", "dosyadan kalip al", "ice aktar", "dxf"])
        add(.handoff(.export, tab: nil, value: nil),
            ["disa aktar", "pdf yap", "pdf", "plotter dosyasi hazirla", "kesime gonder",
             "plotterdan cikar", "yazdir", "plotter dosyasi", "plotter"])
        add(.handoff(.exportSelectedDxf, tab: nil, value: nil),
            ["secili parcayi dxf yap", "secili parcayi dxf", "secilen parcayi aktar", "secili parcayi aktar"])
        add(.handoff(.exportSelectedA4, tab: nil, value: nil),
            ["a4 cikti", "a4 pdf yap", "a4 pdf", "secili parcayi yazdir", "a4"])

        // Hand-offs to existing windows.
        add(.handoff(.textileTools, tab: 1, value: nil),
            ["serileme", "serileme yap", "bir beden buyut", "bir beden kucult",
             "bir ust beden", "bir alt beden", "beden buyut", "beden kucult", "beden serileme", "beden uret"])
        for size in ["xs", "s", "m", "l", "xl", "xxl", "2xl", "3xl"] {
            for noun in ["beden", "bedeni"] {
                for verb in ["cikar", "yap", "uret", "olustur", "hazirla", "ekle"] {
                    add(.handoff(.textileTools, tab: 1, value: nil), ["\(size) \(noun) \(verb)"])
                }
            }
        }
        add(.handoff(.textileTools, tab: 3, value: nil),
            ["pastal yap", "pastal", "parcalari yerlestir", "pastal hazirla", "parcalari yerlestir pastal"])
        add(.handoff(.machineCenter, tab: nil, value: nil),
            ["makine dosyasi hazirla", "makine dosyasi", "simulatoru ac", "simulator"])
        add(.handoff(.grainline, tab: nil, value: nil), ["iplik yonu", "ipligi kaldir", "duz iplik", "iplik"])
        add(.handoff(.pieceMetadata, tab: nil, value: nil),
            ["parca bilgileri", "kumasi astar yap", "adet", "parca bilgisi"])
        add(.handoff(.mirrorCopy, tab: nil, value: nil),
            ["karsi parcasini cikar", "sol kolu da yap", "yanina aynali parca", "karsi parca", "sag kolu da yap"])
        add(.handoff(.groupMove, tab: nil, value: nil),
            ["parcalari tasi", "birden fazla parcayi tasi", "hepsini tasi", "tum parcalari tasi"])
        add(.handoff(.layers, tab: nil, value: nil), ["yeni katman", "katmanlar", "katman ekle", "katman"])

        // Help and small windows.
        add(.help, ["yardim", "neler yazabilirim", "ornekleri goster", "ornekler", "?"])
        add(.handoff(.about, tab: nil, value: nil), ["hakkinda", "surum"])
        add(.handoff(.recoveryScan, tab: nil, value: nil), ["kurtarma tara", "kurtarma kayitlarini tara"])
        add(.handoff(.designRequest, tab: nil, value: nil),
            ["elbise istegi yaz", "kalip istegi hazirla", "elbise istegi", "kalip istegi"])
        add(.cancelPreview, ["vazgec", "iptal"])

        return map
    }
}
