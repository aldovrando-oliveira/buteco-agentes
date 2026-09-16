# server-deployment Specification

## Purpose

TBD - defined by change containerizacao-stack-servidor. Update Purpose after archive.

## Requirements

### Requirement: Dockerfile por app, na pasta do próprio app, imagem final sem SDK nem código-fonte
O sistema SHALL prover um `Dockerfile` multi-stage para cada um dos quatro
apps, localizado dentro da pasta do próprio app —
`apps/api/Dockerfile`, `apps/workers/Dockerfile`, `apps/inbox/Dockerfile`,
`apps/frontend/Dockerfile` — nunca centralizado numa pasta compartilhada.
O build context permanece a raiz do monorepo, produzindo uma imagem final
que roda como usuário non-root, sem o SDK do .NET/Node instalado e sem
código-fonte além do necessário para execução.

#### Scenario: Cada app tem seu próprio Dockerfile, na própria pasta
- **WHEN** a raiz do monorepo é inspecionada após esta change
- **THEN** existe exatamente um `Dockerfile` em cada uma de
  `apps/api/`, `apps/workers/`, `apps/inbox/` e `apps/frontend/`, e
  nenhum Dockerfile desses quatro apps existe fora da pasta do próprio app

#### Scenario: Build de apps/api a partir da raiz do monorepo
- **WHEN** `docker build -f apps/api/Dockerfile .` é executado a partir da
  raiz do monorepo
- **THEN** o build completa com sucesso, resolvendo
  `Directory.Build.props`, `Directory.Packages.props`, `global.json` e o
  `ProjectReference` para `libs/ProviderCatalog`

#### Scenario: Imagem final não contém SDK nem código-fonte
- **WHEN** a imagem final de `apps/api`, `apps/workers` ou `apps/inbox` é
  inspecionada
- **THEN** o comando `dotnet build`/`dotnet ef` não está disponível na
  imagem, e nenhum arquivo `.cs`/`.csproj` de código-fonte está presente
  além dos artefatos publicados

#### Scenario: Container roda como usuário non-root
- **WHEN** qualquer um dos quatro containers é iniciado
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
subindo os quatro apps containerizados, o serviço de migration, e
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

#### Scenario: docker-compose.yml de desenvolvimento continua funcionando
- **WHEN** um desenvolvedor executa `docker compose up` (arquivo
  `docker-compose.yml`, sem o sufixo `.prod`) após esta change
- **THEN** o comportamento é idêntico ao anterior à change — sobe
  Postgres, RabbitMQ e WAHA de desenvolvimento, sem qualquer um dos
  quatro apps

#### Scenario: docker-compose.prod.yml sobe o stack completo
- **WHEN** `docker compose -f docker-compose.prod.yml up -d` é executado
  numa VM do zero, sem bancos pré-existentes, com as variáveis de
  ambiente obrigatórias configuradas
- **THEN** os sete serviços (postgres + rabbitmq + migration + 4 apps)
  sobem com sucesso, e `apps/api`/`apps/inbox`/`apps/workers` reportam
  saudáveis em seus
  respectivos endpoints `/health`

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

### Requirement: Segredos compartilhados vêm de fonte única
O sistema SHALL garantir que segredos que precisam ser byte-idênticos
entre processos (`Auth__TokenSigningKey` entre `apps/api`/`apps/inbox`;
`Mcp__CredentialEncryptionKey` entre `apps/api`/`apps/workers`) sejam
declarados uma única vez num arquivo de ambiente do host, e injetados nos
serviços correspondentes via interpolação de variável do
`docker-compose.prod.yml`, nunca copiados manualmente em blocos
`environment:` distintos.

#### Scenario: Mesma chave de assinatura é usada por apps/api e apps/inbox
- **WHEN** um token de operador é emitido por `apps/api` e validado por
  `apps/inbox`
- **THEN** a validação aceita o token, confirmando que ambos os
  processos carregaram o mesmo valor de `Auth__TokenSigningKey` a partir
  da mesma variável de ambiente do host

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
