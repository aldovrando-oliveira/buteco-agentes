# Seleção esperada: caminho tocado → passos

A expectativa do `scripts/selecionar-testes.py`, **escrita à parte, a partir das regras** (design da change
`selecao-de-testes-por-diff`, D4) e não da saída do script. O `test_selecionar_testes.py` calcula o plano de cada
linha e confere que os passos são **exatamente** estes, além dos dois de todo plano (`check-docs` e
`scripts-testes`). Se o guarda e o código lessem a mesma fonte, o guarda não testaria a fonte (convenção 15, sexta
forma).

Ao mudar uma regra, mude esta tabela primeiro, à mão. Caminhos separados por ` + ` são tocados juntos. Um caminho
que não existe na árvore conta como apagado.

`TUDO` = `Buteco.Api.Tests`, `Buteco.Workers.Tests`, `Buteco.Inbox.Tests`, `Buteco.Connectors.Tests`,
`Buteco.ProviderCatalog.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests`,
`ApiConnectorsRoundTrip.Tests`, `fe-tsc`, `fe-lint`, `fe-completa`

`FE-FOLHA` = `fe-tsc`, `fe-lint`, `fe-related`, `fe-varreduras`

`FE-INTEIRO` = `fe-tsc`, `fe-lint`, `fe-completa`

