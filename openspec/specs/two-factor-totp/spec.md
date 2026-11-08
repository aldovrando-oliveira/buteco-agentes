# two-factor-totp Specification

## Purpose
Diz como funciona a verificação em 2 etapas do usuário local no `apps/api`: o código TOTP
pelo RFC 6238 e a recusa de reuso, o segredo cifrado com chave própria conferida no boot,
o cadastro em dois tempos e o fechamento dele até a tela existir, os códigos de
recuperação, a remoção pela própria pessoa e a redefinição pelo admin. Introduzida pela
change `verificacao-2-etapas-totp` (#218). O segundo passo do login está em
`issuer-user-login`, e o bloqueio das falhas de código em `local-users`. As telas são da
#183, e a obrigatoriedade, da #220.
## Requirements
### Requirement: Código TOTP pelo RFC 6238
O `apps/api` SHALL calcular o código de verificação em 2 etapas pelo RFC 6238, com:

- HMAC-SHA1;
- 6 dígitos, com zeros à esquerda;
- passo de 30 segundos, contado da época Unix (T0 = 0);
- segredo de 20 bytes aleatórios por usuário.

O relógio SHALL vir do `TimeProvider` do processo. Espaços no código digitado SHALL ser ignorados.

#### Scenario: Vetores do Apêndice B do RFC 6238
- **WHEN** o código é calculado com o segredo ASCII `12345678901234567890` nos instantes Unix 59, 1111111109, 1111111111, 1234567890, 2000000000 e 20000000000
- **THEN** os códigos são `287082`, `081804`, `050471`, `005924`, `279037` e `353130`

#### Scenario: Código com espaço é aceito
- **WHEN** o código válido do passo atual é enviado como `"287 082"`
- **THEN** ele é conferido como `287082`

### Requirement: Janela de um passo e recusa do código já usado
Um código SHALL ser aceito quando bater com o passo atual, com o anterior ou com o seguinte, e SHALL ser recusado fora deles. O `apps/api` SHALL gravar o último passo aceito de cada usuário, e SHALL recusar todo código cujo passo não seja maior que ele, inclusive dentro da janela. Aceitar o código e gravar o passo SHALL ser uma única operação atômica: duas submissões simultâneas do mesmo código SHALL resultar em no máximo uma aceita. A confirmação do cadastro SHALL gravar o passo também.

#### Scenario: Passos vizinhos são aceitos
- **WHEN** o código do passo T−1, o do passo T ou o do passo T+1 é enviado no passo T, cada um para um usuário diferente
- **THEN** os três são aceitos

#### Scenario: Dois passos de distância são recusados
- **WHEN** o código do passo T−2 ou o do passo T+2 é enviado no passo T
- **THEN** ele é recusado

#### Scenario: O mesmo código não serve duas vezes
- **WHEN** um código aceito é enviado de novo dentro da janela
- **THEN** a segunda submissão é recusada

#### Scenario: Passo anterior depois de um posterior aceito
- **WHEN** o código do passo T foi aceito, e em seguida chega o código do passo T−1, ainda dentro da janela
- **THEN** ele é recusado

#### Scenario: Submissões simultâneas do mesmo código
- **WHEN** o mesmo código válido é enviado duas vezes ao mesmo tempo
- **THEN** uma submissão é aceita e a outra é recusada

#### Scenario: O código da confirmação não serve para entrar
- **WHEN** a pessoa confirma o cadastro com um código e, no mesmo passo, tenta entrar com ele
- **THEN** o segundo passo do login recusa o código

### Requirement: Segredo cifrado em repouso com chave própria, conferida no boot
O segredo do TOTP, inclusive o de um cadastro em curso, SHALL ser gravado cifrado com AES-256-GCM, com a chave de `Identity:TwoFactorEncryptionKey` (32 bytes em base64), vinda da configuração ou de variável de ambiente e nunca do banco, e com o id do usuário como dado associado. O boot SHALL falhar, com mensagem que nomeia a configuração, quando a chave estiver ausente, fora de base64, com tamanho diferente de 32 bytes, ou igual a `Identity:SigningKeyEncryptionKey` ou a `Mcp:CredentialEncryptionKey`. A checagem SHALL valer mesmo com o cadastro fechado, e SHALL NOT ler o banco.

#### Scenario: Segredo gravado não é legível
- **WHEN** um usuário confirma o cadastro
- **THEN** a coluna do segredo não contém os 20 bytes do segredo nem a forma em base32 dele

#### Scenario: Segredo copiado para outro usuário não decifra
- **WHEN** o segredo cifrado de um usuário é posto na linha de outro
- **THEN** a decifração na linha do outro falha

#### Scenario: Boot sem a chave
- **WHEN** o `apps/api` sobe sem `Identity:TwoFactorEncryptionKey`
- **THEN** o boot falha com uma mensagem que cita `Identity:TwoFactorEncryptionKey`

#### Scenario: Boot com a chave igual a outra
- **WHEN** o `apps/api` sobe com `Identity:TwoFactorEncryptionKey` igual a `Identity:SigningKeyEncryptionKey`
- **THEN** o boot falha com uma mensagem que diz que as chaves precisam ser distintas

### Requirement: Segredo que não decifra não bloqueia os códigos de recuperação
Quando o segredo gravado não decifrar com a chave configurada, o código do app autenticador SHALL ser respondido com `500` e a mensagem "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador.", SHALL NOT contar como falha no bloqueio, e SHALL gerar um log de erro com identificador de evento próprio e sem o segredo. Os códigos de recuperação SHALL continuar valendo.

#### Scenario: Chave trocada e código do app
- **WHEN** o segredo de um usuário foi cifrado com outra chave, e ele envia um código do app no segundo passo do login
- **THEN** a resposta é `500` com a mensagem acima, a contagem de falhas do e-mail não muda, e o log de erro é registrado

#### Scenario: Chave trocada e código de recuperação
- **WHEN** o mesmo usuário envia um código de recuperação válido
- **THEN** o login se completa com `204`

### Requirement: As rotas de 2 etapas atendem só o usuário real do token
As rotas sob `/account/two-factor` e a rota `POST /users/{id}/two-factor/reset` SHALL identificar quem chama pelo `sub` do access token do emissor, e SHALL exigir que ele seja um usuário existente e ativo. Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário) e usuário desativado com access token ainda válido SHALL receber `403`, sem efeito nenhum.

