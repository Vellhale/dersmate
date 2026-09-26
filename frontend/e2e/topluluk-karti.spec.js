import { test, expect } from '@playwright/test'
import { apiyiTaklitEt } from './yardimcilar.js'

/*
  TOPLULUK KARTI — iki sessiz kural (2026-09-27):

  1. İlk yorum önizlemesinin altındaki satır, yorumun GERİ KALANINA giden yol. n ≥ 2 iken
     "{n} yorumun tümünü gör"; n = 1 iken yalnızca önizleme KESİKSE "Yorumun tamamını gör".
     Kesik iki yoldan olur ve ikisi de ayrı sınanıyor: satır sınırı (line-clamp-2, ölçülüyor)
     ve sunucunun 200 grafem sınırı ("…" ile biten gövde; geniş kartta iki satıra sığabilir).
     Satır eskiden yalnızca n ≥ 2 iken çiziliyordu ve uzun tek yorumun devamına kartta yol
     yoktu (GonderiKarti.jsx → YorumOnizlemesi).

  2. İplikteki yazar adı lg altında 44px dokunma hedefi ve bu alanın TAMAMI bağlantıya düşüyor
     (elementFromPoint): kendi yorumunda şikayet düğmesi olmadığı için satır kısa kalıyor ve
     adın alt dolgusunu gövde metni örtüyordu. Kutu ölçümü tek başına yetmez; kutu 44 olup
     etkin alan 36 kalabiliyordu (GonderiKarti.jsx → YazarSatiri, YorumListesi).

  MUTASYONLA DOĞRULANDI: sunucuKestiMi, satır ölçümü, adın -my-3.5 py-3.5'i ve iplik
  satırının min-h-11'i ayrı ayrı kaldırıldığında ilgili test kırılıyor.
*/

const BEN = '11111111-1111-1111-1111-111111111111'
const kisi = (userId, displayName, level = 2) => ({ userId, displayName, level, isStaff: false })
const MERT = kisi('33333333-3333-3333-3333-333333333333', 'Mert Demir')
const AYSE = kisi('22222222-2222-2222-2222-222222222222', 'Ayşe Kaya', 4)
const BENIM = kisi(BEN, 'Deniz Yılmaz', 3)
const simdi = new Date().toISOString()

/* 200 grafemin altında ama dar ekranda iki satırı aşan, sunucunun KESMEDİĞİ gövde. */
const UZUN_TAM =
  'Ben de aynı durumdaydım; paragraf sorularında önce soru kökünü okuyup sonra metne dönmek ' +
  'bana çok zaman kazandırdı, iki üç haftada fark hissedersin.'
/* Sunucunun kestiği biçim (199 grafem + "…") ama dar harflerle: geniş kartta iki satıra sığar. */
const SUNUCU_KESTI = `${'ı '.repeat(100).trimEnd()}…`

const gonderi = (postId, title, commentCount, firstComment, author = AYSE) => ({
  postId,
  tag: 'Question',
  title,
  body: 'Gövde metni.',
  author,
  createdAtUtc: simdi,
  upvoteCount: 1,
  downvoteCount: 0,
  commentCount,
  myVote: 0,
  underReview: false,
  reportCount: 0,
  firstComment,
})
const onizleme = (commentId, body, author = MERT) => ({ commentId, body, author, createdAtUtc: simdi })
const yorum = (commentId, body, author) => ({
  commentId,
  body,
  author,
  createdAtUtc: simdi,
  upvoteCount: 0,
  downvoteCount: 0,
  myVote: 0,
  underReview: false,
})

const GONDERILER = [
  gonderi('p1', 'Tek uzun yorum', 1, onizleme('c1', UZUN_TAM)),
  gonderi('p2', 'Tek kısa yorum', 1, onizleme('c2', 'Teşekkürler, işe yaradı.')),
  gonderi('p3', 'Kendi üç noktası', 1, onizleme('c3', 'Bence de öyle…')),
  gonderi('p4', 'Sunucu kesti', 1, onizleme('c4', SUNUCU_KESTI)),
  gonderi('p5', 'İki yorum', 2, onizleme('c5', 'Kendi yorumum.', BENIM), BENIM),
]

async function toplulugaGir(page, genislik) {
  await page.setViewportSize({ width: genislik, height: 900 })
  await page.addInitScript((ben) => {
    localStorage.setItem(
      'peerlearn.session',
      JSON.stringify({ accessToken: 'e2e', refreshToken: 'e2e-r', userId: ben, displayName: 'Deniz Yılmaz', isAdmin: false }),
    )
    localStorage.setItem(
      'peerlearn.consent',
      JSON.stringify({ analytics: false, functional: false, version: '2026-09-19', updatedAt: new Date().toISOString() }),
    )
  }, BEN)
  await apiyiTaklitEt(page, {
    'GET /api/wallet': { json: { totalEarnedCredits: 0, currentBalance: 0, level: 1, levelMinCredits: 0, nextLevelAt: 100, activeLots: [] } },
    'GET /api/conversations': { json: [] },
    'GET /api/preferences': {
      json: { analyticsConsent: 'Denied', functionalConsent: 'Denied', consentVersion: '2026-09-19', onboardingCompleted: true, onboardingSuppressed: false, onboardingLastStep: 0 },
    },
    'GET /api/community/posts/p5/comments': {
      json: [yorum('c5', 'Kendi yorumum.', BENIM), yorum('c5b', 'Kısa bir yanıt.', MERT)],
    },
    'GET /api/community/posts/p1/comments': { json: [yorum('c1', UZUN_TAM, MERT)] },
    'GET /api/community/posts': {
      json: { items: GONDERILER, page: 1, pageSize: 20, totalPages: 1, totalCount: GONDERILER.length, hasNextPage: false },
    },
  })
  await page.goto('/topluluk')
  await expect(page.getByRole('heading', { name: 'Tek uzun yorum' })).toBeVisible()
}

