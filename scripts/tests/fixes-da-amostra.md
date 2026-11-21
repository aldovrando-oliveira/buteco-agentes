# Os 13 fixes da amostra do estudo de testes rápidos

A amostra da seção 9.1 do estudo de suíte mais rápida (19–20/11/2026, branch descartável fora do `main`, sobre
`c9733ce`). O critério foi fixado antes de olhar os resultados: commits `fix:` que tocam código de produção,
isoláveis por reversão em 3 vias. Cada um foi reintroduzido sobre o código da época, e a coluna "Quem pegou" é o
teste que reprovou com o defeito de volta.

O `test_selecionar_testes.py` calcula a seleção a partir do **diff de produção** de cada fix: o diff do commit
restrito a `apps/*/src/**` (sem `*.test.*` nem `src/test/`) e a `apps/frontend/deploy/**`, o mesmo critério do
estudo para "reintroduzir". Com o diff inteiro, o teste que o próprio fix acrescentou já selecionaria o app, e o
caso passaria por construção. É conferência estática: lê o `git`, não roda suíte.

Na coluna "Passo", o id do passo que tem de estar no plano. Num passo filtrado, o filtro tem de alcançar a classe.
Num `fe-related`, o comando tem de levar o arquivo do fix; que o `related` chega ao teste, o Vitest diz (tarefa 2.6
da change, registrada no design).

| Commit | App | Quem pegou | Passo | Classe ou arquivo |
|---|---|---|---|---|
| `be6d1f2` | inbox | enum de `Message` como inteiro | `Buteco.Inbox.Tests` | `MessagePersistenceTests` |
| `a91f0c9` | inbox | sessão duplicada sob concorrência | `Buteco.Inbox.Tests` | `ContactSessionResolverTests` |
| `2631309` | workers | encoder do `PushNotificationConfigCodec` | `Buteco.Workers.Tests` | `PushNotificationEndToEndTests` |
| `bfa1ec0` | workers | encoder do `ConversationSessionCodec` | `CrossAppTaskStoreCompatibility.Tests` | `PostgresTaskStoreCompatibilityTests` |
| `4729380` | api | desempate nas listas ordenadas | `Buteco.Api.Tests` | `AgentEndpointsTests` |
| `868d291` | workers (+ api e inbox) | compactação com Gemini | `Buteco.Workers.Tests` | `LlmCallDurationChatClientTests` |
| `aae444d` | api | série diária sem o 0 | `Buteco.Api.Tests` | `InsightsEndpointsTests` |
| `bfb114b` | frontend | moldura da lacuna declarada | `fe-related` | `src/features/insights/components/DeclaredGap.test.tsx` |
| `39c43d5` | frontend | falha some da coluna "Falhas" | `fe-related` | `src/features/insights/components/AgentConsumptionCard.test.tsx` |
| `6635db2` | frontend | busca e filtro na primeira carga | `fe-related` | `src/features/applications/pages/ApplicationsListPage.test.tsx` |
| `b4fcf5d` | frontend (`nginx.conf`) | retorno do Google não chega ao api | `Buteco.Api.Tests[Nginx]` | `NginxRouteCoverageTests` |
| `7fd8e75` | frontend | descrição longa esconde colunas | `fe-related` | `src/features/agents/components/AgentTable.test.tsx` |
| `b7333d9` | api | conectar sem provedor volta ao login | `Buteco.Api.Tests` | `IdentityIssuerStartupValidationTests` |
