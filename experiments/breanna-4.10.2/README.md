# Deneysel Breanna 4.10.2

Bu kod ayrı yerel denemedir; ürün UI, gerçek kullanıcı verisi ve plottera bağlı değildir. Çıktı üretime onaysızdır; uzman incelemesi ve numune dikimi gerekir. Breanna tamamlanmış elbise değil gövde bloğudur.

Breanna/core/brian/library/plugin-bust/config/plugin-transform 4.10.2, Node >=20, pnpm kilit dosyası. Kurulum izole store ile `pnpm install --frozen-lockfile --ignore-scripts --config.node-linker=hoisted`; test `node --test draft.test.mjs`. Kaynak: https://freesewing.eu/docs/designs/breanna/ ve https://github.com/freesewing/freesewing/blob/develop/LICENSE . FreeSewing MIT, Copyright (c) 2022 Joost De Cock. Harici bağımlılıkların dağıtım bildirimleri henüz tamamlanmadı; hooks@0.3.2 metadata MIT bildiriyor ama paketinde ayrı lisans metni görülmedi. Dağıtım öncesi doğrula.

15 ölçü zorunlu; shoulderSlope derece, diğerleri mm. Eksik, sayı olmayan, sıfır/negatif uzunluk ve -90/90 dahil sınır dışı açı reddedilir. Bu teknik sınırlar antropometrik doğrulama değildir. Bu deneme motorun kendi varsayılan bolluk ve stil seçeneklerini kullanır; kullanıcı onaylı, açık bolluk seçimine henüz izin vermez. Bu nedenle sonuç üretime aktarılmamalıdır. SVG yalnız deneysel çıktı; mevcut CAD içine aktarım ve tüm dikiş/çentik anlamlarının korunması kanıtlanmamıştır.

Sentetik örnekte ön, arka ve kol parçaları; 28 SVG path ve 879.78 × 595.6 mm görünüm ölçüsü görüldü. Bunlar kişiye ait veri veya doğruluk referansı değildir. Node testleri ölçü reddini ve sonlu nokta/SVG oluşumunu kontrol eder; dikiş uyumunu, kalıp yapımını ve fiziksel boyutu doğrulamaz.
