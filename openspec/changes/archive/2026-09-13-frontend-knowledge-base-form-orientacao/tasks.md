> **Esta change foi implementada duas vezes.** A primeira, em 13/09/2026 sobre
> `3684ed1`, ficou inteira num `git worktree` e **nunca foi commitada**; a limpeza
> pós-merge do PR #17 varreu o diretório e levou tudo junto — sem `git add`, não
> havia blob para o `git fsck` recuperar. A segunda, registrada aqui, foi
> reconstruída sobre `f925ebc` em branch do checkout principal. **Todos os números
> abaixo são da segunda**, remedidos do zero: a base mudou (o PR #17 acrescentou
> testes), então nenhum número da primeira foi reaproveitado. A causa e a régua
> nova estão na memória `limpeza-pos-merge`.

## 1. Baseline (convenção 19)

- [x] 1.1 (`apps/frontend`) Medir a baseline **antes** de qualquer edição, na
      branch `feat/frontend-knowledge-base-form-orientacao` a partir de `main` em
      `f925ebc`, com `npm ci` limpo. **Resultado: 81 arquivos / 850 testes.**
      Saídas completas em `~/.cache/buteco-agents/kb-5a4-baseline-f925ebc/`.

      **A baseline exigiu TRÊS execuções, e isso é registro, não rodapé.** A
      primeira reprovou **3 testes em 2 arquivos**; a segunda e a terceira deram
      **850/850**. A primeira rodou logo após o `npm ci`, com **210s** de duração
      contra 141s da terceira — contenção de máquina, mesma assinatura da família
      de flake que `agente-enderecos-a2a` fechou.

      **E um erro de instrumentação meu, que vale mais que o incidente:** a
      identidade dos 3 testes **se perdeu**, porque a primeira execução foi
      capturada com `npm test | tail -8`. Truncar a saída do instrumento produz
      exatamente o que a convenção 19 proíbe — um número sem diagnóstico. As duas
      execuções seguintes foram capturadas inteiras. **Régua: saída de suíte se
      guarda inteira, e o `tail` é para ler, nunca para gravar.**
- [x] 1.2 (`apps/frontend`) Reproduzir os arquivos que reprovam `format:check`,
      para que "pré-existente" seja conclusão e não leitura. **São 7, não os 8 que
      o registro carrega**: `LoginPage.tsx` saiu da lista porque a
      `frontend-marca-visual` o tocou e formatou junto. Saída em
      `~/.cache/buteco-agents/kb-5a4-baseline-f925ebc/prettier-baseline.txt`.
- [x] 1.3 Antes de **cada** execução da suíte daqui em diante, encerrar processo
      por **porta** e não por linha de comando (`lsof -ti :5173 | xargs kill`), e
      contar a execução anterior da suíte como contenção — não medir duas ao mesmo
      tempo.

## 2. Formatação, antes do conteúdo (design.md, R7)

- [x] 2.1 (`apps/frontend`) Rodar `npx prettier --write` **só** nos quatro
      caminhos que esta change abre: `KnowledgeBaseForm.tsx`,
      `KnowledgeBaseForm.test.tsx`, `KnowledgeBaseDescriptionCard.tsx`,
      `KnowledgeBaseDescriptionCard.test.tsx`. **Não** tocar
      `KnowledgeBaseEditPage` ×2 nem `agentUsage.ts` — o critério é a change abrir
      o arquivo, não o arquivo estar na mesma lista (design.md, D6).
- [x] 2.2 (`apps/frontend`) Conferir que `format:check` passou de **7 para 3**, e
      anotar quais restam: `KnowledgeBaseEditPage` ×2 e
      `mcp-servers/utils/agentUsage.ts`.

## 3. `KnowledgeBaseForm` — a orientação da descrição (D1, D3)

- [x] 3.1 (`apps/frontend`) Acrescentar ao parágrafo de apoio do bloco da
      descrição os dois critérios que faltam: **dizer do que a base não trata**, e
      que **descrição genérica atrai perguntas de outras bases**. Texto de apoio
      permanente, dentro do bloco que já existe — nada condicional, nada derivado
      do conteúdo digitado.
