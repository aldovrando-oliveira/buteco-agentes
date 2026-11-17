## Context

O código em `c74d0d2` (`main` depois do PR #286):

- **`AgentTable`** (`apps/frontend/src/features/agents/components/AgentTable.tsx`, l. 58–139): `<SectionedCard>`
  com uma `<Table>` sem `layout` e sem largura nos `Table.Th`. A primeira célula é um
  `<Stack gap={2} style={{ minWidth: 0 }}>` com o `Anchor` do nome e, quando há descrição,
  `<Text size="xs" c="dimmed" truncate>` (l. 88–97). A última coluna é um `Group gap="xs"` com até dois `Badge`
  ("Precisa de reconfiguração" e "Ativo"/"Inativo").
- **`SectionedCard`** (`components/data/SectionedCard.tsx`, l. 28–50): `Paper` com `overflow: hidden`. É o que
  esconde as colunas que passam da borda, sem rolagem.
- **`KnowledgeBaseTable`** (`knowledge-bases/components/KnowledgeBaseTable.tsx`, l. 229–247): `<Table
  layout="fixed">` com `w` em porcentagem em cada `Table.Th` (`37 | 9 | 23 | 21 | 10`), achado da conferência manual
  de outra change. É o precedente.
- No Mantine 9.4.2, a prop `layout` da `Table` vira a variável `--table-layout` no estilo embutido do `<table>`
  (`varsResolver` em `Table.mjs`), e o CSS aplica `table-layout: var(--table-layout, auto)`. O `w` do `Table.Th`
  vira `width` no estilo embutido do `<th>`. Os dois são observáveis no jsdom.

**Por que quebra:** em `table-layout: auto`, a largura mínima de uma coluna é a largura mínima do conteúdo das
células dela. O `truncate` do Mantine aplica `white-space: nowrap`, `overflow: hidden` e `text-overflow:
ellipsis`; com `nowrap`, a largura mínima do texto é a linha inteira (~330 caracteres), e a coluna cresce até ela
em vez de cortar. O `min-width: 0` do `Stack` não muda isso, porque o piso vem do algoritmo da tabela, não do
flex. A tabela passa da largura do `Paper`, e o `overflow: hidden` some com o resto.

## Goals / Non-Goals

**Goals:** as cinco colunas visíveis em qualquer comprimento de descrição; a descrição em uma linha, truncada,
com o texto completo acessível pelo `title`; a garantia presa por teste que reprova contra o defeito.

**Non-Goals:** limite de tamanho da descrição no cadastro, nome de agente muito longo, a página de detalhe, as
outras listas, o texto dos badges.

## Decisions

### D1. `Table layout="fixed"` com largura em cada coluna

A `Table` do `AgentTable` passa a `layout="fixed"`, com `w` em cada `Table.Th`, o mesmo mecanismo do
`KnowledgeBaseTable`. Em layout fixo, a largura das colunas sai da primeira linha (o cabeçalho) e da largura da
tabela, e o conteúdo das células não a altera. Com a coluna fixa, o `truncate` que já existe passa a cortar, e
nada mais no `Text` muda.

**Alternativa descartada:** manter o layout automático e zerar a largura intrínseca da descrição (`width: 0;
min-width: 100%` no `Text`), com `45%` na primeira coluna. Funciona, e protege melhor os badges em tela estreita,
porque as outras colunas continuam crescendo com o conteúdo. Descartada porque a grade continua variando com o
conteúdo, são dois mecanismos combinados para um efeito, e a base já tem o precedente do `fixed` numa tabela do
mesmo card.

### D2. As larguras: `38% | 18% | 13% | 17% | 14%`

Agente, Provedor / modelo, Ferramentas, Delega para, Estado, somando 100%. A coluna "Estado" leva 14% porque
carrega até dois badges lado a lado. Com `40 | 20 | 14 | 16 | 10`, o badge "Precisa de reconfiguração" já era
cortado a 1650 px.

As porcentagens vão ao merge como estão; a conferência visual é homologação, depois do merge (convenção 23), e
um ajuste pedido ali vira change nova (ver Risks). Qualquer mudança nelas entra no `AgentTable.tsx` e na
expectativa do segundo caso de teste juntos.

### D3. O texto completo no `title`

O `Text` da descrição ganha `title={agent.description}`: o navegador mostra o texto inteiro ao passar o mouse, e
leitor de tela tem o conteúdo completo no próprio nó (o truncate é só visual; o texto continua inteiro no DOM).

**Alternativa descartada:** `Tooltip` do Mantine. Com ~330 caracteres vira um bloco sobre a tabela, cobrindo as
linhas de baixo, e o texto completo já está na página de detalhe, a um clique pelo nome.

### D4. Só o `AgentTable`

Varredura em `c74d0d2`: entre os `.tsx` de `apps/frontend/src` que usam `<Table`, fora os testes, os únicos com
`truncate` ou `lineClamp` são:

| arquivo | uso | layout |
|---|---|---|
| `agents/components/AgentTable.tsx` (l. 93) | descrição, `truncate` | automático (**esta change**) |
| `knowledge-bases/components/KnowledgeBaseTable.tsx` (l. 163, 262) | origem, `truncate`; descrição, `lineClamp={1}` | `fixed` |

Os outros `truncate`/`lineClamp` do app (`AgentMcpServerRow`, `FolderPickerModal`, `KnowledgeBaseOriginCard`,
`SessionList`, `AppShell`) estão fora de tabela, onde o piso de largura do layout automático não existe.

### D5. O `minWidth: 0` do `Stack` sai

Não tem efeito dentro de `td` (D1 explica por quê), e com layout fixo continua sem ter. Deixá-lo sugere a quem lê
que é ele que contém a descrição.

### D6. Os dois casos de teste, e o que cada um prende

- **"trunca a descrição em uma linha e guarda o texto completo no title"**: com a descrição de ~330 caracteres, o
  elemento do texto tem `title` igual à descrição inteira, o texto inteiro no conteúdo e `data-truncate="end"`. É
  o atributo que o `Text` do Mantine 9.4.2 grava quando recebe `truncate` (`getTextTruncate` em `Text.mjs`), e é
  nele que o CSS do Mantine pendura o `nowrap` e as reticências (`.m_b6d8b162:where([data-truncate])`); sem a prop,
  o atributo não existe. O acoplamento ao Mantine é o mesmo do `--table-layout` do outro caso. O estilo computado
  não é afirmado, porque o jsdom não aplica o CSS do Mantine; o corte na tela é da conferência visual da
  homologação.
- **"fixa a largura das colunas, independente do conteúdo"**: renderiza uma lista com a descrição longa e afirma
  `--table-layout: fixed` no estilo do `<table>` e as cinco larguras, na ordem, nos `<th>`. A expectativa é escrita
  à parte, no teste, a partir deste design, e não lida do componente (a sexta forma da convenção 15: guarda
  circular).

O jsdom não calcula layout, então nenhum dos dois mede largura renderizada; eles prendem o **artefato** que a
correção produz (o estilo e o atributo que o Mantine grava), que é a forma determinística que a convenção 15 pede quando o
efeito observável não está ao alcance da suíte. O efeito em si, colunas visíveis, é da conferência visual na homologação.

### Medição fora do painel

Feita antes desta change, sobre o componente real, fora do painel: Mantine 9.4.2, o tema do painel, Chrome
headless, esquema claro, quatro agentes de exemplo (um com a descrição longa; um sem provider/model, inativo,
delegando para dois).

| layout | 1650 px | 1100 px | 820 px |
|---|---|---|---|
| atual (automático) | só "Agente" visível | só "Agente" visível | só "Agente" visível |
| `fixed` `38/18/13/17/14` | cinco colunas; badges inteiros | cinco colunas; "Precisa de reconfiguração" com reticências | cinco colunas; "Precisa de reconfiguração" e "Inativo" com reticências |

**Isto não substitui a conferência manual da convenção 14:** não foi no painel real (com o `AppShell`, a
navegação lateral e a largura útil que ela deixa) nem no esquema escuro. A conferência é a da homologação, no
checklist do PR.

## Risks / Trade-offs

- **[As porcentagens vão ao merge sem conferência no painel real nem no esquema escuro]** → a única medição é a
  de fora do painel (Chrome headless, esquema claro, componente isolado). Pela convenção 23 (decisão de
  14/11/2026), a conferência visual é homologação e não segura o archive nem o PR. Contraparte verificável: o
  checklist de homologação no corpo do PR (tarefa 4.4), com as mesmas telas e larguras da medição, nos dois
  esquemas. Ajuste pedido na homologação vira **change nova, ligada à #287 por issue**; esta change não é
  reaberta.
- **[Badges de estado cortados abaixo de ~1200 px de área útil]** → é o preço do layout fixo contra a
  alternativa de D1. Contraparte verificável: o checklist de homologação olha a ~1100 px, nos dois esquemas. Se
  incomodar, o rótulo curto no badge vira issue própria, fora desta change.
- **[Um guarda que só afirma o `title` passaria com o layout automático de volta ou sem o truncate]** → por isso
  são dois casos, e cada guarda reintroduz um defeito diferente (tarefas 2.1, 2.2 e 2.3): sem `layout="fixed"`
  reprova o segundo; sem o `title` ou sem o `truncate` reprova o primeiro.
- **[As porcentagens mudarem numa change futura e o teste ficar com as antigas]** → o segundo caso afirma as
  larguras; mudar uma sem a outra reprova a suíte, que é o comportamento desejado.

## Migration Plan

Nenhuma migração. Sai na imagem do `frontend`; reverter é voltar o `AgentTable.tsx`.

## Open Questions

Nenhuma.
