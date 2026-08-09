## Context

O monorepo já tem três apps consolidados (`apps/api`, `apps/workers`,
`apps/frontend`), todos criados na change arquivada `estrutura-base-monorepo`
e evoluídos desde então. Essa change original define o padrão de scaffold
.NET do projeto: solução própria por app (`src/`/`tests/`), isolamento
estrito via ausência de `ProjectReference` cruzado, Central Package
Management (`Directory.Packages.props`) e `Directory.Build.props`/
`global.json` compartilhados na raiz.

`apps/inbox` (Buteco.Inbox) é o quarto app: o host HTTP que vai receber
webhooks de canais externos (ChatWoot, Waha, e futuros adapters). Por
natureza — precisa de um listener HTTP síncrono para webhooks — é um ASP.NET
Core Web API, não um Worker Service como `apps/workers`. Esta change cria
apenas a fundação de código, replicando o mecanismo exato já usado por
`apps/api`, sem nenhuma lógica de canal, catálogo, orquestração ou
persistência.

Não há nenhum código pré-existente em `apps/inbox`; este é um scaffold
greenfield, assim como a change original.

## Goals / Non-Goals

**Goals:**
- Estabelecer `apps/inbox/Inbox.sln` com `src/`/`tests/`, plugado no CPM e
  no `Directory.Build.props`/`global.json` já existentes na raiz.
- `apps/inbox` deve buildar, rodar e testar de forma independente, sem
  nenhuma referência de projeto para `apps/api`, `apps/workers` ou
  `apps/frontend`.
- `apps/inbox` expõe um health check mínimo (`GET /health`), mesmo mecanismo
  já usado em `apps/api`.
- README atualizado para incluir `apps/inbox` nas seções já existentes
  (Stack, Estrutura, Como subir cada app, Como testar cada app).

**Non-Goals:**
- Nenhuma lógica de negócio: sem lógica de canal (WhatsApp/Telegram/ChatWoot/
  Waha), sem catálogo de canais, sem orquestrador, sem CRM (Contact/Session).
- Nenhuma persistência (Postgres/EF Core). A decisão de schema/banco de
  `apps/inbox` (banco próprio vs. schema separado no Postgres já usado por
  `apps/api`/`apps/workers`) fica para quando o catálogo de canais ou o CRM
  forem desenhados — não é uma decisão deste scaffold.
- Nenhuma referência a `McpServer`, `Agent`, ou qualquer entidade de
  `apps/api`.
- Nenhuma entrada em `docker-compose.yml` para `apps/inbox` (mesmo padrão de
  `apps/api`/`apps/workers`, que também não são containerizados nele — só
  sobem via `dotnet run` local).
- Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/frontend`.

## Decisions

### Árvore de pastas proposta

```
apps/
  inbox/
    Inbox.sln
    src/
      Buteco.Inbox/
        Buteco.Inbox.csproj
        Program.cs
        appsettings.json
        appsettings.Development.json
    tests/
      Buteco.Inbox.Tests/
        Buteco.Inbox.Tests.csproj
        HealthCheckTests.cs
