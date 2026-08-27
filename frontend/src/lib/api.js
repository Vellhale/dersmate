/*
  API adresi. Yedek DEĞER YALNIZCA GELİŞTİRMEDE geçerli.

  Eskiden koşulsuz `?? 'http://localhost:5000'` yazıyordu ve üretim derlemesinde de
  devreye giriyordu: canlıda her ziyaretçinin tarayıcısı kendi makinesine istek atıyordu.
  Asıl koruma vite.config.js'te (üretim derlemesi VITE_API_URL olmadan HİÇ üretilemiyor);
  buradaki `import.meta.env.DEV` kontrolü ikinci kapı — biri diğerini atlarsa hata
  sessizce yanlış adrese gitmek yerine konsolda görünür.
*/
export const API_BASE =
  import.meta.env.VITE_API_URL ?? (import.meta.env.DEV ? 'http://localhost:5000' : '')

const TOKEN_KEY = 'peerlearn.session'

/**
 * Backend'in ProblemDetails yanıtı: { status, title = HATA_KODU, detail = Türkçe mesaj }.
 * SignalR tarafında aynı bilgi "KOD|mesaj" formatında gelir (bkz. AppExceptionHubFilter).
 */
export class ApiError extends Error {
  constructor(message, code, status) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

export function loadSession() {
  try {
    const raw = localStorage.getItem(TOKEN_KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

export function saveSession(session) {
  if (session) localStorage.setItem(TOKEN_KEY, JSON.stringify(session))
  else localStorage.removeItem(TOKEN_KEY)
}

export function getToken() {
  return loadSession()?.accessToken ?? null
}

/** 401 alındığında oturumu düşürmek için App katmanı bunu dinler. */
export const AUTH_EXPIRED_EVENT = 'peerlearn:auth-expired'

async function request(path, { method = 'GET', body, formData, signal, headers: extra } = {}) {
  const headers = { ...extra }
  const token = getToken()
  if (token) headers.Authorization = `Bearer ${token}`

  let payload
  if (formData) {
    payload = formData // Content-Type'ı tarayıcı boundary ile kendisi koyar.
  } else if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
    payload = JSON.stringify(body)
  }

  let response
  try {
    response = await fetch(`${API_BASE}${path}`, { method, headers, body: payload, signal })
  } catch (err) {
    if (err.name === 'AbortError') throw err
    throw new ApiError(
      /* Metin ortama göre: üretimde kullanıcıya "API'yi başlat" demek anlamsız (yapamaz)
         ve mesajın içindeki localhost adresi, canlı pakette yanlış bir ipucu bırakıyordu. */
      import.meta.env.DEV
        ? `Sunucuya ulaşılamadı. API çalışıyor mu? (${API_BASE})`
        : 'Sunucuya ulaşılamadı. Bağlantını kontrol edip tekrar dene.',
      'NETWORK_ERROR',
      0,
    )
  }

  if (response.status === 401) {
    window.dispatchEvent(new CustomEvent(AUTH_EXPIRED_EVENT))
  }

  if (response.status === 204) return null

  const text = await response.text()
  let data = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = null
    }
  }

  if (!response.ok) {
    // Kod arayüzde gösterilmiyor (bkz. ErrorBox); teşhis için konsolda kalıyor.
    if (data?.title) console.warn(`[api] ${method} ${path} → ${response.status} ${data.title}`)
    throw new ApiError(
      data?.detail ?? `Beklenmeyen hata (HTTP ${response.status}).`,
      data?.title ?? 'UNKNOWN',
      response.status,
    )
  }

  return data
}

/**
 * Kanıt görselini blob olarak indirir ve object URL döner.
 * <img src> Authorization başlığı taşıyamadığı için görsel fetch ile alınmak zorunda;
 * çağıran, kullanmayı bitirince URL.revokeObjectURL ile serbest bırakmalıdır.
 */
async function fetchProofBlob(path) {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { Authorization: `Bearer ${getToken()}` },
  })

  if (!response.ok) {
    throw new ApiError('Kanıt görseli yüklenemedi.', 'PROOF_LOAD_FAILED', response.status)
  }

  return URL.createObjectURL(await response.blob())
}

