Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-268.sh` reintroduz o defeito nomeado. A mutação toca só o trecho nomeado (uma linha do `nginx.conf` ou uma entrada da lista de exceções) e nunca restaura um diretório ao `HEAD`;
2. o teste do app roda e reprova, **nomeando** o prefixo, a exceção ou o bloco;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra:
- o nome do teste;
- a mensagem da reprovação (o trecho que nomeia o item);
- o "voltou: <arquivo>", com o sha igual ao de antes.

Uma mutação que quebra a compilação, que reprova por "no tests", ou que reprova sem nomear o item não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/268-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-268.sh`, **uma por vez, com o lock** (`suites.lock`; o guarda e a conferência da 5 usam o mesmo).
- Suítes: `api`, `inbox`, `connectors` (o teste novo de cada um) e `frontend` (o `nginxHeaders.test.ts` lê o mesmo `nginx.conf`), com o RabbitMQ de pé (`PAUSAR_RABBIT=0`).
- Fora: `workers` (sem rota HTTP), `rt-inbox`, `rt-connectors` e `rt-crossapp` (nenhum código de produção muda, e os acordos não passam pelo nginx).
- Sob limite térmico, vale a variante `MAXWORKERS=4` na `frontend`, registrada no `-estado.txt`.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim; senão é inconclusiva e se repete. Reprovação por caso que esta change mudou não é inconclusiva: registra-se "reprovada pelo X (a → b), corrigida".
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo do limite do script (8; 6 na `frontend`).
- Nunca `podman build` nem `dotnet build` junto com uma suíte.

**A baseline** está em `~/.cache/buteco-agents/268-baseline/`, no worktree `../buteco-agentes-268-baseline`, em `8013521`, com a árvore limpa, todas `APROVADA` (10/11/2026, logo depois de um reboot, com o Spotlight reindexando):
- `api`: 1685 aprovados + 1 ignorado de 1686, em 342 s, CPU mínima 65;
- `inbox`: 361 de 361, em 58 s, CPU mínima 100;
- `connectors`: 338 aprovados + 1 ignorado de 339, em 19 s, CPU mínima 100;
- `frontend`: 2628 casos em 199 arquivos, em 159 s, CPU mínima 67.

A comparação final é por classe + nome (`.trx`) e por arquivo + nome (`frontend.json`). As durações do `api` e do `inbox` só servem de referência para o custo do Postgres a mais com a CPU em 100; a do `api` foi medida com a CPU caindo a 65.

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura (por exemplo: um primeiro segmento servido que não é exceção nem está no nginx, além de `applications`; ou um prefixo do nginx que nenhum app serve);
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

## Casos que saem ou mudam de asserção

Nenhum. Os casos são todos novos. O `nginxHeaders.test.ts` continua com as asserções de hoje: a regex muda numa linha que ele não lê, e o comentário do topo fica fora dos dois blocos de cabeçalho.

**Mudam de arranjo, sem mudar asserção nem nome** (design D2, a fixture): `RouteDeclarationValidationTests` (`apps/api`, 8 casos) e `RoleMatrixTests` (`apps/inbox`) trocam o `IClassFixture` pelo `[Collection]` da `ApiHostCompositionCollection` e da `InboxHostCompositionCollection`, que dividem a fixture com o `NginxRouteCoverageTests` (2.1 e 4.1).

## 0. Preparação

- [x] 0.1 (repo) Issue #268 (`bug`, `tipo: bug`, `app: frontend`, milestone "Entrega M2 — Diferentes acessos") em `In progress` no board; worktrees `../buteco-agentes-268` (`fix/268-nginx-applications`) e `../buteco-agentes-268-baseline` (detached), os dois em `8013521`.
- [x] 0.2 (scripts locais) `~/.cache/buteco-agents/bin/suite-268.sh`, cópia do `suite-188.sh` (só o cabeçalho muda).
- [x] 0.3 (baseline) `suite-268.sh` sobre o worktree da baseline: `api inbox connectors frontend`, com a saída completa em `~/.cache/buteco-agents/268-baseline/`.
- [x] 0.4 (scripts locais) `~/.cache/buteco-agents/bin/guarda-268.sh`, cópia do `guarda-187.sh` com os projetos `api`, `inbox` e `connectors` (os dois últimos com build). Conferir com:
  - uma mutação nula, recusada com "defeito não aplicado";
  - uma mutação real no `nginx.conf`, que volta com o sha igual;
  - uma segunda chamada com o lock tomado por uma suíte, recusada com 75.

