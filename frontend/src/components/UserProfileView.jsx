import { useState } from 'react'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { formatDateTime } from '../lib/format'
import { Avatar } from './Avatar'
import { Badge, Button, EmptyState, ErrorBox, Loading } from './ui'
import { CamKart } from './SayfaZemini'
import { GrafikIkonu, KepIkonu, TakvimIkonu, YildizIkonu } from './Ikonlar'
import { SubjectBadges } from './SubjectBadges'
import { UniversiteRozetleri } from './UniversiteRozetleri'
import { ToplulukRozetleri } from './ToplulukRozetleri'
import { YonetimRozeti } from './YonetimRozeti'
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

  /*
    DEKORATİF ŞERİT KALDIRILDI (bir önceki hâlde burada, kartların arkasında, üst 12 rem'i
    boyayan bir brand-100 → şeffaf geçiş vardı). İki nedeni var:

      1. Ürün kararı: proje sahibi o şeridi "ufak ve basit mavilik" diye tarif etti ve
         yerine sayfanın tamamını kapsayan bir zemin istedi.
      2. Teknik: şerit BU bileşenin içindeydi, yani yalnızca profil görünümünün üstünü
         boyuyordu; sayfa başlığı ("Profilim" + düğmeler) ve sayfanın altı dışarıda
         kalıyordu. Zemin artık Profile.jsx'te (SayfaZemini, `zengin`) ve tüm sayfayı
         kapsıyor — burada ikinci bir dekoratif katman olsaydı ikisi üst üste binerdi.

    Sarmalayıcıdan `relative` de düştü: konumlandıracak mutlak çocuk kalmadı.
  */
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

      {/*
        ÜNİVERSİTE ROZETLERİ yalnızca üniversite bilgisi olan profilde.

        İki şerit birden görünebilir ve bu bir tutarsızlık değil: branş rozetleri "hangi
        derste ne kadar anlattı", görüşme rozeti "toplamda ne kadar görüştü" diyor. Aynı
        kişide ikisi de doğru olabilir. Üniversite bilgisi olmayan profilde ikinci şerit
        hiç çizilmiyor; üniversite bilgisi olup hiç oturumu olmayanda ise bileşen kendini
        gizliyor (bkz. UniversiteRozetleri).
      */}
      {p.university && <UniversiteRozetleri userId={userId} />}

      {/*
        TOPLULUK ROZETLERİ — forumda alınan toplam yukarı oy (100/500/1000).

        Bir süre gizliydi (2026-08-27): forum erişilemezken profilde "100 oy → Topluluk
        Üyesi" merdiveni göstermek, var olmayan bir bölüme davet etmek olurdu. Aynı gün
        forum yayına alındı ve profil ucu `communityUpvotes` döndürmeye başladı.

        Sayaç SUNUCUDAN geliyor ve kaldırılmış/perdeli içeriğin oyunu saymıyor — yani
        kural ihlaliyle toplanan oy rozet kazandırmıyor (bkz. ProfileQueries).
      */}
      <ToplulukRozetleri oy={p.communityUpvotes} kendiProfilim={p.isSelf} />

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
  /*
    CAM YÜZEY. Kart artık opak beyaz değil, CamKart: zeminin mesh havuzları kartın
    KENARLARINDAN ve blur'ından okunuyor, metnin arkası pratik olarak beyaz kalıyor
    (ui.jsx'teki Card'a DOKUNULMADI — orası uygulamanın opak varsayılanı).

    DOLGU BİR KADEME AÇILDI (p-6 → p-7, sm'de p-8 → p-10): vitrin sayfasının en üst
    kartı ve tek portresi burada. Cam yüzeyde dar dolgu, kartı zeminin üstünde "yapışık"
    gösteriyor; boşluk, camın işini görünür kılan şey.
  */
  return (
    <CamKart className="p-7 text-center sm:p-10 sm:text-left">
      <div className="flex flex-col items-center gap-6 sm:flex-row sm:items-start sm:gap-8">
        {/*
          avatarUrl bir DEPO ANAHTARI, gezinilebilir bir adres değil — doğrudan <img src>
          olarak verilemez. Avatar bileşeni görseli yetkili uçtan blob olarak çeker ve
          önbelleğe alır; fotoğraf yoksa baş harflere düşer.

          HALKA ARTIK MARKA TONUNDA (ring-brand-200, eskiden ring-white): beyaz halka
          beyaz kartın üstünde görünmüyordu, yalnızca gölgeyi taşıyordu. Açık mavi halka
          portreyi gerçekten çerçeveliyor ve sayfadaki tek marka-rengi olmayan büyük
          öğeyi (fotoğrafı) kimliğe bağlıyor. shadow-brand-500/10: gölgeye çok hafif
          marka tonu — %10 opaklık bilinçli, daha koyusu "parlama" efektine kayar ve
          bu projede glow yasak.
        */}
        <Avatar
          userId={profile.userId}
          name={profile.displayName}
          size="xl"
          buyutulebilir
          className="shrink-0 shadow-lg shadow-brand-500/10 ring-4 ring-brand-200"
        />

        <div className="min-w-0 flex-1">
          {/*
            ADIN YANINDAKİ SEVİYE ROZETİ KALDIRILDI (2026-08-24).

            Aynı bilgi bu ekranda ÜÇ kez duruyordu: üst barda (her sayfada görünen rozet),
            adın yanında ve hemen altındaki sayaç şeridinde ("8. Seviye · 4300 puan ·
            sonraki seviyeye 1200"). Üçünden yalnızca sayaç şeridi bir şey EKLİYOR —
            seviyenin kaçıncı olduğunu, puanı ve sonraki eşiğe kalanı birlikte söylüyor.
            Adın yanındaki rozet aynı sayıyı üçüncü kez tekrarlıyor, üstelik en az bilgiyle.

            Tekrar zararsız değil: başlık satırı ad + rozet + (dar ekranda) sarma taşıyor
            ve rozet, adın kendisiyle vurgu yarışına giriyordu.
          */}
          {/*
            AD, SAYFANIN EN BÜYÜK METNİ. Eskiden 2xl idi ve sayfa başlığıyla ("Profilim")
            aynı boydaydı: iki farklı şey aynı sesle konuşuyordu. Bir kademe büyüyünce
            hiyerarşi netleşiyor — başlık sayfayı, bu satır KİŞİYİ adlandırıyor.
            Dar ekranda 3xl'de kalıyor; 4xl uzun adlarda 112px avatarın altında üç
            satıra sarıyordu.
          */}
          {/*
            YÖNETİM ROZETİ ADIN YANINDA, altında değil: forumda rozetli bir yorum
            görüp adına tıklayan kullanıcının doğrulamak istediği şey tam olarak bu ve
            aradığı yerde bulmalı. Aşağı kaydırma gerektiren bir rozet, doğrulama
            adımını başarısız kılar.

            Dar ekranda ad ortalı olduğu için rozet de ortalanıyor (justify-center);
            sm üstünde ikisi de sola yaslanıyor.
          */}
          <div className="flex flex-wrap items-center justify-center gap-x-3 gap-y-2 sm:justify-start">
            <h1 className="text-center text-3xl font-bold leading-tight tracking-tight text-slate-900 sm:text-left sm:text-4xl">
              {profile.displayName}
            </h1>
            {profile.isStaff && <YonetimRozeti />}
          </div>

          {/* OKUL SATIRI: adın hemen altında ve marka renginde değil nötr — kimlik
              bilgisi, vurgu değil. Biri boşsa ayraç da düşüyor.

              slate-500 → slate-600: kart artık cam ve altından zemin lekesi geçebiliyor;
              projede gövde metninin tabanı slate-600 (AA eşiği testle korunuyor). */}
          {(profile.university || profile.department) && (
            <p className="mt-2 text-sm font-medium text-slate-600">
              {[profile.university, profile.department].filter(Boolean).join(' · ')}
            </p>
          )}

          {/* BİYOGRAFİ: profilin tek serbest metni, o yüzden en okunur tipografi
              buranın. leading-relaxed + max-w-prose: satır uzunluğu göz için sınırlı,
              kart genişlese bile metin okunur kalıyor. Adın büyümesiyle birlikte üstteki
              boşluk da açıldı; aksi halde ad, altındaki iki satıra yapışıyor. */}
          {profile.bio && (
            <p className="mx-auto mt-4 max-w-prose whitespace-pre-wrap text-[15px] leading-relaxed text-slate-700 sm:mx-0">
              {profile.bio}
            </p>
          )}

          <SayacSeridi profile={profile} />
        </div>
      </div>
    </CamKart>
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
  /*
    Her sayaca bir ikon: rakam tek başına "kaç" diyor ama "neyin kaçı" olduğunu
    okumak için etiketi beklemek gerekiyordu. İkon, göz etikete inmeden sayacın
    konusunu söylüyor. Renk text-brand-600: süs değil işaret, ama rakamla da
    (text-slate-900) vurgu yarışına girmiyor.
  */
  const kalemler = [
    { deger: profile.taughtSessionCount, etiket: 'ders anlattı', Ikon: KepIkonu },
    {
      deger: profile.ratingCount > 0 ? `${Number(profile.averageRating).toFixed(1)} ★` : '—',
      etiket: profile.ratingCount > 0 ? `${profile.ratingCount} değerlendirme` : 'değerlendirme yok',
      Ikon: YildizIkonu,
    },
    { deger: seviyeEtiketi(seviyeHesapla(profile)), etiket: seviyeIlerlemeMetni(profile), Ikon: GrafikIkonu },
    { deger: new Date(profile.joinedAtUtc).getFullYear(), etiket: 'katılım', Ikon: TakvimIkonu },
  ]

  /*
    TEK SATIRA GEÇİŞ sm'DE DEĞİL lg'DE. Kırılım önce sm (640px) idi ve 689px'lik bir
    ekranda ölçüldü: dört sütun ~140px'e düşünce "4 değerlendirme" iki satıra,
    "4300 puan · sonraki seviyeye 1200" üç satıra sarıyordu — sayaçlar hizasız, şerit
    dengesiz görünüyordu. En uzun etiket ilerleme metni ve o metin SUNUCUDAN geliyor
    (uzunluğu puana göre değişiyor), yani sabit bir genişlik varsayılamaz. 2×2 düzen
    lg'ye kadar sürüyor; orada dört sütuna gerçekten yer var.

    DİKEY AYRAÇLAR (lg:divide-x) KALDIRILDI: sayaçlar artık kendi pastel zeminli
    kutucuklarında (bg-brand-50/60) ve ayrımı kutucuk kenarları yapıyor. Zeminli
    kutuların ARASINA bir de çizgi çekmek aynı işi iki kez yapmak olurdu. Zemin /60
    opaklıkta: tam opak brand-50, dört kutu yan yana gelince şeridi kartın geri
    kalanından koparıp ayrı bir panel gibi gösteriyordu.
  */
  return (
    <dl className="mt-6 grid grid-cols-2 gap-3 border-t border-slate-200/70 pt-5 lg:grid-cols-4">
      {kalemler.map((k) => (
        /*
          OPAKLIK /60 → /70 ve ince bir marka halkası: kutucuklar artık CAM bir kartın
          içinde duruyor ve altlarından zeminin mesh havuzu geçebiliyor. /60'ta kutunun
          kenarı belirsizleşip dört sayaç tek bir bulanık alan gibi okunuyordu. Renk
          kimliği aynı kaldı (brand-50 zemin, brand-600 ikon) — değişen yalnızca kutunun
          kendini zeminden ayırma gücü.
        */
        <div
          key={k.etiket}
          className="rounded-xl bg-brand-50/70 p-3 ring-1 ring-inset ring-brand-100/70"
        >
          <dt className="sr-only">{k.etiket}</dt>
          <dd>
            {/* mx-auto sm:mx-0 — kart dar ekranda ortalı, sm üstünde sola yaslı
                (bkz. ProfileHeader); ikon blok elemanı olduğu için text-center'a
                uymuyor, hizayı kendi taşımak zorunda. */}
            <k.Ikon className="mx-auto h-4 w-4 text-brand-600 sm:mx-0" />
            <span className="mt-1.5 block text-lg font-bold leading-tight text-slate-900">{k.deger}</span>
            <span className="mt-0.5 block text-xs leading-snug text-slate-600">{k.etiket}</span>
          </dd>
        </div>
      ))}
    </dl>
  )
}


