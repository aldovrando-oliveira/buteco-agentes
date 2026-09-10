## Context

Etapa 5b da linha de bases de conhecimento, em `apps/frontend` apenas: a aba de
vínculo entre agente e bases, no detalhe do agente. É a última etapa da linha
que não espera a chave de provedor de embedding (`0b`).

**Estado de partida, conferido no código e não assumido:**

| Dependência | Onde está | Situação |
|---|---|---|
| `AgentResponse.knowledgeBases` | `ListAgentsQueryHandler.cs`, `AgentKnowledgeBaseLookup` | populado desde a etapa 3, ordenado por nome **com** `ThenBy(Id)` |
| `Agent.knowledgeBases` no tipo do frontend | `features/agents/types/agent.ts` | existe, **obrigatório**, desde a 5a-1 (D20) |
| `PUT /agents/{id}/knowledge-bases` | `AgentKnowledgeBindingEndpoints.cs:21` | existe; corpo `{ knowledgeBaseIds }`; **substituição do conjunto inteiro** |
| `GET /knowledge-bases` | `KnowledgeBaseEndpoints.cs:21` | existe; devolve inativas também; ordenado por `CreatedAt` |
| `useKnowledgeBasesQuery({ enabled })` | `features/knowledge-bases/api/useKnowledgeBases.ts` | já aceita `enabled` — escrito na 5a-1 **prevendo esta etapa** |
| `agentsConsultingBase` | `features/knowledge-bases/utils/agentUsage.ts` | existe, criado na 5a-1 |
| Busca sem acento | `utils/searchText.ts` (`matchesSearch`) | existe desde `frontend-listas-busca-e-colunas` |
| `SectionedCard` (faixa com `title` + `action`) | `components/data/SectionedCard.tsx` | existe, com slot à direita da faixa |
| Rascunho + guarda | `hooks/useUnsavedChangesGuard.ts` | existe; **dois** consumidores, `AgentToolsTab` e `AgentDelegationsTab` |

**Nada aqui precisa de backend novo.** Não existe `PUT` nem `DELETE` por base:
`AgentKnowledgeBindingEndpoints` registra uma única rota, `MapPut`, e ela
substitui o conjunto. O handoff de design supõe o contrário — ver a conferência
abaixo.

## Protótipo: qual revisão, e o que foi efetivamente percorrido

Anexado em `design/`, a partir da cópia da change 5a-1. Conferido contra o
projeto de design ao propor: os três arquivos têm exatamente o tamanho que o
projeto lista hoje (152095 / 69150 / 17656 bytes), e `support.js` foi comparado
byte a byte (`cmp`) com o baixado agora. Ou seja, **nenhum dos três mudou desde
a 5a-1** — `CONHECIMENTO.md` continua na revisão 2.

| Arquivo | sha256 (prefixo) |
|---|---|
| `Buteco Agentes.dc.html` | `da05f6db700c…` |
| `support.js` | `8fe7df74405f…` |
| `CONHECIMENTO.md` | `ca1abe2e8b19…` — **revisão 2, de 09/09/2026** |

`AJUSTES_VISUAIS.md` e `PROMPT_CLAUDE_CODE.md` **não foram reanexados**, pelo
motivo já registrado em D15 da 5a-1: autorizam ação contra decisão fechada (o
primeiro trata o protótipo como fonte da identidade visual, falso desde
`frontend-tema-identidade-visual`; o segundo pede multipart, recusado na etapa
1).

**O protótipo foi aberto e percorrido de verdade**, não lido só pela
especificação: `Buteco Agentes.dc.html` carregado em Chrome headless dirigido
por CDP, com clique real nos elementos e captura de tela em cada estado, nos
dois esquemas de cor. Percurso, e o que cada tela mostrou:

1. **`Agentes → Atendimento Financeiro → aba Conhecimento`** — a aba é a
   **terceira**, entre Ferramentas e Delegações, com contador `5`. Resumo
   acima do card, card `BASES VINCULADAS` com `9 bases no catálogo` à direita
   da faixa, cinco linhas com nome-link, descrição, contagem de documentos e
   botão `Desvincular`.
2. **Estado vazio (`Suporte Técnico`)** — bloco tracejado *dentro* do card,
   com título, a frase do system prompt e o botão. O resumo acima do card
   muda para "Nenhuma base vinculada — este agente não consulta conhecimento".
3. **Modal de vincular** — busca, nove linhas do catálogo, botão alternando
   `Vincular`/`Vinculada`, rodapé com `Criar nova base` e `Concluir`. Vincular
   **não fecha** o modal.
4. **Aviso de base inativa** — **não aparece em `Atendimento Financeiro`**,
   que o roteiro pedia: nenhuma das cinco bases dele está inativa. A única
   base inativa da semente (`Rotinas Internas`) está vinculada a **`Cobrança
   Ativa`**, e é lá que a faixa âmbar "Base inativa: o agente não a consulta
   enquanto ela estiver desativada." aparece. Percorrido lá, nos dois temas.
5. **Feedback durante a requisição** — clicando `Desvincular`, a linha mostra
   spinner + "salvando…" ao lado do nome por ~560 ms, e **os botões das outras
   linhas continuam habilitados**. Esse detalhe, que a especificação escrita
   não menciona, é o que torna a divergência de contrato consequente (D3).

