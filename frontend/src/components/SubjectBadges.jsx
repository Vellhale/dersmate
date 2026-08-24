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

  METALİK TASARIMDAN VAZGEÇİLDİ (2026-08-24). Bir önceki sürüm gradyanlı, beyaz
  parlama şeritli, renkli gölgeli "metal" haplar taşıyordu; içinde branş emojisi
  (sayı tuşu, atom simgesi gibi), sonunda madalya emojisi (birincilik ve
  ikincilik madalyası) vardı. Sahibin geri bildirimiyle hepsi
  kaldırıldı: parlama efektleri bilgi değil gürültüydü ve emojiler platformdan
  platforma farklı çizildiği için rozetler her cihazda başka görünüyordu. Şimdi
  rozet sade bir beyaz hap: bu dosyada çizilen küçük bir madalya SVG'si + başlık
  metni, başka hiçbir süsleme yok. SVG her platformda aynı çizilir; kademe bilgisini
  tek başına madalyanın rengi (gümüş / altın) taşır.

  Branş bilgisi kaybolmuyor: başlıkta zaten dersin adı yazıyor ("Matematik Üstadı").
  ─────────────────────────────────────────────────────────────────────────────
*/

/*
  KADEMELER.

  Sıra numarası ayrıca tutuluyor (`sira`): aynı branşta iki rozet varsa yükseği
  seçmek için sayısal bir karşılaştırma gerekiyor ve enum adına göre alfabetik
  sıralamak tesadüfen doğru sonuç verip yarın sessizce bozulurdu.

  `disk` / `kenar`, MadalyaIkonu'ndaki SVG'nin dolgu ve kenar renkleri. Hex değil
  Tailwind sınıfı — kaynak-sabitleri süpürgesi hex'i yakalar, sınıf ise paletle
  birlikte yaşar.
*/
const KADEME = {
  Ogretici: {
    sira: 1,
    etiket: 'Gümüş',
    disk: 'fill-slate-300',
    kenar: 'stroke-slate-400',
  },
  Ustad: {
    sira: 2,
    etiket: 'Altın',
    disk: 'fill-amber-400',
    kenar: 'stroke-amber-500',
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
                    className="h-full rounded-full bg-slate-300"
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
 * Madalya ikonu: üstte iki kısa kurdele şeridi, altta disk — klasik madalya silueti.
 *
 * Emoji yerine SVG, çünkü emoji her platformda başka çiziliyordu ve rozetler cihazdan
 * cihaza farklı görünüyordu; bu ikon her yerde aynı piksellerle gelir. Kademeyi tek
 * başına diskin rengi anlatır; kurdele bilerek nötr slate — dikkat metalde kalsın.
 * Şeritler diskten önce çizilir ki uçları diskin arkasına saklansın.
 */
function MadalyaIkonu({ kademe }) {
  return (
    <svg viewBox="0 0 24 24" className="h-5 w-5 shrink-0" aria-hidden="true">
      <path d="M7 2h4l1.8 7.5-4 1z" className="fill-slate-400" />
      <path d="M13 2h4l-1.8 8.5-4-1z" className="fill-slate-400" />
      <circle cx="12" cy="15" r="6" strokeWidth="1.5" className={`${kademe.disk} ${kademe.kenar}`} />
    </svg>
  )
}

/**
 * Tek rozet: beyaz hap içinde madalya ikonu + sunucudan gelen başlık. Görsel dil
 * bilerek bu kadar: sahibin isteği "temiz, sade bir madalya ikonu ve yanında metin,
 * ekstra hiçbir logo veya süsleme" idi. Saat bilgisi ve kademe adı tooltip'te,
 * kademe adı ekran okuyucu için ayrıca sr-only etikette.
 */
function Madalya({ rozet }) {
  const k = KADEME[rozet.level] ?? VARSAYILAN_KADEME

  return (
    <span
      className="inline-flex items-center gap-2 rounded-full border border-slate-200
                  bg-white py-1.5 pl-2.5 pr-3.5 text-sm font-medium text-slate-800 shadow-sm"
      title={`${rozet.title} — ${rozet.hours} saat ders anlatımı (${k.etiket})`}
    >
      <MadalyaIkonu kademe={k} />
      <span>{rozet.title}</span>
      <span className="sr-only">({k.etiket} kademe)</span>
    </span>
  )
}
