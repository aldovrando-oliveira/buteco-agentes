## Context

Motivação e os dois defeitos (R1 e R2) estão no `proposal.md`. Aqui ficam o
mecanismo, as decisões e a verificação.

### O mecanismo do R2, medido

```
 1. refresh / link direto em /agents          (Sec-Fetch-Mode: navigate)
        │
        ▼
    nginx: if navigate → rewrite /index.html → 200 text/html
           sem Cache-Control, sem Vary, com Last-Modified
        │
        ▼
    browser grava o HTML sob a chave  GET https://…/agents
    (cacheável por heurística: 10% de Date − Last-Modified)
        │
 2. SPA carrega, fetch('/agents')              (Sec-Fetch-Mode: cors)
        │
        ▼
    browser acha a entrada ainda válida → 200 OK (from disk cache), HTML
    → response.json() falha → "Não foi possível carregar os agentes."
    a requisição nunca chega ao Cloudflare nem ao nginx
```

Medições de 18/09/2026, contra produção:

| Requisição | Resposta | O que prova |
|---|---|---|
| `/agents` com `Sec-Fetch-Mode: cors`, sem token | `401`, `cf-cache-status: DYNAMIC` | O roteamento ao `apps/api` está correto |
| `/`, `/index.html`, `/agents` navigate | `200 text/html`, `last-modified`, **sem** `cache-control`, **sem** `vary`, **sem** `etag`, `DYNAMIC` | O shell é cacheável por heurística, e o Cloudflare não o guarda |
| `/assets/index-*.js` | `etag` mantido (`W/` quando comprimido), `cache-control: max-age=14400` **injetado pelo Cloudflare**, `MISS` | O Cloudflare aplica Browser Cache TTL onde ele próprio guarda o arquivo |
| `Last-Modified 05:38:42` e `Date 06:24:10` | deploy de 45 min, janela heurística de ~4,5 min | A janela cresce com a idade do deploy: ~2,4 h com 1 dia |

### Estado atual do arquivo

```
apps/frontend/deploy/nginx.conf
  location ~ ^/(agents|providers|mcp-servers|knowledge-bases|knowledge-index|auth)(/|$)  → apps/api   (+ if navigate → rewrite /index.html)
  location ~ ^/(channels|contacts|sessions|webhooks|internal)(/|$)                        → apps/inbox (+ if navigate → rewrite /index.html)
  location /  { try_files $uri /index.html; }                                              → estáticos + fallback de SPA
```

### Árvore de arquivos tocados

```
buteco-agents/
├── apps/frontend/deploy/
│   └── nginx.conf                       ← único arquivo de configuração alterado
├── openspec/changes/nginx-shell-sem-cache-e-prefixo-messages/
│   ├── proposal.md
│   ├── design.md
│   ├── specs/server-deployment/spec.md  ← delta: 1 MODIFIED, 1 ADDED
│   └── tasks.md
└── 02-HISTORICO_E_STATUS.md             ← registro em "Itens em aberto"
```

Nada em `libs/`. Nenhum outro app é tocado.

## Goals / Non-Goals

**Goals:**

- `/messages/summary` chegar ao `apps/inbox` (R1).
- O shell do SPA deixar de ser guardado pelo browser, em todos os caminhos que o
  entregam, sem tocar no cache dos assets (R2).
- Deixar escrito o que só se mede depois do deploy, e a contingência caso a
  medição reprove (V3), antes de aplicar.

**Non-Goals:** ver `proposal.md`. O truque do `Sec-Fetch-Mode` e a lista de
prefixos mantida à mão continuam existindo.

## Decisions

### D1: alvo do `add_header` é `location = /index.html`, nunca `location /`

`rewrite ^ /index.html last` (navegação em prefixo de API) e
`try_files $uri /index.html` (rota profunda) **reentram no roteamento** do nginx.
Sem um bloco próprio, os dois caem em `location /`, que também serve `/assets/*`.
Colocar o header em `location /` mataria o cache dos assets com hash de conteúdo,
justamente os que devem ser cacheados para sempre.

