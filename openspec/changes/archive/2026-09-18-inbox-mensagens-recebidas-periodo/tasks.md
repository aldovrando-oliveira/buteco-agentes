## 1. Baseline, antes de tocar em código

- [x] 1.1 Rodar a suíte de `apps/inbox` **com a árvore ainda limpa** e registrar o
      número aqui, no formato `N/N`. É a baseline da convenção 19: sem ela,
      qualquer reprovação no fechamento vira discussão sobre se é "pré-existente"
      em vez de comparação número contra número.

      **BASELINE MEDIDA: `183/183`** — `dotnet test` sobre
      `Buteco.Inbox.Tests.csproj`, árvore limpa (só os artefatos desta change,
      não rastreados), 19 s, 0 falhas, 0 ignorados, em 2026-09-18.

      **A expectativa bateu exatamente — `183` estático = `183` executado.** Sem
      divergência a investigar, ao contrário da change anterior (que divergiu em
      1 e exigiu investigação antes de começar). O método ancorado, corrigido lá,
      acertou de primeira aqui.

      **Expectativa declarada antes de medir (convenção 22): `183` casos.**
      Contagem **estática**, já feita em 2026-09-18 com a árvore limpa:

      ```
      grep -rhE '^\s*\[Fact\]\s*$' apps/inbox/tests --include='*.cs' | wc -l   →  183
      grep -rhE '^\s*\[Theory\]'    apps/inbox/tests --include='*.cs' | wc -l   →    0
      ```

      Sem `[Theory]`/`[InlineData]` no projeto, então atributo conta igual a caso
      executado. **`183` também é exatamente o número medido no fechamento de
      `inbox-sessoes-por-periodo`** (tasks.md, 6.1) — os dois batem, o que é a
      confirmação mais barata de que nada entrou na suíte entre as duas changes.

      **O método de contagem é o ANCORADO (`^\s*\[Fact\]\s*$`), nunca a substring
      solta — e a lição da vez passada já se pagou de novo.** Na change anterior a
      substring `\[Fact\]` errou por 1 (uma menção em prosa dentro de um
      comentário). Hoje ela erra por **6**:

      ```
      grep -rho '\[Fact\]' … | wc -l   →  189      ← 6 a mais, todas menções em prosa
      ```

      As seis são comentários que citam `[Fact]` em texto — uma em
      `ContactEndpointsTests.cs:25` e **cinco** em `SessionSummaryEndpointsTests.cs`
      (`:22`, `:201`, `:223`, `:291`, `:308`), escritas pela própria change
      anterior. **O erro do método cresce junto com a documentação da suíte**, que
      é o argumento para nunca voltar à substring: quanto melhor comentado o
      código, mais errada ela fica.

      Divergência entre o número estático (`183`) e o executado é achado a
      investigar **antes** de começar, não depois.

      A suíte é de integração com Testcontainers (convenção 5) e precisa do
      runtime de container ativo nesta máquina — sem ele a suíte inteira reprova,
      e isso não é baseline, é ambiente.

      **Ambiente usado, idêntico em todas as execuções desta change** (Podman,
      não Docker Desktop; `DOCKER_HOST` não vem do shell e precisa ser apontado
      para o socket da máquina Podman):

      ```
      export DOCKER_HOST="unix://$(podman machine inspect \
        --format '{{.ConnectionInfo.PodmanSocket.Path}}')"
      export TESTCONTAINERS_RYUK_DISABLED=true
      ```

## 2. Consulta agregada (`COUNT` no banco)

- [x] 2.1 `Messages/Responses/MessagePeriodSummaryResponse.cs` — record
      `public sealed record MessagePeriodSummaryResponse(int InboundCount)`.
      Sem `FromEntity`: não há entidade de origem, é agregação. O campo é
      `InboundCount`, **não** `ReceivedCount` nem `Count` (design.md, D4) — o
      termo "recebidas" fica para o rótulo de tela, que ainda não existe.

- [x] 2.2 `Messages/Queries/GetMessagePeriodSummary/GetMessagePeriodSummaryQuery.cs`
      — record de query recebendo `DateTimeOffset From, DateTimeOffset To` **já
      parseados**. O parse e a validação ficam no endpoint (D8); a query nunca
      recebe `string`.

