import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api, AUTH_EXPIRED_EVENT, loadSession, saveSession } from '../lib/api'
import { getHwidHash } from '../lib/hwid'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [session, setSession] = useState(() => loadSession())

  // Token süresi dolduğunda (401) oturumu düşür — API katmanı bu olayı yayınlar.
  useEffect(() => {
    const onExpired = () => {
      saveSession(null)
      setSession(null)
    }
    window.addEventListener(AUTH_EXPIRED_EVENT, onExpired)
    return () => window.removeEventListener(AUTH_EXPIRED_EVENT, onExpired)
  }, [])

  /*
    rememberMe VARSAYILAN true — sunucudaki varsayılanla aynı. Bu bilinçli: kutuyu
    okumayan eski bir çağrı (ya da ileride eklenecek başka bir giriş yolu) sessizce
    "hatırlama" davranışına düşerse, kullanıcı yalnızca beklediğinden erken çıkış yapar;
    tersi — istemeden 60 gün açık kalan bir oturum — ortak bilgisayarda gerçek bir zarar.
  */
  const login = useCallback(async (email, password, rememberMe = true) => {
    const hwidHash = await getHwidHash()
    const result = await api.login({ email, password, hwidHash, rememberMe })
    saveSession(result)
    setSession(result)
    return result
  }, [])

  /*
    Yerel oturum HEMEN siliniyor (UI beklemez), sunucudaki iptal ise arka planda
    best-effort gidiyor. Sıra bilinçli: istemci taraflı çıkış anında olmalı; sunucu
    çağrısı ağ hatasıyla düşse bile kullanıcı çıkmış sayılır. api.logout token'ı 60 gün
    yaşamaktan alıkoyar (bkz. oturumuKapat). tumCihazlar=true "her yerden çık".
  */
  const logout = useCallback(({ tumCihazlar = false } = {}) => {
    const refreshToken = loadSession()?.refreshToken
    saveSession(null)
    setSession(null)
    if (refreshToken) api.logout(refreshToken, tumCihazlar)
  }, [])

  const value = useMemo(
    () => ({ session, isAuthenticated: Boolean(session?.accessToken), login, logout }),
    [session, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth, AuthProvider içinde kullanılmalı.')
  return context
}
