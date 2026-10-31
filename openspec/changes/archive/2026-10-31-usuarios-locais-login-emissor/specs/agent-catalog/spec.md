## MODIFIED Requirements

### Requirement: Endereços A2A do agente na resposta da API
A resposta de agente de `apps/api` SHALL incluir os dois endereços públicos A2A
daquele agente: o endpoint de execução e o endereço do card de descoberta.

Os endereços SHALL ser montados no servidor, a partir da URL pública
configurada, e SHALL ser idênticos aos que o card de descoberta do mesmo agente
anuncia. Nenhum consumidor SHALL precisar concatenar host com identificador para
obtê-los. A API SHALL NOT devolver endereço relativo, vazio ou construído a
partir do host da requisição. A URL pública é obrigatória, e sem ela o
`apps/api` não sobe (capability `oidc-issuer`).

Os endereços SHALL ser expostos independentemente do estado do agente, porque o
card de descoberta também é — a resposta descreve onde o agente é alcançável,
não se ele aceitará o que receber.

#### Scenario: Consulta por id inclui os endereços
- **WHEN** um cliente consulta um agente existente pelo seu identificador
- **THEN** a resposta inclui o endereço do endpoint de execução e o endereço do
  card de descoberta daquele agente

#### Scenario: Os endereços coincidem com os do card de descoberta
- **WHEN** o card de descoberta de um agente é consultado e comparado com a
  resposta de agente do mesmo identificador
- **THEN** o endereço de execução anunciado nos dois é o mesmo

#### Scenario: Agente inativo também expõe os endereços
- **WHEN** um agente desativado é consultado
- **THEN** a resposta inclui os dois endereços normalmente

#### Scenario: Endereço não sai do host da requisição
- **WHEN** um agente é consultado com o header `Host` diferente da URL pública
- **THEN** os endereços da resposta começam pela URL pública configurada

#### Scenario: A listagem carrega os mesmos endereços
- **WHEN** um cliente lista os agentes
- **THEN** cada agente da lista carrega os mesmos endereços que a consulta por
  id devolveria para ele
