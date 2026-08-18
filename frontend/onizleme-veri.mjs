/*
  ÖNİZLEME SAHTE VERİSİ + UÇ EŞLEŞTİRME.

  İKİ TÜKETİCİSİ VAR ve bu dosyanın biçimi buna göre seçildi:
    1. `onizleme.mjs`      → Node'da import eder (yerel sunucu).
    2. `onizleme-tekdosya.mjs` → dosyanın METNİNİ okur, `export` sözcüklerini atıp
       tek-dosya HTML'in içine gömer (tarayıcıda çalışır).

  Bu yüzden burada Node'a özgü HİÇBİR ŞEY olamaz: import yok, fs yok, process yok.
  Yalnızca düz JavaScript ve `crypto.randomUUID` (ikisinde de var).

  ⚠️ VERİ SAHTEDİR. Doğrulama yok, kayıt yok, üretimle ilgisi yok.
*/

// --- Sahte veri -------------------------------------------------------------

const DEMO_ID = '11111111-1111-1111-1111-111111111111'

const konu = (ad, ders, gonullu = false) => ({
  topicId: crypto.randomUUID(), topicName: ad, subjectName: ders, level: 3, isVolunteer: gonullu,
})

const PROFIL = {
  userId: DEMO_ID,
  displayName: 'Demo Eğitmen',
  bio: 'TYT–AYT matematik ve geometri anlatıyorum. Formül ezberletmek yerine nereden geldiğini göstermeyi seviyorum.',
  avatarUrl: null,
  university: 'Örnek Üniversitesi',
  department: 'Matematik Öğretmenliği',
  joinedAtUtc: '2026-02-14T09:00:00Z',
  averageRating: 4.7,
  ratingCount: 38,
  taughtSessionCount: 96,
  taughtMinutes: 6180,
  totalEarnedCredits: 8400,
  rankTitle: 'Uzman',
  rankEmoji: '🎯',
  nextRankAt: 12000,
  isSelf: true,
  teacherCandidate: null,
  canTeach: [
    konu('Türev Alma Kuralları', 'Matematik'), konu('İntegral Uygulamaları', 'Matematik'),
    konu('Limit ve Süreklilik', 'Matematik'), konu('Çemberde Açı', 'Geometri'),
    konu('Dik Üçgen ve Pisagor Bağıntısı', 'Geometri'), konu('Elektrik ve Manyetizma', 'Fizik', true),
  ],
  wantsToLearn: [konu('Organik Kimya', 'Kimya'), konu('Hücre Bölünmeleri', 'Biyoloji')],
}

const ROZETLER = {
  badges: [
    { branch: 'Matematik', subject: 'Matematik', level: 'Ustad', title: 'Matematik Üstadı', hours: 50, earnedAtUtc: '2026-07-02T10:00:00Z' },
    { branch: 'Matematik', subject: 'Matematik', level: 'Usta', title: 'Matematik Ustası', hours: 20, earnedAtUtc: '2026-05-11T10:00:00Z' },
    { branch: 'Matematik', subject: 'Matematik', level: 'Cirak', title: 'Matematik Çırağı', hours: 5, earnedAtUtc: '2026-03-04T10:00:00Z' },
    { branch: 'Geometri', subject: 'Geometri', level: 'Usta', title: 'Geometri Ustası', hours: 20, earnedAtUtc: '2026-06-18T10:00:00Z' },
    { branch: 'Fizik', subject: 'Fizik', level: 'Cirak', title: 'Fizik Çırağı', hours: 5, earnedAtUtc: '2026-08-01T10:00:00Z' },
    { branch: 'Kimya', subject: 'Kimya', level: 'Cirak', title: 'Kimya Çırağı', hours: 5, earnedAtUtc: '2026-08-09T10:00:00Z' },
  ],
  progress: [
    { branch: 'Matematik', subject: 'Matematik', hours: 62, minutes: 3720 },
    { branch: 'Geometri', subject: 'Geometri', hours: 24, minutes: 1440 },
    { branch: 'Fizik', subject: 'Fizik', hours: 7, minutes: 420 },
    { branch: 'Kimya', subject: 'Kimya', hours: 5, minutes: 300 },
    { branch: 'Biyoloji', subject: 'Biyoloji', hours: 3, minutes: 180 },
    { branch: 'Turkce', subject: 'Türkçe', hours: 1, minutes: 90 },
  ],
}

const yorum = (i, ad, puan, konuAdi, metin, etiketler, gonullu = false) => ({
  reviewId: `r${i}`, reviewerDisplayName: ad, score: puan, topicName: konuAdi,
  wasVolunteerSession: gonullu, createdAtUtc: `2026-08-${String(9 + i).padStart(2, '0')}T14:30:00Z`,
  comment: metin, tags: etiketler,
})