Duas coisas que **só o percurso mostrou**, e nenhuma delas está na prosa do
handoff:

- **A busca do modal não normaliza acento.** Digitar `cardapio` devolve
  "Nenhuma base corresponde à busca."; `Cardápio` acha. O painel faz o
  contrário desde `frontend-listas-busca-e-colunas`. → D6.
- **O modal não marca base inativa.** `Rotinas Internas` aparece na lista de
  seleção sem nenhum sinal de que está desativada; o aviso só nasce depois,
  na linha da lista de vinculadas. O operador vincula às cegas. → D5.

A **nota de projeto recolhida no rodapé do card** ("Por que só as vinculadas, e
numa aba própria") foi aberta e lida: é justificativa de decisão para o time
discutir. O próprio handoff diz que não vai para o codebase. Não implementar.

## Conferência protótipo × código (convenção 17), antes das decisões

Cada divergência com a decisão e o número da convenção que a sustenta.

| # | O protótipo/handoff pede | O código diz | Decisão |
|---|---|---|---|
| 1 | Uma requisição por vincular/desvincular (`PUT`/`DELETE` por base), feedback por linha, sem rascunho (regra 4) | Uma rota só, `PUT /agents/{id}/knowledge-bases`, conjunto inteiro | **Rascunho + barra + guarda**, como as duas abas irmãs → D3 (conv. 13, 17) |
| 2 | `"{n} documentos · {k} indexados"` por linha, na aba e no modal | `KnowledgeBaseResponse` tem seis campos, nenhum de contagem; documento vive em rota por base | **Não exibir** → D5 (conv. 13) |
| 3 | Aviso "nenhum documento indexado nesta base" | mesma causa do #2 | **Não exibir** → D5 (conv. 13) |
| 4 | Busca do modal por `indexOf` cru | `utils/searchText.ts` normaliza acento em todas as listas do painel | **Usar `matchesSearch`** → D6 (conv. 2: reusar o que já existe) |
| 5 | Modal sem marca de base inativa | `KnowledgeBase.isActive` está no catálogo que a tela já busca | **Marcar** → D5 (o dado existe; omiti-lo é que seria escolha) |
| 6 | Descrição com fallback "Sem descrição — o modelo não sabe quando consultar." | `Description` é obrigatória e não vazia na criação **e** na edição (`ValidateShape`), e o campo do response não é anulável | **Sem fallback** — estado inalcançável → D11 (mesma D7 da 5a-1) |
| 7 | Modal informa "O vínculo é salvo na hora." | falso sob D3 | **Trocar a copy** → D12 (conv. 13) |
| 8 | Nota de projeto no rodapé do card | — | **Não implementar** (o próprio handoff assim manda) → D9 |

**Conferência tela a tela do que sobra**, campo por campo: nome ✓
(`KnowledgeBase.name`), descrição ✓ (`KnowledgeBase.description`), estado ✓
(`KnowledgeBase.isActive`), total do catálogo ✓ (tamanho da resposta que a tela
já tem em mãos), contador da aba ✓ (`agent.knowledgeBases.length`). Toda ação
tem rota: vincular/desvincular → `PUT /agents/{id}/knowledge-bases`; abrir base
→ `/knowledge-bases/:id` (rota registrada em `app/routes.tsx:63`); criar base →
`/knowledge-bases/new` (`:62`). Nenhum papel visual novo: faixa de card, rótulo
de seção, badge e faixa âmbar de aviso já são declarados por esquema no tema
(convenção 16), e a única cor nova seria a da faixa de aviso, que reusa o
mesmo tom das faixas de aviso existentes.

## Goals / Non-Goals

**Goals:**

- Aba `Conhecimento` no detalhe do agente, endereçável, com contador.
- Ler e editar o conjunto de bases vinculadas de um agente, contra
  `PUT /agents/{id}/knowledge-bases`, sem inventar semântica que o contrato não
  tem.
- Estado vazio que explique a consequência de não haver vínculo, e aviso de
  base inativa vinculada.
- Consistência de idioma com `AgentToolsTab` e `AgentDelegationsTab`, as duas
  outras abas de vínculo da mesma página.

**Non-Goals:**

- Contagem de documentos, "n indexados", estado de indexação (etapa 2 cria o
  dado; mesma decisão D1 da 5a-1).
- Criar, editar ou excluir documento (etapa 5a-2) e diagnóstico do índice (5c).
- Coluna `Consultada por` no catálogo de bases — usa a mesma derivação já
  pronta, mas não foi pedida.
- Qualquer rota, campo ou validação nova em `apps/api`.
- Corrigir a ordenação sem desempate dos quatro sites de `apps/api` (D10).

## Decisions

### D1 — Quarta aba, terceira posição, `?tab=conhecimento`

Entre Ferramentas e Delegações, como o protótipo mostra ao vivo (não só como a
prosa diz). Segue o desenho de `frontend-agente-detalhe-abas` sem exceção:
`parseTab` ganha o valor novo, valor desconhecido continua caindo na visão
geral sem reescrever o endereço, `keepMounted={false}` continua garantindo que
só a aba ativa existe no DOM — que é o que sustenta "no máximo uma guarda de
navegação armada por vez".

*Alternativa descartada:* seção no fim da Visão geral (revisão 1 do handoff).
O próprio handoff a abandonou, e o motivo se confirma no código: a coluna
direita da Visão geral tem quatro cards e o card de instruções ocupa
`calc(100vh - 360px)`.

### D2 — A linha vinculada é o **cruzamento** de `agent.knowledgeBases` com o catálogo

`AgentResponse.knowledgeBases` é `KnowledgeBaseSummaryResponse`, que é
`id + name` e nada mais — e isso é decisão registrada no próprio `record`:
*"a tela de vínculo tira o estado do catálogo (`GET /knowledge-bases`, que ela
já busca para montar a lista de seleção), não do vínculo"*. Ou seja: o backend
foi desenhado esperando exatamente esta tela buscar as duas coisas.

