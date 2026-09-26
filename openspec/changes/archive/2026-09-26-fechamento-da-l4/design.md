## Context

A **#67** ficou aberta com o rótulo `aguardando gatilho` desde a etapa 4. O
gatilho era a #53 — *"decidir com a tela dos dois lados à vista"* —, a #53
mergeou e **está em produção**. Esta change é o resíduo dessa decisão.

**A decisão é (2):** as três colunas ausentes do ranking do sistema — **Tasks**,
**Tokens por task**, **Duração p95** — ficam **só na aba do agente**.

### O que foi lido e medido, e quando

Medido em **26/09/2026** sobre `3580599` (árvore limpa), com
`buteco-agents_postgres_1` (pgvector:pg18) e `buteco-agents_rabbitmq_1`
(rabbitmq:4.3-management) de pé e *healthy*, `waha` parado. O banco de dev tinha
**27 execuções de 5 agentes**, entre 22/09 e 24/09.

Lidos: os dois handlers de agregação (`GetSystemInsightsQueryHandler`,
`GetAgentInsightsQueryHandler`), o `AgentConsumptionCard` e o teste dele,
`AgentDetailPage`, `AgentInsightsTab`, `SystemInsightsPage`, a spec sincronizada
de `system-insights-ui`, o `design.md` da #53 e os dois comentários da #67.

**A conferência visual das duas telas não aconteceu, e isso vai declarado, não
omitido** (convenção 6). `apps/api` e `apps/frontend` estavam no ar em `5017` e
`5173`, mas o login pelo navegador não passou em quatro tentativas — os campos
preenchem e o submit não dispara, sem erro no console e sem requisição saindo.
**O que a decisão dependia foi medido direto no banco**, que para esta pergunta é
evidência mais forte que um screenshot: a pergunta era se três números fecham
entre si, e isso se responde com SQL, não com o olho. O que ficou sem olho humano
é o **layout**, e ele não estava em disputa.

### O estado da tela hoje, conferido no `HEAD`

```
Consumo por agente
Agente                          Tokens    Falhas
Atendente Ambiente Software      3,2 M   Nenhuma
Triagem                        559 mil         7   ← em vermelho
Especialista Técnico Ambiente        —         2
```

Três colunas, nome ligando a `/agents/{id}`, **nada no rodapé** — o quadro
tracejado saiu na décima rodada da #52.

## Goals / Non-Goals

**Goals:**

- Fechar a #67 pelo (2) **com o argumento registrado**, não com "redundante".
- Fazer a passagem que o argumento supõe **existir**: um clique até a aba.
- Tirar do repositório as três afirmações que a decisão tornou falsas — o
  comentário, o gatilho e **o requisito que contradiz o vizinho**.

**Non-Goals:**

- **Não acrescentar coluna nenhuma à rota do sistema** — é o caminho (1),
  recusado, e a D3 abaixo diz por quê.
- **Não corrigir o buraco da coluna Falhas** (achado 1 do `proposal.md`) nem **o
  período que não viaja** (achado 2). Issues próprias, convenção 1.
- **Não mudar o critério do destaque de falhas.** Só o **gatilho** dele muda. A
  D4 explica por que o critério fica como está mesmo sabendo-o errado.
- Não tocar `apps/api`, `apps/workers`, `apps/inbox` nem `nginx.conf`.
- Não mexer na aba do agente, que acabou de ir a produção.

## A árvore de pastas proposta

**Nenhum arquivo é criado.** Quatro são modificados — e **dois deles a projeção
não previu**, o que a D8 registra.

