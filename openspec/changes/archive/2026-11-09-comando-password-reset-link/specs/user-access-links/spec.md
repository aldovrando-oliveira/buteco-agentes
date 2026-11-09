## MODIFIED Requirements

### Requirement: Gerar o link de redefinição de senha
`POST /users/{id}/password-reset` com `{ senha }` SHALL gerar o link de redefinição para um usuário ativo e responder `200 { link, expiraEm }`. Gerar o link SHALL NOT mudar a senha, o carimbo de segurança nem as sessões do alvo: a senha atual continua valendo até a nova ser criada. O próprio usuário que chama SHALL receber `403` "Use Minha conta para trocar a própria senha.", antes de conferir a senha. O convidado e o desativado SHALL receber `409`, e id inexistente, `404`.

A escrita do link (token, hash, propósito e vencimento, numa única instrução condicionada ao estado ativo) SHALL ser a mesma implementação usada pelo comando `password-reset-link`, para as duas entradas não divergirem. A base do painel SHALL ser resolvida **antes** da escrita, nas duas entradas. Na rota, a ordem muda sem efeito visível, porque o boot já reprova sem `PublicUrl:BaseUrl` válida; no comando, é o que impede o link anterior de morrer sem um novo.

#### Scenario: A senha antiga continua valendo
- **WHEN** um link de redefinição é gerado para um usuário
- **THEN** a resposta é `200` com validade de 24 horas, o login com a senha atual dele continua aceito (`200` com `{ "segundoPasso": "totp" }`), e as sessões dele continuam renovando

#### Scenario: Para si mesmo
- **WHEN** um usuário pede o link de redefinição da própria senha
- **THEN** a resposta é `403`, e nenhum link é gerado

#### Scenario: Para convidado ou desativado
- **WHEN** o link de redefinição é pedido para um convidado ou para um desativado
- **THEN** a resposta é `409`

#### Scenario: Mesmo efeito da rota e do comando
- **WHEN** a mesma situação inicial (um link de redefinição anterior pendente, uma sessão viva, a verificação ligada) recebe um link novo uma vez pela rota e uma vez pelo comando
- **THEN** nos dois casos o link anterior recebe a resposta de link inválido, o novo é aceito, vence 24 horas depois de gerado, o carimbo de segurança e o hash da senha não mudam, o login com a senha atual é aceito, e a sessão continua viva

#### Scenario: Dois links em sequência, pela rota e pelo comando
- **WHEN** um link é gerado pela rota e, em seguida, outro pelo comando para o mesmo usuário
- **THEN** só o segundo é aceito; o primeiro recebe a resposta de link inválido

## ADDED Requirements

### Requirement: Comando de emergência para gerar o link de redefinição de senha
O binário do `apps/api` SHALL aceitar o comando `password-reset-link --email <e-mail>`, lido dos argumentos do processo antes de montar o host web. Com ele, o processo SHALL subir sem servidor HTTP, sem rotas e sem serviços em segundo plano, gerar o link e terminar. Em produção, roda por `docker compose -f docker-compose.prod.yml --env-file .env.prod run --rm --no-deps api password-reset-link --email <e-mail>`.

O comando SHALL achar o usuário pelo e-mail normalizado e SHALL atender só o usuário ativo com papel Admin. O efeito SHALL ser o da rota `POST /users/{id}/password-reset`, pela mesma implementação: o link anterior deixa de valer, e a senha, o carimbo de segurança e as sessões não mudam.

A base do painel SHALL vir da configuração, pela mesma regra da rota (`Identity:PanelBaseUrl` quando configurada, senão `PublicUrl:BaseUrl`), e SHALL ser resolvida antes de abrir qualquer conexão com o banco.

**Saída:**

| caso | código de saída | efeito |
|---|---|---|
| link gerado | `0` | o link novo; o anterior deixa de valer |
| nenhum usuário com o e-mail | `1` | nenhum |
| usuário desativado | `2` | nenhum |
| usuário convidado | `3` | nenhum; o link do convite continua valendo |
| usuário ativo que não é Admin | `4` | nenhum |
| `--email` ausente, vazio, ou argumento desconhecido | `64` | nenhum |
| falha ao falar com o banco ou erro inesperado | `70` | nenhum |
| base do painel ausente ou inválida | `78` | nenhum, sem conexão com o banco |

