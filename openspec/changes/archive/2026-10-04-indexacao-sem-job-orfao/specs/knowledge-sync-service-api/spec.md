## ADDED Requirements

### Requirement: Upsert não falha pela fila de indexação, e o documento é indexado sem novo envio
O upsert `PUT /sync/knowledge-bases/{id}/documents` SHALL responder `200` com o
`documentId` e o `outcome` de sempre (`Created` ou `Updated`) quando o documento
foi gravado e a publicação na fila de indexação falhou. A escrita NÃO SHALL
responder `500` nem `503` por causa da fila: `503` significa contenção para o
`apps/connectors`, e a repetição do mesmo conteúdo é `Unchanged`, que não pede
indexação.

O pedido de indexação SHALL ser gravado na mesma transação que grava o
`externalVersion`. O documento SHALL ser indexado quando a publicação voltar a
funcionar, **sem depender** de o ciclo de sincronização reenviar o arquivo.

#### Scenario: Upsert com a fila indisponível responde 200 e indexa depois
- **WHEN** o `apps/connectors` envia o upsert de uma referência nova enquanto a
  publicação na fila de indexação falha
- **THEN** a API responde `200` com `outcome: "Created"`, e, quando a publicação
  volta a funcionar, exatamente uma mensagem de indexação com o id e a revisão
  do documento é publicada

#### Scenario: Ciclo seguinte sem reenvio não deixa o documento sem indexação
- **WHEN** um upsert com texto diferente grava o documento e o `externalVersion`
  novo enquanto a publicação falha, e o ciclo seguinte não reenvia o arquivo
  porque o marcador é igual ao gravado
- **THEN** quando a publicação volta a funcionar, exatamente uma mensagem com a
  revisão nova é publicada para o documento, sem nenhum upsert adicional

#### Scenario: Reenvio Unchanged depois da falha não duplica a indexação
- **WHEN** depois de um upsert gravado com a publicação falhando, o mesmo
  conteúdo é reenviado com marcador novo e a resposta é `Unchanged`
- **THEN** o reenvio não grava pedido novo, e, quando a publicação volta, é
  publicada exatamente uma mensagem para aquela revisão
