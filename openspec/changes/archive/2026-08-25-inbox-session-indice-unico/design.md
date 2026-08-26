## Context

`ContactSessionResolver.FindOrCreateSessionAsync` (`apps/inbox/src/Buteco.Inbox/Contacts/ContactSessionResolver.cs:11-39`)
faz `SELECT` da `Session` mais recente do `Contact`, decide em memória se
expirou pelo `InactivityTimeout` configurado, e insere uma nova `Session`
se sim — sem nenhuma transação amarrando o `SELECT` ao `INSERT` e sem
índice único protegendo `ContactId`. `Contact` (`HasIndex(ChannelId,
ExternalId).IsUnique()`) e `PendingDispatch` (`HasIndex(SessionId)
.IsUnique().HasFilter("Status = 'Pending'")`) já resolvem o mesmo tipo de
problema com o mesmo idioma: índice único + `catch DbUpdateException`
filtrado por `PostgresErrorCodes.UniqueViolation` + detach da entidade
`Added` + re-busca. `Session` é a única das três sem proteção.

Reproduzido 10 vezes contra Postgres real
(`ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`,
8 chamadas concorrentes ao mesmo `Contact` novo): 3 falhas em 10, com 3,
4 e 8 `Session`/`PendingDispatch` distintas nas três falhas. Essa taxa é
idêntica à já registrada em `02-HISTORICO_E_STATUS.md` para o flake de
`InboundMessageOrchestratorTests`, até então sem causa confirmada — está
confirmada agora.

`Session.ClosedAt` (`DateTimeOffset?`) existe desde a migration original
(`20260809041610_AddContactSessionCrm.cs`) e já está exposto em
`SessionResponse.ClosedAt`, mas nenhum código em todo o repo jamais
escreve nele — foi reservado conscientemente em `inbox-crm-contato-sessao`
("Nenhum encerramento explícito de sessão... decisão já confirmada"),
sem nenhum mecanismo que o preencha.

Diagnóstico rodado contra o Postgres de dev (`buteco-agents_postgres_1`,
banco `buteco_inbox`, 2026-08-26): 10 sessões, 9 contatos distintos, **1
contato com 2 sessões**, iniciadas às 17:01 e 19:12 do mesmo dia — mais
de 2h de intervalo, muito acima do `InactivityTimeout` de dev (1h),
portanto uma fronteira de inatividade legítima, não um artefato da
corrida (que produziria `StartedAt` quase idênticos). Não há como saber
se produção tem mais casos — nenhum acesso a esse banco nesta
investigação.

**Modelo de deploy verificado (não assumido)**: não há, em nenhum lugar
do repositório, infraestrutura de rolling deploy para `apps/inbox` —
`docker-compose.yml` só sobe `postgres`/`rabbitmq`/`waha` (dependências
de dev), sem serviço próprio para nenhum dos quatro apps; não existe
manifesto de deploy (`k8s`, `docker-compose` de produção, CI/CD) em
lugar nenhum do repo. `README.md` documenta o único fluxo existente:
`apps/inbox` sobe via `dotnet run` em um processo único, e a migration é
um passo manual e separado (`dotnet ef database update`), rodado **antes**
de iniciar o processo — confirmado por `grep` em todo
`apps/inbox/src/Buteco.Inbox`: nenhuma chamada a `Migrate`/`MigrateAsync`
existe no código, então o schema nunca muda com o processo já rodando.
O item em aberto "Lock distribuído (Redis/Valkey)... Gatilho:
`apps/workers`/`apps/inbox` escalarem pra múltiplas réplicas de verdade
em produção" confirma que múltiplas réplicas é gatilho futuro, não
realidade atual. **Conclusão: instância única, parada durante a
migration — não existe janela em que código antigo (sem `catch` para a
violação do novo índice) rode contra o schema novo.** Ver "Deploy: por
que a change não precisa ser dividida em duas", abaixo.

## Goals / Non-Goals

**Goals:**
- Garantir, com proteção de banco (não só de aplicação), no máximo uma
  `Session` aberta por `Contact`.
- Seguir o mesmo idioma já estabelecido por `Contact`/`PendingDispatch`
  (índice único + catch + detach + re-busca), não introduzir um segundo
  paradigma de concorrência.
- Popular `Session.ClosedAt` de forma consistente com o índice único, sem
  quebrar nenhuma `Session` existente.

