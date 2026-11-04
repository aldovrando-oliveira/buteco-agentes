## ADDED Requirements

### Requirement: A suíte de integração do apps/inbox autentica pelo token do emissor
As fixtures de integração de `apps/inbox/tests` SHALL subir o `apps/inbox` com a
validação do token do emissor ligada contra um emissor de teste em memória, SHALL
esperar a obtenção das chaves antes do primeiro cliente, e SHALL anexar por padrão
um access token do cliente `buteco-painel`. Nenhum caso SHALL depender do token
HMAC, exceto os que provam o próprio HMAC, listados por nome na #182 para remoção
com ele.

#### Scenario: Suíte com o HMAC recusado
- **WHEN** a validação do token HMAC do `apps/inbox` passa a recusar todo token e a
  suíte de `apps/inbox/tests` roda
- **THEN** reprovam só os casos da lista "só prova o HMAC" da #182, por nome
