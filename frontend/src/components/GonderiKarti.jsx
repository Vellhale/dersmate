import { useLayoutEffect, useRef, useState } from 'react'
import { Avatar } from './Avatar'
import { PersonLink } from './PersonLink'
import { CamKart } from './SayfaZemini'
import { SeviyeRozeti } from './SeviyeRozeti'
import { YonetimRozeti } from './YonetimRozeti'
import { Button, ErrorBox, Loading } from './ui'
import { BayrakIkonu, MesajIkonu, OyOkuIkonu, UyariIkonu } from './Ikonlar'
import { ETIKET_ADI, ETIKET_ANAHTARI, ETIKET_TONU, yasDakika, zamanKisalt } from '../lib/forum'

/*
  ══════════════════════════════════════════════════════════════════════════════
  GÖNDERİ KARTI — Topluluk akışının kartı, İKİ TARZDA (2026-09-26).

  pages/Topluluk.jsx'ten çıkarıldı. Sayfada yalnızca durum, istekler, gönderi kutusu,
  sıralama şeridi ve modallar kaldı; kartın kendisi ve onun parçaları (oy rayı, şikayet
  düğmesi, yorum ipliği, yazar satırı, etiket pili) burada.

  ─── TARZ TEK YERDEN ─────────────────────────────────────────────────────────
  AKIS_TARZI akışın görünümünü belirleyen TEK anahtar. 'reddit' yazılırsa eski kart
  (sol oy rayı, üst satırda etiket + yazar + zaman) geri gelir; iki tarz aynı prop
  sözleşmesini alıyor, sayfada başka hiçbir şey değişmez. Mobil depoda aynı adlarla
  (AKIS_TARZI, TARZLAR, GonderiKarti) aynı yapı var.

  Geliştirme sunucusunda (`npm run dev`) adrese `?akis=reddit` ya da `?akis=instagram`
  eklenerek iki tarz yan yana sınanabilir. Üretim derlemesinde bu okuma YOK
  (import.meta.env.DEV): tarz bir ürün kararı, adresle değiştirilebilen bir tercih değil.
  Kullanılmayan tarzın kodu çürümesin diye ara sıra bu yolla açılıp bakılmalı.

  ─── NEDEN INSTAGRAM TARZI ───────────────────────────────────────────────────
  Eski kartta her gönderinin anatomisi aynıydı (sol ray + metin blokları) ve kartları
  birbirinden yalnızca aradaki boşluk ile cam kartın kenarı ayırıyordu; akış tek düze
  okunuyordu. Yeni kartın iç ayrımı var: avatarlı yazar başlığı → gövde → çizgiyle
  ayrılmış eylem satırı → ilk yorumun önizlemesi. Göz her kartta aynı sırayla
  "kim / ne / ne düşünülüyor"u buluyor.

  Oy SOLDAN eylem satırına indi. Eski düzenin gerekçesi ("oy içerikten önce okunur")
  sıralaması oya bağlı bir akışta doğruydu; ama bu forumun asıl okuma birimi SORU ve
  soruyu soranın kim olduğu (seviyesi, yönetimden mi) cevabı okumadan önce bilinmesi
  gereken şey. Sayı hâlâ görünür, yalnızca başlıktan sonra geliyor.

  ⚠️ BAŞLIK BAĞLANTI DEĞİL: ayrı bir gönderi sayfası yok. Var olmayan bir yere giden
  bağlantı kırık bir vaat olurdu; yorumlar kartın İÇİNDE açılıyor.
  ══════════════════════════════════════════════════════════════════════════════
*/

export const AKIS_TARZI = 'instagram'

const TARZLAR = { instagram: InstagramKarti, reddit: RedditKarti }

function akisTarzi() {
  if (import.meta.env.DEV && typeof window !== 'undefined') {
    const istenen = new URLSearchParams(window.location.search).get('akis')
    if (istenen && TARZLAR[istenen]) return istenen
  }
  return AKIS_TARZI
}

/**
 * Dağıtıcı. İncelemedeki perde İKİ TARZ İÇİN ORTAK ve burada: perdenin metni ve
 * davranışı tarza göre değişmemeli (bir tarzda perdesiz kalan gönderi, perdenin bütün
 * gerekçesini boşa çıkarırdı).
 *
 * Prop sözleşmesi (iki tarz aynı): gonderi, benimUserId, yorumDurumu, onOy,
 * yorumlarAcik, onYorumlar, onYorumYaz, onYorumOy, gizliAcik, onGizliAc, onSikayet.
 */
export function GonderiKarti(props) {
  const { gonderi, gizliAcik, onGizliAc } = props

  if (gonderi.underReview && !gizliAcik) {
    return <IncelemePerdesi gonderi={gonderi} onGizliAc={onGizliAc} />
  }

  const Kart = TARZLAR[akisTarzi()] ?? InstagramKarti
  return <Kart {...props} />
}

/* ─── İNCELEME PERDESİ VE ŞERİDİ (iki tarz için ortak) ─────────────────────── */

