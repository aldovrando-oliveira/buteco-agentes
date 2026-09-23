> **Toda tarefa de configuração roda em `apps/frontend`**, no arquivo
> `deploy/nginx.conf`. Nenhuma toca `apps/api`, `apps/inbox`, `apps/workers` nem
> `apps/frontend/src/`. Nenhuma migração, nenhuma rota, nenhuma tela.
>
> **Não há tarefa de deploy nem de verificação pós-deploy, de propósito**
> (`design.md`, D3). O deploy acontece depois do archive e do merge, então uma
> tarefa que dependa dele não fecha antes do archive. A change fecha com **"a
> linha está no arquivo versionado e o nginx descartável a exerce"**, nunca com
> "o painel alcança a rota" — convenção 13. A verificação pós-deploy vive como
> item com gatilho e posição em `02-HISTORICO_E_STATUS.md`, e o procedimento
> permanente já existe em `docs/deployment.md` §2 "Redeploy só do frontend".

## 0. Baseline

- [x] 0.1 Registrar que **não há suíte a medir**. Nenhuma das quatro suítes lê
      `nginx.conf` (`design.md`, D3). A baseline desta change é a tabela de
      discriminação da D3, medida em nginx descartável sobre a configuração
      **atual** (sem `insights`): esperado `text/html` nas duas primeiras linhas.
      Ela é repetida na tarefa 2.2 sobre o arquivo **editado**.

      Registrar aqui a saída medida, com data.

      **Medido em 23/09/2026**, sobre o `HEAD` `d020fe0`, em nginx descartável
      (`docker.io/library/nginx:1.30-alpine` via `podman`, rede `butecongxtest`,
      `dist/` reconstruído com `npm run build`, upstreams falsos `api`/`inbox`).
      Única diferença da cópia montada para o arquivo do repo, conferida por
      `diff` na mesma execução: `resolver 127.0.0.11` → `resolver 10.89.5.1`, o
      gateway da rede do podman — o DNS embutido do Docker não existe ali.

      | modo | requisição | status | content-type | cache-control | corpo |
      |---|---|---|---|---|---|
      | cors | `/insights/system?from=…&to=…` | 200 | **text/html** | no-store | shell |
      | cors | `/insights/agents/abc` | 200 | **text/html** | no-store | shell |
      | navigate | `/insights` | 200 | text/html | no-store | shell |
      | cors | `/insightsxyz` | 200 | text/html | no-store | shell |
      | cors | `/agents` | 200 | application/json | — | `{"svc":"api"}` |
      | cors | `/messages/summary?from=x&to=y` | 200 | application/json | — | `{"svc":"inbox"}` |
      | navigate | `/index.html` | 200 | text/html | no-store | shell |
      | cors | `/assets/index-Yg5wGJB_.js` | 200 | application/javascript | — | JS |

      **A baseline reproduz o defeito de produção fora de produção**, nas duas
      linhas discriminantes: a requisição que o painel faz devolve o shell com
      `200`. As outras seis são o estado a preservar.

      **Achado de método, sobre o próprio harness:** a primeira execução saiu com
      a linha do asset **faltando**, sem erro visível — `cat body.out | head -c 20`
      leva SIGPIPE num arquivo de 935 KB, e com `set -o pipefail` o `set -e`
      encerrava o script na última linha da tabela. Uma tabela que termina cedo
      parece uma tabela completa. É a forma da convenção 21 (comando de varredura
      que falha em silêncio produz a mesma saída que a ausência do caso), e aqui
      foi pega só porque o número de linhas esperadas estava escrito antes.
      Corrigido para `head -c 24 body.out`, sem pipe.

## 1. Edição em `apps/frontend/deploy/nginx.conf`

