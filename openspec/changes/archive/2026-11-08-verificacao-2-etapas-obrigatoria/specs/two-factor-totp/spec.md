## REMOVED Requirements

### Requirement: Cadastro em dois tempos, com a senha e um código válido
**Reason**: o cadastro passa ao login, pelo cookie pendente, e é o único caminho (decisão do dono, Q5). `POST /account/two-factor/setup` e `POST /account/two-factor/confirm` saem do mapeamento.
**Migration**: o cadastro é o de `issuer-user-login`, "Cadastro da verificação no login, pelo cookie pendente" (`POST /auth/session/enrollment/setup` e `/confirm`). O formato do segredo, o URI, o prazo de 10 minutos e a regra de não trocar o `security_stamp` vão com ele.

### Requirement: Cadastro fechado até a tela existir
**Reason**: a opção `Identity:TwoFactor:EnrollmentEnabled` existia só para a transição entre a #218 e a tela (#218 D1). Com a obrigatoriedade, o cadastro é aberto a todos, e a opção sai junto com o `403` "O cadastro … ainda não está disponível.".
**Migration**: nenhuma. A variável `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED` deixa de ser lida; se ficar no `.env.prod`, não tem efeito.

## RENAMED Requirements

- FROM: `### Requirement: Regeneração dos códigos e remoção da verificação, com um código válido`
- TO: `### Requirement: Regeneração dos códigos de recuperação, com um código válido`

## MODIFIED Requirements

### Requirement: Situação da verificação da própria pessoa
`GET /account/two-factor` SHALL responder com `ativa`, `ativadaEm` e `codigosRestantes` (os códigos de recuperação ainda não usados). O campo `cadastroDisponivel` SHALL NOT fazer parte da resposta.

#### Scenario: Sem a verificação
- **WHEN** quem não tem a verificação chama `GET /account/two-factor`, com um access token ainda válido de antes da obrigatoriedade
- **THEN** a resposta traz `ativa: false`, `ativadaEm: null` e `codigosRestantes: 0`, e não traz `cadastroDisponivel`

#### Scenario: Depois de usar um código de recuperação
- **WHEN** quem ligou a verificação e entrou uma vez com um código de recuperação chama `GET /account/two-factor`
- **THEN** a resposta traz `ativa: true` e `codigosRestantes: 9`

### Requirement: Regeneração dos códigos de recuperação, com um código válido
`POST /account/two-factor/recovery-codes`, com `{ "codigo" }`, SHALL apagar os códigos de recuperação da pessoa e responder `200` com 10 novos. O código SHALL ser um código do app ou de recuperação válido; código errado SHALL responder `401` e contar como falha no bloqueio por e-mail; sem a verificação ligada, SHALL responder `409`.

A própria pessoa SHALL NOT poder remover a verificação: `POST /account/two-factor/remove` SHALL NOT ser mapeada. Quem troca de celular pede a redefinição a outro usuário ("Redefinição da verificação pelo admin") ou usa o comando de emergência.

#### Scenario: Regeneração invalida os códigos antigos
- **WHEN** a pessoa regenera os códigos com um código do app válido
- **THEN** a resposta traz 10 códigos novos, e um código antigo, ainda não usado, passa a ser recusado

#### Scenario: Regeneração com código errado
- **WHEN** a pessoa tenta regenerar os códigos com um código errado
- **THEN** a resposta é `401`, os códigos antigos continuam valendo, e a contagem de falhas do e-mail aumenta

#### Scenario: Não há rota de remover
- **WHEN** `POST /account/two-factor/remove` chega com um access token válido e um código válido
- **THEN** a verificação continua ligada, e a resposta não é `204`

#### Scenario: Não há cadastro por access token
- **WHEN** `POST /account/two-factor/setup` ou `POST /account/two-factor/confirm` chega com um access token válido
- **THEN** nenhum segredo é gerado, e a verificação não muda

### Requirement: Redefinição da verificação pelo admin
`POST /users/{id}/two-factor/reset` SHALL apagar o segredo, o cadastro pendente, a data de ativação, o último passo e os códigos de recuperação do usuário, trocar o `security_stamp` dele e revogar todas as autorizações dele no emissor, numa mesma transação, respondendo `204`. Usuário inexistente SHALL responder `404`; usuário sem a verificação ligada, `409`, sem trocar o stamp e sem revogar nada; e o próprio usuário que chama, `403` com "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor.".

A operação SHALL ser a mesma do comando de emergência ("Comando de emergência para redefinir a verificação"): as duas entradas SHALL usar uma implementação só.

A revogação junto da troca do stamp é a regra de `user-account` ("Toda troca do carimbo de segurança revoga as sessões da pessoa"): sem ela, as sessões mortas pelo stamp continuariam na lista de sessões vivas por até 14 dias.

Depois da redefinição, o login seguinte da pessoa SHALL pedir o cadastro (`issuer-user-login`).

#### Scenario: Redefinir outro usuário
- **WHEN** um usuário redefine a verificação de outro que a tem ligada
- **THEN** a resposta é `204`, o login seguinte do outro responde `200` com `{ "segundoPasso": "cadastro" }`, e a próxima renovação de uma sessão do outro, aberta antes, recebe `invalid_grant`

#### Scenario: A redefinição encerra as sessões na lista
- **WHEN** um usuário com duas sessões vivas tem a verificação redefinida por outro
- **THEN** o detalhe dele em `GET /users/{id}` mostra zero sessões ativas, e as duas autorizações estão revogadas