const YORUMLAR = {
  averageScore: 4.7, reviewCount: 38, scoreDistribution: [1, 1, 2, 6, 28],
  averageTeachingScore: 4.8, averagePunctualityScore: 4.6,
  popularTags: [
    { tag: 'GreatExamples', count: 26 }, { tag: 'StartedOnTime', count: 19 },
    { tag: 'SharedResources', count: 14 }, { tag: 'WouldBookAgain', count: 11 },
  ],
  reviews: {
    page: 1, pageSize: 10, totalPages: 4, hasNextPage: true,
    items: [
      yorum(1, 'Ayşe K.', 5, 'Türev Alma Kuralları', 'Zincir kuralını nihayet oturttum. Tahtada adım adım gitmesi çok işe yaradı, sorduğum her yerde durup tekrar anlattı.', ['GreatExamples', 'StartedOnTime']),
      yorum(2, 'Mert D.', 5, 'İntegral Uygulamaları', 'Alan hesabında takıldığım noktayı iki örnekte çözdü.', ['GreatExamples', 'WouldBookAgain']),
      yorum(3, 'Zeynep A.', 4, 'Limit ve Süreklilik', 'İyiydi, biraz hızlı ilerledi ama sorunca geri döndü.', ['StartedOnTime']),
      yorum(4, 'Can T.', 5, 'Çemberde Açı', 'Gönüllü ders açmış, hiç beklemiyordum. Çok net anlattı.', ['SharedResources'], true),
      yorum(5, 'Elif S.', 5, 'Dik Üçgen ve Pisagor Bağıntısı', 'Formül ezberletmek yerine nereden geldiğini gösterdi.', ['GreatExamples', 'WouldBookAgain']),
      yorum(6, 'Burak Y.', 4, 'Olasılık', 'Kaynak paylaştı, sonrasında da bol soru çözdüm.', ['SharedResources']),
    ],
  },
}

const KATEGORILER = [
  { categoryId: 'c-yks', name: 'YKS', parentCategoryId: null, sortOrder: 1 },
  { categoryId: 'c-tyt', name: 'TYT', parentCategoryId: 'c-yks', sortOrder: 1 },
  { categoryId: 'c-ayt', name: 'AYT', parentCategoryId: 'c-yks', sortOrder: 2 },
]

const DERSLER = ['Türkçe', 'Tarih', 'Coğrafya', 'Matematik', 'Geometri', 'Fizik', 'Kimya', 'Biyoloji']
const KONULAR = DERSLER.flatMap((ders) =>
  ['TYT', 'AYT'].flatMap((seviye) =>
    [1, 2, 3].map((i) => ({
      topicId: crypto.randomUUID(), topic: `${ders} örnek konu ${i}`,
      subject: ders, category: seviye, rootCategory: 'YKS',
    }))))

/*
  UÇ EŞLEŞTİRME.

  Yalnızca önizlemenin gösterdiği ekranların ihtiyaç duyduğu uçlar tanımlı. Tanımsız her
  /api isteği BOŞ ama GEÇERLİ bir gövdeyle yanıtlanıyor (aşağıya bak) — 404 dönmek,
  bakmak istediğimiz sayfayı hata kutusuna çevirirdi.
*/
function apiYanit(method, yol) {
  if (method === 'POST' && yol === '/api/auth/login') {
    return { accessToken: 'onizleme-token', userId: DEMO_ID, displayName: PROFIL.displayName, isAdmin: false }
  }
  if (method === 'POST' && yol === '/api/auth/register') {
    return { userId: DEMO_ID, verificationToken: 'onizleme-dogrulama-tokeni' }
  }
  if (yol.endsWith('/subject-badges')) return ROZETLER
  if (yol.includes('/reviews')) return YORUMLAR
  if (yol.endsWith('/profile')) return PROFIL
  if (yol === '/api/catalog/categories') return KATEGORILER
  if (yol === '/api/catalog/topics') return KONULAR
  if (yol === '/api/wallet') return { balance: PROFIL.totalEarnedCredits, lockedBalance: 0, lots: [], transactions: { items: [], page: 1, totalPages: 1, hasNextPage: false } }
  if (yol === '/api/conversations') return []

  // Portföy uçları DÜZ DİZİ döner (sayfalı zarf değil) — bileşenler doğrudan .filter/.map
  // çağırıyor. Zarf dönmek "C.filter is not a function" ile sayfayı düşürüyordu.
  if (yol.startsWith('/api/portfolio/entries')) return PROFIL.canTeach.map((k, i) => ({
    entryId: `p${i}`, topicId: k.topicId, topicName: k.topicName, subjectName: k.subjectName,
    categoryName: i % 2 ? 'AYT' : 'TYT', direction: 'Offer', level: k.level, isVolunteer: k.isVolunteer,
  }))
  if (yol.startsWith('/api/portfolio/suggestions')) return []

  // Çerez rızası: sunucu "hiç sorulmamış" derse şerit görünür — önizlemede istenen bu.
  if (yol === '/api/preferences') return {
    analyticsConsent: 'NotAsked', functionalConsent: 'NotAsked',
    consentVersion: null, consentUpdatedAtUtc: null, onboardingCompletedAtUtc: null,
  }
  if (yol.startsWith('/api/preferences')) return {}

  /*
    BİLİNMEYEN UÇ İÇİN BOŞ SAYFA NESNESİ.

    Arayüzdeki liste ekranları ya düz dizi ya da sayfalı bir zarf bekliyor. İkisini de
    karşılayan tek bir gövde yok, bu yüzden zarf dönülüyor: dizi bekleyen bileşenler
    boş liste görüyor (`items`), sayfalama okuyanlar da tutarlı bir sayfa görüyor.
    Yine de tanımsız bir uca düşen ekran eksik görünebilir — önizlemenin sınırı burası.
  */
  return undefined
}

const BOS_SAYFA = { items: [], page: 1, pageSize: 20, totalPages: 1, hasNextPage: false }


export { DEMO_ID, apiYanit, BOS_SAYFA }
