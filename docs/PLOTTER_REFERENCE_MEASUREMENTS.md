# Plotter referans ölçüleri

Makine merkezi, isteğe bağlı 100 × 100 mm HPGL referans dosyası oluşturur; dosyayı bir cihaza göndermez. X ve Y kenarları için girilen gerçek ölçülerin 100 mm'den sapmasını gösterir. İsteğe bağlı iki köşegen alanı, her köşegenin nominal `100 × √2` mm değerinden sapmasını ve birinci ölçü eksi ikinci ölçü farkını hesaplar. Tek bir köşegen girilirse sonuç üretilmez.

Dosya başarıyla oluşturulduğunda pencere, son referansın dosya yolunu, hazırlanan birim/mm değerini, yön işareti durumunu ve hazırlanan UTF-8 baytlarının SHA-256 özetini gösterir. Bu bilgi pencerede kalır ve kopyalanabilir; ölçüler bu dosyaya otomatik bağlanmaz. Özet, dosyanın yazımdan sonra değiştirilmediğini veya fiziksel çıktının bu dosyadan alındığını doğrulamaz.

Köşegen farkı, örneğin çarpılma veya ölçüm hatasıyla ilişkili olabilir; tek başına nedenini belirlemez. Eşit köşegenler de doğru ölçek, yön, bütün çıktı alanında doğruluk veya cihaz uyumluluğunu kanıtlamaz. Uygulama tolerans belirlemez, otomatik düzeltme uygulamaz ve üretim onayı vermez. Referansın fiziksel çıktısı ve ölçüleri kullanıcı tarafından ayrıca elde edilmelidir.

Sayısal testler nominal değerleri, işaretli farkı ve geçersiz girdileri; etkileşimsiz masaüstü doğrulaması boş, yerel ondalıklı ve hatalı girişleri ve projenin değişmediğini kontrol eder. Fiziksel plotter veya yazıcıyla bu çalışma kapsamında deneme yapılmamıştır.
