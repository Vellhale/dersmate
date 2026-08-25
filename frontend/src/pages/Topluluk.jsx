import { useMemo, useState } from 'react'
import { useAuth } from '../state/AuthContext'
import { Avatar } from '../components/Avatar'
import { CamKart } from '../components/SayfaZemini'
import { Button, Field, Modal, Notice } from '../components/ui'
import {
  AlevIkonu,
  ArtanIkonu,
  BayrakIkonu,
  BilgiIkonu,
  KalkanIkonu,
  MesajIkonu,
  OyOkuIkonu,
  SaatIkonu,
  ToplulukIkonu,
  UyariIkonu,
} from '../components/Ikonlar'

/*
  ══════════════════════════════════════════════════════════════════════════════
  TOPLULUK — akran forumu.

  ⛔⛔ BU SAYFA HENÜZ SUNUCUYA BAĞLI DEĞİL. ÜRETİME ÇIKMADAN ÖNCE OKU. ⛔⛔

  Arayüz tamamlandı ve "Yakında" işaretleri ürün sahibinin kararıyla kaldırıldı
  (2026-08-25). Ama KALICILIK KATMANI YOK: her şey bu sekmenin belleğinde yaşıyor ve
  sayfa yenilenince sıfırlanıyor. Ekranda hiçbir uyarı KALMADIĞI için tek kayıt burası.

  Bağlanması gereken dört yer:

    1. GONDERILER / YORUMLAR sabitleri  → GET /api/community/posts, .../comments
    2. `ekGonderiler` / `ekYorumlar`     → POST uçları (şu an yalnızca state'e ekliyor)
    3. `oylar` state'i                    → POST .../vote (optimistik güncelleme kalır)
    4. `sikayetGonder`                    → POST .../report

  ⚠️ 4. MADDE EN RİSKLİSİ. Şikayet formu şu an kullanıcıya "iletildi" diyor ama hiçbir
  yere gitmiyor. Gerçek kullanıcıların eline bu hâliyle geçerse, kural ihlali bildiren
  herkes bildirdiğini sanıp beklemede kalır — kimsenin okumadığı bir şikayet kuyruğu,
  hiç olmayan bir şikayet düğmesinden daha zararlıdır. Uç açılana kadar bu sekme
  yayına ÇIKMAMALI.

  Yerel çalışan her şey (oy, sıralama, filtre, gönderi ve yorum yazma) sunucu geldiğinde
  yerinde kalıyor; değişen tek şey verinin nereden okunup nereye yazıldığı.

  ─── NEDEN REDDIT DÜZENİ ──────────────────────────────────────────────────────
  İstenen referans /liseliler tarzı bir akış. Oradan alınan üç şey var ve üçü de
  kararla alındı:

    1. SOL OY RAYI. Oy, gönderinin İÇERİĞİNDEN önce gelir; forumun sıralaması buna
       bağlı olduğu için kullanıcı "bu gönderi topluluk için ne değerde" bilgisini
       başlığı okumadan görüyor. Alt satıra konsaydı, taranan bir listede oy
       yorumların yanında bir sayı daha olurdu.
    2. BAŞLIK + ÖZET. Gönderi kartı içeriği bitirmez, açmaya davet eder — akış
       taranabilir kalmalı. Özet üç satırda kesiliyor (line-clamp-3).
    3. ETİKET (flair). Bir öğrenci forumunda "soru" ile "motivasyon" bambaşka iki
       okuma kipi; etiket, gönderiyi açmadan hangisine girdiğini söylüyor.

  ALINMAYAN ŞEY: Reddit'in yoğunluğu. Orada bir ekrana 12 gönderi sığar; burada 5.
  Bu ürünün geri kalanı (Keşfet, Derslerim) ferah kartlarla çalışıyor ve forum tek
  başına sıkışık bir liste olsaydı uygulamanın içinde başka bir uygulama gibi dururdu.

  ─── MODERASYON ARAYÜZÜ İKİNCİL DEĞİL, DÜZENİN PARÇASI ────────────────────────
  Aktif bir öğrenci forumunda spam, argo, izinsiz PDF ve trollemenin OLUP OLMAYACAĞI
  sorusu yok; ne zaman olacağı sorusu var. Bu yüzden önlemler sonradan eklenen bir
  panel değil, akışın kendisine yerleştirildi:

    • Her gönderide ve her yorumda "Şikayet et" — tek tık uzakta, ama sessiz.
    • Şikayet formu SEBEP SORUYOR. Tek düğmelik şikayet, moderatöre "biri bundan
      hoşlanmadı"dan başka bir şey söylemez; sebep, gelen yığını sıraya sokan şeydir.
    • Eşiği geçen gönderi AKIŞTA KAPALI gelir (bkz. `incelemede`). İçerik silinmiyor,
      perdeleniyor — "yine de göster" duruyor. Sessiz silme, moderasyonu görünmez ve
      tartışılamaz yapar.
    • Kurallar ve sınırlar sağ sütunda YAZILI. Yazılmamış kural, ihlal edildiğinde
      keyfî görünür.
    • Gönderi kutusu dosya yükleme SUNMUYOR. Telif ihlalinin bu üründeki en olası
      yolu izinsiz PDF paylaşımı ve en ucuz önlem, o yolu hiç açmamak.
  ══════════════════════════════════════════════════════════════════════════════
*/

/* ─── SIRALAMA ─────────────────────────────────────────────────────────────── */

const SIRALAMALAR = [
  { key: 'yeni', label: 'En Yeniler', Ikon: SaatIkonu, aciklama: 'Son paylaşılanlar önce.' },
  {
    key: 'oy',
    label: 'En Çok Oy Alanlar',
    Ikon: ArtanIkonu,
    aciklama: 'Topluluğun en çok işe yarar bulduğu gönderiler.',
  },
  {
    key: 'tartismali',
    label: 'Tartışmalı',
    Ikon: AlevIkonu,
    aciklama: 'Oyların ikiye bölündüğü, cevabı net olmayan başlıklar.',
  },
]

/*
  TARİH FİLTRESİ — sıralamadan AYRI bir eksen.

  Sıralama "hangisi önce gelsin", tarih filtresi "hangileri hiç görünmesin" diyor.
  İkisini tek bir listede birleştirmek (Reddit'in eski "top of the week" kalıbı gibi)
  seçenek sayısını 3'ten 12'ye çıkarırdı ve kullanıcı "En Yeniler / Bu ay"ın ne demek
  olduğunu tahmin etmek zorunda kalırdı. Ayrı duruyorlar, birlikte uygulanıyorlar.

  ⚠️ Filtre HER SIRALAMADA açık. Reddit tarih seçicisini yalnızca "top"/"controversial"
  için gösteriyor; burada gizlenmedi çünkü ortadan kaybolan bir denetim, kullanıcının
  "az önce buradaydı" diye aradığı bir şeye dönüşür. "En Yeniler + Bugün" da anlamlı
  bir soru: bugün ne konuşuldu?

  `dakika` alanı örnek verinin yaşını taşıyor; gerçek uçta bunun yerine sunucuya
  `since` parametresi gider ve filtre istemcide değil sorguda uygulanır.
*/
const ZAMAN_ARALIKLARI = [
  { key: 'hepsi', label: 'Tüm zamanlar', dakika: null },
  { key: 'gun', label: 'Bugün', dakika: 60 * 24 },
  { key: 'hafta', label: 'Bu hafta', dakika: 60 * 24 * 7 },
  { key: 'ay', label: 'Bu ay', dakika: 60 * 24 * 30 },
]

const ETIKETLER = [
  { key: 'hepsi', label: 'Tümü' },
  { key: 'stres', label: 'Sınav Stresi' },
  { key: 'soru', label: 'Soru Sor' },
  { key: 'kaynak', label: 'Kaynak' },
  { key: 'program', label: 'Ders Programı' },
  { key: 'motivasyon', label: 'Motivasyon' },
  { key: 'tercih', label: 'Tercih' },
]

/*
  Etiket renkleri. Hepsi 100/700-800 çiftleri — ui.jsx'teki Badge tonlarıyla aynı
  aile, yani forum kendi renk dilini kurmuyor, var olanı kullanıyor. Çiftler AA
  eşiğini geçiyor (en düşüğü violet-700/violet-100 ≈ 6.6:1).

  Marka mavisi SORU etiketine verildi: bu üründe soru sormak ana eylem ve marka rengi
  ana eylemi işaretliyor. Diğerleri marka dışı tonlar — altısı da mavi olsaydı etiket
  bir ayrım aracı olmaktan çıkardı.

  indigo BİLEREK YOK: e2e/marka.spec.js eski indigo tonlarını arayüzde arıyor.
*/
const ETIKET_TONU = {
  stres: 'bg-amber-100 text-amber-800',
  soru: 'bg-brand-100 text-brand-700',
  kaynak: 'bg-emerald-100 text-emerald-700',
  program: 'bg-violet-100 text-violet-700',
  motivasyon: 'bg-rose-100 text-rose-700',
  tercih: 'bg-sky-100 text-sky-800',
}

