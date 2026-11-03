# user-account Specification

## Purpose
Diz o que a própria pessoa faz com a conta dela no `apps/api`, sob `/account`: ler os
próprios dados, trocar a senha com a atual e a política da senha nova, e ver e encerrar as
sessões vivas, com a do token marcada. Também é dona da regra "toda troca do carimbo de
segurança revoga as sessões da pessoa", que vale para a troca de senha, a desativação
(`user-management`) e a redefinição das 2 etapas (`two-factor-totp`). Introduzida pela change
`rotas-conta-usuarios` (#219). As 2 etapas da própria pessoa estão em `two-factor-totp`; as
telas são da #183.
## Requirements
### Requirement: Rotas de Minha conta atendem só o usuário real do token
As rotas de `/account` desta capability (`GET /account`, `POST /account/password`, `GET /account/sessions`, `POST /account/sessions/{id}/revoke` e `POST /account/sessions/revoke-others`) SHALL atender só o usuário do `sub` do access token do emissor, existente e ativo. Token HMAC de operador, `sub` sem usuário e usuário desativado com access token ainda válido SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada.

#### Scenario: HMAC de operador em Minha conta
- **WHEN** um token HMAC `operator` válido chama cada uma das cinco rotas
- **THEN** todas respondem `403`, e nenhuma senha, carimbo ou autorização muda

#### Scenario: Usuário desativado com access token válido
- **WHEN** um usuário é desativado e, com o access token emitido antes, chama `GET /account`
- **THEN** a resposta é `403`

### Requirement: Os próprios dados
`GET /account` SHALL responder `200` com `id`, `nome`, `email`, `criadoEm`, `ultimoAcesso` e `duasEtapas` (verdadeiro quando a verificação em 2 etapas está ligada) do usuário que chama.

#### Scenario: Ler os próprios dados
- **WHEN** um usuário ativo chama `GET /account`
- **THEN** a resposta é `200` com o id, o nome e o e-mail dele, e `duasEtapas` igual à situação gravada

### Requirement: Política de senha
Toda senha nova SHALL ter de 12 a 128 caracteres, contados pelo comprimento da cadeia, sem remover espaços e sem regra de composição. Senha fora da política SHALL ser recusada com `400`, sem gravar nada.

A política SHALL valer só para a senha nova. A senha já gravada, mesmo mais curta que 12 caracteres (a do bootstrap, por exemplo), SHALL continuar aceita no login e como `senhaAtual` na troca.

#### Scenario: Limites da política
- **WHEN** a nova senha tem 11 ou 129 caracteres
- **THEN** a resposta é `400`, e a senha atual continua valendo

#### Scenario: Limites aceitos
- **WHEN** a nova senha tem exatamente 12 ou exatamente 128 caracteres, inclusive com espaços
- **THEN** a troca é aceita

#### Scenario: Senha atual mais curta que a política continua valendo
- **WHEN** um usuário com uma senha gravada de 8 caracteres entra com ela e, em seguida, a usa como `senhaAtual` para trocar por uma de 12
- **THEN** o login responde `204`, a troca responde `204`, e o login com a nova responde `204`

### Requirement: Trocar a própria senha, com a senha atual
`POST /account/password` com `{ senhaAtual, novaSenha }` SHALL conferir a senha atual como o login confere, e SHALL contar a senha atual ausente ou errada como falha no bloqueio por e-mail do usuário, respondendo `401`. Durante o bloqueio, SHALL responder `423` com `Retry-After`, sem conferir senha nenhuma. O acerto SHALL NOT zerar a contagem do bloqueio.

Com a senha atual certa e a nova dentro da política, SHALL gravar o hash Argon2id da nova, tirar o marcador de hash legado, trocar o `security_stamp`, apagar o link de redefinição de senha pendente e revogar todas as autorizações do usuário, numa mesma transação, respondendo `204`. Isso encerra todas as sessões da pessoa, inclusive a da requisição.

#### Scenario: Senha atual errada conta no bloqueio
- **WHEN** um usuário erra a senha atual cinco vezes seguidas e tenta a sexta vez com a senha atual certa
- **THEN** as cinco primeiras respondem `401`, e a sexta responde `423` com `Retry-After`, sem trocar a senha

#### Scenario: Troca bem-sucedida
- **WHEN** um usuário troca a senha com a atual certa e uma nova dentro da política
- **THEN** a resposta é `204`, o login com a senha antiga responde `401`, e o login com a nova responde `204`

#### Scenario: A troca encerra todas as sessões, inclusive a atual
- **WHEN** um usuário com duas sessões vivas troca a senha por uma delas
- **THEN** a renovação de cada uma das duas recebe `invalid_grant`, e `GET /account/sessions`, por um login novo, lista só a sessão nova

#### Scenario: Cookie de sessão em voo recusado depois da troca
- **WHEN** um usuário obtém o cookie da sessão do emissor e, antes de usá-lo no authorize, a senha dele é trocada
- **THEN** o authorize com esse cookie é tratado como sem sessão e redireciona para `/entrar`, sem emitir código

#### Scenario: A troca invalida o link de redefinição pendente
- **WHEN** um usuário com um link de redefinição pendente troca a própria senha
- **THEN** o link recebe a resposta de link inválido na consulta e na conclusão

### Requirement: Toda troca do carimbo de segurança revoga as sessões da pessoa
Toda operação que troca o `security_stamp` de um usuário (trocar a senha, desativar, redefinir as 2 etapas, concluir a redefinição de senha por link) SHALL revogar, na mesma transação, todas as autorizações do usuário no emissor. Se a transação falhar, nem o carimbo nem as autorizações SHALL mudar.

#### Scenario: Sessão morta pelo carimbo não aparece viva
- **WHEN** qualquer uma das quatro operações troca o carimbo de um usuário com sessões vivas
- **THEN** a lista de sessões vivas dele fica vazia, sem esperar a limpeza

### Requirement: Lista das sessões vivas, com a sessão atual marcada
`GET /account/sessions` SHALL responder `200` com as sessões vivas do usuário que chama, cada uma com `id`, `criadaEm`, `ultimoUso` e `atual`, ordenadas por `criadaEm` decrescente com desempate pelo `id`.

Uma sessão viva SHALL ser uma autorização ad hoc do emissor, com `subject` igual ao id do usuário e estado `valid`, que tenha um refresh token `valid` e não vencido. `criadaEm` SHALL ser a criação da autorização; `ultimoUso`, a maior data de criação entre os tokens dela. `atual` SHALL ser verdadeiro só na autorização cujo id é o `oi_au_id` do access token da requisição.

O principal do `apps/api` SHALL guardar o `oi_au_id` do access token do emissor, ao lado do `sub`, sem mudar a regra que tira o subject da tabela de clientes.

#### Scenario: Duas sessões, a atual marcada
- **WHEN** um usuário entra por dois logins e chama a lista com o access token do segundo
- **THEN** a lista tem as duas sessões, e só a do segundo login tem `atual` verdadeiro

#### Scenario: O último uso acompanha a renovação
- **WHEN** uma sessão renova
- **THEN** o `ultimoUso` dela passa a ser o instante da renovação, e o `criadaEm` não muda

#### Scenario: Login abandonado não é sessão
- **WHEN** um usuário obtém um código de autorização e não o troca
- **THEN** a autorização desse authorize não aparece na lista

#### Scenario: Sessão parada além do refresh não é sessão
- **WHEN** o refresh mais novo de uma sessão vence (14 dias sem uso)
- **THEN** a sessão não aparece na lista, mesmo antes da limpeza

### Requirement: Encerrar uma sessão própria
`POST /account/sessions/{id}/revoke` SHALL revogar a autorização indicada quando ela for uma sessão viva do usuário que chama, respondendo `204`. Autorização inexistente, de outra pessoa ou que não seja sessão viva SHALL responder `404`, sem revogar nada. Encerrar a própria sessão atual SHALL ser permitido.

#### Scenario: Encerrar uma sessão
- **WHEN** um usuário com duas sessões encerra a que não é a atual
- **THEN** a resposta é `204`, ela some da lista, a renovação dela recebe `invalid_grant`, e a atual continua renovando

#### Scenario: Sessão de outra pessoa
- **WHEN** um usuário pede para encerrar o id de uma sessão de outro usuário
- **THEN** a resposta é `404`, e a sessão do outro continua viva

#### Scenario: Encerrar a própria sessão atual
- **WHEN** um usuário encerra a sessão do próprio access token
- **THEN** a resposta é `204`, e a renovação seguinte dela recebe `invalid_grant`

### Requirement: Encerrar todas as outras sessões próprias
`POST /account/sessions/revoke-others` SHALL revogar, num comando só, todas as autorizações `valid` do usuário que chama, menos a do `oi_au_id` do access token, respondendo `200 { encerradas }` com quantas foram revogadas. Sem `oi_au_id` no token, SHALL responder `400`, sem revogar nada.

#### Scenario: Só a atual fica
- **WHEN** um usuário com três sessões encerra as outras pela terceira
- **THEN** a resposta é `200` com `encerradas` igual a 2, a lista mostra só a terceira, e ela continua renovando

#### Scenario: As sessões de outra pessoa não são tocadas
- **WHEN** um usuário encerra as outras sessões dele, e outro usuário tem uma sessão viva
- **THEN** a sessão do outro continua viva