Então: os **ids** vêm do agente, a **descrição** e o **estado** vêm do
catálogo. Uma requisição a mais, não N.

Base vinculada ausente do catálogo é estado inalcançável — `KnowledgeBase` não
tem rota de exclusão (`KnowledgeBaseEndpoints` registra o motivo de não ter
`MapDelete`) e o catálogo devolve inativas. O cruzamento não constrói caminho
para esse caso; se um id não casar, a linha simplesmente não é montada, sem
tela de erro.

### D3 — Rascunho, barra de salvamento e guarda de navegação — **contra a regra 4 do handoff**

Esta decisão muda o desenho da tela inteira, e é a que o handoff pede ao
contrário. A regra 4 diz: sem rascunho, sem `UnsavedChangesBar`, uma requisição
por ação, spinner na linha. O handoff prevê a possibilidade de o backend só
expor `PUT` de conjunto e, nesse caso, manda **manter a UI por ação enviando o
conjunto resultante a cada clique**. É essa saída que está sendo recusada.

**Três motivos, em ordem de peso:**

1. **Feedback por linha sobre operação de conjunto mente sobre o que falhou**
   (convenção 13). Se o `PUT` volta 400 ou 500, o que falhou foi a escrita do
   conjunto inteiro, e **nada** mudou no servidor. Uma linha girando e depois
   ficando vermelha afirma que aquela base específica falhou — que não é o que
   aconteceu. A barra afirma a coisa certa: a alteração não foi gravada, o
   rascunho continua na tela.
2. **Ação por linha sob replace-all perde escrita concorrente, e o protótipo
   deixa isso acontecer.** Percorrendo a tela, os botões das outras linhas
   continuam habilitados enquanto uma requisição corre. Dois cliques rápidos
   produzem dois `PUT` com o conjunto calculado a partir do **mesmo** estado
   anterior; o segundo a chegar desfaz o primeiro, em silêncio. Com rascunho,
   um `PUT` sai por salvamento, com o conjunto completo e atual.
3. **Consistência dentro da mesma página.** `useUnsavedChangesGuard` tem
   exatamente dois consumidores, conferido: `AgentToolsTab` e
   `AgentDelegationsTab` — as duas outras abas de vínculo deste mesmo detalhe,
   as duas sobre contratos de substituição de conjunto. Três abas de vínculo
   com dois idiomas de salvamento diferentes seria inconsistência sem causa.

**O que continua valendo do handoff:** o modal com busca, a lista mostrando só
as vinculadas, o modal não fechar ao escolher, o estado vazio forte. O que muda
é apenas **quando a escrita acontece**.

**O que a exposição a escrita concorrente continua sendo:** exatamente a mesma
das outras duas abas — o endpoint não tem versão nem `ETag`, então dois
operadores salvando o mesmo agente continuam podendo se sobrescrever. D3 não
cria esse risco nem o resolve; reduz de "a cada clique" para "a cada
salvamento". Registrado em R1.

### D4 — Ordem da lista replica a do servidor: nome, desempate por id

A lista renderiza o rascunho, que inclui bases ainda não gravadas. Ordenar por
nome com desempate por id é exatamente o que
`ListAgentsQueryHandler`/`AgentKnowledgeBaseLookup` fazem, então a lista **não
se remonta** quando o `PUT` volta: a posição de uma base recém-escolhida é a
mesma antes e depois de salvar. Ordenar pela ordem de escolha, ou pela do
catálogo (`CreatedAt`), faria a lista pular no salvamento.

### D5 — O que a linha e o modal exibem, e o que se recusam a exibir

**Exibem:** nome (link para o detalhe da base), descrição, e — no modal — uma
marca de base inativa. A marca é **acréscimo** ao protótipo: `isActive` está no
catálogo que a tela já tem, e sem ela o operador vincula uma base desativada
sem nenhum sinal, descobrindo só depois pela faixa na lista.

**Não exibem:** contagem de documentos, "{k} indexados", estado de indexação, e
o aviso "nenhum documento indexado nesta base". A causa é uma só e foi
conferida no `record`, não deduzida: `KnowledgeBaseResponse` é
`(Id, Name, Description, IsActive, CreatedAt, UpdatedAt)`. O dado existiria
só a uma requisição por base — 6+ requisições para a aba de um agente com 5
bases. Etapa 2 cria o campo; até lá a tela cala.

