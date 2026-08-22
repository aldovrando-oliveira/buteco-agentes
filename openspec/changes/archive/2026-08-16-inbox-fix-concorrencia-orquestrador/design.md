## Context

`InboundMessageOrchestrator.ReceiveMessageAsync` (`apps/inbox/src/Buteco.Inbox/Orchestration/InboundMessageOrchestrator.cs`)
bufferiza mensagens recebidas numa `PendingDispatch` `Pending` por
`Session`, criada por `inbox-orquestrador-debounce`. `PendingDispatch` é
protegida por concorrência otimista via `xmin` do Postgres (shadow
property `Version`, `.IsRowVersion()`, `AppDbContext.cs:124` — ver
Decisão 5 do design.md de `inbox-orquestrador-debounce`), mecanismo hoje
usado apenas pelo claim `Pending -> Dispatching` de
`DebounceSweepService.TryDispatchAsync`.

### Diagnóstico: hipótese original investigada e refutada

A investigação partiu da hipótese de que `InboundMessageOrchestrator`
reproduzia o bug já corrigido em `ContactSessionResolver` durante
`inbox-crm-contato-sessao` (task 2.3, Decisão 7 daquele design.md): falta
de detach explícito da entidade malsucedida do change tracker após uma
violação de unique constraint na criação, causando uma segunda violação
não capturada no `SaveChangesAsync` seguinte no mesmo `DbContext`.

Essa hipótese está **refutada**. `InboundMessageOrchestrator.cs:45` já
contém o detach correto:

```csharp
catch (DbUpdateException exception) when (IsUniqueViolation(exception))
{
    dbContext.Entry(pendingDispatch).State = EntityState.Detached;

    var existing = await FindPendingAsync(session.Id, cancellationToken)
        ?? throw new InvalidOperationException(...);
    existing.AppendMessage(text, receivedAt);
    await dbContext.SaveChangesAsync(cancellationToken);
}
```

Rodando `ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`
isolado (`dotnet test --filter "FullyQualifiedName~ReceiveMessageAsync_ConcurrentCallsSameSession"`,
via Testcontainers usando o socket do podman como backend, Docker não
instalado neste ambiente), 5 execuções consecutivas produziram o mesmo
resultado determinístico:

```
Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException : The database
operation was expected to affect 1 row(s), but actually affected 0
row(s); data may have been modified or deleted since entities were
loaded.
   at ...StateManager.SaveChangesAsync(...)
   at Buteco.Inbox.Orchestration.InboundMessageOrchestrator.ReceiveMessageAsync(...)
     in InboundMessageOrchestrator.cs:line 50
```

A linha 50 é o `SaveChangesAsync` **dentro** do bloco catch acima — ou
seja, a exceção não é uma segunda violação de unicidade (esse caminho já
funciona corretamente), e sim uma classe de conflito diferente:
`DbUpdateConcurrencyException` do token `xmin`.

### Causa raiz confirmada

O teste dispara 8 chamadas concorrentes de `ReceiveMessageAsync` para a
mesma `Session` (`Task.WhenAll` de 8 tasks, cada uma com seu próprio
`DbContext` via `factory.Services.CreateScope()`). Sequência observada:

1. A primeira chamada a executar `SaveChangesAsync` (linha 36) cria a
   `PendingDispatch` `Pending` com sucesso.
2. As outras 7 colidem na violação de unicidade, entram no bloco catch,
   fazem o detach (correto), e re-buscam a linha existente via
   `FindPendingAsync` — **com tracking**, não `AsNoTracking`.
3. Cada uma dessas 7 chama `AppendMessage` localmente sobre a entidade
   recém-buscada e tenta `SaveChangesAsync` (linha 50). Esse
   `SaveChangesAsync` gera um `UPDATE ... WHERE "Id" = @id AND xmin =
   @originalXmin`. Como várias dessas 7 chamadas buscaram a linha *antes*
   de qualquer uma delas commitar sua própria atualização, mais de uma
   carrega o **mesmo** `xmin` original. Apenas a primeira a executar o
   `UPDATE` o vence; todas as demais têm `xmin` obsoleto, o Postgres
   retorna 0 linhas afetadas, e o EF Core traduz isso em
   `DbUpdateConcurrencyException`.
