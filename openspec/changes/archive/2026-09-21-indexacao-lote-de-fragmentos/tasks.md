## 0. Conferência de escopo de arquivo

**Lista fechada de caminhos permitidos.** Qualquer arquivo fora desta lista que
apareça no `git status` ao fim é achado a reportar, não a commitar.

```
apps/workers/src/Buteco.Workers/Knowledge/Indexing/KnowledgeIndexingService.cs
apps/workers/src/Buteco.Workers/Knowledge/Indexing/EmbeddingBatchSizeValidation.cs   (novo)
apps/workers/src/Buteco.Workers/Options/EmbeddingOptions.cs
apps/workers/src/Buteco.Workers/Program.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/Support/KnowledgeIndexingHarness.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/KnowledgeIndexingTests.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/EmbeddingBatchSizeValidationTests.cs   (novo)
docker-compose.prod.yml
.env.prod.example
docs/configuration.md
02-HISTORICO_E_STATUS.md
CHANGELOG.md
openspec/changes/indexacao-lote-de-fragmentos/**
```

- [x] 0.1 Registrar a baseline de `apps/workers` **antes de tocar em qualquer
      arquivo**: `podman ps` tem que devolver **zero** (o stack de
      desenvolvimento compete por memória da VM do Podman), e a rodada tem que
      fechar abaixo de ~10 min — acima disso é contenção, não medição. Esperado
      **268/268**, com 14 classes no `WorkerHostCollection`. Anotar contagem **e**
      duração; comparação de suíte é por **nome de teste**, nunca só por número.
- [x] 0.2 Ao fim da change, conferir `git status` contra a lista acima.

## 1. Guardas primeiro, vermelhos contra `HEAD` (`apps/workers`)

A convenção 15 exige ver reprovar **antes** da correção, e no **componente que a
correção vai tocar** — aqui, `KnowledgeIndexingService`.

- [x] 1.1 Em `Knowledge/Support/KnowledgeIndexingHarness.cs` (`apps/workers`):
      acrescentar ao `FakeEmbeddingGenerator` a lista dos tamanhos de **cada**
      chamada (`BatchSizes`), **mantendo** `CallCount` e `LastBatchSize` —
      `KnowledgeIndexingTests.cs:75` usa o segundo, e removê-lo é blast radius
      desnecessário. Acrescentar `batchSize` ao `Build`, repassado em
      `EmbeddingOptions`.
- [x] 1.2 Em `Knowledge/Support/KnowledgeIndexingHarness.cs` (`apps/workers`):
      fixture de documento que produza um número **conhecido e pequeno** de
      fragmentos (≥ 3), para os cenários de lote usarem lote pequeno em vez de
      documento gigante — ver D8 do `design.md` para por que não 442.
- [x] 1.3 Em `Knowledge/KnowledgeIndexingTests.cs` (`apps/workers`): cenário
      **vermelho** — documento com mais fragmentos que o lote produz **mais de
      uma** chamada, com o tamanho de cada uma e a soma das entradas afirmados.
      Asserção sobre **número e tamanho das chamadas**, nunca sobre texto de
      mensagem de erro.
- [x] 1.4 Rodar 1.3 contra `HEAD` e **ver reprovar** (hoje: uma chamada só).
      Anotar a mensagem de reprovação — é o que prova o guarda.
- [x] 1.5 Em `Knowledge/KnowledgeIndexingTests.cs` (`apps/workers`): o **par** —
      renomear e recomentar `Embeddings_AreGeneratedInASingleBatch` (linha 65)
      para dizer o que ele passou a afirmar: documento **menor** que o lote
      produz **exatamente uma** chamada. Passa nos dois lados, e é a regressão
      contra a correção errada (lotear sempre).
- [x] 1.6 Em `Knowledge/KnowledgeIndexingTests.cs` (`apps/workers`): a
      **fronteira** — fragmentos em número **igual** ao lote produzem **uma**
      chamada (o off-by-one do `Chunk`).
- [x] 1.7 Em `Knowledge/KnowledgeIndexingTests.cs` (`apps/workers`): o par **"sem
      item"** da convenção 5 — documento com zero fragmentos não chama o gerador,
      **com asserção explícita da precondição** (o texto tem conteúdo; é a
      fragmentação que devolve vazio), para não reprovar por vacuidade.
