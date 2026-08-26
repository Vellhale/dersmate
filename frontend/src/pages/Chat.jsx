import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { useAuth } from '../state/AuthContext'
import { parseHubError } from '../hooks/useChatHub'
import { useInbox } from '../state/InboxContext'
import { formatDateTime, formatTime } from '../lib/format'
import { Badge, Button, EmptyState, ErrorBox, Field, Loading, Modal, Notice } from '../components/ui'
import { PersonLink } from '../components/PersonLink'

/*
  SOHBET SAYFASI EKRANA SIĞAR: sayfanın kendisi kaymaz; kaydırma, listenin ve konuşmanın
  KENDİ İÇİNDEDİR. İki panel de aynı yüksekliği kullanır (aşağıda), böylece lg'de aynı
  hizada başlayıp aynı hizada biterler.

  Çıkarılan değerler, panellerin DIŞINDA kalan sabit yüksekliklerin ölçülmüş toplamıdır —
  tahmin değil, tarayıcıda ölçüldü (2026-08-21):
    mobil 390x844 : üst bar 64 + main dolgusu 48 + başlık satırı 48 + altbilgi 68 = 228px
  Önceki değer 11rem (176px) idi ve sayfa tam 52px taşıyordu: başlık ile yazma alanı
  ekrandan çıkıyor, kullanıcı konuşmaya dönmek için yukarı kaydırmak zorunda kalıyordu.
  14.5rem (232px) birkaç piksel pay bırakır; altbilgi bağlantıları dar ekranda sarılırsa
  yüksekliği artabildiği için sıfır paya güvenilmiyor.

  lg değeri (16rem) ölçümle doğrulandı: 1360x900 ve 1360x700'de sayfa hiç kaymıyor.
*/
const PANEL_YUKSEKLIGI = 'h-[calc(100dvh-14.5rem)] lg:h-[calc(100vh-16rem)] lg:min-h-[420px]'

/**
 * Güvenli sohbet (Modül 2.1 + 2.2).
 * Kanal bağımsızlığı: platform video barındırmaz — Zoom/Meet/Discord linkleri buradan paylaşılır.
 */
