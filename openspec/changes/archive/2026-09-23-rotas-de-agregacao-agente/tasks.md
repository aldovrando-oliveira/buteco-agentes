## 1. A rota, o contrato herdado e o `404`

- [x] 1.1 (`apps/api`) `GET /insights/agents/{id:guid}` acrescentada ao
      `MapInsightsEndpoints` que **já existe** — `Program.cs` **não** é tocado
      (`Program.cs:133` já chama o mapeamento). Assinatura no molde da rota do
      sistema: `from`/`to` como `string?`, resolvidos por `InsightsPeriod`
      **reusada como está** (D4). **Não** acrescentar à allowlist de rotas
      anônimas — entrar lá reprova o boot (D13).
- [x] 1.2 (`apps/api`) Ordem das checagens, e ela é contrato: **janela primeiro,
      existência depois** (D3). Janela inválida para id inexistente responde
      `400`, não `404`.
- [x] 1.3 (`apps/api`) Consulta de existência em `agents` — a **única** leitura
      que a rota faz dessa tabela, e nenhum campo do agente entra no agregado.
      `Results<Ok<AgentInsightsResponse>, NotFound, ValidationProblem>`, no molde
      de `GetAgentByIdAsync`.
- [x] 1.4 **Guardas do `404`, com o par que o torna discriminante**: id
      inexistente responde `404`; agente **existente e sem dado no período**
      responde `200` com contagens `0` e não coletados ausentes. Sem o par, um
      `404` para tudo passaria.
- [x] 1.5 **Guarda do agente inativo**: existe e está inativo → `200`, nunca
      `404`. `Agent.IsActive` existe (`Agents/Entities/Agent.cs:11`), e é onde o
      erro cabe.
- [x] 1.6 **Guarda de autenticação**: sem token responde `401`, sem que nada tenha
      sido declarado para isso.

## 2. O agregado por agente — as 17 que são filtro

- [x] 2.1 (`apps/api`) `GetAgentInsightsQuery` e `GetAgentInsightsQueryHandler`,
      no molde do handler do sistema: toda agregação **no banco**, SQL cru pelo
      mesmo motivo (`AT TIME ZONE` com o nome vindo por parâmetro e
      `percentile_cont` não têm tradução em LINQ).
- [x] 2.2 (`apps/api`) `AgentInsightsResponse` **próprio**, e **não** o
      `SystemInsightsResponse` reaproveitado (D5): o DTO do agente **não tem
      campo** para M13 nem para M30, porque lista vazia é o texto de "medi e não
      achei nada" e afirmaria medição onde não há fonte.
- [x] 2.3 (`apps/api`) As **17 métricas de filtro** do mapa — M1, M2, M6, M7, M9,
      M10, M11, M12, M17, M21, M22, M23, M24, M25, M27, M29, M32 —, com
      `AgentId` do agente e os joins ao pai onde a tabela filha não tem coluna
      temporal própria.
- [x] 2.4 (`apps/api`) O balde em `AT TIME ZONE` sobre as linhas **já restritas
      pela janela**, com o nome do fuso vindo de `MetricsOptions` (reusada, D4).
      Nenhum índice de expressão.
- [x] 2.5 **Guarda do balde local, próprio desta rota e não herdado**: instante
      noturno que cai em outro dia **e** outro dia da semana em UTC é contado no
      dia local, e o guarda **reprova contra balde em UTC**. É consulta nova; o
      verde da A não cobre esta.
- [x] 2.6 **Guarda do recorte**: com dois agentes semeados na janela, o agregado
      de um **não** inclui nenhuma ocorrência do outro.
- [x] 2.7 **Guarda de nulo, negativo**: token não reportado chega ausente/nulo e a
      asserção afirma que **não é `0`**. O par positivo é o `200` vazio da 1.4.
- [x] 2.8 **Guarda de vacuidade**: todo cenário roda sobre banco **povoado**, com
      asserção da precondição contando linhas antes de asserir, no molde de
      `SeededRowCounts` — senão o guarda fica verde com e sem a implementação.

