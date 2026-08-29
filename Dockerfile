# ─────────────────────────────────────────────────────────────────────────────
# dersmate API — üretim imajı.
#
# İKİ AŞAMALI: derleme SDK imajında, çalışma ise yalnızca runtime içeren imajda.
# Tek aşamalı olsaydı sunucuya .NET SDK'sı, NuGet önbelleği ve kaynak kodun tamamı
# giderdi (~800 MB yerine ~220 MB) — hem gereksiz yük hem gereksiz saldırı yüzeyi.
#
# Arayüz BU İMAJDA YOK. Vite paketi statik dosya; nginx doğrudan servis ediyor
# (bkz. docker-compose.prod.yml ve tools/ornek-nginx.conf). API'nin içine koymak,
# arayüzü her değiştirdiğinde API imajını yeniden kurmak demekti.
# ─────────────────────────────────────────────────────────────────────────────

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# ÖNCE yalnızca proje dosyaları kopyalanıyor, sonra restore.
# Sebep katman önbelleği: kaynak kod her commit'te değişir ama bağımlılıklar nadiren.
# Hepsini birlikte kopyalasaydık her küçük değişiklikte tüm paketler yeniden inerdi.
COPY PeerLearn.slnx ./
COPY src/PeerLearn.Domain/PeerLearn.Domain.csproj src/PeerLearn.Domain/
COPY src/PeerLearn.Application/PeerLearn.Application.csproj src/PeerLearn.Application/
COPY src/PeerLearn.Infrastructure/PeerLearn.Infrastructure.csproj src/PeerLearn.Infrastructure/
COPY src/PeerLearn.Api/PeerLearn.Api.csproj src/PeerLearn.Api/
RUN dotnet restore src/PeerLearn.Api/PeerLearn.Api.csproj

COPY src/ src/
RUN dotnet publish src/PeerLearn.Api/PeerLearn.Api.csproj \
    -c Release -o /app/publish --no-restore

# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl SAĞLIK YOKLAMASI İÇİN. Runtime imajı hiçbir HTTP istemcisi taşımıyor (wget de
# yok) — kurulmazsa HEALTHCHECK her seferinde "unhealthy" der ve konteyner sonsuz
# yeniden başlar. Sessiz değil ama teşhisi zor bir arıza; sebebi burada yazılı.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# KÖK KULLANICI DEĞİL. Konteynerden kaçış olasılığını sıfırlamıyor ama bedeli de
# yok: uygulamanın hiçbir yere kök olarak yazması gerekmiyor.
RUN useradd --uid 5000 --create-home --shell /usr/sbin/nologin dersmate

# Kanıt dosyalarının klasörü. Konteyner içinde SABİT bir yol ve dışarıdan bir hacim
# bağlanıyor (compose). Bağlanmazsa dosyalar konteynerle birlikte silinir — kanıtlar
# itiraz hakemliğinin tek dayanağı, o yüzden compose'da hacim ZORUNLU sayılmalı.
RUN mkdir -p /var/lib/dersmate/proof-storage && \
    chown -R dersmate:dersmate /var/lib/dersmate

COPY --from=build --chown=dersmate:dersmate /app/publish .

USER dersmate

# YALNIZCA KONTEYNER İÇİ PORT. Dışarıya açılması compose'un işi ve orada da
# 127.0.0.1'e bağlanıyor: API portu internetten erişilebilir OLMAMALI, çünkü uygulama
# KnownProxies listesini temizliyor ve X-Forwarded-For başlığına koşulsuz güveniyor.
# Doğrudan ulaşabilen biri o başlığı uydurup IP başına hız sınırını atlar.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ProofStorage__RootPath=/var/lib/dersmate/proof-storage

# SAĞLIK YOKLAMASI /health/ready — /health değil.
#
# /health yalnızca sürecin ayakta olduğunu söyler; veritabanı kopukken de 200 döner ve
# nginx istekleri hiçbir şey yapamayan bir uygulamaya yollamaya başlar.
#
# Redis kopukluğu konteyneri ÇÖKERTMEZ ve bu doğru: o durumda uç "Degraded" döndürüyor,
# Degraded'ın HTTP karşılığı 200. Uygulama çalışmaya devam eder (kilit ve önbellek süreç
# içine düşer) — o hâlde yeniden başlatmak sorunu çözmez, yalnızca kesinti ekler.
#
# start-period 40 sn: ilk açılışta göç kontrolü ve bağlantı havuzu ısınması var.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "PeerLearn.Api.dll"]
