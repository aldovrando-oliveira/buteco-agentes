## Context

O monorepo (`apps/api`, `apps/workers`, `apps/inbox`, `apps/frontend`, mais
`libs/ProviderCatalog`) só roda hoje na máquina do desenvolvedor;
`docker-compose.yml` cobre só as dependências de infra (Postgres/RabbitMQ/
WAHA). Não existe Dockerfile para nenhum app nem caminho de deploy. O
objetivo é rodar o stack completo numa única VM na cloud, atrás de um
nginx/Cloudflare já existentes e não tocados por esta change.

Fatos de arquitetura já confirmados no repo, empiricamente ou por leitura
de código (não redescobertos aqui, só referenciados pelas decisões
abaixo): build context precisa ser a raiz do monorepo
(`Directory.Build.props`/`Directory.Packages.props`/`global.json` na raiz,
`ProjectReference` de `apps/api`/`apps/workers` para `libs/ProviderCatalog`);
`global.json` pina SDK `10.0.301`; só `apps/api` e `apps/inbox` aplicam
migration (`apps/workers.AppDbContext` nunca chama `MigrateAsync` — gerar
bundle para ele seria erro); `apps/api` chama `UseHttpsRedirection()`,
`apps/inbox` não; `Cors:AllowedOrigins` tem default vazio nos dois; não
existe `.dockerignore`.

## Goals / Non-Goals

**Goals:**
- Um `Dockerfile` por app, com imagem final rodando como usuário non-root,
  sem SDK nem código-fonte.
- `docker-compose.prod.yml` que sobe o stack completo (4 apps + migration
  bundle one-shot) numa única VM, com o nginx do stack escutando numa
  porta interna do host (não expõe 80/443).
- Mecanismo de segredo único-fonte para as chaves que precisam ser
  byte-idênticas entre processos.
- Runbook de deploy documentado (sequência que preserva a garantia do
  índice único de `Session`).

**Non-Goals:**
- CI/CD — build/push de imagem é manual nesta change.
- Orquestrador além do compose (Kubernetes, Swarm, etc.).
- Observabilidade/métricas (Prometheus, dashboards, etc.).
- Múltiplas réplicas de `apps/workers`/`apps/inbox` — o lock hoje é
  `pg_advisory_lock`/`xmin`, não distribuído; o compose não deve sugerir
  `replicas > 1` para esses dois serviços.
- TLS/certificado — resolvido por um nginx já existente fora deste stack
  (atrás de Cloudflare), não tocado nesta change.
- WAHA no compose de servidor — instância externa já existente, tratada
  como dependência, não como serviço do stack.
- Corrigir o setup desprotegido de `TaskJobConsumer` (RabbitMQ) — item em
  aberto já registrado, deve virar change própria sequenciada **antes**
  desta (mesmo padrão de `crossapp-session-codec-encoder`/
  `inbox-session-indice-unico`). Esta change assume que ela já rodou.
- Migrar a variável `PublicUrl__BaseUrl` de `apps/inbox` (hoje com dois
  consumidores de alcançabilidade opostos) para duas variáveis separadas —
  documentado como item futuro, não implementado aqui.
- Validação/rejeição do placeholder `changeme` em segredos — risco aceito
  e documentado, não mitigado em código nesta change.

## Árvore de pastas proposta

