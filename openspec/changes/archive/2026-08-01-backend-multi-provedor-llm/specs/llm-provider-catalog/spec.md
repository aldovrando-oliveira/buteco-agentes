## ADDED Requirements

### Requirement: Listagem de provedores de LLM disponíveis
O sistema SHALL expor, via `apps/api`, um endpoint `GET /providers` que
retorna apenas os provedores de LLM cujas variáveis de ambiente exigidas
estejam configuradas, cada um acompanhado da lista de modelos disponíveis
para esse provedor (catálogo estático/curado no código).

#### Scenario: Provedor com variáveis de ambiente configuradas aparece na listagem
- **WHEN** um cliente envia `GET /providers` e o provedor Anthropic tem sua
  variável de ambiente de API key configurada
- **THEN** a resposta inclui Anthropic com a lista de modelos disponíveis
  para esse provedor

#### Scenario: Provedor sem variáveis de ambiente configuradas não aparece na listagem
- **WHEN** um cliente envia `GET /providers` e o provedor Gemini não tem
  sua variável de ambiente de API key configurada
- **THEN** a resposta não inclui Gemini, nem sua lista de modelos

#### Scenario: Nenhum provedor configurado retorna lista vazia
- **WHEN** um cliente envia `GET /providers` e nenhum dos provedores
  suportados (OpenAI, Anthropic, Gemini) tem variáveis de ambiente
  configuradas
- **THEN** a API responde com HTTP 200 e uma lista vazia, sem erro

### Requirement: Validação de Provider/Model contra o catálogo disponível
O sistema SHALL usar a mesma fonte de disponibilidade de provedores e
modelos exposta por `GET /providers` para validar `Provider`/`Model` em
qualquer operação que os receba (cadastro ou atualização de agente),
garantindo que a validação nunca diverge do que é reportado publicamente
como disponível.

#### Scenario: Combinação Provider+Model disponível passa na validação
- **WHEN** uma operação recebe um `Provider` configurado e um `Model` que
  consta na lista de modelos desse provedor
- **THEN** a validação aceita a combinação

#### Scenario: Provider não configurado falha na validação
- **WHEN** uma operação recebe um `Provider` cujas variáveis de ambiente
  não estão configuradas
- **THEN** a validação rejeita a combinação, independentemente do `Model`
  informado

#### Scenario: Model fora do catálogo do Provider falha na validação
- **WHEN** uma operação recebe um `Provider` configurado, mas um `Model`
  que não consta na lista de modelos curada para esse provedor
- **THEN** a validação rejeita a combinação