- [x] 1.1 (`apps/frontend`) **Reenumerar os prefixos antes de editar**, com os
      dois greps das linhas 16-17 do próprio arquivo, sem copiar lista alguma
      deste documento nem do `design.md`. Classificar cada literal pela linha de
      código que o contém (`MapGet`/`MapPost`/`MapGroup` = servido;
      `client.GetAsync`/`PostAsJsonAsync` = saída), pelo método que a tarefa 1.1
      da `nginx-shell-sem-cache-e-prefixo-messages` fixou. Descartar `/health`
      (exceção deliberada) e os quatro falsos positivos já nomeados no comentário
      (`/test`, `/indexing-summary`, `/api`, e `/agents` no grep do inbox).

      **Se aparecer prefixo faltante além de `insights`, ele entra na mesma
      edição, e o fato é registrado no `design.md` (convenção 9) e no `02`.**

      A D2 mediu em 23/09/2026 que `"/insights` sai na saída de `apps/api` e que
      não há décimo primeiro literal — **isso não dispensa a tarefa**: a medição é
      sobre o `HEAD` daquele dia, e a régua é reconferir sobre o estado do momento
      da aplicação, não confiar na medição anterior (convenção 6). Registrar a
      saída real aqui, com data.

      **Feito em 23/09/2026**, com os dois greps copiados das linhas 16-17 do
      arquivo, sem alteração. Cada literal classificado pela linha de código que
      o contém:

      | grep | literal | onde | classificação |
      |---|---|---|---|
      | api | `/agents` | `Program.cs:134` (`MapA2A`), `AgentMcpBindingEndpoints.cs:14` (`MapPut`) | **servido** |
      | api | `/auth` | `AuthEndpoints.cs:19` (`MapPost`) | **servido** |
      | api | **`/insights`** | `InsightsEndpoints.cs:34` (`app.MapGet("/insights/system", …)`) | **servido — o que falta** |
      | api | `/knowledge-bases` | `KnowledgeBaseEndpoints.cs:19` (`MapGroup`) | **servido** |
      | api | `/knowledge-index` | `KnowledgeIndexEndpoints.cs:36` (`MapGet`) | **servido** |
      | api | `/mcp-servers` | `McpServerEndpoints.cs:23` (`MapGroup`) | **servido** |
      | api | `/providers` | `ProviderEndpoints.cs:12` (`MapGet`) | **servido** |
      | api | `/health` | `Program.cs:120` (`MapHealthChecks`) | exceção deliberada |
      | api | `/indexing-summary` | `KnowledgeBaseEndpoints.cs:33` (`group.MapGet`) | ruído: sub-rota de grupo |
      | api | `/test` | `McpServerEndpoints.cs:32` (`group.MapPost`) | ruído: sub-rota de grupo |
      | inbox | `/channels` | `ChannelEndpoints.cs:19` (`MapGroup`) | **servido** |
      | inbox | `/contacts` | `ContactEndpoints.cs:13` (`MapGroup`) | **servido** |
      | inbox | `/internal` | `PushNotificationEndpoints.cs:22` (`RoutePattern`) | **servido** |
      | inbox | `/messages` | `MessageSummaryEndpoints.cs:53` (`MapGet`) | **servido** |
      | inbox | `/sessions` | `SessionSummaryEndpoints.cs:40`, `MessageEndpoints.cs:12` | **servido** |
      | inbox | `/webhooks` | `WebhookEndpoints.cs:15` (`MapPost`) | **servido** |
      | inbox | `/health` | `Program.cs:129` (`MapHealthChecks`) | exceção deliberada |
      | inbox | `/api` | `WahaOutboundMessageSender.cs:23` (`PostAsJsonAsync`) | ruído: chamada de saída |
      | inbox | `/agents` | `AgentReferenceValidator.cs:27` (`client.GetAsync`) | ruído: chamada de saída para o `apps/api` |

      **Lista derivada.** `apps/api`: `agents`, `auth`, **`insights`**,
      `knowledge-bases`, `knowledge-index`, `mcp-servers`, `providers` — sete
      servidos, e o bloco tem seis. `apps/inbox`: `channels`, `contacts`,
      `internal`, `messages`, `sessions`, `webhooks` — seis servidos, e o bloco
      tem os seis. **Falta só `insights`, e não há oitavo prefixo.**

      **Nada de novo em relação à D2**, incluindo o ruído: os quatro falsos
      positivos continuam sendo exatamente os quatro já nomeados no comentário.
      O `grep` confirmou-se como método — ver 1.3.

- [x] 1.2 (`apps/frontend`) Acrescentar `insights` ao grupo da `location` da
      linha 59, a do `apps/api`:
      `^/(agents|providers|mcp-servers|knowledge-bases|knowledge-index|insights|auth)(/|$)`.
      **Bloco existente, não bloco novo** (`design.md`, D1). Não tocar no `if` de
      `Sec-Fetch-Mode`, no `set $api_upstream`, nos `proxy_set_header`, no bloco
      do `apps/inbox`, no `location = /index.html` nem no `location /`.

      **Feito.** `git diff --stat`: **1 arquivo, 1 inserção, 1 remoção** — a
      linha 59, e só ela. Nenhum outro bloco tocado.