- [x] 1.8 Em `Knowledge/KnowledgeIndexingTests.cs` (`apps/workers`): cenário de
      **alinhamento** — para um documento que atravessa mais de um lote, o vetor
      gravado de cada fragmento é o que o duplo produz para o **texto daquele
      fragmento**, incluindo o primeiro fragmento do segundo lote.

      **Por que é o guarda que mais importa, e o menos óbvio:** é o **único** modo
      de o loteamento corromper o índice em silêncio (D7). Vetor trocado entre
      fragmentos não falha nada — não há exceção, a contagem bate, o documento
      termina `Indexed`. O sintoma aparece meses depois, como resposta estranha do
      agente que ninguém liga a isto.

      **Três condições, sem as quais a asserção não discrimina:**

      - **Textos distinguíveis, e vetor derivado do texto.** O duplo produz o
        vetor a partir da entrada, de forma que fragmentos diferentes produzam
        vetores **diferentes e comparáveis**. Um duplo que devolva o mesmo vetor
        para tudo — ou uma fixture cujos fragmentos tenham texto repetido — deixa
        o guarda verde com e sem a correção. É a mesma vacuidade que a convenção 5
        pega no par "sem item", e ela aqui entra pela fixture, não pelo duplo.
      - **A troca a pegar é na fronteira de lote**: o **último fragmento do lote 1
        contra o primeiro do lote 2**. É ali que o `Chunk` e a concatenação podem
        desalinhar, e é o par que um off-by-one produz. O cenário nomeia esses dois
        fragmentos explicitamente.
      - **Asserção por fragmento, nunca agregada.** Conferir que o **conjunto** de
        vetores gravados é o conjunto esperado passa com dois trocados entre si.
        A asserção casa `ordinal → vetor` um a um.

      Comentário no arquivo de teste dizendo **por que este guarda existe** — que é
      o único caminho de corrupção silenciosa do loteamento. Registro **barato**
      pela classificação do `design.md`: a evidência é o próprio mecanismo, não um
      contrafactual a reconstruir.
- [x] 1.9 Em `Knowledge/KnowledgeIndexingTests.cs` (`apps/workers`): lote que
      devolve número de vetores diferente do enviado falha a indexação sem gravar
      fragmento.

## 2. Loteamento (`apps/workers`)

- [x] 2.1 Em `Options/EmbeddingOptions.cs` (`apps/workers`): propriedade
      `BatchSize`, `int`, padrão **250**. O XML doc diz **exatamente** o que a
      medição sustenta — *250 porque 267 passou e 442 falhou, com margem; não há
      teto medido* — e **não** diz "limite seguro" (convenção 13). Colar o regime
      da medida (piloto 20/09/2026, gateway do `.env.prod`, modelo de 4.096
      dimensões) e o **gatilho de recalibração** (convenção 22).
- [x] 2.2 Em `Knowledge/Indexing/KnowledgeIndexingService.cs` (`apps/workers`):
      substituir a chamada única da linha 73 por um laço **sequencial** sobre
      `fragments.Chunk(batchSize)`, acumulando os vetores na ordem. Nenhum tipo
      novo, nada extraído (convenção 2 — D6).
- [x] 2.3 Em `Knowledge/Indexing/KnowledgeIndexingService.cs` (`apps/workers`):
      mover a conferência de contagem para **por lote**, nomeando o lote na
      mensagem. A conferência de **dimensão** fica onde está, no laço de gravação
      — é o único lugar onde a dimensão real é conhecida.
- [x] 2.4 Em `Knowledge/Indexing/KnowledgeIndexingService.cs` (`apps/workers`):
      **não** tocar `CommitAsync`. A substituição integral em transação única,
      condicionada a `ContentRevision`, é a garantia 2 de D9 da etapa 1 e tem
      cenário de spec (D3).
- [x] 2.5 Registros de mecanismo no comentário, classificados como o `design.md`
      projetou: **caros** (250 é provisório; por que a retentativa continua por
      documento, citando D3 da `knowledge-base-indexacao` e o XML doc de
      `MaxAttempts`) e **baratos** (por que sequencial, com a medida de 4,4× e o
      regime colado; por que só o gateway é loteado).
- [x] 2.6 Rodar 1.3 a 1.9 e ver **passar**. Conferir que o par de 1.5 continua
      passando — se ele reprovar, a correção loteou sempre, que é o defeito que
      ele existe para pegar.

## 3. Lote inválido reprova o boot (`apps/workers`)

- [x] 3.1 Criar `Knowledge/Indexing/EmbeddingBatchSizeValidation.cs`
      (`apps/workers`): `host.ValidateEmbeddingBatchSize()` no molde da convenção
      8 e no idioma das três checagens existentes — `BatchSize <= 0` lança
      nomeando o valor encontrado e o esperado, em texto de operador. **Sem
      clamp** (D5).
