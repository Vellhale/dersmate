/*
  TEK DOSYALIK ARAYÜZ ÖNİZLEMESİ ÜRETİCİSİ.

      node onizleme-tekdosya.mjs          →  dersmate-onizleme.html

  Ürettiği dosya ÇİFT TIKLAYINCA açılır: sunucu yok, komut yok, kurulum yok. Tarayıcıya
  atman yeterli. Tasarımı birine göstermek ya da telefondan/başka bir makineden bakmak
  için `npm run onizleme`'den daha az sürtünmeli.

  NASIL: Vite ile normal bir üretim derlemesi yapılır, sonra CSS ve JS tek bir HTML'in
  içine GÖMÜLÜR. Ayrıca uygulamadan ÖNCE çalışan bir katman enjekte edilir; bu katman
  `fetch`'i sarmalayıp `/api/...` isteklerini `onizleme-veri.mjs`'teki sahte yanıtlarla
  karşılar. Yani ağa hiç çıkılmaz.

  ÜÇ ŞEY file:// ORTAMI İÇİN ÖZEL OLARAK ELE ALINIYOR — üçü de olmadan sayfa açılmıyor:

    1. YÖNLENDİRME. Uygulama BrowserRouter kullanıyor; file:// üzerinde `pushState`
       güvenlik hatası veriyor. Bu yüzden derleme, HashRouter kullanan geçici bir giriş
       dosyasıyla yapılıyor (adresler /giris yerine #/giris olur).
    2. localStorage. Chrome, file:// kökenini opak sayıp erişimi reddedebiliyor; oturum
       ve çerez tercihi orada tutulduğu için uygulama açılışta patlıyordu. Bellek içi bir
       yedek konuluyor.
    3. GÖRECELİ YOLLAR. `base: './'` — mutlak `/assets/...` yolları file:// altında
       diskin kökünü işaret eder.

  ⚠️ VERİ SAHTEDİR. Doğrulama yok, kayıt yok. Bu bir test aracı değil, bakma aracı.
     Sohbet (SignalR) ve avatar görselleri taklit edilmiyor.

  Geçici dosyalar `.onizleme-gecici/` altında üretilir ve iş bitince silinir; depoya
  hiçbir kalıntı bırakmaz.
*/

import { spawn } from 'node:child_process'
import { readFile, writeFile, mkdir, rm, readdir } from 'node:fs/promises'
import { join, resolve, extname } from 'node:path'
import { fileURLToPath } from 'node:url'

const KOK = resolve(fileURLToPath(import.meta.url), '..')
const GECICI = join(KOK, '.onizleme-gecici')
const CIKTI = join(KOK, 'dersmate-onizleme.html')

function calistir(komut, argumanlar, env = {}) {
  return new Promise((tamam, hata) => {
    // shell: true — Windows'ta `npx` aslında npx.cmd; kabuk olmadan bulunamıyor.
    const p = spawn(komut, argumanlar, { cwd: KOK, shell: true, stdio: 'inherit', env: { ...process.env, ...env } })
    p.on('exit', (k) => (k === 0 ? tamam() : hata(new Error(`${komut} çıkış kodu ${k}`))))
    p.on('error', hata)
  })
}

