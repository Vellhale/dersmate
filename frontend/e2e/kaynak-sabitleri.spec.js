import { test, expect } from '@playwright/test'
import { createHash } from 'node:crypto'
import tailwindConfig from '../tailwind.config.js'
import { kaynakOku, kaynakDosyalari } from './yardimcilar.js'

/*
  KAYNAK SABİTLERİ — tarayıcı açmayan, dosya okuyan testler.

  Buradaki üç şey de derleme hatası vermeden, testler yeşilken, arayüz gayet iyi görünürken
  bozulabilir. Hepsi "zararsız düzeltme" kılığına girer. Bu yüzden tarayıcıda değil,
  kaynakta kilitleniyorlar.
*/

const palet = tailwindConfig.theme.extend.colors.brand

test.describe('HWID parmak izi dokunulmazlığı', () => {
  /*
    docs/DEVAM-EDILECEK.md, F4 altındaki uyarı: canvasSignal() içinde çizilen her şey
    hash'e giriyor. Tek bayt değişirse ÜRETİMDEKİ TÜM HWID BANLARI geçersiz olur ve
    banlı kullanıcılar geri döner. Geri alınamaz.

    Hash'i bilerek dosyanın TAMAMI üzerinden alıyoruz, yalnızca fonksiyon gövdesi
    üzerinden değil: yorumlar da bu korumanın parçası ve silinmemeli.

    BU TEST KIRILDIYSA: "beklenen hash'i güncelle" ÇÖZÜM DEĞİLDİR. Önce ne değiştiğine bak.
    Değişiklik canvasSignal() dışındaysa (ör. sha256Hex'e yorum eklendi) ve canvasSignal
    bayt bayt aynıysa, hash'i güncellemek doğrudur — aşağıdaki ikinci test bunu ayrıca
    kontrol ediyor.
  */
  /*
    2026-08-18'de güncellendi. Testin kendi kuralına göre yapıldı: canvasSignal() gövdesi
    İKİ KOPYADA DA bayt bayt aynıydı (sha256 7d4f6c77…), fark yalnızca fonksiyonun ÜSTÜNDEKİ
    uyarı yorumundaydı — F1b'den sonra "#4f46e5 artık paletteki hiçbir renge karşılık
    gelmiyor" bilgisini taşıyan sürüm alındı. Yani parmak izi değişmedi, ban listesi sağlam.
  */
  const BEKLENEN_HASH = '3fc255be649f1fb818f51a6ca819c4b1e58031ecf7e650c48bc15e4d226d863f'

  test('hwid.js bayt düzeyinde değişmedi', () => {
    /*
      SATIR SONU NORMALİZASYONU (2026-08-20): depo Drive'dan C:\projeler'e taşınırken
      dosya CRLF'e döndü ve hash sebepsiz kırmızıya düştü. Ölçüldü: LF'e çevrilen içerik
      beklenen hash'le BİREBİR eşleşiyor, yani içerik hiç değişmemişti — fark git/OS
      artefaktıydı. Tarayıcıda çalışan kod satır sonundan etkilenmez; canvas'a çizilen
      her şey aynıdır, banlar sağlamdır. Bu yüzden hash satır sonundan bağımsız alınır:
      koruma İÇERİĞİ kilitler, kopyalama artefaktını değil. Gerçek bir bayt değişikliği
      (yorum silme dahil) hâlâ yakalanır.
    */
    const kaynak = kaynakOku('src', 'lib', 'hwid.js').replace(/\r\n/g, '\n')
    const hash = createHash('sha256').update(kaynak, 'utf8').digest('hex')

    expect(
      hash,
      'hwid.js değişmiş. canvasSignal() içindeki HİÇBİR değer değişmediyse ve değişiklik ' +
        'gerçekten zararsızsa bu sabiti güncelle; aksi halde değişikliği geri al.',
    ).toBe(BEKLENEN_HASH)
  })

  test('canvasSignal içindeki iki tuzak sabiti yerinde duruyor', () => {
    const kaynak = kaynakOku('src', 'lib', 'hwid.js')

    // 1. tuzak: ürün adı dersmate olsa bile bu satır PeerLearn kalır (F4).
    expect(kaynak, "fillText('PeerLearn', 2, 2) kaldırılmış — ban listesi çöpe gider").toContain(
      "ctx.fillText('PeerLearn', 2, 2)",
    )

    // 2. tuzak: eski brand-600 ile aynı renk, ama tesadüfen. Palet değişince güncellenmez.
    expect(kaynak, "fillStyle '#4f46e5' değişmiş — ban listesi çöpe gider").toContain(
      "ctx.fillStyle = '#4f46e5'",
    )
  })

  test('parmak izi rengi paletten BAĞIMSIZ kaldı', () => {
    /*
      Asıl korunan şey bu. Yukarıdaki test '#4f46e5'in durduğunu söylüyor; bu test
      paletin ondan uzaklaştığını söylüyor. İkisi birlikte şunu kanıtlıyor: parmak izi
      rengi artık hiçbir marka tonuyla eşleşmiyor, yani "tutarlılık" gerekçesiyle
      güncellenmesi için bir bahane kalmadı.
    */
    const paletTonlari = Object.values(palet).map((t) => t.toLowerCase())
    expect(paletTonlari, 'palet yeniden #4f46e5 içeriyor — tuzak geri kuruldu').not.toContain(
      '#4f46e5',
    )
  })
})

