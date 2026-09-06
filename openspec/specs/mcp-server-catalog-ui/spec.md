# mcp-server-catalog-ui Specification

## Purpose

TBD - defined by change frontend-mcp-servidores-catalogo. Update Purpose after archive.

## Requirements

### Requirement: Listagem de servidores MCP na interface
O sistema SHALL prover, em `apps/frontend`, uma página que lista os
servidores MCP cadastrados consumindo `GET /mcp-servers`, exibindo para
cada servidor o nome (com link para o detalhe), a URL, o tipo de
autenticação (`AuthType`), quantos agentes usam aquele servidor e um
indicador do estado (`isActive`), além de um botão para iniciar o
cadastro de um novo servidor MCP. A página SHALL exibir a quantidade de
servidores cadastrados e um campo de busca por nome ou url, aplicado no
cliente sobre a coleção já carregada. A quantidade de agentes SHALL ser
derivada no cliente a partir de `GET /agents`, percorrendo os vínculos de
cada agente, já que a API não oferece consulta inversa; quando algum
desses agentes está vinculado ao servidor sem nenhuma tool permitida, a
interface SHALL sinalizar quantos estão nessa situação.

#### Scenario: Lista carregada com sucesso
- **WHEN** o usuário acessa a página de servidores MCP e existem
  servidores cadastrados
- **THEN** a interface exibe, para cada servidor, o nome com link para o
  detalhe, a URL, o tipo de autenticação, quantos agentes o usam, um
  indicador visual do estado (ativo ou inativo), a quantidade de
  servidores cadastrados, e um botão para cadastrar um novo servidor MCP

#### Scenario: Lista vazia
- **WHEN** o usuário acessa a página de servidores MCP e não existe
  nenhum servidor cadastrado
- **THEN** a interface indica que não há servidores MCP cadastrados e
  mantém visível o botão para cadastrar um novo

#### Scenario: Erro ao carregar a lista
- **WHEN** a chamada a `GET /mcp-servers` falha (erro de rede ou resposta
  de erro do servidor)
- **THEN** a interface exibe um estado de erro na página, sem quebrar a
  navegação do restante da aplicação

#### Scenario: Indicador distingue servidor MCP inativo na lista
- **WHEN** a lista inclui um servidor MCP com `isActive: false`
- **THEN** a interface exibe, na linha desse servidor, um indicador
  visual diferente do usado para servidores com `isActive: true`

#### Scenario: Servidor sem nenhum agente usando é identificado como tal
- **WHEN** a lista inclui um servidor MCP que nenhum agente do catálogo
  tem entre seus vínculos
- **THEN** a interface indica, na linha desse servidor, que nenhum agente
  o usa, em vez de exibir uma contagem zerada sem explicação

#### Scenario: Aviso de agentes vinculados sem nenhuma tool na listagem
- **WHEN** a lista inclui um servidor MCP que um ou mais agentes têm
  vinculado com a lista de tools permitidas vazia
- **THEN** a interface sinaliza, na linha desse servidor, quantos agentes
  estão vinculados a ele sem nenhuma tool

#### Scenario: Falha ao carregar o catálogo de agentes não quebra a listagem
- **WHEN** a chamada a `GET /agents` falha enquanto `GET /mcp-servers`
  responde normalmente
- **THEN** a interface continua exibindo a lista de servidores com nome,
  URL, autenticação e estado, apenas sem a informação de uso

#### Scenario: Busca por nome ou url
- **WHEN** o usuário digita um termo no campo de busca da listagem de
  servidores MCP
- **THEN** a interface exibe apenas os servidores cujo nome ou url contém
  aquele termo, sem enviar nenhuma requisição nova

#### Scenario: Busca ignora maiúsculas e acentuação
- **WHEN** o usuário busca por um termo sem acentuação ou com caixa
  diferente da cadastrada
- **THEN** a interface encontra o servidor correspondente normalmente

#### Scenario: Nenhum servidor corresponde à busca
- **WHEN** existem servidores cadastrados, mas nenhum corresponde ao
  termo buscado
- **THEN** a interface indica que nenhum servidor corresponde à busca,
  com uma mensagem distinta da usada quando não há nenhum servidor
  cadastrado

### Requirement: Cadastro de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para cadastrar
um servidor MCP informando nome, descrição, URL e tipo de autenticação
(`None` ou `BearerToken`), com um campo de credencial exibido e obrigatório
apenas quando o tipo de autenticação selecionado exigir credencial,
enviando os dados via `POST /mcp-servers`, e uma ação para cancelar o
cadastro e voltar à listagem sem enviar nenhuma requisição.

