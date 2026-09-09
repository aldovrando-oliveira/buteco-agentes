Todas as tarefas rodam em **`apps/frontend`**. Nenhuma toca `apps/api`,
`apps/workers` ou `apps/inbox` — o backend desta etapa já está entregue.

Telas referenciadas por nome, como no `design.md`: **Catálogo de bases**,
**Detalhe da base**, **Formulário de base**. O protótipo navegável está em
`design/Buteco Agentes.dc.html` (abrir com `design/support.js` ao lado); a
especificação de cada tela está em `design/CONHECIMENTO.md`, revisão 2.

## 1. Fundação: tipos, cliente HTTP e cache

- [x] 1.1 (`apps/frontend`) Criar `features/knowledge-bases/types/knowledgeBase.ts` com `KnowledgeBase` espelhando os **seis** campos de `KnowledgeBaseResponse` — `id`, `name`, `description`, `isActive`, `createdAt`, `updatedAt` — e os tipos de entrada de criação e edição. `description` é `string`, **não** `string | null`: a API a impõe não vazia nas duas rotas (design.md, D7).
- [x] 1.2 (`apps/frontend`) Criar `features/knowledge-bases/api/knowledgeBasesApi.ts` com `request<T>` e `ApiError` **próprios da feature**, importando `src/auth/token` — sem cliente HTTP compartilhado, contra a sugestão do handoff (design.md, D12). Molde: `features/mcp-servers/api/mcpServersApi.ts`.
- [x] 1.3 (`apps/frontend`) Cobrir `knowledgeBasesApi` com teste: as seis rotas montadas corretamente, `ApiError` carregando `status` e `problem`, e 400 preservando `ValidationProblemDetails.errors`.
- [x] 1.4 (`apps/frontend`) Criar `features/knowledge-bases/api/useKnowledgeBases.ts` no formato de `useMcpServers.ts`: `queryKey` `['knowledge-bases']` e `['knowledge-bases', id]`; mutations de criação, edição, ativação e desativação com `setQueryData` no item e `invalidateQueries` na coleção.
- [x] 1.5 (`apps/frontend`) Cobrir `useKnowledgeBases` com teste, incluindo o par vazio de cada caso "com item" (convenção 5): coleção com bases e coleção vazia.

## 2. Catálogo de bases (`/knowledge-bases`)

- [x] 2.1 (`apps/frontend`) Criar `components/KnowledgeBaseTable.tsx` com as colunas **Base** (nome como link real + descrição) e **Estado** (badge Ativa/Inativa), reusando a aparência de badge do tema e o molde de `McpServerTable.tsx`. **Sem** as colunas `Documentos`, `Indexação` e `Consultada por` (design.md, D1 e D2).
- [x] 2.2 (`apps/frontend`) Criar `pages/KnowledgeBaseListPage.tsx` buscando os dados e repassando como prop — a tabela não importa hook de query (convenção 7). Busca com normalização de acentos nas duas direções e filtro de três opções (todas · ativas · inativas), no molde de `frontend-listas-busca-e-colunas`.
- [x] 2.3 (`apps/frontend`) Estado vazio do catálogo: explica o que é uma base de conhecimento e oferece a criação da primeira. Distinto do vazio de busca, que diz que nenhuma base corresponde ao termo.
- [x] 2.4 (`apps/frontend`) Testes de `KnowledgeBaseTable` e `KnowledgeBaseListPage`, incluindo as **asserções negativas** que a convenção 13 pede: a listagem renderizada não contém contagem de documentos nem resumo de indexação, e o filtro não oferece opção de falha.
- [x] 2.5 (`apps/frontend`) **Entrou na conferência (design.md, D21).** Acrescentar a coluna `Consultada por` à `KnowledgeBaseTable`, reusando `utils/agentUsage.ts` de D20, com `useAgentsQuery` na página. Três estados, e não os dois do protótipo: nomes quando há, `Nenhum agente` quando o catálogo carregou e é zero, `—` quando a consulta de agentes falhou — no idioma que `McpServerTable` já usa. Asserção negativa: com a consulta falhando, `Nenhum agente` não aparece.

## 3. Detalhe da base (`/knowledge-bases/{id}`)

