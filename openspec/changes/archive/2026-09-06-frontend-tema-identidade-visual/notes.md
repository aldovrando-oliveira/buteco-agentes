# Notas da conferência visual

Divergências encontradas na comparação lado a lado com
`~/Downloads/sistema_gestao/Buteco Agentes.dc.html`, registradas conforme a
tarefa 6.3.

**Nenhuma delas se resolve no tema.** São composição de componente ou escolha
de call site, e por isso ficam para as etapas seguintes do redesenho, não para
esta change. O critério usado: se o conserto é mudar um valor em `theme.ts`, é
daqui; se é mudar markup ou uma prop de componente, é de lá.

## Detalhe do servidor MCP — catálogo de tools

Arquivo: `apps/frontend/src/features/mcp-servers/components/McpServerToolsCatalog.tsx`

### 1. Faltam o divisor entre as linhas e a faixa do cabeçalho

O componente monta `Card > Stack > Group` por tool, sem borda entre as linhas e
sem fundo no cabeçalho. O protótipo separa cada tool com uma borda inferior e
põe o cabeçalho sobre `--sf2`, com borda embaixo.

Nenhum token desenha uma borda que não existe no markup. O conserto é trocar a
composição por `Card.Section withBorder` — uma para o cabeçalho e uma por tool
—, tirando o padding do `Card` e passando para cada seção.

O mesmo padrão de "cabeçalho em faixa + linhas divididas" aparece nas duas
listagens e provavelmente em outros cards de detalhe; vale resolver como um
componente compartilhado em vez de caso a caso.

### 2. O cabeçalho do card está um degrau escuro demais

Usamos `c="dimmed"`, que o tema resolve para `gray[6]` (`--mut`, `#686d74`). O
protótipo usa `--mut2` (`#8b9097`) para header de card — o `gray[5]` da nossa
escala. Na captura, o "CATÁLOGO DE TOOLS" tem quase o mesmo peso visual do
texto do corpo, em vez de recuar.

Não é erro de tema: os dois tons estão corretos e nos índices certos. É o call
site que escolheu o semântico errado. O Mantine não tem um semântico "mais
apagado que dimmed", então a correção é explícita (`c="gray.5"`) ou um override
de `Text` no tema para esse papel.

Vale varrer os outros headers de card em maiúsculas junto, que devem ter o
mesmo problema.

### 3. Falta o tracking nos headers em maiúsculas

O protótipo pede `letter-spacing: .05em` nos rótulos 11px/600 uppercase. Não
declaramos em lugar nenhum. Como o papel se repete, cabe no tema como override
de componente, e não em cada chamada.

### 4. Defeito de layout: a coluna direita quebra em duas linhas

Na tool com descrição longa (`get_music_info`), o texto "permitida em 1 agente"
quebra em duas linhas. O `<Group wrap="nowrap">` impede o container de
quebrar, mas o `<Text>` da direita não tem `flex-shrink: 0` e é comprimido pela
descrição.

Este é o único item da lista que é defeito, não divergência estética — e é
anterior a esta change, só ficou visível na comparação. No protótipo a coluna
direita tem largura própria.

## Detalhe do servidor MCP e do agente — cabeçalho

Arquivos: `apps/frontend/src/features/mcp-servers/pages/McpServerDetailPage.tsx`,
`apps/frontend/src/features/agents/pages/AgentDetailPage.tsx`, e os demais
call sites de `<Badge>` de estado (`McpServerTable.tsx`, `AgentTable.tsx`,
`McpServerAgentsCard.tsx`, `AgentMcpServerRow.tsx`).

### 5. Badge de estado saturado demais — o risco previsto no design.md

Confirmado em tela o trade-off registrado na seção Risks do `design.md`. O
`<Badge>` do Mantine tem `variant="filled"` como padrão, que resolve para
`green[6]` (`#22935c`) com texto branco: 3.89:1, abaixo de AA. O protótipo usa
a variante clara — `green[1]` de fundo com `green[9]` de texto, 4.81:1.

**O tema já está certo.** Os dois tons do protótipo estão exatamente nos
índices que a variante clara consome (1 e 9); o que falta é pedir a variante
nos call sites. Nenhum valor de `theme.ts` muda por causa disto.

Junto vêm dois defaults do Mantine que o protótipo não tem: o `<Badge>` aplica
`text-transform: uppercase` e `font-weight: bold`. O código já passa `Ativo`
como rótulo — o "ATIVO" da captura é do componente, não do texto.

