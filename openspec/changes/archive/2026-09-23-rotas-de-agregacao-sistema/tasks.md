## 1. Fuso em `apps/api` — o pré-requisito, e a primeira decisão a fechar

- [x] 1.1 (`apps/api`) Registrar `TimeProvider.System` no `Program.cs`, com o
      comentário apontando o gêmeo de `apps/workers/Program.cs:38` e a razão de
      não resolver a janela com `now()` no SQL (D3).
- [x] 1.2 (`apps/api`) Opções da agregação: o nome IANA do fuso, lido de `TZ`
      **como configuração**, nunca de `TimeZoneInfo.Local` nem de
      `CultureInfo.CurrentCulture` (D1).
- [x] 1.3 (`apps/api`) Checagem de integridade no startup, cópia da de
      `apps/workers`: comparar o `Id` do fuso da instância **registrada** de
      `TimeProvider` contra o valor declarado em `TZ`, lançar
      `InvalidOperationException`, sem bypass. Comentário de cada cópia apontando
      a outra, e dizendo que é cópia porque os apps não se referenciam (D2).
- [x] 1.4 (`apps/api`) Chamar a checagem no `Program.cs` depois do `Build()`, e
      registrar a linha de boot com fuso resolvido e offset.
- [x] 1.5 (infra) `docker-compose.prod.yml`: entregar `TZ` ao serviço `api` com
      `:?`, no mesmo formato já usado por `workers` (`:139`). Acompanhar em
      `docker-compose.yml` e `.env.example`.
- [x] 1.6 (`apps/api`) Acrescentar `Microsoft.Extensions.TimeProvider.Testing` ao
      `.csproj` de teste — **não** é referenciado hoje; é o custo nomeado da D3.
- [x] 1.7 **Guardas do fuso**, com o par: nome válido sobe e se anuncia; nome que
      não resolve reprova o boot; nome com prefixo POSIX `:` reprova; `TZ`
      ausente reprova. E o guarda que a lacuna de `apps/workers` não tem: a
      checagem é exercitada pela `WebApplicationFactory`, sobre a composição real
      do `Program.cs`, de modo que **remover a chamada reprove**.

## 2. A janela — parse, validação e o guarda que só agora é alcançável

- [x] 2.1 (`apps/api`) Interpretação dos limites: `from`/`to` como `string?` na
      assinatura, ISO 8601, `AssumeUniversal | AdjustToUniversal`, com o
      comentário dos **dois papéis distintos** dos flags (D6).
- [x] 2.2 (`apps/api`) Validação: os dois obrigatórios; problemas acumulados numa
      **única** resposta; mesmo formato de corpo para ausente e malformado;
      inversão checada **só depois** de os dois parsearem; limites inclusivos.
- [x] 2.3 **Guarda do `AdjustToUniversal`**: limite com deslocamento ≠ 0 responde
      `200`, não `500`. Verificar que ele **reprova** com o flag removido — antes
      da tarefa 1.5 esse guarda ficaria verde nos dois casos, e é exatamente a
      quinta forma da convenção 15.
- [x] 2.4 **Guardas da janela**, com o par: ausente + malformado numa resposta só;
      invertido recusado sob a chave do limite final; limite coincidente incluído
      nos dois lados.

## 3. A rota e o agregado

- [x] 3.1 (`apps/api`) `GET /insights/system`, no molde de
      `GetKnowledgeBaseIndexingSummary`: endpoint fino + query + handler de
      Mediator + `AsNoTracking()`. **Não** acrescentar à allowlist de rotas
      anônimas — entrar lá reprova o boot (D14).
- [x] 3.2 (`apps/api`) DTO do agregado, em objeto e nunca linha bruta, com forma
      que **não aceite** linha bruta por descuido de mapeamento (D5).
- [x] 3.3 (`apps/api`) Consultas das métricas com fonte direta — as 22 do mapa —,
      toda agregação **no banco**, com o balde em `AT TIME ZONE` sobre as linhas
      já restritas pela janela (D7).
- [x] 3.4 (`apps/api`) Os três joins ao pai, porque `provider_calls`,
      `embedding_calls` e `delegation_outcomes` **não têm coluna temporal
      própria** (D12) — inclusive os **dois caminhos** de M19, por `Purpose`.
- [x] 3.5 **Guarda do balde local**, e é o guarda central: instante noturno que
      cai em outro dia **e** outro dia da semana em UTC é contado no dia local, e
      o guarda **reprova contra balde em UTC**.
