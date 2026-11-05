# server-deployment Specification

## Purpose

O stack de servidor dos cinco apps: como cada um vira imagem, como o schema é
aplicado antes do primeiro boot e em cada redeploy, e como o conjunto sobe num
compose de produção separado do de desenvolvimento. Define também o nginx
interno, que é a única porta do stack para fora. Ele serve o SPA e decide, por
prefixo de path, se a requisição vai ao `apps/api`, ao `apps/inbox`, ao
`apps/connectors` ou ao shell do SPA. Essa decisão tem duas falhas silenciosas que a capability existe para
impedir: prefixo servido e ausente da lista, que devolve `200` com HTML onde se
esperava JSON; e shell do SPA guardado pelo browser sob uma URL que também é de
API. Fecha com o que o stack exige do ambiente: segredos compartilhados vindos
de fonte única, e TLS terminado fora dele. Não cobre o proxy externo nem a borda
(Cloudflare), que ficam fora do repositório.
## Requirements
### Requirement: Dockerfile por app, na pasta do próprio app, imagem final sem SDK nem código-fonte
O sistema SHALL prover um `Dockerfile` multi-stage para cada um dos cinco
apps, localizado dentro da pasta do próprio app —
`apps/api/Dockerfile`, `apps/workers/Dockerfile`, `apps/inbox/Dockerfile`,
`apps/connectors/Dockerfile`, `apps/frontend/Dockerfile` — nunca centralizado
numa pasta compartilhada.
O build context permanece a raiz do monorepo, produzindo uma imagem final
que roda como usuário non-root, sem o SDK do .NET/Node instalado e sem
código-fonte além do necessário para execução.

#### Scenario: Cada app tem seu próprio Dockerfile, na própria pasta
- **WHEN** a raiz do monorepo é inspecionada após esta change
- **THEN** existe exatamente um `Dockerfile` em cada uma de
  `apps/api/`, `apps/workers/`, `apps/inbox/`, `apps/connectors/` e
  `apps/frontend/`, e nenhum Dockerfile desses cinco apps existe fora da pasta
  do próprio app

#### Scenario: Build de apps/api a partir da raiz do monorepo
- **WHEN** `docker build -f apps/api/Dockerfile .` é executado a partir da
  raiz do monorepo
- **THEN** o build completa com sucesso, resolvendo
  `Directory.Build.props`, `Directory.Packages.props`, `global.json` e o
  `ProjectReference` para `libs/ProviderCatalog`

#### Scenario: Imagem final não contém SDK nem código-fonte
- **WHEN** a imagem final de `apps/api`, `apps/workers`, `apps/inbox` ou
  `apps/connectors` é inspecionada
- **THEN** o comando `dotnet build`/`dotnet ef` não está disponível na
  imagem, e nenhum arquivo `.cs`/`.csproj` de código-fonte está presente
  além dos artefatos publicados

#### Scenario: Container roda como usuário non-root
- **WHEN** qualquer um dos cinco containers é iniciado
- **THEN** o processo principal roda sob um usuário sem privilégio de
  root dentro do container

