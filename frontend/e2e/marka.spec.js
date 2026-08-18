import { test, expect } from '@playwright/test'
import tailwindConfig from '../tailwind.config.js'
import { apiyiTaklitEt, rgbAyristir, hexAyristir, kontrastOrani } from './yardimcilar.js'

/*
  MARKA SÖZLEŞMESİ — tarayıcıda, gerçekten render edilmiş hâli üzerinden.

  kaynak-sabitleri.spec.js dosyaların İÇERİĞİNİ kilitliyor; bu dosya o içeriğin ekrana
  BEKLENEN ŞEKİLDE çıktığını kilitliyor. İkisi farklı şeyler: tailwind.config.js doğru
  olabilir ama `content` deseni bir klasörü kaçırırsa sınıf hiç üretilmez ve düğme
  renksiz kalır — kaynak testi bunu göremez.
*/

const palet = tailwindConfig.theme.extend.colors.brand

test.beforeEach(async ({ page }) => {
  await apiyiTaklitEt(page)
})

test.describe('Ürün adı (F4)', () => {
  test('sayfa başlığı dersmate', async ({ page }) => {
    await page.goto('/giris')
    await expect(page).toHaveTitle(/^dersmate\b/)
  })

  test('kullanıcıya görünen hiçbir metinde PeerLearn geçmiyor', async ({ page }) => {
    /*
      Kod tabanındaki PeerLearn adı bilinçli olarak duruyor (namespace, JWT Issuer,
      localStorage anahtarları) — bunların hiçbiri EKRANDA görünmez. Bu test tam da bu
      ayrımı denetliyor: görünen katmanda sıfır tolerans.
    */
    for (const yol of ['/giris', '/kayit', '/dogrula']) {
      await page.goto(yol)
      await expect(page.locator('body')).toBeVisible()
      const metin = await page.locator('body').innerText()
      expect(metin, `${yol} sayfasında PeerLearn görünüyor`).not.toMatch(/peerlearn/i)
    }
  })
})

test.describe('Marka rengi (F1b)', () => {
  test('birincil düğme brand-600 zemininde render ediliyor', async ({ page }) => {
    await page.goto('/giris')

    const dugme = page.getByRole('button', { name: 'Giriş yap' })
    await expect(dugme).toBeVisible()

    const zemin = await dugme.evaluate((el) => getComputedStyle(el).backgroundColor)
    expect(rgbAyristir(zemin)).toEqual(hexAyristir(palet[600]))
  })

  test('birincil düğmedeki yazı WCAG AA eşiğini geçiyor', async ({ page }) => {
    /*
      PAKETTEKİ EN ÖNEMLİ TEST.

      Marka tonu #38BDF8 ve bir gün birinin "marka rengi buysa düğme de bu olsun" demesi
      çok doğal. Ama beyaz yazıyla kontrastı 2.14:1 — okunmaz. Aynı şekilde stok Tailwind
      sky-600 (#0284c7) 4.10:1 verir ve yine eşiğin altındadır.

      Bu yüzden test rengi SABİTE karşı değil, EŞİĞE karşı ölçüyor: paleti değiştirmek
      serbest, erişilemez hâle getirmek değil.
    */
    await page.goto('/giris')

    const dugme = page.getByRole('button', { name: 'Giriş yap' })
    const { zemin, yazi } = await dugme.evaluate((el) => {
      const s = getComputedStyle(el)
      return { zemin: s.backgroundColor, yazi: s.color }
    })

    const oran = kontrastOrani(rgbAyristir(zemin), rgbAyristir(yazi))
    expect(
      oran,
      `Birincil düğme kontrastı ${oran.toFixed(2)}:1 — WCAG AA için en az 4.5:1 gerekli.`,
    ).toBeGreaterThanOrEqual(4.5)
  })

  test('hover tonu (brand-700) da AA eşiğinde ve 600 den koyu', async ({ page }) => {
    await page.goto('/giris')

    const beyaz = [255, 255, 255]
    const alti = kontrastOrani(hexAyristir(palet[600]), beyaz)
    const yedi = kontrastOrani(hexAyristir(palet[700]), beyaz)

    expect(yedi, 'brand-700 beyaz yazıyla AA eşiğinin altında').toBeGreaterThanOrEqual(4.5)
    expect(yedi, 'hover tonu 600 den koyu değil — hover görünmez').toBeGreaterThan(alti)
  })

  test('marka rozeti (brand-700 / brand-100) AA eşiğinde', async ({ page }) => {
    await page.goto('/giris')
    const oran = kontrastOrani(hexAyristir(palet[700]), hexAyristir(palet[100]))
    expect(oran).toBeGreaterThanOrEqual(4.5)
  })

  test('logo vurgusu sayfada brand-500 olarak çiziliyor', async ({ page }) => {
    /*
      500, 400 DEĞİL. Marka tonu #0088CC gövde rengi olarak AA'yı kaçırıyor (beyaz yazıyla
      3.89:1) ve bu yüzden F1b'de kimlik 500 basamağında bırakılıp zemin görevi 600'e
      taşındı — logo ve odak kenarlığı 500'ü, düğmeler 600'ü kullanır.
      Test o kararı sabitliyor: logo bir gün 400'e kayarsa marka tonu ile arayüz tonu
      birbirinden ayrılır ve kimse fark etmez. (bkz. docs/DEVAM-EDILECEK.md, F1b)
    */
    await page.goto('/giris')

    const logo = page.locator('svg[aria-label="dersmate"]').first()
    await expect(logo).toBeVisible()

    // Vurgu dairesi: logodaki iki düğümden marka renkli olanı.
    const doluluklar = await logo.locator('circle').evaluateAll((el) =>
      el.map((c) => c.getAttribute('fill')?.toLowerCase()),
    )
    expect(doluluklar, 'logoda brand-500 tonunda daire yok').toContain(palet[500].toLowerCase())
  })

  test('arayüzde eski indigo tonlarından hiçbiri kalmadı', async ({ page }) => {
    /*
      Derlenmiş CSS'e karşı süpürge: bir bileşende `bg-indigo-600` gibi doğrudan Tailwind
      sınıfı kalmışsa palet değişikliği onu atlamış demektir ve ekranda iki farklı mavi
      belirir. Kaynak taraması bunu yakalayamaz — orada yazan şey bir hex değil, bir sınıf.
    */
    await page.goto('/giris')

    const eskiTonlar = ['79, 70, 229', '67, 56, 202', '238, 242, 255', '224, 231, 255']
    const kullanilan = await page.evaluate(() =>
      [...document.querySelectorAll('*')].flatMap((el) => {
        const s = getComputedStyle(el)
        return [s.backgroundColor, s.color, s.borderColor]
      }),
    )

    for (const ton of eskiTonlar) {
      expect(kullanilan.some((d) => d.includes(ton)), `eski indigo tonu rgb(${ton}) hâlâ ekranda`).toBe(
        false,
      )
    }
  })
})
