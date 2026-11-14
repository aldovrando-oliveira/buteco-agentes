## MODIFIED Requirements

### Requirement: Lista de usuários
`GET /users` SHALL responder `200` com todos os usuários, ativos, desativados e convidados, sem filtro e sem paginação, cada um com `id`, `nome`, `email`, `estado` (`ativo`, `desativado` ou `convidado`), `formasDeEntrada`, `duasEtapas` (verdadeiro quando a verificação está ligada), `ultimoAcesso`, `criadoEm` e `convite`. `formasDeEntrada` SHALL ser uma lista de strings, nesta ordem e sem repetir: `"senha"` quando há senha gravada, e o `tipo` de cada provedor disponível (`federated-login`) com que o usuário tem vínculo (`"google"`); vazia sem nenhuma. `convite` SHALL ser `{ expiraEm, vencido }` no convidado, com `vencido` calculado pelo relógio do servidor, e nulo nos outros. O convite vencido SHALL continuar na lista até gerar novo link ou cancelar. A lista SHALL sair ordenada por nome, com desempate pelo `id` como último critério (`api-response-ordering`).

#### Scenario: Campos e estados
- **WHEN** existem um usuário ativo com a verificação ligada e um desativado
- **THEN** os dois aparecem, com `estado` e `duasEtapas` conforme gravados, `formasDeEntrada` igual a `["senha"]` e `convite` nulo

#### Scenario: Convite pendente e convite vencido
- **WHEN** existem um convite dentro da validade e um vencido
- **THEN** os dois aparecem com `estado` `convidado`, `formasDeEntrada` vazia, `duasEtapas` falso, `ultimoAcesso` nulo, e `convite.vencido` falso no primeiro e verdadeiro no segundo

#### Scenario: Desempate pelo id
- **WHEN** dois usuários têm o mesmo nome
- **THEN** eles saem em ordem crescente de `id`, e a consulta emitida termina o `ORDER BY` no `id`

#### Scenario: Vínculo com o provedor
- **WHEN** existem um usuário com senha e vínculo com o provedor, e um sem senha só com o vínculo
- **THEN** o primeiro sai com `formasDeEntrada` igual a `["senha", "google"]`, e o segundo com `["google"]`

### Requirement: Detalhe de usuário com a contagem de sessões
`GET /users/{id}` SHALL responder `200` com os campos da lista, mais `desativadoEm`, `convidadoPor`, `sessoes: { ativas, ultimaAtividade }` e `contasConectadas`, em que `ativas` é o número de sessões vivas do usuário (a definição de `user-account`) e `ultimaAtividade` é o maior último uso entre elas, ou nulo sem sessão. `convidadoPor` SHALL ser `{ id, nome }` de quem criou o convite, também depois de aceito, e nulo para quem não entrou por convite. `contasConectadas` SHALL ser `[{ chave, tipo, email, conectadaEm, ultimoUso, disponivel }]`, um item por vínculo, inclusive o de provedor fora da configuração (com `chave` nula e `disponivel` falso), na ordem de `conectadaEm`; vazia sem vínculo. Id inexistente SHALL responder `404`.

#### Scenario: Contagem de sessões de outro usuário
- **WHEN** um usuário com duas sessões vivas, uma renovada depois da outra, é consultado por outro
- **THEN** `sessoes.ativas` é 2, e `sessoes.ultimaAtividade` é o instante da renovação mais recente

#### Scenario: Quem convidou
- **WHEN** o detalhe de um usuário que aceitou um convite é consultado
- **THEN** `convidadoPor` traz o id e o nome de quem convidou, e `criadoEm` é o instante do convite

#### Scenario: Usuário inexistente
- **WHEN** o detalhe é pedido para um id que não existe
- **THEN** a resposta é `404`

#### Scenario: As contas conectadas
- **WHEN** o detalhe de um usuário vinculado ao provedor `corporativo` é consultado
- **THEN** `contasConectadas` traz a chave `corporativo`, `tipo` `google`, o e-mail confirmado, `conectadaEm`, `ultimoUso` e `disponivel` verdadeiro, e nenhum `sub`

### Requirement: Mudar o papel de um usuário
`PUT /users/{id}/role` com `{ papel, senha?, codigo? }` (`papel` em `admin`, `operador` ou `leitor`, pela tabela de correspondência do design, D3) SHALL gravar o papel do usuário indicado (ativo ou convidado; o desativado SHALL receber `409`) e responder `200` com o detalhe. Papel igual ao gravado SHALL responder `200` sem gravar nada. Papel fora dos três valores SHALL responder `400`, e id inexistente, `404`.

- **Promover a Admin** (papel novo `admin`, alvo de outro papel) SHALL pedir a confirmação de quem chama, com as regras de "Criar convite, gerar novo link e gerar redefinição pedem a senha de quem chama" (`user-access-links`): a senha no campo `senha` para quem tem senha, o código do app no campo `codigo` para quem não tem; `423` durante o bloqueio do e-mail de quem chama, `401` contado nesse bloqueio, nada gravado. Rebaixar SHALL NOT pedir confirmação.
- **A mudança SHALL rodar sob o `pg_advisory_xact_lock` `BUTECOD1`** (`4779819235982394417`), a mesma chave da desativação, relendo dentro do lock quem chama e o alvo. Quem chama que já não é Admin ativo SHALL receber `403`, sem gravar nada.
- **O último Admin:** tirar o papel `admin` de um Admin quando ele é o único Admin ativo, contado dentro do lock, SHALL responder `409` "A instalação precisa de pelo menos um Admin ativo.", sem gravar nada.
- **A si mesmo:** quem chama SHALL poder mudar o próprio papel, sujeito à regra do último Admin.
- Mudar o papel SHALL NOT trocar o carimbo de segurança nem revogar sessões (`route-roles`, "Prazos da mudança de papel").

#### Scenario: Rebaixar outro Admin
- **WHEN** um de dois Admins ativos rebaixa o outro a `operador`
- **THEN** a resposta é `200` com `papel` `operador`, o carimbo do alvo não muda, e a sessão dele continua renovando, já com `role` `operator`

#### Scenario: Rebaixar a si mesmo com outro Admin
- **WHEN** um de dois Admins ativos rebaixa a si mesmo a `leitor`
- **THEN** a resposta é `200`, e o `GET /users` seguinte dele, com o mesmo access token, responde `403`

#### Scenario: O último Admin não se rebaixa
- **WHEN** o único Admin ativo pede o próprio rebaixamento
- **THEN** a resposta é `409`, e ele continua `admin`

#### Scenario: Dois Admins rebaixando um ao outro
- **WHEN** os dois únicos Admins ativos pedem, ao mesmo tempo, o rebaixamento um do outro
- **THEN** uma resposta é `200` e a outra `403`, um dos dois continua `admin`, e a consulta emitida pede o `pg_advisory_xact_lock` antes de gravar o papel

#### Scenario: Promover pede a senha
- **WHEN** um Admin promove um Operador a `admin` sem `senha`, e depois com a própria senha certa
- **THEN** a primeira resposta é `401`, contada no bloqueio do e-mail de quem chama, o alvo continua `operador`, e a segunda é `200` com `papel` `admin`

#### Scenario: Admin sem senha promove com o código do app
- **WHEN** um Admin sem senha, com a verificação ligada, promove um Operador a `admin` com um `codigo` válido do app
- **THEN** a resposta é `200` com `papel` `admin`

#### Scenario: Papel inválido
- **WHEN** o corpo traz `papel` `superusuario`
- **THEN** a resposta é `400`, e nada é gravado
