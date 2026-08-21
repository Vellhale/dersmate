import { chromium } from 'playwright'
const KOK = 'http://localhost:5173', API = 'http://localhost:5000'
const bekle = (ms) => new Promise((r) => setTimeout(r, ms))
const y = await fetch(`${API}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'ayse@demo.dev', password: 'Demo12345', hwidHash: 'demo-hwid-abdullah' }) })
const oturum = await y.json()
const t = await chromium.launch({ headless: true })
const s = await (await t.newContext({ viewport: { width: 1360, height: 900 }, locale: 'tr-TR' })).newPage()
await s.goto(KOK); await s.evaluate((o) => localStorage.setItem('peerlearn.session', JSON.stringify(o)), oturum)

async function olc(ad, g, yk, ekran) {
  await s.setViewportSize({ width: g, height: yk })
  await s.goto(`${KOK}/sohbet`); await s.waitForLoadState('networkidle').catch(() => {})
  const z = s.getByRole('button', { name: /Yalnızca zorunlu/i }); if (await z.count()) await z.first().click().catch(() => {})
  await bekle(1500)
  const o = await s.evaluate(() => {
    const d = document.documentElement
    const l = [...document.querySelectorAll('aside')].find((a) => a.querySelectorAll('button').length > 3)
    return { fazla: d.scrollHeight - d.clientHeight, pencere: l ? Math.round(l.clientHeight) : 0, icerik: l ? Math.round(l.scrollHeight) : 0 }
  })
  console.log(` ${ad.padEnd(14)} sayfa fazlasi: ${String(o.fazla).padStart(3)}px | liste ${o.pencere}px pencere / ${o.icerik}px icerik -> ${o.icerik > o.pencere ? 'liste kayiyor' : 'kaymiyor'}`)
  // Listeyi kaydir: sayfa yine kaymamali
  await s.evaluate(() => { const l = [...document.querySelectorAll('aside')].find(a => a.querySelectorAll('button').length > 3); l.scrollTop = 1500 })
  await bekle(600)
  const sonra = await s.evaluate(() => { const d = document.documentElement; const l = [...document.querySelectorAll('aside')].find(a => a.querySelectorAll('button').length > 3); return { sayfa: d.scrollTop, liste: Math.round(l.scrollTop) } })
  console.log(`                kaydirma sonrasi -> liste scrollTop: ${sonra.liste}px, SAYFA scrollTop: ${sonra.sayfa}px`)
  if (ekran) await s.screenshot({ path: `gezinti-goruntuleri/${ekran}.png` })
}
await olc('masaustu', 1360, 900, '31-liste-kaydirildi')
await olc('dizustu-kisa', 1360, 700, null)
await olc('mobil', 390, 844, '32-mobil-kaydirildi')
await t.close()