## 3. A assimetria — a decisão central

- [x] 3.1 (`apps/api`) **"Delega para"**: `delegation_outcomes` filtrada por
      `SourceAgentId` do agente, agrupada por `TargetAgentId` e **discriminada por
      `Outcome`** (`Completed`, `TargetUnsuccessful`, `Expired`, `NotStarted` — o
      vocabulário fechado de `ExecutionMetricsValues`). Janela emprestada do pai
      por `SourceTaskId`, porque `delegation_outcomes` **não tem coluna temporal
      utilizável**.
- [x] 3.2 (`apps/api`) **"Acionado por"**: `task_executions` com
      `Origin = 'Delegation'` e `AgentId` do agente, agrupada por `SourceAgentId`.
      Janela pelo `StartedAt` da **própria linha**.
- [x] 3.3 (`apps/api`) Os dois conjuntos chegam **separados** no DTO, um **não**
      derivado do outro, e a resposta **não** os apresenta como espelho. Comentário
      registrando o mecanismo e apontando a D16 de `metricas-execucao-coleta` — é
      registro de mecanismo, e nesta change o comentário é produto (convenção 18).
- [x] 3.4 (`apps/api`) A assimetria declarada como **parcialidade nomeada** junto
      do conjunto de delegação, com as **duas** causas (D2): resultado que não
      produz execução, **e** os dois relógios de janela.
- [x] 3.5 **O guarda da divergência, e é o guarda central desta change**: cenário
      com `NotStarted` (ou `Expired`) em que "Delega para" e "Acionado por" da
      mesma relação **discordam**, e a asserção **afirma a divergência**. É ele que
      impede alguém "consertar" para bater — um guarda que afirmasse igualdade
      reprovaria o comportamento correto.
- [x] 3.6 **Guardas dos três cenários do protótipo**: agente que **só delega** (o
      outro lado chega vazio), agente que **só é delegado** (idem, invertido), e
      agente que faz **os dois** (os dois chegam preenchidos e separados). Os
      estados vazios são distinguíveis de ausência de medição.

## 4. O que muda de significado e o que não existe

- [x] 4.1 (`apps/api`) **M13 e M30 não entram** — não há campo para elas no DTO
      (D6). M30 porque `knowledge_indexing_attempts` **não tem coluna de agente**;
      M13 porque o recorte já é o agente.
- [x] 4.2 (`apps/api`) **M19 só pelo caminho de busca**:
      `embedding_calls.TaskId` → `task_executions.AgentId`, com
      `Purpose = 'Search'`. O caminho de indexação **não** é alcançado por
      `AgentKnowledgeBases` — o vínculo é de muitos para muitos e contaria a mesma
      indexação em cada agente vinculado, somando mais que o sistema. Comentário
      com a recusa e a razão.
- [x] 4.3 (`apps/api`) **M19 declara a parcialidade** de cobrir só a busca, e
      **M14** declara que o total conversa + embedding perde a metade de
      indexação.
- [x] 4.4 (`apps/api`) **M28 sem o agrupamento por agente** — resta por
      provedor/modelo daquele agente.
- [x] 4.5 (`apps/api`) **M15, M16a, M16b e M26 com o significado declarado no
      comentário e na forma do campo** (D7): mais de um modelo é o agente que
      **mudou** de configuração, não agentes diferentes; a profundidade é a
      **posição na cadeia**, não o tamanho dela.
- [x] 4.6 **Guardas**: M30 e a metade de indexação de M19 **não aparecem** no
      agregado de um agente vinculado a uma base com indexação na janela; M19
      declara a parcialidade; profundidade do agente é **menor** que a do sistema
      quando existe cadeia mais profunda na janela.

## 5. Os regimes e a armadilha herdada

- [x] 5.1 (`apps/api`) O **mapa de regimes** na resposta, reusando
      `MetricsOptions.Regimes`, com cada grupo declarando o seu. A série **omite**
      os dias anteriores ao início do regime; `0` só dentro dele.
