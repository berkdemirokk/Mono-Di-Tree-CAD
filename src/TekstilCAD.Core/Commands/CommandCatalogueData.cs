using System.Collections.Immutable;
using static TekstilCAD.Core.Commands.CommandCatalogue.GroupList;

namespace TekstilCAD.Core.Commands;

/// <summary>
/// The catalogue data. Titles are plain Turkish a beginner understands without CAD words; none equals a label or
/// automation name of a classic button (the headless verification looks those up by content), see the tests.
/// Examples are sentences the FINAL_COMMANDS.md grammar accepts. Synonyms are everyday words, unique across entries.
/// </summary>
internal static class CommandCatalogueData
{
    private static CommandCatalogueEntry F(string id, CommandCatalogueGroup group, string title, string description,
        string[] examples, params string[] synonyms) =>
        new(id, group, title, description, CommandCatalogueKind.Fill, examples, synonyms);

    private static CommandCatalogueEntry D(string id, CommandCatalogueGroup group, string title, string description,
        string label, string[] examples, string[] synonyms, string? accessibleName = null) =>
        new(id, group, title, description, CommandCatalogueKind.Open, examples, synonyms, label, accessibleName);

    private static CommandCatalogueEntry X(string id, CommandCatalogueGroup group, string title, string description,
        string control, string? label, params string[] synonyms) =>
        new(id, group, title, description, CommandCatalogueKind.Detailed, null, synonyms, label, null, control);

    private static readonly string[] None = [];

