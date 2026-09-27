using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Features.Identity;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Communication;

/// <summary>
/// Güvenli sohbet mesajı (Modül 2.1/2.2). Kanal bağımsızlığı ilkesi gereği kullanıcılar
/// Zoom/Meet/Discord linklerini bu mesajlarla paylaşır — platform video barındırmaz.
/// Persist burada; canlı yayın SignalR ChatHub'da (Api katmanı) yapılır.
/// </summary>
public sealed record SendMessageCommand(Guid ConversationId, Guid SenderUserId, string Content)
    : IRequest<SendMessageResult>;

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string Content,
    DateTime SentAtUtc);

/// <param name="RecipientUserId">Hub'ın kullanıcı-bazlı bildirim göndereceği karşı taraf.</param>
public sealed record SendMessageResult(MessageDto Message, Guid RecipientUserId);

public sealed class SendMessageHandler : IRequestHandler<SendMessageCommand, SendMessageResult>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IBildirimSinyali _sinyal;
    private readonly TimeSpan _bildirimGecikmesi;

    public SendMessageHandler(IAppDbContext db, IClock clock, IBildirimSinyali sinyal, IOptions<PushOptions> push)
    {
        _db = db;
        _clock = clock;
        _sinyal = sinyal;
        // Eksi değer "geçmişte vadeli" satır üretir ve gecikmenin amacını (web'de okunan
        // mesaja telefonun çalmaması) sessizce kapatırdı; alt sınır 0.
        _bildirimGecikmesi = TimeSpan.FromSeconds(Math.Max(0, push.Value.MesajGecikmeSaniye));
    }

    public async Task<SendMessageResult> Handle(SendMessageCommand request, CancellationToken ct)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length is 0 or > 2000)
        {
            throw new AppException(ErrorCodes.MessageInvalid, "Mesaj 1-2000 karakter olmalı.");
        }

        // YAZMA erişimi: sonlandırılmış eşleşmenin sohbeti okunabilir ama yazılamaz.
        var access = await ConversationAccess.GetForWriteAsync(_db, request.ConversationId, request.SenderUserId, ct);

        /*
          ⛔ ENGEL, AÇIK SOHBETİ DE KESER — engellemenin asıl işi burada.

          BlockUserHandler yalnızca BEKLEYEN istekleri kapatıyor; kabul edilmiş bir
          eşleşmeye dokunmuyor ve dokunmamalı (bkz. aşağıda). Bu kontrol olmasaydı
          engelleme, en çok ihtiyaç duyulduğu durumda çalışmazdı: engellenen kişi
          çoğu zaman zaten konuştuğun kişidir.

          NEDEN EŞLEŞME "Closed" YAPILMIYOR: CloseMatch, sonuçlanmamış dersi (Booked /
          AwaitingApproval / Disputed) olan bir eşleşmeyi kapatmayı REDDEDİYOR — kapanan
          eşleşme, ortada duran bir puan/onay/itiraz işlemini sahipsiz bırakırdı.
          Engelleme o kapıyı zorlasaydı ekonomiye dokunan bir yan etki üretirdi. Bu
          yüzden eşleşme olduğu gibi kalıyor, YALNIZCA yazma kesiliyor: taraflar dersi
          Dersler ekranından iptal edip/itiraz edip bitirebiliyor, ama yazışamıyorlar.

          OKUMA AÇIK KALIYOR (GetForReadAsync'e dokunulmadı) ve bu bilinçli: geçmiş
          mesajlar bir şikâyetin dayanağı. Engellemenin geçmişi silmesi, tacizciye
          "engellet, kanıt uçsun" düğmesi vermek olurdu.

          MESAJ NÖTR ve iki tarafa da AYNI: "seni engelledi" demek engellemeyi
          misillemeye çevirirdi (aynı gerekçe CreateMatchRequestHandler'da).
        */
        if (await EngelSorgusu.VarMiAsync(_db, request.SenderUserId, access.OtherUserId, ct))
        {
            throw new AppException(ErrorCodes.ConversationAccessDenied,
                "Bu sohbete yeni mesaj yazılamıyor.", statusCode: 403);
        }

        var now = _clock.UtcNow;

        var message = new Message
        {
            ConversationId = request.ConversationId,
            SenderUserId = request.SenderUserId,
            Content = content
        };
        _db.Messages.Add(message);

        /*
          PUSH: BİLDİRİM DEFTERİNE SATIR — MESAJLA AYNI SaveChanges'te.

          Hub (ChatHub.SendMessage) ve REST (ConversationsController) ikisi de bu handler'dan
          geçiyor; satırı burada yazmak iki yolu TEK noktada kapsıyor. Çağıranlara konsaydı
          birine eklenip diğerinde unutulması, "web'den yazınca bildirim gelmiyor" gibi
          ancak cihazda görülen bir hata olurdu.

          Satır mesajla atomik: ayrı yazılsaydı mesaj kaydedilip bildirimi kaybolabilir ya
          da hiç var olmayan bir mesaj için bildirim gidebilirdi. İçerik satıra GİRMEZ —
          defterde yalnızca kimlikler var, metin gönderim anında kurulur.

          Vade now + MesajGecikmeSaniye (10 sn): web sohbeti görünürken gelen mesajı hemen
          okundu işaretliyor; bu sürede okunan mesajın bildirimi dağıtıcıda atlanır.
          Sessiz saat UYGULANMAZ (gece yazan arkadaş da cevap bekliyor).
        */
        BildirimKuyrugu.Ekle(_db, BildirimKuyrugu.YeniMesaj(
            message.Id, request.ConversationId, access.OtherUserId, request.SenderUserId, now, _bildirimGecikmesi));

        var conversation = await _db.Conversations.SingleAsync(c => c.Id == request.ConversationId, ct);
        conversation.LastMessageAtUtc = now;

        await _db.SaveChangesAsync(ct);

        // Commit SONRASI ve fırlatmaz: fırlatsaydı kaydedilmiş mesaj istemciye hata olarak
        // döner, istemci yeniden gönderir ve mesaj iki kez yazılırdı. Uyanış kaçsa da satır
        // defterde; dağıtıcı en geç kendi periyodunda bulur.
        _sinyal.Uyandir();

        var dto = new MessageDto(message.Id, message.ConversationId, message.SenderUserId,
            message.Content, message.CreatedAtUtc);

        return new SendMessageResult(dto, access.OtherUserId);
    }
}

