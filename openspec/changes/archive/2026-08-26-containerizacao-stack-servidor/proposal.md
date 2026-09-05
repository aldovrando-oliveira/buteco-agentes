## Why

O monorepo hoje só sobe localmente: `docker-compose.yml` cobre as
dependências de infra de desenvolvimento (Postgres/RabbitMQ/WAHA), mas não
existe nenhum caminho para rodar `apps/api`, `apps/workers`, `apps/inbox` e
`apps/frontend` fora da máquina do desenvolvedor. Não há Dockerfile para
nenhum dos quatro apps, nem um compose que suba o stack completo. Esta
change entrega a containerização dos quatro apps e um compose de servidor
para rodar o stack numa VM na cloud, atrás de um nginx/Cloudflare já
existentes.

## What Changes

- Adiciona um `Dockerfile` multi-stage por app, cada um dentro da pasta do
  próprio app — `apps/api/Dockerfile`, `apps/workers/Dockerfile`,
  `apps/inbox/Dockerfile`, `apps/frontend/Dockerfile` — mantendo o mesmo
  princípio de posse por app já usado no resto do monorepo. Nenhum
  Dockerfile compartilhado nem centralizado numa pasta `docker/`/`deploy/`
  à parte. O build context continua sendo a raiz do monorepo (necessário
  por `Directory.Build.props`/`Directory.Packages.props`/`global.json` e
  pelo `ProjectReference` de `apps/api`/`apps/workers` para
  `libs/ProviderCatalog`) — Docker permite `Dockerfile` num caminho e
  contexto de build em outro (`docker build -f apps/api/Dockerfile .`), o
  que não entra em conflito com essa exigência.
- Adiciona um `.dockerignore` na raiz cobrindo `**/bin/`, `**/obj/`,
  `**/node_modules/`, `apps/frontend/dist/` e `.git/` — hoje ausente, então
  todo `docker build` levaria esses diretórios pro contexto.
- Adiciona um serviço one-shot de migration bundle (`dotnet ef migrations
  bundle`, gerado no estágio de build), executado antes de `apps/api` e
  `apps/inbox` subirem — nunca para `apps/workers`, cujo `AppDbContext` só
  detecta divergência de schema e nunca chama `MigrateAsync`.
- Adiciona `docker-compose.prod.yml` novo, na raiz, subindo os quatro apps
  containerizados, o container de migration bundle, e Postgres/RabbitMQ
  próprios (mesmas imagens do compose de dev, sem porta publicada no
  host, credenciais só via `.env.prod`) — uma VM do zero precisa de
  infra real, não só dos apps. `docker-compose.yml` (infra de dev)
  permanece intocado.
- O nginx do compose (SPA + proxy para `apps/api`/`apps/inbox`) escuta numa
  porta interna do host — não expõe 80/443 diretamente. TLS/domínio público
  são responsabilidade de um nginx já existente fora deste stack (atrás de
  Cloudflare), fora do escopo desta change.
- Remove `app.UseHttpsRedirection()` de `apps/api/src/Buteco.Api/Program.cs`
  — dentro do compose, `apps/api` só recebe tráfego HTTP puro do nginx
  interno (TLS termina fora do stack); sem essa remoção, todo request via
  proxy sofreria redirect 307 permanente. Alinha `apps/api` com a
  assimetria já existente (`apps/inbox` nunca chamou esse middleware).
- Documenta, no compose/README, o mecanismo de segredo único-fonte
  (`.env` do host + interpolação `${VAR}` do compose) para as chaves que
  precisam ser byte-idênticas entre processos (`Auth__TokenSigningKey` entre
  `apps/api`/`apps/inbox`; `Mcp__CredentialEncryptionKey` entre
  `apps/api`/`apps/workers`), e o risco documentado (não mitigado nesta
  change) de subir com o placeholder `changeme` do `.env.example` — nenhuma
  chave hoje rejeita esse valor especificamente, só ausência/vazio.
- Documenta a sequência de deploy (parar `apps/inbox` → rodar o migration
  bundle → subir `apps/inbox` com a imagem nova) como runbook — não é
  imponível só pelo `docker-compose.prod.yml` (`depends_on:
  service_completed_successfully` cobre o primeiro boot, não um redeploy).
- Documenta, como item futuro (não implementado nesta change), que
  `PublicUrl__BaseUrl` de `apps/inbox` tem dois consumidores com requisito
  de alcançabilidade opostos (entrega de push notification interna vs.
  `setWebhook` do Telegram, que precisa de URL pública) e deveria virar duas
  variáveis.

## Capabilities

### New Capabilities
- `server-deployment`: containerização dos quatro apps (Dockerfile por
  app), migration bundle one-shot, `docker-compose.prod.yml`, roteamento
  interno via nginx do stack, e o runbook de deploy/segredos necessário
  para rodar em produção numa VM.

### Modified Capabilities
(nenhuma — `local-dev-environment` permanece como está; as mudanças em
`apps/api/Program.cs` removem um middleware sem alterar nenhum requisito
de spec existente)

## Impact

- **Novos arquivos**: `apps/api/Dockerfile`, `apps/workers/Dockerfile`,
  `apps/inbox/Dockerfile`, `apps/frontend/Dockerfile`,
  `docker-compose.prod.yml`, `.dockerignore`, config de nginx do stack
  (`nginx.conf` ou similar), documentação de runbook de deploy.
- **Código alterado**: `apps/api/src/Buteco.Api/Program.cs` (remoção de
  `UseHttpsRedirection()`).
- **Sem alteração**: `docker-compose.yml` (infra de dev), nenhum teste
  automatizado existente, `apps/workers` (sem migration bundle, sem porta).
- **Fora de escopo (Non-Goals)**: CI/CD, orquestrador além do compose,
  observabilidade/métricas, múltiplas réplicas de `apps/workers`/
  `apps/inbox` (lock distribuído continua `pg_advisory_lock`/`xmin`), TLS
  (resolvido por nginx externo já existente), WAHA no compose (instância
  externa já existente), correção do defeito desprotegido de boot do
  `TaskJobConsumer` (change própria, sequenciada antes desta).
