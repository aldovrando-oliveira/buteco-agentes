## Why

`ContactSessionResolver.FindOrCreateSessionAsync` cria `Session` sem nenhuma
proteção de banco contra concorrência real, diferente de `Contact` e
`PendingDispatch`. Reproduzido 10 vezes contra Postgres real
(`/opsx:explore inbox-session-concorrencia`): 3 falhas em 10, com 8
chamadas concorrentes para o mesmo `Contact` novo gerando de 3 a 8
`Session` distintas. A consequência não fica só no banco — cada `Session`
duplicada vira seu próprio `PendingDispatch`, despachado de forma
independente pelo `DebounceSweepService` com seu próprio `ContextId` A2A, e
a resposta de saída é endereçada por `ContactExternalId` (não por
`Session`). Resultado: o mesmo chat do usuário final recebe N respostas
independentes, cada uma gerada sem o histórico das outras.

## What Changes

- Adiciona índice único parcial em `sessions` sobre `ContactId`, filtrado
  por `"ClosedAt" IS NULL` — no máximo uma sessão aberta por contato, e a
  concorrência que hoje duplica `Session` passa a colidir no mesmo índice
  e cair no mesmo padrão de `catch DbUpdateException` + detach + re-busca
  já usado por `Contact`/`PendingDispatch`.
- `ContactSessionResolver.FindOrCreateSessionAsync` passa a escrever
  `Session.ClosedAt` (coluna já existente, hoje sempre nula) no momento em
  que detecta que a sessão anterior expirou por inatividade, antes/junto de
  criar a nova.
- **BREAKING (migração de dados)**: a criação do índice único exige que,
  para cada `Contact`, exista no máximo uma `Session` com `ClosedAt IS
  NULL` no momento da migração. Como nenhuma linha jamais teve `ClosedAt`
  escrito, todo `Contact` com mais de uma `Session` histórica viola essa
  condição hoje. A migração inclui um passo de saneamento: para cada
  `ContactId` com múltiplas sessões abertas, fecha todas menos a mais
  recente por `StartedAt`, com `ClosedAt` = `StartedAt` da sessão seguinte.
  Medido em dev: 1 de 9 contatos, 2 sessões afetadas (ver design.md).
- Renomeia
  `ContactSessionResolverTests.FindOrCreateSessionAsync_ConcurrentCallsSamePair_ResolveToSameContactWithoutUnhandledException`
  para refletir que ele verifica dedup de `Contact`, não de `Session` — o
  nome atual é enganoso.
- Adiciona teste de concorrência direto sobre criação de `Session` (hoje a
  única cobertura é indireta, via contagem de `PendingDispatch`/`Message`
  no teste do orquestrador).

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `inbox-contact-session`: o Requirement "Fronteira de sessão por
  inatividade" passa a garantir unicidade de sessão aberta por contato sob
  concorrência real (hoje não garante nada — é só uma leitura seguida de
  escrita, sem proteção de banco), e passa a especificar que a sessão
  superada por timeout tem `ClosedAt` preenchido. O Requirement "Consulta
  de sessões de um contato" (`GET /contacts/{id}/sessions`) passa a
  documentar `closedAt` na resposta — o campo já existe no DTO
  (`SessionResponse.ClosedAt`) mas nunca foi especificado nem populado.

## Impact

- **apps/inbox** (único app afetado — sem referência cruzada a
  `apps/api`/`apps/workers`):
  - `Contacts/Entities/Session.cs` — novo método para fechar a sessão
    (`Close`/equivalente), mantendo o setter de `ClosedAt` privado.
  - `Contacts/ContactSessionResolver.cs` — escreve `ClosedAt` ao detectar
    expiração; trata violação do novo índice único com o mesmo padrão de
    `FindOrCreateContactAsync`.
  - `Infrastructure/AppDbContext.cs` — novo `HasIndex` parcial em
    `sessions`.
  - Nova migration EF Core: índice único + passo de saneamento de dados
    (`Sql(...)` ou `MigrationBuilder.Sql` para o fechamento retroativo).
  - `Contacts/Responses/SessionResponse.cs` — sem mudança de forma
    (`ClosedAt` já existe no record), só passa a vir preenchido às vezes.
  - Testes: `ContactSessionResolverTests.cs` (rename + novo teste de
    concorrência direto sobre `Session`), `InboundMessageOrchestratorTests.cs`
    (nenhuma mudança de asserção esperada — já cobre o sintoma
    indiretamente e deve passar 10/10 após a correção).
- **`02-HISTORICO_E_STATUS.md`**: fecha o item em aberto "Criação de
  `Session` sem índice único", corrige a nota da baseline (a corrida
  agora tem causa confirmada para o flake de 3/10 de
  `InboundMessageOrchestratorTests`), e atualiza (não fecha) o item
  "Estado da sessão não é exposto pela API" — o estado passa a ser
  derivável, expor continua fora de escopo.
- **`01-ARQUITETURA_E_CONVENCOES.md`**: revisar a frase sobre fronteira de
  sessão só por inatividade automática sem encerramento materializado —
  passa a estar parcialmente desatualizada.
- **Frontend**: sem impacto de fato. `ChannelSessionResponse` (consumida
  pela única tela de sessões do frontend, via `GET
  /channels/{id}/sessions`) não tem `ClosedAt` e não muda. O endpoint que
  ganha `ClosedAt` populado (`GET /contacts/{id}/sessions`) não tem
  nenhum consumidor no frontend (verificado: `apps/frontend` só chama
  `/channels/{id}/sessions` e `/sessions/{id}/messages`).
