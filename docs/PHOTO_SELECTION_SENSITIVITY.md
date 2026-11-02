# Fotoğraf seçiminde piksel duyarlılığı

Tek mesafeli ölçeklemede ölçü karşılaştırması, isteğe bağlı bir piksel sapması senaryosu gösterebilir. Kullanıcının girdiği `r`, referansın iki ucu ve kontrol mesafesinin iki ucunun her biri için, önizleme koordinatlarında X ve Y yönlerinin her birindeki en büyük mutlak sapma varsayımıdır. Bu değer otomatik tahmin edilmez. Referansın milimetre değeri kesin kabul edilir.

Önizleme ve yönlendirilmiş kaynak boyutlarının oranları `sx` ve `sy` olsun. Referans ve kontrol uçları arasındaki kaynak koordinatı mesafeleri sırasıyla `R` ve `C` ise, dört uç için varsayılan sapma ile her mesafenin değişimi üçgen eşitsizliğiyle `e = 2 r √(sx² + sy²)` değerinden büyük olmayacak biçimde sınırlandırılır. Yaklaşık alt sınır `L max(0, C−e)/(R+e)`, `R > e` ise üst sınır `L(C+e)/(R−e)` olarak hesaplanır. Aksi durumda sonlu bir üst sınır gösterilmez. Farklı hataların bağımlılıkları ve görüntü sınırları hesaba katılmadığı için aralık geniş olabilir.

Hesaplar binary64 ile yapılır ve ekranda yuvarlanır; bu bir sertifikalı aralık aritmetiği veya istatistiksel güven aralığı değildir. Gerçek seçim hatasının girilen değerden küçük olduğu, referansın fiziksel ölçüsünün doğruluğu, perspektif, lens, görüntü çözünürlüğü, kumaşın düzlüğü ve konturun doğruluğu doğrulanmaz. Dört köşeli düzlem dönüşümünde bu hesap sunulmaz. Sonuç üretim onayı değildir.

`PhotoSelectionSensitivityTests`, farklı X/Y ölçek oranları olan sentetik fotoğrafta dört ucun X/Y koordinatlarının her biri için −1, 0 ve +1 piksellik **6.561** birleşimi gerçek tek mesafe dönüşümüyle karşılaştırır. Ayrıca sıfır sapma, kısa referans ve geçersiz girdileri sınar. Bu sentetik test gerçek fotoğraf veya kullanıcı hatası dağılımı kanıtı değildir.
