## 1. Tipos e estado de servidor (apps/frontend)

- [x] 1.1 Em `features/agents/types/agent.ts`: adicionar `isActive: boolean`
      a `Agent`; adicionar `export type UpdateAgentInput = CreateAgentInput`.
- [x] 1.2 Em `features/agents/api/agentsApi.ts`: adicionar `updateAgent(id:
      string, input: UpdateAgentInput): Promise<Agent>` (`PUT
      /agents/{id}`), `activateAgent(id: string): Promise<Agent>` (`POST
      /agents/{id}/activate`) e `deactivateAgent(id: string): Promise<Agent>`
      (`POST /agents/{id}/deactivate`).
- [x] 1.3 Em `features/agents/api/useAgents.ts`: adicionar
      `useUpdateAgentMutation()`, `useActivateAgentMutation()` e
      `useDeactivateAgentMutation()`, cada um com `onSuccess` fazendo
      `setQueryData(['agents', updated.id], updated)` +
      `invalidateQueries({ queryKey: ['agents'] })` (mesmo padrão de
      `useCreateAgentMutation`).

## 2. Componentes reaproveitados e atualizados (apps/frontend)

- [x] 2.1 Em `features/agents/components/AgentForm.tsx`: adicionar props
      opcionais `initialValues?: CreateAgentInput` (default `{ name: '',
      instructions: '' }`) e `submitLabel?: string` (default `'Criar
      agente'`), usadas em `useForm({ initialValues: ... })` e no texto do
      `Button`. Não alterar o comportamento default (sem props, form deve
      se comportar exatamente como hoje).
- [x] 2.2 Em `features/agents/components/AgentDetailCard.tsx`: exibir um
      `Badge` com o estado do agente (`color="green"`/`"Ativo"` quando
      `isActive`, `color="gray"`/`"Inativo"` caso contrário). Não adicionar
      nenhuma prop de callback (`onEdit`, `onActivate`, `onDeactivate`) —
      o componente continua puramente apresentacional.
- [x] 2.3 Em `features/agents/components/AgentTable.tsx`: adicionar coluna
      de estado exibindo o mesmo `Badge` (`color`/rótulo) por linha, usando
      `isActive` de cada agente.

## 3. Página de edição (apps/frontend)

- [x] 3.1 Criar `features/agents/pages/AgentEditPage.tsx` em
      `/agents/:id/edit`: carrega o agente via `useAgentQuery(id)`, exibe
      `AgentForm` com `initialValues` do agente carregado e
      `submitLabel="Salvar alterações"` somente depois que os dados
      chegarem (mesmo gate de loading/erro de `AgentDetailPage`).
- [x] 3.2 Em `AgentEditPage.tsx`: no submit, chamar
      `useUpdateAgentMutation()`; em sucesso, notificar (`@mantine/notifications`,
      mensagem de "atualizado") e navegar para `/agents/{id}`; em erro 400
      com `ValidationProblemDetails`, mapear para os campos do formulário
      (mesma função `fieldErrorsFrom` já usada em `AgentCreatePage`); em
      qualquer outro erro, notificar erro genérico e manter os dados
      preenchidos, sem navegar.
- [x] 3.3 Em `app/router.tsx`: adicionar a rota `agents/:id/edit` apontando
      para `AgentEditPage`.

## 4. Página de detalhe: link de edição e ativar/desativar (apps/frontend)

- [x] 4.1 Em `features/agents/pages/AgentDetailPage.tsx`: adicionar um
      link/botão "Editar" (`Link to={`/agents/${id}/edit`}`) ao lado do
      `AgentDetailCard`.
- [x] 4.2 Em `AgentDetailPage.tsx`: exibir a ação "Ativar" (chama
      `useActivateAgentMutation()` diretamente, sem confirmação) quando
      `data.isActive === false`, ou a ação "Desativar" quando
      `data.isActive === true` — nunca as duas ao mesmo tempo.
- [x] 4.3 Em `AgentDetailPage.tsx`: implementar o fluxo de confirmação de
      desativação com `useDisclosure(false)` (de `@mantine/hooks`) e
      `Modal` (de `@mantine/core`): clicar em "Desativar" abre o modal
      (explicando que mensagens recebidas enquanto o agente estiver
      inativo serão rejeitadas); "Cancelar" fecha sem chamar a mutation;
      "Confirmar desativação" chama `useDeactivateAgentMutation()`, fecha o
      modal e notifica.