- [x] 3.1 (`apps/frontend`) Criar `pages/KnowledgeBaseDetailPage.tsx` com `DetailHeader` (nome + badge de estado), `BackLink` para a listagem, e as datas. **Sem `Tabs`** (design.md, D3). O subtítulo do cabeçalho **não** é a descrição.
- [x] 3.2 (`apps/frontend`) Criar `components/KnowledgeBaseDescriptionCard.tsx` com `SectionedCard` + `SectionLabel`: rótulo identificando o texto como o que o modelo lê, a descrição citada, e a nota de que não é mostrada ao cliente. **Não** implementar o callout de descrição ausente do protótipo — o estado é inalcançável (design.md, D7).
- [x] 3.3 (`apps/frontend`) Criar `components/KnowledgeBaseDocumentsPlaceholder.tsx`: nota de sequenciamento dizendo que a gestão de documentos chega na etapa seguinte. **Nunca** "Nenhum documento nesta base" (design.md, D4).
- [x] 3.4 (`apps/frontend`) Ações do detalhe: `Editar`, e `Ativar`/`Desativar` conforme o estado. `Desativar` passa por modal de confirmação no padrão de agente e servidor MCP; `Ativar` é imediato. Ambos notificam o resultado.
- [x] 3.5 (`apps/frontend`) Tratar 404 do detalhe: informa que a base não foi encontrada, com volta para a listagem.
- [x] 3.6 (`apps/frontend`) Testes do detalhe e dos dois componentes, com as asserções negativas: sem controle de abas, descrição fora do subtítulo do `DetailHeader`, e ausência da cópia "nenhum documento".
- [x] 3.7 (`apps/frontend`) **Entrou na conferência (design.md, D20).** Acrescentar `knowledgeBases: AgentSummaryReference[]` ao tipo `Agent` — obrigatório, não opcional como `a2a`, porque não há janela de migração (D14 ponto 2). Corrigir as fixtures que o `tsc` apontar: foram **22 em 21 arquivos de teste**.
- [x] 3.8 (`apps/frontend`) **Entrou na conferência (D20).** Criar `utils/agentUsage.ts` (`agentsConsultingBase`, molde de `features/mcp-servers/utils/agentUsage.ts`) e `components/KnowledgeBaseAgentsCard.tsx`, ligados ao detalhe via `useAgentsQuery`. **Remover o card de datas**, que era acréscimo sem decisão e o protótipo não tem. Testes com as duas asserções negativas: o vazio não manda o operador para a tela de vínculo que não existe, e falha ao carregar agentes não vira "nenhum agente consulta".

## 4. Formulário de base (`/knowledge-bases/new`, `/knowledge-bases/{id}/edit`)

- [x] 4.1 (`apps/frontend`) Criar `components/KnowledgeBaseForm.tsx` com nome e descrição obrigatórios, aviso de descrição curta, e erro por campo alimentado por `ValidationProblemDetails`. **Sem** guarda de navegação (design.md, D11).
- [x] 4.2 (`apps/frontend`) Criar `pages/KnowledgeBaseCreatePage.tsx` e `pages/KnowledgeBaseEditPage.tsx`, no molde de `McpServerCreatePage`/`McpServerEditPage`. A edição carrega os valores atuais; a criação leva ao detalhe da base criada.
- [x] 4.3 (`apps/frontend`) Testes do formulário e das duas páginas: nome vazio e descrição vazia barrados no cliente sem requisição, 400 da API virando erro por campo, aviso de descrição curta não bloqueando o salvamento, e navegação com alteração pendente **não** bloqueada.

## 5. Navegação e rotas

- [x] 5.1 (`apps/frontend`) Acrescentar as quatro rotas em `app/routes.tsx` dentro do grupo protegido, no formato dos grupos existentes: `index`, `new`, `:id`, `:id/edit`.
- [x] 5.2 (`apps/frontend`) Acrescentar o item **Conhecimento** em `components/layout/AppShell.tsx` com o ícone `BookOpen` do `lucide-react`, na **terceira** posição, antes de Canais (design.md, D8). Atualizar o teste de `AppShell` para o quarto item, conferindo que o nome acessível é o rótulo e que o item fica ativo em rota profunda.

## 6. Conferência manual — tarefa própria e iterativa (convenção 14)

A suíte roda em jsdom e não enxerga cor, contraste nem layout. Cada correção
muda o que fica visível, então esta seção **repete** até fechar: corrigir um item
e voltar ao começo da lista, não seguir em frente.

- [x] 6.1 (`apps/frontend`) Subir a aplicação e abrir `design/Buteco Agentes.dc.html` ao lado. Conferir o **Catálogo de bases** contra o protótipo no esquema **claro** e no **escuro**: densidade da linha, faixa de cabeçalho, aparência do badge, alinhamento da coluna de estado, cópia dos dois vazios.
- [x] 6.2 (`apps/frontend`) Mesma conferência, nos dois esquemas, para o **Detalhe da base** (com atenção ao card de descrição e à borda esquerda accent da citação) e para o **Formulário de base** (aviso de descrição curta, erro por campo).
- [x] 6.3 (`apps/frontend`) **Reprovar o guarda antes de confiar nele** (convenção 15): introduzir de propósito um tom fixo `gray[n]`/`dark[n]` **dentro de um ternário** em um dos componentes novos — a forma exata que já passou verde antes —, confirmar que o guarda estático de tom fixo reprova, e só então remover. Guarda não reprovado não conta como cobertura.
- [x] 6.4 (`apps/frontend`) Registrar no `design.md` qualquer divergência achada na conferência que mude uma decisão, com a causa real (convenção 9). Não deixar só no resumo da sessão.