```

### Nomenclatura: `Inbox.sln` / `Buteco.Inbox`
Mesmo padrão de `Api.sln`/`Buteco.Api` e `Workers.sln`/`Buteco.Workers`:
solução na raiz do app, projeto interno prefixado `Buteco.*`. Mantém
consistência de namespace com os apps existentes e evita colisão com nomes
genéricos.

### ASP.NET Core Web API mínimo, não Worker Service
`apps/inbox` recebe webhooks de canais externos — precisa de um listener
HTTP síncrono (requisição/resposta), natureza igual a `apps/api` e diferente
de `apps/workers` (que consome fila em background, sem endpoint HTTP). Por
isso o SDK do projeto é `Microsoft.NET.Sdk.Web`, replicando a estrutura de
`Buteco.Api.csproj`.
**Alternativa considerada**: Worker Service com polling. Rejeitada porque
webhooks são push-based por definição — exigiria reintroduzir um listener
HTTP de qualquer forma, sem ganho nenhum.

### `.NET 10` (`net10.0`), herdado de `Directory.Build.props`
Nenhuma versão nova a decidir: `Directory.Build.props` (raiz) já fixa
`TargetFramework=net10.0` para todos os projetos .NET do monorepo, e
`global.json` (raiz, `rollForward: latestFeature`) já pina o SDK — ambos são
root-scoped e cobrem `apps/inbox` automaticamente, sem exigir arquivo
próprio. `Buteco.Inbox.csproj`/`Buteco.Inbox.Tests.csproj` não redeclaram
`TargetFramework`, `Nullable`, `ImplicitUsings` nem `LangVersion`.

### Health check via `Microsoft.Extensions.Diagnostics.HealthChecks`
Mesmo mecanismo nativo já usado em `apps/api`
(`builder.Services.AddHealthChecks()` / `app.MapHealthChecks("/health")`),
em vez de um endpoint manual — consistência entre os dois hosts HTTP do
monorepo, e é o mecanismo que futura infraestrutura de orquestração (probes
de liveness/readiness) vai consumir.

### Testes: `WebApplicationFactory<Program>` puro, sem fixture customizada
`apps/api` hoje usa uma fixture (`ApiFactoryFixture`) que sobe um Postgres
efêmero via Testcontainers — mas esse padrão só existe porque `apps/api` já
tem EF Core/Postgres (adicionados em changes posteriores ao scaffold
original). `apps/inbox` não tem persistência nesta fatia, então
`Buteco.Inbox.Tests` usa `WebApplicationFactory<Program>` diretamente,
sem nenhuma fixture customizada — fiel ao estado real do que existe hoje
(não precisa de Docker/Testcontainers para rodar).
`Program.cs` termina com `public partial class Program;`, mesmo padrão de
`apps/api`, para permitir a instanciação da factory nos testes.

### Central Package Management: reusar pacotes já pinados
`Directory.Packages.props` (raiz) já tem `ManagePackageVersionsCentrally=true`
e já pina todas as `PackageVersion` necessárias para este scaffold
(`Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.NET.Test.Sdk`, `xunit`,
`xunit.runner.visualstudio`, `coverlet.collector`) — usadas hoje pelos testes
de `apps/api`. `Buteco.Inbox.csproj` e `Buteco.Inbox.Tests.csproj` só
declaram `<PackageReference Include="..." />` sem `Version`, mesmo mecanismo
já em uso. Nenhuma versão nova a pesquisar ou fixar.

### `appsettings.json`/`appsettings.Development.json` mínimos
Só as seções padrão do template Web API (`Logging`, `AllowedHosts`) — sem
`ConnectionStrings`, `Cors`, `PublicUrl` ou qualquer seção de configuração
específica de canal/negócio, que ainda não existem nesta fatia. Mesmo
princípio do scaffold original de `apps/api` (antes de EF Core/Postgres
serem adicionados em changes posteriores).

### Porta de desenvolvimento fixa via `launchSettings.json`
`apps/inbox/src/Buteco.Inbox/Properties/launchSettings.json` fixa
`applicationUrl` em `http://0.0.0.0:5027` (perfil `http`) e
`https://localhost:7172;http://localhost:5027` (perfil `https`) — mesmo
formato de `apps/api/src/Buteco.Api/Properties/launchSettings.json`, que usa
`5017`/`7171`. Sem essa porta fixa, `dotnet run` cai no comportamento padrão
do Kestrel (porta aleatória a cada execução), o que inviabiliza documentar
um comando `curl` real no README.
Valores conferidos antes de escolher: `apps/api` usa `5017`/`7171`;
`apps/frontend` usa `5173` (Vite); `apps/workers` não expõe nenhuma porta
HTTP (`launchSettings.json` sem `applicationUrl` — é um Worker Service puro,
sem Kestrel, então não há risco de conflito ali); o `docker-compose.yml`
ocupa `5432`/`5672`/`15672` (Postgres/RabbitMQ). Não existe uma convenção
numérica formal documentada além do padrão gerado pelo template ASP.NET Core
para `apps/api` — `5027`/`7172` seguem o mesmo estilo (par http/https
próximo de `5017`/`7171`) e não colidem com nenhuma porta já em uso.

### Isolamento de `apps/inbox`
Nenhum `ProjectReference` para `apps/api`, `apps/workers` ou
`apps/frontend`, e nenhuma entrada em `libs/` nesta change — não há nenhum
contrato ou modelo compartilhado a extrair ainda (sem canal, sem catálogo,
sem persistência). Validado manualmente via revisão do `.csproj`, mesmo
processo já usado para `apps/api`/`apps/workers`.

## Risks / Trade-offs

- [Duplicação de configuração entre `apps/inbox` e `apps/api`/`apps/workers`
  (ex.: padrões de logging)] → Aceito por ora, mesmo trade-off já aceito
  para `apps/api`/`apps/workers` desde a change original; isolamento total é
  o requisito mais importante nesta fase.
- [Nome do assembly `Buteco.Inbox` pode não ser o nome final da linha de
  produto de inboxes] → Baixo custo de renomear depois via find-and-replace,
  já que não há nenhuma lógica de negócio construída em cima ainda.
- [Decisão de persistência de `apps/inbox` fica em aberto] → Intencional
  (Non-Goal). Adiada para quando o catálogo de canais ou o CRM (Contact/
  Session) forem desenhados, com a necessidade concreta em mãos — mesmo
  princípio já usado para `libs/ProviderCatalog`, criada só quando houve
  necessidade real de código compartilhado entre `apps/api`/`apps/workers`.

## Migration Plan

Não aplicável — não existe código ou ambiente em produção para migrar em
`apps/inbox`. A "implantação" desta change é a criação dos arquivos e a
verificação local de que `apps/inbox` builda, roda (`dotnet run`) e testa
(`dotnet test`) de forma isolada.

## Open Questions

Nenhuma incerteza real de negócio/produto identificada para este scaffold —
o padrão a seguir já está validado pelo mecanismo existente em
`apps/api`/`apps/workers`, e a decisão de persistência é explicitamente
adiada (ver Non-Goals), não uma pergunta em aberto desta change.
