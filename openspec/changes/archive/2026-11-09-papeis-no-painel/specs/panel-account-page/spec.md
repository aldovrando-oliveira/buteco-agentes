## MODIFIED Requirements

### Requirement: Cartão Dados
O cartão "Dados" SHALL vir do `GET /account`, e não do id token, e SHALL mostrar, só para leitura, como o quadro `MinhaConta` (`entrega=m2`):
- "Nome": `nome`;
- "E-mail": `email`, em fonte monoespaçada;
- "Papel": o rótulo do `papel` (`panel-roles`), o gravado; "—" para valor desconhecido;
- "No painel desde": `criadoEm`, como `dd/mm/aaaa`;
- "Último acesso": `ultimoAcesso`, pelo formato relativo de datas desta capacidade.

Abaixo, SHALL mostrar, pelo `papel` do `GET /account`:
- Admin: "Para mudar o nome ou o e-mail, use Usuários.", em que "Usuários" é um link para o detalhe do próprio usuário, `/users/{id}`, com o `id` do `GET /account`;
- Operador, Leitor ou papel desconhecido: "Para mudar o nome ou o e-mail, peça a um Admin do painel.", sem link.

Nenhum campo do cartão SHALL ser editável.

#### Scenario: Os dados da conta
- **WHEN** `GET /account` responde `{ nome: "Ana Souza", email: "ana@exemplo.test", papel: "operador", criadoEm: "2026-08-12T…", ultimoAcesso: <hoje às 14:15> }`
- **THEN** o cartão mostra "Ana Souza", "ana@exemplo.test", "Operador", "12/08/2026" e "hoje, 14:15"

#### Scenario: "Usuários" leva ao detalhe do próprio usuário
- **WHEN** `GET /account` responde com `id: "u-ana"` e `papel: "admin"`
- **THEN** o texto "Para mudar o nome ou o e-mail, use Usuários." aparece, e "Usuários" é um link para `/users/u-ana`, e não para `/users`

#### Scenario: Quem não é Admin pede a um Admin
- **WHEN** `GET /account` responde com `papel: "leitor"`
- **THEN** o texto é "Para mudar o nome ou o e-mail, peça a um Admin do painel.", e não há link para `/users`

#### Scenario: O papel gravado vale sobre o do token
- **WHEN** o id token da sessão ainda traz `role` `admin`, e `GET /account` responde com `papel: "operador"`
- **THEN** o cartão mostra "Operador" e o texto de quem não é Admin

### Requirement: Estado da verificação em 2 etapas
O cartão "Verificação em 2 etapas" SHALL vir do `GET /account/two-factor` (`{ ativa, ativadaEm, codigosRestantes }`):
- **`ativa: false`** (`nao-configurada`): o selo "Não configurada" e o texto "Este painel passou a exigir a verificação em 2 etapas. Na próxima vez que você entrar, o login pede o cadastro do app autenticador.", sem botão. É o intervalo em que o access token de uma sessão de antes da obrigatoriedade ainda vale;
- **`ativa: true`** (`ativa`): o selo "Ativa", "desde `<ativadaEm>`" (`dd/mm/aaaa`), "N códigos de recuperação restantes.", "Gerar novos códigos" e o texto "Trocou de celular ou perdeu o app? Peça a um Admin do painel que redefina a sua verificação; no próximo login você cadastra o app de novo.". O cartão SHALL NOT ter "Remover verificação";
- **com `ativa: true` e `codigosRestantes` de 1 a 3** (`poucos-codigos`): também o aviso "Restam N códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.". Com 1, o texto SHALL ser "Resta 1 código de recuperação. Gere novos para não ficar sem acesso se perder o celular.";
- **com `ativa: true` e `codigosRestantes` 0**: o aviso "Não restam códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.".

Com 4 códigos ou mais, nenhum desses avisos SHALL aparecer.

#### Scenario: Sem a verificação, o cartão não oferece cadastro
- **WHEN** `GET /account/two-factor` responde `{ ativa: false, ativadaEm: null, codigosRestantes: 0 }`
- **THEN** o cartão mostra "Não configurada" e "Este painel passou a exigir a verificação em 2 etapas. Na próxima vez que você entrar, o login pede o cadastro do app autenticador.", sem nenhum botão

#### Scenario: Ativa, sem remover
- **WHEN** `GET /account/two-factor` responde `{ ativa: true, codigosRestantes: 8 }`
- **THEN** o cartão mostra "Ativa", "8 códigos de recuperação restantes.", "Gerar novos códigos" habilitado e o texto "Trocou de celular ou perdeu o app? Peça a um Admin do painel que redefina a sua verificação; no próximo login você cadastra o app de novo.", e não há "Remover verificação"

#### Scenario: Poucos códigos
- **WHEN** `codigosRestantes` é 3, 2, 1 ou 0
- **THEN** aparece o aviso com o texto do número (plural, singular para 1, o texto próprio para 0); com 4 ou mais, nenhum aviso

## ADDED Requirements

### Requirement: Aviso de papel novo depois de rebaixar a si mesmo
Quando Minha conta abre pela navegação do rebaixamento de si mesmo (`panel-user-detail-page`), com o papel novo no estado da navegação, a página SHALL mostrar no topo, acima do cartão Dados, o aviso "O seu papel agora é {rótulo do papel}." (`role="status"`), como o quadro `MinhaConta` (`aviso=rebaixado`). O aviso SHALL NOT aparecer quando o papel do estado é Admin ou desconhecido, nem em outra entrada na página, inclusive ao recarregar.

#### Scenario: Chegada depois do rebaixamento
- **WHEN** Minha conta abre com o papel `operador` no estado da navegação
- **THEN** o topo mostra "O seu papel agora é Operador."

#### Scenario: Entrada comum sem aviso
- **WHEN** a pessoa abre `/account` pelo rodapé
- **THEN** não há "O seu papel agora é"
