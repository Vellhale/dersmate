import { useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../lib/api'
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
  OkAsagiIkonu,
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
  Sayfa başına 5 ders. Eskiden 20'ydi ve "daha fazla yükle" ile ekleniyordu; 60 dersi olan
  bir kullanıcıda sayfa üç ekran boyu uzuyordu. Sayfalar artık DEĞİŞİYOR, birikmiyor —
  liste hep aynı yükseklikte kalıyor ve sayfanın altındaki puan geçmişi hep aynı yerde.
*/
const PAST_PAGE_SIZE = 5

export default function Sessions() {
  const sessions = useAsync(() => api.mySessions(1, PAST_PAGE_SIZE), [])
  const matches = useAsync(() => api.myMatches(), [])
  const { refreshWallet } = useWallet()
  const [notice, setNotice] = useState(null)
  const [bookOpen, setBookOpen] = useState(false)
  const [dialog, setDialog] = useState(null) // { type: 'complete'|'approve'|'report'|'cancel', session }

  /*
    Geçmişin GÖRÜNEN sayfası. null iken sessions.data'daki ilk sayfa gösterilir; kullanıcı
    bir sayfaya tıkladığında burası dolar ve onu GEÇERSİZ KILAR (eklemez).

    Ayrı state olmasının sebebi: sessions.reload() tüm ekranı tazeliyor ve geçmiş sayfası
    da 1'e dönmeli — ders onaylandığında o ders aktiften geçmişin ilk sayfasına düşer.
    Tek kaynak kullanılsaydı ya reload sayfayı sıfırlamaz ya da sayfa değiştirmek tüm
    ekranı spinner'a düşürürdü.
  */
  const [pastView, setPastView] = useState(null)
  const [pastLoading, setPastLoading] = useState(false)
  const [pastError, setPastError] = useState(null)

  /*
    Time-Lock ve otomatik onay geri sayımları canlı aksın diye periyodik yeniden çizim.

    `tick` ARTIK OKUNUYOR (eskiden `const [, setTick]` ile atılıyordu) çünkü aşağıdaki
    gruplama saate bakıyor: yalnızca yeniden render yetmez, useMemo'nun bağımlılığına
    girmezse önbellekteki eski gruplama döner ve saati dolan ders ekranda "Yaklaşan"
    olarak asılı kalır. Sayfa açık dururken de doğru tarafa geçmesi bu bağımlılıkla oluyor.
  */
  const [tick, setTick] = useState(0)
  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 20000)
    return () => clearInterval(id)
  }, [])

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
      duruyordu (bitişinin üzerinden 367 dakika geçmişti). Kullanıcının gördüğü şey
      yanlıştı.

      Ayrım artık ÜÇ yönlü:
        action     → senden bir şey bekliyor (tamamla / onayla / itirazlı)
        upcoming   → saati HENÜZ GELMEDİ
        gecmisAcik → saati GEÇTİ ama kayıt hâlâ açık

      Üçüncü grup "past" ile birleştirilmedi ve bu bilinçli: `past` sunucudan SAYFALI
      geliyor, araya istemci tarafında satır sokmak sayfa sayılarını yalanlardı. Bunun
      yerine sağ sütunun BAŞINA, kendi başlığıyla sabitleniyor — böylece hem "Yaklaşan"
      listesini kirletmiyor hem de sayfalama içinde kaybolmuyor. Durum rozeti hâlâ
      "Rezerve" diyor, yani kaydın kapanmadığı görünmeye devam ediyor.

      KIYAS BİTİŞ SAATİYLE, başlangıçla değil: 60 dakikalık bir ders başladı diye geçmiş
      olmuyor. Sunucu `scheduledEndUtc` alanını zaten gönderiyor (ISO + Z), yani yerel
      saat dilimine göre kayma riski yok — `new Date()` doğrudan çözümlüyor.
      ─────────────────────────────────────────────────────────────────────────
    */
    const active = sessions.data?.active ?? []
    const simdi = Date.now()

    const aksiyonBekliyor = (s) => s.canComplete || s.canApprove || s.status === 'Disputed'
    const saatiGecti = (s) => new Date(s.scheduledEndUtc).getTime() <= simdi

    return {
      action: active.filter(aksiyonBekliyor),
      upcoming: active.filter((s) => !aksiyonBekliyor(s) && !saatiGecti(s)),
      gecmisAcik: active.filter((s) => !aksiyonBekliyor(s) && saatiGecti(s)),
      past: (pastView ?? sessions.data?.past)?.items ?? [],
    }
    // tick: 20 saniyede bir yeniden hesapla — saat ilerledikçe ders kendiliğinden
    // "Yaklaşan"dan "saati geçmiş"e düşsün, sayfa yenilenmesini beklemesin.
  }, [sessions.data, pastView, tick])

  const pastSayfa = pastView ?? sessions.data?.past
  const pastTotal = pastSayfa?.totalCount ?? 0
  const pastPage = pastSayfa?.page ?? 1
  const pastTotalPages = Math.ceil(pastTotal / PAST_PAGE_SIZE)
  const activeTruncated = (sessions.data?.activeTotal ?? 0) > (sessions.data?.active?.length ?? 0)

  function refresh(message) {
    setDialog(null)
    setBookOpen(false)
    if (message) setNotice(message)

    // Geçmiş 1. sayfaya döner: onaylanan ders aktiften çıkıp geçmişin BAŞINA girer,
    // kullanıcı 4. sayfada kalsaydı az önce onayladığı dersi göremezdi.
    setPastView(null)
    setPastError(null)
    sessions.reload()
    // Onay puan basar; profildeki puan sayacı ve başlıktaki seviye rozeti aynı cüzdan
    // ucundan besleniyor, o yüzden tazeleniyor.
    refreshWallet()
  }

  async function sayfayaGit(hedef) {
    if (hedef < 1 || hedef > pastTotalPages || hedef === pastPage) return
    // Sayfalama düğmeleri `disabled={pastLoading}` alıyor ama bu tek başına yetmez:
    // disabled ancak bir sonraki render'da DOM'a yansır, arka arkaya iki tıklama araya
    // render girmeden gelebilir. İkinci istek birincinin yanıtını ezerdi (yavaş olan
    // sonra döner) ve kullanıcı tıklamadığı sayfada bulurdu kendini.
    if (pastLoading) return
    setPastLoading(true)
    setPastError(null)
    try {
      const data = await api.mySessions(hedef, PAST_PAGE_SIZE)
      setPastView(data.past)
    } catch (err) {
      // Hata AYRI tutulur: sayfa yükleme hatası, zaten görünen listeyi silmemeli.
      setPastError(err)
    } finally {
      setPastLoading(false)
    }
  }

  const hicDersYok =
    groups.action.length + groups.upcoming.length + groups.gecmisAcik.length + groups.past.length === 0

  return (
    /*
      ─────────────────────────────────────────────────────────────────────────
      EKRANA SABİT İKİ SÜTUN (2026-08-24).

      Eski düzen üç bölümü alt alta yığıyordu: aksiyon bekleyenler, yaklaşanlar, geçmiş.
      Otuz dersi olan bir kullanıcıda sayfa dört ekran boyu uzuyor ve "yarın dersim var
      mı" sorusu ile "geçen ay ne yapmıştım" sorusu aynı kaydırma çubuğunu paylaşıyordu —
      oysa bunlar birbirinden bağımsız iki iş.

      Artık lg üstünde iki sütun: SOLDA yaklaşan, SAĞDA geçmiş. Sayfanın kendisi
      kaymıyor; her sütun KENDİ İÇİNDE kayıyor. Geçmişi gezerken yaklaşan dersler
      ekranda kalıyor.

      lg ALTINDA sütun yok ve bu bilinçli: dar ekranda yan yana iki kaydırma alanı
      birbirini yiyor, parmak hangisini sürüklediğini şaşırıyor. Orada normal akış
      sürüyor (sayfa kayar, bölümler alt alta) — aynı içerik, ekrana uygun düzen.

      Yükseklik: 100dvh − 10.5rem. Üç parça: 4rem sabit üst bar + 3rem <main> dolgusu
      (py-6) + 3.5rem altbilgi (çerez tercihleri / turu yeniden başlat — Layout.jsx:280).
      Altbilgi ilk hesapta ATLANMIŞTI ve sayfa tam 56px kayıyordu: iki sütun ekrana
      sığıyor ama altbilgi taşıyordu, yani "sayfa kaymasın" kuralı sessizce bozuluyordu.
      Tarayıcıda ölçüldü.
      vh DEĞİL dvh: mobil adres çubuğu vh'ye dahil değil ve alt kenar kırpılırdı.
      ─────────────────────────────────────────────────────────────────────────
    */
    <div className="flex flex-col gap-4 lg:h-[calc(100dvh-10.5rem)] lg:min-h-[520px] lg:overflow-hidden">
      {/*
        SAYFA BAŞLIĞI. items-end (items-start değil): düğme, açıklama satırının değil
        BAŞLIĞIN ağırlık merkezine hizalanınca üst şerit tek bir yatay hat olarak
        okunuyor — başlık ile düğme arasında kayan bir basamak kalmıyor.
      */}
      <div className="flex shrink-0 flex-wrap items-end justify-between gap-x-4 gap-y-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Derslerim</h1>
          <p className="mt-1.5 max-w-prose text-sm text-slate-600">
            Ders almak ücretsizdir. Ders onaylandığında anlatan tarafa puan yazılır.
          </p>
        </div>
        <Button onClick={() => setBookOpen(true)}>
          <ArtiIkonu className="h-4 w-4" />
          Ders rezerve et
        </Button>
      </div>

      {notice && (
        <div className="shrink-0">
          <Notice tone="success" onDismiss={() => setNotice(null)}>
            {notice}
          </Notice>
        </div>
      )}

      <div className="shrink-0 empty:hidden">
        <ErrorBox error={sessions.error} onRetry={sessions.reload} />
      </div>

      {sessions.loading ? (
        <Loading />
      ) : hicDersYok ? (
        <EmptyState
          title="Henüz dersin yok"
          description="Kabul edilmiş bir eşleşmen varsa hemen ders saati belirleyebilirsin."
          action={<Button onClick={() => setBookOpen(true)}>Ders rezerve et</Button>}
        />
      ) : (
        <div className="grid gap-4 lg:min-h-0 lg:flex-1 lg:grid-cols-2">
          {/* ── SOL: YAKLAŞAN ─────────────────────────────────────────────── */}
          <Sutun
            baslik="Yaklaşan dersler"
            sayi={groups.action.length + groups.upcoming.length}
            vurgulu={groups.action.length > 0}
          >
            {/* Kesme SESSİZ olmaz: kullanıcı listenin tamamını görmediğini bilmeli. */}
            {activeTruncated && (
              <Notice tone="warning">
                {sessions.data.activeTotal} aktif dersinden ilk {sessions.data.active.length}{' '}
                tanesi gösteriliyor. Listeyi kısaltmak için tamamlanan dersleri onayla.
              </Notice>
            )}

            {/*
              AKSİYON BEKLEYENLER EN ÜSTTE ve ayrı bir başlık altında. Bunlar da aktif
              ders ama kullanıcıdan bir şey bekliyorlar; yaklaşanların arasına
              karıştırmak "bugün ne yapmam gerek" sorusunu görünmez yapardı.
            */}
            {groups.action.length > 0 && (
              <>
                <AltBaslik tone="amber" sayi={groups.action.length}>
                  Senden aksiyon bekleyenler
                </AltBaslik>
                {groups.action.map((s) => (
                  <SessionCard key={s.sessionId} session={s} onAction={setDialog} />
                ))}
              </>
            )}

            {groups.upcoming.length > 0 && (
              <>
                {groups.action.length > 0 && (
                  <AltBaslik sayi={groups.upcoming.length}>Planlanmış</AltBaslik>
                )}
                {groups.upcoming.map((s) => (
                  <SessionCard key={s.sessionId} session={s} onAction={setDialog} />
                ))}
              </>
            )}

            {groups.action.length + groups.upcoming.length === 0 && (
              <BosSutun
                baslik="Yaklaşan ders yok"
                metin="Eşleşmelerinden birine ders saati belirleyerek başla."
              />
            )}
          </Sutun>

          {/* ── SAĞ: GEÇMİŞ ───────────────────────────────────────────────── */}
          <Sutun
            baslik="Geçmiş dersler"
            sayi={pastTotal + groups.gecmisAcik.length}
            altBilgi={
              pastTotalPages > 1 ? (
                <Pagination
                  page={pastPage}
                  totalPages={pastTotalPages}
                  onChange={sayfayaGit}
                  disabled={pastLoading}
                />
              ) : null
            }
          >
            {/*
              SAATİ GEÇMİŞ AMA AÇIK DERSLER EN ÜSTTE ve ayrı başlıkla. Sayfalı geçmişin
              İÇİNE karıştırılmadılar: `past` sunucudan sayfalı geliyor, araya istemci
              tarafında satır sokmak sayfa sayılarını yalanlardı. Üstte sabit durunca hem
              sayfalamadan etkilenmiyorlar hem de kullanıcı "bu ders oldu mu, ne oldu?"
              sorusunu ilk bakışta görüyor.
            */}
            {groups.gecmisAcik.length > 0 && (
              <>
                <AltBaslik tone="amber" sayi={groups.gecmisAcik.length}>
                  Saati geçti, hâlâ açık
                </AltBaslik>
                {groups.gecmisAcik.map((s) => (
                  <SessionCard key={s.sessionId} session={s} onAction={setDialog} />
                ))}
              </>
            )}

            {groups.past.length > 0 ? (
              <>
                {groups.gecmisAcik.length > 0 && (
                  <AltBaslik sayi={pastTotal}>Tamamlananlar</AltBaslik>
                )}
                {groups.past.map((s) => (
                  <SessionCard key={s.sessionId} session={s} onAction={setDialog} past />
                ))}
              </>
            ) : (
              groups.gecmisAcik.length === 0 && (
                <BosSutun
                  baslik="Geçmiş ders yok"
                  metin="Tamamlanan dersler onaylandıktan sonra burada birikir."
                />
              )
            )}

            <ErrorBox error={pastError} onRetry={() => sayfayaGit(pastPage)} />

            {/*
              PUAN GEÇMİŞİ SAĞ SÜTUNUN İÇİNDE.

              Eskiden iki listenin ALTINDA, sayfanın devamındaydı. Sayfa artık kaymadığı
              için "altında" diye bir yer kalmadı; defteri sağ sütuna almak ayrıca doğru
              yer: her puan hareketinin kaynağı tamamlanmış bir derstir, yani tam olarak
              bu sütunda duran şey. Ayrı bir sekme aynı olayı iki yere bölerdi.
            */}
            <PointHistory />
          </Sutun>
        </div>
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
          onBooked={(code, mintAmount) =>
            refresh(
              // Öğrenci hiçbir şey ödemiyor; gösterilen sayı EĞİTMENİN kazanacağı puan
              // ve SUNUCUDAN geliyor. (Modalin özet şeridi aynı sayının yalnızca bir
              // ÖNİZLEMESİNİ gösterir — bkz. BookModal'daki gösterim sabitleri;
              // bağlayıcı olan her zaman buradaki mintAmount'tur.)
              `Ders rezerve edildi (eğitmen ${mintAmount} puan kazanacak). ` +
                `Doğrulama kodun: ${code} — ders ekran görüntüsünde görünmeli.`,
            )
          }
        />
      )}

      {dialog?.type === 'complete' && (
        <CompleteModal
          key={dialog.session.sessionId}
          session={dialog.session}
          onClose={() => setDialog(null)}
          onDone={() => refresh('Kanıt yüklendi. Ders karşı tarafın onayına gönderildi.')}
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
                'sonucu bu ekrandan takip edebilirsin.',
            )
          }
        />
      )}

      

      {dialog?.type === 'cancel' && (
        <CancelModal
          key={dialog.session.sessionId}
          session={dialog.session}
          onClose={() => setDialog(null)}
          onDone={() => refresh('Ders iptal edildi.')}
        />
      )}
    </div>
  )
}


