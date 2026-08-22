# inbox-message-history-ui Specification

## Purpose

TBD - defined by change frontend-inbox-sessoes-historico. Update Purpose after archive.

## Requirements

### Requirement: Listagem de sessões do canal
O sistema SHALL prover, em `apps/frontend`, na aba Sessões do detalhe de
canal, uma lista das sessões desse canal consumindo
`GET /channels/{id}/sessions`, exibindo para cada sessão o nome de
exibição do contato (ou o `ContactExternalId` quando `ContactDisplayName`
for nulo), a prévia da última mensagem (quando existir) e o instante de
última atividade, na ordem em que a API as retorna (mais recente
primeiro).

#### Scenario: Sessão com nome de exibição
- **WHEN** a lista de sessões inclui uma sessão cujo contato tem
  `ContactDisplayName` preenchido
- **THEN** a linha dessa sessão exibe o `ContactDisplayName`

#### Scenario: Sessão sem nome de exibição usa o identificador externo
- **WHEN** a lista de sessões inclui uma sessão cujo contato tem
  `ContactDisplayName: null`
- **THEN** a linha dessa sessão exibe o `ContactExternalId` no lugar do
  nome, sem ficar sem identificação nenhuma

#### Scenario: Sessão sem prévia de mensagem
- **WHEN** a lista de sessões inclui uma sessão cujo `LastMessage` é
  nulo (ex. sessão criada antes da persistência de mensagens existir,
  sem nenhuma `Message` associada)
- **THEN** a linha dessa sessão exibe o nome (ou fallback) e o instante
  de última atividade normalmente, sem prévia de mensagem e sem tratar
  a ausência como erro

#### Scenario: Canal sem nenhuma sessão
- **WHEN** o usuário acessa a aba Sessões de um canal sem nenhuma sessão
  registrada
- **THEN** a interface exibe um estado vazio indicando que não há
  sessões, sem erro nem tela em branco

#### Scenario: Carregamento da lista de sessões
- **WHEN** a consulta a `GET /channels/{id}/sessions` está em andamento
- **THEN** a interface exibe um indicador de carregamento na aba Sessões

#### Scenario: Erro ao carregar a lista de sessões
- **WHEN** a consulta a `GET /channels/{id}/sessions` falha (erro de rede
  ou resposta de erro do servidor)
- **THEN** a interface exibe um estado de erro na aba Sessões, sem
  quebrar a navegação do restante da aplicação

### Requirement: Timeline de mensagens de uma sessão com URL própria
O sistema SHALL, ao selecionar uma sessão na lista, navegar para
`/channels/{channelId}/sessions/{sessionId}` e exibir, lado a lado com a
lista de sessões, a timeline cronológica dessa sessão consumindo
`GET /sessions/{sessionId}/messages`, preservando a lista visível para
troca de sessão sem perda de contexto.

#### Scenario: Seleção de sessão abre a timeline em URL própria
- **WHEN** o usuário seleciona uma sessão na lista
- **THEN** a URL passa a ser
  `/channels/{channelId}/sessions/{sessionId}` e a timeline dessa sessão
  é exibida ao lado da lista

#### Scenario: Reload da URL da sessão mantém a timeline aberta
- **WHEN** o usuário recarrega a página estando em
  `/channels/{channelId}/sessions/{sessionId}`
- **THEN** a mesma timeline é carregada novamente, sem exigir nova
  seleção na lista

#### Scenario: Sessão sem nenhuma mensagem
- **WHEN** o usuário abre a timeline de uma sessão que não tem nenhuma
  `Message` persistida (ex. sessão criada antes da persistência de
  mensagens existir)
- **THEN** a interface exibe um estado vazio indicando que não há
  mensagens, sem erro nem tela em branco

#### Scenario: URL aponta para sessão inexistente
- **WHEN** o usuário acessa
  `/channels/{channelId}/sessions/{sessionId}` com um `sessionId` que
  não existe (ex. link antigo, sessão de outro canal) e
  `GET /sessions/{sessionId}/messages` retorna 404