- [x] 1.3 (`apps/frontend`) Comentário do arquivo: **editar somente se a 1.1
      tornar alguma frase falsa** (convenção 9). A D2 prevê que **não torna** — o
      `grep` não tem ponto cego a descrever e o conjunto de ruído não muda.
      Registrar aqui o veredito, e **"nada a alterar" é resultado legítimo a
      escrever**, não tarefa pulada.

      Não acrescentar `insights` à frase do achado empírico do `Sec-Fetch-Mode`:
      ela lista os prefixos que **já hoje** são ao mesmo tempo rota de página do
      `react-router` e prefixo de API, e `/insights` só será um na etapa 4
      (`design.md`, D4). Acrescentá-lo agora afirmaria uma rota de página que não
      existe.

      **Veredito: nada a alterar no comentário**, e é resultado, não tarefa
      pulada. Conferido frase a frase contra a 1.1:

      - *"A lista de prefixos de cada bloco cobre TODOS os prefixos de rota que
        `apps/api` e `apps/inbox` servem"* — **a frase estava FALSA, e a edição
        da 1.2 a tornou verdadeira de novo.** Não era o comentário que precisava
        de correção; era o arquivo que precisava obedecê-lo.
      - *"Para reconferir"* e os dois greps — confirmados como método pela 1.1:
        o comando devolve `"/insights` e a classificação o dá como servido.
        **Sem ponto cego a descrever** (D2).
      - a nota de ruído conhecido (`/test`, `/indexing-summary`, `/api`, e
        `/agents` no grep do inbox) — continuam sendo exatamente esses quatro, e
        a 1.1 não achou um quinto.
      - o achado empírico do `Sec-Fetch-Mode`, que enumera `/agents`,
        `/channels`, `/mcp-servers` e `/knowledge-bases` — **`/insights` não
        entra**: `apps/frontend/src/app/routes.tsx` declara `login`, `inventory`,
        `agents`, `mcp-servers`, `knowledge-bases` e `channels`, e **nenhuma**
        `insights` (conferido em 23/09/2026). A rota de página nasce na etapa 4.

## 2. Verificação antes do deploy (é o que fecha a change)

- [x] 2.1 (`apps/frontend`) `nginx -t` sobre o arquivo editado.

      **Feito em 23/09/2026:** *"syntax is ok / test is successful"*.

- [x] 2.2 (`apps/frontend`) Repetir a tabela de discriminação da D3 em **nginx
      descartável**, sobre o arquivo **editado** do repositório — não sobre a
      configuração candidata deste documento. Molde: tarefa 2.1 da
      `nginx-shell-sem-cache-e-prefixo-messages` (`nginx:1.30-alpine`, `dist/` do
      build montado, upstreams falsos `api`/`inbox` devolvendo `{"svc":"api"}` /
      `{"svc":"inbox"}`, `podman`). Conferir e registrar o `diff` da cópia montada
      contra o arquivo do repo — a única diferença esperada é o `resolver`, que na
      rede do podman não é `127.0.0.11`.

      As quatro linhas, e **quais duas discriminam**:

      | requisição | modo | esperado |
      |---|---|---|
      | `/insights/system?from=…&to=…` | cors | `{"svc":"api"}`, `application/json` — **discrimina** |
      | `/insights/agents/abc` | cors | `{"svc":"api"}` — **discrimina** (prefixo da change B) |
      | `/insights` | navigate | shell, `no-store` — aprova nos dois estados, de propósito |
      | `/insightsxyz` | cors | shell — aprova nos dois estados, de propósito (`(/|$)`) |

      Acrescentar as linhas de não-regressão: `/agents` (cors) → `{"svc":"api"}`,
      `/messages/summary` (cors) → `{"svc":"inbox"}`, `/index.html` → `no-store`,
      um `/assets/*` → **sem** `Cache-Control`.

      Registrar a saída linha a linha, e remover contêineres e rede ao final.

      **Feito em 23/09/2026**, sobre o arquivo do repositório depois da edição
      1.2. `dist/` reconstruído com `npm run build`. `diff` da cópia montada
      contra o arquivo do repo conferido na mesma execução: **só o `resolver`**
      (`127.0.0.11` → `10.89.5.1`, gateway da rede do podman).

      | modo | requisição | status | content-type | cache-control | corpo | esperado | |
      |---|---|---|---|---|---|---|---|
      | cors | `/insights/system?from=…&to=…` | 200 | **application/json** | — | `{"svc":"api"}` | upstream api — **discrimina** | ✓ |
      | cors | `/insights/agents/abc` | 200 | **application/json** | — | `{"svc":"api"}` | upstream api — **discrimina** (change B) | ✓ |
      | navigate | `/insights` | 200 | text/html | no-store | shell | shell; igual nos dois estados | ✓ |
      | cors | `/insightsxyz` | 200 | text/html | no-store | shell | não casa, `(/\|$)`; igual nos dois estados | ✓ |
      | cors | `/agents` | 200 | application/json | — | `{"svc":"api"}` | não-regressão | ✓ |
      | cors | `/messages/summary?from=x&to=y` | 200 | application/json | — | `{"svc":"inbox"}` | não-regressão (bloco do inbox) | ✓ |
      | navigate | `/index.html` | 200 | text/html | no-store | shell | não-regressão (shell sem cache) | ✓ |
      | cors | `/assets/index-Yg5wGJB_.js` | 200 | application/javascript | — | JS | não-regressão: **sem `Cache-Control`** | ✓ |

      **8 de 8 conforme o esperado.** Contêineres e rede removidos ao final
      (`trap` no harness).

