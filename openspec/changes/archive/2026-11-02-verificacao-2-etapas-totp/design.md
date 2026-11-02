## Context

**Estado atual (`86abb5d`, merge do PR #228, 01/11/2026).** A #180 e a #217 estão em `main`:

- **`POST /auth/session`** (`Users/Login/SessionEndpoints.cs`) confere JSON e `Origin`, o bloqueio por e-mail, a senha (Argon2id) e o estado, e então zera a contagem, registra o último acesso, grava o cookie `buteco-sessao` com `sub` e `buteco_stamp` (`IssuerSession.cs`, 5 min, sem renovação) e responde `204`.
- **O authorize** (`Identity/Flow/AuthorizeEndpoint.cs`) lê o cookie, relê o usuário e emite o código só se ele estiver ativo e com o stamp igual (l. 80–92). Sem sessão, `302 /entrar?retorno=…`, ou `login_required` com `prompt=none`.
- **Todo token novo de usuário** relê estado e stamp (`UserGrantTokenHandler`). Trocar o stamp derruba a renovação seguinte com `invalid_grant` (#180 D4).
- **O bloqueio por e-mail** (`LoginThrottleStore`, `LoginThrottlePolicy`): 5 falhas → 5 min, reincidência dobrando até 60 min, 24 h sem falha voltam ao início, `423` sem verificar nada durante o bloqueio, sucesso zera.
- **O painel de hoje** (#217) só conhece o `204` do `POST /auth/session`. Qualquer outro status cai no `default` de `avisoDe` e mostra "Não foi possível entrar. Tente de novo." (`features/auth/pages/EntrarPage.tsx`, `avisoDe`; `features/auth/api/sessionApi.ts` l. 33). Uma resposta "falta o código" deixaria a pessoa nesse aviso para sempre.
- **Quem chama uma rota autenticada** do `apps/api` com o token do emissor chega com `NameIdentifier` = `operator` (pela tabela de clientes) e com o `sub` do usuário preservado (`IssuerTokenAuthentication.MapPrincipalByClient`). O HMAC `operator` não traz `sub`. Nenhuma rota do `apps/api` lê o `sub` hoje: esta change é o primeiro consumidor de "quem é o usuário", e a #219 é o segundo.
- **Cifra em repouso no `apps/api`:** dois consumidores de AES-256-GCM com o mesmo formato (`nonce || ciphertext || tag`), cada um com a sua chave: `AesGcmMcpCredentialCipher` (`Mcp:CredentialEncryptionKey`, texto em base64) e `IdentityKeyMaterialCipher` (`Identity:SigningKeyEncryptionKey`, bytes). O comentário deste diz: "é o segundo consumidor de AES-GCM no `apps/api`, e a convenção 2 espera o terceiro". O segredo do TOTP é o terceiro.
- **O nginx do stack roteia por prefixo** (`apps/frontend/deploy/nginx.conf`, l. 78). A lista do `apps/api` é `agents|providers|mcp-servers|knowledge-bases|knowledge-index|insights|auth`. Prefixo servido e ausente da lista cai no fallback do SPA e responde `200` com HTML (comentário l. 11–15). Nenhum teste confere a lista; ela é conferida por `grep` e `curl`.

**A divisão aceita (#180, "Tamanho e divisão", 28/10/2026):**

| issue | o que é dela, para as 2 etapas |
|---|---|
| **#218** (esta) | TOTP: cadastro, verificação no login, códigos de recuperação, redefinição pelo admin, cifra do segredo, migration |
| #219 | rotas de Minha conta e de Usuários. A tabela da divisão conta "redefinir 2 etapas" entre as 11 operações dela, e o corpo da #219 lista "redefinir as 2 etapas (#218)" (D9) |
| #183 | as telas: o passo do código no login, o cadastro com QR e texto (decisão em aberto dela), os cards de Minha conta e de Editar usuário |
| #220 | a obrigatoriedade para todo usuário da M1, depois da #183 |

**O D4 da #180** já decidiu: 10 códigos de uso único, mostrados uma vez e guardados só em hash; "Redefinir verificação" apaga o segredo e os códigos e troca o `security_stamp`; a pessoa cadastra de novo no próximo acesso; e "as colunas das 2 etapas entram com a #218".

**O protótipo** (canvas "Protótipo de autenticação federada", `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`, relido em 01/11/2026 pela ferramenta Artifact):

- **"Login — com provedor federado"** (`Main.dc.html`), estado `mfa`: o mesmo cartão, com o título "Verificação em duas etapas", "Digite o código de 6 dígitos do seu app autenticador.", um campo "Código" (`inputmode="numeric"`, `autocomplete="one-time-code"`, placeholder `000 000`), o botão "Verificar", e embaixo "Voltar" e "Sem acesso ao app? Fale com o administrador." **Não há lugar para código de recuperação.**
- **"Editar usuário"** (`UsuarioDetalhe.dc.html`), card "Verificação em 2 etapas": o selo "Não configurada", o texto "Opcional para Operador. Quando entra pelo Google, a verificação é feita pelo próprio Google.", e o botão "Redefinir verificação", desabilitado quando não configurada. O card "Dados e papel" traz o aviso "Admin exige verificação em 2 etapas. Rafael ainda não configurou a dele e vai precisar fazer isso no próximo acesso." (#220, #184).
- Não há quadro de "Minha conta" no canvas.

### Verificado em 01/11/2026

**RFC 6238** (lido em `rfc-editor.org/rfc/rfc6238.txt`):

- §5.1: a chave "SHOULD be of the length of the HMAC output" (20 bytes no SHA-1), e o RFC recomenda guardá-la cifrada e decifrá-la só para verificar.
- §5.2: "We RECOMMEND that at most one time step is allowed as the network delay"; "We RECOMMEND a default time-step size of 30 seconds"; e "The verifier MUST NOT accept the second attempt of the OTP after the successful validation has been issued for the first OTP".
- Apêndice B: semente SHA-1 `12345678901234567890` (ASCII, 20 bytes), 8 dígitos, X = 30, T0 = 0. Os vetores SHA-1: T = 59 → `94287082`; 1111111109 → `07081804`; 1111111111 → `14050471`; 1234567890 → `89005924`; 2000000000 → `69279037`; 20000000000 → `65353130`. Com 6 dígitos, o valor é o mesmo inteiro truncado módulo 10⁶ (10⁶ divide 10⁸): `287082`, `081804`, `050471`, `005924`, `279037`, `353130`.

**Pacotes** (índice do NuGet e fonte):

| pacote | versão | publicado | licença | alvo / dependências | observação |
|---|---|---|---|---|---|
| `Otp.NET` | 1.4.1 | 01/12/2025 | MIT (arquivo `LICENSE.txt`, sem `licenseExpression`) | `net10.0` sem dependência | um mantenedor; a versão anterior é de 12/04/2024 |
| `OtpSharp` | 1.3.0.4 | 16/11/2015 | — | depende de `Base32` | parado |
| `TwoFactorAuth.Net` | 1.4.0 | 10/07/2020 | MIT | `System.Net.Http` | parado; gera QR por serviço externo |

**A fonte do `Otp.NET` 1.4.1** (tag `v1.4.1`, `83b39e1`, lida num diretório à parte):

- `Otp.CalculateOtp` faz a truncagem dinâmica do RFC 4226 (offset pelo último byte, `& 0x7f`), e `Digits` faz o módulo 10ⁿ com zeros à esquerda;
- `Otp.Verify` percorre a janela, compara em tempo constante e devolve o passo que bateu (`out long matchedStep`). É o que a proteção contra reuso precisa;
- `VerificationWindow` tem por padrão só o passo atual; `RfcSpecifiedNetworkDelay` é um para cada lado. A ordem é atual, anteriores, seguintes;
- o tempo entra por `DateTime`, e não por `TimeProvider`;
- `InMemoryKey` copia a chave para um buffer acolchoado e a apaga depois de cada HMAC;
- o resto do pacote (HOTP, SHA-256/512, correção de relógio, decodificação de base32, `OtpUri`) não seria usado.

**ASP.NET Core Identity:** o `Rfc6238AuthenticationService` é `internal` (`dotnet/aspnetcore`, `src/Identity/Extensions.Core/src/Rfc6238AuthenticationService.cs`), com passo de 3 min e janela de −2 a +2, para os códigos por e-mail e telefone. O caminho público (`AuthenticatorTokenProvider`) exige o `UserManager` do Identity, que o `apps/api` não usa. Não serve.

## Goals / Non-Goals

**Goals:**

- Quem ligar a verificação só recebe sessão do emissor depois do código, e o código de 6 dígitos não cai por força bruta.
- Nenhum usuário fica sem entrar pelo painel de hoje, que não tem a tela do código (D1).
- O backend que a #183 precisa para desenhar o passo do código, o cadastro, Minha conta e o card de Editar usuário, e que a #220 precisa para ligar a obrigatoriedade.
- As decisões a–k pedidas pelo dono, com evidência. As que são do dono estão em "Open Questions", cada uma com a recomendação.

**Non-Goals:**

- **Telas** (#183): o passo do código, o cadastro com QR, os cards.
- **Obrigatoriedade** e o estado "cadastro pendente" no login (#220).
- **Lista, detalhe e as outras ações de Usuários e de Minha conta** (#219). Elas leem as colunas criadas aqui.
- **Claim `amr`** no token (D10).
- **Troca da chave de cifra com recifragem** dos segredos (Risks).
- **WebAuthn, SMS, e-mail** como segundo fator.

## Decisions

### D1. (a) A transição sem tela: o cadastro fica fechado em produção até a #183; a verificação nunca é desligada

O risco é concreto: o painel de hoje trata qualquer resposta diferente de `204` como erro (Context). Quem ligasse a verificação ficaria no aviso "Não foi possível entrar" sem saída, e numa instalação com um Admin só, sem ninguém que o redefina.

```
                       cadastro         verificação no login     painel de hoje
                       (rotas /account) (POST /auth/session)
 (A) gate no cadastro  fechado em prod  sempre ligada            ninguém tem TOTP → sempre 204
 (B) gate na verificação aberto         desligada em prod        TOTP cadastrado não protege nada
 (C) rotas fora do ar  não mapeadas     sempre ligada            a #183 mapeia as rotas
 (D) aviso no painel   aberto           sempre ligada            a pessoa lê o aviso e não entra
```

| | (A) cadastro fechado por configuração | (B) segundo passo só exigido com configuração | (C) rotas de cadastro fora do ar até a #183 | (D) o painel da #217 avisa "segundo passo exigido" |
|---|---|---|---|---|
| alguém fica sem entrar? | não: sem cadastro em produção, ninguém tem TOTP | não | não | **sim**: o aviso explica, mas não deixa entrar |
| o TOTP cadastrado protege? | sim, sempre | **não**, até a configuração ligar: um segredo cadastrado que não é pedido dá falsa segurança | sim | sim |
| "tela não leva backend" | mantida: a #183 não muda código do `apps/api` | mantida | **quebrada**: a #183 mapeia as rotas | mantida, mas esta change toca o painel, contra "backend antes da tela" |
| o que a #183 valida | em `Development`, onde o cadastro está aberto | o mesmo | não tem o que validar antes de mapear | — |
| código morto | não: as rotas são exercitadas em todos os testes | não | **sim**: rotas sem mapeamento (convenção 25) | não |

**Recomendada: (A).** `Identity:TwoFactor:EnrollmentEnabled`, falsa por padrão (`appsettings.json`), verdadeira em `appsettings.Development.json` e nas fixtures de teste:

- **fechado, as rotas de cadastro** (`setup`, `confirm`) respondem `403` "O cadastro da verificação em 2 etapas ainda não está disponível.". O `GET /account/two-factor` diz `cadastroDisponivel: false`, para a #183 poder mostrar o estado sem adivinhar;
- **a verificação no login, a remoção, a regeneração dos códigos e a redefinição pelo admin nunca são fechadas.** Se alguém tiver a verificação ligada (em desenvolvimento, ou por uma configuração aberta antes da hora), ela vale e pode ser desfeita;
- **a opção existe para um cenário real e com prazo** (convenção 2): a transição entre esta change e a #183. **Quem a remove é a #220** (P1): com a obrigatoriedade, o cadastro tem de estar aberto para todos, e a opção perde o sentido. Até lá, o dono pode abri-la em produção no deploy da #183 só pela variável, sem código;
- **`docs/deployment.md`** diz: não abrir antes de o painel ter a tela do código (#183).
- **`docs/development.md`** diz (revisão do dono, 01/11/2026): em desenvolvimento o cadastro está aberto, e quem cadastrar o TOTP pela API não entra mais pelo painel até a #183, porque o painel de hoje não tem o passo do código. A saída é remover a verificação com um código (`POST /account/two-factor/remove`, com o token que ainda se tem) ou pedir a outro usuário a redefinição (`POST /users/{id}/two-factor/reset`).

**Alternativa descartada: (B).** Um segredo cadastrado e não pedido é pior que nenhum: a pessoa acredita estar protegida. **(C)** faz a #183 levar backend. **(D)** não resolve, só explica, e põe tela nesta change.

### D2. (b) O segundo passo no `POST /auth/session`: o mesmo cookie, com o passo pendente

```
 POST /auth/session {email, senha}
   │  guards → bloqueio → senha → estado (como hoje)
   ├── sem 2 etapas ─────────▶ zera contagem, último acesso, cookie COMPLETO ─▶ 204
   └── com 2 etapas ─────────▶ NÃO zera contagem, cookie PENDENTE ───────────▶ 200 {"segundoPasso":"totp"}

 POST /auth/session/second-factor {codigo}
   │  guards (JSON, Origin) → cookie pendente válido? → usuário ativo, stamp igual, 2 etapas ligadas?
   │     não ─────────────────────────────────────────────────────────────────▶ 401 "A entrada expirou…"
   │  bloqueio do e-mail? ────────────────────────────────────────────────────▶ 423 + Retry-After
   │  código TOTP (D3) ou de recuperação (D6)
   │     errado ──▶ falha contada ─────────────────────────────────────────────▶ 401 "Código inválido."
   └──  certo ───▶ zera contagem, último acesso, cookie COMPLETO (novos 5 min) ─▶ 204

 GET /connect/authorize
   cookie completo, ou pendente de usuário SEM 2 etapas ligadas ─▶ código
   cookie pendente de usuário COM 2 etapas, ou cookie completo emitido antes de ligar ─▶ como sem sessão
```

- **O estado intermediário é o próprio `buteco-sessao`** (P3), como o corpo da #218 pede ("O cookie `buteco-sessao` guarda o passo pendente") e como o `IssuerSession` já anuncia ("e mais tarde para o passo das 2 etapas (#218)"). O cookie completo ganha o claim `buteco_2fa` = `ok`; o pendente não o tem. A regra do authorize é **pelo estado atual do usuário**: com a verificação ligada, exige o claim; sem ela, aceita os dois. Assim:
  - o pendente nunca vira código;
  - um cookie completo de quem ligou a verificação nos últimos 5 minutos, emitido sem código, também não;
  - os cookies de antes do deploy (sem o claim, até 5 min) continuam valendo para quem não tem a verificação, que é todo mundo em produção (D1).
- **Duração:** o pendente vive os mesmos 5 minutos do cookie (`IssuerSession.Lifetime`), e o completo é gravado de novo com 5 minutos a partir do código. O cookie já é sem renovação. Um tempo menor para o pendente foi considerado e não ajuda: o bloqueio por e-mail (D7) é o que limita as tentativas, e não a janela do cookie.
- **Por que `200` com corpo, e não `202` nem `401`:** a senha está certa, e a resposta abre um passo. `401` misturaria com "senha errada" e contaria como falha na tela. O `200` com `{ "segundoPasso": "totp" }` deixa espaço para outro fator sem mudar o status (P2).
- **Sem passo pendente válido**, a rota do código responde `401` "A entrada expirou. Informe e-mail e senha de novo.", sem contar falha: não há e-mail conhecido a quem atribuir. Isso cobre o cookie vencido, o stamp trocado por uma redefinição no meio (D9) e a verificação removida no meio.
- **O authorize com só o pendente** faz o que já faz sem sessão: `302 /entrar?retorno=…`, ou `login_required` com `prompt=none`. O cookie pendente fica até vencer ou ser sobrescrito, e não vale nada sozinho. Como o painel começa pelo `/entrar` e só navega ao authorize depois do `204` (#217 D1, P1), esse caminho só aparece num link direto.
- **CSRF e limite:** a rota do código passa pelos mesmos guardas (só JSON, `Origin`) e pelo mesmo limite global do `POST /auth/session` (`LoginRateLimiting.Marker`). É anônima, classificada como `AuthEntryPoint`.
- **O desativado com a senha certa** continua recebendo `403` antes de qualquer passo.

### D3. (c) Parâmetros do TOTP

| parâmetro | valor | por quê |
|---|---|---|
| algoritmo | **HMAC-SHA1** | o padrão do RFC 6238 e o único que todo app autenticador implementa. A colisão de SHA-1 não afeta o HMAC |
| dígitos | **6** | o protótipo ("código de 6 dígitos") e o padrão dos apps |
| passo | **30 s**, T0 = 0 | §5.2 |
| segredo | **20 bytes** de `RandomNumberGenerator` | §5.1: o tamanho da saída do HMAC-SHA1 |
| janela | **passos T−1, T e T+1** (P9) | §5.2 recomenda no máximo um passo de atraso de rede; o +1 cobre o relógio do celular adiantado. É o `RfcSpecifiedNetworkDelay` do `Otp.NET`. A Identity da Microsoft aceita ±2, para outro uso (Context) |
| reuso | **só passa um passo maior que o último aceito**, gravado em `users.two_factor_last_step` | §5.2 ("MUST NOT accept the second attempt"). A regra é monotônica: depois de aceitar T, nem o T−1 passa, mesmo dentro da janela |
| entrada | dígitos, com espaços ignorados | o placeholder do protótipo é `000 000` |
| relógio | `TimeProvider` | os testes usam o `FakeTimeProvider`, como o bloqueio |

**O relógio do servidor** (revisão do dono, 01/11/2026). A janela de um passo para cada lado tolera até 30 s de desvio entre o relógio do host e o do celular. Acima disso, os códigos deixam de bater para todos os usuários ao mesmo tempo, e o sintoma parece "código errado". O `docs/deployment.md` passa a exigir NTP ativo no host do stack (os contêineres usam o relógio dele), com o comando de conferência (`timedatectl`, "System clock synchronized: yes").

**O reuso é atômico.** Aceitar o código é um `UPDATE users SET two_factor_last_step = @passo WHERE id = @id AND (two_factor_last_step IS NULL OR two_factor_last_step < @passo)`. Uma linha afetada é aceite; zero é recusa. Duas submissões simultâneas do mesmo código aceitam uma só, sem ler e depois escrever. A confirmação do cadastro grava o passo também, então o código usado para confirmar não serve para entrar.

### D4. (d) Implementação própria, sobre o BCL

| | `Otp.NET` 1.4.1 | própria |
|---|---|---|
| o que entra | o pacote inteiro: HOTP, SHA-256/512, correção de relógio, decodificação de base32, `OtpUri` | o que se usa: HMAC-SHA1 do contador big-endian, truncagem dinâmica, módulo 10⁶, comparação em tempo constante, codificação em base32 sem padding, o URI |
| tamanho | 0 linhas nossas; ~1.460 no pacote | ~80 linhas (`HMACSHA1.HashData`, `BinaryPrimitives`, `CryptographicOperations.FixedTimeEquals`) |
| manutenção | um mantenedor; uma versão em 20 meses | nossa |
| relógio | `DateTime` | `TimeProvider`, como o resto |
| guarda | os vetores do Apêndice B rodariam contra o pacote | os mesmos vetores rodam contra o nosso |

**Escolhida: própria.** O algoritmo é pequeno, está inteiro no RFC com vetores de teste, e a casa já faz criptografia crua do BCL quando a peça é pequena (`OperatorPasswordHasher`, os dois AES-GCM). A revogação de 24/10/2026 ("biblioteca mantida vence implementação própria") falava de emissão OIDC, rotação, JWKS e validação de `aud`/`iss`/`exp`, onde o defeito sutil está em dezenas de regras. Aqui, os seis vetores do Apêndice B fixam a única conta que existe, e a parte delicada (reuso, janela, força bruta) seria nossa de qualquer jeito, porque o pacote não grava passo nem conta falha.

**Lido da fonte do pacote e aproveitado:** a ordem da janela (atual primeiro), a comparação em tempo constante e o retorno do passo que bateu.

### D5. (e) O segredo em repouso: chave nova, AAD com o id do usuário

- **Chave nova, `Identity:TwoFactorEncryptionKey`** (`Identity__TwoFactorEncryptionKey`), AES-256 em base64. É domínio próprio (convenção 3: "chave por domínio"), distinto do material das chaves do emissor e das credenciais de MCP. Vazar uma não abre as outras.
- **O id do usuário como dado associado (AAD) do GCM.** Copiar o segredo cifrado de uma linha para outra faz a decifração falhar. Os outros dois cifradores não usam AAD; aqui o dono do segredo é uma linha identificada, e o custo é um parâmetro.
- **Formato** `nonce (12) || ciphertext || tag (16)`, em `bytea`, o mesmo das chaves do emissor.
- **Checagem de boot** (convenção 8), na `ValidateIdentityIssuerConfiguration`, sem banco: presente, base64 válido, 32 bytes, **e diferente de `Identity:SigningKeyEncryptionKey` e de `Mcp:CredentialEncryptionKey`**. A igualdade é a regra "distinta das outras" escrita no `appsettings.json` desde a #179, até hoje só em prosa. **Obrigatória sempre**, mesmo com o cadastro fechado (P10): é uma variável a mais no `.env.prod`, e evita um segundo ramo "decifra só se configurada".
- **Se a chave mudar:** o segredo não decifra (`CryptographicException`). O boot não percebe, porque não lê o banco. Na verificação:
  - o código do app responde `500` "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador.", **sem contar falha**, com log `Error` e `EventId` próprio, sem o segredo (convenção 4: a decifração fica dentro do `try`);
  - **os códigos de recuperação continuam valendo**, porque são hash, e não cifra (D6). Eles são a saída da pessoa;
  - a redefinição pelo admin apaga o segredo ilegível, e a pessoa cadastra de novo;
  - `503` foi descartado: o painel o trata como passageiro e repete (#217 D4), e a falha não é passageira.
- **Terceiro consumidor de AES-GCM no `apps/api`** (P4). A convenção 2 manda extrair no terceiro, e o próprio `IdentityKeyMaterialCipher` deixou isso escrito. **Recomendação: extrair** um `AesGcmEnvelope` interno ao `apps/api` (`Seal`/`Open` sobre bytes, AAD opcional, e `ParseKey(nomeDaConfiguração, valor)` com as mensagens de hoje), e os três cifradores passam a usá-lo, **sem mudar o formato gravado**. **Alternativa:** a terceira cópia, com registro; mais simples nesta change, e deixa a regra da convenção 2 escrita e não cumprida. **Aceita a extração (P4).**
- **O que prova que a extração não mudou nada** (revisão do dono, 01/11/2026):
  - **um vetor fixo** cifrado pelo código de hoje (chave, nonce e texto conhecidos) é aberto pelo novo, nos dois cifradores existentes;
  - **as suítes existentes passam sem nenhuma alteração nos testes:** `apps/api/tests/Buteco.Api.Tests/AesGcmMcpCredentialCipherTests.cs` (6 casos) e `apps/api/tests/Buteco.Api.Tests/Identity/IdentityKeyMaterialCipherTests.cs` (6 casos), e as que usam o cifrador das chaves do emissor por tabela (`IdentityKeyPersistenceTests`, `IdentityKeyFactoryTests`, `IdentityKeyRingConcurrencyTests`, `IdentityKeyRingSigningOrderTests`). O fechamento compara por nome contra o `.trx` da baseline, e o `git diff` desses arquivos sai vazio;
  - **a cópia do `apps/workers` continua lendo o que o `apps/api` grava.** Ela é `apps/workers/src/Buteco.Workers/Mcp/Security/AesGcmMcpCredentialCipher.cs`: lê base64 de `nonce (12) || ciphertext || tag (16)` (l. 70–93), sem AAD. O envelope grava o cifrador de MCP sem AAD, e o formato não muda. **Já existe teste de acordo com os artefatos reais dos dois lados:** `tests/CrossAppTaskStoreCompatibility.Tests/McpServerCrossAppCompatibilityTests.cs` grava pela cifra real do `apps/api` e lê pelo resolvedor real do `apps/workers` (convenções 11 e 12). Ele passa a rodar na baseline e no fechamento, e não muda. O código do `apps/workers` não muda.

### D6. (f) Códigos de recuperação

| | decisão | por quê |
|---|---|---|
| quantidade | **10** | #180 D4 |
| formato | **16 caracteres do alfabeto de Crockford** (sem `I`, `L`, `O`, `U`), mostrados como `xxxx-xxxx-xxxx-xxxx` | 80 bits. Com hash rápido, 50 bits (o formato de 10 caracteres) cairiam por força bruta fora do sistema, com uma cópia do banco, em horas numa GPU; 80 bits não (P11) |
| entrada | sem diferença de caixa, hífens e espaços ignorados, e as trocas de Crockford (`I`/`L` → `1`, `O` → `0`) | é digitado por quem perdeu o celular |
| hash | **SHA-256** do código normalizado, em `bytea` | 80 bits aleatórios dispensam o custo do Argon2id. Argon2id custaria ~19 MiB por tentativa, e a verificação não sabe qual dos 10 conferir sem um identificador |
| guarda | tabela `user_recovery_codes` (`id`, `user_id`, `code_hash`, `used_at`), única em (`user_id`, `code_hash`) | consulta por hash, sem laço |
| uso único | `UPDATE … SET used_at = now WHERE user_id = @u AND code_hash = @h AND used_at IS NULL`; uma linha afetada é aceite | atômico, como o passo do TOTP |
| onde entra | **no mesmo campo "Código"** da rota do segundo passo, distinguido pela forma (6 dígitos ou 16 caracteres) | o protótipo tem um campo só e "Sem acesso ao app? Fale com o administrador." (registro para a #183 abaixo) |
| quando nascem | na confirmação do cadastro, devolvidos uma vez | #180 D4 |
| regeneração | `POST /account/two-factor/recovery-codes` com um código válido (do app ou de recuperação); apaga os 10 e devolve 10 novos, uma vez | quem regenera prova a posse do fator |
| quantos restam | no `GET /account/two-factor` | a #183 avisa quando acabam |

- **Usar um código de recuperação não desliga a verificação.** O TOTP continua valendo.
- **A chave de cifra não toca os códigos** (D5), por isso eles sobrevivem à troca dela.
- **Contrariar o protótipo** (convenção 17): o protótipo não tem o código de recuperação no estado `mfa`. O código entra no mesmo campo, e o texto do rodapé é decisão da #183. **Registro proposto:** comentário na #183, depois do ok do dono.

### D7. (g) Força bruta no código

- **O mesmo bloqueio por e-mail da senha** (`identity_login_throttle`, a linha do e-mail normalizado do usuário). Falha de código conta como falha de login, e o `423` vale para os dois passos, sem verificar nada.
- **A senha certa com o código pendente não zera a contagem.** É a regra que segura o resto: se zerasse, quem tem a senha faria senha → 4 códigos → senha → 4 códigos, sem fim. Só o sucesso completo zera.
- **A conta, com a janela de 3 passos:** cada tentativa acerta com probabilidade 3 × 10⁻⁶. As 5 primeiras falhas bloqueiam 5 min, e depois cada falha bloqueia de 10 a 60 min. São ~30 tentativas por dia, ~1 × 10⁻⁴ de chance por dia, para quem já tem a senha, e a vítima vê a conta pausar. A conta por e-mail e o limite global já existem; nada novo.
- **O que a pessoa vê:** `401` "Código inválido." até a quinta falha; depois, o mesmo `423` do login ("acesso pausado"), com o `Retry-After`. A #217 já trata o `423` no `/entrar`, e a #183 trata no passo do código.
- **As rotas autenticadas que pedem código** (remoção, regeneração) contam no mesmo bloqueio. Um access token roubado (15 min) não testa códigos sem limite.

### D8. (h) Cadastro

**Rotas da própria pessoa**, sob `/account/two-factor`, autenticadas pelo token do emissor (P12):

| rota | corpo | resposta |
|---|---|---|
| `GET /account/two-factor` | — | `{ ativa, ativadaEm, codigosRestantes, cadastroDisponivel }` |
| `POST /account/two-factor/setup` | `{ senha }` (P6) | `200 { segredo, otpauthUri }`; `403` cadastro fechado (D1); `409` já ativa; `401` senha errada, contada no bloqueio |
| `POST /account/two-factor/confirm` | `{ codigo }` | `200 { codigosRecuperacao: [10] }`; `400` sem cadastro pendente ou vencido; `401` código errado, contado |
| `POST /account/two-factor/recovery-codes` | `{ codigo }` | `200 { codigosRecuperacao: [10] }`; `409` não ativa |
| `POST /account/two-factor/remove` | `{ codigo }` | `204`; `409` não ativa |

- **Quem chama** é o usuário do `sub` do access token do emissor, ativo. Token HMAC (sem `sub`), `sub` inexistente ou desativado (o access token vale até 15 min) → `403`.
- **Pendente:** o `setup` grava o segredo cifrado em `two_factor_pending_secret`, com `two_factor_pending_at`. Vale **10 minutos**, pela regra do tempo e não por limpeza, como o bloqueio. Um `setup` novo substitui o anterior. Só o `confirm` com um código válido do segredo pendente o move para `two_factor_secret`, grava `two_factor_enabled_at` e o passo, e gera os códigos.
- **A senha no `setup`** (P6): com um access token roubado, quem o tem cadastraria o próprio app na conta da vítima, e ela ficaria sem entrar quando a obrigatoriedade chegar. Pedir a senha fecha isso, ao custo de um Argon2id por cadastro. A remoção e a regeneração pedem o código, que já prova a posse.
- **O segredo devolvido uma vez** é a exceção declarada ao "write-only" da convenção 3, como a credencial de aplicação do protótipo ("exibida uma vez"): sem ele, não há cadastro. Ele não volta em nenhuma outra resposta, nem no `GET`.
- **O URI:** `otpauth://totp/Buteco%20Agentes:{email}?secret={base32}&issuer=Buteco%20Agentes&algorithm=SHA1&digits=6&period=30`. O app autenticador mostra "Buteco Agentes" e o e-mail (P7). O e-mail é o do momento do cadastro; trocar o e-mail (#219) não muda o segredo, só o rótulo antigo no app.
- **O QR é do painel** (#183), a partir do `otpauthUri`, como a #183 já tem em aberto ("como o segredo TOTP é apresentado"). Gerar no `apps/api` exigiria um pacote de QR e devolveria uma imagem, que é tela.
- **Ligar a verificação não troca o stamp** nem encerra as outras sessões: elas já eram da pessoa. Remover também não. Só a redefinição pelo admin troca (D9).

### D9. (i) Redefinição pelo admin: a rota é desta change; a lista e o detalhe são da #219

**Os corpos:** a #218 lista como entrega "Redefinir verificação pelo admin (protótipo, Editar usuário): apaga o segredo e os códigos e troca o `security_stamp`". A #219 lista, em Editar usuário, "redefinir as 2 etapas (#218)", e depende da #218. O parêntese aponta para cá. A tabela da divisão da #180 contava a operação entre as 11 da #219: ela fica com 10, e o registro vai como comentário na #219.

- **Aqui:** `POST /users/{id}/two-factor/reset`, autenticada, com o acesso do operador (M1, sem papéis):
  - apaga `two_factor_secret`, o pendente, `two_factor_enabled_at`, `two_factor_last_step` e os códigos de recuperação, e troca o `security_stamp`, numa transação;
  - `204`; `404` usuário inexistente; `409` verificação não configurada (o protótipo desabilita o botão nesse estado, e um `204` trocaria o stamp à toa);
  - **`403` para o próprio usuário:** ele remove a dele com um código (D8). Sem isso, um access token roubado tiraria a verificação da própria conta sem código.
  - **quem chama é o usuário real do `sub` do token do emissor** (`CurrentUser`, como as rotas de `/account`; revisão do dono, 01/11/2026). O HMAC de operador não traz `sub` e recebe `403`, como o `sub` inexistente e o usuário desativado. Sem isso, a recusa do próprio usuário não teria como ser conferida: um HMAC não diz quem é a pessoa.
- **O efeito da troca do stamp:** o cookie em voo é recusado no authorize, e a renovação seguinte de toda sessão da pessoa recebe `invalid_grant` (#180 D4). O access token já emitido vale até 15 min, como no desativar.
- **Na #219:** o "2 etapas" da lista e do detalhe lê `two_factor_enabled_at` (e o que mais a #219 decidir mostrar). O grupo `/users` nasce aqui com uma rota, e a #219 acrescenta as dela nele.

### D10. (j) Sem `amr` no token

O `amr` (RFC 8176: `pwd`, `otp`, `mfa`) diria ao cliente como a pessoa entrou. **Ninguém o lê na M1:**

- a **#220** obriga o cadastro no `POST /auth/session`, antes de qualquer token, e não precisa do claim;
- os **três apps** decidem acesso pela tabela de clientes, nunca por fator;
- a **M3** (Google) decide no próprio login federado, e o protótipo diz "Quando entra pelo Google, a verificação é feita pelo próprio Google";
- as **aplicações externas** (#186) recebem `client_credentials`, sem pessoa.

Declarar o claim sem consumidor é o que a convenção 25 manda não fazer. O fator fica só no cookie (`buteco_2fa`), onde o authorize o lê (D2). **Gatilho para voltar:** o primeiro consumidor (uma política "só com 2 etapas" num app, ou a #186 pedindo `acr`/`amr`).

### D11. O modelo de dados

**`users`**, colunas novas, todas nulas:

| coluna | conteúdo |
|---|---|
| `two_factor_secret` | `bytea`, o segredo cifrado (D5) |
| `two_factor_enabled_at` | `timestamptz`; nulo = desligada |
| `two_factor_last_step` | `bigint`, o último passo aceito (D3) |
| `two_factor_pending_secret` | `bytea`, o segredo do cadastro em curso |
| `two_factor_pending_at` | `timestamptz` |

**`user_recovery_codes`** (D6): `id` `uuid`, `user_id` com FK para `users` e `ON DELETE CASCADE` (usuário não é excluído, mas a FK não deixa órfão), `code_hash` `bytea`, `used_at` `timestamptz`; índice único (`user_id`, `code_hash`).

- **Colunas em `users`, e não uma tabela `user_two_factor`:** é 1 para 1, e o D4 da #180 já disse "as colunas das 2 etapas entram com a #218". Os códigos são 1 para 10, e vão para tabela (convenção 2, jsonb contra tabela: aqui há consulta por hash).
- **Migration `AddTwoFactor`**, só aditiva. O `apps/workers` não mapeia nada disso.

### D12. (k) Testes

Os guardas de cada linha são verificados contra o defeito (convenção 15): o defeito entre parênteses é reintroduzido, o teste reprova **no componente que a correção toca**, e a árvore volta.

**`apps/api`, unidade:**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `TotpTests` | os seis vetores SHA-1 do Apêndice B, em 6 dígitos (Context), com T0 = 0 e X = 30; o zero à esquerda (`005924`, `081804`) | contador em little-endian; truncagem sem `& 0x7f`; `ToString()` sem preencher zeros |
| `TotpWindowTests` | aceita T−1, T e T+1 e devolve o passo que bateu; recusa T−2 e T+2 | janela de ±2 |
| `Base32Tests` | os vetores do RFC 4648 §10 (`f` → `MY`, …, `foobar` → `MZXW6YTBOI`), sem padding | — |
| `OtpAuthUriTests` | issuer e e-mail codificados no rótulo e no `issuer`; os quatro parâmetros | e-mail sem codificar (`+` no e-mail) |
| `RecoveryCodeTests` | 16 caracteres de Crockford; a normalização (caixa, hífens, espaços, `I`/`L`/`O`); a mesma entrada dá o mesmo hash | — |
| `TwoFactorSecretCipherTests` | ida e volta; o segredo de um usuário não abre com o id de outro; chave errada lança | AAD retirado (o caso do outro usuário) |
| `AesGcmEnvelopeTests` | um vetor cifrado pelo código de hoje abre nos dois cifradores existentes; com AAD, abre só com o mesmo AAD | formato com tag antes do texto |
| `AesGcmMcpCredentialCipherTests`, `IdentityKeyMaterialCipherTests` e os de `Identity/IdentityKey*` (existentes) | passam **sem nenhuma alteração nos arquivos de teste** depois da extração (D5) | — |

**`apps/api`, integração (Testcontainers, `TestServer`):**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `TwoFactorLoginTests` | com a verificação: senha → `200 {segundoPasso}` e cookie sem `buteco_2fa`; authorize com ele → `302 /entrar`; código → `204`; authorize → código; token → access token. Sem a verificação: `204` como antes | authorize que não confere o claim (o pendente vira código) |
| | cookie completo emitido antes de ligar a verificação → authorize como sem sessão | regra pelo cookie, e não pelo estado atual do usuário |
| | código de recuperação → `204`; o mesmo de novo → `401` | `used_at` sem a condição `IS NULL` |
| | o mesmo código TOTP duas vezes → a segunda `401`; um código de T−1 depois de T aceito → `401` | conferência sem `two_factor_last_step` |
| | duas submissões simultâneas do mesmo código (`Task.WhenAll`) → um `204` e um `401` | ler e depois escrever, sem o `UPDATE` condicional (se a corrida não reproduzir em 20 rodadas, o registro diz, e o guarda fica só no caso sequencial) |
| | sem cookie, cookie vencido, ou stamp trocado no meio → `401` "A entrada expirou", sem contar falha | — |
| `TwoFactorThrottleTests` | senha certa e código errado alternados: a quinta falha bloqueia; durante o bloqueio, as duas rotas respondem `423` sem verificar | senha certa zerando a contagem com o código pendente |
| `TwoFactorEnrollmentTests` | cadastro fechado → `403` no `setup` e no `confirm`, e o `GET` diz `cadastroDisponivel: false`; aberto: `setup` sem senha → `401`; `setup` → segredo e URI; `confirm` com código errado → `401`; certo → ativa e 10 códigos; `setup` de novo → `409`; `confirm` depois de 10 min → `400`; o código do `confirm` não serve para entrar | `confirm` que liga sem conferir o código; pendente sem prazo |
| | regenerar com código → 10 novos, os antigos recusados; remover com código → desligada, login volta ao `204`; remover sem código → `400` | — |
| `TwoFactorAdminResetTests` | redefinir outro → `204`, segredo e códigos apagados, a renovação dele → `invalid_grant`, o login dele volta ao `204`; o próprio → `403`; não configurada → `409`; inexistente → `404` | troca do stamp retirada (a renovação passa) |
| `TwoFactorKeyChangeTests` | segredo cifrado com outra chave → código do app `500`, log `Error` com o `EventId`, sem contar falha; código de recuperação → `204` | decifração fora do `try` (a exceção escapa como `500` genérico, sem o log) |
| `TwoFactorCallerTests` | HMAC `operator` em `/account/two-factor` → `403`; HMAC `operator` em `POST /users/{id}/two-factor/reset` → `403`, e o alvo continua com a verificação e o mesmo stamp; usuário desativado com access token ainda válido → `403` nas duas | a rota de redefinição aceitando o HMAC (o caso do HMAC na redefinição) |
| `IdentityIssuerStartupValidationTests` (estendido) | sem `Identity:TwoFactorEncryptionKey`, malformada, ou igual a uma das outras duas → boot falha, pela composição real | chamada retirada do `Program.cs` |
| `RouteAuthenticationTests` (existente) | a rota do código classificada como `AuthEntryPoint`; a checagem de boot das rotas anônimas continua passando | — |

**`apps/frontend`:** nenhum código muda. O `deploy/nginx.conf` ganha `account|users`, conferido por `curl` no fechamento (sem teste automatizado da lista, #57), como a #180 fez no D3.5. O `nginxHeaders.test.ts` continua passando.

**`tests/`:** sem mudança. Os dois projetos de acordo do login rodam no fechamento, porque o `POST /auth/session` muda (para quem não tem a verificação, não deveria). O `CrossAppTaskStoreCompatibility.Tests` roda também, pela extração da cifra de MCP (D5).

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Users/
│   ├── User.cs                                   + colunas das 2 etapas; EnableTwoFactor, ResetTwoFactor (troca o stamp)
│   ├── TwoFactor/                                NOVO
│   │   ├── Totp.cs                               RFC 6238: código, janela, passo que bateu (D3, D4)
│   │   ├── Base32.cs                             só codificação, sem padding
│   │   ├── OtpAuthUri.cs                         o URI do app autenticador (D8)
│   │   ├── TwoFactorSecretCipher.cs              AES-GCM com AAD = id do usuário (D5)
│   │   ├── TwoFactorOptions.cs                   Identity:TwoFactor:EnrollmentEnabled (D1)
│   │   ├── RecoveryCode.cs                       geração, normalização, hash (D6)
│   │   ├── UserRecoveryCode.cs                   entidade
│   │   ├── TwoFactorVerifier.cs                  TOTP ou recuperação, uso único atômico, chave errada (D3, D5, D6)
│   │   ├── AccountTwoFactorEndpoints.cs          /account/two-factor (D8)
│   │   └── UserTwoFactorResetEndpoint.cs         /users/{id}/two-factor/reset (D9)
│   ├── CurrentUser.cs                            NOVO: o usuário ativo do sub do token, ou 403
│   └── Login/
│       ├── SessionEndpoints.cs                   + 200 {segundoPasso}; + POST /auth/session/second-factor (D2)
│       └── LoginThrottleStore.cs                 sem mudança: as falhas do código usam o mesmo RecordFailure
├── Identity/
│   ├── Flow/IssuerSession.cs                     + claim buteco_2fa
│   ├── Flow/AuthorizeEndpoint.cs                 + a regra do passo pelo estado atual do usuário (D2)
│   ├── IdentityIssuerOptions.cs                  + TwoFactorEncryptionKey
│   ├── IdentityIssuerStartupValidation.cs        + a chave nova, distinta das outras duas (D5)
│   └── Keys/IdentityKeyMaterialCipher.cs         sobre o AesGcmEnvelope
├── Infrastructure/
│   ├── AesGcmEnvelope.cs                         NOVO (D5)
│   ├── AppDbContext.cs                           + DbSet<UserRecoveryCode>
│   └── Migrations/<data>_AddTwoFactor.cs         NOVO
├── McpServers/Security/AesGcmMcpCredentialCipher.cs  sobre o AesGcmEnvelope
├── Program.cs                                    rotas novas
├── appsettings.json                              Identity:TwoFactorEncryptionKey ""; TwoFactor:EnrollmentEnabled false
└── appsettings.Development.json                  chave de dev; EnrollmentEnabled true

apps/api/tests/Buteco.Api.Tests/
├── Users/TwoFactor/                              NOVO: os testes do D12
├── Identity/IdentityIssuerStartupValidationTests.cs   estendido
└── Support/
    ├── TestAuthentication.cs                     + a chave nova e o cadastro aberto nos overrides
    └── TwoFactorTestClient.cs                    NOVO: cadastro e código pelo FakeTimeProvider

apps/frontend/deploy/nginx.conf                   + account|users no bloco do apps/api
docker-compose.prod.yml                           + Identity__TwoFactorEncryptionKey: ${IDENTITY_TWO_FACTOR_ENCRYPTION_KEY:?}
.env.prod.example, .env.example                   + a variável
docs/configuration.md, docs/deployment.md (NTP, cadastro fechado), docs/development.md (saída do TOTP em dev), SECURITY.md, 01, 02, CHANGELOG.md
```

**Nada vai para `libs/`.** O `AesGcmEnvelope` fica dentro do `apps/api`: os outros apps têm as próprias cópias e nenhum precisa desta.

## Tamanho e divisão

**Âncoras medidas** (convenção 18), só código, sem `openspec/` nem `.md`, por `git diff --numstat`, sem migrations:

| PR | issue | produção | testes | projetado → entregue |
|---|---|---|---|---|
| #224 | #180 (recortada) | +2.036 / −141, 45 arquivos | +2.984 / −237, 44 arquivos | +57 % / +24 % |
| #228 | #217 | **+1.389 / −381, 49 arquivos** (`86abb5d^1..649e582`, medido hoje) | **+2.264 / −464, 48 arquivos** | 1.220 / 1.880 → +14 % / +20 % |

**Projeção por componente**, sobre este design. É rascunho (convenção 18): erra para baixo por construção. As duas decisões que mexiam na conta, a extração da cifra (P4) e a senha no cadastro (P6), foram aceitas e já estão nela.

| componente | produção | testes |
|---|---|---|
| TOTP, base32, URI, código de recuperação (D3, D4, D6) | ~150 | ~250 |
| cifra, chave, checagem de boot, extração (D5) | ~120 | ~200 |
| entidade, colunas, tabela, `CurrentUser` (D11) | ~110 | — |
| segundo passo: rota, cookie, authorize, bloqueio (D2, D7) | ~180 | ~450 |
| cadastro: 5 rotas (D8), pela régua de ~80 por operação | ~400 | ~500 |
| redefinição pelo admin (D9) | ~70 | ~150 |
| opção do cadastro, configuração, nginx, compose (D1) | ~40 | ~40 |
| apoio de teste | — | ~150 |
| **total** | **~1.070** | **~1.740** |

Com o erro da #180 (+57 %), ~1.700 / ~2.200. **Cabe numa change**, na faixa da #217 e abaixo da #180. Não proponho divisão.

**Divisão possível, registrada e não recomendada**, na régua da #180: (1) TOTP, segundo passo e redefinição, com o cadastro só por semente de teste; (2) as rotas de cadastro. A (1) não teria como ligar a verificação fora dos testes, então o segundo passo entraria em `main` sem caminho de uso, que é o que a convenção 25 descreve.

## Risks / Trade-offs

- **[Alguém abre o cadastro em produção antes da #183 e fica sem entrar]** → a opção é falsa por padrão; o `docs/deployment.md` diz para não abrir; a remoção (com código) e a redefinição pelo admin nunca fecham. Coberto por `TwoFactorEnrollmentTests` (fechado) e `TwoFactorAdminResetTests`. Numa instalação com um usuário só e a verificação ligada sem a tela, não há saída pelo painel: é o motivo do padrão falso.
- **[Troca da chave de cifra torna os segredos ilegíveis]** → o boot não vê, porque não lê o banco. O log `Error` com `EventId` aparece na primeira tentativa; os códigos de recuperação continuam valendo; o admin redefine. Não há recifragem na M1, e o `docs/deployment.md` diz que a chave não se troca sem redefinir todos. Coberto por `TwoFactorKeyChangeTests`.
- **[Força bruta no código com a senha conhecida]** → o bloqueio por e-mail, sem zerar na senha certa (D7). Coberto por `TwoFactorThrottleTests`.
- **[O bloqueio por e-mail agora também pausa quem erra o código]** → é o mesmo preço aceito na #180 (um terceiro pode pausar a conta de outra pessoa até 60 min), e agora quem já tem a senha também pode. Aceito, com o teto de 60 min.
- **[Access token roubado cadastra o app do atacante na conta]** → a senha no `setup` (P6). Coberto por `TwoFactorEnrollmentTests`.
- **[Access token roubado tira a verificação]** → a remoção pede código; a redefinição recusa o próprio usuário (D9). Coberto por `TwoFactorAdminResetTests` e `TwoFactorEnrollmentTests`. **Não coberto, e aceito na M1:** sem papéis, qualquer usuário redefine a verificação de outro, como desativa (#219). É o "todo usuário é Admin em poder" da #180 D4; a #184 restringe.
- **[Reuso do código em corrida]** → o `UPDATE` condicional (D3). O guarda da corrida depende de ela reproduzir; o caso sequencial cobre a regra.
- **[Prefixo novo esquecido no nginx]** → responderia `200` com HTML. A lista entra nesta change e é conferida por `curl` no fechamento.
- **[Rótulo antigo no app autenticador depois de trocar o e-mail]** → só o rótulo; o segredo continua. Declarado.
- **[O protótipo não tem o código de recuperação no passo do código]** → mesmo campo, registro na #183 (D6).

## Migration Plan

1. **Antes:**
   - pôr `IDENTITY_TWO_FACTOR_ENCRYPTION_KEY` no `.env.prod` (`openssl rand -base64 32`, distinta das outras duas). Sem ela, o `api` não sobe, por desenho;
   - conferir o NTP do host (`timedatectl`: "System clock synchronized: yes"), antes de qualquer pessoa ligar a verificação (D3).
2. **Deploy:** `migrator` (colunas e tabela, só aditivas), `api`, `frontend` (o nginx com `account|users`). O cadastro continua fechado.
3. **Conferir:** o login de um usuário continua em `204`; `curl` de `/account/two-factor` sem token responde `401` do `apps/api`, e não `200` com HTML; o `GET` com token diz `cadastroDisponivel: false`.
4. **Rollback:** a imagem anterior do `api` e do `frontend`. As colunas e a tabela ficam sem efeito. Ninguém tem a verificação em produção (D1), então ninguém fica preso a ela. A variável nova fica sobrando.

## Decisões do dono (01/11/2026)

Na revisão do proposal e do design, o dono aceitou **P1 a P12 como recomendadas**:

| | pergunta | decisão | onde |
|---|---|---|---|
| P1 | Como a transição evita alguém sem entrar? | cadastro fechado por `Identity:TwoFactor:EnrollmentEnabled` (falsa em produção); a verificação nunca é desligada; a #220 remove a opção, e o dono pode abri-la pela variável no deploy da #183 | D1 |
| P2 | Resposta da senha certa com o código pendente | `200 { "segundoPasso": "totp" }` | D2 |
| P3 | Estado intermediário | o próprio `buteco-sessao`, com o claim `buteco_2fa` no completo; o authorize decide pelo estado atual do usuário | D2 |
| P4 | Terceiro consumidor de AES-GCM | extrair o `AesGcmEnvelope` no `apps/api`, com os três cifradores sobre ele e o formato intacto | D5 |
| P5 | Fronteira da redefinição | a rota é da #218; a #219 lê as colunas, e o comentário na #219 registra (10 operações) | D9 |
| P6 | Pedir a senha no início do cadastro | sim | D8 |
| P7 | Nome no app autenticador | issuer fixo "Buteco Agentes", conta = e-mail | D8 |
| P8 | `amr` no token | não, até o primeiro consumidor | D10 |
| P9 | Janela | T−1, T, T+1, com o último passo gravado | D3 |
| P10 | Chave nova obrigatória mesmo com o cadastro fechado | sim | D5 |
| P11 | Formato do código de recuperação | 16 caracteres de Crockford (80 bits), SHA-256 | D6 |
| P12 | Prefixos das rotas | `/account/two-factor` e `/users/{id}/two-factor/reset`, com `account|users` no nginx; a #219 herda os dois grupos | D8, D9 |

**Ajustes da mesma revisão**, já incorporados:

- a redefinição pelo admin exige o usuário real do `sub` (`CurrentUser`), e o HMAC de operador recebe `403` (D9, D12);
- a extração da cifra é provada também pelas suítes existentes sem alteração e pelo acordo com a cópia do `apps/workers` (D5, D12);
- NTP no host, no `docs/deployment.md` (D3);
- a saída de quem cadastrar o TOTP em desenvolvimento, no `docs/development.md` (D1).

## Registros (convenção 23)

Publicados em 01/11/2026, com o ok do dono:

- **#219** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/219#issuecomment-6565822306)): a redefinição é da #218 (`POST /users/{id}/two-factor/reset`); a lista e o detalhe leem `two_factor_enabled_at`; os grupos `/account` e `/users` já existem.
- **#220** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/220#issuecomment-6565822505)): remover `Identity:TwoFactor:EnrollmentEnabled` junto com a obrigatoriedade.
- **#183** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6565822709)): o contrato do passo do código (`200 {segundoPasso}`, `POST /auth/session/second-factor`, o código de recuperação no mesmo campo, os textos de `401`, `423` e `500`), o `otpauthUri` para o QR, o `cadastroDisponivel`, e a divergência do protótipo (D6).

## Open Questions

Nenhuma.

## Baseline

No worktree limpo `../buteco-agentes-218-baseline`, em `86abb5d`, sem edição nenhuma. O protocolo é o da #217: `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED`, `DOTNET_SYSTEM_NET_DISABLEIPV6` e `TZ=America/Sao_Paulo`. **Antes de cada suíte**, o script `~/.cache/buteco-agents/bin/suite-218.sh` (cópia do `suite-217.sh`) espera `CPU_Speed_Limit = 100` e o load abaixo do limite (6 no frontend, 8 nas outras), acerta o relógio da VM e registra o `podman ps`. As saídas completas, com o relatório por nome (`.trx` e o JSON do Vitest), ficam em `~/.cache/buteco-agents/218-baseline/`.

| suíte | resultado | duração | estado (início → fim) |
|---|---|---|---|
| `apps/api` | **1007/1007 + 1 ignorado** | 237 s (3m31s de teste) | load 5,60, CPU 100, `podman ps` vazio → load 23,97, **CPU 46** |
| `apps/frontend` | **1978/1978**, 154 arquivos | 177 s | load 5,95 (5 min: 12,10), CPU 100, `podman ps` vazio → load 28,86, **CPU 51** |
| `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 53 s | load 7,45, CPU 100, `podman ps` vazio → load 4,83, CPU 100 |
| `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 23 s | load 4,83, CPU 100, `podman ps` vazio → load 5,69, CPU 100 |
| `tests/CrossAppTaskStoreCompatibility.Tests` | **4/4** | 22 s | load 1,72, CPU 100, `podman ps` vazio → load 2,26, CPU 100 |

- **A CPU caiu para 46 e 51 durante o `apps/api` e o `apps/frontend`**, sempre depois de largar em 100; os resultados não mudam por isso, mas essas duas durações não servem de referência. Os três de `tests/` rodaram inteiros em 100.
- **O `CrossAppTaskStoreCompatibility.Tests` entrou depois da revisão do dono** (mesmo worktree, mesmo commit, mesmo script, que ganhou o caso `rt-crossapp`), quando a extração da cifra de MCP (P4) o pôs no escopo (D5).

- **`apps/api` caiu de 1009 para 1007** desde a baseline da #217 (`ca2bd71`), e a diferença foi conferida por nome contra o `.trx` dela (`218-baseline/api.trx.names`): saíram os 4 do `AuthLoginTests`, o `HmacLogin_StillIssuesTheOperatorToken…` e dois renomeados pela #217 (`…WithTokenFromLogin…` e `Login_WithoutToken_StaysAnonymous`); entraram os 2 do `GoneLoginTests`, o `SameRefreshAgainWithinTheLeeway…` e os dois nomes novos. −7 + 5 = −2, todos da #217.
- `apps/inbox`, `apps/connectors` e `apps/workers` não são medidos: o código deles não muda. A leitura da cifra de MCP pelo `apps/workers` é coberta pelo `CrossAppTaskStoreCompatibility.Tests`.
