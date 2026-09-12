# Handoff: Bases de conhecimento (adendo ao redesenho do painel)

**Revisão 2 — 09/09/2026.** Esta revisão foi validada contra o código do `frontend` (React 19 +
Mantine, react-router em data mode, TanStack Query). Mudança principal em relação à revisão 1:
**o vínculo base ↔ agente saiu da aba Visão geral e virou uma quarta aba, e a lista passou a
mostrar só as bases vinculadas.** A justificativa e o que a validação encontrou no código estão na
seção 4; o que mudou está resumido no fim do arquivo.

Funcionalidade nova no protótipo `Buteco Agentes.dc.html`. Reaproveita os padrões já
estabelecidos no painel — card seccionado, rótulo de seção uppercase, cabeçalho de detalhe com
badges, aparência de badge do tema, tabelas de lista com busca client-side, modal de confirmação,
toast. Nada de token novo, nada de família tipográfica nova.

## Estado do codebase (validação)

Nenhuma parte de conhecimento existe hoje em `frontend/src`: não há `features/knowledge-bases`,
nem campo de bases no tipo `Agent` (`features/agents/types/agent.ts`), nem rota, nem item de nav.
Tudo nesta especificação é código novo. O que existe e serve de molde:

| Precisa de | Molde no codebase |
|---|---|
| Card seccionado com faixa e linhas | `components/data/SectionedCard.tsx` (`.Row`, `.Body`) |
| Aba de vínculo | `features/agents/components/AgentDelegationsTab.tsx` |
| Cabeçalho de detalhe | `components/layout/DetailHeader.tsx` |
| Estado de aba na URL | `features/agents/pages/AgentDetailPage.tsx` (`parseTab`, `?tab=`) |
| Query/mutation + cache | `features/agents/api/useAgents.ts` |
| Cliente HTTP e `ApiError` | `features/agents/api/agentsApi.ts` |
| Visão inversa derivada de `GET /agents` | `features/mcp-servers/utils/agentUsage.ts` |
| Ícones | `lucide-react` (nav usa `Bot`, `Server`, `MessagesSquare`) |

## Rotas

```
/knowledge-bases                 catálogo de bases
/knowledge-bases/new             criar base
/knowledge-bases/:id             detalhe — abas: Documentos | Diagnóstico do índice
/knowledge-bases/:id/edit        editar base (nome + descrição)
```

Estado da aba do detalhe **na URL**, como no detalhe do agente. Documentos são gerenciados em
modal sobre o detalhe (não em rota própria): a operação é curta e o contexto da base importa.

Nav lateral ganha um quarto item, **Conhecimento** (`K`), ativo em todas as rotas do grupo.

## 1. Catálogo (`/knowledge-bases`)

Colunas escolhidas para responder as perguntas que levam alguém a abrir uma base:

| Coluna | Conteúdo | Pergunta que responde |
|---|---|---|
| Base | nome 13px/600 `--ac` + descrição 11px `--mut2`, ellipsis | é essa? |
| Documentos | "{n} documentos" / "Nenhum" | tem conteúdo? |
| Indexação | "3 indexados · 1 falhou", em `--wa` quando há pendente/indexando/falha | está utilizável? |
| Consultada por | nomes dos agentes vinculados, ou "—" | mexer aqui afeta quem? |
| Estado | badge Ativa/Inativa + `Falha` (err) ou `Nada indexado` (warn) | precisa de atenção? |

Busca (nome + descrição) e filtro **Todas · Ativas · Inativas · Com falha** rodam no cliente
sobre a resposta inteira, como nas listas existentes. Sem paginação.

Vazio do catálogo: bloco tracejado, "Nenhuma base de conhecimento cadastrada ainda." + uma linha
explicando o que uma base é + botão **Criar a primeira base**. Vazio de busca: linha centralizada
12.5px `--mut2`, igual às outras listas.

## 2. Detalhe da base

Cabeçalho no padrão de detalhe: "← Bases de conhecimento", nome + badges, subtítulo com
"{n} agentes consultam esta base · {resumo de indexação}", ações **Editar** e
**Desativar/Ativar** (desativar passa por modal, como agente e servidor).

**A descrição é o primeiro card da aba Documentos**, com rótulo
`DESCRIÇÃO — TEXTO LIDO PELO MODELO`, o texto citado com borda esquerda 3px accent, e a nota
"Este texto não é mostrado ao cliente. Ele é a descrição da ferramenta que o agente vê: é por ele
que o modelo decide se a pergunta pertence a esta base." Sem descrição, o card vira callout âmbar.
Ela não é o subtítulo da página exatamente para não parecer decorativa.

