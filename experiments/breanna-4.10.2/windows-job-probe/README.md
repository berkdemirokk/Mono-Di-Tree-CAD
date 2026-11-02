# Windows Job Object — izole sentetik deney

Bu araç üretim süreç çağırıcısı değildir; yalnız sentetik Node işlemi kullanır ve kişisel veri almaz. `CreateProcess(CREATE_SUSPENDED|CREATE_NO_WINDOW)` → `AssignProcessToJobObject` → `ResumeThread` sırası uygulanır. Job `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` ile oluşturulur, job handle devralınabilir açılmaz. Atama reddedilirse süreç yeniden başlatılmaz; sonlandırılmaya çalışılır ve hata döner. Kök ile torunun çıkışı sentetik PID ve süreç handle'larıyla kontrol edilir. `keep` ve `cancel` modlarında yerel denemede kök ve çocuk job kapatılınca çıktı.

`exit` modunda çocuk kök çıkışından sonra, job elle kapatılmadan önce beklenmedik şekilde zaten sonlandı; bu nedenle talep edilen kök-önce-çıkar/pipe-tutan yetim senaryosunda job kapatmanın etkisi gösterilemedi. Bu başarısız/sonuçsuz testtir. Ortamın dış Job Object veya başka süreç politikasının etkisi henüz ayrıştırılmadı. Asıl çağırıcı bu kodu kullanmaz ve kapı kapalıdır.

Riskler: atama ile resume arası kaza/abort, CreateProcess ile atama arası ebeveyn çökmesiyle askıda job dışı süreç, tüm miras alınabilir handle'ların sızması (STARTUPINFOEX handle allowlist henüz yok), parent job politikası/UI sınırlamaları ve nested job, breakaway/WMI ile job dışına kaçış, eşzamanlı dispose, crash/wait ve bütün Windows sürümleri sınanmadı. `PROC_THREAD_ATTRIBUTE_JOB_LIST` ile oluşturma anında atama değerlendirilmelidir; genel güvenlik garantisi verilmez.

Kaynaklar: https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects ; https://learn.microsoft.com/en-us/windows/win32/procthread/nested-jobs ; https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject ; https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812 .

## Paket güvenliği — tasarım, uygulanmadı
Mutlak dosya yolu güven değildir. Dağıtım öncesi imzalı sabit manifest; Node çalıştırılabilir ve tüm worker/bağımlılık dosyalarının hash ve sürümleri, lock dosyası, üçüncü taraf lisans bildirimleri, güvenilir doğrulama anahtarı ve rollback politikası gerekir. Reparse/hardlink, writable dosya/klasör, doğrulama ile çalıştırma arasındaki değiştirme yarışı ve bağımlılık çözümleme sınırı ele alınmadan hash kontrolü de yeterli değildir. İmza/anahtar temini ve güvenli dağıtım hattı bu deneyde yoktur; gerçek ölçüler kullanılmaz.
