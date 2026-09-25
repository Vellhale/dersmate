import { Link } from 'react-router-dom'
import { Logo } from '../components/Logo'
import { CamKart } from '../components/SayfaZemini'
import {
  ADIMLAR,
  BILDIRIM_NOTU,
  DEGERLER,
  GUVENCELER,
  KAPANIS,
  NASIL,
  OZET,
} from '../lib/hakkimizdaMetni'
import {
  AramaIkonu,
  ArtanIkonu,
  CuzdansizIkonu,
  GozIkonu,
  KanitIkonu,
  KepIkonu,
  KisilerIkonu,
  KitapIkonu,
  MesajIkonu,
  RoketIkonu,
  ToplulukIkonu,
} from '../components/Ikonlar'

/*
  HAKKIMIZDA — kartlı sunum sayfası.

  METİN BU DOSYADA DEĞİL (2026-09-26): lib/hakkimizdaMetni.js KAYNAK dosya ve mobil
  depodaki src/lib/hakkimizdaMetni.js onun BAYT BAYT kopyası. Bu sayfa yalnızca düzeni
  ve anahtar → ikon çevirisini taşıyor. Metni değiştiren önce burayı değiştirir, sonra
  mobile kopyalar; sapma `diff` ile ölçülür ve BOŞ çıkmalı.

  YAPI: açılış → üç değer kartı (Misyon / Vizyon / Topluluk) → tek "Nasıl işliyor" kartı
  (altı adım, bildirim dipnotu, Keşfet bağlantısı, üç güvence) → kapanış cümlesi. Sayfa
  bir imzayla BİTMİYOR: altındaki künyeyi Layout'un alt bilgisi (KunyeSatiri) çiziyor.

  Sayfa beş aşama geçirdi. Önce tek bir Card içinde üç paragraftı ve bir liste
  ekranından ayırt edilemiyordu. Sonra kart kaldırıldı, tipografi büyüdü — okunurluk
  düzeldi ama sayfa DÜZ METNE dönüştü. Üçüncü denemede kartların altına referans
  tasarımdan uyarlanmış TAM KANAMALI mavi bir gradyan serildi; sahibin isteğiyle
  kaldırıldı ("yoğun duruyor"). Dördüncüsü onun hafif hâliydi: içerik genişliğinde,
  yalnızca üst 20 rem'i boyayan bir brand-50 şerit.

  ŞİMDİKİ (beşinci) HÂL: SayfaZemini, `zengin` yoğunlukta. Aradaki fark ölçek değil
  YÖNTEM — o şerit tek yönlü bir linear geçişti ve bittiği yerde sayfayı ikiye bölen
  görünür bir sınır bırakıyordu; SayfaZemini içeriden aydınlanan mesh havuzlar +
  ızgara dokusu kullanıyor ve altta kendi kendine sönümleniyor. Kartlar da opak
  beyazdan cama geçtiği için zemin artık kartların ARASINDAN değil ALTINDAN da
  okunuyor: "yoğun" olan eski gradyanın doygunluğuydu, zeminin var olması değil.

  GERİ GETİRME: tam kanamalı gradyan (-mx-4) ve onun düzeni. Negatif kenar boşluğu
  yok, taşma tuzağı da konu dışı.

  ÜÇ KART, TEK IZGARA. Referanstaki üçlü düzen korundu: Misyon, Vizyon, Topluluk.
  Üçüncüsü doldurma değil — bu ürünün taşıyıcı fikri akranlık ve o fikrin misyon/vizyon
  ikilisinde yeri yok; ikisi de "biz ne yapıyoruz" derken topluluk "bunu kim yapıyor"
  diyor. Topluluk kartının ikonu menüdeki "Topluluk"un ikonu (ToplulukIkonu): kart artık
  forumu da anlatıyor ve kullanıcı simgeyi menüden tanıyor.

  HOVER ÖLÇÜLÜ: kenarlık markaya döner, gölge bir kademe artar, kutu 1px yükselir.
  Daha fazlası (renk dolgusu, büyüme, dönme) bir bilgi sayfasında dikkat dağıtır —
  burada tıklanacak bir şey yok, hareket yalnızca sayfanın canlı olduğunu söylüyor.

  ─── KALDIRILANLAR (2026-09-26) ──────────────────────────────────────────────────
  • VAAT ŞERİDİ ("Akran öğrenmesi · Puanla ilerleme · Doğrulanmış dersler", emerald
    tik): altı adım aynı yeri daha doğru dolduruyor ve "Doğrulanmış dersler" güvence
    ızgarasında zaten geçiyordu, iki kez söyleniyordu. OnayIkonu tek çağıranıyla
    birlikte Ikonlar.jsx'ten silindi (ölü ikon bırakılmaz). Emerald'ın buradan gitmesi
    web'in renk sistemine dair bir karar değil, içerik kaldırmanın yan sonucu.
  • "Karşılıklı takas" GÜVENCESİ: fikir Keşfet adımına taşındı ("Karşılıklı ders
    verebileceğin kişiler listenin başında çıkar"). TakasIkonu Keşfet'te kullanılmaya
    devam ediyor.
  • İMZA ("dersmate’i Corventech geliştiriyor ve işletiyor."): Layout alt bilgisindeki
    KunyeSatiri hemen altında "Bir Corventech ürünüdür" diyor ve aynı bağlantıyı
    veriyordu; Corventech sayfanın dibinde arka arkaya İKİ KEZ yazıyordu. "Bunu kim
    yapıyor" sorusunu artık alt bilgi yanıtlıyor. Yasal künye bloğu (KunyeBlogu) yine
    burada DEĞİL: bu sayfa tanıtım, yasal metin değil; tescil bilgileri yasal metinlerin
    altında (MetinSayfasi → KunyeBlogu).
*/

