## Why

O painel de inventário tem quatro cards de catálogo e **nenhum de atividade**
(`frontend-inventario-catalogos`, arquivada em 2026-09-17). "Sessões em um
período" foi a primeira métrica de atividade escolhida para sair do papel, e
hoje **nenhuma rota do `apps/inbox` conta sessões por intervalo**: as duas
existentes — `GET /channels/{id}/sessions` e `GET /contacts/{id}/sessions` —
devolvem a lista completa, sem parâmetro de data.

O consumo final é **um número**, não uma lista. Modelar como rota de listagem e
deixar o cliente contar repetiria o erro que a proposta anterior já recusou para
"mensagens processadas": trazer payload inteiro para exibir um inteiro.

Esta change é **só backend**, pela convenção 1 (catálogo → vínculo → execução →
UI): a tela que consome este número é change futura, sequenciada depois.

## What Changes

- **Rota nova, só leitura**: `GET /sessions/summary?from=…&to=…` no `apps/inbox`,
  devolvendo `{ "startedCount": N }` — a contagem de sessões cujo `StartedAt`
  cai dentro do intervalo, **inclusivo nos dois limites**.
- **"Sessão no período" = sessão iniciada no período.** As outras duas leituras
  possíveis foram descartadas na exploração e a razão está no `design.md`:
  *overlap* depende de um dado que não existe (`ClosedAt = null` não significa
  "aberta" — ver `02-HISTORICO_E_STATUS.md`, item "Encerramento explícito de
  sessão"), e `LastActivityAt` reescreve o passado (o mesmo intervalo fechado
  responde números diferentes conforme o dia da pergunta).
- **O campo da resposta se chama `startedCount`, não `count`.** É a convenção 13
  aplicada ao contrato da API: um nome vago convida a reler como "sessões ativas"
  mais tarde, e não existe rótulo de tela ainda para corrigir a leitura.
- **`from` e `to` são obrigatórios** e vêm de quem chama. Sem período nomeado
  resolvido no servidor — não há relógio injetável no `apps/inbox`, e introduzir
  um é escopo que esta change não paga.
- **Três casos de erro, uma única forma de corpo** (`ValidationProblem`):
  parâmetro ausente, data malformada, `to < from`.
- **Sem teto de intervalo e sem índice novo em `StartedAt`** — os dois ficam
  fora **com gatilho declarado** (convenção 22), não por esquecimento.

Nenhuma quebra de contrato: nada existente muda de forma ou de comportamento.

## Capabilities

### New Capabilities

- `inbox-session-period-summary`: contagem agregada de sessões do `apps/inbox`
  por intervalo de datas, servida como recurso próprio de leitura — os limites do
  intervalo, sua validação, e a semântica de qual instante da sessão decide a
  inclusão.

  **Por que capability nova, e não requisitos em `inbox-contact-session`:** aquela
  capability descreve o **serviço interno** que resolve `Contact`/`Session` na
  chegada de uma mensagem (fronteira de sessão, metadado, timeout) — nenhum
  requisito dela é sobre superfície HTTP de leitura. A separação repete a que já
  existe entre `inbox-message-orchestration` (o mecanismo) e
  `inbox-message-history` (a leitura sobre ele). O nome segue o padrão
  `<app>-<domínio>-<aspecto>` dos vizinhos (`inbox-message-history`,
  `knowledge-index-diagnostics`), e carrega `period` porque é o intervalo — não
  "ativas", não "todas" — que define o recurso.

### Modified Capabilities

Nenhuma. Nenhum requisito existente muda.

## Impact

- **Apps afetados: só `apps/inbox`.** Nenhum arquivo em `apps/api`,
  `apps/workers` ou `apps/frontend`.
- **Código**: um endpoint novo (`Contacts/Endpoints/`), uma query e um handler
  mediator (`Contacts/Queries/`), um record de resposta (`Contacts/Responses/`),
  e o `Map*` correspondente em `Program.cs`. Nenhum arquivo existente muda de
  comportamento.
- **Banco**: **sem migração**. Nenhuma coluna, nenhum índice — a decisão de não
  criar índice em `StartedAt` está no `design.md` com o gatilho que a reabre.
- **Autenticação**: nenhuma exceção a declarar. `Program.cs` classifica toda rota
  como autenticada por padrão e a lista de exceções é só para as anônimas; a rota
  nova não a toca.
- **Contrato**: rota nova, aditiva. Nenhum consumidor existente é afetado.
- **Consumidor**: nenhum nesta change. A tela é sequenciada depois (convenção 1).