- [x] 3.2 (`apps/frontend`) Reescrever o sufixo do contador. Sai *"curto demais
      para o modelo decidir com segurança"*, que promete segurança acima do limiar
      e é o que `0d` refutou; entra *"curto demais para caber o assunto e o que
      fica de fora"*, que diz o que os 80 caracteres de fato medem.
      `SHORT_DESCRIPTION_THRESHOLD`, contador e cor de aviso **permanecem**.
- [x] 3.3 (`apps/frontend`) Atualizar o comentário de
      `SHORT_DESCRIPTION_THRESHOLD` para dizer o que o limiar **não** mede, com o
      número de `0d` ao lado — é a frase que impede a próxima change de "melhorar"
      o aviso subindo o limiar.

## 4. `KnowledgeBaseForm` — o nome da tool (D4)

- [x] 4.1 (`apps/frontend`) Reescrever o comentário que recusa `consultar_base`.
      **Não apagar.** A premissa antiga era metade certa: *"é decisão da etapa 4"*
      foi resolvida, *"não existe em spec nenhuma"* **continua verdadeira** —
      conferido por `grep search_` em
      `openspec/specs/knowledge-tool-execution/spec.md`, que não devolve nada. O
      comentário novo carrega as três razões independentes do design.md, cada uma
      com arquivo e linha.
- [x] 4.2 (`apps/frontend`) Renomear o teste `'não afirma um nome de tool que o
      sistema ainda não definiu'` — o nome dele afirma o que deixou de ser
      verdade. A asserção fica, **estendida ao segundo prefixo** (`search_`); o que
      estava errado era a justificativa.

## 5. `KnowledgeBaseForm` — o preview (D5)

- [x] 5.1 (`apps/frontend`) Reescrever o título e a nota do bloco "Como o agente
      vê esta base". Passa a `Seu texto, dentro do que o agente recebe`, mais a
      nota de que o sistema envolve o texto em instruções fixas. Os dois valores
      exibidos **continuam** os mesmos.
- [x] 5.2 (`apps/frontend`) **Não** reproduzir o texto de `ComoLerOResultado` (441
      caracteres, `KnowledgeToolDescription.cs:44`). A afirmação é **estrutural**.
- [x] 5.3 (`apps/frontend`) Escrever o gatilho no comentário do componente, com
      arquivo e linha (`KnowledgeToolDescription.cs:52`).

## 6. `KnowledgeBaseDescriptionCard` — a mesma frase, no detalhe (D6)

- [x] 6.1 (`apps/frontend`) Corrigir a nota que diz *"Ele é a descrição da
      ferramenta que o agente vê"*. A segunda metade fica; a primeira afirma
      identidade onde há **parte**, e vira *"entra na descrição da ferramenta …
      envolvido por instruções fixas do sistema"*.
- [x] 6.2 (`apps/frontend`) Atualizar o comentário do componente, que repetia a
      mesma afirmação em prosa.

## 7. Guardas — cada negativa é um `it()` (convenções 15 e 18)

A projeção contou **6** asserções negativas antes de escrever, como a quarta causa
da convenção 18 pede: numa tela cujo valor está no que ela se recusa a afirmar,
cada negativa é um `it()`, nunca uma cláusula dentro de um teste positivo.

**Entregues 8, e a causa é estrutural — registrada em 8.4.** A projeção contou
negativas **por afirmação recusada**; a spec as escreveu **por estado
observável**, e duas das seis afirmações têm dois estados cada:

| afirmação recusada (projeção) | estados observáveis (spec) |
|---|---|
| o contador não emite veredito de qualidade | **N2** abaixo do piso · **N3** acima do piso |
| o preview não se declara o texto completo | **N6** o rótulo · **N7** não reproduzir o texto fixo |

É a própria quarta causa uma volta acima: a unidade de projeção precisava casar
com a **da spec**, não só com a de entrega. Nenhuma negativa apareceu que a spec
não previsse — as oito estão nos cenários do delta.

- [x] 7.1 **N1** — nenhum identificador de ferramenta, nem o `consultar_base` do
      protótipo nem o `search_` da etapa 4.
- [x] 7.2 **N2** — abaixo do piso, o sinal não afirma nada sobre a decisão do
      modelo.
- [x] 7.3 **N3** — acima do piso, nenhum selo de aprovação.
- [x] 7.4 **N4** — nenhum veredito de generalidade. Guarda **comportamental**: duas
      descrições de mesmo comprimento (87 caracteres) e generalidade oposta
      produzem a **mesma** devolutiva, e nenhuma gera `role="alert"`.
