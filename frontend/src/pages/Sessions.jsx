import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../lib/api'
import { dersSekmesi, eylemBekliyor, rezervasyonBirlesimi } from '../lib/dersDurumu'
import { useAsync } from '../state/useAsync'
import { useWallet } from '../state/WalletContext'
import { AnalyticsEvents, trackEvent } from '../lib/analytics'
import { ReviewModal } from '../components/ReviewModal'
import { PersonLink } from '../components/PersonLink'
import { Avatar } from '../components/Avatar'
import { CamKart } from '../components/SayfaZemini'
import {
  ArtanIkonu,
  ArtiIkonu,
  KepIkonu,
  SaatIkonu,
  TakvimIkonu,
  UyariIkonu,
} from '../components/Ikonlar'
import {
  REPORT_REASON_LABELS,
  SESSION_STATUS_LABELS,
  TRANSACTION_LABELS,
  formatDateTime,
  remainingText,
  signedCredit,
} from '../lib/format'
/*
  Card ARTIK İMPORT EDİLMİYOR. Bu sayfadaki her yüzey CamKart'a geçti (ders kartı, puan
  geçmişi kapağı ve defteri). Kullanılmayan import'u bırakmak zararsız görünür ama bu
  projede iki kez yanıltıcı oldu: "burada da Card var" sanılıp yeni bir yüzey eski dille
  yazıldı. ui.jsx'teki Card kaldırılmadı, DEĞİŞTİRİLMEDİ de — sadece bu sayfa onu
  kullanmıyor.
*/
import { Badge, Button, EmptyState, ErrorBox, Field, Loading, Modal, Notice, Pagination } from '../components/ui'

/*
  DURUM → TON TABLOSU. TEK KAYNAK.

  Neden tablo, neden koşullu sınıf değil: durum başına DÖRT ayrı görsel karar var (sol
  şerit rengi, takvim yaprağının başlık tonu, rozet tonu, vurgu metni rengi) ve bunlar
  kartın dört ayrı yerinde kullanılıyor. JSX'in içine serpiştirilmiş koşullar olsaydı yeni
  bir durum eklendiğinde dört yerin hepsini bulmak gerekirdi; biri unutulduğunda hata
  sessiz olurdu — kart yanlış renkte çizilir, hiçbir şey patlamaz.

  RENK TEK SİNYAL DEĞİL: şeridin yanında her zaman METİNLİ bir rozet duruyor
  (SESSION_STATUS_LABELS). Renk körü bir kullanıcı için şerit süs, rozet bilgidir.

  Ton seçimleri:
    brand   → süreç işliyor, tarih ileride (Rezerve)
    amber   → TOPUN SENDE olabileceği bekleme hâli (Onay bekliyor)
    emerald → iyi biten iş (Tamamlandı)
    rose    → sorunlu ya da yarıda kesilmiş iş (İtirazlı, İptal edildi)
    slate   → kapanmış, kimseden aksiyon beklemeyen kayıt (Süresi doldu)

  İptal rose ailesinde ama BİR TON AÇIK (rose-300): iptal olumsuz bir sonuç, fakat
  itirazın aksine artık çözülmesi gereken bir mesele değil. Aynı kırmızı tonu vermek,
  kapanmış bir dersi hâlâ ilgi bekleyen bir uyarı gibi gösterirdi.

  ─── ŞERİT ARTIK KENARLIK DEĞİL, KATMAN ───────────────────────────────────────
  Eskiden `border-l-4` idi ve Card'ın kendi kenarlığını sol tarafta eziyordu: kartın dört
  kenarından biri diğer üçünden dört kat kalın çıkıyor, köşeler de kare kalıyordu. Kart
  yüzeyi CamKart'a geçince bu daha da göze battı — cam kenarın yumuşaklığı sol tarafta
  bıçak gibi kesiliyordu. Şerit artık kartın İÇİNDE, üstten ve alttan içeri çekilmiş,
  uçları yuvarlatılmış 4px'lik bir çubuk: aynı tarama sinyalini veriyor, kenarlık dilini
  bozmuyor. Bu yüzden değerler `border-l-*` değil `bg-*`.

  TAKVİM TONU rozetle AYNI aileden (bg-*-100 / text-*-700, bkz. ui.jsx BADGE_TONES): kartın
  solundaki tarih yaprağı ile sağındaki rozet aynı rengi taşıyınca göz ikisini tek bir
  durum ifadesi olarak okuyor. Farklı tonlar seçilseydi tek kartta iki ayrı renk sistemi
  olurdu.
*/
const DURUM_STILI = {
  Booked: { serit: 'bg-brand-500', takvim: 'bg-brand-100 text-brand-700', rozet: 'brand', vurgu: 'text-brand-700' },
  AwaitingApproval: { serit: 'bg-amber-400', takvim: 'bg-amber-100 text-amber-800', rozet: 'warning', vurgu: 'text-amber-700' },
  Completed: { serit: 'bg-emerald-500', takvim: 'bg-emerald-100 text-emerald-700', rozet: 'success', vurgu: 'text-emerald-700' },
  Disputed: { serit: 'bg-rose-500', takvim: 'bg-rose-100 text-rose-700', rozet: 'danger', vurgu: 'text-rose-700' },
  Cancelled: { serit: 'bg-rose-300', takvim: 'bg-rose-100 text-rose-700', rozet: 'danger', vurgu: 'text-rose-700' },
  Expired: { serit: 'bg-slate-300', takvim: 'bg-slate-100 text-slate-700', rozet: 'neutral', vurgu: 'text-slate-600' },
}

// Sunucu tanımadığımız bir durum döndürürse kart RENKSİZ değil NÖTR çizilir: eksik bir
// eşleşme yüzünden `undefined` sınıf adı basıp kartın şeridini tamamen kaybetmeyelim.
const VARSAYILAN_DURUM_STILI = {
  serit: 'bg-slate-300',
  takvim: 'bg-slate-100 text-slate-700',
  rozet: 'neutral',
  vurgu: 'text-slate-600',
}

/*
  ─────────────────────────────────────────────────────────────────────────────
  BEŞ SEKME (2026-09-26, kullanıcı kararı; adlar kullanıcının, değiştirilmeden).

  2026-08-24'ten bu yana sayfa ekrana sabit İKİ SÜTUNDU (solda yaklaşan, sağda geçmiş +
  puan geçmişi; her sütun kendi içinde kayıyordu). Gerekçesi "yarın dersim var mı" ile
  "geçen ay ne yapmıştım" sorularının aynı kaydırma çubuğunu paylaşmamasıydı. Sekmeler o
  ayrımı daha keskin yapıyor: her soru kendi sekmesinde, yani sabit yükseklik, iç kaydırma
  ve onların tuzakları (min-h-0, altbilgi payı, dvh hesabı) gereksiz kaldı. Sayfa artık
  normal kayar; içerik `max-w-3xl` tek sütun (tam genişlikte kart lg'de aşırı yayılıyordu).
  Karar kaydı: docs/ASAMA-3-FRONTEND.md §3.

  Sekme → içerik:
    aksiyon     senden iş bekleyen dersler + "İtirazda, karar yönetimde"
    planlanmis  "Saati geçti, hâlâ açık" + "Yaklaşan"
    gecmis      YALNIZCA tamamlananlar, sunucu süzgeciyle (?pastStatus=Completed),
                numaralı sayfa
    puan        puan defteri (/wallet/statement), birikerek
    rezerve     iki rolde bütün rezervasyonlar, sonucu ne olursa olsun, birikerek
  Bir dersin hangi sekmede durduğu lib/dersDurumu.js'te (mobille BAYT BAYT aynı dosya):
  iki istemci aynı dersi aynı sekmede, aynı sayaçla gösteriyor.

  AYNI SAYFADA İKİ SAYFALAMA DESENİ, BİLİNÇLİ: Geçmiş dersler NUMARALI (sayfa değişir,
  birikmez — web kararı; süzgeçle sayfa sınırları kesin), Puan ve Rezerve BİRİKİR. Rezerve
  numaralanamaz: tam gelen aktif liste sayfalı geçmişin arasına serpiştiği için n. sayfanın
  sınırı önceki sayfalar yüklenmeden bilinemez (bkz. rezervasyonBirlesimi).

  SEKME ADRESTE TUTULUR (?sekme=, replace ile — geri tuşu sekme sekme gezmesin). Web'de
  adres görünür: yenileme ve paylaşılan bağlantı sekmeyi korumalı. Mobil ise aynı
  parametreyi bir KOMUT olarak okuyup adresten siliyor; bilinçli fark.
  ─────────────────────────────────────────────────────────────────────────────
*/
const SEKMELER = [
  { anahtar: 'aksiyon', ad: 'Senden aksiyon bekleyenler' },
  { anahtar: 'planlanmis', ad: 'Planlanmış' },
  { anahtar: 'gecmis', ad: 'Geçmiş dersler' },
  { anahtar: 'puan', ad: 'Puan geçmişi' },
  { anahtar: 'rezerve', ad: 'Rezerve geçmişi' },
]
const SEKME_ANAHTARLARI = new Set(SEKMELER.map((s) => s.anahtar))

/*
  Geçmiş dersler sayfası 5 kart: numaralı sayfa, liste her sayfada aynı boyda kalsın
  (ders kartı büyük; 20'lik sayfa üç ekran boyu uzuyordu).
  Rezerve satırı kompakt bir defter satırı, o yüzden "daha eski" başına 10. İlk sayfası
  sayfa açılışındaki TEK istekle geliyor (aktif liste zaten o yanıtta), ayrı istek yok.
*/
const GECMIS_SAYFA_BOYU = 5
const REZERVE_SAYFA_BOYU = 10
const HISTORY_PAGE_SIZE = 20

const GECMIS_BOS = { sayfa: null, yukleniyor: false, hata: null }

