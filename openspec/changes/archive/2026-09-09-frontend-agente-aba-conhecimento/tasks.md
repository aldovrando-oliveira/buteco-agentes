Todas as tarefas rodam em **`apps/frontend`**. Nenhuma toca `apps/api`,
`apps/workers` ou `apps/inbox`.

## 1. Baseline (convenção 19, antes de qualquer código)

A baseline **já foi medida verde** ao propor: `2e5f750`, paralelismo padrão,
máquina ociosa, **66 arquivos / 574 testes, 574/574, em 103 s**. As tarefas
abaixo confirmam isso no momento do apply e fixam a configuração de medição.

- [x] 1.1 (`apps/frontend`) **Conferir o limiar de carga ANTES de rodar**, com
      `uptime` e `ps aux | sort -k3 -rn | head`. A suíte só é medível quando,
      **antes** de começar:
      - load average de 1 minuto **< 5,0** (máquina de 12 núcleos); e
      - **nenhum processo alheio à suíte ≥ 100% de CPU** (um núcleo cheio) — na
        prática, a VM do Podman parada ou ociosa.

      Medido ao propor: verde com load prévio 3,1-4,2 e nada acima de ~40%;
      vermelho com load prévio 11,5-14,5 e a VM do Podman a ~289%. O limiar 5,0
      fica dentro do lado verde medido; entre 4,2 e 11,5 não há medição.
      **Não usar o load durante a execução como critério** — no paralelismo
      padrão a própria suíte leva o load a ~57 e passa mesmo assim.
> **Resultado de 1.1 no apply (18:43):** limiar **não atendido** — load de 1 min
> 4,34 (ok, < 5,0), mas a VM do Podman voltou a **311,1%**, acima do limite de
> 100%. É exatamente a condição medida que produz 21-26 reprovações. A suíte
> **não** foi rodada nessa janela: rodar ali geraria ruído que o próprio limiar
> existe para evitar.
>
> **Baseline usada:** a medição de hoje 18:33, no **mesmo commit `2e5f750`**,
> com a máquina dentro do limiar — **574/574 em 103 s**, paralelismo padrão. A
> árvore de teste de `apps/frontend` não mudou entre 18:33 e 18:43 (o único
> arquivo rastreado tocado desde então é `02-HISTORICO_E_STATUS.md`, que o
> vitest não lê). Essa é a baseline de comparação de 10.1.
>
> **Consequência para 10.1:** reconferir o limiar antes da execução final. Se a
> VM continuar quente, aplicar 1.3 — e, se o fallback for adotado, a baseline
> precisa ser **remedida** no mesmo paralelismo, porque a de 18:33 é do padrão.

- [x] 1.2 (`apps/frontend`) Com o limiar atendido, rodar `npm test -- --run` e
      confirmar **574/574**. Esse paralelismo (o padrão) fica sendo a
      **condição de medição desta change**: toda execução da suíte inteira usa
      ele, aqui e em 10.1. (Os guardas de 8.x rodam arquivo a arquivo, onde o
      paralelismo não entra na conta.) Se a configuração mudar, ela muda **dos
      dois lados**, e o `design.md` registra
      qual foi usada — comparar baseline num paralelismo com a suíte da change
      noutro deixa de comparar dois commits e passa a comparar duas
      configurações, e aí a convenção 19 não vale mais.
- [x] 1.3 (`apps/frontend`) *(não precisou disparar — 1.2 veio verde. Mantida
      como o procedimento a seguir se voltar a reprovar.)* **Onde parar, se vier
      vermelha com o limiar de 1.1 atendido.** Nesta ordem, sem pular etapa:
      1. Rodar cada arquivo reprovado **isolado**. Se algum reprovar isolado,
         **não é contenção** — é defeito, e a investigação começa aí.
      2. Se todos passam isolados, rodar a suíte inteira com
         `npx vitest run --maxWorkers=3`. Se ficar verde, **adotar
         `--maxWorkers=3` como a condição de medição da change**, registrar isso
         no `design.md`, e rodar 10.1 com ele — nunca metade em cada.
      3. Se `--maxWorkers=3` **também** vier vermelha com o limiar atendido, a
         discriminação falhou: **parar e investigar**, sem começar a implementar.
         Não é mais starvation, e chamar de ambiental aqui seria exatamente o
         erro que a convenção 19 registra três vezes.

      Baseline vermelha **não absolve** nada: remove a hipótese de regressão, e
      nem isso enquanto a causa não estiver nomeada.