- [x] 2.3 (`apps/frontend`) **Reintroduzir o defeito de propósito e ver reprovar**
      (convenção 15): apagar `insights` da regex na cópia montada, repetir as duas
      linhas discriminantes, confirmar que voltam `text/html`, e restaurar. Sem
      este passo o guarda não está verificado, e guarda não verificado é pior que
      nenhum.

      **Se alguma das duas linhas aprovar com o defeito presente**, parar: o
      guarda é não-discriminante e a causa tem de ser achada antes de seguir.

      **Feito em 23/09/2026.** O defeito foi reintroduzido apagando `insights` da
      regex **do arquivo já editado**, e o resultado foi conferido por `diff`
      contra `git show HEAD:apps/frontend/deploy/nginx.conf`: **idêntico**. Ou
      seja, a reintrodução reproduz exatamente o estado de produção, e prova de
      passagem que a edição da 1.2 não mexeu em mais nada.

      | requisição | editado | defeito reintroduzido |
      |---|---|---|
      | `/insights/system` (cors) | `application/json`, `{"svc":"api"}` | **`text/html`, shell** |
      | `/insights/agents/abc` (cors) | `application/json`, `{"svc":"api"}` | **`text/html`, shell** |
      | as outras **seis** linhas | — | **idênticas nos dois estados** |

      **As duas discriminam, 2 de 2. As outras seis não discriminam, 6 de 6, e é
      o desejado:** elas são não-regressão e os dois casos de borda (D3), e uma
      delas mudar aqui seria o defeito. Registrar quais discriminam é o que evita
      a quinta forma da convenção 15 — guarda cujo critério o sistema já satisfaz
      por outro motivo.

      Arquivo do repositório **intacto** durante todo o passo: a cópia com o
      defeito viveu só no scratchpad, e `git diff --stat` continua em 1 arquivo /
      1 inserção / 1 remoção.

- [x] 2.4 (`apps/frontend`) **Se o `podman` não estiver disponível**, registrar
      que o guarda caiu para **não-discriminante** e que a change fecha com
      `nginx -t` mais conferência do literal na regex — **declarado, não
      contornado** (`design.md`, D3, último risco). Não fechar como se a 2.2 e a
      2.3 tivessem rodado.

      **Não se aplicou.** `podman` presente e a VM de pé
      (`podman-machine-default`, `applehv`, backend de rede `netavark`). A 2.2 e a
      2.3 rodaram de verdade, e o guarda **é** discriminante. O item fica escrito
      porque a contingência valia antes de a 2.2 rodar, não depois.

## 3. Spec e documentação

