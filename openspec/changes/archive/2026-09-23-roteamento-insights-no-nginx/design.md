## Context

A `rotas-de-agregacao-sistema` (etapa 3 da linha `metricas-de-operacao`) está
aplicada, arquivada e deployada. `GET /insights/system` está registrada em
`apps/api/src/Buteco.Api/Insights/Endpoints/InsightsEndpoints.cs:34`
(`app.MapGet("/insights/system", …)`), sobe com o app e responde. O que não
acontece é a requisição **chegar** nela a partir do painel.

O diagnóstico está **fechado por execução real em produção** (convenção 6), não
por leitura de código. O passo que o fecha é o quarto da tabela do `proposal.md`:
a mesma rota, chamada **de dentro do container `api`** e sem token, devolve
`401`. Rota que não subiu não devolve `401`; rota não registrada não devolve
`401`; rota registrada e alcançada devolve. Logo o `200 text/html` visto de fora
não sai do `apps/api` — sai do fallback de SPA do nginx, para um caminho que ele
não repassa.

### Estado atual do arquivo

`apps/frontend/deploy/nginx.conf`, 110 linhas, das quais 49 são comentário de
mecanismo. A linha que decide:

```
59:    location ~ ^/(agents|providers|mcp-servers|knowledge-bases|knowledge-index|auth)(/|$) {
```

Seis prefixos. O `apps/api` serve sete. O sétimo é `insights`.

O bloco em que `insights` entra **já traz**, hoje:

- o `if ($http_sec_fetch_mode = navigate) { rewrite ^ /index.html last; }`,
  que manda navegação real de documento para o shell do SPA;
- `set $api_upstream api:8080` com `resolver 127.0.0.11`, que adia a resolução de
  DNS para cada requisição em vez do carregamento da config;
- os três headers de proxy (`Host`, `X-Real-IP`, `X-Forwarded-For`).

Nada disso muda, e é por isso que a correção é de uma palavra e não de um bloco.

### Árvore de arquivos tocados

```
buteco-agents/
├── apps/
│   └── frontend/
│       └── deploy/
│           └── nginx.conf                    (M) `insights` na regex da linha 59;
│                                                 comentário só se a reconferência
│                                                 dos greps tornar alguma frase falsa
├── openspec/
│   └── changes/
│       └── roteamento-insights-no-nginx/
│           ├── proposal.md                   (A)
│           ├── design.md                     (A)
│           ├── tasks.md                      (A)
│           └── specs/
│               └── server-deployment/
│                   └── spec.md               (A) delta: 1 requirement MODIFIED
├── 02-HISTORICO_E_STATUS.md                  (M) defeito, régua, verificação em
│                                                 produção da etapa 3, itens abertos
└── CHANGELOG.md                              (M) entrada em `Fixed`
```

**Sete arquivos, quatro deles artefatos de planejamento.** Nenhum arquivo de
`apps/api`, `apps/workers`, `apps/inbox`, nem de `apps/frontend/src/`. Nenhuma
migração. Nenhuma referência de projeto entre apps — não há código novo a
referenciar nada.

## Goals / Non-Goals

**Goals:**

- `insights` roteado para o `apps/api` pelo nginx do stack, no mesmo bloco e com
  o mesmo tratamento dos outros seis prefixos.
- A lista de prefixos **reconferida pelo método que o próprio arquivo
  documenta**, não copiada deste documento — a enumeração é a garantia, a palavra
  é só a consequência.
- O requisito na spec `server-deployment`, para que a quinta ocorrência não
  dependa de alguém lembrar da quarta.