/*
  İNCELEMEDEKİ GÖNDERİ AKIŞTA KAPALI GELİR.

  Silinmiyor, perdeleniyor. İkisi arasındaki fark moderasyonun görünürlüğü: sessizce
  silinen içerik, hem yazarına hem okuyanına hiçbir şey söylemez ve "burada sansür
  var mı" sorusunu cevaplanamaz hâle getirir. Perde ise sebebi yazıyor, sayıyı
  veriyor ve kararı okuyana bırakıyor. Yazar ve başlık perdeli hâlde GİZLİ.
*/
export function IncelemePerdesi({ gonderi, onGizliAc }) {
  const etiketAnahtari = ETIKET_ANAHTARI[gonderi.tag] ?? gonderi.tag

  return (
    <CamKart className="border-amber-200/80 bg-amber-50/70 p-4">
      <div className="flex items-start gap-3">
        <UyariIkonu className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
        <div className="min-w-0">
          <p className="text-sm font-semibold text-slate-900">Bu gönderi incelemede</p>
          <p className="mt-1 text-sm leading-relaxed text-slate-700">
            {gonderi.reportCount} kişi topluluk kurallarını ihlal ettiğini bildirdi. Moderasyon
            sonuçlanana kadar akışta kapalı tutuluyor.
          </p>
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={onGizliAc}
              className="min-h-11 rounded-lg border border-amber-300 bg-white px-3 text-xs
                         font-semibold text-amber-900 transition hover:bg-amber-100 lg:min-h-9"
            >
              Yine de göster
            </button>
            <span className="text-xs text-slate-600">Etiket: {ETIKET_ADI[etiketAnahtari]}</span>
          </div>
        </div>
      </div>
    </CamKart>
  )
}

/* Perde açıldıysa uyarı kartın ÜSTÜNDE kalıyor: kullanıcı "yine de göster"e bastığı
   anı unutabilir, içeriğin durumu unutulmamalı. Kap overflow-hidden olduğu için şerit
   kartın üst köşelerine kendiliğinden uyuyor; ayrıca yuvarlatılmıyor. */
export function IncelemeSeridi({ reportCount }) {
  return (
    <div className="flex items-center gap-2 border-b border-amber-200 bg-amber-50 px-4 py-2">
      <UyariIkonu className="h-4 w-4 shrink-0 text-amber-600" />
      <p className="text-xs font-medium text-amber-900">
        İncelemede — {reportCount} şikayet aldı, moderasyon sürüyor.
      </p>
    </div>
  )
}

/* ─── INSTAGRAM TARZI (varsayılan) ─────────────────────────────────────────── */

