## Context

**Issue:** #138. Bloqueia a #119.

### Como a indexação é pedida hoje

Toda escrita que pede indexação grava o documento e **depois** chama
`IKnowledgeIndexingJobPublisher.PublishAsync`, sem `try`. São cinco chamadas em
quatro handlers do `apps/api`:

| escrita | arquivo | publica em |
|---|---|---|
| cadastro do operador | `apps/api/src/Buteco.Api/KnowledgeDocuments/Commands/CreateKnowledgeDocument/CreateKnowledgeDocumentCommandHandler.cs` | `:60`, depois do `SaveChangesAsync` de `:55` |
| atualização do operador | `.../UpdateKnowledgeDocument/UpdateKnowledgeDocumentCommandHandler.cs` | `:120`, depois do `SaveChangesAsync` de `:116`, só com `NeedsIndexing` |
| reindexação | `.../ReindexKnowledgeDocument/ReindexKnowledgeDocumentCommandHandler.cs` | `:87`, depois do `SaveChangesAsync` de `:73` |
| upsert de `/sync`, documento novo | `apps/api/src/Buteco.Api/KnowledgeSync/Commands/UpsertSyncedDocument/UpsertSyncedDocumentCommandHandler.cs` | `:114`, depois do `SaveChangesAsync` de `:79` |
| upsert de `/sync`, documento existente | mesmo arquivo | `:205`, depois do `SaveChangesAsync` de `:199`, só com `NeedsIndexing` |

O publisher é `apps/api/src/Buteco.Api/Knowledge/Indexing/RabbitMqKnowledgeIndexingJobPublisher.cs`:
canal preguiçoso, mensagem persistente, `mandatory: true`, **sem confirmação do
broker** (o canal é criado em `:73` sem `CreateChannelOptions`). Sem confirmação,
o retorno de `BasicPublishAsync` só diz que a mensagem foi escrita no socket.

Com o broker fora do ar, `GetChannelAsync` lança `BrokerUnreachableException`,
a exceção escapa do handler, o endpoint responde `500`, e o documento já está
gravado em `Pending`. Medido na #105 (tarefa 1.2 da `ciclo-de-sincronizacao`).

### Como o `apps/workers` consome

`apps/workers/src/Buteco.Workers/Knowledge/Indexing/KnowledgeIndexingConsumer.cs`
consome `knowledge-indexing` com `prefetchCount: 1`. O descarte por revisão está
em `KnowledgeIndexingService.cs`:

- `:78`: documento ausente, ou `ContentRevision` diferente da mensagem →
  `Discarded`, sem contar tentativa;
- `:291` e `:321`: a marcação de `Indexing` e o commit final são condicionados a
  `ContentRevision` igual à da mensagem; zero linhas afetadas no commit →
  rollback e `Discarded`.

**O que o descarte NÃO cobre:** duas mensagens da **mesma** revisão. As duas
indexam; a segunda substitui os fragmentos da primeira pelos mesmos fragmentos,
na transação única de `CommitAsync` (apaga tudo, insere tudo). O dado final é
correto, e o custo é uma indexação a mais. Isso importa para D1 e D4, que
admitem entrega pelo menos uma vez.

### O estado `Pending` não distingue "na fila" de "órfão"

`Pending` é escrito pela escrita do `apps/api` e significa "aguardando
indexação". Um documento cuja mensagem está esperando na fila e um documento cuja
publicação falhou têm **exatamente a mesma linha**. Nenhum campo do documento
separa os dois, e é por isso que uma varredura sobre `Pending` não acha os órfãos
sem também achar os que estão na fila (D1, alternativa B).

### Mecanismos de despacho que já existem no monorepo

Nenhum no `apps/api`. O `apps/api` não tem `BackgroundService`, `IHostedService`
nem outbox: o despacho de task A2A
(`apps/api/src/Buteco.Api/A2A/EnqueueingAgentHandler.cs:83`) é o mesmo "publica
depois e esquece". Componentes periódicos existem em outros apps, e deles vem
só a **forma**: `DebounceSweepService` (`apps/inbox`, `PeriodicTimer`, escopo e
`try/catch` por ciclo) e `NonTerminalTaskDetectorService` (`apps/workers`, só
diagnóstico, não recupera nada).

