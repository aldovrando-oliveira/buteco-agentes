Todas as tarefas desta change rodam em **apps/frontend**. Nenhuma toca
`apps/api`, `apps/workers` ou `apps/inbox`.

## 1. Guarda de navegação e chrome compartilhado (apps/frontend)

- [x] 1.1 Criar `src/hooks/useUnsavedChangesGuard.ts`: recebe um booleano
  de rascunho sujo, envolve `useBlocker` (bloqueia quando sujo e o destino
  difere da localização atual, incluindo mudança só de query param) e
  `useBeforeUnload` (aviso nativo ao fechar ou recarregar), e devolve
  `{ isBlocked, confirmNavigation, cancelNavigation }`. O hook não
  renderiza nada — a cópia do diálogo é do consumidor (Decision 3 do
  design.md).
- [x] 1.2 Criar `src/hooks/useUnsavedChangesGuard.test.tsx` com um
  componente-harness dentro de `createMemoryRouter`, cobrindo: navegação
  livre quando não há rascunho; navegação bloqueada quando há; confirmar
  prossegue; cancelar permanece; troca apenas de query param também é
  bloqueada.
- [x] 1.3 Criar `src/components/feedback/UnsavedChangesBar.tsx`:
  apresentacional, fixo no rodapé, recebe a mensagem, o rótulo da ação de
  salvar, o estado de salvamento e os dois callbacks. Durante o
  salvamento exibe indicador sem contagem e mantém a ação de salvar
  inerte (Decision 6). Teste ao lado.
- [x] 1.4 Criar `src/components/feedback/UnsavedChangesModal.tsx`:
  apresentacional, recebe `{ opened, onConfirm, onCancel }` e a cópia,
  com ações de continuar editando e de sair descartando. Teste ao lado.

## 2. Rota antiga (apps/frontend)

- [x] 2.1 Em `src/app/routes.tsx`, trocar o elemento da rota
  `agents/:id/mcp-servers` por um `loader` que devolve `redirect` para
  `/agents/{id}?tab=ferramentas`, sem componente (Decision 4).
- [x] 2.2 Em `src/app/router.test.tsx`, adicionar caso montando a árvore a
  partir de `/agents/{id}/mcp-servers` e verificando que a aba Ferramentas
  do detalhe é exibida, sem nenhuma página de gestão separada.

## 3. Host de abas (apps/frontend)

- [x] 3.1 Reescrever `src/features/agents/pages/AgentDetailPage.tsx` como
  host: cabeçalho com nome, badges de estado e reconfiguração, descrição
  (ou a indicação de ausência), link de editar e ação de ativar/desativar
  com o modal de confirmação que já existe; abaixo, as três abas.
- [x] 3.2 Ler e escrever a aba ativa na URL com `useSearchParams`
  (`?tab=ferramentas`, `?tab=delegacoes`); ausência ou valor desconhecido
  renderiza a visão geral sem reescrever o endereço (Decision 1).
- [x] 3.3 Configurar as abas com `keepMounted={false}`, de forma que só o
  painel ativo exista no DOM (Decision 2), e exibir os contadores de
  servidores vinculados e de agentes-alvo, ocultos quando zero.
- [x] 3.4 Em `src/features/mcp-servers/api/useMcpServers.ts`, dar a
  `useMcpServersQuery` um parâmetro opcional `{ enabled }`, no mesmo
  formato de `useMcpServerToolsQuery`; a página passa `enabled` ligado só
  quando a aba de ferramentas está ativa (Decision 5). Cobrir no teste do
  hook.
- [x] 3.5 Atualizar `AgentDetailPage.test.tsx`: montagem com
  `createMemoryRouter`; as três abas aparecem; contadores presentes e
  ocultos; aba refletida na URL e restaurada ao recarregar; parâmetro
  ausente e desconhecido caem na visão geral; conteúdo da aba inativa
  ausente do DOM; nenhum link para a rota removida.

## 4. Aba Visão geral (apps/frontend)

- [x] 4.1 Criar `src/features/agents/components/AgentOverviewTab.tsx`:
  grade de duas colunas com o card de instruções (Markdown com a mesma
  área de rolagem de altura mínima que existe hoje) de um lado e, do
  outro, os cards de modelo, de skills (reusando `AgentSkillsCard`) e de
  datas (Decision 7).
- [x] 4.2 Migrar para `AgentOverviewTab.test.tsx` os casos hoje em
  `AgentDetailCard.test.tsx` que continuam valendo: Markdown renderizado
  como formatação real, tabela do remark-gfm, área de rolagem com altura
  e altura mínima, provider e model exibidos, indicador de reconfiguração
  presente e ausente.
- [x] 4.3 Remover `AgentDetailCard.tsx` e `AgentDetailCard.test.tsx`. Os
  casos de descrição presente e ausente passam a viver no teste do
  cabeçalho, em `AgentDetailPage.test.tsx`; os de resumo de servidores MCP
  vinculados são descartados junto com o resumo (Decision 7).

## 5. Aba Ferramentas (apps/frontend)

