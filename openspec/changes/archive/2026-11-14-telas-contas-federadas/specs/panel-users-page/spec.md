## MODIFIED Requirements

### Requirement: Página Usuários em /users
`apps/frontend` SHALL prover a página `/users` dentro da casca, com a mesma exigência de sessão das outras rotas autenticadas, só para o papel Admin (guarda de rota de `panel-roles`). A página SHALL ter:
- o título "Usuários";
- o subtítulo "Quem pode entrar no painel. Todo usuário existe no Buteco; os provedores federados são formas adicionais de provar quem ele é.";
- o cartão "Usuários" do quadro `Usuarios` (`entrega=m3`);
- abaixo dele, os cartões "Acesso só por convite" e "Senha e recuperação", com os textos do quadro na versão `m3`:
  - "Acesso só por convite": "Ninguém entra sem ter sido convidado aqui, mesmo autenticado pelo Google. Não existe entrada automática por domínio.";
  - "Senha e recuperação": "Esqueceu a senha: um Admin gera um link de redefinição em Editar usuário; quem entra só pelo Google recebe o mesmo link para criar uma. A verificação em 2 etapas é obrigatória para todos os papéis, inclusive para quem entra pelo Google: o Buteco pede o próprio código.".

O cartão "Usuários" SHALL ler `GET /users` e SHALL mostrar:
- "Carregando os usuários…" (`role="status"`) enquanto espera;
- com `403`, a tela de acesso negado (`panel-roles`);
- em qualquer outra falha da leitura, "Não foi possível carregar os usuários agora." com o botão "Tentar de novo" (`role="alert"`).

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

#### Scenario: O cartão de senha e recuperação
- **WHEN** a página de Usuários aparece
- **THEN** o cartão "Senha e recuperação" mostra "Esqueceu a senha: um Admin gera um link de redefinição em Editar usuário; quem entra só pelo Google recebe o mesmo link para criar uma. A verificação em 2 etapas é obrigatória para todos os papéis, inclusive para quem entra pelo Google: o Buteco pede o próprio código.", e não contém "ligada por cada pessoa" nem "feita no próprio provedor"
- **AND** o subtítulo é "Quem pode entrar no painel. Todo usuário existe no Buteco; os provedores federados são formas adicionais de provar quem ele é.", e o cartão "Acesso só por convite" mostra "Ninguém entra sem ter sido convidado aqui, mesmo autenticado pelo Google. Não existe entrada automática por domínio."

#### Scenario: Admin rebaixado recebe 403
- **WHEN** a sessão ainda traz `role` `admin`, e o `GET /users` responde `403`
- **THEN** a página mostra "Você não tem acesso a esta página.", e não "Não foi possível carregar os usuários agora."

### Requirement: Colunas da lista de Usuários
A tabela SHALL ter as colunas Usuário, Formas de entrada, Papel, Verificação em 2 etapas, Estado, Último acesso e Ações, nesta ordem. As linhas SHALL vir na ordem da resposta, sem reordenação no cliente.

| coluna | o que mostra |
|---|---|
| Usuário | o `nome` em negrito e, abaixo, o `email` em fonte monoespaçada |
| Formas de entrada | um chip "Senha" para cada `senha` em `formasDeEntrada`, e um chip "Google" para cada `google`, os dois no mesmo tom; "—" quando a lista é vazia; um chip neutro, em contorno, com o valor cru para qualquer outro valor |
| Papel | o rótulo do `papel` (`panel-roles`): "Admin", "Operador" ou "Leitor", também no convidado; "—" para valor desconhecido |
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
- **WHEN** a lista traz `{ nome: "Rafael Duarte", email: "rafael@exemplo.test", estado: "ativo", papel: "operador", formasDeEntrada: ["senha"], duasEtapas: true, ultimoAcesso: <ontem às 18:40> }`
- **THEN** a linha mostra "Rafael Duarte", "rafael@exemplo.test", o chip "Senha", "Operador", "Ativa", "Ativo" e "ontem, 18:40"

#### Scenario: O papel do convidado
- **WHEN** a lista traz um convidado com `papel: "leitor"`
- **THEN** a coluna Papel mostra "Leitor"

#### Scenario: Papel desconhecido sai neutro
- **WHEN** a lista traz um usuário com `papel: "superusuario"`
- **THEN** a coluna Papel mostra "—", e não "Admin", "Operador" nem "Leitor"

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

#### Scenario: O chip Google no tom da Senha
- **WHEN** a lista traz um ativo com `formasDeEntrada: ["senha", "google"]`
- **THEN** a linha mostra os chips "Senha" e "Google", no mesmo tom, e nenhum chip "google"

#### Scenario: Outro tipo de provedor continua neutro
- **WHEN** a lista traz um ativo com `formasDeEntrada: ["keycloak"]`
- **THEN** a linha mostra o chip neutro, em contorno, "keycloak"

