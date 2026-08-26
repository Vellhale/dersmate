import { Link } from 'react-router-dom'
import { Logo } from '../components/Logo'

/*
  YASAL METİN SAYFALARININ ORTAK KABUĞU.

  ⚠️ LAYOUT'UN İÇİNDE DEĞİL, TOP-LEVEL ROTA. Sebep: bu sayfalara KAYIT OLMADAN
  ulaşılabilmeli. Kayıt formundaki onay kutusu bu metinlere bağlanıyor ve henüz hesabı
  olmayan biri okuyamıyorsa, "okudum ve kabul ediyorum" kutusunu işaretlemesi anlamsız
  olur. Layout, RequireAuth'un arkasında.

  Bu yüzden kabuk da sade: kabuğun kendi gezinme rayı burada yok, yalnızca logo ve
  "geri dön" bağlantısı var.
*/
export function MetinSayfasi({ baslik, ozet, sonGuncelleme, children }) {
  return (
    <div className="min-h-[100dvh] bg-slate-50">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-3xl items-center justify-between px-4 py-4">
          <Link to="/" className="flex items-center" aria-label="Ana sayfa">
            <Logo boyut="sm" />
          </Link>
          <Link to="/giris" className="text-sm font-medium text-brand-700 hover:underline">
            Girişe dön
          </Link>
        </div>
      </header>

      <main className="mx-auto max-w-3xl px-4 py-10 pb-20">
        <h1 className="text-3xl font-bold tracking-tight text-slate-900">{baslik}</h1>
        {ozet && <p className="mt-3 text-[15px] leading-relaxed text-slate-600">{ozet}</p>}
        <p className="mt-4 text-xs text-slate-500">Son güncelleme: {sonGuncelleme}</p>

        {/*
          ⚠️ BU UYARI KALDIRILMADAN YAYINA ÇIKILMAMALI ya da metinler bir hukukçuya
          okutulup uyarı bilinçli olarak kaldırılmalı. Metinler ürünün KODUNU okuyarak
          yazıldı — yani hangi verinin gerçekten toplandığı, nerede saklandığı ve ne
          kadar durduğu doğru. Ama "doğru" ile "yeterli" aynı şey değil: KVKK'nın
          biçimsel gerekleri (veri sorumlusu kimlik bilgileri, VERBİS kaydı, açık rıza
          metinlerinin ayrıştırılması) hukuk işidir ve burada üretilemez.
        */}
        <div className="mt-6 rounded-xl border border-amber-200 bg-amber-50 p-4">
          <p className="text-sm font-semibold text-amber-900">Taslak metin</p>
          <p className="mt-1 text-sm leading-relaxed text-amber-900">
            Bu metin, platformun gerçekte ne yaptığı incelenerek hazırlanmış bir
            taslaktır ve yayına alınmadan önce bir hukukçu tarafından gözden
            geçirilmelidir.
          </p>
        </div>

        <div className="mt-8 space-y-8">{children}</div>

        <div className="mt-12 border-t border-slate-200 pt-6">
          <p className="text-sm text-slate-600">
            Sorular ve talepler için:{' '}
            <a href="mailto:iletisim@dersmate.com" className="font-medium text-brand-700 hover:underline">
              iletisim@dersmate.com
            </a>
          </p>
        </div>
      </main>
    </div>
  )
}

/** Numaralı bölüm başlığı + gövde. Metinlere sonradan atıf yapılabilmeli ("§4"). */
export function Bolum({ no, baslik, children }) {
  return (
    <section>
      <h2 className="text-lg font-semibold text-slate-900">
        <span className="text-slate-400">{no}.</span> {baslik}
      </h2>
      <div className="mt-3 space-y-3 text-[15px] leading-relaxed text-slate-700">{children}</div>
    </section>
  )
}

/** Metin içi madde listesi. */
export function Maddeler({ children }) {
  return <ul className="ml-5 list-disc space-y-2 text-[15px] leading-relaxed text-slate-700">{children}</ul>
}
