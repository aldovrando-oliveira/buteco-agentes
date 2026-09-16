## Contexto

Change de código e configuração, saída da leitura de um `RUNBOOK-LOCAL.md` não
versionado escrito por quem subiu o stack numa máquina real. Cada defeito abaixo
tem **duas pontas**: o contorno manual que o runbook ensina, e o `arquivo:linha`
que o torna necessário.

A regra que governa a change: **contorno manual documentado é defeito na origem
até prova em contrário.** A prova em contrário existe e está nos Non-Goals — há
contornos que são legitimamente de máquina.

## Frente 1 — A chave commitada

### O que a árvore tem

`apps/workers/src/Buteco.Workers/appsettings.Development.json:18-19` — endpoint
de gateway interno e chave de API, em arquivo **rastreado**. Entrou em `19a5bec`
(12/09/2026), num commit de UI de bases de conhecimento.

### Decisão F1.1 — remover do arquivo NÃO é a correção; rotacionar é

`git log -S` devolve **um** commit tocando a chave: ela está no histórico. Apagar
a linha muda o estado atual e **não invalida nada** — qualquer clone existente,
qualquer fork, qualquer CI que tenha guardado o checkout continua com o valor.

A tarefa de rotação é o item real, e ela é **operacional, fora do repositório**.
Está no `tasks.md` como passo bloqueante de propósito: uma change que remove a
linha e declara o problema resolvido produz exatamente a falsa sensação de
segurança que a auditoria de documentação passou sete passes combatendo.

### Decisão F1.2 — o que fica no lugar

O `appsettings.Development.json` não ganha outro endpoint no lugar. Os campos
ficam vazios, como `Anthropic__ApiKey` e `Gemini__ApiKey` já estão no mesmo
arquivo (`:27`, `:30`, ambos `changeme`), e quem desenvolve passa o seu por
variável de ambiente.

**O custo disso é real e precisa estar dito:** hoje o Caminho B funciona sem
configurar nada, porque o gateway vem commitado. Depois desta change, `dotnet run`
do worker exige `OpenAI__BaseUrl`/`OpenAI__ApiKey` no ambiente, ou toda task
termina `failed`. É trabalho novo para quem desenvolve, e é o preço correto —
credencial compartilhada por arquivo versionado é o problema, não a solução.
`docs/development.md` ganha a linha.

### Decisão F1.3 — purgar o histórico fica FORA

Reescrever histórico (`filter-repo`, `filter-branch`) invalida todo clone e todo
PR aberto, e é decisão do dono do repositório. A change remove do estado atual e
exige a rotação, que é o que efetivamente mata a chave. Registrado nos Non-Goals
para não ser lido como esquecimento.

## Frente 2 — Os três prefixos do nginx

### O que a árvore faz

```
apps/api serve:                    nginx roteia para api:
  /agents        ────────────────▶   agents        ✓
  /providers     ────────────────▶   providers     ✓
  /mcp-servers   ────────────────▶   mcp-servers   ✓
  /auth          ────────────────▶   auth          ✓
  /knowledge-bases  ─────╳            (ausente)    ✗
  /knowledge-index  ─────╳            (ausente)    ✗

apps/inbox serve:                  nginx roteia para inbox:
  /channels      ────────────────▶   channels      ✓
  /contacts      ────────────────▶   contacts      ✓
  /sessions      ────────────────▶   sessions      ✓
  /webhooks      ────────────────▶   webhooks      ✓
  /internal      ─────╳               (ausente)    ✗
```

O que não é roteado cai em `location / { try_files $uri /index.html; }`
(`nginx.conf:69-71`) e responde **`200` com HTML**. Não é `404`: é a resposta
errada com o status certo, que é o modo de falhar mais caro — o consumidor recebe
sucesso e conteúdo inválido.

### Decisão F2.1 — enumerar, não lembrar

O runbook documenta patch para **dois** prefixos. A enumeração dos prefixos reais
dos dois apps devolveu **três**:

```
grep -rhoE '"/[a-z0-9-]+' --include="*.cs" apps/api/src/Buteco.Api/ | sort -u
grep -rhoE '"/[a-z0-9-]+' --include="*.cs" apps/inbox/src/Buteco.Inbox/ | sort -u
```

O que faltava era `knowledge-index`, e ele **é chamado pelo painel** —
`apps/frontend/src/features/knowledge-bases/api/knowledgeIndexApi.ts:20` chama
`/knowledge-index/diagnostics`. Aplicar o patch do runbook como está corrigiria
duas telas e deixaria a aba de diagnóstico quebrada.

**É a razão de a correção pertencer à origem.** Um patch em runbook se escreve de
memória, a partir do que mordeu; uma correção no repositório se escreve a partir
da enumeração, e a tarefa carrega o comando que a produz.

### Decisão F2.2 — cada prefixo no bloco certo, e só um precisa do tratamento de navegação

| Prefixo | Bloco | É rota de página? | Precisa de `Sec-Fetch-Mode`? |
|---|---|---|---|
| `knowledge-bases` | api | **sim** — `apps/frontend/src/app/routes.tsx:60` | **sim**, e o bloco da api já tem |
| `knowledge-index` | api | não | não; estar no bloco é inócuo |
| `internal` | inbox | não | não; estar no bloco é inócuo |

`knowledge-bases` é o mesmo caso de `agents`, `mcp-servers` e `channels`: path que
é simultaneamente rota do `react-router` e prefixo de API. Sem o tratamento de
`Sec-Fetch-Mode` que o bloco da api já aplica (`nginx.conf:45-47`), um refresh em
`/knowledge-bases/{id}` receberia JSON em vez do SPA. **Colocá-lo no bloco certo
não é detalhe de organização — é o que o faz funcionar nos dois sentidos.**