/*
  Kap `overflow-hidden !p-0`. ÜNLEM ŞART: CamKart'ın taban dolgusu p-5 ve derlenen
  CSS'te .p-5 kuralı .p-0'dan SONRA geliyor, yani düz `p-0` hiçbir şeyi ezmiyor (eski
  kart bu yüzden fark edilmeden 20px dış + 16/20px iç dolguyla çiziliyordu). Profil ve
  Derslerim'deki bölünmüş cam kartlar da aynı nedenle `!p-0` kullanıyor.

  Yatay dolgu px-4 / sm:px-5 her bölümde aynı: başlık, gövde, önizleme ve iplik tek bir
  sol hizadan okunuyor. Eylem satırı px-2 / sm:px-3 — 44px'lik düğmelerdeki ikonun
  görünür kenarı böylece metnin hizasına yaklaşıyor.
*/
function InstagramKarti({
  gonderi,
  benimUserId,
  yorumDurumu,
  onOy,
  yorumlarAcik,
  onYorumlar,
  onYorumYaz,
  onYorumOy,
  onSikayet,
}) {
  const etiketAnahtari = ETIKET_ANAHTARI[gonderi.tag] ?? gonderi.tag
  const benimGonderim = gonderi.author?.userId === benimUserId
  const yorumDugmesi = useRef(null)

  /*
    "Tümünü gör" ipliği açınca önizleme (ve düğmenin kendisi) kalkıyor; odak sayfanın
    başına düşmesin diye yorum düğmesine taşınıyor. Ekran okuyucu orada "Yorumlar —
    3 yorum, genişletilmiş" duyar ve iplik hemen ardından geliyor.
  */
  const tumYorumlariAc = () => {
    onYorumlar()
    yorumDugmesi.current?.focus()
  }

  return (
    <CamKart className="overflow-hidden !p-0">
      {gonderi.underReview && <IncelemeSeridi reportCount={gonderi.reportCount} />}

      <YazarBasligi yazar={gonderi.author} zaman={gonderi.createdAtUtc} etiket={etiketAnahtari} />

      <div className="px-4 pt-3 sm:px-5">
        <h3 className="text-[17px] font-bold leading-snug text-slate-900">{gonderi.title}</h3>
        {/* line-clamp-3: akış TARANABİLİR kalmalı. whitespace-pre-line: kullanıcı satır
            arası bıraktıysa o boşluk anlam taşıyor (madde madde yazılmış bir soru, tek
            paragrafa çökerse okunmaz). slate-700: metin burada içerik, ikincil bilgi değil. */}
        <p className="mt-1.5 line-clamp-3 whitespace-pre-line text-sm leading-relaxed text-slate-700">
          {gonderi.body}
        </p>
      </div>

      <div className="mt-3 flex items-center gap-1 border-t border-slate-200/70 px-2 py-1 sm:px-3">
        <OyRayi
          yatay
          arti={gonderi.upvoteCount}
          eksi={gonderi.downvoteCount}
          oy={gonderi.myVote}
          onOy={(yon) => onOy(gonderi.postId, yon)}
        />
        <YorumDugmesi
          dugmeRef={yorumDugmesi}
          sayi={gonderi.commentCount}
          acik={yorumlarAcik}
          onClick={onYorumlar}
        />
        <span className="flex-1" aria-hidden="true" />
        {/* KENDİ GÖNDERİNİ ŞİKAYET EDEMEZSİN: sunucu da reddediyor ("Kendini şikayet
            edemezsin"), ama hatayı göstermektense düğmeyi hiç çizmemek doğru —
            tıklandığında reddedilen bir düğme, kırık bir düğmedir. */}
        {!benimGonderim && (
          <SikayetDugmesi
            onClick={() =>
              onSikayet({
                tur: 'Gönderi',
                id: gonderi.postId,
                baslik: gonderi.title,
                yazar: gonderi.author?.displayName,
              })
            }
          />
        )}
      </div>

      {/* Önizleme yalnızca iplik KAPALIYKEN: açıkken aynı yorum ipliğin başında zaten
          görünüyor, iki kez yazılırdı. Sunucu göndermediyse (yorum yok, hepsi perdeli ya
          da engelli, eski sunucu) blok hiç çizilmez; yer tutucu yok, kart eylem
          satırıyla biter. */}
      {gonderi.firstComment && !yorumlarAcik && (
        <YorumOnizlemesi
          yorum={gonderi.firstComment}
          toplam={gonderi.commentCount}
          onTumu={tumYorumlariAc}
        />
      )}

      {yorumlarAcik && (
        <div className="px-4 pb-4 sm:px-5">
          <YorumListesi
            className="border-t border-slate-200/70 pt-4"
            durum={yorumDurumu}
            benimUserId={benimUserId}
            onSikayet={onSikayet}
            onYaz={onYorumYaz}
            onOy={onYorumOy}
          />
        </div>
      )}
    </CamKart>
  )
}

/**
 * Yazar başlığı: avatar + ad + (yönetim rozeti) + seviye; altında zaman · etiket.
 *
 * Ad ve avatar PROFİLE GİDİYOR (PersonLink): "ad görünen her yerde profile gidilir"
 * kuralı Keşfet, Arkadaşlar, Sohbet ve Derslerim'de vardı, forumda eksikti. Resmi
 * hesabın rozeti de profilde aynı rozetle doğrulanabiliyor. Avatarın bağlantısı klavye
 * ve ekran okuyucudan gizli; aynı hedefe giden ikinci bir durak olmasın.
 *
 * Etiket SAĞ ÜSTTE DEĞİL, zamanın yanında: dar ekranda "Ders Programı" pili, Yönetim
 * rozeti ve seviye madalyonu ada yer bırakmıyordu. Zaman slate-600: cam kartta
 * slate-500 küçük metin AA'yı geçmiyor (SayfaZemini → CamKart notu).
 */
export function YazarBasligi({ yazar, zaman, etiket }) {
  const ad = yazar?.displayName ?? 'Kullanıcı'

  return (
    <div className="flex items-center gap-3 px-4 pt-4 sm:px-5">
      <PersonLink
        userId={yazar?.userId}
        tabIndex={-1}
        aria-hidden="true"
        className="block shrink-0 rounded-lg"
      >
        {/* Bağlantı yoksa (silinmiş kullanıcı) kapsayıcı düz <span>'e düşüyor ve
            öznitelikleri almıyor; ad yine iki kez okunmasın diye gizleme burada da var. */}
        <span aria-hidden="true" className="block">
          <Avatar userId={yazar?.userId} name={ad} size="sm" />
        </span>
      </PersonLink>

      <div className="min-w-0 flex-1">
        <div className="flex min-w-0 items-center gap-1.5">
          {/* -my-3 py-3: görünüm aynı, dokunma yüksekliği lg altında 44px (satır 20px).
              Aşağı taşan dolgunun üstünü alt satır örtüyor; o satırda tıklanan bir şey yok. */}
          <PersonLink
            userId={yazar?.userId}
            className="-my-3 min-w-0 truncate py-3 text-sm font-semibold text-slate-900 lg:my-0 lg:py-0"
          >
            {ad}
          </PersonLink>
          {yazar?.isStaff && <YonetimRozeti />}
          {/* Seviye yalnızca `level` alanıyla besleniyor; rozet ilerleme verisi olmadan
              ilerleme iddia etmiyor (bkz. SeviyeRozeti). */}
          <SeviyeRozeti kaynak={{ level: yazar?.level }} boyut="sm" ton="acik" className="shrink-0" />
        </div>
        <p className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-600">
          <time dateTime={zaman ?? undefined}>{zamanKisalt(yasDakika(zaman))}</time>
          <span className="text-slate-400" aria-hidden="true">
            ·
          </span>
          <EtiketPili etiket={etiket} />
        </p>
      </div>
    </div>
  )
}

