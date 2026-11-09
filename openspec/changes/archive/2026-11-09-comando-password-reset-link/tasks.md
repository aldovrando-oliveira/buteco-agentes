Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-257.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-184.sh` (com o lock de uma suíte por vez), só com o projeto `api`. A mutação toca só o trecho nomeado, e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação e reprova por "no tests" não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/257-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-257.sh` (só `api`), com o RabbitMQ de pé (`PAUSAR_RABBIT=0`) e o lock de uma suíte por vez: nenhum guarda, build ou suíte em paralelo.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 8, e o `podman ps` do início mostra o `postgres_1` e o `rabbitmq_1`.

**A baseline** está em `~/.cache/buteco-agents/257-baseline/`, em `0ffe651`, com a árvore limpa: `apps/api` 1546/1546 + 1 ignorado, `APROVADA`, CPU mínima 55, e o relatório por nome em `nomes.txt` (1547 linhas). Detalhes no design ("Baseline").

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura; e se a change passar a ter tela;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Casos que saem ou mudam de asserção:** **nenhum.** O requisito "Gerar o link de redefinição de senha" ganha texto e cenários, mas a resposta da rota não muda, e nenhum caso existente sai, muda de nome ou afrouxa. O `EmergencyCommandRunner` ganha parâmetros opcionais, e os casos da #220 que o usam ficam com as mesmas asserções. Um caso que precise sair ou afrouxar é parada.

