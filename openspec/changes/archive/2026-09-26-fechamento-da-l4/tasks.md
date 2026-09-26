## 1. Conferência de escopo — a lista fechada, montada por leitura

A varredura já rodou na proposta e produziu a árvore do `design.md`. As tarefas
abaixo **conferem** a lista contra o `HEAD` no momento da implementação e a
fecham; nenhuma delas descobre escopo novo sem virar issue (convenção 23).

- [x] 1.1 Refazer a varredura e confirmar a lista fechada. Comando:
      `grep -rn "#67\|L4" --include="*.ts" --include="*.tsx" --include="*.cs" --include="*.md" .`
      excluindo `openspec/changes/archive/`, `node_modules/`,
      `openspec/changes/fechamento-da-l4/` e `02-HISTORICO_E_STATUS.md`.
      **Esperado: 7 sítios vivos em 4 arquivos.** Se aparecer sítio fora desta
      lista, **parar** e decidir se entra aqui ou vira issue — não absorver em
      silêncio.

      **RESULTADO: 9 ocorrências, 8 sítios vivos em 4 arquivos.** A varredura
      parou a change, como a tarefa manda, e as duas diferenças foram decididas:

      - `CHANGELOG.md:482` — **fora**, pela tarefa 1.2: registro histórico não se
        reescreve. Era esperado e está coberto.
      - `DeclaredGap.tsx:21` — **entra**, como sítio **8**. Ver abaixo.

      **E a causa da lista curta vale mais que o sítio**, porque ela é de método:
      a varredura da proposta **viu** as duas linhas do `DeclaredGap` e carregou
      só uma para a tabela. **O erro não foi de busca, foi de transcrição** — e
      uma lista fechada com transcrição incompleta é pior que nenhuma lista, por
      afirmar cobertura que não tem. A régua: **a lista fechada se monta colando
      a saída da varredura, nunca redigitando-a.**

      | # | sítio | o que afirma | o que vira |
      |---|---|---|---|
      | 1 | `AgentConsumptionCard.tsx:18` | *"L4 — … NÃO ENTRAM (#67)"* e *"a lacuna é declarada no rodapé"* | ausência decidida; o rodapé **não existe** desde a #52 |
      | 2 | `AgentConsumptionCard.tsx:79` | *"é justamente a coluna que a rota não serve (L4, #67)"* | permanece verdadeira; só o gatilho da linha 81 muda |
      | 3 | `AgentConsumptionCard.tsx:81` | *"o destaque melhora quando a #67 fechar"* | gatilho novo: primeiro pedido do dono por **taxa** de falha |
      | 4 | `AgentConsumptionCard.test.tsx:192` | mesma promessa, no caso do destaque | idem, e a **asserção não muda** |
      | 5 | `AgentConsumptionCard.test.tsx:205` | cabeçalho de seção `L4 (#67)` | passa a nomear a decisão, não a lacuna |
      | 6 | `types/systemInsights.ts:73` | *"é a **lacuna** L4 (#67)"* | ausência decidida, com a superfície de destino nomeada |
      | 7 | `DeclaredGap.tsx:46` | a variante `block` *"entra no RODAPÉ … para a L3 e a **L4**"* | a L4 sai da frase; a variante vira issue (D8) |
      | 8 | `DeclaredGap.tsx:21` | *"Quem precisa da causa vai à #51, à #66 ou à **#67**"* | a #67 sai da lista: depois desta change a L4 **não tem lacuna declarada em tela nenhuma**, então o ponteiro mandaria procurar a causa de um quadro que não existe |

- [x] 1.2 Confirmar que `CHANGELOG.md:482` e o `02` **não** entram na lista —
      são registro histórico e não se reescrevem. O `02` recebe entrada **nova**
      (tarefa 5.1), nunca correção das antigas.
- [x] 1.3 Confirmar que nada em `apps/api`, `apps/workers`, `apps/inbox` ou
      `nginx.conf` aparece na varredura. **Se aparecer, é achado, não escopo.**