- [x] 5.2 (`apps/api`) `ToUniversalTime()` no instante de regime, com o comentário
      apontando o gêmeo de `GetSystemInsightsQueryHandler.cs:85-105` e dizendo
      **por que não é redundante** com a normalização de `InsightsPeriod`: o valor
      de configuração entra por um caminho que nenhum parse cobre, e o Npgsql
      recusa deslocamento ≠ 0 (D10).
- [x] 5.3 **Guarda do instante de regime com deslocamento**: janela que começa
      **antes** do regime, forçando o instante de configuração a virar parâmetro da
      consulta → `200`, não `500`. Conferir que ele **reprova** com o
      `ToUniversalTime()` removido.
- [x] 5.4 **Guarda da série**: janela que começa antes do regime não produz `0` nos
      dias anteriores.

## 6. O fixture, e a 31ª classe de contêiner

- [x] 6.1 (`apps/api`) `AgentInsightsEndpointsTests` com **fixture próprio**, no
      molde de `InsightsEndpointsTests.InsightsFixture`: mesmo `TZ`, mesmos
      regimes, `FakeTimeProvider` com fuso fixo. **Não** reusar o fixture da A — o
      contêiner é campo de instância, o xUnit cria um por classe, e a semeadura da
      A tem guarda de idempotência que tornaria o resultado dependente da ordem
      (D11, quinta forma da convenção 15).
- [x] 6.2 (`apps/api`) Semeadura dos **três cenários do protótipo** mais o agente
      **existente e vazio** e o par de agentes do guarda de recorte, com a
      precondição afirmada por contagem de linhas.
- [x] 6.3 (raiz) **Recalibrar a régua de contenção de `apps/api`** e registrá-la
      com o estado colado (convenção 22): hoje são **30 classes que sobem
      contêiner** (26 por `IClassFixture` e 4 que constroem o seu direto, lido em
      23/09/2026); esta change faz a **31ª**. Recalibrar é tarefa de quem muda o
      estado, não descoberta de quem vier depois.

      **E o registro diz para que o número serve**, senão é valor que ninguém
      consulta: **quando é consultado** (suíte lenta, suíte reprovando em bloco na
      inicialização de fixture, ou decisão de acrescentar mais uma classe com
      contêiner); **o que permite concluir hoje** — nada além de registrar o
      crescimento, porque não há limiar, e dizer isso é resultado e não lacuna; e
      **o que NÃO permite** — concluir que `apps/api` esteja perto da parede que
      `apps/workers` encontrou na 13ª classe, porque lá são **2 contêineres por
      classe e classes serializadas** e aqui é **1 contêiner por classe em
      paralelo** (`WorkerInfrastructureFixture.cs:11`/`:17` contra
      `ApiFactoryFixture.cs:67`; `apps/api` não tem `CollectionDefinition`
      nenhuma). **Com a menção cruzada à régua de `apps/workers`** — são a mesma
      família mantida à mão em dois apps, e tratá-las como assuntos separados é o
      que produziu este estado.

## 7. Documentação e registro

- [x] 7.1 (`apps/api`) **Corrigir a prosa da change A** (D14): o XML doc de
      `InsightsEndpointsTests.cs:75-79` diz *"domingo 14/09"* e **14/09/2026 é
      segunda-feira** — as constantes do próprio arquivo já dizem `Monday`/
      `Tuesday`. **Uma palavra**, sem mudança de comportamento e sem teste novo.
      Feito aqui porque esta change copia esse molde para o seu guarda de balde.
- [x] 7.2 (raiz) `02`: o **mapa das 27 no escopo do agente**, com o veredito por
      métrica — 17 filtro, 4 significado, 4 outra consulta, 2 não existem — e a
      frase que a próxima etapa precisa: **nove não transferem por analogia**.
- [x] 7.3 (raiz) `02`: a **assimetria com as duas causas**, e que a segunda — os
      dois relógios de janela — é achado desta change, não da D16. É o que a
      etapa 5 precisa receber **antes** de desenhar.