export default function Sessions() {
  const sessions = useAsync(() => api.mySessions(1, REZERVE_SAYFA_BOYU), [])
  const matches = useAsync(() => api.myMatches(), [])
  const { wallet, refreshWallet } = useWallet()
  const [notice, setNotice] = useState(null)
  const [bookOpen, setBookOpen] = useState(false)
  const [dialog, setDialog] = useState(null) // { type: 'complete'|'approve'|'report'|'cancel', session }

  /*
    Time-Lock ve otomatik onay geri sayımları canlı aksın diye periyodik yeniden çizim.

    `tick` OKUNUYOR çünkü aşağıdaki gruplama saate bakıyor: yalnızca yeniden render
    yetmez, useMemo'nun bağımlılığına girmezse önbellekteki eski gruplama döner ve saati
    dolan ders ekranda "Yaklaşan" olarak asılı kalır. Sayfa açık dururken de doğru tarafa
    geçmesi (eğitmenin bitişi geçen dersi aksiyon sekmesine geçer) bu bağımlılıkla oluyor.
  */
  const [tick, setTick] = useState(0)
  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 20000)
    return () => clearInterval(id)
  }, [])

  // ── SEKME ────────────────────────────────────────────────────────────────
  const [adres, setAdres] = useSearchParams()
  const adrestekiSekme = adres.get('sekme')

  /*
    VARSAYILAN SEKME mobille aynı kural: adresteki sekme > (ilk veride) senden iş bekleyen
    ders varsa "aksiyon", yoksa "planlanmis". İlk veride BİR KEZ karar verilir ve sabit
    kalır: son işi bitiren kullanıcı sekmesinden atılmasın. İlk yanıta kadar hiçbir sekme
    seçili değil (yanlış sekmeyi bir an gösterip sıçramak yerine).

    Render sırasında state yazımı bilinçli (React'in "önceki render'dan türeyen state"
    kalıbı): efektte yazılsaydı boyanmış bir kare sekmesiz çizilirdi.
  */
  const [ilkSekme, setIlkSekme] = useState(null)
  if (ilkSekme === null && sessions.data) {
    const simdi = Date.now()
    setIlkSekme(sessions.data.active.some((s) => eylemBekliyor(s, simdi)) ? 'aksiyon' : 'planlanmis')
  }

  // Bilinmeyen değer (eski bağlantı, elle yazım) aksiyon sekmesini açar — mobille aynı.
  const sekme = adrestekiSekme
    ? SEKME_ANAHTARLARI.has(adrestekiSekme)
      ? adrestekiSekme
      : 'aksiyon'
    : ilkSekme

  const sekmeSec = useCallback(
    (yeni) =>
      setAdres(
        (onceki) => {
          const p = new URLSearchParams(onceki)
          p.set('sekme', yeni)
          return p
        },
        { replace: true },
      ),
    [setAdres],
  )

  const groups = useMemo(() => {
    /*
      ─────────────────────────────────────────────────────────────────────────
      SAATİ GEÇMİŞ DERS "YAKLAŞAN"DA KALAMAZ (2026-08-24, hata düzeltmesi).

      Sunucu aktif/geçmiş ayrımını YALNIZCA DURUMA göre yapıyor: Booked,
      AwaitingApproval ve Disputed "aktif" sayılıyor (GetMySessions.cs). Saat hiç
      hesaba katılmıyor — çünkü sunucu açısından doğru olan bu: saati geçmiş ama
      tamamlanmamış bir ders hâlâ AÇIK bir kayıt, kapanmış değil.

      Ama arayüzde "Yaklaşan" kelimesi bir SÖZ veriyor: bu ders henüz olmadı. Ölçüldü —
      bugün 15:10'da, sabah 08:10'da bitmiş bir ders hâlâ "Yaklaşan dersler" listesinde
      duruyordu. Planlanmış sekmesi bu yüzden ikiye ayrılıyor: "Saati geçti, hâlâ açık"
      ve "Yaklaşan". KIYAS BİTİŞ SAATİYLE, başlangıçla değil: 60 dakikalık bir ders
      başladı diye geçmiş olmuyor.

      Hangi dersin hangi sekmede durduğu dersDurumu.js'ten (dersSekmesi). 2026-09-26'ya
      kadar burada kendi tanımı vardı (`canComplete || canApprove || Disputed`) ve mobilden
      FARKLIYDI: itiraz sayaca giriyor, eğitmenin bitişi geçmiş Booked dersi girmiyordu.
      Artık itiraz sayaca girmez (basılacak düğmesi yok, karar yönetimde) ama aksiyon
      sekmesinde kendi başlığıyla durur; bitişi geçen Booked ders, sunucu bayrağı henüz
      güncellenmemiş olsa da aksiyona geçer (kartın iyimser completeReady'siyle aynı an).
      ─────────────────────────────────────────────────────────────────────────
    */
    const active = sessions.data?.active ?? []
    const simdi = Date.now()
    const saatiGecti = (s) => new Date(s.scheduledEndUtc).getTime() <= simdi
    const planli = active.filter((s) => dersSekmesi(s, simdi) === 'planlanmis')

    return {
      action: active.filter((s) => eylemBekliyor(s, simdi)),
      itirazda: active.filter((s) => s.status === 'Disputed'),
      upcoming: planli.filter((s) => !saatiGecti(s)),
      gecmisAcik: planli.filter(saatiGecti),
    }
    // tick: 20 saniyede bir yeniden hesapla — saat ilerledikçe ders kendiliğinden doğru
    // gruba geçsin, sayfa yenilenmesini beklemesin.
  }, [sessions.data, tick])

  // ── GEÇMİŞ DERSLER: yalnızca Completed, numaralı sayfa, ilk açılışta yüklenir ──
  /*
    Ayrı istek (?pastStatus=Completed): sayı ve sayfa sınırları ancak sunucu süzerse
    doğru. İstemci süzmesiyle 5'lik sayfadan 0-5 kart çıkar ve sekmedeki sayı iptal /
    süresi dolmuş dersleri de sayardı.

    NESİL SAYACI: refresh() geçmişi 1. sayfaya döndürürken uçuştaki eski bir sayfa isteği
    sonradan dönüp yeni durumu EZMESİN diye. Uçuş kilidi ref'te: `disabled` ancak bir
    sonraki render'da DOM'a yansır, arka arkaya iki tıklama araya render girmeden gelebilir
    ve yavaş dönen istek kullanıcıyı tıklamadığı sayfaya götürürdü.
  */
  const [gecmis, setGecmis] = useState(GECMIS_BOS)
  const gecmisNesli = useRef(0)
  const gecmisUcuyor = useRef(false)

  const gecmisSayfasi = useCallback(async (hedef) => {
    if (gecmisUcuyor.current) return
    gecmisUcuyor.current = true
    const nesil = gecmisNesli.current
    // Hata AYRI tutulur ve görünen sayfa silinmez: sayfa yükleme hatası, zaten görünen
    // listeyi götürmemeli.
    setGecmis((g) => ({ ...g, yukleniyor: true, hata: null }))
    try {
      const data = await api.mySessions(hedef, GECMIS_SAYFA_BOYU, 'Completed')
      if (nesil === gecmisNesli.current) setGecmis({ sayfa: data.past, yukleniyor: false, hata: null })
    } catch (err) {
      if (nesil === gecmisNesli.current) setGecmis((g) => ({ ...g, yukleniyor: false, hata: err }))
    } finally {
      if (nesil === gecmisNesli.current) gecmisUcuyor.current = false
    }
  }, [])

  useEffect(() => {
    if (sekme === 'gecmis' && !gecmis.sayfa && !gecmis.yukleniyor && !gecmis.hata) gecmisSayfasi(1)
  }, [sekme, gecmis, gecmisSayfasi])

  // ── PUAN GEÇMİŞİ: ilk açılışta yüklenir; satırlar sekme değişince KAYBOLMAZ ──
  const puan = usePuanDefteri()
  const { yukle: puanYukle } = puan
  useEffect(() => {
    if (sekme === 'puan' && puan.sayfa === 0 && !puan.yukleniyor && !puan.hata) puanYukle(1)
  }, [sekme, puan.sayfa, puan.yukleniyor, puan.hata, puanYukle])

  // ── REZERVE GEÇMİŞİ: aktif + süzülmemiş geçmiş, ders tarihine göre, birikerek ──
  /*
    İlk geçmiş sayfası açılış isteğinden geliyor; "daha eski" ile gelen sayfalar
    `rezerveEk`te birikiyor ve HANGİ YANITA eklendiklerini (`kaynak`) taşıyor. Ders listesi
    yeniden çekilince (refresh) kaynak değişir ve birikinti kendiliğinden düşer: eski
    ofsetler yeni listede geçersiz, üstüne eklemek kayıtları çiftler ya da atlar.
  */
  const [rezerveEk, setRezerveEk] = useState(null) // { kaynak, items, past }
  const [rezerveYukleniyor, setRezerveYukleniyor] = useState(false)
  const [rezerveHata, setRezerveHata] = useState(null)
  const rezerveUcuyor = useRef(false)

  const rezerve = useMemo(() => {
    const data = sessions.data
    if (!data) return { liste: [], toplam: 0, sonGecmis: null }
    const ek = rezerveEk?.kaynak === data ? rezerveEk : null
    const sonGecmis = ek?.past ?? data.past
    const birikinti = ek ? [...data.past.items, ...ek.items] : data.past.items
    return {
      liste: rezervasyonBirlesimi(data.active, birikinti, !!sonGecmis.hasNextPage),
      toplam: data.activeTotal + sonGecmis.totalCount,
      sonGecmis,
      ek,
    }
  }, [sessions.data, rezerveEk])

  async function dahaEskiRezervasyonlar() {
    const { sonGecmis, ek } = rezerve
    if (rezerveUcuyor.current || !sonGecmis?.hasNextPage) return
    rezerveUcuyor.current = true
    const kaynak = sessions.data
    setRezerveYukleniyor(true)
    setRezerveHata(null)
    try {
      const data = await api.mySessions(sonGecmis.page + 1, REZERVE_SAYFA_BOYU)
      // Yol üstünde liste yeniden çekildiyse bu kayıt eski kaynağa bağlı kalır ve
      // kendiliğinden yok sayılır (yukarıdaki `kaynak` karşılaştırması).
      setRezerveEk({ kaynak, items: [...(ek?.items ?? []), ...data.past.items], past: data.past })
    } catch (err) {
      setRezerveHata(err)
    } finally {
      rezerveUcuyor.current = false
      setRezerveYukleniyor(false)
    }
  }

  function refresh(message) {
    setDialog(null)
    setBookOpen(false)
    if (message) setNotice(message)

    // Geçmiş 1. sayfaya döner: onaylanan ders aktiften çıkıp geçmişin BAŞINA girer,
    // kullanıcı 4. sayfada kalsaydı az önce onayladığı dersi göremezdi. Sekme açık değilse
    // bir sonraki açılışta yeniden çekilir.
    gecmisNesli.current += 1
    gecmisUcuyor.current = false
    setGecmis(GECMIS_BOS)
    // Puan defteri de bayatlar (karşı tarafın onayı, otomatik onay); aynı kural.
    puan.sifirla()
    // Rezerve birikintisi ders listesiyle birlikte düşüyor (kaynak değişiyor).
    setRezerveHata(null)
    sessions.reload()
    // Onay puan basar; profildeki puan sayacı ve başlıktaki seviye rozeti aynı cüzdan
    // ucundan besleniyor, o yüzden tazeleniyor.
    refreshWallet()
  }

  function gecmisSayfayaGit(hedef) {
    const sayfa = gecmis.sayfa
    const sayfaSayisi = Math.ceil((sayfa?.totalCount ?? 0) / GECMIS_SAYFA_BOYU)
    if (hedef < 1 || hedef > sayfaSayisi || hedef === (sayfa?.page ?? 1)) return
    gecmisSayfasi(hedef)
  }

  const kart = (s, past = false) => (
    <SessionCard key={s.sessionId} session={s} onAction={setDialog} past={past} />
  )
  const rezerveEt = () => setBookOpen(true)

  // Aktif listeye dayanan sekmeler ilk yüklemede ve refresh'te spinner'a düşer; ders
  // listesi hiç gelmediyse hata kutusu. Gelmiş listenin tazelemesi düşerse eski liste
  // hata kutusunun altında kalır (useAsync veriyi tutuyor).
  const aktifeBagli = (icerik) =>
    sessions.loading ? (
      <Loading />
    ) : (
      <>
        <ErrorBox error={sessions.error} onRetry={sessions.reload} />
        {sessions.data && icerik()}
      </>
    )

  let panel = null
  if (sekme === 'aksiyon') {
    panel = aktifeBagli(() => (
      <AksiyonPaneli
        groups={groups}
        kesme={<KesmeUyarisi data={sessions.data} />}
        kart={kart}
        onPlanlanmis={() => sekmeSec('planlanmis')}
      />
    ))
  } else if (sekme === 'planlanmis') {
    panel = aktifeBagli(() => (
      <PlanlanmisPaneli
        groups={groups}
        kesme={<KesmeUyarisi data={sessions.data} />}
        kart={kart}
        onRezerve={rezerveEt}
      />
    ))
  } else if (sekme === 'gecmis') {
    panel = (
      <GecmisPaneli
        durum={gecmis}
        kart={kart}
        onSayfa={gecmisSayfayaGit}
        onTekrar={() => gecmisSayfasi(gecmis.sayfa?.page ?? 1)}
      />
    )
  } else if (sekme === 'puan') {
    panel = <PuanPaneli defter={puan} toplamPuan={wallet?.totalEarnedCredits} />
  } else if (sekme === 'rezerve') {
    panel = aktifeBagli(() => (
      <RezervePaneli
        rezerve={rezerve}
        kesme={<KesmeUyarisi data={sessions.data} />}
        yukleniyor={rezerveYukleniyor}
        hata={rezerveHata}
        onDaha={dahaEskiRezervasyonlar}
        onRezerve={rezerveEt}
      />
    ))
  }

  return (
    <div className="max-w-3xl space-y-5">
      {/*
        SAYFA BAŞLIĞI. Eski açıklama cümlesi ("Ders almak ücretsizdir…") Puan geçmişi
        sekmesinin girişine taşındı (mobille aynı): puanın nereden geldiğini anlatan cümle
        puan defterinin başında okunuyor, her sekmenin üstünde tekrar etmiyor.
      */}
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-3">
        <h1 className="text-2xl font-bold tracking-tight text-slate-900">Derslerim</h1>
        <Button onClick={rezerveEt}>
          <ArtiIkonu className="h-4 w-4" />
          Ders rezerve et
        </Button>
      </div>

      {notice && (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      )}

      <SekmeCubugu secili={sekme} onSec={sekmeSec} aksiyonSayisi={groups.action.length} />

      {sekme ? (
        /*
          Yalnızca etkin panel kurulur: Geçmiş ve Puan ilk açılışta yüklenir, açılmayan
          sekmenin maliyeti yok. tabIndex=0: panelin ilk öğesi odaklanabilir değil
          (başlık ya da metin), klavye kullanıcısı sekmeden panele Tab ile geçebilmeli.
        */
        <div
          role="tabpanel"
          id={`panel-${sekme}`}
          aria-labelledby={`sekme-${sekme}`}
          tabIndex={0}
          className="space-y-4 rounded-2xl focus-visible:outline focus-visible:outline-2
                     focus-visible:outline-offset-4 focus-visible:outline-brand-500"
        >
          {panel}
        </div>
      ) : sessions.loading ? (
        <Loading />
      ) : (
        <ErrorBox error={sessions.error} onRetry={sessions.reload} />
      )}

      {/*
        Modallar KOŞULLU render edilir ve session id ile key'lenir.
        Koşulsuz render edilselerdi bileşen state'i (seçilen dosya, yazılan kod) kapatınca
        sıfırlanmaz ve BİR SONRAKİ derse taşınırdı — yanlış derse yanlış kanıt yüklenebilirdi.
      */}
      {bookOpen && (
        <BookModal
          matches={matches.data?.active ?? []}
          onClose={() => setBookOpen(false)}
          onBooked={(code, mintAmount) => {
            refresh(
              // Öğrenci hiçbir şey ödemiyor; gösterilen sayı EĞİTMENİN kazanacağı puan
              // ve SUNUCUDAN geliyor. (Modalin özet şeridi aynı sayının yalnızca bir
              // ÖNİZLEMESİNİ gösterir — bkz. BookModal'daki gösterim sabitleri;
              // bağlayıcı olan her zaman buradaki mintAmount'tur.)
              `Ders rezerve edildi (eğitmen ${mintAmount} puan kazanacak). ` +
                `Doğrulama kodun: ${code} — ders ekran görüntüsünde görünmeli.`,
            )
            // Yeni ders Planlanmış'ta: kullanıcı az önce kurduğu dersi görsün.
            sekmeSec('planlanmis')
          }}
        />
      )}

      {dialog?.type === 'complete' && (
        <CompleteModal
          key={dialog.session.sessionId}
          session={dialog.session}
          onClose={() => setDialog(null)}
          onDone={() =>
            refresh(
              'Kanıt yüklendi. Ders karşı tarafın onayına gönderildi; ' +
                'Planlanmış sekmesinden takip edebilirsin.',
            )
          }
        />
      )}

      {dialog?.type === 'approve' && (
        <ApproveModal
          key={dialog.session.sessionId}
          session={dialog.session}
          onClose={() => setDialog(null)}
          onApproved={(credits, session) => {
            // Tek metin: her ders puan basıyor, "puan yazılmadı" diyen bir dal yok.
            // Yazılan sayı SUNUCUDAN gelen değerdir (istemci yeniden hesaplamaz).
            refresh(`Ders onaylandı. Eğitmene ${credits} puan yazıldı.`)
            // Değerlendirme, onayın hemen ardından açılır: kural gereği yorum ancak
            // tamamlanmış bir dersin çıktısı olabilir ve bu an tam olarak o an.
            setDialog({ type: 'review', session })
          }}
          onReport={() => setDialog({ type: 'report', session: dialog.session })}
          onDispute={() => setDialog({ type: 'dispute', session: dialog.session })}
        />
      )}

      {dialog?.type === 'review' && (
        <ReviewModal
          open
          session={dialog.session}
          onClose={() => setDialog(null)}
          onSubmitted={() => {
            setDialog(null)
            refresh('Değerlendirmen kaydedildi. Teşekkürler!')
          }}
        />
      )}

      {dialog?.type === 'report' && (
        <ReportModal
          key={dialog.session.sessionId}
          session={dialog.session}
          onClose={() => setDialog(null)}
          onDone={() => refresh('Şikayetin yönetime iletildi. Karşı tarafa bildirilmez.')}
        />
      )}

      {dialog?.type === 'dispute' && (
        <DisputeModal
          key={dialog.session.sessionId}
          session={dialog.session}
          onClose={() => setDialog(null)}
          onDone={() =>
            refresh(
              'İtirazın yönetime iletildi. Karar verilene kadar puan yazılmayacak; ' +
                'sonucu bu sekmedeki İtirazda bölümünden takip edebilirsin.',
            )
          }
        />
      )}

      {dialog?.type === 'cancel' && (
        <CancelModal
          key={dialog.session.sessionId}
          session={dialog.session}
          onClose={() => setDialog(null)}
          onDone={() => refresh('Ders iptal edildi. Kaydı Rezerve geçmişi sekmesinde duruyor.')}
        />
      )}
    </div>
  )
}

