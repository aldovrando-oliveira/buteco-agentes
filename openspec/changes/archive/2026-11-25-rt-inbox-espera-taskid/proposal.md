**Issue:** #309 (Refs #176)

## Why

Doze casos do `tests/InboxOrchestratorRoundTrip.Tests` (o `rt-inbox`) esperam o `taskId` no registro de despacho do `apps/inbox` (`PendingDispatch`), consultando a cada 100 ms. O `taskId` só existe nesse registro entre a gravação pelo inbox e o fechamento pelo push. Com o push livre e o LLM mockado sem atraso, essa janela mediu 46 a 140 ms. Quando a task termina dentro de um único intervalo de consulta, o teste nunca vê o `taskId` e estoura em 10 s, mesmo com a resposta já entregue.

É o mecanismo que a #176 descreveu para o `Source3` em 18/10/2026. Ele foi medido em 24/11/2026, durante a #296, sobre `4e44dd3`:

- a espera da sentinela consultando a cada 5 ms passou nas 4 rodadas válidas;
- a `main` com 4 e 8 ms de atraso no `SendMessage` derrubou os casos expostos em 14 de 16 tentativas. Essas tentativas são indício, não medida pela regra assimétrica: a CPU ficou abaixo de 100 em todas.

Qualquer change que mexa no tempo do `SendMessage` herda a intermitência, como a #296 herdou.

## What Changes

- **`tests/InboxOrchestratorRoundTrip.Tests` (só testes):** nenhum caso espera mais o `taskId` no registro de despacho.
  - **Casos 1, 2 e 4–11** (design, D1–D3): cada um espera primeiro uma condição **positiva e durável** e só depois afirma a ausência do registro. A condição é a task do agente do próprio caso em estado final em `a2a_tasks`, ou, na sentinela, a resposta entregue. O `taskId` é lido de `a2a_tasks`, pelo agente do caso, com `Assert.Single` e a lista das tasks na mensagem (D2).
  - **Casos 3 e 12** (design, D4): precisam do `taskId` antes do estado final. Leem-no de `a2a_tasks` pelo agente do caso, consultando até a linha aparecer. Ela nasce no `Submit` e não some.
  - **A sentinela do `Source3` (caso 4)** deixa de esperar o `taskId`. Ganha uma asserção: a resposta da sentinela foi entregue. Assim continua provando que ela passou pelo worker.
- **Guardas** (design, D5):
  - **G1:** com o atraso de 8 ms no `SendMessage`, os 16 casos passam com a correção e os expostos caem sem ela. Se o atraso não der reprovação válida, entra a mutação determinística do plano B;
  - **G2:** tirar a condição positiva reprova;
  - **G3:** com uma segunda task provocada pela reintentativa de transporte, todos os casos que leem task reprovam com a lista.
- **Nenhum caso sai nem muda de nome.** Nenhuma asserção sai nem afrouxa. A sentinela ganha uma.
- **Nenhum arquivo de produção muda**: nada em `apps/*/src`, nem em `libs/`, nem no compose.
- **#176 não fecha neste PR.** Em 19/11/2026 a baseline reprovou o `Source3` com erro de canal do RabbitMQ ao reiniciar o worker, que pode ser outra causa. O design (D6) propõe o critério para fechá-la depois.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `inbox-message-orchestration`: um requisito novo, da própria suíte de round-trip. O caso que acompanha um despacho até o fim espera uma condição durável antes de afirmar o fim. Ele nunca condiciona o progresso a um dado que existe só entre duas transições do despacho, como o `taskId` no registro.

Nenhum comportamento do sistema muda. O requisito existe porque o `openspec validate` exige delta, e é o critério de aceite da #309.

## Impact

- `tests/InboxOrchestratorRoundTrip.Tests/`:
  - `OrphanDispatchRoundTripTests.cs`, casos 1–7;
  - `RoundTripTests.cs`, casos 8–11;
  - `SessionSerializationRoundTripTests.cs`, caso 12;
  - possivelmente um helper novo em `Support/`, para ler `a2a_tasks` pelo agente (design, árvore).
- **Intocados:**
  - `Support/RoundTripFixture.cs`: o N = 1 continua, e a sentinela continua dependendo dele;
  - `ServiceTokenRoundTripTests.cs`, `IssuerTokenRoundTripTests.cs` e `RoleRoundTripTests.cs`.
- `openspec/specs/inbox-message-orchestration/spec.md`: o requisito novo, pelo sync do archive.
- **Relação com a #296:** a #296 espera este PR na `main` e é levada à base nova depois. As suítes de fechamento dela rodam de novo sobre ele.
