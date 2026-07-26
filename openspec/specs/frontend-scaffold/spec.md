# frontend-scaffold Specification

## Purpose

TBD - defined by change estrutura-base-monorepo. Update Purpose after archive.

## Requirements

### Requirement: Projeto Vite + React + TypeScript isolado
O sistema SHALL prover, em `apps/frontend`, um projeto Vite + React +
TypeScript isolado de `apps/api` e `apps/workers`, sem nenhuma dependência ou
import de código interno desses outros apps.

#### Scenario: Build isolado do frontend
- **WHEN** um desenvolvedor executa o comando de build do Vite a partir de `apps/frontend`
- **THEN** o build conclui com sucesso sem depender de nenhum código-fonte localizado em `apps/api` ou `apps/workers`

### Requirement: Mantine configurado com AppShell inicial
O sistema SHALL incluir, em `apps/frontend`, as bibliotecas Mantine `core`,
`hooks`, `form` e `notifications`, configuradas com os providers necessários
(`MantineProvider`, `Notifications`), e uma tela inicial usando o componente
`AppShell` no estilo de composição visual de https://ui.mantine.dev (navbar/
header estruturais).

#### Scenario: Aplicação renderiza o AppShell inicial
- **WHEN** a aplicação frontend é iniciada em modo de desenvolvimento e acessada no navegador
- **THEN** a página renderiza um `AppShell` do Mantine com header e navbar visíveis, sem erros no console

### Requirement: Lint e formatação configurados
O sistema SHALL incluir, em `apps/frontend`, configuração de ESLint e Prettier
aplicável ao código TypeScript/React do projeto.

#### Scenario: Lint executa sem erros no código inicial
- **WHEN** um desenvolvedor executa o script de lint do `apps/frontend`
- **THEN** o processo termina sem erros de lint no código gerado pelo scaffold

#### Scenario: Formatação é verificável
- **WHEN** um desenvolvedor executa o script de checagem de formatação do Prettier em `apps/frontend`
- **THEN** o processo confirma que todos os arquivos estão formatados de acordo com a configuração do projeto