/** Başlığıyla bulunan kart. İç içe cam yüzey olursa en içteki (belge sırasında sonuncu). */
const kart = (page, baslik) =>
  page.locator('div.rounded-2xl', { has: page.getByRole('heading', { name: baslik, exact: true }) }).last()

test.describe('Topluluk kartı: ilk yorum önizlemesi', () => {
  test('tek yorum satır sınırında kesiliyorsa "Yorumun tamamını gör" çıkar ve ipliği açar', async ({ page }) => {
    await toplulugaGir(page, 375)
    const k = kart(page, 'Tek uzun yorum')
    const dugme = k.getByRole('button', { name: 'Yorumun tamamını gör' })
    await expect(dugme).toBeVisible()
    expect((await dugme.boundingBox()).height).toBeGreaterThanOrEqual(44)

    await dugme.click()
    await expect(k.getByText('iki üç haftada fark hissedersin.')).toBeVisible()
    // Önizleme ve düğmesi kalkınca odak yorum düğmesine taşınıyor.
    await expect(page.locator(':focus')).toHaveAttribute('aria-label', 'Yorumlar — 1 yorum')
  })

  test('tek yorum sığıyorsa satır yok; yazarın kendi üç noktası kesilme sayılmaz', async ({ page }) => {
    await toplulugaGir(page, 375)
    await expect(kart(page, 'Tek kısa yorum').getByText('Teşekkürler, işe yaradı.')).toBeVisible()
    await expect(kart(page, 'Tek kısa yorum').getByRole('button', { name: /gör$/ })).toHaveCount(0)
    await expect(kart(page, 'Kendi üç noktası').getByRole('button', { name: /gör$/ })).toHaveCount(0)
  })

  test('sunucunun kestiği gövde iki satıra sığsa da satır alır', async ({ page }) => {
    await toplulugaGir(page, 1280)
    const k = kart(page, 'Sunucu kesti')
    // Ön koşul: bu genişlikte satır sınırı DEVREDE DEĞİL; yoksa test ölçüm yolunu sınardı.
    const kirpik = await k.locator('p.line-clamp-2').evaluate((p) => p.scrollHeight > p.clientHeight + 1)
    expect(kirpik, 'gövde 1280px te de satır sınırına takılıyor — test verisi daraltılmalı').toBe(false)
    await expect(k.getByRole('button', { name: 'Yorumun tamamını gör' })).toBeVisible()
  })

  test('iki ve daha fazla yorumda sayılı satır', async ({ page }) => {
    await toplulugaGir(page, 375)
    await expect(kart(page, 'İki yorum').getByRole('button', { name: '2 yorumun tümünü gör' })).toBeVisible()
  })
})

test.describe('Topluluk kartı: iplikte yazar adı', () => {
  test('lg altında ad 44px ve alanın tamamı bağlantıya düşüyor (kendi yorumu dahil)', async ({ page }) => {
    await toplulugaGir(page, 375)
    const k = kart(page, 'İki yorum')
    await k.getByRole('button', { name: 'Yorumlar — 2 yorum' }).click()
    await expect(k.getByText('Kısa bir yanıt.')).toBeVisible()

    const olcumler = await k.locator('ul li').evaluateAll((liler) =>
      liler.map((li) => {
        // Avatarın bağlantısı klavyeden gizli (tabIndex -1); ölçülen ad bağlantısı.
        const a = [...li.querySelectorAll('a')].find((x) => x.tabIndex >= 0)
        a.scrollIntoView({ block: 'center' })
        const r = a.getBoundingClientRect()
        const x = r.left + Math.min(10, r.width / 2)
        const dusen = (y) => {
          const e = document.elementFromPoint(x, y)
          return e === a || a.contains(e)
        }
        return { ad: a.textContent.trim(), yukseklik: r.height, ust: dusen(r.top + 2), alt: dusen(r.bottom - 2) }
      }),
    )

    expect(olcumler.map((o) => o.ad)).toEqual(['Deniz Yılmaz', 'Mert Demir'])
    for (const o of olcumler) {
      expect(o.yukseklik, `${o.ad} kutusu`).toBeGreaterThanOrEqual(44)
      expect(o.ust, `${o.ad} üst kenarı başka öğeye düşüyor`).toBe(true)
      expect(o.alt, `${o.ad} alt kenarı başka öğeye düşüyor`).toBe(true)
    }
  })
})