- [x] 3.6 **Guarda de nulo, negativo**: token não reportado chega ausente/nulo e
      a asserção afirma que **não é `0`**. Par positivo: período medido e vazio
      chega `0`.
- [x] 3.7 **Guarda de vacuidade**: todo cenário de agregação roda sobre banco
      **povoado**, e há asserção da precondição (contar linhas antes de asserir) —
      senão o guarda fica verde com e sem a implementação.
- [x] 3.8 **Guarda de autenticação**: sem token responde `401`, sem que nada
      tenha sido declarado para isso.

## 4. "Medindo desde" e a série

- [x] 4.1 (`apps/api`) Os instantes de regime como **configuração**, um por
      regime: execução **22/09/2026 01:21**, embedding **23/09/2026 01:18**,
      `America/Sao_Paulo` (D8). **Com comentário ao lado dos dois valores**
      dizendo que são os instantes do **piloto**, e que outro ambiente precisa dos
      seus — é o aviso que chega a quem copiar o arquivo, e é **mitigação parcial
      declarada como tal**: comentário não reprova nada. O modo de falha completo
      está no fim da D8, e o item de fila sai na 7.10.
- [x] 4.2 (`apps/api`) A resposta carrega um **mapa de regimes**, e cada grupo de
      métricas declara o seu — mapa, e não dois campos, porque a etapa 4 traz o
      terceiro.
- [x] 4.3 (`apps/api`) A série **omite** os dias anteriores ao início do regime;
      `0` só dentro dele.
- [x] 4.4 **Guardas**, com o par: os dois regimes chegam separados; início
      declarado prevalece sobre o primeiro dado (ociosidade após o deploy não
      encurta o regime); janela que começa antes do regime não produz `0` nos dias
      anteriores.
- [x] 4.5 Conferir os dois instantes configurados contra `min(StartedAt)` de
      `task_executions` e de `knowledge_indexing_attempts`, convertidos ao fuso —
      é **conferência**, não fonte (D8).

## 5. M32 — as duas populações

- [x] 5.1 (`apps/api`) A consulta das duas populações: execução aberta
      (`EndedAt` nulo) e task nunca consumida (sem linha em `task_executions`,
      existente só em `a2a_tasks`), reportadas de forma **distinguível**.
- [x] 5.2 (`apps/api`) O conjunto de estados não-terminais é **o mesmo** do
      detector (`{Submitted, Working}`), com o comentário dizendo que o protocolo
      tem cinco e por que aqui são dois (D9).
- [x] 5.3 **Guardas**: as duas populações aparecem separadas; task em estado
      terminal nunca é reportada, por mais antiga que seja.

## 6. As parcialidades, o detector e os itens com gatilho

- [x] 6.1 (`apps/api`) M27/M28/M29: a contagem derivada das tabelas de métrica
      **não** é apresentada como completa — a recusa de `apps/api` não tem linha
      de execução, e o **motivo não tem fonte nenhuma** (D11).
- [x] 6.2 (`apps/api`) M21/M22: carimbo de submissão nulo produz **ausente**,
      nunca `0`, e fica fora do cálculo de média/p95.
- [x] 6.3 (`apps/api`) M25: a métrica declara o que o resíduo inclui — o rótulo
      deixa de ser "tempo em tools".
- [x] 6.4 **Guardas**: recusa sem linha de execução não é silenciosamente
      omitida; carimbo ausente produz indefinido e não `0`.
- [x] 6.5 (`apps/workers`) Reescrever o comentário de
      `NonTerminalTaskDetectorService.cs:63-89` com a condição corrigida (D10).
      **Só o comentário** — nenhuma mudança de comportamento, nenhum teste novo.

## 7. Documentação e registro

- [x] 7.1 (raiz) `01`: seção "Fuso horário do sistema" ganha o lado de
      `apps/api`, ao lado do de `apps/workers`, com a decisão da D1 e o motivo de
      ela contrariar a letra da decisão anterior (convenção 9).
- [x] 7.2 (raiz) `02`: as **cinco parcialidades** do mapa das 27, com M29 em
      destaque — sem fonte, aprovada no protótipo, posição **antes da etapa 4**,
      change de coleta.
- [x] 7.3 (raiz) `02`: a condição de remoção do detector **reescrita**, dizendo
      que a cláusula (a) testava a coisa errada e por quê (série × consulta;
      `a2a_tasks` não guarda histórico).
