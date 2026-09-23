## Context

Etapa 3 da linha `metricas-de-operacao`, change **A** de duas. As etapas 1 e 2
estão aplicadas e medindo em produção; o catálogo de 27 métricas é referência
viva no `02`; os protótipos das duas telas estão aprovados.

O diagnóstico vem da exploração `rotas-de-agregacao` de 23/09/2026, e o que ela
estabeleceu por medição está resumido aqui para não precisar ser reconstruído.

**O que foi lido do código, não da memória (convenção 6):**

| fato | evidência |
|---|---|
| `AT TIME ZONE` no repositório | **0 ocorrências** |
| `TimeProvider` em `apps/api` | **0 ocorrências**, em `src` **e** em `tests` |
| `TimeZoneInfo` / `America/Sao_Paulo` em `apps/api/src` | **0 ocorrências** |
| `TZ` em produção | `docker-compose.prod.yml:139` — só no serviço `workers`; o serviço `api` (`:72-82`) não recebe |
| balde diário em qualquer app | **não existe** — zero `date_trunc`, zero `GroupBy` por data em `apps/api/src` e `apps/inbox/src` |
| classificação de rota autenticada nova | **nada a declarar** — `RouteAuthenticationExtensions.cs:22-24` varre só o caminho anônimo |

**O que foi medido, com o regime colado (convenção 22):** `27,7%` das tasks de
`a2a_tasks` mudam de dia — e de dia da semana — se o balde sair em UTC; banco de
desenvolvimento, 23/09/2026, **260 linhas**, carimbos de 01/08 a 22/09. O 29,5%
que circulava é de antes de cinco changes. Em `task_executions` a divergência foi
**0 de 13**, e **esse zero não é contraevidência** — é ausência de população.

**Uma limitação de fonte que esta change declara em vez de esconder:** os
protótipos aprovados **não foram abertos**. O MCP do Claude Design recusou a
autenticação (`FIRST_PARTY_AUTH_REJECTED`; `DesignSync` respondeu pedindo
`/design-login`, que só o dono roda). Tudo que aqui se afirma sobre as telas vem
do `02` e dos `design.md` arquivados — que é registro de segunda mão. **Onde o
protótipo contrariar este documento, o protótipo vence**, e a divergência vira
registro pela convenção 9.

## Goals / Non-Goals

**Goals:**

- `apps/api` passa a ter fuso, e os dois processos passam a concordar **por
  construção**, não por disciplina.
- Uma rota, `GET /insights/system`, servindo o agregado do escopo do sistema.
- Janela, balde, nulo e "medindo desde" decididos **uma vez**, num contrato que a
  change B herda sem reabrir.
- As lacunas do mapa das 27 nomeadas **agora**, antes da tela.

**Non-Goals:**

- **A rota do agente** — change B. E ela **não** é esta filtrada por agente: a
  D16 da `metricas-execucao-coleta` fixou que "Delega para" lê
  `delegation_outcomes` e "Acionado por" lê `task_executions`. Outro conjunto de
  consultas.
- **Nenhuma tela** — etapas 4 e 5.
- **Não corrigir o motivo das recusas de `apps/api`** — é change de coleta.
  Registrada com gatilho cumprido e posição antes da etapa 4.
- **Nenhum índice**, nenhuma migração, nenhuma coluna.
- **Não remover o detector de tasks não-terminais.**
- Não mexer em número de instâncias, na varredura de `PendingDispatch`, nem no
  bloco do inbox.
- **Não tocar `ListTasksAsync` de `apps/api`** — sem consumidor nesta etapa.
- Não converter para `TimeProvider` o `DateTimeOffset.UtcNow` que já existe nas
  entidades de `apps/api`.

## Decisions

### D1 — O fuso vem do mesmo `TZ`, entregue também ao serviço `api`

**Decidido pelo dono.** O `TZ` do compose passa a ser entregue ao serviço `api`
com `:?`, exatamente como já é a `workers`, e é lido **como configuração** do
balde.

**Isto contraria a letra de uma decisão registrada** — *"o nome vem de
configuração explícita da agregação, nunca do `TZ` do processo de `apps/api`"* —
e por isso está aqui, datado (convenção 9).

