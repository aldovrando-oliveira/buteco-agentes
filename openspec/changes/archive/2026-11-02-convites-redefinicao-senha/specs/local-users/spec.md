## MODIFIED Requirements

### Requirement: Usuário local com ID imutável e e-mail como identificador de login
O `apps/api` SHALL manter usuários locais com:

- identificador imutável, que é o `sub` dos tokens emitidos para eles;
- e-mail como identificador de login, único entre os usuários depois de normalizado (sem espaços nas pontas, em minúsculas), inclusive entre os convidados;
- nome;
- estado (`active`, `deactivated` ou `invited`);
- instante do último acesso.

O e-mail SHALL NOT ser a chave do usuário. Um usuário que já foi ativo SHALL NOT ser excluído: ele é desativado. O convidado que nunca aceitou o convite SHALL ser excluído quando o convite é cancelado (`user-access-links`).

O convidado SHALL NOT ter senha, SHALL NOT entrar, e SHALL NOT receber código nem token do emissor. A leitura de um estado gravado fora dos três valores SHALL falhar com erro que cita o valor, e SHALL NOT tratá-lo como nenhum dos três.

#### Scenario: E-mail com caixa e espaços diferentes identifica o mesmo usuário
- **WHEN** existe um usuário com e-mail `Maria@Empresa.com` e alguém entra com ` maria@empresa.com `
- **THEN** o login é avaliado contra esse usuário

#### Scenario: E-mail duplicado é recusado pelo banco
- **WHEN** se tenta gravar um segundo usuário cujo e-mail normalizado é igual ao de um existente
- **THEN** a gravação falha por violação de unicidade

#### Scenario: O sub do token é o id do usuário
- **WHEN** um usuário obtém um access token pelo fluxo do painel
- **THEN** o claim `sub` do token é o identificador do usuário, e não o e-mail

#### Scenario: O estado convidado é lido como convidado
- **WHEN** uma linha de `users` tem `status = 'invited'`
- **THEN** o usuário é lido no estado convidado, e não como desativado

#### Scenario: Estado desconhecido falha na leitura
- **WHEN** uma linha de `users` tem um `status` fora de `active`, `deactivated` e `invited`
- **THEN** a leitura falha com um erro que cita o valor gravado

### Requirement: Resposta de login não revela se a conta existe
Um e-mail sem conta, um usuário sem senha (o convidado) e uma senha errada SHALL receber a mesma resposta (`401`, com a mesma mensagem genérica), com a falha contada no bloqueio do e-mail. O e-mail sem conta e o usuário sem senha gravada SHALL passar por uma verificação de hash com os mesmos parâmetros, de modo que o tempo de resposta não distingue os três casos. Um usuário desativado SHALL receber `403` com a mensagem "Acesso desativado. Fale com o administrador." só quando a senha estiver correta. Com a senha errada, SHALL receber a mesma resposta genérica.

#### Scenario: Conta inexistente e senha errada respondem igual
- **WHEN** uma tentativa usa um e-mail sem conta e outra usa um e-mail existente com senha errada
- **THEN** as duas respostas têm o mesmo status e o mesmo corpo

#### Scenario: Convidado responde como conta inexistente
- **WHEN** uma tentativa usa o e-mail de um convidado e outra usa um e-mail sem conta
- **THEN** as duas respostas têm o mesmo status e o mesmo corpo, e a falha é contada nos dois e-mails

#### Scenario: Usuário sem senha passa pela verificação contra o hash fictício
- **WHEN** a verificação de senha recebe um hash gravado nulo ou vazio
- **THEN** ela reprova, e a verificação contra o hash fictício é chamada uma vez, como para o e-mail sem conta

#### Scenario: Desativado com a senha certa
- **WHEN** um usuário desativado entra com a senha correta
- **THEN** a resposta é `403` com "Acesso desativado. Fale com o administrador.", e nenhum cookie de sessão é gravado

#### Scenario: Desativado com a senha errada
- **WHEN** um usuário desativado entra com a senha errada
- **THEN** a resposta é igual à de senha errada de um usuário ativo
