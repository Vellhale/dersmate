/*
 * SignalR backplane testi — ÇOK INSTANCE'LI kurulumun asıl sınavı.
 *
 * Senaryo: istemci 5001'e bağlanır, mesaj 5000 üzerinden gönderilir.
 *   - Backplane YOKSA: 5000'in yayını yalnızca kendi belleğindeki bağlantılara gider,
 *     5001'e bağlı istemci mesajı ASLA almaz (sohbet instance'a göre bölünür).
 *   - Backplane VARSA (Redis): yayın Redis üzerinden 5001'e taşınır ve istemci alır.
 *
 * Çalıştırma:  node tools/signalr-backplane-probe.js
 * Gereksinim:  demo hesapları (tools/seed-demo.ps1) ve iki API instance'ı (5000 + 5001).
 */

const path = require('path')

// SignalR istemcisi frontend bağımlılıklarından ödünç alınır (ayrı kurulum gerekmesin).
const signalRPath = path.join(
  __dirname, '..', 'frontend', 'node_modules', '@microsoft', 'signalr',
)
const signalR = require(signalRPath)

const A = 'http://localhost:5000' // mesajın GÖNDERİLECEĞİ instance
const B = 'http://localhost:5001' // istemcinin BAĞLANACAĞI instance
const TIMEOUT_MS = 10000

async function login(email) {
  const res = await fetch(`${A}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password: 'Demo12345', hwidHash: 'z'.repeat(64) }),
  })
  if (!res.ok) throw new Error(`giris basarisiz (${email}): ${res.status} ${await res.text()}`)
  return res.json()
}

async function main() {
  for (const [name, base] of [['5000', A], ['5001', B]]) {
    const r = await fetch(`${base}/health`).catch(() => null)
    if (!r || !r.ok) throw new Error(`${name} portundaki instance ayakta degil`)
  }

  const ayse = await login('ayse@demo.dev')
  const berk = await login('berk@demo.dev')

  const convRes = await fetch(`${A}/api/conversations`, {
    headers: { Authorization: `Bearer ${ayse.accessToken}` },
  })
  const conversations = await convRes.json()
  if (!conversations.length) throw new Error('sohbet yok — once tools/seed-demo.ps1 calistirin')
  const conversationId = conversations[0].conversationId

  // Ayşe 5001'e bağlanır.
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${B}/hubs/chat`, { accessTokenFactory: () => ayse.accessToken })
    .configureLogging(signalR.LogLevel.Error)
    .build()

  const received = new Promise((resolve) => {
    connection.on('ReceiveMessage', (message) => resolve(message))
  })

  await connection.start()
  await connection.invoke('JoinConversation', conversationId)
  console.log(`istemci ${B} uzerinden sohbete katildi`)

  // Mesaj DİĞER instance üzerinden gönderilir.
  const text = `backplane testi ${new Date().toISOString()}`
  const sendRes = await fetch(`${A}/api/conversations/${conversationId}/messages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${berk.accessToken}` },
    body: JSON.stringify({ content: text }),
  })
  if (!sendRes.ok) throw new Error(`mesaj gonderilemedi: ${sendRes.status} ${await sendRes.text()}`)
  console.log(`mesaj ${A} uzerinden gonderildi`)

  const timeout = new Promise((resolve) => setTimeout(() => resolve(null), TIMEOUT_MS))
  const message = await Promise.race([received, timeout])

  await connection.stop()

  if (message && message.content === text) {
    console.log('\n[OK] BACKPLANE CALISIYOR — 5000 uzerinden gonderilen mesaj 5001 istemcisine ulasti')
    process.exit(0)
  } else {
    console.log(`\n[HATA] mesaj ${TIMEOUT_MS} ms icinde ULASMADI — backplane yok, sohbet instance'lara bolunuyor`)
    process.exit(1)
  }
}

main().catch((err) => {
  console.error(`\n[HATA] ${err.message}`)
  process.exit(2)
})
