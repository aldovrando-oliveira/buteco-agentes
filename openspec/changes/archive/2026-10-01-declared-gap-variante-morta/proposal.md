**Issue:** #83 (fecha) · **#89 dobrada dentro dela** (mesma classe, conteúdo
exclusivo movido antes de fechar) · **Branch:** `fix/83-declared-gap-variante-morta`,
criada sobre `9709e045` — a `main` com a #84/#86 mergeada, conferida por SHA

## Why

A variante `block` do `DeclaredGap` — a moldura tracejada — **não tem consumidor**
desde que a décima rodada de conferência da #52 removeu os três rodapés de lacuna
da página do sistema. Os **dois** sítios que usam o componente passam
`variant="inline"`:

- `apps/frontend/src/features/insights/components/InsightsKpiGrid.tsx:92`
- `apps/frontend/src/features/insights/components/AgentModelsCard.tsx:148`

**E `block` é o `variant` DEFAULT** (`DeclaredGap.tsx:78`). Um `<DeclaredGap>` novo
escrito sem a prop renderiza exatamente o quadro que o requisito *"Métrica aprovada
no protótipo e sem fonte não é inventada"* proíbe no cenário *"Coluna sem fonte sai
sem deixar quadro no lugar"*. **A régua está invertida em relação à spec:** o caso
proibido é o que sai de graça, e o permitido é o que precisa ser pedido por escrito.
O próximo componente cai na armadilha **sem fazer nada errado**.

**E a razão de ninguém ter visto: o branch morto tem teste verde.**
`DeclaredGap.test.tsx:97` — *"a variante block mantém a moldura tracejada do quadro
6"* — exercita o `block` e passa. Um guarda que cobre código sem consumidor
**mantém o código vivo e esconde que ele está morto**; a cobertura vira camuflagem,
e é por isso que nenhuma varredura de cobertura acusaria.

**A #89 dobra aqui porque registra a MESMA CLASSE** — declaração viva sem
consumidor, que o compilador não acusa — e o caso pontual dela já foi corrigido na
#75. O que resta nela é a **forma**, e a #83 é o caso canônico. Duas issues abertas
sobre a mesma classe produzem o *"corrigir metade"*: fecha-se a instância e a classe
continua sem registro que sobreviva ao archive.

## What Changes

### O que decide a forma, e o motivo escrito

Das três formas que a #83 põe na mesa, **a variante sai inteira**:

| forma | decisão | motivo |
|---|---|---|
| **remover a variante, a prop e o branch** | **escolhida** | não há consumidor, e a variante é previsão e não repetição observada (convenção 2). **E é a única que move a classe para dentro do compilador:** sem a prop, passar `variant` vira erro de compilação, e um `DeclaredGap` novo não tem como produzir o quadro proibido |
| manter a variante e trocar o default para `inline` | recusada | desarma a armadilha e **deixa a classe viva**: continua declaração sem consumidor, invisível ao compilador e varrível só por leitura — que é exatamente o que a #89 registra como não funcionando |
| manter como está | recusada pelo requisito que ela viola | — |

**Custo assumido, declarado:** se um artboard desenhar o quadro depois, ele é
reescrito. O quadro tracejado tem **12 linhas** no `HEAD`, e a razão dele sobrevive
na issue #83 e neste registro.

**A #90 foi lida antes de decidir, e ela NÃO pede o `block`.** O trabalho que ela
guarda é a **nota de regime por grupo** no card de Motivos — *"recusa medida desde
26/09/2026"* —, que a página já renderiza noutro card como `Text size="xs" c="dimmed"`
sem moldura nenhuma (`FailuresCard.tsx:186-190`). O gatilho dela é o `Main.dc.html`
ganhar elemento para a nota, não ganhar quadro. **Nada no caminho pede a moldura.**

### `apps/frontend` — a simplificação

- **`DeclaredGap`** perde a prop `variant`, o branch de `block` e o atributo
  `data-gap-variant`. Sobra a linha esmaecida, que é a única forma com consumidor;
- **os dois consumidores** deixam de passar `variant="inline"`. **A tela não muda** —
  é o que o par de guardas positivos afirma;
- **o atributo `data-gap-variant` sai junto**, e isso é decisão com motivo: com uma
  forma só ele não discrimina nada, e um atributo constante é a mesma classe de
  declaração sem consumidor que esta change fecha. Os três guardas que o afirmam
  passam a afirmar **a ausência da moldura**, que é o estado observável mais próximo
  do elemento que a spec nega — guarda mais forte que o que tem hoje, não mais
  fraco. `data-declared-gap="true"` **fica**: ele é o que separa a lacuna do
  travessão, e tem consumidor na spec e em três guardas;
- **o comentário do componente** é corrigido com a causa (convenção 9): o bloco
  `VARIANTE` tem 18 linhas que descrevem as duas formas e a armadilha, e a change as
  torna falsas.

### `apps/frontend` — a parte da #89

