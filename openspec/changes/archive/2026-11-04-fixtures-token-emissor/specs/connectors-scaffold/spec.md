## ADDED Requirements

### Requirement: A suíte de integração do apps/connectors autentica pelo token do emissor
A fábrica de integração de `apps/connectors/tests` SHALL subir o `apps/connectors`
com a validação do token do emissor ligada contra um emissor de teste em memória, e
os casos SHALL autenticar com o access token do cliente que a tabela de clientes do
`apps/connectors` mapeia para o subject que provam. Quando o caso não pede o
`Api:BaseUrl`, a fábrica SHALL retirar do host de teste o agendamento do ciclo de
sincronização, sem opção nova de configuração de produção. Nenhum caso SHALL
depender do token HMAC, exceto os que provam o próprio HMAC e os que provam o app
sem `Api:BaseUrl`, listados por nome na #182.

#### Scenario: Suíte com o HMAC recusado
- **WHEN** a validação do token HMAC do `apps/connectors` passa a recusar todo
  token e a suíte de `apps/connectors/tests` roda
- **THEN** reprovam só os casos das listas da #182, por nome

#### Scenario: Fábrica padrão sem agendamento do ciclo
- **WHEN** um caso usa a fábrica sem pedir `Api:BaseUrl`
- **THEN** nenhum pedido chega ao `apps/api` falso nem ao `/connect/token` falso
  depois do boot
