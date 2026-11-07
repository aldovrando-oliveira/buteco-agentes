Cada guarda marcado **(guarda)** só conta depois de visto reprovar contra o defeito que ele existe para pegar (convenção 15):

1. o `~/.cache/buteco-agents/bin/guarda-245.sh` reintroduz o defeito nomeado. É uma cópia do `guarda-211.sh`, com o projeto `frontend` acrescentado, que roda `npx vitest run <arquivo> -t "<nome>"` em `apps/frontend`. A mutação toca só o trecho nomeado, e nunca um diretório restaurado ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

Fica registrado na tarefa:

- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes.

As suítes rodam pelo `~/.cache/buteco-agents/bin/suite-245.sh`. A principal é a `frontend`; a `api` roda só com o filtro do teste renomeado (tarefa 1.2). Vale a regra assimétrica: verde conta mesmo com a CPU caindo, e reprovação só conta com a CPU em 100 do início ao fim. Antes de cada rodada da `frontend`, o load fica abaixo de 6.

**A baseline** é `~/.cache/buteco-agents/baseline-245-20261105-0104/`, em `1131635`:

- 1978 aprovados em 154 arquivos, 0 reprovados e 0 pulados, em 151 s;
- o relatório por nome está em `nomes.txt` (1978 linhas);
- a rodada começou com load 4,2 e CPU em 100, e a CPU caiu até 44 (`APROVADA`).

