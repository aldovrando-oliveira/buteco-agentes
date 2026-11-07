Cada guarda marcado **(guarda)** só conta depois de visto reprovar contra o defeito que ele existe para pegar (convenção 15):

1. o `~/.cache/buteco-agents/bin/guarda-246.sh` reintroduz o defeito nomeado. É uma cópia do `guarda-245.sh`, com os projetos `inbox` e `connectors` acrescentados. A mutação toca só o trecho nomeado, e nunca um diretório restaurado ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

Fica registrado na tarefa:

- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-246.sh`:

- a principal é a `frontend`;
- a `api`, a `inbox` e a `connectors` rodam com o filtro do teste novo da tarefa 1, nos dois worktrees.

Vale a regra assimétrica: verde conta mesmo com a CPU caindo, e reprovação só conta com a CPU em 100 do início ao fim. Antes de cada rodada da `frontend`, o load fica abaixo de 6.

**A baseline** é `~/.cache/buteco-agents/baseline-246-20261106-2304/`, em `8884d89`:

- 2079 aprovados em 160 arquivos, 0 reprovados e 0 pulados, em 130 s;
- o relatório por nome está em `nomes.txt` e `nomes-norm.txt` (2079 linhas), igual nome a nome ao da rodada final da #245;
- a rodada começou com load 2,95 e CPU em 100, e a CPU caiu até 46 (`APROVADA`).

**Parada:** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura. A correção que só acrescenta teste ou cenário para pôr um guarda no lugar certo, ou que só toca comentário ou documentação, segue e fica registrada. Nenhum caso existente é removido, renomeado ou afrouxado.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-246.sh`, cópia do `guarda-245.sh` com os projetos `inbox` e `connectors` (`dotnet test` com `--filter` nos projetos de teste de cada um). Conferido com:
  - uma mutação que não altera nada: recusa com "defeito não aplicado";
  - uma que altera, num arquivo de cada projeto: o arquivo volta com o mesmo sha.
  **Feito (07/11/2026):** a mutação nula recusa ("defeito não aplicado"). Uma mutação real (um comentário no fim do arquivo) em `apps/inbox/…/IssuerTokenValidationTests.cs` e em `apps/connectors/…/IssuerTokenValidationTests.cs` rodou o filtro `HmacShapedToken_SignedWithTheOldKey_Is401` (1 de 1 aprovado em cada) e voltou com o sha igual (`eaa98ee8…` e `c40bfe18…`). Os projetos `api` e `frontend` são os do `guarda-245.sh`, já conferidos na #245.
- [x] 0.2 (apps/frontend) `qrcode.react` **4.2.0**, exato (sem `^`), pelo `npm install --save-exact`. Conferir no lock:
  - a versão;
  - a licença ISC;
  - nenhuma dependência transitiva nova além do pacote.
  **Feito:** `"qrcode.react": "4.2.0"` no `package.json`, sem `^`. No lock, 4.2.0, ISC, sem `dependencies`, e só ele entra (`+1` pacote).

## 1. O `401` de token sem corpo, fixado nos três apps (design D10, complemento da Q0)

- [x] 1.1 (apps/api) `IssuerTokenAcceptanceTests`: `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis`, com dois casos numa rota autenticada (`GET /agents`): sem token e com um token que não valida. Cada caso confere:
  - `401` com `WWW-Authenticate: Bearer`;
  - o `Content-Type` diferente de `application/problem+json`;
  - o corpo vazio.

  O comentário diz que o painel depende disso (`apps/frontend/src/auth/sessionFetch.ts`) e aponta a #247.
  **(guarda)** Defeito: um `OnChallenge` no `IssuerTokenAuthentication.cs` do `apps/api` que escreve `{"title":"Não autorizado."}` com `application/problem+json` no `401`. Reprova o teste novo.
  **Feito (07/11/2026):** `[Theory]` com `null` e `"nao.e.um.token"` em `GET /agents`, 2 de 2 aprovados.
  **Visto reprovar** (`~/.cache/buteco-agents/246-guardas/g1.1-api`): um `OnChallenge` que escreve `{"title":"Não autorizado."}` com `application/problem+json` (e mantém o `WWW-Authenticate: Bearer`) → os dois casos de `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis`, `Assert.NotEqual() Failure: Strings are equal`. Voltou: `IssuerTokenAuthentication.cs` do `apps/api` (sha igual ao de antes: `d3b9f972…`).
- [x] 1.2 (apps/inbox) `IssuerTokenValidationTests`: o mesmo teste, com os dois casos em `GET /channels`.
  **(guarda)** Defeito: o `ChallengeWithoutKeysAsUnavailable` do `apps/inbox` escrevendo o mesmo corpo problem+json quando há chaves. Reprova o teste novo.
  **Feito:** os mesmos dois casos em `GET /channels`, 2 de 2 aprovados.
  **Visto reprovar** (`g1.2-inbox`): o ramo de chaves obtidas do `ChallengeWithoutKeysAsUnavailable` escrevendo o corpo problem+json → os dois casos, `Assert.NotEqual() Failure: Strings are equal`. Voltou: `apps/inbox/src/Buteco.Inbox/Auth/IssuerTokenAuthentication.cs` (sha igual: `6c943562…`).
- [x] 1.3 (apps/connectors) `IssuerTokenValidationTests`: o mesmo teste, com os dois casos na rota de operador do arquivo (`OperatorRoute`).
  **(guarda)** Defeito: o mesmo do 1.2, no `apps/connectors`. Reprova o teste novo.
  **Feito:** os mesmos dois casos em `OperatorRoute` (`/connectors/providers`), 2 de 2 aprovados.
  **Visto reprovar** (`g1.3-connectors`): a mesma mutação do 1.2 → os dois casos, a mesma mensagem. Voltou: `apps/connectors/src/Buteco.Connectors/Auth/IssuerTokenAuthentication.cs` (sha igual: `57d7a4c1…`).
- [x] 1.4 (apps/api, apps/inbox, apps/connectors) As três suítes com o filtro `FullyQualifiedName~IssuerToken`, nos dois worktrees. Mesmos casos da baseline, mais os novos, e nenhum outro nome muda.
  **Feito (07/11/2026):** `~/.cache/buteco-agents/246-backend-20261107-0008/`, pelo `suite-246.sh` (com os modos `inbox-pN` e `connectors-pN` acrescentados, com build), filtro `FullyQualifiedName~IssuerToken`. Todas as rodadas `APROVADA`:

  | suíte | baseline `8884d89` | change | por nome (`*-nomes.txt`, pelo `.trx`) |
  |---|---|---|---|
  | `api` | 14 de 14 (CPU 100) | **16 de 16** (CPU até 83) | os 14 iguais; entram os 2 de `TokenRejection_401_…` |
  | `inbox` | 24 de 24 (CPU até 58) | **26 de 26** (CPU até 90) | os 24 iguais; entram os 2 |
  | `connectors` | 30 de 30 (CPU até 46) | **32 de 32** (CPU até 97) | os 30 iguais; entram os 2 |

  Verde conta mesmo com a CPU caindo.

