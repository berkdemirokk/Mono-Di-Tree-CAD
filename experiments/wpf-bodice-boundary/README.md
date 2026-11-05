# WPF sentetik üst beden sınırı

Bu bağımsız .NET/WPF araştırma projesi ana Windows uygulamasına bağlanmaz. On beş zorunlu alan ve Türkçe açıklamaları mevcut `BreannaMeasurementGate` kaynağından derleme zamanında alır; mm/cm açıkça seçilir, açı derecedir ve onay gerekir. Alanlar ilk açılışta boştur. Kullanıcıya yalnız açıkça gösterilen **uydurma sentetik örnek** girilmesi söylenir; herhangi bir farklı değer, eksik ölçü veya eksik onay sınırda reddedilir. Gerçek kişi ölçüsü girmeyin.

Dış motor, süreç, ağ, API veya dosyaya ölçü gönderme uygulanmaz. `synthetic-fixture.json` daha önceki bağımsız Node/Breanna araştırma koduyla sentetik ölçülerden oluşturulup bu deneyin derlemesine gömülür. Fixture zarfı, sabit sentetik ölçüler ve gövde çıktısı kontrol edilir; çıktı, ayrı `bodice-wpf-reader-1105` ağacındaki `271b6c2` sürümünden alınan `BodiceReader.cs` ile doğrulanır. Okuyucu yerel mm ve onaysız durumla tam iki benzersiz ön/arka parça, boyut ve koordinat sınırları, bilinen şema ve sıralı move/line/cubic/close işlemlerini arar. Parça etiketleri dizi sırasından değil doğrulanmış isimlerden gelir. WPF `PathGeometry` yerel ham koordinatları korur; dönüşüm yalnız ekran çiziminde uygulanır.

Kaynak runtime ve transitif bağımlılıkların kimliği, lisansı ve fixture'ın güvenilir üretim kanıtı doğrulanmış değildir. Bu yaklaşım ne canlı motor entegrasyonu ne de gerçek ölçülerden önizlemedir. Okuyucunun şema doğrulaması dikiş semantiği, topoloji, kesişmeme, gerçek birim, kaynak dürüstlüğü veya bedene uyum kanıtı değildir.

Her giriş veya birim değişikliği eski görüntüyü ve onayı temizler; çıktı kaydetme, CAD'e ekleme, yazdırma, export veya cihaz işlevi yoktur. WPF yalnızca ekran gösterimidir; işletim sistemi ekran görüntüsü gibi dış yollar engellenmez. Ölçüm yönergeleri uzman tarafından doğrulanmadı. Topoloji, işaretler, dikiş ilişkisi, alt parçalar ve bedene uygunluk çözülmedi; uzman incelemesi ve gerçek numune prova gereklidir. Dağıtım, gerçek kişisel veri veya üretim için kullanılmaz.

## Test

```powershell
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' build experiments/wpf-bodice-boundary/BodiceResearch.csproj -v:q -m:1
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' experiments/wpf-bodice-boundary/bin/Debug/net10.0-windows/BodiceResearch.dll --verify
```

Başsız `--verify`, 15 alan, eksik alan/birim ve onay reddi, cm/mm eşdeğerliği, sentetik dışı veri reddi, hatalı fixture/şema reddi, ters parça sırasındaki adlar, `PathGeometry` üzerindeki bütün ham çizgi/kübik koordinatlar, WPF ekran dışı renkli eğri çizimi ve veri değişince görüntü/onayın temizlenmesini sınar. Kullanıcıya görünen pencere açılmaz. Görsel düzen, ekran okuyucu, DPI, etkileşimli kullanım, motor izolasyonu veya fiziksel uyum test edilmemiştir. Tek fixture'ın geometrisi Breanna'nın önceki araştırma örneğinden türetilmiştir; `generate-fixture.mjs` yalnız geliştirici tarafından açıkça çalıştırılır, mevcut dosyayı overwrite etmez ve diğer araştırma ağacındaki bağımlılıklara bağlıdır.

Bir sonraki adım, bağımsız protokol doğrulayıcısı ve sabit/kapsamı belirli çalışma ortamını ayrıca denetlemek; çalışan motor ve tüm transitif paketlerin kimliğini, lisanslarını ve gerçek kişisel veri için ağ/disk sınırlarını doğrulamaktır. Bu kanıt sağlanana kadar gerçek ölçüleri dış motora bağlamayın.
