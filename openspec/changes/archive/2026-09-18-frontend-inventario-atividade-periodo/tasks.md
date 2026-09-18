Todas as tarefas rodam em **`apps/frontend`**, exceto as de spec e de
documentação (grupos 8 e 10), que rodam na raiz do repositório. Nenhuma tarefa
toca `apps/api`, `apps/inbox` ou `apps/workers`.

## 1. Baseline

- [x] 1.1 (apps/frontend) Medir a suíte **antes de tocar em código**, com
  `npx vitest run`: número de arquivos e de testes. Referência medida na
  proposta, em 2026-09-18 sobre a `main` em `8273eab`: **82 arquivos, 874 testes,
  todos passando**. Se a medição do apply der outro número, registrar o novo e
  usá-lo como baseline. Rodar também `npm run lint` e `npm run format:check` e
  anotar o resultado.

  **Medido em 2026-09-18, branch `feat/frontend-inventario-atividade-periodo`
  sobre `8273eab`, árvore limpa:** vitest **82 arquivos / 874 testes, 874
  passando** — idêntico à referência da proposta. `npm run lint`: **0 erros, 0
  avisos**. `npm run format:check`: **3 arquivos acusados, todos pré-existentes e
  fora do escopo** — `KnowledgeBaseEditPage.test.tsx`, `KnowledgeBaseEditPage.tsx`,
  `mcp-servers/utils/agentUsage.ts`. Não são tocados aqui; o fechamento compara
  contra esses três.

## 2. Janela de atividade (função pura)

- [x] 2.1 (apps/frontend) Criar `features/inventory/utils/activityWindow.ts` com
  `lastSevenDaysWindow(now: Date): { from: string; to: string }`. `to` é
  `now.toISOString()`; `from` é `now − 7 × 24 × 60 × 60 × 1000 ms`, também em
  `toISOString()`. Sem `new Date()` dentro da função (D3). Comentário curto
  dizendo por que é rolante e não de calendário, e por que não há conversão
  manual para UTC.
- [x] 2.2 (apps/frontend) Criar `activityWindow.test.ts`, com instante fixo, sem
  fake timers:
  - `to` igual ao `now` passado;
  - `from` exatamente 604 800 000 ms antes de `to`;
  - os dois limites terminam em `Z`;
  - uma janela que atravessa a mudança de horário de verão continua com 168h.

  **Achado do apply (2.2):** o processo desta máquina roda em
  `America/Sao_Paulo`, sem horário de verão desde 2019 — um teste de virada no
  fuso local passaria sem atravessar nada. O teste usa uma virada **real e
  datada** (Nova York, 08/03/2026) e lê a hora de relógio com
  `Intl.DateTimeFormat` de fuso explícito: início às 11:00 EST, fim às 12:00
  EDT, 168h entre eles. Roda igual em qualquer fuso de processo. 5 testes,
  passando.

## 3. Clientes HTTP em `sessionsApi.ts`

- [x] 3.1 (apps/frontend) Em `features/sessions/types/session.ts`, acrescentar
  `SessionsSummary { startedCount: number }` e
  `MessagesSummary { inboundCount: number }`, com os nomes de campo do contrato
  (D4).
- [x] 3.2 (apps/frontend) Em `sessionsApi.ts`, acrescentar
  `getSessionsSummary(from, to)` → `GET /sessions/summary?from=…&to=…` e
  `getMessagesSummary(from, to)` → `GET /messages/summary?from=…&to=…`, pelo
  `request<T>` do próprio módulo, com os parâmetros codificados por
  `URLSearchParams`. Acrescentar o comentário da D4: por que uma rota
  `/messages/...` mora aqui, o que o módulo é na prática, e o gatilho para
  separar. **Não** criar `messagesApi.ts`.
- [x] 3.3 (apps/frontend) Criar `sessionsApi.test.ts` com `fetch` stubado:
  caminho e query string de cada um dos dois clientes, os dois limites
  codificados (o `+`/`:` do ISO não pode chegar cru), e erro não-2xx virando
  `ApiError`.

## 4. Hooks

- [x] 4.1 (apps/frontend) Em `useSessions.ts`, acrescentar
  `useSessionsSummaryQuery()` com chave **fixa**
  `['sessions', 'summary', 'last-7-days']` e `useMessagesSummaryQuery()` com
  `['messages', 'summary', 'last-7-days']`. A janela é calculada **dentro do
  `queryFn`** com `lastSevenDaysWindow(new Date())`, **nunca** no render e
  **nunca** na chave. Sem `refetchInterval`. Comentário apontando o modo de
  falha: chave com a janela = consulta nova a cada render = item preso em
  "Consultando…" (D3).
