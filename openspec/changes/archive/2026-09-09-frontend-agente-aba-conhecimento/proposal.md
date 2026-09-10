## Why

O painel já tem o catálogo de bases de conhecimento (etapa 5a-1) e a API já
expõe o vínculo agente ↔ base desde `knowledge-base-vinculo-agente` (etapa 3),
mas **não existe nenhuma tela que vincule os dois**. Hoje o operador consegue
criar uma base e ver, no detalhe dela, quais agentes a consultam — e não tem
como mudar essa lista. O próprio painel admite o buraco em voz alta: o card
"Agentes que consultam esta base" diz *"O vínculo com agentes chega na próxima
etapa."* Esta é a próxima etapa.

Sem essa tela, o sintoma que leva alguém ao painel — *o agente está respondendo
sem contexto, inventando horário e preço* — não tem onde ser diagnosticado nem
corrigido. É também a **última etapa da linha de bases de conhecimento que não
depende da chave de provedor de embedding**: as etapas 2, 4, 5a-2 e 5c esperam
`0b`, esta não espera nada.

Só `apps/frontend`. Nenhuma rota, campo ou comportamento novo de backend:
`PUT /agents/{id}/knowledge-bases`, `GET /knowledge-bases` e
`AgentResponse.knowledgeBases` já existem e foram conferidos no código ao
propor (convenção 1, corolário).

## What Changes

- **Quarta aba `Conhecimento` no detalhe do agente** (`apps/frontend`), entre
  Ferramentas e Delegações, com a aba ativa na URL (`?tab=conhecimento`) e
  contador de bases vinculadas oculto quando zero — no desenho já estabelecido
  por `frontend-agente-detalhe-abas` (`parseTab`, `keepMounted={false}`, só a
  aba ativa montada).
- **Lista das bases vinculadas**, uma linha por base: nome como link para o
  detalhe da base, descrição vinda do catálogo, e a ação de desvincular.
- **Modal de vincular** com busca sobre o catálogo completo, alternando
  Vincular/Vinculada por linha, sem fechar a cada escolha.
- **Estado vazio explicativo** — o que significa um agente sem base vinculada
  ("responde só com o system prompt"), não uma linha de texto.
- **Aviso de base inativa vinculada**, na linha: a base continua vinculada e
  não é consultada. É a contraparte de produto do risco R6 da etapa 3.
- **Rascunho com barra de salvamento e guarda de navegação**, como as duas
  outras abas de vínculo do mesmo detalhe. Isto **contraria a regra 4 do
  handoff de design**, que proíbe `UnsavedChangesBar` e pede uma requisição por
  ação: o backend oferece substituição do conjunto inteiro, e ação por linha
  sobre operação de conjunto tanto mente sobre o que falhou quanto perde
  escrita concorrente. Motivo completo e alternativa descartada no `design.md`
  (convenção 17).
- **Correção de copy no catálogo de bases**: o card "Agentes que consultam esta
  base" para de anunciar etapa futura e passa a apontar a aba que agora existe.
- `apps/frontend`: `replaceAgentKnowledgeBases` no `agentsApi` da feature
  `agents` e a mutação correspondente em `useAgents` — o `request<T>` próprio
  da feature, sem cliente HTTP compartilhado (convenção 7).

**Fora de escopo, cada exclusão conferida no código** (convenção 1, corolário —
"fora de escopo por dependência" exige abrir o código, foi o erro cometido uma
vez na 5a-1):

- **Contagem de documentos e "{k} indexados" por linha**, que o protótipo
  mostra na aba e no modal. `KnowledgeBaseResponse` tem exatamente seis campos
  (`Id`, `Name`, `Description`, `IsActive`, `CreatedAt`, `UpdatedAt`) —
  conferido no `record`, não deduzido. Documento vive em
  `GET /knowledge-bases/{knowledgeBaseId}/documents`, **uma requisição por
  base**: com as 5+ bases por agente que o handoff declara, a aba passaria a
  custar 6+ requisições para exibir um número. Mesma decisão D1 da 5a-1, e o
  dado nasce na etapa 2.
- **Aviso "nenhum documento indexado nesta base"**, pela mesma causa: depende
  de contagem que não existe. Dos dois avisos por linha do protótipo,
  **sobrevive só o de base inativa**, que sai de `KnowledgeBase.IsActive`.
- **Gestão de documentos** (etapa 5a-2) e **diagnóstico do índice** (5c) — o
  handoff é explícito em que criar e editar documento continua exclusivamente
  em `/knowledge-bases`.
- **Nota de projeto recolhida no rodapé do card** ("Por que só as vinculadas, e
  numa aba própria"): o próprio handoff diz que é material de discussão, não
  requisito.

## Capabilities

### New Capabilities
- `agent-knowledge-binding-ui`: a aba Conhecimento do detalhe do agente em
  `apps/frontend` — lista das bases vinculadas, modal de vincular com busca,
  estado vazio, aviso de base inativa, rascunho com barra de salvamento e
  guarda de navegação, e as asserções negativas que impedem a tela de afirmar
  contagem de documentos ou estado de indexação que o sistema não serve.

### Modified Capabilities
- `agent-catalog-ui`: o requisito **"Abas do detalhe do agente"** fixa hoje
  **três** abas ("visão geral, ferramentas e delegações") e um cenário chamado
  "Três abas exibidas no detalhe do agente". Passa a quatro, com a posição de
  Conhecimento entre ferramentas e delegações, o contador da nova aba e a nova
  identificação de aba na URL.
- `knowledge-base-catalog-ui`: o cenário **"Nenhum agente consulta a base"**
  exige hoje que a tela informe a ausência *"sem indicar uma tela de vínculo"*,
  porque a tela não existia. Ela passa a existir, e a copy passa a apontá-la.

## Impact

- **`apps/frontend` apenas.** Nenhuma mudança em `apps/api`, `apps/workers` ou
  `apps/inbox`; nenhuma migração; nenhum `ProjectReference` novo.
- **Rotas consumidas, todas já existentes**: `GET /agents/{id}` (já buscada
  pela página), `GET /knowledge-bases` (buscada só quando a aba está ativa, via
  o `enabled` que `useKnowledgeBasesQuery` já aceita — foi escrito na 5a-1
  prevendo esta etapa) e `PUT /agents/{id}/knowledge-bases`.
- **Arquivos tocados fora da feature `agents`**: `KnowledgeBaseAgentsCard` e o
  teste dele, em `features/knowledge-bases`, pela correção de copy.
- **Sem blast radius de tipo compartilhado**: `Agent.knowledgeBases` já é campo
  obrigatório do tipo desde a 5a-1, e `KnowledgeBase` não muda — nenhuma
  fixture de teste precisa ganhar campo. A verificação foi feita justamente
  porque a quarta medição da convenção 18 mostrou que é aqui que a projeção
  erra por fator de 20; desta vez a conta dá zero.
- **Item em aberto tocado**: a ordenação por nome sem desempate em quatro sites
  de `apps/api` tem **esta etapa como gatilho registrado**. A decisão, com o
  que foi conferido no código, está no `design.md` (D10).
