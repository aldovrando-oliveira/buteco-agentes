## MODIFIED Requirements

### Requirement: Cartão Dados de Editar usuário
O cartão "Dados e papel" do quadro `UsuarioDetalhe` (`entrega=m3`) SHALL ter os campos "Nome" e "E-mail", com os valores do detalhe e a ajuda do e-mail "É o login com senha. Trocar não afeta as contas conectadas.", e o campo "Papel", com as opções "Admin", "Operador" e "Leitor", o papel do detalhe selecionado, e, abaixo, a descrição do papel selecionado:
- Admin: "Tudo, inclusive usuários e Identidade.";
- Operador: "Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade.";
- Leitor: "Vê agentes, MCP, conhecimento, canais e Insights. Não altera nada, não vê o histórico de conversas nem usa os conectores.".

Com `papel` desconhecido no detalhe, o Papel SHALL começar sem seleção, e nenhuma descrição SHALL ser mostrada até a escolha.

**O desativado:** o Papel SHALL ficar desabilitado, com "Reative o usuário antes de mudar o papel." abaixo da descrição. Nome e E-mail continuam editáveis.

**Promover a Admin:** com "Admin" escolhido para um alvo cujo papel lido não é Admin, o cartão SHALL mostrar o bloco "Como Admin, {nome} passa a gerir usuários e a ver Identidade. Para promover, confirme com a sua senha." com o campo "Sua senha" (no modo código, "Como Admin, {nome} passa a gerir usuários e a ver Identidade. Para promover, confirme com o código do app autenticador." com o campo "Código do app autenticador"), com as regras de "Senha de quem chama" de `panel-users-page`, inclusive o modo e o corpo (`PUT /users/{id}/role` com `{ papel, senha }` ou `{ papel, codigo }`): vazia não é enviada; `401` ("Senha inválida." ou "Código inválido.") no campo; `423` com o aviso de bloqueio no bloco (`role="alert"`) e "Salvar alterações" desabilitado até o `Retry-After`. A senha SHALL sair do campo no sucesso, depois do `401` e quando o bloco some.

**Salvar.** "Salvar alterações" (primário) SHALL ficar desabilitado enquanto Nome, E-mail e Papel forem iguais aos lidos. Com um deles diferente, SHALL aparecer "Descartar", que volta aos valores lidos. "Salvar alterações" SHALL, nesta ordem:
1. com Nome ou E-mail diferentes, chamar `PUT /users/{id}` com `{ nome, email }`, como digitados;
2. só depois do `200` do passo 1, ou sem o passo 1, e com o Papel diferente, chamar `PUT /users/{id}/role` com `{ papel }`, e com `{ papel, senha }` na promoção a Admin.

O passo 1 que falha SHALL NOT ser seguido do passo 2. Respostas do passo 1:
- `200`: o cache do detalhe SHALL receber a resposta, e a lista de Usuários SHALL ser invalidada. Quando o usuário é o próprio, a consulta de Minha conta (`GET /account`) também SHALL ser invalidada, e a sessão SHALL NOT ser renovada por isso: o rodapé da casca mostra o nome novo na renovação seguinte, em até 15 min;
- `409`: "Já existe um usuário com este e-mail." no campo E-mail, com `aria-invalid`;
- `400`: o `errors.nome` e o `errors.email` do servidor nos campos;
- `404`: a página vai ao estado de usuário inexistente;
- `403`: o aviso de ação recusada (`panel-roles`);
- `429`, `503` ou rede: o aviso de falha dos cartões (`utils/failureNotice`) no cartão.

Respostas do passo 2:
- `200`: o cache do detalhe SHALL receber a resposta, e a lista de Usuários SHALL ser invalidada;
- `409` "A instalação precisa de pelo menos um Admin ativo.": o texto abaixo do Papel, e o Papel continua com a escolha;
- `409` "Reative o usuário antes de mudar o papel.", `400` (`errors.papel`): o texto abaixo do Papel;
- `401`, `423`: como no bloco de promoção;
- `403`: o aviso de ação recusada (`panel-roles`);
- `404`: a página vai ao estado de usuário inexistente;
- `429`, `503` ou rede: o aviso de falha dos cartões no cartão.

