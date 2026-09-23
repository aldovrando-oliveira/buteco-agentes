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
consumir o RabbitMQ e executar agentes via Microsoft Agent Framework. Antes
de iniciar o processamento, o worker SHALL validar que o fuso horário
resolvido pelo sistema operacional corresponde exatamente ao valor
declarado na variável de ambiente `TZ`, recusando a inicialização caso não
corresponda (variável ausente, vazia, ou com valor que não resolve para o
fuso esperado) — sem permitir que o processo suba silenciosamente operando
em um fuso diferente do declarado.

#### Scenario: Worker inicia e permanece em execução
- **WHEN** o Worker Service de `apps/workers` é iniciado via `dotnet run`
- **THEN** o processo sobe sem erros e permanece em execução até ser encerrado, registrando log de início do host

#### Scenario: Worker não inicia sem TZ definida
- **WHEN** o processo de `apps/workers` é iniciado sem a variável de
  ambiente `TZ` definida
- **THEN** o processo falha a inicialização com um erro explícito, em vez
  de subir normalmente operando no fuso que o sistema operacional
  escolher por padrão

#### Scenario: Worker não inicia com TZ definida para um valor que não resolve corretamente
- **WHEN** o processo de `apps/workers` é iniciado com `TZ` definida para
  um valor que o sistema operacional não reconhece como um fuso horário
  válido, ou que resolve para um fuso diferente do declarado
- **THEN** o processo falha a inicialização com um erro explícito
  apontando o valor declarado e o fuso efetivamente resolvido, em vez de
  subir silenciosamente operando em um fuso não declarado

#### Scenario: Worker inicia normalmente com TZ corretamente configurada e registra o fuso resolvido
- **WHEN** o processo de `apps/workers` é iniciado com `TZ` definida para
  um fuso horário válido reconhecido pelo sistema operacional, cujo
  identificador resolvido corresponde exatamente ao valor declarado
- **THEN** o processo inicia normalmente e registra, no log de
  inicialização, o identificador do fuso resolvido e o offset atual

### Requirement: Testes automatizados para apps/workers
O sistema SHALL incluir, em `apps/workers/tests`, um projeto de testes
automatizados capaz de validar ao menos a inicialização do host do worker.

#### Scenario: Suíte de testes dos workers executa com sucesso
- **WHEN** um desenvolvedor executa `dotnet test` a partir de `apps/workers/Workers.sln`
- **THEN** o teste de inicialização do host é executado e passa

### Requirement: Checagem de inicialização bem-sucedida registra uma linha
Toda checagem de inicialização de `apps/workers` SHALL registrar uma linha de log
quando passar, e não apenas quando falhar, identificando o que foi
conferido e o valor conferido. Sucesso SHALL NOT ser indistinguível de a
checagem não ter rodado. Isso SHALL valer para a checagem do tamanho de lote de
embedding e para a de consistência do índice de embedding, cuja única
manifestação em log era a consulta que o EF Core imprimia — consulta que o nível
de log de produção silencia.

#### Scenario: Worker sobe com configuração de embedding válida
- **WHEN** o worker inicia com tamanho de lote e índice de embedding consistentes
  com a configuração
- **THEN** o log de inicialização tem uma linha por checagem, identificando o
  valor conferido, e o processo segue para o processamento

#### Scenario: Worker sobe em produção, com o log de comando do EF silenciado
- **WHEN** o worker inicia no ambiente de produção, onde o log de comando de
  banco do EF Core está em `Warning`
- **THEN** as linhas das checagens continuam aparecendo, sem depender da consulta
  que o EF Core imprimiria

#### Scenario: Checagem que falha continua falhando
- **WHEN** o worker inicia com configuração de embedding inconsistente
- **THEN** a inicialização falha com o erro explícito de hoje, sem ser mascarada
  pela linha de sucesso
