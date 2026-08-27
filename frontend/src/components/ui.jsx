import { useEffect } from 'react'

const VARIANTS = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700 focus:ring-brand-200',
  secondary: 'bg-white text-slate-700 border border-slate-300 hover:bg-slate-50 focus:ring-slate-200',
  danger: 'bg-rose-600 text-white hover:bg-rose-700 focus:ring-rose-200',
  success: 'bg-emerald-600 text-white hover:bg-emerald-700 focus:ring-emerald-200',
  ghost: 'text-slate-600 hover:bg-slate-100 focus:ring-slate-200',
}

export function Button({ variant = 'primary', className = '', loading = false, children, ...props }) {
  return (
    // min-h-11 (44px): parmakla basılan hedefin alt sınırı. Eşik lg (1024px) — sm DEĞİL:
    // 768px'lik bir tablet hâlâ dokunmatiktir. Uygulama zaten lg'de menüyü hamburgerden
    // tam gezinmeye çeviriyor, yani "masaüstü" sınırı orası; iki ölçü aynı yerde değişsin.
    <button
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm
                  font-medium transition focus:outline-none focus:ring-2 disabled:cursor-not-allowed
                  disabled:opacity-50 lg:min-h-0 ${VARIANTS[variant]} ${className}`}
      {...props}
      /*
        ⚠️ `disabled` SPREAD'DEN SONRA — 2026-08-27'de düzeltildi, sırası KRİTİK.

        Önce `disabled={loading || props.disabled}` yazıp ARDINDAN `{...props}`
        yaymak, çağıranın açıkça geçtiği `disabled` değerinin hesaplanmış olanı
        EZMESİNE yol açıyordu. Yani hem `loading` hem `disabled` geçen her düğme
        (depoda 19 tane) istek uçuşurken TIKLANABİLİR kalıyordu: `disabled={!hazir}`
        ve form geçerliyken `disabled` false oluyor, spinner dönerken düğmeye
        ikinci kez basılabiliyordu.

        Somut sonucu: yönetim panelindeki "Uygula ve şikayeti kapat" iki kez
        basıldığında aynı kullanıcıya İKİ yaptırım uygulanıyor ve denetim izine iki
        kayıt düşüyordu. Aynı tuzak avatar yükleme, değerlendirme gönderme ve parola
        sıfırlamada da vardı.

        Sıra tersine çevrildiği için artık çağıranın `disabled`'ı da hesaba katılıyor
        (ifadenin içinde `props.disabled` duruyor) ama `loading` bastırılamıyor.
      */
      disabled={loading || props.disabled}
    >
      {loading && <Spinner className="h-4 w-4" />}
      {children}
    </button>
  )
}

/*
  YÜZEY DİLİ (madde 5 — modern/minimalist):
    zemin  bg-slate-50 (#F8FAFC, index.css'te body)
    kart   beyaz + border-slate-200/80 + shadow-md
    kenar  yarı saydam: slate-50 zemin üstünde tam opak slate-200 sert bir çizgi bırakıyor;
           /80 aynı ayrımı yapıp gürültüyü düşürüyor.
    gölge  shadow-sm → shadow-md: kenarlık zayıflayınca kartı zeminden ayıran iş gölgeye
           geçiyor. İkisini birden zayıflatmak kartları düz bir yamaya çevirirdi.
*/
/**
 * Uygulamanın tek yüzey dili.
 *
 * 2026-08-24'te üç değer birden değişti ve üçü de aynı şikâyete bakıyor: arayüz "ruhsuz
 * ve amatör" duruyordu.
 *
 *   shadow-md → shadow-sm   Ağır gölge tek bir kartta iyi görünüyor, on kart alt alta
 *                           gelince ekran kabartmalı bir duvara dönüşüyordu. Derinlik
 *                           kartın kendisinden değil, kartın zeminden AYRILMASINDAN
 *                           gelir; slate-50 zemin üstünde ince bir gölge yeter.
 *   rounded-xl → rounded-2xl  12px → 16px. Modern uygulama dilinde köşe yarıçapı,
 *                           yüzeyin "dokunulabilir bir nesne" olduğunu söyleyen ilk
 *                           işaret. 12px hâlâ "kutu", 16px "kart".
 *   border-slate-200/80 → border-slate-100  Kenarlık kartı çevrelemeli, çizmemeli.
 *                           Daha açık kenarlıkla gölge öne çıkıyor ve kart zeminden
 *                           gölgeyle ayrılıyor — kenarlıkla değil.
 *
 * Buradaki her değer TÜM uygulamaya yayılıyor: dersler, yorumlar, profil, panel. Tek
 * yerde durmasının sebebi bu — yüzey dili bir bileşende yaşamazsa her sayfada biraz
 * farklı olur ve "amatör" hissi tam olarak o farklardan doğar.
 */
export function Card({ className = '', children }) {
  return (
    <div className={`rounded-2xl border border-slate-100 bg-white p-5 shadow-sm ${className}`}>
      {children}
    </div>
  )
}

export function SectionTitle({ children, action }) {
  return (
    <div className="mb-3 flex items-center justify-between gap-3">
      <h2 className="text-lg font-semibold text-slate-800">{children}</h2>
      {action}
    </div>
  )
}

const BADGE_TONES = {
  neutral: 'bg-slate-100 text-slate-700',
  brand: 'bg-brand-100 text-brand-700',
  success: 'bg-emerald-100 text-emerald-700',
  warning: 'bg-amber-100 text-amber-800',
  danger: 'bg-rose-100 text-rose-700',
}

export function Badge({ tone = 'neutral', className = '', children }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium
                  ${BADGE_TONES[tone]} ${className}`}
    >
      {children}
    </span>
  )
}

