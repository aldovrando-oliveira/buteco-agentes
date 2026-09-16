> **As três frentes são independentes e podem ser aplicadas em separado.** A
> ordem abaixo é de urgência, não de dependência técnica: a frente 1 é de
> segurança e não espera as outras duas.
>
> **Duas decisões bloqueiam tarefas específicas** (Open Questions do
> `design.md`): a rotação da chave, na tarefa 1.1, e as portas de dev, na
> tarefa 4.1.

## 0. Baseline (convenção 19)

- [x] 0.1 Baseline medida em 16/09/2026, sobre `df242c4` (`main`), com a árvore
      limpa. Saída **inteira** em `~/.cache/buteco-agents/fix-stack-baseline-df242c4/`.

      **`apps/workers`: 252/252 aprovados, 0 falhas, 3 m 30 s.**

      Ambiente do runner, registrado por exigência de
      `docs/development.md#testcontainers-com-podman` — **as duas variáveis**, em
      `env.txt`:
      `DOCKER_HOST=unix:///var/folders/.../podman-machine-default-api.sock` e
      `TESTCONTAINERS_RYUK_DISABLED=true`.

      **Escopo da baseline, e por que não é a suíte inteira:** o único insumo de
      teste que esta change modifica hoje é
      `apps/workers/.../appsettings.Development.json` (tarefa 1.2). `nginx.conf`,
      `docker-compose.prod.yml`, `.env*.example` e a documentação não são lidos
      por suíte nenhuma. **Se a decisão 4.1 alterar os `appsettings` de
      `apps/api`/`apps/inbox`, as duas suítes precisam de baseline antes** — e
      isso está na tarefa 4.2.

- [x] 0.2 `AgentDeactivationTests` — falha pré-existente conhecida (reprova em
      classe, passa isolada). **Não se manifestou nesta baseline**: `apps/workers`
      fechou em 252/252. A suíte de `apps/api`, onde ela vive, não foi medida
      (ver escopo em 0.1). Comparação no fechamento é **número contra número**
      contra os 252, não impressão.

## 1. Frente 1 — A chave commitada (segurança, não espera as outras)

- [x] 1.1 **(operacional, FORA do repositório) Rotacionar a chave exposta.**
      **Confirmado pelo mantenedor em 16/09/2026: a chave já foi rotacionada e
      não é mais válida.** A frente 1 deixa de bloquear o arquivamento e passa a
      ser limpeza de arquivo.

      Fica registrado o que a tornava bloqueante: a chave entrou em `19a5bec`
      (12/09/2026) e **está no histórico**, então esvaziar o arquivo não a
      invalidaria em nenhum clone, fork ou checkout de CI já existente. Rotação
      era a correção; remoção é higiene.

- [x] 1.2 (`apps/workers`) `appsettings.Development.json:18-19` — `OpenAI.BaseUrl`
      e `OpenAI.ApiKey` passam a `changeme`, mesmo formato de `Anthropic.ApiKey`
      (`:27`) e `Gemini.ApiKey` (`:30`) no próprio arquivo.

