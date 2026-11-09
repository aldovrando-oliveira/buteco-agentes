## MODIFIED Requirements

### Requirement: Os próprios dados
`GET /account` SHALL responder `200` com `id`, `nome`, `email`, `papel` (`admin`, `operador` ou `leitor`, o gravado), `criadoEm`, `ultimoAcesso` e `duasEtapas` (verdadeiro quando a verificação em 2 etapas está ligada) do usuário que chama, qualquer que seja o papel.

#### Scenario: Ler os próprios dados
- **WHEN** um usuário ativo chama `GET /account`
- **THEN** a resposta é `200` com o id, o nome, o e-mail e o papel dele, e `duasEtapas` igual à situação gravada

#### Scenario: Papel gravado, e não o do token
- **WHEN** um usuário rebaixado a `leitor` chama `GET /account` com o access token emitido quando era Admin
- **THEN** a resposta é `200` com `papel` `leitor`