/*
  SÜTUN KABUĞU — başlık sabit, gövde kayar.

  ZEMİN BİLEREK BEYAZ DEĞİL. Sayfa zemini slate-50, kartlar cam (beyaza yakın); ikisinin
  arasına hiçbir şey konmadığında ekran "beyazımsı bir alanda yüzen beyaz kartlar" gibi
  duruyordu — sınırların nerede olduğu belli değildi. Sütun gövdesi bir ton daha koyu
  (slate-100/60), böylece cam kartlar ondan AYRILIYOR ve sütunun nerede bitip nerede
  başladığı görünüyor. Başlık şeridi beyaz kalıyor: kayan gövdenin üstünde sabit duran
  bir yüzey, gövdeden farklı olmalı ki "bu satır kaymıyor" anlaşılsın.

  BAŞLIK BÜYÜDÜ (text-sm → text-base) ve slate-900'a çıktı. Eski hâlde sütun başlığı,
  içindeki kartların başlığıyla (h3, text-base) aynı puntodaydı ve ondan daha soluktu —
  yani hiyerarşide ALTINDA görünüyordu. Bir bölüm başlığı, kapsadığı şeyden zayıf
  olamaz; sayfanın "aşırı karışık" okunmasının sebeplerinden biri buydu.

  min-h-0 ŞART: flex çocuğunun varsayılan min-height'ı `auto`, yani içerik kadar. Onu
  sıfırlamadan `overflow-y-auto` hiçbir şey yapmaz — sütun içeriği kadar uzar ve
  kaydırma sayfaya taşar. Bu, ekrana sabitlenen her düzenin ilk tuzağı.
*/
function Sutun({ baslik, sayi, children, altBilgi = null, vurgulu = false }) {
  return (
    <section className="flex min-h-0 flex-col overflow-hidden rounded-2xl border border-slate-200/70 bg-slate-100/60">
      <header className="flex shrink-0 items-center justify-between gap-3 border-b border-slate-200/70 bg-white px-5 py-3.5">
        {/*
          uppercase KALKTI: sütun başlığı bir kez okunan bir yer etiketi, sürekli okunan
          içerik değil. Büyük harf + harf aralığı onu bağıran bir şeride çeviriyordu ve
          alt gruplama başlıklarıyla (AltBaslik) aynı dili konuşuyordu — iki seviye
          birbirinden ayırt edilemiyordu. Normal yazım görsel ağırlığı kartlara bırakıyor;
          mikro etiket dili artık yalnızca AltBaslik'ta.
        */}
        <h2 className="truncate text-base font-semibold tracking-tight text-slate-900">{baslik}</h2>
        {/*
          Sayaç ROZETİ — ui.jsx'teki Badge'in ta kendisi, elle yazılmış bir pil değil.
          Eskiden burada kendi sınıflarıyla kurulmuş bir <span> vardı ve rozet dili
          sayfada iki yerde ayrı ayrı tanımlanıyordu; Badge tonları değişse bu sayaç
          geride kalırdı. tabular-nums ek olarak veriliyor: sayaç 9'dan 10'a çıkarken
          rozetin genişliği zıplamasın.

          Aksiyon bekleyen ders varsa sol sütunun sayacı amber'a dönüyor: kullanıcı
          sütunun içine bakmadan da "burada iş var" bilgisini alıyor. Renk tek sinyal
          değil — aksiyon grubunun kendi başlığı da listede duruyor.
        */}
        <Badge tone={vurgulu ? 'warning' : 'neutral'} className="shrink-0 font-semibold tabular-nums">
          {sayi}
        </Badge>
      </header>

      {/*
        kaydirma-ince (index.css): gövde kendi içinde kayıyor ve tarayıcının varsayılan
        kalın çubuğu sütun kenarında koca gri bir blok bırakıyordu — ince, yarı saydam
        çubuk kaydırılabilirliği söylemeye yetiyor.

        p-4 → p-4 sm:p-5 ve space-y-3 → space-y-4 (kartlar arası 16px). Kartların iç
        dolgusu p-5'e çıktığı için sütun dolgusunun da nefes alması gerekiyordu; 16px
        dolgunun içinde 20px dolgulu kartlar, kart kenarını sütun kenarından daha dar
        gösteriyor ve "sıkışık" hissini üretiyordu.

        DİKKAT — flex-col + gap DENENMEMELİ: bu kutu aynı zamanda `overflow-y-auto` ve
        kendisi de bir flex çocuğu. İçini flex yapmak kartları flex öğesine çevirir ve
        sütun dolduğunda tarayıcı onları sıkıştırmayı deneyebilir. space-y sadece margin
        koyuyor, kutu modeline hiç dokunmuyor — kaydırma davranışı tartışmasız kalıyor.
      */}
      <div className="kaydirma-ince min-h-0 flex-1 space-y-4 overflow-y-auto overscroll-contain p-4 sm:p-5">
        {children}
      </div>

      {altBilgi && (
        <div className="shrink-0 border-t border-slate-200/70 bg-white px-4 py-2.5">{altBilgi}</div>
      )}
    </section>
  )
}

