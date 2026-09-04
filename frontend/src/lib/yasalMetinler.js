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

/** Sunucudaki LegalDocuments.CurrentVersion ile BİREBİR aynı olmalı. */
export const SOZLESME_SURUMU = '2026-09-05'

/** Kullanıcıya gösterilen biçim. Sürümle aynı günü anlatır. */
export const SOZLESME_TARIHI = '5 Eylül 2026'
