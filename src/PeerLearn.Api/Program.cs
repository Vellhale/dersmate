using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PeerLearn.Api.Authorization;
using PeerLearn.Api.Hubs;
using PeerLearn.Api.Middleware;
using PeerLearn.Api.Startup;
using PeerLearn.Application;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Options;
using PeerLearn.Infrastructure;
using PeerLearn.Infrastructure.Persistence;
using PeerLearn.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

/*
  ÜRETİM AYAR KAPISI — her şeyden ÖNCE.
  Geliştirme değerleriyle (paylaşılan JWT anahtarı, DB parolası, doğrulama token'ının
  yanıtta dönmesi, localhost CORS) üretimde açılmaya izin verilmez. Ayrıntılar ve
  ortam değişkeni karşılıkları ProductionGuard'da.
*/
ProductionGuard.EnsureSafeForProduction(builder.Configuration, builder.Environment);

// Katmanlar
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddPeerLearnRateLimiter(builder.Configuration);
builder.Services.AddPeerLearnHealthChecks();

/*
  TERS VEKİL BAŞLIKLARI — 2026-08-27'de eklendi.

  Uygulama TLS'i kendisi sonlandırmıyor; üretimde önünde bir ters vekil (nginx/Caddy/
  bulut yük dengeleyici) olacak. Vekil arkasında iki şey BOZULUR ve ikisi de sessizdir:

    1. Connection.RemoteIpAddress vekilin IP'si olur. IP başına kurulan iki hız sınırı
       (kimlik + genel) o anda tek kovaya döner: tüm sitenin trafiği tek bir IP'den
       geliyormuş gibi sayılır ve GlobalPerMinute=300 "tüm site için 300/dk" anlamına
       gelir. RateLimiting.cs'teki IP bölümlemesi bu satır olmadan etkisizdir.
    2. Request.Scheme "http" kalır. Şemaya bakan her şey (üretilen mutlak adresler,
       çerez Secure bayrağı, HSTS) yanlış tarafa düşer.

  ⚠️ KnownNetworks/KnownProxies BİLEREK TEMİZLENİYOR. Varsayılan olarak yalnızca
  loopback'ten gelen X-Forwarded-* başlıklarına güvenilir; gerçek bir vekil arkasında
  bu, başlıkların SESSİZCE yok sayılması demek (en sık rastlanan tuzak — middleware
  çağrılır, hiçbir şey yapmaz). Temizlemenin bedeli şudur: uygulamaya doğrudan
  ulaşabilen biri X-Forwarded-For uydurup hız sınırını atlayabilir. Bu yüzden ŞART:
  API portu dışarıya AÇIK OLMAMALI, yalnızca vekil erişebilmeli (güvenlik duvarı ya da
  yalnız-loopback bind). Bu, docs/URETIME-CIKIS.md'de de yazılı.
*/
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// JWT auth — SignalR websocket'leri header taşıyamadığı için token'ı query'den de kabul eder.
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt ayarları eksik (appsettings: Jwt).");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options => options.AddPeerLearnPolicies());

// SignalR — Redis yapılandırıldıysa backplane (çoklu instance'ta grup yayınları için).
// AppExceptionHubFilter: iş kuralı hataları istemciye "CODE|mesaj" olarak taşınır.
var signalR = builder.Services.AddSignalR(options => options.AddFilter<AppExceptionHubFilter>());
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(redisConnection);
}

