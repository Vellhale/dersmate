/**
 * Çerez rızası — tek doğruluk kaynağı (KVKK / GDPR, Modül 3).
 *
 * İKİ KATMANLI SAKLAMA, bilinçli:
 *   • localStorage — banner giriş yapılmadan da çıkar; o anda kimlik yoktur, kaydedilecek
 *     tek yer tarayıcıdır. Ayrıca her sayfa açılışında sunucuya sormadan anında okunur
 *     (script yükleme kararı gecikemez).
 *   • Sunucu (UserPreferences) — ispat yükümlülüğü veri sorumlusundadır. localStorage
 *     kullanıcı tarafından silinebilir/değiştirilebilir, kanıt değeri yoktur. Ayrıca
 *     tercih cihazlar arasında taşınır.
 * Giriş yapıldığında yerel tercih sunucuya taşınır; sonrasında sunucu otoritedir.
 */

const STORAGE_KEY = 'peerlearn.consent'

/**
 * Aydınlatma metninin sürümü. Metin DEĞİŞİRSE bu değer artırılmalıdır: eski metne
 * verilmiş onay, yeni işleme kapsamını meşrulaştırmaz ve rıza yeniden sorulmalıdır.
 * Sürüm artınca kullanıcı banner'ı tekrar görür (bkz. needsConsent).
 */
export const CONSENT_VERSION = '2026-08-16'

/** Rızaya tabi kategoriler. "Zorunlu" burada YOK — reddedilemez, saklanacak tercih de yok. */
export const CONSENT_CATEGORIES = [
  {
    key: 'necessary',
    title: 'Zorunlu çerezler',
    required: true,
    description:
      'Oturum açık kalsın ve güvenlik sağlansın diye kullanılır. Bunlar olmadan site çalışmaz, ' +
      'bu yüzden kapatılamaz.',
    // Dürüstlük gereği burada AÇIKÇA yazılıyor: uygulama bir cihaz parmak izi (HWID)
    // üretiyor. Kullanıcıya "sadece oturum çerezi" demek yanıltıcı olurdu.
    details: [
      'Oturum anahtarı (giriş yapmış kalman için)',
      'Cihaz kimliği (HWID) — banlanan hesabın yeni hesapla dönmesini engellemek için üretilen cihaz parmak izi',
      'Çerez tercihinin kendisi',
    ],
  },
  {
    key: 'functional',
    title: 'Fonksiyonel çerezler',
    required: false,
    description:
      'Tanıtım turunda nerede kaldığını hatırlar. Kapatırsan site çalışır ama tur, ' +
      'kapatmadığın sürece yeni oturumlarda yeniden görünebilir.',
    // Dürüstlük: "bir daha gösterme" gibi AÇIK talimatın bu kategoriden bağımsız olarak
    // kaydedildiği söyleniyor — aksi halde kullanıcı kapatamadığı bir turla baş başa kalırdı.
    details: [
      'Ürün turunda kaldığın adım',
      'Aynı oturumda turu "geç" işaretin',
      'Not: "bir daha gösterme" talimatın, bu tercihten bağımsız olarak hesabına kaydedilir.',
    ],
  },
  {
    key: 'analytics',
    title: 'Analitik çerezler',
    required: false,
    description:
      'Hangi sayfaların kullanıldığını anonim olarak ölçmemizi sağlar (Google Analytics). ' +
      'Kapatırsan ölçüm kodu hiç yüklenmez.',
    details: ['Google Analytics (GA4)'],
  },
]

/** Hiçbir şey seçilmemiş başlangıç durumu. */
export const EMPTY_CONSENT = { analytics: false, functional: false, version: null, updatedAt: null }

export function readLocalConsent() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null

    const parsed = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null) return null

    return {
      analytics: Boolean(parsed.analytics),
      functional: Boolean(parsed.functional),
      version: parsed.version ?? null,
      updatedAt: parsed.updatedAt ?? null,
    }
  } catch {
    // Bozuk/erişilemez depolama (gizli sekme kısıtları) rızayı VAR saymamalı:
    // null dönmek banner'ı gösterir, yani analitik varsayılan olarak KAPALI kalır.
    return null
  }
}

export function writeLocalConsent(consent) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(consent))
  } catch {
    // Yazamamak akışı durdurmaz; kullanıcı bir dahaki açılışta yeniden sorulur.
  }
}

export function clearLocalConsent() {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    /* yoksay */
  }
}

/** Banner gösterilmeli mi? Rıza hiç yoksa ya da metnin sürümü değiştiyse evet. */
export function needsConsent(consent) {
  return !consent || consent.version !== CONSENT_VERSION
}
