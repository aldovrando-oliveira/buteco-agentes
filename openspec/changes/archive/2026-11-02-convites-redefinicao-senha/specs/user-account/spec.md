## MODIFIED Requirements

### Requirement: Trocar a própria senha, com a senha atual
`POST /account/password` com `{ senhaAtual, novaSenha }` SHALL conferir a senha atual como o login confere, e SHALL contar a senha atual ausente ou errada como falha no bloqueio por e-mail do usuário, respondendo `401`. Durante o bloqueio, SHALL responder `423` com `Retry-After`, sem conferir senha nenhuma. O acerto SHALL NOT zerar a contagem do bloqueio.

Com a senha atual certa e a nova dentro da política, SHALL gravar o hash Argon2id da nova, tirar o marcador de hash legado, trocar o `security_stamp`, apagar o link de redefinição de senha pendente e revogar todas as autorizações do usuário, numa mesma transação, respondendo `204`. Isso encerra todas as sessões da pessoa, inclusive a da requisição.

#### Scenario: Senha atual errada conta no bloqueio
- **WHEN** um usuário erra a senha atual cinco vezes seguidas e tenta a sexta vez com a senha atual certa
- **THEN** as cinco primeiras respondem `401`, e a sexta responde `423` com `Retry-After`, sem trocar a senha

#### Scenario: Troca bem-sucedida
- **WHEN** um usuário troca a senha com a atual certa e uma nova dentro da política
- **THEN** a resposta é `204`, o login com a senha antiga responde `401`, e o login com a nova responde `204`

#### Scenario: A troca encerra todas as sessões, inclusive a atual
- **WHEN** um usuário com duas sessões vivas troca a senha por uma delas
- **THEN** a renovação de cada uma das duas recebe `invalid_grant`, e `GET /account/sessions`, por um login novo, lista só a sessão nova

#### Scenario: Cookie de sessão em voo recusado depois da troca
- **WHEN** um usuário obtém o cookie da sessão do emissor e, antes de usá-lo no authorize, a senha dele é trocada
- **THEN** o authorize com esse cookie é tratado como sem sessão e redireciona para `/entrar`, sem emitir código

#### Scenario: A troca invalida o link de redefinição pendente
- **WHEN** um usuário com um link de redefinição pendente troca a própria senha
- **THEN** o link recebe a resposta de link inválido na consulta e na conclusão

### Requirement: Toda troca do carimbo de segurança revoga as sessões da pessoa
Toda operação que troca o `security_stamp` de um usuário (trocar a senha, desativar, redefinir as 2 etapas, concluir a redefinição de senha por link) SHALL revogar, na mesma transação, todas as autorizações do usuário no emissor. Se a transação falhar, nem o carimbo nem as autorizações SHALL mudar.

#### Scenario: Sessão morta pelo carimbo não aparece viva
- **WHEN** qualquer uma das quatro operações troca o carimbo de um usuário com sessões vivas
- **THEN** a lista de sessões vivas dele fica vazia, sem esperar a limpeza
