import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'

/*
  ÜNİVERSİTE ROZETLERİ — iki kademe, branşsız.

  Branş rozetlerinden (SubjectBadges) FARKI: orada rozet bir DERSE bağlıdır ("Matematik
  Üstadı") çünkü YKS tarafında anlatılan şey bir konudur. Üniversite tarafında ders ve
  konu kavramı YOK — iki kişi eşleşiyor, sohbette konuşuyor. Ölçülebilen tek şey birlikte
  geçirilen süre, o yüzden rozet de branşsız:

    8 saat  → Öğretici (gümüş)
    15 saat → Üstad    (altın)

  Eşikler branş rozetleriyle BİLEREK aynı (SubjectBadgeRules): kullanıcı tek bir merdiven
  öğreniyor, ikinci bir sayı ezberlemiyor.

  ─── VERİ NEREDEN GELİYOR ───────────────────────────────────────────────────────
  Yeni bir uç açılmadı. `userSubjectBadges` yanıtındaki `progress[]` zaten branş başına
  DAKİKA taşıyor; toplamı, kullanıcının platformda görüşerek geçirdiği toplam süre.
  Backend'de ikinci bir sayaç kurmak aynı sayıyı iki yerde tutmak olurdu ve ikisi er ya
  da geç ayrışırdı.

  BİLİNEN SINIR: bu toplam, TAMAMLANMIŞ ders oturumlarından türüyor. Henüz hiç oturumu
  olmayan bir üniversite kullanıcısında sıfırdır, yani rozet çıkmaz — bileşen de o
  durumda kendini gizler. Sohbet üzerinden yapılan görüşmelerin süresi ölçülmüyor;
  ölçülseydi kaynak yine burası olurdu, bileşen değişmezdi.
  ─────────────────────────────────────────────────────────────────────────────────

  GÖRSEL DİL: gradyan VAR ama parlama YOK. Sahibin isteği "şık, modern ve sade bir
  Tailwind gradient'i; abartılı glow efektleri veya karmaşık emojiler kesinlikle yok".
  Gradyan yalnızca madalya diskinde ve iki durakta — metalin yönünü veren en küçük
  jest. Renkli gölge (`shadow-amber-500/40` gibi) ve beyaz parlama şeridi bilerek
  kullanılmadı: ikisi de bir önceki turda branş rozetlerinden "gürültü" gerekçesiyle
  kaldırılmıştı, aynı gerekçe burada da geçerli.

  EMOJİ YOK: madalya satır içi SVG. Emoji her platformda başka çiziliyor ve aynı rozet
  Windows'ta başka, iOS'ta başka görünüyordu (bkz. SubjectBadges'teki aynı karar).
*/

/** Saat eşikleri. Sıra ÖNEMLİ: en yüksekten aşağı taranıyor. */
const KADEMELER = [
  {
    saat: 15,
    etiket: 'Üstad',
    metal: 'Altın',
    // Gradyan iki durak: açık üstten koyu alta. Üç durak ve beyaz parlama şeridi
    // denendi, "metal" değil "cam" hissi veriyordu.
    disk: 'from-amber-200 to-amber-500',
    halka: 'ring-amber-600/60',
    kurdele: 'fill-slate-300',
    hap: 'border-amber-200 bg-amber-50/60 text-amber-900',
  },
  {
    saat: 8,
    etiket: 'Öğretici',
    metal: 'Gümüş',
    disk: 'from-slate-100 to-slate-400',
    halka: 'ring-slate-500/50',
    kurdele: 'fill-slate-300',
    hap: 'border-slate-200 bg-slate-50 text-slate-800',
  },
]

/* Toplam saatten kazanılmış en yüksek kademe. Yoksa null.
   DIŞA AKTARILMIYOR: tek çağıranı bu dosya. Bu projede çağrılmayan bir export iki kez
   gizli hata sakladı (bkz. Ikonlar.jsx), o yüzden kapsam dar tutuluyor. */
function kademeBul(saat) {
  return KADEMELER.find((k) => saat >= k.saat) ?? null
}

/** Bir sonraki eşiğe kalan saat — ilerleme satırı için. Zirvedeyse null. */
function sonrakiEsik(saat) {
  const artan = [...KADEMELER].sort((a, b) => a.saat - b.saat)
  return artan.find((k) => saat < k.saat)?.saat ?? null
}

