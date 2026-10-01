## Context

**Etapa de consumo da #51, na página do sistema.** A #51 (`recusa-motivo-coleta`,
arquivada em 26/09) passou a gravar o motivo de toda recusa feita antes de
qualquer execução e a servi-lo nas **duas** rotas de agregação. A aba do agente
decidiu **não** apresentá-lo, por decisão do dono na tela; a página do sistema é a
que tem elemento no protótipo para ele, e é esta change.

**A rota não é tocada.** `GET /insights/system` já serve os três campos, e a
capability `system-insights-aggregation` já fixa o contrato. Esta change é
`apps/frontend` e registro, nada mais (convenção 1).

### O que foi lido, e quando

**Os protótipos, primeiro — tarefa 0, não conferência posterior.** A #52 precisou
de **treze** rodadas de conferência manual e a #53 de várias; nenhum dos achados
do dono foi de lógica, todos de fidelidade ao protótipo, de registro ou de
contraste. Trabalhar do `02` ou de `design.md` arquivado garante a rodada extra.

Lidos em **27/09/2026**, pela ferramenta `Artifact` — o MCP `claude-design`
continua recusando com `FIRST_PARTY_AUTH_REJECTED` —, do canvas
`https://claude.ai/artifact/R9JSCiYw7pBEognkKhiaDh`, **versão
`1790403198-56f9`**.

> **A versão mudou desde a leitura da `insights-aba-do-agente`**, que registrou
> `1790386152-b562` em 26/09. O repositório não guarda cópia do `Main.dc.html`,
> então **não há como diffar** — o que segue é o que a versão de 27/09 desenha, e
> a versão vai junto do número pela convenção 22. Nada no card de Motivos nem no
> de Falhas contradiz o que os registros da #52 descrevem, o que sugere que a
> mudança foi noutro board.

| arquivo | o que decide aqui |
|---|---|
| `project/Main.dc.html` | o card **Motivos** e o card **Falhas**, tema escuro — 1360 × 1880 |
| `project/canvas.json` | as quatro notas do autor — `dados`, `cenarios`, `estados`, `titulo` |
| `project/Insights-Claro.dc.html` | **não** desenha os dois cards: é a metade de cima da página no tema claro |

**O `canvas.json` decide uma coisa que muda a leitura dos números do artboard**:
a nota `dados` diz, do autor, *"Dados de exemplo, não medições."* É o que permite
ler as cinco linhas do card de Motivos como **forma**, não como vocabulário — e é
bom, porque três das cinco não existem em vocabulário nenhum do sistema (ver D2).

**O contrato da rota**, lido em
`apps/api/src/Buteco.Api/Insights/Responses/SystemInsightsResponse.cs:189-223` e
no handler `GetSystemInsightsQueryHandler.cs:640-704` — não de memória nem por
analogia com o gêmeo do agente (convenção 6). E o vocabulário em
`apps/api/src/Buteco.Api/RejectionMetrics/RejectionMetricsValues.cs`, que é a
fonte dos quatro valores e de por que são quatro.

## Goals / Non-Goals

**Goals:**

- Os três campos servidos desde a #51 chegam à tela do sistema, com as duas
  populações de recusa distinguidas onde hoje uma ocupa o rótulo da outra.
- A contradição da spec `system-insights-ui` resolvida **pelo lado da fonte**: o
  motivo deixou de ser lacuna porque passou a ser medido.
- O regime da recusa declarado junto do número dele, com rótulo de operador.
- Os guardas que protegiam a ausência de causa não medida reescritos sem perder o
  que protegiam.

**Non-Goals:**

- **`apps/api`.** Nada. Os três campos já são servidos.
- **A #84, a #86, a #83 e a #85.** As duas primeiras são uma change só, adjacente
  na fila. Reconferidas de passagem e registradas na proposta; nenhuma corrigida.
- **A aba do agente.** A decisão de lá continua valendo — ver D1.
- **O período viajar entre as telas.** É a #85, e não há navegação nesta change.

## Decisions

### D1 — Os motivos entram no card de Motivos, e a decisão OPOSTA da aba do agente sai da MESMA régua

