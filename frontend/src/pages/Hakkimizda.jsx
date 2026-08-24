import { Logo } from '../components/Logo'
import {
  ArtanIkonu,
  CuzdansizIkonu,
  EtiketsizIkonu,
  HedefIkonu,
  UfukIkonu,
} from '../components/Ikonlar'

/*
  HAKKIMIZDA — düz metin değil, kutucuklu bir sunum sayfası.

  Sayfa iki aşama geçirdi. Önce tek bir Card'ın içinde üç paragraftı; uygulamanın geri
  kalanıyla aynı yüzey dilini konuşuyordu (aynı kenarlık, aynı gölge, aynı 15px metin) ve
  bir liste ekranından ayırt edilemiyordu. Sonra kart kaldırıldı, tipografi büyütüldü —
  okunurluk düzeldi ama sayfa bu kez DÜZ METNE dönüştü: iki paragraf yan yana, aralarında
  hiçbir görsel işaret yok. "Kurumsal" ile "cansız" arasındaki fark tam olarak burada.

  ŞİMDİ: her fikir kendi kutusunda, kendi ikonuyla. Gerekçe süs değil tarama davranışı —
  bu sayfaya gelen kişi baştan sona okumuyor, GÖZ GEZDİRİYOR. İkon + başlık ikilisi,
  paragrafı okumadan önce "burada ne var" sorusunu yanıtlıyor; metin ancak ilgi çektiğinde
  okunuyor.

  ÜÇ BÖLÜM, ÜÇ İŞ:
    1. Açılış      — ürünün tek cümlelik tanımı, en büyük punto.
    2. Misyon & Vizyon — bugün ne yaptığımız / neye doğru gittiğimiz. İki kutu.
    3. Güvence şeridi  — "ücretli mi?" sorusunun cevabı. Üç küçük kutu.

  Güvence şeridi ayrı tutuldu çünkü kullanıcı bu sayfaya çoğu zaman bir SORUYLA geliyor,
  manifesto okumaya değil. Paragrafın içine gömülü bir güvence uzun metinde kaybolur;
  şerit hâlinde taranarak bulunur.

  HOVER EFEKTİ SADECE KUTULARDA ve ölçülü: kenarlık markaya döner, gölge bir kademe
  artar, kutu 1 piksel yükselir. Daha fazlası (renk dolgusu, büyüme, dönme) bir bilgi
  sayfasında dikkat dağıtır — burada tıklanacak bir şey yok, hareket yalnızca sayfanın
  canlı olduğunu söylüyor.

  Ölçü sınırı max-w-5xl: kutular yan yana sığsın ama satır uzunluğu ~70 karakteri
  geçmesin. Okunabilirlik sınırı kaldırılmadı, yalnızca ızgaraya uyarlandı.
*/

const DEGERLER = [
  {
    Ikon: HedefIkonu,
    baslik: 'Misyonumuz',
    ozet: 'Anlatarak öğrenmeyi herkesin erişebileceği bir şey yapmak.',
    metin:
      'Bir konuyu gerçekten öğrenmenin en kısa yolu onu birine anlatmaktır. dersmate, ' +
      'öğrencilerin bildiklerini anlatarak öğrendiği, eksiklerini bir akranından ' +
      'kapattığı bir alan açıyor — aradaki mesafeyi, ücreti ve aracıyı kaldırarak.',
  },
  {
    Ikon: UfukIkonu,
    baslik: 'Vizyonumuz',
    ozet: 'Sorusunu soracak birini bulamadığı için eksik kalan öğrenci olmasın.',
    metin:
      'Hiçbir öğrencinin bir konuyu, sırf sorusunu soracak birini bulamadığı için ' +
      'eksik bırakmadığı bir öğrenme ağı. Bugün YKS müfredatıyla başlıyoruz; hedef, ' +
      'her öğrencinin hem öğrenci hem öğretmen olabildiği bir topluluk.',
  },
]

