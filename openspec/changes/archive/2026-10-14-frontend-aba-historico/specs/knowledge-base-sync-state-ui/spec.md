## MODIFIED Requirements

### Requirement: Alerta de falha da sincronização com o motivo pelo código
Com a base falhando, o card de origem SHALL exibir um alerta com o texto escolhido
pelo `syncState.lastError.code`, nunca pelo `detail` como frase, e SHALL usar do
`detail` só o e-mail da conta em `access-denied` e o motivo em `provider-error`.
Todo alerta SHALL dizer que nenhum documento saiu da base. Os textos SHALL seguir a
tabela "Falha da base" da D4 do `design.md` da change
`frontend-detalhe-base-sincronizada`.

O texto de `rate-limited` SHALL dizer que a falha é passageira e que a próxima
tentativa é automática, e SHALL NOT falar de acesso nem de compartilhamento. O
texto de `access-denied` SHALL mostrar o e-mail da conta e orientar compartilhar a
pasta como Leitor. O texto de `api-not-configured` SHALL dizer que é configuração
da instalação, e não da pasta. Código desconhecido SHALL cair num texto neutro com
o código, sem reaproveitar o texto de outro código. Nenhum texto SHALL conter
`exclu`, `remov` nem `apag`.

A proibição continua depois de o painel passar a oferecer a exclusão de base
(change `exclusao-base-painel`, #136), com este motivo: **uma falha de
sincronização nunca se resolve excluindo a base.** O remédio de cada código é na
pasta, na instalação ou na próxima rodada, e o alerta aparece também em base
ativa, onde a exclusão nem está disponível. A orientação de desativar e excluir
uma base existe só no `409` `folder-in-use` do cadastro, onde liberar a pasta é o
caminho.

**O alcance da proibição é o texto de estado e de remédio da sincronização, e não
o histórico** (change `frontend-aba-historico`, #101). A proibição alcança os textos
de falha da base, de arquivo ignorado, de resposta do pedido de sincronização e o
card de origem que os exibe. Ela NÃO SHALL ser estendida à aba `Histórico`
(`knowledge-document-history-ui`), cujo rótulo `Excluído` registra um fato gravado
pelo `apps/api` e não orienta ação nenhuma: o motivo da proibição é que uma falha
nunca se resolve excluindo a base, e um registro do que já aconteceu não é
remédio. A varredura SHALL continuar sobre as fontes desses textos e sobre o card
de origem, nunca sobre a página nem sobre o painel de uma aba, e os rótulos do
histórico SHALL morar fora dessas fontes, para a varredura não precisar de
exceção.

#### Scenario: Sem acesso à pasta mostra a conta e o caminho de recuperação
- **WHEN** a base falha com `lastError` `{ code: "access-denied", detail: "sync@exemplo.iam.gserviceaccount.com" }`
- **THEN** o alerta contém o e-mail, a orientação de compartilhar a pasta com essa
  conta como Leitor, a garantia de que nenhum documento saiu da base e a
  orientação de criar uma nova base com outra pasta se a pasta deixou de existir

#### Scenario: Cota estourada é passageira e não fala de acesso
- **WHEN** a base falha com `lastError.code` `rate-limited`
- **THEN** o alerta diz que a falha é passageira e que a próxima tentativa é
  automática, e não contém "acesso" nem "compartilh"

#### Scenario: Cada código de falha da base tem o seu texto
- **WHEN** a base falha, uma vez para cada código da tabela "Falha da base" da D4
- **THEN** o alerta exibe o texto daquele código, diferente do texto de qualquer
  outro código da tabela

#### Scenario: Código de falha desconhecido
- **WHEN** a base falha com `lastError.code` `codigo-novo`
- **THEN** o alerta exibe o texto neutro com `codigo-novo`, e não o texto de
  nenhum código conhecido

#### Scenario: Nenhum texto manda excluir
- **WHEN** os textos de todos os códigos de falha da base, de arquivo ignorado e
  de resposta do pedido de sincronização são montados
- **THEN** nenhum contém `exclu`, `remov` nem `apag`, sem distinção de caixa

#### Scenario: O alerta de base inativa também não manda excluir
- **WHEN** uma base `Synced` inativa, em que a exclusão está disponível no
  cabeçalho, falha com `lastError.code` `access-denied`
- **THEN** o card de origem não contém `exclu`, `remov` nem `apag`

#### Scenario: O histórico na tela não relaxa a proibição do card de origem
- **WHEN** uma base `Synced` inativa falha com `lastError.code` `access-denied`, e
  o operador abre a aba `Histórico` com um evento `Deleted`
- **THEN** o painel da aba exibe `Excluído`, e o card de origem, na mesma tela,
  não contém `exclu`, `remov` nem `apag`

