import { useEffect, useId } from 'react'
import { Button } from './ui'

const SORTS = [
  { value: 'Relevance', label: 'Önerilen' },
  { value: 'Popular', label: 'Popüler' },
  { value: 'RatingDesc', label: 'Puanı yüksek' },
  { value: 'RatingAsc', label: 'Puanı düşük' },
  { value: 'Newest', label: 'En yeni' },
]

/*
  Eğitmen puanı eşikleri PILL, kaydırıcı değil: 0–5 arası 0.5 adımlı kaydırıcı on bir
  değer sunuyordu ama anlamlı eşik zaten üç tane (3.5 / 4.0 / 4.5) — "en az 1.5 puan"
  diye filtreleyen kullanıcı yok. Sıra katıdan gevşeğe: puana bakan önce en iyileri
  süzmek ister, "4.5+" ilk sırada. `value: null` = filtre kapalı; Discover'ın
  activeFilterCount ve filtersTouched sözleşmesi null'u "dokunulmamış" sayar, 0 değil —
  burada 0 kullanmak sayaç rozetini yanlışlıkla yakardı.
*/
const RATINGS = [
  { value: 4.5, label: '4.5+' },
  { value: 4, label: '4.0+' },
  { value: 3.5, label: '3.5+' },
  { value: null, label: 'Hepsi' },
]

/**
 * Filtre çekmecesi (Modül 1).
 *
 * Masaüstünde sol sütunda SABİT durur, mobilde alttan açılan tam ekran bir katman olur:
 * 375px'te kalıcı bir yan sütun, sonuç listesine yer bırakmazdı. İki ayrı bileşen yazmak
 * yerine tek bileşenin kapsayıcısı değişiyor — filtre mantığı tek yerde kalsın.
 */
export function FilterPanel({
  value,
  onChange,
  onReset,
  categories,
  resultCount,
  className = '',
}) {
  const set = (patch) => onChange({ ...value, ...patch, page: 1 })

  // Ağaç düz geliyor; kökler ve alt dallar burada ayrılıyor (bkz. catalog/categories).
  const roots = categories.filter((c) => !c.parentCategoryId)
  const childrenOf = (id) => categories.filter((c) => c.parentCategoryId === id)
  const selectedRoot =
    roots.find((r) => r.categoryId === value.categoryId) ??
    roots.find((r) => childrenOf(r.categoryId).some((c) => c.categoryId === value.categoryId))

  return (
    <div className={`space-y-5 ${className}`}>
      <section>
        <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-500">Kategori</p>
        <div className="flex flex-wrap gap-2">
          <Pill active={!value.categoryId} onClick={() => set({ categoryId: null })}>
            Tümü
          </Pill>
          {roots.map((root) => (
            <Pill
              key={root.categoryId}
              active={value.categoryId === root.categoryId}
              onClick={() => set({ categoryId: root.categoryId })}
            >
              {root.name}
            </Pill>
          ))}
        </div>

        {/* Alt kategoriler yalnızca bir kök seçiliyken: hepsini birden göstermek dar
            ekranda onlarca pill'lik bir duvar oluşturur. */}
        {selectedRoot && childrenOf(selectedRoot.categoryId).length > 0 && (
          <div className="mt-2 flex flex-wrap gap-2 border-l-2 border-slate-200/80 pl-3">
            {childrenOf(selectedRoot.categoryId).map((child) => (
              <Pill
                key={child.categoryId}
                small
                active={value.categoryId === child.categoryId}
                onClick={() =>
                  set({
                    // Seçili alt kategoriye tekrar basmak kökü geri getirir (aç/kapa).
                    categoryId:
                      value.categoryId === child.categoryId
                        ? selectedRoot.categoryId
                        : child.categoryId,
                  })
                }
              >
                {child.name}
              </Pill>
            ))}
          </div>
        )}
      </section>

      <section>
        <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-500">Sıralama</p>
        <div className="flex flex-wrap gap-2">
          {SORTS.map((sort) => (
            <Pill
              key={sort.value}
              small
              active={value.sort === sort.value}
              onClick={() => set({ sort: sort.value })}
            >
              {sort.label}
            </Pill>
          ))}
        </div>
      </section>

      <RangeField
        label="Eğitmenin konu seviyesi"
        hint="Eğitmenin o konudaki öz değerlendirmesi (1–5)."
        min={1}
        max={5}
        step={1}
        value={value.minLevel ?? 1}
        onChange={(v) => set({ minLevel: v === 1 ? null : v })}
        display={value.minLevel ? `${value.minLevel} ve üzeri` : 'Hepsi'}
      />

      <section>
        <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-500">
          Eğitmen puanı
        </p>
        <div className="flex flex-wrap gap-2">
          {RATINGS.map((rating) => (
            <Pill
              key={rating.label}
              small
              active={value.minRating === rating.value}
              onClick={() => set({ minRating: rating.value })}
            >
              {rating.label}
            </Pill>
          ))}
        </div>
        <p className="mt-1 text-xs text-slate-500">Aldığı değerlendirmelerin ortalaması.</p>
      </section>

      {/* Gönüllülük onay kutusu kaldırıldı: ilanlar arasında böyle bir ayrım kalmadı,
          filtrelenecek bir nitelik de yok. */}

      {/*
        Sonuç sayısı YOKKEN satır boş kalır, tire konmaz.

        `resultCount` yalnızca arama kipinde geliyor (bkz. Discover.jsx); onun dışında
        null. Eskiden bu durumda çıplak bir "—" yazılıyordu ve mobil çekmecede —
        "Filtreleri temizle" düğmesinin yanında tek başına duran bir tire olarak —
        yüklenememiş bir değer, yani kırık bir şey gibi okunuyordu. Boş `span`
        justify-between'ı bozmadan aynı hizayı koruyor: düğme yine sağda kalıyor.
      */}
      <div className="flex items-center justify-between gap-3 border-t border-slate-200/80 pt-4">
        <span className="text-sm text-slate-500">
          {resultCount === null ? '' : `${resultCount} sonuç`}
        </span>
        <Button variant="secondary" onClick={onReset}>
          Filtreleri temizle
        </Button>
      </div>
    </div>
  )
}