A `insights-aba-do-agente` tirou a recusa de entrada e os motivos do
`AgentFailuresCard`, e o registro dela é explícito sobre o critério
(`AgentFailuresCard.tsx:24-43`):

> *"O artboard não tem elemento nenhum para eles, e a D10 da mesma change diz,
> por escrito, que métrica servida e não desenhada não ganha elemento novo."*

**Na página do sistema o artboard TEM o elemento.** O `Main.dc.html` desenha, no
card **Motivos**, cinco linhas de rótulo e contagem, e a segunda é literalmente
*"Agente sem provider ou modelo configurado — 5"* — com o mesmo `5` que o card
vizinho mostra em *"Recusadas na entrada"*. O motivo da recusa **é** um elemento
do protótipo desta tela, e sempre foi: ele só não pôde ser preenchido porque não
havia fonte, o que está registrado em `FailureReasonsCard.tsx:9-22`.

**Alternativa considerada: seguir a aba do agente e deixar de fora.** Rejeitada
porque aplicaria o *resultado* da régua em vez da régua. A D10 não diz "recusa de
entrada não aparece"; diz "não se cria elemento que o artboard não tem". Aqui não
se cria nada — se preenche o que ele tem e estava vazio por falta de fonte.

**As duas decisões, juntas, são o teste da régua:** a mesma condição avaliada em
duas telas dá respostas opostas porque os artboards são diferentes. Se o critério
fosse gosto, teria dado a mesma resposta nas duas.

### D2 — Mas NÃO como sexta linha da lista rasa: o grupo é próprio, e é divergência registrada

O card de Motivos do `Main.dc.html` é **uma lista ordenada única**, sem
cabeçalho de grupo, sem separador, ordenada por contagem:

```
Tempo de resposta do provedor esgotado        6
Agente sem provider ou modelo configurado     5   ← recusa de entrada
Servidor MCP indisponível                     3
Provedor recusou por limite de uso            3
Falha de indexação de conhecimento            2
```

**A lista mistura as duas populações, e o artboard não sabia que elas eram
duas.** Ele foi desenhado antes de a #51 existir, quando *"recusa"* era um número
só. Hoje a tela tem, no card vizinho, um texto literal do próprio protótipo
dizendo *"somar os dois esconde qual dos dois problemas existe"* — e a lista rasa
é exatamente o convite a somar: quatro linhas de uma população e uma da outra, sem
nada que as distinga, ordenadas juntas. Quem lê soma de cima para baixo.

**A decisão: um card, dois grupos rotulados.** *"Por fase da execução"* /
*"Por indexação de conhecimento"* seguem na lista concatenada e ordenada que já
existe, e os motivos de recusa entram num grupo próprio, com o regime declarado
no cabeçalho dele.

**A forma não é inventada aqui — é idioma da casa, com precedente do mesmo tipo.**
O `AgentFailuresCard` divide o card em dois grupos (`Grupo`, testId
`grupo-por-fase` e `grupo-por-provedor-e-modelo`) contra **uma tabela única** do
artboard do agente, e o registro dá a razão (D9, `AgentFailuresCard.tsx:51-59`):
*"a rota serve duas agregações INDEPENDENTES sobre a mesma população, e nenhum
campo junta as duas — cruzá-las no cliente inventaria a junção."* Aqui o
argumento é mais forte, porque não são duas agregações sobre a mesma população:
são **duas populações**.

**E a nota `dados` do `canvas.json` sustenta a leitura.** *"Dados de exemplo, não
medições."* Três das cinco linhas do artboard — *"Tempo de resposta do provedor
esgotado"*, *"Servidor MCP indisponível"*, *"Provedor recusou por limite de uso"*
— não são valores de vocabulário nenhum: `ExecutionMetricsValues.FailurePhase`
tem sete, e nenhum é esses. O artboard desenha a **forma** (rótulo à esquerda,
contagem à direita, linhas separadas por borda), e a forma é o que a
implementação segue.

