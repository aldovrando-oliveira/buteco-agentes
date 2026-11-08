# panel-user-detail-page Specification

## Purpose
A página Editar usuário do painel (`/users/:id`): os dados editáveis, a redefinição de senha com a senha de quem chama e o link mostrado uma vez, a redefinição da verificação em 2 etapas, o encerramento das sessões, desativar e reativar, e o reconhecimento do próprio usuário pelo `sub` do id token (change editar-usuario-painel, #183).
## Requirements
### Requirement: Página Editar usuário em /users/:id
`apps/frontend` SHALL prover a página `/users/:id` dentro da casca, com a mesma exigência de sessão das outras rotas autenticadas, pelo quadro `UsuarioDetalhe` (`entrega=m1`). A página SHALL ler `GET /users/{id}` e SHALL mostrar:
- com o pedido em voo: "Carregando o usuário…", com `role="status"`;
- com `404`: "Usuário não encontrado", "O link pode estar errado, ou o convite foi cancelado." e o link "Voltar para Usuários" (`/users`). Um id que não é GUID também recebe `404` do `apps/api`, e a página o trata igual;
- com outra falha (`503`, rede, outro status), depois das tentativas do QueryClient do painel: "Não foi possível carregar o usuário agora." e "Tentar de novo", com `role="alert"`;
- com `200` e `estado: "convidado"`: nada do detalhe. A página SHALL substituir o endereço por `/users` (navegação com `replace`);
- com `200` e outro estado: o cabeçalho e os cartões desta capacidade.

O `404` SHALL NOT ser repetido: a consulta declara um `retry` que recusa o `404` e segue a política do painel no resto. O item "Usuários" do menu SHALL ficar ativo na página.

#### Scenario: Carregando
- **WHEN** o `GET /users/u-rafael` ainda não respondeu
- **THEN** a página mostra "Carregando o usuário…" com `role="status"`

#### Scenario: Usuário inexistente
- **WHEN** `GET /users/u-x` responde `404`
- **THEN** a página mostra "Usuário não encontrado" e o link "Voltar para Usuários" para `/users`

#### Scenario: Id inexistente faz um GET só
- **WHEN** `GET /users/u-x` responde `404`
- **THEN** a rota foi chamada uma vez, e "Usuário não encontrado" aparece sem esperar novas tentativas

#### Scenario: Erro de carga
- **WHEN** `GET /users/u-rafael` falha por rede em todas as tentativas
- **THEN** a página mostra "Não foi possível carregar o usuário agora." e "Tentar de novo", e "Tentar de novo" chama a rota de novo

#### Scenario: O detalhe de um convidado volta para /users
- **WHEN** `GET /users/u-joana` responde `200` com `estado: "convidado"`
- **THEN** o endereço passa a `/users`, sem entrada nova no histórico, e nenhum cartão do detalhe aparece

#### Scenario: Exige sessão
- **WHEN** a pessoa sem sessão abre `/users/u-rafael`
- **THEN** o painel a leva ao `/entrar`, como nas outras rotas autenticadas

### Requirement: Cabeçalho de Editar usuário
O cabeçalho SHALL ter:
- o link de volta "Usuários" para `/users`;
- o `nome` como título;
- o selo do estado: "Ativo" (verde) para `ativo`, "Desativado" (cinza) para `desativado`, e o valor cru em selo neutro para outro valor;
- o selo "Você" quando o usuário é o próprio;
- à direita, "Desativar" (perigo) quando o usuário é outro e está ativo, ou "Reativar" (primário) quando está desativado. O próprio SHALL NOT ter "Desativar";
- o subtítulo, com duas partes unidas por " · ":
  - a origem: "Convidado por {convidadoPor.nome} em {criadoEm, dd/mm/aaaa}", ou "Primeiro administrador, criado na instalação" quando `convidadoPor` é nulo;
  - o estado: "desativado em {desativadoEm, dd/mm/aaaa}" para o desativado; senão "último acesso {ultimoAcesso}", no formato relativo de datas de Minha conta ("hoje, 14:15", "ontem, 18:40", `dd/mm/aaaa, HH:MM`). Com `ultimoAcesso` nulo, a segunda parte SHALL NOT aparecer.

#### Scenario: Outro usuário ativo
- **WHEN** o detalhe é `{ nome: "Rafael Duarte", estado: "ativo", convidadoPor: { nome: "Aldovrando Oliveira" }, criadoEm: "2026-08-12T…", ultimoAcesso: <ontem às 18:40> }`
- **THEN** o título é "Rafael Duarte", o selo é "Ativo", há "Desativar", e o subtítulo é "Convidado por Aldovrando Oliveira em 12/08/2026 · último acesso ontem, 18:40"

#### Scenario: Desativado
- **WHEN** o detalhe tem `estado: "desativado"` e `desativadoEm: "2026-09-20T…"`
- **THEN** o selo é "Desativado", há "Reativar" e não há "Desativar", e o subtítulo termina em "desativado em 20/09/2026"

#### Scenario: Primeiro administrador
- **WHEN** o detalhe tem `convidadoPor: null`
- **THEN** o subtítulo começa por "Primeiro administrador, criado na instalação"

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
- na verificação, "Para trocar de celular, peça a outro usuário que redefina a sua verificação.", sem botão;
- nas sessões, a contagem do requisito "Sessões", a ajuda "As suas sessões aparecem em Minha conta, onde você pode encerrar cada uma." e o link "Ver em Minha conta", sem "Encerrar todas as sessões";
- o cartão Dados editável, como para outro usuário.

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
- **THEN** não há "Desativar", "Redefinir senha", "Redefinir verificação" nem "Encerrar todas as sessões", há os links para Minha conta, e a verificação mostra "Para trocar de celular, peça a outro usuário que redefina a sua verificação."

#### Scenario: 403 do servidor no aviso
- **WHEN** o reconhecimento falhou, e "Desativar" confirmado recebe `403` "Você não pode desativar o próprio usuário."
- **THEN** o texto aparece no aviso da confirmação, e a página não muda de estado

#### Scenario: 403 da redefinição de si mesmo
- **WHEN** o reconhecimento falhou, e "Redefinir" confirmado recebe `403` "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor."
- **THEN** o texto aparece no aviso da confirmação, e a verificação continua "Ativa"

### Requirement: Uma ação aberta por vez
A página SHALL manter no máximo uma ação aberta entre: a confirmação de desativar, o formulário "Redefinir senha", o painel do link de redefinição, a confirmação de redefinir a verificação e a confirmação de encerrar as sessões. Abrir uma ação SHALL fechar a anterior e descartar o que ela guardava, inclusive a senha digitada e um link já mostrado. Os avisos de sucesso ("N sessões encerradas.", "Reativação concluída…") SHALL ficar até a próxima ação. O rascunho do cartão Dados SHALL NOT ser descartado ao abrir uma ação. Com um pedido em voo, o botão dele SHALL mostrar o carregando, e "Cancelar" SHALL ficar desabilitado.

#### Scenario: Abrir outra ação tira o link
- **WHEN** o painel do link de redefinição está aberto, e a pessoa aciona "Encerrar todas as sessões"
- **THEN** o painel do link fecha, a confirmação de encerrar abre, e o link não está em nenhum lugar da página

#### Scenario: O rascunho de Dados sobrevive a uma ação
- **WHEN** o Nome foi editado sem salvar, e a pessoa abre "Redefinir senha" e cancela
- **THEN** o Nome continua com o texto editado, e "Salvar alterações" continua habilitado

### Requirement: Cartão Dados de Editar usuário
O cartão "Dados" SHALL ter os campos "Nome" e "E-mail", com os valores do detalhe, e a ajuda do e-mail "É o e-mail de entrada no painel.". "Salvar alterações" (primário) SHALL ficar desabilitado enquanto os dois campos forem iguais aos lidos. Com um campo diferente, SHALL aparecer "Descartar", que volta aos valores lidos. "Salvar alterações" SHALL chamar `PUT /users/{id}` com `{ nome, email }`, como digitados:
- `200`: "Alterações salvas." ao lado do botão, até a próxima edição. O cache do detalhe SHALL receber a resposta, e a lista de Usuários SHALL ser invalidada. Quando o usuário é o próprio, a consulta de Minha conta (`GET /account`) também SHALL ser invalidada, e a sessão SHALL NOT ser renovada por isso: o rodapé da casca mostra o nome e o e-mail novos na renovação seguinte, em até 15 min;
- `409`: "Já existe um usuário com este e-mail." no campo E-mail, com `aria-invalid`;
- `400`: o `errors.nome` e o `errors.email` do servidor nos campos;
- `404`: a página vai ao estado de usuário inexistente;
- `403`, `429`, `503` ou rede: o aviso de falha dos cartões (`utils/failureNotice`) no cartão.

A página SHALL NOT ter guarda de rascunho ao sair dela.

#### Scenario: Salvar desabilitado sem mudança
- **WHEN** o cartão abre com os valores lidos
- **THEN** "Salvar alterações" está desabilitado e não há "Descartar"

#### Scenario: Voltar ao valor lido desabilita Salvar
- **WHEN** a pessoa muda o Nome e depois o devolve ao texto lido
- **THEN** "Salvar alterações" volta a ficar desabilitado

#### Scenario: Salvar com sucesso
- **WHEN** a pessoa muda o Nome para "Rafael D. Souza", e `PUT /users/u-rafael` responde `200` com o detalhe novo
- **THEN** o pedido leva `{ nome: "Rafael D. Souza", email: "rafael.duarte@empresa.com" }`, o título passa a "Rafael D. Souza", "Alterações salvas." aparece, e a lista de Usuários é invalidada

#### Scenario: O próprio salva e Minha conta é relida
- **WHEN** o próprio usuário salva os Dados com `200`
- **THEN** a consulta de `GET /account` é invalidada, e nenhuma renovação de sessão é pedida

#### Scenario: E-mail em uso
- **WHEN** `PUT /users/u-rafael` responde `409` "Já existe um usuário com este e-mail."
- **THEN** o texto aparece no campo E-mail, e o campo tem `aria-invalid="true"`

#### Scenario: Erros de validação do servidor
- **WHEN** `PUT` responde `400` com `errors: { nome: ["O nome precisa ter de 1 a 200 caracteres."] }`
- **THEN** o texto aparece no campo Nome

#### Scenario: Descartar
- **WHEN** a pessoa muda o E-mail e aciona "Descartar"
- **THEN** o campo volta ao e-mail lido, e "Salvar alterações" fica desabilitado

### Requirement: Formas de entrada
O cartão "Formas de entrada" SHALL ter uma linha por item de `formasDeEntrada`. `senha` SHALL ser a linha "Senha", com a identificação = `email` em fonte monoespaçada. Um valor desconhecido SHALL sair cru, sem ação. A coluna de ações da linha Senha SHALL ser:
- para outro usuário ativo: o botão "Redefinir senha";
- para o próprio: o link "Trocar em Minha conta" (`/account`);
- para o desativado: o texto "Reative para gerar um link", sem botão.

O rodapé do cartão SHALL ser "Por enquanto, a senha é a única forma de entrada.". O cartão SHALL NOT ter as colunas "Conectada em" e "Último uso" nem linhas de provedor federado (M3).

#### Scenario: A linha Senha de outro usuário
- **WHEN** o detalhe de um ativo tem `formasDeEntrada: ["senha"]` e `email: "rafael.duarte@empresa.com"`
- **THEN** o cartão tem a linha "Senha" com "rafael.duarte@empresa.com" e o botão "Redefinir senha"

#### Scenario: A linha Senha do desativado
- **WHEN** o detalhe tem `estado: "desativado"`
- **THEN** a linha Senha mostra "Reative para gerar um link", sem "Redefinir senha"

### Requirement: Redefinir senha com a senha de quem chama
"Redefinir senha" SHALL abrir, dentro do cartão Formas de entrada, o formulário com:
- o título "Gerar link para {nome} criar uma nova senha";
- o texto "A senha atual continua valendo até a nova ser criada. Quando isso acontecer, a pessoa sai de todos os navegadores.";
- "Sua senha", pelo campo de senha de quem chama da `panel-users-page` (o `PasswordField`, `autocomplete="current-password"`), com a ajuda "Para gerar o link, confirme que é você.";
- "Gerar link" (primário) e "Cancelar".

"Gerar link" SHALL seguir o requisito "Senha de quem chama" da `panel-users-page` e chamar `POST /users/{id}/password-reset` com `{ senha }`:
- "Sua senha" vazia: "Informe a sua senha." no campo, sem chamar a rota;
- `401` "Senha inválida.": o `title` no campo, com `aria-invalid`, a senha limpa, sem renovar a sessão nem deslogar;
- `423` + `Retry-After`: o aviso amarelo "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por N minutos." acima do campo, com o N do `Retry-After`, e "Gerar link" desabilitado até passar;
- `403`: o `title` no aviso do formulário;
- `409` ("Usuário desativado." ou "Este usuário ainda não aceitou o convite."): o `title` no aviso do formulário, e o detalhe relido;
- `404`: a página vai ao estado de usuário inexistente;
- `429`, `503` ou rede: o aviso de falha dos cartões;
- `200 { link, expiraEm }`: o painel do link (requisito "Link de redefinição mostrado uma vez").

"Cancelar" SHALL fechar o formulário e limpar a senha. A senha SHALL só existir no estado do formulário, e SHALL ser limpa no sucesso, depois do `401`, ao fechar e ao desmontar.

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

### Requirement: Link de redefinição mostrado uma vez
Com o `200`, o formulário SHALL dar lugar ao painel do link, com a forma do quadro `UsuarioDetalhe` (`acao=link-gerado`):
- o título "Link para redefinir a senha";
- a caixa com o `link` como veio, em fonte monoespaçada, e "Copiar link", que copia o `link` e troca o rótulo por "Copiado ✓". A caixa SHALL ser o mesmo componente do painel do link de convite da `panel-users-page`, que SHALL continuar mostrando o que mostra hoje;
- o texto "Envie o link à pessoa pelo canal que preferir. Vale até {expiraEm} e funciona uma única vez. O Buteco não mostra este link de novo; se ele se perder, gere outro, e o anterior deixa de funcionar.", com `expiraEm` como `dd/mm/aaaa às HH:MM` no fuso do navegador;
- "Concluir".

O painel SHALL NOT mostrar o e-mail, o ícone de sucesso nem o aviso amarelo do painel do convite.

O link e o `expiraEm` SHALL ficar só no estado da página: o pedido SHALL ser uma chamada direta, sem `useMutation`, e a resposta SHALL NOT ir ao cache do TanStack Query, a `sessionStorage`, a `localStorage`, à URL nem ao `history.state`. Depois do `200`, a página SHALL invalidar o detalhe. O link SHALL sumir no "Concluir", ao abrir outra ação e ao sair da página. Sem `navigator.clipboard`, o link SHALL continuar visível e selecionável.

#### Scenario: O painel do link
- **WHEN** `POST /users/u-rafael/password-reset` responde `200 { link: "https://painel.test/redefinir-senha#abc", expiraEm: "2026-11-08T21:30:00Z" }`
- **THEN** aparece "Link para redefinir a senha", o link, "Copiar link", o texto com "Vale até 08/11/2026 às 18:30" (no fuso de São Paulo) e "Concluir", e não aparece o e-mail nem o aviso amarelo

#### Scenario: Copiar link
- **WHEN** a pessoa aciona "Copiar link"
- **THEN** o `link` vai para a área de transferência como veio, e o rótulo passa a "Copiado ✓"

#### Scenario: Concluir tira o link
- **WHEN** a pessoa aciona "Concluir"
- **THEN** o painel fecha, e o link não está em nenhum lugar da página

#### Scenario: O link não fica no MutationCache
- **WHEN** o painel do link está aberto
- **THEN** o `MutationCache` e o `QueryCache` do QueryClient não guardam o link nem a resposta

#### Scenario: O link não vai a storage
- **WHEN** o painel do link está aberto
- **THEN** nenhum valor do `localStorage` e do `sessionStorage`, percorridos por `length` e `key(i)` (e o `buteco.sessao` está entre eles), contém o link, e nem a URL nem o `history.state` o contêm

### Requirement: Verificação em 2 etapas de outro usuário
O cartão "Verificação em 2 etapas" SHALL mostrar, por `duasEtapas`:
- `true`: o selo "Ativa" (verde), "Use Redefinir se a pessoa perdeu ou trocou o celular e os códigos de recuperação." e o botão "Redefinir verificação";
- `false`: o selo "Não configurada" (amarelo), "A pessoa cadastra a verificação no próximo login." e o botão "Redefinir verificação" desabilitado.

O desativado com a verificação ativa SHALL ter o botão habilitado. O cartão SHALL NOT mostrar a data de ativação.

"Redefinir verificação" SHALL abrir, no cartão, a confirmação "Redefinir a verificação de {nome}? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo.", com "Redefinir" (perigo) e "Cancelar". "Redefinir" SHALL chamar `POST /users/{id}/two-factor/reset`, sem corpo:
- `204` ou `409`: a confirmação fecha, sem aviso, e o detalhe é relido; o cartão e as sessões passam a refletir a releitura;
- `403`: o `title` no aviso da confirmação;
- `404`: a página vai ao estado de usuário inexistente;
- `429`, `503` ou rede: o aviso de falha dos cartões, com a confirmação aberta.

#### Scenario: Verificação ativa
- **WHEN** o detalhe de outro usuário tem `duasEtapas: true`
- **THEN** o cartão mostra "Ativa", "Use Redefinir se a pessoa perdeu ou trocou o celular e os códigos de recuperação." e o botão "Redefinir verificação" habilitado

#### Scenario: Com a verificação desligada, o botão fica desabilitado
- **WHEN** o detalhe tem `duasEtapas: false`
- **THEN** o cartão mostra "Não configurada" e "A pessoa cadastra a verificação no próximo login.", e "Redefinir verificação" está desabilitado

#### Scenario: A confirmação diz o que acontece no próximo login
- **WHEN** "Redefinir verificação" é acionado para Rafael Duarte
- **THEN** a confirmação mostra "Redefinir a verificação de Rafael Duarte? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo."

#### Scenario: Depois de redefinir a verificação, as sessões vão a Nenhuma sessão ativa
- **WHEN** a confirmação é aceita, `POST …/two-factor/reset` responde `204`, e a releitura traz `duasEtapas: false` e `sessoes.ativas: 0`
- **THEN** o cartão mostra "Não configurada", e o cartão Sessões mostra "Nenhuma sessão ativa", sem "Encerrar todas as sessões"

#### Scenario: Já redefinida em outra aba
- **WHEN** a rota responde `409`
- **THEN** a confirmação fecha sem aviso, e o detalhe é relido

### Requirement: Sessões de outro usuário
O cartão "Sessões" SHALL mostrar, de `sessoes`:
- com `ativas` ≥ 2: "{N} sessões ativas · último uso {ultimaAtividade}", no formato relativo de datas; com 1: "1 sessão ativa · último uso …". Com `ultimaAtividade` nulo, a parte " · último uso …" SHALL NOT aparecer;
- com `ativas` 0: "Nenhuma sessão ativa";
- a ajuda "Encerrar faz a pessoa precisar entrar de novo em todos os navegadores. Use se suspeitar de acesso indevido.";
- "Encerrar todas as sessões" só quando o usuário é outro e `ativas` ≥ 1.

Para o próprio, SHALL mostrar "{N} sessões ativas", "1 sessão ativa" ou "Nenhuma sessão ativa", **sem** o último uso e **sem** "incluindo esta" (decisão do dono Q12: a sessão atual pode já ter sido revogada enquanto o access token ainda vale).

"Encerrar todas as sessões" SHALL abrir, no cartão, a confirmação "Encerrar as {N} sessões de {nome}? A pessoa precisa entrar de novo em todos os navegadores." (com 1: "Encerrar a sessão de {nome}? A pessoa precisa entrar de novo."), com "Encerrar" (perigo) e "Cancelar". "Encerrar" SHALL chamar `POST /users/{id}/sessions/revoke`, sem senha:
- `200 { encerradas }`: "{N} sessões encerradas." (com 1: "1 sessão encerrada."; com 0: "Nenhuma sessão estava aberta."), com `role="status"`, e o detalhe relido;
- `404`: a página vai ao estado de usuário inexistente;
- `403`, `429`, `503` ou rede: o aviso de falha dos cartões, com a confirmação aberta.

#### Scenario: Duas sessões
- **WHEN** o detalhe de outro usuário tem `sessoes: { ativas: 2, ultimaAtividade: <ontem às 18:45> }`
- **THEN** o cartão mostra "2 sessões ativas · último uso ontem, 18:45" e "Encerrar todas as sessões"

#### Scenario: Sem sessões ativas, não há botão de encerrar
- **WHEN** o detalhe tem `sessoes.ativas: 0`
- **THEN** o cartão mostra "Nenhuma sessão ativa", sem "Encerrar todas as sessões"

#### Scenario: As sessões do próprio sem a dedução
- **WHEN** a página é a do próprio, com `sessoes.ativas: 3`
- **THEN** o cartão mostra "3 sessões ativas" e "Ver em Minha conta", e não mostra "incluindo esta", o último uso nem "Encerrar todas as sessões"

#### Scenario: Encerrar com confirmação
- **WHEN** a pessoa aciona "Encerrar todas as sessões" com 2 ativas, confirma, e a rota responde `200 { encerradas: 2 }`
- **THEN** a confirmação dizia "Encerrar as 2 sessões de Rafael Duarte? …", aparece "2 sessões encerradas.", e o detalhe é relido

#### Scenario: Nenhuma sessão estava aberta
- **WHEN** a rota responde `200 { encerradas: 0 }`
- **THEN** aparece "Nenhuma sessão estava aberta."

### Requirement: Desativar e reativar
"Desativar" SHALL abrir, acima dos cartões, a confirmação "Desativar {nome}? A pessoa sai de todos os navegadores na hora e não consegue mais entrar. Você pode reativar depois, e ela vai precisar entrar de novo.", com `role="alert"`, "Desativar" (perigo) e "Cancelar". "Desativar" da confirmação SHALL chamar `POST /users/{id}/deactivate`:
- `200`: o cache do detalhe recebe a resposta, a página passa a ser a do desativado, sem aviso de sucesso, e a lista de Usuários é invalidada;
- `403` ou `409`: o `title` no aviso da confirmação;
- `404`: a página vai ao estado de usuário inexistente;
- `429`, `503` ou rede: o aviso de falha dos cartões, com a confirmação aberta.

"Reativar" SHALL chamar `POST /users/{id}/activate`, sem confirmação:
- `200`: o cache do detalhe recebe a resposta, a página passa a ser a do ativo, a lista é invalidada, e aparece "Reativação concluída. {nome} pode entrar de novo com a senha que já tinha.", com `role="status"`;
- `403`, `409`, `429`, `503` ou rede: o aviso no topo da página;
- `404`: a página vai ao estado de usuário inexistente.

#### Scenario: Desativar com confirmação
- **WHEN** a pessoa aciona "Desativar" no detalhe de "Rafael Duarte" e confirma, e a rota responde `200` com `estado: "desativado"`
- **THEN** a confirmação dizia "Desativar Rafael Duarte? …", e a página passa a mostrar o selo "Desativado", "Reativar" e "Reative para gerar um link"

#### Scenario: Cancelar a desativação
- **WHEN** a pessoa aciona "Desativar" e depois "Cancelar"
- **THEN** a confirmação fecha, e `POST …/deactivate` não é chamado

#### Scenario: Reativar sem confirmação
- **WHEN** a pessoa aciona "Reativar" no detalhe de "Bruno Teixeira", e a rota responde `200` com `estado: "ativo"`
- **THEN** a rota foi chamada sem confirmação, o selo passa a "Ativo", e aparece "Reativação concluída. Bruno Teixeira pode entrar de novo com a senha que já tinha."