### Decisão F2.3 — a spec viva congelou a lista incompleta

O cenário *"Requisições de API são roteadas para o app correto"*
(`openspec/specs/server-deployment/spec.md:128-135`) enumera exatamente os oito
prefixos que o `nginx.conf` tem. A spec foi escrita **contra a implementação**, e
as duas derivaram juntas do que os apps servem — então nenhum guarda podia pegar:
o cenário passava porque descrevia o defeito.

O delta corrige o cenário **e** acrescenta uma requirement que ataca a causa:
a lista de prefixos tem de ser derivável dos apps, não mantida de memória.

## Frente 3 — O que o compose de produção não entrega

### F3.1 — `Embedding__*`

`docker-compose.prod.yml` não interpola a seção para `workers`, e
`appsettings.json` (Production) não tem default. O boot **passa**, porque a
checagem de consistência do índice não diverge de um índice vazio, e a **primeira
indexação** falha com modelo vazio e dimensão 0.

É a pior forma: o erro aparece longe da causa, na primeira vez que alguém sobe um
documento. Mesma família do que `docs/configuration.md:36-38` chama de *"a fonte
mais comum de falha de configuração no sistema, porque o sintoma aparece longe da
causa"*.

### F3.2 — `Anthropic__ApiKey` e `Gemini__ApiKey`

Já registrado como item aberto em `02-HISTORICO_E_STATUS.md:5266-5273`, e achado
de novo pela auditoria de documentação. `.env.prod.example:47-48` oferece as
variáveis, `docs/configuration.md:246` promete o mapeamento, e nenhum serviço as
consome. Entra aqui porque é a mesma classe e o mesmo arquivo das outras duas.

### F3.3 — `env_file:` NÃO é a correção; variável obrigatória é

**Esta decisão corrige o que o runbook propõe.** O runbook (A1) trata o problema
como armadilha a documentar: *"em todo comando deste caminho, `--env-file
.env.prod`"*.

`env_file:` **não resolve**, e é importante dizer por quê: `env_file:` injeta
variáveis **dentro do contêiner**, enquanto `${VAR}` em `docker-compose.prod.yml`
é **interpolação em tempo de parse**, que lê do shell ou do `--env-file` — nunca
de um `env_file:` declarado no serviço. As connection strings do compose são
montadas por interpolação (`Host=postgres;...;Password=${POSTGRES_PASSWORD}`), então
declarar `env_file:` deixaria o problema exatamente onde está.

A correção é a sintaxe de **variável obrigatória** da spec do Compose:

```
${POSTGRES_PASSWORD:?defina POSTGRES_PASSWORD — use --env-file .env.prod}
```

Sem a variável, o Compose **falha com essa mensagem** em vez de substituir por
string vazia. Converte a armadilha silenciosa em fail-fast com instrução — que é
o padrão que esta base já aplica em `ValidateRouteAuthenticationClassification`,
`ValidateChannelAdapterRegistrations` e `ValidateTimeZoneConfiguration`.

> **Não verificado por execução nesta máquina.** Não há Docker aqui (o ambiente
> usa Podman, e `docker compose version` não resolve), então a sintaxe está
> **declarada a partir da especificação do Compose, não medida**. A tarefa 3.1
> exige a verificação empírica antes de converter as demais variáveis — inclusive
> sob `podman-compose`, que é o que esta máquina tem e que pode divergir.

### F3.4 — As portas de dev

`appsettings.Development.json` (versionado) aponta `15532`/`15772`;
`.env.example` traz `5432`/`5672`. `docs/development.md:92` manda `cp .env.example
.env`, e quem faz isso sobe a infra nas portas padrão enquanto os três apps
procuram as outras.

**A decisão é qual dos dois lados muda**, e ela não é óbvia:

| Saída | A favor | Contra |
|---|---|---|
| alinhar `.env.example` para `15532`/`15772` | o `appsettings` versionado é o que os apps de fato leem; portas altas evitam colisão com Postgres local | muda o exemplo que `docs/development.md` e o `README` citam |
| alinhar `appsettings` para `5432`/`5672` | portas padrão são o que todo mundo espera | colide com Postgres/RabbitMQ instalados na máquina — que foi provavelmente o motivo das portas altas |

Fica como **Open Question**, com recomendação. Escolher no meio da tarefa é como
a divergência nasceu.

## Riscos

**`env_file:`/variável obrigatória muda o comportamento para quem já roda o
stack.** Quem hoje sobe com `--env-file .env.prod` não é afetado; quem sobe sem, e
funciona por acidente (variáveis exportadas no shell), passa a falhar no parse.
Isso é o objetivo, mas é mudança de comportamento e precisa estar no
`docs/deployment.md`.

**A mudança de `appsettings.Development.json` quebra o Caminho B de quem já
desenvolve** até configurar as variáveis. Ver F1.2.

**O nginx é adição de alternativa a regex existente** — risco baixo, e o teste é
observável: cada prefixo novo responde JSON em vez de HTML.

## Open Questions

1. **Portas de dev (F3.4)** — alinhar `.env.example` para as portas altas, ou os
   `appsettings` para as padrão? **Recomendação: alinhar `.env.example`**, porque
   o `appsettings` versionado é o que os apps leem e as portas altas
   provavelmente existem para evitar colisão com serviços locais. Decisão do
   mantenedor.
2. **A chave exposta já foi rotacionada?** Se sim, a frente 1 vira só limpeza de
   arquivo e deixa de ser bloqueante. Se não, ela bloqueia a change.
