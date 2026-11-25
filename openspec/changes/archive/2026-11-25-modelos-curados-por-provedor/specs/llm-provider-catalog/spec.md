## Purpose

Diz quais provedores de LLM estão disponíveis para os agentes e com quais
modelos: `GET /providers`, que junta a chave do ambiente com a lista curada de
`llm-model-curation`, e a validação de `provider`/`model` no cadastro e na edição
de agente, que usa a mesma fonte (changes `backend-multi-provedor-llm` e
`modelos-curados-por-provedor`).

## MODIFIED Requirements

### Requirement: Listagem de provedores de LLM disponíveis
O sistema SHALL expor, via `apps/api`, um endpoint `GET /providers` que
retorna apenas os provedores de LLM cujas variáveis de ambiente exigidas
estejam configuradas, na ordem de `LlmProviders.All`, cada um acompanhado da
lista de modelos **habilitados** para esse provedor na lista curada do banco
do `apps/api` (capability `llm-model-curation`), e não de um catálogo fixo no
código. A forma da resposta SHALL continuar sendo um array na raiz de
`{ "id": string, "models": string[] }`.

Um provedor configurado sem nenhum modelo habilitado SHALL aparecer com
`models: []`: a chave existe e nenhum modelo foi habilitado, o que é
diferente de provedor sem chave, que não aparece.

Os modelos de cada provedor SHALL sair ordenados pela mesma consulta e pela
mesma ordem que `GET /providers/models` usa para a lista curada.

A rota SHALL NOT consultar a listagem de modelos de nenhum fornecedor.

#### Scenario: Provedor com variáveis de ambiente configuradas aparece na listagem
- **WHEN** um cliente envia `GET /providers` e o provedor Anthropic tem sua
  variável de ambiente de API key configurada e dois modelos habilitados na
  lista curada
- **THEN** a resposta inclui Anthropic com exatamente esses dois modelos, e
  nenhum outro

#### Scenario: Provedor sem variáveis de ambiente configuradas não aparece na listagem
- **WHEN** um cliente envia `GET /providers` e o provedor Gemini não tem
  sua variável de ambiente de API key configurada, mesmo com modelos
  habilitados na lista curada
- **THEN** a resposta não inclui Gemini, nem sua lista de modelos

#### Scenario: Provedor configurado sem modelo habilitado aparece com lista vazia
- **WHEN** um cliente envia `GET /providers`, o provedor OpenAI tem a chave
  configurada e nenhum modelo da OpenAI está habilitado na lista curada
- **THEN** a resposta inclui `{ "id": "openai", "models": [] }`

#### Scenario: Nenhum provedor configurado retorna lista vazia
- **WHEN** um cliente envia `GET /providers` e nenhum dos provedores
  suportados (OpenAI, Anthropic, Gemini) tem variáveis de ambiente
  configuradas
- **THEN** a API responde com HTTP 200 e uma lista vazia, sem erro

#### Scenario: Modelo habilitado depois do boot aparece sem reiniciar
- **WHEN** o Admin habilita um modelo e, em seguida, um cliente envia
  `GET /providers`
- **THEN** a resposta já inclui o modelo habilitado

#### Scenario: Listagem não depende do fornecedor
- **WHEN** um cliente envia `GET /providers` com a listagem de modelos dos
  três fornecedores indisponível
- **THEN** a API responde com HTTP 200 e a lista curada, e nenhuma chamada é
  feita a nenhum fornecedor

### Requirement: Validação de Provider/Model contra o catálogo disponível
O sistema SHALL usar a mesma fonte de disponibilidade de provedores e
modelos exposta por `GET /providers` (provedor configurado no ambiente e par
`provider`+`model` habilitado na lista curada) para validar
`Provider`/`Model` em qualquer operação que os receba (cadastro ou
atualização de agente), garantindo que a validação nunca diverge do que é
reportado publicamente como disponível. O provedor sem chave SHALL ser
recusado antes do modelo.

#### Scenario: Combinação Provider+Model disponível passa na validação
- **WHEN** uma operação recebe um `Provider` configurado e um `Model`
  habilitado para esse provedor na lista curada
- **THEN** a validação aceita a combinação

#### Scenario: Provider não configurado falha na validação
- **WHEN** uma operação recebe um `Provider` cujas variáveis de ambiente
  não estão configuradas
- **THEN** a validação rejeita a combinação pelo provedor, independentemente
  do `Model` informado estar habilitado

#### Scenario: Model fora da lista curada do Provider falha na validação
- **WHEN** uma operação recebe um `Provider` configurado, mas um `Model`
  que não está habilitado para esse provedor na lista curada
- **THEN** a validação rejeita a combinação pelo modelo

#### Scenario: Model habilitado para outro provedor falha na validação
- **WHEN** uma operação recebe o `Provider` Anthropic configurado e um
  `Model` que está habilitado só para a OpenAI
- **THEN** a validação rejeita a combinação pelo modelo
