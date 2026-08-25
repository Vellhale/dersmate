import { useCallback, useEffect, useRef, useState } from 'react'

/*
  ══════════════════════════════════════════════════════════════════════════════
  YATAY RAF — kartları alt alta değil YAN YANA dizen kaydırma şeridi.

  ─── NEDEN IZGARA DEĞİL ───────────────────────────────────────────────────────
  Keşfet sonuçları dikey ızgaradayken sayfa uzuyordu. Filtre sütunu solda 260px
  ve kendi yüksekliğinde bitiyor; sonuç listesi onu geçtiği anda aşağı inildikçe
  ekranın SOL YARISI boş kalıyordu. Sorun kart tasarımında değil, listenin
  büyüme yönündeydi: dikey büyüyen bir liste, sabit yükseklikteki bir sütunun
  yanında er ya da geç yalnız kalır.

  Sonuçlar tek satırda yatay kayınca sayfa yüksekliği sabitleniyor — filtre
  sütunu her zaman sonuçların hizasında duruyor ve boşluk hiç oluşmuyor.

  ─── ÜÇ GİRDİ YOLU, ÜÇÜ DE ZORUNLU ────────────────────────────────────────────
    • dokunma / trackpad → doğal yatay kaydırma (overflow-x-auto)
    • fare               → kenarlardaki ok düğmeleri
    • klavye             → ← / → tuşları (şerit odaktayken)

  Yalnızca ok düğmesi koymak klavyeyle gezen kullanıcıyı rafın içine hapsederdi:
  odaklanabilir bir kaydırma kabı, ok tuşlarıyla kaydırılabilmek ZORUNDA — tarayıcı
  bunu dikey kaydırmada kendiliğinden yapar, yatayda yapmaz.

  ─── ÖLÇÜLEREK ALINMIŞ ÜÇ KARAR ───────────────────────────────────────────────
  1. ADIM, EKRAN GENİŞLİĞİ DEĞİL KART GENİŞLİĞİNİN KATI. `scrollBy(clientWidth)`
     yazmak kolaydı ama kabın genişliği kart+boşluk birimine tam bölünmediği için
     her adımda yarım kart görünür kalıyor ve kayma birikiyordu. Adım artık
     "kaça tam sığıyorsa o kadar kart" olarak hesaplanıyor; hizalama bozulmuyor.

  2. SNAP `proximity`, `mandatory` DEĞİL. Mandatory'de listenin SONUNDA tarayıcı
     scrollWidth'e dayanıyor, orası bir snap noktası olmadığı için bir kart geri
     çekiyor ve son kart tam görünemiyordu. Proximity yakınken hizalıyor, uçta
     direnmiyor.

  3. `overscroll-contain` — trackpad'de yatay savurma, kabın sonuna gelince
     tarayıcının GERİ GİT hareketini tetikliyordu. Kullanıcı listeyi kaydırırken
     sayfadan atılıyordu.
  ══════════════════════════════════════════════════════════════════════════════
*/

/** Bir adımda kaç piksel kayılacağı: kaba tam sığan kart sayısı × (kart + boşluk). */
function adimHesapla(el) {
  const ilk = el.firstElementChild
  if (!ilk) return el.clientWidth

  const kart = ilk.getBoundingClientRect().width
  if (kart <= 0) return el.clientWidth

  // columnGap boş dönebilir (gap yalnızca kısayolla verilmişse); 0'a düşmek güvenli.
  const aralik = Number.parseFloat(window.getComputedStyle(el).columnGap) || 0
  const birim = kart + aralik

  // +aralik: son kartın sağında boşluk gerekmediği için sığan sayı bir eksik çıkıyordu.
  const sigan = Math.max(1, Math.floor((el.clientWidth + aralik) / birim))
  return birim * sigan
}