**Convenção 17, e ela é explícita no `canvas.json`**, na nota `estados`, com as
palavras do autor: *"contrariar o protótipo é resultado legítimo e vira registro,
com gatilho para voltar — não implementação silenciosa"*. **Gatilho para voltar à
lista única:** o dono olhar a tela com os dois grupos e preferir a lista rasa —
aí a divergência cai, e o texto do card de Falhas é que tem de mudar, porque
passa a ser ele que contradiz a tela.

**Alternativa considerada: card próprio para os motivos de recusa.** Rejeitada
pela D10 — é elemento novo que o artboard não tem, e é exatamente o que o dono
apontou na aba do agente como *"pesava mais que a tabela de falhas"*.

### D3 — `rejectedCount` sai da tela do sistema, e vai para "servido e não desenhado" com issue

O bloco *"Recusadas na entrada"* passa a ler `rejectedAtEntryCount`. A pergunta
que sobra é onde fica `rejectedCount`, que hoje ocupa aquele lugar.

**Ela sai da página.** O `Main.dc.html` tem **seis** KPIs — duração p95, tasks
executadas, chamadas ao provedor, tokens de conversa, tokens de embedding, tokens
por task — e **nenhum de taxa de falha**. Não há, nesta página, o elemento que na
aba do agente hospeda esse número: lá ele vive no subtítulo do KPI `Taxa de
falha`, *"5 falhas · nenhuma recusa"*, que o artboard do agente desenha
(`AgentKpiGrid.tsx:37-48`). Aqui não existe subtítulo equivalente, e criar um é a
D10 outra vez.

**Alternativa considerada: um terceiro quadro no card de Falhas.** Rejeitada duas
vezes pela mesma régua — elemento que o artboard não tem — e uma terceira por
consequência: com três números, o texto que explica **dois** passa a enumerar um
subconjunto, que é o defeito que esta change está corrigindo no mesmo card.

**Alternativa considerada: manter os dois no mesmo quadro.** Rejeitada porque o
quadro tem um rótulo e um número; dois números sob um rótulo é o defeito original
em forma nova.

**O `caveat` NÃO muda de lugar, e o motivo é que ele não perde o número.**
`rejections-missing-from-executions` diz *"Recusas não geram linha de execução,
então não entram no percentual de falha"*. Ele limita o **percentual de falha**,
que continua na tela, e continua verdadeiro sobre a recusa de entrada, que também
não gera linha de execução. A posição `rejection-count` permanece.

**Gatilho de volta, e ele vai para issue (achado 1 da proposta):** o primeiro
pedido do dono por um número de recusa com linha de execução nesta página, ou o
`Main.dc.html` ganhar elemento para ele. O que sobrevive ao archive é a issue.

### D4 — Dois guardas negativos invertem, e a inversão preserva o que eles protegiam

Em `FailureReasonsCard.test.tsx` há dois guardas que esta change **quebra de
propósito**:

| guarda | linha | o que afirma |
|---|---|---|
| `NEGATIVO: a recusa NÃO vira quadro no card de Motivos` | `:85` | `card.textContent` **não** casa com `/recusa/i` |
| `NEGATIVO: NENHUMA causa é nomeada para as recusas` | `:97` | o texto não casa com `/sem provider/i`, `/agente inativo/i`, `/modelo configurado/i` |

**Os dois estão certos sobre o `HEAD` e errados sobre o que a change entrega.** O
segundo é o mais interessante: as três expressões que ele proíbe são **exatamente**
os rótulos de operador de `ProviderOrModelMissing` e `AgentInactive`. Ele foi
escrito para impedir a tela de afirmar uma causa plausível que ninguém mediu — e é
plausível justamente porque é uma das causas reais.

**O que o guarda protege sobrevive; o que ele afirma, não.** A garantia é *"a tela
não nomeia causa que ninguém mediu"*. A forma era *"a palavra não aparece"*, e
funcionava enquanto nenhuma causa era medida — ausência de palavra e ausência de
medição coincidiam. Agora não coincidem mais, e a forma tem de mudar para a que
afirma a **procedência**:

- **positivo:** cada causa nomeada no grupo de recusa corresponde a um valor que
  chegou em `rejectionsByReason`;
