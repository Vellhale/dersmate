import { EN_YUKSEK_SEVIYE, seviyeEtiketi, seviyeHesapla } from '../lib/seviye'

/*
  SEVİYE ROZETİ — üst barın sağ ucundaki tek işaret.

  Eski unvan rozeti emoji + kelimeydi (🌱 Çırak) ve iki sorunu vardı: emoji platformdan
  platforma değişiyordu (aynı rozet Windows'ta başka, iOS'ta başka görünüyordu) ve
  kelime, kaç basamak olduğunu söylemiyordu. Rakam ikisini de çözüyor.

  ROZET İKİ PARÇALI: koyu bir madalyon içinde rakam, yanında "Seviye" kelimesi. Dar
  ekranda kelime düşer, madalyon kalır — küçülen şey etiket, kimlik değil. Eski rozet
  mobilde unvan yerine ham puanı gösteriyordu, yani dar ekranda BAŞKA BİR ŞEY oluyordu.

  RENK: zemin brand-300, metin slate-900 (ölçülen kontrast 8.19:1 — Layout.jsx'teki
  kayıt). Madalyon ters çevriliyor: slate-900 zemin üstünde brand-300 rakam, aynı çift,
  aynı oran. Sabit hex YOK; renkler paletten sınıf adıyla geliyor (bkz.
  e2e/kaynak-sabitleri.spec.js — src altında #RRGGBB yasak).
*/
export function SeviyeRozeti({ kaynak, className = '' }) {
  const seviye = seviyeHesapla(kaynak)

  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-full bg-brand-300 py-1 pl-1 pr-2.5 text-xs font-semibold text-slate-900 sm:pr-3 ${className}`}
      title={`${seviyeEtiketi(seviye)} — ${EN_YUKSEK_SEVIYE} seviye üzerinden`}
    >
      <span
        className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-slate-900 text-[13px] font-bold leading-none text-brand-300 tabular-nums"
        aria-hidden="true"
      >
        {seviye}
      </span>
      {/* Görünen kelime dar ekranda düşüyor ama erişilebilir ad tam kalıyor: ekran
          okuyucu her boyutta "3. Seviye" duyar, sadece "3" değil. */}
      <span className="hidden sm:inline">Seviye</span>
      <span className="sr-only">{seviyeEtiketi(seviye)}</span>
    </span>
  )
}

/*
  Profil kartındaki büyük hâli. Aynı bilgiyi taşır ama satır içi bir çip değil, kendi
  başına duran bir blok: profilde bunun etrafında boşluk var ve rozet orada okunması
  gereken ilk şey.
*/
export function SeviyeKarti({ kaynak, kazanilanPuan }) {
  const seviye = seviyeHesapla(kaynak)

  return (
    <div className="flex items-center gap-3 rounded-xl border border-brand-200 bg-brand-50 px-4 py-3">
      <span
        className="grid h-11 w-11 shrink-0 place-items-center rounded-full bg-brand-600 text-lg font-bold text-white tabular-nums"
        aria-hidden="true"
      >
        {seviye}
      </span>
      <div className="min-w-0">
        <p className="text-sm font-semibold text-slate-900">{seviyeEtiketi(seviye)}</p>
        {/*
          Puan burada KALDI ama artık seviyenin gerekçesi olarak değil, ayrı bir sayaç
          olarak duruyor: seviye henüz sunucudan gelmediği için ikisi arasında bir eşitlik
          iddia etmiyoruz (bkz. lib/seviye.js).
        */}
        <p className="mt-0.5 text-xs text-slate-600">
          {kazanilanPuan != null ? `${kazanilanPuan} puan kazandın` : `${EN_YUKSEK_SEVIYE} seviye üzerinden`}
        </p>
      </div>
    </div>
  )
}
