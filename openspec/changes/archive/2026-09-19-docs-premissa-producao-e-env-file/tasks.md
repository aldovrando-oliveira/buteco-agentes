> **Nenhuma tarefa roda num app.** Nenhuma toca `apps/api`, `apps/workers`,
> `apps/inbox`, `apps/frontend` ou `libs/`. Os arquivos editados são
> documentação (`docs/`, `SECURITY.md`, `README.md`, `02-HISTORICO_E_STATUS.md`)
> e dois comentários de `docker-compose.prod.yml`. Não há tarefa de deploy.
>
> **Não reabrir a varredura.** As ocorrências (a)/(b) estão fixadas no
> `proposal.md` e no `design.md`. O texto (c) não é tocado. Se aparecer
> ocorrência nova durante a edição, ela é registrada no `design.md`, sem
> ampliar o escopo em silêncio.

## 0. Baseline

- [x] 0.1 Registrar que **não há suíte a medir**, porque nenhuma lê
      documentação. A baseline é:
      (a) `python3 scripts/check-docs.py` → `Integridade da documentação: OK`;
      (b) a saída de
      `podman-compose --env-file .env.prod.example -f docker-compose.prod.yml config`,
      salva em `$CLAUDE_JOB_DIR/tmp` (ou outro diretório temporário), para a
      comparação de 4.3;
      (c) o grep de 1.1 sobre o arquivo atual, com as ocorrências sem a flag
      listadas.

## 1. R1: `--env-file .env.prod` uniforme em `docs/deployment.md`

- [x] 1.1 **Enumerar, não lembrar.** Unir as linhas de continuação e listar
      todo comando `docker compose`/`docker-compose` que referencie
      `docker-compose.prod.yml`. Comando testado na proposta, que une a
      continuação de linha e o `>` de citação e para cada invocação no nome do
      arquivo:

      ```
      perl -0ne 's/\\?\n>?[ \t]*/ /g; while(/(docker compose (?:(?!docker compose ).)*?docker-compose\.prod\.yml)/g){print "$1\n"}' docs/deployment.md
      ```

      Complementar com `grep -n 'docker-compose -' docs/deployment.md`, para o
      CLI legado, que o comando acima não pega. Hoje o complemento volta vazio.
      Esperado hoje: **9 invocações, 6 sem a flag**:
      - o exemplo de teste local do cabeçalho (20-21, errado);
      - o §1 passo 2 (41, certo);
      - o §1 passo 3 (65, errado);
      - o §2 (86-89, quatro errados);
      - o "Redeploy só do frontend" (104-105, certos).

      Se a enumeração achar algo além disso, entra, e o fato vai para o
      `design.md`.
- [x] 1.2 Acrescentar `--env-file .env.prod` a cada comando errado, na forma do
      D1: `docker compose --env-file .env.prod -f docker-compose.prod.yml …`,
      e no cabeçalho `docker compose -p buteco-prod-verify --env-file .env.prod -f docker-compose.prod.yml ...`.
      Os comandos já certos são **conferidos** caractere a caractere, e não
      presumidos.

      **Feito em 19/09/2026.** As seis linhas foram corrigidas. Os três comandos
      já certos (§1 passo 2 e os dois do "Redeploy só do frontend") foram
      conferidos pela saída do extrator de 1.5, que devolve as 9 invocações com
      a mesma sequência literal `docker compose --env-file .env.prod -f
      docker-compose.prod.yml`. Um `LC_ALL=C grep '[^ -~]'` sobre as linhas com
      `--env-file` só acha caractere não-ASCII na prosa (`—`, acentos), nunca
      num comando. Não há hífen tipográfico nem espaço não separável.
- [x] 1.3 Estender a nota "`--env-file .env.prod` não é opcional" do §1 (D1).
      Ela passa a dizer que vale para **todo** subcomando (`ps`, `stop`, `run`,
      `build`, `up`) e por quê: a interpolação avalia o arquivo inteiro antes
      do subcomando.
- [x] 1.4 No §2, logo antes do bloco da sequência, pôr uma linha que remete à
      nota do §1 e nomeia o dano de improviso: flag num passo e não no
      seguinte deixa o `apps/inbox` parado e o migrator não executado.
