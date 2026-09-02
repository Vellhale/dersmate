/*
  TOPLULUK ROZETLERİ — forumda alınan toplam YUKARI OY'a bağlı üç kademe.

    100 oy  → Bronz
    500 oy  → Gümüş
   1000 oy  → Altın

  ÜÇÜNÜN DE ADI AYNI: "Topluluk Üyesi". Bu bir eksiklik değil, istenen tasarım —
  kademeyi ad değil MADALYANIN RENGİ taşıyor. Branş rozetlerinde ad değişiyordu
  ("Öğretici" / "Üstad") çünkü orada rozet bir yetkinlik iddiasıydı; burada iddia yok,
  katkı var. Üç ayrı unvan uydurmak ("Topluluk Dostu / Sevileni / Efsanesi") forumda
  hiyerarşi kurar ve akranlık fikrine ters düşerdi.

  ─── VERİ ───────────────────────────────────────────────────────────────────────
  Sayaç profil ucundan geliyor (`communityUpvotes`, 2026-08-27'de bağlandı).

  ⚠️ SAYI NET OY: artı − eksi, yalnızca GÖRÜNÜR içerikte (2026-08-29'da değişti).
  Ham artıyla sayılsaydı 300 artı / 400 eksi alan bir gönderi — topluluğun değersiz
  bulduğu bir katkı — hem rozet hem PUAN üretirdi; oy artık puana dönüştüğü için
  (CommunityRewardRules: 300 net oy → 100 puan) bu fark önemli hâle geldi.

  Alan hiç gelmezse (eski bir sunucu) bileşen durumu UYDURMUYOR: rozet kazanılmış gibi
  gösterilmiyor, yerine kademe MERDİVENİ çiziliyor — "burada ne kazanabilirsin"
  ekranı, "ne kazandın" değil.

  Merdiven YALNIZCA KENDİ PROFİLİNDE görünüyor. Başkasının profilinde "kazanabileceği
  rozetler" listesi, o kişi hakkında hiçbir şey söylemeyen bir reklam olurdu.
  ─────────────────────────────────────────────────────────────────────────────────

  GÖRSEL DİL: SubjectBadges / UniversiteRozetleri ile aynı yüzey (beyaz kart, hap
  biçiminde rozet, satır içi SVG madalya). Gradyan iki durak, parlama şeridi ve renkli
  gölge YOK — ikisi de bu projede daha önce "gürültü" gerekçesiyle kaldırıldı, aynı
  gerekçe burada da geçerli. Emoji yok: madalya emojisi her platformda başka çiziliyor
  ve rozet cihazdan cihaza farklı görünüyordu.

  MADALYA ÇİZİMİ NEDEN BURADA (UniversiteRozetleri'nden import edilmiyor): oradaki
  çizim iki kademelik bir tabloya bağlı ve bronz durağı yok; ortak bir bileşene
  çıkarmak, çalışan ve şu an doğrulanamayan (API kapalı) bir dosyayı değiştirmeyi
  gerektirirdi. Üçüncü bir kademe eklenirse ortak bileşene çıkarmanın zamanı gelmiş
  demektir — o zaman her iki dosya birlikte taşınmalı.
*/

/*
  KADEMELER — en yüksekten aşağı. `kademeBul` ilk eşleşeni döndürüyor, sıra bu yüzden
  önemli: artan sırada olsaydı 1200 oyu olan kullanıcı bronz rozet alırdı.

  Renkler Tailwind sınıfı, hex DEĞİL: e2e/kaynak-sabitleri.spec.js src altında palet
  dışı hex arıyor ve sabit renk yazmak o süpürgeye takılır.

  Bronz için amber DEĞİL, amber-700/800 ailesinden koyu bir kahve tonu seçildi: altın
  zaten amber-200→amber-500 aralığında ve iki kademe yan yana geldiğinde ayırt
  edilebilmeli. amber-600→amber-800, altının yanında belirgin biçimde daha koyu ve
  "bakır" okunuyor.
*/
const KADEMELER = [
  {
    oy: 1000,
    metal: 'Altın',
    disk: 'from-amber-200 to-amber-500',
    halka: 'ring-amber-600/60',
    hap: 'border-amber-200 bg-amber-50/60 text-amber-900',
  },
  {
    oy: 500,
    metal: 'Gümüş',
    disk: 'from-slate-100 to-slate-400',
    halka: 'ring-slate-500/50',
    hap: 'border-slate-200 bg-slate-50 text-slate-800',
  },
  {
    oy: 100,
    metal: 'Bronz',
    disk: 'from-amber-600 to-amber-800',
    halka: 'ring-amber-900/50',
    hap: 'border-amber-200/70 bg-amber-50/40 text-amber-900',
  },
]