## 7. Handoff para as etapas seguintes

- [x] 7.1 Registrar em `02-HISTORICO_E_STATUS.md` os requisitos que o protótipo gera para a **etapa 2** (indexação): `FailureReason` legível por operador em vez de exceção crua; contagem de tentativas e instante da última tentativa persistidos; **campo de contagem de fragmentos**, que não existe em nenhum response hoje; e rota de reindexação de documento — backend, que pela convenção 1 não nasce em change de tela (design.md, D5 e D6).
- [x] 7.2 Registrar em `02-HISTORICO_E_STATUS.md` os requisitos para a **5a-1 de dados**, que a etapa 2 também destrava: contagem de documentos e resumo de indexação em `KnowledgeBaseResponse` (ou rota de resumo), sem os quais as colunas `Documentos` e `Indexação` e o filtro `Com falha` não podem existir (design.md, D1 e D9).
- [x] 7.3 Registrar em `02-HISTORICO_E_STATUS.md` as duas correções de protótipo da **5a-2**: `0 fragmentos` → célula vazia quando `indexedAt` é nulo, e remoção da frase "documentos muito grandes tendem a bater no limite", que o sistema não verifica (design.md, D5 e D6).
- [x] 7.4 Registrar em `02-HISTORICO_E_STATUS.md` a **terceira** correção de protótipo da 5a-2, e a mais consequente: **o upload é `FileReader` + corpo JSON, não `multipart/form-data`**. O handoff pedia rota `/documents/upload`, `FormData` e modificar `request<T>` para omitir `Content-Type`; a etapa 1 fechou o contrário em **D3, "Zero multipart, com gatilho registrado"** — markdown e `.txt` são texto, o cliente lê com `FileReader` e envia string no mesmo corpo JSON. Registrar com o **motivo** (multipart obrigaria a mexer no `Content-Type` fixo de cada `request<T>`, mais `IFormFile`, mais validação de tipo binário, mais limite separado) **e o gatilho**: multipart nasce com o primeiro tipo de origem binário, junto com o extrator que precisa dos bytes. Sem esse registro a 5a-2 reabre a decisão a partir do handoff (design.md, D15).
- [x] 7.5 Registrar em `02-HISTORICO_E_STATUS.md` que a dependência declarada da **5b** já está satisfeita: `AgentResponse.KnowledgeBases` existe e é populado, e o vínculo é `PUT /agents/{id}/knowledge-bases` de conjunto inteiro — não há `PUT`/`DELETE` por base (design.md, D2 e D14).
- [x] 7.6 Registrar em `02-HISTORICO_E_STATUS.md`, em "Itens em aberto", a **lacuna dupla da spec da etapa 1**: `KnowledgeBaseResponse` expõe `createdAt` e `updatedAt`, a spec viva de `knowledge-base-catalog` não os menciona em nenhum cenário, e **nenhum teste de `apps/api` os afirma**. Anotar o risco: esta change é o primeiro requisito sobre as datas no repositório e ele está do lado da UI, então remover um dos campos do response não reprovaria nenhum teste de backend (design.md, D16, e convenção 12).
- [x] 7.7 Registrar em `02-HISTORICO_E_STATUS.md` a dívida de busca e paginação: `GET /knowledge-bases` não tem `?q=` nem paginação, e o handoff declara volume real de 100+ bases.

## 8. Fechamento

- [x] 8.1 (`apps/frontend`) `npm run lint`, `npm run build` (typecheck) e `npm test` verdes. A baseline desta change é **54 arquivos / 451 testes**, medida quatro vezes em `main` limpo — com ela verde, qualquer falha aqui **é regressão desta change**, não flake pré-existente (convenção 19, design.md D13).
- [x] 8.2 Acrescentar as quatro rodadas verdes desta change ao item de flakes de `apps/frontend` em `02-HISTORICO_E_STATUS.md` **como confirmação**, não como explicação — o item já foi ligado à correção que o resolveu (D8 e tarefa 4.6 de `2026-09-06-agente-enderecos-a2a`: prazo padrão de um segundo das consultas assíncronas, curto para dropdowns em portal sob paralelismo). Somadas às cinco daquela change, são nove execuções verdes (design.md, D13).
- [x] 8.3 Registrar a **quarta medição da série** da convenção 18: contar arquivos criados e modificados entregues contra os **23 arquivos / ~2330 linhas** projetados no `design.md`, com o diffstat **decomposto** (código separado de `openspec/`), nunca o headline do commit.
