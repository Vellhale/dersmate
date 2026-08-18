/*
  ARAYÜZ ÖNİZLEME SUNUCUSU — backend olmadan siteyi açmak için.

      npm run onizleme        →  http://localhost:4173

  NE İŞE YARAR: .NET, PostgreSQL, Redis ve Docker kurmadan arayüzü gerçek hâliyle
  gezmek. Tasarım gözden geçirirken ya da birine ekranı gösterirken tüm yığını ayağa
  kaldırmak gereksiz bir engel.

  ⚠️ VERİ SAHTEDİR. Bu sunucu hiçbir şey doğrulamaz, hiçbir şey kaydetmez: giriş
  ekranında HERHANGİ bir e-posta/şifre kabul edilir ve sabit bir demo profili döner.
  Üretimle ya da testle hiçbir ilgisi yok — yalnızca göz kararı bakmak içindir.
  Gerçek akışları sınamak için `docs/GELISTIRME-ORTAMI.md`'deki tam kurulumu izle.

  NEDEN vite dev DEĞİL: `npm run dev` de arayüzü açar ama her istek gerçek API'ye
  gider ve backend yoksa her sayfa hata kutusuyla gelir. Burada API taklit edildiği
  için giriş, profil, rozetler ve yorumlar dolu görünür.

  Bağımlılık yok: yalnızca Node'un kendi modülleri.

  DERLEMEYİ DE BU DOSYA YAPAR (aşağıdaki derle()). Ayrı bir `.env.onizleme` dosyası
  kullanmıyoruz: derleme, VITE_API_URL boş olacak şekilde alt süreçte koşuyor. Bunun iki
  sebebi var — (1) `VITE_API_URL= vite build` sözdizimi Windows kabuklarında çalışmaz,
  (2) depoda, yanlışlıkla `npm run build` ile karışabilecek ikinci bir ortam dosyası
  bırakmamak daha temiz.

  VITE_API_URL NEDEN BOŞ: api.js'te `API_BASE = import.meta.env.VITE_API_URL ?? '...'`.
  Boş dize nullish DEĞİL, dolayısıyla varsayılana düşmez ve API_BASE boş kalır — istekler
  `/api/...` olarak sayfanın kendi kökenine, yani bu sunucuya gider.
*/

import { spawn } from 'node:child_process'
import { createServer } from 'node:http'
import { readFile, stat } from 'node:fs/promises'
import { join, extname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const KOK = resolve(fileURLToPath(import.meta.url), '..')
const DIST = join(KOK, 'dist')
const PORT = Number(process.env.PORT ?? 4173)

const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml', '.json': 'application/json',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon', '.woff2': 'font/woff2',
}

import { apiYanit, BOS_SAYFA } from './onizleme-veri.mjs'

// --- Sunucu -----------------------------------------------------------------

async function dosyaGonder(res, yol) {
  const govde = await readFile(yol)
  res.writeHead(200, { 'Content-Type': MIME[extname(yol)] ?? 'application/octet-stream' })
  res.end(govde)
}

const sunucu = createServer(async (req, res) => {
  const yol = new URL(req.url, 'http://localhost').pathname

  // SignalR (/hubs/...) taklit EDİLMİYOR: sohbet gerçek zamanlı bir bağlantı ister ve
  // önizlemenin kapsamı dışında. index.html'e düşmesin diye açıkça 404 dönüyoruz —
  // aksi halde istemci HTML'i JSON sanıp konsolu ayrıştırma hatasıyla dolduruyor.
  if (yol.startsWith('/hubs/')) {
    res.writeHead(404, { 'Content-Type': 'text/plain' })
    return res.end('onizlemede sohbet yok')
  }

  // Avatar bir GÖRSEL ucu. JSON dönmek <img>'i bozuk resme çevirir; 404 dönünce
  // Avatar bileşeni baş harf rozetine düşüyor — önizleme için doğrusu bu.
  if (/^\/api\/users\/[^/]+\/avatar$/.test(yol)) {
    res.writeHead(404); return res.end()
  }

  if (yol.startsWith('/api/')) {
    // Gövdeyi tüket: POST'ta okumazsak bağlantı asılı kalabiliyor.
    for await (const _ of req) { /* yut */ }
    const yanit = apiYanit(req.method, yol)
    if (yanit === undefined) {
      // Tanımsız uç: konsola yaz ki önizleme genişletilirken hangi ucun eksik olduğu
      // tahmin edilmesin, görülsün.
      console.log(`  [tanimsiz uc] ${req.method} ${yol}`)
    }
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' })
    return res.end(JSON.stringify(yanit ?? BOS_SAYFA))
  }

  try {
    const aday = join(DIST, yol === '/' ? 'index.html' : yol.slice(1))
    if ((await stat(aday)).isFile()) return await dosyaGonder(res, aday)
  } catch { /* dosya yok → SPA yedeğine düş */ }

  // SPA yedeği: /profil, /kesfet gibi istemci rotaları index.html'e düşer.
  try {
    return await dosyaGonder(res, join(DIST, 'index.html'))
  } catch {
    res.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' })
    res.end('dist/ bulunamadı. Önce `npm run build` çalıştır.')
  }
})

/** Arayüzü, API adresi boş olacak şekilde derler. */
function derle() {
  return new Promise((tamam, hata) => {
    console.log('  arayüz derleniyor…')
    // shell: true — Windows'ta `npx` aslında npx.cmd; kabuk olmadan bulunamıyor.
    const p = spawn('npx', ['vite', 'build'], {
      cwd: KOK, shell: true, stdio: 'inherit',
      env: { ...process.env, VITE_API_URL: '', VITE_GA4_MEASUREMENT_ID: '' },
    })
    p.on('exit', (kod) => (kod === 0 ? tamam() : hata(new Error(`vite build çıkış kodu ${kod}`))))
    p.on('error', hata)
  })
}

if (!process.env.ONIZLEME_DERLEME_ATLA) {
  await derle()
}

sunucu.listen(PORT, () => {
  console.log(`\n  dersmate — ARAYÜZ ÖNİZLEMESİ (sahte veri)\n`)
  console.log(`  →  http://localhost:${PORT}\n`)
  console.log(`  Giriş ekranında herhangi bir e-posta ve şifre kabul edilir.`)
  console.log(`  Profili görmek için giriş yaptıktan sonra sağ üstten "Profilim".`)
  console.log(`  Durdurmak için Ctrl+C.\n`)
})
