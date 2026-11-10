## ADDED Requirements

### Requirement: Aplicações externas são só do Admin, com o papel gravado
As rotas de `/applications/*` do `apps/api` SHALL atender só o token do painel com `role` `admin` **e** cujo usuário tenha o papel `admin` gravado (`CurrentUser`), inclusive as de leitura. Outro papel no token SHALL receber o `403` vazio da autorização (`route-roles`); o token `admin` de um usuário rebaixado SHALL receber o `403` do `CurrentUser` na requisição seguinte ao rebaixamento. Nenhum token de serviço nem de aplicação SHALL alcançar essas rotas.

#### Scenario: Operador e Leitor não veem aplicações
- **WHEN** um token do painel com `role` `operator` e outro com `role` `reader` chamam `GET /applications/`
- **THEN** as duas respostas são `403`

#### Scenario: Admin rebaixado perde Aplicações na requisição seguinte
- **WHEN** um Admin é rebaixado a Operador e, com o access token emitido antes, ainda no prazo, chama `GET /applications/`
- **THEN** a resposta é `403`

### Requirement: Criar aplicação externa
`POST /applications/` SHALL receber `{ nome, descricao, senha }` e SHALL conferir, nesta ordem, parando na primeira falha:

1. quem chama é Admin gravado (`403`);
2. quem chama não está bloqueado (`423`, do bloqueio de login de quem chama, sem conferir nada);
3. o corpo: `nome` obrigatório, sem espaço nas pontas depois de aparado, de 1 a 80 caracteres; `descricao` opcional, até 280 caracteres, vazia gravada como ausente. Falha SHALL responder `400` `ValidationProblem` com os campos `nome` ("Informe o nome.", "O nome tem no máximo 80 caracteres.") e `descricao` ("A descrição tem no máximo 280 caracteres.");
4. o nome não repete o de outra aplicação, ativa ou desativada, sem diferenciar maiúscula: `409` com `title` "Já existe uma aplicação com este nome.";
5. a senha de quem chama (`CallerPassword`): ausente ou errada, `401` com `title` "Senha inválida.", contada no bloqueio de quem chama.

Passando tudo, SHALL criar, **numa transação**, a aplicação, o cliente do OpenIddict (D3 do design: confidencial, consentimento implícito, `DisplayName` = nome, permissões só de endpoint de token, `client_credentials` e escopo `buteco-api`, segredo de enchimento) e o primeiro segredo, e SHALL responder `201` com `{ id, nome, descricao, clientId, ativa: true, criadaEm, ultimoUso: null, endpointToken, segredo: { id, valor, sufixo, criadoEm, validoAte } }`. O `valor` SHALL aparecer só nesta resposta. `endpointToken` SHALL ser `{issuer}connect/token`, com o issuer do emissor (a `PublicUrl:BaseUrl` com barra final).

#### Scenario: Criação devolve o client ID e o segredo uma vez
- **WHEN** um Admin cria a aplicação "Portal do Parceiro" com a senha certa
- **THEN** a resposta é `201` com um `clientId` `btc_app_` mais 8 caracteres, um `segredo.valor` `btc_sk_` mais 43 caracteres, e `GET /applications/{id}` depois não traz o `valor` nem o hash

#### Scenario: Senha errada não cria
- **WHEN** um Admin cria uma aplicação com a senha errada
- **THEN** a resposta é `401` com `title` "Senha inválida.", e não existe linha nova em `external_applications` nem em `openiddict_applications`

#### Scenario: Corpo inválido não conta no bloqueio
- **WHEN** um Admin cria uma aplicação com o nome vazio e a senha errada
- **THEN** a resposta é `400` com o campo `nome`, e a contagem de falhas de login de quem chama não muda

#### Scenario: Nome repetido, sem diferenciar maiúscula
- **WHEN** existe a aplicação desativada "CRM legado" e um Admin cria "crm LEGADO"
- **THEN** a resposta é `409` com `title` "Já existe uma aplicação com este nome."

#### Scenario: Bloqueado antes de tudo
- **WHEN** o Admin está bloqueado pelo login e cria uma aplicação com o corpo inválido
- **THEN** a resposta é `423`

### Requirement: Client ID gerado, imutável e fora do prefixo reservado
O `client_id` de uma aplicação SHALL ser gerado pelo `apps/api`, no formato `btc_app_` seguido de 8 caracteres do alfabeto `23456789abcdefghijkmnpqrstuvwxyz`, por gerador criptográfico, e SHALL ser único (índice único, até 3 tentativas). Nenhuma rota SHALL receber `client_id`. Antes de gravar, um `client_id` que comece com `buteco-` SHALL ser recusado, sem gravar nenhuma linha. O `client_id` SHALL NOT mudar depois de criado.

