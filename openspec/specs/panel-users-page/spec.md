# panel-users-page Specification

## Purpose
A página Usuários do painel (`/users`): a lista de quem pode entrar, o convite com a senha de quem chama, o novo link e o cancelamento do convite, com o link de uso único mostrado uma vez e só na memória da tela (change usuarios-convites-identidade-painel, #250). O "Editar" da linha leva a Editar usuário (`panel-user-detail-page`, #183).
## Requirements
### Requirement: Página Usuários em /users
`apps/frontend` SHALL prover a página `/users` dentro da casca, com a mesma exigência de sessão das outras rotas autenticadas. A página SHALL ter:
- o título "Usuários";
- o subtítulo "Quem pode entrar no painel. O acesso é só por convite.";
- o cartão "Usuários" do quadro `Usuarios` (`entrega=m1`);
- abaixo dele, os cartões "Acesso só por convite" e "Senha e recuperação", com os textos do quadro na versão `m1`.

O cartão "Usuários" SHALL ler `GET /users` e SHALL mostrar:
- "Carregando os usuários…" (`role="status"`) enquanto espera;
- em qualquer falha da leitura, "Não foi possível carregar os usuários agora." com o botão "Tentar de novo" (`role="alert"`).

#### Scenario: Sem sessão, /users vai ao login
- **WHEN** alguém sem sessão abre `/users`
- **THEN** o painel vai a `/entrar` com `voltarPara=/users`

#### Scenario: Carregando
- **WHEN** a pessoa abre `/users` e o `GET /users` ainda não respondeu
- **THEN** o cartão mostra "Carregando os usuários…"

#### Scenario: Erro de carga com Tentar de novo
- **WHEN** o `GET /users` falha por rede
- **THEN** o cartão mostra "Não foi possível carregar os usuários agora." e "Tentar de novo"
- **AND** acionar "Tentar de novo" chama o `GET /users` de novo, e a tabela aparece quando ele responde `200`

### Requirement: Colunas da lista de Usuários
A tabela SHALL ter as colunas Usuário, Formas de entrada, Verificação em 2 etapas, Estado, Último acesso e Ações, nesta ordem e sem coluna de papel. As linhas SHALL vir na ordem da resposta, sem reordenação no cliente.

| coluna | o que mostra |
|---|---|
| Usuário | o `nome` em negrito e, abaixo, o `email` em fonte monoespaçada |
| Formas de entrada | um chip "Senha" para cada `senha` em `formasDeEntrada`; "—" quando a lista é vazia; um chip neutro com o valor cru para qualquer outro valor |
| Verificação em 2 etapas | "Ativa" (verde) com `duasEtapas: true`; "Não configurada" (amarelo) com `false`; "—" (neutro) para o convidado |
| Estado | ver a tabela abaixo |
| Último acesso | "—" quando `ultimoAcesso` é nulo; senão o formato relativo de Minha conta: "hoje, HH:MM", "ontem, HH:MM" ou "dd/mm/aaaa, HH:MM", no fuso do navegador |

A coluna Estado SHALL mostrar:

| `estado` | selo | abaixo do selo |
|---|---|---|
| `ativo` | "Ativo" (verde) | — |
| `desativado` | "Desativado" (cinza) | — |
| `convidado` com `convite.vencido: false` | "Convite pendente" (amarelo) | "vence em dd/mm, HH:MM", pelo `convite.expiraEm` |
| `convidado` com `convite.vencido: true` | "Convite vencido" (cinza) | "venceu em dd/mm, HH:MM" |
| qualquer outro valor | neutro, com o valor cru | — |

- O vencido e o pendente SHALL sair do `convite.vencido` da resposta. A tela SHALL NOT recalculá-los pelo relógio do navegador.
- Os selos de valor desconhecido SHALL NOT reaproveitar a cor de sucesso (convenção 13).

#### Scenario: Usuário ativo com a verificação ligada
- **WHEN** a lista traz `{ nome: "Rafael Duarte", email: "rafael@exemplo.test", estado: "ativo", formasDeEntrada: ["senha"], duasEtapas: true, ultimoAcesso: <ontem às 18:40> }`
- **THEN** a linha mostra "Rafael Duarte", "rafael@exemplo.test", o chip "Senha", "Ativa", "Ativo" e "ontem, 18:40"

#### Scenario: Convite pendente
- **WHEN** a lista traz um convidado com `formasDeEntrada: []`, `duasEtapas: false`, `ultimoAcesso: null` e `convite: { expiraEm: "2026-11-10T21:02:00Z", vencido: false }`
- **THEN** a linha mostra "—" nas formas de entrada, "—" na verificação, "Convite pendente" com "vence em 10/11, 18:02" no fuso de São Paulo, e "—" no último acesso

#### Scenario: O vencido vem do servidor
- **WHEN** a lista traz um convidado com `convite: { expiraEm: <uma hora atrás>, vencido: false }`
- **THEN** a linha mostra "Convite pendente", e não "Convite vencido"

#### Scenario: Convite vencido
- **WHEN** a lista traz um convidado com `convite.vencido: true`
- **THEN** a linha mostra o selo cinza "Convite vencido" e "venceu em dd/mm, HH:MM"

#### Scenario: Estado desconhecido sai neutro
- **WHEN** a lista traz um usuário com `estado: "suspenso"`
- **THEN** o selo mostra "suspenso" no tom neutro, e não o verde de "Ativo"

#### Scenario: A ordem é a da resposta
- **WHEN** a lista traz "Bruno" antes de "Ana"
- **THEN** a tabela mostra "Bruno" na primeira linha

### Requirement: Ações da linha sem destino inexistente
A coluna Ações SHALL mostrar, na linha do convidado, "Gerar novo link" e "Cancelar convite", com `aria-label` "Gerar novo link para {nome}" e "Cancelar convite de {nome}".

A linha do usuário ativo e a do desativado SHALL mostrar "Editar", como no quadro `Usuarios`: um link para `/users/{id}`, com o `aria-label` "Editar {nome}" e o id codificado para a URL. A linha do convidado SHALL NOT ter "Editar" nem link para `/users/{id}`: o detalhe de um convidado volta para `/users` (`panel-user-detail-page`).

#### Scenario: Convidado tem as duas ações
- **WHEN** a lista traz um convidado "Joana Prado"
- **THEN** a linha tem os botões "Gerar novo link para Joana Prado" e "Cancelar convite de Joana Prado"

#### Scenario: Ativo e desativado têm Editar
- **WHEN** a lista traz o ativo "Rafael Duarte" (`u-rafael`) e o desativado "Bruno Teixeira" (`u-bruno`)
- **THEN** a linha de cada um tem o link "Editar Rafael Duarte" para `/users/u-rafael` e "Editar Bruno Teixeira" para `/users/u-bruno`

#### Scenario: Convidado não tem Editar
- **WHEN** a lista traz um convidado "Joana Prado"
- **THEN** a linha dele não tem link "Editar", e nenhum elemento dela tem `href` que comece com `/users/`

### Requirement: Um painel por vez no cartão Usuários
O cartão SHALL mostrar no máximo um destes painéis por vez, entre a faixa e a tabela:
- o formulário de convite;
- o formulário de novo link;
- o painel do link gerado.

A confirmação do cancelamento SHALL ficar na própria linha. "Convidar usuário" SHALL ficar desabilitado enquanto algum painel ou confirmação estiver aberto. Abrir um painel SHALL fechar o anterior e descartar o que ele guardava, inclusive um link já mostrado.

#### Scenario: Convidar usuário desabilitado com um painel aberto
- **WHEN** o formulário de novo link está aberto
- **THEN** "Convidar usuário" está desabilitado

#### Scenario: Abrir outro painel descarta o link
- **WHEN** o painel do link está aberto, e a pessoa aciona "Gerar novo link" na linha de outro convidado
- **THEN** o painel do link fecha, o formulário de novo link abre, e o link anterior não está em nenhum lugar da página

### Requirement: Convidar usuário
"Convidar usuário" SHALL abrir, dentro do cartão, o formulário "Convidar usuário" com:
- os campos Nome, E-mail e "Sua senha" (a senha de quem chama);
- o texto "O Buteco gera um link de uso único, válido por 72 horas. Você copia o link e envia para a pessoa pelo canal que preferir.";
- os botões "Gerar convite" (primário) e "Cancelar".

"Gerar convite" SHALL chamar `POST /users/invitations` com `{ nome, email, senha }`.

| resposta | a tela |
|---|---|
| `201 { usuario, link, expiraEm }` | fecha o formulário, mostra o painel do link com o título "Convite criado para {nome}", relê o `GET /users` e marca a linha de `usuario.id` enquanto o painel está aberto |
| `400` | o `errors.nome` e o `errors.email` da resposta, cada um no seu campo, com `aria-invalid` |
| `409` | o `title` da resposta ("Já existe um usuário com este e-mail.") no campo E-mail, com `aria-invalid` |
| `401`, `423`, vazio, `429`, `503`, rede | como em "Senha de quem chama" |

"Cancelar" SHALL fechar o formulário sem chamar a rota.

#### Scenario: Convite criado
- **WHEN** a pessoa preenche "Marcos Lima", "marcos@exemplo.test" e a senha certa, e aciona "Gerar convite", e a rota responde `201`
- **THEN** o painel mostra "Convite criado para Marcos Lima", o link e "Vale até {expiraEm}"
- **AND** a lista é relida, e a linha de Marcos Lima aparece marcada

#### Scenario: E-mail em uso
- **WHEN** a rota responde `409` "Já existe um usuário com este e-mail."
- **THEN** o campo E-mail mostra o texto, com `aria-invalid="true"`, e o formulário continua aberto

#### Scenario: Campos inválidos
- **WHEN** a rota responde `400` com `errors: { email: ["Informe um e-mail válido."] }`
- **THEN** o campo E-mail mostra "Informe um e-mail válido."

#### Scenario: A linha nova fica na ordem do servidor
- **WHEN** o convite de "Marcos Lima" é criado, e a lista relida traz Marcos entre "Joana" e "Paulo"
- **THEN** a linha de Marcos aparece entre as duas, marcada, e deixa de estar marcada depois de "Concluir"

### Requirement: Gerar novo link de convite
"Gerar novo link" na linha de um convidado SHALL abrir, dentro do cartão, o formulário "Gerar novo link para {nome}" com:
- o texto "O link anterior deixa de funcionar na hora, e o novo vale por 72 horas.";
- o campo "Sua senha";
- os botões "Gerar novo link" (primário) e "Cancelar".

O envio SHALL chamar `POST /users/{id}/invitation/renew` com `{ senha }`.

| resposta | a tela |
|---|---|
| `200 { link, expiraEm }` | fecha o formulário e mostra o painel do link com o título "Novo link gerado para {nome}" e o e-mail da linha |
| `409` | fecha o formulário, relê o `GET /users` e mostra no topo do cartão o aviso azul (`role="status"`) "O convite de {nome} já foi aceito, então não há link a gerar. A lista foi atualizada." |
| `404` | fecha o formulário e relê o `GET /users`, sem aviso |
| `401`, `423`, vazio, `429`, `503`, rede | como em "Senha de quem chama" |

#### Scenario: Novo link gerado
- **WHEN** a pessoa informa a senha certa para o convite de "Joana Prado", e a rota responde `200`
- **THEN** o painel mostra "Novo link gerado para Joana Prado", o e-mail dela, o link novo e "Vale até {expiraEm}"

#### Scenario: Convite aceito no meio
- **WHEN** a rota responde `409` "Este usuário não tem convite pendente."
- **THEN** o cartão mostra "O convite de Joana Prado já foi aceito, então não há link a gerar. A lista foi atualizada.", e o `GET /users` é chamado de novo

#### Scenario: Convite cancelado no meio
- **WHEN** a rota responde `404`
- **THEN** o formulário fecha, o `GET /users` é chamado de novo, e nenhum aviso aparece

### Requirement: Cancelar convite com confirmação na linha
"Cancelar convite" SHALL trocar as ações da linha pela confirmação na própria linha: "Cancelar o convite? O link deixa de funcionar.", com "Cancelar convite" (perigo) e "Voltar". Só a confirmação SHALL chamar `POST /users/{id}/invitation/cancel`, sem senha.

| resposta | a tela |
|---|---|
| `204` | relê o `GET /users` |
| `409` | relê o `GET /users` e mostra no topo do cartão o aviso azul (`role="status"`) "O convite de {nome} já foi aceito, então não há o que cancelar. A lista foi atualizada." |
| `404` | relê o `GET /users`, sem aviso |
| `429`, `503`, rede | o aviso de falha na linha, com a confirmação ainda aberta |

"Voltar" SHALL desfazer a confirmação sem chamar a rota.

#### Scenario: O primeiro clique só pede confirmação
- **WHEN** a pessoa aciona "Cancelar convite de Joana Prado"
- **THEN** a linha mostra "Cancelar o convite? O link deixa de funcionar.", e nenhuma chamada à rota de cancelamento acontece

#### Scenario: Convite cancelado
- **WHEN** a pessoa confirma, e a rota responde `204`
- **THEN** o `GET /users` é chamado de novo, e a linha some quando a lista volta sem ela

#### Scenario: Convite já aceito
- **WHEN** a pessoa confirma, e a rota responde `409` "O convite já foi aceito."
- **THEN** o cartão mostra "O convite de Joana Prado já foi aceito, então não há o que cancelar. A lista foi atualizada.", e o `GET /users` é chamado de novo

### Requirement: Senha de quem chama
Os formulários de convite e de novo link SHALL ter o campo "Sua senha", com:
- `autocomplete="current-password"`;
- a ajuda do quadro: "Para convidar alguém, confirme que é você." ou "Para gerar um link, confirme que é você.";
- o componente `PasswordField`.

| caso | a tela |
|---|---|
| campo vazio no envio | "Informe a sua senha." no campo, com `aria-invalid`, sem chamar a rota |
| `401` | o `title` da resposta ("Senha inválida.") no campo, com `aria-invalid`. A pessoa SHALL continuar na página, com a sessão intacta, e a rota SHALL ter sido chamada uma vez só |
| `423` | acima do campo, o aviso amarelo "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por N minutos.", com N do `Retry-After` arredondado para cima ("alguns minutos" sem o cabeçalho legível), e o botão de envio desabilitado até passar o `Retry-After` |
| `429`, `503`, rede | o aviso de falha do formulário, com os textos do `/entrar`, e o botão desabilitado até o `Retry-After` quando houver |

A senha SHALL ficar só no estado do formulário. Ela SHALL ser apagada ao fechar o formulário, no sucesso, depois do `401` e ao sair da página.

#### Scenario: Senha vazia não é enviada
- **WHEN** a pessoa aciona "Gerar convite" com "Sua senha" vazia
- **THEN** o campo mostra "Informe a sua senha.", e nenhuma chamada a `POST /users/invitations` acontece

#### Scenario: Senha errada fica no campo, sem deslogar
- **WHEN** `POST /users/invitations` responde `401` com `Content-Type: application/problem+json` e `title` "Senha inválida."
- **THEN** o campo "Sua senha" mostra "Senha inválida.", com `aria-invalid="true"`
- **AND** a página continua em `/users`, o registro da sessão continua gravado, nenhuma renovação acontece, e a rota foi chamada uma vez só

#### Scenario: Conta pausada
- **WHEN** `POST /users/{id}/invitation/renew` responde `423` com `Retry-After: 300`
- **THEN** o formulário mostra "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.", e "Gerar novo link" fica desabilitado até passarem 300 s

### Requirement: Link de convite mostrado uma vez e só na memória da tela
O painel do link SHALL mostrar:
- "Link do convite", com o `link` em fonte monoespaçada e o botão "Copiar link";
- "Envie para a pessoa por e-mail, chat ou outro canal. Vale até {expiraEm} e funciona uma única vez.", com a data `dd/mm/aaaa às HH:MM` no fuso do navegador;
- o aviso "O Buteco não mostra este link de novo. Se ele se perder ou vencer, use Gerar novo link na linha do convite; o link anterior deixa de funcionar.";
- o botão "Concluir".

"Copiar link" SHALL copiar o `link` exatamente como veio na resposta, e SHALL trocar o rótulo por "Copiado ✓".

O `link` e o `expiraEm` SHALL ficar só no estado do componente do cartão. Em particular:
- SHALL NOT ir ao `sessionStorage`, ao `localStorage`, à URL ou ao `state` do histórico;
- SHALL NOT ficar no `QueryCache` nem no `MutationCache` do TanStack Query, inclusive depois de sair da página. A lista SHALL ser relida pelo `GET /users`, e não preenchida com a resposta da ação;
- SHALL sumir com "Concluir", ao abrir outro painel e ao sair da página.

#### Scenario: Copiar link
- **WHEN** a pessoa aciona "Copiar link"
- **THEN** a área de transferência recebe o `link` da resposta, e o botão mostra "Copiado ✓"

#### Scenario: Concluir apaga o link
- **WHEN** a pessoa aciona "Concluir"
- **THEN** o painel fecha, e o texto do link não aparece em nenhum lugar da página

#### Scenario: Nenhum cache guarda o link
- **WHEN** o convite é criado, e a pessoa sai de `/users` para `/inventory`
- **THEN** nem o `QueryCache` nem o `MutationCache` do cliente guardam um valor que contenha o link

#### Scenario: Nada em storage nem na URL
- **WHEN** o painel do link está aberto
- **THEN** nem o `sessionStorage`, nem o `localStorage`, nem o endereço, nem o `history.state` contêm o link

#### Scenario: Voltar à página não mostra o link de novo
- **WHEN** a pessoa sai de `/users` com o painel do link aberto e volta a `/users`
- **THEN** a página mostra a lista sem nenhum painel aberto

