## ADDED Requirements

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
