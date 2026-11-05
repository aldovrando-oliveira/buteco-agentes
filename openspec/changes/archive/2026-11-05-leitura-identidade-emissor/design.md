## Context

A #179 (change `emissor-oidc-api`) pôs o emissor OIDC no `apps/api`. Ele publica `/.well-known/openid-configuration` e `/.well-known/jwks`, que são anônimos e feitos para máquina, e guarda as chaves em `identity_keys`. A rota de leitura para o painel ficou de fora por decisão do dono (24/10/2026). É esta change.

### O que existe em `914ca67`

| Peça | Onde | O que importa aqui |
|---|---|---|
| Issuer | `IdentityIssuerState(Uri? Issuer)`, `IdentityIssuerServiceCollectionExtensions.cs` | `Issuer` é a url pública com uma barra final. Nulo só quando a url falta, e aí o boot reprova. |
| Endereços | `IdentityIssuerEndpoints` | `Configuration`, `JsonWebKeySet` (`/.well-known/jwks`), `Token` (`/connect/token`), todos relativos |
| Discovery | `DiscoveryEndpointsFromIssuerHandler` | troca o `BaseUri` pelo issuer, e o `AttachEndpoints` do OpenIddict 7.7.2 monta cada endpoint com `CreateAbsoluteUri(BaseUri, relativo)` |
| Chave | `IdentityKey` | `Kid`, `Usage`, `Algorithm`, `ProtectedMaterial`, `CreatedAt`, `SignsFrom`, `SignsUntil`, `ValidatesUntil` |
| Janelas | `IdentityKeyRotationPolicy` | `RotationInterval` 90 d, `PrePublication` 7 d, `Retention` 15 d e `CheckInterval` 1 h, fonte única de cada uma. `IsInRing` = `ValidatesUntil > now`. `SignsAt` = `SignsFrom <= now < SignsUntil`. |
| Anel | `IdentityKeyRing` | carrega sob advisory lock, cria o que falta e monta o snapshot com as chaves `IsInRing`. Assina a `SignsAt` de maior `SignsFrom`. Diz de si (l. 12): "a única peça que lê e escreve `identity_keys`". |
| Gate | `IdentityKeyRingGate` | sem anel carregado, **toda** rota responde `503` com `Retry-After` |
| Acesso | `Program.cs` l. 135–139, `ServiceScopeAuthorizationHandler`, `CurrentUser` | fallback: autenticado + `ServiceScopeRequirement`. Os subjects de serviço só passam nas rotas listadas. A `CurrentUser` exige o `sub` de um usuário existente e ativo, e responde `403` sem ele. |
| Durações | ver K3 | literais em três lugares |

### Achados da exploração