- [x] 4.2 (apps/frontend) Em `useSessions.test.ts`, com `vi.setSystemTime`:
  - a chave de cada hook é a fixa, sem limites dentro;
  - o `from`/`to` enviado corresponde ao relógio no instante da consulta;
  - após avançar o relógio, um `refetch()` envia uma janela **posterior** à
    primeira (último cenário do `ADDED`);
  - renders repetidos do mesmo hook **não** disparam consultas novas: contar as
    chamadas do cliente mockado.

  **Verificado no apply (4.2):** 10 testes em `useSessions.test.ts` (2
  existentes + 4 × 2 hooks), passando. **Prova por mutação:** com a janela
  calculada no render e posta na chave (`['sessions','summary', from, to]`), três
  testes reprovam — chave fixa, janela atualizada no refetch, e renders
  repetidos (a consulta dispara de novo a cada `rerender` com o relógio
  andando). Revertido; 10/10 de volta. O relógio é movido com
  `vi.useFakeTimers({ toFake: ['Date'] })` + `vi.setSystemTime`: só `Date` é
  falso, os timers do react-query continuam reais.

  **Achado do apply (4.1):** `useSessions.ts` passa a importar
  `features/inventory/utils/activityWindow.ts` — um hook de `sessions`
  dependendo de um utilitário de `inventory`. Aceito como está: o hook é
  "últimos 7 dias", que é decisão do inventário, e mover a função para
  `sessions` a separaria do único consumidor. Se um segundo consumidor da janela
  aparecer, a função sobe para um lugar comum.

## 5. Itens de atividade na `InventoryPage`

- [x] 5.1 (apps/frontend) Em `InventoryPage.tsx`, depois do item de Canais,
  escrever **inline** os itens "Sessões iniciadas" (`CirclePlay`,
  `data-testid="inventario-sessions"`) e "Mensagens recebidas" (`MessageSquare`,
  `data-testid="inventario-messages"`), na forma dos quatro existentes, com os
  textos da tabela da D5:
  - valor: número + `sessão`/`sessões`, `mensagem`/`mensagens`, com o
    substantivo inline;
  - zero medido: **Nenhuma sessão iniciada** / **Nenhuma mensagem recebida**,
    por extenso, nunca `0`;
  - não sei: `—` + "Não foi possível consultar as sessões iniciadas." /
    "…as mensagens recebidas." + **Tentar novamente**, que chama o `refetch` só
    daquele hook;
  - carregando: Skeleton + "Consultando…" em `dimmed`.
- [x] 5.2 (apps/frontend) A janela ("últimos 7 dias", `<Text size="xs" fw={500}
  c="dimmed">`, com `data-testid` próprio) fica **fora** do ternário de estado,
  para aparecer nos quatro estados por construção (D5).
- [x] 5.3 (apps/frontend) Os dois itens **sem** `Anchor` de rodapé. O `Paper`
  leva `justifyContent: 'center'`, para rótulo, contagem e janela ficarem
  centralizados juntos no card esticado pela linha.
- [x] 5.4 (apps/frontend) Atualizar o comentário do topo do arquivo: de quatro
  para seis itens; as duas apurações (catálogo pela consulta da listagem,
  atividade pela rota agregada, nunca recontada); por que a extração continua
  fora (os seis não são iguais: dois sem atalho, dois com janela, um de outro
  processo); e a referência à D3 sobre a chave fixa.
- [x] 5.5 (apps/frontend) Em `InventoryPage.test.tsx`, mock parcial de
  `sessionsApi` com `getSessionsSummary` e `getMessagesSummary`, configurados no
  `beforeEach`. Rodar os **onze testes existentes** antes de escrever os novos, e
  confirmar que continuam passando sem mudança de asserção.
