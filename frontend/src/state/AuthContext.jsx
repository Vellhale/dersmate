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

  const login = useCallback(async (email, password) => {
    const hwidHash = await getHwidHash()
    const result = await api.login({ email, password, hwidHash })
    saveSession(result)
    setSession(result)
    return result
  }, [])

  const logout = useCallback(() => {
    saveSession(null)
    setSession(null)
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
