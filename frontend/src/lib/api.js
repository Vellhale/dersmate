export const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5000'

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
      'Sunucuya ulaşılamadı. API çalışıyor mu? (varsayılan: http://localhost:5000)',
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

  // --- Portföy & eşleştirme ---
  myPortfolio: () => request('/api/portfolio/entries'),
  addPortfolioEntry: (payload) => request('/api/portfolio/entries', { method: 'POST', body: payload }),
  removePortfolioEntry: (id) => request(`/api/portfolio/entries/${id}`, { method: 'DELETE' }),
  suggestions: (limit = 20) => request(`/api/portfolio/suggestions?limit=${limit}`),

  myMatches: () => request('/api/matches'),
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
  createReview: (sessionId, payload) =>
    request(`/api/sessions/${sessionId}/review`, { method: 'POST', body: payload }),

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
  adminSessionProofs: (sessionId) => request(`/api/admin/sessions/${sessionId}/proofs`),

  /** Yönetici, katılımcı olmadığı derslerin kanıtını kendi ucundan görür. */
  adminProofContentUrl: (sessionId, proofId) =>
    fetchProofBlob(`/api/admin/sessions/${sessionId}/proofs/${proofId}/content`),

  resolveDispute: (disputeId, resolution, note) =>
    request(`/api/admin/disputes/${disputeId}/resolve`, { method: 'POST', body: { resolution, note } }),
  banUser: (userId, reason) =>
    request(`/api/admin/users/${userId}/ban`, { method: 'POST', body: { reason } }),

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
