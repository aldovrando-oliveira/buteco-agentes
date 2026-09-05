# syntax=docker/dockerfile:1
#
# Build context: raiz do monorepo — necessário por Directory.Build.props,
# Directory.Packages.props, global.json, e pelo ProjectReference para
# libs/ProviderCatalog. Buildar com:
#   docker build -f apps/api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia só os arquivos de projeto primeiro, pra cachear o restore
# enquanto só o código-fonte muda.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY libs/ProviderCatalog/Buteco.ProviderCatalog.csproj libs/ProviderCatalog/
COPY apps/api/src/Buteco.Api/Buteco.Api.csproj apps/api/src/Buteco.Api/
RUN dotnet restore apps/api/src/Buteco.Api/Buteco.Api.csproj

COPY libs/ProviderCatalog/ libs/ProviderCatalog/
COPY apps/api/src/Buteco.Api/ apps/api/src/Buteco.Api/
RUN dotnet publish apps/api/src/Buteco.Api/Buteco.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Imagem final — variante default (Ubuntu 24.04-based), NUNCA -alpine:
# tzdata e ICU já presentes por padrão, sem
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT — testado empiricamente
# (openspec/changes/containerizacao-stack-servidor/design.md, D4). A
# variante -alpine embute DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true e não
# traz tzdata, quebrando a checagem de fuso horário de apps/workers e a
# formatação de data em pt-BR.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl pro HEALTHCHECK abaixo — a imagem base não traz curl nem wget
# (confirmado empiricamente). Instalado antes de trocar pro usuário
# non-root (apt-get precisa de root).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Usuário non-root já embutido na imagem oficial (confirmado
# empiricamente: uid 1654, /etc/passwd).
USER app

COPY --from=build /app/publish .

EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Buteco.Api.dll"]
