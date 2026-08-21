import { Logo } from '../components/Logo'
import { KisilerIkonu, KepIkonu, KalkanIkonu, BuyutecIkonu } from '../components/Ikonlar'

/*
  Yan menünün alt bağlantısının hedefi.

  YAZIM İLKESİ: her cümle ya bir KURAL ya da bir GEREKÇE anlatır. "Vizyonumuz eğitimde
  fırsat eşitliği sağlamaktır" gibi hangi eğitim şirketine yapıştırılsa duracak cümleler
  bilerek yok — okuyan kişi buradan dersmate'in NASIL çalıştığını öğrenmeli, ne kadar
  iyi niyetli olduğunu değil.

  Sayısal iddialar da yok (kullanıcı sayısı, ders saati): doğrulanamayan rakam yazmak
  yerine ürünün değişmez kuralları yazıldı — onlar zaten kodda uygulanıyor.
*/

const ILKELER = [
  {
    Ikon: KepIkonu,
    baslik: 'Ders almak ücretsizdir',
    metin:
      'Öğrenci hiçbir ders için ödeme yapmaz, puan harcamaz. Ders anlatan kişi puan kazanır; kimseden bir şey eksilmez. Platformda para transferi yoktur.',
  },
  {
    Ikon: KisilerIkonu,
    baslik: 'Öğreten de öğrenir',
    metin:
      'Bir konuyu anlatabiliyorsan onu gerçekten öğrenmişsindir. Bu yüzden herkes hem anlatan hem dinleyen olabilir — roller sabit değil, konuya göre değişir.',
  },
  {
    Ikon: BuyutecIkonu,
    baslik: 'Eşleşme konudan başlar',
    metin:
      'Kim olduğun değil, hangi konuyu anlatabildiğin ve hangisini öğrenmek istediğin eşleştirir. Portföyüne konu ekle, karşılıklı takas mümkün olanlar üste çıksın.',
  },
  {
    Ikon: KalkanIkonu,
    baslik: 'Emek kayıt altında',
    metin:
      'Her ders onaylanır, her puan defterde iz bırakır ve anlaşmazlık çıkarsa hakem paneli iki tarafı da dinler. Seviyeler işte bu doğrulanmış emeğin toplamıdır.',
  },
]

function Bolum({ etiket, baslik, children }) {
  return (
    <section className="space-y-3">
      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-brand-700">{etiket}</p>
      <h2 className="text-xl font-bold tracking-tight text-slate-900 sm:text-2xl">{baslik}</h2>
      <div className="space-y-3 text-[15px] leading-relaxed text-slate-600">{children}</div>
    </section>
  )
}

export default function Hakkimizda() {
  return (
    <div className="mx-auto max-w-3xl space-y-12 pb-4">
      {/* GİRİŞ: koyu blok, kabuğun üst barıyla aynı zemin (slate-900) — sayfa markanın
          içinden çıkıyormuş gibi dursun. Logo burada metin olarak tekrarlanmıyor. */}
      <header className="rounded-2xl bg-slate-900 px-6 py-10 sm:px-10 sm:py-12">
        <Logo onDark className="h-9 w-auto sm:h-10" />
        <h1 className="mt-6 max-w-xl text-2xl font-bold leading-tight tracking-tight text-white sm:text-3xl">
          Öğrencilerin birbirine ders anlattığı yer.
        </h1>
        <p className="mt-4 max-w-xl text-[15px] leading-relaxed text-slate-300">
          dersmate bir kurs değil, bir akran ağı. İyi bildiğin konuyu anlatırsın, eksik
          olduğun konuda başka bir öğrenciden ders alırsın. Anlattıkça puan biriktirir,
          seviye atlarsın.
        </p>
      </header>

      <Bolum etiket="Misyonumuz" baslik="Öğrenmenin bedelini kaldırmak">
        <p>
          Bir konuyu anlamak için ödeme yapmak zorunda kalmamalısın. Sınava hazırlanan bir
          öğrencinin ihtiyacı olan şey çoğu zaman pahalı bir kurs değil; o konuyu geçen yıl
          çözmüş, bir sınıf üstündeki birinin yarım saatidir.
        </p>
        <p>
          dersmate bu yarım saati bulunabilir kılar. Öğrenci için ders <strong>ücretsizdir</strong>;
          anlatan kişi ise emeğinin karşılığını puan ve seviye olarak alır. Tek yönlü bir
          ekonomi kurduk: puan <em>basılır</em>, kimsenin hesabından düşülmez.
        </p>
      </Bolum>

      <Bolum etiket="Vizyonumuz" baslik="Anlatabilmenin görünür bir değeri olsun">
        <p>
          Bir öğrencinin neyi bildiğini gösteren tek belge sınav sonucu olmamalı. Kaç kişiye,
          hangi konuyu, ne kadar süre anlattığı da bir yetkinlik kaydıdır — ve bugün hiçbir
          yerde tutulmuyor.
        </p>
        <p>
          Hedefimiz, anlatarak geçirilen zamanın karşılaştırılabilir ve doğrulanabilir bir
          değere dönüştüğü bir ağ kurmak. Seviye sistemi bunun ilk adımı: her seviye,
          onaylanmış derslerin toplamıdır ve yükseldikçe zorlaşır.
        </p>
      </Bolum>

      <Bolum etiket="Nasıl çalışır" baslik="Dört kural">
        <div className="grid gap-3 sm:grid-cols-2">
          {ILKELER.map(({ Ikon, baslik, metin }) => (
            <article
              key={baslik}
              className="flex flex-col gap-2.5 rounded-xl border border-slate-200/80 bg-white p-5"
            >
              <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-brand-50 text-brand-700">
                <Ikon className="h-5 w-5" />
              </span>
              <h3 className="text-[15px] font-semibold text-slate-900">{baslik}</h3>
              <p className="text-sm leading-relaxed text-slate-600">{metin}</p>
            </article>
          ))}
        </div>
      </Bolum>

      <Bolum etiket="Kim yapıyor" baslik="Öğrencilerin kurduğu bir ürün">
        <p>
          dersmate, aynı sıkıntıyı yaşamış iki öğrenci tarafından geliştiriliyor. Ürünü
          kullanan kitlenin içinden çıkmış olması bir slogan değil, bir yöntem: eklenen her
          özellik önce kendi çalışma düzenimizde işe yarıyor mu diye sınanıyor.
        </p>
        <p>
          Görüşünü duymak isteriz — menüdeki sosyal hesaplardan yazabilirsin.
        </p>
      </Bolum>
    </div>
  )
}
