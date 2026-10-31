# Bağımlılık ve lisans envanteri

31 Ekim 2026 kaynak incelemesi. Bu belge mevcut doğrudan bağımlılıkların envanteridir; tamamlanmış dağıtım/SBOM veya hukuki inceleme değildir. TekstilCAD'in kendi ürün lisansı henüz belirlenmedi.

## Üretim kodu

Mevcut C# üretim projelerinde harici NuGet PackageReference yoktur; .NET ve Windows WPF çerçevesi kullanılır. .NET runtime ve WPF depoları MIT lisansını belirtir: [.NET runtime lisansı](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WPF lisansı](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT). Dağıtılan runtime'ın üçüncü taraf bildirimleri de korunmalıdır.

Uno, Skia, Clipper2, SQLite ve PostgreSQL mevcut derlemeye dahil değildir. Bunlar tercih/gelecek seçenekleridir; seçilecek kesin paket ve sürümlerin lisansı eklenmeden önce incelenmelidir. WearCAD kaynak kodu veya arayüz varlığı kullanılmaz.

## Test bağımlılıkları

Aşağıdaki değerler kurulu NuGet paketlerinin `.nuspec` lisans alanlarından okundu:

| Paket | Sürüm | Bildirilen lisans | Amaç |
| --- | --- | --- | --- |
| xunit | 2.9.3 | Apache-2.0 | Test çatısı |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | Test çalıştırıcısı |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Test altyapısı |
| coverlet.collector | 6.0.4 | MIT | Kapsama toplama |

Test paketleri kullanıcı uygulamasının yayın klasörüne dahil edilmemeli. Geçişli bağımlılıklar ve kesin yayın dosyaları paketleme aşamasında ayrıca envantere alınmalı; gerekli LICENSE/NOTICE bildirimleri dağıtıma eklenmelidir. Bu belge yalnızca doğrudan paket listesini doğrular.

## Ürün lisans altyapısı

`TekstilCAD.Licensing` RSA SHA-256 ile açık anahtardan doğrulama yapar. Özel anahtar dağıtılmaz; test anahtarları geçicidir. Kullanıcı/firma hesabı, lisans satışı, aktivasyon, iptal, güvenilir saat, çevrimiçi doğrulama ve ödeme hizmeti uygulanmış değildir. Masaüstü geliştirme sürümü bu kitaplıkla ücretli özellik kısıtlaması yapmaz.
