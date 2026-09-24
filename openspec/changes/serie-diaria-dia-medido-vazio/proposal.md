**Issue:** #65 · **Bloqueia:** #52

## Why

A spec de `system-insights-aggregation` exige **duas** metades da distinção que a
linha `metricas-de-operacao` existe para preservar:

> *"A série diária SHALL omitir — nunca emitir `0` para — os dias anteriores ao
> início do regime a que a métrica pertence. **Dentro do regime, um dia sem
> ocorrência SHALL emitir `0`.**"*

**A segunda metade não está implementada.** A consulta de M10 é um `group by`
sobre as linhas de `task_executions` que existem na janela, sem
`generate_series` — então um dia **dentro** do regime, medido e sem nenhuma
task, é omitido exatamente como um dia anterior ao regime. As duas ausências
ficam idênticas, e a distinção deixa de existir na ponta que a consome.

**Confirmado contra o corpo real da rota**, em 23/09/2026: janela de 24/08 a
23/09, regime de execução em 22/09 01:21 -03. A resposta trouxe
`dailySeries: []` — 29 dias não medidos e 2 dias medidos e vazios sumiram
juntos, com `volume.executedTaskCount: 0` provando que os dois últimos foram
medidos.

**E o defeito está em dois lugares, não em um.** A conferência que esta proposta
fez — e que não estava no enunciado — mostra que `GET /insights/agents/{id}` tem
**consulta própria**, não compartilhada, com o mesmo `group by` e o mesmo
defeito. O comentário do próprio código já avisava que o verde de uma rota não
cobre a outra.

## What Changes

- **`generate_series` na série diária**, nos **dois** handlers, sobre os dias
  locais entre o início efetivo do regime e o fim da janela. Todo dia medido
  passa a ter ponto; a ausência de um dia passa a significar **uma** coisa: não
  medido.
- **O mesmo tratamento para o mapa por dia da semana**, que tem o defeito
  idêntico e que a spec **não** cobre hoje. Um dia da semana que ocorreu na
  faixa medida e não teve task passa a chegar com `0`; um que não ocorreu
  continua ausente.
- **O dia de pico deixa de ser eleito quando não há medição.** É uma regressão
  que a correção **introduziria** se não fosse tratada: com sete linhas de `0`,
  o `MaxBy` atual devolveria domingo como pico de um período sem nenhuma task.
- **O limite superior do intervalo gerado é recortado pelo instante atual**, e
  não só pelo `to` pedido — senão a correção emite `0` para dias futuros,
  afirmando medição que ainda não aconteceu. É o caso simétrico do limite
  inferior, e não estava no enunciado do defeito.
- **O guarda do par positivo**, que faltou e que é o motivo de o defeito ter
  sobrevivido ao archive. Ele reprova contra `HEAD`.
- **O guarda da metade negativa deixa de passar por vacuidade**: hoje ele fica
  verde com a série vazia, e passa a afirmar a precondição.
- **O XML doc de `DailyInsightPoint` é corrigido com a causa** — ele afirma que
  *"só existem pontos para dias dentro do regime"*, o que só será verdade depois
  desta change.

## Capabilities

### New Capabilities

*(nenhuma — esta change corrige o comportamento de duas capabilities existentes)*

### Modified Capabilities

- `system-insights-aggregation`: o requisito da distinção entre período anterior
  ao regime e período sem uso ganha o **cenário do par positivo**, que nunca
  existiu, e passa a cobrir explicitamente o **mapa por dia da semana** e a
  **ausência de pico** quando nada foi medido.
- `agent-insights-aggregation`: hoje a capability do escopo do agente declara
  apenas a metade negativa — *"a série diária SHALL omitir … os dias anteriores
  ao início do regime"*. A metade positiva **nunca foi especificada** para este
  escopo, e é por isso que o defeito ali não é sequer uma violação de contrato:
  é contrato faltando. Passa a declarar as duas metades, o dia da semana e o
  pico, como o escopo do sistema.

## Impact

**App afetado: `apps/api`, e só ele.**

- **Código modificado:** `Insights/Queries/GetSystemInsights/GetSystemInsightsQueryHandler.cs`
  e `Insights/Queries/GetAgentInsights/GetAgentInsightsQueryHandler.cs` — o método
  `TemporalAsync` de cada um. As duas cópias continuam **copiadas, não
  extraídas**, pelo mesmo motivo registrado quando nasceram; o comentário de cada
  uma aponta a outra.
- **Contrato de resposta inalterado.** `DailyInsightPoint` e
  `WeekdayInsightPoint` não mudam de forma — muda **quantas** linhas chegam.
  Nenhum campo novo, nenhum campo removido, nenhuma quebra para consumidor
  existente.
- **`TimeProvider` passa a ser injetado nos dois handlers**, para o recorte
  superior. Ele já está registrado em `apps/api` desde a
  `rotas-de-agregacao-sistema`, e a alternativa — `now()` no SQL — foi
  explicitamente recusada lá, por tornar a janela não verificável de forma
  determinística.
- **Guardas:** `InsightsEndpointsTests` e `AgentInsightsEndpointsTests`. O seed
  atual **já contém** o caso que faltava — há execuções nos dias locais 14/09 e
  16/09, e **15/09 é um dia medido e vazio dentro do regime**. O guarda ausente
  podia ter sido escrito com os dados que já estavam lá.
- **Sem migração, sem índice, sem coluna, sem `nginx.conf`, sem pacote novo.**

**Não afetados:** `apps/frontend` (é a #52, que esta desbloqueia), `apps/workers`,
`apps/inbox`, `libs/`.
