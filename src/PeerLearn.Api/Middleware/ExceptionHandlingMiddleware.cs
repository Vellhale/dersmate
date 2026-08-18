using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeerLearn.Application.Common;

namespace PeerLearn.Api.Middleware;

/// <summary>
/// Tüm hataları RFC 7807 ProblemDetails'e çevirir. Eşzamanlılık savunma hatları
/// (xmin çakışması, unique violation) burada kullanıcı dostu 409'a dönüşür —
/// istemci aynı isteği yenileyip güncel durumu görmelidir.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            await WriteProblem(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteProblem(context, StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
                "Kayıt bu sırada başka bir işlem tarafından değiştirildi; lütfen tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            await WriteProblem(context, StatusCodes.Status409Conflict, "DUPLICATE",
                "Bu kayıt zaten mevcut (eşzamanlı istek çakışması).");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" })
        {
            // exclusion_violation: çakışan ders saati. Eşzamanlı iki rezervasyonda uygulama
            // katmanındaki kontrol (ReadCommitted altında commit edilmemiş satırı göremez)
            // yetmez; bu kısıt son savunma hattıdır.
            await WriteProblem(context, StatusCodes.Status409Conflict, ErrorCodes.ScheduleConflict,
                "Seçilen saat aralığı taraflardan birinin başka dersiyle çakışıyor.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "İşlenmemiş hata: {Path}", context.Request.Path);
            await WriteProblem(context, StatusCodes.Status500InternalServerError, "INTERNAL_ERROR",
                "Beklenmeyen bir hata oluştu.");
        }
    }

    private static async Task WriteProblem(HttpContext context, int status, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail
        });
    }
}
