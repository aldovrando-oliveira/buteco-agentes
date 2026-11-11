# panel-roles Specification

## Purpose
Diz como o painel (`apps/frontend`) trata o papel da sessão (Admin, Operador e Leitor da #184):
um mapeamento só entre o claim `role` do id token e o campo `papel` das respostas, o papel
desconhecido valendo Leitor, a guarda de rota por papel (que não substitui a recusa do servidor),
a tela de acesso negado, o `403` na carga e na ação, e o que cada papel vê nas telas de domínio.
Introduzida pela change `papeis-no-painel` (#185; ver
`openspec/changes/archive/2026-11-09-papeis-no-painel/`).
## Requirements
### Requirement: Um mapeamento só entre o claim role e o campo papel
`apps/frontend` SHALL converter o papel por um módulo só (`src/auth/roles.ts`), com esta tabela, e nenhum outro arquivo SHALL comparar o claim `role` ou o campo `papel` com um literal:

| papel | claim `role` do id token | campo `papel` das respostas e dos corpos | rótulo |
|---|---|---|---|
| Admin | `admin` | `admin` | Admin |
| Operador | `operator` | `operador` | Operador |
| Leitor | `reader` | `leitor` | Leitor |

Valor fora da tabela, ausente ou de outro tipo SHALL virar papel desconhecido, e SHALL NOT virar nenhum dos três.

#### Scenario: Os três pares nos dois sentidos
- **WHEN** o módulo converte `operator` do claim e `operador` da resposta
- **THEN** os dois dão o papel Operador, com o rótulo "Operador", e o Operador convertido para o corpo da API dá `operador`

#### Scenario: Valor desconhecido
- **WHEN** o claim `role` é `superuser`, ou o campo `papel` é `operator` (o valor do claim no lugar do do fio)
- **THEN** o resultado é papel desconhecido, e não Admin, Operador nem Leitor

### Requirement: O papel da sessão vem do id token, e o desconhecido é Leitor
O papel da sessão SHALL ser o claim `role` do id token do registro da sessão, lido na hora como o `sub` (`panel-issuer-session`), sem campo novo no registro. Papel desconhecido ou sessão sem id token legível SHALL valer como **Leitor** para tudo o que a tela mostra, até a renovação trazer um id token com papel. Quando o registro muda, nesta aba ou noutra, o menu, os botões e as guardas de rota SHALL refletir o papel novo sem recarregar a página.

#### Scenario: Id token sem role
- **WHEN** a sessão tem um id token sem o claim `role`
- **THEN** a barra lateral não tem a seção "Acesso", e a lista de agentes não tem "Novo agente"

#### Scenario: A promoção chega sem recarregar
- **WHEN** a sessão de um Leitor está em `/agents`, e uma renovação grava um id token com `role` `operator`
- **THEN** "Novo agente" aparece sem recarregar a página

#### Scenario: O rebaixamento chega pela outra aba
- **WHEN** outra aba grava um id token com `role` `reader` no lugar de um com `admin`
- **THEN** esta aba tira a seção "Acesso" da barra lateral sem recarregar

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

### Requirement: Tela de acesso negado
A tela de acesso negado SHALL ser um componente só, no lugar do conteúdo da página e dentro da casca (menu e rodapé visíveis), como o quadro `AcessoNegado` (`estado=pagina`), com `role="alert"`:
- o título "Você não tem acesso a esta página.";
- o texto "Se o seu papel mudou há pouco, a mudança pode levar até 15 minutos para chegar a todo o painel.";
- o link "Ir para o Inventário", para `/inventory`.

O texto SHALL NOT nomear o papel da sessão.

#### Scenario: Os textos e o link
- **WHEN** a tela de acesso negado aparece
- **THEN** ela mostra o título, o texto do prazo e o link "Ir para o Inventário" para `/inventory`, e não contém "Operador", "Leitor" nem "Admin"

### Requirement: O 403 na carga e na ação
Quando a consulta principal de uma página responde `403`, a página SHALL mostrar a tela de acesso negado no lugar do conteúdo, e não o erro de carga dela. Quando outra consulta da página, ou uma ação, recebe `403`, o lugar onde a ação mostra a falha SHALL mostrar o aviso do quadro `AcessoNegado` (`estado=acao`), vermelho, com `role="alert"`: "Você não tem permissão para esta ação." e, abaixo, "Se o seu papel mudou há pouco, a mudança pode levar até 15 minutos para chegar a todo o painel.". O `403` que traz `title` de `problem+json` SHALL mostrar o `title` no aviso da ação, como já fazem os `403` "a si mesmo" de Editar usuário (`panel-user-detail-page`).

#### Scenario: Detalhe do agente com 403
- **WHEN** `GET /agents/a1` responde `403` sem corpo
- **THEN** `/agents/a1` mostra "Você não tem acesso a esta página.", e não "Não foi possível carregar o agente."

#### Scenario: Ação recusada pelo papel
- **WHEN** a sessão de um Operador aciona "Desativar" num agente, e `POST /agents/a1/deactivate` responde `403` sem corpo
- **THEN** a página mostra "Você não tem permissão para esta ação." com o texto do prazo, e o agente continua no estado lido

### Requirement: O que cada papel vê nas telas de domínio
O que o papel nunca pode fazer SHALL NOT aparecer: botão, link, aba ou barra de alterações. Desabilitar SHALL ficar para o que o papel pode fazer, mas o estado do objeto impede (como "Excluir" com a base ativa). O controle que mostra estado, como a caixa de um vínculo, SHALL continuar visível e sem edição para quem não pode mudá-lo.

O Admin e o Operador SHALL ver tudo o que esta tabela lista. O Leitor SHALL ver as páginas e os dados, e SHALL NOT ver:

| página | o que o Leitor não vê |
|---|---|
| `/agents` | "Novo agente" |
| `/agents/:id` | "Editar", "Ativar", "Desativar"; na aba Ferramentas, "Salvar vínculo", a barra de alterações e "Cadastrar servidor MCP"; na aba Conhecimento, "Vincular base", "Desvincular" e "Salvar bases de conhecimento"; na aba Delegação, "Salvar delegações" e a barra de alterações |
| `/agents/:id`, abas Ferramentas e Delegação | as caixas continuam visíveis, marcadas como o vínculo lido, e desabilitadas |
| `/mcp-servers` | "Novo servidor MCP" |
| `/mcp-servers/:id` | "Editar", "Ativar", "Desativar", "Testar conexão" |
| `/knowledge-bases` | "Nova base", "Criar a primeira base" |
| `/knowledge-bases/:id` | "Editar", "Ativar", "Desativar", "Excluir", "Sincronizar agora", "Tentar sincronizar agora", "Adicionar documento", e, em cada documento, "Atualizar", "Excluir" e "Reindexar documento" |
| `/channels` | "Novo canal" |
| `/channels/:id` | "Editar", "Ativar", "Desativar" e a aba Sessões (`inbox-channel-catalog-ui`) |

O Leitor SHALL continuar vendo, nessas páginas: as listas, os detalhes, "Ver tools" e o catálogo de tools, os documentos, as abas Histórico, Diagnóstico e Insights, o estado da origem sincronizada e o "Copiar" do webhook.

#### Scenario: Leitor no detalhe da base
- **WHEN** a sessão de um Leitor abre o detalhe de uma base sincronizada com um documento com falha
- **THEN** não há "Sincronizar agora", "Reindexar documento", "Editar", "Desativar" nem "Excluir", e o documento e o estado da sincronização aparecem

#### Scenario: Leitor nas abas do agente
- **WHEN** a sessão de um Leitor abre a aba Ferramentas de um agente com um servidor vinculado
- **THEN** a caixa do servidor aparece marcada e desabilitada, e não há "Salvar vínculo"

#### Scenario: Leitor no MCP
- **WHEN** a sessão de um Leitor abre o detalhe de um servidor MCP
- **THEN** não há "Testar conexão", e "Ver tools" continua

#### Scenario: Operador vê as escritas
- **WHEN** a sessão de um Operador abre o detalhe de uma base sincronizada
- **THEN** "Sincronizar agora", "Editar" e "Desativar" aparecem