A garantia é gravada como **asserção negativa** na spec e no teste (convenções
13 e 15): é a asserção negativa que impede alguém de "completar" a linha com
zero mais tarde, exatamente como a 5a-1 fez com as colunas do catálogo.

**Sobrevive um aviso de dois:** o de base inativa, que sai de `IsActive`. Ele é
a contraparte de produto do risco R6 da etapa 3 — o backend aceita de propósito
vincular base inativa (`where kb.IsActive` pertence à resolução, não ao
vínculo), e é a UI que precisa explicar a consequência.

### D6 — Busca do modal usa `matchesSearch`, sem acento

O protótipo usa `indexOf` cru sobre `nome + descrição`; percorrido ao vivo,
`cardapio` não acha `Cardápio A La Carte`. Todas as buscas do painel usam
`matchesSearch` desde `frontend-listas-busca-e-colunas`, sobre nome e
descrição — inclusive a do catálogo de bases, na 5a-1. Regra que o sistema já
escreveu vence protótipo (convenção 17). Busca no cliente sobre a resposta
inteira, como as outras.

**Dívida assumida, não introduzida:** `GET /knowledge-bases` não tem `?q=` nem
paginação, e o handoff declara 100+ bases como volume real. É a mesma dívida
que a 5a-1 já registrou para o catálogo; esta change a herda no modal, sem
agravá-la.

### D7 — Catálogo buscado só com a aba ativa

`useKnowledgeBasesQuery({ enabled: activeTab === KNOWLEDGE_TAB })`, no mesmo
molde de `useMcpServersQuery` na aba de ferramentas — o `enabled` já existe no
hook, escrito na 5a-1 prevendo esta etapa. Falha do catálogo mostra `Alert` no
painel da aba, não derruba a página, mesmo tratamento do painel de ferramentas.

Sem o catálogo não há descrição nem estado, então a aba **não renderiza a lista
pela metade**: informa a falha. Renderizar só nome e id (que vêm do agente)
daria uma tela que parece funcionar e esconde que metade do conteúdo não
chegou.

### D8 — A copy do card do catálogo de bases para de anunciar etapa futura

`KnowledgeBaseAgentsCard` diz hoje, quando ninguém consulta a base: *"Nenhum
agente consulta esta base. O vínculo com agentes chega na próxima etapa."* —
com o motivo escrito no próprio componente (mandar o operador para uma tela
inexistente afirmaria capacidade que o painel não tem). A tela passa a existir,
e a copy passa a dizer onde vincular. Isso muda um cenário vivo de
`knowledge-base-catalog-ui`, que exige hoje a ausência dessa indicação — daí a
capability aparecer como modificada na proposta.

### D9 — A nota de projeto do rodapé não vai para o código

"Por que só as vinculadas, e numa aba própria" é justificativa de decisão de
design, para o time discutir onde ela aparece. O próprio handoff diz que não é
para levar ao codebase. A justificativa que importa está aqui, no `design.md`.

### D10 — Carve de ordenação: **segue registrado**, com o gatilho corrigido

O item aberto em `02-HISTORICO_E_STATUS.md` registra quatro sites de
`apps/api` que ordenam por nome sem desempate (`AgentDelegationLookup.cs:23`,
`AgentMcpServerLookup.cs:22`, `ListAgentsQueryHandler.cs:34` e `:51`) e nomeia
**esta etapa** como gatilho, com a justificativa de que ela *"renderiza as três
listas lado a lado"*. Conferido no código, a premissa é falsa:

- **As três listas não ficam lado a lado.** São três abas, e
  `AgentDetailPage` monta com `keepMounted={false}` — o conteúdo das outras
  abas nem existe no DOM.
- **Esta aba não renderiza nenhuma das duas ordenações instáveis.** Ela usa
  `agent.knowledgeBases`, que **já tem** o `ThenBy(Id)` desde
  `knowledge-base-vinculo-agente`, e ordena o rascunho pelo mesmo critério
  (D4). Os contadores das outras abas são `length`, não ordem.

Sequenciar um carve de `apps/api` antes de uma change de tela que não depende
dele seria inflar escopo por um gatilho que não disparou. **Decisão: segue
registrado**, com o gatilho reescrito para o que de fato o dispara — uma tela
que renderize `mcpServers` ou `delegatesTo` **em lista**, ou qualquer change
que passe a prometer ordem para esses dois campos.

Achado a somar ao item, do mesmo percurso: **`GET /knowledge-bases` ordena por
`CreatedAt`**, que também não é único por construção — é um quinto site, da
mesma classe e da mesma severidade (apresentação, sem contrato violado), e o
modal desta change passa a renderizá-lo. Vai para o registro junto dos outros,
não para esta change (convenção 12: defeito pertence a quem expõe, e se corrige
em change própria).

### D11 — Sem fallback de descrição vazia

`KnowledgeBase.description` é `string`, não `string | null`, e a API a exige
não vazia na criação **e** na edição (`ValidateShape` em
`KnowledgeBaseEndpoints`). O texto "Sem descrição — o modelo não sabe quando
consultar." do protótipo é código morto contra este backend. Mesma D7 da 5a-1,
que já tinha fechado isso para o catálogo.