Uma `location` exata tem precedência sobre as de prefixo e regex. Toda
reentrada com URI `/index.html` cai nela, e nada mais cai.

**Testado em nginx descartável** (`nginx:1.30-alpine`, a mesma imagem do
Dockerfile, com o `dist/` do build e upstreams falsos `api`/`inbox`), comparando
a configuração atual com a candidata:

| Caminho | Requisição | Atual | Candidata |
|---|---|---|---|
| diretiva `index` | `/`, `/index.html` (navigate) | html, sem `Cache-Control` | html, `no-store` |
| rewrite de navegação em prefixo de API | `/agents`, `/agents/123` (navigate) | html, sem `Cache-Control` | html, `no-store` |
| `try_files` de rota profunda | `/inventory`, `/agents-ui/123` (navigate) | html, sem `Cache-Control` | html, `no-store` |
| fetch em prefixo de API | `/agents` (cors) | JSON do upstream `api` | JSON do upstream `api`, sem mudança |
| **prefixo novo (R1)** | `/messages/summary` (cors) | **html (fallback)** | **JSON do upstream `inbox`** |
| assets | `/assets/index-*.js`, `/favicon.svg` | sem `Cache-Control` | **sem `Cache-Control`, sem mudança** |

`/inventory` é rota real do `react-router`
(`apps/frontend/src/app/routes.tsx:44`, `<Route path="inventory">`). A raiz
**redireciona** para ela e não é o Inventário (comentário em `routes.tsx:39-42`).
Conferido na revisão: a linha da tabela vale como está.

`nginx -t` aprova a candidata. **Alternativa recusada:** `add_header` em
`location /` condicionado por `map $uri`. Funciona, mas espalha a regra num
`map` separado para cobrir um único arquivo que tem nome fixo.

### D2: `no-store` em vez de `no-cache`

As duas resolvem o R2:

- **`no-cache`**: o browser guarda, mas revalida antes de reusar. A revalidação
  de `/agents` feita pelo `fetch()` sai com `Sec-Fetch-Mode: cors`, vai ao
  `apps/api`, e o `200` com JSON substitui a entrada errada. Preserva o bfcache.
- **`no-store`**: o browser não guarda o shell em momento algum.

**Escolha: `no-store`.** A garantia é mais forte, porque não depende de como cada
browser trata revalidação de entrada gravada por outro modo de requisição. E a
change do `/painel/` remove o mecanismo inteiro depois, junto com esta decisão.

**Custos aceitos, os dois:**

1. **~1,4 KB por navegação.** O `index.html` tem 1396 bytes na origem e 1763 com
   o beacon injetado pelo Cloudflare.
2. **`no-store` no documento principal desabilita o bfcache no Chrome.** Voltar
   ao painel pelo botão "voltar" recarrega a página em vez de restaurá-la. Num
   SPA isso só aparece ao **sair do site e voltar**. A navegação interna do
   `react-router` não passa pelo bfcache.

**Alternativa recusada: `no-cache`.** Ela preservaria o bfcache e resolveria o
caso medido. Foi recusada pela garantia mais fraca, num hotfix de piloto cujo
mecanismo já tem data para sair.

### D3: **não** declarar `Vary: Sec-Fetch-Mode`

É a declaração honesta de um recurso que varia por header, e custa uma linha.
Foi recusada por quatro razões:

1. **É redundante com `no-store`.** O shell nunca é guardado, e as respostas da
   API também não, porque não têm validador (ver "Fora de escopo com gatilho").
2. **O Cloudflare ignora `Vary`** que não seja `Accept-Encoding`. Não protegeria
   a borda se ela um dia passasse a guardar HTML.
3. **A change do `/painel/` remove o truque** que a torna necessária. Declará-la
   agora cria uma linha para remover depois.
