## MODIFIED Requirements

### Requirement: Página Usuários em /users
`apps/frontend` SHALL prover a página `/users` dentro da casca, com a mesma exigência de sessão das outras rotas autenticadas, só para o papel Admin (guarda de rota de `panel-roles`). A página SHALL ter:
- o título "Usuários";
- o subtítulo "Quem pode entrar no painel. O acesso é só por convite.";
- o cartão "Usuários" do quadro `Usuarios` (`entrega=m2`);
- abaixo dele, os cartões "Acesso só por convite" e "Senha e recuperação", com os textos do quadro na versão `m2`:
  - "Acesso só por convite": "Ninguém entra sem ter sido convidado aqui. Não existe cadastro aberto nem entrada automática.";
  - "Senha e recuperação": "Esqueceu a senha: um Admin gera um link de redefinição em Editar usuário. A verificação em 2 etapas é obrigatória para todos os papéis: cada pessoa cadastra o app autenticador no primeiro login.".

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
- **THEN** o cartão "Senha e recuperação" mostra "Esqueceu a senha: um Admin gera um link de redefinição em Editar usuário. A verificação em 2 etapas é obrigatória para todos os papéis: cada pessoa cadastra o app autenticador no primeiro login.", e não contém "ligada por cada pessoa"

#### Scenario: Admin rebaixado recebe 403
- **WHEN** a sessão ainda traz `role` `admin`, e o `GET /users` responde `403`
- **THEN** a página mostra "Você não tem acesso a esta página.", e não "Não foi possível carregar os usuários agora."

### Requirement: Colunas da lista de Usuários
A tabela SHALL ter as colunas Usuário, Formas de entrada, Papel, Verificação em 2 etapas, Estado, Último acesso e Ações, nesta ordem. As linhas SHALL vir na ordem da resposta, sem reordenação no cliente.

| coluna | o que mostra |
|---|---|
| Usuário | o `nome` em negrito e, abaixo, o `email` em fonte monoespaçada |
| Formas de entrada | um chip "Senha" para cada `senha` em `formasDeEntrada`; "—" quando a lista é vazia; um chip neutro com o valor cru para qualquer outro valor |
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

### Requirement: Convidar usuário
"Convidar usuário" SHALL abrir, dentro do cartão, o formulário "Convidar usuário" do quadro `Usuarios` (`entrega=m2`), com:
- os campos Nome, E-mail, Papel e "Sua senha" (a senha de quem chama);
- no Papel, as opções "Admin", "Operador" e "Leitor", **sem valor inicial**, com o texto "Escolha o papel" enquanto nada for escolhido;
- abaixo do Papel, sem escolha, "O que a pessoa pode fazer no painel. Dá para mudar depois em Editar usuário."; com escolha, a descrição do papel:
  - Admin: "Tudo, inclusive usuários e Identidade.";
  - Operador: "Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade.";
  - Leitor: "Vê agentes, MCP, conhecimento, canais e Insights. Não altera nada, não vê o histórico de conversas nem usa os conectores.";
- o texto "O Buteco gera um link de uso único, válido por 72 horas. Você copia o link e envia para a pessoa pelo canal que preferir.";
- os botões "Gerar convite" (primário) e "Cancelar".

"Gerar convite" sem papel escolhido SHALL mostrar "Escolha o papel." no campo Papel, com `aria-invalid`, e SHALL NOT chamar a rota. Com papel, SHALL chamar `POST /users/invitations` com `{ nome, email, papel, senha }`, com o `papel` no valor do fio (`admin`, `operador` ou `leitor`).

| resposta | a tela |
|---|---|
| `201 { usuario, link, expiraEm }` | fecha o formulário, mostra o painel do link com o título "Convite criado para {nome}" e, abaixo dele, "{e-mail} · {rótulo do `usuario.papel`}", relê o `GET /users` e marca a linha de `usuario.id` enquanto o painel está aberto |
| `400` | o `errors.nome`, o `errors.email` e o `errors.papel` da resposta, cada um no seu campo, com `aria-invalid` |
| `409` | o `title` da resposta ("Já existe um usuário com este e-mail.") no campo E-mail, com `aria-invalid` |
| `403` | o aviso de ação recusada (`panel-roles`) |
| `401`, `423`, vazio, `429`, `503`, rede | como em "Senha de quem chama" |

"Cancelar" SHALL fechar o formulário sem chamar a rota.

O painel do link do requisito "Gerar novo link de convite" SHALL mostrar também, abaixo do título, "{e-mail} · {rótulo do papel da linha}", como o quadro `Usuarios` (`link-renovado`).

#### Scenario: Convite criado
- **WHEN** a pessoa preenche "Marcos Lima", "marcos@exemplo.test", o papel "Operador" e a senha certa, e aciona "Gerar convite", e a rota responde `201`
- **THEN** o pedido leva `papel: "operador"`, o painel mostra "Convite criado para Marcos Lima", "marcos@exemplo.test · Operador", o link e "Vale até {expiraEm}"
- **AND** a lista é relida, e a linha de Marcos Lima aparece marcada

#### Scenario: Sem papel escolhido
- **WHEN** a pessoa preenche nome, e-mail e senha, não escolhe o papel, e aciona "Gerar convite"
- **THEN** o campo Papel mostra "Escolha o papel.", com `aria-invalid="true"`, e nenhum `POST /users/invitations` é feito

#### Scenario: A descrição acompanha a escolha
- **WHEN** o formulário abre, e depois a pessoa escolhe "Leitor"
- **THEN** abaixo do Papel aparece primeiro "O que a pessoa pode fazer no painel. Dá para mudar depois em Editar usuário." e depois "Vê agentes, MCP, conhecimento, canais e Insights. Não altera nada, não vê o histórico de conversas nem usa os conectores."

#### Scenario: E-mail em uso
- **WHEN** a rota responde `409` "Já existe um usuário com este e-mail."
- **THEN** o campo E-mail mostra o texto, com `aria-invalid="true"`, e o formulário continua aberto

#### Scenario: Campos inválidos
- **WHEN** a rota responde `400` com `errors: { email: ["Informe um e-mail válido."], papel: ["O papel precisa ser admin, operador ou leitor."] }`
- **THEN** o campo E-mail mostra "Informe um e-mail válido.", e o campo Papel mostra "O papel precisa ser admin, operador ou leitor."

#### Scenario: A linha nova fica na ordem do servidor
- **WHEN** o convite de "Marcos Lima" é criado, e a lista relida traz Marcos entre "Joana" e "Paulo"
- **THEN** a linha de Marcos aparece entre as duas, marcada, e deixa de estar marcada depois de "Concluir"
