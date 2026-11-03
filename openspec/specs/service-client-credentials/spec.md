# service-client-credentials Specification

## Purpose
Como os serviços obtêm o token de serviço no emissor do `apps/api`: o `apps/inbox` e o `apps/connectors` pedem o seu ao `/connect/token`, com o segredo do cliente, cache, pedido único e falha que aparece como o `apps/api` fora do ar; o `apps/api` gera o seu no processo, estreito (#235).
## Requirements
### Requirement: Serviço obtém o token de serviço no emissor pelo endereço interno
O `apps/inbox` e o `apps/connectors` SHALL obter o token de serviço com `POST {Api:BaseUrl}/connect/token`, pelo endereço interno, e SHALL NOT usar o `token_endpoint` anunciado no discovery. O corpo SHALL ser `application/x-www-form-urlencoded` codificado pela biblioteca, com os campos:

- `grant_type=client_credentials`;
- `client_id`, constante do código: `buteco-servico-inbox` no `apps/inbox` e `buteco-servico-connectors` no `apps/connectors`;
- `client_secret` igual a `ServiceClient:Secret`;
- `scope=buteco-api`.

O pedido SHALL usar um cliente HTTP próprio, sem o handler que anexa o token de serviço, com timeout de 5 segundos.

#### Scenario: O formulário chega íntegro ao emissor
- **WHEN** o serviço pede o token com um segredo que contém `+`, `/` e `=`
- **THEN** o endpoint recebe `grant_type`, `client_id` do serviço, `scope` `buteco-api` e o `client_secret` idêntico ao configurado

#### Scenario: Token do emissor real com a audience do apps/api
- **WHEN** o `apps/inbox` real pede o token ao `apps/api` real com o segredo do catálogo
- **THEN** o token recebido tem `client_id` `buteco-servico-inbox`, `aud` `buteco-api` e `typ` `at+jwt`

### Requirement: Token de serviço em cache até 1 minuto antes de vencer
O serviço SHALL reaproveitar o token obtido enquanto faltar mais de 1 minuto para o vencimento, contado pelo `expires_in` da resposta e pelo relógio local na chegada. Resposta sem `access_token` ou sem `expires_in` positivo SHALL ser tratada como falha do emissor.

#### Scenario: Duas chamadas, um pedido
- **WHEN** o serviço faz duas chamadas ao `apps/api` com o token em cache
- **THEN** o emissor recebe um único pedido de token

#### Scenario: Margem de 1 minuto
- **WHEN** o token tem `expires_in` de 900 segundos, e uma chamada sai 839 segundos depois da obtenção e outra 841 segundos depois
- **THEN** a primeira reaproveita o token, e a segunda pede um novo

#### Scenario: Resposta sem access_token
- **WHEN** o emissor responde `200` sem `access_token`
- **THEN** a chamada falha como falha do emissor, e nada entra no cache

### Requirement: Pedido único sob concorrência
Quando o cache não servir, chamadas simultâneas SHALL resultar num único pedido ao emissor, e todas SHALL receber o resultado dele.

#### Scenario: Dez chamadas simultâneas
- **WHEN** dez chamadas simultâneas encontram o cache vazio, e o emissor segura a resposta até as dez estarem esperando
- **THEN** o emissor recebe um único pedido, e as dez chamadas usam o mesmo token

### Requirement: Falha do emissor aparece como apps/api indisponível
Quando o emissor responder `503`, `invalid_client`, outro erro, resposta incompleta, ou não responder em 5 segundos, a chamada SHALL falhar com `HttpRequestException`. O cancelamento pedido pelo próprio chamador SHALL continuar `OperationCanceledException`. Durante 10 segundos depois de uma falha, todo pedido SHALL falhar na hora, sem chamada HTTP. Cada falha SHALL ser logada com o `client_id` e o motivo: `Error` para `invalid_client` e `Warning` para os demais. Nenhuma linha SHALL conter o segredo nem o valor de `Api:BaseUrl`.

#### Scenario: Emissor responde 503
- **WHEN** o emissor responde `503`
- **THEN** a chamada falha com `HttpRequestException`, e há um `Warning` com o `client_id`

#### Scenario: Segredo recusado
- **WHEN** o emissor responde `invalid_client`
- **THEN** a chamada falha com `HttpRequestException`, há um `Error` com o `client_id`, e nenhuma linha de log contém o segredo nem o `Api:BaseUrl`

#### Scenario: Emissor não responde
- **WHEN** o emissor não responde
- **THEN** a chamada falha com `HttpRequestException` depois do timeout de 5 segundos, sem esperar o timeout padrão do cliente HTTP

#### Scenario: Espera de 10 segundos depois da falha
- **WHEN** uma chamada sai 5 segundos depois de uma falha do emissor, e outra 11 segundos depois
- **THEN** a primeira falha sem pedido ao emissor, e a segunda faz o pedido

### Requirement: Linha de log do token obtido
O serviço SHALL logar em `Information` o primeiro token obtido e o primeiro token obtido depois de cada falha, com o `client_id` e a validade, sem o token. Os demais tokens obtidos SHALL NOT gerar essa linha.

#### Scenario: Primeiro token e recuperação
- **WHEN** o serviço obtém um token, renova-o depois da margem, falha uma vez e obtém outro
- **THEN** há exatamente duas linhas `Information` de token obtido: a do primeiro e a da recuperação

### Requirement: Token anexado pelo handler de saída, e 401 do destino sem repetição
O handler de saída de cada serviço SHALL anexar `Authorization: Bearer <token de serviço>` a toda chamada ao destino e SHALL NOT anexar token HMAC. Quando o destino responder `401`, o handler SHALL descartar o token do cache, se o anexado ainda for o do cache, e SHALL devolver a resposta sem repetir a chamada.

#### Scenario: 401 descarta o cache sem repetir
- **WHEN** o destino responde `401` a uma chamada
- **THEN** o destino recebeu essa chamada uma única vez, e a chamada seguinte pede um token novo

#### Scenario: 401 atrasado não derruba o token novo
- **WHEN** um `401` chega para um token que já foi trocado no cache
- **THEN** o token novo continua no cache

### Requirement: Segredo do serviço obrigatório e conferido no boot
O `ServiceClient:Secret` SHALL ser conferido depois da construção do host, sem I/O. No `apps/inbox`, sempre. No `apps/connectors`, só quando `Api:BaseUrl` estiver presente. O segredo SHALL:

- estar presente;
- ser base64 válido;
- ter pelo menos 32 bytes decodificado.

No `apps/inbox`, ele SHALL também ser distinto, por bytes decodificados, de `Inbox:CredentialEncryptionKey`. A falha SHALL derrubar o boot com mensagem que nomeia a chave, ou as duas chaves iguais, e SHALL NOT ecoar o valor.

#### Scenario: Segredo ausente no apps/inbox
- **WHEN** o `apps/inbox` sobe sem `ServiceClient:Secret`
- **THEN** a inicialização falha nomeando `ServiceClient:Secret`

#### Scenario: Segredo curto ou fora de base64
- **WHEN** o `apps/inbox` sobe com um segredo de 16 bytes, e depois com um valor fora de base64
- **THEN** a inicialização falha nas duas vezes nomeando `ServiceClient:Secret`, sem o valor na mensagem

#### Scenario: Segredo igual à chave de cifra do inbox
- **WHEN** o `apps/inbox` sobe com `ServiceClient:Secret` igual, em bytes, a `Inbox:CredentialEncryptionKey`
- **THEN** a inicialização falha nomeando `ServiceClient:Secret` e `Inbox:CredentialEncryptionKey`, sem o valor na mensagem

#### Scenario: apps/connectors sem Api:BaseUrl dispensa o segredo
- **WHEN** o `apps/connectors` sobe sem `Api:BaseUrl` e sem `ServiceClient:Secret`
- **THEN** o processo sobe

#### Scenario: apps/connectors com Api:BaseUrl exige o segredo
- **WHEN** o `apps/connectors` sobe com `Api:BaseUrl` e sem `ServiceClient:Secret`
- **THEN** a inicialização falha nomeando `ServiceClient:Secret`

### Requirement: apps/api gera o próprio token de serviço no processo, estreito
O `apps/api` SHALL gerar o token que manda ao `apps/connectors` dentro do próprio processo, assinado pelo anel de chaves do emissor, sem chamada HTTP e sem credencial guardada. O gerador SHALL emitir um único tipo de token:

- `client_id` e `sub` iguais a `buteco-servico-api`;
- `aud` `buteco-connectors`;
- `scope` `buteco-connectors`;
- validade de 15 minutos.

A superfície pública do gerador SHALL NOT receber `client_id`, audience, escopo nem duração. O token SHALL ter o mesmo cabeçalho de um token do `/connect/token`: `typ` `at+jwt`, o mesmo `alg` e o `kid` da credencial que assina. Ele SHALL ter também o mesmo conjunto de nomes de claims, sem exceção. Nenhuma linha SHALL ser gravada em `openiddict_tokens` para ele. O token SHALL ser reaproveitado até faltar 1 minuto para vencer, e o `401` do destino SHALL descartá-lo, como no serviço. `buteco-servico-api` SHALL NOT ser cadastrado no catálogo de clientes.

#### Scenario: Mesmo formato do /connect/token
- **WHEN** o gerador emite um token, e o `/connect/token` emite outro para o `buteco-servico-inbox`
- **THEN** os dois têm `typ` `at+jwt`, o mesmo `alg`, o mesmo `kid`, o mesmo `iss` e o mesmo conjunto de nomes de claims; o do gerador tem `client_id` `buteco-servico-api`, `aud` `buteco-connectors`, `scope` `buteco-connectors` e `exp` 15 minutos depois do `iat`

#### Scenario: Superfície estreita
- **WHEN** a superfície pública do gerador é inspecionada
- **THEN** nenhum método público tem parâmetro além do `CancellationToken`

#### Scenario: O apps/connectors real aceita o token gerado no processo
- **WHEN** o `apps/api` real chama a rota "descrever pasta" do `apps/connectors` real com o token gerado no processo, e o mesmo token chama `GET /connectors/providers`
- **THEN** a primeira chamada não recebe `401` nem `403`, e a segunda recebe `403`

#### Scenario: Nenhuma linha de token
- **WHEN** o gerador emite um token
- **THEN** a contagem de linhas em `openiddict_tokens` não muda

