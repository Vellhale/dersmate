import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { AuthProvider } from './state/AuthContext'
import { ConsentProvider } from './state/ConsentContext'
import { CookieBanner } from './components/CookieBanner'
import { AnalyticsGate } from './state/AnalyticsGate'
import './index.css'

// ConsentProvider, AuthProvider'ın İÇİNDE: giriş yapılınca yerel rızayı hesaba taşıyabilmesi
// için oturumu görmesi gerekiyor. CookieBanner router'ın DIŞINDA: çerez şeridi giriş
// ekranı dahil her sayfada görünmeli.
ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <ConsentProvider>
          <App />
          <CookieBanner />
          {/* GA4 yükleme kararı: rızaya bakan tek nokta. */}
          <AnalyticsGate />
        </ConsentProvider>
      </AuthProvider>
    </BrowserRouter>
  </React.StrictMode>,
)
