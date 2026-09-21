## Context

Posição **4** da fila, e **a última change antes da limpeza do banco e do
deploy**. Consequência prática: se a verificação achar algo que precise entrar
antes de subir, é achado a **sequenciar com posição** — não motivo para alargar
esta change. Nada do que a verificação achou tem essa natureza; os dois itens
achados estão registrados como abertos.

### O estado de `HEAD`, lido no código

`features/agents/components/AgentDelegationsTab.tsx:68-74`:

```ts
onError: () => {
  notifications.show({
    color: 'red',
    title: 'Erro ao atualizar delegações',
    message: 'Não foi possível atualizar as delegações do agente. Tente novamente.',
  });
},
```

O `onError` **não recebe parâmetro**. Não é que a tela escolha ignorar o erro:
ela não tem acesso a ele. Toda informação que a API manda morre nessa
assinatura.

### O que a verificação fechou, e o que cada parte decidiu

**V1 — itens de fila posicionados nesta change.** Um: o **terceiro comparador de
ordem** (`features/agents/utils/knowledgeBaseRows.ts:26-27`), com *"gatilho
imediato e sem nada que o puxe"*, declarado como change de `apps/frontend`, e na
**mesma feature** desta aba.

**Não é puxado, e o motivo é um achado — a premissa do item não se sustenta lida
contra a árvore.** O item diz que, depois de `ordenacao-desempate-listas-vinculo`,
a API ter *"uma ordem só"* **torna a reordenação no cliente desnecessária**. O
comentário do próprio arquivo (`:21-24`) registra outra razão:

> *"reproduzir a ordem do servidor é o que faz a lista NÃO se remontar quando o
> PUT volta. Uma base recém-escolhida ocupa, no rascunho, a mesma posição que vai
> ocupar depois de gravada (design.md, D4)."*

O sort serve o **rascunho**. Uma base marcada agora não existe no servidor, então
ordem de API nenhuma a ordena. Remover o sort mudaria o comportamento do rascunho
— não é limpeza, é reabrir a D4 de outra change com um enunciado que omite a razão
dela. É a forma da convenção 6 que o `02` nomeia como a mais perigosa: item aberto
que descreve código, lido depois por quem não tem o contexto de quem o escreveu.
**A correção do item entra no `02`; o trabalho não entra aqui.**

**V2 — o idioma de `ValidationProblem` no painel.** Duas respostas, e as duas
decidem desenho:

- **Há oito cópias de `fieldErrorsFrom`** — `agents` (2), `knowledge-bases` (2),
  `mcp-servers` (2), `channels` (2) —, **6 byte-a-byte idênticas** e as 2 de
  `channels` divergindo por razão de domínio (separam `credential` do resto). O
  gatilho da convenção 2 está cumprido oito vezes, e ainda assim **não se extrai
  aqui**: (a) cada feature tem seu **próprio** `ApiError` (6 classes
  verificadas), por convenção 7, então um helper comum não pode usar
  `instanceof` sem base compartilhada ou tipagem estrutural — decisão de
  arquitetura, não refatoração; e (b) **esta change não consome
  `fieldErrorsFrom`**: ele mapeia erros para **campos de formulário**, e esta aba
  não tem campo nenhum.
- **O idioma que esta change reusa é o de `AgentToolsTab`** — aba **irmã**, mesma
  feature, e a aba em que esta foi explicitamente modelada (Decision 8 de
  `frontend-agente-detalhe-abas`). Forma, lida no arquivo:

  ```ts
  onError: (error) => {
    if (error instanceof ApiError && error.status === 502) {
      setSubmitError({ title: …, detail: … });
      return;
    }
    notifications.show({ color: 'red', … });
  }
  ```

  Ramo específico → estado persistente → `return` cedo → genérico para o resto,
  renderizado como `<Alert color="red" title={…} data-testid=…>`. Não há nada a
  inventar.

**A aba não tem estado de erro por campo, e isso decide onde a mensagem
aparece.** Não há `useForm`, nenhum `error` em input, nenhum `Alert`. A seleção é
uma lista de `Checkbox`, e **`targetAgentIds` nomeia o conjunto, não um widget**.
Pendurar o caminho do ciclo num checkbox afirmaria qual aresta é o problema — o
que o sistema não sabe.

