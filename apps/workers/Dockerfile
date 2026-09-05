# syntax=docker/dockerfile:1
#
# Build context: raiz do monorepo — necessário por Directory.Build.props,
# Directory.Packages.props, global.json e pelo ProjectReference para
# libs/ProviderCatalog. Buildar com:
#   docker build -f apps/workers/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY libs/ProviderCatalog/Buteco.ProviderCatalog.csproj libs/ProviderCatalog/
COPY apps/workers/src/Buteco.Workers/Buteco.Workers.csproj apps/workers/src/Buteco.Workers/
RUN dotnet restore apps/workers/src/Buteco.Workers/Buteco.Workers.csproj

COPY libs/ProviderCatalog/ libs/ProviderCatalog/
COPY apps/workers/src/Buteco.Workers/ apps/workers/src/Buteco.Workers/
RUN dotnet publish apps/workers/src/Buteco.Workers/Buteco.Workers.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Imagem final — Worker Service, sem ASP.NET Core, sem porta exposta
# (apps/workers não serve HTTP). Variante default (Ubuntu 24.04-based),
# NUNCA -alpine: é o app com a checagem de integridade de TZ no boot
# (ValidateTimeZoneConfiguration) — ver
# openspec/changes/containerizacao-stack-servidor/design.md, D4.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Buteco.Workers.dll"]
