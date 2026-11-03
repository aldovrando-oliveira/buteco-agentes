## MODIFIED Requirements

### Requirement: Lista de usuários
`GET /users` SHALL responder `200` com todos os usuários, ativos, desativados e convidados, sem filtro e sem paginação, cada um com `id`, `nome`, `email`, `estado` (`ativo`, `desativado` ou `convidado`), `formasDeEntrada` (`["senha"]` quando há senha gravada; vazia sem senha), `duasEtapas` (verdadeiro quando a verificação está ligada), `ultimoAcesso`, `criadoEm` e `convite`. `convite` SHALL ser `{ expiraEm, vencido }` no convidado, com `vencido` calculado pelo relógio do servidor, e nulo nos outros. O convite vencido SHALL continuar na lista até gerar novo link ou cancelar. A lista SHALL sair ordenada por nome, com desempate pelo `id` como último critério (`api-response-ordering`).

#### Scenario: Campos e estados
- **WHEN** existem um usuário ativo com a verificação ligada e um desativado
- **THEN** os dois aparecem, com `estado` e `duasEtapas` conforme gravados, `formasDeEntrada` igual a `["senha"]` e `convite` nulo

#### Scenario: Convite pendente e convite vencido
- **WHEN** existem um convite dentro da validade e um vencido
- **THEN** os dois aparecem com `estado` `convidado`, `formasDeEntrada` vazia, `duasEtapas` falso, `ultimoAcesso` nulo, e `convite.vencido` falso no primeiro e verdadeiro no segundo

#### Scenario: Desempate pelo id
- **WHEN** dois usuários têm o mesmo nome
- **THEN** eles saem em ordem crescente de `id`, e a consulta emitida termina o `ORDER BY` no `id`

### Requirement: Detalhe de usuário com a contagem de sessões
`GET /users/{id}` SHALL responder `200` com os campos da lista, mais `desativadoEm`, `convidadoPor` e `sessoes: { ativas, ultimaAtividade }`, em que `ativas` é o número de sessões vivas do usuário (a definição de `user-account`) e `ultimaAtividade` é o maior último uso entre elas, ou nulo sem sessão. `convidadoPor` SHALL ser `{ id, nome }` de quem criou o convite, também depois de aceito, e nulo para quem não entrou por convite. Id inexistente SHALL responder `404`.

#### Scenario: Contagem de sessões de outro usuário
- **WHEN** um usuário com duas sessões vivas, uma renovada depois da outra, é consultado por outro
- **THEN** `sessoes.ativas` é 2, e `sessoes.ultimaAtividade` é o instante da renovação mais recente

#### Scenario: Quem convidou
- **WHEN** o detalhe de um usuário que aceitou um convite é consultado
- **THEN** `convidadoPor` traz o id e o nome de quem convidou, e `criadoEm` é o instante do convite

#### Scenario: Usuário inexistente
- **WHEN** o detalhe é pedido para um id que não existe
- **THEN** a resposta é `404`

### Requirement: Desativar outro usuário, sem nunca ficar sem usuário ativo
`POST /users/{id}/deactivate` SHALL marcar o usuário como desativado, gravar a data, trocar o carimbo de segurança, apagar o link de redefinição pendente e revogar todas as autorizações dele, numa mesma transação, respondendo `200` com o detalhe. O usuário já desativado SHALL receber `200` com o estado atual, sem gravar nada. O convidado SHALL receber `409` "Este usuário ainda não aceitou o convite.", sem gravar nada: o convite sai pelo cancelamento. O próprio usuário que chama SHALL receber `403`, e id inexistente, `404`.

A desativação SHALL rodar sob o `pg_advisory_xact_lock` de chave `BUTECOD1` (`4779819235982394417`), e SHALL reler dentro do lock quem chama e o alvo: quem chama já desativado SHALL receber `403`, sem gravar nada. Assim, duas desativações concorrentes nunca deixam a instalação sem usuário ativo.

#### Scenario: Desativar outro
- **WHEN** um usuário desativa outro que tem sessões vivas
- **THEN** a resposta é `200` com `estado` `desativado`, a renovação das sessões do outro recebe `invalid_grant`, o login dele com a senha certa responde `403`, e o detalhe dele mostra zero sessões

#### Scenario: Desativar o convidado
- **WHEN** um convidado é desativado
- **THEN** a resposta é `409`, ele continua no estado convidado, e o link do convite continua valendo

#### Scenario: Desativar a si mesmo
- **WHEN** um usuário pede a própria desativação
- **THEN** a resposta é `403`, e ele continua ativo

#### Scenario: Desativar de novo
- **WHEN** um usuário já desativado é desativado de novo
- **THEN** a resposta é `200`, e o carimbo dele não muda

#### Scenario: Dois únicos ativos desativando um ao outro
- **WHEN** os dois únicos usuários ativos pedem, ao mesmo tempo, a desativação um do outro
- **THEN** uma resposta é `200` e a outra `403`, um dos dois continua ativo, e a consulta emitida pede o `pg_advisory_xact_lock` antes de gravar o estado

### Requirement: Reativar usuário
`POST /users/{id}/activate` SHALL marcar o usuário desativado como ativo e limpar a data de desativação, sem trocar o carimbo de segurança, respondendo `200` com o detalhe. O usuário já ativo SHALL receber `200`, sem gravar nada. O convidado SHALL receber `409` "Este usuário ainda não aceitou o convite.", sem gravar nada: ele só fica ativo pelo aceite do convite. Id inexistente SHALL responder `404`. As sessões de antes da desativação SHALL NOT voltar: a pessoa entra de novo com a mesma senha, e a verificação em 2 etapas fica como estava. O link de redefinição apagado pela desativação SHALL NOT voltar.

#### Scenario: Reativar
- **WHEN** um usuário desativado, com a verificação ligada, é reativado
- **THEN** a resposta é `200`, o refresh de antes da desativação recebe `invalid_grant`, o login com a mesma senha pede o segundo passo, e a sessão nova renova

#### Scenario: Reativar o convidado
- **WHEN** um convidado é reativado
- **THEN** a resposta é `409`, e ele continua no estado convidado, sem senha
