## Context

Primeiro passo da divisão da #183 (decisão do dono, 05/11/2026): a entrada pública, tudo que acontece antes de haver sessão. O B (#246) traz Minha conta e o aviso de senha alterada; o C (#183) traz Usuários, Editar usuário e Identidade.

**Fontes:**

- **Contratos:** os comentários da #183 da #218 (o passo do código), da #181 (as rotas públicas e as cinco divergências) e da #219, e os designs arquivados dessas changes:
  - `2026-11-02-verificacao-2-etapas-totp` D2, D6 e D7;
  - `2026-11-02-convites-redefinicao-senha` D3, D6, D7, D8 e D10.

  O inventário das telas da M1 está no [comentário da #183 de 05/11/2026](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6649327956).
- **Protótipo:** canvas "Protótipo de autenticação federada" (`H7dPBZafiSkdKd1hqesPZ6`, versão `1793853618-1811`, corrigido pelo dono três vezes em 05/11/2026), lido pela ferramenta Artifact com as propriedades de cada quadro. O quadro `LoginSemProvedor` saiu: o login da M1 é o `Main` com `federados=false`.
  - **`Main`** (`estado`):
    - formulário: `inicio`, `bloqueado`, `sessao-expirada`, `mfa-expirou`;
    - passo do código: `mfa`, `mfa-apos-nova-senha`, `mfa-codigo-vazio`, `mfa-codigo-invalido`, `mfa-bloqueado`, `mfa-muitas-tentativas`, `mfa-indisponivel`, `mfa-falha-de-rede`, `mfa-chave`;
    - fora deste passo: `senha-alterada` (#246, decisão do dono Q3), `redirecionando` e `negado` (M3).
  - **`AceitarConviteSemProvedor` e `RedefinirSenha`** (`estado`): `carregando`, `formulario`, `senhas-diferentes`, `senha-fora-da-regra`, `muitas-tentativas`, `envio-indisponivel`, `indisponivel`, `link-invalido`.

**O que existe em `1131635`:**

- **O login:** `/entrar` (`EntrarPage`, `EntrarForm`) e `/entrar/retorno`, da #217. O `createSession` devolve `ok` só no `204` (`sessionApi.ts` l. 33), e todo outro status vira aviso. O `200 { segundoPasso }` cai no `default`, "Não foi possível entrar. Tente de novo." (`EntrarPage.tsx` l. 44).
- **O que vem depois do `204`:** o `EntrarPage` segue o `retorno` conferido (`followableAuthorizeReturn`) ou começa o fluxo com `beginLogin` (`EntrarPage.tsx` l. 71–80). É o mesmo caminho que o `204` do código precisa.
- **As rotas sem sessão:** `entrar`, `entrar/retorno` e `login` (`routes.tsx` l. 40–42). Não há rota `*` nem `errorElement`.
- **Os campos de senha:** `PasswordInput` direto do Mantine em `EntrarForm`, `WahaCredentialFields`, `TelegramCredentialFields` e `McpServerForm`. Só o último tem o `aria-invalid` explícito (`McpServerForm.tsx` l. 439–445, D8 da change `frontend-mcp-autenticacao-header-customizado`).
- **O caminho das rotas:**
  - em produção, o `nginx.conf` leva `/auth/*` ao apps/api, e `/convite` e `/redefinir-senha` ao `location /`, com o `index.html` (#181 D3);
  - em desenvolvimento, o proxy do Vite leva `/auth` ao apps/api (`vite.config.ts` l. 39), com o `Origin` do painel, que as rotas públicas conferem.
- **O texto do "expirou" no apps/api:** `TwoFactorLoginTests.ExpiredPendingSession_TheEntryExpired` (`apps/api/tests/Buteco.Api.Tests/Users/TwoFactor/TwoFactorLoginTests.cs` l. 197) já afirma o texto exato do `401`, por literal. `WithoutAPendingSession_TheEntryExpired_AndNothingIsCounted` (l. 171) afirma o mesmo texto sem cookie e depois da redefinição.

**Textos do servidor, conferidos no código em `1131635`:**

| rota | status | `title` |
|---|---|---|
| `POST /auth/session/second-factor` | `401` | "Código inválido." (`SessionEndpoints.InvalidCodeMessage`) ou "A entrada expirou. Informe e-mail e senha de novo." (`ExpiredMessage`) |
| | `423` | "Acesso pausado por tentativas seguidas. Tente de novo mais tarde." (`LockedMessage`), com `Retry-After` |
| | `500` | "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador." |
| | `400` | "Corpo do código inválido." |
| `POST /auth/invitation`, `/auth/password-reset` e as duas de conclusão | `404` | "Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador." |
| `…/accept`, `…/complete` | `400` | "A senha precisa ter de 12 a 128 caracteres." (`PasswordPolicy.Message`) |

## Goals / Non-Goals

**Goals:**

- O passo das 2 etapas no `/entrar`, com todos os estados do quadro.
- "Esqueceu a senha?" e o rodapé do convite no `/entrar`.
- `/convite` e `/redefinir-senha`, do link à sessão aberta, inclusive com o passo do código.
- O `aria-invalid` em todo campo de senha do painel, por um caminho só, que fecha a #170.

**Non-Goals:**

- **O aviso "Senha alterada."** (leitura no `/entrar` e gravação no logout): vai para a #246, com o único chamador (decisão do dono, Q3; [registro](https://github.com/aldovrando-oliveira/buteco-agentes/issues/246#issuecomment-6649626655)).
- **Minha conta** (#246) e **Usuários, Editar usuário e Identidade** (#183), incluindo a seção "Acesso" do menu e o link do rodapé para Minha conta.
- **Provedores federados** (M3): o bloco "ou entre com", os estados `redirecionando` e `negado` do `Main`, e o quadro `AceitarConvite` com provedor.
- **Um campo de tipo nos `401` do segundo passo:** é a #247, sem milestone. Aqui o painel reconhece o texto (Q1).
- **Mudança de comportamento no `apps/api`.** Só o nome e o comentário de um teste mudam (D1). Dado que a tela precisar e o backend não servir é achado a sequenciar, e não backend de improviso. Até aqui, nenhum.

## Decisions

### D1. O passo do código é um estado do `/entrar`, sem rota nem URL própria

```
 /entrar, etapa "credenciais"                           /entrar, etapa "codigo"
 ────────────────────────────                           ───────────────────────
 POST /auth/session ──204──▶ continuarEntrada() (o caminho de hoje)
                    ──200 {segundoPasso:"totp"}──▶ campo "Código"
                    ──outro──▶ aviso de hoje          │ campo vazio ─▶ "Informe o código." (sem enviar)
                                                      │ POST /auth/session/second-factor {codigo}
                                                      ├─204────────────────▶ continuarEntrada()
                                                      ├─401 "A entrada expirou…" (igualdade)─▶ etapa "credenciais" + aviso azul
                                                      ├─401 (qualquer outro)─▶ "Código inválido." no campo
                                                      ├─423────────────────▶ aviso amarelo no passo (N do Retry-After)
                                                      ├─429────────────────▶ aviso amarelo no passo; botão volta depois do Retry-After
                                                      ├─503────────────────▶ aviso vermelho "O painel está iniciando…"; idem
                                                      ├─500────────────────▶ aviso vermelho com o title (o texto do quadro mfa-chave)
                                                      ├─rede───────────────▶ aviso vermelho "Não foi possível falar com o servidor." (mfa-falha-de-rede)
                                                      └─"Voltar"───────────▶ etapa "credenciais"
```

- **A etapa é estado do `EntrarPage`, e não parte da URL.**
  - **Por quê:** o passo pendente vive no cookie `buteco-sessao` (5 min, #218 D2), que o SPA não lê. Uma URL `/entrar?etapa=codigo` recarregada depois do cookie vencer mostraria um campo inútil.
  - **Ao recarregar:** a página volta às credenciais. O cookie pendente fica até vencer e não vale nada sozinho (#218 D2).
  - **A exceção** é a entrada vinda das páginas do link (D3), que abre direto no passo pelo `state` do router, e não pela URL.
- **`continuarEntrada()`** é a extração do bloco de hoje (`EntrarPage.tsx` l. 71–80): segue o `retorno` conferido ou começa o fluxo com `beginLogin`, com o `voltarPara`. O `204` da senha e o `204` do código chamam a mesma função, e o `retorno` e o `voltarPara` da URL continuam valendo no passo do código.
- **Os dois `401` (Q1, decisão do dono).** Só o `title` os separa:
  - o "A entrada expirou…" é reconhecido pela igualdade com a constante `ENTRADA_EXPIROU` de `features/auth/messages.ts`, que cita a origem (`SessionEndpoints.ExpiredMessage`);
  - todo outro `401` é "Código inválido.", mostrado no campo com `aria-invalid`.

  Os dois lados ficam presos por teste:
  - **no painel**, o `401` com exatamente aquele texto volta às credenciais, e um com o texto alterado fica no passo;
  - **no apps/api**, o teste que já fixa o texto exato por literal (`ExpiredPendingSession_TheEntryExpired`) passa a se chamar `SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes`, com um comentário que aponta a constante do painel. A asserção não muda. Assim, quem mudar o texto no servidor vê no nome do teste que reprovou que o painel depende dele.

  A troca por um campo de tipo é a **#247** (sem milestone, bloqueada por esta).
- **O bloqueio no passo** usa o mesmo texto do `/entrar`, com N do `Retry-After`. O quadro escreve "10 minutos" fixo, e a #217 já registrou o N calculado (convenção 17).
- **`429` e `503`** desabilitam "Verificar" até passar o `Retry-After`, sem reenviar sozinhos, como o botão "Entrar" (`operator-login-ui`).
- **"Voltar"** só troca a etapa. O cookie pendente fica até vencer, como depois de recarregar.

### D2. O campo do código aceita os dois formatos, sem validar a forma no painel

- **Os atributos são os do quadro:**
  - `inputmode="text"`, `autocapitalize="characters"`, `autocomplete="one-time-code"` e `spellcheck="false"`;
  - o placeholder "000 000" e a ajuda "Código de recuperação: 16 letras e números, como XXXX-XXXX-XXXX-XXXX.".

  O teclado de texto é o que deixa digitar o código de recuperação no celular.
- **O painel só tira os espaços das pontas.** A normalização é do servidor: espaços no meio, hífens, caixa e as trocas de Crockford (`I`/`L` → `1`, `O` → `0`, #218 D6). Validar a forma no painel duplicaria a regra, e uma divergência recusaria um código que o servidor aceita.
- **Campo vazio** mostra "Informe o código." no campo, com `aria-invalid`, sem enviar (estado `mfa-codigo-vazio`).

### D3. O aceite e a conclusão terminam no login, e o passo do código é o do `/entrar`

```
 /convite#tok ─▶ guarda o token (D4) e troca o endereço por /convite
              ─▶ estado carregando ─▶ POST /auth/invitation {token}
                     ├─404──────────▶ link-invalido (apaga o token)
                     ├─503 / rede───▶ indisponivel ("Tentar de novo" repete a consulta)
                     └─200 {nome, email, convidadoPor, expiraEm}──▶ formulario
              ─▶ POST /auth/invitation/accept {token, senha}
                     ├─400──▶ senha-fora-da-regra (o title no campo)
                     ├─404──▶ link-invalido (apaga o token)
                     ├─429──▶ muitas-tentativas (aviso no formulário; botão volta depois do Retry-After)
                     ├─503 / rede──▶ envio-indisponivel (aviso no formulário, sem perder o que foi digitado)
                     └─204──▶ apaga o token ─▶ POST /auth/session {email da consulta, senha}
                                 ├─204──▶ continuarEntrada('/')
                                 ├─200 {segundoPasso}──▶ /entrar, etapa "codigo", aviso "Senha salva…" (mfa-apos-nova-senha)
                                 └─outro──▶ /entrar, com o e-mail preenchido e o aviso daquele status
```

- **O `POST /auth/session` com o e-mail da consulta** é a divergência 5 da #181: o aceite e a conclusão respondem `204` sem sessão (#181 D6, P5). A senha fica só na memória do componente, entre as duas chamadas.
- **Com o `200 { segundoPasso }`** (Q2, decisão do dono), a página navega ao `/entrar` com `state: { etapa: 'codigo', aviso: 'senha-salva' }`. O `/entrar` abre direto no passo, com "Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar." (estado `mfa-apos-nova-senha`).
  - **Quando acontece:** na prática, na redefinição de quem tem as 2 etapas, que a redefinição mantém (#181 D7). O convidado acabou de criar a conta e não tem a verificação, mas o caminho é o mesmo.
  - **O cookie pendente** já foi gravado pelo `POST /auth/session`, na mesma origem.
- **Login recusado depois do aceite** (`423`, `429`, `503`, rede): a senha já foi criada, e repetir o aceite daria `404`, porque o link já foi usado. A página navega ao `/entrar` com o e-mail preenchido e o aviso daquele status, que o `/entrar` já sabe mostrar. Nenhum texto novo.
- **O destino é a tela inicial** (`continuarEntrada('/')`): o link não traz `voltarPara`.

### D4. O token do link: do fragmento ao `sessionStorage` da aba, apagado no fim (Q4)

A #181 (D3) decidiu: o painel lê `location.hash`, troca o endereço por `/convite` com `history.replaceState` antes de qualquer outra coisa, e manda o token no corpo dos `POST`s. O dono decidiu onde o token fica depois disso (Q4, 05/11/2026): **no `sessionStorage` da aba, apagado no sucesso, no link inválido e ao sair da página.**

- **As chaves:** `buteco.link.convite` e `buteco.link.redefinir-senha`, uma por página.
- **Na primeira renderização** (`linkToken.take(pagina)`), nesta ordem:
  1. se `location.hash` traz um token, ele vai para a chave, e o endereço vira `/convite` (ou `/redefinir-senha`) por `history.replaceState`, mantendo o `state` do router;
  2. o token da página é o valor da chave.

  A leitura vai para o estado do componente (inicializador do `useState`). Sob o `StrictMode`, o inicializador roda duas vezes e a segunda chamada já encontra o endereço trocado, mas acha o mesmo valor na chave.
- **Apagado em três casos:**
  - **sucesso:** no `204` do aceite ou da conclusão, antes do login;
  - **link inválido:** no `404` da consulta, do aceite ou da conclusão;
  - **ao sair da página:** na limpeza do efeito, ao desmontar ("Ir para o login", o voltar do navegador dentro do painel, qualquer navegação do SPA). O efeito grava a chave a partir do estado e a apaga na limpeza. No `StrictMode`, a sequência montar → limpar → montar termina com a chave gravada, porque o estado sobrevive à remontagem simulada.

  Recarregar a página não roda a limpeza do React, então a recarga reaproveita o token. Fechar a aba apaga o `sessionStorage` sozinho.
- **Sair do documento pelo "voltar" do navegador** (o link colado na barra, e o voltar levando à página anterior, outro documento) também não roda a limpeza: a chave fica na aba até ela fechar ou o link ser usado, e o "avançar" reaproveita o token. Visto na conferência no navegador (7.4) e **aceito pelo dono (Q7)**: o token vale uma vez, só naquela aba, e expira (72 h o convite, 24 h a redefinição); apagá-lo quebraria o "avançar" com um link ainda válido.
- **Sem token** no fragmento nem na chave (a página aberta direto): estado `link-invalido`, sem chamar o servidor.
- **O token não sai do navegador por outro caminho:** nenhum `GET` com ele, o `Referrer-Policy: no-referrer` já vale (#217), e o painel não o loga.

### D5. Validação e textos das páginas do link

- **Política, no painel e no servidor:** de 12 a 128 caracteres pelo `length` do JavaScript, que conta unidades UTF-16 como o `string.Length` do C# (`PasswordPolicy`, #219 D3). Sem `trim`.
  - O painel reprova antes de enviar, com "A senha precisa ter de 12 a 128 caracteres." no campo "Senha" (ou "Nova senha"), com `aria-invalid` (estado `senha-fora-da-regra`).
  - O `400` do servidor é a rede de segurança, mostrado no mesmo lugar pelo `title`.
  - A ajuda "De 12 a 128 caracteres." fica sempre.
- **"Repita a senha" diferente** mostra "As senhas não conferem." no segundo campo, com `aria-invalid`, sem enviar (estado `senhas-diferentes`).
- **Link inválido, com um texto por página (Q5, decisão do dono):** o título "Este link não vale mais", o texto do quadro de cada página e "Ir para o login":
  - **convite:** "O convite pode ter vencido, já ter sido usado ou ter sido cancelado. Peça um novo link a quem convidou você.";
  - **redefinição:** "O link pode ter vencido, já ter sido usado, ou outro link pode ter sido gerado depois dele. Peça um novo a um administrador do painel.".

  O `title` do servidor não aparece. Nenhum dos dois textos distingue vencido de usado, como a #181 D8 pede.
- **Carregando:** "Conferindo o link…" com o indicador, no lugar do formulário (estado `carregando`).
- **Indisponível na consulta** (`503` ou rede): "Não foi possível abrir o link agora", "O painel está indisponível ou sem conexão. O link continua valendo: tente de novo em alguns segundos." e "Tentar de novo", que repete a consulta (estado `indisponivel`).
- **`429` no aceite ou na conclusão:** o aviso "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." sobre o formulário, com o botão de volta depois do `Retry-After` (estado `muitas-tentativas`).
- **`503` ou rede no aceite ou na conclusão:** o aviso "Não foi possível salvar agora. O que você digitou continua aqui: tente de novo em alguns segundos." sobre o formulário, com `role="alert"`, mantendo o que foi digitado (estado `envio-indisponivel`). O botão volta depois do `Retry-After`, quando há.
- **O resto do quadro:**
  - "Olá, {nome}", com o nome completo da consulta;
  - no convite, "{convidadoPor} convidou você para o Buteco Agentes. Crie sua senha para entrar.";
  - na redefinição, "Criar nova senha" e "Olá, {nome}. Crie uma nova senha para entrar no Buteco Agentes.";
  - o e-mail em `readonly`, com `autocomplete="username"`, para o gerenciador de senhas;
  - o rodapé "Este convite vale até {expiraEm} e só pode ser usado uma vez." (ou "Este link vale até…"), com a data local `dd/mm/aaaa às hh:mm`;
  - na redefinição, o aviso "Ao salvar, você sai de todos os navegadores em que estava conectado. Se a sua conta usa verificação em 2 etapas, o código continua sendo pedido.";
  - os botões "Criar senha e entrar" e "Salvar senha e entrar".

### D6. `PasswordField`: o `aria-invalid` num lugar só, com duas guardas (Q6)

- **O defeito continua no Mantine mais novo.** O painel usa a 9.4.2 (`apps/frontend/node_modules/@mantine/core/package.json`). Na 9.7.5, a mais nova no npm em 05/11/2026, o `PasswordInput.mjs` ainda chama o `Input` com `withAria: false` (l. 169) e marca o `<input>` só com `data-invalid` (l. 175). Atualizar o Mantine não resolve, e fica fora daqui.
- **`components/forms/PasswordField.tsx`** repassa as props ao `PasswordInput` e põe `aria-invalid={error ? true : undefined}` no `<input>`. O valor chega pelo `...rest`, que o Mantine leva ao `<input>` (`PasswordInput.mjs` l. 171 na 9.4.2), a mesma via que o `McpServerForm` usa.
- **Quem passa a usá-lo:** os campos novos (as páginas do link), o `EntrarForm`, `WahaCredentialFields`, `TelegramCredentialFields` e o `McpServerForm`, que perde o ajuste local e o comentário dele.
- **Guardas (Q6, decisão do dono):**
  - **um teste estático na suíte**, no molde do `nginxHeaders.test.ts`, reprova se algum arquivo de `src/` fora do `PasswordField` importar `PasswordInput` de `@mantine/core`;
  - **uma regra `no-restricted-imports` no `eslint.config.js`**, com a mesma restrição, para o aviso no editor. O `PasswordField` fica fora dela por `overrides`.
- **#170:** cada campo da lista dela ganha o teste que afirma o atributo com erro, como ela pede:
  - o `LoginForm` virou o `EntrarForm` na #217. Ele não tem erro por campo, porque o "Informe e-mail e senha." é um aviso geral, então o teste dele afirma que o campo é o `PasswordField`;
  - WAHA e Telegram afirmam o atributo.

### D7. Rotas, e onde o código mora

- **Rotas novas, sem sessão**, ao lado de `entrar`: `convite` e `redefinir-senha`. O caminho é o que o apps/api monta (`AccessLinkUrls`, #181 D9), e mudar um exige mudar o outro.
- **Feature:** as páginas do link ficam em `features/auth`, com o `/entrar`. É o mesmo conceito de domínio, a entrada no painel, e as três páginas compartilham o `continuarEntrada`, o aviso por status e o passo do código.
- **Sem `request<T>`:** as rotas públicas não levam token. O `accessLinkApi.ts`, como o `sessionApi.ts` de hoje, usa o `fetch` direto e devolve um valor por status, sem lançar erro, porque cada status tem um estado de tela.

### D8. Testes e guardas

Na suíte `frontend` (Vitest + Testing Library, `fetch` substituído por `vi.stubGlobal`, como no `EntrarPage.test.tsx`), e um teste renomeado na suíte `api`.

**Cada guarda é visto reprovar contra o defeito que ele nomeia** (convenção 15). A tarefa reintroduz só aquele defeito, roda o teste, registra a reprovação e desfaz:

| guarda | defeito reintroduzido |
|---|---|
| o `200 { segundoPasso }` abre o passo do código | o `createSession` volta a tratar só o `204` como sucesso |
| o "expirou" volta às credenciais | a comparação com `ENTRADA_EXPIROU` trocada por "qualquer `401` é código inválido" |
| um `401` com outro texto fica no passo | a comparação afrouxada para `title.startsWith('A entrada')` |
| o `204` do código segue o `retorno` e o `voltarPara` | o `204` do código chamando `beginLogin('/')` fixo |
| o endereço é trocado antes do primeiro `fetch` | o `replaceState` movido para depois da consulta |
| o token nunca vai numa URL | a consulta montada como `POST /auth/invitation?token=…` |
| o token é apagado no sucesso, no link inválido e ao desmontar | cada um dos três `remove` tirado, um por vez |
| a recarga reaproveita o token | o token guardado só em memória (sem a chave) |
| política e senhas diferentes não enviam | a validação local removida |
| o `200` do login depois do aceite chega ao passo do código | o `200` tratado como erro na página do link |
| `PasswordField` marca `aria-invalid` | o `aria-invalid` tirado do `PasswordField` |
| a guarda estática do `PasswordInput` | um `import { PasswordInput } from '@mantine/core'` de volta no `WahaCredentialFields` |
| `SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes` (api) | o `ExpiredMessage` com uma palavra trocada |

**Os casos, além das guardas:**

- **Passo do código:** cada resposta da tabela do D1; os atributos do campo (D2); "Voltar"; o `/entrar` aberto com `state` no passo e com o aviso "Senha salva…".
- **Páginas do link:** cada estado do quadro, em cada página; o login com o e-mail da consulta; o login recusado chega ao `/entrar` com o e-mail e o aviso; o `StrictMode` não perde o token.
- **`/entrar`:** "Esqueceu a senha?" abre e fecha o texto; o rodapé do convite.
- **`PasswordField`:** com e sem erro; os testes de WAHA, Telegram e `McpServerForm` afirmando o atributo.
- **Rotas:** `/convite` e `/redefinir-senha` abrem sem sessão.

## Árvore de pastas proposta

```
apps/frontend/
├── eslint.config.js                           (alterado: no-restricted-imports do PasswordInput, D6)
└── src/
    ├── app/
    │   ├── routes.tsx                         (alterado: convite, redefinir-senha)
    │   └── router.test.tsx                    (alterado: as duas rotas sem sessão)
    ├── components/forms/
    │   ├── PasswordField.tsx                  (novo, D6)
    │   ├── PasswordField.test.tsx             (novo)
    │   └── passwordFieldGuard.test.ts         (novo: guarda estática do D6)
    └── features/
        ├── auth/
        │   ├── api/
        │   │   ├── sessionApi.ts              (alterado: 200 {segundoPasso} e o second-factor)
        │   │   ├── sessionApi.test.ts         (novo)
        │   │   ├── accessLinkApi.ts           (novo: consulta, aceite e conclusão)
        │   │   └── accessLinkApi.test.ts      (novo)
        │   ├── components/
        │   │   ├── EntrarForm.tsx             (alterado: "Esqueceu a senha?", PasswordField)
        │   │   ├── SecondFactorForm.tsx       (novo: o passo do código, D1 e D2)
        │   │   ├── SecondFactorForm.test.tsx  (novo)
        │   │   ├── NewPasswordForm.tsx        (novo: e-mail travado, senha e repetição, D5)
        │   │   ├── NewPasswordForm.test.tsx   (novo)
        │   │   ├── InvalidLinkCard.tsx        (novo: estado link-invalido)
        │   │   ├── UnavailableLinkCard.tsx    (novo: estado indisponivel)
        │   │   └── EntryCard.tsx              (novo: o cartão com a marca e o rodapé, comum às três páginas)
        │   ├── pages/
        │   │   ├── EntrarPage.tsx             (alterado: etapas, state do router, continuarEntrada)
        │   │   ├── EntrarPage.test.tsx        (alterado)
        │   │   ├── ConvitePage.tsx            (novo)
        │   │   ├── ConvitePage.test.tsx       (novo)
        │   │   ├── RedefinirSenhaPage.tsx     (novo)
        │   │   └── RedefinirSenhaPage.test.tsx (novo)
        │   ├── continueEntry.ts               (novo: continuarEntrada, extraído do EntrarPage)
        │   ├── linkToken.ts                   (novo: fragmento → sessionStorage → endereço, D4)
        │   ├── linkToken.test.ts              (novo)
        │   ├── entryNotices.ts                (novo: aviso por status, comum às três páginas)
        │   └── messages.ts                    (alterado: textos novos e ENTRADA_EXPIROU)
        ├── channels/components/
        │   ├── WahaCredentialFields.tsx       (alterado: PasswordField)
        │   ├── TelegramCredentialFields.tsx   (alterado: PasswordField)
        │   └── (os testes dos dois, novos ou alterados: o atributo com erro)
        └── mcp-servers/components/
            ├── McpServerForm.tsx              (alterado: PasswordField, sai o ajuste local)
            └── McpServerForm.test.tsx         (alterado, se preciso: o atributo continua afirmado)

apps/api/
└── tests/Buteco.Api.Tests/Users/TwoFactor/
    └── TwoFactorLoginTests.cs                 (alterado: nome e comentário de um teste, D1)
```

Nada em `libs/`. Nenhum arquivo de produção do `apps/api`.

## Tamanho

**Âncora** (convenção 18), só código, por `git diff --numstat` do merge da PR #228 (#217) em `apps/frontend/src`:

- **produção:** +1.317 / −290, em 36 arquivos;
- **testes:** +2.091 / −313, em 35 arquivos.

A #217 levou a infraestrutura de sessão inteira (registro, lock, renovação, PKCE), que aqui já existe. Esta change tem duas páginas e um passo, construídos sobre ela.

**Projeção por componente** (rascunho, feito antes da verificação no navegador, então erra para baixo):

| componente | produção | testes |
|---|---|---|
| passo do código, `continuarEntrada` e o `/entrar` vindo do link (D1, D2) | ~240 | ~420 |
| `linkToken` e `accessLinkApi` (D4, D7) | ~160 | ~280 |
| duas páginas do link, formulário de senha nova, cartão e os estados inválido e indisponível (D3, D5) | ~420 | ~700 |
| `PasswordField`, as duas guardas e os quatro campos trocados (D6) | ~70 | ~200 |
| rotas, "Esqueceu a senha?" e o rodapé | ~30 | ~60 |
| teste renomeado no apps/api | ~5 | — |
| **total** | **~925** | **~1.660** |

Cabe num PR revisável, na régua das changes anteriores.

## Risks / Trade-offs

- **[O "expirou" reconhecido pelo texto]** → Se o `ExpiredMessage` mudar no apps/api, o painel mostra "A entrada expirou…" como erro de código, no campo, e a pessoa fica no passo até clicar em "Voltar". Não tranca ninguém. Mitigação:
  - o teste do apps/api com o nome que diz que o painel depende do texto;
  - o teste do painel;
  - a #247.
- **[O token no `sessionStorage`]** → Um XSS na origem do painel lê a chave. Ele também lia o fragmento, a memória e o `localStorage` com os tokens da sessão (#217 D2). A CSP da #217 é a defesa. O token vale 72 h ou 24 h, é de uso único e sai no sucesso, no `404` e ao sair da página.
- **[Resposta do aceite perdida na rede]** → A senha pode ter sido criada sem a página saber. "Criar senha e entrar" de novo dá `404`, e a página mostra "Este link não vale mais", que fala em "já ter sido usado". O texto do quadro não traz o "se você já criou a senha, entre normalmente" do servidor. Aceito: "Ir para o login" leva ao caminho certo.
- **[Link aberto numa aba com sessão de outra pessoa]** → O login novo regrava o registro sob o lock, e o refresh da sessão anterior fica sem dono no navegador. A autorização continua `valid` no banco até a limpeza (#219 D4). Aceito: é o mesmo que entrar com outra conta sem clicar em "Sair", e o caso típico é o admin conferindo o próprio link.
- **[Validação visual]** → O jsdom não vê layout. A conferência é quadro a quadro, nos dois esquemas de cor, antes do archive (convenção 14).

## Migration Plan

Só frontend, mais um teste renomeado no apps/api.

- **Deploy:** o do painel não depende de nenhum outro app. As rotas usadas já estão em produção desde a #218 e a #181.
- **Rollback:** reverter o PR.
- **O passo do código em produção:** o cadastro das 2 etapas continua fechado até o passo B (#218 D1, `Identity:TwoFactor:EnrollmentEnabled`). Até lá, o passo só aparece em desenvolvimento ou para quem a ligou por configuração.

## Divergências do protótipo (convenção 17)

Nenhuma. As três que a primeira versão deste design registrava (o campo "Senha" sem `aria-invalid` em `senha-fora-da-regra`, o `503` e a rede no aceite e na conclusão, e a falha de rede no passo do código) foram desenhadas pelo dono na versão `1793853618-1811` (05/11/2026): o campo marcado, `envio-indisponivel` e `mfa-falha-de-rede`.

## Registros (convenção 23)

**Já publicados em 05/11/2026:**

- **#247**, sem milestone, bloqueada pela #245: o campo de tipo nos `401` do segundo passo, e o painel decidindo por ele (Q1).
- **#246:** o aviso "Senha alterada." vai para lá, leitura e gravação (Q3), com as três opções do caminho pelo logout; e as divergências do B que restam ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/246#issuecomment-6649626655)).
- **#183:** as divergências do C que o protótipo resolveu e as que restam ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6649627043)).

**A publicar no fechamento:**

- **#246:** o que o B herda: o `PasswordField` e as guardas, o passo do código pronto no `/entrar` (o cadastro pode abrir) e o `state` do router com que o `/entrar` abre no passo.
- **#183:** o comentário de entrega do passo A.
- **#170:** fechada pelo PR (`Closes #170`).

## Decisões do dono (05/11/2026)

Na revisão do proposal e do design:

| | pergunta | decisão | onde |
|---|---|---|---|
| Q1 | Como distinguir os dois `401` do passo do código? | igualdade com uma constante, com teste no frontend; no apps/api, o teste que fixa o texto exato do pendente vencido com um nome que diga que o painel depende dele (`SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes`); issue sem milestone para o campo de tipo (#247) | D1 |
| Q2 | Onde aparece o passo do código depois do aceite ou da redefinição? | no `/entrar`, já no passo, quando a conta tem as 2 etapas (estado `mfa-apos-nova-senha`) | D3 |
| Q3 | O aviso "Senha alterada." entra no A sem chamador até o B? | **não**: sai do A inteiro, leitura e gravação, e vai para a #246, que tem o único chamador; registrado na #246 | Non-Goals |
| Q4 | Onde fica o token depois de sair do endereço? | no `sessionStorage` da aba, apagado no sucesso, no link inválido e ao sair da página, com teste para os três | D4 |
| Q5 | O texto do link inválido é o do quadro ou o do servidor? | um texto por página, como nos quadros | D5 |
| Q6 | A guarda do `PasswordField` é um teste estático ou uma regra do ESLint? | as duas: o teste estático na suíte, e a regra do ESLint também | D6 |
| Q7 | O token que fica no `sessionStorage` depois do "voltar" do navegador (saindo do documento) deve ser apagado? (06/11/2026, na validação visual) | **não**: fica aceito. Vale uma vez, só naquela aba, e expira em 24 h (redefinição) ou 72 h (convite); apagá-lo quebraria o "avançar" com um link válido | D4 |

**Também de 05/11/2026:** o protótipo foi corrigido três vezes a partir do inventário. A segunda correção trouxe os botões consertados e os estados `mfa-apos-nova-senha`, `mfa-codigo-vazio`, `mfa-muitas-tentativas`, `mfa-indisponivel`, `carregando`, `senha-fora-da-regra`, `muitas-tentativas` e `indisponivel`, e tirou o quadro `LoginSemProvedor`. A terceira marcou o campo "Senha" em `senha-fora-da-regra` e trouxe `envio-indisponivel` e `mfa-falha-de-rede`, e com isso nenhuma divergência restou para o A.

**Na aprovação das specs e do `tasks.md` (05/11/2026):** a renomeação da 1.2 está aceita e entra na lista de renomeados do fechamento; o teste novo do `aria-invalid` do `McpServerForm` (2.4) está aceito.

## Open Questions

Nenhuma.
