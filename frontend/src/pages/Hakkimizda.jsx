import { Card, SectionTitle } from '../components/ui'

/*
  Yan menünün alt bağlantısının hedefi. Sayfa bilerek kısa: misyonun kendisi üç
  paragraf — pazarlama metniyle şişirmek, "para transferi yok" gibi tek cümlelik
  güvence mesajlarını kalabalığın içinde kaybederdi.
*/
export default function Hakkimizda() {
  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <SectionTitle>Hakkımızda</SectionTitle>

      <Card className="space-y-4 text-[15px] leading-relaxed text-slate-700">
        <p>
          dersmate, öğrencilerin birbirine ders anlattığı bir akran eğitimi platformudur.
          İyi bildiğin konuyu anlatır, eksik olduğun konuda başka bir öğrenciden ders
          alırsın — sınıfın en doğal hâli, çevrimiçi.
        </p>
        <p>
          Ders almak <strong>tamamen ücretsizdir</strong>. Ders anlatan öğrenci puan
          kazanır; puanlar harcanmaz, birikir ve profilinde unvana dönüşür — anlattıkça
          yükselirsin. dersmate&apos;te para transferi yoktur; kimse kimseye ödeme yapmaz.
        </p>
        <p>
          Platform, ders anlatmanın öğrenmenin en etkili yolu olduğu fikrinden doğdu:
          bir konuyu anlatabiliyorsan, onu gerçekten öğrenmişsindir.
        </p>
      </Card>
    </div>
  )
}
