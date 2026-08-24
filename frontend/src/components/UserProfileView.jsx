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

/*
  TAG_LABELS KALDIRILDI. Etiketler artık hiçbir yerde gösterilmiyor (bkz. aşağıdaki
  ReviewsSection notu); sözlüğü burada tutmak, okuyana hâlâ çizildiklerini düşündürürdü.
  Sunucu etiketleri toplamaya ve döndürmeye devam ediyor — yalnızca gösterim kalktı.
*/

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
          buyutulebilir
          className="shrink-0 shadow-sm ring-2 ring-white"
        />

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <h1 className="truncate text-lg font-bold text-slate-900 sm:text-xl">
              {profile.displayName}
            </h1>
            {/*
              Üst bardaki rozetle AYNI bileşen ama küçük/açık varyantı: burada rozet
              başlık değil, başlığa iliştirilen bir nitelik. Dolgun varyant 20px’lik
              ismin yanında onu eziyordu (bkz. SeviyeRozeti.jsx).
            */}
            <SeviyeRozeti kaynak={profile} boyut="sm" ton="acik" className="shrink-0" />
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
  DEĞERLENDİRMELER — özet şeridi + yorum listesi.

  Bu blok bir zamanlar profilin geri kalanını eziyordu: 4xl puan, beş satırlık yıldız
  dağılımı, etiket çubukları ve her yorum için ayrı bir gölgeli kart. Tek yorumu olan bir
  profilde bile ekranın yarısını kaplıyordu. Küçültüldü ve öyle kalıyor.

  ─────────────────────────────────────────────────────────────────────────────
  ETİKETLER KALDIRILDI (2026-08-24, ürün kararı).

  Hem yorum satırlarındaki rozetler ("Konuya çok hakim", "Tekrar ders alırım") hem de
  özetteki etiket histogramı gitti. Gerekçe: aynı bilgiyi üç kez söylüyorlardı —
  yıldız zaten memnuniyeti, yorum metni zaten nedenini anlatıyor. Aradaki etiket şeridi
  yalnızca satırı kalabalıklaştırıp gözü asıl okunacak şeyden (kullanıcının kendi
  cümlesinden) uzaklaştırıyordu.

  VERİ DURUYOR: sunucu etiketleri toplamaya ve döndürmeye devam ediyor
  (`review.tags`, `data.popularTags`), yalnızca GÖSTERİM kalktı. Karar geri alınırsa
  tek yapılacak şey burada yeniden çizmek; hiçbir kayıt kaybolmadı.
  ─────────────────────────────────────────────────────────────────────────────

  PUAN ÖZETİ ÇUBUKLA. Eskiden üç sayı yan yana duruyordu ("4.8 ★  Anlatım 4.8
  Zamanlama 4.3") ve aralarındaki farkı görmek için okumak gerekiyordu. Çubuk, farkı
  OKUMADAN gösteriyor: zamanlamanın anlatımdan geride kaldığı tek bakışta belli oluyor.
  Sayı da duruyor — çubuk kesin değeri veremez, sayı veremediğini gösteremez.

  Yıldız dağılımı hâlâ katlanır ve varsayılan KAPALI: "derine bak" verisi, profile ilk
  bakışta gereken şey değil.
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

  return (
    <Card className="!p-0 overflow-hidden">
      {/*
        ÖZET — hafif renkli bir başlık şeridi. Beyaz kartın içinde beyaz bir özet,
        yorum listesinden ayrılmıyordu; slate-50 zemin ikisi arasına görünmez bir
        çizgi çekiyor ve "bu bölüm bir başlık" diyor.
      */}
      <div className="grid gap-4 border-b border-slate-200/80 bg-slate-50 p-4 sm:grid-cols-[auto,1fr] sm:gap-6 sm:p-5">
        <div className="flex items-center gap-3 sm:flex-col sm:items-start sm:gap-1">
          <p className="text-3xl font-bold leading-none text-slate-900">
            {Number(data.averageScore).toFixed(1)}
          </p>
          <div>
            <Yildizlar deger={data.averageScore} />
            <p className="mt-1 text-xs text-slate-500">{data.reviewCount} değerlendirme</p>
          </div>
        </div>

        <div className="space-y-2 sm:border-l sm:border-slate-200 sm:pl-6">
          <MetrikCubugu label="Anlatım" value={data.averageTeachingScore} />
          <MetrikCubugu label="Zamanlama" value={data.averagePunctualityScore} />

          <button
            type="button"
            onClick={() => setDetayAcik((v) => !v)}
            className="text-xs font-medium text-brand-700 transition hover:text-brand-800 hover:underline"
            aria-expanded={detayAcik}
          >
            {detayAcik ? 'Dağılımı gizle' : 'Yıldız dağılımını gör'}
          </button>

          {detayAcik && (
            <div className="space-y-1 pt-1">
              {[5, 4, 3, 2, 1].map((star) => {
                const count = data.scoreDistribution[star - 1] ?? 0
                const pct = data.reviewCount ? (count / data.reviewCount) * 100 : 0
                return (
                  <div key={star} className="flex items-center gap-2 text-xs text-slate-500">
                    <span className="w-5 shrink-0 tabular-nums">{star}★</span>
                    <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-200">
                      <div className="h-full rounded-full bg-amber-400" style={{ width: `${pct}%` }} />
                    </div>
                    <span className="w-4 shrink-0 text-right tabular-nums">{count}</span>
                  </div>
                )
              })}
            </div>
          )}
        </div>
      </div>

      {/*
        YORUM LİSTESİ — kaydırılabilir.
        max-h-96 (384px) ≈ 4-5 yorum. Kaydırma kutusuna odaklanılabilir olması (tabIndex)
        klavye kullanıcısı için şart: fare tekerleği olmayan biri de listeyi gezebilmeli.

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
        className="max-h-96 divide-y divide-slate-100 overflow-y-auto px-4 pb-6 pt-1 focus:outline-none
                   focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-200 sm:px-5"
        aria-label="Değerlendirme yorumları"
      >
        {data.reviews.items.map((review) => (
          <li key={review.reviewId} className="py-3">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span className="text-sm font-semibold text-slate-800">
                {review.reviewerDisplayName}
              </span>
              <Yildizlar deger={review.score} kucuk />
              <span className="ml-auto text-xs text-slate-400">
                {formatDateTime(review.createdAtUtc)}
              </span>
            </div>

            {/* Konu adı yorumun ALTINDA ve soluk: yorumu okuyan kişi önce ne yazdığına
                bakıyor, hangi derste yazıldığına sonra. Üst satırda dururken adın ve
                yıldızın arasına giriyor, üçü de aynı ağırlıkta görünüyordu. */}
            {review.comment && (
              <p className="mt-1.5 whitespace-pre-wrap text-sm leading-relaxed text-slate-700">
                {review.comment}
              </p>
            )}

            <p className="mt-1.5 text-xs text-slate-400">{review.topicName}</p>
          </li>
        ))}
      </ul>

      {data.reviews.totalPages > 1 && (
        <div className="flex items-center justify-between border-t border-slate-200/80 px-4 py-3 sm:px-5">
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

/**
 * Beş yıldızlık satır. Kesirli değerde son yıldız KISMİ dolar (4.8 → dördüncü yıldızın
 * tamamı, beşincinin %80'i).
 *
 * NEDEN YUVARLANMIYOR: 4.5 ile 4.9 arasındaki fark, beş yıldızın tamamını yakan bir
 * yuvarlamada kayboluyor ve iki farklı eğitmen aynı görünüyor. Kısmi dolgu, sayıya
 * bakmadan da sıralama yapılabilmesini sağlıyor.
 *
 * Teknik: aynı beş yıldız iki kez basılıyor — altta gri, üstte sarı — ve üstteki
 * `width: %` ile kırpılıyor. Yıldız başına ayrı hesap yapmaktan basit ve yarım yıldız
 * SVG'si gerektirmiyor.
 */
function Yildizlar({ deger, kucuk = false }) {
  const oran = Math.max(0, Math.min(100, (Number(deger) / 5) * 100))
  const boyut = kucuk ? 'text-xs' : 'text-base'

  return (
    <span
      className={`relative inline-block select-none leading-none ${boyut}`}
      role="img"
      aria-label={`5 üzerinden ${Number(deger).toFixed(1)}`}
    >
      <span className="text-slate-300" aria-hidden="true">
        ★★★★★
      </span>
      <span
        className="absolute inset-y-0 left-0 overflow-hidden whitespace-nowrap text-amber-400"
        style={{ width: `${oran}%` }}
        aria-hidden="true"
      >
        ★★★★★
      </span>
    </span>
  )
}

/**
 * Alt metrik çubuğu (Anlatım / Zamanlama).
 *
 * Etiket sabit genişlikte (w-20): iki çubuk alt alta gelince başlangıç noktaları
 * hizalanmazsa karşılaştırma zorlaşıyor — çubukların işi tam olarak karşılaştırılmak.
 */
function MetrikCubugu({ label, value }) {
  if (value == null) return null
  const oran = Math.max(0, Math.min(100, (Number(value) / 5) * 100))

  return (
    <div className="flex items-center gap-3">
      <span className="w-20 shrink-0 text-xs text-slate-600">{label}</span>
      <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-200">
        <div className="h-full rounded-full bg-brand-500 transition-[width] duration-500" style={{ width: `${oran}%` }} />
      </div>
      <span className="w-7 shrink-0 text-right text-xs font-semibold tabular-nums text-slate-700">
        {Number(value).toFixed(1)}
      </span>
    </div>
  )
}
