## ADDED Requirements

### Requirement: Escrita do operador não falha pela fila de indexação
Cadastro e atualização de documento SHALL responder o mesmo sucesso de quando a
fila está disponível — `201` no cadastro, `200` na atualização — quando o
documento foi gravado e a publicação na fila de indexação falhou. O corpo SHALL
trazer o documento com `indexingStatus: "Pending"`. A escrita NÃO SHALL
responder `500` nem `503` por causa da fila, porque o documento e o pedido de
indexação foram gravados.

O documento SHALL ser indexado depois, quando a publicação voltar a funcionar,
sem ação do operador.

Falha ao **gravar** o documento continua sendo erro, e nada é gravado: o pedido de
indexação está na mesma transação.

#### Scenario: Cadastro com a fila indisponível responde 201 e indexa depois
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` válido
  enquanto a publicação na fila de indexação falha
- **THEN** a API responde HTTP 201 com `indexingStatus: "Pending"`, o documento
  existe uma vez só, e, quando a publicação volta a funcionar, exatamente uma
  mensagem de indexação com o id e a revisão do documento é publicada sem nova
  requisição

#### Scenario: Atualização com a fila indisponível responde 200 e indexa depois
- **WHEN** um cliente envia `PUT .../documents/{id}` com conteúdo diferente
  enquanto a publicação na fila de indexação falha
- **THEN** a API responde HTTP 200 com a revisão nova e `indexingStatus:
  "Pending"`, e, quando a publicação volta a funcionar, exatamente uma mensagem
  com essa revisão é publicada sem nova requisição

#### Scenario: Atualização só de título com a fila indisponível não grava pedido
- **WHEN** um cliente envia `PUT .../documents/{id}` só com título diferente
  enquanto a publicação falha
- **THEN** a API responde HTTP 200, nenhum pedido de indexação é gravado e
  nenhuma mensagem é publicada depois