```
apps/frontend/src/features/insights/
├── components/
│   ├── AgentConsumptionCard.tsx       [M]  o `to` do Anchor; o comentário do
│   │                                       topo; o gatilho do destaque
│   ├── AgentConsumptionCard.test.tsx  [M]  o caso do link; o comentário do caso
│   │                                       do destaque; o cabeçalho de seção;
│   │                                       um guarda novo
│   └── DeclaredGap.tsx                [M]  só o comentário: a variante `block`
│                                           deixa de dizer que vale para a L4
└── types/
    └── systemInsights.ts              [M]  só o comentário de `AgentTokens`:
                                            "lacuna L4" vira ausência decidida

openspec/specs/system-insights-ui/spec.md   [M]  pelo delta desta change
02-HISTORICO_E_STATUS.md                    [M]  a decisão e o gatilho de reabertura
CHANGELOG.md                                [M]
```

**A conferência de escopo rodou na proposta, e é ela que produziu esta árvore.**
A lista fechada das referências vivas à L4/#67 — fora do `archive` e fora do
registro histórico — está no `tasks.md`, tarefa 1.1.

## Decisions

### D1 — A decisão é (2), e ela foi tomada por medição, não por preferência

**Decisão:** as três colunas não entram na rota do sistema. Três argumentos, em
ordem de peso, todos medidos.

**1. As seis colunas na mesma linha convidam a uma divisão que não fecha.**

| agente | Tasks | Tokens | `Tokens por task` da aba | `Tokens ÷ Tasks` da linha |
|---|---|---|---|---|
| Triagem | 15 | 594.681 | **59.468,1** | **39.645,4** |
| Gestor de Reservas | 3 | 5.550 | **2.775,0** | **1.850,0** |

Os denominadores são outros. A aba (M17) divide pelas tasks **com chamada de
provedor** — `provider_calls pc join task_executions e … group by e."TaskId"`,
que são 10 das 15 — e a linha dividiria por todas as execuções. **50% de
diferença sobre o mesmo agente, na mesma janela.** Numa tabela de seis colunas o
operador faz essa divisão, o número dele discorda da aba, e nada na tela explica
por quê. Nos cards separados da aba ninguém é convidado a dividir.

**2. Uma célula de `p95` não carrega o desconto dela.** Na aba, `Duração p95` vem
com a média e o `sampleCount`, porque execução com `SubmittedAt` nulo —
reentrega — sai do cálculo (M21). Numa célula de tabela o número vai sozinho,
com a exclusão invisível. É a família da convenção 13: o rótulo afirmaria mais
do que o número sabe.