### D12 — "Concluir" fecha; salvar é a barra

Consequências de D3 no modal, todas explícitas:

- A nota do cabeçalho deixa de dizer "O vínculo é salvo na hora." e passa a
  dizer o que é verdade: as escolhas entram na lista e são gravadas ao salvar.
- Escolher uma base **não** dispara requisição e **não** fecha o modal —
  continua dando para escolher várias em sequência, que era o ponto do desenho.
- `Concluir` fecha o modal. Não existe "salvar" dentro dele.
- `Criar nova base` navega para `/knowledge-bases/new`. Com rascunho sujo, essa
  navegação cai na **guarda** e abre o modal de alterações não salvas — o
  mesmo comportamento das outras duas abas. É consequência aceita, não defeito:
  o operador está prestes a perder escolhas que ainda não gravou.

## Árvore de pastas

Só `apps/frontend/src`. Nenhum arquivo em `libs/` — não há segundo consumidor
de nada aqui, e a convenção 2 pede repetição já observada, não prevista.

```
apps/frontend/src/
├── components/data/SectionedCard.tsx            (reuso, sem mudança)
├── components/feedback/UnsavedChangesBar.tsx    (reuso, sem mudança)
├── components/feedback/UnsavedChangesModal.tsx  (reuso, sem mudança)
├── hooks/useUnsavedChangesGuard.ts              (reuso, sem mudança)
├── utils/searchText.ts                          (reuso, sem mudança)
├── features/agents/
│   ├── api/
│   │   ├── agentsApi.ts                     MODIFICADO  + replaceAgentKnowledgeBases
│   │   ├── agentsApi.test.ts                MODIFICADO
│   │   ├── useAgents.ts                     MODIFICADO  + useReplaceAgentKnowledgeBasesMutation
│   │   └── useAgents.test.ts                MODIFICADO
│   ├── components/
│   │   ├── AgentKnowledgeTab.tsx            CRIADO
│   │   ├── AgentKnowledgeTab.test.tsx       CRIADO
│   │   ├── KnowledgeBaseLinkModal.tsx       CRIADO
│   │   └── KnowledgeBaseLinkModal.test.tsx  CRIADO
│   ├── pages/
│   │   ├── AgentDetailPage.tsx              MODIFICADO  + aba, parseTab, query do catálogo
│   │   └── AgentDetailPage.test.tsx         MODIFICADO
│   └── utils/                               (diretório novo, molde em features/mcp-servers/utils)
│       ├── knowledgeBaseRows.ts             CRIADO      cruzamento + ordenação (D2, D4)
│       └── knowledgeBaseRows.test.ts        CRIADO
└── features/knowledge-bases/
    └── components/
        ├── KnowledgeBaseAgentsCard.tsx      MODIFICADO  copy (D8)
        └── KnowledgeBaseAgentsCard.test.tsx MODIFICADO
```

`AgentKnowledgeTab` fica em `features/agents` e importa o tipo `KnowledgeBase`
de `features/knowledge-bases`, como `AgentToolsTab` já importa `McpServer` de
`features/mcp-servers`: feature se organiza por conceito de domínio, e a aba é
do detalhe do agente (convenção 7). A página busca as duas consultas e repassa
por propriedade; nenhum dos dois componentes novos importa hook de query.

## Risks / Trade-offs

Cada risco com contraparte verificável (convenção 10).

- **R1 — Substituição de conjunto sobrescreve escrita concorrente.** Dois
  operadores (ou duas abas) editando o mesmo agente: o segundo salvamento
  desfaz o primeiro, sem aviso. O endpoint não tem versão nem `ETag`, então
  não há defesa do lado do cliente. → *Mitigação:* D3 reduz a janela de "a cada
  clique" para "a cada salvamento", que é exatamente a exposição que
  `AgentToolsTab` e `AgentDelegationsTab` já têm. **Contraparte verificável:**
  cenário e teste de que o salvamento envia **sempre o conjunto completo
  resultante**, nunca um delta, e de que a aba se rebaseia na resposta
  (`updated.knowledgeBases`) em vez de manter o rascunho anterior — é isso que
  impede um segundo salvamento de reenviar estado velho. A exposição residual
  vai para os itens em aberto, com gatilho (surgir necessidade real de edição
  concorrente).
- **R2 — A tela afirmar mais do que o sistema sabe.** A pressão para
  "completar" a linha com `0 documentos` é real e já aconteceu nesta base. →
  *Mitigação:* D5. **Contraparte verificável:** asserção **negativa** na spec e
  no teste — a linha e a linha do modal não exibem contagem de documentos nem
  texto de indexação. A asserção positiva não protege contra isso; a negativa
  sim (convenção 13).
- **R3 — Base inativa vinculada passar despercebida.** O agente não a consulta
  e nada na tela diz por quê. → *Mitigação:* D5, nos dois lugares.
  **Contraparte verificável:** cenário e teste do aviso na linha da lista **e**
  da marca na linha do modal; mais o par vazio, que é a asserção de que base
  ativa **não** exibe aviso (convenção 5: todo caso "com item" ganha o par).