A #47 (`fix/47-pending-dispatch-orfa`) trata da `PendingDispatch` do
`apps/inbox` presa em `Dispatching` quando o push notification do
`apps/workers` falha. É da mesma família (efeito colateral depois de um commit,
sem garantia), mas em outros apps, outra tabela e outra máquina de estados.
Nenhum arquivo de código é comum às duas; o que pode conflitar é a
documentação (`01`, `02`, `CHANGELOG.md`, `docs/architecture.md`).

### Implantação

`docs/deployment.md` só proíbe `replicas > 1` para `apps/workers` e
`apps/inbox`. O `apps/api` pode ter mais de uma instância, e o mecanismo precisa
ser seguro com isso. A migração roda pelo `migrator`, com o `apps/api` antigo
ainda no ar (`docs/deployment.md`, sequência de redeploy).

## Goals / Non-Goals

**Goals:**

- Documento gravado com pedido de indexação recebe a indexação, sem ação do
  operador, mesmo que o broker esteja fora do ar no momento da escrita, nos
  quatro caminhos de escrita.
- O caso permanente da base sincronizada fechado sem depender do ciclo da #105.
- A escrita não afirma falha do que gravou.
- Seguro com várias instâncias do `apps/api`.
- Os documentos que já estão órfãos são recuperados.

**Non-Goals:**

- O despacho de task A2A (`EnqueueingAgentHandler.cs:83`), que tem o mesmo
  defeito em outra fila: achado, virou a #144.
