import { useState } from 'react'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'

/*
  BRANŞ ROZETLERİ.

  Rozet, kazanılan puana değil o branşta ANLATILAN SÜREYE bakar. İki kademe var:
    8 saat  → "Matematik Öğretici"  (gümüş)
    15 saat → "Matematik Üstadı"    (altın)

  Hesap tamamen backend'de (SubjectBadgeEngine); burada tek bir mantık yok, yalnızca
  gösterim. Başlık metni bile ("Matematik Öğretici") sunucudan hazır geliyor — Türkçe
  ekler tek yerde kalsın diye.

  ─────────────────────────────────────────────────────────────────────────────
  ÜÇ KADEME İKİYE İNDİ (2026-08-24). Eski merdiven 5 / 20 / 50 saatti ve iki ucu da
  işe yaramıyordu: 5 saat rozeti neredeyse herkeste vardı, yani hiçbir şey ayırt
  etmiyordu; 50 saat ise pratikte kimsenin ulaşamadığı bir sayıydı, yani ödül değil
  dekordu. 8 ve 15 saat, kazanılabilir ve kazanıldığında bir şey söyleyen iki eşik.

  MADALYA GÖRÜNÜMÜ. Eski rozetler branşa göre renklendirilmiş düz haplardı (Matematik
  mavi, Fizik mor…) ve seviyeyi yalnızca halka kalınlığı ile nokta sayısı anlatıyordu —
  yani en zor kazanılan şey en zor fark edilen şeydi. Artık renk BRANŞI değil KADEMEYİ
  anlatıyor: gümüş ve altın, herkesin ilk bakışta sıraladığı iki metal.

  Metalik his üç katmandan geliyor, hepsi CSS:
    1. Eğik gradyan (135°) — ışığın tek yönden geldiği izlenimi
    2. Üstte beyaz bir parlaklık şeridi (inset ring + üst yarıda beyaz/30 katman)
    3. Kendi renginde renkli gölge — metal, kâğıt gibi nötr gölge düşürmez

  Branş bilgisi kaybolmuyor: ikon rozetin içinde duruyor ve başlıkta zaten dersin adı
  yazıyor ("Matematik Üstadı"). Kaybolan tek şey branşın RENGİ ve o renk, kademenin
  önüne geçtiği için bilerek bırakıldı.
  ─────────────────────────────────────────────────────────────────────────────
*/

/* Branş ikonları. Renk taşımıyorlar artık — madalyanın kendi metali baskın. */
const BRANS_IKONU = {
  Turkce: '📖',
  Tarih: '🏛️',
  Cografya: '🌍',
  Matematik: '🔢',
  Geometri: '📐',
  Fizik: '⚛️',
  Kimya: '🧪',
  Biyoloji: '🧬',
}

/*
  KADEME STİLLERİ.

  Sıra numarası ayrıca tutuluyor (`sira`): aynı branşta iki rozet varsa yükseği
  seçmek için sayısal bir karşılaştırma gerekiyor ve enum adına göre alfabetik
  sıralamak tesadüfen doğru sonuç verip yarın sessizce bozulurdu.

  Kontrast: her iki madalyada da yazı kendi zemininin en koyu tonunda
  (amber-950 / slate-900) — ölçüldü, ikisi de AA eşiğinin üstünde. Altın rozette
  beyaz yazı denendi ve sarı zeminde 1.9:1 ile okunamıyordu.
*/
const KADEME = {
  Ogretici: {
    sira: 1,
    etiket: 'Gümüş',
    madalya: '🥈',
    kabuk:
      'bg-gradient-to-br from-slate-200 via-slate-100 to-slate-400 text-slate-900 ' +
      'ring-1 ring-inset ring-white/70 shadow-md shadow-slate-400/40',
    parlama: 'from-white/70',
  },
  Ustad: {
    sira: 2,
    etiket: 'Altın',
    madalya: '🥇',
    kabuk:
      'bg-gradient-to-br from-yellow-300 via-amber-200 to-yellow-500 text-amber-950 ' +
      'ring-1 ring-inset ring-white/70 shadow-md shadow-amber-500/40',
    parlama: 'from-white/80',
  },
}

const VARSAYILAN_KADEME = KADEME.Ogretici

/** Bir sonraki eşiğe kalan saat — ilerleme satırı için. Kural sunucuda; bu yalnız gösterim. */
const ESIKLER = [8, 15]

function sonrakiEsik(saat) {
  return ESIKLER.find((e) => saat < e) ?? null
}

