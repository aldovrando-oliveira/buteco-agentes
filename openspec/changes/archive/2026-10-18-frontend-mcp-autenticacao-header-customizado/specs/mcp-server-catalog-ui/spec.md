## MODIFIED Requirements

### Requirement: Cadastro de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para cadastrar
um servidor MCP informando nome, descrição, URL e tipo de autenticação
(`None`, `BearerToken` ou `CustomHeader`), enviando os dados via
`POST /mcp-servers`, e uma ação para cancelar o cadastro e voltar à
listagem sem enviar nenhuma requisição. O tipo de autenticação SHALL ser
escolhido numa lista de cartões com radio, um por tipo, cada um com título
e descrição curta, com os títulos "Nenhuma", "Bearer Token" e "Token em
header customizado". Abaixo de uma largura mínima do formulário os
cartões SHALL empilhar, e nenhum título SHALL quebrar linha nem ultrapassar
o cartão. O campo de credencial SHALL ser exibido e obrigatório apenas
quando o tipo selecionado exigir credencial, e SHALL se chamar "Token" com
`CustomHeader`. Com `CustomHeader`, o formulário SHALL exibir um campo
obrigatório "Nome do header", em fonte monoespaçada, e uma caixa que mostra
como a credencial sai em cada requisição ao servidor: o nome do header
digitado seguido do valor sempre mascarado. O nome do header SHALL ser
enviado como `authHeaderName` apenas com `CustomHeader`. A validação feita
no cliente SHALL se limitar à obrigatoriedade dos campos; a validação de
formato do nome do header e do valor do token SHALL ficar com a API, e a
interface SHALL exibir a mensagem que ela devolver.

#### Scenario: Cadastro com sucesso sem autenticação
- **WHEN** o usuário preenche nome, descrição, URL válidos, seleciona
  `AuthType: None` e submete o formulário
- **THEN** a interface envia `POST /mcp-servers` sem nenhum campo de
  credencial e sem `authHeaderName`, exibe uma notificação de sucesso e
  redireciona o usuário para a página de detalhe do servidor recém-criado

#### Scenario: Cadastro com sucesso com autenticação por token
- **WHEN** o usuário preenche nome, descrição, URL válidos, seleciona
  `AuthType: BearerToken` e informa uma credencial não vazia, e submete o
  formulário
- **THEN** a interface envia `POST /mcp-servers` incluindo a credencial
  informada e sem `authHeaderName`, exibe uma notificação de sucesso e
  redireciona o usuário para a página de detalhe do servidor recém-criado

#### Scenario: Cadastro com sucesso com token em header customizado
- **WHEN** o usuário preenche nome, descrição, URL válidos, seleciona
  "Token em header customizado", informa o nome do header e um token não
  vazio, e submete o formulário
- **THEN** a interface envia `POST /mcp-servers` com
  `authType: CustomHeader`, o nome do header em `authHeaderName` e o token
  em `credential`, exibe uma notificação de sucesso e redireciona o usuário
  para a página de detalhe do servidor recém-criado

#### Scenario: Seletor de tipo em cartões com radio
- **WHEN** o usuário abre o formulário de cadastro
- **THEN** a interface exibe o tipo de autenticação como três opções de
  radio em cartões ("Nenhuma", "Bearer Token" e "Token em header
  customizado"), cada uma com uma descrição curta, e não exibe nenhuma
  lista suspensa para o tipo

#### Scenario: Campo de credencial exibido apenas quando exigido pelo tipo de autenticação
- **WHEN** o usuário seleciona `AuthType: BearerToken` no formulário de
  cadastro
- **THEN** a interface exibe o campo de credencial como obrigatório, com o
  rótulo "Credencial", e não exibe o campo de nome do header

#### Scenario: Campo de credencial ausente quando o tipo de autenticação é None
- **WHEN** o usuário seleciona `AuthType: None` no formulário de cadastro
- **THEN** a interface não exibe nenhum campo de credencial nem de nome do
  header

#### Scenario: Token em header customizado exibe o nome do header e o token
- **WHEN** o usuário seleciona "Token em header customizado" no formulário
  de cadastro
- **THEN** a interface exibe o campo obrigatório "Nome do header", com o
  placeholder `X-API-TOKEN`, e o campo obrigatório "Token" no lugar de
  "Credencial"

#### Scenario: Caixa de prévia só com token em header customizado
- **WHEN** o usuário seleciona "Token em header customizado" e digita um
  nome de header
- **THEN** a interface exibe a caixa "Como a credencial sai em cada
  requisição ao servidor" com o nome digitado seguido de `: ••••••••`,
  atualizada a cada tecla, com a nota de que o valor vai sem prefixo e que
  `Authorization` não é enviado, e nunca exibe o valor do token

#### Scenario: Nenhuma caixa de prévia nos outros tipos
- **WHEN** o tipo selecionado é `None` ou `BearerToken`
- **THEN** a interface não exibe a caixa de prévia da credencial

#### Scenario: Nome do header digitado e depois abandonado não é enviado
- **WHEN** o usuário seleciona "Token em header customizado", digita um
  nome de header, troca para outro tipo e submete o formulário
- **THEN** a interface envia `POST /mcp-servers` sem `authHeaderName`, e o
  nome digitado volta preenchido se o usuário selecionar de novo "Token em
  header customizado" antes de submeter

#### Scenario: Nome do header obrigatório no cliente
- **WHEN** o usuário submete o formulário com "Token em header
  customizado" e o nome do header vazio ou só com espaços
- **THEN** a interface exibe a mensagem de obrigatoriedade no campo "Nome
  do header" e não envia nenhuma requisição

#### Scenario: Formato do nome do header validado pela API, não pelo cliente
- **WHEN** o usuário submete o formulário com "Token em header
  customizado" e um nome de header não vazio que a API recusa (por exemplo,
  `Content-Type` ou um nome com espaço)