/**
 * Avatar object URL önbelleği (userId -> Promise<string|null>).
 * Promise saklanır, çözülen değer değil: aynı anda açılan on kart tek istek atsın diye.
 */
const avatarCache = new Map()

function fetchAvatar(userId) {
  if (avatarCache.has(userId)) {
    return avatarCache.get(userId)
  }

  const pending = fetch(`${API_BASE}/api/users/${userId}/avatar`, {
    headers: { Authorization: `Bearer ${getToken()}` },
  })
    .then((response) => (response.ok ? response.blob() : null))
    .then((blob) => (blob ? URL.createObjectURL(blob) : null))
    .catch(() => null) // Avatar yokluğu akışı kırmamalı; çağıran baş harfleri gösterir.

  avatarCache.set(userId, pending)
  return pending
}

export const api = {
  // --- Kimlik ---
  register: (payload) => request('/api/auth/register', { method: 'POST', body: payload }),
  verifyEmail: (token) => request('/api/auth/verify-email', { method: 'POST', body: { token } }),

  /** Doğrulama bağlantısını yeniden gönderir. Yanıt, adres kayıtlı olsun olmasın aynıdır. */
  resendVerification: (email) =>
    request('/api/auth/resend-verification', { method: 'POST', body: { email } }),
  login: (payload) => request('/api/auth/login', { method: 'POST', body: payload }),

  /** Parola sıfırlama bağlantısı ister. Yanıt, adres kayıtlı olsun olmasın AYNI (204) —
      farklı yanıt vermek "bu e-posta kayıtlı mı" sorusunu herkese yanıtlardı. */
  forgotPassword: (email) =>
    request('/api/auth/forgot-password', { method: 'POST', body: { email } }),

  /** Bağlantıdaki token'la yeni parolayı yazar. Token tek kullanımlık, 1 saat geçerli. */
  resetPassword: (token, newPassword) =>
    request('/api/auth/reset-password', { method: 'POST', body: { token, newPassword } }),

  // --- Katalog ---
  topics: () => request('/api/catalog/topics'),
  categories: () => request('/api/catalog/categories'),

  /** Gelişmiş arama (Modül 1). Boş/null filtreler sorgu dizesine hiç eklenmez. */
  searchOffers: (filters) => {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(filters)) {
      if (value === null || value === undefined || value === '') continue
      params.set(key, String(value))
    }
    return request(`/api/discovery/offers?${params.toString()}`)
  },

  /** Üniversite ağı araması. searchOffers ile aynı kural: boş/null filtreler sorguya eklenmez. */
  searchUniversityPeers: (filters) => {
    const params = new URLSearchParams()
    // Yalnızca uçun tanıdığı dört alan geçsin; ekrandaki diğer durum sorguya sızmasın.
    const { university, department, page, pageSize } = filters
    for (const [key, value] of Object.entries({ university, department, page, pageSize })) {
      if (value === null || value === undefined || value === '') continue
      params.set(key, String(value))
    }
    return request(`/api/discovery/users?${params.toString()}`)
  },

  // --- Portföy & eşleştirme ---
  myPortfolio: () => request('/api/portfolio/entries'),
  addPortfolioEntry: (payload) => request('/api/portfolio/entries', { method: 'POST', body: payload }),
  removePortfolioEntry: (id) => request(`/api/portfolio/entries/${id}`, { method: 'DELETE' }),
  suggestions: (limit = 20) => request(`/api/portfolio/suggestions?limit=${limit}`),

  myMatches: () => request('/api/matches'),
  // Konusuz (üniversite ağı) istekte requestedTopicId null gönderilebilir.
  createMatch: (payload) => request('/api/matches', { method: 'POST', body: payload }),
  respondMatch: (matchId, accept) =>
    request(`/api/matches/${matchId}/respond`, { method: 'POST', body: { accept } }),
  closeMatch: (matchId) => request(`/api/matches/${matchId}/close`, { method: 'POST' }),

  // --- Sohbet ---
  conversations: () => request('/api/conversations'),
  messages: (conversationId, page = 1, pageSize = 50) =>
    request(`/api/conversations/${conversationId}/messages?page=${page}&pageSize=${pageSize}`),
  sendMessage: (conversationId, content) =>
    request(`/api/conversations/${conversationId}/messages`, { method: 'POST', body: { content } }),
  markRead: (conversationId) =>
    request(`/api/conversations/${conversationId}/read`, { method: 'POST' }),

  // --- Dersler ---
  /**
   * Derslerim. Aktif dersler HER ZAMAN tam döner; yalnızca geçmiş sayfalanır
   * (bkz. GetMySessions: aksiyon bekleyen bir ders sayfanın altında kalmamalı).
   */
  mySessions: (pastPage = 1, pastPageSize = 20) =>
    request(`/api/sessions?pastPage=${pastPage}&pastPageSize=${pastPageSize}`),
  sessionProofs: (sessionId) => request(`/api/sessions/${sessionId}/proofs`),

  /**
   * Kanıt görselini blob olarak indirir ve object URL döner.
   * <img src> başlık taşıyamadığı için görsel Authorization ile fetch edilmek zorunda;
   * çağıran, URL'yi kullanmayı bitirince URL.revokeObjectURL ile serbest bırakmalıdır.
   */
  proofContentUrl: (sessionId, proofId) =>
    fetchProofBlob(`/api/sessions/${sessionId}/proofs/${proofId}/content`),
  bookSession: (payload) => request('/api/sessions', { method: 'POST', body: payload }),
  completeSession: (sessionId, verificationCode, file) => {
    const form = new FormData()
    form.append('verificationCode', verificationCode)
    form.append('proof', file)
    return request(`/api/sessions/${sessionId}/complete`, { method: 'POST', formData: form })
  },
  approveSession: (sessionId) => request(`/api/sessions/${sessionId}/approve`, { method: 'POST' }),
  cancelSession: (sessionId, reason) =>
    request(`/api/sessions/${sessionId}/cancel`, { method: 'POST', body: { reason } }),
  /**
   * Ders hakkında TEK YÖNLÜ şikayet. Şikayet edilen kişi bunu görmez, bildirilmez ve
   * yanıt veremez; kayıt doğrudan yönetim kuyruğuna düşer. Ders akışı etkilenmez.
   */
  reportSession: (sessionId, reason, description) =>
    request(`/api/sessions/${sessionId}/report`, { method: 'POST', body: { reason, description } }),

  // --- Cüzdan ---
  wallet: () => request('/api/wallet'),
  statement: (page = 1, pageSize = 20) =>
    request(`/api/wallet/statement?page=${page}&pageSize=${pageSize}`),

  // --- Profil ve değerlendirmeler ---
  userProfile: (userId) => request(`/api/users/${userId}/profile`),

  /** Oturumdaki kullanıcının profili — çağıranların userId taşımasını gerektirmez. */
  myProfile: () => {
    const userId = loadSession()?.userId
    if (!userId) return Promise.resolve(null)
    return request(`/api/users/${userId}/profile`)
  },

  updateProfile: (payload) => request('/api/profile', { method: 'PUT', body: payload }),
  uploadAvatar: (formData) => request('/api/profile/avatar', { method: 'POST', formData }),
  /**
   * Öğrenci belgesi (PDF/görsel, en fazla 10 MB). Yeni belge, önceki doğrulama/ret
   * kararını sıfırlar ve beyanı yeniden kuyruğa sokar.
   */
  uploadTeacherDocument: (file) => {
    const form = new FormData()
    form.append('document', file)
    return request('/api/profile/teacher-candidate/document', { method: 'POST', formData: form })
  },

  declareTeacherCandidate: (payload) =>
    request('/api/profile/teacher-candidate', { method: 'PUT', body: payload }),

  /**
   * Avatar'ı blob olarak indirir ve object URL döner; yoksa null.
   *
   * Neden doğrudan <img src> değil: uç [Authorize] altında ve img etiketi Authorization
   * başlığı gönderemez. Ucu herkese açmak alternatifti — profil fotoğrafları giriş
   * yapmış kullanıcılara görünür olduğu için kimlik sınırını burada da korumayı seçtim.
   *
   * Sonuçlar kullanıcı başına ÖNBELLEKLENİR: aynı avatar bir listede onlarca kez
   * görünebilir ve her biri için ayrı istek atmak anlamsız olurdu.
   */
  avatarObjectUrl: (userId) => fetchAvatar(userId),
  forgetAvatar: (userId) => {
    const cached = avatarCache.get(userId)
    if (cached) {
      cached.then((url) => url && URL.revokeObjectURL(url)).catch(() => {})
      avatarCache.delete(userId)
    }
  },

  userReviews: (userId, page = 1, pageSize = 10) =>
    request(`/api/users/${userId}/reviews?page=${page}&pageSize=${pageSize}`),

  /**
   * Branş rozetleri + branş bazlı anlatım saatleri.
   * Profil ucundan AYRI: rozet şeridi daha seyrek değişiyor ve gecikmeli yüklenebiliyor.
   */
  userSubjectBadges: (userId) => request(`/api/users/${userId}/subject-badges`),

  /*
    KULLANICI ŞİKAYETİ — ders bağlamı olmadan (2026-08-27).

    Önceden şikayet açmanın TEK yolu bir ders üzerindendi; sohbette taciz eden ama henüz
    tamamlanmış dersi olmayan biri hiçbir şekilde bildirilemiyordu. Handler bu dalı zaten
    destekliyordu, eksik olan HTTP kapısı ve buradaki sarmalayıcıydı.
  */
  reportUser: (userId, reason, description) =>
    request(`/api/users/${userId}/report`, { method: 'POST', body: { reason, description } }),
  createReview: (sessionId, payload) =>
    request(`/api/sessions/${sessionId}/review`, { method: 'POST', body: payload }),

  /*
    ─── TOPLULUK (FORUM) ────────────────────────────────────────────────────────

    Sayfa (pages/Topluluk.jsx) 2026-08-25'te sabit veriyle yazıldı; bu uçlar onun
    sunucu karşılığı. Sıralama, tarih penceresi ve etiket filtresi SUNUCUDA
    uygulanıyor — istemcide uygulansaydı sayfalama anlamsız olurdu (ikinci sayfayı
    verebilmek için tüm gönderileri indirmek gerekirdi).

    sort / range / tag SUNUCU ENUM ADLARI ile gidiyor ("Newest", "Day", "ExamStress").
    Türkçe arayüz anahtarlarından çeviri Topluluk.jsx'te tek bir tabloda; burada
    çevrilmiyor ki kablo sözleşmesi tek anlamlı kalsın.
  */
  forumFeed: (
    { sort = 'Newest', range = 'All', tag = null, page = 1, pageSize = 20 } = {},
    signal,
  ) => {
    const q = new URLSearchParams({ sort, range, page: String(page), pageSize: String(pageSize) })
    // Etiket yoksa parametre HİÇ gönderilmiyor: boş bir `tag=` değeri sunucuda geçersiz
    // enum olarak bağlanır ve akış 400 dönerdi.
    if (tag) q.set('tag', tag)
    return request(`/api/community/posts?${q}`, { signal })
  },

  createForumPost: (tag, title, body) =>
    request('/api/community/posts', { method: 'POST', body: { tag, title, body } }),

  forumComments: (postId, signal) => request(`/api/community/posts/${postId}/comments`, { signal }),

  createForumComment: (postId, body) =>
    request(`/api/community/posts/${postId}/comments`, { method: 'POST', body: { body } }),

  /**
   * Oy. value yalnızca 1 ya da -1; SIFIR GÖNDERİLMEZ — geri almak, aynı yöne ikinci kez
   * oy vermektir (sunucu satırı siler). Ayrı bir "oyu kaldır" ucu yok çünkü kullanıcı
   * için de tek bir jest: aynı oka tekrar basmak.
   *
   * Dönen değer sunucunun SON sayaçları: { upvoteCount, downvoteCount, myVote }.
   * İstemci optimistik gösterip bu yanıtla düzeltiyor.
   */
  voteForumPost: (postId, value) =>
    request(`/api/community/posts/${postId}/vote`, { method: 'POST', body: { value } }),
  voteForumComment: (commentId, value) =>
    request(`/api/community/comments/${commentId}/vote`, { method: 'POST', body: { value } }),

  /**
   * Şikayet aynı moderasyon kuyruğuna düşüyor (moderation.Reports) — ders, sohbet ve
   * forum şikayetleri moderatör için tek yerde. reason: ReportReason enum adı.
   * Açıklama sunucuda en az 15 karakter (CreateReportHandler); Topluluk.jsx aynı
   * sınırı uyguluyor ki kullanıcı yazıp gönderdikten sonra 400 görmesin.
   */
  reportForumPost: (postId, reason, description) =>
    request(`/api/community/posts/${postId}/report`, {
      method: 'POST',
      body: { reason, description },
    }),
  reportForumComment: (commentId, reason, description) =>
    request(`/api/community/comments/${commentId}/report`, {
      method: 'POST',
      body: { reason, description },
    }),

  // --- Tercihler (çerez rızası / ürün turu) ---
  myPreferences: () => request('/api/preferences'),
  saveCookieConsent: (analytics, functional, consentVersion) =>
    request('/api/preferences/cookie-consent', {
      method: 'PUT',
      body: { analytics, functional, consentVersion },
    }),
  saveOnboarding: (lastStep, completed, suppressed) =>
    request('/api/preferences/onboarding', {
      method: 'PUT',
      body: { lastStep, completed, suppressed },
    }),

  // --- Admin ---
  disputes: () => request('/api/admin/disputes'),

  /** Açık şikayet kuyruğu (yalnızca yönetim). */
  reports: (onlyOpen = true) => request(`/api/admin/reports?onlyOpen=${onlyOpen}`),
  resolveReport: (reportId, actionTaken, note) =>
    request(`/api/admin/reports/${reportId}/resolve`, { method: 'POST', body: { actionTaken, note } }),
  /**
   * Forum içeriğini kaldırır (remove=true) ya da geri getirir (remove=false).
   *
   * ŞİKAYETİ KAPATMAKTAN AYRI: resolveReport yalnızca kuyruktaki kaydı kapatıyor,
   * içeriğe dokunmuyor. Bu uç olmadan üç şikayet alıp otomatik perdelenen bir gönderi
   * SONSUZA KADAR perdeli kalıyordu — "işlem gerekmedi" kararı bile perdeyi
   * kaldırmıyordu.
   *
   * Gerekçe zorunlu (sunucu en az 10 karakter istiyor) ve denetim izine yazılıyor.
   */
  moderateForumContent: ({ postId = null, commentId = null, remove, reason }) =>
    request('/api/admin/community/moderate', {
      method: 'POST',
      body: { postId, commentId, remove, reason },
    }),

  adminSessionProofs: (sessionId) => request(`/api/admin/sessions/${sessionId}/proofs`),

  /** Yönetici, katılımcı olmadığı derslerin kanıtını kendi ucundan görür. */
  adminProofContentUrl: (sessionId, proofId) =>
    fetchProofBlob(`/api/admin/sessions/${sessionId}/proofs/${proofId}/content`),

  resolveDispute: (disputeId, resolution, note) =>
    request(`/api/admin/disputes/${disputeId}/resolve`, { method: 'POST', body: { resolution, note } }),
  banUser: (userId, reason) =>
    request(`/api/admin/users/${userId}/ban`, { method: 'POST', body: { reason } }),

  /*
    YAPTIRIM UÇLARI — 2026-08-27'de bağlandı.

    Üçü de backend'de VARDI ama burada tanımlı DEĞİLDİ, yani panelden erişilemiyordu.
    Sonuç sessiz ve tehlikeliydi: moderatör şikayeti "yaptırım uyguladım" diye
    kapatabiliyor, denetim izine gerçekleşmemiş bir yaptırım yazılıyor ve şikayet
    edilen hesaba hiçbir şey olmuyordu. Kullanıcıya da "yönetim gerekli görürse uyarı,
    askı ya da ban uygular" yazıyordu — tutulamayan bir söz.

    YETKİ FARKI SUNUCUDA: ban/unban yalnızca Admin, sanction (uyarı + süreli askı)
    moderatöre de açık. Arayüz bu ayrımı taklit etmiyor, sunucu 403 döndürüyor —
    yetki kontrolünü iki yerde tutmak, birinin unutulduğu gün sessizce açık bırakır.
  */
  unbanUser: (userId, reason) =>
    request(`/api/admin/users/${userId}/unban`, { method: 'POST', body: { reason } }),

  /** @param type 'Warning' | 'TemporaryBan' — durationHours yalnızca TemporaryBan'de anlamlı. */
  sanctionUser: (userId, type, reason, durationHours = null) =>
    request(`/api/admin/users/${userId}/sanction`, {
      method: 'POST',
      body: { type, reason, durationHours },
    }),

  /** İtiraz detayı: ders, iki taraf, kanıtlar, escrow durumu — hakem kararı için. */
  disputeDetail: (disputeId) => request(`/api/admin/disputes/${disputeId}`),

  /** Öğretmen adaylığı kuyruğu. status: Pending | Verified | Rejected | All */
  teacherCandidates: (status = 'Pending', page = 1, pageSize = 25) =>
    request(`/api/admin/teacher-candidates?status=${status}&page=${page}&pageSize=${pageSize}`),

  /** decision: Verify | Reject | Revert. Gerekçe zorunlu (sunucu da doğruluyor). */
  reviewTeacherCandidate: (profileId, decision, note) =>
    request(`/api/admin/teacher-candidates/${profileId}/review`, {
      method: 'POST',
      body: { decision, note },
    }),

  economyMetrics: () => request('/api/admin/metrics'),

  /**
   * Yönetim eliyle puan tanımlama/düzeltme. Pozitif ekler, negatif düşer; gerekçe zorunlu.
   * Yalnızca Admin rolü çağırabilir (sunucu 403 ile korur).
   *
   * idempotencyKey ZORUNLU (sunucu anahtarsız isteği 400 ile reddeder) ve çağıranın
   * sorumluluğunda: aynı düzeltmenin TEKRAR DENEMELERİ aynı anahtarla gitmeli, yeni bir
   * düzeltme yeni anahtar almalı. Buradan üretilseydi her çağrı yeni anahtar alır ve
   * koruma hiçbir şey yapmazdı — ağ hatasından sonraki tekrar denemede tam olarak
   * korunması gereken durumda.
   */
  adjustCredits: (userId, amount, reason, idempotencyKey) =>
    request(`/api/admin/users/${userId}/credits`, {
      method: 'POST',
      body: { amount, reason },
      headers: { 'Idempotency-Key': idempotencyKey },
    }),
  auditLog: (page = 1, pageSize = 25) =>
    request(`/api/admin/audit-log?page=${page}&pageSize=${pageSize}`),

}