- [x] 3.1 (`openspec`) Conferir que a delta de `server-deployment` bate com o
      arquivo editado: `insights` na enumeração do cenário de roteamento, e os
      dois cenários novos (`/insights/system`, e o de cobertura do prefixo com
      `/insightsxyz` de fora) correspondendo a linhas de fato medidas na 2.2.

      **Confere.** O cenário "Requisições de API são roteadas para o app correto"
      enumera `/insights` no bloco do `apps/api`, na mesma posição em que o
      literal entrou na regex. Os dois cenários novos correspondem a linhas
      medidas: `/insights/system` é a linha 1 da 2.2, e o de cobertura do prefixo
      é as linhas 2 e 4 juntas (`/insights/agents/abc` encaminhada,
      `/insightsxyz` **não**). Nenhum cenário da delta afirma comportamento que a
      2.2 não tenha exercido.

      O parágrafo acrescentado ao texto do requirement — *conferência de escopo
      restrita aos arquivos do app que serve a rota SHALL NOT ser tratada como
      suficiente* — **não** tem cenário próprio, e de propósito: é régua de
      processo de change, não comportamento do nginx observável por requisição.
      O que é observável já está no cenário "Prefixo servido por um app e ausente
      do nginx é detectado", que a delta preserva sem alteração.

- [x] 3.2 (documentação) `docs/`: repetir sobre o estado do momento a busca por
      arquivo que afirme a lista de prefixos do nginx (termos: `insights`,
      `nginx`, `prefixo`, `knowledge-index`, `/internal`, `Sec-Fetch`). A
      enumeração preliminar de 23/09 não achou nenhum — as menções de
      `docs/configuration.md:229,254`, `docs/development.md:523` e
      `docs/deployment.md:69,74,238` dizem que o nginx "serve o SPA e faz proxy",
      o que continua verdadeiro. Corrigir só o que esta change tornar falso.

      **Repetida em 23/09/2026** sobre `docs/*.md`, `README.md` e
      `CONTRIBUTING.md`. **Nada a corrigir**, e `insights` não aparece em
      documento nenhum de `docs/`.

      **O achado é o contrário do previsto, e é bom:** `docs/deployment.md` §2 já
      traz a checagem que fecharia este defeito, e **já a traz genérica**. A
      checagem 3 usa `/messages/summary` como exemplo, mas o texto das linhas
      154-158 diz que *"para conferir todos os prefixos, e não só `/messages`, a
      lista está nos blocos `location ~` do `nginx.conf`. Cada prefixo com
      `Sec-Fetch-Mode: cors` e sem token tem de responder um status do app
      (`401`/`400`/`404`), nunca `200 text/html`"*. O critério do item aberto da
      D3 é **literalmente** esse, então não há procedimento novo a escrever — o
      item aberto aponta para ele em vez de duplicá-lo.

      **E isso afina o veredito da D2**: o método de conferência existia em
      **dois** lugares (o `grep` no `nginx.conf`, para quem edita; esta checagem
      no `docs/deployment.md`, para quem faz deploy), os dois corretos, e o
      defeito passou pelos dois. Reforça que a lacuna é de gatilho — nada
      **obriga** a rodar qualquer um deles quando nasce uma rota.