- [x] 2.3 `…/GetMessagePeriodSummaryQueryHandler.cs` — `CountAsync` sobre
      `dbContext.Messages.AsNoTracking()` com **os dois predicados**:
      `Direction == MessageDirection.Inbound` e
      `OccurredAt >= From && OccurredAt <= To`.

      **Nunca `Select().ToList().Count()`** — sai com o resultado certo e o custo
      errado, e passa despercebido por isso (D1).

      Comentário do handler registra o que **não** se copia do vizinho: aqui não
      há tabela de três definições porque `Message` tem um campo temporal só e ele
      é write-once (D3), e a razão de não haver índice é **outra** — existe
      `IX_messages_SessionId_OccurredAt`, mas com `SessionId` como coluna líder,
      então não serve a esta consulta (D10). Escrever "não há índice em
      `OccurredAt`" seria falso.

- [x] 2.4 `apps/inbox/tests/Buteco.Inbox.Tests/MessageSummaryEndpointsTests.cs` —
      arquivo novo, `IClassFixture<InboxFactoryFixture>`, mais os helpers de que
      todo teste desta classe depende. Reusar os de `MessagePersistenceTests`
      (mesmo fixture): `CreateChannelAsync`, `ResolveSessionIdAsync` e
      `ReceiveAsync` — este último já aceita `externalMessageId` opcional, que é o
      que 4.6 precisa.

      **A retrodatação é por `UPDATE` em `messages."OccurredAt"`, não por passar
      um `receivedAt` passado ao orquestrador — e isso é decisão, não estilo.**
      `IInboundMessageOrchestrator.ReceiveMessageAsync` aceita `receivedAt` como
      parâmetro, então retrodatar por ali *parece* mais limpo. Mas o mesmo valor
      alimenta `PendingDispatch.LastMessageAt` (`InboundMessageOrchestrator.cs:59`),
      e `DebounceSweepService` **roda de verdade** nestes testes
      (`Program.cs:115`, hosted service que a `WebApplicationFactory` inicia):
      com `Window` de 10 s e `SweepInterval` de 2 s (`appsettings.json:17-21`),
      uma mensagem datada de anos atrás fica elegível **no próximo sweep**, e o
      dispatch dispara no meio do teste.

      Criar em "agora" e retrodatar depois mantém `LastMessageAt` dentro da janela
      de 10 s, e o sweep não alcança o teste. É o mesmo mecanismo que
      `SessionSummaryEndpointsTests.cs:404` e `ContactSessionResolverTests.cs:57-62`
      já usam, pelo mesmo motivo de fundo: o campo é `private set` e não há como
      escolher o instante pela API — que é justamente a propriedade de D3.

      **A ordem importa e preserva a dedup**: a mensagem é criada pelo caminho
      real (orquestrador, com dedup), e **só então** retrodatada. 4.6 continua
      exercitando a dedup de verdade.

      **E cada `[Fact]` desta classe usa uma janela própria, distante e disjunta**
      (anos diferentes, bem no passado). A rota conta o sistema **inteiro** e o
      banco é compartilhado por todos os testes da classe: sem janelas disjuntas,
      um teste contaria as mensagens de outro. Os demais testes da suíte criam
      mensagens em "agora", então nenhuma janela no passado os alcança
      (design.md, Risks).

## 3. Endpoint e mapeamento

- [x] 3.1 `Messages/Endpoints/MessageSummaryEndpoints.cs` —
      `MapGet("/messages/summary", …)` com `from`/`to` como **`string?`** na
      assinatura (D8), parse por
      `DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, …)`,
      e `Results<Ok<MessagePeriodSummaryResponse>, ValidationProblem>`.

      Validação acumulada num único `Dictionary<string, string[]>`, chave em
      camelCase e mensagem em pt-BR: dois parâmetros defeituosos saem numa
      resposta só. A checagem `to < from` roda **depois** de os dois parsearem.

      **O comentário de rota registra que aqui se INAUGURA o prefixo `/messages`
      de nível superior** (D2) — conclusão **invertida** em relação ao comentário
      equivalente de `SessionSummaryEndpoints.cs`, que dizia que `/sessions` já
      existia. E registra que inaugurar prefixo **não** cria caso novo de
      autenticação.

      **Não copiar** o parágrafo de `SessionSummaryEndpoints.cs` sobre ordem de
      registro contra rota irmã `:guid`: não há rota irmã sob `/messages`, e não há
      nada contra o que defender a ordem.

