## Context

O detalhe do agente hoje é uma pilha vertical: um bloco de ações, um card
único com nome, estado, descrição, provedor, modelo, resumo dos servidores
MCP vinculados, instruções em Markdown e datas; abaixo dele o card de
skills; abaixo, a seção de delegações com um campo de seleção múltipla e
botões próprios de salvar e cancelar.

O vínculo com servidores MCP vive em outro lugar: uma página em
`/agents/:id/mcp-servers`, alcançada por um botão no detalhe. Ela lista
todo o catálogo de servidores, cada linha com um checkbox e um botão de
ver tools que dispara a descoberta ao vivo, e no fim uma dupla salvar e
cancelar — cancelar volta para o detalhe, salvar também.

As duas relações têm a mesma natureza (substituição atômica de um conjunto
via `PUT`), e a assimetria entre elas é acidente histórico, não decisão.

A change anterior deixou o roteamento em data mode, com a árvore de rotas
em módulo próprio. É o que permite usar `useBlocker` para interceptar
navegação, e o que permite pendurar um `loader` de redirect na rota que
esta change remove.

O handoff de design descreve as três abas, a barra fixa de salvamento, os
avisos de vínculo sem tools e de servidor inativo, e o comportamento de
descoberta. Os pontos em que o protótipo diverge da API real estão
tratados nas Decisions 6 e 9.

## Goals / Non-Goals

**Goals:**
- Trazer o vínculo com servidores MCP para dentro do detalhe do agente,
  como aba, e remover a página separada sem quebrar links salvos.
- Tornar visível o estado "vinculado sem nenhuma tool", que hoje é
  indistinguível de um vínculo saudável e não oferece ferramenta nenhuma
  ao modelo em runtime.
- Nunca perder rascunho em silêncio: avisar antes de trocar de aba, sair
  da rota ou fechar a janela com alterações pendentes.
- Manter o operador na tela depois de salvar.
- Dar às delegações o mesmo padrão de interação da aba de ferramentas.

**Non-Goals:**
- Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/inbox`.
- Nenhum endpoint novo, nenhum campo novo em resposta.
- Nada do lado do servidor MCP (visão inversa, catálogo de tools no
  detalhe, cópia por motivo de falha) — change própria.
- Nenhuma busca, filtro ou coluna nova nas listagens — change própria.
- Nenhuma preferência de densidade e nenhum bloco de primeiros passos.
- Nenhum `loader` que busque dados: o data-layer continua sendo React
  Query em cada página (Decision 4).
- Nenhuma biblioteca nova.

## Estrutura de pastas proposta

```
apps/frontend/src/
├── app/
│   └── routes.tsx                        # rota antiga ganha loader de redirect
├── components/
│   ├── feedback/                         # novo diretório
│   │   ├── UnsavedChangesBar.tsx         # barra fixa de rodapé (+ teste)
│   │   └── UnsavedChangesModal.tsx       # diálogo de descarte (+ teste)
│   └── layout/AppShell.tsx               # inalterado
├── hooks/                                # novo diretório
│   └── useUnsavedChangesGuard.ts         # useBlocker + beforeunload (+ teste)
└── features/
    ├── agents/
    │   ├── components/
    │   │   ├── AgentOverviewTab.tsx      # novo — substitui AgentDetailCard
    │   │   ├── AgentToolsTab.tsx         # novo — vem de AgentMcpServersPage
    │   │   ├── AgentDelegationsTab.tsx   # novo — substitui AgentDelegationsSection
    │   │   ├── AgentMcpServerRow.tsx     # reescrito
    │   │   ├── AgentSkillsCard.tsx       # reusado sem mudança
    │   │   ├── AgentDetailCard.tsx       # REMOVIDO
    │   │   └── AgentDelegationsSection.tsx  # REMOVIDO
    │   └── pages/
    │       ├── AgentDetailPage.tsx       # vira host das abas
    │       └── AgentMcpServersPage.tsx   # REMOVIDO
    └── mcp-servers/api/useMcpServers.ts  # useMcpServersQuery ganha { enabled }
