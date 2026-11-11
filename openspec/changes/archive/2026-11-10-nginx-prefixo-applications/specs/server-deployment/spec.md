## MODIFIED Requirements

### Requirement: Nginx do stack roteia por prefixo, sem expor porta pública diretamente
O sistema SHALL prover um container de nginx, parte do
`docker-compose.prod.yml`, que serve o build estático de `apps/frontend`
com fallback de SPA e roteia requisições por prefixo de path para
`apps/api`, `apps/inbox` ou `apps/connectors` via nome de serviço na rede do
compose. Este container SHALL publicar apenas uma porta interna do host, nunca
80/443 diretamente.

O conjunto de prefixos roteados SHALL cobrir **todos** os prefixos de rota
servidos por `apps/api`, `apps/inbox` e `apps/connectors`. Prefixo servido por um
dos apps e ausente da configuração do nginx SHALL ser tratado como defeito, não
como omissão deliberada: o fallback de SPA responde `200` com HTML, e o
consumidor recebe sucesso com conteúdo inválido em vez de erro.

As exceções SHALL ser explícitas, nomeadas e com o motivo de cada uma:
- `/health`, nos três apps: os healthchecks executam dentro de cada container
  contra o próprio processo e não atravessam o nginx;
- `/sync`, no `apps/api`: só o `apps/connectors` a chama, pela rede interna do
  compose, e ela SHALL NOT ser alcançável pelo domínio público;
- os endpoints do emissor OIDC do `apps/api` que têm bloco exato próprio
  (`/.well-known/openid-configuration`, `/.well-known/jwks`,
  `/connect/authorize` e `/connect/logout`): `/.well-known/` é prefixo de uso
  geral na web e não entra inteiro, e o authorize e o logout são navegação de
  browser por definição, sem o desvio de navegação.

A cobertura SHALL ser conferida por um teste na suíte de cada app que o nginx
roteia, sobre os endpoints do host construído do próprio app (a composição real,
não uma lista mantida à mão), comparados com a configuração do nginx lida do
repositório, nos dois sentidos: prefixo servido sem bloco reprova, e prefixo de
bloco que o app não serve reprova. Exceção que deixou de valer (a rota saiu, ou o
bloco exato saiu) também reprova. A mensagem SHALL nomear o prefixo.

Todo bloco de prefixo de API SHALL aplicar o mesmo desvio de navegação real
(`Sec-Fetch-Mode: navigate` cai no shell do SPA), inclusive quando o prefixo não é
rota de página do `react-router` no momento.

Acrescentar prefixo de rota de primeiro nível a `apps/api`, a `apps/inbox` ou a
`apps/connectors` SHALL incluir a entrada correspondente nesta configuração, ainda
que a change que cria a rota não toque nenhum outro arquivo de `apps/frontend`. A
configuração mora em `apps/frontend/deploy/`, e por isso conferência de escopo
restrita aos arquivos do app que serve a rota SHALL NOT ser tratada como
suficiente para detectar a omissão.

#### Scenario: Rota profunda do SPA sobrevive a refresh
- **WHEN** o browser faz uma navegação real (`Sec-Fetch-Mode: navigate`,
  enviado automaticamente pelo browser) direto numa rota do
  `react-router` que não colide com prefixo de API (ex. `/channels/new`)
  contra o nginx do stack
- **THEN** o nginx responde com `index.html` (fallback de SPA), não 404

#### Scenario: Navegação real prevalece sobre prefixo de API em rota compartilhada
- **WHEN** o browser faz uma navegação real (`Sec-Fetch-Mode: navigate`)
  num path que é ao mesmo tempo rota de página do `react-router` e
  prefixo de API (`/agents/{id}`, `/channels/{id}`, `/mcp-servers/{id}` ou
  `/knowledge-bases/{id}`)
- **THEN** o nginx responde com `index.html` (fallback de SPA), não
  encaminha a requisição para `apps/api`/`apps/inbox`

#### Scenario: Requisições de API são roteadas para o app correto
- **WHEN** uma requisição sem `Sec-Fetch-Mode: navigate` (fetch/XHR
  disparado pela própria SPA já carregada) chega ao nginx do stack com
  path iniciando em `/agents`, `/providers`, `/mcp-servers`,
  `/knowledge-bases`, `/knowledge-index`, `/insights`, `/auth`, `/account`,
  `/users`, `/identity`, `/applications` ou `/connect`
- **THEN** ela é encaminhada para o container de `apps/api`; requisições
  no mesmo formato com path iniciando em `/channels`, `/contacts`,
  `/sessions`, `/messages`, `/webhooks` ou `/internal` são encaminhadas para o
  container de `apps/inbox`; e com path iniciando em `/connectors`, para o
  container de `apps/connectors`

#### Scenario: Prefixo do apps/connectors alcança o app pelo nginx
- **WHEN** a SPA já carregada faz `GET /connectors/providers` (fetch, sem
  `Sec-Fetch-Mode: navigate`) contra o domínio público do stack
- **THEN** o nginx encaminha a requisição para `apps/connectors`, que responde pela
  própria rota (`401` sem token, JSON com token de operador válido). A requisição
  **não** cai no fallback de SPA nem recebe `200` com `text/html`; e um path que
  apenas começa com o literal sem o separador, como `/connectorsxyz`, **não** é
  encaminhado

