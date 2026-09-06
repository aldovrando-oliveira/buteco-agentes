# inbox-channel-catalog-ui Specification

## Purpose

TBD - defined by change frontend-inbox-catalogo-canais. Update Purpose after archive.

## Requirements

### Requirement: Listagem de canais na interface
O sistema SHALL prover, em `apps/frontend`, uma página que lista os
canais de entrada cadastrados consumindo `GET /channels`, exibindo para
cada canal o nome (com link para o detalhe), o `ChannelType`, o agente
responsável (nome resolvido via `GET /agents`, não o `AgentId` cru) e um
indicador do estado (`isActive`), além de um botão para iniciar o
cadastro de um novo canal.

#### Scenario: Lista carregada com sucesso
- **WHEN** o usuário acessa a página de canais e existem canais
  cadastrados
- **THEN** a interface exibe, para cada canal, o nome com link para o
  detalhe, o tipo de canal, o nome do agente responsável, um indicador
  visual do estado (ativo ou inativo), e um botão para cadastrar um novo
  canal

#### Scenario: Lista vazia
- **WHEN** o usuário acessa a página de canais e não existe nenhum canal
  cadastrado
- **THEN** a interface indica que não há canais cadastrados e mantém
  visível o botão para cadastrar um novo

#### Scenario: Erro ao carregar a lista
- **WHEN** a chamada a `GET /channels` falha (erro de rede ou resposta de
  erro do servidor)
- **THEN** a interface exibe um estado de erro na página, sem quebrar a
  navegação do restante da aplicação

#### Scenario: Indicador distingue canal inativo na lista
- **WHEN** a lista inclui um canal com `isActive: false`
- **THEN** a interface exibe, na linha desse canal, um indicador visual
  diferente do usado para canais com `isActive: true`

#### Scenario: Item de navegação para canais
- **WHEN** o usuário está em qualquer página da aplicação
- **THEN** a navegação principal (`AppShell`) exibe um item para acessar
  a listagem de canais em `/channels`

### Requirement: Cadastro de canal pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para cadastrar
um canal informando nome, agente responsável (`AgentId`, incluindo
agentes inativos) e tipo de canal (`waha` ou `telegram`, lista fixa no
frontend), exibindo um sub-formulário de credencial específico para o
tipo selecionado, enviando os dados via `POST /channels` com a credencial
serializada em JSON no campo `credential`, e uma ação para cancelar o
cadastro e voltar à listagem sem enviar nenhuma requisição.

#### Scenario: Troca de tipo de canal troca o sub-formulário de credencial
- **WHEN** o usuário seleciona `ChannelType: waha` no formulário de
  cadastro
- **THEN** a interface exibe os campos de credencial do WAHA (URL do
  serviço, nome da sessão, token de autenticação)

#### Scenario: Troca de tipo de canal para Telegram
- **WHEN** o usuário seleciona `ChannelType: telegram` no formulário de
  cadastro
- **THEN** a interface exibe o campo de credencial do Telegram (token do
  bot), substituindo qualquer campo do WAHA exibido anteriormente

#### Scenario: Cadastro de canal WAHA com sucesso
- **WHEN** o usuário preenche nome, seleciona um agente responsável,
  seleciona `ChannelType: waha`, preenche URL do serviço, nome da sessão
  e token de autenticação, e submete o formulário
- **THEN** a interface envia `POST /channels` com `credential` contendo o
  JSON serializado `{ serviceUrl, sessionName, authToken }`, exibe uma
  notificação de sucesso e redireciona o usuário para a página de
  detalhe do canal recém-criado

#### Scenario: Cadastro de canal Telegram com sucesso
- **WHEN** o usuário preenche nome, seleciona um agente responsável,
  seleciona `ChannelType: telegram`, preenche o token do bot, e submete o
  formulário
- **THEN** a interface envia `POST /channels` com `credential` contendo o
  JSON serializado `{ botToken }`, exibe uma notificação de sucesso e
  redireciona o usuário para a página de detalhe do canal recém-criado

#### Scenario: Cadastro com agente inativo permitido
- **WHEN** o usuário seleciona, no campo de agente responsável, um agente
  marcado como inativo
- **THEN** o formulário permite a seleção e o cadastro prossegue
  normalmente

#### Scenario: Erro de validação (400) exibido por campo
- **WHEN** `POST /channels` retorna 400 com `InvalidCredential` ou
  `AgentNotFound`
- **THEN** a interface exibe a mensagem de erro associada ao campo
  correspondente (`credential` ou `agentId`) diretamente no formulário,
  sem navegar para outra página

#### Scenario: Múltiplas mensagens de erro de credencial exibidas juntas
- **WHEN** `POST /channels` retorna 400 com `InvalidCredential` contendo
  mais de uma mensagem no array de `credential` (ex. URL de serviço
  inválida e nome de sessão vazio simultaneamente, no cadastro de um
  canal WAHA)
- **THEN** a interface exibe todas as mensagens recebidas, não apenas a
  primeira

#### Scenario: Falha de provisionamento (502) exibida com mensagem própria
- **WHEN** `POST /channels` retorna 502 com `ProvisioningFailed` (ex.
  falha ao configurar o webhook do Telegram junto à plataforma externa)
