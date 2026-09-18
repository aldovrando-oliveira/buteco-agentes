# Tarefas

Ordenadas por dependência, não por arquivo: a página precisa existir antes que a
árvore de rotas aponte para ela; a rota precisa existir antes que o item de
navegação possa ser testado como ativo; e a raiz precisa redirecionar antes que
mudar o destino do login signifique alguma coisa.

Cada arquivo de produção vem com o teste dele no mesmo par (convenção 18: todo
arquivo modificado arrasta o teste dele).

## 1. A tela

- [x] 1.1 (apps/frontend) Criar `src/features/inventory/pages/InventoryPage.tsx`:
  um item por catálogo — agentes, servidores MCP, bases de conhecimento e canais
  —, cada um com rótulo, situação de contagem e atalho para a listagem.
  Consumir `useAgentsQuery`, `useMcpServersQuery`, `useKnowledgeBasesQuery` e
  `useChannelsQuery` — os hooks existentes, com as chaves de cache que as
  listagens já usam. **Nenhuma consulta nova, nenhuma rota nova de backend** (D3,
  D6).
  - Os quatro itens escritos **inline**, sem extrair componente compartilhado
    (D4). Contar as cópias depois de escritas; extrair só se forem mesmo iguais.
  - Sem frase explicativa em nenhum item (D3).
  - **Sem portão de carregamento combinado**: cada item carrega o próprio
    `isLoading` e o próprio `isError`. Não repetir o
    `isLoading || agentsQuery.isLoading` de `ChannelListPage.tsx:14` (D6).
  - Os quatro estados de proveniência por item, com o zero medido dito em
    palavras e a indisponibilidade dita com a razão — molde de
    `KnowledgeBaseAgentsCard.tsx:17-27` e da gramática de
    `knowledge-bases/utils/indexingSummary.ts:3-26` (D6).
  - Nova tentativa por item, refazendo **só** a consulta daquele catálogo:
    `refetch()` daquela query, ou `refetchQueries({ queryKey: [...] })`.
    **Nunca `invalidateQueries()` sem chave** (D5).
  - Texto de consulta em andamento com `c="dimmed"`, **não** com o tom `--mut2`
    do protótipo: `gray[5]`/`dark[3]` dão 3,21:1 e 4,41:1, abaixo do mínimo de
    4,5:1 que o próprio tema exige (`theme.ts:168-171`) — D8, D9.
  - Grade de itens em `auto-fit` com mínimo de 256px e gap de 16px; altura
    consistente por linha vindo do `stretch` da grade, com piso de 148px (D4, D7).
  - Ícone de `lucide-react`, como todo o painel.
  - **Contrato de `data-testid`, exigido pelo teste de 2.3** (que cruza duas
    telas e por isso não pode depender de texto de nenhuma delas): cada item
    expõe `inventario-<chave>` no seu elemento raiz, e o número — quando há
    número — em `inventario-<chave>-contagem`, **sozinho, sem a unidade junto**.
    Chaves: `agents`, `mcp-servers`, `knowledge-bases`, `channels`.
- [x] 1.2 (apps/frontend) Criar `src/features/inventory/pages/InventoryPage.test.tsx`
  cobrindo os cenários do delta de `catalog-inventory-ui`:
  - cada catálogo aparece uma vez com a sua contagem, e o atalho aponta para a
    rota da listagem;
  - catálogo vazio afirma ausência e **não** exibe o símbolo de desconhecido;
  - consulta que não respondeu **não** vira zero nem ausência, e diz a razão;
  - consulta em andamento não afirma contagem nem indisponibilidade;
  - falha de um catálogo não apaga os outros três;
  - consulta lenta de um catálogo não segura os que já responderam;
  - a nova tentativa refaz só a consulta daquele catálogo (asserção sobre a
    chave usada, não sobre o número de chamadas globais);
  - a nova tentativa em andamento troca o desconhecido por consulta em andamento;
  - **negativa estrutural**: nenhum item reproduz o texto explicativo de
    listagem alguma — asserção que **não** fixa o teor atual de nenhum subtítulo
    (D3).
  - Cada negativa em `it()` próprio, nunca como cláusula dentro de um teste
    positivo (convenção 18).

## 2. A rota e a entrada

- [x] 2.1 (apps/frontend) Atualizar `src/app/routes.tsx`: acrescentar a rota
  `inventory` com `<InventoryPage/>` dentro do `AppShell`, e trocar o
  `<Route index element={<Navigate to="/agents" replace />} />` da linha 38 por
  `<Navigate to="/inventory" replace />` (D1).