## 2. Camada de dados (`apps/frontend`)

- [x] 2.1 (`apps/frontend`) Acrescentar `replaceAgentKnowledgeBases(agentId,
      knowledgeBaseIds)` a `features/agents/api/agentsApi.ts`, usando o
      `request<T>` próprio da feature (convenção 7): `PUT
      /agents/{agentId}/knowledge-bases` com corpo `{ knowledgeBaseIds }`,
      devolvendo `Agent`.
- [x] 2.2 (`apps/frontend`) Testar em `agentsApi.test.ts`: método, caminho e
      **forma do corpo** — a chave é `knowledgeBaseIds`, e o valor é o conjunto
      completo. Incluir o caso do conjunto vazio, que é a remoção de todos os
      vínculos e não deve virar omissão do campo (o servidor responde 400 a
      campo ausente).
- [x] 2.3 (`apps/frontend`) Acrescentar
      `useReplaceAgentKnowledgeBasesMutation(agentId)` a
      `features/agents/api/useAgents.ts`, no mesmo molde das outras duas
      mutações de vínculo: grava o agente devolvido em `['agents', id]` e
      invalida `['agents']`.
- [x] 2.4 (`apps/frontend`) Testar em `useAgents.test.ts` que a resposta
      alimenta o cache do detalhe e invalida a coleção.

## 3. Cruzamento e ordenação (`apps/frontend`)

- [x] 3.1 (`apps/frontend`) Criar `features/agents/utils/knowledgeBaseRows.ts`
      (diretório novo, molde em `features/mcp-servers/utils/`): dada a lista de
      ids do rascunho e o catálogo de `KnowledgeBase`, devolver as linhas com
      nome, descrição e estado, **ordenadas por nome com desempate por id**
      (D2, D4). Id sem correspondência no catálogo não vira linha nem erro.
- [x] 3.2 (`apps/frontend`) Testar `knowledgeBaseRows.test.ts`: ordem por nome;
      desempate por id entre homônimas; conjunto vazio; id ausente do catálogo
      descartado em silêncio. O teste de desempate SHALL montar a entrada em
      ordem invertida, senão passa por acaso.

## 4. Modal de vincular (`apps/frontend`)

- [x] 4.1 (`apps/frontend`) Criar
      `features/agents/components/KnowledgeBaseLinkModal.tsx`: componente
      apresentacional, sem nenhum hook de query (convenção 7) — recebe
      catálogo, conjunto escolhido e os callbacks por propriedade.
- [x] 4.2 (`apps/frontend`) Busca por nome e descrição com `matchesSearch` de
      `utils/searchText.ts` (D6). Reusar, não recriar.
- [x] 4.3 (`apps/frontend`) Linha do modal: nome, descrição, marca de base
      inativa (D5), e controle que alterna Vincular/Vinculada sem fechar o
      modal e **sem emitir requisição**.
- [x] 4.4 (`apps/frontend`) Dois vazios com textos distintos — busca sem
      correspondência × catálogo sem nenhuma base.
- [x] 4.5 (`apps/frontend`) Rodapé: link `Criar nova base` para
      `/knowledge-bases/new` e ação `Concluir`, que apenas fecha. Copy do
      cabeçalho **não** diz que o vínculo é salvo na hora (D12).
- [x] 4.6 (`apps/frontend`) Testar `KnowledgeBaseLinkModal.test.tsx`: busca sem
      acento achando base acentuada; busca alcançando a descrição; alternância
      mantendo o modal aberto; marca de base inativa e o **par negativo** (base
      ativa sem marca); os dois vazios; e a asserção **negativa** de que a
      linha não exibe contagem de documentos nem texto de indexação.

