## MODIFIED Requirements

### Requirement: Bloqueio progressivo por e-mail, exista ou não a conta
O login SHALL contar as falhas por e-mail normalizado, exista ou não um usuário com aquele e-mail, por estas regras:

- **primeiro bloqueio:** depois de 5 falhas seguidas, o e-mail SHALL ficar bloqueado por 5 minutos;
- **reincidência:** depois de um bloqueio vencido, uma única falha SHALL bloquear de novo, pelo dobro da duração do bloqueio anterior, até o teto de 60 minutos;
- **volta ao início:** quando a última falha tiver mais de 24 horas, a contagem e a duração do último bloqueio SHALL valer como zeradas, e o próximo bloqueio volta a exigir 5 falhas e a durar 5 minutos. A regra SHALL depender só do tempo, e não de a linha já ter sido removida pela limpeza;
- **sucesso:** um login bem-sucedido SHALL zerar a contagem e a duração do último bloqueio. Com a verificação em 2 etapas ligada, o sucesso é o do segundo passo: **a senha certa com o código pendente SHALL NOT zerar a contagem**;
- **desativado com a senha certa:** a resposta `403` SHALL NOT contar como falha e SHALL NOT zerar a contagem. Com a senha errada, conta como falha.

**Contam como falha do e-mail do usuário**, além da senha errada no login: o código errado no segundo passo do login; a senha errada no início do cadastro da verificação em 2 etapas; e o código errado na confirmação do cadastro, na regeneração dos códigos de recuperação e na remoção da verificação.

Durante o bloqueio, o login daquele e-mail e o segundo passo SHALL responder `423 Locked` com `Retry-After`, sem verificar a senha nem o código. A tentativa durante o bloqueio SHALL NOT contar como falha nem estender o bloqueio. As linhas de contagem sem falha há mais de 24 horas e fora de bloqueio SHALL ser removidas pelo ciclo periódico de limpeza do emissor.

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

#### Scenario: Senha certa com o código pendente não zera a contagem
- **WHEN** um usuário com a verificação ligada alterna, cinco vezes, a senha certa e um código errado
- **THEN** a quinta resposta de código errado é `401`, e a tentativa seguinte, de senha ou de código, responde `423`

#### Scenario: Durante o bloqueio, o segundo passo não confere o código
- **WHEN** o e-mail de um usuário com a verificação ligada está bloqueado, e um pedido do segundo passo chega com o cookie da sessão pendente e um código válido
- **THEN** a resposta é `423` com `Retry-After`, e o código não é marcado como usado

#### Scenario: Desativado com a senha certa não mexe na contagem
- **WHEN** o e-mail de um usuário desativado tem 4 falhas seguidas, recebe duas tentativas com a senha certa e, depois, uma com a senha errada
- **THEN** as duas com a senha certa respondem `403`, e não `423`; a com a senha errada é a 5ª falha e bloqueia, e a tentativa seguinte responde `423`

#### Scenario: Tentativa durante o bloqueio não estende o bloqueio
- **WHEN** um e-mail bloqueado por 5 minutos recebe tentativas com a senha errada durante o bloqueio
- **THEN** todas respondem `423`, e o `Retry-After` de cada uma não passa do fim do bloqueio original

#### Scenario: Contagem parada é limpa
- **WHEN** uma linha de contagem não tem falha há mais de 24 horas, não está bloqueada, e o ciclo de limpeza roda
- **THEN** a linha deixa de existir

### Requirement: Último acesso registrado no login e na renovação
O instante de último acesso do usuário SHALL ser atualizado em cada login bem-sucedido e em cada renovação aceita pelo grant `refresh_token`. Com a verificação em 2 etapas ligada, o login bem-sucedido é o segundo passo aceito, e a senha certa sozinha SHALL NOT atualizar o último acesso. Ele SHALL NOT ser atualizado por requisições às rotas de API autenticadas com o access token.

#### Scenario: Login atualiza o último acesso
- **WHEN** um usuário entra com sucesso
- **THEN** o último acesso dele passa a ser o instante do login

#### Scenario: Senha certa com o código pendente não atualiza
- **WHEN** um usuário com a verificação ligada envia a senha certa e não completa o segundo passo
- **THEN** o último acesso dele não muda

#### Scenario: Renovação atualiza o último acesso
- **WHEN** o refresh token de um usuário é trocado por um novo par
- **THEN** o último acesso dele passa a ser o instante da renovação

#### Scenario: Chamada de API não escreve
- **WHEN** um usuário chama `GET /agents` com um access token válido
- **THEN** o último acesso dele não muda
