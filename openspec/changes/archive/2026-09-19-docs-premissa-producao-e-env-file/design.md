## Context

A exploração desta change fez uma varredura enumerada, não de memória, em
`docs/`, `SECURITY.md`, `README.md` e nos comentários de
`docker-compose.prod.yml`, `.env.example` e `.env.prod.example`. Ela classificou
cada ocorrência em três classes:

- **(a)** afirmação factual falsa, que esta change corrige;
- **(b)** aceite de risco cujo argumento mudou, que esta change só marca;
- **(c)** texto que continua verdadeiro, que esta change não toca.

A distinção entre (a) e (b) é o ponto da change. Corrigir texto é documentação.
Rever um aceite de risco é decisão de comportamento em produção e tem exploração
própria.

Fatos medidos que sustentam o design:

- **R1, sem a flag.** Com `podman-compose` 1.6.0, num diretório isolado e com
  ambiente limpo, `build`, `stop inbox`, `run --rm migrator`, `up -d …` e `ps`
  sem `--env-file` falham todos em
  `required variable POSTGRES_USER is missing a value`, com exit 1 e **nenhum
  container criado**. Com o `.env` de dev presente, a falha passa para
  `MCP_CREDENTIAL_ENCRYPTION_KEY`, porque o `.env` de dev cobre só 4 das 14
  variáveis.
- **R1, com a flag.** `config --env-file .env.prod.example` passa, com exit 0.
  As 14 variáveis obrigatórias estão preenchidas no exemplo.
- **A2A.** `ValidateRouteAuthenticationClassification` em `apps/api` lista só
  `/health`, `/auth/login` e o agent card como anônimos. O token de serviço
  (`service:inbox`) é restrito a `POST /agents/{id}/a2a` e `GET /agents/{id:guid}`.
  O único outro token emitido é o do operador, com `sub: operator`, acesso total
  e TTL padrão de 30 min. `auth-login-e-servico`, Decision 6, aceitou que
  "não existe emissão de credencial para clientes externos" e mandou
  "revisitar quando houver um consumidor A2A externo real". O `02` não tem item
  para isso.
- **O repositório é público** (`gh repo view`: `PUBLIC`). Isso pesa na forma das
  marcas (b), ver D3.

## Goals / Non-Goals

**Goals:**

- Todo comando `docker compose` contra `docker-compose.prod.yml` em
  `docs/deployment.md` passa `--env-file .env.prod`, sem exceção.
- Toda afirmação (a) da varredura fica verdadeira.
- Todo aceite (b) diz que foi feito antes de haver tráfego real e que está em
  reavaliação, sem mudar a decisão.
- O `02` recebe, em blocos separados, a correção de status das chaves de
  Anthropic/Gemini, o registro desta change, as três candidatas ordenadas por
  exposição e o item de credencial A2A externa.

**Non-Goals:**

- **Nenhuma decisão de segurança é revista.** Isso vale para rejeitar `changeme`
  no boot, autenticar o webhook do WAHA, verificar o vazamento de descritores e
  emitir credencial A2A externa.
- **Sem delta de spec**, ver D7.
- **Sem código, sem teste.** A regra da casa de incluir teste unitário em
  frontend e backend não se aplica: nenhum app é tocado, e não há suíte que leia
  documentação.
- **Não tocar o texto (c).**
- **Não criar guarda novo em `scripts/`.** A lacuna do `check-docs.py` é
  registrada, não resolvida.

## Árvore de arquivos tocados

```
.
├── 02-HISTORICO_E_STATUS.md       correção de status + registro + itens abertos
│                                  + adjacência domínio/"agora exposto" (D3)
├── README.md                      (b) Project status, linha do WAHA
├── SECURITY.md                    (b) Webhook authenticity; Known Operational Risk
├── docker-compose.prod.yml        (a) só comentários de cabeçalho, linhas 3 e 8
└── docs/
    ├── a2a-integration.md         (a) autenticação: pré-requisito, endpoint,
    │                                  3 curl, exemplo de card, nota operacional
    ├── architecture.md            (b) tabela de adapters, linha do WAHA
    └── deployment.md              R1 (6 linhas + nota) · (a) §4 TaskJobConsumer
                                   · (b) risco aceito do changeme
```

