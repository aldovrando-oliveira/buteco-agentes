## MODIFIED Requirements

### Requirement: Token de operador emitido por apps/api é aceito por apps/inbox sem chamada de rede entre os processos
`apps/inbox` SHALL aceitar, em suas rotas autenticadas, um token de
operador emitido por `apps/api`, validando-o localmente (mesma chave de
assinatura compartilhada) sem realizar nenhuma chamada de rede a
`apps/api` para validar esse token. Isso SHALL valer também com a validação
do token do emissor ligada (`Identity:Issuer` configurado) e nenhuma chave do
emissor obtida: o token HMAC SHALL NOT depender das chaves do emissor.

#### Scenario: Token emitido por apps/api é aceito por apps/inbox
- **WHEN** um operador realiza login com sucesso em `apps/api`
  (`POST /auth/login`) e usa o token retornado para acessar uma rota
  autenticada de `apps/inbox` (ex. `GET /channels`), mesmo com
  `apps/inbox` configurado sem conseguir alcançar `apps/api` pela rede
  nesse instante
- **THEN** `apps/inbox` responde de acordo com a rota (`200 OK`), sem
  `401 Unauthorized`

#### Scenario: Token HMAC aceito com o emissor ligado e sem chaves
- **WHEN** `apps/inbox` sobe com `Identity:Issuer` configurado e o `apps/api`
  inalcançável desde o boot, e o operador usa o token HMAC em `GET /channels`
- **THEN** `apps/inbox` responde `200 OK`, e nenhuma requisição foi feita ao
  discovery nem ao JWKS por causa dessa chamada