export function Spinner({ className = 'h-5 w-5' }) {
  return (
    <svg className={`animate-spin ${className}`} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
    </svg>
  )
}

export function Loading({ label = 'Yükleniyor…' }) {
  return (
    <div className="flex items-center justify-center gap-3 py-10 text-slate-500">
      <Spinner />
      <span className="text-sm">{label}</span>
    </div>
  )
}

/** Hata kutusu — backend'in Türkçe `detail` mesajını ve hata kodunu gösterir. */
export function ErrorBox({ error, onRetry }) {
  if (!error) return null
  return (
    <div className="rounded-lg border border-rose-200 bg-rose-50 p-4 text-sm text-rose-800">
      {/*
          HATA KODU KULLANICIYA GÖSTERİLMEZ. Buraya eskiden MATCH_HAS_ACTIVE_SESSIONS gibi
          backend sabitleri basılıyordu: kullanıcı için anlamsız, üstelik hatayı "sistem
          bozuldu" gibi gösteriyordu. Sunucu zaten her AppException'da Türkçe bir `detail`
          döndürüyor (error.message) — okunması gereken metin o.

          Kod kaybolmuyor: ApiError üstünde duruyor ve konsola yazılıyor, yani hata ayıklama
          gerektiğinde erişilebilir. Görünürlük ile teşhis edilebilirlik ayrı şeyler.
      */}
      <div className="font-medium">{error.message}</div>
      {onRetry && (
        <button onClick={onRetry} className="mt-2 text-xs font-medium underline hover:no-underline">
          Tekrar dene
        </button>
      )}
    </div>
  )
}

export function EmptyState({ title, description, action }) {
  return (
    <div className="rounded-xl border border-dashed border-slate-300 bg-white/60 px-6 py-10 text-center">
      <p className="font-medium text-slate-700">{title}</p>
      {description && <p className="mx-auto mt-1 max-w-md text-sm text-slate-500">{description}</p>}
      {action && <div className="mt-4 flex justify-center">{action}</div>}
    </div>
  )
}

export function Field({ label, hint, children }) {
  return (
    <div>
      <label className="label">{label}</label>
      {children}
      {hint && <p className="mt-1 text-xs text-slate-500">{hint}</p>}
    </div>
  )
}

/**
 * @param genis  içeriği iki sütuna yayılan modaller için (max-w-lg → max-w-2xl).
 *   Varsayılan DAR bırakıldı: metin ve tek sütunlu formlar geniş kutuda okunması zor
 *   satırlara dönüşüyor. Genişlik bir seçenek, varsayılan değil.
 */