- [x] 1.5 **Verificar a uniformidade:** repetir o comando de 1.1 com
      `| grep -vc -- '--env-file .env.prod'` no fim. O esperado é **0**, contra
      6 na baseline, e o total de invocações continua 9, ou o número que 1.1
      achou. Colar a saída como evidência nesta tarefa.

      **Evidência, em 19/09/2026:** 9 invocações (`wc -l` → `9`), **0** sem a
      flag (`grep -vc` → `0`), e `grep -n 'docker-compose -'` sem ocorrência
      (exit 1). Na baseline eram 9 e 6.
- [x] 1.6 (Opcional) Repetir a medição do `podman-compose` com a flag, num
      diretório isolado, com os comandos exatos do §2 corrigido e
      `.env.prod.example` no lugar de `.env.prod`. Usar só `config`, ou
      subcomandos que não criem container: o objetivo é mostrar que a
      interpolação passa, não subir o stack.

      **Feito com `podman-compose --dry-run`** (1.6.0), com os quatro passos
      do §2 literais e `.env.prod.example` copiado como `.env.prod`:
      - **Interpolação:** passou nos quatro passos. Zero `required variable`,
        contra quatro de quatro sem a flag, na exploração.
      - **`stop inbox`:** exit 0.
      - **`build`, `run --rm migrator` e `up -d …`:** exit 1 **depois** da
        interpolação, com `Dockerfile not found`. O diretório isolado só tem
        o compose, então esse é o erro esperado, e ele prova que o
        processamento do arquivo já tinha passado.
      - **Efeito colateral:** nenhum container e nenhuma imagem `r1probe`
        criados.

## 2. R2 (a): afirmações falsas

- [x] 2.1 `docs/a2a-integration.md`, **Pré-requisito** (topo): acrescentar que
      `SendMessage`/`GetTask` exigem `Authorization: Bearer <token>` (D2).
- [x] 2.2 `docs/a2a-integration.md`, **"O endpoint"**: declarar que a rota
      exige Bearer e responde `401` sem ele, e que o agent card é público e
      declara o esquema. Declarar também que **hoje não existe emissão de
      credencial para cliente externo**, com link para
      `openspec/changes/archive/2026-08-22-auth-login-e-servico/design.md`
      (Decision 6). **Não** prescrever o token do operador como credencial de
      integração (D2).

      **Feito como subseção "Autenticação"** dentro de "O endpoint", e o
      header entrou também no bloco da requisição. O `401` foi **medido**
      contra `apps/api` em dev: corpo vazio, sem `WWW-Authenticate`. O token
      do operador é citado só para dizer que **não** é credencial de
      integração. **Achado:** a regra "sempre HTTP 200" era falsa para o `401`.
      Ganhou uma frase de exceção, registrada no `design.md`, D2.
- [x] 2.3 `docs/a2a-integration.md`: acrescentar
      `-H "Authorization: Bearer <token>"` aos **três** `curl` contra
      `/agents/<agentId>/a2a`. O `curl` do agent card fica sem header. Conferir
      por grep que nenhum `curl … /a2a` ficou sem o header.

      **Evidência:** os três `curl` (linhas 178, 239 e 303 depois da edição)
      têm o header. O extrator de blocos `curl … /a2a` sem
      `Authorization: Bearer` devolve **0**.
- [x] 2.4 `docs/a2a-integration.md`, **exemplo de resposta do card**: capturar
      uma resposta real de `GET /agents/<id>/.well-known/agent-card.json`
      contra `apps/api` em desenvolvimento (a rota é anônima) e copiar
      `securitySchemes`/`securityRequirements` **exatamente como vieram**. Sem
      captura possível, o exemplo não muda e ganha uma frase em prosa dizendo
      que o card real inclui o esquema Bearer. **JSON escrito de memória não
      entra** (D2). Registrar nesta tarefa qual dos dois caminhos foi seguido.

      **Caminho seguido: captura real.** Em 19/09/2026, com `apps/api` de pé em
      dev, `dotnet run`, e contra um agente existente do banco de dev,
      `GET …/agent-card.json` respondeu `200`. Os dois campos foram copiados
      exatamente como vieram, `null` inclusive. O processo foi encerrado depois.
      O card real traz também `documentationUrl`, `iconUrl`, `provider` e
      `signatures`, que o exemplo já omitia. O doc passou a dizer que o
      exemplo é resumido, sem acrescentar valores desses campos.
- [x] 2.5 `docs/a2a-integration.md`, **"Notas operacionais"**: substituir o
      bullet "Sem autenticação" pelo estado real, com remissão à seção do
      endpoint (2.2).
