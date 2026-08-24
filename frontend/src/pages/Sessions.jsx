import { useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { useWallet } from '../state/WalletContext'
import { AnalyticsEvents, trackEvent } from '../lib/analytics'
import { ReviewModal } from '../components/ReviewModal'
import { PersonLink } from '../components/PersonLink'
import {
  REPORT_REASON_LABELS,
  SESSION_STATUS_LABELS,
  TRANSACTION_LABELS,
  formatDateTime,
  remainingText,
  signedCredit,
} from '../lib/format'
import { Badge, Button, Card, EmptyState, ErrorBox, Field, Loading, Modal, Notice, Pagination } from '../components/ui'

/*
  DURUM → TON TABLOSU. TEK KAYNAK.

  Neden tablo, neden koşullu sınıf değil: durum başına üç ayrı görsel karar var (sol şerit
  rengi, rozet tonu, vurgu metni rengi) ve bunlar kartın üç ayrı yerinde kullanılıyor.
  JSX'in içine serpiştirilmiş üçlü koşullar olsaydı yeni bir durum eklendiğinde üç yerin
  hepsini bulmak gerekirdi; biri unutulduğunda hata sessiz olurdu — kart yanlış renkte
  çizilir, hiçbir şey patlamaz.

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
*/
const DURUM_STILI = {
  Booked: { serit: 'border-l-brand-500', rozet: 'brand', vurgu: 'text-brand-600' },
  AwaitingApproval: { serit: 'border-l-amber-400', rozet: 'warning', vurgu: 'text-amber-700' },
  Completed: { serit: 'border-l-emerald-500', rozet: 'success', vurgu: 'text-emerald-700' },
  Disputed: { serit: 'border-l-rose-500', rozet: 'danger', vurgu: 'text-rose-700' },
  Cancelled: { serit: 'border-l-rose-300', rozet: 'danger', vurgu: 'text-rose-700' },
  Expired: { serit: 'border-l-slate-300', rozet: 'neutral', vurgu: 'text-slate-600' },
}

// Sunucu tanımadığımız bir durum döndürürse kart RENKSİZ değil NÖTR çizilir: eksik bir
// eşleşme yüzünden `undefined` sınıf adı basıp kartın kenarlığını tamamen kaybetmeyelim.
const VARSAYILAN_DURUM_STILI = { serit: 'border-l-slate-300', rozet: 'neutral', vurgu: 'text-slate-600' }

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
      <div className="flex shrink-0 flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Derslerim</h1>
          <p className="mt-1 text-sm text-slate-600">
            Ders almak ücretsizdir. Ders onaylandığında anlatan tarafa puan yazılır.
          </p>
        </div>
        <Button onClick={() => setBookOpen(true)}>+ Ders rezerve et</Button>
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
                <AltBaslik tone="amber">Senden aksiyon bekleyenler ({groups.action.length})</AltBaslik>
                {groups.action.map((s) => (
                  <SessionCard key={s.sessionId} session={s} onAction={setDialog} />
                ))}
              </>
            )}

            {groups.upcoming.length > 0 && (
              <>
                {groups.action.length > 0 && <AltBaslik>Planlanmış ({groups.upcoming.length})</AltBaslik>}
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
                <AltBaslik tone="amber">
                  Saati geçti, hâlâ açık ({groups.gecmisAcik.length})
                </AltBaslik>
                {groups.gecmisAcik.map((s) => (
                  <SessionCard key={s.sessionId} session={s} onAction={setDialog} />
                ))}
              </>
            )}

            {groups.past.length > 0 ? (
              <>
                {groups.gecmisAcik.length > 0 && <AltBaslik>Tamamlananlar ({pastTotal})</AltBaslik>}
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
              // Öğrenci hiçbir şey ödemiyor; gösterilen tek sayı EĞİTMENİN kazanacağı
              // puan ve o da sunucudan geliyor (istemcide formül kopyası tutulmuyor).
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

  ZEMİN BİLEREK BEYAZ DEĞİL. Sayfa zemini slate-50, kartlar beyaz; ikisinin arasına
  hiçbir şey konmadığında ekran "beyaz kartların yüzdüğü beyazımsı bir alan" gibi
  duruyordu — sınırların nerede olduğu belli değildi. Sütun gövdesi bir ton daha koyu
  (slate-100/60), böylece beyaz kartlar ondan AYRILIYOR ve sütunun nerede bitip
  nerede başladığı görünüyor. Başlık şeridi beyaz kalıyor: kayan gövdenin üstünde
  sabit duran bir yüzey, gövdeden farklı olmalı ki "bu satır kaymıyor" anlaşılsın.

  min-h-0 ŞART: flex çocuğunun varsayılan min-height'ı `auto`, yani içerik kadar. Onu
  sıfırlamadan `overflow-y-auto` hiçbir şey yapmaz — sütun içeriği kadar uzar ve
  kaydırma sayfaya taşar. Bu, ekrana sabitlenen her düzenin ilk tuzağı.
*/
function Sutun({ baslik, sayi, children, altBilgi = null, vurgulu = false }) {
  return (
    <section className="flex min-h-0 flex-col overflow-hidden rounded-2xl border border-slate-200/70 bg-slate-100/60">
      <header className="flex shrink-0 items-center justify-between gap-2 border-b border-slate-200/70 bg-white px-4 py-3">
        {/*
          uppercase KALKTI: sütun başlığı bir kez okunan bir yer etiketi, sürekli okunan
          içerik değil. Büyük harf + harf aralığı onu bağıran bir şeride çeviriyordu ve
          alt gruplama başlıklarıyla (AltBaslik) aynı dili konuşuyordu — iki seviye
          birbirinden ayırt edilemiyordu. Normal yazım görsel ağırlığı kartlara bırakıyor;
          mikro etiket dili artık yalnızca AltBaslik'ta.
        */}
        <h2 className="text-sm font-semibold text-slate-800">{baslik}</h2>
        {/*
          Sayaç rozeti. Aksiyon bekleyen ders varsa sol sütunun sayacı amber'a dönüyor:
          kullanıcı sütunun içine bakmadan da "burada iş var" bilgisini alıyor. Renk tek
          sinyal değil — aksiyon grubunun kendi başlığı da listede duruyor.
        */}
        <span
          className={`rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums ${
            vurgulu ? 'bg-amber-100 text-amber-800' : 'bg-slate-100 text-slate-600'
          }`}
        >
          {sayi}
        </span>
      </header>

      {/*
        kaydirma-ince (index.css): gövde kendi içinde kayıyor ve tarayıcının varsayılan
        kalın çubuğu sütun kenarında koca gri bir blok bırakıyordu — ince, yarı saydam
        çubuk kaydırılabilirliği söylemeye yetiyor. p-4 SABİT (eski p-3 sm:p-4 değil):
        kartların iç dolgusu da 16px, iki ritim aynı olunca kart kenarı her kırılımda
        sütun kenarıyla aynı boşluğu bırakıyor — "boşluklar tutarsız" şikâyetinin kaynağı
        tam bu tür yarım adımlardı.
      */}
      <div className="kaydirma-ince min-h-0 flex-1 space-y-3 overflow-y-auto overscroll-contain p-4">
        {children}
      </div>

      {altBilgi && (
        <div className="shrink-0 border-t border-slate-200/70 bg-white px-3 py-2">{altBilgi}</div>
      )}
    </section>
  )
}

/**
 * Sütun içindeki gruplama başlığı. Sütun başlığı normal yazıma inince mikro etiket dili
 * (küçük punto + büyük harf) yalnızca burada kaldı — göz "sütun mu, grup mu" ayrımını
 * yazım biçiminden yapıyor. font-medium (semibold değil): bu bir etiket, vurgu kartların
 * işi; amber ton "topun sende" gruplarını yine ayırıyor, renk tek sinyal değil çünkü
 * sayı ve metin de yanında.
 */
function AltBaslik({ children, tone = 'slate' }) {
  return (
    <p
      className={`px-0.5 pt-1 text-xs font-medium uppercase tracking-wider ${
        tone === 'amber' ? 'text-amber-700' : 'text-slate-500'
      }`}
    >
      {children}
    </p>
  )
}

/**
 * Sütun boşken görünen kısa metin.
 *
 * ui.jsx'teki EmptyState KULLANILMADI: o, sayfanın tamamı boşken çıkan büyük bir blok
 * (başlık + açıklama + eylem düğmesi) ve bir sütunun içinde orantısız duruyor. Burada
 * boşluk bir hata değil, normal bir durum — o kadar yer kaplamamalı.
 */
function BosSutun({ baslik, metin }) {
  return (
    <div className="rounded-xl border border-dashed border-slate-300 bg-white/60 px-4 py-8 text-center">
      <p className="text-sm font-medium text-slate-600">{baslik}</p>
      <p className="mt-1 text-xs text-slate-500">{metin}</p>
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
        Card yüzeyinin birebir sınıf kopyası (rounded-2xl + border-slate-100 + shadow-sm):
        defterin kapağı da sütundaki kartlarla aynı ailede dursun — eski rounded-xl +
        koyu kenarlık onu sayfadaki tek "başka türlü" kutu yapıyordu. Card bileşeni
        KULLANILAMADI çünkü bu bir <button>: div'in içine buton sarmak tıklanabilir alanı
        daraltır, aria-expanded da yüzeyin kendisinde durmalı.
      */}
      <button
        type="button"
        onClick={toggle}
        aria-expanded={open}
        className="flex min-h-11 w-full items-center justify-between gap-3 rounded-2xl border border-slate-100
                   bg-white px-4 py-3 text-left shadow-sm transition hover:border-slate-200 hover:bg-slate-50"
      >
        <span className="min-w-0">
          <span className="text-sm font-semibold text-slate-900">Puan geçmişi</span>
          <span className="ml-2 text-xs text-slate-500">
            Her hareketin hangi dersten geldiği
          </span>
        </span>
        <span aria-hidden="true" className="shrink-0 text-slate-400">{open ? '▲' : '▼'}</span>
      </button>

      {open && (
        <div className="mt-3">
          <ErrorBox error={error} onRetry={() => loadPage(page || 1)} />

          {loading && rows.length === 0 ? (
            <Loading />
          ) : rows.length === 0 && !error ? (
            /* !p-4: sütun içindeki kartların ortak dolgusu — defter de aynı ritimde. */
            <Card className="!p-4">
              <p className="text-sm text-slate-600">
                Henüz puan hareketin yok. Bir ders anlatıp onaylandığında ilk kaydın burada belirir.
              </p>
            </Card>
          ) : (
            /*
              TEK Card, İNCE AYRAÇLAR. Eskiden her hareket kendi kenarlıklı kutusundaydı ve
              yirmi satırlık defter yirmi küçük karta bölünüyordu — oysa defter tek bir
              belgedir, kart koleksiyonu değil. divide-y satırları ayırıyor, kart kenarı
              belgeyi sarıyor; "daha eski" düğmesi de belgenin altbilgisi gibi içeride
              duruyor ki defter uzadıkça düğme ondan kopup boşlukta yüzmesin.
            */
            <Card className="overflow-hidden !p-0">
              <div className="divide-y divide-slate-100">
                {rows.map((row, i) => (
                  <HistoryRow key={`${row.createdAtUtc}-${i}`} row={row} />
                ))}
              </div>

              {rows.length < total && (
                <div className="flex justify-center border-t border-slate-100 px-4 py-3">
                  <Button variant="secondary" loading={loading} onClick={() => loadPage(page + 1)}>
                    Daha eski hareketler ({rows.length}/{total})
                  </Button>
                </div>
              )}
            </Card>
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
      tek Card'ın içinde divide-y ile ayrılıyor. Kenarlık ve köşe yuvarlama defterin
      sınırında bir kez çiziliyor — satır başına tekrarlamak görsel gürültüden ibaretti.
    */
    <div className="flex items-center justify-between gap-3 px-4 py-2.5">
      <div className="min-w-0">
        <p className="truncate text-sm font-medium text-slate-900">
          {TRANSACTION_LABELS[row.type] ?? row.type}
        </p>
        {/* Konu ve karşı taraf sunucudan geliyor: çıplak bir ders kimliği kullanıcıya
            hiçbir şey anlatmıyor. Ders bilgisi yoksa (hoş geldin puanı gibi) satır
            yalnızca türüyle kalır. */}
        <p className="truncate text-xs text-slate-500">
          {row.topicName
            ? `${row.topicName}${row.counterpartDisplayName ? ` · ${row.counterpartDisplayName}` : ''}`
            : formatDateTime(row.createdAtUtc)}
        </p>
      </div>

      <div className="shrink-0 text-right">
        <div className={`text-sm font-semibold ${kazanc ? 'text-emerald-600' : 'text-slate-500'}`}>
          {signedCredit(row.amount)}
        </div>
        <div className="text-xs text-slate-400">{formatDateTime(row.createdAtUtc)}</div>
      </div>
    </div>
  )
}

/**
 * Meta şeridindeki nokta ayracı. slate-300: ayraç mobilyadır, içerik değil — içerikle
 * aynı tonda olduğunda satır "kelime kelime kelime" diye tek blok okunuyordu, soluk
 * ayraç parçaları birbirinden koparıyor. Tek bileşen olması tutarlılık için: kartta kaç
 * ayraç varsa hepsi aynı karakter ve aynı tonda.
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
  bilgisi. Renk yine tek sinyal değil: kum saati ve metin bilgiyi kendisi taşıyor.
*/
function UyariSatiri({ children }) {
  return (
    <p className="mt-2 flex items-start gap-1.5 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
      <span aria-hidden="true" className="shrink-0">
        ⏳
      </span>
      <span>{children}</span>
    </p>
  )
}

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
    /*
      Sol şerit (border-l-4) Card'ın kendi border-slate-200/80 kenarlığını yalnızca SOL
      kenarda eziyor; kalan üç kenar kart dilinin bir parçası olarak duruyor. Şerit,
      listeyi taramakta olan gözün durumu OKUMADAN ayırt etmesini sağlıyor — okuma
      rozetle yapılıyor.

      GÖLGE ARTIK EZİLMİYOR: Card'ın kendi varsayılanı shadow-sm oldu (ui.jsx, yüzey
      dili revizyonu), yani buradaki eski `!shadow-sm` gereksizleşti ve kaldırıldı.
      Yalnızca hover ağırlığı ekleniyor — çakışma olmadığı için `!` de gerekmiyor.

      p-4 (Card'ın p-5'i yerine): sütun genişliği tam sayfadan dar, aynı dolgu içeriği
      sıkıştırıyordu.
    */
    <Card className={`border-l-4 !p-4 transition-shadow duration-200 hover:shadow-md ${stil.serit}`}>
      {/*
        Üst satır: SOLDA kimlik (konu + karşı taraf), SAĞDA durum.
        Eski düzende konu adı iki rozetin arasına sıkışmış bir <span>'di ve kart
        "beyaz bir etiket yığını" gibi okunuyordu. Konu artık başlık: kullanıcı listede
        önce hangi dersi aradığını arar, durumunu sonra kontrol eder.
      */}
      <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-2">
        {/*
          `grow basis-48` (flex-1 DEĞİL): flex-1 temel genişliği 0 yapar ve dar ekranda
          rozetler konu adını birkaç karaktere kadar ezerdi. 12rem'lik bir taban, yer
          kalmadığında rozetleri ALT SATIRA itiyor — başlık okunur kalıyor.
        */}
        <div className="min-w-0 grow basis-48">
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
          */}
          <h3 className="line-clamp-2 text-base font-semibold text-slate-900 sm:line-clamp-1">
            {session.topicName}
          </h3>
          <p className="mt-0.5 truncate text-sm text-slate-600">
            <PersonLink userId={session.otherUserId} className="font-medium text-brand-700">
              {session.otherDisplayName}
            </PersonLink>{' '}
            ile · {session.subjectName}
          </p>
        </div>

        <div className="flex shrink-0 flex-wrap items-center gap-1.5">
          {/*
            Rol rozeti bilinçli olarak NÖTR. Renk bu kartta tek bir şeyi anlatıyor:
            dersin durumunu. Rol de renkliyse (eski hâlde yeşil/mavi) iki ayrı anlam
            aynı sinyali paylaşır ve "yeşil = tamamlandı" öğrenilemez hâle gelirdi.
          */}
          <Badge tone="neutral">{session.iAmTutor ? 'Anlatıyorum' : 'Alıyorum'}</Badge>
          <Badge tone={stil.rozet}>
            {SESSION_STATUS_LABELS[session.status] ?? session.status}
          </Badge>
        </div>
      </div>

      {/*
        Meta şeridi: tarih/süre/puan. text-xs + slate-500 ile bilinçli olarak SOLUK —
        bunlar doğrulama bilgisi, karar bilgisi değil. Üstündeki ince çizgi kimliği
        metadan ayırıyor.
      */}
      <p className="mt-3 flex flex-wrap items-center gap-x-2 gap-y-1 border-t border-slate-100 pt-3 text-xs text-slate-500">
        <span>{formatDateTime(session.scheduledStartUtc)}</span>
        <Ayrac />
        <span>{session.durationMinutes} dk</span>
        <Ayrac />
        {/* Her ders puan basar; gösterilen sayı sunucudan gelen mintAmount'tur. */}
        <span>{session.mintAmount} puan</span>
        {startsIn && !past && (
          <>
            <Ayrac />
            <span className={`font-medium ${stil.vurgu}`}>{startsIn} sonra</span>
          </>
        )}
      </p>

      {showCode && (
        /*
          Kod artık TEK SATIR + chip. Eski kutu iki cümlelik açıklama taşıyordu ve her
          aktif kartta aynı paragraf tekrar ediyordu — açıklama baskın, kod kaybolandı.
          Açıklamanın tam hâli zaten kararın verildiği yerde duruyor (CompleteModal ve
          ApproveModal'daki Notice), kartta yalnızca kodu hatırlatmak yeter. title
          masaüstünde kısa hatırlatma verir; dokunmatikte tooltip yok ama bilgi kaybı da
          yok — kanıt yükleme/onay akışı aynı metni Notice olarak gösteriyor.
        */
        <p
          className="mt-3 flex flex-wrap items-center gap-2 text-xs text-slate-500"
          title={
            session.iAmTutor
              ? 'Ders ekran görüntüsünde bu kod, sistem saati ve katılımcı listesi görünmeli.'
              : 'Kanıt görselinde bu kodun göründüğünü doğrula.'
          }
        >
          <span>Doğrulama kodu</span>
          <code className="rounded-md bg-slate-100 px-2 py-0.5 font-mono text-sm font-semibold tracking-wider text-slate-800">
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

      {/*
        Aksiyonlar kartın ALTINDA ve sağa yaslı. Eski düzende sağ sütundaydılar ve
        dar ekranda metnin ortasına düşüp okuma akışını kesiyorlardı. Alt sıra ayrıca
        her kartta aynı yerde duruyor — art arda üç kartta göz aynı noktayı arar.
      */}
      <div className="mt-4 flex flex-wrap items-center justify-end gap-2">
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
    </Card>
  )
}

/**
 * Çift taraflı onayın anlamlı olduğu yer: puan BASILMADAN önce öğrenci kanıtı görür.
 * (Eskiden buradaki risk öğrencinin kredisiydi; artık öğrencinin kaybedeceği bir şey yok,
 * ama onay hâlâ şart — basımın tek meşru tetikleyicisi dersin gerçekten yapılmış olması.)
 * Kanıt görseli Authorization başlığı gerektirdiği için blob olarak indirilip object URL'e çevrilir.
 */
function ApproveModal({ session, onClose, onApproved, onReport }) {
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
            {latestProof.isDuplicateHash && (
              <div className="rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800">
                ⚠️ Bu görsel <strong>başka bir derste de kullanılmış</strong>. Sahte kanıt olabilir —
                dikkatle incele.
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

            <p className="text-xs text-slate-500">
              Yükleme: {formatDateTime(latestProof.uploadedAtUtc)} · Büyütmek için görsele tıkla.
            </p>
          </div>
        )}

        <ErrorBox error={error} />

        <div className="flex flex-wrap justify-end gap-2">
          {/* Onay uçarken diğer iki düğme de kapalı: kullanıcı basım devam ederken
              şikayet ekranına geçerse aynı ders hem onaylanmış hem şikayet edilmiş
              olur ve hangi sonucun geçerli olduğu tıklama sırasına kalırdı. */}
          <Button variant="secondary" disabled={onaylaniyor} onClick={onClose}>
            Sonra karar ver
          </Button>
          <Button variant="danger" disabled={onaylaniyor} onClick={onReport}>
            Şikayet et
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
            <Field
              label="Eşleşme"
              hint="Dersi alan taraf sensin; listelenen konu karşı tarafın sana anlatacağı konudur."
            >
              <select
                className="input"
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
            </Field>

            {/*
              Kutu artık TEK biçimli. Eskiden gönüllülük durumuna göre yeşile boyanıyor
              ve "eğitmen puan kazanmıyor" yazıyordu; öyle bir ders türü kalmadı, her
              ders puan basıyor. Kutunun tek işi seçimi doğrulatmak: kullanıcı listeden
              çıktıktan sonra hangi konuyu seçtiğini görebilsin.
            */}
            {selected && (
              <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
                Konu: <strong className="text-slate-800">{selected.topicName}</strong>
              </div>
            )}

            <Field label="Başlangıç" hint="Kendi saat diliminde seç; sistem UTC'ye çevirir.">
              <input
                type="datetime-local"
                className="input"
                value={startLocal}
                onChange={(e) => setStartLocal(e.target.value)}
                required
              />
            </Field>

            {/* ÖĞRENCİYE ÜCRET GÖSTERİLMİYOR — çünkü ücret yok. Süre listesi yalnızca
                süre seçtiriyor; eğitmenin kazanacağı puan bu kararın konusu değil ve
                öğrenciye bir bedel varmış izlenimi vermemeli. */}
            <Field label="Süre" hint="Ders almak ücretsizdir.">
              <select className="input" value={duration} onChange={(e) => setDuration(e.target.value)}>
                {DURATION_OPTIONS.map((dk) => (
                  <option key={dk} value={dk}>
                    {dk} dakika
                  </option>
                ))}
              </select>
            </Field>

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
          <span className="mt-2 block">
            Dersin akışı değişmez — bu bir itiraz değil, kişi hakkında bildirimdir. Yönetim
            gerekli görürse uyarı, askı ya da ban uygular.
          </span>
        </Notice>

        <Field label="Sebep">
          <select className="input" value={reason} onChange={(e) => setReason(e.target.value)}>
            {Object.entries(REPORT_REASON_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
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