**3. O caminho (1) não era "+3 agregações": mudava o que é uma linha.** As linhas
nascem de `tokens.byAgent` (M13), que é `provider_calls join task_executions` —
agente que executou e não chamou provedor **não tem linha**. Medido: **4 de 5
agentes têm linha, e as linhas cobrem 25 das 27 execuções**. Uma coluna `Tasks`
sobre essa população somaria **25 contra o KPI de 27** da própria página — o
mesmo defeito que o comentário do card já guarda para o total de tokens
(*"Sumir com a linha faria o total da tabela não fechar com o KPI, sem
sintoma"*), reintroduzido por outra porta.

**Alternativa considerada: (1), acrescentar as três.** Recusada pelos três
argumentos acima.

### D2 — O custo **não** foi a razão, e isso precisa estar escrito

**Decisão:** registrar a projeção que a D4 da change A nomeou sem fazer.

A rota do sistema já faz **~24 idas ao banco** — 20 `SqlQuery` diretas mais
quatro chamadas a `StatsAsync`. Três agregações a mais são **~+12%**. A D4 da
`rotas-de-agregacao-sistema` chamou isso de *"três agregações a mais numa rota
que já faz muitas"*, **sem projeção**, e a projeção é modesta.

**Por que isto é decisão e não curiosidade:** sem o número escrito, a #67 será
citada depois como *"foi recusada porque a rota já era pesada"*, e a próxima
pessoa que precisar de uma agregação por agente vai acreditar que o caminho está
fechado por custo. **Ele não está.** O (1) perdeu na aritmética das colunas, e a
conta de consultas não teve papel nenhum.

### D3 — Nem mesmo **só** a coluna `Tasks`, e este é o (1) na versão mais forte

**Decisão:** nenhuma das três entra, incluindo `Tasks` sozinha.

**Por que a pergunta aparece:** das três, só `Tasks` tem consequência **em outro
elemento da tela** — o destaque de falhas, que ranqueia por contagem absoluta
porque a taxa exigiria o denominador que ela seria. Uma agregação só, +4% na
rota, e o destaque passaria a ranquear por taxa. É o (1) na forma mais barata e
mais defensável, e **é ela que vai voltar** quando alguém reabrir o assunto —
por isso fica escrita aqui em vez de ser redescoberta.

**Por que ainda assim não:**

- `Tasks` é justamente a coluna do **argumento 3**: a população dela (todas as
  execuções) é maior que a população da tabela (execuções com chamada de
  provedor). Ela não pode entrar sem a tabela deixar de nascer de
  `tokens.byAgent` — e aí não é mais uma agregação, é a tabela inteira.
- E ela reintroduz o **argumento 1** de lado: `Tokens ÷ Tasks` passa a ser uma
  conta que a tela oferece em duas colunas vizinhas, e o resultado discorda da
  aba pelos mesmos 50%.

**A ordem importa:** primeiro a população, depois a coluna. Quem reabrir isto
começa pela mudança de população, não pela agregação.

### D4 — O critério do destaque **não** muda; só o gatilho dele

**Decisão:** o destaque continua marcando a **maior contagem absoluta**. Reescreve-se
o gatilho, no código e no caso de teste.

**A primeira medição desta D4 estava ERRADA, e a correção está aqui em vez de
apagada.** Ela afirmava *"Triagem leva o vermelho com 7 falhas (46,7%); o
Especialista, com 2 em 2 (100%), não leva"* — e a conferência do dono em 26/09
mostrou o contrário na tela: **Triagem com "Nenhuma", Especialista com 2 em
vermelho**.

**A causa: a medição ignorou o recorte de regime.** A consulta foi feita direto
em `task_executions` sem o `Later(from, executionRegime)` que a rota aplica. O
regime de execução começa em `2026-09-22T04:21:00Z` (`01:21-03:00` no
`appsettings.json`), e as sete falhas da Triagem são de `03:03`–`03:21Z` —
**anteriores ao regime**. A rota as exclui, e está certa.

**É a convenção 6 cobrando o preço de sempre:** o número saiu de uma consulta
que *quase* era a da rota, e o *quase* não aparece no resultado.

**O dado, agora medido sob o regime e conferido contra a resposta da rota:**

| agente | tasks | falhas | taxa | tem linha | destacado |
|---|---|---|---|---|---|
| Atendente Sênior | 2 | 2 | **100%** | **não** | — invisível |
| Especialista Técnico Ambiente | 2 | 2 | **100%** | sim | **sim** |
| Atendente Ambiente Software | 5 | 0 | 0% | sim | não |
| Triagem | 3 | 0 | 0% | sim | não |
| Gestor de Reservas | 2 | 0 | 0% | sim | não |

**O cenário do "pior que não leva o destaque" NÃO EXISTE no dado de hoje**, e
passa a ser **hipótese declarada**, nunca medição: os dois agentes com falha
estão ambos em 100%, e o que tem linha **é** o destacado. A hipótese continua
válida como raciocínio — 9 falhas em 1.000 marcadas contra 3 em 5 não marcadas —,
e é só isso que ela é.

**E a medição achou algo mais forte, que não é hipótese:** `Atendente Sênior` tem
**2 falhas em 2 tasks, 100%, e nenhuma linha na tabela** — ele não chamou provedor
e por isso não está em `tokens.byAgent`. **Não é "deixa de ser destacado": é
invisível.** Isso torna a **#84 observada, e não latente** (ver D8).

**Por que não corrigir o critério agora, ainda assim:** a taxa exige `Tasks` por agente, a
D3 acabou de recusá-la, e **um destaque por taxa com a taxa fora da tela é pior
do que o atual**. Hoje o vermelho é verificável pelo olho — é o maior número da
coluna. Ranqueando por taxa, o operador veria `7` em vermelho e `2` em branco
sem nenhum número na tela que justificasse a escolha. **Critério invisível é
pior que critério grosseiro**, e o card não tem coluna para tornar a taxa
visível pelo mesmo motivo que fecha a #67.

E o argumento **ganhou força** com o dado corrigido: hoje o destaque acerta, e a
única coisa que ele não mostra é o agente que **não tem linha** — que é a #84, não
o critério.

**O que muda:** o código e o caso dizem *"o destaque melhora quando a #67
fechar"*. Fechando pelo (2), isso vira referência apontando para issue fechada —
exatamente o que a convenção 13 chama de verdadeira-quando-escrita. O gatilho
novo é **o primeiro pedido do dono por taxa de falha no ranking**, e ele aponta
para a D3, que diz por onde começar.

**A asserção do caso permanece.** Conferido: `AgentConsumptionCard.test.tsx:186`
*afirma* o destaque, não só documenta — o comentário é que carrega a promessa. Só
o comentário muda.

### D5 — A spec se contradiz, e a decisão resolve pelo lado certo

**Decisão:** o requisito *"Consumo por agente…"* perde a exigência de declarar as
colunas sem fonte.

**A contradição, na spec sincronizada, hoje:**

| requisito | o que manda, para **coluna** |
|---|---|
| *"Consumo por agente cruza com o catálogo e declara o que não tem fonte"* | *"O sistema SHALL declarar, junto da tabela, quais colunas não têm fonte nesta rota"* — com cenário próprio |
| *"Métrica aprovada no protótipo e sem fonte não é inventada…"* | *"o sistema SHALL removê-las e SHALL NOT acrescentar elemento próprio para anunciá-las. A lacuna existe na **issue** que a registra, não na tela"* |

**Os dois não podem valer juntos.** A implementação segue o segundo desde a
décima rodada da #52, e o guarda negativo
(`queryByTestId('agentes-lacuna-colunas') → null`) afirma isso. **A spec ficou com
a versão pré-decisão** — mesma família do defeito que a #75 registra para o card
de Motivos, segunda ocorrência, e ninguém a tinha pegado.

**Por que a decisão (2) é o que resolve:** enquanto a #67 estava aberta, as três
colunas eram **lacuna** — algo que falta. Fechada pelo (2), elas passam a ser
**ausência decidida** — algo que se escolheu não ter ali, porque está em outro
lugar. Declarar na tela ausência escolhida é a convenção 13 ao contrário: o texto
pediria desculpa por uma decisão.

**A metade negativa do cenário fica.** *"Nenhuma delas aparece preenchida com
zero ou com valor de outro nível de agregação"* continua sendo o guarda que
impede o defeito, e não depende de haver texto nenhum. Sai só a metade que exige
o texto.

### D6 — A régua ganha a distinção entre **lacuna** e **ausência decidida**

**Decisão:** o requisito da régua passa a nomear os dois casos.

**Por quê:** a régua de hoje só sabe falar de *"métrica cuja fonte não existe na
rota"*, e manda a lacuna viver na issue. **Ela não tem o que dizer quando a issue
fecha sem código** — que é este caso, e será o de toda L que a decisão do dono
resolver por "fica em outra superfície". Sem a distinção, a próxima leitura da
régua conclui que a issue fechada deixou a tela em falta.

**A distinção, e ela é de fato, não de grau:** a **lacuna** é métrica sem fonte
em lugar nenhum; a **ausência decidida** é métrica **com** fonte, servida e
apresentada em outra superfície, deixada fora desta por decisão registrada. As
duas somem da tela do mesmo jeito. **O que muda é onde a explicação mora:** a
lacuna mora na issue aberta; a ausência decidida mora no `02` e na issue
**fechada**, com o gatilho de reabertura.

### D7 — O link leva à aba, e o período **não** vai junto

**Decisão:** `to={`/agents/${agentId}?tab=insights`}`. O período fica para a
issue.

**Por que o parâmetro:** `parseTab(null)` cai em `OVERVIEW_TAB` por contrato
declarado (*"ausência do parâmetro é a forma canônica da visão geral"*), então o
link de hoje custa um clique a mais. Uma linha resolve.

**Por que o período não vai junto, mesmo sendo o mesmo defeito de passagem:** as
duas telas guardam o período em `useState` semeado com
`DEFAULT_INSIGHTS_PERIOD`; levá-lo na URL significa **as duas** passarem a ler e
escrever o período em `searchParams` — a página do sistema também, que esta change
declarou como não tocada. É o item **(e)** do `02`, herdado da #52, e tem escopo
próprio.

**A consequência fica escrita, porque ela deixa este `(a)` pela metade:** quem
comparou em 90 dias e clicar no nome do agente vai para a aba **na janela
padrão**, sem nada na tela dizendo que a janela mudou. O clique fica sendo um; a
janela, não. **Isto é limitação conhecida desta change**, não achado da próxima.

### D8 — A conferência de escopo achou dois arquivos que a projeção não previu, e um defeito próprio

**Decisão:** os dois entram nesta change; o defeito que um deles esconde vira
issue.

**Como apareceu:** varredura por `#67` e `L4` em `*.ts`, `*.tsx`, `*.cs` e
`*.md`, excluindo `openspec/changes/archive/` (história, não se reescreve) e o
`02` (idem). **Sete sítios vivos em quatro arquivos**, contra os dois que a
árvore inicial previa.

**O que a projeção perdeu, e por quê:**

| arquivo | o que afirma | por que escapou |
|---|---|---|
| `types/systemInsights.ts:73` | *"é a **lacuna** L4 (#67)"* no doc de `AgentTokens` | a projeção olhou o componente, e a afirmação está no **tipo** |
| `components/DeclaredGap.tsx:46` | a variante `block` *"entra no RODAPÉ do card, como a D8 especifica para a L3 e a **L4**"* | a projeção olhou quem **mostra** a tabela, não quem **descreve** a lacuna |

**A causa é uma só, e vale como régua:** a projeção enumerou os arquivos que
**mudam de comportamento**, e esta change quase não tem comportamento — ela muda
**afirmações**. Numa change de registro, a unidade de escopo é a **afirmação**,
não o componente, e a varredura tem de ser por texto, não por dependência.

**E a segunda linha destapou um defeito que não é desta change.** A variante
`block` do `DeclaredGap` — a moldura tracejada de rodapé — **não tem nenhum
consumidor**: os dois sítios que usam o componente passam `variant="inline"`. Ela
ficou órfã quando a décima rodada da #52 removeu os três rodapés. **E ela é o
`variant` DEFAULT**, então a armadilha está armada: um `<DeclaredGap>` novo sem a
prop renderiza exatamente a moldura que o requisito *"Coluna sem fonte sai sem
deixar quadro no lugar"* proíbe.

**Fica como issue, não como trabalho daqui** (convenção 1): é resíduo da #52, não
da decisão da #67, e mexer no branch morto e no default é mudança de
comportamento num componente compartilhado, com o teste dele junto. **Nesta
change o `DeclaredGap` só perde a frase que diz que a variante vale para a L4** —
corrigir a frase e deixar o branch armado seria documentar uma variante que não
deveria existir, então a frase passa a apontar para a issue.

## Riscos / Trade-offs

- **[A decisão ser citada depois como "recusada por custo"]** → a D2 fixa o
  número (~24 consultas, +12%) e diz em letras que o custo não teve papel.
- **[O destaque continuar marcando o agente errado]** → assumido e medido na D4;
  o gatilho novo nomeia quem o reabre e a D3 diz por onde começar. **Não é dívida
  escondida: é escolha com o número ao lado.**
- **[O clique virar um e a janela não]** → declarado na D7 como limitação desta
  change, com issue própria. O risco real é alguém ler o `(a)` como resolvido e
  fechar o item (e).
- **[Mexer na spec de uma tela em produção]** → o delta **não muda comportamento
  nenhum** exceto o destino do link; ele alinha a spec ao que a tela já faz desde
  a #52. O guarda negativo que já existe é a prova de que a implementação não se
  move.
- **[A `?tab=insights` quebrar no refresh direto]** → não quebra: é parâmetro de
  consulta sobre rota que já existe, e o deploy da #53 já foi verificado em
  produção. Sem ação.

## Convenção 18 — vigésima segunda medição, projeção

**Unidade declarada antes**, e igual à da vigésima primeira: cenários de delta
separados em **novos**, **reescritos** e **copiados**; arquivos criados ×
modificados separados, contados da árvore acima; casos em novos e adaptados;
linhas em três níveis, com gerado em 0.

| dimensão | projetado |
|---|---|
| cenários de delta | **9** (1 novo, 2 reescritos, 6 copiados) |
| arquivos criados | **0** |
| arquivos modificados (produção + teste) | **2** (1 + 1) |
| casos de teste novos | **1** |
| casos adaptados | **2** |
| linhas de produção à mão | **~25** |
| linhas de teste à mão | **~12** |
| duplos | **0** |
| **linhas geradas** | **0** |

**A tabela acima fica como foi projetada, e a D8 já a contradiz — de propósito.**
A projeção diz **2** arquivos modificados; a conferência de escopo, rodada
**depois** dela e ainda na proposta, achou **4**. O número projetado **não é
corrigido**: corrigi-lo depois de aprender apagaria justamente a medição.
**Erro conhecido antes da primeira linha de código: −50% em arquivos
modificados**, e a D8 diz a causa — a projeção enumerou o que muda de
comportamento numa change que muda afirmações.

**Esta medição é de um regime que a série ainda não tem: change de registro, com
comportamento quase nulo.** Uma linha de produção muda de verdade (o `to`); todo
o resto são comentário e spec. **Duas consequências, e as duas são de método:**

1. **A proporção comentário:lógica desta change NÃO serve de âncora para
   nenhuma projeção futura.** Ela vai sair absurda — a maior parte do diff é
   comentário — e isso é propriedade do regime, não do estilo. A régua da
   vigésima primeira (*"ancorar em arquivo do mesmo tipo E da mesma
   linguagem"*) ganha uma terceira condição: **e do mesmo regime**. A D8 é a
   primeira evidência disso na série, e ela apareceu na dimensão de **arquivos**,
   antes de qualquer linha ser escrita.
2. **A régua da vigésima primeira vale aqui:** *"medir no primeiro verde é medir
   antes do julgamento"*. Se houver conferência do dono, a medição de fechamento
   é **depois** dela. Numa change deste tamanho a conferência muda pouco, mas o
   método não muda com o tamanho.

**Baselines não herdadas** (convenção 19). Remedir `apps/frontend`, declarando o
`podman ps`. As três suítes de backend **não rodam** — nenhuma linha delas no
diff.

## Migration Plan

Não há migração. Nenhum contrato muda, nenhum dado muda, nenhuma rota muda. O
rollback é reverter o commit.

## Open Questions

Apenas incertezas reais de produto, que a conferência do dono resolve
(convenção 14). Nenhuma bloqueia a implementação.

1. **O gatilho de reabertura da #67 é o certo?** Padrão aplicado: *"o primeiro
   pedido do dono pelas colunas no ranking"*. A alternativa seria um gatilho
   medido — *"quando a tabela passar de N agentes"* —, que a série não tem base
   para fixar: são 5 agentes no dev e o piloto não foi medido.
2. **O destaque de falhas deve mesmo ficar como está?** Padrão aplicado: fica
   (D4). É a que mais provavelmente muda na conferência, porque o dono pediu o
   vermelho na #52 e pode preferir **nenhum** destaque a um destaque que ele
   agora sabe que erra. **Se ele preferir tirar, é mudança de comportamento e sai
   desta change.**
