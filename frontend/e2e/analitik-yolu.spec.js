import { test, expect } from '@playwright/test'
import { kaynakOku } from './yardimcilar.js'
import { normalizeYol, SABLONLU_ROTALAR } from '../src/lib/analiz-yolu.js'

/*
  ANALİTİK YOL KİMLİKSİZLEŞTİRME — tarayıcı açmayan, kaynağı okuyan test (kaynak-sabitleri
  tarzı). GA'ya giden page_path'in GUID sızdırmadığını ve App.jsx ile normalizerin senkron
  kaldığını kilitler.
*/

const GUID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301'

test.describe('Analitik yol kimliksizleştirme (KVKK)', () => {
  test('GUID taşıyan yollar rota şablonuna indirgeniyor', () => {
    const profil = normalizeYol(`/profil/${GUID}`)
    expect(profil).toBe('/profil/:userId')
    // MUTASYON: normalizer pas geçip ham yolu döndürürse GUID kalır ve bu patlar.
    expect(profil).not.toContain(GUID)

    const sohbet = normalizeYol(`/sohbet/${GUID}`)
    expect(sohbet).toBe('/sohbet/:conversationId')
    expect(sohbet).not.toContain(GUID)
  })

  test('sondaki eğik çizgi de eşleşir', () => {
    expect(normalizeYol(`/profil/${GUID}/`)).toBe('/profil/:userId')
  })

  test('parametresiz yollar aynen kalır', () => {
    expect(normalizeYol('/dersler')).toBe('/dersler')
    expect(normalizeYol('/profil')).toBe('/profil')
    expect(normalizeYol('/sohbet')).toBe('/sohbet')
  })

  /*
    DRİFT KİLİDİ: App.jsx'e :param taşıyan yeni bir rota eklenip normalizer listesine
    yazılmazsa, o rotanın GUID'i sessizce GA'ya sızabilir. Bu test o senkronu zorunlu kılar
    (Logo↔palet senkron testiyle aynı deyim).
  */
  test('App.jsx teki her parametreli rota normalizerde tanımlı', () => {
    const app = kaynakOku('src', 'App.jsx')
    const parametreli = [...app.matchAll(/path="(\/[^"]*:[^"]*)"/g)].map((m) => m[1])

    // Regex gerçekten parametreli rota buluyor mu — yoksa test boş kümeyle sahte yeşile döner.
    expect(parametreli.length, 'App.jsx te parametreli rota bulunamadı — regex mi bozuldu?').toBeGreaterThan(0)

    for (const rota of parametreli) {
      expect(SABLONLU_ROTALAR, `${rota} normalizerde yok — GUID sızabilir`).toContain(rota)
    }
  })
})