#### Scenario: Cadastro com sucesso sem autenticação
- **WHEN** o usuário preenche nome, descrição, URL válidos, seleciona
  `AuthType: None` e submete o formulário
- **THEN** a interface envia `POST /mcp-servers` sem nenhum campo de
  credencial, exibe uma notificação de sucesso e redireciona o usuário
  para a página de detalhe do servidor recém-criado

#### Scenario: Cadastro com sucesso com autenticação por token
- **WHEN** o usuário preenche nome, descrição, URL válidos, seleciona
  `AuthType: BearerToken` e informa uma credencial não vazia, e submete o
  formulário
- **THEN** a interface envia `POST /mcp-servers` incluindo a credencial
  informada, exibe uma notificação de sucesso e redireciona o usuário
  para a página de detalhe do servidor recém-criado

#### Scenario: Campo de credencial exibido apenas quando exigido pelo tipo de autenticação
- **WHEN** o usuário seleciona `AuthType: BearerToken` no formulário de
  cadastro
- **THEN** a interface exibe o campo de credencial como obrigatório

#### Scenario: Campo de credencial ausente quando o tipo de autenticação é None
- **WHEN** o usuário seleciona `AuthType: None` no formulário de cadastro
- **THEN** a interface não exibe nenhum campo de credencial

#### Scenario: Cadastro rejeitado por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  URL, tipo de autenticação ou credencial ausentes ou inválidos
- **THEN** a interface exibe a mensagem de erro correspondente no campo
  do formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao cadastrar
- **WHEN** a chamada a `POST /mcp-servers` falha por um motivo diferente
  de validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário

#### Scenario: Cancelar o cadastro
- **WHEN** o usuário aciona a ação "Cancelar" no formulário de cadastro,
  preenchido ou não
- **THEN** a interface navega para a listagem de servidores MCP
  (`/mcp-servers`) sem enviar nenhuma requisição a `POST /mcp-servers` e
  sem exibir nenhum diálogo de confirmação

### Requirement: Detalhe de servidor MCP
O sistema SHALL prover, em `apps/frontend`, uma página que exibe os dados
completos de um servidor MCP consumindo `GET /mcp-servers/{id}` — nome,
descrição, URL, tipo de autenticação, datas de criação e atualização e
estado (`isActive`) — nunca incluindo nenhum campo de credencial, com um
link para editar o servidor, uma ação para testar a conexão e uma ação
para ativá-lo ou desativá-lo. Quando o tipo de autenticação exigir
credencial, a interface SHALL exibir uma linha indicando que existe uma
credencial salva de forma cifrada, sempre mascarada e explicitando que a
API nunca devolve o valor.

#### Scenario: Detalhe carregado com sucesso
- **WHEN** o usuário acessa a página de detalhe de um servidor MCP
  existente
- **THEN** a interface exibe nome, descrição, URL, tipo de autenticação,
  as datas de criação e atualização e um indicador do estado (`isActive`)
  do servidor, um link para editá-lo, uma ação para testar a conexão e
  uma ação para ativá-lo ou desativá-lo, sem exibir nenhum valor de
  credencial

#### Scenario: Servidor MCP inexistente
- **WHEN** o usuário acessa a página de detalhe de um id que não
  corresponde a nenhum servidor MCP cadastrado (`GET /mcp-servers/{id}`
  responde 404)
- **THEN** a interface exibe um estado de "servidor não encontrado", sem
  quebrar a navegação do restante da aplicação

#### Scenario: Linha de credencial cifrada exibida quando a autenticação exige credencial
- **WHEN** o servidor exibido tem tipo de autenticação diferente de
  `None`
- **THEN** a interface exibe uma linha de credencial mascarada,
  indicando que ela está cifrada e que a API não devolve o valor

#### Scenario: Nenhuma linha de credencial quando a autenticação é None
- **WHEN** o servidor exibido tem tipo de autenticação `None`
- **THEN** a interface não exibe nenhuma linha de credencial

### Requirement: Edição de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para editar o
nome, a descrição, a URL, o tipo de autenticação e a credencial de um
servidor MCP já cadastrado, pré-preenchido com os dados atuais do
servidor (exceto a credencial, que a API nunca retorna), enviando os
dados via `PUT /mcp-servers/{id}`, e uma ação para cancelar a edição e
voltar à página de detalhe sem enviar nenhuma requisição. Quando o campo
de credencial for exibido (tipo de autenticação diferente de `None`) e o
usuário submeter o formulário sem preenchê-lo, a interface SHALL enviá-lo
em branco, comunicando visualmente que isso mantém a credencial
atualmente persistida em vez de removê-la.

