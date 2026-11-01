## MODIFIED Requirements

### Requirement: Controle de tema no rodapé da barra lateral
O sistema SHALL posicionar o controle de alternância de esquema de cor no
rodapé da barra lateral, preservando o comportamento já especificado em
`frontend-scaffold`: alternância manual, persistência entre sessões e rótulo
que descreve o esquema efetivo.

O rodapé SHALL ser uma linha só, separada da navegação por uma borda superior,
como no protótipo de autenticação (quadro "Editar usuário"), com:

- à esquerda, um avatar circular de 30 px, decorativo (oculto da tecnologia
  assistiva), com fundo azul-claro e texto azul-escuro, que traz as iniciais do
  nome: a primeira letra da primeira e da última palavra, ou só a primeira letra
  quando o nome tem uma palavra. Sem nome, a primeira letra do e-mail;
- no meio, duas linhas: o nome do id token (`panel-issuer-session`), com peso 500 e
  cortado com reticências quando não couber, e, abaixo, em texto menor e cinza, o
  e-mail do id token. Sem nome no id token, o e-mail SHALL ir na primeira linha, e a
  segunda SHALL ficar vazia;
- à direita, dois botões só de ícone, nesta ordem: o controle de tema (lua no
  claro, sol no escuro, com o comportamento e o rótulo acessível de
  `frontend-scaffold`) e o "Sair" (ícone de saída, com `aria-label` e `title`
  "Sair"), que encerra a sessão. Enquanto espera o lock da renovação, o "Sair"
  SHALL ficar desabilitado, com um indicador de carregamento no lugar do ícone e o
  `aria-label` "Saindo…".

O rodapé SHALL NOT exibir dado de identidade que não venha do id token. A #184
troca a segunda linha pelo papel do usuário, quando os papéis existirem, e a #183
só revisa o rodapé.

#### Scenario: O controle de tema vive no rodapé
- **WHEN** o operador visualiza a barra lateral
- **THEN** o controle de alternância de esquema de cor aparece no rodapé da
  barra, abaixo da navegação

#### Scenario: A alternância continua funcionando após a mudança de lugar
- **WHEN** o operador aciona o controle de tema a partir do rodapé e recarrega
  a aplicação
- **THEN** a interface é renderizada no esquema escolhido anteriormente

#### Scenario: Nome com duas palavras
- **WHEN** a sessão tem um id token com `name` "Ana Souza" e `email` "ana@exemplo.test"
- **THEN** o rodapé mostra o avatar com "AS", "Ana Souza" na primeira linha e "ana@exemplo.test" na segunda, e depois o botão de tema e o "Sair", nesta ordem

#### Scenario: Nome de uma palavra
- **WHEN** o id token da sessão tem `name` "admin"
- **THEN** o avatar mostra "A", e o rodapé mostra "admin" e, abaixo, o e-mail

#### Scenario: Sem nome no id token
- **WHEN** o id token da sessão tem `email` "bia@exemplo.test" e não tem `name`
- **THEN** o avatar mostra "B", "bia@exemplo.test" vai na primeira linha, e não há segunda linha

#### Scenario: Saindo enquanto espera o lock
- **WHEN** o usuário aciona o "Sair" enquanto uma renovação em outra aba segura o lock
- **THEN** o botão fica desabilitado, com `aria-label` "Saindo…" e um indicador de carregamento no lugar do ícone, e a navegação ao logout do emissor só acontece depois de o lock ser solto
