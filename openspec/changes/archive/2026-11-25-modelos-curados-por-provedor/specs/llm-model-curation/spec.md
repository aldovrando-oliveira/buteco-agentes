## Purpose

Diz quais modelos de LLM cada provedor oferece nesta instalação e quem decide:
a lista curada no banco do `apps/api`, a carga inicial que a criou, as rotas só do
Admin que a leem, habilitam e retiram, e a consulta às opções da listagem do
próprio fornecedor, com o que essa consulta afirma, como ela falha e onde ela
nunca entra (boot, mensagem, retirada). É a fonte dos modelos que
`llm-provider-catalog` serve e valida, e que `a2a-task-lifecycle` e
`agent-delegation-execution` conferem antes de aceitar tarefa (change
`modelos-curados-por-provedor`, #296).

## ADDED Requirements

### Requirement: Lista curada de modelos habilitados por provedor no banco do apps/api
O `apps/api` SHALL guardar os modelos habilitados de cada provedor de LLM numa
tabela própria (`llm_enabled_models`), um registro por par `provider`+`model`,
com o par como chave. Essa lista SHALL ser a única fonte dos modelos
disponíveis para `GET /providers`, para a validação de agente, para a recusa do
`SendMessage` e para a delegação. Nenhum catálogo de modelos SHALL existir no
código.

O `provider` de todo registro gravado pelas rotas SHALL ser um dos ids de
`LlmProviders.All`. Retirar um modelo SHALL NOT alterar os agentes que o usam.

#### Scenario: O par é único
- **WHEN** o mesmo par `provider`+`model` é habilitado duas vezes
- **THEN** a lista curada tem um registro só desse par

#### Scenario: Retirar não altera o agente
- **WHEN** o Admin retira um modelo usado por um agente
- **THEN** `GET /agents/{id}` do agente continua respondendo o mesmo
  `provider` e o mesmo `model`

### Requirement: Carga inicial preserva todo agente que executa
A migration que cria a lista curada SHALL habilitar, na mesma aplicação, os nove
pares do catálogo estático anterior e todo par `provider`+`model` distinto
gravado em agentes com `provider` entre os ids de `LlmProviders.All` e `model`
não nulo, sem duplicar pares, ainda que o provedor do par esteja sem chave no
ambiente. Par de agente com `provider` fora de `LlmProviders.All` SHALL NOT entrar
na lista curada.

#### Scenario: Pares do catálogo estático habilitados
- **WHEN** a migration é aplicada sobre um banco sem agentes
- **THEN** a lista curada tem exatamente os nove pares do catálogo estático
  anterior

#### Scenario: Par em uso fora do catálogo estático habilitado
- **WHEN** a migration é aplicada sobre um banco com um agente gravado com um
  par fora dos nove do catálogo estático
- **THEN** a lista curada tem os nove pares e também o par desse agente, uma vez
  só, mesmo que outro agente use o mesmo par

#### Scenario: Agente com provedor fora da lista de provedores não gera par
- **WHEN** a migration é aplicada sobre um banco com um agente gravado por SQL
  com `provider` `mistral`, que não está em `LlmProviders.All`
- **THEN** nenhum par com `provider` `mistral` entra na lista curada

#### Scenario: Agente sem provider ou model não gera par
- **WHEN** a migration é aplicada sobre um banco com um agente legado de
  `provider`/`model` nulos
- **THEN** nenhum par com valor nulo entra na lista curada

### Requirement: Leitura da lista curada pelo Admin
O `apps/api` SHALL expor `GET /providers/models`, que responde, para cada um dos
provedores de `LlmProviders.All` e nessa ordem, `provider`, `configured` (se a
chave está no ambiente) e `models`, os modelos habilitados do provedor com `id`
e `agentCount`. A rota SHALL incluir provedor sem chave e provedor sem modelo
habilitado (com `models: []`).

`agentCount` SHALL ser a contagem medida dos agentes, ativos ou não, gravados
com aquele par; zero é resultado da contagem. A ordem dos modelos SHALL ser a
mesma de `GET /providers`, da mesma consulta. A rota SHALL NOT consultar
fornecedor.

#### Scenario: Provedor sem chave aparece com os modelos habilitados
- **WHEN** o Admin chama `GET /providers/models` com a chave do Gemini ausente
  do ambiente e um modelo do Gemini habilitado
- **THEN** a resposta traz o Gemini com `configured: false` e o modelo
  habilitado

#### Scenario: Contagem de agentes por modelo
- **WHEN** dois agentes, um ativo e um inativo, usam o mesmo par habilitado, e
  outro par habilitado não tem agente
- **THEN** a resposta traz `agentCount: 2` no primeiro par e `agentCount: 0` no
  segundo

#### Scenario: Lista vazia de um provedor
- **WHEN** o provedor OpenAI tem a chave configurada e nenhum modelo habilitado
- **THEN** a resposta traz a OpenAI com `configured: true` e `models: []`

### Requirement: Opções de modelo vindas da listagem do fornecedor
O `apps/api` SHALL expor `GET /providers/{provider}/vendor-models`, que consulta
na hora, sem cache, a listagem de modelos do fornecedor com a chave do ambiente
e responde com `provider`, `filter` e `models`, cada item com `id`,
`displayName` (o nome do fornecedor, ou `null` quando ele não o dá) e `enabled`
(se o par está na lista curada). A rota afirma "os modelos que o fornecedor
listou para a chave deste ambiente, depois do filtro do provedor", e SHALL NOT
afirmar mais do que isso. `filter` SHALL dizer qual critério foi aplicado, com o
nome do que o fornecedor informa, e SHALL NOT afirmar que os modelos são de
conversa:

- **OpenAI** (`GET {OpenAI:BaseUrl}/models`): todos os itens de `data`, sem
  filtro, com `filter` `"none"`, porque a listagem não distingue modelo de
  conversa de embedding, imagem ou áudio. O `apps/api` SHALL ler
  `OpenAI:BaseUrl` com o padrão `https://api.openai.com/v1`.
- **Anthropic** (`GET https://api.anthropic.com/v1/models`, `lifecycle=active`):
  só os itens cujo campo `lifecycle` é `"active"`, ainda que o servidor devolva
  outros apesar do parâmetro, com `filter` `"lifecycle-active"`, percorrendo as
  páginas por `after_id` enquanto `has_more`. Item sem o campo `lifecycle` torna a
  resposta inteira fora do contrato.
- **Gemini** (`GET https://generativelanguage.googleapis.com/v1beta/models`): só
  os itens cujo `supportedGenerationMethods` contém `generateContent`, com o
  `id` igual ao `name` sem o prefixo `models/`, e `filter`
  `"supports-generate-content"`, percorrendo as páginas por `pageToken` enquanto
  houver `nextPageToken`. Aceitar `generateContent` SHALL NOT ser apresentado como
  ser modelo de conversa.

Os itens SHALL sair ordenados por `id`, ordinal. Um fornecedor que responde
`200` sem nenhum item SHALL resultar em `200` com `models: []`. O nome do campo
`filter` SHALL ser fixado no fio, e os valores SHALL ser texto, nunca ordinal.

#### Scenario: OpenAI lista tudo e diz que não filtra
- **WHEN** o Admin chama `GET /providers/openai/vendor-models` e o fornecedor
  lista um modelo de chat e um de embedding
- **THEN** a resposta traz os dois, e o texto do JSON tem `"filter":"none"` e
  nenhum campo `onlyChatModels`

#### Scenario: Gemini só lista modelos que geram conteúdo
- **WHEN** o fornecedor Gemini lista `models/gemini-x` com `generateContent` e
  `models/embedding-y` só com `embedContent`
- **THEN** a resposta traz só `gemini-x`, sem o prefixo, e o texto do JSON tem
  `"filter":"supports-generate-content"`

#### Scenario: Anthropic não oferece item deprecated que o servidor devolveu
- **WHEN** o fornecedor Anthropic, consultado com `lifecycle=active`, devolve um
  item `active` e um item com `lifecycle` `"deprecated"`
- **THEN** a resposta traz só o item `active`, e o texto do JSON tem
  `"filter":"lifecycle-active"`

#### Scenario: Item da Anthropic sem lifecycle
- **WHEN** o fornecedor Anthropic devolve um item sem o campo `lifecycle`
- **THEN** a rota responde `502` com `code` `vendor-error`

#### Scenario: Anthropic percorre as páginas
- **WHEN** o fornecedor Anthropic responde a primeira página com `has_more: true`
  e a segunda com `has_more: false`
- **THEN** a resposta traz os itens das duas páginas, e a segunda requisição
  levou `after_id` igual ao `last_id` da primeira

#### Scenario: Modelo já habilitado vem marcado
- **WHEN** o fornecedor lista um modelo que está na lista curada e outro que não
  está
- **THEN** o primeiro vem com `enabled: true` e o segundo com `enabled: false`

#### Scenario: Fornecedor que não lista nada
- **WHEN** o fornecedor responde `200` com a lista vazia (como o gateway
  compatível com OpenAI medido em 19/11/2026)
- **THEN** a rota responde `200` com `models: []`

### Requirement: A chave do provedor nunca sai do processo pela listagem
A chave do provedor SHALL ir só em cabeçalho na consulta ao fornecedor:
`Authorization: Bearer` na OpenAI, `x-api-key` com `anthropic-version:
2023-06-01` na Anthropic e `x-goog-api-key` no Gemini. A chave SHALL NOT aparecer
em URL, em linha de log, em exceção registrada nem em resposta de nenhuma rota,
inclusive nas de falha. O corpo de resposta do fornecedor SHALL NOT ser
repassado.

#### Scenario: Chave do Gemini em cabeçalho, nunca na URL
- **WHEN** o `apps/api` consulta a listagem do Gemini
- **THEN** a requisição recebida pelo fornecedor traz a chave em `x-goog-api-key`
  e a URL dela não tem o parâmetro `key`

#### Scenario: Chave fora do log e da resposta de falha
- **WHEN** a listagem de cada um dos três fornecedores responde `401`
- **THEN** nenhuma linha de log produzida pela consulta e nenhum corpo de
  resposta da rota contém a chave, e o corpo da rota não contém o corpo
  devolvido pelo fornecedor

### Requirement: Falha do fornecedor só impede habilitar
A falha da listagem do fornecedor SHALL ser devolvida pela rota de opções e pela
de habilitar como `ProblemDetails` com um `code` estável, sem gravar nada:

| situação | status | `code` | `vendorStatus` |
|---|---|---|---|
| `{provider}` fora de `LlmProviders.All` | `404` | `provider-not-found` | ausente |
| provedor sem chave no ambiente | `409` | `provider-not-configured` | ausente |
| sem resposta, erro de conexão ou orçamento de 30 s estourado | `503` | `vendor-unavailable` | ausente |
| resposta não `200`, inclusive `401`, `403` e `404` | `502` | `vendor-error` | o status recebido, inteiro |
| `200` fora do contrato (inclusive item da Anthropic sem `lifecycle`) ou mais de 10 páginas | `502` | `vendor-error` | `null` |

A resposta `vendor-error` SHALL trazer `vendorStatus` como membro do
`ProblemDetails`, ao lado de `code`, com esse nome fixado no fio: o status HTTP que
o fornecedor devolveu, ou `null`, escrito e não omitido, quando o fornecedor
respondeu `200` fora do contrato. As outras respostas de falha SHALL NOT trazer o
campo. O número SHALL vir só de `vendorStatus`; o `detail` é texto para pessoas e
SHALL NOT ser a fonte dele. O `code` SHALL continuar um só, `vendor-error`, para
qualquer `vendorStatus`.

Só o cancelamento da própria requisição SHALL propagar. A listagem do fornecedor
SHALL NOT ser consultada no boot, em `GET /providers`, em `GET /providers/models`,
na retirada, na validação de agente, no `SendMessage` nem na delegação.

#### Scenario: Gateway sem a rota de listagem
- **WHEN** o fornecedor OpenAI configurado responde `404` em `/models`
- **THEN** a rota de opções responde `502` com `code` `vendor-error`, e o texto do
  JSON tem `"vendorStatus":404`

#### Scenario: Chave recusada pelo fornecedor
- **WHEN** o fornecedor responde `401` à listagem
- **THEN** a rota de opções responde `502` com `code` `vendor-error`, e o texto do
  JSON tem `"vendorStatus":401`

#### Scenario: Resposta fora do contrato não tem status a informar
- **WHEN** o fornecedor responde `200` com um JSON que não segue o contrato
- **THEN** a rota de opções responde `502` com `code` `vendor-error`, e o texto do
  JSON tem `"vendorStatus":null`

#### Scenario: Falha sem resposta do fornecedor não traz vendorStatus
- **WHEN** o fornecedor não responde dentro do orçamento, e, noutro pedido, o
  provedor está sem chave
- **THEN** as respostas `503` e `409` não têm o campo `vendorStatus`

#### Scenario: Fornecedor que não responde
- **WHEN** o fornecedor aceita a conexão e nunca responde
- **THEN** a rota de opções responde `503` com `code` `vendor-unavailable` dentro
  do orçamento de 30 s

#### Scenario: Provedor sem chave
- **WHEN** o Admin chama `GET /providers/anthropic/vendor-models` sem a chave da
  Anthropic no ambiente
- **THEN** a resposta é `409` com `code` `provider-not-configured`, e nenhuma
  chamada é feita ao fornecedor

#### Scenario: Boot e mensagem não dependem do fornecedor
- **WHEN** os três fornecedores estão configurados para falhar, o `apps/api`
  sobe, e um `SendMessage` chega para um agente com modelo habilitado
- **THEN** o boot conclui, o `SendMessage` é aceito, e nenhuma chamada é feita a
  fornecedor

### Requirement: Habilitar só o que o fornecedor listou
O `apps/api` SHALL expor `POST /providers/{provider}/models` com o corpo
`{ "model": "<id>" }`, que consulta o fornecedor na hora e só grava o par se o
`id` estiver entre as opções daquele momento (depois do filtro do provedor). As
conferências SHALL seguir esta ordem:

1. corpo sem `model`, ou `model` vazio ou só com espaço: `400`, com o erro no
   campo `model`;
2. `{provider}` fora de `LlmProviders.All`: `404` com `code` `provider-not-found`;
3. par já habilitado: `200` com o item, sem consultar o fornecedor e sem gravar,
   com ou sem a chave do provedor no ambiente;
4. provedor sem chave: `409` com `code` `provider-not-configured`;
5. falhas do fornecedor: as da listagem, sem gravar;
6. `id` fora das opções: `422` com `code` `model-not-listed`, sem gravar;
7. senão grava e responde `201` com o item.

A violação da chave primária do par ao gravar (outro pedido gravou o mesmo par
entre o passo 3 e a gravação) SHALL responder `200` com o item, como o passo 3, e
nunca `500`.

#### Scenario: Habilitar um modelo listado
- **WHEN** o Admin envia `POST /providers/gemini/models` com um `model` que o
  fornecedor lista com `generateContent`
- **THEN** a resposta é `201`, e `GET /providers` passa a trazer o modelo no
  Gemini

#### Scenario: Id digitado que o fornecedor não lista
- **WHEN** o Admin envia `POST /providers/openai/models` com um `model` que o
  fornecedor não lista
- **THEN** a resposta é `422` com `code` `model-not-listed`, e a lista curada não
  muda

#### Scenario: Modelo de embedding do Gemini não habilita
- **WHEN** o Admin envia `POST /providers/gemini/models` com o id de um modelo que
  o fornecedor lista só com `embedContent`
- **THEN** a resposta é `422` com `code` `model-not-listed`

#### Scenario: Fornecedor fora do ar impede habilitar
- **WHEN** o fornecedor não responde e o Admin tenta habilitar um modelo novo
- **THEN** a resposta é `503` com `code` `vendor-unavailable`, e a lista curada não
  muda

#### Scenario: Modelo deprecated da Anthropic não habilita
- **WHEN** o Admin envia `POST /providers/anthropic/models` com o id de um item que
  o fornecedor devolveu com `lifecycle` `"deprecated"`
- **THEN** a resposta é `422` com `code` `model-not-listed`, e a lista curada não
  muda

#### Scenario: Par já habilitado com o provedor sem chave
- **WHEN** a chave do Gemini foi removida do ambiente e o Admin envia
  `POST /providers/gemini/models` com um par que já está na lista curada
- **THEN** a resposta é `200` com o item, e nenhuma chamada é feita ao fornecedor

#### Scenario: Par novo com o provedor sem chave
- **WHEN** a chave do Gemini foi removida do ambiente e o Admin envia
  `POST /providers/gemini/models` com um par que não está na lista curada
- **THEN** a resposta é `409` com `code` `provider-not-configured`, e nenhuma
  chamada é feita ao fornecedor

#### Scenario: Dois pedidos do mesmo par ao mesmo tempo
- **WHEN** outro pedido grava o mesmo par depois de este passar pela conferência
  de par já habilitado e antes de ele gravar
- **THEN** a resposta é `200` com o item, nunca `500`, e a lista curada tem um
  registro só do par

#### Scenario: Habilitar sem model
- **WHEN** o Admin envia `POST /providers/openai/models` sem `model`, e depois com
  `"model": ""`
- **THEN** as duas respostas são `400` com o erro no campo `model`, e nenhuma
  chamada é feita ao fornecedor

#### Scenario: Habilitar de novo é idempotente
- **WHEN** o Admin habilita um par que já está na lista curada, com o fornecedor
  fora do ar
- **THEN** a resposta é `200`, e nenhuma chamada é feita ao fornecedor

### Requirement: Retirar nunca depende do fornecedor
O `apps/api` SHALL expor `DELETE /providers/{provider}/models?model=<id>`, que
remove o par da lista curada e responde `204`, sem consultar o fornecedor e com
ou sem a chave do provedor no ambiente. Sem o parâmetro `model`, ou com `model`
vazio ou só com espaço, SHALL responder `400` com o erro no campo `model`, antes de
qualquer leitura. `{provider}` fora de `LlmProviders.All` SHALL responder `404`
com `code` `provider-not-found`. O par que não está na lista SHALL responder `404`
com `code` `model-not-enabled`.

#### Scenario: Retirar com o fornecedor fora do ar e sem chave
- **WHEN** o fornecedor está fora do ar, a chave do provedor foi removida do
  ambiente, e o Admin retira um modelo habilitado desse provedor
- **THEN** a resposta é `204`, o par sai da lista curada, e nenhuma chamada é
  feita ao fornecedor

#### Scenario: Id com barra
- **WHEN** o Admin retira o modelo `org/modelo-x`, habilitado para a OpenAI
- **THEN** a resposta é `204` e o par sai da lista curada

#### Scenario: Retirar sem model
- **WHEN** o Admin envia `DELETE /providers/openai/models` sem o parâmetro `model`,
  e depois com `?model=`
- **THEN** as duas respostas são `400` com o erro no campo `model`, e a lista
  curada não muda

#### Scenario: Retirar o que não está habilitado
- **WHEN** o Admin retira um par que não está na lista curada
- **THEN** a resposta é `404` com `code` `model-not-enabled`

### Requirement: Curadoria só do Admin, com o papel gravado
As quatro rotas de curadoria SHALL exigir o papel Admin na tabela de papéis por
rota e, na própria rota, o papel gravado do usuário que chama, como as rotas de
aplicações externas: um usuário rebaixado a Operador SHALL receber `403` na
requisição seguinte, ainda que o token diga Admin.

#### Scenario: Admin rebaixado com token antigo
- **WHEN** um Admin é rebaixado a Operador e, com o token emitido antes do
  rebaixamento, chama `DELETE /providers/{provider}/models?model=<id>`
- **THEN** a resposta é `403`, e o modelo continua habilitado
