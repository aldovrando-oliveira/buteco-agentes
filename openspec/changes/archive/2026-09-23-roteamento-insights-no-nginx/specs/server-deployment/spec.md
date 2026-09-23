## MODIFIED Requirements

### Requirement: Nginx do stack roteia por prefixo, sem expor porta pública diretamente
O sistema SHALL prover um container de nginx, parte do
`docker-compose.prod.yml`, que serve o build estático de `apps/frontend`
com fallback de SPA e roteia requisições por prefixo de path para
`apps/api` ou `apps/inbox` via nome de serviço na rede do compose. Este
container SHALL publicar apenas uma porta interna do host, nunca 80/443
diretamente.

O conjunto de prefixos roteados SHALL cobrir **todos** os prefixos de rota
servidos por `apps/api` e `apps/inbox`. Prefixo servido por um dos apps e ausente
da configuração do nginx SHALL ser tratado como defeito, não como omissão
deliberada: o fallback de SPA responde `200` com HTML, e o consumidor recebe
sucesso com conteúdo inválido em vez de erro.

Rotas de sondagem de saúde (`/health`) SHALL ser exceção explícita — os
healthchecks executam dentro de cada container contra o próprio processo e não
atravessam o nginx.

Acrescentar prefixo de rota de primeiro nível a `apps/api` ou a `apps/inbox`
SHALL incluir a entrada correspondente nesta configuração, ainda que a change que
cria a rota não toque nenhum outro arquivo de `apps/frontend`. A configuração mora
em `apps/frontend/deploy/`, e por isso conferência de escopo restrita aos arquivos
do app que serve a rota SHALL NOT ser tratada como suficiente para detectar a
omissão.

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
  `/knowledge-bases`, `/knowledge-index`, `/insights` ou `/auth`
- **THEN** ela é encaminhada para o container de `apps/api`; requisições
  no mesmo formato com path iniciando em `/channels`, `/contacts`,
  `/sessions`, `/messages`, `/webhooks` ou `/internal` são encaminhadas para o
  container de `apps/inbox`

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

#### Scenario: Prefixo servido por um app e ausente do nginx é detectado
- **WHEN** os prefixos de rota de primeiro nível servidos por `apps/api` e
  `apps/inbox` são enumerados e comparados com os blocos de roteamento do nginx
- **THEN** todo prefixo servido, exceto `/health`, corresponde a um bloco que o
  encaminha para o app que o serve