- A régua ("rota nova em `apps/api`/`apps/inbox` exige entrada no roteamento do
  nginx, e isso é passo de **deploy**") escrita onde a change B e a etapa 4 vão
  ler.
- A verificação em produção da rota da etapa 3, que **já aconteceu**, registrada
  com o regime colado (convenção 22).

**Non-Goals:**

- Substituir a lista mantida à mão por derivação automática. Change própria
  candidata desde 18/09; ver D5.
- Tratar `/insights` como rota de página do `react-router`. Ver D4 — já coberto.
- Deploy, rebuild do `frontend` e verificação pós-deploy. Ver D3 e "Migration
  Plan": eles acontecem **depois** do archive e do merge, e por construção não
  podem fechar esta change (convenção 13).

## Decisions

### D1: `insights` entra no bloco existente do `apps/api`, não num bloco próprio

**Escolhido:** acrescentar o literal ao grupo da linha 59, mantendo a ordem
alfabética aproximada já usada ali.

**Alternativa recusada: `location ~ ^/insights(/|$)` própria.** Ela exigiria
repetir o `if` de `Sec-Fetch-Mode`, o `set $api_upstream`, o `proxy_pass` e os
três `proxy_set_header` — dez linhas duplicadas para obter exatamente o
comportamento que o bloco existente já dá. Duplicação de configuração é o vetor
clássico de divergência silenciosa: o dia em que alguém acrescentar um header de
proxy ao bloco grande, o bloco de `insights` fica para trás sem nada reprovar.
Convenção 2 (sem abstração prematura) pede o inverso aqui — não há consumidor que
precise de comportamento diferente.

**Consequência aceita e desejada:** `insights` herda o `(/|$)`, então
`/insightsxyz` **não** é roteado, e `/insights/agents/{id}` — a rota da change B
— **é**, sem nenhuma alteração futura neste arquivo. Ver D4.

### D2: o `grep` do comentário TERIA pego `/insights` — a lacuna é de processo, e é isso que vai para o registro

A proposta que originou esta change pedia conferir se o `grep` documentado no
topo do arquivo (linhas 16-17) tem ponto cego, e distinguia dois desfechos:
lacuna de processo (ninguém rodou) contra ponto cego do comando (ele não
enxergaria).

**Medido em 23/09/2026, sobre o `HEAD` `d020fe0`:**

```
$ grep -rhoE '"/[a-z0-9-]+' --include="*.cs" apps/api/src/Buteco.Api/ | sort -u
"/agents
"/auth
"/health
"/indexing-summary
"/insights          ← aqui
"/knowledge-bases
"/knowledge-index
"/mcp-servers
"/providers
"/test
```

`"/insights` sai na oitava linha da saída, e sai pelo `MapGet` real
(`InsightsEndpoints.cs:34`), não por uma menção em comentário — a classificação
que a tarefa 1.1 da change de 18/09 fixou como método (`MapGet`/`MapPost`/
`MapGroup` = servido) o confirma como **servido**, sem ambiguidade.

**Veredito: o comando está correto e o comentário está correto.** O que faltou foi
alguém executá-lo. Logo:

- **nada a corrigir no comentário por causa de ponto cego** — não há ponto cego a
  descrever, e inventar um para ter o que escrever seria afirmar mais do que se
  mediu;
- **o `grep` também não ganha ruído novo:** os dois falsos positivos de `apps/api`
  continuam sendo `/test` e `/indexing-summary`, ambos sub-rotas de grupo, ambos
  já nomeados no comentário. A saída acima não traz um décimo primeiro literal.
- **a correção real é de gatilho, não de comando**, e é a régua do `proposal.md`:
  quem acrescenta rota de primeiro nível roda o `grep` e edita o nginx. Ela vai
  para o `02`, que é onde a change B e a etapa 4 leem.

**Por que este veredito importa mais que a palavra na regex:** ele fecha a
pergunta *"o método de conferência é confiável?"* com **sim, medido**. Se fosse
"não", a correção desta change seria pequena e a da próxima seria grande — e a
lista mantida à mão (D5) deixaria de ser candidata para virar obrigatória.

**A tarefa de reconferência continua existindo** (`tasks.md` 1.1) e **não** é
dispensada por esta medição: convenção 6 — a saída acima é de 23/09, sobre o
`HEAD` de hoje, e o estado no momento da aplicação é outro estado. Se a
reconferência achar um oitavo prefixo servido, ele entra, e o fato vira registro
aqui (convenção 9).

### D3: guarda — a suíte NÃO alcança este defeito, e a base TEM precedente de verificar configuração fora dela

Convenção 15 pede que o guarda tenha falhado contra o defeito real, ou que a
ausência de guarda esteja escrita com o motivo. Aqui os dois lados precisam ser
ditos, e um deles corrige uma premissa.

**O que nenhuma suíte alcança.** As três suítes (`apps/api`, `apps/workers`,
`apps/inbox`) e a do `apps/frontend` não leem `nginx.conf`. Não há como escrever
um teste unitário ou de integração de app que reprove com `insights` fora da
regex: o arquivo não participa de nenhum caminho que elas exercitem. Isto vale
igual para a quarta ocorrência e está registrado desde então.

**A premissa que se corrige (convenção 9).** O enunciado desta change supôs que
"subir o `frontend` e bater nos dois caminhos é integração de infraestrutura, que
esta base não tem precedente de fazer". **Falso, e medido:** a tarefa **2.1** da
`nginx-shell-sem-cache-e-prefixo-messages` (18/09/2026) fez exatamente isso —
`nginx:1.30-alpine` descartável, `dist/` do build montado, **upstreams falsos**
`api`/`inbox` devolvendo `{"svc":"api"}` / `{"svc":"inbox"}`, e uma bateria de 20
requisições nas duas formas (`Sec-Fetch-Mode: navigate` e `cors`), com 20 de 20
conforme o esperado. O registro daquela tarefa traz a tabela linha a linha. A
máquina tem `podman` (`/usr/local/bin/podman`), que é o runtime daquela execução —
a nota do diff de `resolver 127.0.0.11` → endereço da rede do podman está no
próprio registro de 2.1.

**Decisão: a verificação em nginx descartável é o guarda desta change, e ela
FECHA a change.** Não é um teste de suíte, e não finge ser: é verificação
determinística sobre o artefato que a correção produz — o arquivo versionado —,
executada antes do deploy, repetível, e com precedente de dezoito dias nesta
mesma base e neste mesmo arquivo.

**Ela discrimina.** É a exigência que a convenção 15 faz, e aqui é barata de
cumprir, porque o defeito é reintroduzível apagando uma palavra:

| requisição | com `insights` na regex | sem (estado de hoje) |
|---|---|---|
| `/insights/system?from=…&to=…`, `Sec-Fetch-Mode: cors` | `{"svc":"api"}`, `application/json` | shell, `text/html` |
| `/insights/agents/abc`, `cors` (prefixo da change B) | `{"svc":"api"}` | shell |
| `/insights`, `Sec-Fetch-Mode: navigate` | shell, `no-store` | shell, `no-store` |
| `/insightsxyz`, `cors` | shell (não casa, `(/|$)`) | shell |

As duas primeiras linhas reprovam sem a correção e aprovam com ela. A terceira e
a quarta aprovam nos dois estados **de propósito**: a terceira é o caso da etapa 4
(D4) e a quarta é a garantia do `(/|$)` (D1) — nenhuma das duas é o defeito, as
duas são regressões a vigiar. Registrar as quatro, e registrar quais duas
discriminam, evita a quinta forma da convenção 15 (guarda cujo critério o sistema
já satisfaz por outro motivo).

**O que a verificação em nginx descartável NÃO prova, e por isso a segunda
metade.** Ela prova que **a configuração versionada roteia**. Ela não prova que o
**painel alcança a rota**, porque entre uma coisa e outra há `build frontend` e
`up -d frontend` contra o piloto, que acontecem depois do merge. Convenção 13
aplicada ao fechamento da própria change: **a change fecha com "a linha está no
arquivo versionado e o nginx descartável a exerce", nunca com "o painel alcança a
rota".**

A verificação pós-deploy vai como **item com gatilho e posição**, no molde da
conferência de campo da `indexacao-lote-de-fragmentos` e da
`compactacao-historico`, **fora do fechamento**:

> **Gatilho:** o próximo `build frontend` + `up -d frontend` contra o piloto.
> **Posição:** "Abertos por `roteamento-insights-no-nginx`" no `02`, e o
> procedimento permanente já existe em `docs/deployment.md` §2 "Redeploy só do
> frontend".
> **Critério:** `GET /insights/system?from=…&to=…` com `Sec-Fetch-Mode: cors` e
> sem token devolve **`401`**, não `200 text/html`. Sem token de propósito: é o
> critério que não depende de ter um token válido à mão, e `401` já prova que a
> requisição atravessou o nginx e chegou ao `apps/api`.

### D4: `/insights` como rota de página da etapa 4 já está coberto — nada a fazer, e é isso que se registra

Na etapa 4, `/insights` passa a ser **ao mesmo tempo** rota de página do
`react-router` e prefixo de API. Hoje não é: `apps/frontend/src/app/routes.tsx`
declara `login`, `inventory`, `agents`, `mcp-servers`, `knowledge-bases` e
`channels`, e **nenhuma** `insights` (conferido em 23/09/2026).

Esse é exatamente o achado empírico que o arquivo já resolveu para `/agents`,
`/channels`, `/mcp-servers` e `/knowledge-bases`, e que está descrito no
comentário das linhas 28-42: o `if ($http_sec_fetch_mode = navigate)` manda
navegação real de documento para o shell, e `fetch()`/XHR da SPA já carregada
segue para o backend. Como `insights` entra **naquele** bloco (D1), ele nasce com
o tratamento.

**Nada a fazer agora.** O valor desta decisão é negativo e é registro: a etapa 4
não precisa tocar o `nginx.conf`, não precisa redescobrir o `Sec-Fetch-Mode`, e
não deve "corrigir" o roteamento quando a tela aparecer. A limitação herdada é a
mesma já aceita e escrita: cliente que não envia `Sec-Fetch-Mode` (raros,
pré-2020) não tem fallback de SPA nesses prefixos.

**Uma consequência a não confundir:** com a correção, `/insights` com
`Sec-Fetch-Mode: navigate` continua devolvendo o shell — antes por cair no
`try_files`, depois por cair no `rewrite`. Mesmo resultado, caminho diferente. É a
terceira linha da tabela da D3, e é por isso que ela aprova nos dois estados.

### D5: a lista mantida à mão continua sendo change própria candidata, e esta ocorrência reforça o gatilho em vez de cumpri-lo

A candidata está registrada desde 18/09 em "Abertos por
`nginx-shell-sem-cache-e-prefixo-messages`". Esta é a **quinta** ocorrência.

**Não se resolve aqui**, e a razão não é pressa: resolver significa trocar a
enumeração manual por derivação — geração da regex a partir dos endpoints no
build, ou um bloco `default` que encaminhe ao `apps/api` com exceções. As duas
mudam o **mecanismo** de roteamento do stack inteiro, com blast radius sobre os
treze prefixos existentes e sobre o truque do `Sec-Fetch-Mode`, e nenhuma faz o
painel voltar mais cedo do que a palavra faz.

**O que esta change acrescenta à candidata**, e é o que a torna mais forte que na
quarta ocorrência: as quatro primeiras eram *"o autor da rota esqueceu de editar a
lista"*. Esta tem uma causa a mais e mais difícil — **o arquivo mora no app
errado** (D6). Uma derivação automática cobre as duas causas; um checklist humano
cobre só a primeira.

### D6: a régua vai para o `02`, não para o comentário do `nginx.conf`

O comentário do arquivo **já diz** o que precisa dizer ao leitor do arquivo: que a
lista cobre todos os prefixos servidos, que se deriva deles, que não se mantém de
memória, e como reconferir. Nada disso falhou (D2).

**Quem não leu o arquivo é que precisa da régua** — o autor de uma change em
`apps/api` que não tem motivo nenhum para abrir `apps/frontend/deploy/`. Esse
leitor não chega ao comentário por definição, então acrescentar frase lá não o
alcança. Ele lê o `02` e os `design.md` arquivados.

Por isso a régua vai para `02-HISTORICO_E_STATUS.md` em duas posições:

- no histórico desta change, com a causa completa e o veredito do `grep`;
- em "Abertos por `roteamento-insights-no-nginx`", como o item endereçado à
  **change B** — `GET /insights/agents/{id}` já está coberta pela entrada
  `insights` com `(/|$)`, **não precisa de correção nova**, e supor que precisa
  custaria uma segunda edição do mesmo arquivo.

### D7: projeção (convenção 18) — esta change testa a régua no extremo inferior, e uma das duas dimensões não existe aqui

A convenção 18 pede projeção por componente, com "modificados" separado de
"criados", e — quando o entregável inclui registro de decisão ou de mecanismo — a
proporção lógica : comentário projetada antes de escrever o código.

**Projeção, feita depois de a verificação fechar** (a verificação aqui é a D2 e a
leitura de `routes.tsx` da D4; as duas estão fechadas neste documento):

| dimensão | projetado | como foi contado |
|---|---|---|
| **produção — lógica** | **1 palavra**, 0 linhas novas, 1 linha modificada | um literal na regex da linha 59 |
| **produção — comentário** | **0 linhas**, com um caso condicional | o comentário só muda se a reconferência (1.1) tornar alguma frase falsa; a D2 mediu que não torna |
| spec (delta `server-deployment`) | ~30 linhas, 1 arquivo criado | 1 requirement MODIFIED, com o cenário de enumeração e um cenário novo no molde do de `/messages/summary` |
| `02-HISTORICO_E_STATUS.md` | ~110 linhas, modificado | 1 entrada de histórico (defeito + régua + verificação em produção da etapa 3 com o regime colado) + 1 seção "Abertos por" com 3 itens |
| `CHANGELOG.md` | ~12 linhas, modificado | 1 entrada em `Fixed`, no molde da de `messages` |
| artefatos OpenSpec | ~500 linhas, 3 arquivos criados | proposal, design, tasks |

**A razão lógica : comentário não é projetável nesta change, e o motivo é
estrutural, não uma falha de projeção.** Com **uma** palavra de produção, qualquer
razão é degenerada: 1:0 se o comentário não mudar, 1:N para qualquer N se mudar
uma frase. A régua da convenção 18 conta *registros de mecanismo entregues em
código de produção* — aqui o número é **zero**, porque o mecanismo já está
registrado no arquivo desde `fix-stack-servidor-lacunas` e a D2 mediu que ele
continua correto.

**O que o extremo inferior ensina, e é o item que esta change devolve à convenção
18:** quando a produção é de uma linha, **a projeção que decide o tamanho é a de
registro** — `02` e `CHANGELOG` são ~122 das ~152 linhas fora dos artefatos
OpenSpec, ou **80%**. E a própria convenção já avisa que registro "cresce com
quantos achados a change produz, que é justamente o que não se projeta": esta
produziu **três** antes de a implementação começar (o veredito do `grep` em D2, o
precedente de nginx descartável em D3, a ausência de `/insights` em `routes.tsx`
em D4), e é por isso que a linha do `02` é a maior da tabela. A conferência no
`tasks.md` mede as duas dimensões contra o entregue.

### D8: a verificação em produção da etapa 3 é registro, não tarefa — e vai com o regime colado (convenção 22)

Depois da correção aplicada à mão e do rebuild do `frontend` no piloto, quatro
requisições já foram medidas, e o corpo já confirmou o contrato da change
anterior. Isso **já aconteceu**: não é tarefa desta change, e transformá-lo em
tarefa faria a change afirmar como trabalho o que é evidência.

Vai para o `02` como registro, e **cada número vai com o estado sobre o qual foi
medido**, porque é exatamente o tipo de número que migra de pergunta sem ninguém
notar:

> Medido em **23/09/2026**, no piloto, fuso `America/Sao_Paulo`, sobre a
> **primeira janela medida** — 01/09 a 23/09, **156 execuções** —, com os regimes
> declarados em `execution` **22/09 01:21** e `embedding` **23/09 01:18**.

O gatilho de recalibração que esse registro nasce com: **qualquer número dele
citado sobre uma janela que comece antes de 22/09/2026 01:21 está sendo citado
fora do regime em que foi medido.** É a propriedade que a própria série confirma —
a série omite os dias anteriores ao regime, e a janela pedida desde 01/09 trouxe
**só 22 e 23**, sem nenhum `0` nos vinte e um dias anteriores.

## Risks / Trade-offs

- **[A reconferência da 1.1 acha um oitavo prefixo servido e ausente]** → Ele
  entra na mesma edição, e o fato vira registro aqui (convenção 9) e no `02`. A
  D2 mediu que hoje não há, mas a medição é de 23/09 e a régua é reconferir, não
  confiar nela.
- **[`insights` passa a interceptar algo que hoje cai no SPA]** → É o risco
  inverso do defeito, e o único de regressão real. Coberto pelas linhas 3 e 4 da
  tabela da D3 no nginx descartável, e mitigado por construção: não há rota de
  página `/insights` hoje (D4), e o `(/|$)` impede `/insightsxyz`.
- **[A change fecha e o painel continua sem a rota]** → **Esperado, não risco**, e
  é o que a convenção 13 obriga a dizer: o fechamento é sobre o arquivo
  versionado. O painel só alcança depois de `build frontend` + `up -d frontend`.
  Coberto pelo item com gatilho da D3, e o procedimento já existe em
  `docs/deployment.md` §2.
- **[A sexta ocorrência]** → Não mitigada por esta change, e assumido. A régua
  (D6) e a spec reduzem a chance; só a derivação automática (D5) a elimina, e ela
  é change própria.
- **[O nginx descartável não roda por falta de runtime]** → `podman` está
  presente e é o runtime do precedente de 18/09. Se não estivesse, a alternativa é
  `nginx -t` sobre o arquivo editado mais a conferência do literal na regex, **e
  a change registraria que o guarda caiu para não-discriminante** em vez de
  fechar como se tivesse rodado.

## Migration Plan

Sem migração de dados, sem migração de esquema, sem ordem entre apps.

1. Merge na `main`.
2. `docker compose --env-file .env.prod -f docker-compose.prod.yml build frontend`
3. `docker compose --env-file .env.prod -f docker-compose.prod.yml up -d frontend`
4. Verificação pós-deploy pelo procedimento permanente de `docs/deployment.md` §2,
   com o critério da D3 (`/insights/system` com `cors` e sem token → `401`).

**Nenhum dos quatro é tarefa desta change** (D3). O `apps/api` e o `apps/inbox`
não param: não há migração e o `migrator` não roda.

**Rollback:** reverter o commit e repetir 2-3. O estado anterior é o defeito
conhecido, não uma quebra — nada que hoje funcione passa a depender desta linha.

## Open Questions

Nenhuma. As três que existiam foram fechadas por medição antes de este documento
fechar, e cada uma está na sua decisão: o ponto cego do `grep` (D2, **não há**), o
precedente de teste de configuração (D3, **há**, de 18/09), e o estado de
`/insights` no `react-router` (D4, **não existe ainda**).
