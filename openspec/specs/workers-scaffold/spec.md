# workers-scaffold Specification

## Purpose

TBD - defined by change estrutura-base-monorepo. Update Purpose after archive.

## Requirements

### Requirement: Solução .NET própria para apps/workers
O sistema SHALL prover, em `apps/workers`, uma solução .NET própria
(`Workers.sln`) com pastas `src/` e `tests/`, isolada de `apps/api` e
`apps/frontend`, sem nenhuma project reference para projetos desses outros
apps.

#### Scenario: Build isolado da solução dos workers
- **WHEN** um desenvolvedor executa `dotnet build` a partir de `apps/workers/Workers.sln`
- **THEN** a solução compila com sucesso sem depender de nenhum projeto localizado em `apps/api` ou `apps/frontend`

#### Scenario: Nenhuma referência cruzada de projeto
- **WHEN** os arquivos `.csproj` dentro de `apps/workers/src` e `apps/workers/tests` são inspecionados
- **THEN** nenhum deles contém `ProjectReference` apontando para caminhos fora de `apps/workers`

### Requirement: Worker Service mínimo
O sistema SHALL expor, em `apps/workers`, um projeto .NET Worker Service
mínimo, executável como serviço em background, servindo como base para
consumir o RabbitMQ e executar agentes via Microsoft Agent Framework em uma
mudança futura.

#### Scenario: Worker inicia e permanece em execução
- **WHEN** o Worker Service de `apps/workers` é iniciado via `dotnet run`
- **THEN** o processo sobe sem erros e permanece em execução até ser encerrado, registrando log de início do host

### Requirement: Testes automatizados para apps/workers
O sistema SHALL incluir, em `apps/workers/tests`, um projeto de testes
automatizados capaz de validar ao menos a inicialização do host do worker.

#### Scenario: Suíte de testes dos workers executa com sucesso
- **WHEN** um desenvolvedor executa `dotnet test` a partir de `apps/workers/Workers.sln`
- **THEN** o teste de inicialização do host é executado e passa
