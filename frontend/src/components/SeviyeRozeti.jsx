import { EN_YUKSEK_SEVIYE, seviyeEtiketi, seviyeHesapla, seviyeIlerlemeMetni } from '../lib/seviye'

/*
  SEVİYE ROZETİ — üst barda ve profil başlığında görünen tek işaret.

  Eski unvan rozeti emoji + kelimeydi (🌱 Çırak) ve iki sorunu vardı: emoji platformdan
  platforma değişiyordu (aynı rozet Windows'ta başka, iOS'ta başka görünüyordu) ve
  kelime, kaç basamak olduğunu söylemiyordu. Rakam ikisini de çözüyor.

  ROZET İKİ PARÇALI: madalyon içinde rakam, yanında "Seviye" kelimesi. Dar ekranda kelime
  düşer, madalyon kalır — küçülen şey etiket, kimlik değil. Eski rozet mobilde unvan
  yerine ham puanı gösteriyordu, yani dar ekranda BAŞKA BİR ŞEY oluyordu.

  ─────────────────────────────────────────────────────────────────────────────
  İKİ BOYUT, İKİ TON (2026-08-24). Tek bir rozet iki yere birden uymuyordu.

  Üst bar koyu (slate-900) ve rozet orada TEK renkli öğe — dolgun durması doğru.
  Profil kartı ise beyaz ve rozet orada 20px'lik bir ismin YANINDA duruyor; aynı dolgun
  rozet oraya konunca ismi eziyordu ("devasa ve orantısız"). Kişinin adı başlıktır,
  rozet ona iliştirilen bir niteliktir — görsel ağırlık sırası bunu yansıtmalı.

  Bu yüzden:
    boyut="md" + ton="koyu"  → üst bar: dolu brand-300 zemin, 24px madalyon
    boyut="sm" + ton="acik"  → profil: brand-50 zemin + ince halka, 18px madalyon

  Açık tonun zemini brand-50, yazısı brand-800, halkası brand-200: aynı skaladan üç
  basamak, yani rozet marka ailesinden çıkmıyor ama başlığı bastırmıyor.
  ─────────────────────────────────────────────────────────────────────────────

  RENK: koyu tondaki zemin brand-300, metin slate-900 (ölçülen kontrast 8.19:1 —
  Layout.jsx'teki kayıt). Sabit hex YOK; renkler paletten sınıf adıyla geliyor
  (bkz. e2e/kaynak-sabitleri.spec.js — src altında #RRGGBB yasak).

  İLERLEME TOOLTIP'TE, ROZETTE DEĞİL: "sonraki seviyeye 250 puan" bilgisi rozetin içine
  sığmıyor ve üst barda her sayfada duran bir öğenin sürekli değişen bir sayı taşıması
  gürültü olurdu. Rakam kimliği, tooltip ayrıntıyı veriyor.
*/

const TONLAR = {
  koyu: {
    kabuk: 'bg-brand-300 text-slate-900',
    madalyon: 'bg-slate-900 text-brand-300',
  },
  acik: {
    kabuk: 'bg-brand-50 text-brand-800 ring-1 ring-inset ring-brand-200',
    madalyon: 'bg-brand-600 text-white',
  },
}

const BOYUTLAR = {
  md: {
    kabuk: 'gap-1.5 py-1 pl-1 pr-2.5 text-xs sm:pr-3',
    madalyon: 'h-6 w-6 text-[13px]',
  },
  sm: {
    kabuk: 'gap-1 py-0.5 pl-0.5 pr-2 text-[11px]',
    madalyon: 'h-[18px] w-[18px] text-[11px]',
  },
}

export function SeviyeRozeti({ kaynak, boyut = 'md', ton = 'koyu', className = '' }) {
  const seviye = seviyeHesapla(kaynak)
  const t = TONLAR[ton] ?? TONLAR.koyu
  const b = BOYUTLAR[boyut] ?? BOYUTLAR.md

  return (
    <span
      className={`inline-flex items-center whitespace-nowrap rounded-full font-semibold
                  ${t.kabuk} ${b.kabuk} ${className}`}
      title={`${seviyeEtiketi(seviye)} (${EN_YUKSEK_SEVIYE} üzerinden) — ${seviyeIlerlemeMetni(kaynak)}`}
    >
      <span
        className={`grid shrink-0 place-items-center rounded-full font-bold leading-none
                    tabular-nums ${t.madalyon} ${b.madalyon}`}
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
