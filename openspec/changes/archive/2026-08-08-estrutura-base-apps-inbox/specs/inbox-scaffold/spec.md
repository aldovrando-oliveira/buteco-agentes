## ADDED Requirements

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
