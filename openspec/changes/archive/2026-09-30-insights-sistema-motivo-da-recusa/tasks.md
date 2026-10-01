## 0. Leitura antes do primeiro componente

Feita na proposta desta change, e registrada aqui para que o resultado não viva só
no chat. **Se a leitura tivesse falhado, a change pararia aqui** — a #52 gastou
treze rodadas de ajuste por divergência com o protótipo e a #53 idem.

- [x] 0.1 **Ler os artboards**, em 27/09/2026, pela ferramenta `Artifact` (o MCP
      `claude-design` recusa com `FIRST_PARTY_AUTH_REJECTED`), do canvas
      `R9JSCiYw7pBEognkKhiaDh`, **versão `1790403198-56f9`** — a versão vai ao lado
      da afirmação, e ela **difere** da `1790386152-b562` lida em 26/09.
      `Main.dc.html` (os cards Falhas e Motivos) e `Insights-Claro.dc.html` (que
      **não** os desenha).
- [x] 0.2 **Ler o `canvas.json`**, que decide duas coisas que registro nenhum tem:
      a nota `dados` (*"Dados de exemplo, não medições"*, que é o que permite ler
      as cinco linhas do card de Motivos como forma) e a nota `estados` (a
      convenção 17 dita pelo autor dentro do próprio desenho).
- [x] 0.3 **Ler o contrato da rota no código**, não de memória nem por analogia
      com o gêmeo do agente (convenção 6): `SystemInsightsResponse.cs:189-223`,
      `GetSystemInsightsQueryHandler.cs:640-704` e
      `RejectionMetricsValues.cs` (os quatro valores e por que são quatro).
- [x] 0.4 **Reconferir os cinco itens da #75 contra o `HEAD`**, porque ela foi
      escrita antes da `insights-aba-do-agente` e da `fechamento-da-l4`. Resultado
      na proposta: **um item já está feito** (o `caveat` morto saiu de
      `caveatLabels.ts`), **uma premissa é falsa** (`rejectionReasonLabels.ts` não
      existe, e esta é a **primeira** superfície a traduzir o vocabulário — a
      convenção 2 **não** foi atingida), e **dois defeitos novos** apareceram (o
      terceiro regime sem rótulo, e `REGIMES_FIXTURE` com dois regimes contra três
      da rota).
- [x] 0.5 **Marcar na issue #75 o item cumprido** em vez de refazê-lo, e registrar
      no `proposal.md` o que estava feito. *(Feito no corpo da issue — ver
      tarefa 7.1.)*

## 1. Baseline, antes de qualquer edição

- [x] 1.1 **Remedir `apps/frontend` em árvore limpa sobre a `main` (`8f646c0`)**,
      porque a última baseline registrada é sobre outro commit (convenção 22).
      Resultado: **1374 testes / 117 arquivos, 117 passaram**, 232 s.
- [x] 1.2 **Declarar o `podman ps`** junto do número: `pgvector/pgvector:pg18` e
      `rabbitmq:4.3-management`, os dois `Up 3 days (healthy)`; `DOCKER_HOST`
      vazio. A suíte do frontend não usa Testcontainers, e é por isso que o
      `DOCKER_HOST` vazio não a afeta.
- [x] 1.3 Conferir que a árvore segue limpa antes da primeira edição
      (`git status --short` vazio, fora dos artefatos desta change).

## 2. O contrato e o duplo

- [x] 2.1 Acrescentar `RejectionReasonCount` a `types/systemInsights.ts`, no mesmo
      formato de `FailurePhaseCount` — que é o formato que a rota escolheu de
      propósito, e o comentário registra por quê (lista própria, não dentro de
      `byPhase`, porque o vocabulário de `byPhase` é de outra capability).
- [x] 2.2 Acrescentar `rejectionRegime: string`, `rejectedAtEntryCount: number` e
      `rejectionsByReason: RejectionReasonCount[]` a `ErrorInsights`. **Nenhum é
      anulável, e o zero das contagens é verdade** — são `int` no C#, e a régua da
      gramática dos quatro estados sai da proveniência do zero, não do tipo.
