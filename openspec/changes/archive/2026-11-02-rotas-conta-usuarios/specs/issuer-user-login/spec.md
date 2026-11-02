## ADDED Requirements

### Requirement: A renovação leva o nome e o e-mail vigentes
Na renovação (grant `refresh_token`) de um usuário aceito pela conferência de estado e carimbo, o emissor SHALL substituir no principal os claims `name` e `email` pelos valores gravados do usuário naquele momento, antes de emitir os tokens. O id token e o refresh novo SHALL sair com esses valores, e o destino dos dois claims SHALL continuar sendo só o id token.

Sem isso, o principal da renovação é o do login (OpenIddict 7.7.2, `OpenIddictServerHandlers.Exchange.cs` l. 2132–2135), e um nome ou e-mail editado não chegaria ao painel, que os lê do id token, enquanto a sessão renovasse.

#### Scenario: Nome editado chega na renovação seguinte
- **WHEN** o nome de um usuário com sessão viva é editado, e a sessão renova
- **THEN** o id token da renovação traz o nome novo, e o refresh emitido nela, ao renovar de novo, também

#### Scenario: E-mail editado chega na renovação seguinte
- **WHEN** o e-mail de um usuário com sessão viva é editado, e a sessão renova
- **THEN** o id token da renovação traz o e-mail novo

#### Scenario: Os claims continuam fora do access token
- **WHEN** uma sessão renova depois da edição
- **THEN** o access token emitido não contém `name` nem `email`
