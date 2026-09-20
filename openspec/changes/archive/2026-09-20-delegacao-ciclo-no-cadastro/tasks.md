Toda tarefa de `dotnet test` roda com as **duas** variáveis de Testcontainers
exportadas (`DOCKER_HOST` do Podman e `TESTCONTAINERS_RYUK_DISABLED=true`) —
sem a segunda a suíte reprova inteira parecendo falha de teste.

## 1. Escopo 2 — carve do defeito pré-existente de `AgentDeactivationTests` (`apps/api`)

Vem **primeiro** porque toda rodada de guardas dos grupos seguintes lê a suíte de
`apps/api`, e uma reprovada conhecida no meio obriga quem lê o vermelho a
refazer a discriminação de três passos (design.md, D9).

- [x] 1.1 Registrar o vermelho de G10 já medido nesta change contra `HEAD`
      `1ba84ec`: suíte inteira 317/318; a classe sozinha 2 aprovados / 1
      reprovado de 3; o teste sozinho aprovado. Conferir que `HEAD` não mudou
      antes de usar os números — se mudou, remedir (convenção 19).
- [x] 1.2 Em `apps/api/tests/Buteco.Api.Tests/AgentDeactivationTests.cs`, trocar
      as três `Assert.Empty(fixture.TaskJobPublisher.PublishedMessages)` (linhas
      25, 75 e 88) por asserção relativa ao agente do próprio teste. **Não** usar
      `Clear()` entre testes — depende de os testes da classe não rodarem em
      paralelo, que é propriedade do runner e não do teste (design.md, D9).
- [x] 1.3 Comentar no arquivo o mecanismo: `AgentDeactivationFixture.TaskJobPublisher`
      é `IClassFixture`, `PublishedMessages` acumula, e
      `SendMessage_AfterReactivation_PublishesNormally` publica de verdade —
      quem rodasse depois dele reprovava. Registrar que a asserção por-agente é
      independente de ordem **por construção**.
- [x] 1.4 Rodar a classe sozinha (`--filter FullyQualifiedName~AgentDeactivationTests`):
      **3 de 3 aprovados**. G10 verde.
- [x] 1.5 Rodar a suíte inteira de `apps/api`: **318/318**. É a nova baseline dos
      grupos seguintes, e é contra ela que todo vermelho a partir daqui é lido.
- [x] 1.6 Registrar como item aberto, com gatilho, que
      `SendMessageProviderRejectionTests` compartilha o formato
      (`AgentProviderRejectionFixture`, mesmo `FakeTaskJobPublisher` acumulando)
      mas hoje não reprova — nenhum dos seus dois testes publica. Gatilho: o
      primeiro teste que publique de verdade naquela classe. **Não corrigir
      agora**: seria correção sem guarda vermelho.

## 2. Escopo 1 — os guardas, vermelhos contra `HEAD`, antes da correção (`apps/api`)

Convenção 15 é a ordem: o guarda reprova contra `HEAD` antes de existir a
correção, e o resultado é registrado com a causa atribuída.

- [x] 2.1 Inverter os dois testes verdes que hoje afirmam o defeito como
      requisito, em `AgentDelegationEndpointsTests.cs`:
      `ReplaceAgentDelegations_BidirectionalPair_IsPermitted` (linha 150) vira
      **G1** (a forma exata de `Probe_CycleAB_A`: `A→B` cadastrado, `B→A`
      recusado) e `ReplaceAgentDelegations_IndirectCycle_IsPermitted` (linha
      135) vira **G2** (`A→B→C→A`).
- [x] 2.2 Acrescentar **G3** (ciclo de quatro saltos `A→B→C→D→A` recusado) e
      **G6** (a recusa por ciclo é atômica: os vínculos anteriores do Source
      ficam intactos, conferidos por `GET /agents/{id}` depois do 400).
- [x] 2.3 Acrescentar **G7**: o corpo do 400 traz, sob `targetAgentIds`, o
      caminho do ciclo pelos **nomes** dos agentes. Asserção sobre o JSON da
      resposta real, não round-trip pelo mesmo tipo (convenção 11).