/**
 * Yorum düğmesi: balon + sayı. 0 ise sayı YAZILMAZ ("0" bir bilgi değil, boşluk).
 * Görünen metin yalnızca sayı olduğu için ad aria-label'da ve title'da (fare ipucu);
 * "Yorumlar — 3 yorum" görünen "3"ü içeriyor, yani sesli komutla da bulunuyor.
 */
export function YorumDugmesi({ sayi, acik, onClick, dugmeRef }) {
  const ad = sayi > 0 ? `Yorumlar — ${sayi} yorum` : 'Yorumlar — henüz yorum yok'

  return (
    <button
      ref={dugmeRef}
      type="button"
      onClick={onClick}
      aria-expanded={acik}
      aria-label={ad}
      title={ad}
      className={`flex min-h-11 min-w-11 items-center justify-center gap-1.5 rounded-lg px-2.5
                  text-sm font-semibold transition lg:min-h-9 lg:min-w-9 ${
                    acik
                      ? 'bg-brand-50 text-brand-700'
                      : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
                  }`}
    >
      <MesajIkonu className="h-[18px] w-[18px]" />
      {sayi > 0 && <span className="tabular-nums">{sayi}</span>}
    </button>
  )
}

/*
  Sunucunun önizleme sınırı (ForumQueries.cs → ForumOnizleme.EnFazlaGrafem). Kesilen gövde
  "…" ile biter ve 199 ya da 200 grafemdir (kesme noktasındaki boşluk kırpıldıysa 199).
*/
const ONIZLEME_GRAFEM_SINIRI = 200

/**
 * Sunucu gövdeyi kestiyse true. Kod noktası sayısı grafem sayısından küçük olamaz, yani
 * kesilmiş bir gövde bu eşiğin altına düşmez; eşiğin altında "…" ile biten bir yorum
 * yazarın kendi üç noktasıdır, kesilme değil.
 */
function sunucuKestiMi(govde) {
  return govde.endsWith('…') && Array.from(govde).length >= ONIZLEME_GRAFEM_SINIRI - 1
}

/**
 * İlk yorumun önizlemesi (sunucu: ForumPostDto.firstComment). "Ad yorum" tek paragrafta,
 * iki satırda kesiliyor.
 *
 * SUNUCU ZATEN SÜZÜYOR: incelemedeki ya da kaldırılmış yorum, bakanla arasında engel
 * olan kişinin yorumu ve perdeli gönderinin yorumu buraya hiç gelmiyor; gövde tek satıra
 * indirilmiş ve 200 grafemde kesilmiş geliyor. İstemci bu kararları YENİDEN VERMİYOR
 * (iplikten türetmek de yok: iplik ucu engele göre süzmüyor).
 *
 * ALTTAKİ SATIR yorumun geri kalanına giden yol:
 *   • n ≥ 2 → "{n} yorumun tümünü gör". Sayı sunucunun commentCount'u ve kaldırılan
 *     yorumları da sayıyor; iplik açılınca kart görünen sayıya düzeltiliyor
 *     (Topluluk.jsx → yorumEkle notu).
 *   • n = 1 ve önizleme KESİKSE → "Yorumun tamamını gör". Kesik iki yoldan olur: satır
 *     sınırı (line-clamp-2; dar ekranda ~90 karakter) ya da sunucunun 200 grafem sınırı
 *     (geniş kartta iki satıra sığıp "…" ile biten gövde). 2026-09-27'ye kadar satır
 *     yalnızca n ≥ 2 iken çiziliyordu ("tek yorum zaten önizlemede"); uzun tek yorum
 *     kesiliyor ve devamına giden tek yol eylem satırındaki balon düğmesi kalıyordu.
 *     Paragrafın kendisi düğme YAPILAMAZ: içinde yazarın profil bağlantısı var. (Mobilde
 *     önizlemenin tamamı ipliği açan tek Pressable; orada ad bağlantı değil.)
 *   • n = 1 ve yorum sığıyorsa satır yok: gösterilecek başka bir şey yok.
 *
 * Satır sınırı ÖLÇÜLÜYOR (scrollHeight > clientHeight), karakter saymak yetmez: kaç
 * karakterin iki satıra sığdığı kartın genişliğine bağlı. ResizeObserver genişlik
 * değişince yeniden ölçüyor; +1 alt piksel yuvarlamasının payı (satır yüksekliği
 * 22.75px, iki satır 45.5px).
 */