- [x] 2.3 Corrigir o comentário de `rejectedCount`: *"Sem motivo — é a lacuna L1
      (#51)"* deixou de ser verdade. Reescrito com a causa (convenção 9) e com a
      população que ele conta, que é a que **tem** linha de execução.
- [x] 2.4 Acrescentar os três campos a `errorsFixture`
      (`test/systemInsightsFixture.ts`), com os defaults de zero e lista vazia — o
      duplo não inventa números plausíveis.
- [x] 2.5 **Acrescentar `rejection` a `REGIMES_FIXTURE`**, que declara dois e a
      rota serve três. Corrigir o comentário *"Os dois regimes que a rota declara
      hoje"*, que é a convenção 11 na forma barata: duplo que combina com o teste e
      não com a rota.
- [x] 2.6 Acrescentar os três campos a `CORPO_AUSENTE.errors`
      (`pages/SystemInsightsPage.tsx`) — o esqueleto, onde `queryState` vence sobre
      o valor.

## 3. O módulo de rótulos de motivo de recusa

- [x] 3.1 Criar `utils/rejectionReasonLabels.ts` com o dicionário fechado dos
      **quatro** valores de `RejectionMetricsValues.Reason`, lidos do código:
      `AgentNotFound`, `AgentInactive`, `ProviderOrModelMissing`,
      `ProviderNotConfigured`.
- [x] 3.2 Registrar no cabeçalho do módulo **por que ele nasce aqui** (D5): a #80
      dizia que ele já existia e escrito pela #53; a #53 **removeu** os motivos em
      vez de traduzi-los, e esta é a primeira superfície a fazê-lo. **A convenção 2
      não foi atingida** — um consumidor —, e o módulo não é extração: é onde o
      código mora. Registrar também por que ele **não** entrou em
      `failurePhaseLabels.ts` (motivo de recusa não é fase de falha, e guardá-los
      juntos contradiria a tese da change no nome do módulo).
- [x] 3.3 Importar o **tipo** `PhaseLabel` de `failurePhaseLabels.ts` e **copiar**
      as três linhas de `lookup`, declarando a cópia como segunda ocorrência, com o
      gatilho de extração: um terceiro vocabulário fechado que precise dela.
- [x] 3.4 Exportar `KNOWN_REJECTION_REASONS` para o guarda de cobertura do
      vocabulário inteiro, como os três dicionários vizinhos já fazem.
- [x] 3.5 `rejectionReasonLabels.test.ts`: cada um dos quatro valores com o rótulo
      de operador; **valor desconhecido devolve `{ text: <o valor>, unknown: true }`**;
      cobertura do vocabulário inteiro. E o guarda **negativo**: o desconhecido
      **não** recebe o rótulo de nenhum dos quatro.

## 4. O rótulo de regime, e o terceiro regime na tela

- [x] 4.1 Substituir o ternário de `regimeNoteFor`
      (`pages/SystemInsightsPage.tsx:163-169`) por um mapa fechado de nome de
      regime → rótulo de operador, com os três que a rota declara. **A queda para
      o nome cru permanece**, e passa a ser deliberada: é a régua do desconhecido
      da casa, com o valor recebido visível.
- [x] 4.2 Registrar no comentário que o terceiro regime é a **primeira** vez que o
      requisito *"Regime novo é absorvido sem mudança de estrutura"* é exercitado —
      ele passava porque nenhum teste jamais passou um terceiro.
- [x] 4.3 Guarda: com um regime que a tela não conhece, o texto apresenta **o
      próprio nome recebido**; e o **negativo** — ele não recebe o rótulo de outro
      regime.

## 5. O card de Falhas — a troca de população

- [x] 5.1 Trocar `errors.rejectedCount` por `errors.rejectedAtEntryCount` no bloco
      *"Recusadas na entrada"* (`components/FailuresCard.tsx:116`). É o número que
      o `Main.dc.html` desenha ali, e o mesmo `5` que ele repete na linha de motivo
      do card vizinho.
- [x] 5.2 Declarar o **regime da recusa** dentro do quadro da recusa, abaixo do
      `caveat`, que é onde o artboard escreve o subtítulo daquele número (D7).
- [x] 5.3 Corrigir o texto literal do protótipo, que enumera **duas** das
      **quatro** causas — faltam `ProviderNotConfigured` e `AgentNotFound`. Com os
      motivos na lista ao lado, a frase estaria enumerando um subconjunto do que a
      tela mostra: convenção 13 na forma *"verdadeira quando escrita, e outra etapa
      tornou falsa"*. **Registrar que é divergência do literal do protótipo**, com
      a causa.
- [x] 5.4 Reescrever o comentário do topo do card com a decisão da D3 — por que
      `rejectedCount` **sai da página** (o `Main.dc.html` não tem KPI de taxa de
      falha, logo não há o subtítulo que hospeda esse número na aba do agente), e
      por que o `caveat` **não** muda de posição (ele limita o percentual de falha,
      que continua na tela, e continua verdadeiro sobre a recusa de entrada).
- [x] 5.5 Guardas em `FailuresCard.test.tsx`: o número apresentado é o da recusa de
      entrada, com os dois valores **diferentes** na fixture — um caso em que os
      dois são iguais passaria com o campo errado; o **negativo** de que o valor de
      `rejectedCount` não aparece na página; o regime declarado junto do número; e
      o **negativo** de que os dois números não aparecem somados.
- [x] 5.6 Re-semear os **16** sítios de `errorsFixture({ … })` que declaram
      `rejectedCount` — 13 em `FailuresCard.test.tsx`, 3 em
      `FailureReasonsCard.test.tsx`. **O compilador não os acusa** (passam
      `Partial<ErrorInsights>`), então a varredura é por `grep`, não por `tsc`.

## 6. O card de Motivos — o grupo próprio

- [x] 6.1 Dividir o corpo do card em grupos, na forma que o `AgentFailuresCard` já
      usa (`Grupo` com rótulo de seção): fases de execução e falhas de indexação
      seguem na lista concatenada e ordenada que já existe, e os motivos de recusa
      entram em grupo próprio.
- [x] 6.2 Descer a nota de regime do **cabeçalho do card** para o **cabeçalho de
      cada grupo** (D7), porque o card passa a ter populações de regimes
      diferentes e uma nota no cabeçalho qualificaria o card inteiro.
- [x] 6.3 Motivos ordenados por contagem, como a lista de fases, e rotulados por
      `rejectionReasonLabel`. **Motivo desconhecido aparece cru e visível** —
      neutro e monoespaçado, como a fase desconhecida ao lado.
- [x] 6.4 Rever o texto do estado vazio: *"Nenhuma falha nem recusa neste
      período."* passa a depender de `rejectedAtEntryCount` e não de
      `rejectedCount`, que sai da tela. Caso próprio para "sem falha e **com**
      recusa" e para "com falha e **sem** recusa" — os dois estados que o texto
      único hoje esconde.
- [x] 6.5 Reescrever o cabeçalho do arquivo, que hoje descreve a L1 como **aberta**
      e o `caveat` `rejection-reason-not-collected` como vivo
      (`FailureReasonsCard.tsx:7-24`). **Reescrita completa, não emenda**, com: a
      L1 fechada pela #51; a decisão da D1 (por que aqui a resposta é oposta à da
      aba do agente, saindo da mesma régua); e a divergência da D2 (por que a lista
      rasa do artboard é dividida em dois grupos), com o gatilho de volta.

