Todas as tarefas rodam no worktree
`/Users/aldovrando/Projetos/buteco-agentes-frontend-mcp-autenticacao-header-customizado`,
branch `feat/168-frontend-mcp-autenticacao-header-customizado`. Nenhuma toca
`apps/api`, `apps/workers`, `apps/inbox` nem `apps/connectors`.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/frontend`) Antes de qualquer edição de código, conferir a
      máquina e rodar a baseline: `uptime`, `ps aux | sort -nrk 3 | head` e
      `podman ps` **antes** de cada comando (convenção 19); abortar e esperar se
      o `load average` passar de ~1× o número de núcleos, ou se houver suíte ou
      conferência de outro worktree rodando. Depois: `npm run lint`,
      `npm run format:check`, `npm run test`, `npm run build`. **Saída completa
      de cada um em arquivo** fora do diretório de sessão
      (`~/.cache/buteco-agents/168-baseline/<comando>.log`), sem `| tail` nem
      `| grep` no cano. Registrar aqui: o commit, o resultado de cada um, a
      **contagem medida** de arquivos e casos da suíte (nunca de memória), a
      duração, o `load` na largada e no fim, e a lista de arquivos que já
      reprovam no `format:check` — só esses contam como pré-existentes.
      Feito em 18/10/2026, em `f505114`, sem nenhuma edição de código, depois do
      `npm ci` deste worktree; 12 núcleos. Antes de começar: nenhuma suíte nem
      conferência de outro worktree no `ps`; `podman ps` com o Postgres e o
      RabbitMQ do compose, VM a ~25% de CPU; Vite da 5173 e `apps/api` da 5017
      do checkout principal no ar, ociosos. Saídas completas em
      `~/.cache/buteco-agents/168-baseline/`.
      - `npm run lint`: **verde** (saída 0), 13s. Load 2,86 → 3,12.
      - `npm run format:check`: **reprova** (saída 1) em **52 arquivos**, 8s.
        Load 3,12 → 3,42. Da feature MCP só
        `src/features/mcp-servers/utils/agentUsage.ts`, que esta change não
        toca. Nenhum dos arquivos que esta change edita está na lista. A lista
        completa está em `format:check.log`, e é idêntica à da baseline de
        `frontend-cadastro-base-sincronizada` (`diff` das duas listas
        ordenadas, 52 contra 52, `exit=0`).
      - `npm run test`: **verde** — **136 arquivos, 1783/1783**, 167,53s
        (`DUR=170s` com o `npm`). Load **3,35** na largada e **30,51** no fim
        (a própria suíte; nenhuma outra no `ps`). Nenhum `FAIL`, nenhum
        `Unhandled` (`grep -n "FAIL\|Unhandled\|failed"`, `exit=1`).
      - `npm run build`: **verde**, 18s, só o aviso de chunk acima de 500 kB.

## 2. Tipos e utilitários (`design.md`, D4, D7)

- [x] 2.1 (`apps/frontend`) `types/mcpServer.ts`: `McpServerAuthType` ganha
      `'CustomHeader'`; `McpServer` ganha `authHeaderName: string | null`,
      obrigatório; `CreateMcpServerInput` e `TestMcpServerConfigInput` ganham
      `authHeaderName?: string` (`UpdateMcpServerInput` herda).
- [x] 2.2 (`apps/frontend`) Rodar `npx tsc -b` e registrar aqui a lista de
      erros: ela é a enumeração dos fixtures de `McpServer` (convenção 18).
      Acrescentar `authHeaderName: null` em cada fixture apontado, uma linha
      por objeto, sem tocar em asserção. Registrar quantos arquivos e quantas
      linhas, contra a projeção do `design.md`.
      Feito. Saída completa em `~/.cache/buteco-agents/168-tsc-2.2.log`: **16
      linhas de erro** (`grep -c "error TS"`). Quatro são os dois `Record` de
      rótulos de produção (`McpServerConfigCard.tsx:9`, `McpServerTable.tsx:19`),
      cada um impresso duas vezes pelo `tsc -b`, esperados e resolvidos na
      2.3/5.1. As outras **12** são fixtures, em **11 arquivos** (contados com
      `cut | sort -u`): dois em `McpServerTable.test.tsx`, `:10` e `:21`; um em
      cada um de
      `AgentMcpServerRow.test.tsx:15`, `AgentToolsTab.test.tsx:48` — a fábrica
      `mcpServer(overrides)`, cujo objeto base não tinha o campo —,
      `AgentDetailPage.test.tsx:77`, `InventoryPage.test.tsx:75`,
      `useMcpServers.test.ts:18`, `McpServerConfigCard.test.tsx:8`,
      `McpServerCreatePage.test.tsx:29`, `McpServerDetailPage.test.tsx:44`,
      `McpServerEditPage.test.tsx:29`, `McpServerListPage.test.tsx:23`.
      Correção: **12 linhas** `authHeaderName: null`, logo depois do
      `authType` de cada objeto, nos **11 arquivos**. Nenhuma asserção tocada
      (`git diff -U1` lido: só as linhas acrescentadas). Depois, `tsc -b` só
      com os dois `Record` (`168-tsc-2.2-after.log`, 4 linhas: os dois erros,
      cada um repetido pelo modo de build). **Contra a projeção:** o
      `design.md` previa "até 12 arquivos, uma linha cada"; foram 11 arquivos e
      12 linhas. `McpServerForm.test.tsx` (8 `authType`) não entrou: os dele
      são inputs, não `McpServer`.
- [x] 2.3 (`apps/frontend`) `features/mcp-servers/utils/authTypeLabels.ts`:
      `Record<McpServerAuthType, string>` exaustivo, com "Nenhuma", "Bearer
      Token" e "Token em header customizado" (decisão 1 do dono). Comentário
      com o motivo da extração (três consumidores, D7).
- [x] 2.4 (`apps/frontend`) `features/mcp-servers/utils/connectionTestMode.ts`:
      a função pura da D4 (`'saved' | 'unsaved' | 'blocked'`), com o comentário
      que registra as duas mudanças de comportamento (troca de tipo e `None` em
      edição) e o porquê.
- [x] 2.5 (`apps/frontend`) `connectionTestMode.test.ts`: um caso por linha da
      tabela da D4, mais as negativas (`None` mantido não é `saved`; credencial
      só com espaços conta como em branco). É a metade determinística do G2.
      Feito: 8 casos, `npx vitest run .../connectionTestMode.test.ts` →
      **8 passed (8)**. Inclui edição sem id (`unsaved`), que a tabela não
      tinha linha própria e a função trata.

## 3. Componente compartilhado (`design.md`, D1, D2)

- [x] 3.1 (`apps/frontend`) `src/components/forms/RadioCardOption.tsx`: o corpo
      do `OriginOption` com a API da D1, os comentários atuais (estado marcado
      por esquema; alinhamento da R1-1) e a classe
      `buteco-radio-card-option`. `src/index.css`: renomear a classe e ajustar
      o comentário para não nomear um consumidor só.
- [x] 3.2 (`apps/frontend`) `RadioCardOption.test.tsx`: papel `radio`, nome
      acessível com título, `aria-checked` dentro de um `Radio.Group`, nota
      opcional, `disabled`.
      Feito: 6 casos (mais "clica e avisa o grupo" e "usa a classe do estado
      marcado"), `npx vitest run src/components/forms/RadioCardOption.test.tsx`
      → **6 passed (6)**.
- [x] 3.3 (`apps/frontend`) `KnowledgeBaseOriginCard.tsx`: importar
      `RadioCardOption` e apagar o `OriginOption` local. Rodar
      `KnowledgeBaseOriginCard.test.tsx` e `KnowledgeBaseCreatePage*.test.tsx`
      e confirmar `git diff --stat` **vazio** nos arquivos de teste das bases:
      nenhuma asserção editada. Registrar aqui a saída.
      Feito. `npx vitest run .../KnowledgeBaseOriginCard.test.tsx
      .../pages/KnowledgeBaseCreatePage` (casou `KnowledgeBaseCreatePage.test.tsx`
      e `KnowledgeBaseCreatePage.sync.test.tsx`) → **3 arquivos, 53 passed
      (53)**, load 3,99 na largada (saída em
      `~/.cache/buteco-agents/168-3.3.log`). `git diff --stat --
      src/features/knowledge-bases`:
      `components/KnowledgeBaseOriginCard.tsx | 54 ++----, 3 insertions(+), 51
      deletions(-)`, **um arquivo, nenhum de teste**; `git status --short` da
      pasta só com esse `M`. O diff de produção é o import, a remoção do
      `OriginOption` (interface, comentário e função) e a troca do nome nos
      dois usos.

## 4. Formulário (`design.md`, D3–D6, D8; spec `mcp-server-catalog-ui`)

- [x] 4.1 (`apps/frontend`) `McpServerForm.tsx`, estado e payload:
      `authHeaderName` nos valores, no `toFormValues` e no `onSubmit` (só com
      `CustomHeader`, sem `trim`); tipo salvo derivado de
      `initialValues.authType` em edição; `TestedConnectionConfig` e a
      comparação com `authHeaderName`.
      Feito; verificado pelos casos da 4.8 (payload, campo que volta
      preenchido, G1).
- [x] 4.2 (`apps/frontend`) `McpServerForm.tsx`, composição da D8: divisor,
      `SectionLabel` "Autenticação", `Radio.Group` com `RadioCardOption` em
      `SimpleGrid type="container"` (`cols={{ base: 1, '760px': 3 }}`, o `N`
      provisório da D3), descrições dos cartões, "Nome do header" em
      monoespaçado com placeholder `X-API-TOKEN`, rótulo "Token" com
      `CustomHeader`, avisos amarelos da D4, caixa de prévia da D6.
      Feito. O aviso de troca de tipo ficou entre a descrição e a caixa do
      campo, pelo `inputContainer` (`design.md`, D8, "Corrigido na
      implementação"). O `SimpleGrid` aceitou `cols` com chave em px no
      `tsc` do 9.4.2. Layout não se verifica em jsdom: fica para a 7.4.
- [x] 4.3 (`apps/frontend`) `McpServerForm.tsx`, os quatro comentários de
      afirmação estrutural da D6, cada um junto da frase, com arquivo, linha e
      GATILHO.
      Feito. Cada arquivo e linha citados foram relidos no worktree antes de
      escrever: `McpServer.cs:63-78` e `:74-76`,
      `UpdateMcpServerCommandHandler.cs:33-36`,
      `McpConnectionTester.cs:126-140`, `McpTransportFactory.cs:68-82` (apps/workers)
      e `McpServerEndpoints.cs:151-155`. Quatro comentários, um junto de cada
      frase: aviso de troca de tipo, aviso de troca para `None`, caixa de
      prévia (que cobre também as descrições dos cartões Bearer e header) e
      aviso do 400 do teste.
- [x] 4.4 (`apps/frontend`) `McpServerForm.tsx`, validação e teste: a tabela de
      obrigatoriedade da D5 no `validate`; `connectionTestMode` decidindo a
      rota, o `disabled` do botão e a nota; checagem local antes do teste não
      salvo (URL; com `CustomHeader`, nome e token); 400 do teste aplicado aos
      campos com o aviso inline "Corrija os campos destacados antes de testar.
      Nenhuma conexão foi tentada.", e chave sem campo dentro do aviso; o aviso
      também é descartado quando a configuração muda.
      Feito; verificado pelos casos da 4.8. Mensagens locais do teste: "Informe
      a Url antes de testar." (a de hoje), "Informe o nome do header antes de
      testar." e "Informe o token antes de testar.".
- [x] 4.5 (`apps/frontend`) `McpServerForm.tsx`, precedência de erros (D5,
      decisão (a)): conjunto de campos editados desde o último `errors`
      recebido, zerado quando chega um `errors` novo; campo editado, ou com 400
      do teste aplicado, mostra só `form.errors`. Comentário com o motivo e a
      alternativa (b) recusada. A página não muda.
      Feito: `pageErrorsDismissed`, zerado quando chega um `errors` novo
      (comparação de identidade no render), alimentado pelo `onValuesChange`
      do `useForm` e pelo 400 do teste. Verificado pelos dois casos de
      precedência da 4.8 e pelo guarda da 4.10.
- [x] 4.6 (`apps/frontend`) `McpServerEditPage.tsx`: `authHeaderName:
      data.authHeaderName ?? ''` nos `initialValues`.
      Feito; verificado pelo pré-preenchimento da 4.9.
- [x] 4.7 (`apps/frontend`) `McpServerForm.test.tsx`, migrar os testes que
      usam o `combobox` do tipo para os radios e trocar a asserção do teste
      "erro de validação (400) no teste de conexão é normalizado para o mesmo
      alerta de falha" (`:213`) pela da D5, por decisão registrada. Listar aqui
      cada asserção editada e o motivo.
      Feito. Antes de editar, a suíte antiga rodou contra o formulário novo
      (`~/.cache/buteco-agents/168-4.7-antes.log`): 62 casos em 5 arquivos
      (`McpServerForm.test.tsx` e as páginas), **5 reprovados**, três por
      `combobox`, o de `:213` e o de `McpServerEditPage.test.tsx:140` (este
      levou à revisão da D5, ver 4.9). Neste arquivo:
      - **Arranjo, sem asserção:** os helpers `getSelect`/`selectOption`
        viraram `authTypeRadio`/`selectAuthType` (clique no cartão), e as duas
        chamadas trocaram de helper, em "chama onSubmit incluindo a credencial
        quando AuthType é BearerToken" e "campo Credencial aparece e é
        obrigatório no cadastro quando AuthType é BearerToken".
      - **Asserção editada, por decisão (D5, aprovada em 18/10/2026):** o
        teste de `:213`, renomeado para "erro de validação (400) no teste de
        conexão vai para o campo, sem o alerta de falha de conexão".
        `findByText('Falha na conexão')` virou
        `findByTestId('connection-test-invalid')` mais
        `queryByText('Falha na conexão')` ausente; a asserção da mensagem da
        URL ficou como estava. Motivo: o 400 não é resultado de conexão.
      Nenhuma outra asserção do arquivo mudou.
- [x] 4.8 (`apps/frontend`) `McpServerForm.test.tsx`, casos novos, um `it()`
      por estado (convenção 18), lidos dos cenários da delta:
      - seletor em cartões, nenhum `combobox` de tipo;
      - cada tipo mostra e esconde os campos certos (`None`: nenhum;
        `BearerToken`: "Credencial"; `CustomHeader`: "Nome do header" e
        "Token");
      - payload com `authHeaderName` só com `CustomHeader`; nome digitado,
        troca para outro tipo: **sem** `authHeaderName`; volta ao tipo: o nome
        continua preenchido;
      - obrigatoriedade local do nome do header (vazio e só espaços) e da
        credencial nos três casos da D5, sem `onSubmit`;
      - nome não vazio com formato ruim **é** enviado (sem validação local de
        formato);
      - erros da página (`errors`) em `authHeaderName` e `credential`
        aparecem nos campos;
      - prévia: nome atualizado a cada tecla, `<nome do header>` sem nome,
        valor do token nunca no DOM da caixa; **sem** prévia em `None` e em
        `BearerToken` (dois `it()`);
      - avisos da D4: troca entre tipos com credencial (aviso e asterisco);
        troca para `None` (aviso de apagar); saída de `None` (**sem** aviso,
        com asterisco);
      - teste: `CustomHeader` envia `authHeaderName`; barrado sem nome e sem
        token no não salvo; 400 do teste nos campos `authHeaderName` e
        `credential`, com o aviso inline e **sem** "Falha na conexão"; chave
        sem campo aparece no aviso; nota de configuração salva com
        `CustomHeader` cita o nome do header; `None` em edição testa o digitado
        e **sem** nota;
      - precedência de erros (D5): com `errors` da página trazendo 400 em
        `authHeaderName`, editar o campo esconde a mensagem do submit
        (negativa, `it()` próprio); editar o campo e testar com outro 400 no
        mesmo campo mostra a mensagem nova do teste;
      - **[guarda G1]** resultado descartado ao editar o nome do header;
      - **[guarda G2]** botão desabilitado e nota na troca de tipo com
        credencial em branco, sem chamada a `testSavedMcpServerConnection`.
      Feito: **33 casos novos** num `describe` próprio (o arquivo tinha 17,
      contados com `git show HEAD:` e `grep -c`). Arquivo com 50 casos,
      `npx vitest run .../McpServerForm.test.tsx` → **50 passed (50)**
      (`~/.cache/buteco-agents/168-4.8.log`). Na edição, o servidor salvo é
      montado só pelos `initialValues` (com `authHeaderName`), como a página
      faz.
- [x] 4.9 (`apps/frontend`) `McpServerEditPage.test.tsx`: pré-preenchimento de
      um `CustomHeader` (nome no campo, token vazio); `PUT` com nome novo e sem
      credencial ao editar só o nome; os testes que usam o `combobox` migram
      para os radios (asserções editadas listadas aqui). O teste de `:140`
      ("em erro 400 por credencial nunca persistida…") é reescrito com o
      comportamento novo: `None` → Bearer Token com credencial em branco mostra
      a mensagem local no campo, não chama `updateMcpServer` e não navega
      (`design.md`, D5, "Corrigido na implementação"). Dois casos novos pelo
      caminho real da página (`PUT` rejeitado → `fieldErrorsFrom` → campo):
      servidor `CustomHeader` com token começando por espaço (400 em
      `credential`, mensagem no campo "Token", sem navegar); troca de Bearer
      Token para "Token em header customizado" com `Content-Type` no nome e
      token preenchido (400 em `authHeaderName`, mensagem no campo "Nome do
      header", sem navegar).
      Feito. `npx vitest run .../McpServerEditPage.test.tsx` → **12 passed
      (12)**, 8 que já existiam e 4 novos (`~/.cache/buteco-agents/168-4.9.log`).
      - **Arranjo, sem asserção:** "trocar o tipo de autenticação para None
        oculta o campo de credencial e não o envia" trocou o clique no
        `combobox` e na opção por um clique no cartão "Nenhuma".
      - **Reescrito, por decisão (D5, "Corrigido na implementação", dono em
        18/10/2026):** o teste de `:140` virou "trocar None por Bearer Token
        com a credencial em branco é barrado no cliente, sem PUT e sem
        navegar". A rejeição simulada do `updateMcpServer` saiu; a asserção da
        mensagem da API ("A credencial é obrigatória para o tipo de
        autenticação informado.") virou a local ("Informe a credencial nova
        para o tipo Bearer Token."), mais `updateMcpServer` não chamado; o
        "não navega" ficou.
      - **Novos:** pré-preenchimento do `CustomHeader`; edição só do nome do
        header (PUT com `authHeaderName: 'X-API-KEY'` e `credential:
        undefined`, sem aviso de troca); 400 em `credential` (token `'
        segredo-123'`, mensagem no campo "Token"); 400 em `authHeaderName`
        (Bearer → header customizado com `Content-Type`, mensagem no campo
        "Nome do header"). Os dois 400 conferem que o PUT saiu e que a
        mensagem pertence ao campo pelo `aria-describedby` e que o campo se
        declara inválido em `aria-invalid`. A primeira versão do caso do token
        reprovou no `aria-invalid`, porque o `PasswordInput` do 9.4.2 só põe
        `data-invalid` no `<input>` (`PasswordInput.mjs:159` e `:165`); o
        formulário passou a enviar `aria-invalid` explícito (`design.md`, D8,
        segunda correção, decisão do dono em 18/10/2026). Verificado que a
        asserção reprova sem a correção: `McpServerEditPage.test.tsx`, 12
        executados, 1 reprovado (o do token, *aria-invalid … Received: null*);
        restaurado e conferido com `cmp`, 12/12. Depois, a feature inteira:
        `npx vitest run src/features/mcp-servers` → **13 arquivos, 172 passed
        (172)** (`~/.cache/buteco-agents/168-aria-invalid.log`). O helper exige
        `id` não vazio, senão o `toContain('')` passaria sempre.
