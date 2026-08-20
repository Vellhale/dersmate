import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import Layout from './components/Layout'
import { useAuth } from './state/AuthContext'
import Login from './pages/Login'
import Register from './pages/Register'
import VerifyEmail from './pages/VerifyEmail'
import Portfolio from './pages/Portfolio'
import Discover from './pages/Discover'
import Matches from './pages/Matches'
import Chat from './pages/Chat'
import Sessions from './pages/Sessions'
import Profile from './pages/Profile'
import Admin from './pages/Admin'
import Hakkimizda from './pages/Hakkimizda'

function RequireAuth({ children }) {
  const { isAuthenticated } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/giris" replace state={{ from: location.pathname }} />
  }

  return children
}

/** Yönetim sayfası yalnızca admin'e; yetkisiz kullanıcı Keşfet'e döner (API zaten 403 verir). */
function RequireAdmin({ children }) {
  const { session } = useAuth()
  return session?.isAdmin ? children : <Navigate to="/kesfet" replace />
}

export default function App() {
  const { isAuthenticated } = useAuth()

  return (
    <Routes>
      <Route path="/giris" element={isAuthenticated ? <Navigate to="/kesfet" replace /> : <Login />} />
      <Route path="/kayit" element={isAuthenticated ? <Navigate to="/kesfet" replace /> : <Register />} />
      <Route path="/dogrula" element={<VerifyEmail />} />

      <Route
        element={
          <RequireAuth>
            <Layout />
          </RequireAuth>
        }
      >
        {/*
          PANEL KALDIRILDI. Ürün artık Keşfet/Ders Portföyü odaklı çalışıyor; ayrı bir
          özet ekranı, kullanıcıyı asıl işini yaptığı yerden bir tık uzağa koyuyordu.
          "/" adresinin bir karşılığı kalmadığı için Keşfet'e yönlendiriliyor — rota
          silinip yerine bir şey konmasaydı giriş sonrası boş ekran gelirdi.
        */}
        <Route index element={<Navigate to="/kesfet" replace />} />
        <Route path="/portfolio" element={<Portfolio />} />
        <Route path="/kesfet" element={<Discover />} />
        <Route path="/eslesmeler" element={<Matches />} />
        <Route path="/sohbet" element={<Chat />} />
        <Route path="/sohbet/:conversationId" element={<Chat />} />
        <Route path="/dersler" element={<Sessions />} />
        {/* CÜZDAN KALDIRILDI: puan artık harcanan bir bakiye değil, profilde görünen bir
            unvan. Eski bağlantılar (yer imi, tur adımı) kırık kalmasın diye yönlendiriliyor;
            işlem geçmişi Derslerim'e taşındı. */}
        <Route path="/cuzdan" element={<Navigate to="/dersler" replace />} />
        {/* Yan menünün alt bağlantısı: misyon sayfası. Kabuğun (Layout) içinde, çünkü
            tek erişim yolu menü ve menü yalnızca giriş yapmış kullanıcıda var. */}
        <Route path="/hakkimizda" element={<Hakkimizda />} />
        {/* Tek bileşen: parametresiz kendi profilin, id ile başkasınınki. */}
        <Route path="/profil" element={<Profile />} />
        <Route path="/profil/:userId" element={<Profile />} />
        <Route
          path="/admin"
          element={
            <RequireAdmin>
              <Admin />
            </RequireAdmin>
          }
        />
      </Route>

      <Route path="*" element={<Navigate to="/kesfet" replace />} />
    </Routes>
  )
}
