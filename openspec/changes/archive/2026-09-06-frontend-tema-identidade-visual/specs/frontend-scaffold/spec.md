## RENAMED Requirements

- FROM: `### Requirement: Tema claro com alternância manual`
- TO: `### Requirement: Esquema de cor automático com alternância manual`

## MODIFIED Requirements

### Requirement: Esquema de cor automático com alternância manual
O sistema SHALL configurar, em `apps/frontend`, o Mantine com o esquema de
cor `auto` como padrão — seguindo a preferência de esquema declarada pelo
sistema operacional de quem nunca escolheu — oferecendo uma alternância
manual entre os esquemas `light` e `dark` que persiste a escolha do usuário
entre sessões.

A resolução do esquema inicial SHALL acontecer antes da primeira pintura da
página, de modo que quem usa o sistema operacional no escuro não veja a
interface piscar em claro enquanto a aplicação monta.

Os controles que exibem ou alternam o esquema SHALL ler o esquema efetivo, e
não a preferência crua — que sob o padrão `auto` não é nem `light` nem
`dark` — para que o rótulo do controle descreva o que está na tela e o
primeiro acionamento produza mudança visível.

#### Scenario: Aplicação segue a preferência do sistema operacional
- **WHEN** a aplicação é acessada pela primeira vez, sem nenhuma preferência
  de tema salva anteriormente
- **THEN** a interface é renderizada no esquema de cor declarado pelo sistema
  operacional do usuário

#### Scenario: Alternância de tema persiste entre sessões
- **WHEN** o usuário aciona o controle de alternância de tema e recarrega a
  aplicação
- **THEN** a interface é renderizada no esquema de cor escolhido
  anteriormente, sobrepondo-se à preferência do sistema operacional

#### Scenario: Sem piscada de esquema na carga inicial
- **WHEN** a aplicação é carregada por um usuário cujo esquema resolvido é o
  escuro, seja por preferência salva, seja pela preferência do sistema
  operacional
- **THEN** a página já é pintada no esquema escuro, sem exibir o esquema claro
  em nenhum momento antes da aplicação montar

#### Scenario: Controle de alternância descreve o esquema efetivo
- **WHEN** o usuário sem preferência salva, cujo sistema operacional está no
  escuro, visualiza o controle de alternância de tema
- **THEN** o controle oferece mudar para o esquema claro, e não para o escuro
  que já está em uso