```
buteco-agents/
├── docker-compose.yml                       # infra de dev — intocado
├── docker-compose.prod.yml                  # NOVO — 7 serviços (D10: inclui postgres/rabbitmq próprios)
├── .env.prod.example                         # NOVO — template de segredos do host (D7)
├── .dockerignore                             # NOVO
├── .env.example                              # sem mudança nesta change
├── apps/
│   ├── api/
│   │   ├── Dockerfile                        # NOVO — multi-stage, contexto = raiz
│   │   └── src/Buteco.Api/Program.cs         # editado (remove UseHttpsRedirection)
│   ├── workers/
│   │   └── Dockerfile                        # NOVO — sem migration, sem porta exposta
│   ├── inbox/
│   │   └── Dockerfile                        # NOVO
│   └── frontend/
│       ├── Dockerfile                        # NOVO — build Vite + nginx final stage
│       └── deploy/
│           └── nginx.conf                    # NOVO — SPA fallback + proxy /agents,... -> api; /channels,... -> inbox
├── deploy/
│   ├── migrate/
│   │   ├── Dockerfile                        # NOVO — gera os dois migration bundles (api + inbox)
│   │   └── entrypoint.sh                     # NOVO — roda os dois bundles em sequência
│   └── runbook.md                            # NOVO — sequência de deploy e gestão de segredos
└── libs/ProviderCatalog/                     # sem mudança — só referenciado pelo build context
```

Nenhum conteúdo novo em `libs/` — o único código C# alterado é a remoção
de `UseHttpsRedirection()` em `apps/api/Program.cs`; tudo mais é
infraestrutura de build/deploy.

## Decisions

**D0 — Um Dockerfile por app, dentro da pasta do próprio app**. Cada
Dockerfile vive junto do app que builda —
`apps/api/Dockerfile`, `apps/workers/Dockerfile`, `apps/inbox/Dockerfile`,
`apps/frontend/Dockerfile` — em vez de centralizados numa pasta
`docker/`/`deploy/` à parte. Mantém o mesmo princípio de posse por app já
usado no resto do monorepo (cada app é dono do que builda) e deixa óbvio,
a quem abre a pasta de um app, que ele é containerizável e como. Não
conflita com a exigência de build context na raiz: Docker aceita
`Dockerfile` num caminho e contexto de build em outro
(`docker build -f apps/api/Dockerfile .`), então o Dockerfile fica no app
mesmo com o contexto sendo a raiz do monorepo. O único Dockerfile fora de
uma pasta `apps/*` é `deploy/migrate/Dockerfile` (D1) — deliberado, já que
o migration bundle não pertence a um único app (builda contra o
`AppDbContext` de `apps/api` **e** de `apps/inbox`).

**D1 — Migration bundle, one-shot, antes de `apps/api`/`apps/inbox`**
(já trazida como direção, confirmada sem achado que a derrube). O
`deploy/migrate/Dockerfile` builda dois bundles (`dotnet ef migrations
bundle` para o `AppDbContext` de `apps/api` e outro para o de
`apps/inbox` — schemas/bancos diferentes) no estágio de build, e a
imagem final só contém os dois executáveis self-contained + o SDK não
entra na imagem final. `apps/workers` nunca gera nem roda bundle — gerar
um seria erro, não redundância, dado que seu `AppDbContext` só existe
para detectar divergência de schema.

**D2 — Nginx do stack numa porta interna, sem TLS** (ajustada durante a
exploração). O container de nginx do compose serve o build estático de
`apps/frontend` e roteia por prefixo de path para `apps/api`/`apps/inbox`
via nome de serviço na rede do compose:

| Prefixo | Destino |
|---|---|
| `/agents`, `/providers`, `/mcp-servers`, `/auth` | `apps/api` |
| `/channels`, `/contacts`, `/sessions`, `/webhooks` | `apps/inbox` |
| tudo o mais | arquivos estáticos do frontend, com `try_files $uri /index.html` (fallback de SPA para `react-router`) |

`/health` de cada app **não** passa pelo nginx — os healthchecks do
compose chamam `localhost/health` de dentro do próprio container. O
container de nginx publica só uma porta interna do host (ex.
`127.0.0.1:8080:80`), nunca 80/443 — quem recebe tráfego público é um
nginx já existente fora deste stack, atrás de Cloudflare, que faz
`proxy_pass` para essa porta. TLS/certificado ficam inteiramente fora
desta change.