- **R4 — Buscar o catálogo em toda visita ao detalhe do agente.** São 100+
  bases declaradas. → *Mitigação:* D7. **Contraparte verificável:** teste de
  que o catálogo **não** é requisitado enquanto a aba não está ativa.
- **R5 — Rascunho perdido em navegação.** Consequência direta de D3, e mais
  visível aqui do que nas abas irmãs porque o modal tem um link que navega
  (`Criar nova base`). → *Mitigação:* a guarda já existente.
  **Contraparte verificável:** cenário e teste de que navegar com rascunho sujo
  abre o modal de alterações não salvas, incluindo o caminho pelo link do
  modal.
- **R6 — Busca que falha com acento.** Digitar `cardapio` e não achar
  `Cardápio` é o caso real do painel em português. → *Mitigação:* D6.
  **Contraparte verificável:** teste que digita o termo sem acento e espera a
  base acentuada na lista.
- **Trade-off assumido: contrariar o handoff em D3.** Se a leitura de produto
  for que salvar na hora vale mais que a consistência e que a honestidade do
  feedback, a decisão se inverte — e aí a mesma D3 é o registro do que se está
  aceitando junto (perda silenciosa sob dois cliques rápidos). Está aqui
  escrita para poder ser contestada, não para ser descoberta depois.
- **Trade-off assumido: uma requisição a mais por visita à aba.** D2 exige o
  catálogo para ter descrição e estado. É uma requisição, não N, e só quando a
  aba está ativa.

## Verificação manual (convenção 14)

A suíte roda em jsdom e não enxerga cor, contraste nem layout. A conferência
manual é **tarefa própria e iterativa**, tela a tela, nos dois esquemas de cor,
contra o protótipo anexado — não uma linha no fim do `tasks.md`.

Nota da 5a-1 que se aplica aqui: com a identidade visual já estabelecida, as
rodadas acham **cópia, estrutura e escopo**, não cor. Isso não dispensa a
rodada nos dois temas (a faixa âmbar de aviso é papel visual que troca de ponta
da escala, convenção 16); muda onde olhar primeiro.

Telas a percorrer, no mínimo: aba com bases vinculadas, aba vazia, aviso de
base inativa, modal cheio, modal com busca sem resultado, modal com catálogo
vazio, barra de salvamento visível, e o modal de alterações não salvas.

**E dimensões, não só estados** — acrescentado depois de a conferência declarar
convergência e o usuário achar, na tela dele, uma largura errada que quatro
rodadas não viram. A causa tem duas partes, e as duas viram regra:

1. **Comparar largura e altura contra o protótipo explicitamente.** As quatro
   rodadas compararam *estados* (lista, vazio, aviso, modal) e nunca uma
   dimensão. O defeito era `maw={860}` no container da aba, quando o protótipo
   deixa o card em largura cheia e limita **a descrição** em 620px — as duas
   coisas invertidas.
2. **Capturar num viewport pelo menos tão largo quanto o do operador.** A
   captura usava 1440px, onde um card de 860 termina perto da borda útil e a
   diferença não salta aos olhos; em ~1860px ela é gritante.

O erro de origem foi reusar o idioma da aba vizinha sem conferir: as três abas
do detalhe têm larguras **deliberadamente diferentes** no protótipo — Delegações
620px (lista de checkbox), Ferramentas e Conhecimento em largura cheia.
`AgentDelegationsTab` está fiel; quem copiou errado foi esta change.

## Baseline da suíte, estabelecida ao propor (convenção 19)

`apps/frontend`, árvore em `2e5f750` limpa (só o diretório desta change,
sem arquivo de teste, como não rastreado). A contagem bate com a registrada:
**66 arquivos / 574 testes**.

**A baseline é verde**, e isso é medição, não previsão. Quatro execuções, com a
carga externa **antes** de cada uma anotada:

| # | Execução | Carga externa antes | Resultado | Duração |
|---|---|---|---|---|
| 1 | `npm test -- --run` (padrão) | load 14,5; VM do Podman ~289%, `ReportCrash` ~77% | 21 reprovados | — |
| 2 | idem, repetida | load ~11,7; mesma VM | 26 reprovados | — |
| 3 | `npx vitest run --maxWorkers=3` | load ~11,5; mesma VM | 573/574 | 319 s |
| 4 | `npm test -- --run` (padrão) | **load 3,1; sem a VM; nada acima de ~40%** | **574/574 ✅** | 103 s |
| — | cada arquivo reprovado, isolado | qualquer | passa | — |

**A causa é contenção de CPU externa, e foi identificada — não suposta.** Todas
as falhas são `Test timed out in 15000ms`, todas em testes que digitam em
formulário (`userEvent`), e a execução 4 as elimina sem tocar em uma linha de
código.

**O que a execução 4 corrige na leitura, e vale carregar:** a suíte **não é
sensível a load alto em si**. No padrão ela sozinha leva o load de 3,1 para
**57,6** em 12 núcleos — cinco vezes sobrescrita — e passa 574/574 assim. O que
a derruba é **competição externa**: ~2,9 núcleos consumidos por outro processo
(a VM do Podman) mais `ReportCrash` foram suficientes para empurrar os testes de
`userEvent` além dos 15 s. Portanto o limiar útil se mede **antes** de começar,
sobre quem mais está na máquina — nunca durante a execução, onde o número alto é
a própria suíte trabalhando.

