import { useState } from 'react'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { formatDateTime } from '../lib/format'
import { Avatar } from './Avatar'
import { Badge, Button, Card, EmptyState, ErrorBox, Loading } from './ui'
import { SubjectBadges } from './SubjectBadges'
import { SeviyeRozeti } from './SeviyeRozeti'
import { seviyeEtiketi, seviyeHesapla, seviyeIlerlemeMetni } from '../lib/seviye'

/**
 * Samimi profil görünümü — CV değil, sosyal profil.
 *
 * TASARIM KARARI: en üstte kimlik ve seviye, hemen altında ders/puan sayaçları, sonra
 * "ne anlatır / ne öğrenmek ister" etiketleri, en altta doğrulanmış yorumlar. Sıralama
 * "bu kişiyle ders yapar mıyım" sorusunu yukarıdan aşağıya yanıtlar; resmi bir özgeçmiş
 * sıralaması (eğitim → deneyim → referanslar) burada işe yaramaz çünkü karar puan ve
 * yorumlarla veriliyor.
 *
 * ROZET DUVARI KALDIRILDI: her başarı ayrı bir rozetken hepsi aynı görsel ağırlıktaydı
 * ve hiçbiri bir şey söylemiyordu. Yerini tek bir seviye aldı — karşılaştırılabilir ve
 * tek bakışta okunur.
 */

const TAG_LABELS = {
  KnowsSubject: 'Konuya çok hakim',
  PatientAndClear: 'Sabırlı ve açıklayıcı',
  StartedOnTime: 'Zamanında başladı',
  GreatExamples: 'Çözümlü sorular çok iyiydi',
  SharedResources: 'Anlaşılır kaynaklar paylaştı',
  WouldBookAgain: 'Tekrar ders alırım',
}

export function UserProfileView({ userId }) {
  const profile = useAsync(() => api.userProfile(userId), [userId])
  const [reviewPage, setReviewPage] = useState(1)
  const reviews = useAsync(() => api.userReviews(userId, reviewPage), [userId, reviewPage])

  if (profile.loading) return <Loading />
  if (profile.error) return <ErrorBox error={profile.error} onRetry={profile.reload} />
  if (!profile.data) return null

  const p = profile.data

  return (
    <div className="space-y-6">
      <ProfileHeader profile={p} />
      <StatsRow profile={p} />

      {/* Branş rozetleri istatistiklerin hemen altında: ikisi de "bu kişi ne yapmış"
          sorusunu yanıtlıyor, konu panellerinden ("ne yapabilir") önce gelmeli.
          Bileşen, rozet de ilerleme de yoksa kendini tamamen gizler. */}
      <SubjectBadges userId={userId} />

      <div className="grid gap-4 lg:grid-cols-2">
        <TopicPanel
          title="Anlatabilirim"
          tone="brand"
          topics={p.canTeach}
          emptyText="Henüz konu eklenmemiş."
        />
        <TopicPanel
          title="Öğrenmek istiyorum"
          tone="success"
          topics={p.wantsToLearn}
          emptyText="Henüz konu eklenmemiş."
        />
      </div>

      <ReviewsSection reviews={reviews} page={reviewPage} onPage={setReviewPage} />
    </div>
  )
}

/**
 * KOMPAKT PROFİL KARTI.
 *
 * Önceki hâlde 80 piksellik dekoratif bir bant, taşan bir avatar ve altında ayrı bir
 * rozet duvarı vardı; kart tek başına ekranın yarısını yiyordu ve asıl bilgi (bu kişi
 * ne anlatıyor, ne kadar deneyimli) kaydırma gerektiriyordu. Bant kaldırıldı, avatar
 * satır içine alındı ve seviye doğrudan adın yanına taşındı — profilin ilk ekranında
 * artık karar için gereken her şey var.
 */