- [x] 3.2 Em `Program.cs` (`apps/workers`): chamar `host.ValidateEmbeddingBatchSize()`
      junto das outras duas, com o comentário dizendo que **nenhum teste desta
      suíte prova este registro** — o mesmo que já está escrito para o
      `NonTerminalTaskDetectorService`.
- [x] 3.3 Criar `Knowledge/EmbeddingBatchSizeValidationTests.cs` (`apps/workers`):
      unitário puro, **sem `WorkerInfrastructureFixture` e sem
      `[Collection(WorkerHostCollection.Name)]`**. Dois cenários: inválido reprova
      (com o valor na mensagem), válido sobe. Se este arquivo acabar precisando da
      fixture, **parar** — seria a 15ª classe da coleção e obrigaria a recalibrar
      a referência de duração (convenção 22).
- [x] 3.4 Reintroduzir o defeito de propósito (`BatchSize = 0` passando) e ver
      3.3 reprovar — convenção 15.

## 4. Configuração de stack e documentação

- [x] 4.1 `docker-compose.prod.yml`: `Embedding__BatchSize:
      ${EMBEDDING_BATCH_SIZE:-250}` no serviço de `apps/workers`. **Com default,
      não obrigatória** — diferente de `EMBEDDING_MODEL`/`EMBEDDING_DIMENSIONS`,
      que usam `:?` porque não têm default seguro.
- [x] 4.2 `.env.prod.example`: `EMBEDDING_BATCH_SIZE=250` no bloco de embedding,
      com o comentário dizendo que é **provisório** e que baixá-lo é o modo de
      descobrir o teto do gateway.
- [x] 4.3 `docs/configuration.md`: a linha em ambas as tabelas — a de
      `Embedding__*` de `apps/workers` (perto da linha 151) e a de mapeamento de
      variáveis do compose (perto da linha 247). Deixar explícito que é a única
      das quatro com default.

## 5. Registros no `02-HISTORICO_E_STATUS.md`

- [x] 5.1 Seção nova em "Changes aplicadas, por linha de trabalho" para esta
      change, com as **cinco medidas do gateway** e o regime colado (piloto,
      20/09/2026, gateway de embedding do `.env.prod`, modelo de 4.096
      dimensões). São a linha de base de qualquer ajuste futuro do parâmetro, e o
      par **4,4×** do `01` é o que caracteriza o upstream como instável. Registrar
      também a comparação da projeção do `design.md` (convenção 18) — o
      fechamento apenas compara.

      **E o registro diz as duas coisas SEPARADAMENTE, em frases próprias:**

      - **A change está aplicada** — o lote existe, o parâmetro existe, o boot
        reprova valor inválido, os guardas passam.
      - **O defeito NÃO está verificado como resolvido** contra o gateway real. O
        que se sabe é que **267 passou uma vez**; 250 tem margem sobre isso e
        **nenhuma medição o otimizou**. O teto do gateway continua sem formato
        estabelecido.

      Sem essa separação escrita, quem ler o `02` daqui a um mês conclui que o
      `502 upstream_error` foi resolvido em 20/09 — e a primeira falha nova parece
      **regressão** em vez de **continuação**. É a forma da convenção 13 aplicada
      ao registro: não afirmar mais do que o sistema sabe.
- [x] 5.2 Em "Itens em aberto", item **Limite de documentos indexados
      simultaneamente**, com o que a análise já concluiu e que se perde se não for
      escrito: (a) `prefetchCount` não serve como ponto de controle em k8s, porque
      o paralelismo vira consequência do autoscaler, que decide por profundidade
      de fila — e a fila de indexação alimenta essa decisão; (b) separar workers de
      embedding de workers de conversa **agrava**, porque com pods próprios o
      autoscaler vê 100 mensagens de indexação como sinal puro e escala para
      atendê-las, enquanto hoje, misturado, a pressão é diluída na métrica de
      conversas; (c) o caminho viável é **limitar o que entra na fila**, mantendo
      o pendente no banco, o que muda o significado da fila de *trabalho pendente*
      para *trabalho liberado* e exige decidir **quem libera o próximo** — o
      worker ao concluir (barato, mas a cadeia para em silêncio se o pod morre no
      meio, mesma forma do `PendingDispatch` órfão em `Dispatching`) ou uma
      varredura periódica (recuperável por construção; molde do
      `DebounceSweepService` e do `NonTerminalTaskDetector`); (d) o teto é
      **global**, então a contagem é global — `count(*)` sobre documentos em
      indexação no Postgres compartilhado —, com a disputa entre pods resolvida
      como o inbox resolve, por `xmin`; (e) efeito de segunda ordem, **separado**:
      hoje indexação e conversa compartilham pods, então um lote grande de
      documentos rouba capacidade de atendimento — problema real, e não é o que
      esta linha resolve.
      **Gatilho:** o primeiro caso real de muitos documentos submetidos de uma vez,
      ou qualquer evidência de indexação degradando atendimento.
      **Posição:** change própria, **depois da `metricas-execucao-coleta`** — sem
      medição de embedding, o teto seria escolhido por palpite, que é o defeito
      que esta change está evitando no tamanho do lote.