- [x] 5.6 (apps/frontend) Testes novos em `InventoryPage.test.tsx`, um por
  cenário do delta:
  - valor com unidade no plural e no singular (`1 sessão`, `1 mensagem`);
  - zero medido por extenso, sem o algarismo `0` e sem o travessão;
  - não sei com a razão e a nova tentativa;
  - carregando sem número, sem travessão e sem afirmar ausência;
  - **a janela presente nos quatro estados**, nos dois itens;
  - **nenhum link** dentro dos dois itens, em nenhum estado;
  - Sessões em zero medido e Mensagens em não sei **ao mesmo tempo**;
  - um de catálogo e um de atividade em não sei, e os outros quatro com
    contagem;
  - a nova tentativa num item de atividade chama só o cliente dele: contar as
    chamadas dos seis;
  - a contagem apresentada é a da rota, e `listChannelSessions` **não** é
    chamada;
  - negativa estrutural: o item de atividade não tem texto além de rótulo,
    situação de contagem e janela.

  **Verificado no apply (5.5/5.6):** com o mock parcial de `sessionsApi` no
  lugar, os **11 testes existentes rodaram antes de a página mudar e depois**,
  sem mudança de asserção: 11/11 nas duas vezes. Testes novos: **25** (11 por
  item × 2, via `describe.each`, mais 3 de independência e apuração única) —
  36 no arquivo. Um tropeço do próprio teste, registrado: a primeira versão da
  negativa do zero medido usava `not.toMatch(/\\d/)` e reprovou por causa do
  "7" de "últimos 7 dias". Foi trocada pela igualdade do conteúdo inteiro
  (`rótulo + frase de ausência + janela`), que é mais forte. O defeito era do
  teste, não da página.

## 6. Grade com teto de quatro colunas

- [x] 6.1 (apps/frontend) Trocar o `gridTemplateColumns` de `InventoryPage.tsx`
  por `repeat(auto-fit, minmax(max(min(256px, 100%), calc((100% - 3 *
  var(--mantine-spacing-md)) / 4)), 1fr))` (D6). Reescrever o comentário da grade:
  o teto, o porquê (5 + 1 a ~1860px sem ele), os pontos de reflow medidos
  (**1328 / 1056 / 784px** de janela), e que a regra vale para os seis itens,
  inclusive os quatro de catálogo.
- [x] 6.2 (apps/frontend) Não há teste de suíte para a grade (convenção 14:
  jsdom não calcula layout). Registrar isso no comentário, em vez de escrever uma
  asserção sobre a string do CSS, que só fixaria o texto.

  **Verificado no apply (6.1):** a string do `gridTemplateColumns` foi extraída
  **por script** do bloco de código da D6 do `design.md` e colada — não
  redigitada. A regra atinge os seis itens; o comentário da grade diz isso.

## 7. `router.test.tsx`

- [x] 7.1 (apps/frontend) Acrescentar `vi.mock` **parcial** de
  `../features/sessions/api/sessionsApi` (`importOriginal`, preservando
  `request`, `ApiError` e as funções que as telas de canal chamam), substituindo
  `getSessionsSummary` e `getMessagesSummary`. Comentário ligando ao aviso já
  existente em `router.test.tsx:42-50`.
- [x] 7.2 (apps/frontend) No `beforeEach` de `describe('AppRouter')` e no
  `beforeEach` geral, dar `mockReset` e `mockResolvedValue` aos dois resumos, junto
  dos quatro catálogos. Atualizar o comentário "AS QUATRO" para seis.
- [x] 7.3 (apps/frontend) Rodar `router.test.tsx` **isolado e dentro da suíte
  inteira**, e confirmar que nenhum teste renderiza `/login` por vazamento.

  **Achado do apply (7.1–7.3):** antes do mock, com a página já chamando os dois
  resumos, `router.test.tsx` **passava 11/11** — mas só porque `apps/inbox` não
  estava de pé (porta 5027 recusando conexão): a chamada vazada falhava como erro
  de rede, sem 401, sem `clearToken()`, sem `/login`. Verde por ambiente.
  **Sonda de rede** (`fetch` trocado por um que registra e rejeita, removida
  depois): **sem** o mock, 6 chamadas escapam (3 × `/sessions/summary`, 3 ×
  `/messages/summary`) com os testes ainda verdes; **com** o mock, **0**.
  Isolado: 11/11. Suíte inteira: 84 arquivos / 920 testes, 920 passando. O
  comentário do mock registra isto.

## 8. Spec

- [x] 8.1 (raiz) Antes do archive, reconferir **cada cenário** do delta
  `specs/catalog-inventory-ui/spec.md` contra o entregue. Se a implementação
  revelar estado observável que o delta não previa, ajustar o delta, e não o
  entregue.

  **Verificado no apply (8.1):** cada um dos 23 cenários do delta tem teste que o
  exercita — os de catálogo pelos 11 testes que já existiam, os de atividade
  pelos novos, a rota raiz por `router.test.tsx`, e os dois de janela
  consultada por `useSessions.test.ts`. **Um cenário não estava coberto ao pé da
  letra:** "A nova tentativa consulta o período atualizado" fala de nova
  tentativa **depois de uma falha**, e o teste do hook fazia refetch depois de um
  sucesso. Acrescentado em `InventoryPage.test.tsx`: falha às 12:00, relógio a
  12:20, clique em Tentar novamente, e a janela enviada termina às 12:20. O delta
  não precisou de ajuste; o entregue também não.

  **8.2:** `openspec validate --all` → 53/53; os quatro cabeçalhos `MODIFIED`
  casam byte a byte com os da spec viva.