O código `1` SHALL ser só o de "nenhum usuário com o e-mail", decidido por uma consulta que respondeu. Uma exceção não tratada SHALL NOT sair com nenhum dos códigos de resultado (`0` a `4`): SHALL sair com `70`. O comando SHALL NOT aplicar migrations, nem perguntar nada.

**O link e o registro:** na saída `0`, o link SHALL sair uma única vez, sozinho numa linha da saída padrão (stdout). Cada execução SHALL escrever uma linha `Warning` com `EventId` próprio na saída de erro (stderr), com o resultado, o e-mail pedido e, quando houver, o id do usuário. A saída de erro SHALL NOT conter o token, o link, o hash do token nem o carimbo de segurança. Na saída `70`, a linha SHALL trazer o tipo do erro, e SHALL NOT trazer a string de conexão nem a senha do banco. Na saída `78`, a linha SHALL citar os nomes das chaves, e SHALL NOT trazer valor de configuração.

O comando SHALL NOT ter rota HTTP equivalente, nem ser alcançável pelo processo web em execução.

#### Scenario: Link gerado pelo comando
- **WHEN** o comando roda com o e-mail de um Admin ativo que tem um link de redefinição anterior e uma sessão viva
- **THEN** o processo sai com `0`, o stdout tem uma única linha `<base do painel>/redefinir-senha#<token>` com o token de 43 caracteres, esse link é aceito em `POST /auth/password-reset`, o anterior recebe a resposta de link inválido, o login com a senha atual continua aceito, e a sessão continua viva

#### Scenario: O stderr não leva o link
- **WHEN** o comando gera um link
- **THEN** a saída de erro capturada tem a linha `Warning` com o `EventId` e o resultado `gerado`, e não contém o token, o link nem o hash do token

#### Scenario: E-mail com maiúsculas e espaços
- **WHEN** o comando roda com `--email "  Joana.183@Empresa.com "` e existe a usuária `joana.183@empresa.com`
- **THEN** o comando a encontra pelo e-mail normalizado e sai com `0` se ela for Admin ativa, ou com o código do estado ou do papel dela, e nunca com `1`

#### Scenario: E-mail sem usuário
- **WHEN** o comando roda com um e-mail que nenhum usuário tem
- **THEN** o processo sai com `1`, e nada muda no banco

#### Scenario: Usuário desativado
- **WHEN** o comando roda com o e-mail de um usuário desativado
- **THEN** o processo sai com `2`, e nenhum link é gravado

#### Scenario: Convidado
- **WHEN** o comando roda com o e-mail de um convidado com o link do convite pendente
- **THEN** o processo sai com `3`, e o link do convite continua sendo aceito

#### Scenario: Usuário que não é Admin
- **WHEN** o comando roda com o e-mail de um Operador ou de um Leitor ativo
- **THEN** o processo sai com `4`, e nenhum link é gravado

#### Scenario: Uso errado
- **WHEN** o comando roda sem `--email`, com o e-mail vazio, ou com um argumento desconhecido
- **THEN** o processo sai com `64`, e nada muda no banco

#### Scenario: Banco fora do ar
- **WHEN** o comando roda com a conexão do banco apontando para uma porta fechada
- **THEN** o processo sai com `70`, a saída não diz que o usuário não existe, e não contém a string de conexão nem a senha do banco

#### Scenario: Sem a base do painel
- **WHEN** o comando roda com `PublicUrl:BaseUrl` vazia e sem `Identity:PanelBaseUrl`, para um Admin com um link de redefinição anterior
- **THEN** o processo sai com `78`, e a cópia do link anterior continua sendo aceita

#### Scenario: Não aplica migrations
- **WHEN** o comando roda com uma migration pendente no banco
- **THEN** a migration continua pendente
