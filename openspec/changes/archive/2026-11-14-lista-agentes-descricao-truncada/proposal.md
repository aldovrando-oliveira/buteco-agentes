**Issue:** #287

## Why

Em `/agents`, um agente com descrição longa (caso real: "Butecando - Recrutamento", ~330 caracteres) faz a tabela
passar da largura do card. Só a coluna "Agente" fica visível: "Provedor / modelo", "Ferramentas", "Delega para" e
"Estado" somem, sem rolagem, porque o `SectionedCard` (`overflow: hidden`) esconde o que passa da borda.

A descrição já tem `truncate`, mas a `Table` do `AgentTable` está em layout automático. Nele, o piso de largura da
célula é o texto sem quebra, então o `nowrap` do truncate empurra a coluna em vez de cortar. O
`style={{ minWidth: 0 }}` do `Stack` da célula não tem efeito dentro de `td`.

## What Changes

**`AgentTable`** (`apps/frontend/src/features/agents/components/`):
- a `Table` passa a `layout="fixed"`, com `w` em cada `Table.Th`: `38% | 18% | 13% | 17% | 14%` (Agente, Provedor /
  modelo, Ferramentas, Delega para, Estado), o mesmo mecanismo do `KnowledgeBaseTable`. Com a coluna fixa, o
  truncate que já existe passa a cortar;
- o `Text` da descrição ganha `title={agent.description}`, com o texto completo;
- sai o `style={{ minWidth: 0 }}` do `Stack`, que não fazia nada.

**Spec `agent-catalog-ui`:** o requisito "Listagem de agentes na interface" diz que a descrição ocupa uma linha,
truncada, com o texto completo no `title` e na página de detalhe, e que o comprimento dela não altera a largura das
colunas nem tira as outras quatro da área visível. Entra o cenário "Descrição longa é truncada sem esconder as
outras colunas".

**Testes:** dois casos novos no `AgentTable.test.tsx`; nenhum sai.

**Documentação:** `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.

**Fora do escopo:**
- limite de tamanho da descrição no cadastro;
- nome de agente muito longo;
- a página de detalhe do agente;
- as outras listas do painel (a única outra tabela com truncate/lineClamp, o `KnowledgeBaseTable`, já é `fixed`;
  design D4).

**Risco conhecido:** abaixo de ~1200 px de área útil, os badges da coluna "Estado" ganham reticências ("Precisa de
reconfiguração" a 1100 px; "Inativo" também a 820 px). Um rótulo curto no badge fica para outra issue, se a
conferência visual incomodar.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `agent-catalog-ui`: o requisito "Listagem de agentes na interface", com a descrição truncada em uma linha e as
  colunas com largura independente do comprimento dela.

## Impact

- **App:** só o `apps/frontend`.
- **Arquivos:** `AgentTable.tsx` e `AgentTable.test.tsx`.
- **Deploy:** só a imagem do `frontend`.
- **Suíte:** `frontend`.
