## Why

Desde a #184 (`a00966a`, 09/11/2026), `POST /agents/{id}/a2a` só aceita o token de serviço do
`apps/inbox`: o token do painel recebe `403` com qualquer papel, e não existe outra credencial.
Nenhum sistema de fora do Buteco chama um agente pelo A2A hoje. A #186 cria a **aplicação
externa**: um cliente do emissor do `apps/api` (#179), cadastrado pelo Admin, com client ID e
segredos gerados pelo Buteco, que obtém token por `client_credentials`. Qual agente cada
aplicação alcança é da #187; até ela, a aplicação tem credencial e não alcança nenhum agente
(D2).

## What Changes

- **Cadastro de aplicação externa no `apps/api`**, só do Admin, inclusive ler: listar (busca por
  nome ou client ID, filtro por estado), ver, criar, editar nome e descrição, desativar e
  reativar. Não há exclusão.
- **Client ID gerado** no formato `btc_app_` + 8 caracteres; o prefixo `buteco-` nunca é gerado
  nem aceito, e a aplicação só recebe o escopo `buteco-api`.
- **Segredos próprios**, fora do `ClientSecret` do OpenIddict: gerados pelo Buteco, mostrados uma
  vez, guardados como SHA-256; vários por aplicação, validade padrão de 365 dias; gerar um novo
  deixa os anteriores valendo por mais 7 dias; revogar recusa o segredo no próximo pedido de
  token. Criar a aplicação e gerar segredo pedem a senha de quem chama.
- **O emissor autentica a aplicação pelos segredos dela** (gerente de aplicações do OpenIddict
  estendido), recusa a aplicação desativada, e emite o access token com **5 min** de duração, no
  principal, sem mudar a do painel (15 min).
- **O `apps/api` reconhece o token da aplicação** pelo subject `application`, fora da tabela
  fechada de clientes, numa entrada da tabela de subjects sem papel. Nesta change a lista de
  rotas dele é **vazia**: o token autentica e recebe `403` em toda rota, inclusive na A2A. A #187
  acrescenta `POST /agents/{id}/a2a` junto com o filtro por agente.
- **Último uso** gravado na emissão do token, por segredo e por aplicação.
- **AgentCard**: `securitySchemes` passa a declarar o esquema OAuth2 do Buteco (fluxo
  `clientCredentials`, `tokenUrl` público, escopo `buteco-api`). O card continua anônimo.
- **Checagem de boot da retenção das chaves** passa a contar a duração do token de aplicação.
- **Checagem de boot das rotas**: lista vazia de subject sem papel derruba o boot, com a exceção
  temporária de `application` até a #187.
- **Docs**: `docs/a2a-integration.md` (credencial, token, o pedido sem `scope` recusado com `401`, o que ainda não funciona até a #187),
  `01` (Autenticação), `docs/deployment.md` (a volta atrás).

## Capabilities

### New Capabilities

- `external-applications`: cadastro de aplicação externa, client ID, segredos (geração, hash,
  validade, sobreposição, revogação), desativação e reativação, último uso, as rotas do Admin.

### Modified Capabilities

- `oidc-issuer`: o grant `client_credentials` da aplicação externa, autenticada pelos segredos
  dela, com 5 min de duração e sem `refresh_token`.
- `route-roles`: as rotas `/applications/*` na matriz (só Admin, com o papel gravado), e o subject
  `application` na checagem de boot.
- `route-authentication`: o `apps/api` passa a mapear o subject `application`; com lista vazia,
  ele recebe `403` em toda rota.
- `a2a-agent-card`: o esquema OAuth2 em `securitySchemes`.
- `identity-signing-keys`: a retenção das chaves cobre também a duração do token de aplicação.

## Impact

- **Código:** só o `apps/api` (cadastro, emissor, validação do token, AgentCard). O `apps/inbox`
  e o `apps/connectors` não mudam: a tabela de clientes deles é fechada, e a aplicação só recebe
  a audience `buteco-api`.
- **Banco:** migration com duas tabelas novas (`external_applications`,
  `external_application_secrets`); cada aplicação cria uma linha em `openiddict_applications`.
- **Contrato:** rotas novas `/applications/*`; a resposta do AgentCard muda em `securitySchemes` e
  `securityRequirements`.
- **Suítes:** `api`, `rt-inbox`, `rt-connectors` (`~/.cache/buteco-agents/bin/suite-186.sh`).
- **Telas:** da #188, que consome estas rotas, contra o protótipo na versão 60. Os estados sem quadro
  (lista vazia, erros, confirmações, senha, desativada) são desenhados no design da #188; o contrato
  de cada um (status, `title`, campo) está nas specs desta change.
- **#187:** a exceção da lista vazia de `application` na checagem de boot é temporária, e o teste que
  afirma `403` em toda rota para o token de aplicação muda de asserção lá (registrado na #187).
- **Decisões:** Q1–Q15 do design aceitas pelo dono em 09/11/2026, com os guardas da D9.