- [x] 4.4 Em `AgentDetailPage.tsx`: notificar sucesso/erro de ativar e de
      desativar via `@mantine/notifications`, com mensagens distintas
      ("ativado" / "desativado") e um caso de erro genérico para falha de
      rede/servidor em qualquer uma das duas ações.

## 5. Testes (apps/frontend)

- [x] 5.1 `AgentForm.test.tsx`: estender para cobrir `initialValues`
      pré-preenchendo os campos do formulário, e `submitLabel` alterando o
      texto do botão de submit; confirmar que, sem essas props, o
      comportamento atual (campos vazios, botão "Criar agente") não muda.
- [x] 5.2 `AgentTable.test.tsx` (ou `AgentListPage.test.tsx`, se os testes
      de tabela viverem lá): cobrir o indicador de estado por linha —
      (a) agente ativo exibe o indicador de "ativo" na lista carregada com
      sucesso (Scenario "Lista carregada com sucesso"); (b) agente ativo e
      agente inativo lado a lado exibem indicadores visuais distintos
      (Scenario "Indicador distingue agente inativo na lista").
- [x] 5.3 Criar `AgentEditPage.test.tsx` com os três cenários já usados em
      `AgentCreatePage.test.tsx`: (a) formulário pré-preenchido com os
      dados do agente carregado e sucesso navegando para `/agents/{id}`
      com notificação de sucesso; (b) erro 400 aplicando mensagens nos
      campos certos, sem navegar; (c) falha de rede/servidor notificando
      erro genérico, sem navegar, preservando os dados já editados no
      formulário.
- [x] 5.4 Reescrever o teste de `AgentDetailPage.test.tsx` que hoje afirma
      "sem controles de edição/exclusão" (`queryByRole` para os botões
      "editar"/"excluir" não existirem) — essa asserção não é mais
      verdadeira. Adaptar o teste "exibe nome, instruções e datas" para
      também afirmar, no cenário de sucesso (Scenario "Detalhe carregado
      com sucesso"): a presença do indicador de `isActive`, do link
      "Editar" apontando para `/agents/{id}/edit`, e da ação de
      ativar/desativar correta conforme o estado do agente carregado.
- [x] 5.5 Em `AgentDetailPage.test.tsx`: adicionar cenário cobrindo agente
      inativo exibindo a ação "Ativar" (e não "Desativar"), e agente ativo
      exibindo a ação "Desativar" (e não "Ativar").
- [x] 5.6 Em `AgentDetailPage.test.tsx`: adicionar cenário de ativação —
      clicar em "Ativar" chama `activateAgent` imediatamente (sem nenhum
      modal/diálogo), exibe notificação de sucesso e atualiza o indicador
      de estado exibido.
- [x] 5.7 Em `AgentDetailPage.test.tsx`: adicionar cenários de
      desativação cobrindo o fluxo do modal de confirmação: (a) clicar em
      "Desativar" abre o modal sem chamar `deactivateAgent`; (b) cancelar
      no modal fecha o diálogo sem chamar `deactivateAgent` e mantém o
      agente exibido como ativo; (c) confirmar no modal chama
      `deactivateAgent`, exibe notificação de sucesso e atualiza o
      indicador de estado exibido para inativo.
- [x] 5.8 Em `AgentDetailPage.test.tsx`: adicionar cenário de falha de
      rede/servidor ao ativar ou desativar — notifica erro genérico e
      mantém o indicador de estado igual ao estado anterior à tentativa.

## 6. Validação (apps/frontend)

- [x] 6.1 Rodar `npm run lint`, `npm run format:check` e a suíte Vitest
      completa (incluindo `AgentCreatePage.test.tsx` e
      `AgentListPage.test.tsx` já existentes, para confirmar ausência de
      regressão).
- [x] 6.2 Validar manualmente o fluxo ponta a ponta local (`docker compose
      up -d`, `dotnet run` em `apps/api`, `npm run dev` em
      `apps/frontend`): editar um agente existente, ativar/desativar
      (incluindo o modal de confirmação ao desativar) e conferir o
      indicador de estado atualizado na lista e no detalhe.