4. **Estaria no lugar errado se fosse no `index.html`.** O `index.html` também
   atende `/`, que não varia por header. Se um dia a declaração existir, o lugar
   dela são os **dois blocos de proxy**, com `always`, para cobrir também as
   respostas `4xx`.

### D4: o ETag do HTML e a reescrita de HTML na borda

O Cloudflare remove o `ETag` de **todo** `text/html`. Medido:

- Com `curl` simples, o corpo chega **byte a byte idêntico** à origem: 1396
  bytes, que em hexa é 0x574, o próprio sufixo do ETag de origem (`"…-574"`).
  Não há beacon injetado, e mesmo assim o `ETag` some.
- Com `User-Agent` e `Accept` de browser, o beacon do Cloudflare Insights é
  injetado no `<head>` e o corpo passa a 1763 bytes. O `ETag` também some.
- Nos assets o `ETag` é mantido.

**Conclusão:** não é a injeção em si que tira o validador. A zona tem uma
**reescrita de HTML ativa**, e todo `text/html` passa por ela, tenha havido
reescrita ou não. A candidata visível é a injeção automática do **Web
Analytics**. **Email Obfuscation**, se estiver ligado, teria o mesmo efeito.
Desligar uma das duas **não** devolve o `ETag` se a outra estiver ativa. Isso
não foi medido e exige olhar o painel do Cloudflare.

**Consequência para esta change:** com `no-store`, o validador deixa de
importar para o R2. Mas o `Cache-Control` da origem **também atravessa esse
processamento**, e as requisições de browser seguem um caminho diferente das de
`curl` na borda. Por isso a verificação pós-deploy (V2) mede das **duas** formas.

### D5: nota de ruído nos greps de conferência do comentário

Os dois greps que o comentário do `nginx.conf` manda rodar devolvem, além dos
prefixos reais, quatro falsos positivos. A proposta listava três; a
reenumeração da tarefa 1.1 achou o quarto (última linha):

| Devolvido | O que é | Por que não é prefixo |
|---|---|---|
| `/test` | `MapPost("/test", …)` em `McpServerEndpoints.cs:32` | sub-rota de grupo (`/mcp-servers/test`) |
| `/indexing-summary` | `MapGet("/indexing-summary", …)` em `KnowledgeBaseEndpoints.cs:33` | sub-rota de grupo (`/knowledge-bases/indexing-summary`) |
| `/api` | `PostAsJsonAsync("/api/sendText", …)` em `WahaOutboundMessageSender.cs:23` | chamada de **saída** para o WAHA, não rota servida |
| `/agents` (no grep do **inbox**) | `client.GetAsync($"/agents/{agentId}")` em `AgentReferenceValidator.cs:27` | chamada de **saída** do `apps/inbox` para o `apps/api` (validação de referência de agente), não rota servida pelo inbox |

O quarto é o mais enganoso dos quatro. `agents` **é** prefixo real, só que do
outro app. Uma leitura que confere "o literal está em algum bloco?" o aprova
sem perceber que ele apareceu no grep errado. Por isso a nota do comentário
manda conferir a linha de código do literal (`MapGet`/`MapPost`/`MapGroup` ou
chamada de cliente HTTP) antes de descartar.

A nota vai no próprio comentário, ao lado dos greps, para quem conferir a lista
não repetir o trabalho de descartá-los. A regra de derivar a lista enumerando, e
não de memória, continua. A nota só a torna mais barata de seguir.

### Testes

Nenhuma suíte de `apps/api`, `apps/inbox`, `apps/workers` ou `apps/frontend` lê o
`nginx.conf`. Isso foi confirmado na mesma situação por `fix-stack-servidor-lacunas`
(tarefa 0.1). Não há teste unitário a escrever nem baseline a medir. O que faz o
papel de teste é:

1. o **nginx descartável** (D1), repetido sobre o arquivo editado **antes** do
   deploy, com a mesma tabela;
2. a **verificação pós-deploy** V2, contra o stack real.