const GUVENCELER = [
  {
    Ikon: EtiketsizIkonu,
    baslik: 'Ders almak ücretsiz',
    metin: 'Ders alan öğrenci hiçbir şey ödemez. Hiçbir koşulda.',
  },
  {
    Ikon: CuzdansizIkonu,
    baslik: 'Para transferi yok',
    metin: 'Kimse kimseye ödeme yapmaz. Platformda para dolaşmaz.',
  },
  {
    Ikon: ArtanIkonu,
    baslik: 'Puan anlatana yazılır',
    metin: 'Anlatan öğrenci puan kazanır; puan harcanmaz, seviyeni yükseltir.',
  },
]

export default function Hakkimizda() {
  return (
    <div className="mx-auto max-w-5xl pb-10">
      {/* ── AÇILIŞ ──────────────────────────────────────────────────────────── */}
      <header className="pb-10">
        <Logo className="h-9 w-auto" />
        <h1 className="mt-6 max-w-3xl text-3xl font-bold leading-tight tracking-tight text-slate-900 sm:text-4xl">
          Öğrencilerin birbirine ders anlattığı yer.
        </h1>
        <p className="mt-4 max-w-2xl text-lg leading-relaxed text-slate-600">
          İyi bildiğin konuyu anlatırsın, eksik olduğun konuda başka bir öğrenciden ders
          alırsın. Sınıfın en doğal hâli — çevrimiçi, ücretsiz ve akranlar arasında.
        </p>
      </header>

      {/* ── MİSYON & VİZYON ─────────────────────────────────────────────────── */}
      <div className="grid gap-4 sm:grid-cols-2 sm:gap-5">
        {DEGERLER.map(({ Ikon, baslik, ozet, metin }) => (
          <article
            key={baslik}
            className="group rounded-2xl border border-slate-200/80 bg-white p-6 shadow-sm
                       transition duration-200 hover:-translate-y-0.5 hover:border-brand-300
                       hover:shadow-lg sm:p-7"
          >
            {/*
              İkon kutusu: marka tonlu, yumuşak köşeli bir kare. Hover'da zemin bir
              kademe koyulaşıyor ve ikon beyaza dönüyor — kutunun tamamı tek bir öğe
              gibi tepki veriyor, ikon ayrı bir şey gibi durmuyor.
            */}
            <span
              className="grid h-12 w-12 place-items-center rounded-xl bg-brand-50 text-brand-600
                         ring-1 ring-inset ring-brand-100 transition duration-200
                         group-hover:bg-brand-600 group-hover:text-white group-hover:ring-brand-600"
            >
              <Ikon className="h-6 w-6" />
            </span>

            <h2 className="mt-5 text-xl font-semibold tracking-tight text-slate-900">{baslik}</h2>

            {/* ÖZET CÜMLE başlığın hemen altında ve marka renginde: tarayan göz için
                paragrafın tek satırlık karşılığı. Paragraf, ilgi çektiyse okunuyor. */}
            <p className="mt-2 text-[15px] font-medium leading-snug text-brand-700">{ozet}</p>

            <p className="mt-3 text-[15px] leading-relaxed text-slate-600">{metin}</p>
          </article>
        ))}
      </div>

      {/* ── GÜVENCE ŞERİDİ ──────────────────────────────────────────────────── */}
      <section className="mt-10">
        <h2 className="text-xs font-semibold uppercase tracking-wider text-slate-500">
          Nasıl işliyor
        </h2>

        <div className="mt-4 grid gap-3 sm:grid-cols-3">
          {GUVENCELER.map(({ Ikon, baslik, metin }) => (
            <div
              key={baslik}
              className="flex items-start gap-3 rounded-xl border border-slate-200/80 bg-white p-4
                         shadow-sm transition duration-200 hover:border-brand-300 hover:shadow-md"
            >
              <span className="mt-0.5 shrink-0 text-brand-600">
                <Ikon className="h-5 w-5" />
              </span>
              <div className="min-w-0">
                <p className="text-sm font-semibold text-slate-900">{baslik}</p>
                <p className="mt-1 text-sm leading-relaxed text-slate-600">{metin}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* Kapanış: sayfanın tezi, tek cümlede. Kutu yok — burada duracak bir şey değil,
          okunup geçilecek bir cümle. */}
      <p className="mt-10 border-t border-slate-200 pt-8 text-center text-sm italic text-slate-500">
        Bir konuyu anlatabiliyorsan, onu gerçekten öğrenmişsindir.
      </p>
    </div>
  )
}
