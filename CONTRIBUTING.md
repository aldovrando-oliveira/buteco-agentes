# Como contribuir

Obrigado pelo interesse no projeto. Este documento descreve como o trabalho é
organizado aqui, o que se espera de uma contribuição e como verificar seu
trabalho antes de abrir um pull request.

> Este guia está em português, assim como toda a documentação em
> [`docs/`](docs/README.md). A camada de vitrine do repositório — `README.md`,
> `LICENSE`, `SECURITY.md` e `CODE_OF_CONDUCT.md` — está em inglês. O critério
> dessa divisão está em [`docs/conventions.md`](docs/conventions.md#fronteira-de-idioma).

Ao participar deste projeto você concorda com o
[Código de Conduta](CODE_OF_CONDUCT.md). Vulnerabilidades de segurança **não**
devem ser reportadas como issue pública — siga a [política de
segurança](SECURITY.md).

---

## Antes de escrever código

Leia [`docs/conventions.md`](docs/conventions.md). Ele descreve as premissas
que governam esta base — isolamento entre apps, ausência de abstração
prematura, degradação graciosa, checagem de integridade no startup, contratos
entre apps e padrões de teste. Uma contribuição que ignore essas regras vai
precisar ser refeita, mesmo que funcione.

Para entender o sistema, [`docs/architecture.md`](docs/architecture.md). Para
montar o ambiente, [`docs/development.md`](docs/development.md).

---

## O fluxo: spec-driven via OpenSpec

Este projeto **não** trabalha com "abrir um PR direto com o código". Toda
mudança de comportamento passa antes por proposta e especificação, versionadas
junto com o código em `openspec/`.

```
  /opsx:explore     investigar o problema, mapear o código, comparar opções
        │           — pensamento, nenhum código escrito
        ▼
  /opsx:propose     gerar proposal.md, design.md, specs/ e tasks.md
        │
        ▼
     revisão        o mantenedor revisa a proposta antes da implementação
        │
        ▼
  /opsx:apply       implementar as tarefas, marcando cada uma ao concluir
        │
        ▼
  /opsx:sync        promover as specs da mudança para openspec/specs/
        │
        ▼
  /opsx:archive     mover a mudança para openspec/changes/archive/
```

Os artefatos de cada mudança vivem em `openspec/changes/<nome-em-kebab-case>/`:

| Artefato | Responde |
|---|---|
| `proposal.md` | por que a mudança é necessária, o que muda, quais capabilities são afetadas |
| `design.md` | como implementar: decisões técnicas com alternativas consideradas, riscos e trade-offs |
| `specs/<capability>/spec.md` | o que o sistema deve fazer: requisitos com `SHALL`/`MUST` e cenários `WHEN`/`THEN` |
| `tasks.md` | passos de implementação, em checkboxes, ordenados por dependência |

Comandos úteis:

```bash
openspec list                          # mudanças ativas
openspec status --change "<nome>"      # progresso de uma mudança
openspec validate <nome>               # valida os artefatos
openspec show <nome>                   # exibe uma mudança ou spec
```

### Regras dos artefatos

- **Cada tarefa deve indicar em qual app ela roda** (`apps/api`,
  `apps/workers`, `apps/inbox`, `apps/frontend`).
- **Nunca proponha referência de projeto entre apps.** O isolamento é
  estrutural, não uma preferência.
- **Justifique qualquer conteúdo colocado em `libs/`** no `design.md`.
- **Inclua testes unitários** no frontend e no backend.
- Antes de fixar qualquer versão de runtime, linguagem, framework ou
  biblioteca como decisão, **verifique a versão estável e suportada na data**.
  Versão de dependência não é pergunta aberta — é decisão a ser verificada
  antes de propor.
- **Se a implementação divergir do `design.md` aprovado**, corrija o
  `design.md` para refletir a causa real. Um achado técnico que muda uma
  decisão nunca fica registrado apenas na conversa.

---

## Verificação antes do pull request

> **Este repositório não tem integração contínua configurada.** Não há
> `.github/workflows/`, e nada roda automaticamente quando você abre um pull
> request. Tudo abaixo precisa ser executado **localmente** por você, e o
> resultado declarado no PR. Adicionar CI é uma mudança em aberto, ainda não
> feita.

### 1. Testes dos apps afetados

Rode a suíte de cada app que sua mudança tocou. Os testes de integração sobem
Postgres e RabbitMQ efêmeros via Testcontainers e precisam de Docker ou
Podman disponível — se você usa Podman, leia
[a seção de troubleshooting](docs/development.md#testcontainers-com-podman)
antes, porque a falha mais comum parece defeito de código e é de
infraestrutura.

```bash
dotnet test libs/ProviderCatalog.Tests
dotnet test apps/api/Api.sln
dotnet test apps/workers/Workers.sln
dotnet test apps/inbox/Inbox.sln
dotnet test tests/CrossAppTaskStoreCompatibility.Tests
dotnet test tests/InboxOrchestratorRoundTrip.Tests

cd apps/frontend && npm run lint && npm run format:check && npm run test && npm run build
```

Se sua mudança toca o contrato entre dois apps, rode também os dois projetos
cruzados em `tests/` — são a única verificação de acordo real entre eles.

### 2. Integridade da documentação

```bash
python3 scripts/check-docs.py
```

Verifica links relativos quebrados, links para mudanças arquivadas sem o
prefixo `archive/`, apps ausentes da documentação de arquitetura (nos dois
sentidos) e a presença da seção `[Unreleased]` no `CHANGELOG.md`.

### 3. Conferência manual, quando houver mudança visual

A suíte do frontend roda em jsdom, que **não enxerga cor, contraste nem
layout**. Uma mudança de tema ou de composição pode deixar a suíte inteira
verde e o painel ilegível — isso já aconteceu.

Mudança que mexe em aparência traz conferência manual como tarefa própria,
tela a tela, **nos dois esquemas de cor**. A conferência é iterativa: cada
correção muda o que fica visível.

---

## Convenção de commits

[Conventional Commits](https://www.conventionalcommits.org/), com escopo por
app e assunto em português, no imperativo, sem ponto final.

```
<tipo>(<escopo>): <assunto em pt-BR, imperativo>
```

**Tipos em uso:** `feat`, `fix`, `refactor`, `doc`, `chore`.

**Escopos em uso:** `api`, `workers`, `inbox`, `frontend`, e ocasionalmente
`auth`, `mcp`, `deploy`, `dev`. Uma mudança que atinge mais de um app lista
os escopos separados por vírgula.

Exemplos reais deste repositório:

```
feat(api,workers): abre o catálogo de bases de conhecimento e documentos
feat(frontend): unifica a casca do painel numa barra lateral com ícones
refactor(frontend): migra o roteamento para o data mode do react-router
chore(dev): libera a porta da pré-visualização do painel no CORS local
doc: registra o redesenho do painel e a entrega containerizada
```

O commit sem escopo é aceitável quando a mudança não pertence a nenhum app —
`doc:` é o caso típico.

---

## Abrindo o pull request

O template de PR pede três coisas. Elas não são burocracia:

1. **Qual mudança OpenSpec** o PR implementa. Se não houver uma, explique por
   que — correções triviais e ajustes de documentação podem dispensar, mas
   mudança de comportamento não.
2. **Quais verificações você rodou**, com o resultado. Como não há CI, essa
   declaração é a única evidência que o revisor tem.
3. **O que ficou de fora**, se algo ficou. Escopo reduzido é decisão
   legítima; escopo reduzido em silêncio não é.

Alguns pedidos adicionais:

- Um PR por mudança OpenSpec. Não junte linhas de trabalho diferentes.
- Marque as tarefas concluídas em `tasks.md` conforme avança — o arquivo é o
  registro de progresso, não um plano estático.
- Se você encontrou um defeito que não é o objeto do PR, **reporte e
  sequencie** em vez de corrigir de passagem. Correção fora de escopo dentro
  de outra mudança dificulta a revisão das duas.

---

## Reportando defeitos e propondo funcionalidades

Use os templates de issue. Para defeito, o que mais ajuda é o caminho de
reprodução e qual app está envolvido; para funcionalidade, o problema que
você quer resolver, mais que a solução que você imaginou — a solução costuma
mudar durante a exploração.

Vulnerabilidade de segurança **nunca** vai em issue pública. Ver
[SECURITY.md](SECURITY.md).
