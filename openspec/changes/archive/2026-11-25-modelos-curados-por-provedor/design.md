## Context

**Issue:** #296. Telas na #297 (sequenciada depois, convenção 1). Achado da exploração
fora do escopo: #298 (a delegação não confere provedor sem chave).

### Estado atual, lido em `c9733ce` (convenção 6: cada premissa do pedido foi conferida)

| premissa | resultado | onde |
|---|---|---|
| `ProviderCatalogService` é singleton sem banco | **confere** | `Program.cs:103`; `ProviderCatalogService.cs:15` só recebe `IConfiguration` |
| consumidores: `GET /providers`, Create/Update (`Validate`), `EnqueueingAgentHandler` (`IsProviderConfigured`), `AgentA2AServerRegistry` só repassa | **confere** | `ListProvidersQueryHandler`, `CreateAgentCommandHandler`, `UpdateAgentCommandHandler.cs:30`, `EnqueueingAgentHandler.cs:63`, `AgentA2AServerRegistry.cs:14` |
| a execução não confere o modelo | **confere** | `AgentExecutionService.cs:336` chama `chatClientResolver.Resolve(agent.Provider!, agent.Model!)` sem conferência |
| a delegação roda no `apps/workers` e não passa pelo `EnqueueingAgentHandler` | **confere** | `AgentDelegationToolSetResolver.DelegateToTargetAsync`, `:110` (Source) e `:123` (Target): só `IsActive` e nulos; publica o job direto (`:145`) |
| contexto espelho com migrations que não rodam em runtime, mas migram as fixtures | **confere** | comentário de `apps/workers/.../AppDbContext.cs:16-19`; `WorkerInfrastructureFixture.cs:31` (`MigrateAsync`); precedente `20261018042026_AddMcpServerAuthHeaderName` |
| o `apps/api` recebe `OpenAI__BaseUrl` no compose de produção e não o lê | **confere** | `docker-compose.prod.yml:117` (serviço `api`); nenhuma leitura de `OpenAI:BaseUrl` em `apps/api/src` |
| o frontend consome `GET /providers` como `{ id, models: string[] }` | **confere** | `apps/frontend/src/features/agents/types/agent.ts:80` |
| `rejectionReasonLabels.ts` mostra o motivo desconhecido como o próprio texto | **confere** | `lookup` devolve `{ text: value, unknown: true }` |
| o catálogo estático já teve outros modelos | **não**: um commit só | `git log --follow` de `ProviderModelCatalog.cs` = `7d54131` |

**Consequência medida da última linha:** os únicos escritores de `Agent.Model` são
`new Agent(...)` (`CreateAgentCommandHandler.cs:26`) e `Agent.UpdateDetails`
(`UpdateAgentCommandHandler.cs:36`), e os dois validam contra o catálogo, que nunca
mudou. Logo, todo agente com modelo gravado tem hoje um dos nove pares, **salvo edição
manual por SQL**. A carga inicial (D3) cobre também esse caso.

