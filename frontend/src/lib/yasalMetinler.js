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
 * ⚠️ tools/yasal-surum.ps1 bu dosyada sabitin ATAMASINI düzenli ifadeyle arıyor ve İLK
 * eşleşmeyi alıyor. Bu yorumlara sabitin adını eşittir ve tırnaklı bir değerle yazma:
 * yazılırsa betik yorumdaki değeri okur ve bütün e2e paketleri kayıtta düşer.
 */
export const SOZLESME_SURUMU = '2026-09-25'

/** Kullanıcıya gösterilen biçim. Sürümle aynı günü anlatır. */
export const SOZLESME_TARIHI = '25 Eylül 2026'
