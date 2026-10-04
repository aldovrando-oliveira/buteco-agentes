# Tarefas — cadastro de base sincronizada com seletor de pasta (#106)

**As tarefas de código e verificação rodam em `apps/frontend`.** Nenhuma toca
`apps/api`, `apps/connectors`, `apps/workers`, `apps/inbox`,
`docker-compose.prod.yml`, `.env.prod.example`, o nginx do stack nem
`docs/deployment.md` (#119). As do grupo 9 rodam na raiz do repositório e não
pertencem a app nenhum. Se a tela precisar de algo que o backend não oferece,
PARAR e reportar.

**Worktree:** `/Users/aldovrando/Projetos/buteco-agents-106`, branch
`feat/106-cadastro-base-sincronizada`, criada sem upstream de `origin/main`
(`ffc6a7b`) em 03/10/2026. Confirmar `pwd` e `git branch --show-current` antes de
cada grupo.

**Trabalho em paralelo:** a #138 e a #47 correm em outros worktrees e podem
editar os mesmos `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md`,
`CHANGELOG.md` e `docs/`. **Esses arquivos podem conflitar no merge.** Edições
curtas e localizadas: acrescentar seções, nunca reescrever trechos. Três suítes
podem rodar ao mesmo tempo: conferir o `load average` antes de classificar
qualquer falha de teste, e com a máquina carregada esperar e refazer.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/frontend`) Rodar a baseline neste worktree antes de qualquer
      edição de código: `npm run lint`, `npm run format:check`, `npm run test`,
      `npm run build`, com o `load average` anotado. Registrar aqui o resultado
      de cada um e, para o `format:check`, a lista de arquivos que já reprovam —
      só esses contam como pré-existentes.
      Feito em 03/10/2026, em `ffc6a7b` sem nenhuma edição de código, depois de
      `npm ci` neste worktree; 12 núcleos.
      - `npm run lint`: **verde** (saída 0). Load 11,5.
      - `npm run format:check`: **reprova** (saída 1) em **52 arquivos**, a
        lista abaixo. Dois são desta feature e esta change edita um deles
        (`KnowledgeBaseEditPage.test.tsx`, tarefa 7.3): ele já reprovava.
      - `npm run test`: **verde** — 118 arquivos, **1444/1444**, 166,41s. Load
        18,7 no início e **71,3** no fim (as outras sessões subiram suítes no
        meio); passou mesmo assim.
      - `npm run build`: **verde**, só o aviso de chunk acima de 500 kB.

```
src/features/agents/pages/AgentDetailPage.test.tsx
src/features/insights/api/insightsApi.test.ts
src/features/insights/api/insightsApi.ts
src/features/insights/api/useAgentInsights.test.tsx
src/features/insights/components/AgentConsumptionCard.test.tsx
src/features/insights/components/AgentConsumptionCard.tsx
src/features/insights/components/AgentDelegationCard.test.tsx
src/features/insights/components/AgentDelegationCard.tsx
src/features/insights/components/AgentFailuresCard.test.tsx
src/features/insights/components/AgentFailuresCard.tsx
src/features/insights/components/AgentInsightsTab.test.tsx
src/features/insights/components/AgentInsightsTab.tsx
src/features/insights/components/AgentKpiGrid.test.tsx
src/features/insights/components/AgentKpiGrid.tsx
src/features/insights/components/AgentModelsCard.test.tsx
src/features/insights/components/ConversationModelsCard.test.tsx
src/features/insights/components/ConversationModelsCard.tsx
src/features/insights/components/DailyTasksCard.test.tsx
src/features/insights/components/DailyTasksCard.tsx
src/features/insights/components/DeclaredGap.test.tsx
src/features/insights/components/FailureReasonsCard.test.tsx
src/features/insights/components/FailureReasonsCard.tsx
src/features/insights/components/FailuresCard.test.tsx
src/features/insights/components/InsightsKpiGrid.test.tsx
src/features/insights/components/MetricValue.test.tsx
src/features/insights/components/NonTerminalBanner.test.tsx
src/features/insights/components/PartialMeasurementNotice.test.tsx
src/features/insights/components/PeriodHeatmapCard.tsx
src/features/insights/components/PeriodPicker.tsx
src/features/insights/components/ProviderConsumptionCard.tsx
src/features/insights/components/TaskDurationCard.test.tsx
src/features/insights/components/TaskDurationCard.tsx
src/features/insights/components/TimeBreakdownCard.test.tsx
src/features/insights/components/TimeBreakdownCard.tsx
src/features/insights/components/WeekdayActivityCard.test.tsx
src/features/insights/components/WeekdayActivityCard.tsx
src/features/insights/pages/SystemInsightsPage.test.tsx
src/features/insights/pages/SystemInsightsPage.tsx
src/features/insights/test/agentInsightsFixture.ts
src/features/insights/test/systemInsightsFixture.ts
src/features/insights/utils/caveatLabels.test.ts
src/features/insights/utils/caveatLabels.ts
src/features/insights/utils/delegationOutcomeLabels.test.ts
src/features/insights/utils/delegationOutcomeLabels.ts
src/features/insights/utils/delegationRows.test.ts
src/features/insights/utils/failurePhaseLabels.test.ts
src/features/insights/utils/insightsWindow.test.ts
src/features/insights/utils/insightsWindow.ts
src/features/insights/utils/measuredDays.test.ts
src/features/knowledge-bases/pages/KnowledgeBaseEditPage.test.tsx
src/features/knowledge-bases/pages/KnowledgeBaseEditPage.tsx
src/features/mcp-servers/utils/agentUsage.ts
```

- [x] 1.2 (nenhum app, `Dockerfile` descartável em `/tmp`) Medir se um `ARG`
      declarado sem valor chega ao `RUN` como ausente ou como `""`, em `sh` e em
      Node, nos três builds (sem `--build-arg`, `X=`, `X=valor`).
      Feito em 03/10/2026, Podman 5.8.3, `node:24-alpine`, `--no-cache`:
      - sem `--build-arg`: `SH: AUSENTE` / `NODE: AUSENTE`;
      - `--build-arg X=`: `SH: DEFINIDA=[]` / `NODE: DEFINIDA=[]`;
      - `--build-arg X=valor`: `SH: DEFINIDA=[valor]` / `NODE: DEFINIDA=[valor]`.
      **A D1 se sustenta** (registrado lá). `Dockerfile` e as três imagens
      `argprobe-106:c1..c3` apagados; nenhuma camada intermediária sobrou.

## 2. Endereço do `apps/connectors` e tipos (`design.md`, D1, D8)

- [x] 2.1 (`apps/frontend`) `vite-env.d.ts`: `VITE_CONNECTORS_BASE_URL?: string`.
      `.env.example`: `VITE_CONNECTORS_BASE_URL=http://localhost:5037`, com o
      comentário de que ausente desliga a opção Sincronizada e vazio é caminho
      relativo.
- [x] 2.2 (`apps/frontend`) `Dockerfile`: `ARG VITE_CONNECTORS_BASE_URL`, sem
      `RUN test -n` e sem `ENV`, com o comentário do porquê (D1: `ENV` com o
      argumento ausente grava `""`, que é caminho relativo).
- [x] 2.3 (`apps/frontend`) `types/connectors.ts` (`ConnectorProvider`,
      `ConnectorFolder` com `kind: 'SharedDrive' | 'Folder'`) e
      `types/knowledgeBase.ts` (`contentMode`, `syncSource`,
      `KnowledgeBaseSyncSource`, input de criação como união). Sem `syncState`
      (convenção 25). Atualizar os testes que montam `KnowledgeBase` à mão.

## 3. Cliente do `apps/connectors` (`design.md`, D2)

- [x] 3.1 (`apps/frontend`) `api/connectorsApi.ts`: `connectorsBaseUrl()`,
      `request<T>` próprio com o token do operador, `ConnectorsError`
      (`kind`, `status`, `code`, `detail`), `listConnectorProviders()` e
      `listConnectorFolders(provider, parentId?)` com `parentId` e provedor
      escapados. `401` **não** limpa token nem redireciona. Comentário do módulo
      apontando a #107 (`requestKnowledgeBaseSync` entra aqui).
- [x] 3.2 (`apps/frontend`) `api/connectorsApi.test.ts`: URL montada com
      endereço absoluto e com `""` (relativo); `connectorsBaseUrl()` nulo sem a
      variável (`vi.stubEnv`); cabeçalho `Authorization`; falha de `fetch` vira
      `kind: "network"`; `ProblemDetails` com `code` e `detail` lidos; `401`
      mantém o token e não muda `window.location` (asserção negativa).
- [x] 3.3 (`apps/frontend`) `api/useConnectors.ts` e teste: chaves da D2,
      `retry: false`, `enabled` respeitado (nenhum `fetch` com `enabled: false`).

## 4. Textos de erro e pasta em uso (`design.md`, D3, D4)

- [x] 4.1 (`apps/frontend`) `utils/connectorErrors.ts`: a tabela da D3, com a
      variante de cadastro terminando em "Nenhuma base foi criada.".
- [x] 4.2 (`apps/frontend`) `utils/connectorErrors.test.ts`: um caso por linha
      da tabela; `access-denied` contém o e-mail; `folder-in-use` contém o nome
      da base e "inativa", e **não** casa `/exclu|remov|apag/i`; `rate-limited`
      contém "passageira" e não contém "acesso"; código desconhecido cita o
      código; nenhum texto usa o `title` recebido (asserção negativa com um
      `title` marcado).
- [x] 4.3 (`apps/frontend`) `utils/foldersInUse.ts` e teste: mapa a partir das
      bases com `syncSource`, ativas e inativas; `AbC` e `abc` são pastas
      diferentes; base manual não entra.
- [x] 4.4 (`apps/frontend`) Guarda verificado (convenção 15): reintroduzir de
      propósito a palavra "excluir" no texto de `folder-in-use` e a comparação
      sem caixa no mapa, ver os testes reprovarem, e desfazer. Registrar aqui.
      Feito em 03/10/2026, um por vez, com cópia do arquivo antes e restauração
      depois:
      - **"excluir" no `folder-in-use`:** a frase final virou "Para liberar a
        pasta, exclua a outra base." em `utils/connectorErrors.ts`. Reprovou
        **1 de 46**, exatamente
        `connectorErrors.test.ts > connectorErrorMessage — cadastro (apps/api) >
        folder-in-use nomeia a base, diz que segue ocupada inativa, e não manda
        excluir`, com `AssertionError: expected 'Esta pasta já é usada pela base
        “Polí…' not to match /exclu|remov|apag/i`.
      - **comparação sem caixa:** `folderId.toLowerCase()` na chave de
        `utils/foldersInUse.ts`. Reprovou **1 de 4**, exatamente
        `foldersInUse.test.ts > buildFoldersInUse > ids que diferem só na caixa
        são pastas diferentes, como no índice do apps/api`, com
        `AssertionError: expected 'Base AbC' to be undefined`.
      Depois de restaurar, `grep -c 'exclua'` e `grep -c 'toLowerCase'` nos dois
      arquivos deram **0**.

## 5. Card de Origem e formulário (`design.md`, D1, D8; spec `knowledge-base-catalog-ui`)

- [x] 5.1 (`apps/frontend`) `KnowledgeBaseForm`: props `origin` (depois do
      bloco da descrição) e `footerNote` (substitui a nota de documentos). Sem
      as duas, o formulário renderiza igual ao de hoje.
- [x] 5.2 (`apps/frontend`) `KnowledgeBaseOriginCard`: legenda "Origem dos
      documentos", aviso de que não muda depois, opções Manual e Sincronizada
      (grade de duas colunas, como as pranchas 2a/2b); Sincronizada desabilitada
      com a explicação quando o endereço é nulo; bloco sincronizado com provedor
      (carregando sem texto de erro, erro com "Tentar de novo", nenhum provedor
      como explicação), conta com `CopyButton` e instrução de Leitor, pasta
      (nenhuma escolhida / escolhida com "Abrir no Drive" em outra aba e "Trocar
      pasta"), a frase de que entram só Google Docs e `.md` da raiz, erro de
      campo da pasta e `Alert` de erro do cadastro.
- [x] 5.3 (`apps/frontend`) `KnowledgeBaseOriginCard.test.tsx` e
      `KnowledgeBaseForm.test.tsx`: cada estado acima; carregando **sem** texto de
      erro; `[]` de provedores sem texto de erro; link com `target="_blank"` e
      `rel` com `noopener`; formulário sem `origin` igual ao de hoje (nota de
      documentos presente, nenhum texto de origem).

## 6. Seletor de pasta (`design.md`, D4, D5; spec `knowledge-base-sync-source-ui`)

- [x] 6.1 (`apps/frontend`) `FolderPickerModal` apresentacional: caminho a
      partir de "Início" com volta por passo; grupos do nível de cima pelo
      `kind`; `Folder` com escolha e abrir, `SharedDrive` só abrir; pasta em uso
      com escolha desabilitada e "Já sincronizada pela base “<nome>”" sem link;
      contagem dentro de pasta; vazio por nível; carregando com `Loader` e
      "Carregando pastas…"; erro com texto da D3 e "Tentar de novo"; aviso de
      listagem de bases indisponível; rodapé com o e-mail, "Recarregar",
      "Os arquivos da raiz de “X” entram na base." e "Selecionar “X”"
      (desabilitado sem escolha).
- [x] 6.2 (`apps/frontend`) `FolderPickerModal.test.tsx`: cada item acima, e as
      negativas — carregando não contém "Não foi possível", "erro", "fora do ar",
      "sem acesso" nem "Nenhuma"; erro não mostra lista vazia; `SharedDrive` sem
      rádio; pasta em uso sem link e ainda abrível; listagem de bases
      indisponível não desabilita nenhuma pasta.

## 7. Página de criação (`design.md`, D3, D5, D8)

- [x] 7.1 (`apps/frontend`) `KnowledgeBaseCreatePage`: estado da origem, do
      provedor, da pasta e do caminho do seletor; provedores consultados só com
      Sincronizada marcada e endereço configurado; pastas só com o modal aberto;
      bases pela `useKnowledgeBasesQuery`; pasta obrigatória no cliente;
      corpo manual `{ name, description }` e sincronizado com `contentMode`,
      `provider` e `folderId`; erro sincronizado no card, `400` por campo,
      notificação genérica do manual como hoje; trocar de provedor limpa a pasta.
- [x] 7.2 (`apps/frontend`) `KnowledgeBaseCreatePage.test.tsx`, os aceites da
      issue, com asserção negativa:
      - sem `VITE_CONNECTORS_BASE_URL`: só Manual disponível, a explicação
        aparece, e **nenhum** `fetch` sai para o endereço do `apps/connectors`
        ao carregar e ao criar base manual;
      - com a variável e Manual marcada: nenhum `fetch` ao `apps/connectors`;
      - com a variável e Sincronizada: provedores listados, e-mail da conta,
        seletor abre e consulta sem `parentId`, descida com `parentId`;
      - cada código da D3, vindo da navegação e do cadastro, mostra o seu texto;
        `folder-in-use` não contém `exclu`, `remov` nem `apag`, e nome e
        descrição continuam preenchidos;
      - pasta em uso desabilitada no seletor, com o nome da base;
      - durante o carregamento do seletor e dos provedores, nenhum texto de erro;
      - `401` do `apps/connectors` não limpa o token nem navega para `/login`;
      - o cadastro Manual envia exatamente `{ name, description }` e leva ao
        detalhe, como hoje;
      - corpo sincronizado com exatamente as cinco chaves, sem `folderName` nem
        `folderUrl`;
      - nota de rodapé por origem, sem prazo na sincronizada.
- [x] 7.3 (`apps/frontend`) `KnowledgeBaseEditPage.test.tsx`: edição de base
      sincronizada sem card de Origem, e `PUT` só com `name` e `description`.

## 8. Verificação técnica e conferência visual (`design.md`, D7)

- [x] 8.1 (`apps/frontend`) `npm run lint`, `npm run format:check` (só os
      arquivos da baseline podem reprovar), `npm run test`, `npm run build`, com
      o `load average` anotado. Registrar os números medidos aqui, nunca de
      memória.
      Feito em 03/10/2026, depois da conferência visual (as correções dela
      entraram antes desta medição):
      - `npm run lint`: **verde**. Load 6,6.
      - `npm run format:check`: reprova nos **mesmos 52 arquivos da baseline**
        (`diff` vazio contra a lista da 1.1); nenhum arquivo desta change
        reprova além do `KnowledgeBaseEditPage.test.tsx`, que já reprovava.
      - `npm run test`: **verde** — 125 arquivos, **1580/1580**, 178,41s (baseline
        118 arquivos, 1444/1444). Load 7,5 no início e 42,0 no fim, por outras
        suítes na máquina; nenhuma falha a classificar. Os +136 são exatamente
        os testes novos: `connectorsApi` 12, `useConnectors` 5,
        `connectorErrors` 46, `foldersInUse` 4, `KnowledgeBaseOriginCard` 12,
        `FolderPickerModal` 15, `KnowledgeBaseForm` +6,
        `KnowledgeBaseCreatePage.sync` 35, `KnowledgeBaseEditPage` +1.
      - `npm run build`: **verde**, só o aviso de chunk acima de 500 kB.
- [x] 8.2 (`apps/frontend`) Build da imagem `apps/frontend/Dockerfile` com
      `VITE_API_BASE_URL=` e `VITE_INBOX_BASE_URL=` e **sem**
      `VITE_CONNECTORS_BASE_URL`: o build completa, o bundle não contém
      `localhost:5037`, e **a opção Sincronizada está desabilitada**: provar
      pelo bundle, identificando como o Vite compila o acesso à variável ausente
      (`void 0`, `undefined` ou equivalente) e afirmando que o bundle traz essa
      forma de "ausente" no ponto de leitura, e não `""`. Se der, servir a
      imagem numa porta da faixa 56100–56199 e confirmar na tela de
      `/knowledge-bases/new` por Chrome headless. Depois com
      `--build-arg VITE_CONNECTORS_BASE_URL=http://conectores.invalid`: o bundle
      contém o endereço. Podman nesta máquina; imagens e contêineres com `106`
      no nome, removidos ao fim só se foram criados aqui.
      Feito em 03/10/2026, Podman 5.8.3, contexto na raiz do worktree:
      - **sem a variável** (`buteco-frontend-106:sem-conectores`): build verde. No
        bundle extraído da imagem (`index-C73GMuZ2.js`), a leitura do endereço
        compila como `function UB(){return null}`: o Vite troca o acesso à
        variável ausente por `undefined`, e o minificador dobra o
        `undefined ?? null` em `null`. A forma de `""` seria
        ``function UB(){return``}`` e **não** aparece; `localhost:5037` também
        não. **Na tela:** a imagem servida na porta 56110 (contêiner
        `buteco-frontend-106-tela`), aberta por Chrome headless em
        `/knowledge-bases/new` com token gravado no `sessionStorage`, mostrou
        Sincronizada com `disabled`, Manual com `aria-checked="true"` e a
        explicação, e o log de rede do CDP teve **zero** requisições para
        `/connectors` (`design/capturas/imagem-sem-conectores_claro_1440.png`).
      - **com `VITE_CONNECTORS_BASE_URL=http://conectores.invalid`**
        (`buteco-frontend-106:com-conectores`): build verde, e a leitura compila
        como ``function UB(){return`http://conectores.invalid`}``, uma ocorrência
        no bundle.
      Contêineres de extração e o da tela removidos; as duas imagens são
      removidas no fim da conferência.
- [x] 8.3 (`apps/frontend`) Subir o painel para a conferência: portas da faixa
      56100–56199, cada uma conferida com `lsof -i :<porta>`; Vite com
      `VITE_API_BASE_URL` e `VITE_CONNECTORS_BASE_URL` em portas sem processo,
      e uma segunda instância sem `VITE_CONNECTORS_BASE_URL`; Chrome headless com
      depuração remota na mesma faixa. Registrar aqui as portas usadas. Não
      derrubar processo que não foi iniciado aqui (o `apps/api` da 5017 e os das
      outras sessões).
      Feito em 03/10/2026. Portas, todas conferidas livres com `lsof` antes:
      **56110** (nginx da imagem sem a variável, tarefa 8.2), **56111** (Vite com
      `VITE_API_BASE_URL=http://localhost:56114` e
      `VITE_CONNECTORS_BASE_URL=http://localhost:56115`), **56112** (Vite só com
      `VITE_API_BASE_URL`), **56113** (depuração remota do Chrome headless,
      perfil próprio no scratchpad); **56114** e **56115** sem processo nenhum,
      só como destino das respostas montadas. Os dois Vite dividiram o
      `node_modules/.vite` e o segundo reprovou uma vez na otimização de
      dependências (`ENOTEMPTY` no rename), refeita sozinha na sequência. No fim,
      os dois Vite e os Chrome foram encerrados (eram desta sessão) e as seis
      portas voltaram a livres. O `apps/api` da 5017 não foi tocado.
- [x] 8.4 (`apps/frontend`) Percorrer por CDP os estados da D7, com as respostas
      do `apps/api` e do `apps/connectors` montadas pelo `Fetch`, nos esquemas
      claro e escuro, a 1440px e a ~1860px. Na instância sem a variável,
      conferir no log do CDP que nenhuma requisição saiu para o
      `apps/connectors`.
      Feito: 14 estados da D7 × 2 esquemas × 2 larguras = **56 capturas** por
      rodada, com `Fetch.fulfillRequest` (preflight `OPTIONS` atendido pelo
      próprio `Fetch`, sem precisar de `--disable-web-security`),
      `Fetch.failRequest` para "fora do ar" e requisição pausada para
      "carregando". Nomes `<estado>_<esquema>_<largura>.png`.
      **Sem a variável, zero requisições ao `apps/connectors`** nas duas
      rodadas. Na rodada 1 o filtro do roteiro acusou 5, todas
      `http://127.0.0.1:56112/src/features/knowledge-bases/api/connectorsApi.ts`
      — o módulo servido pelo Vite, cuja URL contém "/connectors". Falso positivo
      do filtro, corrigido para "origem 56115 ou caminho começando por
      `/connectors/`"; a rodada 2 deu 0 com o filtro certo. O
      `medidas-r1.json` guarda a lista com o falso positivo, como saiu.
- [x] 8.5 (`apps/frontend`) Comparar dimensões contra as pranchas 2a–2d de
      `design/` (D7) e iterar até uma rodada sem achado; capturas e medidas em
      `design/capturas/`. Achado que contrarie o protótipo entra na tabela da
      D6 antes de ir para o `02`.
      Feito em **duas rodadas**; a segunda sem achado. Achados da rodada 1, todos
      corrigidos (tabela na D7 do `design.md`):
      - **R1-1** conteúdo do cartão Manual centralizado na vertical quando o
        vizinho tinha duas linhas (deslocamento de 25px contra 16px);
      - **R1-2** descrição das opções em 14px contra 13px da prancha;
      - **R1-3** modal com 543px de altura contra 640px das pranchas 2c/2d;
      - **R1-4** caixa da conta no tom da prévia da descrição, e não no da página
        como na prancha 2b;
      - **R1-5** a nota "Só aparece…" e "Recarregar" cortadas no fim da área
        rolável (correção de protótipo C10: passaram para uma faixa fixa);
      - **R1-6** linhas do seletor com 53px contra ~45px da prancha (botão de
        abrir de 28px).
      Medidas finais (rodada 2), iguais nos dois esquemas: card de Origem com a
      largura do bloco da descrição (1184px a 1440; 1604px a 1860); duas opções
      com vão de **12px**, borda de **2px** na marcada e 1px na outra, raio do
      tema (9px, o mesmo dos outros cards; a prancha usa 8px); conteúdo das
      opções a 15/16px do topo (o 1px é a borda de 2px, como na prancha); campo
      da pasta com 62px; modal de **720 × 639px**; linhas do seletor com **47px**;
      nenhuma rolagem horizontal. Capturas da rodada 2 e `medidas-r1.json`,
      `medidas-r2.json` em `design/capturas/`, mais a captura da imagem da
      tarefa 8.2.
- [x] 8.6 Validação manual pelo mantenedor (convenção 14). O archive espera
      esta validação.

      Feita pelo dono em 04/10/2026: "testes manuais realizados e fluxos
      funcionando corretamente", sem ajuste pedido. Ambiente da validação,
      tudo deste worktree: `apps/api` na 56123 (Postgres `buteco-106-pg` na
      56120 e RabbitMQ `buteco-106-rmq` na 56121/56122, próprios, com as
      migrations da branch), `apps/connectors` na 56124 com a chave da service
      account de validação (`google-drive` listado, 2 itens no nível de cima) e
      `Api__BaseUrl` ligada, e o painel (Vite) na 56125 com
      `VITE_CONNECTORS_BASE_URL=http://localhost:56124`. Sem `apps/workers` nem
      `apps/inbox`.
## 9. Registro

- [x] 9.1 (raiz, nenhum app) `docs/configuration.md`: `VITE_CONNECTORS_BASE_URL`
      na seção do `apps/frontend` como opcional, com os três estados da D1,
      acrescentada sem reescrever a tabela das obrigatórias.
      `docs/development.md`: a variável no `.env` de desenvolvimento e o build
      da imagem sem ela. Edições curtas (podem conflitar com a #138 e a #47).
      Feito: seção nova "`VITE_CONNECTORS_BASE_URL` (opcional)" em
      `docs/configuration.md`, depois da tabela do `apps/frontend`, sem tocar na
      tabela das obrigatórias; em `docs/development.md`, um parágrafo na seção
      do `apps/frontend` (o `.env`) e outro depois do parágrafo dos `VITE_*` no
      build da imagem.
- [x] 9.2 (raiz, nenhum app) Comentário na **#119**: o build do frontend no
      `docker-compose.prod.yml` precisa de `VITE_CONNECTORS_BASE_URL: ""` junto
      com o bloco `/connectors` do nginx, e não antes dele; sem a variável, o
      painel publica a opção Sincronizada indisponível. Registrar aqui o link.
      Feito na abertura da change, em 03/10/2026:
      <https://github.com/aldovrando-oliveira/buteco-agentes/issues/119#issuecomment-5975539376>.
      Se a revisão mudar a D1, o comentário é corrigido por outro.
- [x] 9.3 (raiz, nenhum app) `02-HISTORICO_E_STATUS.md`: entrada da change e as
      correções da D6 na lista viva "Correções de protótipo", como seção
      acrescentada (pode conflitar com a #138 e a #47).
      Feito: seção nova no fim do `02` e o bloco "Da décima sexta à vigésima
      quinta, da #106" depois da décima quinta. **Uma linha reescrita:** a
      contagem da lista viva ("São **quinze**…" virou "São **vinte e cinco**…"),
      porque sem ela a contagem ficaria falsa; é a linha mais provável de
      conflitar, e é de uma frase só.
- [x] 9.4 (raiz, nenhum app) `CHANGELOG.md`, seção `[Unreleased]`, entrada
      acrescentada (pode conflitar).
      Feito: uma entrada depois da do ciclo de sincronização (#105), no grupo
      "Bases de conhecimento".
- [x] 9.5 (raiz, nenhum app) Se a implementação divergir do `design.md`,
      corrigir o `design.md` com a causa real.
      Feito: D4 (listagem de bases só com o seletor aberto), D5 (faixa fixa do
      "Recarregar"), D6 (C10), D7 (resultado da conferência), D8 (`validateExtra`
      e os testes da página em dois arquivos), árvore de pastas, e a medição do
      `ARG` na D1.
- [x] 9.6 (raiz, nenhum app) `python3 scripts/check-docs.py` e
      `openspec validate frontend-cadastro-base-sincronizada --strict`.
      Feito: `Integridade da documentação: OK` e `Change
      'frontend-cadastro-base-sincronizada' is valid`.