**A razão da decisão original fica preservada.** O motivo escrito era *"só assim o
balde não depende de qual container respondeu"*. O `:?` entrega o **mesmo valor a
toda réplica**; o balde continua sem depender de quem respondeu.

**E ela resolve o que a alternativa deixava aberto.** Com uma variável própria da
agregação, **nada** faria o fuso do balde e o `TZ` do worker concordarem — e
divergência desloca 27,7% da série em silêncio, sem erro, sem log, sem sintoma.

**Alternativa recusada:** variável própria (`Metrics:TimeZone`). Recusada por não
ter mecanismo de concordância. **Custo aceito da escolhida:** `TZ` no container
muda o fuso do processo `api`. Hoje o efeito é **nenhum** — nada ali renderiza
data local — e se um dia renderizar, será no fuso certo.

### D2 — A checagem de boot é a **mesma** do worker, copiada; ela subsume a de resolução

A exploração propôs uma checagem nova, de **resolução do nome**. Com a D1
decidida, ela é desnecessária: o `TZ` do container define o fuso do processo,
então a checagem do worker — comparar `TimeProvider.LocalTimeZone.Id` com a env
`TZ` — **já cobre o nome que não resolve**. O XML doc de
`TimeZoneStartupValidation` registra isso verificado: *"`TZ` ausente/vazia/inválida
resolve para um fuso diferente do declarado (Achado 5/8) e falha aqui"*. Nome
digitado errado não resolve, o .NET cai para outro fuso, os identificadores
divergem, o boot reprova. Cobre também o prefixo POSIX `:`, pelo mesmo mecanismo.

**Copiada, não extraída.** É regra da casa que `apps/api` e `apps/workers` não
referenciem um ao outro. A duplicação é consciente e tem precedente direto: as
entidades de métrica já vivem duplicadas, com testes de espelho. O comentário de
cada cópia aponta a outra.

**Alternativa recusada:** inventar uma segunda forma de checagem só para
`apps/api`. Recusada porque duas checagens com o mesmo propósito e formas
diferentes divergem na primeira manutenção.

### D3 — `TimeProvider.System` registrado em `apps/api`

Primeiro do app. Precedente: `apps/workers/Program.cs:38`. É também o que a D2
precisa, já que a checagem lê o fuso **da instância registrada**, não de
`TimeProvider.System` direto — assim ela valida o que a aplicação vai usar.

**Alternativa recusada:** resolver a janela com `now()` no SQL. Recusada porque
uma janela relativa cujo "agora" nasce no banco **não é verificável de forma
determinística**: não há como afirmar em teste onde a janela termina.

**Custo nomeado:** a suíte de `apps/api` **não** referencia
`Microsoft.Extensions.TimeProvider.Testing`. O pacote entra com esta change.

### D4 — Uma rota, não 27 e não seis

`GET /insights/system` devolve o agregado inteiro da página.

**O motivo não é economia de rota.** É que janela, fuso, nulo e "medindo desde"
são **um contrato só**. Reparti-los entre N rotas cria N lugares onde o balde
pode discordar, e a divergência entre dois cards da mesma tela é exatamente o
tipo de defeito que não aparece em teste de rota isolada.

**Alternativas recusadas:** uma rota por métrica (27 contratos); uma por grupo do
catálogo (6 contratos, e os grupos não são independentes — M14 soma conversa e
embedding, que estão em regimes diferentes).

**Custo nomeado:** muitas consultas por requisição, sem cache por grupo.
**Não projetado** — convenção 18, e o volume de dev e do piloto não mede latência.

### D5 — Agregado, nunca linha bruta

Precedente: as duas rotas de resumo do `apps/inbox` e
`GetKnowledgeBaseIndexingSummary`, todas devolvendo objeto agregado.

Dois motivos, e o segundo é o que decide: linha bruta levaria milhares de linhas
ao navegador **e** agregação no cliente **colapsa nulo em zero**, que é
precisamente a distinção que a convenção 13 exige preservar aqui.

### D6 — A janela: texto na assinatura, ISO 8601, inclusiva nos dois lados, sem teto

Copiado do precedente de `apps/inbox`, com um item **não** copiado.