- [x] 1.3 (`apps/workers`) Varredura por outros segredos em arquivo versionado.

      **Os dois primeiros padrões que usei estavam defeituosos, e o controle é o
      que revelou isso** — registrado porque a régua vale mais que o achado:

      | Tentativa | Defeito | Como apareceu |
      |---|---|---|
      | 1ª — `(?!changeme)` | **lookahead é PCRE, não ERE**; `grep -E` não o suporta. Aquele "zero achados" não valia para a cláusula de `ApiKey` | caso de controle explícito |
      | 2ª — alternação com ramo vazio | `ugrep: error at position 16, empty (sub)expression` | a própria saída |
      | 3ª — por **nome** de campo (`ApiKey\|BaseUrl\|Password\|Token\|Secret`) | não listava `CredentialEncryptionKey`, e por isso **perdeu duas chaves AES** | só apareceu ao buscar por **formato do valor** |

      **A régua: buscar segredo por formato do valor, não por nome do campo.**
      Nome de campo é a lista do que se espera encontrar; formato base64 de 32
      bytes acha o que não estava na lista.

      Controle que provou que o pipeline rodava: `changeme` casa em 5 arquivos
      versionados.

      **Achados, em duas classes distintas — nenhum deles corrigido aqui:**

      1. **Chaves de criptografia/assinatura de desenvolvimento**, em
         `appsettings.Development.json` versionado:
         `apps/api:33` (`Mcp:CredentialEncryptionKey`), `apps/api:36` e
         `apps/inbox:32` (`Auth:TokenSigningKey`, o mesmo valor nos dois),
         `apps/inbox:12` (`Inbox:CredentialEncryptionKey`). São material
         criptográfico **local de dev**, não credencial contra serviço externo —
         classe diferente da chave da 1.2. A de `apps/api:33` é **a mesma** usada
         como constante de fixture em nove arquivos de teste.
      2. **`apps/inbox/.../appsettings.Development.json:18`** — URL de túnel
         Cloudflare commitada e **morta** (conferido: `HTTP 000`, inalcançável).
         Não é segredo; é valor de máquina que força contorno manual, e
         `RUNBOOK-LOCAL.md` (B3) manda sobrescrevê-la a cada execução.

      **Os dois ficam para decisão do mantenedor** — nenhum estava na lista de
      oito defeitos do `proposal.md`, e absorvê-los em silêncio seria a expansão
      de escopo que a convenção 1 proíbe.
- [x] 1.4 (documentação) `docs/development.md` — registrar que o worker agora
      exige `OpenAI__BaseUrl`/`OpenAI__ApiKey` no ambiente para o Caminho
      híbrido, e que sem eles **toda task termina `failed`**. É trabalho novo
      para quem desenvolve (F1.2 do `design.md`), e o custo se escreve.

## 2. Frente 2 — Os três prefixos do nginx

- [x] 2.1 **Reenumerar os prefixos antes de editar**, e não copiar a lista deste
      documento — é a régua que achou o terceiro:

      ```
      grep -rhoE '"/[a-z0-9-]+' --include="*.cs" apps/api/src/Buteco.Api/ | sort -u
      grep -rhoE '"/[a-z0-9-]+' --include="*.cs" apps/inbox/src/Buteco.Inbox/ | sort -u
      ```

      Conferir cada prefixo devolvido contra os dois blocos de
      `apps/frontend/deploy/nginx.conf`. **Se a enumeração devolver um quarto
      ausente, ele entra** — a lista de hoje é de 16/09/2026.

      Descartar `/health` com o motivo escrito: os healthchecks rodam dentro de
      cada contêiner contra o próprio processo, não atravessam o nginx.

- [x] 2.2 (`apps/frontend`) `deploy/nginx.conf:44` — acrescentar
      `knowledge-bases` e `knowledge-index` ao bloco de `apps/api`.

      `knowledge-bases` **precisa** estar neste bloco, não é escolha de
      organização: é rota de página do `react-router`
      (`apps/frontend/src/app/routes.tsx:60`) **e** prefixo de API, mesmo caso de
      `agents`/`mcp-servers`. O tratamento de `Sec-Fetch-Mode` que o bloco já
      aplica (`nginx.conf:45-47`) é o que faz um refresh em
      `/knowledge-bases/{id}` devolver o SPA em vez de JSON.

- [x] 2.3 (`apps/frontend`) `deploy/nginx.conf:55` — acrescentar `internal` ao
      bloco de `apps/inbox`. Não é rota de página; entrar no bloco é inócuo.