## 5. Aba Conhecimento (`apps/frontend`)

- [x] 5.1 (`apps/frontend`) Criar
      `features/agents/components/AgentKnowledgeTab.tsx` usando
      `SectionedCard` com `title` e o total do catálogo no slot `action` da
      faixa; sem recriar card, rótulo de seção nem badge.
- [x] 5.2 (`apps/frontend`) Rascunho local + `UnsavedChangesBar` +
      `useUnsavedChangesGuard` + `UnsavedChangesModal`, no mesmo idioma de
      `AgentDelegationsTab` (D3). Comparação de conjunto para decidir "sujo".
- [x] 5.3 (`apps/frontend`) Linha: nome como link para `/knowledge-bases/:id`,
      descrição, ação de desvincular (local, sem requisição), e faixa de aviso
      quando a base está inativa.
- [x] 5.4 (`apps/frontend`) Estado vazio explicativo dentro do card, com a
      ação de vincular, e resumo acima da lista distinguindo vazio de não vazio.
- [x] 5.5 (`apps/frontend`) Salvar envia **o conjunto completo resultante** em
      uma requisição, notifica sucesso e **rebaseia o rascunho na resposta**;
      em falha, notifica o erro e mantém o rascunho, sem marcar linha nenhuma.
- [x] 5.6 (`apps/frontend`) Testar `AgentKnowledgeTab.test.tsx`: lista com
      bases vinculadas; estado vazio; aviso de base inativa e o par com base
      ativa; vincular pelo modal e salvar enviando o conjunto certo;
      desvincular e salvar; remover todas e salvar com conjunto vazio;
      **nenhuma requisição ao alternar sem salvar**; barra aparecendo e
      descartar voltando ao gravado; falha preservando o rascunho; e a
      asserção **negativa** de contagem de documentos e estado de indexação
      ausentes da linha.

## 6. Página do detalhe (`apps/frontend`)

- [x] 6.1 (`apps/frontend`) Em `AgentDetailPage.tsx`: constante da aba,
      `parseTab` aceitando o valor novo, `Tabs.Tab` na **terceira** posição com
      `TabCounter` sobre `data.knowledgeBases.length`, e `Tabs.Panel`.
- [x] 6.2 (`apps/frontend`) Buscar o catálogo com
      `useKnowledgeBasesQuery({ enabled: activeTab === KNOWLEDGE_TAB })` e
      repassar por propriedade (convenção 7); `Alert` no painel quando a
      consulta falha, sem derrubar a página e sem renderizar a lista pela
      metade (D7).
- [x] 6.3 (`apps/frontend`) Testar em `AgentDetailPage.test.tsx`: as quatro
      abas na ordem certa; contador da aba nova e o par oculto quando zero;
      `?tab=conhecimento` na URL e o recarregamento reabrindo a aba; valor
      desconhecido caindo na visão geral; conteúdo da aba inativa ausente do
      DOM; **catálogo não requisitado enquanto a aba não está ativa**; e a
      falha do catálogo exibindo o alerta.

## 7. Copy do catálogo de bases (`apps/frontend`)

- [x] 7.1 (`apps/frontend`) Em `KnowledgeBaseAgentsCard.tsx`, trocar
      *"O vínculo com agentes chega na próxima etapa."* pela indicação de que o
      vínculo é feito na aba Conhecimento do detalhe do agente (D8), e ajustar
      o comentário do componente, que hoje registra o motivo oposto.
- [x] 7.2 (`apps/frontend`) Atualizar `KnowledgeBaseAgentsCard.test.tsx`, que
      hoje afirma a copy antiga.

## 8. Guardas reprovando contra o defeito real (convenção 15)

- [x] 8.1 (`apps/frontend`) Para **cada** asserção negativa de D5 (sem contagem
      de documentos, sem estado de indexação, na linha e no modal):
      reintroduzir o defeito de propósito — renderizar `0 documentos` —, ver o
      teste reprovar, e só então remover. Guarda não verificado é pior que
      nenhum.
