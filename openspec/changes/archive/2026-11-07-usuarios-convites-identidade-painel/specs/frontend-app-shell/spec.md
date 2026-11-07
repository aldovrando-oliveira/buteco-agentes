## ADDED Requirements

### Requirement: Seção Acesso na navegação
A navegação da barra lateral SHALL ter, depois do item "Insights", o rótulo de seção "Acesso", e, abaixo dele, os itens "Usuários" (`/users`) e "Identidade" (`/identity`), nesta ordem.

- O rótulo SHALL seguir o `.navsec` do protótipo: maiúsculas e menor que os itens, no tom `dimmed` do `SectionLabel`, que é o tom que passa no contraste exigido pela `frontend-visual-theme`.
- O rótulo SHALL NOT ser link nem item de navegação, e SHALL NOT receber o estado ativo.
- Os dois itens SHALL seguir o requisito "Navegação com ícone e estado ativo por grupo de rotas": cada um com ícone decorativo e rótulo textual, ativo enquanto a rota atual começar pelo caminho dele.
- A seção SHALL NOT ter o item "Aplicações externas", que só chega com a M2.

#### Scenario: A seção e os dois itens
- **WHEN** a pessoa visualiza a barra lateral
- **THEN** depois de "Insights" aparece o rótulo "Acesso" e, abaixo dele, os links "Usuários" e "Identidade"

#### Scenario: O rótulo não é link
- **WHEN** a pessoa percorre a navegação por teclado
- **THEN** o rótulo "Acesso" não recebe foco, e não há link com o nome "Acesso"

#### Scenario: Usuários ativo na página de Usuários
- **WHEN** a pessoa está em `/users`
- **THEN** o item "Usuários" aparece ativo, e "Identidade" não

#### Scenario: Sem Aplicações externas
- **WHEN** a pessoa visualiza a barra lateral
- **THEN** não há item "Aplicações externas"