Nada em `apps/` nem em `libs/`, e nenhum arquivo novo.

## Decisions

### D1. R1: comando completo em toda linha, e a nota do §1 vale para o documento

Todo comando contra o compose de servidor passa a ter a forma
`docker compose --env-file .env.prod -f docker-compose.prod.yml <subcomando>`,
e isso inclui o exemplo de teste local do cabeçalho
(`docker compose -p buteco-prod-verify --env-file .env.prod -f docker-compose.prod.yml ...`).

A nota "`--env-file .env.prod` não é opcional" do §1 passa a dizer
explicitamente que vale para **todo** subcomando: `ps`, `stop`, `run`, `build`
e `up`. Ela também explica por quê: a interpolação avalia o arquivo inteiro
antes do subcomando. O §2 ganha uma linha que remete a ela e nomeia o dano de
improviso, com o `apps/inbox` parado e o migrator não executado.

- **Alternativa rejeitada: um alias ou variável de shell**
  (`DC="docker compose --env-file …"`). Encurta, mas cria estado de shell que o
  copiar-e-colar de um passo isolado não carrega. Um passo colado sozinho numa
  sessão nova falharia de outro jeito. A redundância textual é o preço da
  uniformidade.
- **Alternativa rejeitada: corrigir só o §2**, que era o item registrado.
  Deixaria o `ps` do §1 e o exemplo do cabeçalho errados, e o documento
  continuaria misto. A mistura é o que ensina o improviso.

### D2. A2A: tornar o guia verdadeiro e registrar o buraco, sem prescrever o token do operador

O guia é escrito para integrador externo. Hoje ele omite a autenticação por
inteiro, e isso não é uma frase errada numa nota: é o documento descrevendo a
única configuração em que a integração funcionaria. O que muda:

- **Pré-requisito** (topo): acrescentar que `SendMessage`/`GetTask` exigem
  `Authorization: Bearer <token>`.
- **"O endpoint"**: dizer que a rota exige Bearer e responde `401` sem ele; que
  o agent card é público e declara o esquema; e que **hoje não existe emissão de
  credencial para cliente externo**, com link para a Decision 6 de
  `auth-login-e-servico`, caminho do archive.
- **Os três `curl` contra `/a2a`**: acrescentar
  `-H "Authorization: Bearer <token>"`. O `curl` do card continua sem header,
  porque o card é anônimo.
- **Exemplo de resposta do card**: incluir `securitySchemes`/`securityRequirements`
  **copiados de uma resposta real** de `GET …/agent-card.json`, em
  desenvolvimento. Não pode ser escrito de memória a partir do código, porque a
  forma serializada vem do pacote `A2A` 1.0.0-preview2 e os testes
  (`AgentCardSecuritySchemeTests`) desserializam em vez de fixar o JSON. Se a
  captura não for possível no momento da aplicação, o exemplo fica como está e
  ganha uma frase em prosa dizendo que o card real inclui o esquema Bearer. JSON
  inventado não entra.
- **Nota operacional "Sem autenticação"**: substituída pelo estado real. As rotas
  de cadastro e a A2A exigem token; a limitação de credencial externa é
  consciente e está registrada.

**Achado durante a aplicação (19/09/2026), registrado aqui em vez de ampliar o
escopo em silêncio.** A seção "A regra mais importante: erro não é HTTP
4xx/5xx" dizia que o JSON-RPC responde "**sempre HTTP 200**". Com a
autenticação, isso é falso: sem token, a resposta é `401` com corpo vazio e sem
`WWW-Authenticate`, medido contra `apps/api` em dev. É a mesma afirmação (a),
vista de outro ângulo. Um cliente que siga a regra ao pé da letra trataria o
`401` como sucesso sem `result`. A correção é uma frase de exceção na regra e
na seção nova "Autenticação", sem reescrever a regra.

**O que o guia NÃO faz é recomendar o token do operador como credencial de
integração.** Tecnicamente ele funciona, porque `sub: operator` passa no
`ServiceScopeAuthorizationHandler`. Mas é a senha do painel inteiro, com TTL de
30 min, e prescrevê-lo seria decidir o modelo de credencial externa num
documento, que é justamente a candidata que esta change não decide.

