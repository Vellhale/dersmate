using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PeerLearn.Api.Authorization;
using PeerLearn.Api.Hubs;
using PeerLearn.Api.Middleware;
using PeerLearn.Api.Startup;
using PeerLearn.Application;
using PeerLearn.Application.Options;
using PeerLearn.Infrastructure;
using PeerLearn.Infrastructure.Persistence;

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
    .AddControllers()
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

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors();

// Hız sınırı kimlik doğrulamadan ÖNCE: parola denemesi yapan istemci zaten kimliksiz
// geliyor, sınırı doğrulamanın arkasına koymak onu hiç uygulamamak olurdu.
app.UseRateLimiter();

app.UseAuthentication();

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
