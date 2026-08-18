import { useEffect, useMemo, useRef, useState } from 'react'

/**
 * Aranabilir seçim kutusu (combobox).
 *
 * NEDEN VAR: katalog 767 konuya çıktı. Düz bir <select> ile "Türev"i bulmak, açılır
 * listede sekiz dersin altındaki yüzlerce satırı kaydırmak demekti — tarayıcının kendi
 * "yazarak atlama" davranışı da yalnızca baş harfe bakıyor ve gruplu listede işe yaramıyor.
 *
 * İKİ YOL BİRDEN AÇIK: kullanıcı yazarak süzebilir ya da hiçbir şey yazmadan listeyi
 * gezebilir. Yalnızca arama bırakılsaydı "ne var burada" sorusu cevapsız kalırdı; yalnızca
 * liste bırakılsaydı bilinen bir konuyu bulmak yine kaydırma işi olurdu.
 *
 * ARAMA TÜRKÇE KARAKTERE DUYARSIZ: "turev" yazan da "Türev"i bulur. Kullanıcıların
 * çoğu telefonda İ/ı ayrımını dert etmeden yazıyor; eşleşmemesi "konu yok" gibi görünürdü.
 *
 * ERİŞİLEBİLİRLİK: WAI-ARIA combobox deseni — input'ta role=combobox, listede role=listbox,
 * aktif satır aria-activedescendant ile bildirilir. Ok tuşları, Enter, Escape çalışır;
 * fare olmadan da kullanılabilir.
 */

/** Türkçe karakterleri sadeleştirip küçültür — arama karşılaştırması için. */
function sadelestir(metin) {
  return (metin ?? '')
    .replace(/[çÇ]/g, 'c')
    .replace(/[ğĞ]/g, 'g')
    .replace(/[ıİ]/g, 'i')
    .replace(/[öÖ]/g, 'o')
    .replace(/[şŞ]/g, 's')
    .replace(/[üÜ]/g, 'u')
    .toLocaleLowerCase('tr')
}

/**
 * @param {Array<{value: string, label: string, group?: string}>} options
 * @param {string} value  seçili değer (controlled)
 * @param {(value: string) => void} onChange
 */
export function SearchableSelect({
  options,
  value,
  onChange,
  placeholder = 'Yazarak ara ya da listeden seç…',
  emptyLabel = 'Eşleşen kayıt yok',
  id = 'aranabilir-secim',
}) {
  const [acik, setAcik] = useState(false)
  const [sorgu, setSorgu] = useState('')
  const [aktif, setAktif] = useState(0)
  const sarmalayici = useRef(null)
  const listeRef = useRef(null)

  const secili = useMemo(() => options.find((o) => o.value === value) ?? null, [options, value])

  // Süzme: sorgu boşken TAM liste görünür (gezinme yolu açık kalsın).
  const suzulmus = useMemo(() => {
    const q = sadelestir(sorgu).trim()
    if (!q) return options
    return options.filter(
      (o) => sadelestir(o.label).includes(q) || sadelestir(o.group).includes(q),
    )
  }, [options, sorgu])

  // Sorgu değişince vurgu başa döner; aksi halde eski indeks yeni listede alakasız satırı gösterir.
  useEffect(() => setAktif(0), [sorgu])

  // Dışarı tıklanınca kapan.
  useEffect(() => {
    if (!acik) return
    function disariTiklandi(e) {
      if (!sarmalayici.current?.contains(e.target)) setAcik(false)
    }
    document.addEventListener('mousedown', disariTiklandi)
    return () => document.removeEventListener('mousedown', disariTiklandi)
  }, [acik])

  // Klavyeyle gezerken aktif satır görüş alanında kalsın.
  useEffect(() => {
    if (!acik) return
    listeRef.current
      ?.querySelector(`[data-indeks="${aktif}"]`)
      ?.scrollIntoView({ block: 'nearest' })
  }, [aktif, acik])

  function sec(secenek) {
    onChange(secenek.value)
    setSorgu('')
    setAcik(false)
  }

  function tusaBasildi(e) {
    if (!acik && (e.key === 'ArrowDown' || e.key === 'Enter')) {
      setAcik(true)
      e.preventDefault()
      return
    }
    if (!acik) return

    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setAktif((i) => Math.min(i + 1, suzulmus.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setAktif((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      if (suzulmus[aktif]) sec(suzulmus[aktif])
    } else if (e.key === 'Escape') {
      setAcik(false)
      setSorgu('')
    }
  }

  // Kapalıyken seçili etiketi, açıkken yazılan sorguyu göster.
  const gosterilen = acik ? sorgu : secili?.label ?? ''

  let oncekiGrup = null

  return (
    <div ref={sarmalayici} className="relative">
      <input
        id={id}
        type="text"
        role="combobox"
        aria-expanded={acik}
        aria-controls={`${id}-liste`}
        aria-autocomplete="list"
        aria-activedescendant={acik && suzulmus[aktif] ? `${id}-secenek-${aktif}` : undefined}
        className="input"
        value={gosterilen}
        placeholder={secili && !acik ? secili.label : placeholder}
        onChange={(e) => {
          setSorgu(e.target.value)
          if (!acik) setAcik(true)
        }}
        onFocus={() => setAcik(true)}
        onKeyDown={tusaBasildi}
        autoComplete="off"
      />

      {/* Seçiliyken temizleme yolu: yalnızca listeden başka bir şey seçerek değiştirilebilseydi
          "seçimimi geri alayım" mümkün olmazdı. */}
      {secili && !acik && (
        <button
          type="button"
          onClick={() => onChange('')}
          className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-slate-400
                     hover:bg-slate-100 hover:text-slate-600"
          aria-label="Seçimi temizle"
        >
          ×
        </button>
      )}

      {acik && (
        <ul
          ref={listeRef}
          id={`${id}-liste`}
          role="listbox"
          className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border
                     border-slate-200 bg-white py-1 shadow-lg"
        >
          {suzulmus.length === 0 && (
            <li className="px-3 py-2 text-sm text-slate-500">{emptyLabel}</li>
          )}

          {suzulmus.map((secenek, i) => {
            const grupBasligi = secenek.group && secenek.group !== oncekiGrup ? secenek.group : null
            oncekiGrup = secenek.group
            return (
              <li key={secenek.value}>
                {grupBasligi && (
                  <div className="px-3 pb-1 pt-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
                    {grupBasligi}
                  </div>
                )}
                <div
                  id={`${id}-secenek-${i}`}
                  data-indeks={i}
                  role="option"
                  aria-selected={secenek.value === value}
                  onMouseEnter={() => setAktif(i)}
                  onMouseDown={(e) => e.preventDefault()} // input blur'u engelle
                  onClick={() => sec(secenek)}
                  className={`cursor-pointer px-3 py-2 text-sm ${
                    i === aktif ? 'bg-brand-50 text-brand-800' : 'text-slate-700'
                  } ${secenek.value === value ? 'font-semibold' : ''}`}
                >
                  {secenek.label}
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
