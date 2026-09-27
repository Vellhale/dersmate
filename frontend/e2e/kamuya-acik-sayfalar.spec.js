import { test, expect } from '@playwright/test'
import { apiyiTaklitEt } from './yardimcilar.js'

/*
  OTURUMSUZ ULAŞILABİLMESİ GEREKEN SAYFALAR.

  Üçü de bir mağaza şartına bağlı ve üçünün de RequireAuth'un arkasına düşmesi
  SESSİZ bir hata olur: sayfa çalışmaya devam eder, yalnızca giriş yapmamış
  ziyaretçi — ve mağaza incelemecisi — onu hiç göremez. Bu test o sessizliği
  bozuyor.

    /gizlilik-uygulama  App Store Connect ve Play'in zorunlu "gizlilik politikası
                        adresi" alanı. /gizlilik verilemez: o sayfa çerezleri,
                        Google Analytics'i ve canvas parmak izini anlatıyor, üçü
                        de mobil uygulamada YOK.
    /hakkimizda         2026-09-28'de kabuğun dışına alındı; önce Layout'un
                        içindeydi ve dışarıdan okunamıyordu.
    /kosullar,
    /gizlilik,
    /hesap-silme        zaten dışarıdaydı; kapıyı bir daha kapatmamak için
                        burada birlikte sınanıyorlar.

  ⚠️ Yönlendirme kontrolü ŞART: rota korumalı bloğa düşerse sayfa açılmaz,
  /giris'e gider. İçerik kontrolü tek başına yetmez — App.jsx'in catch-all'ı
  bilinmeyen adresi /kesfet'e, oradan da /giris'e düşürüyor, yani "sayfa yok"
  ile "sayfa korumalı" aynı sonucu veriyor.
*/

const KAMUYA_ACIK = [
  { yol: '/hakkimizda', baslik: 'Hakkımızda' },
  { yol: '/gizlilik-uygulama', baslik: 'Mobil uygulama — gizlilik ve KVKK aydınlatma metni' },
  { yol: '/gizlilik', baslik: 'Gizlilik ve KVKK aydınlatma metni' },
  { yol: '/kosullar', baslik: 'Kullanım koşulları' },
]

test.describe('Oturumsuz ulaşılabilen sayfalar', () => {
  for (const { yol, baslik } of KAMUYA_ACIK) {
    test(`${yol} girişe yönlenmiyor ve başlığı çiziliyor`, async ({ page }) => {
      await apiyiTaklitEt(page)
      await page.goto(yol)
      await expect(page).toHaveURL(new RegExp(`${yol}$`))
      await expect(page.getByRole('heading', { name: baslik, level: 1 })).toBeVisible()
    })
  }
})

test.describe('Hakkımızda — oturumsuz kabuk', () => {
  test.beforeEach(async ({ page }) => {
    await apiyiTaklitEt(page)
    await page.goto('/hakkimizda')
  })

  /* Hedef oturuma göre değişiyor: oturumsuz ziyaretçiyi "Keşfet'e göz at" ile giriş
     duvarına çarptırmak, sayfayı herkese açmanın anlamını yok ederdi. */
  test('çağrı düğmesi kayda götürüyor, Keşfet’e değil', async ({ page }) => {
    const dugme = page.getByRole('link', { name: 'Hesap oluştur' })
    await expect(dugme).toBeVisible()
    await expect(dugme).toHaveAttribute('href', '/kayit')
    await expect(page.getByRole('link', { name: 'Keşfet’e göz at' })).toHaveCount(0)
  })

  /* Alt bilgi Layout'tan geliyordu; kabuğun dışında sayfanın kendisi çiziyor.
     Düşerse künye ve yasal bağlantılar sessizce kaybolur. */
  test('alt bilgi ve yasal bağlantılar duruyor', async ({ page }) => {
    const alt = page.getByRole('navigation', { name: 'Alt bilgi' })
    await expect(alt.getByRole('link', { name: 'Kullanım koşulları' })).toBeVisible()
    await expect(alt.getByRole('link', { name: 'Gizlilik', exact: true })).toBeVisible()
  })

  /* "Rehberi tekrar izle" oturum istiyor; oturumsuz ziyaretçide çizilmemeli. */
  test('rehber düğmesi oturumsuzken çizilmiyor', async ({ page }) => {
    await expect(page.getByRole('button', { name: 'Rehberi tekrar izle' })).toHaveCount(0)
  })
})

test.describe('İki gizlilik metni birbirine bağlı', () => {
  /* Okuyucunun yanlış belgeye düşmemesi için: web metni çerezleri ve ölçüm
     taşıyıcısını anlatıyor, uygulama metni onları anlatmıyor. */
  test('web metninden uygulama metnine geçiliyor', async ({ page }) => {
    await apiyiTaklitEt(page)
    await page.goto('/gizlilik')
    await page.getByRole('link', { name: 'uygulamanın gizlilik metnini' }).click()
    await expect(page).toHaveURL(/\/gizlilik-uygulama$/)
  })

  test('uygulama metninden web metnine dönülüyor', async ({ page }) => {
    await apiyiTaklitEt(page)
    await page.goto('/gizlilik-uygulama')
    await page.getByRole('link', { name: 'web sürümünün gizlilik metnini' }).click()
    await expect(page).toHaveURL(/\/gizlilik$/)
  })
})