### Requirement: Senha de quem chama
Os formulários de convite e de novo link SHALL ter o campo da confirmação de quem chama. Esta é a regra de todos os formulários que confirmam quem chama: o convite e o novo link (aqui), a redefinição de senha e a promoção a Admin (`panel-user-detail-page`), a nova aplicação e o novo segredo (`panel-external-applications`) e o conectar (`panel-federated-accounts`).

**O modo** SHALL vir de `GET /account` (a mesma consulta de Minha conta): **código** só quando a resposta chegou e `formasDeEntrada` não tem `"senha"`; **senha** em qualquer outro caso, inclusive com a consulta pendente ou em erro.

| | modo senha | modo código |
|---|---|---|
| rótulo | "Sua senha" | "Código do app autenticador" |
| entrada | o `PasswordField`, `autocomplete="current-password"` | texto, `inputmode="numeric"`, `autocomplete="one-time-code"` |
| ajuda | a do formulário: "Para convidar alguém, confirme que é você." ou "Para gerar um link, confirme que é você." | "Você entra só pelo Google: confirme com o código de 6 dígitos do app. O código de recuperação não vale aqui." |
| vazio | "Informe a sua senha." | "Informe o código." |
| corpo | `senha` | `codigo` |

O corpo SHALL levar só o campo do modo: `senha` no modo senha, `codigo` no modo código, nunca os dois. **No modo senha, um `401` com o `title` "Código inválido."** (a conta não carregou, e quem chama não tem senha) SHALL reler `GET /account`; com a resposta, o campo SHALL passar ao modo código. **A troca de modo SHALL chegar limpa:** o campo do código SHALL aparecer vazio e sem erro, com a ajuda do modo código; o "Código inválido." da tentativa com a senha não fica no campo do código, porque a pessoa digitou uma senha.

| caso | a tela |
|---|---|
| campo vazio no envio | o texto do vazio do modo no campo, com `aria-invalid`, sem chamar a rota |
| `401` | o `title` da resposta ("Senha inválida." ou "Código inválido.") no campo, com `aria-invalid`. A pessoa SHALL continuar na página, com a sessão intacta, e a rota SHALL ter sido chamada uma vez só |
| `423` | acima do campo, o aviso amarelo "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por N minutos.", com N do `Retry-After` arredondado para cima ("alguns minutos" sem o cabeçalho legível), e o botão de envio desabilitado até passar o `Retry-After` |
| `429`, `503`, rede | o aviso de falha do formulário, com os textos do `/entrar`, e o botão desabilitado até o `Retry-After` quando houver |

A senha ou o código SHALL ficar só no estado do formulário, e SHALL ser apagado ao fechar o formulário, no sucesso, depois do `401` e ao sair da página.

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

#### Scenario: Sem senha, o campo é o do código
- **WHEN** `GET /account` traz `formasDeEntrada: ["google"]`, e o Admin abre o formulário de convite
- **THEN** o campo é "Código do app autenticador", com `autocomplete="one-time-code"` e a ajuda "Você entra só pelo Google: confirme com o código de 6 dígitos do app. O código de recuperação não vale aqui."

#### Scenario: O código vai como codigo, sem a senha
- **WHEN** o Admin sem senha preenche o convite, digita `123456` e aciona "Gerar convite"
- **THEN** o corpo do `POST /users/invitations` tem `codigo: "123456"` e não tem `senha`

#### Scenario: Código errado fica no campo
- **WHEN** no modo código, `POST /users/invitations` responde `401` com `title` "Código inválido."
- **THEN** o campo mostra "Código inválido.", com `aria-invalid="true"`, e fica vazio

#### Scenario: Código vazio não é enviado
- **WHEN** no modo código, o Admin aciona "Gerar convite" com o campo vazio
- **THEN** o campo mostra "Informe o código.", e nenhuma chamada a `POST /users/invitations` acontece

#### Scenario: Com a conta em erro, o modo é a senha
- **WHEN** `GET /account` falha por rede, e o Admin abre o formulário de convite
- **THEN** o campo é "Sua senha"

#### Scenario: O código inválido no modo senha relê a conta
- **WHEN** `GET /account` falhou, o Admin envia "Sua senha", `POST /users/invitations` responde `401` "Código inválido.", e a releitura de `GET /account` traz `formasDeEntrada: ["google"]`
- **THEN** `GET /account` foi chamado de novo, e o campo passa a "Código do app autenticador"

#### Scenario: A troca para o código chega limpa
- **WHEN** `GET /account` falhou, o Admin envia "Sua senha", `POST /users/invitations` responde `401` "Código inválido.", e a releitura de `GET /account` traz `formasDeEntrada: ["google"]`
- **THEN** o campo "Código do app autenticador" está vazio, sem `aria-invalid` e sem "Código inválido.", com a ajuda "Você entra só pelo Google: confirme com o código de 6 dígitos do app. O código de recuperação não vale aqui."