Abaixo: nota de assincronia, botão **Adicionar documento**, callout âmbar quando há documento
pendente/indexando, tabela de documentos, e o card **Agentes que consultam esta base** (visão
inversa derivada de `GET /agents`, mesmo padrão do detalhe de servidor MCP).

## 3. Documentos

Tabela: `minmax(0,2.2fr) 130px 150px 170px` — Documento (título + pill mono `markdown` + linha de
estado/atualização) · Fragmentos · Indexação · Ações (**Atualizar**, **Excluir**).

Estados de indexação, todos com badge do tema:

- `Pendente` — cinza (`--sf2/--mut`), nota "na fila de indexação", fragmentos "—".
- `Indexando` — accent (`--acsf/--ac`) com spinner 11px à esquerda, nota "gerando embeddings".
- `Indexado` — verde, contagem de fragmentos em mono.
- `Falhou` — vermelho, **mais uma faixa `--dabg` de largura total sob a linha** com título
  "Falhou ao indexar", o motivo em 12px/1.55 sem truncar (`max-width: 820px`, quebra normal) e
  botão **Reindexar documento**.

**A transição é o comportamento, não só os estados.** Documento novo entra como `Pendente`, vira
`Indexando` ~2,4s depois e chega a `Indexado` ~3,2s depois disso, sem recarregar a tela. No
codebase: polling da lista de documentos enquanto houver documento em `pending`/`indexing`
(ou SSE, se houver), parando quando todos estiverem terminais. **Sem barra de progresso
percentual** — o sistema conhece o estado do documento, não o percentual.

Atualizar um documento o devolve para `Pendente` e descarta os fragmentos antigos; o modal avisa
isso antes de salvar. Excluir passa por modal nomeando os agentes afetados.

Vazio: "Nenhum documento nesta base." + "Esta base está vinculada a N agentes, mas uma consulta
não devolve nada até algum documento ficar indexado." + botão.

Modal de documento, com dois modos num segmented control no topo:

- **Subir arquivos** (padrão ao adicionar) — área tracejada com drag-and-drop e clique, aceitando
  `.md`, `.markdown` e `.txt`, vários arquivos por vez. Cada arquivo aceito entra numa linha com
  o nome do arquivo em mono, o tamanho, um campo **Título do documento** já pré-preenchido a
  partir do nome (extensão removida, `-`/`_` viram espaço) e um × para descartar. Arquivo de
  formato não aceito entra na mesma lista com faixa `--dabg` dizendo o motivo — não é descartado
  em silêncio. Nota fixa: "PDF, DOCX e imagens não são aceitos: a base guarda texto markdown, e
  mídia binária não é armazenada. Converta o arquivo antes de subir." O botão primário conta os
  itens ("Adicionar 3 documentos") e **cada arquivo se torna um documento próprio**, todos
  entrando como `Pendente`. No codebase: `multipart/form-data`, um documento por arquivo; o
  cliente lê o texto só para mostrar tamanho e título sugerido.
- **Escrever manualmente** — Título*, **Tipo de origem** como select desabilitado com um único
  valor `markdown` (mostra que o campo existe e que hoje não há escolha), e Conteúdo em textarea
  mono.

Ao atualizar um documento existente, os modos viram **Substituir por arquivo** (um arquivo só,
mantendo o título atual) e **Escrever manualmente** (padrão, já com o conteúdo atual carregado).

## 4. Vínculo base ↔ agente — **aba Conhecimento** (tela nova)

Esta é a tela nova desta revisão. Substitui a seção inline no fim da Visão geral proposta na
revisão 1.

### 4.1 Onde fica

Quarta aba do detalhe do agente, entre **Ferramentas** e **Delegações**:

```
Visão geral | Ferramentas (n) | Conhecimento (n) | Delegações (n)
```

Valor na URL: `?tab=conhecimento`. O contador é o número de bases **vinculadas**, no mesmo
`TabCounter` das outras abas (escondido quando 0).

**Por que mudou.** A revisão 1 colocava o vínculo no fim da Visão geral. O código mostra que
aquela aba não tem fim visível:

1. O card de instruções é limitado em `ScrollArea h="calc(100vh - 360px)" mih={220}` — ele não
   empurra nada, mas ocupa quase toda a altura útil.
2. A coluna direita tem **quatro** cards (Modelo, Skills, **Protocolo A2A**, Datas) e é ela que
   define a altura da grade. O card de A2A entrou depois da revisão 1 e é o que fecha a conta.
3. Logo, qualquer seção abaixo da grade nasce fora da dobra, em uma posição que varia por agente
   (tamanho do prompt, presença de callouts, endereços A2A ausentes ou não). O vínculo de
   conhecimento é justamente o que o operador vem conferir quando o agente responde sem contexto.