## 1. O leitor do `nginx.conf` (tests/Shared, design D3)

- [x] 1.1 (tests/Shared) `tests/Shared/NginxRouting.cs`: acha o `nginx.conf` subindo de `AppContext.BaseDirectory` até a pasta com `apps/frontend/deploy/nginx.conf` (não achar é falha, com o caminho de partida, nunca "pulado"); tira os comentários; separa os `location` contando chaves; devolve, por bloco, o modificador, o padrão, os prefixos (padrão `^/(a|b|…)(/|$)`), o host do upstream (`set $<x>_upstream <host>:8080`) e se tem o desvio de navegação. Um bloco de regex que encaminha e não tem a forma `^/(…)(/|$)` faz o leitor falhar, nomeando o padrão. O comentário do arquivo diz por que ele mora em `tests/Shared/` e é linkado.
- [x] 1.2 (apps/api, apps/inbox, apps/connectors) A linha `<Compile Include="…\tests\Shared\NginxRouting.cs" Link="Support\NginxRouting.cs" />` nos três `.csproj` de teste. Os três compilam (`dotnet build` de cada projeto, um por vez, sem suíte rodando).
- [x] 1.3 (apps/api) Casos do leitor, sobre um texto de `nginx.conf` montado no teste (só aqui o texto é forjado, porque o caso é o do leitor, não o do acordo): bloco com `if` aninhado; comentário com `{`; bloco exato; bloco sem upstream (`location /`); a forma desconhecida, que falha nomeando o padrão. Moram no projeto do `apps/api` só para não rodar três vezes.

## 2. O teste de cobertura do `apps/api` antes da correção (design D2, D4, D6)

- [x] 2.1 (apps/api) `NginxRouteCoverageTests` com o `ApiFactoryFixture`: os primeiros segmentos dos `RouteEndpoint` da `EndpointDataSource` comparados com os prefixos dos blocos de regex de upstream `api`, nos dois sentidos; as exceções nomeadas no próprio teste, cada uma com o motivo, escritas **à parte** a partir da spec (convenção 15, sexta forma): `health` e `sync` (servidos, sem bloco); `= /.well-known/openid-configuration`, `= /.well-known/jwks`, `= /connect/authorize`, `= /connect/logout` (blocos exatos). Mais as checagens do D4: exceção que deixou de valer, exceção nos dois lados, bloco exato fora da lista, toda URI de `IdentityIssuerEndpoints.Expected` alcançando o `apps/api`, desvio de navegação em todo bloco de regex do app e em nenhum bloco exato da lista, prefixo repetido em dois blocos, upstream desconhecido, primeiro segmento não literal. Todas as divergências numa lista, uma falha só.
  Feito: a fixture vem da collection `ApiHostCompositionCollection`, e não de um `IClassFixture` próprio (D2).
- [x] 2.2 **(guarda)** "Prefixo servido sem bloco", contra o **defeito real**: o teste da 2.1 roda na árvore com o `nginx.conf` do `main` (sem `applications`) e reprova nomeando `applications`, e só ele. A rodada é com o `guarda-268.sh` sem mutação (filtro na classe) e fica registrada antes da 3.1. Se reprovar por outro item, parar: é premissa falsa da tabela do design.
  Feito (`268-guardas/2.2/defeito-real.log`): reprovou com uma linha só, `prefixo servido pelo apps/api sem bloco no nginx: 'applications' (cai no fallback do SPA, 200 com HTML).`; os 18 outros casos do filtro (leitor e `RouteDeclarationValidationTests`) aprovados.

