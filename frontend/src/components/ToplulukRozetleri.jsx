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
  ⚠️ SAYAÇ HENÜZ SUNUCUDA YOK. Profil ucu `communityUpvotes` alanını döndürmüyor
  (Topluluk bölümünün kalıcılık katmanı da bağlı değil — bkz. pages/Topluluk.jsx
  başındaki uyarı). Bileşen bu durumu UYDURMUYOR: alan yoksa rozet kazanılmış gibi
  gösterilmiyor, yerine kademe MERDİVENİ çiziliyor — "burada ne kazanabilirsin"
  ekranı, "ne kazandın" değil.

  Sunucu alanı eklediğinde tek yapılacak şey `communityUpvotes` göndermek; bu dosyada
  değişiklik gerekmiyor, kazanılmış hâl zaten yazılı ve `oy` sayı olur olmaz devreye
  giriyor.

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
      title={`${ROZET_ADI} — ${oy} yukarı oy (${kademe.metal})`}
    >
      <Madalya kademe={kademe} />
      <span>{ROZET_ADI}</span>
      <span className="sr-only">
        ({kademe.metal} kademe, {oy} yukarı oy)
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
            title={`${kademe.metal} — ${kademe.oy} yukarı oyda açılıyor`}
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
 * @param oy               Toplam yukarı oy. Sunucu bu alanı henüz göndermiyor;
 *                         undefined/null geldiğinde bileşen merdiven kipine düşüyor.
 * @param kendiProfilim    Merdiven yalnızca kendi profilinde çiziliyor.
 *
 * SubjectBadges ile aynı yüzey ve aynı gizlenme kuralı: gösterecek bir şey yoksa
 * bileşen hiç çizilmiyor. Boş bir "rozetin yok" kutusu, henüz başlamamış birine
 * eksiklik gibi okunuyor.
 */
export function ToplulukRozetleri({ oy, kendiProfilim = false }) {
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
          <span className="text-xs text-slate-600">{toplam} yukarı oy</span>
        </div>
      ) : (
        <>
          <KademeMerdiveni oy={toplam} />
          {/* Sayaç yokken cümle bir DURUM değil KURAL anlatıyor ("...dönüşür"): "şu an
              0 oydasın" demek, olmayan bir sayacı varmış gibi göstermek olurdu. */}
          <p className="mt-3 text-xs leading-relaxed text-slate-600">
            {sayacVar
              ? `Gönderi ve yorumlarına gelen yukarı oylar burada sayılıyor — şu an ${toplam} oydasın.`
              : 'Topluluktaki gönderi ve yorumlarına gelen yukarı oylar burada madalyaya dönüşür.'}
          </p>
        </>
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
