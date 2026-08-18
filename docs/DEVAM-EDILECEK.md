# Yarım kalan işler

Bu dosya, büyük ekonomi/profil revizyonuna geçmeden önce sıradaki işleri dondurur.
Revizyon bittikten sonra buradan devam edilecek.

Tarih: 2026-08-16

## Tamamlananlar (bu iş için arka plan)

| Grup | Konu | Durum |
|---|---|---|
| A | Üretim hazırlığı (ProductionGuard, hız sınırı, sağlık uçları, SMTP) | bitti |
| B | Kullanıcıyı yakan hatalar (portföy, cüzdan gösterimi, kanıt sinyali, doğrulama e-postası) | bitti |
| C | Yaptırım zinciri (anında ban, unban, geçici askı, rol atama, itiraz tekrarı) | bitti |
| D | Ürün görünürlüğü (profil bağlantıları, gönüllü ders bayrağı, gelen kutusu, hesap özeti) | bitti |
| E | Ölçek ve veri (sayfalama, eşleşme kapanışı/süre dolumu, dosya temizliği, panel sorgusu, süpürücü geri çekilme) | bitti |

## F — Ürün kararları (TAMAMLANDI, 2026-08-17)

Beş maddenin beşi de kapandı. Aşağıdaki kayıtlar kararların GEREKÇESİNİ saklamak için
duruyor — özellikle "neden böyle yapılmadı" kısımları.

### F1. 30 vs 60 dakika kredi birimi — **REVİZYONLA ÇÖZÜLDÜ**
Yeni model süreye göre ölçekliyor (30 dk = 50, 60 dk = 100 kredi). Bu madde kapandı.

### F1b. Marka rengi tutarsızlığı — **KAPANDI** (2026-08-17)
Üç ayrı mavi tek skalada birleşti: **Sky Blue, `#0088CC` çevresinde**
(`frontend/tailwind.config.js`).

**KRİTİK BULGU — istenen renk doğrudan buton olamazdı.** Ölçüldü (WCAG 2.1):

| | beyaz metinle |
|---|---|
| `#0088CC` (istenen) | **3.89:1** — AA (4.5) KALIR |
| `#0077B3` (brand-600) | 4.90:1 — geçer |
| eski indigo `#4f46e5` | 6.29:1 — geçer |

Bu yüzden `#0088CC` **500 basamağında** duruyor (marka kimliği: logo, odak kenarlığı) ve
gövde renkleri ondan koyulaştırılarak türedi. "İstenen rengi koru" ile "okunabilir kal"
çatışmadı; renk kimlikte kaldı, zemin görevi bir basamak aşağı taşındı.

**İKİNCİ BULGU:** eski logo vurgusu `#38BDF8` beyaz üzerinde **2.14:1** veriyordu — büyük
metin eşiğini (3.0) bile geçmiyordu, yani logo zaten erişilebilir değildi. Yeni değerle
3.89:1. Ayrıca logonun zemini `#EEF2FF` **indigo-50**'ydi: logo, izlediğini söylediği
`brand-50` ile farklı bir renk ailesindeydi.

Koyu tema aslı (`resimler/gemini-svg.svg`) `brand-400 #33A7DF` kullanıyor — koyu zeminde
6.57:1. **Aynı rengi iki temada kullanmak zorunlu değil; zorunlu olan aynı skaladan
gelmesi.** `#38BDF8` koyu zeminde sorunsuzdu (8.33:1); kusur rengin kendisinde değil,
açık temaya taşınmasındaydı.

Doğrulama: hesaplanmış CSS'ten okundu — buton `#0077B3`, logo `#0088CC`, rozet `#CCE9F7`,
zemin `#F8FAFC`.

> ⚠️ `hwid.js` içindeki `#4f46e5` **DEĞİŞMEDİ**. Artık paletteki hiçbir renge karşılık
> gelmiyor ve bu iyi: "tutarlılık düzeltmesi" görüntüsü ortadan kalktı. Dosyadaki uyarı
> da bu yeni duruma göre güncellendi.

### F2. Eğitmen gelmediğinde iade — **KAPANDI** (2026-08-17)
Sorunun kaynağı öğrencinin bloke kredisiydi; ders ücretsizleşince ortadan kalktı. Bugün
yapılan temizlikle kod ve belge de hizalandı:

- `SweepSessions` artık **deftere hiç dokunmuyor**. `CreditLedgerService` ve
  `IDistributedLockProvider` bağımlılıkları kaldırıldı — atanıyor ama hiç kullanılmıyorlardı
  (escrow döneminden kalma). Her süpürme turunda boşuna kuruluyor ve okuyucuya "süpürücü
  krediye dokunuyor" izlenimi veriyorlardı.
- Sınıf açıklaması, 2. fazın iade fazı olmadığını ve puana tek dolaylı etkinin 1. fazdaki
  otomatik onay olduğunu söylüyor.
- `RespondedAtUtc`'ye dokunmama kuralının gerekçesi yeniden yazıldı: dayanağı artık emekli
  olan ⚡ rozeti değil, alanın kendi anlamı ("muhatap ne zaman karar verdi").
- Gecikmiş itiraz log'u düzeltildi: bekleyen taraf artık öğrencinin bloke kredisi değil,
  **eğitmenin basılmamış puanı**.

> AÇIK KALAN TEK NOKTA (ürün kararı): itiraz karara bağlanana kadar eğitmenin puanı
> basılmıyor ve beklemenin ÜST SINIRI yok. Bedelin yüzü değişti, kendisi durmuyor.

### F3. `AdminGrant` / `AdminAdjustment` yazma yolu — **KAPANDI** (2026-08-17)
`POST /api/admin/users/{userId}/credits` — pozitif tutar ekler, negatif düşer.
**Yalnızca Admin** (moderatöre kapalı): ekonominin kullanıcı akışları dışındaki tek
yazma yolu.

Korunan kurallar:
- **Unvan sayacına dokunmaz.** `TotalEarnedCredits` yalnızca ders anlatarak artar;
  yönetim eliyle unvan dağıtılamaz. Bu aynı zamanda
  `TotalEarnedCredits = SUM(LessonEarning)` denetimini de korur.
- Ekleme **vadesiz** lot açar (yanan bir düzeltme kendini geri alırdı).
- Düşme lotları FIFO tüketir ve **bloke bakiyeye dokunmaz**; bakiyeyi aşan düşme
  `409 ADJUSTMENT_EXCEEDS_BALANCE`.
- Gerekçe zorunlu (≥10 karakter), tek işlem tavanı ±10.000 (yazım hatası freni).
- Her düzeltme `AdminActionLogs`'a `CreditAdjusted` olarak, tutar + gerekçe + karar
  anındaki rol ile düşer; reddedilen istekler iz bırakmaz.

Testi: `tools/e2e-admin-credits.ps1` (30 kontrol). İki mutasyonla kanıtlandı — sayacı
artırmak ve lot tüketimini atlamak, ilgili iddiaları kırmızıya düşürüyor.

Arayüz: hakem panelinin **Ekonomi** sekmesinde, arz metriklerinin hemen altında
(düzeltmenin etkisi aynı ekranda görünsün diye).