- **THEN** a interface envia `POST /mcp-servers` e exibe, no campo "Nome
  do header", a mensagem devolvida pela API em `authHeaderName`

#### Scenario: Cadastro rejeitado por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  URL, tipo de autenticação, nome do header ou credencial ausentes ou
  inválidos
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
para ativá-lo ou desativá-lo. O tipo `CustomHeader` SHALL ser exibido como
"Token em header customizado", e, só com esse tipo, a interface SHALL
exibir uma linha "Header" com o nome do header em fonte monoespaçada.
Quando o tipo de autenticação exigir credencial, a interface SHALL exibir
uma linha indicando que existe uma credencial salva de forma cifrada,
sempre mascarada e explicitando que a API nunca devolve o valor.

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

#### Scenario: Servidor com token em header customizado exibe o rótulo e o header
- **WHEN** o servidor exibido tem `authType: CustomHeader` e
  `authHeaderName: X-API-TOKEN`
- **THEN** a interface exibe "Token em header customizado" como tipo de
  autenticação e uma linha "Header" com `X-API-TOKEN` em fonte
  monoespaçada

#### Scenario: Nenhuma linha de header nos outros tipos
- **WHEN** o servidor exibido tem tipo de autenticação `None` ou
  `BearerToken`
- **THEN** a interface não exibe nenhuma linha "Header"

### Requirement: Edição de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para editar o
nome, a descrição, a URL, o tipo de autenticação, o nome do header e a
credencial de um servidor MCP já cadastrado, pré-preenchido com os dados
atuais do servidor (exceto a credencial, que a API nunca retorna),
enviando os dados via `PUT /mcp-servers/{id}`, e uma ação para cancelar a
edição e voltar à página de detalhe sem enviar nenhuma requisição. O
seletor de tipo, o campo de nome do header e a caixa de prévia SHALL seguir
as mesmas regras do cadastro. Com o tipo salvo mantido, quando o campo de
credencial for exibido e o usuário submeter o formulário sem preenchê-lo,
a interface SHALL enviá-lo em branco, comunicando visualmente que isso
mantém a credencial atualmente persistida em vez de removê-la. Quando o
usuário trocar o tipo salvo por outro tipo que exige credencial, a
credencial SHALL passar a ser obrigatória, porque a API não reaproveita a
credencial de outro tipo; se o tipo salvo também exigia credencial, a
interface SHALL avisar que a credencial salva não é reaproveitada. Quando
o usuário trocar um tipo com credencial por `None`, a interface SHALL
avisar que a credencial atual será apagada ao salvar.

#### Scenario: Formulário de edição pré-preenchido, sem o campo de credencial preenchido
- **WHEN** o usuário acessa a página de edição de um servidor MCP
  existente com `AuthType: BearerToken`
- **THEN** a interface exibe o formulário com nome, descrição, URL e tipo
  de autenticação atuais já preenchidos, e o campo de credencial vazio
  com uma indicação de que deixá-lo em branco mantém a credencial atual

#### Scenario: Formulário de edição pré-preenchido com o nome do header
- **WHEN** o usuário acessa a página de edição de um servidor com
  `authType: CustomHeader` e `authHeaderName: X-API-TOKEN`
- **THEN** a interface exibe "Token em header customizado" marcado, o
  campo "Nome do header" preenchido com `X-API-TOKEN` e o campo "Token"
  vazio, com a indicação de que deixá-lo em branco mantém a credencial
  atual

#### Scenario: Edição mantendo a credencial atual
- **WHEN** o usuário altera nome, descrição ou URL, mantém
  `AuthType: BearerToken` e deixa o campo de credencial em branco, e
  submete o formulário de edição
