**Issue:** #67 (fecha) · **Decisão do dono:** caminho **(2)**, o ranking continua
ranking · **Resíduo de decisão** da linha `metricas-de-operacao`, não etapa nova
· **Achados com issue própria:** ver *Achados* abaixo

## Why

A **#67** perguntava se as três colunas que o protótipo desenha e a rota do
sistema não serve — **Tasks**, **Tokens por task**, **Duração p95** — deviam
entrar no ranking do sistema **(1)** ou ficar só na aba do agente **(2)**. O
gatilho dela era a #53, que mergeou e **está em produção**. A pergunta foi
respondida por medição, e a resposta é **(2)**.

**Fechar pelo (2) não é não fazer nada.** A decisão transforma três ausências em
**escolhas**, e três artefatos do repositório continuam afirmando que elas são
**lacunas** — um comentário de código, um gatilho escrito em teste e, o mais
grave, **um requisito da spec que contradiz outro requisito da mesma spec**.
Declarar ausência do que se escolheu não ter é a convenção 13 ao contrário; e
deixar a spec se contradizendo é o que a convenção 9 existe para impedir.

E o argumento que decidiu — *"a profundidade está a um clique"* — **não é
verdade no `HEAD`**: são dois cliques. Uma decisão que se apoia numa passagem
que não funciona é decisão mal fundada até a passagem funcionar.

## What Changes

Pequena, toda em `apps/frontend` mais registro. **Nenhuma linha em `apps/api`.**

- **O link do `AgentConsumptionCard` passa a levar `?tab=insights`.** Hoje liga
  para `/agents/{id}` sem parâmetro, e `parseTab(null)` cai em *"Visão geral"* —
  **dois cliques**, e o argumento do (2) supõe um. É a única mudança de
  comportamento desta change.
- **O requisito *"Consumo por agente"* deixa de exigir a declaração que o
  requisito vizinho proíbe.** A spec sincronizada manda *"declarar, junto da
  tabela, quais colunas não têm fonte"*, com cenário próprio; e o requisito
  *"Métrica aprovada no protótipo e sem fonte…"* manda, para **coluna**,
  removê-la **sem acrescentar elemento** para anunciá-la, porque *"o que
  sobrevive ao archive é a issue"*. **Os dois não podem valer juntos**, a
  implementação segue o segundo desde a décima rodada da #52, e o guarda negativo
  `agentes-lacuna-colunas → null` já afirma isso. A decisão (2) resolve a
  contradição pelo lado certo: a ausência não é lacuna, é escolha.
- **O comentário do topo do `AgentConsumptionCard.tsx` é corrigido.** Ele diz
  *"As três saem, e a lacuna é declarada no rodapé"* — **falso duas vezes**: o
  rodapé foi removido na #52, e a ausência virou decisão. Reescrito com a causa.
- **O gatilho do destaque de falhas é reescrito**, no código e no caso de teste
  que o carrega. Os dois prometem *"o destaque melhora quando a #67 fechar"* —
  fechando pelo (2), a promessa passa a apontar para issue fechada, que é a
  referência sobrevivendo apontando para o vazio. **Gatilho novo:** o primeiro
  pedido do dono por taxa de falha no ranking — e aí o trabalho é a mudança de
  população descrita no `design.md`, não uma coluna. **O critério não muda**, e a
  asserção do caso permanece.

### O que NÃO muda, e é o miolo da decisão

Nenhuma coluna entra na rota do sistema. A tabela continua sendo onde se
descobre **qual** agente olhar; a aba continua sendo onde se descobre **o que
aconteceu com ele**.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `system-insights-ui`: dois requisitos mudam.
  - *"Consumo por agente cruza com o catálogo e declara o que não tem fonte"* —
    **a segunda metade do título sai com a exigência**. Cai o `SHALL` de declarar
    junto da tabela quais colunas não têm fonte, e cai o cenário *"Colunas sem
    fonte são declaradas, não preenchidas"* na metade que exige o texto; a metade
    **negativa** dele (nenhuma coluna preenchida com zero ou com valor de outro
    nível) **permanece**, porque é ela que impede o defeito. E o `SHALL` de
    navegação deixa de ser *"ao detalhe daquele agente"* e passa a ser **à aba de
    Insights daquele agente**.
  - *"Métrica aprovada no protótipo e sem fonte não é inventada…"* — ganha a
    distinção entre **lacuna** (métrica sem fonte, cuja declaração vive na issue)
    e **ausência decidida** (métrica com fonte em **outra** superfície, deixada
    fora desta por decisão registrada). Sem ela, a régua não sabe dizer o que
    fazer quando a issue fecha sem código, que é exatamente o caso aqui.

