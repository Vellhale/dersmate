# Uçtan uca tarayıcı testleri

Playwright. `frontend/` altından koşar, **backend gerektirmez.**

## Kurulum

```bash
cd frontend
npm install
npx playwright install chromium     # tarayıcı ikilisi ayrı iner (npm paketiyle gelmez)
```

`@playwright/test` sürümü **tam sabitlenmiştir** (`1.56.0`, `^` yok). Playwright'ın tarayıcı
ikilileri sürüme kilitlidir; caret bırakılırsa `npm install` yeni bir sürüm çeker ve
`npx playwright install` tekrar koşulana kadar tüm paket "Executable doesn't exist" ile
düşer. Ekipteki iki makinenin aynı tarayıcıda koşması da bunu gerektiriyor.

## Koşturma

```bash
npm run test:e2e            # tümü
npm run test:e2e:ui         # Playwright UI (adım adım, zaman tüneli)
npm run test:e2e:report     # son koşunun HTML raporu
npx playwright test --grep "WCAG"        # tek bir konuyu koştur
npx playwright test --headed             # tarayıcıyı görerek
```

Dev sunucusunu Playwright kendisi başlatır (`playwright.config.js` → `webServer`). Zaten
`npm run dev` çalışıyorsa onu yeniden kullanır, ikinci sunucu açmaz.

## Neden API'siz

Buradaki testler backend'e giden her isteği `yardimcilar.js` içindeki `apiyiTaklitEt()`
ile keser. Bu bir kolaylık değil, kapsam kararı: PostgreSQL + Redis + dotnet ayakta
olmadan koşamayan bir paket pratikte hiç koşulmaz. Ekonomi akışlarının uçtan uca kanıtı
`tools/` altındaki PowerShell betiklerinde ve `tests/PeerLearn.UnitTests`'te.

Bu paketin koruduğu şey **arayüzün sessizce bozulabilen sözleşmeleri**: hiçbiri derleme
hatası vermez, hiçbiri gözle fark edilmez, hepsi "zararsız bir düzeltme"yle kırılır.

## Dosyalar

| Dosya | Neyi kilitliyor |
|---|---|
| `kaynak-sabitleri.spec.js` | HWID parmak izinin bayt düzeyinde dokunulmazlığı; paletin tek kaynakta kalması (logo + favicon + skala senkronu, sabit renk yasağı). Tarayıcı açmaz. |
| `marka.spec.js` | Ürün adının ekranda dersmate olması, marka renginin gerçekten render edilmesi, WCAG AA kontrast eşikleri. |
| `giris-akisi.spec.js` | Oturum kapısı, giriş formu, hata kodlarına verilen tepkiler, dokunma hedefi ölçüleri. |
| `yardimcilar.js` | API taklidi, renk ayrıştırma, WCAG kontrast hesabı. Test değil. |

## En kritik iki test

**`hwid.js bayt düzeyinde değişmedi`** — `canvasSignal()` içinde çizilen her şey cihaz
parmak izine giriyor. Tek bayt değişirse üretimdeki **tüm HWID banları geçersiz olur** ve
banlı kullanıcılar geri döner; geri alınamaz. Test dosyanın SHA-256'sını sabitliyor.

> Bu test kırıldıysa beklenen hash'i güncellemek **çözüm değildir.** Önce neyin
> değiştiğine bak. Detay: `docs/DEVAM-EDILECEK.md`, F4 altındaki uyarı.

**`birincil düğmedeki yazı WCAG AA eşiğini geçiyor`** — marka tonu `#38BDF8` ve bir gün
birinin "marka rengi buysa düğme de bu olsun" demesi çok doğal. Beyaz yazıyla kontrastı
2.14:1, yani okunmaz. Test rengi sabite karşı değil **eşiğe** karşı ölçer: paleti
değiştirmek serbest, erişilemez hâle getirmek değil.

## Test eklerken

Proje kuralı (`docs/DEVAM-EDILECEK.md`): **"Geçiyor" yetmez.** Eklediğin her testin,
koruduğu şey bozulduğunda gerçekten KIRILDIĞINI mutasyonla göster — ilgili satırı boz,
testin kırmızıya düştüğünü gör, sonra geri al. Bu paketteki 26 testin tamamı 14 ayrı
mutasyonla bu şekilde doğrulandı (2026-08-17).
