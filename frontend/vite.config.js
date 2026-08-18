import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

/*
  Buradan KALDIRILAN iki ayar, projenin Google Drive'da durduğu döneme aitti:

    resolve.preserveSymlinks  → node_modules yerel diskte, kaynak Drive'da junction'dı.
    server.watch.usePolling   → Drive native dosya izleme olayı üretmiyor, 3 sn'de bir
                                yoklama gerekiyordu; HMR o kadar gecikmeli çalışıyordu.

  Proje yerel diske taşındı, ikisi de gereksiz. Vite'ın kendi izleyicisi devrede ve HMR
  anında. Projeyi tekrar bir senkron klasörüne taşırsan ikisini geri getirmen gerekir.
*/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
})