#### Scenario: Prefixo reservado nunca é gravado
- **WHEN** o gerador de client ID devolve `buteco-x` e um Admin cria uma aplicação
- **THEN** a criação falha, e não existe linha nova em `external_applications` nem em `openiddict_applications`

#### Scenario: Client ID enviado no corpo é ignorado
- **WHEN** um Admin cria uma aplicação com `clientId` `buteco-painel` no corpo
- **THEN** a aplicação criada tem um `clientId` `btc_app_…`, e o cliente `buteco-painel` não muda

### Requirement: Listar aplicações
`GET /applications/` SHALL devolver todas as aplicações, ativas e desativadas, com `{ id, nome, descricao, clientId, ativa, criadaEm, desativadaEm, ultimoUso }`, ordenadas por ativas antes e depois pelo nome. `?busca=` SHALL filtrar pelo nome ou pelo `clientId` contendo o texto, sem diferenciar maiúscula. `?estado=ativas` e `?estado=desativadas` SHALL filtrar pelo estado; outro valor SHALL responder `400` com o campo `estado`. Sem aplicação, a resposta SHALL ser `200` com a lista vazia.

#### Scenario: Busca por client ID
- **WHEN** existem "Portal do Parceiro" (`btc_app_7kq2m9x4`) e "Chatbot do site", e o Admin chama `GET /applications/?busca=7KQ2`
- **THEN** a lista tem só "Portal do Parceiro"

#### Scenario: Desativadas continuam listadas, depois das ativas
- **WHEN** existem "Zeta" ativa e "Alfa" desativada, e o Admin chama `GET /applications/`
- **THEN** a lista é "Zeta" e depois "Alfa", com `ativa` `false` na segunda

#### Scenario: Estado desconhecido
- **WHEN** o Admin chama `GET /applications/?estado=todas`
- **THEN** a resposta é `400` com o campo `estado`

### Requirement: Detalhe da aplicação com os segredos
`GET /applications/{id}` SHALL devolver os campos da lista, mais `endpointToken` e `segredos`: cada um com `{ id, sufixo, estado, criadoEm, validoAte, revogadoEm, ultimoUso }`, do mais novo para o mais antigo. `estado` SHALL ser derivado no momento da leitura: `revogado` se revogado; senão `expirado` se `validoAte` passou; senão `substituido` se existe segredo mais novo da mesma aplicação; senão `ativo`. Nenhuma resposta SHALL trazer o hash nem o texto do segredo. Aplicação inexistente SHALL responder `404` com `title` "Aplicação não encontrada.".

#### Scenario: Os quatro estados
- **WHEN** uma aplicação tem um segredo revogado, um vencido, um substituído dentro da sobreposição e o mais novo, e o Admin chama o detalhe
- **THEN** os estados são `revogado`, `expirado`, `substituido` e `ativo`, e nenhum segredo traz `valor` nem hash

### Requirement: Editar nome e descrição
`PUT /applications/{id}` SHALL receber `{ nome, descricao }`, com as regras de campo e de nome repetido da criação (`400`, `409`, desconsiderando a própria aplicação), SHALL gravar também o `DisplayName` do cliente no OpenIddict, e SHALL responder `200` com o detalhe. Não SHALL pedir senha nem mudar o `clientId`. Aplicação desativada SHALL poder ser editada.

#### Scenario: Renomear não muda a credencial
- **WHEN** o Admin renomeia "Portal do Parceiro" para "Portal de Revendas"
- **THEN** o `clientId` é o mesmo, o `DisplayName` do cliente no OpenIddict é "Portal de Revendas", e o segredo vivo continua emitindo token

### Requirement: Desativar e reativar
`POST /applications/{id}/deactivate` SHALL, **numa transação**, gravar a desativação e revogar todo segredo vivo da aplicação, e SHALL responder `200` com o detalhe. Desativar uma aplicação já desativada SHALL responder `200` sem mudar nada. `POST /applications/{id}/activate` SHALL voltar a aplicação a ativa **sem segredo vivo** e SHALL responder `200` com o detalhe. Nenhuma das duas SHALL pedir senha. Não SHALL existir rota de exclusão de aplicação.

#### Scenario: Aplicação desativada não obtém token
- **WHEN** o Admin desativa uma aplicação e ela pede token com o segredo que tinha
- **THEN** a resposta do token é `invalid_client`, e todo segredo dela tem `estado` `revogado`

