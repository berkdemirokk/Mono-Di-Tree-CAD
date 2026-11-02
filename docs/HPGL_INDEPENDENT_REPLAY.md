# Bağımsız HPGL hareket okuması

Bu deney yerel dosyayı okur; cihaz, ağ veya fiziksel çıktı kullanmaz. `tools/HpglIndependentReplay` altındaki küçük .NET programı, ürünün `HpglFormat.Prepare` koduyla ayrı bir sentetik iş üretir. İş, negatif koordinatlı kapalı dikdörtgen ile 100 noktalı açık zikzak içerir; 40 birim/mm kullanılır. İkinci yol 128 baytlık komut parçalama sınırını aşacak kadar uzundur.

Ayrı Python okuyucu yalnız bu işte kullanılan IN, SP, PA, PU ve PD komutlarını yorumlar; mutlak koordinatlar ve kalem durumu ile çizilen sıralı yolları, önizlemeden alınmamış ve betikte ayrı tanımlanmış beklenen değerlere karşılaştırır. 2 yol ve 103 çizim kenarı eşleşti. Fazladan çizim hareketi, kaldırılmamış kalem, değişmiş koordinat ve eksik bitiş içeren dört kasıtlı bozulma reddedildi.

Tekrarlama: yeni bir çıktı dosyası adıyla `dotnet run --project tools/HpglIndependentReplay/HpglIndependentReplay.csproj -c Release -- YENI_DOSYA.plt`; ardından `python tools/HpglIndependentReplay/replay_fixture.py YENI_DOSYA.plt`. Üretici var olan dosyayı değiştirmez.

Bu tek sentetik durumdur, genel HPGL standardı yorumu veya bağımsız ürün sertifikası değildir. Hedef plotterın komut desteği, tamponu, eksenleri, ölçeği ve fiziksel doğruluğu doğrulanmadı; gerçek kumaş/kalıp uyumluluğu hakkında sonuç çıkarılamaz.