#### Scenario: Formulário de edição pré-preenchido, sem o campo de credencial preenchido
- **WHEN** o usuário acessa a página de edição de um servidor MCP
  existente com `AuthType: BearerToken`
- **THEN** a interface exibe o formulário com nome, descrição, URL e tipo
  de autenticação atuais já preenchidos, e o campo de credencial vazio
  com uma indicação de que deixá-lo em branco mantém a credencial atual

#### Scenario: Edição mantendo a credencial atual
- **WHEN** o usuário altera nome, descrição ou URL, mantém
  `AuthType: BearerToken` e deixa o campo de credencial em branco, e
  submete o formulário de edição
- **THEN** a interface envia `PUT /mcp-servers/{id}` sem o campo de
  credencial preenchido, exibe uma notificação de sucesso e navega de
  volta para a página de detalhe do servidor editado

#### Scenario: Edição trocando a credencial
- **WHEN** o usuário preenche uma nova credencial no formulário de edição
  de um servidor com `AuthType: BearerToken` e submete
- **THEN** a interface envia `PUT /mcp-servers/{id}` incluindo a nova
  credencial, exibe uma notificação de sucesso e navega de volta para a
  página de detalhe do servidor editado

#### Scenario: Trocar o tipo de autenticação para None oculta o campo de credencial
- **WHEN** o usuário, no formulário de edição de um servidor com
  `AuthType: BearerToken`, seleciona `AuthType: None`
- **THEN** a interface oculta o campo de credencial e não o envia em
  `PUT /mcp-servers/{id}`

#### Scenario: Edição rejeitada por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  URL, tipo de autenticação ou credencial ausentes ou inválidos —
  incluindo o caso de `AuthType` diferente de `None` sem nenhuma
  credencial jamais persistida para esse servidor
- **THEN** a interface exibe a mensagem de erro correspondente no campo
  do formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao editar
- **WHEN** a chamada a `PUT /mcp-servers/{id}` falha por um motivo
  diferente de validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário, sem navegar para outra página

#### Scenario: Cancelar a edição
- **WHEN** o usuário aciona a ação "Cancelar" no formulário de edição,
  com ou sem alterações não salvas
- **THEN** a interface navega para a página de detalhe do servidor MCP
  (`/mcp-servers/{id}`) sem enviar nenhuma requisição a
  `PUT /mcp-servers/{id}` e sem exibir nenhum diálogo de confirmação

### Requirement: Ativação e desativação de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, ações para ativar e desativar
um servidor MCP cadastrado, consumindo `POST /mcp-servers/{id}/activate`
e `POST /mcp-servers/{id}/deactivate` a partir da página de detalhe.
Desativar um servidor MCP SHALL exigir confirmação explícita do usuário
antes de enviar a requisição; ativar SHALL NOT exigir confirmação. O
diálogo de confirmação da desativação SHALL nomear os agentes que usam
aquele servidor, ou informar que nenhum agente o usa.

#### Scenario: Ativar servidor MCP com sucesso, sem confirmação
- **WHEN** o usuário aciona a ação de ativar um servidor MCP inativo
- **THEN** a interface envia `POST /mcp-servers/{id}/activate`
  imediatamente, sem exibir nenhum diálogo de confirmação, exibe uma
  notificação de sucesso e atualiza o indicador de estado exibido para
  "ativo"

#### Scenario: Desativar servidor MCP exige confirmação antes de enviar a requisição
- **WHEN** o usuário aciona a ação de desativar um servidor MCP ativo
- **THEN** a interface exibe um diálogo de confirmação antes de enviar
  qualquer requisição, e `POST /mcp-servers/{id}/deactivate` só é enviado
  se o usuário confirmar explicitamente a ação nesse diálogo

#### Scenario: Diálogo de desativação nomeia os agentes afetados
- **WHEN** o usuário aciona a ação de desativar um servidor MCP que um ou
  mais agentes têm entre seus vínculos
- **THEN** o diálogo de confirmação exibe quantos agentes são afetados e
  os nomes deles

#### Scenario: Diálogo de desativação informa quando nenhum agente é afetado
- **WHEN** o usuário aciona a ação de desativar um servidor MCP que
  nenhum agente tem entre seus vínculos
- **THEN** o diálogo de confirmação informa que nenhum agente usa aquele
  servidor, em vez de repetir o aviso genérico de impacto