#### Scenario: Reativar não devolve o segredo antigo
- **WHEN** o Admin desativa e reativa uma aplicação, e ela pede token com o segredo de antes
- **THEN** a resposta do token é `invalid_client`, e o detalhe mostra a aplicação ativa sem segredo `ativo`

#### Scenario: Excluir não existe
- **WHEN** o Admin chama `DELETE /applications/{id}`
- **THEN** a resposta não é `2xx`, e a aplicação continua existindo

### Requirement: Gerar segredo, com sobreposição e limite
`POST /applications/{id}/secrets` SHALL receber `{ senha }` e SHALL conferir, nesta ordem: Admin gravado (`403`), bloqueio (`423`), aplicação existente (`404`), aplicação ativa (`409` "Reative a aplicação antes de gerar um segredo."), menos de 3 segredos vivos na aplicação (`409` "Revogue um segredo antes de gerar outro."), senha de quem chama (`401` "Senha inválida."). Passando, SHALL gerar o segredo, SHALL fazer cada segredo vivo anterior valer até no máximo 7 dias depois de agora, e SHALL responder `201` com `{ id, valor, sufixo, criadoEm, validoAte }`, o `valor` só aqui. "Vivo" SHALL ser não revogado e não vencido.

#### Scenario: O anterior vale mais 7 dias
- **WHEN** uma aplicação tem um segredo válido por mais 300 dias e o Admin gera outro
- **THEN** o anterior passa a `substituido`, com `validoAte` 7 dias depois da geração, e os dois emitem token

#### Scenario: O anterior que vence antes não ganha prazo
- **WHEN** o segredo vivo vence em 2 dias e o Admin gera outro
- **THEN** o `validoAte` do anterior não muda

#### Scenario: Quarto segredo vivo recusado
- **WHEN** uma aplicação tem 3 segredos vivos e o Admin gera outro com a senha certa
- **THEN** a resposta é `409` com `title` "Revogue um segredo antes de gerar outro.", e nenhum segredo é criado

#### Scenario: Senha errada não gera
- **WHEN** o Admin gera um segredo com a senha errada
- **THEN** a resposta é `401` com `title` "Senha inválida.", e nenhum segredo é criado

### Requirement: Revogar segredo
`POST /applications/{id}/secrets/{secretId}/revoke` SHALL gravar a revogação e responder `200` com o detalhe, sem pedir senha. Segredo de outra aplicação ou inexistente SHALL responder `404` com `title` "Segredo não encontrado.". Revogar um segredo já revogado ou vencido SHALL responder `200` sem mudar nada. O próximo pedido de token com o segredo revogado SHALL receber `invalid_client`; o token já emitido continua aceito até o `exp`, no máximo 5 minutos (`oidc-issuer`).

#### Scenario: Revogado recusado no próximo pedido
- **WHEN** o Admin revoga um segredo e a aplicação pede token com ele
- **THEN** a resposta é `invalid_client`

#### Scenario: Segredo de outra aplicação
- **WHEN** o Admin revoga, pela rota da aplicação A, um segredo da aplicação B
- **THEN** a resposta é `404`, e o segredo continua vivo

### Requirement: O segredo é gerado, mostrado uma vez e guardado como SHA-256
O segredo SHALL ser `btc_sk_` seguido de 32 bytes do gerador criptográfico em base64url sem preenchimento (43 caracteres). O `apps/api` SHALL guardar só o SHA-256 do texto (índice único) e o sufixo de 4 caracteres; o texto SHALL NOT ser gravado, logado nem devolvido fora das duas respostas de criação. A validade SHALL ser de 365 dias a partir da criação, e a sobreposição de 7 dias; as duas SHALL ser constantes do código, não opções.

#### Scenario: Só o hash no banco
- **WHEN** um segredo é gerado
- **THEN** a linha dele tem o SHA-256 do `valor` devolvido e o sufixo igual aos 4 últimos caracteres, e nenhuma coluna nem linha de log contém o `valor`

### Requirement: Último uso gravado na emissão do token
Quando um segredo de aplicação autentica um pedido de token, o `apps/api` SHALL gravar o instante em `ultimoUso` do segredo e da aplicação, no máximo uma vez por minuto por segredo. Nenhuma requisição a outra rota SHALL gravar último uso. Aplicação e segredo nunca usados SHALL ter `ultimoUso` `null`.

#### Scenario: Token emitido marca o uso
- **WHEN** uma aplicação nunca usada obtém um token
- **THEN** o detalhe mostra `ultimoUso` no segredo usado e na aplicação, e os outros segredos continuam `null`

#### Scenario: Rajada grava uma vez
- **WHEN** uma aplicação pede cinco tokens em menos de um minuto
- **THEN** o `ultimoUso` foi escrito uma vez nesse minuto
