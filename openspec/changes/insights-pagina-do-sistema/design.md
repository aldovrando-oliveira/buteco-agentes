## Context

Etapa 4 da linha `metricas-de-operacao`, em `apps/frontend`. As etapas 1, 2 e 3
estão aplicadas, deployadas e verificadas: `GET /insights/system` responde, está
roteada no nginx, e o contrato de janela, fuso, nulo e regimes está fixado.

### A limitação de fonte que a etapa 3 declarou fecha aqui

A `rotas-de-agregacao-sistema` registrou no `design.md` que **não abriu os
protótipos** — o MCP do Claude Design recusou a autenticação
(`FIRST_PARTY_AUTH_REJECTED`) — e trabalhou do `02` e dos `design.md`
arquivados, com a regra de que o protótipo vence em divergência.

**Nesta change os três artboards foram lidos**, em 23/09/2026, pelo canvas
`R9JSCiYw7pBEognkKhiaDh`. O servidor MCP `claude-design` **continua recusando a
conexão nesta sessão**; o acesso saiu por outro caminho — leitura direta dos
arquivos publicados do artifact. Os três:

| artboard | o que decide |
|---|---|
| `Main.dc.html` | a página, tema escuro — 1360 × 1880 |
| `Estados.dc.html` | os seis quadros de exceção — a **gramática de exibição** |
| `Insights-Claro.dc.html` | o tema claro, em que **a escala do mapa de calor inverte** |

### As três notas do `canvas.json`, e o que cada uma decide

O `canvas.json` carrega notas do autor coladas ao lado dos boards. Elas **não**
são legenda: duas registram decisão de exploração, e a terceira é autoridade
para o tratamento que a D1 escolheu. Nenhuma delas estava em registro nenhum do
repositório.

**Nota `dados` (azul, ao lado do `Main`)** — a composição dos dados de exemplo,
e no fim uma decisão:

> *"Cache lido é coluna da tabela de modelos, não card: a exploração mostrou que
> só um dos três provedores reporta escrita, e lá zero colapsa com
> não-reportado."*

Ela também fixa que das 563 chamadas ao provedor **41 são compactação de
histórico e não turnos do usuário** — isto é, a separação do subtítulo do KPI
(L2) é resultado de exploração, não enfeite.

**Nota `cenarios` (verde, ao lado dos três boards do agente)** — uma estrutura,
três preenchimentos, com os quadros nas mesmas posições; o card de Delegação com
as duas seções sempre, e o lado sem vínculo recebendo o quadro tracejado. E uma
frase que **acrescenta um cenário que o escopo da etapa 5 não nomeia**:

> *"O quarto cenário, agente que não delega nem é delegado, é o mesmo card com as
> duas seções tracejadas."*

Há três artboards de agente e **quatro** cenários. O quarto não tem board porque
é derivável dos outros, mas quem implementar a etapa 5 lendo só a lista de
arquivos não o encontra. Achado registrado na issue da etapa 5, fora do escopo
desta change.

**Nota `estados` (laranja, ao lado do `Estados`)** — a que é contrato de leitura,
e a que autoriza a D1:

> *"Os estados de exceção são metade do protótipo, não um apêndice. O quadro 6
> mudou: agora registra a métrica que a exploração recusou. Contrariar o
> protótipo é resultado legítimo e vira registro, com gatilho para voltar — não
> implementação silenciosa."*

**Nenhuma das três contradiz decisão registrada — e uma delas a confirma, o que
muda o peso de uma das lacunas.** O `02:4994` registra, com as mesmas palavras:
*"Cache lido: coluna da tabela de modelos, sem número nem card próprio."* A L3,
então, não remove enfeite de protótipo: remove uma coluna que a exploração
**decidiu manter** depois de ter **recusado** a irmã dela — o quadro 6 do
`Estados.dc.html` é justamente o registro da recusa do cache **escrito**, e o
cache **lido** sobreviveu àquela recusa. A L3 sobe de "divergência com o
protótipo" para "divergência com decisão registrada", e é por isso que ela entra
com issue própria e não só com texto na tela.

E o `02:4671` mostra que a célula vazia daquela coluna **não é hipótese**:
`CachedInputTokens` chegou *"nulo em todas e zero em nenhuma"* nas chamadas
medidas.

### O que foi lido da rota real, não deste documento

A rota foi chamada contra o banco de desenvolvimento em 23/09/2026 22:22 -03,
com `apps/api` subido localmente:

```
GET /insights/system?from=2026-08-24T00:00:00Z&to=2026-09-23T23:59:59Z
→ 200, 1.479 bytes, 2,24 s
```

O corpo real, resumido pelo que ele **decide** (o JSON sai em `camelCase`):

| bloco | o que veio |
|---|---|
| `window` | `from`/`to` ecoados, `timeZone: "America/Sao_Paulo"` |
| `regimes` | mapa: `execution` `2026-09-22T01:21:00-03:00`, `embedding` `2026-09-23T01:18:00-03:00` |
| `volume` | `executedTaskCount: 0`, `externalOriginTaskCount: 0` — zeros **medidos** |
| `temporal` | `dailySeries: []`, `byWeekday: []`, `peakWeekday: null` |
| `tokens` | todos os agregados **nulos**; `byAgent`, `byProvider`, `byModel` vazios |
| `performance` | todas as estatísticas nulas, `sampleCount: 0`; `caveats` com 2 códigos |
| `errors` | contagens zero; `nonTerminal.neverConsumedCount: 5`; `caveats` com 3 códigos |
| `delegation` | `pairs: []` |

**Três fatos do corpo real contrariam o que se supunha, e os três mudam o
desenho:**

1. **`caveats` não existe em todo bloco.** Só `performance` e `errors` têm o
   campo — conferido no corpo e em `SystemInsightsResponse.cs`. Os cinco códigos
   chegam por esses dois, e a tela precisa saber a **qual número** cada um se
   aplica, porque o bloco que o carrega não é o único que ele limita.
2. **`from` e `to` são obrigatórios** (`InsightsPeriod.MissingBoundMessage`). Sem
   os dois a rota responde `400` — não existe período implícito. A janela é
   responsabilidade de quem pergunta.
3. **A omissão de dia não distingue os dois casos sozinha** — e isso contradiz
   a spec da change A, que exige `0` para o dia medido e vazio. Ver D2, onde a
   decisão é corrigir a rota.

### O que foi lido do código, não da memória (convenção 6)

