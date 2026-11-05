## MODIFIED Requirements

### Requirement: A suíte de integração do apps/inbox autentica pelo token do emissor
As fixtures de integração de `apps/inbox/tests` SHALL subir o `apps/inbox` com a
validação do token do emissor ligada contra um emissor de teste em memória, SHALL
esperar a obtenção das chaves antes do primeiro cliente, e SHALL anexar por padrão
um access token do cliente `buteco-painel`. Todo host de teste do `apps/inbox`
SHALL subir com `Identity:Issuer` configurado, porque o app não sobe sem ele.
Nenhum helper de teste SHALL assinar token fora do emissor.

#### Scenario: Fixture padrão autentica pelo emissor
- **WHEN** um caso usa o cliente padrão de uma fixture de integração e chama
  `GET /channels`
- **THEN** a resposta é `200 OK`, sem esperar a primeira obtenção de chaves no
  próprio caso