/*
  ── SEKME ÇUBUĞU ─────────────────────────────────────────────────────────────
  Hap dizisi, Keşfet'teki gri ray DEĞİL: beş sekme, en uzunu ("Senden aksiyon
  bekleyenler") tek başına dar ekranın yarısı. Ray içinde beşi ya taşar ya ezilirdi; ayrı
  haplar lg altında yatay kayar, lg'de sarar. Mavi aile (seçili dolu brand-600, seçili
  olmayan brand-50 + halka) mobildeki hap çubuğuyla aynı dil.

  KLAVYE: roving tabindex (Tab sekme çubuğuna TEK durakla girer), ←/→ döngüsel, Home/End
  uçlara. Ok tuşu seçimi de değiştirir (otomatik etkinleştirme): paneller hemen çiziliyor,
  Geçmiş ve Puan'ın ilk yüklemesi spinner'la geliyor, beklenecek bir şey yok.

  SEÇİLİ HAP GÖRÜNÜR TUTULUR: sekme programatik değiştiğinde (rezervasyondan sonra
  Planlanmış, boş aksiyondan "Planlanmış derslere bak", adresteki ?sekme=rezerve) dar
  ekranda hap çubuğun dışında kalabiliyordu. `scrollIntoView` KULLANILMADI: dikey ekseni
  de kaydırıyor ve çubuk ekranın üstünde değilken sayfayı yukarı zıplatırdı; burada
  yalnızca çubuğun kendi yatay kaydırması ayarlanıyor.

  Çubuk `-mx-4 px-4` ile ekran kenarına kadar uzanır: kayan haplar kesik görünür, "daha
  var" ipucu bu. sm'de `-mx-1 px-1`: odak halkası (2px + 2px pay) kaydırma kabının
  kenarında kırpılmasın.
*/
function SekmeCubugu({ secili, onSec, aksiyonSayisi }) {
  const kapRef = useRef(null)
  const haplar = useRef({})

  useEffect(() => {
    const kap = kapRef.current
    const hap = secili ? haplar.current[secili] : null
    if (!kap || !hap || kap.scrollWidth <= kap.clientWidth) return
    const pay = 16
    const sol = hap.offsetLeft
    const sag = sol + hap.offsetWidth
    if (sol - pay < kap.scrollLeft) kap.scrollLeft = Math.max(0, sol - pay)
    else if (sag + pay > kap.scrollLeft + kap.clientWidth) kap.scrollLeft = sag + pay - kap.clientWidth
    // aksiyonSayisi: sayaç rozeti ilk veriyle gelince aksiyon hapı genişliyor ve arkasındaki
    // haplar sağa kayıyor. Yalnızca seçime bağlı kalsaydı adresten açılan "Rezerve geçmişi"
    // hapı, rozet geldikten sonra 32px kesik kalıyordu (375px'te ölçüldü).
  }, [secili, aksiyonSayisi])

  function tus(e, i) {
    const n = SEKMELER.length
    let hedef = null
    if (e.key === 'ArrowRight') hedef = (i + 1) % n
    else if (e.key === 'ArrowLeft') hedef = (i - 1 + n) % n
    else if (e.key === 'Home') hedef = 0
    else if (e.key === 'End') hedef = n - 1
    if (hedef === null) return
    e.preventDefault()
    const anahtar = SEKMELER[hedef].anahtar
    onSec(anahtar)
    haplar.current[anahtar]?.focus()
  }

  return (
    <div
      ref={kapRef}
      role="tablist"
      aria-label="Derslerim bölümleri"
      className="kaydirma-ince relative -mx-4 flex gap-2 overflow-x-auto px-4 py-1 sm:-mx-1 sm:px-1
                 lg:flex-wrap lg:overflow-visible"
    >
      {SEKMELER.map(({ anahtar, ad }, i) => {
        const bu = secili === anahtar
        // Hiçbir sekme seçili değilken (ilk yanıt gelmeden) Tab ilk hapa inebilmeli.
        const durak = secili ? bu : i === 0
        const sayac = anahtar === 'aksiyon' && aksiyonSayisi > 0 ? aksiyonSayisi : 0
        return (
          <button
            key={anahtar}
            ref={(el) => {
              haplar.current[anahtar] = el
            }}
            type="button"
            role="tab"
            id={`sekme-${anahtar}`}
            aria-selected={bu}
            aria-controls={bu ? `panel-${anahtar}` : undefined}
            // Sayı rozeti görsel; ekran okuyucu sayıyı cümleyle duyar.
            aria-label={sayac ? `${ad}, ${sayac} ders` : undefined}
            tabIndex={durak ? 0 : -1}
            onClick={() => onSec(anahtar)}
            onKeyDown={(e) => tus(e, i)}
            className={`inline-flex min-h-11 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full px-4
                        text-sm font-medium transition lg:min-h-9 focus-visible:outline focus-visible:outline-2
                        focus-visible:outline-offset-2 focus-visible:outline-brand-500 ${
                          bu
                            ? 'bg-brand-600 text-white shadow-sm hover:bg-brand-700'
                            : 'bg-brand-50 text-brand-800 ring-1 ring-inset ring-brand-200 hover:bg-brand-100'
                        }`}
          >
            {ad}
            {/*
              Web'in "burada iş var" dili: amber Badge (eski sol sütunun vurgulu sayacı).
              Mobildeki rose SayacRozeti buraya taşınmadı; web'de rose hata ve iptal dili.
            */}
            {sayac > 0 && (
              <Badge tone="warning" className="font-semibold tabular-nums">
                {sayac}
              </Badge>
            )}
          </button>
        )
      })}
    </div>
  )
}

/**
 * Panel içindeki gruplama başlığı — etiket + sayaç.
 *
 * Mikro etiket dili (küçük punto + büyük harf) yalnızca burada; web'de `uppercase` doğru
 * (lang="tr", "i" → "İ"). Mobil aynı başlığı UstEtiket ile yazıyor.
 *
 * SAYI AYRI ROZETTE, metnin içinde değil: parantezli sayı ("Planlanmış (3)") etiketin bir
 * parçası gibi okunuyordu. Yanındaki ince çizgi (flex-1 + h-px) grubu görsel olarak da
 * açıyor: etiketten sonra devam eden hat, altındaki kartların bu başlığa ait olduğunu
 * söylüyor.
 */
function AltBaslik({ children, sayi, tone = 'slate' }) {
  const amber = tone === 'amber'

  return (
    <div className="flex items-center gap-2.5 pt-1">
      <h2
        className={`shrink-0 text-xs font-semibold uppercase tracking-wider ${
          amber ? 'text-amber-700' : 'text-slate-600'
        }`}
      >
        {children}
      </h2>
      {sayi !== undefined && (
        <Badge tone={amber ? 'warning' : 'neutral'} className="shrink-0 font-semibold tabular-nums">
          {sayi}
        </Badge>
      )}
      <span aria-hidden="true" className="h-px flex-1 bg-slate-200" />
    </div>
  )
}

/**
 * Sekme boşken görünen kutu.
 *
 * ui.jsx'teki EmptyState KULLANILMADI: açıklaması slate-500 ve bu kutu yarı saydam
 * zeminde (bg-white/60) duruyor; altından sayfa zemini geçerken slate-500 AA'ya
 * dayanmıyordu. Ayrım renkten değil ağırlıktan (font-semibold / normal).
 */
function BosPanel({ baslik, metin, eylem = null }) {
  return (
    <div className="rounded-2xl border border-dashed border-slate-300 bg-white/60 px-5 py-10 text-center">
      <p className="text-sm font-semibold text-slate-700">{baslik}</p>
      <p className="mx-auto mt-1.5 max-w-sm text-sm text-slate-600">{metin}</p>
      {eylem && <div className="mt-4 flex justify-center">{eylem}</div>}
    </div>
  )
}

/**
 * Aktif liste 100'de kesilir (GetMySessions MaxActive). Kesme SESSİZ olmaz: kullanıcı
 * listenin tamamını görmediğini bilmeli. Aktif listeye dayanan her sekmede (aksiyon,
 * planlanmış, rezerve) gösterilir.
 */
function KesmeUyarisi({ data }) {
  if (!data || data.activeTotal <= data.active.length) return null
  return (
    <Notice tone="warning">
      {data.activeTotal} aktif dersinden ilk {data.active.length} tanesi gösteriliyor. Listeyi
      kısaltmak için tamamlanan dersleri onayla.
    </Notice>
  )
}

function AksiyonPaneli({ groups, kesme, kart, onPlanlanmis }) {
  const planliVar = groups.upcoming.length + groups.gecmisAcik.length > 0

  if (groups.action.length === 0 && groups.itirazda.length === 0) {
    return (
      <>
        {kesme}
        <BosPanel
          baslik="Şu an senden beklenen bir iş yok"
          metin="Kanıt yüklemen ya da onay vermen gereken dersler burada görünür."
          eylem={
            planliVar ? (
              <Button variant="secondary" onClick={onPlanlanmis}>
                Planlanmış derslere bak
              </Button>
            ) : null
          }
        />
      </>
    )
  }

  return (
    <>
      {kesme}
      {groups.action.length > 0 ? (
        groups.action.map((s) => kart(s))
      ) : (
        <p className="text-sm text-slate-600">Şu an senden beklenen bir iş yok.</p>
      )}

      {/*
        İTİRAZDAKİLER ayrı başlıkta ve SAYACA GİRMEZ: karar yönetimde, kullanıcının
        basabileceği düğme yok (dersDurumu.js). Yine de bu sekmede: açılmış itiraz
        kullanıcının gündeminde, "Planlanmış" bir ders değil.
      */}
      {groups.itirazda.length > 0 && (
        <>
          <AltBaslik sayi={groups.itirazda.length}>İtirazda, karar yönetimde</AltBaslik>
          {groups.itirazda.map((s) => kart(s))}
        </>
      )}
    </>
  )
}

function PlanlanmisPaneli({ groups, kesme, kart, onRezerve }) {
  if (groups.upcoming.length + groups.gecmisAcik.length === 0) {
    return (
      <>
        {kesme}
        <BosPanel
          baslik="Planlanmış dersin yok"
          metin="Bir arkadaşınla ders saati belirleyerek başla."
          eylem={<Button onClick={onRezerve}>Ders rezerve et</Button>}
        />
      </>
    )
  }

  return (
    <>
      {kesme}
      {/*
        SAATİ GEÇMİŞ AMA AÇIK DERSLER ÜSTTE, amber başlıkla: "bu ders oldu mu, ne oldu?"
        sorusu ilk bakışta görünsün. Durum rozeti hâlâ "Rezerve" diyor, yani kaydın
        kapanmadığı görünmeye devam ediyor.
      */}
      {groups.gecmisAcik.length > 0 && (
        <>
          <AltBaslik tone="amber" sayi={groups.gecmisAcik.length}>
            Saati geçti, hâlâ açık
          </AltBaslik>
          {groups.gecmisAcik.map((s) => kart(s))}
        </>
      )}

      {groups.upcoming.length > 0 && (
        <>
          <AltBaslik sayi={groups.upcoming.length}>Yaklaşan</AltBaslik>
          {groups.upcoming.map((s) => kart(s))}
        </>
      )}
    </>
  )
}

