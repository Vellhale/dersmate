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

/** Sunucudaki LegalDocuments.CurrentVersion ile BİREBİR aynı olmalı. */
export const SOZLESME_SURUMU = '2026-08-27'

/** Kullanıcıya gösterilen biçim. Sürümle aynı günü anlatır. */
export const SOZLESME_TARIHI = '27 Ağustos 2026'