Como aba, tem endereço fixo, é linkável, a contagem aparece na barra sem rolagem, e a requisição do
catálogo sai da rota padrão. A régua da revisão 1 ("relação com configuração própria = aba, vínculo
puro = seção inline") não se sustentou: o custo de achar a seção passou a ser maior que o custo de
uma aba a mais.

### 4.2 O que a lista mostra: só o que está vinculado

Volume real informado pelo time: **100+ bases no catálogo, 5+ vinculadas por agente**. Nessa
proporção, a lista completa com checkbox obriga o operador a varrer o catálogo inteiro para
descobrir o vínculo atual — que é a informação que ele abriu a tela para ver. A leitura e a edição
foram separadas:

- **A aba lista as bases vinculadas.** Uma linha por base: nome (link para o detalhe da base),
  descrição em 11.5px `--mut2`, contagem `"{n} documentos · {k} indexados"` (em `--wa` quando
  `k = 0`) e botão secundário **Desvincular**.
- **Vincular é uma ação, não um estado da lista.** Botão primário **Vincular base** no topo abre um
  modal com busca.

Cabeçalho do card: rótulo `BASES VINCULADAS` à esquerda e `"{n} bases no catálogo"` à direita — o
total continua visível, sem ocupar a lista.

Resumo acima do card, 12px `--mut`:
`"{n} bases vinculadas · consultadas sob demanda, como ferramenta"`, ou
`"Nenhuma base vinculada — este agente não consulta conhecimento"`.

Avisos por linha (faixa `--wabg` sob a linha, recuada 14px):

- base inativa → "Base inativa: o agente não a consulta enquanto ela estiver desativada."
- base sem documento indexado → "Nenhum documento indexado nesta base: a consulta não devolve
  trecho algum."

Enquanto a requisição do vínculo corre, a linha mostra spinner 11px + "salvando…" ao lado do nome.

### 4.3 Vazio

Bloco tracejado dentro do card, centralizado:

> **Nenhuma base vinculada**
> Sem vínculo, este agente responde só com o system prompt: ele não tem onde consultar horários,
> cardápio ou políticas.
> [Vincular base]

O vazio é forte de propósito — é a causa comum de "o agente está inventando resposta".

### 4.4 Modal "Vincular base de conhecimento"

Max-width 560px, `max-height: 80vh`, corpo com scroll próprio.

- **Cabeçalho**: título + nota "O vínculo é salvo na hora. Documentos são criados e editados em
  Conhecimento." + input **"Buscar por nome ou descrição"**.
- **Corpo**: uma linha por base do catálogo — nome 12.5px/600, descrição, contagem de documentos —
  e à direita um botão que alterna: **Vincular** (primário) quando não vinculada, **Vinculada**
  (secundário, fundo `--sf2`) quando já vinculada. Clicar dispara a requisição na hora; **o modal
  não fecha**, para vincular várias em sequência.
- **Vazio**: "Nenhuma base corresponde à busca." / "Nenhuma base cadastrada ainda."
- **Rodapé**: link **Criar nova base** (→ `/knowledge-bases/new`) à esquerda e botão **Concluir**
  à direita. Concluir só fecha — não existe salvar.

A busca é client-side sobre a resposta inteira, como as outras listas do painel. **Com 100+ bases
essa é a primeira lista do painel em que a ausência de busca no backend incomoda** — se o catálogo
crescer para centenas, pedir `GET /knowledge-bases?q=` e paginação, e trocar a filtragem local por
requisição com debounce. Enquanto isso, documentar como dívida.

### 4.5 Nota de projeto embutida na tela

O rodapé do card tem um "Por que só as vinculadas, e numa aba própria" recolhido, com a
justificativa acima em três parágrafos. É para o time discutir a decisão onde ela aparece; **não é
para levar ao codebase.**

## Divergências dos padrões existentes (explícitas, com motivo)

1. **Sem rascunho e sem barra fixa de salvamento no vínculo de conhecimento.** Cada ação é uma
   requisição própria (`PUT /agents/{id}/knowledge-bases/{kbId}` ou `DELETE`), com spinner
   "salvando…" na linha e toast no fim. É a única aba de vínculo que **não** usa
   `UnsavedChangesBar` + `useUnsavedChangesGuard`, ao contrário de `AgentToolsTab` e
   `AgentDelegationsTab`. Motivo: o vínculo é atômico e não tem semântica de conjunto (o vínculo
   MCP é substituição do conjunto inteiro validada com handshake; a delegação também é conjunto).
   Além disso o modal de vincular é uma superfície transitória — um rascunho que só existisse
   enquanto o modal está aberto seria pior que salvar na hora. Se o backend só expuser `PUT` de
   conjunto, manter a UI: uma chamada por ação enviando o conjunto resultante.
2. **Descrição obrigatória na criação da base.** O restante do painel só exige nome. Motivo: sem
   descrição, o modelo não tem critério para consultar a base e a funcionalidade não funciona —
   é campo de runtime, não de conveniência do operador. O form mostra contador com aviso âmbar
   abaixo de 80 caracteres e um preview "Como o agente vê esta base" com nome e descrição em mono.
   Se o backend aceitar descrição vazia, a validação é só do cliente.
3. **A lista de agentes não ganhou coluna de conhecimento.** Já tem Ferramentas e Delega para;
   uma terceira coluna de relação deixaria a linha ilegível. O vínculo aparece no catálogo de
   bases (coluna "Consultada por") e no detalhe do agente.
4. **Rodapé da barra lateral sem identificação de operador.** O protótipo anterior mostrava um
   avatar com `operador@buteco`; removido, porque o login devolve credencial e validade, não nome
   nem e-mail. Sobra só o controle de tema.

## O que não foi projetado, de propósito

- Nenhum formulário no diagnóstico. Provedor, modelo e dimensão saem do índice e vêm rotulados
  como somente leitura, com a razão dita na tela ("configuração de processo, validada no boot").
  Quando nada está indexado, os três campos mostram "—" e um bloco explica que eles passam a
  existir depois do primeiro documento indexado — o painel não afirma qual modelo *seria* usado.
- Nenhuma seleção de documentos permitidos, número de resultados ou "sempre injetar" no vínculo.
- Nenhuma métrica de uso: nem consultas por base, nem popularidade de documento, nem taxa de
  acerto. O diagnóstico diz em uma linha que esse dado não é coletado, para a ausência não parecer
  bug.
- Nada que afirme que uma resposta "foi fundamentada" em um trecho. O sistema registra o que a
  busca devolveu; se essa visão for construída um dia, o rótulo é "consultado", não "fonte".
- Nenhum "criado por" nem autoria.

## Cenários embutidos no protótipo

- **Políticas de Cobrança** — 3 indexados + 1 falhou (429 do provedor, motivo longo e completo).
- **Catálogo de Produtos** — 1 indexado, 1 indexando, 1 pendente ao abrir: a transição acontece
  sozinha na tela nos primeiros segundos.
- **Rotinas Internas** — inativa, sem descrição, sem documento: cobre vazio de documentos,
  callout de descrição ausente e diagnóstico sem nada indexado.
- **Suporte Técnico** (agente) — nenhuma base vinculada: vazio da aba Conhecimento.
- **Atendimento Financeiro** (agente) — 5 bases vinculadas de um catálogo de 9, incluindo uma com
  documento em indexação: cobre a lista cheia, o aviso por linha e a busca no modal.
- O catálogo do protótipo tem 9 bases (Políticas de Cobrança, Catálogo de Produtos, Rotinas
  Internas, Horários e Endereço, Cardápio A La Carte, Rodízio, Promoções Vigentes, Reservas e
  Eventos, Delivery e Retirada) — o suficiente para a busca do modal fazer sentido. **A tela real
  precisa aguentar 100+.**
- Tweaks: `catalogoVazio` mostra o catálogo sem nenhuma base (e o vínculo sem base para vincular);
  `simularFalhaIndexacao` faz o próximo documento indexado falhar, para inspecionar o erro.
- Os dois esquemas de cor saem do controle de tema no rodapé da barra lateral; todos os papéis
  visuais novos usam apenas tokens semânticos já definidos nos dois esquemas.


## O que mudou da revisão 1 para a revisão 2

1. **Vínculo virou aba** `?tab=conhecimento`, em vez de seção no fim da Visão geral. Motivo na
   seção 4.1: a coluna direita da Visão geral tem quatro cards (o de A2A entrou depois) e empurra
   qualquer seção inline para fora da dobra.
2. **A lista mostra só as bases vinculadas**, em vez de todas com checkbox. Motivo na seção 4.2:
   100+ bases no catálogo contra 5+ por agente.
3. **Vincular passou para um modal com busca**, com vínculo salvo por ação e sem fechar o modal.
4. **Colunas da lista reduzidas** a nome, descrição e contagem de documentos — conforme o que o
   time apontou como necessário na página do agente. Estado de indexação continua na contagem
   (`{k} indexados`, em âmbar quando zero) porque é o que diz se o vínculo é utilizável.
5. **Cadastro e edição de documentos permanecem exclusivamente em `/knowledge-bases`.** Na página
   do agente só existe vincular e desvincular. Confirmado com o time.
6. Catálogo fictício do protótipo ampliado de 3 para 9 bases.