const ETIKET_ADI = Object.fromEntries(ETIKETLER.map((e) => [e.key, e.label]))

/* ─── ÖRNEK İÇERİK ─────────────────────────────────────────────────────────────
   Kişiler kurgusal. `renk` alanı yer tutucu avatarın rengini seçiyor (bkz.
   YerTutucuAvatar): gerçek Avatar bileşeni userId ile SUNUCUYA gidiyor ve burada
   var olmayan yedi kişi için yedi başarısız istek atardı.

   `arti` / `eksi` ayrı tutuluyor, tek bir "puan" değil: tartışmalı sıralaması ikisinin
   ORANINA bakıyor ve fark tek sayıya indirilseydi (184-6 ile 95-89 aynı 95'i verir)
   o sıralama hesaplanamazdı.                                                        */

const GONDERILER = [
  {
    id: 'g1',
    etiket: 'stres',
    baslik: 'Deneme netlerim düşünce panik oluyorum, sizde de böyle mi?',
    ozet:
      'Son üç denemede matematik netim 28’den 19’a indi. Çalışma temposunu değiştirmedim, konu ' +
      'eksiğim de yok — ama sınav başlayınca ilk zor soruda kafam duruyor ve gerisi çorba oluyor. ' +
      'Evde aynı soruyu iki dakikada çözüyorum. Bunu yaşayıp aşan var mı, ne yaptınız?',
    yazar: 'Elif A.',
    renk: 2,
    dakika: 22,
    arti: 184,
    eksi: 6,
  },
  {
    id: 'g2',
    etiket: 'soru',
    baslik: 'Limitte 0/0 belirsizliğini L’Hospital’sız kaldırmanın kısa yolu var mı?',
    ozet:
      'Hocam türev almadan çarpanlara ayırarak da çözülür diyor ama kökler girince ne yapacağımı ' +
      'şaşırıyorum. Eşleniğiyle genişletme dışında bir yöntem öğrenen var mı? Soruyu ve kendi ' +
      'çözüm denememi yazdım, nerede tıkandığımı göstermeye çalıştım.',
    yazar: 'Mert S.',
    renk: 0,
    dakika: 64,
    arti: 96,
    eksi: 3,
  },
  {
    id: 'g3',
    etiket: 'kaynak',
    baslik: 'Fizik soru bankası önerisi — PDF isteyen olmasın, hangisi işe yaradı onu yazın',
    ozet:
      'Geçen hafta aynı soruyu soran üç başlık gördüm ve hepsi “linki atar mısın”a döndü. Burada ' +
      'sadece kitabın adını ve neden işe yaradığını yazalım: hangi konuda kaç soru, çözümleri ' +
      'anlaşılır mı, seviyesi nasıl. Ben iki bankayı bitirdim, ikisini de karşılaştırdım.',
    yazar: 'Zeynep K.',
    renk: 4,
    dakika: 191,
    arti: 212,
    eksi: 11,
  },
  {
    id: 'g4',
    etiket: 'program',
    baslik: 'Günde 10 saat çalışmak gerçekten gerekli mi? Kendi programımı paylaşıyorum',
    ozet:
      'Altı saat verimli çalışıp geri kalan zamanda dinlenerek geçen yıl istediğim bölüme ' +
      'girdim. Buradaki “12 saat” paylaşımlarının kimseye iyi geldiğini düşünmüyorum. Programımı ' +
      'saat saat yazdım; katılmayan varsa tartışalım, ikna olmaya da açığım.',
    yazar: 'Kaan D.',
    renk: 3,
    dakika: 2880,
    arti: 143,
    eksi: 118,
  },
  {
    id: 'g5',
    etiket: 'motivasyon',
    baslik: 'Bir yıl önce burada “bırakıyorum” diye yazmıştım, dün kaydımı yaptırdım',
    ozet:
      'Geçen sene bu aylarda denemelerim çok kötüydü ve gerçekten bırakmayı düşünüyordum. O gün ' +
      'yazdığım başlığın altına yorum yazan herkese teşekkür ederim. Ne değiştirdiğimi ve neyi ' +
      'boşuna yaptığımı maddeler hâlinde yazdım — belki birine denk gelir.',
    yazar: 'Selin Y.',
    renk: 5,
    dakika: 7200,
    arti: 401,
    eksi: 9,
  },
  {
    id: 'g6',
    etiket: 'tercih',
    baslik: 'Sayısaldan eşit ağırlığa geçmek son sınıfta mantıklı mı?',
    ozet:
      'Matematiğim iyi, fen dörtlüsünde tıkandım. Tarih ve coğrafyayı sıfırdan çalışmak bu ' +
      'aşamada kayıp mı olur, yoksa fen netlerimi kovalamaktan daha mı hızlı? İki tarafı da ' +
      'yaşayan varsa gerçekten duymak istiyorum, çünkü tavsiyeler tam ikiye bölünmüş durumda.',
    yazar: 'Burak T.',
    renk: 1,
    dakika: 17280,
    arti: 88,
    eksi: 76,
  },
  {
    /*
      MODERASYON DURUMU ÖRNEĞİ — akışta kapalı gelir.

      Örnek bilerek TELİF İHLALİ: bu üründe en olası kural dışı paylaşım bu ve
      arayüzün ona nasıl davrandığını göstermenin en açık yolu, gerçek bir örneğini
      koymak. Gönderi silinmiş gibi davranılmıyor; kapalı geliyor, sebebi yazıyor ve
      kullanıcı isterse açabiliyor.
    */
    id: 'g7',
    etiket: 'kaynak',
    baslik: 'Bütün yayınların PDF’lerini bir klasöre topladım, link yorumlarda',
    ozet:
      'Aradığınız her kitabın taranmış hâli klasörde var, isteyene özelden de atarım. Ücretsiz ' +
      'olsun herkes faydalansın.',
    yazar: 'Anonim',
    renk: 0,
    dakika: 41,
    arti: 12,
    eksi: 64,
    incelemede: true,
    sikayetSayisi: 7,
  },
]