/*
  Tek pill (chip) — panelin bütün seçim gruplarının ortak düğmesi.

  aria-pressed: bu düğmeler görsel olarak buton, anlamsal olarak aç/kapa; ekran okuyucu
  "basılı" durumunu ancak bu öznitelikle duyurur. Aktif hâldeki shadow-sm, seçili pill'i
  beyaz zeminli komşularından yalnız renkle değil derinlikle de ayırıyor. Pasif kenarlık
  slate-300 → slate-200: .input'la aynı gerekçe (index.css) — sınır zaten zemin
  karşıtlığıyla okunuyor, koyu çerçeve gereksiz ağırlık. active:bg-brand-50 dokunmatik
  için: hover orada yok, "tıklamam algılandı" hissi basma anındaki bu renkten gelir.
  min-h-11 lg altında 44px dokunma hedefini korur.
*/
function Pill({ active, small, onClick, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`min-h-11 rounded-full border px-3 font-medium transition lg:min-h-0 lg:py-1.5 ${
        small ? 'text-xs' : 'text-sm'
      } ${
        active
          ? 'border-brand-500 bg-brand-600 text-white shadow-sm'
          : 'border-slate-200 bg-white text-slate-600 hover:border-brand-300 hover:text-brand-700 active:bg-brand-50'
      }`}
    >
      {children}
    </button>
  )
}

function RangeField({ label, hint, min, max, step, value, onChange, display }) {
  return (
    <section>
      <div className="mb-1 flex items-baseline justify-between gap-2">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</p>
        <span className="text-sm font-medium text-brand-700">{display}</span>
      </div>
      {/* h-11: kaydırıcının doğal yüksekliği 16px ve parmakla tutulamıyor. Tarayıcılar
          şeridi öğenin içinde dikey ortaladığı için görünüm değişmez, yalnızca dokunma
          alanı 44px olur. Masaüstünde doğal ölçüye dönülüyor. */}
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="h-11 w-full accent-brand-600 lg:h-auto"
        aria-label={label}
      />
      <p className="mt-1 text-xs text-slate-500">{hint}</p>
    </section>
  )
}