- **negativo, e é o que substitui os dois:** com `rejectedAtEntryCount > 0` e
  `rejectionsByReason` **vazio**, **nenhuma** causa é nomeada. Este é o caso que o
  guarda antigo cobria por acidente e o novo cobre por desenho — é o estado em que
  a tentação de preencher com a causa plausível volta.

**Convenção 15, e ela tem uma armadilha própria aqui.** Um guarda de *"não casa
com regex"* reprova com o defeito presente **e** com a correção presente, se o
regex continuar amplo. Ao reescrever: ver o guarda novo reprovar contra o defeito
real (o caso com `rejectionsByReason` vazio e uma causa nomeada), e conferir que
ele reprova **no componente que a correção toca** — a segunda forma da convenção
15, a de `dedupe-global-nome-de-tool`.

### D5 — O módulo de rótulos NASCE aqui, e a convenção 2 NÃO foi atingida

A #80 diz que o vocabulário *"já está traduzido em
`utils/rejectionReasonLabels.ts`, escrito pela #53"* e que *"nenhum código novo é
necessário"*. **O arquivo não existe no `HEAD`** — a #53 removeu os motivos em vez
de traduzi-los. Esta é a **primeira** superfície a traduzi-los.

**Então a convenção 2 não se aplica como a issue previa, e a leitura invertida é
a que importa:** o gatilho da convenção 2 é repetição **já observada**, e aqui há
**um** consumidor. Isso não significa "não criar o módulo" — significa que o
módulo não é **extração**, é onde o código mora. A convenção 2 governa *extrair
código compartilhado*, não *escrever a primeira cópia num arquivo próprio*.

**Alternativa considerada: pôr o quarto vocabulário dentro de
`failurePhaseLabels.ts`**, que já hospeda três dicionários (fases de execução,
resultados de indexação, fases de indexação) e já exporta o tipo `PhaseLabel` com
o contrato `unknown` que a régua do desconhecido precisa. **Rejeitada, e o motivo
é o miolo da change:** o motivo de recusa **não é uma fase de falha**, e a change
inteira existe para impedir que as duas populações se confundam. Guardá-las no
mesmo arquivo chamado `failurePhaseLabels` contradiria a tese no próprio nome do
módulo — o tipo de divergência que não causa defeito e causa a dúvida de se são a
mesma coisa.

**O que é compartilhado e o que é copiado:** o **tipo** `PhaseLabel` é importado
de `failurePhaseLabels.ts`, porque já é exportado e porque as duas superfícies
precisam do mesmo par `{ text, unknown }` para a apresentação neutra. A função
`lookup` de três linhas é **copiada**, e a cópia é declarada — é a segunda
ocorrência, e a convenção 2 pede a terceira.

**Gatilho de extração:** um terceiro vocabulário fechado que precise de
`lookup`. Aí os três saem para um módulo de vocabulário, e não antes.

### D6 — O terceiro regime ganha rótulo, e a queda para o nome cru passa a ser deliberada

`regimeNoteFor` (`SystemInsightsPage.tsx:163-169`) monta o texto com
`name === insights.tokens.embeddingRegime ? 'embedding' : name`. Com dois regimes,
o ternário cobre os dois casos: `execution` governa a página e não gera nota,
`embedding` é traduzido. **Com o terceiro, ele emite o nome do fio** — *"rejection
medido desde …"*, em inglês, numa tela em português.

**O requisito *"Medindo desde é declarado por regime"* já manda absorver regime
novo sem mudança de estrutura, e o cenário *"Regime novo é absorvido"* passa
hoje** — porque nenhum teste jamais passou um terceiro regime. A fixture tem
dois (`systemInsightsFixture.ts:118-122`); a rota serve três. **É a convenção 11
na forma barata:** o duplo montado pelo teste combina com o próprio teste, e não
com a rota.

**A decisão:** um mapa fechado de nome de regime → rótulo de operador, com os três
que a rota declara, e **a queda para o nome cru permanece** para o regime que a
tela não conhecer. A diferença é que ela deixa de ser acidente e passa a ser a
régua do desconhecido da casa — valor não reconhecido aparece, cru e visível,
nunca com o rótulo de outro —, com guarda próprio.

