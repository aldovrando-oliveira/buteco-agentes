**Issue:** #53 · **Última etapa** da linha `metricas-de-operacao` · **Informa:** #67
(L4) · **Achados com issue própria:** ver *Achados* abaixo

## Why

`GET /insights/agents/{id}` está mergeada e verificada em produção desde a etapa
3, e **nada dela é visível**. A rota devolve o recorte por agente de 25 das 27
métricas do catálogo, dois conjuntos de delegação com fontes diferentes, um mapa
de regimes e seis códigos de parcialidade — e enquanto não houver aba, esse
trabalho não chega a quem opera um agente específico.

E há uma coisa que **só esta tela pode preservar ou destruir**: a rota entrega os
dois lados da delegação como conjuntos separados, de fontes diferentes, que
**podem mostrar números diferentes para a mesma relação entre os mesmos dois
agentes**. Desenhá-los como espelho — a mesma relação vista de dois ângulos —
transforma um resultado correto em defeito aparente, e o operador passa a
procurar erro onde há medição. A change B existe para impedir isso do lado do
contrato; esta existe para impedir do lado da tela.

## What Changes

- **Nova aba `Insights` no detalhe do agente** (`/agents/{id}?tab=insights`), em
  `apps/frontend`, servida por **uma** requisição a `GET /insights/agents/{id}` —
  montada a partir dos artboards aprovados `Agente-Insights.dc.html`,
  `Agente-Delegado.dc.html` e `Agente-Misto.dc.html`, mais o **quarto cenário sem
  artboard** (agente que não delega nem é delegado), que a nota `cenarios` do
  `canvas.json` fixa como *"o mesmo card com as duas seções tracejadas"*.
- **A assimetria da delegação vira comportamento verificável.** As duas seções —
  *Delega para* e *Acionado por* — são apresentadas como conjuntos de **fontes
  distintas**, nunca como duas vistas da mesma contagem, e o guarda **afirma a
  divergência**: um guarda que afirmasse igualdade reprovaria o comportamento
  correto. O `caveat` `delegation-sides-are-not-mirrors` é renderizado dentro do
  card que ele limita.
- **Cadastro e uso ficam distinguíveis nas duas seções.** *"Sem delegação
  cadastrada"* (o tracejado) e *"vínculo cadastrado que não foi usado no
  período"* (linha com contagem `0`) são fatos diferentes — o próprio
  `Agente-Delegado.dc.html` escreve isso — e a distinção só existe cruzando o
  agregado com o catálogo de agentes, que a rota de métricas não devolve.
- **Os rótulos das métricas que mudam de significado são reescritos**, nunca
  copiados da página do sistema: profundidade é **posição na cadeia** e não
  tamanho dela; mais de um provedor ou modelo na distribuição é **o agente que
  mudou de configuração**, nunca comparação entre agentes.
- **Os três estados de resposta da rota ficam distinguíveis na aba**: `404`
  (agente inexistente), `200` com tudo zerado (agente que existe e não tem dado)
  e `200` de **agente inativo** (que continua consultável). Nenhum colapsa no
  outro, e nenhum colapsa no travessão de *"a consulta não respondeu"*.
- **A gramática dos quatro estados é herdada, não redefinida** — `Estados.dc.html`
  continua sendo o contrato, e os módulos que a carregam (`metricState`,
  `MetricValue`, `measuredDays`, `DeclaredGap`, `PartialMeasurementNotice`,
  `PeriodPicker`, `insightsWindow`) são **reusados**, não recriados.
- **A repetição deixa de ser prevista e passa a ser observada** (convenção 2). A
  #52 foi o primeiro consumidor e esta é o segundo: `KpiCard` sai de dentro de
  `InsightsKpiGrid` para módulo próprio, e `WeekdayActivityCard` ganha a nota de
  rodapé que cada cenário exige. Nada é extraído por previsão — só o que **duas**
  superfícies já usam.
- **Os dois códigos de parcialidade novos deste escopo entram no mapa fechado**
  (`embedding-covers-search-only` e `delegation-sides-are-not-mirrors`), e a
  **posição** de cada código passa a ser por superfície: o mesmo código limita
  elementos diferentes na página do sistema e na aba do agente.
- **As divergências com o protótipo são registradas, não implementadas em
  silêncio** (convenções 9 e 17). Sete estão nomeadas no `design.md`, cada uma
  com o que a causa, o que a tela faz no lugar e o gatilho para voltar.

