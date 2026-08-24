import { Logo } from '../components/Logo'

/*
  HAKKIMIZDA — kart içinde üç paragraf değil, kendi tipografisi olan bir sunum sayfası.

  Eski hâl tek bir Card'ın içine sıkışmış üç paragraftı ve uygulamanın geri kalanıyla
  aynı yüzey dilini konuşuyordu: aynı kenarlık, aynı gölge, aynı 15px metin. Sorun
  görsel değildi — bu sayfanın işi bir liste ya da form göstermek değil, ürünün NEDEN
  var olduğunu anlatmak, ve o iş kendi ritmini istiyor: geniş bir açılış cümlesi,
  nefes alan bir boşluk, iki başlık altında toplanmış iki fikir.

  ÜÇ BÖLÜM, ÜÇ İŞ:
    1. Açılış — ürünün tek cümlelik tanımı, en büyük punto.
    2. Misyon & Vizyon — bugün ne yaptığımız / neye doğru gittiğimiz.
    3. Güvence şeridi — "para transferi yok" gibi tek satırlık, aranan cevaplar.

  Güvence şeridi ayrı tutuldu çünkü kullanıcı bu sayfaya çoğu zaman bir SORUYLA geliyor
  ("ücretli mi?"), manifesto okumaya değil. Paragrafın içine gömülü bir güvence, uzun
  metinde kaybolur; şerit hâlinde ise taranarak bulunur.

  Ölçü sınırı max-w-3xl: satır uzunluğu ~70 karakteri geçince göz satır başını
  kaybediyor. Kart genişliği kaldırıldı ama okunabilirlik sınırı kaldırılmadı.
*/

const DEGERLER = [
  {
    baslik: 'Misyonumuz',
    metin:
      'Bir konuyu gerçekten öğrenmenin en kısa yolu onu birine anlatmaktır. dersmate, ' +
      'öğrencilerin bildiklerini anlatarak öğrendiği, eksiklerini bir akranından ' +
      'kapattığı bir alan açıyor — aradaki mesafeyi, ücreti ve aracıyı kaldırarak.',
  },
  {
    baslik: 'Vizyonumuz',
    metin:
      'Hiçbir öğrencinin bir konuyu, sırf sorusunu soracak birini bulamadığı için ' +
      'eksik bırakmadığı bir öğrenme ağı. Bugün YKS müfredatıyla başlıyoruz; hedef, ' +
      'her öğrencinin hem öğrenci hem öğretmen olabildiği bir topluluk.',
  },
]

const GUVENCELER = [
  ['Ders almak ücretsiz', 'Ders alan öğrenci hiçbir şey ödemez. Hiçbir koşulda.'],
  ['Para transferi yok', 'Kimse kimseye ödeme yapmaz. Platformda para dolaşmaz.'],
  ['Puan anlatana yazılır', 'Anlatan öğrenci puan kazanır; puan harcanmaz, birikir.'],
]

export default function Hakkimizda() {
  return (
    <div className="mx-auto max-w-3xl">
      {/* AÇILIŞ */}
      <header className="border-b border-slate-200 pb-8">
        <Logo className="h-10 w-auto" vurgulu />
        <h1 className="mt-6 text-3xl font-bold leading-tight tracking-tight text-slate-900 sm:text-4xl">
          Öğrencilerin birbirine ders anlattığı yer.
        </h1>
        <p className="mt-4 text-lg leading-relaxed text-slate-600">
          İyi bildiğin konuyu anlatırsın, eksik olduğun konuda başka bir öğrenciden ders
          alırsın. Sınıfın en doğal hâli — çevrimiçi, ücretsiz ve akranlar arasında.
        </p>
      </header>

      {/* MİSYON & VİZYON */}
      <div className="grid gap-8 py-10 sm:grid-cols-2 sm:gap-10">
        {DEGERLER.map(({ baslik, metin }) => (
          <section key={baslik}>
            {/* Başlığın üstündeki kısa marka çizgisi: iki bölümü ayıran şey renk değil
                ritim olsun — renk körlüğünde de aynı yapı okunur. */}
            <span className="block h-1 w-10 rounded-full bg-brand-500" aria-hidden="true" />
            <h2 className="mt-4 text-xl font-semibold tracking-tight text-slate-900">{baslik}</h2>
            <p className="mt-3 text-[15px] leading-relaxed text-slate-600">{metin}</p>
          </section>
        ))}
      </div>

      {/* GÜVENCE ŞERİDİ */}
      <section className="rounded-2xl border border-slate-200/80 bg-white p-6 shadow-md sm:p-8">
        <h2 className="text-sm font-semibold uppercase tracking-wider text-slate-500">
          Nasıl işliyor
        </h2>
        <dl className="mt-5 grid gap-6 sm:grid-cols-3">
          {GUVENCELER.map(([baslik, aciklama]) => (
            <div key={baslik}>
              <dt className="text-sm font-semibold text-slate-900">{baslik}</dt>
              <dd className="mt-1 text-sm leading-relaxed text-slate-600">{aciklama}</dd>
            </div>
          ))}
        </dl>
      </section>

      <p className="py-8 text-center text-sm text-slate-500">
        Bir konuyu anlatabiliyorsan, onu gerçekten öğrenmişsindir.
      </p>
    </div>
  )
}
