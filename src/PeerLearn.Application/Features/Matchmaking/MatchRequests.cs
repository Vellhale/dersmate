using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Community;
using PeerLearn.Application.Features.Identity;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>
/// Eşleşme isteği: RequestedTopicId = karşı taraftan almak istediğim konu;
/// OfferedTopicId (opsiyonel) = karşılığında anlatmayı önerdiğim konu (çapraz teklif).
///
/// RequestedTopicId NULL ise bu bir ÜNİVERSİTE AĞI isteğidir: ders değil, tanışma.
/// İki türün doğrulama kapıları farklı — aşağıdaki handler'da gerekçesiyle birlikte.
/// </summary>
public sealed record CreateMatchRequestCommand(
    Guid InitiatorUserId,
    Guid ResponderUserId,
    Guid? RequestedTopicId,
    Guid? OfferedTopicId) : IRequest<Guid>;

public sealed class CreateMatchRequestHandler : IRequestHandler<CreateMatchRequestCommand, Guid>
{
    /// <summary>
    /// Bir kullanıcının 24 saatte gönderebileceği en fazla istek sayısı.
    /// </summary>
    /// <remarks>
    /// Üniversite kapısının yerine gelen fren; gerekçesi <c>Handle</c> içinde. 20, normal
    /// kullanımın belirgin üstünde (kimse günde 20 kişiye tanışma isteği atmıyor) ama
    /// toplu mesajı anlamsız kılacak kadar düşük.
    /// </remarks>
    public const int GunlukIstekTavani = 20;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CreateMatchRequestHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateMatchRequestCommand request, CancellationToken ct)
    {
        if (request.InitiatorUserId == request.ResponderUserId)
        {
            throw new AppException(ErrorCodes.SelfMatch, "Kendinizle eşleşemezsiniz.");
        }

        /*
          ⛔ ENGEL KONTROLÜ — TÜR AYRIMINDAN ÖNCE, ÇİFT YÖNLÜ, HER İKİ TÜR İÇİN.

          Aşağıdaki dallardan birine konsaydı diğeri açık kalırdı: engellediğin kişi sana
          DERS isteği göndermeye devam ederdi. Engel, isteğin türüne bakmaz.

          Hata mesajı BİLEREK NÖTR ve engelin varlığını söylemiyor — "seni engelledi"
          demek, engellemeyi misillemeye çevirirdi. Karşı taraf isteğin gitmediğini
          zaten anlıyor, ama nedenini üründen öğrenmiyor.
        */
        if (await EngelSorgusu.VarMiAsync(_db, request.InitiatorUserId, request.ResponderUserId, ct))
        {
            throw new AppException(ErrorCodes.MatchNotFound,
                "Bu kişiye istek gönderilemiyor.", statusCode: 409);
        }

        /*
          İKİ TÜR, İKİ AYRI KAPI.

          DERS isteğinde kapı: karşı taraf o konuyu gerçekten sunuyor mu. Yani istek,
          karşı tarafın kendi ilan ettiği bir şeye dayanıyor.

          ─── ÜNİVERSİTE KAPISI KALDIRILDI (isimle arama ile birlikte) ──────────────
          Eskiden tanışma isteği için karşı tarafın profiline ÜNİVERSİTESİNİ girmiş
          olması gerekiyordu; üniversite yazmak "bu ağda görüneyim" onayı sayılıyordu.
          Buradaki eski yorum kapının gerekçesini şöyle bitiriyordu:

            "Bu kapı olmadan uç, herhangi bir kullanıcıya doğrudan mesaj isteği hâline
             gelirdi — bu üründe ENGELLEME/BLOK MEKANİZMASI OLMADIĞI için bunun bedeli
             yüksek olurdu."

          Kapı, Keşfet'e isimle arama eklendiği için kalktı (ürün sahibi kararı): adını
          bildiğin ama üniversitesini yazmamış bir arkadaşını bulmanın başka yolu yoktu.
          Ve o cümlenin şart koştuğu şey aynı değişiklikle geldi: engelleme artık VAR
          (yukarıdaki kontrol). İkisi ayrı ayrı sevk edilemez — arama engellemesiz
          açılsaydı, kapının kapattığı zarar açıkta kalırdı.

          ⚠️ KAPININ İÇİNDEKİ AKTİFLİK KONTROLÜ AŞAĞIDA KORUNDU. Eski blok üç şeyi
          birden sınıyordu: üniversite dolu mu, kullanıcı var mı, VE Status == Active.
          Blok komple silinseydi üçüncüsü de giderdi ve banlı/askıdaki hesaplara istek
          gitmesi SESSİZCE mümkün olurdu — hiçbir test bunu yakalamazdı.
        */
        if (request.RequestedTopicId is { } topicId)
        {
            var offers = await _db.PortfolioEntries.AnyAsync(p =>
                p.UserId == request.ResponderUserId && p.TopicId == topicId &&
                p.Direction == PortfolioDirection.Offer && p.IsActive, ct);

            if (!offers)
            {
                throw new AppException(ErrorCodes.MatchNotFound,
                    "Karşı taraf bu konuyu portföyünde sunmuyor.", statusCode: 409);
            }
        }
        else
        {
            // Üniversite koşulu kalktı; AKTİFLİK koşulu kaldı (yukarıdaki uyarı).
            var alabilirMi = await _db.Users.AnyAsync(u =>
                u.Id == request.ResponderUserId &&
                u.Status == UserStatus.Active, ct);

            if (!alabilirMi)
            {
                throw new AppException(ErrorCodes.MatchNotFound,
                    "Bu kişiye istek gönderilemiyor.", statusCode: 409);
            }
        }

        /*
          MÜKERRER BEKLEYEN İSTEK.

          Konusuz istekte karşılaştırma `RequestedTopicId == null` ile yapılmalı,
          `== request.RequestedTopicId` ile DEĞİL: SQL'de NULL = NULL sonucu NULL'dır,
          yani hiçbir zaman true olmaz ve kontrol sessizce hiçbir şey bulmazdı. Aynı
          tuzak veritabanı tarafında da var — bu yüzden ikinci bir kısmi tekillik indeksi
          eklendi (bkz. MatchmakingConfigurations).
        */
        var pendingExists = request.RequestedTopicId is { } istenenKonu
            ? await _db.Matches.AnyAsync(m =>
                m.InitiatorUserId == request.InitiatorUserId &&
                m.ResponderUserId == request.ResponderUserId &&
                m.RequestedTopicId == istenenKonu &&
                m.Status == MatchStatus.Pending, ct)
            : await _db.Matches.AnyAsync(m =>
                m.InitiatorUserId == request.InitiatorUserId &&
                m.ResponderUserId == request.ResponderUserId &&
                m.RequestedTopicId == null &&
                m.Status == MatchStatus.Pending, ct);

        if (pendingExists)
        {
            throw new AppException(ErrorCodes.DuplicateMatchRequest,
                request.RequestedTopicId is null
                    ? "Bu kişiye zaten bekleyen bir isteğiniz var."
                    : "Bu kişiye bu konu için zaten bekleyen isteğiniz var.",
                statusCode: 409);
        }

        /*
          ⛔ GÜNLÜK TAVAN — üniversite kapısının yerine gelen fren.

          Kapı kalkmadan önce spam'i üç şey sınırlıyordu: kendine istek yasağı, çift
          başına TEK bekleyen istek, ve dakikalık genel hız sınırı. Bunların hiçbiri
          "kaç FARKLI kişiye istek atabilirsin" sorusunu yanıtlamıyor:

            • Bekleyen istek freni yalnızca Status='Pending' iken çalışıyor. Reddedilen
              ya da süresi dolan bir istekten sonra AYNI kişiye sınırsız yeni istek
              gidebiliyor.
            • Genel hız sınırı bir TRAFİK sınırı; "10 farklı kişiye istek" ile "10 sayfa
              yüklemesi" arasında ayrım yapmıyor ve dakikada 300'de duruyor.

          Üniversite kapısı fiilen bu boşluğu kapatıyordu (hedef havuzu daraltarak).
          Kaldırıldığına göre yerine açık bir tavan konmalı, yoksa "arkadaş ekle" bir
          toplu mesaj aracına döner.

          Tavan GÖNDERİLEN isteğe göre ve 24 saatlik kayan pencerede. Kabul edilmiş
          istekler de sayılıyor: amaç kötüye kullanımı değil, TANIMADIĞIN insanlara
          seri istek atmayı frenlemek — normal bir kullanıcı günde 20 kişiye istek
          göndermiyor.
        */
        var gunlukEsik = _clock.UtcNow.AddDays(-1);
        var bugunGonderilen = await _db.Matches.CountAsync(m =>
            m.InitiatorUserId == request.InitiatorUserId &&
            m.CreatedAtUtc >= gunlukEsik, ct);

        if (bugunGonderilen >= GunlukIstekTavani)
        {
            throw new AppException(ErrorCodes.MintLimitReached,
                $"Günde en fazla {GunlukIstekTavani} istek gönderebilirsin. Yarın tekrar dene.",
                statusCode: 429);
        }

        var match = new Match
        {
            InitiatorUserId = request.InitiatorUserId,
            ResponderUserId = request.ResponderUserId,
            RequestedTopicId = request.RequestedTopicId,
            OfferedTopicId = request.OfferedTopicId
        };

        _db.Matches.Add(match);
        await _db.SaveChangesAsync(ct); // Partial unique index (Pending) eşzamanlı istekte son savunma.

        return match.Id;
    }
}