- **a decisão de custo da #89 é tomada, e é "vale, por uma forma mais barata que a
  que ela propõe"**. A #89 propõe derivar `CaveatPlacement` de um `const` array com
  `typeof`; isso **custa os sete comentários por membro** da união, que é onde mora
  *"Só na página do sistema"* / *"Só na aba do agente"* — e nesta base o registro é o
  produto. A forma escolhida é um `Record<CaveatPlacement, …>` **exaustivo no
  teste**: o `tsc` passa a enumerar a união de graça, membro novo não compila até ser
  classificado, e o guarda afirma que todo membro que é posição de render tem pelo
  menos um código apontando para ele;
- **a varredura da terceira ocorrência foi FEITA, e fechou:** os **30** aliases de
  tipo de `apps/frontend/src` foram varridos **pela forma**, e **nenhum membro de
  nenhuma união da classe está sem consumidor de produção hoje**. A saída colada está
  no `tasks.md`. A #89 fecha com resultado, não com omissão;
- **e a varredura achou o precedente da casa**: `HeatStep` já tem a lista como valor
  (`HEAT_STEPS`, `heatScale.ts:21`) — a forma que a #89 propõe já existe aqui, e ela
  **não é exaustiva**, o que é o argumento final da decisão acima.

### Documentação — e ela fecha nesta change, sem PR de cauda

- **`01-ARQUITETURA_E_CONVENCOES.md`** ganha a classe como convenção: *declaração
  viva sem consumidor*, com as **duas formas de sobrevivência opostas** que as duas
  ocorrências mostram — a da #83 **tem teste verde que mantém o código morto vivo**,
  a da #89 **não tem teste nenhum** e uma união de tipo não é varrível em tempo de
  execução. É o conteúdo exclusivo da #89, movido antes de ela fechar;
- **`02-HISTORICO_E_STATUS.md`** — a entrada desta change e a vigésima quinta
  medição da convenção 18;
- **`CHANGELOG.md`**.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `system-insights-ui`: o requisito *"Métrica aprovada no protótipo e sem fonte não
  é inventada, e a lacuna é declarada onde há elemento para declará-la"* passa a
  afirmar que a lacuna declarada tem **uma forma só** — a do subtítulo —, e que **não
  existe forma que renderize quadro**. Hoje ele diz que a tela não apresenta o
  quadro; passa a dizer que a tela não **tem como** apresentá-lo. A diferença é a
  armadilha: proibir o resultado deixa o caminho aberto, remover a forma o fecha.

**`agent-insights-ui` NÃO ganha delta**, e é decisão: o requisito de lá já difere
para esta regra (*"o sistema SHALL seguir a regra já fixada para esta família de
telas"*). Escrever a mesma afirmação nos dois criaria segunda fonte de verdade para
uma regra só.

## Impact

**Código — `apps/frontend`, e só ele**

- `src/features/insights/components/DeclaredGap.tsx` — a prop, o branch, o atributo
  e o bloco de comentário da variante;
- `src/features/insights/components/DeclaredGap.test.tsx` — o guarda central novo, a
  remoção do caso do `block` (**categoria "removidos"**) e a adaptação do caso do
  `inline`;
- `src/features/insights/components/InsightsKpiGrid.tsx` e
  `src/features/insights/components/AgentModelsCard.tsx` — a prop sai da chamada;
- `src/features/insights/components/InsightsKpiGrid.test.tsx` e
  `src/features/insights/components/AgentModelsCard.test.tsx` — os guardas de
  `data-gap-variant` passam a afirmar a ausência da moldura;
- `src/features/insights/utils/caveatLabels.test.ts` — o guarda exaustivo da #89.

**Contrato e rota**

- **Nenhuma alteração em `apps/api`, `apps/workers` ou `apps/inbox`**, em nenhuma
  rota e em nenhum tipo de resposta. A change não lê dado nenhum: é forma de
  componente de apresentação.

**Documentação**

- `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md`, `CHANGELOG.md`;
- `openspec/specs/system-insights-ui/spec.md`, pelo delta.

**Dependências:** nenhuma.

## Achados

**`theme.test.ts:147` redeclara `HEAT_STEPS` em vez de importar o exportado**, e
alimenta com a cópia dois `it.each` que afirmam as variáveis `--buteco-heat-N`. Se a
escala ganhar um passo, a cópia continua com seis e os dois casos passam verdes
cobrindo menos — **a mesma mecânica de camuflagem da #83, por duplicação em vez de
orfandade**. **Não é escopo desta change** (outro arquivo, outra feature, outra
causa), e **vira issue própria antes do fechamento**, com a abertura dependendo do
dono. Detalhe na D5 do `design.md`.

## Non-Goals

- **Não implementar a #85 nem a nota por grupo da #90.** A #90 foi lida para decidir
  a forma, e o que ela diz está registrado acima — ler não é implementar;
- **Não tocar `apps/api`, `apps/workers`, `apps/inbox` nem o `nginx.conf`**;
- **Não mexer nos cards que a #75 e a #84/#86 acabaram de mudar**, salvo o que o
  `DeclaredGap` alcança — e ele alcança dois sítios, nenhum deles naqueles cards;
- **Não transformar `CaveatPlacement` em `const` array**, que é a forma que a #89
  propõe e que esta change recusa com motivo;
- **Não varrer as uniões dos outros três apps.** A varredura é de `apps/frontend`,
  que é onde as duas ocorrências aconteceram, e o escopo dela está colado no
  `tasks.md`.
