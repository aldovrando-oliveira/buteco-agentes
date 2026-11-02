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
As rotas sob `/account/two-factor` e a rota `POST /users/{id}/two-factor/reset` SHALL identificar quem chama pelo `sub` do access token do emissor, e SHALL exigir que ele seja um usuário existente e ativo. Token HMAC (sem `sub`), `sub` sem usuário e usuário desativado com access token ainda válido SHALL receber `403`, sem efeito nenhum.

#### Scenario: HMAC de operador em Minha conta
- **WHEN** `GET /account/two-factor` chega com um token HMAC `operator` válido
- **THEN** a resposta é `403`

#### Scenario: HMAC de operador na redefinição
- **WHEN** `POST /users/{id}/two-factor/reset` chega com um token HMAC `operator` válido, para um usuário com a verificação ligada
- **THEN** a resposta é `403`, e o usuário continua com a verificação, os códigos e o mesmo `security_stamp`

#### Scenario: Usuário desativado com access token ainda válido
- **WHEN** um usuário é desativado e, dentro da validade do access token dele, chama `GET /account/two-factor` ou `POST /users/{id}/two-factor/reset`
- **THEN** as duas respostas são `403`

### Requirement: Cadastro em dois tempos, com a senha e um código válido
`POST /account/two-factor/setup`, com `{ "senha" }`, SHALL conferir a senha da pessoa e gerar um segredo novo, gravado como cadastro pendente por 10 minutos, e SHALL responder `200` com o segredo em base32 sem padding e o URI `otpauth://totp/Buteco%20Agentes:{e-mail}?secret={segredo}&issuer=Buteco%20Agentes&algorithm=SHA1&digits=6&period=30`, com o e-mail codificado. É a única resposta que SHALL conter o segredo. Senha errada SHALL responder `401` e contar como falha no bloqueio por e-mail. Com a verificação já ligada, SHALL responder `409`. Um cadastro pendente novo SHALL substituir o anterior.

`POST /account/two-factor/confirm`, com `{ "codigo" }`, SHALL ligar a verificação só com um código válido do segredo pendente, e SHALL responder `200` com os 10 códigos de recuperação. Código errado SHALL responder `401` e contar como falha; sem cadastro pendente, ou com ele vencido, SHALL responder `400`.

Ligar a verificação SHALL NOT trocar o `security_stamp`.

#### Scenario: Cadastro sem a senha certa
- **WHEN** `POST /account/two-factor/setup` chega com a senha errada
- **THEN** a resposta é `401`, nenhum segredo é gerado, e a contagem de falhas do e-mail da pessoa aumenta

#### Scenario: Cadastro devolve o segredo e o URI
- **WHEN** `POST /account/two-factor/setup` chega com a senha certa
- **THEN** a resposta é `200` com o segredo de 32 caracteres em base32 e o URI `otpauth://` com o issuer `Buteco Agentes`, o e-mail da pessoa e os parâmetros `SHA1`, `6` e `30`

#### Scenario: Confirmação com código errado não liga
- **WHEN** `POST /account/two-factor/confirm` chega com um código que não é do segredo pendente
- **THEN** a resposta é `401`, e a verificação continua desligada

#### Scenario: Confirmação com código certo liga e devolve os códigos
- **WHEN** `POST /account/two-factor/confirm` chega com um código válido do segredo pendente
- **THEN** a resposta é `200` com 10 códigos de recuperação distintos, a verificação fica ligada, e o login seguinte da pessoa pede o segundo passo

#### Scenario: Cadastro pendente vencido
- **WHEN** a confirmação chega mais de 10 minutos depois do `setup`
- **THEN** a resposta é `400`, e a verificação continua desligada

#### Scenario: Cadastro com a verificação já ligada
- **WHEN** `POST /account/two-factor/setup` chega de quem já tem a verificação ligada
- **THEN** a resposta é `409`, e o segredo ativo não muda

#### Scenario: O segredo não volta em nenhuma leitura
- **WHEN** a pessoa com a verificação ligada chama `GET /account/two-factor`
- **THEN** a resposta não contém o segredo nem o URI

### Requirement: Cadastro fechado até a tela existir
Com `Identity:TwoFactor:EnrollmentEnabled` falsa, que é o padrão, `POST /account/two-factor/setup` e `POST /account/two-factor/confirm` SHALL responder `403` com "O cadastro da verificação em 2 etapas ainda não está disponível.", sem gerar segredo nem ligar a verificação. A verificação no login, a remoção, a regeneração dos códigos e a redefinição pelo admin SHALL funcionar com a opção em qualquer valor.

