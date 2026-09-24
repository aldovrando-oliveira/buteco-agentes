## 1. Os guardas primeiro, e eles precisam reprovar

Convenção 15: guarda só vale depois de ter falhado contra o defeito real. Toda
tarefa desta change roda em **`apps/api`**.

- [x] 1.1 Em `InsightsEndpointsTests`, escrever **o par positivo que faltou**:
      janela inteiramente dentro do regime, contendo um dia sem ocorrência entre
      dias que tiveram, e a série traz esse dia com `taskCount: 0`. **O seed já
      serve** — há execução no dia local 14/09 e no 16/09, o regime começa em
      01/09, e **15/09 é o dia-buraco**. Nenhum dado novo.
- [x] 1.2 **Rodar e ver reprovar contra `HEAD`.** Se passar, o cenário não é o do
      defeito e a tarefa 1.1 está errada — parar e refazer antes de tocar em
      qualquer código.
- [x] 1.3 Escrever o guarda do contador de tokens do dia vazio: contagem `0` e
      `tokenCount` **nulo** na mesma linha. É o par que impede alguém propagar o
      zero de uma pergunta para a outra.
- [x] 1.4 Escrever o guarda do dia da semana **coberto e vazio**: chega com `0`,
      e a asserção é contra `0`, **não** contra "algum número" — é ela que pega a
      armadilha do `count(*)` devolvendo `1` no `left join` (design.md, D4).
- [x] 1.5 Escrever o guarda do dia da semana **não coberto**: faixa medida curta
      demais, e o dia da semana **não aparece** — nenhum `0` para ele.
- [x] 1.6 Escrever o guarda do **pico ausente**: janela dentro do regime, sem
      nenhuma ocorrência, e `peakWeekday` chega **nulo**. Ele reprova contra a
      implementação ingênua da D4 e é o que prende a D5.
- [x] 1.7 Escrever o guarda do **dia futuro**: janela terminando depois do
      instante fixo da fixture (`2026-09-23T12:00:00Z`), e a série **para no dia
      da consulta** — nenhum dia posterior com `0`.
- [x] 1.8 **Reforçar `DaysBeforeTheRegime_AreOmitted_NeverEmittedAsZero` com
      precondição afirmada.** Hoje ele passa por vacuidade: `Assert.All` sobre
      lista vazia é verde, e foi assim que ele ficou verde contra uma
      implementação que não emitia nada. Afirmar que a série **não** está vazia e
      contém os dias esperados **antes** de afirmar que nenhum é anterior ao
      regime.
- [x] 1.9 Repetir **1.1 a 1.8** em `AgentInsightsEndpointsTests`, contra a rota do
      agente. **Não é duplicação supérflua:** `GetAgentInsightsQueryHandler` tem
      consulta própria, e o comentário do próprio código já registra que *"o verde
      de lá não cobre esta"*.
- [x] 1.10 Acrescentar ao escopo do agente o guarda que só faz sentido lá: **dia
      em que só outro agente executou** continua sendo dia medido, e a série do
      agente consultado traz esse dia com `0` em vez de omiti-lo.
- [x] 1.11 Rodar as duas classes e registrar **quais** reprovam e com que
      mensagem. É esse registro que a convenção 15 pede, e ele vai para o `02`.

## 2. A correção da série diária

- [x] 2.1 Injetar `TimeProvider` em `GetSystemInsightsQueryHandler`. Já está
      registrado em `apps/api` desde a `rotas-de-agregacao-sistema`; entra um
      parâmetro de construtor, nenhum registro novo.
- [x] 2.2 Calcular o fim efetivo da janela como o **mais cedo** entre `query.To` e
      o instante de `TimeProvider`. Comentar apontando a D3: o enunciado da #65
      dizia "termina no `to` pedido", e seguir à letra criaria o defeito
      simétrico — emitir `0` para dias que ainda não aconteceram.
- [x] 2.3 Reescrever a consulta diária com `generate_series` sobre o dia local,
      de `executionFrom` até o fim efetivo, com `left join` a `task_executions`
      pelo dia local **e** pelo filtro de instante.
- [x] 2.4 Comentar **os dois limites**, cada um com o seu motivo: o inferior é
      `executionFrom` porque gerar antes do regime reintroduziria o defeito que a
      metade negativa proíbe, e seria pior que o atual por emitir `0` em vez de
      omitir; o superior é o recorte da 2.2.
- [x] 2.5 Conferir que `TokenCount` continua **nulo** no dia sem ocorrência — o
      `filter` sobre nenhuma linha devolve `NULL`, e isso é requisito (D9), não
      efeito colateral. O guarda 1.3 é quem prende.
- [x] 2.6 Rodar e ver **1.1, 1.3 e 1.7 passarem a verde**, e **1.8 continuar
      verde** com a precondição afirmada.

## 3. A correção do dia da semana e do pico

