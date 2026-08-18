const HWID_KEY = 'peerlearn.hwid'

/**
 * Cihaz parmak izi (HWID). Backend login'de bunu ZORUNLU tutar ve ban listesiyle karşılaştırır.
 *
 * Dürüst sınır: tarayıcı parmak izi taklit edilebilir — bu, ban kaçağını zorlaştıran bir
 * katmandır, tek güvence değildir (asıl yaptırım itiraz/moderasyon akışındadır).
 * Kalıcılık için localStorage'a yazılır; silinse bile aynı sinyallerden aynı hash üretilir.
 */
export async function getHwidHash() {
  const cached = localStorage.getItem(HWID_KEY)
  if (cached) return cached

  const signals = [
    navigator.userAgent,
    navigator.language,
    (navigator.languages ?? []).join(','),
    Intl.DateTimeFormat().resolvedOptions().timeZone ?? '',
    `${screen.width}x${screen.height}x${screen.colorDepth}`,
    String(navigator.hardwareConcurrency ?? ''),
    String(navigator.maxTouchPoints ?? ''),
    canvasSignal(),
  ].join('|')

  const hash = await sha256Hex(signals)
  localStorage.setItem(HWID_KEY, hash)
  return hash
}

/*
  ⚠️ BU FONKSİYONUN İÇİ SABİTTİR — HİÇBİR DEĞERİ DEĞİŞTİRME.

  Çizilen her şey (yazı tipi, RENK, metin, koordinatlar) hash'e giriyor. Tek bir baytı
  değişirse ÜRETİMDEKİ TÜM HWID BANLARI geçersiz olur: banlı kullanıcıların cihazı yeni
  bir hash üretir ve hepsi geri döner. Ban listesi geri alınamaz biçimde işe yaramaz hâle
  gelir.

  İKİ AYRI TUZAK VAR, ikisi de "zararsız" görünür:
    1. İSİM DEĞİŞİKLİĞİ → 'PeerLearn' metni. Ürün adı dersmate OLDU ve bu satır bilerek
       DEĞİŞMEDİ (bkz. docs/DEVAM-EDILECEK.md, F4).
    2. PALET DEĞİŞİKLİĞİ → '#4f46e5'. Bu değer eskiden Tailwind brand-600 ile AYNIYDI;
       marka indigo'dan Sky Blue'ya geçerken (F1b) burası bilerek yerinde bırakıldı.
       Artık paletteki hiçbir renge karşılık gelmiyor — ve bu İYİ: "tutarlılık düzeltmesi"
       görüntüsü ortadan kalktı. Yine de GÜNCELLENMEMELİDİR; buradaki değer bir tasarım
       kararı değil, parmak izinin sabitidir.

  KISACASI: bu blok markayı değil, cihazı tanımlar. Marka değişir, bu değişmez.
*/
function canvasSignal() {
  try {
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d')
    if (!ctx) return 'no-canvas'
    ctx.textBaseline = 'top'
    ctx.font = '14px Arial'
    ctx.fillStyle = '#4f46e5'
    ctx.fillRect(0, 0, 60, 20)
    ctx.fillStyle = '#111'
    ctx.fillText('PeerLearn', 2, 2)
    return canvas.toDataURL().slice(-64)
  } catch {
    return 'canvas-error'
  }
}

async function sha256Hex(input) {
  const bytes = new TextEncoder().encode(input)

  // crypto.subtle yalnızca güvenli bağlamda (https / localhost) vardır.
  if (globalThis.crypto?.subtle) {
    const digest = await crypto.subtle.digest('SHA-256', bytes)
    return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('')
  }

  // Güvensiz bağlam yedeği: kriptografik değildir, yalnızca cihazı ayırt eder.
  let h1 = 0x9e3779b9
  let h2 = 0x85ebca6b
  for (const byte of bytes) {
    h1 = Math.imul(h1 ^ byte, 2654435761) >>> 0
    h2 = Math.imul(h2 ^ byte, 1597334677) >>> 0
  }
  return (h1.toString(16) + h2.toString(16)).padStart(16, '0').repeat(4).slice(0, 64)
}
