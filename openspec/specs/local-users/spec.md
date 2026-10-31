# local-users Specification

## Purpose

Diz como vivem os usuários locais do `apps/api`: identidade imutável com o e-mail como
identificador de login, senha em Argon2id, o bootstrap do primeiro Admin a partir da
configuração do operador, o bloqueio progressivo por e-mail, o limite global de
verificações, a resposta que não revela se a conta existe e o último acesso. Introduzida
pela change `usuarios-locais-login-emissor` (#180). Papéis são da #184, e as rotas de
gestão de usuários, da #219.

## Requirements

### Requirement: Usuário local com ID imutável e e-mail como identificador de login
O `apps/api` SHALL manter usuários locais com:

- identificador imutável, que é o `sub` dos tokens emitidos para eles;
- e-mail como identificador de login, único entre os usuários depois de normalizado (sem espaços nas pontas, em minúsculas);
- nome;
- estado (`active` ou `deactivated`);
- instante do último acesso.

O e-mail SHALL NOT ser a chave do usuário. Um usuário SHALL NOT ser excluído: ele é desativado.

#### Scenario: E-mail com caixa e espaços diferentes identifica o mesmo usuário
- **WHEN** existe um usuário com e-mail `Maria@Empresa.com` e alguém entra com ` maria@empresa.com `
- **THEN** o login é avaliado contra esse usuário

#### Scenario: E-mail duplicado é recusado pelo banco
- **WHEN** se tenta gravar um segundo usuário cujo e-mail normalizado é igual ao de um existente
- **THEN** a gravação falha por violação de unicidade

#### Scenario: O sub do token é o id do usuário
- **WHEN** um usuário obtém um access token pelo fluxo do painel
- **THEN** o claim `sub` do token é o identificador do usuário, e não o e-mail

### Requirement: Senha guardada com Argon2id
A senha de um usuário SHALL ser guardada só como hash Argon2id, numa string no formato PHC que carrega os parâmetros (memória de 19.456 KiB, 2 iterações, paralelismo 1), com sal aleatório de 16 bytes. Nenhuma resposta, log ou mensagem de erro SHALL conter a senha ou o hash. Um hash em formato desconhecido SHALL fazer a verificação reprovar, sem exceção.

#### Scenario: Hash gravado é Argon2id
- **WHEN** a senha de um usuário é gravada
- **THEN** a coluna de hash começa por `$argon2id$v=19$m=19456,t=2,p=1$` e não contém a senha

#### Scenario: Senha errada reprova
- **WHEN** a verificação recebe uma senha diferente da gravada
- **THEN** a verificação reprova

#### Scenario: Formato desconhecido reprova sem exceção
- **WHEN** a coluna de hash contém uma string fora dos formatos conhecidos
- **THEN** a verificação reprova e o login responde como senha errada

### Requirement: Primeiro Admin criado pelo bootstrap, sem sobrescrever nada
Depois do start, em segundo plano e sem acesso ao banco no boot, o `apps/api` SHALL criar um usuário ativo quando não houver nenhum usuário e `Auth:BootstrapAdminEmail` e `Auth:OperatorPasswordHash` estiverem presentes e válidos. O hash inicial desse usuário SHALL ser o de `Auth:OperatorPasswordHash` (PBKDF2-HMAC-SHA256, formato `{iterações}.{sal}.{hash}`), marcado como legado de bootstrap.

O primeiro login bem-sucedido desse usuário SHALL substituir o hash por Argon2id e tirar o marcador. O verificador PBKDF2 SHALL valer só para a linha com o marcador.

Quando já houver ao menos um usuário, o bootstrap SHALL NOT criar, alterar senha nem alterar e-mail de ninguém, e SHALL registrar no log que foi ignorado. Sem as configurações, SHALL registrar um aviso. Duas instâncias executando o bootstrap ao mesmo tempo SHALL resultar em um único usuário. O bootstrap SHALL rodar com tratamento de falha próprio: uma falha dele SHALL NOT impedir a carga das chaves nem a sincronização dos clientes, e ele SHALL ser tentado de novo no ciclo seguinte do aquecimento até concluir.

#### Scenario: Banco sem usuário cria o primeiro
- **WHEN** o app sobe sem nenhum usuário, com `Auth:BootstrapAdminEmail` e `Auth:OperatorPasswordHash` válidos
- **THEN** existe um usuário ativo com aquele e-mail, cujo hash é o da configuração com o marcador de legado, e o log registra a criação sem conter o hash

#### Scenario: Primeiro login refaz o hash
- **WHEN** o usuário do bootstrap entra com a senha correta pela primeira vez
- **THEN** o hash gravado passa a ser Argon2id, o marcador sai, e o login seguinte com a mesma senha também é aceito

#### Scenario: Hash PBKDF2 sem o marcador é recusado
- **WHEN** um usuário sem o marcador de legado tem na coluna de hash uma string no formato PBKDF2 e tenta entrar com a senha correspondente
- **THEN** o login é recusado como senha errada

#### Scenario: Com usuários, a configuração é ignorada
- **WHEN** o app sobe com usuários existentes e com `Auth:BootstrapAdminEmail` e `Auth:OperatorPasswordHash` configurados com outros valores
- **THEN** nenhum usuário é criado ou alterado, e o log registra que o bootstrap foi ignorado

#### Scenario: Sem configuração, só aviso
- **WHEN** o app sobe sem nenhum usuário e sem `Auth:BootstrapAdminEmail`
- **THEN** nenhum usuário é criado, o app atende normalmente, e o log contém um aviso que cita `Auth:BootstrapAdminEmail`

#### Scenario: Duas instâncias criam um usuário só
- **WHEN** dois bootstraps rodam ao mesmo tempo contra um banco sem usuário
- **THEN** a tabela termina com exatamente um usuário

#### Scenario: Falha do bootstrap é tentada de novo no ciclo seguinte
- **WHEN** o bootstrap falha no primeiro ciclo do aquecimento, com um banco sem usuário e as configurações válidas
- **THEN** o log registra a falha com o evento do bootstrap, o anel de chaves carrega e o catálogo de clientes sincroniza no mesmo ciclo, e depois do ciclo seguinte existe o usuário do bootstrap

### Requirement: Bloqueio progressivo por e-mail, exista ou não a conta
O login SHALL contar as falhas por e-mail normalizado, exista ou não um usuário com aquele e-mail, por estas regras:

- **primeiro bloqueio:** depois de 5 falhas seguidas, o e-mail SHALL ficar bloqueado por 5 minutos;
- **reincidência:** depois de um bloqueio vencido, uma única falha SHALL bloquear de novo, pelo dobro da duração do bloqueio anterior, até o teto de 60 minutos;
- **volta ao início:** quando a última falha tiver mais de 24 horas, a contagem e a duração do último bloqueio SHALL valer como zeradas, e o próximo bloqueio volta a exigir 5 falhas e a durar 5 minutos. A regra SHALL depender só do tempo, e não de a linha já ter sido removida pela limpeza;
- **sucesso:** um login bem-sucedido SHALL zerar a contagem e a duração do último bloqueio;
- **desativado com a senha certa:** a resposta `403` SHALL NOT contar como falha e SHALL NOT zerar a contagem. Com a senha errada, conta como falha.

Durante o bloqueio, o login daquele e-mail SHALL responder `423 Locked` com `Retry-After`, sem verificar a senha. A tentativa durante o bloqueio SHALL NOT contar como falha nem estender o bloqueio. As linhas de contagem sem falha há mais de 24 horas e fora de bloqueio SHALL ser removidas pelo ciclo periódico de limpeza do emissor.

#### Scenario: Cinco falhas bloqueiam por cinco minutos
- **WHEN** um e-mail recebe 5 tentativas com senha errada seguidas
- **THEN** a quinta responde `401`, e a sexta, mesmo com a senha certa, responde `423` com `Retry-After` de até 300 segundos

#### Scenario: E-mail sem conta bloqueia igual
- **WHEN** um e-mail que não pertence a nenhum usuário recebe 5 tentativas seguidas
- **THEN** a sexta tentativa responde `423`, como para uma conta existente

#### Scenario: Depois de um bloqueio vencido, uma falha bloqueia pelo dobro
- **WHEN** um e-mail foi bloqueado por 5 minutos, o bloqueio venceu, e a próxima tentativa erra a senha
- **THEN** essa tentativa responde `401`, e a seguinte, mesmo com a senha certa, responde `423` com `Retry-After` de até 600 segundos

#### Scenario: Reincidência dobra até o teto
- **WHEN** depois de cada bloqueio vencido a mesma conta erra a senha uma vez, repetidamente
- **THEN** os bloqueios seguintes ao primeiro duram 10, 20, 40 e 60 minutos, e não passam de 60

#### Scenario: Vinte e quatro horas sem falha voltam ao início
- **WHEN** um e-mail teve um bloqueio de 10 minutos, passam mais de 24 horas sem falha, a linha ainda não foi removida pela limpeza, e o e-mail erra a senha 4 vezes
- **THEN** nenhuma das 4 tentativas, nem a seguinte, responde `423`; a 5ª falha bloqueia por 5 minutos, e não por 20

#### Scenario: Sucesso zera a contagem
- **WHEN** um e-mail com 4 falhas seguidas entra com a senha certa e, depois, erra 4 vezes
- **THEN** nenhuma dessas tentativas responde `423`

#### Scenario: Desativado com a senha certa não mexe na contagem
- **WHEN** o e-mail de um usuário desativado tem 4 falhas seguidas, recebe duas tentativas com a senha certa e, depois, uma com a senha errada
- **THEN** as duas com a senha certa respondem `403`, e não `423`; a com a senha errada é a 5ª falha e bloqueia, e a tentativa seguinte responde `423`

#### Scenario: Tentativa durante o bloqueio não estende o bloqueio
- **WHEN** um e-mail bloqueado por 5 minutos recebe tentativas com a senha errada durante o bloqueio
- **THEN** todas respondem `423`, e o `Retry-After` de cada uma não passa do fim do bloqueio original

#### Scenario: Contagem parada é limpa
- **WHEN** uma linha de contagem não tem falha há mais de 24 horas, não está bloqueada, e o ciclo de limpeza roda
- **THEN** a linha deixa de existir

### Requirement: Limite global de verificações de senha
A rota de login SHALL limitar as verificações de senha simultâneas e por segundo, para todo o processo e independentemente de IP. Acima do limite, a resposta SHALL ser `429 Too Many Requests` com `Retry-After`, sem verificar a senha.

#### Scenario: Rajada acima do limite recebe 429
- **WHEN** chegam mais pedidos de login ao mesmo tempo do que o limite de concorrência e a fila comportam
- **THEN** os excedentes recebem `429` com `Retry-After`, e nenhum deles verifica senha

### Requirement: Resposta de login não revela se a conta existe
Um e-mail sem conta e uma senha errada SHALL receber a mesma resposta (`401`, com a mesma mensagem genérica), e o e-mail sem conta SHALL passar por uma verificação de hash com os mesmos parâmetros. Um usuário desativado SHALL receber `403` com a mensagem "Acesso desativado. Fale com o administrador." só quando a senha estiver correta. Com a senha errada, SHALL receber a mesma resposta genérica.

#### Scenario: Conta inexistente e senha errada respondem igual
- **WHEN** uma tentativa usa um e-mail sem conta e outra usa um e-mail existente com senha errada
- **THEN** as duas respostas têm o mesmo status e o mesmo corpo

#### Scenario: Desativado com a senha certa
- **WHEN** um usuário desativado entra com a senha correta
- **THEN** a resposta é `403` com "Acesso desativado. Fale com o administrador.", e nenhum cookie de sessão é gravado

#### Scenario: Desativado com a senha errada
- **WHEN** um usuário desativado entra com a senha errada
- **THEN** a resposta é igual à de senha errada de um usuário ativo

### Requirement: Último acesso registrado no login e na renovação
O instante de último acesso do usuário SHALL ser atualizado em cada login bem-sucedido e em cada renovação aceita pelo grant `refresh_token`. Ele SHALL NOT ser atualizado por requisições às rotas de API autenticadas com o access token.

#### Scenario: Login atualiza o último acesso
- **WHEN** um usuário entra com sucesso
- **THEN** o último acesso dele passa a ser o instante do login

#### Scenario: Renovação atualiza o último acesso
- **WHEN** o refresh token de um usuário é trocado por um novo par
- **THEN** o último acesso dele passa a ser o instante da renovação

#### Scenario: Chamada de API não escreve
- **WHEN** um usuário chama `GET /agents` com um access token válido
- **THEN** o último acesso dele não muda
