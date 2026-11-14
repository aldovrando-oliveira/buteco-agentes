## MODIFIED Requirements

### Requirement: Os próprios dados
`GET /account` SHALL responder `200` com `id`, `nome`, `email`, `papel` (`admin`, `operador` ou `leitor`, o gravado), `criadoEm`, `ultimoAcesso`, `duasEtapas` (verdadeiro quando a verificação em 2 etapas está ligada), `formasDeEntrada` e `contasConectadas` do usuário que chama, qualquer que seja o papel. `formasDeEntrada` e `contasConectadas` SHALL ter o formato e as regras do detalhe de usuário (`user-management`).

#### Scenario: Ler os próprios dados
- **WHEN** um usuário ativo chama `GET /account`
- **THEN** a resposta é `200` com o id, o nome, o e-mail e o papel dele, e `duasEtapas` igual à situação gravada

#### Scenario: Papel gravado, e não o do token
- **WHEN** um usuário rebaixado a `leitor` chama `GET /account` com o access token emitido quando era Admin
- **THEN** a resposta é `200` com `papel` `leitor`

#### Scenario: As formas de entrada da própria conta
- **WHEN** um usuário com senha e vínculo com o provedor `corporativo` chama `GET /account`
- **THEN** a resposta traz `formasDeEntrada` igual a `["senha", "google"]` e `contasConectadas` com a chave `corporativo` e o e-mail confirmado