- [x] 5.3 Em "Itens em aberto", item **texto de falha da tela de documentos**. O
      texto atual (`KnowledgeIndexingFailure.cs:52-53`) é *"A indexação falhou por
      um erro interno e as tentativas se esgotaram. Reindexe o documento; se
      persistir, é caso de suporte técnico."* Para a causa medida, reindexar é a
      **quarta** tentativa — as três automáticas já aconteceram, e o texto não diz
      isso. É a segunda ocorrência da família que a `frontend-mensagem-recusa-ciclo`
      corrigiu na tela de delegações: mensagem que instrui a repetir sem dizer o
      que já foi repetido.
      **Gatilho:** cumprido — é esta medição.
      **Posição:** change de `apps/frontend`, **depois desta**; se o lote resolver
      o caso em produção, a urgência cai mas o texto continua impreciso.
- [x] 5.4 Em "Itens em aberto", item **verificação de campo do tamanho do lote**
      — é a verificação que a suíte não faz, e ela **não é tarefa desta change**:
      só existe depois do deploy, que está na posição 5 da fila e ainda não
      aconteceu. Mesmo tratamento que a `delegacao-diagnostico` deu à inspeção de
      dado da janela do deploy.
      O que fica escrito: reindexar o `02` contra o gateway real com o lote em
      vigor; se falhar, **baixar** `EMBEDDING_BATCH_SIZE` e repetir, registrando
      **cada passo** no `02` com o regime colado (convenção 22) — é assim que o
      formato do teto vai ser estabelecido. Dizer também o que a change **não**
      prova: que 250 resolve.
      **Gatilho:** o primeiro deploy com esta change aplicada.
      **Posição:** a janela do deploy da posição 5, junto da reindexação que a
      limpeza do banco obriga.
- [x] 5.5 Atualizar a **fila** em "Sequência de deploy fixada em 20/09/2026" — a
      única cópia viva. Esta change entra na **posição 5, antes da limpeza do
      banco e do deploy**, empurrando `replicas-de-worker` para 6 e
      `metricas-execucao-coleta` para 7. O motivo fica escrito: a limpeza **custa
      reindexação de todo o conhecimento**, que é exatamente quando o defeito
      medido voltaria a aparecer em todos os documentos grandes de uma vez, e
      agora sem conteúdo anterior para preservar. Nomear as changes, nunca dizer
      "a próxima".
- [x] 5.6 `CHANGELOG.md` (pt-BR), descrevendo o que muda para quem opera: o lote,
      o padrão provisório, a variável nova com default, e que o boot reprova lote
      inválido.

## 6. Fechamento

- [x] 6.1 `apps/workers`: rodar a suíte inteira com `podman ps` em zero. Esperado
      **274/274** e **14 classes** no `WorkerHostCollection` (o arquivo de teste
      novo é unitário puro). Comparar com a baseline de 0.1 **por nome de teste**.
      Se alguma reprovação aparecer fora dos arquivos desta change, conferir o
      load average antes de concluir qualquer coisa — conjunto de falhas instável
      entre rodadas é diagnóstico de contenção, não regressão (convenção 19).
- [x] 6.2 **Conferência manual de escopo** (não é cobertura de teste): abrir
      `Program.cs` e confirmar que `host.ValidateEmbeddingBatchSize()` está
      registrada. Remover a chamada deixa 3.3 verde e a produção sem checagem —
      é a forma da convenção 8 que o `Program.cs` já documenta.
- [x] 6.3 Comparar a projeção do `design.md` com o entregue (convenção 18):
      arquivos **criados e modificados separadamente**, linhas, proporção
      comentário:lógica em produção, número de cenários, e contagem da suíte. O
      fechamento **apenas compara** — sem inventar fator de correção. Escrever o
      resultado em 5.1.
- [x] 6.4 `openspec validate --all` verde.

**A verificação de campo NÃO está nesta seção, e é de propósito.** Reindexar o
`02` contra o gateway real só existe depois do deploy, que está na posição 5 da
fila e ainda não aconteceu — marcá-la junto com o fechamento faria a leitura
futura virar "resolvido em 20/09". Ela é item aberto do `02`, com gatilho e
posição (task 5.4). **Fechar esta change significa "o lote existe e os guardas
passam", nunca "o `502` acabou".**
