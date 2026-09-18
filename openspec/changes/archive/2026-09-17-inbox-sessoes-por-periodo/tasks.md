## 1. Baseline, antes de tocar em código

- [x] 1.1 Rodar a suíte de `apps/inbox` **com a árvore ainda limpa** e registrar
      o número aqui, no formato `N/N`. É a baseline da convenção 19: sem ela,
      qualquer reprovação no fechamento vira discussão sobre se é "pré-existente"
      em vez de comparação número contra número.

      **BASELINE MEDIDA: `164/164`** — `dotnet test` sobre
      `Buteco.Inbox.Tests.csproj`, árvore limpa (só os artefatos desta change e a
      edição de `02-HISTORICO_E_STATUS.md` não versionada em `apps/`), 23 s,
      0 falhas, 0 ignorados, em 2026-09-17.

      **Expectativa declarada antes de medir** (convenção 22, com o estado ao
      lado do número): **165 casos**, contagem **estática** por
      `grep -rho '\[Fact\]' apps/inbox/tests | wc -l` em 2026-09-17, com a árvore
      limpa e zero `[Theory]`/`[InlineData]` no projeto — então atributo conta
      igual a caso executado. Divergência entre o número estático e o executado é
      achado a investigar **antes** de começar, não depois.

      **DIVERGIU EM 1, E A CAUSA FOI INVESTIGADA ANTES DE COMEÇAR.** Não há teste
      ignorado, removido nem sem executar: `ContactEndpointsTests.cs:25` **cita
      `[Fact]` em prosa dentro de um comentário** (*"reutilizado por todos os
      [Fact] desta classe"*), e o `grep` contou a menção como se fosse atributo.
      A contagem correta é `grep -rhE '^\s*\[Fact\]\s*$'` → **164**, idêntica à
      executada.

      **É a convenção 6 na sua forma mais difícil — a fonte foi consultada
      corretamente e a PERGUNTA estava errada.** O `grep` mediu *"quantas vezes a
      string `[Fact]` aparece"*; o que decidia era *"quantos métodos de teste
      existem"*. O número saiu certo para a pergunta errada, e só a medição real
      separou as duas. **A referência a citar daqui em diante é 164**, e a forma
      de contar estaticamente é a do padrão ancorado (`^\s*\[Fact\]\s*$`), nunca
      a substring solta.

      A suíte é de integração com Testcontainers (convenção 5) e precisa do
      runtime de container ativo nesta máquina — sem ele a suíte inteira reprova,
      e isso não é baseline, é ambiente.

## 2. Consulta agregada (`COUNT` no banco)

- [x] 2.1 `Contacts/Responses/SessionPeriodSummaryResponse.cs` — record
      `public sealed record SessionPeriodSummaryResponse(int StartedCount)`.
      Sem `FromEntity`: não há entidade de origem, é agregação (design.md, D4).
- [x] 2.2 `Contacts/Queries/GetSessionPeriodSummary/GetSessionPeriodSummaryQuery.cs`
      — record de query recebendo `DateTimeOffset From, DateTimeOffset To` **já
      parseados**. O parse e a validação ficam no endpoint (D6/D7); a query não
      recebe `string`.
- [x] 2.3 `…/GetSessionPeriodSummaryQueryHandler.cs` — `CountAsync` com
      `StartedAt >= From && StartedAt <= To` sobre `dbContext.Sessions.AsNoTracking()`.
      **Nunca `Select().ToList().Count()`** — sai com o resultado certo e o custo
      errado, e passa despercebido por isso (D1, molde de
      `GetKnowledgeBaseIndexingSummaryQueryHandler`).
- [x] 2.4 `apps/inbox/tests/Buteco.Inbox.Tests/SessionSummaryEndpointsTests.cs` —
      arquivo novo, `IClassFixture<InboxFactoryFixture>`, mais os dois helpers de
      que todo teste desta classe depende:

      - **criação de sessão** pelo caminho real (`IContactSessionResolver`, como
        `ChannelSessionEndpointsTests.cs:96-101`);
      - **retrodatação de `StartedAt`** via `ExecuteSqlInterpolatedAsync`, mesmo
        mecanismo de `ContactSessionResolverTests.cs:57-62`.

      A retrodatação **não é conveniência, é obrigatória**: `Session.StartedAt` é
      `private set`, atribuído no construtor (`Session.cs:31`) — não há como
      escolher o instante pela API.

      **E cada `[Fact]` desta classe usa uma janela própria, distante e disjunta**
      (anos diferentes, bem no passado). A rota conta o sistema **inteiro** e o
      banco é compartilhado por todos os testes da classe: sem janelas disjuntas,
      um teste contaria as sessões de outro. Os demais testes da suíte criam
      sessões em "agora", então nenhuma janela no passado os alcança (design.md,
      Risks).

## 3. Endpoint e mapeamento

- [x] 3.1 `Contacts/Endpoints/SessionSummaryEndpoints.cs` —
      `MapGet("/sessions/summary", …)` com `from`/`to` como **`string?`** na
      assinatura (D6), parse por
      `DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, …)`,
      e `Results<Ok<SessionPeriodSummaryResponse>, ValidationProblem>`.

      Validação acumulada num único `Dictionary<string, string[]>` com chave em
      camelCase e mensagem em pt-BR, como `ChannelEndpoints.cs:135,182-183`: dois
      parâmetros defeituosos saem numa resposta só. A checagem `to < from` roda
      **depois** de os dois parsearem (D7).
- [x] 3.2 `Program.cs` — uma linha, `app.MapSessionSummaryEndpoints()`, entre os
      `Map*` existentes (`:132-137`) e **antes** de
      `ValidateRouteAuthenticationClassification`. Não acrescentar nada à lista
      de rotas anônimas: a rota é autenticada, e se for esquecida o startup
      reprova sozinho (D2).
- [x] 3.3 `[Fact]` do caminho feliz — sessões retrodatadas para dentro da janela,
      resposta `200`, `startedCount` igual à quantidade criada. Asserção sobre o
      **número**, nunca "respondeu corretamente" (convenção 5).
- [x] 3.4 `[Fact]` de que a contagem atravessa canais — sessões de contatos de
      canais diferentes, todas dentro da janela, contadas na mesma resposta.

## 4. Bordas da contagem — um `[Fact]` por caso

Cada caso em `[Fact]` próprio, **nunca como cláusula extra dentro do teste
positivo** (convenção 18): asserção pendurada em teste alheio não reprova
sozinha e não se lê no relatório.

- [x] 4.1 `StartedAt` **exatamente igual a `from`** conta.
- [x] 4.2 `StartedAt` **exatamente igual a `to`** conta.
- [x] 4.3 `StartedAt` anterior a `from` não conta.
- [x] 4.4 `StartedAt` posterior a `to` não conta.
- [x] 4.5 Sessão iniciada **antes** da janela com `LastActivityAt` **dentro**
      dela não conta. É o teste que trava a definição escolhida: sem ele,
      trocar `StartedAt` por `LastActivityAt` no handler deixa a suíte verde
      (design.md, D3).
- [x] 4.6 Sessão iniciada antes da janela e com `ClosedAt` nulo não conta. Trava
      a outra definição descartada (overlap), pelo mesmo motivo de 4.5.
- [x] 4.7 Janela sem nenhuma sessão responde **`200` com `startedCount` = 0** —
      não `404`, não corpo vazio. O zero é contagem **medida**, e a asserção é
      sobre o par (status, valor), não só sobre o valor (convenção 13).

## 5. Validação — um `[Fact]` por caso

- [x] 5.1 `from` ausente → `400`, erro identificando `from`.
- [x] 5.2 `to` ausente → `400`, erro identificando `to`.
- [x] 5.3 Ambos ausentes → `400`, com **os dois** na mesma resposta. É o caso que
      verifica o acumulador do 3.1; sem ele, uma implementação que retorna no
      primeiro defeito passa 5.1 e 5.2 e continua errada.
- [x] 5.4 `from` malformado → `400`, erro identificando `from`.
- [x] 5.5 **A forma do corpo é a mesma** entre "ausente" e "malformado" — o
      teste compara a estrutura das duas respostas, não só os status. É a
      asserção que justifica o binding manual de D6: com `DateTimeOffset?` na
      assinatura este `[Fact]` reprova, e é ele que impede a "simplificação"
      bem-intencionada de voltar ao binding automático.
- [x] 5.6 `to` anterior a `from`, ambos válidos → `400`.
- [x] 5.7 `from` igual a `to` → `200`, contando as sessões iniciadas exatamente
      naquele instante. O par "com item"/"sem item" da convenção 5 aplicado à
      borda degenerada do intervalo.
- [x] 5.8 Limite malformado é reportado como **malformado**, não como intervalo
      invertido — trava a ordem das duas checagens de 3.1.
- [x] 5.9 Limites **sem** deslocamento de fuso dão a mesma contagem que os mesmos
      instantes **com** `+00:00` explícito. É o teste do `AssumeUniversal`: sem
      ele, o valor nu recebe o offset local do processo e o `Npgsql` recusa o
      `DateTimeOffset` resultante — **500, não número errado** (ver D6, corrigida
      na implementação). Verificado por mutação: é o único `[Fact]` que reprova
      quando os dois estilos saem.
- [x] 5.10 **(não estava no plano — descoberta na implementação)** Limites com
      deslocamento **explícito não-UTC** são normalizados antes da comparação.
      O requisito de spec já exigia isso e **nenhum `[Fact]` cobria o caminho**:
      os demais mandam `"O"` (que já sai `+00:00`) ou valor nu. Sem este teste,
      `AdjustToUniversal` era linha sem guarda (convenção 15) — e a mutação
      confirma: removendo só ele, este é o único teste que reprova.

      Cobre o Scenario *"Limite com deslocamento de fuso explícito não-UTC é
      normalizado para UTC"*, acrescentado à spec **depois** da implementação:
      o requisito já exigia a normalização em prosa, mas sem cenário próprio a
      `spec.md` deixava de listar o que de fato está testado.

## 6. Fechamento

- [x] 6.1 Rodar a suíte de `apps/inbox` e comparar com a baseline de 1.1,
      **número contra número**. Esperado: baseline **+ 19** — `[Fact]` de 3.3 e
      3.4 (2), 4.1-4.7 (7) e 5.1-5.10 (10); 2.4 é arquivo e helpers, não
      acrescenta caso. Se a conta não fechar, ou faltou teste ou sobrou, e os
      dois são achado.

      **MEDIDO: `183/183`**, 0 falhas, 0 ignorados, 17 s. Fecha contra a baseline
      de 1.1: `164 + 19 = 183`.

      **O `+ 19` corrige o `+ 18` escrito na proposta**, e a diferença é a tarefa
      5.10, que não existia no plano. Recalibrar a conta é tarefa de quem muda o
      estado (convenção 22), não descoberta da próxima leitura.
- [x] 6.2 Confirmar que **nenhuma migração foi gerada**: `AppDbContextModelSnapshot.cs`
      inalterado e nada novo em `Infrastructure/Migrations/`. Esta change não
      toca o modelo (D9), e uma migração vazia aparecendo aqui é sinal de que
      alguém acrescentou o índice que a decisão deixou fora.

      **CONFIRMADO:** `AppDbContextModelSnapshot.cs` inalterado (`git diff` vazio)
      e nada novo em `Infrastructure/Migrations/`.
- [x] 6.3 `git diff --stat` conferido contra a árvore do `design.md`: nada em
      `apps/api`, `apps/workers` ou `apps/frontend`. O escopo é declarado e
      verificável, não uma intenção.

      **CONFIRMADO:** o diff de código toca `apps/inbox` e mais nada —
      `Program.cs` (+1 linha) e quatro arquivos novos. Zero em `apps/api`,
      `apps/workers`, `apps/frontend`.
- [x] 6.4 `openspec validate --all` e `python3 scripts/check-docs.py`. Registrar
      a saída **como o que ela é**: nenhum dos dois enxerga semântica de rota,
      forma de corpo de erro nem definição de intervalo — verde aqui não é
      cobertura de nada que esta change decidiu.

      **RODADOS:** `openspec validate --all` → 51/51 (50 specs + esta change);
      `check-docs.py` → "Integridade da documentação: OK". **Nenhum dos dois
      enxerga** semântica de rota, forma de corpo de erro, definição de intervalo
      nem fuso — verde aqui não cobre nada que esta change decidiu. O que cobre
      são os 19 `[Fact]`, e as cinco mutações que os viram vermelhos.
- [x] 6.5 `CHANGELOG.md` — entrada escrita **depois** do código e conferida
      contra ele, e afirmando o que a rota faz (*"conta sessões iniciadas no
      período"*), nunca *"sessões no período"* sem qualificar. Precedente: o
      `CHANGELOG` da etapa 4 de `knowledge-base-indexacao` nasceu falso, escrito
      pela própria change que tornou a frase falsa (convenção 13).

      **ESCRITO** no bloco "Caixas de entrada e canais", depois do código e
      conferido contra ele: diz "sessões **iniciadas** no período", com os
      limites obrigatórios e inclusivos — não "sessões no período" sem qualificar.
- [x] 6.6 `02-HISTORICO_E_STATUS.md` — registrar a rota nova e **os dois gatilhos
      pendurados** (teto de intervalo e índice em `StartedAt`, que se recalibram
      juntos no primeiro deploy com volume real, D8/D9). Sem isso os dois viram
      esquecimento em vez de decisão, que é exatamente o que a convenção 22 pede
      para evitar.

      O achado do `ClosedAt` **já foi registrado** naquele arquivo antes desta
      change, no item "Encerramento explícito de sessão" — não repetir aqui, só
      referenciar se for útil.


      **REGISTRADO** em "Abertos por `inbox-sessoes-por-periodo` (2026-09-17)":
      a rota, os dois gatilhos recalibrando juntos, a armadilha de fuso medida
      (com o erro do Npgsql verbatim) e a janela de teste em fatia de ano.