- [x] 8.2 (raiz) Conferir que os cabeçalhos dos quatro `MODIFIED` casam
  **exatamente** com os da spec viva, e rodar `openspec validate --all`.
- [x] 8.3 (raiz) **Reescrever o `## Purpose` de
  `openspec/specs/catalog-inventory-ui/spec.md` no arquivo vivo, na passada do
  sync**, com o texto da D1. A delta não carrega `Purpose`, então o sync não o
  toca. Reescrever **a frase inteira**: as duas afirmações que deixam de ser
  verdade são "um item por catálogo" e "o atalho para a listagem
  correspondente". Trocar só uma deixa a outra falsa (precedente: tasks 11.2 de
  `2026-09-13-frontend-knowledge-base-form-orientacao` e de
  `…-resumo-indexacao`).
- [x] 8.4 (raiz) Depois do sync, conferir no arquivo vivo: `## Requirements` no
  lugar, cinco requisitos sem duplicata, nenhum cabeçalho de delta vazado,
  nenhuma cópia velha sobrevivente de requisito modificado, e `Purpose` sem
  `TBD`.

  **8.3 e 8.4 ficam abertas de propósito no apply:** as duas são da passada do
  sync, que roda no archive. Escrever o `Purpose` agora deixaria a spec viva
  adiantada em relação aos requisitos dela.

  **Feito no archive (2026-09-18):** delta aplicado em
  `openspec/specs/catalog-inventory-ui/spec.md`, com os quatro blocos
  `MODIFIED` substituídos inteiros e o `ADDED` acrescentado ao final.
  `## Purpose` reescrito **inteiro** com o texto da D1, extraído por script da
  citação do `design.md`: as duas afirmações que ficavam falsas ("um item por
  catálogo", "o atalho para a listagem correspondente") saíram juntas.
  Conferido no arquivo vivo: `## Purpose` e `## Requirements` no lugar; 5
  requisitos, nenhum repetido; 0 cabeçalhos de delta vazados; 0 `TBD`; 26
  cenários (eram 15), **nenhum dos 15 antigos perdido** (comparação por título
  contra a cópia de antes do sync); `openspec validate --specs --strict` →
  52/52.

## 9. Conferência visual manual (convenção 14)

- [x] 9.1 (apps/frontend) Com o app rodando e `apps/inbox` de pé, nos **dois**
  esquemas de cor, comparar contra
  `design/Inventario Buteco Agentes.dc.html` **o interior** do item de atividade:
  hierarquia rótulo → número + substantivo → janela, a centralização vertical, e
  os quatro estados. O protótipo **não** é o alvo da grade (D8).
- [x] 9.2 (apps/frontend) **Começar por ~1860px**: 4 + 2 com itens de **389px**.
  A largura já foi aprovada para os de catálogo; o item de atividade nessa largura
  **nunca foi visto**. Conferir se o bloco centralizado não fica perdido no card
  alargado, e se a linha 2 (dois itens de atividade) não parece incompleta. Se
  reprovar, reabrir a D6 com o dado na mão, e não improvisar um `maw`.
- [x] 9.3 (apps/frontend) Cruzar os três pontos de reflow **1328 / 1056 / 784px**
  (4 → 3 → 2 → 1 coluna) e confirmar a disposição 4 + 2 → 3 + 3 → 2 + 2 + 2 →
  1 × 6.
- [x] 9.4 (apps/frontend) Estado de falha isolada, com um item de catálogo e um
  de atividade em "não sei": conferir a altura igual dentro de cada linha, e que
  o item de atividade com razão e botão não desalinha a janela.
- [x] 9.5 (apps/frontend) A conferência é **iterativa**: cada correção muda o que
  fica visível, e o passo é refeito depois dela.

  **Resultado da conferência (apply, 2026-09-18).** Método: app real (`vite`,
  CSS real do Mantine) contra um servidor de simulação local nas portas 5017/5027,
  fora do repositório, que devolve cada um dos seis itens no estado pedido
  (valor / zero / falha 500 / nunca responde). Chrome headless via DevTools
  Protocol: largura de janela **exata**, `prefers-color-scheme` emulado, captura
  de tela e medição do DOM (`getBoundingClientRect`,
  `gridTemplateColumns/Rows`) na mesma passada. 22 combinações de estado ×
  largura × esquema, mais a `main` e o protótipo para comparação.

  | janela | colunas | item | disposição | claro | escuro |
  |---|---|---|---|---|---|
  | 1860 | 4 | **389px** | 4 + 2 | ok | ok |
  | 1328 / 1327 | 4 / 3 | 256 / 346px | 4 + 2 / 3 + 3 | ok | ok |
  | 1056 / 1055 | 3 / 2 | 256 / 391,5px | 3 + 3 / 2 + 2 + 2 | ok | ok |
  | 784 / 783 | 2 / 1 | 256 / 527px | 2 + 2 + 2 / 1 × 6 | ok | ok |

  Os três pontos de reflow caem **exatamente** no pixel previsto, nos dois
  esquemas.

  **9.2 — item de atividade a 389px (nunca visto antes):** aprovado. Rótulo,
  número + substantivo e janela ficam legíveis e juntos, sem se perder no card
  alargado; a 1860px o card sai com 389 × 148px, no piso de altura. A segunda
  linha com dois itens lê como grupo próprio (catálogo em cima, atividade
  embaixo), não como linha incompleta.

  **9.4 — altura da linha com "não sei":** a equalização funciona em todas as
  larguras (todos os itens de uma linha com a mesma altura). Medido no app:
  **201,4px** (linha de catálogo com falha, a 389px), **192,5px** (linha de
  atividade com falha, a 389px), **218,2 / 209,3px** a 256px, porque a razão
  quebra em mais linhas. Os números 187/182px eram **do protótipo**, não do app.
  **Não é regressão:** a `main` sem esta change, medida no mesmo cenário, dá os
  mesmos 201,4px a 389px e 218,2px a 256px.

  **Observação 1, para decisão (não corrigida):** com o bloco centralizado (5.3),
  dois itens de atividade em estados diferentes na mesma linha ficam com o
  **rótulo em alturas diferentes**: ~32px a 1860px, com Sessões em zero medido ao
  lado de Mensagens em não sei. Os itens de catálogo não têm isso, porque o
  rodapé com `margin-top:auto` ancora o rótulo no topo. **O protótipo aprovado
  faz o mesmo**: renderizado e medido, cenário B com rótulos em 86px e 115px, e o
  cenário misto em 116px e 83px. A implementação reproduz o alvo. Fica registrado
  porque a 1056–1327px (3 + 3) Canais divide a segunda linha com os dois de
  atividade e o desalinhamento aparece ao lado de um item de catálogo. Se for
  reprovado, a saída é ancorar o rótulo no topo e centralizar só contagem +
  janela, o que é mudança da D5 e não ajuste de implementação.

  **Observação 2 (preexistente, não desta change):** o "não sei" leva ~7s para
  aparecer, porque o `QueryClient` do app (`app/queryClient.ts`) usa o
  `retry: 3` padrão do react-query, com espera crescente. Vale igual para os
  seis itens; a conferência precisou esperar 9s por cenário de falha.

  Capturas: `$CLAUDE_JOB_DIR/tmp/conferencia/` (efêmero, 26 PNGs). Não
  anexadas ao repositório.

## 10. Fechamento

- [x] 10.1 (apps/frontend) Rodar a suíte inteira e comparar **número contra
  número** com a baseline da 1.1: arquivos e testes antes e depois, com a
  diferença explicada pelos testes acrescentados. Rodar `npm run lint`,
  `npm run format:check` e o build.
- [x] 10.2 (raiz) `CHANGELOG.md`: ler a entrada da change anterior antes de
  acrescentar, e não só acrescentar.
- [x] 10.3 (raiz) `02-HISTORICO_E_STATUS.md`: registrar que o D10 de
  `frontend-inventario-catalogos` foi fechado pelas três changes (duas de backend
  e esta), e que a reserva do nome `dashboard-*` **não** foi usada, com a razão
  da D1.

  **Fechamento medido (10.1), número contra número:**

  | | baseline (1.1) | fechamento | diferença |
  |---|---|---|---|
  | arquivos de teste | 82 | **84** | +2 (`activityWindow.test.ts`, `sessionsApi.test.ts`) |
  | testes | 874 | **921** | +47 = 5 (janela) + 8 (clientes) + 8 (hooks) + 26 (página) |
  | passando | 874 | **921** | nenhuma falha |
  | `lint` | 0 | **0** | — |
  | `format:check` | 3 preexistentes | **os mesmos 3** | nenhum arquivo desta change |
  | `build` | — | **ok** | aviso de chunk > 500 kB (bundle único de ~935 kB), anterior a esta change e registrado no histórico |

  `router.test.tsx` continua com 11 testes: o mock mudou, os testes não.