**Divergência a decidir antes de implementar:** o `AJUSTES_VISUAIS.md` (seção
3, item 3) pede "pill uppercase, fz 10, fw 600, letter-spacing .04em". O
protótipo mostra "Ativo" em caixa de sentença. Os dois discordam. A arbitragem
do documento foi sobre o README, não sobre o protótipo — e a decisão fechada
foi que o protótipo é a identidade. Ficando com o protótipo: caixa de sentença,
`tt="none"`.

Alcance: 6 badges de estado, mais os de aviso (`Vinculado sem tools`,
`Precisa de reconfiguração`, `Sem tools`), que têm o mesmo problema em âmbar —
`yellow[6]` com branco dá 3.72:1.

### 6. Falta o link de volta para a listagem

Nenhuma das duas páginas de detalhe tem navegação de volta: não há `Anchor`,
breadcrumb nem link para a rota da listagem. O protótipo põe "← Servidores MCP"
acima do título.

Não é só estético — sem isso, voltar à listagem depende do botão do navegador
ou da barra lateral. Vale como um componente compartilhado de cabeçalho de
detalhe, já que as duas telas repetem a mesma estrutura de título, badge,
descrição e ações.

## Listagens

### 7. Tabelas sem fundo branco — REGRESSÃO DESTA CHANGE, já corrigida

Ao pintar o fundo da página (D4), o conteúdo principal do painel ficou sobre o
cinza: as três tabelas, os dois formulários e as duas colunas do detalhe de
canal nunca tiveram `Paper` nem `Card`, e o `<Table>` do Mantine não tem fundo
próprio. Enquanto o `<body>` era branco isso não aparecia.

Diferente dos outros itens desta nota, **não fica para uma etapa seguinte**:
foi esta change que quebrou. Corrigido aqui — ver D12 no `design.md`.

O que ficou de fora e continua para a etapa das telas: a faixa de fundo no
cabeçalho da tabela, as colunas em maiúsculas com tracking, e os divisores
entre as linhas com o tom certo — o mesmo padrão do item 1.

### 8. Itens da navegação lateral sem ícone

Confirmado: nenhum item da barra lateral tem ícone. É a etapa do shell (E2),
onde entram a dependência do Lucide, o badge do produto no topo, o item ativo
com fundo do accent a ~8%, e o rodapé com o toggle de tema. Nada disso é
tema — o E1 não tocaria nisso em nenhuma hipótese.

Na mesma captura, também do E2: o header superior ainda existe (o protótipo o
elimina) e o toggle de tema está no canto superior direito em vez do rodapé da
barra lateral.

### 9. Rótulo "Buscar" visível acima do campo

O protótipo usa só o placeholder "Buscar por nome ou descrição", sem rótulo.
A implementação mostra "Buscar" acima do campo. Etapa das telas — é uma prop do
`TextInput`, não tema.

### 10. Filtro por situação sem container — REGRESSÃO DESTA CHANGE, já corrigida

O `SegmentedControl` apareceu sem fundo, restando só os separadores verticais.
Causa: o Mantine pinta o container com `gray[1]`, e a primeira versão do D4
tinha posto o fundo da página nesse mesmo tom. Os dois se anularam.

Corrigido aqui — ver D13 no `design.md`. Afeta mais catorze outros papéis que
liam `gray[1]` e estavam igualmente invisíveis: hover de item, linha
selecionada, `Code`.

Continua para a etapa das telas, porque é call site e não tema: os separadores
verticais entre os itens (o protótipo não os tem), a borda em volta do
container, o tamanho compacto, e o alinhamento na mesma linha do campo de
busca.

## Conferência concluída

Cobertas: as duas listagens, o detalhe do agente (visão geral, Ferramentas e
Delegações), o detalhe do servidor MCP, os formulários de agente e de servidor,
os modais de confirmação de ação e o modo escuro.

**Sem divergência** nas abas Ferramentas e Delegações, nos formulários, nos
modais de confirmação e no modo escuro — nenhum achado além dos dez listados
acima. O painel não tem modal além dos de confirmação.

Dois dos dez achados eram regressão desta change e foram corrigidos aqui (7 e
10, decisões D12 e D13). Os outros oito são de composição ou call site e vão
para as etapas seguintes:

- **Etapa do shell (E2):** item 8 — ícones da navegação, badge do produto,
  header a eliminar, toggle no rodapé.
- **Etapa das telas (E3):** itens 1, 2, 3, 4, 5, 6 e 9 — faixa e divisores em
  tabelas e cards, tom e tracking dos rótulos em maiúsculas, badges em
  `variant="light"`, link de volta no detalhe, rótulo do campo de busca, e o
  acabamento do `SegmentedControl`.

Item 4 é o único que é defeito e não divergência estética, e é anterior a esta
change.