/** Üç kademede de aynı: rozetin adı metali değil, üyeliği söylüyor. */
const ROZET_ADI = 'Topluluk Üyesi'

/** Kazanılmış en yüksek kademe; hiçbiri tutmuyorsa null. */
function kademeBul(oy) {
  return KADEMELER.find((k) => oy >= k.oy) ?? null
}

/** Bir sonraki eşik — ilerleme çubuğu için. Zirvedeyse null. */
function sonrakiEsik(oy) {
  const artan = [...KADEMELER].sort((a, b) => a.oy - b.oy)
  return artan.find((k) => oy < k.oy)?.oy ?? null
}

/**
 * Madalya: üstte iki kurdele şeridi, altta gradyanlı disk.
 *
 * Gradyan SVG `<linearGradient>` ile değil, diskin kendisi bir HTML kutusu olarak
 * çiziliyor. Sebep: SVG gradyanı her kademe için benzersiz bir `id` ister ve aynı
 * sayfada iki rozet varsa id çakışması ilk gradyanı ikinciye de uygular (bu proje bunu
 * favicon'da bir kez yaşadı). Tailwind sınıfıyla boyanan kutu bu sorunu tanımıyor.
 *
 * Kurdele nötr slate ve diskten ÖNCE çiziliyor: uçları diskin arkasında kalsın, dikkat
 * metalde toplansın.
 */
function Madalya({ kademe }) {
  return (
    <span className="relative grid h-5 w-5 shrink-0 place-items-center" aria-hidden="true">
      <svg viewBox="0 0 24 24" className="absolute inset-0 h-5 w-5">
        <path d="M7 2h4l1.6 7-3.4.9z" className="fill-slate-300" />
        <path d="M13 2h4l-1.6 7.9-3.4-.9z" className="fill-slate-300" />
      </svg>
      <span
        className={`absolute bottom-0 h-[13px] w-[13px] rounded-full bg-gradient-to-b
                    ${kademe.disk} ring-1 ring-inset ${kademe.halka}`}
      />
    </span>
  )
}

/** Kazanılmış rozet hapı. Oy sayısı tooltip'te ve ekran okuyucu metninde. */
function Rozet({ kademe, oy }) {
  return (
    <span
      className={`inline-flex items-center gap-2 rounded-full border py-1.5 pl-2.5 pr-3.5
                  text-sm font-medium shadow-sm ${kademe.hap}`}
      title={`${ROZET_ADI} — ${oy} net oy (${kademe.metal})`}
    >
      <Madalya kademe={kademe} />
      <span>{ROZET_ADI}</span>
      <span className="sr-only">
        ({kademe.metal} kademe, {oy} net oy)
      </span>
    </span>
  )
}

/**
 * Kademe merdiveni — HENÜZ KAZANILMAMIŞ üç rozet.
 *
 * Haplar kazanılmış hâlle aynı görünmüyor: zemin nötr, eşik sayısı hapın İÇİNDE ve
 * asıl etiketten önce okunuyor. Amaç, bunların bir başarı değil bir HEDEF listesi
 * olduğunun tek bakışta anlaşılması — kazanılmış rozetle aynı görünselerdi, profili
 * gezen biri üçünü de kazanılmış sanırdı.
 *
 * Madalya tam renkli kalıyor (soluklaştırılmadı): kullanıcıya neyi kazanacağını
 * göstermenin tek yolu o madalyayı göstermek.
 */
