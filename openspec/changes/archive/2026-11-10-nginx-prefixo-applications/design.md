## Context

O `nginx.conf` do stack (`apps/frontend/deploy/nginx.conf`) decide por regex de prefixo se a requisição vai ao
`apps/api`, ao `apps/inbox`, ao `apps/connectors` ou ao shell do SPA. O que não casa com nenhum bloco cai no
fallback de SPA (`location /`, `try_files $uri /index.html`) e recebe `200` com HTML.

### O código em `8013521` (main depois do PR #267)

- **Bloco do `apps/api`**, l. 70:
  `^/(agents|providers|mcp-servers|knowledge-bases|knowledge-index|insights|auth|account|users|identity)(/|$)`,
  com o desvio de navegação (`if ($http_sec_fetch_mode = navigate) { rewrite ^ /index.html last; }`) e
  `set $api_upstream api:8080`.
- **Blocos exatos do emissor:** `= /.well-known/openid-configuration` e `= /.well-known/jwks` (l. 91–105),
  `= /connect/authorize` e `= /connect/logout` (l. 112–126), sem o desvio. O resto de `/connect` vai pela regex
  `^/(connect)(/|$)`, com o desvio (l. 130–139).
- **`apps/inbox`:** `^/(channels|contacts|sessions|messages|webhooks|internal)(/|$)`, `$inbox_upstream inbox:8080`.
- **`apps/connectors`:** `^/(connectors)(/|$)`, `$connectors_upstream connectors:8080`.
- **O guarda de hoje** é o comentário do topo (l. 12–31): três `grep` dos literais `"/…"` nos fontes, mais um
  parágrafo de ruído conhecido a descartar à mão. Nenhum teste lê os prefixos do `nginx.conf`. O único teste que
  lê o arquivo é o `apps/frontend/src/app/nginxHeaders.test.ts`, e ele confere só os cabeçalhos.

**Os primeiros segmentos que cada host mapeia**, pelos `Map*` do `Program.cs` e dos grupos:

| app | servidos | no nginx | diferença |
|---|---|---|---|
| `apps/api` | `health`, `auth`, `connect`, `account`, `users`, `applications`, `identity`, `agents`, `providers`, `mcp-servers`, `knowledge-bases`, `sync`, `knowledge-index`, `insights` | os 10 da regex, mais `connect` | falta `applications` (o defeito); `health` e `sync` de propósito |
| `apps/inbox` | `health`, `channels`, `contacts`, `sessions`, `messages`, `webhooks`, `internal` | os 6 da regex | `health` de propósito |
| `apps/connectors` | `health`, `connectors` | `connectors` | `health` de propósito |

O `/.well-known/openid-configuration`, o `/.well-known/jwks` e o `/connect/token` não estão na tabela: o
OpenIddict os atende dentro do `UseAuthentication`, sem rota mapeada (`IdentityIssuerEndpoints`, l. 5–10). O
`/connect/authorize` e o `/connect/logout` são passthrough, com rota mapeada (`AuthorizeEndpoint`,
`EndSessionEndpoint`), e aparecem como `connect`.

**O que já lê os endpoints do host construído:** `RouteDeclarationValidationTests.RealComposition_…`
(`apps/api`) e `RoleMatrixTests.RealComposition_EveryAuthenticatedRouteIsDeclared` (`apps/inbox`) pegam a
`EndpointDataSource` dos `Services` da fixture. As três suítes têm fixture de host:
- `ApiFactoryFixture`, com Postgres por Testcontainers;
- `InboxFactoryFixture`, idem;
- `ConnectorsFactory`, sem banco.

### O caminho até o defeito

A spec `server-deployment` já dizia (requisito "Nginx do stack roteia por prefixo") que acrescentar prefixo a um
app "SHALL incluir a entrada correspondente nesta configuração, ainda que a change que cria a rota não toque
nenhum outro arquivo de `apps/frontend`". A #186 tocou só o `apps/api`, e a regra dependia de alguém lembrar do
`grep`. A #188 fez a tela, testada contra um `fetch` falso, e o painel local fala direto com o `apps/api`
(`VITE_API_BASE_URL`), sem passar por nginx. Só a homologação passa pelo nginx.

## Goals / Non-Goals

**Goals:**
- `/applications` chega ao `apps/api`, e a navegação em `/applications/*` continua no shell do SPA;
- uma rota nova sem prefixo reprova na suíte do app que a serve, e não na homologação;
- um prefixo do nginx que nenhum app serve reprova também;
- as exceções viram lista nomeada, com motivo, e uma exceção que deixou de valer reprova.

