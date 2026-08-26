## 1. Domínio — `Session` (apps/inbox)

- [x] 1.1 Em `Contacts/Entities/Session.cs`, adicionar método
      `Close(DateTimeOffset closedAt)` (setter de `ClosedAt` privado, mesmo
      estilo de `RegisterActivity()`).

## 2. Resolução de sessão — `ContactSessionResolver` (apps/inbox)

- [x] 2.1 Em `FindOrCreateSessionAsync`, adicionar `existing.ClosedAt ==
      null` ao `Where` da busca da sessão mais recente — o índice único
      passa a ser a única fonte de verdade sobre "aberta", a query não
      depende mais do invariante implícito "mais recente por StartedAt é
      sempre a aberta" (ver design.md, "A busca de sessão passa a filtrar
      ClosedAt IS NULL explicitamente").
- [x] 2.2 Ao detectar que a `Session` mais recente (agora, aberta) expirou
      pelo timeout, chamar `session.Close(now)` na entidade existente
      antes de criar e adicionar a nova `Session`, no mesmo
      `SaveChangesAsync`.
- [x] 2.3 Envolver esse `SaveChangesAsync` em
      `catch (DbUpdateException exception) when (IsUniqueViolation(exception))`
      (reaproveitar `IsUniqueViolation`, já `private static` na classe).
- [x] 2.4 No catch, fazer detach de **todas** as entidades da tentativa
      perdedora com estado `Added` ou `Modified` no `ChangeTracker` — não só
      `Added` (diferente de
      `InboundMessageOrchestrator.DetachAddedEntities()`, que não precisa
      cobrir `Modified` porque seu cenário não faz update na mesma
      transação que o insert).
- [x] 2.5 Após o detach, retentar a resolução **uma única vez** (sem loop
      nem contador) — re-executar a mesma busca filtrada por `ClosedAt ==
      null`, que resolve deterministicamente para a `Session` vencedora
      (ver design.md, "Por que uma retentativa basta"). Não
      propagar a exceção se a retentativa resolver.

## 3. Schema e migration (apps/inbox)

- [x] 3.1 Em `Infrastructure/AppDbContext.cs`, adicionar no
      `modelBuilder.Entity<Session>(...)`:
      `entity.HasIndex(session => session.ContactId).IsUnique().HasFilter("\"ClosedAt\" IS NULL");`
- [x] 3.2 Rodar a query de diagnóstico contra o Postgres de dev
      (`buteco_inbox`) para confirmar o número de contatos com múltiplas
      sessões abertas antes de gerar a migration (deve bater com o
      registrado em design.md — 1 contato, 2 sessões, salvo mudança desde
      então):
      ```sql
      SELECT count(*) FROM (
        SELECT "ContactId" FROM sessions WHERE "ClosedAt" IS NULL
        GROUP BY "ContactId" HAVING count(*) > 1
      ) sub;
      ```
- [x] 3.3 Gerar a migration (`dotnet ef migrations add
      AddUniqueOpenSessionIndex` a partir de `apps/inbox/src/Buteco.Inbox`).
- [x] 3.4 Editar o `Up()` gerado para inserir, **antes** do `CreateIndex`,
      o `migrationBuilder.Sql(...)` de saneamento (ver design.md — `UPDATE`
      com `LEAD() OVER (PARTITION BY "ContactId" ORDER BY "StartedAt")`,
      fechando toda `Session` exceto a mais recente por `ContactId`).
- [x] 3.5 Confirmar que `Down()` só remove o índice (sem tentar reverter o
      saneamento de dados).
- [x] 3.6 Rodar a migration contra o Postgres de dev e confirmar, por
      query direta, que (a) nenhum `ContactId` tem mais de uma `Session`
      com `ClosedAt` nulo, e (b) o índice foi criado.

## 4. Testes — concorrência direta sobre Session (apps/inbox)

- [x] 4.1 Em `ContactSessionResolverTests.cs`, renomear
      `FindOrCreateSessionAsync_ConcurrentCallsSamePair_ResolveToSameContactWithoutUnhandledException`
      para deixar explícito que ele verifica dedup de `Contact` (ex.
      `..._ResolveToSameContactWithoutDuplicatingContact`), sem mudar a
      asserção existente.
- [x] 4.2 Adicionar um teste novo em `ContactSessionResolverTests.cs` que
      dispare N chamadas concorrentes reais (mesmo padrão
      `Task.WhenAll`/escopo próprio por chamada) para o mesmo `(ChannelId,
      ExternalId)` novo e assine explicitamente `Assert.Single` sobre os
      `Session.Id` distintos retornados — não só sobre `ContactId` como o
      teste existente já cobre.
- [x] 4.3 Adicionar um teste que force o caminho de retry dos itens 2.4/2.5:
      duas chamadas concorrentes para um `Contact` com uma `Session`
      existente já expirada pelo timeout (mesmo mecanismo de backdate via
      `ExecuteSqlInterpolatedAsync` já usado em
      `FindOrCreateSessionAsync_AfterTimeout_CreatesNewSessionForSameContact`),
      verificando que só uma nova `Session` é criada e a antiga fica com
      `ClosedAt` preenchido.
- [x] 4.4 Rodar
      `InboundMessageOrchestratorTests.ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`
      10 vezes contra Postgres real e confirmar 10/10 (hoje 3/10 falha,
      ver proposal.md) — não é tarefa de escrever teste novo, é
      verificação da correção via o teste que já expõe o sintoma.
      **Confirmado: 10/10 aprovado** (antes da correção: 3/10 falhava).

## 5. Testes — contrato da API (apps/inbox)

- [x] 5.1 Em `ContactEndpointsTests.cs`, estender
      `GetContactSessions_ExistingContactWithSession_ReturnsSessions` (ou
      adicionar teste novo) para verificar `closedAt` nulo na `Session`
      aberta.
- [x] 5.2 Adicionar cenário com uma `Session` superada por timeout,
      verificando `closedAt` preenchido com o `StartedAt` da `Session`
      seguinte.

## 6. Antes do deploy em produção (apps/inbox)

- [ ] 6.1 Rodar a query de diagnóstico do item 3.2 contra o Postgres de
      produção antes de aplicar a migration lá, e registrar o número
      encontrado (design.md deixa em aberto — não é conhecido nesta
      proposta).
- [ ] 6.2 Aplicar a migration seguindo a sequência manual confirmada em
      design.md ("Deploy: por que a change não precisa ser dividida em
      duas"): parar o processo de `apps/inbox` → `dotnet ef database
      update` → subir o processo já com o código desta change. Não pular
      para "subir direto" — é essa ordem que garante que o código antigo
      nunca roda contra o índice novo.

## 7. Documentação

- [x] 7.1 Em `02-HISTORICO_E_STATUS.md`: fechar o item em aberto "Criação
      de `Session` sem índice único protegendo contra concorrência";
      corrigir a nota da baseline (a corrida agora tem causa confirmada
      para o flake de 3/10 de `InboundMessageOrchestratorTests`, deixa de
      ser "sem relação de causa confirmada"); atualizar (não fechar) o
      item "Estado da sessão não é exposto pela API" para registrar que o
      estado passa a ser derivável via `ClosedAt`, mas expor pela API
      continua fora de escopo.
- [x] 7.2 Em `01-ARQUITETURA_E_CONVENCOES.md`: revisar a descrição de
      fronteira de sessão só por inatividade automática sem encerramento
      materializado, refletindo que `ClosedAt` agora é escrito.