**Os textos do resultado**, ao lado do botão, até a próxima edição:
- só o passo 1, com `200`: "Alterações salvas.";
- o passo 2 com `200`: "Alterações salvas. O novo papel chega ao painel de {nome} em até 15 minutos, quando a sessão renovar; um rebaixamento já vale na hora em Usuários e Identidade.";
- o passo 1 com `200` e o passo 2 com falha: o aviso "Nome e e-mail salvos. O papel não mudou: {motivo}." (`role="alert"`), com o `title` da resposta como motivo (o texto de bloqueio no `423`, o texto genérico de falha sem `title`), e o Papel continua com a escolha.

A página SHALL NOT ter guarda de rascunho ao sair dela.

#### Scenario: Salvar desabilitado sem mudança
- **WHEN** o cartão abre com os valores lidos
- **THEN** "Salvar alterações" está desabilitado, não há "Descartar", e o título do cartão é "Dados e papel"

#### Scenario: Voltar ao valor lido desabilita Salvar
- **WHEN** a pessoa muda o Nome e depois o devolve ao texto lido
- **THEN** "Salvar alterações" volta a ficar desabilitado

#### Scenario: Salvar com sucesso
- **WHEN** a pessoa muda o Nome para "Rafael D. Souza", e `PUT /users/u-rafael` responde `200` com o detalhe novo
- **THEN** o pedido leva `{ nome: "Rafael D. Souza", email: "rafael.duarte@empresa.com" }`, o título passa a "Rafael D. Souza", "Alterações salvas." aparece, nenhum `PUT /users/u-rafael/role` é feito, e a lista de Usuários é invalidada

#### Scenario: O próprio salva e Minha conta é relida
- **WHEN** o próprio usuário salva o Nome com `200`
- **THEN** a consulta de `GET /account` é invalidada, e nenhuma renovação de sessão é pedida

#### Scenario: Rebaixar outro
- **WHEN** a pessoa muda o Papel de "Rafael Duarte" de "Admin" para "Operador", e `PUT /users/u-rafael/role` responde `200`
- **THEN** o pedido leva `{ papel: "operador" }`, sem `senha`, e aparece "Alterações salvas. O novo papel chega ao painel de Rafael Duarte em até 15 minutos, quando a sessão renovar; um rebaixamento já vale na hora em Usuários e Identidade."

#### Scenario: Dados antes do papel
- **WHEN** a pessoa muda o Nome e o Papel, e aciona "Salvar alterações"
- **THEN** o `PUT /users/u-rafael` sai antes do `PUT /users/u-rafael/role`, e o segundo só sai depois do `200` do primeiro

#### Scenario: Dados com falha não mudam o papel
- **WHEN** a pessoa muda o E-mail e o Papel, e `PUT /users/u-rafael` responde `409`
- **THEN** o campo E-mail mostra o texto, e nenhum `PUT /users/u-rafael/role` é feito

#### Scenario: Gravação parcial
- **WHEN** o próprio Admin, único Admin ativo, muda o Nome e o Papel para "Operador" e confirma "Deixar de ser Admin", `PUT /users/{id}` responde `200`, e `PUT /users/{id}/role` responde `409` "A instalação precisa de pelo menos um Admin ativo."
- **THEN** aparece "Nome e e-mail salvos. O papel não mudou: A instalação precisa de pelo menos um Admin ativo.", e o Papel continua "Operador"

#### Scenario: O último Admin
- **WHEN** só o Papel muda, e `PUT /users/{id}/role` responde `409` "A instalação precisa de pelo menos um Admin ativo."
- **THEN** o texto aparece abaixo do Papel, e "Alterações salvas." não aparece

#### Scenario: Promover pede a senha
- **WHEN** a pessoa escolhe "Admin" para "Rafael Duarte", Operador
- **THEN** aparece "Como Admin, Rafael Duarte passa a gerir usuários e a ver Identidade. Para promover, confirme com a sua senha." com o campo "Sua senha"
- **AND** com a senha vazia, "Salvar alterações" não chama a rota, e com a senha preenchida o pedido leva `{ papel: "admin", senha }`

#### Scenario: Promoção com a senha errada
- **WHEN** `PUT /users/u-rafael/role` com a senha responde `401` "Senha inválida."
- **THEN** o campo "Sua senha" mostra "Senha inválida.", com `aria-invalid="true"`, fica vazio, e o pedido saiu uma vez só

#### Scenario: Promoção bloqueada
- **WHEN** `PUT /users/u-rafael/role` responde `423` com `Retry-After: 300`
- **THEN** o bloco mostra o aviso de bloqueio com "5 minutos", e "Salvar alterações" fica desabilitado até o prazo