## 3. A correção (apps/frontend, design D1)

- [x] 3.1 (apps/frontend) `applications` na regex do bloco do `apps/api`, depois de `identity`, no bloco que já tem o desvio de navegação.
- [x] 3.2 (apps/api) O teste da 2.1 aprova com a correção, pelo `guarda-268.sh` sem mutação.
  Feito: 19/19 (`268-guardas/3.2-depois-da-correcao.log`).
- [x] 3.3 (apps/frontend) O comentário do topo: o "Para reconferir" passa a citar os três `NginxRouteCoverageTests` como o guarda; os `grep` e o parágrafo do ruído conhecido ficam, como explicação de onde os prefixos vêm; as exceções passam a citar a lista do teste. O `nginxHeaders.test.ts` continua aprovado (só esse arquivo, pelo `npx vitest run`).
  Feito: `nginxHeaders.test.ts` 5/5.

## 4. Os testes do `apps/inbox` e do `apps/connectors`, e os guardas (design D2, D4)

- [x] 4.1 (apps/inbox) `NginxRouteCoverageTests` com o `InboxFactoryFixture`, upstream `inbox`; exceção `health`. As checagens comuns da 2.1 que valem para o app.
- [x] 4.2 (apps/connectors) `NginxRouteCoverageTests` com a `ConnectorsFactory` construída no teste (como o `RoutePatternTests`), upstream `connectors`; exceção `health`.
- [x] 4.3 **(guarda)** "Prefixo do nginx que o app não serve": `ghost` acrescentado à regex do bloco do `apps/api` → o teste do `apps/api` reprova nomeando `ghost`. Repetir no bloco do `apps/connectors`, com o teste do `apps/connectors`.
- [x] 4.4 **(guarda)** "Tirar uma exceção da lista reprova": cada mutação em separado, na lista do teste:
  - (a) `sync` fora da lista do `apps/api` → reprova nomeando `sync`;
  - (b) `= /.well-known/jwks` fora da lista do `apps/api` → reprova nomeando o bloco;
  - (c) `health` fora da lista do `apps/inbox` → reprova nomeando `health`.
- [x] 4.5 **(guarda)** "`/sync` exposto por engano": `sync` acrescentado à regex do bloco do `apps/api` → reprova nomeando `sync` como exceção nos dois lados.
- [x] 4.6 **(guarda)** "Prefixo do app sem bloco", no `apps/inbox`: `contacts` tirado da regex do bloco do `apps/inbox` → o teste do `apps/inbox` reprova nomeando `contacts`.
- [x] 4.7 **(guarda)** "Bloco sem o desvio de navegação": o `if ($http_sec_fetch_mode = navigate)` tirado do bloco do `apps/connectors` → o teste do `apps/connectors` reprova nomeando o bloco.
- [x] 4.8 **(guarda)** "Prefixo em dois blocos": `agents` acrescentado à regex do `apps/inbox` → o teste do `apps/api` reprova nomeando `agents` como repetido (e o do `apps/inbox`, como não servido; os dois registrados).
- [x] 4.9 **(guarda)** "Bloco exato fora da lista": um `location = /connect/introspect` encaminhando ao `apps/api` → reprova nomeando o bloco.
- [x] 4.10 **(guarda)** "Upstream desconhecido": o `set $connectors_upstream connectors:8080` trocado por `conectores:8080` → o teste do `apps/connectors` reprova nomeando o upstream.
- [x] 4.11 **(guarda)** "Forma que o leitor não entende falha alto": o padrão do bloco do `apps/inbox` trocado por `^/(channels|contacts)/` (sem o `(/|$)`) → o teste do `apps/inbox` reprova nomeando o padrão, e não fica verde por ler zero prefixos.
  Os 13 guardas da seção 4 reprovaram, cada um nomeando o item, e cada arquivo voltou com o sha de antes (`268-guardas/4/resumo.txt`). A 4.11 usou `^/(channels|contacts|sessions|messages|webhooks|internal)/` (todos os prefixos, sem o `(/|$)`).

