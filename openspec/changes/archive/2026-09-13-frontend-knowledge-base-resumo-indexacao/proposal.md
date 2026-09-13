## Why

O catálogo de bases de conhecimento não responde as duas perguntas que levam
alguém a abri-lo: **tem conteúdo?** e **está utilizável?**. A etapa 5a-1 recusou
as colunas `Documentos` e `Indexação` e a quarta opção de filtro (`Com falha`)
porque obtê-las exigiria uma requisição **por base**, contra as 100+ bases que o
handoff declara como volume real (`design.md` da 5a-1, D1 e D9).

**O argumento morreu com a chegada da rota.** A etapa 2b entregou
`GET /knowledge-bases/indexing-summary`, que devolve `documentCount`,
`indexedCount` e `failedCount` de **todas** as bases numa única requisição, com
custo independente do número de bases. A rota está no ar e **sem consumidor
nenhum desde 11/09/2026** — a 5a-2 a conferiu presente e adiou de propósito, com
posição na fila e não com gatilho (`design.md` da 5a-2, D1).

Esta change não contorna o requisito negativo que hoje proíbe as colunas: ela o
**modifica**, e a redação nova registra o que mudou — não a regra, mas o dado.

## What Changes

Tudo em **`apps/frontend`**. Nenhum arquivo de `apps/api`, `apps/workers` ou
`apps/inbox` é tocado, e nenhuma rota nova é pedida.

- **Coluna `Documentos`** no catálogo de bases, a partir de `documentCount`.
  Contagem em zero é exibida como `Nenhum`, e isso é deliberado: o zero do resumo
  é uma contagem **medida** — a agregação percorreu os documentos e não achou
  nenhum —, ao contrário de `fragmentCount` em documento nunca indexado, que é o
  default de uma coluna que ninguém escreveu.
- **Coluna `Indexação`** no catálogo, a partir de `indexedCount`, `failedCount` e
  do complemento não terminal obtido por subtração. O protótipo distingue
  `pendente` de `indexando` nessa coluna; a rota **não** distingue, e a coluna
  passa a dizer o que o sistema sabe.
- **Quarta opção do filtro, `Com falha`**, sobre `failedCount > 0`.
- **Tom de alerta só na parcela de falha.** O protótipo pinta a célula inteira
  quando há falha **ou** qualquer documento não terminal; documento indexando é o
  fluxo normal, e marcá-lo como atenção é afirmar problema onde não há.
- **Consulta nova `useKnowledgeBaseIndexingSummaryQuery`**, em paralelo com
  `GET /knowledge-bases`, com o cruzamento no cliente feito **por identificador**
  e nunca por posição.
- **Degradação explícita quando o resumo não responde**: a listagem continua
  servindo sem as duas colunas, que não afirmam ausência; e a opção `Com falha`
  fica desabilitada em vez de filtrar para o vazio — oferecê-la inerte é o que a
  5a-1 já recusou.
- **BREAKING (de spec, não de contrato)**: o requisito negativo hoje vigente em
  `knowledge-base-catalog-ui` — *"a listagem SHALL NOT exibir contagem de
  documentos nem resumo de indexação"* — e o *"exatamente três opções"* do filtro
  deixam de valer. Nenhuma API muda; nenhum consumidor externo é afetado.

## Capabilities

### New Capabilities

Nenhuma. A tela existe e a capability que a governa também.

### Modified Capabilities

- `knowledge-base-catalog-ui`: dois requisitos mudam de sinal e um é
  acrescentado. **Listagem de bases de conhecimento** deixa de proibir contagem
  de documentos e resumo de indexação e passa a exigi-los, com a justificativa do
  zero medido e o tratamento de resumo indisponível; **Busca e filtro por estado
  na listagem de bases** deixa de fixar três opções e passa a quatro, com o
  comportamento da quarta quando o resumo não está disponível; e entra
  **Tom de alerta no resumo de indexação da listagem**, requisito novo porque o
  comportamento é observável e não existia antes — sem ele a decisão de separar
  falha de progresso ficaria só no `design.md` e sumiria no archive, para a
  próxima change que tocar a tabela reintroduzir.

## Impact

**App afetado: `apps/frontend`, e só ele.**

| Área | Efeito |
|---|---|
| `features/knowledge-bases/types/` | tipo novo do item de resumo, espelhando o response |
| `features/knowledge-bases/api/` | função de listagem e consulta nova; `request<T>` da própria feature, sem cliente compartilhado (convenção 7) |
| `features/knowledge-bases/utils/` | função pura do cruzamento por id e da apresentação das duas colunas |
| `features/knowledge-bases/components/KnowledgeBaseTable.tsx` | duas colunas novas; passa de três para cinco |
| `features/knowledge-bases/pages/KnowledgeBaseListPage.tsx` | busca o resumo e repassa por propriedade; quarta opção de filtro |
| `openspec/specs/knowledge-base-catalog-ui/spec.md` | dois requisitos `MODIFIED` |

**Dependência de backend: nenhuma pendente.** `GET /knowledge-bases/indexing-summary`
existe em `apps/api`, com o formato de fio conferido por teste que inspeciona o
**texto** do JSON (`KnowledgeBaseIndexingSummaryTests.SummaryResponse_UsesCamelCaseFieldNamesOnTheWire`)
e com a ordenação com desempate explícito verificada por captura do SQL emitido.

**Dependência nova de pacote: nenhuma.**

**Fora de escopo**, e cada um com o motivo no `design.md`: os badges `Falha` e
`Nada indexado` da coluna `Estado` do protótipo, o acompanhamento por polling do
catálogo, e a tela de diagnóstico do índice (5c), que continua bloqueada por um
passo de `apps/api` que ainda não foi proposto.
