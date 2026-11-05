# inbox-scaffold Specification

## Purpose

TBD - defined by change estrutura-base-apps-inbox. Update Purpose after archive.
## Requirements
### Requirement: Solução .NET própria para apps/inbox
O sistema SHALL prover, em `apps/inbox`, uma solução .NET própria
(`Inbox.sln`) com pastas `src/` e `tests/`, isolada de `apps/api`,
`apps/workers` e `apps/frontend`, sem nenhuma project reference para
projetos desses outros apps.

#### Scenario: Build isolado da solução de inbox
- **WHEN** um desenvolvedor executa `dotnet build` a partir de `apps/inbox/Inbox.sln`
- **THEN** a solução compila com sucesso sem depender de nenhum projeto localizado em `apps/api`, `apps/workers` ou `apps/frontend`

#### Scenario: Nenhuma referência cruzada de projeto
- **WHEN** os arquivos `.csproj` dentro de `apps/inbox/src` e `apps/inbox/tests` são inspecionados
- **THEN** nenhum deles contém `ProjectReference` apontando para caminhos fora de `apps/inbox`

### Requirement: Web API mínima com health check
O sistema SHALL expor, em `apps/inbox`, um projeto ASP.NET Core Web API
mínimo que responde a um endpoint de health check, servindo como base para
receber o catálogo de canais, o orquestrador e os adapters de canal em
mudanças futuras.

#### Scenario: Health check responde OK
- **WHEN** a aplicação Web API de `apps/inbox` está em execução e uma requisição HTTP GET é feita ao endpoint de health check
- **THEN** a resposta retorna HTTP 200 com um corpo indicando o status saudável da aplicação

### Requirement: Testes automatizados para apps/inbox
O sistema SHALL incluir, em `apps/inbox/tests`, um projeto de testes
automatizados capaz de validar ao menos o endpoint de health check.

#### Scenario: Suíte de testes de inbox executa com sucesso
- **WHEN** um desenvolvedor executa `dotnet test` a partir de `apps/inbox/Inbox.sln`
- **THEN** o teste do endpoint de health check é executado e passa

### Requirement: A suíte de integração do apps/inbox autentica pelo token do emissor
As fixtures de integração de `apps/inbox/tests` SHALL subir o `apps/inbox` com a
validação do token do emissor ligada contra um emissor de teste em memória, SHALL
esperar a obtenção das chaves antes do primeiro cliente, e SHALL anexar por padrão
um access token do cliente `buteco-painel`. Todo host de teste do `apps/inbox`
SHALL subir com `Identity:Issuer` configurado, porque o app não sobe sem ele.
Nenhum helper de teste SHALL assinar token fora do emissor.

#### Scenario: Fixture padrão autentica pelo emissor
- **WHEN** um caso usa o cliente padrão de uma fixture de integração e chama
  `GET /channels`
- **THEN** a resposta é `200 OK`, sem esperar a primeira obtenção de chaves no
  próprio caso

