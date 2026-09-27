/*
  YASAL METİNLERİN SÜRÜMÜ — tek kaynak.

  Kayıt formu bu sürümü sunucuya bildiriyor; sunucu kendi sabitiyle (Domain/Identity/
  LegalDocuments.cs) KARŞILAŞTIRIP kendi değerini kaydediyor. Yani buradaki değer bir
  veri değil, bir DOĞRULAMA ANAHTARI: kullanıcının tarayıcısındaki arayüzün hangi metni
  gösterdiğini söylüyor.

  ⚠️ İKİ TARAF BİRLİKTE ARTMALI. Ayrışırsa hiç kimse kayıt olamaz — gürültülü bir hata
  ve bilinçli: sessizce yanlış sürümü kaydetmektense kaydı durdurmak yeğdir. Dağıtım
  sırasında kısa bir pencerede (yeni sunucu + eski önbellekli arayüz) bu hata görülebilir;
  kullanıcıya "sayfayı yenile" diyen mesaj tam da bunun için.

  GÖSTERİLEN TARİH DE BURADAN OKUNUYOR (Kosullar.jsx / Gizlilik.jsx). Eskiden her sayfa
  kendi tarihini elle yazıyordu; metin güncellenip tarihlerden biri unutulduğunda
  kullanıcıya gösterilen tarih ile kaydedilen sürüm birbirini tutmazdı — ve o fark
  yalnızca bir denetimde, en kötü anda fark edilirdi.
*/

/*
  ⚠️ MOBİL UYGULAMA DA BU SÜRÜMÜ GÖNDERİYOR ve onunki PAKETE GÖMÜLÜ.

  Web'de sürüm dağıtımla birlikte anında güncellenir (paket yeniden derlenir). Mobilde
  öyle değil: kullanıcının telefonundaki APK eski sabiti taşır ve sunucu eşitlik aradığı
  için o kullanıcı KAYIT OLAMAZ. Yani sürüm artırmak, mobil tarafta bir yayın işidir.

  Sıra: mobil deposundaki src/lib/yasalMetinler.js'i de artır → yeni APK'yı yayınla →
  sonra sunucuyu dağıt. Ters sırada, güncellemeyi almamış her kullanıcı kayıt ekranında
  takılır.
*/

