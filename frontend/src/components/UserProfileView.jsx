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
      {/* StatsRow KALDIRILDI: dört ayrı sayaç kartı, profil kartının İÇİNDEKİ tek
          şeride indi (bkz. SayacSeridi). Yüzey sayısı beşten ikiye düştü ve sayfa bir
          gösterge paneli değil, bir kişi gibi okunmaya başladı. */}
      <ProfileHeader profile={p} />

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
 * PROFİL BAŞLIĞI — Instagram düzeni.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * ÜÇÜNCÜ HÂLİ (2026-08-24). Önce dekoratif bir bant + taşan avatar + rozet duvarı
 * vardı; kart ekranın yarısını yiyordu. Sonra hepsi tek satıra indirildi: 48px avatar,
 * yanında ad, altında dört ayrı sayaç kartı. O hâl kompakttı ama profil olmaktan
 * çıkmıştı — kullanıcının kendisi, sayfadaki en küçük öğeydi.
 *
 * Şimdiki düzen sıralamayı tersine çeviriyor: ÖNCE KİŞİ, sonra sayılar.
 *   • Avatar 112–128px ve tek başına duran ilk şey. Tıklanınca tam ekran açılıyor.
 *   • Hemen altında ad + seviye, sonra okul, sonra biyografi.
 *   • Sayaçlar dört ayrı karttan çıkıp AYNI kartın içinde tek bir şeride indi.
 *
 * DÖRT KART NEDEN KALDIRILDI: her biri kendi kenarlığı, gölgesi ve dolgusuyla ayrı bir
 * yüzeydi; profil sayfası "bir kişi" değil "bir gösterge paneli" gibi okunuyordu. Aynı
 * dört sayı tek şeritte, ince ayraçlarla, kartın içinde duruyor — bilgi kaybı yok,
 * yüzey sayısı dörtten bire indi.
 *
 * DAR EKRANDA ORTALI, GENİŞTE SOLA YASLI: mobilde ortalanmış bir portre doğal
 * (Instagram da öyle yapıyor), masaüstünde ise ortalamak satırları sayfanın ortasında
 * asılı bırakıyor ve okuma başlangıcı her satırda kayıyor.
 * ─────────────────────────────────────────────────────────────────────────────
 */
function ProfileHeader({ profile }) {
  return (
    <Card className="p-6 text-center sm:p-8 sm:text-left">
      <div className="flex flex-col items-center gap-5 sm:flex-row sm:items-start sm:gap-7">
        {/*
          avatarUrl bir DEPO ANAHTARI, gezinilebilir bir adres değil — doğrudan <img src>
          olarak verilemez. Avatar bileşeni görseli yetkili uçtan blob olarak çeker ve
          önbelleğe alır; fotoğraf yoksa baş harflere düşer.

          ring-4 + ring-white + gölge: portreyi zeminden ayıran ince bir halka. Kartın
          kendisi zaten beyaz olduğu için halka görünmüyor, gölgeyi taşıyor.
        */}
        <Avatar
          userId={profile.userId}
          name={profile.displayName}
          size="xl"
          buyutulebilir
          className="shrink-0 shadow-lg ring-4 ring-white"
        />

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center justify-center gap-x-2.5 gap-y-1.5 sm:justify-start">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900">
              {profile.displayName}
            </h1>
            {/* Üst bardaki rozetle AYNI bileşen, küçük/açık varyantı: burada rozet
                başlık değil, başlığa iliştirilen bir nitelik (bkz. SeviyeRozeti.jsx). */}
            <SeviyeRozeti kaynak={profile} boyut="sm" ton="acik" className="shrink-0" />
          </div>

          {/* OKUL SATIRI: adın hemen altında ve marka renginde değil nötr — kimlik
              bilgisi, vurgu değil. Biri boşsa ayraç da düşüyor. */}
          {(profile.university || profile.department) && (
            <p className="mt-1.5 text-sm font-medium text-slate-500">
              {[profile.university, profile.department].filter(Boolean).join(' · ')}
            </p>
          )}

          {/* BİYOGRAFİ: profilin tek serbest metni, o yüzden en okunur tipografi
              buranın. leading-relaxed + max-w-prose: satır uzunluğu göz için sınırlı,
              kart genişlese bile metin okunur kalıyor. */}
          {profile.bio && (
            <p className="mx-auto mt-3 max-w-prose whitespace-pre-wrap text-[15px] leading-relaxed text-slate-700 sm:mx-0">
              {profile.bio}
            </p>
          )}

          <SayacSeridi profile={profile} />
        </div>
      </div>
    </Card>
  )
}

/**
 * Sayaç şeridi — dört ayrı kartın yerine tek satır.
 *
 * "Deneyim / 0 dk" alanı bir zamanlar buradaydı ve yeni hesaplarda her zaman "0 dk"
 * yazıyordu: profile giren ilk kişiye söylediği tek şey "bu kullanıcı hiçbir şey
 * yapmamış" oluyordu. Seviye aynı yeri kullanır ama en baştan anlamlı bir şey söyler.
 *
 * İLERLEME SATIRI SUNUCUDAN: "sonraki seviyeye X puan" hesabı `nextLevelAt` üzerinden
 * yapılıyor (bkz. lib/seviye.js). Eşikler istemciye kopyalanmıyor — kopyalansaydı
 * sunucudaki tablo değiştiğinde profil eski hedefi göstermeye devam ederdi.
 *
 * IZGARA, SARAN FLEX DEĞİL. İlk hâl `flex flex-wrap` idi ve dört kalem üç + bir olarak
 * sarıyordu: dördüncü sayaç ("2026 katılım") tek başına alt satırda, hizasız duruyordu.
 * Izgara sarma kararını tarayıcıya bırakmıyor — dar ekranda 2×2, sm üstünde tek satır.
 * Dikey ayraç da yalnızca tek satıra düştüğü boyutta açılıyor; 2×2 düzende ayraçlar
 * ızgaranın ortasında havada kalırdı.
 */
function SayacSeridi({ profile }) {
  const kalemler = [
    { deger: profile.taughtSessionCount, etiket: 'ders anlattı' },
    {
      deger: profile.ratingCount > 0 ? `${Number(profile.averageRating).toFixed(1)} ★` : '—',
      etiket: profile.ratingCount > 0 ? `${profile.ratingCount} değerlendirme` : 'değerlendirme yok',
    },
    { deger: seviyeEtiketi(seviyeHesapla(profile)), etiket: seviyeIlerlemeMetni(profile) },
    { deger: new Date(profile.joinedAtUtc).getFullYear(), etiket: 'katılım' },
  ]

  return (
    <dl className="mt-5 grid grid-cols-2 gap-x-4 gap-y-4 border-t border-slate-100 pt-4 sm:grid-cols-4 sm:divide-x sm:divide-slate-100">
      {kalemler.map((k, i) => (
        <div key={k.etiket} className={i > 0 ? 'sm:pl-4' : ''}>
          <dt className="sr-only">{k.etiket}</dt>
          <dd>
            <span className="block text-lg font-bold leading-tight text-slate-900">{k.deger}</span>
            <span className="mt-0.5 block text-xs leading-snug text-slate-500">{k.etiket}</span>
          </dd>
        </div>
      ))}
    </dl>
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
