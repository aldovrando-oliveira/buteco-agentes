**Issue:** #104

## Why

O catálogo do `apps/api` já sabe guardar uma base sincronizada (#102) e o
`apps/connectors` já descreve uma pasta do Google Drive para o subject
`service:api` (#103), mas nenhuma rota do operador cria base `Synced`: o
`POST /knowledge-bases` recusa `contentMode: "Synced"` com `400`. Falta ligar as
duas pontas, com a pasta validada por quem de fato acessa o provedor, e com o
nome e a URL da pasta vindos dessa validação, nunca do frontend, que não é fonte
confiável. É a peça que destrava a #105 (ciclo) e a #106 (tela de cadastro).

## What Changes

- **`apps/api`, cadastro:** `POST /knowledge-bases` passa a aceitar
  `contentMode: "Synced"` com `provider` e `folderId`, ambos obrigatórios em
  `Synced` e proibidos em `Manual`, recusados com `400` antes de qualquer chamada
  externa. O requisito da #102 que recusava `Synced` com `400` é substituído.
- **`apps/api`, validação da pasta:** o `apps/api` chama
  `GET /connectors/providers/{provider}/folder?id=` do `apps/connectors`,
  assinando um token `service:api`, e grava o nome e a URL da pasta que vêm da
  resposta. `folderName` e `folderUrl` enviados no corpo são ignorados.
- **`apps/api`, pasta em uso:** a pasta já usada por outra base, ativa ou inativa,
  responde `409` com o id e o nome da base que a usa, inclusive na corrida entre
  dois cadastros simultâneos. A mensagem diz que a pasta continua ocupada mesmo
  com a base inativa e **não** manda excluir a base: a exclusão é da #108.
- **`apps/api`, falhas do `apps/connectors`:** o código e o detalhe do
  `apps/connectors` (`access-denied` com o e-mail da conta, `not-a-folder`,
  `folder-trashed`, `provider-not-configured`, `api-not-configured`,
  `rate-limited`, `provider-unavailable`, ...) chegam ao cliente como código, sem
  tradução para frase, com status por natureza. `apps/connectors` fora do ar,
  lento ou fora do contrato vira recusa explícita com código próprio do `apps/api`,
  nunca base criada às cegas e nunca `500`.
- **`apps/api`, configuração opcional:** `Connectors__BaseUrl`. Sem ela o
  `apps/api` sobe e nenhuma outra rota muda; só o cadastro `Synced` é recusado,
  com código próprio. Presente e inválida, o boot falha. Em produção a variável só
  chega com a #119.
- **`apps/api`, token de serviço de saída:** `DelegatingHandler` próprio que
  assina `sub: "service:api"` com a mesma `Auth:TokenSigningKey` e TTL fixo de
  5 minutos, duplicado do `ServiceTokenDelegatingHandler` do `apps/inbox`, sem
  `libs/`.
- **`tests/`, ida e volta:** projeto novo que sobe o `apps/api` e o
  `apps/connectors` reais (com o conector falso) e prova o cadastro pela chamada
  real entre os dois, sem chamada ao Google. **Depende de autorização do
  mantenedor** para uma fonte de contêiner nova (Open Questions do `design.md`).
- **Documentação:** `docs/configuration.md`, `.env.example`,
  `docs/architecture.md`, `01`, `02` e `CHANGELOG.md`. A variável nova de
  produção é registrada na #119, por comentário. Entram também a correção da
  **#127** (`apps/connectors` e `Connectors.sln` na estrutura do
  `docs/conventions.md`, achado desta change no bloco que ela já editava, fechado
  pelo PR com `Closes #127`) e o ajuste da regra do `Closes` na convenção 24 do `01`
  (achado corrigido por inteiro no mesmo PR entra como `Closes`, em linha própria).

### O que esta change NÃO faz, e é decisão

- **Telas:** #106 (seletor de pasta e mensagens por código).
- **Sincronização:** #105. A base nasce `Synced` sem nenhum ciclo, com
  `syncState` nos valores de "nunca sincronizou".
- **Exclusão de base:** #108. Por isso a mensagem do `409` não fala em excluir.
- **Implantação do `apps/connectors` em produção:** #119. Nada em
  `docker-compose.prod.yml`, `.env.prod.example`, nginx do stack ou
  `deployment.md`.
- **Nenhuma mudança no `apps/connectors`:** a rota de descrição e o subject
  `service:api` já existem desde a #103.

## Capabilities

### New Capabilities

- `knowledge-sync-folder-validation`: a chamada do `apps/api` ao
  `apps/connectors` para validar a pasta: configuração opcional, token
  `service:api`, limite de tempo, repasse de código e status de cada falha, e a
  prova de ida e volta entre os dois apps.

### Modified Capabilities

- `knowledge-base-catalog`: o cadastro aceita `Synced` com provedor e pasta
  validados e grava os snapshots da validação (substitui a recusa com `400`); a
  pasta em uso responde `409` com a base que a usa, sem `500` na corrida.

## Impact

- **`apps/api`:** endpoint e comando de cadastro de base, cliente HTTP nomeado do
  `apps/connectors` com o `DelegatingHandler` de `service:api`, opções e checagem
  de boot da configuração, `appsettings*.json`, e testes.
- **Contrato HTTP:** `POST /knowledge-bases` ganha `provider` e `folderId`, e
  passa a responder `409`, `422`, `502` e `503` em base `Synced`, com
  `ProblemDetails` e a extensão `code`. O cadastro `Manual` não muda, salvo o
  `400` novo quando traz `provider` ou `folderId`.
- **`apps/connectors`:** nenhuma mudança de código; passa a ter o primeiro
  consumidor da rota de descrição de pasta.
- **Banco:** nenhuma migração. O índice de pasta e as colunas são da #102.
- **Sem dependência nova.** O projeto de teste novo usa pacotes já fixados em
  `Directory.Packages.props`.
- **Testes:** casos novos em classes de `apps/api` que já existem (`partial`),
  testes sem contêiner para o cliente, o token e a checagem de boot, e o projeto
  de ida e volta em `tests/`, que é a única fonte de contêiner nova.
