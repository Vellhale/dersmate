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
 * ⚠️ MOBİL DEPO HENÜZ HİZALANMADI. Bu değer web + sunucuda artırıldı; mobil depodaki
 * kopya aynı değere çekilmeden yeni APK yayınlanırsa o kullanıcılar KAYIT OLAMAZ.
 * Sıra aşağıdaki notta. Bugün bunun canlı karşılığı yok çünkü mobil uygulama henüz
 * mağazada değil — kapı, ilk yayından ÖNCE kapatılmış oluyor.
 */
export const SOZLESME_SURUMU = '2026-09-19'

/** Kullanıcıya gösterilen biçim. Sürümle aynı günü anlatır. */
export const SOZLESME_TARIHI = '19 Eylül 2026'
