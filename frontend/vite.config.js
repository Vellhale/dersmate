import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  resolve: {
    // Geliştirme çalışma alanındaki "src", Drive'daki kaynağa junction'dır (bkz. setup-dev.ps1).
    // Bu ayar olmadan Rollup yolu gerçek hedefe (G:) çözer ve yerel node_modules'ü bulamaz.
    preserveSymlinks: true,
  },
  server: {
    port: 5173,
    watch: {
      /*
        Google Drive sanal diski dosya zaman damgalarını sürekli oynattığı için agresif
        polling (400 ms) Vite'a "dosya değişti" dedirtip sayfayı saniyede bir yeniden
        yüklüyordu — form doldurmak bile imkânsızdı. Polling gerekli (Drive native izleme
        olayı üretmiyor) ama seyrek olmalı; awaitWriteFinish yazma bitmeden tetiklemeyi keser.
      */
      usePolling: true,
      interval: 3000,
      awaitWriteFinish: { stabilityThreshold: 1000, pollInterval: 300 },
    },
  },
})
