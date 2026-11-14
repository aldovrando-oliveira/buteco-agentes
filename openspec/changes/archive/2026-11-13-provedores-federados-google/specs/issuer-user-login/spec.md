## ADDED Requirements

### Requirement: A sessão pendente também nasce do retorno do provedor federado
O retorno do provedor que decide pela entrada (`federated-login`, "Decisão do login no retorno") SHALL gravar o cookie `buteco-sessao` da **sessão pendente**, com os mesmos atributos e o mesmo conteúdo do login por senha (`HttpOnly`, `SameSite=Strict`, `Path=/`, `Secure` fora de `Development`, 5 minutos, o id e o `security_stamp`, **sem** o claim da verificação). A verificação em 2 etapas SHALL continuar obrigatória para todo usuário, qualquer que seja a forma de entrada: o retorno SHALL mandar ao passo `totp` (com a verificação ligada) ou ao `cadastro` (sem ela), e a sessão SHALL se completar só pelas rotas de hoje (`POST /auth/session/second-factor`, `POST /auth/session/enrollment/setup` e `/confirm`). O authorize, a troca do código e a renovação SHALL NOT mudar.

**Como o cookie atravessa a cadeia cross-site.** A navegação que volta do provedor começa em outro site. Por isso:

1. o cookie SHALL ser gravado na **resposta do retorno** (`GET /auth/federated/callback`), junto do `302` para o `/entrar`. A gravação é permitida porque o pedido é uma navegação de topo: draft-ietf-httpbis-rfc6265bis-22, §5.7, passo 18.3 ("If the cookie was received from a request which is navigating a top-level traversable … skip the remaining substeps and continue processing the cookie"; nota: "Top-level navigations can create a cookie with any `SameSite` value"). O que o §5.7 recusa é a gravação de `Lax` ou `Strict` em resposta a subrecurso cross-site (§4.1.2.7);
2. o cookie **não** acompanha o carregamento do `/entrar` que o `302` provoca, porque esse pedido ainda pertence à navegação iniciada no outro site (§5.6.7.1: "Same-site cookies in "Strict" enforcement mode will not be sent along with top-level navigations which are triggered from a cross-site document context"). O `/entrar` é o documento do painel e não lê a sessão;
3. o cookie **acompanha** o `POST` do segundo passo ou do cadastro, feito por `fetch` do documento do painel: o "site for cookies" desse documento é a origem do painel (§5.2.1: "For a document displayed in a top-level traversable, the document's "site for cookies" is the top-level origin"), a URL é da mesma origem, e o pedido é same-site (§5.2);
4. o cookie completo acompanha o `/connect/authorize` porque a navegação ao `retorno` é iniciada pelo próprio documento do painel, como no login por senha.

Por isso o retorno SHALL NOT responder `302` direto ao `/connect/authorize`: esse pedido ainda seria da cadeia cross-site, e o cookie `Strict` não iria.

#### Scenario: O cookie pendente do retorno não emite código
- **WHEN** um usuário vinculado volta do provedor, e o mesmo navegador pede `GET /connect/authorize` com o cookie gravado pelo retorno
- **THEN** a resposta é `302` para `/entrar`, sem código

#### Scenario: O segundo passo completa a sessão federada
- **WHEN** depois do retorno, o painel envia `POST /auth/session/second-factor` com o cookie gravado pelo retorno e um código válido
- **THEN** a resposta é `204`, e o authorize seguinte com o cookie emite o código

#### Scenario: Quem nunca cadastrou cadastra no login federado
- **WHEN** um usuário vinculado, sem a verificação, volta do provedor e o painel chama `POST /auth/session/enrollment/setup` e `/confirm` com o cookie do retorno
- **THEN** o `setup` responde `200` com o segredo, e o `confirm` com um código válido responde `200` com os códigos de recuperação e completa a sessão

#### Scenario: O retorno nunca manda ao authorize
- **WHEN** o retorno decide pela entrada, com um `retorno` de authorize no início
- **THEN** o `Location` é `/entrar?…`, com o `retorno` na query, e não `/connect/authorize`