- **`from`/`to` como `string?`.** Com tipo de data na assinatura, valor
  malformado falha no *binding* e o framework devolve um `400` de forma diferente
  do resto da casa (`MessageSummaryEndpoints.cs:58-63`).
- **`AssumeUniversal | AdjustToUniversal`.** Papéis diferentes: o primeiro decide
  **qual instante** um valor sem deslocamento significa; o segundo evita que um
  `DateTimeOffset` com deslocamento ≠ 0 chegue ao Npgsql, que o **recusa**
  (`ArgumentException` → `500`).
- **Sem teto de intervalo, com gatilho — mas o gatilho é renomeado.** Lá a rota
  devolve um escalar; aqui, o agregado de 27 métricas. O gatilho herdado
  (*"recalibrar quando o primeiro deploy tiver volume real"*) vira: **a primeira
  requisição de período que passar de 2 s contra o piloto.**

**E o `AdjustToUniversal` ganha guarda obrigatório nesta change, por uma razão
que só vale agora.** O comentário de `SessionSummaryEndpoints.cs:100-103` chama o
defeito de *"o pior formato possível de bug de fuso"* porque ele **só aparece em
servidor cujo `TZ` não seja UTC**. `apps/api` é um servidor UTC hoje — e **deixa
de ser com a D1**. O defeito passa de inalcançável a alcançável exatamente aqui.

### D7 — O balde sai em SQL, e **nenhum índice de expressão**

`(<coluna> AT TIME ZONE '<nome>')::date` no `GROUP BY`, sobre as linhas já
restritas pela janela.

**Dois fatos medidos contra o banco real, e o segundo é o que decide:**

1. `timezone(text, timestamp with time zone)` é **`IMMUTABLE`** no pg18
   (`pg_proc.provolatile = 'i'`). Um índice de expressão sobre a conversão **é
   criável** — criado e removido na verificação. **Não usar é escolha.**
2. **Ele não é necessário.** O filtro de janela é um range sobre o instante e usa
   índice comum (`Bitmap Index Scan` verificado com `enable_seqscan=off`); o
   `AT TIME ZONE` agrupa o que já passou pelo filtro.

Usar índice de expressão **congelaria o nome do fuso no schema**, que é o oposto
de mantê-lo em configuração — e obrigaria uma migração a cada mudança de fuso.

### D8 — "Medindo desde" é um mapa de regimes, de configuração e não de `min()`

Dois regimes hoje: execução desde **22/09/2026 01:21**, embedding desde
**23/09/2026 01:18**, `America/Sao_Paulo`. Um texto único mentiria sobre um dos
dois.

**Mapa, e não dois campos**, porque a etapa 4 acrescenta um terceiro e o contrato
precisa absorvê-lo sem mudar de forma. Cada grupo de métricas declara a que
regime pertence.

**A fonte é o instante do deploy, constante configurada** — não `min(StartedAt)`.
O `min()` é *"quando a primeira linha chegou"*: se o sistema ficou ocioso depois
do deploy, ele marcaria como **não medido** um período que foi medido e estava
vazio, que é exatamente o que a convenção 13 proíbe aqui. O `min()` **continua
valendo como conferência**, que é o papel que o `02:8846` já lhe dá.

**Alternativa recusada:** derivar de `min()`. Recusada pelo motivo acima.
**Custo aceito:** o instante do regime vira configuração que alguém precisa
lembrar de escrever no deploy da próxima coleta — e por isso entra como tarefa da
change que criar o regime, nunca como descoberta de quem vier depois.

#### O modo de falha que a D8 cria, e que esta change nomeia sem resolver

**O que falha.** O instante de regime é **valor de um ambiente** — o piloto — num
arquivo de configuração que vale para **todos**. Num ambiente novo, ou em dev, a
configuração afirmaria *"medindo desde 22/09"* sobre um banco cuja coleta começou
depois — ou nunca começou.

**A consequência é exatamente o que a capability existe para impedir.** A rota
emite `0` para dias que estão **dentro do regime declarado** e **fora da medição
real** — afirmando medido onde não houve medição, que é a fronteira que
*"Período anterior ao regime é distinguível de período sem uso"* protege. É a
convenção 13 furada por configuração, não por código.

