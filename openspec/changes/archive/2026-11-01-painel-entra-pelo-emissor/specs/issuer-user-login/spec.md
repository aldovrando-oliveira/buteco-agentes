## MODIFIED Requirements

### Requirement: Refresh rotativo e deslizante de 14 dias; todo token novo relê o usuário
O emissor SHALL emitir refresh token com duração de 14 dias, rotativo: o refresh apresentado vira resgatado, e um novo é emitido com a duração inteira. O access token SHALL durar 15 minutos.

O refresh resgatado SHALL continuar aceito por 30 segundos depois do primeiro resgate, para renovações concorrentes do mesmo cliente, cada uma recebendo um par novo. Reapresentado depois disso, SHALL ser recusado com `invalid_grant`, e os tokens da autorização dele SHALL ser revogados. O painel conta com essa tolerância para repetir uma renovação por até 25 segundos (`panel-issuer-session`).

O principal do usuário SHALL carregar o carimbo de segurança (`security_stamp`) vigente no authorize. O código de autorização e o refresh SHALL levá-lo adiante, o refresh a cada rotação. O carimbo SHALL NOT sair no access token nem no id token.

Todo token novo de usuário SHALL conferir o estado e o carimbo: na troca do código (grant `authorization_code`) e em cada renovação (grant `refresh_token`), o emissor SHALL reler o usuário e SHALL recusar com `invalid_grant` quando:

- ele não existir mais;
- estiver desativado;
- o carimbo gravado for diferente do carimbo do código ou do refresh apresentado.

#### Scenario: Renovação emite novo par
- **WHEN** um refresh token válido de um usuário ativo é apresentado
- **THEN** a resposta traz um novo access token e um novo refresh token, e o refresh apresentado passa a resgatado

#### Scenario: O mesmo refresh reapresentado dentro da tolerância dá outro par
- **WHEN** um refresh token é resgatado, e o mesmo refresh é apresentado de novo 10 segundos depois (a segunda aba, que chegou depois do resgate)
- **THEN** as duas respostas são `200 OK` com pares novos, e o refresh mais novo de cada uma renova de novo com `200 OK`

#### Scenario: Reuso depois da tolerância revoga a cadeia
- **WHEN** um refresh resgatado há mais de 30 segundos é reapresentado
- **THEN** a resposta é `invalid_grant`, e o refresh emitido no primeiro resgate também passa a ser recusado

#### Scenario: Access token dura 15 minutos
- **WHEN** um access token de usuário é emitido
- **THEN** o `exp` dele é 15 minutos depois do `iat`

#### Scenario: Usuário desativado não renova
- **WHEN** o refresh token de um usuário desativado depois do login é apresentado
- **THEN** a resposta é `invalid_grant`, e nenhum token é emitido

#### Scenario: Desativado entre o authorize e a troca do código não recebe tokens
- **WHEN** um usuário obtém um código de autorização, é desativado antes da troca, e o código é apresentado com o `code_verifier` correto
- **THEN** a resposta é `invalid_grant`, e nenhum token é emitido

#### Scenario: Desativado e reativado não renova com o refresh de antes
- **WHEN** um usuário obtém um refresh token, é desativado (o que troca o carimbo de segurança) e reativado, e o refresh de antes da desativação é apresentado
- **THEN** a resposta é `invalid_grant`, e nenhum token é emitido

#### Scenario: O carimbo não sai nos tokens do cliente
- **WHEN** um usuário completa o fluxo e obtém access token e id token
- **THEN** nenhum dos dois contém o carimbo de segurança