export default function Chat() {
  const { conversationId } = useParams()
  const navigate = useNavigate()
  const { session } = useAuth()

  /*
    Konuşma listesi ve hub artık UYGULAMA DÜZEYİNDE (InboxContext). Bu sayfa kendi
    bağlantısını kurmaz: aynı kullanıcı için iki SignalR bağlantısı açılsaydı gruba
    katılma/ayrılma iki bağlantı arasında bölünür, mesaj birine gelip diğerinde beklenirdi.
  */
  const inbox = useInbox()
  const conversations = {
    data: inbox.conversations,
    loading: inbox.loading,
    error: inbox.error,
    reload: inbox.reloadConversations,
  }
  const [messages, setMessages] = useState([])
  const [loadingMessages, setLoadingMessages] = useState(false)
  const [historyError, setHistoryError] = useState(null)
  const [joinError, setJoinError] = useState(null)
  const [sendError, setSendError] = useState(null)
  const [draft, setDraft] = useState('')
  const [sending, setSending] = useState(false)
  const [sikayetAcik, setSikayetAcik] = useState(false)
  const [sikayetBildirimi, setSikayetBildirimi] = useState(null)
  const bottomRef = useRef(null)
  // Sohbet değişince sıfırlanır: her sohbetin ilk açılışı yine anında en alta gitsin.
  const jumpedToBottom = useRef(false)

  const active = conversations.data?.find((c) => c.conversationId === conversationId) ?? null

  // Arka plan tazelemesi: spinner göstermez (yoksa her mesajda ekran sıfırlanır).
  const refreshConversations = inbox.reloadConversations

  const onIncomingMessage = useCallback(
    (message) => {
      if (message.conversationId !== conversationId) {
        refreshConversations() // Başka sohbete mesaj geldi: okunmamış rozetini tazele.
        return
      }

      setMessages((prev) => (prev.some((m) => m.id === message.id) ? prev : [...prev, message]))

      // Kullanıcı bu sohbete BAKIYORKEN gelen mesaj okunmuş sayılır; aksi halde kendi
      // açık tuttuğu sohbette okunmamış rozeti belirir ve orada kalırdı.
      if (document.visibilityState === 'visible' && message.senderUserId !== session.userId) {
        api.markRead(conversationId).then(refreshConversations).catch(() => {})
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [conversationId, refreshConversations, session.userId],
  )

  const hub = inbox.hub

  // Canlı mesajlara abone ol; bağlantının kendisi sağlayıcıda duruyor.
  useEffect(() => inbox.subscribe({ onMessage: onIncomingMessage }), [inbox, onIncomingMessage])

  // (1) Geçmiş + okundu işaretleme — YALNIZCA sohbet değişince.
  // Bağlantı durumuna bağlanmamalı: her yeniden bağlanmada geçmişi yeniden çekmek,
  // uçuşta gelen canlı mesajı ezip kalıcı olarak kaybettirirdi.
  useEffect(() => {
    if (!conversationId) return

    let cancelled = false
    jumpedToBottom.current = false
    setMessages([])
    setLoadingMessages(true)
    setHistoryError(null)
    setSendError(null)

    api
      .messages(conversationId, 1, 50)
      .then((page) => {
        if (cancelled) return
        // Fetch uçuştayken gelen canlı mesajlar korunur (id'ye göre birleştir).
        setMessages((live) => mergeById([...page].reverse(), live))
      })
      .catch((err) => !cancelled && setHistoryError(err))
      .finally(() => !cancelled && setLoadingMessages(false))

    api.markRead(conversationId).then(refreshConversations).catch(() => {})

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversationId])

  // (2) Gruba katılma — yalnızca bağlantı GERÇEKTEN kuruluyken.
  // Yeniden bağlanmada sunucudaki grup üyeliği kaybolduğu için tekrar katılmak şart.
  useEffect(() => {
    if (!conversationId || hub.status !== 'connected') return

    let cancelled = false
    setJoinError(null)

    hub.joinConversation(conversationId)?.catch((err) => {
      if (!cancelled) setJoinError(parseHubError(err))
    })

    return () => {
      cancelled = true
      hub.leaveConversation(conversationId)?.catch(() => {})
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversationId, hub.status])

  // Sohbetin ilk açılışında ANINDA en alta; sonraki mesajlarda yumuşak kaydırma.
  // Soğuk yüklemede (doğrudan /sohbet/:id adresine girildiğinde) 'smooth' animasyon
  // yerleşim daha oturmadan başladığı için tarayıcı tarafından iptal ediliyordu ve
  // kullanıcı en ESKİ mesajların başında kalıyordu — telefonda mesaj listesi uzunsa
  // güncel konuşmayı bulmak için elle epeyce kaydırmak gerekiyordu.
  useEffect(() => {
    if (!messages.length) return
    bottomRef.current?.scrollIntoView({ behavior: jumpedToBottom.current ? 'smooth' : 'auto' })
    jumpedToBottom.current = true
  }, [messages])

  async function send(event) {
    event.preventDefault()
    const content = draft.trim()
    if (!content) return

    setSending(true)
    setSendError(null)
    try {
      // Hub kapalıysa REST yedeği; backend her iki yolda da aynı yayınları yapar.
      const message = hub.isConnected()
        ? await hub.sendMessage(conversationId, content)
        : await api.sendMessage(conversationId, content)

      if (message) {
        setMessages((prev) => (prev.some((m) => m.id === message.id) ? prev : [...prev, message]))
      }
      setDraft('')
      refreshConversations()
    } catch (err) {
      setSendError(err.name === 'ApiError' ? err : parseHubError(err))
    } finally {
      setSending(false)
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold text-slate-900">Sohbet</h1>
        <div className="flex items-center gap-2">
          {joinError && <Badge tone="danger">Sohbete katılınamadı</Badge>}
          <ConnectionBadge status={hub.status} />
        </div>
      </div>

      <ErrorBox error={conversations.error} onRetry={conversations.reload} />

      {conversations.loading ? (
        <Loading />
      ) : conversations.data?.length === 0 ? (
        <EmptyState
          title="Henüz sohbetin yok"
          description="Sohbet, bir eşleşme isteği kabul edildiğinde otomatik açılır."
          action={<Button onClick={() => navigate('/kesfet')}>Eşleşme bul</Button>}
        />
      ) : (
        /* ANA-DETAY: lg altında ekrana yalnızca biri sığar. Sohbet seçilmemişse liste,
           seçilmişse konuşma gösterilir (geri düğmesiyle listeye dönülür). lg ve üstünde
           ikisi yan yana kalır. Önceki hâlde ikisi alt alta duruyordu: telefonda konuşmaya
           yarım ekran kalıyor, hiçbiri seçilmemişken de "Soldaki listeden seç" yazan boş
           bir panel tüm ekranı kaplıyordu — üstelik mobilde "sol" diye bir yer yok. */
        <div className="grid gap-4 lg:grid-cols-[300px_1fr]">
          {/* Liste KENDİ İÇİNDE kaydırılır, sayfayı uzatmaz. Yüksekliği sağdaki konuşma
              paneliyle birebir aynı (aşağıdaki section ile aynı calc'lar): iki sütun lg'de
              aynı hizada başlayıp aynı hizada biter.

              Neden gerekli: sohbet sayısı arttıkça (elli sohbetli bir eğitmen olağan) liste
              sayfayı aşağı uzatıyordu; başlık ve yazma alanı ekrandan çıkıyor, kullanıcı
              konuşmaya dönmek için yukarı kaydırmak zorunda kalıyordu.

              overscroll-contain: liste sonuna gelince kaydırma sayfaya SIÇRAMASIN.
              pr-1: kaydırma çubuğu seçili öğenin sağ kenarlığını örtmesin. */}
          <aside
            className={`space-y-1.5 overflow-y-auto overscroll-contain pr-1 ${PANEL_YUKSEKLIGI}
                        ${conversationId ? 'hidden lg:block' : 'block'}`}
          >
            {(conversations.data ?? []).map((conversation) => {
              const isActive = conversation.conversationId === conversationId
              // Açık sohbette rozet daima 0: sunucu tazelemesi dönene kadar yanıp sönmesin.
              const unread = isActive ? 0 : conversation.unreadCount

              return (
                <button
                  key={conversation.conversationId}
                  onClick={() => navigate(`/sohbet/${conversation.conversationId}`)}
                  className={`flex w-full items-center justify-between gap-2 rounded-lg border p-3 text-left transition ${
                    isActive ? 'border-brand-300 bg-brand-50' : 'border-slate-200/80 bg-white hover:bg-slate-50'
                  }`}
                >
                  <div className="min-w-0">
                    <div className="truncate font-medium text-slate-800">
                      {conversation.otherDisplayName}
                    </div>
                    <div className="text-xs text-slate-500">
                      {conversation.lastMessageAtUtc
                        ? formatDateTime(conversation.lastMessageAtUtc)
                        : 'Henüz mesaj yok'}
                    </div>
                  </div>
                  {conversation.isClosed && <Badge tone="neutral">Kapalı</Badge>}
                  {unread > 0 && <Badge tone="brand">{unread}</Badge>}
                </button>
              )
            })}
          </aside>

          {/* Yükseklik mobilde dvh ile: vh, mobil tarayıcının adres çubuğunu saymadığı için
              yazma alanı ekranın altında kalıyordu. Yükseklik listeyle ORTAK sabitten gelir
              (bkz. PANEL_YUKSEKLIGI) — ikisi ayrı ayarlanırsa lg'de sütunlar hizasız kalır. */}
          <section
            className={`${PANEL_YUKSEKLIGI} flex-col rounded-xl border border-slate-200/80 bg-white lg:flex
                        ${conversationId ? 'flex' : 'hidden'}`}
          >
            {!conversationId ? (
              <div className="grid flex-1 place-items-center p-6 text-center text-sm text-slate-500">
                Soldaki listeden bir sohbet seç.
              </div>
            ) : (
              <>
                <header className="flex shrink-0 items-center gap-2 border-b border-slate-200/80 px-4 py-3">
                  {/* Mobilde listeye dönüş: lg'de liste zaten solda durduğu için gizli.

                      44px (h-11): bu düğme YALNIZCA dokunmatik boyutlarda var — lg'de
                      hiç çizilmiyor. 40px'ti ve projenin dokunma eşiğinin altındaydı;
                      görünürlüğü zaten `lg:hidden` ile mobile bağlı olan bir düğmenin
                      masaüstü ölçüsünde durması için bir sebep yok. */}
                  <button
                    onClick={() => navigate('/sohbet')}
                    className="-ml-2 grid h-11 w-11 shrink-0 place-items-center rounded-lg text-slate-500 hover:bg-slate-100 lg:hidden"
                    aria-label="Sohbet listesine dön"
                  >
                    ←
                  </button>
                  <div className="min-w-0">
                    <div className="truncate font-semibold text-slate-800">
                      <PersonLink userId={active?.otherUserId} className="text-brand-700">
                        {active?.otherDisplayName ?? 'Sohbet'}
                      </PersonLink>
                    </div>
                    <p className="text-xs text-slate-500">
                      {active?.isClosed
                        ? 'Bu eşleşme sonlandırıldı — geçmiş okunabilir, yeni mesaj yazılamaz.'
                        : 'Ders linkini (Zoom / Google Meet / Discord) buradan paylaşabilirsin.'}
                    </p>
                  </div>

                  {/*
                    ŞİKAYET — SOHBETTEN (2026-08-27).

                    Buraya kadar şikayet açmanın tek yolu bir DERS üzerindendi. Yani
                    eşleşip yazışmaya başlayan iki kişiden biri taciz ederse, henüz
                    tamamlanmış ders yoksa karşı tarafın bildirme yolu YOKTU — öğrenci
                    platformunda tacizin en olası anı tam olarak burası.

                    Kapalı sohbette de görünüyor: eşleşme sonlandırıldıktan sonra
                    "artık bildiremezsin" demek, kişiyi susturmanın en kolay yolunu
                    (önce taciz et, sonra eşleşmeyi kapat) açık bırakırdı.

                    Sessiz duruyor (slate-500 + ikonsuz küçük metin), hover'da rose:
                    dikkat çeken bir düğme sohbeti ihbar hattı gibi gösterirdi, gömülü
                    bir menü ise ihlali gören kişinin vazgeçtiği yol olurdu.
                  */}
                  {active?.otherUserId && (
                    <button
                      type="button"
                      onClick={() => setSikayetAcik(true)}
                      className="ml-auto shrink-0 rounded-lg px-2 py-1 text-xs font-medium
                                 text-slate-500 transition hover:bg-rose-50 hover:text-rose-700"
                    >
                      Şikayet et
                    </button>
                  )}
                </header>

                <div className="flex-1 space-y-2 overflow-y-auto p-4">
                  {loadingMessages ? (
                    <Loading label="Mesajlar yükleniyor…" />
                  ) : historyError ? (
                    <ErrorBox
                      error={historyError}
                      onRetry={() => navigate(`/sohbet/${conversationId}`, { replace: true })}
                    />
                  ) : messages.length === 0 ? (
                    <p className="py-8 text-center text-sm text-slate-500">
                      {/* Kapalı sohbette "ilk mesajı sen yaz" demek, yapılamayacak bir şeyi
                          önermek olurdu — yazma alanı zaten kaldırılmış durumda. */}
                      {active?.isClosed
                        ? 'Bu sohbette hiç mesaj yazılmadan eşleşme sonlandırıldı.'
                        : 'İlk mesajı sen yaz. Ders saatini kararlaştırıp toplantı linkini paylaşın.'}
                    </p>
                  ) : (
                    messages.map((message) => (
                      <MessageBubble
                        key={message.id}
                        message={message}
                        mine={message.senderUserId === session.userId}
                      />
                    ))
                  )}
                  <div ref={bottomRef} />
                </div>

                {/* Kapalı sohbette yazma alanı hiç çizilmez. Kutuyu gösterip gönderimi
                    sunucuya reddettirmek, kullanıcıya mesajını yazdırıp sonra kaybettirirdi. */}
                {active?.isClosed ? (
                  <div className="shrink-0 border-t border-slate-200/80 p-3 text-center text-sm text-slate-500">
                    Bu eşleşme sonlandırıldı. Geçmişi okuyabilirsin ama yeni mesaj gönderemezsin.
                  </div>
                ) : (
                  <form onSubmit={send} className="shrink-0 border-t border-slate-200/80 p-3">
                    {sendError && <p className="mb-2 text-sm text-rose-600">{sendError.message}</p>}
                    <div className="flex gap-2">
                      <input
                        className="input"
                        placeholder="Mesajını yaz…"
                        value={draft}
                        onChange={(e) => setDraft(e.target.value)}
                        maxLength={2000}
                      />
                      <Button type="submit" loading={sending} disabled={!draft.trim()}>
                        Gönder
                      </Button>
                    </div>
                  </form>
                )}
              </>
            )}
          </section>
        </div>
      )}

      <SohbetSikayetModali
        open={sikayetAcik}
        kisi={active}
        onClose={() => setSikayetAcik(false)}
        onGonderildi={() => {
          setSikayetAcik(false)
          setSikayetBildirimi('Şikayetin yönetime iletildi. Karşı taraf bunu görmez.')
        }}
      />

      {sikayetBildirimi && (
        <Notice tone="success" onDismiss={() => setSikayetBildirimi(null)}>
          {sikayetBildirimi}
        </Notice>
      )}
    </div>
  )
}

/*
  SOHBET ŞİKAYET MODALI.

  SEBEP LİSTESİ DERS ŞİKAYETİNDEN FARKLI: oradaki seçenekler dersle ilgili
  ("ders hiç yapılmadı", "kanıt sahte", "süre kısa sürdü") ve sohbette hiçbirinin
  karşılığı yok. Burada yalnızca ders bağlamı gerektirmeyen ikisi listeleniyor.
  Sunucu enum'u aynı (ReportReason); ayrılan yalnızca kullanıcıya sunulan alt küme.

  Açıklama ZORUNLU ve alt sınırı var: "Diğer" seçildiğinde moderatörün elinde
  yalnızca bu metin oluyor. Sunucu 10-2000 karakter istiyor; istemci de aynı alt
  sınırı uyguluyor ki kullanıcı gidip gelmesin.
*/
/* SUNUCUYLA AYNI SAYI (CreateReportHandler). İstemcide daha GEVŞEK bir sınır,
   kullanıcıya 12 karakter yazdırıp gönderdikten sonra 400 gösterirdi — kontrolün
   istemcide olmasının tek amacı o gidiş gelişi önlemek. */
const EN_AZ_ACIKLAMA = 15

const SOHBET_SIKAYET_SEBEPLERI = [
  { key: 'Abuse', label: 'Hakaret, taciz veya uygunsuz davranış' },
  { key: 'Other', label: 'Diğer' },
]

function SohbetSikayetModali({ open, kisi, onClose, onGonderildi }) {
  const [sebep, setSebep] = useState('Abuse')
  const [aciklama, setAciklama] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const kapat = () => {
    setSebep('Abuse')
    setAciklama('')
    setError(null)
    onClose()
  }

  const gonderilebilir = aciklama.trim().length >= EN_AZ_ACIKLAMA

  async function gonder(event) {
    event.preventDefault()
    if (!gonderilebilir) return
    setBusy(true)
    setError(null)
    try {
      await api.reportUser(kisi.otherUserId, sebep, aciklama.trim())
      setSebep('Abuse')
      setAciklama('')
      onGonderildi()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={kapat}
      title="Şikayet et"
      footer={
        <>
          <Button variant="secondary" onClick={kapat}>
            Vazgeç
          </Button>
          <Button
            type="submit"
            form="sohbet-sikayet"
            variant="danger"
            loading={busy}
            disabled={!gonderilebilir}
          >
            Şikayeti gönder
          </Button>
        </>
      }
    >
      <form id="sohbet-sikayet" onSubmit={gonder} className="space-y-4">
        <Notice tone="info">
          Şikayetin <strong>yalnızca yönetime</strong> gider. {kisi?.otherDisplayName ?? 'Karşı taraf'}{' '}
          ne şikayeti görür, ne bildirim alır, ne de kim olduğunu öğrenir.
        </Notice>

        <Field label="Sebep">
          <select className="input" value={sebep} onChange={(e) => setSebep(e.target.value)}>
            {SOHBET_SIKAYET_SEBEPLERI.map(({ key, label }) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Ne oldu?" hint={`En az ${EN_AZ_ACIKLAMA} karakter. Mümkünse mesajdan alıntı yap.`}>
          <textarea
            className="input h-28 resize-none"
            value={aciklama}
            onChange={(e) => setAciklama(e.target.value)}
            maxLength={2000}
            placeholder="Örn. Sohbette ısrarla telefon numaramı istedi ve reddedince hakaret etti."
          />
        </Field>

        <ErrorBox error={error} />
      </form>
    </Modal>
  )
}

/** Geçmiş ile canlı mesajları id'ye göre tekilleştirip zamana göre sıralar. */
function mergeById(history, live) {
  const byId = new Map()
  for (const message of [...history, ...live]) byId.set(message.id, message)
  return [...byId.values()].sort((a, b) => new Date(a.sentAtUtc) - new Date(b.sentAtUtc))
}

function MessageBubble({ message, mine }) {
  return (
    <div className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
      <div
        className={`max-w-[80%] rounded-2xl px-3.5 py-2 text-sm ${
          mine ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-800'
        }`}
      >
        <p className="whitespace-pre-wrap break-words">{linkify(message.content)}</p>
        <div className={`mt-1 text-[11px] ${mine ? 'text-brand-100' : 'text-slate-500'}`}>
          {formatTime(message.sentAtUtc)}
        </div>
      </div>
    </div>
  )
}

/**
 * Toplantı linklerini tıklanabilir yapar (kanal bağımsızlığı ilkesinin arayüz karşılığı).
 * Yalnızca http/https eşleşir — javascript:/data: gibi şemalar link'e ASLA dönüşmez.
 */
function linkify(text) {
  const parts = String(text).split(/(https?:\/\/[^\s]+)/g)
  return parts.map((part, index) =>
    /^https?:\/\//.test(part) ? (
      <a
        key={index}
        href={part}
        target="_blank"
        rel="noopener noreferrer nofollow"
        className="underline underline-offset-2"
      >
        {part}
      </a>
    ) : (
      part
    ),
  )
}

function ConnectionBadge({ status }) {
  const map = {
    connected: { tone: 'success', label: 'Canlı bağlantı' },
    connecting: { tone: 'warning', label: 'Bağlanıyor…' },
    reconnecting: { tone: 'warning', label: 'Yeniden bağlanıyor…' },
    disconnected: { tone: 'danger', label: 'Bağlantı yok — yeni mesajlar gecikebilir' },
  }
  const { tone, label } = map[status] ?? map.connecting
  return <Badge tone={tone}>{label}</Badge>
}
