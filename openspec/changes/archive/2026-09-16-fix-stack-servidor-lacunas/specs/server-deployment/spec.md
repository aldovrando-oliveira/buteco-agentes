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
  `/sessions`, `/webhooks` ou `/internal` são encaminhadas para o container de
  `apps/inbox`

#### Scenario: Push notification interna alcança apps/inbox pelo nginx
- **WHEN** `apps/workers` conclui uma task registrada com
  `pushNotificationConfig` cuja URL aponta para o domínio público do stack, em
  `/internal/push-notifications`
- **THEN** o nginx encaminha a requisição para `apps/inbox`, que a processa e
  entrega a resposta no canal de origem — a requisição **não** cai no fallback de
  SPA nem recebe `200` com HTML

#### Scenario: Prefixo servido por um app e ausente do nginx é detectado
- **WHEN** os prefixos de rota de primeiro nível servidos por `apps/api` e
  `apps/inbox` são enumerados e comparados com os blocos de roteamento do nginx
- **THEN** todo prefixo servido, exceto `/health`, corresponde a um bloco que o
  encaminha para o app que o serve

### Requirement: Compose de servidor completo, sem tocar o compose de dev
O sistema SHALL prover um `docker-compose.prod.yml` na raiz que sobe o stack
completo numa VM — Postgres, RabbitMQ, migrator one-shot, `apps/api`,
`apps/inbox`, `apps/workers` e o nginx que serve o SPA —, sem alterar o
`docker-compose.yml` de desenvolvimento.

O compose SHALL injetar em cada serviço **toda** a configuração que o processo
correspondente exige para operar, não apenas a que ele exige para iniciar.
Configuração cuja ausência permite o boot e falha no primeiro uso SHALL ser
tratada como lacuna do compose.

Variável de ambiente cuja ausência produziria configuração insegura ou silenciosa
— credencial de banco, de broker, chave de assinatura ou de criptografia — SHALL
ser declarada como **obrigatória na interpolação**, de modo que o Compose falhe ao
processar o arquivo, com mensagem que instrui como corrigir, em vez de substituir
por string vazia.

#### Scenario: Stack sobe com os dois bancos migrados antes dos apps
- **WHEN** o stack de servidor é iniciado do zero numa VM
- **THEN** `postgres` e `rabbitmq` ficam saudáveis, o `migrator` aplica as
  migrations dos dois bancos e termina, e só então `apps/api`, `apps/inbox` e
  `apps/workers` iniciam

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
