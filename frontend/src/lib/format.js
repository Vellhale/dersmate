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
  ESKİ TÜR LİSTEDEN ÇIKARILMADI. LessonSpending artık YAZILMIYOR (ders almak ücretsiz) ama
  geçiş öncesinden kalan satırlar defterde duruyor ve kullanıcının geçmişinde görünüyor.
  Etiketi kaldırmak o satırları ham enum adıyla ("LessonSpending") gösterirdi; "eski" eki
  hem okunur tutuyor hem de artık olmayan bir mekanizmayı yürürlükteymiş gibi anlatmıyor.

  Expiry "eski" DEĞİL ve hâlâ yazılıyor: ders ve topluluk kazancı yanmıyor, ama
  e-posta doğrulamasında verilen hoş geldin puanı 14 günde doluyor
  (CreditLedgerService.GrantWelcomeCreditAsync → WelcomeCreditValidityDays) ve
  ExpireCredits işi onu, geçiş öncesinden kalan vadeli lotlarla birlikte, bir Expiry
  satırıyla düşürüyor. 2026-09-27'ye kadar etiket "Süresi dolan puan (eski)" idi ve bu
  yorum "Expiry artık yazılmıyor" diyordu: her yeni kullanıcı 14 gün sonra kendi
  geçmişinde bu satırı, artık olmayan bir mekanizmanın kalıntısı gibi görüyordu. Satırın
  açıklaması Kullanım koşulları §3'te.

  CommunityReward (Topluluk'ta net oy eşiğinin bastığı puan, sunucuda
  CreditTransactionType.CommunityReward) 2026-09-26'ya kadar tabloda YOKTU: Puan geçmişi
  o satırı ham enum adıyla ("CommunityReward") gösteriyordu. Sunucuya yeni bir tür
  eklenirse etiketi AYNI GÜN iki istemcide buraya da yazılır.
*/
export const TRANSACTION_LABELS = {
  WelcomeBonus: 'Hoş geldin puanı',
  LessonEarning: 'Ders anlatım puanı',
  CommunityReward: 'Topluluk katkı puanı',
  LessonSpending: 'Ders harcaması (eski)',
  Expiry: 'Süresi dolan puan',
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
 *
 * ⚠️ BU TABLO GÖSTERİM İÇİN, FORM İÇİN DEĞİL. Moderasyon kuyruğu her sebebi
 * yazabilmek zorunda olduğu için tablo TAM; ama hiçbir şikayet formu bu tablonun
 * tamamını listelemez — ders formunda "Spam" seçeneği, forum formunda "Ders hiç
 * yapılmadı" anlamsız. Her form kendi alt kümesini kendi dosyasında tutuyor
 * (Sessions.jsx, Chat.jsx, Topluluk.jsx). Tabloyu doğrudan `Object.entries` ile
 * bir <select>'e dökmek, sonraki eklenen sebebi sessizce yanlış forma düşürür.
 */
export const REPORT_REASON_LABELS = {
  SessionNotHeld: 'Ders hiç yapılmadı',
  FakeProof: 'Kanıt sahte / alakasız',
  DurationMismatch: 'Süre anlaşılandan kısa sürdü',
  Abuse: 'Hakaret, taciz veya uygunsuz davranış',
  // Forum şikayetlerinden gelenler (bkz. Domain/Moderation/Report.cs).
  Spam: 'Spam veya reklam',
  Copyright: 'Telif ihlali',
  PersonalInfo: 'Kişisel bilgi paylaşımı',
  OffTopic: 'Konu dışı veya trolleme',
  Other: 'Diğer',
}
