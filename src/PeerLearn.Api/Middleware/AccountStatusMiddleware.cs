using Microsoft.EntityFrameworkCore;
using PeerLearn.Api.Controllers;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Api.Middleware;

/// <summary>
/// Yaptırımı ANINDA uygular: banlanmış/askıya alınmış kullanıcının elindeki geçerli token
/// işe yaramaz.
///
/// NEDEN GEREKLİ: ban yalnızca User.Status'u değiştiriyordu; JWT ise imzalandığı anki
/// bilgiyi taşır ve ömrü dolana kadar (varsayılan 2 saat) geçerlidir. Yani itiraz sonucu
/// banlanan bir eğitmen iki saat daha ders rezerve edebiliyor, sohbet edebiliyor, kanıt
/// yükleyebiliyordu — yaptırımın engellemeyi amaçladığı zararı sürdürebiliyordu. Çıkış
/// yapma, refresh token ya da iptal listesi de yoktu, yani çalınan bir token'ı geçersiz
/// kılmanın hiçbir yolu bulunmuyordu.
///
/// NEDEN ÖNBELLEKSİZ: her kimlikli istekte birincil anahtardan tek satır okunuyor. Kısa
/// ömürlü bir önbellek maliyeti düşürürdü ama yaptırımın ısırması o TTL kadar gecikirdi;
/// bu üründe ban kararı nadir ve sonuçları ağır olduğu için gecikmesiz doğruluk seçildi.
/// Ölçümde bu sorgu öne çıkarsa sonraki adım kısa TTL'li önbellektir.
/// </summary>
public sealed class AccountStatusMiddleware
{
    private readonly RequestDelegate _next;

    public AccountStatusMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAppDbContext db, IClock clock)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userId = context.User.GetUserIdOrNull();
        if (userId is null)
        {
            await _next(context);
            return;
        }

        var hesap = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => new { u.Status, u.SuspendedUntilUtc })
            .SingleOrDefaultAsync(context.RequestAborted);

        // Kullanıcı silinmişse token'ı geçerli saymayız.
        if (hesap is null)
        {
            await Reddet(context, "USER_NOT_FOUND", "Hesap bulunamadı.", StatusCodes.Status401Unauthorized);
            return;
        }

        if (hesap.Status == UserStatus.Banned)
        {
            await Reddet(context, "USER_BANNED",
                "Hesabınız kalıcı olarak askıya alındı.", StatusCodes.Status403Forbidden);
            return;
        }

        // Süre dolmuşsa yaptırım fiilen bitmiştir: arka plan işinin Status'u düzeltmesini
        // BEKLEMEDEN serbest bırakılır. Aksi halde ceza, işin periyodu kadar uzardı.
        if (hesap.Status == UserStatus.Suspended &&
            hesap.SuspendedUntilUtc is { } bitis && bitis > clock.UtcNow)
        {
            await Reddet(context, "USER_SUSPENDED",
                $"Hesabınız geçici olarak askıya alındı. Bitiş: {bitis:dd.MM.yyyy HH:mm} UTC.",
                StatusCodes.Status403Forbidden);
            return;
        }

        await _next(context);
    }

    private static async Task Reddet(HttpContext context, string kod, string mesaj, int durum)
    {
        // Uygulamanın geri kalanıyla AYNI hata biçimi (bkz. ExceptionHandlingMiddleware):
        // istemci tek bir ayrıştırma yolu kullanabilsin.
        context.Response.StatusCode = durum;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { title = kod, status = durum, detail = mesaj });
    }
}