**Integrador externo hoje:** o repositório não registra nenhum. O único
consumidor de `SendMessage` documentado é o `apps/inbox`, com token de serviço.
Até onde o repositório sabe, o dano é de documentação, não de alguém quebrado.
A pergunta vai para o item do `02` (D5), porque o repositório não pode
respondê-la.

### D3. Marca (b): texto padrão, sem domínio e sem "exposto" nos arquivos públicos

Cada aceite (b) ganha uma marca curta, colada ao texto do aceite e não numa
seção separada, para quem lê o aceite ler a marca.

- **Em `docs/`** (pt-BR): *"(Premissa alterada em DD/MM/AAAA: este aceite foi
  feito antes de haver deploy atendendo tráfego real, e hoje vale contra um
  piloto em produção. A decisão não foi revista aqui; está registrada como
  candidata em `02-HISTORICO_E_STATUS.md`.)"*
- **Em `SECURITY.md`/`README.md`** (inglês): o equivalente, *"accepted before any
  deployment served real traffic; under reassessment"*.

**Sem o domínio do piloto e sem a palavra "exposto".**

- **Por quê:** o repositório é público, e `SECURITY.md` é a página que um
  pesquisador lê primeiro. A marca precisa informar que a premissa mudou, não
  virar um aponte-aqui.
- **Nenhuma frase de política muda.** "Does not need to be reported as a new
  finding" e "does not need to be reported" continuam, porque mudá-las já seria
  rever a decisão.
- **Posição da marca em `SECURITY.md`.** Ali a marca faz trabalho de verdade:
  "does not need to be reported" é instrução a terceiros, e o efeito dela
  mudou com a produção. Um pesquisador que encontre o webhook aberto contra o
  piloto está sendo dissuadido de avisar. Por isso a marca vem **antes ou
  junto** da frase de dispensa, nunca como rodapé da seção, para que ninguém
  leia a dispensa isolada. O mesmo vale para o `README.md`, se houver dispensa
  ou remissão a ela.
- **Limite do D3, testado na prática (19/09/2026):** a primeira aplicação
  trocou "known, accepted risk" por "known risk" e pôs "until it changes"
  antes da dispensa, e a revisão reverteu as duas. **A marca pode ser
  acrescentada. A frase do aceite e a da dispensa não podem ser reformuladas,
  porque isso já é rever o aceite.**
- **Alternativa rejeitada: repetir nos arquivos públicos o texto do `02`**
  ("risco não mitigado agora exposto"). É exato, mas publica, na política de
  segurança, a combinação de superfície conhecida e alvo ativo.

**O D3 vale para todo arquivo versionado no repositório público, e isso inclui
o `02`.** O argumento é o mesmo nos cinco arquivos: o repositório é público, e
a marca não pode virar um aponte-aqui. Hoje o `02` publica exatamente a
combinação que o D3 recusa, e com o alvo nomeado: o domínio do piloto a poucas
linhas de "risco não mitigado **agora exposto**" sobre um webhook anônimo.

- **Aplicação no `02`:** o parágrafo "Existe ambiente de produção", a nota de
  correção de 18/09 e o domínio **ficam**, porque é o domínio que torna o
  registro útil. O que muda é a adjacência: "risco não mitigado **agora
  exposto**" vira uma formulação sem convite ("cuja premissa mudou"). Nenhuma
  outra das correções de 18/09 é alterada, e nenhum item sai da lista.
- **A candidata 1 no `02`** descreve a superfície sem repetir o domínio.
- **Exceção deliberada, para ninguém "corrigir":** o `02` é o único arquivo
  versionado que pode conter o domínio do piloto. A checagem de 3.5
  (`grep -rn butecandoespetobar docs SECURITY.md README.md` → vazio) não inclui
  o `02` de propósito. O que o D3 proíbe no `02` é a adjacência entre o alvo
  nomeado e a linguagem de convite, não o domínio.
