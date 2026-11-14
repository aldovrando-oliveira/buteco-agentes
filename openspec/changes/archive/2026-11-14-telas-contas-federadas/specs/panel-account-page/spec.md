## MODIFIED Requirements

### Requirement: Página Minha conta em /account
`apps/frontend` SHALL prover a página `/account`, dentro da casca e da exigência de sessão, como as outras rotas autenticadas. Ela SHALL ter o título "Minha conta", o subtítulo "Os seus dados, as formas de entrada, a sua senha, os códigos de recuperação e os navegadores em que você está conectado." e, nesta ordem, os cartões "Dados", "Formas de entrada" (quando aparece, `panel-federated-accounts`), "Senha", "Verificação em 2 etapas" e "Sessões" do quadro `MinhaConta` (`entrega=m3`).
- A entrada para a página SHALL ser o link do rodapé (`frontend-app-shell`). Nenhum item de menu SHALL levar a ela.
- Cada cartão SHALL carregar os próprios dados, e SHALL mostrar um indicador de carregamento enquanto espera.
- Um erro no `GET` de um cartão SHALL mostrar dentro dele um aviso com o texto do erro (D8 do design) e o botão "Tentar de novo", sem afetar os outros cartões.

#### Scenario: Sem sessão, /account vai ao login
- **WHEN** alguém sem sessão abre `/account`
- **THEN** o painel vai a `/entrar` com `voltarPara=/account`

#### Scenario: Os quatro cartões, na ordem
- **WHEN** a pessoa abre `/account` com a sessão válida, sem provedor configurado e sem conta conectada
- **THEN** a página mostra "Minha conta", o subtítulo "Os seus dados, as formas de entrada, a sua senha, os códigos de recuperação e os navegadores em que você está conectado." e os cartões "Dados", "Senha", "Verificação em 2 etapas" e "Sessões", nesta ordem

#### Scenario: Com provedor, os cinco cartões
- **WHEN** a lista dos provedores traz `google`
- **THEN** os cartões são "Dados", "Formas de entrada", "Senha", "Verificação em 2 etapas" e "Sessões", nesta ordem

#### Scenario: Erro num cartão não derruba os outros
- **WHEN** `GET /account/sessions` falha por rede, e as outras leituras respondem `200`
- **THEN** o cartão "Sessões" mostra o aviso com "Tentar de novo", e os outros três mostram os seus dados

#### Scenario: Tentar de novo relê o cartão
- **WHEN** o cartão mostra o aviso de erro, e a pessoa aciona "Tentar de novo"
- **THEN** o cartão chama o `GET` de novo, e mostra os dados quando ele responder `200`

### Requirement: Troca da própria senha
O cartão "Senha" SHALL começar fechado, com "Você entra com `<e-mail>` e a sua senha." e o botão "Trocar senha". **Para a pessoa sem senha** (`GET /account` com `formasDeEntrada` sem `"senha"`), o cartão SHALL mostrar só "Você entra só pelo Google. Para ter também uma senha, peça a um administrador o link para criar uma.", sem "Trocar senha" e sem o formulário; com a consulta pendente ou em erro, o cartão SHALL ser o de quem tem senha. O botão SHALL abrir o formulário com:
- "Senha atual", "Nova senha" (com a ajuda "De 12 a 128 caracteres.") e "Repita a nova senha", os três pelo `PasswordField` (`frontend-password-field`);
- o aviso "Ao trocar, você sai de todos os navegadores, inclusive deste, e entra de novo com a nova senha.";
- "Trocar senha" e "Cancelar". "Cancelar" SHALL fechar o formulário e limpar os campos.

**Antes de enviar**, o painel SHALL conferir:
- a nova senha com a política (de 12 a 128 caracteres, contados sem `trim`). Fora dela: "A senha precisa ter de 12 a 128 caracteres." no campo "Nova senha", sem envio;
- a repetição igual à nova. Diferente: "As senhas não conferem." em "Repita a nova senha", sem envio.

**O `POST /account/password` com `{ senhaAtual, novaSenha }`:**
- `204`: o painel SHALL gravar o aviso de senha alterada (`operator-login-ui`) e, em seguida, chamar o mesmo logout do "Sair" (`panel-issuer-session`), com o botão desabilitado e em "Saindo…" até a navegação;
- `401`: o `title` do servidor ("Senha atual inválida.") SHALL ir no campo "Senha atual", com `aria-invalid="true"`, sem deslogar;
- `400`: o `title` SHALL ir no campo "Nova senha";
- `423`: o aviso de bloqueio desta capacidade, acima dos campos.

#### Scenario: Senha nova curta não envia
- **WHEN** a nova senha tem 11 caracteres, e a pessoa aciona "Trocar senha"
- **THEN** "A senha precisa ter de 12 a 128 caracteres." aparece em "Nova senha", e nenhum pedido sai

#### Scenario: Repetição diferente não envia
- **WHEN** a repetição difere da nova senha
- **THEN** "As senhas não conferem." aparece em "Repita a nova senha", e nenhum pedido sai

#### Scenario: Senha atual errada fica no campo, sem deslogar
- **WHEN** `POST /account/password` responde `401` com `application/problem+json` e `title` "Senha atual inválida."
- **THEN** "Senha atual inválida." aparece no campo "Senha atual", com `aria-invalid="true"`, o pedido saiu uma vez só, a sessão continua, e o painel não sai de `/account`

#### Scenario: Depois do 204, o aviso e o logout
- **WHEN** `POST /account/password` responde `204`
- **THEN** a chave `buteco.aviso.senha-alterada` é gravada no `sessionStorage`, e depois disso o logout do emissor é chamado

#### Scenario: Cancelar fecha e limpa
- **WHEN** a pessoa digita nos campos e aciona "Cancelar"
- **THEN** o cartão volta ao estado fechado, e reabrir o formulário mostra os campos vazios

#### Scenario: A pessoa sem senha
- **WHEN** `GET /account` traz `formasDeEntrada: ["google"]`
- **THEN** o cartão "Senha" mostra "Você entra só pelo Google. Para ter também uma senha, peça a um administrador o link para criar uma.", e não mostra "Trocar senha"
