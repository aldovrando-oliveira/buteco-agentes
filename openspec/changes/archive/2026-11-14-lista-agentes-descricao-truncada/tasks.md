## Casos que saem ou mudam de asserção

Nenhum caso sai nem muda de asserção. Entram dois, em
`apps/frontend/src/features/agents/components/AgentTable.test.tsx` (design D6):

- **`trunca a descrição em uma linha e guarda o texto completo no title`** (novo): com uma descrição de ~330
  caracteres, o texto inteiro está no elemento da descrição, o `title` dele é igual à descrição e ele tem
  `data-truncate="end"`.
- **`fixa a largura das colunas, independente do conteúdo`** (novo): com a mesma descrição longa, o `<table>` tem
  `--table-layout: fixed` e os cinco `<th>` têm, na ordem, `38%`, `18%`, `13%`, `17%` e `14%`. A expectativa é
  escrita no teste, não lida do componente.

Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar
(convenção 15). O `~/.cache/buteco-agents/bin/guarda-287.sh` reintroduz só o defeito nomeado no `AgentTable.tsx`,
roda o `AgentTable.test.tsx` e devolve o arquivo pela cópia, com o sha conferido. Saídas em
`~/.cache/buteco-agents/287-guardas/`.

**A suíte** roda pelo `~/.cache/buteco-agents/bin/suite-287.sh` (cópia do `suite-279.sh`), com o lock e a regra
assimétrica: uma rodada verde conta mesmo com a CPU caindo; uma reprovação só conta com a CPU em 100 do início ao
fim, senão é inconclusiva e se repete. Reprovação por caso que esta change mudou registra-se "reprovada pelo X
(a → b), corrigida". A comparação é por arquivo + nome (`frontend.json`) contra a baseline.

## 0. Preparação

- [x] 0.1 (repo) Issue #287 (`bug`, `app: frontend`) em `In progress` no board; worktrees `../buteco-agentes-287` (`fix/287-lista-agentes-descricao`) e `../buteco-agentes-287-baseline` (detached), os dois em `c74d0d2`.
- [x] 0.2 (scripts locais) `suite-287.sh`, cópia do `suite-279.sh`, e `guarda-287.sh`, cópia do `guarda-279.sh` (só o cabeçalho muda nos dois).
  Conferido: o corpo dos dois é idêntico ao da origem (`diff` sem saída, fora o cabeçalho).
- [x] 0.3 (apps/frontend, baseline) `suite-287.sh` sobre o worktree da baseline, `frontend`, com a saída completa em `~/.cache/buteco-agents/287-baseline/`. Registrar commit, casos, arquivos, duração, CPU mínima e load.
  Feito em 14/11/2026, em `c74d0d2`, árvore limpa: 2818 casos em 207 arquivos, 198 s, CPU mínima 55, `APROVADA`. Load de 5,99 no início e 40,70 no fim; VM do Podman de pé, com o `postgres_1` e o `rabbitmq_1` do compose (`PAUSAR_RABBIT=0`). Antes, `npm ci` no `apps/frontend` dos dois worktrees, que nasceram sem `node_modules`.

## 1. A tabela (apps/frontend, design D1, D2, D3, D5)

- [x] 1.1 `AgentTable`: `<Table layout="fixed">`, `w` em cada `Table.Th` (`38% | 18% | 13% | 17% | 14%`), `title={agent.description}` no `Text` da descrição, e sem o `style={{ minWidth: 0 }}` do `Stack`. O comentário sobre a tabela diz por que o layout é fixo (o piso de largura do automático) e aponta o precedente do `KnowledgeBaseTable`.
- [x] 1.2 Os dois casos do topo no `AgentTable.test.tsx`, com a descrição longa como constante do teste.
  Feito: a descrição tem 354 caracteres, e as larguras esperadas são uma constante do teste. O arquivo passa com 17 de 17; `eslint` nos dois arquivos e `tsc -b` sem erro.

## 2. Os guardas (apps/frontend, convenção 15)

- [x] 2.1 **(guarda)** "Layout automático de volta": o `layout="fixed"` removido da `Table` do `AgentTable.tsx`, com as larguras dos `Table.Th` mantidas (o defeito real é a volta ao layout automático, não uma porcentagem trocada) → reprova `fixa a largura das colunas, independente do conteúdo`, no `AgentTable.test.tsx`.
  Visto: 1 reprovado de 17, `fixa a largura das colunas, independente do conteúdo`, com `AssertionError: expected '' to be 'fixed'` em `expect(table.style.getPropertyValue('--table-layout')).toBe('fixed')` (l. 215); voltou: `AgentTable.tsx`, sha igual (`8a29393`). Saída em `~/.cache/buteco-agents/287-guardas/2.1.log`.
