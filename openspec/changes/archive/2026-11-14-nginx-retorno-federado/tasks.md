Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-285.sh` reintroduz o defeito nomeado. A mutação toca só o trecho nomeado (o bloco do `nginx.conf`, uma entrada de lista ou a linha do fixture) e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova, **nomeando** o caminho ou o bloco;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação (o trecho que nomeia o item) e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação, que reprova por "no tests" ou que reprova sem nomear o item não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/285-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-285.sh`, **uma por vez, com o lock** (`suites.lock`; o guarda e a conferência da 4 usam o mesmo).
- Suítes: `api` (os testes novos e o de cobertura), `inbox` e `connectors` (os testes de cobertura leem o mesmo `nginx.conf`) e `frontend` (o `nginxHeaders.test.ts` também), com o RabbitMQ de pé (`PAUSAR_RABBIT=0`).
- Fora: `workers` (sem rota HTTP), `rt-inbox`, `rt-connectors` e `rt-crossapp` (nenhum código de produção muda, e os acordos não passam pelo nginx).
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim; senão é inconclusiva e se repete. Reprovação por caso que esta change mudou não é inconclusiva: registra-se "reprovada pelo X (a → b), corrigida".
- Antes de cada rodada, o load de 1 e de 5 min abaixo do limite do script. Nunca `podman build` nem `dotnet build` junto com uma suíte.

A comparação final é por classe + nome (`.trx`) e por arquivo + nome (`frontend.json`), com a contagem por linha e por nome distinto.

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura (por exemplo: o teste da 2.2 reprovar contra o `nginx.conf` de hoje por outro caminho além do retorno, ou outro caminho de navegação de topo aparecer no código);
- **parar** quando a conferência da 4 não der o `302` do `apps/api` na imagem da change, ou não der o `200` com HTML na da baseline;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Nada de commit** até o archive; um commit só, depois dele, e o PR com `Closes #285`.

## Casos que saem ou mudam de asserção

Nenhum. Os casos novos são os da 1.2 e da 2.1. O `NginxRouteCoverageTests` do `apps/api` não muda de nome nem de asserção: a lista `ExactBlocks` ganha uma entrada (3.2). O `nginxHeaders.test.ts` continua igual: o bloco novo não tem `add_header`, e o leitor dele olha só o `server` e o `location = /index.html`.

## 0. Preparação

- [x] 0.1 (repo) Issue #285 (`bug`, `tipo: bug`, `app: frontend`, milestone M3) em `In progress` no board, no checklist da #196, e o comentário na #189; worktrees `../buteco-agentes-285` (`fix/285-nginx-retorno-federado`) e `../buteco-agentes-285-baseline` (detached), os dois em `f503aca`.
- [x] 0.2 (scripts locais) `~/.cache/buteco-agents/bin/suite-285.sh` e `guarda-285.sh`, cópias do `suite-268.sh` e do `guarda-268.sh` (só o cabeçalho muda). Conferir o guarda com uma mutação nula (recusada com "defeito não aplicado") e uma real no `nginx.conf`, que volta com o sha igual.
- [x] 0.3 (baseline) `suite-285.sh` sobre o worktree da baseline: `api inbox connectors frontend`, com a saída completa em `~/.cache/buteco-agents/285/baseline/`. Registrar no design o resultado, a duração e a CPU mínima de cada uma.

## 1. O leitor (tests/Shared, design D3)

- [x] 1.1 (tests/Shared) `NginxRouting.FindNavigationProblems(paths, locations, upstream)`: para cada caminho, um `location =` de mesmo padrão, encaminhando ao `upstream`, sem o desvio de navegação; senão, uma linha que nomeia o caminho e o que falta (sem bloco exato; bloco exato de outro upstream; bloco exato com o desvio). Lista vazia é tudo certo.
- [x] 1.2 (apps/api) Casos do leitor em `NginxRoutingReaderTests`, sobre um texto de `nginx.conf` forjado: o caminho com o bloco exato certo não dá problema; sem bloco exato (só o bloco de regex com o desvio), com o desvio no bloco exato, e de outro upstream, cada um dá a linha que nomeia o caminho.

## 2. O guarda antes da correção (apps/api, design D3 e D6)

- [x] 2.1 (apps/api) `NginxNavigationEndpointsTests` com o `FederatedLoginFixture`: a lista do código (o `FederatedLogin.CallbackPath` mais os caminhos de `RedirectionEndpointUris` e `PostLogoutRedirectionEndpointUris` das `OpenIddictClientOptions` do host) e o `FindNavigationProblems` contra o `nginx.conf` do repositório. O caso reprova se a lista do cliente sair vazia. Todas as divergências numa falha só.
- [x] 2.2 **(guarda)** "Caminho de navegação de topo sem bloco exato", contra o **defeito real**: o teste da 2.1 roda com o `nginx.conf` do `main` e reprova nomeando `/auth/federated/callback`, e só ele. A rodada é com o `guarda-285.sh` sem mutação (filtro na classe) e fica registrada antes da 3.1. Se reprovar por outro caminho, parar.

## 3. A correção (apps/frontend, design D1)

