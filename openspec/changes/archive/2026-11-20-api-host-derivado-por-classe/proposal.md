**Issue:** #303

## Why

`KnowledgeBaseCatalogTests` é a classe que faz o caminho crítico da suíte do `apps/api`. Ela é `partial` de propósito, para não subir mais um contêiner, e por isso roda os 106 casos em série. Na parte `SyncedCreation`, 38 dos 41 casos derivam um host novo (`WithWebHostBuilder`) **cada um**, para falar com um connectors falso.

Medido em 20/11/2026 na baseline (`ef3e9f1`, com a #302, 6 threads), numa rodada aprovada da suíte (462 s):

- **a janela de `KnowledgeBaseCatalogTests`: 198 s**, terminando junto com a suíte. É mais que o dobro da segunda maior (`KnowledgeDocumentCatalogTests`, 91 s);
- **os 41 casos da `SyncedCreation` somaram 181 s**, 92% da janela. São ~4,4 s por caso, quase tudo boot de host.

Com um host por classe só na `SyncedCreation` (prova de conceito, sobre `c9733ce`), a `KnowledgeBaseCatalogTests` caiu de 84–89 s para **22 s** sozinha, com 106/106 verde. Os números, o estado da máquina e o que é estimado estão no design.

`KnowledgeDocumentCatalogTests` também deriva hosts por caso. O diagnóstico da D0 mostrou que, com 6 threads, isso pesa 25–30 s fora do caminho crítico, e a parte dessa classe saiu da change por decisão do dono (design, D0).

É a segunda change de barateamento da suíte, depois da #302 (paralelismo).

## What Changes

- **`apps/api` (só testes), `KnowledgeBaseCatalogTests.SyncedCreation`:** o host derivado com o connectors falso passa a ser **um por classe e por valor de `clientTimeout`**. Hoje são dois: o padrão e o de 300 ms. Ele é reaproveitado pelos casos, que rodam em série.
- **Diagnóstico primeiro (design, D0), feito:** o caso de `KnowledgeDocumentCatalogTests` com 5 hosts, medido por iteração. O host é 75–83% do caso, mas com 6 threads o caso leva 24 s e a classe 76–91 s. **A parte dessa classe saiu da change** (decisão do dono, 20/11/2026), e ela fica como candidata nos itens em aberto do `02`.
- **Reposição de estado entre casos:**
  - `FakeConnectorsHttpMessageHandler.Reset()` (requisições, `Respond`, barreira);
  - `LogEventCounter.Reset()`.
- **Estado em voo reprova, em duas linhas.** No fim do caso que deixou uma requisição ao connectors falso em andamento (descarte do `ConnectorsHost`). E, se escapar, no reset do caso seguinte, com o nome do caso anterior na mensagem.
- **Ficam como estão, por desenho** (design, D3):
  - `WithDispatchHost`: cada caso liga a varredura com agendamento próprio;
  - a segunda instância de `IndexingDispatch`: ela é o que o caso prova;
  - os dois hosts derivados de `KnowledgeBaseCatalogTests.Deletion`.
- **Nenhum caso sai, muda de nome ou de asserção. Nenhum arquivo de produção muda.**

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `api-scaffold`: um requisito novo da própria suíte, no padrão que o dono escolheu para a #302, **escrito como condição**. **Quando** casos de uma classe compartilham um host derivado, o estado de teste mutável é reposto antes de cada caso, e estado de outro caso em andamento reprova (no fim do caso que o deixou e, se escapar, no seguinte, com o nome do anterior). O requisito não exige compartilhar: a D3 mantém host por caso em três lugares.

Nenhum comportamento do sistema muda. O requisito existe porque o `openspec validate` exige delta, e é o critério de aceite da issue.

## Impact

- `apps/api/tests/Buteco.Api.Tests/Knowledge/KnowledgeBaseCatalogTests.SyncedCreation.cs`. O ponto de chamada de `WithConnectors` em `KnowledgeBaseCatalogTests.Deletion.cs` passa a usar o host compartilhado sem mudar de forma.
- `apps/api/tests/Buteco.Api.Tests/Support/FakeConnectorsHttpMessageHandler.cs` (reset e contagem em voo) e `Knowledge/KnowledgeSyncTestSeed.cs` (`LogEventCounter.Reset`).
- `openspec/specs/api-scaffold/spec.md`: o requisito novo, pelo sync do archive.
- **Relação com a #302:** entra depois dela. A #302 já está no `main` (`ef3e9f1`): a baseline e o fechamento rodam com 6 threads, e o registro por rodada que a #302 introduz vale aqui.