**Alternativa considerada: traduzir no servidor.** Rejeitada: o nome do regime é
chave de contrato, não texto de operador, e traduzir no servidor obrigaria a rota
a saber o idioma da tela. É `apps/api`, e a change não o toca.

> **CORREÇÃO DA D6 NA IMPLEMENTAÇÃO — a premissa estava errada, a decisão fica**
> (convenção 9: corrigir o artefato com a causa real, não só o resumo do chat).
>
> A D6 dizia que a queda para o nome cru era acidente e que a change a tornaria
> deliberada. **Ela já era deliberada, e já tinha guarda.** O caso
> `regime NOVO é absorvido sem mudança de estrutura`
> (`SystemInsightsPage.test.tsx:178`) passa `indexing` como regime **da página** e
> afirma o texto `"indexing medido desde 24/09/2026"` — o nome cru —, e o
> comentário dele diz por escrito: *"O NOME sai cru quando a tela não o conhece,
> pelo mesmo critério da fase de falha desconhecida."* Escrito pela #52.
>
> **A tarefa 2.5 foi feita primeiro e NENHUM caso reprovou** — os 21 da página
> seguiram verdes com os três regimes na fixture e `regimeNoteFor` intocado. O
> motivo não é guarda fraco: **é que não havia caso a reprovar.** Acrescentar
> `rejection` ao mapa `regimes` não faz a tela renderizar nota nenhuma para ele,
> porque `regimeNoteFor` só é chamado para `tokens.embeddingRegime` e
> `temporal.regime`. **O terceiro regime estava no mapa e sem consumidor na tela** —
> e é esta change que lhe dá o primeiro.
>
> **O que o guarda existente não cobre, e não pode cobrir:** ele passa um nome que
> a tela **genuinamente não conhece**, e para esse caso o cru é o certo. `rejection`
> é o caso oposto — **regime que a tela conhece** (a rota o exige em
> `MetricsOptions.All`, o contrato o declara em campo próprio, e esta change o
> apresenta) caindo no caminho do desconhecido. Nenhum arranjo do guarda antigo
> separa os dois, porque ele nunca passa um conhecido-sem-rótulo.
>
> **A decisão da D6 não muda** — mapa fechado de rótulo para os três conhecidos, e
> a queda para o cru permanece. **O guarda novo muda:** ele não é *"o cru continua
> funcionando"* (isso já existe e fica), é *"regime conhecido NÃO sai cru"*, e o
> discriminante é `rejection` apresentado com rótulo de operador. **Reprova contra
> a implementação de hoje**, que é o que a convenção 15 pede.

### D7 — Onde o regime da recusa é declarado: no número, não no card

O `FailureReasonsCard` já recebe `regimeNote` e a desenha no **cabeçalho do
card**, à direita, e hoje ela carrega o regime de **indexação**
(`SystemInsightsPage.tsx:323`). Não cabe uma segunda nota no mesmo cabeçalho: ela
qualificaria o card inteiro, e o card passa a ter três populações de três regimes.

**A decisão:** a nota do regime de recusa vai no **cabeçalho do grupo** de motivos
de recusa, que é o escopo exato ao qual ela se aplica, e a nota do regime de
indexação **desce do cabeçalho do card para o grupo de indexação** pelo mesmo
motivo. É a régua já escrita em `caveatLabels.ts` — *"texto de limitação junto do
número limitado"* — aplicada a regime em vez de a `caveat`.

E no card de Falhas a nota vai **dentro do quadro da recusa**, logo abaixo do
`caveat`, que é onde o artboard escreve o subtítulo daquele número.

**Consequência a conferir na tela (convenção 14):** o cabeçalho do card de
Motivos fica sem a nota que tinha, e o dono já apontou uma vez que nota de regime
*"vive no cabeçalho, no mesmo idioma do máximo · mínimo do gráfico"*
(`FailureReasonsCard.tsx:62-68`). A descida para o grupo é mudança de posição de
um elemento que ele conferiu — **candidata a rodada de ajuste**, e está nomeada
aqui para que a rodada não seja surpresa.

