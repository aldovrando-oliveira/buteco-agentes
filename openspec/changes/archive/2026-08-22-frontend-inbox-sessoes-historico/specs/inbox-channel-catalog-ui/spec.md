## MODIFIED Requirements

### Requirement: Detalhe de canal
O sistema SHALL prover, em `apps/frontend`, uma página de detalhe de
canal consumindo `GET /channels/{id}`, organizada em duas abas —
**Sessões** (padrão) e **Configuração** — com as ações de editar
(`Editar`) e ativar/desativar o canal exibidas acima das abas, no nível
do canal, não da aba aberta. A aba Configuração exibe nome, tipo, agente
responsável, estado, datas de criação/atualização, a URL de webhook
(`webhookUrl`) em destaque com um botão de copiar e um texto de
instrução que varia conforme o `ChannelType`, sem nenhuma alteração de
comportamento em relação à página anterior de card único. A credencial
nunca é exibida, pois `ChannelResponse` nunca a inclui. O conteúdo da aba
Sessões é definido pela capability `inbox-message-history-ui`.

#### Scenario: Aba Sessões é a aba padrão
- **WHEN** o usuário acessa a página de detalhe de um canal existente
- **THEN** a aba Sessões é exibida selecionada por padrão, sem exigir
  clique do usuário

#### Scenario: Aba Configuração carregada com sucesso
- **WHEN** o usuário seleciona a aba Configuração no detalhe de um canal
  existente
- **THEN** a interface exibe nome, tipo, nome do agente responsável,
  estado, datas de criação/atualização e a URL de webhook em destaque com
  um botão de copiar, exatamente como exibido antes da introdução das
  abas

#### Scenario: Instrução de webhook para canal WAHA
- **WHEN** o usuário acessa a aba Configuração de um canal com
  `ChannelType: waha`
- **THEN** a interface exibe uma instrução indicando que a sessão do WAHA
  precisa ser configurada manualmente com a URL de webhook exibida

#### Scenario: Instrução de webhook para canal Telegram
- **WHEN** o usuário acessa a aba Configuração de um canal com
  `ChannelType: telegram`
- **THEN** a interface exibe uma indicação de que o webhook já foi
  configurado automaticamente junto ao Telegram no momento do cadastro,
  sem exigir nenhuma ação manual

#### Scenario: Ações de editar/desativar independem da aba aberta
- **WHEN** o usuário está na aba Sessões ou na aba Configuração do
  detalhe de um canal
- **THEN** os botões `Editar` e `Ativar`/`Desativar` permanecem visíveis
  acima das abas, e agem sobre o canal, não sobre a aba aberta

#### Scenario: Canal não encontrado
- **WHEN** o usuário acessa a página de detalhe com um identificador de
  canal inexistente e `GET /channels/{id}` retorna 404
- **THEN** a interface exibe uma indicação de que o canal não foi
  encontrado, independentemente de qual aba seria exibida
