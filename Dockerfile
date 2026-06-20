# syntax=docker/dockerfile:1

# ============================================================
#  TPanel — .NET 10 (Onion) + Vue SPA tek imaj
#  Build aşaması: .NET publish | Runtime: aspnet:10.0 (ICU dahil → tr-TR çalışır)
# ============================================================

# ---- 1) .NET build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce csproj'lar — restore katmanını cache'le
COPY PayDoPay.slnx ./
COPY src/backend/TPanel.Domain/TPanel.Domain.csproj            src/backend/TPanel.Domain/
COPY src/backend/TPanel.Application/TPanel.Application.csproj   src/backend/TPanel.Application/
COPY src/backend/TPanel.Infrastructure/TPanel.Infrastructure.csproj src/backend/TPanel.Infrastructure/
COPY src/backend/TPanel.Api/TPanel.Api.csproj                  src/backend/TPanel.Api/
RUN dotnet restore src/backend/TPanel.Api/TPanel.Api.csproj

# Kaynak + publish
COPY src/backend/ src/backend/
RUN dotnet publish src/backend/TPanel.Api/TPanel.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- 2) Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Yayınlanan .NET çıktısı
COPY --from=build /app/publish ./

# Derlenmiş Vue SPA (Vite çıktısı) — repodaki hazır build
COPY src/frontend/public /app/frontend

# Yüklemeler için yazılabilir dizin (compose'da volume ile kalıcı yapılır)
RUN mkdir -p /app/storage/app/public/receipts

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Frontend__PublicPath=/app/frontend \
    Storage__LocalDiskPath=/app/storage/app \
    Storage__PublicDiskPath=/app/storage/app/public

EXPOSE 8080
ENTRYPOINT ["dotnet", "TPanel.Api.dll"]