    public static ImmutableArray<CommandCatalogueEntry> Build()
    {
        var all = ImmutableArray.CreateBuilder<CommandCatalogueEntry>();

        // ---- Başlangıç ve dosya ----
        all.Add(D("start.design-request", GStart, "Elbise isteği yaz…",
            "Yazdığınız elbise tarifinden bir istek hazırlar; kalıp oluşturmaz.",
            "Kalıp isteği hazırla…", ["elbise isteği yaz", "kalıp isteği hazırla"],
            ["istek", "elbise", "tarif", "model fikri"]));
        all.Add(D("start.new", GStart, "Yeni proje başlat",
            "Boş bir proje başlatır; kaydedilmemiş işiniz varsa önce sorar.",
            "Yeni", ["yeni proje", "boş sayfa"],
            ["sıfırdan başla", "temiz sayfa", "yeni kalıp", "yeni iş"]));
        all.Add(D("start.open", GStart, "Projeyi aç…",
            "Daha önce kaydettiğiniz bir projeyi dosyadan açar.",
            "Aç…", ["proje aç", "dünkü işi getir"],
            ["dosya aç", "kayıtlı işi aç", "getir"]));
        all.Add(D("start.save", GStart, "Projeyi kaydet",
            "Projenizi dosyaya yazar; ilk kayıtta dosya adı sorulur.",
            "Kaydet", ["kaydet", "projeyi kaydet"],
            ["sakla", "dosyaya yaz", "kaydet"]));
        all.Add(F("start.rename", GStart, "Projeye ad ver",
            "Açık projenin adını değiştirir.",
            ["projenin adı Yaz Koleksiyonu olsun", "proje adını Kışlık Etek yap"],
            "proje adı", "projeyi adlandır"));
        all.Add(D("start.save-as", GStart, "Başka adla kaydet…",
            "Projeyi yeni bir ad vererek ayrı bir dosyaya kaydeder.",
            "Farklı kaydet…", ["farklı kaydet", "başka adla kaydet"],
            ["yeni isimle kaydet", "adını değiştirerek kaydet"]));
        all.Add(D("start.save-copy", GStart, "Yedek kopya kaydet…",
            "Açık dosyaya dokunmadan ayrı bir yedek kopya yazar.",
            "Yeni dosya olarak güvenli kopya…", ["yedeğini al", "yedek kopya"],
            ["yedek", "güvenli kopya", "kopyasını kaydet"]));
        all.Add(D("start.recovery-scan", GStart, "Kurtarma kayıtlarını tara",
            "Program beklenmedik biçimde kapandıysa kalan otomatik kayıtları arar; açık projeyi değiştirmez.",
            "Kurtarma tara", ["kurtarma tara", "kurtarma kayıtlarını tara"],
            ["kurtar", "kayıp iş", "otomatik kayıt", "bilgisayar kapandı"]));

        // ---- Çizim ----
        all.Add(D("draw.tool-select", GDraw, "Fareyle parça seç",
            "Fare imlecini seçme aracına çevirir; bir parçaya tıklayınca o parça seçilir.",
            "Seç", ["seç aracı", "fareyle seç"],
            ["seçim aracı", "imleç", "tıklayarak seç"]));
        all.Add(D("draw.tool-line", GDraw, "Fareyle çizgi çiz",
            "İki noktaya tıklayarak düz bir çizgi çizersiniz.",
            "Çizgi", ["çizgi aracı", "fareyle çizgi"],
            ["düz çizgi", "tıklayarak çizgi"]));
        all.Add(D("draw.tool-polyline", GDraw, "Nokta nokta çiz",
            "Her tıklamada bir köşe eklersiniz; Enter şekli kapatır, Esc çizimi bırakır.",
            "Çoklu çizgi", ["nokta nokta çiz", "çoklu çizgi aracı"],
            ["şekil çiz", "çokgen", "serbest şekil"]));
        all.Add(D("draw.tool-rect", GDraw, "Fareyle dikdörtgen çiz",
            "İki köşeye tıklayarak dikdörtgen çizersiniz.",
            "Dikdörtgen", ["dikdörtgen aracı", "fareyle dikdörtgen"],
            ["köşe köşe çiz", "tıklayarak dikdörtgen"]));
        all.Add(D("draw.tool-bezier", GDraw, "Eğri çiz",
            "Dört noktaya tıklayarak yumuşak bir eğri çizersiniz.",
            "Bézier", ["eğri aracı", "bézier aracı"],
            ["kavis", "yumuşak çizgi", "kıvrım"]));
        all.Add(D("draw.tool-arc", GDraw, "Yay çiz",
            "Üç noktaya tıklayarak kavisli bir kenar (yay) çizersiniz.",
            "Yay", ["yay aracı", "yay çiz"],
            ["yarım daire", "kemer", "daire parçası"]));
        all.Add(D("draw.coordinate", GDraw, "Yerini yazarak nokta ekle…",
            "Önce bir çizim aracı seçin; sonra noktanın yerini soldan ve yukarıdan milimetre olarak yazarsınız.",
            "Koordinatla nokta gir…", ["koordinat gir", "koordinatla çiz"],
            ["x y değeri", "tam yerini yaz", "ölçüyle nokta", "koordinat"]));
        all.Add(D("draw.finish-open", GDraw, "Çizgiyi kapatmadan bitir",
            "Sürmekte olan çizimi uçlarını birleştirmeden bitirir; en az iki nokta gerekir.",
            "Açık çizgiyi bitir", ["çizimi açık bırak", "açık bitir"],
            ["açık bırak", "çizimi bitir"]));
        all.Add(F("draw.trapezoid", GDraw, "Ölçü yazarak yamuk çiz",
            "Alt ve üst kenar ortalanarak kapalı bir parça çizilir.",
            ["yamuk alt 60 cm üst 40 cm yükseklik 50 cm"], "yamuk", "alt üst kenar"));
        all.Add(F("draw.rectangle", GDraw, "Ölçü yazarak dikdörtgen çiz",
            "Genişliği ve yüksekliği yazarsınız, program sizin için dikdörtgen bir parça çizer.",
            ["120 x 80 dikdörtgen", "kare 100", "12 x 8 cm dikdörtgen"],
            "kare", "kutu", "en boy"));
        all.Add(F("draw.line", GDraw, "Ölçü yazarak çizgi çiz",
            "Uzunluğu yazarsınız, program yatay ya da dikey düz bir çizgi çizer.",
            ["150 çizgi", "80 dikey çizgi", "15 cm yatay çizgi"],
            "uzunluk", "yatay çizgi", "dikey çizgi"));

        // ---- Parçayı değiştir ----
        all.Add(F("piece.move", GPiece, "Parçayı taşı",
            "Seçili parçayı sağa, sola, yukarı ya da aşağı kaydırır; önce ne olacağını gösterir.",
            ["25 sağa taşı", "1 cm sola kaydır", "20 sağa 10 yukarı", "x 100 y 50 konumuna taşı"],
            "kaydır", "oynat", "yerini değiştir", "götür"));
        all.Add(F("piece.rotate", GPiece, "Parçayı döndür",
            "Seçili parçayı kendi ortası etrafında çevirir; açıyı yazabilirsiniz.",
            ["döndür", "90 derece döndür", "çeyrek tur"],
            "çevir", "tur at", "açı"));
        all.Add(F("piece.resize", GPiece, "Parçanın ölçüsünü değiştir",
            "Dikdörtgen bir parçanın genişliğini ya da yüksekliğini yazıyla değiştirir.",
            ["genişliği 95 yap", "boyu 60 yap", "95 x 60 yap"],
            "büyüt", "küçült", "genişlet", "daralt", "ölçü", "boyutunu değiştir"));
        all.Add(F("piece.rename", GPiece, "Parçanın adını değiştir",
            "Seçili parçaya yeni bir ad verir.",
            ["adı ön beden olsun", "adını kol yap"],
            "isim ver", "adlandır", "ismini değiştir"));
        all.Add(F("piece.select", GPiece, "Parçayı adıyla seç",
            "Listedeki bir parçayı adını yazarak seçer ya da seçimi bırakır.",
            ["ön bedeni seç", "sonraki parçayı seç", "seçimi bırak"],
            "adıyla seç", "seçimi kaldır", "bul"));
        all.Add(D("piece.rot-left", GPiece, "Parçayı 90° sola döndür",
            "Seçili parçayı kendi ortası etrafında saat yönünün tersine çeyrek tur çevirir.",
            "90° sola", ["sola döndür", "90 sola"],
            ["saat yönünün tersine", "ters yöne çevir", "çeyrek tur sola"],
            "Parçayı saat yönünün tersine 90 derece döndür"));
        all.Add(D("piece.rot-right", GPiece, "Parçayı 90° sağa döndür",
            "Seçili parçayı kendi ortası etrafında saat yönünde çeyrek tur çevirir.",
            "90° sağa", ["90 sağa döndür", "saat yönünde döndür"],
            ["saat yönünde", "çeyrek tur sağa"],
            "Parçayı saat yönünde 90 derece döndür"));
        all.Add(D("piece.rot-half", GPiece, "Parçayı 180° döndür",
            "Seçili parçayı kendi ortası etrafında yarım tur çevirir; baş aşağı olur.",
            "180° döndür", ["180 derece", "yarım tur", "baş aşağı döndür"],
            ["baş aşağı", "tam ters"],
            "Parçayı merkezinden 180 derece döndür"));
        all.Add(D("piece.mirror", GPiece, "Parçayı sağ-sol aynala",
            "Seçili parçanın aynadaki görüntüsünü yapar; sağ taraf sola, sol taraf sağa geçer.",
            "Aynala", ["aynala", "sağ sol aynala"],
            ["ayna", "ayna görüntüsü", "simetri", "yansıt"]));
        all.Add(D("piece.mirror-h", GPiece, "Parçayı alt-üst aynala",
            "Seçili parçayı alt ve üstü yer değiştirecek biçimde ters çevirir.",
            "Yatay aynala", ["alt üst aynala", "yatay aynala"],
            ["yukarı aşağı çevir", "yatay ayna"],
            "Parçayı yatay eksene göre aynala"));
        all.Add(D("piece.mirror-copy", GPiece, "Yanına aynalı parça ekle…",
            "Parçayı yerinde bırakıp yanına aynadaki görüntüsünü yeni parça olarak koyar; önce önizleme gösterir.",
            "Yanına aynalı parça…", ["karşı parçasını çıkar", "sol kolu da yap", "yanına aynalı parça"],
            ["karşı parça", "eşini yap", "çift parça"]));
        all.Add(D("piece.duplicate", GPiece, "Parçanın kopyasını çıkar…",
            "Seçili parçadan aynı yerde bir kopya yapar; neleri kopyalayacağınızı sorar.",
            "Parçayı çoğalt…", ["çoğalt", "kopyala", "bir kopya daha"],
            ["kopya", "kopyala", "bir tane daha", "ikiye katla"]));
        // Kind F on purpose: the classic button only works with the Seç tool and deletes at once; the typed "sil"
        // flow shows the piece first and asks (FINAL_COMMANDS C19). The entry still names the classic button.
        all.Add(new("piece.delete", GPiece, "Seçili parçayı sil",
            "Seçili parçayı projeden kaldırır; önce gösterir ve sorar, geri alınabilir.",
            CommandCatalogueKind.Fill, ["sil", "parçayı sil", "kaldır"],
            ["sil", "yok et", "ortadan kaldır"], "Parçayı sil"));
        all.Add(D("piece.group-move", GPiece, "Birkaç parçayı birlikte taşı…",
            "Listeden birden çok parça seçip hepsini aynı miktarda kaydırır; tek adımda geri alınır.",
            "Birden fazla parçayı taşı…", ["birden fazla parçayı taşı", "parçaları taşı"],
            ["toplu taşı", "hepsini taşı", "çoklu taşı"]));
        all.Add(D("piece.metadata", GPiece, "Adet ve kumaş bilgisi gir…",
            "Parçanın kaç tane kesileceğini ve hangi kumaştan olduğunu yazarsınız; adetli pastal bu bilgiyi kullanır, temel pastal her parçayı bir kez yerleştirir.",
            "Parça bilgileri…", ["parça bilgileri", "adet 2 yap", "kumaşı astar yap"],
            ["adet", "kumaş", "malzeme", "kaç tane"]));
        all.Add(D("piece.grainline", GPiece, "İplik yönünü ayarla…",
            "Kumaşın ipliğinin parça üzerinde hangi yöne gittiğini gösteren çizgiyi ekler, değiştirir ya da kaldırır.",
            "İplik yönü…", ["iplik yönü", "ipliği kaldır", "düz iplik"],
            ["iplik çizgisi", "kumaş yönü"]));
        all.Add(D("piece.curve-edit", GPiece, "Eğri noktalarını düzenle…",
            "Çizdiğiniz eğrinin ya da yayın ayar noktalarını değiştirir.",
            "Parametrik konturu düzenle…", None,
            ["eğriyi düzelt", "kavis ayarı", "bézier noktaları"]));
        all.Add(D("piece.curve-flatten", GPiece, "Eğriyi sabit çizgiye çevir…",
            "Eğri ayarlarını bırakır; mevcut noktalar sabit çizgi olarak kalır. Geri alınabilir.",
            "Parametrik konturu poliline dönüştür…", None,
            ["eğriyi düzleştir", "eğriyi çizgi yap", "sabitle"]));
        all.Add(X("piece.apply-fields", GPiece, "Ad ve konum kutularını uygula",
            "Ayrıntılı görünümde Ad, X ve Y kutularına yazdıklarınızı seçili parçaya uygular.",
            "Konumu ve adı uygula", "Konumu ve adı uygula",
            "uygula", "konumu uygula", "x y kutusu"));
        all.Add(X("piece.drafts-review", GPiece, "Uygulanmamış yazıları incele…",
            "Yazıp henüz uygulamadığınız ad ve konum değerlerini tek tek gösterir; istediğinizi kaldırabilirsiniz.",
            "Alan taslaklarını incele…", "Alan taslaklarını incele…",
            "taslak", "alan taslağı", "yarım kalan yazı"));
        all.Add(X("piece.drafts-clear", GPiece, "Uygulanmamış yazıları sil",
            "Yazıp uygulamadığınız bütün ad ve konum değerlerini siler; parçalar değişmez.",
            "Alan taslaklarını temizle", "Alan taslaklarını temizle",
            "taslakları sil", "yazıları temizle"));

        // ---- Ölçü, beden ve pastal ----
        all.Add(F("size.seam", GSize, "Dikiş payı ver…",
            "Parçanın dış kenarına eşit genişlikte pay ekler ve sonucu ayrı bir parça yapar; Tekstil araçları penceresi açılır.",
            ["1 cm dikiş payı", "dikiş payı 10", "bir buçuk santim pay ver"],
            "pay", "pay ekle", "dikiş", "kenar payı", "dikiş payı ekle"));
        all.Add(F("size.grade", GSize, "Beden oluştur…",
            "Seçili parçadan daha büyük ya da daha küçük bedenler üretir (serileme); Tekstil araçları penceresi açılır.",
            ["bir beden büyüt", "bir üst beden", "L bedeni çıkar", "serileme yap"],
            "serileme", "beden", "büyük beden", "küçük beden", "başka beden"));
        all.Add(F("size.nest", GSize, "Pastal yerleştir…",
            "Parçaları kumaşın eni içine dizerek pastal planı hazırlar; temel pastalda bütün parçaların yeri değişir.",
            ["pastal yap", "parçaları yerleştir", "kumaş eni 150 santim pastal"],
            "pastal", "kumaşa diz", "fire", "yerleşim", "kumaş eni"));
        all.Add(D("size.textile-tools", GSize, "Tekstil araçlarını aç…",
            "Dikiş payı, beden ve pastal işlerinin hepsini tek pencerede açar.",
            "Tekstil araçları…", None,
            ["atölye", "tekstil", "araçlar penceresi"]));
        all.Add(D("size.machine", GSize, "Makine dosyası hazırla (deneme)…",
            "Makineye bağlanmaz; yalnız deneme dosyası hazırlar.",
            "Makine simülatörü…", ["makine dosyası hazırla", "simülatörü aç"],
            ["makine", "simülatör", "deneme dosyası"]));

        // ---- Dosya alma ve verme ----
        all.Add(D("exchange.import", GExchange, "DXF dosyası al…",
            "Başka bir programdan DXF dosyası alır; ölçü birimini siz seçersiniz, onaylayana kadar projeniz değişmez.",
            "DXF al…", ["dxf al", "dxf aç", "içe aktar"],
            ["dxf yükle", "başka programdan al", "dosyadan kalıp al"]));
        all.Add(D("exchange.block-copy", GExchange, "DXF'teki hazır şekilden kopya çıkar…",
            "DXF dosyasında tanımlı ama sayfaya yerleştirilmemiş bir şekli ayrı bir inceleme dosyası olarak hazırlar.",
            "DXF blok tanımından kopya…", None,
            ["blok", "blok tanımı", "tanımdan kopya"]));
        all.Add(D("exchange.selected-dxf", GExchange, "Seçilen parçaları DXF yap…",
            "Yalnız seçtiğiniz parçaları ayrı bir DXF dosyasına yazar; yalnız inceleme içindir, üretim onayı değildir.",
            "Seçilen parçaları DXF aktar…", ["seçili parçayı dxf yap", "seçilen parçayı aktar"],
            ["seçileni dxf yap", "parçayı dxf olarak ver"]));
        all.Add(D("exchange.selected-notch-review", GExchange, "Seçilenler için işaretli inceleme DXF…",
            "Seçili parçaları köşe işaretleriyle birlikte inceleme dosyasına yazar. Üretim dosyası değildir.",
            "Seçilen parçalar · işaretli inceleme DXF…", None,
            ["işaretli dxf", "köşe işareti", "çentik"]));
        all.Add(D("exchange.export-all", GExchange, "Hepsini dışa aktar…",
            "Projedeki bütün parçaları DXF, SVG, HPGL ya da PDF biçiminde inceleme dosyası olarak yazar; üretim onayı değildir.",
            "Tümünü dışa aktar…", ["dışa aktar", "pdf yap", "plotter dosyası hazırla", "kesime gönder", "plotterdan çıkar"],
            ["dışa ver", "hepsini çıkar", "plotter"]));
        all.Add(D("exchange.export-hpgl", GExchange, "Tüm projeyi HPGL / PLT dışa aktar…",
            "Projedeki bütün parçaları yalnız HPGL / PLT inceleme dosyası olarak yazar; üretim onayı değildir ve makineye göndermez.",
            "HPGL / PLT dışa aktar…", ["makinada bastır", "plotterda bastır"], None));
        all.Add(D("exchange.grain-review", GExchange, "İplik işaretli inceleme DXF…",
            "İplik yönü çizgilerini işaretleyerek inceleme DXF'i yazar. Üretim dosyası değildir; işaretler başka programda kesim çizgisi sanılabilir.",
            "İplik işaretli DXF inceleme dosyası…", None,
            ["iplikli dxf", "iplik dxf"]));
        all.Add(D("exchange.notch-review", GExchange, "Köşe işaretli inceleme DXF…",
            "Köşe noktalarını işaretleyerek inceleme DXF'i yazar; işaretler kesim komutu değildir.",
            "Köşe işaretli DXF incelemesi…", None,
            ["köşe dxf", "çentikli dxf"]));
        all.Add(D("exchange.a4", GExchange, "Seçili parçayı A4 PDF yap…",
            "Seçili parçayı gerçek boyutuyla A4 sayfalara PDF olarak yazar; yazıcıya göndermez.",
            "Seçili parçayı A4 PDF çıkar…", ["a4 çıktı", "a4 pdf yap", "seçili parçayı yazdır"],
            ["a4", "gerçek boyut", "yazıcı çıktısı", "yazdır"]));

        // ---- Görünüm ----
        all.Add(D("view.fit-all", GView, "Tümünü ekrana sığdır",
            "Bütün parçaları ve çizimi ekrana sığacak biçimde gösterir; parçalar değişmez.",
            "Tümünü göster", ["tümünü göster", "hepsini göster", "ekrana sığdır"],
            ["sığdır", "hepsini gör", "tamamını göster"]));
        all.Add(D("view.fit-selected", GView, "Seçili parçayı ekrana sığdır",
            "Seçili parçaya yaklaşıp ekranı onunla doldurur; parçalar değişmez.",
            "Seçili parçayı göster", ["seçiliyi göster", "parçayı göster"],
            ["seçiliye yaklaş", "parçaya git", "odakla"]));
        all.Add(D("view.zoom-in", GView, "Ekranı yakınlaştır",
            "Çalışma alanını büyütür; parçalar değişmez.",
            "+", ["yakınlaştır", "yaklaş"],
            ["zoom", "artı", "daha yakın"],
            "Görünümü yakınlaştır"));
        all.Add(D("view.zoom-out", GView, "Ekranı uzaklaştır",
            "Çalışma alanını küçültür, daha geniş bir alan görürsünüz; parçalar değişmez.",
            "−", ["uzaklaştır", "uzaklaş"],
            ["eksi", "daha uzak", "geri çekil"],
            "Görünümü uzaklaştır"));
        all.Add(D("view.pan-left", GView, "Ekranı sola kaydır",
            "Çalışma alanındaki görüntüyü sola iter; parçalar yerinde kalır.",
            "←", ["görünümü sola kaydır"],
            ["sola git", "sol tarafa bak"],
            "Görünümü sola kaydır"));
        all.Add(D("view.pan-right", GView, "Ekranı sağa kaydır",
            "Çalışma alanındaki görüntüyü sağa iter; parçalar yerinde kalır.",
            "→", ["görünümü sağa kaydır"],
            ["sağa git", "sağ tarafa bak"],
            "Görünümü sağa kaydır"));
        all.Add(D("view.pan-up", GView, "Ekranı yukarı kaydır",
            "Çalışma alanındaki görüntüyü yukarı iter; parçalar yerinde kalır.",
            "↑", ["görünümü yukarı kaydır"],
            ["yukarı git", "üst tarafa bak"],
            "Görünümü yukarı kaydır"));
        all.Add(D("view.pan-down", GView, "Ekranı aşağı kaydır",
            "Çalışma alanındaki görüntüyü aşağı iter; parçalar yerinde kalır.",
            "↓", ["görünümü aşağı kaydır"],
            ["aşağı git", "alt tarafa bak"],
            "Görünümü aşağı kaydır"));
        all.Add(D("view.undo", GView, "Son işlemi geri al",
            "Yaptığınız son değişikliği geri alır.",
            "↶", ["geri al", "son işlemi geri al", "3 adım geri"],
            ["geri al", "geri", "yanlış yaptım", "geri dön"],
            "Geri al"));
        all.Add(D("view.redo", GView, "Geri alınanı yeniden yap",
            "Az önce geri aldığınız değişikliği yeniden uygular.",
            "↷", ["yinele", "ileri al", "tekrar yap"],
            ["ileri", "yeniden yap"],
            "Yinele"));
        all.Add(F("view.text-bigger", GView, "Yazıyı büyüt",
            "Programdaki yazıları büyütür; yazı büyüdükçe çalışma alanı küçülür.",
            ["yazıyı büyüt"],
            "büyük yazı", "yazı boyutu", "okunaklı yap"));
        all.Add(F("view.text-smaller", GView, "Yazıyı küçült",
            "Programdaki yazıları küçültür; çalışma alanı genişler.",
            ["yazıyı küçült"],
            "küçük yazı", "yazıyı daralt"));
        all.Add(F("view.theme-light", GView, "Açık tema",
            "Ekranı açık renklerle gösterir.",
            ["açık tema"],
            "aydınlık", "beyaz", "gündüz"));
        all.Add(F("view.theme-dark", GView, "Koyu tema",
            "Ekranı koyu renklerle gösterir.",
            ["koyu tema"],
            "karanlık", "gece", "siyah"));
        all.Add(F("view.detailed", GView, "Ayrıntılı görünüme geç",
            "Bütün düğmelerin göründüğü eski ve yoğun düzene geçer; hiçbir parça değişmez.",
            ["ayrıntılı görünüm", "uzman görünümü", "tüm düğmeler"],
            "bütün düğmeler", "eski görünüm", "gelişmiş"));

        // ---- Katman ve liste ----
        all.Add(X("layer.new", GLayer, "Yeni katman ekle",
            "Parçaları gruplamak için yeni bir katman açar ve seçer.",
            "Yeni katman", "Yeni katman",
            "katman", "katmanlar", "grup aç"));
        all.Add(X("layer.rename", GLayer, "Katmanı yeniden adlandır…",
            "Seçili katmanın adını değiştirir.",
            "Yeniden adlandır…", "Yeniden adlandır…",
            "katman adı", "katmanın ismi"));
        all.Add(X("layer.delete", GLayer, "Katmanı sil",
            "Seçili katmanı siler; içindeki parçalar varsayılan katmana geçer.",
            "Sil", "Sil",
            "katmanı kaldır", "katmanı at"));
        all.Add(X("layer.assign", GLayer, "Parçayı seçili katmana koy",
            "Seçili parçayı seçili katmana geçirir; katman gizli ya da kilitli olsa da olur.",
            "Seçili parçayı katmana ata", "Seçili parçayı katmana ata",
            "katmana ata", "katmana geçir"));
        all.Add(X("layer.visible", GLayer, "Katmanı göster ya da gizle",
            "Seçili katmandaki parçaları ekranda görünür ya da görünmez yapar.",
            "Görünür", null,
            "gizle", "saklamak", "görünürlük"));
        all.Add(X("layer.locked", GLayer, "Katmanı kilitle",
            "Seçili katmandaki parçaların yanlışlıkla değişmesini engeller.",
            "Kilitli", null,
            "kilitle", "kilit", "korumaya al"));
        all.Add(X("layer.snap", GLayer, "Izgaraya yapışmayı aç ya da kapat",
            "Açıkken fare görünmez 1 mm'lik noktalara yapışıp düzgün çizmenizi sağlar; kapalıyken noktayı serbestçe koyarsınız.",
            "1 mm ızgaraya yakala", null,
            "ızgara", "yakalama", "adım adım çiz"));
        all.Add(X("list.search", GLayer, "Parçayı adıyla bul",
            "Arama kutusuna gider; parça listesini ad ya da kimlikle daraltırsınız.",
            "Parça ara", "Parça ara",
            "ara", "arama", "süz", "listede ara"));
        all.Add(X("list.search-clear", GLayer, "Aramayı temizle",
            "Arama kutusunu boşaltır; liste yeniden bütün parçaları gösterir.",
            "Temizle", "Temizle",
            "aramayı sıfırla", "süzmeyi kaldır"));
        all.Add(X("list.pieces", GLayer, "Parça listesi",
            "Projedeki bütün parçaları sırayla gösterir; birine tıklayarak seçersiniz.",
            "Proje parçaları", null,
            "parçalar", "hangi parçalar var", "liste"));

        // ---- Yardım ----
        all.Add(F("help.examples", GHelp, "Örnekleri göster",
            "Komut kutusuna yazabileceğiniz örnek cümleleri listeler.",
            ["yardım", "neler yazabilirim", "örnekleri göster"],
            "örnek", "nasıl yazılır", "komutlar"));
        all.Add(D("help.about", GHelp, "Sürüm ve program bilgisi",
            "Programın sürümünü, veri klasörünü ve açık kaynak bildirimlerini gösterir.",
            "Hakkında", ["hakkında", "sürüm"],
            ["sürüm bilgisi", "lisans", "veri klasörü"]));

        return all.ToImmutable();
    }

