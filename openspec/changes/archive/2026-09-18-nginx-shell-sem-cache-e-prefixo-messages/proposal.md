## Why

Hotfix de estabilização do piloto. Em `https://agente.butecandoespetobar.com.br`, a
tela de entrada (Inventário) mostra o card **Agentes** e o card **Mensagens
recebidas** em erro, e `/agents` responde *"Não foi possível carregar os
agentes."*. Os dois cards parecem um defeito só, mas são **dois defeitos
independentes**. Os dois moram em `apps/frontend/deploy/nginx.conf` e saem no
mesmo deploy.

### R1: prefixo `messages` ausente da enumeração do nginx

`GET /messages/summary` devolve **`200 text/html`** em produção.
`GET /sessions/summary`, medido lado a lado, devolve `401`, ou seja, chega ao
`apps/inbox`. A rota nasceu no #24 (`81efadd`, `inbox-mensagens-recebidas-periodo`)
sem entrar no bloco do inbox. Ela cai no fallback de SPA e responde a resposta
errada com o status certo.

É a **quarta ocorrência** da mesma classe de defeito. As três anteriores foram
`internal`, `knowledge-bases` e `knowledge-index`, corrigidas em
`openspec/changes/archive/2026-09-16-fix-stack-servidor-lacunas/`.

### R2: o shell do SPA é cacheável pelo browser, sob URL que também é de API

O nginx devolve duas respostas diferentes para a **mesma URL**, conforme o
header `Sec-Fetch-Mode`: `index.html` para `navigate`, e proxy para o
`apps/api` nos demais casos. Ele não declara `Vary`, e o `index.html` sai sem
`Cache-Control`, só com `Last-Modified`. Isso o torna cacheável por heurística:
o browser guarda a cópia por 10% da idade do arquivo, que cresce com a idade do
deploy.

Um refresh ou link direto em `/agents` grava o HTML sob essa URL. A partir daí,
todo `fetch()` da SPA para `/agents` é servido do cache de disco com HTML.
Medido: `/agents` com `Sec-Fetch-Mode: cors` responde `401` ao vivo, então o
roteamento está correto e o HTML vem do cache do browser. O DevTools de produção
mostra `200 OK (from disk cache)`. O Cloudflare **não** guarda o HTML
(`cf-cache-status: DYNAMIC`).

### Perfis de risco opostos na mesma change

| | R1: `messages` | R2: shell sem cache |
|---|---|---|
| Natureza | Determinística: falha para todo mundo, em todo carregamento | Depende de estado do cache de cada browser e da idade do deploy |
| Verificação | `curl` fecha: `401`, não `text/html` | O header no `curl` fecha. O efeito no browser só discrimina corrigido de não corrigido com deploy envelhecido (>1 dia); logo depois do deploy, o browser passa igual com ou sem o patch |
| Dependência externa | Nenhuma | O `Cache-Control` da origem precisa **sobreviver ao Cloudflare**. Isso não foi medido e só se mede depois do deploy (ver `design.md`, V3) |
| Quem já foi afetado | Deixa de falhar no deploy | Quem já tem a cópia errada precisa de recuperação (ver `design.md`, V5) |

Por isso os cenários de spec de R1 e R2 são **separados** e nunca misturados. Se
o R2 não surtir efeito em produção, o R1 continua valendo sozinho.

### Veredito sobre "funcionava na versão anterior"

- O `nginx.conf` não mudou entre a versão anterior ao Inventário e o `HEAD`.
- O R2 existia antes. Antes do Inventário a exposição era até maior: a rota
  índice redirecionava para `/agents`, então um F5 na tela de entrada gravava a
  cópia errada.
- O que o Inventário trouxe de fato foi o R1: um card que falha sempre, na tela
  de entrada.
- **Por que o R2 pode ter passado despercebido: a idade do deploy.** O browser
  calcula por quanto tempo reusa a cópia **no momento em que a grava**: 10% de
  `Date − Last-Modified`. O `Last-Modified` do `index.html` é a hora do build, e
  o `Date` é a hora da resposta. Então:

  | Idade do deploy quando a cópia é gravada | Janela de reuso |
  |---|---|
  | 1 min | ~6 s |
  | 45 min (medido em 18/09) | ~4,5 min |
  | 1 dia | ~2,4 h |
  | 10 dias | ~1 dia |

  Logo depois de um deploy, a janela nasce perto de zero. A cópia gravada já
  está vencida quando o `fetch()` a encontra. O browser então manda uma
  requisição condicional, que sai com `Sec-Fetch-Mode: cors`, vai ao `apps/api` e
  volta com JSON. O defeito se cura sozinho e não aparece. Ele só se manifesta
  quando o deploy já envelheceu. Com deploys frequentes, o painel "funciona".

  Isso é uma explicação com mecanismo, não mais uma conjectura. O que continua
  sem medição é o histórico: a hora exata de cada deploy anterior, e se alguém
  deu F5 em `/agents` com o deploy velho.