- [x] 3.2 `Program.cs` — uma linha, `app.MapMessageSummaryEndpoints()`, entre os
      `Map*` existentes (`:132-138`) e **antes** de
      `ValidateRouteAuthenticationClassification`. Não acrescentar nada à lista de
      rotas anônimas: a rota é autenticada, e se for esquecida o startup reprova
      sozinho (D2).

- [x] 3.3 `[Fact]` do caminho feliz — mensagens de entrada retrodatadas para
      dentro da janela, resposta `200`, `inboundCount` igual à quantidade criada.
      Asserção sobre o **número**, nunca "respondeu corretamente" (convenção 5).
      Cobre o Scenario *"Mensagens de entrada dentro do intervalo são contadas"*.

- [x] 3.4 `[Fact]` de que a contagem atravessa canais, contatos e sessões —
      mensagens em sessões de contatos de canais diferentes, todas dentro da
      janela, contadas na mesma resposta. Cobre *"A contagem não se restringe a um
      canal, contato ou sessão"*.

## 4. Bordas e discriminadores de domínio — um `[Fact]` por caso

Cada caso em `[Fact]` próprio, **nunca como cláusula extra dentro do teste
positivo** (convenção 18): asserção pendurada em teste alheio não reprova sozinha
e não se lê no relatório.

- [x] 4.1 `OccurredAt` **exatamente igual a `from`** conta.
- [x] 4.2 `OccurredAt` **exatamente igual a `to`** conta.
- [x] 4.3 `OccurredAt` anterior a `from` não conta.
- [x] 4.4 `OccurredAt` posterior a `to` não conta.

- [x] 4.5 **Mensagem de saída dentro da janela NÃO entra em `inboundCount`.**
      É o teste que trava a definição escolhida — o análogo direto do teste de
      `LastActivityAt` da change anterior. Sem ele, remover o predicado de
      `Direction` do handler (ou trocá-lo por `Outbound`) deixa a suíte verde e a
      rota passa a contar outra coisa.

      **Guarda verificável por mutação, e a mutação é obrigatória**: removendo o
      predicado de `Direction` em 2.3, **só este `[Fact]` reprova**. Se mais de um
      reprovar, as janelas não estão disjuntas; se nenhum reprovar, o teste não
      está criando Outbound de verdade.

      **MUTAÇÃO RODADA E CONFIRMADA:** predicado de `Direction` removido do
      handler → **`202/203`, exatamente 1 reprovação**, e é
      `GetMessageSummary_OutboundMessagesInsideWindow_DoNotCountAsInbound`.
      Nenhum outro teste da suíte reprova, o que também confirma que as janelas
      estão disjuntas. Handler restaurado depois (`git diff` do arquivo vazio).

      O `[Fact]` cria as duas de saída nos **dois** estados de entrega (`Sent` e
      `Failed`), porque o requisito diz *"independentemente de seu estado de
      entrega"*: uma implementação que filtrasse só `Sent` continuaria errada.

      Criar a Outbound pelo caminho real exige o ciclo de push notification
      (`PushNotificationEndpoints.cs:163`). Se isso ficar desproporcional dentro
      desta classe, inserir a linha `Outbound` direto pelo `AppDbContext` é
      aceitável **aqui e só aqui** — o que o teste afirma é o predicado do
      handler, não o caminho de gravação, que já tem cobertura própria em
      `MessagePersistenceTests`. Registrar qual dos dois foi usado.