### Requirement: Imagem runtime .NET com tzdata e ICU disponíveis por padrão
O sistema SHALL usar, para as imagens finais de `apps/api`, `apps/inbox`
e `apps/workers`, uma variante de imagem runtime .NET que inclua a tz
database do sistema operacional e as bibliotecas ICU por padrão, sem
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` habilitado.

#### Scenario: TZ resolve para o valor declarado
- **WHEN** o container de `apps/workers` sobe com `TZ=America/Sao_Paulo`
- **THEN** a checagem de integridade de fuso horário no startup
  (`ValidateTimeZoneConfiguration`) passa, e o fuso resolvido é
  exatamente `America/Sao_Paulo`

#### Scenario: Nome do dia da semana em pt-BR renderiza corretamente
- **WHEN** o bloco de contexto temporal de `apps/workers` formata o dia
  da semana durante o processamento de uma task
- **THEN** o valor é renderizado em português (ex. "quarta-feira"), sem
  lançar `CultureNotFoundException`

### Requirement: Migration bundle one-shot, restrito a apps/api e apps/inbox
O sistema SHALL prover um serviço one-shot que gera e executa migration
bundles (`dotnet ef migrations bundle`) para os `AppDbContext` de
`apps/api` e `apps/inbox` antes desses dois apps subirem. Este serviço
NUNCA SHALL gerar ou executar bundle para o `AppDbContext` de
`apps/workers`.

#### Scenario: Migration bundle aplica schema antes do primeiro boot
- **WHEN** `docker-compose.prod.yml` sobe do zero, sem bancos
  pré-existentes
- **THEN** o serviço de migration completa com sucesso antes de
  `apps/api`/`apps/inbox` iniciarem (`depends_on:
  service_completed_successfully`), e os bancos `buteco_agents`/
  `buteco_inbox` têm o schema atualizado

#### Scenario: Nenhum bundle é gerado para apps/workers
- **WHEN** o serviço de migration é inspecionado
- **THEN** não existe nenhum artefato de migration bundle referenciando o
  `AppDbContext` de `apps/workers`

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
  `/knowledge-bases`, `/knowledge-index`, `/insights`, `/auth`, `/account` ou
  `/users`
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

#### Scenario: Prefixo servido por um app e ausente do nginx é detectado
- **WHEN** os prefixos de rota de primeiro nível servidos por `apps/api`,
  `apps/inbox` e `apps/connectors` são enumerados e comparados com os blocos de
  roteamento do nginx
- **THEN** todo prefixo servido, exceto `/health`, corresponde a um bloco que o
  encaminha para o app que o serve

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

### Requirement: Segredos compartilhados vêm de fonte única
O sistema SHALL garantir que segredos que precisam ser byte-idênticos
entre processos (`Mcp__CredentialEncryptionKey` entre
`apps/api`/`apps/workers`; o segredo de cada cliente de serviço entre o
`apps/api` e o serviço dele) sejam declarados uma única vez num arquivo de
ambiente do host, e injetados nos serviços correspondentes via interpolação de
variável do `docker-compose.prod.yml`, nunca copiados manualmente em blocos
`environment:` distintos. Nenhum segredo de assinatura de token SHALL ser
compartilhado entre processos: o `apps/api` assina todos os tokens com as chaves
do emissor, e os outros apps só conhecem as chaves públicas.

#### Scenario: Token do painel vale no apps/inbox e no apps/connectors
- **WHEN** um access token do painel, emitido pelo `apps/api`, é usado em
  `GET /channels` e em `GET /connectors/providers` pelo nginx do stack
- **THEN** o `apps/inbox` e o `apps/connectors` aceitam o token e respondem `200`,
  sem nenhum segredo de assinatura no ambiente deles

#### Scenario: Tokens de serviço entre apps/api e apps/connectors são aceitos nos dois sentidos
- **WHEN** `apps/api` gera no processo o token do `buteco-servico-api` para
  descrever uma pasta no `apps/connectors` durante o cadastro de base
  sincronizada, e `apps/connectors` obtém no emissor o token do
  `buteco-servico-connectors` para escrever no `apps/api` pelas rotas de `/sync`
- **THEN** nenhum dos dois lados responde `401`: o cadastro responde um resultado
  do conector (e não `502 connectors-error`), e o ciclo grava os documentos

#### Scenario: Compose recusa subir sem o domínio público
- **WHEN** `docker compose -f docker-compose.prod.yml --env-file .env.prod config` roda
  com `PUBLIC_DOMAIN` vazio
- **THEN** o compose falha citando `PUBLIC_DOMAIN`, antes de subir qualquer contêiner,
  e por isso o `apps/inbox` e o `apps/connectors` nunca sobem sem `Identity__Issuer`

### Requirement: apps/api não redireciona HTTPS internamente no stack
O sistema SHALL remover o middleware `UseHttpsRedirection` de `apps/api`,
já que TLS é responsabilidade de infraestrutura externa ao stack, e o
tráfego entre o nginx do stack e `apps/api` é sempre HTTP dentro da rede
do compose.

#### Scenario: Requisição via nginx interno não sofre redirect
- **WHEN** o nginx do stack encaminha uma requisição HTTP para
  `apps/api` dentro da rede do compose
- **THEN** `apps/api` responde diretamente, sem emitir redirect 307 para
  um scheme HTTPS

### Requirement: Nível de log de produção vive no repositório, em fonte única
O nível de log que cada app usa em produção SHALL estar declarado em arquivo
versionado do próprio app, e SHALL NOT depender de ajuste feito no ambiente do
servidor. Em produção, o log de comando de banco do EF Core
(`Microsoft.EntityFrameworkCore.Database.Command`) SHALL estar em `Warning` nos
apps que usam EF Core, e a categoria `Default` SHALL permanecer em
`Information` — subi-la esconderia as linhas de início dos serviços de
varredura, que são a única prova de que estão registrados. Havendo configuração
equivalente no ambiente do servidor, ela SHALL ser removida no deploy desta
mudança: duas fontes do mesmo valor divergem.

#### Scenario: App sobe em produção sem configuração de log no ambiente
- **WHEN** um app que usa EF Core sobe com o ambiente de hospedagem `Production`
  e nenhuma variável de log definida no ambiente
- **THEN** o log de comando de banco do EF Core não aparece, e as linhas de
  nível `Information` dos serviços do app continuam aparecendo

#### Scenario: Desenvolvimento não é afetado
- **WHEN** o mesmo app sobe no ambiente de desenvolvimento
- **THEN** o nível de log continua o de desenvolvimento, sem herdar o silêncio
  configurado para produção

### Requirement: Serviços de réplica única declaram o motivo de cada um
O `docker-compose.prod.yml` SHALL declarar uma única instância de `apps/workers`,
`apps/inbox` e `apps/connectors`, sem `deploy.replicas` maior que 1, e o
compose e o runbook de deploy SHALL nomear os três como serviços que nunca usam
`replicas > 1`, com o motivo de cada um:

- `apps/connectors`: o lock em memória que impede dois ciclos de sincronização da
  mesma base (#105) — restrição técnica de coordenação;
- `apps/workers`: decisão de disponibilidade, em aberto na #48. A capacidade é
  contada em slots (`réplicas × N`), e não é motivo para a réplica única;
- `apps/inbox`: decisão operacional, sem restrição técnica de coordenação, a rever
  na #165.

O compose e o runbook SHALL NOT dar como motivo, para `apps/workers` ou
`apps/inbox`, a falta de coordenação entre instâncias: `pg_advisory_lock` e `xmin`
vivem no Postgres compartilhado e funcionam entre instâncias.

#### Scenario: apps/connectors sobe com uma instância
- **WHEN** o stack de servidor é iniciado
- **THEN** existe exatamente um contêiner do serviço `connectors`

#### Scenario: Runbook lista o apps/connectors entre os serviços de réplica única
- **WHEN** o runbook de deploy é consultado sobre réplicas
- **THEN** ele lista `apps/workers`, `apps/inbox` e `apps/connectors` como serviços
  que nunca usam `replicas > 1`, com o motivo de cada um

#### Scenario: Motivo de cada serviço corresponde à sua restrição real
- **WHEN** o compose ou o runbook de deploy é consultado sobre o motivo da réplica
  única de cada serviço
- **THEN** o `apps/connectors` tem como motivo o lock em memória (#105), o
  `apps/workers` a decisão de disponibilidade da #48, e o `apps/inbox` a decisão
  operacional da #165, e nenhum texto afirma que `pg_advisory_lock`/`xmin` deixam
  de funcionar entre instâncias

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

### Requirement: Segredos dos clientes de serviço declarados um por serviço
O `docker-compose.prod.yml` SHALL passar ao serviço `api` os segredos `Identity__ServiceClients__Inbox__Secret` e `Identity__ServiceClients__Connectors__Secret`, interpolados das variáveis `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS` do arquivo de ambiente do host, cada uma com `:?`. Das mesmas variáveis, também com `:?`, o compose SHALL passar `ServiceClient__Secret` ao serviço `inbox`, com o valor de `SERVICE_CLIENT_SECRET_INBOX`, e ao serviço `connectors`, com o de `SERVICE_CLIENT_SECRET_CONNECTORS`. O `.env.prod.example` SHALL declarar as duas, com a instrução de gerar cada uma com `openssl rand -base64 32`, distintas entre si e de todas as outras chaves, e dizendo que cada uma vai ao `api` e ao serviço dela. Nenhum valor SHALL ser copiado em bloco `environment:` sem interpolação.

#### Scenario: Compose sem o segredo não sobe o apps/api
- **WHEN** o `docker compose --env-file .env.prod config` roda com um `.env.prod` sem `SERVICE_CLIENT_SECRET_INBOX`
- **THEN** o comando falha citando `SERVICE_CLIENT_SECRET_INBOX`

#### Scenario: Compose com os segredos entrega cada um ao apps/api e ao serviço dele
- **WHEN** o `docker compose --env-file .env.prod config` roda com as duas variáveis definidas
- **THEN** o serviço `api` recebe `Identity__ServiceClients__Inbox__Secret` e `Identity__ServiceClients__Connectors__Secret` com os valores do arquivo, o serviço `inbox` recebe `ServiceClient__Secret` com o valor de `SERVICE_CLIENT_SECRET_INBOX`, e o serviço `connectors` recebe `ServiceClient__Secret` com o valor de `SERVICE_CLIENT_SECRET_CONNECTORS`