**V3 — quais 400 essa rota produz.** Lido em
`apps/api/src/Buteco.Api/AgentDelegations/Endpoints/AgentDelegationEndpoints.cs`:
os **quatro** saem como `ValidationProblem` sob a **mesma chave**:

| caso | linha | mensagem |
|---|---|---|
| conjunto nulo | `:26-29` | *"O conjunto de agentes-alvo vinculados é obrigatório…"* |
| auto-delegação | `:42-46` | *"Um agente não pode delegar para si mesmo."* |
| **ciclo** | `:55-58` | *"Esta delegação fecha um ciclo entre agentes: {caminho}. …"* |
| ids inexistentes | `:64-67` | *"Os seguintes ids não correspondem a nenhum agente cadastrado: …"* |

Mais `404` para agente inexistente (`:37`). **E não há `MapDelete` para agente
em `apps/api`** — verificado —, então o `404` é praticamente inalcançável a partir
de uma aba renderizada de um agente já carregado. Ele fica no ramo genérico, e
isso é decisão escrita, não omissão.

**V4 — o texto, conferido na resposta real.** Não inferido do handler: o teste de
integração `ReplaceAgentDelegations_CycleRejection_NamesThePath` lê o **JSON
bruto** da resposta HTTP real (`ReadFromJsonAsync<JsonElement>` →
`GetProperty("errors").GetProperty("targetAgentIds")`) contra API e Postgres
reais — a forma forte da convenção 11 — e foi **rodado verde nesta sessão** (1/1,
1 s). O caminho é montado com `string.Join(" → ", …)` sobre **nomes de agente**.

**Duas consequências de apresentação, das quais uma é sutil:** nome de agente é
texto livre e pode ser longo, então o aviso tem de **quebrar linha**; e um nome
contendo `→` tornaria o caminho ambíguo **se alguém tentasse parseá-lo**. A saída
é não parsear — a tela exibe a string como veio, e isso é requisito, não escolha
de implementação.

### Restrições

- Só `apps/frontend`. Nada em `libs/` — **não há nada a justificar**, porque nada
  é colocado lá.
- Sem dependência nova. `Alert` e `notifications` já são usados nesta feature;
  não há versão de biblioteca a fixar.

## Goals / Non-Goals

**Goals:**

- A tela deixa de mandar repetir uma operação que nunca vai funcionar.
- O operador recebe o caminho do ciclo, que a API já manda e a tela descartava.
- O genérico continua valendo onde está certo — rede e 5xx.

**Non-Goals:**

- `apps/api`, `apps/workers`, `apps/inbox`, `libs/`.
- Redesenhar a aba de delegações.
- Instâncias, compose, documentação de deploy.
- Linha de métricas e protótipos de Insights.
- **Detecção de ciclo no cliente.** A tela não conhece o grafo de delegações de
  todos os agentes, e duplicar a regra do servidor criaria segunda fonte de
  verdade que diverge na primeira mudança de regra.
- **Extrair `fieldErrorsFrom`** — ver D5.
- Tocar o terceiro comparador de ordem — ver V1.

## Árvore de pastas proposta

Nenhuma pasta nova, nenhum arquivo criado.

```
apps/frontend/src/features/agents/
├── components/
│   ├── AgentDelegationsTab.tsx        (M)
│   ├── AgentDelegationsTab.test.tsx   (M)
│   ├── AgentToolsTab.tsx              (…)  idioma reusado daqui
│   └── …                              (…)
├── api/agentsApi.ts                   (…)  ApiError e ValidationProblemDetails
└── utils/knowledgeBaseRows.ts         (…)  NÃO tocado — ver V1
```

## Decisions

### D1 — O ramo é pela chave `targetAgentIds`, nunca pelo texto da mensagem

**Decidido:** o ramo específico dispara quando a resposta é `400` **e** há
mensagem sob `problem.errors.targetAgentIds`. O conteúdo da mensagem não é lido,
comparado nem parseado.