**Nenhuma mudança de backend.** `apps/api`, `apps/workers`, `apps/inbox`,
`libs/` e o `nginx.conf` ficam intocados — a rota já serve tudo deste escopo, o
prefixo `insights` já roteia desde a etapa 3 e a aba não cria rota de página
nova: ela é um parâmetro de consulta sobre `/agents/{id}`, que o bloco do
`Sec-Fetch-Mode: navigate` já cobre.

## Capabilities

### New Capabilities

- `agent-insights-ui`: a aba Insights no detalhe do agente em `apps/frontend` —
  a apresentação dos dois lados assimétricos da delegação, a distinção entre
  vínculo cadastrado e vínculo usado, os quatro cenários de delegação com a
  mesma estrutura, as três respostas distintas da rota (`404`, `200` zerado,
  agente inativo), os rótulos próprios das métricas que mudam de significado
  neste escopo, a omissão do que este escopo não sustenta e a posição dos
  códigos de parcialidade nesta superfície.

### Modified Capabilities

- `agent-catalog-ui`: o detalhe do agente passa de **quatro** para **cinco**
  abas. A quinta é Insights, entra depois de delegações e **não** tem contador —
  não há vínculo a contar, e um número ali afirmaria uma quantidade que a aba não
  representa.

## Impact

**App afetado: `apps/frontend`, e só ele.** Nenhuma tarefa desta change roda em
`apps/api`, `apps/workers` ou `apps/inbox`.

- **Código novo:** `src/features/insights/` ganha o contrato do escopo do agente
  (`types/agentInsights.ts`), o hook de consulta, os utilitários puros do
  cruzamento de delegação e dos dois vocabulários novos (resultado de delegação
  e motivo de recusa), sete componentes de apresentação e o componente da aba.
- **Código modificado:** `src/features/insights/api/insightsApi.ts` (a função da
  rota nova), `utils/caveatLabels.ts` (dois códigos novos e a posição por
  superfície), `components/InsightsKpiGrid.tsx` (o `KpiCard` extraído),
  `components/WeekdayActivityCard.tsx` (nota de rodapé opcional) e
  `src/features/agents/pages/AgentDetailPage.tsx` (a quinta aba).
- **Dependência de segunda consulta, já paga:** a rota de métricas devolve
  `targetAgentId` e `sourceAgentId`, **nunca o nome**. O nome — e o registro de
  quais vínculos existem — vem de `useAgentsQuery`, que a página de detalhe
  **já consulta hoje**, sem requisição nova. É o mesmo cruzamento que
  `AgentConsumptionCard` faz na página do sistema.
- **Sem dependência nova de pacote.** Barras e proporções saem em CSS, como no
  protótipo e como na #52; nenhuma biblioteca de gráficos entra.
- **Verificação:** a suíte de `apps/frontend` roda em jsdom, que não enxerga cor,
  contraste, layout nem quebra de linha. **A conferência manual nos dois esquemas
  é tarefa do dono** (convenção 14); o agente produz os estados a conferir. O
  precedente que justifica o custo é a `frontend-mensagem-recusa-ciclo`, que pegou
  um estado intermediário que nenhum guarda pediria.

**Não afetados, e a razão:** `apps/api` (a rota já serve o contrato desta aba;
achado de UI é sequenciado, não corrigido aqui — convenção 1), `apps/workers` e
`apps/inbox` (não participam), `libs/` (nada a compartilhar),
`apps/frontend/deploy/nginx.conf` (verificado na linha 59: `agents` e `insights`
já estão no bloco do `api`, e a navegação é coberta pelo `rewrite`),
`src/app/routes.tsx` (a aba é parâmetro de consulta, não rota).

## Achados que viram issue nesta change (convenção 23)

Descobertos ao ler o repositório para montar esta proposta, e **fora do escopo
dela**:

- **A página do sistema não conhece o que a #51 entregou.** `apps/api` passou a
  servir `errors.rejectedAtEntryCount`, `errors.rejectionsByReason` e
  `errors.rejectionRegime` na change `recusa-motivo-coleta`, mergeada em 26/09, e
  `src/features/insights/types/systemInsights.ts` não tem nenhum dos três — a
  contagem de recusa de entrada e os motivos não chegam à tela do sistema, que é
  exatamente a lacuna L1 que a #51 existia para fechar. **Issue nova**, em
  `apps/frontend`, na página do sistema.
- **`rejection-reason-not-collected` é entrada morta no mapa fechado de
  `caveatLabels.ts`.** O código saiu dos dois handlers de `apps/api` com a #51 e
  os testes de lá afirmam a ausência dele; o mapa do painel continua declarando-o.
  Vai junto da issue acima, porque é a mesma causa.
