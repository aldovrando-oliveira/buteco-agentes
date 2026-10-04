> **Base.** Branch `fix/138-indexacao-sem-job`, criada de `ffc6a7b`, atualizada com a
> `main` em `737ec2c` (merge da #106, PR #147), sem conflito de código. As baselines de
> 1.1 e 1.2 foram medidas em `ffc6a7b`.
>
> **Trabalho em paralelo.** A #106 (worktree `buteco-agents-106`) e a #47
> (`fix/47-pending-dispatch-orfa`) estão abertas ao mesmo tempo. Nenhuma toca os
> arquivos de código desta change, mas as três podem editar
> `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md`, `CHANGELOG.md` e
> `docs/architecture.md`. Nesses quatro, **acrescentar seções curtas** em vez de
> reescrever trechos, e esperar conflito de merge neles.
>
> Nenhuma tarefa em `apps/frontend` nem em `apps/connectors`. Nada em
> `docker-compose.prod.yml`, `.env.prod.example`, nginx do stack ou
> `docs/deployment.md`. Classe nova de contêiner de teste só com autorização do
> mantenedor.

## 1. Baseline e reprodução

- [x] 1.1 [apps/api] Medir a baseline da suíte do `apps/api` antes de tocar no código (`dotnet test apps/api/Api.sln`, com `DOCKER_HOST` do Podman e `TZ=America/Sao_Paulo`), conferindo o `load average` antes; registrar contagem e falhas pré-existentes aqui
  - Medido em 03/10/2026 23:03, sobre `ffc6a7b`, neste worktree: **730/730** em 2 min 9 s. `load average` 15,45 no início e 17,38 no fim (Spotlight indexando o worktree novo, nenhum `vstest` de outra sessão).
- [x] 1.2 [apps/workers] Medir a baseline da suíte do `apps/workers` nas mesmas condições; registrar aqui
  - Medido em 03/10/2026 23:06, sobre `ffc6a7b`, neste worktree: **396/396** em 8 min 35 s. `load average` 13,07 no início e 5,66 no fim. (Uma parte da janela coincidiu com a reprodução de 1.4, seis testes do `apps/api` em 12 s.)
- [x] 1.3 [apps/api] Adicionar ao `FakeKnowledgeIndexingJobPublisher` um modo "indisponível" que lança como o publisher real lança sem broker (`BrokerUnreachableException` ou equivalente), e que pode ser desligado no meio do teste
  - `Mode` (`Available`/`Unavailable`/`Blocking`), `Attempts` (toda chamada, com sucesso ou não) e `Delay`. `Unavailable` lança `BrokerUnreachableException`, a mesma do publisher real sem broker.
- [x] 1.4 [apps/api] Escrever os testes de aceite de D1/D2 (cadastro, atualização, reindexação, upsert novo e upsert existente) com a publicação indisponível: resposta de sucesso, documento gravado uma vez, pedido no banco, e mensagem publicada com id e revisão **depois** de a publicação voltar, sem nova requisição. Rodar contra o código atual e registrar que reprovam (todos respondem `500` e nenhuma mensagem sai depois) — é o defeito real (convenção 15)
  - Cinco testes em `KnowledgeDocumentCatalogTests.IndexingRequests.cs` (`partial`, mesma fixture). Contra o código atual, os cinco reprovam pela asserção conjunta, e o log traz `BrokerUnreachableException: None of the specified endpoints were reachable` em cada um: `cadastro: resposta 500 (esperado 201); mensagens da revisão 1 publicadas depois de a publicação voltar: 0 (esperado 1)`; `atualização: resposta 500 (esperado 200); ... revisão 2 ...: 0`; `reindexação: resposta 500 (esperado 200); ... revisão 1 ...: 0`; `upsert novo: resposta 500 (esperado 200); ... revisão 1 ...: 0`; `upsert existente: resposta 500 (esperado 200); ... revisão 2 ...: 0`.
- [x] 1.5 [apps/api] Escrever o teste do caso permanente da base sincronizada (upsert com texto novo e publicação falhando; reenvio `Unchanged` com marcador novo; publicação volta) e registrar que reprova contra o código atual
  - `SyncedDocument_UpdatedWhilePublicationFails_IsIndexedEvenIfTheNextCycleDoesNotResend`, contra o código atual: `upsert sem reenvio: resposta 500 (esperado 200); mensagens da revisão 2 publicadas depois de a publicação voltar: 0 (esperado 1)`. Load 18,87 → 16,49.

## 2. Pedido de indexação (D1, D6)

- [x] 2.1 [apps/api] Entidade `KnowledgeIndexingRequest` (`Id`, `KnowledgeDocumentId`, `ContentRevision`, `CreatedAt`) e mapeamento no `AppDbContext`: tabela `knowledge_indexing_requests`, FK para `knowledge_documents` com `Cascade`, índice em `CreatedAt`
  - Índice em `(CreatedAt, Id)`, a ordem exata do despacho, em vez de só `CreatedAt`; o EF cria também o índice da FK em `KnowledgeDocumentId`.
- [x] 2.2 [apps/api] Migração `AddKnowledgeIndexingRequests`, com o `INSERT ... SELECT` de D6 para os documentos em `Pending`, e `Down` que apaga a tabela
  - `20261004020943_AddKnowledgeIndexingRequests`, gerada pelo `dotnet-ef` 10.0.10, com o `INSERT ... SELECT` de D6 acrescentado à mão no fim do `Up`.
- [x] 2.3 [apps/api] Teste de migração (molde de `KnowledgeDocumentEventsMigrationTests`, na `MigrationPostgresCollection`): documentos nos quatro estados antes, um pedido por `Pending` com a revisão corrente depois, e nenhum para `Indexing`, `Indexed` e `Failed`
  - `KnowledgeIndexingRequestsMigrationTests` na `MigrationPostgresCollection` (nenhuma fonte de contêiner nova): 2/2. Cinco documentos (dois `Pending`, revisões 1 e 4, e um em cada outro estado) → exatamente `pendente:1,editado:4`; e a FK é `knowledge_documents:CASCADE`.
- [x] 2.4 [apps/api] Teste de exclusão: excluir documento e excluir base levam os pedidos junto (FK `Cascade`), e nenhuma mensagem é publicada depois para o documento excluído
  - `DeletingTheDocumentOrTheBase_TakesThePendingRequestsAlong_AndNothingIsPublishedLater`: o pedido some com o `DELETE` do documento e com o da base, e a varredura seguinte não publica nada para eles.

## 3. Confirmação do broker (D5)

- [x] 3.1 [apps/api] Conferir no assembly do `RabbitMQ.Client` 7.2.1 (decompilado, não de memória — convenção 6) o comportamento de `BasicPublishAsync` com `PublisherConfirmationsEnabled` e `PublisherConfirmationTrackingEnabled`: espera a confirmação, e lança `PublishException` em `nack` e em `basic.return`. Registrar o achado no `design.md` se divergir
  - Decompilado com `ilspycmd` 10.1.1 (`RabbitMQ.Client.Impl.Channel`, 7.2.1), confere com a D5: com `PublisherConfirmationsEnabled` e `PublisherConfirmationTrackingEnabled`, o `finally` de `BasicPublishAsync` aguarda o `TaskCompletionSource` da sequência (`MaybeEndPublisherConfirmationTrackingAsync`); `ack` resolve (`HandleAck`), `nack` e `basic.return` fazem `SetException(PublishException)` (`HandleNack`; o `basic.return` só é correlacionado pelo cabeçalho `x-dotnet-pub-seq-no`, que só é posto com o *tracking* ligado); fechamento do canal também falha o TCS (`MaybeSetExceptionOnConfirmsTcs`); cancelamento remove o TCS e relança `OperationCanceledException`. Nenhuma divergência; `design.md` sem mudança.
- [x] 3.2 [apps/api] Criar o canal do `RabbitMqKnowledgeIndexingJobPublisher` com as duas opções de confirmação
- [x] 3.3 [apps/api] Teste contra o RabbitMQ real da `A2ATaskLifecycleFixture`: publicação confirmada chega a `knowledge-indexing` com o corpo esperado. Se a fixture não servir, PARAR e pedir autorização ao mantenedor antes de criar classe de contêiner nova
  - A `A2ATaskLifecycleFixture` serviu: `DocumentCreated_ReachesTheIndexingQueue_ThroughTheConfirmedPublisher_AndTheRequestIsRemoved`, como `partial` da `A2ATaskLifecycleTests` (a classe passou a `partial`; nenhuma fonte de contêiner nova). A mensagem chega a `knowledge-indexing` com id, revisão 1 e `Attempt` 1, e o pedido sai da tabela. **Limite do que ele prova:** que o caminho confirmado funciona contra o broker real; sem a confirmação a mensagem também chegaria. O comportamento do `nack`/`basic.return` fica provado pela decompilação (3.1), não por teste.

## 4. Despacho (D3)

- [x] 4.1 [apps/api] `KnowledgeIndexingRequestSchedule` (intervalo de 30 s, lote de 100, limite de 5 s do despacho na requisição), registrado como singleton e trocável por DI nos testes; nada em configuração
  - Singleton `new KnowledgeIndexingRequestSchedule()` no `Program.cs`; a `ApiFactoryFixture` troca por `SweepInterval = 1 h`, para nenhum tique automático entrar no meio de um teste.
- [x] 4.2 [apps/api] `KnowledgeIndexingRequestDispatcher`: transação, `SELECT ... ORDER BY "CreatedAt" LIMIT @lote FOR UPDATE SKIP LOCKED` (opcionalmente restrito a ids), publicação em ordem com parada no primeiro erro, `DELETE` dos publicados, commit
  - SQL emitido, capturado do caminho de produção: `SELECT * FROM knowledge_indexing_requests ORDER BY "CreatedAt", "Id" LIMIT @p0 FOR UPDATE SKIP LOCKED` (varredura) e `... WHERE "Id" = ANY(@p0) ORDER BY "CreatedAt", "Id" FOR UPDATE SKIP LOCKED` (fim da escrita).
- [x] 4.3 [apps/api] `KnowledgeIndexingRequestSweepService` (`BackgroundService`, `PeriodicTimer`), escopo novo e `try/catch` por ciclo envolvendo a consulta inclusive (convenção 4); despacha lotes até esvaziar ou falhar; registro no `Program.cs`
- [x] 4.4 [apps/api] Teste de concorrência: dois despachos simultâneos, publisher lento, cada pedido publicado exatamente uma vez; e o par determinístico: o SQL capturado com `EmittedSqlCapture` termina em `FOR UPDATE SKIP LOCKED` (convenção 15, quinta forma). Reintroduzir o defeito (tirar o `SKIP LOCKED`) e registrar qual dos dois reprova
  - `TwoConcurrentSweeps_PublishEachRequestExactlyOnce` e `DispatchQueries_AreEmittedWithForUpdateSkipLocked`. **Guarda, duas variantes, 3 execuções cada:** (a) tirando só `SKIP LOCKED` (fica `FOR UPDATE`): reprova **só** o do SQL, 3/3 (`Assert.EndsWith() Failure: String end does not match` / `Expected end: "FOR UPDATE SKIP LOCKED"`); o comportamental **passa 3/3** — o `FOR UPDATE` sozinho já impede a duplicata: o segundo despacho espera e não vê as linhas apagadas. (b) tirando `FOR UPDATE SKIP LOCKED` inteiro: reprovam os dois, 3/3 (`Assert.Single() Failure: The collection contained 2 items`). Desfeito; `grep -c "FOR UPDATE SKIP LOCKED"` no despacho = 3 (2 consultas + 1 comentário). Achado registrado no R2 do `design.md`.
- [x] 4.5 [apps/api] Teste da varredura: primeira consulta falhando, pedido publicado num ciclo seguinte (R4); mais pedidos que o lote, todos publicados em ordem (R8)
  - `SweepQueryFailure_Throws_AndDoesNotOpenTheWindow`; `SweepService_SurvivesAFailedCycle_AndPublishesInALaterOne_WithoutAnyCall` (o serviço rodando sozinho, intervalo de 200 ms, a primeira consulta falhando por interceptor, `SweepCycleFailedEvent` contado 1 vez, pedido publicado num ciclo seguinte); `SweepWithMoreRequestsThanTheBatch_PublishesAllInTheOrderTheyWereRecorded` (lote 3, 7 pedidos).
- [x] 4.6 [apps/api] Teste do pedido não confirmado: publicação que lança mantém o pedido, e o ciclo seguinte o publica
  - `SweepWhosePublicationFails_KeepsTheRequest_OpensTheWindow_AndTheNextCyclePublishesIt`.
- [x] 4.7 [apps/api] `KnowledgeIndexingDispatchWindow` (D8): singleton em memória, por instância, com `TimeProvider`; abre por 30 s na falha do despacho na requisição (erro ou limite) e na falha de publicação da varredura; não abre na falha da consulta da varredura; fecha em qualquer despacho bem-sucedido
  - Abre na falha do despacho na requisição e na falha de publicação da varredura; não abre na falha da consulta (provado em 4.5); fecha em qualquer despacho que publicou. Conta as escritas que pularam (`Skipped`).
- [x] 4.8 [apps/api] Testes unitários da janela com `FakeTimeProvider`: abre na falha, vence em 30 s, fecha no sucesso da varredura e no da requisição, não abre na falha da consulta
  - `KnowledgeIndexingDispatchWindowTests`, 5 testes com `FakeTimeProvider`, sem contêiner.

## 5. Escritas passam pelo pedido (D1, D2, D4, D7)

- [x] 5.1 [apps/api] `CreateKnowledgeDocumentCommandHandler`: pedido no mesmo `SaveChanges`, despacho na requisição restrito ao pedido gravado, com exceção capturada e logada e limite de 5 s; sem dependência do publisher
- [x] 5.2 [apps/api] `UpdateKnowledgeDocumentCommandHandler`: idem, só com `NeedsIndexing`, nos dois `ApplyOnceAsync` (a releitura depois de `DbUpdateConcurrencyException` limpa o change tracker, então o pedido da tentativa perdida não é gravado — conferir)
- [x] 5.3 [apps/api] `ReindexKnowledgeDocumentCommandHandler`: idem, com a revisão corrente; a tentativa perdida para escrita concorrente não grava pedido
- [x] 5.4 [apps/api] `UpsertSyncedDocumentCommandHandler`: pedido no mesmo `SaveChanges` do documento novo e do existente com `NeedsIndexing`; `DetachAddedEntities` também solta o pedido da tentativa perdida para `UniqueViolation`; `Unchanged` não grava pedido
- [x] 5.5 [apps/api] Teste de arquitetura (D7): o único tipo do assembly do `apps/api` com `IKnowledgeIndexingJobPublisher` no construtor é o `KnowledgeIndexingRequestDispatcher`
  - `KnowledgeIndexingPublisherDependencyTests.OnlyTheDispatcherDependsOnTheIndexingPublisher`, por reflexão sobre os construtores do assembly do `apps/api`, sem contêiner.
- [x] 5.6 [apps/api] Rodar os testes de 1.4 e 1.5 e ver passar. Depois, **guarda contra o defeito real** (convenção 15): voltar um dos handlers ao "publica depois e esquece" (publisher direto, sem pedido) e registrar que o teste de aceite daquele caminho reprova **e** que o teste de 5.5 reprova; restaurar
  - Os seis testes de 1.4/1.5 passam. **Guarda:** `CreateKnowledgeDocumentCommandHandler` voltado ao publisher direto, sem pedido. Reprovaram `Create_WithPublicationFailing_Returns201AndIsIndexedLater` (`cadastro: resposta 500 (esperado 201); mensagens da revisão 1 publicadas depois de a publicação voltar: 0 (esperado 1)`) e `OnlyTheDispatcherDependsOnTheIndexingPublisher` (`Assert.Equal() Failure: Collections differ`, `Actual` com `...KnowledgeDocuments.Commands.CreateKnowl...` a mais); `UpsertNew_WithPublicationFailing...`, caminho não tocado, ficou verde como controle. Desfeito.
- [x] 5.7 [apps/api] Teste do limite de 5 s (R3): publisher que bloqueia, resposta da escrita antes do limite mais folga, documento e pedido gravados
  - `WriteWithBlockingPublication_RespondsWithinTheDispatchLimit_WithTheRequestRecorded`, limite reduzido por DI para 1 s.
- [x] 5.8 [apps/api] Teste de revisões sucessivas (R6): com a publicação falhando, duas atualizações de conteúdo geram pedidos N e N+1; publicação volta; as duas mensagens saem em ordem
  - `TwoContentUpdatesWhilePublicationFails_PublishBothRevisionsInOrder_WhenItRecovers`: revisões `[2, 3]`, nessa ordem.
- [x] 5.9 [apps/api] Ajustar os testes existentes que afirmam publicação logo depois da escrita (ex.: `KnowledgeDocumentCatalogTests.Concurrency`, reindexação, upsert), sem afrouxar asserção negativa ("nenhuma publicação") — a de concorrência continua afirmando o mesmo duplo da fixture
  - **Nenhum teste existente foi ajustado.** O despacho no fim da escrita publica antes da resposta, como antes, e todo teste que afirmava a publicação logo depois da escrita (`KnowledgeDocumentReindexTests`, `KnowledgeDocumentIndexingContractTests`, `KnowledgeDocumentCatalogTests.Concurrency`, `.BaseDeletion`, `.Synced`, `KnowledgeRouteAuthenticationTests`) passou sem mudança, inclusive as asserções negativas. O único ajuste foi na fixture (`ApiFactoryFixture`: varredura a cada hora, para nenhum tique automático tomar o pedido de uma escrita entre o commit e o despacho dela). Suíte inteira: 756/756.
- [x] 5.10 [apps/api] Teste da janela (D8, R9), provado pela **contagem**, não pelo relógio: com a publicação bloqueando, uma sequência de 5 escritas (misturando upsert e cadastro). Asserção principal: o número de tentativas de despacho vindas da requisição é **uma** (a da primeira escrita), lido de um contador no publicador falso ou na própria janela. Asserção secundária, com folga larga: cada escrita seguinte responde abaixo do limite de despacho. Todos os pedidos gravados; a publicação volta, a varredura roda e cada pedido é publicado exatamente uma vez. O limite pode ser reduzido por DI pelo `KnowledgeIndexingRequestSchedule` para encurtar a suíte, desde que a asserção continue sendo a contagem
  - `AfterAFailedDispatch_FollowingWritesSkipIt_OneAttemptForFiveWrites_AndTheSweepPublishesAll`: 5 escritas (3 cadastros e 2 upserts, alternados), limite reduzido por DI para 2 s. Principal: tentativas de publicação = **1** (contador `Attempts` do duplo). Secundárias: `Skipped` da janela = 4; cada escrita seguinte abaixo do limite; os 5 pedidos gravados; depois da varredura, uma mensagem por documento, nenhum pedido restante, janela fechada.
- [x] 5.11 [apps/api] **Guarda da janela** (convenção 15): retirar a janela (despacho na requisição sempre tentado) e registrar que o teste de 5.10 reprova **pela contagem** de tentativas (cinco em vez de uma), com a mensagem; restaurar
  - **Guarda:** `if (window.IsOpen)` trocado por `if (false && window.IsOpen)`. O 5.10 reprovou **pela contagem**, na primeira asserção: `tentativas de despacho vindas da requisição em 5 escritas com a publicação bloqueando: 5 (esperado 1)`. Desfeito.

## 6. Consumidor (R1)

- [x] 6.1 [apps/workers] Teste: duas mensagens da mesma revisão entregues ao consumo terminam com o documento em `Indexed` e o mesmo conjunto de fragmentos de uma entrega só (mesma contagem, nenhum fragmento repetido). Nenhuma mudança de código de produção; se o teste reprovar, PARAR e reportar
  - `KnowledgeIndexingTests.SameRevisionDeliveredTwice_EndsIndexedWithTheFragmentSetOfASingleDelivery`, na classe existente (mesma fixture): passa. As duas entregas terminam `Indexed`, com o mesmo conjunto `(Ordinal, Text)`, sem ordinal repetido, `FragmentCount` igual ao número de linhas, e duas chamadas de embedding (o custo aceito). Nenhuma mudança de código de produção no `apps/workers`.
- [x] 6.2 [apps/workers] Conferir que o teste existente de descarte por revisão obsoleta continua verde
  - `KnowledgeIndexingConcurrencyTests` (descarte por revisão obsoleta, documento excluído durante a indexação, revisão obsoleta antes de começar) junto com a `KnowledgeIndexingTests`: 35/35.

## 7. Verificação

- [x] 7.1 [apps/api] Suíte inteira, conferindo o `load average` antes; comparar com 1.1 e classificar qualquer falha só depois de refazer com a máquina livre
  - 03/10/2026 23:54–23:56: **756/756** em 2 min 40 s (baseline 730; +26 novos, conferidos por nome). `load average` 4,00 → 16,45.
- [x] 7.2 [apps/workers] Suíte inteira, comparando com 1.2
  - Primeira rodada, 23:56–00:05, `load` 16,45 → 4,75: **396/397**, reprovou `KnowledgeIndexingTests.EachFragmentKeepsTheVectorOfItsOwnText_AcrossTheBatchBoundary` (`Expected: 5 / Actual: 4`), que é a **#130**, intermitente e anterior a esta change (mesmo teste, mesma mensagem, observada na baseline da #46). Refeita em 04/10 00:05–00:12, `load` 4,29 → 4,37, sem `vstest` de outra sessão: **397/397** (baseline 396 + 1 novo).
- [x] 7.3 [tests/] `ApiConnectorsRoundTrip.Tests` (o upsert de `/sync` é contrato entre `apps/api` e `apps/connectors`)
  - 04/10 00:05: **7/7** em 16 s, `load` 4,75 → 4,36.
- [x] 7.4 [apps/api] Verificação manual com host real do `apps/api` sem RabbitMQ (portas só na faixa 55100–55199, conferidas com `lsof`): cadastro e upsert respondem sucesso; subir o RabbitMQ; o pedido sai em até 30 s e a mensagem aparece em `knowledge-indexing`. Registrar portas e resultado aqui
  - Em 03/10/2026, 23:31–23:54. Portas: **55100** (`apps/api`, `dotnet run --no-launch-profile`), **55101** (Postgres `pgvector/pgvector:pg18`, contêiner `buteco-138-verify-pg`), **55102** (AMQP) e **55103** (gestão) do RabbitMQ `rabbitmq:4.3-management` (`buteco-138-verify-rmq`), todas conferidas livres com `lsof` antes e depois. (4) Banco migrado até `AddKnowledgeBaseSync`, cinco documentos semeados (2 `Pending`, revisões 1 e 3, e um em cada outro estado): **2 `Pending` antes da migração, 2 pedidos criados por ela**, com as revisões 1 e 3, nenhum para os outros. (1) Com o RabbitMQ apontado para a 55102 vazia: cadastro **201** com `indexingStatus: Pending`, upsert **200** `Created`, pedidos gravados. (2) Oito escritas seguidas (4 cadastros e 4 upserts alternados): pelo log, **1 despacho tentado e falho** (evento 1032, `BrokerUnreachableException: None of the specified endpoints were reachable`) e **7 pulados** (evento 1031); a primeira levou 2,94 s, as outras 10–118 ms; 10 pedidos na tabela (2 da migração + 8). Entre 23:41:56 e 23:52:26, 22 tiques da varredura falharam a publicação (evento 1033) sem derrubar o serviço. (3) RabbitMQ no ar às 23:52:42; a varredura de 23:52:56 esvaziou a tabela (14 s depois, dentro dos 30 s), e `rabbitmqctl list_queues` deu **`knowledge-indexing 10`**: 10 documentos distintos, um por pedido, revisões certas, `Attempt` 1. Uma escrita depois disso publicou no fim da própria requisição (201 em 0,06 s, tabela vazia, 11ª mensagem): a varredura que publicou fechou a janela. **Ambiente:** o `rabbitmq:4.3-management` por `podman run` nesta máquina não sobe com o cookie em arquivo (`Error when reading /var/lib/rabbitmq/.erlang.cookie: eacces`, com volume anônimo, volume nomeado, `tmpfs` e `label=disable`); subiu com `RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS="-setcookie ..."`. As tentativas com volume anônimo deixaram dois volumes anônimos sem dono identificável na VM (os contêineres foram removidos sem `-v`); não os apaguei, porque não há como separá-los dos de outras sessões. Contêineres e processo removidos no fim.
- [x] 7.5 `python3 scripts/check-docs.py` e `openspec validate indexacao-sem-job-orfao --strict`
  - `check-docs.py`: OK; `openspec validate --strict`: válida. Guardas conferidos por `grep`: nenhum marcador `GUARDA 5.` em `apps/`; `FOR UPDATE SKIP LOCKED` presente; `if (window.IsOpen)` restaurado; o handler de cadastro depende do despacho.

## 8. Documentação (pode conflitar com a #106 e a #47)

- [x] 8.1 `01-ARQUITETURA_E_CONVENCOES.md`, seção "Filas de trabalho": acrescentar um parágrafo sobre o pedido de indexação (outbox), o despacho e o `SKIP LOCKED`, sem reescrever o texto existente dos "três publicadores"
  - Um parágrafo acrescentado depois do dos três publicadores, sem reescrever o existente.
- [x] 8.2 `docs/architecture.md`: acrescentar a tabela `knowledge_indexing_requests` e o primeiro `BackgroundService` do `apps/api`, em seção curta
  - Seção `KnowledgeIndexingRequest (apps/api)` acrescentada depois da de `KnowledgeDocument`.
- [x] 8.3 `02-HISTORICO_E_STATUS.md`: entrada da change, com o que foi medido em 1.x, 5.6 e 7.4
  - Entrada nova no fim do `02`.
- [x] 8.4 `CHANGELOG.md`, `[Unreleased]`: uma linha em "Corrigido"
  - Primeira entrada de `### Fixed`.
- [x] 8.5 Antes do archive, conferir que a #144 (despacho de task A2A, Achados do `design.md`) está citada no `design.md`, no `proposal.md` e na entrada do `02`
  - #144 citada no `design.md` (Non-Goals e Achados), no `proposal.md` (Impact) e no `02`.
