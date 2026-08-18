const dateTimeFormat = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

const dateFormat = new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'long', year: 'numeric' })

/** Backend tüm zamanları UTC ("...Z") döner; burada kullanıcının yerel saatine çevrilir. */
export function formatDateTime(utcString) {
  if (!utcString) return '—'
  return dateTimeFormat.format(new Date(utcString))
}

export function formatDate(utcString) {
  if (!utcString) return '—'
  return dateFormat.format(new Date(utcString))
}

export function formatTime(utcString) {
  if (!utcString) return '—'
  return new Date(utcString).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

/** "3 gün 4 saat" / "12 dakika" gibi kalan süre metni. Geçmişse null. */
export function remainingText(utcString) {
  if (!utcString) return null
  const ms = new Date(utcString).getTime() - Date.now()
  if (ms <= 0) return null

  const minutes = Math.ceil(ms / 60000)
  if (minutes < 60) return `${minutes} dakika`

  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} saat ${minutes % 60} dakika`

  const days = Math.floor(hours / 24)
  return `${days} gün ${hours % 24} saat`
}

export function daysUntil(utcString) {
  const ms = new Date(utcString).getTime() - Date.now()
  return Math.ceil(ms / 86400000)
}

/** Puan hareketi işaretli gösterim: +100 / −1 */
export function signedCredit(amount) {
  return amount > 0 ? `+${amount}` : `−${Math.abs(amount)}`
}

export const SESSION_STATUS_LABELS = {
  Booked: 'Rezerve',
  AwaitingApproval: 'Onay bekliyor',
  Completed: 'Tamamlandı',
  Disputed: 'İtirazlı',
  Cancelled: 'İptal edildi',
  Expired: 'Süresi doldu',
}

/*
  ESKİ TÜRLER LİSTEDEN ÇIKARILMADI. LessonSpending ve Expiry artık YAZILMIYOR (ders almak
  ücretsiz, kazanılan puan da yanmıyor) ama geçiş öncesinden kalan satırlar defterde
  duruyor ve kullanıcının geçmişinde görünüyor. Etiketi kaldırmak o satırları ham enum
  adıyla ("LessonSpending") gösterirdi; "eski" ön eki hem okunur tutuyor hem de artık
  olmayan bir mekanizmayı yürürlükteymiş gibi anlatmıyor.
*/
export const TRANSACTION_LABELS = {
  WelcomeBonus: 'Hoş geldin puanı',
  LessonEarning: 'Ders anlatım puanı',
  LessonSpending: 'Ders harcaması (eski)',
  Expiry: 'Süresi dolan puan (eski)',
  AdminGrant: 'Yönetici tanımı',
  AdminAdjustment: 'Yönetici düzeltmesi',
}

export const LOT_SOURCE_LABELS = {
  WelcomeBonus: 'Hoş geldin',
  LessonEarning: 'Ders kazancı',
  AdminGrant: 'Yönetici',
}

export const DISPUTE_REASON_LABELS = {
  SessionNotHeld: 'Ders yapılmadı',
  FakeProof: 'Sahte kanıt yüklendi',
  DurationMismatch: 'Süre uyuşmuyor',
  Abuse: 'Kötüye kullanım',
  Other: 'Diğer',
}

/**
 * Şikayet sebepleri. DISPUTE_REASON_LABELS ile aynı değerleri taşıyor ama ayrı duruyor:
 * itiraz geçmişi salt okunur olarak korunuyor, yeni akış şikayet üzerinden gidiyor ve
 * ikisinin metinleri bağımsız değişebilmeli.
 */
export const REPORT_REASON_LABELS = {
  SessionNotHeld: 'Ders hiç yapılmadı',
  FakeProof: 'Kanıt sahte / alakasız',
  DurationMismatch: 'Süre anlaşılandan kısa sürdü',
  Abuse: 'Hakaret, taciz veya uygunsuz davranış',
  Other: 'Diğer',
}
