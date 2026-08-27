import { useEffect, useState } from 'react'
import { api } from '../lib/api'

/**
 * Profil fotoğrafı. Yoksa baş harflerden bir yer tutucu üretir.
 *
 * Yer tutucu bilinçli olarak RENKLİ ve kişiye özel: kullanıcı kimliğinden türeyen sabit
 * bir renk seçilir, böylece fotoğrafı olmayan kişiler de listede birbirinden ayırt edilir.
 * Tek tip gri bir daire, sohbet ve arama listelerini okunmaz hâle getirirdi.
 */

const COLORS = [
  'bg-brand-600',
  'bg-sky-600',
  'bg-emerald-600',
  'bg-amber-600',
  'bg-rose-600',
  'bg-violet-600',
]

function colorFor(userId = '') {
  let sum = 0
  for (let i = 0; i < userId.length; i++) sum += userId.charCodeAt(i)
  return COLORS[sum % COLORS.length]
}

function initialsOf(name = '') {
  return name
    .split(' ')
    .filter(Boolean)
    .map((w) => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

/**
 * Avatar görselinin object URL'i. Ayrı bir hook: hem <Avatar> hem büyütme katmanı
 * aynı URL'i kullanıyor ve ikinci bir istek atılmasın diye tek yerden okunuyor.
 */
function useAvatarUrl(userId) {
  const [url, setUrl] = useState(null)

  useEffect(() => {
    let cancelled = false
    if (!userId) return

    api.avatarObjectUrl(userId).then((objectUrl) => {
      if (!cancelled) setUrl(objectUrl)
    })

    return () => {
      cancelled = true
      // Object URL BİLEREK serbest bırakılmıyor: önbellek onu paylaşıyor ve burada
      // iptal etmek aynı avatarı gösteren diğer kartları bozardı.
    }
  }, [userId])

  return url
}

/*
  Küçük boyutlar YUVARLATILMIŞ KARE, xl ise TAM DAİRE — ve bu tutarsızlık değil, ölçek
  farkı. Liste satırlarında kare avatar hizalanması kolay bir blok; profil başlığında
  ise avatar tek başına duran bir portre ve orada daire, kareye göre daha az yer
  kaplayıp daha çok yüz gösteriyor. Instagram'dan Slack'e kadar bu ayrım aynı.
*/
const SIZES = {
  /* xs yalnızca forum akışındaki yazar satırında: etiket, ad ve zamanın yan yana
     durduğu 320px'lik bir satırda 32px'lik avatar adın yerini yiyordu. Halka (ring)
     bu boyutta YOK — 24px'lik bir karede 2px beyaz halka, baş harflere kalan yeri
     gözle görülür biçimde daraltıyor. */
  xs: 'h-6 w-6 text-[10px] rounded-md',
  sm: 'h-8 w-8 text-xs rounded-lg',
  md: 'h-12 w-12 text-sm rounded-xl',
  lg: 'h-20 w-20 text-2xl rounded-2xl',
  xl: 'h-28 w-28 text-4xl rounded-full sm:h-32 sm:w-32',
}

/** Karartma katmanının köşesi. SIZES ile aynı anahtarlar — bkz. Avatar içindeki not. */
const KOSELER = {
  xs: 'rounded-md',
  sm: 'rounded-lg',
  md: 'rounded-xl',
  lg: 'rounded-2xl',
  xl: 'rounded-full',
}

/**
 * @param buyutulebilir  tıklayınca fotoğrafı tam ekran gösterir (bkz. AvatarKatmani).
 *   Varsayılan KAPALI ve bilerek: avatar sohbet listesinde, arama sonucunda, yorum
 *   satırında da kullanılıyor. Oralarda tıklama zaten kişiye gitmeli — fotoğrafı
 *   büyütmek o akışı keserdi. Büyütme yalnızca fotoğrafın kendisinin konu olduğu
 *   yerde (profil başlığı) anlamlı.
 */
export function Avatar({ userId, name, size = 'md', className = '', buyutulebilir = false }) {
  const url = useAvatarUrl(userId)
  const [acik, setAcik] = useState(false)

  const gorsel = url ? (
    <img
      src={url}
      alt={name ?? 'Profil fotoğrafı'}
      className={`${SIZES[size]} shrink-0 object-cover ${className}`}
    />
  ) : (
    <div
      aria-label={name}
      className={`${SIZES[size]} ${colorFor(userId)} grid shrink-0 place-items-center font-bold text-white ${className}`}
    >
      {initialsOf(name)}
    </div>
  )

  // Fotoğrafı OLMAYAN avatar büyütülmez: baş harfleri tam ekran göstermek boş bir
  // jest olurdu ve tıklanabilir görünen ama bir şey yapmayan bir öğe üretirdi.
  if (!buyutulebilir || !url) return gorsel

  return (
    <>
      <button
        type="button"
        onClick={() => setAcik(true)}
        aria-label={`${name ?? 'Profil'} fotoğrafını büyüt`}
        className="group relative shrink-0 rounded-xl focus:outline-none focus-visible:ring-2
                   focus-visible:ring-brand-500 focus-visible:ring-offset-2"
      >
        {gorsel}
        {/*
          Büyütülebilirliğin GÖRÜNÜR işareti. İmleç değişimi tek başına yetmiyor:
          dokunmatik cihazda imleç yok ve kullanıcı fotoğrafın tıklanabilir olduğunu
          hiç fark etmiyordu. Karartma + simge yalnızca hover/odakta çıkıyor, yani
          normal görünümü kirletmiyor.
        */}
        {/*
          Köşe yarıçapı AYRI TABLODAN okunuyor. Eskiden `SIZES[size].split(' ').pop()`
          ile son sınıf alınıyordu; xl'e duyarlı sınıf (`sm:w-32`) eklenince o hile
          sessizce yanlış sınıfı seçti ve karartma katmanı kare çıktı. Sınıf dizisinin
          SIRASINA bağlı kod, diziye bir şey eklenince bozulur.
        */}
        <span
          className={`pointer-events-none absolute inset-0 grid place-items-center bg-slate-900/45
                      text-white opacity-0 transition group-hover:opacity-100
                      group-focus-visible:opacity-100 ${KOSELER[size] ?? 'rounded-xl'}`}
          aria-hidden="true"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-1/3 w-1/3">
            <path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </span>
      </button>

      <AvatarKatmani open={acik} url={url} name={name} onClose={() => setAcik(false)} />
    </>
  )
}

/**
 * Tam ekran fotoğraf katmanı (lightbox).
 *
 * ui.jsx'teki Modal KULLANILMADI ve bu bilinçli: Modal başlık çubuğu, beyaz kutu ve
 * gövde dolgusu getiriyor — hepsi burada fotoğrafın etrafına çerçeve çiziyor. Bir
 * fotoğrafı büyütmenin amacı çerçeveyi kaldırmak; kutunun içine koymak amacın tersi.
 *
 * Kapanma yolları ÜÇ tane çünkü kullanıcının hangisini deneyeceği bilinmiyor:
 * Escape, boşluğa tıklama, sağ üstteki düğme. Üçü de aynı işi yapıyor.
 */
function AvatarKatmani({ open, url, name, onClose }) {
  useEffect(() => {
    if (!open) return
    const onKey = (e) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)

    // Arkadaki sayfa kaymasın: katman açıkken tekerlek fotoğrafı değil sayfayı
    // kaydırıyordu ve kapatınca kullanıcı başka bir yerde buluyordu kendini.
    const oncekiOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = oncekiOverflow
    }
  }, [open, onClose])

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/85 p-4 backdrop-blur-sm"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-label={`${name ?? 'Profil'} fotoğrafı`}
    >
      <button
        type="button"
        onClick={onClose}
        aria-label="Kapat"
        className="absolute right-3 top-3 grid h-11 w-11 place-items-center rounded-full
                   bg-white/10 text-2xl leading-none text-white transition hover:bg-white/20"
      >
        ✕
      </button>

      {/*
        max-h/max-w yüzde İLE: fotoğraf ekranı taşamaz ama kendi oranını da korur.
        dvh (vh değil) çünkü mobil adres çubuğu vh'ye dahil değil ve fotoğrafın altı
        kırpılırdı. Görsele tıklama katmana ULAŞMIYOR (stopPropagation): kullanıcı
        fotoğrafa bakarken üstüne tıklayınca kapanması beklenmedik olurdu.
      */}
      <img
        src={url}
        alt={name ?? 'Profil fotoğrafı'}
        onClick={(e) => e.stopPropagation()}
        className="max-h-[85dvh] max-w-full rounded-2xl object-contain shadow-2xl"
      />
    </div>
  )
}
