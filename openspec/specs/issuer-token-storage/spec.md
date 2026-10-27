# issuer-token-storage Specification

## Purpose

Diz o que o emissor OIDC do `apps/api` grava em `openiddict_tokens` e
`openiddict_authorizations`, e quando as linhas saem. O access token de
`client_credentials` não gera linha. A limpeza periódica remove as entradas mortas com
mais de 14 dias, uma instância por ciclo, sob lock próprio, e preserva o que a revogação
de sessão usa. O boot amarra a idade mínima da limpeza à duração do refresh token.
Criada pela change `limpeza-tokens-openiddict` (#209).

## Requirements

### Requirement: O access token de client_credentials não gera entrada de token
Com o emissor ligado, um access token emitido pelo grant `client_credentials` SHALL NOT gerar linha em `openiddict_tokens` nem em `openiddict_authorizations`, e SHALL continuar verificável pelo JWKS exatamente como antes. Os demais tipos de token (código de autorização, refresh token, id token, e access token de qualquer outro grant) SHALL continuar gerando a linha que o OpenIddict grava por padrão.

#### Scenario: Pedido de client_credentials não grava nada
- **WHEN** um cliente confidencial cadastrado obtém um access token por `client_credentials`
- **THEN** a resposta traz o access token, o token é verificável pelo JWKS do emissor, e `openiddict_tokens` e `openiddict_authorizations` continuam sem nenhuma linha

#### Scenario: Refresh token continua gravado
- **WHEN** um cliente com permissão de refresh obtém um refresh token
- **THEN** existe uma linha em `openiddict_tokens` com o tipo `refresh_token` e status `valid`, ligada a uma linha `valid` de `openiddict_authorizations`

#### Scenario: Só o par access token e client_credentials muda
- **WHEN** o OpenIddict gera um token de qualquer tipo (access, refresh, id, código de autorização) em qualquer grant (`client_credentials`, `authorization_code`, `refresh_token`)
- **THEN** a criação da linha fica desligada só para o access token do grant `client_credentials`, e ligada em todas as outras combinações

### Requirement: Entradas mortas de token e de autorização são removidas periodicamente
Com o emissor ligado, o `apps/api` SHALL remover, a cada 1 hora, as linhas de `openiddict_tokens` e de `openiddict_authorizations` criadas há mais de 14 dias que o OpenIddict considera removíveis: token vencido, ou com status diferente de `valid` e de `inactive`, ou cuja autorização não está `valid`; e autorização sem nenhum token que não esteja `valid` ou seja ad hoc. Os tokens SHALL ser removidos antes das autorizações, no mesmo ciclo. A remoção SHALL acontecer em lotes, sem uma transação que abranja o ciclo inteiro. O primeiro ciclo SHALL acontecer um intervalo depois do start, sem acesso ao banco no boot.

#### Scenario: Linhas mortas antigas são removidas
- **WHEN** existem um access token vencido, um refresh token resgatado e uma autorização ad hoc sem tokens, todos criados há mais de 14 dias, e um ciclo de limpeza roda
- **THEN** as três linhas deixam de existir

#### Scenario: Linha morta recente fica
- **WHEN** existe um refresh token resgatado criado há 1 dia e um ciclo de limpeza roda
- **THEN** a linha continua existindo

#### Scenario: Sessão válida fica, qualquer que seja a idade
- **WHEN** existem um refresh token `valid`, não vencido, e a autorização `valid` dele, criados há mais de 14 dias, e um ciclo de limpeza roda
- **THEN** as duas linhas continuam existindo, e o refresh token continua aceito no endpoint de token

#### Scenario: Falha de um ciclo não para a limpeza
- **WHEN** um ciclo falha por erro do banco
- **THEN** o erro é registrado no log, e o ciclo seguinte roda normalmente

### Requirement: A limpeza preserva o efeito da revogação
Remover uma linha SHALL NOT tornar aceito um token que a revogação recusaria. Uma sessão viva SHALL continuar revogável depois de qualquer ciclo de limpeza. Um refresh token resgatado SHALL continuar disparando a revogação da cadeia da autorização quando reapresentado, enquanto não tiver vencido.

#### Scenario: Sessão viva continua revogável depois da limpeza
- **WHEN** um ciclo de limpeza roda e, em seguida, as autorizações do subject da sessão são revogadas
- **THEN** o refresh token da sessão passa a ser recusado no endpoint de token

#### Scenario: Reuso de refresh token resgatado é detectado
- **WHEN** um refresh token resgatado há mais de 30 segundos e há menos de 14 dias é reapresentado depois de um ciclo de limpeza
- **THEN** o pedido é recusado, e os tokens da autorização dele passam a `revoked`

#### Scenario: Sessão revogada e removida continua recusada
- **WHEN** uma sessão revogada há mais de 14 dias é removida pela limpeza e o refresh token dela é reapresentado
- **THEN** o pedido é recusado

### Requirement: Uma instância por ciclo de limpeza
O ciclo de limpeza SHALL tentar um `pg_try_advisory_xact_lock` na chave `bigint` `4779819235982397489`, diferente da chave da rotação de chaves do emissor, numa transação separada da remoção. A instância que não obtiver o lock SHALL pular o ciclo sem remover nada. Ciclos concorrentes SHALL terminar sem erro, e a soma do que removeram SHALL ser igual ao que estava removível.

#### Scenario: Ciclos concorrentes não removem em dobro nem travam
- **WHEN** quatro ciclos de limpeza rodam ao mesmo tempo contra o mesmo banco
- **THEN** todos terminam sem exceção em menos de 30 segundos, e a soma das linhas removidas é igual ao número de linhas removíveis antes dos ciclos

#### Scenario: Lock ocupado pula o ciclo
- **WHEN** o lock da limpeza está preso por outra transação e um ciclo roda
- **THEN** o ciclo termina sem remover nada, e o ciclo seguinte, com o lock livre, remove as linhas removíveis

#### Scenario: Lock da rotação não bloqueia a limpeza
- **WHEN** o lock da rotação de chaves do emissor está preso e um ciclo de limpeza roda
- **THEN** o ciclo remove as linhas removíveis

### Requirement: Idade mínima da limpeza conferida contra a duração do refresh token
Com o emissor ligado, o boot SHALL falhar quando a idade mínima da limpeza (14 dias) for menor que a duração do refresh token configurada no servidor, com mensagem que cite as duas durações.

#### Scenario: Refresh token mais longo que a idade mínima derruba o boot
- **WHEN** o servidor do emissor é composto com refresh token de 15 dias
- **THEN** o boot falha com mensagem que cita 14 dias e 15 dias

#### Scenario: Duração padrão sobe
- **WHEN** o servidor do emissor é composto com a duração padrão do refresh token
- **THEN** o boot segue