**Por que é silencioso.** Nada reprova, nada loga. O número sai **plausível**, e a
tela o apresenta como período medido e vazio — indistinguível do caso legítimo.

**A 4.5 não protege contra isso**, e a distinção importa: ela confere o declarado
contra `min(StartedAt)`, mas é **conferência única, feita nesta change, numa
máquina**. O próximo ambiente sobe sem conferir nada.

**Por que não se resolve aqui.** A saída natural é uma **checagem de boot** que
compare o declarado com o menor carimbo e reprove — e ela tem molde direto na
casa, a checagem de fuso da D2. Mas é escopo que esta change **não previu**, e
acrescentá-lo durante o apply seria decidir desenho no meio da implementação, que
é precisamente o que a convenção 1 recusa.

- **Gatilho:** o primeiro ambiente novo a subir a rota — **dev incluído**.
- **Posição:** item próprio no `02`. Decidir então entre três caminhos:
  conferência de boot, valor por ambiente, ou derivar com salvaguarda.

**E o aviso que chega a quem copiar o arquivo:** o comentário ao lado dos dois
valores diz que são os instantes do **piloto**, e que outro ambiente precisa dos
seus. É mitigação parcial e declarada como tal — comentário não reprova nada.

### D9 — M32 usa **o mesmo conjunto de estados** que o detector

O detector varre `{Submitted, Working}` (`NonTerminalTaskDetector.cs:40-44`).
O protocolo tem **nove** estados, dos quais **cinco** são não-terminais —
`Unspecified`, `InputRequired` e `AuthRequired` também. **Nenhum dos três foi
observado** nesta base (o banco de dev só tem `Completed`, `Failed`, `Submitted`,
`Working`, `Rejected`).

**Decidido: o mesmo conjunto de dois.** Não porque cinco esteja errado — está
mais certo —, mas porque as duas fontes precisam ser **comparadas em paralelo**
enquanto a `replicas-de-worker` decide, e conjuntos diferentes fariam elas
medirem números diferentes precisamente durante a comparação que as valida.

**Registrado como item com gatilho:** quando a `replicas-de-worker` fechar, M32
passa aos cinco não-terminais reais e o detector sai junto.

### D10 — O detector fica, e a condição de remoção é reescrita (convenção 9)

A condição está em três lugares
(`delegacao-diagnostico/proposal.md:69-72`, `metricas-execucao-coleta/design.md`
D11, `NonTerminalTaskDetectorService.cs:63-89`). A cláusula (a) — *"sai quando a
rota M32 cobrir as duas populações"* — **testa a coisa errada.**

| | detector | M32 |
|---|---|---|
| o que é | **série**, amostrada a cada 30 s, `Warning` por ciclo | **consulta sob demanda** |
| o que responde | quantas estavam não-terminais **ao longo do tempo** | quantas estão **agora** |
| reconstrói um pico passado? | sim, é a razão de existir | **não** |

`a2a_tasks` **não guarda histórico de status**: o carimbo é sobrescrito a cada
transição (`PostgresTaskStore.cs:36`), e o `02` já registra que *"o `submitted` é
destruído na transição"*. M32 não reconstrói o pico das 03:14 de terça — e o pico
é justamente o `C` de que a `replicas-de-worker` depende.

**Cobrir a mesma população não é cobrir o mesmo uso.** Esta change satisfaz a
cláusula (a) e **mesmo assim o detector fica**.

**A condição reescrita:**

> O detector sai quando o `C` de pico tiver sido **medido e a decisão tomada** — o
> instrumento sai com a decisão que ele existe para tomar. Ou, se a série
> precisar sobreviver à decisão, quando ela for **persistida em tabela**, que não
> é esta etapa e não está na fila.

A reescrita vai para o `02` e para o comentário do serviço. Os dois `design.md`
arquivados não são editados — eles registram o que se decidiu então.

### D11 — As lacunas do mapa são **nomeadas**, não corrigidas

Convenção 1: se a etapa de UI descobrir que precisa de dado que o backend não
serve, é achado a sequenciar — nunca backend de improviso dentro da change de
tela. Aqui o achado aparece **uma etapa antes da tela**, que é o melhor momento
possível, e a resposta é registrar com posição, não corrigir de improviso dentro
da change de rota.