/**
 * Üniversite ağı filtre paneli (Modül 1'in yanındaki ikinci keşif kipi).
 *
 * ─── NEDEN YALNIZCA İKİ ALAN ───────────────────────────────────────────────────
 * Üniversite ağı DERS/KONU KAVRAMI TAŞIMIYOR. Buradaki kayıt bir ilan değil, bir
 * kişinin okuduğu yer: üniversite + bölüm. Kategori ağacı, konu seviyesi (1–5) ve
 * eğitmen puanı bu veride KARŞILIĞI OLMAYAN alanlar — hepsi katalog ilanına bağlı.
 * FilterPanel'i kopyalayıp "şimdilik boş dursun" demek, kullanıcıya hiçbir zaman
 * sonucu değiştirmeyecek denetimler göstermek olurdu; filtrenin sessizce çalışmaması,
 * olmamasından daha kötü.
 *
 * Bu bir ÜRÜN KARARIDIR, eksik iş değil: ders/konu filtreleri buraya sonradan da
 * eklenmez — eklenecekse önce veri modelinin o kavramı kazanması gerekir.
 * ───────────────────────────────────────────────────────────────────────────────
 *
 * Görsel dil FilterPanel ile birebir aynı (başlık biçimi, 5'lik boşluk ritmi, alt
 * satır); iki panel aynı çekmecede yan yana görülebiliyor, ayrışmaları gerekmiyor.
 * FilterDrawer'a çocuk olarak verilebilir — çekmece içeriğinden habersizdir.
 */
export function UniversiteFiltrePaneli({ value, onChange, onReset, resultCount }) {
  /*
    FilterPanel'deki `set` kalıbının aynısı; `page: 1` kasıtlı. Üniversite adını
    daraltırken 7. sayfada kalmak, sonuç kümesi küçüldüğü için boş sayfa gösterirdi.
  */
  const set = (patch) => onChange({ ...value, ...patch, page: 1 })

  /*
    useId: panel Discover'da HEM masaüstü sütununda HEM mobil çekmecede aynı anda
    monte olabiliyor. Sabit bir id iki kez basılır, label'lar ilk girdiye bağlanır ve
    "Bölüm" etiketine tıklamak odağı Üniversite'ye taşırdı.
  */
  const idOnEki = useId()
  const universiteId = `universite-${idOnEki}`
  const bolumId = `bolum-${idOnEki}`

  return (
    <div className="space-y-5">
      <section>
        <label
          htmlFor={universiteId}
          className="mb-2 block text-xs font-medium uppercase tracking-wide text-slate-500"
        >
          Üniversite
        </label>
        <input
          id={universiteId}
          type="text"
          className="input"
          placeholder="Üniversite adı ara…"
          value={value.university ?? ''}
          onChange={(e) => set({ university: e.target.value })}
        />
      </section>

      <section>
        <label
          htmlFor={bolumId}
          className="mb-2 block text-xs font-medium uppercase tracking-wide text-slate-500"
        >
          Bölüm
        </label>
        <input
          id={bolumId}
          type="text"
          className="input"
          placeholder="Bölüm ara…"
          value={value.department ?? ''}
          onChange={(e) => set({ department: e.target.value })}
        />
      </section>

      {/* Alt satır FilterPanel'in aynısı — boş `span` gerekçesi orada yazılı: sonuç
          sayısı yokken tire basmak, yüklenememiş bir değer gibi okunuyordu. */}
      <div className="flex items-center justify-between gap-3 border-t border-slate-200/80 pt-4">
        <span className="text-sm text-slate-500">
          {resultCount === null ? '' : `${resultCount} sonuç`}
        </span>
        <Button variant="secondary" onClick={onReset}>
          Filtreleri temizle
        </Button>
      </div>
    </div>
  )
}

