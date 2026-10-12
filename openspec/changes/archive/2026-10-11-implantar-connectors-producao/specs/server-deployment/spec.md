## MODIFIED Requirements

### Requirement: Compose de servidor completo, sem tocar o compose de dev
O sistema SHALL prover `docker-compose.prod.yml` na raiz do monorepo,
subindo os cinco apps containerizados (`apps/api`, `apps/workers`,
`apps/inbox`, `apps/connectors` e `apps/frontend`), o serviço de migration, e
Postgres/RabbitMQ próprios do stack de servidor (sem porta publicada no
host — diferente do compose de dev), sem nenhuma alteração de
comportamento no `docker-compose.yml` existente (infra de desenvolvimento
local).

O compose SHALL injetar em cada serviço **toda** a configuração que o processo
correspondente exige para operar, não apenas a que ele exige para iniciar.
Configuração cuja ausência permite o boot e falha no primeiro uso SHALL ser
tratada como lacuna do compose.

Variável de ambiente cuja ausência produziria configuração insegura ou silenciosa
— credencial de banco, de broker, chave de assinatura ou de criptografia — SHALL
ser declarada como **obrigatória na interpolação**, de modo que o Compose falhe ao
processar o arquivo, com mensagem que instrui como corrigir, em vez de substituir
por string vazia.

A credencial da service account do Google (`GoogleDrive__ServiceAccountKeyBase64`
do `apps/connectors`) SHALL ser **opcional** na interpolação: ausente, o
`apps/connectors` sobe sem o provedor `google-drive`, e nenhum outro serviço muda
de comportamento.

O `apps/connectors` SHALL subir somente depois de o `apps/api` estar saudável. O
nginx do stack SHALL subir depois de `apps/api` e `apps/inbox` saudáveis, e SHALL
NOT depender do `apps/connectors`: uma falha de boot do `apps/connectors` afeta só
o prefixo `/connectors`, nunca o painel nem as rotas dos outros apps.

#### Scenario: docker-compose.yml de desenvolvimento continua funcionando
- **WHEN** um desenvolvedor executa `docker compose up` (arquivo
  `docker-compose.yml`, sem o sufixo `.prod`) após esta change
- **THEN** o comportamento é idêntico ao anterior à change — sobe
  Postgres, RabbitMQ e WAHA de desenvolvimento, sem qualquer um dos
  cinco apps

#### Scenario: docker-compose.prod.yml sobe o stack completo
- **WHEN** `docker compose -f docker-compose.prod.yml up -d` é executado
  numa VM do zero, sem bancos pré-existentes, com as variáveis de
  ambiente obrigatórias configuradas
- **THEN** os oito serviços (postgres + rabbitmq + migration + 5 apps)
  sobem com sucesso, e `apps/api`/`apps/inbox`/`apps/connectors` reportam
  saudáveis em seus respectivos endpoints `/health`

#### Scenario: Stack sobe sem a credencial do Google
- **WHEN** o stack é iniciado com o arquivo de ambiente completo, exceto a
  credencial da service account do Google, ausente ou vazia
- **THEN** o Compose processa o arquivo, o `apps/connectors` reporta saudável, e
  `GET /connectors/providers` com token de operador responde `200` com lista vazia

#### Scenario: apps/connectors sobe depois do apps/api
- **WHEN** o stack é iniciado do zero
- **THEN** o contêiner do `apps/connectors` só é iniciado depois de o `apps/api`
  reportar saudável, e o do nginx do stack só depois de `apps/api` e `apps/inbox`
  reportarem saudáveis, sem esperar o `apps/connectors`

#### Scenario: Painel funciona com o apps/connectors fora do ar
- **WHEN** o `apps/connectors` está fora do ar ou não consegue subir (por exemplo,
  com a credencial do Google presente e inválida)
- **THEN** o painel carrega, as rotas do `apps/api` e do `apps/inbox` respondem
  pelo nginx do stack, e o prefixo `/connectors` responde `502` com a página de
  erro do próprio nginx, como os prefixos do `apps/api` e do `apps/inbox` com o
  app deles fora do ar — nunca `200 text/html` do fallback de SPA

#### Scenario: Indexação de conhecimento funciona no primeiro uso
- **WHEN** o primeiro documento é enviado para indexação num stack de servidor
  recém-provisionado
- **THEN** `apps/workers` indexa com o provedor, modelo e dimensão de embedding
  configurados — a indexação **não** falha com modelo vazio ou dimensão zero

#### Scenario: Provedor de LLM configurado no ambiente chega aos dois processos
- **WHEN** uma credencial de provedor não-OpenAI é definida no arquivo de
  ambiente do host
- **THEN** o provedor aparece em `GET /providers` de `apps/api` **e** é utilizável
  por `apps/workers` na execução de uma task

#### Scenario: Ausência de segredo obrigatório falha o processamento do compose
- **WHEN** o stack é iniciado sem o arquivo de ambiente que define as credenciais
  de Postgres, RabbitMQ e autenticação
- **THEN** o Compose falha ao processar o arquivo, identificando qual variável
  falta e como fornecê-la — nenhum serviço é criado, e o Postgres **não** sobe com
  credencial vazia

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

Rotas de sondagem de saúde (`/health`) SHALL ser exceção explícita — os
healthchecks executam dentro de cada container contra o próprio processo e não
atravessam o nginx.

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
  `/knowledge-bases`, `/knowledge-index`, `/insights` ou `/auth`
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