A lacuna maior — **o motivo de uma recusa de `apps/api`** — é change de **coleta**,
não de agregação. Posição: **antes da etapa 4**, porque a tela mostra o motivo.

### D12 — A forma das consultas: join ao pai, porque três tabelas não têm tempo

**Fato de schema, conferido contra o banco:**

| tabela | coluna temporal própria |
|---|---|
| `task_executions` | `SubmittedAt?`, `StartedAt`, `LockAcquiredAt?`, `EndedAt?` |
| `knowledge_indexing_attempts` | `StartedAt`, `EndedAt` |
| `provider_calls` | **nenhuma** |
| `embedding_calls` | **nenhuma** |
| `delegation_outcomes` | só `LastObservedAt?` — a **última leitura**, não o instante do evento |

Toda janela sobre as três de baixo é **join ao pai**:

```
provider_calls       → task_executions.StartedAt            (FK, IX_provider_calls_TaskId)
embedding_calls      → knowledge_indexing_attempts.StartedAt  quando Purpose = Indexing
                     → task_executions.StartedAt              quando Purpose = Search
delegation_outcomes  → task_executions.StartedAt            por SourceTaskId
```

Todos caem em PK ou índice existente. **É por isso que a rota não pode ser "uma
consulta por tabela"** — e é por isso que M19 precisa de dois caminhos, não um.

### D13 — Nenhum índice entra, e D4 da exploração original fecha

**D4 pediu índice em `a2a_tasks.status_timestamp`. Está respondida, e não se
aplica como escrita.** O preditor seletivo de M32 é **`state`** — `Submitted` são
4 de 260 —, e o planejador escolheu `IX_a2a_tasks_state` mesmo com
`enable_seqscan=off`, mesmo depois de eu criar o composto `(state,
status_timestamp)` e rodar `ANALYZE`.

O índice certo, se algum dia for preciso, é o **composto**. Nenhuma medição o
justifica: 260 linhas em dev, e o piloto é menor nas tabelas de métrica. Criá-lo
agora seria **projetar tamanho** (convenção 18).

**Item no `02`, com gatilho:** remedir o plano de M32 contra o piloto depois do
primeiro mês da rota em produção. Junto dele, os sugeridos pela **forma** e não
por medição — `embedding_calls(Purpose)` e `(KnowledgeBaseId)`,
`task_executions(TerminalState)` e `(EndedAt)`.

### D14 — Autenticação: nada a declarar, e o erro possível é o inverso

`apps/api` fecha por `FallbackPolicy` (`Program.cs:66-70`). A validação de
startup varre **só** o caminho anônimo (`RouteAuthenticationExtensions.cs:22-24`),
e há teste afirmando que rota sem classificação **sobe**
(`RouteAuthenticationStartupFailureTests.cs:14-25`, `Assert.Null(exception)`).

**Pôr `/insights/system` na allowlist derrubaria o boot** — aquela lista é de
rotas que precisam estar anônimas. Nada a escrever, e o registro existe para que
ninguém "conserte" isso depois.

## O mapa das 27 contra as tabelas

`TE` = `task_executions` · `PC` = `provider_calls` · `DO` = `delegation_outcomes`
· `KIA` = `knowledge_indexing_attempts` · `EC` = `embedding_calls` ·
`AT` = `a2a_tasks`