- Documento preso em `Indexing` porque o `apps/workers` caiu no meio
  (`KnowledgeIndexingConsumer` confirma a mensagem no `catch`; ver #125). Outro
  defeito, outro app.
- Republicação na fila de espera pelo `apps/workers`
  (`KnowledgeIndexingConsumer.cs:93`): a mensagem já foi consumida e o estado
  fica visível; não é o caminho desta issue.
- Mostrar na tela que o pedido está esperando o broker. `Pending` é verdade, e
  nenhuma tarefa toca `apps/frontend`.
- A pilha da exceção no corpo do `500` em `Development` (citada na #138 como
  fora de escopo).

## Decisions

### D1. Outbox transacional no `apps/api`: o pedido de indexação é gravado com o documento

Tabela nova `knowledge_indexing_requests`, do `AppDbContext` do `apps/api`:

| coluna | tipo | |
|---|---|---|
| `Id` | `uuid` | PK |
| `KnowledgeDocumentId` | `uuid` | FK para `knowledge_documents`, **`Cascade`** |
| `ContentRevision` | `int` | a revisão que a mensagem vai levar |
| `CreatedAt` | `timestamptz` | ordem de despacho e idade do pedido |

Índice em `(CreatedAt, Id)`, a ordem exata do despacho (corrigido na
implementação: o rascunho dizia só `CreatedAt`). A FK é `Cascade` porque o pedido é conteúdo do
documento e não tem significado sem ele: exclusão de documento e exclusão de
base (que apaga documentos na aplicação, `exclusao-base-conhecimento`) levam os
pedidos junto, e um pedido para documento excluído nunca chega à fila.

Cada uma das cinco chamadas da tabela do Context vira **um `Add` de pedido no
mesmo `SaveChanges`** do documento, com a revisão já incrementada. Documento e
pedido são gravados juntos ou nenhum dos dois, e isso continua cumprindo "a
publicação ocorre na mesma transação que persiste o documento, ou depois dela —
nunca antes".

**Alternativas descartadas:**

- **B. Varredura periódica que republica documentos `Pending` sem job.** Não há
  como saber qual `Pending` está sem job (Context): o estado é o mesmo com a
  mensagem na fila. Um limiar de idade republicaria todo documento que esperou
  na fila mais que o limiar, que é exatamente o caso de backlog grande (primeira
  sincronização de uma base); e não republicaria o órfão antes do limiar. Seria
  uma heurística sobre o sintoma, com a régua errada nos dois sentidos.
- **C. Reaproveitar o mecanismo de despacho que o `apps/api` já tenha.** Não
  existe (Context). O `DebounceSweepService` é do `apps/inbox`, e o isolamento
  entre apps impede reaproveitar código; dele vem só a forma do laço (D3).
- **D. Varredura no `apps/workers`.** O `apps/workers` compartilha o banco e já
  tem um publisher de indexação (para as filas de espera), então poderia
  varrer a tabela. Descartado: quem escreve o pedido é o `apps/api`, a entidade
  e a migração moram lá, e os três publicadores de pedido inicial já são do
  `apps/api` (`01`, Filas de trabalho). Dividir o par entre dois apps faria o
  `AppDbContext` espelhado do `apps/workers` mapear uma tabela que ele só lê
  por causa disso. E o `apps/workers` é instância única, então o despacho
  pararia junto com o consumo, sem ganho.
- **E. `try/catch` no handler e responder `503` com `Retry-After`.** Não fecha
  o defeito: ver D2.

### D2. A escrita responde o mesmo sucesso de hoje com o broker fora do ar

Cadastro `201`, atualização e reindexação `200`, upsert `200` com
`Created`/`Updated`. O documento **foi** gravado, e o pedido de indexação
também: o que o sistema sabe é "gravado e aguardando indexação", e o corpo diz
exatamente isso (`indexingStatus: Pending`). Responder `500` ou `503` afirmaria
falha do que foi gravado, que é a convenção 13 na direção errada.

**Alternativa descartada: `503` com `Retry-After`.** O cliente que obedece
repete a escrita, e a repetição é pior que a resposta errada:

- no cadastro do operador, `POST` não é idempotente: a repetição cria um
  **segundo documento**;
- no upsert, a repetição do mesmo conteúdo é `Unchanged`, que não publica: é
  exatamente o caminho permanente da #138;
- o `apps/connectors` lê `503` do upsert como `Contention`
  (`apps/connectors/src/Buteco.Connectors/Sync/SyncApiClient.cs:112`), e mudar
  o significado disso é mudar o contrato de outro app.

**O que acontece quando a gravação falha:** como hoje. O pedido está na mesma
transação, então banco fora do ar continua sendo erro, e nada é gravado.

### D3. Um componente de despacho, chamado no fim da requisição e por varredura

`KnowledgeIndexingRequestDispatcher` (`apps/api`), o **único** dependente de
`IKnowledgeIndexingJobPublisher` no `apps/api`. Uma operação:

1. abre transação;
2. `SELECT ... ORDER BY "CreatedAt" LIMIT 100 FOR UPDATE SKIP LOCKED` sobre
   `knowledge_indexing_requests` (opcionalmente restrito a ids);
3. publica cada pedido, em ordem, com confirmação do broker (D5); para no
   primeiro que falhar;
4. apaga os pedidos publicados;
5. commit.

E é chamado de dois lugares:

- **No fim da requisição.** Depois do `SaveChanges` que deu certo, o handler
  chama o despacho restrito aos pedidos que acabou de gravar. É o caminho
  normal: com o broker no ar, a mensagem sai antes da resposta, como hoje, e a
  latência até a indexação não muda. Qualquer exceção é **capturada e logada**
  (warning), e a resposta é a de D2. A tentativa é limitada a **5 s**
  (`CancellationTokenSource` ligado ao da requisição): com o broker num endereço
  que não responde, a conexão do RabbitMQ.Client espera o próprio timeout, e a
  escrita não pode herdar isso. O limite fica bem abaixo dos 30 s com que o
  `apps/connectors` chama o `apps/api` (`SyncApiClient.Timeout`,
  `apps/connectors/src/Buteco.Connectors/Sync/SyncApiClient.cs:36`). Depois
  de uma falha, as requisições seguintes pulam esta tentativa por uma janela
  (D8).
- **Por varredura.** `KnowledgeIndexingRequestSweepService`, `BackgroundService`
  com `PeriodicTimer` de **30 s**, despacha em lotes de 100 até a tabela esvaziar
  ou um despacho falhar. Forma de `DebounceSweepService`: escopo novo e
  `try/catch` **por ciclo**, envolvendo a consulta inclusive (convenção 4: a
  chamada que mais realisticamente falha é a do banco, e ela fica dentro). Um
  ciclo que falha loga e espera o próximo tique; o serviço nunca morre.

É o **primeiro `BackgroundService` do `apps/api`**. Não é contêiner novo nem
processo novo: roda dentro do processo de sempre.

**Várias instâncias.** `FOR UPDATE SKIP LOCKED` faz cada pedido ser tomado por
uma transação só: a requisição e a varredura de uma instância, ou as varreduras
de duas instâncias, nunca publicam o mesmo pedido ao mesmo tempo. Quem perde
pula o pedido; depois do commit de quem venceu, o pedido não existe mais.

**Corrigido na implementação — o que cada metade faz** (medido na tarefa 4.4).
O rascunho atribuía a ausência de duplicata ao `SKIP LOCKED`, e isso estava
impreciso. **Quem impede a duplicata é o `FOR UPDATE`:** sem o `SKIP LOCKED`, o
segundo despacho **espera** a trava do primeiro e, depois do commit dele, não vê
mais as linhas apagadas — o teste de dois despachos concorrentes passou 3/3 assim.
Só sem trava nenhuma ele reprovou (3/3, mensagem duplicada). **O `SKIP LOCKED` é o
que faz o segundo pular em vez de esperar** enquanto o primeiro publica, com a
transação aberta, um lote inteiro com confirmação do broker. Os dois continuam: o
primeiro pela correção, o segundo para que uma varredura não fique atrás da outra.
E é por isso que o guarda determinístico (o SQL emitido) é o par obrigatório: o
comportamental não distingue `FOR UPDATE` de `FOR UPDATE SKIP LOCKED`.

**Corrigido na implementação — o despacho abandonado é cancelado antes de ser
largado.** O limite é aplicado duas vezes: `CancelAfter` no token e `WaitAsync`
na espera, para que uma chamada que não honre o token não segure a resposta. As
duas expiram juntas, e o `WaitAsync` pode vencer: se o `CancellationTokenSource`
fosse descartado em seguida sem ter cancelado, o despacho abandonado seguiria
segurando a transação e a trava do pedido, que a varredura pularia (`SKIP LOCKED`)
para sempre. O despacho cancela explicitamente (`CancelAsync`) antes de abandonar.

**Entrega pelo menos uma vez.** O pedido é apagado depois da confirmação, na
mesma transação que o travou. Se o processo morrer entre a confirmação e o
commit, o pedido volta a ficar visível e é publicado de novo. A duplicata é da
**mesma revisão**, e o `apps/workers` a indexa outra vez com o mesmo resultado
(Context). Nenhum dado errado, uma indexação a mais, numa janela de
milissegundos.

**Alternativas descartadas:**

- **Só varredura, sem o despacho na requisição.** Um caminho só, mais simples,
  mas toda indexação passaria a esperar até 30 s para começar, e cada teste que
  hoje afirma a publicação logo depois da escrita viraria espera. Encurtar o
  intervalo para 1 s troca isso por uma consulta por segundo por instância, à
  toa na maior parte do tempo.
- **Sinal em memória acordando a varredura.** Sem a espera, mas a resposta sai
  antes de a publicação acontecer, e o teste "publicou exatamente uma" passa a
  depender de tempo. O despacho na requisição dá o mesmo resultado com o
  mesmo código e sem essa dependência.
- **Publicar no handler e gravar o pedido só quando a publicação falha.** O
  pedido seria gravado **depois** do commit do documento, numa segunda escrita
  que pode falhar pelo mesmo motivo (processo caindo): é o defeito de novo, uma
  linha abaixo.
- **Intervalo configurável.** Convenção 2: nenhum cenário real pede outro valor.
  É constante no código; os testes trocam o agendamento por DI, não por
  configuração.
- **Contador de tentativas e último erro no pedido.** Não há consumidor: nada
  na tela lê, e o log de cada ciclo já diz por que falhou. A idade do pedido
  (`CreatedAt`) é a medida que a operação precisa, e já existe.

### D4. Base sincronizada: o pedido viaja com o `externalVersion`

O caso permanente da #138 acontece porque o `externalVersion` novo e a
publicação são separados: o primeiro é gravado, a segunda falha, e o ciclo
seguinte compara o marcador e não reenvia. Com D1, o pedido entra no **mesmo
`SaveChanges`** que grava o `externalVersion`. Se o marcador foi gravado, o
pedido também foi, e a varredura o publica. O ciclo seguinte pode responder
`Unchanged` sem publicar nada, e isso deixa de importar: o pedido não depende
dele.

Não muda nada no `apps/connectors` nem na regra `Unchanged` (D3 da
`catalogo-base-sincronizada`).

**Alternativa descartada: o upsert `Unchanged` republicar quando o documento
está `Pending`.** Depende de o ciclo reenviar o arquivo, e ele não reenvia: o
marcador é igual. E, quando reenviasse, republicaria também o documento cuja
mensagem está na fila (o problema de B em D1).

**Alternativa descartada: o ciclo da #105 reindexar.** Já descartada na issue:
o `apps/connectors` não sabe o estado de indexação, e passaria a decidir uma
regra do `apps/api`.

### D5. Publicação com confirmação do broker

O canal do `RabbitMqKnowledgeIndexingJobPublisher` do `apps/api` passa a ser
criado com `CreateChannelOptions(publisherConfirmationsEnabled: true,
publisherConfirmationTrackingEnabled: true)`. Conferido no XML do
`RabbitMQ.Client` **7.2.1** (versão em `Directory.Packages.props`): com as duas
opções, `BasicPublishAsync` espera a confirmação e lança `PublishException` em
`nack` ou `basic.return` (o `mandatory: true` que o publisher já usa). A tarefa
de implementação confere o comportamento contra o broker real, não só a
documentação (convenção 6).

Sem isso, D3 apagaria o pedido depois de uma publicação que o broker pode não
ter recebido, e o órfão voltaria por outro caminho.

O `PublishToWaitQueueAsync` do mesmo publisher não é chamado no `apps/api`
(existe por espelho da interface); a confirmação vale para ele também, sem
custo.

**Alternativa descartada: transação AMQP (`TxSelect`/`TxCommit`).** Mesma
garantia, mais lenta, e não é o mecanismo que a biblioteca recomenda na v7.

### D6. Os órfãos que já existem são recuperados pela migração

A migração que cria a tabela insere um pedido para cada documento em `Pending`
naquele momento, com a revisão corrente:

```sql
INSERT INTO knowledge_indexing_requests ("Id", "KnowledgeDocumentId", "ContentRevision", "CreatedAt")
SELECT gen_random_uuid(), "Id", "ContentRevision", now()
FROM knowledge_documents
WHERE "IndexingStatus" = 'Pending';
```

Quem está em `Pending` com a mensagem na fila recebe uma segunda mensagem da
mesma revisão: uma indexação a mais, sem dado errado (D3, entrega pelo menos uma
vez). É o preço de não saber quem é órfão, pago uma vez.

**Custo aceito: indexação duplicada na implantação.**

- **Quantos documentos:** no máximo os que estiverem em `Pending` no instante em
  que a migração roda — os órfãos (que é o que se quer recuperar) somados aos
  que têm mensagem esperando na fila ou em espera de nova tentativa. D1 explica
  por que os dois grupos não se separam. Num stack sem backlog de indexação, o
  segundo grupo é pequeno; o pior caso é implantar durante a primeira
  sincronização de uma base grande, quando quase todos os documentos dela estão
  em `Pending` com mensagem na fila. A contagem real é medível antes do deploy
  com `SELECT count(*) FROM knowledge_documents WHERE "IndexingStatus" = 'Pending'`,
  e vai registrada no `02` junto com a implantação.
- **Por que é seguro:** as duas mensagens levam a **mesma revisão**. O
  `apps/workers` indexa a segunda como a primeira, e o commit substitui o
  conjunto inteiro de fragmentos numa transação (apaga tudo, insere tudo): o
  resultado final é o de uma indexação só. Isso não é suposição, é o que a
  tarefa 6.1 prende — duas mensagens da mesma revisão terminam em `Indexed` com
  o conjunto de fragmentos de uma entrega só, sem fragmento repetido. Se a 6.1
  reprovar, a migração não sai como está.
- **O custo:** uma chamada de embedding repetida por documento afetado, e o
  tempo de fila correspondente, **uma vez**, na implantação. Não se repete: a
  partir dela, cada escrita grava o próprio pedido, e nenhum `Pending` novo nasce
  sem pedido.

Fora do critério, de propósito: `Indexed` e `Failed` (rodada encerrada; `Failed`
tem o botão de reindexar e o motivo na tela) e `Indexing` (preso por queda do
`apps/workers`, não desta issue).

**Prova:** teste de migração no molde de `KnowledgeDocumentEventsMigrationTests`:
banco parado na migração anterior, documentos nos quatro estados, e depois da
migração exatamente um pedido por documento `Pending`, com a revisão corrente,
e **nenhum** para `Indexing`, `Indexed` e `Failed`.

**Alternativas descartadas:**

- **Recuperar na primeira execução da varredura.** "Primeira" não existe sem
  estado extra: o processo não sabe se é o primeiro boot depois do deploy, e
  repetir a cada boot republicaria todo `Pending` a cada reinício.
- **Não recuperar, e deixar o operador reindexar.** Na base sincronizada não há
  sinal na tela de que o documento está órfão; o operador não tem como saber
  qual reindexar.

### D7. Todas as escritas passam pelo pedido, e um teste prende isso

As cinco chamadas do Context são substituídas. Depois da change, o único tipo
do assembly do `apps/api` com `IKnowledgeIndexingJobPublisher` no construtor é
o `KnowledgeIndexingRequestDispatcher`. Um teste de arquitetura afirma isso por
reflexão sobre os construtores, e a asserção é a **negativa**: nenhum handler
depende do publisher. Uma escrita nova que publique direto reprova o teste.

**Alternativa descartada: só os testes de comportamento por caminho.** Cobrem
os quatro caminhos de hoje e não dizem nada do quinto.

### D8. Depois de uma falha, o despacho na requisição é pulado por uma janela

O limite de 5 s de D3 é aceitável para o operador, que faz uma escrita de cada
vez. Para o ciclo da #105, que faz um upsert por arquivo em sequência, não é
(R9): com o broker inacessível, cada upsert esperaria o limite inteiro.

**A regra:**

- Quando o despacho na requisição falha — exceção ou limite de 5 s —, a
  instância **abre uma janela de 30 s** (o intervalo da varredura). Enquanto a
  janela está aberta, as requisições **pulam** o despacho na requisição e
  respondem logo depois do commit. O pedido continua sendo gravado na mesma
  transação em **todos** os casos: só a tentativa imediata é pulada, e o pedido
  fica para a varredura.
- Uma falha de **publicação** na varredura também abre a janela: é a mesma
  informação ("o broker não está aceitando"). Uma falha da **consulta** da
  varredura não abre, porque não diz nada sobre o broker.
- **Um despacho bem-sucedido** — na requisição ou na varredura — **fecha a
  janela** na hora.
- Janela vencida sem nenhum sucesso: a próxima requisição tenta de novo. Se
  falhar, a janela reabre. Com o broker fora por muito tempo, cada instância
  paga no máximo um limite de 5 s a cada 30 s.

**O tamanho, 30 s:** é o intervalo da varredura, que é quem publica enquanto a
janela está aberta. Mais curta, a instância testaria o broker na requisição mais
vezes do que a varredura, pagando o limite à toa; mais longa, não ganha nada,
porque a varredura que publica com sucesso já fecha a janela, e o atraso máximo
depois da volta do broker continua sendo um ciclo.

**Onde o estado vive:** em memória do processo, num singleton
(`KnowledgeIndexingDispatchWindow`, com `TimeProvider` para os testes), **por
instância**. Cada instância descobre sozinha, na primeira falha, que o broker
caiu, e paga isso uma vez. Não é estado que precise sobreviver a reinício: um
processo novo começa com a janela fechada e, se o broker estiver fora, abre na
primeira falha.

**Alternativas descartadas:**

- **Estado compartilhado no banco** (uma linha "broker indisponível até ..."):
  cada instância deixaria de pagar o primeiro limite, ao custo de uma escrita e
  uma leitura no caminho de toda requisição, e de mais um estado para expirar
  certo. Pagar 5 s uma vez por instância é mais barato que isso.
- **Limite menor na requisição (1 s) sem janela:** 200 arquivos ainda seriam
  200 s de espera por ciclo, e 1 s é curto demais para um broker lento mas vivo,
  que passaria a ser tratado como fora.
- **Pular o despacho na requisição para o subject `service:connectors`:** amarra
  o comportamento à identidade de quem chama, deixa o operador pagando 5 s em
  toda escrita enquanto o broker estiver fora, e faz a indexação da base
  sincronizada esperar até 30 s mesmo com o broker no ar.
- **Sem despacho na requisição** (só varredura): já descartado em D3.

### Árvore de pastas

```
apps/api/src/Buteco.Api/
  Knowledge/Indexing/
    KnowledgeIndexingRequest.cs              (novo: entidade do pedido)
    KnowledgeIndexingRequestDispatcher.cs    (novo: despacho, D3)
    KnowledgeIndexingRequestSweepService.cs  (novo: BackgroundService, D3)
    KnowledgeIndexingRequestSchedule.cs      (novo: intervalo e lote, trocado por DI nos testes)
    KnowledgeIndexingDispatchWindow.cs       (novo: janela de D8, em memória, por instância)
    RabbitMqKnowledgeIndexingJobPublisher.cs (alterado: confirmação, D5)
  Infrastructure/
    AppDbContext.cs                          (alterado: DbSet e mapeamento)
    Migrations/<data>_AddKnowledgeIndexingRequests.cs (+ Designer, snapshot)
  KnowledgeDocuments/Commands/
    CreateKnowledgeDocument/CreateKnowledgeDocumentCommandHandler.cs     (alterado)
    UpdateKnowledgeDocument/UpdateKnowledgeDocumentCommandHandler.cs     (alterado)
    ReindexKnowledgeDocument/ReindexKnowledgeDocumentCommandHandler.cs   (alterado)
  KnowledgeSync/Commands/UpsertSyncedDocument/UpsertSyncedDocumentCommandHandler.cs (alterado)
  Program.cs                                 (alterado: registro do despacho e do serviço)

apps/api/tests/Buteco.Api.Tests/
  Knowledge/KnowledgeDocumentCatalogTests.IndexingRequests.cs (novo, partial: aceite nos quatro caminhos e o caso permanente)
  Knowledge/KnowledgeDocumentCatalogTests.IndexingDispatch.cs (novo, partial: despacho, varredura, janela, SQL emitido)
  Knowledge/KnowledgeIndexingDispatchWindowTests.cs    (novo: janela com FakeTimeProvider, sem contêiner)
  Knowledge/KnowledgeIndexingPublisherDependencyTests.cs (novo: D7 por reflexão, sem contêiner)
  A2ATaskLifecycleTests.IndexingConfirm.cs             (novo, partial: broker real da fixture que já existe)
  A2ATaskLifecycleTests.cs                             (alterado: passa a partial)
  KnowledgeIndexingRequestsMigrationTests.cs           (novo: D6, na MigrationPostgresCollection)
  Support/FakeKnowledgeIndexingJobPublisher.cs         (alterado: modos indisponível e bloqueante, contador)
  Support/ApiFactoryFixture.cs                         (alterado: varredura a cada hora)

apps/workers/tests/Buteco.Workers.Tests/
  Knowledge/KnowledgeIndexingTests.cs                  (alterado: reentrega da mesma revisão)
```

**Corrigido na implementação — os testes não criam fonte de contêiner nova.** O
rascunho da árvore listava classes novas com fixture própria, e cada uma subiria
um Postgres (critério do `02`: classes com fixture de contêiner). Os testes que
precisam de banco entraram como `partial` de classes existentes
(`KnowledgeDocumentCatalogTests`, `A2ATaskLifecycleTests`) ou na
`MigrationPostgresCollection`; os dois que não precisam são classes novas sem
contêiner.

**Corrigido na implementação — a fixture agenda a varredura para uma hora.** Com
os 30 s de produção, um tique automático no meio de um teste poderia tomar o
pedido de uma escrita entre o commit e o despacho dela, e a publicação sairia
depois da resposta. Os testes chamam o ciclo direto; o que prova o serviço
rodando sozinho deriva um host com 200 ms.

Nada em `libs/`.

## Risks / Trade-offs

Cada risco tem contraparte verificável (convenção 10).

- **[R1] Publicação duplicada da mesma revisão** (processo morre entre a
  confirmação e o commit; migração de D6) → dado certo, uma indexação a mais.
  **Verificação:** teste no `apps/workers` que entrega duas mensagens da mesma
  revisão e afirma o conjunto de fragmentos igual ao de uma entrega só (mesma
  contagem, sem fragmento repetido) e o documento em `Indexed`.
- **[R2] Duas instâncias publicando o mesmo pedido** → `FOR UPDATE` (a trava) e
  `SKIP LOCKED` (pular em vez de esperar); ver a correção em D3.
  **Verificação:** teste com dois despachos concorrentes sobre os mesmos
  pedidos, publisher lento, e cada pedido publicado exatamente uma vez. Como a
  corrida pode não acontecer numa execução (convenção 15, quinta forma), o par
  determinístico: o SQL emitido pelo despacho, capturado com
  `EmittedSqlCapture`, termina em `FOR UPDATE SKIP LOCKED`.
- **[R3] O despacho na requisição atrasar a resposta com o broker inacessível**
  → limite de 5 s. **Verificação:** teste com publisher que bloqueia além do
  limite e resposta de escrita antes de 5 s mais folga, com o pedido gravado.
- **[R4] A varredura morrer na primeira falha de banco e parar para sempre**
  (convenção 4) → `try/catch` por ciclo envolvendo a consulta. **Verificação:**
  teste com a primeira consulta falhando e o pedido publicado num ciclo
  seguinte.
- **[R5] "Publicado" sem o broker ter aceitado** → confirmação (D5).
  **Verificação:** teste contra o RabbitMQ real da `A2ATaskLifecycleFixture`
  (contêiner que já existe), lendo a mensagem de `knowledge-indexing`. Se a
  fixture não servir e for preciso uma classe de contêiner nova, a tarefa para
  e pede autorização ao mantenedor.
- **[R6] Pedido obsoleto despachado depois de uma revisão nova** (broker volta
  com N e N+1 na tabela) → descarte por `ContentRevision` do `apps/workers`,
  que já existe e tem teste (`Trabalho obsoleto é descartado, não gravado`).
  **Verificação no `apps/api`:** com o broker fora, duas atualizações geram dois
  pedidos com revisões N e N+1, e o despacho publica as duas, em ordem.
- **[R7] Janela do deploy:** o `apps/api` antigo continua no ar entre a migração
  e o novo subir, e uma escrita com o broker fora nessa janela cria órfão sem
  pedido. **Não testável** de forma útil: depende da sequência de deploy, não de
  código. Mitigação: a janela é de segundos, e o órfão nela é recuperável por
  reindexação (manual) ou pela edição seguinte. Registrado aqui, sem cenário.
- **[R8] Backlog grande na tabela com o broker fora por horas** → lote de 100 e
  ordem por `CreatedAt`; a varredura esvazia em ciclos. Volume de referência: a
  primeira sincronização de base grande é o pior caso, e cada pedido é uma
  linha pequena. **Verificação:** teste com mais pedidos que o lote, todos
  publicados depois de o broker voltar.
- **[R9] O ciclo da #105 travado pelo limite de 5 s com o broker
  inacessível.** O ciclo faz um upsert por arquivo, em sequência. Sem a janela
  de D8, uma base de **200 arquivos** com o broker inacessível levaria
  200 × 5 s = **1.000 s, cerca de 16 min 40 s** só esperando o despacho na
  requisição — mais de três vezes o intervalo de 5 min do ciclo: o ciclo
  seguinte já teria vencido antes de este terminar. É o motivo de D8. Com a
  janela, o mesmo ciclo paga no máximo **um** limite de 5 s a cada 30 s por
  instância. **Verificação:** teste com uma sequência de escritas e a
  publicação bloqueando: só a primeira espera o limite, as seguintes respondem
  rápido, todos os pedidos são gravados e publicados quando a publicação volta;
  e o guarda — retirar a janela faz o teste reprovar pelo tempo.

## Migration Plan

1. A migração `AddKnowledgeIndexingRequests` cria a tabela e insere os pedidos
   de D6. Roda pelo `migrator`, como toda migração.
2. O `apps/api` novo sobe e a varredura publica os pedidos no primeiro ciclo.
3. **Rollback:** reverter a imagem do `apps/api` deixa a tabela sem leitor; os
   pedidos nela ficam parados, e o código antigo volta a publicar direto. O
   `Down` da migração apaga a tabela. Nenhum dado de documento é tocado em
   nenhum dos dois sentidos.

## Open Questions

Nenhuma de produto.

## Achados

- **Despacho de task A2A** (`apps/api/src/Buteco.Api/A2A/EnqueueingAgentHandler.cs:83`):
  a task é gravada `Submitted` em `a2a_tasks` e o `TaskJobMessage` é publicado
  depois, sem garantia. Com o broker fora, a task fica `Submitted` sem job. O
  `NonTerminalTaskDetectorService` do `apps/workers` observa tasks
  não-terminais envelhecidas, mas não as recupera. Fora do escopo desta change
  (outra fila, outra máquina de estados, contrato do protocolo A2A). Registrado
  na **#144**, que pede o mesmo desenho desta change aplicado à fila de
  execução. Diferente da #138, esse caminho já atende tráfego no piloto em
  produção.
