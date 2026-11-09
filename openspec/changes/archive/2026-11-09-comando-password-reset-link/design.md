## Context

Segunda issue da M2, logo depois da #184. A #220 deixou no `Program.cs` o ramo de `args` e o padrão do comando de emergência (`two-factor-reset`), e a #184 deixou a geração do link de redefinição só para o Admin. Esta change faz o comando irmão, do lado da senha.

**Fontes lidas:**
- **A #257:** o corpo e os dois comentários (08/11/2026: entra na M2 depois da #184; a numeração "M2 · 2 de 6").
- **O design da #220** (`openspec/changes/archive/2026-11-08-verificacao-2-etapas-obrigatoria/design.md`): D6 (o comando), D9 (`TwoFactorEmergencyResetTests` e `TwoFactorResetParityTests`), "Tamanho e divisão" e Migration Plan.
- **O código em `0ffe651`:**
  - `Users/AccessLinks/PasswordResetEndpoints.cs`, `CreateAsync` (l. 39–99): Admin pelo papel (l. 51), bloqueio (l. 56), si mesmo → `403` (l. 61–64), estado (l. 66–75), senha de quem chama (l. 77), e a geração: token, hash, vencimento, `UPDATE` condicionado ao ativo (l. 82–92), e a url no fim (l. 95–98);
  - `Users/TwoFactor/TwoFactorReset.cs` e `TwoFactorEmergencyCommand.cs` (o molde), `Program.cs` l. 40–46;
  - `Identity/PanelBaseUrl.cs`, `IdentityIssuerOptions.cs` l. 27–33, `IdentityIssuerServiceCollectionExtensions.cs` l. 36–46 e l. 145–166 (`IdentityIssuerState.TryParseIssuer`);
  - `Users/AccessLinks/AccessLinkPublicEndpoints.cs`, `CompleteAsync` (l. 160–224): a conclusão troca senha e stamp, apaga o link e encerra as sessões, e zera o bloqueio;
  - `apps/frontend/src/features/auth/pages/AccessLinkPage.tsx` l. 107–116 (o que a pessoa vê depois de salvar a senha);
  - `docker-compose.prod.yml` (serviço `api`, l. 82–143) e `apps/api/Dockerfile` (`USER app`, `ENTRYPOINT`);
  - `Users/UserAdministration.cs` (o lock `BUTECOD1`).
- **A spec viva `user-access-links`:** "Link de acesso de uso único", "O link leva o token no fragmento", "Gerar o link de redefinição de senha", "O que invalida o link".

## Goals / Non-Goals

**Goals:**
- O último Admin que perdeu a senha volta a entrar sem SQL à mão, com o mesmo link e o mesmo efeito da rota do admin.
- O link, que é uma credencial de 24 h, não fica em log persistente por obra do comando, e o operador sabe o que conferir.
- O roteiro de quem perdeu a senha **e** as 2 etapas, escrito no `docs/deployment.md`.

**Non-Goals:**
- **Tela:** o painel não muda. Se passar a precisar, a change para.
- **Tabela de auditoria:** como na #220 (Q4 de lá), o registro é a linha `Warning`.
- **Renovar o convite pelo comando:** o convidado sai com código próprio (d).
- **Mudar o `two-factor-reset`:** o comando da #220 fica como está, inclusive o log na saída padrão.

## Decisions

### D1. (a) A extração: `PasswordResetLink`

**Recomendação.** Sai de `CreateAsync` só a escrita do link, que é o que a rota e o comando têm de fazer igual:

```csharp
public sealed class PasswordResetLink(AppDbContext dbContext, TimeProvider timeProvider)
{
    public sealed record Issued(string Link, DateTimeOffset ExpiresAt);

    // null: o usuário não está mais ativo quando o UPDATE roda (desativado no meio).
    public async Task<Issued?> IssueAsync(Guid userId, Uri panelBase, CancellationToken cancellationToken)
    {
        var token = AccessLinkToken.New();
        var expiresAt = timeProvider.GetUtcNow() + AccessLinkToken.PasswordResetLifetime;
        var issued = await dbContext.Users
            .Where(user => user.Id == userId && user.Status == UserStatus.Active)
            .ExecuteUpdateAsync(set => set
                .SetProperty(user => user.AccessLinkHash, AccessLinkToken.Hash(token))
                .SetProperty(user => user.AccessLinkPurpose, AccessLinkPurpose.PasswordReset)
                .SetProperty(user => user.AccessLinkExpiresAt, expiresAt), cancellationToken);
        return issued == 1 ? new Issued(AccessLinkUrls.For(AccessLinkPurpose.PasswordReset, panelBase, token), expiresAt) : null;
    }
}
```

- **Fica na rota** o que é da entrada HTTP: Admin pelo papel, bloqueio, `403` de si mesmo, `404`/`409` pelo estado, a senha de quem chama. A rota resolve a base (`PanelBaseUrl.Resolve`) e chama `IssueAsync`; `null` → o `409` "Usuário desativado." de hoje.
- **Fica no comando** o que é da entrada de emergência: e-mail normalizado, estado, papel (D4), a base resolvida da configuração (D2), os códigos (D5).
- **A base entra como argumento, resolvida antes da escrita.** Hoje a rota resolve depois do `UPDATE` (l. 97). Na rota não faz diferença observável, porque o boot reprova sem issuer (`IdentityIssuerStartupValidation` l. 34–42); no comando faria: resolver depois deixaria o link anterior morto e nenhum novo na tela. Passar a base pronta torna a ordem obrigatória para as duas entradas.

**O efeito, idêntico ao da rota de hoje** (é o mesmo `UPDATE`):

| | rota hoje | rota e comando depois |
|---|---|---|
| link anterior | sobrescrito: deixa de valer | igual |
| senha atual | continua até a nova ser criada | igual |
| `security_stamp` | não muda | igual |
| sessões (autorizações) | ficam vivas | igual |
| desativado entre a leitura e a escrita | nenhum link (`UPDATE` condicionado) | igual (`null` → `409` na rota, `2` no comando) |

**O teste de paridade**, no molde do `TwoFactorResetParityTests`: `PasswordResetLinkParityTests`. Dois usuários ativos com o mesmo estado inicial (um link de redefinição anterior pendente, uma sessão viva, a verificação ligada); um pela rota, outro pelo comando. Afirma que os dois terminam iguais: o link anterior recebe a resposta de link inválido; o novo é aceito em `POST /auth/password-reset`; propósito `password_reset`; vencimento a 24 h da geração (tolerância de 1 min, porque o comando roda com o relógio do sistema num processo à parte); stamp e hash da senha iguais aos de antes; o login com a senha atual aceito (`200 {segundoPasso:"totp"}`); a autorização viva. **Guarda contra:** uma das entradas sem o `PasswordResetLink` (o comando numa cópia que troca o stamp reprova).

**Alternativa descartada: extrair a rota inteira** (uma operação com papel, senha de quem chama e "si mesmo"). O comando não tem quem chama nem senha a conferir; a extração teria parâmetros que só uma entrada usa, e a regra de quem pode viraria `if` por entrada dentro do serviço. A #220 separou do mesmo jeito: o `TwoFactorReset` só faz a escrita.

### D2. (b) De onde vem a url do painel

**A rota** monta o link com `PanelBaseUrl.Resolve(options.Value, issuer)` (`PasswordResetEndpoints.cs` l. 97): `Identity:PanelBaseUrl` quando é uma url absoluta, senão o issuer, que é `PublicUrl:BaseUrl` normalizada por `IdentityIssuerState.TryParseIssuer`. Em produção, `Identity__PanelBaseUrl` não existe no compose, e vale `PublicUrl__BaseUrl: ${PUBLIC_DOMAIN:?…}` (`docker-compose.prod.yml` l. 130).

**No host mínimo, a configuração está disponível; o estado do issuer, não.**
- O `docker compose run` usa o `environment:` do serviço `api` inteiro, e o `:?` faz o compose recusar a execução sem `PUBLIC_DOMAIN`. O `Host.CreateApplicationBuilder` lê `appsettings.json`, `appsettings.{ambiente}.json` e as variáveis de ambiente, como o host web.
- O `IdentityIssuerState` só é registrado pelo `AddIdentityIssuer`, que o host mínimo não chama (e não deve: registra OpenIddict, anel de chaves e serviços em segundo plano).

