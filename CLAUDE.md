# dersmate (PeerLearn) — çalışma kuralları

Öğrencilerin birbirine ders verdiği akran öğrenme platformu. .NET 8 modüler monolit +
PostgreSQL (modül başına ayrı şema) + Redis, React/Vite arayüz. **İletişim dili Türkçe** —
kod yorumları, commit mesajları, test çıktıları ve kullanıcıya görünen her metin Türkçe.

Bu dosya mimariyi anlatmaz (onun için `docs/`). Burada yalnızca **kodu okuyarak
anlaşılamayacak** olanlar var: ihlali sessiz olan kurallar ve daha önce ısırmış tuzaklar.

---

## Komutlar

```bash
docker compose up -d                              # PostgreSQL + Redis
dotnet run --project src/PeerLearn.Api            # API :5000
npm --prefix frontend run dev                     # arayüz :5173
```

```powershell
powershell -File .\tools\start-dev.ps1            # üçünü birden, ayrı konsollarda
powershell -File .\tools\stop-dev.ps1             # düzgün kapat (Postgres'i ÖLDÜRME)
powershell -File .\tools\restart-api.ps1          # durdur → derle → başlat
powershell -File .\tools\run-all-tests.ps1        # 14 paket, 562 kontrol
```

```bash
npm --prefix frontend run test:e2e                # tarayıcı testleri (backend gerekmez)
```

**API çalışırken `dotnet build` çalıştırma** — DLL'ler kilitli, MSB3027 alırsın.
`restart-api.ps1` bunun için var.

---

## ⛔ Dokunulmaz

### `frontend/src/lib/hwid.js` → `canvasSignal()`

**Fonksiyonun tamamı sabittir.** Çizilen her şey — yazı tipi, renkler, metin,
koordinatlar — cihaz parmak izinin hash'ine giriyor. Tek bayt değişirse **üretimdeki tüm
HWID banları geçersiz olur** ve banlı kullanıcılar geri döner. Geri alınamaz.

İki değer özellikle "düzeltilesi" görünüyor, ikisi de bilerek yerinde duruyor:

| satır | neden düzeltilesi görünür | neden dokunulmaz |
|---|---|---|
| `ctx.fillText('PeerLearn', 2, 2)` | ürün adı artık **dersmate** | metin markayı değil cihazı tanımlıyor |
| `ctx.fillStyle = '#4f46e5'` | paletteki hiçbir renge karşılık gelmiyor | eski `brand-600` ile aynı olması tesadüftü; parmak izi sabiti |

Marka değişir, bu blok değişmez. Dosyanın içindeki uyarıyı da silme.

### Enum üyeleri

Enum'lar veritabanında **metin olarak** saklanıyor (`HasConversion<string>`). Bir üyeyi
verisinden önce silersen o satırlar okunamaz hâle gelir ve hata okuma anında,
başka bir yerde patlar. Önce veriyi göç ettir, sonra üyeyi kaldır.

---

## Ekonomiye dokunan kod

Öğrenci ders almak için **hiçbir şey ödemez**. Puan, dersi verene onay anında **basılır**:
30 dakikalık blok başına 50 puan (`SessionRules.MintPerBlock` / `MintBlockMinutes`), yani
30 dk = 50, 60 dk = 100. Tek bacaklı işlem — escrow/bloke kredi mekanizması kaldırıldı,
geri getirme.

Puan yazan her yol `CreditLedgerService` üzerinden geçer
(`src/PeerLearn.Application/Economy/CreditLedgerService.cs`). Başka yerden cüzdan/lot/defter
yazma. Çağıran taraf **üçünü birden** kurmak zorunda:

1. **Dağıtık kilit** (Redis) — `LockKeys` içinden uygun anahtar
2. **Açık transaction**
3. **`ConcurrencyRetry`** (xmin iyimser kilit)