## 2. O `sessionFetch` devolve o `401` de recusa da rota (apps/frontend, D10)

- [x] 2.1 (apps/frontend) `src/auth/sessionFetch.ts`: no `401`, antes de renovar, o `Content-Type` com `application/problem+json` devolve a resposta à tela, sem renovar, repetir nem encerrar. O comentário cita a decisão do dono (Q0), o teste dos três apps (tarefa 1) e a #247.
  **Feito (07/11/2026):** `isRouteRefusal(response)` compara o tipo do `Content-Type` (sem parâmetros, sem caixa) com `application/problem+json`, e o `401` dela volta antes da renovação. O comentário cita a Q0, o teste dos três apps e a #247.
- [x] 2.2 (apps/frontend) `sessionFetch.test.ts`: os cenários novos da `panel-issuer-session`:
  - "401 com problem+json volta à tela sem renovar": a renovação não é chamada, o `fetch` é chamado uma vez, o registro continua e não há navegação;
  - "401 sem corpo continua renovando".

  Os casos de hoje ficam como estão.
  **(guarda)** Defeito: a conferência do `Content-Type` removida. Reprova "401 com problem+json volta à tela sem renovar".
  **(guarda)** Defeito: o ramo do problem+json chamando a renovação antes de devolver (sem repetir o pedido). Reprova "401 com problem+json volta à tela sem renovar", que confere que a renovação não foi chamada.
  **(guarda)** Defeito: a conferência trocada por "qualquer `Content-Type`" (`response.headers.has('content-type')`). Reprova "401 sem corpo continua renovando", cujo `401` simula o do JwtBearer: sem corpo e sem `Content-Type`. Se o `fetch` falso puser um `Content-Type` padrão na resposta vazia, o caso o remove e a tarefa registra.
  **Feito:** dois casos novos: "401 com problem+json volta à tela sem renovar nem repetir" (com `charset` no tipo) e "401 sem corpo continua renovando e repete com o token novo" (`new Response(null, { status: 401 })`). O arquivo tem 11 de 11 aprovados, e os 9 de antes não mudaram.
  **Vistos reprovar** (`~/.cache/buteco-agents/246-guardas/`, 07/11/2026):
  - g2.2-sem-conferencia → `401 com problem+json volta à tela sem renovar nem repetir`. Voltou: `sessionFetch.ts` (sha igual: `d61acd47…`);
  - g2.2-renova-antes → o mesmo caso. Voltou: idem.
  - **Correção do registro (premissa falsa que não reduz cobertura):** g2.2-qualquer-tipo (`response.headers.has('content-type')`) não reprova em "401 sem corpo…", porque esse `401` não tem `Content-Type`, e o defeito o trata igual. Ele reprova em quatro casos que já existiam, cujo `401` é JSON comum (`application/json`): `401 seguido de sucesso não desloga…`, `segundo 401 encerra a sessão…`, `no apps/connectors, o segundo 401 não encerra…` e `invalid_grant na renovação encerra…`. Esses casos são o guarda. Voltou: idem.

## 3. O aviso "Senha alterada." (apps/frontend, D3)