**Recomendação.** O comando lê as duas chaves da configuração e monta a base com as mesmas funções da rota: `IdentityIssuerState.TryParseIssuer(configuration["PublicUrl:BaseUrl"])` → `new IdentityIssuerState(issuer ou null)`, `configuration.GetSection("Identity").Get<IdentityIssuerOptions>()`, e `PanelBaseUrl.Resolve`. **Antes de montar o host e de abrir qualquer conexão com o banco** (Q5): a configuração é a do `builder.Configuration` do `Host.CreateApplicationBuilder()`, lida antes do `Build()` (as mesmas fontes do host: `appsettings.json`, `appsettings.{ambiente}.json`, variáveis de ambiente), e o `AppDbContext` só é registrado e o host só é montado com a base em mãos. *(Corrigido no apply: a primeira redação dizia um `ConfigurationBuilder` à parte; ler do builder dá as mesmas fontes sem duplicá-las.)* Sem base válida, sai com **`78`** (`EX_CONFIG` do `sysexits.h`, na família do `64` e do `70`), sem tocar o banco:

> `password-reset-link: a url do painel não está configurada (PublicUrl:BaseUrl ausente ou inválida, e Identity:PanelBaseUrl vazia); nada mudou. Resultado sem-url.`

A mensagem cita só os nomes das chaves, nunca o valor de nenhuma variável.

**Alternativa descartada: deixar cair no `70`.** O `PanelBaseUrl.Resolve` lança `InvalidOperationException`, e o `catch` geral sairia `70` com o tipo do erro: correto quanto ao "nada mudou" só se a resolução vier antes do `UPDATE`, e sem dizer ao operador o que falta. Um código próprio custa uma constante e um teste.

### D3. (c) Onde o link aparece e onde ele não pode ficar

**O que o Docker guarda.** O `docker-compose.prod.yml` não declara `logging:` em nenhum serviço (conferido: zero ocorrências), então vale o driver padrão do daemon: `json-file`, salvo se o `/etc/docker/daemon.json` do host disser outro. O contêiner do `run` grava **stdout e stderr** no driver mesmo com o terminal ligado (o attach e o log são caminhos paralelos), e com TTY (o padrão do `compose run`) os dois chegam já misturados.
- **`json-file` e `local`:** o arquivo fica em `/var/lib/docker/containers/<id>/` e **some com o contêiner**: com `--rm`, ao sair.
- **`journald`, `syslog`, `gelf`, `fluentd`, `awslogs` e afins:** a linha sai do host ou vai para o journal, e **fica depois do `--rm`**. O `docker compose run` não tem `--log-driver` para trocar por execução.
- **Fora do Docker:** o scrollback do terminal e, se houver, o log do `tmux`/`screen`. O histórico do shell guarda só o e-mail.

**Recomendação: stdout, uma vez, sozinho numa linha; a linha `Warning` vai para stderr.** Separar os canais não muda a persistência (os dois vão ao driver); serve para o operador poder copiar o link sem o log e para o teste afirmar que o link aparece só no stdout, e nunca na linha de registro. A proteção real está no próprio link e no procedimento:
- **usar o link logo:** concluir a redefinição apaga o hash (`AccessLinkPublicEndpoints.cs` l. 209), e uma cópia que tenha ficado em algum log morre junto;
- **não usado, vence em 24 h**; na dúvida, rodar o comando de novo, que invalida o anterior;
- **antes de rodar**, `docker info --format '{{.LoggingDriver}}'`: com `json-file` ou `local`, o `--rm` leva o log; com outro, saber que a linha fica lá até o link ser usado.

**A linha `Warning`** (`EventId 1197`, `PasswordResetLinkEmergency`): o resultado, o e-mail pedido, o id do usuário quando houver, o papel e o vencimento. **Nunca** o token, o link, o hash nem o carimbo. Na falha (`70`), só o tipo do erro e o da causa, como na #220.