## 2. Comportamento — a única mudança desta change

- [x] 2.1 Escrever o guarda **antes** da correção e vê-lo reprovar contra o
      `HEAD` (convenção 15): em `AgentConsumptionCard.test.tsx`, o caso *"o nome
      leva ao detalhe do agente"* passa a exigir
      `href = /agents/${ATENDENTE}?tab=insights`. **Hoje ele passa com
      `/agents/${ATENDENTE}` — o guarda tem de falhar primeiro.**
- [x] 2.2 Guarda novo, negativo: o destino **não** cai na visão geral. Afirmar
      que o `href` carrega o parâmetro de aba e que ele é `insights` — o par que
      pega o dia em que alguém trocar o valor por outro reconhecido por
      `parseTab`.
- [x] 2.3 Trocar o `to` do `Anchor` em `AgentConsumptionCard.tsx` para
      `` `/agents/${linha.agentId}?tab=insights` ``. Ver 2.1 e 2.2 passarem.
- [x] 2.4 Conferir que **nenhum outro** destino do card mudou. O único outro
      elemento acionável é o `Anchor component="button"` da nova tentativa do
      catálogo, que não tem `href` — registrar que o par negativo **não existe**
      por ausência de segundo link, em vez de inventar um guarda vazio.

## 3. As afirmações que a decisão tornou falsas

Nenhuma tem comportamento a guardar. São comentário — e é por isso que a 1.1
existe: sem a lista fechada, comentário é o que mais sobrevive apontando para o
vazio.

