## Context

O monorepo hoje só tem a estrutura de planejamento (`openspec/`). Esta mudança
cria a fundação de código dos três apps definidos no contexto do projeto:
`apps/api` (ASP.NET Core, futuro host do `A2AServer`), `apps/workers` (.NET
Worker Service, futuro consumidor do RabbitMQ) e `apps/frontend` (React +
Vite + Mantine). A regra de isolamento estrito entre apps é um requisito de
arquitetura de longo prazo do projeto (compartilhamento real só via `libs/`
explícita), não apenas desta mudança — por isso o design trata isso como
restrição obrigatória, não como recomendação.

Não há nenhum código pré-existente para migrar; este é um scaffold greenfield.

## Goals / Non-Goals

**Goals:**
- Estabelecer a árvore de pastas e as soluções/projetos base dos três apps.
- Cada app deve buildar, rodar e testar de forma independente, sem qualquer
  referência de projeto ou import para os outros dois apps.
- `apps/api` expõe um health check mínimo.
- `apps/workers` sobe como Worker Service mínimo (sem lógica de negócio).
- `apps/frontend` renderiza um `AppShell` inicial do Mantine, com lint e
  formatação configurados.

**Non-Goals:**
- Nenhuma lógica de negócio: sem A2A server real, sem consumo de RabbitMQ, sem
  PostgreSQL/EF Core, sem MCP, sem LLM, sem adapters de canais (ChatWoot/Waha).
- Nenhum conteúdo em `libs/` (ver decisão abaixo).
- Nenhuma configuração de CI/CD, Docker ou orquestração de containers.
- Nenhuma autenticação/autorização.

## Decisions

### Árvore de pastas proposta

```
apps/
  api/
    Api.sln
    src/
      Buteco.Api/
        Buteco.Api.csproj
        Program.cs
        appsettings.json
        appsettings.Development.json
    tests/
      Buteco.Api.Tests/
        Buteco.Api.Tests.csproj
        HealthCheckTests.cs
  workers/
    Workers.sln
    src/
      Buteco.Workers/
        Buteco.Workers.csproj
        Program.cs
        Worker.cs
        appsettings.json
        appsettings.Development.json
    tests/
      Buteco.Workers.Tests/
        Buteco.Workers.Tests.csproj
        WorkerTests.cs
  frontend/
    package.json
    tsconfig.json
    vite.config.ts
    eslint.config.js
    .prettierrc.json
    .prettierignore
    index.html
    src/
      main.tsx
      App.tsx
      components/
        layout/
          AppShell.tsx
      theme.ts
      vite-env.d.ts
    public/
```

### Nomenclatura das soluções e projetos .NET
Usar `Api.sln` / `Workers.sln` na raiz de cada app (como pedido), com projetos
internos prefixados `Buteco.*` (`Buteco.Api`, `Buteco.Api.Tests`,
`Buteco.Workers`, `Buteco.Workers.Tests`) para manter consistência de
namespace e facilitar a futura adição de mais projetos dentro de `src/` sem
ambiguidade de nomes.
**Alternativa considerada**: nomear os projetos apenas `Api`/`Workers`. Rejeitada
por colidir facilmente com nomes genéricos de namespace e dificultar distinguir
os assemblies em logs/pacotes mais adiante.

### .NET 10 com Worker Service template padrão
Todos os projetos .NET desta mudança (`Buteco.Api`, `Buteco.Workers` e seus
projetos de teste) têm `TargetFramework` fixado em `net10.0`. `apps/workers`
usa o template `worker` do .NET (`Microsoft.Extensions.Hosting`), que já
fornece `BackgroundService` + `Generic Host`, alinhado ao que o Microsoft
Agent Framework espera para executar agentes em background.
**Alternativa considerada**: um `Console App` com loop manual. Rejeitada por
reimplementar o que o Generic Host já resolve (DI, configuração, lifetime,
logging) e que será necessário quando o consumo do RabbitMQ for adicionado.

### Health check via `Microsoft.Extensions.Diagnostics.HealthChecks`
Usar o middleware de health checks nativo do ASP.NET Core
(`AddHealthChecks()` / `MapHealthChecks("/health")`) em vez de um endpoint
manual, porque é o mecanismo padrão que o `A2AServer` e infraestrutura de
orquestração (probes de liveness/readiness) vão consumir depois.

