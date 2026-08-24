import { createContext, useContext, useMemo } from 'react'
import { api } from '../lib/api'
import { useAsync } from './useAsync'

const WalletContext = createContext(null)

/**
 * Cüzdan ucu için TEK kaynak. Daha önce hem Layout hem Dashboard aynı ucu ayrı ayrı
 * çekiyordu; Layout hiç unmount olmadığı için başlıktaki rozet oturum boyunca donuyordu.
 * Puanı değiştiren her işlem refreshWallet() çağırır.
 *
 * AD ESKİDİ, İŞLEV DEĞİŞTİ: cüzdan ekranı kaldırıldı; bu bağlamın bugün taşıdığı şey
 * kazanılan puan ve ondan türeyen seviye (totalEarnedCredits / level / nextLevelAt).
 * Uç hâlâ /api/wallet olduğu için ad korundu — yeniden adlandırma, karşılığı olan bir
 * uç değişikliğiyle birlikte yapılmalı, tek başına kozmetik bir gürültü olur.
 *
 * SEVİYE AYRI BİR İSTEK GEREKTİRMİYOR: sunucu onu bu yanıtın içinde gönderiyor, çünkü
 * hesabın girdisi zaten burada olan puan. Ayrı bir uç açmak, üst bardaki rozeti her
 * sayfa geçişinde ikinci bir isteğe bağlardı.
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
