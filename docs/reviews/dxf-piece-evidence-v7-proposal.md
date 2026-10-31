# DXF parça kaynak kaydı — deneysel v7 taslağı

Durum: Tasarım önerisi; kodlanmadı ve doğrulanmadı. v6 ayrı vertex-marker deneyi için ayrılmıştır. Mevcut önizleme kilidi, import onayı ve kaynak yeniden kontrolü değiştirilmez.

## Mevcut kanıt ve sınır

`DxfImportResult.PieceSources` parça kimliği, kaynak kod-0 kayıt sıraları, section/block/layer/entity, INSERT yolu ve POLYLINE alt kayıtlarını taşır. Bunlar vertex eşlemesi veya tekstil anlamı değildir. `PieceSourcesFingerprint` aktarım anındaki tüm parça durumuna bağlıdır; değiştirilebilir projeyle ilişkilendirilmeden önce karşılaştırılmalıdır. Kaynak ayrıntıları sınırlı ve kesilmiş olabilir; kaynak kayıtları proje dosyasına kaydedilmez. `ImportProvenance` ise dosya özeti, boyutu, ölçek, uyarılar ve tarihsel kimlikler taşır; tek başına kaynak-geometri eşleşmesini kanıtlamaz. `ProjectStorage` bugün en fazla v5 okur ve bilinmeyen alanları reddeder.

## Önerilen v7 sözleşmesi

- İsteğe bağlı proje düzeyi `DxfPieceEvidence` nesnesi yalnız tam ve doğrulanmış aktarım sonucundan oluşturulsun. Sürüm 7 seçildiğinde `ImportProvenance` zorunlu olsun; v0–v6 bu yeni alanı null dahil reddetsin. v7 alanı eksik/null/yanlış türde veya herhangi bir alt alanı eksikse açma reddedilsin; yinelenen ve bilinmeyen alanlar da mevcut katı kuralla reddedilsin. v7 okuyucusu v6 şemasını ancak diğer deneyin kesin sözleşmesiyle birleştikten sonra kabul etsin.
- Kayıt, kaynak SHA-256 ve bayt uzunluğu, açık kodlama, ölçek, aktarım modu ve tüm ilgili toleranslar/onaylı katmanlar ile bağlansın. Her parça için benzersiz parça kimliği, aktarım anı geometri fingerprint'i, kaynak kayıt sıraları ve varsa sıralı INSERT yolu ve çocuk kayıtları bulunsun. Kaynak sırası yalnız aynı byte snapshot ve aynı parser sürümü içinde anlamlıdır; DXF handle veya küresel kimlik olarak sunulmasın. Parser/algoritma sürümü ve fingerprint kodlaması açıkça sürümlensin.
- Kayıt tamlığı açıkça ifade edilsin: eksik, kesilmiş, doğrulanmamış, desteklenmeyen veya elle oluşturulmuş girdi tam kanıt sayılmasın. Sınırlar aşılırsa kayıt kesilip başarı olarak yazılmasın; işlem reddedilsin veya kullanıcıya açık, ayrı ve eksik durum saklansın. SourceOrdinals / InsertPath / çocuk kayıtları sınırları ve string Unicode doğrulaması katı olsun.
- Geometri düzenleme, silme, kopyalama, derecelendirme, pastal, undo/redo ve kurtarma semantiği önceden tanımlansın: tarihsel kayıt korunur, güncellik fingerprint karşılaştırmasıyla hesaplanır; kimlik veya geometri değişiminde mevcut diye gösterilmez. Yeni türetilen parçalara kanıt otomatik devredilmez. Silinen parça için tarihsel kayıt yetim olarak açıkça gösterilebilir; hiçbir durumda kaynak doğrulandı veya üretime uygun denmez.
- Kaydetme, kurtarma, klonlama ve dışa aktarmada alan korunmalı veya açık kayıp bildirimi ile engellenmeli. Eski okuyucuların v7'yi reddettiği ve v0–v5 dosyalarının aynı anlamda açılıp yazıldığı gerçek eski binary ile sınanmalı. v6 ile birleşme tamamlanmadan v7 uygulaması ve ana dala öneri yapılmamalı.
- Doğrulama: eksik/null/yinelenen/bilinmeyen alanlar, bozuk enum/hash/sayı, sınır ve taşma, yinelenen kimlik/sıra, yanlış snapshot, sahte elle oluşturulmuş sonuç, parser sürüm uyuşmazlığı, kırpılmış rapor, geometri düzenleme/undo, silme/kopyalama, recovery, export kaybı ve önceki sürüm uyumluluğu için anlamlı regresyonlar; ayrıca preview iptal/yeniden kontrol kilidinin değişmediği kontrol edilmeli.

## Uygulama engeli

Mevcut snapshot fingerprint'i proje çapındadır ve import kaydının tümüyle tam/eksiksiz olduğu, her kaynağın semantik olarak bir kalıba karşılık geldiği veya yeni türetilen parçaların kökeninin korunduğu anlamına gelmez. v6 şeması başka çalışmada henüz sabitlenmemiştir. UI/iş akışı, clone, recovery, export, türetme ve uyumluluk birlikte tasarlanmadan tek başına kalıcı alan eklemek sessiz kayıp ya da yanlış güven yaratabilir. Bu nedenle burada uygulama yapılmadı; taslak ana dal için hazır değildir.
