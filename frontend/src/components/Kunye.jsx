import {
  ADRES,
  ILETISIM_EPOSTA,
  ISLETMECI,
  ISLETMECI_ADRESI,
  ISLETMECI_ALAN_ADI,
  MARKA,
  MERSIS,
  TELIF_YILI,
  TESCIL_BILGISI_VAR,
  TICARI_UNVAN,
} from '../lib/kunye'

/**
 * KÜNYE — iki boy, tek kaynak.
 *
 *   • <KunyeSatiri />  → altbilgilerdeki tek satır ("© 2026 dersmate · Bir Corventech…")
 *   • <KunyeBlogu />   → yasal metinlerin altındaki kimlik bloğu
 *
 * İkisi de lib/kunye.js'ten besleniyor; oradaki not neden bu bilginin ürünün içinde
 * bulunması gerektiğini (KVKK m.10, 6563 tanıtıcı bilgi, mağaza kaydı) anlatıyor.
 */

/**
 * Tek satırlık künye. Altbilgilerde (Layout + AuthShell) kullanılır.
 *
 * ⚠️ DIŞ BAĞLANTI: `rel="noopener noreferrer"` ŞART. `target="_blank"` ile açılan
 * sayfa, bunlar olmadan `window.opener` üzerinden bu sekmeyi başka bir adrese
 * yönlendirebilir (tabnabbing) — ve o sekme kullanıcının OTURUM AÇMIŞ olduğu sekmedir.
 * Modern tarayıcılar `noopener`ı artık varsayılan sayıyor ama bu bir tarayıcı sürümü
 * varsayımı; bağlantının kendisi güvenli olmalı.
 */
export function KunyeSatiri({ className = '' }) {
  return (
    <p className={`text-xs leading-relaxed text-slate-500 ${className}`}>
      © {TELIF_YILI} {MARKA} · Bir{' '}
      {/* Dokunma hedefi: CLAUDE.md'ye göre 44px sınırı `lg`, `sm` değil — tablet de
          parmakla kullanılıyor. Aynı kalıp CookieSettingsLink'te de var. */}
      <a
        href={ISLETMECI_ADRESI}
        target="_blank"
        rel="noopener noreferrer"
        className="-my-2 inline-flex min-h-11 items-center py-2 font-medium underline
                   hover:text-slate-700 lg:my-0 lg:min-h-0 lg:py-0"
      >
        {ISLETMECI}
      </a>{' '}
      ürünüdür
    </p>
  )
}

/**
 * Yasal metinlerin altındaki kimlik bloğu (MetinSayfasi).
 *
 * ─── SATIRLAR KOŞULLU ───────────────────────────────────────────────────────
 * Ticari unvan / adres / MERSIS, lib/kunye.js'te `null` olduğu sürece HİÇ ÇİZİLMEZ —
 * "Ticari unvan: —" gibi boş bir satır, bilgiyi vermemekten daha kötü görünür ve
 * doldurulmamış bir alanı doldurulmuş gibi gösterir. Üçü doldurulduğu anda blok
 * kendiliğinden tamamlanır; burada değişiklik gerekmez.
 *
 * "… tarafından işletilmektedir" satırı KOŞULSUZ: Corventech tescilli bir tüzel kişi
 * de olsa yalnızca bir marka da olsa bu ifade doğru. Bloğun bugün bile taşıdığı asıl
 * değer bu satır — dün ürünün hiçbir yerinde yazmıyordu.
 */
export function KunyeBlogu({ className = '' }) {
  return (
    <div className={className}>
      <p className="text-sm font-semibold text-slate-800">Künye</p>

      <p className="mt-2 text-sm leading-relaxed text-slate-600">
        {MARKA},{' '}
        <a
          href={ISLETMECI_ADRESI}
          target="_blank"
          rel="noopener noreferrer"
          className="font-medium text-brand-700 hover:underline"
        >
          {ISLETMECI}
        </a>{' '}
        ({ISLETMECI_ALAN_ADI}) tarafından işletilmektedir.
      </p>

      {TESCIL_BILGISI_VAR && (
        /* <dl>: bunlar etiket-değer çiftleri, serbest metin değil. Ekran okuyucu
           "Ticari unvan" ile değerini ilişkilendirebilsin diye. */
        <dl className="mt-3 space-y-1.5 text-sm text-slate-600">
          {TICARI_UNVAN && <KunyeSatir etiket="Ticari unvan" deger={TICARI_UNVAN} />}
          {ADRES && <KunyeSatir etiket="Adres" deger={ADRES} />}
          {MERSIS && <KunyeSatir etiket="MERSIS no" deger={MERSIS} />}
        </dl>
      )}

      <p className="mt-3 text-sm leading-relaxed text-slate-600">
        Sorular, talepler ve KVKK başvuruları için:{' '}
        <a
          href={`mailto:${ILETISIM_EPOSTA}`}
          className="font-medium text-brand-700 hover:underline"
        >
          {ILETISIM_EPOSTA}
        </a>
      </p>
    </div>
  )
}

/** Künye bloğundaki tek etiket-değer satırı. */
function KunyeSatir({ etiket, deger }) {
  return (
    <div className="flex flex-wrap gap-x-2">
      <dt className="shrink-0 text-slate-500">{etiket}:</dt>
      <dd className="min-w-0 text-slate-700">{deger}</dd>
    </div>
  )
}
