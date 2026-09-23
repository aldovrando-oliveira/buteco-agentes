## Why

`GET /insights/system` — entregue pela `rotas-de-agregacao-sistema`, etapa 3 da
linha `metricas-de-operacao` — **existe, sobe e responde**, e **não chega ao
painel**. O nginx do stack (`apps/frontend/deploy/nginx.conf`) roteia por lista
de prefixos enumerada à mão, e `insights` não está nela. A requisição cai no
fallback de SPA e devolve `200 text/html` onde o consumidor espera JSON: a
resposta errada com o status certo.

É a **quinta ocorrência** da mesma classe de defeito. As três primeiras
(`internal`, `knowledge-bases`, `knowledge-index`) foram corrigidas em
`openspec/changes/archive/2026-09-16-fix-stack-servidor-lacunas/`; a quarta
(`messages`) em
`openspec/changes/archive/2026-09-18-nginx-shell-sem-cache-e-prefixo-messages/`.

### Como o defeito foi isolado

Medido no piloto em **23/09/2026**, fuso `America/Sao_Paulo`:

| passo | resultado |
|---|---|
| `GET /insights/system` pelo navegador, com token | **`200 text/html`** — o `index.html` do SPA |
| a mesma requisição **sem** token | **`200 text/html`** — a autenticação nem era exercida |
| `GET /agents` pelo navegador, com token | `200 application/json` |
| `GET /insights/system` **de dentro do container `api`**, sem token | **`401`** |

O **`401` por dentro** é o passo discriminante, e é ele que separa as duas
hipóteses possíveis: *a rota não subiu* contra *a rota não é alcançada*. Ele
prova que a rota existe, subiu e está protegida — logo o HTML de fora não vem do
`apps/api`, vem do fallback de SPA do nginx respondendo por um caminho que ele
não repassa.

### A causa, no arquivo

`apps/frontend/deploy/nginx.conf`, a `location` que enumera os prefixos do
`apps/api`:

```
location ~ ^/(agents|providers|mcp-servers|knowledge-bases|knowledge-index|auth)(/|$) {
```

`insights` falta. A correção é **uma palavra** dentro desse grupo.

## What Changes

Configuração do nginx do stack, em `apps/frontend`. **Nenhuma rota de backend
muda, nenhum código de aplicação, nenhuma migração, nenhuma tela.** A rota não
muda — ela só passa a ser alcançável.

- **`insights` entra na enumeração do bloco que encaminha para o `apps/api`.**
- **O comentário do arquivo**, só se a reconferência dos greps achar algo que ele
  hoje afirme e seja falso (convenção 9). A conferência preliminar indica que
  **não há**: o `grep` documentado no topo do arquivo devolve `"/insights` na
  saída de `apps/api`, isto é, ele **teria pego** o prefixo faltante. A lacuna é
  de **processo** — ninguém o rodou —, não de ponto cego do comando. A
  reconferência sobre o estado do momento da aplicação é tarefa (`tasks.md`), e o
  veredito preliminar não a dispensa.
- **Spec `server-deployment`:** `insights` entra na enumeração de prefixos do
  cenário de roteamento, e ganha cenário próprio, no molde do de
  `/messages/summary`.
- **`02-HISTORICO_E_STATUS.md`:** o defeito, a régua que ele ensina, e a
  verificação em produção da rota da etapa 3 — que já aconteceu e por isso é
  **registro, não tarefa**.
- **`CHANGELOG.md`:** entrada em `Fixed`.

### A régua que este defeito ensina

**Rota nova em `apps/api` ou `apps/inbox` exige entrada no roteamento do nginx, e
isso é passo de DEPLOY, não de código.**

O que a torna difícil de aplicar: o arquivo mora em **`apps/frontend/deploy/`**.
A `rotas-de-agregacao-sistema` declarou `apps/frontend` como **"nada tocado"** —
e declarou **corretamente**, porque as telas são das etapas 4 e 5. **Nenhuma
conferência de lista de arquivos pegaria isto**, porque a lista é do app errado.

