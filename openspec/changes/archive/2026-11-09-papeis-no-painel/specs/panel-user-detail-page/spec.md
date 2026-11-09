## MODIFIED Requirements

### Requirement: Cabeçalho de Editar usuário
O cabeçalho SHALL ter:
- o link de volta "Usuários" para `/users`;
- o `nome` como título;
- o selo do estado: "Ativo" (verde) para `ativo`, "Desativado" (cinza) para `desativado`, e o valor cru em selo neutro para outro valor;
- o selo "Você" quando o usuário é o próprio;
- à direita, "Desativar" (perigo) quando o usuário é outro e está ativo, ou "Reativar" (primário) quando está desativado. O próprio SHALL NOT ter "Desativar";
- o subtítulo, com duas partes unidas por " · ":
  - a origem: "Convidado por {convidadoPor.nome} em {criadoEm, dd/mm/aaaa}", ou "Criado na instalação" quando `convidadoPor` é nulo. A origem SHALL NOT falar em administrador: o usuário criado na instalação pode ter outro papel;
  - o estado: "desativado em {desativadoEm, dd/mm/aaaa}" para o desativado; senão "último acesso {ultimoAcesso}", no formato relativo de datas de Minha conta ("hoje, 14:15", "ontem, 18:40", `dd/mm/aaaa, HH:MM`). Com `ultimoAcesso` nulo, a segunda parte SHALL NOT aparecer.

#### Scenario: Outro usuário ativo
- **WHEN** o detalhe é `{ nome: "Rafael Duarte", estado: "ativo", convidadoPor: { nome: "Aldovrando Oliveira" }, criadoEm: "2026-08-12T…", ultimoAcesso: <ontem às 18:40> }`
- **THEN** o título é "Rafael Duarte", o selo é "Ativo", há "Desativar", e o subtítulo é "Convidado por Aldovrando Oliveira em 12/08/2026 · último acesso ontem, 18:40"

#### Scenario: Desativado
- **WHEN** o detalhe tem `estado: "desativado"` e `desativadoEm: "2026-09-20T…"`
- **THEN** o selo é "Desativado", há "Reativar" e não há "Desativar", e o subtítulo termina em "desativado em 20/09/2026"

#### Scenario: Criado na instalação
- **WHEN** o detalhe tem `convidadoPor: null` e `ultimoAcesso` de agora
- **THEN** o subtítulo é "Criado na instalação · último acesso agora", e não contém "administrador"

#### Scenario: Sem último acesso
- **WHEN** o detalhe de um ativo tem `ultimoAcesso: null`
- **THEN** o subtítulo tem só a origem, sem " · " e sem "último acesso"

#### Scenario: Estado desconhecido
- **WHEN** o detalhe tem `estado: "suspenso"`
- **THEN** o selo mostra "suspenso" em tom neutro, e não em verde

### Requirement: Reconhecimento do próprio usuário
A página SHALL reconhecer o próprio usuário pelo `sub` do id token da sessão, comparado com o `:id` da rota sem diferença de caixa. O `sub` SHALL ser lido do id token na hora, e o registro da sessão SHALL NOT ganhar campo novo: um registro gravado antes desta change continua válido. Nome e e-mail SHALL NOT servir para o reconhecimento. Sem `sub` legível, o usuário SHALL ser tratado como outro. Quando o registro da sessão muda (outra aba), o reconhecimento SHALL ser refeito.

Para o próprio, a página SHALL mostrar:
- a nota "Este é você. A sua senha, a sua verificação em 2 etapas e as suas sessões ficam em Minha conta.", com "Minha conta" como link para `/account`, e `role="note"`;
- o selo "Você", e não "Desativar";
- na linha Senha, o link "Trocar em Minha conta", e não "Redefinir senha";
- na verificação, "Para trocar de celular, peça a outro Admin que redefina a sua verificação.", sem botão;
- nas sessões, a contagem do requisito "Sessões", a ajuda "As suas sessões aparecem em Minha conta, onde você pode encerrar cada uma." e o link "Ver em Minha conta", sem "Encerrar todas as sessões";
- o cartão "Dados e papel" editável, como para outro usuário, com o rebaixamento de si mesmo do requisito "Rebaixar a si mesmo".

Os `403` do servidor para as ações vetadas a si mesmo SHALL aparecer pelo `title` no aviso da ação, caso o reconhecimento falhe. Na redefinição da verificação, o `title` é "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor." (`two-factor-totp`).

#### Scenario: O próprio é o do sub
- **WHEN** o `sub` do id token é `u-ana`, a rota é `/users/u-ana`, e outro usuário tem o mesmo nome e e-mail que constam na sessão
- **THEN** a página de `/users/u-ana` mostra o selo "Você" e a nota "Este é você…", e a página de qualquer outro id não mostra

#### Scenario: Id da rota em maiúsculas ainda é o próprio
- **WHEN** o `sub` é `3f2a…c1` e a rota é `/users/3F2A…C1`
- **THEN** a página mostra o selo "Você"

#### Scenario: Registro gravado antes desta change continua válido
- **WHEN** o registro da sessão no `localStorage` tem só os campos de antes desta change
- **THEN** o `parseSession` o aceita, e a sessão continua aberta