/**
 * Madalya: üstte iki kurdele şeridi, altta gradyanlı disk.
 *
 * Gradyan SVG'de `<linearGradient>` ile değil, `foreignObject` da değil — diskin
 * kendisi bir HTML kutusu. Sebep: SVG gradyanı için her kademeye benzersiz bir `id`
 * gerekiyor ve aynı sayfada iki rozet varsa id çakışması ilk gradyanı ikinciye de
 * uyguluyor (aynı hata bu projede favicon'da bir kez yaşandı). Tailwind sınıfıyla
 * çizilen kutu bu sorunu tanımıyor ve renkler paletle birlikte yaşıyor.
 */
function Madalya({ kademe }) {
  return (
    <span className="relative grid h-5 w-5 shrink-0 place-items-center" aria-hidden="true">
      {/* Kurdele: diskin ARKASINDA kalsın diye önce çiziliyor ve üst hizada duruyor. */}
      <svg viewBox="0 0 24 24" className="absolute inset-0 h-5 w-5">
        <path d="M7 2h4l1.6 7-3.4.9z" className={kademe.kurdele} />
        <path d="M13 2h4l-1.6 7.9-3.4-.9z" className={kademe.kurdele} />
      </svg>
      {/* Disk: gradyan burada. `ring-1` ile ince bir kenar — kademe rengini taşıyan
          asıl şey gradyan, kenar yalnızca beyaz zeminde sınırı okutuyor. */}
      <span
        className={`absolute bottom-0 h-[13px] w-[13px] rounded-full bg-gradient-to-b
                    ${kademe.disk} ring-1 ring-inset ${kademe.halka}`}
      />
    </span>
  )
}

/** Tek rozet hapı: madalya + kademe adı. Saat bilgisi tooltip'te. */
function UniversiteRozeti({ kademe, saat }) {
  return (
    <span
      className={`inline-flex items-center gap-2 rounded-full border py-1.5 pl-2.5 pr-3.5
                  text-sm font-medium shadow-sm ${kademe.hap}`}
      title={`${kademe.etiket} — ${saat} saat görüşme (${kademe.metal})`}
    >
      <Madalya kademe={kademe} />
      <span>{kademe.etiket}</span>
      <span className="sr-only">({kademe.metal} kademe, {saat} saat görüşme)</span>
    </span>
  )
}

/**
 * Profildeki üniversite rozeti şeridi.
 *
 * Rozet YOKSA ve ilerleme de yoksa bileşen kendini tamamen gizler — henüz başlamamış
 * birine boş bir "rozetin yok" kutusu göstermek eksiklik gibi okunur (aynı karar
 * SubjectBadges'te de alındı).
 */
export function UniversiteRozetleri({ userId, kendiProfilim = false }) {
  const veri = useAsync(() => api.userSubjectBadges(userId), [userId])

  // SESSİZ BAŞARISIZLIK: rozet şeridi profilin yardımcı bir parçası. Uç 500 dönerse
  // profil açılmaya devam etmeli; burada hata kutusu asıl içeriği gölgelerdi.
  if (veri.loading || veri.error || !veri.data) return null

  const dakika = (veri.data.progress ?? []).reduce((t, p) => t + (p.minutes ?? 0), 0)
  const saat = Math.floor(dakika / 60)
  const kademe = kademeBul(saat)
  const hedef = sonrakiEsik(saat)

  if (!kademe && saat === 0) return null

  return (
    <section className="rounded-2xl border border-slate-100 bg-white p-5 shadow-sm">
      <h2 className="mb-4 text-sm font-semibold text-slate-800">Görüşme rozetleri</h2>

      {kademe ? (
        <div className="flex flex-wrap items-center gap-3">
          <UniversiteRozeti kademe={kademe} saat={saat} />
          <span className="text-xs text-slate-500">{saat} saat görüşme</span>
        </div>
      ) : (
        <p className="text-xs text-slate-500">
          {kendiProfilim
            ? `İlk rozet 8 saat görüşmede geliyor — ${saat} saatteysin.`
            : `${saat} saat görüşme yapmış.`}
        </p>
      )}

      {/* İlerleme çubuğu yalnızca zirvede DEĞİLKEN. Üstad olan birine "bir sonraki
          eşik" göstermek, olmayan bir hedefi varmış gibi sunardı. */}
      {hedef && (
        <div className="mt-4 flex items-center gap-2 border-t border-slate-100 pt-4 text-xs text-slate-500">
          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
            <div
              className="h-full rounded-full bg-slate-300"
              style={{ width: `${Math.min(100, (saat / hedef) * 100)}%` }}
            />
          </div>
          <span className="shrink-0 tabular-nums">
            {saat}/{hedef} sa
          </span>
        </div>
      )}
    </section>
  )
}