| | métrica | fonte | estado |
|---|---|---|---|
| M1 | tasks de origem externa | `TE.Origin='External'` | tem |
| M2 | tasks executadas | `count(TE)` | tem |
| M6 | calor por dia da semana | `TE.StartedAt`, balde local | tem |
| M7 | calendário do período | `TE.StartedAt` | tem |
| M9 | dia da semana de pico | derivada de M6 | tem |
| M10 | série diária de tasks e tokens | `TE.StartedAt` + `PC` **por join** | tem |
| M11 | tokens de conversa | `PC.InputTokens`/`OutputTokens` | tem |
| M12 | entrada × saída | `PC` | tem |
| M13 | por agente | `PC` **por join** (`PC` não tem `AgentId`) | tem |
| M14 | por provedor, + total conversa+embedding | `PC.Provider` + `EC.Provider` | tem |
| M15 | por modelo | `PC.Model` | tem |
| M16a | ranking de modelos por tokens | `PC` | tem |
| M16b | ranking de modelos por nº de chamadas | `count(PC)` | tem |
| M17 | tokens por task, média e p95 | `PC` por `TaskId` | tem |
| M19 | tokens de embedding | `EC.InputTokens` | tem — **dois pais**, por `Purpose` |
| M21 | duração `submitted`→terminal | `TE.SubmittedAt`→`EndedAt` | **parcial** |
| M22 | tempo de fila `submitted`→`working` | `TE.SubmittedAt`→`StartedAt` | **parcial** |
| M23 | duração da chamada ao provedor | `PC.DurationMs` | tem |
| M24 | chamadas ao provedor por task | `count(PC)` por task | tem |
| M25 | tempo em tools | resíduo | **parcial** |
| M26 | profundidade de delegação | `TE.DelegationDepth` | tem |
| M27 | `failed` e `rejected` separados | `TE.TerminalState` | **parcial** |
| M28 | falhas por agente e por provedor/modelo | `TE` | **parcial** |
| M29 | motivo da falha | `TE.FailurePhase` | **parcial — e sem fonte para parte** |
| M30 | falhas de indexação | `KIA.Outcome`/`FailurePhase` | tem |
| M32 | tasks sem estado terminal | `TE.EndedAt` nulo + `AT` | tem — só o instante (D10) |
| M34 | delegações, par origem → destino | `DO` + `TE.Origin='Delegation'` | tem — janela **por join** |

**Vinte e duas têm fonte. Cinco são parciais.** As parcialidades, com a causa:

1. **M29 — o motivo das recusas de `apps/api` não tem fonte nenhuma.** Três
   causas distintas — agente inativo (`EnqueueingAgentHandler.cs:29`),
   `Provider`/`Model` nulos (`:38`), provedor não configurado (`:46`) — colapsam
   num único `Rejected`, e **não há coluna de motivo em lugar algum**. Conferido
   na linha real do banco: o payload traz `{"state": "TASK_STATE_REJECTED",
   "timestamp": ...}` e mais nada. **M29 está aprovada no protótipo.** É a
   repetição exata do padrão do card de delegação expirada.
2. **M27 e M28 — parciais pelo mesmo motivo.** Recusa de `apps/api` não gera linha
   em `TE`; `rejected` contado de lá **subconta**, e o que falta está em `AT`, que
   tem `agent_id` mas **não** tem provedor nem modelo.
3. **M21 e M22 — parciais por anulabilidade.** `TE.SubmittedAt` é nulo em
   reentrega. As duas ficam **indefinidas** nesses casos — nulo a preservar, não
   zero.
4. **M25 — o rótulo afirma mais do que o resíduo sabe.** Duração menos soma de
   `PC.DurationMs` contém tool **mais** espera de lock, MCP e busca de embedding.
   `LockAcquiredAt` permite descontar o lock; o resto não é separável. **Decidido:
   a métrica declara o que inclui** — o rótulo deixa de ser "tempo em tools" e
   passa a nomear o resíduo. É a convenção 13 aplicada a um nome de card, e a
   etapa 4 recebe isto como restrição de desenho.

## Risks / Trade-offs

Cada risco tem contraparte verificável (convenção 10).

- **[O balde sai em UTC e ninguém nota — 27,7% da série na barra errada]** →
  guarda que reprova contra balde em UTC, com instante noturno que muda de dia
  **e** de dia da semana. É o guarda central desta change.
- **[`TZ` chega ao `api` com prefixo POSIX `:` ou com nome inválido, e o balde sai
  em UTC silenciosamente]** → a checagem de boot da D2 reprova; cenário de spec e
  teste. O mecanismo está verificado no gêmeo de `apps/workers`.
- **[Limite com deslocamento ≠ 0 vira `500`]** → guarda com deslocamento ≠ 0
  explícito. O defeito só é **alcançável** depois da D1; antes dela o guarda
  ficaria verde com e sem a correção, que é a quinta forma da convenção 15.
- **[Nulo normalizado para zero em algum ponto do caminho]** → guarda **negativo**,
  afirmando que o valor não é `0` onde a fonte é nula. A asserção positiva
  passaria igual nos dois comportamentos.