- **Alternativa rejeitada: relaxar o D3** e deixar as quatro marcas tão diretas
  quanto o `02`. A direção conservadora venceu por dois motivos. Ela é a barata,
  porque troca uma locução no `02`, contra a política de segurança passar a
  apontar o alvo. E o defeito real era a **assimetria sem razão escrita**: o D3
  aplicado a quatro arquivos e não ao quinto, que é justamente o que tem o
  domínio.

### D4. Comentários do compose: corrigir, e provar que é só comentário

- `:3` passa a apontar para
  `openspec/changes/archive/2026-08-26-containerizacao-stack-servidor/design.md`.
- `:8` passa a apontar para `docs/deployment.md`, que é o runbook real. O arquivo
  `deploy/runbook.md` não existe.

A prova de que nada além de comentário mudou é mecânica: a saída de
`podman-compose --env-file .env.prod.example -f docker-compose.prod.yml config`
é byte-idêntica antes e depois da edição.

- **Alternativa rejeitada: registrar as duas linhas no `02` como pendência.**
  Custaria mais escrever e carregar o item do que corrigir duas linhas de
  comentário.

### D5. `02-HISTORICO_E_STATUS.md`: três tipos de edição, que não se misturam

1. **Correção de status, no lugar.** Nos dois itens que afirmam que
   `Anthropic__ApiKey`/`Gemini__ApiKey` "não chegam a processo nenhum"
   (o REABERTO do vazamento de descritores e o item de
   "Abertos por `fix-vazamento-httpclient-chat`"), entra a marca
   *"(Correção de status em DD/MM/AAAA: falso desde `268814d`…)"*, colada ao
   texto e com a mesma forma das correções de 18/09.
   - O texto original fica. A marca aponta
     `docker-compose.prod.yml:93-94,147-148` e o cenário "Provedor de LLM
     configurado no ambiente chega aos dois processos" de `server-deployment`.
   - **Esta é a correção que não pode ficar para depois.** A candidata 3 se
     apoia nela, e registrar a candidata sobre uma premissa que o próprio
     arquivo desmente plantaria o erro seguinte.
2. **Registro desta change**: seção própria, com data, depois do hotfix do
   nginx. Ela fecha o item de `--env-file` de "Abertos por
   `nginx-shell-sem-cache-e-prefixo-messages`", riscado e com ponteiro, no molde
   dos itens resolvidos que já existem no arquivo.
3. **"Abertos por `docs-premissa-producao-e-env-file`"**, nesta ordem:
   1. **Autenticidade do webhook do WAHA** (`apps/inbox`, comportamento).
      - **Por que primeiro:** é o único dos três com superfície pública.
        `POST /webhooks/{channelId:guid}` é anônimo (`ExternalUnauthenticated`)
        e está publicado atrás do Cloudflare; a única barreira é conhecer o GUID.
      - **Gatilho: já cumprido**, com o WAHA ativo no piloto. É a próxima
        exploração de segurança, não "quando tocar `apps/inbox`".
      - **Textos a revisitar quando fechar:** `SECURITY.md` (Webhook
        authenticity), `README.md` (Project status) e `docs/architecture.md`
        (tabela de adapters).
   2. **Rejeitar `changeme` no boot** (apps .NET, comportamento).
      - **Exposição:** nula se o `.env.prod` foi editado, total se não foi. O
        `02` não registra se o piloto foi conferido.
      - **Gatilho:** o próximo deploy completo, ou uma change que toque a
        validação de startup.
      - **Ação barata que precede a change:** conferir o `.env.prod` do piloto.
        Isso é operação, não change.
   3. **Vazamento de descritores com Claude/Gemini** (`apps/workers`).
      - **Natureza:** não é decisão de segurança. É a verificação manual pendente
        de `fix-vazamento-httpclient-chat`.
      - **Peso:** os dois apoios do "sem consequência hoje" caíram. Existe
        produção, e o compose passa as chaves.
      - **Gatilho:** a primeira vez que `ANTHROPIC_API_KEY` ou `GEMINI_API_KEY`
        for preenchida no `.env.prod`.
      - **Pergunta sem resposta no `02`:** quais provedores o piloto usa. Se a
        resposta for "Gemini ou Anthropic", o gatilho já foi cumprido.
   - **Item à parte, não candidata de segurança: credencial para cliente A2A
     externo.**
     - **Origem:** limitação aceita em `auth-login-e-servico`, Decision 6, nunca
       registrada.
     - **Gatilho:** o primeiro consumidor A2A externo real.
     - **Pergunta que acompanha:** há algum hoje? O repositório não registra
       nenhum.
     - **Nota:** o guia A2A passou a declarar a limitação.
   - **Marca no item existente do `TaskJobConsumer`:** o gatilho "subir em
     ambiente onde o RabbitMQ pode não estar pronto no boot" tem agora um alvo
     real. O `docs/deployment.md` dizia que a correção vinha antes do deploy, e
     não veio.

