# knowledge-sync-folder-validation Specification

## Purpose

Cobre a **validação da pasta de uma base sincronizada**, que o `apps/api` faz no
`apps/connectors` antes de criar a base `Synced`: onde o `apps/api` encontra o
`apps/connectors` (`Connectors:BaseUrl`), qual rota chama e o que exige da
resposta, como os códigos e status de falha do `apps/connectors` chegam ao
cliente, o token de serviço `service:api` que assina cada chamada, e o teste de
ida e volta entre os dois apps reais.

## Requirements

### Requirement: Endereço do apps/connectors opcional
O `apps/api` SHALL ler o endereço do `apps/connectors` de `Connectors:BaseUrl`
(`Connectors__BaseUrl` no ambiente). Ausente ou vazio, o processo SHALL subir,
registrar um aviso no log de boot, e nenhuma rota além do cadastro de base `Synced`
SHALL mudar de comportamento; o cadastro de base `Synced` SHALL responder `503`
com `ProblemDetails` e `code: "connectors-not-configured"`, sem nenhuma chamada de
rede e sem criar base. Presente e diferente de uma URI absoluta `http` ou `https`,
a inicialização SHALL falhar com mensagem que nomeia `Connectors:BaseUrl` e NÃO
contém o valor configurado.

#### Scenario: Boot sem o endereço
- **WHEN** o `apps/api` sobe sem `Connectors:BaseUrl`, ou com o valor vazio
- **THEN** o processo sobe, `GET /knowledge-bases` responde 200 e
  `POST /knowledge-bases` sem `contentMode` responde 201

#### Scenario: Cadastro sincronizado sem o endereço
- **WHEN** um cliente cria uma base `Synced` válida com `Connectors:BaseUrl`
  ausente
- **THEN** a API responde HTTP 503 com `code: "connectors-not-configured"`, não
  cria nenhuma base e não faz nenhuma requisição HTTP de saída

#### Scenario: Endereço presente e inválido
- **WHEN** o `apps/api` sobe com `Connectors:BaseUrl` igual a `connectors:8080`
  ou `ftp://connectors`
- **THEN** a inicialização falha nomeando `Connectors:BaseUrl`, e a mensagem não
  contém o valor configurado

### Requirement: Validação da pasta pela rota de descrição do apps/connectors
Para criar uma base `Synced`, o `apps/api` SHALL chamar
`GET /connectors/providers/{provider}/folder?id={folderId}` no `apps/connectors`,
com o provedor e o id escapados para URL. A resposta `200` SHALL trazer `id`,
`name` e `webUrl` não vazios, `webUrl` como URI absoluta `http` ou `https`, e `id`
igual, por comparação ordinal, ao `folderId` pedido; o `apps/api` SHALL usar `name`
e `webUrl` como nome e URL da pasta. A chamada SHALL ter limite fixo de 35
segundos, não configurável.

#### Scenario: Pasta válida
- **WHEN** o `apps/connectors` responde 200 com o mesmo id, um nome e uma URL
  absoluta
- **THEN** a base é criada com esse nome e essa URL

#### Scenario: Id devolvido diferente do pedido
- **WHEN** o `apps/connectors` responde 200 com um `id` diferente do `folderId`
  pedido
- **THEN** a API responde HTTP 502 com `code: "connectors-error"` e não cria
  nenhuma base

#### Scenario: Limite da composição real
- **WHEN** o host de produção do `apps/api` é construído
- **THEN** o `HttpClient` usado para o `apps/connectors` tem `Timeout` de 35
  segundos e o handler que assina o token de serviço

### Requirement: Código e status das falhas do apps/connectors chegam ao cliente
Uma falha de validação SHALL responder `ProblemDetails` com a extensão `code` e
`detail`, sem traduzir o código em frase, e NUNCA `500`, e NÃO SHALL criar base.
Quando o `apps/connectors` responde erro com `code` no formato de código
(`^[a-z0-9]+(-[a-z0-9]+)*\z`, até 64 caracteres) e status `404`, `422`, `502` ou
`503`, o `apps/api` SHALL repassar o `code` e o `detail` como vieram, com status
`422` quando o do `apps/connectors` for `404` ou `422`, e o mesmo status nos demais.

O `apps/api` SHALL usar códigos próprios para a ligação entre os dois apps:

| situação | status | `code` | `detail` |
|---|---|---|---|
| conexão recusada, falha de rede ou limite de tempo estourado | `503` | `connectors-unavailable` | nulo |
| qualquer outra resposta: status fora da lista acima (inclusive `400`, `401`, `403` e `500`), erro sem `code` no formato, ou `200` fora do contrato | `502` | `connectors-error` | o status HTTP recebido, como texto |

