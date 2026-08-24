import { createContext, useContext, useMemo } from 'react'
import { api } from '../lib/api'
import { useAsync } from './useAsync'

const WalletContext = createContext(null)

/**
 * Cüzdan ucu için TEK kaynak. Daha önce hem Layout hem Dashboard aynı ucu ayrı ayrı
 * çekiyordu; Layout hiç unmount olmadığı için başlıktaki rozet oturum boyunca donuyordu.
 * Puanı değiştiren her işlem refreshWallet() çağırır.
 *
 * AD ESKİDİ, İŞLEV DEĞİŞTİ: cüzdan ekranı kaldırıldı; bu bağlamın bugün taşıdığı tek şey
 * kazanılan puan (totalEarnedCredits). Uç hâlâ /api/wallet olduğu için ad korundu —
 * yeniden adlandırma, karşılığı olan bir uç değişikliğiyle birlikte yapılmalı, tek
 * başına kozmetik bir gürültü olur.
 *
 * rankTitle / rankEmoji ARTIK OKUNMUYOR: unvan sistemi seviyeye dönüştü (lib/seviye.js).
 * Sunucu alanları göndermeye devam ediyor, arayüz kullanmıyor. Alanların sunucudan da
 * kalkması, XP algoritmasıyla birlikte yapılacak bir backend işi.
 */
export function WalletProvider({ children }) {
  const { data, error, loading, reload } = useAsync(() => api.wallet(), [])

  const value = useMemo(
    () => ({
      wallet: data,
      loading,
      error,
      refreshWallet: () => reload({ silent: true }),
    }),
    [data, loading, error, reload],
  )

  return <WalletContext.Provider value={value}>{children}</WalletContext.Provider>
}

export function useWallet() {
  const context = useContext(WalletContext)
  if (!context) throw new Error('useWallet, WalletProvider içinde kullanılmalı.')
  return context
}