| fato | evidência |
|---|---|
| `dailySeries` sai de `group by` sobre linhas existentes | `GetSystemInsightsQueryHandler.cs`, consulta de M10/M7 — **nenhum `generate_series`** |
| `byWeekday` idem | mesma consulta, `group by extract(dow …)` |
| `byModel` não tem cache por modelo | `ModelTokenResponse(Provider, Model, TotalTokens, CallCount)` |
| `byAgent` traz só tokens | `AgentTokenResponse(AgentId, InputTokens, OutputTokens)` — **sem nome, sem tasks, sem duração** |
| `taskDuration` mede **média**, não mediana | `avg(ms)` + `percentile_cont(0.95)`; não há `percentile_cont(0.5)` |
| `Purpose` (`Turn`/`Compaction`) existe na tabela | `ExecutionMetricsValues.Purpose`, `ProviderCall.Purpose` — e **não é exposto** pela rota |
| `CallCount` conta **todas** as chamadas | consulta de M15/M16 sem filtro de `Purpose` |
| variável por esquema é o mecanismo da casa | `theme.ts`, `cssVariablesResolver` — `--buteco-surface-subtle`, `--buteco-brand-ink` |
| `request<T>` é duplicado por feature | `agentsApi.ts`, `sessionsApi.ts`, `channelsApi.ts`, `mcpServersApi.ts` |
| janela rolante calculada dentro do `queryFn`, chave fixa | `activityWindow.ts` + `useSessions.ts` |
| o travessão com razão ao lado já é idioma | `InventoryPage.tsx`, quatro estados por item |

**Um achado de ambiente, de fora do escopo mas que custa tempo a quem vier:**
subir `apps/api` nesta máquina exige `TZ=America/Sao_Paulo` no ambiente. Sem a
variável, o macOS resolve o fuso local como `Brazil/East`, a checagem de boot da
D2 da etapa 3 reprova e a aplicação não sobe:
`Fuso horário não configurado corretamente: TZ='America/Sao_Paulo' não
corresponde ao fuso resolvido 'Brazil/East'`. A checagem está **certa** — é
exatamente o caso que ela existe para pegar. O item vai para o `02`.

## Goals / Non-Goals

**Goals:**

- A página `/insights` do escopo do sistema, montada a partir de `Main.dc.html`,
  nos dois esquemas de cor.
- A gramática dos quatro estados fixada em **comportamento verificável**,
  inclusive pelos guardas negativos.
- A distinção entre período não medido e período medido sem uso, **lida da série
  que a rota devolve** e testada — é a distinção que as três etapas anteriores
  existiram para preservar, e a D2 decide que ela se resolve no servidor.
- "Medindo desde" **por regime**, absorvendo um terceiro sem mudar de forma.
- Os cinco códigos de parcialidade renderizados ao lado do número que limitam.
- As lacunas entre o protótipo e a rota **declaradas na tela**, com gatilho e
  posição no `02`.

**Non-Goals:**

- **A aba Insights do agente** — etapa 5. Os três cenários de delegação dela
  estão em `Agente-Insights.dc.html`, `Agente-Delegado.dc.html` e
  `Agente-Misto.dc.html`, e nenhum deles foi usado para decidir nada aqui.
- **Coletar o motivo das recusas** (M29) — change própria, decidida para **depois
  desta**, para nascer sabendo como o card precisa do dado.
- **Tocar `apps/api`, `apps/workers`, `apps/inbox`, `libs/` ou o `nginx.conf`.**
- **Acrescentar métrica à rota** — nem a separação turno × compactação, nem o
  cache por modelo, nem as três colunas por agente. Convenção 1: achado da etapa
  de UI é sequenciado, nunca backend de improviso dentro da change de tela.
- **Corrigir a série diária aqui.** A D2 decide que a rota passa a emitir `0`
  dentro do regime, e essa correção é **change própria em `apps/api`**, que
  bloqueia esta. Pela mesma convenção 1: a decisão é desta change, a
  implementação não.
- Índice, instâncias, detector de tasks não-terminais, ou qualquer item aberto da
  linha.
- Biblioteca de gráficos (D13).
- Componente compartilhado com a etapa 5 (D14).

## Decisions

### D1 — O protótipo é a fonte, e onde ele não pode ser cumprido a divergência é registrada

Os três artboards foram lidos e são o contrato da tela. Mas **o protótipo não
vence contra dado que não existe**: ele desenha colunas e subtítulos cuja fonte
não está na rota, e a convenção 1 proíbe resolver isso com backend de improviso.

A regra desta change, então, tem duas metades:

- **onde a rota serve**, o protótipo decide forma, ordem, rótulo e cor;
- **onde a rota não serve**, o elemento entra no estado que o próprio protótipo
  desenhou para isso — o quadro 6 de `Estados.dc.html`, a **lacuna declarada** —
  e a divergência vira registro com gatilho e posição (convenção 9).

Card ausente é invisível; card com lacuna declarada é item aberto que se vê.

**Alternativa recusada:** cumprir o protótipo ao pé da letra, inventando o dado
que falta (somar `0`, repetir um número de outro nível, estimar). Recusada pela
convenção 13 — seria a tela afirmando o que o sistema não sabe, que é o defeito
que esta tela existe para não cometer.

**Alternativa recusada:** omitir em silêncio os elementos sem fonte. Recusada
porque some com o item aberto: ninguém volta para procurar o que não aparece.

### D2 — A rota passa a emitir o zero dentro do regime, e a tela **lê** em vez de reconstruir

Este é o núcleo da tela, e a decisão mudou depois que a spec da change A foi
lida linha a linha. Ela está escrita aqui **com a causa**, porque a primeira
redação deste documento decidia o contrário.

**O que a spec de `system-insights-aggregation` exige** (linhas 232-234), em
duas metades:

> *"A série diária SHALL omitir — nunca emitir `0` para — os dias anteriores ao
> início do regime a que a métrica pertence. **Dentro do regime, um dia sem
> ocorrência SHALL emitir `0`.**"*

**A segunda metade não está implementada.** A consulta de M10 é um `group by`
sobre as linhas de `task_executions` que existem na janela; não há
`generate_series`. Um dia **dentro** do regime, medido e sem nenhuma task, é
omitido exatamente como um dia anterior ao regime.

**E o guarda da change A cobriu só a primeira metade.** Conferido no arquivo:

| guarda | o que afirma | o que deixa de fora |
|---|---|---|
| `DaysBeforeTheRegime_AreOmitted_NeverEmittedAsZero` | nenhum dia da série é anterior ao regime | passa trivialmente com a série **vazia** |
| `MeasuredAndEmptyPeriod_ArrivesAsZero_NotAsNull` | o zero medido chega como número | afirma sobre **`volume.executedTaskCount`**, um escalar — **não** sobre `dailySeries` |

É a régua *"o par não é universal"* mordendo no sentido inverso: aqui o par
**era** exprimível, e não foi escrito. O requisito positivo ficou sem guarda e
sem implementação, e o negativo sozinho é satisfeito pela ausência de dados.

#### A decisão: a rota emite o `0`, e a spec continua verdadeira

**Decidido pelo dono, com a recomendação acatada.** `generate_series` no
servidor, sobre a interseção entre a janela pedida e o regime. A `dailySeries`
passa a ter um ponto para **todo** dia medido, e a ausência de um dia passa a
significar **uma** coisa só: não medido.

**Quatro razões, e a quarta é a que decide:**

1. **A spec está certa e foi revisada.** Alterar um contrato correto para
   acomodar uma implementação incompleta é a direção errada da correção.
2. **O regime é do servidor.** Ele vem de configuração (`Metrics:Regimes:*`), e
   o cliente só o conhece porque a rota conta. Reconstruir no cliente é
   rederivar, numa segunda linguagem, um fato que o servidor já tem.
3. **A divergência seria silenciosa.** Se o recorte de regime do servidor mudar
   — a função `Later()` que casa janela e regime —, a regra do cliente
   desanda sem erro, sem log e sem sintoma: os números continuam plausíveis.
4. **Com a reconstrução, o cliente *inventaria* zeros.** A D5 da change A
   proibiu agregação no cliente porque ela *"colapsa nulo em zero"*. Sintetizar
   `0` para dias que o servidor omitiu é da mesma família — o cliente
   fabricando valor medido — e que hoje acerte depende inteiramente de a cópia
   da regra no cliente bater com a do servidor.

**O efeito sobre esta change é uma simplificação, não um custo.** A regra do
cliente colapsa em ler o que o servidor disse:

| condição | estado da célula |
|---|---|
| `d` tem ponto em `dailySeries` com contagem > 0 | valor medido |
| `d` tem ponto em `dailySeries` com contagem `0` | **zero medido** |
| `d` está na janela e **não** tem ponto | **não medido** — hachurado |
| a consulta não respondeu | travessão, e nenhuma célula é desenhada |

O conjunto de dias medidos **é** o conjunto de dias da série. A tela não lê
`regimes` para decidir célula nenhuma — ela lê `regimes` só para o texto
"medindo desde" (D4 da change A, e o requisito próprio desta spec).

#### O que isso cria: uma dependência, e ela é de sequenciamento

**Esta change passa a depender de uma correção em `apps/api`**, e a correção é
**change própria** — o Non-Goal *"não tocar `apps/api`"* continua valendo aqui.
Convenção 1: achado da etapa de UI é sequenciado, nunca feito de improviso
dentro da change de tela.

A correção entrega três coisas: o `generate_series` na consulta de M10, o
**guarda positivo** que faltava (dia medido e vazio aparece na série com `0`), e
a correção do XML doc de `DailyInsightPoint`, cuja afirmação — *"Só existem
pontos para dias dentro do regime"* — passa a ser verdadeira em vez de
aspiracional.

**Gatilho: antes do apply desta change.** Issue própria, e ela bloqueia a #52.

**Alternativa recusada: reconstruir no cliente e alterar a spec da A por delta
`MODIFIED`.** É a opção honesta da dupla — decidir contrato dentro de change de
tela **sem** delta é que não seria —, e foi recusada pelas quatro razões acima.
O custo aceito da escolhida é o sequenciamento: esta change espera uma correção
de backend que ainda não existe.

**Duas coisas da redação anterior sobrevivem, e as duas continuam sendo da tela:**

**O dia local do regime conta como medido, inteiro.** O regime começa às 01:21,
então 22/09 foi medido em 22h40 das suas 24h. Com o `generate_series` recortado
pelo regime, o servidor emite esse dia com a contagem que mediu; a tela o
apresenta como medido. Desenhar um quarto estado visual para "parcialmente
medido" inventaria um estado que o protótipo não desenhou. A parcialidade é
declarada **em palavras**, no cabeçalho — "medindo desde 22/09/2026 01:21".

**O dia da semana continua sendo reconstruído no cliente, e agora a partir da
série.** `byWeekday` tem o mesmo defeito — omite o dia da semana sem ocorrência
—, e a spec da A **não** o cobre: a frase é sobre a série diária. Mas com a
`dailySeries` completa, a cobertura sai dela e não do regime: os dias da semana
medidos são os que aparecem entre os dias da série. Um dia da semana que **não
ocorre** na série nunca foi medido, e preencher a barra com `0` afirmaria uma
medição que não houve — ele entra como **célula vazia**. É a mesma fonte única,
uma pergunta depois.

### D3 — Toda aritmética de dia usa o fuso **da resposta**, nunca o do navegador

`window.timeZone` vem `"America/Sao_Paulo"`, e é o fuso em que o balde diário foi
feito no servidor. Um operador acessando de outro fuso veria o calendário
deslocar se a tela usasse o fuso do navegador — o mesmo defeito de 27,7% que a
etapa 3 mediu, agora na outra ponta.

A conversão de instante para dia local sai de
`Intl.DateTimeFormat('sv-SE', { timeZone })`, que devolve `YYYY-MM-DD` — a mesma
forma em que `dailySeries[].day` já chega (`DateOnly`). Comparar e ordenar
strings nesse formato é comparação lexicográfica correta, e **nenhum `Date` local
entra no caminho**.

**Alternativa recusada:** `toLocaleDateString` com `pt-BR`. Recusada porque
devolve `DD/MM/AAAA`, que não ordena; a formatação para exibição é outra função,
e é só ela que usa `pt-BR`.

**Alternativa recusada:** biblioteca de datas (`date-fns-tz`, `luxon`).
Recusada: dependência nova para duas conversões que o `Intl` do runtime já faz.

### D4 — A janela é **rolante em instantes**, e a escolha fica em estado do componente

`from`/`to` são obrigatórios. A tela oferece 7d, 30d e 90d — os três do
protótipo, com 30d marcado.