#### Scenario: Operador sem usuário em Minha conta
- **WHEN** `GET /account/two-factor` chega com um access token do `buteco-painel` cujo `sub` não é id de usuário
- **THEN** a resposta é `403`

#### Scenario: Operador sem usuário na redefinição
- **WHEN** `POST /users/{id}/two-factor/reset` chega com um access token do `buteco-painel` cujo `sub` não é id de usuário, para um usuário com a verificação ligada
- **THEN** a resposta é `403`, e o usuário continua com a verificação, os códigos e o mesmo `security_stamp`

#### Scenario: Usuário desativado com access token ainda válido
- **WHEN** um usuário é desativado e, dentro da validade do access token dele, chama `GET /account/two-factor` ou `POST /users/{id}/two-factor/reset`
- **THEN** as duas respostas são `403`

### Requirement: Situação da verificação da própria pessoa
`GET /account/two-factor` SHALL responder com `ativa`, `ativadaEm` e `codigosRestantes` (os códigos de recuperação ainda não usados). O campo `cadastroDisponivel` SHALL NOT fazer parte da resposta.

#### Scenario: Sem a verificação
- **WHEN** quem não tem a verificação chama `GET /account/two-factor`, com um access token ainda válido de antes da obrigatoriedade
- **THEN** a resposta traz `ativa: false`, `ativadaEm: null` e `codigosRestantes: 0`, e não traz `cadastroDisponivel`

#### Scenario: Depois de usar um código de recuperação
- **WHEN** quem ligou a verificação e entrou uma vez com um código de recuperação chama `GET /account/two-factor`
- **THEN** a resposta traz `ativa: true` e `codigosRestantes: 9`

### Requirement: Códigos de recuperação de uso único, guardados só em hash
Os códigos de recuperação SHALL ser 10 por usuário, com 16 caracteres do alfabeto de Crockford, mostrados em grupos de quatro separados por hífen, e SHALL ser devolvidos só na resposta que os gera. SHALL ser guardados só como SHA-256 do código normalizado. A conferência SHALL ignorar caixa, hífens e espaços, e SHALL tratar `I` e `L` como `1` e `O` como `0`. Cada código SHALL valer uma vez, marcado como usado na mesma operação atômica que o aceita. Usar um código SHALL NOT desligar a verificação.

#### Scenario: Código guardado não é legível
- **WHEN** os códigos de recuperação de um usuário são gerados
- **THEN** nenhuma coluna guarda um código em texto

#### Scenario: Código digitado de outro jeito
- **WHEN** um código válido é digitado em minúsculas, sem hífens e com a letra `O` no lugar de um `0`
- **THEN** ele é aceito

#### Scenario: Código de recuperação não vale duas vezes
- **WHEN** um código de recuperação aceito é enviado de novo
- **THEN** ele é recusado

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

