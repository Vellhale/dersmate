import { useEffect, useMemo, useState } from 'react'
import { Loading, Modal } from './ui'

/*
  ADIM ADIM KONU SEÇİCİ — Sınav → Seviye → Ders → Konu.

  NEDEN DÜZ ARAMA YETMEDİ. Önceki hâl tek bir aranabilir listeydi: 767 konu, hepsi aynı
  düzlemde. Adını bilen kullanıcı için hızlıydı ama iki şeyi yapamıyordu:

  1. NE OLDUĞUNU GÖSTERMEK. "Türev" yazan biri sonucu buluyordu; ne aradığını bilmeyen
     biri listenin neyi kapsadığını hiç göremiyordu. Katalog gezilebilir değildi.
  2. MANTIKSIZ EŞLEŞMELERİ ÖNLEMEK. Arama kutusu "asit" yazınca Kimya konusunu da,
     Biyoloji konusunu da getiriyordu; kullanıcı Matematik anlatmak isterken yanlışlıkla
     bir Fizik konusu seçebiliyordu. Hiyerarşi bunu yapısal olarak imkânsız kılıyor:
     Matematik seçilmeden Matematik konuları görünmüyor, seçildikten sonra da BAŞKA
     dersin konusu listede yok.

  GEOMETRİ AYRI BİR DERS ve bu bir arayüz kararı değil, katalogun kendi yapısı
  (Curriculum.cs → SubjectBranch.Geometri). Seçici hiyerarşiyi katalogtan OKUYOR,
  kendi içinde bir kopya taşımıyor — bu kasıtlı: müfredat dosyası her yıl güncelleniyor
  ve arayüze gömülmüş ikinci bir liste, ilk güncellemede sessizce yanlış hâle gelirdi.
  Ayrıca konu kimlikleri sunucudan gelen GUID'ler; elle yazılmış bir JSON'daki adlar
  hiçbir kayda bağlanamazdı.

  SIRA HER ZAMAN AYNI, ama tek seçenekli bir basamak kendini atlamaz — bugün yalnızca
  YKS var ve kullanıcı yine de onu görüp seçiyor. Görünmeyen bir adım, ileride ikinci
  bir sınav eklendiğinde arayüzün birdenbire "değişmiş" gibi hissettirmesine yol açardı.
*/

/** Katalog satırlarını Sınav → Seviye → Ders → Konu ağacına çevirir. */
function agacKur(satirlar) {
  const agac = new Map()

  for (const satir of satirlar ?? []) {
    // Alan adları CatalogController.TopicRow ile birebir: rootCategory / category /
    // subject / topic. Biri değişirse ağaç sessizce boşalır, o yüzden tek yerde okunuyor.
    const sinav = satir.rootCategory ?? '—'
    const seviye = satir.category ?? '—'
    const ders = satir.subject ?? '—'

    if (!agac.has(sinav)) agac.set(sinav, new Map())
    const seviyeler = agac.get(sinav)

    if (!seviyeler.has(seviye)) seviyeler.set(seviye, new Map())
    const dersler = seviyeler.get(seviye)

    if (!dersler.has(ders)) dersler.set(ders, [])
    dersler.get(ders).push(satir)
  }

  return agac
}

/*
  Tek bir seçenek düğmesi. Kart biçiminde çünkü seçenekler parmakla basılıyor: 44px'lik
  dokunma hedefi (min-h-11) lg altında zorunlu, üstünde de zarar vermiyor.

  active:bg-brand-100 dokunmatik için: hover masaüstü lüksü, telefonda yok. Basma
  anında bir kademe koyulaşan zemin "dokunuşun algılandı" der; bu seçicide her seçim
  yeni bir basamak açtığı için o anlık geri bildirim olmadan geçiş "takıldı mı?"
  hissi veriyordu.
*/
function Secenek({ children, alt, onClick, secili = false }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={secili}
      className={`flex min-h-11 w-full items-center justify-between gap-3 rounded-lg border px-3.5 py-2.5 text-left text-sm transition ${
        secili
          ? 'border-brand-400 bg-brand-50 font-semibold text-brand-800 active:bg-brand-100'
          : 'border-slate-200/80 bg-white text-slate-800 hover:border-brand-300 hover:bg-brand-50/50 active:border-brand-400 active:bg-brand-100'
      }`}
    >
      <span className="min-w-0">
        <span className="block truncate">{children}</span>
        {alt && <span className="mt-0.5 block text-xs font-normal text-slate-500">{alt}</span>}
      </span>
      <span className="shrink-0 text-slate-400" aria-hidden="true">
        ›
      </span>
    </button>
  )
}

/*
  Kırıntı yolu (breadcrumb) aynı zamanda GERİ DÖNÜŞ YOLU: her basamak tıklanabilir.
  Ayrı bir "geri" düğmesi tek adım geri alır; kullanıcı üç adım sonra dersi değiştirmek
  istediğinde üç kez basması gerekirdi. Kırıntıya tıklamak doğrudan o basamağa döner.
*/
function Kirinti({ adimlar, onGit }) {
  if (adimlar.length === 0) return null

  return (
    <nav className="mb-3 flex flex-wrap items-center gap-1 text-xs" aria-label="Seçim yolu">
      {adimlar.map((ad, i) => (
        <span key={`${ad}-${i}`} className="flex items-center gap-1">
          {i > 0 && (
            <span className="text-slate-300" aria-hidden="true">
              ›
            </span>
          )}
          <button
            type="button"
            onClick={() => onGit(i)}
            className="rounded px-1.5 py-1 font-medium text-brand-700 transition hover:bg-brand-50"
          >
            {ad}
          </button>
        </span>
      ))}
    </nav>
  )
}

