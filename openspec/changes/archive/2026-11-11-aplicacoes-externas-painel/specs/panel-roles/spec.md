## MODIFIED Requirements

### Requirement: Guarda de rota por papel
As rotas abaixo SHALL mostrar a tela de acesso negado, sem montar a página e sem chamar o servidor, quando o papel da sessão não está na coluna "papéis":

| rotas | papéis |
|---|---|
| `/users`, `/users/:id`, `/identity`, `/applications`, `/applications/new`, `/applications/:id`, `/applications/:id/edit` | Admin |
| `/agents/new`, `/agents/:id/edit`, `/mcp-servers/new`, `/mcp-servers/:id/edit`, `/knowledge-bases/new`, `/knowledge-bases/:id/edit`, `/channels/new`, `/channels/:id/edit`, `/channels/:id/sessions/:sessionId` | Admin, Operador |

As outras rotas autenticadas SHALL abrir para os três papéis. A guarda SHALL NOT substituir a recusa do servidor: com o papel do id token na coluna, a página carrega, e o `403` do servidor segue "O 403 na carga e na ação".

#### Scenario: Operador em /users
- **WHEN** a sessão de um Operador abre `/users`
- **THEN** a página mostra "Você não tem acesso a esta página.", e nenhum `GET /users` é feito

#### Scenario: Leitor numa sessão de canal
- **WHEN** a sessão de um Leitor abre `/channels/c1/sessions/s1`
- **THEN** a página mostra a tela de acesso negado, e nenhum `GET /channels/c1/sessions` nem `GET /sessions/s1/messages` é feito

#### Scenario: Operador cria agente
- **WHEN** a sessão de um Operador abre `/agents/new`
- **THEN** o formulário do agente aparece

#### Scenario: Admin em Identidade
- **WHEN** a sessão de um Admin abre `/identity`
- **THEN** a página Identidade carrega

#### Scenario: Operador e Leitor numa aplicação externa
- **WHEN** a sessão de um Operador abre `/applications/a1`, e depois a de um Leitor abre `/applications/new`
- **THEN** as duas mostram "Você não tem acesso a esta página.", e nenhuma chamada a `/applications/` é feita

#### Scenario: Admin nas aplicações externas
- **WHEN** a sessão de um Admin abre `/applications`
- **THEN** a lista de aplicações carrega