**Achado durante a implementação (via smoke-test real, não previsto na
proposta original)**: `/agents`, `/channels` e `/mcp-servers` são ao
mesmo tempo rota de página do `react-router`
(`apps/frontend/src/app/router.tsx`: `/agents/:id`, `/channels/:id`,
`/mcp-servers/:id`) **e** prefixo de API. O mesmo path serve os dois
propósitos — um refresh/link direto em `/agents/{id}` e um
`fetch('/agents/{id}')` disparado pela própria SPA são indistinguíveis
só pelo path. Testado sem tratamento: o refresh caía sempre no proxy
(502 sem `apps/api` no ar; JSON em vez do SPA com `apps/api` no ar),
nunca no fallback de SPA. Resolvido com o header `Sec-Fetch-Mode`
(enviado automaticamente por browsers modernos desde ~2020, não
controlado pela aplicação): `navigate` só em navegação real de
documento, nunca em `fetch()`/XHR disparado por JS já carregado. As
locations de proxy (`/agents|providers|mcp-servers|auth` e
`/channels|contacts|sessions|webhooks`) fazem
`rewrite ^ /index.html last` quando `$http_sec_fetch_mode = navigate`,
antes do `proxy_pass` — navegação sempre cai no shell do SPA, mesmo em
paths que também são prefixo de API. Limitação aceita: um cliente que
não envia esse header (raro, browsers pré-2020) não teria o fallback de
SPA nesses três prefixos especificamente — degradação, não regressão,
já que este nginx não existia antes desta change.

**D3 — `docker-compose.prod.yml` novo, `docker-compose.yml` intocado**
(já trazida como direção, confirmada). Nenhuma mudança no compose de dev
documentado pelo README.

**D4 — Imagem base .NET: variante default (não-Alpine)**. Testado
empiricamente via `podman` com um probe replicando
`TimeZoneStartupValidation` + `CultureInfo("pt-BR").GetDayName`, publicado
com `mcr.microsoft.com/dotnet/sdk:10.0` e executado nas duas variantes
runtime candidatas:

| | `mcr.microsoft.com/dotnet/{aspnet,runtime}:10.0` | `...-alpine` |
|---|---|---|
| OS base | Ubuntu 24.04 | Alpine Linux |
| `TZ=America/Sao_Paulo` resolve como | `America/Sao_Paulo` ✅ | `UTC` (tzdata ausente) ❌ |
| `ValidateTimeZoneConfiguration` no boot | passa | derruba o processo |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` | não setado | `true`, embutido na imagem |
| `CultureInfo("pt-BR").GetDayName(...)` | `'quarta-feira'` ✅ | `CultureNotFoundException` ❌ |

A variante Alpine exigiria `apk add tzdata icu-libs` **e** desfazer o
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true` que a própria imagem já
embute — e o segundo defeito, se não corrigido, não derrubaria o boot:
estouraria dentro do `catch (Exception)` genérico de
`AgentExecutionService.ExecuteAsync`, marcando a task como `Failed` sem
crash de processo, silenciosamente. A imagem default (Ubuntu-based) não
precisa de nenhum pacote extra nem variável de globalização — usada nas
quatro imagens `mcr.microsoft.com/dotnet/aspnet:10.0` (`apps/api`,
`apps/inbox`) e `mcr.microsoft.com/dotnet/runtime:10.0` (`apps/workers`,
sem ASP.NET Core).

**D5 — Imagens de build auxiliares**: `mcr.microsoft.com/dotnet/sdk:10.0`
para o estágio de build dos quatro apps .NET e do migration bundle
(nunca chega à imagem final); `node:24-alpine` para o estágio de build do
frontend (Node 24 é a LTS ativa corrente — verificado, não assumido de
memória); `nginx:1.30-alpine` para o estágio final do `apps/frontend`
(versão estável corrente do nginx — Alpine aqui não tem o mesmo risco de
D4 porque nginx não depende de tzdata/ICU do jeito que o runtime .NET
depende).