function yumusakKaydirmaAcikMi() {
  return !window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/**
 * Yatay kaydırılan kart rafı.
 *
 * @param etiket  ekran okuyucuya rafın ne listelediğini söyler (`aria-label`).
 *   Zorunlu: etiketsiz bir `role="group"` odaklanınca "grup" diye okunur ve
 *   kullanıcı neyin içinde olduğunu bilemez.
 * @param children  kartlar. Her biri kendi genişliğini taşımalı (`shrink-0`) —
 *   flex kabında esneyen bir kart, raf uzadıkça incelir.
 */
export function YatayRaf({ etiket, children, className = '' }) {
  const rafRef = useRef(null)
  const [solaGidebilir, setSolaGidebilir] = useState(false)
  const [sagaGidebilir, setSagaGidebilir] = useState(false)

  const durumGuncelle = useCallback(() => {
    const el = rafRef.current
    if (!el) return

    /*
      1 PİKSEL TOLERANS. Tarayıcılar scrollLeft'i kesirli üretiyor (özellikle
      yakınlaştırma açıkken) ve `scrollLeft + clientWidth === scrollWidth` eşitliği
      pratikte hiç tutmuyor. Toleranssız yazıldığında sağ ok, listenin sonuna
      gelinmiş olmasına rağmen etkin kalıyordu.
    */
    setSolaGidebilir(el.scrollLeft > 1)
    setSagaGidebilir(el.scrollLeft + el.clientWidth < el.scrollWidth - 1)
  }, [])

  // Bağlanma: kaydırma olayı + kabın yeniden boyutlanması.
  useEffect(() => {
    const el = rafRef.current
    if (!el) return undefined

    el.addEventListener('scroll', durumGuncelle, { passive: true })
    const gozlemci = new ResizeObserver(durumGuncelle)
    gozlemci.observe(el)

    return () => {
      el.removeEventListener('scroll', durumGuncelle)
      gozlemci.disconnect()
    }
  }, [durumGuncelle])

  /*
    İÇERİK DEĞİŞİNCE YENİDEN ÖLÇ. Sayfa değiştirmek ya da filtre uygulamak kartları
    yeniler ama ne `scroll` olayı ne de `resize` üretir — kap aynı boyutta kalır,
    yalnızca scrollWidth değişir. Bu efekt olmadan, 3 sonuçlu bir sayfaya geçildiğinde
    sağ ok etkin görünmeye devam ediyordu.
  */
  useEffect(durumGuncelle)

  const kaydir = useCallback((yon) => {
    const el = rafRef.current
    if (!el) return

    el.scrollBy({
      left: yon * adimHesapla(el),
      behavior: yumusakKaydirmaAcikMi() ? 'smooth' : 'auto',
    })
  }, [])

  function tusla(olay) {
    if (olay.key !== 'ArrowLeft' && olay.key !== 'ArrowRight') return

    /*
      Kartın içindeki bir yazı alanında ok tuşu İMLECİ taşır. Rafın onu kapması,
      kullanıcının yazdığı yerde gezinmesini engellerdi. Bugün bu kartlarda girdi
      yok ama kart içeriği büyüyen bir yer; kuralı şimdi koymak sonra aramaktan ucuz.
    */
    const hedef = olay.target
    if (
      hedef instanceof HTMLElement &&
      (hedef.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(hedef.tagName))
    ) {
      return
    }

    olay.preventDefault()
    kaydir(olay.key === 'ArrowRight' ? 1 : -1)
  }

  return (
    <div className={`relative ${className}`}>
      <OkDugmesi yon="sol" etkin={solaGidebilir} onClick={() => kaydir(-1)} />

      {/*
        tabIndex={0}: kaydırılabilir kabın klavyeyle odaklanabilmesi bir erişilebilirlik
        gereği — odaklanamayan bir kabın içeriğine klavye kullanıcısı ulaşamaz.

        pt-2: kartların hover gölgesi `overflow-x-auto` tarafından kırpılıyordu
        (yatayda auto verilince tarayıcı dikeyi de auto sayar).
        pb-3: ince kaydırma çubuğuna yer.
      */}
      <div
        ref={rafRef}
        tabIndex={0}
        role="group"
        aria-label={etiket}
        onKeyDown={tusla}
        className="kaydirma-ince flex snap-x snap-proximity gap-4 overflow-x-auto overscroll-x-contain pb-3 pt-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2"
      >
        {children}
      </div>

      <OkDugmesi yon="sag" etkin={sagaGidebilir} onClick={() => kaydir(1)} />
    </div>
  )
}

/**
 * Kenardaki ok düğmesi.
 *
 * GİZLEMEK YERİNE `disabled`: düğme uçta yok olsaydı raf yanıp sönerek genişler,
 * bir sonraki adımda geri gelirdi. Sönük ama yerinde duran düğme, listenin nerede
 * bittiğini de söylüyor.
 *
 * Dokunma boyutu 44px (lg altı), fare boyutu 36px — projenin kırılım kuralı:
 * `lg` dokunma sınırıdır, `sm` değil (tablet hâlâ parmakla kullanılıyor).
 */
function OkDugmesi({ yon, etkin, onClick }) {
  const sol = yon === 'sol'

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={!etkin}
      aria-label={sol ? 'Önceki kartlar' : 'Sonraki kartlar'}
      className={`absolute top-1/2 z-10 grid h-11 w-11 -translate-y-1/2 place-items-center rounded-full border border-slate-200 bg-white/95 text-slate-600 shadow-lg backdrop-blur transition hover:border-brand-300 hover:text-brand-700 disabled:pointer-events-none disabled:opacity-0 lg:h-9 lg:w-9 ${
        sol ? 'left-1' : 'right-1'
      }`}
    >
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        className="h-5 w-5"
        aria-hidden="true"
      >
        <path d={sol ? 'M15 18l-6-6 6-6' : 'M9 18l6-6-6-6'} />
      </svg>
    </button>
  )
}