- [x] 3.3 (documentação) `02-HISTORICO_E_STATUS.md` — entrada de histórico nova,
      no fim de "Changes aplicadas", com o estado real (aplicada até 2.3, deploy
      pendente, nada commitado sem autorização). Ela traz:

      - **o defeito e como foi isolado**: a tabela de quatro passos, com o `401`
        de dentro do container como o passo discriminante e o porquê de ele
        separar as duas hipóteses;
      - **a quinta ocorrência** da classe, ligada às quatro anteriores
        (`internal`, `knowledge-bases`, `knowledge-index`, `messages`);
      - **a régua**: rota nova em `apps/api`/`apps/inbox` exige entrada no
        roteamento do nginx, e isso é passo de **deploy**, não de código — com a
        causa que a torna difícil de pegar (o arquivo mora em
        `apps/frontend/deploy/`, e a change anterior declarou `apps/frontend` como
        "nada tocado" **corretamente**);
      - **o quarto desvio de escopo da `rotas-de-agregacao-sistema`**, ao lado dos
        três já registrados em "Conferência de escopo de arquivo — três desvios da
        lista fechada", e **de natureza diferente**: os três são arquivo tocado ou
        não tocado, este é passo de deploy não previsto, que conferência de lista
        de arquivos não alcança;
      - **o veredito do `grep`** (D2): ele **teria** pego `/insights`, logo a
        lacuna é de processo e não ponto cego — com a saída medida ao lado;
      - **a verificação em produção da etapa 3**, que já aconteceu: as quatro
        requisições, e o corpo que confirmou o contrato (mapa de regimes com as
        duas datas; série omitindo os dias anteriores ao regime; nulo preservado e
        visível em `gemini`/`openai`; os cinco `caveats`; `nonProviderResidual` em
        vez de "tempo em tools"; M32 com as duas populações e `observedStates`; os
        dois `Expired` no par origem→destino). **Com o regime colado** (convenção
        22): medido em 23/09/2026, piloto, `America/Sao_Paulo`, primeira janela
        01/09 a 23/09, 156 execuções, regimes `execution` 22/09 01:21 e
        `embedding` 23/09 01:18 — e o gatilho de que citar qualquer número dele
        sobre janela iniciada antes de 22/09 01:21 é citá-lo fora do regime;
      - **o item da convenção 18 no extremo inferior** (D7): com uma palavra de
        produção a razão lógica : comentário é degenerada, e quem decide o tamanho
        é a projeção de **registro** (~80% das linhas fora dos artefatos OpenSpec).

      **Feito.** Entrada nova — "A rota da etapa 3 não chegava ao painel:
      `insights` fora do nginx" — no fim de "Changes aplicadas", logo depois da
      conferência de escopo da `rotas-de-agregacao-sistema`, que é onde o quarto
      desvio precisa ser lido junto dos três. **173 linhas.** Traz os oito itens
      da tarefa, e mais **dois achados que só a aplicação produziu**:

      - **o método de conferência existia em DOIS lugares** — o `grep` no
        `nginx.conf` e a checagem genérica do `docs/deployment.md` §2 —, os dois
        corretos, e o defeito passou pelos dois. Reforça que a lacuna é de
        gatilho, não de método (3.2);
      - **o SIGPIPE do harness** (0.1), que é a convenção 21 aparecendo dentro da
        própria verificação desta change.

- [x] 3.4 (documentação) `02-HISTORICO_E_STATUS.md` — seção nova **"Abertos por
      `roteamento-insights-no-nginx`"**, no fim do arquivo, junto das demais
      "Abertos por". Três itens:

      - **A verificação pós-deploy**, que esta change não pode fechar.
        **Gatilho:** o próximo `build frontend` + `up -d frontend` contra o
        piloto. **Posição:** este item, com o procedimento em
        `docs/deployment.md` §2. **Critério:** `/insights/system` com
        `Sec-Fetch-Mode: cors` e **sem token** → `401`, não `200 text/html`.
      - **Para a change B (`GET /insights/agents/{id}`): nada a fazer no nginx.**
        A entrada `insights` com `(/|$)` já a cobre, e supor que precisa custaria
        uma segunda edição do mesmo arquivo. **Gatilho:** a change B começar.
        **Posição:** antes dela.
      - **A lista de prefixos mantida à mão** — **não** um item novo: acrescentar
        a quinta ocorrência ao item já existente em "Abertos por
        `nginx-shell-sem-cache-e-prefixo-messages`", com o que ela acrescenta à
        candidata (D5): as quatro primeiras foram "o autor esqueceu de editar a
        lista"; esta tem uma causa a mais, o arquivo morar no app errado, que
        checklist humano não cobre e derivação automática cobre.

      **Para a etapa 4**, registrar junto (D4): `/insights` vai ser rota de página
      **e** prefixo de API, o `Sec-Fetch-Mode` do bloco já resolve, e o
      `nginx.conf` **não** deve ser tocado por causa disso.

      **Feito.** Seção "Abertos por `roteamento-insights-no-nginx` (2026-09-23)"
      no fim do arquivo, com **três** itens: a verificação pós-deploy (gatilho,
      posição e critério, apontando para o `docs/deployment.md` §2 em vez de
      duplicá-lo — ver 3.2); a change B (nada a fazer, e **medido**, não suposto);
      e a etapa 4, com a ressalva de qual frase do comentário passa a merecer
      `insights` quando a rota de página existir.

      **A lista mantida à mão não virou item novo**, como a tarefa pedia: o item
      existente em "Abertos por `nginx-shell-sem-cache-e-prefixo-messages`" foi
      atualizado de **quatro** para **cinco** ocorrências, com o que a quinta
      acrescenta — e o acréscimo **muda a forma exigida da candidata**, não só a
      contagem: checklist humano cobre a causa das quatro primeiras e **não**
      cobre a desta (arquivo no app errado); guarda em `scripts/` cobre as duas,
      porque não pergunta qual app está sendo mexido. Deixou de ser preferência
      entre formas e virou requisito.

