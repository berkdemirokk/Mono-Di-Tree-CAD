# Kaynak kenarlarını kapsayan özellik bildirimi: deney

Bu taslak ve kod yalnız test projesindedir. `PatternTools.Offset`, proje dosyası, mevcut `NotchMark`, dışa aktarma ve üretim guard'ı değiştirilmemiştir. Bildirim geçse bile çıktı yalnız deneysel önizlemedir; bir parça veya üretim onayı verilmez.

## Mevcut veriden bilinenler

`PatternPiece.Points` sıralı poligon noktalarını ve dolayısıyla mevcut anlık görüntüdeki sonlu düz kenarları verir. Kapalı kontur, basitlik, yerel dönüşler ve noktaların konumları geometrik olarak kontrol edilebilir. `NotchMark` ise yalnız köşe indeksi ve görsel konumdur; fiziksel çentik türü, yönü veya derinliği değildir. `PatternTools.InsertNotch` fiziksel V'yi üç kontur noktası olarak ekler ama kalıcı V kimliği, kaynak kenar, tolerans ve tasarım niyeti kaydı bırakmaz. Kalıcı kenar kimliği veya bütün kritik özelliklerin eksiksiz listesi mevcut değildir.

## Deneysel bildirim

Bildirim; kaynak `PatternPiece.Id` ve tüm sıralı binary64 nokta bitlerinden türetilen anlık görüntü özeti, inceleyici beyanı, kullanıcıdan açıkça alınmış pozitif en büyük miter sapması (mm), her kaynak kenarı için tam bir inceleme satırı ve bildirilen V/kanalları içerir. Bir kenar indeksi yalnız bu özetle birlikte geçerlidir; kaynak düzenlemesinde bildirim bayatlar. Her V, kimlik, yönlü ağız–tanık–ağız indeksleri ve ayrı genişlik/derinlik toleranslarını mm cinsinden; kanal iki komşu olmayan kenar indeksini ve pozitif en az açıklığı mm cinsinden belirtir. Aynı kenarda birden çok özellik bildirilebilir.

Prototip eksik/tekrarlı/incelenmemiş kenarı, tanımlanmamış veya ilgili tüm kenarlara bağlanmamış özellik kimliğini, yinelenen kimliği, geçersiz toleransı, kaynak özeti uyuşmazlığını ve eski/işaretli modeli reddeder. Hiç fiziksel özellik bildirilmemişse, mevcut modelden gerçekten yok oldukları belirlenemediği için reddeder. Geçerli yapısal bildirimden sonra mevcut üretim çağrısı, deneysel kaynak grid/indeksli aday/topoloji/ters kontroller, istenen binary64 paya göre miter mesafe aralığı ve bildirilen V/kanal ölçüleri denenir. Eski ve kesin kaynak gridleri farklıysa uyumluluk gerekçesiyle bu taslak reddeder.

Bu, **bildirilen** tüm kenar ve özelliklerin yapısal hesabıdır; semantik tamlık kanıtı değildir. Karşı örnek testinde 6×0,5 mm fiziksel V taşıyan kaynak için inceleme bütün kenarları kapsadığını beyan ederken başka bir üçlü seçilir ve gerçek V bildirilmez. Yapısal ve seçili ölçü deneyi yine geçebilir. Bu nedenle `SemanticCompletenessCertified` ve `ProductionApproved` daima false'tur; tanımlanmamış özelliklerin gerçekten yokluğu otomatik çıkarılamaz. İç içe veya daha derin özellikler tek bir tanığa indirgenemez. Kaybolan bir gridlik çentik ve kapanan kanal mevcut deney guard'larında reddedilir.

Gerçek iki loblu regresyonun güncel verisinde böyle bir özellik bildirimi bulunmadığından prototip eksik bilgiyi reddeder; iki hatayı üretimde çözmez. Kullanıcının/ürünün toleransları, hangi özelliklerin önemli olduğu ve inceleme sorumluluğu hakkında değer uydurulmaz. İnceleyici metni kimlik doğrulaması değildir.

## Gerçek uygulamaya geçiş için gereken kararlar

1. `src/TekstilCAD.Core/Models.cs`: mevcut `NotchMark` anlamını değiştirmeden, fiziksel özellik/kanal türleri, kararlı özellik ve kenar kimlikleri, kaynak geometri revizyonu, kenar parametresi, yön, boyut ve ayrı mm toleransları için sürümlenmiş ve açık bilinmiyor durumları olan ayrı tipler tasarlanmalı. İndeks+özet geçici deney çözümüdür.
2. `src/TekstilCAD.FileFormats/ProjectStorage.cs` ve ilgili sürüm/uyumluluk doğrulayıcıları: eski dosyaları eksik değerleri tahmin etmeden okumak, yeni alanların sürümünü ve eksiksizliğini doğrulamak, bilinmeyen alanlarda veri kaybını önlemek için ayrı migration ve round-trip testleri gerekir. Eski dosyada alanın olmaması “özellik yok” değildir.
3. `src/TekstilCAD.Core/DocumentSession.cs`, geçmiş/undo ve geometri işlemleri: kaynak revizyonu, feature/edge kimliği, silme–bölme–birleştirme–yansıtma–serilemede taşınmalı veya açıkça geçersiz kılınmalı; değişen geometride eski inceleme sessizce kullanılmamalıdır.
4. `src/TekstilCAD.Patterns/PatternTools.cs` ve kaynak–aday eşleme: önce mevcut guard'lar korunarak indeksin değiştiği, çoklu kontur, yakın kenar, eğri ve tolerans belirsizliği durumları için test edilmiş deterministik ilişki gerekir. Kullanıcı isteğine göre fiziksel sınır, tüm özelliklerin keşfi/kanıtı ve performans/iptal ayrı incelenmelidir.
5. `src/TekstilCAD.Desktop` kullanıcı arayüzü, DXF ve `src/TekstilCAD.MachineGateway`: gözden geçirilmemiş alanları görünür kılan seçim/ölçüm akışı, silinme/eksik bilgi uyarıları ve bağımsız dışa aktarma–makine sözleşmeleri gerekir. Kullanıcı beyanı tek başına makine güvenliği veya eksiksizlik kanıtı değildir.

Bu liste tasarım önerisidir; bu turda söz konusu dosyalarda değişiklik veya üretim entegrasyonu yapılmamıştır.
