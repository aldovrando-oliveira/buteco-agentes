## RENAMED Requirements

- FROM: `### Requirement: Fluxos habilitados e o único cliente mantido pelo apps/api`
- TO: `### Requirement: Fluxos habilitados e os clientes mantidos pelo apps/api`

## MODIFIED Requirements

### Requirement: Fluxos habilitados e os clientes mantidos pelo apps/api
O emissor SHALL habilitar os grants `client_credentials`, `authorization_code` e `refresh_token`, com PKCE obrigatório no `authorization_code`. Os únicos clientes que o próprio `apps/api` SHALL manter são o `buteco-painel` (capability `issuer-user-login`) e os clientes de serviço `buteco-servico-inbox` e `buteco-servico-connectors` (requisito "Clientes de serviço mantidos pelo apps/api"). Os únicos escopos são `buteco-api`, `buteco-inbox` e `buteco-connectors`. Todos são sincronizados em segundo plano depois do start. Nenhum outro cliente, escopo ou recurso SHALL ser cadastrado por migration nem por código. Requisição com outro `grant_type` SHALL ser recusada com `unsupported_grant_type`.

#### Scenario: Banco recém-migrado tem só os clientes do apps/api
- **WHEN** o banco do `apps/api` é migrado do zero, o app sobe e o aquecimento termina
- **THEN** os clientes do emissor com prefixo `buteco-` são exatamente três: `buteco-painel`, `buteco-servico-inbox` e `buteco-servico-connectors`

#### Scenario: Grant de senha é recusado
- **WHEN** um cliente envia `POST /connect/token` com `grant_type=password`
- **THEN** a resposta é `400` com o erro `unsupported_grant_type`

## ADDED Requirements

### Requirement: Clientes de serviço mantidos pelo apps/api
O `apps/api` SHALL manter, na mesma sincronização do `buteco-painel` (em segundo plano depois do start, sob o mesmo lock, sem acesso ao banco no boot), dois clientes de serviço:

- `buteco-servico-inbox`, com o segredo de `Identity:ServiceClients:Inbox:Secret`;
- `buteco-servico-connectors`, com o segredo de `Identity:ServiceClients:Connectors:Secret`.

Cada cliente de serviço SHALL ser:

- confidencial, com consentimento implícito;
- com permissão só do endpoint de token, do grant `client_credentials` e do escopo `buteco-api`;
- sem `redirect_uri`, sem `post_logout_redirect_uri` e sem requisito.

O OpenIddict SHALL guardar só o hash do segredo.

A cada sincronização, qualquer divergência do cliente gravado em relação ao descritor SHALL ser corrigida. Isso inclui o segredo: quando o segredo configurado não validar contra o gravado, a sincronização SHALL regravá-lo. Toda correção SHALL levar o segredo configurado, para que nenhuma correção deixe o cliente sem credencial. O log da correção SHALL nomear o cliente e SHALL NOT conter o segredo.

Cada escopo e cada cliente SHALL ser sincronizado isoladamente: a falha de um, inclusive um descritor que o OpenIddict recusa, SHALL gerar log de erro que nomeia o item e SHALL NOT conter o segredo. Essa falha SHALL NOT impedir os outros itens no mesmo ciclo, nem gravar o item que falhou. Quando algum item falha, o ciclo SHALL terminar com falha depois de gravar os outros, para ser tentado de novo no ciclo seguinte do aquecimento.

O token `client_credentials` de um cliente de serviço SHALL ter `sub` e `client_id` iguais ao `client_id` do cliente, `aud` igual a `buteco-api` e `typ` `at+jwt`.

#### Scenario: O aquecimento cria os clientes de serviço
- **WHEN** o app sobe contra um banco migrado sem nenhum cliente, e o aquecimento termina
- **THEN** existem os clientes `buteco-servico-inbox` e `buteco-servico-connectors`, confidenciais e com só as três permissões

#### Scenario: Token do cliente de serviço
- **WHEN** alguém envia `POST /connect/token` com `grant_type=client_credentials`, `client_id=buteco-servico-inbox`, o segredo configurado e `scope=buteco-api`
- **THEN** a resposta traz um access token com `client_id` `buteco-servico-inbox`, `aud` só `buteco-api` e `typ` `at+jwt`