## 7. Os guardas invertidos — convenção 15, um a um

- [x] 7.1 Reescrever `NEGATIVO: a recusa NÃO vira quadro no card de Motivos`
      (`:85`). A garantia que ele protege — *"a tela não nomeia causa que ninguém
      mediu"* — sobrevive; a forma (*"a palavra `recusa` não aparece"*) não, porque
      ela funcionava só enquanto ausência de palavra e ausência de medição
      coincidiam.
- [x] 7.2 Reescrever `NEGATIVO: NENHUMA causa é nomeada para as recusas` (`:97`).
      As três expressões que ele proíbe — `/sem provider/i`, `/agente inativo/i`,
      `/modelo configurado/i` — são **exatamente** os rótulos de
      `ProviderOrModelMissing` e `AgentInactive`. A forma nova afirma a
      **procedência**: causa nomeada ⇒ valor que chegou em `rejectionsByReason`.
- [x] 7.3 O guarda que substitui os dois, e é o que importa: com
      `rejectedAtEntryCount > 0` e `rejectionsByReason` **vazio**, **nenhuma** causa
      é nomeada. É o estado em que a tentação de preencher com a causa plausível
      volta.
- [x] 7.4 **Reintroduzir o defeito real contra cada guarda novo e ver reprovar**, e
      conferir que ele reprova **no componente que a correção toca** (segunda forma
      da convenção 15). **Rodar a classe completa, não o caso isolado** — conferir
      isolado é o jeito enganoso, e é o que a quinta forma da convenção 15 mediu.