> **CORREÇÃO DA D7 NA IMPLEMENTAÇÃO — a nota do regime de recusa é UMA, não duas**
> (convenção 9).
>
> A D7 mandava declarar o regime da recusa em dois lugares: no cabeçalho do grupo
> de motivos **e** dentro do quadro da recusa no card de Falhas. **São a mesma
> data, na mesma tela, em dois cards vizinhos** — e a casa já rejeitou exatamente
> isso uma vez: o `caveat` de recusa da aba do agente estava *"aqui em prosa, no
> rodapé, **e também** no card de falhas"*, e o registro diz que *"o dono apontou
> o rodapé que o artboard não desenha, e a régua mostrou que era DUPLICAÇÃO"*
> (`AgentKpiGrid.tsx:50-72`).
>
> **O regime é declarado UMA vez, no quadro do card de Falhas**, que é onde está a
> **contagem** de recusa de entrada. O grupo de motivos apresenta a decomposição
> daquele mesmo número, na mesma janela e no mesmo regime; declará-lo de novo ali
> seria a data repetida sem acrescentar nada. E o guarda de página
> `NEGATIVO: a nota de regime não se repete fora do cabeçalho do card` — que
> afirma que **nenhuma data se repete entre as notas** — reprovaria contra a D7 como
> ela estava escrita. A D7 contradizia um guarda existente, e o guarda tem razão.
>
> **O que da D7 FICA:** a descida da nota de **indexação** do cabeçalho do card para
> o grupo de indexação. O motivo ficou mais forte, não mais fraco — o card passa a
> ter **três** populações de três regimes, e uma nota no cabeçalho qualificaria as
> três dizendo a data de uma.

> **SEGUNDA CORREÇÃO DA D7, E ELA VEM DA CONFERÊNCIA DO DONO: a nota SAI do card.**
>
> A correção acima moveu a nota de indexação para o cabeçalho do grupo de falha, e
> **errou pelo mesmo motivo que diagnosticou** — ela nomeia três populações e depois
> conserta o lugar em vez do texto. **O lugar nunca foi o problema.** O card mostra
> fase de falha de execução (`execution`, 22/09), falha de indexação (`embedding`,
> 23/09) e motivo de recusa (`rejection`, 26/09): **nomear um deles afirma que tudo
> ali é daquele, e dois terços não são.** É a mesma família do defeito que esta
> change corrige no card vizinho.
>
> **Decisão do dono: remover. Nota ausente é melhor que nota errada**, e a informação
> não se perde — o card de Falhas declara `"recusa medida desde…"` no quadro dela, a
> página declara o de execução, e o mapa de regimes chega inteiro.
>
> **As duas alternativas, recusadas com o motivo:**
>
> - **a data mais antiga sem nomear o regime** — verdade no sentido de que nada ali
>   foi medido antes dela, mas sugeriria que um motivo de recusa de 23/09 poderia
>   existir, e ele não poderia;
> - **uma nota por grupo** — **correto**, e é o que a página faz noutros cards.
>   Recusada por peso, não por estar errada: três linhas num card que acabou de
>   ganhar dois grupos. **Fica como a saída registrada**, com issue e gatilho (#90).
>
> **O que sobra da D7:** nada da nota de indexação. O que sobrevive dela é a outra
> metade — o regime da **recusa** declarado uma vez só, no quadro da contagem.

### D8 — A gramática dos quatro estados, herdada e não reaberta

As três contagens novas são `int` no C# (`RejectedAtEntryCount`, e `Count` em
`RejectionReasonResponse`), então **contagem medida, e o zero delas é verdade** —
entram como `number`, sem `| null`, exatamente como `rejectedCount` e
`failedCount`. `rejectionRegime` é `string` não anulável.

O que a change acrescenta à gramática é um caso do **quarto** estado: a lista
`rejectionsByReason` **vazia** com `rejectedAtEntryCount` em zero é *"medi e não
achei nada"*; a mesma lista vazia com a consulta em `failed` é travessão. É o que
o `queryState` já decide em `readMetric`, e nenhuma linha nova é necessária — o
que é necessário é a **asserção negativa** que impede o colapso, e ela entra
(convenção 13).