- **[A série emite `0` antes do início do regime e some com a distinção
  "não medido" × "sem uso"]** → cenário com janela que começa antes do regime.
- **[Um guarda de agregação fica verde sobre banco vazio, sem nunca chegar à
  comparação]** → é a forma da vacuidade, já registrada na convenção 8. Todo
  cenário de agregação roda sobre **banco povoado**, e há teste que afirma essa
  precondição contando linhas antes de asserir.
- **[A checagem de boot é removida do `Program.cs` e a suíte fica verde]** → é a
  lacuna medida em `apps/workers`. Aqui **não** se repete: `apps/api` testa pela
  `WebApplicationFactory`, que roda a composição real do `Program.cs`. Há teste
  que afirma isso, e a diferença entre as duas suítes fica escrita no comentário.
- **[M32 e o detector medem conjuntos diferentes de estado durante a comparação
  paralela]** → D9 fixa o mesmo conjunto, com item de gatilho para trocar depois.
- **[A rota devolve linha bruta por descuido de mapeamento]** → o DTO não tem
  forma que aceite linha bruta, e o teste de contrato afirma a forma do corpo.
- **[Tempo de resposta inaceitável com volume real]** → **não mitigado, e
  declarado**: não há como medir com 260/13/10/1 linhas. O gatilho de teto de
  janela (D6) é a rede de segurança, e a remedição de índice (D13) é a resposta.

## Migration Plan

**Nenhuma migração de banco.** A change é somente leitura.

**O que o deploy exige, em ordem:**

1. `TZ` no `.env.prod` já existe (é o que `workers` consome). **Nada a criar** —
   só o compose passa a entregá-la também ao serviço `api`, com `:?`.
2. Os dois instantes de regime entram como configuração antes do deploy:
   **22/09/2026 01:21** (execução) e **23/09/2026 01:18** (embedding),
   `America/Sao_Paulo`.
3. Subir. **O boot reprova** se `TZ` estiver ausente, vazia, inválida ou com
   prefixo POSIX — que é o comportamento desejado, e é mudança que se nota:
   **antes desta change o serviço `api` subia sem `TZ`**.

**Rollback:** reverter a imagem. Sem estado novo, sem coluna nova, sem dado a
migrar de volta. O único efeito colateral do deploy é o fuso do processo `api`,
que volta a UTC no rollback — e nada em produção depende dele hoje.

**Conferência pós-deploy** (não é tarefa de código):

- a linha de boot com o fuso resolvido e o offset aparece no log do `api`;
- `GET /insights/system` com janela cobrindo uma noite conhecida devolve o dia
  local, e não o dia UTC — conferido contra uma consulta manual com
  `AT TIME ZONE`;
- os dois regimes aparecem separados, com as duas datas distintas.

## Projeção de tamanho (convenção 18 — décima quarta medição)

A projeção fica aqui; o fechamento **só compara**. Método, com as lições que a
série acumulou: três níveis separados; **guarda mais par**, nunca guarda;
unidade do xUnit é o **caso** (uma `[Theory]` de quatro conta quatro);
`git diff -w` para modificado; duplo classificado **por natureza, não por
arquivo**; e assinatura conferida compilando **todos os `.csproj`** — não há
`.sln` na raiz.

| nível | arquivos | linhas | como foi projetado |
|---|---|---|---|
| escrito à mão — `src` | 7 criados, 3 modificados | ~980 | endpoint, query, handler, DTO, janela, opções, checagem de boot; `Program.cs` e os dois `appsettings` |
| escrito à mão — teste | 2 criados, 1 modificado | ~620 | 2 guardas por requisito, contados **com o par**; o modificado é o `.csproj` que ganha o pacote de `TimeProvider` |
| duplo / infraestrutura de teste | 0 arquivos próprios | ~120 | semeadura de banco povoado; **mora dentro dos arquivos de caso**, e está contado aqui e não como caso |
| **gerado** | 0 | **0** | **não há migração** — e é a diferença de regime que mais separa esta projeção da etapa 2, onde gerado foi 45% do diff |
| `apps/workers` — só comentário | 1 modificado | ~30 | a condição de remoção reescrita (D10); nenhuma mudança de comportamento |
| documentação/config | 6 modificados | ~240 | `01`, `02`, `CHANGELOG`, 2 compose, `.env.example` |

