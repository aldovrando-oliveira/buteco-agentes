## Why

O repositório vai ser tratado como open-source, e hoje não é um: não há
`LICENSE` — sem ele todo direito fica reservado por padrão, mesmo com o
repositório público. Além disso, a documentação existente já apodreceu em
silêncio, e o estrago é verificável: o `README.md` (548 linhas, 27 KB) diz
"três apps" quando são quatro, afirma que `apps/frontend` "ainda não consome
o backend" (consome; 451 testes; oito etapas de redesenho concluídas) e lista
como "fora de escopo por enquanto" MCP, adapters de canal, autenticação,
push notification e UI — tudo entregue e arquivado. Cinco dos seis links para
`openspec/` no `README` estão quebrados, porque foram escritos durante a
change e não acompanharam o prefixo `archive/AAAA-MM-DD-` que o arquivamento
adiciona. O mesmo defeito está no `.env.example` e no `openspec/config.yaml`
— este último ainda declara "ChatWoot e Waha" como canais suportados, quando
o que existe é WAHA e Telegram, alimentando toda proposta futura com
informação errada.

Nada disso foi percebido porque **nada verifica documentação**. A convenção
nº 8 da casa (`01-ARQUITETURA_E_CONVENCOES.md`) já diz o remédio para
registros que ficam incompletos em silêncio: checagem de integridade, não
documentação em prosa. Esta change aplica essa mesma régua à própria
documentação.

## What Changes

**Governança open-source (novo, raiz, em inglês)**

- `LICENSE`: Apache-2.0, texto integral e não modificado.
- `NOTICE`: `Copyright 2026 Aldovrando Oliveira`. Sem headers de copyright
  nos arquivos-fonte — decisão explícita registrada em `design.md`.
- `SECURITY.md`: canal de reporte de vulnerabilidade. Relevante porque o
  sistema lida com credenciais AES-GCM, tokens de operador e de serviço.
- `CODE_OF_CONDUCT.md`: Contributor Covenant 2.1.
- `.github/ISSUE_TEMPLATE/` (bug + feature) e `.github/PULL_REQUEST_TEMPLATE.md`.

**Documentação técnica (novo, `docs/`, em pt-BR)**

- `docs/development.md`: pré-requisitos, subida da infra, subida dos quatro
  apps, testes por app, build das imagens, troubleshooting de Testcontainers
  com Podman. Extraído do `README.md` atual e corrigido.
- `docs/configuration.md`: inventário das 29 variáveis de ambiente, por
  processo, com origem (`.env.example` / `.env.prod.example`).
- `docs/deployment.md`: **MOVE** `deploy/runbook.md` para `docs/`, mantendo
  o conteúdo. Nenhuma spec viva referencia `deploy/runbook.md`.
- `docs/architecture.md`: os quatro apps, modelo de domínio, regras de
  negócio, protocolo A2A, contrato de plugin de canal. Curadoria pública
  derivada de `01-ARQUITETURA_E_CONVENCOES.md`.
- `docs/conventions.md`: premissas oficiais e o "estilo da casa" —
  isolamento entre apps, Central Package Management, degradação graciosa,
  checagem de integridade no startup, padrões de frontend, testes com
  infraestrutura real.
- `docs/README.md`: índice navegável de `docs/`.

**Porta de entrada e histórico**

- `README.md`: **reescrito** em inglês, de 548 para ~120 linhas. O quê, por
  quê, quickstart, links. Apresenta `01-ARQUITETURA_E_CONVENCOES.md` e
  `02-HISTORICO_E_STATUS.md` como notas internas do mantenedor — sem
  promovê-los a documentação oficial e sem escondê-los.
- `CHANGELOG.md`: **novo**, formato Keep a Changelog + SemVer. Só
  `[Unreleased]`, sem versão fechada, com o histórico das 55 changes
  arquivadas agrupado por linha de trabalho em `Added`/`Changed`/`Fixed`.
- `CONTRIBUTING.md`: fluxo OpenSpec (`/opsx:explore` → `propose` → `apply`
  → `sync` → `archive`), convenção de commits, requisitos de PR, como rodar
  os testes.

**Correções de conteúdo desatualizado**

- `openspec/config.yaml`: corrigir "três apps" → quatro, e "ChatWoot e Waha"
  → WAHA e Telegram.
- `apps/frontend/README.md`: **REMOVIDO** — é o template cru do Vite, nunca
  editado; `docs/development.md` cobre o assunto.
- Todos os links para `openspec/changes/` passam a usar o caminho de
  `archive/` quando a change está arquivada.

**Verificação (o que impede a regressão)**

- Script de checagem de integridade da documentação, rodável à mão: links
  relativos que não resolvem, links para `openspec/changes/<nome>/` sem
  `archive/`, app em `apps/` ausente de `docs/architecture.md`, e ausência
  de `[Unreleased]` no `CHANGELOG.md`.

**Não muda**

- `01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md`: intocados,
  no lugar onde estão. Servem de fonte para `docs/`, nunca de destino.

## Capabilities

### New Capabilities

- `repository-documentation`: estrutura da documentação do repositório —
  responsabilidade de cada documento por audiência, fronteira de idioma
  (raiz em inglês, `docs/` em pt-BR), e as invariantes verificáveis que
  impedem a documentação de divergir do código em silêncio.
- `open-source-governance`: licenciamento (Apache-2.0, `NOTICE`, política
  de headers), canal de reporte de segurança, código de conduta, guia de
  contribuição e templates de issue/PR.

### Modified Capabilities

Nenhuma. Nenhuma spec viva em `openspec/specs/` referencia `README.md` ou
`deploy/runbook.md` — verificado por busca. A movimentação do runbook para
`docs/deployment.md` não altera nenhum requisito de `server-deployment`, que
especifica o compose e as imagens, não a localização da documentação.

## Impact

- **Raiz do monorepo**: sete arquivos novos (`LICENSE`, `NOTICE`,
  `CHANGELOG.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`,
  mais `.github/`), um reescrito (`README.md`).
- **`docs/`**: seis arquivos novos; `docs/a2a-integration.md` permanece como
  está, apenas passa a ser referenciado pelo índice.
- **`deploy/`**: `runbook.md` sai da pasta; `deploy/migrate/` fica intacto.
- **`apps/frontend`**: remoção de `README.md`. Nenhum código, build ou teste
  é afetado — o arquivo não é referenciado por `package.json`, `Dockerfile`
  ou configuração de lint.
- **`openspec/config.yaml`**: correção do bloco `context`, que é lido por
  agentes ao gerar artefatos de toda change futura.
- **Nenhum app é afetado em código.** Não há alteração em `apps/api`,
  `apps/workers`, `apps/inbox` ou `apps/frontend` além da remoção do
  `README.md` do frontend. Nenhuma referência de projeto é criada.
- **Licenciamento de dependências**: stack é .NET (MIT), Mantine (MIT),
  pacotes `A2A`/`Microsoft.Agents.AI` (MIT). Nenhuma incompatibilidade com
  Apache-2.0.
- **CI**: não existe `.github/workflows/` neste repositório. O script de
  verificação nasce rodável à mão; automatizá-lo em CI é change separada, e
  `CONTRIBUTING.md` deve declarar isso honestamente em vez de prometer uma
  verificação automática que não existe.