A falta de uma checagem automatizada da lista de prefixos é justamente o item
"lista mantida à mão" de "Fora de escopo com gatilho". Não é omissão desta change.

## Verificação

### V1: o deploy é só do frontend

```
docker compose -f docker-compose.prod.yml --env-file .env.prod build frontend
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d frontend
```

A sequência de `docs/deployment.md` §2 (parar o `apps/inbox` antes do `migrator`)
existe por causa do índice único parcial de `Session`, e **não se aplica aqui**:
não há migration. Parar o inbox num piloto sem necessidade é risco gratuito.

Hoje o §2 só descreve essa sequência. Quem seguir o texto como está, num deploy
só do frontend, para o `apps/inbox` sem necessidade. Por isso esta change
acrescenta ao §2 o caso "só o frontend" (tarefa 3.4), com a verificação da V2 e
a contingência da V3.

### Onde a verificação pós-deploy é executada

V1, V2 e V3 são o **desenho** da verificação, e este é o lugar delas. A
**execução** não é tarefa desta change. O deploy acontece depois do archive e do
merge na `main`, e uma tarefa que dependesse dele não fecharia antes do
archive. Seria a mesma forma da 2.4 de `fix-stack-servidor-lacunas`, arquivada
aberta e deixada por meses. A execução vive em dois lugares:

- **Procedimento:** `docs/deployment.md` §2, "Redeploy só do frontend" →
  "Verificação depois do redeploy do frontend". Tem os comandos exatos, o
  critério de cada linha e a contingência. Vale para **qualquer** redeploy só do
  frontend, não só para este.
- **Obrigação:** `02-HISTORICO_E_STATUS.md`, "Abertos por
  `nginx-shell-sem-cache-e-prefixo-messages`", com o comando, o critério de
  aprovação e o que fazer se reprovar. Depois do archive, este `design.md` é
  imutável, e o `02` é o **único** lugar onde a verificação pendente é
  acionável.

**Dimensionamento do resíduo.** A tarefa 2.1 rodou 20 requisições contra o
arquivo editado, em nginx real: os três caminhos do shell, os dois upstreams e os
assets. Isso cobre todo o comportamento do nginx. Produção acrescenta **uma**
pergunta: se o `Cache-Control` da origem sobrevive ao Cloudflare. O efeito no
browser não discrimina logo depois do deploy (V2), a contingência está escrita
(V3), e o R1 não depende disso.

### V2: pós-deploy, com curl simples e com cabeçalhos de browser

O HTML segue caminhos diferentes na borda conforme a requisição (D4). Então cada
checagem do shell roda **nas duas formas**:

```
H=https://agente.butecandoespetobar.com.br
UA='Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36'

for p in / /index.html /agents /inventory; do
  curl -sI "$H$p" -H 'Sec-Fetch-Mode: navigate'                                             | grep -i '^cache-control'
  curl -sI "$H$p" -H 'Sec-Fetch-Mode: navigate' -H "User-Agent: $UA" -H 'Accept: text/html' | grep -i '^cache-control'
done
```

| Checagem | Esperado |
|---|---|
| as 8 linhas acima | `cache-control: no-store` em todas |
| `curl -sI $H/assets/<js atual>` | `cache-control: max-age=14400`, sem mudança |
| `curl -s -o /dev/null -w '%{http_code} %{content_type}' -H 'Sec-Fetch-Mode: cors' $H/messages/summary` | `401`, sem `text/html` (R1) |
| browser: login → Inventário | os seis cards carregam (Agentes, Servidores MCP, Bases de conhecimento, Canais, Sessões iniciadas, Mensagens recebidas) |
| browser: `/agents` | lista os agentes |
| browser: F5 em `/agents`, depois um segundo acesso | a requisição `fetch` a `/agents` **sem** `(from disk cache)` no DevTools; o documento com `Cache-Control: no-store` em Response Headers |

