# Makine uyumluluğu

**Hiçbir fiziksel makine modeli doğrulanmadı.** Uygulama USB, seri port veya Ethernet üzerinden hareket/kesim komutu göndermez. Dosya üretimi tek başına üretici/model uyumluluğu kanıtı değildir.

| Hedef | Mevcut durum | Doğrulama |
| --- | --- | --- |
| TekstilCAD yerel simülatörü | DXF veya HPGL önizleme dosyası, SHA256 ve JSON manifest; Prepared/Simulated/Failed kuyruğu | Yazılım testleri; fiziksel cihaz yok |
| Gerber | Adaptör/protokol geliştirilmedi | Doğrulanmadı |
| Lectra | Adaptör/protokol geliştirilmedi | Doğrulanmadı |
| Zünd | Adaptör/protokol geliştirilmedi | Doğrulanmadı |
| Eastman | Adaptör/protokol geliştirilmedi | Doğrulanmadı |
| Richpeace | Adaptör/protokol geliştirilmedi | Doğrulanmadı |
| Algotex | Bilgisayarda yazılımı gözlendi; bağlantı/çıktı testi yapılmadı | Doğrulanmadı |

HPGL varsayılan ölçeği 40 birim/mm'dir; profil ölçeği ve birim/mm ayarlanabilir. Gerçek hedefin bu birimi kabul ettiği varsayılamaz. Profiller `IsVerified=false`, `IsSimulator=true` olarak tutulur. Kuyruk bellektedir; manifest bulunması yeniden başlatmada kuyruk geri yüklemesi anlamına gelmez.

Gerçek entegrasyon aşamasında model, kontrolör/firmware, üretici belge sürümü, dosya birimi, kalibrasyon, sınırlar ve test sonucu ayrı kayda bağlanmalıdır. Belgelenmiş protokol, uygun yetki ve fiziksel güvenlik testi olmadan hareket/kesim çalıştırılmamalıdır. Bu oturumda WearCAD ve bağlı üretim yazılımlarına iş gönderilmedi.
