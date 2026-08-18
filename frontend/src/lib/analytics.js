/**
 * Google Analytics 4 (Modül 4).
 *
 * TEK KURAL: script, kullanıcı analitik çerezlere izin VERMEDEN yüklenmez. Yükleme kararı
 * ConsentContext'e bakan tek bir efekttir (bkz. useAnalytics); buradaki fonksiyonlar rıza
 * durumunu kendileri sorgulamaz, çağrıldıkları anda izin verilmiş sayarlar — iki yerde
 * kontrol etmek, birinin unutulması demektir.
 *
 * Ölçüm kimliği yoksa (yerel geliştirme, testler) her şey sessizce no-op'a döner: analitik
 * yokluğu asla bir akışı kırmamalı.
 */

const MEASUREMENT_ID = import.meta.env.VITE_GA4_MEASUREMENT_ID ?? ''
const SCRIPT_ID = 'ga4-script'

/** GA4'ün kendi kapatma anahtarı; true iken kütüphane hiçbir istek göndermez. */
const disableFlag = `ga-disable-${MEASUREMENT_ID}`

export const analyticsConfigured = Boolean(MEASUREMENT_ID)

function gtag() {
  // gtag, arguments nesnesini OLDUĞU GİBİ dataLayer'a iter — spread ile dizi yapılırsa
  // GA4 parametreleri okuyamaz. Bu yüzden ok fonksiyonu değil, klasik fonksiyon.
  window.dataLayer = window.dataLayer || []
  window.dataLayer.push(arguments)
}

/** Rıza verildiğinde çağrılır. Birden çok kez çağrılması zararsızdır. */
export function enableAnalytics() {
  if (!analyticsConfigured) return

  window[disableFlag] = false

  if (document.getElementById(SCRIPT_ID)) {
    return // Zaten yüklü.
  }

  const script = document.createElement('script')
  script.id = SCRIPT_ID
  script.async = true
  script.src = `https://www.googletagmanager.com/gtag/js?id=${MEASUREMENT_ID}`
  document.head.appendChild(script)

  gtag('js', new Date())
  gtag('config', MEASUREMENT_ID, {
    // IP anonimleştirme GA4'te varsayılan; yine de açıkça belirtiliyor ki
    // yapılandırma tek bakışta okunabilsin.
    anonymize_ip: true,
    // Sayfa görüntülemeyi GA4 kendisi gönderir; SPA'da rota değişimleri
    // useAnalytics içinde elle bildirilir.
    send_page_view: false,
  })
}

/**
 * Rıza geri alındığında çağrılır.
 *
 * BURASI ÖNEMLİ: yalnızca "yeni olay göndermeyi bırakmak" yetmez. Kullanıcı izni geri
 * çektiğinde daha önce yazılmış _ga çerezleri de silinmelidir — aksi halde tanımlayıcı
 * tarayıcıda kalmaya devam eder ve "izni geri çektim" demek fiilen anlamsızlaşır.
 * Script etiketi de kaldırılır; sayfa yenilenene kadar kütüphane bellekte kalabilir,
 * bu yüzden asıl güvence ga-disable bayrağıdır.
 */
export function disableAnalytics() {
  if (!analyticsConfigured) return

  window[disableFlag] = true

  document.getElementById(SCRIPT_ID)?.remove()

  // _ga, _ga_<ID>, _gid… Alan adı varyantlarıyla birlikte temizlenir: çerez üst alan
  // adına (.site.com) yazılmış olabilir ve yalnızca tam eşleşen alan/yol ile silinir.
  const hostParts = window.location.hostname.split('.')
  const domains = [undefined, window.location.hostname]
  for (let i = 0; i < hostParts.length - 1; i++) {
    domains.push('.' + hostParts.slice(i).join('.'))
  }

  for (const cookie of document.cookie.split(';')) {
    const name = cookie.split('=')[0]?.trim()
    if (!name || !(name.startsWith('_ga') || name.startsWith('_gid'))) continue

    for (const domain of domains) {
      document.cookie =
        `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/` +
        (domain ? `; domain=${domain}` : '')
    }
  }
}

/**
 * Özel olay gönderir.
 *
 * KİŞİSEL VERİ GÖNDERİLMEZ. Parametreler bilinçli olarak kaba: süre, kredi tutarı, itiraz
 * sebebi gibi. Kullanıcı kimliği, e-posta, karşı tarafın adı ya da ders/konu kimliği
 * GÖNDERİLMEZ — bunlar tek başına ya da birleştirilerek kişiyi işaret eder ve analitik
 * rızası "kim ne yaptı"yı üçüncü tarafa aktarma izni değildir.
 */
export function trackEvent(name, params = {}) {
  if (!analyticsConfigured) return
  if (window[disableFlag]) return

  gtag('event', name, params)
}

/** Modül 4'te izlenecek olaylar — isimler tek yerde, çağrı yerlerinde yazım hatası olmasın. */
export const AnalyticsEvents = {
  LessonCreated: 'lesson_created',
  SessionRequested: 'session_requested',
  ProofUploaded: 'proof_uploaded',
  CreditTransferred: 'credit_transferred',
  DisputeOpened: 'dispute_opened',
}

export function trackPageView(path) {
  if (!analyticsConfigured || window[disableFlag]) return
  gtag('event', 'page_view', { page_path: path })
}
