## Why

Posição **4** da fila em `02-HISTORICO_E_STATUS.md` → `## Próximo passo`, e a
**última change antes da limpeza do banco e do deploy**. As três anteriores
(`lock-de-contexto-falha-terminal`, `delegacao-ciclo-no-cadastro`,
`delegacao-diagnostico`) estão aplicadas.

`AgentDelegationsTab.tsx:68-74` responde a **qualquer** erro de
`PUT /agents/{id}/delegations` com o mesmo texto:

> *"Não foi possível atualizar as delegações do agente. Tente novamente."*

Desde `delegacao-ciclo-no-cadastro`, a API recusa ciclo com **400
`ValidationProblem` sob `targetAgentIds`**, carregando o caminho completo pelos
nomes dos agentes. Para essa recusa a tela afirma mais do que o sistema sabe
(convenção 13) — **tentar de novo nunca vai funcionar** — e joga fora a única
informação que resolveria o problema, que a API já está mandando: o
`onError: () => {...}` **descarta o argumento de erro inteiro**.

O defeito é o genérico engolir o específico, não a ausência de mensagem.

**E o genérico não é lacuna: é requisito escrito.**
`agent-delegation-binding-ui` diz *"O sistema SHALL exibir, quando `PUT
/agents/{id}/delegations` falhar, uma notificação de erro **genérica**"*, com
cenário correspondente. Corrigir passa por **inverter** requisito e cenário —
mesma forma de `delegacao-ciclo-no-cadastro`, e a diferença aparece no diff.

## What Changes

**Não é um ramo para ciclo. É um ramo para recusa permanente**, e a verificação
é o que decidiu isso.

- **`apps/frontend` — a aba de delegações distingue recusa permanente de falha
  transitória.** Quando a resposta é **400 com mensagem sob `targetAgentIds`**, a
  tela exibe **a mensagem que a API mandou**, num `Alert` que permanece; todo o
  resto (rede, 5xx, 404) continua na notificação genérica com "Tente novamente",
  onde ela está correta.

- **O ramo é pela CHAVE do `ValidationProblem`, nunca pelo texto da mensagem.**
  Verificado no endpoint: os **quatro** 400 dessa rota saem como
  `ValidationProblem` sob a **mesma chave** `targetAgentIds` — conjunto nulo,
  auto-delegação, ciclo e ids inexistentes —, e os quatro já têm texto de
  operador pronto. Um ramo específico para ciclo precisaria casar texto, que é
  frágil por construção e deixaria os outros três caindo no genérico. Um ramo só
  cobre os quatro e não lê o conteúdo da mensagem.

- **A mensagem vai num `Alert` persistente, não na notificação.** Medido no
  pacote instalado (`@mantine/notifications` 9.4.2): `<Notifications />` usa o
  default `autoClose: 4e3`, e o app não o sobrescreve — **4 segundos**. O
  caminho do ciclo é a informação que o operador precisa ler para agir, e um
  toast que se fecha sozinho a mostra e a tira.

- **O idioma é reusado, não inventado.** `AgentToolsTab` — aba **irmã da mesma
  feature**, e a aba em que esta foi modelada — já tem exatamente esta forma:
  ramo específico que grava estado persistente e faz `return` cedo, caindo no
  `notifications.show` genérico para o resto, renderizado como
  `<Alert color="red" title={…} data-testid=…>`.

- **A tela não interpreta o caminho do ciclo.** Ela exibe a string como veio.
  Nome de agente é texto livre, então um nome contendo `→` tornaria o caminho
  ambíguo se alguém tentasse parseá-lo — e parsear criaria segunda fonte de
  verdade sobre uma regra que é do servidor.

**Dois achados de verificação que NÃO viram escopo aqui, e ficam registrados
com posição:**

1. **`fieldErrorsFrom` tem OITO cópias** em `apps/frontend` — 6 byte-a-byte
   idênticas e 2 variantes em `channels`, que divergem por razão de **domínio**
   (separam erros de credencial). O gatilho da convenção 2 está cumprido oito
   vezes. **Não é extraído aqui**, e por dois motivos independentes: cada feature
   tem seu **próprio** `ApiError` (6 classes verificadas — convenção 7), então um
   helper compartilhado não pode usar `instanceof` sem base comum ou tipagem
   estrutural; e **esta change não usa `fieldErrorsFrom`** — aquele helper mapeia
   erros para campos de formulário, e esta aba não tem campo nenhum a que
   pendurar erro.
2. **O terceiro comparador de ordem** (`knowledgeBaseRows.ts:26-27`), item com
   gatilho imediato e posição de `apps/frontend`, **não é puxado** — a premissa
   dele não se sustenta lida contra a árvore. O item corrigido entra no `02`.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `agent-delegation-binding-ui`: **dois requisitos mudam.**
  - *"Erro de submit exibido via notificação genérica"* — **invertido** para o
    caso de recusa por validação: deixa de ser genérico para todo erro.
  - *"Nenhuma detecção de ciclo de delegação na interface"* — **esclarecido**. O
    texto atual diz *"sem detectar, **avisar** ou bloquear esse caso na
    interface"*, e depois desta change a interface **avisa** sobre o ciclo, só
    que depois da recusa do servidor. Sem o esclarecimento o requisito fica
    falso sem ninguém tocá-lo — a forma da convenção 13 que nenhuma revisão de
    tela pega, porque a tela não mudou. A proibição que continua valendo é a de
    detecção **antes do submit**.

## Impact

- **App afetado: `apps/frontend`, apenas.** Dois arquivos, os dois na feature
  `agents`: `components/AgentDelegationsTab.tsx` e o teste dele. Conferência de
  escopo de arquivo no `tasks.md`.
- **Nenhuma unidade pública nova.** Nenhum arquivo criado, nenhum export novo —
  o ramo é local ao `onError`, como no da aba irmã.
- **Sem mudança de API.** O backend já manda o texto pronto; esta change só
  para de jogá-lo fora.
- **Baseline de `apps/frontend`: 921/921 em 84 arquivos**, confirmada nesta
  sessão em 73,3s com load 2,78 — não herdada do fechamento anterior.
- **Sem Testcontainers**, então esta suíte não sofre a contenção que
  desqualificou duas rodadas na change anterior.
- **Non-Goals:** não tocar `apps/api`, `apps/workers`, `apps/inbox`, `libs/`;
  não redesenhar a aba; não mexer em instâncias, compose ou documentação de
  deploy; não tocar a linha de métricas nem protótipos de Insights; **não
  implementar detecção de ciclo no cliente** — duplicar a regra do servidor cria
  duas fontes de verdade que divergem na primeira mudança de regra.
