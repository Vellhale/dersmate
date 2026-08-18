/*
 * PeerLearn yük / performans testi.
 *
 * Ölçtüğü: sık çağrılan okuma yollarının gecikme dağılımı (p50/p90/p95/p99), verim
 * (istek/sn) ve hata oranı — gerçekçi veri hacmi altında.
 *
 * Kullanım:
 *   node tools/loadtest.js                 # varsayılan: 16 eşzamanlı, senaryo başına 6 sn
 *   node tools/loadtest.js 32 10           # 32 eşzamanlı, 10 sn
 *
 * Gereksinim: API :5000, demo hesapları (tools/seed-demo.ps1).
 */

const API = process.env.PEERLEARN_API ?? 'http://localhost:5000'
const CONCURRENCY = Number(process.argv[2] ?? 16)
const DURATION_S = Number(process.argv[3] ?? 6)
const WARMUP_MS = 1500

async function login(email) {
  const res = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password: 'Demo12345', hwidHash: 'l'.repeat(64) }),
  })
  if (!res.ok) throw new Error(`giris basarisiz (${email}): ${res.status}`)
  return res.json()
}

function percentile(sorted, p) {
  if (!sorted.length) return 0
  const idx = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)
  return sorted[idx]
}

/** Sabit sayıda işçi, süre dolana kadar arka arkaya istek atar. */
async function runScenario(name, requestFn, { concurrency, durationMs }) {
  const latencies = []
  let errors = 0
  let bytes = 0
  const deadline = Date.now() + durationMs

  async function worker() {
    while (Date.now() < deadline) {
      const t0 = performance.now()
      try {
        const res = await requestFn()
        const body = await res.text()
        bytes += body.length
        if (!res.ok) errors++
      } catch {
        errors++
      }
      latencies.push(performance.now() - t0)
    }
  }

  const started = performance.now()
  await Promise.all(Array.from({ length: concurrency }, worker))
  const elapsedS = (performance.now() - started) / 1000

  latencies.sort((a, b) => a - b)
  return {
    name,
    count: latencies.length,
    rps: latencies.length / elapsedS,
    p50: percentile(latencies, 50),
    p90: percentile(latencies, 90),
    p95: percentile(latencies, 95),
    p99: percentile(latencies, 99),
    max: latencies[latencies.length - 1] ?? 0,
    errors,
    avgKb: latencies.length ? bytes / latencies.length / 1024 : 0,
  }
}

function fmt(n, d = 1) {
  return n.toFixed(d).padStart(7)
}

async function main() {
  const health = await fetch(`${API}/health`).catch(() => null)
  if (!health || !health.ok) throw new Error(`API ayakta degil: ${API}`)

  const ayse = await login('ayse@demo.dev')
  const berk = await login('berk@demo.dev')
  const auth = { Authorization: `Bearer ${ayse.accessToken}` }

  const convRes = await fetch(`${API}/api/conversations`, { headers: auth })
  const conversations = await convRes.json()
  const conversationId = conversations[0]?.conversationId
  if (!conversationId) throw new Error('sohbet yok — tools/seed-demo.ps1 calistirin')

  // Yoğun senaryo: aradığı konuyu BİNLERCE kişinin sunduğu kullanıcı. Varsa ölçüme eklenir
  // (scratchpad/perf-user.ps1 ile oluşturulur). Eşleştirme maliyeti buna göre ölçeklenir.
  let heavyAuth = null
  try {
    const heavy = await login('perf@bench.dev')
    heavyAuth = { Authorization: `Bearer ${heavy.accessToken}` }
  } catch {
    console.log('(not: perf@bench.dev yok — yogun oneri senaryosu atlaniyor)')
  }

  const scenarios = [
    { name: 'GET /catalog/topics',            fn: () => fetch(`${API}/api/catalog/topics`) },
    { name: 'GET /portfolio/suggestions',     fn: () => fetch(`${API}/api/portfolio/suggestions?limit=20`, { headers: auth }) },
    ...(heavyAuth ? [{
      name: 'GET /suggestions (yogun konu)',
      fn: () => fetch(`${API}/api/portfolio/suggestions?limit=20`, { headers: heavyAuth }),
    }] : []),
    { name: 'GET /sessions',                  fn: () => fetch(`${API}/api/sessions`, { headers: auth }) },
    { name: 'GET /matches',                   fn: () => fetch(`${API}/api/matches`, { headers: auth }) },
    { name: 'GET /conversations',             fn: () => fetch(`${API}/api/conversations`, { headers: auth }) },
    { name: 'GET /wallet',                    fn: () => fetch(`${API}/api/wallet`, { headers: auth }) },
    { name: 'GET /wallet/statement',          fn: () => fetch(`${API}/api/wallet/statement?page=1&pageSize=20`, { headers: auth }) },
    { name: 'GET /conversations/{id}/mesajlar', fn: () => fetch(`${API}/api/conversations/${conversationId}/messages?page=1&pageSize=50`, { headers: auth }) },
    {
      name: 'POST mesaj gonder (yazma)',
      fn: () => fetch(`${API}/api/conversations/${conversationId}/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${berk.accessToken}` },
        body: JSON.stringify({ content: `yuk testi ${Math.random().toString(36).slice(2, 8)}` }),
      }),
    },
  ]

  console.log(`\nAPI: ${API}   eszamanli: ${CONCURRENCY}   senaryo suresi: ${DURATION_S} sn\n`)
  console.log('senaryo                          istek     rps      p50      p90      p95      p99      max   hata   ort.KB')
  console.log('-'.repeat(115))

  const results = []
  for (const s of scenarios) {
    // Isınma: JIT, bağlantı havuzu ve EF sorgu derlemesi ölçüme karışmasın.
    await runScenario(s.name, s.fn, { concurrency: 4, durationMs: WARMUP_MS })
    const r = await runScenario(s.name, s.fn, { concurrency: CONCURRENCY, durationMs: DURATION_S * 1000 })
    results.push(r)
    console.log(
      `${r.name.padEnd(32)}${String(r.count).padStart(6)}${fmt(r.rps)}${fmt(r.p50)}${fmt(r.p90)}` +
      `${fmt(r.p95)}${fmt(r.p99)}${fmt(r.max)}${String(r.errors).padStart(7)}${fmt(r.avgKb, 1)}`,
    )
  }

  console.log('\n(gecikmeler ms)')

  const slow = results.filter((r) => r.p95 > 200)
  if (slow.length) {
    console.log('\nDIKKAT — p95 > 200 ms olan uclar:')
    for (const r of slow) console.log(`  ${r.name}: p95=${r.p95.toFixed(0)} ms, rps=${r.rps.toFixed(0)}`)
  }
  const failing = results.filter((r) => r.errors > 0)
  if (failing.length) {
    console.log('\nHATA URETEN UCLAR:')
    for (const r of failing) console.log(`  ${r.name}: ${r.errors} hata / ${r.count} istek`)
  }
}

main().catch((err) => {
  console.error(`\n[HATA] ${err.message}`)
  process.exit(1)
})