#### Scenario: Escopo de outro app é recusado
- **WHEN** o mesmo pedido chega com `scope=buteco-connectors`
- **THEN** nenhum token é emitido, e o erro da resposta é `invalid_request`, porque o escopo existe e o cliente não tem permissão dele

#### Scenario: Segredo errado é recusado
- **WHEN** o pedido chega com um segredo diferente do configurado
- **THEN** nenhum token é emitido, e a resposta é `invalid_client`

#### Scenario: Cliente de serviço adulterado volta ao descritor com a credencial
- **WHEN** o cliente `buteco-servico-inbox` gravado ganhou a permissão do escopo `buteco-connectors`, e a sincronização roda
- **THEN** o cliente volta a ter só as três permissões, e um pedido com o segredo configurado continua emitindo token

#### Scenario: Um cliente de serviço que falha não impede os outros
- **WHEN** o `buteco-painel` e os dois clientes de serviço foram adulterados, e a sincronização roda com o descritor do `buteco-servico-inbox` recusado pelo OpenIddict
- **THEN** o `buteco-painel` e o `buteco-servico-connectors` voltam ao descritor no mesmo ciclo, o `buteco-servico-inbox` fica como estava e continua emitindo token com o segredo dele, o log tem um erro que cita `cliente buteco-servico-inbox` sem nenhum segredo, e o ciclo termina com falha

#### Scenario: Segredo trocado na configuração vale depois da sincronização
- **WHEN** o app sobe com um segredo do `buteco-servico-inbox` diferente do gravado, e o aquecimento termina
- **THEN** o segredo anterior recebe `invalid_client`, o novo emite token, e o log cita a troca do segredo do `buteco-servico-inbox` sem conter nenhum dos dois valores

### Requirement: Segredos dos clientes de serviço conferidos no boot
O boot do `apps/api` SHALL falhar, depois da construção do host e sem acesso ao banco, quando `Identity:ServiceClients:Inbox:Secret` ou `Identity:ServiceClients:Connectors:Secret`:

- estiver ausente ou vazio;
- não for base64 válido;
- tiver menos de 32 bytes depois de decodificado;
- for igual, em bytes decodificados, ao outro segredo de serviço;
- for igual, em bytes decodificados, a `Identity:SigningKeyEncryptionKey`, a `Identity:TwoFactorEncryptionKey` ou a `Mcp:CredentialEncryptionKey`. Esta última só é comparada quando estiver configurada e decodificar.

A mensagem SHALL nomear a chave do segredo, e, no caso de igualdade, as duas chaves iguais. Nenhuma mensagem SHALL conter o valor. Os dois segredos SHALL ser obrigatórios mesmo sem `Connectors:BaseUrl`. O registro dos serviços SHALL NOT lançar exceção por isso, para o migration bundle continuar subindo até a construção do host.

#### Scenario: Segredo ausente derruba o boot
- **WHEN** o app sobe sem `Identity:ServiceClients:Inbox:Secret`
- **THEN** o boot falha com uma mensagem que cita `Identity:ServiceClients:Inbox:Secret`

#### Scenario: Segredo curto derruba o boot sem ecoar o valor
- **WHEN** o app sobe com `Identity:ServiceClients:Connectors:Secret` igual ao base64 de 16 bytes
- **THEN** o boot falha com uma mensagem que cita `Identity:ServiceClients:Connectors:Secret` e não contém o valor configurado

#### Scenario: Segredos iguais entre si derrubam o boot
- **WHEN** o app sobe com os dois segredos de serviço iguais
- **THEN** o boot falha com uma mensagem que cita `Identity:ServiceClients:Inbox:Secret` e `Identity:ServiceClients:Connectors:Secret`, sem o valor

#### Scenario: Segredo igual a uma chave de cifra derruba o boot
- **WHEN** o app sobe com `Identity:ServiceClients:Inbox:Secret` igual a `Identity:TwoFactorEncryptionKey`, e, em outras subidas, igual a `Identity:SigningKeyEncryptionKey` e a `Mcp:CredentialEncryptionKey`
- **THEN** cada boot falha com uma mensagem que cita `Identity:ServiceClients:Inbox:Secret` e a chave igual, sem o valor

#### Scenario: Composição real sobe com os segredos válidos
- **WHEN** o app sobe com os dois segredos presentes, distintos entre si e das três chaves de cifra
- **THEN** a checagem passa e a aplicação responde
