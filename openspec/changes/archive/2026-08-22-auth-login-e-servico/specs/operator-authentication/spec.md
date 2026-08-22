## ADDED Requirements

### Requirement: Login do operador único
`apps/api` SHALL expor `POST /auth/login`, recebendo `username` e
`password`, validando contra um único operador configurado via variável
de ambiente (usuário e hash de senha). Em caso de sucesso, SHALL
responder `200 OK` com um token e seu instante de expiração. Em caso de
usuário ou senha incorretos, SHALL responder `401 Unauthorized` com a
mesma mensagem genérica em ambos os casos, sem indicar qual campo estava
incorreto.

#### Scenario: Login com credenciais corretas
- **WHEN** um cliente envia `POST /auth/login` com o `username` e
  `password` que correspondem ao operador configurado
- **THEN** a API responde `200 OK` com um token não vazio e um instante
  de expiração futuro

#### Scenario: Login com usuário incorreto
- **WHEN** um cliente envia `POST /auth/login` com um `username` que não
  corresponde ao operador configurado
- **THEN** a API responde `401 Unauthorized` com uma mensagem genérica de
  credencial inválida

#### Scenario: Login com senha incorreta
- **WHEN** um cliente envia `POST /auth/login` com o `username` correto e
  um `password` que não corresponde à senha configurada
- **THEN** a API responde `401 Unauthorized` com a mesma mensagem
  genérica de credencial inválida usada no caso de usuário incorreto

### Requirement: Token de operador assinado com TTL curto e configurável
O sistema SHALL emitir o token do operador assinado, com expiração após
um intervalo configurável (`Auth:OperatorTokenLifetime`), cujo valor
padrão quando não configurado SHALL ser 30 minutos. Um token
estruturalmente válido porém expirado SHALL ser tratado como inválido por
qualquer verificação subsequente.

#### Scenario: Token dentro do prazo é aceito
- **WHEN** um token de operador emitido há menos tempo que seu TTL
  configurado é apresentado em uma requisição a uma rota autenticada
- **THEN** a requisição é autenticada com sucesso, sem erro `401`

#### Scenario: Token expirado é rejeitado
- **WHEN** um token de operador cujo instante de expiração já passou é
  apresentado em uma requisição a uma rota autenticada
- **THEN** a requisição é rejeitada com `401 Unauthorized`

#### Scenario: TTL assume 30 minutos quando não configurado
- **WHEN** `Auth:OperatorTokenLifetime` não está presente na configuração
  e um login é realizado com sucesso
- **THEN** o instante de expiração do token retornado é 30 minutos após a
  emissão