- [x] 4.6 **Webhook reentregue conta uma vez.** Duas chamadas de `ReceiveAsync`
      com o **mesmo** `externalMessageId` na mesma sessão, janela cobrindo o
      instante, resposta `inboundCount == 1` — não `2`.

      **É guarda de regressão sobre dependência, não cobertura de código novo**: a
      dedup é de `inbox-mensagens-persistidas`
      (`InboundMessageOrchestrator.cs:47,67` + índice único parcial de
      `AppDbContext.cs:168-170`), e nada nesta change a implementa. Existe porque
      o **requisito de spec** que ela sustenta é desta change (D7): quem lê o
      contrato precisa saber que a unidade contada é a mensagem, não a entrega.

      Mutação que a valida: fazer `IsDuplicateAsync` retornar sempre `false` e
      capturar a violação única como sucesso — este `[Fact]` reprova, e nenhum
      outro desta classe reprova.

      **ACHADO — A MUTAÇÃO ESCRITA ACIMA ESTAVA ERRADA, E FOI MEDIDA.** Rodada de
      fato, `IsDuplicateAsync` sempre `false` deu **`203/203`, zero reprovações**.
      Ela não isola nada, porque mira a camada errada:

      ```
      IsDuplicateAsync (:47)   ← FAST PATH. Mutá-la sozinha não muda o resultado.
            │ mutada para false
            ↓
      INSERT → índice único parcial IX_messages_SessionId_ExternalId  ← O GUARDA REAL
            │ viola
            ↓
      catch DbUpdateException when IsUniqueViolation (:67)  ← converte em "deduplicada"
            └→ DetachAddedEntities + reentra no fluxo → resultado final: 1 linha
      ```

      A garantia é do **banco mais o `catch`**, não da consulta de aplicação. Com
      as **duas** mutadas (`IsDuplicateAsync` → `false` **e** `IsUniqueViolation`
      → `false`), a suíte dá `198/203` — **5 reprovações**, e aí sim este `[Fact]`
      está entre elas.

      **E as outras quatro são o segundo achado:** já existiam quatro testes
      guardando esta dedup — `MessagePersistenceTests.ReceiveMessageAsync_SameExternalMessageIdTwice_DoesNotDuplicateMessageInResponse`
      e três em `InboundMessageOrchestratorTests`. Ou seja, **4.6 não é o guarda
      solitário que esta tarefa supôs**: é uma reafirmação, no nível da rota, de
      comportamento que já tinha cobertura própria.

      Ele **continua valendo** — é o único que afirma o efeito sobre
      `inboundCount`, e os outros quatro afirmam sobre a lista de mensagens e a
      persistência —, mas o registro honesto é que ele está mais perto de 4.7
      (teste de contrato) do que esta tarefa escreveu. A parte da frase original
      que **se sustenta**: com a mutação correta, nenhum outro `[Fact]` *desta
      classe* reprova.

- [x] 4.7 **Mensagem recebida dentro da janela, em sessão iniciada ANTES dela, é
      contada.** Espelho explícito de `/sessions/summary`: as duas rotas respondem
      perguntas diferentes e os números não se implicam.

      **Este é honestamente um teste de contrato, não de código**: ele não exercita
      nenhum caminho que 3.3 já não exercite, e **nenhuma mutação o isola** — o
      handler não olha para a sessão, que é exatamente o que ele afirma. Existe
      para que a diferença entre as duas rotas fique afirmada em vez de
      subentendida, e para reprovar caso alguém "melhore" o handler juntando-o a
      `Session.StartedAt`. Registrado como tal para a próxima varredura não o
      levantar como teste redundante.

      **ACHADO — A ORDEM DAS OPERAÇÕES DESTE TESTE É OBRIGATÓRIA, E O PLANO NÃO
      PREVIA.** A escrita óbvia (retrodatar a sessão e **depois** receber a
      mensagem) **reprovou com `0` em vez de `1`**, e a causa é real:

      ```
      ContactSessionResolver.cs:57-58
        if (session is null || UtcNow - session.LastActivityAt > InactivityTimeout)
            session?.Close(UtcNow);          ← FECHA a sessão retrodatada
            session = new Session(contactId) ← e abre OUTRA
      ```

      `InactivityTimeout` é de **1 hora** (`SessionOptions.cs:7`). Uma sessão
      retrodatada em um ano está, por definição, expirada: o `ReceiveAsync`
      seguinte não escreve nela — fecha-a e cria uma sessão nova, onde a mensagem
      cai. O `UPDATE` posterior então retrodata uma sessão **vazia**, e a contagem
      dá zero.

      **A ordem correta é receber primeiro e retrodatar depois** (mensagem e
      sessão), e está escrita como comentário no próprio `[Fact]` — não como
      convenção de estilo, mas com a medição ao lado, para ninguém "arrumar" a
      ordem depois.

      É a mesma família da decisão de 2.4 (criar em "agora", retrodatar depois),
      por um mecanismo **diferente**: lá o risco era o `DebounceSweepService`
      disparar; aqui é o resolver fechar a sessão. Dois motivos independentes para
      a mesma ordem.