- [x] 2.4 Montar o helper de semeadura **direta** de `agent_delegations`, por
      SQL, antes de escrever G5. Precedente de idioma: `Support/CreatedAtTie.cs`.
      **O G5 usa este helper nas DUAS rodadas — a de `HEAD` e a de depois da
      correção —, nunca a API.** Montar o ciclo herdado pela API funcionaria em
      `HEAD` e **quebraria na 3.7 por falha de setup**, porque depois da correção
      a montagem pela API passa a devolver 400: G5 ficaria vermelho pelo motivo
      errado, que é exatamente o que a V2 do `design.md` evitou em outro ponto
      desta mesma change. Semeadura direta é a única forma que produz o mesmo
      estado nos dois lados.
- [x] 2.5 Acrescentar **G4** (losango `A→B`, `A→C`, `B→D`, `C→D` continua
      aceito) e **G5** (remover a aresta que fecha um ciclo herdado é aceito;
      o ciclo herdado vem do helper da 2.4, **por SQL direto**, nunca de chamadas
      à API). **Os dois passam em `HEAD` de propósito** — são de regressão contra
      a correção errada, não contra o defeito, e isso vai escrito no arquivo de
      teste ao lado de cada um. **G8** (auto-delegação com a mensagem própria)
      já existe como `ReplaceAgentDelegations_SelfDelegation_...` e também é
      verde nos dois lados.
- [x] 2.6 Rodar a suíte de `apps/api` contra `HEAD` (com o escopo 2 já aplicado)
      e **registrar a tabela medida**: G1, G2, G3, G6, G7 vermelhos; G4, G5, G8
      verdes. Atribuir a causa de cada vermelho — se algum reprovar por motivo
      diferente do esperado, é achado e o enunciado muda antes de qualquer
      correção.

## 3. Escopo 1 — a correção (`apps/api`)

- [x] 3.1 Criar `apps/api/src/Buteco.Api/AgentDelegations/AgentDelegationCycleDetector.cs`:
      estático, **função pura sobre a lista de arestas**, sem acesso a banco.
      Recebe o grafo e o agente Source, devolve o caminho que fecha o ciclo
      (`IReadOnlyList<Guid>`) ou `null`. Comentar por que travessia completa e
      não ciclo de N saltos fixo (design.md, D1), e por que não CTE recursiva,
      com o escopo colado ao número (zero linhas em `agent_delegations` é
      referência de **volume**, não de latência — convenção 22) e com o gatilho
      de recalibração.
- [x] 3.2 Criar `AgentDelegationCycleDetectorTests.cs` (**G9**, unitário, sem
      Testcontainers): ciclo de 1, 2, 3 e 4 saltos; grafo sem ciclo; losango;
      caminho devolvido na ordem certa; grafo vazio.
- [x] 3.3 Em `ReplaceAgentDelegationsCommandHandler.cs`: carregar todas as
      arestas numa consulta, montar o grafo **pós-replace** (remover as de saída
      do Source, acrescentar as pedidas — design.md, D7), chamar o detector e
      devolver a rejeição. A checagem de auto-delegação (linhas 33-36) **fica
      onde está**, antes da travessia (D6).
- [x] 3.4 **Corrigir a Decision 3 no comentário do handler com a causa real**
      (convenção 9): o que estava escrito como "fora de escopo desta camada"
      tinha mecanismo de dano não medido; agora tem — ciclo `A→B→A` autotrava no
      advisory lock de contexto, medido com quatro instâncias de worker, e
      `DelegationDepthLimit = 5` não cobre porque a checagem de profundidade roda
      antes da aquisição do lock.
- [x] 3.5 Em `ReplaceAgentDelegationsResult.cs`: acrescentar o sinal de rejeição
      por ciclo carregando o caminho. Manter o padrão do arquivo — sinal neutro,
      sem nenhuma referência a `Microsoft.AspNetCore.Http.HttpResults`.
- [x] 3.6 Em `AgentDelegationEndpoints.cs`: novo ramo devolvendo
      `ValidationProblem` sob `targetAgentIds` com o caminho formatado por nome
      de agente (`A → B → C → A`), no mesmo molde das demais rejeições.
- [x] 3.7 Rodar a suíte de `apps/api`: G1–G3, G6, G7 e G9 verdes; G4, G5 e G8
      continuam verdes. Registrar a tabela dos dois estados lado a lado.

## 4. Escopo 3 — forma curta da convenção 22 (`01`)

