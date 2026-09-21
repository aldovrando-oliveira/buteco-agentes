Todas as tarefas rodam em **`apps/frontend`**, salvo as de fechamento que editam
documentação de raiz — indicado em cada uma.

## 1. Baseline e reconferência

- [x] 1.1 (`apps/frontend`) Confirmar a baseline sobre a árvore limpa, com
  `uptime` na largada. Referência medida **nesta sessão**, não herdada:
  **921/921 em 84 arquivos, 73,3 s, load 2,78**. Divergência é achado a explicar
  antes de tocar arquivo.
- [x] 1.2 (`apps/frontend`) Reconferir as quatro afirmações de que o `design.md`
  depende: que `onError` em `AgentDelegationsTab.tsx:68` **não recebe
  parâmetro**; que `AgentToolsTab` tem o ramo específico → estado → `return` cedo
  → genérico; que `<Notifications />` em `main.tsx` **não** sobrescreve
  `autoClose`; e que os quatro 400 da rota saem sob a mesma chave
  `targetAgentIds`. Divergência corrige o `design.md` (convenção 9), não o
  código.

## 2. Guardas — vermelhos ANTES da correção

- [x] 2.1 (`apps/frontend`) Helper local no arquivo de teste que constrói a
  rejeição: um `ApiError` real (o módulo já é mockado com `importOriginal`, então
  a classe verdadeira está disponível) com `status: 400` e
  `problem.errors.targetAgentIds`. Um só arranjo serve aos quatro guardas de
  recusa.
- [x] 2.2 (`apps/frontend`) **G1** — recusa por ciclo: a mensagem que a API
  mandou, **com o caminho**, aparece na tela. Usar um caminho no formato real
  (`A → B → C → A`), não um texto qualquer.
- [x] 2.3 (`apps/frontend`) **G2** — a negativa que prende o defeito: naquele
  caminho, **o texto genérico de "Tente novamente" não aparece**. Sem ela,
  acrescentar a mensagem nova sem remover a velha passa verde.
- [x] 2.4 (`apps/frontend`) **G3** — outra recusa 400 sob a mesma chave
  (auto-delegação) também é exibida, pelo mesmo caminho. É o guarda que prova que
  o ramo **não é específico de ciclo** e que ninguém vai reintroduzir casamento
  de texto.
- [x] 2.5 (`apps/frontend`) **G4** — falha transitória (erro sem `problem`, ou
  status 5xx) mantém a notificação genérica com "Tente novamente", e **não**
  exibe aviso de recusa. **Este guarda passa em `HEAD`**, de propósito: é de
  regressão contra a correção errada (trocar o genérico em vez de acrescentar um
  ramo). A observação vai escrita ao lado dele, como a convenção 15 exige.
- [x] 2.6 (`apps/frontend`) **G5** — salvamento bem-sucedido depois de uma recusa
  **remove** o aviso. **G6** — descartar **remove** o aviso. Os dois são a
  negativa de D3: aviso que sobrevive afirma uma recusa que já não vale.
- [x] 2.7 (`apps/frontend`) Rodar os seis contra `HEAD` e **registrar a causa
  atribuída de cada resultado**, com `uptime` na largada. Esperado: G1, G2, G3,
  G5 e G6 🔴; **G4 🟢**. Um vermelho em G4 é guarda errado e volta para 2.5.

## 3. A correção

- [x] 3.1 (`apps/frontend`) `onError` passa a receber o erro, e o ramo específico
  dispara por **400 com mensagem sob `targetAgentIds`** — nunca por casamento de
  texto. Estado local `refusal`, `return` cedo, genérico para o resto. Molde de
  `AgentToolsTab`, não invenção.
- [x] 3.2 (`apps/frontend`) Renderizar o aviso como `<Alert color="red">` acima
  da lista, com `data-testid`, exibindo a mensagem **inteira e sem
  interpretação**, quebrando linha para nome longo.
- [x] 3.3 (`apps/frontend`) Limpar `refusal` antes de cada submit, no `onSuccess`
  e no descarte (D3).
- [x] 3.4 (`apps/frontend`) Três registros de mecanismo no arquivo, que são o
  entregável desta change tanto quanto o código: por que o ramo é pela **chave** e
  não pelo texto (com o precedente de guarda que reprovou por texto); os **4 s**
  medidos do `autoClose` default do Mantine, com arquivo e linha, que é o que
  decide `Alert` em vez de notificação; e por que a tela **não parseia** o
  caminho (nome de agente é texto livre e pode conter o separador).
- [x] 3.5 (`apps/frontend`) Rodar os seis guardas e registrar verde, com `uptime`
  na largada.

## 4. Conferência manual (convenção 14)

- [x] 4.1 (`apps/frontend`) Conferir **à mão**, nos **dois** esquemas de cor, com
  a stack de desenvolvimento de pé: a aba com o aviso de recusa exibindo um
  caminho de ciclo real. jsdom não enxerga cor, contraste nem layout.
  **Verificado pelo dono em 20/09/2026: legível nos dois esquemas.**