export function YorumOnizlemesi({ yorum, toplam, onTumu }) {
  const ad = yorum.author?.displayName ?? 'Kullanıcı'
  const metinRef = useRef(null)
  const [satirdaKesik, setSatirdaKesik] = useState(false)

  useLayoutEffect(() => {
    const metin = metinRef.current
    if (!metin) return undefined

    const olc = () => setSatirdaKesik(metin.scrollHeight > metin.clientHeight + 1)
    olc()

    if (typeof ResizeObserver === 'undefined') return undefined
    const gozlemci = new ResizeObserver(olc)
    gozlemci.observe(metin)
    return () => gozlemci.disconnect()
  }, [yorum.body, ad])

  const devamSatiri =
    toplam >= 2
      ? `${toplam} yorumun tümünü gör`
      : satirdaKesik || sunucuKestiMi(yorum.body)
        ? 'Yorumun tamamını gör'
        : null

  return (
    <div className="px-4 pb-3 pt-1 sm:px-5">
      <p ref={metinRef} className="line-clamp-2 text-sm leading-relaxed text-slate-700">
        <span className="sr-only">İlk yorum, </span>
        <PersonLink userId={yorum.author?.userId} className="font-semibold text-slate-900">
          {ad}
        </PersonLink>
        {yorum.author?.isStaff && <YonetimRozeti kucuk className="ml-1 align-[-2px]" />}
        <span className="sr-only">:</span> {yorum.body}
      </p>
      {devamSatiri && (
        /* Dokunma sınırı lg (CLAUDE.md): altında 44px, üstünde metin boyu. */
        <button
          type="button"
          onClick={onTumu}
          className="inline-flex min-h-11 items-center text-xs font-medium text-slate-600
                     transition hover:text-slate-900 lg:mt-1 lg:min-h-0"
        >
          {devamSatiri}
        </button>
      )}
    </div>
  )
}

/* ─── REDDIT TARZI (geri dönüş) ────────────────────────────────────────────── */

/*
  2026-09-26'ya kadarki kart, Topluluk.jsx'ten TAŞINDI (silinmedi). Sol dikey oy rayı,
  üst satırda etiket + yazar + zaman, sağ üstte şikayet. Değişen iki şey: perde
  dağıtıcıya çıktı (iki tarz için ortak) ve kap `overflow-hidden !p-0` oldu — eski
  `p-0` CamKart'ın p-5'ini ezemiyordu (Instagram kartındaki not).
*/
function RedditKarti({
  gonderi,
  benimUserId,
  yorumDurumu,
  onOy,
  yorumlarAcik,
  onYorumlar,
  onYorumYaz,
  onYorumOy,
  onSikayet,
}) {
  const etiketAnahtari = ETIKET_ANAHTARI[gonderi.tag] ?? gonderi.tag
  const benimGonderim = gonderi.author?.userId === benimUserId

  return (
    <CamKart className="overflow-hidden !p-0">
      {gonderi.underReview && <IncelemeSeridi reportCount={gonderi.reportCount} />}

      <div className="flex gap-3 p-4 sm:gap-4 sm:p-5">
        <OyRayi
          arti={gonderi.upvoteCount}
          eksi={gonderi.downvoteCount}
          oy={gonderi.myVote}
          onOy={(yon) => onOy(gonderi.postId, yon)}
        />

        <div className="min-w-0 flex-1">
          {/* ÜST SATIR: etiket + yazar + zaman solda, şikayet sağ üstte. */}
          <div className="flex items-start justify-between gap-3">
            {/*
              AYIRAÇ NOKTALARI KENDİ BAŞLARINA BİR ÖĞE DEĞİL, ait oldukları metnin
              başında duruyor. 320px'te bu satır sarıyor ve nokta ayrı bir flex öğesi
              olduğunda satır sonunda tek başına asılı kalıyordu ("Sınav Stresi ·" /
              yeni satır / "Elif A."). Noktayı takip ettiği metne bağlamak, sarmanın
              nereden olursa olsun düzgün görünmesini sağlıyor.
            */}
            <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
              <EtiketPili etiket={etiketAnahtari} />
              <YazarSatiri yazar={gonderi.author} boyut="xs" />

              <span className="flex items-center gap-1.5 text-xs text-slate-600">
                <span className="text-slate-400" aria-hidden="true">
                  ·
                </span>
                {zamanKisalt(yasDakika(gonderi.createdAtUtc))}
              </span>
            </div>

            {!benimGonderim && (
              <SikayetDugmesi
                onClick={() =>
                  onSikayet({
                    tur: 'Gönderi',
                    id: gonderi.postId,
                    baslik: gonderi.title,
                    yazar: gonderi.author?.displayName,
                  })
                }
              />
            )}
          </div>

          <h3 className="mt-2.5 text-[17px] font-bold leading-snug text-slate-900">
            {gonderi.title}
          </h3>

          <p className="mt-2 line-clamp-3 whitespace-pre-line text-sm leading-relaxed text-slate-600">
            {gonderi.body}
          </p>

          <div className="mt-3.5 flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={onYorumlar}
              aria-expanded={yorumlarAcik}
              className={`flex min-h-11 items-center gap-2 rounded-lg px-2.5 text-xs font-semibold
                          transition lg:min-h-9 ${
                            yorumlarAcik
                              ? 'bg-brand-50 text-brand-700'
                              : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
                          }`}
            >
              <MesajIkonu className="h-4 w-4" />
              {gonderi.commentCount > 0 ? `${gonderi.commentCount} yorum` : 'Yorumlar'}
            </button>
          </div>

          {yorumlarAcik && (
            <YorumListesi
              durum={yorumDurumu}
              benimUserId={benimUserId}
              onSikayet={onSikayet}
              onYaz={onYorumYaz}
              onOy={onYorumOy}
            />
          )}
        </div>
      </div>
    </CamKart>
  )
}