**Non-Goals:**
- Expor o estado da sessão (aberta/encerrada) pela API — o campo já
  existe em `SessionResponse`, mas documentá-lo/promovê-lo como contrato
  de produto é decisão separada.
- Encerramento explícito de sessão por operador ou agente.
- Qualquer mudança em `apps/api`/`apps/workers` ou no frontend — nenhum
  dos dois é afetado (frontend verificado: `ChannelSessionResponse`, a
  única forma de sessão que o frontend consome via `GET
  /channels/{id}/sessions`, não tem `ClosedAt` e não muda; o endpoint que
  ganha `ClosedAt` populado, `GET /contacts/{id}/sessions`, não tem
  nenhum consumidor no frontend).
- Testes de frontend: não se aplicam a esta change — não há nenhuma
  linha de frontend tocada (verificado por grep em todo `apps/frontend`
  antes de escrever esta proposta).

## Decisions

### Árvore de arquivos afetados

Nenhuma pasta nova — só arquivos existentes modificados, mais uma
migration nova no lugar já estabelecido:

```
apps/inbox/
├── src/Buteco.Inbox/
│   ├── Contacts/
│   │   ├── Entities/Session.cs              (modificado — novo Close())
│   │   └── ContactSessionResolver.cs        (modificado — escreve ClosedAt, trata violação)
│   └── Infrastructure/
│       ├── AppDbContext.cs                  (modificado — novo HasIndex)
│       └── Migrations/
│           └── <timestamp>_AddUniqueOpenSessionIndex.cs   (novo)
└── tests/Buteco.Inbox.Tests/
    ├── ContactSessionResolverTests.cs       (modificado — rename + novo teste de concorrência)
    └── ContactEndpointsTests.cs             (modificado — cobertura de GetContactSessionsQueryHandler)
```

### Índice único parcial em `sessions."ContactId"` filtrado por `"ClosedAt" IS NULL`

```csharp
entity.HasIndex(session => session.ContactId)
    .IsUnique()
    .HasFilter("\"ClosedAt\" IS NULL");
```

O predicado é determinístico e imutável por linha (depende só do valor
persistido, nunca de relógio ou configuração) — resolve exatamente a
objeção que travava um índice único sobre "sessão aberta por contato"
antes desta change: não havia coluna que representasse "aberta" sem
depender do timeout calculado em tempo de leitura.

### A busca de sessão passa a filtrar `ClosedAt IS NULL` explicitamente

```csharp
var session = await dbContext.Sessions
    .Where(existing => existing.ContactId == contact.Id && existing.ClosedAt == null)
    .OrderByDescending(existing => existing.StartedAt)
    .FirstOrDefaultAsync(cancellationToken);
```

Hoje, "a sessão mais recente por `StartedAt`" e "a sessão aberta"
coincidem sempre, porque a única coisa que fecha uma sessão é a criação
da seguinte — mas isso é um invariante implícito, não garantido pela
query. "Encerramento explícito de sessão" é item em aberto já registrado
(`02-HISTORICO_E_STATUS.md`) como próximo passo natural depois desta
change; no dia em que existir, uma sessão pode ser fechada **sem** que
uma nova exista ainda, e a busca por `OrderByDescending(StartedAt)` sem
o filtro encontraria essa sessão fechada, avaliaria seu timeout de
inatividade e, se ainda dentro da janela, **reutilizaria uma sessão
encerrada** silenciosamente. O filtro custa uma cláusula e torna o
índice único a única fonte de verdade sobre "aberta" — a query nunca mais
depende de esse invariante se manter true por acidente.

### `ContactSessionResolver` passa a escrever `ClosedAt` ao detectar expiração

`Session` ganha um método (`Close(DateTimeOffset closedAt)`, setter
privado, mesmo estilo de `RegisterActivity()`). Quando
`FindOrCreateSessionAsync` decide que a sessão mais recente expirou, chama
`session.Close(now)` na entidade já rastreada **antes** de adicionar a
nova `Session` — as duas mudanças (`UPDATE` da antiga, `INSERT` da nova)
vão no mesmo `SaveChangesAsync`, portanto na mesma transação implícita.

### Tratamento da violação — mesmo idioma, uma única retentativa (não um loop)