#### Scenario: Redefinir a si mesmo
- **WHEN** um usuário chama a redefinição com o próprio id
- **THEN** a resposta é `403` com "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor.", e a verificação dele continua ligada

#### Scenario: Redefinir quem não tem a verificação
- **WHEN** a redefinição é pedida para um usuário sem a verificação ligada
- **THEN** a resposta é `409`, o `security_stamp` dele não muda, e as sessões dele continuam vivas

#### Scenario: Redefinir usuário inexistente
- **WHEN** a redefinição é pedida para um id que não existe
- **THEN** a resposta é `404`

## ADDED Requirements

### Requirement: Comando de emergência para redefinir a verificação
O binário do `apps/api` SHALL aceitar o comando `two-factor-reset --email <e-mail>`, lido dos argumentos do processo antes de montar o host web. Com ele, o processo SHALL subir sem servidor HTTP, sem rotas e sem serviços em segundo plano, executar a redefinição e terminar. Em produção, roda por `docker compose -f docker-compose.prod.yml --env-file .env.prod run --rm --no-deps api two-factor-reset --email <e-mail>`.

O comando SHALL achar o usuário pelo e-mail normalizado e SHALL ter efeito idêntico ao de `POST /users/{id}/two-factor/reset`: apagar o segredo, o cadastro pendente, a data de ativação, o último passo e os códigos de recuperação, trocar o `security_stamp` e revogar as autorizações, numa transação, pela mesma implementação.

**Saída:**

| caso | código de saída | efeito |
|---|---|---|
| redefinida | `0` | a redefinição |
| nenhum usuário com o e-mail | `1` | nenhum |
| usuário desativado | `2` | nenhum |
| usuário sem a verificação ligada (inclusive convidado) | `3` | nenhum |
| `--email` ausente ou argumento desconhecido | `64` | nenhum |
| falha ao falar com o banco ou erro inesperado | `70` | nenhum (a transação não se completa) |

O código `1` SHALL ser só o de "nenhum usuário com o e-mail", decidido por uma consulta que respondeu. Uma exceção não tratada SHALL NOT sair com `1`, nem com nenhum dos códigos de resultado (`0`, `1`, `2`, `3`): SHALL sair com `70`. O comando SHALL NOT aplicar migrations.

**Registro:** cada execução SHALL escrever uma linha `Warning` com `EventId` próprio, o resultado, o id do usuário quando houver, o e-mail pedido e, na redefinida, quantas sessões foram encerradas. Nenhuma saída do comando SHALL conter segredo, código de recuperação, código TOTP, hash ou o carimbo de segurança. Na saída `70`, a linha de registro SHALL trazer o tipo do erro, e SHALL NOT trazer a string de conexão nem a senha do banco.

O comando SHALL NOT ter rota HTTP equivalente, nem ser alcançável pelo processo web em execução.

#### Scenario: Redefinição pelo comando
- **WHEN** o comando roda com o e-mail de um usuário ativo que tem a verificação ligada e duas sessões vivas
- **THEN** o processo sai com `0`, a verificação dele está apagada, o `security_stamp` mudou, as duas autorizações estão revogadas, e o login seguinte dele responde `200` com `{ "segundoPasso": "cadastro" }`

#### Scenario: Mesmo efeito da rota do admin
- **WHEN** a mesma situação inicial é redefinida uma vez pela rota do admin e uma vez pelo comando
- **THEN** o estado final do usuário (colunas das 2 etapas, códigos, carimbo trocado, autorizações revogadas) é o mesmo nos dois casos

#### Scenario: E-mail sem usuário
- **WHEN** o comando roda com um e-mail que não é de nenhum usuário
- **THEN** o processo sai com `1`, e nada muda no banco

#### Scenario: Usuário desativado
- **WHEN** o comando roda com o e-mail de um usuário desativado com a verificação ligada
- **THEN** o processo sai com `2`, e a verificação e o carimbo dele não mudam

#### Scenario: Usuário sem a verificação
- **WHEN** o comando roda com o e-mail de um usuário sem a verificação ligada
- **THEN** o processo sai com `3`, e o carimbo dele não muda

#### Scenario: Uso errado
- **WHEN** o comando roda sem `--email`
- **THEN** o processo sai com `64`, sem tocar no banco

#### Scenario: Banco fora do ar não vira "usuário inexistente"
- **WHEN** o comando roda com a conexão apontando para uma porta fechada
- **THEN** o processo sai com `70`, a saída traz o tipo do erro, não diz que o usuário não existe, e não contém a string de conexão nem a senha

#### Scenario: O comando não aplica migrations
- **WHEN** o comando roda contra um banco com uma migration pendente
- **THEN** a tabela de histórico de migrations não muda

#### Scenario: O registro não traz segredo
- **WHEN** o comando redefine um usuário
- **THEN** a saída tem uma linha `Warning` com o `EventId` do comando, o id do usuário e o número de sessões encerradas, e não contém o segredo, nenhum código de recuperação nem o carimbo, antigo ou novo

#### Scenario: Não é rota HTTP
- **WHEN** o `apps/api` sobe como servidor web
- **THEN** nenhuma rota mapeada executa a redefinição sem um usuário autenticado, e as únicas rotas anônimas novas são as duas de cadastro no login