- **THEN** a interface exibe um alerta de erro dedicado com a mensagem
  recebida no corpo da resposta, visualmente distinto do erro de
  validação por campo, sem navegar para outra página

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

### Requirement: Edição de canal pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para editar
nome, agente responsável e credencial de um canal existente via
`PUT /channels/{id}`, com `ChannelType` fixo e não editável, e um
sub-formulário de credencial write-only (nunca pré-preenchido) que, se
deixado inteiramente em branco, preserva a credencial já persistida.

#### Scenario: Tipo de canal não é editável
- **WHEN** o usuário acessa o formulário de edição de um canal existente
- **THEN** a interface exibe o `ChannelType` do canal como informação
  fixa, sem permitir alterá-lo, e renderiza apenas o sub-formulário de
  credencial correspondente a esse tipo

#### Scenario: Edição mantendo a credencial em branco preserva a persistida
- **WHEN** o usuário altera apenas o nome ou o agente responsável,
  deixando todos os campos do sub-formulário de credencial em branco, e
  submete o formulário
- **THEN** a interface envia `PUT /channels/{id}` sem o campo
  `credential`, e a credencial previamente persistida permanece
  inalterada

#### Scenario: Edição substituindo a credencial por inteiro
- **WHEN** o usuário preenche todos os campos obrigatórios do
  sub-formulário de credencial do tipo do canal e submete o formulário
- **THEN** a interface envia `PUT /channels/{id}` com `credential`
  contendo o novo JSON serializado, substituindo a credencial anterior
  por inteiro

#### Scenario: Preenchimento parcial da credencial é bloqueado antes do envio
- **WHEN** o usuário preenche apenas parte dos campos obrigatórios do
  sub-formulário de credencial (ex. só o token de autenticação do WAHA,
  sem preencher URL do serviço e nome da sessão)
- **THEN** a interface impede o envio do formulário e indica quais campos
  do sub-formulário de credencial ainda precisam ser preenchidos

#### Scenario: Falha de provisionamento (502) na edição
- **WHEN** `PUT /channels/{id}` retorna 502 com `ProvisioningFailed` após
  uma troca de credencial (ex. novo token do Telegram inválido para
  reconfigurar o webhook)
- **THEN** a interface exibe um alerta de erro dedicado com a mensagem
  recebida, e o formulário permanece preenchido para nova tentativa

### Requirement: Ativação e desativação de canal pela interface
O sistema SHALL prover, na página de detalhe de canal, ações para ativar
(`POST /channels/{id}/activate`) e desativar
(`POST /channels/{id}/deactivate`) o canal, com confirmação exigida
apenas para a desativação.

#### Scenario: Ativar canal sem confirmação
- **WHEN** o usuário aciona "Ativar" num canal com `isActive: false`
- **THEN** a interface envia `POST /channels/{id}/activate` imediatamente,
  sem exigir confirmação, e atualiza o estado exibido

#### Scenario: Desativar canal exige confirmação
- **WHEN** o usuário aciona "Desativar" num canal com `isActive: true`
- **THEN** a interface exibe uma confirmação antes de enviar
  `POST /channels/{id}/deactivate`

#### Scenario: Ativação idempotente
- **WHEN** o usuário aciona "Ativar" num canal que já está ativo
- **THEN** a interface envia a requisição normalmente e reflete o estado
  ativo retornado, sem erro

### Requirement: Busca na listagem de canais
O sistema SHALL prover, na listagem de canais, um campo de busca que filtra a
coleção já carregada por nome do canal, tipo do canal e nome do agente
responsável — os três dados que a linha exibe.

A busca SHALL ignorar diferenças de caixa e de acentuação, e SHALL ser aplicada
no cliente, porque a rota de listagem de canais não oferece busca nem
paginação.

A listagem SHALL exibir a quantidade de canais cadastrados, e SHALL distinguir
a lista vazia por falta de cadastro da lista vazia por busca sem resultado, por
serem situações com saídas diferentes.

Com o catálogo vazio, o campo de busca SHALL NOT ser exibido.

#### Scenario: Busca por nome, tipo ou agente responsável
- **WHEN** o operador digita um termo no campo de busca da listagem de canais
- **THEN** a interface exibe apenas os canais cujo nome, tipo ou agente
  responsável contém aquele termo, sem enviar nenhuma requisição nova

#### Scenario: Busca ignora maiúsculas e acentuação
- **WHEN** o operador busca por um termo sem acentuação ou com caixa diferente
  da cadastrada
- **THEN** a interface encontra o canal normalmente

#### Scenario: Nenhum canal corresponde à busca
- **WHEN** existem canais cadastrados, mas nenhum corresponde ao termo buscado
- **THEN** a interface indica que nenhum canal corresponde à busca, com uma
  mensagem distinta da usada quando não há nenhum canal cadastrado

#### Scenario: Catálogo vazio não exibe busca
- **WHEN** não há nenhum canal cadastrado
- **THEN** o campo de busca não é exibido, e a interface indica que não há canal
  cadastrado
