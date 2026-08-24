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

      <div className="flex items-center justify-between gap-3 border-t border-slate-200/80 pt-4">
        <span className="text-sm text-slate-500">
          {resultCount === null ? '—' : `${resultCount} sonuç`}
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

/** Mobilde filtreleri alttan açan katman. */
export function FilterDrawer({ open, onClose, children }) {
  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-end bg-slate-900/40 lg:hidden">
      <div className="flex max-h-[85dvh] w-full flex-col rounded-t-2xl bg-white">
        <div className="flex shrink-0 items-center justify-between border-b border-slate-200/80 px-5 py-3">
          <h3 className="font-semibold text-slate-800">Filtreler</h3>
          <button
            onClick={onClose}
            className="grid h-11 w-11 place-items-center rounded-lg text-slate-400 hover:bg-slate-100"
            aria-label="Kapat"
          >
            ✕
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>

        <div className="shrink-0 border-t border-slate-200/80 px-5 py-3">
          <Button className="w-full" onClick={onClose}>
            Sonuçları göster
          </Button>
        </div>
      </div>
    </div>
  )
}