## 5. Conferência com o nginx de verdade (apps/frontend, design D5)

Sem suíte rodando: o script da conferência toma o mesmo lock. A troca do `resolver` dentro do contêiner (o contorno da #212) fica registrada na saída.

- [x] 5.1 (apps/frontend) `podman build` do `apps/frontend/Dockerfile` na árvore da change e na da baseline; `nginx -t` dentro das duas imagens, com a saída guardada em `~/.cache/buteco-agents/268-conferencia/`.
- [x] 5.2 (apps/api) O `apps/api` local (`dotnet run`, `TZ=America/Sao_Paulo`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1`, o Postgres do compose de dev) e a imagem do frontend numa rede do Podman, com o contêiner `api` encaminhando a porta `8080` ao host. Token de um Admin do banco local, pelo caminho da conferência da #188.
- [x] 5.3 (apps/frontend) Os `curl` contra a imagem da change, com status, `Content-Type` e o começo do corpo guardados:
  - `GET /applications/` com o token, sem `Sec-Fetch-Mode` → `200`, `application/json`, o corpo do `apps/api`;
  - `GET /applications/` sem token → `401` do `apps/api`;
  - a mesma URL com `Sec-Fetch-Mode: navigate` → `200`, `text/html`, `Cache-Control: no-store`, o `index.html`;
  - `GET /applicationsxyz` → o `index.html` do fallback.
- [x] 5.4 (apps/frontend) O contraste: a imagem da baseline responde ao `GET /applications/` com token `200` e `text/html`, o defeito da homologação.
  Feito (`268-conferencia/curl-resumo.txt`): a imagem da change responde `200 application/json` (`[]`) com token, `401` sem token, e o `index.html` com `no-store` na navegação e em `/applicationsxyz`; a da baseline responde `200 text/html` ao pedido com token e ao sem token. Banco descartável, contêineres, rede e imagens apagados; `postgres_1` parado de novo; RabbitMQ parado o tempo todo (o `api` só conecta ao publicar, e nada publicaria na fila de dev).

## 6. Documentação

- [x] 6.1 `docs/deployment.md`: a nota da #268 (só a imagem do `frontend`; pode ir sozinha depois do PR #267; substitui a correção manual da homologação; recuo: a imagem anterior, com o defeito de volta).
- [x] 6.2 `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`, **antes do archive** e dentro do PR. O 02 traz a baseline e a rodada final (com a duração das suítes do `apps/api` e do `apps/inbox` contra a baseline, o custo do Postgres a mais), os guardas com a reprovação vista e a conferência da 5.

## 7. Fechamento

- [x] 7.1 Rodadas `api`, `inbox`, `connectors` e `frontend` pelo `suite-268.sh`, uma por vez, com a árvore da change. Comparação por classe + nome com a baseline: nenhum caso some nem muda de resultado; os novos são só os da 1.3, 2.1, 4.1 e 4.2, listados.
  Feito (`268-fechamento/`): `api` 1696 + 1 ignorado (CPU mín. 55), `inbox` 362 (100), `connectors` 339 + 1 ignorado (100), `frontend` 2628 em 199 arquivos (62), todas `APROVADA`. Nenhum caso de antes sem par (nome, resultado); novos só os da 1.3 (10), 2.1, 4.1 e 4.2.
- [x] 7.2 `openspec validate nginx-prefixo-applications --strict`.
- [x] 7.3 Nenhum código de produção dos três apps no diff (`git diff --stat` só com `apps/frontend/deploy/nginx.conf`, `tests/Shared/`, os projetos de teste, `openspec/` e os documentos).
  Feito: o diff toca só `apps/frontend/deploy/nginx.conf`, `tests/Shared/`, os três projetos de teste, `openspec/`, `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md` e `docs/deployment.md`.