```csharp
try
{
    await dbContext.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateException exception) when (IsUniqueViolation(exception))
{
    // detach de TODAS as entidades da tentativa perdedora — Added (a nova
    // Session) E Modified (o Close() da antiga, que também é revertido
    // pelo rollback da transação falha) — diferente do
    // DetachAddedEntities() de InboundMessageOrchestrator, que só cobre
    // Added porque seu cenário não tem update na mesma transação.
    // re-busca: a mesma query com WHERE ClosedAt IS NULL já resolve para
    // a Session vencedora — é a única linha que satisfaz o filtro para
    // esse Contact, garantido pelo próprio índice único, não por
    // ordenação implícita.
}
```

**Por que uma retentativa basta, sem loop nem contador** (diferente de
`InboundMessageOrchestrator.TryAppendWithRetryAsync`, que usa até
`MaxAppendRetries`): o Postgres só libera a exceção de violação de
unicidade para o perdedor depois que o vencedor **já commitou** — a
segunda transação bloqueia na inserção até a primeira resolver, e só
levanta `unique_violation` se a primeira commitou (se a primeira tivesse
sido revertida, a segunda simplesmente prosseguiria). Então, na
retentativa, a busca sempre encontra a `Session` vencedora já persistida —
não há uma segunda rodada de corrida a resolver. Este é o mesmo raciocínio
já usado por `FindOrCreateContactAsync`, que também não tem loop.
Chamadas adicionais que cheguem depois (3ª, 4ª, ...) encontram a `Session`
vencedora na primeira leitura, sem nunca tentar inserir — não há explosão
combinatória de retentativas sob N concorrentes.

### Migração de dados — saneamento antes do índice, não depois

A migração cria o índice único **depois** de rodar um `UPDATE` que fecha,
para cada `ContactId`, toda `Session` exceto a mais recente por
`StartedAt`:

```sql
WITH ordered AS (
  SELECT "Id",
         LEAD("StartedAt") OVER (PARTITION BY "ContactId" ORDER BY "StartedAt") AS next_started_at
  FROM sessions
)
UPDATE sessions s
SET "ClosedAt" = o.next_started_at
FROM ordered o
WHERE s."Id" = o."Id" AND o.next_started_at IS NOT NULL AND s."ClosedAt" IS NULL;
```

`ClosedAt` da sessão superada recebe o `StartedAt` da sessão seguinte —
não o instante da migração — porque é o valor correto de domínio (foi
naquele momento que ela deixou de ser a sessão ativa), e porque rodar a
migração não deve depender de quando ela é aplicada. A sessão mais
recente de cada `ContactId` fica com `ClosedAt` nulo (não há linha
seguinte) — exatamente a que o índice único deve aceitar como aberta.

Isso não é limpeza de duplicatas do bug — é dado que sempre deveria ter
sido assim: `Session.ClosedAt` existe desde a criação da tabela para
representar isso, só nunca foi escrito. Rodar contra dev confirma o
efeito: 1 `Contact`, a `Session` de 17:01 recebe `ClosedAt` =
`StartedAt` da sessão de 19:12; a de 19:12 permanece aberta.

### Deploy: por que a change não precisa ser dividida em duas

A dúvida real, antes de aceitar o índice e o código novo na mesma
migration: o índice único parcial rejeita, a partir do momento em que
existe, qualquer segunda `Session` com `ClosedAt IS NULL` para o mesmo
`Contact` — inclusive as que o **código antigo** (sem `Close()`, sem
`catch`) tentaria inserir na rotação normal por inatividade. Se código
antigo pudesse rodar contra o schema novo, toda expiração de timeout
comum quebraria com `DbUpdateException` não tratada no caminho de
recepção de webhook — pior que o bug atual, que ao menos não lança
exceção.

Essa janela exigiria uma das duas coisas: múltiplas instâncias
com deploy faseado (algumas já no código novo, outras ainda não,
enquanto a migration já rodou), ou a migration rodando **antes** do
processo novo subir enquanto o processo antigo continua respondendo
tráfego. Nenhuma das duas é possível hoje: **instância única, sem
orquestração de deploy no repo, migration é passo manual e separado do
boot** (evidência no Context). O fluxo real é sempre: parar o processo →
`dotnet ef database update` → subir o processo já com o código novo — o
código antigo nunca vê o schema novo. Por isso a change permanece
**unificada** (índice, `Close()` e `catch` na mesma migration/PR), sem
necessidade de uma fase intermediária "só grava `ClosedAt`, sem índice".