const DEGER_IKONU = { misyon: RoketIkonu, vizyon: GozIkonu, topluluk: ToplulukIkonu }

/* Layout.jsx → NAV ile aynı ikonlar, aynı sırayla: kullanıcı simgeyi menüden tanır ve
   metin "sol ray" ya da "çekmece" demeden menüyü anlatmış olur. Menüde ikon değişirse
   burada da değişmeli. */
const ADIM_IKONU = {
  kesfet: AramaIkonu,
  portfoy: KitapIkonu,
  arkadaslar: KisilerIkonu,
  sohbet: MesajIkonu,
  derslerim: KepIkonu,
  topluluk: ToplulukIkonu,
}

const GUVENCE_IKONU = { puan: ArtanIkonu, para: CuzdansizIkonu, kanit: KanitIkonu }

/* Metin modülüne yeni bir anahtar eklenip ikonu buraya eklenmediyse sayfa düşmesin:
   ikon kutusu boş kalır, metin yine okunur. */
function AnahtarIkonu({ tablo, anahtar, className }) {
  const Ikon = tablo[anahtar]
  return Ikon ? <Ikon className={className} /> : null
}

/* Güvence ve adım ikonlarının ortak kutusu. Değer kartlarındaki çip diliyle aynı aile
   (brand-50 + ince halka): çıplak ikon cam zeminde kayboluyordu, kutu maddeleri aynı
   ailenin üyesi gibi okutuyor. Hover yok — bunlar tıklanmaz. */
const IKON_KUTUSU =
  'grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600 ' +
  'ring-1 ring-inset ring-brand-100'

