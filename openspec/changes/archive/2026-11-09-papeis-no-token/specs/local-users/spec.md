## ADDED Requirements

### Requirement: Papel do usuário, gravado sem padrão no código
Todo usuário, inclusive o convidado, SHALL ter um papel: `admin`, `operator` ou `reader` no banco e no claim `role`, e `admin`, `operador` ou `leitor` no campo `papel` das respostas e dos corpos JSON. A leitura de um papel gravado fora dos três valores SHALL falhar com erro que cita o valor, e SHALL NOT tratá-lo como nenhum dos três. O banco SHALL recusar, por `CHECK`, um papel fora dos três.

Todo `INSERT` em `users` feito pelo `apps/api` SHALL levar o papel explicitamente. O `DEFAULT 'reader'` da coluna SHALL existir só no SQL da migration, para a imagem anterior inserir sem a coluna numa volta atrás, e SHALL NOT estar no modelo do EF.

A migration SHALL dar `admin` a todo usuário que já existe, ativo, desativado ou convidado. O bootstrap SHALL criar o primeiro usuário com o papel `admin`.

#### Scenario: Usuários existentes viram Admin
- **WHEN** a migration roda sobre um banco com um usuário ativo, um desativado e um convidado
- **THEN** os três ficam com `role = 'admin'`

#### Scenario: O bootstrap cria um Admin
- **WHEN** o app sobe sem nenhum usuário, com `Auth:BootstrapAdminEmail` e `Auth:OperatorPasswordHash` válidos
- **THEN** o usuário criado tem o papel `admin`

#### Scenario: Todo INSERT do código leva o papel
- **WHEN** o `apps/api` grava um usuário de papel `admin`, um convite de papel `operator` e o usuário do bootstrap, com o SQL emitido capturado
- **THEN** todo `INSERT INTO users` emitido lista a coluna `role`, e os papéis gravados são os pedidos, nunca o `reader` do default

#### Scenario: O default fica só no banco
- **WHEN** uma linha é inserida em `users` por SQL sem a coluna `role`
- **THEN** ela fica com `role = 'reader'`, e o modelo do EF não declara valor padrão para a propriedade

#### Scenario: Papel desconhecido falha na leitura
- **WHEN** uma linha de `users` tem um `role` fora dos três valores, gravado com o `CHECK` desligado
- **THEN** a leitura falha com um erro que cita o valor gravado