- [x] 2.6 `docs/deployment.md` §4, bullet do `TaskJobConsumer`: trocar "deve
      virar change própria sequenciada antes desta. Esta change assume que ela
      já rodou." pelo que aconteceu (D6). A sequência não se concretizou, o
      stack está no ar sem a correção, e o item segue aberto no `02`.
- [x] 2.7 `docker-compose.prod.yml`, **só comentários** (D4). Linha 3 aponta
      para
      `openspec/changes/archive/2026-08-26-containerizacao-stack-servidor/design.md`,
      e linha 8 aponta para `docs/deployment.md`. Conferir que os dois caminhos
      existem.

      **Os dois caminhos existem** (`ls`). O caminho do archive é mais longo e
      quebrou a linha 3 em duas. As linhas 4-5 foram rearranjadas, com o mesmo
      texto. A prova de "só comentário" é a 4.3.

## 3. R2 (b): marcar, sem decidir

- [x] 3.1 `docs/deployment.md`, "Risco aceito: placeholder `changeme` não é
      rejeitado no boot": acrescentar a marca padrão pt-BR do D3, com a data do
      dia da aplicação, colada ao texto do aceite.
- [x] 3.2 `docs/architecture.md`, tabela de adapters, linha do WAHA: marca
      padrão pt-BR, curta o bastante para caber na célula ou como nota logo
      abaixo da tabela.

      **Feito nas duas formas:** "(premissa alterada, ver nota abaixo)" na
      célula, e a marca padrão logo abaixo da tabela.
- [x] 3.3 `SECURITY.md`, "Webhook authenticity" e "Known Operational Risk":
      marca em inglês do D3 (*accepted before any deployment served real
      traffic; under reassessment*). **Sem domínio, sem "exposed", e nenhuma
      frase de política alterada.** "Does not need to be reported" fica.
      **A marca vem antes ou junto da frase de dispensa, nunca como rodapé da
      seção.** Conferir, lendo a seção editada, que não existe leitura da
      dispensa isolada da marca.

      **Feito, nas duas seções, na forma "marca; dispensa":**
      *"It was accepted before any deployment served real traffic, and that
      acceptance is under reassessment;"* seguido, na mesma frase, de
      "until it changes, … does not need to be reported".
      - **Leitura conferida:** a dispensa não aparece em frase nenhuma sem a
        marca imediatamente antes.
      - **O que foi acrescentado além da marca:**
        - Em "Webhook authenticity", "known, accepted risk" virou "known
          risk", porque o "accepted" passou para a marca.
        - O conector temporal **"until it changes"** entrou nas duas seções.
        - **Nenhuma frase de política foi removida ou invertida.** "Does not
          need to be reported" continua nas duas.

      **Revertido na revisão (7.4).** As duas edições além da marca saíram.
      O texto original foi restaurado, e a marca ficou entre parênteses logo
      antes de "and does not need to be reported", nas duas seções.
- [x] 3.4 `README.md`, "Project status", bullet do WAHA: mesma marca em inglês,
      com as mesmas restrições de 3.3. A marca fica no próprio bullet, antes da
      remissão ao `SECURITY.md`, e não depois dela.
- [x] 3.5 Conferir que nenhuma marca menciona o domínio do piloto:
      `grep -rn butecandoespetobar docs SECURITY.md README.md` deve voltar vazio.

      **Vazio** (exit 1). Complemento: `exposed`/`exposto` não aparece em
      `SECURITY.md`, `README.md` nem `docs/deployment.md`. As duas ocorrências
      em `docs/architecture.md` (355 e 387) são sobre nome de tool exposto, sem
      relação com esta change.

## 4. Verificação dos arquivos fora do `02`

- [x] 4.1 `python3 scripts/check-docs.py` → OK. O novo link para o archive de
      `auth-login-e-servico` (2.2) precisa resolver.

      **`Integridade da documentação: OK`**, exit 0. O link novo e os dois
      links novos para o `02`, em `deployment.md` e `architecture.md`,
      resolvem.
- [x] 4.2 Registrar nesta tarefa o que o `check-docs.py` **não** cobriu nesta
      change: os comandos de shell do R1, o `.yml` do compose e caminho em
      crase fora de link. As tarefas 1.5, 2.3, 2.7 e 3.5 são a verificação real
      desses pontos.

      **Não coberto, e verificado por outra via:**
      - comandos de shell do R1 (1.5);
      - `curl` do guia A2A (2.3);
      - `.yml` do compose (2.7 e 4.3);
      - ausência do domínio (3.5).

      **Também fora do alcance dele:** as **âncoras** internas. Ele remove a
      âncora antes de resolver, então os quatro links `#autenticação` novos
      não são checados. Conferidos à mão: o cabeçalho `### Autenticação`
      existe, e o slug do GitHub mantém o acento, como nas âncoras
      `#método-sendmessage` já existentes no mesmo arquivo.