- [x] 7.5 O guarda da **soma**: a soma das contagens de motivo apresentadas fecha
      com `rejectedAtEntryCount`, inclusive com um motivo desconhecido no meio. E o
      **negativo**: nenhum dos motivos recebidos falta na apresentação.

## 8. A união morta e a contagem errada em `caveatLabels`

- [x] 8.1 Remover `'rejection-reason'` de `CaveatPlacement`
      (`utils/caveatLabels.ts:68-69`) — nenhum código mapeia código nenhum para
      ele desde que `rejection-reason-not-collected` saiu. Registrar a causa, e que
      **a forma é a da #83**: declaração sem consumidor que o compilador não acusa.
- [x] 8.2 Corrigir o comentário de conferência (`:41-55`): ele diz *"5 códigos em 2
      blocos — `performance` (2), `errors` (2)"*, e 2 + 2 são **4**. Recontado no
      handler (`:529` e `:698-699`). Escrever o **critério de contagem** ao lado do
      número, com detalhe para alguém recontar (sexta ocorrência da convenção 22:
      número que nasceu errado).
- [x] 8.3 Ajustar `caveatLabels.test.ts` ao que sair da união, e conferir se algum
      caso afirmava a posição removida.

## 9. Verificação

- [x] 9.1 **`npx tsc -b` limpo em `apps/frontend`** — e **não** `--noEmit`, que era
      o que esta tarefa dizia. `apps/frontend/tsconfig.json` é
      `{"files": [], "references": [...]}`, então `tsc --noEmit` **sai com 0 sem
      compilar um arquivo**: saiu verde na primeira conferência com dois erros reais
      na árvore. Convenção 21 na letra — varredura que falha em silêncio produz a
      mesma saída que um sistema sadio. `tsc -b` é o que o `npm run build` usa, e é
      ele que enumerou os 2 sítios de construção do tipo.
- [x] 9.2 **`npx vitest run` — 1402 / 118, ZERO falhas, 301 s**, contra a baseline
      **1374 / 117** em 232 s da tarefa 1.1. **+28 casos e +1 arquivo.** A projeção
      do `design.md` era ~1388 / 118: erro de **−1,0%**.
      **Três execuções anteriores foram descartadas por ambiente, não por defeito** —
      a VM do Podman (`com.apple.Virtualization.VirtualMachine`) em **485% de CPU**,
      `load average` a **21,35** e depois **26,88** numa máquina de 12 núcleos. A
      quarta rodou com a VM fora do topo de `ps` e carga em 5,24. As 9 classes que
      reprovavam sob contenção já passavam isoladas (**163 / 163**), e nenhuma estava
      em arquivo que a change toca.
      `tsc -b` verde, `npm run lint` sem saída, `openspec validate --all` 62/62.
- [x] 9.4 **Conferência manual do dono, quadro a quadro** (convenção 14): os dois
      esquemas de cor; motivo desconhecido; recusa com motivos e sem motivos; os
      dois números lado a lado sem somar; a nota de regime na posição nova (D7,
      candidata a rodada de ajuste); e os dois grupos contra a lista rasa do
      artboard (D2, com a alternativa pronta se ele preferir a rasa).
      **FEITA.** A tela subiu com `apps/api` em 5017 e o Vite em 5173, com dado real
      de recusa produzido pela API, e foi conferida nos dois esquemas de cor.
      **Resultado: um achado, e ele não era de lógica** — a nota de regime do card de
      Motivos nomeava uma de três populações. Corrigido em 9.4.1. É a **segunda vez
      nesta linha de trabalho** que a conferência manual pega o que nenhum guarda
      pediria, com a suíte inteira verde nas duas.
