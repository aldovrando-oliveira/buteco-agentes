## Context

Na suíte do `apps/api`, cada classe com `IClassFixture<ApiFactoryFixture>` recebe uma fixture própria: Postgres do Testcontainers, `MigrateAsync` e boot do host. Quando um caso precisa de outra composição (outro endereço do connectors, um interceptor do EF Core), ele **deriva** um host da fixture por `WithWebHostBuilder`: mesmo banco, composição de serviços própria, e um boot completo do `Program` de novo.

Duas classes fazem isso a cada caso, e elas são `partial` de propósito: "uma classe nova subiria mais um contêiner" (`KnowledgeBaseCatalogTests.Synced.cs`, D12 da `catalogo-base-sincronizada`). Como o xUnit roda os casos de uma classe em série, o custo dos hosts derivados se soma. Com 6 threads (#302), só uma delas, `KnowledgeBaseCatalogTests`, ainda faz o caminho crítico da suíte; `KnowledgeDocumentCatalogTests` saiu desta change na D0.

**Fontes lidas** (20/11/2026; `c9733ce` na proposta, conferidas de novo no `ef3e9f1` no apply), em `apps/api/tests/Buteco.Api.Tests/`:

- `Knowledge/KnowledgeBaseCatalogTests.SyncedCreation.cs`: `WithConnectors`, l. 36–75, e os 16 pontos de chamada (19 métodos, 41 casos com as linhas de `Theory`);
- `Knowledge/KnowledgeBaseCatalogTests.Deletion.cs`: um 17º ponto de chamada de `WithConnectors`, l. 208, que passa a usar o host compartilhado sem mudar de forma; e os dois hosts próprios, l. 241 e 329 (D3);
- `Knowledge/KnowledgeDocumentCatalogTests.BaseDeletion.cs`: o caso medido na D0, l. 66–134;
- `Knowledge/KnowledgeDocumentCatalogTests.IndexingDispatch.cs`: `WithDispatchHost`, l. 400–435, e a segunda instância, l. 160;
- `Support/FakeConnectorsHttpMessageHandler.cs`, `Knowledge/KnowledgeSyncTestSeed.cs` (`LogEventCounter`) e `Support/ApiFactoryFixture.cs`;
- a prova de conceito do estudo de suíte mais rápida (commit `452b6f9`, numa branch descartável fora do `main`). Ela é referência; não entra por cherry-pick.

### Medições que justificam a change

**Estado da máquina** (20/11/2026, baseline `ef3e9f1`, com a #302):

- macOS, 12 núcleos, 16 GiB; VM do Podman com 6 CPUs e 6 GiB;
- `postgres` e `rabbitmq` do compose de pé, WAHA parado;
- uma suíte por vez pelo `suites.lock`;
- espera do `CPU_Speed_Limit` = 100 e do load < 8 antes de cada passo, CPU amostrada a cada 5 s;
- **6 threads**, lidas do `.trx` de cada rodada.

Veredito pela regra assimétrica (#181).

**[M] O custo dentro da suíte, com 6 threads** (`~/.cache/buteco-agents/303/`, `janelas.txt` de cada rodada):

| | Baseline, 0.3 | Diagnóstico, 0.5 |
|---|---|---|
| suíte do `apps/api` | 462 s, CPU mín. 41, **aprovada**, 1860/1861 | 416 s, CPU mín. 44, inconclusiva, 1859/1861 |
| janela de `KnowledgeBaseCatalogTests` (106 casos, em série) | **197,8 s**, de +257 s até o fim da suíte (+455 s) | 190,5 s, de +76 s a +267 s |
| soma dos 41 casos da `SyncedCreation` | **181,1 s** (92% da janela) | 169,5 s |
| janela de `KnowledgeDocumentCatalogTests` (121 casos) | 90,6 s | 76,0 s |
| 3ª a 5ª maiores janelas | `ExecutionMetricsMigrationTests` 55,2 s; `PasswordResetLinkCommandTests` 45,9 s; `IdentityKeyRingGateTests` 43,0 s | `PasswordResetLinkCommandTests` 53,3 s; `ExecutionMetricsMigrationTests` 49,2 s; `IdentityKeyRingGateTests` 33,8 s |

`KnowledgeBaseCatalogTests` é a maior janela nas duas rodadas, mais que o dobro da segunda. Na rodada válida ela termina junto com a suíte: é o caminho crítico. A rodada da 0.5 tinha a instrumentação temporária num caso de `KnowledgeDocumentCatalogTests` e reprovou fora do escopo; entra aqui só como segunda leitura.

**[M] O custo de um host derivado, com 6 threads:**

- dentro da suíte, `SyncedCreation` gasta 181 s em 41 casos, 38 deles com host derivado: ≈ **4,4 s por caso**, quase tudo no boot do host derivado (na D0, o boot de um host derivado dentro da suíte foi de 3,5–4,5 s);
- sozinho, sem contenção, o boot de um host derivado levou 1,0–1,5 s (D0).

**[M] A prova de conceito**, só na `SyncedCreation`, com um host por classe e por `clientTimeout`, e o reset do handler e do contador. A classe **sozinha** não depende do paralelismo da suíte (19–20/11/2026, sobre `c9733ce`):

| `KnowledgeBaseCatalogTests` sozinha | Host por caso (hoje) | Host por classe |
|---|---|---|
| rodada 1 | 88,6 s (CPU mín. 97), 106/106 | **22,2 s** (100), 106/106 |
| rodada 2 | 84,4 s (100), 106/106 | **22,0 s** (100), 106/106 |

As rodadas da suíte inteira da prova de conceito foram com 12 threads e não valem como referência depois da #302.

**[M] Convenção 15 na prova de conceito**, com a classe sozinha e o defeito reintroduzido em produção. Com host por caso e com host por classe, a classe reprovou **os mesmos casos**:

- **d9**, o código de fio `folder-in-use` trocado em `KnowledgeBaseEndpoints`: 3 casos;
- **d10**, sem a checagem de pasta em uso antes da chamada ao connectors, em `CreateKnowledgeBaseCommandHandler`: 4 casos, entre eles `CreateSynced_FolderAlreadyUsed_Returns409EvenWithConnectorsDown`, que depende do `Respond` e da contagem de requisições do handler, ou seja, do estado que o reset zera.

**O que é estimativa e esta change mede:**

- **[E] o ganho na suíte com 6 threads.** Com 2 hosts no lugar de 41, a janela de `KnowledgeBaseCatalogTests` deve cair na mesma ordem que a classe sozinha caiu na prova de conceito (−66 s). Como ela é o caminho crítico, a suíte deve encurtar, até o ponto em que outra classe passe a ser a maior janela. A 4.1 e a 4.2 medem.

## Goals / Non-Goals

**Goals:**

- Pagar o boot de cada composição de host derivado **uma vez por classe** na `KnowledgeBaseCatalogTests.SyncedCreation`. `KnowledgeDocumentCatalogTests` saiu na D0.
- Manter o que cada caso prova: nenhum caso sai, muda de nome ou de asserção.
- Tornar visível, em vez de silencioso, qualquer estado de um caso que chegue ao seguinte pelo host compartilhado.
- Refazer, com o host compartilhado, a prova dos guardas que dependem desse host (convenção 15).

**Non-Goals:**

- Código de produção.
- `WithDispatchHost`, a segunda instância de `IndexingDispatch` e os hosts de `KnowledgeBaseCatalogTests.Deletion` (D3).
- `KnowledgeDocumentCatalogTests` (D0).
- Postgres compartilhado, paralelismo (é a #302), camada rápida, `Trait`, script de seleção, `apps/workers` e `apps/frontend`.
- Desmembrar as classes `partial`. Com host por classe, o caminho crítico encolhe sem mudar o arranjo.

## Decisions

### D0. Diagnóstico primeiro: onde estão os 125 s, e o ponto de decisão (decisão do dono, 20/11/2026)

**Antes de qualquer código**, a tarefa 0.5 mede o caso `SyncWrites_WhenTheBaseIsDeletedBetweenReadAndSave_Return404` **por iteração**, no worktree da baseline, separando:

- **o tempo de derivar o host**, do `WithFailingDocumentWrites` até o host responder. O host derivado do `WebApplicationFactory` só compõe e sobe no primeiro uso, então o boot é medido em volta do primeiro `CreateClient`;
- **o tempo do corpo**: semear a base, o `UpsertOkAsync` prévio, a escrita com o interceptor (que exclui a base por HTTP de dentro do `SaveChanges`), e as contagens;
- **o descarte do host**, no fim do `await using`.

A medição usa uma instrumentação **temporária** no worktree da baseline (`Stopwatch` e `Console.WriteLine`, no molde da linha `[janela]` que o caso já imprime). Ela é desfeita depois, conferida por `git status` vazio. Roda de duas formas, porque a contenção pode mudar a resposta: o caso sozinho, e a classe inteira dentro da suíte.

**Ponto de decisão, com critério escrito antes de medir:**

- **o host é o custo dominante** se a soma dos boots e descartes dos 5 hosts for **pelo menos metade** do tempo do caso, nas duas formas. Aí a parte de `KnowledgeDocumentCatalogTests` segue nesta change: o interceptor trocável, a concessão, d11, d12 e d13;
- **se não for**, essa parte **sai desta change** antes de ir adiante. As tarefas marcadas **[KDoc]** viram "não se aplica", o design ganha a seção "O que o apply mudou", e o achado (onde estão os ~85 s) volta ao dono antes do resto do apply. A `SyncedCreation` segue de qualquer forma: lá o ganho já está medido.

**Uma hipótese, para não ser lida como achado:** o interceptor exclui a base por uma requisição HTTP **de dentro** de uma gravação em andamento. Uma espera de lock entre as duas transações explicaria tempo que não é host. A D0 mede; não presume.

**Resultado da 0.5** (20/11/2026, baseline `ef3e9f1`, 6 threads lidas do `.trx`; instrumentação temporária, desfeita; saídas em `~/.cache/buteco-agents/303/diagnostico/`).

O caso sozinho, duas rodadas (as duas aprovadas, CPU mínima 100). Os valores são da rodada 1 / rodada 2:

| Iteração | Boot | Semear | Upsert prévio | Escrita (exclusão no `SaveChanges`) | Contagens | Descarte |
|---|---|---|---|---|---|---|
| 1, inclusão | 1,24 / 1,09 s | 114 / 90 ms | — | 581 / 417 ms (517 / 372 ms) | 43 / 40 ms | 29 / 27 ms |
| 2, atualização | 1,10 / 0,96 s | 3 / 3 ms | 163 / 182 ms | 212 / 170 ms (29 / 26 ms) | 4 / 4 ms | 5 / 2 ms |
| 3, sem mudança | 1,01 / 1,02 s | 3 / 2 ms | 21 / 16 ms | 302 / 223 ms (13 / 10 ms) | 7 / 4 ms | 3 / 2 ms |
| 4, exclusão por referência | 1,24 / 0,98 s | 4 / 2 ms | 22 / 18 ms | 289 / 190 ms (12 / 7 ms) | 4 / 4 ms | 2 / 2 ms |
| 5, resultado de ciclo | 1,47 / 1,20 s | 2 / 2 ms | — | 252 / 274 ms (6 / 6 ms) | 5 / 4 ms | 6 / 3 ms |
| **caso** | **8,1 / 6,9 s** | | | | | |

Boots + descartes: **6,1 s de 8,1 s (75%)** e **5,3 s de 6,9 s (76%)**.

Dentro da suíte, uma rodada (inconclusiva: 1 reprovado fora do escopo, CPU mínima 44; o caso aprovou):

| Iteração | Boot | Escrita (exclusão no `SaveChanges`) | Upsert prévio | Semear, contagens, descarte |
|---|---|---|---|---|
| 1 | 3,47 s | 728 ms (13 ms) | — | 2–5 ms cada |
| 2 | 3,66 s | 724 ms (10 ms) | 17 ms | 2–6 ms cada |
| 3 | 4,52 s | 940 ms (11 ms) | 21 ms | 3–5 ms cada |
| 4 | 4,04 s | 667 ms (19 ms) | 15 ms | 2–5 ms cada |
| 5 | 4,01 s | 810 ms (8 ms) | — | 2–5 ms cada |
| **caso** | **23,7 s** | | | |

Boots + descartes: **19,7 s de 23,7 s (83%)**. A janela de `KnowledgeDocumentCatalogTests` na mesma rodada: **76 s** (121 casos).

**Leitura:**

- pelo critério, o host **é** o custo dominante, nas duas formas;
- a hipótese não se confirmou. A exclusão de dentro do `SaveChanges` levou 6–30 ms; só na primeira iteração do caso sozinho, 370–520 ms, que é aquecimento;
- os 125 s e os ~85 s não atribuídos não se reproduziram com 6 threads. Eram contenção do paralelismo de 12, que a #302 tirou.

**Decisão do dono (20/11/2026), apesar do critério:** a parte de `KnowledgeDocumentCatalogTests` **sai desta change**. Com 6 threads o caso leva 24 s e a janela da classe 76 s. O ganho restante (7 hosts a menos × 3,5–4,5 s ≈ 25–30 s) fica fora do caminho crítico e não paga o interceptor trocável, a concessão e os três guardas (d11, d12, d13). A classe fica como candidata nos itens em aberto do `02`, com estes números.

### D1. O cache de host derivado mora na `ApiFactoryFixture`, por chave

A fixture ganha um método que devolve um host derivado **compartilhado** para uma chave: compõe na primeira chamada e devolve o mesmo nas seguintes. Os hosts compartilhados são **descartados explicitamente no `DisposeAsync` da fixture**, antes do banco.

**Por que na fixture:**

- a fixture vive exatamente o tempo da classe (uma instância por classe, no xUnit 2);
- as duas classes usam o mesmo mecanismo;
- o descarte fica escrito, sem depender de detalhe de implementação do `WebApplicationFactory`.

**Alternativas:**

- **`ConditionalWeakTable` estática por fixture, como na prova de conceito:** descartada. Funcionou, mas o descarte vinha do `WebApplicationFactory` de origem, que guarda e descarta os derivados. É detalhe interno, que a base não precisa assumir.
- **Uma segunda `IClassFixture` só para o host derivado:** descartada. No xUnit 2 uma fixture de classe não recebe outra fixture no construtor. E mudar o construtor primário de uma classe `partial` mexe no arquivo principal de cada uma.

### D2. O que muda em cada classe

**`KnowledgeBaseCatalogTests.SyncedCreation`**:

- `WithConnectors(clientTimeout)` pede o host compartilhado pela chave `connectors:<clientTimeout>`. São duas composições: o padrão e o de 300 ms.
- O `FakeConnectorsHttpMessageHandler` e o `LogEventCounter` passam a ser **do host**, e são repostos a cada pedido de `WithConnectors`.
- O `HttpClient` continua **por caso**, criado do host compartilhado, com o token do painel.
- O `ConnectorsHost` deixa de descartar o host. Ele descarta só o cliente.

**`KnowledgeDocumentCatalogTests`** fica como está: saiu na D0, por decisão do dono. `WithFailingDocumentWrites` continua derivando um host por chamada.

### D3. O que fica por caso, e por quê

- **`WithDispatchHost`** (5 casos de `IndexingDispatch`): cada caso compõe o host com um `KnowledgeIndexingRequestSchedule` próprio. Isso **liga o `BackgroundService` da varredura** com o intervalo do caso. Compartilhar o host deixaria a varredura de um caso rodando durante o seguinte, com o agendamento errado. A independência entre esses casos é parte do que eles provam.
- **A segunda instância** (`factory.WithWebHostBuilder(_ => { })`, l. 160): o caso prova duas instâncias varrendo ao mesmo tempo. O host separado é o objeto do teste.
- **`KnowledgeBaseCatalogTests.Deletion`** (l. 241 e 329): dois hosts, fora do pedido desta change e fora do caminho crítico medido (os 9 casos de `Deletion` somaram 25–41 s nas rodadas do estudo). Fica anotado como candidato, sem issue: o ganho estimado é pequeno.

### D4. O reset recusa estado em voo

O risco é uma requisição ao connectors falso, iniciada num caso, chegar ao handler depois do reset e contar no caso seguinte. Em vez de confiar na ordem, o reset **reprova**:

- **O handler falso conta as requisições em andamento** (`Interlocked`, num `try/finally` em volta do `SendAsync`), e guarda **o nome do caso** que pediu o host. `WithConnectors` recebe o nome por `[CallerMemberName]`, que é o método do caso, porque o xUnit 2 não expõe o caso corrente.
- **Primeira linha, no fim do caso que vazou.** O descarte do `ConnectorsHost` (o fim do `await using` do caso) confere a contagem. Se houver requisição em andamento, lança `InvalidOperationException` ("o caso <nome> terminou com N requisição(ões) ao connectors falso em andamento"), e **a reprovação cai no caso que vazou**.
  - Se o caso já tinha reprovado por outra asserção, a exceção do descarte pode encobri-la. É aceitável: a reprovação continua no caso certo, e o vazamento também é defeito do caso.
- **Segunda linha, no caso seguinte.** O `Reset()` faz a mesma conferência, para o que escapar da primeira, por exemplo um caso que não descarta o `ConnectorsHost`. A mensagem cita **o nome do caso anterior**: "requisição ao connectors falso ainda em andamento, deixada pelo caso <nome anterior>".
- **Sem corrida no caso legítimo.** O único caso com requisição lenta (`CreateSynced_ConnectorsSlow_Is503UnavailableAndNever500`) tem `Respond` que honra o cancelamento (`Task.Delay(5 s, cancellationToken)`). O `HttpClient` do api cancela em 300 ms, e o handler em processo termina, decrementando a contagem, **antes** de a chamada do api devolver a resposta ao caso. No descarte, a contagem já é zero. Requisição em voo de verdade é a que o api dispara sem esperar, e é ela que as duas linhas acusam.
- **O caso de 300 ms tem host próprio** (a chave inclui o `clientTimeout`). O `Respond` lento dele, que pode continuar depois do cancelamento do cliente, não cai no host dos outros casos. Ele é o único caso com essa chave.

**Alternativa:** esperar as requisições em voo terminarem antes de repor. Descartada, porque esconderia o vazamento em vez de mostrá-lo.

### D5. Convenção 15: os guardas refeitos com o host compartilhado

Os casos dessas classes foram provados com host por caso, e o ambiente deles muda. Cada defeito abaixo é reintroduzido **no worktree da baseline** (host por caso) e **no da change** (host por classe), com a classe sozinha. As duas listas de reprovação têm de ser iguais:

| Defeito | Produção | Casos que guardam | Host |
|---|---|---|---|
| **d9** | `KnowledgeBaseEndpoints`: o código `folder-in-use` trocado | 3 de `KnowledgeBaseCatalogTests` (2 em `SyncedCreation`) | connectors, padrão |
| **d10** | `CreateKnowledgeBaseCommandHandler`: sem a checagem de pasta em uso antes da chamada ao connectors | 4 de `SyncedCreation` | connectors, padrão (depende do `Respond` e da contagem) |

d11, d12 e d13, os guardas de `KnowledgeDocumentCatalogTests`, saíram com a parte dessa classe (D0): o ambiente daqueles casos não muda.

Mais **o guarda do próprio mecanismo** (D4), que prova **as duas linhas**:

1. com um caso alterado de propósito para deixar uma requisição em andamento, **esse caso** reprova no descarte, com a mensagem da primeira linha;
2. com a mesma alteração e a conferência do descarte desligada por mutação, **o caso seguinte** reprova no `Reset()`, com a mensagem que cita o nome do caso alterado.

As duas alterações são desfeitas pela cópia.

### D6. Um requisito da suíte no `api-scaffold`

O `openspec validate` exige delta, com e sem `--strict` (medido na #302). Pelo padrão que o dono escolheu na #302, o requisito vai sem mecanismo nem número:

- **escrito como condição**, porque a D3 mantém hosts por caso em três lugares: **quando** casos de uma classe compartilham um host derivado, o estado de teste mutável é reposto antes de cada caso, e estado de outro caso em andamento reprova;
- o requisito não exige compartilhar: classe que não compartilha não está sujeita a ele;
- as duas linhas da D4 são cenários dele (decisão do dono, 20/11/2026).

### D7. Medição

Com o paralelismo que estiver no `main` (D8):

- **a classe sozinha**, `KnowledgeBaseCatalogTests`, duas rodadas no worktree da baseline e duas no da change, intercaladas;
- **a suíte inteira**, uma rodada na baseline e duas na change, intercaladas (mesma estrutura da #302).

Cada rodada com a linha das threads, o veredito, a duração e a CPU mínima, conforme o registro por rodada que a #302 introduz.

### D8. Relação com a #302

Esta change entra **depois** da #302. O worktree da baseline é destacado no `origin/main` do início do apply. Se a #302 já tiver entrado, as duas pontas rodam com 6 threads, e a comparação mede só esta change. Se não tiver entrado, rodam com o padrão, e o registro diz qual foi.

## Árvore de arquivos

```
apps/api/tests/Buteco.Api.Tests/
├── Support/
│   ├── ApiFactoryFixture.cs                         (muda: o cache de host derivado por chave, D1)
│   └── FakeConnectorsHttpMessageHandler.cs          (muda: Reset e contagem em voo, D4)
└── Knowledge/
    ├── KnowledgeSyncTestSeed.cs                     (muda: LogEventCounter.Reset)
    └── KnowledgeBaseCatalogTests.SyncedCreation.cs  (muda: WithConnectors, D2)
openspec/changes/api-host-derivado-por-classe/
├── proposal.md
├── design.md
├── tasks.md
└── specs/api-scaffold/spec.md                       (requisito novo, D6)
```

Nenhum arquivo em `apps/api/src`, `libs/`, `tests/` nem nos outros apps.

## Risks / Trade-offs

- **[Estado vazando entre casos pelo host compartilhado: a família de `AgentDeactivationTests`, já registrada no `02`]**
  - **O que muda e o que não muda:** os casos dessas classes **já** compartilham o banco e o `IndexingPublisher` da fixture; esta change não muda isso. O que passa a ser compartilhado são os singletons do host derivado.
  - **Os que os casos mutam** (o handler falso e o contador) são repostos a cada caso, com recusa em voo (D4).
  - **Para os demais**, a tarefa 3 roda cada classe duas vezes inteira e, isolado, cada caso que usa o host compartilhado, e confere o mesmo resultado. É a forma de separar "ordem" de "recurso" que o `02` registra para essa família.
- **[Requisição em voo chegando depois do reset]** → as duas linhas da D4 (no fim do caso que vazou e no reset do seguinte, com o nome do anterior), o host próprio para o caso de 300 ms, e o guarda do mecanismo, que prova as duas (D5).
- **[O host não ser o custo dominante em `KnowledgeDocumentCatalogTests`]** → medido na D0: era (75–83%), mas com 6 threads o ganho ficou pequeno, e a parte saiu por decisão do dono.
- **[Um guarda passar a aprovar com o defeito por causa do host compartilhado]** → D5, nas duas pontas. Lista diferente é parada.

## Migration Plan

Não há migração nem deploy: é código de teste. Para voltar atrás, basta reverter o commit.

## Open Questions

Nenhuma de produto.

## O que o apply mudou

Convenção 9: o que o apply achou e o design não previa, ou previa diferente.

- **A D0 deu "o host é o custo dominante", e a parte de `KnowledgeDocumentCatalogTests` saiu mesmo assim, por decisão do dono.** O diagnóstico, as tabelas e o motivo estão na D0. Saíram o interceptor trocável, a concessão, d11, d12, d13 e a medição dessa classe; a classe ficou como candidata nos itens em aberto do `02`.
- **Os 125 s e os ~85 s não atribuídos eram do paralelismo de 12.** Com 6 threads o caso levou 23,7 s dentro da suíte e 7–8 s sozinho, e a hipótese da espera de lock não se confirmou (a exclusão de dentro do `SaveChanges` leva 6–30 ms). As medições do Context foram refeitas com 6 threads.
- **Os pontos de chamada eram 17, não 18, e um deles está fora da `SyncedCreation`.** São 16 em `SyncedCreation`, em 16 dos seus 19 métodos (38 casos), e 1 em `KnowledgeBaseCatalogTests.Deletion.cs:208` (`DeleteSyncedBase_FreesTheFolderForANewBase`). Esse caso passa ao host compartilhado sem mudar de forma, pela mesma `WithConnectors`, e é um dos 3 que o d9 reprova nas duas pontas. Os 3 métodos restantes de `SyncedCreation` usam o host da fixture. A lista da 3.2 tem 39 casos.
- **O cache da D1 guarda a composição, não só o host.** `ApiFactoryFixture.SharedDerivedHost<T>(chave, compor)` devolve um `IAsyncDisposable` do tipo do chamador. Em `SyncedCreation`, ele é o host com o handler falso e o contador que a composição capturou: o caso precisa alcançá-los, e eles nascem junto com o host.
- **O guarda 2.5 teve uma primeira tentativa descartada.** Com o vazamento disparado antes das asserções, o caso alterado reprovava por uma asserção própria na linha 2. Refeito com o vazamento depois da última asserção. A primeira linha também mostrou que, enquanto a requisição vazada dura, todo caso seguinte que pede o host reprova no `Reset`, citando o caso que vazou. É o comportamento da segunda linha, e não esconde o caso de origem.
- **A estimativa do Context foi medida.** A classe sozinha caiu de 71–73 s para 10 s, e a janela dela dentro da suíte de 182–198 s para 27–28 s. A suíte foi de 462–464 s para 372–411 s, com 6 threads, todas as rodadas aprovadas e os mesmos 1861 nomes. **`KnowledgeBaseCatalogTests` saiu do caminho crítico:** a maior janela passou a ser `KnowledgeDocumentCatalogTests` (78–91 s), a classe que a D0 tirou desta change. O item dela no `02` tem como gatilho a janela passar de um terço da suíte (decisão do dono, 20/11/2026), que com 78–91 s em 372–411 s não está atingido.
- **Limitação da primeira linha: a exceção do descarte encobre a asserção.** Quando o caso que vazou também reprova por asserção, o xUnit mostra só a `InvalidOperationException` do descarte do `ConnectorsHost`, que é lançada depois. A primeira tentativa do guarda 2.5 mostrou isso: o caso reprovava pela própria asserção, e a mensagem era a do descarte. A reprovação continua no caso certo (D4), mas a causa primeira fica escondida. Por decisão do dono (20/11/2026), a mensagem diz que pode ter encoberto uma asserção, e manda rodar o caso isolado para vê-la.
