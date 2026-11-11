## MODIFIED Requirements

### Requirement: Seção Acesso na navegação
Para a sessão de papel Admin (`panel-roles`), a navegação da barra lateral SHALL ter, depois do item "Insights", o rótulo de seção "Acesso", e, abaixo dele, os itens "Usuários" (`/users`), "Aplicações externas" (`/applications`) e "Identidade" (`/identity`), nesta ordem. Para o Operador, o Leitor e o papel desconhecido, a navegação SHALL NOT ter o rótulo nem os três itens.

- O rótulo SHALL seguir o `.navsec` do protótipo: maiúsculas e menor que os itens, no tom `dimmed` do `SectionLabel`, que é o tom que passa no contraste exigido pela `frontend-visual-theme`.
- O rótulo SHALL NOT ser link nem item de navegação, e SHALL NOT receber o estado ativo.
- Os três itens SHALL seguir o requisito "Navegação com ícone e estado ativo por grupo de rotas": cada um com ícone decorativo e rótulo textual, ativo enquanto a rota atual começar pelo caminho dele. O ícone de "Aplicações externas" SHALL ser o plugue (`Plug`) da biblioteca de ícones.

#### Scenario: A seção e os três itens
- **WHEN** a sessão de um Admin visualiza a barra lateral
- **THEN** depois de "Insights" aparece o rótulo "Acesso" e, abaixo dele, os links "Usuários", "Aplicações externas" e "Identidade", nesta ordem

#### Scenario: Operador e Leitor sem a seção
- **WHEN** a sessão de um Operador, e depois a de um Leitor, visualiza a barra lateral
- **THEN** não há o rótulo "Acesso" nem os links "Usuários", "Aplicações externas" e "Identidade", e os itens de "Inventário" a "Insights" continuam

#### Scenario: O rótulo não é link
- **WHEN** a pessoa percorre a navegação por teclado
- **THEN** o rótulo "Acesso" não recebe foco, e não há link com o nome "Acesso"

#### Scenario: Usuários ativo na página de Usuários
- **WHEN** a sessão de um Admin está em `/users`
- **THEN** o item "Usuários" aparece ativo, e "Aplicações externas" e "Identidade" não

#### Scenario: Aplicações externas ativo no detalhe de uma aplicação
- **WHEN** a sessão de um Admin está em `/applications/a1`
- **THEN** o item "Aplicações externas" aparece ativo, com o link para `/applications`