- [ ] 2.4 **BLOQUEADA no ambiente atual — verificar os três com o stack no ar.**

      **Feito:** `nginx -t` sobre a configuração editada aprova
      (`podman run --rm -v .../nginx.conf:/etc/nginx/conf.d/default.conf nginx:alpine nginx -t`
      → *"syntax is ok / test is successful"*).

      **NÃO feito, e o `nginx -t` não substitui:** sintaxe válida não é
      roteamento correto. Exige `.env.prod` completo, provedor de LLM alcançável
      e um canal real — indisponíveis aqui. Registrado em `02`.
      Cada prefixo responde **JSON**, não `200` com HTML:
      - `GET /knowledge-bases` → lista de bases;
      - `GET /knowledge-index/diagnostics` → proveniência (lista vazia é resposta
        válida);
      - o round-trip de push notification completa — **agente responde e a
        resposta sai no canal**, que é o sintoma que o defeito produzia.

      O terceiro é o único que exige round-trip real; os dois primeiros se veem
      no painel.

## 3. Frente 3 — Configuração que o compose não entrega

- [x] 3.1 **Verificar empiricamente a sintaxe de variável obrigatória
      (`${VAR:?mensagem}`) ANTES de converter qualquer variável.** O `design.md`
      (F3.3) a declarava a partir da especificação do Compose, sem medir.

      **MEDIDO em 16/09/2026, e a sintaxe se sustenta — parcialmente.**

      | Runtime | Resultado |
      |---|---|
      | `podman compose` (podman 5.8.3, `podman-compose` Python) | ✓ **verificado**. Sem a variável: `ValueError: required variable TESTE_OBRIGATORIA is missing a value: <mensagem customizada>`, exit 1, nenhum serviço criado. Com a variável: renderiza normalmente |
      | `docker compose` | ✗ **NÃO verificado** — Docker ausente nesta máquina (`command -v docker` → ausente) |

      **A mensagem customizada é propagada na íntegra**, que é o ponto: o erro
      instrui em vez de só apontar ausência.

      **O que fica sem medição, e não se assume:** o alvo real de produção é
      `docker compose`, e ele não foi testado aqui. A sintaxe é da especificação
      do Compose e ambos a implementam, mas **paridade não medida não é paridade
      verificada**. Quem aplicar num ambiente com Docker deve repetir o teste —
      são dois comandos.

- [x] 3.2 (`docker-compose.prod.yml`) Interpolar a seção `Embedding` em
      `workers`: `Embedding__Provider`, `Embedding__Model` e
      `Embedding__Dimensions`. Sem isso o boot passa e a **primeira indexação**
      falha com modelo vazio e dimensão 0.
- [x] 3.3 (`docker-compose.prod.yml`) Interpolar `Anthropic__ApiKey` e
      `Gemini__ApiKey` em `api` **e** em `workers` — a mesma chave é lida pelos
      dois, um para presença e outro para uso
      (`docs/configuration.md:44`). Fecha o item aberto de
      `02-HISTORICO_E_STATUS.md:5266-5273`.
- [x] 3.4 (`docker-compose.prod.yml`) Converter para variável obrigatória as que
      produzem falha silenciosa quando vazias — no mínimo `POSTGRES_USER`,
      `POSTGRES_PASSWORD`, `RABBITMQ_USER`, `RABBITMQ_PASSWORD`,
      `AUTH_TOKEN_SIGNING_KEY`, `AUTH_OPERATOR_USERNAME`,
      `AUTH_OPERATOR_PASSWORD_HASH`, `MCP_CREDENTIAL_ENCRYPTION_KEY`,
      `INBOX_CREDENTIAL_ENCRYPTION_KEY`, `TZ` e `STACK_HTTP_PORT`.

      A mensagem de erro **diz o que fazer**, não só o que faltou: *"defina X —
      use `--env-file .env.prod`"*. Erro que não instrui só adia a descoberta.

      **Não** declarar `env_file:` achando que resolve: ele injeta variável
      dentro do contêiner, e o problema é interpolação em tempo de parse (F3.3).
- [x] 3.5 (`.env.prod.example`) Acrescentar `EMBEDDING_MODEL` e
      `EMBEDDING_DIMENSIONS`, com o aviso de que a coluna do índice é
      `vector(4096)` fixa na migration — o modelo precisa emitir exatamente 4096
      dimensões, e `text-embedding-3-large` (3072) **não serve** sem alterar a
      migration.