#### Scenario: Sem sub legível
- **WHEN** o id token da sessão não tem payload legível
- **THEN** a página trata o usuário como outro, sem erro

#### Scenario: O próprio não tem Desativar
- **WHEN** a página é a do próprio usuário
- **THEN** não há "Desativar", "Redefinir senha", "Redefinir verificação" nem "Encerrar todas as sessões", há os links para Minha conta, e a verificação mostra "Para trocar de celular, peça a outro Admin que redefina a sua verificação."

#### Scenario: 403 do servidor no aviso
- **WHEN** o reconhecimento falhou, e "Desativar" confirmado recebe `403` "Você não pode desativar o próprio usuário."
- **THEN** o texto aparece no aviso da confirmação, e a página não muda de estado

#### Scenario: 403 da redefinição de si mesmo
- **WHEN** o reconhecimento falhou, e "Redefinir" confirmado recebe `403` "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor."
- **THEN** o texto aparece no aviso da confirmação, e a verificação continua "Ativa"

### Requirement: Cartão Dados de Editar usuário
O cartão "Dados e papel" do quadro `UsuarioDetalhe` (`entrega=m2`) SHALL ter os campos "Nome" e "E-mail", com os valores do detalhe e a ajuda do e-mail "É o e-mail de entrada no painel.", e o campo "Papel", com as opções "Admin", "Operador" e "Leitor", o papel do detalhe selecionado, e, abaixo, a descrição do papel selecionado:
- Admin: "Tudo, inclusive usuários e Identidade.";
- Operador: "Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade.";
- Leitor: "Vê agentes, MCP, conhecimento, canais e Insights. Não altera nada, não vê o histórico de conversas nem usa os conectores.".

Com `papel` desconhecido no detalhe, o Papel SHALL começar sem seleção, e nenhuma descrição SHALL ser mostrada até a escolha.

**O desativado:** o Papel SHALL ficar desabilitado, com "Reative o usuário antes de mudar o papel." abaixo da descrição. Nome e E-mail continuam editáveis.

**Promover a Admin:** com "Admin" escolhido para um alvo cujo papel lido não é Admin, o cartão SHALL mostrar o bloco "Como Admin, {nome} passa a gerir usuários e a ver Identidade. Para promover, confirme com a sua senha." com o campo "Sua senha", com as regras de "Senha de quem chama" de `panel-users-page`: vazia não é enviada; `401` "Senha inválida." no campo; `423` com o aviso de bloqueio no bloco (`role="alert"`) e "Salvar alterações" desabilitado até o `Retry-After`. A senha SHALL sair do campo no sucesso, depois do `401` e quando o bloco some.

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

## ADDED Requirements

### Requirement: Rebaixar a si mesmo
No próprio usuário de papel Admin, "Salvar alterações" com outro papel escolhido SHALL abrir, no cartão "Dados e papel", a confirmação do quadro (`acao=rebaixar-a-si-mesmo`), com `role="alert"`: "Deixar de ser Admin? Usuários e Identidade saem do seu menu agora, e você vai para Minha conta. Para voltar a ser Admin, outro Admin precisa promover você.", com "Deixar de ser Admin" (perigo) e "Cancelar". Nenhuma rota SHALL ser chamada antes de "Deixar de ser Admin".

"Deixar de ser Admin" SHALL seguir a ordem de "Salvar" do requisito "Cartão Dados de Editar usuário". Com o `200` do `PUT /users/{id}/role`, o painel SHALL:
1. pedir a renovação da sessão, para o id token trazer o papel novo;
2. SHALL NOT reler nem invalidar `GET /users` nem `GET /users/{id}`, que responderiam `403`;
3. ir a `/account`, levando o papel novo no estado da navegação, e não na URL, para Minha conta mostrar o aviso de `panel-account-page`.

O `409` do último Admin SHALL aparecer abaixo do Papel, como no requisito "Cartão Dados de Editar usuário", e a página SHALL continuar em Editar usuário. "Cancelar" SHALL fechar a confirmação, com o Papel ainda escolhido.

#### Scenario: A confirmação antes da rota
- **WHEN** o próprio Admin escolhe "Operador" e aciona "Salvar alterações"
- **THEN** aparece "Deixar de ser Admin? Usuários e Identidade saem do seu menu agora, e você vai para Minha conta. Para voltar a ser Admin, outro Admin precisa promover você.", e nenhum `PUT /users/{id}/role` é feito

#### Scenario: Rebaixado vai a Minha conta sem reler Usuários
- **WHEN** o próprio Admin confirma "Deixar de ser Admin", e `PUT /users/{id}/role` responde `200` com `papel: "operador"`
- **THEN** a sessão renova, o painel vai a `/account` e mostra "O seu papel agora é Operador.", e nenhum `GET /users` nem `GET /users/{id}` é feito depois do `200`

#### Scenario: O último Admin não se rebaixa
- **WHEN** o próprio Admin confirma, e a rota responde `409` "A instalação precisa de pelo menos um Admin ativo."
- **THEN** o texto aparece abaixo do Papel, e a página continua em `/users/{id}`
