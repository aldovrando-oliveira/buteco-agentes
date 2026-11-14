## ADDED Requirements

### Requirement: A sessão pendente também nasce do aceite de convite pelo provedor
O retorno que aceita um convite pelo provedor (`federated-login`, "Decisão do aceite de convite no retorno") SHALL gravar o cookie `buteco-sessao` da **sessão pendente**, com os mesmos atributos e o mesmo conteúdo do retorno do login federado ("A sessão pendente também nasce do retorno do provedor federado"), só **depois** de a transação do consumo e do vínculo terminar com sucesso, e com o usuário relido no estado ativo. Como o convidado nunca tem a verificação em 2 etapas, o passo SHALL ser o `cadastro`, completado pelas rotas de hoje (`POST /auth/session/enrollment/setup` e `/confirm`). O retorno SHALL responder `302` para o `/entrar`, e SHALL NOT responder `302` ao `/connect/authorize`. O authorize, a troca do código e a renovação SHALL NOT mudar.

#### Scenario: O cookie do aceite não emite código
- **WHEN** um convite é aceito pelo provedor, e o mesmo navegador pede `GET /connect/authorize` com o cookie gravado pelo retorno
- **THEN** a resposta é `302` para `/entrar`, sem código

#### Scenario: O cadastro completa a sessão do aceite
- **WHEN** depois do aceite, o painel chama `POST /auth/session/enrollment/setup` e `/confirm` com o cookie do retorno e um código válido
- **THEN** o `setup` responde `200` com o segredo, o `confirm` responde `200` com os códigos de recuperação e o `Set-Cookie: buteco-sessao` completo, e o authorize seguinte com o cookie emite o código

#### Scenario: O passo do aceite é sempre o cadastro
- **WHEN** um convite é aceito pelo provedor
- **THEN** o `Location` do retorno traz `segundoPasso=cadastro`, e o `POST /auth/session/second-factor` com o cookie do retorno responde `401` "A entrada expirou. Informe e-mail e senha de novo."

### Requirement: Os logs do emissor não levam dado pessoal nem segredo, em nenhuma categoria
Nenhuma linha de log do login por senha, do segundo passo, do cadastro da verificação, do authorize, da troca do código, da renovação, do logout e do `client_credentials` (de serviço e de aplicação externa) SHALL conter o e-mail, o nome, a senha, o segredo ou o código das 2 etapas, os códigos de recuperação, o código do authorize, o `code_verifier`, os tokens nem o segredo do cliente. Vale para as linhas do Buteco e para as da biblioteca do servidor OIDC (`OpenIddict.Server`), no nível configurado em todo ambiente: o `appsettings.json` SHALL fixar as categorias `OpenIddict` em `Warning` ou acima, porque a biblioteca grava o pedido de token com o `code_verifier` em `Information` e os tokens inteiros em `Trace`.

#### Scenario: O login por senha, a renovação e o logout não vazam
- **WHEN** um usuário entra por senha, cadastra a verificação, entra de novo com o código, renova e sai, com o log capturado em todas as categorias do Buteco e do OpenIddict
- **THEN** nenhuma linha contém o e-mail, o nome, a senha, o segredo das 2 etapas, os códigos, o código do authorize, o `code_verifier` nem os tokens

#### Scenario: O client_credentials não vaza
- **WHEN** um cliente de serviço e uma aplicação externa pedem token por `client_credentials`
- **THEN** nenhuma linha contém o segredo do cliente nem o token emitido

#### Scenario: Sem o filtro da biblioteca, o pedido de token vaza
- **WHEN** a categoria `OpenIddict` volta ao nível `Information`
- **THEN** a linha `OpenIddictServerDispatcher[6075]` leva o `code_verifier` do pedido de token

