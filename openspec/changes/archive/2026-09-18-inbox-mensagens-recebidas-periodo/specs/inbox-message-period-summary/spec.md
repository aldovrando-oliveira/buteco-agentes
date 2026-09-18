## ADDED Requirements

### Requirement: Contagem de mensagens de entrada recebidas em um intervalo

O sistema SHALL expor, em `apps/inbox`, uma rota HTTP de leitura
`GET /messages/summary` que recebe um intervalo de datas e responde `200` com um
objeto contendo a quantidade de mensagens de **entrada** cujo instante de
recebimento (`OccurredAt`) está dentro desse intervalo, considerando **todas as
mensagens do sistema**, sem recorte por canal, por contato nem por sessão.

O campo da resposta SHALL se chamar `inboundCount`. O nome SHALL usar o
vocabulário de direção da entidade (`Inbound`), e não o termo "recebidas": este
último é ambíguo quanto ao ponto de vista — do contato, a mensagem "recebida" é a
que o sistema enviou — e o contrato não SHALL depender de um referencial que ele
não declara.

A rota SHALL exigir autenticação, como toda rota de `apps/inbox` que não esteja
explicitamente classificada como anônima.

A contagem SHALL ser calculada por agregação no banco de dados, com custo
independente do número de mensagens existentes — nunca materializando as
mensagens para contá-las em memória.

#### Scenario: Mensagens de entrada dentro do intervalo são contadas

- **WHEN** a rota é chamada com um intervalo `[from, to]` e existem mensagens de
  entrada cujo `OccurredAt` está estritamente entre `from` e `to`
- **THEN** a resposta é `200` com `inboundCount` igual à quantidade dessas
  mensagens

#### Scenario: A contagem não se restringe a um canal, contato ou sessão

- **WHEN** existem mensagens de entrada em sessões de contatos de canais
  diferentes, todas com `OccurredAt` dentro do intervalo
- **THEN** todas elas são contadas na mesma resposta, sem que a chamada informe
  canal, contato ou sessão

### Requirement: Somente mensagens de entrada são contadas

O sistema SHALL contar exclusivamente mensagens cuja direção seja de **entrada**
(`Inbound`), e SHALL excluir da contagem toda mensagem de **saída**
(`Outbound`), independentemente de seu estado de entrega.

Uma mensagem de saída dentro do intervalo SHALL NOT incrementar `inboundCount`,
nem quando sua entrega ao canal tenha sido bem-sucedida, nem quando tenha
falhado.

#### Scenario: Mensagem de saída dentro do intervalo não entra em inboundCount

- **WHEN** existem mensagens de saída cujo `OccurredAt` está dentro do intervalo,
  e nenhuma mensagem de entrada nesse mesmo intervalo
- **THEN** a resposta é `200` com `inboundCount` igual a `0`

### Requirement: A unidade contada é a mensagem distinta, não a entrega de webhook

O sistema SHALL contar **mensagens recebidas distintas**, e não chamadas de
webhook atendidas. Quando o provedor de canal reentrega o mesmo evento — mesma
mensagem, mesmo identificador externo, na mesma sessão —, a mensagem SHALL ser
contada **uma única vez**.

Esta é uma garantia observável do contrato da rota, e não um efeito colateral de
implementação: quem consome `inboundCount` SHALL poder lê-lo como "quantas
mensagens chegaram", nunca como "quantas vezes o provedor chamou o sistema".

#### Scenario: Webhook reentregue conta uma vez

- **WHEN** o mesmo evento de mensagem de entrada é entregue mais de uma vez pelo
  provedor, com o mesmo identificador externo e na mesma sessão, e o instante
  cai dentro do intervalo
- **THEN** a resposta é `200` com `inboundCount` igual a `1`, e não igual ao
  número de entregas

### Requirement: A inclusão depende do instante da mensagem, não do da sessão

O sistema SHALL decidir a inclusão de cada mensagem pelo seu próprio
`OccurredAt`, **sem considerar** o instante em que a sessão a que ela pertence
foi iniciada, nem seu estado de encerramento.

Em particular, uma mensagem recebida dentro do intervalo SHALL ser contada mesmo
quando a sessão que a contém tenha sido iniciada **antes** do intervalo. Esta
rota e a rota de contagem de sessões iniciadas por período respondem perguntas
diferentes, e seus números SHALL NOT ser tratados como implicando um ao outro.

#### Scenario: Mensagem recebida dentro do intervalo em sessão iniciada antes dele é contada

- **WHEN** existe uma sessão cujo início é anterior a `from`, contendo uma
  mensagem de entrada cujo `OccurredAt` está dentro de `[from, to]`
- **THEN** essa mensagem é incluída em `inboundCount`

### Requirement: Os dois limites do intervalo são inclusivos

O sistema SHALL contar uma mensagem cujo `OccurredAt` seja **exatamente igual** a
`from`, e SHALL contar uma mensagem cujo `OccurredAt` seja **exatamente igual** a
`to`. O critério é `OccurredAt >= from AND OccurredAt <= to`.

#### Scenario: Mensagem com OccurredAt exatamente igual a from é contada

- **WHEN** existe uma mensagem de entrada cujo `OccurredAt` é exatamente o valor
  informado em `from`
- **THEN** essa mensagem é incluída em `inboundCount`

#### Scenario: Mensagem com OccurredAt exatamente igual a to é contada