function GecmisPaneli({ durum, kart, onSayfa, onTekrar }) {
  const sayfa = durum.sayfa
  const basRef = useRef(null)
  const oncekiNo = useRef(null)

  /*
    SAYFA DEĞİŞİNCE LİSTENİN BAŞINA. Numaralı düğmeler listenin ALTINDA; iki sütunlu eski
    düzende liste kendi kutusunda kaydığı için sorun yoktu, sayfa artık bütün olarak
    kaydığından 2'ye basan kullanıcı yeni sayfanın SONUNDA kalıyordu (800px'te görüldü).
    Yalnızca kullanıcı sayfa değiştirince (ilk yükleme ve refresh değil) ve başlık yapışkan
    üst çubuğun (4rem) altında kaldıysa kaydırılır; scroll-mt-20 çubuğun payı.
  */
  const no = sayfa?.page ?? null
  useEffect(() => {
    const el = basRef.current
    if (no && oncekiNo.current && no !== oncekiNo.current && el && el.getBoundingClientRect().top < 64) {
      el.scrollIntoView({ block: 'start' })
    }
    oncekiNo.current = no
  }, [no])

  if (!sayfa) return durum.hata ? <ErrorBox error={durum.hata} onRetry={onTekrar} /> : <Loading />

  /*
    İstemci de süzer: sunucu parametreyi tanımıyorsa (pastStatus'tan önceki sürüm) yok
    sayar ve iptal / süresi dolmuş dersleri de döndürür. O durumda sayı ve sayfa sınırları
    süzülmemiş kümeye ait, yani YANLIŞ: sayı gösterilmez. Sunucu önce dağıtılır; bu yalnızca
    geçiş anının güvencesi.
  */
  const tamamlananlar = sayfa.items.filter((s) => s.status === 'Completed')
  const suzgecTutmadi = tamamlananlar.length !== sayfa.items.length
  const sayfaSayisi = Math.ceil(sayfa.totalCount / GECMIS_SAYFA_BOYU)

  if (sayfa.totalCount === 0) {
    return (
      <BosPanel
        baslik="Henüz tamamlanan dersin yok"
        metin="Onaylanan dersler burada birikir. İptal edilen ya da süresi dolan dersler Rezerve geçmişi sekmesinde."
      />
    )
  }

  return (
    <>
      <div ref={basRef} className="scroll-mt-20">
        <AltBaslik sayi={suzgecTutmadi ? undefined : sayfa.totalCount}>Tamamlanan dersler</AltBaslik>
      </div>
      {tamamlananlar.map((s) => kart(s, true))}
      <ErrorBox error={durum.hata} onRetry={onTekrar} />
      <Pagination page={sayfa.page} totalPages={sayfaSayisi} onChange={onSayfa} disabled={durum.yukleniyor} />
    </>
  )
}

/**
 * Puan defteri (eski Cüzdan ekranının defteri) — durum Sessions'ta tutulur.
 *
 * Eskiden katlanır bir bölümdü ve kendi state'ini taşıyordu; artık kendi sekmesi var ve
 * yalnızca etkin panel kuruluyor. State bileşende kalsaydı her sekme değişiminde
 * yüklenen sayfalar silinir, dönüşte baştan istenirdi.
 */
function usePuanDefteri() {
  const [durum, setDurum] = useState({ satirlar: [], toplam: 0, sayfa: 0, yukleniyor: false, hata: null })
  const ucuyor = useRef(false)
  const nesil = useRef(0)

  const yukle = useCallback(async (hedef) => {
    // "Daha eski hareketler" ekleyerek çalışıyor: çift tıklama AYNI sayfayı iki kez
    // eklerdi ve defterde her satır çift görünürdü. Düğmenin `loading` kilidi bir
    // sonraki render'a kadar geçerli olmadığı için muhafız burada, ref'te.
    if (ucuyor.current) return
    ucuyor.current = true
    const bu = nesil.current
    setDurum((d) => ({ ...d, yukleniyor: true, hata: null }))
    try {
      const sonuc = await api.statement(hedef, HISTORY_PAGE_SIZE)
      if (bu !== nesil.current) return
      // Sayfa EKLENİR, değiştirilmez: "daha eski" akışında önceki satırlar kaybolmamalı.
      setDurum((d) => ({
        satirlar: hedef === 1 ? sonuc.items : [...d.satirlar, ...sonuc.items],
        toplam: sonuc.totalCount,
        sayfa: hedef,
        yukleniyor: false,
        hata: null,
      }))
    } catch (err) {
      if (bu === nesil.current) setDurum((d) => ({ ...d, yukleniyor: false, hata: err }))
    } finally {
      if (bu === nesil.current) ucuyor.current = false
    }
  }, [])

  const sifirla = useCallback(() => {
    nesil.current += 1
    ucuyor.current = false
    setDurum({ satirlar: [], toplam: 0, sayfa: 0, yukleniyor: false, hata: null })
  }, [])

  return { ...durum, yukle, sifirla }
}

function PuanPaneli({ defter, toplamPuan }) {
  const { satirlar, toplam, sayfa, yukleniyor, hata, yukle } = defter

  return (
    <>
      {/* Puanın İKİ kaynağı var: onaylanan dersin anlatanı ve Topluluk'ta net oy eşiğini
          geçen katkı (CommunityReward). 2026-09-27'ye kadar bu cümle "her hareketin hangi
          dersten geldiği" diyordu; listedeki Topluluk ve hoş geldin satırları dersten
          gelmiyor. Mobil app/dersler.jsx → PuanPaneli ile aynı metin. */}
      <p className="max-w-prose text-sm text-slate-600">
        Ders almak ücretsizdir. Puan, onaylanan dersin anlatanına ve Topluluk’ta oy toplayan
        katkılara yazılır; her hareketin kaynağı burada.
      </p>
      {/* Cüzdan bağlamından, ek istek yok (başlıktaki seviye rozetiyle aynı sayı). */}
      {Number.isInteger(toplamPuan) && (
        <p className="text-sm text-slate-700">
          Toplam puanın: <strong className="font-semibold tabular-nums text-slate-900">{toplamPuan}</strong>
        </p>
      )}

      <ErrorBox error={hata} onRetry={() => yukle(sayfa + 1)} />

      {sayfa === 0 ? (
        !hata && <Loading />
      ) : satirlar.length === 0 ? (
        <CamKart>
          <p className="text-sm text-slate-600">
            Henüz puan hareketin yok. Bir ders anlatıp onaylandığında ya da Topluluk katkın
            puan kazandığında ilk kaydın burada belirir.
          </p>
        </CamKart>
      ) : (
        /*
          TEK kart, İNCE AYRAÇLAR: defter tek bir belgedir, kart koleksiyonu değil.
          divide-y satırları ayırıyor, kart kenarı belgeyi sarıyor; "daha eski" düğmesi de
          belgenin altbilgisi gibi içeride duruyor ki defter uzadıkça düğme ondan kopmasın.
        */
        <CamKart className="overflow-hidden !p-0">
          <div className="divide-y divide-slate-200/70">
            {satirlar.map((row, i) => (
              <HistoryRow key={`${row.createdAtUtc}-${i}`} row={row} />
            ))}
          </div>

          {satirlar.length < toplam && (
            <div className="flex justify-center border-t border-slate-200/70 px-5 py-3.5">
              <Button variant="secondary" loading={yukleniyor} onClick={() => yukle(sayfa + 1)}>
                Daha eski hareketler ({satirlar.length}/{toplam})
              </Button>
            </div>
          )}
        </CamKart>
      )}
    </>
  )
}

function RezervePaneli({ rezerve, kesme, yukleniyor, hata, onDaha, onRezerve }) {
  const { liste, toplam, sonGecmis } = rezerve
  const dahaVar = !!sonGecmis?.hasNextPage

  return (
    <>
      <p className="max-w-prose text-sm text-slate-600">
        Rezerve ettiğin ve sana rezerve edilen bütün dersler, sonucuyla birlikte.
      </p>
      {kesme}

      {liste.length === 0 ? (
        <BosPanel
          baslik="Henüz rezervasyon yok"
          metin="Rezerve ettiğin ya da sana rezerve edilen her ders, sonucu ne olursa olsun burada listelenir."
          eylem={<Button onClick={onRezerve}>Ders rezerve et</Button>}
        />
      ) : (
        <>
          <AltBaslik sayi={toplam}>Tüm rezervasyonlar</AltBaslik>
          {/* Defter dili (Puan geçmişiyle aynı): tek kart, satırlar divide-y. */}
          <CamKart className="overflow-hidden !p-0">
            <ul className="divide-y divide-slate-200/70">
              {liste.map((s) => (
                <li key={s.sessionId}>
                  <RezervasyonSatiri session={s} />
                </li>
              ))}
            </ul>

            {/*
              Düğme `dahaVar`a bağlı, x < y'ye değil: aktif liste 100'de kesildiyse x
              sona gelindiğinde de y'den küçük kalır ve düğme boşa basılırdı (kesme
              uyarısı zaten yukarıda).
            */}
            {dahaVar && (
              <div className="flex justify-center border-t border-slate-200/70 px-5 py-3.5">
                <Button variant="secondary" loading={yukleniyor} onClick={onDaha}>
                  Daha eski rezervasyonlar ({liste.length}/{toplam})
                </Button>
              </div>
            )}
          </CamKart>
          <ErrorBox error={hata} onRetry={onDaha} />
        </>
      )}
    </>
  )
}

/*
  REZERVASYON SATIRI — kayıt, eylem değil. Düğme taşımaz: bir dersin yapılacak işi
  aksiyon ve planlanmış sekmelerindeki kartta. Burada "ne oldu" okunur: konu, kişi ve
  rol, süre ve puan, sonuç (durum rozeti, ders kartıyla aynı tonlar) ve ders tarihi.

  Ad profile gider (PersonLink, "ad görünen her yerde profile gidilir"). lg altında
  bağlantı 44px dokunma hedefi: `-my-3.5 py-3.5` görünümü değiştirmeden yüksekliği
  büyütüyor (Topluluk kartındaki ad bağlantısının kalıbı; orada text-sm'in 20px satırına
  -my-3 yetiyor, burada text-xs'in 16px satırı 14px pay istiyor — 375px'te 44 ölçüldü).
*/
function RezervasyonSatiri({ session }) {
  const stil = DURUM_STILI[session.status] ?? VARSAYILAN_DURUM_STILI

  return (
    <div className="flex items-start justify-between gap-3 px-5 py-3.5">
      <div className="min-w-0 space-y-0.5">
        <p className="truncate text-sm font-medium text-slate-900">{session.topicName}</p>
        <div className="flex min-w-0 items-center gap-1.5 text-xs text-slate-600">
          <PersonLink
            userId={session.otherUserId}
            className="-my-3.5 min-w-0 truncate py-3.5 font-medium text-slate-700 hover:text-brand-700 lg:my-0 lg:py-0"
          >
            {session.otherDisplayName}
          </PersonLink>
          <Ayrac />
          <span className="shrink-0">{session.iAmTutor ? 'Anlatıyorum' : 'Alıyorum'}</span>
        </div>
        <p className="flex items-center gap-1.5 text-xs tabular-nums text-slate-600">
          <span>{session.durationMinutes} dk</span>
          <Ayrac />
          <span>{session.mintAmount} puan</span>
        </p>
      </div>

      <div className="flex shrink-0 flex-col items-end gap-1.5 text-right">
        <Badge tone={stil.rozet}>{SESSION_STATUS_LABELS[session.status] ?? session.status}</Badge>
        <time dateTime={session.scheduledStartUtc} className="text-xs tabular-nums text-slate-600">
          {formatDateTime(session.scheduledStartUtc)}
        </time>
      </div>
    </div>
  )
}

function HistoryRow({ row }) {
  const kazanc = row.amount > 0

  return (
    /*
      Satırın kendi kart kabuğu YOK (eski rounded-lg + border kaldırıldı): satırlar artık
      tek kartın içinde divide-y ile ayrılıyor. Kenarlık ve köşe yuvarlama defterin
      sınırında bir kez çiziliyor — satır başına tekrarlamak görsel gürültüden ibaretti.

      TONLAR YÜKSELDİ (slate-500 → slate-600, slate-400 → slate-600): defter artık cam bir
      yüzeyin üstünde ve arkasından sayfa zemini geçiyor. En açık iki ton o bileşimde AA
      eşiğine dayanmıyordu; ayrım artık renk yerine punto ile yapılıyor (text-xs).
    */
    <div className="flex items-center justify-between gap-3 px-5 py-3">
      <div className="min-w-0">
        <p className="truncate text-sm font-medium text-slate-900">
          {TRANSACTION_LABELS[row.type] ?? row.type}
        </p>
        {/* Konu ve karşı taraf sunucudan geliyor: çıplak bir ders kimliği kullanıcıya
            hiçbir şey anlatmıyor. Ders bilgisi yoksa (Topluluk katkısı, hoş geldin puanı,
            süresi dolan puan) satır yalnızca türüyle kalır: kaynağı etiket zaten söylüyor
            (TRANSACTION_LABELS). 2026-09-27'ye kadar bu satır yedek olarak tarihi yazıyordu
            ve aynı tarih sağ sütunda ikinci kez görünüyordu. */}
        {row.topicName && (
          <p className="truncate text-xs text-slate-600">
            {row.topicName}
            {row.counterpartDisplayName ? ` · ${row.counterpartDisplayName}` : ''}
          </p>
        )}
      </div>

      <div className="shrink-0 text-right">
        <div
          className={`text-sm font-semibold tabular-nums ${
            kazanc ? 'text-emerald-700' : 'text-slate-600'
          }`}
        >
          {signedCredit(row.amount)}
        </div>
        <div className="text-xs tabular-nums text-slate-600">{formatDateTime(row.createdAtUtc)}</div>
      </div>
    </div>
  )
}

