# connectors-scaffold Specification

## Purpose

O app `apps/connectors`: projeto e solução próprios, sem banco e sem referência a
outro app, imagem `buteco-connectors`, saúde anônima e classificada, CORS para o
painel e autenticação por token validado localmente com a chave compartilhada. Diz o
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

### Requirement: Token validado localmente com a chave compartilhada
`apps/connectors` SHALL validar o token `Bearer` de toda rota não anônima com a
mesma assinatura HMAC de `apps/api` e `apps/inbox` (`Auth:TokenSigningKey`), sem
chamada de rede. Token ausente, malformado, com assinatura inválida ou expirado
SHALL receber `401`. `Auth:TokenSigningKey` ausente ou vazia SHALL derrubar o boot.

#### Scenario: Sem token
- **WHEN** um cliente faz `GET /connectors/providers` sem `Authorization`
- **THEN** a resposta é `401`

#### Scenario: Token emitido pelo login de apps/api é aceito
- **WHEN** um token de operador assinado com a mesma `Auth:TokenSigningKey` é
  enviado a `GET /connectors/providers`
- **THEN** a autenticação passa e a rota responde `200`

#### Scenario: Chave de assinatura ausente derruba o boot
- **WHEN** o processo sobe sem `Auth__TokenSigningKey`
- **THEN** a inicialização falha nomeando a configuração ausente

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
- **THEN** o build completa, e o contêiner iniciado com
  `Auth__TokenSigningKey` responde `200` em `/health` na porta 8080
