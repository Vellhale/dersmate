# Üretime çıkış

Bu dosya, PeerLearn'ü canlıya alırken **zorunlu** olan ayarları listeler. Uygulama, bu
ayarlar eksikken üretim ortamında **açılmaz** (bkz. `src/PeerLearn.Api/Startup/ProductionGuard.cs`)
— sessizce güvensiz çalışmak yerine net bir hatayla durur.

## 1. Zorunlu ortam değişkenleri

Depodaki `appsettings.json` **geliştirme** dosyasıdır; üretim değerleri oraya YAZILMAZ,
ortam değişkeniyle verilir. ASP.NET'te iç içe anahtarlar çift alt çizgiyle yazılır.

| Değişken | Neden zorunlu |
|---|---|
| `Jwt__Key` | Depodaki anahtar herkese açık; onunla üretilen her token taklit edilebilir. En az 32 karakter, rastgele. |
| `Jwt__ExposeVerificationTokenInResponse=false` | `true` iken doğrulama token'ı kayıt yanıtında döner ve e-posta sahipliği hiç kanıtlanmaz. |
| `ConnectionStrings__Postgres` | Depodaki bağlantı dizesi geliştirme parolasını içeriyor. |
| `ConnectionStrings__Redis` | Boş bırakılırsa kilit ve önbellek **süreç içine** düşer; yalnızca TEK instance için güvenlidir. |
| `Cors__Origins__0` | Üretim arayüz alan adı. localhost kalırsa gerçek arayüz API'ye erişemez. |
| `Email__Provider=Smtp` | `Log` iken e-posta gönderilmez; doğrulama token'ı yalnızca e-postayla gittiği için **hiç kimse hesabını doğrulayamaz**. |
| `Email__Host`, `Email__Port`, `Email__Username`, `Email__Password`, `Email__FromAddress` | SMTP bağlantısı. |

### Hız sınırı (isteğe bağlı, varsayılanlar üretim için güvenli)

| Değişken | Varsayılan | Anlamı |
|---|---|---|
| `RateLimit__AuthPerMinute` | 10 | Kayıt/giriş/doğrulama yeniden gönderimi — IP başına dakikada |
| `RateLimit__GlobalPerMinute` | 300 | Diğer tüm uçlar — IP başına dakikada |

`appsettings.json` bu değerleri **geliştirme için bilerek yükseltir** (uçtan uca testler
saniyeler içinde yüzlerce kayıt isteği atıyor). Üretimde ortam değişkeni verilmezse koddaki
güvenli varsayılanlar geçerlidir.

## 2. Şema kurulumu

```bash
dotnet run --project src/PeerLearn.Api -- --migrate
```

Migration'ları uygular, rozet/katalog tohumlamasını yapar ve **çıkar** — istek karşılamaz.

Uygulama açılışında migration koşulmaz: aynı anda başlayan iki instance aynı şemayı
yarıştırır ve hatalı bir migration fark edilmeden canlıya inerdi. Şema değişikliği
dağıtımın ayrı ve görünür bir adımıdır.

## 3. Sağlık uçları

| Uç | Soru | Bağımlılık yoklar mı |
|---|---|---|
| `GET /health` | Süreç ayakta mı? (canlılık) | Hayır |
| `GET /health/ready` | İstek karşılayabilir mi? (hazırlık) | Evet: PostgreSQL + Redis |

Yük dengeleyici **`/health`**'e bakmalı. `/health/ready` DB kopukken `503 Unhealthy` döner;
Redis kopukken `Degraded` — uygulama çalışmaya devam eder ama kilit ve önbellek süreç
içine düşer, yani o anda **birden fazla instance çalıştırmak güvenli değildir**.

## 4. Bilinen sınırlar

- **Dosya depolama yereldir** (`ProofStorage:RootPath`). Birden fazla instance'ta bir
  instance'ın yazdığı kanıtı diğeri bulamaz (404) ve disk yedeklenmiyorsa kanıtlar
  kaybolur. Çok instance'a geçmeden önce nesne depolamaya (S3 vb.) taşınmalı:
  `IProofStorage`'ın ikinci bir uygulaması yeterli.
- Doğrulama yalnızca ortam ayarlarını kontrol eder; SMTP'nin gerçekten çalıştığını
  **denemez**. İlk dağıtımdan sonra bir kayıt yapıp e-postanın ulaştığı elle doğrulanmalı.

## 5. Arka plan işleri ve saklama süreleri

| İş | Sıklık | Ne yapar | Elle tetikleme |
|---|---|---|---|
| Kredi vade süpürmesi | 15 dk | 30 günü dolan lotları yakar | `POST /api/admin/jobs/credit-expiry` |
| Oturum süpürmesi | 10 dk | Otomatik onay, düşen rezervasyon, yanıtsız eşleşme isteği, biten askı | `POST /api/admin/jobs/session-sweep` |
| Depo bakımı | 24 saat | Saklama süresi dolan kanıt görselleri + artık dosyalar | `POST /api/admin/jobs/storage-cleanup` (yalnızca Admin) |

Saklama kararları (hepsi `CleanupStorage.cs` içinde sabit):

- **Kanıt görseli 180 gün** saklanır, sonra silinir. **Satır silinmez** — SHA-256 hash'i
  sahte kanıt tespitinin tek dayanağı ve silinmesi hilekâra "bekle, aynı görseli yeniden
  kullan" kapısı açardı. Silinmiş kanıt indirilmeye çalışılırsa uç `410` döner ("süresi
  doldu"), `404` değil.
- **İtirazlı ders kapsam dışı**: kanıt, kredinin kaderini belirleyen delildir; süre dolsa
  bile hakem karar verene kadar durur.
- **7 günden genç dosyaya dokunulmaz**. Dosya depoya yazıldıktan sonra DB satırı yazılana
  kadar geçen kısa aralıkta dosya referanssızdır; bu pencere olmadan temizlik, o anda
  yüklenmekte olan kanıtı silerdi.
- Referans kümesi boş çıkarsa **hiçbir şey silinmez** ve hata loglanır: "veritabanı boş
  görünüyor" neredeyse her zaman bir bağlantı/şema sorunudur, "her dosya artık" demek değil.

**Takılan süpürme kayıtları:** bir kayıtta üst üste başarısız olan faz o kaydı üstel olarak
erteler (10 dk → 20 → 40 … en fazla 24 saat) ve `scheduling.SweepFailures`'a yazar. Hakem
panelindeki **"Takılı süpürme kaydı"** metriği sıfırdan büyükse o kayıtlarda otomatik
onay/iade **işlemiyor** demektir; `LastError` sütunu ve sunucu log'u sebebi söyler.

## 6. Dağıtım öncesi doğrulama

```bash
powershell -ExecutionPolicy Bypass -File .\tools\verify-production-guard.ps1
```

Üretim kapısının ve hız sınırının gerçekten devrede olduğunu sınar (kapı, geliştirme
ayarlarıyla açılışı durduruyor mu; kimlik ucu 429 veriyor mu; sağlık ucu sınır dışı mı).