```

Cada componente novo tem seu arquivo de teste ao lado, omitido acima por
brevidade.

## Decisions

### Decision 1: Aba na URL como query param, Visão geral como padrão implícito

A aba ativa é `?tab=ferramentas` ou `?tab=delegacoes`. Ausência do
parâmetro, ou valor desconhecido, renderiza a Visão geral — sem reescrever
a URL, porque a ausência é a forma canônica do padrão e um redirect
automático poluiria o histórico de navegação.

Query param e não sub-rota (`/agents/:id/ferramentas`): mantém uma página
só, sem rota por aba nem camada de layout com `Outlet`, e é a forma que o
próprio handoff sugere ao propor o redirect da rota antiga. Como
`useSearchParams` navega de verdade, trocar de aba é uma navegação — então
a mesma guarda cobre trocar de aba e sair da tela, sem código especial
para cada caso (Decision 3).

**Alternativa descartada**: sub-rotas por aba. Ficaria mais explícita na
URL, mas exigiria três rotas novas e um componente de layout só para
hospedar o `Outlet`, sem ganho para quem usa.

### Decision 2: Só a aba ativa fica montada

As abas são renderizadas com `keepMounted={false}`, então o painel
inativo não existe no DOM.

Isso é o que sustenta o modelo de rascunho inteiro: como trocar de aba é
navegação bloqueada quando há alterações pendentes, e confirmar a saída
desmonta o painel, **no máximo uma aba tem rascunho vivo a qualquer
momento**. Nenhum estado precisa subir para a página, nenhuma aba precisa
saber da outra, e não existe o caso de duas abas sujas ao mesmo tempo.

O cache da descoberta de tools não se perde nesse desmonte porque vive no
React Query, com `staleTime` infinito, e não em estado de componente —
decisão que já existe hoje e que esta change preserva.

**Alternativa descartada**: manter as duas abas montadas e permitir dois
rascunhos simultâneos, guardando só na saída da rota. Seria mais
permissivo e bem mais complicado: a barra de salvamento teria que saber de
qual aba fala, e a guarda teria que agregar o estado das duas.

### Decision 3: A guarda e a barra pertencem à aba, não à página

Cada aba com rascunho chama `useUnsavedChangesGuard(isDirty)` e renderiza
a própria barra e o próprio diálogo de descarte. A página não recebe nem
repassa estado de rascunho.

O hook envolve `useBlocker` (troca de aba e saída de rota) e
`useBeforeUnload` (fechar ou recarregar a janela), e devolve apenas o
estado do bloqueio e as ações de confirmar e cancelar. Ele não renderiza
nada: a cópia do diálogo é do consumidor.

O hook e os dois componentes de UI ficam fora da pasta de agentes porque
não têm nenhum conhecimento de domínio — recebem um booleano e texto. É a
camada que o guia de padrões do projeto chama de `hooks/` e
`components/feedback/`.

**Alternativa descartada**: página dona da barra, abas reportando sujeira
por callback ou contexto. Rejeitada porque a Decision 2 já garante uma aba
suja por vez, então o estado compartilhado não compraria nada e a cópia da
barra (que muda por aba) teria que subir junto.

### Decision 4: Redirect da rota antiga por `loader`, sem elemento

A rota `/agents/:id/mcp-servers` continua existindo na árvore, sem
componente, apenas com um `loader` que devolve um `redirect` para
`/agents/:id?tab=ferramentas`.

É o primeiro `loader` do projeto e deve continuar sendo o único: ele não
busca nada, só traduz uma rota morta. O data-layer segue sendo React Query
dentro de cada página, e esta change não abre exceção a isso.

**Alternativa descartada**: um componente que lê o parâmetro da rota e
renderiza `<Navigate>`. Monta um componente só para descartá-lo no
próximo instante, e ainda exige um arquivo novo para algo que cabe em uma
linha na definição da rota.

### Decision 5: A página busca o catálogo de servidores, mas só com a aba ativa

`useMcpServersQuery` ganha um parâmetro opcional `{ enabled }`, no mesmo
formato que `useMcpServerToolsQuery` já usa hoje. O detalhe do agente
chama a query com `enabled` ligado apenas quando a aba de ferramentas está
ativa, e repassa o resultado por prop.

Isso preserva o padrão do projeto — componente de apresentação não busca
dado, a página busca e repassa — sem pagar uma requisição de catálogo em
toda visita ao detalhe, já que a maioria delas nunca abre a aba.

**Alternativa descartada**: a aba buscar o próprio catálogo. Seria lazy de
graça, mas quebraria a fronteira que o projeto mantém desde a change de
delegações, e tornaria a aba mais difícil de testar isoladamente.

### Decision 6: Progresso do salvamento é indeterminado, não por servidor

Durante o salvamento a barra mostra um indicador sem contagem, dizendo que
as tools estão sendo validadas nos servidores MCP.

O protótipo do handoff mostra "validando servidor 2 de 4", mas isso é
ficção: `PUT /agents/{id}/mcp-servers` é uma requisição única e atômica, o
handshake com cada servidor acontece inteiro dentro dela, e o frontend não
recebe nenhum sinal intermediário. Exibir uma contagem seria inventar
progresso que ninguém mediu.

**Alternativa descartada**: simular a contagem no cliente com temporizador,
como o protótipo faz. Rejeitada por mostrar ao operador um número que não
corresponde a nada.

### Decision 7: O card único de detalhe é desmontado, não adaptado

`AgentDetailCard` deixa de existir. Nome, estado e descrição sobem para o
cabeçalho da página, que é comum às três abas; instruções, modelo e datas
viram cards da Visão geral, ao lado do card de skills que já existe; o
resumo textual de servidores vinculados é removido.

O resumo sai porque a aba Ferramentas passa a mostrar a mesma informação
com muito mais precisão — quantos servidores, quantas tools, quais estão
sem tool nenhuma. Manter as duas seria duplicar, com a versão pior sempre
visível.

Os testes que hoje cobrem a renderização de Markdown, a área de rolagem
das instruções e a presença ou ausência de descrição migram para o teste
da aba de Visão geral, sem perder nenhum caso.

### Decision 8: Delegações viram lista com busca, não campo de seleção múltipla

A aba de delegações abandona o `MultiSelect` e passa a listar os agentes
como linhas com checkbox, filtradas por um campo de busca por nome.

Dois motivos. O primeiro é informação: o handoff pede o modelo de cada
agente-alvo na linha, e a marca de inativo, que um campo de seleção
múltipla não tem onde mostrar. O segundo é consistência: as duas abas
passam a ter a mesma anatomia — resumo, busca, lista de checkboxes, barra
de alterações não salvas — e o operador aprende um padrão só.

O contrato com a API não muda: continua sendo a substituição do conjunto
inteiro de ids.

### Decision 9: Marcar o servidor vincula, expande e descobre em um gesto

Na linha do servidor MCP, marcar o checkbox passa a fazer as três coisas:
inclui o servidor no rascunho, expande a área de tools e dispara a
descoberta, se ainda não houver resultado em cache. Hoje vincular e
expandir são gestos separados.

A área expandida continua sendo a mesma máquina de quatro estados que já
existe — carregando, erro com ação de tentar novamente, vazio e lista — e
as tools ficam desabilitadas enquanto o servidor não está marcado.

## Risks / Trade-offs

- [Risco] O tratamento de sessão expirada troca o endereço do documento
  por fora do roteador; com um aviso de `beforeunload` armado, um 401 em
  meio a um rascunho pode disparar o diálogo nativo do browser →
  [Mitigação] aceito. É raro, e o comportamento resultante (avisar antes
  de descartar trabalho) não está errado, só é mais feio que o diálogo do
  próprio painel.
- [Risco] A barra fixa no rodapé pode cobrir conteúdo da aba →
  [Mitigação] a página reserva espaço inferior enquanto a barra está
  visível, como o handoff descreve.
- [Risco] Esquecer de desligar a montagem dos painéis inativos quebraria
  a Decision 2 em silêncio, deixando duas guardas ativas ao mesmo tempo →
  [Mitigação] caso de teste explícito verificando que o conteúdo da aba
  inativa não está no DOM.
- [Trade-off] Trocar de aba com rascunho e confirmar a saída descarta o
  rascunho de vez, sem opção de recuperá-lo. É o preço de não manter duas
  abas montadas, e o aviso torna a perda uma escolha, não um acidente.
- [Risco] Esta é a maior change do redesenho, com muitos arquivos e a
  remoção de uma página inteira → [Mitigação] os requisitos de vínculo
  que não dependem do formato (pré-seleção, descoberta lazy, erro 502,
  falha de descoberta) permanecem intactos nas specs, e seus testes
  migram sem reescrita de comportamento.

## Open Questions

(nenhuma)