## What Changes

Configuração do nginx do stack, `apps/frontend`. Nenhuma rota de backend, nenhum
código de aplicação, nenhuma migration.

- **R1:** `messages` entra na enumeração do bloco que encaminha para o `apps/inbox`.
- **R2:** o shell do SPA (`index.html`) passa a sair com
  `Cache-Control: no-store`, **somente** o shell. Os assets com hash de conteúdo
  continuam cacheáveis.
- **Comentário do `nginx.conf`:** os dois greps de conferência de prefixos devolvem
  ruído conhecido (`/test`, `/indexing-summary`, `/api`, e `/agents` no grep do
  inbox). A nota evita repetir o trabalho de descartá-los.
- **Spec `server-deployment`:** `messages` entra na enumeração de prefixos, e o
  comportamento de cache do shell passa a ser requisito.
- **`docs/deployment.md` §2:** passa a descrever o redeploy "só o frontend"
  (`build frontend` + `up -d frontend`, sem parar o `apps/inbox` e sem rodar o
  `migrator`), e a verificação que acompanha qualquer redeploy desse tipo.
- **`02-HISTORICO_E_STATUS.md`:** os dois achados, o gatilho de `Cache-Control`
  em resposta de API, e o fechamento **integral** da pendência 2.4 de
  `fix-stack-servidor-lacunas`. O item sai de "Itens em aberto".

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `server-deployment`:
  - o requirement de roteamento por prefixo passa a enumerar `messages` no bloco
    do `apps/inbox` (R1);
  - novo requirement: o shell do SPA não é armazenado em cache, sem afetar os
    assets (R2).

## Non-Goals

- **Remover o truque do `Sec-Fetch-Mode`.** A mesma URL continua devolvendo HTML
  ou JSON conforme o header. Isso fica para a change do prefixo `/painel/`, já
  decidida. Este hotfix só impede que a variação fique gravada no browser.
- **Acabar com a lista de prefixos mantida à mão.** Quatro ocorrências já
  cumpriram qualquer gatilho razoável, e a change do `/painel/` **não** resolve
  isso: ela separa o frontend das APIs, mas não o `apps/api` do `apps/inbox`.
  Fica registrada como change própria candidata (ver `design.md`).
- **`Cache-Control` nas respostas de API.** Hoje elas são seguras só porque não
  têm validador (nem `ETag`, nem `Last-Modified`). O gatilho fica escrito no
  `design.md`.
- **`Vary: Sec-Fetch-Mode`.** Recusado com razão no `design.md` (D3).
- **Configuração da borda Cloudflare** (Web Analytics, Email Obfuscation, Browser
  Cache TTL). Fica fora do repositório. É contingência preparada (V3), não tarefa.

## Impact

- **Código/configuração:** `apps/frontend/deploy/nginx.conf`, e só ele.
- **Specs:** `server-deployment`: um requirement MODIFIED, um ADDED.
- **Documentação:** `02-HISTORICO_E_STATUS.md` e `docs/deployment.md` §2. O
  resto de `docs/` fica a conferir: a enumeração inicial não encontrou
  afirmação sobre a lista de prefixos nem sobre cache do shell (ver `tasks.md`).
- **Deploy:** só o serviço `frontend` (`build frontend` + `up -d frontend`). O
  `apps/inbox` **não** para: não há migration. O deploy acontece **depois** do
  archive e do merge, e não é tarefa desta change.
- **Suítes de teste:** nenhuma lê o `nginx.conf`. O que fecha a change é o nginx
  descartável sobre o arquivo editado (tarefa 2.1), que cobre todo o
  comportamento do nginx. A verificação pós-deploy (`curl` e browser) **não
  fecha a change**. Ela responde uma única pergunta que só produção responde:
  se o `Cache-Control` sobrevive ao Cloudflare. Fica como procedimento em
  `docs/deployment.md` §2 e como obrigação conferível no `02`.
- **Custo aceito:** o shell deixa de ser guardado pelo browser, o que custa
  ~1,4 KB por navegação e desabilita o bfcache no documento principal (D2).
- **Risco de regressão:** baixo no R1 (um item a mais numa regex existente). No
  R2, o risco é o header pegar os assets por engano. O alvo escolhido
  (`location = /index.html`) evita isso, e foi testado.