/**
 * Mobilde filtreleri alttan açan katman.
 *
 * ─── KATMAN ARTIK GERÇEKTEN KİPSEL (2026-08-24) ──────────────────────────────
 * Görünüşü baştan beri bir kip (modal) idi ama davranışı değildi ve telefonda üç ayrı
 * şekilde ısırıyordu:
 *
 *  1. PERDEYE DOKUNMAK KAPATMIYORDU. Alttan açılan bir çekmeceyi kapatmanın en
 *     beklenen yolu üstündeki karartıya dokunmaktır; burada tek çıkış sağ üstteki
 *     ✕ idi, yani başparmağın en uzak köşesi.
 *  2. ARKA PLAN KAYIYORDU. Perde `fixed inset-0` ile ekranı kaplıyor ama kendi
 *     kaydırılabilir içeriği olmadığı için parmak hareketi ALTTAKİ sayfaya geçiyordu:
 *     çekmece kapanınca kullanıcı listenin bambaşka bir yerinde buluyordu kendini.
 *     `overscroll-contain` panelin kendi kaydırma zincirini keser, gövdedeki
 *     `overflow:hidden` de perdeye düşen dokunuşu durdurur — ikisi birlikte gerekli.
 *  3. EKRAN OKUYUCU KİP OLDUĞUNU BİLMİYORDU. role/aria-modal yoktu; katman açıkken
 *     arkadaki sayfa hâlâ gezilebilir görünüyordu.
 *
 * Esc de bağlandı: klavyeli bir tablet ya da masaüstü tarayıcı dar pencerede bu
 * çekmeceyi görüyor ve orada Esc, kapatmanın standart yolu.
 * ─────────────────────────────────────────────────────────────────────────────
 */
export function FilterDrawer({ open, onClose, children }) {
  /*
    Esc dinleyicisi ve gövde kilidi TEK etkide: ikisinin de ömrü aynı (katman açık
    olduğu süre) ve ikisi de aynı temizliği istiyor. Ayrı efektlere bölmek, birini
    temizlemeyi unutmayı kolaylaştırırdı.

    Kilit, önceki `overflow` değerini geri yazar — sabit bir '' yazmak, başka bir
    bileşenin (ileride bir kip daha) koyduğu kilidi sessizce açardı.
  */
  useEffect(() => {
    if (!open) return

    const oncekiOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    const esc = (e) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', esc)

    return () => {
      document.body.style.overflow = oncekiOverflow
      document.removeEventListener('keydown', esc)
    }
  }, [open, onClose])

  if (!open) return null

  return (
    /*
      Perde tıklaması: onClick PERDEDE, panelde stopPropagation YOK — panelin kendi
      onClick'i olmadığı için olay perdeye kadar çıkar ve panel içindeki her dokunuş
      çekmeceyi kapatırdı. `e.target === e.currentTarget` kontrolü, olayın gerçekten
      perdenin kendisinden geldiğini söyler; stopPropagation'a göre daha dar bir söz
      veriyor: panel içindeki bileşenlerin olay yayılımını bozmuyor.
    */
    <div
      className="fixed inset-0 z-50 flex items-end bg-slate-900/40 lg:hidden"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="filtre-cekmecesi-baslik"
    >
      <div className="flex max-h-[85dvh] w-full flex-col rounded-t-2xl bg-white">
        <div className="flex shrink-0 items-center justify-between border-b border-slate-200/80 px-5 py-3">
          <h3 id="filtre-cekmecesi-baslik" className="font-semibold text-slate-800">
            Filtreler
          </h3>
          <button
            onClick={onClose}
            className="grid h-11 w-11 place-items-center rounded-lg text-slate-400 hover:bg-slate-100"
            aria-label="Kapat"
          >
            ✕
          </button>
        </div>

        <div className="flex-1 overflow-y-auto overscroll-contain px-5 py-4">{children}</div>

        <div className="shrink-0 border-t border-slate-200/80 px-5 py-3">
          <Button className="w-full" onClick={onClose}>
            Sonuçları göster
          </Button>
        </div>
      </div>
    </div>
  )
}