- [x] 3.5 (documentação) `CHANGELOG.md`: entrada em `Fixed`, no molde da de
      `messages` — o que o usuário via (a página de Insights sem dado, com `200`
      e HTML), a causa em uma frase, que é a quinta ocorrência, e que a rota não
      mudou, só passou a ser alcançável.

      **Feito**, no topo de `Fixed` — é onde as entradas mais recentes ficam
      neste arquivo (a da compactação, de 22/09, abria a seção). **10 linhas.**
      Diz o que o usuário via, o passo que isolou a causa (`401` por dentro), que
      é a quinta ocorrência, que a rota não mudou, e que a entrada cobre as rotas
      sob `/insights/` — este último para o leitor não esperar uma linha nova no
      CHANGELOG quando a change B chegar.

## 4. Fechamento

- [x] 4.1 (`openspec`) `openspec validate --all` verde.

      **Feito:** `Totals: 58 passed, 0 failed (58 items)`.

- [x] 4.2 **Conferência de escopo de arquivo, contra a lista fechada.** A lista
      desta change, e nada além dela:

      | arquivo | previsto |
      |---|---|
      | `apps/frontend/deploy/nginx.conf` | M — uma palavra na linha 59; comentário só se a 1.1 o exigir |
      | `openspec/changes/roteamento-insights-no-nginx/proposal.md` | A |
      | `openspec/changes/roteamento-insights-no-nginx/design.md` | A |
      | `openspec/changes/roteamento-insights-no-nginx/tasks.md` | A |
      | `openspec/changes/roteamento-insights-no-nginx/specs/server-deployment/spec.md` | A |
      | `02-HISTORICO_E_STATUS.md` | M |
      | `CHANGELOG.md` | M |

      Rodar `git status --porcelain` e comparar. **Todo arquivo fora da lista, e
      todo arquivo da lista não tocado, é desvio a registrar com a causa** — nunca
      absorvido em silêncio. É a conferência que a `rotas-de-agregacao-sistema`
      fez e que, mesmo bem feita, não alcançou este defeito: ela é sobre arquivo
      tocado, e o que faltou ali foi um passo de deploy.

      **Aviso de alcance, para esta mesma conferência não enganar de novo:** um
      `git status` limpo contra esta lista **não** significa que a linha de
      trabalho está completa — significa que esta change não vazou escopo. O que
      ela não alcança continua sendo o que não vira arquivo.

      **Feito em 23/09/2026. Nenhum desvio: a lista fechou exata.**

      ```
      $ git status --porcelain
       M 02-HISTORICO_E_STATUS.md
       M CHANGELOG.md
       M apps/frontend/deploy/nginx.conf
      ?? openspec/changes/roteamento-insights-no-nginx/
      ```

      Três modificados e o diretório da change, com os quatro artefatos
      previstos. **Nenhum arquivo fora da lista, nenhum arquivo da lista não
      tocado** — inclusive o caso condicional do comentário do `nginx.conf`, que
      a 1.3 mediu não ser necessário e que por isso não aparece como desvio.

      **Um arquivo de fato reconstruído e ausente daqui, de propósito:**
      `apps/frontend/dist/`, por `npm run build` na 2.2. É ignorado por git e não
      é entregável — o build de produção sai do `Dockerfile`, não deste diretório.

      **A ressalva de alcance, repetida porque é o ponto:** esta conferência
      fechou exata **na change anterior também**, e ainda assim o defeito que
      esta change corrige passou por ela. Lista fechada exata significa *não
      vazei escopo*, nunca *não faltou nada* — o que faltou lá não virava arquivo
      em `apps/api`, e por isso nenhum `git status` o mostraria.