export function SubjectBadges({ userId, kendiProfilim = false }) {
  const veri = useAsync(() => api.userSubjectBadges(userId), [userId])
  const [ilerlemeAcik, setIlerlemeAcik] = useState(false)

  // SESSİZ BAŞARISIZLIK: rozet şeridi profilin yardımcı bir parçası. Uç 500 dönerse
  // kullanıcının profili açılmaya devam etmeli — burada hata kutusu göstermek, asıl
  // içeriği gölgeleyen bir gürültü olurdu. Yükleniyorken de yer tutmuyoruz.
  if (veri.loading || veri.error || !veri.data) return null

  const { badges = [], progress = [] } = veri.data

  // Aynı branştan yalnızca EN YÜKSEK kademe gösterilir. Backend alt kademeyi de
  // saklıyor (kazanım geçmişi), ama "Matematik Öğretici + Matematik Üstadı" yan yana
  // durunca düşük olan yükseği zayıflatıyor.
  const enYuksek = new Map()
  for (const b of badges) {
    const mevcut = enYuksek.get(b.branch)
    const yeniSira = (KADEME[b.level] ?? VARSAYILAN_KADEME).sira
    const mevcutSira = mevcut ? (KADEME[mevcut.level] ?? VARSAYILAN_KADEME).sira : -1
    if (yeniSira > mevcutSira) enYuksek.set(b.branch, b)
  }

  const gosterilecek = [...enYuksek.values()].sort(
    (a, b) =>
      (KADEME[b.level] ?? VARSAYILAN_KADEME).sira - (KADEME[a.level] ?? VARSAYILAN_KADEME).sira,
  )

  // Rozeti olmayan ama ders anlatmış branşlar — "az kaldı" göstergesi.
  const rozetsiz = progress.filter((p) => !enYuksek.has(p.branch) && p.hours > 0)

  if (gosterilecek.length === 0 && rozetsiz.length === 0) {
    // Hiç ders anlatmamış kullanıcıda blok tamamen gizlenir; boş bir "rozet yok"
    // kutusu, henüz başlamamış birine eksiklik gibi görünür.
    return null
  }

  return (
    <section className="rounded-2xl border border-slate-100 bg-white p-5 shadow-sm">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold text-slate-800">Branş rozetleri</h2>
        {rozetsiz.length > 0 && (
          <button
            type="button"
            onClick={() => setIlerlemeAcik((v) => !v)}
            className="text-xs font-medium text-brand-700 transition hover:text-brand-800 hover:underline"
            aria-expanded={ilerlemeAcik}
          >
            {ilerlemeAcik ? 'İlerlemeyi gizle' : `İlerleme (${rozetsiz.length})`}
          </button>
        )}
      </div>

      {gosterilecek.length > 0 ? (
        <ul className="flex flex-wrap gap-3">
          {gosterilecek.map((b) => (
            <li key={`${b.branch}-${b.level}`}>
              <Madalya rozet={b} />
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-xs text-slate-500">
          {kendiProfilim
            ? 'İlk rozet 8 saat ders anlatımıyla geliyor.'
            : 'Henüz branş rozeti kazanılmamış.'}
        </p>
      )}

      {ilerlemeAcik && rozetsiz.length > 0 && (
        <ul className="mt-4 space-y-2 border-t border-slate-100 pt-4">
          {rozetsiz.map((p) => {
            const hedef = sonrakiEsik(p.hours)
            const oran = hedef ? Math.min(100, (p.hours / hedef) * 100) : 100
            return (
              <li key={p.branch} className="flex items-center gap-2 text-xs text-slate-500">
                <span className="w-20 shrink-0 truncate">{p.subject}</span>
                <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                  <div
                    className="h-full rounded-full bg-gradient-to-r from-slate-300 to-slate-400"
                    style={{ width: `${oran}%` }}
                  />
                </div>
                <span className="shrink-0 tabular-nums">
                  {p.hours}/{hedef ?? ESIKLER.at(-1)} sa
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

/**
 * Tek madalya.
 *
 * Metalik his katman katman kuruluyor; tek bir gradyan "metal" hissi vermiyor, düz
 * renkli bir hap gibi duruyor. Üç katman:
 *   • eğik ana gradyan (ışık sol üstten)
 *   • üst yarıda beyaz parlaklık (metalin ışık alan yüzü)
 *   • kendi renginde gölge (metal nötr gri gölge düşürmez)
 *
 * `overflow-hidden` + `relative` şart: parlaklık katmanı mutlak konumlu ve rozetin
 * yuvarlak köşelerinden taşmamalı.
 */
function Madalya({ rozet }) {
  const k = KADEME[rozet.level] ?? VARSAYILAN_KADEME
  const ikon = BRANS_IKONU[rozet.branch] ?? '🎓'

  return (
    <span
      className={`relative inline-flex items-center gap-2 overflow-hidden rounded-full
                  py-1.5 pl-1.5 pr-3.5 text-sm font-semibold ${k.kabuk}`}
      title={`${rozet.title} — ${rozet.hours} saat ders anlatımı (${k.etiket})`}
    >
      {/* Parlaklık: üst yarıyı kaplayan yumuşak beyaz geçiş. pointer-events yok,
          tamamen dekoratif. */}
      <span
        aria-hidden="true"
        className={`pointer-events-none absolute inset-x-0 top-0 h-1/2 bg-gradient-to-b ${k.parlama} to-transparent`}
      />

      {/* Madalyon: koyu bir kuyu içinde branş ikonu. Metalin üstünde ikinci bir yüzey
          olması, rozeti düz bir etiketten çıkarıp nesneye benzetiyor. */}
      <span
        aria-hidden="true"
        className="relative grid h-6 w-6 shrink-0 place-items-center rounded-full
                   bg-white/60 text-[13px] shadow-inner ring-1 ring-inset ring-white/80"
      >
        {ikon}
      </span>

      <span className="relative">{rozet.title}</span>

      {/* Kademe işareti sonda: madalya emojisi platformdan platforma değişse bile
          metalin RENGİ kademeyi zaten söylüyor, bu yüzden emoji tek başına taşıyıcı
          değil — destekleyici. */}
      <span aria-hidden="true" className="relative text-base leading-none">
        {k.madalya}
      </span>

      <span className="sr-only">({k.etiket} kademe)</span>
    </span>
  )
}