**`docs/deployment.md`** (Q4, decisão do dono): a seção do comando ao lado da do `two-factor-reset`, com o cuidado do operador:
- **não colar o link em chat, ticket nem e-mail**; entregá-lo à própria pessoa;
- conferir `docker info --format '{{.LoggingDriver}}'` antes de rodar;
- **se o driver não for `json-file` nem `local`, abrir o link logo depois de gerá-lo e criar a senha**: usar o link mata a cópia que ficou no log;
- **se a pessoa não for usar o link na hora, rodar o comando de novo quando for**, em vez de guardar o link;
- se o link vazou, rodar de novo: o anterior deixa de valer.

**Alternativas descartadas:**
- **Arquivo com permissão restrita.** O sistema de arquivos do contêiner some com o `--rm`, então exigiria um bind mount gravável pelo usuário `app` e um `--out` no comando. Troca um log que some (ou que morre quando o link é usado) por um arquivo no disco do host, que entra em backup e depende de alguém apagar.
- **stderr para o link.** Mesma persistência do stdout, e o link deixaria de ser o único conteúdo do canal "de dados".
- **Um serviço `api-cli` no compose com `logging: driver: none`** (Q4, decisão do dono). Além de duplicar o bloco `environment:` do `api` (ou exigir âncoras YAML) e mudar o comando da issue, o `none` não esconde só o link: **apaga também a linha de registro**, que é o único rastro de que o comando rodou (D3; sem tabela de auditoria, como na #220).

### D4. (d) Quem o comando atende

**Estado:**
- **ativo:** gera;
- **convidado → `3`, sem efeito.** O `UPDATE` é condicionado ao ativo, então mesmo sem a checagem o convidado não receberia o link de redefinição; a checagem dá o código e a mensagem: "o usuário {id} ainda não aceitou o convite; gere um novo link do convite em Usuários". O convidado nunca teve senha; o caminho dele é o convite, e o link do convite que ele tem continua valendo (o teste afirma);
- **desativado → `2`, sem efeito**, como a rota (`409`).

**Alternativa descartada para o convidado: o comando renovar o convite.** É outra operação (72 h, `/convite`, `RenewAsync`), e um convidado não é o último Admin: quem convida é um Admin ativo (#184), que segue lá para renovar.

**Papel. Recomendação: só Admin; Operador e Leitor → `4`, sem efeito** (Q2), com "o usuário {id} é {papel}; peça o link a um Admin em Editar usuário". Motivo: a #184 garante que sempre sobra um Admin ativo (`UserAdministration`, regra do último Admin, D6 de lá), então quem não é Admin **sempre** tem o caminho normal, que pede a senha de quem gera (P10). O comando fica exatamente do tamanho do buraco que fecha.

**Alternativa descartada: qualquer papel**, em paridade com o `two-factor-reset`, que aceita qualquer um. Tem a seu favor a simetria e um código a menos. Contra: transforma o servidor em segundo canal de redefinição para todo mundo, sem a confirmação de senha do painel. **Por que é diferente do `two-factor-reset`** (decisão do dono, Q2): cada comando, **sozinho**, não dá acesso. A redefinição das 2 etapas sem a senha não abre sessão, e o link de senha também não, porque o login ainda pede o código do app. Por isso o `two-factor-reset` pode aceitar qualquer papel. **No roteiro do último Admin (D6) os dois rodam juntos**, e a partir do segundo comando **o link passa a ser credencial completa por 24 h**: quem o abre cria a senha e cadastra o próprio app autenticador. É o caso que o `password-reset-link` existe para atender, e o motivo de o restringir ao Admin, que não tem outro caminho. Isso vai em destaque no `docs/deployment.md`.

**O que muda em cada caso:**

| | só Admin (recomendado) | qualquer papel |
|---|---|---|
| códigos | `0 1 2 3 4 64 70 78` | `0 1 2 3 64 70 78` |
| Operador sem senha | pede ao Admin no painel | pede ao Admin, ou a quem tem o servidor |
| testes | + um caso (`4`, sem efeito) | — |
| docs | "só para Admin; os outros pedem ao Admin" | "para qualquer usuário ativo" |

### D5. (e) Códigos de saída

| caso | código | efeito | resultado no log |
|---|---|---|---|
| link gerado | `0` | o link anterior deixa de valer; o novo no stdout | `gerado` |
| nenhum usuário com o e-mail | `1` | nenhum | `sem-usuario` |
| usuário desativado | `2` | nenhum | `desativado` |
| usuário convidado | `3` | nenhum (o convite dele continua valendo) | `convidado` |
| usuário não é Admin (Q2) | `4` | nenhum | `nao-admin` |
| `--email` ausente, vazio ou argumento a mais/desconhecido | `64` | nenhum | `uso` |
| falha ao falar com o banco ou erro inesperado | `70` | nenhum (o `UPDATE` não se completa) | `falha` |
| url do painel ausente ou inválida | `78` | nenhum (antes do banco) | `sem-url` |

- **O `1` só sai de uma consulta que respondeu.** Uma exceção não tratada nunca sai com `0` a `4`: o `catch` geral é `70`, como na #220.
- **A ordem:** uso (`64`) → url (`78`) → consulta → estado (`1`, `2`, `3`) → papel (`4`) → escrita. O `UPDATE` que não afeta linha (desativado no meio) sai `2`.
- **Não pergunta nada** e não aplica migrations.

### D6. (f) O último Admin que perdeu a senha e as 2 etapas

**Primeiro as 2 etapas, depois a senha.** Os dois comandos são independentes no banco: redefinir as 2 etapas não toca o link (spec `user-access-links`, "O que invalida o link"), e gerar o link não toca a verificação. A ordem importa pelo que a pessoa vê:

1. `… run --rm --no-deps api two-factor-reset --email maria@empresa.com` → `0`. A verificação apagada, o stamp trocado, as sessões encerradas. (Sai `3` se ela não tinha a verificação; segue igual.)
2. `… run --rm --no-deps api password-reset-link --email maria@empresa.com` → `0` e o link.
3. Ela abre o link, cria a senha nova. A conclusão troca a senha e o stamp, apaga o link, encerra as sessões e **zera o bloqueio** (`AccessLinkPublicEndpoints.cs` l. 222), o que cobre quem se trancou tentando senhas.
4. A página entra com a senha nova e recebe `200 {segundoPasso:"cadastro"}` (`AccessLinkPage.tsx` l. 107–116): `/entrar` na etapa do cadastro, "Senha salva. Falta só cadastrar o app autenticador para entrar." → QR → código → 10 códigos → painel.

**Na ordem inversa** funciona, mas tropeça: depois do passo 3, a página recebe `{segundoPasso:"totp"}` e abre o passo do código, que ela não tem como passar; aí roda o `two-factor-reset` e entra de novo com a senha nova, já no cadastro. Um login a mais e uma tela sem saída no meio.

**Só a senha perdida** (com o celular): só o passo 2; o login pede o código, como sempre.

**Ajuste de código:** nenhum. O roteiro usa o que a #220 e a #181 já entregam.

**Entre o passo 2 e o passo 3, o link é credencial completa** (D4): com a verificação já redefinida, quem abre o link entra como Admin, cadastrando o próprio app. O `docs/deployment.md` traz isso em destaque, junto do roteiro: rodar o passo 2 só quando a pessoa for abrir o link na hora, e entregá-lo a ela sem passar por chat, ticket nem e-mail.

### D7. (g) Concorrência com a rota

**Vale o link de quem escreveu por último.** Rota e comando fazem um `UPDATE` só, de uma linha, que grava hash, propósito e vencimento juntos. O Postgres serializa os dois pelo lock de linha; o segundo sobrescreve o primeiro, e o link entregue primeiro já nasce morto (resposta de link inválido). Cada lado vê um link; só o último funciona. Quem descobrir que o seu não abre gera outro.

**O `BUTECOD1` não é preciso.** Ele existe para um invariante entre linhas ("sempre sobra um Admin ativo"), que duas transações podem quebrar juntas lendo cada uma o estado da outra. Aqui não há invariante entre linhas: o estado lido antes (ativo, papel) é conferido de novo no `WHERE` do `UPDATE` quanto ao estado. O papel não é reconferido: um Admin rebaixado entre a leitura e a escrita receberia o link, o que é inofensivo (como Operador, pediria o mesmo link a um Admin) e não vale um lock.

**Alternativa descartada:** pegar o `BUTECOD1` no comando e na rota. Serializaria com desativações e mudanças de papel sem proteger invariante nenhum, e a rota passaria a esperar por ele.

### D8. Testes

Os guardas de cada linha são conferidos contra o defeito (convenção 15).

**`apps/api`, integração:**

| teste | o que afirma | guarda contra |
|---|---|---|
| `PasswordResetLinkCommandTests` | pela entrada do `Program.cs`, num processo à parte, contra o Postgres do Testcontainers: `0`, o link no stdout uma vez, no formato `<base>/redefinir-senha#<43>`, aceito em `POST /auth/password-reset`, e o anterior inválido; a senha atual ainda entra, stamp igual, a sessão viva | comando que troca o stamp; comando que imprime o link duas vezes |
| | a linha `Warning` com `[1197]` no stderr, com o id e `Resultado gerado`, sem o token, o link nem o hash | link no log |
| | `1` sem usuário; `2` desativado; `3` convidado, com o link do convite ainda aceito; `4` Operador e Leitor; `64` nas formas erradas: sem efeito em todas | comando que aceita quem não é Admin |
| | `70` com o banco fora: não diz "nenhum usuário", sem senha nem `Host=` na saída | `catch` que devolve `1` |
| | `78` com `PublicUrl__BaseUrl` vazia e sem `Identity__PanelBaseUrl`: o link anterior continua aceito | url resolvida depois do `UPDATE` |
| | não aplica migrations | — |
| `PasswordResetLinkParityTests` | o mesmo estado inicial pela rota e pelo comando termina igual (D1) | uma das entradas sem o `PasswordResetLink` |
| | dois links em sequência, pela rota e depois pelo comando: só o segundo é aceito (D7) | — |
| testes da rota de hoje | continuam como estão: a rota responde igual | — |

**Apoio:** o `EmergencyCommandRunner` da #220 ganha variáveis de ambiente extras (`PublicUrl__BaseUrl`) e devolve stdout e stderr também separados; o `Output` concatenado continua, para os testes da #220 não mudarem.

**Nada vai para `libs/`.** Só o `apps/api` muda.

### Árvore

```
apps/api/src/Buteco.Api/
├── Program.cs                                   + o ramo do password-reset-link, junto do two-factor-reset
└── Users/AccessLinks/
    ├── PasswordResetLink.cs                     NOVO: a geração do link, extraída (D1)
    ├── PasswordResetLinkCommand.cs              NOVO: o comando (D2 a D5)
    └── PasswordResetEndpoints.cs                sobre o PasswordResetLink; base resolvida antes
apps/api/tests/Buteco.Api.Tests/
├── Support/EmergencyCommandRunner.cs            + ambiente extra, stdout e stderr separados
└── Users/AccessLinks/
    ├── PasswordResetLinkCommandTests.cs         NOVO
    └── PasswordResetLinkParityTests.cs          NOVO
docs/deployment.md                               + o comando, o cuidado com o link, o roteiro (D6)
openspec/specs/user-access-links/spec.md         (no archive) + o requisito do comando
```

`AddScoped<PasswordResetLink>()` no host web e no host mínimo do comando. O `AdvisoryLockKeysTests` não muda (nenhum lock novo).

## Suítes

Pelo `~/.cache/buteco-agents/bin/suite-257.sh` (cópia do `suite-184.sh`, com o lock de uma suíte por vez).

| suíte | entra? | por quê |
|---|---|---|
| `apps/api` | **entra** | todo o código e os testes da change |
| `apps/frontend` | fora | o painel não muda, e a resposta da rota é a mesma |
| `apps/inbox`, `apps/connectors` | fora | código intocado; não geram nem consomem link |
| `apps/workers` | fora | sem usuário nem rota |
| `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests` | fora | passam pelo login do emissor (`IssuerUserFlow`), que a change não toca; nenhum usa a redefinição (conferido: nenhuma ocorrência de `password-reset` em `tests/`) |
| `tests/CrossAppTaskStoreCompatibility.Tests` | fora | acordo de banco entre api e workers, sem usuário |

## Tamanho (h)

**Âncora decomposta** (convenção 18, só código): o pedaço do comando no PR #258 (#220), medido entre `8471250` e `ebf7b2d`:

| arquivo | produção | testes |
|---|---|---|
| `TwoFactorEmergencyCommand.cs` | +110 | |
| `TwoFactorReset.cs` (a extração) | +31 | |
| `UserTwoFactorResetEndpoint.cs` | +14 / −18 | |
| `Program.cs` | +9 | |
| `EmergencyCommandRunner.cs` | | +42 |
| `TwoFactorEmergencyResetTests.cs` | | +174 |
| `TwoFactorResetParityTests.cs` | | +76 |
| **total** | **+164 / −18** | **+292** |

A #220 tinha projetado esse pedaço em ~+110 + parte de +90/−80 em produção e +170 + parte de +60 em testes: a produção acertou perto, os testes erraram ~+40 %.

**Projeção** (rascunho até a verificação fechar):

| componente | produção | testes |
|---|---|---|
| `PasswordResetLinkCommand` (o molde, mais a url e o papel) | +130 | |
| `PasswordResetLink` | +35 | |
| `PasswordResetEndpoints` sobre a extração | +5 / −15 | |
| `Program.cs` | +6 | |
| `EmergencyCommandRunner` | | +15 / −3 |
| `PasswordResetLinkCommandTests` (dois códigos e o link a mais que a âncora) | | +230 |
| `PasswordResetLinkParityTests` | | +90 |
| **total** | **~+176 / −15** | **~+335 / −3** |

Com o erro de teste da âncora, ~+180 / ~+470. **Uma change só**, menor que a parte do comando da #220 somada à extração.

## Risks / Trade-offs

- **[O link fica num log persistente do host]** → só se o daemon usar driver que não some com o `--rm`. Mitigação: usar o link logo (a conclusão apaga o hash), conferir `docker info` antes, rodar de novo se vazou (D3). Verificável: o teste afirma que o link não está na linha de registro.
- **[O link colado em chat ou ticket]** → o `docs/deployment.md` diz para não fazer, e o link vence em 24 h e morre ao ser usado.
- **[O Admin gera pelo painel enquanto o comando roda]** → vale o último; o outro recebe "link inválido" e gera de novo (D7).
- **[Quem tem o servidor gera link para um Admin qualquer]** → quem roda `docker compose` já lê o `.env.prod`, com a senha do Postgres; o comando não dá poder novo, dá o caminho certo. A linha `Warning` fica no terminal de quem rodou (D3), como na #220.
- **[Url do painel errada no `.env.prod`]** → o link sai com a base errada, como sai hoje pela rota; o `78` cobre só a ausente ou inválida.

## Migration Plan (i)

**Deploy:** só a imagem do `api`. **Sem migration:** as colunas `access_link_*` são da #181. Nenhuma variável nova.

**Conferir:**
- `… run --rm --no-deps api password-reset-link --email <inexistente>` sai `1`, sem mudar nada;
- `docker info --format '{{.LoggingDriver}}'` anotado no `docs/deployment.md` do servidor (o dono confere uma vez);
- a geração pelo painel (Editar usuário → redefinir senha) continua igual.

**Voltar atrás:** a imagem anterior do `api`. O comando some com ela; links já gerados pelo comando continuam valendo até o vencimento, porque são links de redefinição comuns que a imagem anterior aceita. Nada no banco a desfazer.

## Premissas do corpo da issue (j)

Conferidas em `0ffe651`. Nenhuma muda comportamento ou contrato.

| premissa | confere? | onde |
|---|---|---|
| o link só sai de `POST /users/{id}/password-reset`, e a própria pessoa recebe `403` "Use Minha conta…" | sim, e desde a #184 só o Admin chega lá | `PasswordResetEndpoints.cs` l. 30, 51–54, 61–64 |
| link de 24 h, `<painel>/redefinir-senha#<token>` | sim | `AccessLinkToken.cs` l. 19; `AccessLinkUrls.cs` l. 16, 27 |
| o link anterior deixa de valer, a senha atual continua | sim | `PasswordResetEndpoints.cs` l. 85–92 (sobrescreve; não toca senha, stamp nem sessões) |
| a geração não é um serviço separado, está em `CreateAsync` | sim | `PasswordResetEndpoints.cs` l. 82–98 |
| depende do ramo de `args` da #220, em `main` | sim | `Program.cs` l. 40–46 |
| **"SQL à mão: … não é viável sem código"** | **inexata** | o token não tem validação de formato além de não ser vazio (`AccessLinkPublicEndpoints.cs`, `FindAsync`, l. 226–236, só recusa o vazio e faz o SHA-256 do texto): 32 bytes aleatórios em base64url e um `UPDATE` com `sha256()` do Postgres funcionam. É viável e arriscado (esquecer o propósito, o vencimento ou o estado). O motivo do comando continua de pé |
| **"sem o token no log (o link sai só na saída do comando, uma vez)"** | **inexata** | no contêiner, a saída do comando e o log são o mesmo destino: stdout e stderr vão ao driver de log do Docker (D3). O comando separa os canais e o teste afirma que a linha de registro não tem o link; a persistência depende do driver do host |

## Baseline

No worktree limpo `../buteco-agentes-257-baseline`, em `0ffe651`, sem edição nenhuma, pelo `suite-257.sh` com `PAUSAR_RABBIT=0`. Saídas e o relatório por nome em `~/.cache/buteco-agents/257-baseline/`.

| suíte | resultado | duração | estado (início → fim) | CPU mínima |
|---|---|---|---|---|
| `apps/api` | **1546/1546 + 1 ignorado** | 406 s | load 5,04, CPU 100 → load 11,74, CPU 100 | 55 |

- **APROVADA** (regra assimétrica da #181): a CPU caiu a 55 no meio e voltou a 100; verde conta mesmo assim, e a duração não serve de referência.
- `podman ps` no início: `postgres_1` e `rabbitmq_1` de pé.
- `nomes.txt`: 1547 linhas (1546 aprovados, 1 não executado, 1 GUID normalizado), a base da comparação por nome no fechamento.

## Decisões do dono (09/11/2026)

Na revisão do proposal e do design:

| | pergunta | decisão | onde |
|---|---|---|---|
| Q1 | A extração? | só a escrita do link (`PasswordResetLink.IssueAsync`), com a base resolvida antes do `UPDATE`; a spec diz que a ordem muda também na rota, sem efeito visível, porque o boot já exige o issuer; paridade como no D1 | D1 |
| Q2 | Que papel o comando atende? | só Admin, `4` para os outros; o porquê da diferença com o `two-factor-reset` registrado, e o link como credencial completa no roteiro, em destaque no `docs/deployment.md` | D4, D6 |
| Q3 | O convidado? | `3`, sem efeito, apontando para o novo link do convite | D4 |
| Q4 | Como o link sai? | sozinho numa linha do stdout; `Warning` com `EventId` no stderr; sem serviço novo no compose (o `logging: none` apagaria também o registro); os cuidados do driver no `docs/deployment.md`; cenário: o stderr capturado sem o token, o link nem o hash | D3 |
| Q5 | Url do painel ausente? | `78`, conferido antes de abrir conexão com o banco; cenário: a cópia do link anterior continua valendo | D2, D5 |
| Q6 | O roteiro do último Admin? | primeiro `two-factor-reset`, depois `password-reset-link`, no `docs/deployment.md`, com o que a pessoa vê em cada passo | D6 |
| Q7 | Concorrência? | vale o último, sem `BUTECOD1`; cenário: dois links em sequência (rota e comando), só o segundo aceito | D7 |

As duas premissas inexatas do (j) ficam registradas, sem parada.