    public static ImmutableArray<CommandCatalogueHeaderButton> BuildHeaderButtons() =>
    [
        new("DXF kaynak ve aktarım notları", "Üst bilgi", "Kaynak geçmişi uyarısının düğmesi; her an görünür, yerinde kalır."),
        new("Pastal geçmişini incele…", "Üst bilgi", "Yalnız pastal geçmişi varken görünür."),
        new("HPGL aktarımını iptal et", "Üst bilgi", "Yalnız çalışan bir dışa aktarmada görünür."),
        new("DXF okumayı iptal et", "Üst bilgi", "Yalnız bir DXF okuması sürerken görünür."),
        new("DXF önizlemesini aç", "Üst bilgi", "Yalnız hazırlanmış bir DXF inceleme bekliyorsa görünür."),
        new("Kurtarma uyarısı ayrıntıları", "Üst bilgi", "Yalnız kurtarma uyarısı varken görünür."),
        new("Tarama hatası ayrıntıları", "Üst bilgi", "Yalnız kurtarma taraması hata verdiğinde görünür."),
        new("Kurtarma kayıtlarını incele", "Üst bilgi", "Yalnız tarama kayıt bulduğunda görünür."),
        new("Açmayı iptal et", "Komut şeridi / yardım satırı", "Yalnız bir proje okunurken görünür."),
        new("Çizim uyarısını kapat", "Alt bilgi üstündeki kırmızı şerit", "Yalnız bir çizim uyarısı varken görünür."),
    ];
}