/* ─── ORTAK PARÇALAR ───────────────────────────────────────────────────────── */

export function EtiketPili({ etiket, className = '' }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold
                  ${ETIKET_TONU[etiket] ?? 'bg-slate-100 text-slate-700'} ${className}`}
    >
      {ETIKET_ADI[etiket] ?? etiket}
    </span>
  )
}

/**
 * Yazar satırı: avatar + ad + (yönetim rozeti) + seviye. Yorumlarda ve Reddit tarzı
 * kartta; Instagram kartının başlığı YazarBasligi.
 *
 * Yorumlarda ve Reddit kartında AYNI bileşen: yazarın nasıl gösterildiği iki yerde ayrı
 * yazılsaydı, rozet birine eklenip diğerine eklenmeden kalabilirdi. Ad profile gidiyor
 * (PersonLink); avatarın bağlantısı klavye ve ekran okuyucudan gizli (YazarBasligi notu).
 */
export function YazarSatiri({ yazar, boyut = 'xs' }) {
  const ad = yazar?.displayName ?? 'Kullanıcı'

  return (
    <span className="flex min-w-0 items-center gap-1.5">
      <PersonLink userId={yazar?.userId} tabIndex={-1} aria-hidden="true" className="block shrink-0">
        <span aria-hidden="true" className="block">
          <Avatar userId={yazar?.userId} name={ad} size={boyut} />
        </span>
      </PersonLink>
      {/* -my-3.5 py-3.5: görünüm aynı, dokunma yüksekliği lg altında 44px (text-xs satırı
          16px + 2 × 14px). YazarBasligi'ndaki kalıp; orada text-sm (20px) olduğu için 3.
          Taşan dolgu hiçbir düğmeyle kesişmiyor (375px'te ölçüldü, 2026-09-27): yukarıda
          li arası 16px ya da kartın dolgusu, oy rayı ayrı sütunda, aşağıda iplik satırı
          en az 44px (YorumListesi'ndeki min-h-11). Reddit kartında altta başlık var, o
          bağlantı değil. 2026-09-27'ye kadar ad 16px yüksekliğindeydi ("Mert Demir 63x16"). */}
      <PersonLink
        userId={yazar?.userId}
        className="-my-3.5 min-w-0 truncate py-3.5 text-xs font-medium text-slate-700 lg:my-0 lg:py-0"
      >
        {ad}
      </PersonLink>
      {yazar?.isStaff && <YonetimRozeti kucuk={boyut === 'xs'} />}
      {/* Seviye yalnızca `level` alanıyla besleniyor; rozet ilerleme verisi olmadan
          ilerleme iddia etmiyor (bkz. SeviyeRozeti). Puan başkasının verisi ve forum
          DTO'su onu göndermiyor. */}
      <SeviyeRozeti kaynak={{ level: yazar?.level }} boyut="sm" ton="acik" className="shrink-0" />
    </span>
  )
}

/*
  OY RAYI — dikey (Reddit kartı ve yorumlar) ya da yatay (`yatay`, Instagram kartının
  eylem satırı). Ölçüler iki yönde aynı.

  Dokunma hedefi lg altında 44px (min-h-11): oy okları bu ekranda birbirine en yakın
  duran iki düğme ve yanlış oku basmak, kullanıcının kendi oyunu ters çevirmesi demek.
  lg üstünde fare hassas olduğu için 36px yetiyor.

  Renk oyun yönünü söylüyor: yukarı marka mavisi (bu ürünün "evet" rengi), aşağı rose.
  Sayı da oyun rengini alıyor — kullanıcı kendi oyunu, okların hangisinin dolu olduğuna
  bakmadan, tek bir sayıya bakarak görebiliyor. Ekran okuyucu sayıyı "Puan: n" diye
  okuyor; çıplak bir sayı iki oy düğmesinin arasında ne olduğunu söylemiyordu.

  ⚠️ SAYI OYU AYRICA EKLEMİYOR. Sabit veriyle çalışırken gösterilen değer `puan + oy`
  idi, çünkü taban sayı kullanıcının kendi oyunu içermiyordu. Sunucudan gelen
  upvoteCount/downvoteCount İÇERİYOR; toplamak kendi oyumuzu iki kez saymak olurdu.
*/
export function OyRayi({ arti, eksi, oy = 0, onOy, kucuk = false, yatay = false }) {
  const olcu = kucuk ? 'h-9 w-9 lg:h-8 lg:w-8' : 'h-11 w-11 lg:h-9 lg:w-9'
  const ortak =
    `grid ${olcu} place-items-center rounded-lg transition ` +
    'focus:outline-none focus:ring-2 focus:ring-brand-200'

  return (
    <div className={`flex shrink-0 items-center gap-0.5 ${yatay ? 'flex-row' : 'flex-col'}`}>
      <button
        type="button"
        aria-label="Yukarı oy ver"
        aria-pressed={oy === 1}
        onClick={() => onOy(1)}
        className={`${ortak} ${
          oy === 1 ? 'bg-brand-50 text-brand-600' : 'text-slate-400 hover:bg-slate-100 hover:text-brand-600'
        }`}
      >
        <OyOkuIkonu className="h-[18px] w-[18px]" strokeWidth={oy === 1 ? 2.6 : 2} />
      </button>

      <span
        className={`text-sm font-bold tabular-nums ${yatay ? 'min-w-[2ch] text-center' : ''} ${
          oy === 1 ? 'text-brand-700' : oy === -1 ? 'text-rose-700' : 'text-slate-800'
        }`}
      >
        <span className="sr-only">Puan: </span>
        {arti - eksi}
      </span>

      <button
        type="button"
        aria-label="Aşağı oy ver"
        aria-pressed={oy === -1}
        onClick={() => onOy(-1)}
        className={`${ortak} ${
          oy === -1 ? 'bg-rose-50 text-rose-600' : 'text-slate-400 hover:bg-slate-100 hover:text-rose-600'
        }`}
      >
        {/* Tek çizim, iki yön: aşağı ok ayrı bir ikon değil, aynı okun 180° dönmüşü. */}
        <OyOkuIkonu className="h-[18px] w-[18px] rotate-180" strokeWidth={oy === -1 ? 2.6 : 2} />
      </button>
    </div>
  )
}

/*
  ŞİKAYET DÜĞMESİ — her gönderide ve her yorumda, aynı çizim, aynı yer mantığı.

  Sessiz duruyor (slate-500, ikon + küçük metin) ama saklı değil. İki uç da yanlış
  olurdu: dikkat çeken bir "Şikayet Et" düğmesi forumu bir ihbar hattı gibi gösterir;
  üç nokta menüsünün içine gömülen bir şikayet ise ihlali gören kullanıcının vazgeçtiği
  bir yol olur. Hover'da rose'a dönüyor — eylemin ağırlığı ancak niyet edildiğinde
  görünüyor.

  Metin sm (640px) altında GİZLİ, ikon kalıyor: dar ekranda aynı satırda oy, yorum ve
  (Reddit kartında) etiket, yazar ve zaman yarışıyor. Erişilebilir ad her iki durumda
  da aria-label'da. (Bu not 2026-09-26'ya kadar "lg altında" diyordu; kod baştan beri
  `sm:inline`.)

  lg altında en az 44×44 (min-w-11): yalnız ikonken düğme 32px genişliğe iniyordu,
  yüksekliği 44 olsa da parmak hedefi dar kalıyordu (375px'te ölçüldü, 2026-09-26).
*/
export function SikayetDugmesi({ onClick, kucuk = false }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Şikayet et"
      title="Şikayet et"
      className={`flex min-w-11 shrink-0 items-center justify-center gap-1.5 rounded-lg
                  text-slate-500 transition hover:bg-rose-50 hover:text-rose-700 lg:min-w-0 ${
                    kucuk ? 'min-h-11 px-2 text-[11px] lg:min-h-8' : 'min-h-11 px-2 text-xs lg:min-h-9'
                  }`}
    >
      <BayrakIkonu className={kucuk ? 'h-3.5 w-3.5' : 'h-4 w-4'} />
      <span className="hidden font-medium sm:inline">Şikayet et</span>
    </button>
  )
}

/* ─── YORUM İPLİĞİ ─────────────────────────────────────────────────────────── */

/*
  Yorumlar gönderinin İÇİNDE açılıyor, ayrı bir sayfada değil. Akranlar arası kısa
  cevaplar için yerinde açılan bir iplik, sayfa değiştirip geri dönmekten daha az iş.

  Sol kenardaki dikey çizgi (border-l) yorumları gönderiye bağlıyor: girinti tek başına
  "bu yorumlar o gönderiye ait" demiyor, çizgi diyor.

  `className` ipliğin üst kenarını kartın tarzı veriyor: Reddit kartında metin
  sütununun içinde, üstünde boşlukla; Instagram kartında eylem satırının hemen altında,
  çizgiyle.
*/
export function YorumListesi({
  durum,
  benimUserId,
  onSikayet,
  onYaz,
  onOy,
  className = 'mt-4 border-t border-slate-200/70 pt-4',
}) {
  const [taslak, setTaslak] = useState('')
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const [hata, setHata] = useState(null)

  /* Alt sınır 5 karakter (sunucudaki ForumRules.CommentMinLength ile aynı): "+1" ya da
     "aynen" gibi tek kelimelik onaylar bir tartışmayı ilerletmiyor ama boş bir yorumu
     göndermeyi engellemek yeterli — gönderi formundaki 20 karakterlik eşik burada fazla
     olurdu, kısa ve isabetli cevaplar meşru. */
  const gonderilebilir = taslak.trim().length >= 5 && !gonderiliyor

  const gonder = async (e) => {
    e.preventDefault()
    if (!gonderilebilir) return
    setGonderiliyor(true)
    setHata(null)
    try {
      await onYaz(taslak.trim())
      setTaslak('')
    } catch (err) {
      // Taslak SİLİNMİYOR: yazdığı yorumu kaybeden kullanıcı yeniden yazmıyor, vazgeçiyor.
      setHata(err)
    } finally {
      setGonderiliyor(false)
    }
  }

  const liste = durum?.liste

  return (
    <div className={className}>
      {durum?.yukleniyor ? (
        <Loading label="Yorumlar yükleniyor…" />
      ) : durum?.hata ? (
        <ErrorBox error={durum.hata} />
      ) : !liste || liste.length === 0 ? (
        <p className="text-sm text-slate-600">Bu gönderide henüz yorum yok.</p>
      ) : (
        <ul className="space-y-4 border-l-2 border-slate-100 pl-3 sm:pl-4">
          {liste.map((yorum) => (
            <li key={yorum.commentId}>
              <div className="flex items-start gap-2.5">
                <OyRayi
                  kucuk
                  arti={yorum.upvoteCount}
                  eksi={yorum.downvoteCount}
                  oy={yorum.myVote}
                  onOy={(yon) => onOy(yorum.commentId, yon)}
                />

                <div className="min-w-0 flex-1">
                  {/* min-h-11 (lg altı): yazar adının 44px dokunma alanı (YazarSatiri) satırın
                      İÇİNDE kalsın. Başkasının yorumunda satırı şikayet düğmesi zaten 44px
                      yapıyordu; kendi yorumunda düğme yok, satır 24px kalıyor ve adın alt
                      dolgusunu gövde metni örtüyordu (etkin alan 36px, 375px'te ölçüldü).
                      Artık iki tür yorumun gövdesi aynı yükseklikten başlıyor. */}
                  <div className="flex min-h-11 items-start justify-between gap-2 lg:min-h-0">
                    <span className="flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-1">
                      <YazarSatiri yazar={yorum.author} boyut="xs" />
                      <span className="flex items-center gap-1.5 text-xs text-slate-600">
                        <span className="text-slate-400" aria-hidden="true">
                          ·
                        </span>
                        {zamanKisalt(yasDakika(yorum.createdAtUtc))}
                      </span>
                    </span>

                    {/* Yorumun şikayet düğmesi de aynı yerde: sağ üst. Gönderiyle
                        aynı konum, aynı ikon — kullanıcı kuralı bir kez öğreniyor. */}
                    {yorum.author?.userId !== benimUserId && (
                      <SikayetDugmesi
                        kucuk
                        onClick={() =>
                          onSikayet({
                            tur: 'Yorum',
                            id: yorum.commentId,
                            baslik: yorum.body,
                            yazar: yorum.author?.displayName,
                          })
                        }
                      />
                    )}
                  </div>

                  {yorum.underReview && (
                    <p className="mt-1 flex items-center gap-1.5 text-xs font-medium text-amber-800">
                      <UyariIkonu className="h-3.5 w-3.5 shrink-0" />
                      Bu yorum incelemede.
                    </p>
                  )}

                  <p className="mt-1 whitespace-pre-line text-sm leading-relaxed text-slate-700">
                    {yorum.body}
                  </p>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      {/*
        Yorum kutusu SATIR İÇİ, modal değil — gönderiden farkı burada: yorumun tek bir
        alanı var ve bağlamı (üstündeki tartışma) ekranda kalmalı. Modal açsaydı,
        cevap yazarken cevapladığın şeyi görmez olurdun.

        Düğme metnin ALTINDA ve alan boşken pasif: hedef 44px, dar ekranda da rahat
        basılıyor. Enter'la göndermek YOK — çok satırlı bir alanda Enter satır başıdır.
      */}
      <form onSubmit={gonder} className="mt-4">
        <ErrorBox error={hata} />
        <textarea
          className="input mt-2 h-20 resize-none"
          value={taslak}
          onChange={(e) => setTaslak(e.target.value)}
          maxLength={1000}
          placeholder="Yorumunu yaz…"
          aria-label="Yorum yaz"
        />
        <div className="mt-2 flex justify-end">
          <Button
            type="submit"
            loading={gonderiliyor}
            disabled={!gonderilebilir}
            className="px-4 py-1.5 text-xs"
          >
            Yorumla
          </Button>
        </div>
      </form>
    </div>
  )
}