- **WHEN** existe uma mensagem de entrada cujo `OccurredAt` é exatamente o valor
  informado em `to`
- **THEN** essa mensagem é incluída em `inboundCount`

### Requirement: Mensagens fora do intervalo não são contadas

O sistema SHALL excluir da contagem toda mensagem cujo `OccurredAt` seja anterior
a `from` ou posterior a `to`.

#### Scenario: Mensagem recebida antes do intervalo não é contada

- **WHEN** existe uma mensagem de entrada cujo `OccurredAt` é anterior a `from`
- **THEN** essa mensagem não é incluída em `inboundCount`

#### Scenario: Mensagem recebida depois do intervalo não é contada

- **WHEN** existe uma mensagem de entrada cujo `OccurredAt` é posterior a `to`
- **THEN** essa mensagem não é incluída em `inboundCount`

### Requirement: Intervalo sem mensagens responde contagem zero, não erro

O sistema SHALL responder `200` com `inboundCount` igual a `0` quando nenhuma
mensagem de entrada tiver `OccurredAt` dentro do intervalo informado. Esse zero é
uma **contagem medida** — a agregação percorreu as mensagens e não encontrou
nenhuma — e SHALL NOT ser reportado como erro, como ausência de recurso, nem como
resposta vazia sem corpo.

#### Scenario: Intervalo válido sem nenhuma mensagem retorna zero com 200

- **WHEN** a rota é chamada com um intervalo válido no qual nenhuma mensagem de
  entrada foi recebida
- **THEN** a resposta é `200` com corpo contendo `inboundCount` igual a `0`

### Requirement: Ambos os limites são obrigatórios

O sistema SHALL exigir os dois limites do intervalo em toda chamada, e SHALL
responder `400` com corpo de erro de validação quando `from`, `to`, ou ambos,
estiverem ausentes. O sistema SHALL NOT assumir um intervalo implícito, um limite
padrão, nem um período relativo ao instante da requisição.

#### Scenario: Chamada sem o limite inicial é rejeitada

- **WHEN** a rota é chamada informando apenas `to`
- **THEN** a resposta é `400` com corpo de erro de validação identificando `from`

#### Scenario: Chamada sem o limite final é rejeitada

- **WHEN** a rota é chamada informando apenas `from`
- **THEN** a resposta é `400` com corpo de erro de validação identificando `to`

#### Scenario: Chamada sem nenhum dos dois limites é rejeitada

- **WHEN** a rota é chamada sem informar `from` nem `to`
- **THEN** a resposta é `400` com corpo de erro de validação identificando os
  dois limites na mesma resposta

### Requirement: Limite malformado é rejeitado na mesma forma de corpo que limite ausente

O sistema SHALL responder `400` com corpo de erro de validação quando um limite
informado não puder ser interpretado como instante. Esse corpo SHALL ter
**exatamente o mesmo formato** do corpo devolvido para limite ausente — a rota
SHALL NOT ter duas formas de erro conforme o defeito seja "ausente" ou
"malformado".

O sistema SHALL interpretar os limites de forma determinística e independente do
fuso horário do processo: um valor sem deslocamento de fuso SHALL ser
interpretado como UTC, e um valor com deslocamento explícito SHALL ser
normalizado para UTC antes da comparação.

#### Scenario: Limite não interpretável é rejeitado

- **WHEN** a rota é chamada com um valor que não é um instante válido em `from`
- **THEN** a resposta é `400` com corpo de erro de validação identificando `from`

#### Scenario: O corpo do erro de limite malformado tem o mesmo formato do de limite ausente

- **WHEN** uma chamada é rejeitada por limite malformado e outra por limite
  ausente
- **THEN** as duas respostas têm o mesmo formato de corpo, diferindo apenas na
  mensagem

#### Scenario: Limite sem deslocamento de fuso é interpretado como UTC

- **WHEN** a rota é chamada com limites sem deslocamento de fuso explícito
- **THEN** a contagem é a mesma que seria obtida informando os mesmos instantes
  com deslocamento `+00:00` explícito

#### Scenario: Limite com deslocamento de fuso explícito não-UTC é normalizado para UTC

- **WHEN** a rota é chamada com um limite informado com deslocamento de fuso
  explícito diferente de UTC
- **THEN** a contagem é a mesma que seria obtida informando o mesmo instante já
  normalizado para UTC

### Requirement: Intervalo invertido é rejeitado

O sistema SHALL responder `400` com corpo de erro de validação quando `to` for
anterior a `from`. Um intervalo em que `from` e `to` são iguais SHALL ser
aceito — pelos limites inclusivos, ele seleciona as mensagens recebidas
exatamente naquele instante.

A verificação de ordem SHALL ocorrer somente depois de os dois limites terem sido
interpretados com sucesso; um limite malformado SHALL ser reportado como
malformado, nunca como intervalo invertido.

#### Scenario: to anterior a from é rejeitado

- **WHEN** a rota é chamada com `to` anterior a `from`, ambos válidos
- **THEN** a resposta é `400` com corpo de erro de validação

#### Scenario: from igual a to é aceito

- **WHEN** a rota é chamada com `from` e `to` iguais
- **THEN** a resposta é `200`, contando as mensagens de entrada recebidas
  exatamente naquele instante

#### Scenario: Limite malformado em intervalo aparentemente invertido é reportado como malformado

- **WHEN** a rota é chamada com um limite malformado
- **THEN** a resposta identifica o limite malformado, e não um intervalo
  invertido