/**
 * Meta şeridindeki nokta ayracı. slate-300: ayraç mobilyadır, içerik değil — içerikle
 * aynı tonda olduğunda satır "kelime kelime kelime" diye tek blok okunuyordu, soluk
 * ayraç parçaları birbirinden koparıyor. Tek bileşen olması tutarlılık için: kartta kaç
 * ayraç varsa hepsi aynı karakter ve aynı tonda.
 *
 * NOT — kontrast kuralı bu öğeyi KAPSAMIYOR: aria-hidden, yani ekran okuyucuya hiç
 * ulaşmıyor ve gören kullanıcı için de okunacak bir içerik değil. Kural gövde METNİ için;
 * ayracı slate-600'e çıkarmak onu kelimelerle eşit ağırlığa getirip işini bozardı.
 */
function Ayrac() {
  return (
    <span aria-hidden="true" className="text-slate-300">
      ·
    </span>
  )
}

/*
  UYARI SATIRI — Time-Lock ve otomatik onay TEK görsel dilde.

  İkisi de aynı türden bilgi: "bir sayaç işliyor ve sonunda bir şey olacak". Eskiden ikisi
  de çıplak amber metindi ve kartın diğer satırlarına karışıyordu; hafif amber zemin
  satırı gövdeden ayırıyor ama Notice kadar bağırmıyor — bu bir hata değil, takvim
  bilgisi. Renk yine tek sinyal değil: saat ikonu ve metin bilgiyi kendisi taşıyor.

  KUM SAATİ EMOJİSİ (⏳) ÇIKTI, SaatIkonu GİRDİ. Emoji satırın rengini almıyordu (her
  platformda kendi renginde bir görsel), punto ile ölçeklenmiyordu ve Windows/Android/iOS
  üçlüsünde üç ayrı çizim gösteriyordu. Aynı bilgi artık currentColor kullanan satır içi
  SVG ile veriliyor — amber-800 metnin tonunu birebir alıyor.

  mt-0.5 ikonun üstünde: 24'lük ızgaradaki bir ikon, yanındaki 12px'lik metnin ilk
  satırıyla optik olarak hizalanmıyor; yarım adım aşağı alınca daire metnin x-yüksekliğine
  oturuyor.
*/
function UyariSatiri({ children }) {
  return (
    <p className="flex items-start gap-2 rounded-xl bg-amber-50 px-3.5 py-2.5 text-xs leading-relaxed text-amber-800">
      <SaatIkonu className="mt-0.5 h-4 w-4 shrink-0" />
      <span>{children}</span>
    </p>
  )
}

/*
  ── TAKVİM YAPRAĞI ───────────────────────────────────────────────────────────
  Kartın solunda SABİT GENİŞLİKTE tarih bloğu: ay kısaltması üstte, gün sayısı büyük,
  saat altta.

  NEDEN: eski kartta tarih, meta şeridinin içinde "26 Ara 14:30 · 60 dk · 100 puan" diye
  akan bir metin parçasıydı — yani listedeki en çok aranan bilgi (ne zaman?), en soluk
  satırın ilk kelimesiydi. Blok hâline gelince iki iş birden görülüyor: tarih tek bakışta
  okunuyor ve kartlar alt alta dizildiğinde sol kenarda bir ZAMAN ÇİZELGESİ oluşuyor —
  ayrı bir timeline bileşeni çizmeye gerek kalmadan.

  SABİT GENİŞLİK ŞART (w-16): değişken genişlik, kartların metin sütununu farklı yerden
  başlatır ve liste "her kart biraz kaymış" gibi okunur. 64px, iki basamaklı gün sayısını
  text-2xl'de ve "14:30"u text-xs'te taşıyor.

  BİÇİMLENDİRİCİLER MODÜL SEVİYESİNDE: Intl.DateTimeFormat kurulumu pahalı ve bu bileşen
  listede kart başına bir kez çiziliyor. format.js'e KONMADILAR çünkü orası uygulamanın
  ortak biçim sözlüğü; bu üçü tek bir görsel bileşenin parçalarına ait — ortak sözlüğe
  girseler "nerede kullanılıyor" sorusu cevapsız kalırdı.
*/
const AY_KISALTMASI = new Intl.DateTimeFormat('tr-TR', { month: 'short' })
const GUN_SAYISI = new Intl.DateTimeFormat('tr-TR', { day: 'numeric' })
const SAAT_DAKIKA = new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' })

function TarihBlogu({ utcString, tonSinifi }) {
  const tarih = new Date(utcString)

  /*
    Geçersiz tarihe karşı muhafız: Intl.format geçersiz Date'te RangeError FIRLATIR ve tek
    bozuk kayıt bütün listeyi beyaz ekrana düşürürdü. Sunucu bugüne kadar hep geçerli ISO
    gönderdi; buradaki koruma "gönderdiğine güven ama düşürme" ilkesinin bedeli.
  */
  const gecerli = !Number.isNaN(tarih.getTime())

  return (
    /*
      self-start ŞART: kapsayıcı bir flex satırı ve varsayılan `align-items: stretch`,
      yaprağı kartın TAM YÜKSEKLİĞİNE çekiyordu — tarayıcıda görüldü. Uzun bir kartta
      (uyarı satırı + kod çipi olan) blok, altındaki üç satır boyunca uzayan boş beyaz bir
      şerite dönüşüyordu; takvim yaprağı hissi tam olarak bu yüzden kayboluyordu.
    */
    <div
      className="flex w-16 shrink-0 flex-col self-start overflow-hidden rounded-xl border border-slate-200/80
                 bg-white text-center shadow-sm"
    >
      {/* Ay şeridi durum tonunu taşıyor — sağdaki durum rozetiyle aynı renk ailesi. */}
      <span className={`py-1 text-xs font-semibold uppercase tracking-wide ${tonSinifi}`}>
        {gecerli ? AY_KISALTMASI.format(tarih) : '—'}
      </span>
      <span className="pt-2 text-2xl font-bold leading-none tabular-nums text-slate-900">
        {gecerli ? GUN_SAYISI.format(tarih) : '—'}
      </span>
      <span className="px-1 pb-2 pt-1.5 text-xs font-medium tabular-nums text-slate-600">
        {gecerli ? SAAT_DAKIKA.format(tarih) : '—'}
      </span>
    </div>
  )
}

