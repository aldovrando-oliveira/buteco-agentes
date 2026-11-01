**Base:** `origin/main` em `ca2bd71` (merge do PR #224), 31/10/2026.
- **Worktree da change:** `../buteco-agentes-217`, branch `feat/217-painel-entra-pelo-emissor`.
- **Baseline:** worktree limpo separado, `../buteco-agentes-217-baseline`, no mesmo commit, sem edição nenhuma.

**Saídas completas:** `.log` inteiro, `.trx` ou relatório JSON do Vitest, e estado da máquina colado ao número, nunca filtrados (convenção 19), em:
- `~/.cache/buteco-agents/217-baseline/`;
- `~/.cache/buteco-agents/217-verificacao/`;
- `~/.cache/buteco-agents/217-fechamento/`.

**Variáveis em toda rodada .NET:** `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`.

**Antes de cada suíte**, inclusive as do `apps/frontend`, e não só da primeira, o script `~/.cache/buteco-agents/bin/suite-217.sh` confere:
- `CPU_Speed_Limit = 100`;
- load de 1 min abaixo de 6 no `apps/frontend` e de 8 nas outras;
- relógio da VM acertado;
- nenhum contêiner em `podman ps`.

**O que muda nesta change:**
- `apps/frontend`: o módulo de sessão, `/entrar`, `/entrar/retorno`, a casca, as 7 cópias do `request<T>`, o `queryClient`, o `vite.config.ts`, o lint e o `deploy/nginx.conf`;
- `apps/api`: a remoção do `POST /auth/login` e do que só ele usava, e um teste novo da tolerância do refresh;
- `tests/InboxOrchestratorRoundTrip.Tests`: a fixture deixa de logar pelo `POST /auth/login`;
- o compose de produção e os `.env*.example`.

O `apps/inbox`, o `apps/connectors` e o `apps/workers` não mudam.

**Convenção 15 em todo guarda:** escrever o teste, reintroduzir o defeito nomeado na tarefa, ver reprovar **no componente que a correção toca**, guardar a saída em `217-verificacao/guardas/<tarefa>.log`, voltar a árvore e ver aprovar. A tarefa só é marcada com as duas saídas registradas nela.

**Commit:** nenhum commit parcial, nem só da documentação ou dos artefatos do OpenSpec. O único commit da change acontece depois do sync e do archive (11.8), com tudo junto. Push e PR só com o ok do dono naquela mensagem.

**Paradas obrigatórias:**
- **grupo 2:** qualquer verificação que reprovar;
- **em qualquer grupo:** qualquer premissa do design que se mostrar falsa (convenção 9).

Nos dois casos, a decisão volta ao dono antes de seguir.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/frontend`, `apps/api`, `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests`) Baseline no worktree limpo em `ca2bd71`.

      **Resultado, 31/10/2026, worktree `../buteco-agentes-217-baseline`, sem edição nenhuma.** O script esperou `CPU_Speed_Limit = 100` e o load antes de cada suíte:

      | suíte | resultado | duração | estado (início → fim) |
      |---|---|---|---|
      | `apps/frontend` | **1839/1839**, 138 arquivos | 151 s | CPU 100 → 48; load 5,61 → 25,75 |
      | `apps/api` | **1009/1009 + 1 ignorado** | 217 s | CPU 100 → 48; load 6,19 → 13,21 |
      | `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 51 s | CPU 100 → 55 |
      | `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 22 s | CPU 100 → 83 |

      Iguais por número aos fechamentos da #180. A CPU caiu durante as quatro, sempre depois de largar em 100, então as durações não servem de referência. As saídas estão em `217-baseline/`.

- [x] 1.2 (`apps/frontend`) Repetir a baseline do `apps/frontend` no mesmo worktree limpo, pelo mesmo script, com `--reporter=default --reporter=json --outputFile=217-baseline/frontend.json`. O `frontend.log` da 1.1 só tem os totais, e o fechamento (11.1) compara por nome. Se o total divergir de 1839/1839, parar e investigar antes de seguir (convenção 19).

      **Resultado, 01/11/2026.** O script ganhou o relatório JSON no caso `frontend`; as saídas da 1.1 foram renomeadas para `frontend-1.1.log` e `frontend-1.1-estado.txt`.
      - **1ª rodada (`frontend-1.2a.*`): 1837/1839**, 2 reprovados, os dois no `KnowledgeBaseCreatePage.sync.test.tsx` ("cadastro: connectors-error no card de Origem" e "cadastro: provider-not-configured no card de Origem"), ambos `Test timed out in 15000ms`, sem asserção reprovando. Início: load 2,21, CPU 100, `podman ps` vazio. Fim: load 26,61, **CPU 53**. É a forma da convenção 19 já registrada (timeout sob carga que a própria suíte cria), e não defeito.
      - **2ª rodada (`frontend.json`, `frontend.log`): 1839/1839**, 138 arquivos, 150 s. Início: load 5,91, CPU 100, `podman ps` vazio. Fim: load 27,46, CPU 51. É esta a baseline por nome do 11.1.
      - **Registro (convenção 23, decisão do dono de 01/11/2026):** a #210 é de um teste do `apps/api` e não cobre este. Aberta a **#226**, sem milestone, com as duas rodadas, a queda da CPU e o que o JSON mostra: mesmo na rodada aprovada, os casos da tabela "texto de cada código" levam de 9 a 10,5 s, e o arquivo leva 122 s.

## 2. Verificação antes de qualquer código (parada obrigatória se reprovar)

- [x] 2.1 (`apps/api`, D3, H2) Em `Identity/RefreshRotationTests.cs`, o caso novo: o mesmo refresh apresentado duas vezes ao mesmo tempo (`Task.WhenAll`), as duas respostas `200` com pares novos, e o refresh novo de cada uma renova de novo com `200` (`issuer-user-login`, "Duas renovações simultâneas do mesmo refresh dão dois pares"). **Guarda:** com a tolerância zerada na fixture do teste (`SetRefreshTokenReuseLeeway(TimeSpan.Zero)`), o caso reprova; e o `ReuseAfterTheLeeway_RevokesTheChain`, que já existe, continua aprovando. Se o caso reprovar **sem** o defeito, a premissa H2 caiu: parar.

      **Resultado, 01/11/2026.** O H2 se confirmou, e o caso como estava escrito não o provava (convenção 15 fazendo o trabalho dela):
      - **1ª forma, com `Task.WhenAll`:** aprovou **também com a tolerância zerada** na fixture. Pela fonte, a tolerância só é consultada quando o refresh já está marcado como resgatado (`OpenIddictServerHandlers.Protection.cs` l. 1245–1247 e 1366–1369), e dois pedidos de fato simultâneos passam os dois pela validação antes do resgate. O caso aprovaria com qualquer tolerância. Essa rodada saiu no mesmo arquivo de log da seguinte, que a sobrescreveu, e ficou só nesta nota;
      - **forma final** (`SameRefreshAgainWithinTheLeeway_IssuesASecondPair_AndBothKeepRenewing`): o primeiro refresh responde `200`, o relógio avança 10 s, e o mesmo refresh responde `200` de novo (é a segunda aba que chega depois do resgate, o caso que o painel realmente encontra). O refresh de cada par renova com `200`. O cenário do `issuer-user-login` e a linha do D12 foram reescritos com essa forma;
      - **guarda** (`guardas/2.1-reprova.log`): `server.SetRefreshTokenReuseLeeway(TimeSpan.Zero)` no `LocalUsersFixture` → o caso reprova na l. 90 (`Expected: OK`, `Actual: BadRequest`), e `ReuseAfterTheLeeway_RevokesTheChain` continua aprovando;
      - **de volta** (`guardas/2.1-aprova.log`): 4/4 da classe.

- [x] 2.2 (`apps/frontend`, D2) `npm run build` e conferir que o script inline do `index.html` chega ao `dist/index.html` byte a byte, com o SHA-256 em base64 igual nos dois. Registrar o hash. Se o Vite reescrever o script, a forma do hash na CSP muda: parar.

      **Resultado, 01/11/2026** (`217-verificacao/2.2-build.log` e `2.2-e-2.5-inline.txt`): `npm run build` sem erro. O `<script data-mantine-script>` chega ao `dist/index.html` com os mesmos 598 caracteres e o mesmo hash nos dois arquivos: **`sha256-II5F/q4jI3zI9g9/17tVWddqwse6H5qdSUB46UEQ1Rg=`**. O Vite não reescreve o script.

- [x] 2.3 (`apps/frontend`, D6) Num teste de componente com `createMemoryRouter`, confirmar que `navigate('/entrar/retorno', { replace: true })` a partir de `/entrar/retorno?code=x&state=y` mantém o componente montado (uma `ref` sobrevive) e deixa `historyAction` em `REPLACE`. Esse teste vira o da 4.2. Se o componente remontar, o processamento por valores capturados do D6 muda: parar.

      **Resultado, 01/11/2026** (`217-verificacao/2.3-e-3.1.log`): com `createMemoryRouter` em `/entrar/retorno?code=x&state=y`, o `navigate('/entrar/retorno', { replace: true })` deixa `search` vazio, `historyAction` em `REPLACE`, e o componente com a mesma instância (a `ref` criada antes do `navigate` é a que continua; uma montagem só). O teste é temporário, em `src/app/replaceProbe.test.tsx`, e as asserções passam ao `EntrarRetornoPage.test.tsx` na 4.2, quando ele sai.

- [x] 2.4 (`apps/api`, `frontend-app-shell`; acréscimo do dono, 01/11/2026) O id token traz `name` e `email` com os escopos que o painel pede (`openid offline_access buteco-api buteco-inbox buteco-connectors`, sem `profile` nem `email`)? Conferir as destinations no `UserPrincipalFactory` da #180, com arquivo e linha, e num id token real emitido pelo fluxo do `IdentityIssuerTestClient`. Se `name` ou `email` não saírem: parar e levar ao dono as opções (pedir os escopos `profile` e `email`, que exigem registrá-los no catálogo, ou mudar as destinations), sem escolher.

      **Resultado, 01/11/2026: `name` e `email` saem com os escopos do painel.**
      - **Destinations:** `apps/api/src/Buteco.Api/Identity/Flow/UserPrincipalFactory.cs` l. 35–41. `Claims.Email or Claims.Name => [Destinations.IdentityToken]` (l. 38), sem condição de escopo. `sub` vai aos dois tokens (l. 37), e o carimbo a nenhum (l. 39);
      - **teste que já fixa:** `UserLoginFlowTests.FullFlow_IssuesTokensForTheThreeApps` (l. 74–76) confere `email` e `name` no id token, com `IdentityIssuerUserFlow.AllScopes` (`IdentityIssuerTestClient.cs` l. 118), que é exatamente `openid offline_access buteco-api buteco-inbox buteco-connectors`;
      - **id token real** (teste temporário, apagado depois; saída em `217-verificacao/2.4-id-token.txt`), pelo `IdentityIssuerUserFlow.SignInAsync` e por uma renovação:
        - troca do código: `{"iss":"https://api.exemplo.test/","exp":…,"iat":…,"aud":"buteco-painel","sub":"01a19887-…","email":"tmp217-…@exemplo.test","name":"Ana Souza","oi_au_id":"…","azp":"buteco-painel","at_hash":"…"}`;
        - renovação: os mesmos `email` e `name`, com outro `at_hash`;
        - o access token não tem `name` nem `email`.

- [x] 2.5 (`apps/frontend`, D2; acréscimo do dono, 01/11/2026) No `dist/index.html` do `npm run build`, listar todo `<script>` e `<style>` inline e todo atributo de evento inline (`on*=`). O único permitido é o script do tema, cujo hash vai na CSP. Se houver outro: parar e levar ao dono a origem dele.

      **Resultado, 01/11/2026** (`217-verificacao/2.2-e-2.5-inline.txt`), no `dist/index.html` do build da 2.2:
      - `<script>` inline: **um**, o `data-mantine-script` (o tema), com o hash da 2.2. O outro `<script>` é o `type="module" src="/assets/index-….js"`, que não é inline;
      - `<style>` inline: nenhum;
      - atributo de evento (`on…=`): nenhum. Nem `style=` nem `javascript:`;
      - de passagem, o bundle (`assets/index-….js`) não tem `eval(` nem `new Function(`, que o `script-src 'self'` bloquearia.

      O Mantine grava `<style>` em tempo de execução (as variáveis de CSS do `MantineProvider`) e `style=` nos componentes, e é por isso que a CSP do D2 tem `style-src 'self' 'unsafe-inline'`. Não há script em tempo de execução.

## 3. O módulo de sessão (`apps/frontend/src/auth/`, D2–D7, D13)

- [x] 3.1 `pkce.ts` e `pkce.test.ts`: verifier de 32 bytes em base64url (43 caracteres), challenge S256, `state` de 32 bytes. Teste com o vetor do apêndice B da RFC 7636 e o alfabeto. **Guardas:** challenge em base64 padrão (com `+/=`) reprova o vetor; o verifier devolvido como challenge reprova o vetor.

      **Resultado, 01/11/2026:** `pkce.test.ts`, 4/4. Guardas, cada um com `-reprova.log` e `-aprova.log` em `217-verificacao/guardas/`:
      - `3.1-base64-padrao` (`btoa` sem trocar `+/` nem tirar `=`): os 4 casos reprovam, o do vetor da RFC entre eles;
      - `3.1-verifier-como-challenge`: reprova o caso do vetor.

- [x] 3.2 `sessionStore.ts` e teste: o registro `buteco.sessao` no `localStorage` (`accessToken`, `accessExpiresAt`, `refreshToken`, `idToken`, `nome`, `email`), com `nome` e `email` do payload do id token; registro ilegível é ausente; `subscribe` pelo evento `storage`. O storage é injetável. **Guardas:** o parse sem `try` faz o caso do registro corrompido reprovar; sem o ouvinte, o caso "registro apagado por outra aba" reprova.

      **Resultado, 01/11/2026:** `sessionStore.ts` e `sessionStore.test.ts`, 8/8. O `subscribe` avisa também as mudanças da própria aba (o evento `storage` só dispara nas outras), e o `ProtectedRoute` usa isso na 4.3. Guardas em `217-verificacao/guardas/`:
      - `3.2-parse-sem-try`: reprova "registro corrompido é ausência, sem lançar";
      - `3.2-sem-ouvinte` (sem o `addEventListener('storage')`): reprova "avisa quando o registro é apagado por outra aba".

- [x] 3.3 O gerenciador de lock injetável (padrão `navigator.locks`) e o lock falso de teste em `src/test/`, **com exclusão mútua real** entre instâncias que o compartilham. Teste do próprio lock falso: dois pedidos sobrepostos rodam em série. **Guarda:** um lock falso que só chama o callback faz esse teste reprovar. É o que impede o teste da 3.4 de passar por um dublê sem exclusão (convenção 15, guarda no lugar errado).

      **Resultado, 01/11/2026:** `auth/runtime.ts` (o `LockManagerLike` e o resto do que o módulo usa do navegador, lido na hora da chamada), `test/fakeLocks.ts` e `fakeLocks.test.ts`, 3/3. Guarda `3.3-lock-sem-exclusao` (o callback chamado sem esperar o anterior): reprova "dois pedidos sobrepostos pelo mesmo nome rodam em série".

- [x] 3.4 `renewal.ts` e teste: renovação preguiçosa abaixo de 60 s, sob `buteco.sessao.renovar`, relendo o registro dentro do lock; reaproveita a renovação de outra aba; `AbortSignal.timeout(10 s)` por tentativa; `503` e falha de rede repetem com o `Retry-After` (ou 1 s) sem começar tentativa depois de 25 s da primeira, e desistem sem apagar; `invalid_grant` apaga e encerra. A constante dos 25 s cita o H1. O teste obrigatório **"duas renovações simultâneas não derrubam a sessão"**: duas instâncias sobre o mesmo storage, o lock da 3.3, e um `/connect/token` falso de rotação estrita (o refresh resgatado reapresentado responde `invalid_grant` e revoga a cadeia). **Guardas:**
  - o lock retirado: a segunda instância apresenta o refresh velho, e o caso reprova com a sessão caída;
  - a leitura do registro movida para fora do lock: o caso "reaproveita a de outra aba" reprova;
  - sem o prazo de 25 s (relógio falso): o caso do `503` contínuo reprova;
  - o `503` tratado como fim: o caso "503 na renovação não apaga a sessão" reprova.

      **Resultado, 01/11/2026:** `renewal.ts` (com `oauth.ts` e `transient.ts`), `renewal.test.ts` 9/9, e o `/connect/token` estrito em `test/strictTokenEndpoint.ts`. Guardas em `217-verificacao/guardas/`:
      - `3.4-sem-lock`: reprovam "duas renovações simultâneas…" (a cadeia revogada pelo emissor estrito: `isValid('refresh-2')` falso) e "reaproveita a renovação de outra aba". A primeira rodada reprovou pela contagem de pedidos antes de chegar à sessão; as asserções da sessão passaram para o começo do caso, e o guarda foi repetido;
      - `3.4-leitura-fora-do-lock`: reprovam os mesmos dois;
      - `3.4-sem-prazo-25s`: reprova "503 na renovação não apaga a sessão…". O caso tem uma válvula (200 na 11ª tentativa) para reprovar pelo resultado, sem travar;
      - `3.4-503-como-fim`: reprova o mesmo caso.

- [x] 3.5 Todo escritor do registro sob o lock (D3): a gravação depois da troca do código, a renovação, o logout e o fim de sessão. Dentro do lock, o fim de sessão compara o `refreshToken` ou o `accessToken` que a aba usou antes de apagar. Testes:
  - **"logout durante uma renovação"**: a instância A com o pedido de renovação em voo (a resposta segura por uma promessa do teste), a B aciona o logout, a resposta `200` de A chega, e o registro termina **ausente**, com o `id_token_hint` de B igual ao id token novo de A;
  - fim de sessão não apaga a sessão nova de outra aba.

  **Guardas:** o logout apagando fora do lock faz o primeiro caso reprovar, com o registro presente; o fim de sessão apagando sem comparar faz o segundo reprovar.

      **Resultado, 01/11/2026:** `writers.test.ts`, 4/4 (os dois casos pedidos, mais "apaga quando o registro ainda é o que falhou" e "a gravação de um login novo espera a renovação em voo"). Guardas em `217-verificacao/guardas/`:
      - `3.5-logout-fora-do-lock`: reprova o primeiro caso com o registro presente (`expected { accessToken: 'access-2', … } to be null`);
      - `3.5-fim-sem-comparar`: reprova "fim de sessão não apaga a sessão nova de outra aba".

- [x] 3.6 `loginFlow.ts` e teste: `beginLogin` (login em andamento no `sessionStorage`, chave `buteco.login`, com `criadoEm`, substituindo o anterior; a URL do authorize com os parâmetros do `panel-issuer-session`); a validação do `retorno` (só o prefixo `/connect/authorize?` sobre o valor decodificado uma vez, e a navegação com exatamente a string conferida, pela decisão do dono de 01/11/2026) e do `voltarPara`; a leitura de uso único com prazo de 10 min; a troca do código. Testes de tabela para o `retorno` (ausente, absoluto, `//`, `/\`, `/connect/authorizex?`, sem query, `/connect/logout?`, codificado duas vezes, e a string conferida devolvida como está) e para o `voltarPara`. **Guardas:**
  - o prefixo sem o `?`: o caso do `/connect/authorizex?` reprova;
  - decodificar antes de conferir: o caso do codificado duas vezes reprova;
  - devolver o valor decodificado de novo: o caso da string conferida reprova;
  - `voltarPara` aceitando `//host`: o caso reprova;
  - o login em andamento não apagado na leitura: o caso de uso único reprova;
  - sem o prazo de 10 min: o caso do vencido reprova.

      **Parada (convenção 9) e decisão, 01/11/2026.** O guarda previsto antes, "só o `startsWith('/connect/authorize?')`, sem a conferência de origem e caminho", não reprovou (35/35 com a conferência retirada) e não tinha como reprovar: o prefixo já fixa a origem e o caminho. A premissa do D12 era falsa. O dono decidiu tirar a conferência por `new URL` (opção 2) e acrescentar o caso da string conferida. As saídas de antes da decisão estão em `217-verificacao/guardas/3.6-antes-da-decisao/`.

      **Resultado:** `loginFlow.test.ts`, 36/36. Guardas em `217-verificacao/guardas/`, cada um com `-reprova.log` e `-aprova.log`:
      - `3.6-prefixo-sem-interrogacao`: reprovam "outro caminho com o mesmo prefixo" e "sem query";
      - `3.6-decodifica-antes-de-conferir`: reprovam "codificado duas vezes" e "devolve exatamente a string conferida…";
      - `3.6-devolve-decodificado-de-novo` (o guarda pedido pelo dono): reprova "devolve exatamente a string conferida, sem decodificar de novo nem recompor";
      - `3.6-voltarPara-aceita-barra-dupla`: reprovam o `//host-externo/x` e o `beginLogin` com ele;
      - `3.6-nao-apaga-na-leitura`: reprova "é de uso único";
      - `3.6-sem-prazo-10min`: reprova "com 11 minutos é recusado".

      A navegação do `/entrar` com essa string é conferida de novo no teste de componente da 4.1.

- [x] 3.7 `sessionFetch.ts` e teste (D5, D13): `sessionFetch(input, init, { endsSessionOn401 })` anexa o token, renova antes se preciso, no `401` renova com o token que falhou e repete uma vez; o segundo `401` encerra a sessão com `voltarPara` só quando `endsSessionOn401` é verdadeiro. Ele não monta URL, não lê corpo e não cria erro. **Guardas:** encerrar no primeiro `401` faz o caso "401 seguido de sucesso" reprovar; ignorar a opção faz o caso do `apps/connectors` reprovar.

      **Resultado, 01/11/2026:** `sessionFetch.ts` e `sessionFetch.test.ts`, 9/9. Os cabeçalhos continuam objeto simples, como as cópias já montam (um `Headers` passaria os nomes a minúsculas). Dois comportamentos que o design não detalhava, decididos pelo D4 e pelo D5 e testados: `invalid_grant` na renovação encerra também no `apps/connectors` (o registro já saiu), e o emissor indisponível depois do `401` devolve o `401` à tela sem encerrar. Guardas em `217-verificacao/guardas/`:
      - `3.7-encerra-no-primeiro-401`: reprovam "401 seguido de sucesso não desloga", "segundo 401 encerra…" e "emissor indisponível depois do 401…";
      - `3.7-ignora-a-opcao`: reprova "no apps/connectors, o segundo 401 não encerra".

- [x] 3.8 `logout.ts` e teste (D7): sob o lock, lê o `idToken`, apaga, solta, e navega a `/connect/logout?id_token_hint=…&post_logout_redirect_uri=<origem>/entrar`; sem `idToken`, sem o hint; expõe o estado de espera para o "Saindo…". A navegação é injetável. **Guarda:** navegar antes de apagar faz o caso "no instante da navegação o registro já não existe" reprovar.

      **Resultado, 01/11/2026:** `logout.ts` e `logout.test.ts`, 3/3. O estado de espera é a própria promessa do `logout()`, que só resolve depois de o lock ser obtido e o registro apagado; o "Saindo…" da 4.4 é o item enquanto ela está pendente. Guarda `3.8-navega-antes-de-apagar` (a remoção adiada para depois da navegação): reprova "no instante da navegação o registro já não existe".

- [x] 3.9 `transient.ts`, `app/queryClient.ts` e `queryClient.test.ts` (D4): o erro com `retryAfter` lido do cabeçalho; consulta com `503` repete até 3 vezes com o `Retry-After` limitado a 1–30 s; mutação não repete; os outros erros ficam no padrão; consulta com `retry` próprio não muda. **Guardas:** `retry` em mutação faz o caso reprovar; sem o limite, o caso do `Retry-After: 120` reprova.

      **Resultado, 01/11/2026:** `transient.ts`, `app/queryClient.ts` (`createAppQueryClient`) e `queryClient.test.ts`, 9/9. A regra repete o padrão do TanStack Query para os outros erros, lido no `retryer.js` (`config.retry ?? 3` e `defaultRetryDelay`), e o comentário do `useAgentInsights.ts` que dizia "o cliente SEM `defaultOptions`" foi corrigido. Guardas em `217-verificacao/guardas/`:
      - `3.9-retry-em-mutacao`: reprova "mutação com 503 não repete" (`called 1 times, but got 4 times`). Na primeira rodada, ele reprovou por timeout de 15 s (o `await` da mutação esperava as tentativas com o relógio falso parado); o caso passou a disparar a mutação, avançar o relógio e contar, e o guarda foi repetido;
      - `3.9-sem-limite`: reprova "Retry-After 120 no 503 espera 30000 ms".

- [x] 3.10 A conferência de contexto seguro (`navigator.locks` e `crypto.subtle`), usada pelo `/entrar` (4.1).

      **Resultado, 01/11/2026:** `secureContext.ts` (`hasSecureContextApis`). O guarda dele é o da 4.1. Junto, `auth/session.ts` liga as peças ao navegador (`authRuntime`, `sessionStore`, `renewal`, `sessionFetch`, `logout` e o `useSession`), e o `test/setup.ts` instala o lock falso em `navigator.locks`, porque o jsdom não tem Web Locks; o `MemoryStorage` do setup passou ao `test/authFakes.ts`.

## 4. Telas e rotas (`apps/frontend`, D1, D6, D7)

- [x] 4.1 `features/auth/api/sessionApi.ts` (`POST /auth/session` com `email` e `senha`), `components/EntrarForm.tsx` e `pages/EntrarPage.tsx`, com os estados da tabela do `operator-login-ui`: `400`, `401`, `403`, `423` com N do `Retry-After`, `429`, `503`, falha de rede, `?motivo=sessao-expirada` com `role="status"`, o botão desabilitado até o `Retry-After`, e o aviso de HTTPS sem enviar (3.10). Sem "Esqueceu a senha?" e sem o convite. Testes de componente em `EntrarPage.test.tsx`. **Guardas:** N fixo em 5 faz os casos de `600` e `61` reprovarem; sem a conferência de contexto seguro, o caso "sem Web Locks o formulário não envia" reprova.

      **Resultado, 01/11/2026:** `sessionApi.ts`, `EntrarForm.tsx`, `EntrarPage.tsx` e `EntrarPage.test.tsx`, 16/16. Os textos e os alertas seguem o protótipo (`project/Main.dc.html`, relido em 01/11/2026): `bloqueado` amarelo, `sessao-expirada` azul com `role="status"`, e os erros vermelhos. O lockup continua o da change `frontend-marca-visual` (D11), e não o SVG desenhado no protótipo. Duas decisões de implementação, registradas para a validação visual (11.5):
      - **`retorno` fora da regra depois do `204`:** o D6 dizia "vai a `/`, e o `ProtectedRoute` começa um fluxo novo", mas o `ProtectedRoute` manda para o `/entrar` (`operator-login-ui`), o que pediria a senha de novo. O `/entrar` chama então `beginLogin('/')`: o cookie recém-gravado devolve o código sem formulário, e o login termina em `/`. É o efeito que o D6 descreve, por outro caminho (convenção 9; o D6 foi corrigido);
      - **`?motivo=falha-na-entrada`:** o aviso genérico do código recusado no `/entrar/retorno` (4.2), "Não foi possível concluir a entrada. Entre de novo.".

      Guardas em `217-verificacao/guardas/`: `4.1-N-fixo-em-5` reprova os três casos de `423` (`600`, `61` e `60`); `4.1-sem-contexto-seguro` reprova "sem Web Locks o formulário não envia"; e `4.1-navega-com-retorno-decodificado-de-novo` reprova "204 com retorno válido navega para exatamente a string recebida" (a mesma regra da 3.6, agora na navegação).

- [x] 4.2 `pages/EntrarRetornoPage.tsx` e teste (D6): lê `code`, `state`, `error` e `error_description` uma vez, troca a URL por `navigate('/entrar/retorno', { replace: true })` antes de qualquer pedido, processa os valores capturados uma vez por montagem (`ref`), confere o login em andamento e o `state`, trata o `error` pela tabela do D6 (o `error_description` só no console), troca o código e trata `200`, `invalid_grant`, `invalid_client` e `503`. O `fetch` falso confere a URL da página no instante do `POST /connect/token`. **Guardas:**
  - sem o `navigate`: o caso "a URL fica sem o código antes da troca" reprova;
  - com `push` no lugar de `replace`: o mesmo caso reprova pelo `historyAction`;
  - a troca antes da conferência do `state`: o caso do `state` diferente reprova;
  - o `navigate` só no ramo de sucesso: o caso "a URL fica sem query também no erro" reprova.

      **Resultado, 01/11/2026:** `EntrarRetornoPage.tsx` e `EntrarRetornoPage.test.tsx`, 8/8, inclusive "no StrictMode, o retorno é processado uma vez só". A sonda da 2.3 (`src/app/replaceProbe.test.tsx`) saiu. O `fetch` falso lê a URL e a ação do histórico do router no instante do `POST`. Na primeira rodada, dois casos reprovaram com dois `POST` por defeito do teste: o router era atribuído depois do `render`, o efeito já rodava dentro dele, e o `fetch` falso lançava ao ler o router. O `exchangeCode` tratou isso como falha de rede e repetiu depois de 1 s. Os testes passaram a criar o router antes do `render`. Guardas em `217-verificacao/guardas/`:
      - `4.2-sem-navigate`: reprovam "a URL fica sem o código antes da troca…", "a URL fica sem query também no erro…" e "state diferente não troca o código";
      - `4.2-push-no-lugar-de-replace`: reprovam os dois primeiros, pelo `historyAction`;
      - `4.2-troca-sem-conferir-state`: reprova "state diferente não troca o código";
      - `4.2-navigate-so-no-sucesso` (o `navigate` movido para logo antes da troca): o caso de sucesso continua aprovando, e reprovam o do erro e o do `state` diferente.

- [x] 4.3 `app/ProtectedRoute.tsx`, `app/routes.tsx` e os testes (`ProtectedRoute.test.tsx`, `router.test.tsx`): sem registro, `/entrar` com `voltarPara`; `/entrar`, `/entrar/retorno` e `/login` → `/entrar` como as únicas rotas sem sessão; a navegação a `/entrar` quando o registro sai por outra aba (3.2). **Guarda:** o `ProtectedRoute` conferindo o `sessionStorage` antigo faz o caso "outra aba usa a mesma sessão" reprovar.

      **Resultado, 01/11/2026:** `ProtectedRoute.tsx` (pelo `useSession`, que lê o registro com `useSyncExternalStore` e reage ao evento `storage`), `routes.tsx` (`/entrar`, `/entrar/retorno` e `/login` → `/entrar`), `ProtectedRoute.test.tsx` 4/4 e `router.test.tsx` 16/16, com os casos novos "link antigo para /login chega ao /entrar" e "/entrar e /entrar/retorno abrem sem sessão". Os testes passam a gravar a sessão pelo `signInForTest()` do `test/authFakes.ts`. Guarda `4.3-sessionStorage-antigo` (o `ProtectedRoute` lendo `buteco.operatorToken` no `sessionStorage`): reprovam "outra aba usa a mesma sessão…" e "quando outra aba apaga o registro, esta vai ao /entrar".

- [x] 4.4 `components/layout/AppShell.tsx` (D7, `frontend-app-shell`): no rodapé, o nome do id token (ou o e-mail) e o item "Sair", com "Saindo…" enquanto espera o lock. O teste "o rodapé não afirma a identidade do operador" é trocado pelos dois casos novos do requisito. **Guarda:** o rodapé com o nome fixo, sem ler o registro, faz o caso do e-mail reprovar.

      **Resultado, 01/11/2026:** `AppShell.tsx` e `AppShell.test.tsx`, 21/21. No rodapé, o nome (ou o e-mail) ao lado do controle de tema, e abaixo o "Sair", com o ícone `LogOut` do lucide-react. O teste "não afirma a identidade do operador no rodapé" saiu, e entraram os dois casos do requisito, mais "Sair mostra 'Saindo…' enquanto espera o lock…", que segura o lock por uma renovação "em voo". Guarda `4.4-nome-fixo`: reprova "sem nome no id token, o rodapé mostra o e-mail e o Sair".

      **Refeito pela validação visual (11.5, 01/11/2026):** o rodapé passa a seguir o protótipo de autenticação atualizado (D7): avatar com as iniciais (`components/layout/userInitials.ts`, com teste), nome e e-mail em duas linhas, e os botões de tema e de "Sair" só de ícone, nessa ordem, com o `loading` do `ActionIcon` no "Saindo…". As cores do avatar entram como `--buteco-avatar-bg` e `--buteco-avatar-ink` no `cssVariablesResolver`, com o par invertido no escuro, e o `theme.test` as fixa. Os dois casos do rodapé viraram os quatro cenários do spec (duas palavras, uma palavra, sem nome, "Saindo…"). `AppShell.test`, `userInitials.test` e `theme.test`: 77/77. Guardas, cada um visto reprovar e voltar a aprovar (`217-verificacao/guardas/11.5-*`):

      | guarda | defeito | reprova |
      |---|---|---|
      | `11.5-iniciais-so-a-primeira` | só a primeira inicial | 4 do `userInitials.test` e o caso de duas palavras |
      | `11.5-sem-segunda-linha` | a linha do e-mail retirada | os casos de duas palavras e de uma palavra |
      | `11.5-email-duas-vezes-sem-nome` | segunda linha também sem nome | o caso sem nome |
      | `11.5-sair-antes-do-tema` | os dois botões trocados de lugar | o caso de duas palavras (a ordem) |
      | `11.5-rotulo-fixo-no-saindo` | `aria-label` "Sair" fixo | o caso do "Saindo…" |
      | `11.5-sem-carregamento` | `loading={false}` | o caso do "Saindo…" |
      | `11.5-avatar-sem-inverter-no-escuro` | o fundo do escuro igual ao do claro | o caso do avatar no `theme.test` |

      **Achado na olhada no navegador antes de devolver ao dono (stack da 11.3, frontend reconstruído):** o fundo do avatar estava na raiz do `Avatar`, e o `placeholder` do Mantine a cobria com o próprio cinza (`#faf9f8`, medido pelo estilo calculado); as iniciais já saíam em `#1a3f78`. O fundo passou para o `placeholder`, e o estilo calculado ficou `#eef3fb` no claro; no escuro, fundo `#1a3f78` com as iniciais claras. **Sem guarda automático:** o jsdom não carrega o CSS do Mantine, então o defeito não aparece na suíte; a prova é a conferência no navegador (convenção 14), e a validação do dono.

- [x] 4.5 Teste de integração do fluxo inteiro: rota protegida → `/entrar` → `204` → authorize (navegação simulada) → `/entrar/retorno` → `/connect/token` → a rota protegida carrega, com o `Authorization` do emissor. É a costura, sem guarda próprio: as partes já têm (D12).

      **Resultado, 01/11/2026:** `features/auth/loginFlow.integration.test.tsx`, 1/1, sobre a árvore de rotas real (`appRoutes`). Feita depois da 5.1, porque a lista de agentes só leva o token do emissor quando o `agentsApi` usa o `sessionFetch`.

## 5. As 7 cópias do `request<T>` e a saída do `token.ts` (`apps/frontend`, D5, D8, D13)

- [x] 5.1 `agentsApi`, `insightsApi`, `knowledgeBasesApi`, `mcpServersApi`, `channelsApi` e `sessionsApi` trocam o `fetch` por `sessionFetch`; o `connectorsApi` usa `{ endsSessionOn401: false }`. O `ApiError` de cada cópia leva o `retryAfter` (3.9). A URL base, o corpo e o `204` continuam de cada cópia (convenção 7).

      **Resultado, 01/11/2026:** as seis cópias do `apps/api` e do `apps/inbox` tinham o mesmo bloco, trocado da mesma forma: `sessionFetch` no lugar do `fetch`, sem o `getToken` e sem o `401` → `/login`. O `ApiError` de cada uma ganhou `retryAfter`, como quarto argumento opcional do construtor. O `connectorsApi` usa `{ endsSessionOn401: false }`, o `ConnectorsError` ganhou `retryAfter` nas opções, e o comentário do `401` foi reescrito (D5, P5). `npx tsc -b` sem erro.

- [x] 5.2 **Sem aumentar o timeout do `KnowledgeBaseCreatePage.sync.test.tsx`** (decisão do dono de 01/11/2026; #226). Os testes de `401` de cada cópia são reescritos para "renova e repete uma vez; o segundo `401` encerra" (e "não encerra" no `connectorsApi.test.ts` e no `KnowledgeBaseCreatePage.sync.test.tsx`). **Guarda:** numa cópia, o `fetch` cru de volta no lugar do `sessionFetch` faz o caso dela reprovar.

      **Resultado, 01/11/2026.** Os dois casos ficam num contrato só, `test/sessionContract.ts` (`sessionFetchContract(call, { endsSession })`), que cada cópia chama com uma função dela: `agentsApi`, `knowledgeBasesApi`, `knowledgeIndexApi`, `insightsApi` (as duas rotas), `connectorsApi` (`endsSession: false`) e, em arquivos novos, `channelsApi` (o cenário do `apps/inbox`), `mcpServersApi` e `sessionsApi`, que não tinham caso de `401`. O caso da tela no `KnowledgeBaseCreatePage.sync.test.tsx` passou a "renova e repete, e o segundo não desloga", **sem mexer no timeout** (o arquivo, sozinho, levou 37 s: 35/35). Dois ajustes de teste, sem mudar produção:
      - o `Response` falso do `insightsApi.test.ts` ganhou `headers`, porque o `ApiError` agora lê o `Retry-After`;
      - o `installFetch` do `KnowledgeBaseCreatePage.sync.test.tsx` não faz mais `JSON.parse` de todo corpo: o do `/connect/token` é form-urlencoded, e o falso lançaria.

      Guardas em `217-verificacao/guardas/`:
      - `5.2-fetch-cru-no-channelsApi`: reprovam os dois casos do contrato no `channelsApi.test.ts`;
      - `5.2-connectors-encerra-no-401` (`endsSessionOn401: true` no `connectorsApi`): reprovam o caso do contrato no `connectorsApi.test.ts` e o da tela.

- [x] 5.3 Saem `src/auth/token.ts`, `features/auth/api/authApi.ts`, `useAuth.ts`, `types/auth.ts`, `LoginPage` e `LoginForm`; os 19 arquivos que importavam o `token.ts` passam ao módulo novo. Varredura (convenção 25): `git grep -n "buteco.operatorToken\|auth/login\|getToken\|clearToken" -- apps/frontend` sem resultado fora de comentário histórico. A saída vai na tarefa.

      **Resultado, 01/11/2026.** Os sete arquivos saíram, sem `git rm` no índice (a árvore fica sem nada preparado para commit). Os importadores do `token.ts` passaram ao `test/authFakes.ts` (`signInForTest`, `signOutForTest`) ou ao módulo novo. A varredura em `217-verificacao/5.3-varredura.txt` usa `git grep --untracked`, porque sem ela o `git grep` não olha os arquivos novos. Os únicos resultados são falsos positivos: `auth/login` casa com `auth/loginFlow`, nos quatro imports das telas novas, e o comentário histórico do `test/authFakes.ts` ("Substitui o `setToken`/`clearToken` do `auth/token.ts`, que saiu"). `npx tsc -b` sem erro.

- [x] 5.4 Lint (D2): `no-restricted-syntax` para `JSXAttribute[name.name='dangerouslySetInnerHTML']` e `no-restricted-imports` para `rehype-raw`, no `eslint.config.js`, sem dependência nova. **Guarda:** um arquivo temporário com cada um faz `npm run lint` reprovar nomeando o arquivo; o arquivo sai.

      **Resultado, 01/11/2026:** as duas regras no `eslint.config.js`; `npm run lint` sem erro. Guarda (`217-verificacao/guardas/5.4-lint-reprova.log` e `-aprova.log`): `src/LintGuard217Html.tsx` com `dangerouslySetInnerHTML` e `src/lintGuard217Raw.ts` com o import de `rehype-raw` fazem o lint reprovar, nomeando os dois arquivos (`no-restricted-syntax` e `no-restricted-imports`); os arquivos saíram, e o lint volta a passar. De passagem, as constantes de texto compartilhadas pelo `/entrar` e pelo `/entrar/retorno` foram para `features/auth/messages.ts`, para o `EntrarPage.tsx` só exportar o componente (`react-refresh/only-export-components`).

## 6. Desenvolvimento e nginx (`apps/frontend`, D2, D10, D11)

- [x] 6.1 `vite.config.ts`: `server.proxy` para `/auth`, `/connect` e `/.well-known` em `http://localhost:5017`, sem `changeOrigin`. Teste sobre a configuração exportada. **Guarda:** `changeOrigin: true` num dos três faz o teste reprovar.

      **Resultado, 01/11/2026:** `server.proxy` no `vite.config.ts`, com o comentário do D10, e `src/app/viteConfig.test.ts`, 1/1 (no ambiente jsdom: o `setup.ts` usa `window`, e no ambiente `node` o arquivo não carrega). Guarda `6.1-changeOrigin` (`changeOrigin: true` no `/connect`): reprova o caso.

- [x] 6.2 `.env.example` do `apps/frontend`: o comentário de que o fluxo do emissor vai pelo proxy, e as `VITE_*_BASE_URL` continuam nas rotas.

      **Resultado, 01/11/2026:** o comentário entrou antes das três variáveis.

- [x] 6.3 `deploy/nginx.conf`: a CSP do D2, com o hash da 2.2, e `Referrer-Policy: no-referrer`, os dois com `always`, no `server` e repetidos no `location = /index.html`, com o comentário da armadilha do `add_header` atualizado. Teste de unidade que lê o `nginx.conf` e o `index.html`: o hash da CSP é o do script, e os dois cabeçalhos estão nos dois níveis. **Guardas:** mudar o script sem mudar o hash faz o teste reprovar; tirar o `Referrer-Policy` do `location = /index.html` faz o teste reprovar.

      **Resultado, 01/11/2026:** os dois cabeçalhos no `server` e no `location = /index.html`, com `always`, e a CSP do D2 com o hash da 2.2. O comentário da armadilha do `add_header` foi reescrito: o bloco declara o `Cache-Control` e por isso não herda os do `server`. `src/app/nginxHeaders.test.ts`, 5/5: o teste lê o `index.html`, calcula o SHA-256 do único script inline e confere o hash, a presença dos dois cabeçalhos com `always` nos dois níveis, a mesma CSP nos dois, e as diretivas do D2. Guardas em `217-verificacao/guardas/`:
      - `6.3-script-sem-hash` (o `catch (e) {}` do script do tema reescrito): reprova "a CSP leva o hash do script inline do index.html";
      - `6.3-sem-referrer-no-index`: reprova "a CSP e o Referrer-Policy estão no location = /index.html, com always".

      A sintaxe do nginx e os cabeçalhos de verdade são conferidos pelos curl da 11.3.

## 7. Remoção no `apps/api` (D8)

- [x] 7.1 Saem `Auth/Endpoints/AuthEndpoints.cs`, `Auth/Requests/LoginRequest.cs`, `Auth/Responses/LoginResponse.cs`, `Auth/OperatorCredentialOptions.cs` (o `UserBootstrapOptions.SectionName` passa a `"Auth"` literal) e `TokenSigningOptions.OperatorTokenLifetime`. No `Program.cs`: o `Configure<OperatorCredentialOptions>`, o `MapAuthEndpoints()` e `"/auth/login"` da allowlist. `Auth:OperatorUsername` sai dos `appsettings*.json`. Ficam o `OperatorPasswordHasher`, o `ITokenService`, a validação HMAC e o `ServiceTokenDelegatingHandler`.

      **Resultado, 01/11/2026:** os quatro arquivos e as pastas `Auth/Endpoints`, `Auth/Requests` e `Auth/Responses` saíram, junto com o `using Buteco.Api.Auth.Endpoints` do `Program.cs`. O comentário do `UserBootstrapOptions` passou a dizer que a variável serve só ao bootstrap, com a #225. `Auth:OperatorUsername` só estava no `appsettings.Development.json`. `dotnet build` do `Buteco.Api`: 0 avisos, 0 erros.

- [x] 7.2 Testes afetados: sai o `AuthLoginTests`; sai o `IdentityIssuerCoexistenceTests.HmacLogin_StillIssuesTheOperatorToken_AndItIsAcceptedOnAgents`; o caso anônimo do `RouteAuthenticationTests` passa ao `POST /auth/session`; o `RouteAuthenticationStartupFailureTests` troca `/auth/login` por uma rota inventada; o `KnowledgeDocumentCatalogTests` (l. 405–408) pega o token do `ITokenService`; `OperatorUsername` sai do `TestAuthentication` e do `ApiFactoryFixture`.

      **Resultado, 01/11/2026:**
      - saem o `AuthLoginTests.cs` e o caso HMAC do `IdentityIssuerCoexistenceTests` (o resumo da classe diz onde a prova foi parar);
      - o `RouteAuthenticationTests.Login_WithoutToken_StaysAnonymous` passa a `Session_WithoutToken_StaysAnonymous`: o `POST /auth/session` sem e-mail nem senha responde o `400` dele, e não `401`;
      - o `RouteAuthenticationStartupFailureTests` usa `/rota-anonima-que-nao-existe`;
      - o `KnowledgeDocumentCatalogTests.DocumentWrites_WithTokenFromLogin_RecordOperatorAsAuthor` vira `…_WithOperatorHmacToken_…`, com o token do `ITokenService`, e o comentário explica a troca;
      - `KnownOperatorUsername` e `Auth:OperatorUsername` saem do `TestAuthentication` e do `ApiFactoryFixture`.

      Os casos afetados aprovam (`217-verificacao/7.3-aprova.log`, 22 de 23; o que falta é o da 7.3).

- [x] 7.3 Teste novo `GoneLoginTests` (decisão do dono na parada, 01/11/2026), na fixture `LocalUsersFixture`, dois casos com a credencial do Admin do bootstrap no corpo:
  - `GoneLogin_WithoutToken_Is401_LikeAnyMissingRoute`: sem `Authorization`, `401` com o corpo vazio, igual a qualquer rota inexistente;
  - `GoneLogin_WithIssuerUserToken_Is404`: com o access token de usuário do emissor (o `IdentityIssuerUserFlow.SignInAsync`), e não com o HMAC de operador, que a #184 vai recusar, exatamente `404`.

      **Guarda:** só a rota de volta (`~/.cache/buteco-agents/bin/guard-73-rota.sh`): os quatro arquivos dela (`AuthEndpoints`, `OperatorCredentialOptions`, `LoginRequest`, `LoginResponse`), a propriedade `OperatorTokenLifetime` que ela lê e, no `Program.cs`, o `using`, o `Configure`, o `MapAuthEndpoints()` e `"/auth/login"` na allowlist, com `Auth__OperatorUsername=operator`. Os dois casos reprovam com `Actual: OK`, o do token pelo `200` pedido (`217-verificacao/guardas/7.3-goneLogin-reprova.log`, com a lista do que foi reintroduzido no começo). O script restaura só esses seis arquivos. Com a árvore restaurada, os 2 casos aprovam, ao lado do `RouteAuthenticationTests`, do `KnowledgeRouteAuthenticationTests` e do `RouteAuthenticationStartupFailureTests`, 21 de 21 (`guardas/7.3-goneLogin-aprova.log`). Um primeiro guarda, que restaurou o `apps/api/src` inteiro ao `HEAD`, foi refeito assim a pedido do dono: só o defeito nomeado.

      **A primeira parada (premissa do `404` sem token):** o `FallbackPolicy` do `apps/api` (`Program.cs` l. 131–134) vale também para o pedido que não casa com nenhuma rota. Medido por uma sonda temporária, já apagada (`217-verificacao/7.3-sonda.txt`):

      | pedido | sem token | com HMAC de operador |
      |---|---|---|
      | `POST /auth/login` | `401` | `404` |
      | `GET /rota-que-nao-existe` | `401` | `404` |
      | `POST /auth/session-x` | `401` | `404` |

      Corrigido no D8 (com o porquê do token do emissor e do `401` intencional), no D11, no D12, no cenário "A rota de login não existe mais" do `operator-authentication` e, num quinto lugar que a parada não listava, no cenário "A rota de login antiga não existe" do `route-authentication`.

      **PARADA (convenção 9), 01/11/2026, outra vez.** A instrução pedia que o curl da 11.3 esperasse o `401` "com o `WWW-Authenticate` do api". O `apps/api` não manda esse cabeçalho: sem `Authorization`, o pedido vai ao `OperatorTokenAuthenticationHandler` (`IssuerTokenAuthentication.SelectScheme`), cujo challenge padrão responde `401` com o corpo vazio e **sem** `WWW-Authenticate`. Uma sonda temporária, já apagada, mostrou o mesmo em `POST /auth/login`, `GET /agents` e `GET /rota-que-nao-existe`: nenhum cabeçalho e o corpo com 0 bytes (`217-verificacao/7.3-sonda-cabecalhos.txt`). A asserção do cabeçalho foi tirada do caso sem token. **Decisão do dono (01/11/2026), opção 1:** o caso sem token afirma `401` e corpo vazio; o curl da 11.3 espera `401` com corpo vazio e sem `Content-Type: text/html`, sem afirmar `Content-Length: 0`, porque pelo nginx o tamanho pode chegar de outro jeito; nada muda no handler HMAC.

      As quatro decisões da implementação aceitas pelo dono estão no design e no spec, cada uma com cenário e teste: o `beginLogin('/')` no `retorno` fora da regra (D6; `panel-issuer-session`, "retorno fora da regra vai para a tela inicial"); o `?motivo=falha-na-entrada` (D6; `panel-issuer-session`, "Código já usado volta ao /entrar", e `operator-login-ui`, "Código recusado mostra o aviso genérico", com o caso novo no `EntrarPage.test` e o guarda `guardas/7.3-falha-na-entrada-*`); o `401` com o emissor fora do ar sem encerrar a sessão (D5 item 4; cenário novo "Emissor indisponível depois do 401 não encerra a sessão"); e o `invalid_grant` encerrando também no `apps/connectors` (D5 item 5; cenário novo "invalid_grant na renovação encerra também no apps/connectors"). Os dois últimos já tinham teste e guarda da 3.7.

- [x] 7.4 Varredura (convenção 25): `git grep -n "OperatorUsername\|OperatorTokenLifetime\|LoginRequest\|LoginResponse\|MapAuthEndpoints\|auth/login" -- apps tests` sem resultado fora de comentário histórico e de `openspec/changes/archive/`. A saída vai na tarefa.

      Saída em 01/11/2026 (`217-verificacao/7.4-varredura.txt`), 15 linhas, classificadas:
      - **comentário histórico** (dizem que a rota saiu): `UserBootstrapOptions.cs` l. 6, `GoneLoginTests.cs` l. 9, `IdentityIssuerCoexistenceTests.cs` l. 16, `KnowledgeDocumentCatalogTests.cs` l. 400, `RouteAuthenticationTests.cs` l. 54;
      - **uso intencional:** `GoneLoginTests.cs` l. 38 e 52, que provam a ausência da rota (7.3);
      - **falso positivo do padrão** (`auth/login` dentro de `auth/loginFlow`): `EntrarPage.tsx` l. 5, `EntrarPage.test.tsx` l. 9, `EntrarRetornoPage.tsx` l. 4, `EntrarRetornoPage.test.tsx` l. 8;
      - **da 8.1:** `RoundTripFixture.cs` l. 92, 190 e 395;
      - **fora da regra:** `apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs` l. 17, um comentário que compara com o "TTL do operador (`Auth:OperatorTokenLifetime`)" como se ele ainda existisse.

      **PARADA (convenção 9), 01/11/2026.** A tarefa pressupõe que nada fica fora de comentário histórico, e o design diz que o código do `apps/inbox` não muda (Measured baseline, `apps/inbox` não medido). O comentário da l. 17 é a exceção. **Decisão do dono (01/11/2026), opção 1 com ajuste:** o comentário passou a descrever só o estado atual (por que o TTL do token de serviço é fixo), sem comparar com o `Auth:OperatorTokenLifetime` nem contar a remoção, que fica no OpenSpec e no CHANGELOG. O design registra que o `apps/inbox` muda só nesse comentário (Measured baseline), e a 11.2 ganha o build do `apps/inbox`.

      Depois da correção, a mesma varredura dá 14 linhas, nenhuma fora das quatro primeiras classes (`217-verificacao/7.4-varredura-depois.txt`). Os 3 da 8.1 saem na 8.1.

      **Critério de parada daqui em diante (dono, 01/11/2026):** parar quando a premissa falsa muda comportamento, contrato, escopo de teste ou uma decisão do design. Achado que só toca comentário ou documentação é resolvido, registrado aqui com o motivo e listado no fechamento.

## 8. Acordo em `tests/` (D8)

- [x] 8.1 `tests/InboxOrchestratorRoundTrip.Tests/Support/RoundTripFixture.cs` (l. 395): o token do operador vem do `IssuerUserFlow`, que o projeto já tem, e não do `POST /auth/login`. Conferir com `git grep` que o `tests/ApiConnectorsRoundTrip.Tests` não usa o login.

      `tests/ApiConnectorsRoundTrip.Tests`: `git grep -n "auth/login\|LoginAsOperator\|OperatorUsername"` sem resultado (exit 1, 01/11/2026).

      **PARADA (convenção 9; muda escopo de teste), 01/11/2026.** O `LoginAsOperatorAsync` tem 10 chamadores (a parada dizia 11, contando a definição): 9 usam o token no `apps/api`, e um, `RoundTripTests.OperatorToken_IssuedByApi_IsAcceptedByInboxWithoutNetworkCallToApi` (l. 191–213), usa no `apps/inbox`. Esse caso prova que o `apps/inbox` aceita o **HMAC** emitido pelo `apps/api` só pela chave compartilhada, sem chamada de rede (o "Risco 1" citado no comentário). Com o token do emissor no lugar, o caso passa a depender do JWKS buscado no `apps/api` (o `IssuerKeysGate`, com o `503` inicial que o `IssuerTokenRoundTripTests` espera por polling), o comentário dele fica falso, e a aceitação do HMAC no `apps/inbox`, que continua até a #182, perde a única prova de ponta a ponta. A decisão volta ao dono antes de mexer na fixture.

      **Decisão do dono (01/11/2026), opção 1:** os dois usos se separam (D8).
      - `RoundTripFixture`: sai o `LoginAsOperatorAsync`. Entram o `UserAccessTokenAsync` (o `IssuerUserFlow.AccessTokenAsync` do primeiro Admin do bootstrap, escopo `openid buteco-api`, com o aquecimento do bootstrap e do catálogo), usado pelos 9 chamadores do `apps/api` (`RoundTripTests` ×4, `OrphanDispatchRoundTripTests` ×3, `SessionSerializationRoundTripTests` ×2). **Correção de contagem:** a parada falava em 11 chamadores, 10 no `apps/api`; são 10, 9 no `apps/api` e 1 no `apps/inbox` (a conta anterior incluía a linha da definição), e o `OperatorHmacToken` (o `ITokenService` do `apps/api`, subject `operator`, 5 min). Saem o `OperatorUsername`, o `Auth:OperatorUsername` e os `using` de JSON que só o login usava.
      - **Renomeado (para a lista do fechamento por nome):** `RoundTripTests.OperatorToken_IssuedByApi_IsAcceptedByInboxWithoutNetworkCallToApi` → `RoundTripTests.OperatorHmacToken_SignedWithTheSharedKey_IsAcceptedByInboxWithoutNetworkCallToApi`. O comentário passou a descrever o estado atual. **Ganhou uma asserção:** a chamada ao `apps/inbox` acontece com o portão das chaves do emissor fechado (`ApiIssuerKeys.Closed`). O comentário antigo dizia que o `apps/inbox` não tinha caminho até o TestServer do `apps/api` além do cliente A2A, e isso deixou de ser verdade com a #208 (o cliente das chaves do emissor também vai lá). Com o portão fechado, o "sem rede" fica provado pelo próprio caso.
      - Suíte: 12/12 (`217-verificacao/8.1/rt-inbox-*`, CPU 100 no começo e no fim).
      - **Guarda:** só o `Auth:TokenSigningKey` do `apps/inbox` diferente do do `apps/api`: o caso reprova com `Actual: Unauthorized`. Árvore restaurada: aprova (`guardas/8.1-hmac-inbox-*`).
      - Comentários com o nome novo do caso: #184 (https://github.com/aldovrando-oliveira/buteco-agentes/issues/184#issuecomment-6564218592, ajustar quando o `operator` for recusado no HMAC) e #182 (https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6564218820, o caso sai com o HMAC).

## 9. Compose e configuração (D9)

- [x] 9.1 `docker-compose.prod.yml`: sai `Auth__OperatorUsername`; `Auth__OperatorPasswordHash` sem `:?`, como o `Auth__BootstrapAdminEmail`, com o comentário do D9 e da #225. `.env.prod.example` e `.env.example` sem `AUTH_OPERATOR_USERNAME`, e com a orientação de tirar o hash e o e-mail depois do primeiro login.

      Feito em 01/11/2026:
      - `docker-compose.prod.yml`: sai o `Auth__OperatorUsername`; o `Auth__OperatorPasswordHash` passa a `${AUTH_OPERATOR_PASSWORD_HASH}`, sem `:?`. Um comentário só cobre o hash e o e-mail: o que o bootstrap faz com eles, por que não têm `:?`, a orientação de tirar os dois depois do primeiro login e a #225.
      - `.env.prod.example`: sai o `AUTH_OPERATOR_USERNAME`; o hash e o e-mail ficam num bloco só, com a mesma orientação.
      - `.env.example`: sai o `Auth__OperatorUsername`, e o comentário do bloco descreve o bootstrap. **Entra `Auth__BootstrapAdminEmail=operador@buteco.local`**, o mesmo do `appsettings.Development.json` (l. 51): o arquivo traz os defaults de desenvolvimento, e sem o e-mail o hash dele não serve para nada. Só documentação de exemplo, sem mudar comportamento (critério de parada do dono).
      - **Achado de comentário, resolvido sem parar:** o bloco do `Auth__TokenSigningKey` no `.env.example` falava em "token emitido por apps/api (login do operador)"; passou a "token HMAC assinado por apps/api".
- [x] 9.2 Conferência do cenário "Stack sobe sem as variáveis do bootstrap": `podman compose -f docker-compose.prod.yml --env-file <cópia sem as duas variáveis> config` processa sem erro, e o ambiente do `api` não tem `Auth__OperatorUsername`. E o cenário "Ausência de segredo obrigatório" continua reprovando sem `AUTH_TOKEN_SIGNING_KEY`. As saídas vão na tarefa.

      Medido em 01/11/2026 (`217-verificacao/9.2/`), com `env -i` para o ambiente do shell não completar nada, e o `.env.prod.example` como base:
      - **sem `AUTH_OPERATOR_PASSWORD_HASH` e sem `AUTH_BOOTSTRAP_ADMIN_EMAIL`** (`sem-bootstrap.env`): `podman compose … config` com exit 0. No serviço `api`: `Auth__OperatorUsername` ausente; `Auth__OperatorPasswordHash: ''` e `Auth__BootstrapAdminEmail: ''`; `Auth__TokenSigningKey` presente;
      - **também sem `AUTH_TOKEN_SIGNING_KEY`** (`sem-signing-key.env`): exit 1, `required variable AUTH_TOKEN_SIGNING_KEY is missing a value: defina AUTH_TOKEN_SIGNING_KEY — use --env-file .env.prod`.

      Sem a variável, o compose entrega a string vazia, e não a ausência. O `UserBootstrapper` (l. 58–83) trata o vazio como não configurado: com usuários, ignora sem log; sem usuários, só avisa (`MissingConfigurationEvent`). Nenhum outro leitor do `Auth:OperatorPasswordHash` no `apps/api/src` (`git grep`). O boot de fato com os dois vazios fica conferido no stack da 11.3.

## 10. Documentação (antes do archive)

- [x] 10.1 `01-ARQUITETURA_E_CONVENCOES.md`: a seção de Autenticação (o painel entra pelo emissor; o HMAC só validado até a #182 e a #184) e a leitura da convenção 7 (D13: módulo transversal que envolve o `fetch` sem conhecer o contrato de nenhuma API).
- [x] 10.2 `docs/deployment.md`: "Primeiro Admin" pelo D9 (a #219 não tem tela; tirar o hash e o e-mail do `.env.prod` e dos backups depois do primeiro login; trocar a senha quando a #183 existir; #225); a janela do `400` do catálogo logo depois do deploy (D1); o rollback exige `AUTH_OPERATOR_USERNAME` de volta (Migration Plan); os `curl` do D11 como conferência. E a nota do dono (01/11/2026): "Sair" com o `apps/api` no `503` do gate leva à página de texto do `apps/api`, e não ao `/entrar`; a sessão local já foi apagada, e a autorização fica viva até a próxima vez que o usuário entrar e sair, ou até vencer.
- [x] 10.3 `docs/configuration.md` (variáveis que saem e que viram opcionais), `docs/development.md` (proxy do Vite, login local pelo bootstrap, contexto seguro) e `SECURITY.md` (tokens no `localStorage`, o custo aceito, CSP, `Referrer-Policy`, o access token válido até 15 min depois do logout).

      Feito em 01/11/2026: `docs/configuration.md` (saem o `Auth__OperatorUsername` e o `Auth__OperatorTokenLifetime`; o hash e o e-mail do bootstrap viram opcionais, nas três tabelas; o login pelo emissor sem variável de build), `docs/development.md` (proxy do Vite, `/entrar` com o primeiro Admin de dev, contexto seguro, o fluxo do emissor por `curl` terminando em `$TOKEN` no lugar do `POST /auth/login`) e `SECURITY.md` (os dois mecanismos de token, a sessão do painel no `localStorage` e o custo aceito, a CSP, o `Referrer-Policy`, os 15 min depois do logout; e o `apps/connectors` entre os que compartilham a chave HMAC, que já faltava). **A conferir:** o script do `curl` do `development.md` (PKCE com `openssl`, o `code` lido do `redirect_url`) ainda não rodou contra um `apps/api` local; roda junto do stack da 11.3.

      **PARADA (convenção 9; muda comportamento em produção), 01/11/2026.** Escrevendo o `SECURITY.md`, a CSP do D2 (`script-src 'self' 'sha256-…'`, `connect-src 'self'`) foi conferida contra o que a borda faz com o HTML. O `docs/deployment.md` (§2, "Verificação depois do redeploy do frontend") e o `02` (l. 3754 e 9371) registram que o Cloudflare da zona **injeta o beacon do Web Analytics** no HTML dos pedidos com cabeçalhos de navegador. O beacon é um `<script>` de `https://static.cloudflareinsights.com`, que a CSP bloqueia, e ele reporta a um endereço fora da origem, que o `connect-src 'self'` também bloqueia. O D2 não trata disso, e nenhum `curl` do D11 o pega, porque o stack local não passa pelo Cloudflare. Efeito provável depois do deploy: o Web Analytics para de contar o painel, e o console mostra violação de CSP em toda página. Não foi medido em produção. A decisão volta ao dono antes da 10.4.

      **Medição (pedido do dono, 01/11/2026; `217-verificacao/10.3-borda.txt`).** `GET /` e `GET /entrar` de produção com cabeçalhos de navegador: a única injeção da borda é o beacon do Web Analytics, `<script type="module" src="https://static.cloudflareinsights.com/beacon.min.js/…" integrity="sha512-…" data-cf-beacon='{"version":"2024.11.0",…}'>`. Nenhum Rocket Loader, ofuscação de e-mail, script ou `<style>` inline a mais, nem atributo de evento. O inline do Mantine é o mesmo do `dist`. **O envio não vai a `cloudflareinsights.com`:** pelo código do beacon, com `version` no `data-cf-beacon` e sem `send.to`, ele envia a `/cdn-cgi/rum` relativo, na própria origem, que o `connect-src 'self'` já cobre. A borda não manda nenhum cabeçalho de segurança. Duas lacunas da instrução: o campo do domínio veio sem preencher (usado o de produção do `02`, l. 38) e a decisão de uso do Web Analytics veio como `[USO / NÃO USO]`. A CSP não mudou, à espera dessa decisão.

      **Decisão do dono (01/11/2026): o painel não usa o Web Analytics.** A CSP fica como está, sem host do Cloudflare. Registrado no D2 (a medição e a decisão) e no `docs/deployment.md`, seção da #217: desligar a injeção automática na zona, com o `curl` que confere; enquanto não for, a violação de CSP do beacon no console, sem efeito no painel; e, se um dia for desejado, só o `script-src https://static.cloudflareinsights.com`, com o `connect-src` só se o `data-cf-beacon` vier sem `version`. **Achado (convenção 23):** a borda não manda HSTS, nem outro cabeçalho de segurança. Issue aberta sem milestone: #227 (https://github.com/aldovrando-oliveira/buteco-agentes/issues/227), com os cabeçalhos medidos e a CSP e o `Referrer-Policy` vindo do nginx desde a #217.
- [x] 10.4 `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, com o que a implementação mediu e as divergências do design (convenção 9), escritos antes do sync e do archive.

      Escritos em 01/11/2026. `CHANGELOG.md`: a entrada em Added (Autenticação), cinco em Changed e uma seção Removed nova (o `POST /auth/login` e o operador único; o módulo de token sobre `sessionStorage`). `02-HISTORICO_E_STATUS.md`: a seção da change no fim, no molde da #180 (o que entrou, o que a implementação mediu, os guardas — 41, e 48 depois do rodapé da 11.5 —, as divergências, os achados e o deploy). A tabela das suítes de fechamento foi preenchida na 11.1, e a linha do `apps/frontend` atualizada pela 2ª rodada.

## 11. Fechamento

- [x] 11.1 As quatro suítes no worktree da change, pelo mesmo script e protocolo da baseline, com `CPU_Speed_Limit = 100` conferido antes de cada uma, inclusive a do `apps/frontend` (com o relatório JSON). Comparação por nome contra a baseline (`.trx` contra `.trx`; JSON da 1.2 contra JSON): ausentes, novos e com resultado mudado, cada ausente explicado por uma tarefa. As saídas em `217-fechamento/`.

      **Resultado, 01/11/2026** (`217-fechamento/`, comparação por `~/.cache/buteco-agents/bin/compare-217.py` em `cmp-*.txt`). O script conferiu `CPU_Speed_Limit = 100` antes de cada suíte, acertou o relógio da VM e registrou `podman ps` vazio:

      | suíte | baseline | fechamento | ausentes | novos | resultado mudado |
      |---|---|---|---|---|---|
      | `apps/frontend` | 1839/1839, 138 arquivos | **1970/1970**, 153 arquivos, 111 s (CPU 100 → 58) | 16 | 147 | 0 |
      | `apps/api` | 1009 + 1 ignorado | **1007 + 1 ignorado**, 121 s (CPU 100 → 100) | 7 | 5 | 0 |
      | `tests/InboxOrchestratorRoundTrip.Tests` | 12/12 | **12/12**, 44 s | 1 | 1 | 0 |
      | `tests/ApiConnectorsRoundTrip.Tests` | 8/8 | **8/8**, 22 s | 0 | 0 | 0 |

      Cada ausente, com a tarefa que o explica:
      - **`apps/api`** (7.2): `AuthLoginTests` ×4 (a rota saiu); `IdentityIssuerCoexistenceTests.HmacLogin_StillIssuesTheOperatorToken_AndItIsAcceptedOnAgents` (saiu, D8); `KnowledgeDocumentCatalogTests.DocumentWrites_WithTokenFromLogin_RecordOperatorAsAuthor` → `DocumentWrites_WithOperatorHmacToken_RecordOperatorAsAuthor` (renomeado); `RouteAuthenticationTests.Login_WithoutToken_StaysAnonymous` → `Session_WithoutToken_StaysAnonymous` (renomeado). Os outros novos: os 2 do `GoneLoginTests` (7.3) e o `RefreshRotationTests.SameRefreshAgainWithinTheLeeway_IssuesASecondPair_AndBothKeepRenewing` (2.1).
      - **`tests/InboxOrchestratorRoundTrip.Tests`** (8.1): `RoundTripTests.OperatorToken_IssuedByApi_IsAcceptedByInboxWithoutNetworkCallToApi` → `RoundTripTests.OperatorHmacToken_SignedWithTheSharedKey_IsAcceptedByInboxWithoutNetworkCallToApi` (renomeado).
      - **`apps/frontend`:** `LoginPage.test` ×4 (4.1 e 5.3: a tela saiu, e o `EntrarPage.test` a substitui); `ProtectedRoute.test` ×2 e `router.test` ×2, que falavam em "token armazenado" e `/login` (4.3); `AppShell.test` "não afirma a identidade do operador no rodapé" (4.4: o requisito mudou); os casos de `401` que limpavam o token do `sessionStorage`, substituídos pelo contrato comum de `401` (5.2): `agentsApi`, `insightsApi` ×2, `connectorsApi`, `knowledgeBasesApi`, `knowledgeIndexApi` e o caso de navegação do `KnowledgeBaseCreatePage.sync`.

      **Renomeados, para a lista do fechamento:** os 2 do `apps/api` e o do `InboxOrchestratorRoundTrip` acima. O `02` tem a tabela preenchida.

      **2ª rodada do `apps/frontend`, depois do rodapé da 11.5** (`217-fechamento/r2/`, CPU 100 → 48, com o stack da 11.3 de pé): **1978/1978**, 154 arquivos, 174 s. Contra a baseline: 16 ausentes (os mesmos), 155 novos, 0 com resultado mudado. Contra a 1ª rodada: 2 ausentes, os dois casos antigos do rodapé ("o rodapé mostra o nome do id token e o Sair, e nenhum avatar" e "sem nome no id token, o rodapé mostra o e-mail e o Sair", substituídos na 4.4), e 10 novos (3 do `AppShell.test`, 6 do `userInitials.test` e 1 do `theme.test`). `npm run lint` e `npx tsc --noEmit` sem erro.

      **3ª rodada, depois da correção do fundo do avatar** (abaixo, na 4.4; `217-fechamento/r3/`, CPU 100 → 48): **1978/1978**, idêntica à 2ª por nome (0 ausentes, 0 novos, 0 com resultado mudado). É a rodada que vale para o `02`.
- [x] 11.2 `npm run lint`, `npx tsc --noEmit` e `npm run build` do `apps/frontend` sem erro; build `--no-incremental` dos projetos .NET tocados, com o mesmo conjunto de avisos da baseline, inclusive o `apps/inbox` (só o comentário da 7.4 mudou nele).

      **Resultado, 01/11/2026** (`217-fechamento/11.2-*`): `npm run lint`, `npx tsc --noEmit` e `npm run build` com exit 0. O build avisa de um chunk acima de 500 kB, e a baseline avisa o mesmo. Build `--no-incremental` nos dois worktrees, com os avisos comparados por conjunto (código e texto, sem o caminho do worktree):

      | projeto | baseline | change |
      |---|---|---|
      | `Buteco.Api` | 0 avisos | 0 avisos |
      | `Buteco.Api.Tests` | 18 avisos, 9 distintos | o mesmo conjunto |
      | `Buteco.Inbox` | 0 avisos | 0 avisos |
      | `InboxOrchestratorRoundTrip.Tests` | 4 avisos, 2 distintos (`NU1903` do SSH.NET) | o mesmo conjunto |

- [x] 11.3 Stack local (`docker-compose.prod.yml`, com o `resolver` do Podman, #212): os `curl` do D11, com a saída colada aqui:
  - `/entrar` e `/entrar/retorno?code=x&state=y` em navegação: `200`, o shell, `Cache-Control: no-store`, a CSP e `Referrer-Policy: no-referrer`;
  - `/agents` em navegação: o shell com os mesmos três cabeçalhos;
  - `POST /auth/login`: `401` do `apps/api`, com o corpo vazio e sem `Content-Type: text/html` (D8). O `Content-Length: 0` não é afirmado: pelo nginx o tamanho pode chegar de outro jeito;
  - `/connect/authorize?client_id=buteco-painel&…` em navegação: `302 Location: /entrar?retorno=…`.

      **Resultado, 01/11/2026.** `podman compose -p stack217 -f docker-compose.prod.yml --env-file <segredos descartáveis> up -d --build`, com `PUBLIC_DOMAIN=http://localhost:18217`, o hash e o e-mail do bootstrap (`217-fechamento/stack/`). O `api` logou `Primeiro Admin criado pelo bootstrap`. O `resolver` do nginx foi trocado **só dentro do contêiner** para o `nameserver` do `resolv.conf` (`10.89.5.1`), com `nginx -t` e `reload` (o contorno da #212). Saída inteira em `217-fechamento/stack/11.3-curl.txt`; os cabeçalhos que importam:

      ```
      GET /entrar (navegação)                       → 200, Content-Type: text/html, Cache-Control: no-store,
                                                      Content-Security-Policy: default-src 'self'; script-src 'self' 'sha256-II5F/q4jI3zI9g9/17tVWddqwse6H5qdSUB46UEQ1Rg='; …,
                                                      Referrer-Policy: no-referrer
      GET /entrar/retorno?code=x&state=y (navegação) → 200, os mesmos quatro
      GET /agents (navegação)                       → 200, os mesmos quatro
      GET /index.html                               → 200, os mesmos quatro (o location = /index.html)
      POST /auth/login                              → 401, Content-Length: 0, sem Content-Type, com a CSP e o Referrer-Policy do server
      GET /connect/authorize?client_id=buteco-painel&… (navegação)
                                                    → 302, Location: /entrar?retorno=%2Fconnect%2Fauthorize%3Fclient_id%3Dbuteco-painel…
      ```

      - **O hash da CSP é o do script inline que o stack serve**, e não só o do `dist` local: o SHA-256 do único `<script>` sem `src` do `/entrar` servido dá `sha256-II5F/q4jI3zI9g9/17tVWddqwse6H5qdSUB46UEQ1Rg=`.
      - **O script de `curl` do `development.md`** (10.3), contra o stack, com a origem e a porta trocadas: sessão `204`, código de 43 caracteres lido do `redirect_url`, access token de 996 caracteres, e com ele `GET /agents`, `GET /channels` e `GET /connectors/providers` com `200`. É o critério de aceite por `curl`; o do navegador é a 11.4.
- [x] 11.4 Conferência no navegador real (Claude in Chrome) contra o stack local da 11.3, com os passos do dono (D10), cada um com o que foi visto:
  1. login, e a URL sem `code` depois do retorno;
  2. recarga, sem pedir login;
  3. segunda aba, sem pedir login;
  4. aba duplicada pelo navegador, e as duas continuam depois de uma renovação (o access token vencido à mão);
  5. logout numa aba, e a outra indo a `/entrar`;
  6. a CSP sem violação no console, em todas as telas do painel.

  E, à parte, no `npm run dev` com o `dotnet run`: login pelo proxy, com o `Set-Cookie` de `buteco-sessao` `HttpOnly` e `SameSite=Strict` no painel de rede. **Critério de aceite** (herdado da #180): com o token do emissor, a lista de agentes (`apps/api`), a de canais (`apps/inbox`) e uma sincronização (`apps/connectors`).

      **Resultado no stack da 11.3, 01/11/2026, Claude in Chrome** (o Chrome do dono, numa aba nova do grupo da extensão), com o primeiro Admin do bootstrap do stack:
      1. **Login:** `/agents` sem sessão foi a `/entrar?voltarPara=%2Fagents`; e-mail e senha, e a aba terminou em `/agents`, sem `code` na URL. O registro `buteco.sessao` tem os três tokens, `nome` `admin` e o e-mail; o `buteco.login` foi apagado; o `document.cookie` vem vazio (o `buteco-sessao` é `HttpOnly`).
      2. **Recarga:** `/agents` de novo, sem pedir login e sem `POST /connect/token` (o access token tinha 15 min).
      3. **Segunda aba:** `/channels` abriu sem pedir login, com a lista do `apps/inbox`.
      4. **Renovação entre abas:** **a aba duplicada pelo menu do navegador não foi feita.** As ferramentas não alcançam o "Duplicar" do Chrome, e o `window.open` da mesma URL foi bloqueado como pop-up, por não ter gesto do usuário. Fica para a validação do dono. Feito com as duas abas existentes: o `accessExpiresAt` do registro compartilhado foi posto no passado, e as duas abas recarregaram no mesmo segundo (`setTimeout` para um instante comum). Houve **um único** `POST /connect/token` (200), na aba de `/agents`. A de `/channels` usou o par novo sem fazer pedido (o access e o refresh do registro mudaram), e as duas telas carregaram.
      6. **CSP** (feito antes do 5, porque precisa da sessão): **o leitor de console da extensão não mostra violação de CSP.** Um `<script>` inline injetado de propósito foi bloqueado (o evento `securitypolicyviolation` disparou com `script-src-elem inline`, e o script não rodou), e o console lido veio vazio. As violações foram contadas por esse evento, com o ouvinte instalado antes de percorrer as telas pela navegação do painel: `/inventory`, `/agents`, `/agents/new`, `/mcp-servers`, `/mcp-servers/new`, `/knowledge-bases`, `/knowledge-bases/new` (com a origem "Sincronizada" escolhida), `/channels`, `/channels/new` e `/insights`, **zero violações**. A carga inicial do shell, que acontece antes de o ouvinte existir, fica coberta pelo hash conferido contra o HTML servido (11.3) e pelos assets, todos da própria origem. As telas de detalhe ficaram de fora porque o stack não tem cadastro.
      5. **Logout:** "Sair" na aba de `/knowledge-bases/new` chamou `/connect/logout?id_token_hint=…&post_logout_redirect_uri=http%3A%2F%2Flocalhost%3A18217%2Fentrar` e terminou em `/entrar`. A aba de `/channels` foi sozinha a `/entrar?voltarPara=%2Fchannels`, com o registro ausente.

      **Critério de aceite no navegador:** a lista de agentes (`apps/api`) e a de canais (`apps/inbox`) carregaram com o token do emissor, e o cadastro de base chamou `GET /connectors/providers` (`apps/connectors`) com `200`. **Uma sincronização de fato não foi feita:** o stack subiu sem a chave do Drive (`GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64` vazia), então não há provedor para sincronizar.

      **Desenvolvimento (`npm run dev` com `dotnet run`), 01/11/2026**, contra o Postgres e o RabbitMQ de dev (o bootstrap logou "ignorado: já existem usuários"), com o primeiro Admin de dev (`operador@buteco.local`):
      - `curl` pelo proxy do Vite (`POST http://localhost:5173/auth/session`, `Origin: http://localhost:5173`): `204` e `set-cookie: buteco-sessao=…; expires=…; path=/; samesite=strict; httponly`, sem `Secure`, como deve ser em `Development`;
      - no navegador, `http://localhost:5173/agents` foi a `/entrar?voltarPara=%2Fagents`; com e-mail e senha, o fluxo passou pelo `/connect/authorize` e pelo `/entrar/retorno` (pelo proxy, com `iss=http://localhost:5017/`) e terminou em `/agents`, com a lista de agentes do banco de dev;
      - **o painel de rede da extensão não mostra cabeçalhos de resposta**, então os atributos do `Set-Cookie` foram lidos pelo `curl` acima, e não no navegador;
      - **o leitor de rede da extensão registra o `POST /auth/session` como `503`.** O `fetch` do mesmo pedido, feito de dentro da página, recebeu `204`, e o painel só navega ao authorize no `204`. A ferramenta registra errado a resposta `204` sem corpo; não é o `apps/api`.

      **Fechamento pelo dono, 01/11/2026:**
      - **aba duplicada pelo menu do navegador (passo 4):** feita pelo dono, tudo certo;
      - **critério de aceite, decisão do dono:** a sincronização real com o Drive não é necessária. O critério prova que o `apps/connectors` aceita o token do emissor, e isso está provado pelo `200` de `GET /connectors/providers`, pelo `curl` (11.3) e pelo navegador. A sincronização passa pela mesma autenticação, e só acrescentaria o Drive e o token de serviço, que esta change não toca.
- [x] 11.5 Validação visual pelo dono (convenção 14): o `/entrar` nos estados `inicio`, `bloqueado` e `sessao-expirada` contra o protótipo, quadro a quadro, com as divergências registradas (convenção 17: N minutos do `Retry-After`, sem "Esqueceu a senha?" e sem o convite). O archive só depois dela.

      **Resultado, 01/11/2026.** Primeira passada: divergência no rodapé da barra lateral, porque o protótipo de autenticação foi atualizado com o botão de tema. O rodapé foi refeito pelo protótipo (D7, 4.4, spec `frontend-app-shell`, com 7 guardas), e a #184 e a #183 foram comentadas. Segunda passada, no stack da 11.3 com o `frontend` reconstruído: **o rodapé e os estados `inicio`, `bloqueado` e `sessao-expirada` do `/entrar` aprovados pelo dono**, com as divergências já registradas (N minutos do `Retry-After`, sem "Esqueceu a senha?" e sem o convite).
- [x] 11.6 `openspec validate painel-entra-pelo-emissor --strict`. Valid em 01/11/2026, depois de todos os registros acima.
- [x] 11.7 `/opsx:sync` e `/opsx:archive` dentro do worktree, com o `02` e o `CHANGELOG` já escritos (10.4), e `openspec validate --all --strict` depois.

      **Resultado, 01/11/2026** (`217-fechamento/11.7-archive.log`): `openspec archive painel-entra-pelo-emissor -y` dentro do worktree, depois de o dono aprovar a 11.5 e com o `02` e o `CHANGELOG` escritos (10.4). As nove deltas foram aplicadas: + 15, ~ 11, − 3. A `panel-issuer-session` foi criada, com o "Purpose" escrito depois (o archive o deixa TBD). Os avisos do archive são do proposal, cujos itens de capacidade o parser lê como requisitos, e não bloqueiam. `openspec validate --all --strict`: 81 de 81. As 4 tarefas abertas no momento do archive eram a própria 11.7 e as três seguintes.
- [ ] 11.8 O único commit da change, depois do sync e do archive, com tudo junto (código, testes, docs e OpenSpec), só com o pedido do dono naquela mensagem e sem atribuição.
- [ ] 11.9 Push e PR com `Closes #217`, só com o ok do dono naquela mensagem; mover a #217 para `In review`.
- [ ] 11.10 Depois do merge: remover os dois worktrees, um de cada vez, e a branch local com `git branch -d`.