## Impact

**`apps/frontend`** — e só ele:

- `src/features/insights/components/AgentConsumptionCard.tsx` (o `to` do
  `Anchor`, o comentário do topo, o gatilho do destaque)
- `src/features/insights/components/AgentConsumptionCard.test.tsx` (o caso do
  link, o comentário do caso do destaque, o cabeçalho de seção, um guarda novo)
- `src/features/insights/types/systemInsights.ts` (só o comentário de
  `AgentTokens`: *"é a lacuna L4"* vira ausência decidida)
- `src/features/insights/components/DeclaredGap.tsx` (só o comentário: a variante
  `block` deixa de dizer que vale para a L4)

**Os dois últimos saíram da conferência de escopo**, não da projeção — ver D8 do
`design.md`.

**Registro:**

- `openspec/specs/system-insights-ui/spec.md`, pelo delta desta change
- `02-HISTORICO_E_STATUS.md` — a decisão, os três argumentos medidos, o custo que
  **não** foi a razão, e o gatilho de reabertura
- `CHANGELOG.md`

**Não tocados:** `apps/api`, `apps/workers`, `apps/inbox`, `nginx.conf`, e a aba
do agente, que acabou de ir a produção.

### Achados desta change, com issue própria (convenção 23)

Nenhum é trabalho daqui (convenção 1).

1. **O buraco latente na coluna Falhas — issue nova, e é correção.** As linhas
   da tabela nascem de `tokens.byAgent`, que é `provider_calls join
   task_executions`: agente que executou e **não chamou provedor não tem linha**.
   Medido no banco de dev: **6 das 9 falhas** vêm de execuções sem nenhuma
   chamada de provedor. Um agente cujas falhas fossem todas desse tipo não teria
   linha, e **as falhas dele não apareceriam na página do sistema**. Hoje não
   acontece porque os dois agentes que falham também têm chamadas — é
   coincidência de população, não desenho. **Defeito da tela como ela está,
   independente da #67.**
2. **A variante `block` do `DeclaredGap` é branch morto, e é o `variant`
   DEFAULT — issue nova.** Os dois consumidores do componente passam
   `variant="inline"`; a moldura tracejada de rodapé ficou órfã quando a décima
   rodada da #52 removeu os três rodapés. **Como é o default, a armadilha está
   armada:** um `<DeclaredGap>` novo sem a prop renderiza exatamente o quadro que
   o requisito *"Coluna sem fonte sai sem deixar quadro no lugar"* proíbe.
   Resíduo da #52, não da #67 — ver D8.
3. **O período não viaja entre as telas** — item **(e)** do `02`, herdado da #52.
   As duas guardam o período em `useState` com `DEFAULT_INSIGHTS_PERIOD`: quem
   comparou em 90 dias abre a aba na janela padrão, **sem nada dizendo que a
   janela mudou**. Conferir se já tem issue; se não tiver, abrir. Deixa o `(a)`
   desta change pela metade enquanto durar.
4. **A #80 dobra dentro da #75** — as duas descrevem a mesma tela e a mesma
   causa, e duas issues sobre isso produzem o *"corrigir metade"*. A #75 é mais
   antiga e está em `Ready`. **Antes de fechar a #80, mover para a #75 os dois
   itens que só existem nela**: motivo desconhecido renderizado cru para a soma
   fechar com `rejectedAtEntryCount`, e o registro de que o `caveat` morto já foi
   limpo. E atualizar a #75 com o conferido: `rejection-reason-not-collected`
   **já saiu** de `caveatLabels.ts`, com teste afirmando a ausência; e o item 3 é
   real — `FailuresCard` rotula `errors.rejectedCount` como *"Recusadas na
   entrada"*, que é **a outra população**.