- [x] 3.6 (`.env.prod.example`) Descomentar `ANTHROPIC_API_KEY` e
      `GEMINI_API_KEY`, agora que passam a ter consumidor.

## 4. Frente 3 (continuação) — As portas de dev

- [x] 4.1 **DECIDIR e registrar aqui, antes de editar** (Open Question 1 do
      `design.md`): alinhar `.env.example` para `15532`/`15772`, ou os
      `appsettings.Development.json` para `5432`/`5672`?

      Recomendação do `design.md`: alinhar o `.env.example`, porque o
      `appsettings` versionado é o que os apps de fato leem e as portas altas
      provavelmente existem para evitar colisão com serviços locais. **Conferir
      essa hipótese** — se as portas altas foram acidentais, a recomendação
      inverte.
- [x] 4.2 Executar a saída decidida, nos arquivos que ela implicar.
- [x] 4.3 (documentação) Conferir que `docs/development.md` e o `README.md`
      continuam verdadeiros depois da decisão — os dois citam `cp .env.example
      .env` e portas.

## 5. Documentação

Só o que a correção tornar verdadeiro (convenção 1: nada de arrastar achado de
documentação de outra linha para cá).

- [x] 5.1 (documentação) `docs/deployment.md` — a mudança de comportamento do
      `--env-file` (passa a falhar no parse em vez de silenciar), e as variáveis
      novas na tabela "Variáveis por processo".
- [x] 5.2 (documentação) `docs/configuration.md` — `Embedding__*` no stack de
      servidor, e a linha de `ANTHROPIC_API_KEY`/`GEMINI_API_KEY` (`:246`) passa
      a ser verdadeira em vez de promessa.
- [x] 5.3 (documentação) `CHANGELOG.md` — entrada escrita **depois** das edições
      e conferida contra elas. Precedente: o `CHANGELOG` da etapa 4 nasceu falso,
      escrito pela própria change que tornou a frase falsa.

## 6. Fechamento

- [x] 6.1 Rodar as suítes e comparar com a baseline de 0.1, **número contra
      número**.
- [x] 6.2 Rodar `python3 scripts/check-docs.py` e `openspec validate --all`.
      Registrar a saída como o que é: nenhum dos dois enxerga roteamento de
      nginx, variável de compose ou segredo em arquivo versionado. **Verde aqui
      não é cobertura de nada que esta change corrigiu.**
- [x] 6.3 Varredura larga de referências cruzadas sobre os artefatos desta
      change — os dois validadores não enxergam referência de tarefa:

      ```
      grep -rnoE "(item|tarefa|seção|Decisão)s? [0-9]+(\.[0-9]+)+[a-z]?" openspec/changes/fix-stack-servidor-lacunas/
      grep -rnoE "\([0-9]+\.[0-9]+[a-z]?\)" openspec/changes/fix-stack-servidor-lacunas/
      ```

      **São dois comandos, e o segundo existe porque o primeiro já falhou aqui.**
      O padrão exige a palavra (`tarefa`, `item`, …) antes do número e não
      captura referência em parêntese nu — foi assim que um `(4.4)` no cabeçalho
      deste arquivo, apontando para uma tarefa que é **4.1**, passou pela
      varredura na primeira vez que ela rodou.

      Cada resultado se confere contra o alvo real, não só contra a existência do
      número.
- [x] 6.4 **Registrar no `02-HISTORICO_E_STATUS.md`** que os três defeitos de
      roteamento só apareceram porque alguém rodou o stack numa máquina nova e
      escreveu o que precisou contornar. **O gatilho que sai daqui:** contorno
      manual que entra em runbook é candidato a defeito na origem, e a pergunta
      *"por que isto não vem certo do repositório?"* se faz na hora de escrever o
      contorno, não meses depois.