**Parada:** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura. A correção que só acrescenta teste ou cenário para pôr um guarda no lugar certo, ou que só toca comentário ou documentação, segue e fica registrada. Nenhum caso existente é removido, e nenhuma asserção é afrouxada. **Um só caso muda de nome**, por decisão do dono (Q1): o da tarefa 1.2. Nenhum outro muda. Ele entra na lista de renomeados do fechamento.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-245.sh`, com o projeto `frontend` e o `api`, conferido com uma mutação que não altera nada (a mutação recusa com "defeito não aplicado") e com uma que altera (o arquivo volta com o mesmo sha).
  **Feito (05/11/2026):** a mutação nula recusa ("defeito não aplicado"), e uma mutação real no `Logo.tsx` roda o `Logo.test.tsx` e volta com o sha igual (`f50ef21e…`).

## 1. O texto do "expirou", preso nos dois lados

- [x] 1.1 (apps/frontend) `features/auth/messages.ts`: a constante `ENTRADA_EXPIROU` = "A entrada expirou. Informe e-mail e senha de novo.", com o comentário que cita a origem (`apps/api/src/Buteco.Api/Users/Login/SessionEndpoints.cs`, `ExpiredMessage`), o teste do apps/api que a prende (1.2) e a #247.
- [x] 1.2 (apps/api) `TwoFactorLoginTests.ExpiredPendingSession_TheEntryExpired` passa a se chamar `SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes`. O comentário diz que o painel reconhece o texto por igualdade (`apps/frontend/src/features/auth/messages.ts`, `ENTRADA_EXPIROU`) e aponta a #247. A asserção, por literal, não muda. A suíte `api` roda com o filtro `FullyQualifiedName~TwoFactorLoginTests` nos dois worktrees, e o relatório por nome registra a troca do nome, com o mesmo número de casos.
  **(guarda)** Defeito: o `ExpiredMessage` com "Informe" trocado por "Digite" em `SessionEndpoints.cs`. Reprova o caso renomeado (e o `WithoutAPendingSession_…`, que também usa o literal).
  **Feito (05/11/2026):** a suíte `api` com o filtro `FullyQualifiedName~TwoFactorLoginTests` (`/Users/aldovrando/.cache/buteco-agents/245-api-20261105-0155/`) deu `APROVADA` nos dois worktrees, com 10 de 10. Na baseline a CPU caiu até 86; na change ficou em 100. A única diferença por nome é `ExpiredPendingSession_TheEntryExpired` → `SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes` (`nomes.txt` de cada lado).
  **Visto reprovar** (`~/.cache/buteco-agents/245-guardas/g1.2`): `ExpiredMessage` com "Informe" trocado por "Digite" → `SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes` e `WithoutAPendingSession_TheEntryExpired_AndNothingIsCounted`, `Assert.Equal() Failure: Strings differ` (2 de 10 reprovados). Voltou: `SessionEndpoints.cs` (sha igual: `58d14377…`).

## 2. `PasswordField` e as guardas (apps/frontend)

- [x] 2.1 (apps/frontend) `components/forms/PasswordField.tsx` (design D6): repassa as props ao `PasswordInput` e põe `aria-invalid={error ? true : undefined}` no `<input>`, com o comentário que cita o `withAria: false` do Mantine (9.4.2 l. 159; 9.7.5 l. 169) e a #170.
- [x] 2.2 (apps/frontend) `PasswordField.test.tsx`: com erro, `aria-invalid="true"` e o `aria-describedby` apontando para a mensagem; sem erro, sem o atributo.
  **(guarda)** Defeito: o `aria-invalid` tirado do `PasswordField`. Reprova o caso com erro.
  **Visto reprovar** (`~/.cache/buteco-agents/245-guardas/g2.2`, 05/11/2026): `com erro, o input é anunciado como inválido e aponta para a mensagem`, `Expected the element to have attribute`. Voltou: `PasswordField.tsx` (sha igual ao de antes: `f72dd147…`).
- [x] 2.3 (apps/frontend) Trocar o `PasswordInput` pelo `PasswordField` no `EntrarForm`, no `WahaCredentialFields`, no `TelegramCredentialFields` e no `McpServerForm`. O `McpServerForm` perde o `aria-invalid` local e o comentário dele (l. 439–445).
- [x] 2.4 (apps/frontend) Os testes da #170, um por campo:
  - `WahaCredentialFields.test.tsx` e `TelegramCredentialFields.test.tsx`: o envio com o token vazio deixa o `<input>` com `aria-invalid="true"`;
  - `McpServerForm.test.tsx`: o mesmo para a credencial obrigatória. Hoje nenhum teste afirma o atributo, que só tem o comentário da #168;
  - `EntrarPage.test.tsx`: o campo "Senha" é o `PasswordField`.

  **(guarda)** Defeito: o `WahaCredentialFields` volta ao `PasswordInput` do `@mantine/core`. Reprova o caso do WAHA.
  **(guarda)** Defeito: o mesmo no `TelegramCredentialFields`. Reprova o caso do Telegram.
  **(guarda)** Defeito: o mesmo no `McpServerForm`, sem o ajuste local. Reprova o caso do MCP.
  **Correção do registro (convenção 9, premissa falsa que só acrescenta guarda):** o `McpServerForm` já tinha o `aria-invalid` afirmado, no helper `expectErrorOnField` do `McpServerEditPage.test.tsx` (l. 41–52). O registro dizia que nenhum teste o afirmava. Esse teste existente passou a ser o guarda do MCP, sem duplicá-lo, e só o comentário dele mudou: citava o ajuste local, agora cita o `PasswordField`. Nenhuma asserção mudou. O caso do `EntrarForm` fica coberto pela guarda estática da 2.5: depois da troca, o único campo de senha que existe é o `PasswordField`, e um `PasswordInput` direto reprova a suíte.
  **Vistos reprovar** (`~/.cache/buteco-agents/245-guardas/`, 05/11/2026), cada defeito trocando o import do `PasswordField` pelo `PasswordInput` do Mantine:
  - g2.4-waha → `WahaCredentialFields › com o token obrigatório vazio, o campo é anunciado como inválido`, `Expected the element to have attribute`. Voltou: `WahaCredentialFields.tsx` (sha igual ao de antes: `3c712a4f…`);
  - g2.4-telegram → `TelegramCredentialFields › com o token obrigatório vazio, o campo é anunciado como inválido`, a mesma mensagem. Voltou: `TelegramCredentialFields.tsx` (sha igual: `1748110f…`);
  - g2.4-mcp → `McpServerEditPage › token em header customizado › 400 da API em credential aparece no campo "Token" e não navega`, a mesma mensagem. Voltou: `McpServerForm.tsx` (sha igual: `bdce309f…`).
- [x] 2.5 (apps/frontend) `components/forms/passwordFieldGuard.test.ts`: lê os `.ts` e `.tsx` de `src/` e reprova, nomeando o arquivo, quando algum fora do `PasswordField.tsx` importa `PasswordInput` de `@mantine/core`, em qualquer forma de import nomeado, inclusive com outros nomes na mesma linha e em várias linhas.
  **(guarda)** Defeito: um `import { PasswordInput, Stack, TextInput } from '@mantine/core'` de volta no `WahaCredentialFields`. Reprova nomeando o arquivo.
  **(guarda)** Defeito: o mesmo em várias linhas, no `TelegramCredentialFields`. Reprova nomeando o arquivo.
  **Na implementação:** o próprio `passwordFieldGuard.test.ts` traz exemplos de import no caso que confere o reconhecimento, e na primeira rodada a guarda o acusou. Ele ficou fora da varredura, ao lado do `PasswordField.tsx`.
  **Vistos reprovar** (`~/.cache/buteco-agents/245-guardas/`, 05/11/2026):
  - g2.5-uma-linha (`import { PasswordInput, Stack, TextInput }` no `WahaCredentialFields`) → `nenhum arquivo de src/ fora do PasswordField importa PasswordInput de @mantine/core`, `expected [ Array(1) ] to deeply equal []`. Voltou: `WahaCredentialFields.tsx` (sha igual: `3c712a4f…`);
  - g2.5-varias-linhas (o import em várias linhas no `TelegramCredentialFields`) → o mesmo caso, a mesma mensagem. Voltou: `TelegramCredentialFields.tsx` (sha igual: `1748110f…`).
- [x] 2.6 (apps/frontend) `eslint.config.js`: `no-restricted-imports` para `PasswordInput` de `@mantine/core`, com a mensagem que aponta o `PasswordField`, e o `PasswordField.tsx` fora da regra. `npm run lint` passa na árvore da change.
  **(guarda)** Defeito: o import direto de volta no `WahaCredentialFields`. O `npx eslint src/features/channels/components/WahaCredentialFields.tsx` reporta `no-restricted-imports`. Esta guarda roda pelo ESLint, e não pela suíte, e o registro diz isso.
  **Feito:** o `npm run lint` passa sem erro nem aviso. O `PasswordField.tsx` fica fora da regra pelo `eslint-disable-next-line` na linha do import, que o lint confere: sem a regra, ele acusaria a diretiva sem uso.
  **Visto reprovar pelo ESLint, e não pela suíte** (`~/.cache/buteco-agents/245-guardas/g2.6`, 05/11/2026): `import { PasswordInput as PasswordField, Stack, TextInput } from '@mantine/core'` no `WahaCredentialFields` → `'PasswordInput' import from '@mantine/core' is restricted` (`no-restricted-imports`). Voltou: `WahaCredentialFields.tsx` (sha igual: `3c712a4f…`).
  **Primeira tentativa descartada:** um import não usado reprovou por `no-unused-vars`, e não pela regra; o defeito foi refeito com o import em uso.

## 3. O passo das 2 etapas no `/entrar` (apps/frontend)

- [x] 3.1 (apps/frontend) `features/auth/api/sessionApi.ts`: o `createSession` devolve `{ kind: 'segundo-passo' }` no `200` com `segundoPasso: "totp"`, e o `submitSecondFactor(codigo)` chama o `POST /auth/session/second-factor` no mesmo molde (resultado por status, `title`, `Retry-After`, rede).
- [x] 3.2 (apps/frontend) `sessionApi.test.ts`: o corpo e os cabeçalhos dos dois pedidos; cada status vira o resultado certo; o `200` sem `segundoPasso` não é tomado por segundo passo.
  **(guarda)** Defeito: o `createSession` volta a tratar só o `204` como sucesso, sem o ramo do `200`. Reprova o caso do `200 { segundoPasso }`.
  **Visto reprovar** (`~/.cache/buteco-agents/245-guardas/g3.2`, 05/11/2026): o ramo do `200 { segundoPasso }` retirado do `createSession`, que volta a dar sucesso só no `204` → `createSession › 200 com segundoPasso "totp" abre o passo do código`, `expected { kind: 'http', status: 200 } to deeply equal { kind: 'segundo-passo' }`. Voltou: `sessionApi.ts` (sha igual: `fba3daf4…`).
- [x] 3.3 (apps/frontend) `features/auth/continueEntry.ts`: o `continuarEntrada` extraído do `EntrarPage` (l. 71–80), sem mudar o comportamento. Os testes existentes do `EntrarPage` continuam verdes sem alteração.
  **Feito:** o `continueEntry` (`features/auth/continueEntry.ts`) é o bloco do `EntrarPage` (l. 71–80 de `1131635`) sem mudança de comportamento. Os casos de `204` do `EntrarPage.test.tsx` passam sem alteração (ver 3.5).
- [x] 3.4 (apps/frontend) `SecondFactorForm.tsx` e a etapa no `EntrarPage` (design D1 e D2):
  - o campo com os atributos do quadro;
  - a ajuda por `aria-describedby`;
  - "Informe o código." no campo vazio;
  - o `trim` só das pontas;
  - a tabela de respostas da `operator-login-ui`;
  - o `Retry-After` desabilitando "Verificar";
  - "Voltar";
  - o `state` do router `{ etapa: 'codigo' }` com o aviso "Senha salva…".
  **Na implementação:** o aviso por status saiu do `EntrarPage` para o `entryNotices.ts`, porque o `/entrar` vindo do link também o usa. O cartão comum é o `EntryCard`. O rótulo "Senha" virou um `<label>` próprio ao lado do botão "Esqueceu a senha?", para o botão não entrar no nome acessível do campo.
- [x] 3.5 (apps/frontend) `SecondFactorForm.test.tsx` e `EntrarPage.test.tsx`, um caso por cenário do requisito "Passo das 2 etapas no /entrar".
  **(guarda)** Defeito: a comparação com `ENTRADA_EXPIROU` trocada por "todo `401` é código inválido". Reprova "Entrada expirada volta a e-mail e senha".
  **(guarda)** Defeito: a comparação afrouxada para `title.startsWith('A entrada')`. Reprova "Outro texto de 401 não é tomado por entrada expirada".
  **(guarda)** Defeito: o `204` do código chamando `beginLogin(authRuntime, '/')` fixo, em vez do `continuarEntrada`. Reprovam "Código certo segue o fluxo do emissor" (o `voltarPara`) e "Código certo com retorno do authorize segue o retorno".
  **(guarda)** Defeito: o `trim` trocado por `replace(/\s/g, '')`, que tira também o espaço do meio. Reprova "Espaços nas pontas saem, o resto vai como está", cujo código tem um espaço no meio.
  **(guarda)** Defeito: o `inputmode` do campo de volta a `numeric`. Reprova o caso dos atributos.
  **Na implementação:** os casos ficaram no `EntrarPage.test.tsx` (36 casos), pelo `/entrar` inteiro, e não num `SecondFactorForm.test.tsx` à parte. O comportamento é da página: as etapas, o `continueEntry` e o `Retry-After`.
  **Segundo renomeado (consequência da spec alterada, para a lista do fechamento):** `EntrarPage › tem E-mail e Senha, sem "Esqueceu a senha?" e sem o convite` → `tem E-mail e Senha, com "Esqueceu a senha?" e o convite ao administrador`. O requisito da `operator-login-ui` que o caso afirmava foi invertido nesta change. As asserções do e-mail e da senha ficaram; as duas negativas viraram as positivas do requisito novo. Nenhuma cobertura saiu.
  **Vistos reprovar** (`~/.cache/buteco-agents/245-guardas/`, 05/11/2026):
  - g3.5-todo401 (o "expirou" nunca reconhecido) → `entrada expirada volta a e-mail e senha, com o aviso em role="status"`;
  - g3.5-startswith (`title?.startsWith("A entrada")`) → `outro texto de 401 não é tomado por entrada expirada`;
  - g3.5-beginlogin-fixo (o `204` do código com `beginLogin(authRuntime, '/')`) → `código certo segue o fluxo do emissor com o voltarPara da URL` e `código certo com retorno do authorize segue o retorno`;
  - g3.5-trim (`replace(/\\s/g, "")`) → `espaços nas pontas saem, e o resto vai como está`;
  - g3.5-inputmode (`inputMode="numeric"`) → `o campo aceita texto, com a ajuda do código de recuperação`.

  Os três primeiros voltaram em `EntrarPage.tsx` (sha igual: `abe67e91…`), os dois últimos em `SecondFactorForm.tsx` (sha igual: `e45a8cdf…`).
- [x] 3.6 (apps/frontend) `EntrarForm`: o botão "Esqueceu a senha?" ao lado do rótulo "Senha", que abre e fecha o texto; o rodapé do convite abaixo do cartão; o e-mail e o aviso vindos do `state` do router. Os casos dos cenários novos da `operator-login-ui`.
  **(guarda)** Defeito: o texto do "Esqueceu a senha?" sempre visível, sem o botão alternar. Reprova o caso do segundo acionamento.
  **Visto reprovar** (`~/.cache/buteco-agents/245-guardas/g3.6`, 05/11/2026): o texto sempre visível (`{(true || esqueceu) && (`) → `"Esqueceu a senha?" abre e fecha o texto na linha, sem enviar nada`. Voltou: `EntrarForm.tsx` (sha igual: `eb37f954…`). O e-mail e o aviso vindos do `state` estão nos casos `vindo das páginas do link`.

## 4. O token do link (apps/frontend)

- [x] 4.1 (apps/frontend) `features/auth/linkToken.ts` (design D4): `take(pagina)` lê o fragmento, grava a chave e troca o endereço por `history.replaceState`, mantendo o `state` do router. `clear(pagina)` apaga a chave. O hook `useLinkToken(pagina)` lê no inicializador do `useState`, grava no efeito e apaga na limpeza.
  **Na implementação:** a leitura do fragmento e a gravação ficam no inicializador do `useState` (`takeLinkToken`). O efeito grava a chave a partir do estado e a apaga na limpeza. A `replaceState` mantém o `window.history.state` do router.
- [x] 4.2 (apps/frontend) `linkToken.test.ts` e os casos das páginas, um por cenário do requisito "O token sai do endereço antes de qualquer pedido e fica só na aba", com o `StrictMode` explícito no render do caso dele.
  **(guarda)** Defeito: o `replaceState` movido para depois da consulta. Reprova "O endereço troca antes do primeiro pedido".
  **(guarda)** Defeito: a consulta com o token também na query (`/auth/invitation?token=…`). Reprova "O token nunca vai numa URL".
  **(guarda)** Defeito: o `clear` tirado do `204`. Reprova "O sucesso apaga o token".
  **(guarda)** Defeito: o `clear` tirado do `404`. Reprova "O link inválido apaga o token".
  **(guarda)** Defeito: a limpeza do efeito sem o `clear`. Reprova "Sair da página apaga o token".
  **(guarda)** Defeito: o token só em memória, sem gravar a chave. Reprovam "Recarregar reaproveita o token" e "O StrictMode não perde o token".
  **Na implementação:**
  - os casos das duas páginas estão num arquivo só, `AccessLinkPage.test.tsx`, com `describe.each` sobre `convite` e `redefinir-senha`;
  - o `linkToken.test.ts` cobre a função sozinha;
  - a "recarga" é uma segunda árvore renderizada com a primeira ainda montada, porque recarregar não roda a limpeza do React;
  - a fila de respostas do falso casa só o caminho, sem a query. A primeira versão casava o caminho exato, e o defeito do token na URL reprovava 46 casos por "pedido sem resposta", e não pela asserção; o guarda foi refeito com o falso corrigido.

  **Vistos reprovar** (`~/.cache/buteco-agents/245-guardas/`, 05/11/2026), cada um nas duas páginas:
  - g4-replace-depois (a `replaceState` adiada para 50 ms depois, com o primeiro pedido já saído) → `o endereço troca antes do primeiro pedido, e o token vai no corpo`. Voltou: `linkToken.ts` (sha igual ao de antes: `49d1a4cb…`);
  - g4-token-na-url (`?token=` na consulta) → `o token nunca vai numa URL`, e só ele. Voltou: `accessLinkApi.ts` (sha igual: `f0b8a516…`);
  - g4-sem-clear-204 → `o sucesso apaga o token`. Voltou: `AccessLinkPage.tsx` (sha igual: `ebfb8e8c…`);
  - g4-sem-clear-404 → `o link inválido apaga o token`. Voltou: idem;
  - g4-sem-clear-saida (a limpeza do efeito sem o `clearLinkToken`) → `sair da página apaga o token`. Voltou: `linkToken.ts` (sha igual);
  - g4-so-memoria (o `setItem` tirado) → `recarregar reaproveita o token guardado`, `o StrictMode não perde o token` e `sair da página apaga o token` (este pela pré-condição: a chave gravada antes de sair). Voltou: `linkToken.ts` (sha igual).

## 5. As páginas do link (apps/frontend)

- [x] 5.1 (apps/frontend) `features/auth/api/accessLinkApi.ts`: a consulta e a conclusão das duas páginas, com resultado por status, sem lançar erro e sem token de sessão. `accessLinkApi.test.ts`: o caminho, o método, o corpo e o `Content-Type` de cada uma; o `fetch` direto, e não o `sessionFetch`.
- [x] 5.2 (apps/frontend) Os componentes comuns (design D5):
  - `EntryCard`, o cartão com a marca e o rodapé;
  - `NewPasswordForm`, com o e-mail travado, a senha e a repetição, a política e as senhas diferentes, os campos com o `PasswordField`;
  - `InvalidLinkCard`, com o texto por página;
  - `UnavailableLinkCard`;
  - a data do rodapé no formato `dd/mm/aaaa às hh:mm`, por um formatador que recebe o fuso, para o teste não depender do fuso da máquina.
  **Na implementação:**
  - os componentes são o `EntryCard`, o `NewPasswordForm` e o `LinkStateCards.tsx` (`LoadingLinkCard`, `UnavailableLinkCard`, `InvalidLinkCard`);
  - a política foi para o `passwordPolicy.ts`, e o formatador para o `linkExpiry.ts`, porque o `react-refresh/only-export-components` não aceita função exportada ao lado de componente;
  - na redefinição, o aviso fixo usa `role="note"`, porque o `Alert` do Mantine nasce com `role="alert"`.
- [x] 5.3 (apps/frontend) `ConvitePage` e `RedefinirSenhaPage`: os estados `carregando`, `formulario`, `senhas-diferentes`, `senha-fora-da-regra`, `muitas-tentativas`, `indisponivel` e `link-invalido`; o estado `envio-indisponivel` no `503` e na rede da conclusão, sobre o formulário; o aceite ou a conclusão seguidos do login (design D3).
  **Na implementação:** as duas páginas são o `AccessLinkPage`, com os textos por página numa tabela. O `ConvitePage` e o `RedefinirSenhaPage` são invólucros dele, no mesmo arquivo.
- [x] 5.4 (apps/frontend) `ConvitePage.test.tsx`, `RedefinirSenhaPage.test.tsx` e `NewPasswordForm.test.tsx`, um caso por cenário dos requisitos "Consulta, carregando, indisponível e link inválido", "O formulário da senha nova" e "Depois de criar a senha, a pessoa entra", nas duas páginas onde o cenário vale para as duas.
  **(guarda)** Defeito: a validação de tamanho removida. Reprova "Senha curta não envia".
  **(guarda)** Defeito: a política com `senha.trim().length`. Reprova "Espaços contam como caracteres".
  **(guarda)** Defeito: a conferência da repetição removida. Reprova "Senhas diferentes não enviam".
  **(guarda)** Defeito: o link inválido mostrando o `title` do servidor no lugar do texto da página. Reprovam "Link inválido no convite" e "Link inválido na redefinição".
  **(guarda)** Defeito: o `200 { segundoPasso }` do login tratado como erro na página do link. Reprova "Redefinição de quem tem as 2 etapas chega ao passo do código".
  **(guarda)** Defeito: a senha incluída no `state` da navegação ao `/entrar`. Reprova o mesmo caso (o `state` não contém a senha).
  **(guarda)** Defeito: o `503` da conclusão levando ao estado `indisponivel`, que troca o formulário. Reprova "Painel indisponível na conclusão mantém o formulário".
  **Na implementação:**
  - os casos estão no `AccessLinkPage.test.tsx` (53 casos, com os da 4.2);
  - o caso "sem a senha no state" lia o `window.history.replaceState`, que o `MemoryRouter` não usa, e a asserção não podia reprovar. Foi corrigido antes do guarda para ler o `state` do router por uma sonda no `/entrar`.

  **Vistos reprovar** (`~/.cache/buteco-agents/245-guardas/`, 05/11/2026), cada um nas duas páginas:
  - g5-sem-tamanho → `senha curta não envia` e `senha longa demais não envia`. Voltou: `NewPasswordForm.tsx` (sha igual ao de antes: `74aa3ad5…`);
  - g5-trim (`senha.trim().length`) → `espaços contam como caracteres`. Voltou: `passwordPolicy.ts` (sha igual: `cf6c5f7a…`);
  - g5-sem-repeticao → `senhas diferentes não enviam`. Voltou: `NewPasswordForm.tsx` (sha igual). **Observação:** na primeira rodada reprovou também `/redefinir-senha › login recusado depois de criar a senha chega ao /entrar com o e-mail e o aviso` (o texto do bloqueio não apareceu no prazo). Em duas rodadas seguintes só os dois casos nomeados reprovaram, e o arquivo passou três vezes na árvore limpa. Fica observado na suíte completa (7.2);
  - g5-title-do-servidor → `link inválido mostra o texto da página, e não o title do servidor`. Voltou: `AccessLinkPage.tsx` (sha igual: `ebfb8e8c…`);
  - g5-200-como-erro → `com as 2 etapas, chega ao /entrar no passo do código, sem a senha no state`. Voltou: idem;
  - g5-senha-no-state (a senha posta no `state`) → o mesmo caso, pela sonda. Voltou: idem;
  - g5-503-indisponivel (o `503` da conclusão levando a `indisponivel`) → `painel indisponível na conclusão mantém o formulário` e `sem conexão na conclusão mantém o formulário`. Voltou: idem.

## 6. Rotas (apps/frontend)

- [x] 6.1 (apps/frontend) `app/routes.tsx`: `convite` e `redefinir-senha` ao lado de `entrar`, fora do `ProtectedRoute`, com o comentário que aponta o `AccessLinkUrls` do apps/api.
- [x] 6.2 (apps/frontend) `router.test.tsx`: as duas abrem sem sessão (os cenários do requisito "Páginas públicas do link de uso único").
  **(guarda)** Defeito: `convite` dentro do `ProtectedRoute`. Reprova "O convite abre sem sessão".
  **Visto reprovar** (`~/.cache/buteco-agents/245-guardas/g6.2`, 05/11/2026): `convite` dentro do `ProtectedRoute` → `appRoutes › /convite abre sem sessão`. Voltou: `routes.tsx` (sha igual ao de antes: `37ae1493…`).

## 7. Suíte e conferências

- [x] 7.1 (apps/frontend) `npm run lint` e `npm run build` em `apps/frontend`, sem erro.
  **Feito (05/11/2026):** o `npm run lint` passa sem erro nem aviso, e o `npm run build` também. O aviso de chunk acima de 500 kB já existia na baseline: o bundle tinha 1.085,6 kB em `1131635` e tem 1.097,9 kB aqui (+12 kB).
- [x] 7.2 (apps/frontend) Suíte `frontend` completa no worktree da change, pelo `suite-245.sh`, com o relatório por nome comparado à baseline:
  - nenhum caso da baseline sumiu ou mudou de nome;
  - os novos estão listados;
  - o veredito está pela regra assimétrica.
  **Rodada 1** (`~/.cache/buteco-agents/245-frontend-20261105-0225/`): **INCONCLUSIVA**, com 2076 de 2079 e a CPU em até 23 (load 36). As três reprovações:
  - `KnowledgeBaseCreatePage.sync` › `connectors-not-configured` e `folder-in-use`: o prazo de 15 s estourado. É o arquivo que reprovou do mesmo jeito na rodada inconclusiva da baseline;
  - `AccessLinkPage › /redefinir-senha › login recusado depois de criar a senha chega ao /entrar com o e-mail e o aviso`, em 992 ms: **defeito do teste novo**. Ele esperava o campo "E-mail", que a página do link também tem, e sob carga achava o campo dela antes da navegação. Agora espera o aviso, que só existe no `/entrar`. Era a reprovação a mais vista no g5-sem-repeticao. A correção só acrescenta espera, sem afrouxar asserção.

  **Rodada 2** (`~/.cache/buteco-agents/245-frontend-20261105-0230-r2/`): **APROVADA**, 2079 de 2079 em 160 arquivos, em 128 s. Começou com load 3,4 e CPU em 100; a CPU caiu até 62.
  **Por nome** (`nomes.txt` contra o da baseline):
  - todos os 1978 da baseline continuam aprovados, menos o renomeado da 3.5;
  - entram 102 (`novos.txt`): `AccessLinkPage` 53, `EntrarPage` 20 (com o renomeado), `sessionApi` 9, `accessLinkApi` 7, `linkToken` 5, `router` 2, `PasswordField` 2, `passwordFieldGuard` 2, WAHA 1 e Telegram 1.
- [x] 7.3 (apps/api) A suíte `api` com o filtro `FullyQualifiedName~TwoFactorLoginTests`, nos dois worktrees. Mesmo número de casos, e a única diferença por nome é a da tarefa 1.2.
  **Feito:** ver a 1.2 (`~/.cache/buteco-agents/245-api-20261105-0155/`): `APROVADA` nos dois worktrees, 10 de 10. A única diferença por nome é a renomeação.
- [x] 7.4 (apps/frontend + apps/api) **Conferência no navegador**, com o `apps/api` local (TZ `America/Sao_Paulo`) e o Vite:
  - o passo do código com uma conta que tem as 2 etapas (cadastro aberto em `Development`);
  - um código de recuperação no mesmo campo;
  - o "expirou", esperando os 5 min do cookie pendente;
  - um convite e uma redefinição gerados pelas rotas administrativas;
  - o endereço sem o fragmento depois de abrir o link, e o histórico da aba sem o token;
  - a recarga da página do link;
  - a chave do `sessionStorage` apagada ao sair, no sucesso e no link inválido;
  - a redefinição de quem tem as 2 etapas, chegando ao passo do código;
  - nos dois esquemas de cor.
  **Feito (05/11/2026, 05:40–05:53 UTC):** no Chrome real, com o Vite do worktree (`localhost:5173`) e o `apps/api` do worktree (`dotnet run`, `TZ=America/Sao_Paulo`) contra o Postgres do compose de desenvolvimento. O lado administrativo foi pela API (`conf245.sh`, no scratchpad da sessão). Dados criados no banco local: o usuário `marcos.lima@buteco.local`, com as 2 etapas ligadas.
  - **Login sem 2 etapas** (`operador@buteco.local`): a senha leva ao inventário.
  - **Convite:**
    - o link aberto leva a barra a `/convite`, sem fragmento, com a chave `buteco.link.convite` gravada;
    - o histórico cresceu uma entrada só (de 4 para 5), e ela não tem o token: voltar leva ao `/entrar`, e avançar volta a `/convite`, sem fragmento, reaproveitando o token guardado;
    - "Criar senha e entrar" levou ao inventário, como Marcos, com a chave apagada;
    - o mesmo link, reaberto, mostrou "Este link não vale mais" com o texto do convite, a barra limpa e a chave apagada.
  - **Login com 2 etapas** (o TOTP ligado pela API): a senha abre o passo do código; `000000` mostra "Código inválido." no campo, com `aria-invalid="true"` e `inputmode="text"`; o código certo leva ao inventário.
  - **Redefinição com as 2 etapas ligadas:**
    - a página abre com a barra limpa;
    - a senha curta reprova no campo, sem envio;
    - a válida leva ao `/entrar` no passo do código, com "Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.";
    - o `history.state` traz só `{"usr":{"etapa":"codigo"}}`, sem a senha;
    - um **código de recuperação**, digitado em minúsculas, entrou no mesmo campo e levou ao inventário.
  - **Entrada expirada:** a senha certa, 5 min 30 s de espera, e o código certo → "A entrada expirou. Informe e-mail e senha de novo.", de volta ao formulário. O texto reconhecido é o do servidor real.
  - **Registro, sem mudança de comportamento:** a chave do token sai quando a página desmonta, numa navegação dentro do painel. Sair do documento pelo voltar do navegador não roda a limpeza do React, então a chave fica na aba até ela fechar ou o link ser usado. Foi o que deixou o "avançar" reaproveitar o token. A spec descreve a limpeza na desmontagem, e é isso que acontece.
  - **Não conferido aqui:** os dois esquemas de cor e a aparência, que ficam para a validação visual (7.5).
- [x] 7.5 **Validação visual quadro a quadro com o dono** (convenção 14), contra o `Main`, o `AceitarConviteSemProvedor` e o `RedefinirSenha` (versão `1793853618-1811`), em cada estado listado no design. A conferência é iterativa, e o archive só vem depois do ok do dono.
  **Feito (06/11/2026):** o dono validou quadro a quadro, **sem divergência**, com o manual e as ferramentas de `~/.cache/buteco-agents/245-validacao/` (os usuários `validacao.2fa@` e `validacao.bloqueio@` no banco local, e o simulador de respostas `falhas.js` para os estados de 423, 429, 503, rede, chave e expiração). Decisão do dono na validação (**Q7**, no design, D4): o token que fica no `sessionStorage` depois do "voltar" do navegador, saindo do documento, está aceito.
- [x] 7.6 Medição da convenção 18 **depois** da validação visual: produção e testes, separados, por `git diff --numstat` contra `1131635`, só código, comparados à projeção do design.
  **Feito (06/11/2026)**, com os arquivos novos contados (`git add -N`), fora `docs/`, `openspec/`, `.md` e `.json`:
  - **produção:** +1.044 / −127, em 21 arquivos, contra ~925 projetados (**+13%**);
  - **testes:** +1.118 / −17, em 12 arquivos, contra ~1.660 projetados (**−33%**).

  | componente | produção (proj. → real) | testes (proj. → real) |
  |---|---|---|
  | passo do código, `continueEntry`, `entryNotices` e o `/entrar` vindo do link | ~240 → 433 | ~420 → 378 |
  | `linkToken` e `accessLinkApi` | ~160 → 124 | ~280 → 111 |
  | páginas do link, formulário, cartões, política e data | ~420 → 399 | ~700 → 505 |
  | `PasswordField`, guardas, ESLint e os campos trocados | ~70 → 32 | ~200 → 106 |
  | rotas, "Esqueceu a senha?" e rodapé | ~30 → 56 | ~60 → 13 |
  | teste renomeado no apps/api | — | ~5 → 5 |

  **Leitura:** a produção errou para cima no passo do código: os avisos por status saíram para o `entryNotices.ts`, compartilhado com as páginas do link, e o `SecondFactorForm` tem os seis avisos. Os testes ficaram abaixo porque o `describe.each` cobre as duas páginas do link num arquivo só, e a projeção contava cada página à parte.

## 8. Documentação e registros

- [x] 8.1 (docs) `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, antes do archive.
  **Em andamento (05/11/2026):**
  - o `CHANGELOG.md` tem as entradas da #245 (em Autenticação) e da #170 (em Fixed);
  - a fila da M1 no `02` está renumerada (17 itens), com a nota da divisão;
  - a entrada da change no `02` vem depois da medição (7.6).

  **Feito (06/11/2026):** a entrada `entrada-publica-painel` no fim do `02`. Também o comentário do `TwoFactor:EnrollmentEnabled` no `appsettings.json` do `apps/api`, que dizia "fechado até o painel ter a tela do código (#183)": agora diz que fecha até a tela do cadastro (#246), e que o passo do código já existe (#245). Só comentário.

  **Documentação que esta change tornou falsa, corrigida (premissa que só toca doc):**
  - `docs/development.md` dizia que quem cadastrasse o TOTP "não entra mais pelo painel até a #183";
  - `docs/configuration.md` e duas seções do `docs/deployment.md` (#218 e #181) diziam que o painel não tinha a tela do código nem as páginas do link.

  Também entrou a seção de deploy **"Entrada pública do painel (#245)"** no `docs/deployment.md`.
- [x] 8.2 (issues) Comentários de entrega, com o ok do dono:
  - na **#246**: o `PasswordField` e as guardas, o passo do código pronto no `/entrar`, o `state` do router com que o `/entrar` abre no passo;
  - na **#183**: a entrega do passo A.

  O PR leva `Closes #245` e `Closes #170`.
  **Feito (06/11/2026):** [comentário na #246](https://github.com/aldovrando-oliveira/buteco-agentes/issues/246#issuecomment-6700633258) e [comentário na #183](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6700633531).