- [x] 4.2 (`apps/frontend`) Caso deliberado de **nome longo** — três agentes com
  nomes longos formando ciclo — para ver se o `Alert` quebra linha em vez de
  cortar o caminho. É o risco nomeado no `design.md`.
  **Verificado pelo dono em 20/09/2026, e era a task que decidia a change:** o
  caminho de **234 caracteres** do cenário B sai **inteiro**, em quatro linhas,
  quebrando **por palavra** — sem reticências, sem corte no meio de nome e sem
  rolagem horizontal. **O risco nomeado no `design.md` não se materializou.**
- [x] 4.3 (`apps/frontend`) Conferir que a notificação de erro genérica **ainda
  aparece** numa falha transitória de verdade (API desligada), e que o aviso de
  recusa **não** aparece nesse caso. A conferência é **iterativa**: cada correção
  muda o que fica visível.
  **Verificado pelo dono em 20/09/2026:** notificação genérica presente, e
  **nenhum `Alert`** na página. **E mais que o pedido:** a captura é do mesmo
  agente que acabara de exibir a recusa do cenário B, então a sequência
  **recusa → falha transitória** foi exercitada de fato e o aviso foi limpo
  corretamente. Essa sequência **não tem guarda na suíte** — ver item aberto.

## 5. Conferência de escopo

- [x] 5.1 (raiz) Conferência de escopo de arquivo por `git status`/`git diff
  --stat`. Suíte verde não prova que um arquivo não foi tocado. Caminhos
  permitidos, e **nenhum outro**:
  - `apps/frontend/src/features/agents/components/AgentDelegationsTab.tsx`
  - `apps/frontend/src/features/agents/components/AgentDelegationsTab.test.tsx`
  - `openspec/changes/frontend-mensagem-recusa-ciclo/**`
  - `openspec/specs/agent-delegation-binding-ui/spec.md` (na sincronização)
  - `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md` (fechamento)
- [x] 5.2 (raiz) Afirmar **zero** arquivo tocado em `apps/api`, `apps/workers`,
  `apps/inbox`, `libs/`, `deploy/`, `docs/`, `docker-compose*`, e **zero** em
  `apps/frontend/src/features/agents/utils/knowledgeBaseRows.ts` — o item que V1
  deliberadamente não puxou.
- [x] 5.3 (`apps/frontend`) `npx tsc --noEmit` (ou o script equivalente do
  projeto) como **conferência**, não como enumerador: esta change não muda
  assinatura nenhuma, e é isso que o `tsc` tem de confirmar.

## 6. Fechamento

- [x] 6.1 (`apps/frontend`) Suíte completa, com `uptime` na largada. Projetado:
  **927** (baseline 921 + 6). Diferença é achado a explicar, não número a
  ajustar.
- [x] 6.2 (raiz) `openspec validate --all` verde.
- [x] 6.3 (raiz) **Comparar** o entregue com a projeção do `design.md` — apenas
  comparar. Diffstat decomposto (produção × teste, lógica × comentário), nunca o
  headline do commit. **Duas perguntas específicas da nona medição:** a mistura
  comentário:lógica ficou perto de **4,2:1** (a âncora da 5a-4, cujo entregável
  também era uma recusa)? E a contagem de **zero unidades públicas novas** se
  manteve?
- [x] 6.4 (raiz) Registrar se as duas direções de erro nomeadas aconteceram, e
  se o desvio veio de um terceiro lugar **pela terceira vez seguida** — nesse
  caso, isso é o achado da nona medição e vale mais que o número.
- [x] 6.5 (raiz) **Corrigir o item do terceiro comparador de ordem no `02`**: a
  premissa registrada (*"a reordenação no cliente ficou desnecessária"*) omite a
  razão que o próprio arquivo carrega (o sort serve o **rascunho**, D4 de outra
  change). É convenção 6 aplicada ao registro interno, e é trabalho desta change
  porque foi ela que consumiu o item.
- [x] 6.6 (raiz) Registrar como item aberto, com gatilho **e** posição: as **oito
  cópias** de `fieldErrorsFrom` e a decisão de convenção 7 que governa a extração
  (base comum contra tipagem estrutural); e as frases defasadas restantes de
  `agent-delegation-binding-ui` em requisitos que esta change não reescreveu.
- [x] 6.7 (raiz) `CHANGELOG.md` e seção própria no `02`, com a distinção
  permanente × transitório e o motivo de o ramo ser pela chave.
- [x] 6.8 (raiz) Atualizar `## Próximo passo`: posição 4 aplicada, e a fila passa
  a **limpeza do banco + deploy → OBSERVAR**. **Sem commit** — o trabalho fica na
  árvore e o commit é decisão de quem revisa.
