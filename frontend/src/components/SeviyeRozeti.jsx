import { EN_YUKSEK_SEVIYE, seviyeEtiketi, seviyeHesapla, seviyeIlerlemeMetni } from '../lib/seviye'

/*
  SEVİYE ROZETİ — üst barın sağ ucundaki ve profil başlığındaki tek işaret.

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

  İLERLEME TOOLTIP'TE, ROZETTE DEĞİL: "sonraki seviyeye 250 puan" bilgisi rozetin
  içine sığmıyor ve üst barda her sayfada duran bir öğenin sürekli değişen bir sayı
  taşıması gürültü olurdu. Rakam kimliği, tooltip ayrıntıyı veriyor.
*/
export function SeviyeRozeti({ kaynak, className = '' }) {
  const seviye = seviyeHesapla(kaynak)

  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-full bg-brand-300 py-1 pl-1 pr-2.5 text-xs font-semibold text-slate-900 sm:pr-3 ${className}`}
      title={`${seviyeEtiketi(seviye)} (${EN_YUKSEK_SEVIYE} üzerinden) — ${seviyeIlerlemeMetni(kaynak)}`}
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
