## ADDED Requirements

### Requirement: O papel no access token e no id token
O authorize SHALL pôr no principal do usuário o claim `role` com o papel gravado dele (`admin`, `operator` ou `reader`), com destino no access token **e** no id token. Na troca do código e em toda renovação de um usuário aceito pela conferência de estado e carimbo, o emissor SHALL substituir o `role` do principal pelo papel gravado naquele momento, com os dois destinos gravados de novo, antes de emitir os tokens. O refresh emitido na renovação SHALL levar o papel novo.

#### Scenario: Papel no login
- **WHEN** um usuário de papel `operator` entra pelo fluxo do painel
- **THEN** o access token e o id token trazem `role` `operator`

#### Scenario: Papel novo na renovação
- **WHEN** o papel de um usuário com sessão viva muda de `admin` para `reader`, e a sessão renova
- **THEN** o access token e o id token da renovação trazem `role` `reader`, e o refresh emitido nela, ao renovar de novo, também

#### Scenario: Refresh de antes do papel
- **WHEN** um refresh emitido antes desta change, cujo principal não tem `role`, renova
- **THEN** os tokens emitidos trazem o `role` gravado do usuário

#### Scenario: O papel não é o subject
- **WHEN** um usuário de papel `admin` obtém um access token pelo fluxo do painel
- **THEN** o `sub` continua sendo o id dele, e o `role` é um claim à parte