### Frontend: Vite + React + TS + Mantine 9, ESLint flat config + Prettier
- Vite como bundler/dev server (já definido pelo pedido).
- Mantine v9 (última major estável), pacotes `@mantine/core`, `@mantine/hooks`,
  `@mantine/form`, `@mantine/notifications`, com `@mantine/core/styles.css`
  importado uma vez em `main.tsx` e `ColorSchemeScript` no `index.html`.
- React >=19.2 como requisito explícito: é a versão mínima exigida pelo
  Mantine v9 (que depende das APIs mais recentes de `react`/`react-dom`), e
  deve ser conferida manualmente após o scaffold do Vite, já que templates do
  `create vite` podem gerar uma versão de React mais antiga que essa mínima.
- `AppShell` inicial em `src/components/layout/AppShell.tsx`, seguindo o
  padrão de composição do https://ui.mantine.dev: `AppShell.Header` +
  `AppShell.Navbar` + `AppShell.Main`, com `useDisclosure` do `@mantine/hooks`
  para o toggle do navbar em mobile.
- ESLint usando flat config (`eslint.config.js`) com
  `typescript-eslint`, `eslint-plugin-react-hooks` e `eslint-plugin-react-refresh`
  — é o padrão atual gerado pelo template oficial `vite` + `react-ts` e evita
  configurar o formato legado (`.eslintrc`).
- Prettier como formatter separado do lint (regra comum: ESLint cuida de
  qualidade de código, Prettier cuida só de formatação), com
  `eslint-config-prettier` para desativar regras de estilo conflitantes no
  ESLint.

### Isolamento entre apps
Nenhum mecanismo de build compartilhado (sem `ProjectReference` cruzada entre
soluções .NET, sem o frontend importar de `apps/api` ou `apps/workers`). Cada
app tem seu próprio `package.json` / `.sln`, dependências e scripts de
build/test/lint independentes. Isso é validado manualmente nesta mudança
(revisão dos `.csproj` e `package.json`); um lint/CI que imponha isso
automaticamente fica fora do escopo (Non-Goal).

### Nada em `libs/`
Não há necessidade real de código compartilhado ainda: os três apps não têm
nenhum contrato ou modelo em comum implementado nesta mudança (sem A2A, sem
mensageria, sem persistência). Criar uma lib agora seria especulativo e
violaria a diretriz do projeto de que `libs/` deve ser explícita, pequena e
justificada por necessidade real — não um padrão default. Quando o A2A
server e o consumo de RabbitMQ forem implementados (mudanças futuras), pode
fazer sentido extrair contratos compartilhados entre `apps/api` e
`apps/workers` (ex.: DTOs de mensagens da fila); essa decisão deve ser tomada
naquele momento, com a necessidade concreta em mãos.

## Risks / Trade-offs

- [Duplicação de configuração entre `apps/api` e `apps/workers` (ex.: padrões
  de logging, versão do SDK .NET)] → Aceito por ora; isolamento total é o
  requisito mais importante nesta fase. Se a duplicação virar dor real, tratar
  como possível candidato a `libs/` no futuro, com justificativa própria.
- [Mantine v9 tem breaking changes conhecidos em relação a versões anteriores
  (ex.: remoção dos schema resolvers de terceiros — Zod/Yup/Joi — do
  `@mantine/form`, que passa a exigir validação manual ou uma integração
  própria) e exige React >=19.2] → Mitigado por partir direto do zero com v9,
  fixar as versões dos pacotes explicitamente (ver tasks.md 3.2) e seguir a
  documentação/exemplos atuais de https://ui.mantine.dev, que já refletem v9;
  se validação de formulário baseada em schema for necessária depois, tratar
  como decisão explícita em mudança futura.
- [Nome dos assemblies `Buteco.*` pode não ser o nome final do produto] →
  Baixo custo de renomear depois via find-and-replace, já que não há nenhuma
  lógica de negócio construída em cima ainda.

## Migration Plan

Não aplicável — não existe código ou ambiente em produção para migrar. A
"implantação" desta mudança é a criação dos arquivos e a verificação local de
que cada app builda, roda e testa isoladamente (ver tasks.md).

## Open Questions

- Gerenciador de pacotes do frontend (`npm`, `pnpm` ou `yarn`) — assumido
  `npm` por ser o default do `create vite` e não exigir tooling adicional;
  pode ser revisitado quando `libs/` de frontend existir e workspaces forem
  necessários.
