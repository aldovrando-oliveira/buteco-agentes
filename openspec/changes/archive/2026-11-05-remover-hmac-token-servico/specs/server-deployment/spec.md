## MODIFIED Requirements

### Requirement: Segredos compartilhados vêm de fonte única
O sistema SHALL garantir que segredos que precisam ser byte-idênticos
entre processos (`Mcp__CredentialEncryptionKey` entre
`apps/api`/`apps/workers`; o segredo de cada cliente de serviço entre o
`apps/api` e o serviço dele) sejam declarados uma única vez num arquivo de
ambiente do host, e injetados nos serviços correspondentes via interpolação de
variável do `docker-compose.prod.yml`, nunca copiados manualmente em blocos
`environment:` distintos. Nenhum segredo de assinatura de token SHALL ser
compartilhado entre processos: o `apps/api` assina todos os tokens com as chaves
do emissor, e os outros apps só conhecem as chaves públicas.

#### Scenario: Token do painel vale no apps/inbox e no apps/connectors
- **WHEN** um access token do painel, emitido pelo `apps/api`, é usado em
  `GET /channels` e em `GET /connectors/providers` pelo nginx do stack
- **THEN** o `apps/inbox` e o `apps/connectors` aceitam o token e respondem `200`,
  sem nenhum segredo de assinatura no ambiente deles

#### Scenario: Tokens de serviço entre apps/api e apps/connectors são aceitos nos dois sentidos
- **WHEN** `apps/api` gera no processo o token do `buteco-servico-api` para
  descrever uma pasta no `apps/connectors` durante o cadastro de base
  sincronizada, e `apps/connectors` obtém no emissor o token do
  `buteco-servico-connectors` para escrever no `apps/api` pelas rotas de `/sync`
- **THEN** nenhum dos dois lados responde `401`: o cadastro responde um resultado
  do conector (e não `502 connectors-error`), e o ciclo grava os documentos

#### Scenario: Compose recusa subir sem o domínio público
- **WHEN** `docker compose -f docker-compose.prod.yml --env-file .env.prod config` roda
  com `PUBLIC_DOMAIN` vazio
- **THEN** o compose falha citando `PUBLIC_DOMAIN`, antes de subir qualquer contêiner,
  e por isso o `apps/inbox` e o `apps/connectors` nunca sobem sem `Identity__Issuer`
