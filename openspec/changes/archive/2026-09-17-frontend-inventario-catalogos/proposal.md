## Why

O painel não tem tela de entrada. A rota raiz redireciona para `/agents`
(`apps/frontend/src/app/routes.tsx:38`), e quem abre o sistema cai na listagem de
agentes.

**O que falta não é a contagem — é o sinal de saúde.** As quatro contagens já
estão na tela hoje, cada uma no subtítulo da sua listagem
(`AgentListPage.tsx:62-67`, `McpServerListPage.tsx:31-37`,
`KnowledgeBaseListPage.tsx:108-113`, `ChannelListPage.tsx:38-43`), na mesma
gramática. Ninguém conta linha: o número sai pronto. O que custa é a navegação —
três cliques para ver os quatro.

Poupar três navegações não paga uma rota nova. O que paga é o que o inventário
carrega junto: **os quatro catálogos consultados ao mesmo tempo, com o estado de
cada consulta visível.** Hoje um `apps/inbox` fora do ar só aparece para quem
clica em Canais; um catálogo vazio, só para quem entra nele. A tela existe para
que "está tudo de pé?" seja respondida ao abrir, e não descoberta navegando.

**Esta proposta registra isso explicitamente: a tela entra pelo sinal de saúde
por catálogo, não pelo valor do inventário.** O inventário é o que ela consegue
afirmar hoje; os quadros de atividade que responderiam a pergunta da manhã
dependem de backend que não existe, e ficam fora com destino registrado no
`design.md`.

## What Changes

- Entra a rota `/inventory` com a tela de inventário, e a rota raiz passa a
  redirecionar para ela em vez de para `/agents`. **Muda o comportamento de
  entrada de todo operador.**
- A barra lateral ganha um quinto item de navegação, "Inventário", na primeira
  posição. O item existe porque a página passa a existir — que é a condição de
  reabertura escrita na decisão que o removeu em 2026-07-27
  (`openspec/changes/archive/2026-07-27-frontend-cadastro-agentes/design.md:146-153`).
- A tela exibe um card por catálogo — agentes, servidores MCP, bases de
  conhecimento e canais —, cada um com a contagem, o estado da consulta e um
  atalho para a listagem correspondente.
- Cada card **carrega a proveniência da sua contagem**: contagem medida que deu
  zero é dita em palavras, consulta que não respondeu é dita como desconhecida
  com o motivo, consulta em andamento não afirma nenhuma das duas.
- Cada card **falha e recarrega sozinho**. Um catálogo indisponível não impede os
  outros três de exibir contagem, e oferece nova tentativa que refaz só a consulta
  dele.
- O card **não reproduz a frase explicativa de nenhuma listagem** — só rótulo,
  contagem e atalho.
- `LoginPage` passa a navegar para `/` em vez de `/agents`
  (`features/auth/pages/LoginPage.tsx:20`), para que o destino da entrada tenha
  uma definição só, em `routes.tsx`. Sem isso, o caminho de entrada dominante —
  toda expiração de token força `/login` a partir de qualquer tela — nunca
  passaria pela tela nova.

Fora de escopo: mensagens processadas, sessões ativas, sessões por período e
armazenamento consolidado de dados agregados. Os quatro exigem backend que não
existe; o `design.md` registra o que cada um pede e o bar do último.

## Capabilities

### New Capabilities
- `catalog-inventory-ui`: a tela de inventário como contrato — um item por
  catálogo com a contagem, os quatro estados de proveniência daquela contagem,
  o comportamento independente de falha e recarga por catálogo, a recusa de
  reproduzir a explicação das listagens, e a rota raiz como ponto de entrada.

  **O nome é `catalog-inventory-ui` e não `dashboard-ui` de propósito.**
  "Dashboard" promete atividade, e os quadros de atividade estão fora por falta
  de backend. Uma capability que afirmasse atividade no próprio nome seria a
  convenção 13 quebrada no vocabulário do processo — e o nome `dashboard-*` fica
  livre para o dia em que a atividade existir, que é outra capability com outras
  dependências.

  **O mesmo argumento vale para o identificador desta change, e por isso ela se
  chama `frontend-inventario-catalogos`** (padrão `frontend-<descrição>`, como
  `frontend-cadastro-agentes` e `frontend-shell-navegacao-e-icones`). O nome da
  change é a **primeira** coisa lida — num `grep`, no histórico de `archive/`, no
  reconhecimento de uma linha numa lista — e é lida **antes** de qualquer arquivo
  que explique o que a entrega é. Um identificador `dashboard-*` puxaria a
  expectativa de atividade em todos esses lugares, e o `proposal.md` só a
  desfaria depois de aberto. Era o terceiro lugar onde a palavra aparecia; os
  outros dois — rótulo e capability — já a tinham recusado pelo mesmo motivo.

### Modified Capabilities
Nenhuma. As três specs vivas que pareciam alvo foram lidas e **já estão
satisfeitas pelo texto que têm**:

- `frontend-scaffold` — o requisito de roteamento diz que a raiz redireciona para
  "a primeira feature disponível" (`spec.md:79-80`) e o cenário diz "uma feature
  existente" (`:88-90`). Nunca nomeou `/agents`. O que ele protege — "sem exibir
  uma página vazia" — continua verdadeiro.
- `operator-login-ui` — diz "a área autenticada" (`spec.md:12-13`) e "a página
  inicial autenticada" (`:20-21`). Também nunca nomeou `/agents`. O código muda;
  a spec volta a ser verdadeira sozinha.
- `frontend-app-shell` — o requisito de navegação já exige "um item para cada
  área do painel que possua página real" (`spec.md:33`). Com a página, o quinto
  item passa a ser **obrigatório** pela spec vigente, sem alteração.

## Impact

Afeta **apenas `apps/frontend`**. Nenhuma mudança em `apps/api`, `apps/inbox`,
`apps/workers` ou em contrato de rota HTTP. Nenhuma rota nova de backend: as
quatro consultas já existem e já são consumidas pelo painel.

- Uma feature nova (`features/inventory`) com uma página e o teste dela.
- Três arquivos existentes modificados, cada um arrastando o teste dele: a árvore
  de rotas, a casca e a página de login.
- **A disponibilidade de `apps/inbox` entra no caminho de entrada.** Hoje abrir o
  painel dispara uma requisição a um processo; passa a disparar quatro a dois.
  Risco declarado e aceito no `design.md`, com as razões.
- Risco de regressão concentrado na casca, que aparece em todas as telas — mas é
  um item de navegação a mais, não uma reescrita.
- Conferência visual manual é tarefa própria (convenção 14): a suíte roda em
  jsdom e não enxerga a grade responsiva nem contraste.