- [x] 3.1 Reescrever a consulta por dia da semana sobre o **mesmo domínio de dias**
      do `generate_series`, com `extract(dow)` e `left join`.
- [x] 3.2 **Trocar `count(*)` por `count(e."TaskId")`.** Com o `left join`,
      `count(*)` conta `1` no dia sem correspondência e todo dia da semana vazio
      chegaria com `1` — números pequenos, todos positivos, nenhum sintoma.
      Comentar a armadilha no lugar.
- [x] 3.3 Acrescentar a condição do pico: nulo quando a lista está vazia **ou**
      quando a maior contagem é `0`. **As duas condições**, e comentar que a
      segunda só passou a ser necessária por causa da 3.1 — sem ela, sete linhas
      de `0` elegem domingo como pico de um período em que nada aconteceu, que é
      o que o comentário original já proibia em palavras.
- [x] 3.4 Rodar e ver **1.4, 1.5 e 1.6** passarem a verde.

## 4. A rota do agente

- [x] 4.1 Repetir **2.1 a 2.5 e 3.1 a 3.3** em
      `GetAgentInsightsQueryHandler.TemporalAsync`, preservando o filtro por
      `AgentId` nos dois pontos em que ele já existe.
- [x] 4.2 **Não extrair método comum** (design.md, D6). São consultas diferentes,
      não a mesma com um parâmetro; uma extração com `Guid?` opcional esconderia
      a diferença em vez de compartilhar a semelhança. O comentário de cada cópia
      aponta a outra, como já fazem as entidades de métrica duplicadas entre
      `apps/api` e `apps/workers`.
- [x] 4.3 Rodar a classe do agente e ver **1.9 e 1.10** passarem a verde.

## 5. Mutação

- [x] 5.1 **Mutação do limite inferior:** trocar `executionFrom` por `query.From`
      no `generate_series`, nos dois handlers. Conferir que **só** os guardas do
      regime reprovam — nos dois escopos. Se outro reprovar, ou se nenhum
      reprovar, o conjunto não discrimina o que diz discriminar.
- [x] 5.2 **Mutação do pico:** remover a condição do máximo zero. Conferir que
      **só** o guarda do pico reprova, nos dois escopos.
- [x] 5.3 **Mutação do contador:** voltar `count(e."TaskId")` para `count(*)` na
      consulta por dia da semana. Conferir que **só** o guarda do dia da semana
      coberto e vazio reprova.
- [x] 5.4 Reverter as três mutações e registrar o resultado das três no `02`.

## 6. O XML doc que afirma mais do que a consulta fazia

- [x] 6.1 Corrigir o XML doc de `DailyInsightPoint` em
      `Insights/Responses/SystemInsightsResponse.cs`. Ele afirma *"Só existem
      pontos para dias dentro do regime: um dia anterior ao início da medição é
      OMITIDO, nunca emitido com `0`"* — o que descrevia metade do comportamento
      e passa a descrever o todo. Escrever **com a causa** (convenção 9): a
      afirmação era aspiracional até esta change, e a omissão passou a significar
      uma coisa só.
- [x] 6.2 Registrar no comentário que a ausência de um dia agora é suficiente
      para o cliente decidir, e que cruzar com `regimes` para isso deixou de ser
      necessário — é o que a D2 da change da #52 assume.

## 7. Verificação de suíte

- [x] 7.1 Rodar `apps/api` e comparar com a baseline do `design.md` (**391/391**,
      medida em 24/09/2026 sobre `0b189fa`), **por nome de teste** e não só pelo
      total. O esperado é 391 + os casos novos.
- [x] 7.2 Confirmar que `apps/workers`, `apps/inbox` e `apps/frontend` **não foram
      tocadas** — nenhum arquivo desta change vive fora de `apps/api`. Registrar
      as baselines do `design.md` sem rodá-las de novo.
- [x] 7.3 Se alguma precisar rodar, rodar **uma por vez**: a baseline registra o
      flake de `DebounceSweepServiceTests` que aparece sob contenção e some na
      rodada isolada.
- [x] 7.4 `openspec validate --all` verde.

## 8. Conferência contra a rota real

- [x] 8.1 Subir `apps/api` local contra o banco de dev — **com
      `TZ=America/Sao_Paulo` no ambiente**, senão a checagem de boot reprova com
      `'Brazil/East'` — e chamar a rota com a mesma janela do diagnóstico:
      `from=2026-08-24T00:00:00Z&to=2026-09-23T23:59:59Z`.
- [x] 8.2 Conferir no corpo real que a `dailySeries` traz **os dias de 22/09 em
      diante com `0`** e **nenhum dia anterior a 22/09** — era `[]` antes desta
      change, com os dois casos colapsados.
- [x] 8.3 Chamar a rota do agente com a mesma janela e conferir o mesmo.
- [x] 8.4 Registrar os dois corpos no `02`, antes e depois, que é a evidência de
      que o defeito medido foi o defeito corrigido.