- [x] 8.2 (`apps/frontend`) Para o guarda de "nenhuma requisição ao alternar
      sem salvar": reintroduzir a chamada por ação e ver reprovar. Checar
      também que ele reprova **no componente que a correção toca** — o segundo
      modo de falha da convenção 15 é guarda no lugar certo pelo motivo errado.
- [x] 8.3 (`apps/frontend`) Para o guarda de busca sem acento: trocar
      `matchesSearch` por comparação crua e ver reprovar.
- [x] 8.4 (`apps/frontend`) Para o guarda de `enabled` do catálogo: remover o
      `enabled` e ver reprovar.

## 9. Conferência manual (convenção 14 — tarefa própria e iterativa)

> **Resultado: quatro rodadas, convergiu na quarta.** Painel real rodando
> (`vite` em :5174), Chrome dirigido por CDP com as rotas de `apps/api`
> interceptadas e servidas com fixtures (9 bases no catálogo, 3 cenários de
> agente), nos dois esquemas de cor.
>
> - **Rodada 1 — 2 achados.** (a) O botão `Desvincular` encolhia até truncar o
>   rótulo ("Desvinc") na linha de descrição longa: `Group` é `nowrap`, mas o
>   botão não tinha `flex: none`. (b) O aviso de base inativa tinha sido escrito
>   como texto âmbar solto, quando a casa já resolve exatamente este caso —
>   "vinculado e inativo" — com `Alert color="yellow"` em `AgentMcpServerRow`.
>   Idioma inventado onde havia um estabelecido (convenção 2).
> - **Rodada 2 — 1 achado.** O modal não tinha scroll próprio: com **9** bases já
>   passava da dobra, e o handoff declara 100+ — o `Concluir` sairia da tela.
>   Corrigido com corpo rolável e busca/rodapé fixos, como o protótipo.
> - **Rodada 3 — 1 achado.** A **mesma** truncagem da rodada 1, agora nas linhas
>   do modal. A correção da aba não alcançou o segundo componente; cada um
>   precisou da sua.
> - **Rodada 4 — nada.** Convergiu *(prematuramente — ver rodada 5)*.
> - **Rodada 5, provocada pelo usuário — 1 achado, e o mais visível de todos.**
>   A lista saiu com **largura errada**: `maw={860}` no container, quando o
>   protótipo deixa o card em **largura cheia** e limita **a descrição** em
>   620px. Eu tinha invertido as duas coisas, copiando o idioma de
>   `AgentDelegationsTab` (`maw={620}` no container).
>
>   **`AgentDelegationsTab` está correto** — conferido: o protótipo dá
>   `max-width: 620px` à aba de Delegações de propósito, porque ali é lista de
>   checkbox. As três abas têm larguras **deliberadamente diferentes** no
>   protótipo (Delegações 620, Ferramentas e Conhecimento cheias). O erro foi
>   reusar o idioma da aba vizinha sem conferir que esta tinha outra largura.
>
>   **Por que quatro rodadas não pegaram:** a conferência comparou *estados*
>   contra o protótipo — lista, vazio, aviso, modal — e nunca comparou
>   **dimensões**. Somado a isso, o viewport de captura era 1440px, onde o card
>   de 860 termina perto da borda útil e a diferença não salta; na tela do
>   usuário (~1860px) ela é gritante. **Lição para a próxima etapa de UI:
>   conferir largura/altura contra o protótipo explicitamente, e capturar num
>   viewport pelo menos tão largo quanto o do operador.**
>
> **Artefato de captura, não do produto:** o Chrome headless não completa a
> transição de entrada do `Modal` (rAF), então ele fica no DOM com `opacity: 0`
> e some da captura. Custou três diagnósticos até ficar claro — estado do
> componente conferido como aberto, `mantine-Modal-root` presente — e foi
> contornado injetando CSS que anula a transição só durante a conferência.
> **Vale registrar para a próxima etapa de UI não repetir a caça.**
>
> Confirmação da nota da 5a-1: com a identidade visual já estabelecida, as
> rodadas acharam **estrutura e reúso**, não cor. Os dois esquemas foram
> percorridos e nenhum achado foi de tom.
>
> **Validação manual pelo operador: realizada em 09/09/2026.** É ela que fecha a
> convenção 14 — as rodadas dirigidas por CDP são apoio, não substituto: foram
> quatro delas que declararam convergência, e foi o olho humano que achou a
> largura errada (rodada 5).

