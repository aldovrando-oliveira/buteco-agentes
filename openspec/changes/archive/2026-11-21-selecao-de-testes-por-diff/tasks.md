**O que esta change toca:** só `scripts/` (o script novo e os testes dele) e documentação. Nenhum arquivo de `apps/`, `libs/` nem `tests/` muda. Nenhum teste existente muda.

**As suítes** só rodam na medição (seção 5) e na conferência 2.6, pelo `~/.cache/buteco-agents/bin/suite-307.sh` (tarefa 0.1), **uma por vez, com o lock** (`suites.lock`):

- vale a regra assimétrica: verde conta mesmo com a CPU caindo; reprovação só conta com a CPU em 100 do início ao fim, senão é inconclusiva e se repete;
- antes de cada passo, o load de 1 e de 5 min abaixo do limite do script (6 no frontend, 8 no .NET) e o `CPU_Speed_Limit` em 100; a VM do Podman com o relógio acertado. Nunca `dotnet build` junto de uma suíte;
- toda rodada do `apps/api` registra as threads efetivas (do `.trx`), o veredito, a duração e a CPU mínima (convenção 19, parágrafo da #302).

**Os testes do script** (`python3 -B -m unittest discover -s scripts/tests`) não sobem contêiner e não tomam o lock.

**Parada** (convenção 9 e critério de parada por premissa falsa):

- **parar** se um dos 13 fixes da amostra (2.3) não for coberto pela seleção calculada. É a premissa da change;
- **parar** se um guarda (2.2, 2.4, 2.5, 2.7) passar verde contra o defeito que existe para pegar;
- **parar e voltar ao dono** se a medição (seção 5) mostrar a seleção do frontend, numa folha, acima de ~50% do Vitest inteiro (na proposta: 46–49 s contra 145–147 s, ~1/3). O ganho que justifica a change deixaria de existir;
- **seguir e registrar** quando a mudança for só de texto da documentação ou do formato de saída, sem mudar o que é selecionado.

**Nada de commit** até o dono pedir. Um commit só, depois do archive.

## 0. Preparação

- [x] 0.1 (scripts locais) `suite-307.sh`, cópia do `suite-303.sh`, com um modo `plano`: lê as linhas de `selecionar-testes.py --passos` (id, diretório, comando) e roda cada passo como uma suíte. O lock, a espera de load e CPU, a amostragem, o veredito e o `-estado.txt` são os de hoje, um por passo; o comando vem do plano, e não de um `case` do script. Os passos `.NET` do `apps/api` levam a linha das threads. O cabeçalho cita a regra: o script do repositório planeja e este executa (design, D1).
  **Feito:** `suite-307.sh` com o modo `plano` (`suite-307.sh <worktree> <saida> plano <arquivo-de-passos>`): lock esperado, load < 6 nos passos `fe-*` e < 8 nos outros, relógio da VM, CPU a cada 5 s, veredito assimétrico, `.trx` acrescentado a todo `dotnet test`, threads lidas do `.trx` nos passos do `Buteco.Api.Tests`, e um `resumo.txt` por plano. A primeira versão lia o plano pela entrada padrão, e o `podman machine ssh` de dentro do laço a consumia: rodou só o primeiro passo. Corrigido (leitura pelo descritor 3) e conferido no plano da 4.1.
- [x] 0.2 (repo) Conferir que o worktree desta change está no `origin/main` do início (`5c21c89` ou o merge seguinte) e registrar o SHA. **Sem baseline de suíte:** a change não toca nenhum app nem teste. A referência da suíte inteira é a do `02` para o `apps/api` (#303) e a medição do design para o frontend (D6), e a seção 5 mede a seleção no mesmo `main`.
  **Feito:** worktree em `5c21c89` (merge do PR #305), igual ao `origin/main` local. Sem baseline de suíte.

## 1. O script (scripts/, repo)

- [x] 1.1 `scripts/selecionar-testes.py`, só biblioteca padrão. **Os caminhos tocados** (design, D3):
  - o diff do `merge-base` entre `origin/main` (ou `--base`) e `HEAD` até a árvore de trabalho (`git diff --name-status -M`), mais `git ls-files --others --exclude-standard`;
  - renomeação conta os dois caminhos; arquivo apagado conta como tocado;
  - `--caminhos <arquivo>|-` substitui o diff por uma lista dada (os testes e a medição usam);
  - o plano registra a base (o SHA do `merge-base`) e se ela veio de `origin/main` sem `fetch` (o script não faz `fetch`).
  **Feito.**
- [x] 1.2 **O grafo** (design, D5): os `.csproj` versionados e os novos, lidos na hora. Arestas: `ProjectReference` (caminho relativo com `\` normalizado) e `Compile Include … Link` (o arquivo linkado de fora do projeto). O fecho é transitivo. **Projeto de teste** é o `.csproj` com `PackageReference` do `Microsoft.NET.Test.Sdk`, e não o nome `*.Tests`.
  **Feito:** 8 projetos de teste achados pelo SDK, os mesmos 8 da leitura do design.
- [x] 1.3 **As regras**, na ordem da tabela da D4, primeira que casa, mais os acréscimos A1 e A2, que valem além da classificação. Caminho que não casa com nenhuma linha: **tudo**. O `check-docs.py` e os testes do script entram em todo plano.
  **Feito.** **E uma regra a mais, achada no apply** (design, "O que o apply mudou"): na linha 6, as **varreduras de `src/`**, os testes do frontend que leem `src/` inteiro por `readdirSync` (`forbiddenPages`, `surfaceTokens`, `passwordFieldGuard`), descobertas na hora do plano. O `related` não as alcança.
- [x] 1.4 **Tudo**, por descoberta: cada projeto de teste da 1.2, o frontend (`tsc -b`, lint e Vitest inteiro) e os testes do próprio script. Nenhuma lista de projetos escrita no script.
  **Feito.**
- [x] 1.5 **Saída** (design, D8): o texto (padrão), com a base, os caminhos agrupados pela regra que os pegou e cada passo com o motivo; e `--passos`, uma linha por passo (`id`, diretório, comando), na ordem fixa da D8. Passo filtrado some quando a suíte inteira do mesmo projeto já está no plano. `--passos` não imprime mais nada na saída padrão.
  **Feito.** Os passos de arquivo do frontend (`fe-nginx`, `fe-varreduras`) também somem quando o Vitest inteiro está no plano.
- [x] 1.6 Repositório sem histórico suficiente (clone raso, `origin/main` ausente): o script **falha nomeando o motivo**, e nunca devolve plano vazio.
  **Feito:** base inexistente → saída 2, "a base 'origin/nao-existe' não existe neste clone (falta fetch?)"; clone raso com a base sem ancestral comum → saída 2, "sem merge-base … (clone raso? histórico cortado?)". Num clone raso com o merge-base alcançável, o plano sai normal.

## 2. Os testes e os guardas (scripts/tests/, repo)

Cada guarda só conta depois de reprovar contra o defeito que existe para pegar (convenção 15). A mutação é feita numa cópia do script, nunca no arquivo versionado. A volta é pela cópia anterior, com o `sha256` conferido; saídas em `~/.cache/buteco-agents/307-guardas/<tarefa>/`.

- [x] 2.1 `scripts/tests/selecao-esperada.md`: a tabela à parte, **caminho concreto tocado → passos esperados**, escrita a partir da D4 e não da saída do script. Uma linha por regra, e pelo menos: uma folha e um arquivo de cada núcleo do frontend; um arquivo apagado do frontend; `apps/frontend/deploy/nginx.conf`; `apps/frontend/package.json`; um `.csproj` de cada app; `src/` e `tests/` de cada app .NET; cada projeto de `tests/`; `tests/Shared/`; `libs/`; `Directory.Build.props`; `global.json`; um app novo; `docs/`, `openspec/`, `.claude/`, um `.md` da raiz; um `.csproj` de `tests/<P>` (a linha 3 com o acréscimo A1); um `.md` **dentro** de `apps/connectors/tests/.../Fixtures/` (não é documentação); `scripts/`. `test_selecionar_testes.py` lê a tabela e confere o plano de cada linha.
  **Feito:** 48 linhas, com os conjuntos `TUDO`, `FE-FOLHA` e `FE-INTEIRO` escritos por extenso, quatro linhas de caminhos combinados e o `.csproj` de `tests/<P>` (a linha 3 com o A1).
- [x] 2.2 **(guarda) A tabela pega regra errada.** Quatro mutações, uma por rodada: o inbox sem a própria suíte; `apps/frontend/deploy/**` caindo na regra do frontend comum; `src/auth/` fora do núcleo; o acréscimo A1 virando uma linha da classificação depois da 2 (o defeito apontado na revisão: nunca dispara). Cada uma reprova nomeando a linha da tabela.
  **Feito, as quatro reprovaram, mais uma (2.2e)** (`~/.cache/buteco-agents/307-guardas/`, `resumo.txt`; a mutação vive na cópia, apontada por `SELECIONAR_TESTES_SCRIPT`):
  - 2.2a, inbox sem a própria suíte: reprovou em `apps/inbox/src/…/Program.cs`, no `.csproj` do inbox e na linha combinada com `docs/`;
  - 2.2b, `deploy/**` como frontend comum: reprovou em `nginx.conf` (sozinho e combinado) e na ordem fixa;
  - 2.2c, `src/auth/` fora do núcleo: reprovou em `src/auth/session.ts` (sozinho e combinado);
  - 2.2d, A1 como linha depois da 2: reprovou nos três `.csproj` que só o acréscimo alcança (workers, inbox, `tests/CrossApp…`);
  - 2.2e, a folha sem as varreduras: reprovou nas linhas `FE-FOLHA`.
- [x] 2.3 `scripts/tests/fixes-da-amostra.md`: os 13 fixes da seção 9.1 do estudo (lista no design, D7), com o projeto e a classe do teste que pegou cada um. O teste calcula a seleção do **diff de produção** do fix (o diff do commit sem os arquivos de teste, o mesmo critério do estudo) e confere que o projeto do teste está nela. Para o `b4fcf5d`, o passo filtrado `Nginx` do `apps/api` inclui a classe `NginxRouteCoverageTests`. Nos fixes do frontend, que o passo `related` inclui o arquivo do fix.
  **Feito:** os 13, com o diff de produção; o teste falha alto se um commit não estiver no clone.
- [x] 2.4 **(guarda) Os fixes pegam regra que falta.** Duas mutações: sem a regra do `nginx.conf` (o `b4fcf5d` reprova); sem o grafo de `tests/` (o `bfa1ec0` reprova, porque o teste que o pegou é do `CrossAppTaskStoreCompatibility.Tests`).
  **Feito, as duas reprovaram:** sem a regra do nginx, `b4fcf5d: Buteco.Api.Tests[Nginx] fora do plano ['check-docs', 'scripts-testes']`; sem o grafo, `bfa1ec0: CrossAppTaskStoreCompatibility.Tests fora do plano ['Buteco.Workers.Tests', …]`. Nenhum outro fix reprovou nas duas.
- [x] 2.5 **(guarda) Projeto de teste sem regra.** O teste descobre os projetos de teste pelo conteúdo (1.2) e confere: cada um está em "tudo", e tocar um arquivo dentro dele o seleciona. Mutação: a descoberta da cópia do script passa a filtrar pelo nome `*.Tests.csproj`, com um projeto de teste falso, não versionado, `tests/Novo.Contrato/Novo.Contrato.csproj`, criado na hora e apagado depois. Reprova nomeando o projeto.
  **Feito, reprovou:** `'Novo.Contrato' not found` em "tudo". O controle, com o script correto e o mesmo projeto falso, passou verde. O projeto falso foi apagado depois (conferido).
- [x] 2.6 **(conferência, apps/frontend) O `related` alcança o teste dos fixes do frontend.** Para cada um dos fixes de folha do frontend da amostra, `vitest list --filesOnly --staticParse --changed HEAD` com o arquivo do fix tocado (uma linha de comentário, desfeita depois pela cópia) lista o arquivo de teste que pegou o defeito. Só lista, não roda caso nenhum. Registrar as cinco listas.
  **Feito (`307-guardas/2.6/`):** são **4** fixes de folha, e não 5: o quinto do frontend, `b4fcf5d`, é a linha 4. Com os arquivos de cada fix tocados, a lista do `vitest list` traz o teste que pegou o defeito nos 4 (`bfb114b`: 11 arquivos, com `DeclaredGap.test.tsx`; `39c43d5`: 7, com `AgentConsumptionCard.test.tsx`; `6635db2`: 12, com `ApplicationsListPage.test.tsx`; `7fd8e75`: 7, com `AgentTable.test.tsx`). A árvore do frontend voltou limpa.
- [x] 2.7 **(guarda) Leitura cruzada sem regra** (design, D7, G4). O teste varre as fontes de teste por leitura de arquivo do repositório fora do próprio projeto: a subida de pasta até um arquivo de outra árvore, `readFileSync` de fora de `src/`, e caminho literal começando por `apps/` ou nome de arquivo da raiz. Cada achado tem de estar na lista de artefatos lidos por outra pilha da D4. Mutação: o `ProductVersionCoherenceTests` fora da lista reprova nomeando o arquivo de teste.
  **Feito, reprovou:** `leitura de arquivo de fora do projeto sem regra na seleção (design, D4): apps/api/tests/Buteco.Api.Tests/ProductVersionCoherenceTests.cs`. A heurística acha 8 leitores hoje: os 3 da D4, as 3 varreduras e os 2 que leem o próprio componente.
- [x] 2.8 O mesmo plano duas vezes: `--passos` sobre o mesmo diff dá a mesma saída, byte a byte.
  **Feito.**
- [x] 2.9 `python3 -B -m unittest discover -s scripts/tests` verde, com o tempo registrado. **Parada:** acima de ~10 s, parar e avisar o dono (os testes entram em todo plano e no checklist antes do PR).
  **Feito: 12 testes em 1,6–1,8 s**, abaixo da parada de ~10 s. O comando leva `-B`: sem ele, o `unittest` deixa `__pycache__/` em `scripts/`, que o `.gitignore` não cobre, e o plano o vê como arquivo novo. O teste também liga `sys.dont_write_bytecode`.

## 3. Documentação

- [x] 3.1 (docs) `docs/development.md`, "Rodar os testes", subseção "Rodadas intermediárias: a seleção pelo diff":
  - para que serve, e que a baseline e o fechamento continuam completos;
  - como pedir o plano e como rodar os passos (um por vez, sem duas suítes .NET juntas);
  - a tabela das regras da D4, com o motivo de cada artefato lido por outra pilha;
  - os limites da D10: o `related` segue imports; contrato copiado à mão não aparece.
  **Feito:** subseção "Rodadas intermediárias: a seleção pelo diff".
- [x] 3.2 (docs) `CONTRIBUTING.md` §1: um parágrafo. Durante a mudança, a seleção pelo diff (link para a 3.1); antes do PR, a suíte de cada app afetado, como hoje. E os testes do script no checklist antes do PR, junto do `check-docs.py`, porque o G3 e o G4 acusam mudanças em `apps/` e `tests/`.
  **Feito:** o comando no bloco do §1, o parágrafo dos testes do script e o da seleção. Também no checklist do `.github/PULL_REQUEST_TEMPLATE.md`, que espelha o §1.
- [x] 3.3 (docs) `01-ARQUITETURA_E_CONVENCOES.md`, convenção 19: um parágrafo junto do da #302. As rodadas intermediárias de uma change usam o plano do script, e a tarefa delas no `tasks.md` cita o plano (a saída de `--passos`) no registro da rodada. A baseline e o fechamento continuam completos. O que a seleção não rodou, o fechamento roda.
  **Feito:** o parágrafo "E as rodadas do meio rodam o que o diff seleciona, e dizem o que foi isso", logo depois do da #302.
- [x] 3.4 (docs) `python3 scripts/check-docs.py` sem erro.
  **Feito:** `Integridade da documentação: OK`.

## 4. A seleção no plano real (repo)

- [x] 4.1 O plano desta própria branch (só `scripts/` e docs): os testes do script e o `check-docs.py`, nada de suíte de app. Registrar a saída.
  **Feito (`~/.cache/buteco-agents/307/plano-da-branch/`):** 13 caminhos, 9 na linha 1 e 4 na linha 8; 2 passos, `check-docs` e `scripts-testes`, executados pelo `suite-307.sh plano`: 1 s e 2 s, aprovados.

## 5. Medição: tempo da seleção por tipo de mudança, no `main` atual

Cada tipo é uma lista de caminhos dada (`--caminhos`), executada pelo `suite-307.sh plano` (0.1), uma rodada por tipo, sobre o SHA da 0.2. Registrar, por passo e no total: a duração, a CPU mínima e o veredito, com o `uptime` e o `podman ps` ao lado. Saídas em `~/.cache/buteco-agents/307/medicao/`.

- [x] 5.1 (apps/frontend) **Folha:** `src/features/agents/components/AgentTable.tsx`.
  **Feito:** 59 s de passos (69 s de parede), CPU 100 em todos.
- [x] 5.2 (apps/frontend) **Núcleo:** `src/auth/session.ts` (Vitest inteiro, pela regra).
  **Feito:** 311 s (Vitest inteiro 277 s, CPU mín. 34).
- [x] 5.3 (apps/inbox) **Só inbox:** `apps/inbox/src/Buteco.Inbox/Program.cs` (inbox e `InboxOrchestratorRoundTrip.Tests`).
  **Feito:** 125 s (inbox 54 s, `rt-inbox` 67 s, CPU mín. 32).
- [x] 5.4 (apps/workers) **Só workers:** `apps/workers/src/Buteco.Workers/Program.cs` (workers, `CrossAppTaskStoreCompatibility.Tests` e `InboxOrchestratorRoundTrip.Tests`).
  **Feito:** 505 s (workers 412 s, CPU mín. 25).
- [x] 5.5 (apps/api) **Só api:** `apps/api/src/Buteco.Api/Program.cs` (api inteiro e os três de `tests/`), com a linha das threads.
  **Feito:** 961 s (api 825 s, `[6 threads]`, 1860 + 1 ignorado, CPU mín. 25). **Passa do gatilho 2 da #302** (rodada válida acima de ~600 s): registrado no item em aberto do `02`, para o dono.
- [x] 5.6 A tabela no design ("O que o apply mudou") e no `02`: tipo, passos, tempo da seleção e a referência da suíte inteira do mesmo app, com a fonte e o estado de cada número. **Parada** pela regra do topo se a folha do frontend passar de ~50% do Vitest inteiro.
  **Feito:** a tabela em "O que o apply mudou" do design e na entrada do `02`. Sem parada: a folha ficou em 19–33% dos mesmos passos com o Vitest inteiro.

## 6. Fechamento

- [x] 6.1 `python3 -B -m unittest discover -s scripts/tests` e `python3 scripts/check-docs.py` verdes na árvore final.
  **Feito:** 12 testes em 1,8 s, OK; `Integridade da documentação: OK`.
- [x] 6.2 `openspec validate selecao-de-testes-por-diff --strict` passa. O resultado vai registrado.
  **Feito:** `Change 'selecao-de-testes-por-diff' is valid`.
- [x] 6.3 (docs) **Antes do archive:**
  - a entrada da change no `02-HISTORICO_E_STATUS.md`: o que o script decide, os guardas com as mutações, a conferência 2.6 e a tabela da 5.6;
  - nos itens em aberto do `02`, os limites da D10 e as observações fora de escopo do design;
  - o `CHANGELOG.md`: uma linha em `[Unreleased]`;
  - o design corrigido com o que o apply achou, em "O que o apply mudou" (convenção 9).
  **Feito:** a entrada no fim do `02`; nos itens em aberto, os limites da seleção e a ocorrência do gatilho da #302; a linha em `[Unreleased]` do `CHANGELOG`; "O que o apply mudou" no design.
- [x] 6.4 `/opsx:archive` com sync: o requisito novo entra em `openspec/specs/open-source-governance/spec.md`. Depois, o commit único e o PR, **quando o dono pedir** (convenção 24: archive antes do push; `Closes #307`).
  **Feito:** sync à mão do requisito ADDED (nada modificado nem removido) no fim de `openspec/specs/open-source-governance/spec.md`, e a change movida para `openspec/changes/archive/2026-11-21-selecao-de-testes-por-diff/`. O commit único e o PR, a pedido do dono (21/11/2026).