const YORUMLAR = {
  g1: [
    {
      id: 'y1',
      yazar: 'Kaan D.',
      renk: 3,
      dakika: 14,
      oy: 46,
      metin:
        'Bende de aynısıydı. Deneme sonrası nete değil, YANLIŞ SEBEBİNE bakmaya başlayınca ' +
        'düzeldi: her yanlışın yanına “bilmiyordum / dikkatsizlik / zaman” diye tek kelime ' +
        'yazdım. Üç denemede panik sorularının hepsinin “zaman” olduğunu gördüm, sorun konu ' +
        'değil tempoymuş.',
    },
    {
      id: 'y2',
      yazar: 'Zeynep K.',
      renk: 4,
      dakika: 52,
      oy: 28,
      metin:
        'Üç deneme bir eğilim değil, özellikle yayın değiştiyse. Aynı yayının denemesi mi? ' +
        'Farklıysa netlerin düşmesi senin değil denemenin zorluğuyla ilgili olabilir.',
    },
  ],
  g2: [
    {
      id: 'y3',
      yazar: 'Elif A.',
      renk: 2,
      dakika: 31,
      oy: 19,
      metin:
        'Kök varsa eşlenikle genişletme dışında pratik bir yol yok ama çarpanlara ayırmayı ' +
        'deneme sırasını değiştir: önce ortak parantez, sonra özdeşlik, en son eşlenik. ' +
        'Çoğu soru ikinci adımda bitiyor.',
    },
    {
      id: 'y4',
      yazar: 'Burak T.',
      renk: 1,
      dakika: 44,
      oy: 7,
      metin: 'Çözüm denemeni yazman çok iyi olmuş, hatanın nerede olduğu üçüncü satırda görünüyor.',
    },
  ],
  g3: [
    {
      id: 'y5',
      yazar: 'Mert S.',
      renk: 0,
      dakika: 96,
      oy: 63,
      metin:
        'Başlığın kendisi kural gibi olmuş, keşke her kaynak başlığı böyle açılsa. Ben de ' +
        'bitirdiğim iki bankayı konu konu karşılaştırıp yazayım.',
    },
    {
      id: 'y6',
      yazar: 'Selin Y.',
      renk: 5,
      dakika: 120,
      oy: 34,
      metin:
        'Kitabın zor olması iyi olduğu anlamına gelmiyor. Çözemediğin soru bankası seni ' +
        'çalıştırmıyor, yalnızca yıpratıyor — seviyeni yazan yorumlara bakın.',
    },
  ],
  g4: [
    {
      id: 'y7',
      yazar: 'Zeynep K.',
      renk: 4,
      dakika: 2400,
      oy: 51,
      metin:
        'Altı saat verimliyse on saat de verimli olabilir; ikisi birbirinin alternatifi değil. ' +
        'Asıl mesele saat değil, o saatin içinde kaç soru çözdüğün.',
    },
    {
      id: 'y8',
      yazar: 'Elif A.',
      renk: 2,
      dakika: 2760,
      oy: 39,
      metin:
        'Katılmıyorum ama programı saat saat paylaştığın için teşekkürler — tartışılacak somut ' +
        'bir şey olması iyi.',
    },
  ],
  g5: [
    {
      id: 'y9',
      yazar: 'Kaan D.',
      renk: 3,
      dakika: 6000,
      oy: 88,
      metin: 'Geçen seneki başlığını hatırlıyorum. Tebrikler, gerçekten.',
    },
    {
      id: 'y10',
      yazar: 'Burak T.',
      renk: 1,
      dakika: 6900,
      oy: 25,
      metin:
        '“Neyi boşuna yaptım” kısmı en değerlisi. Herkes ne yaptığını yazıyor, neyi bıraktığını ' +
        'yazan çok az.',
    },
  ],
  g6: [
    {
      id: 'y11',
      yazar: 'Selin Y.',
      renk: 5,
      dakika: 15000,
      oy: 33,
      metin:
        'Ben geçtim ve pişman değilim, ama şunu bilerek geç: tarih ezber değil, kronoloji ' +
        'kurma işi. Sıfırdan çalışmak dört ay sürüyor, üç değil.',
    },
    {
      id: 'y12',
      yazar: 'Mert S.',
      renk: 0,
      dakika: 16800,
      oy: 30,
      metin:
        'Fen dörtlüsünde tıkanmak genelde fizik kaynaklı oluyor. Geçmeden önce sadece fiziğe ' +
        'iki hafta ver, karar o zaman daha net olur.',
    },
  ],
  g7: [],
}

/*
  ŞİKAYET SEBEPLERİ — beş tanesi bu ürünün gerçek risklerine birebir karşılık geliyor,
  altıncısı ("Diğer") açık uç. Serbest metin ZORUNLU DEĞİL ama "Diğer" seçildiğinde
  gerekli: sebepsiz bir "diğer", moderatör kuyruğunda okunamayan bir satırdır.

  Sıra rastgele değil, BEKLENEN SIKLIĞA göre: spam ve dil ihlali her forumda ilk
  ikidir; kullanıcı listenin başında aradığını bulursa formu okumadan geçer.
*/
const SIKAYET_SEBEPLERI = [
  { key: 'spam', baslik: 'Spam veya reklam', aciklama: 'Satış, yönlendirme bağlantısı, tekrar eden gönderi.' },
  { key: 'dil', baslik: 'Hakaret, argo veya taciz', aciklama: 'Kişiye yönelik saldırı ya da aşağılayıcı dil.' },
  {
    key: 'telif',
    baslik: 'Telif ihlali',
    aciklama: 'İzinsiz kitap, PDF, deneme ya da video paylaşımı.',
  },
  {
    key: 'kisisel',
    baslik: 'Kişisel bilgi paylaşımı',
    aciklama: 'Telefon, adres, sosyal hesap — kendisinin ya da başkasının.',
  },
  { key: 'konudisi', baslik: 'Konu dışı veya trolleme', aciklama: 'Tartışmayı bilerek bozan içerik.' },
  { key: 'diger', baslik: 'Diğer', aciklama: 'Yukarıdakilere girmiyorsa kısaca anlat.' },
]

/* Kurallar kullanıcıya GÖRÜNÜR yerde duruyor: yazılmamış kural, uygulandığında keyfî
   görünür ve moderasyona duyulan güveni bitirir. */
/*
  ⚠️ "AYNI SORUYU TEKRAR AÇMA" KURALI KALDIRILDI (2026-08-25, ürün sahibi kararı).
  Tekrar başlık açmakta kısıtlama YOK ve bu bilinçli: bir öğrenci aynı soruyu ikinci kez
  soruyorsa çoğu zaman ilk cevabı anlamamıştır. Onu "zaten sorulmuştu" diye geri
  çevirmek, forumun var oluş sebebine ters. Kuralı geri eklemeden önce bu notu oku.
*/
const KURALLAR = [
  'Argo, hakaret ve kişisel saldırı yok. Fikre karşı çık, kişiye değil.',
  'Telif hakkı olan kitap, PDF ve denemeleri paylaşma — kaynağın adını yaz, dosyasını değil.',
  'Reklam, satış ve yönlendirme bağlantısı yasak.',
  'Kendinin ya da başkasının telefon, adres ve sosyal hesap bilgisini paylaşma.',
]

/* Arayüzde görünen sınırlar. Bir kısmı kuralları ihlal etmeyi ZORLAŞTIRIYOR (dosya
   yükleme yok), bir kısmı ihlalin MALİYETİNİ düşürüyor (otomatik incelemeye alma). */
const ONLEMLER = [
  { baslik: 'Yalnızca metin', metin: 'Dosya yükleme kapalı; izinsiz PDF paylaşımının yolu hiç açılmıyor.' },
  { baslik: 'Yeni hesap sınırı', metin: 'İlk hafta günde en fazla 3 gönderi — spam duvarı.' },
  { baslik: 'Bağlantı eşiği', metin: 'Dışarıya bağlantı paylaşımı 3. seviyeden itibaren açılıyor.' },
  { baslik: 'Otomatik inceleme', metin: 'Kısa sürede 3 şikayet alan gönderi akışta kapatılır.' },
]

/* ─── YARDIMCILAR ──────────────────────────────────────────────────────────── */

/** "22 dk" / "3 sa" / "2 g". Forumda mutlak tarih işe yaramıyor: okuyanın sorduğu şey
    "ne zaman yazıldı" değil, "hâlâ taze mi". */
function zamanKisalt(dakika) {
  // Az önce yazılan gönderi/yorum "0 dk" gösteriyordu; sayı doğruydu ama okunuşu
  // bozuktu — sıfır birimli bir süre, süre değil.
  if (dakika < 1) return 'şimdi'
  if (dakika < 60) return `${dakika} dk`
  const saat = Math.floor(dakika / 60)
  if (saat < 24) return `${saat} sa`
  return `${Math.floor(saat / 24)} g`
}

/**
 * Tartışma puanı — Reddit'in "controversial" ölçüsünün sadeleştirilmiş hâli.
 *
 * Fark (artı − eksi) DEĞİL, oranla ağırlıklandırılmış TOPLAM kullanılıyor ve sebebi
 * şu: 184/6 ile 95/89 aynı farkı (178 değil ama benzer mantıkla) verebiliyor, oysa
 * biri fikir birliği diğeri kavga. Tartışmalı olan, çok oy alan değil ZIT oy alandır.
 *
 * Tek yönlü gönderiler (eksi ya da artı sıfır) doğrudan 0 alıyor: bir gönderi kimse
 * karşı çıkmadan tartışmalı olamaz.
 */
function tartismaPuani({ arti, eksi }) {
  if (arti === 0 || eksi === 0) return 0
  const oran = Math.min(arti, eksi) / Math.max(arti, eksi)
  return (arti + eksi) * oran
}

/**
 * Yer tutucu avatar — ÖRNEK KİŞİLER İÇİN.
 *
 * Gerçek <Avatar> burada kullanılamıyor: o, userId ile sunucuya gidip fotoğraf
 * indiriyor ve bu sayfadaki yedi kişi kurgusal. Kullanılsaydı her sayfa açılışında
 * var olmayan kullanıcılar için başarısız istekler atılırdı.
 *
 * Renk `renk` indeksinden geliyor, isimden türetilmiyor: örnek veri elle yazıldığı
 * için hangi kartın hangi rengi alacağı da elle seçilebiliyor ve akışta yan yana iki
 * aynı renk düşmüyor. Sunucu geldiğinde bu bileşen silinir, yerine <Avatar userId>
 * gelir — düzen değişmez, ikisi de aynı ölçüde bir kare.
 */
