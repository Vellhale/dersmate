import { useEffect, useRef, useState } from 'react'
import { HttpTransportType, HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { API_BASE, getToken } from '../lib/api'

const MAX_BACKOFF_MS = 30000

/**
 * SignalR ChatHub bağlantısı (Modül 2.1).
 *
 * - Token, WebSocket header taşıyamadığı için accessTokenFactory ile query'den gider
 *   (backend yalnızca /hubs yolunda kabul eder).
 * - Backend AppExceptionHubFilter iş kuralı hatalarını "KOD|mesaj" olarak yollar;
 *   parseHubError bunu ayrıştırır.
 * - SINIRSIZ yeniden bağlanma: withAutomaticReconnect'in denemeleri tükendiğinde SignalR
 *   bir daha denemez ve canlı akış kalıcı ölür. onclose üzerinden üstel geri çekilmeli
 *   kendi döngümüzü kuruyoruz ki uzun kesintiden sonra sohbet kendiliğinden geri gelsin.
 * - Tüm setStatus çağrıları "bu effect hâlâ güncel mi" bayrağıyla korunur; aksi halde
 *   kapanan eski bağlantının onclose'u yeni bağlantının durumunu 'disconnected' yapardı.
 */
export function useChatHub({ onMessage, onConversationUpdated, onMessagesRead }) {
  const [status, setStatus] = useState('connecting')
  const connectionRef = useRef(null)

  // Olay işleyicileri ref'te tutulur: her render'da bağlantı yeniden kurulmasın.
  const handlers = useRef({ onMessage, onConversationUpdated, onMessagesRead })
  handlers.current = { onMessage, onConversationUpdated, onMessagesRead }

  useEffect(() => {
    let disposed = false
    let retryTimer = null
    let attempt = 0

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/chat`, {
        accessTokenFactory: () => getToken() ?? '',
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('ReceiveMessage', (message) => handlers.current.onMessage?.(message))
    connection.on('ConversationUpdated', (id) => handlers.current.onConversationUpdated?.(id))
    connection.on('MessagesRead', (id, byUserId) => handlers.current.onMessagesRead?.(id, byUserId))

    connection.onreconnecting(() => !disposed && setStatus('reconnecting'))
    connection.onreconnected(() => {
      if (disposed) return
      attempt = 0
      setStatus('connected')
    })
    connection.onclose(() => {
      if (disposed) return
      setStatus('disconnected')
      scheduleRestart()
    })

    function scheduleRestart() {
      if (disposed) return
      const delay = Math.min(MAX_BACKOFF_MS, 2000 * 2 ** attempt)
      attempt += 1
      retryTimer = setTimeout(start, delay)
    }

    function start() {
      if (disposed) return
      setStatus('connecting')
      connection
        .start()
        .then(() => {
          if (disposed) return
          attempt = 0
          setStatus('connected')
        })
        .catch(() => {
          if (disposed) return
          setStatus('disconnected')
          scheduleRestart()
        })
    }

    connectionRef.current = connection
    start()

    return () => {
      disposed = true
      if (retryTimer) clearTimeout(retryTimer)
      connectionRef.current = null
      connection.off('ReceiveMessage')
      connection.off('ConversationUpdated')
      connection.off('MessagesRead')
      connection.stop()
    }
  }, [])

  return {
    status,
    joinConversation: (id) => connectionRef.current?.invoke('JoinConversation', id),
    leaveConversation: (id) => connectionRef.current?.invoke('LeaveConversation', id),
    sendMessage: (id, content) => connectionRef.current?.invoke('SendMessage', id, content),
    markRead: (id) => connectionRef.current?.invoke('MarkRead', id),
    isConnected: () => connectionRef.current?.state === 'Connected',
  }
}

/** HubException mesajı "KOD|Türkçe mesaj" formatındadır; kullanıcıya yalnızca mesajı göster. */
export function parseHubError(error) {
  const raw = String(error?.message ?? '')
  const match = raw.match(/([A-Z_]+)\|(.+)$/)
  if (match) return { code: match[1], message: match[2] }
  return { code: 'HUB_ERROR', message: 'İşlem tamamlanamadı. Bağlantını kontrol et.' }
}