A linha do R1 é independente das demais. Se só ela aprovar, o R1 está fechado,
seja qual for o resultado do R2.

**O que cada checagem prova no R2.** As duas famílias de checagem provam coisas
diferentes, e não se confundem:

- **O header no `curl` é a verificação do R2.** É determinística: aprova com o
  patch e reprova sem ele, em qualquer momento.
- **As checagens no browser confirmam que o painel voltou a funcionar. Elas
  _não_ provam que o R2 foi corrigido.** Rodam minutos depois do deploy e
  passariam igual no nginx **sem** o patch. O browser calcula a janela de reuso
  da cópia no momento em que a grava: 10% de `Date − Last-Modified`, e o
  `Last-Modified` do `index.html` é a hora do build. Com o deploy recém-feito,
  essa janela nasce praticamente zero. A cópia gravada pelo F5 já está vencida
  quando o `fetch()` a encontra. O browser então manda uma requisição
  condicional, que sai com `Sec-Fetch-Mode: cors`, vai ao `apps/api` e volta com
  JSON. O defeito se cura sozinho e não aparece.

A verificação empírica do efeito no browser só discrimina corrigido de não
corrigido com **deploy envelhecido**. Com mais de 1 dia de deploy, a janela é de
~2,4 h. Esse é o cenário marcado assim na spec. Ele não é pré-condição para
fechar a change. Se o mantenedor quiser a prova empírica, ela se faz depois de
um dia de deploy: F5 em `/agents` e `fetch` sem `(from disk cache)`.

**Ordem no browser.** Antes de julgar as linhas de browser, o browser de quem
verifica precisa estar livre de cópia errada gravada antes do deploy (V5), ou
então usar perfil limpo, janela anônima ou "Disable cache". Senão uma entrada
antiga reprova a checagem, e a change certa parece errada.

### V3: contingência se o `no-store` não sobreviver ao Cloudflare

Isto é contingência preparada, não pendência. Está escrito **antes** de aplicar
para não virar improviso em produção.

- **Indício favorável, medido:** o Cloudflare só injeta `max-age` onde ele
  próprio guarda o arquivo (os assets, `MISS`/`HIT`). No HTML, que ele não
  guarda (`DYNAMIC`), não injeta nada hoje, e `last-modified`/`accept-ranges` da
  origem passam sem mudança.
- **O que não foi medido:** nenhuma resposta da origem traz `Cache-Control` hoje.
  Não existe um caso real de header de origem atravessando a borda no HTML.

**Se a V2 mostrar o `no-store` ausente ou trocado:**

1. A causa provável é o **Browser Cache TTL** da zona em valor fixo, em vez de
   *"Respect Existing Headers"*. A segunda candidata é a reescrita de HTML (D4)
   descartando o header.
2. A correção é **configuração de borda, fora do repositório**: uma Cache Rule de
   exceção para `text/html` (ou para o hostname), preservando o `Cache-Control`
   da origem. Nada nesta change é revertido: o header na origem continua correto
   e passa a valer assim que a borda o respeitar.
3. Repetir a V2 inteira depois de aplicar a regra.
4. Registrar no `02` a configuração de borda aplicada. É estado do ambiente que o
   repositório não versiona e que a change do `/painel/` precisa conhecer.

Enquanto a V3 estiver aberta, o R1 continua valendo. O R2 fica mitigado só pela
recuperação manual (V5).

### V4: pendência antiga que esta change fecha

`fix-stack-servidor-lacunas`, tarefa 2.4 (e o item correspondente em
`02-HISTORICO_E_STATUS.md`): *"subir o stack e confirmar que cada prefixo responde
JSON em vez de 200 com HTML"*.

**Medido em 18/09/2026, contra o stack real, sem token, `Sec-Fetch-Mode: cors`:**

