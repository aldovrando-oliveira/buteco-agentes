## ADDED Requirements

### Requirement: Solução .NET própria para apps/api
O sistema SHALL prover, em `apps/api`, uma solução .NET própria (`Api.sln`) com
pastas `src/` e `tests/`, isolada de `apps/workers` e `apps/frontend`, sem
nenhuma project reference para projetos desses outros apps.

#### Scenario: Build isolado da solução da API
- **WHEN** um desenvolvedor executa `dotnet build` a partir de `apps/api/Api.sln`
- **THEN** a solução compila com sucesso sem depender de nenhum projeto localizado em `apps/workers` ou `apps/frontend`

#### Scenario: Nenhuma referência cruzada de projeto
- **WHEN** os arquivos `.csproj` dentro de `apps/api/src` e `apps/api/tests` são inspecionados
- **THEN** nenhum deles contém `ProjectReference` apontando para caminhos fora de `apps/api`

### Requirement: Web API mínima com health check
O sistema SHALL expor, em `apps/api`, um projeto ASP.NET Core Web API mínimo
que responde a um endpoint de health check, servindo como base para receber o
`A2AServer` (Microsoft.Agents.AI.Hosting.A2A) em uma mudança futura.

#### Scenario: Health check responde OK
- **WHEN** a aplicação Web API de `apps/api` está em execução e uma requisição HTTP GET é feita ao endpoint de health check
- **THEN** a resposta retorna HTTP 200 com um corpo indicando o status saudável da aplicação

### Requirement: Testes automatizados para apps/api
O sistema SHALL incluir, em `apps/api/tests`, um projeto de testes automatizados
capaz de validar ao menos o endpoint de health check.

#### Scenario: Suíte de testes da API executa com sucesso
- **WHEN** um desenvolvedor executa `dotnet test` a partir de `apps/api/Api.sln`
- **THEN** o teste do endpoint de health check é executado e passa