- [x] 3.1 `AgentConsumptionCard.tsx`, comentário do topo (sítio 1): reescrever
      com a **causa**, não com o resultado. Tem de dizer três coisas: as três
      colunas ficam fora **por decisão** (#67, caminho 2), a profundidade está na
      aba do agente a um clique, e **nada entra no lugar delas** — o rodapé saiu
      na #52 e não volta. Sem prometer trabalho futuro.
- [x] 3.2 `AgentConsumptionCard.tsx`, gatilho do destaque (sítio 3): trocar *"o
      destaque melhora quando a #67 fechar"* por **"o primeiro pedido do dono por
      taxa de falha no ranking"**, apontando para a D3 do `design.md` desta change
      — a mudança começa pela **população da tabela**, não por uma coluna.
      Manter a ressalva medida, que continua verdadeira.
- [x] 3.3 `AgentConsumptionCard.test.tsx` (sítios 4 e 5): mesmo gatilho no
      comentário do caso, e o cabeçalho de seção passa a nomear a decisão.
      **A asserção do caso do destaque NÃO muda** — conferido: ela afirma o
      vermelho, e o critério continua sendo a maior contagem absoluta.
- [x] 3.4 `types/systemInsights.ts:73` (sítio 6): `AgentTokens` deixa de ser
      *"a lacuna L4"* e passa a dizer o que é — a rota serve tokens por agente, e
      tasks, tokens por task e duração p95 vivem em `GET /insights/agents/{id}`,
      por decisão da #67.
- [x] 3.5 `DeclaredGap.tsx:46` (sítio 7): a L4 sai da frase da variante `block`,
      e a frase passa a apontar a issue da variante órfã (tarefa 4.1). **Não
      mexer no branch nem no `variant` default** — é a issue, não esta change.
- [x] 3.6 `DeclaredGap.tsx:21` (sítio 8, achado na varredura da 1.1): a **#67**
      sai da lista de onde mora a causa das lacunas. `#51` e `#66` ficam — são
      lacunas de verdade e não são assunto desta change.

## 4. Issues (convenção 23) — abrir antes do archive

- [x] 4.1 **Aberta: #83.** A variante `block` do `DeclaredGap` é branch morto e é o
      `variant` **default**. Os dois consumidores passam `variant="inline"`;
      ficou órfã quando a décima rodada da #52 removeu os três rodapés. Como é o
      default, `<DeclaredGap>` sem a prop renderiza o quadro que o requisito
      *"Coluna sem fonte sai sem deixar quadro no lugar"* proíbe. **App:
      `apps/frontend`. Tipo: débito técnico. Gatilho: imediato.**
- [x] 4.2 **Aberta: #84.** O buraco latente na coluna Falhas. As linhas nascem de
      `tokens.byAgent` (`provider_calls join task_executions`), então agente que
      executou e não chamou provedor **não tem linha** — e **6 das 9 falhas** do
      banco de dev vêm de execuções sem nenhuma chamada. Um agente cujas falhas
      fossem todas desse tipo teria as falhas invisíveis na página do sistema.
      Hoje não acontece por coincidência de população. **App: `apps/frontend`
      (e possivelmente `apps/api`, se a população certa exigir campo novo).
      Tipo: bug. Gatilho: imediato.**
- [x] 4.3 **Conferido: não tinha. Aberta: #85.** O período que não viaja — item **(e)** do
      `02`, herdado da #52. Se não tiver, abrir: as duas telas guardam o período
      em `useState` com `DEFAULT_INSIGHTS_PERIOD`, e quem comparou em 90 dias
      abre a aba na janela padrão sem aviso. **Citar que ela deixa o clique único
      desta change pela metade** (D7). **App: `apps/frontend`. Tipo: feature.**
- [x] 4.4 **Feito: #80 fechada, conteúdo movido ANTES.** Antes de fechar a #80, **mover para a
      #75** os dois itens que só existem nela: motivo desconhecido renderizado
      cru para a soma fechar com `rejectedAtEntryCount`, e o registro de que o
      `caveat` morto já foi limpo. Fechar a #80 apontando para a #75.
- [x] 4.5 **Feito** — #75 atualizada com o conferido nesta leitura: o item do `caveat`
      **já está feito** — `rejection-reason-not-collected` saiu de
      `caveatLabels.ts` e há teste afirmando a ausência; e o item 3 é real —
      `FailuresCard.tsx:116` rotula `errors.rejectedCount` como *"Recusadas na
      entrada"*, que é a outra população.

## 5. Registro

- [x] 5.1 `02-HISTORICO_E_STATUS.md`: entrada nova para esta change, com **os
      três argumentos medidos** (a divisão que não fecha, o `p95` sem desconto, a
      população da linha), **o custo que não foi a razão** (~24 consultas, +12% —
      D2), o **gatilho de reabertura** da #67, e a declaração de que **a
      conferência visual não aconteceu** e por quê (convenção 6). Fechar o item
      **(d)** da etapa 5 apontando para aqui.
- [x] 5.2 `CHANGELOG.md`: linha da change.
- [x] 5.3 **Conferido no DELTA; a confirmação na spec sincronizada é PÓS-ARCHIVE** (o `openspec/specs/` só muda lá). Conferir que a spec sincronizada deixou de se contradizer: o requisito
      *"Consumo por agente…"* não exige mais o texto que o requisito
      *"Métrica aprovada no protótipo e sem fonte…"* proíbe.

## 6. Verificação

- [x] 6.1 **Baseline de `apps/frontend` primeiro, em árvore limpa**, declarando o
      `podman ps` na saída (convenção 19). Não herdar o **1383 / 118** do
      fechamento da #53 — é de outro commit.
- [x] 6.2 **1373 / 117**, contra a baseline 1372 / 117 — o +1 é o guarda negativo da 2.2. Suíte de `apps/frontend` verde depois da mudança, comparada contra a
      baseline de 6.1.
- [x] 6.3 **Conferido: `git status` de apps/api, apps/workers e apps/inbox vazio.** Não rodar `apps/api`, `apps/workers` nem `apps/inbox` — nenhuma
      linha delas no diff. Registrar como não rodadas, com o motivo.
- [x] 6.4 **Verde (exit 0).** `npx tsc -b` em `apps/frontend` — **`tsc --noEmit` avulso não checa
      nada** nesta árvore (item (j) da etapa 5: o `tsconfig.json` da raiz é
      arquivo de referências, `"files": []`).
- [x] 6.5 `openspec validate --all` verde — **62 passed, 0 failed**.
- [x] 6.6 **Conferência do dono — feita em 26/09/2026, com três respostas.**

      **(a) O clique único: FUNCIONA**, e há um episódio que vai registrado
      inteiro. Numa primeira tentativa a página abriu na **Visão geral** com a
      URL mostrando `/agents/{id}?tab=insights` na barra de endereço. Depois de
      parar e subir o serviço de desenvolvimento, **não reproduziu**.
      **A causa não foi determinada** (convenção 19). *Não* se escreve "era cache
      do Vite": isso explicaria o link antigo, **não** a URL certa com a aba
      errada, e a explicação que quase serve é pior que a ausência dela.
      **Gatilho:** se voltar, é defeito de leitura de aba e a #53 precisa saber.

      **(b) O destaque de falhas: CORRETO na tela, e a medição anterior estava
      ERRADA.** A tela mostra Triagem com *"Nenhuma"* e Especialista Técnico
      Ambiente com **2 em vermelho**. A D4 afirmava o oposto porque mediu
      `task_executions` **sem o recorte de regime**: o regime de execução começa
      em `2026-09-22T04:21:00Z` e as 7 falhas da Triagem são de `03:03`–`03:21Z`,
      anteriores a ele. **D4, `02`, o comentário do componente e o do caso de
      teste corrigidos**, e o cenário do "pior que não leva o destaque" passou a
      **hipótese declarada**, nunca medição (convenção 6).
      **Open Question 2 respondida:** o critério fica; o gatilho reescrito basta.

      **(c) O guarda de travessia — ver 6.7.**
- [x] 6.7 **O guarda que faltava, escrito e CLASSIFICADO COMO DE DEFEITO.**

      **Primeiro o que já existia, para não duplicar:** o guarda que a
      conferência pediu — renderizar com `?tab=insights` e asserir o painel — **já
      existe**, em `AgentDetailPage.test.tsx:600`, escrito pela #53. Ele é de
      **regressão** (passa no `HEAD`) e não foi verificado por esta change.
      Escrever um gêmeo dele teria dado ilusão de cobertura nova.

      **O que de fato faltava é a COSTURA**, e o episódio de (a) a expôs: o card
      afirma o `href`, a página afirma que o parâmetro abre o painel, e **nenhum
      dos dois navega**. Escrito em `src/app/router.test.tsx`: parte de
      `/insights`, **clica** no nome do agente e afirma a aba `aria-selected` e o
      painel presente.

      **Classificação (convenção 15): guarda de DEFEITO, verificado.** Com o `to`
      revertido para `/agents/${id}` ele reprova; restaurado, passa.

- [x] 6.8 **ACHADO DE ESCOPO: um quinto arquivo, e a causa é a prevista.**
      `src/app/router.test.tsx` **não estava na lista fechada** e não poderia
      estar: a varredura foi por `#67|L4`, e esse arquivo nunca menciona nenhum
      dos dois.

      > **Varredura por texto acha o que FALA do assunto, não o que o EXECUTA.**

      Junto veio um buraco latente do próprio arquivo: `getAgentInsights` **não
      estava mockada** ali, embora a aba seja alcançável pela árvore de rotas — o
      modo de falha que o comentário do próprio arquivo descreve ("função que
      falta escapa para a rede, e quem reprova é o caso SEGUINTE, na tela de
      login"). Mockada junto com o guarda.

- [x] 6.9 **Régua nova, TERCEIRA ocorrência da mesma forma:**

      > **Guarda que afirma o meio do caminho não prova o fim dele.**

      As três: o `Assert.All` sobre lista vazia da **#65**; o branch morto com
      teste verde da **#83**; e aqui, os dois guardas nas pontas da travessia com
      a travessia descoberta.

- [x] 6.10 **A hipótese da baseline da #53: REFUTADA.** Rodada a suíte no commit
      de fechamento da #53 (`f7a819f`, em worktree separada): **1372 / 117** — o
      mesmo do `HEAD`, e não os **1383 / 118** registrados. Então **não** é o
      registro ter nomeado o commit das baselines em vez do de fechamento: o
      número não reproduz em nenhum dos dois. **Fica como "não se sabe"**, com os
      dois números e os dois commits.
      **A régua vale nos dois casos:** registro de baseline precisa do commit **do
      fechamento** colado.

## 7. Convenção 18 — medição de fechamento

- [x] 7.1 **Medição de fechamento, feita DEPOIS da conferência do dono.**
      Unidade igual à declarada no `design.md`. Erro = `(projetado − medido) /
      medido`.

      | dimensão | projetado | **fechamento** | erro |
      |---|---|---|---|
      | cenários de delta | 9 (1 novo, 2 reescritos, 6 copiados) | **10** (2 novos, 2 reescritos, 6 copiados) | −10% |
      | arquivos criados | 0 | **0** | **exato** |
      | arquivos modificados (produção + teste) | 2 (1+1) | **5** (3+2) | **−60%** |
      | casos de teste novos | 1 | **2** | −50% |
      | casos adaptados | 2 | **1** | +100% |
      | linhas de produção à mão | ~25 | **64** | **−61%** |
      | linhas de teste à mão | ~12 | **81** | **−85%** |
      | duplos | 0 | **1** (`getAgentInsights: vi.fn()`) | −100% |
      | **linhas geradas** | **0** | **0** | **exato** |

      **A projeção NÃO foi corrigida** (a do `design.md` segue como estava):
      corrigi-la depois de aprender apagaria a medição.

      **O desvio de arquivos mudou de causa no meio do caminho, e isso é o mais
      útil daqui.** Projetados 2; a conferência de escopo da 1.1 achou 4; a
      conferência do dono trouxe o **quinto**. **São duas causas diferentes, e
      nenhuma é descuido de contagem:**

      | de → para | causa |
      |---|---|
      | 2 → 4 | a projeção enumerou o que **muda de comportamento** numa change que muda **afirmações** (D8) |
      | 4 → 5 | a varredura foi por texto (`#67\|L4`), e **acha o que fala do assunto, não o que o executa** (6.8) |

      **As duas juntas dão a régua de escopo para change de registro:** enumerar
      por **afirmação** cobre o que declara, e ainda falta o que **executa** — e
      esse não se acha por varredura de texto nenhuma, só perguntando *"quem
      atravessa este caminho?"*.

      **E as linhas erraram muito mais que os arquivos** (−61% e −85% contra
      −60%), pelo motivo da 7.2.

- [x] 7.2 **Proporção comentário:lógica — medida e marcada como NÃO-ÂNCORA.**

      | tipo | adicionadas | comentário | lógica | proporção |
      |---|---|---|---|---|
      | produção | 64 | 63 | **1** | **63:1** |
      | teste | 81 | 46 | 35 | **1,31:1** |

      **Uma linha de lógica em produção** — o `to` do `Anchor`. Todo o resto do
      lado de produção é comentário e tipo.

      **Isto NÃO serve de âncora para projetar nada**, e a razão é de regime, não
      de estilo: numa change de registro a proporção não mede disciplina de
      comentário, mede **que não há lógica no denominador**. Ancorar qualquer
      projeção futura nela produziria número sem sentido.

      **A régua da vigésima primeira ganha a terceira condição, agora com
      evidência:** ancorar a proporção em arquivo do **mesmo tipo**, da **mesma
      linguagem** *e* do **mesmo regime**. As medições 21ª (TSX de tela, 0,78:1 e
      0,41:1) e 22ª (TSX de registro, 63:1) são a mesma linguagem e o mesmo tipo
      de arquivo, e diferem em **duas ordens de grandeza**.