É o **quarto desvio de escopo** daquela change, e o de natureza diferente dos
outros três: os três registrados em `02-HISTORICO_E_STATUS.md` ("Conferência de
escopo de arquivo — três desvios da lista fechada") são *arquivo tocado fora da
lista* (dois) e *arquivo da lista não tocado* (um). Este é **passo de deploy não
previsto**, que a conferência de arquivo não tem como alcançar.

**E ela vale para a change B** (`GET /insights/agents/{id}`, a rota de escopo de
agente da mesma linha): como o padrão é por prefixo com `(/|$)`, a entrada
`insights` **já a cobre**. Fica registrado para que a B não repita a correção nem
suponha que precisa dela.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `server-deployment`: o requirement de roteamento por prefixo passa a enumerar
  `insights` no bloco do `apps/api`, e ganha um cenário de alcance da rota de
  agregação, no molde do cenário de `/messages/summary`.

## Non-Goals

- **Acabar com a lista de prefixos mantida à mão.** Já é change própria candidata,
  registrada em "Abertos por `nginx-shell-sem-cache-e-prefixo-messages`" desde
  18/09. Esta ocorrência **reforça** o gatilho e é registrada lá; resolvê-la aqui
  seria trocar um hotfix de uma palavra por uma mudança de mecanismo de
  roteamento, sem o painel voltar mais cedo por isso.
- **Tratamento de `/insights` como rota de página do `react-router`.** A etapa 4
  vai criar a tela, e `/insights` passará a ser **ao mesmo tempo** rota de página
  e prefixo de API — o mesmo caso que o arquivo já resolve para `/agents` e
  `/knowledge-bases`. O bloco que recebe `insights` **já** tem o tratamento de
  `Sec-Fetch-Mode: navigate`: navegação real cai no SPA, `fetch()` da própria app
  vai ao backend. **Nada a fazer por isso agora**, e o `design.md` registra que
  já está coberto para que a etapa 4 não redescubra.
- **Deploy e rebuild do `frontend`.** Acontecem depois do archive e do merge.
  Não são tarefa desta change (convenção 13 — ver abaixo).
- **Teto de janela, índices e demais itens abertos pela etapa 3.** Seguem nos
  respectivos itens, com os gatilhos que já têm.

## Impact

- **Código/configuração:** `apps/frontend/deploy/nginx.conf`, e só ele.
- **Specs:** `server-deployment`, um requirement MODIFIED.
- **Documentação:** `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`. `docs/` fica a
  conferir na aplicação: a enumeração preliminar não achou arquivo que afirme a
  lista de prefixos do nginx — a `nginx-shell-sem-cache-e-prefixo-messages` já
  fizera essa varredura em 18/09 e nada encontrou —, mas a busca se repete sobre
  o estado do momento.
- **Suítes de teste: nenhuma alcança este defeito**, e isso é declarado, não
  contornado (convenção 15). Nenhuma das três suítes lê o `nginx.conf`. O que a
  change pode fechar é a configuração **verificada fora das suítes**; o que ela
  **não pode** afirmar é que o painel alcança a rota, porque isso depende de
  rebuild e deploy que acontecem depois do merge. Ver `design.md`.
- **Deploy:** só o serviço `frontend` (`build frontend` + `up -d frontend`, com
  `--env-file .env.prod`), pelo procedimento permanente de
  `docs/deployment.md` §2 "Redeploy só do frontend". Sem migração, sem parar
  `apps/api` ou `apps/inbox`.
- **Risco de regressão:** baixo — um item a mais numa regex existente, num grupo
  que já tem seis. O risco nomeado é o inverso: `insights` passar a interceptar
  algo que hoje cai no SPA. Hoje **não há** rota de página `/insights` no
  `react-router` (ela nasce na etapa 4), e quando nascer o `Sec-Fetch-Mode` a
  cobre. Verificável na tabela de verificação do `design.md`.