- [x] 3.1 (apps/frontend) `features/auth/passwordChangedNotice.ts`:
  - `markPasswordChanged()` grava `buteco.aviso.senha-alterada` no `sessionStorage`;
  - `takePasswordChanged()` lê e apaga, devolvendo um booleano;
  - os dois com `try/catch`, como o `linkToken`.

  `messages.ts` ganha `SENHA_ALTERADA` = "Senha alterada. Entre com a nova senha.".
  **Na implementação (07/11/2026):** em vez de um `takePasswordChanged()` que lê e apaga, são três funções: `markPasswordChanged`, `hasPasswordChanged` e `clearPasswordChanged`. O StrictMode chama o inicializador do `useState` duas vezes, e apagar na leitura faria a segunda chamada não ver a chave (o mesmo cuidado do `linkToken` da #245). O `/entrar` lê no inicializador e apaga num efeito da montagem. O comportamento é o do requisito, e o caso do StrictMode o fixa.
- [x] 3.2 (apps/frontend) `EntrarPage.tsx`: lê a chave uma vez, no inicializador do `useState`. Com ela, mostra o aviso verde em `role="status"`, no lugar de "Sua sessão expirou…". O aviso some na primeira submissão.
  **Feito:** `daTrocaDeSenha` (lido uma vez) e `senhaAlterada` (o aviso visível). O aviso verde, em `role="status"`, fica acima do "sessão expirada", que não aparece durante a página inteira quando a troca é o motivo. O aviso some no envio e no envio incompleto.
- [x] 3.3 (apps/frontend) `passwordChangedNotice.test.ts` e `EntrarPage.test.tsx`, um caso por cenário do requisito "Aviso de senha alterada no /entrar".
  **(guarda)** Defeito: o `takePasswordChanged` sem o `removeItem`. Reprova "O aviso aparece uma vez só".
  **(guarda)** Defeito: o aviso de sessão expirada testado antes do de senha alterada. Reprova "O aviso vence a sessão expirada".
  **Feito:** `passwordChangedNotice.test.ts` (3 casos) e o `describe('aviso de senha alterada')` no `EntrarPage.test.tsx` (6 casos, um deles no StrictMode). Os dois arquivos: 45 de 45.
  **Vistos reprovar** (`~/.cache/buteco-agents/246-guardas/`):
  - g3.3-sem-remove (o `removeItem` trocado por nada) → `o aviso aparece uma vez só`, e também `apagar tira a chave`, `com a chave na aba, avisa em role="status" e apaga a chave` e `no StrictMode, o aviso aparece e a chave sai`. Voltou: `passwordChangedNotice.ts` (sha igual: `e31ab18a…`);
  - g3.3-precedencia (o `&& !daTrocaDeSenha` tirado) → `vence a sessão expirada`, e só ele. Voltou: `EntrarPage.tsx` (sha igual: `d4fe895d…`).

## 4. A API de Minha conta (apps/frontend)

- [x] 4.1 (apps/frontend) `features/account/api/accountApi.ts`:
  - a cópia do `request<T>` e do `ApiError` (convenção 2, com o comentário das outras cópias), com o `204` sem corpo;
  - as dez chamadas: `getAccount`, `changePassword`, `getTwoFactor`, `setupTwoFactor`, `confirmTwoFactor`, `regenerateRecoveryCodes`, `removeTwoFactor`, `listSessions`, `revokeSession`, `revokeOtherSessions`;
  - os tipos do contrato da #218 e da #219.

  O `ApiError` leva o `title` e o `retryAfter`.
  **Feito (07/11/2026):** as dez chamadas e os tipos; o `ApiError` com `status`, `title`, `problem` e `retryAfter`; o `204` devolve `undefined`.
- [x] 4.2 (apps/frontend) `features/account/test/fakeAccountApi.ts`: o `fetch` falso por caminho e método, com a fila de respostas, o registro dos pedidos e a falha de rede. Ligado ao `sessionFetch` pelo mesmo runtime de teste que as outras páginas usam.
  **Feito:** `fakeAccountApi(routes)` sobre o `fetch` global (o que o `authRuntime` usa), com uma fila por `MÉTODO caminho`, onde a última resposta fica valendo; `problem(status, title, headers)` responde `application/problem+json`; `'rede'` lança; uma função atrasa a resposta. Traz também as respostas comuns (`ACCOUNT`, `TWO_FACTOR_OFF`, `SESSIONS`, `defaultRoutes()`).
- [x] 4.3 (apps/frontend) `accountApi.test.ts`: o método, o caminho, o corpo e o `Authorization` de cada chamada; o `204` sem corpo; o `ApiError` com `title` e `retryAfter`.
  **Feito:** 13 casos: os três `GET`; os sete `POST`, com o corpo, o `Authorization` e o `Content-Type`; o `204`; o `ApiError` do `423`, com o `title` e o `Retry-After`; e o `401` de senha errada, sem renovar nem deslogar, pelo `sessionFetch` real.

## 5. A página, a rota, os Dados, as datas e o rodapé (apps/frontend, D1, D2, D9)

- [x] 5.1 (apps/frontend) `app/routes.tsx`: `account` dentro do `ProtectedRoute` e da casca. `router.test.tsx`: `/account` sem sessão vai a `/entrar` com `voltarPara=/account`.
  **Feito (07/11/2026):** a rota `account`, ao lado de `insights`. O caso novo em `router.test.tsx`, "`/account` sem sessão vai ao /entrar com o voltarPara", confere `voltarPara=/account`.
- [x] 5.2 (apps/frontend) `features/account/accountDates.ts`: `formatRelative(iso, now, timeZone)` e `formatDate(iso, timeZone)`. `accountDates.test.ts`, com os dois cenários do requisito "Datas de Minha conta".
  **(guarda)** Defeito: "hoje" decidido pela data UTC, e não pelo fuso. Reprova "Virada do dia pelo fuso".
  **Feito:** `formatDate(iso, timeZone?)` e `formatRelative(iso, now?, timeZone?)`, que decidem "hoje" e "ontem" pelo calendário do fuso. 3 casos.
  **Visto reprovar** (`246-guardas/g5.2-utc`): "hoje" pela data UTC (`toISOString().slice(0, 10)`) → `a virada do dia é a do fuso, e não a do UTC` (`Expected: "ontem, 23:30"`, `Received: "hoje, 23:30"`) e também `hoje, ontem e antes, no fuso`. Voltou: `accountDates.ts` (sha igual: `63b67711…`, conferido também contra a cópia).
- [x] 5.3 (apps/frontend) `AccountPage.tsx`: o título, o subtítulo e os quatro cartões, cada um com o seu `useQuery`, o `Skeleton` do carregamento e o aviso de erro com "Tentar de novo". `AccountDataCard.tsx`: o requisito "Cartão Dados", com o "use Usuários" sem link.
  **Feito:** `AccountPage` (`maw` 880), `AccountCard` (a seção com `aria-labelledby`, o título `h3` e a ação), `CardLoading`, `CardLoadError` e `AccountDataCard` (uma grade `dl`). Os avisos de falha ficaram em `cardFailure.ts` (`failureText`, `failureNotice` com a cor, `lockedText`, `blockedUntil`), e o `Retry-After` em `useBlockedUntil.ts`, compartilhados pelos cartões (tarefa 9).
- [x] 5.4 (apps/frontend) `AccountPage.test.tsx`: os cenários de "Página Minha conta em /account" e de "Cartão Dados".
  **(guarda)** Defeito: o erro de um cartão levado ao `errorElement` da página, no lugar do aviso do cartão. Reprova "Erro num cartão não derruba os outros".
  **Feito:** `AccountPage.test.tsx`, 5 casos: os quatro cartões em ordem; o erro de um que não derruba os outros; "Tentar de novo"; os Dados; e o "use Usuários" sem link. O harness `test/renderAccount.tsx` (QueryClient sem `retry`, Mantine, router) é um duplo novo.
  **Visto reprovar** (`g5.4-erro-na-pagina`): `throwOnError: true` no `useQuery` das sessões → `o erro de um cartão não derruba os outros` e também `"Tentar de novo" relê o cartão`. Voltou: `AccountSessionsCard.tsx` (sha igual: `8e1a4578…`).
- [x] 5.5 (apps/frontend) `AppShell.tsx`: o nome e a segunda linha dentro de um `Link` para `/account`, com `title` "Minha conta" e `aria-current="page"` quando a rota é `/account` ou começa com `/account/`. O estilo segue o do bloco de hoje, sem sublinhado nem cor de link, e com o foco visível. `AppShell.test.tsx`: os três cenários novos do requisito do rodapé. Os casos de hoje do rodapé continuam.
  **(guarda)** Defeito: o `aria-current` sempre ausente. Reprova "Em Minha conta, o link marca a página atual".
  **(guarda)** Defeito: o "Sair" dentro do link. Reprova "O avatar e os botões ficam fora do link".
  **Feito:** o nome e a segunda linha dentro de um `UnstyledButton component={Link}` para `/account`, com `title` "Minha conta" e o `aria-current` calculado pelo `pathname`. O `title` com o nome completo saiu das duas linhas, porque o link agora leva o `title` "Minha conta". `AppShell.test.tsx`: 3 casos novos (25 de 25), e os 22 de antes passam sem mudança.
  **Vistos reprovar** (`246-guardas/`):
  - g5.5-sem-aria-current → `em Minha conta, o link marca a página atual, e o menu não tem item para ela`. Voltou: `AppShell.tsx` (sha igual: `26f16614…`);
  - g5.5-sair-no-link (o fechamento do link levado para depois do "Sair") → `o avatar e os botões ficam fora do link` e também `dá um ícone decorativo a cada item…`. Voltou: idem. **A primeira tentativa foi descartada:** ela só tirava o fechamento, quebrava o JSX e reprovava por compilação ("no tests"), e não pelo defeito.

## 6. Senha (apps/frontend, D2, D3)

- [x] 6.1 (apps/frontend) `AccountPasswordCard.tsx`, conforme o requisito "Troca da própria senha":
  - fechado e aberto;
  - os três `PasswordField`;
  - a política e a repetição conferidas antes do envio (`passwordFitsPolicy`, `POLITICA_DE_SENHA`, `SENHAS_DIFERENTES`);
  - o `401` e o `400` no campo;
  - o `423` e as falhas passageiras (tarefa 9);
  - no `204`, o `markPasswordChanged()` e depois o `logout()` de `src/auth/session.ts`, com o botão em "Saindo…".
  **Feito (07/11/2026):** como descrito. Depois do `204`, `markPasswordChanged()`, depois `setSaindo(true)` e `await logout()`. O botão mostra "Saindo…" e fica desabilitado. O aviso usa o `failureNotice` (amarelo no `423` e no `429`).
- [x] 6.2 (apps/frontend) `AccountPasswordCard.test.tsx`, um caso por cenário do requisito, com o `sessionFetch` real sobre o `fetch` falso e o `logout` espiado.
  **(guarda)** Defeito: o `logout()` chamado antes do `markPasswordChanged()`. Reprova "Depois do 204, o aviso e o logout", que confere a ordem.
  **(guarda)** Defeito: a validação de tamanho removida. Reprova "Senha nova curta não envia".
  **(guarda)** Defeito: o `401` mostrado como aviso genérico do cartão, e não no campo "Senha atual". Reprova "Senha atual errada fica no campo, sem deslogar".
  **(guarda, ponta a ponta com a tarefa 2)** Defeito: o mesmo da tarefa 2.2, a conferência do `Content-Type` removida do `sessionFetch`. Reprova também "Senha atual errada fica no campo, sem deslogar": o pedido sai duas vezes, e o painel navega a `/entrar`.
  **Feito:** 9 casos, com o `sessionFetch` real sobre o `fakeAccountApi`, e o `logout` trocado por `vi.mock` do módulo `auth/session` (o resto do módulo é o original). O `fakeAccountApi` ganhou a resposta como `Response`.
  **Vistos reprovar** (`246-guardas/`):
  - g6.2-logout-antes (o `logout()` antes do `markPasswordChanged()`) → `depois do 204, grava o aviso e só então chama o logout`. Voltou: `AccountPasswordCard.tsx` (sha igual: `4abc3b34…`);
  - g6.2-sem-tamanho → `senha nova curta não envia`. Voltou: idem;
  - g6.2-401-generico (o ramo do `401` desligado: a recusa vai ao aviso do cartão) → `a senha atual errada fica no campo, sem deslogar`. Voltou: idem;
  - g6.2-ponta-a-ponta (a conferência do `Content-Type` removida do `sessionFetch`) → `a senha atual errada fica no campo, sem deslogar`, por espera de 5 s. **Registro:** com o defeito, o pedido entra na renovação, que neste duplo não chega ao `/connect/token` (os pedidos registrados foram só `GET /account` e `POST /account/password`), e a tela não recebe a recusa. O caso reprova pelo defeito, a recusa que não volta à tela, mas não mostra a repetição nem o logout. Estes ficam fixados no `sessionFetch.test.ts` (2.2). Voltou: `sessionFetch.ts` (sha igual: `d61acd47…`).

## 7. Verificação em 2 etapas (apps/frontend, D4, D5)

- [x] 7.1 (apps/frontend) `TwoFactorCard.tsx`, a máquina de estados do D4:
  - o estado do `GET` (`indisponivel`, `desligada`, `ativa`, poucos códigos e zero);
  - o passo da senha, o do QR e o dos códigos;
  - "Gerar novos códigos" e "Remover verificação";
  - os textos do quadro no QR (Q6);
  - as mutações com `gcTime: 0`, e o resultado guardado no estado do componente.
  **Feito (07/11/2026):** a máquina de estados (`Passo`: `estado`, `senha`, `qr`, `codigos`, `regenerar`, `remover`), `fewCodesText` (de 1 a 3, singular para 1, o texto próprio para 0) e os textos do quadro no QR (`CONFIRMA_CODIGO_INVALIDO`, `CADASTRO_VENCEU`). O `409` e o `403` releem o `GET` e voltam ao estado. O código vazio mostra "Informe o código." (estado sem quadro, entra na lista da validação visual). O singular de "N códigos de recuperação restantes." para 1 também é sem quadro.
  **Correção do design e da spec (premissa falsa que só reforça a garantia):** o design previa as mutações com `gcTime: 0`. Mesmo assim, o observador do `useMutation` guarda o `data` enquanto o cartão está montado, também depois de sair do passo. As quatro chamadas viraram chamadas diretas, sem `useMutation`, e o resultado vai só para o estado do componente. O D5 do design, o requisito "Segredo e códigos só na memória da tela" e o guarda da 7.4 foram ajustados, e o `openspec validate --strict` continua válido.
- [x] 7.2 (apps/frontend) `TwoFactorQr.tsx`:
  - o `QRCodeSVG` com `size` 164, fundo branco, margem e `title`/`aria-label` "QR para o app autenticador", sem `imageSettings`;
  - o segredo em grupos de 4, com o `CopyButton` copiando o valor cru.
  **Feito:** `TwoFactorQr` com `QRCodeSVG` (`size` 164, `marginSize` 2, fundo `#ffffff`, módulos `#191a1c`, numa moldura branca), o `title` e o `aria-label`; `groupSecret` agrupa de 4 em 4; o `CopyButton` copia o valor cru.
- [x] 7.3 (apps/frontend) `RecoveryCodes.tsx` e `recoveryCodesFile.ts`: os códigos, "Copiar todos" (um por linha) e "Baixar arquivo .txt" (`Blob`, `createObjectURL`, `<a download>`, `revokeObjectURL`), com o cabeçalho, o e-mail e a data. `recoveryCodesFile.test.ts`: o texto do arquivo e a liberação da URL.
  **Feito:** `recoveryCodesText` (cabeçalho, conta, data, a orientação e os códigos) e `downloadRecoveryCodes` (`Blob` `text/plain;charset=utf-8`, `<a download>`, `revokeObjectURL` num `finally`). `RecoveryCodes` com a lista (`role="list"`, com o nome "Códigos de recuperação"), "Copiar todos", "Baixar arquivo .txt" e "Concluir". **Na implementação:** o `recoveryCodesFile.test.ts` previsto ficou dentro do `TwoFactorCard.test.tsx`, que confere o conteúdo do `Blob`, o nome do arquivo e a liberação da URL pelo clique real.
- [x] 7.4 (apps/frontend) `TwoFactorCard.test.tsx`, um caso por cenário dos requisitos:
  - "Estado da verificação em 2 etapas";
  - "Ligar a verificação em 2 etapas";
  - "QR e segredo do cadastro";
  - "Códigos de recuperação mostrados uma vez";
  - "Segredo e códigos só na memória da tela";
  - "Gerar novos códigos e remover a verificação".

  **(guarda)** Defeito: "Ligar verificação" habilitado com `cadastroDisponivel: false`. Reprova "Cadastro fechado".
  **(guarda)** Defeito: o cartão `ativa` escondido quando `cadastroDisponivel` é falso. Reprova "Ativa com o cadastro fechado continua gerenciável".
  **(guarda)** Defeito: o limite de poucos códigos em `< 3`. Reprova "Poucos códigos", no caso do 3.
  **(guarda)** Defeito: o `401` do `confirm` com o `title` do servidor no lugar do texto do quadro. Reprova "Código errado no QR".
  **(guarda)** Defeito: o `QRCodeSVG` com `imageSettings` (um `src` de `data:`). Reprova "O QR não traz imagem nem script".
  **(guarda)** Defeito: o `CopyButton` do segredo com o valor agrupado. Reprova "Copiar copia sem espaços".
  **(guarda)** Defeito: o `revokeObjectURL` removido. Reprova "O .txt sai do navegador".
  **(guarda)** Defeito: o `setup` pelo `useMutation` (com o `gcTime` padrão), no lugar da chamada direta. Reprova "Nada no cache depois de sair".
  **(guarda)** Defeito: o segredo gravado no `sessionStorage` "para sobreviver à recarga". Reprova "Nada no storage".
  **Feito:** `TwoFactorCard.test.tsx`, 28 casos (os da 9.2 incluídos).
  **Teste corrigido antes do guarda (asserção vazia):** "nada no storage…" lia o storage por `{ ...sessionStorage }`, que no jsdom não enumera as chaves. O guarda g7.4-segredo-no-storage **passou na primeira rodada** (28 de 28), o que mostrou o defeito. O caso agora lê chave por chave (`storage.key(i)`), e o guarda refeito reprova.
  **Vistos reprovar** (`246-guardas/`, 07/11/2026), todos com a volta pela cópia e o sha conferido (`TwoFactorCard.tsx` `e6130d35…`, `TwoFactorQr.tsx` `3d296849…`, `recoveryCodesFile.ts` `18fe07cd…`):
  - g7.4-ligar-habilitado → `cadastro fechado: … "Ligar verificação" desabilitado`, e também `cadastro fechado no meio do caminho…`;
  - g7.4-ativa-escondida → `ativa com o cadastro fechado continua gerenciável`;
  - g7.4-limite-menor-que-3 → `poucos códigos: com 3, o aviso do número`;
  - g7.4-title-do-servidor → `código errado no QR: o texto do quadro, no campo`;
  - g7.4-image-settings → `o QR não traz imagem, foreignObject nem script`, e também `o QR é o do otpauthUri`;
  - g7.4-copia-agrupado → `o segredo em grupos de 4, e "Copiar" copia sem espaços`;
  - g7.4-sem-revoke → `o .txt sai do navegador… e libera a URL do blob`;
  - g7.4-use-mutation (o `setup` pelo `useMutation`) → `nada no cache do TanStack Query depois de sair`;
  - g7.4-segredo-no-storage (refeito) → `nada no storage, no passo do QR e no dos códigos`.

## 8. Sessões (apps/frontend, D6)

- [x] 8.1 (apps/frontend) `AccountSessionsCard.tsx`, conforme o requisito "Sessões da própria conta":
  - a tabela, os nomes pela posição e a `atual` pelo campo;
  - a confirmação de uma e a das outras;
  - os textos do `{encerradas}` (Q10);
  - o `404` relendo a lista;
  - "Encerrar e sair" pelo `logout()`.
  **Feito (07/11/2026):** como descrito. "Encerrar" de cada linha tem o `aria-label` "Encerrar a Sessão N", e a confirmação e o aviso ficam acima da tabela. A falha passageira vira o aviso do cartão.
- [x] 8.2 (apps/frontend) `AccountSessionsCard.test.tsx`, um caso por cenário do requisito.
  **(guarda)** Defeito: o selo "Esta sessão" na primeira linha, e não na `atual`. Reprova "A sessão atual é marcada pelo campo".
  **(guarda)** Defeito: "Encerrar" revogando sem confirmar. Reprova "Encerrar uma pede confirmação".
  **(guarda)** Defeito: "Encerrar e sair" chamando `revokeSession(atual.id)` em vez do `logout()`. Reprova "Encerrar e sair".
  **(guarda)** Defeito: o texto de `encerradas` sempre no plural. Reprova "Uma e nenhuma".
  **Feito:** 10 casos, com o `logout` trocado por `vi.mock` do módulo `auth/session`.
  **Vistos reprovar** (`246-guardas/`, todos voltando para `AccountSessionsCard.tsx` com o sha igual: `8e1a4578…`):
  - g8.2-selo-na-primeira → `a tabela, com a sessão atual marcada pelo campo, e não pela posição`;
  - g8.2-sem-confirmar → `encerrar uma pede confirmação…`, e também `"Cancelar" na confirmação não encerra` e `sessão já encerrada (404)…`;
  - g8.2-sair-pela-rota → `"Encerrar e sair" vai ao logout do emissor…`;
  - g8.2-plural-fixo → os dois casos de `com %i encerrada(s)`.

## 9. Bloqueio e falhas passageiras (apps/frontend, D8)

- [x] 9.1 (apps/frontend) O aviso de bloqueio e o das falhas passageiras, num componente do cartão compartilhado pelos três cartões de ação:
  - o texto do bloqueio com o N do `Retry-After` (`lockMinutes`);
  - os textos do `/entrar` para `429`, `503` e rede;
  - o `title` do `500`;
  - o botão desabilitado até o `Retry-After`;
  - o `409` e o `403` das 2 etapas relendo o `GET`.
  **Feito (07/11/2026), dentro das tarefas 5 a 8:** `cardFailure.ts` (`lockedText` com o N do `Retry-After` e o singular, `failureText`, `failureNotice` com a cor, e `blockedUntil` para o `423`, o `429` e o `503`) e `useBlockedUntil.ts`, usados pelos três cartões de ação. O `409` e o `403` das 2 etapas releem o `GET`. **Na implementação:** em vez de um componente comum, cada cartão põe o `Alert` com o `failureNotice`. O texto e o tempo vêm dos mesmos dois helpers.
- [x] 9.2 (apps/frontend) Os cenários do requisito "Bloqueio e falhas passageiras nos cartões", nos testes dos cartões.
  **(guarda)** Defeito: o botão não desabilitado no `503`. Reprova "Servidor indisponível no cadastro".
  **(guarda)** Defeito: o `403` do `setup` mostrado como erro, sem reler o `GET`. Reprova "Cadastro fechado no meio do caminho".
  **Feito:** os quatro cenários do requisito estão nos testes dos cartões: o bloqueio com o tempo do servidor no `AccountPasswordCard.test.tsx`; o indisponível no cadastro, o cadastro fechado no meio do caminho e a chave trocada ao gerar códigos no `TwoFactorCard.test.tsx`; mais a senha bloqueada no cadastro (1 minuto, no singular) e a falha de rede nas sessões.
  **Vistos reprovar** (`246-guardas/`, voltando para `TwoFactorCard.tsx` com o sha igual: `e6130d35…`):
  - g9.2-503-sem-desabilitar → `servidor indisponível no cadastro: o aviso, e "Continuar" desabilitado pelo Retry-After`;
  - g9.2-403-sem-reler → `cadastro fechado no meio do caminho: relê e mostra o indisponível`.

## 10. Deploy do cadastro (Q8)

- [x] 10.1 (deploy) `docker-compose.prod.yml`, no serviço `api`: `Identity__TwoFactor__EnrollmentEnabled: ${IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED:-false}`, com o comentário que cita a #246 e a #220. `.env.prod.example`: a linha comentada, com a explicação. `docker compose -f docker-compose.prod.yml --env-file .env.prod.example config` resolve a variável como `false`, e como `true` quando definida.
  **Feito (07/11/2026):** a linha no serviço `api` do `docker-compose.prod.yml`, com o comentário, e o bloco comentado no `.env.prod.example`. **Conferido com `podman compose … --env-file .env.prod.example config`** (o `docker compose` desta máquina é o do Podman): sem a variável, `Identity__TwoFactor__EnrollmentEnabled: 'false'`; com `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED=true` no ambiente, `'true'`.
- [x] 10.2 (docs) `docs/deployment.md`: a seção "Minha conta e o cadastro das 2 etapas (#246)", com:
  - o deploy só do `frontend` e o `api` relendo o compose;
  - como abrir o cadastro, como conferir e como voltar atrás;
  - a #220.

  Também corrigir, no `docs/configuration.md` (a linha do `Identity__TwoFactor__EnrollmentEnabled`) e nas seções da #218 e da #245 do `deployment.md`, o texto "até a #246", que esta change torna falso.
  **Feito:**
  - **`docs/deployment.md`:**
    - a seção nova "Minha conta e o cadastro das 2 etapas (#246)": o que muda, a ordem do deploy, a troca de senha que sai do painel, abrir, conferir (o `printenv` no contêiner e o cadastro no painel), voltar atrás, rollback e a #220;
    - a linha na tabela de variáveis;
    - as seções da #218 e da #245, que diziam "até a #246" e "Não abra o cadastro antes da #246", passam a apontar a seção nova.
  - **`docs/configuration.md`:** a linha do `Identity__TwoFactor__EnrollmentEnabled` (agora "no compose de produção vem de `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED`") e a linha nova na tabela do `.env.prod`.
  - **`docs/development.md`** (premissa que só toca doc): dizia "fechado até a tela de cadastro em Minha conta (#246)" e "Até a #246, o cadastro só se faz pela API". Agora descreve Minha conta.

## 11. Suítes e conferências

- [x] 11.1 (apps/frontend) `npm run lint` e `npm run build`, sem erro. A diferença do bundle contra a baseline (1.097,9 kB em `8884d89`) é comparada aos ~16,6 kB medidos do `qrcode.react` mais o código novo.
  **Feito (07/11/2026):** `npm run lint` sem erro nem aviso. A primeira rodada acusou 6 `react-refresh/only-export-components`: as chaves de consulta e os textos foram para `queryKeys.ts` e `accountTexts.ts`. `npm run build` (com o `tsc -b`) passa; ele acusou a tipagem de dois `it.each` do `accountApi.test.ts`, que foi corrigida. O aviso de chunk acima de 500 kB já existia.
  **Bundle**, medido nos dois worktrees:

  | | `8884d89` | change | diferença |
  |---|---|---|---|
  | `index-*.js` minificado | 1.097,94 kB | 1.134,93 kB | **+36,99 kB** |
  | gzip (Vite) | 318,75 kB | 330,47 kB | **+11,72 kB** |

  São os ~16,6 kB do `qrcode.react` (medidos sozinhos) mais ~20 kB do código novo.
- [x] 11.2 (apps/frontend) Suíte `frontend` completa, pelo `suite-246.sh`, com o relatório por nome comparado à baseline:
  - nenhum caso da baseline sumiu, mudou de nome ou deixou de passar;
  - os novos estão listados por arquivo;
  - o veredito está pela regra assimétrica.
  **Feito (07/11/2026):** `~/.cache/buteco-agents/246-frontend-20261107-0034/`, **APROVADA**: 2162 de 2162 em 167 arquivos, em 130 s. Começou com load 4,73 e CPU em 100; a CPU caiu até 60, e verde conta mesmo assim.
  **Por nome** (`nomes.txt` contra o `nomes.txt` da baseline): os 2079 da baseline continuam aprovados, e **nenhum sumiu ou mudou de nome**. Entram 83 (`novos.txt`):
  - `TwoFactorCard` 28, `accountApi` 13, `AccountSessionsCard` 10, `AccountPasswordCard` 9;
  - `EntrarPage` 6, `AccountPage` 5;
  - `AppShell` 3, `accountDates` 3, `passwordChangedNotice` 3;
  - `sessionFetch` 2, `router` 1.
- [x] 11.3 (apps/api, apps/inbox, apps/connectors) As rodadas da tarefa 1.4 registradas aqui, por nome, nos dois worktrees.
  **Feito:** as rodadas da 1.4 (`~/.cache/buteco-agents/246-backend-20261107-0008/`), todas `APROVADA` nos dois worktrees. Por nome, só entram os dois casos de `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis` em cada app: api 14 → 16, inbox 24 → 26, connectors 30 → 32.
- [x] 11.4 (apps/frontend + apps/api) **Conferência no navegador**, com o `apps/api` local (TZ `America/Sao_Paulo`, cadastro aberto em `Development`) e o Vite:
  - o rodapé leva a Minha conta, com a página atual marcada;
  - os Dados;
  - a senha atual errada fica no campo, sem deslogar, e conta uma falha só (a contagem no banco);
  - a troca de senha termina no `/entrar` com "Senha alterada." e entra com a nova;
  - o cadastro das 2 etapas de ponta a ponta, com um app autenticador real lendo o QR, "Copiar", "Copiar todos" e o `.txt` baixado;
  - o login seguinte pede o código;
  - "Gerar novos códigos" e "Remover verificação";
  - as sessões com dois navegadores: encerrar uma, encerrar as outras e "Encerrar e sair";
  - nos dois esquemas de cor.
  **Em andamento, PARADA (07/11/2026, 00:38–01:05):** no Chrome real, com o Vite e o `apps/api` do worktree, e o Postgres do compose de desenvolvimento. Ferramentas em `~/.cache/buteco-agents/246-conferencia/` (`val246.sh`). Usuário de teste: `conta.246@buteco.local`.
  - **Rodapé:** o link tem `title` "Minha conta" e `aria-current="page"` em `/account`. O login com `voltarPara=/account` voltou a Minha conta.
  - **Dados:** o nome, o e-mail, "07/11/2026" e "hoje, 00:38".
  - **Senha atual errada:** "Senha atual inválida." no campo, com `aria-invalid="true"`. A página continuou em `/account`, e a contagem do bloqueio no banco (`identity_login_throttle`) foi de nada para **1**: uma falha só.
  - **Senha errada no cadastro:** "Senha inválida." no campo, sem deslogar.
  - **QR:** decodificado no próprio Chrome pelo `BarcodeDetector` nativo, que é um leitor de QR independente do painel: `otpauth://totp/…`, rótulo **"Buteco Agentes:conta.246@buteco.local"**, `issuer` **"Buteco Agentes"**, SHA1, 6 dígitos, 30 s; o `secret` é igual ao segredo da tela. **Não houve app autenticador de celular:** a leitura por app real fica para a validação do dono (11.6). O segredo não estava no `sessionStorage` nem no `localStorage`.
  - **Código errado no QR:** "Código inválido. Confira se o relógio do celular está certo.", com `aria-invalid`. O código certo (TOTP do segredo) deu "Verificação ligada." e os 10 códigos.
  - **`.txt`:** o clique, interceptado na página para não gravar nos Downloads, gerou `buteco-agentes-codigos-de-recuperacao.txt` por um `blob:http://localhost:5173/…`, com o cabeçalho, a conta, a data e os 10 códigos, e a URL foi liberada. O download de verdade fica para a validação do dono.
  - **"Concluir":** "Ativa", "desde 07/11/2026", "10 códigos de recuperação restantes.".
  - **Login com o código:** "Sair", e de novo e-mail e senha → o passo do código → o TOTP → `/account`.
  - **Gerar novos códigos:** um código errado deu "Código inválido." no campo, sem deslogar; o TOTP deu "Novos códigos gerados.", com 10 códigos.
  - **Remover:** com um código de recuperação novo digitado **em minúsculas**, o cartão voltou a "Não configurada".
  - **Sessões, com duas sessões a mais** abertas pelo mesmo fluxo PKCE do painel (`val246.sh sessao`), como outros navegadores:
    - a atual é a "Sessão 3", marcada pelo campo;
    - "Encerrar a Sessão 1" pediu confirmação; depois dela, a lista ficou com 2, e a renovação daquela sessão passou a dar `400` (a outra, `200`);
    - "Encerrar as outras" deu "1 sessão encerrada.", e a renovação da outra passou a dar `400`;
    - "Encerrar e sair" levou ao `/entrar` e apagou o registro.
  - **Achado e corrigido (o texto do meu código):** com uma outra sessão só, a confirmação dizia "Encerrar as outras 1 sessões?". Agora ela diz "Encerrar a outra sessão? Quem estiver usando esse navegador precisa entrar de novo." (`confirmOthersText`), com o cenário novo na spec, o caso novo no `AccountSessionsCard.test.tsx` (11 de 11) e o guarda visto reprovar (`g8.2-confirmacao-plural`, `com uma outra sessão só, a confirmação vai no singular`; voltou: `accountTexts.ts`, sha igual: `d1a62b58…`). É um estado sem quadro, e entra na lista da validação visual.
  - **Troca de senha:**
    - a sessão de outro navegador passou a dar `400` na renovação;
    - o painel caiu no `/entrar`, e a senha nova entrou;
    - **mas o aviso "Senha alterada." NÃO apareceu.** Isso é a **PARADA** desta tarefa.

  **PARADA: premissa falsa que muda comportamento especificado (critério de parada).** O D3 dizia que a chave gravada antes do logout chega ao `/entrar` carregado pelo logout. Instrumentado no navegador (o `removeItem` da chave registrando a rota): a chave foi apagada em `/entrar?voltarPara=%2Faccount`, duas vezes (StrictMode), **dentro do documento antigo, antes** de o navegador carregar o `/entrar` do logout. O `logout()` apaga o registro, o `ProtectedRoute` vê a sessão nula e leva o SPA a `/entrar?voltarPara=…`, e esse `/entrar` consome a chave. Só depois o `/connect/logout` carrega o `/entrar` de verdade, já sem ela. Os testes do `EntrarPage` passam, porque no jsdom não há o logout de verdade. A correção muda quando a chave sai, e isso está escrito no requisito "Aviso de senha alterada no /entrar". Fica para o dono decidir.

  **Decisão do dono (Q11, opção 3) e correção (07/11/2026):**
  - `src/auth/logout.ts`: `createLeavingFlag()` e o `createLogout(runtime, store, leaving)`, que marca a aba como saindo **antes** de apagar o registro, dentro do lock;
  - `src/auth/session.ts`: a instância `leaving` e o `useLeaving()`;
  - `src/app/ProtectedRoute.tsx`: com a sessão nula e a aba saindo, mostra só "Saindo…" (`role="status"`), sem o `Navigate`.

  As outras abas não têm a marcação e seguem pelo evento `storage`. O `/entrar` continua lendo a chave uma vez e apagando.
  - **Testes:**
    - `features/account/passwordChangeLogout.test.tsx` (2 casos), com o logout real e o `authRuntime.assign` só registrando a navegação: a página não é descarregada. A troca de senha mostra "Saindo…", fica em `/account`, não renderiza o `/entrar` interno e mantém a chave; o `/entrar` montado depois (o documento novo) mostra o aviso e apaga a chave. O "Sair" comum também fica em "Saindo…";
    - `ProtectedRoute.test.tsx`: o caso "com o logout desta aba em andamento, mostra Saindo… e não vai ao /entrar". O harness ganhou o `MantineProvider`, porque a tela "Saindo…" usa Mantine; o caso reprovava na árvore limpa por isso, e não pelo defeito.
  - **Vistos reprovar** (`246-guardas/`):
    - g-saindo-sem-marcacao (o `leaving.mark()` tirado do logout) → os dois casos do `passwordChangeLogout.test.tsx`. Voltou: `logout.ts` (sha igual: `ff2b457d…`);
    - g-saindo-rota-ignora (o `if (saindo)` desligado no `ProtectedRoute`) → os dois e o caso do `ProtectedRoute`. Voltou: `ProtectedRoute.tsx` (sha igual: `d5ad155c…`).
  - **Documentos:** o D3 do design registra por que a premissa estava errada; o requisito "Encerrar sessão revoga pelo emissor e avisa as outras abas" da `panel-issuer-session` (MODIFIED, com o cenário "A aba que sai não passa pelo /entrar interno"); o requisito do aviso na `operator-login-ui` (com o cenário "A chave sobrevive ao logout até o /entrar de verdade"); Q11 nas "Decisões do dono". O `openspec validate --strict` continua válido.
  **Retomada depois da correção (07/11/2026, 01:08–01:12), no Chrome, com o stack de novo:**
  - **Suíte inteira de novo:** `~/.cache/buteco-agents/246-frontend-20261107-0104-r2/`, **APROVADA**, 2166 de 2166 em 168 arquivos (load 4,24 no início, CPU até 53). Por nome, os 2079 da baseline continuam aprovados, nenhum sumiu, e entram 87 (os 83 de antes, mais 1 do singular das sessões, 2 do `passwordChangeLogout` e 1 do `ProtectedRoute`).
  - **Troca de senha:** com a página antiga registrando cada mudança de rota em `localStorage` (a cada 5 ms), a troca levou ao `/entrar` carregado pelo logout (`performance` → `http://localhost:5173/entrar`) com **"Senha alterada. Entre com a nova senha."**. O documento antigo só registrou `/account`, sem o `/entrar` interno. A chave saiu depois da leitura.
  - **Recarregar o `/entrar`:** o aviso não volta.
  - **A senha nova entra.**
  - **"Sair" comum:** o documento antigo só registrou `/inventory`, sem o `/entrar?voltarPara` por um instante, e o `/entrar` veio do logout.
  - **O simulador de respostas** para a validação visual (`~/.cache/buteco-agents/246-conferencia/falhas.js`) foi conferido em dois estados: zero códigos restantes, e o erro do `GET` das sessões, que precisa de 4 respostas porque o painel repete a leitura 3 vezes.
  - **Não feito aqui, fica para o dono (11.6):** ler o QR com um app autenticador de celular, e baixar o `.txt` de verdade.
- [x] 11.5 (apps/frontend) **O QR contra a CSP:** o `vite build` servido pelo `vite preview` com o cabeçalho `Content-Security-Policy` do `nginx.conf`, por um arquivo de configuração temporário fora do repositório. O QR aparece, o download do `.txt` funciona, e o console não tem aviso de CSP.
  **Feito (07/11/2026):** o `vite build` com `VITE_API_BASE_URL=""` (a API na mesma origem, como em produção), servido pelo `vite preview` na porta 5173 com a CSP lida do `nginx.conf` e um proxy no molde do nginx (navegação → SPA; fetch → `apps/api`). A configuração fica fora do repositório: `~/.cache/buteco-agents/246-conferencia/vite.preview-csp.ts`. O `curl` confirmou o cabeçalho `Content-Security-Policy` igual ao do `nginx.conf`.
  - **A CSP está em vigor na página:** um `<script>` inline foi bloqueado (`script-src-elem`), e um `fetch` a `http://localhost:5017` também (`connect-src`). Os dois aparecem como `securitypolicyviolation`.
  - **Com ela ativa:** o login, Minha conta e o passo do QR. O QR aparece (164 px) e é decodificável pelo `BarcodeDetector`, e "Copiar" funciona. Nenhuma violação durante o QR e o "Copiar", e nenhum aviso de CSP no console.
  - **Não conferido com a CSP:** o download real do `.txt` (a navegação ao `blob:` pelo `<a download>`), porque baixaria um arquivo. A geração do blob foi conferida sem a CSP (11.4), e o download de verdade fica para a validação do dono.
- [x] 11.6 **Validação visual quadro a quadro com o dono** (convenção 14), contra o `MinhaConta` (`entrega=m1`), em todos os valores de `verificacao`, `senha` e `sessoes`, e contra o `Main` em `senha-alterada`. Também os estados sem quadro (Q2):
  - o carregamento e o erro do `GET` em cada cartão;
  - `429`, `503` e rede em cada ação;
  - o `500` em regenerar e remover;
  - zero códigos restantes;
  - "1 sessão encerrada." e "Nenhuma outra sessão estava aberta.".

  E o link do rodapé, nos dois esquemas de cor. A conferência é iterativa, e o archive só vem depois do ok do dono.
  **Feito (07/11/2026):** o dono validou quadro a quadro, **sem divergência**, com o manual e as ferramentas de `~/.cache/buteco-agents/246-conferencia/` (`MANUAL.md`, `val246.sh`, `falhas.js`).
  - **App autenticador:** o nome **não foi informado**; a mensagem do dono trouxe o campo como o marcador "[NOME DO APP]". O dono confirmou que o rótulo aparece como "Buteco Agentes" com o e-mail, e que o código entrou.
  - **O `.txt` dos códigos:** baixado e conferido pelo dono.
- [x] 11.7 Medição da convenção 18 **depois** da validação visual: produção, testes e duplos, separados, por `git diff --numstat` contra `8884d89`, só código, comparados à projeção do design.
  **Feito (07/11/2026):** `git diff --numstat` contra `8884d89`, com os arquivos novos contados (`git add -N`), fora `docs/`, `openspec/`, `.md`, `.json`, `.yml` e o lock. Duplos de teste são o `features/account/test/`.
  - **produção:** +1.516 / −17, em 24 arquivos, contra ~1.260 projetados (**+20%**);
  - **testes (casos):** +1.356 / −2, em 16 arquivos, contra ~1.670 (**−19%**);
  - **duplos:** +138, em 2 arquivos, contra ~140 (**−1%**).

  | componente | produção (proj. → real) | testes (proj. → real) |
  |---|---|---|
  | `accountApi` | ~170 → 160 | ~180 → 97 |
  | página, Dados, datas (+ `AccountCard`, `queryKeys`) | ~150 → 170 | ~150 → 118 |
  | Senha e o aviso no `/entrar` | ~210 → 207 | ~300 → 277 |
  | 2 etapas (cartão, QR, códigos, `.txt`, textos) | ~480 → 618 | ~620 → 387 |
  | Sessões | ~200 → 189 | ~260 → 173 |
  | falhas dos cartões (`cardFailure`, `useBlockedUntil`) | — → 62 | — |
  | rodapé e rota | ~35 → 26 | ~60 → 78 |
  | `sessionFetch` (D10) | ~15 → 16 | ~40 → 42 |
  | os três testes do `401` sem corpo (backend) | — | ~60 → 55 |
  | a aba que sai (Q11: `logout`, `session`, `ProtectedRoute`) | não projetado → 68 | não projetado → 129 |

  **Leitura:**
  - **A produção passou** em três pontos: no cartão das 2 etapas, que tem os passos, a máquina de estados e as falhas no mesmo arquivo (+138); nos helpers de falha, que a projeção contava dentro dos cartões (+62); e na correção da Q11, que não estava projetada (+68).
  - **Os testes ficaram abaixo** porque os casos do cartão das 2 etapas e das sessões reaproveitam o mesmo `setup` e o `fakeAccountApi`, e são mais curtos que a projeção supôs.
  - **Os duplos acertaram:** 138 contra ~140.

## 12. Documentação e registros

- [x] 12.1 (docs) `02-HISTORICO_E_STATUS.md` (a entrada da change) e `CHANGELOG.md`, antes do archive.
  **Feito (07/11/2026):**
  - `CHANGELOG.md`: "Minha conta no painel" em Added; "Senha ou código errado não desloga mais" e "O 'Sair' não mostra mais o /entrar por um instante" em Fixed;
  - `02-HISTORICO_E_STATUS.md`: a entrada `minha-conta-painel`.
- [x] 12.2 (issues) Comentários de entrega, com o ok do dono:
  - na **#183**: o que ela encontra pronto (o `sessionFetch` com o `401` de recusa, o padrão dos avisos dos cartões, o "use Usuários" que ela transforma em link);
  - na **#220**: a variável do compose que ela remove junto com a opção.

  O PR leva `Closes #246`.
  **Feito (07/11/2026):** [comentário de entrega na #183](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6702276299) e [comentário na #220](https://github.com/aldovrando-oliveira/buteco-agentes/issues/220#issuecomment-6702276561).