export function Modal({ open, title, onClose, children, footer, genis = false }) {
  useEffect(() => {
    if (!open) return
    const onKey = (e) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-slate-900/40 p-4 sm:items-center">
      {/* max-h-[90dvh]: mobilde modal ekranı asla aşamaz. dvh (vh değil) çünkü mobil
          tarayıcının adres çubuğu vh'ye dahil edilmez ve alt kısım kırpılırdı —
          kaydırılamayan bir "Kaydet" düğmesi demek olurdu. */}
      <div
        className={`flex max-h-[90dvh] w-full flex-col rounded-xl bg-white shadow-xl ${
          genis ? 'max-w-2xl' : 'max-w-lg'
        }`}
      >
        <div className="flex shrink-0 items-center justify-between border-b border-slate-200 px-5 py-3">
          <h3 className="font-semibold text-slate-800">{title}</h3>
          <button
            onClick={onClose}
            className="rounded p-1 text-slate-400 transition hover:bg-slate-100 hover:text-slate-600"
            aria-label="Kapat"
          >
            ✕
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {footer && (
          <div className="flex shrink-0 justify-end gap-2 border-t border-slate-200 px-5 py-3">
            {footer}
          </div>
        )}
      </div>
    </div>
  )
}

/** Kısa süreli bilgi/başarı bildirimi (sayfa üstünde). */
/**
 * Numaralı sayfalama.
 *
 * NEDEN "DAHA FAZLA YÜKLE" DEĞİL: eklemeli akış listeyi sonsuza kadar uzatıyor ve
 * kullanıcı 40. dersi gördükten sonra sayfanın altına ulaşmak için 40 satır kaydırmak
 * zorunda kalıyordu. Sayfa DEĞİŞTİRİLİR, eklenmez: liste hep aynı yükseklikte kalır.
 *
 * PENCERE MANTIĞI: 10 sayfadan sonra tüm numaraları basmak satırı taşırıyor. Aktif
 * sayfanın iki yanında birer komşu gösterilip arası "…" ile kısaltılıyor; ilk ve son
 * sayfa HER ZAMAN görünür, çünkü "en başa/en sona git" en sık istenen iki sıçrama.
 */
export function Pagination({ page, totalPages, onChange, disabled = false }) {
  if (!totalPages || totalPages < 2) return null

  const numaralar = []
  for (let i = 1; i <= totalPages; i++) {
    const kenar = i === 1 || i === totalPages
    const komsu = Math.abs(i - page) <= 1
    if (kenar || komsu) numaralar.push(i)
    else if (numaralar[numaralar.length - 1] !== '…') numaralar.push('…')
  }

  const kutu =
    'inline-flex min-h-11 min-w-11 items-center justify-center rounded-lg px-3 text-sm ' +
    'font-medium transition disabled:cursor-not-allowed disabled:opacity-40 lg:min-h-9 lg:min-w-9'

  return (
    <nav className="mt-4 flex flex-wrap items-center justify-center gap-1.5" aria-label="Sayfalama">
      <button
        type="button"
        className={`${kutu} border border-slate-200 bg-white text-slate-600 hover:bg-slate-50`}
        onClick={() => onChange(page - 1)}
        disabled={disabled || page <= 1}
        aria-label="Önceki sayfa"
      >
        ‹
      </button>

      {numaralar.map((n, i) =>
        n === '…' ? (
          <span key={`bosluk-${i}`} className="px-1 text-sm text-slate-400" aria-hidden="true">
            …
          </span>
        ) : (
          <button
            key={n}
            type="button"
            className={
              n === page
                ? `${kutu} bg-brand-600 text-white`
                : `${kutu} border border-slate-200 bg-white text-slate-600 hover:bg-slate-50`
            }
            onClick={() => onChange(n)}
            disabled={disabled}
            aria-label={`Sayfa ${n}`}
            aria-current={n === page ? 'page' : undefined}
          >
            {n}
          </button>
        ),
      )}

      <button
        type="button"
        className={`${kutu} border border-slate-200 bg-white text-slate-600 hover:bg-slate-50`}
        onClick={() => onChange(page + 1)}
        disabled={disabled || page >= totalPages}
        aria-label="Sonraki sayfa"
      >
        ›
      </button>
    </nav>
  )
}

export function Notice({ tone = 'success', children, onDismiss }) {
  if (!children) return null
  const tones = {
    success: 'border-emerald-200 bg-emerald-50 text-emerald-800',
    info: 'border-brand-200 bg-brand-50 text-brand-800',
    warning: 'border-amber-200 bg-amber-50 text-amber-900',
  }
  return (
    <div className={`flex items-start justify-between gap-3 rounded-lg border p-4 text-sm ${tones[tone]}`}>
      <div>{children}</div>
      {onDismiss && (
        <button onClick={onDismiss} className="text-xs underline hover:no-underline">
          kapat
        </button>
      )}
    </div>
  )
}
