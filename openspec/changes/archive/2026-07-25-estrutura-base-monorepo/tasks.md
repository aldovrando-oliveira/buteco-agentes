## 1. apps/api — scaffold da solução .NET

- [x] 1.1 [apps/api] Criar `apps/api/Api.sln` e a estrutura de pastas `src/` e `tests/`.
- [x] 1.2 [apps/api] Criar o projeto `src/Buteco.Api` (ASP.NET Core Web API mínima, `net10.0`) e adicioná-lo à `Api.sln`.
- [x] 1.3 [apps/api] Configurar `Program.cs` com `AddHealthChecks()` / `MapHealthChecks("/health")` e `appsettings.json` / `appsettings.Development.json` mínimos.
- [x] 1.4 [apps/api] Criar o projeto `tests/Buteco.Api.Tests` e adicioná-lo à `Api.sln`, com um teste (`HealthCheckTests`) que valida HTTP 200 no endpoint de health check.
- [x] 1.5 [apps/api] Rodar `dotnet build` e `dotnet test` a partir de `Api.sln` e confirmar que ambos passam.
- [x] 1.6 [apps/api] Conferir os `.csproj` de `src/` e `tests/` e confirmar ausência de qualquer `ProjectReference` para fora de `apps/api`.

## 2. apps/workers — scaffold da solução .NET

- [x] 2.1 [apps/workers] Criar `apps/workers/Workers.sln` e a estrutura de pastas `src/` e `tests/`.
- [x] 2.2 [apps/workers] Criar o projeto `src/Buteco.Workers` a partir do template `worker` (.NET, `net10.0`) e adicioná-lo à `Workers.sln`.
- [x] 2.3 [apps/workers] Ajustar `Worker.cs` / `Program.cs` para um `BackgroundService` mínimo que apenas loga o ciclo de vida (start/stop), sem lógica de RabbitMQ ainda.
- [x] 2.4 [apps/workers] Criar o projeto `tests/Buteco.Workers.Tests` e adicioná-lo à `Workers.sln`, com um teste (`WorkerTests`) que valida a inicialização do host.
- [x] 2.5 [apps/workers] Rodar `dotnet build` e `dotnet test` a partir de `Workers.sln` e confirmar que ambos passam.
- [x] 2.6 [apps/workers] Conferir os `.csproj` de `src/` e `tests/` e confirmar ausência de qualquer `ProjectReference` para fora de `apps/workers`.

## 3. apps/frontend — scaffold Vite + React + Mantine

- [x] 3.1 [apps/frontend] Gerar o projeto com `create vite` (template `react-ts`) em `apps/frontend`, depois conferir em `package.json` que `react` e `react-dom` resolvem para `>=19.2`; se o template gerar uma versão menor, atualizar manualmente as duas dependências (e seus `@types/*`) para `>=19.2` antes de prosseguir.
- [x] 3.2 [apps/frontend] Instalar `@mantine/core@^9`, `@mantine/hooks@^9`, `@mantine/form@^9`, `@mantine/notifications@^9` (versões fixadas, não instalar sem pin) e suas dependências (`postcss`, `postcss-preset-mantine`, `postcss-simple-vars`), configurando `postcss.config.cjs`.
- [x] 3.3 [apps/frontend] Configurar `MantineProvider` e `Notifications` em `src/main.tsx`, importando `@mantine/core/styles.css` e `@mantine/notifications/styles.css`, e adicionar `ColorSchemeScript` em `index.html`.
- [x] 3.4 [apps/frontend] Implementar `src/components/layout/AppShell.tsx` com `AppShell.Header` + `AppShell.Navbar` + `AppShell.Main` (toggle de navbar via `useDisclosure`), no estilo de https://ui.mantine.dev, e usá-lo em `src/App.tsx`.
- [x] 3.5 [apps/frontend] Configurar ESLint (`eslint.config.js`, flat config) com `typescript-eslint`, `eslint-plugin-react-hooks`, `eslint-plugin-react-refresh` e `eslint-config-prettier`.
- [x] 3.6 [apps/frontend] Configurar Prettier (`.prettierrc.json`, `.prettierignore`) e os scripts `lint`, `format`, `format:check` em `package.json`.
- [x] 3.7 [apps/frontend] Rodar `npm run build`, `npm run lint` e `npm run format:check` e confirmar que todos passam sem erros.
- [x] 3.8 [apps/frontend] Rodar `npm run dev` e verificar visualmente no navegador que o `AppShell` renderiza corretamente, sem erros no console.
- [x] 3.9 [apps/frontend] Conferir `package.json` e o código-fonte e confirmar ausência de qualquer dependência ou import apontando para `apps/api` ou `apps/workers`.

## 4. Verificação final de isolamento

- [x] 4.1 [monorepo] Confirmar que `apps/api`, `apps/workers` e `apps/frontend` buildam e testam de forma independente (cada comando executado isoladamente a partir da pasta do respectivo app).
- [x] 4.2 [monorepo] Confirmar que nenhum arquivo foi criado em `libs/` nesta mudança.