export default function Hakkimizda() {
  return (
    /* Zemini bu sayfa çizmiyor — Layout, rotaya bakarak `zengin` yoğunlukta veriyor
       (bkz. ZENGIN_ZEMIN_ROTALARI). Burada da bir SayfaZemini olsaydı iki ızgara üst
       üste biner ve çizgi opaklığı iki katına çıkardı.

       `isolate` de yok: bu projede modallar portal kullanmıyor ve isolate, z-50 perdeyi
       kendi yığın bağlamına hapsedip z-40 üst barın altında bırakıyor. Bu sayfada şu an
       modal yok ama tuzağı hazır bırakmamak için kural burada da geçerli. */
    <div className="mx-auto max-w-5xl pb-10">
      {/* ── AÇILIŞ ────────────────────────────────────────────────────────── */}
      <header>
        <Logo boyut="lg" />
        <h1 className="mt-6 text-4xl font-bold tracking-tight text-slate-900 sm:text-5xl">
          Hakkımızda
        </h1>
        <p className="mt-4 max-w-2xl text-lg leading-relaxed text-slate-600">{OZET}</p>
      </header>

      {/* ── MİSYON / VİZYON / TOPLULUK ────────────────────────────────────── */}
      <div className="mt-10 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {DEGERLER.map(({ anahtar, baslik, metin }) => (
          /*
            CAM KART. Opak beyaz kutular zemini kendi alanlarında tamamen kesiyordu ve
            üç kart yan yana gelince sayfanın orta bandı yine bembeyaz kalıyordu — zemin
            "cömertçe" değil, yalnızca kenarlarda görünüyordu. CamKart'ın /80 opaklığı
            metnin arkasını pratik olarak beyaz bırakırken zeminin rengini kartın
            içinden de geçiriyor.

            <article> → <div> (CamKart): kartlar bağımsız birer makale değil, tek bir
            "değerlerimiz" ızgarasının parçaları; başlık hiyerarşisini h2'ler zaten
            taşıyor. Hover ölçüsü aynı kaldı — kenarlık markaya döner, gölge bir kademe
            artar, kutu 1px yükselir.
          */
          <CamKart
            key={anahtar}
            className="group p-6 transition duration-200 hover:-translate-y-0.5
                       hover:border-brand-200 hover:shadow-lg sm:p-7"
          >
            {/* İkon kutusu hover'da doluyor: kutunun tamamı tek bir öğe gibi tepki
                veriyor, ikon ayrı bir şey gibi durmuyor. */}
            <span
              className="grid h-11 w-11 place-items-center rounded-xl bg-brand-50 text-brand-600
                         ring-1 ring-inset ring-brand-100 transition duration-200
                         group-hover:bg-brand-600 group-hover:text-white group-hover:ring-brand-600"
            >
              <AnahtarIkonu tablo={DEGER_IKONU} anahtar={anahtar} className="h-5 w-5" />
            </span>

            <h2 className="mt-6 text-xl font-semibold tracking-tight text-slate-900">{baslik}</h2>
            <p className="mt-3 text-[15px] leading-relaxed text-slate-600">{metin}</p>
          </CamKart>
        ))}
      </div>

      {/* ── GENİŞ KART: NASIL İŞLİYOR ─────────────────────────────────────── */}
      {/*
        Bu kart da cama geçti. Üstteki üçlü ızgara cam, bu opak beyaz kalsaydı sayfanın
        EN BÜYÜK yüzeyi zemini tek başına kesip aşağı yarıyı yine düz beyaz gösterirdi —
        ve iki farklı kart dili aynı sayfada yan yana dururdu.

        <section> KORUNDU, CamKart onun İÇİNDE: bölüm kendi başlığı olan gerçek bir
        bölüm ve bunu işaretlemek CamKart'ın işi değil (CamKart bir <div> basar). Yüzey
        ile anlam ayrı katmanlarda duruyor.
      */}
      <section className="mt-6">
        <CamKart className="p-6 sm:p-8">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0">
              {/* Etiket ikonsuz: ArtanIkonu puan güvencesine geçti, iki yerde olsaydı
                  anlamı bulanırdı. */}
              <p className="text-sm font-medium text-slate-600">{NASIL.etiket}</p>
              <h2 className="mt-2 text-2xl font-bold tracking-tight text-slate-900 sm:text-3xl">
                {NASIL.baslik}
              </h2>
              <p className="mt-2 text-sm text-slate-600">{NASIL.giris}</p>
            </div>

            {/* Bağlantı OPAK beyaz kalıyor: cam bir kartın içinde ikinci bir yarı saydam
                yüzey, tıklanabilir olanı tıklanamayandan ayırmayı bırakır. Denetimler
                zemine değil, KENDİLERİNE benziyor. */}
            <Link
              to="/kesfet"
              className="inline-flex min-h-11 shrink-0 items-center gap-2 rounded-xl border
                         border-slate-200 bg-white px-5 text-sm font-semibold text-slate-800
                         shadow-sm transition hover:border-brand-300 hover:bg-brand-50
                         hover:text-brand-800"
            >
              Keşfet’e göz at
              <span aria-hidden="true">→</span>
            </Link>
          </div>

          {/*
            ALTI ADIM, menüdeki sırayla. <ol>: sıra anlam taşıyor (bir dersin baştan sona
            akışı) ve ekran okuyucu "6 öğeli liste" diye duyuruyor. Başlık hiyerarşisi
            h1 → h2 (kart başlıkları) → h3 (adımlar); numara görünür metinde de var,
            çünkü ızgara iki-üç sütuna dağılınca liste sırası gözle takip edilemiyor.
          */}
          <ol className="mt-6 grid gap-x-6 gap-y-5 sm:grid-cols-2 lg:grid-cols-3">
            {ADIMLAR.map(({ anahtar, baslik, metin }, i) => (
              <li key={anahtar} className="flex items-start gap-3">
                <span className={IKON_KUTUSU}>
                  <AnahtarIkonu tablo={ADIM_IKONU} anahtar={anahtar} className="h-5 w-5" />
                </span>
                <div className="min-w-0">
                  <h3 className="text-sm font-semibold text-slate-900">
                    <span className="text-slate-400">{i + 1}.</span> {baslik}
                  </h3>
                  <p className="mt-1 text-sm leading-relaxed text-slate-600">{metin}</p>
                </div>
              </li>
            ))}
          </ol>

          {/* Push yalnızca mobil uygulamada; cümle bunu kendisi söylüyor, yol vermiyor
              (bildirim ayarlarının yeri iki platformda farklı). slate-600, 500 DEĞİL:
              cam kartta küçük metin slate-500 ile AA'nın altına düşüyor (SayfaZemini →
              CamKart notu); mobilde beyaz kart üstünde olduğu için orada 500. */}
          <p className="mt-5 text-xs leading-relaxed text-slate-600">{BILDIRIM_NOTU}</p>

          <div className="mt-8 grid gap-4 border-t border-slate-200/70 pt-6 sm:grid-cols-3">
            {GUVENCELER.map(({ anahtar, baslik, metin }) => (
              <div key={anahtar} className="flex items-start gap-3">
                <span className={IKON_KUTUSU}>
                  <AnahtarIkonu tablo={GUVENCE_IKONU} anahtar={anahtar} className="h-5 w-5" />
                </span>
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-slate-900">{baslik}</p>
                  <p className="mt-1 text-sm leading-relaxed text-slate-600">{metin}</p>
                </div>
              </div>
            ))}
          </div>
        </CamKart>
      </section>

      {/* Kapanış: sayfanın tezi, tek cümlede. Kutu yok — burada duracak bir şey
          değil, okunup geçilecek bir cümle. Altındaki künye Layout'un alt bilgisi. */}
      <p className="mt-10 text-center text-sm italic text-slate-600">{KAPANIS}</p>
    </div>
  )
}
