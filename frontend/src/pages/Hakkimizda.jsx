import { Link } from 'react-router-dom'
import { Logo } from '../components/Logo'
import { CamKart } from '../components/SayfaZemini'
import {
  ArtanIkonu,
  CuzdansizIkonu,
  GozIkonu,
  KanitIkonu,
  KisilerIkonu,
  OnayIkonu,
  RoketIkonu,
  TakasIkonu,
} from '../components/Ikonlar'

/*
  HAKKIMIZDA — kartlı sunum sayfası.

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
  diyor.

  HOVER ÖLÇÜLÜ: kenarlık markaya döner, gölge bir kademe artar, kutu 1px yükselir.
  Daha fazlası (renk dolgusu, büyüme, dönme) bir bilgi sayfasında dikkat dağıtır —
  burada tıklanacak bir şey yok, hareket yalnızca sayfanın canlı olduğunu söylüyor.
*/

const DEGERLER = [
  {
    Ikon: RoketIkonu,
    baslik: 'Misyonumuz',
    metin:
      'Bir konuyu gerçekten öğrenmenin en kısa yolu onu birine anlatmaktır. dersmate, ' +
      'öğrencilerin bildiklerini anlatarak öğrendiği, eksiklerini bir akranından ' +
      'kapattığı bir alan açıyor — aradaki mesafeyi, ücreti ve aracıyı kaldırıyoruz.',
  },
  {
    Ikon: GozIkonu,
    baslik: 'Vizyonumuz',
    metin:
      'Hiçbir öğrencinin bir konuyu, sırf sorusunu soracak birini bulamadığı için ' +
      'eksik bırakmadığı bir öğrenme ağı. Bugün YKS müfredatıyla başlıyoruz; hedef, ' +
      'her öğrencinin hem öğrenci hem öğretmen olabildiği bir topluluk.',
  },
  {
    Ikon: KisilerIkonu,
    baslik: 'Topluluğumuz',
    metin:
      'Öğretmen yok, akran var. Anlatan da öğrenen de aynı sıralarda; bu yüzden sorular ' +
      'çekinmeden soruluyor, cevaplar aynı dilden geliyor. Her ders iki kişiyi birden ' +
      'ilerletiyor.',
  },
]

/*
  ─────────────────────────────────────────────────────────────────────────────
  "NASIL İŞLİYOR" MADDELERİ — biri kaldırıldı, biri eklendi (2026-08-24).

  KALDIRILAN: "Ders almak ücretsiz". Proje sahibinin tespiti: "Para transferi yok"
  maddesiyle aynı yeri kaplıyordu. İkisi teknik olarak farklı şeyler söylüyor (ücretsiz
  olmak ile kullanıcılar arasında para dolaşmaması aynı iddia değil) ama okuyan için
  ayrımı yok — üç maddelik bir şeritte iki madde aynı soruyu yanıtlıyordu.

  EKLENEN: "Karşılıklı takas".

  ⚠️ ÖNCE BAŞKA BİR METİN İSTENMİŞTİ ve yazılmadı — kaydı burada duruyor çünkü aynı
  hataya bir daha düşülmesin. İstenen metin "kredi sistemi: ders aldıkça kredi
  harcarsın" diyordu. SİSTEM BUNU YAPMIYOR: CreditLedgerService yalnızca anlatana puan
  BASIYOR, öğrenciden hiçbir şey düşmüyor (tek bacaklı işlem — CLAUDE.md, "escrow/bloke
  kredi mekanizması kaldırıldı, geri getirme"). O cümle ekranda dursaydı kullanıcı var
  olmayan bir mekanizmaya göre karar verirdi: "kredim biterse ders alamam" diye ders
  almaktan çekinmek gibi. Bir güvence şeridinin yapabileceği en kötü şey, güvence diye
  yanlış bilgi vermek. Proje sahibi yanlışlıkla yazdığını doğruladı, madde değişti.

  YERİNE GELEN ŞEY, İSTENEN FİKRİN GERÇEK KARŞILIĞI. "Adil takas" bu üründe var — ama
  kredi üzerinden değil, KONU-KONUYA: eşleştirme motoru, senin aradığın konuyu anlatan
  kişiler arasından senin anlatabildiğin konuyu arayanları bulup listenin başına alıyor
  (GetMatchSuggestions.cs → IsCrossMatch, OrderByDescending). Keşfet ekranında da
  "Karşılıklı takas" rozetiyle görünüyor, yani bu sayfa ürünle çelişmiyor, onu anlatıyor.
  ─────────────────────────────────────────────────────────────────────────────
*/
const GUVENCELER = [
  {
    Ikon: TakasIkonu,
    baslik: 'Karşılıklı takas',
    metin:
      'Senin öğrenmek istediğin konuyu anlatan ve senin anlatabildiğin konuyu ' +
      'öğrenmek isteyen kişiler Keşfet’te listenin başında çıkar.',
  },
  {
    Ikon: CuzdansizIkonu,
    baslik: 'Para transferi yok',
    metin: 'Kimse kimseye ödeme yapmaz. Platformda para dolaşmaz.',
  },
  {
    Ikon: KanitIkonu,
    baslik: 'Doğrulanmış dersler',
    metin: 'Her ders kanıtla kapanır; değerlendirmeler yalnızca gerçek derslerden gelir.',
  },
]

