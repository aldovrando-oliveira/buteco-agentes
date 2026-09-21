## MODIFIED Requirements

### Requirement: Erro de submit exibido via notificação genérica

O sistema SHALL distinguir, quando `PUT /agents/{id}/delegations` falhar,
**recusa permanente de falha transitória**, sem interromper a renderização do
restante da página de detalhe do agente nem exigir recarregamento.

Quando a resposta for **400 com uma mensagem de validação sob o campo
`targetAgentIds`**, a interface SHALL exibir **a mensagem que a API enviou**, em
um aviso que **permanece na tela** até a próxima tentativa ou o descarte, e
SHALL **não** exibir a notificação genérica nem instruir a tentar de novo —
essa recusa é permanente, e mandar repetir a operação afirmaria mais do que o
sistema sabe.

Para **qualquer outra falha** — indisponibilidade de rede, erro de servidor, ou
resposta sem mensagem sob aquele campo — a interface SHALL exibir a notificação
de erro genérica, com a instrução de tentar de novo, que ali está correta.

A interface SHALL exibir a mensagem da API **como ela veio**, sem interpretá-la,
sem extrair partes dela e sem sugerir qual vínculo remover — o sistema conhece o
caminho que a recusa nomeia, não a preferência do operador sobre qual aresta
desfazer.

O aviso de recusa SHALL desaparecer quando a operação seguinte tiver sucesso ou
quando o operador descartar as alterações, para que a tela não continue afirmando
uma recusa que já não vale.

O aviso SHALL **permanecer enquanto o operador edita a seleção**, inclusive
depois de ele já ter desmarcado o agente que fechava o ciclo. Não é omissão: o
caminho nomeado na recusa é a única informação que diz **qual aresta remover**, e
um aviso que sumisse ao primeiro clique desapareceria exatamente no instante em
que passa a ser útil. A limpeza acontece no submit seguinte, não na edição.

#### Scenario: Falha no submit não quebra a página
- **WHEN** o usuário aciona "Salvar delegações" e `PUT
  /agents/{id}/delegations` responde com erro
- **THEN** a interface mantém a seleção atual do controle e mantém o restante
  da página de detalhe do agente renderizado normalmente

#### Scenario: Recusa por ciclo exibe o caminho que a API enviou
- **WHEN** o usuário aciona "Salvar delegações" e `PUT
  /agents/{id}/delegations` responde `400` com uma mensagem sob
  `targetAgentIds` nomeando o caminho do ciclo entre agentes
- **THEN** a interface exibe essa mensagem, com o caminho, em um aviso que
  permanece na tela

#### Scenario: Recusa permanente não instrui a tentar de novo
- **WHEN** a interface exibe uma recusa recebida como `400` sob
  `targetAgentIds`
- **THEN** a interface **não** exibe a notificação de erro genérica nem
  qualquer instrução de repetir a operação

#### Scenario: Outra recusa de validação da mesma rota também é exibida
- **WHEN** o usuário aciona "Salvar delegações" e a rota responde `400` sob
  `targetAgentIds` por um motivo que não é ciclo — auto-delegação, ou id que
  não corresponde a agente cadastrado
- **THEN** a interface exibe a mensagem enviada pela API, pelo mesmo caminho da
  recusa por ciclo, sem tratamento específico por motivo

#### Scenario: Falha transitória mantém a notificação genérica
- **WHEN** o usuário aciona "Salvar delegações" e a requisição falha por
  indisponibilidade de rede ou erro de servidor
- **THEN** a interface exibe a notificação de erro genérica, com a instrução de
  tentar de novo, e não exibe nenhum aviso de recusa permanente

#### Scenario: Sucesso seguinte remove o aviso de recusa
- **WHEN** a interface está exibindo um aviso de recusa e o usuário corrige a
  seleção e salva com sucesso
- **THEN** o aviso de recusa desaparece e a interface exibe a notificação de
  sucesso

#### Scenario: Descartar remove o aviso de recusa
- **WHEN** a interface está exibindo um aviso de recusa e o usuário descarta as
  alterações
- **THEN** o aviso de recusa desaparece e a seleção volta à original

### Requirement: Nenhuma detecção de ciclo de delegação na interface

O sistema SHALL permitir que o usuário selecione, na lista de agentes-alvo,
qualquer combinação de agentes como delegações de saída, incluindo combinações
que formem um ciclo indireto (A→B→C→A) ou um par bidirecional (A→B e B→A)
quando consideradas em conjunto com delegações já cadastradas em outros agentes
— **sem detectar, avisar ou bloquear esse caso antes do submit**.

A detecção de ciclo é do servidor, e a interface SHALL **não** reimplementá-la:
ela não conhece o grafo completo de delegações de todos os agentes, e duplicar a
regra criaria uma segunda fonte de verdade que divergiria na primeira mudança
dessa regra.

Exibir a recusa que o servidor devolveu **não** é detecção na interface, e é
obrigação do requisito de erro de submit acima.

#### Scenario: Selecionar uma delegação que fecha um ciclo indireto é permitido
- **WHEN** o agente B já delega para o agente C, o agente C já delega
  para o agente A, e o usuário, na página de detalhe do agente A,
  seleciona o agente B como delegação de saída
- **THEN** a interface permite a seleção e o submit normalmente, sem
  exibir nenhum aviso sobre o ciclo resultante antes da resposta do servidor