/*
  ── DERS KARTI ───────────────────────────────────────────────────────────────
  Üç bölgeli sabit iskelet. Eski kart "beyaz bir etiket yığını" gibi okunuyordu: konu,
  iki rozet, dört meta parçası, kod satırı, uyarı ve düğmeler hepsi aynı görsel ağırlıkta
  alt alta dizilmişti — göz nereye bakacağını her kartta yeniden aramak zorundaydı.

  Yeni iskelet üç soruyu üç ayrı bölgeye ayırıyor ve sıra her kartta AYNI:

    1. NE ZAMAN  → solda sabit genişlikte takvim yaprağı (TarihBlogu)
    2. NE / KİMLE → sağdaki sütun: konu başlığı, branş, eğitmen profili, meta
    3. NE YAPMALIYIM → alt şeritte, ince bir ayraçtan sonra, sağa yaslı düğmeler

  Aksiyonların KENDİ ŞERİDİNDE olması "birbirine girmesin" isteğinin karşılığı: eskiden
  düğmeler içeriğin akışına ekleniyordu ve kartın yüksekliği değiştikçe farklı yerlerde
  bitiyordu. Şimdi kart ne kadar uzarsa uzasın düğmeler kartın alt kenarına yapışık —
  art arda üç kartta göz aynı noktayı arar.

  DOLGU: gövde p-5, alt şerit px-5 py-3.5. Kartın kendi dolgusu `!p-0` ile sıfırlanıyor
  çünkü alt şerit kenardan kenara bir ayraç çizgisi taşıyor; ortak dolgu içinde kalsaydı
  o çizgi iki yanda boşluk bırakır, "kesilmiş çizgi" gibi görünürdü. `!` gerekli: Tailwind
  padding sınıflarını değere göre sıralıyor, yani `p-0` stil sayfasında `p-5`ten ÖNCE
  geliyor ve önem işareti olmadan kaybediyor.
*/
function SessionCard({ session, onAction, past = false }) {
  const startsIn = remainingText(session.scheduledStartUtc)
  const endsIn = remainingText(session.scheduledEndUtc)
  const autoApproveIn = remainingText(session.autoApproveDeadlineUtc)

  // Sunucu bayrağı kaynak-of-truth; ders bitişi geçtiyse iyimser davranıp butonu açarız
  // (sunucu yine doğrular — kullanıcı "neden hâlâ kapalı?" diye takılmasın).
  const completeReady =
    session.canComplete || (session.iAmTutor && session.status === 'Booked' && !endsIn)

  const showCode = !past && (session.status === 'Booked' || session.status === 'AwaitingApproval')

  const stil = DURUM_STILI[session.status] ?? VARSAYILAN_DURUM_STILI

  return (
    <CamKart className="relative overflow-hidden !p-0 transition-shadow duration-200 hover:shadow-md">
      {/*
        DURUM ŞERİDİ. Kenarlık değil, kartın içinde duran ince bir çubuk (bkz. DURUM_STILI
        başındaki not): üstten ve alttan içeri çekilmiş, sağ ucu yuvarlatılmış 4px. Eski
        border-l-4 cam kartın yumuşak sol kenarını düz kesiyordu.

        Şerit BİLGİ TAŞIMIYOR, hızlandırıyor: aynı durum her kartta metinli rozetle de
        yazılı. aria-hidden bu yüzden — ekran okuyucuya rengi anlatmanın anlamı yok, rozet
        zaten okunuyor.
      */}
      <span aria-hidden="true" className={`absolute inset-y-4 left-0 w-1 rounded-r-full ${stil.serit}`} />

      <div className="flex gap-4 p-5">
        <TarihBlogu utcString={session.scheduledStartUtc} tonSinifi={stil.takvim} />

        <div className="min-w-0 flex-1 space-y-3">
          {/* ── BAŞLIK SATIRI: konu solda, durum rozeti sağda ──────────────── */}
          <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-1.5">
            <div className="min-w-0 grow basis-40">
              {/*
                MOBİLDE KIRPMA DEĞİL SARMA (2026-08-24).

                `truncate` her boyutta tek satıra zorluyordu. Telefonda sütun 272px'e
                düşüyor ve kartın EN ÖNEMLİ bilgisi ortadan kesiliyordu: "Geometrik
                Kavramlar (Nokta, Doğr…" — parantez açılıp kapanmadığı için hangi konu
                olduğu okunmuyordu bile.

                `line-clamp-2` iki satıra izin verir, üçüncüde yine üç nokta koyar; yani
                kart yüksekliği en uzun konu adında bile kontrolden çıkmaz. sm üstünde
                sütun genişliyor ve tek satır zaten yetiyor — orada tekrar tek satıra iniyor,
                liste ritmi bozulmasın diye.

                sm'de `truncate` DEĞİL `line-clamp-1`: truncate `white-space:nowrap` yazıyor,
                line-clamp ise `display:-webkit-box` — ikisi üst üste binince tek satır iki
                farklı mekanizmayla kurulur ve davranış tarayıcıya kalır. Aynı mekanizmanın
                iki değeri, tanımı belirsizliğe bırakmıyor.

                `grow basis-40` (flex-1 DEĞİL): flex-1 temel genişliği 0 yapar ve dar
                ekranda rozet konu adını birkaç karaktere kadar ezerdi. 10rem'lik bir
                taban, yer kalmadığında rozeti ALT SATIRA itiyor — başlık okunur kalıyor.
              */}
              <h3 className="line-clamp-2 text-base font-semibold leading-snug text-slate-900 sm:line-clamp-1">
                {session.topicName}
              </h3>
              {/*
                Branş konunun ALTINA indi. Eskiden "… ile · Matematik" diye eğitmenin
                adına yapışıktı ve kişiye ait bir bilgi gibi okunuyordu; oysa branş konunun
                üst kategorisi — yerinin de konunun altı olması gerekiyordu.
              */}
              <p className="mt-0.5 truncate text-sm text-slate-600">{session.subjectName}</p>
            </div>

            <Badge tone={stil.rozet} className="shrink-0">
              {SESSION_STATUS_LABELS[session.status] ?? session.status}
            </Badge>
          </div>

          {/*
            ── EĞİTMEN PROFİLİ ─────────────────────────────────────────────────
            Karşı taraf artık bir metin parçası değil, YÜZÜ olan bir satır: avatar + ada
            tıklanabilir bağlantı + rol rozeti. Eski kartta ad, meta cümlesinin içinde
            geçen bir kelimeydi ("Ayşe ile · Matematik") ve profile giden bağlantı olduğu
            fark edilmiyordu.

            Avatar size="sm" (h-8): kartın en büyük öğesi tarih bloğu olmalı, kişi değil.
            md (h-12) denendi ve takvim yaprağıyla boy yarışına giriyordu.

            İSTEK ÜCRETSİZ: Avatar görselleri api.js'te userId bazında önbellekleniyor
            (avatarCache), yani aynı kişi listede kaç kez geçerse geçsin tek indirme.

            Rol rozeti bilinçli olarak NÖTR. Renk bu kartta tek bir şeyi anlatıyor: dersin
            durumunu. Rol de renkliyse iki ayrı anlam aynı sinyali paylaşır ve
            "yeşil = tamamlandı" öğrenilemez hâle gelirdi.
          */}
          <div className="flex flex-wrap items-center gap-x-2.5 gap-y-2 border-t border-slate-200/70 pt-3">
            <Avatar userId={session.otherUserId} name={session.otherDisplayName} size="sm" />
            {/*
              `grow basis-24` + flex-wrap (düz `truncate` DEĞİL): tarayıcıda ölçüldü —
              telefonda kartın metin sütunu 223px'e düşüyor ve "Anlatıyorum" rozeti tek
              başına ~90px alıyordu; ada kalan 81px'te "Mert Kaya" bile "Mert …" diye
              kesiliyordu. Kişinin adı bu satırın KONUSU, rozet ise sıfatı; sıfat konuyu
              ezemez.

              6rem'lik taban, yer kalmadığında rozeti ALT SATIRA itiyor ve ad tüm genişliği
              alıyor. Geniş kartta üçü zaten yan yana sığdığı için davranış değişmiyor.
              truncate yine duruyor: gerçekten uzun bir ad (30+ karakter) kartı bozmasın.
            */}
            <PersonLink
              userId={session.otherUserId}
              className="min-w-0 grow basis-24 truncate text-sm font-medium text-slate-800 hover:text-brand-700"
            >
              {session.otherDisplayName}
            </PersonLink>
            <Badge tone="neutral" className="ml-auto shrink-0">
              {session.iAmTutor ? 'Anlatıyorum' : 'Alıyorum'}
            </Badge>
          </div>

          {/*
            META ŞERİDİ — süre, puan ve geri sayım. Tarih artık burada DEĞİL: takvim
            bloğuna taşındı ve şerit üç parçadan ikiye indi, yani satır gerçekten
            "doğrulama bilgisi" hâline geldi.

            slate-600 (eski slate-500 değil): kart artık cam bir yüzeyin üstünde ve
            arkasından zemin geçiyor; slate-500 o bileşimde AA eşiğine dayanmıyordu.
            Meta'yı gövdeden ayıran şey artık renk değil PUNTO (text-xs).
          */}
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-slate-600">
            <span className="tabular-nums">{session.durationMinutes} dk</span>
            <Ayrac />
            {/* Her ders puan basar; gösterilen sayı sunucudan gelen mintAmount'tur. */}
            <span className="tabular-nums">{session.mintAmount} puan</span>
            {startsIn && !past && (
              <>
                <Ayrac />
                <span className={`font-semibold ${stil.vurgu}`}>{startsIn} sonra</span>
              </>
            )}
          </p>

          {showCode && (
            /*
              Kod TEK SATIR + chip. Eski kutu iki cümlelik açıklama taşıyordu ve her aktif
              kartta aynı paragraf tekrar ediyordu — açıklama baskın, kod kaybolandı.
              Açıklamanın tam hâli zaten kararın verildiği yerde duruyor (CompleteModal ve
              ApproveModal'daki Notice), kartta yalnızca kodu hatırlatmak yeter.

              Satır artık kendi kutusunda (slate-50 zemin + ince kenarlık): cam kartın
              üstünde çıplak duran gri bir chip zeminde kayboluyordu. title masaüstünde
              kısa hatırlatma verir; dokunmatikte tooltip yok ama bilgi kaybı da yok —
              kanıt yükleme/onay akışı aynı metni Notice olarak gösteriyor.
            */
            <p
              className="flex flex-wrap items-center gap-x-2.5 gap-y-1 rounded-xl border border-slate-200/70
                         bg-slate-50/80 px-3.5 py-2 text-xs text-slate-600"
              title={
                session.iAmTutor
                  ? 'Ders ekran görüntüsünde bu kod, sistem saati ve katılımcı listesi görünmeli.'
                  : 'Kanıt görselinde bu kodun göründüğünü doğrula.'
              }
            >
              <span>Doğrulama kodu</span>
              <code className="rounded-md bg-white px-2 py-0.5 font-mono text-sm font-semibold tracking-widest text-slate-900 ring-1 ring-inset ring-slate-200">
                {session.verificationCode}
              </code>
            </p>
          )}

          {session.iAmTutor && session.status === 'Booked' && endsIn && (
            <UyariSatiri>
              Time-Lock: “Dersi Tamamladım” <strong>{endsIn}</strong> sonra (planlanan bitişte) açılır.
            </UyariSatiri>
          )}

          {session.canApprove && autoApproveIn && (
            <UyariSatiri>
              Onaylamazsan <strong>{autoApproveIn}</strong> sonra otomatik onaylanacak ve{' '}
              eğitmene {session.mintAmount} puan yazılacak. İtiraz hakkın da o an kapanır.
            </UyariSatiri>
          )}
        </div>
      </div>

      {/*
        ── AKSİYON ŞERİDİ ────────────────────────────────────────────────────
        Kartın alt kenarına yapışık, üstünde ince bir ayraç. Bu bölge HER KARTTA var:
        `canApprove` ise tek düğme (onay), değilse en az "Şikayet et" duruyor — yani
        boş bir şerit çizilmiyor ve koşullu bir kabuk gerekmiyor.

        justify-end: düğmeler sağda. Türkçe soldan sağa okunuyor, karar da satırın
        sonunda veriliyor; ayrıca sağ yaslama, düğme sayısı 1'den 3'e çıktığında kartın
        sol tarafındaki metin hizasını hiç oynatmıyor.
      */}
      <div className="flex flex-wrap items-center justify-end gap-2 border-t border-slate-200/70 px-5 py-3.5">
        {session.iAmTutor && session.status === 'Booked' && (
          <Button
            disabled={!completeReady}
            onClick={() => onAction({ type: 'complete', session })}
            title={completeReady ? undefined : 'Ders bitiş saatinden önce tamamlanamaz.'}
          >
            Dersi tamamladım
          </Button>
        )}

        {session.canApprove && (
          <Button variant="success" onClick={() => onAction({ type: 'approve', session })}>
            Kanıtı incele ve onayla
          </Button>
        )}

        {/* Şikayet HER derste açık: eski itiraz yalnızca onay bekleyen derste
            mümkündü, oysa kötü davranış geçmiş bir derste de yaşanmış olabilir.
            Savunma düğmesi YOK — şikayet tek yönlüdür (bkz. Domain/Moderation/Report.cs). */}
        {!session.canApprove && (
          <Button variant="secondary" onClick={() => onAction({ type: 'report', session })}>
            Şikayet et
          </Button>
        )}

        {session.canCancel && (
          <Button variant="secondary" onClick={() => onAction({ type: 'cancel', session })}>
            İptal
          </Button>
        )}
      </div>
    </CamKart>
  )
}

/**
 * Çift taraflı onayın anlamlı olduğu yer: puan BASILMADAN önce öğrenci kanıtı görür.
 * (Eskiden buradaki risk öğrencinin kredisiydi; artık öğrencinin kaybedeceği bir şey yok,
 * ama onay hâlâ şart — basımın tek meşru tetikleyicisi dersin gerçekten yapılmış olması.)
 * Kanıt görseli Authorization başlığı gerektirdiği için blob olarak indirilip object URL'e çevrilir.
 */
