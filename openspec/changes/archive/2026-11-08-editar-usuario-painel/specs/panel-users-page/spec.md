## MODIFIED Requirements

### Requirement: Ações da linha sem destino inexistente
A coluna Ações SHALL mostrar, na linha do convidado, "Gerar novo link" e "Cancelar convite", com `aria-label` "Gerar novo link para {nome}" e "Cancelar convite de {nome}".

A linha do usuário ativo e a do desativado SHALL mostrar "Editar", como no quadro `Usuarios`: um link para `/users/{id}`, com o `aria-label` "Editar {nome}" e o id codificado para a URL. A linha do convidado SHALL NOT ter "Editar" nem link para `/users/{id}`: o detalhe de um convidado volta para `/users` (`panel-user-detail-page`).

#### Scenario: Convidado tem as duas ações
- **WHEN** a lista traz um convidado "Joana Prado"
- **THEN** a linha tem os botões "Gerar novo link para Joana Prado" e "Cancelar convite de Joana Prado"

#### Scenario: Ativo e desativado têm Editar
- **WHEN** a lista traz o ativo "Rafael Duarte" (`u-rafael`) e o desativado "Bruno Teixeira" (`u-bruno`)
- **THEN** a linha de cada um tem o link "Editar Rafael Duarte" para `/users/u-rafael` e "Editar Bruno Teixeira" para `/users/u-bruno`

#### Scenario: Convidado não tem Editar
- **WHEN** a lista traz um convidado "Joana Prado"
- **THEN** a linha dele não tem link "Editar", e nenhum elemento dela tem `href` que comece com `/users/`