Bracket medido, que é o que sustenta o limiar da tarefa 1.1:

- **verde** com load prévio de 3,1-4,2 e nenhum processo externo acima de ~40%;
- **vermelho** com load prévio de 11,5-14,5 e um processo externo a ~289%.

Entre 4,2 e 11,5 não há medição — o limiar declarado (load prévio < 5,0, nenhum
processo não-suíte ≥ 100%, ou seja um núcleo cheio) fica dentro do lado verde
medido, com folga, e é conservador por escolha, não por dado.

A conclusão que **não** se autoriza mesmo com a execução 4 na mão: tratar
timeout como "ambiental" por padrão. A convenção 19 registra três vezes em que
essa leitura estava errada, e o que separa este caso daqueles é ter o consumidor
de CPU nomeado, a passagem isolada como contraprova, **e a execução verde com o
consumidor ausente** — que é a evidência que faltava nas três vezes anteriores.

## Migration Plan

Não há. Só `apps/frontend`, sem migração de banco, sem mudança de contrato de
fio, sem janela em que os dois lados estejam em versões incompatíveis: as rotas
consumidas estão implantadas desde as etapas 1 e 3. Rollback é reverter o
commit.

## Tamanho projetado por componente (convenção 18)

Projetado **depois** de fechar a verificação e as decisões — as duas dimensões
contadas separadas, com o blast radius lido no código, e cada arquivo de
produção em par com o teste dele. Quinta medição da série.

**Âncoras medidas no repositório**, não estimadas de cabeça — a convenção pede
custo por componente sobre número real:

| Arquivo existente | Linhas |
|---|---|
| `AgentDelegationsTab.tsx` / `.test.tsx` | 155 / 255 |
| `AgentToolsTab.tsx` / `.test.tsx` | 217 / 456 |
| `agentUsage.ts` / `.test.ts` | 14 / 65 |
| `KnowledgeBaseAgentsCard.tsx` / `.test.tsx` | 66 / 99 |
| `AgentDetailPage.test.tsx` | 418 |

Custo unitário de cenário que sai daí: `AgentDelegationsTab.test.tsx` cobre ~10
cenários em 255 linhas, ou **~25 linhas por cenário** — acima dos 19-21 que a
convenção registrava, e coerente com o refinamento da quarta medição (cenário
com arranjo próprio custa 25-40). Cenário de unidade sem render é mais barato:
`agentUsage.test.ts` faz ~4 em 65 linhas.

**Criados — 6 arquivos / ~920 linhas**

| Arquivo | Linhas | Base da conta |
|---|---|---|
| `AgentKnowledgeTab.tsx` | ~195 | 155 do `AgentDelegationsTab`, mais estado vazio (~20), aviso por linha (~12) e acoplamento do modal (~15) |
| `AgentKnowledgeTab.test.tsx` | ~300 | 13 cenários × ~25 |
| `KnowledgeBaseLinkModal.tsx` | ~130 | corpo, busca, linhas com alternância, marca de inativa, dois vazios, rodapé |
| `KnowledgeBaseLinkModal.test.tsx` | ~175 | 7 cenários × ~25 |
| `knowledgeBaseRows.ts` | ~45 | cruzamento + ordenação (D2, D4); `agentUsage.ts` tem 14, mas faz só um `filter` |
| `knowledgeBaseRows.test.ts` | ~75 | 4 cenários de unidade, no custo de `agentUsage.test.ts` |

**Modificados — 8 arquivos / ~225 linhas**, projetados **em pares** (o
refinamento que a quarta medição obrigou: todo arquivo modificado arrasta o
teste dele, e ignorar isso errou 3x lá).

| Par | Linhas | O que muda |
|---|---|---|
| `AgentDetailPage.tsx` + teste | ~25 + ~110 | constante da aba, `parseTab`, `Tabs.Tab`, `Tabs.Panel`, query do catálogo com `enabled`; no teste, 5 cenários novos mais 3 existentes editados (três → quatro abas) |
| `agentsApi.ts` + teste | ~8 + ~30 | uma função; dois cenários, um deles o conjunto vazio |
| `useAgents.ts` + teste | ~12 + ~25 | um hook de mutação, um cenário de cache |
| `KnowledgeBaseAgentsCard.tsx` + teste | ~5 + ~10 | copy (D8) |

**Total projetado: 14 arquivos / ~1145 linhas de código.** Faixa: 13-16
arquivos / 1000-1300 linhas.

O par mais caro dos modificados é `AgentDetailPage.test.tsx`, e é ele que
carrega a maior incerteza: passar de três para quatro abas edita cenários já
escritos, e o custo de editar cenário existente não tem âncora medida nesta
base.

**Blast radius de tipo compartilhado: zero, e foi conferido.** A quarta medição
registrou que campo obrigatório novo num tipo compartilhado do frontend tem
como blast radius a contagem de **fixtures**, não a de componentes — e que
quem projeta isso lendo o código de produção erra por fator de 20. Esta change
**não acrescenta campo a tipo nenhum**: `Agent.knowledgeBases` já existe e já é
obrigatório desde a 5a-1 (foi ela que pagou as 22 fixtures em 21 arquivos), e
`KnowledgeBase` não muda. A verificação foi feita; o resultado é zero.