- [x] 4.3 Comparar a saída do `config` com a baseline de 0.1(b). Ela deve ser
      **byte-idêntica** (`diff` vazio), o que prova que a edição de 2.7 foi só
      comentário.

      **Byte-idêntica:** `diff` vazio (exit 0) e `cmp` sem diferença, sobre as
      129 linhas de `config` com `.env.prod.example`.
- [x] 4.4 `git diff --stat`: nenhum arquivo em `apps/`, `libs/`, `deploy/` ou
      `openspec/specs/`.

      **Zero arquivos** nesses caminhos. Até aqui foram 6 arquivos, com +111
      e −32 linhas. O `02` entra na seção 5.

## 5. `02-HISTORICO_E_STATUS.md` (D5)

- [x] 5.1 **Correção de status, primeiro e em bloco próprio.** Acrescentar a
      marca *"(Correção de status em DD/MM/AAAA: …)"* colada ao texto dos dois
      itens que afirmam que `Anthropic__ApiKey`/`Gemini__ApiKey` "não chegam a
      processo nenhum" no compose:
      (i) o **REABERTO** do vazamento de descritores, em "Itens em aberto";
      (ii) o item em "Abertos por `fix-vazamento-httpclient-chat`".
      A marca diz que isso é falso desde `268814d`
      (`fix-stack-servidor-lacunas`), aponta
      `docker-compose.prod.yml:93-94,147-148` e o cenário "Provedor de LLM
      configurado no ambiente chega aos dois processos" de
      `server-deployment`, e **não apaga o texto original**. No (i), dizer
      também que a frase "Enquanto aquele não for resolvido, este não tem como
      disparar nem em servidor" deixou de valer.
- [x] 5.2 **Registro desta change**, em "Changes aplicadas", logo depois do
      hotfix do nginx. O que entra:
      - o que foi corrigido (R1 uniforme; as três (a); as marcas (b));
      - a medição do R1, com `podman-compose` 1.6.0, falha antes de agir e
        nenhum container criado, e a ressalva de `docker compose` não medido;
      - a declaração de que não há delta de spec, e por quê;
      - o que o `check-docs.py` não cobre.
- [x] 5.3 Fechar o item de `--env-file` em "Abertos por
      `nginx-shell-sem-cache-e-prefixo-messages`", riscado e com ponteiro para
      esta change, no molde dos itens resolvidos do arquivo.
- [x] 5.4 Criar "Abertos por `docs-premissa-producao-e-env-file`" com as três
      candidatas **nesta ordem, por exposição**, cada uma com natureza,
      exposição, gatilho e o que revisitar quando fechar, conforme o D5:
      (1) autenticidade do webhook do WAHA, gatilho **já cumprido**, com a
      superfície descrita **sem repetir o domínio do piloto** (D3);
      (2) rejeitar `changeme` no boot, com a conferência do `.env.prod` do
      piloto como ação operacional precedente;
      (3) vazamento de descritores com Claude/Gemini, com a pergunta "quais
      provedores o piloto usa". Registrar em uma frase que o argumento que
      sustentava as três caiu: elas passaram de "risco aceito em dev" para
      "risco aceito contra um stack real".
- [x] 5.5 No mesmo bloco, o item à parte **credencial para cliente A2A
      externo**: a origem (Decision 6 de `auth-login-e-servico`, nunca
      registrada), o gatilho (o primeiro consumidor externo real), a pergunta
      "há algum hoje?" e a nota de que o guia A2A passou a declarar a
      limitação.
- [x] 5.6 No item existente do `TaskJobConsumer`, em "Itens em aberto", marcar
      que o gatilho "subir em ambiente onde o RabbitMQ pode não estar pronto no
      boot" tem agora alvo real, e que o `docs/deployment.md` supunha a
      correção feita antes do deploy.
