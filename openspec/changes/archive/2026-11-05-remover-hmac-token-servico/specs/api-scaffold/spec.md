## MODIFIED Requirements

### Requirement: A suíte de integração do apps/api autentica pelo token do emissor
As fixtures de integração de `apps/api/tests` SHALL anexar por padrão um access
token do emissor do próprio `apps/api` (cliente `buteco-painel`, `sub` que não é
usuário), e os casos que provam regra por subject SHALL obter o token do cliente
que a tabela de clientes do `apps/api` mapeia para aquele subject. Nenhum helper de
teste SHALL assinar token fora do emissor.

#### Scenario: Fixture padrão com o comportamento do operador sem usuário
- **WHEN** um caso usa o cliente padrão de uma fixture de integração
- **THEN** as rotas do catálogo respondem como ao operador, e as rotas que exigem
  um usuário respondem `403`