- [x] 2.2 (apps/frontend) Atualizar `src/app/router.test.tsx`:
  - a asserção de `:127-131` ("redireciona a rota raiz ... sem exibir página
    vazia") muda de **alvo**: passa a esperar o inventário, mantendo a forma;
  - os casos de `:133-151` e `:153-175`, que partem de `/` esperando o heading
    "Agentes", passam a atravessar o inventário antes;
  - **configurar as quatro consultas no `beforeEach` de `describe('AppRouter')`
    (`:213-222`)**, que hoje reseta só `listAgents`. Com a raiz indo para o
    inventário, o smoke test passaria a depender de `mockResolvedValue` vazado do
    `describe` anterior — `vite.config.ts` não liga `clearMocks` (ver Riscos do
    `design.md`).
- [x] 2.3 (apps/frontend) Acrescentar a `src/app/router.test.tsx` o caso que
  cobre o cenário **"A contagem do inventário e a da listagem não divergem"**.
  Mora aqui, e não em `InventoryPage.test.tsx`, porque o cenário **cruza duas
  páginas**: só é observável renderizando a árvore de rotas a partir de `/`,
  lendo a contagem no item do inventário, navegando pelo atalho e lendo a
  contagem na listagem — no **mesmo** `QueryClient`, sem reset de mock entre as
  duas leituras.

  Um catálogo como representante (agentes), não os quatro: o que o cenário
  afirma é o acordo entre as duas superfícies, e ele tem a mesma forma nos
  quatro.

  ```tsx
  it('a contagem do inventário e a da listagem de agentes não divergem', async () => {
    const user = userEvent.setup();
    vi.mocked(listAgents).mockResolvedValue([
      { ...agent, id: '11111111-1111-1111-1111-111111111111', name: 'Atendente' },
      { ...agent, id: '22222222-2222-2222-2222-222222222222', name: 'Cobrança' },
      { ...agent, id: '33333333-3333-3333-3333-333333333333', name: 'Triagem' },
    ]);

    renderRoutesFrom('/');

    // A contagem é LIDA do inventário, não escrita aqui. Fixar o número no
    // teste provaria outra coisa — que o mock tem três agentes —, e é o acordo
    // entre as duas telas que este caso existe para provar.
    // Espera pelo NÚMERO, não pelo card: o card renderiza de imediato, com o
    // esqueleto de carregamento, e só depois ganha a contagem.
    const contagem = (
      await screen.findByTestId('inventario-agents-contagem')
    ).textContent?.trim();
    const item = screen.getByTestId('inventario-agents');

    // Guarda do próprio teste: sem ela, uma contagem vazia faria a asserção
    // final virar /  agentes cadastrados/ e casar por acidente.
    //
    // Checa que é UM número, nunca QUAL número: prender ao valor mockado faria
    // este caso reprovar ao mudar a fixture, por um motivo que não é o que ele
    // prova. O número certo quem confere é a asserção final, contra a listagem.
    expect(contagem).toMatch(/^\d+$/);

    await user.click(within(item).getByRole('link', { name: 'Ver agentes' }));

    expect(await screen.findByRole('heading', { name: 'Agentes' })).toBeInTheDocument();
    // Mesmo idioma de asserção de AgentListPage.test.tsx:126, com o número
    // vindo do card e não do teste.
    expect(screen.getByText(new RegExp(`${contagem} agentes cadastrados`))).toBeInTheDocument();
  });
  ```

  **O que este caso pega, e nenhum outro mecanismo pega.** Hoje o acordo é
  garantido por construção — mesma chave de cache, mesmo `QueryClient`,
  `staleTime` 0 (D3) —, e uma contagem vinda de uma **função** nova escaparia
  para a rede e derrubaria o teste seguinte pelo mecanismo já registrado em
  `router.test.tsx:42-50`. O que escaparia dos dois é uma divergência **dentro
  da mesma consulta**: um `select` no inventário filtrando o array (só agentes
  ativos, por exemplo). Não bate na rede, não muda a chave, e faz as duas telas
  dizerem números diferentes em silêncio. É o que o requisito de spec proíbe ao
  dizer que a contagem SHALL ser derivada da mesma consulta que alimenta a
  listagem.

## 3. A navegação

- [x] 3.1 (apps/frontend) Atualizar `src/components/layout/AppShell.tsx`:
  acrescentar `{ to: '/inventory', label: 'Inventário', icon: ... }` como
  **primeiro** item do array `navItems` (`:14-23`), com ícone do `lucide-react`.
  O item aponta para `/inventory` e **não** para `/`, porque
  `startsWith('/')` na linha 65 deixaria o item permanentemente ativo em toda
  rota do painel (D1).
- [x] 3.2 (apps/frontend) Atualizar `src/components/layout/AppShell.test.tsx`:
  - `:41-53` — o item "Inventário" entra na lista; **a asserção
    `queryByText('Dashboard')` de `:51` inverte**, virando a afirmação positiva
    de que o item existe e aponta para `/inventory`. A asserção de "Inboxes"
    ausente permanece: aquele item continua sem página;
  - `:113` — `container.querySelectorAll('a svg')` passa de `4` para `5`;
  - `:105` e `:132` — os dois arrays literais de rótulos ganham "Inventário";
  - `:116-137` — a tabela `it.each` de item ativo ganha a linha de `/inventory`,
    e o laço de "outros" passa a cobrir cinco rótulos.

## 4. O caminho de entrada dominante

- [x] 4.1 (apps/frontend) Atualizar `src/features/auth/pages/LoginPage.tsx:20`:
  `navigate('/agents', { replace: true })` passa a `navigate('/', { replace: true })`.
  Para `/` e não para `/inventory`: a definição de qual é a entrada fica só em
  `routes.tsx` (D2).
- [x] 4.2 (apps/frontend) Atualizar `src/features/auth/pages/LoginPage.test.tsx:88`:
  a asserção `toHaveBeenCalledWith('/agents', { replace: true })` passa a esperar
  `'/'`.

## 5. Verificação

- [x] 5.1 (apps/frontend) Rodar a suíte inteira e comparar **número contra
  número** com a baseline anterior à change — não "passou/não passou"
  (convenção 19). `AgentDeactivationTests` de `apps/api` reprova em classe e passa
  isolada; é falha pré-existente registrada, não regressão desta change, e não é
  tocada aqui.
- [x] 5.2 (apps/frontend) **Conferência visual manual, tarefa própria**
  (convenção 14 — a suíte roda em jsdom e não enxerga grade nem contraste), nos
  **dois** esquemas de cor, com a janela cruzando os três pontos de reflow
  calculados para o cromo real do app (barra de 224px + 2 × 16px de `padding="md"`):
  - **1328px** — quatro colunas acima, três abaixo;
  - **1056px** — três colunas acima, duas abaixo;
  - **784px** — duas colunas acima, uma abaixo. Esta faixa cai abaixo do `sm` do
    Mantine, que é o `breakpoint` declarado em `AppShell.tsx:41`; o comportamento
    da casca ali é o registrado na D4 da change da casca e **não foi reconferido**
    na fase de proposta.
  - **~1860px de janela — a largura de trabalho real deste projeto, e a única do
    checklist que nunca foi vista.** D7 recusa o `max-width:1320px` do protótipo,
    então acima de ~1596px os itens deixam de ter largura fixa e crescem: a
    1860px de janela o cálculo dá **~389px** por item. As três rodadas de
    protótipo tinham o teto de 1320px embutido, e a medição headless da proposta
    ficou presa a ele — mediu 1320px de grade mesmo com 1624px de janela.
    Conferir, **nos dois esquemas**, se o item alargado mantém proporção
    aceitável: número e rótulo não perdidos num retângulo achatado, e o atalho
    no rodapé do card sem parecer solto. Se reprovar, a saída é reabrir D7 com o
    dado na mão — não improvisar um `maw` na implementação.
  - Conferir também o estado de falha isolada (dois itens com contagem
    desconhecida ao lado de dois com número) e a altura consistente da linha.
  - A conferência é **iterativa**: cada correção muda o que fica visível.

## 6. Spec

- [x] 6.1 (apps/frontend) O delta de `catalog-inventory-ui` já está escrito em
  `openspec/changes/frontend-inventario-catalogos/specs/catalog-inventory-ui/spec.md`.
  Antes do archive, reconferir **cada cenário contra o que foi entregue** — e
  ajustar o delta se a implementação tiver revelado estado observável que ele não
  previa, em vez de ajustar o entregue para caber no delta.
- [x] 6.2 Confirmar que **nenhuma** spec viva precisou de `MODIFIED`:
  `frontend-scaffold` (`:79-80`, `:88-90`), `operator-login-ui` (`:12-13`,
  `:20-21`) e `frontend-app-shell` (`:33`) foram lidas e já estão satisfeitas
  pelo texto que têm. Se a implementação divergir disso, é achado a reportar.