function ProfileHeader({ profile }) {
  return (
    <Card className="p-4 sm:p-5">
      <div className="flex items-start gap-4">
        {/*
          avatarUrl bir DEPO ANAHTARI, gezinilebilir bir adres değil — doğrudan <img src>
          olarak verilemez. Avatar bileşeni görseli yetkili uçtan blob olarak çeker ve
          önbelleğe alır; fotoğraf yoksa baş harflere düşer.
        */}
        <Avatar
          userId={profile.userId}
          name={profile.displayName}
          size="md"
          className="shrink-0 ring-2 ring-white shadow-sm"
        />

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <h1 className="truncate text-lg font-bold text-slate-900 sm:text-xl">
              {profile.displayName}
            </h1>
            {/* Üst bardaki rozetin aynısı (bkz. Layout.jsx): aynı bilgi her yerde
                aynı biçimde görünsün diye tek bileşenden geliyor. */}
            <SeviyeRozeti kaynak={profile} className="shrink-0" />
          </div>

          {(profile.university || profile.department) && (
            <p className="mt-0.5 truncate text-sm text-slate-600">
              {[profile.university, profile.department].filter(Boolean).join(' · ')}
            </p>
          )}

          {profile.bio && (
            <p className="mt-2 whitespace-pre-wrap text-sm leading-relaxed text-slate-700">
              {profile.bio}
            </p>
          )}
        </div>
      </div>

      {/* Öğretmen adaylığı kutusu kaldırıldı: adaylık kavramı üründen çıktı, profilde
          kullanıcıları ikiye ayıran bir işaret kalmadı. */}
    </Card>
  )
}

function StatsRow({ profile }) {
  /*
    "Deneyim / 0 dk" alanı kaldırıldı ve yerine SEVİYE geldi.

    O alan yeni hesaplarda her zaman "0 dk" yazıyordu — profile giren ilk kişiye
    söylediği tek şey "bu kullanıcı hiçbir şey yapmamış" oluyordu. Seviye aynı yeri
    kullanır ama en baştan anlamlı bir şey söyler.

    İLERLEME SATIRI SUNUCUDAN: "sonraki seviyeye X puan" hesabı `nextLevelAt` üzerinden
    yapılıyor (bkz. lib/seviye.js). Eşikler istemciye kopyalanmıyor — kopyalansaydı
    sunucudaki tablo değiştiğinde profil eski hedefi göstermeye devam ederdi.
  */
  const items = [
    {
      label: 'Puan',
      value: profile.ratingCount > 0 ? `${Number(profile.averageRating).toFixed(1)} ★` : '—',
      hint: profile.ratingCount > 0 ? `${profile.ratingCount} değerlendirme` : 'Henüz puan yok',
    },
    { label: 'Anlatılan ders', value: profile.taughtSessionCount, hint: 'Tamamlanmış' },
    {
      label: 'Seviye',
      value: seviyeEtiketi(seviyeHesapla(profile)),
      hint: seviyeIlerlemeMetni(profile),
    },
    { label: 'Üyelik', value: new Date(profile.joinedAtUtc).getFullYear(), hint: 'Katılım yılı' },
  ]

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      {items.map((item) => (
        <Card key={item.label} className="p-4">
          <p className="text-xs text-slate-500">{item.label}</p>
          <p className="mt-0.5 text-2xl font-bold text-slate-900">{item.value}</p>
          <p className="mt-0.5 text-xs text-slate-500">{item.hint}</p>
        </Card>
      ))}
    </div>
  )
}


function TopicPanel({ title, tone, topics, emptyText }) {
  return (
    <Card>
      <p className="mb-2 text-sm font-medium text-slate-700">{title}</p>
      {topics?.length ? (
        <div className="flex flex-wrap gap-1.5">
          {topics.map((topic) => (
            <Badge key={topic.topicId} tone={tone}>
              {topic.topicName}
              {/* Gönüllülük işareti kaldırıldı: konular arasında böyle bir ayrım yok. */}
              <span className="ml-1 opacity-70">· {topic.subjectName}</span>
            </Badge>
          ))}
        </div>
      ) : (
        <p className="text-sm text-slate-500">{emptyText}</p>
      )}
    </Card>
  )
}

