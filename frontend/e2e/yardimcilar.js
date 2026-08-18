import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const buradan = dirname(fileURLToPath(import.meta.url))

/** Depo kökündeki bir yolu (frontend/ göreli) mutlak yola çevirir. */
export function frontendYolu(...parcalar) {
  return resolve(buradan, '..', ...parcalar)
}

export function kaynakOku(...parcalar) {
  return readFileSync(frontendYolu(...parcalar), 'utf8')
}

/**
 * Verilen klasör altındaki tüm .js/.jsx dosyalarını frontend/ göreli yol olarak döndürür.
 * Yeni bir dosya eklendiğinde denetimlere elle kaydedilmesi gerekmesin diye taranıyor —
 * elle tutulan bir liste, gözden kaçan ilk dosyada sessizce delinirdi.
 */
export function kaynakDosyalari(kok) {
  const cikti = []
  const gez = (goreli) => {
    for (const girdi of readdirSync(frontendYolu(goreli), { withFileTypes: true })) {
      const yol = `${goreli}/${girdi.name}`
      if (girdi.isDirectory()) gez(yol)
      else if (/\.jsx?$/.test(girdi.name)) cikti.push(yol)
    }
  }
  gez(kok)
  return cikti.sort()
}

/**
 * Backend'e giden HER isteği keser.
 *
 * NEDEN VARSAYILAN OLARAK 401: uygulamanın oturumsuz hâli test edilirken API'nin
 * "kimsin?" cevabı budur. Testin ilgilendiği uç varsa `yanitlar` ile üzerine yazılır:
 *   apiyiTaklitEt(page, { 'POST /api/auth/login': { status: 200, json: {...} } })
 *
 * Anahtar biçimi: "METOT /yol" — yol `startsWith` ile eşleşir (sorgu dizesi serbest).
 */
export async function apiyiTaklitEt(page, yanitlar = {}) {
  await page.route('**/api/**', async (route) => {
    const istek = route.request()
    const yol = new URL(istek.url()).pathname

    const eslesen = Object.entries(yanitlar).find(([k]) => {
      const [metot, onEk] = k.split(' ')
      return istek.method() === metot && yol.startsWith(onEk)
    })

    if (eslesen) {
      const { status = 200, json = {} } = eslesen[1]
      return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(json) })
    }

    // Taklit edilmemiş uç: ProblemDetails biçiminde 401 (backend'in gerçek biçimi).
    return route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ status: 401, title: 'UNAUTHORIZED', detail: 'Oturum yok.' }),
    })
  })
}

/** "rgb(6, 118, 168)" → [6, 118, 168] */
export function rgbAyristir(deger) {
  const m = deger.match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)/)
  if (!m) throw new Error(`rgb ayrıştırılamadı: ${deger}`)
  return [Number(m[1]), Number(m[2]), Number(m[3])]
}

/** "#38BDF8" → [56, 189, 248] */
export function hexAyristir(deger) {
  const h = deger.replace('#', '').trim()
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16))
}

/** WCAG 2.1 bağıl parlaklık. */
function parlaklik([r, g, b]) {
  const [R, G, B] = [r, g, b]
    .map((v) => v / 255)
    .map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
  return 0.2126 * R + 0.7152 * G + 0.0722 * B
}

/** WCAG 2.1 kontrast oranı (1..21). */
export function kontrastOrani(rgb1, rgb2) {
  const [a, b] = [parlaklik(rgb1), parlaklik(rgb2)].sort((x, y) => y - x)
  return (a + 0.05) / (b + 0.05)
}