**Alternativa recusada — um ramo para ciclo, casando o texto** (*"fecha um
ciclo"*). Recusada por três motivos, nesta ordem:

1. **Casar texto é a armadilha que esta linha de trabalho já pagou duas vezes.**
   A change anterior registrou que quem restaurasse a sonda `Probe_CycleAB_A`
   como estava veria o guarda reprovar **por texto** porque a mensagem tinha
   mudado. Um ramo de produção que case texto tem o mesmo defeito, com
   consequência pior: ele **silenciosamente** para de funcionar quando alguém
   melhorar a redação da API, e a tela volta ao genérico sem nenhum sinal.
2. **Deixaria os outros três 400 no genérico**, e a pergunta de V3 era
   exatamente essa. Os quatro são recusa permanente com texto pronto; tratar um
   e não os outros seria decisão por acidente.
3. **Um ramo cobre quatro casos e o seguinte de graça.** O quinto motivo de 400
   que a rota ganhar entra sem tocar o frontend — que é a relação certa entre
   quem define a regra e quem a exibe.

**O preço, declarado:** se a API algum dia responder 400 sob `targetAgentIds`
para algo **transitório**, a tela chamaria de permanente. Não é hipótese
plausível — `ValidationProblem` é, por definição, recusa de entrada —, mas o
ramo está ancorado nessa premissa e é justo escrevê-la.

### D2 — A mensagem vai num `Alert` persistente, não na notificação

**Decidido:** estado local `refusal`, renderizado como `<Alert color="red">`,
acima da lista.

**O número que decide, medido no pacote instalado e não suposto:**
`@mantine/notifications` 9.4.2, `esm/Notifications.mjs:14-16` —
`defaultProps = { position: "bottom-right", autoClose: 4e3, … }`. `main.tsx:21`
monta `<Notifications />` **sem sobrescrever**, então toda notificação deste
painel se fecha em **4 segundos**.

O caminho do ciclo é a informação que o operador precisa **ler para agir**. Um
canal que a mostra e a tira em 4 s é pior que não a mostrar, porque cria a
impressão de que a informação foi dada.

**Alternativa recusada — notificação com `autoClose: false`.** Seria menos
código. Recusada porque mistura dois regimes no mesmo canal: o painel inteiro
trata notificação como efêmera, e uma que não fecha vira a única que exige gesto
do operador, num canto da tela que não é onde ele está olhando. E o idioma da
aba irmã já resolve isso com `Alert`, no fluxo da página.

**Alternativa recusada — erro sob o campo.** Não há campo. `targetAgentIds`
nomeia o conjunto; a seleção é uma lista de checkboxes. Escolher um checkbox para
pendurar a mensagem afirmaria qual aresta é o problema (convenção 13).

### D3 — O aviso é limpo no sucesso e no descarte

**Decidido:** `setRefusal(undefined)` antes de cada submit, no `onSuccess` e no
descarte.

É a mesma forma da convenção 13 que a 5a-4 achou e que **nenhuma revisão de tela
pega**: uma afirmação verdadeira quando foi escrita e que outra ação torna falsa.
Um aviso de recusa que sobrevive a um salvamento bem-sucedido afirma uma recusa
que já não existe. Há cenário e guarda para os dois caminhos.

### D4 — A tela não interpreta o caminho do ciclo

**Decidido:** a mensagem é exibida como string, inteira, sem extração de partes.

Nome de agente é texto livre. O caminho é `string.Join(" → ", nomes)`, então um
agente chamado `Vendas → Suporte` produz um caminho que **nenhum parser
distingue** do separador real. Mais forte que o argumento de robustez: parsear
seria reimplementar, no cliente, conhecimento sobre uma regra do servidor — o
mesmo motivo que recusa a detecção de ciclo no cliente, aplicado à
apresentação.

**Consequência de apresentação:** o `Alert` precisa quebrar linha para nome
longo. Não é decoração — é o que impede o caminho de ser cortado justamente na
parte que o operador precisa.

### D5 — `fieldErrorsFrom` NÃO é extraído, e o gatilho fica registrado

**Decidido:** as oito cópias ficam. O item aberto entra no `02` com gatilho e
posição.

Contado antes de decidir, como a convenção 2 exige — e o gatilho dela está
cumprido **oito vezes**, o que normalmente mandaria extrair. Duas razões
independentes sustentam a recusa:

- **Convenção 7 está no caminho, e não é obstáculo acidental.** Cada feature tem
  seu próprio `ApiError` (6 classes: `auth`, `agents`, `knowledge-bases`,
  `sessions`, `mcp-servers`, `channels`). Um helper comum não pode usar
  `instanceof ApiError` — precisaria de base compartilhada (que a convenção 7
  recusa) ou de tipagem estrutural sobre `status`/`problem`. É **decisão de
  arquitetura**, não limpeza, e não pertence a uma change cujo entregável é uma
  mensagem de erro.
- **Esta change não o consome.** `fieldErrorsFrom` produz
  `Record<campo, mensagem>` para alimentar `error=` de inputs. Esta aba não tem
  input. Extrair aqui seria extrair para um consumidor que não existe.

**Gatilho:** a primeira feature que precise de `fieldErrorsFrom` **e** não tenha
`ApiError` próprio, ou a primeira mudança na forma do `ValidationProblem` que
obrigue a editar as oito. **Posição:** change própria, que decide primeiro a
questão de convenção 7 (base comum contra tipagem estrutural) — porque é ela que
governa a extração, não a contagem de cópias.

### D6 — Dois requisitos de spec mudam, e um deles nem é sobre esta mudança

**Decidido:** delta com **dois** `MODIFIED`.

- *"Erro de submit exibido via notificação genérica"* — **invertido** para o caso
  de validação. O genérico não era lacuna, era requisito, com cenário verde
  correspondente. Mesma forma de `delegacao-ciclo-no-cadastro`, e vale repetir
  por quê: corrigir uma tela contra um requisito que manda o contrário exige
  mexer no requisito, senão a spec e o código divergem com a spec parecendo
  certa.
- *"Nenhuma detecção de ciclo de delegação na interface"* — **esclarecido**. O
  texto dizia *"sem detectar, **avisar** ou bloquear esse caso na interface"*, e
  depois desta change a interface **avisa** — depois da recusa do servidor. Sem o
  esclarecimento o requisito passa a ser falso **sem ninguém o tocar**, que é a
  forma da convenção 13 que nenhuma revisão de tela pega. A proibição que
  continua valendo é a de detecção **antes do submit**, e ela ganhou a razão
  (a tela não conhece o grafo completo) junto.

**E uma correção de fato aproveitada nesse requisito, declarada em vez de
silenciosa:** o texto dizia *"no controle de seleção múltipla"*, e esse controle
não existe mais — `frontend-agente-detalhe-abas` o trocou por lista de checkboxes
(registrado no comentário de `AgentDelegationsTab.tsx:25-29`). Como o bloco do
requisito é reescrito de todo jeito, a frase foi corrigida para *"na lista de
agentes-alvo"*. **Outras frases defasadas da mesma spec, em requisitos que esta
change não reescreve, ficam como estão** — corrigi-las é varredura própria, e
vira item aberto.

## Projeção (convenção 18) — fechada AQUI, antes de escrever código

**Nona medição.** A oitava confirmou duas dimensões e nomeou uma régua nova;
esta aplica as três.

### Contagem de componentes

| | criados | modificados |
|---|---|---|
| produção | **0** | **1** (`AgentDelegationsTab.tsx`) |
| teste | **0** | **1** (`AgentDelegationsTab.test.tsx`) |

**Unidades públicas novas: ZERO.** Nenhum export, nenhum arquivo, nenhum tipo.
O ramo é local ao `onError` e o estado é um `useState` local, exatamente como na
aba irmã. Isso é dito porque a dimensão que a oitava confirmou foi a contagem de
unidades públicas, e **zero é uma projeção, não ausência de projeção**.

**Blast radius, pela ferramenta certa.** A régua da oitava é que `grep` subestima
mudança de assinatura e só a compilação enumera; o equivalente aqui é o
**type-check** (`tsc`). Mas **esta change não muda assinatura nenhuma** — não há
prop nova, não há tipo compartilhado alterado, `AgentDelegationsTabProps` fica
igual. Então a régua não se aplica, e **dizer isso é o ponto**: a regra candidata
da oitava (*custo de mudança de assinatura = sítios × linhas de parâmetro*) **não
é testável nesta change**, e herdá-la como se fosse produziria projeção inflada.
O `tsc` roda no fechamento como conferência, não como enumerador.

### Linhas

| | projetado |
|---|---|
| produção, lógica | **~20** |
| produção, comentário | **~85** |
| produção, total | **~105** |
| teste, acrescentadas | **~150** |
| teste : produção | **~1,4 : 1** |
| comentário : lógica | **~4,2 : 1** |
| testes novos | **6** (921 → **927**) |

**A razão comentário:lógica é alta de propósito, e a âncora é medida.** O
entregável desta change **é a recusa** — a distinção entre permanente e
transitório, com a razão de cada ramo. O precedente mais próximo é a **5a-4**,
outra change de `apps/frontend` cujo entregável era uma recusa: ela entregou
**+80 de comentário contra +13 de código** em produção, com custo unitário de
**~26 linhas por recusa de três razões com arquivo e linha**. Três registros de
mecanismo aqui — o ramo pela chave e não pelo texto (D1), os 4 s medidos do
`autoClose` (D2), e o não-parse do caminho (D4) — a ~28 dão ~85. **Se a mistura
sair perto de 1,4:1, a projeção errou a natureza da change, não o volume.**

**Os 6 testes saíram dos cenários do delta**, que é a régua que acertou duas
vezes seguidas (12 contra 12 na oitava, depois de 6 contra 8 na 5a-4 por contar
afirmações em vez de estados). São 7 cenários no requisito modificado, e o
primeiro (*"Falha no submit não quebra a página"*) **já tem teste verde** — não
conta como novo.

**Direções de erro nomeadas, sabendo que nomear não carrega a projeção:**

- **para cima:** os 6 testes podem sair mais baratos que ~25 linhas cada, porque
  o arranjo (`mockRejectedValue` com um `ApiError`) é o mesmo nos quatro de
  recusa e sai para um helper local;
- **para baixo:** limpar o aviso em três pontos (submit, sucesso, descarte) pode
  revelar um quarto ponto — a troca de aba, se ela desmontar o componente — e aí
  vira decisão, não linha.

**Regime da medição do fechamento (convenção 22):** `apps/frontend` não usa
Testcontainers, então a contenção que desqualificou duas rodadas na change
anterior não se aplica; mesmo assim o número vai com `uptime` na largada. A
baseline usada é **921/921 em 84 arquivos, 73,3 s, load 2,78**, medida **nesta
sessão** sobre a árvore limpa — não herdada do fechamento anterior.

## Risks / Trade-offs

- **[O guarda reprova por texto em vez de por propriedade]** → o par de guardas
  afirma **o que aparece** (a mensagem da API) e **o que não aparece** (o texto
  genérico de tentar de novo). A segunda é a que prende o defeito: sem ela,
  acrescentar a mensagem nova **sem remover a velha** passaria verde.
- **[O guarda de falha transitória passa nos dois lados]** → declarado. Ele não
  prova nada sobre o defeito; é **regressão contra a correção errada** — alguém
  trocar o genérico em vez de acrescentar um ramo. Vai escrito ao lado dele,
  como a convenção 15 exige dos guardas que passam em `HEAD`.
- **[Mudança visual não é verificada por jsdom]** → convenção 14. A suíte afirma
  contrato (o texto aparece, o outro não); **cor, contraste e quebra de linha do
  `Alert` com nome longo só olho humano vê**, e há conferência manual como tarefa
  própria, nos dois esquemas de cor.
- **[O `Alert` corta o caminho do ciclo com nome longo]** → é justamente o que a
  conferência manual procura, com um caso deliberado de nome longo.
- **[Um 400 transitório futuro seria chamado de permanente]** → premissa de D1,
  escrita. `ValidationProblem` é recusa de entrada por definição; se isso mudar,
  D1 reabre.
- **[A tela passa a afirmar uma recusa que já não vale]** → D3, com cenário e
  guarda para sucesso e para descarte.
- **[O requisito de "nenhuma detecção na interface" fica falso em silêncio]** →
  é a razão do segundo `MODIFIED` (D6), e não haveria como pegá-lo por teste: a
  tela não muda naquele aspecto.

## Migration Plan

Sem migration, sem passo de deploy, sem mudança de contrato. Rollback é reverter
o commit.

**Esta change entra antes da limpeza do banco e do deploy**, e é por isso que ela
está na fila aqui: o `400` por ciclo é recusa permanente, e subir a detecção sem
a mensagem entrega ao operador uma recusa que ele não consegue interpretar.

## Open Questions

- **Trocar de aba deve preservar o aviso de recusa?** Se a troca desmontar o
  componente, o aviso morre com ele — comportamento aceitável, e é o que o
  desenho atual produz. É pergunta de produto, não técnica, e só vale decidir se
  a conferência manual mostrar que incomoda; até lá, registrar é mais honesto que
  escolher.