- **THEN** a interface envia `PUT /mcp-servers/{id}` sem o campo de
  credencial preenchido, exibe uma notificação de sucesso e navega de
  volta para a página de detalhe do servidor editado

#### Scenario: Edição do nome do header mantendo o token atual
- **WHEN** o usuário altera o nome do header de um servidor
  `CustomHeader`, mantém o tipo e deixa o campo "Token" em branco, e
  submete o formulário de edição
- **THEN** a interface envia `PUT /mcp-servers/{id}` com o nome novo em
  `authHeaderName` e sem o campo de credencial preenchido, sem exibir
  nenhum aviso de troca de tipo

#### Scenario: Edição trocando a credencial
- **WHEN** o usuário preenche uma nova credencial no formulário de edição
  de um servidor com `AuthType: BearerToken` e submete
- **THEN** a interface envia `PUT /mcp-servers/{id}` incluindo a nova
  credencial, exibe uma notificação de sucesso e navega de volta para a
  página de detalhe do servidor editado

#### Scenario: Troca entre dois tipos com credencial avisa e exige a credencial nova
- **WHEN** o usuário, no formulário de edição de um servidor
  `BearerToken`, seleciona "Token em header customizado" (ou, de um
  servidor `CustomHeader`, seleciona "Bearer Token")
- **THEN** a interface exibe o aviso de que a credencial salva era do
  tipo anterior e não é reaproveitada, marca o campo de credencial como
  obrigatório, e, se o usuário submeter com o campo em branco, exibe a
  mensagem de obrigatoriedade no campo sem enviar nenhuma requisição

#### Scenario: Trocar o tipo de autenticação para None oculta o campo de credencial e avisa que ela será apagada
- **WHEN** o usuário, no formulário de edição de um servidor com
  `AuthType: BearerToken` ou `CustomHeader`, seleciona `AuthType: None`
- **THEN** a interface oculta o campo de credencial e o de nome do
  header, não os envia em `PUT /mcp-servers/{id}`, e exibe o aviso de que a
  credencial atual será apagada ao salvar

#### Scenario: Saída de None exige credencial, sem aviso de credencial anterior
- **WHEN** o usuário, no formulário de edição de um servidor com
  `AuthType: None`, seleciona "Bearer Token" ou "Token em header
  customizado"
- **THEN** a interface marca o campo de credencial como obrigatório e não
  exibe nenhum aviso sobre credencial salva ou anterior

#### Scenario: Edição rejeitada por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  URL, tipo de autenticação, nome do header ou credencial inválidos que o
  formulário deixa passar — por exemplo, um token de "Token em header
  customizado" com espaço no início (400 em `credential`), ou a troca para
  "Token em header customizado" com o nome `Content-Type` e o token
  preenchido (400 em `authHeaderName`)
- **THEN** a interface exibe a mensagem de erro correspondente no campo
  do formulário associado (no exemplo, "Token" e "Nome do header"), sem
  navegar para outra página

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

### Requirement: Teste de conexão exibido inline na interface
O sistema SHALL prover, em `apps/frontend`, uma ação "Testar conexão" em
dois contextos: no formulário de cadastro/edição e na página de detalhe
de um servidor MCP já cadastrado. Na página de detalhe, e no formulário
de edição quando o tipo de autenticação salvo é mantido, exige credencial
e o campo de credencial está em branco, a interface SHALL usar
`POST /mcp-servers/{id}/test`, que testa a configuração salva (URL, tipo,
nome do header e credencial); nos demais casos SHALL usar
`POST /mcp-servers/test` com os valores atuais do formulário, incluindo
`authHeaderName` apenas com `CustomHeader`. No formulário de edição,
quando o tipo mudou para um tipo que exige credencial e a credencial nova
está em branco, a ação SHALL ficar desabilitada, com uma nota explicando
que o tipo novo só é testado com a credencial nova. Antes de
`POST /mcp-servers/test`, a interface SHALL barrar no cliente apenas a
ausência dos campos que o teste exige (URL e, com `CustomHeader`, nome do
header e token). Um erro de validação (HTTP 400) do teste SHALL ser exibido
nos campos do formulário, como no envio, e não como resultado de conexão.
O erro de validação de um envio anterior exibido num campo SHALL deixar de
ser exibido quando o usuário edita esse campo, ou quando um erro de
validação do teste é aplicado a ele, para que a mensagem exibida seja
sempre a do valor atual. O resultado de um teste SHALL ser exibido inline na própria tela, sem
notificação (toast) nem diálogo modal, e SHALL explicar a falha a partir do
`failureReason` retornado, exibindo também o motivo como rótulo técnico. O
resultado SHALL indicar quando o teste foi feito e deixar explícito que
não é persistido.