#### Scenario: Cancelar a confirmação de desativação não envia a requisição
- **WHEN** o usuário aciona a ação de desativar um servidor MCP ativo e,
  no diálogo de confirmação exibido, escolhe cancelar
- **THEN** a interface fecha o diálogo sem enviar
  `POST /mcp-servers/{id}/deactivate` e o servidor permanece exibido como
  ativo

#### Scenario: Confirmar a desativação envia a requisição e atualiza o estado
- **WHEN** o usuário aciona a ação de desativar um servidor MCP ativo e,
  no diálogo de confirmação exibido, confirma a ação
- **THEN** a interface envia `POST /mcp-servers/{id}/deactivate`, exibe
  uma notificação de sucesso e atualiza o indicador de estado exibido
  para "inativo"

#### Scenario: Falha de rede ou do servidor ao ativar ou desativar
- **WHEN** a chamada a `POST /mcp-servers/{id}/activate` ou
  `POST /mcp-servers/{id}/deactivate` falha (erro de rede ou erro do
  servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém o
  indicador de estado exibido igual ao estado anterior à tentativa

### Requirement: Teste de conexão exibido inline na interface
O sistema SHALL prover, em `apps/frontend`, uma ação "Testar conexão" em
dois contextos: no formulário de cadastro/edição e na página de detalhe
de um servidor MCP já cadastrado. Na página de detalhe, e no formulário
de edição quando o campo de credencial está em branco, a interface SHALL
usar `POST /mcp-servers/{id}/test`, que testa com a credencial já salva;
nos demais casos SHALL usar `POST /mcp-servers/test` com os valores
atuais do formulário. Em ambos os contextos, o resultado SHALL ser
exibido inline na própria tela, sem notificação (toast) nem diálogo
modal, e SHALL explicar a falha a partir do `failureReason` retornado,
exibindo também o motivo como rótulo técnico. O resultado SHALL indicar
quando o teste foi feito e deixar explícito que não é persistido.

#### Scenario: Teste de conexão bem-sucedido no formulário
- **WHEN** o usuário preenche URL e tipo de autenticação (e credencial,
  se aplicável) válidos no formulário e aciona "Testar conexão"
- **THEN** a interface envia `POST /mcp-servers/test` com os valores
  atuais do formulário e exibe, na própria tela, uma indicação inline de
  sucesso

#### Scenario: Teste no formulário de edição sem credencial digitada usa a credencial salva
- **WHEN** o usuário aciona "Testar conexão" no formulário de edição de
  um servidor já cadastrado, com o campo de credencial em branco
- **THEN** a interface envia `POST /mcp-servers/{id}/test`, e informa na
  própria tela que o teste está usando a credencial salva daquele
  servidor

#### Scenario: Teste no formulário de edição com credencial digitada usa a configuração informada
- **WHEN** o usuário digita uma credencial no formulário de edição e
  aciona "Testar conexão"
- **THEN** a interface envia `POST /mcp-servers/test` com os valores
  atuais do formulário, incluindo a credencial digitada

#### Scenario: Testar sem URL preenchida é barrado antes da requisição
- **WHEN** o usuário aciona "Testar conexão" no formulário com o campo de
  URL vazio
- **THEN** a interface indica que a URL precisa ser informada antes de
  testar e não envia nenhuma requisição de teste

#### Scenario: Teste de conexão bem-sucedido na página de detalhe
- **WHEN** o usuário aciona "Testar conexão" na página de detalhe de um
  servidor MCP já cadastrado
- **THEN** a interface envia `POST /mcp-servers/{id}/test` e exibe, na
  própria tela, uma indicação inline de sucesso

#### Scenario: Cada motivo de falha recebe uma explicação com a ação correspondente
- **WHEN** o teste de conexão responde indicando falha com um
  `failureReason` (`HostUnreachable`, `CredentialRejected`,
  `CredentialDecryptionFailed` ou `Unknown`)
- **THEN** a interface exibe uma explicação específica daquele motivo,
  indicando o que o usuário precisa fazer, e exibe também o motivo como
  rótulo técnico

#### Scenario: Mensagem técnica da API exibida como detalhe secundário
- **WHEN** a resposta de falha do teste inclui uma mensagem da API
- **THEN** a interface exibe essa mensagem como detalhe secundário, sem
  substituir a explicação do motivo

#### Scenario: Resultado do teste indica quando foi feito e que não é persistido
- **WHEN** a interface exibe o resultado de um teste de conexão, de
  sucesso ou de falha
- **THEN** a interface indica quando aquele teste foi executado e deixa
  explícito que o resultado não fica guardado

#### Scenario: Alterar um campo relevante após um teste limpa o resultado exibido
- **WHEN** o usuário altera a URL, o tipo de autenticação ou a credencial
  no formulário depois de já ter exibido um resultado de teste
- **THEN** a interface deixa de exibir o resultado do teste anterior, até
  que um novo teste seja executado para a configuração atual

#### Scenario: Nenhuma notificação (toast) nem diálogo modal é usado para o resultado do teste
- **WHEN** o usuário aciona "Testar conexão" em qualquer um dos dois
  contextos (formulário ou página de detalhe)
- **THEN** a interface não exibe o resultado como notificação (toast) nem
  como diálogo modal, apenas como conteúdo inline na própria tela

### Requirement: Catálogo de tools do servidor MCP no detalhe

O sistema SHALL exibir, na página de detalhe do servidor MCP, um catálogo
das tools que aquele servidor oferece, consumindo
`GET /mcp-servers/{id}/tools`. O catálogo SHALL começar ocioso, sem
disparar nenhuma requisição ao abrir a página, oferecendo uma ação para
consultar o servidor, e SHALL exibir, para cada tool, em quantos agentes
ela está permitida — informação derivada no cliente a partir dos vínculos
dos agentes.

#### Scenario: Catálogo começa ocioso, sem consultar o servidor
- **WHEN** o usuário abre a página de detalhe de um servidor MCP
- **THEN** a interface indica que as tools são descobertas ao vivo e
  oferece uma ação para consultá-las, sem enviar
  `GET /mcp-servers/{id}/tools`

#### Scenario: Consultar exibe as tools do servidor
- **WHEN** o usuário aciona a ação de consultar as tools e a descoberta
  responde com sucesso e ao menos uma tool
- **THEN** a interface exibe o nome e a descrição de cada tool retornada

#### Scenario: Servidor sem nenhuma tool
- **WHEN** a descoberta responde com sucesso e nenhuma tool
- **THEN** a interface indica que o servidor não oferece nenhuma tool, em
  vez de uma lista vazia sem explicação

#### Scenario: Falha na descoberta exibe o motivo e permite tentar novamente
- **WHEN** a descoberta de tools falha
- **THEN** a interface exibe o motivo da falha e uma ação para tentar
  novamente, sem quebrar o restante da página de detalhe

#### Scenario: Cada tool indica em quantos agentes está permitida
- **WHEN** o catálogo exibe uma tool que consta na lista de tools
  permitidas de um ou mais agentes vinculados àquele servidor
- **THEN** a interface indica, junto dessa tool, em quantos agentes ela
  está permitida

#### Scenario: Tool não permitida em nenhum agente é identificada como tal
- **WHEN** o catálogo exibe uma tool que não consta na lista de tools
  permitidas de nenhum agente
- **THEN** a interface indica que ela não está permitida em nenhum agente

### Requirement: Agentes que usam o servidor MCP no detalhe

O sistema SHALL exibir, na página de detalhe do servidor MCP, a relação
dos agentes que têm aquele servidor entre seus vínculos, derivada no
cliente a partir de `GET /agents`, mostrando para cada agente o nome com
link para o seu detalhe, as tools permitidas naquele vínculo e o estado
do agente. Quando o vínculo de um agente não tem nenhuma tool permitida,
a interface SHALL sinalizar essa condição na linha desse agente.

#### Scenario: Relação de agentes que usam o servidor
- **WHEN** o usuário abre o detalhe de um servidor MCP que um ou mais
  agentes têm entre seus vínculos
- **THEN** a interface exibe, para cada um desses agentes, o nome com
  link para o detalhe do agente, as tools permitidas naquele vínculo e um
  indicador do estado do agente

#### Scenario: Agente vinculado sem nenhuma tool é sinalizado
- **WHEN** um dos agentes que usam o servidor tem o vínculo com a lista
  de tools permitidas vazia
- **THEN** a interface sinaliza, na linha desse agente, que ele está
  vinculado sem nenhuma tool

#### Scenario: Nenhum agente usa o servidor
- **WHEN** o usuário abre o detalhe de um servidor MCP que nenhum agente
  tem entre seus vínculos
- **THEN** a interface informa que nenhum agente usa aquele servidor e
  que desativá-lo não afeta nenhum agente no momento

#### Scenario: Falha ao carregar o catálogo de agentes não quebra o detalhe
- **WHEN** a chamada a `GET /agents` falha enquanto o detalhe do servidor
  carrega normalmente
- **THEN** a interface continua exibindo os dados do servidor e indica
  que não foi possível carregar a informação de uso