Üçü de gerekli: kilit süreç içi yarışı, transaction kısmi yazmayı, retry iki instance
arasındaki çakışmayı kapatır. Örnek: `Features/Scheduling/BookSession.cs`.

**Kilit anahtarı seçerken sorguyu takip et.** Suistimal tavanları `TutorUserId`'ye göre
sayıyor, bu yüzden kilit de eğitmen bazında (`LockKeys.Tutor`). Bir zamanlar çift bazındaydı
ve tavan **tamamen** etkisizdi: 12 paralel istek 12 kabul aldı. Anahtar sorgunun grupladığı
şeyi kapsamıyorsa kilit hiçbir şey korumaz.

### Rozet motoru sırası

`BadgeEngine` veriyi DB'den okur, bellekteki değişiklikleri görmez. Doğru sıra:

```
transaction aç → SaveChanges → EvaluateAsync → SaveChanges → commit
```

`SaveChanges`'ten önce çağırmak sessizce yanlış sonuç verir (rozet hiç gelmez ya da eşik bir
adım kayar). Bu hata bu projede **üç kez** yapıldı.

---

## Veritabanı

- **Şema başına modül**: `catalog`, `identity`, `matchmaking`, `comms`, `scheduling`,
  `economy`, `moderation`, `community`. Modüller birbirinin tablosuna doğrudan gitmez.
- **`timestamptz` her yerde**, alan adları `...Utc` ile biter.
- **Kısmi (filtreli) index'ler son savunma hattıdır**, tek savunma değil: uygulama katmanı
  önce kontrol eder, index yarışı kapatır. Ama `HasFilter(...)` taşıyan bir index, sorgunun
  `WHERE`'i o koşulu **birebir** içermedikçe kullanılmaz — yeni index eklerken buna dikkat.
- Göç yazarken **veri onarımı şemadan ÖNCE** gelir ve `RAISE EXCEPTION` ile değişmezleri
  sına. Bozuk veriyle sessizce ilerleyen bir göç, geri alınamaz hasar demektir.

---

## Testler

`tools/*.ps1` paketleri **gerçek veritabanına** karşı koşar. Kurallar:

- **Başarısızlık işaretleri**: `[KALDI]`, `[FAIL]`, `[HATA]`. Yeni bir paket farklı bir
  etiket kullanacaksa `run-all-tests.ps1` içindeki listeye **eklenmeli** — eksik kalırsa o
  paketin başarısızlıkları özete hiç yansımaz ve kırmızı paket yeşil görünür.
- **`[ATLANDI]` başarısızlık değildir ama "geçti" de değildir**; özet bunu `EKSİK` olarak
  raporlar. Sınanmamış kodu sınanmış saymamak için.
- **Test betikleri idempotent değildir.** Sabit HWID veya sabit isim kullanma — ban kalıcı,
  aynı adlı kullanıcılar filtreleri bozar. Koşum başına üret, `userId` ile eşleştir.
- **Paylaşılan katalog konusu kullanma.** Birikmiş kullanıcılar öneri listesini doldurup
  testi kırıyor; koşuma özel konu üret. Konuyu **isimle arama** — katalog senkronizasyonu
  konuları pasifleştirebiliyor.
- **Kanıt standardı mutasyondur.** Bir testin geçmesi yetmez; ilgili kuralı bozup testin
  gerçekten kırıldığını gör. Bu projede dört test aynı anda **yanlış nedenle** geçiyordu.
- Yapısal iddia ("bu kolon artık yok") değer iddiasından ("değeri 0") güçlüdür; kaldırılan
  bir şeyi sınarken yapısal olanı yaz.

---

## PowerShell 5.1 tuzakları

Hepsi bu projede **en az bir kez** ısırdı:

- **Kesme işareti tek tırnaklı dizgeyi kapatır.** `'index'i'` ya da SQL içindeki
  `'economy'` betiği sessizce bozar. Çift tırnak kullan ya da ikiye katla (`''`).
