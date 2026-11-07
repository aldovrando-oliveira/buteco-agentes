## MODIFIED Requirements

### Requirement: 401 renova uma vez antes de encerrar a sessão
Toda chamada autenticada do painel aos três apps SHALL anexar `Authorization: Bearer <accessToken>`. No `401`, SHALL renovar com o token que falhou e repetir a chamada uma vez.

O `401` com `Content-Type: application/problem+json` é a recusa da própria rota (a senha ou o código conferido por ela estava errado), e não do token: ele SHALL voltar à tela como resposta, SHALL NOT disparar a renovação, SHALL NOT ser repetido e SHALL NOT encerrar a sessão, em qualquer dos três apps. O `401` de token recusado não traz esse corpo (`issuer-token-validation`), e só ele segue as regras abaixo.

- No `apps/api` e no `apps/inbox`, o segundo `401` SHALL encerrar a sessão: apagar o registro e ir a `/entrar?motivo=sessao-expirada`, com a rota atual como `voltarPara`.
- No `apps/connectors`, o segundo `401` SHALL NOT encerrar a sessão: a chamada falha com o erro da tela, e o registro continua.
- Renovação com `invalid_grant` SHALL encerrar a sessão em qualquer dos três apps, inclusive no `apps/connectors`: o registro já foi apagado pela renovação.
- Renovação que desiste por indisponibilidade (`503` ou falha de rede até o fim da janela) SHALL NOT encerrar a sessão: a chamada SHALL falhar com o `401` recebido, sem repetição, e o registro SHALL continuar.

#### Scenario: 401 seguido de sucesso não desloga
- **WHEN** `GET /agents` responde `401` e, depois da renovação, `200`
- **THEN** a tela recebe a lista, e o registro tem o par novo

#### Scenario: Segundo 401 no apps/api encerra a sessão
- **WHEN** `GET /agents` responde `401` antes e depois da renovação
- **THEN** o registro é apagado, e o painel vai a `/entrar?motivo=sessao-expirada` com `voltarPara` igual à rota atual

#### Scenario: Segundo 401 no apps/inbox encerra a sessão
- **WHEN** `GET /channels` responde `401` antes e depois da renovação
- **THEN** o registro é apagado, e o painel vai a `/entrar?motivo=sessao-expirada`

#### Scenario: Segundo 401 no apps/connectors não encerra
- **WHEN** a consulta de pastas do `apps/connectors` responde `401` antes e depois da renovação
- **THEN** o registro continua, e o painel não navega para `/entrar`

#### Scenario: invalid_grant na renovação encerra também no apps/connectors
- **WHEN** a consulta de pastas do `apps/connectors` responde `401`, e a renovação recebe `400` com `error=invalid_grant`
- **THEN** o registro é apagado, e o painel vai a `/entrar?motivo=sessao-expirada`

#### Scenario: Emissor indisponível depois do 401 não encerra a sessão
- **WHEN** `GET /agents` responde `401`, e o `/connect/token` responde `503` em todas as tentativas da renovação
- **THEN** a tela recebe o erro do `401`, a rota não é repetida, o registro continua, e o painel não navega para `/entrar`

#### Scenario: 401 com problem+json volta à tela sem renovar
- **WHEN** `POST /account/password` responde `401` com `Content-Type: application/problem+json` e `title` "Senha atual inválida."
- **THEN** a tela recebe o `401` com o `title`, a renovação não é chamada, o pedido sai uma vez só, o registro continua, e o painel não navega para `/entrar`

#### Scenario: 401 sem corpo continua renovando
- **WHEN** `POST /account/password` responde `401` sem corpo e, depois da renovação, `204`
- **THEN** a renovação é chamada com o token que falhou, o pedido sai de novo com o token novo, e a tela recebe o `204`

### Requirement: Encerrar sessão revoga pelo emissor e avisa as outras abas
"Sair" SHALL, sob o lock da renovação, ler o `idToken` do registro e apagar o registro, e depois navegar a `/connect/logout?id_token_hint=<idToken>&post_logout_redirect_uri=<origem do painel>/entrar`. Sem `idToken`, SHALL navegar sem o hint. Enquanto espera o lock, o item SHALL mostrar "Saindo…".

O logout SHALL marcar a aba como saindo **antes** de apagar o registro. Enquanto a aba está saindo, a rota protegida SHALL mostrar só "Saindo…" (`role="status"`), e SHALL NOT levar ao `/entrar` do próprio painel: o registro sai antes de a navegação ao `/connect/logout` descarregar a página, e nesse intervalo o `/entrar` interno aparecia por um instante e consumia o aviso de senha alterada (`operator-login-ui`). Isso vale para todo logout desta aba: o "Sair", a troca de senha e o "Encerrar e sair" de Minha conta.

Toda aba aberta da mesma origem SHALL ir a `/entrar` quando o registro sair por outra aba, pelo evento `storage`. A marcação é só da aba que sai.

#### Scenario: Sair apaga antes de navegar
- **WHEN** o operador aciona "Sair"
- **THEN** no instante da navegação a `/connect/logout` o registro já não existe, e a URL leva o `id_token_hint` e o `post_logout_redirect_uri` `<origem>/entrar`

#### Scenario: A aba que sai não passa pelo /entrar interno
- **WHEN** o operador aciona "Sair", o registro é apagado, e a página ainda não foi descarregada pela navegação ao `/connect/logout`
- **THEN** a rota protegida mostra "Saindo…", continua na rota em que estava, e o `/entrar` do painel não é renderizado

#### Scenario: A outra aba vai ao /entrar
- **WHEN** o registro é apagado por outra aba
- **THEN** esta aba navega a `/entrar`