/// <summary>
/// Sohbet erişim kuralı (tek yerde): kullanıcı, konuşmanın bağlı olduğu eşleşmenin
/// tarafı olmalı. Hub'daki JoinConversation da bunu kullanır.
/// </summary>
/// <remarks>
/// OKUMA VE YAZMA AYRI, çünkü eşleşme SONLANDIRILDIĞINDA (Closed) ikisi farklı davranır:
///  • Yazma kapanır — sonlandırma bunun için var.
///  • Okuma AÇIK KALIR — geçmiş sohbet kullanıcının kendi kaydı; ders bağlantıları,
///    verilen sözler, hatta bir şikâyete dayanak olacak mesajlar orada. Kapatmayı
///    geçmişi silmeye çevirmek, tacizciye "kapat, kanıt uçsun" düğmesi vermek olurdu.
/// Tek bir GetAsync bırakılsaydı iki davranış kaçınılmaz olarak birbirine karışırdı.
/// </remarks>
public static class ConversationAccess
{
    public readonly record struct AccessInfo(Guid MatchId, Guid OtherUserId, bool IsClosed);

    /// <summary>Mesaj yazma / okundu işaretleme: yalnızca KABUL EDİLMİŞ eşleşme.</summary>
    public static async Task<AccessInfo> GetForWriteAsync(
        IAppDbContext db, Guid conversationId, Guid userId, CancellationToken ct)
    {
        var access = await GetForReadAsync(db, conversationId, userId, ct);

        if (access.IsClosed)
        {
            throw new AppException(ErrorCodes.ConversationAccessDenied,
                "Bu arkadaşlık sonlandırıldı; sohbete yeni mesaj yazılamaz.", statusCode: 403);
        }

        return access;
    }

    /// <summary>Geçmişi görüntüleme: kabul edilmiş VEYA sonlandırılmış eşleşme.</summary>
    public static async Task<AccessInfo> GetForReadAsync(
        IAppDbContext db, Guid conversationId, Guid userId, CancellationToken ct)
    {
        var info = await (
                from c in db.Conversations
                join m in db.Matches on c.MatchId equals m.Id
                where c.Id == conversationId
                select new { m.Id, m.InitiatorUserId, m.ResponderUserId, m.Status })
            .SingleOrDefaultAsync(ct);

        if (info is null ||
            (info.InitiatorUserId != userId && info.ResponderUserId != userId) ||
            (info.Status != MatchStatus.Accepted && info.Status != MatchStatus.Closed))
        {
            throw new AppException(ErrorCodes.ConversationAccessDenied,
                "Bu sohbete erişiminiz yok.", statusCode: 403);
        }

        var other = info.InitiatorUserId == userId ? info.ResponderUserId : info.InitiatorUserId;
        return new AccessInfo(info.Id, other, info.Status == MatchStatus.Closed);
    }
}