/*
  DEĞERLENDİRMELER — KOMPAKT.

  Önceki hâlde bu blok profilin geri kalanını eziyordu: 4xl puan, beş satırlık yıldız
  dağılımı, etiket çubukları ve her yorum için ayrı bir gölgeli kart. Tek bir yorumu olan
  bir profilde bile ekranın yarısını kaplıyordu.

  Üç karar küçültmeyi yapıyor:
    1. ÖZET TEK SATIRA İNDİ. Ortalama, adet ve iki alt puan yan yana; yıldız dağılımı ve
       etiket çubukları katlanır hâle geldi (varsayılan KAPALI). Bunlar "derine bak"
       verisi — profile ilk bakışta gereken şey değil.
    2. YORUMLAR KART DEĞİL, LİSTE. Kart başına gölge + kenarlık + p-5 yerine ince ayraçlı
       satırlar; metin text-sm, üstbilgi text-xs.
    3. LİSTE KAYDIRILABİLİR. max-h ile sınırlanıyor, içinde kayıyor — 50 yorumu olan bir
       eğitmenin profili, 3 yorumu olanınkiyle aynı yüksekliği kaplıyor.

  Sayfalama korundu: kaydırma sayfa İÇİNDE, sayfalar arası geçiş yine düğmelerle.
  İkisini birleştirip sonsuz kaydırma yapmak, sunucudaki sayfalı ucu yeniden yazmayı
  gerektirirdi ve bu iş bir düzen işi, veri işi değil.
*/
function ReviewsSection({ reviews, page, onPage }) {
  const [detayAcik, setDetayAcik] = useState(false)

  if (reviews.loading) return <Loading label="Değerlendirmeler yükleniyor…" />
  if (reviews.error) return <ErrorBox error={reviews.error} onRetry={reviews.reload} />

  const data = reviews.data
  if (!data || data.reviewCount === 0) {
    return (
      <EmptyState
        title="Henüz değerlendirme yok"
        description="Değerlendirmeler yalnızca tamamlanmış derslerden sonra yazılabilir."
      />
    )
  }

  const maxTag = Math.max(...data.popularTags.map((t) => t.count), 1)
  const detayVar = data.popularTags.length > 0 || data.reviewCount > 0

  return (
    <Card className="!p-4">
      {/* ÖZET ŞERİDİ — tek satır. */}
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <p className="text-lg font-semibold text-slate-900">
          {Number(data.averageScore).toFixed(1)}
          <span className="ml-0.5 text-base text-amber-400">★</span>
        </p>
        <span className="text-xs text-slate-500">{data.reviewCount} değerlendirme</span>

        <span className="hidden h-4 w-px bg-slate-200 sm:block" />

        <ScoreChip label="Anlatım" value={data.averageTeachingScore} />
        <ScoreChip label="Zamanlama" value={data.averagePunctualityScore} />

        {detayVar && (
          <button
            type="button"
            onClick={() => setDetayAcik((v) => !v)}
            className="ml-auto text-xs font-medium text-brand-600 hover:underline"
            aria-expanded={detayAcik}
          >
            {detayAcik ? 'Dağılımı gizle' : 'Dağılımı gör'}
          </button>
        )}
      </div>

      {/* DETAY — katlanır. Yıldız dağılımı ve etiketler. */}
      {detayAcik && (
        <div className="mt-3 grid gap-4 border-t border-slate-100 pt-3 sm:grid-cols-2">
          <div className="space-y-1">
            {[5, 4, 3, 2, 1].map((star) => {
              const count = data.scoreDistribution[star - 1] ?? 0
              const pct = data.reviewCount ? (count / data.reviewCount) * 100 : 0
              return (
                <div key={star} className="flex items-center gap-2 text-xs text-slate-500">
                  <span className="w-5 shrink-0">{star}★</span>
                  <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                    <div className="h-full rounded-full bg-amber-400" style={{ width: `${pct}%` }} />
                  </div>
                  <span className="w-5 shrink-0 text-right tabular-nums">{count}</span>
                </div>
              )
            })}
          </div>

          {data.popularTags.length > 0 && (
            <div className="space-y-1">
              {data.popularTags.map((tag) => (
                <div key={tag.tag} className="flex items-center gap-2 text-xs text-slate-500">
                  <span className="w-32 shrink-0 truncate">{TAG_LABELS[tag.tag] ?? tag.tag}</span>
                  <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                    <div
                      className="h-full rounded-full bg-brand-500"
                      style={{ width: `${(tag.count / maxTag) * 100}%` }}
                    />
                  </div>
                  <span className="w-5 shrink-0 text-right tabular-nums">{tag.count}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/*
        YORUM LİSTESİ — kaydırılabilir.
        max-h-96 (384px) ≈ 4-5 yorum. Kaydırma kutusuna odaklanılabilir olması (tabIndex)
        klavye kullanıcısı için şart: fare tekerleği olmayan biri de listeyi gezebilmeli.
      */}
      {/*
        Alttaki maske, kaydırma sınırında yarım kalan satırı KASITLI gösteriyor. Maskesiz
        hâlde kesilen satır bir çizim hatası gibi duruyordu; solarak biten bir liste ise
        "devamı var" demenin standart yolu. mask-image yalnızca görsel — kaydırma,
        odaklanma ve ekran okuyucu davranışı değişmiyor.
      */}
      <ul
        tabIndex={0}
        style={{
          maskImage: 'linear-gradient(to bottom, black calc(100% - 28px), transparent)',
          WebkitMaskImage: 'linear-gradient(to bottom, black calc(100% - 28px), transparent)',
        }}
        className="mt-3 max-h-96 divide-y divide-slate-100 overflow-y-auto border-t
                   border-slate-100 pb-6 pt-1 focus:outline-none focus:ring-2 focus:ring-brand-100"
        aria-label="Değerlendirme yorumları"
      >
        {data.reviews.items.map((review) => (
          <li key={review.reviewId} className="py-2.5">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
              <span className="font-medium text-slate-800">{review.reviewerDisplayName}</span>
              <span className="text-amber-400">{'★'.repeat(review.score)}</span>
              <span className="text-slate-400">·</span>
              <span className="text-slate-500">{review.topicName}</span>
              {/* Dersin gönüllü olup olmadığı artık gösterilmiyor: her ders aynı ders. */}
              <span className="ml-auto text-slate-400">{formatDateTime(review.createdAtUtc)}</span>
            </div>

            {review.comment && (
              <p className="mt-1 whitespace-pre-wrap text-sm leading-snug text-slate-700">
                {review.comment}
              </p>
            )}

            {review.tags.length > 0 && (
              <div className="mt-1.5 flex flex-wrap gap-1">
                {review.tags.map((tag) => (
                  <span
                    key={tag}
                    className="rounded bg-brand-50 px-1.5 py-0.5 text-[11px] text-brand-700"
                  >
                    {TAG_LABELS[tag] ?? tag}
                  </span>
                ))}
              </div>
            )}
          </li>
        ))}
      </ul>

      {data.reviews.totalPages > 1 && (
        <div className="mt-3 flex items-center justify-between border-t border-slate-100 pt-3">
          <Button variant="secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>
            ← Önceki
          </Button>
          <span className="text-xs text-slate-500">
            {data.reviews.page} / {data.reviews.totalPages}
          </span>
          <Button
            variant="secondary"
            disabled={!data.reviews.hasNextPage}
            onClick={() => onPage(page + 1)}
          >
            Sonraki →
          </Button>
        </div>
      )}
    </Card>
  )
}

/** Özet şeridindeki küçük alt puan. Eski ScoreLine'ın tek satırlık hâli. */
function ScoreChip({ label, value }) {
  if (value == null) return null
  return (
    <span className="text-xs text-slate-500">
      {label} <span className="font-medium text-slate-700">{Number(value).toFixed(1)}</span>
    </span>
  )
}

function ScoreLine({ label, value }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span className="text-sm text-slate-600">{label}</span>
      <span className="font-medium text-slate-800">
        {Number(value).toFixed(1)} <span className="text-amber-400">★</span>
      </span>
    </div>
  )
}