**A âncora citada é decomposta, não headline** (a razão de headline chegou a
3,1x nesta base por causa de `.Designer.cs` de migração — aqui não há migração,
mas a regra de citar decomposto vale igual): 5a-1 entregou 52 arquivos / 3078
linhas **de código** (25 criados / 2949; 27 modificados / 129), das quais 21
modificados eram fixtures de uma linha. Esta change é uma fração daquilo: uma
aba, um modal, um utilitário, contra uma feature inteira com quatro páginas.

**O que esta projeção não pode conter**, e a quarta medição obriga a dizer:
escopo acrescentado durante a implementação (a 5a-1 ganhou +4 criados e +21
modificados por duas decisões tomadas na conferência manual) e o que só a
montagem revela — verificação lê o que o código **expõe**, montagem revela o
que ele **assume**. A faixa de modificados carrega essa incerteza; a de criados
não. Medir o método comparando só o escopo que estava projetado.

## Quinta medição da convenção 18 — projetado × entregue

| | projetado | entregue | erro |
|---|---|---|---|
| criados | 6 arquivos / ~920 linhas | **6 / 1025** | arquivo **exato**; linhas +11,4% |
| modificados | 8 / ~225 | **8 / 302** | arquivo **exato**; linhas +34% |
| total | 14 / ~1145 | **14 / 1327** | arquivo **exato**; linhas +15,9% |

**Terceiro acerto seguido na contagem de arquivo, e o primeiro em que as duas
metades acertam.** `knowledge-base-vinculo-agente` acertou 25/25 no total,
`frontend-knowledge-base-catalogo` acertou os criados (21/21) e errou os
modificados 3x; aqui as duas metades batem exatas. O método — contar por
componente, criados e modificados separados, em pares com o teste, a partir do
blast radius lido no código — está confirmado para contagem de arquivo.

**O erro de linha está quase todo em teste, e a causa é contagem de cenário, não
custo por cenário.** Projetei 13 + 7 = **20** cenários para os dois arquivos de
componente novos; foram entregues 16 + 12 = **28**. O custo unitário se
comportou: a aba saiu a ~22 linhas por cenário (dentro da faixa medida de ~25) e
o modal a ~16, mais barato que o previsto porque as asserções compartilham um
helper de render. Ou seja: a régua de custo está calibrada; o que errou foi
**quantos** cenários existiriam.

**E há uma pista forte de por quê, que vale testar na sexta medição.** A spec
desta change tem **28 cenários** — exatamente o número de testes entregues nos
dois arquivos de componente. A correspondência **não é item a item** (os 5 testes
de unidade de `knowledgeBaseRows` não são cenários de spec, e alguns cenários da
spec caíram em `AgentDetailPage.test.tsx`), então o casamento exato dos totais
tem componente de coincidência. Mas a hipótese é barata e verificável:
**projetar linhas de teste a partir da contagem de `#### Scenario:` da spec, e
não da intuição.**

O obstáculo é de ordem, e é o que a convenção precisa registrar: **a projeção
mora no `design.md`, que é escrito ANTES da spec.** A convenção 18 já manda
projetar depois de fechar a verificação; isto acrescenta que a spec é o artefato
que fixa a contagem de cenário, e quem projeta antes dela está adivinhando a
variável que domina o custo de teste. Ou se projeta depois da spec, ou se conta
os cenários que as decisões do design já implicam.

**Um desvio isolado, com causa própria:** `useAgents.test.ts` saiu 51 linhas
contra ~25 projetadas (2x). Projetei "um cenário de cache" e o padrão da casa é
o par sucesso + erro (convenção 5: todo caso "com item" ganha o par). Custo de
não aplicar uma convenção que já estava escrita.

**Escopo acrescentado durante a implementação**, que a projeção não podia conter
e que **não** entra na conta do erro de método: o scroll interno do modal e as
duas correções de truncagem (conferência manual, rodadas 2 e 3), e dois cenários
de teste que só existiram ao descobrir que a asserção original não discriminava
(o rebase do rascunho — ver abaixo).

**Um achado de teste que vale além desta change.** A primeira versão do teste de
rebase afirmava "a barra some depois de salvar", e ela **não some** no teste: a
barra compara o rascunho com o vínculo **gravado**, que chega por propriedade, e
quem a faz sumir é a página re-renderizando com o agente atualizado. O
componente sozinho não tem como saber. `AgentToolsTab` e `AgentDelegationsTab`
têm exatamente a mesma propriedade, sem teste que a cubra. A asserção foi
trocada pela que discrimina de verdade — resposta do servidor **diferente** do
enviado, e a gravação seguinte partindo da resposta —, porque com resposta
idêntica os dois comportamentos (rebasear ou não) são indistinguíveis.

## Open Questions

Nenhuma incerteza de negócio em aberto. As três que existiam foram fechadas
com o código na mão e estão registradas como decisão: como a UI se comporta
sobre replace-all (D3), o que a tela pode afirmar sobre indexação (D5) e se o
carve de ordenação entra aqui (D10).