- **THEN** a interface exibe, na área da timeline, o mesmo estado de erro
  genérico usado para outras falhas dessa consulta — não um estado
  dedicado de "sessão não encontrada"

#### Scenario: Carregamento da timeline
- **WHEN** a consulta a `GET /sessions/{sessionId}/messages` está em
  andamento
- **THEN** a interface exibe um indicador de carregamento na área da
  timeline

#### Scenario: Erro ao carregar a timeline
- **WHEN** a consulta a `GET /sessions/{sessionId}/messages` falha (erro
  de rede ou resposta de erro do servidor)
- **THEN** a interface exibe um estado de erro na área da timeline, sem
  quebrar a navegação do restante da aplicação

### Requirement: Indicador de envio de mensagem de saída nunca comunica entrega ou leitura
O sistema SHALL renderizar, para toda mensagem de saída, exatamente um
indicador de envio quando `DeliveryStatus: Sent`, e um indicador de erro
com o motivo de falha acessível (truncado, sem stack/detalhe interno
cru) quando `DeliveryStatus: Failed`. O sistema SHALL NOT renderizar
nenhum indicador que sugira entrega ao destinatário final ou leitura,
pois esse dado não é coletado por esta fatia.

#### Scenario: Mensagem de saída enviada mostra um único indicador
- **WHEN** a timeline inclui uma mensagem de saída com
  `DeliveryStatus: Sent`
- **THEN** a interface renderiza exatamente um indicador de envio para
  essa mensagem, e nenhum segundo indicador de entrega ou leitura

#### Scenario: Mensagem de saída com falha mostra motivo acessível
- **WHEN** a timeline inclui uma mensagem de saída com
  `DeliveryStatus: Failed`
- **THEN** a interface renderiza um indicador de erro para essa mensagem,
  com o `DeliveryFailureReason` (truncado) exposto de forma acessível

### Requirement: Status de dispatch da mensagem de entrada visível na timeline
O sistema SHALL renderizar, para toda mensagem de entrada, um indicador
distinguível para cada um dos quatro valores de `DispatchStatus`
(`Pending`, `Dispatching`, `Failed`, `Completed`), sem tentar diferenciar,
para `Failed`, qual das três causas que o backend agrupa sob esse valor
ocorreu.

#### Scenario: Mensagem de entrada pendente
- **WHEN** a timeline inclui uma mensagem de entrada com
  `DispatchStatus: Pending`
- **THEN** a interface renderiza um indicador distinto de pendente para
  essa mensagem

#### Scenario: Mensagem de entrada em processamento
- **WHEN** a timeline inclui uma mensagem de entrada com
  `DispatchStatus: Dispatching`
- **THEN** a interface renderiza um indicador distinto de em
  processamento para essa mensagem

#### Scenario: Mensagem de entrada com falha de dispatch
- **WHEN** a timeline inclui uma mensagem de entrada com
  `DispatchStatus: Failed`
- **THEN** a interface renderiza um indicador distinto de falha para essa
  mensagem, sem indicar qual causa específica ocorreu

#### Scenario: Mensagem de entrada concluída
- **WHEN** a timeline inclui uma mensagem de entrada com
  `DispatchStatus: Completed`
- **THEN** a interface renderiza um indicador distinto de concluído para
  essa mensagem

#### Scenario: Os quatro indicadores são visualmente distinguíveis entre si
- **WHEN** a timeline inclui mensagens de entrada com os quatro valores
  de `DispatchStatus`
- **THEN** cada um dos quatro indicadores é visualmente distinto dos
  outros três

### Requirement: Mensagem de mídia mostra marcador de tipo sem tentar carregar binário
O sistema SHALL, para toda mensagem com `ContentType` diferente de
`Text` (`Image`, `Audio` ou `Document`), renderizar o marcador textual
persistido junto com uma indicação do tipo de mídia, e SHALL NOT tentar
renderizar conteúdo binário (ex. uma tag de imagem apontando para uma
URL que não existe).