- [x] 2.2 **(guarda)** "Sem o texto completo": o `title={agent.description}` removido do `Text` da descrição no `AgentTable.tsx` → reprova `trunca a descrição em uma linha e guarda o texto completo no title`, no `AgentTable.test.tsx`.
  Visto: 1 reprovado de 17, `trunca a descrição em uma linha e guarda o texto completo no title`, com `Expected the element to have attribute: title="Conduz a triagem…"` / `Received:` vazio, em `expect(description).toHaveAttribute('title', longDescription)` (l. 205); voltou com o sha igual (`8a29393`). Saída em `2.2.log`.
- [x] 2.3 **(guarda)** "Sem o truncamento": o `truncate` removido do `Text` da descrição no `AgentTable.tsx`, com o `title` mantido → reprova `trunca a descrição em uma linha e guarda o texto completo no title`, no `AgentTable.test.tsx`, na asserção do `data-truncate`.
  Visto: 1 reprovado de 17, `trunca a descrição em uma linha e guarda o texto completo no title`, com `Expected the element to have attribute: data-truncate="end"` / `Received:` vazio, em `expect(description).toHaveAttribute('data-truncate', 'end')` (l. 208), depois de a asserção do `title` (l. 205) ter passado; voltou com o sha igual (`8a29393`). Saída em `2.3.log`.

## 3. Spec e documentação

- [x] 3.1 (openspec) Delta da `agent-catalog-ui`: o texto do requisito "Listagem de agentes na interface" com a descrição em uma linha e as colunas independentes do comprimento dela, e o cenário "Descrição longa é truncada sem esconder as outras colunas".
- [x] 3.2 (repo) `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`, **antes do archive**, com a baseline, a rodada final e os guardas. A conferência visual vai como pendente de homologação, com o checklist da 4.4, sem segurar o archive.
  Feito: no `CHANGELOG.md`, em `Fixed`; no `02`, a seção `lista-agentes-descricao-truncada` no fim do histórico. A #287 não tem milestone, então não entrou na fila ordenada do `02`.

## 4. Fechamento

- [x] 4.1 (apps/frontend) Rodada `frontend` pelo `suite-287.sh` na árvore da change. Comparação por arquivo + nome com a baseline: nenhum caso some nem muda de resultado; os novos são os dois do topo. Saída em `~/.cache/buteco-agents/287-fechamento/`.
  Feito depois do Prettier, do `eslint` e do `tsc -b`: 2820 em 207 arquivos, 169 s, CPU mínima 62, load 1,37 no início, `APROVADA`. Nenhum caso sumiu nem mudou de resultado; os novos são os dois do topo. Uma rodada anterior, antes do Prettier (só uma quebra de linha no teste), também saiu `APROVADA` com 2820 e ficou à parte, em `287-fechamento-pre-prettier/`; não conta.
- [x] 4.2 (openspec) Archive com o sync da delta, e `openspec validate --all --strict`.
  Feito: `agent-catalog-ui` com 1 requisito modificado; `validate --all --strict` com 103 de 103.
- [x] 4.3 (repo) `git diff --stat` só com `AgentTable.tsx`, `AgentTable.test.tsx`, `openspec/`, `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.
  Conferido: os dois arquivos do `AgentTable`, `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md`, `openspec/specs/agent-catalog-ui/spec.md` e o archive da change.
- [x] 4.4 (repo) Push e PR com `Closes #287`, depois do archive (convenção 24); a issue vai para `In review` logo após o `gh pr create`. O corpo do PR leva o checklist de homologação (convenções 14 e 23), marcado como "homologação, não prende o PR":
  - `/agents` no painel com três agentes: um com descrição longa (~330 caracteres), um sem provider/model e um que delega para dois;
  - nos dois esquemas de cor, com a janela cheia e a ~1100 px;
  - as cinco colunas visíveis, a descrição numa linha com reticências, o `title` com o texto inteiro ao passar o mouse, e o estado dos badges anotado por largura;
  - ajuste pedido aqui (porcentagens, rótulo do badge) vira change nova, ligada à #287 por issue; esta change não é reaberta.
