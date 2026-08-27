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

/*
  ─── YATAY OPTİK DÜZELTME: "1" RAKAMI ────────────────────────────────────────

  Dikey merkezleme çözüldükten sonra rozet 4x yakınlaştırılıp gözle bakıldı ve "1"
  dairede hâlâ SOLA kaçık duruyordu. İlk yatay ölçümüm bunu kaçırmıştı çünkü glif
  KUTUSUNU ölçüyordu, MÜREKKEBİ değil — kutu kusursuz ortadaydı (sapma 0.01px).

  Canvas TextMetrics ile mürekkep ölçüldü (Segoe UI bold, 40px'te ölçüp rozet
  puntosuna oranlandı). Her rakam aynı ilerleme genişliğini alıyor (23.01) ama
  mürekkek genişlikleri farklı:

      rakam   mürekkep genişliği   merkez sapması (11px rozette)
        1           14                    −0.41px   ← gözle görülen
        2           20                    −0.14px
        7, 8        21                     0.00px
        10          42                    +0.27px

  `tabular-nums` KALDIRILMADI ve kaldırılması işe de yaramaz: Segoe UI'ın
  varsayılan rakamları zaten sabit genişlikte, yani sorun font-variant değil
  glifin kendi çizimi. Çare, yalnızca ölçülebilir şekilde sapan rakamlara em
  cinsinden karşı itme uygulamak.

  Sapması 0.15px'in altında kalan rakamlar (2 ve diğerleri) DÜZELTİLMİYOR: o
  ölçekte düzeltme, gözün fark ettiği bir şeyi değil yuvarlama gürültüsünü kovalar.

  Em cinsinden çünkü rozetin iki boyutu var (13px ve 11px); sabit piksel küçük
  rozette fazla iterdi.
*/
const YATAY_DUZELTME = {
  1: 'translate-x-[0.038em]', // +0.41px @11px — mürekkep solda, sağa itiliyor
  10: '-translate-x-[0.025em]', // −0.27px @11px — çift hane sağa kayıyor
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

  /*
    İLERLEME METNİ YALNIZCA VERİSİ VARSA (2026-08-27).

    seviyeIlerlemeMetni eksik veriyi 0'a düşürüyor ve `nextLevelAt` yoksa "en üst
    seviye" diyor. Cüzdanda ikisi de dolu olduğu için bu görünmüyordu; forum akışı
    yazarın YALNIZCA seviyesini gönderiyor (ForumAuthorDto — puan başkasının verisi,
    sızdırılmıyor) ve o kaynakla tooltip "8. Seviye — 0 puan · en üst seviye" yazardı:
    iki iddia birden yanlış.

    Ölçüt totalEarnedCredits: ilerleme cümlesinin her iki yarısı da ona dayanıyor.
    Yoksa rozet seviyeyi söylüyor, ilerleme hakkında hiçbir şey iddia etmiyor.
  */
  const ilerlemeVar = Number.isInteger(kaynak?.totalEarnedCredits)
  const baslik = ilerlemeVar
    ? `${seviyeEtiketi(seviye)} (${EN_YUKSEK_SEVIYE} üzerinden) — ${seviyeIlerlemeMetni(kaynak)}`
    : `${seviyeEtiketi(seviye)} (${EN_YUKSEK_SEVIYE} üzerinden)`

  return (
    <span
      className={`inline-flex items-center whitespace-nowrap rounded-full font-semibold
                  ${t.kabuk} ${b.kabuk} ${className}`}
      title={baslik}
    >
      {/*
        RAKAM MADALYONUN OPTİK MERKEZİNDE (2026-08-25) — ölçümle.

        Kutu merkezleme zaten kusursuzdu (glif kutusunun sapması ölçümde 0.00px).
        Sapma kutuda değil MÜREKKEPTE. Segoe UI 13px'te glif kutusu ascent 14 +
        descent 3 = 17px; taban çizgisi kutunun %82'sinde. Rakamların mürekkebi
        taban çizgisinden 10px yukarı çıkıyor, aşağı hiç inmiyor (alt çıkıntı yok).
        Hesap: mürekkep merkezi = kutu üstü + 14 − 10/2 = kutu üstü + 9; kutu merkezi
        ise 8.5 — yani rakam merkezden 0.5px AŞAĞIDA duruyordu.

        (Bu yorumun ilk sürümü sapmayı ters yönde, "0.79px yukarıda" diye ölçmüştü —
        formül taban çizgisini yanlış ölçekliyordu. Düzeltme bir tur yanlış yöne
        itildi, temiz formülle yeniden ölçülüp çevrildi. Ders: optik düzeltmeyi
        uygulamadan ÖNCE ve SONRA aynı formülle ölç.)

        Çare iki parça:
          • flex items-center justify-center — kutu merkezleme (grid ile eşdeğer,
            sahibin isteğiyle flexbox).
          • -translate-y-[0.04em] — optik düzeltme, em cinsinden: 13px'te 0.52px,
            sm boyutun 11px'inde 0.44px. Sabit piksel olsaydı küçük madalyonda
            fazla iterdi; em, rakamla birlikte ölçekleniyor.

        Rakam kendi span'inde çünkü translate madalyona uygulanamaz — yuvarlak zemin
        yerinde kalmalı, yalnızca mürekkep kaymalı.
      */}
      <span
        className={`flex shrink-0 items-center justify-center rounded-full font-bold
                    leading-none tabular-nums ${t.madalyon} ${b.madalyon}`}
        aria-hidden="true"
      >
        {/* İki eksende de optik düzeltme: dikey her rakam için aynı, yatay yalnızca
            ölçülen sapması göze görünen rakamlar için (bkz. YATAY_DUZELTME). */}
        <span className={`-translate-y-[0.04em] ${YATAY_DUZELTME[seviye] ?? ''}`}>{seviye}</span>
      </span>
      {/* Görünen kelime dar ekranda düşüyor ama erişilebilir ad tam kalıyor: ekran
          okuyucu her boyutta "3. Seviye" duyar, sadece "3" değil. */}
      <span className="hidden sm:inline">Seviye</span>
      <span className="sr-only">{seviyeEtiketi(seviye)}</span>
    </span>
  )
}