/// <summary>Kabulde sohbet kanalı otomatik açılır (Modül 2.1: Match &amp; Chat).</summary>
public sealed record RespondMatchCommand(Guid MatchId, Guid ResponderUserId, bool Accept)
    : IRequest<RespondMatchResult>;

public sealed record RespondMatchResult(Guid MatchId, string Status, Guid? ConversationId);

public sealed class RespondMatchHandler : IRequestHandler<RespondMatchCommand, RespondMatchResult>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly BadgeEngine _badges;

    public RespondMatchHandler(IAppDbContext db, IClock clock, BadgeEngine badges)
    {
        _db = db;
        _clock = clock;
        _badges = badges;
    }

    public async Task<RespondMatchResult> Handle(RespondMatchCommand request, CancellationToken ct)
    {
        var match = await _db.Matches.SingleOrDefaultAsync(m => m.Id == request.MatchId, ct)
                    ?? throw new AppException(ErrorCodes.MatchNotFound, "Eşleşme bulunamadı.", statusCode: 404);

        if (match.ResponderUserId != request.ResponderUserId)
        {
            throw new AppException(ErrorCodes.NotMatchParticipant,
                "Bu isteği yalnızca muhatabı yanıtlayabilir.", statusCode: 403);
        }

        if (match.Status != MatchStatus.Pending)
        {
            throw new AppException(ErrorCodes.MatchNotPending,
                $"İstek zaten yanıtlanmış ({match.Status}).", statusCode: 409);
        }

        /*
          ⛔ ENGEL KONTROLÜ BURADA DA GEREKLİ — istek gönderimindekiyle birlikte anlamlı.

          Yalnızca gönderime konsaydı şu boşluk kalırdı: A, B'ye istek gönderir; B (ya da
          A) sonradan diğerini engeller; ama BEKLEYEN istek ortada durmaya devam eder ve
          kabul edilince aşağıda Conversation açılır — yani engel, kurulduğu gün sohbetle
          delinir.

          BlockUser akışı bekleyen istekleri kapatıyor, dolayısıyla buraya normalde
          düşülmemeli. Bu kontrol ikinci savunma hattı: engelin o adımı bir gün
          atlanırsa ya da yarış oluşursa sohbet yine açılmasın.

          Yalnızca KABUL engelleniyor; reddetmek serbest. Engellenmiş bir isteği
          reddedememek, kullanıcıyı kendi gelen kutusunda kilitli bırakırdı.
        */
        if (request.Accept &&
            await EngelSorgusu.VarMiAsync(_db, match.InitiatorUserId, match.ResponderUserId, ct))
        {
            throw new AppException(ErrorCodes.MatchNotFound,
                "Bu istek artık kabul edilemiyor.", statusCode: 409);
        }

        match.Status = request.Accept ? MatchStatus.Accepted : MatchStatus.Declined;
        match.RespondedAtUtc = _clock.UtcNow;

        Conversation? conversation = null;
        if (request.Accept)
        {
            conversation = new Conversation { MatchId = match.Id };
            _db.Conversations.Add(conversation);
        }

        /*
          ROZET DEĞERLENDİRMESİ BURADA ÇAĞRILIR.

          ⚡ Hızlı yanıt rozetinin dayandığı veriyi ÜRETEN akış burasıdır; tetikleyici
          listesinde olmasaydı, yalnızca isteklere hızla dönen (ama ders değerlendirmesi
          almayan, öğretmen adaylığı beyanı vermeyen) bir kullanıcı rozeti HİÇ göremezdi —
          rozet motoru arka planda koşmuyor, yalnızca kullanıcının verisini değiştiren
          akışların sonunda çalışıyor.

          SIRA: yanıt ÖNCE yazılır. Motor veriyi DB'den okuduğu için SaveChanges'ten önce
          çağrılsaydı az önceki yanıtı göremez ve 5. yanıtta açılması gereken eşik 6.'ya
          kayardı (bu projede iki kez düşülen tuzak; bkz. CreateReview notu).
        */
        await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

        await _db.SaveChangesAsync(ct);
        await _badges.EvaluateAsync(request.ResponderUserId, ct);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return new RespondMatchResult(match.Id, match.Status.ToString(), conversation?.Id);
    }
}