builder.Services
    .AddControllers(options =>
    {
        /*
          Her uç /api/… ile birlikte /api/v1/… altında DA yayımlanıyor. Gerekçesi ve
          neden toplu yol değişimi yerine bu yolun seçildiği: Startup/SurumOnekiKurali.cs.
          Kısaca: mağazaya çıkan mobil sürüm ilk günden /api/v1 çağırsın, ama web ve 454
          test referansı tek seferde taşınmak zorunda kalmasın.
        */
        options.Conventions.Add(new SurumOnekiKurali());
    })
    .AddJsonOptions(options =>
    {
        // Enum'lar YANITLARDA zaten string dönüyor ("Offer", "SessionNotHeld").
        // Bu converter olmadan İSTEKLERDE sayı bekleniyordu; asimetri, arayüzün gönderdiği
        // her enum alanını 400'e düşürüyordu (uçtan uca testte yakalandı).
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// React dev sunucusu için CORS (SignalR credential ister → AllowCredentials + açık origin).
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "PeerLearn API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

/*
  ÜRETİMDE ŞEMA KURULUMU: "dotnet run -- --migrate".

  Süreç migration'ları uygular, katalogları tohumlar ve ÇIKAR — istek karşılamaz. Böylece
  şema değişikliği dağıtımın ayrı ve görünür bir adımı olur; uygulama açılışında sessizce
  migration koşturmak, aynı anda başlayan iki instance'ın aynı şemayı yarıştırmasına ve
  hatalı bir migration'ın fark edilmeden canlıya inmesine yol açardı.
*/
if (args.Contains("--migrate"))
{
    using var migrateScope = app.Services.CreateScope();
    var migrateDb = migrateScope.ServiceProvider.GetRequiredService<PeerLearnDbContext>();
    var migrateLogger = migrateScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    migrateLogger.LogInformation("Migration uygulanıyor…");
    await migrateDb.Database.MigrateAsync();
    await DbSeeder.SeedAsync(migrateDb);
    migrateLogger.LogInformation("Migration ve katalog tohumlama tamamlandı.");
    return;
}

/*
  SMTP SINAMASI: "dotnet run -- --test-email eposta@alan.com"

  ⛔ BU KOMUT OLMADAN YANLIŞ SMTP AYARI SESSİZ KALIYOR.

  ProductionGuard yalnızca ayarların VARLIĞINI kontrol ediyor, çalıştığını değil.
  SmtpEmailSender ise gönderim hatasını bilerek yutuyor (kayıt tamamlanmışken e-posta
  yüzünden isteği düşürmek, kullanıcıyı "hesabın açıldı ama kayıt başarısız" gibi bir
  çelişkide bırakırdı). İkisi birleşince şu tablo çıkıyor:

    yanlış SMTP → kayıt 200 döner, kullanıcı oluşur, e-posta GİTMEZ
                → hata yalnızca sunucu günlüğünde
                → hiç kimse hesabını doğrulayamaz, site "çalışıyor" görünür

  Bu komut o boşluğu kapatıyor: gönderimi YUTMADAN dener ve sonucu ekrana yazar.
  Dağıtımdan sonra, ilk gerçek kullanıcıdan ÖNCE çalıştırılmalı.
*/
var testEmailIndex = Array.IndexOf(args, "--test-email");
if (testEmailIndex >= 0)
{
    var hedef = testEmailIndex + 1 < args.Length ? args[testEmailIndex + 1].Trim() : null;

    using var mailScope = app.Services.CreateScope();
    var mailLogger = mailScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (string.IsNullOrWhiteSpace(hedef))
    {
        mailLogger.LogError("Kullanım: dotnet PeerLearn.Api.dll --test-email eposta@alan.com");
        Environment.ExitCode = 1;
        return;
    }

    var sender = mailScope.ServiceProvider.GetRequiredService<IEmailSender>();

    // Log sağlayıcısı ile "başarılı" demek yanıltıcı olurdu: hiçbir şey gönderilmiyor.
    if (sender is not SmtpEmailSender smtpSender)
    {
        mailLogger.LogError(
            "Email:Provider 'Smtp' DEĞİL ({Tur}) — hiçbir e-posta gönderilmiyor. " +
            "Ortam değişkeni: Email__Provider=Smtp", sender.GetType().Name);
        Environment.ExitCode = 1;
        return;
    }

    try
    {
        await smtpSender.SendOrThrowAsync(
            hedef,
            "dersmate SMTP sınaması",
            "Bu bir sınama e-postasıdır. Bunu okuyabiliyorsanız SMTP ayarları çalışıyor " +
            "ve kullanıcılar doğrulama e-postalarını alabilecek.");

        mailLogger.LogInformation(
            "SMTP ÇALIŞIYOR — sınama e-postası {Hedef} adresine gönderildi. " +
            "Kutuya (ve spam klasörüne) bakıp ULAŞTIĞINI doğrulayın: sunucunun kabul " +
            "etmesi, teslim edildiği anlamına gelmez.", hedef);
    }
    catch (Exception ex)
    {
        mailLogger.LogError(ex,
            "SMTP ÇALIŞMIYOR — sınama e-postası gönderilemedi. Bu ayarla hiç kimse " +
            "hesabını doğrulayamaz. Email__Host / Port / Username / Password / " +
            "FromAddress değerlerini kontrol edin.");
        Environment.ExitCode = 1;
    }

    return;
}

/*
  İLK YÖNETİCİYİ AÇ: "dotnet run -- --promote-admin eposta@alan.com"

  ⛔ BU ADIM OLMADAN TAZE BİR ÜRETİM VERİTABANINDA MODERASYON ULAŞILAMAZ.

  Yumurta-tavuk: rol atama ucu (PUT api/admin/users/{id}/role) Policies.AdminOnly ile
  korunuyor, yani yeni bir yönetici ancak MEVCUT bir yönetici tarafından atanabiliyor.
  DbSeeder yalnızca rozet ve ders kataloğunu tohumluyor, kullanıcı açmıyor. Sonuç: yeni
  kurulumda hiç kimse Admin değil → /admin paneline kimse giremiyor → şikayet kuyruğu
  okunamıyor ve yaptırım (uyarı/askı/ban) uygulanamıyor. Yaptırım zinciri kodda tam
  ama pratikte erişilemez kalıyordu.

  NEDEN HTTP UCU DEĞİL, KOMUT SATIRI: "ilk yöneticiyi aç" ucu, ne kadar korunursa
  korunsun kalıcı bir yetki yükseltme yüzeyi bırakır (kurulum bayrağı unutulur, koşul
  bir gün yanlış değerlendirilir). Komut satırı bu yüzeyi hiç açmıyor: çalıştırabilmek
  için zaten sunucuya erişimin olması gerekiyor ve o erişimin varsa veritabanına da
  doğrudan erişimin var demektir — yani yeni bir ayrıcalık vermiyor.

  --migrate gibi ÇALIŞIP ÇIKAR, istek karşılamaz. Karar denetim izine yazılıyor;
  aktör olarak yükseltilen kişinin kendisi kaydediliyor (başka bir aktör yok) ve
  özet bunun bir kurulum adımı olduğunu açıkça söylüyor.
*/
var promoteIndex = Array.IndexOf(args, "--promote-admin");
if (promoteIndex >= 0)
{
    var hedefEposta = promoteIndex + 1 < args.Length ? args[promoteIndex + 1].Trim() : null;

    using var promoteScope = app.Services.CreateScope();
    var promoteDb = promoteScope.ServiceProvider.GetRequiredService<PeerLearnDbContext>();
    var promoteLogger = promoteScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (string.IsNullOrWhiteSpace(hedefEposta))
    {
        promoteLogger.LogError(
            "Kullanım: dotnet run --project src/PeerLearn.Api -- --promote-admin eposta@alan.com");
        Environment.ExitCode = 1;
        return;
    }

    var hedef = await promoteDb.Users
        .SingleOrDefaultAsync(u => u.Email.ToLower() == hedefEposta.ToLower());

    if (hedef is null)
    {
        // Kullanıcı ÖNCE kayıt olmalı: burada hesap açmıyoruz. Parola oluşturmak,
        // doğrulama akışını atlamak ve e-posta sahipliğini kanıtsız kabul etmek demek.
        promoteLogger.LogError(
            "Kullanıcı bulunamadı: {Eposta}. Önce arayüzden kayıt olup e-postasını " +
            "doğrulasın, sonra bu komutu çalıştırın.", hedefEposta);
        Environment.ExitCode = 1;
        return;
    }

    if (hedef.Role == PeerLearn.Domain.Identity.UserRole.Admin)
    {
        promoteLogger.LogInformation("{Eposta} zaten Admin. Değişiklik yapılmadı.", hedefEposta);
        return;
    }

    var oncekiRol = hedef.Role;
    hedef.Role = PeerLearn.Domain.Identity.UserRole.Admin;

    promoteDb.AdminActionLogs.Add(new PeerLearn.Domain.Moderation.AdminActionLog
    {
        ActorUserId = hedef.Id,
        ActorRole = PeerLearn.Domain.Identity.UserRole.Admin,
        Action = PeerLearn.Domain.Moderation.AdminActionType.RoleChanged,
        TargetType = "User",
        TargetId = hedef.Id,
        Summary = $"Kurulum komutuyla Admin yapıldı ({oncekiRol} → Admin). " +
                  "Sunucuya erişimi olan biri tarafından --promote-admin ile çalıştırıldı.",
        CreatedAtUtc = DateTime.UtcNow
    });

    await promoteDb.SaveChangesAsync();
    promoteLogger.LogWarning(
        "{Eposta} artık Admin ({OncekiRol} idi). Denetim izine yazıldı.", hedefEposta, oncekiRol);
    return;
}

// Geliştirmede şemayı otomatik kur + katalog seed'i (yukarıdaki adımı elle koşmaya gerek kalmasın).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PeerLearnDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        await DbSeeder.SeedAsync(db);
        logger.LogInformation("Veritabanı hazır (migration + katalog seed tamamlandı).");
    }
    catch (Exception ex)
    {
        // DB yoksa uygulamayı ÇÖKERTME: frontend geliştiricisi API'yi ayağa kaldırabilsin,
        // sorunu net bir mesajla görsün. DB gerektiren tüm istekler yine de 500 dönecektir.
        logger.LogError(ex,
            "PostgreSQL'e BAĞLANILAMADI. API çalışıyor ama veri gerektiren tüm istekler " +
            "başarısız olacak. PostgreSQL'i başlatın ve ConnectionStrings:Postgres ayarını " +
            "kontrol edin (bkz. docs/ASAMA-3-FRONTEND.md § Kurulum).");
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}

/*
  EN BAŞTA: vekil başlıkları. Kendisinden SONRAKİ her middleware gerçek istemci IP'sini
  ve şemasını görsün diye burada — özellikle UseRateLimiter (aşağıda) doğru IP'yi
  okuyabilmeli. Yapılandırması ve "neden KnownProxies temizlendi" gerekçesi yukarıda.
*/
app.UseForwardedHeaders();

/*
  HSTS yalnızca üretimde: geliştirmede localhost'a HTTPS zorlaması, tarayıcıda kalıcı
  bir kayıt bırakıp diğer localhost projelerini de kırıyor.

  UseHttpsRedirection BİLEREK YOK: TLS vekilde sonlanıyor, uygulamaya gelen istek zaten
  düz HTTP. Yönlendirmeyi de vekil yapmalı — burada yapılsaydı, vekil başlıkları bir gün
  yanlış yapılandırıldığında sonsuz yönlendirme döngüsü oluşurdu. HSTS ise tarayıcıya
  "bu alan adına bir daha HTTP ile gelme" der; yönlendirmeden farklı ve tamamlayıcı.

  ⚠️ HTTP'de yayına çıkmanın bu projeye özel bir bedeli var: hwid.js'teki sha256Hex,
  güvenli bağlam dışında crypto.subtle bulamayıp TAMAMEN FARKLI bir hash üretiyor ve
  onu localStorage'a kalıcı yazıyor. Yani düz HTTP, canvasSignal()'a hiç dokunmadan
  tüm HWID banlarını geçersizleştirir (CLAUDE.md'nin dokunulmaz bölümü).
*/
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors();

/*
  ⛔ SIRA DEĞİŞTİ: KİMLİK DOĞRULAMA ARTIK HIZ SINIRINDAN ÖNCE (2026-09-05).

  Eskiden ters sıradaydı ve buradaki not "parola denemesi yapan istemci zaten kimliksiz
  geliyor" diyordu. O gerekçe hâlâ doğru ama SONUCU yanlıştı: sıra ters olduğu için
  RateLimiting.cs'teki genel sınır HttpContext.User'ı BOŞ görüyor ve her isteği IP
  kovasına koyuyordu. CGNAT'ta bu, tek operatör IP'sinin arkasındaki ~15-30 eşzamanlı
  kullanıcının 300/dk tavanını doldurması demekti.

  Kimlik doğrulama middleware'i hiçbir isteği REDDETMİYOR — yalnızca token'ı okuyup
  HttpContext.User'ı dolduruyor; reddetme UseAuthorization'ın işi. Dolayısıyla sınırın
  arkasına geçmiş olmuyor, sınırın DAHA İYİ bölümlenmesini sağlıyor.

  Kimlik uçlarındaki sıkı politika (RateLimiting.AuthPolicy) IP'de KALIYOR ve bu sıradan
  etkilenmiyor: oraya gelen istemcinin zaten token'ı yok, User boş kalıyor, IP anahtarı
  devreye giriyor. Yani parola denemesi hâlâ IP başına 10/dk.

  Bedeli: geçersiz token taşıyan bir sel, 429 yemeden önce JWT imza doğrulamasından
  geçiyor. Ucuz bir işlem ve o istekler zaten kimliksiz sayılıp IP kovasına düşüyor,
  yani tavan yine koruyor.

  ⚠️ BU SIRAYI GERİ ALMA. Ters çevirirsen hiçbir hata çıkmaz, hiçbir test kırılmaz —
  yalnızca genel sınır sessizce IP başına dönerdi.
*/
app.UseAuthentication();

app.UseRateLimiter();

// Kimlik doğrulandıktan SONRA, yetkilendirmeden ÖNCE: banlanan/askıya alınan kullanıcının
// elindeki geçerli token'ı anında işlevsiz kılar (JWT'nin kendisi 2 saat daha geçerli).
app.UseMiddleware<AccountStatusMiddleware>();

app.UseAuthorization();

app.MapControllers();

// CloseOnAuthenticationExpiration: JWT süresi dolunca (veya kullanıcı banlanıp token
// yenileyemeyince) açık WebSocket bağlantısı da kapanır — aksi halde bağlantı, token
// ömründen bağımsız olarak süresiz yaşardı.
app.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true);

// CANLILIK: bağımlılık yoklamaz. Yük dengeleyici buna bakar — DB kısa süre düştü diye
// sağlıklı instance'ları havuzdan düşürüp kaskad başlatmamalı.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// HAZIRLIK: DB'ye gerçekten bağlanır, Redis yoksa "degraded" der.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Description })
        });
    }
});

app.Run();