- [x] 9.4.1 **A nota de regime SAIU do card de Motivos** — decisão do dono na
      conferência, e ele achou o erro que a minha pré-conferência não tinha achado.
      Eu propunha condicionar a nota a `indexingFailures.length > 0`; **era conserto
      de sintoma**. O lugar sempre esteve certo; o **texto** é que mente, porque o
      card mostra três populações de três regimes e nomear um afirma que tudo ali é
      daquele. **Nota ausente é melhor que nota errada.** As duas alternativas
      recusadas — a data mais antiga sem nomear o regime, e a nota por grupo — estão
      no `design.md`, e a segunda virou a **#90** com gatilho.
- [x] 9.4.2 **O par de guardas, e ele reprovou DUAS vezes por arranjo antes de
      reprovar pelo defeito.** O de componente não podia discriminar (o defeito
      morava na fiação); o de página passou verde porque a precondição que afirmei
      era o mapa de regimes, e a fixture padrão não tem linha de falha — **sem grupo,
      não havia nota a negar**. Corrigido para afirmar a linha que produz o elemento.
      Verificados os dois lados: reintroduzir a nota reprova o negativo, e tirá-la de
      **todos** os cards reprova o positivo.
- [x] 9.4.3 **`ProviderConsumptionCard` tem a mesma forma e NÃO foi tocado** —
      declara `embedding` no cabeçalho com colunas de dois regimes. Materialmente
      mais fraco (o regime nomeado tem coluna com o próprio nome). **Achado, não
      escopo** (convenção 23): registrado na #90.
- [x] 9.5 **`openspec validate --all` verde.**

## 10. Registro

- [x] 10.1 **A medição da convenção 18 — vigésima terceira — só depois da
      conferência do dono**, nunca no primeiro verde (régua da vigésima primeira).
      Projetado contra entregue, decomposto: criados e modificados **separados**,
      as três categorias de caso, as **8** negativas por estado observável, a
      semeadura de fixture em linha própria, e a proporção comentário:lógica
      (projetada em ~2,0:1 na produção, por três registros de mecanismo).
- [x] 10.2 Registrar no `02` **o blast radius medido do campo obrigatório**, que é
      pequeno pelo motivo **oposto** ao do caso da quarta medição: o tipo tem
      construtor de duplo único, então o blast radius é 2 por construção. A régua
      reusável: contar sítios de **construção completa**, não arquivos que importam
      o tipo.
- [x] 10.3 Registrar no `02` a divergência do artboard (D2) e a decisão oposta à da
      aba do agente (D1), **com a causa** — e o gatilho de volta de cada uma.
- [x] 10.4 Registrar no `02` **o destravamento do índice do board**: o gatilho
      pendurado na convenção 24 (*"`project.items` devolver mais de 20"*) **venceu**
      — mede **35** em 27/09/2026. `Priority` e `Iteration` continuam vazios nos 35,
      então o gatilho C1 **não** venceu. Ver a tarefa 11.
- [x] 10.5 `CHANGELOG.md`.
- [x] 10.6 Abrir as issues dos achados, **na hora e antes do archive** (convenção
      23): **#88** (`rejectedCount` fora da tela do sistema, *servido e não
      desenhado*, gatilho de duas alternativas) e **#89** (a forma da #83 se
      repetiu, e uma união de tipo não é varrível em tempo de execução — decisão de
      custo pendente).

## 11. O que NÃO é desta change

- [x] 11.1 **Não tocar `apps/api`.** Os três campos já são servidos desde a #51.
- [x] 11.2 **Não corrigir a #84 nem a #86.** As duas são uma change só, adjacente
      na fila, e corrigir uma deixa o número errado de outro jeito. Reconferidas de
      passagem e registradas na proposta — `AgentConsumptionCard.tsx:65` e `:108`.
- [x] 11.3 **Não corrigir a #83 nem a #85.** A #83 é o `DeclaredGap`; a #85 é o
      período que não viaja. **Não há navegação nesta change**, então a régua da
      `fechamento-da-l4` — *guarda que afirma o meio do caminho não prova o fim
      dele* — não tem travessia para cobrir aqui, e nenhum guarda de travessia é
      inventado.
- [x] 11.4 **Não mexer na aba do agente**, que acabou de ir a produção.
- [x] 11.5 **Nada commitado, nenhum push, nenhum PR.** O archive vem antes
      (convenção 24), e os dois são do dono.