#### Scenario: Prefixo servido por um app e ausente do nginx é detectado
- **WHEN** os prefixos de rota de primeiro nível servidos por `apps/api`,
  `apps/inbox` e `apps/connectors` são enumerados e comparados com os blocos de
  roteamento do nginx
- **THEN** todo prefixo servido, exceto `/health`, corresponde a um bloco que o
  encaminha para o app que o serve

### Requirement: Segredos compartilhados vêm de fonte única
O sistema SHALL garantir que segredos que precisam ser byte-idênticos
entre processos (`Auth__TokenSigningKey` entre `apps/api`, `apps/inbox` e
`apps/connectors`; `Mcp__CredentialEncryptionKey` entre
`apps/api`/`apps/workers`) sejam declarados uma única vez num arquivo de
ambiente do host, e injetados nos serviços correspondentes via interpolação de
variável do `docker-compose.prod.yml`, nunca copiados manualmente em blocos
`environment:` distintos.

#### Scenario: Mesma chave de assinatura é usada por apps/api e apps/inbox
- **WHEN** um token de operador é emitido por `apps/api` e validado por
  `apps/inbox`
- **THEN** a validação aceita o token, confirmando que ambos os
  processos carregaram o mesmo valor de `Auth__TokenSigningKey` a partir
  da mesma variável de ambiente do host

#### Scenario: Token de operador emitido pelo apps/api vale no apps/connectors
- **WHEN** um token de operador emitido no login de `apps/api` é usado em
  `GET /connectors/providers` pelo nginx do stack
- **THEN** `apps/connectors` aceita o token e responde `200`

#### Scenario: Tokens de serviço entre apps/api e apps/connectors são aceitos nos dois sentidos
- **WHEN** `apps/api` assina `service:api` para descrever uma pasta no
  `apps/connectors` durante o cadastro de base sincronizada, e `apps/connectors`
  assina `service:connectors` para escrever no `apps/api` pelas rotas de `/sync`
- **THEN** nenhum dos dois lados responde `401`: o cadastro responde um resultado
  do conector (e não `502 connectors-error`), e o ciclo grava os documentos

## ADDED Requirements

### Requirement: Serviços com coordenação em memória rodam em réplica única
O `docker-compose.prod.yml` SHALL declarar uma única instância de `apps/workers`,
`apps/inbox` e `apps/connectors`, sem `deploy.replicas` maior que 1, e o
compose e o runbook de deploy SHALL nomear os três como serviços que nunca usam
`replicas > 1` enquanto a coordenação entre instâncias não for distribuída. Para
o `apps/connectors`, o motivo é o lock em memória que impede dois ciclos de
sincronização da mesma base.

#### Scenario: apps/connectors sobe com uma instância
- **WHEN** o stack de servidor é iniciado
- **THEN** existe exatamente um contêiner do serviço `connectors`

#### Scenario: Runbook lista o apps/connectors entre os serviços de réplica única
- **WHEN** o runbook de deploy é consultado sobre réplicas
- **THEN** ele lista `apps/workers`, `apps/inbox` e `apps/connectors` como serviços
  que nunca usam `replicas > 1`, com o motivo de cada um

### Requirement: apps/api e apps/connectors se alcançam pela rede interna do compose
O `docker-compose.prod.yml` SHALL entregar ao `apps/api` o endereço do
`apps/connectors` (`Connectors__BaseUrl`) e ao `apps/connectors` o endereço do
`apps/api` (`Api__BaseUrl`), ambos pelo nome de serviço e pela porta interna na
rede do compose, fixos no bloco `environment:`, sem passar pelo nginx nem pelo
domínio público e sem entrada no arquivo de ambiente do host. No stack de
servidor os dois endereços SHALL estar sempre preenchidos.

#### Scenario: Cadastro de base sincronizada alcança o apps/connectors
- **WHEN** o operador cadastra uma base sincronizada num stack de servidor sem a
  credencial do Google
- **THEN** o `apps/api` responde com o código vindo do `apps/connectors`
  (`provider-not-configured`), e **não** com `connectors-not-configured` nem com
  `connectors-unavailable`

#### Scenario: Ciclo de sincronização ligado em produção
- **WHEN** o `apps/connectors` do stack de servidor termina o boot
- **THEN** o log dele não traz o aviso de `Api:BaseUrl` não configurado, e o log
  do `apps/api` não traz o aviso de `Connectors:BaseUrl` não configurado

### Requirement: Endereço de build do apps/connectors entra junto com o prefixo do nginx
O build do `apps/frontend` no `docker-compose.prod.yml` SHALL receber
`VITE_CONNECTORS_BASE_URL` como string vazia (caminho relativo) somente quando a
configuração do nginx do stack roteia o prefixo `/connectors` para o
`apps/connectors`. O argumento SHALL NOT ser passado sem o bloco do nginx.

#### Scenario: Painel chama o apps/connectors no próprio domínio
- **WHEN** o frontend é construído pelo compose de servidor e o operador abre o
  cadastro de base
- **THEN** o painel chama `/connectors/providers` no mesmo domínio do painel, sem
  endereço de desenvolvimento no bundle, e a chamada chega ao `apps/connectors`
  pelo nginx

#### Scenario: Origem sincronizada habilitada com o provedor configurado
- **WHEN** o stack de servidor tem a credencial do Google configurada e o operador
  abre `/knowledge-bases/new`
- **THEN** a origem "Sincronizada" está habilitada e lista o provedor
  `google-drive` com o e-mail da conta de serviço
