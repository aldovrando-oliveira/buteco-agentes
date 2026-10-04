**Issue:** #138

## Why

Toda escrita do `apps/api` que pede indexação grava o documento e **depois**
publica na fila `knowledge-indexing`, sem tratar a falha da publicação. Com o
RabbitMQ fora do ar o documento fica gravado em `Pending`, o cliente recebe
`500`, e nenhum job existe. Na base manual o operador ainda pode reindexar à
mão; na base sincronizada o defeito é **permanente**: o upsert já gravou o
`externalVersion` novo, o ciclo seguinte do `apps/connectors` (#105) não reenvia
o arquivo, e o upsert do mesmo conteúdo responde `Unchanged`, que não publica
nada. A #119 (implantar o `apps/connectors` em produção) está bloqueada por
este caminho aberto.

Medido na tarefa 1.2 da change `ciclo-de-sincronizacao` (#105): host do
`apps/api` sem RabbitMQ, o primeiro upsert de `/sync` respondeu `500` com
`BrokerUnreachableException`, e o upsert seguinte do mesmo conteúdo respondeu
`200` com `outcome: "Unchanged"`.

## What Changes

- **Pedido de indexação durável.** Toda escrita que hoje publica passa a gravar
  um **pedido de indexação** (`knowledge_indexing_requests`: documento e
  revisão) no **mesmo `SaveChanges`** do documento. O documento e o pedido são
  gravados juntos ou nenhum dos dois.
- **Despacho do pedido no `apps/api`.** Um componente único publica os pedidos
  na fila e só os apaga depois que o broker confirmou a mensagem: na própria
  requisição, logo depois do commit (o caminho normal, sem latência nova de
  indexação), e numa varredura periódica que reenvia o que ficou para trás.
  Várias instâncias do `apps/api` não publicam o mesmo pedido ao mesmo tempo
  (`FOR UPDATE SKIP LOCKED`). Depois de uma falha de despacho, a instância pula
  a tentativa na requisição por 30 s, para que o ciclo de sincronização não
  espere o limite em cada upsert.
- **A escrita deixa de responder `500` pela fila.** Com o broker fora do ar,
  cadastro, atualização, reindexação e upsert respondem o mesmo sucesso de hoje
  (`201`/`200`, e `Created`/`Updated` no upsert): o documento **foi** gravado e
  o pedido de indexação também. O estado servido continua `Pending`, que é
  verdade.
- **Publicação com confirmação do broker** no publisher de indexação do
  `apps/api`, para que "publicado" signifique "o broker aceitou", e não "foi
  escrito no socket".
- **Recuperação dos documentos que já estão órfãos.** A migração que cria a
  tabela grava um pedido para cada documento em `Pending` naquele momento.
- **Nenhum outro caminho publica.** Só o despacho depende do publisher de
  indexação no `apps/api`; um teste de arquitetura prende isso.
- Sem mudança de contrato de fio, de rota, de fila, de mensagem ou de tela. O
  `apps/workers` não muda de comportamento: o descarte por `ContentRevision`
  continua sendo a defesa contra pedido obsoleto, e ganha um teste de reentrega
  da mesma revisão.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-document-indexing`: a publicação deixa de ser "depois da gravação,
  sem garantia" e passa a ser um pedido durável gravado junto com o documento e
  despachado com confirmação do broker, inclusive com várias instâncias; a
  reindexação passa pelo mesmo pedido; documentos já órfãos são recuperados na
  migração.
- `knowledge-document-catalog`: cadastro e atualização respondem sucesso quando
  o documento foi gravado, mesmo com a fila fora do ar, e o documento é
  indexado depois sem ação do operador.
- `knowledge-sync-service-api`: o upsert responde sucesso com a fila fora do
  ar, e o documento sincronizado é indexado depois mesmo que o ciclo seguinte
  não reenvie o arquivo.

## Impact

- **`apps/api`**: entidade e migração novas (`knowledge_indexing_requests`, com
  recuperação dos órfãos); os quatro handlers de escrita (cadastro,
  atualização, reindexação, upsert) deixam de chamar o publisher; componente de
  despacho novo, o **primeiro `BackgroundService` do `apps/api`**; publisher de
  indexação com confirmação do broker.
- **`apps/workers`**: só teste (reentrega da mesma revisão não gera dado
  errado). Nenhuma mudança de código de produção.
- **Sem mudança** em `apps/frontend`, `apps/connectors`, `apps/inbox`, no stack
  de produção, no contrato da fila e na mensagem.
- **Implantação**: a migração roda pelo `migrator` de sempre; nenhuma variável
  nova, nenhum contêiner novo.
- **Documentação**: `01` (Filas de trabalho), `docs/architecture.md`, `02` e
  `CHANGELOG.md`, em edições curtas e localizadas — a #106 e a #47 estão em
  andamento em paralelo e podem tocar os mesmos arquivos.
- **Fora de escopo**, registrado: o mesmo "publica depois e esquece" existe no
  despacho de task A2A (`apps/api/src/Buteco.Api/A2A/EnqueueingAgentHandler.cs:83`);
  é outra fila, outra máquina de estados, e está registrado na **#144**.