- [x] 9.1 (`apps/frontend`) Subir o painel e percorrer, **nos dois esquemas de
      cor**, contra o protótipo anexado em `design/`: aba com bases vinculadas,
      aba vazia, aviso de base inativa, modal cheio, modal com busca sem
      resultado, modal com catálogo vazio, barra de salvamento visível, e o
      modal de alterações não salvas.
- [x] 9.2 (`apps/frontend`) **Repetir até uma rodada não achar nada.** Cada
      correção muda o que fica visível; a conferência é iterativa por
      construção. Registrar cada rodada e o que ela achou.
- [x] 9.3 (`apps/frontend`) Onde a rodada achar divergência, decidir e
      registrar no `design.md` com o número da convenção que sustenta a
      decisão — contrariar o protótipo é resultado legítimo (convenção 17).
      Onde a decisão mudar o que já está escrito, corrigir a decisão original,
      não anexar nota (convenção 9).

## 10. Fechamento

> **10.1 — suíte completa, 617/617 verde, 69 arquivos** (baseline 66 / 574),
> paralelismo padrão, o mesmo da baseline. Limiar de 1.1 conferido antes:
> VM do Podman ociosa (7,8%), load de 1 min 4,76 na checagem — subiu para 5,97
> no instante do início, marginalmente acima do limiar declarado, e ainda assim
> verde. Registrado por honestidade: o limiar de 5,0 é conservador, e este é o
> primeiro dado de que a faixa 5-6 também passa.

- [x] 10.1 (`apps/frontend`) Rodar a suíte inteira **no mesmo paralelismo fixado
      em 1.1** e comparar com a baseline. Rodar num paralelismo diferente do da
      baseline invalida a comparação: ela passa a ser entre duas configurações,
      não entre dois commits.
- [x] 10.2 (`apps/frontend`) `npm run lint` e `tsc` limpos.
- [x] 10.3 Medir o entregue **decomposto** — criados e modificados separados,
      arquivos e linhas, só código, sem os artefatos `openspec/` — e comparar
      com a projeção do `design.md` (14 arquivos / ~1145 linhas; 6 criados /
      ~920, 8 modificados / ~225). **Quinta medição da convenção 18.**
      Comparar só o escopo que estava projetado: escopo acrescentado durante a
      conferência manual entra na conta do entregue, não na do erro de método.

## 11. Registros em `02-HISTORICO_E_STATUS.md` (um por item)

Quatro tarefas separadas de propósito. Registro composto perde item: foi o que
aconteceu com o número "doze" na 5a-1, corrigido em três arquivos e esquecido no
quarto. Cada uma abaixo é verificável sozinha.

- [x] 11.1 Registrar a **quinta medição da convenção 18** — a tabela de 10.3,
      projetado × entregue, criados e modificados separados, com a leitura
      causal de cada desvio (não só a direção).
- [x] 11.2 Registrar o **resultado da conferência manual**: quantas rodadas, o
      que cada uma achou, e o custo. Se alguma rodada mudou escopo, dizer o que
      entrou e por quê — é isso que separa erro de projeção de escopo novo.
- [x] 11.3 Registrar o **handoff que sobra** para as etapas 2, 5a-2 e 5c: o que
      esta change não pôde implementar e vira requisito lá, na mesma forma do
      handoff que a 5a-1 deixou.
- [x] 11.4 Registrar a **correção do gatilho do carve de ordenação** (D10) no
      item aberto correspondente: a premissa "renderiza as três listas lado a
      lado" é falsa (abas com `keepMounted={false}`; esta aba usa
      `agent.knowledgeBases`, que já tem `ThenBy(Id)`), o gatilho reescrito, e o
      **quinto site** achado — `ListKnowledgeBasesQueryHandler` ordena por
      `CreatedAt`, sem desempate, e o modal desta change passa a renderizá-lo.
