using System.Threading.Channels;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// "Kuyrukta iş var" sinyali: tek kapasiteli, dolunca yazımı düşüren kanal. Singleton.
/// </summary>
/// <remarks>
/// ─── NEDEN KAPASİTE 1 VE DropWrite ──────────────────────────────────────────
/// Sinyal bir SAYAÇ değil, bir bayrak: "en az bir yeni satır var, uyan ve tara". Dağıtıcı
/// uyandığında vadesi gelmiş BÜTÜN satırları zaten alıyor. Art arda bin Uyandir tek bir
/// uyanış bırakmalı; sayaç tutsaydık dağıtıcı bin kez boşuna tarardı. DropWrite dolu
/// kanala yazmayı sessizce düşürür — bekletmez, fırlatmaz.
///
/// ─── NEDEN FIRLATMAZ ────────────────────────────────────────────────────────
/// Handler'lar bunu COMMIT'TEN SONRA çağırıyor. Fırlatsaydı commit olmuş bir mesaj
/// istemciye hata olarak döner, istemci yeniden gönderir ve mesaj iki kez yazılırdı.
/// TryWrite zaten fırlatmıyor; yine de sözleşme bir try ile kilitlendi, ileride kanal
/// tamamlanırsa (Complete) ya da uygulama değişirse de hata handler'a sızmasın.
///
/// Süreç içi: iki sunucu kopyası birbirinin sinyalini görmez. Doğruluk buna bağlı değil;
/// her kopyanın dağıtıcısı birkaç saniyede bir zaten tarıyor, sinyal yalnızca gecikmeyi kısaltır.
/// </remarks>
public sealed class BildirimSinyali : IBildirimSinyali
{
    private readonly Channel<bool> _kanal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });

    public void Uyandir()
    {
        try
        {
            _kanal.Writer.TryWrite(true);
        }
        catch
        {
            // Sözleşme: hiçbir koşulda fırlatmaz. Kaybolan bir uyanışı tarama telafi eder.
        }
    }

    public async Task<bool> BekleAsync(TimeSpan enFazla, CancellationToken ct)
    {
        // Bekleme başlamadan gelmiş sinyal: hemen tüket.
        if (_kanal.Reader.TryRead(out _))
        {
            return true;
        }

        using var zaman = CancellationTokenSource.CreateLinkedTokenSource(ct);
        zaman.CancelAfter(enFazla);

        try
        {
            // WaitToReadAsync öğeyi TÜKETMEZ; iptal edilince de kanalda öğe kaybolmaz.
            if (await _kanal.Reader.WaitToReadAsync(zaman.Token))
            {
                return _kanal.Reader.TryRead(out _);
            }

            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Süre doldu: sinyal yok, periyodik tarama zamanı.
            return false;
        }
    }
}
