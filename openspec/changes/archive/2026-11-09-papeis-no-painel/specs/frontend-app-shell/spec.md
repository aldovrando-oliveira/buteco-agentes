## MODIFIED Requirements

### Requirement: Controle de tema no rodapé da barra lateral
O sistema SHALL posicionar o controle de alternância de esquema de cor no
rodapé da barra lateral, preservando o comportamento já especificado em
`frontend-scaffold`: alternância manual, persistência entre sessões e rótulo
que descreve o esquema efetivo.

O rodapé SHALL ser uma linha só, separada da navegação por uma borda superior,
como no protótipo de autenticação (quadros da entrega `m2`), com:

- à esquerda, um avatar circular de 30 px, decorativo (oculto da tecnologia
  assistiva), com fundo azul-claro e texto azul-escuro, que traz as iniciais do
  nome: a primeira letra da primeira e da última palavra, ou só a primeira letra
  quando o nome tem uma palavra. Sem nome, a primeira letra do e-mail;
- no meio, duas linhas: o nome do id token (`panel-issuer-session`), com peso 500 e
  cortado com reticências quando não couber, e, abaixo, em texto menor e cinza, o
  rótulo do papel do id token ("Admin", "Operador" ou "Leitor", `panel-roles`). Sem
  nome no id token, o e-mail SHALL ir na primeira linha. Com papel desconhecido, a
  segunda linha SHALL ficar vazia. O e-mail SHALL NOT aparecer na segunda linha. As
  duas linhas SHALL formar um link só para `/account`
  (Minha conta, `panel-account-page`), com `title` "Minha conta", sem aparência de
  link (sem sublinhado e sem a cor de link) e com o foco visível. O link SHALL levar
  `aria-current="page"` quando a rota atual for `/account` ou começar com `/account/`,
  e SHALL NOT levá-lo nas outras rotas. O avatar e os dois botões SHALL ficar fora do
  link. Nenhum item de menu SHALL levar a Minha conta;
- à direita, dois botões só de ícone, nesta ordem: o controle de tema (lua no
  claro, sol no escuro, com o comportamento e o rótulo acessível de
  `frontend-scaffold`) e o "Sair" (ícone de saída, com `aria-label` e `title`
  "Sair"), que encerra a sessão. Enquanto espera o lock da renovação, o "Sair"
  SHALL ficar desabilitado, com um indicador de carregamento no lugar do ícone e o
  `aria-label` "Saindo…".

O rodapé SHALL NOT exibir dado de identidade que não venha do id token.

#### Scenario: O controle de tema vive no rodapé
- **WHEN** o operador visualiza a barra lateral
- **THEN** o controle de alternância de esquema de cor aparece no rodapé da
  barra, abaixo da navegação

#### Scenario: A alternância continua funcionando após a mudança de lugar
- **WHEN** o operador aciona o controle de tema a partir do rodapé e recarrega
  a aplicação
- **THEN** a interface é renderizada no esquema escolhido anteriormente

#### Scenario: Nome com duas palavras
- **WHEN** a sessão tem um id token com `name` "Ana Souza", `email` "ana@exemplo.test" e `role` `admin`
- **THEN** o rodapé mostra o avatar com "AS", "Ana Souza" na primeira linha e "Admin" na segunda, e depois o botão de tema e o "Sair", nesta ordem

#### Scenario: O papel na segunda linha
- **WHEN** o id token da sessão tem `name` "Rafael Duarte" e `role` `reader`
- **THEN** a segunda linha é "Leitor", e o e-mail não aparece no rodapé

#### Scenario: Nome de uma palavra
- **WHEN** o id token da sessão tem `name` "admin" e `role` `operator`
- **THEN** o avatar mostra "A", e o rodapé mostra "admin" e, abaixo, "Operador"

#### Scenario: Sem nome no id token
- **WHEN** o id token da sessão tem `email` "bia@exemplo.test" e `role` `admin`, e não tem `name`
- **THEN** o avatar mostra "B", "bia@exemplo.test" vai na primeira linha, e "Admin" na segunda

#### Scenario: Papel desconhecido
- **WHEN** o id token da sessão tem `name` "Ana Souza" e não tem `role`
- **THEN** o rodapé mostra "Ana Souza" e não tem segunda linha

#### Scenario: Saindo enquanto espera o lock
- **WHEN** o usuário aciona o "Sair" enquanto uma renovação em outra aba segura o lock
- **THEN** o botão fica desabilitado, com `aria-label` "Saindo…" e um indicador de carregamento no lugar do ícone, e a navegação ao logout do emissor só acontece depois de o lock ser solto

#### Scenario: O nome no rodapé leva a Minha conta
- **WHEN** o usuário, em `/agents`, aciona o nome no rodapé
- **THEN** o painel vai a `/account`, e o link não tem `aria-current`

#### Scenario: Em Minha conta, o link marca a página atual
- **WHEN** a rota atual é `/account`
- **THEN** o link do rodapé tem `aria-current="page"`, e o menu da barra lateral não tem item marcado como ativo para Minha conta

#### Scenario: O avatar e os botões ficam fora do link
- **WHEN** o rodapé é renderizado
- **THEN** o avatar, o botão de tema e o "Sair" não estão dentro do link para `/account`, e acionar o tema ou o "Sair" não navega para `/account`

### Requirement: Seção Acesso na navegação
Para a sessão de papel Admin (`panel-roles`), a navegação da barra lateral SHALL ter, depois do item "Insights", o rótulo de seção "Acesso", e, abaixo dele, os itens "Usuários" (`/users`) e "Identidade" (`/identity`), nesta ordem. Para o Operador, o Leitor e o papel desconhecido, a navegação SHALL NOT ter o rótulo nem os dois itens.

- O rótulo SHALL seguir o `.navsec` do protótipo: maiúsculas e menor que os itens, no tom `dimmed` do `SectionLabel`, que é o tom que passa no contraste exigido pela `frontend-visual-theme`.
- O rótulo SHALL NOT ser link nem item de navegação, e SHALL NOT receber o estado ativo.
- Os dois itens SHALL seguir o requisito "Navegação com ícone e estado ativo por grupo de rotas": cada um com ícone decorativo e rótulo textual, ativo enquanto a rota atual começar pelo caminho dele.
- A seção SHALL NOT ter o item "Aplicações externas", que chega com a #188.

#### Scenario: A seção e os dois itens
- **WHEN** a sessão de um Admin visualiza a barra lateral
- **THEN** depois de "Insights" aparece o rótulo "Acesso" e, abaixo dele, os links "Usuários" e "Identidade"

#### Scenario: Operador e Leitor sem a seção
- **WHEN** a sessão de um Operador, e depois a de um Leitor, visualiza a barra lateral
- **THEN** não há o rótulo "Acesso" nem os links "Usuários" e "Identidade", e os itens de "Inventário" a "Insights" continuam

#### Scenario: O rótulo não é link
- **WHEN** a pessoa percorre a navegação por teclado
- **THEN** o rótulo "Acesso" não recebe foco, e não há link com o nome "Acesso"

#### Scenario: Usuários ativo na página de Usuários
- **WHEN** a sessão de um Admin está em `/users`
- **THEN** o item "Usuários" aparece ativo, e "Identidade" não

#### Scenario: Sem Aplicações externas
- **WHEN** a sessão de um Admin visualiza a barra lateral
- **THEN** não há item "Aplicações externas"