- [x] 7.5 **N5** — nenhum número de uso da base. Guarda sobre o **bloco inteiro**:
      removido o texto do contador, não sobra dígito nenhum.
- [x] 7.6 **N6** — o preview não se intitula como tudo o que o agente recebe.
- [x] 7.7 **N7** — o preview não reproduz as instruções fixas.
- [x] 7.8 **N8**, em `KnowledgeBaseDescriptionCard.test.tsx` — o card não afirma
      que a descrição cadastrada **é** a descrição da ferramenta.
- [x] 7.9 Positivas novas: **P1** delimitação; **P2** genérica atrai perguntas de
      outras bases; **P3** o preview declara as instruções fixas sem reproduzi-las;
      **P4** o card diz "entra na descrição da ferramenta".
- [x] 7.10 **Asserções existentes a editar: UMA, e não das quatro projetadas.** A
      projeção previu 4 (`/curto demais/` ×3 no formulário e `/o modelo decide se a
      pergunta pertence a esta base/` no card). **Nenhuma dessas foi necessária:**
      as quatro estavam escritas contra a metade **estável** de cada frase, e esta
      change edita a outra metade. A única edição veio do achado de 9.5 —
      `/o modelo recebe só o nome e decide no chute/` virou
      `/o modelo recebe só o nome desta base e decide no chute/`.

      **A consequência é o argumento da convenção 15 medido:** a suíte de 850
      testes **não enxerga** nenhuma das duas correções de cópia desta change.
      Confirmado em 8.2.

## 8. Verificação dos guardas, com as duas metades registradas (convenção 15)

- [x] 8.1 Para cada `it()` de 7.1 a 7.9: escrever o teste, **reintroduzir o defeito
      de propósito**, ver reprovar, e só então manter a correção.
- [x] 8.2 Registrar as duas metades — o que reprovou e **o que continuou verde**.
- [x] 8.3 Conferir que cada guarda reprova **no componente que a correção vai
      tocar** — a segunda forma do erro, registrada em `dedupe-global-nome-de-tool`.

      **Resultado, oito defeitos reintroduzidos um a um** (saída completa em
      `~/.cache/buteco-agents/kb-5a4-baseline-f925ebc/guardas-convencao-15.txt`):

      | defeito reintroduzido | reprovou | onde |
      |---|---|---|
      | identificador `search_` no preview | 1 de 35 | N1, formulário |
      | sufixo "para o modelo decidir com segurança" | 1 de 35 | N2, formulário |
      | `· boa` acima do piso | 1 de 35 | N3, formulário |
      | `· genérica demais` quando o texto contém "diversos" | 1 de 35 | N4, formulário |
      | `Consultada 12 vezes pelos agentes.` no bloco | 1 de 35 | N5, formulário |
      | rótulo "Como o agente vê esta base" | 1 de 35 | N6, formulário |
      | trecho de `ComoLerOResultado` colado na nota | 1 de 35 | N7, formulário |
      | "Ele **é** a descrição da ferramenta" no card | 2 de 35 | **N8 e P4, card** |

      **A separação está medida nos dois sentidos:** nenhum dos sete defeitos do
      formulário reprovou teste do card, e o do card não reprovou teste nenhum do
      formulário. O único que derruba dois derruba a negativa **e** a positiva da
      mesma frase, as duas no card — esperado, porque leem as duas metades dela.

      **E o veículo do defeito também precisa ser limpo**, o que custou uma volta:
      a primeira versão do patch de N5 alterava o texto da nota do preview, e por
      isso derrubava **N5 e P3** — dois, mas um deles por artefato do patch, não
      por alcance do guarda. Reancorado para inserir um `<Text>` novo depois do
      contador, N5 passou a derrubar **exatamente um**. **Régua: defeito injetado
      que altera texto vizinho mede o guarda errado.**

      **O que continuou verde, que é a metade que costuma faltar:** contra a cópia
      velha do contador, os três testes de `/curto demais/` passaram. Contra a
      cópia velha do card, `'diz que o texto não é mostrado ao cliente e para que
      serve'` passou. As asserções antigas estão presas ao **comportamento**, não
      à cópia.