**D6 — Remoção de `UseHttpsRedirection()` em `apps/api`**. Achado durante
a montagem deste design: dentro do compose, `apps/api` só recebe tráfego
HTTP puro do nginx interno (D2) — TLS termina fora do stack, e nenhum
`ForwardedHeaders` está configurado. Sem essa remoção, todo request via
proxy sofreria redirect 307 permanente (`UseHttpsRedirection` redireciona
sempre que `Request.IsHttps` é falso, e sem `ForwardedHeadersMiddleware`
reescrevendo o scheme, o Kestrel dentro do container nunca sabe que a
requisição já chegou como HTTPS na borda). Alinha `apps/api` com a
assimetria que `apps/inbox` já tinha (nunca chamou esse middleware) —
nenhum dos dois precisa dele agora que TLS é responsabilidade
inteiramente externa ao stack.

**D7 — Segredos: `.env` do host + interpolação nativa do compose**.
Descartado `env_file` compartilhado (o próprio `.env.example` já avisa
contra isso, com `PublicUrl__BaseUrl` declarado duas vezes com valores
diferentes) e YAML anchor (exigiria que os blocos `environment:` de
`apps/api`/`apps/inbox`/`apps/workers` tivessem exatamente as mesmas
outras chaves, o que não é o caso). Um `.env.prod` no host (fora do
controle de versão, mesmo princípio do `.env` de dev) declara cada
segredo uma vez; o `docker-compose.prod.yml` referencia
`${AUTH_TOKEN_SIGNING_KEY}`/`${MCP_CREDENTIAL_ENCRYPTION_KEY}` nos blocos
`environment:` dos serviços que precisam do mesmo valor
(`Auth__TokenSigningKey` em `apps/api`+`apps/inbox`;
`Mcp__CredentialEncryptionKey` em `apps/api`+`apps/workers`) — fonte
única, sem copiar valor à mão.

**D8 — `PublicUrl__BaseUrl` de `apps/inbox` aponta pro domínio público**.
Como o nginx externo (fora do escopo) expõe o stack inteiro sob um único
domínio público (ver D2), `PublicUrl__BaseUrl` de `apps/inbox` usa esse
mesmo domínio — satisfaz o requisito mais restritivo (Telegram
`setWebhook`, que exige URL pública HTTPS). Isso significa que
`apps/workers` entrega push notification pra `apps/inbox` saindo pela
internet e voltando pelo mesmo domínio público, não por nome de serviço
interno — aceito como trade-off nesta change (ver Risks); a correção
"ideal" (duas variáveis, uma interna e uma externa) fica documentada como
item futuro, não implementada aqui.

**D9 — Runbook de deploy, não automação de compose**. A sequência que
preserva a garantia do índice único de `Session`
(`AddUniqueOpenSessionIndex`) — parar `apps/inbox` → rodar o migration
bundle → subir `apps/inbox` com a imagem nova — vira `deploy/runbook.md`,
documentada como passo manual/scriptável fora do compose.
`depends_on: service_completed_successfully` no serviço de migration
cobre só o primeiro boot do stack a partir do zero, não um redeploy.

**D10 — `docker-compose.prod.yml` também sobe Postgres e RabbitMQ**
(lacuna encontrada durante a implementação, não prevista na proposta
original — nem o proposal nem os Goals originais deste design mencionavam
infra, só os 4 apps + migration). "Subir o stack completo numa VM na
cloud" exige um banco e um broker reais — nenhum item de Non-Goals exclui
isso (só WAHA foi explicitamente marcado como dependência externa,
Postgres/RabbitMQ nunca foram discutidos como externos durante a
exploração). Mesmas imagens do `docker-compose.yml` de dev
(`postgres:18`, `rabbitmq:4.3-management`), volumes nomeados distintos
(`buteco-postgres-data-prod`/`buteco-rabbitmq-data-prod`, pra nunca
colidir com os volumes de dev caso rodem na mesma máquina), sem porta
publicada no host (só acessível dentro da rede do compose — diferente do
dev, que expõe portas pra acesso de fora do container). Credenciais vêm
só de `.env.prod` (D7), sem default de dev tipo `buteco_dev_password`.

