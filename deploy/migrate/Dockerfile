# syntax=docker/dockerfile:1
#
# Migration bundle one-shot — SÓ para apps/api e apps/inbox. apps/workers
# NUNCA gera nem roda bundle: seu AppDbContext existe só para detectar
# divergência de schema e nunca chama MigrateAsync (ver
# openspec/changes/containerizacao-stack-servidor/design.md, D1).
#
# Build context: raiz do monorepo. Buildar com:
#   docker build -f deploy/migrate/Dockerfile .
#
# Achado empírico (verificado, não assumido): o `--connection` do
# `dotnet ef` bundle NÃO funciona neste app — AddInfrastructure lê
# ConnectionStrings:Postgres da configuração e lança exceção antes do
# mecanismo de override do bundle entrar em ação. Cada bundle só aceita a
# connection string via a env var ConnectionStrings__Postgres (mesma
# convenção do app em si) — por isso o entrypoint.sh reatribui essa
# variável entre as duas execuções sequenciais, em vez de usar
# `--connection`.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN dotnet tool install --global dotnet-ef --version 10.0.10
ENV PATH="$PATH:/root/.dotnet/tools"

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY libs/ProviderCatalog/Buteco.ProviderCatalog.csproj libs/ProviderCatalog/
COPY apps/api/src/Buteco.Api/Buteco.Api.csproj apps/api/src/Buteco.Api/
COPY apps/inbox/src/Buteco.Inbox/Buteco.Inbox.csproj apps/inbox/src/Buteco.Inbox/
RUN dotnet restore apps/api/src/Buteco.Api/Buteco.Api.csproj \
    && dotnet restore apps/inbox/src/Buteco.Inbox/Buteco.Inbox.csproj

COPY libs/ProviderCatalog/ libs/ProviderCatalog/
COPY apps/api/src/Buteco.Api/ apps/api/src/Buteco.Api/
COPY apps/inbox/src/Buteco.Inbox/ apps/inbox/src/Buteco.Inbox/

# -r linux-x64 explícito: não depender do RID implícito da máquina que
# builda a imagem (pode ser arm64 num Mac, diferente do alvo típico de VM
# na cloud). Achado empírico (verificado, não assumido): o bundle do
# `dotnet ef` é framework-dependent por padrão, não self-contained —
# `--self-contained` precisaria ser passado explicitamente para embutir o
# runtime. Optou-se por manter framework-dependent e usar
# mcr.microsoft.com/dotnet/runtime:10.0 (com o runtime compartilhado, sem
# SDK) na imagem final, em vez de --self-contained + runtime-deps.
RUN mkdir -p /out \
    && cd apps/api/src/Buteco.Api \
    && dotnet ef migrations bundle --project . --output /out/api-migrate -r linux-x64 --force \
    && cd /src/apps/inbox/src/Buteco.Inbox \
    && dotnet ef migrations bundle --project . --output /out/inbox-migrate -r linux-x64 --force

# Imagem final — variante default (Ubuntu 24.04-based), NUNCA -alpine
# (mesmo motivo de D4). Bundle é framework-dependent (ver acima) e precisa
# especificamente do runtime do ASP.NET Core (não só o runtime base),
# porque apps/api e apps/inbox usam Microsoft.NET.Sdk.Web — confirmado
# empiricamente: com mcr.microsoft.com/dotnet/runtime:10.0 (sem ASP.NET
# Core) o bundle falha com "No frameworks were found" pedindo
# Microsoft.AspNetCore.App. Sem SDK, sem código-fonte — só os dois
# executáveis e o entrypoint.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

USER app

COPY --from=build /out/api-migrate ./api-migrate
COPY --from=build /out/inbox-migrate ./inbox-migrate
COPY deploy/migrate/entrypoint.sh ./entrypoint.sh

ENTRYPOINT ["./entrypoint.sh"]