## Projeção de tamanho — vigésima terceira medição da convenção 18

**Feita com a verificação de escopo FECHADA** (a régua: projeção durante a
verificação é rascunho), e **medida depois da conferência do dono**, não no
primeiro verde (régua da vigésima primeira).

**Baseline REMEDIDA, não herdada.** A última registrada é `apps/frontend`
**1374 / 117**, sobre outro commit. Remedida em **árvore limpa** sobre a `main`
atual (`8f646c0`), em 27/09/2026: **1374 testes / 117 arquivos, 117 passaram**,
duração 232 s. `podman ps` declarado: `pgvector/pgvector:pg18` e
`rabbitmq:4.3-management`, os dois `Up 3 days (healthy)`; `DOCKER_HOST` vazio (a
suíte do frontend não usa Testcontainers). **O número não envelheceu — ele se
confirma neste commit**, o que é o caso raro de recalibração que não corrige.

**Arquivos — criados e modificados contados SEPARADAMENTE**, com o blast radius
lido no código antes de projetar, e **os modificados em pares** (todo arquivo de
produção modificado arrasta o teste dele):

| | contagem | linhas à mão |
|---|---|---|
| criados | **2** | ~125 |
| modificados (produção) | **6** | ~160 |
| modificados (teste) | **4** | ~180 |
| **total** | **12** | **~465** |

**O blast radius do campo obrigatório está MEDIDO, e ele é pequeno — pelo motivo
oposto ao do caso que a convenção registra.** A quarta medição achou 21 arquivos
de fixture ao tornar um campo obrigatório num tipo de domínio compartilhado.
Aqui, acrescentar três campos obrigatórios a `ErrorInsights` arrasta **2**
arquivos: `grep -rn "rejectedCount:" src/` acha os sítios de **construção
completa**, e há só dois — `errorsFixture` (`systemInsightsFixture.ts:99`) e
`CORPO_AUSENTE` (`SystemInsightsPage.tsx:90`). Os **26** sítios de
`errorsFixture({ … })` nos testes — 13 em `FailuresCard.test.tsx`, 10 em
`FailureReasonsCard.test.tsx`, 3 em `NonTerminalBanner.test.tsx` — **não** são
tocados pelo compilador, porque passam `Partial<ErrorInsights>`. Dos 26, **16**
declaram `rejectedCount` e são os que a change re-semeia à mão: não porque o
compilador obrigue, mas porque o campo que o caso afirma mudou de nome.

**A causa é estrutural e vale carregar:** o blast radius de campo obrigatório é o
número de sítios que constroem o tipo **inteiro**, não o de arquivos que o usam.
Um tipo com **construtor de duplo único** tem blast radius 2 por construção; um
tipo montado em literal em cada teste tem blast radius igual ao número de testes.
**A régua para projetar:** contar sítios de construção completa (`grep` por um
campo obrigatório **existente**), não arquivos que importam o tipo. Enumerado de
graça, como o `tsc` da quarta medição.

**Casos de teste — três categorias, e as negativas contadas por ESTADO
OBSERVÁVEL** (régua da 5a-4: contar afirmações subestima, contar estados acerta),
lidas dos cenários do delta e não da lista de coisas que a tela se recusa a dizer:

| categoria | projeção |
|---|---|
| casos **novos** | **14** |
| casos **reforçados** | 3 |
| casos **adaptados** | 6 |
| **negativas** (dentro dos novos e adaptados) | **8** |
| **semeadura de fixture como arranjo compartilhado** | **16 sítios** a re-semear, 1 linha cada — não são casos |

As **8 negativas**, por estado observável: (1) *"Recusadas na entrada"* não lê
`rejectedCount`; (2) `rejectedCount` não aparece em parte nenhuma da página; (3)
os dois números não aparecem somados; (4) o motivo desconhecido não é omitido; (5)
o motivo desconhecido não pega o rótulo de outro; (6) nenhuma causa é nomeada com
`rejectionsByReason` vazio; (7) regime desconhecido não recebe rótulo de outro
regime; (8) os motivos de recusa não entram na lista rasa das fases.

