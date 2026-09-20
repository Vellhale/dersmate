import { matchPath } from 'react-router-dom'

/*
  ANALİTİĞE GİDEN YOLUN KİMLİKSİZLEŞTİRİLMESİ (KVKK).

  /profil/:userId ve /sohbet/:conversationId gerçek GUID taşır (Profile.jsx, Chat.jsx →
  useParams; backend tarafında bunlar Guid). Ham yol GA'ya page_path olarak giderse kimlik
  ÜÇÜNCÜ TARAFA aktarılır — analitik rızası "kim neye baktı"yı Google'a verme izni değil.
  Mobil port aynı kuralı trackScreenView ile uyguluyor: rota parametresi hiç geçirilmiyor.

  Elle regex YOK: eşleşme react-router'ın matchPath'iyle, ROTA TANIMLARINDAN yapılır.
  Aşağıdaki liste App.jsx'teki parametreli rotaların TEK kaynağıdır; yeni bir :param rotası
  eklenip buraya eklenmezse analitik-yolu drift kilidi (analitik-yolu.spec.js) kırılır.

  DİKKAT — yalnızca location.pathname normalize edilir; sorgu dizesi ve hash İÇERİLMEZ,
  çünkü pathname bunları taşımaz. İleride biri location.search'i de izlemeye eklerse
  query'deki kimlik yeniden sızabilir; o yol da buradan geçirilmeli.
*/
export const SABLONLU_ROTALAR = ['/sohbet/:conversationId', '/profil/:userId']

/**
 * Ham bir URL yolunu, GUID taşıyan parametreli rotalarda rota şablonuna indirger
 * (/profil/<guid> → /profil/:userId). Parametresiz rotalar kimlik taşımadığı için
 * olduğu gibi döner.
 */
export function normalizeYol(pathname) {
  for (const sablon of SABLONLU_ROTALAR) {
    if (matchPath({ path: sablon, end: true }, pathname)) return sablon
  }
  return pathname
}
