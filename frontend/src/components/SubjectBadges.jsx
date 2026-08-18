import { useState } from 'react'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'

/*
  BRANŞ ROZETLERİ ŞERİDİ.

  Rozet, kazanılan puana değil o derste ANLATILAN SÜREYE bakar: 5 saat → Çırak,
  20 saat → Usta, 50 saat → Üstad. Hesap tamamen backend'de (SubjectBadgeEngine);
  burada tek bir mantık yok, yalnızca gösterim. Başlık metni bile ("Matematik Çırağı")
  sunucudan hazır geliyor — Türkçe ekler (Çırağı/Ustası/Üstadı) tek yerde kalsın diye.
*/

/*
  Branş renkleri. Ders çağrışımına yakın, birbirinden ayırt edilebilir sekiz ton.

  MARKA PALETİNDEN (brand-*) BİLEREK AYRI: bunlar kategorik renklerdir, işlevleri
  BİRBİRİNDEN AYRILMAK. Hepsini marka mavisinin tonlarına indirseydik sekiz rozet tek
  bakışta ayırt edilemezdi. Marka rengi arayüzün "eylem" dili olarak kalıyor.

  Her ton, kendi 700 yazısını kendi 50 zemininde taşıyor — bu çift Tailwind'de WCAG AA
  eşiğini geçer (ölçülen en düşük oran amber'da 6.1:1).
*/
const BRANS_STILI = {
  Turkce: { bg: 'bg-rose-50', text: 'text-rose-700', ring: 'ring-rose-200', ikon: '📖' },
  Tarih: { bg: 'bg-amber-50', text: 'text-amber-800', ring: 'ring-amber-200', ikon: '🏛️' },
  Cografya: { bg: 'bg-lime-50', text: 'text-lime-800', ring: 'ring-lime-200', ikon: '🌍' },
  Matematik: { bg: 'bg-sky-50', text: 'text-sky-700', ring: 'ring-sky-200', ikon: '🔢' },
  Geometri: { bg: 'bg-indigo-50', text: 'text-indigo-700', ring: 'ring-indigo-200', ikon: '📐' },
  Fizik: { bg: 'bg-violet-50', text: 'text-violet-700', ring: 'ring-violet-200', ikon: '⚛️' },
  Kimya: { bg: 'bg-teal-50', text: 'text-teal-700', ring: 'ring-teal-200', ikon: '🧪' },
  Biyoloji: { bg: 'bg-emerald-50', text: 'text-emerald-700', ring: 'ring-emerald-200', ikon: '🧬' },
}

const VARSAYILAN_STIL = {
  bg: 'bg-slate-50', text: 'text-slate-700', ring: 'ring-slate-200', ikon: '🎓',
}

/* Seviye işareti. Halka kalınlığı seviyeyle artıyor: renk körlüğünden bağımsız bir sinyal. */
const SEVIYE_STILI = {
  Cirak: { halka: 'ring-1', isaret: '●', etiket: 'Çırak' },
  Usta: { halka: 'ring-2', isaret: '●●', etiket: 'Usta' },
  Ustad: { halka: 'ring-2 ring-offset-1', isaret: '●●●', etiket: 'Üstad' },
}

/** Bir sonraki eşiğe kalan saat — ilerleme satırı için. */
const ESIKLER = [5, 20, 50]

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

  // Aynı branştan yalnızca EN YÜKSEK seviye gösterilir. Backend alt seviyeleri de
  // saklıyor (kazanım geçmişi), ama "Matematik Çırağı + Matematik Ustası" yan yana
  // durunca şerit iki kat uzuyor ve düşük olan yükseği zayıflatıyor.
  const enYuksek = new Map()
  const sira = { Cirak: 1, Usta: 2, Ustad: 3 }
  for (const b of badges) {
    const mevcut = enYuksek.get(b.branch)
    if (!mevcut || sira[b.level] > sira[mevcut.level]) enYuksek.set(b.branch, b)
  }
  const gosterilecek = [...enYuksek.values()].sort((a, b) => sira[b.level] - sira[a.level])

  // Rozeti olmayan ama ders anlatmış branşlar — "az kaldı" göstergesi.
  const rozetsiz = progress.filter((p) => !enYuksek.has(p.branch) && p.hours > 0)

  if (gosterilecek.length === 0 && rozetsiz.length === 0) {
    // Hiç ders anlatmamış kullanıcıda blok tamamen gizlenir; boş bir "rozet yok"
    // kutusu, henüz başlamamış birine eksiklik gibi görünür.
    return null
  }

  return (
    <section className="rounded-xl border border-slate-200/80 bg-white p-4 shadow-md">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold text-slate-800">Branş rozetleri</h2>
        {rozetsiz.length > 0 && (
          <button
            type="button"
            onClick={() => setIlerlemeAcik((v) => !v)}
            className="text-xs font-medium text-brand-600 hover:underline"
            aria-expanded={ilerlemeAcik}
          >
            {ilerlemeAcik ? 'İlerlemeyi gizle' : `İlerleme (${rozetsiz.length})`}
          </button>
        )}
      </div>

      {gosterilecek.length > 0 ? (
        <ul className="flex flex-wrap gap-2">
          {gosterilecek.map((b) => {
            const s = BRANS_STILI[b.branch] ?? VARSAYILAN_STIL
            const sv = SEVIYE_STILI[b.level] ?? SEVIYE_STILI.Cirak
            return (
              <li key={`${b.branch}-${b.level}`}>
                <span
                  className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs
                              font-medium ring-inset ${s.bg} ${s.text} ${s.ring} ${sv.halka}`}
                  title={`${b.title} — ${b.hours} saat ders anlatımı`}
                >
                  <span aria-hidden="true">{s.ikon}</span>
                  {b.title}
                  <span aria-hidden="true" className="opacity-50">
                    {sv.isaret}
                  </span>
                </span>
              </li>
            )
          })}
        </ul>
      ) : (
        <p className="text-xs text-slate-500">
          {kendiProfilim
            ? 'İlk rozet 5 saat ders anlatımıyla geliyor.'
            : 'Henüz branş rozeti kazanılmamış.'}
        </p>
      )}

      {ilerlemeAcik && rozetsiz.length > 0 && (
        <ul className="mt-3 space-y-1.5 border-t border-slate-100 pt-3">
          {rozetsiz.map((p) => {
            const hedef = sonrakiEsik(p.hours)
            const oran = hedef ? Math.min(100, (p.hours / hedef) * 100) : 100
            return (
              <li key={p.branch} className="flex items-center gap-2 text-xs text-slate-500">
                <span className="w-20 shrink-0 truncate">{p.subject}</span>
                <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                  <div className="h-full rounded-full bg-brand-400" style={{ width: `${oran}%` }} />
                </div>
                <span className="w-20 shrink-0 text-right tabular-nums">
                  {p.hours} / {hedef ?? 50} saat
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