**Suíte projetada: ~1388 / 118** (1374 + 14 novos; adaptados e reforçados não
somam contagem; 1 arquivo de teste novo).

**A proporção comentário:lógica, projetada como item próprio**, porque o
entregável inclui registro de decisão — nessas, o comentário é o produto. A change
entrega **três registros de mecanismo**: (a) por que a decisão é oposta à da aba do
agente saindo da mesma régua; (b) por que a lista rasa do artboard é dividida; (c)
por que o módulo de rótulos nasce aqui e a convenção 2 **não** foi atingida. A
tabela da convenção 18 mede três registros em **2,3:1** e um em **1,3:1**.

**Projeção: ~2,0:1 em produção** — abaixo dos 2,3:1 da change de três registros,
porque esta entrega também JSX real (o grupo, o mapa de regime, a troca de campo),
e a de 2,3:1 era quase toda registro. **~1,3:1 em teste.** Das ~160 linhas de
produção modificada, ~105 projetadas como comentário e ~55 como lógica.

**Direção de erro nomeada — e a convenção diz que nomear direção não substitui
contar componentes**, então vai nomeada com essa ressalva: o risco maior é para
**cima** nas linhas de comentário do `FailureReasonsCard.tsx`, cujo cabeçalho
inteiro (22 linhas) descreve a L1 como aberta e é reescrita completa, não emenda.

## Risks / Trade-offs

- **[A divergência do artboard vira rodada de conferência, e talvez duas]** → A D2
  e a D7 mudam a forma de um card que o dono já conferiu treze vezes. **Mitigação:**
  as duas estão nomeadas aqui **antes** da implementação, com o gatilho de volta
  escrito, e a conferência é quadro a quadro (convenção 14). A D2 tem alternativa
  pronta se ele preferir a lista rasa — e nesse caso o texto do card de Falhas é
  que muda.
- **[Os dois guardas invertidos são o ponto mais frágil da change]** → Reescrever
  um guarda negativo é a operação em que a convenção 15 mais cobra: o novo pode
  reprovar antes e depois da correção, ou reprovar no componente errado.
  **Mitigação:** cada um dos dois é reintroduzido contra o defeito real — causa
  nomeada com `rejectionsByReason` vazio — e a reprovação é conferida **na classe
  completa**, não isolada (a quinta forma da convenção 15: conferir isolado é o
  jeito enganoso).
- **[A saída de `rejectedCount` da tela é perda de informação real]** → É um número
  medido que deixa de ser apresentado. **Mitigação:** issue própria com gatilho
  observável (achado 1), que é o único registro que sobrevive ao archive
  (convenção 23). **Não** mitigado por texto na tela: declarar na tela o que se
  escolheu não mostrar é a convenção 13 ao contrário, e foi o que a
  `fechamento-da-l4` acabou de decidir para o caso gêmeo.
- **[O regime `rejection` pode não estar no mapa de uma instância antiga]** →
  `MetricsOptions.All` o exige e a validação de startup o cobre, mas a tela lê
  `insights.regimes[name]` e já trata `undefined` devolvendo nota nenhuma. Risco
  coberto pelo caminho existente; **sem** cenário novo, e a justificativa é esta
  linha (convenção 10).
- **[A versão do canvas mudou e não há cópia para diffar]** → Lido `1790403198-56f9`
  em 27/09 contra `1790386152-b562` registrado em 26/09. **Mitigação:** a versão vai
  ao lado de cada afirmação sobre o artboard (convenção 22), e o que os dois cards
  desenham não contradiz nenhum registro da #52. **Não** mitigado por cópia no
  repositório — guardar artboard versionado no repo é decisão própria, não efeito
  colateral desta change.

## Open Questions

- **A lista rasa contra os dois grupos (D2) é decisão do dono na tela.** A change
  entrega os dois grupos, que é a forma que não contradiz o texto do card vizinho;
  se ele preferir a lista rasa, o que muda com ela está escrito na D2.
- **A descida da nota de regime do cabeçalho do card para os grupos (D7)** mexe num
  elemento que ele posicionou. Nomeada, não decidida por mim.