## Risks / Trade-offs

- **[Migration bundle rodar por engano contra `apps/workers`]** → Só
  `apps/api`/`apps/inbox` têm bundle gerado; documentado explicitamente
  no `deploy/migrate/Dockerfile` e no runbook, com o motivo (D1).
- **[Redeploy de `apps/inbox` atropela o índice único de `Session`]** →
  Mitigado só por runbook documentado (D9), não pelo compose — se o
  runbook não for seguido à risca (ex. alguém rodar só `docker compose up
  -d` numa atualização), o `DbUpdateException` não tratado no caminho de
  webhook volta a ser possível.
- **[`TaskJobConsumer` derruba `apps/workers` se RabbitMQ não estiver
  pronto no boot]** → Fora do escopo desta change por decisão explícita
  (deve ser corrigido antes, em change própria); se essa change própria
  não acontecer antes desta na prática, o risco existe sem mitigação
  além de `restart: unless-stopped` absorvendo o crash-loop.
- **[Placeholder `changeme` sobe em produção sem aviso]** → Risco aceito
  e documentado (decisão explícita) — nenhuma chave rejeita esse valor
  especificamente hoje, só ausência/vazio. Mitigação é checklist manual
  de deploy, não código.
- **[Push notification interna de `apps/workers` para `apps/inbox`
  atravessa a internet pública (D8)]** → Funciona, mas depende de
  egress/DNS/Cloudflare estarem saudáveis para uma comunicação que
  poderia ser puramente interna; latência extra e uma dependência a mais
  na cadeia de entrega de resposta ao usuário final. Aceito como
  trade-off; correção fica para change futura.
- **[`.dockerignore` ausente até esta change]** → Sem ele, `bin/`,
  `obj/`, `node_modules/`, `apps/frontend/dist/`, `.git/` entram no
  contexto de build — build lento e risco de artefato de build do host
  (RID errado, ex. `osx-arm64`) vazar pra imagem se algum estágio reusar
  `bin/`/`obj/` locais em vez de restaurar do zero. Mitigado pelo
  `.dockerignore` desta própria change.
- **[Nenhum teste automatizado valida os artefatos Docker]** → Decisão
  explícita: verificação é manual (build + subida do compose + round-trip
  real de webhook), nenhum teste existente é alterado ou estendido para
  rodar contra containers. Trade-off aceito: sem regressão automática
  para mudanças futuras no `Dockerfile`/compose.

## Migration Plan

1. Adicionar `.dockerignore` na raiz.
2. Criar os quatro `Dockerfile`s (`apps/api`, `apps/workers`,
   `apps/inbox`, `apps/frontend`) e `deploy/migrate/Dockerfile`.
3. Remover `UseHttpsRedirection()` de `apps/api/Program.cs` (D6).
4. Criar `apps/frontend/deploy/nginx.conf` com o roteamento da D2.
5. Criar `docker-compose.prod.yml` com os cinco serviços (4 apps +
   migrator), variáveis via `${...}` interpolado de um `.env.prod` (D7).
6. Escrever `deploy/runbook.md` com a sequência de deploy (D9), o
   inventário de variáveis por processo, e os dois trade-offs aceitos
   (D8, placeholder `changeme`).
7. Verificação manual: subir o compose numa VM (ou localmente via
   podman/docker), confirmar boot dos cinco serviços, e um round-trip
   real de webhook → resposta no canal.

Rollback: parar `docker-compose.prod.yml`; nenhuma mudança em
`docker-compose.yml` (dev) ou em specs/requirements existentes — reverter
é remover os artefatos novos e o commit de `UseHttpsRedirection()`.

## Open Questions

- Porta interna exata publicada pelo nginx do stack (ex.
  `127.0.0.1:8080`) precisa ser coordenada com a config do nginx externo
  já existente, para não colidir com outro serviço na mesma VM — decisão
  de ambiente, não bloqueia a implementação desta change.
