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

O hash e o e-mail do bootstrap do primeiro Admin (`Auth__OperatorPasswordHash` e
`Auth__BootstrapAdminEmail` do `apps/api`) SHALL ser **opcionais** na
interpolação: eles só são lidos quando não há nenhum usuário, e a orientação do
`docs/deployment.md` é tirá-los do `.env.prod` depois do primeiro login do Admin.
Ausentes numa instalação nova, o `apps/api` sobe e registra o aviso do bootstrap.
O compose SHALL NOT passar `Auth__OperatorUsername` ao `apps/api`.

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

#### Scenario: Stack sobe sem as variáveis do bootstrap
- **WHEN** o stack é iniciado com o arquivo de ambiente completo, exceto
  `AUTH_OPERATOR_PASSWORD_HASH` e `AUTH_BOOTSTRAP_ADMIN_EMAIL`, ausentes
- **THEN** o Compose processa o arquivo, o `apps/api` reporta saudável, e o
  ambiente do contêiner do `apps/api` não tem `Auth__OperatorUsername`

## ADDED Requirements

### Requirement: Shell do SPA entregue com CSP e Referrer-Policy
O nginx do stack SHALL entregar o shell do SPA com os cabeçalhos abaixo, com
`always`, em **todo** caminho que o entrega (os mesmos do requisito "Shell do SPA
não é armazenado em cache"), declarados no `server` e repetidos no
`location = /index.html`, porque `add_header` num nível suprime os do nível de
cima:

- `Content-Security-Policy: default-src 'self'; script-src 'self' 'sha256-<hash>'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'`,
  em que `<hash>` é o SHA-256, em base64, do script inline do `index.html`;
- `Referrer-Policy: no-referrer`.

O painel em produção SHALL carregar sem violação da CSP no console do navegador.

#### Scenario: /entrar traz a CSP e o Referrer-Policy
- **WHEN** o browser navega para `/entrar` no domínio público do stack
- **THEN** a resposta é o `index.html` com `Cache-Control: no-store`, a
  `Content-Security-Policy` acima e `Referrer-Policy: no-referrer`

#### Scenario: /entrar/retorno com código traz os mesmos cabeçalhos
- **WHEN** o browser navega para `/entrar/retorno?code=x&state=y`
- **THEN** a resposta é o `index.html` com os mesmos três cabeçalhos

#### Scenario: Navegação em prefixo de API traz os mesmos cabeçalhos
- **WHEN** o browser faz uma navegação real (`Sec-Fetch-Mode: navigate`) em `/agents`
- **THEN** a resposta é o `index.html` com os mesmos três cabeçalhos

#### Scenario: O hash da CSP é o do script do index.html
- **WHEN** o script inline do `index.html` muda e o `nginx.conf` não
- **THEN** o teste que compara o hash do script com o da CSP do `nginx.conf` reprova
