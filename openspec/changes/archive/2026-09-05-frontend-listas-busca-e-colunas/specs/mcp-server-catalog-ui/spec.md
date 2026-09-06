## MODIFIED Requirements

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
