import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

/*
  Buradan KALDIRILAN iki ayar, projenin Google Drive'da durduğu döneme aitti:

    resolve.preserveSymlinks  → node_modules yerel diskte, kaynak Drive'da junction'dı.
    server.watch.usePolling   → Drive native dosya izleme olayı üretmiyor, 3 sn'de bir
                                yoklama gerekiyordu; HMR o kadar gecikmeli çalışıyordu.

  Proje yerel diske taşındı, ikisi de gereksiz. Vite'ın kendi izleyicisi devrede ve HMR
  anında. Projeyi tekrar bir senkron klasörüne taşırsan ikisini geri getirmen gerekir.
*/
/*
  ÜRETİM DERLEMESİ, API ADRESİ VERİLMEDEN ÜRETİLEMEZ — 2026-08-27'de eklendi.

  Kapatılan hata şuydu: api.js'te `import.meta.env.VITE_API_URL ?? 'http://localhost:5000'`
  yazıyordu ve Vite üretim modunda `.env.development`'ı OKUMAZ. `.env.production` da
  .gitignore'da olduğu için depoyu klonlayan hiç kimsede yok. Sonuç sessizdi: `npm run
  build` uyarısız başarılı oluyor, çıkan paket `localhost:5000` gömüyor ve canlıda her
  ziyaretçinin tarayıcısı KENDİ makinesine istek atıyordu. Site açılır, tasarım görünür,
  giriş dahil tek istek çalışmaz — ve sunucu tarafı kusursuz kurulmuş olsa bile.

  Kontrol RUNTIME'da değil DERLEME ZAMANINDA: çalışma zamanı kontrolü hatayı ancak
  kullanıcı sayfayı açtığında gösterirdi. Burada ise bozuk paket hiç ÜRETİLEMİYOR.

  Yalnızca `mode === 'production'`: geliştirme ve test derlemelerinde localhost yedeği
  doğru davranış.
*/
export default defineConfig(({ mode }) => {
  if (mode === 'production') {
    // loadEnv, Vite'ın kendi sırasıyla okur (.env, .env.production, .env.production.local
    // ve süreç ortam değişkenleri). Üçüncü argüman '' → VITE_ öneki filtresini kaldırmaz,
    // yalnızca burada okuduğumuz için önekli adı doğrudan veriyoruz.
    const env = loadEnv(mode, process.cwd(), 'VITE_')

    if (!env.VITE_API_URL) {
      throw new Error(
        'VITE_API_URL tanımsız — üretim derlemesi durduruldu.\n' +
          'Bu değişken olmadan üretilen paket API adresi olarak http://localhost:5000 gömer\n' +
          've canlıda hiçbir istek çalışmaz.\n\n' +
          'Çözüm: frontend/.env.production dosyası oluşturun (.env.example örnek):\n' +
          '  VITE_API_URL=https://api.alan-adiniz.com\n' +
          'ya da derlemeyi ortam değişkeniyle koşun: VITE_API_URL=… npm run build',
      )
    }

    if (env.VITE_API_URL.includes('localhost') || env.VITE_API_URL.includes('127.0.0.1')) {
      throw new Error(
        `VITE_API_URL hâlâ yerel bir adres (${env.VITE_API_URL}) — üretim derlemesi durduruldu.\n` +
          'Ziyaretçinin tarayıcısı bu adrese KENDİ makinesinde bakar.',
      )
    }
  }

  return {
    plugins: [react()],
    server: {
      port: 5173,
    },
  }
})