- [x] 4.8 Janela sem nenhuma mensagem responde **`200` com `inboundCount` = 0** —
      não `404`, não corpo vazio. O zero é contagem **medida**, e a asserção é
      sobre o par (status, valor), não só sobre o valor (convenção 13).

## 5. Validação — um `[Fact]` por caso

- [x] 5.1 `from` ausente → `400`, erro identificando `from`.
- [x] 5.2 `to` ausente → `400`, erro identificando `to`.
- [x] 5.3 Ambos ausentes → `400`, com **os dois** na mesma resposta. É o caso que
      verifica o acumulador de 3.1; sem ele, uma implementação que retorna no
      primeiro defeito passa 5.1 e 5.2 e continua errada.
- [x] 5.4 `from` malformado → `400`, erro identificando `from`.
- [x] 5.5 **A forma do corpo é a mesma** entre "ausente" e "malformado" — o teste
      compara a estrutura das duas respostas, não só os status. É a asserção que
      justifica o binding manual de D8: com `DateTimeOffset?` na assinatura este
      `[Fact]` reprova, e é ele que impede a "simplificação" bem-intencionada de
      voltar ao binding automático.
- [x] 5.6 `to` anterior a `from`, ambos válidos → `400`.
- [x] 5.7 `from` igual a `to` → `200`, contando as mensagens recebidas exatamente
      naquele instante. O par "com item"/"sem item" da convenção 5 aplicado à borda
      degenerada do intervalo.
- [x] 5.8 Limite malformado é reportado como **malformado**, não como intervalo
      invertido — trava a ordem das duas checagens de 3.1.
- [x] 5.9 Limites **sem** deslocamento de fuso dão a mesma contagem que os mesmos
      instantes **com** `+00:00` explícito. É o teste do `AssumeUniversal`: sem
      ele, o valor nu recebe o offset local do processo e o `Npgsql` recusa o
      `DateTimeOffset` resultante — **500, não número errado**, e invisível num
      servidor com `TZ=UTC` (D8; medido na change anterior).
- [x] 5.10 Limites com deslocamento **explícito não-UTC** são normalizados antes da
      comparação. É o teste do `AdjustToUniversal`, separado de 5.9 de propósito:
      na change anterior ele **faltou no plano** e só apareceu durante a
      implementação, porque os demais testes mandam `"O"` (que já sai `+00:00`) ou
      valor nu, e nenhum cobria o caminho. Entra no plano desta vez.

## 6. Fechamento

- [x] 6.1 Rodar a suíte de `apps/inbox` e comparar com a baseline de 1.1, **número
      contra número**. Esperado: baseline **+ 20** — `[Fact]` de 3.3 e 3.4 (2),
      4.1-4.8 (8) e 5.1-5.10 (10); 2.4 é arquivo e helpers, não acrescenta caso.

      Com a baseline esperada de `183`, o fechamento esperado é **`203/203`**. Se a
      conta não fechar, ou faltou teste ou sobrou, e os dois são achado.

      **+ 20 é também a contagem de cenários da `spec.md`** (10 requisitos, 20
      cenários) — a correspondência 1:1 entre cenário e `[Fact]` é a mesma que a
      change anterior manteve, e conferi-la é parte desta tarefa, não da leitura
      seguinte.

      **MEDIDO: `203/203`**, 0 falhas, 0 ignorados, 17 s. Fecha exato contra a
      baseline de 1.1: `183 + 20 = 203`. **A conta fechou de primeira**, sem o
      recálculo que a change anterior precisou (`+18` planejado → `+19` real).

      **Correspondência 1:1 conferida, nos três números:** 20 cenários na
      `spec.md`, 20 `[Fact]` em `MessageSummaryEndpointsTests.cs`, e contagem
      estática ancorada da suíte = `203` = executado.