const YER_TUTUCU_RENKLER = [
  'bg-brand-600',
  'bg-sky-600',
  'bg-emerald-600',
  'bg-amber-600',
  'bg-rose-600',
  'bg-violet-600',
]

/* Ölçüler Avatar.jsx'in SIZES tablosuyla aynı ailede (kare + yumuşak köşe); `xs`
   yalnızca burada var, akış satırındaki yazar adının önünde duruyor. Halka (ring)
   yalnızca sm ve üstünde: 24px'lik bir karede 2px beyaz halka, harflere ayrılan yeri
   gözle görülür biçimde yiyor. */
const YER_TUTUCU_OLCULER = {
  xs: 'h-6 w-6 rounded-md text-[10px]',
  sm: 'h-8 w-8 rounded-lg text-xs ring-2 ring-white',
  md: 'h-10 w-10 rounded-xl text-sm ring-2 ring-white',
}

function YerTutucuAvatar({ ad, renk = 0, boyut = 'md' }) {
  const olcu = YER_TUTUCU_OLCULER[boyut] ?? YER_TUTUCU_OLCULER.md
  const basHarfler = ad
    .split(' ')
    .filter(Boolean)
    .map((k) => k[0])
    .slice(0, 2)
    .join('')
    .toLocaleUpperCase('tr-TR')

  return (
    <div
      aria-hidden="true"
      className={`grid shrink-0 place-items-center font-bold text-white
                  ${olcu} ${YER_TUTUCU_RENKLER[renk % YER_TUTUCU_RENKLER.length]}`}
    >
      {basHarfler}
    </div>
  )
}