- [x] 8.4 **Registrar a divergência de contagem**: projetadas 6 negativas,
      entregues 8. A causa não é escopo novo nem negativa imprevista — **as oito
      estão nos cenários do delta de spec**. A projeção contou por afirmação
      recusada; a spec escreveu por estado observável.

## 9. Conferência manual (convenção 14) — tarefa própria, iterativa, nos dois esquemas

- [x] 9.1 Subir painel e protótipo, encerrando por **porta** antes e depois.

      **Como foi montado, e o que isso NÃO cobre.** Não há ambiente de produção e
      a stack não estava no ar. O painel subiu no Vite real (`:5173`), com um
      **stub de `apps/api` em `:5017` que loga toda chamada e devolve 200**, e o
      acesso pelo `ProtectedRoute` com token em `sessionStorage` — que é tudo o que
      aquele guarda verifica. Conferido no fim: **zero rotas "NÃO PREVISTA"** no
      log. As duas portas encerradas e conferidas em zero.

      **Não alcança** dado real, migração nem o login de verdade. Para uma change
      de cópia dentro de componentes que não mudaram de forma isso não morde, mas
      não é equivalente a percorrer a stack inteira.
- [x] 9.2 **Percorrer** o formulário nos **dois esquemas**, em criação e edição,
      com descrição vazia, curta, longa e de **423 caracteres** (o perfil da
      atratora de `0d`). Esse estado renderiza `423 caracteres`, **sem sufixo e em
      tom padrão** — a tela não afirma nada sobre ela, que é o ponto.
- [x] 9.3 **Medir, não comparar aparência.** Régua declarada antes: o parágrafo de
      orientação não passa de **6 linhas** a 620px.

      | medida | 1440px | 968px |
      |---|---|---|
      | parágrafo de orientação | 620px / **4 linhas** | 620px / **4 linhas** |
      | nota do preview | 620px / 2 linhas | 620px / 2 linhas |
      | bloco da descrição | 419px vazio → 454px com 423 caracteres | — |

      **ACHADO da primeira implementação, já incorporado:** a nota nova do preview
      saiu com **1058px** de linha contra os 620px de toda a prosa da tela, por
      falta de `maw`. Olhar não pegaria — o texto cabe e não quebra feio. Corrigido
      com `maw={PROSE_MAX_WIDTH}` e o comentário do porquê.

      **Resultado negativo, medido no navegador e não só lido em `theme.ts`
      (convenção 16):** `c="yellow"` resolve por esquema. Claro `rgb(176,122,30)`,
      contraste **3,72** — o mesmo número que `theme.test.ts` guarda. Escuro
      `rgb(226,166,63)`, contraste **7,84**. Nenhuma variável nova nasce.
- [x] 9.4 Percorrer o **card de descrição no detalhe**, nos dois esquemas. Sem
      achado: a nota corrigida cabe em 1 linha, mesmo perfil da nota irmã do card
      de documentos. O detalhe **não** capa prosa em 620px, e essa é a régua local.
- [x] 9.5 **Ler** o protótipo (`kbFormVals`, `Buteco Agentes.dc.html:1818-1831`)
      contra a spec viva.

      **ACHADO, terceiro membro da família do D5 e o menor.** `kfPreviewDesc`
      trazia *"Sem descrição: o modelo recebe só o nome…"*, e desde a etapa 4 o
      modelo recebe também o bloco fixo. Sozinha seria imprecisão tolerável;
      **ao lado da nota nova vira contradição na mesma tela**. Corrigido para
      *"só o nome **desta base**"* — o bloco fixo é igual para todas e não
      distingue nenhuma. O estado é inalcançável em produção (a API exige descrição
      não-vazia), e por isso é o menor dos três.

      **O resto do bloco foi lido sem achado:** `kfChars` (já corrigido em D3),
      `kfCharsFg`, `kfPreviewName` e a cópia de validação de `kfSubmit` — essa
      continua verdadeira depois da etapa 4. A família tem **três** membros nesta
      tela, não mais.
- [x] 9.6 Iterar: a correção do `maw` foi remedida, e a de 9.5 só apareceu porque a
      nota nova já estava na tela ao lado dela.