- [x] 6.2 Confirmar que **nenhuma migração foi gerada**: `AppDbContextModelSnapshot.cs`
      inalterado e nada novo em `Infrastructure/Migrations/`. Esta change não toca
      o modelo (D10), e uma migração aparecendo aqui é sinal de que alguém
      acrescentou o índice que a decisão deixou fora.

      **CONFIRMADO:** `git status` de `Infrastructure/` vazio —
      `AppDbContextModelSnapshot.cs` inalterado e nada novo em `Migrations/`.

- [x] 6.3 `git diff --stat` conferido contra a árvore do `design.md`: nada em
      `apps/api`, `apps/workers` ou `apps/frontend`. O escopo é declarado e
      verificável, não uma intenção.

      **CONFIRMADO:** `Program.cs` (+1 linha) e cinco arquivos novos, todos em
      `apps/inbox`. Zero em `apps/api`, `apps/workers`, `apps/frontend`.

- [x] 6.4 `openspec validate --all` e `python3 scripts/check-docs.py`. Registrar a
      saída **como o que ela é**: nenhum dos dois enxerga semântica de rota, forma
      de corpo de erro, definição de intervalo, direção de mensagem nem dedup —
      verde aqui não é cobertura de nada que esta change decidiu. O que cobre são
      os 20 `[Fact]` e as mutações de 4.5 e 4.6.

      **RODADOS:** `openspec validate --all` → **52/52** (51 specs + esta change);
      `check-docs.py` → "Integridade da documentação: OK". Rodados **duas vezes**,
      antes e depois das edições de `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.

- [x] 6.5 `CHANGELOG.md` — entrada escrita **depois** do código e conferida contra
      ele, afirmando o que a rota faz (*"conta mensagens recebidas no período"*)
      **e que só conta as de entrada**. Nunca "mensagens no período" sem
      qualificar: seria a mesma frase falsa que o `CHANGELOG` da etapa 4 de
      `knowledge-base-indexacao` produziu (convenção 13).

      **ESCRITO** no bloco "Caixas de entrada e canais", depois do código e
      conferido contra ele: diz "mensagens **recebidas**", explicita que conta
      **só as de entrada** (as respostas do agente ficam de fora em qualquer
      estado de entrega) e que a unidade é a mensagem distinta, não a entrega de
      webhook. Não diz "mensagens no período" sem qualificar.

- [x] 6.6 `02-HISTORICO_E_STATUS.md` — registrar:

      1. a rota nova e o campo `inboundCount` (com o porquê do nome, D4);
      2. **o gatilho novo de D5** — `OccurredAt` é instante de recebimento pelo
         servidor, não do provedor, e uma change futura que passe a parsear o
         `date` do provedor torna esta rota retroativamente backdatable **sem
         tocar este código**. É o item mais importante desta seção: é o único que
         não tem precedente em change anterior;
      3. que o gatilho de teto/índice agora vale para **duas** rotas, e que aqui a
         razão do índice é outra (existe índice composto que não serve), com a
         ressalva de que `messages` cresce por um múltiplo de `sessions`;
      4. `outboundCount` como adiado-com-motivo (D6), junto com a pergunta que ele
         carrega: *"entrega falhada conta como enviada?"*.

      **Não** editar aqui a linha `:4882` ("não existe ambiente de produção") nem
      o checklist de `:4880`: essa atualização tem alcance maior que esta change e
      acontece uma vez só, fora daqui (design.md, Open Questions). Registrar a
      pendência, não resolvê-la.

      **REGISTRADO** em "Abertos por `inbox-mensagens-recebidas-periodo`
      (2026-09-18)", com os quatro pontos pedidos mais **três achados da
      implementação**: a dedup ser garantida pelo banco e não pela consulta (com
      `IsDuplicateAsync` marcada como **não-achado** para varredura de código
      morto), e a tabela das duas ordens de retrodatação com os mecanismos que as
      forçam.

      **`:4880` e `:4882` NÃO foram tocadas** — conferido por `sed -n '4880p;4882p'`
      depois de escrever: seguem dizendo "Primeiro deploy em produção (checklist)"
      e "Não existe ambiente de produção hoje".
