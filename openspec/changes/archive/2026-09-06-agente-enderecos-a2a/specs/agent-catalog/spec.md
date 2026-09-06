## ADDED Requirements

### Requirement: Endereços A2A do agente na resposta da API
A resposta de agente de `apps/api` SHALL incluir os dois endereços públicos A2A
daquele agente: o endpoint de execução e o endereço do card de descoberta.

Os endereços SHALL ser montados no servidor, a partir da URL pública
configurada, e SHALL ser idênticos aos que o card de descoberta do mesmo agente
anuncia. Nenhum consumidor SHALL precisar concatenar host com identificador para
obtê-los.

Quando a URL pública não estiver configurada, os endereços SHALL estar ausentes
da resposta, e a API SHALL NOT devolver endereço relativo, vazio ou construído
a partir do host da requisição.

Os endereços SHALL ser expostos independentemente do estado do agente, porque o
card de descoberta também é — a resposta descreve onde o agente é alcançável,
não se ele aceitará o que receber.

#### Scenario: Consulta por id inclui os endereços
- **WHEN** um cliente consulta um agente existente pelo seu identificador, com a
  URL pública configurada
- **THEN** a resposta inclui o endereço do endpoint de execução e o endereço do
  card de descoberta daquele agente

#### Scenario: Os endereços coincidem com os do card de descoberta
- **WHEN** o card de descoberta de um agente é consultado e comparado com a
  resposta de agente do mesmo identificador
- **THEN** o endereço de execução anunciado nos dois é o mesmo

#### Scenario: Agente inativo também expõe os endereços
- **WHEN** um agente desativado é consultado
- **THEN** a resposta inclui os dois endereços normalmente

#### Scenario: Sem URL pública configurada, os endereços ficam ausentes
- **WHEN** a URL pública do servidor não está configurada e um agente é
  consultado
- **THEN** a resposta não inclui os endereços A2A, em vez de incluir endereço
  incompleto ou construído a partir do host da requisição

#### Scenario: A listagem carrega os mesmos endereços
- **WHEN** um cliente lista os agentes
- **THEN** cada agente da lista carrega os mesmos endereços que a consulta por
  id devolveria para ele