| Prefixo | Status | Veredito |
|---|---|---|
| `/agents`, `/providers`, `/mcp-servers`, `/knowledge-bases`, `/knowledge-index`, `/auth/login` | 401 | chega ao backend |
| `/channels`, `/contacts`, `/sessions`, `/internal/push-notifications`, `/webhooks/<guid>` | 401 | chega ao backend |
| `/messages/summary` | **200 `text/html`** | **reprovado**: é o R1 |

O `401` uniforme sozinho não diz **qual** app respondeu: os dois apps usam a
mesma `FallbackPolicy`, que também responde `401` a método não mapeado. Por
isso cada upstream foi atribuído **por comportamento que só ele tem**, sem
token, sem corpo e sem efeito colateral, repetido 4 vezes:

| Requisição | Resposta | Atribuição |
|---|---|---|
| `GET /agents/<guid>/.well-known/agent-card.json` (rota anônima) | 404 | `apps/api` |
| `POST /auth/login`, corpo vazio | 400 | `apps/api` |
| `POST /webhooks/<guid inventado>`, corpo vazio | 404 (canal inexistente) | `apps/inbox` |
| `POST /internal/push-notifications`, corpo vazio | 400 | `apps/inbox` |

**Fecha:** o roteamento dos três prefixos de `fix-stack-servidor-lacunas`
(`internal`, `knowledge-bases`, `knowledge-index`), com `internal` confirmado
chegando ao inbox. Era o defeito que fazia a task completar sem nada sair no
canal.

**Fecha também:** a outra metade da 2.4, o round-trip de push notification com a
resposta **saindo no canal**.