- [x] 4.3 **Conferência da projeção da D7**, contra o entregue: linhas de produção
      (lógica e comentário, separadas), linhas de spec, linhas de `02`, linhas de
      `CHANGELOG`, e a fração que o registro representa. Registrar no `02` o erro
      de cada dimensão, no molde das conferências anteriores da convenção 18, e em
      especial se a projeção de **registro** — a que decide o tamanho nesta
      escala — errou, e para que lado.

      **Medido em 23/09/2026**, `git diff --numstat` mais `wc -l` nos criados.

      | dimensão | projetado (D7) | entregue | erro |
      |---|---|---|---|
      | produção — lógica | 1 palavra, 1 linha modificada | **1 / 1** | **exato** |
      | produção — comentário | 0 linhas | **0** | **exato** |
      | spec delta | ~30 linhas | **87** no arquivo, **~23** escritas | ver abaixo |
      | `02-HISTORICO_E_STATUS.md` | ~110 linhas | **231** (+2 removidas) | **+110%** |
      | `CHANGELOG.md` | ~12 linhas | **10** (+1 removida) | −17% |
      | artefatos OpenSpec | ~500 linhas | **962** | **+92%** |

      **As duas dimensões de produção acertaram exato, e isso não é mérito:** com
      uma palavra, não havia o que errar. A régua não se testa aí.

      **O `02` errou +110%, e a causa tem duas metades, uma prevista e outra
      não.** Decomposto: entrada de histórico **173**, seção "Abertos por" **33**,
      atualização do item pré-existente da lista mantida à mão **25**.

      - **A metade prevista, e mesmo assim não contada:** as 25 linhas do item
        pré-existente são **modificação em seção de outra change**, e a projeção
        só enumerou o que esta change **cria** (uma entrada, uma seção). É
        exatamente a causa estrutural que a convenção 18 nomeia — *projeção conta
        arquivos criados, não modificados* —, aqui na forma de **seção** em vez de
        arquivo. Ler a convenção não impediu de repetir o erro, o que sugere que a
        pergunta útil não é "quantos arquivos modifico" e sim **"que registro
        pré-existente esta change torna desatualizado"**.
      - **A metade não prevista, e é a dominante:** a entrada de histórico sozinha
        (173) já passou a projeção inteira (110). A causa é a que a própria
        convenção declara não-projetável: **registro cresce com quantos achados a
        change produz.** A D7 contou os **três** achados que existiam quando o
        `design.md` fechou; a aplicação produziu **mais dois** (o método de
        conferência existir em dois lugares e falhar nos dois; o SIGPIPE do
        harness), e cada um custou parágrafo próprio.

      **Terceira confirmação da mesma propriedade**, junto das duas já
      registradas, e a primeira em que ela é o **termo dominante** do erro — não
      um acréscimo sobre uma base de código. **Item para a convenção 18:** em
      change cuja produção é de uma linha, projetar registro pelo número de
      achados **conhecidos** erra por construção, porque a verificação e a
      aplicação ainda vão produzir achados. O que a projeção pode fazer é
      declarar a contagem no momento em que projeta — *"três achados, ~110
      linhas"* — para que o erro seja lido como *"saíram cinco"* em vez de
      *"projetou mal"*.

      **Nota sobre o próprio número, e ela não é preciosismo:** as 231 linhas do
      `02` foram medidas **antes** de esta conferência ser escrita lá. Com ela, o
      arquivo fecha em **276**. O número da tabela continua sendo o certo para a
      comparação — é o registro que a change produziu **até** o momento de
      projetar contra entregar —, mas registrar só ele deixaria o `git diff` final
      contradizendo a tabela sem explicação. **Registro que mede a si mesmo tem de
      dizer em que instante se mediu**, que é a convenção 22 na sua forma mais
      literal.

      **O delta de spec merece nota de unidade, e é a convenção 22 aplicada à
      própria régua.** Projetado ~30, entregue 87 **no arquivo** e ~23
      **escritas**: um `MODIFIED` exige copiar o requirement inteiro, então o
      tamanho do arquivo é dominado por cópia obrigatória e **não** mede trabalho.
      Os dois números estão certos sobre perguntas diferentes, e a projeção não
      disse qual respondia. **Projeção de delta de spec declara a unidade —
      linhas escritas ou linhas do arquivo — ou não é comparável.**

      **E a previsão qualitativa da D7 — "registro decide o tamanho, ~80% das
      linhas fora dos artefatos OpenSpec" — acertou a direção e é ambígua na
      magnitude, pelo mesmo motivo:** `02` + `CHANGELOG` = 241 linhas contra 329
      (**73%**) contando o delta pelo arquivo, ou contra 265 (**91%**) contando-o
      por linhas escritas. O valor projetado cai entre os dois. **Régua citada sem
      escopo não é régua** — inclusive quando a régua é da própria projeção.