/**
 * Sütun içindeki gruplama başlığı — etiket + sayaç.
 *
 * Sütun başlığı normal yazımda ve slate-900'da; mikro etiket dili (küçük punto + büyük
 * harf) yalnızca burada kaldı, yani göz "sütun mu, grup mu" ayrımını yazım biçiminden
 * yapıyor.
 *
 * SAYI ARTIK METNİN İÇİNDE DEĞİL, AYRI ROZETTE. Eskiden çağıran taraf başlığa
 * "Planlanmış (3)" diye elle parantez ekliyordu: parantezli sayı etiketin bir parçası gibi
 * okunuyor, sütun başlığındaki rozetle de aynı dili konuşmuyordu. Sayı ayrılınca sayfadaki
 * her sayaç aynı biçimde görünüyor — sütun başlığında da, grup başlığında da rozet.
 *
 * Yanındaki ince çizgi (flex-1 + border-t) grubu görsel olarak da açıyor: etiketten sonra
 * devam eden hat, altındaki kartların bu başlığa ait olduğunu söylüyor.
 */
function AltBaslik({ children, sayi, tone = 'slate' }) {
  const amber = tone === 'amber'

  return (
    <div className="flex items-center gap-2.5 pt-1">
      <p
        className={`shrink-0 text-xs font-semibold uppercase tracking-wider ${
          amber ? 'text-amber-700' : 'text-slate-600'
        }`}
      >
        {children}
      </p>
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
 * Sütun boşken görünen kısa metin.
 *
 * ui.jsx'teki EmptyState KULLANILMADI: o, sayfanın tamamı boşken çıkan büyük bir blok
 * (başlık + açıklama + eylem düğmesi) ve bir sütunun içinde orantısız duruyor. Burada
 * boşluk bir hata değil, normal bir durum — o kadar yer kaplamamalı.
 *
 * Metin tonları slate-600'ün altına İNMİYOR: kutu yarı saydam bir zeminin (bg-white/60)
 * üstünde duruyor ve altından sütunun slate-100'ü geçiyor; slate-500 bu bileşimde AA'ya
 * dayanmıyordu. Ayrım artık renkten değil ağırlıktan geliyor (font-medium / normal).
 */
function BosSutun({ baslik, metin }) {
  return (
    <div className="rounded-2xl border border-dashed border-slate-300 bg-white/60 px-5 py-10 text-center">
      <p className="text-sm font-semibold text-slate-700">{baslik}</p>
      <p className="mx-auto mt-1.5 max-w-xs text-sm text-slate-600">{metin}</p>
    </div>
  )
}

const HISTORY_PAGE_SIZE = 20

/**
 * Puan geçmişi (eski Cüzdan ekranının defteri).
 *
 * AÇILINCA YÜKLENİR. Derslerim zaten iki istek atıyor; defter kullanıcıların çoğunun her
 * ziyarette bakmadığı bir kayıt. Kapalı dururken sıfır maliyeti var, açıldığında tek
 * sayfa geliyor.
 */
function PointHistory() {
  const [open, setOpen] = useState(false)
  const [rows, setRows] = useState([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  async function loadPage(next) {
    // "Daha eski hareketler" ekleyerek çalışıyor: çift tıklama AYNI sayfayı iki kez
    // eklerdi ve defterde her satır çift görünürdü. Butonun `loading` ile kilitlenmesi
    // bir sonraki render'a kadar geçerli olmadığı için muhafız burada, fonksiyonun
    // başında duruyor.
    if (loading) return
    setLoading(true)
    setError(null)
    try {
      const result = await api.statement(next, HISTORY_PAGE_SIZE)
      // Sayfa EKLENİR, değiştirilmez: "daha fazla" akışında önceki satırlar kaybolmamalı.
      setRows((prev) => (next === 1 ? result.items : [...prev, ...result.items]))
      setTotal(result.totalCount)
      setPage(next)
    } catch (err) {
      setError(err)
    } finally {
      setLoading(false)
    }
  }

  function toggle() {
    const next = !open
    setOpen(next)
    if (next && page === 0) loadPage(1)
  }

  return (
    <section>
      {/*
        CamKart yüzeyinin sınıf kopyası: defterin kapağı da sütundaki ders kartlarıyla aynı
        ailede dursun — ders kartları cama geçtiğinde bu kapak beyaz kalsaydı sayfadaki tek
        "başka türlü" kutu olurdu. CamKart bileşeni KULLANILAMADI çünkü bu bir <button>:
        div'in içine buton sarmak tıklanabilir alanı daraltır, aria-expanded da yüzeyin
        kendisinde durmalı.

        px-5 py-4 (eski px-4 py-3): ders kartlarının dolgusu p-5'e çıktı, kapak da aynı
        ritimde nefes almalı.
      */}
      <button
        type="button"
        onClick={toggle}
        aria-expanded={open}
        className="flex min-h-11 w-full items-center justify-between gap-3 rounded-2xl border border-white/70
                   bg-white/80 px-5 py-4 text-left shadow-sm shadow-brand-900/5 ring-1 ring-inset ring-white/40
                   transition hover:bg-white supports-[backdrop-filter]:backdrop-blur-xl"
      >
        <span className="min-w-0">
          <span className="block text-sm font-semibold text-slate-900">Puan geçmişi</span>
          {/*
            Açıklama ARTIK ALT SATIRDA (eski ml-2 ile aynı satırdaydı): dar sütunda başlıkla
            aynı satıra sığmıyor ve "Puan geçmişi Her hareketin hangi…" diye tek cümle gibi
            okunuyordu. slate-600, cam yüzey üstündeki kontrast alt sınırı.
          */}
          <span className="mt-0.5 block text-xs text-slate-600">
            Her hareketin hangi dersten geldiği
          </span>
        </span>
        {/*
          Üçgen KARAKTERLERİ (▲ / ▼) çıktı, tek bir ok İKONU girdi. O karakterler yazı
          tipinden geliyordu: yazı tipi değişince boyu ve ağırlığı değişiyor, satırın
          rengini ancak tesadüfen alıyor ve bazı Windows yazı tiplerinde kutu olarak
          çiziliyordu. Tek çizim + rotate-180 hem durum değişimini animasyonla anlatıyor
          hem de "açık" ile "kapalı" arasında geometri farkı bırakmıyor.
        */}
        <OkAsagiIkonu
          className={`h-5 w-5 shrink-0 text-slate-500 transition-transform duration-200 ${
            open ? 'rotate-180' : ''
          }`}
        />
      </button>

      {open && (
        <div className="mt-3">
          <ErrorBox error={error} onRetry={() => loadPage(page || 1)} />

          {loading && rows.length === 0 ? (
            <Loading />
          ) : rows.length === 0 && !error ? (
            <CamKart>
              <p className="text-sm text-slate-600">
                Henüz puan hareketin yok. Bir ders anlatıp onaylandığında ilk kaydın burada belirir.
              </p>
            </CamKart>
          ) : (
            /*
              TEK kart, İNCE AYRAÇLAR. Eskiden her hareket kendi kenarlıklı kutusundaydı ve
              yirmi satırlık defter yirmi küçük karta bölünüyordu — oysa defter tek bir
              belgedir, kart koleksiyonu değil. divide-y satırları ayırıyor, kart kenarı
              belgeyi sarıyor; "daha eski" düğmesi de belgenin altbilgisi gibi içeride
              duruyor ki defter uzadıkça düğme ondan kopup boşlukta yüzmesin.
            */
            <CamKart className="overflow-hidden !p-0">
              <div className="divide-y divide-slate-200/70">
                {rows.map((row, i) => (
                  <HistoryRow key={`${row.createdAtUtc}-${i}`} row={row} />
                ))}
              </div>

              {rows.length < total && (
                <div className="flex justify-center border-t border-slate-200/70 px-5 py-3.5">
                  <Button variant="secondary" loading={loading} onClick={() => loadPage(page + 1)}>
                    Daha eski hareketler ({rows.length}/{total})
                  </Button>
                </div>
              )}
            </CamKart>
          )}
        </div>
      )}
    </section>
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
            hiçbir şey anlatmıyor. Ders bilgisi yoksa (hoş geldin puanı gibi) satır
            yalnızca türüyle kalır. */}
        <p className="truncate text-xs text-slate-600">
          {row.topicName
            ? `${row.topicName}${row.counterpartDisplayName ? ` · ${row.counterpartDisplayName}` : ''}`
            : formatDateTime(row.createdAtUtc)}
        </p>
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
          title="Kabul edilmiş eşleşmen yok"
          description="Önce Keşfet sayfasından istek gönder ve karşı tarafın kabul etmesini bekle."
        />
      ) : bookable.length === 0 ? (
        <EmptyState
          title="Bu eşleşmelerde sana anlatılacak konu yok"
          description="Aktif eşleşmelerinde ders anlatan taraf sensin. Ders almak için Keşfet'ten yeni bir istek gönder."
        />
      ) : (
        <>
          <form onSubmit={submit} id="book-form" className="space-y-4">
            {/*
              1. GRUP — eşleşme ve konu. Modal üç katlı bir hiyerarşi anlatıyor: önce
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
                  <p className="text-sm font-semibold text-slate-800">Eşleşme ve konu</p>
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
                aria-label="Eşleşme"
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
                      <span className="font-normal text-brand-700/70">Eşleşme seçilmedi</span>
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