Se isso mudar no futuro (múltiplas instâncias, deploy faseado — o
próprio gatilho do item "Lock distribuído" registrado no histórico),
qualquer migration futura que adicione uma restrição de banco sobre
comportamento que o código antigo não respeita precisa reavaliar esta
mesma pergunta — não é garantia permanente, é o estado verificado hoje.

### Alternativas rejeitadas (registradas aqui, não só na exploração/chat)

- **B — `pg_advisory_xact_lock(hashtext(contactId))`**: funciona e não
  exige mudança de schema, mas introduz um **segundo paradigma de
  concorrência dentro da mesma classe** — `FindOrCreateContactAsync` é
  otimista (insere e trata violação), `FindOrCreateSessionAsync` passaria
  a ser pessimista (lock antes de ler). Dois modelos para o mesmo tipo de
  problema no mesmo arquivo. Além disso, o único precedente de
  `pg_advisory_lock` no repo (`ConversationContextLock`, `apps/workers`)
  é lock de **sessão de conexão** (`pg_advisory_lock`/`pg_advisory_unlock`
  manual sobre uma conexão dedicada), não de transação
  (`pg_advisory_xact_lock`) — nem o mecanismo é o mesmo, adotá-lo aqui
  seria um terceiro padrão, não reaproveito de um existente.
- **C — índice único sobre chave derivada determinística**: não existe
  chave assim sem materializar estado — qualquer derivação (ex. bucket de
  tempo) seria arbitrária e desconectada do conceito real de sessão, não
  uma alternativa real.
- **D — upsert/`ON CONFLICT` com retry**: colapsa na alternativa A — é o
  mesmo mecanismo de `catch`+retry que `Contact`/`PendingDispatch` já
  usam, e precisa da mesma coluna materializada (`ClosedAt`) como alvo de
  conflito. Não é uma alternativa independente.
- **E — transação `SERIALIZABLE` sem novo índice**: funcionaria (Postgres
  detecta o conflito de leitura/escrita via `SIREAD` locks e aborta uma
  das transações com `serialization_failure`), mas não tem nenhum
  precedente no repo — todo o tratamento de concorrência existente é
  baseado em `unique_violation` (`Contact`, `PendingDispatch`) ou
  `DbUpdateConcurrencyException`/`xmin` (`PendingDispatch`, update).
  Adotar `SERIALIZABLE` exigiria um terceiro `SqlState` a tratar
  (`40001`), um terceiro idioma de retry, sem ganho sobre a alternativa A.

## Riscos / Trade-offs

- **[Risco] Produção pode ter uma distribuição de duplicatas diferente da
  observada em dev (1 contato, 2 sessões)** → **Mitigação**: a migração
  em si já trata qualquer quantidade de sessões supérfluas por contato
  (a janela `LEAD()` generaliza para N sessões, não só pares) — não há
  limite assumido. Rodar a mesma query de diagnóstico contra produção
  antes do deploy, registrar o número (tarefa em `tasks.md`), mas a
  migração não depende desse número para funcionar.
- **[Risco] Retry precisa descartar `Modified`, não só `Added`** — se a
  implementação copiar `InboundMessageOrchestrator.DetachAddedEntities()`
  sem adaptar, o `Close()` da sessão antiga fica preso como `Modified` no
  change tracker entre a retentativa, corrompendo o próximo
  `SaveChangesAsync` → **Mitigação**: escrito explicitamente acima; tarefa
  dedicada em `tasks.md` com teste que force esse caminho (retentativa
  após `Close()` + `Add()` na mesma chamada).
- **[Risco] Corrida reaparecer na fronteira de expiração** (duas chamadas
  decidem "expirou" ao mesmo tempo) → **Mitigação**: é exatamente o
  cenário que o índice único resolve — ambas tentam inserir com
  `ClosedAt IS NULL`, a segunda colide, mesmo caminho de retry. Coberto
  pelo teste de concorrência direto sobre `Session` (ver `tasks.md`).