- [x] 4.1 Acrescentar à convenção 22 de `01-ARQUITETURA_E_CONVENCOES.md` a forma
      curta **"régua citada sem escopo não é régua"**, citando o custo acumulado
      já documentado no `02` (quatro valores da mesma régua, quatro documentos,
      nenhum com o escopo colado). Promoção autorizada explicitamente pelo dono
      nesta change — não é consequência automática de contagem.

## 5. Verificação por execução real (convenção 6)

- [x] 5.1 Subir a stack local e conferir pela API real, não só pela suíte: criar
      três agentes, cadastrar `A→B` e `B→C`, e tentar `C→A`. Conferir o 400 e o
      **texto do caminho** no corpo da resposta.
- [x] 5.2 Conferir na tela de vínculos o que o operador vê hoje para essa recusa:
      `AgentDelegationsTab.tsx:68-74` mostra *"Não foi possível atualizar as
      delegações do agente. Tente novamente."*. **Não corrigir aqui** (convenção
      1) — registrar a observação para a change de `apps/frontend` sequenciada,
      cuja posição é imediatamente depois desta.
- [x] 5.3 Conferir que uma edição que **desfaz** um ciclo herdado passa: semear o
      ciclo por SQL direto, editar um dos agentes removendo a aresta que fecha, e
      ver 200. É a contraparte verificável do risco de D2.

## 6. Fechamento

- [x] 6.1 **Comparar** o tamanho entregue contra a projeção do `design.md`, e
      registrar a divergência com a direção e o motivo. **Não reprojetar**: a
      projeção já existe e foi feita antes de escrever o código — esta task só
      confere. É a sétima medição da série da convenção 18, que ficou em seis
      quando a anterior perdeu o par projetado/entregue.
- [x] 6.2 Registrar as duas razões entregues (teste:produção e
      comentário:lógica em produção) **com o escopo colado** (convenção 22), e
      dizer se a família *"produção dobra em linhas por comentário, não por
      lógica"* teve terceira ocorrência — se teve, a promoção da regra candidata
      ao `01` entra no fechamento, como o `02` já registra.
- [x] 6.3 Rodar a suíte de `apps/api` inteira e registrar o número final contra a
      baseline de 1.5 (**318/318**).
- [x] 6.4 Rodar a suíte de `apps/workers` inteira e conferir que **nada** mudou —
      esta change não toca o app, e o número serve para provar isso, não para
      descobrir.
- [x] 6.5 Rodar a suíte de `apps/frontend` e conferir que nada mudou — esta
      change não toca o app.
- [x] 6.6 Conferir que **nenhum arquivo fora dos caminhos dos três escopos foi
      tocado** — suíte verde não prova que um arquivo não foi mexido, e aqui a
      fronteira importa mais do que na change anterior porque são três escopos e
      um deles mexe no `01`, que lá era violação explícita. Caminhos permitidos,
      e só estes:
      `apps/api/src/Buteco.Api/AgentDelegations/` (escopo 1, produção);
      `apps/api/tests/Buteco.Api.Tests/` (escopos 1 e 2, teste);
      `01-ARQUITETURA_E_CONVENCOES.md` (escopo 3);
      `02-HISTORICO_E_STATUS.md` (fechamento, task 6.7);
      `openspec/changes/delegacao-ciclo-no-cadastro/` (artefatos da própria
      change).
      Qualquer outro arquivo é violação, e estes são non-goal declarado:
      `apps/workers`, `apps/frontend`, `apps/inbox`, `libs/`, os arquivos de
      compose, `docs/` e qualquer migration.
- [x] 6.7 Abrir no `02`, com **gatilho e posição** em cada um (gatilho sem
      posição é adiamento indefinido com outro nome):
      (a) a mensagem de erro da tela de vínculos — posição: change própria de
      `apps/frontend`, imediatamente depois desta;
      (b) `SendMessageProviderRejectionTests` com o formato latente — gatilho da
      task 1.6;
      (c) a reabertura de D3 no primeiro segundo escritor de
      `agent_delegations`.
- [ ] 6.8 Registrar o resultado da inspeção de dado da janela do deploy: contar
      linhas de `agent_delegations` e ciclos entre elas. Se vier diferente de
      zero, D2 reabre com dado — e o registro diz isso em vez de deixar a
      suposição de pé.