- [x] 7.4 (raiz) `02`: as **duas parcialidades novas** deste escopo (M19 só busca,
      M30 inexistente) e a **recusa do caminho por `AgentKnowledgeBases`**, com a
      razão — senão a etapa seguinte a redescobre e pode aceitá-la.
- [x] 7.5 (raiz) `02`: item com gatilho — **M27 somar as duas fontes**, que este
      escopo torna quantificável (`a2a_tasks` tem `agent_id`) sem tornar
      resolvida. *Gatilho:* a change de coleta do motivo das recusas fechar.
      *Posição:* dentro dela, decidindo para **os dois escopos ao mesmo tempo**.
- [x] 7.6 (raiz) `02`: registrar que os protótipos **continuam sem ser abertos** —
      a recusa do MCP do Claude Design (`FIRST_PARTY_AUTH_REJECTED`, HTTP 403) se
      repetiu nesta sessão. *Gatilho:* `/design-login` rodado pelo dono.
      *Posição:* antes de a etapa 5 começar.
- [x] 7.7 (raiz) `02`: item com gatilho — **a régua de contenção de `apps/api`
      não tem critério, só contagem**. Decidir então entre **duração com a
      contagem colada** (molde da recalibração de `apps/workers` de 20/09/2026,
      `WorkerHostCollection.cs:25-47`) e ir direto para a `ICollectionFixture`
      que dispensa a régua. **Item de método, não da linha
      `metricas-de-operacao`.** *Gatilho:* a primeira das três situações de
      consulta — suíte lenta, suíte reprovando em bloco na inicialização de
      fixture, ou a **32ª** classe de contêiner. *Posição:* item próprio, **junto
      do item da `ICollectionFixture` de `apps/workers`**, porque são a mesma
      família.
- [x] 7.8 (raiz) `CHANGELOG.md`.

## 8. Verificação e fechamento

- [x] 8.1 **Remedir as três baselines** antes de qualquer comparação. **Não
      herdar** `apps/api` 372/372, `apps/workers` 386/386, `apps/inbox` 202/203.
      Registrar **qual alvo rodou antes** — a régua "202/203" engana, e o flake de
      `apps/inbox` reprova sob contenção e passa isolado (`02:5969-5974`).
- [x] 8.2 **Declarar o estado do `podman ps`** em vez de afirmar zero: o stack de
      desenvolvimento de pé colide com a régua, e foi o que aconteceu no
      fechamento da A (`02:5519-5522`). Se não estiver em zero, dizer quais
      contêineres estavam no ar.
- [x] 8.3 **Guardar a saída COMPLETA** de baseline e de fechamento, em arquivo,
      não filtrada e não resumida (convenção 19). Sem `| grep | head`, que fecha o
      cano e mata o produtor por `SIGPIPE`.
- [x] 8.4 Rodar as três suítes e registrar o resultado com o regime colado. **E,
      para `apps/api`, registrar a DURAÇÃO com a contagem de classes ao lado** —
      é a primeira vez que esse par existe, e é o que torna a régua da 6.3
      consultável em vez de só crescente. A duração já medida (`1 m 21 s` e
      `2 m 18 s` em 23/09/2026, `02:5955-5956`) foi anotada para explicar o flake
      de `apps/inbox` e **não** diz quantas classes havia — que é exatamente o
      defeito da convenção 22 que esta tarefa fecha.
- [x] 8.5 **Conferir a assinatura compilando todos os `.csproj`** — não há `.sln`
      na raiz, e compilar só um projeto não pega quebra de assinatura.
- [x] 8.6 **Verificação por mutação (convenção 15, quinta forma)**, no molde que a
      A usou e funcionou — **três mutações, cada uma derrubando só os guardas que
      a guardam.** Se uma derrubar metade da suíte, metade dos guardas está
      testando outra coisa.

      | mutação | deve derrubar |
      |---|---|
      | "Acionado por" passa a ler `delegation_outcomes` por `TargetAgentId` | **só** o guarda da divergência (3.5) |
      | balde da rota nova trocado para `UTC` | **só** o guarda de balde local (2.5) |
      | `404` trocado por `200` com agregado vazio | **só** o guarda de agente inexistente (1.4) |

