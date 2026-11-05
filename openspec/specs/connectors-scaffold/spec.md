# connectors-scaffold Specification

## Purpose

O app `apps/connectors`: projeto e solução próprios, sem banco e sem referência a
outro app, imagem `buteco-connectors`, saúde anônima e classificada, CORS para o
painel e autenticação pelo access token do emissor do `apps/api`, validado com as chaves
públicas dele. Diz o
que o app precisa para existir e subir; o que cada rota faz e quem pode chamá-la está
em `connectors-api`, e o contrato dos conectores em `connector-plugin`.
## Requirements
### Requirement: Solução .NET própria para apps/connectors, sem banco
O sistema SHALL prover `apps/connectors/Connectors.sln` com o projeto
`src/Buteco.Connectors` (ASP.NET Core Minimal API, .NET 10) e o projeto de teste
`tests/Buteco.Connectors.Tests`. Nenhum projeto de `apps/connectors` SHALL
referenciar projeto de outro app nem de `libs/`, e o app SHALL NOT ter banco de
dados nem connection string.

#### Scenario: Build isolado
- **WHEN** `dotnet build apps/connectors/Connectors.sln` é executado na raiz do
  monorepo
- **THEN** o build completa com sucesso, e nenhum `.csproj` de `apps/connectors`
  contém `ProjectReference` para fora de `apps/connectors`

#### Scenario: Sem banco
- **WHEN** a configuração e os pacotes de `Buteco.Connectors` são inspecionados
- **THEN** não há `ConnectionStrings`, `DbContext` nem pacote de EF Core no
  projeto de produção

### Requirement: Health check anônimo e classificado
`apps/connectors` SHALL expor `GET /health` sem token, marcado como anônimo com
`AnonymousRouteClassification` de motivo `HealthProbe`. A checagem de
classificação de rotas anônimas SHALL rodar no boot, depois de todos os `Map*`,
e derrubar o processo se uma rota anônima não tiver classificação ou se a
allowlist citar rota que não existe.

#### Scenario: Health responde sem token
- **WHEN** um cliente faz `GET /health` sem `Authorization`
- **THEN** a resposta é `200`

#### Scenario: Rota anônima sem classificação derruba o boot
- **WHEN** um endpoint é mapeado com `AllowAnonymous()` sem
  `AnonymousRouteClassification`
- **THEN** a inicialização falha com mensagem que nomeia a rota

### Requirement: CORS para o frontend
`apps/connectors` SHALL aceitar requisições de navegador apenas das origens em
`Cors:AllowedOrigins`, no mesmo formato de `apps/api` e `apps/inbox`.

#### Scenario: Origem configurada
- **WHEN** um preflight `OPTIONS` chega com `Origin` presente em
  `Cors:AllowedOrigins`
- **THEN** a resposta traz `Access-Control-Allow-Origin` com essa origem

#### Scenario: Origem não configurada
- **WHEN** um preflight `OPTIONS` chega com `Origin` ausente de
  `Cors:AllowedOrigins`
- **THEN** a resposta não traz `Access-Control-Allow-Origin`

### Requirement: Imagem buteco-connectors
O sistema SHALL prover `apps/connectors/Dockerfile` multi-stage, com build
context na raiz do monorepo, imagem final runtime ASP.NET sem SDK, usuário
non-root, porta 8080 e `HEALTHCHECK` contra `/health`.

#### Scenario: Build da imagem a partir da raiz
- **WHEN** `docker build -f apps/connectors/Dockerfile -t buteco-connectors .` é
  executado na raiz do monorepo
- **THEN** o build completa, e o contêiner iniciado com `Identity__Issuer`,
  `Api__BaseUrl` e `ServiceClient__Secret` responde `200` em `/health` na porta
  8080

### Requirement: A suíte de integração do apps/connectors autentica pelo token do emissor
A fábrica de integração de `apps/connectors/tests` SHALL subir o `apps/connectors`
sempre com a validação do token do emissor ligada contra um emissor de teste em
memória, com `Identity:Issuer` e `Api:BaseUrl` do emissor de teste por padrão, e
os casos SHALL autenticar com o access token do cliente que a tabela de clientes do
`apps/connectors` mapeia para o subject que provam. Quando o caso não pede o
`Api:BaseUrl`, a fábrica SHALL retirar do host de teste o agendamento do ciclo de
sincronização, sem opção nova de configuração de produção. Nenhum helper de teste
SHALL assinar token fora do emissor.

#### Scenario: Fábrica padrão aponta para o emissor de teste
- **WHEN** um caso usa a fábrica sem configuração própria
- **THEN** o `Api:BaseUrl` resolvido é o endereço interno do emissor de teste, e não
  o do `appsettings.Development.json`

#### Scenario: Fábrica padrão sem agendamento do ciclo
- **WHEN** um caso usa a fábrica sem pedir `Api:BaseUrl`
- **THEN** nenhum pedido chega ao `apps/api` falso nem ao `/connect/token` falso
  depois do boot

### Requirement: Token do emissor validado localmente
`apps/connectors` SHALL validar o token `Bearer` de toda rota não anônima como
access token do emissor do `apps/api` (capability `issuer-token-validation`), com
as chaves obtidas em segundo plano pelo `Api:BaseUrl`, sem chamada de rede no
caminho da requisição. Token ausente, malformado, com assinatura inválida ou
expirado SHALL receber `401`, com `WWW-Authenticate: Bearer`. `Identity:Issuer` ou
`Api:BaseUrl` ausentes ou vazios SHALL derrubar o boot.

#### Scenario: Sem token
- **WHEN** um cliente faz `GET /connectors/providers` sem `Authorization`
- **THEN** a resposta é `401`, com `WWW-Authenticate: Bearer`

#### Scenario: Token do painel é aceito
- **WHEN** um access token do emissor do cliente `buteco-painel`, com `aud`
  `buteco-connectors`, é enviado a `GET /connectors/providers`
- **THEN** a autenticação passa e a rota responde `200`

#### Scenario: Sem Identity:Issuer o boot falha
- **WHEN** o processo sobe sem `Identity__Issuer`
- **THEN** a inicialização falha nomeando a configuração ausente