function EtiketPili({ etiket, className = '' }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold
                  ${ETIKET_TONU[etiket] ?? 'bg-slate-100 text-slate-700'} ${className}`}
    >
      {ETIKET_ADI[etiket] ?? etiket}
    </span>
  )
}

/* ─── SAYFA ────────────────────────────────────────────────────────────────── */

export default function Topluluk() {
  const { session } = useAuth()

  const [sira, setSira] = useState('yeni')
  const [zaman, setZaman] = useState('hepsi')
  const [etiket, setEtiket] = useState('hepsi')
  /* Oylar: { [gonderiId]: 1 | -1 }. Kullanıcının kendi oyu, gönderinin sayısından AYRI
     tutuluyor — sıralama tabandaki sayılara bakıyor (aşağıdaki nota bkz.). */
  const [oylar, setOylar] = useState({})
  const [acikYorum, setAcikYorum] = useState(null)
  const [acilanGizli, setAcilanGizli] = useState([])
  const [sikayetHedefi, setSikayetHedefi] = useState(null)
  const [bildirim, setBildirim] = useState(null)
  const [yaziyor, setYaziyor] = useState(false)

  /*
    KULLANICININ BU OTURUMDA YAZDIKLARI.

    Sabit listeyle BİRLEŞTİRİLMİYOR, önüne EKLENİYOR (aşağıda) — sabitler modül
    seviyesinde ve onları mutasyona uğratmak, sayfadan çıkıp geri gelindiğinde
    birikmiş bir listeye yol açardı. State sayfa ömrü kadar yaşıyor; sunucu geldiğinde
    bu iki alan POST yanıtıyla doluyor, düzen değişmiyor.
  */
  const [ekGonderiler, setEkGonderiler] = useState([])
  const [ekYorumlar, setEkYorumlar] = useState({})

  /*
    SIRALAMA KULLANICININ KENDİ OYUNU HESABA KATMIYOR — bilerek.

    Kattığı anda "En Çok Oy Alanlar" listesinde bir gönderiye oy vermek, o gönderiyi
    parmağının altından kaydırırdı: kullanıcı bir şeye oy verir, liste yeniden sıralanır
    ve az önce okuduğu kart başka bir yere gider. Reddit de aynı sebeple sıralamayı
    anlık oyla yenilemiyor. Görünen sayı hemen değişiyor (geri bildirim orada), yalnızca
    SIRA sabit kalıyor.
  */
  const akis = useMemo(() => {
    /* Önce DARALT, sonra sırala. İki eksen bağımsız: tarih ve etiket hangi gönderilerin
       görüneceğini, sıralama görünenlerin hangi düzende duracağını belirliyor. */
    const sinir = ZAMAN_ARALIKLARI.find((z) => z.key === zaman)?.dakika ?? null

    const secili = [...ekGonderiler, ...GONDERILER].filter(
      (g) =>
        (etiket === 'hepsi' || g.etiket === etiket) && (sinir === null || g.dakika <= sinir),
    )

    const kopya = [...secili]
    if (sira === 'yeni') kopya.sort((a, b) => a.dakika - b.dakika)
    else if (sira === 'oy') kopya.sort((a, b) => b.arti - b.eksi - (a.arti - a.eksi))
    else kopya.sort((a, b) => tartismaPuani(b) - tartismaPuani(a))

    return kopya
  }, [sira, zaman, etiket, ekGonderiler])

  const oyVer = (id, yon) => {
    setOylar((onceki) => {
      const yeni = { ...onceki }
      // Aynı yöne ikinci tık oyu GERİ ALIR: oy vermenin geri dönüşü olmalı, yoksa
      // yanlışlıkla basılan bir ok kalıcı bir karara dönüşür.
      if (yeni[id] === yon) delete yeni[id]
      else yeni[id] = yon
      return yeni
    })
  }

  const sikayetGonder = () => {
    setSikayetHedefi(null)
    setBildirim('Şikayetin iletildi. Moderasyon ekibi inceleyip sonucunu sana bildirecek.')
  }

  /*
    YENİ GÖNDERİ. `dakika: 0` — az önce yazıldı, yani "En Yeniler"de başa geçiyor ve
    her tarih aralığına giriyor. Kendi oyu 1: kimse kendi gönderisini sıfır oyla
    görmüyor, yazarın kendi yukarı oyu varsayılan (Reddit'te de öyle).

    id, sayaçtan değil UZUNLUKTAN türetiliyor ve bu yeterli: liste yalnızca büyüyor,
    silme yok. Sunucu geldiğinde id oradan gelecek.
  */
  const gonderiEkle = ({ baslik, etiket: yeniEtiket, ozet }) => {
    const id = `yerel-${ekGonderiler.length + 1}`
    setEkGonderiler((l) => [
      { id, etiket: yeniEtiket, baslik, ozet, yazar: session?.displayName ?? 'Sen', renk: 1, dakika: 0, arti: 1, eksi: 0 },
      ...l,
    ])
    setYaziyor(false)
    // Yazdığı şeyi görebilsin: filtreler onu gizliyor olabilir, o yüzden akış
    // varsayılana dönüyor. Sessizce "kayboldu" görünen bir gönderi, kullanıcıya
    // paylaşımın başarısız olduğunu düşündürür.
    setSira('yeni')
    setZaman('hepsi')
    setEtiket('hepsi')
    setAcikYorum(null)
    setBildirim('Gönderin paylaşıldı.')
  }

  const yorumEkle = (gonderiId, metin) => {
    setEkYorumlar((mevcut) => {
      const oncekiler = mevcut[gonderiId] ?? []
      return {
        ...mevcut,
        [gonderiId]: [
          ...oncekiler,
          {
            id: `${gonderiId}-yerel-${oncekiler.length + 1}`,
            yazar: session?.displayName ?? 'Sen',
            renk: 1,
            dakika: 0,
            oy: 1,
            metin,
          },
        ],
      }
    })
  }

  const seciliSiralama = SIRALAMALAR.find((s) => s.key === sira)

  return (
    <div className="space-y-6">
      {/* ── BAŞLIK ─────────────────────────────────────────────────────────── */}
      <header>
        <h1 className="text-2xl font-bold text-slate-900">Topluluk</h1>
        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-slate-600">
          Sınav stresinden soru çözümüne, kaynak tartışmasından tercih kararına — herkesin aynı
          sıralarda olduğu ortak alan. Ders almak için eşleşmene gerek yok; buraya yazıp
          topluluğa sorabilirsin.
        </p>
      </header>

      {bildirim && (
        <Notice tone="success" onDismiss={() => setBildirim(null)}>
          {bildirim}
        </Notice>
      )}

      {/*
        İKİ SÜTUN: akış + kurallar. Kurallar sütunu lg altında akışın ALTINA düşüyor
        (ızgara sırası doğal akış sırası) — mobilde forumun kendisinden önce beş maddelik
        bir kural listesi okutmak, kimsenin okumadığı bir duvar üretirdi. Masaüstünde ise
        yan sütun boş alanı dolduruyor ve kurallar akışla aynı anda görünüyor.
      */}
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="min-w-0 space-y-4">
          <GonderiKutusu session={session} onAc={() => setYaziyor(true)} />

          <SiralamaSeridi
            sira={sira}
            onSira={setSira}
            zaman={zaman}
            onZaman={setZaman}
            etiket={etiket}
            onEtiket={setEtiket}
            aciklama={seciliSiralama?.aciklama}
            sonuc={akis.length}
          />

          {akis.length === 0 ? (
            <CamKart className="py-10 text-center">
              {/* Boş sonuç iki sebepten gelebilir (etiket ya da tarih) ve hangisi
                  olduğunu söylemek yerine ikisini birden gösteriyoruz: yanlış sebebi
                  tahmin eden bir metin, kullanıcıyı çalışmayan düzeltmeye yollar. */}
              <p className="text-sm font-semibold text-slate-900">
                Bu filtrelerle gösterilecek gönderi yok.
              </p>
              <p className="mt-1 text-sm text-slate-600">
                Tarih aralığını genişlet ya da etiketi “Tümü”ne al.
              </p>
            </CamKart>
          ) : (
            <div className="space-y-4">
              {akis.map((gonderi) => (
                <GonderiKarti
                  key={gonderi.id}
                  gonderi={gonderi}
                  /* Sabit yorumlar + bu oturumda yazılanlar. Birleştirme BURADA, kartın
                     içinde değil: kart hangi yorumun nereden geldiğini bilmek zorunda
                     değil, yalnızca listeyi çiziyor. */
                  yorumlar={[...(YORUMLAR[gonderi.id] ?? []), ...(ekYorumlar[gonderi.id] ?? [])]}
                  oy={oylar[gonderi.id] ?? 0}
                  onOy={oyVer}
                  yorumlarAcik={acikYorum === gonderi.id}
                  onYorumlar={() =>
                    setAcikYorum((mevcut) => (mevcut === gonderi.id ? null : gonderi.id))
                  }
                  onYorumYaz={(metin) => yorumEkle(gonderi.id, metin)}
                  gizliAcik={acilanGizli.includes(gonderi.id)}
                  onGizliAc={() => setAcilanGizli((l) => [...l, gonderi.id])}
                  onSikayet={setSikayetHedefi}
                />
              ))}
            </div>
          )}
        </div>

        <aside className="space-y-4">
          <KurallarKarti />
          <OnlemlerKarti />
        </aside>
      </div>

      <GonderiModali open={yaziyor} onClose={() => setYaziyor(false)} onPaylas={gonderiEkle} />

      <SikayetModali
        hedef={sikayetHedefi}
        onClose={() => setSikayetHedefi(null)}
        onGonder={sikayetGonder}
      />
    </div>
  )
}

/* ─── GÖNDERİ KUTUSU ───────────────────────────────────────────────────────── */

/*
  Gönderi kutusu — forumun ANA EYLEMİ. Bir akış, yazma yolu görünmeden anlaşılmıyor:
  kullanıcı "burada ben ne yapıyorum" sorusunun cevabını gönderilerden değil bu
  kutudan alıyor.

  Gerçek bir <input> DEĞİL, MODALI AÇAN bir <button>. Sebep: gönderi başlık + etiket +
  metin istiyor, yani tek satırlık bir kutuya sığmıyor. Satır içi bir alan kullanıcıya
  "bir cümle yaz ve gönder" diye söz verip sonra üç alanlık bir forma çıkarırdı;
  düğme baştan doğru sözü veriyor. (Twitter satır içi yazdırıyor çünkü orada başlık ve
  etiket yok; Reddit modal açıyor çünkü var.)

  DOSYA EKLEME DÜĞMESİ YOK ve bu tasarımın kendisi bir önlem: telif ihlalinin bu üründe
  en olası yolu izinsiz PDF paylaşımı; en ucuz çözüm, o yolu arayüzde hiç açmamak.
  Kutunun altındaki şerit bunu kural olarak da söylüyor.
*/
function GonderiKutusu({ session, onAc }) {
  return (
    <CamKart className="p-4">
      <div className="flex items-center gap-3">
        <Avatar userId={session?.userId} name={session?.displayName} size="sm" />
        <button
          type="button"
          onClick={onAc}
          className="min-h-11 min-w-0 flex-1 truncate rounded-xl border border-slate-200
                     bg-white/70 px-4 py-2.5 text-left text-sm text-slate-500 transition
                     hover:border-brand-300 hover:bg-white hover:text-slate-700"
        >
          Bir soru sor ya da neler olduğunu anlat…
        </button>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1.5 border-t border-slate-200/70 pt-3">
        {['Yalnızca metin', 'Dosya yükleme kapalı', 'Etiket seçmek zorunlu'].map((madde) => (
          <span key={madde} className="flex items-center gap-1.5 text-xs text-slate-600">
            <BilgiIkonu className="h-3.5 w-3.5 text-slate-400" />
            {madde}
          </span>
        ))}
      </div>
    </CamKart>
  )
}

/* ─── YENİ GÖNDERİ MODALI ──────────────────────────────────────────────────── */

/*
  Üç alan: başlık, etiket, metin. Dördüncüsü yok ve olmayacak — dosya eki, bağlantı
  alanı ve anket, hepsi ayrı birer moderasyon yükü açıyor.

  ALT SINIRLAR (başlık 10, metin 20 karakter) BİR KALİTE KAPISI. "yardım" diye açılan
  tek kelimelik başlıklar bir forumu en hızlı bozan şey; kimseye cevap veremeyecek
  kadar boş bir gönderi, paylaşılmadan önce durdurulmalı. Sayılar düşük tutuldu: amaç
  yazmayı zorlaştırmak değil, boş göndermeyi engellemek.

  ETİKET ZORUNLU. Etiketsiz gönderilere izin verilseydi çoğu etiketsiz gelirdi (en az
  dirençli yol) ve akıştaki filtre şeridi işe yaramaz hâle gelirdi.

  Kural hatırlatması formun İÇİNDE, yan sütunda değil: kuralı okumanın en anlamlı anı,
  onu çiğneyebileceğin an.
*/
function GonderiModali({ open, onClose, onPaylas }) {
  const [baslik, setBaslik] = useState('')
  const [etiket, setEtiket] = useState('')
  const [metin, setMetin] = useState('')

  const temizle = () => {
    setBaslik('')
    setEtiket('')
    setMetin('')
  }

  const kapat = () => {
    temizle()
    onClose()
  }

  const paylasilabilir =
    baslik.trim().length >= 10 && etiket !== '' && metin.trim().length >= 20

  const paylas = () => {
    onPaylas({ baslik: baslik.trim(), etiket, ozet: metin.trim() })
    temizle()
  }

  return (
    <Modal
      open={open}
      onClose={kapat}
      title="Yeni gönderi"
      genis
      footer={
        <>
          <Button variant="secondary" onClick={kapat}>
            Vazgeç
          </Button>
          <Button onClick={paylas} disabled={!paylasilabilir}>
            Paylaş
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Başlık" hint="Sorunu tek cümlede özetle — akışta önce bu okunuyor.">
          <input
            className="input"
            value={baslik}
            onChange={(e) => setBaslik(e.target.value)}
            maxLength={120}
            placeholder="Örn. Deneme netlerim düşünce panik oluyorum, sizde de böyle mi?"
          />
        </Field>

        <Field label="Etiket" hint="Gönderinin hangi başlıkta okunacağını belirler.">
          <select className="input" value={etiket} onChange={(e) => setEtiket(e.target.value)}>
            <option value="">Seç…</option>
            {/* 'hepsi' bir etiket değil, filtrenin "tümü" seçeneği — burada listelenmez. */}
            {ETIKETLER.filter((e) => e.key !== 'hepsi').map(({ key, label }) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Ne olduğunu anlat" hint="Ayrıntı ver: ne denedin, nerede tıkandın.">
          <textarea
            className="input h-40 resize-none"
            value={metin}
            onChange={(e) => setMetin(e.target.value)}
            maxLength={2000}
            placeholder="Durumu birkaç cümleyle anlat…"
          />
        </Field>

        <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
          <p className="flex items-center gap-2 text-xs font-semibold text-slate-800">
            <KalkanIkonu className="h-4 w-4 text-slate-500" />
            Paylaşmadan önce
          </p>
          <ul className="mt-2 space-y-1">
            {[
              'Telif hakkı olan kitap, PDF ve deneme paylaşma — kaynağın adını yaz.',
              'Telefon, adres ve sosyal hesap bilgisi yazma.',
              'Reklam ve yönlendirme bağlantısı yasak.',
            ].map((madde) => (
              <li key={madde} className="text-xs leading-relaxed text-slate-600">
                {madde}
              </li>
            ))}
          </ul>
        </div>
      </div>
    </Modal>
  )
}

/* ─── SIRALAMA + ETİKET ŞERİDİ ─────────────────────────────────────────────── */

/*
  Sıralama ŞERİT (segment), açılır menü değil: üç seçenek var ve üçü de aynı anda
  görünüyor — açılır menü, seçenekleri görmek için fazladan bir tık isterdi ve
  kullanıcı "başka nasıl sıralayabilirim"i hiç öğrenmezdi. Discover'daki YKS/Üniversite
  şeridiyle aynı bileşen dili; uygulama içinde ikinci bir sekme biçimi doğmuyor.

  ⚠️ MOBİLDE ŞERİT DİKEY. "En Çok Oy Alanlar" uzun bir etiket; 320px'te üç düğme tek
  satıra sığmıyor. Yatay kaydırma seçilmedi — kaydırılabildiği görünmeyen bir şerit,
  gizli seçenek demektir. Onun yerine düğmeler sm altında TAM GENİŞLİK alıp alt alta
  diziliyor: sarma zaten oluyordu, `w-full` onu kazaya değil karara çeviriyor (seçili
  olan satırın tamamını dolduruyor, yarısını değil).

  Etiket pilleri ayrı bir satırda ve SIRALAMADAN sonra: ikisi farklı sorular (“neye
  göre sıralansın” / “ne konuşulsun”) ve aynı satıra konsalar tek bir denetim gibi
  okunurlardı.
*/
function SiralamaSeridi({ sira, onSira, zaman, onZaman, etiket, onEtiket, aciklama, sonuc }) {
  const zamanAdi = ZAMAN_ARALIKLARI.find((z) => z.key === zaman)?.label

  return (
    <CamKart className="p-4">
      {/* Sıralama solda, tarih filtresi sağda: aynı satır, ama aynı denetim değil.
          Dar ekranda ikisi de tam genişliğe geçip alt alta diziliyor. */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div
          className="flex w-full flex-wrap gap-1 rounded-xl bg-slate-100 p-1 sm:inline-flex sm:w-auto"
          role="tablist"
          aria-label="Gönderi sıralaması"
        >
          {SIRALAMALAR.map(({ key, label, Ikon }) => (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={sira === key}
              onClick={() => onSira(key)}
              className={`flex min-h-11 w-full items-center gap-2 rounded-lg px-3 text-sm
                          font-medium transition sm:w-auto lg:min-h-9 ${
                            sira === key
                              ? 'bg-white text-brand-700 shadow-sm'
                              : 'text-slate-600 hover:text-slate-900'
                          }`}
            >
              <Ikon className="h-4 w-4" strokeWidth={sira === key ? 2.4 : 2} />
              {label}
            </button>
          ))}
        </div>

        {/*
          TARİH FİLTRESİ — yerel <select>, özel açılır menü DEĞİL.

          Dört seçenekli, tek seçimli bir daraltma için özel bir popover yazmak; odak
          tuzağı, Esc, dışarı tıklama, ok tuşlarıyla gezinme ve mobil klavye davranışını
          elde yeniden kurmak demek. Yerel select bunların hepsini işletim sisteminden
          getiriyor ve mobilde parmakla kullanılan asıl doğru denetim o. Uygulamanın
          geri kalanı da aynı kalıbı kullanıyor (Keşfet ve Derslerim modalleri).

          .input sınıfı 16px punto veriyor: iOS, 16px'ten küçük yazılı bir alana
          odaklanınca sayfayı otomatik yakınlaştırıyor (bkz. index.css).

          Görünür etiket yerine aria-label: "Tarih" diye bir başlık koymak satıra
          üçüncü bir metin ekliyordu ve seçili değerin kendisi ("Bu hafta") zaten ne
          olduğunu söylüyor.
        */}
        <select
          className="input sm:w-auto"
          value={zaman}
          onChange={(e) => onZaman(e.target.value)}
          aria-label="Tarih filtresi"
        >
          {ZAMAN_ARALIKLARI.map(({ key, label }) => (
            <option key={key} value={key}>
              {label}
            </option>
          ))}
        </select>
      </div>

      {/* Seçilen sıralamanın ne yaptığı YAZIYOR: "Tartışmalı" hiçbir kullanıcının
          tahmin edemeyeceği bir ölçüt ve etiketin kendisi bunu anlatmıyor. Tarih
          aralığı da burada tekrar ediyor — sonuç sayısının neden düştüğü, sayının
          yanında yazmazsa fark edilmiyor. */}
      <p className="mt-3 text-xs text-slate-600">
        {aciklama} <span className="text-slate-400" aria-hidden="true">·</span> {zamanAdi}{' '}
        <span className="text-slate-400" aria-hidden="true">·</span> {sonuc} gönderi
      </p>

      <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-200/70 pt-4">
        {ETIKETLER.map(({ key, label }) => (
          <button
            key={key}
            type="button"
            aria-pressed={etiket === key}
            onClick={() => onEtiket(key)}
            className={`min-h-11 rounded-full border px-3 text-xs font-semibold transition lg:min-h-9 ${
              etiket === key
                ? 'border-brand-600 bg-brand-600 text-white'
                : 'border-slate-200 bg-white text-slate-600 hover:border-brand-300 hover:text-brand-700'
            }`}
          >
            {label}
          </button>
        ))}
      </div>
    </CamKart>
  )
}

/* ─── GÖNDERİ KARTI ────────────────────────────────────────────────────────── */

function GonderiKarti({
  gonderi,
  yorumlar,
  oy,
  onOy,
  yorumlarAcik,
  onYorumlar,
  onYorumYaz,
  gizliAcik,
  onGizliAc,
  onSikayet,
}) {
  /*
    İNCELEMEDEKİ GÖNDERİ AKIŞTA KAPALI GELİR.

    Silinmiyor, perdeleniyor. İkisi arasındaki fark moderasyonun görünürlüğü: sessizce
    silinen içerik, hem yazarına hem okuyanına hiçbir şey söylemez ve "burada sansür
    var mı" sorusunu cevaplanamaz hâle getirir. Perde ise sebebi yazıyor, sayıyı
    veriyor ve kararı okuyana bırakıyor.
  */
  if (gonderi.incelemede && !gizliAcik) {
    return (
      <CamKart className="border-amber-200/80 bg-amber-50/70 p-4">
        <div className="flex items-start gap-3">
          <UyariIkonu className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div className="min-w-0">
            <p className="text-sm font-semibold text-slate-900">Bu gönderi incelemede</p>
            <p className="mt-1 text-sm leading-relaxed text-slate-700">
              {gonderi.sikayetSayisi} kişi topluluk kurallarını ihlal ettiğini bildirdi. Moderasyon
              sonuçlanana kadar akışta kapalı tutuluyor.
            </p>
            <div className="mt-3 flex flex-wrap items-center gap-3">
              <button
                type="button"
                onClick={onGizliAc}
                className="min-h-11 rounded-lg border border-amber-300 bg-white px-3 text-xs
                           font-semibold text-amber-900 transition hover:bg-amber-100 lg:min-h-9"
              >
                Yine de göster
              </button>
              <span className="text-xs text-slate-600">Etiket: {ETIKET_ADI[gonderi.etiket]}</span>
            </div>
          </div>
        </div>
      </CamKart>
    )
  }

  return (
    <CamKart className="p-0">
      {/* Perde açıldıysa uyarı kartın ÜSTÜNDE kalıyor: kullanıcı "yine de göster"e
          bastığı anı unutabilir, içeriğin durumu unutulmamalı. */}
      {gonderi.incelemede && (
        <div className="flex items-center gap-2 rounded-t-2xl border-b border-amber-200 bg-amber-50 px-4 py-2">
          <UyariIkonu className="h-4 w-4 shrink-0 text-amber-600" />
          <p className="text-xs font-medium text-amber-900">
            İncelemede — {gonderi.sikayetSayisi} şikayet aldı, moderasyon sürüyor.
          </p>
        </div>
      )}

      <div className="flex gap-3 p-4 sm:gap-4 sm:p-5">
        <OyRayi puan={gonderi.arti - gonderi.eksi} oy={oy} onOy={(yon) => onOy(gonderi.id, yon)} />

        <div className="min-w-0 flex-1">
          {/* ÜST SATIR: etiket + yazar + zaman solda, şikayet sağ üstte. */}
          <div className="flex items-start justify-between gap-3">
            {/*
              AYIRAÇ NOKTALARI KENDİ BAŞLARINA BİR ÖĞE DEĞİL, ait oldukları metnin
              başında duruyor. 320px'te bu satır sarıyor ve nokta ayrı bir flex öğesi
              olduğunda satır sonunda tek başına asılı kalıyordu ("Sınav Stresi ·" /
              yeni satır / "Elif A."). Noktayı takip ettiği metne bağlamak, sarmanın
              nereden olursa olsun düzgün görünmesini sağlıyor.
            */}
            <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
              <EtiketPili etiket={gonderi.etiket} />

              {/* Avatar ile ad TEK bir küme: sarma yaptığında yazarın fotoğrafı bir
                  satırda, adı diğerinde kalmasın. */}
              <span className="flex min-w-0 items-center gap-1.5">
                <YerTutucuAvatar ad={gonderi.yazar} renk={gonderi.renk} boyut="xs" />
                <span className="truncate text-xs font-medium text-slate-700">{gonderi.yazar}</span>
              </span>

              <span className="flex items-center gap-1.5 text-xs text-slate-600">
                <span className="text-slate-400" aria-hidden="true">
                  ·
                </span>
                {zamanKisalt(gonderi.dakika)}
              </span>
            </div>

            <SikayetDugmesi
              onClick={() =>
                onSikayet({ tur: 'Gönderi', baslik: gonderi.baslik, yazar: gonderi.yazar })
              }
            />
          </div>

          {/* Başlık gönderinin kendisi: kartın tıklanabilir hissi buradan geliyor.
              Şimdilik ayrı bir gönderi sayfası yok, o yüzden bağlantı değil — var
              olmayan bir yere giden bir link, kırık bir vaat olurdu. */}
          <h3 className="mt-2.5 text-[17px] font-bold leading-snug text-slate-900">
            {gonderi.baslik}
          </h3>

          {/* line-clamp-3: akış TARANABİLİR kalmalı. Tam metin gönderi sayfasında. */}
          <p className="mt-2 line-clamp-3 text-sm leading-relaxed text-slate-600">{gonderi.ozet}</p>

          <div className="mt-3.5 flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={onYorumlar}
              aria-expanded={yorumlarAcik}
              className={`flex min-h-11 items-center gap-2 rounded-lg px-2.5 text-xs font-semibold
                          transition lg:min-h-9 ${
                            yorumlarAcik
                              ? 'bg-brand-50 text-brand-700'
                              : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
                          }`}
            >
              <MesajIkonu className="h-4 w-4" />
              {yorumlar.length > 0 ? `${yorumlar.length} yorum` : 'Yorumlar'}
            </button>
          </div>

          {yorumlarAcik && (
            <YorumListesi yorumlar={yorumlar} onSikayet={onSikayet} onYaz={onYorumYaz} />
          )}
        </div>
      </div>
    </CamKart>
  )
}

/*
  OY RAYI — dikey, kartın solunda.

  Dokunma hedefi lg altında 44px (min-h-11): oy okları bu ekranda birbirine en yakın
  duran iki düğme ve yanlış oku basmak, kullanıcının kendi oyunu ters çevirmesi demek.
  lg üstünde fare hassas olduğu için 36px yetiyor.

  Renk oyun yönünü söylüyor: yukarı marka mavisi (bu ürünün "evet" rengi), aşağı rose.
  Sayı da oyun rengini alıyor — kullanıcı kendi oyunu, okların hangisinin dolu olduğuna
  bakmadan, tek bir sayıya bakarak görebiliyor.
*/
function OyRayi({ puan, oy, onOy }) {
  const ortak =
    'grid h-11 w-11 place-items-center rounded-lg transition lg:h-9 lg:w-9 ' +
    'focus:outline-none focus:ring-2 focus:ring-brand-200'

  return (
    <div className="flex shrink-0 flex-col items-center gap-0.5">
      <button
        type="button"
        aria-label="Yukarı oy ver"
        aria-pressed={oy === 1}
        onClick={() => onOy(1)}
        className={`${ortak} ${
          oy === 1 ? 'bg-brand-50 text-brand-600' : 'text-slate-400 hover:bg-slate-100 hover:text-brand-600'
        }`}
      >
        <OyOkuIkonu className="h-[18px] w-[18px]" strokeWidth={oy === 1 ? 2.6 : 2} />
      </button>

      <span
        className={`text-sm font-bold tabular-nums ${
          oy === 1 ? 'text-brand-700' : oy === -1 ? 'text-rose-700' : 'text-slate-800'
        }`}
      >
        {puan + oy}
      </span>

      <button
        type="button"
        aria-label="Aşağı oy ver"
        aria-pressed={oy === -1}
        onClick={() => onOy(-1)}
        className={`${ortak} ${
          oy === -1 ? 'bg-rose-50 text-rose-600' : 'text-slate-400 hover:bg-slate-100 hover:text-rose-600'
        }`}
      >
        {/* Tek çizim, iki yön: aşağı ok ayrı bir ikon değil, aynı okun 180° dönmüşü. */}
        <OyOkuIkonu className="h-[18px] w-[18px] rotate-180" strokeWidth={oy === -1 ? 2.6 : 2} />
      </button>
    </div>
  )
}

/*
  ŞİKAYET DÜĞMESİ — her gönderide ve her yorumda, aynı çizim, aynı yer mantığı.

  Sessiz duruyor (slate-500, ikon + küçük metin) ama saklı değil. İki uç da yanlış
  olurdu: dikkat çeken bir "Şikayet Et" düğmesi forumu bir ihbar hattı gibi gösterir;
  üç nokta menüsünün içine gömülen bir şikayet ise ihlali gören kullanıcının vazgeçtiği
  bir yol olur. Hover'da rose'a dönüyor — eylemin ağırlığı ancak niyet edildiğinde
  görünüyor.

  Metin lg altında GİZLİ, ikon kalıyor: dar ekranda üst satırda etiket, yazar ve zaman
  zaten yarışıyor. Erişilebilir ad her iki durumda da aria-label'da.
*/
function SikayetDugmesi({ onClick, kucuk = false }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Şikayet et"
      title="Şikayet et"
      className={`flex shrink-0 items-center gap-1.5 rounded-lg text-slate-500 transition
                  hover:bg-rose-50 hover:text-rose-700 ${
                    kucuk ? 'min-h-11 px-2 text-[11px] lg:min-h-8' : 'min-h-11 px-2 text-xs lg:min-h-9'
                  }`}
    >
      <BayrakIkonu className={kucuk ? 'h-3.5 w-3.5' : 'h-4 w-4'} />
      <span className="hidden font-medium sm:inline">Şikayet et</span>
    </button>
  )
}

/* ─── YORUMLAR ─────────────────────────────────────────────────────────────── */

/*
  Yorumlar gönderinin İÇİNDE açılıyor, ayrı bir sayfada değil. Sebep: bu bir iskelet ve
  gönderi sayfası henüz yok; ama karar geçici değil — akranlar arası kısa cevaplar için
  yerinde açılan bir iplik, sayfa değiştirip geri dönmekten daha az iş.

  Sol kenardaki dikey çizgi (border-l) yorumları gönderiye bağlıyor: girinti tek başına
  "bu yorumlar o gönderiye ait" demiyor, çizgi diyor.
*/
function YorumListesi({ yorumlar, onSikayet, onYaz }) {
  const [taslak, setTaslak] = useState('')

  /* Alt sınır 5 karakter: "+1" ya da "aynen" gibi tek kelimelik onaylar bir tartışmayı
     ilerletmiyor ama boş bir yorumu göndermeyi engellemek yeterli — gönderi formundaki
     20 karakterlik eşik burada fazla olurdu, kısa ve isabetli cevaplar meşru. */
  const gonderilebilir = taslak.trim().length >= 5

  const gonder = (e) => {
    e.preventDefault()
    if (!gonderilebilir) return
    onYaz(taslak.trim())
    setTaslak('')
  }

  return (
    <div className="mt-4 border-t border-slate-200/70 pt-4">
      {yorumlar.length === 0 ? (
        <p className="text-sm text-slate-600">Bu gönderide henüz yorum yok.</p>
      ) : (
        <ul className="space-y-4 border-l-2 border-slate-100 pl-3 sm:pl-4">
          {yorumlar.map((yorum) => (
            <li key={yorum.id}>
              <div className="flex items-start gap-2.5">
                <YerTutucuAvatar ad={yorum.yazar} renk={yorum.renk} boyut="sm" />

                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-2">
                    <p className="min-w-0 text-xs">
                      <span className="font-semibold text-slate-800">{yorum.yazar}</span>{' '}
                      <span className="text-slate-400" aria-hidden="true">
                        ·
                      </span>{' '}
                      <span className="text-slate-600">{zamanKisalt(yorum.dakika)}</span>
                    </p>

                    {/* Yorumun şikayet düğmesi de aynı yerde: sağ üst. Gönderiyle
                        aynı konum, aynı ikon — kullanıcı kuralı bir kez öğreniyor. */}
                    <SikayetDugmesi
                      kucuk
                      onClick={() =>
                        onSikayet({ tur: 'Yorum', baslik: yorum.metin, yazar: yorum.yazar })
                      }
                    />
                  </div>

                  <p className="mt-1 text-sm leading-relaxed text-slate-700">{yorum.metin}</p>

                  {/*
                    Yorum oyu OKUNUR, TIKLANMAZ — ve "oy" kelimesi tam da bunun için
                    duruyor. Çıplak bir ok + sayı, gönderideki oy rayına benzediği için
                    tıklanabilir görünüyordu; basıldığında hiçbir şey olmayan bir düğme,
                    hiç olmayan bir düğmeden kötüdür. Yorum oylaması bu sürümün kapsamı
                    dışında (istenen oy düğmeleri gönderiler için); geldiğinde bu satır
                    gönderinin rayıyla aynı bileşene döner.
                  */}
                  <p className="mt-1.5 flex items-center gap-1.5 text-xs font-medium text-slate-600">
                    <OyOkuIkonu className="h-3.5 w-3.5 text-slate-400" />
                    {yorum.oy} oy
                  </p>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      {/*
        Yorum kutusu SATIR İÇİ, modal değil — gönderiden farkı burada: yorumun tek bir
        alanı var ve bağlamı (üstündeki tartışma) ekranda kalmalı. Modal açsaydı,
        cevap yazarken cevapladığın şeyi görmez olurdun.

        Düğme metnin ALTINDA ve alan boşken pasif: hedef 44px, dar ekranda da rahat
        basılıyor. Enter'la göndermek YOK — çok satırlı bir alanda Enter satır başıdır.
      */}
      <form onSubmit={gonder} className="mt-4">
        <textarea
          className="input h-20 resize-none"
          value={taslak}
          onChange={(e) => setTaslak(e.target.value)}
          maxLength={1000}
          placeholder="Yorumunu yaz…"
          aria-label="Yorum yaz"
        />
        <div className="mt-2 flex justify-end">
          <Button type="submit" disabled={!gonderilebilir} className="px-4 py-1.5 text-xs">
            Yorumla
          </Button>
        </div>
      </form>
    </div>
  )
}

/* ─── YAN SÜTUN ────────────────────────────────────────────────────────────── */

function KurallarKarti() {
  return (
    <CamKart className="p-5">
      <div className="flex items-center gap-2.5">
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600 ring-1 ring-inset ring-brand-100">
          <KalkanIkonu className="h-5 w-5" />
        </span>
        <h2 className="text-sm font-bold text-slate-900">Topluluk kuralları</h2>
      </div>

      {/* Numaralı liste: kurallara sonradan atıf yapılabilmeli ("2. kural"). Madde
          işareti bunu yapamaz. */}
      <ol className="mt-4 space-y-3">
        {KURALLAR.map((kural, i) => (
          <li key={kural} className="flex gap-2.5">
            <span className="mt-px grid h-5 w-5 shrink-0 place-items-center rounded-md bg-slate-100 text-[11px] font-bold text-slate-700">
              {i + 1}
            </span>
            <span className="text-xs leading-relaxed text-slate-600">{kural}</span>
          </li>
        ))}
      </ol>

      <p className="mt-4 border-t border-slate-200/70 pt-3 text-xs leading-relaxed text-slate-600">
        Kuralı çiğneyen gönderi kaldırılır. Tekrarlayan hesaplar topluluğa gönderi
        paylaşamaz — dersler ve sohbet bundan etkilenmez.
      </p>
    </CamKart>
  )
}

function OnlemlerKarti() {
  return (
    <CamKart className="p-5">
      <div className="flex items-center gap-2.5">
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600 ring-1 ring-inset ring-brand-100">
          <ToplulukIkonu className="h-5 w-5" />
        </span>
        <h2 className="text-sm font-bold text-slate-900">Burası nasıl korunuyor</h2>
      </div>

      <dl className="mt-4 space-y-3">
        {ONLEMLER.map(({ baslik, metin }) => (
          <div key={baslik}>
            <dt className="text-xs font-semibold text-slate-900">{baslik}</dt>
            <dd className="mt-0.5 text-xs leading-relaxed text-slate-600">{metin}</dd>
          </div>
        ))}
      </dl>
    </CamKart>
  )
}

/* ─── ŞİKAYET MODALI ───────────────────────────────────────────────────────── */

/*
  SEBEP SORAN ŞİKAYET. Tek düğmelik bir şikayet moderatöre "biri bundan hoşlanmadı"dan
  başka bir şey söylemez; sebep, gelen yığını sıraya sokan ve otomatik kuralların
  (ör. telif ihbarlarını öne alma) dayandığı tek veridir.

  Şikayet edilen içerik modalın içinde TEKRAR GÖSTERİLİYOR: kullanıcı listede yanlış
  satırın bayrağına basmış olabilir ve bunu ancak neyi şikayet ettiğini görürse anlar.

  "Anonim" bilgisi yazıyor: şikayet etmenin önündeki en büyük engel, şikayet edilenin
  bunu öğreneceği korkusudur.
*/
function SikayetModali({ hedef, onClose, onGonder }) {
  const [sebep, setSebep] = useState(null)
  const [detay, setDetay] = useState('')

  const kapat = () => {
    setSebep(null)
    setDetay('')
    onClose()
  }

  const gonder = () => {
    setSebep(null)
    setDetay('')
    onGonder()
  }

  // "Diğer" seçildiyse açıklama zorunlu: sebepsiz bir "diğer", moderatör kuyruğunda
  // okunamayan bir satırdır.
  const gonderilebilir = sebep !== null && (sebep !== 'diger' || detay.trim().length >= 10)

  return (
    <Modal
      open={hedef !== null}
      onClose={kapat}
      title="Şikayet et"
      footer={
        <>
          <Button variant="secondary" onClick={kapat}>
            Vazgeç
          </Button>
          <Button variant="danger" onClick={gonder} disabled={!gonderilebilir}>
            Şikayeti gönder
          </Button>
        </>
      }
    >
      {hedef && (
        <div className="space-y-4">
          <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <p className="text-xs font-semibold text-slate-600">
              {hedef.tur} · {hedef.yazar}
            </p>
            <p className="mt-1 line-clamp-2 text-sm text-slate-800">{hedef.baslik}</p>
          </div>

          <fieldset>
            <legend className="text-sm font-medium text-slate-700">Sebep</legend>
            <div className="mt-2 space-y-1.5">
              {SIKAYET_SEBEPLERI.map(({ key, baslik, aciklama }) => (
                <label
                  key={key}
                  className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3 transition ${
                    sebep === key
                      ? 'border-brand-500 bg-brand-50'
                      : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50'
                  }`}
                >
                  <input
                    type="radio"
                    name="sikayet-sebebi"
                    value={key}
                    checked={sebep === key}
                    onChange={() => setSebep(key)}
                    className="mt-0.5 h-4 w-4 shrink-0 accent-brand-600"
                  />
                  <span className="min-w-0">
                    <span className="block text-sm font-semibold text-slate-900">{baslik}</span>
                    <span className="mt-0.5 block text-xs leading-relaxed text-slate-600">
                      {aciklama}
                    </span>
                  </span>
                </label>
              ))}
            </div>
          </fieldset>

          <Field
            label={sebep === 'diger' ? 'Açıklama (zorunlu)' : 'Açıklama (isteğe bağlı)'}
            hint="Moderasyon ekibine ne olduğunu birkaç cümleyle anlat."
          >
            <textarea
              className="input h-24 resize-none"
              value={detay}
              onChange={(e) => setDetay(e.target.value)}
              maxLength={500}
              placeholder="Örn. gönderi izinsiz PDF bağlantısı paylaşıyor."
            />
          </Field>

          <p className="rounded-lg bg-slate-50 p-3 text-xs leading-relaxed text-slate-600">
            Şikayetin <span className="font-semibold">anonimdir</span>; şikayet ettiğin kişiye kim
            olduğun gösterilmez.
          </p>
        </div>
      )}
    </Modal>
  )
}