- [x] 3.1 (apps/frontend) `location = /auth/federated/callback` depois do `location = /connect/logout`, com o corpo do `/connect/authorize` e sem o desvio, e o comentário do D1. O comentário do topo passa a listar os cinco blocos exatos do `apps/api`.
- [x] 3.2 (apps/api) `/auth/federated/callback` na lista `ExactBlocks` do `NginxRouteCoverageTests`, com o motivo do D3.
- [x] 3.3 (apps/api) Os testes da 2.1 e o `NginxRouteCoverageTests` aprovam com a correção, pelo `guarda-285.sh` sem mutação; o `nginxHeaders.test.ts` aprovado (só esse arquivo, pelo `npx vitest run`).
- [x] 3.4 **(guarda)** "Bloco exato com o desvio": o `if ($http_sec_fetch_mode = navigate)` acrescentado ao bloco novo → o teste da 2.1 reprova nomeando o caminho (e o de cobertura, pela exceção exata com o desvio; os dois registrados).
- [x] 3.5 **(guarda)** "Bloco exato de outro upstream": o `set $api_upstream api:8080` do bloco novo trocado por `set $inbox_upstream inbox:8080`, com o `proxy_pass` correspondente → o teste da 2.1 reprova nomeando o caminho.
- [x] 3.6 **(guarda)** "A lista do código vazia": o teste da 2.1 sobre o `LocalUsersFixture` (sem provedor) no lugar do `FederatedLoginFixture` → reprova pela lista do cliente vazia, e não fica verde por não ter o que conferir.
- [x] 3.7 **(guarda)** "O bloco novo fora das exceções": a entrada da 3.2 tirada do `ExactBlocks` → o `NginxRouteCoverageTests` reprova nomeando o bloco.

## 4. Conferência com o nginx de verdade (apps/frontend, design D4)

Sem suíte rodando: o script da conferência toma o mesmo lock. A troca do `resolver` dentro do contêiner (o contorno da #212) fica registrada na saída.

- [x] 4.1 (apps/frontend) `podman build` do `apps/frontend/Dockerfile` na árvore da change e na da baseline; `nginx -t` dentro das duas, com a saída em `~/.cache/buteco-agents/285-conferencia/`.
- [x] 4.2 (apps/api) O `apps/api` local (`dotnet run`, `TZ=America/Sao_Paulo`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1`, um banco descartável), com um provedor Google de credenciais falsas, e a imagem do frontend numa rede do Podman, com o contêiner `api` encaminhando a porta `8080` ao host. Registrar se o retorno com o state inválido pediu rede; se pediu, a variante sem provedor (D4), registrada.
- [x] 4.3 (apps/frontend) Os `curl` contra a imagem da change, com o status, o `Location`, o `Content-Type`, o `Referrer-Policy` e o começo do corpo guardados:
  - `GET /auth/federated/callback?state=x&code=y` com `Sec-Fetch-Mode: navigate` → `302`, `Location: /entrar?recusa=falha`, `Referrer-Policy: no-referrer`;
  - o mesmo sem `Sec-Fetch-Mode` → o mesmo `302`;
  - `GET /auth/federated/providers` com `Sec-Fetch-Mode: navigate` → `200`, `text/html`, `Cache-Control: no-store`; sem o cabeçalho → o JSON do `apps/api`;
  - `GET /account?conectado=x` com `Sec-Fetch-Mode: navigate` → o `index.html`;
  - `GET /auth/federated/callbackxyz` com `Sec-Fetch-Mode: navigate` → o `index.html`.
- [x] 4.4 (apps/frontend) O contraste: a imagem da baseline responde ao primeiro pedido da 4.3 `200` com `text/html`. No fim, o banco, os contêineres, a rede e as imagens apagados.

## 5. Documentação (design D5)

- [x] 5.1 `docs/deployment.md`, na seção da #189, em "Conferir": a conferência com navegação (`curl` com `Sec-Fetch-Mode: navigate` no retorno, `302` para `/entrar?recusa=falha`; `200` é o defeito), ao lado das conferências por `curl`, com o porquê. A nota de deploy da #285.
- [x] 5.2 `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md` (a entrada, a baseline e o fechamento, os guardas com a reprovação vista, a conferência da 4 e a medição da convenção 18), **antes do archive** e dentro do PR.

## 6. Fechamento

- [x] 6.1 Rodadas `api`, `inbox`, `connectors` e `frontend` pelo `suite-285.sh`, uma por vez, com a árvore da change. Comparação por classe + nome com a baseline: nenhum caso some nem muda de resultado; os novos são só os da 1.2 e da 2.1, listados.
- [x] 6.2 A medição da convenção 18 contra o D6, e o design com "O que o apply mudou" (convenção 9), se houver.
- [x] 6.3 Nenhum código de produção dos apps no diff (`git diff --stat` só com `apps/frontend/deploy/nginx.conf`, `tests/Shared/`, o projeto de teste do `apps/api`, `openspec/` e os documentos). `openspec validate nginx-retorno-federado --strict`.
- [x] 6.4 (openspec) O archive com o sync da spec `server-deployment`.
- [x] 6.5 (git) Um commit só, depois do archive; o push; o PR com `Closes #285`, citando a #189 e a #196; a #285 em In review.