function KademeMerdiveni({ oy }) {
  const artan = [...KADEMELER].sort((a, b) => a.oy - b.oy)

  return (
    <ul className="flex flex-wrap gap-2">
      {artan.map((kademe) => (
        <li key={kademe.metal}>
          <span
            className="inline-flex items-center gap-2 rounded-full border border-slate-200
                       bg-white py-1.5 pl-2.5 pr-3.5 text-sm text-slate-600"
            title={`${kademe.metal} — ${kademe.oy} net oyda açılıyor`}
          >
            <Madalya kademe={kademe} />
            <span className="font-semibold tabular-nums text-slate-800">{kademe.oy} oy</span>
            <span className="text-slate-600">{ROZET_ADI}</span>
            <span className="sr-only">
              — {kademe.metal} kademe, henüz kazanılmadı
              {oy > 0 ? `; şu an ${oy} oy` : ''}
            </span>
          </span>
        </li>
      ))}
    </ul>
  )
}

/**
 * Profildeki topluluk rozeti şeridi.
 *
 * @param oy               Toplam NET oy (artı − eksi, görünür içerikte).
 *                         undefined/null geldiğinde bileşen merdiven kipine düşüyor.
 * @param kendiProfilim    Merdiven yalnızca kendi profilinde çiziliyor.
 *
 * SubjectBadges ile aynı yüzey ve aynı gizlenme kuralı: gösterecek bir şey yoksa
 * bileşen hiç çizilmiyor. Boş bir "rozetin yok" kutusu, henüz başlamamış birine
 * eksiklik gibi okunuyor.
 */
/**
 * Topluluk katkısının sayıları: gönderi, yorum, net oy ve puana kalan.
 *
 * ⚠️ ÜST SAYAÇ ŞERİDİNE EKLENMEDİ, bilerek. Oradaki ızgara TAM DÖRT kalem için
 * yazılmış (2×2 / lg:grid-cols-4) ve beşinci kalem düzeni bozuyor. Ama asıl gerekçe
 * yerleşim değil ANLAM: üstteki şerit kişinin DERS kimliğini anlatıyor (kaç ders
 * anlattı, puanı, seviyesi). Topluluk katkısı ayrı bir eksen ve kendi kartında,
 * rozetin hemen yanında okunması daha doğru.
 *
 * PUANA KALAN da yazılıyor: rozet merdiveni "hangi madalyaya ne kadar kaldı" diyor,
 * bu satır "kaç oyda 100 puan" diyor. İkisi farklı sorular ve kullanıcı ikincisini
 * ancak burada görebiliyor — puan kazanmanın ders dışında bir yolu olduğunu
 * öğrendiği tek yer.
 */
function KatkiSayaclari({ gonderi, yorum, oy, puanEsigi, puanOdul }) {
  // Bir sonraki ödüle kalan oy. Sıfır olamaz: eşiği tam dolduran an zaten ödenmiş olur.
  const kalan = puanEsigi - (oy % puanEsigi)

  const kalemler = [
    { deger: gonderi, etiket: gonderi === 1 ? 'gönderi' : 'gönderi' },
    { deger: yorum, etiket: 'yorum' },
    { deger: oy, etiket: 'net oy' },
  ]

  return (
    <div className="mt-4 border-t border-slate-100 pt-4">
      <dl className="grid grid-cols-3 gap-2">
        {kalemler.map((k) => (
          <div key={k.etiket} className="rounded-lg bg-slate-50 p-2.5 text-center">
            <dd className="text-base font-bold tabular-nums text-slate-900">{k.deger}</dd>
            <dt className="mt-0.5 text-[11px] text-slate-600">{k.etiket}</dt>
          </div>
        ))}
      </dl>

      <p className="mt-3 text-xs leading-relaxed text-slate-600">
        Her <span className="font-semibold tabular-nums">{puanEsigi}</span> net oy{' '}
        <span className="font-semibold tabular-nums">{puanOdul}</span> puan kazandırıyor —
        ders anlatmadan da seviye atlayabilirsin.
        {oy > 0 && (
          <>
            {' '}
            Bir sonraki <span className="font-semibold tabular-nums">{puanOdul}</span> puana{' '}
            <span className="font-semibold tabular-nums">{kalan}</span> oy kaldı.
          </>
        )}
      </p>
    </div>
  )
}