- [x] 7.4 (raiz) `02`: itens com gatilho — o composto
      `a2a_tasks(state, status_timestamp)` e os quatro sugeridos pela forma
      (`embedding_calls(Purpose)`, `(KnowledgeBaseId)`,
      `task_executions(TerminalState)`, `(EndedAt)`); o teto de janela; M32 aos
      cinco estados. Cada um com gatilho **e** posição.
- [x] 7.5 (raiz) `CHANGELOG.md`.
- [x] 7.6 (raiz) `02`: registrar que os protótipos **não foram abertos** nesta
      change e que o contrato veio do registro — com o gatilho (`/design-login`) e
      a regra de que o protótipo vence em caso de divergência.
- [x] 7.7 (raiz) `02`: registrar o **Escopo 2**, que tem **duas** alterações.
      **(a)** A instrução: o texto exato (*"Sempre utilize a ferramenta
      codegraph"*), **quando entrou** (23/09/2026, à mão, pelo dono), onde vive
      (bloco `context:` de `openspec/config.yaml`, injetado nas instruções de
      todos os artefatos) e que vale **a partir daqui para toda change**. Sem
      isso, a próxima sessão acha a instrução sem saber desde quando vale nem por
      quê — a mesma forma de decisão tomada fora do repositório que esta linha já
      registrou três vezes. **(b)** A remoção da prosa solta, com o **resultado da
      conferência da 7.9** — se a linha estava ou não sendo injetada —, porque é
      isso que decide se a remoção foi higiene ou correção de ruído no prompt.
- [x] 7.8 (raiz) `02`, junto da 7.7: o **pré-requisito declarado** — `.codegraph/`
      é integralmente ignorado por git e a CLI vive fora do repositório, logo nem
      o índice nem a ferramenta acompanham um clone, e em máquina nova ou em CI a
      instrução é inexecutável até alguém instalar e indexar. **Verificado nesta
      change:** CLI `1.5.0`, índice presente, `codegraph explore` respondendo.
      *Gatilho:* o primeiro clone novo, ou o primeiro colaborador. *Posição:* item
      próprio — versionar índice ou acrescentar passo de bootstrap é decisão que
      não cabe nesta change.
- [x] 7.9 (`openspec/config.yaml`) **Remover a prosa solta da primeira linha** —
      `Crie (ou edite, se já existir) o arquivo openspec/config.yaml com o
      seguinte conteúdo:`. É resto de instrução de criação que sobreviveu porque
      o formato tolera: a mesma família de texto que parece dado e não é. Remove-se
      **agora**, e não vira item, porque o arquivo já é o assunto declarado do
      Escopo 2 e a alternativa é ela sobreviver indefinidamente — nunca há motivo
      para abrir aquele arquivo.

      **A conferência que decide o que o registro afirma já foi feita, e o
      resultado é "não injetada".** Verificado em `proposal`, `design`, `specs` e
      `tasks`: o texto não aparece em **campo nenhum** do JSON devolvido por
      `openspec instructions` — nem em `context`, nem em `rules`, nem em
      `instruction`. O `openspec` lê `schema`, `context` e `rules` e **ignora** a
      chave de topo estranha; `openspec validate --all` passa com ela presente.
      **Logo a remoção é higiene, sem efeito observável no prompt de artefato
      nenhum** — e é isso que a 7.7(b) registra.

      **Guarda da tarefa:** depois de remover, reconferir que
      `openspec instructions` continua devolvendo `context` e `rules` íntegros —
      inclusive a linha do `codegraph`, que fica — e `openspec validate --all`
      verde. A remoção não pode levar junto o conteúdo que o arquivo de fato tem.
- [x] 7.10 (raiz) `02`: registrar o **modo de falha dos instantes de regime**
      (fim da D8) como item de fila. **O que falha:** instante de regime é valor de
      **um** ambiente — o piloto — num arquivo que vale para todos; ambiente cujo
      início real seja posterior ao declarado recebe `0` onde deveria receber
      ausência, afirmando medido onde não houve medição. **Por que é silencioso:**
      nada reprova, nada loga, o número sai plausível e a tela o mostra como
      período medido e vazio. **Por que não saiu aqui:** a saída é checagem de
      boot comparando o declarado com o menor carimbo — tem molde na casa (a
      checagem de fuso) —, mas é escopo não previsto, e decidi-lo no apply seria
      desenhar no meio da implementação. **Gatilho:** o primeiro ambiente novo a
      subir a rota, **dev incluído**. **Posição:** item próprio, decidindo então
      entre checagem de boot, valor por ambiente, ou derivar com salvaguarda.
      Registrar junto que a **4.5 não cobre** isto — ela é conferência única,
      desta change, numa máquina.

## 8. Verificação e fechamento

- [x] 8.1 **Remedir as três baselines** antes de qualquer comparação, com
      `podman ps` em zero, e registrar **qual alvo rodou antes** — o flake de
      `apps/inbox` reprova sob contenção e passa isolado. Não herdar 386/351/203.
- [x] 8.2 Rodar as três suítes e registrar o resultado com o regime colado.
- [x] 8.3 **Conferir a assinatura compilando todos os `.csproj`** — não há `.sln`
      na raiz, e compilar só um projeto não pega quebra de assinatura.
- [x] 8.4 **Fechamento da convenção 18 (décima quarta medição).** Comparar contra
      a projeção do `design.md`, **sem inventar correção**: **9 criados, 11
      modificados, ~1.990 linhas, 0 geradas, 34 casos** — todos do **Escopo 1**.
      Usar `git diff -w` para modificado, contar o duplo **por natureza e não por
      arquivo**, e conferir se o item da décima quarta — **projetar guarda mais
      par** — fez o erro à mão cair. `openspec/config.yaml` **não entra na
      conta** (Escopo 2, trabalho não feito por esta change) e é declarado ao
      lado. Registrar no `02`.
- [x] 8.5 **Conferência de escopo de arquivo contra a lista fechada abaixo**, nos
      **dois** escopos. Arquivo tocado fora da lista é achado a registrar, não a
      absorver em silêncio — e `openspec/config.yaml` deve aparecer **alterado e
      declarado**, nunca como achado de origem desconhecida.

### A lista fechada de arquivos

**`apps/api` — criados (9):**

```
src/Buteco.Api/Insights/Endpoints/InsightsEndpoints.cs
src/Buteco.Api/Insights/Queries/GetSystemInsights/GetSystemInsightsQuery.cs
src/Buteco.Api/Insights/Queries/GetSystemInsights/GetSystemInsightsQueryHandler.cs
src/Buteco.Api/Insights/Responses/SystemInsightsResponse.cs
src/Buteco.Api/Insights/InsightsPeriod.cs
src/Buteco.Api/Options/MetricsOptions.cs
src/Buteco.Api/Infrastructure/TimeZoneStartupValidation.cs
tests/Buteco.Api.Tests/InsightsEndpointsTests.cs
tests/Buteco.Api.Tests/TimeZoneStartupValidationTests.cs
```

**`apps/api` — modificados (4):**

```
src/Buteco.Api/Program.cs
src/Buteco.Api/appsettings.Development.json
src/Buteco.Api/appsettings.json
tests/Buteco.Api.Tests/Buteco.Api.Tests.csproj
```

**`apps/workers` — modificados (1):**

```
src/Buteco.Workers/Diagnostics/NonTerminalTaskDetectorService.cs   (só comentário)
```

**Infra e documentação — modificados (7):**

```
docker-compose.yml
docker-compose.prod.yml
.env.example
01-ARQUITETURA_E_CONVENCOES.md
02-HISTORICO_E_STATUS.md
CHANGELOG.md
openspec/config.yaml                               (Escopo 2 — DUAS alterações)
```

**Nota sobre `openspec/config.yaml`:** o arquivo é o **Escopo 2** inteiro, e
carrega **duas alterações de origens diferentes** — a conferência da 8.5 deve
encontrar **as duas**, e não tratar a segunda como alteração não declarada:

| | alteração | origem | quem faz |
|---|---|---|---|
| a | linha *"Sempre utilize a ferramenta codegraph"* acrescentada ao bloco `context:` | decisão do dono, feita **à mão antes** desta change | já está na árvore |
| b | remoção da prosa solta da primeira linha do arquivo | defeito **pré-existente** (está em `HEAD`) | **esta change**, tarefa 7.9 |

A **(a)** não recebe tarefa de implementação, só de registro (7.7). A **(b)** é a
única coisa que esta change **faz** no Escopo 2.

**Fora da lista, e por quê:** nenhuma migração (a change é somente leitura);
nenhum arquivo de `apps/frontend` (etapas 4 e 5); nenhum arquivo de `apps/inbox`;
`PostgresTaskStore.cs` **não** é tocado (o defeito de `ListTasksAsync` não tem
consumidor nesta etapa).