- **`Write-Host` nesne akışına yazmaz.** Aynı süreçte `& betik` ile çağırınca çıktı
  yakalanamaz. Paketler `powershell.exe -File` ile **ayrı süreçte** koşmalı.
- **Satır içi ortam değişkeni öneki yok.** `PGPASSWORD=x psql` çalışmaz; `$env:` ile ayrı
  satırda ata.
- **`&&` ve `||` yok.** `;` ve `if ($?)` kullan.
- **Performans ölçme.** `Invoke-WebRequest` büyük yanıtlarda kendi maliyetini ekliyor
  (112 KB'de ~10 ms). Ölçümü Node ile yap.
- PostgreSQL'i **asla** `Stop-Process` ile kapatma — `pg_ctl -m fast`. Kirli kapanma
  kurtarma moduna sokar, en kötüde veri kaybettirir.

---

## Frontend

- **Kırılım kuralı**: `sm` (640px) **genişlik** içindir (kaç sütun sığar), `lg` (1024px)
  **dokunma** sınırıdır (44px hedef, 16px girdi punto). Tablet hâlâ parmakla kullanılıyor;
  dokunmayı `sm`'e bağlamak tableti masaüstü sanmaktır.
- **`vh` değil `dvh`** — mobil adres çubuğu `vh`'ye dahil değil, alt kısım kırpılır.
- **Girdilerde 16px'ten küçük punto kullanma** — iOS sayfayı otomatik yakınlaştırır.
- **Marka skalası** `frontend/tailwind.config.js`'te. `#0088CC` bilerek **500'de**, 600'de
  değil: üzerine beyaz metin 3.89:1 veriyor ve AA eşiği 4.5:1. Buton zeminleri 600/700'den
  gelir. Yeni bir renk eklerken kontrastı ölç.
- **Erişilebilirlik eşikleri testle korunuyor** (`frontend/e2e/marka.spec.js`). Palet
  değiştirirsen orası kırılır — bu kasıtlı.

---

## Depo

- **Senkron klasörüne koyma** (Google Drive, OneDrive, Dropbox). `.git` bozulabiliyor;
  ayrıca proje bir dönem Drive'daydı ve üç ayrı geçici çözüm doğurmuştu — hepsi kaldırıldı,
  geri getirme. Tarihçe `docs/ASAMA-3-FRONTEND.md` §1.2'de.
- **Depodaki parolalar geliştirme parolalarıdır** (`appsettings.json`, localhost, açıkça
  `DEV-ONLY`). Açıkta durmaları bilinçli. Üretim değerleri depoya girmez —
  `docs/URETIME-CIKIS.md`.
- **`proof-storage/` depoya girmez** — kullanıcıların yüklediği belgeler, kişisel veri.
- Ana dal `main`. Doğrudan itmek yerine dal aç ve PR aç.

---

## Nereye bakmalı

| konu | dosya |
|---|---|
| Modül sınırları, şema ayrımı, kilit stratejisi | `docs/ASAMA-1-MIMARI.md` |
| Ekonomi, moderasyon, arka plan işleri | `docs/ASAMA-2-BACKEND.md` |
| Sayfalar, bileşenler, tasarım kararları | `docs/ASAMA-3-FRONTEND.md` |
| Sıfırdan kurulum | `docs/GELISTIRME-ORTAMI.md` |
| Üretim ayarları ve kapılar | `docs/URETIME-CIKIS.md` |
| Sıfırdan sunucuya kurulum (adım adım) | `docs/SUNUCUYA-KURULUM.md` |
| Açık işler ve **bilinerek kabul edilmiş sınırlar** | `docs/DEVAM-EDILECEK.md` |

Son satır önemli: `DEVAM-EDILECEK.md` içinde "eksik" gibi görünüp aslında **tartışılıp
kabul edilmiş** durumlar var. Orada kayıtlı bir sınırı hata sanıp "düzeltmeden" önce
gerekçesini oku.