**Evidência (observação em produção, não teste automatizado):** em 18/09/2026, o
mantenedor observou respostas de agente chegando ao canal **em produção**, nos
**dois** canais, WAHA e Telegram. É observação direta do round-trip, não
indício. A resposta só sai no canal se o push notification completar. E ele sai
de `apps/workers` para o domínio público e **volta** pelo Cloudflare e pelo nginx
até `/internal/push-notifications`: é o ir-e-volta que `docs/deployment.md`
registra como item futuro ("`PublicUrl__BaseUrl` de `apps/inbox` com semântica
dupla"). Não há caminho alternativo por onde a resposta pudesse chegar.

**O que a evidência não garante:** ela é um ponto no tempo. O caminho continua
**sem cobertura automatizada**, e uma regressão futura (por exemplo, `internal`
sumindo da enumeração) passaria despercebida do mesmo jeito que a original, até
alguém notar que as respostas pararam de chegar. Essa lacuna é o item "lista de
prefixos mantida à mão" de "Fora de escopo, com gatilho escrito".

### V5: recuperação de quem já tem a cópia errada

O `no-store` impede **novas** gravações. Ele **não apaga** as que já existem.
Em ordem de custo:

1. **F5 em `/agents` depois do deploy.** Espera-se que a resposta `no-store`
   descarte a entrada guardada. **Hipótese não medida.**
2. **Clear site data** (DevTools → Application → Storage). O custo é baixo: o
   token vive em `sessionStorage` (`apps/frontend/src/auth/token.ts:6-19`) e já
   some ao fechar a aba. O `localStorage` só guarda a preferência de tema do
   Mantine (`mantine-color-scheme-value`). Custa um login e a preferência de tema.
3. **Não fazer nada.** A entrada expira sozinha dentro da janela heurística dela.

**Fechar a aba não resolve.** A cópia errada está no cache HTTP do browser, que
não depende de aba nem de `sessionStorage`.

## Risks / Trade-offs

- **[`no-store` não sobrevive ao Cloudflare]** → a V2 detecta com a checagem nas
  duas formas; a V3 tem a correção preparada. O R1 não é afetado.
- **[Header pega os assets por engano]** → o alvo é `location = /index.html`
  (D1), com a tabela repetida sobre o arquivo editado antes do deploy e a
  checagem de assets na V2.
- **[A exact location muda o que `/index.html` serve]** → o bloco não declara
  `root` nem `try_files` próprios, então herda o `root` do `server` e serve o
  mesmo arquivo. Testado: `/` e `/index.html` devolvem o mesmo corpo.
- **[bfcache desabilitado no documento principal]** → custo aceito em D2, visível
  só ao sair do site e voltar.
- **[Operadores com a cópia errada continuam vendo erro depois do deploy]** → V5.
  O `02` registra o procedimento, e o operador do piloto é avisado junto com o
  deploy, **antes** da verificação no browser.
- **[A verificação no browser é tomada como prova do R2]** → ela não é: passa
  igual sem o patch num deploy recente (V2). A prova do R2 é o header.
- **[Quinta ocorrência de prefixo faltante]** → não mitigado aqui. É o item
  "lista mantida à mão" abaixo, com gatilho já cumprido.

## Migration Plan

1. Aplicar as edições no `nginx.conf` e repetir o nginx descartável (tabela D1)
   sobre o arquivo editado.
2. Sync, archive e commit na branch da change; merge na `main` pelo mantenedor.
3. **Depois do merge, fora desta change:**
   1. deploy só do `frontend` (V1);
   2. V2 por `curl`: header do shell, assets e R1. Se o `no-store` reprovar, V3;
   3. recuperação do browser de quem vai verificar, e aviso ao operador do
      piloto (V5). Perfil limpo ou "Disable cache" também servem;
   4. V2 no browser: confirma que o painel voltou a funcionar, não que o R2 foi
      corrigido.

   O procedimento está no `docs/deployment.md` §2, e a obrigação no `02`.

**Rollback:** `git revert` do commit e repetir a V1. O `nginx.conf` anterior não
tem estado: a volta é imediata e não deixa resíduo. As entradas `no-store` nunca
foram guardadas, então não há cache a limpar na volta.

## Fora de escopo, com gatilho escrito

- **O truque do `Sec-Fetch-Mode`.** A mesma URL continua devolvendo HTML ou JSON
  conforme o header. **Gatilho:** a change do prefixo `/painel/`, já decidida,
  remove o truque e, com ele, a razão do `no-store`. **Essa change precisa saber
  que o `no-store` já está no `nginx.conf`**, para não propô-lo de novo nem
  assumir que está ausente. Ela decide se o mantém: é boa prática para shell de
  SPA mesmo sem o truque.
- **A lista de prefixos mantida à mão.** Quatro ocorrências (`internal`,
  `knowledge-bases`, `knowledge-index`, `messages`) já cumpriram qualquer gatilho
  razoável. A change do `/painel/` **não** resolve isso: ela separa o frontend
  das APIs, mas não o `apps/api` do `apps/inbox`. Change própria candidata: uma
  checagem de integridade **bidirecional** entre os prefixos mapeados pelos apps e
  a lista declarada no `nginx.conf`. Pode ser no molde de
  `ValidateRouteAuthenticationClassification`/`ValidateChannelAdapterRegistrations`
  (falha no startup), ou um guarda em `scripts/` no molde de `check-docs.py`. Tem
  de ter o descarte de ruído do D5 embutido.
- **Respostas de API sem `Cache-Control`.** Hoje isso é seguro **só** porque elas
  também não têm validador: sem `ETag` nem `Last-Modified`, o browser não calcula
  prazo por heurística e não as reusa. **Gatilho explícito:** qualquer change
  futura que adicione `ETag` ou `Last-Modified` a uma resposta de API **reabre o
  envenenamento no sentido inverso**. Uma navegação em `/agents` passaria a
  receber o JSON do cache no lugar do shell. Enquanto o truque do
  `Sec-Fetch-Mode` existir, essa change precisa declarar `Cache-Control` nas
  respostas de API.

## Open Questions

- **Acesso ao painel do Cloudflare.** A V3 e a causa exata do D4 (Web Analytics ×
  Email Obfuscation) exigem ver a configuração da zona. Quem tem esse acesso
  precisa estar disponível no deploy, caso a V2 reprove o R2.