## 9. Registro e fechamento

- [x] 9.1 Escrever no `02-HISTORICO_E_STATUS.md`, com o número da issue
      (convenção 23):
      **(a)** o defeito, a causa de ter sobrevivido ao archive, e o resultado das
      três mutações — **#65**;
      **(b)** o **refinamento da régua "o par não é universal"**: a régua cobria
      quando o par não existe; este caso é o inverso — o par existia, era
      exprimível, e nenhum guarda foi escrito. A ausência do par precisa ser
      **decidida e escrita**, não omitida. Vai também para a **#68**, junto da
      documentação do board;
      **(c)** que `byWeekday` entrou aqui em vez de issue própria, com o critério
      que decidiu (D4);
      **(d)** que a D3 contraria a letra do enunciado da #65 e preserva a razão
      dele (convenção 9).
- [x] 9.2 Fechar a **décima sétima medição da convenção 18**: comparar a projeção
      do `design.md` com o medido, nas três colunas separadas — escrito à mão,
      **duplo** (projetado **zero**, e é a linha mais informativa da tabela), e
      gerado (deve ser 0). Contar `#### Scenario:` nos deltas e usar `git diff -w`
      para o modificado. **Não revisar a projeção**; só comparar. Anotar que os
      deltas fecharam em **16** cenários, dos quais **12 novos** — os outros 4 são
      preexistentes que o `MODIFIED` obriga a copiar, e essa distinção é régua
      nova para a série.
- [x] 9.3 Atualizar o `CHANGELOG.md` e rodar `scripts/check-docs.py`.
- [x] 9.4 Abrir o PR referenciando a **#65** no corpo, e mover a issue para
      `In Review`. O `proposal.md` já abre com o número (convenção 23).
- [x] 9.5 **Avisar na #52** que o bloqueio caiu, com o corpo real da tarefa 8.2
      como evidência — é o que a tarefa 0.1 de lá vai conferir.

## 10. Conferência de escopo — a lista fechada

Montada por **leitura do repositório** em 24/09/2026. Antes de fechar, confirmar
com `git status` que o diff contém **apenas** o que está na primeira coluna.

**Pode ser tocado:**

| caminho | o que muda |
|---|---|
| `apps/api/src/Buteco.Api/Insights/Queries/GetSystemInsights/GetSystemInsightsQueryHandler.cs` | `TemporalAsync` e o construtor |
| `apps/api/src/Buteco.Api/Insights/Queries/GetAgentInsights/GetAgentInsightsQueryHandler.cs` | idem, com o filtro por agente |
| `apps/api/src/Buteco.Api/Insights/Responses/SystemInsightsResponse.cs` | **só** o XML doc de `DailyInsightPoint` |
| `apps/api/tests/Buteco.Api.Tests/InsightsEndpointsTests.cs` | guardas novos e a precondição do negativo |
| `apps/api/tests/Buteco.Api.Tests/AgentInsightsEndpointsTests.cs` | idem |
| `openspec/changes/serie-diaria-dia-medido-vazio/**` | os artefatos desta change |
| `02-HISTORICO_E_STATUS.md` · `CHANGELOG.md` | itens 9.1 e 9.3 |

**Não pode ser tocado, e a razão de cada um:**

| caminho | razão |
|---|---|
| `apps/frontend/**` | é a #52, que esta desbloqueia; a D2 de lá já está escrita contra o comportamento corrigido |
| `apps/workers/**` · `apps/inbox/**` | não participam; nenhuma consulta desta change os alcança |
| `libs/ProviderCatalog*` | correção de consulta dentro de um app, nada a compartilhar |
| `apps/api/src/Buteco.Api/Options/MetricsOptions.cs` | o mapa de regimes e o fuso não mudam (D9) |
| `apps/api/src/Buteco.Api/Insights/InsightsPeriod.cs` | o contrato de janela não muda — o recorte da D3 é do handler, não do parse |
| `apps/api/src/Buteco.Api/Insights/Endpoints/InsightsEndpoints.cs` | nenhuma rota nova, nenhuma assinatura nova |
| as demais consultas dos dois handlers | volume, tokens, desempenho, erros e delegação não têm domínio denso a preencher |
| `apps/api/src/Buteco.Api/Migrations/**` · `deploy/migrate` | nenhuma migração, nenhum índice |
| `apps/frontend/deploy/nginx.conf` | o prefixo `insights` já roteia desde a etapa 3 |
| `docs/conventions.md` | esta change **cumpre** convenções; o refinamento da régua vai para o `02` e a #68, não para cá |
| `openspec/specs/**` | main specs só mudam no archive, nunca no apply |
| `openspec/changes/archive/**` · `openspec/changes/insights-pagina-do-sistema/**` | registro do que se decidiu então; a change da #52 é outra branch |
