using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Günlüğe yazılan her satırı toplar: "tam token günlüğe düşmedi" iddiası ancak yazılanın
/// tamamı okunarak kanıtlanabilir.
/// </summary>
internal sealed class ToplayanGunluk
{
    public ConcurrentQueue<(LogLevel Seviye, string Metin)> Satirlar { get; } = new();

    public ILogger<T> Al<T>() => new Gunlukcu<T>(this);

    public int Say(LogLevel seviye) => Satirlar.Count(s => s.Seviye == seviye);

    private sealed class Gunlukcu<T>(ToplayanGunluk hedef) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => hedef.Satirlar.Enqueue((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " | " + exception.Message)));
    }
}

/// <summary>
/// Gerçek ağa çıkmayan HTTP ucu: gelen isteği kaydeder, yanıtı test verir.
/// </summary>
internal sealed class SahteHttpUcu : HttpMessageHandler
{
    public List<(string Yol, string? Yetki, string Govde)> Istekler { get; } = new();

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Yanit { get; set; }
        = (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var govde = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Istekler)
        {
            Istekler.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), govde));
        }

        return await Yanit(request, cancellationToken);
    }
}