function ApproveModal({ session, onClose, onApproved, onReport, onDispute }) {
  const proofs = useAsync(() => api.sessionProofs(session.sessionId), [session.sessionId])
  const [imageUrl, setImageUrl] = useState(null)
  const [imageError, setImageError] = useState(null)
  const [error, setError] = useState(null)

  /*
    Onay, dosyanın en pahalı geri alınamaz işlemi: puan basıyor. Kilit BookModal'daki
    gerekçenin aynısıyla ref üzerinde (state bir sonraki render'a kadar eski değeri
    gösterir). İki kez gönderilen onay, sunucu idempotent değilse çift basım demektir.

    Bu bayrak MODALIN KENDİ state'i, sayfa seviyesinde global bir bayrak DEĞİL: modal
    session id ile key'lendiği için her ders kendi kilidini taşır. Global tek bayrak,
    bir dersi onaylarken başka bir dersin butonunu da kilitlerdi.
  */
  const [onaylaniyor, setOnaylaniyor] = useState(false)
  const onayKilidi = useRef(false)

  const latestProof = proofs.data?.[proofs.data.length - 1] ?? null

  useEffect(() => {
    if (!latestProof) return

    let revoked = false
    let url = null

    api
      .proofContentUrl(session.sessionId, latestProof.proofId)
      .then((objectUrl) => {
        if (revoked) {
          URL.revokeObjectURL(objectUrl)
          return
        }
        url = objectUrl
        setImageUrl(objectUrl)
      })
      .catch(setImageError)

    return () => {
      revoked = true
      if (url) URL.revokeObjectURL(url) // Bellek sızıntısını önle.
    }
  }, [latestProof, session.sessionId])

  async function approve() {
    if (onayKilidi.current) return
    onayKilidi.current = true
    setOnaylaniyor(true)
    setError(null)
    try {
      const result = await api.approveSession(session.sessionId)

      // credit_transferred — transfer GERÇEKLEŞTİKTEN sonra; onay tıklaması yetmez,
      // sunucu kilit/escrow adımlarında reddedebilir. Sunucu 0 döndürse bile olay
      // gönderilir: olayı atlamak "kaç ders tamamlandı" ölçümünü eksik bırakırdı.
      trackEvent(AnalyticsEvents.CreditTransferred, {
        credits: result.creditsMinted,
        trigger: 'student_approval',
        volunteer: result.creditsMinted === 0,
      })

      onApproved(result.creditsMinted, session)
    } catch (err) {
      setError(err)
    } finally {
      onayKilidi.current = false
      setOnaylaniyor(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="Kanıtı incele ve onayla">
      <div className="space-y-4">
        <Notice tone="info">
          {/* "aktarılır" DEĞİL "yazılır": senden bir şey alınıp ona verilmiyor, puan bu anda
              üretiliyor. Eski transfer dili öğrenciye bir bedel ödediği izlenimi veriyordu. */}
          Onayladığında {session.otherDisplayName} kişisine{' '}
          <strong>{session.mintAmount} puan</strong> yazılır ve işlem geri alınamaz. Senden
          bir şey düşmez. Görselde{' '}
          <strong className="font-mono">{session.verificationCode}</strong> kodunun, sistem saatinin
          ve katılımcı listesinin göründüğünü doğrula.
        </Notice>

        {proofs.loading ? (
          <Loading label="Kanıt yükleniyor…" />
        ) : !latestProof ? (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
            Bu derse hiç kanıt yüklenmemiş. Ders gerçekten yapılmadıysa onaylama —
            dilersen <strong>şikayet et</strong>, yönetim inceler.
          </div>
        ) : (
          <div className="space-y-2">
            {/* Emoji (⚠️) yerine UyariIkonu: satırın rose-800 tonunu currentColor ile
                birebir alıyor ve her platformda aynı çiziliyor. */}
            {latestProof.isDuplicateHash && (
              <div className="flex items-start gap-2 rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800">
                <UyariIkonu className="mt-0.5 h-5 w-5 shrink-0" />
                <span>
                  Bu görsel <strong>başka bir derste de kullanılmış</strong>. Sahte kanıt olabilir —
                  dikkatle incele.
                </span>
              </div>
            )}

            {imageError ? (
              <ErrorBox error={imageError} />
            ) : imageUrl ? (
              <a href={imageUrl} target="_blank" rel="noopener noreferrer">
                <img
                  src={imageUrl}
                  alt="Ders kanıtı ekran görüntüsü"
                  className="max-h-80 w-full rounded-lg border border-slate-200/80 object-contain"
                />
              </a>
            ) : (
              <Loading label="Görsel indiriliyor…" />
            )}

            <p className="text-xs text-slate-600">
              Yükleme: {formatDateTime(latestProof.uploadedAtUtc)} · Büyütmek için görsele tıkla.
            </p>
          </div>
        )}

        <ErrorBox error={error} />

        {/*
          ŞİKAYET DÜĞMEDEN BAĞLANTIYA İNDİ. Düğmeler dersin KADERİNİ belirleyenler:
          onayla ya da itiraz et. Şikayet dersi etkilemiyor (kişi hakkında bildirim);
          üçünü eşit ağırlıkta sunmak "hangisi puan basımını durdurur" sorusunu
          belirsiz bırakıyordu.
        */}
        <div className="border-t border-slate-100 pt-3 text-center">
          <button
            type="button"
            disabled={onaylaniyor}
            onClick={onReport}
            className="text-sm text-slate-500 underline underline-offset-2 hover:text-slate-700
                       disabled:cursor-not-allowed disabled:opacity-50"
          >
            Ders değil, kişi hakkında şikayetim var
          </button>
        </div>

        <div className="flex flex-wrap justify-end gap-2">
          {/* Onay uçarken diğer düğmeler de kapalı: basım devam ederken itiraza geçmek,
              hangi sonucun geçerli olduğunu tıklama sırasına bırakırdı. */}
          <Button variant="secondary" disabled={onaylaniyor} onClick={onClose}>
            Sonra karar ver
          </Button>
          <Button variant="danger" disabled={onaylaniyor} onClick={onDispute}>
            İtiraz et
          </Button>
          <Button variant="success" loading={onaylaniyor} disabled={onaylaniyor} onClick={approve}>
            Onayla ve puanı yaz
          </Button>
        </div>
      </div>
    </Modal>
  )
}

function BookModal({ matches, onClose, onBooked }) {
  // Sunucudaki izinli süre kümesiyle birebir (SessionRules.AllowedDurations).
  // Buraya fazladan bir değer eklemek, kullanıcıya sunucunun reddedeceği bir seçenek
  // göstermek olur.
  const DURATION_OPTIONS = [30, 60]

  /*
    Puan önizlemesinin GÖSTERİM sabitleri — sunucudaki SessionRules.MintPerBlock (50)
    ve MintBlockMinutes (30) ile birebir.

    KURAL DEĞİL: seviye.js'teki EN_YUKSEK_SEVIYE ile aynı statüde. Hiçbir karar bu
    sayılara bakılarak verilmiyor; rezervasyon sonrası bildirimde ve ders kartında
    görünen sayı her zaman sunucunun döndürdüğü mintAmount. Buradaki tek iş, kullanıcı
    "Rezerve et"e basmadan ÖNCE özet şeridinde kaç puan basılacağını gösterebilmek —
    rezervasyon öncesinde bu sayının sorulabileceği bir uç yok. Sunucuda kural
    değişirse burası da güncellenmeli: DURATION_OPTIONS ile aynı bakım sözleşmesi.
  */
  const BLOK_DAKIKA = 30
  const BLOK_PUANI = 50

  // Dokunma kuralı (bkz. ui.jsx Button): lg ALTINDA 44px hedef; lg üstünde fare var,
  // buton girdilerle aynı yüksekliğe iner.
  const SURE_BUTON_SINIFI =
    'min-h-11 flex-1 rounded-lg border px-3 py-2 text-sm font-medium transition ' +
    'focus:outline-none focus:ring-2 focus:ring-brand-200 lg:min-h-9'

  const [matchId, setMatchId] = useState('')
  const [topicId, setTopicId] = useState('')
  const [startLocal, setStartLocal] = useState('')
  const [duration, setDuration] = useState(60)
  const [error, setError] = useState(null)

  /*
    ÇİFT REZERVASYON KORUMASI — bayrağın İKİ kopyası var ve bu bilinçli.

    `gonderiliyor` (state) ARAYÜZ içindir: butonu disabled yapar ve spinner'ı gösterir.
    `gonderimKilidi` (ref) MANTIK içindir: senkron muhafız.

    Neden state tek başına YETMEZ: setState asenkrondur ve handler'ın gördüğü değer, o
    handler'ın ait olduğu RENDER'ın anlık görüntüsüdür. Kullanıcı butona 50 ms arayla iki
    kez bastığında ikinci tıklama, ilk tıklamanın tetiklediği yeniden render DOM'a
    yansımadan handler'a girer; orada `gonderiliyor` hâlâ false'tur, `disabled` da henüz
    DOM'a işlenmemiştir. Sonuç: api.bookSession iki kez çağrılır ve aynı ders iki kez
    rezerve edilir.

    Ref'in değeri ise atandığı anda okunabilir — render beklemez. Muhafız bu yüzden ref
    üzerinden çalışıyor; state yalnızca gördüğümüz şeyi anlatıyor.
  */
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const gonderimKilidi = useRef(false)

  /*
    Rezervasyonu YAPAN taraf her zaman ÖĞRENCİdir. Dolayısıyla seçilebilecek konu, karşı
    tarafın BANA anlatacağı konudur:
      - Ben isteği başlattıysam  → requestedTopic (benim öğrenmek istediğim)
      - Karşı taraf başlattıysa  → offeredTopic  (onun karşılığında anlatmayı önerdiği)
    Her ikisini birden listelemek, kullanıcının KENDİ anlatacağı konuya öğrenci olarak
    kaydolmasına yol açardı (backend bunu reddetmez: ders açılır, puan yanlış tarafa yazılır).
  */
  const options = matches.map((match) => ({
    match,
    topicId: match.iAmInitiator ? match.requestedTopicId : match.offeredTopicId,
    topicName: match.iAmInitiator ? match.requestedTopicName : match.offeredTopicName,
  }))

  const selected = options.find((o) => o.match.matchId === matchId) ?? null
  const bookable = options.filter((o) => o.topicId)

  /*
    Özet şeridinin canlı türevleri.

    baslangicGecerli: datetime-local ya boş ya geçerli değer verir; Invalid Date'e karşı
    yine de muhafız var, çünkü Intl.format geçersiz tarihte RangeError fırlatır ve tek
    bozuk değer tüm modali düşürürdü. formatDateTime'a startLocal'ın YEREL hâli gidiyor
    (UTC'ye çevrilmeden): fonksiyon yalnızca biçimlendirir ve kullanıcı kendi saat
    diliminde seçtiğini kendi saat diliminde görmeli. UTC dönüşümü yalnızca sunucuya
    giden yolda (submit içindeki toISOString).
  */
  const baslangicGecerli = Boolean(startLocal) && !Number.isNaN(new Date(startLocal).getTime())
  const puanOnizleme = (Number(duration) / BLOK_DAKIKA) * BLOK_PUANI

  async function submit(event) {
    event.preventDefault()

    // Muhafız fonksiyonun EN BAŞINDA: gerekçe yukarıdaki kilit tanımında.
    if (gonderimKilidi.current) return
    gonderimKilidi.current = true
    setGonderiliyor(true)
    setError(null)
    try {
      // datetime-local yerel saat verir; backend UTC bekler (toISOString hep "...Z" üretir).
      const result = await api.bookSession({
        matchId,
        topicId: selected.topicId,
        scheduledStartUtc: new Date(startLocal).toISOString(),
        durationMinutes: Number(duration),
      })
      // session_requested — ders talebi (rezervasyon) oluştu. Escrow yok; basım onayda.
      trackEvent(AnalyticsEvents.SessionRequested, {
        duration_minutes: Number(duration),
        mint_amount: result.mintAmount,
      })

      onBooked(result.verificationCode, result.mintAmount)
    } catch (err) {
      setError(err)
    } finally {
      // Kilit YALNIZCA burada açılır: istek başarıyla bitse de hata alsa da. Erken
      // açılsaydı (örneğin try'ın sonunda) hata dalında buton kilitli kalır, kullanıcı
      // düzeltip tekrar deneyemezdi.
      gonderimKilidi.current = false
      setGonderiliyor(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="Ders rezerve et">
      {matches.length === 0 ? (
        <EmptyState
          title="Henüz arkadaşın yok"
          description="Önce Keşfet sayfasından istek gönder ve karşı tarafın kabul etmesini bekle."
        />
      ) : bookable.length === 0 ? (
        <EmptyState
          title="Arkadaşlarında sana anlatılacak konu yok"
          description="Mevcut arkadaşlarında ders anlatan taraf sensin. Ders almak için Keşfet'ten yeni bir istek gönder."
        />
      ) : (
        <>
          <form onSubmit={submit} id="book-form" className="space-y-4">
            {/*
              1. GRUP — arkadaş ve konu. Modal üç katlı bir hiyerarşi anlatıyor: önce
              KİMDEN/NE (bu grup), sonra NE ZAMAN (alttaki grup), en altta da kararın
              tamamını tek bakışta doğrulatan özet şeridi. Grup başlığındaki ikon çipi
              (bg-brand-50) süs değil yön işareti: kutunun konusunu metinden önce söylüyor.
            */}
            <section className="rounded-xl border border-slate-100 p-4">
              <div className="mb-3 flex items-center gap-2.5">
                <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-600">
                  <KepIkonu className="h-5 w-5" />
                </span>
                <div>
                  <p className="text-sm font-semibold text-slate-800">Arkadaş ve konu</p>
                  <p className="text-xs text-slate-600">
                    Dersi alan taraf sensin; listelenen konu karşı tarafın sana anlatacağı konudur.
                  </p>
                </div>
              </div>
              {/*
                Eski "Konu: …" doğrulama kutusu SİLİNMEDİ, taşındı: aynı işi artık özet
                şeridinin "Konu" satırı yapıyor. Bilgiyi iki yerde tekrar etmek, şeridin
                "tek bakışta doğrula" işini sulandırırdı.
              */}
              <select
                className="input"
                aria-label="Arkadaş"
                value={matchId}
                onChange={(e) => {
                  setMatchId(e.target.value)
                  setTopicId('')
                }}
                required
              >
                <option value="">Seç…</option>
                {bookable.map((option) => (
                  <option key={option.match.matchId} value={option.match.matchId}>
                    {option.match.otherDisplayName} anlatacak — {option.topicName}
                  </option>
                ))}
              </select>
            </section>

            {/* 2. GRUP — zaman: tarih-saat ve süre yan yana (mobilde alt alta). */}
            <section className="rounded-xl border border-slate-100 p-4">
              <div className="mb-3 flex items-center gap-2.5">
                <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-600">
                  <TakvimIkonu className="h-5 w-5" />
                </span>
                <div>
                  <p className="text-sm font-semibold text-slate-800">Tarih, saat ve süre</p>
                  <p className="text-xs text-slate-600">Kendi saat diliminde seç; sistem UTC'ye çevirir.</p>
                </div>
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <div>
                  <label className="label" htmlFor="rezervasyon-baslangic">
                    Başlangıç
                  </label>
                  <input
                    id="rezervasyon-baslangic"
                    type="datetime-local"
                    className="input"
                    value={startLocal}
                    onChange={(e) => setStartLocal(e.target.value)}
                    required
                  />
                </div>
                <div>
                  <span className="label" id="rezervasyon-sure-etiketi">
                    Süre
                  </span>
                  {/*
                    Süre bir liste değil İKİ BUTON: seçenek sayısı ikiyken açılır listeyi
                    açıp kapatmak gereksiz bir adımdı ve iki seçenek yan yana durunca
                    karşılaştırma bedava. State sözleşmesi değişmedi — aynı `duration`
                    state'i, sunucuya yine Number(duration) gidiyor; buton sayıyı sayı
                    olarak atıyor, eski select'in dizgeye çevirmesi zaten Number ile
                    karşılanıyordu.
                  */}
                  <div className="flex gap-2" role="group" aria-labelledby="rezervasyon-sure-etiketi">
                    {DURATION_OPTIONS.map((dk) => {
                      const aktif = Number(duration) === dk
                      return (
                        <button
                          key={dk}
                          type="button"
                          onClick={() => setDuration(dk)}
                          aria-pressed={aktif}
                          className={`${SURE_BUTON_SINIFI} ${
                            aktif
                              ? 'border-brand-500 bg-brand-50 text-brand-800'
                              : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50'
                          }`}
                        >
                          {dk} dakika
                        </button>
                      )
                    })}
                  </div>
                </div>
              </div>
            </section>

            {/*
              ÖZET ŞERİDİ — kararın tamamı tek bakışta: konu, anlatan, zaman, süre ve
              basılacak puan. Girdiler değiştikçe canlı güncellenir (hepsi zaten state).

              Eski tasarımın "öğrenciye sayı gösterme" kuralı BİLEREK değişti: sayı
              etiketsiz durduğunda ücret gibi okunuyordu ve bu yüzden gizleniyordu.
              Şerit sayıyı açıkça "eğitmenin kazanacağı puan" diye etiketleyip hemen
              altında dersin sana ücretsiz olduğunu söylüyor — böylece onay ekranında
              beliren puan da sürpriz olmaktan çıkıyor. Şeffaflık güven verir; sayının
              kaynağı yukarıdaki gösterim sabitleri, bağlayıcı değer sunucunun
              mintAmount'u.

              aria-live: değişen değerleri ekran okuyucu da duysun.
            */}
            <section
              className="rounded-xl border border-brand-100 bg-brand-50 px-4 py-3 text-brand-800"
              aria-live="polite"
            >
              <p className="text-xs font-semibold uppercase tracking-wide text-brand-700">Özet</p>
              <dl className="mt-2 space-y-1.5 text-sm">
                <div className="flex items-baseline justify-between gap-3">
                  <dt className="shrink-0 text-brand-700">Konu</dt>
                  <dd className="text-right font-medium">
                    {selected ? (
                      selected.topicName
                    ) : (
                      <span className="font-normal text-brand-700/70">Arkadaş seçilmedi</span>
                    )}
                  </dd>
                </div>
                <div className="flex items-baseline justify-between gap-3">
                  <dt className="shrink-0 text-brand-700">Anlatan</dt>
                  <dd className="text-right font-medium">
                    {selected ? (
                      selected.match.otherDisplayName
                    ) : (
                      <span className="font-normal text-brand-700/70">—</span>
                    )}
                  </dd>
                </div>
                <div className="flex items-baseline justify-between gap-3">
                  <dt className="shrink-0 text-brand-700">Tarih ve saat</dt>
                  <dd className="text-right font-medium">
                    {baslangicGecerli ? (
                      formatDateTime(startLocal)
                    ) : (
                      <span className="font-normal text-brand-700/70">Henüz seçilmedi</span>
                    )}
                  </dd>
                </div>
                <div className="flex items-baseline justify-between gap-3">
                  <dt className="shrink-0 text-brand-700">Süre</dt>
                  <dd className="text-right font-medium">{Number(duration)} dakika</dd>
                </div>
              </dl>
              <div className="mt-3 flex items-center justify-between gap-3 border-t border-brand-100 pt-3">
                <span className="flex items-center gap-2 text-sm font-medium">
                  <ArtanIkonu className="h-4 w-4" />
                  Eğitmenin kazanacağı puan
                </span>
                <span className="text-base font-semibold tabular-nums">+{puanOnizleme} puan</span>
              </div>
              <p className="mt-1.5 text-xs text-brand-700">
                Sana ücretsiz — puanı sen ödemezsin, ders onaylandığında sistem basar.
              </p>
            </section>

            <ErrorBox error={error} />
          </form>

          <div className="mt-4 flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Vazgeç
            </Button>
            {/* loading zaten disabled yapıyor; `gonderiliyor` yine de disabled ifadesine
                AYRICA yazıldı — koşul okunurken "gönderim sırasında basılamaz" kuralının
                Button'un iç ayrıntısına bağlı kalmaması için. */}
            <Button
              type="submit"
              form="book-form"
              loading={gonderiliyor}
              disabled={gonderiliyor || !selected || !startLocal}
            >
              Rezerve et
            </Button>
          </div>
        </>
      )}
    </Modal>
  )
}

function CompleteModal({ session, onClose, onDone }) {
  const [code, setCode] = useState('')
  const [file, setFile] = useState(null)
  const [error, setError] = useState(null)

  /*
    Kanıt yüklemesi bir DOSYA taşıyor: yavaş bağlantıda istek saniyelerce sürer ve
    "bir şey olmuyor" sanan kullanıcının tekrar basma ihtimali en yüksek yer burasıdır.
    Çift gönderim aynı derse iki kanıt yüklerdi; ikincisi, ilkiyle bire bir aynı görsel
    olduğu için karşı tarafa "başka derste de kullanılmış" (isDuplicateHash) uyarısı
    olarak dönebilirdi — yani kullanıcı kendi kanıtını kendi eliyle şüpheli hâle getirirdi.
  */
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const gonderimKilidi = useRef(false)

  async function submit(event) {
    event.preventDefault()

    if (gonderimKilidi.current) return
    gonderimKilidi.current = true
    setGonderiliyor(true)
    setError(null)
    try {
      await api.completeSession(session.sessionId, code.trim(), file)

      // proof_uploaded — kanıt sunucuya kabul edildi (kod eşleşmesi ve dosya doğrulaması geçti).
      trackEvent(AnalyticsEvents.ProofUploaded, {
        duration_minutes: session.durationMinutes,
      })

      onDone()
    } catch (err) {
      setError(err)
    } finally {
      gonderimKilidi.current = false
      setGonderiliyor(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="Dersi tamamladım">
      <form onSubmit={submit} id="complete-form" className="space-y-4">
        <Notice tone="info">
          Ekran görüntüsünde <strong>sistem saati</strong>, <strong>katılımcı listesi</strong> ve
          doğrulama kodu <strong className="font-mono">{session.verificationCode}</strong> görünmelidir.
          Öğrenci onayladığında {session.mintAmount} puan kazanırsın.
        </Notice>

        <Field label="Doğrulama kodu (Session ID)">
          <input
            className="input font-mono uppercase tracking-wider"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            required
            maxLength={12}
            placeholder={session.verificationCode}
          />
        </Field>

        <Field label="Kanıt ekran görüntüsü" hint="PNG, JPEG veya WebP · en fazla 10 MB.">
          <input
            type="file"
            accept="image/png,image/jpeg,image/webp"
            className="input"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            required
          />
        </Field>

        <ErrorBox error={error} />
      </form>

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Vazgeç
        </Button>
        <Button
          type="submit"
          form="complete-form"
          loading={gonderiliyor}
          disabled={gonderiliyor || !file || !code.trim()}
        >
          Gönder
        </Button>
      </div>
    </Modal>
  )
}

/*
  DERS ŞİKAYETİNİN SEBEP ALT KÜMESİ.

  Eskiden bu <select>, REPORT_REASON_LABELS tablosunun tamamını döküyordu. Tablo o
  gün ders sebeplerinden ibaret olduğu için sorun görünmüyordu; forum şikayetleri
  eklenince (Spam, Telif, Kişisel bilgi, Konu dışı — 2026-08-27) ders formunda
  "Spam veya reklam" gibi bağlamsız seçenekler belirecekti. Alt küme artık BURADA
  yazılı: tabloya sebep eklemek bir daha bu formu sessizce değiştirmiyor.

  Sıra bilinçli: en sık şikayet edilen ilk sırada, "Diğer" en sonda.
*/
/*
  İTİRAZ — şikayetten AYRI bir mekanizma ve arayüzün bunu net söylemesi gerekiyor.

  Şikayet kişi hakkında; ders akmaya devam eder, puan basılır. İtiraz ise dersin
  KENDİSİNE dair: "yapılmadı" ya da "kanıt sahte". Ders Disputed'a geçer, puan basımı
  DONAR ve konu yönetim hakemliğine düşer; öğrenci onay yolunu da kapatmış olur.

  Sebep listesi DisputeReason enum'undan ve ders şikayeti listesiyle aynı beş değeri
  taşıyor — ama AYRI yazılıyor: iki enum bağımsız, birinin değişmesi diğerinin formunu
  sessizce bozmamalı.
*/
const ITIRAZ_SEBEPLERI = ['SessionNotHeld', 'FakeProof', 'DurationMismatch', 'Abuse', 'Other']

function DisputeModal({ session, onClose, onDone }) {
  const [reason, setReason] = useState('SessionNotHeld')
  const [description, setDescription] = useState('')
  const [error, setError] = useState(null)
  const [gonderiliyor, setGonderiliyor] = useState(false)

  async function submit(e) {
    e.preventDefault()
    // Çift gönderim: ikincisi DISPUTE_ALREADY_OPEN alır ve kullanıcı, itiraz ASLINDA
    // açılmışken hata görürdü.
    if (gonderiliyor || description.trim().length < 10) return
    setGonderiliyor(true)
    setError(null)
    try {
      await api.disputeSession(session.sessionId, reason, description.trim())
      onDone()
    } catch (err) {
      setError(err)
      setGonderiliyor(false)
    }
  }

  return (
    <Modal open onClose={gonderiliyor ? () => {} : onClose} title="Bu derse itiraz et">
      <form onSubmit={submit} className="space-y-4">
        <Notice tone="warning">
          İtiraz, dersi <strong>yönetim hakemliğine</strong> taşır: {session.otherDisplayName}{' '}
          kişisine puan <strong>yazılmaz</strong> ve karar verilene kadar donar. Bu dersi
          artık onaylayamazsın. Yalnızca ders gerçekten yapılmadıysa ya da kanıt bu derse
          ait değilse itiraz et.
        </Notice>

        <Field label="Sebep">
          <select className="input" value={reason} onChange={(e) => setReason(e.target.value)}>
            {ITIRAZ_SEBEPLERI.map((value) => (
              <option key={value} value={value}>
                {REPORT_REASON_LABELS[value]}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Ne oldu?" hint="En az 10 karakter. Hakem yalnızca bunu ve kanıtı görecek.">
          <textarea
            className="input h-28 resize-none"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            required
            minLength={10}
            maxLength={2000}
          />
        </Field>

        <ErrorBox error={error} />

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" disabled={gonderiliyor} onClick={onClose}>
            Vazgeç
          </Button>
          <Button
            type="submit"
            variant="danger"
            loading={gonderiliyor}
            disabled={gonderiliyor || description.trim().length < 10}
          >
            İtirazı gönder
          </Button>
        </div>
      </form>
    </Modal>
  )
}

const DERS_SIKAYET_SEBEPLERI = ['SessionNotHeld', 'FakeProof', 'DurationMismatch', 'Abuse', 'Other']

function ReportModal({ session, onClose, onDone }) {
  const [reason, setReason] = useState('SessionNotHeld')
  const [description, setDescription] = useState('')
  const [error, setError] = useState(null)

  // Çift gönderim burada yönetime AYNI şikayetten iki kayıt düşürürdü; moderatör aynı
  // olayı iki kez inceler, kullanıcı da "ısrarla şikayet eden" gibi görünürdü.
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const gonderimKilidi = useRef(false)

  async function submit(event) {
    event.preventDefault()

    if (gonderimKilidi.current) return
    gonderimKilidi.current = true
    setGonderiliyor(true)
    setError(null)
    try {
      await api.reportSession(session.sessionId, reason, description.trim())

      // dispute_opened — itiraz sebebi sabit bir sözlükten geldiği için serbest metin
      // değil; kullanıcının yazdığı açıklama GÖNDERİLMEZ (kişisel veri içerebilir).
      trackEvent(AnalyticsEvents.DisputeOpened, { reason })

      onDone()
    } catch (err) {
      setError(err)
    } finally {
      gonderimKilidi.current = false
      setGonderiliyor(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="Şikayet et">
      <form onSubmit={submit} id="report-form" className="space-y-4">
        <Notice tone="info">
          Şikayetin <strong>yalnızca yönetime</strong> gider. Karşı taraf ne şikayeti görür,
          ne bildirim alır, ne de yanıt verebilir.
          {/* "Yönetim gerekli görürse uyarı, askı ya da ban uygular" cümlesi KALDIRILDI
              (2026-08-27, canlıya çıkış denetimi): yaptırım uçları backend'de vardı ama
              api.js'te tanımlı değildi, yani moderatör panelden kimseyi uyaramıyordu.

              O eksik aynı gün kapatıldı (api.sanctionUser / unbanUser + Admin.jsx'teki
              yaptırım modali), yani cümle artık tutulabilir bir söz. Yine de geri
              KONMADI ve sebebi ayrı: yaptırımın uygulanıp uygulanmayacağı moderatörün
              kararı, şikayet edene verilebilecek bir söz değil. Aşağıdaki metin
              yalnızca KESİN olanı söylüyor — şikayetin incelendiğini. */}
          <span className="mt-2 block">
            Dersin akışı değişmez — bu bir itiraz değil, kişi hakkında bildirimdir.
            Şikayetin yönetim tarafından incelenir.
          </span>
        </Notice>

        <Field label="Sebep">
          <select className="input" value={reason} onChange={(e) => setReason(e.target.value)}>
            {DERS_SIKAYET_SEBEPLERI.map((value) => (
              <option key={value} value={value}>
                {REPORT_REASON_LABELS[value]}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Ne oldu?" hint="En az 15 karakter. Yönetim yalnızca senin anlattığını görecek.">
          <textarea
            className="input h-28 resize-none"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            required
            minLength={15}
            maxLength={2000}
          />
        </Field>

        <ErrorBox error={error} />
      </form>

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Vazgeç
        </Button>
        <Button
          type="submit"
          form="report-form"
          variant="danger"
          loading={gonderiliyor}
          disabled={gonderiliyor || description.trim().length < 15}
        >
          Şikayeti gönder
        </Button>
      </div>
    </Modal>
  )
}

function CancelModal({ session, onClose, onDone }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState(null)

  // İptal, ilk çağrıdan sonra dersi Cancelled'a taşır; ikinci çağrı sunucudan hata
  // döner ve kullanıcı, iptal ASLINDA başarılı olmuşken kırmızı bir hata kutusu görür.
  const [iptalEdiliyor, setIptalEdiliyor] = useState(false)
  const iptalKilidi = useRef(false)

  async function submit() {
    if (iptalKilidi.current) return
    iptalKilidi.current = true
    setIptalEdiliyor(true)
    setError(null)
    try {
      await api.cancelSession(session.sessionId, reason.trim() || null)
      onDone()
    } catch (err) {
      setError(err)
    } finally {
      iptalKilidi.current = false
      setIptalEdiliyor(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="Dersi iptal et">
      <div className="space-y-4">
        <p className="text-sm text-slate-600">
          {formatDateTime(session.scheduledStartUtc)} tarihli ders iptal edilecek. Ders almak
          ücretsiz olduğu için iade edilecek bir puan yok; eğitmene de puan yazılmaz.
        </p>

        <Field label="Sebep (opsiyonel)">
          <input
            className="input"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={500}
          />
        </Field>

        <ErrorBox error={error} />

        <div className="flex justify-end gap-2">
          <Button variant="secondary" disabled={iptalEdiliyor} onClick={onClose}>
            Vazgeç
          </Button>
          <Button
            variant="danger"
            loading={iptalEdiliyor}
            disabled={iptalEdiliyor}
            onClick={submit}
          >
            Dersi iptal et
          </Button>
        </div>
      </div>
    </Modal>
  )
}