### D6. `docs/deployment.md` §4: `TaskJobConsumer`

A frase "deve virar change própria sequenciada antes desta. Esta change assume
que ela já rodou" passa a dizer o que aconteceu: a sequência não se
concretizou; o stack de servidor está no ar sem essa correção; o item continua
aberto no `02`. O bullet continua na lista de Non-Goals da change original, que
é o que ele é.

### D7. Sem delta de spec

Ver o proposal, "Sem delta de spec". Em resumo:

- **`server-deployment`:** nenhum requisito descreve a sequência de redeploy, e
  ela não deve virar requisito enquanto for procedimento que o sistema não
  garante.
- **`a2a-agent-card`:** já especifica o esquema Bearer, e o doc passa a
  concordar com ela.
- **Consequência:** `openspec validate --strict` acusa o `specs/` vazio, o
  falso positivo conhecido (precedentes: `apps-api-cqrs-mediator` e
  `inbox-fix-concorrencia-orquestrador`).

## Risks / Trade-offs

- **[Um comando sem a flag escapa da correção]** → A verificação é por grep
  sobre o documento inteiro, não por conferência das linhas conhecidas. A task
  exige zero ocorrência de `docker compose`/`docker-compose` com
  `docker-compose.prod.yml` sem `--env-file .env.prod` na mesma linha lógica,
  com os comandos quebrados em duas linhas unidos antes do grep.
- **[`docker compose` se comporta diferente do `podman-compose` medido]** → Não
  muda a correção, já que a flag é necessária em qualquer runtime. Muda só o
  dimensionamento do dano. A ressalva fica escrita no registro, ligada à lacuna
  de paridade já aberta no `02`.
- **[Exemplo de card inventado]** → D2 proíbe. Sem captura real, fica prosa.
- **[Marca (b) lida como mudança de política]** → O texto padrão diz "decisão
  não revista", e nenhuma frase de política muda (D3).
- **[`check-docs.py` passa e é tomado como prova]** → O proposal registra o que
  ele não vê: shell, `.yml` e caminho em crase. O `check-docs.py` é condição
  necessária, não suficiente.
- **[O `02` já publica o domínio do piloto ao lado de "risco … agora exposto"]**
  → Resolvido pelo D3 estendido. A adjacência é quebrada, e o domínio e a nota
  ficam.
- **[Alguém "corrige" a exceção do domínio no `02`]** → A exceção está escrita
  no D3, com a razão.

## Migration Plan

Não há deploy. É uma edição de documentação e de comentários. Para desfazer,
basta reverter o commit. A prova de que o compose não mudou de comportamento é
o `config` byte-idêntico (D4).

## Open Questions

As duas foram **respondidas pelo mantenedor em 19/09/2026** e aplicadas ao
`02`:

- **Quais provedores o piloto usa.** Gemini, mais um serviço compatível com o
  contrato da OpenAI, que entra pelo caminho do provedor OpenAI do sistema, com
  `BaseUrl` próprio. **Não é a OpenAI oficial.** O gatilho da candidata 3 foi
  cumprido, e o item foi reescrito com três alvos de pesos diferentes e com o
  recorte do que a medição pelo caminho OpenAI prova e não prova.
- **Há consumidor A2A externo hoje?** Não, mas há um a caminho, sem data. O item
  da credencial externa deixou de ser nota de rodapé e virou candidata a
  sequenciar. Nada foi desenhado.