#### Scenario: Mensagem de imagem mostra marcador com tipo
- **WHEN** a timeline inclui uma mensagem com `ContentType: Image`
- **THEN** a interface exibe o marcador textual da mensagem junto com uma
  indicação de que é uma imagem, sem nenhum elemento tentando carregar
  a imagem em si

#### Scenario: Mensagem de áudio ou documento mostra marcador com tipo
- **WHEN** a timeline inclui uma mensagem com `ContentType: Audio` ou
  `ContentType: Document`
- **THEN** a interface exibe o marcador textual da mensagem junto com uma
  indicação do tipo correspondente

### Requirement: Valor de enum desconhecido não é tratado como sucesso silencioso
O sistema SHALL, ao encontrar um valor de `DispatchStatus`,
`DeliveryStatus` ou `ContentType` que não reconhece (ex. um valor novo
adicionado no backend antes desta UI ser atualizada), renderizar um
indicador neutro e distinto dos indicadores de sucesso conhecidos
(`Completed`, `Sent`), e SHALL NOT tratar esse valor desconhecido como
equivalente a sucesso.

O sistema SHALL, ao encontrar um valor de `Direction` que não reconhece
(nem `Inbound` nem `Outbound`), renderizar a mensagem em uma forma
neutra, sem escolher lado (entrada ou saída) e sem renderizar nenhum dos
dois blocos de status (`DispatchStatus` ou `DeliveryStatus`), e SHALL NOT
assumir entrada ou saída por default.

#### Scenario: `DispatchStatus` desconhecido não é exibido como concluído
- **WHEN** a timeline inclui uma mensagem de entrada com um
  `DispatchStatus` que não corresponde a nenhum dos quatro valores
  conhecidos
- **THEN** a interface renderiza um indicador neutro de status
  desconhecido, distinto do indicador usado para `Completed`

#### Scenario: `DeliveryStatus` desconhecido não é exibido como enviado
- **WHEN** a timeline inclui uma mensagem de saída com um
  `DeliveryStatus` que não corresponde a `Sent` nem a `Failed`
- **THEN** a interface renderiza um indicador neutro de status
  desconhecido, distinto do indicador usado para `Sent`

#### Scenario: `ContentType` desconhecido mostra marcador genérico
- **WHEN** a timeline inclui uma mensagem com `ContentType` que não
  corresponde a `Text`, `Image`, `Audio` nem `Document`
- **THEN** a interface exibe o marcador textual da mensagem com uma
  indicação genérica de tipo não reconhecido, sem tentar carregar
  binário

#### Scenario: `Direction` desconhecido não escolhe lado nem bloco de status
- **WHEN** a timeline inclui uma mensagem com um `Direction` que não
  corresponde a `Inbound` nem a `Outbound`
- **THEN** a interface renderiza essa mensagem em forma neutra, sem
  colocá-la no lado de entrada nem no de saída, e sem renderizar
  indicador de `DispatchStatus` nem de `DeliveryStatus` para ela

### Requirement: Atualização periódica da timeline aberta
O sistema SHALL atualizar automaticamente a timeline de uma sessão
aberta a cada 5 segundos enquanto a página estiver em primeiro plano, e
SHALL NOT aplicar atualização automática periódica à lista de sessões.

#### Scenario: Nova mensagem aparece sem ação do operador
- **WHEN** uma nova `Message` é persistida para a sessão cuja timeline
  está aberta
- **THEN** a mensagem aparece na timeline em até 5 segundos, sem o
  operador precisar recarregar a página

#### Scenario: Timeline em segundo plano não atualiza
- **WHEN** a aba do navegador com a timeline aberta perde o foco
- **THEN** a atualização automática periódica é pausada até a aba voltar
  ao primeiro plano

Nota: sem task de teste dedicada — o comportamento é o default
`refetchIntervalInBackground: false` do react-query, não código escrito
nesta change (justificativa em design.md, Decisão 4).
