# inbox-session-period-summary Specification

## Purpose

TBD - defined by change inbox-sessoes-por-periodo. Update Purpose after archive.

## Requirements

### Requirement: Contagem de sessões iniciadas em um intervalo

O sistema SHALL expor, em `apps/inbox`, uma rota HTTP de leitura
`GET /sessions/summary` que recebe um intervalo de datas e responde `200` com um
objeto contendo a quantidade de sessões cujo instante de início (`StartedAt`)
está dentro desse intervalo, considerando **todas as sessões do sistema**, sem
recorte por canal nem por contato.

O campo da resposta SHALL se chamar `startedCount` — a semântica é "conversas
**iniciadas** no período", e não "conversas com atividade no período" nem
"sessões vivas durante o período". A rota SHALL exigir autenticação, como toda
rota de `apps/inbox` que não esteja explicitamente classificada como anônima.

A contagem SHALL ser calculada por agregação no banco de dados, com custo
independente do número de sessões existentes — nunca materializando as sessões
para contá-las em memória.

#### Scenario: Sessões iniciadas dentro do intervalo são contadas

- **WHEN** a rota é chamada com um intervalo `[from, to]` e existem sessões cujo
  `StartedAt` está estritamente entre `from` e `to`
- **THEN** a resposta é `200` com `startedCount` igual à quantidade dessas
  sessões

#### Scenario: A contagem não se restringe a um canal ou contato

- **WHEN** existem sessões de contatos de canais diferentes com `StartedAt`
  dentro do intervalo
- **THEN** todas elas são contadas na mesma resposta, sem que a chamada informe
  canal ou contato

### Requirement: Os dois limites do intervalo são inclusivos

O sistema SHALL contar uma sessão cujo `StartedAt` seja **exatamente igual** a
`from`, e SHALL contar uma sessão cujo `StartedAt` seja **exatamente igual** a
`to`. O critério é `StartedAt >= from AND StartedAt <= to`.

#### Scenario: Sessão com StartedAt exatamente igual a from é contada

- **WHEN** existe uma sessão cujo `StartedAt` é exatamente o valor informado em
  `from`
- **THEN** essa sessão é incluída em `startedCount`

#### Scenario: Sessão com StartedAt exatamente igual a to é contada

- **WHEN** existe uma sessão cujo `StartedAt` é exatamente o valor informado em
  `to`
- **THEN** essa sessão é incluída em `startedCount`

### Requirement: Sessões fora do intervalo não são contadas

O sistema SHALL excluir da contagem toda sessão cujo `StartedAt` seja anterior a
`from` ou posterior a `to`, **independentemente de sua atividade posterior ou de
seu estado de encerramento**. Em particular, uma sessão iniciada antes de `from`
não SHALL ser contada por ter recebido mensagens durante o intervalo, nem por ter
`ClosedAt` nulo.

#### Scenario: Sessão iniciada antes do intervalo não é contada

- **WHEN** existe uma sessão cujo `StartedAt` é anterior a `from`
- **THEN** essa sessão não é incluída em `startedCount`

#### Scenario: Sessão iniciada depois do intervalo não é contada

- **WHEN** existe uma sessão cujo `StartedAt` é posterior a `to`
- **THEN** essa sessão não é incluída em `startedCount`

#### Scenario: Atividade dentro do intervalo não inclui sessão iniciada antes dele

- **WHEN** existe uma sessão cujo `StartedAt` é anterior a `from` e cujo
  `LastActivityAt` está dentro de `[from, to]`
- **THEN** essa sessão não é incluída em `startedCount`

#### Scenario: Sessão sem encerramento registrado não é contada por isso

- **WHEN** existe uma sessão cujo `StartedAt` é anterior a `from` e cujo
  `ClosedAt` é nulo
- **THEN** essa sessão não é incluída em `startedCount`

### Requirement: Intervalo sem sessões responde contagem zero, não erro

O sistema SHALL responder `200` com `startedCount` igual a `0` quando nenhuma
sessão tiver `StartedAt` dentro do intervalo informado. Esse zero é uma
**contagem medida** — a agregação percorreu as sessões e não encontrou nenhuma —
e não SHALL ser reportado como erro, como ausência de recurso, nem como resposta
vazia sem corpo.

#### Scenario: Intervalo válido sem nenhuma sessão retorna zero com 200

- **WHEN** a rota é chamada com um intervalo válido no qual nenhuma sessão foi
  iniciada
- **THEN** a resposta é `200` com corpo contendo `startedCount` igual a `0`

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
não SHALL ter duas formas de erro conforme o defeito seja "ausente" ou
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
- **THEN** a contagem é a mesma que seria obtida informando o mesmo instante
  já normalizado para UTC

### Requirement: Intervalo invertido é rejeitado

O sistema SHALL responder `400` com corpo de erro de validação quando `to` for
anterior a `from`. Um intervalo em que `from` e `to` são iguais SHALL ser
aceito — pelos limites inclusivos, ele seleciona as sessões iniciadas exatamente
naquele instante.

A verificação de ordem SHALL ocorrer somente depois de os dois limites terem sido
interpretados com sucesso; um limite malformado SHALL ser reportado como
malformado, nunca como intervalo invertido.

#### Scenario: to anterior a from é rejeitado

- **WHEN** a rota é chamada com `to` anterior a `from`, ambos válidos
- **THEN** a resposta é `400` com corpo de erro de validação

#### Scenario: from igual a to é aceito

- **WHEN** a rota é chamada com `from` e `to` iguais
- **THEN** a resposta é `200`, contando as sessões iniciadas exatamente naquele
  instante

#### Scenario: Limite malformado em intervalo aparentemente invertido é reportado como malformado

- **WHEN** a rota é chamada com um limite malformado
- **THEN** a resposta identifica o limite malformado, e não um intervalo
  invertido