#### Scenario: Desativado não muda de papel
- **WHEN** o detalhe tem `estado: "desativado"`
- **THEN** o Papel está desabilitado, com "Reative o usuário antes de mudar o papel.", e Nome e E-mail continuam editáveis

#### Scenario: Erros de validação do servidor
- **WHEN** `PUT` responde `400` com `errors: { nome: ["O nome precisa ter de 1 a 200 caracteres."] }`
- **THEN** o texto aparece no campo Nome

#### Scenario: Descartar
- **WHEN** a pessoa muda o E-mail e o Papel e aciona "Descartar"
- **THEN** o E-mail volta ao lido, o Papel volta ao lido, e "Salvar alterações" fica desabilitado

#### Scenario: A ajuda do e-mail da M3
- **WHEN** o detalhe de outro usuário aparece
- **THEN** o campo "E-mail" tem a ajuda "É o login com senha. Trocar não afeta as contas conectadas."

#### Scenario: A promoção com o código
- **WHEN** o Admin sem senha (`GET /account` com `formasDeEntrada: ["google"]`) escolhe "Admin" para Rafael Duarte
- **THEN** aparece "Como Admin, Rafael Duarte passa a gerir usuários e a ver Identidade. Para promover, confirme com o código do app autenticador." com o campo "Código do app autenticador"
- **AND** salvar com `123456` manda `PUT /users/u-rafael/role` com `{ papel: "admin", codigo: "123456" }`, sem `senha`

### Requirement: Formas de entrada
O cartão "Formas de entrada" do quadro `UsuarioDetalhe` (`entrega=m3`) SHALL ter a tabela Forma · Identificação · Conectada em · Último uso · Ações, com a linha "Senha" e uma linha por item de `contasConectadas` do detalhe, na ordem da resposta.

**A linha Senha** SHALL aparecer sempre. Com `"senha"` em `formasDeEntrada`, a identificação SHALL ser o `email` em fonte monoespaçada; sem, "Sem senha". "Conectada em" e "Último uso" SHALL ser "—" (a data da senha não é servida). As ações:
- para outro usuário ativo com senha: o botão "Redefinir senha";
- para outro usuário ativo sem senha: o botão "Gerar link de senha";
- para o próprio, com senha: o link "Trocar em Minha conta" (`/account`); sem senha, nenhuma ação;
- para o desativado: o texto "Reative para gerar um link", sem botão.

**As contas conectadas** SHALL seguir as linhas e a regra da última forma de "Formas de entrada em Minha conta" (`panel-federated-accounts`), com as ações de "Desconectar pelo Admin em Editar usuário". O rótulo da Forma SHALL vir do mapa de tipos conhecidos (`google` → "Google"); um `tipo` desconhecido SHALL sair cru, sem ação.

O rodapé do cartão SHALL ser "Contas de provedores são conectadas pela própria pessoa em Minha conta. Aqui o administrador só desconecta. A última forma de entrada não pode ser removida.".

#### Scenario: A linha Senha de outro usuário
- **WHEN** o detalhe de um ativo tem `formasDeEntrada: ["senha"]`, `contasConectadas: []` e `email: "rafael.duarte@empresa.com"`
- **THEN** o cartão tem a linha "Senha" com "rafael.duarte@empresa.com", "—" em "Conectada em" e "Último uso", e o botão "Redefinir senha"
- **AND** o rodapé é "Contas de provedores são conectadas pela própria pessoa em Minha conta. Aqui o administrador só desconecta. A última forma de entrada não pode ser removida."

#### Scenario: A linha Senha do desativado
- **WHEN** o detalhe tem `estado: "desativado"`
- **THEN** a linha Senha mostra "Reative para gerar um link", sem "Redefinir senha"

#### Scenario: A senha e o Google
- **WHEN** o detalhe de um ativo tem `formasDeEntrada: ["senha", "google"]` e uma conta Google disponível de "rafael.duarte@empresa.com", conectada em 14/08/2026
- **THEN** a linha "Google" mostra o e-mail, "14/08/2026" e "Desconectar"

#### Scenario: A pessoa sem senha
- **WHEN** o detalhe de um ativo tem `formasDeEntrada: ["google"]`
- **THEN** a linha Senha mostra "Sem senha" e o botão "Gerar link de senha"