**Achado de passagem (vira a #298, fora desta change):** a delegação também não confere
provedor sem chave, nem no Source nem no Target.

### Base nova, `4e44dd3` (24/11/2026, convenção 9)

A `main` avançou durante o apply com os PRs #304, #305 e #308 (#302, #303 e #307), e a
branch foi levada de `c9733ce` para `4e44dd3` por fast-forward, com o trabalho sem commit
preservado (cópia de segurança com manifesto sha256 em
`~/.cache/buteco-agents/296-backup-rebase-20261124-162919/`). Conferido contra a base nova:

- **As premissas da tabela acima continuam valendo, com as mesmas linhas.**
  `git diff c9733ce 4e44dd3` não toca `apps/*/src`, `apps/inbox`, `apps/connectors`,
  `apps/frontend`, `tests/`, `libs/`, o compose de produção, `Directory.Build.props` nem
  `global.json`. Os três PRs só mudam testes do `apps/api` (`KnowledgeBaseCatalogTests`,
  `KnowledgeSyncTestSeed`, `ApiFactoryFixture`, `FakeConnectorsHttpMessageHandler`, o
  `.csproj` e o `xunit.runner.json` novo), `scripts/`, documentação e specs.
- **A lista de "casos que saem ou mudam de asserção" do `tasks.md` não muda:** nenhum dos
  arquivos de teste dela está entre os que os PRs mudaram, e os testes que mudaram não
  criam agente nem leem provedor ou modelo. O `ApiFactoryFixture` ganhou
  `SharedDerivedHost<T>` e o descarte desses hosts, só por acréscimo; a
  `ModelCurationFixture` deriva de `LocalUsersFixture` e não o usa.
- **Regras novas que esta change passa a seguir:**
  - a suíte do `apps/api` roda com no máximo 6 coleções (o `xunit.runner.json`); toda
    baseline e todo fechamento registram, por rodada, as threads efetivas lidas do `.trx`, o
    veredito da regra assimétrica, a duração e o `CPU_Speed_Limit` mínimo e mediano, e a
    entrada no `02` traz a linha "`apps/api`: N rodadas, M inconclusivas, K threads"
    (convenção 19, #302);
  - rodada intermediária cita o plano de `python3 scripts/selecionar-testes.py --passos` e o
    SHA da base que ele imprime; a baseline e o fechamento continuam completos (#307);
  - `python3 -B -m unittest discover -s scripts/tests` entra nas conferências antes do PR,
    porque reprova projeto de teste que nenhuma regra alcança e teste que lê arquivo de
    fora do próprio projeto (o `EnabledLlmModelsCrossAppCompatibilityTests` novo é desse
    tipo de risco).
- **As medições sobre `c9733ce` ficam históricas** e não valem como comparação: a baseline
  (0.2/0.3), as rodadas do `rt-inbox` da parada 2 (baseline 2 em 8, change 7 em 7, `h2-so-chave`
  16/16 duas vezes) e a rodada de diagnóstico de 19/11. A baseline e a change são refeitas
  sobre `4e44dd3`.
- **Limite do método, registrado (decisão do dono, 24/11/2026):** as sondas de ordem de
  eventos durante a execução mudaram o resultado que deviam observar. Sem elas, o `Source3`
  reprovou na change 4 vezes em 4 sobre `4e44dd3`; com elas, 0 em 2. O registro em arquivo
  em cada ponto desloca a janela de tempo do que se mede, e a ordem obtida vale só para
  rodadas que aprovam. A investigação segue por experimentos que mexem só no tempo (o
  intervalo de consulta do teste, um atraso artificial na baseline) e, se preciso, por um
  retrato tirado só quando a espera estoura, todos em cópias descartáveis (`tasks.md`, parada 2).
- **Conclusão da parada 2 (decisão do dono, 24/11/2026): (i), fragilidade de teste, e não
  defeito da change.** O experimento A descarta (ii): com a espera da sentinela consultando a
  cada 5 ms, a change viu o `taskId` nas 4 rodadas válidas, então o inbox o grava. O
  experimento B (a baseline com 4 e 8 ms de atraso no ponto da conferência) é **indício, não
  medido pela regra**: nenhuma reprovação válida, porque a CPU ficou abaixo de 100 em todas as
  16 tentativas; o `Source3` e os outros testes que esperam o `taskId` com consulta de 100 ms
  caíram em 14 delas, sempre nessa espera. O `taskId` só existe no registro de despacho entre a
  gravação pelo inbox e o fechamento pelo push (46 a 140 ms medidos com sondas), e os ~4 ms da
  conferência nova deslocam essa janela em relação à consulta. A correção é de teste, fora
  desta change, num PR próprio na `main` (`tasks.md`, parada 2).

### Base nova, `3b6899b` (25/11/2026, convenção 9)

A `main` avançou com o PR #315 (#309), a correção da parada 2, e a branch foi levada de `4e44dd3` para `3b6899b` por fast-forward.

- **Como o trabalho foi preservado:**
  - a cópia de segurança, com manifesto sha256, está em `~/.cache/buteco-agents/296-backup-rebase-20261124-232627/`;
  - só o `02` e o `CHANGELOG`, os dois em sobreposição, voltaram ao `HEAD` e foram reaplicados;
  - no `02` ficaram as duas seções, a da `main` primeiro;
  - no `CHANGELOG` ficou um único `### Changed`, com as entradas da `main` primeiro.

Conferido contra a base nova:

- **As premissas da tabela acima continuam valendo, com as mesmas linhas.** O `git diff 4e44dd3 3b6899b` toca só:
  - os três testes do `rt-inbox` e o `Support/AgentTasks.cs` novo;
  - a spec `inbox-message-orchestration`, o `02`, o `CHANGELOG` e o archive da #309.

  Não toca `apps/*/src`, `libs/`, o compose de produção nem o `Support/RoundTripFixture.cs`, cujo comentário esta change muda.
- **A lista de "casos que saem ou mudam de asserção" do `tasks.md` não muda.** Nenhum arquivo dela está entre os que o #315 mudou.
- **O que os testes do `rt-inbox` passam a exigir desta change:**
  - os casos leem a task do agente em `a2a_tasks` e afirmam que há uma só. Esta change não muda `a2a_tasks` nem quem grava nela;
  - o agente dos casos (`openai`/`gpt-5.6-sol`) continua na lista curada pela carga da migration (D3).
- **A parada 2 fecha como (i), corrigida fora desta change pelo #315.** As medições do `rt-inbox` sobre `4e44dd3` ficam históricas. A baseline e as suítes de fechamento são refeitas sobre `3b6899b`, com o `rt-inbox` esperado em 16/16 nos dois lados.

### Contrato das listagens dos fornecedores (convenção 6), consultado em 19/11/2026

| | OpenAI | Anthropic | Gemini |
|---|---|---|---|
| rota | `GET {OpenAI:BaseUrl}/models` | `GET https://api.anthropic.com/v1/models` | `GET https://generativelanguage.googleapis.com/v1beta/models` |
| fonte | `openai-openapi` `manual_spec/openapi.yaml`, operação `listModels`, esquemas `ListModelsResponse` e `Model` | `platform.claude.com/docs/en/api/models/list` e `/api/overview` | `aistudio.google.com/api-reference/models/list-models` e `/docs/api-key` |
| autenticação | `Authorization: Bearer <chave>` (`securitySchemes.ApiKeyAuth: http/bearer`) | `x-api-key: <chave>` e `anthropic-version: 2023-06-01` (D5) | `x-goog-api-key: <chave>` (o exemplo REST de `/docs/api-key`; a página da listagem usa `?key=`, recusado em D5) |
| paginação | **nenhuma**: `{ object: "list", data: [...] }` | `limit` (1 a 1000, padrão 20), `after_id`; resposta `has_more`, `last_id` | `pageSize` (padrão 50, teto 1000), `pageToken`; resposta `nextPageToken` (ausente = fim) |
| campos do item | `id`, `object`, `created`, `owned_by` | `id`, `display_name`, `lifecycle` (`active`, `deprecated`, `retired`), `created_at`, `capabilities`, … | `name` (`models/{id}`), `baseModelId`, `displayName`, `supportedGenerationMethods`, … |
| separa modelo de chat? | **não**: nenhum campo distingue chat de embedding, imagem ou áudio | todo item é modelo de mensagem | `supportedGenerationMethods` contém `generateContent` |

**Resposta real gravada (OpenAI compatível):** só há chave da OpenAI no ambiente local,
apontada para um **gateway compatível**. Com a chave em cabeçalho, `GET {BaseUrl}/models`
respondeu `200` com `{"object":"list","data":[]}`, e `401` sem chave e com chave errada.
O gateway implementa a rota, autentica e **não lista nada**. Isso decidiu a Q2 (abaixo)
e é o caso que a D4 trata como "o fornecedor respondeu e não listou nenhum modelo".
Anthropic e Gemini ficam só com a documentação (R3).

### Decisões do dono

De 19/11/2026, no pedido, que não se reabrem: curadoria no painel com lista no banco do
`apps/api`; opções vindas da API de listagem do fornecedor com a chave do ambiente (sem
digitação livre, sem lista em variável, sem oferecer tudo); agente com modelo fora da
lista **recusa novas tarefas** até ser editado (sem bloquear a retirada).

E as quatro perguntas em aberto, respondidas antes de fechar este design:

- **Q1, quem cura:** só o Admin, com o papel gravado, como `/applications` (D6).
- **Q2, gateway sem listagem útil:** aceitar e documentar (D4, D10).
- **Q3, delegação:** conferir o Source e o Target (D8). A lacuna da chave vai para a #298.
- **Q4, carga inicial:** os nove do catálogo estático **mais** os pares em uso (D3).

E as decisões tomadas com a aprovação do protótipo da tela (19/11/2026), que eram as
Open Questions deste design:

- **Retirar modelo não pede senha** nem o código de quem chama. A confirmação é **na
  própria linha**, com a contagem de agentes afetados (`agentCount`), como o quadro
  `ModelosProvedores` desenha (D6).
- **Na Anthropic, só modelos ativos são oferecidos para habilitar** (D4). O `lifecycle`
  não vai no fio.
- **A1, saída (a):** o erro do fornecedor leva `vendorStatus` (D4). As divergências V1 a
  V4 foram aceitas como propostas (seção do protótipo).

## Protótipo aprovado da tela (#297), conferido contra as rotas

**Aprovado pelo dono em 19/11/2026.** Canvas `H7dPBZafiSkdKd1hqesPZ6` (Claude Design,
artifact do tipo "Design"), versão `1795117800-893f`; quadro
`project/ModelosProvedores.dc.html`, "Modelos — curadoria por provedor: habilitados,
opções do fornecedor, retirar e todos os estados (#296, #297)", `sha256`
`ad9d5a1b9553ed68bd1ed8a924f79abd05b0991d16e87e391bf6952787add91c` (conferido no arquivo
lido). Tweaks: `carga` (ok, carregando, erro), `painel` (fechado, openai, anthropic,
gemini), `fornecedor` (lista, carregando, vazio, indisponivel, erro, nao-listado),
`retirar` (nenhum, com-agentes, sem-agentes), `chaveGemini` (sem-chave, configurada),
`anthropicSemModelos`.

**Como foi lido:** o MCP do Claude Design conectou depois do novo login, mas não alcança
o quadro: `get_project` recusa o id do canvas (22 caracteres, não é UUID),
`list_projects` volta vazio e o projeto de protótipos da casa responde
`project not found`. O canvas foi lido pela ferramenta `Artifact` (`list` com
`scope: files`, `read` do quadro e do `canvas.json`). É o arquivo real, com versão e
`sha256` conferidos, e não memória nem descrição.

### Conferência estado por estado

A premissa a confirmar: o quadro não pede nenhum dado além do que as quatro rotas servem.

| estado do quadro | o que o quadro mostra | dado que pede | rota e campo | confere? |
|---|---|---|---|---|
| `carga=ok` | um cartão por provedor, na ordem OpenAI, Anthropic, Gemini | provedores na ordem | `GET /providers/models`, ordem de `LlmProviders.All` | sim |
| `carga=ok` | selo "Chave configurada" ou "Sem chave no ambiente" | se a chave está no ambiente | `configured` | sim |
| `carga=ok` | tabela "Modelo habilitado", "Agentes que usam" ("Nenhum", "1 agente", "N agentes") | id e contagem medida | `models[].id`, `models[].agentCount` | sim |
| `carga=carregando` / `carga=erro` | "Carregando os modelos…"; "Não foi possível carregar os modelos agora…" com "Tentar de novo" | só o resultado da leitura | falha de `GET /providers/models` | sim |
| `anthropicSemModelos` | "Nenhum modelo habilitado para este provedor. Ele aparece no formulário de agente, mas sem modelo para escolher." | lista vazia | `models: []` | sim (ver divergência V3) |
| `chaveGemini=sem-chave` | aviso de chave ausente, "Habilitar modelo" desabilitado, "Retirar" disponível | `configured: false` e os modelos habilitados | `configured`, `models` | sim |
| `painel=<provedor>`, `fornecedor=carregando` | "Consultando o fornecedor… Pode levar até 30 segundos." | nada | orçamento de 30 s (D4) | sim |
| `fornecedor=lista` | título "Modelos que a OpenAI lista para a chave deste ambiente"; texto do critério; busca por id e nome; "Habilitado" ou "Habilitar" | itens, nome, se está habilitado, critério | `models[].id`, `displayName`, `enabled`, `filter` | sim (ver divergência V1) |
| `fornecedor=vazio` | "O fornecedor respondeu e não listou nenhum modelo…" | lista vazia que chegou | `200` com `models: []` | sim |
| `fornecedor=indisponivel` | "O fornecedor não respondeu…" | a falha sem resposta | `503` `vendor-unavailable` | sim |
| `fornecedor=erro` | "O fornecedor recusou a consulta **(resposta 401)**. Confira a chave deste provedor no ambiente do servidor…" | **o status HTTP que o fornecedor devolveu, como dado** | `502` `vendor-error`, com o status **só na prosa do `detail`** | **não: achado A1** |
| `fornecedor=nao-listado` | "O fornecedor deixou de listar o modelo escolhido. Nada foi habilitado, e a lista abaixo foi atualizada." | a recusa por não listado | `422` `model-not-listed`, e nova leitura das opções | sim |
| habilitar com sucesso | "<id> habilitado. Já pode ser escolhido no formulário de agente." | o item gravado | `POST` `201` (ou `200` já habilitado) | sim |
| `retirar=com-agentes` / `sem-agentes` | confirmação na linha com a contagem: "N agentes usam este modelo e passam a recusar novas tarefas…"; ou "Nenhum agente usa este modelo…" | a contagem | `agentCount` | sim |
| retirar confirmado | "<id> retirado. N agentes passam a recusar novas tarefas." | a contagem lida antes | `agentCount`, `DELETE` `204` | sim |
| rodapé | "Retirar um modelo não altera os agentes que o usam… Tarefas já em execução terminam normalmente." | nada | D6, D7, Non-Goals | sim |

### Achado A1: o estado de erro do fornecedor pede o status como dado

O quadro mostra o status que o fornecedor devolveu ("resposta 401") e, a partir dele,
manda conferir a chave. A D4 põe esse status **só no texto do `detail`** do
`ProblemDetails` de `502 vendor-error`. Para a tela mostrar o número e escolher o texto,
ela teria de ler prosa, o que a convenção 12 recusa (formato de fio é contrato) e o
que a D4 não promete. O texto do quadro também só é verdadeiro para `401` e `403`: para
o `404` de um gateway sem `/models`, para um `500` ou para um `200` fora do contrato,
"confira a chave" seria afirmar uma causa que o sistema não sabe (convenção 13).

**Decidido pelo dono em 19/11/2026: saída (a)**, aplicada na D4. As duas saídas que
estavam na mesa:

- **(a) Campo `vendorStatus` no `ProblemDetails`** de `502 vendor-error` (inteiro, ou
  `null` quando não houve status, como no `200` fora do contrato), com o nome fixado no
  fio. A tela escolhe o texto pelo status: `401`/`403` → conferir a chave; `404` → o
  fornecedor não publica a lista; outro → genérico com o número. O dado já existe no
  servidor e só muda de lugar. **Escolhida.**
- **(b) Códigos separados**: `vendor-rejected-credentials` para `401`/`403`, e
  `vendor-error` para o resto. Fixa a decisão de texto no servidor, mas tira da tela o
  número que o quadro mostra. Recusada.

### Divergências entre o quadro e o design (convenção 17)

O design **não** foi ajustado para caber no quadro. As quatro divergências foram
**aceitas pelo dono em 19/11/2026 como propostas**. O quadro fica como referência na
versão registrada; as correções de texto ficam marcadas como **quadro a atualizar** e
não bloqueiam a #296.

| # | onde | quadro | design | decisão (aceita em 19/11/2026) |
|---|---|---|---|---|
| V1 | texto do critério das opções | escolhe o texto **pelo provedor** | a D4 manda a tela traduzir **pelo valor de `filter`**, com valor desconhecido neutro | a #297 traduz por `filter` e adota **os textos do quadro** para os três valores, no lugar dos rascunhos da D4 ("A OpenAI não informa o tipo de cada modelo…", "Só os modelos que a Anthropic mantém ativos…", "Só os modelos que o Gemini declara aceitar geração de conteúdo. Isso não garante que o modelo converse…") |
| V2 | ordem depois de habilitar | reordena a lista na tela, por comparação de código (`a.id < b.id`) | a D2 ordena no PostgreSQL e nenhuma superfície reordena em memória (collation ≠ comparador do .NET e do JS) | a #297 relê `GET /providers/models` depois de habilitar e de retirar, e mostra a ordem do servidor |
| V3 | provedor sem modelo habilitado | um só texto: "…Ele aparece no formulário de agente, mas sem modelo para escolher." | a D2 tira de `GET /providers` o provedor **sem chave** | o texto do quadro vale com `configured: true`; com `configured: false` e `models: []` a afirmação é falsa (convenção 13), e a #297 usa **"Nenhum modelo habilitado para este provedor."**. **Quadro a atualizar.** |
| V4 | o painel do Gemini sem chave | o quadro trata o Gemini como configurado quando o painel dele está aberto | o painel só abre com `configured: true` | artifício do protótipo para mostrar o painel; sem efeito no contrato |
| V5 | estado `fornecedor=erro` (achado A1) | um texto só, com "(resposta 401)" e "confira a chave" | `vendorStatus` (D4); a tela escolhe o texto pelo status | `401`/`403`: o fornecedor recusou a consulta, conferir a chave do provedor; `404`: o fornecedor não publica a lista de modelos; outro status: texto genérico com o número; `null`: genérico, sem número. **Quadro a atualizar.** |

**Situações sem quadro, com o comportamento decidido pelo dono em 19/11/2026** (sem
quadro novo; registradas na #297):

- `409 provider-not-configured` com a página aberta (a chave saiu): a tela relê
  `GET /providers/models`, e o provedor passa ao estado "sem chave", que já tem quadro;
- `404 model-not-enabled` no `DELETE` (outro Admin retirou antes): a tela trata como
  concluído e relê a lista;
- `403` de quem foi rebaixado com a página aberta: segue o quadro `AcessoNegado` do
  mesmo canvas.

## Goals / Non-Goals

**Goals:**

- A lista de modelos habilitados por provedor no banco do `apps/api`, curada por rotas
  só do Admin.
- As opções a habilitar vindas da listagem do fornecedor, com a rota dizendo o que
  sabe sobre o que devolve (convenção 13).
- `GET /providers` e a validação de agente lendo a lista curada, com a forma do fio
  intacta.
- A recusa do `SendMessage` e da delegação (Source e Target) para modelo não
  habilitado, com motivo próprio na métrica de recusa.
- Nenhum agente que executa hoje passa a ser recusado no deploy.

**Non-Goals:**

- Telas (#297). Provedor novo. Credencial fora do ambiente (inclusive
  `anthropic-workspace-id`, ver R4). Modelo de embedding. Metadados de modelo (preço,
  contexto, `lifecycle` guardado). Sincronização automática com o fornecedor. A chave
  na delegação (#298).
- **Recusar o job já publicado.** A regra é "recusa **novas** tarefas": o job publicado
  antes da retirada executa. A continuação de uma conversa é tarefa nova e passa pela
  recusa.
- Auditoria de quem habilitou ou retirou (nenhum consumidor; convenção 2).

## Decisions

### D1. Tabela `llm_enabled_models`, chave composta `(Provider, Model)`, sem outras colunas

Entidade `EnabledLlmModel { Provider, Model }`, tabela `llm_enabled_models`, chave
primária `(Provider, Model)`, as duas `text NOT NULL`. A unicidade do par é a da chave.

- **Sem `Id` substituto:** a identidade do item **é** o par, e o par já é o que o agente
  grava (`agents."Provider"`, `agents."Model"`). Um `Id` a mais seria uma segunda
  identidade sem consumidor.
- **Sem `CreatedAt` e sem "habilitado por":** nenhuma tela nem rota pede (convenção 2).
- **Sem FK de `agents` para esta tabela:** a decisão do dono é que retirar não é
  bloqueado e o agente fica com o modelo gravado; FK impediria exatamente isso.
- **Sem CHECK sobre o `Provider`:** os ids vêm de `LlmProviders.All`; um CHECK com a lista
  exigiria migration por provedor novo. A rota de habilitar é a única escritora e recusa
  provedor desconhecido (`404`).

Recusado: jsonb por provedor na própria configuração ou numa linha só. Os dois lados do
dado (o agente e o item da lista) se cruzam por consulta relacional (contagem de uso,
recusa), e a convenção 2 pede tabela nesse caso.

### D2. `ProviderCatalogService` passa a ler o banco e vira scoped

O serviço continua sendo o **único** ponto que combina disponibilidade de ambiente
(`IsConfigured`) com a lista de modelos, agora lida de `llm_enabled_models`. Ele passa a
receber `AppDbContext` e a ser registrado como **scoped**; os métodos de leitura viram
assíncronos.

- `GetAvailableProvidersAsync`: provedores configurados, na ordem de `LlmProviders.All`,
  cada um com os modelos habilitados. **Um provedor configurado sem nenhum modelo
  habilitado aparece com `models: []`.** É "sei que não existe" (convenção 13): a chave
  está lá e ninguém habilitou nada, que é diferente de provedor sem chave (ausente da
  lista). A forma `{ id, models }` não muda.
- `ValidateAsync(provider, model)`: `ProviderNotConfigured` antes de `ModelUnavailable`,
  como hoje; o modelo vale se o par está na tabela.
- **`EnqueueingAgentHandler` e `AgentA2AServerRegistry` deixam de receber o serviço por
  construtor**: o handler é criado por agente pelo registro (singleton) e já abre o
  próprio escopo para ler o estado do agente. Ele passa a **resolver o
  `ProviderCatalogService` desse escopo e a chamar o mesmo `ValidateAsync`** que o
  cadastro e a edição chamam (D7). O registro só repassava o serviço, e deixa de
  repassar.
- **`IsProviderConfigured` fica, com outros consumidores** (corrigido no apply, convenção
  9). A primeira versão desta decisão o removia, porque o handler era o único
  consumidor em `c9733ce` e passa a usar o `ValidateAsync`. Ao escrever o serviço ficou
  claro que as rotas de curadoria fazem a mesma pergunta (o `configured` de
  `GET /providers/models` e o `409 provider-not-configured` de habilitar, D6): o método
  perde um consumidor e ganha dois, e removê-lo obrigaria a recopiar a checagem da
  chave fora do serviço, que é o que esta decisão existe para evitar.

Assim o serviço continua sendo o **único ponto** da regra "par disponível": a validação
de agente, a recusa do `SendMessage` e `GET /providers` leem a chave e a lista curada pelo
mesmo código. O teste que amarra os dois lados está na D7.

Recusado: manter singleton com `IServiceScopeFactory` interno. Esconde um escopo de
banco dentro de um serviço que parece puro, e os três chamadores HTTP já estão num
escopo de requisição.

**Ordem dos modelos:** `ORDER BY "Model"` no PostgreSQL. O par é único por construção
(chave primária), então dentro de um provedor o `Model` já é desempate total, e a regra
de `api-response-ordering` está satisfeita sem coluna extra. As duas superfícies que
servem a lista curada (`GET /providers` e `GET /providers/models`) usam a **mesma**
consulta, e por isso a mesma ordem; a collation do banco e o comparador do .NET
discordam (convenção 6), e por isso nenhuma das duas reordena em memória.

### D3. Carga inicial na migration: nove estáticos ∪ pares em uso

A migration que cria a tabela insere, por `migrationBuilder.Sql` (precedente
`20261109025039_AddUserRole`):

1. os nove pares do `ProviderModelCatalog`, literais na migration;
2. `SELECT DISTINCT "Provider", "Model" FROM agents WHERE "Provider" IN ('openai',
   'anthropic', 'gemini') AND "Model" IS NOT NULL`, com os ids de `LlmProviders.All`
   literais na migration, como os nove pares;

com `ON CONFLICT DO NOTHING`. Assim, nenhum agente que executa hoje é recusado depois do
deploy, inclusive o editado por SQL manual. **O par em uso entra mesmo que o provedor
dele esteja sem chave**: a carga não decide disponibilidade de ambiente, que continua
sendo a recusa `ProviderNotConfigured`.

**Por que o `SELECT` se restringe aos ids de `LlmProviders.All`:** um agente com
provedor fora da lista só existe por SQL manual (a validação recusa). Ele já é recusado
hoje como `ProviderNotConfigured` (`IsConfigured` não acha a definição), então habilitar
o par dele não o faria executar. E o par ficaria preso na lista curada: as rotas de
curadoria respondem `404 provider-not-found` para provedor fora de `LlmProviders.All`, e
nem o `DELETE` o alcançaria. Os ids ficam literais na migration pelo mesmo motivo dos
nove pares: migration é histórica, e um provedor novo não reescreve a carga de antes.

O `ProviderModelCatalog` é **removido** na mesma change (convenção 25: o último
consumidor sai, a declaração sai). Os nove pares passam a existir só na migration, que é
histórica e não é código vivo.

Recusado: carga no boot. Faz I/O de escrita no boot do `apps/api` em toda subida, e a
convenção 8 só aceitou I/O no boot para **checagem**, não para escrita.

### D4. Opções do fornecedor: o que a rota afirma, e como ela falha

`GET /providers/{provider}/vendor-models` consulta o fornecedor **na hora**, sem cache, e
responde com:

```json
{ "provider": "gemini", "filter": "supports-generate-content",
  "models": [ { "id": "gemini-3.6-flash", "displayName": "Gemini 3.6 Flash", "enabled": true } ] }
```

- **O que a rota afirma (convenção 13):** "os modelos que o fornecedor listou para a
  chave deste ambiente, depois do filtro deste provedor". Não afirma que o modelo
  funciona nem que é de conversa. O campo `filter` diz **qual critério foi aplicado**,
  com o nome do que o fornecedor informa, e nunca uma conclusão sobre o modelo. É enum
  como texto (convenção 12), com o nome do campo fixado no fio:
  - **OpenAI: `"none"`.** A listagem não tem campo que separe chat de embedding, imagem
    ou áudio, e filtrar pelo nome (`gpt-`, `o1`…) seria afirmar o que o fornecedor não
    disse, e quebraria em gateway.
  - **Anthropic: `"lifecycle-active"`.** O `deprecated` é "fechado a novos usuários" na
    documentação, e oferecer para habilitar o que o fornecedor fecha a novos usuários
    seria enganoso. O critério é aplicado **duas vezes**: `lifecycle=active` na consulta
    **e** o campo `lifecycle` de cada item, que precisa ser `"active"` para o item ser
    oferecido. Assim a rota não depende de o servidor honrar o parâmetro com
    `anthropic-version: 2023-06-01` (D5). **Item sem o campo `lifecycle`** torna a
    resposta inteira `200` fora do contrato (`502 vendor-error`): sem o campo não há
    como saber se o modelo está ativo. Que o campo vem nessa versão **não está medido**:
    é a conferência em homologação da tarefa 3.7 (#301). Um modelo já habilitado que vire `deprecated` **continua
    habilitado** (a lista curada não depende do fornecedor).
  - **Gemini: `"supports-generate-content"`.** Só os itens cujo
    `supportedGenerationMethods` contém `generateContent`. Isso **não** é "é de
    conversa": é o método que o fornecedor diz que o modelo aceita, e há modelos que
    aceitam `generateContent` e não conversam (de imagem, por exemplo). O nome do valor
    diz exatamente o critério. O `id` é o `name` sem o prefixo `models/`, que é o que o
    `Google.GenAI` do `apps/workers` recebe como modelo (R3).
  - **Para a #297:** a tela traduz o `filter`, e não deduz dele que a lista é só de
    conversa. Os textos são os do quadro aprovado (divergência V1): `"none"`, "A OpenAI
    não informa o tipo de cada modelo. A lista traz tudo o que a chave alcança,
    inclusive modelos de embedding, imagem e áudio, que não servem para agentes.";
    `"lifecycle-active"`, "Só os modelos que a Anthropic mantém ativos. Os
    descontinuados não são oferecidos para habilitar."; `"supports-generate-content"`,
    "Só os modelos que o Gemini declara aceitar geração de conteúdo. Isso não garante
    que o modelo converse: alguns geram imagem ou áudio.". Valor desconhecido de
    `filter` aparece neutro, nunca como lista filtrada (convenção 13).
- `displayName` é o do fornecedor quando existe (Anthropic e Gemini), e `null` na OpenAI.
- `enabled` cruza com a lista curada. Um modelo habilitado que o fornecedor **não** listou
  não aparece aqui: ele está em `GET /providers/models`, que é a lista curada.
- **Ordem:** por `id`, ordinal, no .NET. É lista vinda de fora do banco, e não é a
  mesma lista da D2 (que é ordenada pelo PostgreSQL).
- **Lista vazia é `200` com `models: []`**: o fornecedor respondeu e não listou nada (o
  caso medido no gateway). É resposta que chegou, não falha.

Falhas, com `ProblemDetails` e um código estável em `code` (precedente
`ConnectorsFolderClient`):

| situação | status | `code` | `vendorStatus` |
|---|---|---|---|
| `{provider}` fora de `LlmProviders.All` | `404` | `provider-not-found` | ausente |
| provedor sem chave no ambiente | `409` | `provider-not-configured` | ausente |
| sem resposta, erro de conexão ou orçamento estourado | `503` | `vendor-unavailable` | ausente |
| resposta não `200` (inclusive `401`, `403`, `404` do gateway sem `/models`) | `502` | `vendor-error` | o status recebido (inteiro) |
| `200` fora do contrato (JSON inválido, campo obrigatório ausente, item da Anthropic sem `lifecycle`, paginação além do teto) | `502` | `vendor-error` | `null` |

**`vendorStatus` (decisão do dono de 19/11/2026, achado A1 do protótipo):** extensão do
`ProblemDetails`, ao lado de `code`, presente **só** em `vendor-error`: o status HTTP que
o fornecedor devolveu, ou `null` quando houve `200` e o problema é o conteúdo. O nome vai
fixado no fio (convenção 12), e o `null` é escrito, não omitido: `null` é "o fornecedor
respondeu `200` fora do contrato", que é diferente de "não é erro do fornecedor". O
`detail` continua texto para pessoas e **deixa de ser a fonte do número**: a tela nunca
o lê. O `code` continua um só, `vendor-error`; quem decide o texto é a tela, pelo
`vendorStatus`:

| `vendorStatus` | o que a tela (#297) diz |
|---|---|
| `401`, `403` | o fornecedor recusou a consulta; conferir a chave do provedor |
| `404` | o fornecedor não publica a lista de modelos |
| outro inteiro | texto genérico, com o número |
| `null` | texto genérico, sem número |

O corpo da resposta do fornecedor **nunca** é repassado (pode ecoar a requisição).

**Orçamento:** um `HttpClient` nomeado por fornecedor, com `Timeout` de 15 s por página e
um orçamento de **30 s** para a listagem inteira (CTS ligado ao cancelamento da
requisição), abaixo dos 60 s de `proxy_read_timeout` do nginx do painel. Paginação até
**10 páginas** (com `limit`/`pageSize` 1000, são 10.000 modelos). Só o cancelamento do
próprio chamador propaga.

Recusado: guardar em cache a listagem. A curadoria é rara e o Admin quer o estado de
agora; cache seria uma terceira fonte de verdade.

### D5. A chave só em cabeçalho, e o cabeçalho do Anthropic é o que o `apps/workers` já usa

- **OpenAI** `Authorization: Bearer`. **Gemini** `x-goog-api-key`, **nunca** `?key=`. O
  motivo **não** é o log do `IHttpClientFactory`: desde o .NET 9 ele troca a query e o
  fragmento da URL por `*` e oculta o valor dos cabeçalhos
  ([query-redaction-logs](https://learn.microsoft.com/dotnet/core/compatibility/networking/9.0/query-redaction-logs),
  [redact-headers](https://learn.microsoft.com/dotnet/core/compatibility/networking/9.0/redact-headers)).
  Uma primeira versão deste design afirmou o contrário, de memória, e foi corrigida
  antes da revisão. Os motivos são outros: a ocultação da query depende de um switch
  (`System.Net.Http.DisableUriRedaction`) que qualquer um liga sem tocar no código; a
  URL inteira chega aos logs de acesso de proxy e gateway no caminho; e a convenção 3
  diz "nunca em URL", o que o cabeçalho cumpre por construção. O cabeçalho do Gemini
  está no exemplo REST oficial; ver R3 sobre a resposta real.
- **Anthropic** `x-api-key` e `anthropic-version: 2023-06-01`. Decompilado o pacote
  `Anthropic` 12.39.0, que o `apps/workers` usa (`Anthropic.Core/ParamsBase.cs`): com
  `ApiKey` ele envia `X-Api-Key`, e o cabeçalho padrão é `anthropic-version: 2023-06-01`.
  A documentação de hoje chama o `x-api-key` de "legacy fallback, still supported" e
  exige o `anthropic-version`. Usar o mesmo par que o caminho de produção já usa faz a
  listagem e a conversa falharem juntas, se falharem, e não em dias diferentes.
- A chave nunca entra em resposta, `ProblemDetails`, log, exceção nem URL. O log de
  falha nomeia provedor, status e código (como o `ConnectorsFolderClient`).
- Os cabeçalhos são postos **por requisição**, não no `DefaultRequestHeaders` do cliente
  nomeado, para a chave ser lida da configuração na hora (a mesma regra do
  `IsConfigured`).
- **`OpenAI:BaseUrl`:** o `apps/api` passa a lê-lo, com o mesmo padrão do `apps/workers`
  (`https://api.openai.com/v1`, `ChatClientOptions.BaseUrl`), para listar no mesmo
  endpoint em que o worker conversa. Anthropic e Gemini têm endereço fixo, como no
  worker.

### D6. Rotas de curadoria, só do Admin com o papel gravado

| rota | o que faz | fornecedor? |
|---|---|---|
| `GET /providers/models` | a lista curada dos **três** provedores, na ordem de `LlmProviders.All`: `[{ provider, configured, models: [{ id, agentCount }] }]` | não |
| `GET /providers/{provider}/vendor-models` | as opções (D4) | sim |
| `POST /providers/{provider}/models` `{ "model": "<id>" }` | habilita | sim |
| `DELETE /providers/{provider}/models?model=<id>` | retira | **não** |

- **Papel:** `AdminOnly` na `RoleRoutes` **e** `CurrentUser.FindAdminAsync` na rota, como
  `/applications`: o rebaixamento vale na requisição seguinte, sem esperar o token
  vencer. Retirar um modelo faz agentes de toda a instalação recusarem tarefas; é
  decisão de instalação, não de operação.
- **`GET /providers` continua para os três papéis**, e o formulário de agente continua
  lendo só dele.
- **`GET /providers/models` traz os três provedores, inclusive o sem chave**
  (`configured: false`): a curadoria precisa mostrar e deixar retirar o que está
  habilitado num provedor cuja chave saiu. `agentCount` é a contagem **medida** dos
  agentes, ativos ou não, gravados com aquele par (o zero é medição, pode ser exibido);
  é o que a tela da #297 usa para avisar antes de retirar.
- **Habilitar consulta o fornecedor de novo** e só grava se o id está nas opções daquele
  momento. É o servidor que impõe "sem digitação livre", e não a tela. A ordem das
  conferências, e a resposta de cada uma:
  1. corpo sem `model`, ou `model` vazio ou só com espaço → `400` (erro no campo
     `model`);
  2. `{provider}` fora de `LlmProviders.All` → `404 provider-not-found`;
  3. par **já habilitado** → `200` com o item, **sem consultar o fornecedor e com ou sem
     a chave no ambiente**. Nada é gravado, e a resposta só afirma o estado da lista
     curada, que não depende da chave (como retirar);
  4. provedor sem chave → `409 provider-not-configured`;
  5. consulta ao fornecedor; falhas da D4 sem gravar nada;
  6. `id` fora das opções → `422 model-not-listed`;
  7. grava → `201` com o item.
- **`POST` concorrente do mesmo par:** dois `POST` passam juntos pelo passo 3, e o
  segundo `INSERT` viola a chave primária. A violação é capturada no `SaveChanges`
  (`DbUpdateException` com `PostgresException` `UniqueViolation` e `ConstraintName`
  igual ao nome da chave primária, `PK_llm_enabled_models`; precedente
  `ExternalApplicationFields.IsNameUniqueViolation`) e vira **`200` com o item**, o mesmo
  do passo 3: o estado pedido é o estado que existe. Nunca `500`. Outra violação, de
  outro nome, continua propagando. O teste é determinístico, sem corrida de verdade: o
  lister falso grava o par no banco, por outro contexto, durante a consulta, e o
  `SaveChanges` da rota encontra a violação.
- **Retirar nunca consulta o fornecedor** (convenção 4), e funciona com o provedor sem
  chave. Sem o parâmetro `model`, ou com `model` vazio ou só com espaço, `400` (erro no
  campo `model`), antes de qualquer leitura; `{provider}` fora da lista,
  `404 provider-not-found`; par fora da lista curada, `404 model-not-enabled`; senão
  `204`. Os agentes com o
  par **não** mudam; passam a ser recusados (D7, D8).
- **Por que `?model=` no `DELETE`:** id de gateway compatível com OpenAI pode ter `/`
  (por exemplo `org/modelo`), que não cabe num segmento de rota. O id não é segredo.
- **Sem confirmação com a senha de quem chama** (decisão do dono de 19/11/2026, com a
  aprovação do quadro): nesta base ela guarda ações de identidade e credencial (convite,
  links, promoção, aplicação e segredo). Curar modelo não toca credencial e se desfaz
  habilitando de novo. A confirmação de retirar é na própria linha, com a contagem de
  agentes afetados.

### D7. Recusa do `SendMessage`: motivo novo `ModelNotEnabled`, depois da chave

No `EnqueueingAgentHandler`, depois de ler o estado do agente (existe, ativo, `Provider`
e `Model` não nulos), o handler chama o `ProviderCatalogService.ValidateAsync` do próprio
escopo e traduz o resultado: `ProviderNotConfigured` → motivo `ProviderNotConfigured`,
`ModelUnavailable` → motivo `ModelNotEnabled`. A ordem das recusas fica:

```
AgentNotFound → AgentInactive → ProviderOrModelMissing → ProviderNotConfigured → ModelNotEnabled
```

- O motivo novo `ModelNotEnabled` entra em `RejectionMetricsValues.Reason` e no `All`.
  É texto, nunca ordinal (convenção 12).
- **Predicado compartilhado, e não copiado:** a recusa e a validação de agente são o
  mesmo método, e a ordem "chave antes de modelo" vem dele. Custa uma consulta a mais
  por `SendMessage` (o estado do agente e o `EXISTS` do par), contra uma chamada a LLM.
  Recusado: a subconsulta `EXISTS` dentro da leitura do estado, que era a primeira
  versão deste design. Ela poria a regra em dois lugares, e o "único ponto" da D2
  deixaria de ser verdade.
- **O teste que amarra os dois lados:** uma tabela de casos (par habilitado com chave;
  par habilitado sem chave; par não habilitado com chave; par habilitado só em outro
  provedor) afirma, para cada linha, que **o `PUT /agents/{id}` aceita ⇔ o `SendMessage`
  é aceito**, e que o motivo da recusa corresponde ao campo do erro de validação
  (`provider` ↔ `ProviderNotConfigured`, `model` ↔ `ModelNotEnabled`). O guarda reintroduz
  a cópia: o handler volta a conferir só a chave, e o caso do par não habilitado reprova.
- **A chave vem antes do modelo:** com o provedor sem chave, nada desse provedor
  executa, e o motivo útil para quem opera é a chave. O par também é lido fresco a cada
  `SendMessage`, então habilitar de novo vale na mensagem seguinte, sem reiniciar nada.
- **A linha de recusa continua sem provedor e modelo** (requisito "A recusa não é
  atribuída a provedor nem a modelo"): o motivo novo tem modelo para atribuir, mas uma
  coluna preenchida só nele seria lida como distribuição.
- O Insights mostra o valor como o próprio texto até a #297 traduzi-lo
  (`rejectionReasonLabels.ts`); a soma das contagens por motivo continua fechando.

### D8. Delegação: Source e Target conferem o modelo, com a mesma leitura fresca

Em `DelegateToTargetAsync`, as duas leituras frescas (`:109` e `:122`) passam a
conferir também que o par do agente está em `llm_enabled_models`. A falha segue o
caminho que já existe para cada lado: o texto devolvido ao LLM **não muda** (Source:
"…não está mais ativo ou configurado."; Target: "…não está disponível no momento."),
`RecordNotStarted` grava o resultado `NotStarted`, e o log de aviso ganha a causa
(modelo não habilitado) para quem opera distinguir.

- **Por que o Source também:** o Source já é relido do banco justamente para pegar
  mudança de estado no meio da própria task (Decision 5 revisada, comentário em `:99`).
  Retirar o modelo do Source no meio da task é o mesmo caso.
- O `apps/workers` lê a tabela pelo contexto espelho: entidade espelho, configuração
  igual (`llm_enabled_models`, chave composta) e migration espelho com **a mesma**
  criação **e a mesma carga inicial**. Medido ao montar as tarefas: os testes de
  delegação e de execução do `apps/workers` (`AgentDelegationExecutionTests.cs:60-62`
  e mais nove arquivos) gravam agentes com `gpt-5.6-sol` e `claude-opus-5` e migram
  pela migration espelho. Um espelho sem a carga reprovaria esses casos sem defeito
  nenhum, e deixaria de ser espelho. Em produção ela continua sem rodar. O teste de
  acordo da tabela é a D9.
- A execução (`AgentExecutionService`) **não** ganha conferência: o job publicado antes
  da retirada executa (Non-Goal).

### D9. Teste de acordo do schema entre os dois contextos (convenção 11)

`tests/CrossAppTaskStoreCompatibility.Tests` ganha o acordo da tabela nova: o banco é
migrado pelo **`apps/api` real** (com a carga inicial), e o contexto do **`apps/workers`**
lê a linha que a migration do api gravou e a linha que a rota de habilitar do api gravou.
Fixture montada pelo próprio teste com o mesmo modelo dos dois lados não prova nada.

**Corrigido no apply (convenção 9):** a "linha que a rota de habilitar gravou" é gravada
pelo **modelo EF do `apps/api`** (a mesma entidade e configuração que a rota usa), e não por
uma chamada HTTP à rota: o acordo é de schema, e a camada HTTP não acrescenta nada a ele. E o
acordo ganhou um segundo caso, que a D8 pedia e este texto não: a carga do espelho é
comparada, texto a texto, com a do `apps/api` (`SeedSql` das duas migrations), porque um
espelho com carga diferente faria os testes do `apps/workers` rodarem sobre uma lista que
produção nunca teve.

### D10. Documentação

- `docs/deployment.md`: a linha de `OPENAI_BASE_URL` deixa de dizer "só checa presença"
  no api; nova nota do **gateway compatível com OpenAI**: se o `/models` dele não listar
  nada (medido) ou não existir, a curadoria não oferece modelo novo daquele provedor; os
  modelos já habilitados (inclusive os da carga inicial) continuam valendo, e o caminho
  é o gateway expor o `/models`.
- `docs/` do domínio de agentes (a página que descreve provedores e modelos) e o
  `CHANGELOG` em `[Unreleased]`.

### Árvore de pastas

```
apps/api/src/Buteco.Api/
├── Infrastructure/
│   ├── AppDbContext.cs                                   (DbSet + configuração de llm_enabled_models)
│   └── Migrations/20261119211338_AddLlmEnabledModels.cs (+ Designer, snapshot; tabela + carga D3 em SeedSql)
├── Providers/
│   ├── Entities/EnabledLlmModel.cs                       (novo)
│   ├── ProviderCatalogService.cs                         (lê o banco, scoped, assíncrono)
│   ├── ProviderModelCatalog.cs                           (REMOVIDO)
│   ├── ProviderValidationOutcome.cs                      (sem mudança)
│   ├── Endpoints/
│   │   ├── ProviderEndpoints.cs                          (GET /providers, sem mudança de forma)
│   │   ├── ProviderCurationEndpoints.cs                  (novo: as quatro rotas da D6, pelo AppDbContext direto)
│   │   └── ProviderCurationErrors.cs                     (novo: códigos, vendorStatus e ProblemDetails)
│   ├── Vendors/
│   │   ├── IVendorModelLister.cs                         (novo: keyed por provedor)
│   │   ├── VendorModelListing.cs                         (novo: filtros, resultado e falhas)
│   │   ├── VendorModelCatalog.cs                         (novo: páginas, orçamento, falhas, log)
│   │   ├── VendorModelListerRegistrationExtensions.cs    (novo: registro e checagem de boot)
│   │   ├── VendorJson.cs                                 (novo: VendorHttpClients público, VendorJson internal)
│   │   ├── OpenAiModelLister.cs                          (novo)
│   │   ├── AnthropicModelLister.cs                       (novo)
│   │   └── GeminiModelLister.cs                          (novo)
│   ├── Queries/ListProviders/ListProvidersQueryHandler.cs (assíncrono)
│   └── Responses/                                        (+ respostas de curadoria e de opções)
├── A2A/EnqueueingAgentHandler.cs                         (recusa ModelNotEnabled pelo ValidateAsync do escopo)
├── A2A/AgentA2AServerRegistry.cs                         (deixa de repassar o serviço)
├── Auth/RoleRoutes.cs                                    (quatro entradas AdminOnly)
├── Agents/Commands/{Create,Update}Agent/*Handler.cs      (ValidateAsync)
├── RejectionMetrics/RejectionMetricsValues.cs            (ModelNotEnabled)
└── Program.cs                                            (scoped, HttpClients nomeados, listers keyed)

apps/api/tests/Buteco.Api.Tests/                          (integração e unidade; fakes HTTP dos três fornecedores)

apps/workers/src/Buteco.Workers/
├── Agents/Entities/EnabledLlmModel.cs                    (novo, espelho)
├── Infrastructure/AppDbContext.cs                        (DbSet espelho)
├── Infrastructure/Migrations/20261119211417_AddLlmEnabledModels.cs (espelho, com a mesma criação e a mesma carga)
└── AgentDelegations/AgentDelegationToolSetResolver.cs    (Source e Target conferem o par)

apps/workers/tests/Buteco.Workers.Tests/                  (delegação com modelo não habilitado)
tests/CrossAppTaskStoreCompatibility.Tests/               (acordo da tabela, D9)
docs/deployment.md, docs/<agentes>.md, CHANGELOG.md
```

**`libs/ProviderCatalog` não muda.** A lista curada é dado do `apps/api` lido pelo
`apps/workers` pelo banco compartilhado, como as outras tabelas espelhadas; não há
código a compartilhar.

**Listers keyed por provedor (DI keyed):** três implementações reais, uma por
fornecedor, com contrato idêntico e escolhidas pelo id do provedor: é o padrão de ponto
de extensão tipo-plugin da casa (convenção 8). A checagem de boot confere nos dois
sentidos que todo id de `LlmProviders.All` tem um lister e que nenhum lister tem id fora
dela, sobre a `IServiceCollection`, com o teste a mais sobre a composição real que a
convenção 8 exige para essa forma.

## Risks / Trade-offs

Cada risco tem cenário e teste, ou a justificativa de por que não (convenção 10).

- **R1. Deploy recusa agente que executava.** → Carga D3 (estáticos ∪ em uso). Teste: a
  migration aplicada sobre banco **povoado** com um agente de par fora dos nove
  (inserido por SQL, como a edição manual) deixa o par habilitado e o `SendMessage`
  aceito. O guarda roda sobre banco povoado; sobre banco vazio a carga de pares em uso é
  vácua (convenção 15) e o teste afirma a precondição.
- **R2. A chave vaza em URL, log ou resposta.** → D5. Teste: o fake HTTP do Gemini
  afirma que a chave chegou em `x-goog-api-key` e que a URL recebida não tem `key=`; um
  teste de log captura as linhas das três listagens (inclusive a de falha) e afirma que
  a chave não aparece; a resposta de falha não contém a chave nem o corpo do fornecedor.
- **R3. O contrato real de Anthropic e Gemini diverge do documentado** (não há chave
  local para gravar resposta real; o `id` do Gemini pode não ser o que o `Google.GenAI`
  aceita). → Fakes com o JSON **copiado da documentação de hoje**. **Os dois contratos
  seguem NÃO medidos até a homologação** (decisão do dono de 19/11/2026: sem chave
  agora, a conferência real passa a ser feita por ele **na homologação, depois do
  deploy**, e não bloqueia o archive; tarefa 3.7, issue #301, roteiro no
  `docs/deployment.md`). A falha de contrato degrada para `502 vendor-error` na curadoria,
  sem efeito na execução: nenhum agente em execução é afetado, e os modelos já habilitados
  continuam valendo. Se divergir: issue de correção.
- **R4. Chave da Anthropic que exige `anthropic-workspace-id`** (chave não escopada a um
  workspace) recebe erro na listagem. → Fora de escopo (credencial fora do ambiente); vira
  `502 vendor-error` com o status no `detail`. A mesma chave já falharia na conversa, no
  worker. Sem teste além do de `vendor-error`.
- **R5. Fornecedor lento derruba o painel.** → Orçamento de 30 s (D4). Teste: fake que
  nunca responde; a rota responde `503 vendor-unavailable` dentro do orçamento.
- **R6. A listagem entra no caminho da mensagem ou no boot.** → Teste: com os três fakes
  configurados para falhar, o `apps/api` sobe e o `SendMessage` para agente habilitado é
  aceito; o fake registra **zero** chamadas.
- **R7. Retirar depende do fornecedor.** → Teste: com o fake fora do ar **e** com o
  provedor sem chave, o `DELETE` responde `204` e o fake registra zero chamadas.
- **R8. A delegação continua executando o agente recusado na rota A2A.** → D8. Testes no
  `apps/workers`: Target com par fora da lista não recebe task (nenhuma mensagem
  publicada, resultado `NotStarted`); idem para o Source.
- **R9. O espelho diverge do api.** → D9.
- **R10. Edição de agente com modelo retirado responde `400` até trocar o modelo**,
  inclusive para mudar só o nome. → Comportamento desejado (o agente sai do estado
  recusado só com modelo habilitado). Cenário em `agent-catalog` e teste.
- **R11. A OpenAI e o Gemini oferecem modelo que não conversa.** A OpenAI não separa
  nada; o filtro do Gemini (`generateContent`) deixa passar modelo que gera conteúdo e
  não conversa. → Aceito: a rota diz o critério em `filter`, sem afirmar "de conversa",
  e a #297 avisa o Admin com o texto da D4. Habilitar um desses faz a task falhar na
  execução, com o motivo de falha que já existe. Teste: o texto do JSON traz
  `"filter":"none"`, `"filter":"lifecycle-active"` e `"filter":"supports-generate-content"`
  nos três provedores, e **nenhum** campo `onlyChatModels` (convenção 12).
- **R12. O servidor da Anthropic ignora `lifecycle=active` com a versão
  `2023-06-01`.** → Filtro pelo campo de cada item (D4). Teste: o fake devolve um item
  `deprecated` mesmo com o parâmetro, e a rota não o oferece nem o `POST` o habilita.
  Guarda na tarefa 3.4. **O comportamento real do servidor com `2023-06-01` segue NÃO
  medido** (o parâmetro honrado ou não, e o campo `lifecycle` presente em cada item):
  conferência em homologação, tarefa 3.7 (#301). Se o campo faltar, a listagem da
  Anthropic responde `502` com `vendorStatus` `null` e não oferece nada; a curadoria dos
  outros provedores e os agentes não são afetados.
- **R13. Corrida de dois `POST` do mesmo par.** → D6. Teste determinístico com o lister
  falso gravando o par durante a consulta; guarda na tarefa 4.4.
- **R14. A tela lê o status do texto do `detail`, ou o campo some do fio.** → D4:
  `vendorStatus` como extensão com nome fixado. Teste: o texto do JSON de `502` traz
  `"vendorStatus":401` no `401`, `"vendorStatus":404` no `404` do gateway e
  `"vendorStatus":null` no `200` fora do contrato (escrito, não omitido), e as respostas
  `503`, `409` e `404 provider-not-found` não trazem o campo. Guarda na tarefa 3.6.
- **Trade-off:** com gateway que não lista nada, a instalação fica presa aos modelos da
  carga inicial para aquele provedor. Aceito pelo dono (Q2), e documentado (D10).

### O que o apply mudou, e cada risco no teste que o cobre (convenções 9 e 10)

**A varredura da tarefa 0.3 mediu um nome quando a change age sobre um mecanismo**
(convenção 6, "a fonte foi consultada corretamente e a PERGUNTA estava errada"). Ela procurou
os textos `ProviderModelCatalog`, `ProviderCatalogService`, `Reason.All`, `"/providers"` (com
as aspas) e os nove ids de modelo. Mas esta change **acrescenta rotas ao host**, e todo teste
que enumera os endpoints do host é alcançado por qualquer rota nova, cite ele `/providers` ou
não. `FederatedClientWithoutProviderTests.TheHostWithoutProvider_ExposesTheSameEndpoints` (da
#281) traz `"GET /providers"`, que o padrão com as aspas não casa, e reprovou na primeira
`api` de fechamento: "A mais: DELETE /providers/{provider}/models; GET /providers/models; GET
/providers/{provider}/vendor-models; POST /providers/{provider}/models". A `RoleMatrixTests`
só entrou na lista do topo porque tem `"/providers"` como chave literal, ou seja, por acaso.
A pergunta certa era "que teste enumera as rotas do host", e a resposta, medida depois:
`HostEndpointList.` tem um uso só, este caso, que compara contra uma **lista gravada**; e
`EndpointDataSource` é lido também por `NginxRouteCoverageTests`, `RouteDeclarationValidationTests`,
`ServiceScopeAuthorizationTests`, `RouteAuthenticationTests` e `ExternalApplicationAuthorizationTests`,
que aplicam uma **regra** a cada rota (cobertura do nginx, declaração de papel, escopo de
serviço) em vez de comparar com uma lista, e por isso aceitam rota nova que cumpra a regra:
na primeira `api` de fechamento, 1 + 8 + 12 + 13 + 5 casos, todos aprovados (`api.trx`). O
dono aceitou acrescentar as quatro rotas à lista gravada (19/11/2026), com a asserção igual.

Corrigido no apply, cada um no ponto do design que ele toca: a D2 (`IsProviderConfigured`
fica, para as rotas de curadoria), a D9 (o par gravado pelo modelo EF do `apps/api`, e o
acordo da carga do espelho) e a árvore acima (as rotas pelo `AppDbContext` direto, como
`/applications`; o `VendorHttpClients` público). Nenhum muda contrato nem comportamento.

| risco | teste |
|---|---|
| R1 | `LlmEnabledModelsMigrationTests` (carga sobre banco vazio e povoado) e `AgentBeforeCurationMigrationTests` (de ponta a ponta, `SendMessage` aceito) |
| R2 | `VendorModelsEndpointTests.TheGeminiKey_GoesInTheHeader_AndNeverInTheUrl`, `TheAnthropicHeaders_AreTheOnesTheWorkersSdkSends`, `On401_NeitherTheLogNorTheResponseCarryTheKey_OrTheVendorBody` (três provedores, todo o log do host em nível `Trace`) |
| R3 | **não medido até a homologação**: tarefa 3.7 (#301), roteiro no `docs/deployment.md`; os fakes copiam a documentação de 19/11/2026 |
| R4 | `VendorModelsEndpointTests.AKeyRefusedByTheVendor_Is502_WithVendorStatus401` (o erro da chave chega como `vendor-error` com o status) |
| R5 | `VendorModelsEndpointTests.AVendorThatNeverAnswers_Is503_WithinTheBudget_AndWithoutVendorStatus` |
| R6 | `ProviderAvailabilityTests.SendMessage_DoesNotCallAnyVendor`, `ProviderAvailabilityTests.*Providers*` (sem chamada), e o boot em toda classe com os fakes |
| R7 | `ModelCurationEndpointsTests.Retiring_WithTheVendorOutAndTheProviderWithoutKey_Is204_WithoutCallingTheVendor` |
| R8 | `AgentDelegationExecutionTests.TargetWithModelNotEnabled_RecordsNotStarted_WithoutTargetTask` e `SourceModelRetiredDuringOwnProcessing_DegradesGracefully_NoTaskCreatedForTarget` |
| R9 | `EnabledLlmModelsCrossAppCompatibilityTests` (leitura pelo espelho e a carga igual nas duas migrations) |
| R10 | `ProviderAvailabilityTests.UpdatingWhileKeepingARetiredModel_Is400OnModel_AndTheRecordStays` |
| R11 | `VendorModelsEndpointTests.OpenAi_ListsEverything_AndTheWireSaysFilterNone`, `Gemini_ListsOnlyGenerateContent_WithoutThePrefix`, `Anthropic_WalksThePages_WithAfterIdEqualToTheLastId` (o `filter` no texto do JSON, sem `onlyChatModels`) |
| R12 | `VendorModelsEndpointTests.Anthropic_DoesNotOfferADeprecatedItemTheServerReturned` e `ModelCurationEndpointsTests.AnAnthropicDeprecatedModel_DoesNotEnable`; a resposta real, na 3.7 |
| R13 | `ModelCurationEndpointsTests.TwoRequestsForTheSamePair_AnswerTheSecondWith200_NeverWith500` |
| R14 | `VendorModelsEndpointTests.AGatewayWithoutTheListRoute_Is502_WithVendorStatus404`, `AKeyRefusedByTheVendor_Is502_WithVendorStatus401`, `A200OutOfContract_Is502_WithVendorStatusNullWritten`, e a ausência do campo no `503`, no `409` e no `404` |

## Migration Plan

1. A migration do `apps/api` cria `llm_enabled_models` e faz a carga D3; o `migrator` do
   compose a aplica antes de o `api` e o `workers` subirem
   (`depends_on: migrator: service_completed_successfully`).
2. A migration espelho do `apps/workers` não roda em produção.
3. Sem variável nova: `OpenAI__BaseUrl` já chega ao serviço `api`.
4. **Rollback:** a versão anterior ignora a tabela (o catálogo estático volta a valer, e
   ela não confere modelo na execução). O `Down` da migration a remove. Habilitações
   feitas depois do deploy se perdem no `Down`.

## Open Questions

Nenhuma. As duas que estavam aqui (senha ao retirar; Anthropic só `active`) foram
decididas pelo dono em 19/11/2026, com a aprovação do protótipo, e estão em "Decisões do
dono". A conferência real de Anthropic e Gemini foi movida pelo dono (19/11/2026) para a
homologação, depois do deploy, e não bloqueia o archive (tarefa 3.7, #301); até lá, os dois
contratos seguem não medidos (R3, R12).