- **Rolante, não de calendário**, pelo precedente escrito de `activityWindow.ts`:
  "desde a meia-noite de N dias atrás" obrigaria a decidir de qual fuso é a
  meia-noite e mudaria de tamanho com o horário de verão. São N × 24 h de relógio
  absoluto.
- **A janela nasce dentro do `queryFn`**, e a chave de cache leva o **período**
  (`['insights', 'system', '30d']`), não os instantes. Chave com instante geraria
  chave nova a cada render — o relógio andou — e a tela ficaria presa em
  "Consultando…" para sempre. É literalmente o defeito que o comentário de
  `useSessions.ts` registra.
- **A escolha não vai para a URL.** O protótipo não tem parâmetro, e um
  `useSearchParams` aqui seria escopo que ninguém pediu. **Gatilho para
  acrescentar:** o primeiro pedido de compartilhar um link de período.

**Custo nomeado:** recarregar a página volta para 30d. Aceito, e é o
comportamento do protótipo.

### D5 — Uma consulta para a página, e o catálogo de agentes é a **segunda**

A rota é uma só por decisão da etapa 3 (D4 de lá: janela, fuso, nulo e "medindo
desde" são um contrato só). A página faz **uma** requisição a `/insights/system`.

Mas `AgentTokenResponse` e `AgentFailureResponse` trazem `agentId`, e **não o
nome**. A tabela "Consumo por agente" precisa do nome, e ele vem de
`useAgentsQuery` — que já existe, já é consumida pelo inventário, e compartilha
o `QueryClient` e a chave de cache com a listagem de agentes. Nenhuma rota nova,
nenhuma consulta paralela.

**Os dois estados de carregamento são independentes**, pelo precedente escrito de
`InventoryPage.tsx`: nada de `isLoading || outraQuery.isLoading`. A tabela mostra
os números assim que a agregação responde, e o nome entra quando o catálogo
responde.

**Agente presente na agregação e ausente do catálogo** — removido do cadastro
depois de ter executado — mostra o identificador abreviado em monoespaçada, não
travessão: o travessão é "não sei o número", e aqui o número é conhecido; o que
falta é o rótulo. A linha não some, porque o consumo dela é real.

**Alternativa recusada:** chamar `/insights/agents/{id}` por linha para completar
as colunas que faltam. Recusada por ser N requisições a uma rota de **outro
escopo**, e por ser a etapa 5 vazando para dentro da etapa 4.

### D6 — A gramática dos quatro estados é **um módulo**, e o guarda é negativo

Um único ponto decide o que um valor vira na tela:

| entrada | saída |
|---|---|
| `number` | o número formatado |
| `null` ou ausente | **célula vazia** — sem conteúdo, com a linha preservada |
| consulta sem resposta | **travessão**, com a razão ao lado |
| `0` | **zero**, escrito |

**Nenhum `?? 0`, nenhum `|| 0`, nenhum `Number(x)` no caminho de apresentação.**
O módulo recebe `number | null | undefined` e o estado da consulta; quem chama
não decide nada.

**As asserções que protegem isto são negativas** — a convenção 13 e o quadro 5 do
`Estados.dc.html` pedem a mesma coisa: o teste afirma **a ausência do zero** onde
a origem é nula, não a presença do vazio. Um teste que só afirmasse "renderiza
vazio" passaria por acidente se alguém trocasse o vazio por outra coisa qualquer
que não fosse zero.

**E o zero medido é dito por extenso onde cabe**, seguindo `InventoryPage.tsx`:
"Nenhuma task neste período", não `0`. O quadro 2 do `Estados.dc.html` desenha
exatamente esse estado, e a frase é o que o distingue do quadro 3.

### D7 — A escala do mapa de calor é **variável por esquema** (convenção 15)

O papel troca de ponta da escala entre os esquemas, e esse é precisamente o caso
que a convenção 15 nomeia. Conferido nos dois artboards:

| passo | escuro (`Main`) | claro (`Insights-Claro`) |
|---|---|---|
| zero | `#24272c` = `dark[6]` | `#f1f1f1` = `gray[2]` |
| 1 | `#1e4d92` = `butecoBlue[8]` | `#d6e3f6` = `butecoBlue[2]` |
| 2 | `#245cad` = `butecoBlue[7]` | `#b0c8ee` = `butecoBlue[3]` |
| 3 | `#2a6ecb` = `butecoBlue[6]` | `#6495db` = `butecoBlue[4]` |
| 4 | `#4180d4` = `butecoBlue[5]` | `#4180d4` = `butecoBlue[5]` |
| 5 | `#6495db` = `butecoBlue[4]` | `#2a6ecb` = `butecoBlue[6]` |

No escuro a rampa vai do tom 8 ao 4 — **cresce clareando**. No claro vai do 2 ao
6 — **cresce escurecendo**. É a mesma paleta percorrida em sentidos opostos, e
**nenhuma cor nova entra**: os doze valores já existem em `theme.ts`.

Seis variáveis — `--buteco-heat-0` a `--buteco-heat-5` — entram no
`cssVariablesResolver`, declaradas nos dois blocos, ao lado de
`--buteco-surface-subtle` e `--buteco-brand-ink`, que estão lá pelo mesmo
critério. O componente lê a variável e **nunca um tom**.

**A quantização é do cliente, e é linear sobre a série medida.** O protótipo
crava limiares (8/16/24/30) que só fazem sentido para os dados de exemplo dele.
Cinco faixas iguais sobre `[1, máximo medido]`; o zero medido tem passo próprio;
o dia não medido não usa a escala — é hachura. Função pura, testada sem jsdom.

**Alternativa recusada:** quantis da distribuição. Recusada por não ter pedido, e
porque com poucos dias medidos o quantil inverte a leitura de intensidade sem
aviso.

### D8 — As cinco lacunas entre o protótipo e a rota

Todas entram no estado de lacuna declarada, todas viram item no `02` com gatilho.

| # | o protótipo desenha | a rota serve | tratamento |
|---|---|---|---|
| L1 | "Motivos" com linha *"Agente sem provider ou modelo configurado — 5"* | `errors.rejectedCount`, **sem motivo** (`rejection-reason-not-collected`) | as recusas entram no card como **uma linha de lacuna declarada** com a contagem e o texto do caveat; a causa **não** é nomeada |
| L2 | KPI "Chamadas ao provedor" com *"522 de turno · 41 de compactação"* | total derivável; **`Purpose` não é exposto** | o KPI mostra o total; o subtítulo vira a lacuna declarada |
| L3 | "Modelos de conversa" com coluna **Cache lido** — e o `02:4994` a registra como decisão, não como desenho | `byModel` não tem cache; só existe o total global em `conversation.cachedInputTokens` | a coluna **não entra**; a lacuna é declarada no rodapé do card |
| L4 | "Consumo por agente" com **Tasks**, **Tokens por task**, **Duração p95** | `byAgent` traz só tokens; `errors.byAgent` traz falhas | as três colunas **não entram**; lacuna declarada no rodapé, e o nome do agente leva ao detalhe dele |
| L5 | KPI "Duração da task (p95)" com subtítulo *"mediana 4,1 s"* | `taskDuration.averageMs` — **média**, não mediana | o subtítulo diz **"média"**; ver D9 |

**O total de L2 sai de `sum(byModel[].callCount)`**, e a escolha tem razão: o
`CallCount` é documentado como *"contagem medida — pode ser zero"*, é `int` e
nunca nulo, e a consulta que o produz não filtra `Purpose`. Somar contagens não
colapsa nada — a proibição da D5 da etapa 3 é sobre **agregar linha bruta**, que
colapsaria nulo em zero, e aqui não há nulo para colapsar.
`performance.providerCallDuration.sampleCount` daria o mesmo número hoje, e foi
**recusado como fonte**: por contrato aquele campo é uma **amostra** ("quantas
entraram no cálculo — não quantas existem"), e a igualdade de hoje é incidental.

**L3 é a mais pesada das cinco, e o `canvas.json` é o que mostra isso.** A
coluna "Cache lido" é o exemplo canônico da célula vazia no protótipo, é
**decisão de exploração registrada** no `02:4994` — *"coluna da tabela de
modelos, sem número nem card próprio"* — e é a irmã sobrevivente de uma métrica
que a exploração **recusou** (o cache escrito, quadro 6). E ela sai. **A demonstração da célula
vazia não se perde**: ela continua em "Consumo por provedor", na coluna
Embedding — que o `Main.dc.html` já desenha vazia para `anthropic` e `gemini`,
com o mesmo rodapé. O requisito da célula vazia é cumprido pela tabela que tem
a fonte.

**Posições no `02`:**

- L1 é a change de coleta de M29, **já decidida para depois desta**;
- L2, L3 e L4 são mudanças de `apps/api` na rota do sistema, **sem posição
  fixada** — item aberto com gatilho: o primeiro pedido do dono por qualquer uma
  delas, ou a etapa 5, que ao abrir a aba do agente torna L4 redundante por
  outro caminho;
- L5 é uma linha de SQL (`percentile_cont(0.5)`), e entra junto de L2 se L2 for
  feita.

### D9 — "mediana" vira "média", e a palavra é o contrato

O subtítulo do protótipo diz *"mediana 4,1 s"*. A rota calcula `avg(ms)`. Escrever
"mediana" sobre uma média é a tela afirmando o que o sistema não mediu — o mesmo
defeito que fez a métrica M25 não se chamar "tempo em tools".

**O protótipo vence em forma, não em fato.** A posição, o tamanho, a cor e a
estrutura do card são os do protótipo; a palavra é a da medição.

**Registrado como divergência** (convenção 9), com gatilho: se o dono quiser a
mediana, é uma linha de SQL em `apps/api`, e o rótulo volta a ser o do protótipo.

### D10 — Recusa não vira percentual, e falha e recusa não se somam

O protótipo mostra *"12 · 2,5% das tasks"* e *"5 · 1,1% das tasks"*. O primeiro
percentual é legítimo: `failedCount` e `executedTaskCount` contam a mesma
população.

**O segundo não é, e por dois motivos ao mesmo tempo.** Recusa feita por
`apps/api` **não produz linha de execução** (`rejections-missing-from-executions`):
o numerador subconta, e o denominador é de outra população. Um percentual assim
parece preciso e é duplamente errado.

**Decisão:** a recusa mostra a **contagem**, e no lugar do percentual vai o texto
do caveat. A falha mantém o percentual.

**A separação entre os dois continua sendo o requisito principal do card** — e o
texto do protótipo sobre isso é excelente e fica literal: *"Recusa é agente
inativo ou sem provider e modelo configurados: nunca chega a processar. Falha é
execução que começou e quebrou. Somar os dois esconde qual dos dois problemas
existe."*

### D11 — O banner de tasks não-terminais: duas populações, e **sem link**

`errors.nonTerminal` traz `openExecutionCount` e `neverConsumedCount`
**separados**, e o XML doc diz por quê: execução aberta é task consumida e ainda
rodando; task nunca consumida é task publicada que instância nenhuma pegou — e
esta última nem tem linha em `task_executions`. Somá-las no banner apagaria a
causa, que é a única coisa acionável ali.

**Duas divergências com o protótipo, as duas registradas:**

- O protótipo diz *"há mais de 30 dias"*. **A rota não filtra por idade**, e o
  caveat `point-in-time-only` diz que é leitura do instante da consulta. O banner
  não afirma idade; diz "no instante desta consulta".
- O protótipo tem *"Ver as tasks"*. **Não existe listagem de tasks no painel.**
  Um link para lugar nenhum é pior que nenhum link, e a convenção do inventário
  já recusou atalho para listagem que não conta o mesmo conjunto. O link não
  entra; **gatilho** para acrescentá-lo: a primeira tela de tasks.

O banner só aparece quando a soma das duas contagens é maior que zero. Zeros
medidos não geram aviso.

### D12 — Os caveats saem ao lado do número que limitam, com mapa fechado

Cinco códigos, e o bloco que os carrega **não é o único que eles limitam**:

| código | onde a rota o entrega | onde a tela o mostra |
|---|---|---|
| `submitted-at-missing-on-redelivery` | `performance` | KPI de duração da task |
| `residual-is-not-only-tools` | `performance` | não tem elemento no protótipo — **não é renderizado** |
| `rejections-missing-from-executions` | `errors` | card de Falhas, no lugar do percentual de recusa |
| `rejection-reason-not-collected` | `errors` | card de Motivos, na linha de lacuna declarada |
| `point-in-time-only` | `errors` | banner de não-terminais |

O mapa é **fechado e explícito**, e um código desconhecido na resposta renderiza
o próprio código em monoespaçada, como aviso visível, em vez de sumir. A
convenção da casa para enum desconhecido é indicador neutro, nunca silêncio e
nunca reaproveitar o indicador de sucesso.

`residual-is-not-only-tools` não é renderizado porque o resíduo (M25) **não tem
elemento no `Main.dc.html`** — junto com `queueTime`, `providerCallDuration`,
`maxObservedDelegationDepth`, `delegation.pairs`, `indexingFailures` como bloco
próprio e `conversation.cachedInputTokens`. São campos que a rota serve e o
protótipo não desenhou; o protótipo decide o escopo da tela (convenção 9), e
inventar card para eles seria desenhar sem protótipo. Item no `02`, sem gatilho
— é escolha de produto, não lacuna.

### D13 — Nenhuma biblioteca de gráficos

Os três gráficos do protótipo são desenhados nele em SVG inline e CSS puro: a
linha de "Tasks por dia" é um `<polyline>`, o calendário é uma grade de `<div>`
e as barras de dia da semana são `<div>` com largura percentual. Nada ali pede
biblioteca.

Uma dependência de charts traria tema próprio a reconciliar com o `theme.ts`,
peso de bundle e uma segunda gramática de cor — para desenhar o que trinta linhas
de SVG já desenham. **Gatilho para reconsiderar:** o primeiro gráfico que precise
de interação (tooltip, zoom, seleção de faixa) — nenhum dos três precisa.

### D14 — Nenhum componente é extraído para a etapa 5

A etapa 5 é o **segundo consumidor previsto**, não observado, e a convenção 2 é
explícita: componente só se extrai com repetição **observada**.

E a previsão é fraca por evidência: os artboards do agente têm outra estrutura —
o card de Delegação com duas seções e quadro tracejado no lado sem vínculo não
existe nesta tela, e a tabela de modelos de lá absorveu o cache lido. Extrair
agora esconderia essas diferenças exatamente como a `frontend-inventario-*`
registrou para os seis itens dela.

**O que é compartilhado é o tema** — as seis variáveis de calor —, e isso é
contrato de tema, não componente.

**Extrações que acontecem dentro desta change são outra coisa**: `MetricValue` e
`DeclaredGap` têm repetição observada **aqui**, em quatro e mais de dez sítios
respectivamente, e a régua é cumprida.

### D15 — A página busca, os componentes apresentam

Regra escrita da casa: *"Página busca dados e repassa como prop. Componente
apresentacional nunca importa hook de query ou mutation diretamente."*

`SystemInsightsPage` detém as duas consultas (D5) e repassa. Os treze
componentes recebem props e não importam nada de `api/`. É também o que torna os
estados de exceção testáveis sem servidor: cada componente é montado com o dado
do estado que se quer afirmar.

## O protótipo contra a resposta real, elemento a elemento

| elemento de `Main.dc.html` | fonte na resposta | estado |
|---|---|---|
| cabeçalho "de … a … · medindo desde …" | `window.from/to` + `regimes` | **por regime** (D2), não texto único |
| 7d / 30d / 90d | cliente calcula `from`/`to` | tem (D4) |
| KPI Tasks executadas + externa/delegação | `volume` | tem; delegação = executadas − externas |
| KPI Chamadas ao provedor + turno/compactação | `sum(byModel[].callCount)` | **L2** — total sim, separação não |
| KPI Duração p95 + "mediana" | `performance.taskDuration` | **L5** — a palavra vira "média" |
| KPI Tokens de conversa + entrada/saída | `tokens.conversation` | tem |
| KPI Tokens de embedding | `tokens.embeddingInputTokens` | tem |
| KPI Tokens por task + p95 | `tokens.perTask` | tem |
| Dias da semana com mais atividade | `temporal.byWeekday`, `peakWeekday` | tem, com a regra de faixa medida (D2) |
| Calendário do período | `temporal.dailySeries` | tem, com os quatro estados de célula (D2, D7) |
| Tasks por dia | `temporal.dailySeries` | tem; a linha **quebra** no não medido, não cai a zero |
| Consumo por provedor | `tokens.byProvider` | tem — e é onde a célula vazia se demonstra |
| Modelos de conversa | `tokens.byModel` | **L3** — sem a coluna Cache lido |
| Consumo por agente | `tokens.byAgent` + `errors.byAgent` + catálogo | **L4** — três colunas a menos |
| Falhas | `errors.failedCount`/`rejectedCount` + `volume` | tem, sem percentual na recusa (D10) |
| Motivos | `errors.byPhase` + `errors.indexingFailures` | **L1** — recusa entra como lacuna |
| banner de não-terminais | `errors.nonTerminal` | tem, sem idade e sem link (D11) |

## Árvore de pastas proposta

```
apps/frontend/src/
├── app/
│   └── routes.tsx                              (modificado: rota /insights)
├── components/layout/
│   └── AppShell.tsx                            (modificado: item Insights)
├── theme.ts                                    (modificado: --buteco-heat-0..5)
└── features/insights/
    ├── api/
    │   ├── insightsApi.ts                      request<T>/ApiError próprios + getSystemInsights
    │   ├── insightsApi.test.ts
    │   ├── useSystemInsights.ts                chave por período, janela no queryFn
    │   └── useSystemInsights.test.ts
    ├── types/
    │   └── systemInsights.ts                   o contrato da resposta, em camelCase
    ├── test/
    │   └── systemInsightsFixture.ts            construtor da resposta para os guardas
    ├── utils/
    │   ├── insightsWindow.ts                   7d/30d/90d → { from, to } rolante
    │   ├── insightsWindow.test.ts
    │   ├── measuredDays.ts                     regime + janela → dias medidos/não medidos
    │   ├── measuredDays.test.ts
    │   ├── heatScale.ts                        quantização linear em 5 faixas
    │   ├── heatScale.test.ts
    │   ├── metricState.ts                      os quatro estados, e a formatação
    │   ├── metricState.test.ts
    │   ├── failurePhaseLabels.ts               vocabulário → rótulo, neutro no desconhecido
    │   ├── failurePhaseLabels.test.ts
    │   ├── caveatLabels.ts                     código → texto, código cru no desconhecido
    │   └── caveatLabels.test.ts
    ├── components/
    │   ├── MetricValue.tsx / .test.tsx         os quatro estados renderizados
    │   ├── DeclaredGap.tsx / .test.tsx         o quadro 6 do Estados.dc.html
    │   ├── PeriodPicker.tsx / .test.tsx
    │   ├── InsightsKpiGrid.tsx / .test.tsx
    │   ├── WeekdayActivityCard.tsx / .test.tsx
    │   ├── PeriodHeatmapCard.tsx / .test.tsx
    │   ├── DailyTasksCard.tsx / .test.tsx
    │   ├── ProviderConsumptionCard.tsx / .test.tsx
    │   ├── ConversationModelsCard.tsx / .test.tsx
    │   ├── AgentConsumptionCard.tsx / .test.tsx
    │   ├── FailuresCard.tsx / .test.tsx
    │   ├── FailureReasonsCard.tsx / .test.tsx
    │   └── NonTerminalBanner.tsx / .test.tsx
    └── pages/
        ├── SystemInsightsPage.tsx
        └── SystemInsightsPage.test.tsx
```

**Nada em `libs/`.** `libs/` é para código compartilhado entre apps do backend;
esta change é inteira de `apps/frontend`.

## Verificação

### O que a suíte cobre

A suíte de `apps/frontend` roda em jsdom, não usa Testcontainers e não sofre a
contenção que desqualificou rodadas em `apps/workers`.

- **A gramática dos quatro estados**, inclusive os **guardas negativos**: afirmar
  que **não** há `0` renderizado onde a origem é `null`.
- **A regra de dia medido** (D2): dia presente na série com `0` é zero medido;
  dia ausente da série é não medido; e o negativo — **nenhum** dia ausente da
  série renderiza contagem. A tela não consulta `regimes` para decidir célula.
- **O dia da semana não coberto pela faixa medida** não vira barra de zero.
- **A quantização da escala** como função pura, sem jsdom.
- **O mapa de caveats**, inclusive o código desconhecido.
- **O vocabulário de fase de falha**, inclusive o valor desconhecido.
- **A chave de cache por período** e a janela calculada no `queryFn`.
- **Os estados de carregamento, erro e nova tentativa** por consulta, independentes.
- **A rota e o item de navegação.**
- **Que as seis variáveis de calor existem nos dois esquemas** — contrato de
  token, que é o que a convenção 14 diz que a suíte *pode* cobrir.

### O que só a conferência manual cobre — e é do dono

jsdom não enxerga cor, contraste, layout nem quebra de linha (convenção 14). A
`frontend-mensagem-recusa-ciclo` registrou o que a conferência pega: um estado
intermediário que nenhum guarda pediria.

**O agente produz os estados; o dono julga.** A conferir, tela a tela, **nos dois
esquemas**:

1. a página com dados reais do piloto, 7d / 30d / 90d;
2. **a inversão da escala do mapa de calor** — o teste afirma que a variável
   existe, não que a rampa cresce no sentido certo;
3. o contraste da faixa hachurada contra a célula de zero medido, que é a
   distinção mais fácil de perder no claro;
4. os seis quadros de `Estados.dc.html`: coleta recém-iniciada, período medido
   sem uso, consulta sem resposta, os quatro estados de valor, provedor que não
   reporta, e a lacuna declarada;
5. o layout das duas tabelas com nomes de agente e de modelo longos — a quebra de
   linha, que jsdom não calcula;
6. os cinco textos de caveat em largura de trabalho real.

A conferência é **iterativa**: cada correção muda o que fica visível.

## Projeção da convenção 18 — décima sexta medição

**Unidade declarada antes:** `#### Scenario:` contados nos arquivos de delta desta
change — não blocos, não métodos de teste. Modificado por `git diff -w`. Três
níveis contados à parte: escrito à mão, **duplo**, gerado.

**Esta é a primeira change de `apps/frontend` desde a
`frontend-mensagem-recusa-ciclo`, e a série não tem ponto de calibração para tela
de dados.** A âncora mais próxima em natureza é a
`frontend-knowledge-index-diagnostics` — painel de diagnóstico com estados de
exceção. A `frontend-mensagem-recusa-ciclo`, que é a change mais recente,
**não** serve de razão: 8 cenários e 177 linhas para uma mensagem de erro, e
herdar essa razão seria projetar uma página inteira pelo custo de um parágrafo.

**Âncoras decompostas** (só `apps/frontend`, `git diff -w` contra o primeiro pai):

| âncora | cenários | fonte | teste | total | linhas/cenário |
|---|---|---|---|---|---|
| `frontend-mensagem-recusa-ciclo` | 8 | 1 arq, 70+ | 1 arq, 107+ | 177+ | 22 |
| `frontend-inventario-catalogos` | 15 | 4 arq, 399+ | 4 arq, 412+ | 811+ | 54 |
| `frontend-inventario-atividade-periodo` | 26 | 5 arq, 323+ | 5 arq, 541+ | 864+ | 33 |
| `frontend-knowledge-index-diagnostics` | 42 | 7 arq, 578+ | 6 arq, 1.004+ | 1.582+ | 38 |

**Projeção por componente:**

| componente | arquivos | linhas |
|---|---|---|
| cliente da rota (`api/`) | 4 | 200 |
| tipos do contrato | 1 | 110 |
| utilitários puros (fonte) | 6 | 260 |
| componentes de apresentação (fonte) | 13 | 700 |
| página | 1 | 180 |
| modificados (`routes`, `AppShell`, `theme`) | 3 | 70 |
| **fonte, subtotal** | **28** | **1.520** |
| casos de teste escritos à mão (≈48 cenários × 24) | 21 | 1.150 |
| **duplo** — construtor da resposta agregada | 1 | 180 |
| testes dos três modificados | 3 | 60 |
| **teste, subtotal** | **25** | **1.390** |
| **gerado** | **0** | **0** |
| **total `apps/frontend`** | **≈53 arquivos** | **≈2.900 linhas** |

**Faixa: 2.300 a 3.400 linhas.** A largura é deliberada: a série não mediu tela
de dados, e o item mais incerto é o custo dos componentes de apresentação, que
dependem de quanta estrutura do protótipo cabe em cada um.

**O duplo tem linha própria** porque a décima segunda medição mostrou que sem
isso a projeção erra por baixo: os casos acertaram +16/+16 e as linhas erraram
+70%, quase tudo em infraestrutura de duplo que a tabela não tinha onde contar.
Aqui o duplo é **um só** — um construtor da resposta de `/insights/system` com
padrões razoáveis e sobrescrita por bloco — e ele é o que torna barato escrever
quarenta e oito cenários que diferem num campo cada.

**Cenários projetados: ≈48** — ≈45 em `system-insights-ui` e ≈3 no delta de
`frontend-visual-theme`.

**O fechamento só compara.** Nenhum número desta seção é revisado durante o
apply. Um dado já disponível, anotado aqui **sem** alterar a projeção acima: os
dois arquivos de delta desta change fecharam em **51** `#### Scenario:` — 48 em
`system-insights-ui` e 3 em `frontend-visual-theme` —, contra os ≈48
projetados antes de eles existirem. A projeção de linhas continua a da tabela.

**E um efeito da D2 a medir no fechamento, porque ele vai no sentido contrário
do que a série de medições costuma ver:** a decisão de corrigir a rota em vez de
reconstruir no cliente **reduz** o trabalho desta change — `measuredDays.ts`
deixa de cruzar regime com janela e passa a ler a série. A projeção da tabela
**não** foi rebaixada por isso, de propósito: ela foi feita antes, e rebaixá-la
agora seria ajustar a projeção ao resultado. O fechamento mede quanto a
simplificação valeu, e esse número é o que interessa — é a primeira vez que a
série tem um caso de decisão de contrato **encolhendo** o lado do cliente.

## Baselines — remedidas, não herdadas

Medidas nesta máquina em 23/09/2026, entre 22:24 e 22:52 -03, sobre `0b189fa`:

| suíte | resultado | duração |
|---|---|---|
| `apps/frontend` | **927/927**, 84 arquivos | 1m54s |
| `apps/api` | **391/391** | 2m01s |
| `apps/workers` | **386/386** | 7m10s |
| `apps/inbox` | **203/203** | 19s |

**Os últimos registrados eram `apps/frontend` 921/921** — a suíte tem **927**
hoje, e é por isso que a baseline se remede em vez de se herdar. As outras três
bateram com os últimos registrados (`apps/api` 391, `apps/workers` 386,
`apps/inbox` 203), e o fato de baterem **não** dispensa a medição: é o que a
remedição confirmou, não o que ela assumiu.

**Uma reprovação apareceu e foi caracterizada, em vez de arredondada.** Na
primeira rodada, `apps/inbox` fechou em **202/203**, com
`DebounceSweepServiceTests.InfrastructureFailure_DuringCandidateQuery_IsLoggedAndSweepRecoversNextCycle`
estourando o `PollUntil` por `TimeoutException`. É teste sensível a tempo, e a
rodada aconteceu com `apps/workers` rodando em paralelo e `apps/api` de pé na
mesma máquina. Rodada de novo **sozinha**, a classe passa (10/10) e a suíte
inteira fecha em **203/203**. A baseline registrada é a da rodada sem
contenção; o episódio fica escrito porque "pré-existente" e "ambiental" exigem a
baseline, e porque é a mesma contenção que já desqualificou rodadas em
`apps/workers`.

**Estado do `podman ps` no momento da medição**, declarado em vez de afirmado
como zero: três containers de pé —`buteco-agents_postgres_1`,
`buteco-agents_rabbitmq_1` e `buteco-agents_waha_1`, todos `Up 21 hours`. As
suítes de backend rodaram com `DOCKER_HOST` do Podman e
`TESTCONTAINERS_RYUK_DISABLED=true`, e `apps/frontend` não usa Testcontainers.

## Risks / Trade-offs

| risco | mitigação |
|---|---|
| **Um `?? 0` entra na primeira manutenção e desfaz o trabalho das três etapas.** É a falha mais provável e a mais silenciosa: o número sai plausível. | Guardas **negativos** por cada superfície que exibe valor anulável — afirmam a ausência do zero, não a presença do vazio (D6). É a asserção que sobrevive a um refactor bem-intencionado. |
| **A correção da série (D2) não chega antes do apply**, e alguém implementa a tela contra a rota incompleta. | A dependência é issue que **bloqueia a #52**, e a tarefa 0.1 do `tasks.md` é conferi-la antes de qualquer código. O sintoma, se escapar, é silencioso — por isso a conferência é a primeira tarefa e não uma nota. |
| **A cobertura de dia da semana continua reconstruída no cliente** (`byWeekday` tem o mesmo defeito e a spec da A não o cobre). | A reconstrução passa a sair da **própria série**, não do regime: com a `dailySeries` completa, os dias da semana medidos são os que aparecem nela. Uma fonte só, a do servidor, uma pergunta depois. Registrado no `02` para a change que corrigir M10 avaliar se estende o `generate_series` a M6. |
| **A configuração de regime vale para todos os ambientes**, e num ambiente novo a tela afirmaria "medindo desde 22/09" sobre um banco que não mediu nada (modo de falha nomeado pela D8 da etapa 3, não resolvido lá). | Fora do alcance desta change — é checagem de boot em `apps/api`. O item já está no `02` com gatilho ("o primeiro ambiente novo a subir a rota, dev incluído"), e esta change **o cumpre**: a tela foi exercitada contra dev, que é exatamente o ambiente em que a configuração não corresponde. Registrar a observação no fechamento. |
| **As cinco lacunas declaradas ocupam espaço na tela** e podem ser lidas como defeito pelo operador. | O texto de cada uma diz o que falta e por quê, no idioma do quadro 6. A alternativa — sumir — é o que a convenção 9 e o próprio protótipo recusam. A conferência manual do dono julga se o peso ficou certo. |
| **A conferência manual não pega o que não foi produzido.** Um estado que o agente não capturar não é julgado. | A lista de seis itens da seção de verificação é fechada e vira tarefa própria no `tasks.md`, com o `Estados.dc.html` como checklist. |
| **Tela com quatro consultas SQL por card e uma única rota**: a resposta levou 2,24 s contra dev vazio. | Não é risco desta change — é o custo nomeado da D4 da etapa 3, com gatilho já escrito ("a primeira requisição de período que passar de 2 s contra o piloto"). **Esta medição o dispara**: 2,24 s contra um banco com 260 linhas. Registrar no fechamento como gatilho cumprido, com a ressalva de que dev inclui o custo de subida a frio. |
| **Treze componentes novos numa change só** podem virar arquivos pequenos demais, o erro que a convenção 18 nomeia. | Cada um corresponde a um bloco desenhado no protótipo; nenhum é extração especulativa (D14). A régua está declarada, e o fechamento mede. |

## Open Questions

Nenhuma incerteza de negócio em aberto. As decisões de produto que esta change
tomou — quais lacunas declarar, qual palavra usar onde o protótipo diverge da
medição, e o que fica de fora — estão em D8 a D12, cada uma com gatilho e
posição, e são reversíveis por decisão do dono sem retrabalho de estrutura.