- [x] 4.10 (`apps/frontend`) Guardas verificados (convenção 15), um de cada vez,
      registrando aqui o nome do teste e a mensagem de reprovação:
      - G1: tirar `authHeaderName` de `TestedConnectionConfig` e da comparação;
        o teste do G1 reprova; restaurar.
      - G2: fazer `connectionTestMode` devolver `'saved'` sempre que a
        credencial está em branco em edição (a regra de hoje,
        `McpServerForm.tsx:101`); reprovam o caso de `connectionTestMode.test.ts`
        **e** o do formulário; restaurar. Se só um reprovar, o guarda está no
        lugar errado (segunda forma da convenção 15) e é corrigido antes de
        seguir.
      - Precedência: voltar ao `errors?.<campo> ?? form.errors.<campo>` de
        hoje; os dois testes de precedência da 4.8 reprovam; restaurar.
      Feito em 18/10/2026, um de cada vez, a partir de cópias dos arquivos
      corretos (`~/.cache/buteco-agents/168-guardas/*.ok`), restaurados com
      `cp` e conferidos com `cmp` (idênticos) antes do guarda seguinte. Saídas
      em `~/.cache/buteco-agents/168-guardas/`.
      - **G1.** Defeito: a linha
        `testState.config.authHeaderName === currentConfig.authHeaderName &&`
        removida da comparação. `McpServerForm.test.tsx`, **50 executados, 1
        reprovado**: "[guarda G1] editar o nome do header descarta o resultado
        do teste" — `expect(element).not.toBeInTheDocument()`, *expected
        document not to contain element, found <span …>Conexão
        bem-sucedida</span>*. Restaurado: 50/50.
      - **G2.** Defeito: `connectionTestMode` com a regra de antes (`isEditing
        && hasServerId && !credential.trim() ? 'saved' : 'unsaved'`).
        `connectionTestMode.test.ts` + `McpServerForm.test.tsx`, **58
        executados, 7 reprovados**, entre eles **os dois do guarda**:
        "edição com o tipo trocado … fica bloqueada" — *expected 'saved' to be
        'blocked'* — e "[guarda G2] trocar o tipo com a credencial em branco
        desabilita o teste, com nota" — `expect(element).toBeDisabled()`,
        *Received element is not disabled*. Os outros cinco são os casos de
        `None` em edição e de credencial só com espaços, que a regra antiga
        também contraria. Restaurado: 58/58.
      - **Precedência.** Defeito: o erro da página sempre exibido, como o
        `errors?.<campo> ?? form.errors.<campo>` de antes. `McpServerForm.test.tsx`,
        **50 executados, 2 reprovados**, os dois de precedência: "editar o
        campo esconde o erro do envio anterior" — `expect(element).not
        .toBeInTheDocument()` — e "depois de editar o campo, o 400 do teste
        substitui o erro do envio anterior" — *Unable to find an element with
        the text: Mensagem do teste.* Restaurado: 50/50.

## 5. Tabela e detalhe (`design.md`, D7; spec `mcp-server-catalog-ui`)

- [x] 5.1 (`apps/frontend`) `McpServerTable.tsx` e `McpServerConfigCard.tsx`
      importam `authTypeLabels`; o card ganha a linha "Header" (monoespaçado,
      só com `CustomHeader`, `—` se o nome vier nulo).
      Feito; `tsc -b` limpo depois (os dois erros de `Record` da 2.2
      resolvidos).
- [x] 5.2 (`apps/frontend`) `McpServerTable.test.tsx`: rótulo "Token em header
      customizado".
      Feito: 1 caso novo; arquivo com **8 passed (8)**.
- [x] 5.3 (`apps/frontend`) `McpServerConfigCard.test.tsx`: rótulo e linha
      "Header" com `X-API-TOKEN` em `CustomHeader`; **sem** linha "Header" em
      `None` e em `BearerToken` (dois `it()`); `—` com nome nulo.
      Feito: 4 casos novos (o do rótulo e da linha em monoespaçado, o do `—`
      e as duas negativas); arquivo com **7 passed (7)**. As duas execuções
      juntas: **2 arquivos, 15 passed (15)** (`~/.cache/buteco-agents/168-5.log`).
- [x] 5.4 (`apps/frontend`) Reconferir a lista da V3 do `design.md` contra a
      árvore: `grep -rn "authType\|AuthType\|BearerToken\|CustomHeader"
      apps/frontend/src` (com `echo $?`, convenção 21), lida pelo `-n`. Todo
      consumidor que exiba o tipo usa `authTypeLabels`; nenhum outro `Record` de
      rótulo sobrou.
      Feito. A primeira tentativa, com `--exclude=*.test.ts` sem aspas,
      falhou no `zsh` com *no matches found* e não rodou; repetida com aspas,
      `exit=0`, saída em `~/.cache/buteco-agents/168-5.4.txt`. Produção: 7
      arquivos, todos de `features/mcp-servers` (`McpServerForm.tsx` 51 linhas,
      `utils/connectionTestMode.ts` 8, `McpServerConfigCard.tsx` 6,
      `types/mcpServer.ts` 6, `utils/authTypeLabels.ts` 6, `McpServerTable.tsx`
      2, `McpServerEditPage.tsx` 1), lidos pelo `-n`: tabela (`:83`), card
      (`:33`) e formulário exibem o tipo por `authTypeLabels`. `Record` por
      tipo: só `authTypeLabels` e `authTypeDescriptions` (descrições dos
      cartões, não rótulo). As strings dos rótulos ("Nenhuma", "Bearer
      Token", "Token em header customizado") só aparecem em
      `authTypeLabels.ts` (mais uma menção em comentário do formulário).
      Nenhum consumidor fora de `mcp-servers` lê o tipo, como a V3 dizia.

## 6. Fechamento técnico

- [x] 6.1 (`apps/frontend`) Com a máquina conferida como na 1.1 e **nenhum
      navegador de conferência aberto** (encerrar por porta), rodar `npm run
      lint`, `npm run format:check`, `npm run test`, `npm run build`, com a
      saída completa em `~/.cache/buteco-agents/168-fechamento/`. Registrar a
      contagem medida contra a da 1.1, a diferença esperada (casos novos
      menos casos removidos, listados) e qualquer reprovação com o nome do
      teste. Reprovação isolada: rodar o arquivo isolado repetido antes de
      chamar de ambiental (convenção 19).
      Feito em 18/10/2026, sobre a árvore com os grupos 1 a 5 (sem commit).
      Antes: load 4,10; `ps` sem suíte nem navegador de conferência; 9222
      livre (`lsof`, `exit=1`); `podman ps` com Postgres e RabbitMQ do
      compose. Saídas completas em `~/.cache/buteco-agents/168-fechamento/`.
      - `npm run lint`: **verde**, 12s.
      - `npm run format:check`: **reprova em 52 arquivos**, e a lista é
        **idêntica** à da 1.1 (`diff` das listas ordenadas, `exit=0`): nenhum
        arquivo desta change reprova.
      - `npm run build`: **verde**, 14s, só o aviso de chunk.
      - `npm run test`: **verde — 138 arquivos, 1839/1839**, 125,99s. Load
        5,63 na largada e 30,44 no fim (a própria suíte). Nenhum `FAIL` nem
        `Unhandled` (`grep`, `exit=1`).
      **Contra a 1.1 (136 arquivos, 1783/1783): +2 arquivos e +56 casos**, e a
      conta fecha caso a caso (`grep -cE "^\s*it\("` antes, por `git show
      HEAD:`, e depois):

      | arquivo | antes | depois | diferença |
      |---|---|---|---|
      | `components/forms/RadioCardOption.test.tsx` | — | 6 | +6 (novo) |
      | `mcp-servers/utils/connectionTestMode.test.ts` | — | 8 | +8 (novo) |
      | `mcp-servers/components/McpServerForm.test.tsx` | 17 | 50 | +33 |
      | `mcp-servers/pages/McpServerEditPage.test.tsx` | 8 | 12 | +4 |
      | `mcp-servers/components/McpServerConfigCard.test.tsx` | 3 | 7 | +4 |
      | `mcp-servers/components/McpServerTable.test.tsx` | 7 | 8 | +1 |
      | **total** | | | **+56** |

      Nenhum caso removido: o de `McpServerForm.test.tsx:213` e o de
      `McpServerEditPage.test.tsx:140` foram editados ou reescritos no lugar
      (4.7 e 4.9). Os 11 arquivos de fixture da 2.2 não mudaram de contagem.
      **Segunda rodada, 18/10/2026, depois da correção do `aria-invalid` (D8),
      da conferência e da troca do comentário do `N` no `McpServerForm`.**
      Antes: load 2,49; `ps` sem suíte, sem Vite e sem navegador de
      conferência; 9222, 4173 e 5173 livres (`lsof`, `exit=1`); `podman ps`
      com Postgres e RabbitMQ. Saídas em
      `~/.cache/buteco-agents/168-fechamento-2/`.
      - `npm run lint`: **verde**, 11s.
      - `npm run format:check`: **52 arquivos**, lista **idêntica** à da 1.1
        (`diff`, `exit=0`).
      - `npm run build`: **verde**, 14s.
      - `npm run test`: **verde — 138 arquivos, 1839/1839**, 159,85s; load
        3,57 na largada. Nenhum `FAIL` nem `Unhandled` (`grep`, `exit=1`).
      **Contra a 1.1: os mesmos +2 arquivos e +56 casos** da primeira rodada,
      com a mesma contagem por arquivo (17→50, 8→12, 7→8, 3→7, +6, +8). A
      correção do `aria-invalid` **não acrescentou caso**: acrescentou uma
      asserção ao helper `expectErrorOnField` de `McpServerEditPage.test.tsx`,
      usado pelos dois casos de 400 da página que já existiam, e essa
      asserção foi verificada reprovando sem a correção (4.9).

## 7. Conferência visual (convenção 14; `design.md`, D9)

Feita pelo dono, com o painel servido deste worktree. O archive espera por ela.
Nenhuma suíte roda, neste ou em outro worktree, durante a conferência.

- [x] 7.1 (`apps/frontend`) Preparar o painel: conferir a 4173 livre
      (`lsof -iTCP:4173 -sTCP:LISTEN`), subir `npm run dev -- --port 4173
      --strictPort` deste worktree, e registrar aqui a porta e o PID. Nunca a
      5173 (Vite do checkout principal).
      Feito em 18/10/2026: 4173 livre antes (`lsof`, `exit=1`). Subido com
      `VITE_API_BASE_URL=http://localhost:5017` e
      `VITE_INBOX_BASE_URL=http://localhost:5027` na linha de comando — os
      mesmos valores do `.env` do checkout principal; nenhum `.env` criado no
      worktree. Sem `VITE_CONNECTORS_BASE_URL`, como no checkout principal: a
      origem "Sincronizada" do cadastro de base aparece indisponível (D1 de
      `frontend-cadastro-base-sincronizada`), o que não afeta a 7.6. **Porta
      4173, PID 15008**, `cwd` no worktree (`lsof -p`), HTTP 200 em `/`. Log em
      `~/.cache/buteco-agents/168-vite-4173.log`.
      Reiniciado a pedido do dono às 17:03, porque as mudanças não apareciam
      no teste manual: parado por porta (`lsof -ti tcp:4173 | xargs kill`) e
      subido de novo com `--force` (dependências reotimizadas). **PID 16214**,
      `cwd` no worktree; conferido que o módulo servido de `McpServerForm.tsx`
      e de `utils/authTypeLabels.ts` traz o código novo ("Token em header
      customizado", `RadioCardOption`, `credential-preview`).
- [x] 7.2 (nenhum app, só leitura) Conferir a API: `GET
      http://localhost:5017/mcp-servers` devolve a chave `authHeaderName` (o
      binário tem a #160). Se não devolver, parar e reportar ao dono; esta
      change não sobe nem derruba o `apps/api` de outro checkout.
      Feito: login com a credencial de desenvolvimento de
      `docs/development.md` e `GET /mcp-servers` com o token → 3 itens, **todos
      com a chave `authHeaderName`** (nula nos dois `None`, `'X-Api-Key'` no
      `CustomHeader` "MCP header local" que já existia no banco local). O
      binário da 5017 tem a #160.
- [x] 7.3 (nenhum app) Roteiro de dados, pela tela deste painel: cadastrar um
      servidor `None`, um `BearerToken` e um `CustomHeader` (`X-API-TOKEN`),
      com URLs de teste. Registrar aqui os ids.
      **Pendente do dono** (decisão de 18/10/2026): o dono cadastra os três
      pela tela no começo da conferência e passa os ids para registrar aqui.
      Motivo: a digitação do Chrome da automação não chegou aos campos
      controlados. O login pela tela funcionou, mas o cadastro não: em
      três tentativas no Chrome da automação (clique e digitação por
      coordenada; clique e digitação por referência; `form_input` com clique
      no botão), a digitação não chegou aos campos (o `value` ficou `''` no
      DOM) e o clique em "Cadastrar servidor" não enviou nada. Conferido pela
      API depois de cada tentativa: nenhum servidor novo. Parado aqui, como
      manda o roteiro de automação, para decisão do dono.
      **Ids não identificados (18/10/2026, depois da conferência).** `GET
      /mcp-servers` (só leitura) devolveu **3 itens**, e nenhum trio criado na
      conferência: `84999c69-…` "Informações Gerais" (`None`, criado em
      03/08/2026), `c1b5876e-…` "Reservas" (`None`, 03/08/2026) e
      `e1a0c362-ad4f-4098-a78a-ee4b139e5e9a` "MCP header local"
      (`CustomHeader`, `X-Api-Key`, criado em 18/10/2026 às 17:50 UTC, antes
      de o painel da conferência subir às ~18:45 UTC). Lista levada ao dono;
      a tarefa segue aberta até ele dizer quais ids registrar.
      **Fechada depois da rodada 2 (18/10/2026).** O dono fez a rodada sobre os
      cadastros que já existiam, **sem cadastrar servidor novo**. `GET
      /mcp-servers` (só leitura) depois da rodada:

      | id | tipo | `authHeaderName` | nome | `updatedAt` |
      |---|---|---|---|---|
      | `84999c69-df1a-49a6-9667-535f8aa714e2` | `CustomHeader` | `X-CUSTOM-TOKEN` | Informações Gerais | **18/10, 20:32 UTC** |
      | `c1b5876e-bd7f-4d0a-8940-271ce5ba6212` | `None` | — | Reservas | 03/08/2026 |
      | `e1a0c362-ad4f-4098-a78a-ee4b139e5e9a` | `CustomHeader` | `X-Api-Key` | MCP header local | 18/10, 17:50 UTC (o da criação) |

      **O que a API confirma:** o único registro alterado na rodada é
      `84999c69-…`, que era `None` antes dela (listagem acima) e terminou
      `CustomHeader` com `authHeaderName` preenchido. **O que só o dono
      atesta:** os passos intermediários desse registro — salvo como Bearer
      Token, trocado e salvo como "Token em header customizado", e salvo de
      novo editando só o nome do header, sem token —, porque a API guarda só
      o estado final e o `updatedAt` da última gravação. "MCP header local" e
      "Reservas" não foram gravados na rodada.
- [x] 7.4 (`apps/frontend`) **Quadro `Main`, criar e editar**, claro e escuro:
      os três cartões lado a lado; os campos por tipo; prévia atualizando;
      edição de cada servidor do 7.3 com `tipoSalvo` correspondente, cada
      linha da tabela de avisos da D4, as notas de teste e o botão
      desabilitado. **Empilhamento:** reduzir a largura até o corte, medir a
      largura do container no corte e a do título "Token em header
      customizado" contra a do cartão logo acima e logo abaixo dele, com a
      navbar aberta e colapsada; ajustar o `N` da D3 até nenhum título quebrar
      nem estourar e registrar o valor e as medidas aqui e na D3.
      **Conferência do dono:** feita em 18/10/2026, aprovada na rodada 1, sem
      achados, nos dois esquemas.
      **Medição do `N` (18/10/2026, navegador da automação, sem digitar em
      nenhum campo).** A sessão foi criada por `fetch` ao `/auth/login` (a
      credencial de desenvolvimento de `docs/development.md`), com o token
      gravado no `sessionStorage`. **O redimensionamento da janela não teve
      efeito** (pedidos 1440 e 1100, o `innerWidth` ficou em 1864), então a
      viewport foi controlada por um `iframe` da mesma origem com largura
      fixa: dentro dele a viewport é a largura do `iframe`, e as media queries
      (navbar em `sm`) e as container queries (`SimpleGrid`) reagem de verdade.
      Leituras por `getBoundingClientRect`; largura útil = área de conteúdo do
      cartão (`clientWidth` menos o `padding` de 14px dos dois lados) menos o
      indicador (20px) e o vão (10px).

      | viewport | navbar | container | colunas | cartão | útil | título | folga |
      |---|---|---|---|---|---|---|---|
      | 1440 | aberta, 224 | 1142 | 3 | 372,66 | 313 | 169,02 | 143,98 |
      | 1059 | aberta, 224 | 761 | 3 | 245,67 | 186 | 169,02 | 16,98 |
      | **1058** | aberta, 224 | **760** | **3** | 245,33 | **185** | 169,02 | **15,98** |
      | **1057** | aberta, 224 | **759** | **1** | 759 | 699 | 169,02 | 529,98 |
      | 1073, "Token em header customizado" **marcado** (borda de 2px, barra de rolagem de 15px) | aberta, 224 | **760** | **3** | 245,33 | **183** | 169,02 | **13,98** |
      | 1072, idem | aberta, 224 | 759 | 1 | 759 | 697 | 169,02 | 527,98 |

      O título mediu 169,02px (IBM Plex Sans 600, 12px) e ficou em **uma
      linha** em todas as larguras. No último ponto com três colunas, no pior
      caso (cartão marcado), sobram **13,98px**. **Corte medido: três colunas
      com container ≥ 760px, uma coluna em 759px — o `N` provisório,
      confirmado.** Com o cartão marcado o formulário cresce e a barra de
      rolagem desloca o corte de viewport de 1058 para 1073, mas o de
      container não muda, que é o motivo da container query (D3).
      **Navbar "colapsada": o painel não tem esse estado.** Em viewport ≤
      768px a navbar passa a ocupar a largura inteira, sem `transform` nem
      `Burger`, e cobre o formulário (medido: navbar de 767px em viewport de
      767; o `main` cai para `padding-left: 16px`); container de 693px em 767,
      uma coluna, título em uma linha. É comportamento anterior a esta change
      (`components/layout/AppShell.tsx:56` não muda aqui) e contradiz o "o
      conteúdo aperta" do comentário de `AppShell.tsx:51-55`: **#171**.
- [x] 7.5 (`apps/frontend`) **Quadro `Erros`**, claro e escuro: edição de um
      `BearerToken` trocando para "Token em header customizado", com
      `Content-Type` no nome e token em branco: salvar (obrigatoriedade local
      no token), depois preencher o token e salvar (400 da API no nome do
      header); testar (400 nos campos e aviso inline).
      Feito pelo dono, rodada 1, sem achados, nos dois esquemas.
- [x] 7.6 (`apps/frontend`) **Quadro `Detalhe`**, claro e escuro: card de
      configuração do `CustomHeader` (rótulo e "Header" em monoespaçado) e dos
      outros dois (sem "Header"); tabela da listagem com os três rótulos. E o
      **card de origem** do cadastro de base, nos dois esquemas, para conferir
      que a extração (D1) não mudou nada.
      Feito pelo dono, rodada 1, sem achados, nos dois esquemas.
- [x] 7.7 (`apps/frontend`) Tema escuro conferido por contraste: avisos
      amarelos, caixa de prévia, cartão marcado e nota de teste legíveis contra
      a superfície do formulário.
      Feito pelo dono, rodada 1, sem achados.
- [x] 7.8 Validação manual pelo dono, quadro a quadro, iterativa: cada achado
      registrado aqui com o que foi medido e a correção; nova rodada depois de
      cada correção, até uma rodada sem achado. Ao fim, encerrar o Vite da 4173
      por porta (`lsof -ti :4173 | xargs kill`).
      **Rodada 1, sem achados, aprovada pelo dono em 18/10/2026**, nos dois
      esquemas de cor; medidas do `N` na 7.4 (corte em container de 760px,
      título com 13,98px de folga no pior caso). O Vite da 4173 (PID 16771,
      subido para a medição) foi encerrado por porta; 4173 e 5173 livres
      (`lsof`, `exit=1`). O Vite da 5173 (checkout principal) já tinha sido
      parado a pedido do dono.
      **Rodada 2: fluxos de salvar (cadastro dos três tipos, troca de tipo e
      edição só do nome do header), aprovada pelo dono em 18/10/2026.** Feita
      sobre os cadastros existentes, sem servidor novo (7.3). Registro gravado
      na rodada: `84999c69-df1a-49a6-9667-535f8aa714e2` ("Informações Gerais"),
      que terminou `CustomHeader` com `authHeaderName: X-CUSTOM-TOKEN`
      (`updatedAt` 18/10, 20:32 UTC); os outros dois
      (`c1b5876e-bd7f-4d0a-8940-271ce5ba6212`,
      `e1a0c362-ad4f-4098-a78a-ee4b139e5e9a`) sem gravação na rodada. Os passos
      intermediários são atestados pelo dono; a API mostra só o estado final.
      Painel da rodada: Vite da 4173, PID 17788, encerrado por porta; 4173
      livre (`lsof`, `exit=1`).

## 8. Registro

- [x] 8.1 (raiz, nenhum app) Se a implementação divergir do `design.md`,
      corrigir o `design.md` com a causa real (convenção 9), inclusive o `N` da
      D3 depois da conferência.
      Feito em 18/10/2026. Registrados no `design.md`: D3 com o `N` medido
      (760px, confirmado) e o método; D5 com a cláusula do 400 de credencial
      nunca persistida; D8 com as duas correções (posição do aviso;
      `aria-invalid` explícito); o entregue contra a projeção na árvore de
      pastas; o risco do `N` atualizado; e a seção "Achados fora do escopo" com a
      #170 e o achado da navbar. O comentário do `cardsColumns` no
      `McpServerForm` deixou de dizer "provisório" e carrega a medida e o
      gatilho.
- [x] 8.2 (raiz, nenhum app) `02-HISTORICO_E_STATUS.md`: entrada da change e
      as divergências P1–P13 na lista viva "Correções de protótipo", como seção
      acrescentada. Escrito **antes** do archive.
      Feito: seção nova no fim do `02` (o que entrou, o que a implementação
      mediu, guardas, divergências, achados, com a #170) e o bloco "Da
      quadragésima nona à sexagésima primeira, da #168" na lista viva, depois do
      bloco da #101. **Uma linha reescrita:** a contagem da lista ("São
      **quarenta e oito**, achadas em doze etapas…" virou "São **sessenta e
      uma**, achadas em treze etapas…"), porque sem ela a contagem ficaria
      falsa.
- [x] 8.3 (raiz, nenhum app) `CHANGELOG.md`, `[Unreleased]`: entrada da #168.
      Escrito **antes** do archive.
      Feito, em "Integração MCP": entrada do painel em `Added` (com a #170);
      duas em `Changed` (400 do teste nos campos; `None` em edição testa o
      digitado); uma em `Fixed` (teste do tipo antigo na troca de tipo). **Uma
      frase da #160 reescrita:** "o painel ainda não oferece o tipo, e chega com
      a #168" ficaria falsa com esta change (convenção 13).
- [x] 8.4 (raiz, nenhum app) `python3 scripts/check-docs.py` e
      `openspec validate frontend-mcp-autenticacao-header-customizado --strict`.
      Feito: `Integridade da documentação: OK` (`exit=0`) e `Change
      'frontend-mcp-autenticacao-header-customizado' is valid`.

- [ ] 8.5 (raiz, nenhum app) Abrir o PR (convenção 24). Só depois de: a 7.8
      fechada pelo dono; as 8.1–8.4 feitas; o archive da change, com sync das
      specs; e o push da branch. E só com autorização explícita do dono na
      hora: nenhum push nem PR com a change ativa. Corpo do PR com a issue em
      linha própria:

      ```
      Closes #168
      ```

      Achado que tiver virado issue e ficado na fila entra como `Refs #N`, nunca
      como `Closes`. Até aqui, um: a **#170** (`aria-invalid` nos campos de
      senha; `design.md`, "Achados fora do escopo"), que entra em linha própria:

      ```
      Refs #170
      ```

      E a **#171** (navbar abaixo de `sm` cobre o conteúdo; achado na medição do
      `N`, 7.4), também na fila, abaixo da #170:

      ```
      Refs #171
      ```

      Aberto o PR, mover a #168 para `In review` e conferir com
      `gh issue view 168 --json projectItems`. Registrar aqui o link do PR.
