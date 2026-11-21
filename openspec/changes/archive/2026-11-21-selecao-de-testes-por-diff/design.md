## Context

Hoje, cada change decide à mão o que roda nas rodadas intermediárias do apply. A decisão fica escrita no cabeçalho do `suite-NNN.sh` local ("Dentro: api, workers, rt-crossapp…; Fora: inbox, connectors, porque…"), herdado de cópia em cópia. A baseline e o fechamento rodam completos (convenção 19). O meio é julgamento.

Um estudo de suíte mais rápida (19–20/11/2026, numa branch descartável fora do `main`, sobre `c9733ce`) recomendou uma camada rápida. Dela, já entraram as duas changes de barateamento do `apps/api` (#302, paralelismo 6; #303, host derivado por classe). A marcação por `Trait` e a "área tocada" (R1 e R3 do estudo) ficaram adiadas por decisão do dono. Esta change é a parte que **não marca nenhum teste**: um script que decide, a partir do diff, o que rodar.

**Fontes:**

- o estudo, seções 6.3 e 9.1–9.3 (os números citados daqui levam "estudo, `c9733ce`");
- o `02-HISTORICO_E_STATUS.md`, entrada da #303, para a suíte do `apps/api` depois das duas changes;
- as medições feitas nesta proposta, em 21/11/2026, sobre `5c21c89` (merge do PR #305);
- a leitura dos `.csproj`, das `.sln` e das fontes de teste em `5c21c89`;
- o protótipo descartável desta proposta (fora do repositório), que aplicou as regras da D4 aos 13 fixes.

### O que a leitura do repositório achou, além do estudo

- **Dois artefatos lidos por outra pilha, e não um.** O estudo achou o `nginx.conf`, lido por testes .NET de três apps (`tests/Shared/NginxRouting.cs`, linkado por `Compile Include` em api, inbox e connectors). Há outro: `apps/api/tests/.../ProductVersionCoherenceTests.cs` lê o `Directory.Build.props`, o `apps/frontend/package.json` e **todo** `.csproj` e `package.json` do repositório. Uma mudança de versão no `package.json` do frontend, ou uma `<Version>` num `.csproj` do workers, só é pega por um teste do api. E o próprio frontend tem `src/app/nginxHeaders.test.ts`, que lê `deploy/nginx.conf` por `readFileSync`, e o `related` não o alcança.
- **`.md` não é sempre documentação.** O `apps/connectors/tests/.../Fixtures/GoogleDrive/` tem `.md` que os testes leem (`export-doc-com-imagem.md`, `…esperado.md`). A regra "só documentação" é por **lugar**, não por extensão.
- **O `tests/ApiConnectorsRoundTrip.Tests` compila arquivos de `apps/connectors/tests/`** (`FakeConnector.cs`, `TestIssuer.cs`) por `Compile Include … Link`. O grafo lê esse tipo de aresta também.
- **Cada app .NET tem um só projeto de teste**, e cada `.sln` só o seu `src` e o seu `tests`. `libs/ProviderCatalog.Tests` é o oitavo projeto de teste. Os três de `tests/` não estão em nenhuma `.sln`.

## Goals / Non-Goals

**Goals:**

- Tornar a seleção das rodadas intermediárias **mecânica e auditável**: o mesmo diff dá o mesmo plano, e cada passo diz por que entrou.
- Entregar o ganho do frontend: numa folha, `tsc -b` + lint + `related` em vez do Vitest inteiro (D6).
- Errar para o lado barulhento: o que o script não reconhece roda tudo.
- Fechar por regra o único escape da amostra do estudo (`b4fcf5d`) e o outro artefato cruzado que a leitura achou.

**Non-Goals:**

- Acelerar a suíte do `apps/api`. Com o api tocado, ela roda inteira.
- `Trait`, área tocada (R1 e R3 do estudo), Postgres compartilhado, cache de baseline por SHA.
- Mudar qualquer teste existente ou código de produção.
- Substituir a baseline ou o fechamento. Os dois continuam completos (convenção 19).
- Executar suíte de dentro do repositório (D1).

## Decisions

### D1. O script do repositório planeja; a execução fica onde o lock já está

O script só **imprime** o plano. Ele não chama `dotnet test` nem `npm`, e não toma o lock.

**A execução**, nesta máquina, continua no `suite-NNN.sh` de `~/.cache/buteco-agents/bin`. Ele já tem o lock (`suites.lock`, por `zsystem flock`), a espera de load e de `CPU_Speed_Limit`, a amostragem a cada 5 s, o veredito assimétrico (#181) e o registro das threads do `apps/api` (#302). O `suite-307.sh` ganha um modo `plano`: lê as linhas de `--passos` e roda cada uma como uma suíte, com tudo isso. O comando vem do plano, e não de um `case` do script local. Assim a lista do que existe para rodar mora num lugar só, o repositório.

**Para quem não tem os scripts locais**, o `docs/development.md` diz para rodar os comandos do plano um por vez, como já diz das suítes hoje.

**Alternativas:**

- **`--executar` no script do repositório:** descartada. Sem o lock, ele o contorna. Com o lock, ele o duplica: teria de tomar o mesmo `suites.lock` que o `zsystem flock` do zsh toma, com a mesma semântica de espera, e duplicaria também a amostragem de CPU e o veredito. Os três são prática desta máquina e mudam de change para change (o lock esperado em vez de recusado veio na #302). No repositório, virariam uma segunda cópia que envelhece.
- **O script local seguir com o `case` e o plano dar só nomes** (`api`, `rt-inbox`…): descartada. Os passos novos (`Nginx` filtrado, o teste de versão, o `related` com a lista de arquivos) exigiriam um `case` novo em cada cópia, e a lista voltaria a viver fora do repositório.

**Por que separar dá teste:** o plano é função pura dos caminhos tocados e da árvore. Os testes da D7 rodam sobre ele sem subir nada.

### D2. Python 3, só a biblioteca padrão

O precedente é o `scripts/check-docs.py`. Sem dependência nova, sem `pytest` (não está instalado nesta máquina; `python3 -c 'import pytest'` falha), com `unittest`. Não se fixa versão de Python: o script usa o mesmo `python3` que o `check-docs.py`, e nada além do que ele já usa (`pathlib`, `re`, `subprocess`). Nesta máquina, Python 3.14.6.

**Alternativa:** zsh, como os scripts locais. Descartada: ler `.csproj`, montar o fecho do grafo e testar a tabela em zsh é mais frágil, e o repositório já tem a convenção de Python para script.

### D3. Os caminhos tocados

- **Base:** o `merge-base` entre `origin/main` e `HEAD`, ou `--base <ref>`. O script **não faz `fetch`**; o plano imprime o SHA da base, para quem lê saber contra o que foi calculado.
- **O que entra:** `git diff --name-status -M <base>` (commitado, no índice e na árvore de trabalho, numa chamada) mais `git ls-files --others --exclude-standard` (arquivo novo não rastreado).
- **Renomeação:** os dois caminhos. **Apagado:** conta como tocado, e no frontend força a suíte inteira (D6).
- **`--caminhos <arquivo>|-`:** a lista dada no lugar do diff. Os testes e a medição usam.
- **Sem histórico** (clone raso, `origin/main` ausente): falha com o motivo. Plano vazio seria o erro silencioso.

### D4. As regras: caminho tocado → o que roda

Cada caminho cai na **primeira** regra da tabela que casa (a classificação). Os **acréscimos**, abaixo da tabela, valem **além** da classificação, para todo caminho que casar com eles. O plano é a união. Em todo plano entram, sempre, o `check-docs.py` (1 s, e ele confere coisas que dependem de `apps/`) e os testes do próprio script: o G3 e o G4 (D7) acusam mudanças que acontecem em `apps/` e `tests/`, e só rodariam quando `scripts/` fosse tocado se dependessem de uma regra (correção do dono na revisão, 21/11/2026).

| # | Caminho tocado | O que roda, além dos dois de sempre |
|---|---|---|
| 1 | `docs/**`, `openspec/**`, `.github/**`, `.claude/**`, `.gitignore` e `*.md` **da raiz**, `LICENSE`, `NOTICE` | nada |
| 2 | `apps/{api,workers,inbox,connectors}/**` | a suíte inteira de cada projeto de teste dentro do app, e os projetos do grafo (D5) |
| 3 | `tests/<P>/**`, com `P` ≠ `Shared` | o projeto `P` e os que o referenciam |
| 4 | `apps/frontend/deploy/**` | `Nginx*` do api, do inbox e do connectors (filtrados), e `src/app/nginxHeaders.test.ts` |
| 5 | `apps/frontend/src/auth/**`, `src/app/**`, `src/theme.ts`, ou arquivo apagado de `src/` | `tsc -b`, lint e o Vitest inteiro |
| 6 | `apps/frontend/src/**` (o resto) | `tsc -b`, lint, `vitest related --run <arquivos>` e as varreduras de `src/` (achado do apply) |
| 7 | `apps/frontend/**` (o resto: `package.json`, configs, `index.html`, `package-lock.json`, `Dockerfile`, `public/`) | `tsc -b`, lint e o Vitest inteiro |
| 8 | `scripts/**` | nada (os testes do script já estão em todo plano) |
| — | **qualquer outro**: `Directory.*.props`, `global.json`, `libs/**`, `tests/Shared/**`, `apps/<app novo>/**`, `deploy/**`, `docker-compose*`, `.env*`, `.codegraph/**` | **tudo** (D5) |

**Acréscimos** (valem junto da classificação):

| | Caminho | O que acrescenta |
|---|---|---|
| A1 | qualquer `*.csproj` | o `ProductVersionCoherenceTests` do api (filtrado) |
| A2 | `apps/frontend/package.json` | o `ProductVersionCoherenceTests` do api (filtrado) |

**Por que acréscimo, e não linha da tabela:** com "primeira que casa", uma linha de `.csproj` depois da 2 nunca dispararia, e antes dela tiraria do `.csproj` a suíte do app. O acréscimo não depende da ordem. A1 cobre também o `.csproj` de `tests/<P>` (o teste de versão varre todos) e o de `libs/` (que já roda tudo; o filtrado some, abaixo).

**`.claude/**` é documentação para a seleção** (correção do dono): são comandos e skills do assistente, e nenhum teste lê esses arquivos. Se um passar a ler, o G4 acusa.

**Passo filtrado** (`Nginx`, versão) é `dotnet test <projeto> --filter "FullyQualifiedName~<padrão>"`. Ele **some** quando a suíte inteira do mesmo projeto já está no plano.

**Por que a linha 4 e os acréscimos existem:** são os artefatos lidos por outra pilha (ver Context). A linha 4 é o `b4fcf5d`, o único escape da amostra do estudo com a área tocada. O estudo não a contou na taxa, para não fechar o caso por construção na própria amostra. Aqui ela entra porque é regra por artefato, e não por amostra. A1 e A2 são o mesmo tipo, achado na leitura; não houve defeito real por eles na amostra.

**Por que `tests/Shared/**` roda tudo, e não os três que linkam o arquivo:** pela decisão do dono, a lista do que é infraestrutura comum fica barulhenta. O grafo da D5 lê o `Compile Include … Link` e saberia os três. Fica registrado como refinamento possível, não feito.

### D5. O grafo e o "tudo" saem da árvore, não de lista

- **Projetos:** todos os `.csproj` (versionados e novos), lidos na hora.
- **Projeto de teste:** o que tem `PackageReference` do `Microsoft.NET.Test.Sdk`, e não o nome `*.Tests`. Hoje são 8.
- **Arestas:** `ProjectReference` (com `\` normalizado) e `Compile Include … Link` de arquivo fora do projeto. Fecho transitivo.
- **Seleção pela regra 2 ou 3:** um projeto de teste entra se ele próprio, ou qualquer projeto do seu fecho, ou qualquer arquivo que ele linka, está sob um diretório tocado.
- **App .NET roda pelo `.csproj` de teste, e não pela `.sln`.** Assim um projeto de teste novo fora da `.sln` não fica de fora.
- **Tudo** é: cada projeto de teste descoberto, o frontend (`tsc -b`, lint e Vitest inteiro) e os testes do script. `format:check` e `build` do frontend ficam fora, como na seleção: são do fechamento (CONTRIBUTING §1). O `format:check` reprova no `main` por débito conhecido (#255).

**O grafo hoje [lido em `5c21c89`], e o que ele seleciona por app tocado:**

| App tocado | Projetos de teste |
|---|---|
| api | `Buteco.Api.Tests`, `CrossAppTaskStoreCompatibility`, `InboxOrchestratorRoundTrip`, `ApiConnectorsRoundTrip` |
| workers | `Buteco.Workers.Tests`, `CrossAppTaskStoreCompatibility`, `InboxOrchestratorRoundTrip` |
| inbox | `Buteco.Inbox.Tests`, `InboxOrchestratorRoundTrip` |
| connectors | `Buteco.Connectors.Tests`, `ApiConnectorsRoundTrip` (também pelo `Compile Include` dos fakes) |

É a "variante 1, grafo estrito" do estudo (9.3), sem lista escrita.

### D6. O frontend: `related` na folha, a suíte inteira no núcleo

**Medido nesta proposta [M, 21/11/2026, `5c21c89`]**, uma rodada por passo (duas na folha e no Vitest inteiro, intercaladas), com o lock, o load de 1 e 5 min abaixo de 6 e o `CPU_Speed_Limit` em 100 na largada; `postgres_1` e `rabbitmq_1` do compose de pé, nenhuma outra suíte rodando. Saídas em `~/.cache/buteco-agents/307/proposta/`.

| Passo | Arquivos de teste | Duração | CPU mín. | Veredito |
|---|---|---|---|---|
| `tsc -b` | — | 17 s | 100 | aprovada |
| lint | — | 18 s | 100 | aprovada |
| `related` de `AgentTable.tsx` (folha) | 7 (133 casos) | 14 s / 11 s | 100 / 100 | aprovada / aprovada |
| `related` de `src/auth/session.ts` | 102 (1675 casos) | 122 s | 62 | aprovada |
| Vitest inteiro | 207 (2820 casos) | 147 s / 145 s | 100 / 83 | aprovada / aprovada |

**Comparados com os mesmos passos dos dois lados** (`tsc -b` e lint entram nos dois; correção do dono na revisão):

| | `tsc -b` | lint | Vitest | Total |
|---|---|---|---|---|
| folha (`related`) | 17 s | 18 s | 11–14 s | **46–49 s** |
| suíte inteira | 17 s | 18 s | 145–147 s | **180–182 s** |

**A folha custa ~1/4 da suíte inteira (46–49 s contra 180–182 s), uma economia de ~130 s por rodada intermediária de frontend.** O estudo, sobre `c9733ce`, comparava 50 s contra 208–263 s só de Vitest; com os mesmos passos, seria 50 s contra ~240–300 s. Aqui a CPU ficou em 83–100, e o Vitest inteiro saiu bem mais rápido. O ganho depende de quanto a CPU cai durante a suíte inteira, e isso varia (convenção 22). `tsc` e lint, ~35 s fixos, passam a ser a maior parte do custo de uma folha. A seção 5 das tarefas remede.

**Quantos arquivos o `related` puxa, por arquivo tocado** [M, `vitest list --filesOnly --staticParse --changed HEAD`, só listagem, sobre 207 arquivos de teste]:

| Arquivo tocado | Arquivos que o `related` puxa |
|---|---|
| `src/test/setup.ts` | 207 |
| `src/theme.ts` | 130 |
| `src/auth/roles.ts`, `session.ts`, `sessionFetch.ts` | 102–103 |
| `src/test/authFakes.ts` | 72 |
| `src/utils/failureNotice.ts` | 56 |
| `src/components/layout/BackLink.tsx` | 36 |
| `src/utils/panelDates.ts` | 32 |
| folhas da amostra (`AgentTable`, `AgentConsumptionCard`, `DeclaredGap`, `ApplicationsListPage`) | 7–12 |
| `src/app/routes.tsx`, `src/app/queryClient.ts` | 5 e 2 |

**O que a tabela diz sobre a regra 5:**

- Para `src/auth/` e `src/theme.ts`, o motivo do estudo vale: o `related` já puxa metade ou mais da suíte, e a suíte inteira custa pouco a mais e não depende do grafo de imports.
- **Para `src/app/`, não vale.** O `related` puxa 2 a 5 arquivos. A regra fica (decisão do dono) por outro motivo: `src/app/` é a composição do painel (rotas, guarda de papel, cliente de consulta), e 54 arquivos de teste montam o próprio `QueryClient` em vez de importar o de `src/app/`. Uma mudança de política ali não aparece por import. A suíte inteira também não pegaria por isso; o que ela dá é rodar as telas que passam pelo roteador. **É escolha de cautela, não de medida**, e fica registrada assim.
- `src/test/` **não** precisa de regra: o `related` já puxa o que importa dele, e o `setup.ts` puxa tudo.

**Arquivo apagado** força a suíte inteira, porque o `related` não recebe caminho que não existe.

### D7. Os guardas (convenção 15)

Os testes ficam em `scripts/tests/`, com `unittest`. Cada guarda só conta depois de reprovar contra o defeito que existe para pegar (tarefas 2.2, 2.4, 2.5 e 2.7).

- **G1, a tabela à parte.** `scripts/tests/selecao-esperada.md`: caminho concreto → passos esperados, escrita a partir da D4. O teste confere o plano de cada linha contra a tabela, nunca contra a lógica do script. Se o guarda e o código lessem a mesma fonte, o guarda não testaria a fonte (sexta forma da convenção 15).
- **G2, os 13 fixes da amostra do estudo (9.1).** `scripts/tests/fixes-da-amostra.md`, com o commit e o projeto e a classe que pegou cada um. O teste calcula a seleção a partir do **diff de produção** do fix: o diff do commit sem os arquivos de teste, o mesmo critério do estudo para "reintroduzir". Com o diff inteiro, o teste que o próprio fix acrescentou já selecionaria o app, e o caso passaria por construção. É conferência estática: lê o `git`, não roda suíte.

  | Commit | App do diff de produção | Quem pegou (estudo) | O que a seleção precisa incluir |
  |---|---|---|---|
  | `be6d1f2` | inbox | `MessagePersistenceTests` | `Buteco.Inbox.Tests` |
  | `a91f0c9` | inbox | `ContactSessionResolverTests` | `Buteco.Inbox.Tests` |
  | `2631309` | workers | `PushNotificationEndToEndTests` | `Buteco.Workers.Tests` |
  | `bfa1ec0` | workers | `PostgresTaskStoreCompatibilityTests` | `CrossAppTaskStoreCompatibility.Tests` |
  | `4729380` | api | `AgentEndpointsTests` | `Buteco.Api.Tests` |
  | `868d291` | workers, api, inbox (`appsettings`) | `LlmCallDurationChatClientTests` | `Buteco.Workers.Tests` |
  | `aae444d` | api | `InsightsEndpointsTests` | `Buteco.Api.Tests` |
  | `bfb114b` | frontend (folha) | `DeclaredGap.test.tsx` | `related` com `DeclaredGap.tsx` |
  | `39c43d5` | frontend (folha) | `AgentConsumptionCard.test.tsx` | `related` com `AgentConsumptionCard.tsx` |
  | `6635db2` | frontend (folha) | `ApplicationsListPage.test.tsx` | `related` com `ApplicationsListPage.tsx` |
  | `b4fcf5d` | `apps/frontend/deploy/nginx.conf` | `api/NginxRouteCoverageTests` | `Buteco.Api.Tests` filtrado por `Nginx` |
  | `7fd8e75` | frontend (folha) | `AgentTable.test.tsx` | `related` com `AgentTable.tsx` |
  | `b7333d9` | api | `IdentityIssuerStartupValidationTests` | `Buteco.Api.Tests` |

  **O protótipo desta proposta cobriu os 13** [M, 21/11/2026, `5c21c89`]. O `aae444d`, que o estudo contou como escape da camada sem área, é coberto pela regra 2, porque o api roda inteiro. O `b4fcf5d` passa a ser coberto pela linha 4.

  **Nos 5 do frontend, a conferência estática só vê o arquivo no passo `related`.** Que o `related` chega ao teste que pegou é o Vitest quem diz. A tarefa 2.6 confere uma vez, por `vitest list --changed`, que lista sem rodar caso nenhum (~5 s, medido nesta proposta), e registra.
- **G3, projeto de teste que nenhuma regra alcança.** O teste descobre os projetos de teste pelo conteúdo (D5) e confere: cada um está em "tudo", e tocar um arquivo dentro dele o seleciona. A descoberta é por conteúdo, e a seleção pelo grafo: são fontes diferentes, e o guarda não é circular. O defeito que ele pega é a descoberta que envelhece (filtrar pelo nome, ou pela `.sln`).
- **G4, leitura cruzada sem regra (acréscimo a este pedido).** O teste varre as fontes de teste por leitura de arquivo do repositório fora do próprio projeto: subida de pasta até um arquivo de outra árvore, `readFileSync` fora de `src/`, caminho literal de `apps/` ou de arquivo da raiz. Cada achado tem de estar na lista da linha 4 e dos acréscimos A1 e A2 (o artefato que ele lê), ou o artefato tem de cair em "tudo". É heurístico e erra para o lado de reprovar. É o guarda do tipo de escape do `b4fcf5d`: sem ele, o próximo artefato cruzado entra calado, como o `ProductVersionCoherenceTests` entrou depois do estudo.

### D8. A saída

- **Texto (padrão):** a base e o SHA; os caminhos tocados, agrupados pela regra que os pegou; os passos, cada um com o motivo.
- **`--passos`:** uma linha por passo, `id<TAB>diretório<TAB>comando`, e nada mais na saída padrão. É a entrada do `suite-307.sh plano`.
- **Ordem fixa:** primeiro os baratos e determinísticos, que reprovam antes de uma suíte longa: `check-docs`, testes do script, `tsc -b`, lint; depois os filtrados .NET; depois `related` ou o Vitest inteiro; depois as suítes .NET por app, na ordem do nome do projeto; os de `tests/` por último. Dois planos do mesmo diff são iguais byte a byte (tarefa 2.8).
- **`id`:** o nome do `.csproj` de teste (`Buteco.Inbox.Tests`, `InboxOrchestratorRoundTrip.Tests`), com o filtro entre colchetes (`Buteco.Api.Tests[Nginx]`, `Buteco.Api.Tests[Versao]`), ou `fe-tsc`, `fe-lint`, `fe-nginx`, `fe-varreduras`, `fe-related`, `fe-completa`, `scripts-testes`, `check-docs`. Sai da árvore, não de lista.

### D9. Delta de spec: um requisito em `open-source-governance`, sem mecanismo

A change não muda comportamento do sistema. O `openspec validate` exige delta (a #302 registrou: reprova sem delta, com e sem `--strict`). O padrão da #302 é um requisito na spec que já guarda o assunto, sem o mecanismo.

**A spec é `open-source-governance`**, onde está o "Guia de contribuição alinhado ao fluxo OpenSpec", que já exige que o `CONTRIBUTING.md` diga "como executar os testes de cada app". O requisito novo diz:

- a seleção das rodadas intermediárias sai do diff, inclusive do que não foi commitado e dos arquivos novos;
- o plano aparece antes de rodar, com o motivo de cada passo, e é determinístico;
- caminho não reconhecido seleciona tudo;
- todo projeto de teste é alcançado por alguma regra;
- a seleção não substitui a suíte de cada app afetado antes do PR, e o `CONTRIBUTING.md` diz isso.

**Não entram:** o nome do script, as regras, o `related`, o formato de `--passos`. Mudam sem mudar a spec.

**Alternativas:** `local-dev-environment` (é do compose e das variáveis de ambiente, não de testes) e `repository-documentation` (tem o requisito do `check-docs.py`, mas a seleção não é documentação). Uma capability nova (`test-selection`) seria uma spec de um requisito só, para uma prática de contribuição que o `open-source-governance` já cobre.

### D10. Limites, registrados

- **O `related` segue imports.** Contrato copiado à mão entre api e frontend não aparece. O caso do estudo (6.2, defeito d9) é o código de erro `folder-in-use`, que o painel lê e que está copiado à mão no frontend: mudado no api, só a suíte do api pegou, e o frontend não pegaria. Aqui o api tocado roda inteiro e pega; o lado inverso (mudar a cópia do frontend) só o fechamento pega.
- **A seleção não substitui o fechamento.** O que ela não roda, a suíte completa roda antes do archive (convenção 19 e CONTRIBUTING §1).
- **A amostra dos 13 mede se o guarda do defeito está na seleção**, e não se um defeito novo, sem guarda, seria pego (estudo, 9.1). E é enviesada para o que é isolável: os fixes de concorrência e de ciclo de vida (#46, #47, #49, #162) ficaram fora.
- **O G4 é heurístico.** Uma leitura de arquivo por caminho montado de outro jeito escapa dele.
- **A base sem `fetch`:** o plano é contra o `origin/main` local. O SHA impresso é o que permite conferir.

### Lugar no fluxo

- **`docs/development.md`, "Rodar os testes":** como usar, a tabela da D4 e os limites. É onde o como rodar já mora.
- **`CONTRIBUTING.md` §1:** um parágrafo. Durante a mudança, a seleção; antes do PR, a suíte de cada app afetado, como hoje. É o que a spec exige.
- **`01`, convenção 19:** um parágrafo junto do da #302. É o que quem escreve o `tasks.md` lê (a própria convenção diz que essas regras pertencem ao `tasks.md`). Ele diz que as rodadas intermediárias usam o plano e o registram, e que baseline e fechamento continuam completos.
- **O `suite-307.sh`**, por cópia nos seguintes: o cabeçalho cita a regra. Não basta sozinho, porque vive fora do repositório.

### Validação [M, 21/11/2026, nesta proposta]

- `openspec validate selecao-de-testes-por-diff`: `Change 'selecao-de-testes-por-diff' is valid`, com e sem `--strict`, com o delta da D9. A tarefa 6.2 refaz no fechamento.
- `python3 scripts/check-docs.py`: `Integridade da documentação: OK`, com os artefatos da change na árvore.

## Árvore de arquivos

```
scripts/
├── check-docs.py                         (não muda)
├── selecionar-testes.py                  (novo)
└── tests/
    ├── test_selecionar_testes.py         (novo)
    ├── selecao-esperada.md               (novo: a tabela à parte, G1)
    └── fixes-da-amostra.md               (novo: os 13 casos, G2)
docs/
└── development.md                        (muda: "Rodar os testes")
CONTRIBUTING.md                           (muda: §1)
01-ARQUITETURA_E_CONVENCOES.md            (muda: convenção 19)
openspec/changes/selecao-de-testes-por-diff/
├── proposal.md
├── design.md
├── tasks.md
└── specs/open-source-governance/spec.md  (requisito novo)
```

Nenhum arquivo em `apps/`, `libs/` nem `tests/`. Nada em `libs/`, então nada a justificar lá.

## Risks / Trade-offs

- **[Uma regra escrita errada seleciona de menos, e a rodada intermediária passa verde]** → a tabela à parte (G1) e os 13 fixes (G2), com mutação provada; e o fechamento completo, que pega o que escapou antes do archive.
- **[Um projeto de teste novo, ou um artefato cruzado novo, entra sem regra]** → G3 e G4 reprovam e nomeiam. E o caminho não reconhecido roda tudo.
- **[O `related` deixa passar contrato sem import]** → D10, registrado; o fechamento completo cobre.
- **[O script local e o plano divergirem]** → o comando vem do plano (D1); o `suite-307.sh` não tem lista própria.
- **[`origin/main` local velho]** → o plano imprime o SHA da base; quem mede confere.
- **[Rodar dois passos .NET ao mesmo tempo derrubar a VM]** → o plano é sequencial, e o executor é o script com o lock. O `docs/development.md` diz um por vez.
- **[A conferência G2 depender do histórico]** → clone raso falha alto (D3), nunca passa calado.

## Migration Plan

Não há migração nem deploy. Para voltar atrás, apagar o script e os testes e reverter os parágrafos de documentação. Nenhuma change em andamento depende do script; as que vierem depois passam a citar o plano nas rodadas intermediárias.

## Open Questions

Nenhuma de produto. Ficam como **observações fora de escopo**, para issue se o dono quiser:

- o `tests/Shared/**` poderia selecionar só os três projetos que linkam o arquivo (D4), em vez de tudo;
- o `format:check` do frontend reprova no `main` (#255, já aberta), e por isso fica fora do "tudo".

## O que o apply mudou (21/11/2026)

- **Correções do dono na revisão, antes do apply:** os testes do script entram em todo plano e no checklist antes do PR; a linha de `.csproj` virou o acréscimo A1, porque com "primeira que casa" ela nunca dispararia; `.claude/**` foi para a linha 1; a D6 compara a folha e a suíte inteira com `tsc` e lint dos dois lados (~1/4, e não ~1/3).
- **As varreduras de `src/` (linha 6).** A varredura do G4 achou três testes do frontend que leem `src/` inteiro por `readdirSync`: `app/forbiddenPages.test.tsx` (notificação vermelha fora do `notifyFailure`), `components/data/surfaceTokens.test.ts` (tom fixo de superfície) e `components/forms/passwordFieldGuard.test.ts` (`PasswordInput` direto). São guardas, e o `related` não os alcança, porque não importam o que varrem. Um componente-folha novo que viola um deles passaria verde pela seleção. Eles passam a rodar junto de todo `related`, como o passo `fe-varreduras`, descobertos na hora do plano (`readdirSync` no arquivo de teste). A mudança acrescenta cobertura e não muda contrato, então seguiu sem parada, e tem a mutação 2.2e.
- **O G4 tem uma terceira categoria**, escrita à parte no teste: os que leem o próprio componente e também o importam (`AgentInsightsTab.test.tsx`, `DeclaredGap.test.tsx`). O `related` os alcança.
- **`python3 -B` e o `.gitignore`.** Sem o `-B`, o `unittest` deixa `__pycache__/` em `scripts/`, que o `.gitignore` não cobria. Na revisão do apply, o dono pediu as duas coisas: o `__pycache__/` no `.gitignore`, mantendo o `-B`, e o `.gitignore` da raiz na linha 1, porque nenhum teste o lê. Ele muda a lista de arquivos que a seleção enxerga (`git ls-files --exclude-standard`), e os testes do script, que entram em todo plano, cobrem isso. **Limite:** se um `.gitignore` passar a esconder um projeto de teste, a descoberta do script e a do G3 deixam de vê-lo juntas, porque as duas partem da mesma lista.
- **O checklist do `.github/PULL_REQUEST_TEMPLATE.md`** também ganhou os testes do script, porque espelha o §1 do CONTRIBUTING.
- **O `suite-307.sh`** lia o plano pela entrada padrão, e o `podman machine ssh` do laço a consumia. Corrigido antes da medição.
- **Os fixes de folha do frontend na amostra são 4, e não 5.** O quinto fix de frontend, o `b4fcf5d`, é a linha 4.

### Medição: tempo da seleção por tipo de mudança (tarefas 5.1–5.6) [M, 21/11/2026, `5c21c89`]

Cada tipo é uma lista de caminhos dada por `--caminhos`, executada pelo `suite-307.sh plano`, um passo por vez, com o lock e a espera de load (< 6 no frontend, < 8 no .NET) e de `CPU_Speed_Limit` = 100 **antes de cada passo**. As soluções foram compiladas uma vez antes (todas incrementais depois, 3–38 s). `postgres_1` e `rabbitmq_1` do compose de pé, nenhuma outra suíte rodando. Saídas em `~/.cache/buteco-agents/307/medicao/`. Todos os passos saíram **aprovados**. "Passos" é a soma das durações dos passos; "parede" inclui a espera por load e CPU entre eles.

| Tipo (caminho dado) | Passos do plano | Passos | Parede | CPU mín. no passo mais longo | Referência da suíte inteira |
|---|---|---|---|---|---|
| folha do frontend (`AgentTable.tsx`) | `check-docs` 1 s, `scripts-testes` 2 s, `tsc` 17 s, lint 17 s, `fe-varreduras` 8 s, `related` 14 s | **59 s** | 69 s | 100 | os mesmos passos com o Vitest inteiro: 311 s nesta sessão (5.2), 180–182 s na proposta (D6) |
| núcleo do frontend (`src/auth/session.ts`) | `check-docs` 1 s, `scripts-testes` 2 s, `tsc` 16 s, lint 15 s, Vitest inteiro **277 s** | **311 s** | 319 s | **34** (Vitest inteiro) | é a própria suíte inteira |
| só inbox (`Program.cs`) | 4 s + `Buteco.Inbox.Tests` 54 s + `InboxOrchestratorRoundTrip` 67 s | **125 s** | 633 s | 32 (`rt-inbox`) | a mesma: com o inbox tocado, a suíte dele roda inteira (sem ganho, como o estudo já dizia) |
| só workers (`Program.cs`) | 4 s + `Buteco.Workers.Tests` **412 s** + `CrossAppTaskStoreCompatibility` 20 s + `InboxOrchestratorRoundTrip` 69 s | **505 s** | 632 s | **25** (workers) | a mesma |
| só api (`Program.cs`) | 3 s + `Buteco.Api.Tests` **825 s** (6 threads, 1860 + 1 ignorado) + `ApiConnectorsRoundTrip` 41 s + `CrossAppTaskStoreCompatibility` 19 s + `InboxOrchestratorRoundTrip` 73 s | **961 s** | 1392 s | **25** (api) | a mesma; o `02` (#303) registra o api em 372–411 s, com a CPU mínima em 34–51 |

**O que a tabela diz:**

- **A folha do frontend custa 59 s**, contra 180–311 s dos mesmos passos com o Vitest inteiro: **19–33%**, abaixo da parada de ~50%. O passo `fe-varreduras`, que entrou neste apply, custa 8 s.
- **No .NET a seleção não acelera nada, e não era para acelerar.** Com o app tocado, a suíte dele roda inteira, e o grafo acrescenta os de `tests/`. O ganho é contra "rodar tudo", e é a auditabilidade.
- **Esta sessão foi das mais estranguladas já registradas.** O `CPU_Speed_Limit` ficou entre 25 e 39 em quase toda a rodada do api (amostras a cada 5 s, em `5.5-api/Buteco.Api.Tests-cpu.txt`), com o load de 1 min em 15–21, gerado pela própria suíte (a VM do Podman a ~410% de CPU). O Vitest inteiro foi de 145–147 s (CPU 83–100, na proposta) a 277 s (CPU 34). **O número que vale é a faixa, com a CPU ao lado** (convenção 22).
- **A rodada do api, 825 s e válida, passou dos ~600 s do gatilho 2 de recalibração da #302.** A pedido do dono, ela foi repetida uma vez com a máquina fria (5 min com a CPU em 100 e o load em 2, sem suíte antes): **590 s**, com a CPU caindo a 23 durante a própria rodada (mediana 44). As duas fecharam como ambiente (decisão do dono): a duração acompanha a **mediana** do `CPU_Speed_Limit`, e não o mínimo. As 9 rodadas da série da #302 e da #303 tiveram mediana 55–67 e 372–502 s; estas duas, 34 e 44. Daí, no item do `02`: o gatilho 2 passa a valer só com a mediana em 55 ou mais; três rodadas seguidas com a mediana abaixo de 55 são sinal para investigar a máquina, e não o paralelismo; e o registro por rodada leva a mediana ao lado da mínima (convenção 19, `docs/development.md` e o cabeçalho do `suite-307.sh`). O paralelismo não mudou.


