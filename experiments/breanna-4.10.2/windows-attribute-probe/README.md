# STARTUPINFOEX ve Job List sentetik deneyi

İzole Windows .NET 10 denemesi; ürün veya Node çağırıcısına bağlanmaz. `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` yalnız stdin ve stdout/stderr için iki devralınabilir pipe handle belirtir. Ayrı bir üçüncü devralınabilir pipe sentinel olarak oluşturulur. `PROC_THREAD_ATTRIBUTE_JOB_LIST` kill-on-close job'u CreateProcess anında verir; süreç CREATE_SUSPENDED ile oluşturulur ve resume öncesi IsProcessInJob kontrol edilir. API veya doğrulama başarısızsa çalışma durur; fallback yoktur. Bu ortamda:

- `deny`: listede olmayan sentinel child içinde görülmedi.
- `allow`: aynı sentinel bilerek listeye eklenince görüldü (negatif kontrol).
- `reject`: geçersiz job handle ile CreateProcess reddedildi; süreç başlatılmadı.

Deney tek makine/OS/parent-job koşulunda ve sentetik .NET child ile yapıldı; standart akış semantiği, kötü niyetli child, tüm Windows sürümleri, nested/breakaway politikaları, yarışlar veya gerçek Node ağacı kapsamlı doğrulanmadı. Handle sayısının child tarafından tekrar kullanılması sonucu etkileyebilir; test bu durumda başarısız/inconclusive sayılır. İşaretçinin/sürecin güvenilirliği, paket bütünlüğü, crash/iptal ve kilit açılması ayrı bağımsız inceleme gerektirir. Sertifikalı dağıtım ve lisans incelemesi yok; gerçek kişisel ölçü kullanılmaz.

Microsoft: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute (handle list, inheritable handles, bInheritHandles=TRUE); https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812/ (job list ile oluşturma anında bağlama); https://learn.microsoft.com/en-us/windows/win32/procthread/nested-jobs (nested kısıtları).