/**
 * Sunucudaki LegalDocuments.CurrentVersion ile BİREBİR aynı olmalı.
 *
 * 2026-09-05 → 2026-09-19 — İKİ DEĞİŞİKLİK TEK ARTIŞTA BİRLEŞTİRİLDİ:
 *
 *   1. VERİ SORUMLUSU KİMLİĞİ. Metinler bugüne kadar "biz" diyordu ama kimin
 *      olduğunu söylemiyordu; Gizlilik §1 ve Koşullar §1 artık dersmate'i
 *      Corventech'in işlettiğini yazıyor, künye de her iki sayfanın altında
 *      (lib/kunye.js, components/Kunye.jsx).
 *   2. ARKADAŞ SAYISI + ORTAK ARKADAŞLAR. Gizlilik §6'ya 2026-09-10'da eklenen ama
 *      sürümü artırılamayan ifşa. Gerekçesi Gizlilik.jsx başındaki nota yazılıydı:
 *      kiminle arkadaş olduğun, §6'nın herkese açık saydığı kümede yoktu.
 *
 * İKİSİNİ AYRI AYRI ARTIRMAK, MOBİL TARAFTA İKİ AYRI MAĞAZA YAYINI demekti. Sürüm
 * artışı mobilde bir yayın kapısı (aşağıdaki nota bak) ve mağaza incelemesi gün alır;
 * aynı anda yürürlüğe giren iki metin değişikliğini tek kapıdan geçirmek, kullanıcıya
 * görünen hiçbir şeyi bozmadan o bedeli yarıya indiriyor.
 *
 * (Mobil depodaki kopya 2026-09-21'de bu değere çekildi; o gün burada duran "mobil depo
 * henüz hizalanmadı" uyarısı o tarihten beri bayattı ve aşağıdaki artışla kaldırıldı.)
 *
 * 2026-09-19 → 2026-09-25 — PUSH BİLDİRİMLERİ. Yeni bir ifşa ve artması ZORUNLUYDU:
 * push üç şeyi birden getiriyor — yeni bir veri türü (telefonun bildirim adresi,
 * bildirim tercihleri, bildirim kayıtları), yeni alıcılar (Expo, Google FCM, Apple APNs)
 * ve onlarla birlikte yeni bir yurt dışı aktarım. Gizlilik §1/§2/§3/§5/§6/§7 AYNI turda
 * yazıldı (pages/Gizlilik.jsx), silinecekler listeleri de (HesapSilme.jsx, Profile.jsx):
 * sayı metinsiz yükselmedi. Push web'de yok ama metin yine de web'de de değişti, çünkü
 * bildirim kayıtları web kullanıcısı için de tutuluyor (§2) ve /gizlilik, mobil
 * kullanıcının da okuduğu tek herkese açık metin.
 *
 * ÜÇ YER AYNI DEĞERE, AYNI DALDA (ozellik/push-bildirimleri) çekildi: bu dosya,
 * LegalDocuments.CurrentVersion ve mobil depodaki src/lib/yasalMetinler.js. Aşağıdaki
 * "önce mobil yayın, sonra sunucu" sırası bu kez GEREKMEDİ: mağazada henüz uygulama yok,
 * yani eski sabiti gönderecek kurulu bir paket de yok. Mağazaya ilk çıkış bu değerle
 * olacak; ondan sonraki her artışta sıra yeniden "önce mobil yayın".
 *
 * Mevcut kullanıcılar YENİDEN ONAYLATILMIYOR — sürüm yalnızca yeni kayıtları kapsıyor
 * (Register.cs). Push'a ilişkin aydınlatma mobil uygulamanın içinde, veri akışından ÖNCE
 * yapılıyor ve sunucu o anın damgası (DisclosureShownAtUtc) olmadan cihaz kaydını kabul
 * etmiyor.
 *
 * 2026-09-25 İÇİNDE, YAYINDAN ÖNCE — KOŞULLAR §3 PUAN DÜZELTMESİ (2026-09-26). §3
 * sunucuyla çelişiyordu: "puan yalnızca ders anlatana yazılır" (Topluluk oyları da puan
 * basıyor) ve "kazanılan puan 30 günde yanar" (ders ve topluluk kazancı vadesiz). Metin
 * gerçeğe çekildi (pages/Kosullar.jsx; mobil app/kosullar.jsx aynı gün, aynı cümleler).
 * SÜRÜM ARTMADI ve bu bir istisna değil, kuralın kendisi: sürüm "kullanıcıya hangi metni
 * gösterdim" beyanı. 2026-09-25 bugün HİÇBİR yerde yayında değil (sunucu, web ve mobil
 * main'e birleşmemiş dallarda); bu sürümü kabul etmiş tek bir kullanıcı yok. Yayından
 * önce aynı sürümün metnini düzeltmek, kimseye gösterilmemiş bir metni değiştirmek.
 * 2026-09-25 yayına çıktıktan SONRA §3'e dokunan her değişiklik sürümü artırır.
 *
 * Aynı dalda (tasarim/profil-dersler-topluluk), yine 2026-09-25'in içinde düzeltilenler:
 *   • Gizlilik §7 ve HesapSilme §1 yol metinleri (ayarlar profilin dişlisine taşındı).
 *   • Koşullar §3'e hoş geldin puanı maddesi (14 günde düşüyor, seviyeye sayılmıyor).
 *   • Gizlilik §2/§6/§7, HesapSilme §3 ve silme penceresine Topluluk içeriği (gönderi,
 *     yorum, oy; herkese açıklığı ve hesap silinince kaldığı).
 *   • Gizlilik §4 çerez yolu ("Çerez ayarları" diye bir öğe yoktu).
 *
 * ⛔ BİRLEŞTİRME SIRASI — bu düzeltmelerin sürüm artırmadan yapılabilmesinin BEDELİ.
 * 2026-09-25 bugün İKİ dalda İKİ ayrı metne karşılık geliyor: ozellik/push-bildirimleri
 * (sunucu + web PR #38, mobil PR #20; ikisi de açık ve main'e hedefli) ESKİ metni taşıyor
 * ("puan yalnızca ders anlatana", "30 günde yanar", "sayfanın en altındaki Hesabımı sil",
 * Topluluk içeriği yok), bu dal yenisini. #38 ya da #20 bu daldan ÖNCE ya da onsuz
 * birleşip dağıtılırsa, o arada kayıt olanlar için Register.cs TermsVersion olarak
 * 2026-09-25 yazar ve bu dal gelince aynı dizge başka bir metni gösterir: kayıt, kullanıcının
 * hangi metni kabul ettiğini artık kanıtlamaz.
 *   KURAL: #38 ve #20 TEK BAŞINA main'e alınmaz, dağıtılmaz. Bu dal push dalının üstünde
 *   (push dalı atası), yani bu dalın PR'ı push işini de taşıyor; iki iş aynı birleştirmede
 *   ve aynı dağıtımda çıkar. Push tek başına çıkacaksa (ör. bu dal gecikir), bu dal
 *   birleşmeden önce sürümü ÜÇ YERDE artırmak zorundadır.
 * Aynı kural LegalDocuments.cs notunda ve CLAUDE.md "Mobil uygulamayla ortak sözleşme"de.
 *
 * ⚠️ tools/yasal-surum.ps1 bu dosyada sabitin ATAMASINI düzenli ifadeyle arıyor ve İLK
 * eşleşmeyi alıyor. Bu yorumlara sabitin adını eşittir ve tırnaklı bir değerle yazma:
 * yazılırsa betik yorumdaki değeri okur ve bütün e2e paketleri kayıtta düşer.
 */
export const SOZLESME_SURUMU = '2026-09-25'

/** Kullanıcıya gösterilen biçim. Sürümle aynı günü anlatır. */
export const SOZLESME_TARIHI = '25 Eylül 2026'