- **[Risco] `ContactEndpointsTests.GetContactSessions_ExistingContactWithSession_ReturnsSessions`
  já existe e cobre o endpoint, mas só verifica `Id`/`ContextId` — nenhum
  teste hoje verifica `ClosedAt` na resposta** (checado por leitura
  direta do arquivo, corrigindo uma nota da exploração anterior que
  citava "sem teste" — havia teste, só não desse campo) → **Mitigação**:
  tarefa em `tasks.md` estende esse teste (ou adiciona um novo) para
  verificar `ClosedAt` nulo e preenchido nos dois cenários relevantes.
- **Trade-off aceito, não mitigado**: sessões já superadas por
  inatividade em produção **antes** desta migração nunca tiveram
  `LastActivityAt` recalculado para refletir exatamente o instante real
  do timeout — o `ClosedAt` retroativo usa `StartedAt` da sessão
  seguinte como proxy (só isso é conhecido), não o instante exato em que
  o timeout expirou. Diferença é limitada pela duração do timeout
  configurado; não há dado para fazer melhor que isso.
- **Trade-off aceito, não mitigado**: o saneamento (`LEAD()`) trata
  fronteiras legítimas (o caso observado em dev — horas de intervalo) e
  duplicatas geradas pela própria corrida (`StartedAt` quase idênticos,
  se produção tiver algum caso) da mesma forma — a sessão superada recebe
  `ClosedAt` ≈ seu próprio `StartedAt`, uma sessão que nasceu e foi
  fechada quase no mesmo instante. Inofensivo (o índice único passa a
  valer igual para as duas origens dali em diante) e não há como
  distinguir as duas origens só pelos dados hoje — não é um caso a
  tratar diferente, só a reconhecer que produção pode ter as duas.

## Migration Plan

1. `Session.cs`: adicionar `Close(DateTimeOffset closedAt)`.
2. `ContactSessionResolver.cs`: filtrar a busca por `ClosedAt == null`
   (ver Decisions); chamar `Close()` na sessão expirada antes de criar a
   nova; envolver o `SaveChangesAsync` final em
   `catch (DbUpdateException) when (IsUniqueViolation)`, com detach de
   `Added` + `Modified` e re-busca (sem loop).
3. `AppDbContext.cs`: adicionar o `HasIndex` parcial em `Session`.
4. Nova migration EF Core (`dotnet ef migrations add
   AddUniqueOpenSessionIndex`, mesmo padrão de nomes das anteriores —
   `AddContactSessionCrm`, `AddPendingDispatch`): `Up()` roda o `UPDATE`
   de saneamento (passo 1, via `migrationBuilder.Sql(...)`) antes de
   `CreateIndex` (passo 2, gerado pelo EF a partir do `HasIndex`); `Down()`
   só remove o índice — não há como reverter o saneamento de dados sem
   perder informação, e não deveria: os dados saneados são o estado
   correto.
5. Rodar a query de diagnóstico (mesma do Context) contra dev antes de
   aplicar, confirmar que o número não mudou desde a exploração.
6. Deploy: **sequência manual, confirmada — não há automação de boot no
   repo** (ver "Deploy: por que a change não precisa ser dividida em
   duas"): parar o processo de `apps/inbox` → `dotnet ef database update`
   (aplica saneamento + índice) → subir o processo já com o código desta
   change (`Close()` + `catch`). Nessa ordem, o código antigo nunca roda
   contra o índice novo — não há janela a proteger com feature flag, e
   nenhuma é adicionada.
7. Rollback: `Down()` remove o índice; o código de aplicação (chamada a
   `Close()`) pode continuar rodando sem o índice sem quebrar nada — só
   deixa de ser garantido por banco. Reverter o código do resolver junto
   se o rollback for por causa de um bug na lógica de `Close()`, não só
   no índice.

## Open Questions

- **Volume de duplicatas em produção é desconhecido** — não é pergunta
  de produto, é lacuna de acesso: precisa ser checado antes do deploy em
  produção (tarefa registrada em `tasks.md`), mas não bloqueia esta
  proposta nem muda o desenho, porque a migração já é genérica para
  qualquer volume.
- **Nenhuma outra em aberto** — alternativas B, C, D e E descartadas com
  justificativa própria em "Alternativas rejeitadas" (Decisions, acima);
  modelo de deploy verificado (não assumido) em "Deploy: por que a change
  não precisa ser dividida em duas"; não há decisão de desenho pendente.
