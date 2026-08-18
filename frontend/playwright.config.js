import { defineConfig, devices } from '@playwright/test'

/*
  Uçtan uca tarayıcı testleri.

  KAPSAM KARARI — bu paket API'siz koşar.
  Buradaki testler Vite dev sunucusundan başka hiçbir şeye bağlı değil: backend'e giden her
  istek `e2e/yardimcilar.js` içindeki `apiyiTaklitEt()` ile kesiliyor. Sebep, testlerin
  kolay olması değil; PostgreSQL + Redis + dotnet ayakta olmadan koşamayan bir paketin
  pratikte hiç koşulmamasıdır. Ekonomi akışlarının uçtan uca kanıtı zaten `tools/` altındaki
  PowerShell betiklerinde (ör. e2e-admin-credits.ps1) ve `tests/PeerLearn.UnitTests`'te.

  Bu paket şunu koruyor: ARAYÜZÜN SESSİZCE BOZULABİLEN SÖZLEŞMELERİ — marka rengi, ürün adı,
  erişilebilirlik eşikleri ve HWID parmak izinin dokunulmazlığı. Hepsi "zararsız görünen bir
  düzeltme"yle kırılabilecek, hiçbiri derleme hatası vermeyecek şeyler.

  Gerçek API'ye karşı koşmak istersen: dotnet API'yi ayağa kaldır, `apiyiTaklitEt()`
  çağrısını ilgili testten çıkar. Varsayılan akış bu değildir.
*/
export default defineConfig({
  testDir: './e2e',

  // Google Drive üzerinde dosya erişimi yavaş; varsayılan 5 sn dar kalıyor.
  timeout: 30_000,
  expect: { timeout: 7_000 },

  // CI'da yanlışlıkla bırakılmış .only tüm paketi sessizce daraltır — hata ver.
  forbidOnly: !!process.env.CI,

  /*
    CI'da 2 deneme, yerelde 0. Yerelde sıfır olması bilinçli: bu paketteki testlerin hiçbiri
    zamanlamaya bağlı değil (ağ taklit ediliyor), yani yerelde kırılan test GERÇEKTEN
    kırıktır. Tekrar denemek onu gizlerdi.
  */
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,

  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: 'http://localhost:5173',
    // İlk denemede iz tutulmaz (yavaş); yalnızca tekrar denemede. Kırılan testi
    // `npx playwright show-report` ile adım adım izleyebilirsin.
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    locale: 'tr-TR',
    timezoneId: 'Europe/Istanbul',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  /*
    Dev sunucusunu Playwright başlatır. `reuseExistingServer` yerelde açık: zaten
    `npm run dev` çalıştırıyorsan ikinci bir sunucu açıp 5173'ü çakıştırmaz.
    CI'da kapalı — orada her koşu temiz bir sunucu ister.
  */
  webServer: {
    command: 'npm run dev -- --port 5173 --strictPort',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
})
