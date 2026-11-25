**Issue:** #296

## Why

A lista de modelos de cada provedor de LLM está fixa no código (`ProviderModelCatalog`,
nove modelos levantados em julho de 2026 e nunca alterados desde o commit `7d54131`).
Um modelo novo do fornecedor só chega aos agentes com uma versão nova do produto, e um
modelo que a instalação não quer oferecer não sai sem mexer no código. O dono decidiu
em 19/11/2026 que a lista passa a ser **curada no painel por um administrador**, a
partir da **listagem de modelos do próprio fornecedor**, e que o agente cujo modelo sai
da lista **recusa novas tarefas** até ser editado.

## What Changes

- **Lista curada no banco do `apps/api`**: uma tabela nova com os pares
  `(provider, model)` habilitados. A migration que a cria faz a carga inicial com os
  nove modelos do catálogo estático **mais** os pares `provider`/`model` já gravados em
  agentes, para que nenhum agente que executa hoje seja recusado no deploy.
- **Rotas de curadoria, só do Admin (com o papel gravado)**: ler a lista curada por
  provedor (com quantos agentes usam cada modelo), consultar as opções que o
  fornecedor lista para a chave do ambiente, habilitar um modelo que o fornecedor
  listou e retirar um modelo habilitado. Retirar nunca consulta o fornecedor.
- **Consulta à listagem do fornecedor** (OpenAI `GET {BaseUrl}/models`, Anthropic
  `GET /v1/models`, Gemini `GET /v1beta/models`), com a chave sempre em cabeçalho,
  nunca em URL, log ou resposta, e só a pedido da tela de curadoria: nunca no boot nem
  no caminho da mensagem. Falha do fornecedor só impede habilitar.
- O `apps/api` passa a ler `OpenAI:BaseUrl` (o compose de produção já o entrega), para
  listar pelo mesmo endpoint que o `apps/workers` usa para conversar.
- **`GET /providers` e a validação de cadastro e edição de agente** passam a ler a lista
  curada em vez do catálogo estático. A forma de `GET /providers` (`{ id, models }`) não
  muda. O `ProviderModelCatalog` é removido.
- **BREAKING (comportamento)**: o `SendMessage` para agente cujo modelo não está
  habilitado é **recusado** sem publicar job, com o motivo novo `ModelNotEnabled` na
  métrica de recusa. A edição de um agente que mantém um modelo retirado passa a
  responder `400`.
- **Delegação no `apps/workers`**: o Source e o Target com modelo não habilitado não
  delegam nem recebem delegação (falha da tool, sem criar task). O `apps/workers` lê a
  tabela nova pelo contexto espelho, com migration espelho para as fixtures.
- Documentação: `deployment.md` (gateway compatível com OpenAI cuja listagem não lista
  nada), `docs/` do domínio de agentes e `CHANGELOG`.

Fora de escopo: provedor novo, credencial fora do ambiente, modelo de embedding,
metadados de modelo (preço, contexto), sincronização automática com o fornecedor, as
telas (issue #297, sequenciada depois, convenção 1), e a lacuna de provedor sem chave
na delegação (issue #298).

## Capabilities

### New Capabilities
- `llm-model-curation`: a lista curada de modelos por provedor no banco do `apps/api`,
  a carga inicial, as rotas de curadoria (ler, habilitar e retirar) e a consulta às
  opções do fornecedor, com o que a rota afirma sobre o que devolve e como ela falha.

### Modified Capabilities
- `llm-provider-catalog`: `GET /providers` e a validação de `Provider`/`Model` passam a
  vir da lista curada, e não de um catálogo estático no código.
- `agent-catalog`: a edição que mantém um modelo retirado é recusada como modelo
  indisponível (explicita o caso que sai da lista depois do cadastro).
- `a2a-task-lifecycle`: requisito novo de recusa do `SendMessage` para agente com
  modelo não habilitado.
- `agent-rejection-metrics`: o motivo novo `ModelNotEnabled` no vocabulário fechado, e
  o cenário da linha de recusa correspondente.
- `agent-delegation-execution`: a validação fresca do Target e a do Source passam a
  exigir modelo habilitado.
- `route-roles`: as rotas de curadoria entram na matriz como só do Admin.

## Impact

- **`apps/api`**: entidade, configuração do EF e migration (com a carga inicial);
  `ProviderCatalogService` passa a ler o banco (deixa de ser singleton sem banco);
  `EnqueueingAgentHandler` (recusa nova), `AgentA2AServerRegistry` (deixa de repassar o
  serviço); `RejectionMetricsValues`; `RoleRoutes`; rotas e handlers de curadoria; os
  três clientes de listagem com `HttpClient` nomeado; `Program.cs`.
- **`apps/workers`**: entidade espelho, `AppDbContext` espelho e migration espelho;
  `AgentDelegationToolSetResolver.DelegateToTargetAsync`.
- **`libs/ProviderCatalog`**: sem mudança (continua só com a identidade dos provedores).
- **`apps/frontend`**: nenhuma mudança nesta change. O motivo de recusa novo aparece no
  Insights como o próprio texto até a #297.
- **Testes**: `apps/api` (integração com Testcontainers e fakes HTTP dos três
  fornecedores), `apps/workers` (delegação), `tests/CrossAppTaskStoreCompatibility.Tests`
  (acordo do schema da tabela nova entre os dois contextos).
- **Deploy**: nenhuma variável nova. A migration roda pelo `migrator` do compose.
