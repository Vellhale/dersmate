import tailwindConfig from '../../tailwind.config.js'

/**
 * Marka tonları, JS'ten okunabilir hâlde.
 *
 * NEDEN VAR: Tailwind sınıfları yalnızca CSS özelliklerine uygulanabiliyor. SVG gradyan
 * durakları (`<stop stopColor>`), canvas çizimleri ve satır içi `fill` değerleri bir
 * SINIF değil, gerçek bir renk DEĞERİ istiyor. Bu dosya olmasaydı her biri paleti elle
 * kopyalar, palet değiştiğinde geride kalırdı — tam olarak F1b'de yaşanan sorun.
 *
 * Değerler tailwind.config.js'ten okunuyor, kopyalanmıyor: tek kaynak orası ve burada
 * eşitliği bozacak bir ikinci liste yok. `e2e/kaynak-sabitleri.spec.js` içindeki
 * "src altında paletin dışında sabit renk yok" testi bu dosyanın gerekçesidir.
 *
 * ⚠️ `frontend/src/lib/hwid.js` içindeki '#4f46e5' BURAYA TAŞINMAZ. O bir marka rengi
 * değil, cihaz parmak izinin sabiti; palete bağlanması tam da kaçınılması gereken şey.
 */
export const brand = tailwindConfig.theme.extend.colors.brand

/** Sık kullanılan iki nötr. Tailwind'in kendi slate ölçeğinden, marka dışı. */
export const ink = '#0f172a' // slate-900