/**
 * @param {boolean} open
 * @param {() => void} onClose
 * @param {(konu: object) => void} onSelect  seçilen katalog satırını döndürür
 * @param {object[]} konular  api.topics() sonucu
 * @param {boolean} yukleniyor
 */
export function KonuSecici({ open, onClose, onSelect, konular, yukleniyor = false, baslik = 'Konu seç' }) {
  const [sinav, setSinav] = useState(null)
  const [seviye, setSeviye] = useState(null)
  const [ders, setDers] = useState(null)
  const [arama, setArama] = useState('')

  /*
    Modal her açıldığında seçim SIFIRLANIR. Kalıcı bırakmak "geçen sefer Matematik
    seçmiştim" kolaylığı gibi görünüyor ama kullanıcı ikinci konuyu genellikle BAŞKA
    bir dersten ekliyor; açılışta yarı dolu bir yol, seçilmiş sanılan bir dersle yanlış
    konu eklenmesine yol açıyordu.
  */
  useEffect(() => {
    if (open) {
      setSinav(null)
      setSeviye(null)
      setDers(null)
      setArama('')
    }
  }, [open])

  const agac = useMemo(() => agacKur(konular), [konular])

  const seviyeler = sinav ? agac.get(sinav) : null
  const dersler = seviye && seviyeler ? seviyeler.get(seviye) : null
  const konuListesi = ders && dersler ? (dersler.get(ders) ?? []) : []

  const suzulmus = useMemo(() => {
    const q = arama.trim().toLocaleLowerCase('tr')
    if (!q) return konuListesi
    return konuListesi.filter((k) => k.topic.toLocaleLowerCase('tr').includes(q))
  }, [konuListesi, arama])

  const kirintilar = [sinav, seviye, ders].filter(Boolean)

  const kirintiyaGit = (i) => {
    // i = tıklanan basamağın indeksi; o basamaktan SONRAKİ seçimler düşer.
    if (i === 0) {
      setSeviye(null)
      setDers(null)
    } else if (i === 1) {
      setDers(null)
    }
    setArama('')
  }

  const adimBasligi = !sinav
    ? 'Hangi sınav?'
    : !seviye
      ? 'TYT mi, AYT mi?'
      : !ders
        ? 'Hangi ders?'
        : 'Hangi konu?'

  return (
    <Modal open={open} onClose={onClose} title={baslik} genis>
      {yukleniyor ? (
        <Loading label="Katalog yükleniyor…" />
      ) : (
        <div>
          <Kirinti adimlar={kirintilar} onGit={kirintiyaGit} />

          <p className="mb-2 text-sm font-semibold text-slate-800">{adimBasligi}</p>

          {/* 1. SINAV */}
          {!sinav && (
            <div className="grid gap-2 sm:grid-cols-2">
              {[...agac.keys()].map((ad) => (
                <Secenek key={ad} onClick={() => setSinav(ad)} alt={`${agac.get(ad).size} seviye`}>
                  {ad}
                </Secenek>
              ))}
            </div>
          )}

          {/* 2. SEVİYE (TYT / AYT) */}
          {sinav && !seviye && (
            <div className="grid gap-2 sm:grid-cols-2">
              {[...(seviyeler?.keys() ?? [])].map((ad) => (
                <Secenek key={ad} onClick={() => setSeviye(ad)} alt={`${seviyeler.get(ad).size} ders`}>
                  {ad}
                </Secenek>
              ))}
            </div>
          )}

          {/* 3. DERS — Geometri burada Matematik'ten AYRI bir satır olarak çıkar. */}
          {seviye && !ders && (
            <div className="grid gap-2 sm:grid-cols-2">
              {[...(dersler?.keys() ?? [])].map((ad) => (
                <Secenek key={ad} onClick={() => setDers(ad)} alt={`${dersler.get(ad).length} konu`}>
                  {ad}
                </Secenek>
              ))}
            </div>
          )}

          {/* 4. KONU */}
          {ders && (
            <div className="space-y-2">
              {/*
                Arama kutusu YALNIZCA son basamakta. Üst basamaklarda en fazla sekiz
                seçenek var, arama oraya gürültü olurdu; tek bir derste ise 40+ konu
                olabiliyor ve adını bilen kullanıcının listeyi taraması gerekmemeli.
              */}
              <input
                className="input"
                value={arama}
                onChange={(e) => setArama(e.target.value)}
                placeholder={`${ders} içinde ara…`}
                aria-label={`${ders} konularında ara`}
              />

              {suzulmus.length === 0 ? (
                <p className="rounded-lg border border-dashed border-slate-300 px-4 py-6 text-center text-sm text-slate-500">
                  Bu aramayla eşleşen konu yok.
                </p>
              ) : (
                // max-h + overflow: uzun ders listeleri modalı ekran dışına taşırmasın;
                // dvh çünkü mobil adres çubuğu vh'ye dahil değil.
                <div className="max-h-[45dvh] space-y-1.5 overflow-y-auto pr-1">
                  {suzulmus.map((k) => (
                    <Secenek key={k.topicId} onClick={() => onSelect(k)}>
                      {k.topic}
                    </Secenek>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
