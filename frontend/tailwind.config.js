/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,jsx}'],
  theme: {
    extend: {
      /*
        MARKA SKALASI — Sky Blue, #0088CC çevresinde (F1b).

        ÖNCEKİ DURUM ÜÇ AYRI MAVİYDİ: arayüz indigo (#4f46e5), logo vurgusu açık mavi
        (#38BDF8), istekte "korunsun" denen #0088CC ise kod tabanında hiç geçmiyordu.
        Logonun zemin rengi (#EEF2FF) indigo-50'ydi — yani logo, izlediğini sandığı
        skalayla bile aynı ailede değildi.

        #0088CC NEDEN 500'DE, 600'DE DEĞİL:
        Ölçüldü — #0088CC üzerine beyaz metin 3.89:1 veriyor ve WCAG AA'nın normal metin
        eşiği 4.5:1. Birincil buton zemini olsaydı her buton etiketi erişilebilirlik
        sınırının altında kalırdı. Bu yüzden marka kimliği rengi 500 basamağında duruyor
        (odak kenarlığı, büyük işaretler, logo) ve gövde/buton renkleri ondan
        KOYULAŞTIRILARAK türetiliyor:
          600 #0077B3 → beyaz metinle 4.90:1  (birincil buton, bağlantılar)
          700 #006699 → beyaz metinle 6.25:1  (hover)
        Yani "istenen rengi koru" ile "okunabilir kal" çatışmadı; renk kimlikte kaldı,
        yalnızca zemin görevi bir basamak aşağı taşındı.

        ⚠️ frontend/src/lib/hwid.js İÇİNDEKİ #4f46e5 BU SKALADAN BAĞIMSIZDIR ve
        DEĞİŞTİRİLMEMİŞTİR. O değer canvas parmak izinin sabiti; paletle aynı olması
        tarihsel bir tesadüftü. Değişirse tüm HWID banları geçersiz olur.
      */
      colors: {
        brand: {
          50: '#E6F4FB',
          100: '#CCE9F7',
          200: '#99D3EF',
          300: '#66BDE7',
          400: '#33A7DF',
          500: '#0088CC',
          600: '#0077B3',
          700: '#006699',
          800: '#005580',
          900: '#004466',
        },
      },
    },
  },
  plugins: [],
}