#### Scenario: Teste de conexão bem-sucedido no formulário
- **WHEN** o usuário preenche URL e tipo de autenticação (e credencial,
  se aplicável) válidos no formulário e aciona "Testar conexão"
- **THEN** a interface envia `POST /mcp-servers/test` com os valores
  atuais do formulário e exibe, na própria tela, uma indicação inline de
  sucesso

#### Scenario: Teste de token em header customizado envia o nome do header
- **WHEN** o usuário seleciona "Token em header customizado", preenche
  URL, nome do header e token, e aciona "Testar conexão"
- **THEN** a interface envia `POST /mcp-servers/test` com
  `authType: CustomHeader`, o nome do header em `authHeaderName` e o token
  em `credential`

#### Scenario: Teste no formulário de edição sem credencial digitada usa a configuração salva
- **WHEN** o usuário aciona "Testar conexão" no formulário de edição de
  um servidor `BearerToken` ou `CustomHeader`, com o tipo mantido e o
  campo de credencial em branco
- **THEN** a interface envia `POST /mcp-servers/{id}/test`, e informa na
  própria tela que o teste está usando a configuração salva daquele
  servidor — com `CustomHeader`, inclusive o nome do header, e que uma
  alteração no nome só é testada depois de salvar

#### Scenario: Teste no formulário de edição com credencial digitada usa a configuração informada
- **WHEN** o usuário digita uma credencial no formulário de edição e
  aciona "Testar conexão"
- **THEN** a interface envia `POST /mcp-servers/test` com os valores
  atuais do formulário, incluindo a credencial digitada

#### Scenario: Teste desabilitado na troca de tipo sem a credencial nova
- **WHEN** o usuário, no formulário de edição, troca o tipo salvo por
  outro tipo que exige credencial e deixa a credencial em branco
- **THEN** a ação "Testar conexão" fica desabilitada, a interface exibe a
  nota "Para testar o tipo novo, informe a credencial nova", e nenhuma
  requisição a `POST /mcp-servers/{id}/test` é enviada

#### Scenario: Teste no formulário de edição com tipo None usa a configuração informada
- **WHEN** o tipo selecionado no formulário de edição é `None`, mantido
  ou trocado, e o usuário aciona "Testar conexão"
- **THEN** a interface envia `POST /mcp-servers/test` com a URL e o tipo
  atuais do formulário, e não exibe a nota de configuração salva

#### Scenario: Testar sem URL preenchida é barrado antes da requisição
- **WHEN** o usuário aciona "Testar conexão" no formulário com o campo de
  URL vazio
- **THEN** a interface indica que a URL precisa ser informada antes de
  testar e não envia nenhuma requisição de teste

#### Scenario: Testar token em header customizado sem nome ou sem token é barrado antes da requisição
- **WHEN** o usuário aciona "Testar conexão" com "Token em header
  customizado" e o nome do header ou o token em branco, num caso em que o
  teste usaria `POST /mcp-servers/test`
- **THEN** a interface indica, no campo vazio, que ele precisa ser
  informado antes de testar, e não envia nenhuma requisição de teste

#### Scenario: Erro de validação do teste aparece nos campos
- **WHEN** `POST /mcp-servers/test` responde 400 com erros em
  `authHeaderName` ou `credential`
- **THEN** a interface exibe cada mensagem no campo correspondente, exibe
  inline que nenhuma conexão foi tentada, e não exibe o resultado "Falha
  na conexão" nem nenhum `failureReason`

#### Scenario: Erro do teste substitui o erro de um envio anterior no mesmo campo
- **WHEN** um envio do formulário recebeu 400 em `authHeaderName`, o
  usuário edita o nome do header, aciona "Testar conexão" e
  `POST /mcp-servers/test` responde 400 com outra mensagem em
  `authHeaderName`
- **THEN** a interface exibe no campo "Nome do header" a mensagem do
  teste, e não a do envio anterior

#### Scenario: Editar um campo esconde o erro do envio anterior
- **WHEN** um envio do formulário recebeu 400 em `authHeaderName` e o
  usuário edita o nome do header, sem enviar nem testar de novo
- **THEN** a interface deixa de exibir a mensagem do envio anterior no
  campo "Nome do header"

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
- **WHEN** o usuário altera a URL, o tipo de autenticação, o nome do
  header ou a credencial no formulário depois de já ter exibido um
  resultado de teste
- **THEN** a interface deixa de exibir o resultado do teste anterior, até
  que um novo teste seja executado para a configuração atual

#### Scenario: Nenhuma notificação (toast) nem diálogo modal é usado para o resultado do teste
- **WHEN** o usuário aciona "Testar conexão" em qualquer um dos dois
  contextos (formulário ou página de detalhe)
- **THEN** a interface não exibe o resultado como notificação (toast) nem
  como diálogo modal, apenas como conteúdo inline na própria tela
