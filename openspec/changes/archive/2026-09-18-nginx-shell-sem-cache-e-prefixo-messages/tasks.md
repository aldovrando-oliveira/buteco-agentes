> Todas as tarefas de código rodam em **`apps/frontend`**, no arquivo
> `deploy/nginx.conf`. Nenhuma toca `apps/api`, `apps/inbox` ou `apps/workers`.
> R1 (`messages`) e R2 (shell sem cache) são independentes. A ordem abaixo é a
> de aplicação, não de dependência.
>
> **Não há tarefa de deploy nem de verificação pós-deploy, de propósito.** O
> deploy acontece depois do archive e do merge na `main`, então uma tarefa que
> dependa dele não fecha antes do archive. A verificação pós-deploy vive em dois
> lugares: como procedimento permanente em `docs/deployment.md` §2 ("Redeploy só
> do frontend"), e como obrigação conferível em `02-HISTORICO_E_STATUS.md`
> ("Abertos por `nginx-shell-sem-cache-e-prefixo-messages`").

## 0. Baseline

- [x] 0.1 Registrar que **não há suíte a medir**. Nenhuma suíte lê o
      `nginx.conf` (ver `design.md`, "Testes"). A baseline desta change é a
      tabela D1 do `design.md`, medida sobre a configuração atual em nginx
      descartável. Ela é repetida na tarefa 2.1 sobre o arquivo editado.

      **Registrado em 18/09/2026.** Baseline da D1 (coluna "Atual") medida em
      nginx descartável durante a exploração, sobre `e7212e9`.

## 1. Edições em `apps/frontend/deploy/nginx.conf`

- [x] 1.1 (`apps/frontend`) **Reenumerar os prefixos antes de editar**, com os
      dois greps do comentário do arquivo, sem copiar a lista deste documento.
      Descartar `/health` (exceção deliberada) e os três falsos positivos do D5
      (`/test`, `/indexing-summary`, `/api`). Se aparecer um prefixo faltante
      além de `messages`, ele entra, e o fato é registrado no `design.md`.

      **Feito em 18/09/2026**, com os greps copiados das linhas 16-17 do
      arquivo, sem alteração. Cada literal devolvido foi classificado pela
      linha de código que o contém (`MapGet`/`MapPost`/`MapGroup` = servido;
      `client.GetAsync`/`PostAsJsonAsync` = saída):

      | Grep | Literal | Onde | Classificação |
      |---|---|---|---|
      | api | `/agents` | `Program.cs:100` (`MapA2A`), `AgentMcpBindingEndpoints.cs:14` | **servido** |
      | api | `/auth` | `AuthEndpoints.cs:19` (`MapPost("/auth/login")`) | **servido** |
      | api | `/knowledge-bases` | `KnowledgeBaseEndpoints.cs:19` (`MapGroup`) | **servido** |
      | api | `/knowledge-index` | `KnowledgeIndexEndpoints.cs:36` | **servido** |
      | api | `/mcp-servers` | `McpServerEndpoints.cs:23` (`MapGroup`) | **servido** |
      | api | `/providers` | `ProviderEndpoints.cs:12` | **servido** |
      | api | `/health` | `Program.cs:87` | exceção deliberada |
      | api | `/indexing-summary` | `KnowledgeBaseEndpoints.cs:33` (`group.MapGet`) | ruído: sub-rota de grupo |
      | api | `/test` | `McpServerEndpoints.cs:32` (`group.MapPost`) | ruído: sub-rota de grupo |
      | inbox | `/channels` | `ChannelEndpoints.cs:19` (`MapGroup`) | **servido** |
      | inbox | `/contacts` | `ContactEndpoints.cs:13` (`MapGroup`) | **servido** |
      | inbox | `/internal` | `PushNotificationEndpoints.cs:22` | **servido** |
      | inbox | `/messages` | `MessageSummaryEndpoints.cs:53` | **servido** |
      | inbox | `/sessions` | `SessionSummaryEndpoints.cs:40`, `MessageEndpoints.cs:12` | **servido** |
      | inbox | `/webhooks` | `WebhookEndpoints.cs:15` | **servido** |
      | inbox | `/health` | `Program.cs:129` | exceção deliberada |
      | inbox | `/api` | `WahaOutboundMessageSender.cs:23` (`PostAsJsonAsync`) | ruído: chamada de saída |
      | inbox | **`/agents`** | `AgentReferenceValidator.cs:27` (`client.GetAsync`) | **ruído novo: chamada de saída para o `apps/api`** |

      **Lista derivada.** `apps/api`: `agents`, `auth`, `knowledge-bases`,
      `knowledge-index`, `mcp-servers`, `providers`, e o bloco já tinha todos.
      `apps/inbox`: `channels`, `contacts`, `internal`, `messages`, `sessions`,
      `webhooks`; faltava **só `messages`**. **Nenhum quinto prefixo.**

      **Achado:** um **quarto falso positivo**, ausente do D5: `"/agents` no grep
      do inbox. Registrado no D5 do `design.md` e na nota de ruído do comentário
      (1.4). A exploração não o viu porque a saída foi lida contra a lista
      esperada, e `agents` estava nela, só que no bloco do outro app.
- [x] 1.2 (`apps/frontend`) R1: acrescentar `messages` à regex do bloco que
      encaminha para o `apps/inbox`
      (`^/(channels|contacts|sessions|messages|webhooks|internal)(/|$)`).
- [x] 1.3 (`apps/frontend`) R2: criar `location = /index.html` com
      `add_header Cache-Control "no-store";`, **sem** `root`/`try_files`
      próprios (herda do `server`). **Não** tocar em `location /` (D1).
- [x] 1.4 (`apps/frontend`) Comentário do arquivo:
      - nota de ruído dos greps de conferência ao lado deles (D5);
      - um parágrafo sobre o `no-store` do shell: por que existe (mesma URL,
        duas respostas, cache do browser) e por que está em `= /index.html` e
        não em `location /`;
      - `messages` acrescentado à frase do achado empírico, se ela enumerar
        prefixos;
      - uma frase sobre a armadilha do `add_header`: ele **não é acumulativo**
        entre níveis. Diretivas de nível superior só são herdadas se o nível
        atual não declarar nenhuma. Hoje não há `add_header` no `server`, então
        está correto. Mas, a partir desta change, qualquer `add_header` global
        futuro (`X-Content-Type-Options`, CSP) passará a valer em todo lugar
        **menos** em `/index.html`, silenciosamente, e é justamente o documento
        onde header de segurança mais importa. Quem acrescentar um header global
        tem de repeti-lo em `location = /index.html`.

      **Feito.** A nota de ruído lista os **quatro** falsos positivos (1.1).
      O parágrafo do `no-store` e a armadilha do `add_header` ficam acima do
      próprio `location = /index.html`. O parágrafo cita a change pelo nome, e
      não pelo caminho `openspec/changes/…`, que quebra no arquivamento (mesmo
      defeito D8 da linha 5). **`messages` não entrou na frase do achado
      empírico**, porque ela não lista prefixos de API em geral, só os que são
      **ao mesmo tempo** rota de página do `react-router`. `messages` não é rota
      de página.

## 2. Verificação antes do deploy

- [x] 2.1 (`apps/frontend`) `nginx -t` sobre o arquivo editado, e a tabela D1
      repetida em nginx descartável (`nginx:1.30-alpine`, `dist/` do build,
      upstreams falsos `api`/`inbox`), com a configuração **editada**, não a
      candidata desta proposta. Esperado, linha a linha: `no-store` nos três
      caminhos do shell, JSON do `inbox` em `/messages/summary` (cors), assets
      sem `Cache-Control`. Registrar a saída aqui.

      **Feito em 18/09/2026**, sobre o arquivo do repositório depois das
      edições 1.2 a 1.4. `dist/` foi reconstruído com `npm run build`.

      - `nginx -t` sobre o arquivo **sem alteração**: *"syntax is ok / test is
        successful"*.
      - Na cópia montada no contêiner, a única diferença para o arquivo do repo
        é `resolver 127.0.0.11` → `resolver 10.89.4.1`. O DNS embutido do
        Docker não existe numa rede do podman. O `diff` foi conferido na mesma
        execução e devolveu só essa linha.

      | Modo | Requisição | Status | Content-Type | Cache-Control | Corpo | Esperado | |
      |---|---|---|---|---|---|---|---|
      | navigate | `/` | 200 | text/html | no-store | shell | shell + no-store (index) | ✓ |
      | navigate | `/index.html` | 200 | text/html | no-store | shell | idem | ✓ |
      | navigate | `/agents` | 200 | text/html | no-store | shell | shell + no-store (rewrite) | ✓ |
      | navigate | `/agents/123` | 200 | text/html | no-store | shell | idem | ✓ |
      | navigate | `/channels/abc` | 200 | text/html | no-store | shell | idem (bloco inbox) | ✓ |
      | navigate | `/knowledge-bases/abc` | 200 | text/html | no-store | shell | idem | ✓ |
      | navigate | `/inventory` | 200 | text/html | no-store | shell | shell + no-store (try_files) | ✓ |
      | navigate | `/login` | 200 | text/html | no-store | shell | idem | ✓ |
      | navigate | `/channels/new` | 200 | text/html | no-store | shell | idem (rewrite, bloco inbox) | ✓ |
      | navigate | `/agents-ui/123` | 200 | text/html | no-store | shell | idem (try_files) | ✓ |
      | cors | `/agents` | 200 | application/json | — | `{"svc":"api"}` | upstream api, sem no-store | ✓ |
      | cors | `/index.html` | 200 | text/html | no-store | shell | shell + no-store | ✓ |
      | cors | `/messages/summary?from=x&to=y` | 200 | application/json | — | `{"svc":"inbox"}` | **upstream inbox (R1)** | ✓ |
      | navigate | `/messages/summary` | 200 | text/html | no-store | shell | shell (não é rota de página; inócuo) | ✓ |
      | cors | `/sessions/summary` | 200 | application/json | — | `{"svc":"inbox"}` | upstream inbox | ✓ |
      | cors | `/internal/push-notifications` | 200 | application/json | — | `{"svc":"inbox"}` | upstream inbox | ✓ |
      | cors | `/knowledge-index/diagnostics` | 200 | application/json | — | `{"svc":"api"}` | upstream api | ✓ |
      | cors | `/assets/index-Bq85FZl6.js` | 200 | application/javascript | — | JS | **sem Cache-Control** | ✓ |
      | cors | `/assets/index-CrpdPg9y.css` | 200 | text/css | — | CSS | **sem Cache-Control** | ✓ |
      | navigate | `/favicon.svg` | 200 | image/svg+xml | — | SVG | **sem Cache-Control** | ✓ |

      Resultado: 20 de 20 conforme o esperado. Contêineres e rede removidos ao
      final. A diferença para a D1 da exploração é o arquivo medido: aqui é o
      editado, com os comentários novos e a regex real. O `/login` e o
      `/channels/new` entraram como casos a mais de rota do SPA.

## 3. Spec e documentação

- [x] 3.1 (`openspec`) Conferir que a delta de `server-deployment` bate com o
      arquivo editado: `messages` na enumeração do cenário de roteamento, e os
      caminhos do shell do requirement novo iguais aos testados em 2.1.

      **Confere.** O cenário de roteamento enumera `/messages` no bloco do
      `apps/inbox`. Os caminhos do shell no requirement ADDED (`/` e
      `/index.html`, rewrite em `/agents`, `try_files` em `/inventory`) e o
      cenário de assets correspondem a linhas medidas na 2.1. A spec não
      enumera os falsos positivos dos greps, e não precisa: eles não são
      requisito, são nota de manutenção do `nginx.conf`.
- [x] 3.2 (documentação) `docs/`: conferir se algum arquivo afirma a lista de
      prefixos do nginx ou o comportamento de cache do shell. A enumeração da
      proposta (grep por `knowledge-index`, `/internal`, `Sec-Fetch`,
      `no-store`, `Cache-Control`, `prefixo`) não achou afirmação desse tipo; só
      `docs/development.md:302` cita `POST /internal/push-notifications` como
      rota, e isso não muda. Repetir a busca sobre o estado do momento da
      aplicação e corrigir só o que a change tornar falso.

      **Repetida em 18/09/2026** sobre `docs/*.md`, `README.md` e
      `CONTRIBUTING.md` (acrescentando `/messages` e `nginx` aos termos). Nada
      a corrigir. As menções ao nginx do stack (`docs/configuration.md:228`,
      `docs/development.md:523`, `docs/deployment.md:63,67,111,151`) dizem que
      ele "serve o SPA e faz proxy", o que continua verdadeiro, e nenhuma afirma
      a lista de prefixos nem o cache do shell. O caso novo do §2 do
      `docs/deployment.md` é a 3.4.
- [x] 3.3 (documentação) `02-HISTORICO_E_STATUS.md` (itens novos em "Itens em aberto"; o fechamento da 2.4 no histórico da change):
      - os dois achados: o cache do browser guardando HTML sob URL de API (R2),
        e a quarta ocorrência de prefixo faltante (R1);
      - o fechamento **integral** da pendência 2.4 de `fix-stack-servidor-lacunas`.
        O item **sai** de "Itens em aberto" em vez de ser reescrito lá. O
        registro do fechamento vai no histórico desta change e traz: a tabela
        de atribuição por comportamento da V4 (roteamento); a evidência do
        round-trip no canal (18/09/2026, WAHA e Telegram, observação em produção
        pelo mantenedor, não teste automatizado); e o que ela **não** garante
        (ponto no tempo, caminho sem cobertura automatizada);
      - o gatilho de `Cache-Control` em resposta de API (qualquer `ETag`/
        `Last-Modified` novo em API reabre o envenenamento no sentido inverso);
      - a lista de prefixos mantida à mão como change própria candidata;
      - para a change do `/painel/`: o `no-store` já está no `nginx.conf`;
      - a causa provável do ETag ausente no HTML (D4).

      **Feito.** Entrada de histórico nova, "Hotfix do nginx: shell sem cache e
      prefixo `messages`", no fim de "Changes aplicadas", com o estado real:
      aplicada até a 2.1, deploy pendente, nada commitado. Ela traz os dois
      achados, o mecanismo da idade do deploy, o fechamento integral da 2.4
      (roteamento por atribuição de comportamento, round-trip observado em WAHA
      e Telegram, e o que isso não garante) e o quarto falso positivo. A
      pendência 2.4 foi **retirada** de "Abertos por `fix-stack-servidor-lacunas`".
      Seção nova "Abertos por `nginx-shell-sem-cache-e-prefixo-messages`" no fim
      do arquivo, onde estão as duas seções "Abertos por" mais recentes: deploy
      pendente, contingência do Cloudflare, recuperação do browser, gatilho de
      `Cache-Control` em API, lista mantida à mão, aviso para a change do
      `/painel/`, `ETag` do HTML.

      A correção de status da 5a-4 **não** faz parte desta tarefa: está no
      parágrafo de "Status atual" sobre bases de conhecimento, marcada com data,
      e não cita esta change.
- [x] 3.4 (documentação) `docs/deployment.md` §2: acrescentar o caso de redeploy
      **só do frontend** (`build frontend` + `up -d frontend`, sem parar o
      `apps/inbox` e sem `migrator`), com a verificação que o acompanha:
      - a bateria de `curl` da V2 nas duas formas, com os comandos exatos e o
        critério de cada linha;
      - a checagem de assets inalterados;
      - `/messages/summary` com `Sec-Fetch-Mode: cors` → `401`, não `text/html`;
      - a contingência V3 resumida, com ponteiro para o `design.md` arquivado.

      Não reescrever a sequência existente (parar o inbox + `migrator`): ela
      continua certa para o caso dela. A sequência nova é a do design (V1). Se a
      execução real divergir, corrige-se o §2 depois.

      **Feito em 18/09/2026.** Subseção nova "Redeploy só do frontend", com
      "Verificação depois do redeploy do frontend", logo depois da sequência
      existente. A verificação deixou de ser tarefa desta change e virou
      procedimento de **qualquer** redeploy só do frontend.

      **Achado, não corrigido aqui:** os comandos da sequência existente do §2 não
      passam `--env-file .env.prod`. Desde `fix-stack-servidor-lacunas`,
      catorze variáveis são obrigatórias na interpolação, e sem o `--env-file` o
      Compose falha ao processar o arquivo. O §1 do mesmo documento usa o
      `--env-file`. O caso novo também usa. A sequência antiga ficou como
      está, por instrução de não reescrevê-la, e o achado foi para o `02`.