- [x] 9.7 **Reconferir sobre `f925ebc`**, porque a base mudou: a
      `frontend-marca-visual` trocou o logo da casca e acrescentou
      `--buteco-brand-ink` ao tema. Percorridas as duas telas nos dois esquemas na
      árvore nova: **todas as medidas idênticas** — orientação 620px/4 linhas, nota
      620px/2 linhas, contraste 7,84 no escuro. A marca nova não desloca nada
      nestas duas telas.

## 10. Verificação final

- [x] 10.1 (`apps/frontend`) Suíte completa, com a porta limpa antes.
- [x] 10.2 (`apps/frontend`) `npm run lint` e `npx tsc -b --noEmit` limpos.
- [x] 10.3 (`apps/frontend`) `format:check` reprovando em **3** — os que esta
      change não abriu.
- [x] 10.4 `openspec validate --all`.

## 11. Documentação, um artefato por vez

- [x] 11.1 `openspec/specs/knowledge-base-catalog-ui/spec.md` — sincronizar os dois
      requisitos `MODIFIED`. **Feito em 13/09/2026, depois da revisão e do merge do
      PR #18.** Os dois foram substituídos inteiros: `A descrição da base é
      apresentada como texto lido pelo modelo` (19 → 30 linhas, +1 cenário) e
      `Orientação e preview da descrição no formulário` (46 → 133 linhas, de 5 para
      **14** cenários). Conferido depois: `## Requirements` no lugar, 10 requisitos
      sem duplicata, nenhum cabeçalho de delta vazado, nenhuma cópia velha
      sobrevivente, `openspec validate --all` em **50/50**.
- [x] 11.2 O `## Purpose` foi editado **nas duas metades da mesma frase**, que era
      a armadilha registrada aqui. Não bastava trocar `três` por `quatro`: a frase
      também afirmava *"sempre da mesma forma"*, e o quarto lugar **não** é da
      mesma forma. Ficou:

      > *"…se manifesta em **quatro** lugares desta interface, **não todos da mesma
      > forma**: nos três primeiros, distinguindo 'sei que é zero' de 'não sei'; no
      > quarto, distinguindo **a parte do todo**."*

      Com um quarto marcador na lista, na mesma voz dos outros três. A leitura que
      antecipou isso está preservada abaixo.

      O `## Purpose` vivo não tem `TBD`, e mesmo assim **fica incompleto com esta
      change**. Ele diz que a regra de não afirmar o que o sistema não sabe *"se
      manifesta em **três** lugares desta interface, **sempre da mesma forma**:
      distinguindo 'sei que é zero' de 'não sei'"*.

      Esta change acrescenta um **quarto** lugar que **não é da mesma forma** — não
      é zero contra desconhecido, é **parte contra todo**. O sync precisa mudar as
      duas coisas na mesma frase: o número **e** a afirmação de que a forma é
      sempre a mesma. Trocar só o número deixaria a segunda metade falsa.
- [x] 11.3 `CHANGELOG.md` — **ler, não só acrescentar**.
- [x] 11.4 `01-ARQUITETURA_E_CONVENCOES.md` — convenção 13 ganha a forma nova, e a
      18 ganha a quarta causa mais o degrau da unidade de spec.
- [x] 11.5 `02-HISTORICO_E_STATUS.md` — fechar os **dois itens abertos**.
- [x] 11.6 `02-HISTORICO_E_STATUS.md` — corrigir o item de `format:check`.
- [x] 11.7 `02-HISTORICO_E_STATUS.md` — corrigir o item do nome da tool.
- [x] 11.8 `02-HISTORICO_E_STATUS.md` — registrar o sintoma de base-atrator (D2).
- [x] 11.9 `02-HISTORICO_E_STATUS.md` — a **décima quarta** correção de protótipo.
- [x] 11.10 `02-HISTORICO_E_STATUS.md` — atualizar "Status atual" e "Próximo passo".
- [x] 11.11 `02-HISTORICO_E_STATUS.md` — atualizar a tabela da fila.

## 12. Projeção de tamanho (convenção 18), feita DEPOIS da verificação

| | arquivos | linhas |
|---|---|---|
| **Criados** | **0** | **0** |
| Modificados — produção | 2 | ~55-70 |
| Modificados — teste | 2 | ~110-135 |
| **Total de código** | **4** | **~165-205** |

