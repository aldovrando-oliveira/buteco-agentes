## Context

Issue #99. No detalhe da base (`/knowledge-bases/{id}`), hoje:

- `KnowledgeBaseDetailPage.tsx:269-318` monta a barra de abas logo abaixo do
  `DetailHeader`, e **dentro** de `<Tabs.Panel value="documentos">` empilha
  `KnowledgeBaseDescriptionCard`, `KnowledgeDocumentsCard` e
  `KnowledgeBaseAgentsCard`, nessa ordem.
- `keepMounted={false}` (linha 269) desmonta o painel inativo. É ele que
  sustenta o `enabled` da consulta de proveniência e **não muda**, mas é também
  o que faz Descrição e Agentes sumirem na aba `Diagnóstico do índice`.
- `KnowledgeBaseAgentsCard.tsx` lista um agente por `SectionedCard.Row`, com o
  nome como `Anchor` para `/agents/{id}` e um `Badge` `Ativo`/`Inativo` à
  direita. Recebe `agents?: Agent[]` da página (convenção 7); `undefined`
  quando a consulta não tem dado.

A referência visual é o canvas "Bases sincronizadas — protótipo"
(<https://claude.ai/artifact/XzvXSpPRpqaCjNZ45ooAPY>), pranchas **3a**
(`DetalheManual.dc.html`) e **4a** (`Detalhe.dc.html`), copiadas em
[`design/`](design/README.md) como lidas em 02/10/2026. O que as pranchas
decidem para esta change:

| elemento | 3a | 4a |
|---|---|---|
| ordem acima da barra | Descrição, Agentes | Descrição, Origem (#107), Agentes |
| card de agentes | estado vazio, sem contagem no cabeçalho | `7 agentes` no cabeçalho, 7 chips em `flex-wrap`, `gap: 8px` |
| chip | — | pill de 28px de altura, raio 14px, borda, fundo levemente distinto do card, só o nome |
| largura | os dois cards em largura cheia do conteúdo | idem |
| barra de abas | `margin-top: 6px` a mais que o espaçamento entre cards | idem |

Restrições: só `apps/frontend`; nenhum dado novo (mesmas `GET
/knowledge-bases/{id}` e `GET /agents`); nenhum comportamento das abas muda.

## Goals / Non-Goals

**Goals:**

- Descrição e Agentes acima da barra de abas, nessa ordem, em qualquer aba.
- Card de agentes em largura cheia, com chips que quebram na horizontal.
- Estado do agente visível por texto no chip; contagem no cabeçalho só com a
  lista carregada e não vazia.
- Estado vazio e estado indisponível com o texto de hoje.
- Estado de carregamento próprio no card de agentes, distinto da falha (D5).

**Non-Goals:**

- Aba Histórico (#101); card Origem, documentos somente leitura e arquivos
  ignorados (#107); qualquer comportamento de base sincronizada.
- O mesmo defeito de carregamento no detalhe do servidor MCP
  (`McpServerAgentsCard` e o modal de desativação): reportado na #112, não
  corrigido aqui.
- Componente de chip compartilhado — ver D4.

## Decisions

### D1 — Os dois cards viram irmãos da barra na `Stack` da página, e não conteúdo repetido em cada painel

Descrição e Agentes passam a ser filhos diretos da `Stack` da página, entre o
`DetailHeader` e o `<Tabs>`. Nada mais na página muda de lugar — exceto a barra
de abas, que ganhou `mt={8}` na conferência (D8).

**Descartado — montar os dois cards no topo de cada `Tabs.Panel`.** Produziria
o mesmo visual, mas os cards continuariam "dentro da aba": remontariam a cada
troca de aba e ficariam, para leitor de tela, dentro do `tabpanel` — que é
exatamente o que a issue diz que eles não são. E cada aba nova (#101) teria de
lembrar de repeti-los.

**Descartado — card de agentes ao lado da Descrição.** Já descartado na issue:
com muitos agentes ele fica muito mais alto que a Descrição.

Consequência a registrar para a #107: o card Origem entra **entre** Descrição e
Agentes (prancha 4a). Esta change não o antecipa.

### D2 — Correção de protótipo: o chip marca por texto só o agente inativo

O protótipo (3a, 4a, 4b) mostra chips só com o nome. O card de hoje mostra o
estado, e esse dado não pode sumir: agente inativo **não consulta** a base, e um
chip igual aos outros afirmaria uma consulta que não acontece (convenção 13 na
direção da omissão). Regra que o sistema já tem vence o protótipo (convenção
17).

**Forma escolhida:** o chip de agente inativo exibe o texto `Inativo` depois do
nome, em tom atenuado; o chip de agente ativo não carrega marca. O texto fica
**dentro do chip e fora do link**: o link continua tendo o nome do agente como
nome acessível, e o `Inativo` é texto vizinho, lido em sequência por leitor de
tela. A cor nunca é o único sinal.

**Alternativas descartadas:**

- **Rótulo nos dois (`Ativo` e `Inativo`).** Manteria o teste atual intacto,
  mas com sete agentes repete `Ativo` sete vezes para dizer o caso normal,
  dobra a largura de cada chip e desfaz o que a forma em chips existe para
  resolver. O estado relevante para o operador é a exceção.
- **`Inativo` dentro do link.** O nome acessível viraria `Pós-venda Inativo`,
  e a issue exige o nome do agente como nome acessível.
- **Só cor (chip acinzentado).** Convenção 13 e acessibilidade: cor sozinha não
  é texto.
- **Link com `aria-label` = nome e `Inativo` visível dentro dele.** Esconde o
  estado de quem usa leitor de tela.

**O que acontece com o teste "exibe o estado de cada agente"**
(`KnowledgeBaseAgentsCard.test.tsx:51`). Ele afirma `getByText('Ativo')` e
`getByText('Inativo')`. A primeira asserção deixa de valer **por decisão**, não
por regressão, e o teste é reescrito como "marca por texto só o agente
inativo": `Inativo` dentro do chip do agente inativo, ausência de `Inativo` no
chip do ativo, e ausência de qualquer `Ativo` no card (a negativa é a que
impede a volta do rótulo duplicado). É a única asserção existente que muda.

Esta correção entra, na implementação, na lista viva **"Correções de
protótipo"** do `02-HISTORICO_E_STATUS.md`.

### D3 — A contagem conta os chips, inativos incluídos, e só existe com lista carregada e não vazia

O cabeçalho exibe `N agentes` (`1 agente` no singular), no slot `action` que o
`SectionedCard` já tem, com o tom de texto atenuado da prancha 4a.

**N é o número de chips exibidos**, inativos incluídos. A seção lista os
agentes **vinculados**; o número que a encabeça conta o que está embaixo dele.

- **Descartado — contar só os ativos.** O número deixaria de bater com os chips
  visíveis, e o operador teria de descobrir a regra.
- **Descartado — `7 agentes, 2 inativos`.** Redundante com a marca de cada chip
  (D2).

**Proveniência do número (convenção 13):** ele vem da filtragem de `GET
/agents` por `agentsConsultingBase`. Os três estados:

| estado | `status` / `agents` (D5) | cabeçalho |
|---|---|---|
| carregando | `pending` / `undefined` | sem contagem |
| falha | `error` / `undefined` | sem contagem |
| respondeu, nenhum vinculado | `[...]`, filtro vazio | sem contagem; o estado vazio diz o fato |
| respondeu, N vinculados | `[...]`, filtro com N | `N agentes` |

O zero medido **não** é exibido como `0 agentes`: o estado vazio já diz a
mesma coisa por extenso, e a prancha 3a não mostra número nenhum.

### D4 — O chip é local ao card; nenhum componente compartilhado

Não existe chip no painel hoje (varredura por `Pill`/`chip` em
`apps/frontend/src`, sem resultado). Um consumidor só não justifica extração
(convenção "sem abstração prematura"). O chip é marcado dentro de
`KnowledgeBaseAgentsCard`.

Semântica: a lista de chips é uma lista (`ul`/`li` sem marcador), para o leitor
de tela anunciar quantos agentes há. **Descartado** um `div` com links soltos,
que perde essa contagem para quem não vê o cabeçalho.

Cores: fundo e borda do chip usam variável que troca por esquema (convenção
"papel visual que troca de ponta da escala") — `var(--mantine-color-default-border)`
para a borda e **`var(--buteco-page-bg)` para o fundo**, decidido na
conferência (D8); **nunca** `gray[n]` ou `dark[n]` fixos. Altura, raio e
espaçamento seguem a prancha 4a (28px, pill, 8px de vão), conferidos na D6.

O texto do chip é a tinta do card (`c="inherit"`), em 13px, e **não** a cor de
link: o chip da prancha é neutro. O link ocupa a pílula na altura toda e carrega
o padding, e o anel de foco é desenhado na pílula (`li`) e não no link — uma
regra em `index.css` com `:has(a:focus-visible)`, mesma espessura e mesma
variável do foco do Mantine. As três coisas saíram da conferência (D8).

### D5 — O card distingue carregando, falha e respondido, e a página diz qual é

**O defeito.** Até aqui `agents` chegava `undefined` **tanto no carregamento
quanto na falha** (`KnowledgeBaseDetailPage.tsx:304` repassava só
`agentsQuery.data`), e o card mostrava "Não foi possível carregar os agentes…"
nos dois casos. Durante o carregamento isso afirma uma falha que não aconteceu
(convenção 13). É defeito existente, e é corrigido aqui porque esta change
reescreve exatamente o requisito e o componente afetados — decisão do
mantenedor na revisão.

**Forma escolhida.** A página repassa, ao lado de `agents`, o `status` da
consulta: `'pending' | 'error' | 'success'`, os três valores que
`agentsQuery.status` do TanStack Query já tem. O tipo é declarado no próprio
card, e não importado da biblioteca, para o componente apresentacional não
depender da camada de consulta (convenção 7). O card decide assim, cada ramo
por um fato e nenhum por exclusão:

| condição | o que o card mostra |
|---|---|
| `agents` definido | os chips ou o estado vazio — o dado está na mão |
| `agents` indefinido e `status === 'pending'` | carregamento |
| `agents` indefinido e `status === 'error'` | o texto de indisponibilidade de hoje |

O dado na mão vem primeiro de propósito: se uma revalidação em segundo plano
falhar depois de uma resposta, o TanStack Query mantém `data` e passa `status`
a `error`. A lista lida continua sendo uma medição que aconteceu, e trocá-la
por "não foi possível carregar" apagaria o que o sistema sabe. É o mesmo
comportamento de hoje nesse caso.

**Descartado — `isLoading` ao lado de `agents`.** O card ainda teria de
concluir "falha" de "não está carregando e não tem dado", que é exatamente a
inferência por ausência que produziu o defeito. Um quarto estado silencioso
(consulta desabilitada, por exemplo) cairia em "falha" sem ninguém decidir.

**Descartado — a página montar um objeto de estado único**
(`{ kind: 'loading' } | { kind: 'error' } | { kind: 'ready', agents }`).
Elimina o mesmo problema, mas reescreve na página um tipo que a consulta já
fornece, e a página tem mais um ponto de mapeamento para manter.

**O que o carregamento mostra.** O padrão do painel para carregamento de card é
o de `KnowledgeDocumentsCard.tsx:113-124`: dentro do `SectionedCard.Body`, um
`Loader size="sm"` ao lado de um `Text size="sm"` "Carregando …". O card usa a
mesma forma, com "Carregando agentes...", e mantém o título da faixa. Durante o
carregamento o card **não** mostra o texto de indisponibilidade, **não** mostra
o estado vazio e **não** mostra contagem.

**Consumidor afetado fora do card.** `InventoryPage.tsx:126-128` cita
`KnowledgeBaseAgentsCard.tsx:17-27` como precedente do "não sei" e vai
apontar para linhas que deixam de existir. O comentário é atualizado; o
comportamento do inventário não muda.

**Mesmo defeito em outra tela.** `McpServerAgentsCard`, cujo tratamento este
card copiou, tem a mesma forma (`McpServerDetailPage.tsx:170` repassa só
`agentsQuery.data`; `McpServerAgentsCard.tsx:15-24` trata `undefined` como
falha), e o modal de desativação da mesma página diz "Nenhum agente usa este
servidor no momento." com o catálogo carregando ou em falha
(`McpServerDetailPage.tsx:115` e `:179-188`). Reportado na **#112**, não
corrigido aqui.

### D6 — Conferência contra o protótipo é passo explícito, por dimensão e não só por estado

No molde do que o `02` registrou nas etapas de UI anteriores (a conferência
que comparou estados e nunca dimensões, e o viewport de captura mais estreito
que o do operador):

1. Subir `apps/frontend` (`npm run dev`) contra `apps/api` local. **Os dados
   não foram gravados no banco** (corrigido na implementação): a base local não
   tinha nenhum vínculo e só cinco agentes. "Sem agente" usa a resposta real de
   `GET /agents`, sem interceptação; os estados com vínculo usam a **resposta
   real** da API, à qual o CDP acrescenta o vínculo com a base
   (`Fetch.fulfillRequest`), e dois a sete clones de um agente real para chegar
   a sete e a doze. Mesmo mecanismo do carregamento e da falha, sem alterar
   dados do ambiente.
2. Percorrer a tela com **Chrome headless dirigido por CDP**, clique real nas
   abas, e capturar nos esquemas **claro e escuro**, a **1440px** e a
   **~1860px** (a largura em que o painel é usado).
3. Estados a capturar, **cada um nas abas `Documentos` e `Diagnóstico do
   índice`**:
   - sem agente vinculado;
   - com um agente;
   - com sete ou mais agentes (quebra de linha dos chips);
   - com agente inativo;
   - agentes carregando;
   - agentes indisponíveis.

   Com o `apps/api` de pé, `GET /agents` não falha sozinho. Os dois últimos
   estados são produzidos **pelo próprio CDP que percorre a tela**, sem derrubar
   a API e sem alterar dados: `Fetch.enable` com padrão filtrando `/agents`;
   para "indisponível", `Fetch.failRequest` em cada requisição pausada; para
   "carregando", a requisição pausada **não é continuada** pelo tempo da
   captura.
4. Comparar contra as pranchas 3a e 4a as **dimensões**, não só a presença:
   largura dos dois cards (cheia, igual entre eles, igual à da barra de abas);
   altura e raio do chip; vão entre chips; onde a quebra acontece a 1440 e a
   1860; distância entre o último card e a barra de abas.
5. Conferir também, fora do que a prancha desenha:
   - o **anel de foco do teclado** no link dentro do chip, visível nos esquemas
     claro e escuro, percorrendo com Tab;
   - a **área clicável do chip de agente inativo**, em que só o nome é link e o
     `Inativo` fica fora dele (D2). O resultado — aceitável ou não — fica
     registrado na conferência; se não for aceitável, a D2 volta para decisão
     do mantenedor antes de qualquer mudança.
6. Cada correção muda o que fica visível: repetir a captura até uma rodada sem
   achado. As capturas ficam em `design/capturas/`, nomeadas por estado, aba,
   esquema e largura.

**A validação manual pelo operador fica pendente** (convenção 14). A
automação reduz rodadas humanas, não substitui nenhuma; o archive espera essa
validação.

### D7 — Testes

Asserções de contrato no jsdom, com as negativas que a convenção 13 pede:

- Página: Descrição e Agentes presentes com `?tab=diagnostico` (hoje não
  estão); os dois **fora** de qualquer `tabpanel` e **antes** do `tablist` na
  ordem do documento (`compareDocumentPosition`), Descrição antes de Agentes;
  com `listAgents` pendente, o card indica carregamento e **não** mostra o
  texto de indisponibilidade, o estado vazio nem contagem; com `listAgents`
  rejeitado, nenhuma contagem.
- Card: chip com link para `/agents/{id}` e nome acessível igual ao nome;
  `Inativo` no chip do inativo e ausente no do ativo; nenhum `Ativo`; contagem
  `7 agentes` com sete vinculados e `1 agente` com um; nenhuma contagem no
  vazio e no indisponível; textos de vazio e indisponível iguais aos de hoje;
  sete agentes num único agrupamento de lista; com `status` `pending` e sem
  `agents`, carregamento sem indisponibilidade, vazio nem contagem; com
  `agents` na mão e `status` `error`, a lista continua exibida.
- Os testes existentes de abas, documentos, diagnóstico e modais **não mudam
  de asserção**. A única exceção é a da D2.
- No teste do card, as chamadas existentes de `renderCard` passam a informar o
  `status` (`'success'` com lista, `'error'` no teste de indisponibilidade):
  é a nova propriedade obrigatória da D5, e não muda nenhuma asserção. O
  helper exige o `status` explícito em vez de deduzi-lo de `agents`, que seria
  a mesma inferência que a D5 remove.

### D8 — O que a conferência mudou (corrige a D1, a D4 e o estado vazio)

Quatro rodadas pela D6 (Chrome headless via CDP, 7 estados × 2 abas × 2
esquemas × 2 larguras, mais foco e área clicável). Cada achado, e o que mudou:

| rodada | achado medido | correção |
|---|---|---|
| 1 | chip com o fundo `--mantine-color-default`, que é **branco no claro, igual ao card**: o chip virava só contorno, onde a prancha 4a o destaca (`#faf9f8` sobre branco) | fundo `var(--buteco-page-bg)` — `#f6f5f3` no claro; no escuro `dark[9]`, mais escuro que o card, o sentido certo da superfície sutil nesse esquema |
| 1 | texto do chip na cor de link (azul) e em 12px (`size="sm"` do tema é 12px) | `c="inherit"` e `size="md"` (13px, o da prancha) |
| 1 | barra de abas a 16px do card de agentes, contra 24px na prancha (vão de 18 + `margin-top` de 6) | `mt={8}` nos `Tabs`: 16 + 8 = 24px. **Corrige a D1**, que dizia que nada mais na página mudava de lugar |
| 2 | sete agentes nunca quebravam linha, nem a 1440: o estado "quebra" não estava sendo exercitado | estado novo de **doze** agentes na conferência — duas linhas nas duas larguras |
| 2 | só o TEXTO do link era clicável (101×19 num chip de 168×28), em **todo** chip, não só no inativo; na prancha o chip inteiro é o link | o link leva o padding e a altura da pílula: chip ativo inteiro clicável; no inativo, link de 119×26 em 168×28, só o rótulo `Inativo` fica de fora |
| 2 | anel de foco retangular no link, cortando a pílula | anel na pílula via `index.css` (`[data-agent-chip]:has(a:focus-visible)`) |
| 2 | estado vazio centralizado com `py="lg"` (card de 123px), contra a frase alinhada à esquerda da prancha 3a | sem `ta="center"` nem `py="lg"` |
| 3 | altura do card variava por estado — carregando 88px, uma linha de chips 94px, vazio e indisponível 83px —, então a barra e tudo abaixo **pulavam** quando `GET /agents` respondia | altura mínima de 28px (a de um chip) no conteúdo de todos os estados: 94px em todos |
| 4 | — | rodada sem achado |

**Divergência aceita, não corrigida:** na prancha o chip muda borda e texto
para azul no *hover*. Aqui o *hover* é o sublinhado padrão do `Anchor`. A
prancha só tem CSS para isso, e reproduzir pediria mais uma regra global por um
estado que o operador já reconhece pelo cursor.

**Área clicável do chip inativo — resultado aceitável.** Só o rótulo `Inativo`
(47×15) não navega; o nome e a altura toda do chip, sim. É o mesmo tamanho de
alvo do chip ativo, menos o rótulo, que é texto de estado e não ação. A D2 não
mudou.

## Árvore de pastas

Só arquivos modificados; nenhum criado fora da change.

```
apps/frontend/src/features/knowledge-bases/
├── components/
│   ├── KnowledgeBaseAgentsCard.tsx        (modificado — chips, contagem, D2-D4)
│   └── KnowledgeBaseAgentsCard.test.tsx   (modificado — D2, D7)
└── pages/
    ├── KnowledgeBaseDetailPage.tsx        (modificado — D1)
    └── KnowledgeBaseDetailPage.test.tsx   (modificado — só acréscimos, D7)

apps/frontend/src/features/inventory/pages/
└── InventoryPage.tsx                      (modificado — só o comentário que cita o card, D5)

apps/frontend/src/
└── index.css                              (modificado — anel de foco do chip, D8)

openspec/changes/frontend-knowledge-base-cards-acima-das-abas/
├── proposal.md
├── design.md
├── tasks.md
├── specs/knowledge-base-catalog-ui/spec.md
└── design/
    ├── README.md
    ├── DetalheManual.dc.html              (prancha 3a)
    └── Detalhe.dc.html                    (prancha 4a)
```

Nada em `libs/`. Nenhuma dependência nova, nenhuma versão fixada.

## Risks / Trade-offs

- **[A lista de documentos desce na tela]** → com uma descrição longa e muitos
  agentes, a barra de abas e a tabela ficam mais abaixo do que hoje. É a troca
  que a issue escolhe; os chips (D3/D4) existem para limitar a altura do card
  de agentes. Verificável na D6, estado "sete ou mais agentes" a 1440px.
  Não testável em jsdom, que não mede layout.
- **[Agente inativo confundido com ativo]** → coberto pela D2 e pelos cenários
  "Agente inativo vinculado aparece marcado por texto" e "Agente ativo não
  carrega marca de estado", com teste negativo.
- **[Contagem durante o carregamento ou na falha]** → cenários "Nenhuma
  contagem durante o carregamento" e "Nenhuma contagem com o catálogo
  indisponível", com teste na página.
- **[Carregamento lido como falha ou como ausência]** → D5; cenário
  "Carregamento não afirma falha nem ausência", com teste no card e na página.
- **[Cor do chip quebrar num dos esquemas]** → D4 proíbe tom fixo; conferido
  nos dois esquemas na D6. Não testável em jsdom.
- **[Área clicável menor no chip do inativo]** → só o nome é link (D2).
  Conferido na D6, item 5; não testável em jsdom.
