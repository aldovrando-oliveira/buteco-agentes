**Issue:** #105

## Why

Uma base sincronizada nasce pela #104 com a pasta validada, mas nada a mantém em dia:
o `apps/api` tem as rotas de `/sync` desde a #102 e o `apps/connectors` sabe listar a
raiz e entregar o markdown desde a #103, e nenhum processo liga os dois. Sem o ciclo,
a base fica vazia para sempre, e o operador não tem como forçar uma sincronização.

## What Changes

- **Ciclo periódico no `apps/connectors`**, a cada 5 minutos, sobre toda base
  `Synced` (inclusive inativa), uma base por vez:
  1. descreve a pasta; falha de acesso grava `Failed` com o código e não exclui nada;
  2. lista a raiz por completo;
  3. compara com as referências e marcadores gravados no `apps/api` e baixa só o
     que mudou de marcador;
  4. faz o upsert do que é novo ou mudou;
  5. exclui por referência o que sumiu da pasta — só o que sumiu da listagem
     inteira, e só com a pasta lida e a listagem completa;
  6. grava o resultado do ciclo, com a lista de ignorados.
- **Falha de arquivo não derruba a base:** markdown que não pôde ser obtido e recusa
  de conteúdo do `apps/api` (os quatro códigos da #120) viram arquivo ignorado com o
  código, e o documento existente continua como está.
- **"Sincronizar agora":** `POST /connectors/knowledge-bases/{id}/sync`, só para o
  operador, responde `202` e roda o ciclo daquela base em segundo plano. Não dispara
  um segundo ciclo da mesma base enquanto um estiver rodando nesta instância.
- **Chamada ao `apps/api`** por um `DelegatingHandler` próprio que assina
  `service:connectors`, par do `service:api` da #104.
- **Configuração nova e opcional, `Api:BaseUrl`.** Sem ela o `apps/connectors` sobe
  como hoje, com o ciclo desligado e aviso no log; presente e inválida, o boot falha.
- **Testes de ida e volta no sentido novo**, `apps/connectors` → `apps/api`, no
  projeto `tests/ApiConnectorsRoundTrip.Tests`.

Nenhuma mudança no `apps/api`, no `apps/frontend` nem no stack de produção.

## Capabilities

### New Capabilities

- `knowledge-sync-cycle`: o ciclo de sincronização por reconciliação no
  `apps/connectors` — o agendamento, a ordem dos passos, o que exclui e o que nunca
  exclui, onde cada falha cai, a chamada ao `apps/api` como `service:connectors`, a
  configuração opcional e o "Sincronizar agora".

### Modified Capabilities

- `connectors-api`: a rota nova `POST /connectors/knowledge-bases/{id}/sync` entra na
  lista de rotas e na tabela de subjects, só para `operator`.
- `connector-plugin`: o conector falso da composição de teste passa a ter a listagem
  e o markdown configuráveis pelo teste, para o ciclo ser testado sem o Google.

## Impact

- **`apps/connectors`**: ciclo, agendamento, cliente do `apps/api`, handler de token,
  validação de configuração no boot, rota nova e tabela de subjects. Nenhum pacote
  novo.
- **`apps/api`**: nenhuma linha. O ciclo consome as rotas de `/sync` da #102 e os
  códigos de recusa da #120 como estão.
- **`tests/ApiConnectorsRoundTrip.Tests`**: cenários no sentido
  `apps/connectors` → `apps/api`, na fixture existente, sem fonte nova de contêiner.
- **Produção:** nada nesta change. A configuração que o `apps/connectors` vai
  precisar foi registrada na #119 por comentário.
- **Segurança:** o `apps/connectors` passa a **escrever** no `apps/api`. A chave
  compartilhada que ele já guarda continua sendo a mesma (#117).
- **Documentação**: `docs/architecture.md`, `docs/configuration.md`,
  `docs/development.md`, `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md` e
  `CHANGELOG.md`.