#### Scenario: O vínculo fora da configuração, no Admin
- **WHEN** o detalhe tem um item com `disponivel: false`
- **THEN** a linha mostra o selo "Provedor fora da configuração" e "Sem ação"

### Requirement: Redefinir senha com a senha de quem chama
"Redefinir senha" SHALL abrir, dentro do cartão Formas de entrada, o formulário com:
- o título "Gerar link para {nome} criar uma nova senha";
- o texto "A senha atual continua valendo até a nova ser criada. Quando isso acontecer, a pessoa sai de todos os navegadores.";
- o campo da confirmação de quem chama da `panel-users-page` ("Senha de quem chama", com o modo), com a ajuda "Para gerar o link, confirme que é você." no modo senha;
- "Gerar link" (primário) e "Cancelar".

"Gerar link de senha", para a pessoa sem senha, SHALL abrir o mesmo formulário, com o título "Gerar link para {nome} criar uma senha" e o texto "Hoje a pessoa entra só pelo Google. Com o link, ela cria uma senha e passa a ter as duas formas de entrada.".

"Gerar link" SHALL seguir o requisito "Senha de quem chama" da `panel-users-page` e chamar `POST /users/{id}/password-reset` com `{ senha }` ou `{ codigo }`, pelo modo:
- o campo vazio: o texto do vazio do modo no campo, sem chamar a rota;
- `401` ("Senha inválida." ou "Código inválido."): o `title` no campo, com `aria-invalid`, o campo limpo, sem renovar a sessão nem deslogar;
- `423` + `Retry-After`: o aviso amarelo "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por N minutos." acima do campo, com o N do `Retry-After`, e "Gerar link" desabilitado até passar;
- `403`: o `title` no aviso do formulário;
- `409` ("Usuário desativado." ou "Este usuário ainda não aceitou o convite."): o `title` no aviso do formulário, e o detalhe relido;
- `404`: a página vai ao estado de usuário inexistente;
- `429`, `503` ou rede: o aviso de falha dos cartões;
- `200 { link, expiraEm }`: o painel do link (requisito "Link de redefinição mostrado uma vez").

"Cancelar" SHALL fechar o formulário e limpar o campo. A senha ou o código SHALL só existir no estado do formulário, e SHALL ser limpo no sucesso, depois do `401`, ao fechar e ao desmontar.

#### Scenario: O formulário
- **WHEN** a pessoa aciona "Redefinir senha" no detalhe de "Rafael Duarte"
- **THEN** aparece "Gerar link para Rafael Duarte criar uma nova senha", o campo "Sua senha" com a ajuda "Para gerar o link, confirme que é você.", "Gerar link" e "Cancelar"

#### Scenario: Senha vazia não chama a rota
- **WHEN** a pessoa aciona "Gerar link" com "Sua senha" vazia
- **THEN** "Informe a sua senha." aparece no campo, e `POST /users/u-rafael/password-reset` não é chamado

#### Scenario: Senha errada na redefinição fica no campo, sem deslogar
- **WHEN** `POST /users/u-rafael/password-reset` responde `401` com `Content-Type: application/problem+json` e `title` "Senha inválida."
- **THEN** "Senha inválida." aparece no campo "Sua senha" com `aria-invalid="true"`, o campo fica vazio, nenhuma renovação nem logout é pedido, e a sessão continua no registro

#### Scenario: Conta pausada
- **WHEN** a rota responde `423` com `Retry-After: 120`
- **THEN** o aviso diz "…pausada por 2 minutos.", e "Gerar link" fica desabilitado até passar o prazo

#### Scenario: Desativado no meio
- **WHEN** a rota responde `409` "Usuário desativado."
- **THEN** o texto aparece no aviso do formulário, e `GET /users/u-rafael` é chamado de novo

#### Scenario: O link de senha para quem entra só pelo Google
- **WHEN** o Admin aciona "Gerar link de senha" no detalhe de Rafael Duarte, sem senha
- **THEN** aparece "Gerar link para Rafael Duarte criar uma senha" e "Hoje a pessoa entra só pelo Google. Com o link, ela cria uma senha e passa a ter as duas formas de entrada."

#### Scenario: A redefinição com o código
- **WHEN** o Admin sem senha digita `123456` e aciona "Gerar link"
- **THEN** `POST /users/u-rafael/password-reset` recebe exatamente `{ "codigo": "123456" }`

