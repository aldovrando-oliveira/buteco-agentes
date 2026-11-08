## RENAMED Requirements

- FROM: `### Requirement: O login começa pelo /entrar e só navega ao authorize depois do 204`
- TO: `### Requirement: O login começa pelo /entrar e só navega ao authorize com a sessão completa`

## MODIFIED Requirements

### Requirement: O login começa pelo /entrar e só navega ao authorize com a sessão completa
Sem sessão no navegador, o painel SHALL mandar toda rota protegida para `/entrar`, guardando a rota pedida como `voltarPara`. Depois que a sessão do emissor fica completa (o `204` do `POST /auth/session/second-factor`, ou "Continuar" depois do `200` do `POST /auth/session/enrollment/confirm`; o `POST /auth/session` não responde mais `204`), o painel SHALL começar o fluxo: gerar o login em andamento e navegar a `/connect/authorize` com:

- `response_type=code`, `client_id=buteco-painel`, `redirect_uri` igual a `<origem do painel>/entrar/retorno`;
- `scope=openid offline_access buteco-api buteco-inbox buteco-connectors`;
- `code_challenge` e `code_challenge_method=S256`;
- `state`.

Quando `/entrar` chega com `?retorno=…`, o painel SHALL seguir esse valor, depois da sessão completa, só quando ele, decodificado uma vez, começa por `/connect/authorize?`, e SHALL navegar para exatamente esse valor, sem decodificá-lo de novo e sem recompor a URL. Qualquer outro valor SHALL levar à tela inicial: o painel começa um fluxo novo com `voltarPara` `/`, que o cookie recém-gravado conclui sem formulário.

`voltarPara` SHALL ser aceito só como caminho do próprio painel: começa por `/`, não por `//`, não contém `\` e não começa por `/entrar`. Qualquer outro valor SHALL virar `/`.

#### Scenario: Rota protegida sem sessão vai ao /entrar com voltarPara
- **WHEN** alguém sem o registro da sessão navega para `/knowledge-bases`
- **THEN** o painel mostra `/entrar`, sem renderizar a rota pedida, e o login seguinte termina em `/knowledge-bases`

#### Scenario: Sessão completa leva ao authorize com PKCE e state
- **WHEN** o `POST /auth/session/second-factor` responde `204`
- **THEN** o painel navega a `/connect/authorize` com `client_id=buteco-painel`, `redirect_uri` `<origem>/entrar/retorno`, os quatro escopos, `code_challenge_method=S256`, um `code_challenge` e um `state`

#### Scenario: retorno válido é seguido
- **WHEN** `/entrar?retorno=%2Fconnect%2Fauthorize%3Fclient_id%3Dbuteco-painel%26…` completa o segundo passo com `204`
- **THEN** o painel navega para `/connect/authorize?client_id=buteco-painel&…`

#### Scenario: retorno é seguido como foi conferido
- **WHEN** o `retorno` decodificado é `/connect/authorize?client_id=buteco-painel&redirect_uri=https%3A%2F%2Fpainel%2Fentrar%2Fretorno&state=a%2Bb%3D` e o segundo passo responde `204`
- **THEN** o painel navega para essa mesma string, com o `%2F`, o `%2B` e o `%3D` intactos

#### Scenario: retorno fora da regra vai para a tela inicial
- **WHEN** o `retorno` é absoluto (`https://outro/connect/authorize?x`), começa por `//` ou `/\`, tem outro caminho (`/connect/authorizex?`, `/connect/logout?`) ou está codificado duas vezes, e o segundo passo responde `204`
- **THEN** o painel não navega para o valor recebido: começa um fluxo novo com `voltarPara` `/`, e o login termina na tela inicial

#### Scenario: voltarPara de outra origem é recusado
- **WHEN** o login em andamento traz `voltarPara` igual a `//host-externo/x`
- **THEN** o login termina em `/`
