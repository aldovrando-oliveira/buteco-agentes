## 1. Preparação do build context

- [x] 1.1 [raiz] Criar `.dockerignore` cobrindo `**/bin/`, `**/obj/`,
      `**/node_modules/`, `apps/frontend/dist/`, `.git/`
- [x] 1.2 [apps/api] Remover `app.UseHttpsRedirection()` de
      `apps/api/src/Buteco.Api/Program.cs`

## 2. Dockerfile por app (.NET)

- [x] 2.1 [apps/api] Criar `apps/api/Dockerfile` multi-stage: build com
      `mcr.microsoft.com/dotnet/sdk:10.0` (contexto na raiz do monorepo,
      restaurando `Directory.Build.props`/`Directory.Packages.props`/
      `global.json` e o `ProjectReference` para `libs/ProviderCatalog`),
      imagem final `mcr.microsoft.com/dotnet/aspnet:10.0`, usuário
      non-root
- [x] 2.2 [apps/inbox] Criar `apps/inbox/Dockerfile`, mesmo padrão de 2.1
- [x] 2.3 [apps/workers] Criar `apps/workers/Dockerfile`, mesmo padrão de
      2.1 mas com imagem final `mcr.microsoft.com/dotnet/runtime:10.0`
      (sem ASP.NET Core) e sem porta exposta
- [x] 2.4 [apps/api, apps/inbox, apps/workers] Validar manualmente, num
      container isolado, que `TZ=America/Sao_Paulo` resolve corretamente
      e que `CultureInfo("pt-BR")` não lança exceção nas três imagens
      finais

## 3. Migration bundle one-shot

- [x] 3.1 [deploy/migrate] Criar `deploy/migrate/Dockerfile`: estágio de
      build com `mcr.microsoft.com/dotnet/sdk:10.0` gerando dois
      `dotnet ef migrations bundle` (um para o `AppDbContext` de
      `apps/api`, outro para o de `apps/inbox`); imagem final só com os
      dois executáveis, sem SDK (bundle é framework-dependent, não
      self-contained — achado empírico, ver comentário no Dockerfile)
- [x] 3.2 [deploy/migrate] Garantir explicitamente, via comentário no
      Dockerfile, que nenhum bundle é gerado para o `AppDbContext` de
      `apps/workers`

## 4. Frontend e nginx do stack

- [x] 4.1 [apps/frontend] Criar `apps/frontend/Dockerfile` multi-stage:
      build com `node:24-alpine` (`npm ci && npm run build`, recebendo
      `VITE_API_BASE_URL`/`VITE_INBOX_BASE_URL` como `ARG` obrigatórios,
      sem default silencioso), estágio final `nginx:1.30-alpine` servindo
      `dist/`
- [x] 4.2 [apps/frontend] Criar `apps/frontend/deploy/nginx.conf` com
      `try_files $uri /index.html` (fallback de SPA) e roteamento por
      prefixo: `/agents`, `/providers`, `/mcp-servers`, `/auth` →
      `apps/api`; `/channels`, `/contacts`, `/sessions`, `/webhooks` →
      `apps/inbox`. Achado real via smoke-test: `/agents`, `/channels` e
      `/mcp-servers` colidem com rotas de página do react-router — resolvido
      com guard de `Sec-Fetch-Mode: navigate` (rewrite pro SPA antes do
      proxy), validado com os 4 cenários (navegação, fetch/XHR, asset
      estático, prefixo sem colisão)
- [x] 4.3 [apps/frontend] Configurar o container de nginx do stack para
      publicar só uma porta interna do host (ex. `127.0.0.1:8080:80`),
      nunca 80/443

## 5. Compose de servidor

- [x] 5.1 [raiz] Criar `docker-compose.prod.yml` com os sete serviços
      (postgres + rabbitmq + migrator + `apps/api` + `apps/inbox` +
      `apps/workers` + `apps/frontend`/nginx — achado durante a
      implementação: postgres/rabbitmq não estavam na proposta original,
      mas o stack não sobe do zero sem eles, ver design.md D10),
      `depends_on: service_completed_successfully` no migrator para
      `apps/api`/`apps/inbox`/`apps/workers`
- [x] 5.2 [raiz] Configurar variáveis de ambiente por serviço via
      interpolação `${VAR}` de um `.env.prod` do host (não versionado,
      `.env.prod.example` criado como template), com
      `Auth__TokenSigningKey` e `Mcp__CredentialEncryptionKey`
      referenciando a mesma variável de host nos serviços que precisam
      do valor idêntico — validado com `podman-compose config`
- [x] 5.3 [raiz] Confirmar que `docker-compose.yml` (infra de dev) não
      foi alterado (`git diff --stat docker-compose.yml` vazio)

## 6. Runbook e documentação

- [x] 6.1 [raiz] Escrever `deploy/runbook.md` com a sequência de deploy
      que preserva a garantia do índice único de `Session` (parar
      `apps/inbox` → rodar o migration bundle → subir `apps/inbox` com a
      imagem nova)
- [x] 6.2 [raiz] Documentar em `deploy/runbook.md` a tabela
      variável×processo×obrigatória (incluindo quais precisam ser
      idênticas entre processos) e o risco aceito do placeholder
      `changeme` não ser rejeitado no boot
- [x] 6.3 [raiz] Documentar em `deploy/runbook.md` que
      `PublicUrl__BaseUrl` de `apps/inbox` usa o domínio público (mesmo
      valor externo/interno nesta change), com nota de item futuro para
      separar em duas variáveis
- [x] 6.4 [raiz] Documentar em `deploy/runbook.md` os Non-Goals
      explícitos: sem múltiplas réplicas de `apps/workers`/`apps/inbox`,
      sem TLS/WAHA no compose, sem CI/CD

## 7. Verificação manual (sem alterar testes automatizados existentes)

- [x] 7.1 [raiz] Subir `docker-compose.prod.yml` localmente (podman ou
      docker) com um `.env.prod` de teste e confirmar que os sete
      serviços sobem saudáveis — feito com `podman-compose -p
      buteco-prod-verify` (nome de projeto isolado, ver nota no runbook
      sobre colisão com o compose de dev); schema criado do zero,
      migrations aplicadas, todos os apps de pé sem crash
- [x] 7.2 [raiz] Confirmar via nginx do stack: rota profunda do SPA
      sobrevive a refresh, e requisições para os prefixos de `apps/api`/
      `apps/inbox` chegam ao app correto — confirmado (e corrigido o bug
      real de colisão `/agents`/`/channels`/`/mcp-servers` via
      `Sec-Fetch-Mode`, ver 4.2)
- [x] 7.3 [apps/inbox, apps/workers] Executar um round-trip real: enviar
      um webhook (canal de teste) contra o stack containerizado e
      confirmar resposta entregue de volta ao canal — webhook WAHA
      simulado → debounce → dispatch real inbox→api (interno) → RabbitMQ
      → workers → chamada real à API da OpenAI (rejeitada por chave
      falsa, esperado) → task `Failed` → push notification disparada via
      `PublicUrl__BaseUrl`, confirmando D8 na prática (falhou só porque o
      `.env.prod` de teste local usou `localhost` como domínio público,
      artefato do teste, não do Docker). LLM com sucesso não testado
      (sem credencial real disponível) — toda a malha de rede entre
      containers, sim
