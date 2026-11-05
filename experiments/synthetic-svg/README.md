# Sentetik SVG araştırma görünümü

Bu bağımsız konsol deneyi, sabit sentetik katalog girdisinden altı panel adayını C# hesaplarıyla oluşturur; kaynak Python fixture veya golden koordinatlarını okumaz. Uygulamaya, gerçek kullanıcı ölçülerine, üretim dosyalarına, yazıcıya veya plotter'a bağlı değildir. SVG yalnız araştırma görselleştirmesidir: fiziksel ölçek, dikiş payı, yerleşim veya kesim talimatı içermez. Eğri dönüşümü ve görünüm henüz çalıştırılıp doğrulanmamıştır; adayların topolojisi, uyumu, dikişleri ve gerçek giysi olarak geçerliliği de doğrulanmamıştır.

Araç yalnız Windows üzerinde `C:\Projects\TekstilCAD\work\synthetic-svg` çalışma ağacına sabitlenmiştir. Önce `experiments\synthetic-svg\test-output` dizinini bu çalışma ağacında hazırlayın; yoksa araç dosya oluşturmaz. Yol bileşenleri yönlendirme noktasıysa veya kilitlenemezse işlem durur. Taşınmış derleme başka bir konuma yazmaz. Komut (proje kökünden):

```powershell
dotnet run --project experiments/synthetic-svg/SyntheticSvg.csproj -- synthetic-base
```

Diğer sabit seçenekler `synthetic-distinct` ve `synthetic-third`'tür. Program hiçbir ölçü veya çıktı yolu almaz. Her çağrı yalnız `experiments/synthetic-svg/test-output` altında yeni ve benzersiz SVG dosyası oluşturmaya çalışır; dosya adı konsolda yazılır. Dosya otomatik açılmaz. Dosyanın kendisinde de sentetik/doğrulanmamış uyarısı vardır. Buradaki hesapların kapsamı ve sınırlamaları için [araştırma notunu](../garmentcode-fitted-synthetic/README.md) inceleyin. Gelecekte ölçü bağlantısı ayrı tasarım, gizlilik ve doğrulama çalışması gerektirir; bu deney böyle bir yol açmaz.

