## Context

A rota raiz do painel redireciona para `/agents` desde o primeiro corte de
interface (`apps/frontend/src/app/routes.tsx:38`). Não existe tela de entrada.

A navegação teve "Dashboard" e "Inboxes" removidos em 2026-07-27, e o motivo está
registrado em
`openspec/changes/archive/2026-07-27-frontend-cadastro-agentes/design.md:146-153`:

> `AppShell.tsx` perde `Dashboard` e `Inboxes` do array de nav — **não existe
> página nenhuma para eles** [...] é mais simples reintroduzi-los quando a página
> existir de fato.

O motivo não era "não há o que mostrar", era "não há página" — e a própria decisão
escreve a condição de reabertura. A recusa está fixada em teste
(`AppShell.test.tsx:51-52`), mas o teste fixa o **exemplo** de 2026-07-27; a
**regra** está na spec viva `frontend-app-shell` (`spec.md:33`), que exige um item
para cada área com página real. Criar a página satisfaz a regra sem alterá-la.

As quatro contagens que esta tela exibe já existem no painel, cada uma no
subtítulo da sua listagem. A tela não apura nada novo: ela consulta os quatro
catálogos ao mesmo tempo e mostra o estado de cada consulta.

O protótipo desta tela é `Inventario Buteco Agentes.dc.html`, no projeto Claude
Design "Sistema Gestão de Agentes"
(https://claude.ai/design/p/e4f9bbd6-dd31-4ff1-954b-0e686d2eaa54). Foi lido na
íntegra e percorrido no navegador; os números deste documento saem da leitura do
CSS e da medição no motor de layout, não da prosa do handoff.

## Goals / Non-Goals

**Goals:**
- Uma tela que responda "o que existe cadastrado, e as quatro consultas
  responderam?" numa olhada.
- Proveniência correta de cada contagem: os quatro estados separados, nenhum
  colapsado no outro.
- Falha e recarga independentes por catálogo.
- Ser o destino da entrada, pelos dois caminhos (raiz e login), com **uma**
  definição de qual é a entrada.

**Non-Goals:**
- Qualquer quadro de atividade — mensagens processadas, sessões ativas, sessões
  por período. Ver D10.
- Armazenamento consolidado de dados agregados. Ver D10.
- Rota nova em `apps/api` ou `apps/inbox`. As quatro consultas já existem.
- Busca, filtro ou ordenação na tela de inventário.
- Card "Primeiros passos" do protótipo original, adiado por decisão de 2026-09-05
  e registrado em `02-HISTORICO_E_STATUS.md:4808-4809`.

## Decisions

### D1 — `/inventory`, rótulo "Inventário", e a raiz redireciona para lá

Rota e rótulo vêm do protótipo, que os declara como propriedades editáveis com
esses defaults, e o item de navegação como primeiro da lista:

```js
{ rota: { default: '/inventory' }, rotulo: { default: 'Inventário' } }
const NAV = [ { icon:'layout-grid', label:'Inventário', route:'/inventory', active:true }, ... ]
```

É a única opção prototipada, e é adotada sem alternativas a registrar.

**A raiz redireciona para `/inventory`; ela não *é* o inventário.** O motivo é de
código, não de gosto. `AppShell.tsx:65` calcula o item ativo assim:

```tsx
active={location.pathname.startsWith(to)}
```

Um item com `to="/"` ficaria **permanentemente ativo** em toda rota do painel —
`startsWith('/')` é sempre verdadeiro. Com `/inventory` a regra existente continua
valendo sem caso especial, e a asserção de `router.test.tsx:127` ("redireciona a
rota raiz ... sem exibir página vazia") **muda de alvo, não de forma**: continua
provando a mesma propriedade.

### D2 — O login navega para `/`, não para `/inventory`

`features/auth/pages/LoginPage.tsx:20` hoje é `navigate('/agents', { replace: true })`,
com a asserção correspondente em `LoginPage.test.tsx:88`. Passa a `navigate('/')`.

**Para `/` e não para `/inventory`: um único lugar define qual é a entrada**, que
é `routes.tsx`. Duas constantes apontando para o mesmo destino é o defeito que D3
recusa na prosa, aplicado à navegação.

**Sem esta mudança, D1 funcionaria pela metade — e na metade errada.** O caminho
do login é o dominante, porque `ProtectedRoute.tsx:4-9` redireciona para `/login`
**sem preservar a rota tentada** (não há `state: { from }`), e o tratamento de 401
força esse caminho a partir de qualquer tela, em cinco módulos de API:

```
agentsApi.ts · channelsApi.ts · knowledgeBasesApi.ts · mcpServersApi.ts · sessionsApi.ts
   └─ 401 → clearToken() → window.location.href = '/login'
```

Toda expiração de token terminaria em `/agents`, e a tela nova só seria vista por
quem digita a URL raiz.

Nenhuma spec muda: `operator-login-ui` já diz "a página inicial autenticada"
(`spec.md:20-21`), e volta a ser verdadeira sozinha.

### D3 — O card não carrega prosa, e a negativa que protege isso é estrutural

As quatro consultas do inventário usam **as mesmas chaves de cache** das
listagens:

| catálogo | hook | chave | também consumido por |
|---|---|---|---|
| agentes | `useAgents.ts:20-22` | `['agents']` | `AgentListPage.tsx:40` e mais três telas |
| servidores | `useMcpServers.ts:25-26` | `['mcp-servers']` | `McpServerListPage.tsx:10` |
| bases | `useKnowledgeBases.ts:21-22` | `['knowledge-bases']` | `KnowledgeBaseListPage.tsx:67` |
| canais | `useChannels.ts:13` | `['channels']` | `ChannelListPage.tsx:12` |

E `app/queryClient.ts:3` é `new QueryClient()` sem `defaultOptions` — `staleTime`
é 0, então navegar do inventário para uma listagem **renderiza o array cacheado no
mesmo tick** e refaz a consulta em segundo plano.

Consequência: **o número não pode divergir do subtítulo da listagem**, porque não
são duas cópias — são dois leitores da mesma entrada de cache. Quando o refetch
traz outro número, o card já está desmontado.

**A frase, essa sim, seria cópia.** Cada listagem tem um subtítulo escrito à mão
sobre o que aquele catálogo faz. Reproduzi-lo no card criaria uma quinta
superfície de texto que envelhece em silêncio — a última forma da convenção 13, a
afirmação verdadeira quando escrita que outra etapa torna falsa. `array.length`
não envelhece; prosa sim.

**Decisão: o card carrega rótulo, contagem (ou um dos outros três estados de D6) e
o atalho. Nenhuma frase explicativa.** Quem quer a explicação está a um clique
dela, e a listagem de destino já a dá.

**A negativa que protege isso é estrutural, nunca verbatim:** "o card não
reproduz a frase de nenhuma listagem". Escrever a asserção contra o texto atual de
um subtítulo faria do teste uma segunda fonte de verdade daquele texto — o
subtítulo mudaria e o teste reprovaria por ter decorado a versão antiga, ou pior,
alguém atualizaria o teste e a cópia passaria a existir em dois lugares. O modo de
falha real é alguém acrescentar prosa depois "para ficar igual à lista", e a forma
estrutural o pega sem conhecer o texto.

### D4 — Quatro cards escritos inline, com altura consistente por linha

Convenção 2: o gatilho de extração é repetição **já observada**, nunca prevista.
Os quatro cards **não são cópias idênticas** — o de Canais fala com outro
processo, em outra porta, por outro `request<T>` (`channelsApi.ts:26`,
`INBOX_BASE_URL`), com outro modo de falha. Extrair antes de escrever é prever a
forma da repetição e esconder justamente a diferença que mais importa.

Escrever os quatro inline. Contar depois. Extrair se forem mesmo iguais — e aí a
extração é barata, com `SectionedCard` já dando a moldura.

**Altura consistente entre cards da mesma linha — achado do Claude Design, não
pedido nosso.** O protótipo resolve por `min-height:148px` como piso e deixa a
linha da grade esticar todos até o mais alto, que é o comportamento padrão de
`align-items: stretch`. Medido no motor, no cenário de falha isolada: os quatro
cards da linha saem com **187px**, incluindo os dois que só mostram número. É o
que impede a linha de ficar serrilhada quando um card ganha motivo e botão de nova
tentativa e o vizinho não.

### D5 — Nova tentativa por card, refazendo só a consulta daquele catálogo

Lido no protótipo:

```js
onRetry: () => this.retry(c.key),

retry(key) {
  this.setState(s => ({ scenario: null, states: Object.assign({}, s.states, { [key]: 'loading' }) }));
  setTimeout(() => this.setState(s => (
    s.states[key] === 'loading' ? { states: Object.assign({}, s.states, { [key]: 'value' }) } : null
  )), 1400);
}
```

**Escopo: (a) — só o catálogo daquele card.** `{ [key]: ... }` toca exatamente uma
entrada, e `Object.assign({}, s.states, ...)` carrega as outras três inalteradas.

**Ressalva que muda o que isso significa para a implementação: o protótipo não faz
requisição nenhuma.** É uma máquina de estados fixa com `setTimeout` de 1400ms
virando `'loading'` em `'value'`. O que ele confirma é a **intenção de interação**
— isolada por card, com estado de consulta em andamento no meio, e o número
voltando ao fim —, não comportamento de rede.

Na implementação real isso é o `refetch()` daquela `useQuery`, ou
`queryClient.refetchQueries({ queryKey: [...] })` com a chave do catálogo.
**Nunca `invalidateQueries()` sem chave**, que refaria as quatro e contradiria o
escopo confirmado.

Vale carregar também a guarda do protótipo: o `setTimeout` só aplica o resultado
`s.states[key] === 'loading' ? ... : null`, isto é, não sobrescreve se o estado
daquele card mudou nesse meio-tempo. O análogo real é não aplicar resposta de uma
consulta que já não é a corrente — que o TanStack Query já faz por chave, e é mais
uma razão para a nova tentativa ser por chave e não global.

### D6 — Os quatro estados de proveniência

A régua é a proveniência do dado, nunca o tipo do campo (convenção 13,
`01-ARQUITETURA_E_CONVENCOES.md:805`). Os quatro convivem nesta tela, por catálogo:

| estado | de onde vem | como a tela diz |
|---|---|---|
| valor | a consulta respondeu, `length > 0` | `4` + a unidade |
| **medição que deu zero** | a consulta respondeu, `length === 0` | `Nenhum agente` — **nunca `0`** |
| **medição que não aconteceu** | a consulta falhou, ou não há dado | `—`, **com o motivo**, e nova tentativa |
| consulta em andamento | carregando | nem número, nem travessão |

**O zero aqui é medido.** A resposta chegou, é `[]`, e "não há" é verdade — é o
zero da coluna da esquerda da tabela da convenção 13, o mesmo de `documentCount`
no resumo de indexação. Registrado junto do campo, como a convenção manda, porque
é a frase que impede a etapa seguinte de "consertar" a exibição.

**Nenhuma das quatro rotas pagina** — conferido nos handlers
(`AgentEndpoints.cs:53`, `McpServerEndpoints.cs:60`, `KnowledgeBaseEndpoints.cs:64`,
`ChannelEndpoints.cs:56`): as quatro devolvem a coleção inteira em
`Ok<IReadOnlyList<...>>`, sem parâmetro. Então `array.length` **é** a contagem, e é
completa. Se alguma paginasse, o número seria uma afirmação falsa com cara de
número, que é o defeito central desta tela.

**Reuso, não reinvenção.** Os dois moldes já existem:

- `KnowledgeBaseAgentsCard.tsx:17-27` — catálogo indisponível: o card não afirma
  nada e **diz o motivo**, com `data-testid` próprio.
- `knowledge-bases/utils/indexingSummary.ts:3-26` — a gramática de três estados,
  com a frase que vale copiar: *"Devolver `null` daqui significa SEMPRE 'não sei';
  quem renderiza traduz em `—`"*.

**E o que NÃO reusar: o portão de carregamento combinado.**
`ChannelListPage.tsx:14` faz `const loading = isLoading || agentsQuery.isLoading`,
e só reporta o erro de uma das duas origens — a outra degrada em silêncio para
lista vazia. Numa listagem é um incômodo; numa tela cujo conteúdo são quatro
números, é a convenção 13 quebrada. **Cada card carrega o próprio estado de
carregamento e o próprio estado de erro.** Um `apps/inbox` lento não segura três
cards prontos atrás de um `<Loader/>` compartilhado.

### D7 — Dimensões e reflow: os valores do app, não os do protótipo

**Não existe `@media` no protótipo.** As únicas regras condicionais no documento
são `print` e um `@container (max-width: 719px)` que encolhe a barra lateral para
um rail de 58px. **O rail não tem contraparte no app**: a change da casca o
descartou explicitamente (D4 de
`openspec/changes/archive/2026-09-06-frontend-shell-navegacao-e-icones/design.md`),
o painel é de desktop, e a barra é fixa em 224px.

A grade de cards é declarada assim, sem breakpoint nenhum:

```css
display:grid; grid-template-columns:repeat(auto-fit,minmax(min(256px,100%),1fr)); gap:16px
```

O reflow é **emergente do `auto-fit`**, não declarado. Medido no motor de layout,
variando a largura disponível para a grade:

| grade disponível | colunas | template resolvido |
|---|---|---|
| 1344px | 4 (+1 faixa colapsada) | `324px ×4 + 0px` |
| **1320px** | **4** | **`318px ×4`** |
| 1072px | 4 | `256px ×4` |
| **1071px** | **3** | `346px ×3` |
| 800px | 3 | `256px ×3` |
| **799px** | **2** | `391px ×2` |

Aritmética que gera isso: `n` colunas exigem `256n + 16(n−1)` de grade — 1072px
para quatro, 800px para três, 528px para duas.

**O número declarado pelo Design bate — pela via certa.** O protótipo tem barra de
224px, `padding:22px 26px 40px` no `<main>` (52px horizontais) e um invólucro de
`max-width:1320px`. Cromo = 276px, logo quatro colunas a partir de **1348px de
janela**, e cards de 318px quando o invólucro satura — `(1320 − 48)/4 = 318`. Os
dois números do handoff conferem. O que **não** confere é a expectativa de achar
um `@media 1348px` no arquivo: ele não existe, e quem for procurá-lo não acha.

**No app real o cromo é outro.** `AppShell.tsx:41` usa `navbar={{ width: 224 }}` —
igual ao protótipo — e `padding="md"`. `theme.ts` não sobrescreve `spacing`, então
`md` é o default do Mantine, `1rem` = **16px**. Cromo real = 224 + 2×16 = **256px**:

```
                      protótipo        app real
barra lateral            224px           224px     (igual)
padding horizontal    2 × 26px        2 × 16px     divergente
invólucro máximo        1320px          nenhum     divergente
cromo total              276px           256px
4 colunas a partir de   1348px          1328px  ← o que vai para o tasks.md
3 colunas a partir de   1076px          1056px
2 colunas a partir de    804px           784px
```

**O invólucro de 1320px não é adotado.** Nenhuma página do painel tem container de
largura máxima — `maw` aparece só em campo de busca (`340`) e em prosa de estado
vazio (`520`). Introduzir um container de página para uma tela só seria padrão
novo sem o segundo consumidor que a convenção 2 exige. Sem ele os cards crescem
além de 318px em telas largas (medido: 325px com 1360px de grade), o que é largura,
não defeito — `auto-fit` nunca quebra, só alarga.

**Nada disto vira requisito de spec.** Pixel e breakpoint são design; o que a spec
fixa é comportamento observável do sistema.

### D8 — Contraste: calculado, não citado

O handoff declarou 6,5:1 e 8,4:1 para o texto do estado "não sei". Recalculado
pela fórmula de luminância relativa do WCAG, sobre os hex lidos no `:root` e no
`[data-scheme="dark"]` do protótipo:

| | tokens | **calculado** | declarado | AA 4,5:1 |
|---|---|---|---|---|
| claro — motivo da falha | `--wa #8a5a00` sobre `--sf #ffffff` | **5,93:1** | 6,5:1 | passa |
| escuro — motivo da falha | `--wa #e2a63f` sobre `--sf #1b1d21` | **7,84:1** | 8,4:1 | passa |
| claro — travessão | `--mut #686d74` sobre `--sf` | 5,21:1 | — | passa |
| escuro — travessão | `--mut #9aa0a8` sobre `--sf` | 6,40:1 | — | passa |

Os dois declarados estão **acima** do real, por ~0,6. Nenhum muda de veredito: os
quatro passam AA, e o do claro não chega a AAA. Os valores reais são os de cima.

**E a medição achou um que o handoff não declarou.** O protótipo põe o texto
"Consultando…" em `--mut2`:

| | tokens | calculado | AA 4,5:1 |
|---|---|---|---|
| claro — "Consultando…" | `--mut2 #8b9097` sobre `--sf #ffffff` | **3,21:1** | **reprova** |
| escuro — "Consultando…" | `--mut2 #7d838b` sobre `--sf #1b1d21` | **4,41:1** | **reprova** |

Esse token existe no tema do app — é `gray[5]` e `dark[3]`, com os mesmos hex
(`theme.ts:90`, `theme.ts:104`) — e **nunca é usado como texto** em nenhuma tela
hoje (varredura por `gray[5]`/`dark[3]`: zero ocorrências fora dos comentários da
escala). O tema declara o mínimo que este projeto exige no comentário do Badge,
`theme.ts:168-171`: *"em verde dá 3,89:1 e em âmbar 3,72:1, que é abaixo do mínimo
de 4,5:1 que este mesmo tema exige"*.

**Decisão: o texto de consulta em andamento usa `c="dimmed"`**, que resolve para
`gray[6]`/`dark[2]` — os tons que o próprio tema documenta como o que `dimmed` lê
(`theme.ts:95` e `:107`) — e dá 5,21:1 e 6,40:1. Ver D9.

### D9 — Divergências do protótipo, registradas (convenção 17)

O redesenho já registrou cinco (`01-ARQUITETURA_E_CONVENCOES.md:976-981`). Esta
change acrescenta duas.

**Sexta — o protótipo original não tem tela de entrada, e isso é escolha dele.**
Conferido nas quatro revisões do handoff, por leitura e por navegação:
`README-painel.md:58-72` declara o mapa de rotas como *"Mesmo mapa de rotas de
hoje, menos uma"*, sem raiz; a barra lateral tem quatro itens
(`README-painel.md:95-96`); `grep -i dashboard` no `.dc.html` dá zero em todas as
revisões; e percorrido no navegador, o protótipo **abre em `/agents`** e o DOM
confirma os quatro itens. A resposta dele para "quem chega sem nada cadastrado" é
o card *Primeiros passos* dentro de `/agents` (`README-painel.md:109-118`), adiado
por decisão de 2026-09-05; para "o que existe cadastrado aqui" ele não responde em
lugar nenhum.

Esta change **diverge disso** — acrescenta uma tela que o protótipo original não
tem —, e a razão é a do `proposal.md`: o sinal de saúde por catálogo não existe em
nenhuma tela. O protótipo de inventário lido aqui é posterior e foi feito para
esta change; ele não contradiz o original, preenche o vazio dele.

**Sétima — o tom `--mut2` do texto de consulta em andamento reprova o contraste
mínimo que a própria identidade visual exige.** Medido em D8: 3,21:1 no claro.
É exatamente a quinta divergência do redesenho repetindo a forma — *"um tom de
rótulo que reprova no contraste mínimo que a própria identidade visual exige"* —
e resolve do mesmo jeito: regra que o sistema já escreveu vence protótipo. A tela
usa `c="dimmed"`.

### D10 — O que fica fora, e o que cada um exige

Conferido no código, não por julgamento. O único `MapGet` agregado em todo o
monorepo é `/knowledge-bases/indexing-summary`.

**Mensagens processadas.** Nenhuma rota conta. E o termo tem três referentes nesta
base — recebidas; as que geraram resposta
(`MessageDispatchStatus.Completed`, `MessageDispatchStatus.cs:16`, ao lado de
`Pending`/`Dispatching`/`Failed`); tarefas executadas em `apps/api`. **Definir
qual é trabalho de backend, não da tela**, e precede qualquer medição: a convenção
6 registra que a pergunta errada é o modo de falha mais caro, porque o número sai
certo respondendo ao que não decide.

**Sessões ativas.** Confirmado nos records, não no item aberto:

```
SessionResponse.cs:21-27        (..., LastActivityAt, ClosedAt?)   ← GET /contacts/{id}/sessions
ChannelSessionResponse.cs:5-10  (..., LastActivityAt)              ← GET /channels/{id}/sessions
```

O item aberto de `02-HISTORICO_E_STATUS.md:4305-4311` **disparou**. Ressalva de
precisão: o gatilho escrito lá é *"a lista de sessões do frontend precisar
distinguir conversa viva de conversa encerrada"*, e um card de contagem é **outro
consumidor** da mesma lacuna — registrar como segunda ocorrência do mesmo gatilho,
não como o gatilho previsto batendo.

**Sessões por período.** Nenhuma rota filtra por intervalo.

**Armazenamento consolidado de dados agregados — fora, com gatilho e bar escritos
antes de medir** (convenção 22, que exige o estado ao lado do número e um gatilho
observável):

> Consolidação só entra se a consulta direta agregada — **no molde de
> `GetKnowledgeBaseIndexingSummaryQueryHandler`, uma consulta agregada com custo
> independente de N** — reprovar contra volume **medido em produção**, com o
> número declarado antes de medir e o estado escrito junto dele.
> **Recalibrar quando o primeiro deploy em produção tiver volume real**, condição
> já rastreada no checklist de `02-HISTORICO_E_STATUS.md:4765`.
> Hoje não há medição que justifique: dev tem 10 sessões e não há produção. E a
> consolidação introduz segunda fonte de verdade que envelhece em silêncio.

### Árvore de arquivos

```
apps/frontend/src/
├── app/
│   ├── routes.tsx                          (M) rota /inventory; a raiz passa a
│   │                                           redirecionar para ela (D1)
│   └── router.test.tsx                     (M) alvo do redirect da raiz; o
│                                               beforeEach de describe('AppRouter')
│                                               (ver Riscos)
├── components/layout/
│   ├── AppShell.tsx                        (M) quinto item de navegação (D1)
│   └── AppShell.test.tsx                   (M) lista de itens, contagem de ícones,
│                                               tabela de item ativo, e a asserção
│                                               de 'Dashboard' ausente
├── features/
│   ├── auth/pages/
│   │   ├── LoginPage.tsx                   (M) navigate('/') (D2)
│   │   └── LoginPage.test.tsx              (M) a asserção de destino
│   └── inventory/pages/
│       ├── InventoryPage.tsx               (A) a tela (D3-D8)
│       └── InventoryPage.test.tsx          (A) estados, independência, negativas
└── (nenhum arquivo novo em components/ — D4 recusa extrair antes de contar)

openspec/changes/frontend-inventario-catalogos/
├── specs/catalog-inventory-ui/spec.md      (A) ADDED
└── design/
    ├── Inventario Buteco Agentes.dc.html   (A) o protótipo desta tela
    └── support.js                          (A) runtime do Claude Design
```

O protótipo é anexado como as outras changes do redesenho fazem. O `support.js`
é o runtime do Claude Design, não lógica desta tela: as cinco cópias já
arquivadas no repositório são **byte-idênticas** entre si (um único md5), e a do
projeto de design tem o mesmo tamanho, o mesmo número de linhas (1911) e o mesmo
cabeçalho `GENERATED`. A cópia anexada aqui veio do repositório, não de
transcrição.

Nenhum arquivo em `apps/api`, `apps/inbox` ou `apps/workers`.

## Risks / Trade-offs

**`apps/inbox` entra no caminho de entrada.** Hoje abrir o painel dispara
`listAgents` e nada mais. Com esta change, toda entrada dispara **quatro
requisições contra dois processos**, e a tela inicial de todo operador passa a
depender de `apps/inbox` estar de pé.

O tratamento por card (D6) resolve o que a tela **mostra**; não resolve que a
disponibilidade entrou no caminho de entrada. **Não há convenção que já governe
isto** — a seção *Degradação graciosa* de `docs/conventions.md:136-161` é inteira
sobre backend (task principal, `BackgroundService`), e a seção *Frontend*
(`:346-357`) tem quatro regras, nenhuma sobre falha parcial de uma de N origens
numa tela. Fica como risco declarado.

→ *Leitura: aceitável*, por quatro razões:

1. Falha isolada por card. Os outros três servem.
2. Nenhum card bloqueia a casca nem a navegação — de qualquer estado, o atalho
   para qualquer listagem funciona.
3. **A change não cria a dependência, ela a torna visível mais cedo.**
   `apps/inbox` fora do ar já quebra `/channels` hoje; a diferença é que o
   operador descobre ao abrir em vez de ao clicar. Para uma tela cujo propósito é
   dizer o que está de pé, isso é a função, não o custo.
4. Sem ela, o operador só descobre navegando.

→ *Mitigação em código:* a regra do fim de D6 — nenhum portão de carregamento
combinado.

**O precedente em código para falha parcial é bom, e é outro.**
`KnowledgeBaseListPage.tsx:157-167` degrada só a parte afetada, **diz por quê**, e
nomeia o que ficou indisponível: *"Falha do resumo não derruba a listagem [...]
Dizer por quê é o que impede o travessão de ser lido como 'zero'"*. É o molde.

**`router.test.tsx` tem um ponto frágil que esta change encosta.** O aviso de
`router.test.tsx:42-50` registra a dimensão de blast radius da convenção 18 —
toda função de um módulo mockado parcialmente que uma rota chame precisa estar no
override, ou **escapa para a rede** e derruba o teste *seguinte*. As quatro
funções do inventário já estão no override. **Mas** `describe('AppRouter')`
(`:213-222`) tem `beforeEach` próprio que reseta só `listAgents`, e
`vite.config.ts` não liga `clearMocks`. Hoje o smoke test renderiza `/` → `/agents`,
que só chama `listAgents`. Com a raiz indo para o inventário, ele passa a depender
de `mockResolvedValue` **vazado** do `describe` anterior: passa por acidente de
ordem. → *Mitigação:* configurar as quatro no `beforeEach` daquele `describe`.

**A casca aparece em todas as telas.** Um erro nela é visível na aplicação
inteira. → *Mitigação:* é um item de navegação a mais num array literal, com as
asserções de estrutura já existentes; risco bem menor que o da change que
reescreveu a casca.

**A suíte não enxerga a grade responsiva nem contraste** (convenção 14). → *Ver
Open Questions.*

## Migration Plan

Uma implantação, sem etapas: frontend estático, o build sai inteiro. Nenhuma
migração de dados, feature flag ou compatibilidade a manter. Nenhuma rota de
backend tocada.

**Rollback:** reverter o commit. A raiz volta a `/agents`, o login volta a
`/agents`, o quinto item some. Nenhum estado persistido no navegador depende
desta change.

## Open Questions

**Nenhuma pergunta de produto em aberto.** Rota, rótulo, escopo da nova tentativa,
proveniência, dimensões e contraste fecharam por leitura do protótipo e do código.

**Uma pendência de verificação, com gatilho** — não é pergunta, é trabalho que
esta fase não podia fazer:

> **Conferência visual do responsivo.** Renderização real não se confirma lendo
> CSS. O protótipo foi renderizado e medido no motor de layout (D7), mas o **app**
> não existe ainda, e o cromo dele é diferente do protótipo em 20px. Os três
> pontos de reflow calculados para o app — **1328px**, **1056px** e **784px** de
> janela — são aritmética verificada no motor com o cromo do protótipo,
> transposta para o cromo do app.
>
> **Gatilho: primeira rodada de conferência manual da convenção 14, antes do
> apply**, nos dois esquemas de cor, com a janela cruzando os três pontos. Atenção
> à faixa de coluna única: ela cai abaixo do `sm` do Mantine (768px), que é o
> `breakpoint` declarado em `AppShell.tsx:41` — o comportamento da casca ali é o
> registrado na D4 da change da casca (o conteúdo aperta, não vira gaveta) e **não
> foi reconferido nesta fase**.
