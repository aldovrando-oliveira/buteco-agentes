## RENAMED Requirements

- FROM: `### Requirement: Token validado localmente com a chave compartilhada`
- TO: `### Requirement: Token do emissor validado localmente`

## MODIFIED Requirements

### Requirement: Token do emissor validado localmente
`apps/connectors` SHALL validar o token `Bearer` de toda rota não anônima como
access token do emissor do `apps/api` (capability `issuer-token-validation`), com
as chaves obtidas em segundo plano pelo `Api:BaseUrl`, sem chamada de rede no
caminho da requisição. Token ausente, malformado, com assinatura inválida ou
expirado SHALL receber `401`, com `WWW-Authenticate: Bearer`. `Identity:Issuer` ou
`Api:BaseUrl` ausentes ou vazios SHALL derrubar o boot.

#### Scenario: Sem token
- **WHEN** um cliente faz `GET /connectors/providers` sem `Authorization`
- **THEN** a resposta é `401`, com `WWW-Authenticate: Bearer`

#### Scenario: Token do painel é aceito
- **WHEN** um access token do emissor do cliente `buteco-painel`, com `aud`
  `buteco-connectors`, é enviado a `GET /connectors/providers`
- **THEN** a autenticação passa e a rota responde `200`

#### Scenario: Sem Identity:Issuer o boot falha
- **WHEN** o processo sobe sem `Identity__Issuer`
- **THEN** a inicialização falha nomeando a configuração ausente

### Requirement: Imagem buteco-connectors
O sistema SHALL prover `apps/connectors/Dockerfile` multi-stage, com build
context na raiz do monorepo, imagem final runtime ASP.NET sem SDK, usuário
non-root, porta 8080 e `HEALTHCHECK` contra `/health`.

#### Scenario: Build da imagem a partir da raiz
- **WHEN** `docker build -f apps/connectors/Dockerfile -t buteco-connectors .` é
  executado na raiz do monorepo
- **THEN** o build completa, e o contêiner iniciado com `Identity__Issuer`,
  `Api__BaseUrl` e `ServiceClient__Secret` responde `200` em `/health` na porta
  8080

### Requirement: A suíte de integração do apps/connectors autentica pelo token do emissor
A fábrica de integração de `apps/connectors/tests` SHALL subir o `apps/connectors`
sempre com a validação do token do emissor ligada contra um emissor de teste em
memória, com `Identity:Issuer` e `Api:BaseUrl` do emissor de teste por padrão, e
os casos SHALL autenticar com o access token do cliente que a tabela de clientes do
`apps/connectors` mapeia para o subject que provam. Quando o caso não pede o
`Api:BaseUrl`, a fábrica SHALL retirar do host de teste o agendamento do ciclo de
sincronização, sem opção nova de configuração de produção. Nenhum helper de teste
SHALL assinar token fora do emissor.

#### Scenario: Fábrica padrão aponta para o emissor de teste
- **WHEN** um caso usa a fábrica sem configuração própria
- **THEN** o `Api:BaseUrl` resolvido é o endereço interno do emissor de teste, e não
  o do `appsettings.Development.json`

#### Scenario: Fábrica padrão sem agendamento do ciclo
- **WHEN** um caso usa a fábrica sem pedir `Api:BaseUrl`
- **THEN** nenhum pedido chega ao `apps/api` falso nem ao `/connect/token` falso
  depois do boot