async function main() {
  await rm(GECICI, { recursive: true, force: true })
  await mkdir(GECICI, { recursive: true })

  /*
    GEÇİCİ GİRİŞ NOKTASI — tek farkı HashRouter.

    src/main.jsx'i kopyalayıp BrowserRouter'ı değiştirmek yerine yeniden yazıyoruz ki
    asıl dosya değişmesin ve iki sürüm birbirinden habersiz ayrışmasın. Buradaki ağaç
    main.jsx ile AYNI sırada olmalı: ConsentProvider, AuthProvider'ın İÇİNDE (oturumu
    görmesi gerekiyor) ve CookieBanner router'ın DIŞINDA.
  */
  const girisJsx = `
import React from 'react'
import ReactDOM from 'react-dom/client'
import { HashRouter } from 'react-router-dom'
import App from '../src/App'
import { AuthProvider } from '../src/state/AuthContext'
import { ConsentProvider } from '../src/state/ConsentContext'
import { CookieBanner } from '../src/components/CookieBanner'
import '../src/index.css'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <HashRouter>
      <AuthProvider>
        <ConsentProvider>
          <App />
          <CookieBanner />
        </ConsentProvider>
      </AuthProvider>
    </HashRouter>
  </React.StrictMode>,
)
`
  // AnalyticsGate bilerek yok: GA4 ölçüm kimliği boş olduğu için zaten no-op, ama
  // file:// altında script enjeksiyonu deneyip konsolu kirletmesin.
  await writeFile(join(GECICI, 'giris.jsx'), girisJsx, 'utf8')
  await writeFile(join(GECICI, 'index.html'),
    `<!doctype html><html lang="tr"><head><meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>dersmate — arayüz önizlemesi</title></head>
<body><div id="root"></div><script type="module" src="./giris.jsx"></script></body></html>`, 'utf8')

  await writeFile(join(GECICI, 'vite.config.js'),
    `import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
export default defineConfig({
  plugins: [react()],
  root: '${GECICI.replace(/\\/g, '/')}',
  base: './',
  build: { outDir: '${join(GECICI, 'dist').replace(/\\/g, '/')}', emptyOutDir: true, assetsInlineLimit: 0 },
})`, 'utf8')

  console.log('  arayüz derleniyor…')
  await calistir('npx', ['vite', 'build', '--config', join(GECICI, 'vite.config.js')],
    { VITE_API_URL: '', VITE_GA4_MEASUREMENT_ID: '' })

  // --- Derlenen varlıkları tek HTML'e göm --------------------------------------
  const distDizin = join(GECICI, 'dist')
  const varliklar = await readdir(join(distDizin, 'assets'))
  let js = '', css = ''
  for (const ad of varliklar) {
    const icerik = await readFile(join(distDizin, 'assets', ad), 'utf8')
    if (extname(ad) === '.js') js += icerik
    if (extname(ad) === '.css') css += icerik
  }

  /*
    Sahte API katmanı: `onizleme-veri.mjs`'in METNİ olduğu gibi gömülüyor, yalnızca
    `export` sözcükleri atılıyor. Böylece sunucu ile tek-dosya sürümü AYNI veriyi ve
    AYNI eşleştirme mantığını kullanıyor — iki kopya tutulmuyor.
  */
  const veriKaynak = (await readFile(join(KOK, 'onizleme-veri.mjs'), 'utf8'))
    .replace(/^export \{[^}]*\}\s*$/m, '')
    .replace(/^export /gm, '')

  const kabuk = `
${veriKaynak}

/* --- file:// uyumu ------------------------------------------------------- */

// Chrome, file:// kökenini opak sayıp localStorage erişimini reddedebiliyor. Oturum ve
// çerez tercihi orada tutulduğu için erişim patladığında uygulama hiç açılmıyor.
try { window.localStorage.setItem('__onizleme', '1'); window.localStorage.removeItem('__onizleme') }
catch {
  const bellek = new Map()
  Object.defineProperty(window, 'localStorage', { value: {
    getItem: (k) => (bellek.has(k) ? bellek.get(k) : null),
    setItem: (k, v) => bellek.set(k, String(v)),
    removeItem: (k) => bellek.delete(k),
    clear: () => bellek.clear(),
  }})
}

// Tüm /api/... çağrıları buradan karşılanır; ağa hiç çıkılmaz.
const gercekFetch = window.fetch.bind(window)
window.fetch = async (girdi, ayar = {}) => {
  const url = typeof girdi === 'string' ? girdi : girdi.url

  // SignalR el sıkışması: file:// altında fetch'e hiç izin verilmiyor ve tarayıcı
  // "URL scheme file is not supported" diye konsolu dolduruyor. Temiz bir 404
  // dönmek, istemcinin sohbeti kapatıp susmasını sağlıyor.
  if (url.includes('/hubs/')) return new Response(null, { status: 404 })

  if (!url.includes('/api/')) return gercekFetch(girdi, ayar)

  const yol = url.slice(url.indexOf('/api/'))
  const metot = (ayar.method ?? (typeof girdi === 'object' ? girdi.method : 'GET') ?? 'GET').toUpperCase()

  // Avatar bir GÖRSEL ucu; JSON dönmek <img>'i bozuk resme çevirir. 404 dönünce
  // Avatar bileşeni baş harf rozetine düşüyor — önizleme için doğrusu bu.
  if (/\\/api\\/users\\/[^/]+\\/avatar$/.test(yol.split('?')[0])) {
    return new Response(null, { status: 404 })
  }

  const govde = apiYanit(metot, yol.split('?')[0]) ?? BOS_SAYFA
  return new Response(JSON.stringify(govde), {
    status: 200, headers: { 'Content-Type': 'application/json' },
  })
}

// SignalR taklit edilmiyor: WebSocket denemesi konsolu hata yağmuruna çeviriyordu.
// Hiç bağlanmayan bir yerine koyucu, sohbeti sessizce devre dışı bırakıyor.
window.WebSocket = class { constructor() { /* önizlemede sohbet yok */ } close() {} }
`

  const html = `<!doctype html>
<html lang="tr">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>dersmate — arayüz önizlemesi</title>
<!--
  TEK DOSYALIK ARAYÜZ ÖNİZLEMESİ — otomatik üretildi (frontend/onizleme-tekdosya.mjs).
  Elle düzenleme; kaynak değişince yeniden üret.
  VERİ SAHTEDİR: hiçbir doğrulama yapılmaz, hiçbir şey kaydedilmez, ağa çıkılmaz.
-->
<style>${css}</style>
</head>
<body>
<div id="root"></div>
<script>${kabuk}</script>
<script type="module">${js}</script>
</body>
</html>`

  await writeFile(CIKTI, html, 'utf8')
  await rm(GECICI, { recursive: true, force: true })

  const mb = (Buffer.byteLength(html) / 1024 / 1024).toFixed(2)
  console.log(`\n  ✓ ${CIKTI}  (${mb} MB)\n`)
  console.log('  Çift tıklayarak tarayıcıda aç. Sunucu ya da kurulum gerekmez.')
  console.log('  Giriş ekranında herhangi bir e-posta ve şifre kabul edilir.\n')
}

main().catch((e) => { console.error(e); process.exit(1) })