- [x] 8.7 **Fechamento da convenção 18 (décima quinta medição).** Comparar contra
      a projeção do `design.md`, **sem inventar correção**: **4 criados, 4
      modificados, ~1.410 linhas, 0 geradas, 15 casos**. `git diff -w` para
      modificado; duplo contado **por natureza e não por arquivo**; unidade em
      **casos**, não métodos. A pergunta desta medição é se **projetar o par só
      onde o oposto é exprimível** derruba o erro em casos para a faixa das linhas
      (±11%) — a décima terceira subprojetou 34%, a décima quarta superprojetou
      62%. Registrar no `02`.
- [x] 8.8 **Conferência de escopo de arquivo contra a lista fechada abaixo.**
      Arquivo tocado fora da lista é achado a registrar, não a absorver em
      silêncio.

### A lista fechada de arquivos

**Montada por leitura do repositório, não por analogia com a lista da change A** —
foi por analogia que o `docker-compose.yml` entrou na lista dela sem ser tocado.

**`apps/api` — criados (4):**

```
src/Buteco.Api/Insights/Queries/GetAgentInsights/GetAgentInsightsQuery.cs
src/Buteco.Api/Insights/Queries/GetAgentInsights/GetAgentInsightsQueryHandler.cs
src/Buteco.Api/Insights/Responses/AgentInsightsResponse.cs
tests/Buteco.Api.Tests/AgentInsightsEndpointsTests.cs
```

**`apps/api` — modificados (2):**

```
src/Buteco.Api/Insights/Endpoints/InsightsEndpoints.cs      (+ 1 rota, + 1 método)
tests/Buteco.Api.Tests/InsightsEndpointsTests.cs            (uma palavra — D14, tarefa 7.1)
```

**Documentação — modificados (2):**

```
02-HISTORICO_E_STATUS.md
CHANGELOG.md
```

**Fora da lista, e cada exclusão conferida no repositório:**

| arquivo | por que a analogia com a A o sugeriria | por que **não** entra |
|---|---|---|
| `src/Buteco.Api/Program.cs` | a A o modificou | `:133` **já** chama `MapInsightsEndpoints()`; a A o tocou para registrar `TimeProvider` e a checagem de boot, que não se repetem |
| `01-ARQUITETURA_E_CONVENCOES.md` | a A o modificou | a A mexeu na seção *"Fuso horário do sistema"*; esta change não mexe em fuso, e o `01` **não tem inventário de rotas** — a palavra `insights` não aparece no arquivo |
| `apps/frontend/deploy/nginx.conf` | é o arquivo que a change anterior consertou | `:59` já traz `insights` com `(/\|$)`, e `server-deployment/spec.md:217-222` tem cenário citando `/insights/agents/{id}` pelo nome |
| `docker-compose.yml`, `docker-compose.prod.yml`, `.env.example` | a A os modificou | `TZ` e os regimes já estão entregues desde a A, e são os **mesmos** valores — a rota nova lê a mesma seção `Metrics` |
| `src/Buteco.Api/Options/MetricsOptions.cs` | é o que guarda fuso e regimes | **reusada como está** (D4) |
| `src/Buteco.Api/Insights/InsightsPeriod.cs` | é o contrato de janela | **reusada como está** — a A a extraiu com esta change declarada no comentário (`:8-13`) |
| `src/Buteco.Api/Insights/.../GetSystemInsights*` e `SystemInsightsResponse.cs` | é o molde | a rota do sistema **não é alterada** (Non-Goal) |
| qualquer migração | — | a change é **somente leitura**: nenhuma tabela, coluna ou índice |
| `apps/workers`, `apps/inbox`, `apps/frontend` | — | nenhuma mudança de comportamento lá; as telas são etapas 4 e 5 |