**Totais projetados para o Escopo 1:** **9 arquivos criados**, **11
modificados**, **~1.990 linhas**, das quais **0 geradas**.

**O Escopo 2 fica fora da projeção, e a exclusão é metodológica, não descuido.**
A convenção 18 projeta **o trabalho que a change faz**. `openspec/config.yaml` é
o **12º arquivo tocado**, com **duas alterações de uma linha cada**, e nenhuma
delas entra na conta:

- a linha do `codegraph` **já estava escrita**, à mão, antes de a change existir —
  contá-la poluiria a série com trabalho que não foi feito aqui;
- a remoção da prosa solta **é** trabalho desta change, mas é **uma linha
  removida** num arquivo de configuração, sem código, sem teste e sem par de
  guarda. Projetá-la não mede nada: a série existe para calibrar a projeção de
  **código e teste**, e um item de uma linha entra como ruído em qualquer das
  três réguas.

**A tabela acima não muda por causa dela** — conferido, e dito aqui para que o
fechamento não procure a diferença. O fechamento compara os **9 criados e 11
modificados do Escopo 1**, e declara `openspec/config.yaml` ao lado, com as duas
alterações nomeadas e fora da conta.

**E ela também não muda pelo modo de falha dos instantes de regime** (o bloco ao
fim da D8) — dito pelo mesmo motivo. O que aquele achado acrescenta é **um
comentário** ao lado de dois valores que a 4.1 já ia escrever, e **um registro no
`02`**, que é arquivo já contado entre os 11 modificados. Nenhum arquivo novo,
nenhum guarda novo, nenhum caso novo: **9 criados, 11 modificados, ~1.990 linhas,
0 geradas, 34 casos** seguem valendo.

**Casos de teste: 34.** Derivação, com o par explícito — 17 requisitos/cenários
com guarda positivo e negativo, e onde a projeção previu um `[Fact]` sobre um
vocabulário fechado, previu uma `[Theory]`, contada pelo número de casos.

**Baselines — NÃO herdadas.** Os últimos registrados são `apps/workers` 386/386,
`apps/api` 351/351, `apps/inbox` 203/203. Todos serão **remedidos** com
`podman ps` em zero antes de qualquer comparação, e a medição dirá **qual alvo
rodou antes**, porque o flake de `apps/inbox` reprova sob contenção e passa
isolado.

## Open Questions

1. **O rótulo de M25.** A D11 decidiu que a métrica declara o que inclui; **o
   texto** é decisão da tela. *Gatilho:* o desenho da etapa 4. *Posição:* etapa 4,
   com esta restrição como entrada.
2. **O teto de janela.** Nasce ausente, com gatilho renomeado. *Gatilho:* a
   primeira requisição de período acima de 2 s contra o piloto. *Posição:* item no
   `02`, depois do deploy desta change.
3. **Índice composto `a2a_tasks(state, status_timestamp)` e os quatro sugeridos
   pela forma.** *Gatilho:* remedir o plano de M32 contra o piloto depois do
   primeiro mês da rota em produção. *Posição:* item no `02`.
4. **M32 passar aos cinco estados não-terminais reais.** *Gatilho:* a
   `replicas-de-worker` fechar. *Posição:* dentro dela, junto da remoção do
   detector.
5. **O motivo das recusas de `apps/api`.** *Gatilho:* cumprido — a lacuna está
   nomeada e M29 está aprovada. *Posição:* change de coleta, **antes da etapa 4**.
6. **Os protótipos não foram abertos.** *Gatilho:* `/design-login` rodado pelo
   dono. *Posição:* antes de a etapa 4 começar — e, se o protótipo contrariar
   este documento, a correção vem por convenção 9.
7. **O instante de regime é valor de um ambiente num arquivo que vale para
   todos** — e a falha é silenciosa: `0` onde deveria haver ausência. Descrita ao
   fim da D8. *Gatilho:* o primeiro ambiente novo a subir a rota, dev incluído.
   *Posição:* item próprio, entre checagem de boot, valor por ambiente, ou
   derivar com salvaguarda.