/**
 * @param gonderiSayisi/yorumSayisi  Görünür forum içeriği sayıları (profil ucundan).
 * @param puanEsigi/puanOdul         Sunucudaki CommunityRewardRules ile aynı olmalı.
 */
export function ToplulukRozetleri({
  oy,
  kendiProfilim = false,
  gonderiSayisi = 0,
  yorumSayisi = 0,
  puanEsigi = 300,
  puanOdul = 100,
}) {
  const sayacVar = typeof oy === 'number'
  const toplam = sayacVar ? oy : 0
  const kademe = sayacVar ? kademeBul(toplam) : null
  const hedef = sonrakiEsik(toplam)

  // Sayaç yokken (topluluk henüz açılmadı) yalnızca kendi profilinde merdiven; başkasının
  // profilinde çizilecek hiçbir şey yok.
  if (!sayacVar && !kendiProfilim) return null
  // Sayaç var ama kişi hiç oy almamışsa ve profil başkasınınsa: gizle.
  if (sayacVar && !kademe && !kendiProfilim) return null

  return (
    <section className="rounded-2xl border border-slate-100 bg-white p-5 shadow-sm">
      {/* "Yakında" çipi KALDIRILDI (2026-08-25, ürün sahibi kararı) — menüdeki ve
          Topluluk sayfasındakiyle birlikte. Bölüm artık diğer rozet şeritleriyle aynı
          ağırlıkta duruyor; kazanılmamış olmayı çipin değil, merdivenin kendisi
          anlatıyor (eşik sayıları rozet adından önce okunuyor). */}
      <h2 className="mb-4 text-sm font-semibold text-slate-800">Topluluk rozetleri</h2>

      {kademe ? (
        <div className="flex flex-wrap items-center gap-3">
          <Rozet kademe={kademe} oy={toplam} />
          <span className="text-xs text-slate-600">{toplam} net oy</span>
        </div>
      ) : (
        <>
          <KademeMerdiveni oy={toplam} />
          {/* Sayaç yokken cümle bir DURUM değil KURAL anlatıyor ("...dönüşür"): "şu an
              0 oydasın" demek, olmayan bir sayacı varmış gibi göstermek olurdu. */}
          <p className="mt-3 text-xs leading-relaxed text-slate-600">
            {sayacVar
              ? `Gönderi ve yorumlarına gelen oylar (artı − eksi) burada sayılıyor — şu an ${toplam} net oydasın.`
              : 'Topluluktaki gönderi ve yorumlarına gelen oylar (artı − eksi) burada madalyaya dönüşür.'}
          </p>
        </>
      )}

      {/* Katkı sayaçları yalnızca sayaç varken: sunucu bu alanları göndermiyorsa
          "0 gönderi / 0 yorum" yazmak, olmayan bir veriyi sıfır diye göstermek olurdu. */}
      {sayacVar && (
        <KatkiSayaclari
          gonderi={gonderiSayisi}
          yorum={yorumSayisi}
          oy={toplam}
          puanEsigi={puanEsigi}
          puanOdul={puanOdul}
        />
      )}

      {/* İlerleme çubuğu yalnızca sayaç VARKEN ve zirvede DEĞİLKEN. Sayaç yokken
          çizilseydi hep %0 duran, hiç kıpırdamayan bir çubuk olurdu — ilerleme
          göstermeyen bir ilerleme çubuğu. */}
      {sayacVar && hedef && (
        <div className="mt-4 flex items-center gap-2 border-t border-slate-100 pt-4 text-xs text-slate-600">
          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
            <div
              className="h-full rounded-full bg-slate-300"
              style={{ width: `${Math.min(100, (toplam / hedef) * 100)}%` }}
            />
          </div>
          <span className="shrink-0 tabular-nums">
            {toplam}/{hedef} oy
          </span>
        </div>
      )}
    </section>
  )
}
