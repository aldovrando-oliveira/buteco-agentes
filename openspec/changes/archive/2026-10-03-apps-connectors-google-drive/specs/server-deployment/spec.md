## MODIFIED Requirements

### Requirement: Dockerfile por app, na pasta do próprio app, imagem final sem SDK nem código-fonte
O sistema SHALL prover um `Dockerfile` multi-stage para cada um dos cinco
apps, localizado dentro da pasta do próprio app —
`apps/api/Dockerfile`, `apps/workers/Dockerfile`, `apps/inbox/Dockerfile`,
`apps/connectors/Dockerfile`, `apps/frontend/Dockerfile` — nunca centralizado
numa pasta compartilhada.
O build context permanece a raiz do monorepo, produzindo uma imagem final
que roda como usuário non-root, sem o SDK do .NET/Node instalado e sem
código-fonte além do necessário para execução.

#### Scenario: Cada app tem seu próprio Dockerfile, na própria pasta
- **WHEN** a raiz do monorepo é inspecionada após esta change
- **THEN** existe exatamente um `Dockerfile` em cada uma de
  `apps/api/`, `apps/workers/`, `apps/inbox/`, `apps/connectors/` e
  `apps/frontend/`, e nenhum Dockerfile desses cinco apps existe fora da pasta
  do próprio app

#### Scenario: Build de apps/api a partir da raiz do monorepo
- **WHEN** `docker build -f apps/api/Dockerfile .` é executado a partir da
  raiz do monorepo
- **THEN** o build completa com sucesso, resolvendo
  `Directory.Build.props`, `Directory.Packages.props`, `global.json` e o
  `ProjectReference` para `libs/ProviderCatalog`

#### Scenario: Imagem final não contém SDK nem código-fonte
- **WHEN** a imagem final de `apps/api`, `apps/workers`, `apps/inbox` ou
  `apps/connectors` é inspecionada
- **THEN** o comando `dotnet build`/`dotnet ef` não está disponível na
  imagem, e nenhum arquivo `.cs`/`.csproj` de código-fonte está presente
  além dos artefatos publicados

#### Scenario: Container roda como usuário non-root
- **WHEN** qualquer um dos cinco containers é iniciado
- **THEN** o processo principal roda sob um usuário sem privilégio de
  root dentro do container
