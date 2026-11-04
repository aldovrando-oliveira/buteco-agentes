## ADDED Requirements

### Requirement: A suíte de integração do apps/api autentica pelo token do emissor
As fixtures de integração de `apps/api/tests` SHALL anexar por padrão um access
token do emissor do próprio `apps/api` (cliente `buteco-painel`, `sub` que não é
usuário), e os casos que provam regra por subject SHALL obter o token do cliente
que a tabela de clientes do `apps/api` mapeia para aquele subject. Nenhum caso
SHALL depender do token HMAC, exceto os que provam o próprio HMAC, listados por
nome na #182 para remoção com ele.

#### Scenario: Suíte com o HMAC recusado
- **WHEN** a validação do token HMAC do `apps/api` passa a recusar todo token e a
  suíte de `apps/api/tests` roda
- **THEN** reprovam só os casos da lista "só prova o HMAC" da #182, por nome

#### Scenario: Fixture padrão com o comportamento do operador sem usuário
- **WHEN** um caso usa o cliente padrão de uma fixture de integração
- **THEN** as rotas do catálogo respondem como ao operador, e as rotas que exigem
  um usuário respondem `403`