#### Scenario: Navegação real em /connectors entrega o shell do SPA
- **WHEN** o browser faz uma navegação real (`Sec-Fetch-Mode: navigate`) em
  `/connectors` ou em um path abaixo dele
- **THEN** o nginx responde com `index.html` e `Cache-Control: no-store`, sem
  encaminhar a requisição para `apps/connectors`

#### Scenario: Push notification interna alcança apps/inbox pelo nginx
- **WHEN** `apps/workers` conclui uma task registrada com
  `pushNotificationConfig` cuja URL aponta para o domínio público do stack, em
  `/internal/push-notifications`
- **THEN** o nginx encaminha a requisição para `apps/inbox`, que a processa e
  entrega a resposta no canal de origem — a requisição **não** cai no fallback de
  SPA nem recebe `200` com HTML

#### Scenario: Resumo de mensagens recebidas alcança apps/inbox pelo nginx
- **WHEN** a SPA já carregada faz `GET /messages/summary?from=…&to=…` (fetch,
  sem `Sec-Fetch-Mode: navigate`) contra o domínio público do stack
- **THEN** o nginx encaminha a requisição para `apps/inbox`, que responde pela
  própria rota (`401` sem token, JSON com token válido). A requisição **não** cai
  no fallback de SPA nem recebe `200` com `text/html`

#### Scenario: Agregado de insights do sistema alcança apps/api pelo nginx
- **WHEN** a SPA já carregada faz `GET /insights/system?from=…&to=…` (fetch, sem
  `Sec-Fetch-Mode: navigate`) contra o domínio público do stack
- **THEN** o nginx encaminha a requisição para `apps/api`, que responde pela
  própria rota (`401` sem token, JSON com token válido). A requisição **não** cai
  no fallback de SPA nem recebe `200` com `text/html`

#### Scenario: Prefixo de insights cobre as rotas de escopo abaixo dele
- **WHEN** a SPA já carregada faz fetch em qualquer path sob `/insights/`, como
  `/insights/agents/{id}`
- **THEN** o nginx encaminha a requisição para `apps/api` pela mesma entrada de
  prefixo, sem exigir entrada nova por rota; um path que apenas **começa** com o
  literal sem o separador, como `/insightsxyz`, **não** é encaminhado e cai no
  fallback de SPA

#### Scenario: Rotas de 2 etapas alcançam apps/api pelo nginx
- **WHEN** a SPA já carregada faz fetch (sem `Sec-Fetch-Mode: navigate`) em
  `GET /account/two-factor` ou em `POST /users/{id}/two-factor/reset` contra o
  domínio público do stack
- **THEN** o nginx encaminha a requisição para `apps/api`, que responde pela
  própria rota (`401` sem token). A requisição **não** cai no fallback de SPA nem
  recebe `200` com `text/html`; e um path que apenas começa com o literal sem o
  separador, como `/accountxyz` ou `/usersxyz`, **não** é encaminhado

#### Scenario: Lista de aplicações externas alcança apps/api pelo nginx
- **WHEN** a SPA já carregada faz `GET /applications/` (fetch, sem
  `Sec-Fetch-Mode: navigate`) contra o domínio público do stack
- **THEN** o nginx encaminha a requisição para `apps/api`, que responde pela
  própria rota (`401` sem token, JSON com token de Admin). A requisição **não**
  cai no fallback de SPA nem recebe `200` com `text/html`; e um path que apenas
  começa com o literal sem o separador, como `/applicationsxyz`, **não** é
  encaminhado

#### Scenario: Navegação real em /applications entrega o shell do SPA
- **WHEN** o browser faz uma navegação real (`Sec-Fetch-Mode: navigate`) em
  `/applications` ou em um path abaixo dele, como `/applications/{id}`
- **THEN** o nginx responde com `index.html` e `Cache-Control: no-store`, sem
  encaminhar a requisição para `apps/api`

#### Scenario: Prefixo servido por um app e ausente do nginx reprova a suíte do app
- **WHEN** um app que o nginx roteia (`apps/api`, `apps/inbox` ou
  `apps/connectors`) serve uma rota cujo primeiro segmento não está em nenhum
  bloco de regex que encaminha para ele, e o segmento não é exceção nomeada
- **THEN** o teste de cobertura da suíte desse app reprova, nomeando o prefixo

#### Scenario: Prefixo do nginx que o app não serve reprova a suíte do app
- **WHEN** um bloco de regex que encaminha para um app lista um prefixo que
  nenhuma rota do host construído desse app tem como primeiro segmento
- **THEN** o teste de cobertura da suíte desse app reprova, nomeando o prefixo

#### Scenario: Exceção que deixou de valer reprova
- **WHEN** uma exceção nomeada no teste não corresponde mais a nada (a rota
  `/health` ou `/sync` saiu do app, ou o bloco exato de um endpoint do emissor
  saiu do nginx), ou um bloco exato que encaminha para o app não está entre as
  exceções nomeadas
- **THEN** o teste de cobertura reprova, nomeando a exceção ou o bloco

#### Scenario: Bloco de prefixo de API sem o desvio de navegação reprova
- **WHEN** um bloco de regex que encaminha para um app não tem o desvio de
  navegação (`Sec-Fetch-Mode: navigate` reescrito para `/index.html`)
- **THEN** o teste de cobertura da suíte desse app reprova, nomeando o bloco
