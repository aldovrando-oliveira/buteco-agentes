# syntax=docker/dockerfile:1
#
# Build context: raiz do monorepo — necessário por Directory.Build.props,
# Directory.Packages.props e global.json. Buildar com:
#   docker build -f apps/inbox/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY apps/inbox/src/Buteco.Inbox/Buteco.Inbox.csproj apps/inbox/src/Buteco.Inbox/
RUN dotnet restore apps/inbox/src/Buteco.Inbox/Buteco.Inbox.csproj

COPY apps/inbox/src/Buteco.Inbox/ apps/inbox/src/Buteco.Inbox/
RUN dotnet publish apps/inbox/src/Buteco.Inbox/Buteco.Inbox.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Imagem final — variante default (Ubuntu 24.04-based), NUNCA -alpine:
# ver apps/api/Dockerfile e
# openspec/changes/containerizacao-stack-servidor/design.md, D4.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl pro HEALTHCHECK abaixo — ver apps/api/Dockerfile.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

USER app

COPY --from=build /app/publish .

EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Buteco.Inbox.dll"]