> BİLİNÇLİ EKSİK: idempotens anahtarı yok. İki kez tıklamak iki düzeltme yazar; koruma,
> her işlemin denetim izinde görünür ve ters işaretli bir düzeltmeyle geri alınabilir
> olması. Bu yüzden başarı bildiriminin GÖRÜNMESİ bir kolaylık değil, korumanın parçası:
> yönetici sonucu göremezse tekrar dener. (Metrik tazelemesi sessiz yapılmalı — normal
> `reload` formu yeniden monte edip bildirimi yutuyordu; bkz. Admin.jsx'teki not.)

### F4. dersmate / PeerLearn adlandırması — **KAPANDI** (2026-08-17)
**Ürün adı: dersmate.** Birleşme YALNIZCA kullanıcı yüzeyinde yapıldı; kod tabanı bilinçli
olarak PeerLearn kaldı.

Ölçüm, kararı kolaylaştırdı: kullanıcının gerçekten gördüğü yüzey **4 dizeydi** (sekme
başlığı, giriş sayfası alt metni, 2 e-posta konusu). `dersmate` ise yalnızca 3 yerde
geçiyordu (Logo bileşeni + bir SVG yorumu). Yani logo bir şey, yazı başka bir şey diyordu.

Değişenler:
- `frontend/index.html` → `<title>dersmate — Akran Eğitimi Platformu</title>`
- `Login.jsx` → "dersmate'te para transferi yoktur…"
- E-posta konuları artık tek sabitten okunuyor:
  `PeerLearn.Application/Common/Branding.cs` → `Branding.ProductName`

**DEĞİŞMEYENLER ve gerekçeleri** (1.990 kullanım, 194 dosya — hepsi kullanıcıya görünmez):
namespace'ler, proje/çözüm adları, `peerlearn` veritabanı ve kullanıcısı, `PeerLearnBuild`
derleme yolu, Swagger başlığı. Üçü ayrıca kırılgan:
`Jwt:Issuer`/`Audience` (değişirse yaşayan tüm oturumlar düşer), veritabanı adı (göç
gerekir) ve aşağıdaki HWID satırı.

> Marka adı ile namespace'in farklı olması bir tutarsızlık DEĞİL, bilinçli bir ayrım:
> biri marka, diğeri altyapı kimliği. Gerekçe `Branding.cs` içinde de yazılı.

> ⚠️ TUZAK (2026-08-17'de yakalandı): `frontend/index.html` çalışma alanına **KOPYALANIR**,
> junction DEĞİLDİR (yalnızca `src`/`public` junction). Drive'da düzenlemek Vite'a hiç
> ulaşmaz ve değişiklik sessizce yok sayılır. `tools/start-dev.ps1` artık yapılandırma
> dosyalarını her başlangıçta yeniden kopyalıyor.

> ⚠️ **ASLA DEĞİŞTİRİLMEYECEK YER:** `frontend/src/lib/hwid.js` içindeki `canvasSignal()`
> fonksiyonunun **tamamı**. Çizilen her şey hash'e giriyor; tek bayt değişirse TÜM mevcut
> HWID banları geçersiz olur ve banlı kullanıcılar geri döner.
>
> İki ayrı tuzak var ve ikisi de "zararsız düzeltme" gibi görünür:
> 1. **İsim değişikliği** → `ctx.fillText('PeerLearn', 2, 2)`. Ürün adı dersmate olsa bile
>    bu satır olduğu gibi kalır.
> 2. **Palet değişikliği** → `ctx.fillStyle = '#4f46e5'`. Bu değer Tailwind `brand-600` ile
>    aynı; marka rengi değişirse buranın da güncellenmesi son derece doğal görünür.
>    **GÜNCELLENMEMELİDİR** — buradaki renk bir tasarım kararı değil, parmak izinin
>    sabitidir; paletle aynı olması tarihsel bir tesadüf.
>
> Gerekçe artık dosyanın kendisinde de yazılı (2026-08-17).

## G — Suistimal frenleri (2026-08-17)

### G1. Eğitmen tavanı eşzamanlılıkla aşılıyordu — **KAPANDI**

MintGuard'ın iki tavanı var ve farklı şeylere karşı korurlar:

| tavan | sayım neye bakar | neyi keser |
|---|---|---|
| çift (2/gün) | (eğitmen, öğrenci) ikilisi | iki hesabın birbirini beslemesi |
| eğitmen (8/gün) | yalnızca eğitmen | bir hesabın sahte öğrenci ordusundan ders toplaması |

İkincisi **hiç çalışmıyordu**. Rezervasyon kilidi `lock:pair:A:B` idi; farklı öğrenciler
farklı kilitlere düştüğü için aynı eğitmene yönelen paralel istekler birbirini hiç
görmüyordu. Sayacı hepsi aynı anda 0 okuyup geçiyordu.

Ölçüm (`tools/e2e-mintguard.ps1`, iki instance'a bölünmüş 12 eşzamanlı istek):

| | düzeltme öncesi | sonrası |
|---|---|---|
| kabul edilen | **12 / 12** | 8 / 12 |
| MINT_LIMIT_REACHED | 0 | 4 |
| veritabanındaki ders | 12 | 8 |

"Teorik bir açık" değildi: tavan sıfır etkiliydi.

**Düzeltme:** kilit anahtarı eğitmen bazına alındı (`LockKeys.SessionPair` → `LockKeys.Tutor`).
Bu, çift tavanını da kapsar ve kapsaması tesadüf değil — MintGuard'ın **her iki** sayımı da
`TutorUserId` ile filtreleniyor, dolayısıyla herhangi bir sayımı değiştirebilecek her yazma
bu kilidi almak zorunda. Çift anahtarı geniş değil, DAR kalıyordu.

Rol değiştirmeye karşı ayrı anahtar gerekmiyor: iki sayım da yönlü olduğu için A'nın
eğitmen olduğu bir ders B'nin sayımına hiç girmez.

`tools/e2e-mintguard.ps1` beş şeyi ölçüyor: tavanın sıralı doğruluğu (A), yarış altında
sağlamlığı (B), iptalin kotayı geri vermesi (C), pencerenin kayan olması (D) ve çift
tavanının gerilememesi (E). `tools/restart-api.ps1` de eklendi — kod değişikliği sıcak
yüklenmiyor, yeniden başlatılmadan koşan test **eski kodu** ölçer.

### G2. Çift tavanı yönlü: ikilinin gerçek sınırı 2 değil 4 — **(a) SEÇİLDİ, GÖZDEN GEÇİRİLECEK**

> **Karar (2026-08-17): şimdilik (a) — değişiklik yok.** Davranış olduğu gibi bırakıldı;
> ikili 24 saatte 4 ders / 400 puan üretebiliyor. Bu bir onay değil ERTELEME.
>
> **ZAMANLAMA PROJE SAHİBİNİN:** maddenin ne zaman açılacağına o karar verecek ve
> söyleyecek. Kendiliğinden gündeme getirilmeyecek — açık kalması bilinen ve kabul edilmiş
> bir durum, hatırlatılması gereken bir eksik değil.
>
> Karar değişirse dokunulacak tek yer MintGuard'ın çift sayımıdır (kilit zaten eğitmen
> bazında ve her iki tavanı da kapsıyor, yani (b) ya da (c) için kilit tarafında ek iş YOK).
>
> Gözden geçirirken elde olması gereken sayı: bir ikilinin günlük üst sınırı **4 ders =
> 400 puan**; unvan eşiği Öğretici için 500 puan, yani iki sahte hesap iki günde unvan
> atlayabilir. Kararı bu rakamla verin.


Sayım `TutorUserId == X && StudentUserId == Y` biçiminde, yani yönlü. Ölçüldü:

```
A öğrenci / B eğitmen : KABUL, KABUL, RET 429
B öğrenci / A eğitmen : KABUL, KABUL, RET 429
ikilinin 24 saatteki toplam dersi: 4   (belgede yazan tavan: 2)
```

İki hesap günde 4 ders = **400 puan** üretebiliyor. Kilit yorumu ise "rolleri değiştirerek
yapılan saldırı" için tek anahtar kullandığını, yani ikilinin bütün olarak sınırlandığını
söylüyordu; ikisi aynı anda doğru olamazdı. Yorum ölçülen gerçeğe göre düzeltildi.

**Neden kendiliğinden değiştirilmedi:** platform akran eğitimi. İki kişinin birbirine ders
anlatması suistimal değil, ürünün kendisi. Tavanı ikili bazına çevirmek karşılıklı öğreten
dürüst kullanıcıları da yarıya indirir. Seçenekler:

- **(a) Bırak** — 400 puan/gün kabul edilebilir bir tavan sayılır. Değişiklik yok.
- **(b) İkili bazına çevir** — sayımdan yön kaldırılır, ikili günde 2 ders. Karşılıklı
  öğrenen dürüst kullanıcı da etkilenir.
- **(c) Ayrı bir ikili tavanı** — yön korunur (her yön 2) ama ikiliye ayrıca 3 gibi bir
  üst sınır konur. Karşılıklılığı öldürmez, simetrik suistimali kırpar.

### G3. Puan düzeltmesinde tekillik yoktu — **KAPANDI**

Uç bilinçli olarak idempotent değildi ve gerekçesi şuydu: "her düzeltme denetim izine ayrı
satır bırakır, yani yanlış GÖRÜNÜR ve ters işaretli bir düzeltmeyle geri alınabilir."
Bu, kusuru bir özellik gibi anlatıyordu.

**Asıl açık çift tıklama değildi.** Panelde buton zaten istek boyunca kilitli. Tehlikeli
olan hiç konuşulmayan yol: istek sunucuya ulaşır, defter yazılır, **yanıt dönerken
bağlantı kopar**. Yönetici bir hata görür ve doğal olarak tekrar dener — puan iki kez
uygulanır. "Geri alınabilir olması" burada işe yaramaz, çünkü geri alınması gerektiği
fark edilmez.

İkinci gerekçe ("istemci anahtar üretmiyor, alan korumadığı bir şeyi koruyormuş gibi
görünür") doğruydu ama çözümü alanı eklememek değil, istemciye anahtar ÜRETTİRMEKTİ.

**Yapılan:**

| katman | değişiklik |
|---|---|
| şema | `moderation.AdminActionLogs.IdempotencyKey` + KISMİ unique index `(ActorUserId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL` |
| uç | `Idempotency-Key` başlığı **zorunlu** (yoksa 400) |
| davranış | aynı anahtar + aynı yük → uygulamaz, ilk sonucu döner (`replayed: true`) |
| davranış | aynı anahtar + **farklı** yük → 409 `IDEMPOTENCY_KEY_REUSED`, hiçbir şey yazılmaz |
| panel | anahtar form doldurulmadan üretilir, yalnızca BAŞARIDA yenilenir |

Üç tasarım kararı ve gerekçeleri:

1. **Ayrı tablo değil denetim izinde.** Tekillik kaydının ömrü denetim izininkiyle aynı
   olmalı; ayrı tablo temizlendiği an çok eski bir isteğin tekrarı sessizce yeniden
   uygulanırdı. Denetim izi hiç silinmiyor, yani koruma da kalıcı.
2. **Gövdede değil başlıkta.** Anahtar işlemin ne olduğuna dair bilgi değil, isteğin
   kimliği. Gövdede olsaydı "yük aynı mı" karşılaştırmasına kendisi de girerdi.
3. **Farklı yükte sessiz tekrar değil 409.** Yönetici tutarı 100'den 200'e düzeltip
   tekrar gönderdiyse, tekrar oynatma 100'ü uygular ve "uygulandı" der — yani hata,
   kullanıcının düzeltmeye çalıştığı şeyin ta kendisini gizler.

**Anahtarın nerede üretildiği korumanın tamamıdır.** `submit` içinde üretilseydi her
deneme yeni anahtar alırdı ve koruma tam da işe yaraması gereken anda — "hata gördüm,
tekrar denedim" — hiçbir şey yapmazdı.

**Kanıt.** `tools/e2e-admin-credits.ps1` H bölümü (47 kontrol, hepsi geçti): tekrar
oynatma, farklı tutar/gerekçe 409'u, anahtarsız istek 400'ü, anahtarın yöneticiye göre
tekilliği ve **eşzamanlı 4 aynı-anahtar isteğinden defterde tek hareket kalması** (kısmi
unique index'in son savunma olarak sınandığı yer).

Ayrıca panelde gerçek senaryo canlandırıldı: `fetch` sarmalanıp istek sunucuya ulaştırıldı
ama yanıt çağırandan gizlendi. Sunucu 200 döndü, panel ağ hatası gösterdi, tekrar
gönderildi → *"Bu düzeltme zaten uygulanmıştı; ikinci kez yazılmadı"*. Veritabanında
tutar bir kez yazılmıştı ve defter dengeliydi.

## Geliştirme ortamı (2026-08-17'de eklendi)

`docker-compose.yml` — PostgreSQL 16 + Redis 7. Uygulamanın kendisi konteynerde değil;
API `dotnet run`, arayüz `npm run dev` ile koşmaya devam ediyor. Sıfırdan kurulum sırası
ve takılma tablosu: **`docs/GELISTIRME-ORTAMI.md`**.

```bash
docker compose up -d && docker compose ps        # healthy olmasını bekle
dotnet run --project src/PeerLearn.Api -- --migrate
```

Kimlik bilgileri `appsettings.json`'la birebir aynı tutuldu (doğrulandı). Üç bilinçli
karar, üçü de sonradan "düzeltilmesi" cazip görünecek türden:

- **Portlar `127.0.0.1`'e bağlı** — salt `5432:5432` veritabanını ağa açar, Windows
  güvenlik duvarı bunu sormaz.
- **Collation `C`** (bayt sırası), `tr_TR` değil: yerel ayara bağlı sıralama iki
  geliştiricide farklı sonuç verir ve indeksler ICU/glibc yükseltmesinde bozulur. Türkçe
  sıralama gereken sorgu bunu açıkça istemeli — `ORDER BY x COLLATE "tr-TR-x-icu"`
  (çalıştığı doğrulandı).
- **Redis kalıcılığı kapalı** — yeniden başlayan konteyner eski kilitleri geri yükleseydi
  "cüzdan kilidi alınamadı" hataları sebepsiz görünürdü.

> `POSTGRES_INITDB_ARGS` yalnızca hacim boşken uygulanır; encoding/collation değiştirmek
> `docker compose down -v` (veri silinir) gerektirir.

## Uçtan uca tarayıcı testleri (2026-08-17'de eklendi)

`frontend/e2e/` — Playwright, **backend gerektirmez**. Kurulum, koşturma ve dosya haritası:
`frontend/e2e/README.md`.

```bash
cd frontend && npm install && npx playwright install chromium
npm run test:e2e
```

Paket, arayüzün **sessizce bozulabilen sözleşmelerini** kilitliyor — hiçbiri derleme hatası
vermez, hiçbiri gözle fark edilmez: HWID parmak izinin dokunulmazlığı, paletin tek kaynakta
kalması (skala ↔ logo ↔ favicon senkronu), ürün adının ekranda dersmate olması, WCAG AA
kontrast eşikleri, oturum kapısı ve dokunma hedefi ölçüleri.

API bilerek taklit ediliyor: PostgreSQL + Redis + dotnet ayakta olmadan koşamayan bir paket
pratikte hiç koşulmaz. Ekonomi akışlarının uçtan uca kanıtı `tools/` altındaki PowerShell
betiklerinde ve `tests/PeerLearn.UnitTests`'te kalıyor.

**Kanıt:** 26 testin tamamı, 14 ayrı mutasyonla kırıldığı gösterilerek doğrulandı —
`brand-600`'ü marka tonuna çekmek, stok `sky-600` kullanmak, palete indigoyu geri koymak,
başlığı/altbilgiyi PeerLearn'e döndürmek, `hwid.js`'teki iki sabiti "hizalamak", logo
vurgusunu paletten kaydırmak, `min-h-11`'i silmek, doğrulama kurtarma düğmesini kapatmak,
bir bileşene sabit hex yazmak ve `RequireAuth` kapısını devre dışı bırakmak. On dördü de
kırmızıya düştü, sıfır kaçak.

> `@playwright/test` sürümü **tam sabit** (`1.56.0`, `^` yok): tarayıcı ikilileri sürüme
> kilitli, caret bırakılırsa `npm install` yeni sürüm çeker ve paket
> "Executable doesn't exist" ile düşer.

> **BİZİM PALETİMİZE UYARLANDI (2026-08-18).** `marka.spec.js` içindeki "logo vurgusu"
> testi `palet[400]` bekliyordu; bizde marka tonu `#0088CC` **brand-500**'de duruyor
> (gövde rengi olarak AA'yı kaçırdığı için kimlik 500'de, zemin görevi 600'de — bkz. F1b).
> Doğrulama `palet[500]`'e çevrildi. Testlerin geri kalanı paleti config'ten OKUDUĞU için
> hiç dokunulmadı; sabit hex gömülmemiş olması bu uyarlamayı tek satıra indirdi.

## Devam ederken hatırlanacaklar

- **BadgeEngine sıra kuralı:** motor veriyi DB'den okur, bu yüzden `EvaluateAsync`
  SaveChanges'ten SONRA çağrılmalı. Desen: `BeginTransaction → SaveChanges → EvaluateAsync
  → SaveChanges → Commit`. Bu tuzağa proje boyunca dört kez düşüldü.
- **EF izleme tuzağı:** sorgunun HERHANGİ bir yerindeki `AsNoTracking()` tüm sonucu
  izlenmeyen yapar. Yazma amaçlı sorgularda birleşimin karşı tarafında bile kullanma.
- **Test kanıtı:** eklenen her testin, ilgili kod bozulduğunda KIRILDIĞI mutasyonla
  gösterilmeli. "Geçiyor" yetmez.
- Tarayıcı testleri **yeni bir Playwright sekmesinde** yapılmalı.