- [x] 5.1 Criar `src/features/agents/components/AgentToolsTab.tsx` a
  partir do `AgentMcpServersManager` que hoje vive dentro de
  `AgentMcpServersPage.tsx`: recebe `agent` e o catálogo de servidores por
  prop, mantém o rascunho `{ [serverId]: string[] }` inicializado de
  `agent.mcpServers`, e mantém a poda de tools que a descoberta não
  retorna mais.
- [x] 5.2 Adicionar o resumo do topo (quantidade de servidores vinculados
  e de tools permitidas) e o estado vazio com a ação de cadastrar
  servidor MCP quando o catálogo está vazio.
- [x] 5.3 Adicionar o aviso de vínculo sem tools: indicador na linha de
  cada servidor vinculado com `allowedTools` vazio e aviso no topo da aba
  nomeando todos eles; nenhum aviso quando todo vinculado tem ao menos
  uma tool, e nenhum para servidor não vinculado.
- [x] 5.4 Reescrever `AgentMcpServerRow.tsx`: linha com checkbox, nome,
  url, badges de inativo e de sem tools, contador "{k} de {n}
  selecionadas" ou indicação de não vinculado, e ação de ver/ocultar
  tools. Marcar o checkbox vincula, expande e dispara a descoberta no
  mesmo gesto (Decision 9). Manter os quatro estados da descoberta e as
  tools desabilitadas enquanto o servidor não está marcado. Adicionar o
  aviso recuado quando o servidor vinculado está inativo.
- [x] 5.5 Calcular o diff normalizado (conjunto de servidores e de tools,
  ordem irrelevante) contra `agent.mcpServers` e renderizar
  `UnsavedChangesBar` só quando houver diferença, com a ação de descartar
  restaurando o rascunho ao vínculo salvo.
- [x] 5.6 Ligar `useUnsavedChangesGuard` ao estado sujo e renderizar
  `UnsavedChangesModal` a partir do resultado.
- [x] 5.7 No submit: em sucesso, notificar, descartar o rascunho e
  permanecer na aba, sem navegar; em 502, exibir o alerta no topo da aba
  identificando o servidor e o motivo, mantendo o rascunho intacto.
- [x] 5.8 Criar `AgentToolsTab.test.tsx` migrando os casos de
  `AgentMcpServersPage.test.tsx` que continuam valendo (pré-seleção,
  descoberta lazy, poda de tools inexistentes, 502 preservando a seleção,
  falha de descoberta não bloqueando a seleção) e acrescentando os novos:
  resumo, catálogo vazio com ação de cadastro, aviso de vínculo sem tools
  em um e em vários servidores, aviso de servidor inativo vinculado,
  contador por servidor, barra aparecendo e sumindo conforme o diff,
  descartar, permanência na aba após salvar, e progresso sem contagem.
- [x] 5.9 Reescrever `AgentMcpServerRow.test.tsx` para o novo
  comportamento, incluindo marcar o checkbox disparando a descoberta.
- [x] 5.10 Remover `AgentMcpServersPage.tsx` e
  `AgentMcpServersPage.test.tsx`.

## 6. Aba Delegações (apps/frontend)

- [x] 6.1 Criar `src/features/agents/components/AgentDelegationsTab.tsx`
  substituindo o campo de seleção múltipla por lista de linhas com
  checkbox, cada uma exibindo o nome do agente e, à direita, o modelo
  configurado ou a marca de inativo (Decision 8). Manter a exclusão do
  próprio agente e a ausência de detecção de ciclo.
- [x] 6.2 Adicionar o campo de busca por nome, filtrando sem afetar a
  seleção, com indicação própria quando nada corresponde, e o resumo da
  quantidade de agentes-alvo selecionados.
- [x] 6.3 Renderizar `UnsavedChangesBar` a partir do diff normalizado
  contra `agent.delegatesTo`, com descartar restaurando a seleção, e
  ligar `useUnsavedChangesGuard`.
- [x] 6.4 Criar `AgentDelegationsTab.test.tsx` migrando os casos de
  `AgentDelegationsSection.test.tsx` que continuam valendo (pré-seleção,
  próprio agente ausente, catálogo com um único agente, agente inativo
  selecionável, submit com e sem alvos, erro genérico) e acrescentando os
  novos: modelo exibido na linha, busca filtrando, busca sem
  correspondência, filtro não alterando a seleção, barra conforme o diff,
  descartar.
- [x] 6.5 Remover `AgentDelegationsSection.tsx` e
  `AgentDelegationsSection.test.tsx`.

## 7. Verificação (apps/frontend)

- [x] 7.1 Rodar a suíte completa e confirmar que tudo passa, sem nenhum
  teste remanescente referenciando a rota removida.
- [x] 7.2 Rodar lint e typecheck e confirmar que não há erros
  introduzidos.
- [x] 7.3 Rodar o build de produção e confirmar que compila.
- [x] 7.4 Subir a aplicação e conferir manualmente o caminho crítico:
  trocar de aba com e sem rascunho, salvar o vínculo permanecendo na aba,
  abrir um link antigo de `/agents/{id}/mcp-servers`, e recarregar a
  página com a aba de ferramentas ativa.
