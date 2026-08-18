# dersmate (PeerLearn)

Öğrencilerin birbirine ders verdiği akran öğrenme platformu. Öğrenci ders almak için
**hiçbir şey ödemez**; puan, dersi veren tarafa onay anında **basılır**.

## Yığın

| Katman | Teknoloji |
|---|---|
| Arka uç | .NET 8, modüler monolit, MediatR |
| Veri | PostgreSQL 16 — **modül başına ayrı şema** (`catalog`, `identity`, `matchmaking`, `comms`, `scheduling`, `economy`, `moderation`, `community`) |
| Önbellek / kilit | Redis 7 |
| Ön yüz | React + Vite + Tailwind |
| Testler | xUnit (birim) + PowerShell uçtan uca paketleri |

## Hızlı başlangıç

```bash
docker compose up -d
dotnet run --project src/PeerLearn.Api
```

```bash
cd frontend && npm install && npm run dev
```

Arayüz `http://localhost:5173`, API `http://localhost:5000`.

**Sadece tasarıma bakacaksan** hiçbirine gerek yok — `frontend/dersmate-onizleme.html`
dosyasına çift tıkla. Sahte API gömülü, ağa çıkmaz.

Ayrıntılı kurulum: [`docs/GELISTIRME-ORTAMI.md`](docs/GELISTIRME-ORTAMI.md)

## Testler

```bash
powershell -ExecutionPolicy Bypass -File .\tools\run-all-tests.ps1
```

14 paket, 562 kontrol. Eşzamanlılık ve suistimal paketleri **iki API instance** ister
(`:5000` ve `:5001`); ikincisi yoksa `-SkipConcurrency` ekle — özet bunu `EKSİK` olarak
raporlar, sessizce yeşile dönmez.

## Belgeler

| Dosya | İçerik |
|---|---|
| [`docs/ASAMA-1-MIMARI.md`](docs/ASAMA-1-MIMARI.md) | Modül sınırları, şema ayrımı, kilit stratejisi |
| [`docs/ASAMA-2-BACKEND.md`](docs/ASAMA-2-BACKEND.md) | Ekonomi, moderasyon, arka plan işleri |
| [`docs/ASAMA-3-FRONTEND.md`](docs/ASAMA-3-FRONTEND.md) | Sayfalar, bileşenler, tasarım kararları |
| [`docs/GELISTIRME-ORTAMI.md`](docs/GELISTIRME-ORTAMI.md) | Sıfırdan kurulum |
| [`docs/URETIME-CIKIS.md`](docs/URETIME-CIKIS.md) | Üretim ayarları ve kapılar |
| [`docs/DEVAM-EDILECEK.md`](docs/DEVAM-EDILECEK.md) | Açık işler |

## Bilinmesi gerekenler

**Depodaki parolalar geliştirme parolalarıdır.** `appsettings.json` içindeki Postgres
parolası ve JWT anahtarı yalnızca localhost içindir ve açıkta durmaları bilinçlidir
(`DEV-ONLY-...-CHANGE-IN-PRODUCTION`). Üretim değerleri depoya **girmez** —
`docs/URETIME-CIKIS.md`'e bak.

**`.claude/launch.json` makineye özeldir.** `frontend` yapılandırmasındaki yol bir
kullanıcının makinesine sabitlenmiş durumda; kendi makinende çalışmazsa oradan düzelt.

⚠️ **`frontend/src/lib/hwid.js` içindeki `canvasSignal()` fonksiyonuna dokunma.**
Çizilen metin ve renk, cihaz parmak izinin **girdisidir**. Herhangi birini değiştirmek
mevcut TÜM donanım banlarını geçersiz kılar ve banlanmış kullanıcılar geri döner.
Dosyanın içindeki uyarıyı oku.

## Birlikte çalışma

Ana dal `main`. Doğrudan `main`'e itmek yerine dal aç, PR aç; iki kişi aynı dosyaya
aynı anda dokunduğunda birleştirme çakışmasını PR'da görmek, ittikten sonra görmekten
iyidir.

```bash
git switch -c ozellik/kisa-ad
git push -u origin ozellik/kisa-ad
```