| Caminho tocado | Passos esperados | Por quê |
|---|---|---|
| `docs/development.md` | — | linha 1 |
| `openspec/specs/api-scaffold/spec.md` | — | linha 1 |
| `.claude/commands/opsx/apply.md` | — | linha 1: nenhum teste lê `.claude/` |
| `.github/PULL_REQUEST_TEMPLATE.md` | — | linha 1 |
| `README.md` | — | linha 1: `.md` da raiz |
| `CHANGELOG.md` | — | linha 1 |
| `LICENSE` | — | linha 1 |
| `.gitignore` | — | linha 1: só o da raiz; nenhum teste o lê, e os testes do script, de todo plano, cobrem a lista de arquivos que ele muda |
| `apps/frontend/.gitignore` | FE-INTEIRO | linha 7: o `.gitignore` de dentro de um app não é o da raiz |
| `scripts/check-docs.py` | — | linha 8: os testes do script já estão em todo plano |
| `apps/api/src/Buteco.Api/Program.cs` | `Buteco.Api.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests`, `ApiConnectorsRoundTrip.Tests` | linha 2: o api e os três de `tests/` que o referenciam |
| `apps/api/tests/Buteco.Api.Tests/AgentEndpointsTests.cs` | `Buteco.Api.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests`, `ApiConnectorsRoundTrip.Tests` | linha 2: o app inteiro, inclusive o `tests/` dele |
| `apps/api/README.md` | `Buteco.Api.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests`, `ApiConnectorsRoundTrip.Tests` | linha 2: `.md` dentro de app não é documentação |
| `apps/api/src/Buteco.Api/Buteco.Api.csproj` | `Buteco.Api.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests`, `ApiConnectorsRoundTrip.Tests` | linha 2 + A1; o filtrado de versão some, porque o api roda inteiro |
| `apps/workers/src/Buteco.Workers/Program.cs` | `Buteco.Workers.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests` | linha 2 |
| `apps/workers/src/Buteco.Workers/Buteco.Workers.csproj` | `Buteco.Workers.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests`, `Buteco.Api.Tests[Versao]` | linha 2 + A1: a versão de todo `.csproj` é conferida por um teste do api |
| `apps/inbox/src/Buteco.Inbox/Program.cs` | `Buteco.Inbox.Tests`, `InboxOrchestratorRoundTrip.Tests` | linha 2 |
| `apps/inbox/tests/Buteco.Inbox.Tests/Buteco.Inbox.Tests.csproj` | `Buteco.Inbox.Tests`, `InboxOrchestratorRoundTrip.Tests`, `Buteco.Api.Tests[Versao]` | linha 2 + A1 |
| `apps/connectors/src/Buteco.Connectors/Program.cs` | `Buteco.Connectors.Tests`, `ApiConnectorsRoundTrip.Tests` | linha 2 |
| `apps/connectors/tests/Buteco.Connectors.Tests/Support/FakeConnector.cs` | `Buteco.Connectors.Tests`, `ApiConnectorsRoundTrip.Tests` | linha 2; o round-trip também compila o arquivo por `Compile Include` |
| `apps/connectors/tests/Buteco.Connectors.Tests/Fixtures/GoogleDrive/export-doc-com-imagem.md` | `Buteco.Connectors.Tests`, `ApiConnectorsRoundTrip.Tests` | linha 2: fixture `.md` lida por teste, não documentação |
| `tests/CrossAppTaskStoreCompatibility.Tests/PostgresTaskStoreCompatibilityTests.cs` | `CrossAppTaskStoreCompatibility.Tests` | linha 3 |
| `tests/InboxOrchestratorRoundTrip.Tests/Support/IssuerKeysGate.cs` | `InboxOrchestratorRoundTrip.Tests` | linha 3 |
| `tests/ApiConnectorsRoundTrip.Tests/Support/IssuerKeysGate.cs` | `ApiConnectorsRoundTrip.Tests` | linha 3 |
| `tests/CrossAppTaskStoreCompatibility.Tests/CrossAppTaskStoreCompatibility.Tests.csproj` | `CrossAppTaskStoreCompatibility.Tests`, `Buteco.Api.Tests[Versao]` | linha 3 + A1: o caso de `.csproj` que só o acréscimo alcança no api |
| `apps/frontend/deploy/nginx.conf` | `Buteco.Api.Tests[Nginx]`, `Buteco.Inbox.Tests[Nginx]`, `Buteco.Connectors.Tests[Nginx]`, `fe-nginx` | linha 4: o `b4fcf5d` (#285) |
| `apps/frontend/src/auth/session.ts` | FE-INTEIRO | linha 5 |
| `apps/frontend/src/app/routes.tsx` | FE-INTEIRO | linha 5 (cautela, não medida: design, D6) |
| `apps/frontend/src/theme.ts` | FE-INTEIRO | linha 5 |
| `apps/frontend/src/features/agents/components/ComponenteQueNaoExiste.tsx` | FE-INTEIRO | linha 5: arquivo apagado; o `related` não recebe caminho que não existe |
| `apps/frontend/src/features/agents/components/AgentTable.tsx` | FE-FOLHA | linha 6, com as varreduras de `src/` |
| `apps/frontend/src/utils/panelDates.ts` | FE-FOLHA | linha 6 |
| `apps/frontend/src/test/setup.ts` | FE-FOLHA | linha 6: o `related` já puxa tudo a partir dele |
| `apps/frontend/package.json` | FE-INTEIRO, `Buteco.Api.Tests[Versao]` | linha 7 + A2 |
| `apps/frontend/vite.config.ts` | FE-INTEIRO | linha 7 |
| `apps/frontend/index.html` | FE-INTEIRO | linha 7 (o `nginxHeaders.test.ts` lê o arquivo e está no Vitest inteiro) |
| `tests/Shared/NginxRouting.cs` | TUDO | não reconhecido (decisão do dono) |
| `libs/ProviderCatalog/ProviderCatalogEntry.cs` | TUDO | não reconhecido |
| `libs/ProviderCatalog/Buteco.ProviderCatalog.csproj` | TUDO | não reconhecido + A1; o filtrado some |
| `Directory.Build.props` | TUDO | não reconhecido |
| `Directory.Packages.props` | TUDO | não reconhecido |
| `global.json` | TUDO | não reconhecido |
| `apps/novo/src/Program.cs` | TUDO | não reconhecido: app novo |
| `deploy/migrate/Dockerfile` | TUDO | não reconhecido |
| `docker-compose.yml` | TUDO | não reconhecido |
| `.env.example` | TUDO | não reconhecido |
| `docs/development.md` + `apps/inbox/src/Buteco.Inbox/Program.cs` | `Buteco.Inbox.Tests`, `InboxOrchestratorRoundTrip.Tests` | a documentação não acrescenta nada |
| `apps/frontend/src/features/agents/components/AgentTable.tsx` + `apps/frontend/src/auth/session.ts` | FE-INTEIRO | o núcleo leva o Vitest inteiro, e o `related` e as varreduras saem |
| `apps/frontend/deploy/nginx.conf` + `apps/api/src/Buteco.Api/Program.cs` | `Buteco.Api.Tests`, `CrossAppTaskStoreCompatibility.Tests`, `InboxOrchestratorRoundTrip.Tests`, `ApiConnectorsRoundTrip.Tests`, `Buteco.Inbox.Tests[Nginx]`, `Buteco.Connectors.Tests[Nginx]`, `fe-nginx` | o filtrado do api some, porque o api roda inteiro |
| `apps/frontend/deploy/nginx.conf` + `apps/frontend/index.html` | `Buteco.Api.Tests[Nginx]`, `Buteco.Inbox.Tests[Nginx]`, `Buteco.Connectors.Tests[Nginx]`, FE-INTEIRO | o `fe-nginx` sai, porque o Vitest inteiro já o roda |