- [x] 5.7 **D3 no `02`: quebrar a adjacência**, no parágrafo "Existe ambiente
      de produção" e na nota de correção de 18/09 logo abaixo dele.
      - **Manter** o parágrafo, a nota e o domínio. O domínio é o que torna o
        registro útil, e o `02` é a exceção deliberada da checagem 3.5 (D3).
      - **Trocar** "risco não mitigado **agora exposto**" por uma formulação
        sem convite, do tipo "cuja premissa mudou".
      - **Não** apagar o item da lista, não reescrever a nota e não alterar
        nenhuma outra das correções de 18/09.
      - **Conferir** que "agora exposto" não sobra perto do domínio, com
        `grep -n "agora exposto\|butecandoespetobar" 02-HISTORICO_E_STATUS.md`
        antes e depois. Colar as duas saídas aqui.

      **Antes:**
      ```
      38:`https://agente.butecandoespetobar.com.br`, com o stack de servidor
      52:risco não mitigado **agora exposto**, com o WAHA ativo no piloto; o
      3421:(`docs/deployment.md` §2), contra `https://agente.butecandoespetobar.com.br`:
      3570:  adjacência entre o domínio e "risco não mitigado **agora exposto**" foi
      ```

      A linha 3570 era **o registro desta própria change (5.2)**, que citava a
      frase e a reintroduzia. Foi reescrita para "a linguagem de exposição
      sobre o webhook".

      **Depois:**
      ```
      38:`https://agente.butecandoespetobar.com.br`, com o stack de servidor
      3421:(`docs/deployment.md` §2), contra `https://agente.butecandoespetobar.com.br`:
      ```

      A linha 52 passou a dizer "risco não mitigado **cuja premissa mudou**".
      Resíduo **deixado de propósito**: "está exposto", no item do WAHA em
      "Itens em aberto" (linha 4539). É outra correção de 18/09, que esta
      tarefa proíbe alterar, e fica a ~4500 linhas do domínio. Levado ao
      mantenedor no relatório.

## 6. Fechamento

- [x] 6.1 `openspec validate docs-premissa-producao-e-env-file --strict`.
      **Critério mecânico:** a saída tem **exatamente um** `✗ [ERROR]`, e o
      texto dele contém `Change must have at least one delta`. É o falso
      positivo do proposal, com `specs/` vazio de propósito. Qualquer saída
      diferente, seja no número de erros, seja no texto, é erro real e é
      corrigido, não interpretado.

      **Resultado, em 19/09/2026:** exit 1, `grep -c '✗ \[ERROR\]'` → `1`, e
      `grep -c 'Change must have at least one delta'` → `1`. Critério
      cumprido.
- [x] 6.2 `python3 scripts/check-docs.py` final → OK.

      **`Integridade da documentação: OK`**, exit 0, depois das edições do
      `02`.
- [x] 6.3 Deixar o trabalho na árvore, **sem commit**. Relatar os arquivos
      tocados e que o commit está pendente de autorização.

## 7. Ajustes finais da revisão (19/09/2026)

> Vindos das respostas do mantenedor às duas Open Questions e da revisão do
> `SECURITY.md`. Continuam só documentação.

- [x] 7.1 `02`, candidata 3 (vazamento de descritores): reescrita.
      - **Gatilho cumprido**, porque o piloto usa Gemini.
      - **O próximo passo é executar a verificação já desenhada**, não abrir
        exploração.
      - **Três alvos:** o caminho OpenAI é executável agora; o Gemini tem
        credencial no piloto; a Anthropic segue sem credencial.
      - **O recorte do que o caminho OpenAI prova e não prova.**
      - **Linhas conferidas do compose:** `OpenAI__BaseUrl`/`OpenAI__ApiKey`
        em `api` 88-89 e `workers` 145-146, sem `:?`.
      - **O provedor do piloto não é a OpenAI oficial.**
- [x] 7.2 `02`, correção de status 5.1: as duas marcas passaram a enumerar a
      lista real de variáveis de provedor que o compose passa, com as linhas
      atuais.
      - **As de OpenAI** estão desde `de9bbae` (05/09), e o item original do
        `02` já as registrava, de modo que a afirmação falsa era só a de
        Anthropic/Gemini.
      - **Acrescentado:** nenhuma das quatro variáveis do host é obrigatória na
        interpolação.
- [x] 7.3 `02`, item da credencial A2A externa: passou a candidata a sequenciar.
      O texto registra que há um consumidor a caminho, sem data, que o buraco é
      real, e o desenho que a Decision 6 recusou por escopo. Nada foi desenhado.
- [x] 7.4 `SECURITY.md`: revertidos "known risk" e "until it changes". O limite
      do D3 foi registrado no `design.md`.
- [x] 7.5 O "está exposto" da linha ~4539 do `02` **não foi tocado**, e a 5.7
      continua valendo.