- [x] 12.1 Comparar o entregue com esta projeção, **decomposto**, e registrar a
      direção **com a causa**.

      **Primeiro, separar o reflow do conteúdo**, porque a formatação e a mudança
      ficaram na mesma árvore (nada foi commitado nesta sessão). Reconstruído
      "HEAD + prettier" e medidos os dois saltos: **37 linhas de reflow** (13 + 16
      + 4 + 4), que não são trabalho, e **+281 / −27 de conteúdo**. Os números
      abaixo são só o segundo salto.

      **E a primeira medição desse reflow estava ERRADA por fator de 9** — 332
      linhas —, porque as cópias ficaram em `/tmp` e o `prettier` resolve
      configuração pelo caminho do **arquivo**, não pelo `cwd`: formatou com o
      default em vez do `.prettierrc` do projeto, sem erro visível. Refeito com as
      cópias dentro de `apps/frontend/`. Registrado em `02` junto com o outro erro
      de instrumento desta change.

      | | projetado | entregue | desvio |
      |---|---|---|---|
      | arquivos criados | 0 | **0** | exato |
      | arquivos modificados | 4 | **4** | exato |
      | linhas — produção | ~55-70 | **+113 / −19** | +61% sobre o topo |
      | linhas — teste | ~110-135 | **+168 / −8** | +24% sobre o topo |
      | **total de código** | **~165-205** | **+281 / −27** | **+37% sobre o topo** |

      **A contagem de arquivo acertou exata pela terceira vez seguida** com o
      método de contar criados e modificados separadamente a partir do blast
      radius lido no código. E a varredura do quinto arquivo — `grep -rn` pelas
      cadeias de cópia — **funcionou**: nenhum apareceu.

      **A causa do desvio de LINHAS é estrutural, e é nova nesta convenção: numa
      change cujo entregável é a RECUSA, o artefato é a razão registrada — e razão
      registrada mora em comentário, não em código.**

      | | linhas totais | comentário | não-comentário |
      |---|---|---|---|
      | `KnowledgeBaseForm.tsx` | 183 → 268 (**+85**) | 22 → 93 (**+71**) | 161 → 175 (**+14**) |
      | `KnowledgeBaseDescriptionCard.tsx` | 46 → 54 (**+8**) | 12 → 21 (**+9**) | 34 → 33 (**−1**) |

      **Dos +93 de produção, +80 são comentário e +13 são código.** O card
      entregou **menos** código do que tinha. Custo unitário medido: **~26 linhas
      para uma recusa de três razões com arquivo e linha**, ~11 para um limiar com
      o que ele não mede, ~14 para um rótulo com gatilho.

      **E o desvio de teste tem causa própria, já conhecida:** +33 sobre o topo, e
      os **dois `it()` negativos não projetados** (8 contra 6, ver 8.4) custam ~28
      dessas linhas. Descontadas as duas, a projeção de teste teria ficado dentro
      da faixa.

      **Nota de reprodutibilidade:** esta change foi implementada duas vezes, sobre
      bases diferentes, e a decomposição deu **os mesmos números** nas duas
      (+281/−27 de conteúdo; +80 comentário / +13 código na produção). Não é
      medição de método — é a mesma pessoa repetindo o mesmo trabalho —, mas
      elimina a dúvida de que os números fossem acidente de uma rodada.
- [x] 12.2 Conferir a razão de headline contra o código real.

      **Não houve commit** — nada foi commitado nesta sessão —, então a âncora é o
      estado da árvore. Decomposto, para que ninguém some as colunas erradas:

      | grupo | arquivos | linhas |
      |---|---|---|
      | **código** (`apps/frontend`) | 4 modificados | **+281 / −27** (mais 37 de reflow) |
      | documentação do repositório | 3 modificados | ~+280 |
      | artefatos OpenSpec + `design/` | não rastreados | **5.400 linhas**, das quais **4.243 são os três arquivos de protótipo copiados** — 79% |

      A razão de headline contra o código real ficaria em torno de **20x** se
      alguém contasse a árvore inteira — a mais extrema já registrada aqui, acima
      dos 3,1x da migração com `.Designer.cs`. E a causa é diferente daquela: lá
      era arquivo **gerado**, aqui é arquivo **copiado** (o `.dc.html` sozinho tem
      163.947 bytes). Terceiro caso da mesma lição: contagem de headline não serve
      para nada nesta base.