const VAATLER = ['Akran öğrenmesi', 'Puanla ilerleme', 'Doğrulanmış dersler']

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
        <p className="mt-4 max-w-2xl text-lg leading-relaxed text-slate-600">
          dersmate, öğrencilerin birbirine ders anlattığı bir akran öğrenme
          platformudur. İyi bildiğin konuyu anlatır, eksik olduğun konuda başka bir
          öğrenciden ders alırsın.
        </p>
      </header>

      {/* ── MİSYON / VİZYON / TOPLULUK ────────────────────────────────────── */}
      <div className="mt-10 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {DEGERLER.map(({ Ikon, baslik, metin }) => (
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
            key={baslik}
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
              <Ikon className="h-5 w-5" />
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
              <p className="flex items-center gap-2 text-sm font-medium text-slate-600">
                <ArtanIkonu className="h-4 w-4 text-brand-600" />
                Nasıl işliyor
              </p>
              <h2 className="mt-2 text-2xl font-bold tracking-tight text-slate-900 sm:text-3xl">
                Anlat, öğren, ilerle
              </h2>

              {/* Vaat şeridi: tek satırlık, tik işaretli. Detay aşağıdaki üç kutuda —
                  burası "tarayarak geçen" göz için. */}
              <ul className="mt-5 flex flex-wrap gap-x-6 gap-y-2">
                {VAATLER.map((v) => (
                  <li key={v} className="flex items-center gap-2 text-sm text-slate-600">
                    <OnayIkonu className="h-4 w-4 text-emerald-500" />
                    {v}
                  </li>
                ))}
              </ul>
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

          <div className="mt-8 grid gap-4 border-t border-slate-200/70 pt-6 sm:grid-cols-3">
            {GUVENCELER.map(({ Ikon, baslik, metin }) => (
              <div key={baslik} className="flex items-start gap-3">
                {/* Güvence ikonları üstteki değer kartlarıyla aynı çip diline taşındı:
                    çıplak ikon beyaz zeminde kayboluyordu, brand-50 kutu üç maddeyi
                    aynı ailenin üyesi gibi okutuyor. Hover yok — bunlar tıklanmaz,
                    değer kartlarındaki dolgu efekti buraya taşınmadı. */}
                <span
                  className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-brand-50
                             text-brand-600 ring-1 ring-inset ring-brand-100"
                >
                  <Ikon className="h-5 w-5" />
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
          değil, okunup geçilecek bir cümle. */}
      <p className="mt-10 text-center text-sm italic text-slate-600">
        Bir konuyu anlatabiliyorsan, onu gerçekten öğrenmişsindir.
      </p>
    </div>
  )
}