### Requirement: Verificação em 2 etapas de outro usuário
O cartão "Verificação em 2 etapas" SHALL mostrar, por `duasEtapas`:
- `true`: o selo "Ativa" (verde) e o botão "Redefinir verificação";
- `false`: o selo "Não configurada" (amarelo) e o botão "Redefinir verificação" desabilitado.

Nos dois casos, o texto SHALL ser "Obrigatória para todos os papéis, inclusive para quem entra pelo Google: o Buteco pede o próprio código." (quadro `UsuarioDetalhe`, `entrega=m3`).

O desativado com a verificação ativa SHALL ter o botão habilitado. O cartão SHALL NOT mostrar a data de ativação.

"Redefinir verificação" SHALL abrir, no cartão, a confirmação "Redefinir a verificação de {nome}? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo.", com "Redefinir" (perigo) e "Cancelar". "Redefinir" SHALL chamar `POST /users/{id}/two-factor/reset`, sem corpo:
- `204` ou `409`: a confirmação fecha, sem aviso, e o detalhe é relido; o cartão e as sessões passam a refletir a releitura;
- `403`: o `title` no aviso da confirmação;
- `404`: a página vai ao estado de usuário inexistente;
- `429`, `503` ou rede: o aviso de falha dos cartões, com a confirmação aberta.

#### Scenario: Verificação ativa
- **WHEN** o detalhe de outro usuário tem `duasEtapas: true`
- **THEN** o cartão mostra "Ativa", "Obrigatória para todos os papéis, inclusive para quem entra pelo Google: o Buteco pede o próprio código." e o botão "Redefinir verificação" habilitado

#### Scenario: Com a verificação desligada, o botão fica desabilitado
- **WHEN** o detalhe tem `duasEtapas: false`
- **THEN** o cartão mostra "Não configurada" e "Obrigatória para todos os papéis, inclusive para quem entra pelo Google: o Buteco pede o próprio código.", e "Redefinir verificação" está desabilitado

#### Scenario: A confirmação diz o que acontece no próximo login
- **WHEN** "Redefinir verificação" é acionado para Rafael Duarte
- **THEN** a confirmação mostra "Redefinir a verificação de Rafael Duarte? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo."

#### Scenario: Depois de redefinir a verificação, as sessões vão a Nenhuma sessão ativa
- **WHEN** a confirmação é aceita, `POST …/two-factor/reset` responde `204`, e a releitura traz `duasEtapas: false` e `sessoes.ativas: 0`
- **THEN** o cartão mostra "Não configurada", e o cartão Sessões mostra "Nenhuma sessão ativa", sem "Encerrar todas as sessões"

#### Scenario: Já redefinida em outra aba
- **WHEN** a rota responde `409`
- **THEN** a confirmação fecha sem aviso, e o detalhe é relido

### Requirement: Uma ação aberta por vez
A página SHALL manter no máximo uma ação aberta entre: a confirmação de desativar, o formulário "Redefinir senha", o painel do link de redefinição, a confirmação de redefinir a verificação, a confirmação de encerrar as sessões, e a confirmação de desconectar uma conta de provedor (com o `409` que fica na linha) (`panel-federated-accounts`). Abrir uma ação SHALL fechar a anterior e descartar o que ela guardava, inclusive a senha digitada e um link já mostrado. Os avisos de sucesso ("N sessões encerradas.", "Reativação concluída…") SHALL ficar até a próxima ação. O rascunho do cartão Dados SHALL NOT ser descartado ao abrir uma ação. Com um pedido em voo, o botão dele SHALL mostrar o carregando, e "Cancelar" SHALL ficar desabilitado.

#### Scenario: Abrir outra ação tira o link
- **WHEN** o painel do link de redefinição está aberto, e a pessoa aciona "Encerrar todas as sessões"
- **THEN** o painel do link fecha, a confirmação de encerrar abre, e o link não está em nenhum lugar da página

#### Scenario: O rascunho de Dados sobrevive a uma ação
- **WHEN** o Nome foi editado sem salvar, e a pessoa abre "Redefinir senha" e cancela
- **THEN** o Nome continua com o texto editado, e "Salvar alterações" continua habilitado

#### Scenario: Abrir outra ação fecha o desconectar
- **WHEN** a confirmação de desconectar o Google de Rafael Duarte está aberta, e a pessoa aciona "Redefinir senha"
- **THEN** a confirmação de desconectar fecha, e o formulário da redefinição abre