function TopicPanel({ title, tone, topics, emptyText }) {
  return (
    <CamKart>
      <p className="mb-2 text-sm font-medium text-slate-700">{title}</p>
      {topics?.length ? (
        <div className="flex flex-wrap gap-1.5">
          {/*
            KONU ADI VE DERS TEK METİN AKIŞI — iki flex öğesi değil.

            Badge `inline-flex items-center` (ui.jsx). İçeride iki ayrı çocuk olunca
            bunlar iki flex ÖĞESİ oluyordu: uzun bir konu adı ("Geometrik Kavramlar
            (Nokta, Doğru, Düzlem)") telefonda kendi içinde iki satıra kırılıyor, ders
            adı ise yanında dikey ortalanmış tek bir parça olarak kalıyordu — etiketin
            sağında kocaman bir boşluk ve tek başına asılı bir "· Geometri".

            Tek `<span>` içine alınca ikisi aynı satır akışının parçası oluyor ve normal
            metin gibi sarıyor. Nokta ayracı da artık `ml-1` yerine gerçek bir boşluk
            karakteriyle geliyor: kırılma noktası oradaysa satır oradan bölünsün.
          */}
          {topics.map((topic) => (
            <Badge key={topic.topicId} tone={tone}>
              <span>
                {topic.topicName}
                <span className="opacity-70"> · {topic.subjectName}</span>
              </span>
            </Badge>
          ))}
        </div>
      ) : (
        <p className="text-sm text-slate-600">{emptyText}</p>
      )}
    </CamKart>
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
    <CamKart className="!p-0 overflow-hidden">
      {/*
        ÖZET — hafif renkli bir başlık şeridi. Kartın içinde zeminle aynı tonda bir özet,
        yorum listesinden ayrılmıyordu; slate zemin ikisi arasına görünmez bir çizgi
        çekiyor ve "bu bölüm bir başlık" diyor.

        TAM OPAK slate-50 DEĞİL /70: kart artık cam ve tam opak bir şerit, camın içinde
        pencereyi kapatan bir levha gibi duruyordu. Yarı saydamda zemin şeridin altından
        da geçiyor, ayrım ise duruyor — metnin arkası (beyaz kart + slate tonu) yine AA
        eşiğinin çok üstünde.
      */}
      <div className="grid gap-4 border-b border-slate-200/80 bg-slate-50/70 p-4 sm:grid-cols-[auto,1fr] sm:gap-6 sm:p-5">
        <div className="flex items-center gap-3 sm:flex-col sm:items-start sm:gap-1">
          <p className="text-3xl font-bold leading-none text-slate-900">
            {Number(data.averageScore).toFixed(1)}
          </p>
          <div>
            <Yildizlar deger={data.averageScore} />
            <p className="mt-1 text-xs text-slate-600">{data.reviewCount} değerlendirme</p>
          </div>
        </div>

        <div className="space-y-2 sm:border-l sm:border-slate-200 sm:pl-6">
          <MetrikCubugu label="Anlatım" value={data.averageTeachingScore} />
          <MetrikCubugu label="Zamanlama" value={data.averagePunctualityScore} />

          {/*
            Dokunma hedefi: metin 12px olduğu için düğmenin kendisi de 16px yüksekti ve
            telefonda ıskalanıyordu. `min-h-11` (44px) dokunma alanını büyütür, punto
            aynı kalır — burada küçük punto bilinçli, ikincil bir eylem.

            lg:min-h-0: eşik projede DOKUNMAYA bağlı, genişliğe değil (bkz. CLAUDE.md).
            Fare olan boyutlarda 44px'lik boşluk, metrik çubuklarıyla düğmenin arasını
            gereksiz açardı.

            -mb-2: eklenen yükseklik kartın alt dolgusunu şişirmesin diye geri alınıyor;
            büyüyen şey dokunma alanı, düzenin ritmi değil.
          */}
          <button
            type="button"
            onClick={() => setDetayAcik((v) => !v)}
            className="-mb-2 flex min-h-11 items-center text-xs font-medium text-brand-700
                       transition hover:text-brand-800 hover:underline lg:mb-0 lg:min-h-0"
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
                  <div key={star} className="flex items-center gap-2 text-xs text-slate-600">
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
              {/* slate-400 → slate-500: 400 beyaz üzerinde 3.0:1 veriyor ve cam yüzeyde
                  altından geçen zeminle birlikte iyice siliniyordu. 500 (4.76:1) hâlâ
                  ikincil okunuyor ama AA'yı geçiyor. */}
              <span className="ml-auto text-xs text-slate-600">
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

            <p className="mt-1.5 text-xs text-slate-600">{review.topicName}</p>
          </li>
        ))}
      </ul>

      {data.reviews.totalPages > 1 && (
        <div className="flex items-center justify-between border-t border-slate-200/80 px-4 py-3 sm:px-5">
          <Button variant="secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>
            ← Önceki
          </Button>
          <span className="text-xs text-slate-600">
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
    </CamKart>
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