test.describe('Marka paleti tek kaynakta', () => {
  test('marka tonu #0088CC skalada brand-500 olarak duruyor', () => {
    /*
      500, 400 DEĞİL. #0088CC beyaz yazıyla 3.89:1 veriyor ve gövde rengi olarak AA'yı
      (4.5) kaçırıyor; F1b bu yüzden marka kimliğini 500'de bırakıp zemin görevini
      600'e (#0077B3, 4.90:1) taşıdı. Ton skalanın ortasında sabit kalmalı — bir üst ya
      da alt basamağa kayarsa logo ile düğmeler farklı mavilere ayrılır.
    */
    expect(palet[500].toLowerCase()).toBe('#0088cc')
  })

  test('logo renkleri paletle birebir aynı', () => {
    /*
      Logo.jsx palete `import` edemiyor (SVG'nin içinde satır içi hex olmak zorunda), bu
      yüzden iki dosya elle senkron tutuluyor. Bu test o senkronu zorunlu kılıyor —
      aksi halde palet değişir, logo eski tonda kalır ve kimse fark etmez.
    */
    const logo = kaynakOku('src', 'components', 'Logo.jsx')
    const oku = (ad) => logo.match(new RegExp(`const ${ad} = '(#[0-9a-fA-F]{6})'`))?.[1]?.toLowerCase()

    expect(oku('ACCENT'), 'Logo ACCENT ile brand-500 ayrışmış').toBe(palet[500].toLowerCase())
    expect(oku('BG'), 'Logo BG ile brand-50 ayrışmış').toBe(palet[50].toLowerCase())
  })

  test('favicon, LogoMark ile aynı renkleri kullanıyor', () => {
    const favicon = kaynakOku('public', 'favicon.svg').toLowerCase()
    expect(favicon).toContain(palet[500].toLowerCase())
    expect(favicon).toContain(palet[50].toLowerCase())
  })

  test('src altında paletin dışında sabit renk yok', () => {
    /*
      Palet drift'ine karşı süpürge. Bir bileşene doğrudan '#0ea5e9' yazmak bugün doğru
      görünür, palet yarın değişince o bileşen geride kalır.

      İzinli üç istisna, üçü de gerekçeli:
        Logo.jsx      → SVG içi hex zorunlu; yukarıdaki test paletle eşitliğini koruyor.
        hwid.js       → parmak izi sabiti; PALETE BAĞLANMAMALI.
        AvatarPicker  → '#ffffff', JPEG'in saydamlık desteklememesinden gelen zemin.
        brand.js      → paletin JS köprüsü; marka tonlarını tailwind.config'ten OKUR
                        (kopyalamaz — aşağıdaki test bunu doğruluyor). İçindeki tek hex
                        marka dışı bir nötr (slate-900).
    */
    const muaf = [
      'src/lib/hwid.js',
      'src/components/Logo.jsx',
      'src/components/AvatarPicker.jsx',
      'src/lib/brand.js',
    ]

    const bulunanlar = []

    for (const yol of kaynakDosyalari('src')) {
      if (muaf.includes(yol)) continue
      const ham = kaynakOku(yol)
      // Yorumları çıkar: açıklamalarda geçen renk kodu ihlal değildir.
      const kod = ham.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1')
      for (const eslesme of kod.matchAll(/#[0-9a-fA-F]{6}\b/g)) {
        bulunanlar.push(`${yol}: ${eslesme[0]}`)
      }
    }

    expect(bulunanlar, 'Sabit renk yerine tailwind brand-* sınıfı kullan').toEqual([])
  })

  test('brand.js paleti KOPYALAMIYOR, tailwind.config.js ten okuyor', async () => {
    /*
      brand.js sabit renk denetiminden muaf; muafiyetin bedeli bu test.

      Dosya marka tonlarını elle yazsaydı denetimden yine geçerdi (muaf çünkü) ama palet
      değiştiğinde SVG gradyanları eski tonda kalırdı — yani muafiyet, kaçınmaya
      çalıştığımız sorunu geri getirirdi. İki şey birlikte kontrol ediliyor: kaynakta
      import var mı, ve çalışma zamanında dönen nesne palet nesnesinin TA KENDİSİ mi.
    */
    const kaynak = kaynakOku('src', 'lib', 'brand.js')
    expect(kaynak, 'brand.js paleti import etmiyor').toMatch(/from '\.\.\/\.\.\/tailwind\.config\.js'/)

    const { brand } = await import('../src/lib/brand.js')
    expect(brand).toEqual(palet)
    expect(brand[500].toLowerCase()).toBe('#0088cc')
  })
})
