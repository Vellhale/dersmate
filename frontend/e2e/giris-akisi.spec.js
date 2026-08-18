import { test, expect } from '@playwright/test'
import { apiyiTaklitEt } from './yardimcilar.js'

/*
  Oturum kapısı ve giriş formunun temel davranışı. API taklit ediliyor (bkz.
  playwright.config.js başındaki kapsam notu), yani burada kanıtlanan şey backend'in
  doğruluğu değil — ARAYÜZÜN backend'in verdiği cevaba doğru tepki verdiği.
*/

test.describe('Oturum kapısı', () => {
  for (const yol of ['/kesfet', '/dersler', '/portfolio', '/profil', '/admin']) {
    test(`oturumsuz ${yol} girişe yönleniyor`, async ({ page }) => {
      await apiyiTaklitEt(page)
      await page.goto(yol)
      await expect(page).toHaveURL(/\/giris$/)
    })
  }

  test('tanınmayan adres keşfete, oradan girişe düşer', async ({ page }) => {
    await apiyiTaklitEt(page)
    await page.goto('/boyle-bir-sayfa-yok')
    await expect(page).toHaveURL(/\/giris$/)
  })
})

test.describe('Giriş formu', () => {
  test.beforeEach(async ({ page }) => {
    await apiyiTaklitEt(page)
    await page.goto('/giris')
  })

  test('form alanları ve gönder düğmesi görünür', async ({ page }) => {
    await expect(page.getByRole('heading', { name: 'Giriş yap' })).toBeVisible()
    await expect(page.locator('input[type="email"]')).toBeVisible()
    await expect(page.locator('input[type="password"]')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Giriş yap' })).toBeVisible()
  })

  test('dokunma hedefleri en az 44px yüksekliğinde', async ({ page }) => {
    /*
      ui.jsx'teki min-h-11 ve index.css'teki py-2.5 + text-base bilinçli kararlar:
      iOS Safari 16px'ten küçük yazılı bir alana odaklanınca sayfayı zorla yakınlaştırıyor.
      Sınıf silinirse görsel olarak fark edilmez ama mobilde form kullanılamaz hâle gelir.
      Ölçüm mobil genişlikte yapılmalı — masaüstünde lg: kuralları kutuyu küçültüyor.
    */
    await page.setViewportSize({ width: 390, height: 844 })

    for (const hedef of [
      page.locator('input[type="email"]'),
      page.locator('input[type="password"]'),
      page.getByRole('button', { name: 'Giriş yap' }),
    ]) {
      const kutu = await hedef.boundingBox()
      expect(kutu.height).toBeGreaterThanOrEqual(44)
    }
  })

  test('hatalı giriş, backend mesajını gösterir ve sayfada kalır', async ({ page }) => {
    await apiyiTaklitEt(page, {
      'POST /api/auth/login': {
        status: 401,
        json: { status: 401, title: 'INVALID_CREDENTIALS', detail: 'E-posta veya şifre hatalı.' },
      },
    })

    await page.locator('input[type="email"]').fill('yok@example.com')
    await page.locator('input[type="password"]').fill('yanlissifre')
    await page.getByRole('button', { name: 'Giriş yap' }).click()

    await expect(page.getByText('E-posta veya şifre hatalı.')).toBeVisible()
    await expect(page).toHaveURL(/\/giris$/)
  })

  test('doğrulanmamış hesaba doğrulama sayfasına çıkış verilir', async ({ page }) => {
    /*
      Login.jsx'te bilinçli bir kurtarma yolu var: EMAIL_NOT_VERIFIED kodunda ekstra bir
      düğme beliriyor. Kod eşleşmesi elle yazılmış bir string; backend tarafında kod
      yeniden adlandırılırsa bu düğme sessizce kaybolur ve kullanıcı çıkmaza girer.
    */
    await apiyiTaklitEt(page, {
      'POST /api/auth/login': {
        status: 403,
        json: { status: 403, title: 'EMAIL_NOT_VERIFIED', detail: 'Hesabın doğrulanmamış.' },
      },
    })

    await page.locator('input[type="email"]').fill('bekleyen@example.com')
    await page.locator('input[type="password"]').fill('birsifre')
    await page.getByRole('button', { name: 'Giriş yap' }).click()

    await expect(page.getByRole('button', { name: 'E-postamı doğrula' })).toBeVisible()
  })

  test('API kapalıyken ağ hatası mesajı gösteriliyor', async ({ page }) => {
    // Taklit yok: istek gerçekten başarısız olsun.
    await page.route('**/api/**', (route) => route.abort('connectionrefused'))

    await page.locator('input[type="email"]').fill('biri@example.com')
    await page.locator('input[type="password"]').fill('birsifre')
    await page.getByRole('button', { name: 'Giriş yap' }).click()

    await expect(page.getByText(/Sunucuya ulaşılamadı/)).toBeVisible()
  })
})
