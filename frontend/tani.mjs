import { chromium } from 'playwright'
const KOK = 'http://localhost:5173', API = 'http://localhost:5000'
const bekle = (ms) => new Promise((r) => setTimeout(r, ms))
const y = await fetch(`${API}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'ayse@demo.dev', password: 'Demo12345', hwidHash: 'demo-hwid-abdullah' }) })
const oturum = await y.json()
const liste = await (await fetch(`${API}/api/conversations`, { headers: { Authorization: `Bearer ${oturum.accessToken}` } })).json()
const dizi = Array.isArray(liste) ? liste : (liste.items ?? [])
// Aysenin yanit verdigi bir sohbet bul (iki tarafli gorunsun)
const t = await chromium.launch({ headless: false, slowMo: 150 })
const s = await (await t.newContext({ viewport: { width: 1360, height: 900 }, locale: 'tr-TR' })).newPage()
await s.goto(KOK); await s.evaluate((o) => localStorage.setItem('peerlearn.session', JSON.stringify(o)), oturum)
for (const [ad, sohbet] of [['26-konusma', dizi[0]], ['27-konusma-yanitli', dizi.find(c => c.otherDisplayName === 'Ege Karaca') ?? dizi[3]]]) {
  await s.goto(`${KOK}/sohbet/${sohbet.conversationId}`)
  await s.waitForLoadState('networkidle').catch(() => {})
  await bekle(2500)
  await s.screenshot({ path: `gezinti-goruntuleri/${ad}.png` })
  console.log(' ', ad + '.png', '->', sohbet.otherDisplayName)
}
await bekle(3000); await t.close()
