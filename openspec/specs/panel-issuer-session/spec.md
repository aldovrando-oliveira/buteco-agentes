# panel-issuer-session Specification

## Purpose

Diz como o painel (`apps/frontend`) entra pelo emissor do `apps/api` e mantém a sessão:
o `/entrar` que só navega ao authorize depois do `204`, o PKCE e o `state` gerados e
conferidos no navegador, o `/entrar/retorno` que tira o código da URL antes da troca, o
registro da sessão no `localStorage` compartilhado entre as abas, a renovação coordenada
por Web Lock, o `401` que renova antes de encerrar, o `503` que nunca desloga e o
encerramento pelo `/connect/logout`. Criada pela change `painel-entra-pelo-emissor` (#217).
## Requirements
### Requirement: PKCE S256 e state gerados no navegador, de uso único e com prazo
O painel SHALL gerar, a cada início de fluxo:

- `code_verifier` de 32 bytes de `crypto.getRandomValues`, em base64url sem preenchimento (43 caracteres);
- `code_challenge` igual ao base64url sem preenchimento do SHA-256 do verifier;
- `state` de 32 bytes aleatórios, em base64url.

O login em andamento (`verifier`, `state`, `voltarPara`, instante de criação) SHALL ficar no `sessionStorage` da aba, sob uma chave só, e um início novo SHALL substituir o anterior. O `/entrar/retorno` SHALL lê-lo e apagá-lo antes de qualquer outra decisão, e SHALL recusá-lo com mais de 10 minutos.

#### Scenario: Challenge é o do vetor da RFC 7636
- **WHEN** o verifier é `dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk`
- **THEN** o challenge é `E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM`

#### Scenario: Verifier tem o tamanho e o alfabeto certos
- **WHEN** um fluxo começa
- **THEN** o verifier tem 43 caracteres de `[A-Za-z0-9_-]`, e o challenge não contém `+`, `/` nem `=`

#### Scenario: Login em andamento é de uso único
- **WHEN** o `/entrar/retorno` é aberto duas vezes com o mesmo `code` e `state`
- **THEN** só a primeira troca o código; a segunda mostra "Não foi possível concluir a entrada. Entre de novo." sem pedido ao `/connect/token`

#### Scenario: Login em andamento vencido é recusado
- **WHEN** o `/entrar/retorno` chega 11 minutos depois do início do fluxo, com o `state` certo
- **THEN** nenhum código é trocado, e o painel mostra "Não foi possível concluir a entrada. Entre de novo."

### Requirement: /entrar/retorno tira o código da URL, confere o state e troca o código
O `/entrar/retorno` SHALL, antes de qualquer pedido, ler `code`, `state`, `error` e `error_description` da URL uma vez e trocar a URL por `/entrar/retorno` sem query, por navegação com `replace`. O processamento SHALL usar os valores lidos, e SHALL rodar uma vez por montagem. Isso SHALL valer para toda saída: sucesso, `error`, `state` errado e login em andamento ausente.

Depois:

- sem login em andamento, ou `state` diferente do guardado, ou login em andamento vencido: SHALL mostrar "Não foi possível concluir a entrada. Entre de novo.", com o botão "Entrar", sem trocar nada;
- com `error` e `state` conferido: `login_required` SHALL levar a `/entrar` sem aviso de erro; qualquer outro `error` SHALL mostrar "Não foi possível concluir a entrada." com "Tentar de novo", e o `error_description` SHALL ir ao console, e não à tela;
- com `code` e `state` conferido: SHALL enviar `POST /connect/token` com `grant_type=authorization_code`, `code`, `redirect_uri`, `client_id` e `code_verifier`.

Na resposta da troca:

- `200`: SHALL gravar o registro da sessão, sob o lock da renovação, e navegar a `voltarPara`;
- `invalid_grant`: SHALL levar a `/entrar?motivo=falha-na-entrada`, com o `voltarPara` do login em andamento, sem gravar registro;
- `invalid_client` ou `503`: SHALL repetir uma vez depois do `Retry-After`, e, falhando de novo, mostrar "O painel está iniciando. Tente de novo em alguns segundos." com "Tentar de novo".

O `iss` da resposta SHALL NOT ser exigido enquanto o painel conhecer um emissor só (registro na #189).

#### Scenario: A URL fica sem o código antes da troca
- **WHEN** o `/entrar/retorno?code=abc&state=<o guardado>` é aberto
- **THEN** a URL passa a `/entrar/retorno` sem query por `REPLACE`, no instante do `POST /connect/token` ela já não tem `code`, e o pedido leva `code=abc`

#### Scenario: A URL fica sem query também no erro
- **WHEN** o `/entrar/retorno?error=access_denied&state=<o guardado>` é aberto
- **THEN** a URL passa a `/entrar/retorno` sem query por `REPLACE`, e o painel mostra "Não foi possível concluir a entrada."

#### Scenario: state diferente não troca o código
- **WHEN** o `/entrar/retorno` chega com um `state` diferente do guardado
- **THEN** nenhum pedido vai ao `/connect/token`, e o painel mostra "Não foi possível concluir a entrada. Entre de novo."

#### Scenario: Troca bem-sucedida grava a sessão e volta à rota pedida
- **WHEN** o `/connect/token` responde `200` com `access_token`, `refresh_token`, `id_token` e `expires_in`
- **THEN** o registro da sessão existe com os três tokens, e o painel navega a `voltarPara`

#### Scenario: Código já usado volta ao /entrar
- **WHEN** o `/connect/token` responde `400` com `error=invalid_grant`, e o login em andamento tem `voltarPara` `/agents`
- **THEN** o painel vai a `/entrar?motivo=falha-na-entrada&voltarPara=%2Fagents`, e nenhum registro é gravado

### Requirement: A sessão do navegador fica num registro só no localStorage
O painel SHALL guardar a sessão no `localStorage`, na chave `buteco.sessao`, com `accessToken`, `accessExpiresAt`, `refreshToken`, `idToken`, `nome` e `email`. `nome` e `email` SHALL sair do payload do id token recebido do `/connect/token`, e SHALL servir só para exibição.

Recarregar a página e abrir outra aba da mesma origem SHALL usar a mesma sessão, sem pedir login. O painel SHALL NOT guardar o refresh token em nenhum outro lugar que viva mais que uma renovação.

Registro ilegível SHALL ser tratado como ausente.

#### Scenario: Recarregar não desloga
- **WHEN** o registro da sessão existe com o access token válido e a página é recarregada
- **THEN** a rota pedida carrega sem passar por `/entrar`

#### Scenario: Outra aba usa a mesma sessão
- **WHEN** uma segunda instância do painel abre sobre o mesmo storage com o registro presente
- **THEN** ela carrega a rota protegida sem passar por `/entrar`

#### Scenario: Registro corrompido é ausência
- **WHEN** `buteco.sessao` contém um texto que não é o registro
- **THEN** a rota protegida vai a `/entrar`, sem erro na tela

### Requirement: Renovação coordenada entre abas por Web Lock
Antes de cada chamada autenticada, o painel SHALL renovar quando o access token vence em menos de 60 segundos. Não SHALL haver timer de renovação.

A renovação SHALL rodar sob o Web Lock exclusivo `buteco.sessao.renovar` e, dentro dele:

- reler o registro; ausente, a sessão acabou;
- se o access token do registro não é o que motivou a renovação e vence em mais de 60 segundos, usar o registro, sem pedido;
- senão, enviar `POST /connect/token` com `grant_type=refresh_token`, o `refreshToken` do registro e `client_id`, com prazo de 10 segundos por tentativa;
- `200`: gravar o registro inteiro antes de soltar o lock;
- `400 invalid_grant`: apagar o registro e encerrar a sessão;
- `503` ou falha de rede: repetir depois do `Retry-After` (ou 1 segundo), sem começar tentativa nova depois de 25 segundos da primeira; esgotado o prazo, desistir **sem** apagar o registro.

#### Scenario: Duas renovações simultâneas não derrubam a sessão
- **WHEN** duas abas sobre o mesmo storage, com o access token vencendo, chamam uma rota ao mesmo tempo, contra um `/connect/token` de rotação estrita (o refresh resgatado reapresentado responde `invalid_grant` e revoga a cadeia)
- **THEN** o `/connect/token` recebe um pedido só, as duas chamadas saem com o access token novo, e o registro termina com o par novo

#### Scenario: A renovação reaproveita a de outra aba
- **WHEN** a aba A renova e grava, e a aba B, que esperava o lock com o access token antigo, entra nele
- **THEN** B usa o registro gravado por A, sem pedido ao `/connect/token`

#### Scenario: Renovação com tempo de sobra não acontece
- **WHEN** o access token vence em mais de 60 segundos e uma rota é chamada
- **THEN** nenhum pedido vai ao `/connect/token`

#### Scenario: 503 na renovação não apaga a sessão
- **WHEN** o `/connect/token` responde `503` com `Retry-After: 5` em todas as tentativas
- **THEN** a última tentativa começa antes de 25 segundos da primeira, o registro continua presente, e o painel não navega para `/entrar`

#### Scenario: invalid_grant na renovação encerra a sessão
- **WHEN** o `/connect/token` responde `400` com `error=invalid_grant`
- **THEN** o registro é apagado, e o painel vai a `/entrar?motivo=sessao-expirada` com a rota atual como `voltarPara`

### Requirement: Todo escritor do registro escreve sob o lock da renovação
A gravação depois da troca do código, a renovação, o logout e o fim de sessão SHALL alterar o registro só dentro do Web Lock `buteco.sessao.renovar`. Leitura SHALL NOT pedir o lock.

Dentro do lock, o fim de sessão SHALL apagar o registro só quando ele ainda tem o `refreshToken` (fim por `invalid_grant`) ou o `accessToken` (segundo `401`) que a aba usou. O logout SHALL apagar sempre.

#### Scenario: Logout durante uma renovação termina sem sessão
- **WHEN** a aba A está com o pedido de renovação ao `/connect/token` em voo, a aba B aciona "Sair", e a resposta `200` de A chega depois
- **THEN** o registro termina ausente, e o `id_token_hint` de B é o id token novo recebido por A

#### Scenario: Fim de sessão não apaga a sessão nova de outra aba
- **WHEN** a aba A recebe o segundo `401` com o access token `t1`, e o registro já tem o access token `t2`, gravado por um login em outra aba
- **THEN** o registro continua com `t2`

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

### Requirement: 503 é indisponibilidade passageira e nunca desloga
Nenhum `503` de nenhum dos três apps SHALL apagar o registro nem levar a `/entrar`. A chamada SHALL falhar com um erro que traga o `Retry-After` lido do cabeçalho.

O `QueryClient` do painel SHALL repetir consulta que falhou com `503` até 3 vezes, esperando o `Retry-After` limitado a 1–30 segundos (1 segundo sem cabeçalho). Mutação SHALL NOT ser repetida. Consulta que falhou com `403` SHALL NOT ser repetida: o papel não muda entre as tentativas. Os outros erros SHALL manter a repetição de antes, e consulta com `retry` próprio SHALL NOT mudar.

#### Scenario: Consulta com 503 repete esperando o Retry-After
- **WHEN** uma consulta responde `503` com `Retry-After: 2` e depois `200`
- **THEN** a segunda tentativa sai 2 segundos depois da primeira, e a tela mostra o dado

#### Scenario: Mutação com 503 não repete
- **WHEN** uma mutação responde `503`
- **THEN** só um pedido é feito, a tela mostra o erro, e o registro continua

#### Scenario: Retry-After fora da faixa é limitado
- **WHEN** uma consulta responde `503` com `Retry-After: 120`
- **THEN** a tentativa seguinte espera 30 segundos

#### Scenario: Consulta com 403 não repete
- **WHEN** uma consulta responde `403`
- **THEN** só um pedido é feito à rota, e a tela mostra o estado do `403` sem esperar novas tentativas

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

### Requirement: Fora de contexto seguro, o painel avisa e não começa o fluxo
Sem `navigator.locks` ou sem `crypto.subtle`, o `/entrar` SHALL mostrar "O painel precisa ser aberto por HTTPS." e SHALL NOT enviar `POST /auth/session` nem começar o fluxo.

#### Scenario: Sem Web Locks o formulário não envia
- **WHEN** o `/entrar` abre num navegador sem `navigator.locks`
- **THEN** o aviso aparece, e submeter o formulário não faz pedido

### Requirement: Nenhum HTML vindo de dado no painel
O código do `apps/frontend` SHALL NOT usar `dangerouslySetInnerHTML`, e o `ReactMarkdown` SHALL NOT receber `rehype-raw`. O lint SHALL reprovar os dois com regras do próprio ESLint, sem dependência nova: `no-restricted-syntax` para o atributo `dangerouslySetInnerHTML` e `no-restricted-imports` para `rehype-raw`.

#### Scenario: Lint reprova HTML cru
- **WHEN** um componente usa `dangerouslySetInnerHTML`
- **THEN** `npm run lint` reprova nomeando o arquivo

#### Scenario: Lint reprova o rehype-raw
- **WHEN** um arquivo importa `rehype-raw`
- **THEN** `npm run lint` reprova nomeando o arquivo

### Requirement: Em desenvolvimento, o fluxo do emissor passa pelo proxy do Vite
O `npm run dev` SHALL encaminhar `/auth`, `/connect` e `/.well-known` ao `apps/api` local, sem `changeOrigin`, para o cookie `buteco-sessao` e as navegações do fluxo ficarem na origem do painel. As chamadas às rotas dos três apps SHALL continuar nos endereços `VITE_*_BASE_URL`.

#### Scenario: O proxy cobre os três prefixos
- **WHEN** a configuração do Vite é carregada
- **THEN** `server.proxy` tem `/auth`, `/connect` e `/.well-known` apontando para o `apps/api` local, nenhum com `changeOrigin`

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

### Requirement: 403 renova a sessão uma vez e não repete a chamada
Quando uma chamada pelo `sessionFetch` responde `403`, com ou sem corpo, o painel SHALL pedir uma renovação, sob o lock da renovação, com o access token que recebeu o `403`, e SHALL devolver à tela **o mesmo** `403`, sem repetir a chamada. A renovação existe para o id token trazer o papel gravado.

- Um `403` recebido com um access token que já pediu renovação por `403` SHALL NOT pedir outra.
- A renovação que termina em sessão encerrada (`invalid_grant`, ou registro ausente) SHALL levar a `/entrar?motivo=sessao-expirada`, como no `401`.
- A renovação indisponível SHALL NOT mudar o que a tela recebe.
- Nenhum corpo de pedido, inclusive a senha de quem chama, SHALL ser reenviado por causa de um `403`.

#### Scenario: Um 403, uma renovação, nenhuma repetição
- **WHEN** `POST /agents/a1/deactivate` responde `403`
- **THEN** o `/connect/token` recebe um pedido de renovação, a rota recebe um pedido só, e a tela recebe o `403`

#### Scenario: Dois 403 seguidos, uma renovação
- **WHEN** a sessão renova por um `403`, e duas chamadas seguintes, com o access token novo, respondem `403`
- **THEN** o `/connect/token` recebe uma renovação só depois da primeira, e nenhuma por causa das duas seguintes

#### Scenario: A senha não é reenviada
- **WHEN** `POST /users/invitations` com a senha de quem chama responde `403`
- **THEN** a rota recebe um pedido só, e o corpo com a senha não sai de novo

#### Scenario: invalid_grant na renovação leva ao /entrar
- **WHEN** uma chamada responde `403`, e a renovação responde `400` com `error: invalid_grant`
- **THEN** o registro é apagado, e o painel vai a `/entrar?motivo=sessao-expirada`

#### Scenario: A renovação traz o papel novo
- **WHEN** um Admin rebaixado a Operador recebe `403` em `GET /users`, e a renovação traz um id token com `role` `operator`
- **THEN** a barra lateral perde a seção "Acesso" sem recarregar