#### Scenario: Cadastro fechado
- **WHEN** a opção está falsa e `POST /account/two-factor/setup` chega com a senha certa
- **THEN** a resposta é `403`, e nenhum cadastro pendente é gravado

#### Scenario: Verificação vale com o cadastro fechado
- **WHEN** a opção está falsa e um usuário que já tem a verificação ligada entra com a senha certa
- **THEN** o login pede o segundo passo

#### Scenario: Redefinição com o cadastro fechado
- **WHEN** a opção está falsa e um usuário redefine a verificação de outro
- **THEN** a redefinição acontece

### Requirement: Situação da verificação da própria pessoa
`GET /account/two-factor` SHALL responder com `ativa`, `ativadaEm`, `codigosRestantes` (os códigos de recuperação ainda não usados) e `cadastroDisponivel` (o valor de `Identity:TwoFactor:EnrollmentEnabled`).

#### Scenario: Sem a verificação
- **WHEN** quem não tem a verificação chama `GET /account/two-factor`, com o cadastro fechado
- **THEN** a resposta traz `ativa: false`, `ativadaEm: null`, `codigosRestantes: 0` e `cadastroDisponivel: false`

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

### Requirement: Regeneração dos códigos e remoção da verificação, com um código válido
`POST /account/two-factor/recovery-codes`, com `{ "codigo" }`, SHALL apagar os códigos de recuperação da pessoa e responder `200` com 10 novos. `POST /account/two-factor/remove`, com `{ "codigo" }`, SHALL apagar o segredo, o cadastro pendente, o último passo e os códigos, e responder `204`. Nas duas, o código SHALL ser um código do app ou de recuperação válido; código errado SHALL responder `401` e contar como falha no bloqueio por e-mail; sem a verificação ligada, SHALL responder `409`. Remover SHALL NOT trocar o `security_stamp`.

#### Scenario: Regeneração invalida os códigos antigos
- **WHEN** a pessoa regenera os códigos com um código do app válido
- **THEN** a resposta traz 10 códigos novos, e um código antigo, ainda não usado, passa a ser recusado

#### Scenario: Remoção com código
- **WHEN** a pessoa remove a verificação com um código válido
- **THEN** a resposta é `204`, e o login seguinte dela responde `204` sem o segundo passo

#### Scenario: Remoção com código errado
- **WHEN** a pessoa tenta remover a verificação com um código errado
- **THEN** a resposta é `401`, a verificação continua ligada, e a contagem de falhas do e-mail aumenta

### Requirement: Redefinição da verificação pelo admin
`POST /users/{id}/two-factor/reset` SHALL apagar o segredo, o cadastro pendente, a data de ativação, o último passo e os códigos de recuperação do usuário, trocar o `security_stamp` dele e revogar todas as autorizações dele no emissor, numa mesma transação, respondendo `204`. Usuário inexistente SHALL responder `404`; usuário sem a verificação ligada, `409`, sem trocar o stamp e sem revogar nada; e o próprio usuário que chama, `403`.

A revogação junto da troca do stamp é a regra de `user-account` ("Toda troca do carimbo de segurança revoga as sessões da pessoa"): sem ela, as sessões mortas pelo stamp continuariam na lista de sessões vivas por até 14 dias.

#### Scenario: Redefinir outro usuário
- **WHEN** um usuário redefine a verificação de outro que a tem ligada
- **THEN** a resposta é `204`, o login seguinte do outro responde `204` sem o segundo passo, e a próxima renovação de uma sessão do outro, aberta antes, recebe `invalid_grant`

#### Scenario: A redefinição encerra as sessões na lista
- **WHEN** um usuário com duas sessões vivas tem a verificação redefinida por outro
- **THEN** o detalhe dele em `GET /users/{id}` mostra zero sessões ativas, e as duas autorizações estão revogadas

#### Scenario: Redefinir a si mesmo
- **WHEN** um usuário chama a redefinição com o próprio id
- **THEN** a resposta é `403`, e a verificação dele continua ligada

#### Scenario: Redefinir quem não tem a verificação
- **WHEN** a redefinição é pedida para um usuário sem a verificação ligada
- **THEN** a resposta é `409`, o `security_stamp` dele não muda, e as sessões dele continuam vivas

#### Scenario: Redefinir usuário inexistente
- **WHEN** a redefinição é pedida para um id que não existe
- **THEN** a resposta é `404`