**Non-Goals:**
- trocar a regra de roteamento (regex por prefixo, desvio por `Sec-Fetch-Mode`);
- conferir o roteamento das sub-rotas: o nginx decide pelo primeiro segmento;
- rodar o nginx no teste: a configuração é conferida pelo texto, e de verdade pelos `curl` da conferência (D5),
  como no `nginxHeaders.test.ts`;
- expor `/sync` no domínio público;
- o `resolver 127.0.0.11` que não resolve sob o Podman (o contorno da #212, só na conferência).

## Decisions

### D1. A correção: `applications` na regex do bloco do `apps/api`

`applications` entra na lista do bloco que já existe, depois de `identity`. O bloco já tem o desvio de
navegação, então `/applications/{id}` aberto no browser (rota de página do painel, desde a #188) continua no
shell. A conferência é a do D5.

**Alternativa descartada:** um bloco próprio para `/applications`. Repetiria o corpo do bloco do `apps/api` sem
nenhuma diferença de comportamento.

### D2. Onde o guarda mora: um teste em cada uma das três suítes, sobre o host do próprio app

Cada um dos três projetos de teste ganha um `NginxRouteCoverageTests` que:
1. lê os primeiros segmentos de todo `RouteEndpoint` da `EndpointDataSource` da fixture de host da suíte;
2. lê o `nginx.conf` pelo caminho do repositório;
3. compara os primeiros segmentos com os prefixos dos blocos de regex cujo upstream é o do app (`api`, `inbox`
   ou `connectors`, o host do `set $<x>_upstream <host>:8080`), nos dois sentidos.

**Por que três testes, e não um só para os três:**
- **A suíte que reprova é a de quem errou.** Quem cria a rota no `apps/inbox` roda a suíte do `apps/inbox`. Um
  teste só, num projeto de `tests/`, ficaria numa suíte que a change da rota não tem motivo para rodar (a #186 rodou
  `api`, `rt-inbox`, `rt-crossapp` e `rt-connectors`; um `rt-nginx` não estaria na lista).
- **Um teste só teria de subir os três hosts num projeto.** Seria referenciar os três `Program` e as três
  fixtures, com os dois Postgres. É o custo dos acordos de `tests/`, que existem para o que cruza apps. A
  cobertura do nginx não cruza: cada bloco é de um app.
- **O "prefixo que nenhum app serve" fecha por bloco.** Cada bloco de regex encaminha para um upstream só. Se o
  teste de cada app confere os blocos dele nos dois sentidos, e cada um dos três confere que todo bloco que
  encaminha tem um dos três upstreams conhecidos, nenhum prefixo do arquivo fica sem dono conferido.

**A fonte das rotas é o host construído, não os fontes.** Os `grep` de hoje leem literais, com ruído (sub-rotas de
grupo, chamadas de saída) e sem as constantes (`GroupPath`, `RoutePattern`, `SyncNowPattern`): o `applications`
estava em `ExternalApplicationEndpoints.GroupPath`, que o `grep '"/[a-z0-9-]+'` acha só porque a constante é um
literal. A `EndpointDataSource` tem o padrão final de cada rota, depois dos grupos, e é a mesma fonte das
checagens de boot da convenção 8.

**A fixture: uma collection com a classe que já lê a composição, e não um `IClassFixture` próprio** (decidido na
revisão do dono, antes do apply). As duas suítes só usam `IClassFixture`, e o `IClassFixture` é por classe: uma
classe nova com `IClassFixture<ApiFactoryFixture>` subiria um Postgres a mais só para ler a `EndpointDataSource`.
No lugar:
- `apps/api`: a collection `ApiHostCompositionCollection` (`ICollectionFixture<ApiFactoryFixture>`), com o
  `NginxRouteCoverageTests` e o `RouteDeclarationValidationTests`, que já lia a composição real e também não
  toca no banco;
- `apps/inbox`: a `InboxHostCompositionCollection` (`ICollectionFixture<InboxFactoryFixture>`), com o
  `NginxRouteCoverageTests` e o `RoleMatrixTests`;
- `apps/connectors`: sem banco, a `ConnectorsFactory` é construída no próprio teste, como no `RoutePatternTests`.

**O teste não fica acoplado a estado de banco.** Ele não lê nem grava nada no banco: o contêiner existe só porque
o host não sobe sem ele. A classe que divide a fixture com ele não depende do que ele faz. As duas classes
existentes trocam o `IClassFixture` pelo `[Collection]` e mais nada, sem caso a menos, sem nome mudado e sem
asserção mudada. O preço é que as duas classes de cada collection não rodam em paralelo entre si, e os casos do
teste novo são sub-segundo. Alternativa descartada: pôr os casos dentro do `RouteDeclarationValidationTests`. Divide
o contêiner do mesmo jeito, mas mistura requisitos de duas specs (`route-roles` e `server-deployment`) numa classe.

**O caminho do `nginx.conf`:** sobe de `AppContext.BaseDirectory` até a pasta que tem
`apps/frontend/deploy/nginx.conf`. Não achar é falha do teste, com o caminho de onde partiu: nunca "pulado".

### D3. O leitor do `nginx.conf`: um arquivo em `tests/Shared/`, compilado nos três por link

```
tests/
  Shared/
    NginxRouting.cs                  (novo: lê os blocos location do nginx.conf)
apps/api/tests/Buteco.Api.Tests/
  Buteco.Api.Tests.csproj            (+ <Compile Include="..\..\..\..\tests\Shared\NginxRouting.cs" Link="Support\NginxRouting.cs" />)
  NginxRouteCoverageTests.cs         (novo)
apps/inbox/tests/Buteco.Inbox.Tests/
  Buteco.Inbox.Tests.csproj          (+ a mesma linha)
  NginxRouteCoverageTests.cs         (novo)
apps/connectors/tests/Buteco.Connectors.Tests/
  Buteco.Connectors.Tests.csproj     (+ a mesma linha)
  NginxRouteCoverageTests.cs         (novo)
apps/frontend/deploy/
  nginx.conf                         (a regex e o comentário do topo)
```

O `NginxRouting` devolve cada `location` com:
- o modificador (`~` ou `=`) e o padrão;
- os prefixos, quando o padrão é `^/(a|b|…)(/|$)`;
- o host do upstream, ou nenhum quando o bloco não encaminha;
- se o bloco tem o desvio de navegação.

Ele tira os comentários (`#` até o fim da linha) e acha o fim de cada bloco contando chaves, porque o `if` do
desvio tem as dele. Um bloco de regex que encaminha e não tem a forma `^/(…)(/|$)` faz o leitor falhar, nomeando
o padrão: não vira um bloco de zero prefixos.

**Por que um arquivo linkado, e não três cópias nem `libs/`:**
- **Três cópias** derivariam. Uma correção do leitor (um bloco novo com outra forma, por exemplo) numa cópia
  deixaria as outras duas lendo o mesmo arquivo de outro jeito.
- **`libs/`** é para código de produção compartilhado, pequeno e versionado (`openspec/config.yaml`). O leitor é
  código de teste e não vai a nenhuma imagem.
- **O link já tem precedente:** o `tests/ApiConnectorsRoundTrip.Tests.csproj` compila o `FakeConnector.cs` e o
  `TestIssuer.cs` do projeto de teste do `apps/connectors` pelo mesmo `<Compile Include … Link>`.
- **O isolamento não muda.** A regra é que um app não referencia código interno de outro. O `NginxRouting.cs`
  não é de nenhum app, não referencia nenhum, e lê um arquivo de configuração do repositório. Os projetos de
  teste continuam referenciando só o próprio app.

### D4. As exceções, nomeadas no teste de cada app, com o motivo

| app | exceção | forma | motivo |
|---|---|---|---|
| os três | `health` | servido, sem bloco | os healthchecks rodam dentro de cada contêiner contra o próprio processo e não atravessam o nginx |
| `apps/api` | `sync` | servido, sem bloco | só o `apps/connectors` chama, pela rede interna do compose (requisito "apps/api e apps/connectors se alcançam pela rede interna do compose"); expor seria abrir uma rota de serviço ao domínio público sem quem a use |
| `apps/api` | `= /.well-known/openid-configuration`, `= /.well-known/jwks` | bloco exato, sem rota mapeada | o OpenIddict os atende fora do roteamento; `/.well-known/` é prefixo de uso geral na web (ACME, `security.txt`), e só as duas URIs vão ao `apps/api` |
| `apps/api` | `= /connect/authorize`, `= /connect/logout` | bloco exato, rota passthrough | navegação de browser por definição, e por isso sem o desvio de navegação; o `location =` vence a regex de `connect` |

**Cada exceção reprova quando deixa de valer:**
- uma exceção "servido, sem bloco" que o host não serve mais reprova, nomeando a exceção;
- uma exceção "servido, sem bloco" que ganhou bloco reprova também: está nos dois lados ao mesmo tempo;
- um bloco exato da lista que saiu do `nginx.conf` reprova;
- um bloco exato que encaminha para o app e não está na lista reprova, nomeando o padrão.

**Os endpoints do emissor também são conferidos:** toda URI de `IdentityIssuerEndpoints.Expected` (as cinco)
tem de chegar ao `apps/api` por um bloco exato da lista ou pelo prefixo de um bloco de regex do `apps/api`. Hoje
o `/connect/token` chega pela regex `connect`.

**Tirar uma exceção da lista reprova pelo outro lado.** Sem `sync` na lista, o `sync` servido fica sem bloco.
Sem `= /.well-known/jwks`, o bloco exato fica fora da lista. É o guarda pedido para as exceções (4.4 do tasks).

**As outras checagens do teste de cada app:**
- todo bloco de regex que encaminha para o app tem o desvio de navegação (requisito "Todo bloco de prefixo de API
  SHALL aplicar o mesmo desvio");
- nenhum bloco exato da lista o tem;
- nenhum prefixo está em dois blocos de regex: o nginx usa a primeira regex que casa, e o segundo bloco seria
  código morto;
- todo bloco que encaminha usa um dos três upstreams conhecidos.

Toda divergência entra numa lista, e o teste falha uma vez com todas, como a `RouteDeclarationValidation`. A
mensagem nomeia o prefixo, o padrão ou a exceção.

**Primeiro segmento que não é literal** (uma rota `"/{id}"` na raiz) reprova, nomeando a rota: o nginx não teria
como rotear por prefixo.

### D5. A conferência com o nginx de verdade

- **`nginx -t` da imagem:** `podman build` do `apps/frontend/Dockerfile` e `nginx -t` dentro dela. Sem a rede do
  compose, o `nginx -t` passa, porque o upstream em variável não é resolvido no carregamento.
- **Os `curl`:** o `apps/api` local (`dotnet run`, `TZ=America/Sao_Paulo`, o Postgres do compose de dev) e a
  imagem do frontend numa rede do Podman. Na mesma rede, um contêiner com o nome `api` encaminha a porta `8080`
  ao `apps/api` do host. O `resolver` é trocado só dentro do contêiner do frontend para o `nameserver` do
  `resolv.conf` dele, com `nginx -t` e `reload` (o contorno da #212, como na conferência da change
  painel-entra-pelo-emissor), e a troca fica registrada.
- **Os pedidos:**
  - `GET /applications/` com o token de Admin, sem `Sec-Fetch-Mode` → `200`, `application/json`, o corpo do
    `apps/api`;
  - `GET /applications/` sem token → `401` do `apps/api`;
  - a mesma URL com `Sec-Fetch-Mode: navigate` → `200`, `text/html`, `Cache-Control: no-store`, o `index.html`;
  - `GET /applicationsxyz` → o `index.html` do fallback.
- **O contraste com a imagem de antes:** a imagem construída da baseline (`8013521`) responde ao mesmo
  `GET /applications/` com token `200` e `text/html`, o defeito da homologação. É o que prova que o `curl` mede a
  correção, e não o ambiente.
- **O token:** de um Admin do banco local, pelo mesmo caminho da conferência da #188.

### D6. A ordem: o teste antes da correção

O teste do `apps/api` entra antes da regex e reprova na árvore sem a correção, nomeando `applications`. A
correção vem depois, e o mesmo teste aprova. A reprovação registrada é o guarda do defeito real (2.2 do tasks), e não uma
mutação montada: o defeito é o do `main`.

## Risks / Trade-offs

- **[O leitor entende só a forma de hoje do `nginx.conf`]** → falha alto. Um bloco de regex fora de
  `^/(…)(/|$)` faz o leitor reprovar, nomeando o padrão, e não ser pulado. Quem mudar a forma muda o leitor junto.
- **[A fixture do `apps/api` e a do `apps/inbox` sobem Postgres]** → o teste entra numa collection com uma classe
  que já subia a fixture (D2), e não sobe contêiner próprio. A rodada final registra, contra a baseline, a duração
  das duas suítes.
- **[`tests/Shared/` é pasta nova]** → é um arquivo, sem projeto, e o motivo fica no D3 e no comentário do
  arquivo. Se aparecer um segundo arquivo, o lugar já está decidido.
- **[O `nginx.conf` mora no `apps/frontend`, e o teste nas suítes dos backends]** → é o que a spec já dizia
  ("conferência de escopo restrita aos arquivos do app que serve a rota SHALL NOT ser tratada como suficiente").
  O teste põe essa frase na suíte de quem serve a rota.
- **[O `/sync` exposto por engano seria verde no teste?]** → não. O `sync` está na lista de exceções, e a
  exceção que ganhou bloco reprova por estar nos dois lados (D4).

## Migration Plan

Só a imagem do `frontend` muda. O deploy pode ir sozinho, a qualquer momento depois do PR #267, porque o
`apps/api` já serve `/applications`. A correção manual da homologação (o `sed` no contêiner) é substituída
pela imagem nova, sem passo extra. Volta atrás: a imagem do `frontend` anterior, que traz o defeito de volta.

## Open Questions

Nenhuma.