`401` e `403` do `apps/connectors` NÃO SHALL ser repassados com o mesmo status.

#### Scenario: Cada código do apps/connectors chega como código
- **WHEN** o `apps/connectors` responde `access-denied` (422, detalhe com o e-mail
  da conta), `not-a-folder` (422), `folder-trashed` (422),
  `provider-not-configured` (404), `api-not-configured` (502), `rate-limited` (503)
  ou `provider-unavailable` (503)
- **THEN** a API responde, respectivamente, 422, 422, 422, 422, 502, 503 e 503,
  com `code` igual ao recebido e `detail` igual ao recebido (o e-mail no
  `access-denied`), e nenhuma base é criada

#### Scenario: Código desconhecido no formato passa
- **WHEN** o `apps/connectors` responde 422 com `code: "folder-too-deep"`
- **THEN** a API responde HTTP 422 com `code: "folder-too-deep"`

#### Scenario: Código fora do formato não passa
- **WHEN** o `apps/connectors` responde 422 com `code: "Sem acesso"`
- **THEN** a API responde HTTP 502 com `code: "connectors-error"` e o texto
  `Sem acesso` não aparece na resposta

#### Scenario: apps/connectors fora do ar
- **WHEN** a conexão com o `apps/connectors` é recusada
- **THEN** a API responde HTTP 503 com `code: "connectors-unavailable"` e nenhuma
  base é criada

#### Scenario: apps/connectors lento
- **WHEN** o `apps/connectors` não responde dentro do limite de tempo
- **THEN** a API responde HTTP 503 com `code: "connectors-unavailable"`, nunca
  500, e nenhuma base é criada

#### Scenario: Token recusado pelo apps/connectors não desloga o operador
- **WHEN** o `apps/connectors` responde 401 ou 403
- **THEN** a API responde HTTP 502 com `code: "connectors-error"` e `detail` igual
  a `"401"` ou `"403"`, e nunca 401 nem 403

#### Scenario: Erro interno do apps/connectors
- **WHEN** o `apps/connectors` responde 500 sem `code`
- **THEN** a API responde HTTP 502 com `code: "connectors-error"` e `detail` igual
  a `"500"`

### Requirement: Token de serviço service:api nas chamadas ao apps/connectors
Toda requisição do `apps/api` ao `apps/connectors` SHALL levar um token assinado
pelo próprio `apps/api` com `Auth:TokenSigningKey`, com `sub` igual a
`service:api` e validade fixa de 5 minutos, emitido a cada requisição, sem cache.
O subject `service:api` NÃO SHALL ter acesso a nenhuma rota do `apps/api`.

#### Scenario: Subject do token enviado
- **WHEN** o `apps/api` chama o `apps/connectors` para validar uma pasta
- **THEN** o cabeçalho `Authorization` traz um token `Bearer` que a chave de
  assinatura valida, com `sub` igual a `service:api` e expiração 5 minutos depois
  da emissão

#### Scenario: Token novo a cada requisição
- **WHEN** o `apps/api` faz duas chamadas ao `apps/connectors`
- **THEN** cada uma leva o token emitido para ela

#### Scenario: service:api não acessa o apps/api
- **WHEN** um token validamente assinado com `sub` igual a `service:api` chama
  `GET /knowledge-bases` e `POST /knowledge-bases`
- **THEN** as duas respostas são 403

### Requirement: Ida e volta entre apps/api e apps/connectors reais
A suíte SHALL ter um teste que sobe o `apps/api` e o `apps/connectors` reais, com a
mesma chave de assinatura, o conector falso do `apps/connectors` registrado e
nenhuma credencial do Google, e liga o `HttpClient` do `apps/api` ao servidor de
teste do `apps/connectors`, sem rede e sem chamada ao Google.

#### Scenario: Cadastro pela chamada real
- **WHEN** o operador cria uma base `Synced` com o provedor do conector falso
- **THEN** a API responde HTTP 201 e o banco tem o nome e a URL devolvidos pelo
  conector falso

#### Scenario: Código atravessa os dois apps
- **WHEN** o conector falso falha com `access-denied` e o e-mail da conta
- **THEN** a API responde HTTP 422 com esse código e esse e-mail, e nenhuma base é
  criada

#### Scenario: Provedor não configurado no apps/connectors real
- **WHEN** o operador cria uma base `Synced` com `provider: "google-drive"` num
  `apps/connectors` sem credencial do Google
- **THEN** a API responde HTTP 422 com `code: "provider-not-configured"`

#### Scenario: Chave de assinatura divergente
- **WHEN** o `apps/connectors` usa uma chave de assinatura diferente da do
  `apps/api`
- **THEN** a API responde HTTP 502 com `code: "connectors-error"` e `detail` igual
  a `"401"`, e nenhuma base é criada
