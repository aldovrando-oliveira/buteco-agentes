**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-302.sh` (tarefa 0.1), **uma por vez, com o lock** (`suites.lock`; as rodadas filtradas e a conferência da 2.2 tomam o mesmo):

- suíte: só `api`. Fora: `inbox`, `connectors`, `workers`, `rt-*` e `frontend`. Nenhum código deles muda, e o paralelismo deles não é tocado;
- vale a regra assimétrica: verde conta mesmo com a CPU caindo; reprovação só conta com a CPU em 100 do início ao fim, senão é inconclusiva e se repete;
- **toda rodada da suíte do `api` registra**, no `-estado.txt`: as threads, pela linha `parallel test collections = on [N threads]` do log; o veredito; a duração; a CPU mínima (design, D4);
- antes de cada rodada, o load de 1 e de 5 min abaixo do limite do script, e a VM do Podman com o relógio e a memória conferidos. Nunca `dotnet build` junto de uma suíte.

**Parada** (convenção 9):

- **parar** se a linha da D3 não mostrar `[6 threads]` com o arquivo no lugar, ou se mostrar `[6 threads]` com o arquivo fora (o guarda da 2.2 não discrimina);
- **parar e voltar ao dono** se o fechamento (4.1) tiver uma rodada inconclusiva com 6 threads. É o gatilho 1 da D4 dentro da própria change, e a decisão volta a ser dele;
- **parar** se algum caso sair, mudar de nome ou reprovar de forma válida;
- **seguir e registrar** quando a conferência só mudar o texto do `docs/development.md` (a verbosidade, a precedência).

**Nada de commit** até o dono pedir. Um commit só, depois do archive.

**O que não muda:** nenhum arquivo de `apps/api/src`, nenhum teste, nenhum outro app, nenhum projeto de `tests/`.

## Casos que saem ou mudam de asserção (a lista do topo)

Nenhum. Os 1861 casos da suíte do `apps/api` (1860 + 1 ignorado, contagem do estudo sobre `c9733ce`) rodam com os mesmos nomes. A comparação da 4.2 confere por nome contra a baseline da 0.3, e um caso fora dessa igualdade é parada.

## 0. Preparação e baseline

- [x] 0.1 (scripts locais) `suite-302.sh`, cópia do `suite-296.sh`, com:
  - só a suíte `api`;
  - o grep da linha `[N threads]` do log para o `-estado.txt`;
  - um parâmetro `RUNSETTINGS` repassado depois de `--`, usado só na baseline (sem o arquivo, a linha precisa de `xUnit.DiagnosticMessages=true`).

  Mais o `teste-302.sh`, a rodada filtrada com o lock. O cabeçalho do `suite-302.sh` cita a regra do registro por rodada e onde ela está no `01` (3.3), para a cópia da próxima change levá-la (design, D4).
  **Feito:** `suite-302.sh` (cópia do `suite-296.sh`, só `api`; o lock é esperado em vez de recusado, porque outra sessão usa o mesmo; `RUNSETTINGS` e `LOGGER` por variável; as threads lidas do `.trx` e do log; o cabeçalho com a regra do registro por rodada e o gatilho), `teste-302.sh` e `build-302.sh`/`build-sln-302.sh` (builds com o lock).
- [x] 0.2 (repo) O worktree da baseline, `.claude/worktrees/testes-api-paralelismo-baseline`, destacado no `origin/main` do início da change, criado por `git worktree add --detach`, sem mexer na árvore da change nem na `main`.
  **Feito:** `testes-api-paralelismo-baseline`, destacado em `c9733ce`, sem `xunit.runner.json`.
- [x] 0.3 (apps/api) A baseline no worktree da 0.2: **uma** rodada da suíte `api` com o padrão do xUnit, em `~/.cache/buteco-agents/302/baseline/`, com `RUNSETTINGS="xUnit.DiagnosticMessages=true"`, e o relatório por nome (`nomes-trx.py`). Registrar a linha `[12 threads]`, o veredito, a duração e a CPU mínima. Se a rodada sair inconclusiva, ela **conta** para a série da D4 e se repete para ter a lista de nomes de uma rodada válida.
  **Feito:** `302/baseline/` (`c9733ce`, árvore limpa), com `RUNSETTINGS="xUnit.DiagnosticMessages=true"` e o console em `detailed`: **502 s, `[12 threads]`, CPU mín. 44, APROVADA**, 1860 aprovados + 1 ignorado de 1861; `nomes.txt` com 1861 linhas. Load de 1 min na largada abaixo de 8.

## 1. A configuração (apps/api, só testes)

- [x] 1.1 (apps/api) `apps/api/tests/Buteco.Api.Tests/xunit.runner.json`, com `$schema`, `maxParallelThreads: 6` e `diagnosticMessages: true` (design, D1 e D3).
  **Feito.**
- [x] 1.2 (apps/api) No `Buteco.Api.Tests.csproj`, o `<Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />`. Conferir que o arquivo chega a `bin/Debug/net10.0/`.
  **Feito:** o arquivo chega a `bin/Debug/net10.0/xunit.runner.json`.
- [x] 1.3 (apps/api) `dotnet build` do `apps/api/Api.sln` sem aviso novo, comparado à baseline (`--no-incremental` nos dois worktrees).
  **Feito:** 19 avisos e 9 distintos nos dois worktrees, a mesma lista por código; 0 erros.

## 2. As conferências da D3 (apps/api)

- [x] 2.1 **Onde a linha aparece.** Uma rodada filtrada (`teste-302.sh`, uma classe unitária, ex.: `AgentDelegationCycleDetectorTests`):
  1. com a saída padrão do `dotnet test`;
  2. se a linha não aparecer, com `--logger "console;verbosity=normal"`;
  3. se ainda não aparecer, com `detailed`.

  Registrar a forma mínima que mostra `[6 threads]`. O `suite-302.sh` passa a usá-la, e o `docs/development.md` a cita (3.1).
  **Feito (`302/conferencias/2.1/`):** com a saída padrão do console, a linha **não aparece**; com `--logger "console;verbosity=normal"`, aparece `[6 threads]` (e em `detailed`). **E o `.trx` guarda a linha em qualquer verbosidade**, no bloco de saída da execução. O `.trx` passa a ser a forma descrita (3.1), e os scripts leem as threads de lá.
- [x] 2.2 **(guarda) O arquivo é o que muda o valor** (`api-scaffold`, cenário "Sem o arquivo, o registro mostra o padrão"). Com a linha `<Content …>` da 1.2 tirada do `.csproj` e o `bin/` limpo, a mesma rodada filtrada mostra o padrão (`[12 threads]`), e não `[6 threads]`.

  A árvore volta **pela cópia** feita antes da mutação, com o `sha256` conferido, e a rodada repetida volta a mostrar `[6 threads]`. Registrar as duas linhas e o "voltou: <arquivo> (<sha>)". Saídas em `~/.cache/buteco-agents/302-guardas/2.2/`.
  **Feito, reprovou contra o defeito:** sem o `<Content …>` e com o arquivo apagado da saída, a rodada (com `xUnit.DiagnosticMessages=true` por runsettings, porque sem o arquivo as mensagens também somem) mostrou **`[12 threads]`**. Restaurado pela cópia ("voltou: Buteco.Api.Tests.csproj (`7287659414529f8c`)", igual ao de antes) e recompilado: **`[6 threads]`**. As duas rodadas 12/12 aprovadas.
- [x] 2.3 **A precedência.** A mesma rodada filtrada, com o arquivo em 6 e `-- xUnit.MaxParallelThreads=4`. Registrar qual valor aparece na linha. A documentação oficial não respondeu: a página de runsettings estava em 404 em 20/11/2026.
  **Feito:** com o arquivo em 6 e `-- xUnit.MaxParallelThreads=4`, a linha mostra **`[4 threads]`**: o runsettings da linha de comando vence o arquivo.
- [x] 2.4 **O ruído.** Contar as linhas que o `diagnosticMessages` acrescenta ao log de uma rodada da suíte inteira, na forma da 2.1. Se passar de uma dezena, o valor volta a `false`, e a linha passa a ser pedida pelo script por runsettings (design, Risks). A decisão vai para "O que o apply mudou".
  **Feito:** o `diagnosticMessages` acrescenta **6 linhas** por execução (o adaptador, `Discovering`, `Discovered`, `Starting` com as threads, `Finished` e o aviso de dados de teoria não serializáveis), contadas no `.trx` de uma rodada filtrada. As outras linhas `[xUnit.net …]` de uma suíte inteira são a saída dos casos, que já existia. Fica `true`.

## 3. Documentação

- [x] 3.1 (docs) `docs/development.md`, seção "Rodar os testes", sobre o `apps/api`:
  - **o valor e o motivo:** menos rodadas inconclusivas, não mais velocidade;
  - **onde foi calibrado:** 12 núcleos, VM do Podman com 6 CPUs e 6 GiB, 20/11/2026;
  - **que é provisório**, e o gatilho da D4 (2 inconclusivas nas últimas 10 rodadas com 6 threads; rodada válida acima de ~10 min; mudança de máquina, VM ou versão do xUnit);
  - **a regra do registro por rodada:** threads, veredito, duração e CPU mínima em toda baseline e todo fechamento do `apps/api`;
  - **a forma de ver o valor efetivo no registro da execução (2.1)**, que é a "forma descrita" do requisito do `api-scaffold`. Se a 2.4 desligar o `diagnosticMessages`, a forma descrita passa a ser o argumento de runsettings que liga as mensagens só na execução;
  - **a precedência (2.3).**

  **Feito:** `docs/development.md`, subseção "Paralelismo da suíte do `apps/api`" em "Rodar os testes". A forma descrita é o `.trx` (a linha fica gravada em qualquer verbosidade), com o console em `normal` e o runsettings como alternativas.
- [x] 3.2 (docs) `python3 scripts/check-docs.py` sem erro.
  **Feito:** `Integridade da documentação: OK`.
- [x] 3.3 (docs) `01-ARQUITETURA_E_CONVENCOES.md`, **convenção 19**: um parágrafo junto do que manda a baseline e o fechamento guardarem a saída completa. Toda baseline e todo fechamento que rodar a suíte do `apps/api` registra, por rodada, as threads efetivas, o veredito, a duração e a CPU mínima, e a entrada da change no `02` traz a linha "`apps/api`: N rodadas, M inconclusivas, K threads". O parágrafo cita o gatilho da D4 e aponta para o `docs/development.md` (como rodar) e para o `02` (a série).
  **Feito:** o parágrafo "E a rodada da suíte do `apps/api` registra com quantas threads rodou, e se valeu", entre a "terceira coisa" e a "QUARTA coisa" da convenção 19.

## 4. Fechamento

- [x] 4.1 (apps/api) As rodadas **intercaladas**, uma por vez e com o lock, em `~/.cache/buteco-agents/302/fechamento/`:
  1. change, 6 threads;
  2. baseline (worktree da 0.2), 12 threads, com `RUNSETTINGS="xUnit.DiagnosticMessages=true"`;
  3. change, 6 threads.

  Cada uma com a linha das threads, o veredito, a duração e a CPU mínima. Com a 0.3, a série desta change fica em **2 rodadas com 12 e 2 com 6**, somada às medições do design. **Parada** se uma rodada com 6 sair inconclusiva.

  **Feito (`302/fechamento/`), sem parada:**
  - change, 6: **456 s, `[6 threads]`, CPU mín. 53, APROVADA**;
  - baseline, 12: **446 s, `[12 threads]`, CPU mín. 48, INCONCLUSIVA** (`FederatedProviderHangingTests`, 13 s);
  - change, 6: **458 s, `[6 threads]`, CPU mín. 44, APROVADA**.

  Com a 0.3, a série da change fica em 12: 2 rodadas, 1 inconclusiva; 6: 2 rodadas, 0 inconclusivas.
- [x] 4.2 (apps/api) A comparação por nome (`nomes-trx.py`) de cada rodada válida da change contra a baseline da 0.3: os mesmos nomes, nenhum sai nem muda.
  **Feito:** as duas rodadas da change com os mesmos 1861 nomes da baseline (1860 aprovados + 1 ignorado), diferença vazia (`diff-nomes.txt`).
- [x] 4.3 `openspec validate testes-api-paralelismo --strict` passa, com o delta do `api-scaffold` (design, D6). O resultado vai registrado.
  **Feito:** `Change 'testes-api-paralelismo' is valid`.
- [x] 4.4 (docs) **Antes do archive**:
  - **a entrada da change no `02-HISTORICO_E_STATUS.md`:**
    - as medições do design;
    - a série desta change (0.3 e 4.1), na linha "`apps/api`: N rodadas, M inconclusivas, 6 threads";
    - as conferências 2.1–2.4;
    - a comparação por nome;
  - **nos itens em aberto do `02`:** o gatilho de recalibração da D4, com as três condições. A primeira é a taxa: 2 inconclusivas nas últimas 10 rodadas do `apps/api` com 6 threads;
  - **o `CHANGELOG.md`:** uma linha em `[Unreleased]`;
  - **o design corrigido** com o que a 2.x achou, numa seção "O que o apply mudou" (convenção 9).

  **Feito:** a entrada no fim do `02`, com a tabela da série e a linha "`apps/api`: 4 rodadas, 1 inconclusiva (com 12 threads), 0 inconclusivas com 6"; o item do gatilho em "Itens em aberto, registrados conscientemente", ao lado da régua de contenção; a linha em `[Unreleased]` do `CHANGELOG`; a seção "O que o apply mudou" do design.
- [x] 4.5 O worktree da baseline removido, conferido por `git worktree list`; as saídas ficam em `~/.cache/buteco-agents/302/`.
  **Feito:** `git status` do worktree da baseline vazio antes; removido, e fora do `git worktree list`.
- [x] 4.6 `/opsx:archive` com sync: o requisito novo entra no `openspec/specs/api-scaffold/spec.md`. Depois, o commit único e o PR, **quando o dono pedir** (convenção 24: archive antes do push; `Closes #302`).
  **Feito:** antes do archive, pedido do dono, a convenção 19 do `01` e o `docs/development.md` deixaram de repetir os limites do gatilho e apontam para o item do `02`, o único lugar com os números.