4. Essa exceção não é capturada em nenhum lugar — a chamada falha, e a
   mensagem do usuário não chega a ser bufferizada.

O mesmo problema existe, de forma latente, no caminho principal (linhas
23-28: quando `FindPendingAsync` já encontra a `PendingDispatch` na
primeira leitura, sem passar pela violação de unicidade) — esse caminho
também faz um `AppendMessage` + `SaveChangesAsync` direto, sem qualquer
tratamento de `DbUpdateConcurrencyException`. O teste atual não o exercita
diretamente (todas as 8 chamadas partem de "nenhuma `PendingDispatch`
existe ainda"), mas o mesmo padrão de corrida se aplica a qualquer
sequência onde duas chamadas leem a mesma `PendingDispatch` `Pending` já
existente antes de uma delas salvar.

Entidade confirmada por leitura direta do código: `PendingDispatch`
(`Orchestration/Entities/PendingDispatch.cs`) — não `Contact` nem
`Session`, que são resolvidos por `ContactSessionResolver` antes deste
ponto e não fazem parte da corrida que este teste exercita.

## Goals / Non-Goals

**Goals:**
- Fazer `ReceiveMessageAsync` sobreviver a chamadas concorrentes para a
  mesma `Session` sem perder nenhuma mensagem, cumprindo o Requirement
  já existente "Debounce agrupa mensagens dentro da janela configurada"
  (`inbox-message-orchestration`) sob concorrência real.
- Cobrir os dois caminhos de append (direto e retry pós-unique-violation)
  com o mesmo mecanismo de retry para `DbUpdateConcurrencyException`.

**Non-Goals:**
- Nenhuma mudança de comportamento especificado, Requirement ou Scenario
  — fix de bug dentro do que `inbox-orquestrador-debounce` já especifica.
- Não introduz lock explícito (`pg_advisory_lock`, `SELECT ... FOR
  UPDATE`) — mantém o mecanismo `xmin` já estabelecido pela Decisão 5 de
  `inbox-orquestrador-debounce`, só adiciona o retry que faltava do lado
  do append.
- Não resolve concorrência entre `ReceiveMessageAsync` e o claim do
  `DebounceSweepService` (`Pending -> Dispatching`) — esse caso já é
  coberto pela Decisão 5 original: se o `AppendMessage` perder a corrida
  porque a linha já foi movida para `Dispatching`, o retry deste design
  vai recarregar, ver `Status != Pending`, e cair no mesmo caminho de
  criar uma nova `PendingDispatch` (ver Decisão abaixo) — comportamento
  já correto e non-goal alterar.

## Decisions

### Decisão 1: Retry loop com reload, não catch-and-bail nem retry único

O padrão de `DebounceSweepService.TryDispatchAsync` (linhas 72-82) já
trata `DbUpdateConcurrencyException` no seu próprio `SaveChangesAsync` do
claim — mas com semântica de **bail-and-return**: "outra instância já
reivindicou esta linha, não há mais nada a fazer, sigo para a próxima
candidata". Essa semântica não se aplica ao append: perder a corrida do
`AppendMessage` não significa que não há mais nada a fazer — a mensagem
do usuário ainda precisa ser persistida em algum `PendingDispatch`.
Descartar a mensagem silenciosamente reproduziria exatamente a classe de
perda de mensagem que `inbox-orquestrador-debounce` (Decisão 9 daquele
design.md) trata como inaceitável.

O retry único já existente no bloco catch de violação de unicidade
(linhas 44-51) também não é suficiente: sob N chamadas concorrentes, o
retry precisa poder repetir até N-1 vezes no pior caso (cada chamada só
avança quando vence um `UPDATE`), não uma vez. O teste com 8 chamadas
concorrentes demonstra isso — um retry único ainda falha
deterministicamente.

**Decisão**: envolver o append (`AppendMessage` + `SaveChangesAsync`) num
loop de retry limitado. A cada `DbUpdateConcurrencyException`:
1. Detachar a entidade do change tracker e rebuscá-la via
   `FindPendingAsync` (mesma query já usada em todo o resto do arquivo) —
   traz o `Messages` e `xmin` atuais, incluindo mensagens já anexadas por
   chamadas concorrentes que venceram rodadas anteriores.
2. Se `FindPendingAsync` não encontrar mais nenhuma `PendingDispatch`
   `Pending` para a Session (reivindicada pelo `DebounceSweepService` ou
   removida entre a leitura original e a rebusca), tratar como "não há
   mais `PendingDispatch` `Pending` para esta Session" e cair no mesmo
   caminho de criação de uma nova `PendingDispatch` já usado quando
   `FindPendingAsync` retorna null na primeira leitura — evita duplicar a
   lógica de criação.
3. Caso contrário, reaplicar `AppendMessage(text, receivedAt)` sobre a
   entidade rebuscada e tentar `SaveChangesAsync` novamente.
4. Repetir até suceder ou esgotar um limite de tentativas
   (`MaxAppendRetries`, constante local — ver Alternativas).

Isso substitui tanto o `SaveChangesAsync` direto do caminho principal
(linhas 23-28) quanto o `SaveChangesAsync` do bloco catch (linhas 44-51)
por uma única função auxiliar compartilhada, eliminando a duplicação
entre os dois caminhos.

**Correção durante a implementação: detach + rebusca via `FindPendingAsync`,
não `ReloadAsync` — achado do teste 2.1, não previsto neste design
originalmente.** A primeira implementação seguiu literalmente o texto
acima na sua versão original, usando
`dbContext.Entry(pendingDispatch).ReloadAsync(cancellationToken)` para o
passo 1. O teste
`ReceiveMessageAsync_ConcurrentCallsExistingPendingDispatch_AppendsAllMessagesWithoutLoss`
(tasks.md 2.1 — 8 chamadas concorrentes contra uma `PendingDispatch`
pré-existente, todas passando pelo caminho direto de append) expôs uma
perda de mensagem silenciosa e intermitente com essa implementação: 15
execuções isoladas produziram falhas em ~80% delas
(`Assert.Equal(9, actual: 8)`), sem nenhuma exceção — nem
`DbUpdateConcurrencyException` propagada, nem qualquer outro erro,
apenas uma mensagem que nunca chegava a ser persistida.

Diagnóstico: `Messages` é uma coleção owned mapeada via `ToJson()`
(`AppDbContext.cs:101`), rastreada pelo change tracker do EF Core por
snapshot (comparação estrutural entre um valor "original" e o valor
"current" da propriedade). A hipótese confirmada empiricamente (a falha
desaparecia de forma consistente ao trocar `ReloadAsync` por detach +
rebusca, e reaparecia ao reverter) é que
`dbContext.Entry(pendingDispatch).ReloadAsync(...)`, para esta coleção
owned/JSON específica, deixava o snapshot "original" usado pelo change
tracker apontando para a MESMA instância de `List<BufferedMessage>`
atribuída à propriedade `Messages` (em vez de uma cópia independente).
Como `PendingDispatch.AppendMessage` muta a lista em vigor via
`Messages.Add(...)` (não substitui a referência), a chamada seguinte de
`AppendMessage` após o `ReloadAsync` mutava as DUAS "cópias" ao mesmo
tempo (original e current eram o mesmo objeto) — o `DetectChanges()`
subsequente não via nenhuma diferença entre original e current para
`Messages`, então o `SaveChangesAsync` seguinte não incluía a coluna JSON
no `UPDATE`. Como `LastMessageAt` (uma propriedade escalar comum,
sempre corretamente rastreada) TINHA mudado, o `UPDATE` ainda afetava 1
linha de verdade — `SaveChangesAsync` retornava sucesso normalmente, sem
lançar nenhuma exceção, mas a mensagem nunca era gravada.

Reproduzida de forma isolada e confirmada por eliminação: qualquer
instrumentação de diagnóstico adicionada ao laço de retry (até um
simples `Console.WriteLine` ou um `ConcurrentQueue<string>.Enqueue`)
alterava o timing o suficiente para praticamente eliminar a
reprodução (de ~80% de falha para 0/15) — evidência de que a janela de
corrida é extremamente estreita, e que qualquer solução baseada nessa
mesma "reload em vigor" seria uma correção frágil, difícil de verificar
de forma confiável mesmo em produção. Por isso a correção não foi
apenas "consertar o reload", mas **trocar o mecanismo inteiro**: em vez
de `ReloadAsync` na entidade já rastreada, o passo 1 detacha a entidade
do change tracker (`dbContext.Entry(pendingDispatch).State =
EntityState.Detached`) e a rebusca do zero via `FindPendingAsync` — o
mesmo padrão de query LINQ já usado em todo o resto do arquivo (a
primeira leitura em `ReceiveForSessionAsync`, e a rebusca pós-violação-
de-unicidade da Decisão original) — garantindo uma materialização nova
via EF Core, sem qualquer aliasing entre snapshot original e valor
current. Com essa troca, 15/15 execuções passaram de forma
determinística (task 2.2).

**Alternativas descartadas**:
- *Catch-and-bail (copiar o padrão do `DebounceSweepService` tal qual)*
  — rejeitada: perderia a mensagem do usuário, inaceitável (mesmo
  raciocínio da Decisão 9 de `inbox-orquestrador-debounce`).
- *Lock explícito (`pg_advisory_lock` por `SessionId`)* — rejeitada:
  introduziria um mecanismo de concorrência novo só para este caminho,
  divergindo do padrão já estabelecido (`xmin`) sem necessidade — o
  volume de contenção esperado (mensagens de um único usuário digitando)
  não justifica a complexidade adicional de gerenciar um lock mantido
  aberto.
- *Retry sem limite (loop `while (true)`)* — rejeitada: sob uma falha
  real e persistente (não uma corrida transitória), um loop sem limite
  trava a requisição indefinidamente em vez de falhar de forma visível;
  um limite finito com exceção ao esgotar preserva a mesma garantia de
  liveness prática (a corrida real do teste é resolvida em poucas
  rodadas) sem esse risco.
- *Manter `ReloadAsync` e investigar uma forma de forçar `Messages` como
  modificado manualmente (ex. marcar a propriedade `IsModified = true`
  após o reload)* — rejeitada: dependeria de comportamento interno do
  change tracker do EF Core para coleções owned/JSON que não está
  documentado de forma confiável para esta versão do provider, e o
  próprio ato de investigar esse comportamento (via instrumentação)
  demonstrou alterar o timing da corrida — uma correção mais simples e
  auditável (rebusca completa, sem depender de nenhum comportamento
  interno de tracking incremental) foi preferida.

### Decisão 2: Limite de tentativas — `MaxAppendRetries = 10`

Com N chamadas concorrentes para a mesma `Session`, o pior caso teórico
de serialização exige até N-1 rodadas de retry para a última chamada
avançar (cada rodada, exatamente uma chamada vence o `UPDATE`). O teste
de referência usa 8 chamadas concorrentes; um limite de 10 cobre esse
caso com margem sem ser um valor "mágico" desproporcional. Diferente de
`DebounceOptions.MaxDispatchAttempts` (que modela retries de falha de
rede entre varreduras, um conceito de negócio com backoff ao longo do
tempo), este limite é uma proteção de robustez local dentro de uma única
chamada, sem necessidade de ser configurável — não é exposto em
`Options`.

## Risks / Trade-offs

- **[Risco] Volume de contenção real maior que o do teste (dezenas de
  mensagens quase simultâneas da mesma Session)** → `MaxAppendRetries`
  poderia se esgotar, propagando exceção para o chamador do adapter de
  canal. Mitigação: aceito conscientemente — esse volume não corresponde
  ao padrão de uso esperado (mensagens humanas digitadas em sequência);
  se o comportamento em produção mostrar o contrário, ajustar o limite
  vira uma mudança pontual futura, não uma mudança de design.
- **[Risco] A rebusca (detach + `FindPendingAsync`) traz `Status` diferente
  de `Pending` porque o `DebounceSweepService` reivindicou a linha entre a
  leitura original e a rebusca** → tratado explicitamente na Decisão 1,
  passo 2: cai no caminho de criação de uma nova `PendingDispatch`, mesmo
  comportamento já usado quando não existe nenhuma `PendingDispatch`
  `Pending`.
- **[Trade-off] Duas responsabilidades (criar vs. anexar) agora
  compartilham um caminho de retry único** → aumenta levemente a
  complexidade do método em troca de eliminar a duplicação de lógica de
  append entre o caminho direto e o caminho pós-unique-violation, e de
  garantir que ambos ganhem a mesma robustez.
