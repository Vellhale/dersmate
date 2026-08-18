import { createContext, useCallback, useContext, useMemo, useRef } from 'react'
import { api } from '../lib/api'
import { useAsync } from './useAsync'
import { useChatHub } from '../hooks/useChatHub'

const InboxContext = createContext(null)

/**
 * Gelen kutusu için TEK kaynak: konuşma listesi + SignalR bağlantısı.
 *
 * NEDEN UYGULAMA DÜZEYİNDE: sunucu, mesaj alındığında alıcıya kişisel bir
 * "ConversationUpdated" olayı gönderiyor ve ChatHub'daki yorumu amacını açıkça yazıyor —
 * "karşı taraf sohbeti açık tutmuyorsa bile gelen kutusu rozetini güncelleyebilsin".
 * Oysa hub YALNIZCA Chat sayfasında kuruluyordu: yani olayı sadece zaten gelen kutusuna
 * bakan kullanıcı duyuyordu. Panelde, Keşfet'te ya da Cüzdan'da duran kullanıcı gelen
 * mesajı hiç fark etmiyordu — ders saati ve toplantı linki yalnızca sohbetten
 * kararlaştırıldığı için randevulaşmanın en kırılgan adımı sessiz kalıyordu.
 *
 * TEK BAĞLANTI: Chat sayfası da kendi hub'ını kurmaz, buradakini kullanır. İki bağlantı
 * olsaydı aynı kullanıcı sunucuda iki oturum açar, gruba katılma/ayrılma iki bağlantı
 * arasında bölünür ve mesajlar bir bağlantıya gelip diğerinde beklenirdi.
 */
export function InboxProvider({ children }) {
  const conversations = useAsync(() => api.conversations(), [])

  // Sayfa bileşenleri (bugün yalnızca Chat) canlı mesajlara buradan abone olur.
  const listeners = useRef(new Set())

  const reloadConversations = useCallback(
    () => conversations.reload({ silent: true }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [conversations.reload],
  )

  const hub = useChatHub({
    // ReceiveMessage yalnızca DAĞITILIR, burada listeyi tazelemez: aynı olay için hem
    // burada hem Chat'te tazeleme yapmak iki eşzamanlı GET açar ve geç dönen eski yanıt
    // yeni rozeti ezebilir.
    onMessage: (message) => listeners.current.forEach((l) => l.onMessage?.(message)),

    // Rozetin asıl kaynağı bu: kullanıcı sohbete bakmıyorken de gelir.
    onConversationUpdated: (id) => {
      reloadConversations()
      listeners.current.forEach((l) => l.onConversationUpdated?.(id))
    },

    onMessagesRead: (id, byUserId) =>
      listeners.current.forEach((l) => l.onMessagesRead?.(id, byUserId)),
  })

  const subscribe = useCallback((handlers) => {
    listeners.current.add(handlers)
    return () => listeners.current.delete(handlers)
  }, [])

  const value = useMemo(() => {
    const list = conversations.data ?? []
    return {
      conversations: list,
      loading: conversations.loading,
      error: conversations.error,
      reloadConversations,
      unreadTotal: list.reduce((sum, c) => sum + (c.unreadCount ?? 0), 0),
      hub,
      subscribe,
    }
  }, [conversations.data, conversations.loading, conversations.error, reloadConversations, hub, subscribe])

  return <InboxContext.Provider value={value}>{children}</InboxContext.Provider>
}

export function useInbox() {
  const context = useContext(InboxContext)
  if (!context) throw new Error('useInbox, InboxProvider içinde kullanılmalı.')
  return context
}