- **K1. O ramo "emissor desligado" não existe mais.** A issue pede que, sem url pública, a rota responda "desligado", "como os endereços A2A fazem". Desde a #217 (design da change `usuarios-locais-login-emissor`, D3.4), a url pública é obrigatória e `IdentityIssuerStartupValidation.cs` l. 38 reprova o boot. O molde A2A também perdeu o ramo: `AgentA2AAddresses.cs` l. 29 usa `options.BaseUrl!`. A premissa é falsa, mas a rota é nova: não há comportamento, contrato ou cobertura existente em jogo (D5).
- **K2. O quadro "Identidade" corrigido (04/11/2026) contra o servidor.** Canvas "Protótipo de autenticação federada", `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`, `project/Provedores.dc.html` (sha256 `3ed1b805…`), relido nos dois valores de `momento`:

  | Campo do quadro | Exemplo do quadro | Fonte no servidor |
  |---|---|---|
  | Issuer, Discovery, Endpoint de token, Chaves públicas (JWKS) | `https://[URL_PUBLICA]/…`, JWKS em `/.well-known/jwks` | `IdentityIssuerState.Issuer` + `IdentityIssuerEndpoints` |
  | Validade do token | "15 min para usuários, aplicações e módulos" | access token 15 min, um valor para todo cliente: o catálogo zera `Settings` por aplicação (`IdentityClientCatalog.cs` l. 219), e o token que o `apps/api` gera para o connectors usa a mesma duração (K3) |
  | Sessão do painel | "renovada enquanto houver uso; termina depois de 14 dias sem atividade" | refresh 14 d, rotativo e **deslizante**: o OpenIddict 7.7.2 só fixa o vencimento com `DisableSlidingRefreshTokenExpiration` (`OpenIddictServerHandlers.cs` l. 4473–4486, `OpenIddictServerOptions.cs` l. 408), que o Buteco não liga |
  | Troca automática a cada 90 dias | cabeçalho do card | `RotationInterval` |
  | 7 dias antes / 15 dias depois | texto do card | `PrePublication` / `Retention` |
  | "A troca acontece em até 1 h depois da data marcada" | texto do card | `CheckInterval` (Q5 aceita) |
  | kid | `fniY4qt8pBcSxBan9KihTw` (22 caracteres) | `Kid`, base64url de 16 bytes (`IdentityKeyFactory.cs` l. 55): a forma bate |
  | Estado | "Em uso", "Próxima", "Aposentando" | D3 |
  | Criada em | 13/10/2026 (em uso), 11/01/2027 (próxima) | `CreatedAt`. A próxima nasce no primeiro tique depois de `SignsUntil − 7 d` da ativa, então "criada em" ≈ "assina desde − 7 d", como no quadro |
  | Assina desde / assina até | "Assina desde 20/10/2026; é substituída em 18/01/2027" | `SignsFrom` / `SignsUntil` (90 d depois, como no quadro) |
  | Sai da publicação em | "Não assina desde 20/10/2026; sai da publicação em 04/11/2026" | `ValidatesUntil` (= `SignsUntil` + 15 d, como no quadro) |
  | Selo "Operando" | fixo | sem campo: com o anel fora, o gate já responde `503` a toda rota. Registrado para a #183. |
  | Provedores federados | card inteiro | fora (M3, #189) |

  **Nenhum campo do quadro fica sem dado no servidor.** Cada exemplo do quadro bate com as janelas da política, nos dois momentos. As duas chaves de cada momento também não convivem com uma terceira: a próxima aparece 83 d depois da troca, e a aposentada sai 15 d depois dela.

  **Divergência que resta** para o comentário de entrega na #183: só o selo "Operando", que é fixo, sem campo na rota. A ordem com "Em uso" em cima e o "1 h" vêm da rota (D2, Q5 aceita).
- **K3. As durações estão em literais espalhados.** `SetAccessTokenLifetime(TimeSpan.FromMinutes(15))` e `SetRefreshTokenLifetime(TimeSpan.FromDays(14))` (`IdentityIssuerServiceCollectionExtensions.cs` l. 90–91), e `ServiceAccessTokenIssuer.Lifetime = TimeSpan.FromMinutes(15)` (`ServiceAccessTokenIssuer.cs` l. 40), que é o token "de módulo" que o quadro inclui no "15 min". O id token e o código não são registrados: valem os padrões do OpenIddict 7.7.2, 20 min e 5 min (`OpenIddictServerOptions.cs` l. 271 e 245, fonte em `~/.cache/buteco-agents/182-fonte/openiddict-core`, tag `7.7.2`, `950cb13`). O `IdentityTokenPrunePolicy.MinimumAge` (14 d) é outro conceito: é o piso da limpeza, que o boot exige ≥ refresh (`IdentityIssuerStartupValidation.cs` l. 91–97), e fica onde está.
- **K4. A troca real atrasa até um tique.** A chave muda de fato no primeiro tique depois de `SignsFrom` (`IdentityKeyRotationPolicy.Retention`, comentário). Até lá, a instância segue com o snapshot anterior, e o JWKS também. Dá até `CheckInterval` = 1 h, e o quadro já diz isso ("em até 1 h depois da data marcada").

## Goals / Non-Goals

**Goals:**

- Uma rota que entrega à #183 tudo o que os dois cards do quadro corrigido mostram, sem a tela inventar estado nem número (convenção 13).
- Estado e datas da mesma conta que a rotação usa. Durações das mesmas constantes que o emissor usa, e um teste que reprova se um lado mudar sem o outro.
- Nenhum material de chave na resposta, e nem na memória da requisição.
- Os endereços iguais, byte a byte, aos do discovery.

**Non-Goals:**

- Tela. A #183 consome a rota.
- Provedores federados (M3, #189) e o papel Admin (#184).
- Ação sobre chave (girar agora, revogar, apagar).
- A chave de cifra (`encryption`), que não roda e o quadro não mostra, e a chave que saiu do anel (`ValidatesUntil <= agora`).
- Mudar qualquer duração.

## Decisions

### D1. Uma rota, `GET /identity` (Q2, aceita)

Uma chamada por tela, como o `GET /account` serve Minha conta. O caminho é livre: o OpenIddict atende `/.well-known/*` e `/connect/*`, e nenhuma rota mapeada começa com `/identity`. Nome em inglês, como `/account` e `/users`.

Alternativas descartadas: duas rotas (`/identity/issuer` e `/identity/signing-keys`), que dariam duas chamadas para uma tela que sempre mostra os dois cards (convenção 2), e `/issuer`, que se confunde com os endereços anônimos do protocolo.

### D2. Formato da resposta

Nomes em português e estado em string minúscula, como as rotas da M1 (`UserSummary.Estado = "ativo"`, `CriadoEm`). Instantes em `DateTimeOffset` UTC. Durações em **segundos inteiros, com o sufixo no nome**, como o `AverageMs` dos Insights. O `TimeSpan` do `System.Text.Json` sai como `"00:15:00"`, que a tela teria que interpretar.

```json
{
  "emissor": {
    "issuer": "https://buteco.exemplo/",
    "discovery": "https://buteco.exemplo/.well-known/openid-configuration",
    "jwks": "https://buteco.exemplo/.well-known/jwks",
    "token": "https://buteco.exemplo/connect/token",
    "validadeDoTokenSegundos": 900,
    "sessaoSemAtividadeSegundos": 1209600
  },
  "chavesDeAssinatura": {
    "intervaloDeTrocaSegundos": 7776000,
    "prePublicacaoSegundos": 604800,
    "retencaoSegundos": 1296000,
    "verificacaoSegundos": 3600,
    "chaves": [
      { "kid": "…", "estado": "em-uso",      "criadaEm": "…", "assinaDesde": "…", "assinaAte": "…", "saiDaPublicacaoEm": "…" },
      { "kid": "…", "estado": "proxima",     "criadaEm": "…", "assinaDesde": "…", "assinaAte": "…", "saiDaPublicacaoEm": "…" },
      { "kid": "…", "estado": "aposentando", "criadaEm": "…", "assinaDesde": "…", "assinaAte": "…", "saiDaPublicacaoEm": "…" }
    ]
  }
}
```

- **Ordem:** `em-uso`, depois `proxima`, depois `aposentando`. Dentro do mesmo estado, `assinaDesde` decrescente. É a ordem do quadro, com "Em uso" em cima nos dois momentos.
- **As três datas vão em toda chave**, e não um "próximo passo" escolhido no servidor. O quadro compõe frases diferentes por estado ("é substituída em", "passa a assinar em", "sai da publicação em"), e a frase é da tela.
- **Sem campo "operando"** (decisão do dono): o `200` só existe com o anel carregado.
- **`verificacaoSegundos`** é o `CheckInterval` (Q5 aceita).

Os nomes do fio ficam fixos com `[JsonPropertyName]` nos records da resposta. O teste compara o texto do JSON, não a desserialização (`docs/conventions.md`, cláusula de naming policy).

### D3. Estado pela política, numa função pura (Q3, aceita)

A classificação mora em `IdentityKeyRotationPolicy`, ao lado de `SignsAt` e `IsInRing`, como `Describe(IReadOnlyCollection<IdentityKeyWindowsView> keys, DateTimeOffset now)`:

| Condição (no `now`) | Estado |
|---|---|
| `SignsAt(key, now)` e é a de maior `SignsFrom` entre as que assinam | `em-uso` |
| `SignsFrom > now` | `proxima` |
| `SignsUntil <= now < ValidatesUntil`, ou `SignsAt` mas não a escolhida | `aposentando` |
| `ValidatesUntil <= now`, ou uso `encryption` | fora da lista |

A escolha da `em-uso` é a do `IdentityKeyRingSnapshot.Build` (maior `SignsFrom` entre as `SignsAt`). Com as janelas contíguas não há duas `SignsAt`, mas a regra de desempate é a do snapshot, para nunca divergir dele. As bordas seguem os operadores que já existem: `SignsFrom == now` já é `em-uso`, `SignsUntil == now` já é `aposentando` e `ValidatesUntil == now` já está fora.

O `now` é o `TimeProvider` da requisição. A rota diz o que a política manda, e o atraso de até um tique (K4) é o que o `verificacaoSegundos` informa.

Alternativa descartada: ler o snapshot do anel. Ele não guarda datas, e com duas instâncias duas chamadas seguidas poderiam se contradizer por até 1 h. A issue pede "o mesmo cálculo de janelas que a rotação usa, e não uma segunda conta".

### D4. A leitura fica no anel, sem material (Q4, aceita)

`IdentityKeyRing.ListSigningKeysAsync` faz `AsNoTracking` com **projeção para `IdentityKeyWindowsView`** (`Kid`, `CreatedAt`, `SignsFrom`, `SignsUntil`, `ValidatesUntil`), filtrando `Usage == Signing`. Não há lock, porque ler não cria chave, e não há cache, porque são três ou quatro linhas e a tela abre raramente. O anel continua sendo "a única peça que lê e escreve `identity_keys`", e o material nem sai do banco. A view não tem propriedade de material, então nenhum código futuro serializa a entidade por engano.

### D5. Sem estado "desligado" (K1)

A rota lê `IdentityIssuerState.Issuer` com guarda (`?? throw new InvalidOperationException(...)`), e não com um ramo de resposta: um estado que não existe não ganha contrato. Fica registrado no `02` e no comentário de fechamento da #211.

### D6. Durações em constantes únicas (Q1, decisão do dono)

`Identity/IdentityTokenLifetimes.cs`:

| Constante | Valor | Quem lê |
|---|---|---|
| `AccessToken` | 15 min | `SetAccessTokenLifetime`, `ServiceAccessTokenIssuer.Lifetime`, rota (`validadeDoTokenSegundos`) |
| `RefreshToken` | 14 d | `SetRefreshTokenLifetime`, rota (`sessaoSemAtividadeSegundos`) |
| `IdentityToken` | 20 min | `SetIdentityTokenLifetime` (novo, igual ao padrão de hoje) |
| `AuthorizationCode` | 5 min | `SetAuthorizationCodeLifetime` (novo, igual ao padrão de hoje) |

A rotação continua em `IdentityKeyRotationPolicy` (`RotationInterval`, `PrePublication`, `Retention`, `CheckInterval`), que já é a fonte única. A rota lê de lá. Mover essas quatro para outra classe mexeria nos testes da política sem ganho.

O comentário de `Retention` ("Quem manda é o refresh") passa a citar `IdentityTokenLifetimes.RefreshToken`. A checagem de boot da retenção (`IdentityIssuerStartupValidation.cs` l. 66–84) não muda: ela já lê as opções compostas.

**Guarda (pedido do dono):** o teste de integração compara a resposta da rota com as `OpenIddictServerOptions` compostas do host (`IOptionsMonitor<OpenIddictServerOptions>.CurrentValue`, lido depois do `200`, com o anel carregado): `validadeDoTokenSegundos` com `AccessTokenLifetime` e `sessaoSemAtividadeSegundos` com `RefreshTokenLifetime`. As janelas são comparadas com o que a rotação gravou na chave do banco: `assinaAte − assinaDesde` com `intervaloDeTrocaSegundos` e `saiDaPublicacaoEm − assinaAte` com `retencaoSegundos`. A pré-publicação é comparada com o limiar do `Plan`, num teste unitário. Uma constante trocada num lado só, ou um literal posto de volta no registro, reprova. O id token e o código não aparecem na rota: o teste afirma que as opções compostas têm os valores das constantes, e o defeito "constante trocada sem o registro" reprova nele.

### D7. Acesso (pedido do dono)

| Quem chama | Resposta | Por quê |
|---|---|---|
| anônimo | `401` | fallback (`RequireAuthenticatedUser`) |
| `service:inbox`, `service:connectors` | `403` | fallback (`ServiceScopeRequirement`: a rota não está na `ServiceRoutes`) |
| token do painel sem usuário (`sub` que não é usuário) | `403` | `CurrentUser.FindAsync` nulo → `CurrentUser.Forbidden()` |
| usuário desativado, com access token ainda no prazo | `403` | idem |
| usuário real e ativo | `200` | — |

A rota usa a `CurrentUser` como as rotas de Usuários (`UserEndpoints.ListAsync`). Na M2, a #184 restringe a Admin. O comentário na #184 sai nesta change: "a #211 criou `GET /identity`, hoje aberto a todo usuário ativo; na M2 passa a ser só de Admin".

### D8. Testes (suíte `api`)

**Unitários** (`IdentityKeyRotationPolicyTests`, sem banco):
- os três estados, com as três bordas;
- a chave de cifra e a vencida ficam fora;
- a ordem `em-uso`, `proxima`, `aposentando`;
- a `em-uso` coincide com a escolha do `IdentityKeyRingSnapshot.Build` para o mesmo conjunto;
- `Plan` cria a próxima com `SignsUntil − now` um tique abaixo de `PrePublication` e não cria um tique acima. É o guarda da pré-publicação.

**Integração** (`IdentityOverviewEndpointTests`, numa `LocalUsersFixture` própria da classe):
- os quatro endereços iguais aos do discovery da mesma fixture, também com outro `Host`;
- as durações iguais às opções compostas e às janelas gravadas (D6);
- as três chaves: o fixture grava, pela `IdentityKeyFactory` (material real), uma `proxima` e uma `aposentando` ao lado da ativa. A resposta traz os três estados na ordem da D2, com as datas iguais às colunas;
- **sem material:** o texto do JSON não contém `"d"`, `"p"`, `"q"`, `"dp"`, `"dq"`, `"qi"`, `"n"`, `"e"`, `"k"`, `"x5c"` nem `protected`. Também não contém o material cifrado de nenhuma linha (base64 e hex das colunas lidas do banco). E as propriedades de cada chave são exatamente as seis da D2;
- os nomes do fio no texto (`chavesDeAssinatura`, `saiDaPublicacaoEm`, `validadeDoTokenSegundos`);
- acesso: os cinco casos da D7.

**Os que mudam:** `ServiceAccessTokenIssuerTests` (l. 55) passa a comparar `ValidTo − IssuedAt` também com `AccessTokenLifetime` das opções compostas. O literal de 15 min continua, e a asserção ganha uma comparação, não perde nenhuma.

Cada guarda é visto reprovar contra o defeito nomeado no `tasks.md` (convenção 15).

## Árvore de pastas proposta

```
apps/api/
├── src/Buteco.Api/
│   ├── Program.cs                                      (+ app.MapIdentityOverviewEndpoint())
│   └── Identity/
│       ├── IdentityTokenLifetimes.cs                   (novo, D6)
│       ├── IdentityIssuerServiceCollectionExtensions.cs  (lê as constantes; + id token e código)
│       ├── Overview/
│       │   ├── IdentityOverviewEndpoint.cs             (novo: GET /identity)
│       │   └── IdentityOverviewResponse.cs             (novo: records da D2)
│       ├── Keys/
│       │   ├── IdentityKeyRing.cs                      (+ ListSigningKeysAsync)
│       │   └── IdentityKeyRotationPolicy.cs            (+ Describe, IdentityKeyWindowsView, estados)
│       └── ServiceTokens/
│           └── ServiceAccessTokenIssuer.cs             (Lifetime = IdentityTokenLifetimes.AccessToken)
└── tests/Buteco.Api.Tests/Identity/
    ├── IdentityKeyRotationPolicyTests.cs               (+ Describe, + limiar do Plan)
    ├── IdentityOverviewEndpointTests.cs                (novo)
    ├── IdentityTokenLifetimesTests.cs                  (novo: opções compostas = constantes)
    └── ServiceAccessTokenIssuerTests.cs                (+ comparação com as opções compostas)
docs/architecture.md                                    (rota na lista do apps/api)
02-HISTORICO_E_STATUS.md, CHANGELOG.md                  (antes do archive)
```

Nada em `libs/`: a rota é do `apps/api`, e nenhum outro app a lê.

## Risks / Trade-offs

- **[A tela diz "em uso" até uma hora antes de a instância trocar]** → é a política, não o snapshot (D3). O `verificacaoSegundos` dá o tamanho do atraso, e o quadro já o escreve.
- **[Registrar id token e código explicitamente muda comportamento]** → não muda: os valores são os padrões de hoje (K3), e o teste da D6 afirma as opções compostas.
- **[Um leitor futuro da tabela esquece a projeção]** → a leitura fica no anel (D4), e o teste do JSON afirma a lista exata de campos.

## Migration Plan

Nenhuma migration nem configuração nova, e nenhuma duração muda. A rota nasce no deploy, e o rollback a remove sem efeito em dado.

## Decisões do dono (04/11/2026)

- **Q1 aceita, ampliada:**
  - as durações (access, refresh, id token, código) e as da rotação (intervalo, pré-publicação, retenção) ficam em constantes únicas, usadas pelo registro do OpenIddict, pela política de rotação e pela rota (D6);
  - guarda: uma constante trocada num lado só faz reprovar o teste que compara a resposta da rota com a configuração do OpenIddict.
- **Q2 aceita:** `GET /identity`, uma rota (D1).
- **Q3 aceita:** estado pela política no instante da requisição (D3).
- **Q4 aceita:** leitura no `IdentityKeyRing`, com projeção sem material (D4).
- **O quadro "Identidade" foi corrigido** e é a referência do contrato (D2, K2). O selo "Operando" é fixo na tela, sem campo na rota. Provedores federados ficam fora (M3, #189).
- **Acesso:** todo usuário real (`CurrentUser`) na M1, serviço `403`, nunca material privado (D7, D8). A #184 recebe o comentário de que a rota passa a ser só de Admin.
- **Q5 aceita:** `verificacaoSegundos` entra, lido de `IdentityKeyRotationPolicy.CheckInterval`, a mesma constante do intervalo de verificação da rotação. A divergência que sobra para a #183 é só o selo "Operando".

## Open Questions

Nenhuma.
