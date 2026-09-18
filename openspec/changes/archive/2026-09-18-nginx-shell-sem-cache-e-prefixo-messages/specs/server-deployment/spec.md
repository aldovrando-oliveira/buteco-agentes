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
  `/knowledge-bases`, `/knowledge-index` ou `/auth`
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

#### Scenario: Prefixo servido por um app e ausente do nginx é detectado
- **WHEN** os prefixos de rota de primeiro nível servidos por `apps/api` e
  `apps/inbox` são enumerados e comparados com os blocos de roteamento do nginx
- **THEN** todo prefixo servido, exceto `/health`, corresponde a um bloco que o
  encaminha para o app que o serve

## ADDED Requirements

### Requirement: Shell do SPA não é armazenado em cache, e os assets continuam cacheáveis
O nginx do stack SHALL entregar o shell do SPA (`index.html`) com
`Cache-Control: no-store` em **todo** caminho que o entrega:
- a diretiva `index` (`/`) e o acesso direto a `/index.html`;
- o rewrite de navegação real em path que também é prefixo de API
  (`Sec-Fetch-Mode: navigate` em `/agents`, `/agents/{id}`, `/channels/{id}`
  etc.);
- o `try_files` de rota profunda do `react-router` que não colide com prefixo de
  API (ex. `/inventory`).

A razão: enquanto uma mesma URL puder devolver o shell ou a resposta de API
conforme o header da requisição, uma cópia do shell guardada sob essa URL é
servida pelo cache do browser aos `fetch()` seguintes da própria SPA, com HTML
no lugar de JSON e sem a requisição chegar ao servidor.

Os arquivos estáticos com hash de conteúdo no nome (`/assets/*`) e os demais
arquivos estáticos do build SHALL NOT receber esse header. O nginx SHALL NOT
alterar o comportamento de cache deles.

#### Scenario: Shell entregue pela rota raiz não é armazenado
- **WHEN** o browser navega para `/` ou `/index.html` no domínio público do stack
- **THEN** a resposta é o `index.html` com `Cache-Control: no-store`

#### Scenario: Shell entregue por navegação em prefixo de API não é armazenado
- **WHEN** o browser faz uma navegação real (`Sec-Fetch-Mode: navigate`) em
  `/agents`
- **THEN** a resposta é o `index.html` com `Cache-Control: no-store`

#### Scenario: Shell entregue por rota profunda do SPA não é armazenado
- **WHEN** o browser faz uma navegação real em `/inventory`, rota do
  `react-router` que não é prefixo de API
- **THEN** a resposta é o `index.html` com `Cache-Control: no-store`

Os três cenários de header acima são a verificação **discriminante** deste
requirement. Eles aprovam com o patch e reprovam sem ele em qualquer momento. O
cenário de efeito no browser, abaixo, só discrimina com **deploy envelhecido**.
O browser calcula a janela de reuso de uma cópia sem `Cache-Control` no momento
em que a grava: 10% de `Date − Last-Modified`, em que `Last-Modified` é a hora do
build. Logo depois de um deploy essa janela é praticamente zero. A cópia gravada
já nasce vencida, e o `fetch()` a revalida no servidor. Nesse momento o cenário
aprovaria igual **sem** o patch. Com 1 dia de deploy a janela é de ~2,4 h, e aí o
cenário discrimina.

#### Scenario: Fetch da SPA depois de refresh em rota compartilhada chega ao backend
- **WHEN** o shell em produção tem mais de 1 dia desde o build
  (`Date − Last-Modified` > 24 h), o operador faz refresh em `/agents` e a SPA,
  já carregada, faz `fetch('/agents')` dentro da mesma janela
- **THEN** a requisição chega ao `apps/api` e a resposta é a da API. Ela **não** é
  servida do cache de disco do browser com o `index.html`

#### Scenario: Assets com hash de conteúdo continuam cacheáveis
- **WHEN** o browser requisita um arquivo em `/assets/` ou outro arquivo
  estático do build (ex. `/favicon.svg`)
- **THEN** a resposta do nginx do stack **não** traz `Cache-Control: no-store`