**Premissas que caíram no apply** (convenção 9; nenhuma muda comportamento ou contrato, todas seguem e ficam registradas):
- o banco de desenvolvimento não tem a migration da #184 (`role`): a 4.4 a aplicou só na cópia descartável;
- o `node_modules` do `apps/frontend` do `main` estava desatualizado (sem o `qrcode.react` da #246): o Vite da 4.4 rodou do worktree, depois de um `npm ci`;
- o `CREATE DATABASE` pelo `template1` recusa por divergência de collation (já visto na #220): a cópia usou o `template0`;
- a primeira mutação do `g2.3b` trocou a mensagem e reprovou pela asserção do texto, não pelo defeito: descartada e refeita (`-r2`).

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-257.sh`, cópia do `guarda-184.sh` só com o projeto `api`. Conferir com uma mutação nula (o script recusa com "defeito não aplicado") e uma real em `apps/api/src` (o arquivo volta com o mesmo sha), e que uma segunda cópia em paralelo recusa com `75`.
  **Feito (09/11/2026):** a mutação nula recusa ("defeito não aplicado"); com o lock tomado por outro processo, recusa com `75` ("recusado: outra suíte ou guarda está rodando"); a real (um comentário no fim de `Users/AccessLinks/AccessLinkUrls.cs`) rodou `AccessLinkUrlsTests` (6/6) e o arquivo voltou com o sha igual (`db774c14…`). Saídas em `~/.cache/buteco-agents/257-guardas/0.1/`.

## 1. A extração (apps/api, D1)

- [x] 1.1 (apps/api) `Users/AccessLinks/PasswordResetLink.cs`: `IssueAsync(userId, panelBase, cancellationToken)` com o token, o hash, o vencimento de 24 h e o `UPDATE` condicionado ao ativo; devolve `Issued(Link, ExpiresAt)` ou `null`. Registrado como `Scoped` no host web.
  **Feito.** Registrado no `AddLocalUsers` (`UserServiceCollectionExtensions.cs`).
- [x] 1.2 (apps/api) `PasswordResetEndpoints.CreateAsync` sobre o `PasswordResetLink`: resolve a base (`PanelBaseUrl.Resolve`) **antes** da escrita e passa a base pronta; `null` → o `409` "Usuário desativado." de hoje. Nada mais muda na rota.
  - Os testes da rota de hoje passam sem mudança de asserção.
  **Feito.** O `TimeProvider` saiu dos parâmetros da rota (vai pelo `PasswordResetLink`, do mesmo contêiner). Os 41 casos de `PasswordResetTests`, `TwoFactorEmergencyResetTests`, `TwoFactorResetParityTests` e dos dois arquivos novos passaram na primeira rodada filtrada, sem mudança de asserção.

## 2. O comando (apps/api, D2 a D5)

- [x] 2.1 (apps/api) `Users/AccessLinks/PasswordResetLinkCommand.cs` e o ramo no `Program.cs`, junto do `two-factor-reset`, antes do `WebApplication.CreateBuilder`:
  - uso (`64`) → a base do painel pela configuração (`appsettings*.json` e ambiente), com `IdentityIssuerState.TryParseIssuer` e `PanelBaseUrl.Resolve`, **antes de montar o host e de abrir conexão** (`78`) → o host mínimo (configuração, log, `AppDbContext`, `TimeProvider`, `PasswordResetLink`), sem Kestrel nem serviços em segundo plano → o usuário pelo e-mail normalizado (`1`) → estado (`2`, `3`) → papel (`4`) → `IssueAsync` (`null` → `2`) → `0`;
  - o `catch` geral sai com `70`, com o tipo do erro e o da causa; o log do host e o do EF ficam de fora, como na #220; não chama `Migrate`;
  - o link sai uma vez, sozinho numa linha do **stdout**; a linha `Warning` (`EventId 1197`, `PasswordResetLinkEmergency`) vai ao **stderr** (`LogToStandardErrorThreshold`), com o resultado, o e-mail, o id, o papel e o vencimento, nunca o token, o link, o hash nem o carimbo.
  **Feito.** A base é lida do `builder.Configuration` do `Host.CreateApplicationBuilder()` antes do `Build()` e de qualquer consulta; o `78` sai antes de registrar o `AppDbContext`. O registro vai ao stderr pelo `LogToStandardErrorThreshold = Trace` do console. O ramo entra no `Program.cs` logo depois do `two-factor-reset`.
- [x] 2.2 (apps/api, testes) `Support/EmergencyCommandRunner.cs`: variáveis de ambiente extras opcionais e o `Result` com `Stdout` e `Stderr` além do `Output` concatenado. Os testes da #220 não mudam.
  **Feito.** `RunAsync(connectionString, environment, args)` a mais; o `Result` passou a `(ExitCode, Stdout, Stderr)` com `Output => Stdout + Stderr`, a mesma concatenação de antes. Os casos da #220 não mudaram.
- [x] 2.3 (apps/api, testes) `Users/AccessLinks/PasswordResetLinkCommandTests.cs`, pelo `Buteco.Api.dll` num processo à parte, contra o Postgres do Testcontainers, com `PublicUrl__BaseUrl` no ambiente:
  - `0`: o stdout com uma única linha `<base>/redefinir-senha#<43>`, aceita em `POST /auth/password-reset`; o link anterior inválido; o login com a senha atual aceito (`totp`); stamp e hash da senha iguais; a autorização viva;
  - o stderr com `[1197]` e `Resultado gerado`, sem o token, o link nem o hash do token;
  - `1`, `2`, `3` (o link do convite ainda aceito), `4` (Operador e Leitor), `64` (sem `--email`, vazio, argumento desconhecido): sem efeito;
  - o e-mail com maiúsculas e espaços (`"  Joana.183@Empresa.com "`) acha a usuária e sai com o código dela, nunca `1`;
  - `70` com a conexão numa porta fechada: sem "nenhum usuário", sem a senha nem `Host=`;
  - `78` com `PublicUrl__BaseUrl` vazia e sem `Identity__PanelBaseUrl`: o link anterior ainda aceito, e a saída sem valor de configuração;
  - o histórico de migrations sem mudança com uma migration pendente.
  - **(guarda)** Defeito: o comando aceitando um não-Admin (a checagem do papel retirada). Reprova o caso do `4`.
  - **(guarda)** Defeito: o `UPDATE` antes da url resolvida (o comando chama `IssueAsync` e só depois confere a base, saindo `78`). Reprova o caso do `78`: o link antigo morre sem um novo.
  - **(guarda)** Defeito: o link na linha de registro (o `Warning` com o link). Reprova o caso do stderr.
  - **(guarda)** Defeito: o `catch` geral devolvendo `1`. Reprova o caso do `70`.
  - **(guarda)** Defeito: o comando comparando o e-mail sem normalizar. Reprova o caso do e-mail com maiúsculas e espaços.
  **Feito:** 17 casos (o `4` e o `64` em `Theory`). O e-mail literal `joana.183@empresa.com` vale só nesta classe, que tem o próprio banco.
  **Visto reprovar** (saídas em `~/.cache/buteco-agents/257-guardas/`):
  - `g2.3a-aceita-nao-admin` (a checagem do papel virou `false && …`) → `NotAdmin_ExitsFour_AndNoLinkIsWritten` (`Operator` e `Reader`), `Values differ`, `Expected: 4`. Voltou: `Users/AccessLinks/PasswordResetLinkCommand.cs` (`d831453a…`);
  - `g2.3b-update-antes-da-url-r2` (sem base, o comando escreve com uma base de mentira e só depois sai `78`, com a mensagem de verdade) → `NoPanelBase_ExitsSeventyEight_AndThePreviousLinkStillWorks`, `Assert.True() Failure` na linha 236 (`LinkWorksAsync(previous)`: o link antigo morreu sem um novo). Voltou: o mesmo arquivo (`d831453a…`). A primeira tentativa (`g2.3b-update-antes-da-url`) reprovou na asserção da mensagem, porque a mutação a trocou, e não pelo defeito: foi descartada e refeita;
  - `g2.3c-link-no-registro` (o link no `Warning` do `gerado`) → `TheStderr_DoesNotCarryTheLink`, `Assert.DoesNotContain() Failure: Sub-string found`. Voltou: o mesmo arquivo (`d831453a…`);
  - `g2.3d-catch-devolve-1` → `TheDatabaseDown_ExitsSeventy_AndDoesNotSayTheUserDoesNotExist`, `Expected: 70`. Voltou: o mesmo arquivo (`d831453a…`);
  - `g2.3e-email-sem-normalizar` (`normalizedEmail = email`) → `AnEmailWithCapitalsAndSpaces_FindsTheUser`, `Expected: 0` (saiu `1`). Voltou: o mesmo arquivo (`d831453a…`).
- [x] 2.4 (apps/api, testes) `Users/AccessLinks/PasswordResetLinkParityTests.cs`: o mesmo estado inicial (link anterior pendente, sessão viva, verificação ligada) pela rota e pelo comando termina igual (D1); e dois links em sequência, pela rota e depois pelo comando: só o segundo é aceito (D7).
  - **(guarda)** Defeito: a rota com uma cópia da escrita que troca o carimbo de segurança. Reprova a paridade.
  **Feito:** 2 casos.
  **Visto reprovar** (`g2.4-rota-copia-troca-stamp`): a rota com uma cópia da escrita que também grava `SecurityStamp` → `PasswordResetLinkParityTests.TheRouteAndTheCommand_EndInTheSameState`, `Values differ` (`StampChanged`). Voltou: `Users/AccessLinks/PasswordResetEndpoints.cs` (`1a7037c0…`).

## 3. Documentação de deploy (D3, D6, Migration Plan)

- [x] 3.1 (docs) `docs/deployment.md`, ao lado da seção do `two-factor-reset`: o comando (quem usa, só Admin, o comando, as saídas, onde fica o registro); o cuidado com o link (não colar em chat, ticket nem e-mail; `docker info --format '{{.LoggingDriver}}'`; fora de `json-file` e `local`, abrir o link logo e criar a senha; sem uso imediato, rodar de novo quando for; vazou, rodar de novo); e, **em destaque**, o roteiro do último Admin que perdeu a senha e as 2 etapas (D6), com o que a pessoa vê em cada passo e o aviso de que, depois dos dois comandos, o link é credencial completa por 24 h. A nota "O comando irmão … é a #257" passa a apontar para a seção nova.
  **Feito:** a seção "Comando de emergência da senha: `password-reset-link` (#257)" depois da da #220, com o aviso em destaque (⚠) antes do roteiro; os textos da página no roteiro conferidos no `AccessLinkPage.tsx` ("Criar nova senha", "Salvar senha e entrar"). A nota da #220 aponta para a seção nova.
- [x] 3.2 (docs) `docs/development.md` (o comando por `dotnet run -- password-reset-link`) e `SECURITY.md` (o comando ao lado do `two-factor-reset`), se citarem o caminho de emergência.
  **Feito:** `docs/development.md` (o comando por `dotnet run`, na caixa do "Perdeu o segredo") e `SECURITY.md` (o comando ao lado do `two-factor-reset`).

## 4. Suítes e conferências

- [x] 4.1 (apps/api) `dotnet build` sem aviso novo contra a baseline.
  **Feito (09/11/2026):** `dotnet build --no-incremental` do `Buteco.Api.Tests` nos dois worktrees: os mesmos 16 avisos distintos (`diff` vazio), nenhum novo.
- [x] 4.2 Suíte completa pelo `suite-257.sh`: `api`, com o veredito e a CPU mínima.
  **Feito (09/11/2026), r1 APROVADA:** 1565/1565 + 1 ignorado, 371 s, CPU mínima 60 (75 amostras), load 5,71 → 7,15, o `podman ps` do início com o `postgres_1` e o `rabbitmq_1` (`~/.cache/buteco-agents/257-fechamento-r1/`).
- [x] 4.3 Comparação por nome contra o `257-baseline/nomes.txt`: nenhum caso sumiu, mudou de nome ou deixou de passar (a lista do topo é vazia); os casos novos contados por arquivo.
  **Feito**, contra `257-baseline/nomes.txt` (1547 → 1566): **nenhum sai**, nenhum muda de nome, nenhum deixa de passar; o único não executado é o mesmo da baseline (`IdentityTokenPruneQueryPlanTests.FourQueries_With150kTokens_StayUnder100Ms`). **Entram 19:** `PasswordResetLinkCommandTests` 17 e `PasswordResetLinkParityTests` 2.
- [x] 4.4 (apps/api + apps/frontend) **O comando pela imagem real, num banco descartável**, como na 10.5 da #220: `podman build -f apps/api/Dockerfile .`, e um banco descartável no Postgres do compose de desenvolvimento, copiado do `buteco_agents` por `pg_dump` (o banco de desenvolvimento não é tocado); `podman run --rm --network buteco-agents_default …` com `PublicUrl__BaseUrl` (e `Identity__PanelBaseUrl=http://localhost:5173` para o Vite):
  - **saída `4`** com o e-mail de um Operador: nenhum link gravado;
  - **saída `78`** sem `PublicUrl__BaseUrl` válida nem `Identity__PanelBaseUrl`: o link anterior intacto no banco;
  - **o roteiro da Q6 inteiro** com um Admin do banco descartável: `two-factor-reset` (`0`), depois `password-reset-link` (`0`, o link só no stdout, a linha `[1197]` sem ele); o link aberto no navegador, com o `apps/api` local apontando para o banco descartável e o Vite: a senha nova → `/entrar` na etapa do cadastro, "Senha salva. Falta só cadastrar o app autenticador para entrar." → QR → código → 10 códigos → painel;
  - o banco descartável e a imagem apagados no fim; as saídas em `~/.cache/buteco-agents/257-dev/4.4/`.
  **Feito (09/11/2026)**, saídas em `~/.cache/buteco-agents/257-dev/4.4/`:
  - **banco descartável** `buteco_257_cmd` no Postgres do compose de desenvolvimento, por `CREATE DATABASE … TEMPLATE template0` (o `template1` recusou por divergência de collation) e `pg_dump buteco_agents | psql`; 10 usuários. **O `buteco_agents` não tem a migration da #184** (`role`): ela foi aplicada só na cópia, por `dotnet ef database update` com a conexão da cópia; o banco de desenvolvimento continua sem a coluna. Na cópia, o `rafael.183` passou a Operador;
  - **imagem:** `podman build -f apps/api/Dockerfile -t buteco-api:257-cmd .`; cada execução por `podman run --rm --network buteco-agents_default … password-reset-link --email …`;
  - **saída `4`** (`rafael.183`, Operador): `exit 4`, stdout vazio, `[1197] … tem o papel Operator; o comando atende só Admin… Resultado nao-admin.`; hash, propósito, vencimento e stamp iguais antes e depois (a primeira conferência, por `access_link_hash is null`, não servia: ele já tinha um link antigo; refeita por comparação);
  - **saída `78`** (`joana.183`, com um link gerado antes pela própria imagem): `exit 78`, stdout vazio, a mensagem com só os nomes das chaves; hash e vencimento do link anterior iguais antes e depois;
  - **o roteiro da Q6** (`validacao.2fa`, Admin com a verificação e 10 códigos):
    1. `two-factor-reset` → `exit 0`, "`[1196]` … 1 sessões encerradas"; no banco, verificação e segredo apagados, códigos 10 → 0, stamp `A85813D6…` → `C0AF32C1…`;
    2. `password-reset-link` → `exit 0`, o link `http://localhost:5173/redefinir-senha#…` sozinho no stdout; o `[1197]` no stderr sem o token; o stamp não mudou;
    3. o link no Chrome, com o `apps/api` local apontado para a cópia e o Vite: "Criar nova senha", "Olá, Validação Duas Etapas…"; a senha nova (gerada para a conferência, guardada em arquivo 600 e apagada com o banco) em "Salvar senha e entrar";
    4. `/entrar` na etapa do cadastro: "Senha salva. Falta só cadastrar o app autenticador para entrar.", o QR e o segredo;
    5. o código (calculado do segredo, RFC 6238, como na #220) → "Verificação ligada" e os 10 códigos → "Continuar" → `/inventory`, o painel (`painel.jpg`); no banco, a verificação ligada, o link apagado, o stamp trocado e 10 códigos;
  - **no fim:** o banco descartável e a imagem apagados (conferido: nenhum `buteco_257%` e nenhuma imagem `257-cmd`); o `apps/api` e o Vite parados. O Vite rodou do worktree depois de um `npm ci`, porque o `node_modules` do `main` não tinha o `qrcode.react`; o `node_modules` do worktree foi apagado depois.
- [x] 4.5 Medição da convenção 18: produção, testes e duplos separados, por `git diff --numstat` contra `0ffe651` mais as linhas dos arquivos novos, comparados à projeção do design (~+176 / −15 e ~+335 / −3).
  **Feito**, por `git diff --numstat 0ffe651` com os arquivos novos, só código:
  - **produção:** +237 / −16 em 5 arquivos (`PasswordResetLinkCommand.cs` 170, dos quais 40 de comentário e 21 em branco; `PasswordResetLink.cs` 47, com 19 de comentário; `PasswordResetEndpoints.cs` +9/−16; `Program.cs` +7; `UserServiceCollectionExtensions.cs` +4), contra ~+176 / −15 projetados (**+35 %**) e ~+180 corrigidos (**+32 %**);
  - **testes:** +376 em 2 arquivos (`PasswordResetLinkCommandTests` 274, `PasswordResetLinkParityTests` 102); **duplos:** +17 / −4 (`EmergencyCommandRunner`). Somados, +393 / −4, contra ~+335 / −3 projetados (**+17 %**) e ~+470 corrigidos (**−16 %**);
  - **docs:** +102 / −3 (`deployment.md`, `development.md`, `SECURITY.md`).

## 5. Documentação e registros

- [x] 5.1 (docs) `01-ARQUITETURA_E_CONVENCOES.md` (o comando ao lado do `two-factor-reset`, se a seção de autenticação o descrever), `02-HISTORICO_E_STATUS.md` (a entrada da change, com as decisões Q1–Q7, as premissas inexatas, as rodadas e a medição) e `CHANGELOG.md`, **antes do archive**.
  **Feito (09/11/2026):** `01` (o comando ao lado do `two-factor-reset`, na seção de autenticação; nenhuma convenção numerada mudou), `02` (a entrada da change, com as decisões, as premissas, o guarda refeito, as medições e o banco de desenvolvimento sem a migration da #184) e `CHANGELOG.md` (Added), antes do archive.
- [x] 5.2 (issues) O PR leva `Closes #257`. Comentários de fechamento só com o ok do dono.
  **Feito:** o PR leva `Closes #257` e a #257 vai para `In review` ao abri-lo; nenhum comentário de fechamento publicado.
